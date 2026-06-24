using HarmonyLib;
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
	readonly struct ReplacementContaminationState
	{
		readonly float contamination;
		readonly Map map;

		ReplacementContaminationState(float contamination, Map map)
		{
			this.contamination = contamination;
			this.map = map;
		}

		public static ReplacementContaminationState Capture(Thing thing)
		{
			if (thing == null)
				return default;
			_ = ContaminationManager.TryGetThingMap(thing, null, out var map);
			return new ReplacementContaminationState(ContaminationManager.Instance.Get(thing, false, map), map);
		}

		public void Move(Thing source, Thing target)
		{
			if (target == null || contamination <= 0)
				return;
			ContaminationManager.Instance.Remove(source, map);
			ContaminationManager.Instance.Add(target, contamination, map);
		}
	}

	[HarmonyPatch(typeof(Frame), nameof(Frame.CompleteConstruction))]
	static class Frame_CompleteConstruction_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static float ClearAndDestroyContents(ThingOwner self, DestroyMode mode)
		{
			var contamination = self.Sum(thing => thing.GetContamination());
			if (contamination <= 0 && self.owner is Frame frame)
				contamination = frame.GetContamination();
			self.ClearAndDestroyContents(mode);
			return contamination;
		}

		static Thing MakeThing(ThingDef def, ThingDef stuff, float contamination, Pawn worker)
		{
			var thing = ThingMaker.MakeThing(def, stuff);
			var factor = ZombieSettings.Values.contamination.constructionAdd;
			thing.AddContamination(contamination, worker.mapIndexOrState, factor * (1 - ZombieSettings.Values.contamination.constructionTransfer));
			worker.AddContamination(contamination, null, factor * ZombieSettings.Values.contamination.constructionTransfer);
			return thing;
		}

		static void SetGroundContamination(TerrainGrid self, IntVec3 c, float contamination)
		{
			if (contamination > 0)
			{
				var map = self.map;
				var grounds = map.GetContamination();
				grounds.cells[map.cellIndices.CellToIndex(c)] = contamination;
				grounds.SetDirty();
			}
		}

		static void SetTerrain(TerrainGrid self, IntVec3 c, TerrainDef newTerr, float contamination)
		{
			self.SetTerrain(c, newTerr);
			SetGroundContamination(self, c, contamination);
		}

		static void SetFoundation(TerrainGrid self, IntVec3 c, TerrainDef newTerr, float contamination)
		{
			self.SetFoundation(c, newTerr);
			SetGroundContamination(self, c, contamination);
		}

		static void SetTempTerrain(TerrainGrid self, IntVec3 c, TerrainDef newTerr, float contamination)
		{
			self.SetTempTerrain(c, newTerr);
			SetGroundContamination(self, c, contamination);
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
		{
			var sumVar = generator.DeclareLocal(typeof(float));

			var from1 = SymbolExtensions.GetMethodInfo((ThingOwner owner) => owner.ClearAndDestroyContents(DestroyMode.Vanish));
			var to1 = SymbolExtensions.GetMethodInfo(() => ClearAndDestroyContents(default, default));

			var from2 = SymbolExtensions.GetMethodInfo(() => ThingMaker.MakeThing(default, default));
			var to2 = SymbolExtensions.GetMethodInfo(() => MakeThing(default, default, default, default));

			var from3 = SymbolExtensions.GetMethodInfo((TerrainGrid grid) => grid.SetFoundation(default, default));
			var to3 = SymbolExtensions.GetMethodInfo(() => SetFoundation(default, default, default, default));

			var from4 = SymbolExtensions.GetMethodInfo((TerrainGrid grid) => grid.SetTempTerrain(default, default));
			var to4 = SymbolExtensions.GetMethodInfo(() => SetTempTerrain(default, default, default, default));

			var from5 = SymbolExtensions.GetMethodInfo((TerrainGrid grid) => grid.SetTerrain(default, default));
			var to5 = SymbolExtensions.GetMethodInfo(() => SetTerrain(default, default, default, default));

			return new CodeMatcher(instructions)
				 .MatchStartForward(new CodeMatch(operand: from1))
				 .ThrowIfInvalid($"Cannot find {from1.FullDescription()}")
				 .SetOperandAndAdvance(to1)
				 .Insert(Stloc[sumVar])
				 .MatchStartForward(new CodeMatch(operand: from2))
				 .ThrowIfInvalid($"Cannot find {from2.FullDescription()}")
				 .InsertAndAdvance(Ldloc[sumVar], Ldarg_1)
				 .SetInstruction(Call[to2])
				 .MatchStartForward(new CodeMatch(operand: from3))
				 .ThrowIfInvalid($"Cannot find {from3.FullDescription()}")
				 .InsertAndAdvance(Ldloc[sumVar])
				 .SetInstruction(Call[to3])
				 .MatchStartForward(new CodeMatch(operand: from4))
				 .ThrowIfInvalid($"Cannot find {from4.FullDescription()}")
				 .InsertAndAdvance(Ldloc[sumVar])
				 .SetInstruction(Call[to4])
				 .MatchStartForward(new CodeMatch(operand: from5))
				 .ThrowIfInvalid($"Cannot find {from5.FullDescription()}")
				 .InsertAndAdvance(Ldloc[sumVar])
				 .SetInstruction(Call[to5])
				 .InstructionEnumeration();
		}
	}

	[HarmonyPatch(typeof(MinifyUtility), nameof(MinifyUtility.MakeMinified))]
	static class MinifyUtility_MakeMinified_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Prefix(Thing thing, out ReplacementContaminationState __state)
		{
			__state = ReplacementContaminationState.Capture(thing);
		}

		static void Postfix(MinifiedThing __result, Thing thing, ReplacementContaminationState __state)
		{
			__state.Move(thing, __result);
		}
	}

	[HarmonyPatch(typeof(GenConstruct), nameof(GenConstruct.PlaceBlueprintForInstall))]
	static class GenConstruct_PlaceBlueprintForInstall_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Prefix(MinifiedThing itemToInstall, out ReplacementContaminationState __state)
		{
			__state = ReplacementContaminationState.Capture(itemToInstall);
		}

		static void Postfix(Blueprint_Install __result, MinifiedThing itemToInstall, ReplacementContaminationState __state)
		{
			__state.Move(itemToInstall, __result);
		}
	}

	[HarmonyPatch(typeof(GenConstruct), nameof(GenConstruct.PlaceBlueprintForReinstall))]
	static class GenConstruct_PlaceBlueprintForReinstall_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Prefix(Building buildingToReinstall, out ReplacementContaminationState __state)
		{
			__state = ReplacementContaminationState.Capture(buildingToReinstall);
		}

		static void Postfix(Blueprint_Install __result, Building buildingToReinstall, ReplacementContaminationState __state)
		{
			__state.Move(buildingToReinstall, __result);
		}
	}

	[HarmonyPatch(typeof(Blueprint), nameof(Blueprint.TryReplaceWithSolidThing))]
	static class Blueprint_TryReplaceWithSolidThing_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Prefix(Blueprint __instance, out ReplacementContaminationState __state)
		{
			__state = ReplacementContaminationState.Capture(__instance);
		}

		static void Postfix(ref Thing createdThing, Blueprint __instance, ReplacementContaminationState __state)
		{
			__state.Move(__instance, createdThing);
		}
	}

	[HarmonyPatch(typeof(SmoothableWallUtility), nameof(SmoothableWallUtility.SmoothWall))]
	static class SmoothableWallUtility_SmoothWall_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Prefix(Thing target, out float __state)
		{
			__state = target?.GetContamination() ?? 0;
		}

		static void Postfix(Thing __result, float __state)
		{
			if (__result == null)
				return;
			__result.SetContamination(__state);
		}
	}

	[HarmonyPatch(typeof(SmoothableWallUtility), nameof(SmoothableWallUtility.Notify_BuildingDestroying))]
	static class SmoothableWallUtility_Notify_BuildingDestroying_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		sealed class RevertedWall
		{
			public Map map;
			public IntVec3 cell;
			public ThingDef expectedDef;
			public float contamination;
		}

		static void Prefix(Thing t, DestroyMode mode, out List<RevertedWall> __state)
		{
			__state = null;
			var map = t?.Map;
			if (map == null || (mode != DestroyMode.KillFinalize && mode != DestroyMode.Deconstruct) || t.def.IsSmoothed == false)
				return;

			foreach (var direction in GenAdj.CardinalDirections)
			{
				var cell = t.Position + direction;
				if (cell.InBounds(map) == false)
					continue;

				var edifice = cell.GetEdifice(map);
				var unsmoothedDef = edifice?.def?.building?.unsmoothedThing;
				if (edifice == null || edifice.def.IsSmoothed == false || unsmoothedDef == null)
					continue;

				__state ??= new List<RevertedWall>();
				__state.Add(new RevertedWall
				{
					map = map,
					cell = edifice.Position,
					expectedDef = unsmoothedDef,
					contamination = edifice.GetContamination()
				});
			}
		}

		static void Postfix(List<RevertedWall> __state)
		{
			foreach (var wall in __state ?? Enumerable.Empty<RevertedWall>())
			{
				var edifice = wall.cell.GetEdifice(wall.map);
				if (edifice?.def == wall.expectedDef)
					edifice.SetContamination(wall.contamination);
			}
		}
	}

	[HarmonyPatch(typeof(Building_SubcoreScanner), nameof(Building_SubcoreScanner.Tick))]
	static class Building_SubcoreScanner_Tick_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static Thing MakeThing(ThingDef def, ThingDef stuff, Building_SubcoreScanner scanner)
		{
			var result = ThingMaker.MakeThing(def, stuff);
			scanner.TransferContamination(ZombieSettings.Values.contamination.subcoreScannerTransfer, result);
			return result;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> instructions.ExtraArgumentsTranspiler(typeof(ThingMaker), () => MakeThing(default, default, default), new[] { Ldarg_0 }, 1);
	}

	[HarmonyPatch(typeof(Building_GeneExtractor), nameof(Building_GeneExtractor.Finish))]
	static class Building_GeneExtractor_Finish_Patch
	{
		[ThreadStatic] static Pawn containedPawn;

		static bool Prepare() => Constants.CONTAMINATION;

		static void Prefix(Building_GeneExtractor __instance)
			=> containedPawn = __instance.ContainedPawn;

		static void Finalizer()
			=> containedPawn = null;

		static bool TryPlaceThing(Thing thing, IntVec3 center, Map map, ThingPlaceMode mode, Action<Thing, int> placedAction, Predicate<IntVec3> nearPlaceValidator, Rot4? rot, int squareRadius, Building_GeneExtractor extractor)
		{
			containedPawn?.TransferContamination(ZombieSettings.Values.contamination.geneExtractorTransfer, thing);
			return GenPlace.TryPlaceThing(thing, center, map, mode, placedAction, nearPlaceValidator, rot, squareRadius);
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> instructions.ExtraArgumentsTranspiler(typeof(GenPlace), () => TryPlaceThing(default, default, default, default, default, default, default, default, default), new[] { Ldarg_0 }, 1);
	}

	[HarmonyPatch(typeof(Building_NutrientPasteDispenser), nameof(Building_NutrientPasteDispenser.TryDispenseFood))]
	static class Building_NutrientPasteDispenser_TryDispenseFood_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		[ThreadStatic] static List<Thing> activeFeed;

		static void Prefix()
			=> activeFeed = new List<Thing>();

		internal static void NotifySplitOff(Thing thing)
		{
			if (activeFeed != null && thing != null && activeFeed.Contains(thing) == false)
				activeFeed.Add(thing);
		}

		static void Postfix(Building_NutrientPasteDispenser __instance, Thing __result)
		{
			try
			{
				if (__result != null && activeFeed != null)
				{
					var mapIndex = __instance.Map == null ? (sbyte?)null : (sbyte)__instance.Map.Index;
					var factor = ZombieSettings.Values.contamination.dispenseFoodTransfer;
					foreach (var thing in activeFeed)
					{
						__result.AddContamination(thing.GetContamination(), mapIndex, factor);
						thing.ClearContamination();
					}
				}
			}
			finally
			{
				activeFeed = null;
			}
		}
	}

	[HarmonyPatch]
	static class Misc_Building_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

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
				factor = ZombieSettings.Values.contamination.geneAssemblerTransfer;
			else if (building is Building_FermentingBarrel)
				factor = ZombieSettings.Values.contamination.fermentingBarrelTransfer;
			building.TransferContamination(factor, result);
			return result;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> instructions.ExtraArgumentsTranspiler(typeof(ThingMaker), () => MakeThing(default, default, default), new[] { Ldarg_0 }, 1);
	}

	[HarmonyPatch]
	static class JobDriver_Repair_MakeNewToils_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static MethodBase TargetMethod()
		{
			var m_Notify_BuildingRepaired = SymbolExtensions.GetMethodInfo((ListerBuildingsRepairable lister) => lister.Notify_BuildingRepaired(default));
			var type = AccessTools.FirstInner(typeof(JobDriver_Repair), type => type.Name.Contains("DisplayClass"));
			if (type == null)
			{
				Patches.Error("Cannot find RimWorld.JobDriver_Repair MakeNewToils display class");
				return null;
			}
			var method = AccessTools.FirstMethod(type, method => method.CallsMethod(m_Notify_BuildingRepaired));
			if (method == null)
				Patches.Error("Cannot find RimWorld.JobDriver_Repair repair delegate");
			return method;
		}

		public static void Equalize(Pawn pawn, Thing thing)
		{
			if (thing != null)
				ZombieSettings.Values.contamination.repairTransfer.Equalize(pawn, thing);
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
	static class JobDriver_RepairMech_MakeNewToils_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION && TargetMethod() != null;

		static MethodBase TargetMethod()
		{
			var m_RepairTick = SymbolExtensions.GetMethodInfo(() => MechRepairUtility.RepairTick(default));
			var method = AccessTools.FirstMethod(typeof(JobDriver_RepairMech), method => method.CallsMethod(m_RepairTick));
			if (method == null)
				Patches.Error("Cannot find RimWorld.JobDriver_RepairMech repair delegate");
			return method;
		}

		static void RepairTick(Pawn mech, JobDriver_RepairMech jobDriver)
		{
			JobDriver_Repair_MakeNewToils_Patch.Equalize(jobDriver.pawn, mech);
			MechRepairUtility.RepairTick(mech);
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> instructions.ExtraArgumentsTranspiler(typeof(MechRepairUtility), () => RepairTick(default, default), new[] { Ldarg_0 }, 1);
	}
}
