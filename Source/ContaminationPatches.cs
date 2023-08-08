using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using static HarmonyLib.Code;

// TODO the following is code that adds all patches to track future contamination
//      its purpose is to test compatibility before actually releasing such a feature
//
namespace ZombieLand
{
	static class ContaminationFactors
	{
		public static float minContaminationThreshold = 0.0001f;
		public static float contaminationElevationDelta = 0.18f;

		public static float ambrosiaAdd = 1f;
		public static float constructionAdd = 0.5f;
		public static float deepDrillAdd = 0.5f;
		public static float destroyMineableAdd = 1f;
		public static float floorAdd = 0.01f;
		public static float jellyAdd = 0.5f;
		public static float plantAdd = 0.5f;
		public static float pollutionAdd = 0.05f;
		public static float snowAdd = 0.1f;
		public static float sowedPlantAdd = 0.2f;
		public static float wastePackAdd = 0.5f;

		public static float disassembleTransfer = 0.1f;
		public static float dispenseFoodTransfer = 0.1f;
		public static float fermentingBarrelTransfer = 0.1f;
		public static float filthTransfer = 0.01f;
		public static float geneAssemblerTransfer = 0.2f;
		public static float geneExtractorTransfer = 0.2f;
		public static float generalTransfer = 0.1f;
		public static float ingestTransfer = 0.25f;
		public static float leavingsTransfer = 0.5f;
		public static float medicineTransfer = 0.9f;
		public static float plantTransfer = 0.5f;
		public static float receipeTransfer = 0.5f;
		public static float repairTransfer = 0.01f;
		public static float stumpTransfer = 1f;
		public static float subcoreScannerTransfer = 0.2f;
		public static float workerTransfer = 0.02f;

		public static float benchEqualize = 0.02f;
		public static float bloodEqualize = 0.1f;
		public static float carryEqualize = 0.002f;
		public static float enterCellEqualize = 0.001f;
		public static float filthEqualize = 0.01f;
		public static float meleeEqualize = 0.1f;
		public static float produceEqualize = 0.1f;
		public static float restEqualize = 0.001f;
		public static float sowingPawnEqualize = 0.1f;
		public static float tendEqualizeWorst = 0.2f;
		public static float tendEqualizeBest = 0f;

		public static float fireReduction = 0.05f;
	}

	[HarmonyPatch(typeof(Game))]
	[HarmonyPatch(nameof(Game.FinalizeInit))]
	static class Game_FinalizeInit_Patch
	{
		static void Postfix()
		{
			ContaminationManager.Instance.FixGrounds();
			//ContaminationManager.Instance.FixMinerables();
		}
	}

	[HarmonyPatch(typeof(ThingMaker), nameof(ThingMaker.MakeThing))]
	static file class Thing_MakeThing_TestPatch
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
	static class Thing_Destroy_TestPatch
	{
		static bool Prepare() => false;

		static void Prefix(Thing __instance, out int __state)
		{
			if (Tools.IsPlaying() && __instance is not Mote)
			{
				Log.ResetMessageCount();
				Log.Message($"DEL {__instance}");
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

	[HarmonyPatch(typeof(PlaySettings), nameof(PlaySettings.DoPlaySettingsGlobalControls))]
	static class PlaySettings_DoPlaySettingsGlobalControls_TestPatch
	{
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
	static class MouseoverReadout_MouseoverReadoutOnGUI_TestPatch
	{
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
	static class InspectPaneUtility_PaneWidthFor_TestPatch
	{
		[HarmonyPriority(Priority.Last)]
		static void Postfix(ref float __result, IInspectPane pane, bool __runOriginal)
		{
			if (__runOriginal && pane is MainTabWindow_Inspect)
				__result += 146f;
		}
	}

	[HarmonyPatch(typeof(InspectPaneFiller), nameof(InspectPaneFiller.DoPaneContentsFor))]
	static class InspectPaneFiller_DoPaneContentsFor_TestPatch
	{
		static void DrawHealth(WidgetRow row, Thing t)
		{
			InspectPaneFiller.DrawHealth(row, t);

			var contamination = t.GetContamination();
			if (contamination == 0)
				contamination = t.Map.GetContamination(t.Position, true);

			GUI.color = Color.gray;
			if (contamination > 0.2f) GUI.color = Color.white;
			if (contamination > 0.4f) GUI.color = Color.cyan;
			if (contamination > 0.6f) GUI.color = Color.yellow;
			if (contamination > 0.8f) GUI.color = Color.red;
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
	static class BeautyDrawer_DrawBeautyAroundMouse_TestPatch
	{
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
						if (contaminationCell > 0.2f || contaminationThings > 0.2f) textColor = Color.white;
						if (contaminationCell > 0.4f || contaminationThings > 0.2f) textColor = Color.cyan;
						if (contaminationCell > 0.6f || contaminationThings > 0.6f) textColor = Color.yellow;
						if (contaminationCell > 0.8f || contaminationThings > 0.8f) textColor = Color.red;
						GenMapUI.DrawThingLabel(GenMapUI.LabelDrawPosFor(cell), $"{totalContamination * 100:F1}", textColor);
					}

					cellThings
						.DoIf(thing => thing.DrawPos != thing.Position.ToVector3Shifted(), thing =>
						{
							var contaminiaton = thing.GetContamination();
							if (contaminiaton < ContaminationFactors.minContaminationThreshold)
								return;

							var textColor = Color.gray;
							if (contaminiaton > 0.2f) textColor = Color.white;
							if (contaminiaton > 0.2f) textColor = Color.cyan;
							if (contaminiaton > 0.6f) textColor = Color.yellow;
							if (contaminiaton > 0.8f) textColor = Color.red;

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