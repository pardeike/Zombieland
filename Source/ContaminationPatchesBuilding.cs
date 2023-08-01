using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
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
				Log.Warning($"Consume {thing} [{contamination}]");
				return contamination;
			});
			self.ClearAndDestroyContents(mode);
			return contamination;
		}

		static Thing MakeThing(ThingDef def, ThingDef stuff, float contamination)
		{
			var thing = ThingMaker.MakeThing(def, stuff);
			thing.AddContamination(contamination, ContaminationFactors.construction);
			Log.Warning($"Produce {thing} [{contamination}]");
			return thing;
		}

		static void SetTerrain(TerrainGrid self, IntVec3 c, TerrainDef newTerr, float contamination)
		{
			self.SetTerrain(c, newTerr);
			var map = self.map;
			var grounds = map.GetContamination();
			grounds.cells[map.cellIndices.CellToIndex(c)] = contamination;
			grounds.drawer.SetDirty();
			Log.Warning($"Produce for {newTerr} at {c} [{contamination}]");
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
			thing.TransferContamination(__result);
			Log.Warning($"Minified {__result} from {thing}");
		}
	}

	[HarmonyPatch(typeof(GenConstruct), nameof(GenConstruct.PlaceBlueprintForInstall))]
	static class GenConstruct_PlaceBlueprintForInstall_TestPatch
	{
		static void Postfix(Blueprint_Install __result, MinifiedThing itemToInstall)
		{
			if (itemToInstall == null || __result == null)
				return;
			itemToInstall.TransferContamination(__result);
			Log.Warning($"Installed {__result} from {itemToInstall}");
		}
	}

	[HarmonyPatch(typeof(GenConstruct), nameof(GenConstruct.PlaceBlueprintForReinstall))]
	static class GenConstruct_PlaceBlueprintForReinstall_TestPatch
	{
		static void Postfix(Blueprint_Install __result, Building buildingToReinstall)
		{
			if (buildingToReinstall == null || __result == null)
				return;
			buildingToReinstall.TransferContamination(__result);
			Log.Warning($"Created {__result} from {buildingToReinstall}");
		}
	}

	[HarmonyPatch(typeof(Blueprint), nameof(Blueprint.TryReplaceWithSolidThing))]
	static class Blueprint_TryReplaceWithSolidThing_TestPatch
	{
		static void Postfix(ref Thing createdThing, Blueprint __instance)
		{
			if (createdThing == null)
				return;
			__instance.TransferContamination(createdThing);
			Log.Warning($"Installed {createdThing} from {__instance}");
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
			__result.AddContamination(__state);
			Log.Warning($"Smoothed {__result} [{__state}]");
		}
	}

	[HarmonyPatch(typeof(SmoothableWallUtility), nameof(SmoothableWallUtility.Notify_BuildingDestroying))]
	static class SmoothableWallUtility_Notify_BuildingDestroying_TestPatch
	{
		static Thing Spawn(Thing newThing, IntVec3 loc, Map map, Rot4 rot, WipeMode wipeMode, bool respawningAfterLoad, Thing destroyedThing)
		{
			var thing = GenSpawn.Spawn(newThing, loc, map, rot, wipeMode, respawningAfterLoad);
			destroyedThing.TransferContamination(thing);
			Log.Warning($"Produced {thing} from destroyed {destroyedThing}");
			return thing;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var from = SymbolExtensions.GetMethodInfo(() => GenSpawn.Spawn(null, IntVec3.Zero, null, Rot4.Invalid, WipeMode.Vanish, false));
			var to = SymbolExtensions.GetMethodInfo(() => Spawn(null, IntVec3.Zero, null, Rot4.Invalid, WipeMode.Vanish, false, null));

			return new CodeMatcher(instructions)
				.MatchStartForward(new CodeMatch(operand: from))
					 .ThrowIfInvalid($"Cannot find {from.FullDescription()}")
					 .InsertAndAdvance(Ldarg_0)
					 .SetInstruction(Call[to])
					 .InstructionEnumeration();
		}
	}

	[HarmonyPatch(typeof(Building_SubcoreScanner), nameof(Building_SubcoreScanner.Tick))]
	static class Building_SubcoreScanner_Tick_TestPatch
	{
		static Thing MakeThing(ThingDef def, ThingDef stuff, Building_SubcoreScanner scanner)
		{
			var result = ThingMaker.MakeThing(def, stuff);
			scanner.TransferContamination(ContaminationFactors.subcoreScanner, result);
			Log.Warning($"Produce {result} from {scanner}");
			return result;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> instructions.ExtraThisTranspiler(typeof(ThingMaker), () => MakeThing(default, default, default));
	}

	[HarmonyPatch(typeof(Building_GeneExtractor), nameof(Building_GeneExtractor.Finish))]
	static class Building_GeneExtractor_Finish_TestPatch
	{
		static Thing MakeThing(ThingDef def, ThingDef stuff, Building_GeneExtractor extractor)
		{
			var result = ThingMaker.MakeThing(def, stuff);
			var pawn = extractor.ContainedPawn;
			pawn.TransferContamination(ContaminationFactors.geneExtractor, result);
			Log.Warning($"Produce {result} from {pawn}");
			return result;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> instructions.ExtraThisTranspiler(typeof(ThingMaker), () => MakeThing(default, default, default));
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
			if (things != null)
			{
				things.TransferContamination(ContaminationFactors.dispenseFood, result);
				Log.Warning($"Produce {result} from [{things.Join(t => $"{t}")}]");
			}
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
				factor = ContaminationFactors.geneAssembler;
			else if (building is Building_FermentingBarrel)
				factor = ContaminationFactors.fermentingBarrel;
			building.TransferContamination(factor, result);
			Log.Warning($"Produce {result} [{factor}] from {building}");
			return result;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> instructions.ExtraThisTranspiler(typeof(ThingMaker), () => MakeThing(default, default, default));
	}
}