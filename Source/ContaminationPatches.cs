using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Steamworks;
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
		public static float construction = 1f;
		public static float receipe = 1f;
		public static float billGiver = 0.2f;
		public static float worker = 0.1f;
		public static float wildPlant = 0.1f;
		public static float jelly = 0.1f;
		public static float mineable = 0.5f;
		public static float leavings = 0.5f;
		public static float filth = 0.1f;
		public static float dispenseFood = 0.1f;
		public static float produce = 0.1f;
		public static float subcoreScanner = 0.1f;
		public static float geneExtractor = 0.1f;
		public static float geneAssembler = 0.1f;
		public static float fermentingBarrel = 0.1f;
		public static float plant = 0.5f;
		public static float fire = 0.05f;
		public static float ground = 0.001f;
		public static float blood = 0.01f;
	}

	[HarmonyPatch(typeof(ThingMaker), nameof(ThingMaker.MakeThing))]
	static file class Thing_MakeThing_TestPatch
	{
		static bool Prepare() => false;

		[HarmonyPostfix]
		static void DebugLogMakeThing(Thing __result)
		{
			if (MapGenerator.mapBeingGenerated == null && Current.Game?.initData == null)
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
			if (MapGenerator.mapBeingGenerated == null && Current.Game?.initData == null)
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
				var contamination = map.GetContamination()[cell];
				if (contamination > 0)
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
				if (contamination > 0)
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

	[HarmonyPatch(typeof(InspectPaneFiller), nameof(InspectPaneFiller.DoPaneContentsFor))]
	static class InspectPaneFiller_DoPaneContentsFor_TestPatch
	{
		static void DrawHealth(WidgetRow row, Thing t)
		{
			InspectPaneFiller.DrawHealth(row, t);
			if (t is not Pawn)
			{
				var contamination = t.GetContamination();
				if (contamination > 0)
				{
					GUI.color = Color.gray;
					if (contamination > 0.2f) GUI.color = Color.white;
					if (contamination > 0.4f) GUI.color = Color.cyan;
					if (contamination > 0.6f) GUI.color = Color.yellow;
					if (contamination > 0.8f) GUI.color = Color.red;
					row.Gap(6f);
					row.FillableBar(140f, 16f, contamination, $"{contamination:P2} contamination", InspectPaneFiller.MoodTex, InspectPaneFiller.BarBGTex);
					GUI.color = Color.white;
				}
			}
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var from = SymbolExtensions.GetMethodInfo(() => InspectPaneFiller.DrawHealth(default, default));
			var to = SymbolExtensions.GetMethodInfo(() => DrawHealth(default, default));
			return instructions.MethodReplacer(from, to);
		}
	}

	[HarmonyPatch(typeof(InspectPaneUtility), nameof(InspectPaneUtility.AdjustedLabelFor))]
	static class InspectPaneUtility_AdjustedLabelFor_TestPatch
	{
		static void Postfix(List<object> selected, ref string __result)
		{
			if (selected.Count != 1)
				return;
			if (selected[0] is Pawn pawn)
			{
				var contamination = pawn.GetContamination();
				if (contamination > 0)
					__result += $" ({contamination * 100:F2}%)";
			}
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
					if (totalContamination > 0)
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
							if (contaminiaton == 0)
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