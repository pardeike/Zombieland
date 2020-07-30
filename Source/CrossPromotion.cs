using HarmonyLib;
using RimWorld;
using Steamworks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using UnityEngine;
using Verse;
using Verse.Steam;

namespace CrossPromotionModule
{
	[StaticConstructorOnStartup]
	static class CrossPromotion
	{
		const string _crosspromotion = "brrainz-crosspromotion";
		internal const ulong userID = 76561197973010050;

		internal static List<SteamUGCDetails_t> promotionMods = new List<SteamUGCDetails_t>();
		internal static Dictionary<ulong, bool?> allVoteStati = new Dictionary<ulong, bool?>();
		internal static Dictionary<ulong, Texture2D> previewTextures = new Dictionary<ulong, Texture2D>();
		internal static List<ulong> subscribingMods = new List<ulong>();
		internal static ulong? lastPresentedMod = null;

		static CrossPromotion()
		{
			if (Harmony.HasAnyPatches(_crosspromotion))
				return;

			var instance = new Harmony(_crosspromotion);

			_ = instance.Patch(
				SymbolExtensions.GetMethodInfo(() => ModLister.RebuildModList()),
				postfix: new HarmonyMethod(SymbolExtensions.GetMethodInfo(() => ModLister_RebuildModList_Postfix()))
			);

			_ = instance.Patch(
				AccessTools.DeclaredMethod(typeof(Page_ModsConfig), nameof(Page_ModsConfig.PostClose)),
				postfix: new HarmonyMethod(SymbolExtensions.GetMethodInfo(() => Page_ModsConfig_PostClose_Postfix()))
			);

			_ = instance.Patch(
				AccessTools.DeclaredMethod(typeof(WorkshopItems), "Notify_Subscribed"),
				postfix: new HarmonyMethod(SymbolExtensions.GetMethodInfo(() => WorkshopItems_Notify_Subscribed_Postfix(new PublishedFileId_t(0))))
			);

			_ = instance.Patch(
				AccessTools.DeclaredMethod(typeof(Page_ModsConfig), nameof(Page_ModsConfig.DoWindowContents)),
				transpiler: new HarmonyMethod(SymbolExtensions.GetMethodInfo(() => Page_ModsConfig_DoWindowContents_Transpiler(null, null)))
			);
		}

		static void ModLister_RebuildModList_Postfix()
		{
			_ = ModPreviewPath(0);
			new Thread(() => { FetchPromotionMods(); }).Start();
		}

		static void Page_ModsConfig_PostClose_Postfix()
		{
			subscribingMods.Clear();
		}

		static void WorkshopItems_Notify_Subscribed_Postfix(PublishedFileId_t pfid)
		{
			var longID = pfid.m_PublishedFileId;
			if (subscribingMods.Contains(longID) == false)
				return;
			_ = subscribingMods.Remove(longID);

			LongEventHandler.ExecuteWhenFinished(() =>
			{
				var mod = ModLister.AllInstalledMods.FirstOrDefault(meta => meta.GetPublishedFileId().m_PublishedFileId == longID);
				if (mod == null)
					return;

				ModsConfig.SetActive(mod, true);
				ModsConfig.Save();
				Find.WindowStack.Add(new MiniDialog(mod.Name + " added"));
			});
		}

		static readonly MethodInfo m_BeginGroup = SymbolExtensions.GetMethodInfo(() => GUI.BeginGroup(new Rect()));
		static readonly MethodInfo m_EndGroup = SymbolExtensions.GetMethodInfo(() => GUI.EndGroup());
		static readonly MethodInfo m_Promotion = SymbolExtensions.GetMethodInfo(() => PromotionLayout.Promotion(new Rect(), null));

		static IEnumerable<CodeInstruction> Page_ModsConfig_DoWindowContents_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
		{
			var list = instructions.ToList();
			var beginGroupIndicies = list
				.Select((instr, idx) => new Pair<int, CodeInstruction>(idx, instr))
				.Where(pair => pair.Second.operand is MethodInfo mi && mi == m_BeginGroup)
				.Select(pair => pair.First).ToArray();
			var endGroupIndicies = list
				.Select((instr, idx) => new Pair<int, CodeInstruction>(idx, instr))
				.Where(pair => pair.Second.operand is MethodInfo mi && mi == m_EndGroup)
				.Select(pair => pair.First).ToArray();
			if (beginGroupIndicies.Length != 2 || endGroupIndicies.Length != 2)
				return instructions;
			var iBegin = beginGroupIndicies[1] - 1;
			var iEnd = endGroupIndicies[0];

			var jump = generator.DefineLabel();
			list[iEnd + 1].labels.Add(jump);
			var localPositionVar = list[iBegin];
			list.InsertRange(iBegin, new[]
			{
					localPositionVar.Clone(),
					new CodeInstruction(OpCodes.Ldarg_0),
					new CodeInstruction(OpCodes.Call, m_Promotion),
					new CodeInstruction(OpCodes.Brtrue, jump)
				});

			return list.AsEnumerable();
		}

		internal static string ModPreviewPath(ulong modID)
		{
			var dir = Path.GetTempPath() + "BrrainzMods" + Path.DirectorySeparatorChar;
			if (Directory.Exists(dir) == false)
				_ = Directory.CreateDirectory(dir);
			return dir + modID + "-preview.jpg";
		}

		internal static byte[] SafeRead(string path)
		{
			for (var i = 1; i <= 5; i++)
			{
				try
				{
					return File.ReadAllBytes(path);
				}
				catch (Exception)
				{
					Thread.Sleep(250);
				}
			}
			return null;
		}

		internal static Texture2D PreviewForMod(ulong modID)
		{
			if (previewTextures.TryGetValue(modID, out var texture))
				return texture;
			var path = ModPreviewPath(modID);
			if (File.Exists(path) == false)
				return null;
			texture = new Texture2D(1, 1, TextureFormat.ARGB32, false);
			if (texture.LoadImage(SafeRead(path)))
				previewTextures[modID] = texture;
			return texture;
		}

		internal static void UpdateVotingStatus(ulong modID, Action<GetUserItemVoteResult_t, bool> callback)
		{
			var callDelegate = new CallResult<GetUserItemVoteResult_t>.APIDispatchDelegate(callback);
			var call = SteamUGC.GetUserItemVote(new PublishedFileId_t(modID));
			var resultHandle = CallResult<GetUserItemVoteResult_t>.Create(callDelegate);
			resultHandle.Set(call, null);
		}

		static void AsyncUserModsQuery(UGCQueryHandle_t query, Action<SteamUGCQueryCompleted_t, bool> callback)
		{
			var callDelegate = new CallResult<SteamUGCQueryCompleted_t>.APIDispatchDelegate((result, failure) =>
			{
				callback(result, failure);
				_ = SteamUGC.ReleaseQueryUGCRequest(query);
			});
			var call = SteamUGC.SendQueryUGCRequest(query);
			var resultHandle = CallResult<SteamUGCQueryCompleted_t>.Create(callDelegate);
			resultHandle.Set(call, null);
		}

		static void AsyncDownloadQuery(UGCHandle_t content, string path, Action<RemoteStorageDownloadUGCResult_t, bool> callback)
		{
			var callDelegate = new CallResult<RemoteStorageDownloadUGCResult_t>.APIDispatchDelegate(callback);
			var call = SteamRemoteStorage.UGCDownloadToLocation(content, path, 0);
			var resultHandle = CallResult<RemoteStorageDownloadUGCResult_t>.Create(callDelegate);
			resultHandle.Set(call, null);
		}

		public static void FetchPromotionMods()
		{
			if (SteamManager.Initialized == false)
				return;

			var rimworldID = SteamUtils.GetAppID();
			unchecked
			{
				var aID = new AccountID_t((uint)userID);
				var itemQuery = SteamUGC.CreateQueryUserUGCRequest(aID,
				EUserUGCList.k_EUserUGCList_Published, EUGCMatchingUGCType.k_EUGCMatchingUGCType_UsableInGame,
				EUserUGCListSortOrder.k_EUserUGCListSortOrder_VoteScoreDesc, rimworldID, rimworldID,
				1);
				_ = SteamUGC.SetReturnLongDescription(itemQuery, true);
				_ = SteamUGC.SetRankedByTrendDays(itemQuery, 7);
				AsyncUserModsQuery(itemQuery, (result, failure) =>
				{
					for (uint i = 0; i < result.m_unNumResultsReturned; i++)
						if (SteamUGC.GetQueryUGCResult(result.m_handle, i, out var mod))
							if (promotionMods.Any(m => m.m_nPublishedFileId.m_PublishedFileId == mod.m_nPublishedFileId.m_PublishedFileId) == false)
							{
								promotionMods.Add(mod);
								var modID = mod.m_nPublishedFileId.m_PublishedFileId;

								var path = ModPreviewPath(modID);
								if (File.Exists(path) == false || new FileInfo(path).Length != mod.m_nPreviewFileSize)
								{
									AsyncDownloadQuery(mod.m_hPreviewFile, path, (result2, failure2) =>
									{
										if (File.Exists(path))
										{
											if (previewTextures.ContainsKey(modID))
												_ = previewTextures.Remove(modID);
										}
									});
								}

								UpdateVotingStatus(modID, (result2, failure2) =>
								{
									allVoteStati[modID] = (result2.m_eResult == EResult.k_EResultOK) ? result2.m_bVotedUp : (bool?)null;
								});
							}
				});
			}
		}
	}

	[StaticConstructorOnStartup]
	internal class PromotionLayout
	{
		static readonly AccessTools.FieldRef<WorkshopItemHook, CSteamID> ref_steamAuthor = AccessTools.FieldRefAccess<WorkshopItemHook, CSteamID>("steamAuthor");
		internal static bool Promotion(Rect mainRect, Page_ModsConfig page)
		{
			if (SteamManager.Initialized == false)
				return false;

			var mod = page.selectedMod;
			if (mod == null
				|| ref_steamAuthor(mod.GetWorkshopItemHook()).m_SteamID != CrossPromotion.userID
				|| CrossPromotion.promotionMods.Count == 0)
				return false;

			var leftColumn = mainRect.width * 2 / 3;
			var rightColumn = mainRect.width - leftColumn - 10f;

			GUI.BeginGroup(mainRect);
			try
			{
				ContentPart(mainRect, leftColumn, mod, page);
				PromotionPart(mainRect, leftColumn, rightColumn, mod, page);
			}
			catch
			{
				GUI.EndGroup();
				return false;
			}
			GUI.EndGroup();
			return true;
		}

		static Vector2 leftScroll = Vector2.zero;
		static Vector2 rightScroll = Vector2.zero;

		static void ContentPart(Rect mainRect, float leftColumn, ModMetaData mod, Page_ModsConfig page)
		{
			var workshopMods = WorkshopItems.AllSubscribedItems.Select(wi => wi.PublishedFileId.m_PublishedFileId).ToList();

			var mainModID = mod.GetPublishedFileId().m_PublishedFileId;
			var promoMods = CrossPromotion.promotionMods.ToArray();
			var thisMod = promoMods.FirstOrDefault(m => m.m_nPublishedFileId.m_PublishedFileId == mainModID);
			var isLocalFile = ModLister.AllInstalledMods.Any(meta => meta.GetPublishedFileId().m_PublishedFileId == mainModID && meta.Source == ContentSource.ModsFolder);
			var isSubbed = workshopMods.Contains(mainModID);

			if (CrossPromotion.lastPresentedMod != mainModID)
			{
				leftScroll = Vector2.zero;
				rightScroll = Vector2.zero;
				CrossPromotion.lastPresentedMod = mainModID;

				new Thread(() =>
				{
					foreach (var promoMod in promoMods)
						CrossPromotion.UpdateVotingStatus(promoMod.m_nPublishedFileId.m_PublishedFileId, (result2, failure2) =>
						{
							CrossPromotion.allVoteStati[promoMod.m_nPublishedFileId.m_PublishedFileId] = (result2.m_eResult == EResult.k_EResultOK) ? result2.m_bVotedUp : (bool?)null;
						});
				}).Start();
			}

			var description = thisMod.m_rgchDescription;
			if (description == null || description.Length == 0)
				description = mod.Description;

			var outRect = new Rect(0f, 0f, leftColumn, mainRect.height);
			var width = outRect.width - 20f;
			var imageRect = new Rect(0f, 0f, width, width * mod.PreviewImage.height / mod.PreviewImage.width);
			var textRect = new Rect(0f, 24f + 10f + imageRect.height, width, Text.CalcHeight(description, width));
			var innerRect = new Rect(0f, 0f, width, imageRect.height + 20f + 8f + 10f + textRect.height);
			Widgets.BeginScrollView(outRect, ref leftScroll, innerRect, true);
			GUI.DrawTexture(imageRect, mod.PreviewImage, ScaleMode.ScaleToFit);
			var widgetRow = new WidgetRow(imageRect.xMax, imageRect.yMax + 8f, UIDirection.LeftThenDown, width, 8f);
			if (isLocalFile == false)
			{
				if (widgetRow.ButtonText("Unsubscribe".Translate(), null, true, true))
				{
					Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("ConfirmUnsubscribe".Translate(mod.Name), delegate
					{
						mod.enabled = false;
						new Thread(() =>
						{
							_ = AccessTools.Method(typeof(Workshop), "Unsubscribe").Invoke(null, new object[] { mod });
							_ = AccessTools.Method(typeof(Page_ModsConfig), "Notify_SteamItemUnsubscribed").Invoke(page, new object[] { mainModID });
						}).Start();
					}, true, null));
				}
			}
			if (isSubbed)
			{
				if (widgetRow.ButtonText("WorkshopPage".Translate(), null, true, true))
					SteamUtility.OpenWorkshopPage(new PublishedFileId_t(mainModID));
			}
			if (Prefs.DevMode && mod.CanToUploadToWorkshop())
			{
				widgetRow = new WidgetRow(imageRect.xMin, imageRect.yMax + 8f, UIDirection.RightThenDown, width, 8f);
				if (widgetRow.ButtonText("Upload", null, true, true))
					Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("ConfirmSteamWorkshopUpload".Translate(), delegate
					{
						_ = AccessTools.Method(typeof(Workshop), "Upload").Invoke(null, new object[] { mod });
					}, true, null));
			}

			Widgets.Label(textRect, description);
			Widgets.EndScrollView();
		}

		static void PromotionPart(Rect mainRect, float leftColumn, float rightColumn, ModMetaData mod, Page_ModsConfig page)
		{
			var mainModID = mod.GetPublishedFileId();

			Text.Font = GameFont.Tiny;
			var headerHeight = 30f;
			var headerRect = new Rect(leftColumn + 10f, -4f, rightColumn - 20f, headerHeight);
			Text.Anchor = TextAnchor.UpperCenter;
			Widgets.Label(headerRect, "Mods of " + mod.Author.Replace("Andreas Pardeike", "Brrainz") + ":".Truncate(headerRect.width, null));
			Text.Anchor = TextAnchor.UpperLeft;

			var outRect = new Rect(leftColumn + 10f, headerHeight - 4f, rightColumn, mainRect.height - (headerHeight - 4f));
			var width = outRect.width - 20f;
			var previewHeight = width * 319f / 588f;
			var promoMods = CrossPromotion.promotionMods.ToArray().Where(m => m.m_nPublishedFileId != mainModID);
			var workshopMods = WorkshopItems.AllSubscribedItems.Select(wi => wi.PublishedFileId.m_PublishedFileId).ToList();
			var activeMods = ModLister.AllInstalledMods.Where(meta => meta.Active).Select(meta => meta.GetPublishedFileId().m_PublishedFileId).ToList();

			var height = 0f;
			foreach (var promoMod in promoMods)
			{
				var myModID = promoMod.m_nPublishedFileId.m_PublishedFileId;
				var isLocalFile = ModLister.AllInstalledMods.Any(meta => meta.GetPublishedFileId().m_PublishedFileId == myModID && meta.Source == ContentSource.ModsFolder);
				var isSubbed = workshopMods.Contains(myModID);
				_ = CrossPromotion.allVoteStati.TryGetValue(myModID, out var voteStatus);

				if (height > 0)
					height += 10f;
				var preview = CrossPromotion.PreviewForMod(promoMod.m_nPublishedFileId.m_PublishedFileId);
				if (preview != null)
				{
					height += width * preview.height / preview.width + 2f;
					if (isLocalFile == false && (isSubbed == false || (voteStatus == false)))
						height += 16f;
				}
			}

			Widgets.BeginScrollView(outRect, ref rightScroll, new Rect(0f, 0f, width, height), true);
			var firstTime = true;
			var modRect = new Rect(0f, 0f, width, 0f);
			foreach (var promoMod in promoMods)
			{
				var myModID = promoMod.m_nPublishedFileId.m_PublishedFileId;
				var isLocalFile = ModLister.AllInstalledMods.Any(meta => meta.GetPublishedFileId().m_PublishedFileId == myModID && meta.Source == ContentSource.ModsFolder);
				var isSubbed = workshopMods.Contains(myModID);
				var isActive = activeMods.Contains(myModID);
				_ = CrossPromotion.allVoteStati.TryGetValue(myModID, out var voteStatus);

				if (firstTime == false)
					modRect.y += 10f;

				var preview = CrossPromotion.PreviewForMod(promoMod.m_nPublishedFileId.m_PublishedFileId);
				if (preview != null)
				{
					modRect.height = width * preview.height / preview.width;
					GUI.DrawTexture(modRect, preview, ScaleMode.ScaleToFit);

					var checkRect = modRect;
					checkRect.xMax -= 4f;
					checkRect.yMax -= 4f;
					checkRect.xMin = checkRect.xMax - 18f;
					checkRect.yMin = checkRect.yMax - 18f;
					var active = isActive;
					GUI.DrawTexture(checkRect.ContractedBy(-2f), CheckboxBackground);
					Widgets.Checkbox(checkRect.xMin, checkRect.yMin, ref active, checkRect.width);
					if (active != isActive)
					{
						var clickedMod = ModLister.AllInstalledMods.FirstOrDefault(meta => meta.GetPublishedFileId().m_PublishedFileId == myModID);
						if (clickedMod != null)
						{
							ModsConfig.SetActive(clickedMod, active);
							ModsConfig.Save();
						}
					}

					if (Mouse.IsOver(checkRect) == false)
					{
						Widgets.DrawHighlightIfMouseover(modRect);
						if (Widgets.ButtonInvisible(modRect, true))
						{
							var description = promoMod.m_rgchTitle + "\n\n" + promoMod.m_rgchDescription;
							var actionButton = isSubbed || isLocalFile ? "Select" : "Subscribe";
							void actionButtonAction()
							{
								if (isSubbed || isLocalFile)
								{
									var orderedMods = (IEnumerable<ModMetaData>)AccessTools.Method(typeof(Page_ModsConfig), "ModsInListOrder").Invoke(page, Array.Empty<object>());
									page.selectedMod = orderedMods.FirstOrDefault(meta => meta.GetPublishedFileId().m_PublishedFileId == myModID);
									var modsBefore = orderedMods.ToList().FindIndex(m => m == page.selectedMod);
									if (modsBefore >= 0)
										_ = Traverse.Create(page).Field("modListScrollPosition").SetValue(new Vector2(0f, modsBefore * 26f + 4f));
								}
								else
									new Thread(() =>
									{
										CrossPromotion.subscribingMods.Add(myModID);
										_ = SteamUGC.SubscribeItem(new PublishedFileId_t(myModID));
									}).Start();
							}
							var infoWindow = new Dialog_MessageBox(description, "Close".Translate(), null, actionButton, actionButtonAction, null, false, null, null);
							Find.WindowStack.Add(infoWindow);
						}
					}
					modRect.y += modRect.height + 2f;

					modRect.height = 0f;
					if (isLocalFile == false)
					{
						if (isSubbed == false)
						{
							modRect.height = 16f;
							if (CrossPromotion.subscribingMods.Contains(myModID))
								Widgets.Label(modRect, WaitingString);
							else if (Widgets.ButtonText(modRect, "Subscribe", false, true, true))
								new Thread(() =>
								{
									CrossPromotion.subscribingMods.Add(myModID);
									_ = SteamUGC.SubscribeItem(new PublishedFileId_t(myModID));
								}).Start();
						}
						else if (voteStatus != null && voteStatus == false)
						{
							modRect.height = 16f;
							if (Widgets.ButtonText(modRect, "Like", false, true, true))
							{
								new Thread(() =>
								{
									CrossPromotion.allVoteStati[myModID] = true;
									_ = SteamUGC.SetUserItemVote(new PublishedFileId_t(myModID), true);
								}).Start();
							}
						}
					}
					modRect.y += modRect.height;
				}

				firstTime = false;
			}
			Widgets.EndScrollView();
		}

		static Texture2D _checkboxBackground;
		static Texture2D CheckboxBackground
		{
			get
			{
				if (_checkboxBackground == null)
					_checkboxBackground = SolidColorMaterials.NewSolidColorTexture(new Color(0f, 0f, 0f, 0.5f));
				return _checkboxBackground;
			}
		}

		static string WaitingString
		{
			get
			{
				var i = (DateTime.Now.Ticks / 20) % 4;
				return new string[] { "....", "... .", ".. ..", ". ..." }[i];
			}
		}
	}

	internal class MiniDialog : Dialog_MessageBox
	{
		internal MiniDialog(string text, string buttonAText = null, Action buttonAAction = null, string buttonBText = null, Action buttonBAction = null, string title = null, bool buttonADestructive = false, Action acceptAction = null, Action cancelAction = null)
			: base(text, buttonAText, buttonAAction, buttonBText, buttonBAction, title, buttonADestructive, acceptAction, cancelAction) { }

		public override Vector2 InitialSize => new Vector2(320, 240);
	}
}