using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using UnityEngine;
using Verse;
using static HarmonyLib.Code;

namespace ZombieLand
{
	[HarmonyPatch(typeof(Pawn_NeedsTracker))]
	[HarmonyPatch(nameof(Pawn_NeedsTracker.ShouldHaveNeed))]
	static class Pawn_NeedsTracker_ShouldHaveNeed_Patch
	{
		static void Postfix(NeedDef nd, ref bool __result)
		{
			if (nd == CustomDefs.Contamination && Constants.CONTAMINATION == 0)
				__result = false;
		}
	}

	[HarmonyPatch(typeof(Game))]
	[HarmonyPatch(nameof(Game.FinalizeInit))]
	static class Game_FinalizeInit_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION > 0;

		static void Postfix()
		{
			ContaminationManager.Instance.FixGrounds();
		}
	}

	[HarmonyPatch(typeof(ThingMaker), nameof(ThingMaker.MakeThing))]
	static file class Thing_MakeThing_Patch
	{
		static bool Prepare() => false;

		static void Postfix(Thing __result)
		{
			if (Tools.IsPlaying() && __result is not Mote)
			{
				Log.ResetMessageCount();
				Log.Message($"NEW {__result}");
			}
		}
	}

	[HarmonyPatch(typeof(Thing), nameof(Thing.Destroy))]
	static class Thing_Destroy_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION > 0;

		static void Prefix(Thing __instance, out int __state)
		{
			if (Tools.IsPlaying() && __instance is not Mote)
			{
				//Log.ResetMessageCount();
				//Log.Message($"DEL {__instance}");
				__state = __instance.thingIDNumber;
			}
			else
				__state = -1;
		}

		static void Postfix(int __state)
		{
			if (__state != -1)
				ContaminationManager.Instance.contaminations.Remove(__state);
		}
	}

	[HarmonyPatch(typeof(Thing), nameof(Thing.SpecialDisplayStats))]
	static class Thing_SpecialDisplayStats_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION > 0;

		static IEnumerable<StatDrawEntry> Postfix(IEnumerable<StatDrawEntry> entries, Thing __instance)
		{
			foreach (var entry in entries)
				yield return entry;
			yield return new StatDrawEntry(
				StatCategoryDefOf.BasicsImportant,
				"ZombieContamination".Translate(),
				$"{100 * __instance.GetContamination():F2}%",
				"ContaminationHelp".Translate(),
				0
			);
		}
	}

	[HarmonyPatch(typeof(Widgets), nameof(Widgets.ThingIcon))]
	[HarmonyPatch(new[] { typeof(Rect), typeof(Thing), typeof(float), typeof(Rot4?), typeof(bool) })]
	static class Widgets_ThingIcon_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION > 0;

		static void Prefix(Rect rect, Thing thing, float alpha)
		{
			var contamination = thing.GetContamination();
			if (contamination == 0)
				return;
			var color = new Color(0, 1, 0, alpha);
			Tools.DrawBorderRect(rect, color.ToTransparent(0.5f));
			rect = rect.ExpandedBy(-1, -1);
			rect.yMin = rect.yMax - rect.height * Tools.Boxed(contamination, 0, 1);
			Widgets.DrawBoxSolid(rect, color.ToTransparent(0.25f));
		}
	}

	[HarmonyPatch(typeof(MainTabWindow_Quests), nameof(MainTabWindow_Quests.DoRow))]
	static class MainTabWindow_Quests_DoRow_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION > 0;

		static int Max(int a, int b, Quest quest)
		{
			if (quest.parts.Any(part => part is QuestPart_DecontaminateColonists))
				return 0;
			return Mathf.Max(a, b);
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> instructions.ExtraArgumentsTranspiler(typeof(Mathf), () => Max(default, default, default), new[] { Ldarg_2 }, 1);
	}

	[HarmonyPatch(typeof(PlaySettings), nameof(PlaySettings.DoPlaySettingsGlobalControls))]
	static class PlaySettings_DoPlaySettingsGlobalControls_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION > 0;

		static void Postfix(WidgetRow row, bool worldView)
		{
			if (worldView == false && Current.ProgramState == ProgramState.Playing)
			{
				var label = "ShowContaminationOverlayToggleButton".Translate();
				row.ToggleableIcon(ref ContaminationManager.Instance.showContaminationOverlay, Constants.ShowContaminationOverlay, label, SoundDefOf.Mouseover_ButtonToggle);
			}
		}
	}

	[HarmonyPatch(typeof(MouseoverReadout), nameof(MouseoverReadout.MouseoverReadoutOnGUI))]
	static class MouseoverReadout_MouseoverReadoutOnGUI_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION > 0;

		static string GetGlowLabelByValue(float value, IntVec3 cell)
		{
			var result = MouseoverUtility.GetGlowLabelByValue(value);
			var map = Find.CurrentMap;
			if (cell.InBounds(map))
			{
				var contamination = map.GetContamination(cell);
				if (contamination >= ContaminationFactors.minContaminationThreshold)
					result += $" Contaminated ({contamination:P2})";
			}
			return result;
		}

		static string LabelMouseover(Entity self)
		{
			var result = self.LabelMouseover;
			if (self is Thing thing)
			{
				var contamination = thing.GetContamination();
				if (contamination >= ContaminationFactors.minContaminationThreshold)
					result += $", {contamination:P2} contaminated";
			}
			return result;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var m_MouseCell = SymbolExtensions.GetMethodInfo(() => UI.MouseCell());
			var p_LabelMouseover = AccessTools.PropertyGetter(typeof(Entity), nameof(Entity.LabelMouseover));
			var m_LabelMouseover = SymbolExtensions.GetMethodInfo(() => LabelMouseover(default));
			var m_GetGlowLabelByValue = SymbolExtensions.GetMethodInfo(() => MouseoverUtility.GetGlowLabelByValue(default));
			var m_GetGlowLabelByValueWithCell = SymbolExtensions.GetMethodInfo(() => GetGlowLabelByValue(default, default));

			return new CodeMatcher(instructions)
				.MatchEndForward(new CodeMatch(operand: m_MouseCell), Stloc_0)
				.ThrowIfInvalid("Cannot find UI.MouseCell(), Stloc_0")
				.MatchStartForward(new CodeMatch(operand: m_GetGlowLabelByValue))
				.ThrowIfInvalid($"Cannot find {m_GetGlowLabelByValue.FullDescription()}")
				.Set(OpCodes.Call, m_GetGlowLabelByValueWithCell)
				.Insert(Ldloc_0)
				.MatchStartForward(new CodeMatch(operand: p_LabelMouseover))
				.ThrowIfInvalid($"Cannot find {p_LabelMouseover.FullDescription()}")
				.Set(OpCodes.Call, m_LabelMouseover)
				.InstructionEnumeration();
		}
	}

	[HarmonyPatch(typeof(InspectPaneUtility), nameof(InspectPaneUtility.PaneWidthFor))]
	static class InspectPaneUtility_PaneWidthFor_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION > 0;

		[HarmonyPriority(Priority.Last)]
		static void Postfix(ref float __result, IInspectPane pane, bool __runOriginal)
		{
			if (__runOriginal && pane is MainTabWindow_Inspect)
				__result += 146f;
		}
	}

	[HarmonyPatch(typeof(InspectPaneFiller), nameof(InspectPaneFiller.DoPaneContentsFor))]
	static class InspectPaneFiller_DoPaneContentsFor_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION > 0;

		static void DrawHealth(WidgetRow row, Thing t)
		{
			InspectPaneFiller.DrawHealth(row, t);

			var contamination = t.GetContamination();
			if (contamination == 0)
				contamination = t.Map.GetContamination(t.Position, true);

			GUI.color = Color.gray;
			if (contamination > 0.2f)
				GUI.color = Color.white;
			if (contamination > 0.4f)
				GUI.color = Color.cyan;
			if (contamination > 0.6f)
				GUI.color = Color.yellow;
			if (contamination > 0.8f)
				GUI.color = Color.red;
			row.Gap(6f);
			row.FillableBar(140f, 16f, contamination, $"{contamination:P2} contamination", InspectPaneFiller.MoodTex, InspectPaneFiller.BarBGTex);
			GUI.color = Color.white;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var from = SymbolExtensions.GetMethodInfo(() => InspectPaneFiller.DrawHealth(default, default));
			var to = SymbolExtensions.GetMethodInfo(() => DrawHealth(default, default));
			return instructions.MethodReplacer(from, to);
		}
	}

	[HarmonyPatch(typeof(BeautyDrawer), nameof(BeautyDrawer.DrawBeautyAroundMouse))]
	static class BeautyDrawer_DrawBeautyAroundMouse_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION > 0;

		static bool Prefix()
		{
			if (Input.GetKey(KeyCode.LeftShift) == false && Input.GetKey(KeyCode.RightShift) == false)
				return true;

			var map = Find.CurrentMap;
			var mouseCell = UI.MouseCell();
			var grid = map.GetContamination();

			for (var i = 0; i < BeautyUtility.SampleNumCells_Beauty; i++)
			{
				var cell = mouseCell + GenRadial.RadialPattern[i];
				if (cell.InBounds(map) && !cell.Fogged(map))
				{
					var cellThings = map.thingGrid.ThingsListAtFast(cell).Where(t => t is not Mote).ToArray();

					var contaminationCell = grid[cell];
					var contaminationThings = cellThings
						.Where(thing => thing.DrawPos == thing.Position.ToVector3Shifted())
						.Sum(thing => thing.GetContamination());
					var totalContamination = contaminationCell + contaminationThings;
					if (totalContamination >= ContaminationFactors.minContaminationThreshold)
					{
						var textColor = Color.gray;
						if (contaminationCell > 0.2f || contaminationThings > 0.2f)
							textColor = Color.white;
						if (contaminationCell > 0.4f || contaminationThings > 0.2f)
							textColor = Color.cyan;
						if (contaminationCell > 0.6f || contaminationThings > 0.6f)
							textColor = Color.yellow;
						if (contaminationCell > 0.8f || contaminationThings > 0.8f)
							textColor = Color.red;
						GenMapUI.DrawThingLabel(GenMapUI.LabelDrawPosFor(cell), $"{totalContamination * 100:F1}", textColor);
					}

					cellThings
						.DoIf(thing => thing.DrawPos != thing.Position.ToVector3Shifted(), thing =>
						{
							var contaminiaton = thing.GetContamination();
							if (contaminiaton < ContaminationFactors.minContaminationThreshold)
								return;

							var textColor = Color.gray;
							if (contaminiaton > 0.2f)
								textColor = Color.white;
							if (contaminiaton > 0.2f)
								textColor = Color.cyan;
							if (contaminiaton > 0.6f)
								textColor = Color.yellow;
							if (contaminiaton > 0.8f)
								textColor = Color.red;

							var vector = thing.DrawPos + new Vector3(0, AltitudeLayer.MetaOverlays.AltitudeFor(), 0);
							var vector2 = Find.Camera.WorldToScreenPoint(vector) / Prefs.UIScale;
							vector2.y = UI.screenHeight - vector2.y;
							vector2.y -= 1f;
							GenMapUI.DrawThingLabel(vector2, $"{contaminiaton * 100:F1}", textColor);
						});
				}
			}

			return false;
		}
	}
}