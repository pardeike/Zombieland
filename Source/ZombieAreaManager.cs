using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public static class ZombieAreaManager
	{
		public static Dictionary<Area, HashSet<IntVec3>> cache = new Dictionary<Area, HashSet<IntVec3>>();
		public static List<(Pawn, Area)> pawnsInDanger = new List<(Pawn, Area)>();
		public static DateTime nextUpdate = DateTime.Now;

		public static void DangerAlertsOnGUI()
		{
			var map = Find.CurrentMap;
			if (map == null) return;

			var now = DateTime.Now;
			if (now > nextUpdate)
			{
				nextUpdate = now.AddSeconds(0.5f);
				var pawns = map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer);
				pawnsInDanger = ZombieSettings.Values.dangerousAreas
					.Where(pair => pair.Key.Map == Find.CurrentMap)
					.SelectMany(pair =>
					{
						var area = pair.Key;
						var mode = pair.Value;
						return pawns.Where(pawn =>
						{
							if (Tools.HasInfectionState(pawn, InfectionState.Infecting, InfectionState.Infected)) return false;
							var inside = area.innerGrid[pawn.Position];
							return inside && mode == ZombieRiskMode.IfInside || !inside && mode == ZombieRiskMode.IfOutside;
						})
						.Select(pawn => (pawn, area));
					})
					.ToList();
			}
			DrawDangerous();
		}

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
					colorTexture = SolidColorMaterials.NewSolidColorTexture(c.r, c.g, c.b, 0.75f);
					Graphics.DrawTexture(new Rect(0, 0, UI.screenWidth, 2), colorTexture);

					if (highlightDangerousAreas)
						area.MarkForDraw();
				}
				foundArea = area;

				var renderTexture = RenderTexture.GetTemporary(44, 44, 32, RenderTextureFormat.ARGB32);
				Find.PawnCacheRenderer.RenderPawn(pawn, renderTexture, new Vector3(0, 0, 0.4f), 1.75f, 0f, Rot4.South, true, false, true, true, true, default, null, null, false);
				var texture = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.ARGB32, false);
				RenderTexture.active = renderTexture;
				texture.ReadPixels(new Rect(0f, 0f, renderTexture.width, renderTexture.height), 0, 0);
				texture.Apply();
				RenderTexture.active = null;
				RenderTexture.ReleaseTemporary(renderTexture);

				headsToDraw.Add((pawn, texture));
			}
			if (colorTexture != null)
			{
				var n = headsToDraw.Count;
				var width = 5 + n * 2 + (n + 1) * 18 + 5;
				var rect = new Rect(118, 2, width, 29);
				Graphics.DrawTexture(rect, colorTexture);
				var showPositions = Mouse.IsOver(rect.ExpandedBy(4));

				rect = new Rect(123, 7, 18, 18);
				Graphics.DrawTexture(rect, Constants.Danger);

				for (var i = 0; i < n; i++)
				{
					var pawn = headsToDraw[i].Item1;
					rect = new Rect(141 + i * 22, 5, 22, 22);
					Graphics.DrawTexture(rect, headsToDraw[i].Item2);
					if (showPositions)
						TargetHighlighter.Highlight(new GlobalTargetInfo(pawn), true, false, false);
					if (Widgets.ButtonInvisible(rect))
						CameraJumper.TryJump(pawn);
				}
			}
		}
	}

	[HarmonyPatch(typeof(Area))]
	public static class Area_AreaUpdate_Patch
	{
		[HarmonyPostfix]
		[HarmonyPatch(nameof(Area.AreaUpdate))]
		static void AreaUpdate(Area __instance)
		{
			if (ZombieSettings.Values.dangerousAreas.TryGetValue(__instance, out var mode) && mode != ZombieRiskMode.Ignore)
				ZombieAreaManager.cache[__instance] = new HashSet<IntVec3>(__instance.ActiveCells);
		}

		[HarmonyPostfix]
		[HarmonyPatch(nameof(Area.Delete))]
		static void Delete(Area __instance)
		{
			_ = ZombieAreaManager.cache.Remove(__instance);
		}
	}

	[HarmonyPatch(typeof(AreaManager))]
	public static class AreaManager_Patches
	{
		[HarmonyPrefix]
		[HarmonyPatch(nameof(AreaManager.CanMakeNewAllowed))]
		public static bool CanMakeNewAllowed(ref bool __result)
		{
			__result = true;
			return false;
		}

		[HarmonyPrefix]
		[HarmonyPatch(nameof(AreaManager.SortAreas))]
		public static bool SortAreas() => false;
	}

	[HarmonyPatch(typeof(Dialog_ManageAreas))]
	public static class Dialog_ManageAreas_Patches
	{
		public static readonly Color listBackground = new Color(32 / 255f, 36 / 255f, 40 / 255f);
		public static readonly Color highlightedBackground = new Color(74 / 255f, 74 / 255f, 74 / 255f, 0.5f);
		public static readonly Color background = new Color(74 / 255f, 74 / 255f, 74 / 255f);
		public static readonly Color inactiveTextColor = new Color(145 / 255f, 125 / 255f, 98 / 255f);
		public static readonly Color areaNameZombiesInside = new Color(1f, 0.2f, 0.2f);
		public static readonly Color areaNameZombiesOutside = new Color(0.2f, 1f, 0.2f);
		public static readonly GUIStyle textFieldStyle = new GUIStyle()
		{
			alignment = TextAnchor.MiddleLeft,
			clipping = TextClipping.Clip,
			font = Text.fonts[1],
			normal = new GUIStyleState() { textColor = Color.white },
			padding = new RectOffset(7, 0, 0, 0)
		};
		public static Area selected = null;
		public static int selectedIndex = -1;
		public static Vector2 scrollPosition = Vector2.zero;
		public static AreaManager areaManager;

		[HarmonyPostfix]
		[HarmonyPatch(MethodType.Constructor, new[] { typeof(Map) })]
		public static void Constructor()
		{
			selected = null;
			selectedIndex = -1;
			scrollPosition = Vector2.zero;
		}

		[HarmonyPrefix]
		[HarmonyPriority(Priority.High)]
		[HarmonyPatch(nameof(Dialog_ManageAreas.DoWindowContents))]
		public static bool Prefix(Dialog_ManageAreas __instance)
		{
			Text.Font = GameFont.Small;

			RenderList(__instance.map);
			if (selected != null)
			{
				RenderSelectedRowContent(selected);
				selected.MarkForDraw();
			}
			return false;
		}

		public static void RenderList(Map map)
		{
			areaManager = map.areaManager;
			var allAreas = areaManager.AllAreas;
			var rowHeight = 24;

			var rect = new Rect(0, 0, 198, 283);
			Widgets.DrawBoxSolid(rect, listBackground);

			var innerWidth = rect.width - (allAreas.Count > 11 ? 16 : 0);
			var innerRect = new Rect(0f, 0f, innerWidth, allAreas.Count * rowHeight);
			Widgets.BeginScrollView(rect, ref scrollPosition, innerRect, true);
			var list = new Listing_Standard();
			list.Begin(innerRect);

			var y = 0f;
			var i = 0;
			foreach (var area in allAreas)
			{
				RenderListRow(new Rect(0, y, innerRect.width, rowHeight), area, i++);
				y += rowHeight;
			}

			list.End();
			Widgets.EndScrollView();

			y = 283 + 8;
			var bRect = new Rect(0, y, 24, 24);
			if (Widgets.ButtonImage(bRect, Constants.ButtonAdd[1]))
			{
				Event.current.Use();
				if (areaManager.TryMakeNewAllowed(out Area_Allowed newArea))
				{
					selected = newArea;
					selectedIndex = areaManager.AllAreas.IndexOf(selected);
					GUI.FocusControl("area-name");
				}
			}
			bRect.x += 32;
			var deleteable = selected?.Mutable ?? false;
			if (Widgets.ButtonImage(bRect, Constants.ButtonDel[deleteable ? 1 : 0]) && deleteable)
			{
				Event.current.Use();
				areaManager.Remove(selected);
				var newCount = areaManager.AllAreas.Count;
				if (newCount == 0)
				{
					selectedIndex = -1;
					selected = null;
				}
				else
				{
					while (newCount > 0 && selectedIndex >= newCount)
						selectedIndex--;
					selected = areaManager.AllAreas[selectedIndex];
					GUI.FocusControl("area-name");
				}
			}
			bRect.x += 32;
			var dupable = selected != null;
			if (Widgets.ButtonImage(bRect, Constants.ButtonDup[dupable ? 1 : 0]) && dupable)
			{
				Event.current.Use();
				var labelPrefix = Regex.Replace(selected.Label, @" \d+$", "");
				var existingLabels = areaManager.AllAreas.Select(a => a.Label).ToHashSet();
				for (var n = 1; true; n++)
				{
					var newLabel = $"{labelPrefix} {n}";
					if (existingLabels.Contains(newLabel) == false)
					{
						if (areaManager.TryMakeNewAllowed(out Area_Allowed newArea))
						{
							newArea.labelInt = newLabel;
							foreach (IntVec3 cell in selected.ActiveCells)
								newArea[cell] = true;
							selected = newArea;
							selectedIndex = areaManager.AllAreas.IndexOf(selected);
							GUI.FocusControl("area-name");
						}
						break;
					}
				}
			}
			bRect.x += 78;
			var upable = selectedIndex > 0;
			if (Widgets.ButtonImage(bRect, Constants.ButtonUp[upable ? 1 : 0]) && upable)
			{
				Event.current.Use();
				allAreas.Insert(selectedIndex - 1, selected);
				allAreas.RemoveAt(selectedIndex + 1);
				selectedIndex--;
			}
			bRect.x += 32;
			var downable = selectedIndex >= 0 && selectedIndex < allAreas.Count - 1;
			if (Widgets.ButtonImage(bRect, Constants.ButtonDown[downable ? 1 : 0]) && downable)
			{
				Event.current.Use();
				allAreas.Insert(selectedIndex + 2, selected);
				allAreas.RemoveAt(selectedIndex);
				selectedIndex++;
			}

			var backgroundRect = innerRect;
			backgroundRect.height = rect.height;
			if (Widgets.ButtonInvisible(backgroundRect, false))
			{
				Event.current.Use();
				selectedIndex = -1;
				selected = null;
			}
		}

		public static Color AreaLabelColor(Area area)
		{
			return GetMode(area) switch
			{
				ZombieRiskMode.IfInside => areaNameZombiesInside,
				ZombieRiskMode.IfOutside => areaNameZombiesOutside,
				_ => Color.white,
			};
		}

		public static void RenderListRow(Rect rect, Area area, int idx)
		{
			if (area == selected)
				Widgets.DrawBoxSolid(rect, background);
			else if (Mouse.IsOver(rect))
				Widgets.DrawBoxSolid(rect, highlightedBackground);

			var innerRect = rect.ExpandedBy(-3);
			innerRect.xMax += 3;
			var cRect = innerRect;
			cRect.width = cRect.height;
			Widgets.DrawBoxSolid(cRect, area.Color);

			var tRect = rect;
			tRect.xMin += 24;
			tRect.yMin += 1;
			GUI.color = AreaLabelColor(area);
			_ = Widgets.LabelFit(tRect, area.Label);
			GUI.color = Color.white;

			if (area.Mutable == false)
			{
				var lRect = rect.RightPartPixels(13).LeftPartPixels(10);
				lRect.yMin += 5;
				lRect.height = 13;
				GUI.DrawTexture(lRect, Constants.Lock);
			}

			if (Widgets.ButtonInvisible(rect))
			{
				selected = area;
				selectedIndex = idx;
				GUI.FocusControl("area-name");
			}
		}

		public static void Label(Rect rect, string key)
		{
			var lRect = rect;
			lRect.xMin -= 1;
			lRect.yMin -= 5;
			lRect.height += 5;
			Text.Anchor = TextAnchor.UpperLeft;
			_ = Widgets.LabelFit(lRect, GenText.CapitalizeAsTitle(key.Translate()));
		}

		public static string ToStringHuman(this ZombieRiskMode mode)
		{
			return mode switch
			{
				ZombieRiskMode.Ignore => "Ignore".Translate(),
				ZombieRiskMode.IfInside => "IfInside".Translate(),
				ZombieRiskMode.IfOutside => "IfOutside".Translate(),
				_ => null,
			};
		}

		public static ZombieRiskMode GetMode(Area area)
		{
			if (ZombieSettings.Values.dangerousAreas.TryGetValue(area, out var mode))
				return mode;
			return ZombieRiskMode.Ignore;
		}

		public static void ZombieMode(Rect rect)
		{
			var mode = GetMode(selected);
			if (Widgets.ButtonText(rect, mode.ToStringHuman()))
			{
				var options = new List<FloatMenuOption>();
				foreach (var choice in Enum.GetValues(typeof(ZombieRiskMode)))
				{
					var localPmode2 = (ZombieRiskMode)choice;
					var localPmode = localPmode2;
					options.Add(new FloatMenuOption(localPmode.ToStringHuman(), delegate ()
					{
						if (localPmode != mode)
						{
							if (localPmode == ZombieRiskMode.Ignore)
								_ = ZombieSettings.Values.dangerousAreas.Remove(selected);
							else
								ZombieSettings.Values.dangerousAreas[selected] = localPmode;
						}
					},
					MenuOptionPriority.Default, null, null, 0f, null, null, true, 0));
				}
				Find.WindowStack.Add(new FloatMenu(options));
			}
		}

		public static void RenderSelectedRowContent(Area area)
		{
			var left = 198 + 18;
			var width = 197;

			var lRect = new Rect(left, 0, width, 17);
			Label(lRect, "Title");
			var tRect = new Rect(left, 17, width, 27);
			Widgets.DrawBoxSolid(tRect, background);

			Text.Anchor = TextAnchor.MiddleLeft;
			if (area.Mutable)
			{
				GUI.SetNextControlName("area-name");
				var newLabel = GUI.TextField(tRect, area.Label, textFieldStyle);
				if (newLabel.Length > 28)
					newLabel = newLabel.Substring(0, 28);
				if (newLabel != area.Label)
					area.SetLabel(newLabel);
			}
			else
			{
				lRect = tRect;
				lRect.xMin += 7;
				lRect.yMin += 1;
				Widgets.Label(lRect, area.Label);
			}

			lRect = new Rect(left, 59, width, 17);
			Label(lRect, "AreaLower");
			var cRect = new Rect(left, 76, width, 27);
			Widgets.DrawBoxSolid(cRect, area.Color);

			cRect = new Rect(left, 109, 14, 14);
			Widgets.DrawBoxSolid(cRect, Color.red);
			cRect.xMin = 240;
			cRect.xMax = 413;
			var newRed = Widgets.HorizontalSlider(cRect, area.Color.r, 0f, 1f);
			if (area is Area_Allowed allowed1)
			{
				allowed1.colorInt.r = newRed;
				allowed1.colorTextureInt = null;
				area.Drawer.material = null;
				area.Drawer.SetDirty();
			}

			cRect = new Rect(left, 129, 14, 14);
			Widgets.DrawBoxSolid(cRect, Color.green);
			cRect.xMin = 240;
			cRect.xMax = 413;
			var newGreen = Widgets.HorizontalSlider(cRect, area.Color.g, 0f, 1f);
			if (area is Area_Allowed allowed2)
			{
				allowed2.colorInt.g = newGreen;
				allowed2.colorTextureInt = null;
				area.Drawer.material = null;
				area.Drawer.SetDirty();
			}

			cRect = new Rect(left, 149, 14, 14);
			Widgets.DrawBoxSolid(cRect, Color.blue);
			cRect.xMin = 240;
			cRect.xMax = 413;
			var newBlue = Widgets.HorizontalSlider(cRect, area.Color.b, 0f, 1f);
			if (area is Area_Allowed allowed3)
			{
				allowed3.colorInt.b = newBlue;
				allowed3.colorTextureInt = null;
				area.Drawer.material = null;
				area.Drawer.SetDirty();
			}

			lRect = new Rect(left, 178, width, 17);
			Label(lRect, "Contents");
			var bRect = new Rect(left, 196, width, 27);
			if (Tools.ButtonText(bRect, "InvertArea".Translate(), area.Mutable, Color.white, inactiveTextColor))
				area.Invert();

			lRect = new Rect(left, 238, width, 17);
			Label(lRect, "ShowZombieRisk");
			bRect = new Rect(left, 256, width, 27);
			ZombieMode(bRect);
		}
	}
}
