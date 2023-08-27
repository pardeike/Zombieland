﻿using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Verse;
using static HarmonyLib.Code;

namespace ZombieLand
{
	[HarmonyPatch(typeof(Frame), nameof(Frame.CompleteConstruction))]
	static class Frame_CompleteConstruction_TestPatch
	{
		static float ClearAndDestroyContents(ThingOwner self, DestroyMode mode)
		{
			var contamination = self.Sum(thing =>
			{
				var contamination = thing.GetContamination();
				if (contamination > 0)
					Log.Warning($"Consume {thing} gives {contamination}");
				return contamination;
			});
			self.ClearAndDestroyContents(mode);
			return contamination;
		}

		static Thing MakeThing(ThingDef def, ThingDef stuff, float contamination)
		{
			var thing = ThingMaker.MakeThing(def, stuff);
			thing.AddContamination(contamination, () => Log.Warning($"Produce {thing} gains {contamination}"), ContaminationFactors.constructionAdd);
			return thing;
		}

		static void SetTerrain(TerrainGrid self, IntVec3 c, TerrainDef newTerr, float contamination)
		{
			self.SetTerrain(c, newTerr);
			if (contamination > 0)
			{
				var map = self.map;
				var grounds = map.GetContamination();
				grounds.cells[map.cellIndices.CellToIndex(c)] = contamination;
				grounds.SetDirty();
				Log.Warning($"Produce for {newTerr} at {c} [{contamination}]");
			}
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
		{
			var sumVar = generator.DeclareLocal(typeof(float));

			var from1 = SymbolExtensions.GetMethodInfo((ThingOwner owner) => owner.ClearAndDestroyContents(DestroyMode.Vanish));
			var to1 = SymbolExtensions.GetMethodInfo(() => ClearAndDestroyContents(default, default));

			var from2 = SymbolExtensions.GetMethodInfo(() => ThingMaker.MakeThing(default, default));
			var to2 = SymbolExtensions.GetMethodInfo(() => MakeThing(default, default, default));

			var from3 = SymbolExtensions.GetMethodInfo((TerrainGrid grid) => grid.SetTerrain(default, default));
			var to3 = SymbolExtensions.GetMethodInfo(() => SetTerrain(default, default, default, default));

			return new CodeMatcher(instructions)
				 .MatchStartForward(new CodeMatch(operand: from1))
				 .ThrowIfInvalid($"Cannot find {from1.FullDescription()}")
				 .SetOperandAndAdvance(to1)
				 .Insert(Stloc[sumVar])
				 .MatchStartForward(new CodeMatch(operand: from2))
				 .ThrowIfInvalid($"Cannot find {from2.FullDescription()}")
				 .InsertAndAdvance(Ldloc[sumVar])
				 .SetInstruction(Call[to2])
				 .MatchStartForward(new CodeMatch(operand: from3))
				 .ThrowIfInvalid($"Cannot find {from3.FullDescription()}")
				 .InsertAndAdvance(Ldloc[sumVar])
				 .SetInstruction(Call[to3])
				 .InstructionEnumeration();
		}
	}

	[HarmonyPatch(typeof(MinifyUtility), nameof(MinifyUtility.MakeMinified))]
	static class MinifyUtility_MakeMinified_TestPatch
	{
		static void Postfix(MinifiedThing __result, Thing thing)
		{
			if (thing == null || __result == null)
				return;
			thing.TransferContamination(__result, () => Log.Warning($"Minified {__result} from {thing}"));
		}
	}

	[HarmonyPatch(typeof(GenConstruct), nameof(GenConstruct.PlaceBlueprintForInstall))]
	static class GenConstruct_PlaceBlueprintForInstall_TestPatch
	{
		static void Postfix(Blueprint_Install __result, MinifiedThing itemToInstall)
		{
			if (itemToInstall == null || __result == null)
				return;
			itemToInstall.TransferContamination(__result, () => Log.Warning($"Installed {__result} from {itemToInstall}"));
		}
	}

	[HarmonyPatch(typeof(GenConstruct), nameof(GenConstruct.PlaceBlueprintForReinstall))]
	static class GenConstruct_PlaceBlueprintForReinstall_TestPatch
	{
		static void Postfix(Blueprint_Install __result, Building buildingToReinstall)
		{
			if (buildingToReinstall == null || __result == null)
				return;
			buildingToReinstall.TransferContamination(__result, () => Log.Warning($"Created {__result} from {buildingToReinstall}"));
		}
	}

	[HarmonyPatch(typeof(Blueprint), nameof(Blueprint.TryReplaceWithSolidThing))]
	static class Blueprint_TryReplaceWithSolidThing_TestPatch
	{
		static void Postfix(ref Thing createdThing, Blueprint __instance)
		{
			if (createdThing == null)
				return;
			var _createdThing = createdThing;
			__instance.TransferContamination(createdThing, () => Log.Warning($"Installed {_createdThing} from {__instance}"));
		}
	}

	[HarmonyPatch(typeof(SmoothableWallUtility), nameof(SmoothableWallUtility.SmoothWall))]
	static class SmoothableWallUtility_SmoothWall_TestPatch
	{
		static void Prefix(Thing target, out float __state)
		{
			__state = target?.GetContamination() ?? 0;
		}

		static void Postfix(Thing __result, float __state)
		{
			if (__result == null)
				return;
			__result.AddContamination(__state, () => Log.Warning($"Smoothed {__result} [{__state}]"));
		}
	}

	[HarmonyPatch(typeof(SmoothableWallUtility), nameof(SmoothableWallUtility.Notify_BuildingDestroying))]
	static class SmoothableWallUtility_Notify_BuildingDestroying_TestPatch
	{
		static Thing Spawn(Thing newThing, IntVec3 loc, Map map, Rot4 rot, WipeMode wipeMode, bool respawningAfterLoad, Thing t)
		{
			var thing = GenSpawn.Spawn(newThing, loc, map, rot, wipeMode, respawningAfterLoad);
			t.TransferContamination(thing, () => Log.Warning($"Produced {thing} from destroyed {t}"));
			return thing;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> instructions.ExtraArgumentsTranspiler(typeof(GenSpawn), () => Spawn(default, default, default, default, default, default, default), new[] { Ldarg_0 }, 1);
	}

	[HarmonyPatch(typeof(Building_SubcoreScanner), nameof(Building_SubcoreScanner.Tick))]
	static class Building_SubcoreScanner_Tick_TestPatch
	{
		static Thing MakeThing(ThingDef def, ThingDef stuff, Building_SubcoreScanner scanner)
		{
			var result = ThingMaker.MakeThing(def, stuff);
			scanner.TransferContamination(ContaminationFactors.subcoreScannerTransfer, () => Log.Warning($"Produce {result} from {scanner}"), result);
			return result;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> instructions.ExtraArgumentsTranspiler(typeof(ThingMaker), () => MakeThing(default, default, default), new[] { Ldarg_0 }, 1);
	}

	[HarmonyPatch(typeof(Building_GeneExtractor), nameof(Building_GeneExtractor.Finish))]
	static class Building_GeneExtractor_Finish_TestPatch
	{
		static Thing MakeThing(ThingDef def, ThingDef stuff, Building_GeneExtractor extractor)
		{
			var result = ThingMaker.MakeThing(def, stuff);
			var pawn = extractor.ContainedPawn;
			pawn.TransferContamination(ContaminationFactors.geneExtractorTransfer, () => Log.Warning($"Produce {result} from {pawn}"), result);
			return result;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> instructions.ExtraArgumentsTranspiler(typeof(ThingMaker), () => MakeThing(default, default, default), new[] { Ldarg_0 }, 1);
	}

	[HarmonyPatch(typeof(Building_NutrientPasteDispenser), nameof(Building_NutrientPasteDispenser.TryDispenseFood))]
	static class Building_NutrientPasteDispenser_TryDispenseFood_TestPatch
	{
		static Thing AddToThingList(Thing thing, List<Thing> things)
		{
			things?.Add(thing);
			return thing;
		}

		static Thing MakeThing(ThingDef def, ThingDef stuff, List<Thing> things)
		{
			var result = ThingMaker.MakeThing(def, stuff);
			things?.TransferContamination(ContaminationFactors.dispenseFoodTransfer, () => Log.Warning($"Produce {result} from [{things.Join(t => $"{t}")}]"), result);
			return result;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
		{
			var thingList = generator.DeclareLocal(typeof(List<Thing>));
			var thingListConstructor = AccessTools.DeclaredConstructor(thingList.LocalType, Array.Empty<Type>());

			var m_SplitOff = SymbolExtensions.GetMethodInfo((Thing thing) => thing.SplitOff(0));
			var m_AddToThingList = SymbolExtensions.GetMethodInfo(() => AddToThingList(default, default));

			var from2 = SymbolExtensions.GetMethodInfo(() => ThingMaker.MakeThing(default, default));
			var to2 = SymbolExtensions.GetMethodInfo(() => MakeThing(default, default, default));

			return new CodeMatcher(instructions)
				 .MatchStartForward(Newobj)
				 .Insert(Newobj[thingListConstructor], Stloc[thingList])
				 .MatchStartForward(new CodeMatch(operand: m_SplitOff))
				 .Advance(1)
				 .Insert(Ldloc[thingList], Call[m_AddToThingList])
				 .MatchStartForward(new CodeMatch(operand: from2))
				 .ThrowIfInvalid($"Cannot find {from2.FullDescription()}")
				 .InsertAndAdvance(Ldloc[thingList])
				 .SetInstruction(Call[to2])
				 .InstructionEnumeration();
		}
	}

	[HarmonyPatch]
	static class Misc_Building_TestPatches
	{
		static IEnumerable<MethodBase> TargetMethods()
		{
			yield return SymbolExtensions.GetMethodInfo((Building_GeneAssembler building) => building.Finish());
			yield return SymbolExtensions.GetMethodInfo((Building_FermentingBarrel building) => building.TakeOutBeer());
		}

		static Thing MakeThing(ThingDef def, ThingDef stuff, Building building)
		{
			var result = ThingMaker.MakeThing(def, stuff);
			var factor = 1f;
			if (building is Building_GeneAssembler)
				factor = ContaminationFactors.geneAssemblerTransfer;
			else if (building is Building_FermentingBarrel)
				factor = ContaminationFactors.fermentingBarrelTransfer;
			building.TransferContamination(factor, () => Log.Warning($"Produce {result} [{factor}] from {building}"), result);
			return result;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> instructions.ExtraArgumentsTranspiler(typeof(ThingMaker), () => MakeThing(default, default, default), new[] { Ldarg_0 }, 1);
	}

	[HarmonyPatch]
	static class JobDriver_Repair_MakeNewToils_TestPatch
	{
		static MethodBase TargetMethod()
		{
			var m_Notify_BuildingRepaired = SymbolExtensions.GetMethodInfo((ListerBuildingsRepairable lister) => lister.Notify_BuildingRepaired(default));
			var type = AccessTools.FirstInner(typeof(JobDriver_Repair), type => type.Name.Contains("DisplayClass"));
			return AccessTools.FirstMethod(type, method => method.CallsMethod(m_Notify_BuildingRepaired));
		}

		public static void Equalize(Pawn pawn, Thing thing)
		{
			if (thing != null)
				ContaminationFactors.repairTransfer.Equalize(pawn, thing, () => Log.Warning($"{pawn} repaired {thing}"));
		}

		static void Notify_BuildingRepaired(ListerBuildingsRepairable self, Building b, Pawn pawn)
		{
			Equalize(pawn, b);
			self.Notify_BuildingRepaired(b);
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> instructions.ExtraArgumentsTranspiler(typeof(ListerBuildingsRepairable), () => Notify_BuildingRepaired(default, default, default), new[] { Ldloc_0 }, 1);
	}

	[HarmonyPatch]
	static class JobDriver_RepairMech_MakeNewToils_TestPatch
	{
		static MethodBase TargetMethod()
		{
			var m_RepairTick = SymbolExtensions.GetMethodInfo(() => MechRepairUtility.RepairTick(default));
			return AccessTools.FirstMethod(typeof(JobDriver_RepairMech), method => method.CallsMethod(m_RepairTick));
		}

		static void RepairTick(Pawn mech, JobDriver_RepairMech jobDriver)
		{
			JobDriver_Repair_MakeNewToils_TestPatch.Equalize(jobDriver.pawn, mech);
			MechRepairUtility.RepairTick(mech);
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> instructions.ExtraArgumentsTranspiler(typeof(MechRepairUtility), () => RepairTick(default, default), new[] { Ldarg_0 }, 1);
	}
}