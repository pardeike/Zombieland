using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public static class ZombieAreaManager
	{
		public static Map lastMap = null;
		public static Dictionary<Pawn, Area> pawnsInDanger = new();
		public static bool warningShowing = false;
		public static IEnumerator stateUpdater = StateUpdater();

		public static bool IsZombielandPawn(Pawn pawn)
		{
			return pawn is Zombie || pawn is ZombieSymbiant || pawn is ZombieSpitter;
		}

		static Pawn[] AllZombies(Map map)
		{
			try
			{
				return map.mapPawns.AllPawnsSpawned
					.Where(IsZombielandPawn)
					.ToArray();
			}
			catch (Exception)
			{
				return Array.Empty<Pawn>();
			}
		}

		static bool IsInDangerArea(Pawn pawn, Area area, AreaRiskMode mode)
		{
			var inside = area.innerGrid[pawn.Position];
			return inside && mode == AreaRiskMode.ColonistInside
				|| inside == false && mode == AreaRiskMode.ColonistOutside
				|| inside && mode == AreaRiskMode.ZombieInside
				|| inside == false && mode == AreaRiskMode.ZombieOutside;
		}

		static IEnumerator StateUpdater()
		{
			while (true)
			{
				yield return null;
				var map = Find.CurrentMap;
				if (map == null)
					continue;

				if (lastMap != map)
				{
					lastMap = map;
					pawnsInDanger.Clear();
				}

				var areas = ZombieSettings.Values.dangerousAreas.Where(pair => pair.Key.Map == map && pair.Value != AreaRiskMode.Ignore).ToArray();
				if (areas.Length == 0)
				{
					pawnsInDanger.Clear();
					continue;
				}

				var pawns = map.mapPawns.FreeColonistsSpawned.Where(pawn => pawn.InfectionState() < InfectionState.Infecting).ToArray();
				yield return null;
				for (int pIdx = 0; pIdx < pawns.Length; pIdx++)
				{
					var pawn = pawns[pIdx];
					var found = false;
					if (pawn.Spawned && pawn.Map == map)
					{
						for (var aIdx = 0; aIdx < areas.Length; aIdx++)
						{
							var (area, mode) = (areas[aIdx].Key, areas[aIdx].Value);
							if ((mode == AreaRiskMode.ColonistInside || mode == AreaRiskMode.ColonistOutside) && IsInDangerArea(pawn, area, mode))
							{
								if (pawnsInDanger.ContainsKey(pawn) == false)
									pawnsInDanger.Add(pawn, area);
								found = true;
							}
							yield return null;
						}
					}
					if (found == false)
						_ = pawnsInDanger.Remove(pawn);
				}

				if (map == null)
					continue;

				var zombies = AllZombies(map);
				yield return null;

				for (int zIdx = 0; zIdx < zombies.Length; zIdx++)
				{
					var zombie = zombies[zIdx];
					var found = false;
					if (zombie.Spawned)
					{
						for (var aIdx = 0; aIdx < areas.Length; aIdx++)
						{
							var (area, mode) = (areas[aIdx].Key, areas[aIdx].Value);
							if ((mode == AreaRiskMode.ZombieInside || mode == AreaRiskMode.ZombieOutside) && IsInDangerArea(zombie, area, mode))
							{
								if (pawnsInDanger.ContainsKey(zombie) == false)
									pawnsInDanger.Add(zombie, area);
								found = true;
							}
							yield return null;
						}
					}
					if (found == false)
						_ = pawnsInDanger.Remove(zombie);
				}
			}
		}

		public static void DangerAlertsOnGUI()
		{
			var map = Find.CurrentMap;
			if (map == null)
			{
				warningShowing = false;
				return;
			}

			try
			{
				_ = stateUpdater.MoveNext();
			}
			catch (Exception ex)
			{
				Log.Error($"ZombieAreaManager threw an exception in the state updater: {ex}");
				stateUpdater = StateUpdater();
			}

			if (WorldRendererUtility.WorldRendered == false)
				DrawDangerous();
			else
				warningShowing = false;
		}

		public static void ShowCentered(IntVec3 minCell, IntVec3 maxCell)
		{
			var center = new IntVec3((minCell.x + maxCell.x) / 2, 0, (minCell.z + maxCell.z) / 2);
			CameraJumper.TryJump(new GlobalTargetInfo(center, Find.CurrentMap));
		}

		static readonly Dictionary<Color, Texture2D> areaColorTextures = new();
		static readonly CountingCache<Pawn, Texture2D> pawnHeadTextures = new(120);

		public static void DrawDangerous()
		{
			Area foundArea = null;
			Texture2D colorTexture = null;
			var headsToDraw = new List<(Pawn, Texture)>();
			var highlightDangerousAreas = ZombieSettings.Values.highlightDangerousAreas;
			foreach (var (pawn, area) in pawnsInDanger)
			{
				if (foundArea != null && foundArea != area)
					break;
				if (foundArea == null)
				{
					var c = area.Color;
					if (areaColorTextures.TryGetValue(c, out colorTexture) == false)
					{
						colorTexture = SolidColorMaterials.NewSolidColorTexture(c.r, c.g, c.b, 0.75f);
						areaColorTextures[c] = colorTexture;
					}
					Graphics.DrawTexture(new Rect(0, 0, UI.screenWidth, 2), colorTexture);

					if (highlightDangerousAreas)
						area.MarkForDraw();
				}
				foundArea = area;

				if (IsZombielandPawn(pawn) == false)
				{
					var texture = pawnHeadTextures.Get(pawn, p =>
					{
						var renderTexture = RenderTexture.GetTemporary(44, 44, 32, RenderTextureFormat.ARGB32);
						Find.PawnCacheRenderer.RenderPawn(pawn, renderTexture, new Vector3(0, 0, 0.4f), 1.75f, 0f, Rot4.South);
						var tex = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.ARGB32, false) { name = "DangerousInfoPawn" };
						RenderTexture.active = renderTexture;
						tex.ReadPixels(new Rect(0f, 0f, renderTexture.width, renderTexture.height), 0, 0);
						tex.Apply();
						RenderTexture.active = null;
						RenderTexture.ReleaseTemporary(renderTexture);
						return tex;
					}, UnityEngine.Object.Destroy);
					headsToDraw.Add((pawn, texture));
				}
				else
					headsToDraw.Add((pawn, null));
			}

			warningShowing = colorTexture != null;
			if (warningShowing)
			{
				var zombiesInArea = headsToDraw.Where(pair => IsZombielandPawn(pair.Item1)).Select(pair => pair.Item1).ToArray();

				var n = headsToDraw.Where(pair => pair.Item2 != null).Count();
				if (zombiesInArea.Length > 0)
					n++;
				var width = 5 + n * 2 + (n + 1) * 18 + 5;
				var rect = new Rect(118, 2, width, 29);
				Graphics.DrawTexture(rect, colorTexture);
				var showPositions = Mouse.IsOver(rect.ExpandedBy(4));

				rect = new Rect(123, 7, 18, 18);
				Graphics.DrawTexture(rect, Constants.Danger);

				var pos = 0;
				if (zombiesInArea.Length > 0)
				{
					rect = new Rect(141 + pos++ * 22, 5, 22, 22);
					Graphics.DrawTexture(rect, Constants.zoneZombie);
					if (Widgets.ButtonInvisible(rect))
					{
						var minX = 100000;
						var minZ = 100000;
						var maxX = -100000;
						var maxZ = -100000;
						zombiesInArea.Select(z => z.Position).Do(p =>
						{
							minX = Mathf.Min(minX, p.x);
							minZ = Mathf.Min(minZ, p.z);
							maxX = Mathf.Max(maxX, p.x);
							maxZ = Mathf.Max(maxZ, p.z);
						});
						ShowCentered(new IntVec3(minX, 0, minZ), new IntVec3(maxX, 0, maxZ));
					}
					if (showPositions)
						zombiesInArea.Do(zombie => TargetHighlighter.Highlight(new GlobalTargetInfo(zombie), true, false, false));
				}
				for (var i = 0; i < n; i++)
				{
					var (pawn, texture) = headsToDraw[i];
					if (texture != null)
					{
						rect = new Rect(141 + pos++ * 22, 5, 22, 22);
						Graphics.DrawTexture(rect, texture);
						if (Widgets.ButtonInvisible(rect))
							ShowCentered(pawn.Position, pawn.Position);
					}
					if (showPositions)
						TargetHighlighter.Highlight(new GlobalTargetInfo(pawn), true, false, false);
				}
			}
		}
	}

	[HarmonyPatch(typeof(AreaManager))]
	public static class AreaManager_Patches
	{
		[HarmonyPrefix]
		[HarmonyPatch(nameof(AreaManager.Remove))]
		public static void RemovePrefix(Area area, out bool __state)
		{
			__state = area?.Mutable == true;
		}

		[HarmonyPostfix]
		[HarmonyPatch(nameof(AreaManager.Remove))]
		public static void RemovePostfix(Area area, bool __state)
		{
			if (__state)
				Dialog_ManageAreas_Patches.ClearZombieRisk(area);
		}

		[HarmonyPrefix]
		[HarmonyPatch(nameof(AreaManager.SortAreas))]
		public static bool SortAreas() => false;
	}

	[HarmonyPatch(typeof(Dialog_ManageAreas))]
	public static class Dialog_ManageAreas_Patches
	{
		const float VanillaAreaRowHeight = 24f;
		const float VanillaAreaRowGap = 6f;
		public const float VanillaAreaRowPitch = VanillaAreaRowHeight + VanillaAreaRowGap;
		public const float VanillaWidgetRowGap = 4f;
		const float VanillaAreaColorIconWidth = 24f;
		const float VanillaAreaColorLabelGap = 4f;
		const float VanillaAreaLabelInternalGap = 4f;
		const float VanillaAreaLabelWidth = 160f;
		const float VanillaAreaTextButtonWidth = 76f;
		const float VanillaAreaTextButtonCount = 3f;
		const float VanillaAreaIconButtonWidth = 24f;
		const float VanillaAreaIconButtonCount = 3f;
		public const float VanillaAreaRowTrashRight = VanillaAreaColorIconWidth
			+ VanillaWidgetRowGap
			+ VanillaAreaColorLabelGap
			+ VanillaAreaLabelInternalGap
			+ VanillaAreaLabelWidth
			+ VanillaAreaTextButtonCount * (VanillaAreaTextButtonWidth + VanillaWidgetRowGap)
			+ VanillaAreaIconButtonCount * VanillaAreaIconButtonWidth;
		public const float ZombieRiskButtonLabelWidth = 150f;
		public const float ZombieRiskButtonHorizontalPadding = 32f;
		public const float ZombieRiskButtonWidth = ZombieRiskButtonLabelWidth + ZombieRiskButtonHorizontalPadding;
		public const float ZombieRiskButtonGap = VanillaWidgetRowGap * 2f;
		public const float ZombieRiskButtonLeft = VanillaAreaRowTrashRight + ZombieRiskButtonGap;
		public const float ZombieRiskFooterTextMaxWidth = ZombieRiskButtonLeft + ZombieRiskButtonWidth;
		const float ZombieRiskFooterArrowSize = 12f;
		const float ZombieRiskFooterTextHeight = 24f;
		const float ZombieRiskFooterTopPadding = 4f;
		const float ZombieRiskFooterHeight = ZombieRiskFooterTopPadding + ZombieRiskFooterArrowSize + ZombieRiskFooterTextHeight;
		public const float ZombieRiskDialogExtraHeight = ZombieRiskFooterHeight + 8f;
		static readonly string[] ZombieRiskFooterTextKeys =
		{
			"ZombieRiskAreaColumnHintLong",
			"ZombieRiskAreaColumnHintMedium",
			"ShowZombieRisk"
		};

		[HarmonyPostfix]
		[HarmonyPatch(nameof(Dialog_ManageAreas.InitialSize), MethodType.Getter)]
		public static void InitialSizePostfix(ref Vector2 __result)
		{
			__result.x += ZombieRiskButtonWidth + ZombieRiskButtonGap;
			__result.y += ZombieRiskDialogExtraHeight;
		}

		[HarmonyPostfix]
		[HarmonyPatch(nameof(Dialog_ManageAreas.DoWindowContents))]
		public static void DoWindowContentsPostfix(Dialog_ManageAreas __instance, Rect inRect)
		{
			RenderZombieRiskFooter(__instance.map, inRect);
		}

		[HarmonyPostfix]
		[HarmonyPatch("DoAreaRow", new[] { typeof(Rect), typeof(Area), typeof(int) })]
		public static void DoAreaRowPostfix(Rect rect, Area area)
		{
			var oldAnchor = Text.Anchor;
			var oldFont = Text.Font;
			var oldColor = GUI.color;
			try
			{
				Text.Font = GameFont.Small;
				Text.Anchor = TextAnchor.UpperLeft;
				GUI.color = Color.white;

				var buttonRect = new Rect(rect.x + ZombieRiskButtonLeft, rect.y + 1f, ZombieRiskButtonWidth, VanillaAreaRowHeight);
				ZombieMode(buttonRect, area);
			}
			finally
			{
				Text.Anchor = oldAnchor;
				Text.Font = oldFont;
				GUI.color = oldColor;
			}
		}

		public static bool CanRenderZombieRiskFooter(Map map)
		{
			if (map?.areaManager?.AllAreas == null)
				return false;
			var mutableCount = map.areaManager.AllAreas.Count(area => area.Mutable);
			if (mutableCount <= 0)
				return false;
			return mutableCount <= 10;
		}

		static void RenderZombieRiskFooter(Map map, Rect inRect)
		{
			if (CanRenderZombieRiskFooter(map) == false)
				return;

			var mutableCount = map.areaManager.AllAreas.Count(area => area.Mutable);
			var availableBeforeNewArea = (9 - mutableCount) * VanillaAreaRowPitch;
			var drawAfterNewArea = map.areaManager.CanMakeNewAllowed() && availableBeforeNewArea < ZombieRiskFooterHeight;
			var footerTop = drawAfterNewArea ? 10 * VanillaAreaRowPitch : mutableCount * VanillaAreaRowPitch;
			var arrowY = inRect.y + footerTop + ZombieRiskFooterTopPadding;
			var buttonCenterX = inRect.x + ZombieRiskButtonLeft + ZombieRiskButtonWidth / 2f;
			var arrowRect = new Rect(buttonCenterX - ZombieRiskFooterArrowSize / 2f, arrowY, ZombieRiskFooterArrowSize, ZombieRiskFooterArrowSize);
			var textWidth = Math.Min(inRect.width, ZombieRiskFooterTextMaxWidth);
			var textRect = new Rect(inRect.x, arrowRect.yMax, textWidth, ZombieRiskFooterTextHeight);

			var oldAnchor = Text.Anchor;
			var oldFont = Text.Font;
			var oldColor = GUI.color;
			try
			{
				GUI.color = new Color(1f, 1f, 1f, 0.65f);
				GUI.DrawTexture(arrowRect, TexButton.ReorderUp);

				Text.Font = GameFont.Tiny;
				Text.Anchor = TextAnchor.UpperRight;
				GUI.color = new Color(1f, 1f, 1f, 0.6f);
				_ = Widgets.LabelFit(textRect, ZombieRiskFooterText(textRect.width));
			}
			finally
			{
				Text.Anchor = oldAnchor;
				Text.Font = oldFont;
				GUI.color = oldColor;
			}
		}

		public static string ZombieRiskFooterText(float width)
		{
			for (var i = 0; i < ZombieRiskFooterTextKeys.Length; i++)
			{
				var text = ZombieRiskFooterTextKeys[i].Translate().ToString();
				if (Text.CalcSize(text).x <= width)
					return text;
			}
			return "ShowZombieRisk".Translate().ToString();
		}

		public static string ToStringHuman(this AreaRiskMode mode)
		{
			return mode switch
			{
				AreaRiskMode.Ignore => "Ignore".Translate(),
				AreaRiskMode.ColonistInside => "ColonistInside".Translate(),
				AreaRiskMode.ColonistOutside => "ColonistOutside".Translate(),
				AreaRiskMode.ZombieInside => "ZombieInside".Translate(),
				AreaRiskMode.ZombieOutside => "ZombieOutside".Translate(),
				_ => null,
			};
		}

		public static AreaRiskMode GetMode(Area area) => ZombieSettings.Values.dangerousAreas.TryGetValue(area, AreaRiskMode.Ignore);

		public static void ZombieMode(Rect rect, Area area)
		{
			if (area == null)
				return;
			var currentMode = GetMode(area);
			if (Widgets.ButtonText(rect, currentMode.ToStringHuman()))
			{
				var options = new List<FloatMenuOption>();
				foreach (var choice in Enum.GetValues(typeof(AreaRiskMode)))
				{
					var newMode = (AreaRiskMode)choice;
					options.Add(new FloatMenuOption(newMode.ToStringHuman(), delegate ()
					{
						if (newMode != currentMode)
							SetMode(area, newMode);
					},
					MenuOptionPriority.Default, null, null, 0f, null, null, true, 0));
				}
				Find.WindowStack.Add(new FloatMenu(options));
			}
		}

		public static void SetMode(Area area, AreaRiskMode mode)
		{
			if (area == null)
				return;
			if (mode == AreaRiskMode.Ignore)
				_ = ZombieSettings.Values.dangerousAreas.Remove(area);
			else
				ZombieSettings.Values.dangerousAreas[area] = mode;
		}

		public static void ClearZombieRisk(Area area)
		{
			if (area == null)
				return;
			_ = ZombieAreaManager.pawnsInDanger.RemoveAll(pair => pair.Value == area);
			_ = ZombieSettings.Values?.dangerousAreas?.Remove(area);
		}
	}
}
