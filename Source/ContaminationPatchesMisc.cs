using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;
using static HarmonyLib.Code;

namespace ZombieLand
{
	[HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
	static class Pawn_Kill_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Prefix(Pawn __instance)
		{
			if (__instance is Zombie zombie)
				zombie.Map?.AddContamination(zombie.Position, ZombieSettings.Values.contamination.zombieDeathAdd);
		}
	}

	[HarmonyPatch(typeof(Fire), nameof(Fire.DoComplexCalcs))]
	static class Fire_DoComplexCalcs_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Postfix(Fire __instance)
		{
			var map = __instance.Map;
			if (map == null)
				return;
			var cell = __instance.Position;
			var instance = ContaminationManager.Instance;
			map.thingGrid.ThingsListAtFast(cell).Do(thing => instance.Subtract(thing, ZombieSettings.Values.contamination.fireReduction));
			var grid = map.GetContamination();
			var oldValue = grid[cell];
			if (oldValue > 0)
				grid[cell] = oldValue - ZombieSettings.Values.contamination.fireReduction;
		}
	}

	[HarmonyPatch]
	static class Verb_MeleeAttack_ApplyMeleeDamageToTarget_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static IEnumerable<MethodBase> TargetMethods()
			=> Tools.MethodsImplementing((Verb_MeleeAttack verb) => verb.ApplyMeleeDamageToTarget(default));

		static void Postfix(Verb_MeleeAttack __instance, LocalTargetInfo target, DamageWorker.DamageResult __result)
		{
			if (__result.totalDamageDealt <= 0f)
				return;
			var pawn = __instance.Caster;
			var thing = target.Thing;
			ZombieSettings.Values.contamination.meleeEqualize.Equalize(pawn, thing);
		}
	}

	[HarmonyPatch(typeof(GenRecipe), nameof(GenRecipe.MakeRecipeProducts))]
	static class GenReciepe_MakeRecipeProducts_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static IEnumerable<Thing> Postfix(IEnumerable<Thing> things, Pawn worker, IBillGiver billGiver, List<Thing> ingredients)
		{
			if (billGiver is not Thing bench)
				return things;

			var results = things.ToArray();
			var manager = ContaminationManager.Instance;
			var transfer = ingredients.Sum(i => manager.Get(i));
			ingredients.TransferContamination(ZombieSettings.Values.contamination.receipeTransfer, results);
			foreach (var result in results)
				transfer += Mathf.Abs(manager.Equalize(result, bench, ZombieSettings.Values.contamination.produceEqualize));
			transfer += Mathf.Abs(manager.Equalize(bench, worker, ZombieSettings.Values.contamination.benchEqualize));
			worker.TransferContamination(ZombieSettings.Values.contamination.workerTransfer, results);
			//if (transfer > 0)
			//	Log.Warning($"{worker} produces {results.Join(t => $"{t}")} from {ingredients.Join(t => $"{t}")}{(bench != null ? $" on {bench}" : "")}");
			return results;
		}
	}

	[HarmonyPatch(typeof(ThingOwner), nameof(ThingOwner.TryTransferToContainer))]
	[HarmonyPatch([typeof(Thing), typeof(ThingOwner), typeof(int), typeof(Thing), typeof(bool)], [ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Normal, ArgumentType.Out, ArgumentType.Normal])]
	static class ThingOwner_TryTransferToContainer_Patch
	{
		public static Map activeThingOwnerMap;

		static bool Prepare() => Constants.CONTAMINATION;

		static void Prefix(ThingOwner __instance, Thing item, ThingOwner otherContainer)
		{
			activeThingOwnerMap = ThingOwnerUtility.GetRootMap(__instance.owner);
			if (otherContainer.owner is Frame frame && frame.mapIndexOrState >= 0)
			{
				_ = ContaminationManager.TryGetThingMap(frame, activeThingOwnerMap, out var frameMap);
				ContaminationManager.Instance.Set(frame, ContaminationManager.Instance.Get(item, false, frameMap), frameMap);
			}
		}

		static void Postfix()
		{
			activeThingOwnerMap = null;
		}
	}

	[HarmonyPatch]
	static class Pawn_CarryTracker_TryStartCarry_Patch_Patch
	{
		public static Map pawnMap;

		static bool Prepare() => Constants.CONTAMINATION;

		static IEnumerable<MethodBase> TargetMethods()
		{
			var methods = AccessTools.GetDeclaredMethods(typeof(Pawn_CarryTracker))
				.Where(method => method.Name == nameof(Pawn_CarryTracker.TryStartCarry));
			foreach (var method in methods)
				yield return method;
		}

		static void Prefix(Pawn_CarryTracker __instance)
		{
			_ = ContaminationManager.TryGetThingMap(__instance.pawn, null, out pawnMap);
		}

		static void Postfix()
		{
			pawnMap = null;
		}
	}

	[HarmonyPatch(typeof(Thing), nameof(Thing.TryAbsorbStack))]
	static class Thing_TryAbsorbStack_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Prefix(Thing other, out (int, float) __state)
		{
			__state = other == null || Tools.IsPlaying() == false ? (0, 0f) : (other.stackCount, other.GetContamination(includeHoldings: true));
		}

		static void Postfix(bool __result, Thing __instance, Thing other, (int, float) __state)
		{
			if (Tools.IsPlaying() == false || other == null)
				return;

			var (otherOldStackSize, otherContamination) = __state;
			var otherNewStackSize = other.stackCount;
			var otherCount = otherOldStackSize - otherNewStackSize;
			var thisCount = __instance.stackCount - otherCount;
			if (thisCount + otherCount <= 0)
				return;

			var thisContamination = __instance.GetContamination(includeHoldings: true);
			var newContamination = (otherCount * otherContamination + thisCount * thisContamination) / (thisCount + otherCount);
			var transfer = newContamination - thisContamination;

			if (transfer > 0)
				ContaminationManager.Instance.Add(__instance, transfer, Pawn_CarryTracker_TryStartCarry_Patch_Patch.pawnMap);
			if (transfer < 0)
				ContaminationManager.Instance.Subtract(__instance, -transfer, Pawn_CarryTracker_TryStartCarry_Patch_Patch.pawnMap);

			if (__result == false && otherOldStackSize > 0)
			{
				var factor = otherNewStackSize / (float)otherOldStackSize;
				ContaminationManager.Instance.Subtract(other, otherContamination * factor, Pawn_CarryTracker_TryStartCarry_Patch_Patch.pawnMap);
			}
		}
	}

	[HarmonyPatch(typeof(Thing), nameof(Thing.SplitOff))]
	static class Thing_SplitOff_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		internal static void CopyContaminationToSplitOff(Thing result, Thing instance)
		{
			if (result == null || result == instance)
				return;
			if (Tools.IsPlaying() == false)
				return;

			var contextMap = ThingOwner_TryTransferToContainer_Patch.activeThingOwnerMap;
			_ = ContaminationManager.TryGetThingMap(instance, contextMap, out contextMap);
			var contamination = ContaminationManager.Instance.Get(instance, false, contextMap);
			if (contamination == 0)
				return;

			if (Mathf.Abs(ContaminationManager.Instance.Get(result, false, contextMap) - contamination) < ContaminationThresholds.MinimumForCalculations)
				return;

			ContaminationManager.Instance.Set(result, contamination, contextMap);
		}

		static void Postfix(Thing __result, Thing __instance)
		{
			CopyContaminationToSplitOff(__result, __instance);
			Building_NutrientPasteDispenser_TryDispenseFood_Patch.NotifySplitOff(__result);
		}
	}

	[HarmonyPatch(typeof(ThingWithComps), nameof(ThingWithComps.SplitOff))]
	static class ThingWithComps_SplitOff_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Postfix(Thing __result, ThingWithComps __instance)
		{
			Thing_SplitOff_Patch.CopyContaminationToSplitOff(__result, __instance);
			Building_NutrientPasteDispenser_TryDispenseFood_Patch.NotifySplitOff(__result);
		}
	}

	[HarmonyPatch(typeof(Thing), nameof(Thing.Ingested))]
	static class Thing_Ingested_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void IngestedCalculateAmounts(Thing self, Pawn ingester, float nutritionWanted, out int numTaken, out float nutritionIngested)
		{
			var oldStackCount = self.stackCount;
			if (oldStackCount <= 0)
			{
				numTaken = 0;
				nutritionIngested = 0f;
				return;
			}

			float totalNutrition = 0f;
			if (self is Plant plant)
				totalNutrition = plant.GetStatValue(StatDefOf.Nutrition);
			if (self is Pawn pawn)
				totalNutrition = FoodUtility.NutritionForEater(ingester, pawn);
			if (self is Corpse corpse)
				totalNutrition = FoodUtility.NutritionForEater(corpse.InnerPawn, self);

			self.IngestedCalculateAmounts(ingester, nutritionWanted, out numTaken, out nutritionIngested);
			var factor = numTaken == 0 ? (totalNutrition == 0 ? 1 : nutritionIngested / totalNutrition) : (oldStackCount == 0 ? 1 : numTaken / (float)oldStackCount);
			if (factor <= 0f)
				return;
			self.TransferContamination(ZombieSettings.Values.contamination.ingestTransfer * factor, ingester);
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var from = SymbolExtensions.GetMethodInfo((Thing thing, int numToken, float nutritionIngested) => thing.IngestedCalculateAmounts(default, default, out numToken, out nutritionIngested));
			var to = SymbolExtensions.GetMethodInfo((int numToken, float nutritionIngested) => IngestedCalculateAmounts(default, default, default, out numToken, out nutritionIngested));
			return instructions.MethodReplacer(from, to);
		}
	}

	[HarmonyPatch(typeof(MinifiedThing), nameof(MinifiedThing.SplitOff))]
	static class MinifiedThing_SplitOff_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Prefix(MinifiedThing __instance, out int __state) => __state = __instance.stackCount;

		static void Postfix(Thing __result, MinifiedThing __instance, int __state)
		{
			if (__result == __instance)
				return;
			if (Tools.IsPlaying() == false)
				return;

			var remaining = __instance.Spawned == false ? 0 : __instance.stackCount;
			var factor = __state == 0 ? 1f : 1f - remaining / (float)__state;
			// Thing.SplitOff's generic postfix already copied the source contamination.
			__result.ClearContamination();
			__instance.TransferContamination(factor, __result);
		}
	}

	[HarmonyPatch]
	static class ThingComp_MakeThing_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static IEnumerable<MethodBase> TargetMethods()
		{
			var makeThing = SymbolExtensions.GetMethodInfo(() => ThingMaker.MakeThing(default, default));
			var candidates = new MethodBase[]
			{
				SymbolExtensions.GetMethodInfo((CompChangeableProjectile comp) => comp.RemoveShell()),
				SymbolExtensions.GetMethodInfo((CompEggLayer comp) => comp.ProduceEgg()),
				SymbolExtensions.GetMethodInfo((CompHasGatherableBodyResource comp) => comp.Gathered(default)),
				AccessTools.Method(typeof(CompMechCarrier), nameof(CompMechCarrier.PostSpawnSetup)),
				SymbolExtensions.GetMethodInfo((CompPlantable comp) => comp.DoPlant(default, default, default)),
				SymbolExtensions.GetMethodInfo((CompPollutionPump comp) => comp.Pump()),
				AccessTools.Method(typeof(CompRefuelable), nameof(CompRefuelable.PostDestroy)),
				SymbolExtensions.GetMethodInfo((CompSpawnerItems comp) => comp.SpawnItems()),
				SymbolExtensions.GetMethodInfo((CompSpawner comp) => comp.TryDoSpawn()),
				AccessTools.Method(typeof(CompTreeConnection), nameof(CompTreeConnection.CompTick)),
				SymbolExtensions.GetMethodInfo((CompWasteProducer comp) => comp.ProduceWaste(0))
			};
			foreach (var method in candidates)
				if (method?.CallsMethod(makeThing) == true)
					yield return method;
		}

		static Thing MakeThing(ThingDef def, ThingDef stuff, ThingComp thingComp)
		{
			var result = ThingMaker.MakeThing(def, stuff);
			if (thingComp?.parent is Thing thing)
			{
				thing.TransferContamination(ZombieSettings.Values.contamination.generalTransfer, result);
			}
			return result;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> instructions.ExtraArgumentsTranspiler(typeof(ThingMaker), () => MakeThing(default, default, default), new[] { Ldarg_0 }, 1);
	}

	[HarmonyPatch(typeof(ExecutionUtility), nameof(ExecutionUtility.ExecutionInt))]
	[HarmonyPatch(new[] { typeof(Pawn), typeof(Pawn), typeof(bool), typeof(int), typeof(bool) })]
	static class ExecutionUtility_ExecutionInt_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Prefix(Pawn victim) => Filth_MakeThing_Patch.filthSource = victim;
		static void Postfix() => Filth_MakeThing_Patch.filthSource = null;
	}

	[HarmonyPatch(typeof(TendUtility), nameof(TendUtility.DoTend))]
	static class TendUtility_DoTend_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Postfix(Pawn doctor, Pawn patient, Medicine medicine)
		{
			if (doctor == null || patient == null)
				return;
			var manager = ContaminationManager.Instance;
			if (medicine != null)
			{
				manager.Transfer(medicine, ZombieSettings.Values.contamination.medicineTransfer, new[] { patient });
				if (doctor != patient)
					manager.Transfer(medicine, ZombieSettings.Values.contamination.medicineTransfer, new[] { doctor });
			}
			if (doctor != patient)
			{
				var medicineSkill = doctor.skills.GetSkill(SkillDefOf.Medicine).Level;
				var weight = GenMath.LerpDoubleClamped(0, 20, ZombieSettings.Values.contamination.tendEqualizeWorst, ZombieSettings.Values.contamination.tendEqualizeBest, medicineSkill);
				manager.Equalize(doctor, patient, weight);
			}
		}
	}

	[HarmonyPatch(typeof(PawnUtility), nameof(PawnUtility.GainComfortFromCellIfPossible))]
	[HarmonyPatch(new[] { typeof(Pawn), typeof(int), typeof(bool) })]
	static class PawnUtility_GainComfortFromCellIfPossible_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Postfix(Pawn p)
		{
			var tick = p.thingIDNumber % 1000;
			if (Find.TickManager.TicksGame % 1000 != tick)
				return;

			var cell = p.Position;
			ZombieSettings.Values.contamination.restEqualize.Equalize(p, cell);
			var edifice = cell.GetEdifice(p.Map);
			if (edifice != null)
				ZombieSettings.Values.contamination.restEqualize.Equalize(p, edifice);
		}
	}

	[HarmonyPatch(typeof(Pawn_CarryTracker), nameof(Pawn_CarryTracker.CarryHandsTickInterval))]
	static class Pawn_CarryTracker_CarryHandsTickInterval_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Postfix(Pawn_CarryTracker __instance)
		{
			var pawn = __instance.pawn;

			var tick = pawn.thingIDNumber % 900;
			if (Find.TickManager.TicksGame % 900 != tick)
				return;

			var thing = __instance.CarriedThing;
			if (thing == null)
				return;
			ZombieSettings.Values.contamination.carryEqualize.Equalize(pawn, thing, false, true);
		}
	}

	[HarmonyPatch(typeof(PawnUtility), nameof(PawnUtility.Mated))]
	static class PawnUtility_Mated_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Postfix(Pawn male, Pawn female)
		{
			0.5f.Equalize(male, female);
		}
	}

	[HarmonyPatch(typeof(JobDriver_Lovin), nameof(JobDriver_Lovin.MakeNewToils))]
	static class JobDriver_Lovin_MakeNewToils_Patch
	{
		static readonly string layDownToilName = Toils_LayDown.LayDown(default, default, default).debugName;

		static bool Prepare() => Constants.CONTAMINATION;

		static IEnumerable<Toil> Postfix(IEnumerable<Toil> toils, JobDriver_Lovin __instance)
		{
			foreach (var toil in toils)
			{
				if (toil.debugName == layDownToilName && toil.initAction != null)
				{
					var action = toil.initAction;
					toil.initAction = () =>
					{
						if (__instance.ticksLeft <= 25000)
						{
							var p1 = __instance.pawn;
							var p2 = __instance.Partner;
							0.1f.Equalize(p1, p2);
						}
						action();
					};
				}
				yield return toil;
			}
		}
	}

	[HarmonyPatch(typeof(Corpse), nameof(Corpse.InnerPawn), MethodType.Setter)]
	static class Corpse_InnerPawn_Setter_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;
		static void Postfix(Corpse __instance, Pawn value)
		{
			if (Current.Game.World != null && value != null)
				__instance.SetContamination(value.GetContamination());
		}
	}

	[HarmonyPatch]
	static class Jobdriver_ClearPollution_Spawn_Patch
	{
		static readonly MethodInfo m_Spawn = SymbolExtensions.GetMethodInfo(() => GenSpawn.Spawn((ThingDef)default, default, default, default));

		static bool Prepare() => Constants.CONTAMINATION;

		static MethodBase TargetMethod()
		{
			var method = AccessTools.FirstMethod(typeof(JobDriver_ClearPollution), method => method.CallsMethod(m_Spawn));
			if (method == null)
				Patches.Error("Cannot find RimWorld.JobDriver_ClearPollution wastepack spawn delegate");
			return method;
		}

		static Thing Spawn(ThingDef def, IntVec3 loc, Map map, WipeMode wipeMode, JobDriver_ClearPollution driver)
		{
			var thing = GenSpawn.Spawn(def, loc, map, wipeMode);
			var contamination = map.GetContamination(driver.job.targetA.Cell);
			thing.AddContamination(contamination, null, ZombieSettings.Values.contamination.wastePackAdd);
			return thing;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> instructions.ExtraArgumentsTranspiler(typeof(GenSpawn), () => Spawn(default, default, default, default, default), new[] { Ldarg_0 }, 1);
	}

	[HarmonyPatch]
	static class MedicalRecipesUtility_GenSpawn_Spawn_Patches
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static IEnumerable<MethodBase> TargetMethods()
		{
			yield return SymbolExtensions.GetMethodInfo(() => MedicalRecipesUtility.SpawnNaturalPartIfClean(default, default, default, default));
			yield return SymbolExtensions.GetMethodInfo(() => MedicalRecipesUtility.SpawnThingsFromHediffs(default, default, default, default));
		}

		static Thing Spawn(ThingDef def, IntVec3 loc, Map map, WipeMode wipeMode, Pawn pawn)
		{
			var result = GenSpawn.Spawn(def, loc, map, wipeMode);
			pawn.TransferContamination(ZombieSettings.Values.contamination.generalTransfer, result);
			return result;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> instructions.ExtraArgumentsTranspiler(typeof(GenSpawn), () => Spawn(default, default, default, default, default), new[] { Ldarg_0 }, 1);
	}

	[HarmonyPatch(typeof(Recipe_RemoveImplant), nameof(Recipe_RemoveImplant.ApplyOnPawn))]
	static class Recipe_RemoveImplant_ApplyOnPawn_Patches
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static Thing Spawn(ThingDef def, IntVec3 loc, Map map, WipeMode wipeMode, Pawn pawn)
		{
			var result = GenSpawn.Spawn(def, loc, map, wipeMode);
			pawn.TransferContamination(ZombieSettings.Values.contamination.generalTransfer, result);
			return result;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> instructions.ExtraArgumentsTranspiler(typeof(GenSpawn), () => Spawn(default, default, default, default, default), new[] { Ldarg_1 }, 1);
	}

	[HarmonyPatch(typeof(CompLifespan), nameof(CompLifespan.Expire))]
	static class CompLifespan_Expire_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static Thing Spawn(ThingDef def, IntVec3 loc, Map map, WipeMode wipeMode, CompLifespan comp)
		{
			var result = GenSpawn.Spawn(def, loc, map, wipeMode);
			comp.parent.TransferContamination(ZombieSettings.Values.contamination.generalTransfer, result);
			return result;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> instructions.ExtraArgumentsTranspiler(typeof(GenSpawn), () => Spawn(default, default, default, default, default), new[] { Ldarg_0 }, 1);
	}

	[HarmonyPatch(typeof(RoofCollapserImmediate), nameof(RoofCollapserImmediate.DropRoofInCellPhaseOne))]
	static class RoofCollapserImmediate_DropRoofInCellPhaseOne_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static Thing Spawn(ThingDef def, IntVec3 loc, Map map, WipeMode wipeMode, IntVec3 c)
		{
			var contamination = map.GetContamination(c);
			var result = GenSpawn.Spawn(def, loc, map, wipeMode);
			result.SetContamination(contamination);
			return result;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> instructions.ExtraArgumentsTranspiler(typeof(GenSpawn), () => Spawn(default, default, default, default, default), new[] { Ldarg_0 }, 1);
	}

	[HarmonyPatch]
	static class JobDriver_AffectFloor_MakeNewToils_Patch
	{
		static readonly MethodInfo m_DoEffect = SymbolExtensions.GetMethodInfo((JobDriver_AffectFloor jobdriver) => jobdriver.DoEffect(default));

		static bool Prepare() => Constants.CONTAMINATION;

		static MethodBase TargetMethod()
		{
			var type = AccessTools.FirstInner(typeof(JobDriver_AffectFloor), type => type.Name.Contains("DisplayClass"));
			if (type == null)
			{
				Patches.Error("Cannot find RimWorld.JobDriver_AffectFloor MakeNewToils display class");
				return null;
			}

			var method = AccessTools.FirstMethod(type, method => method.CallsMethod(m_DoEffect));
			if (method == null)
				Patches.Error("Cannot find RimWorld.JobDriver_AffectFloor effect delegate");
			return method;
		}

		static void DoEffect(JobDriver_AffectFloor self, IntVec3 c)
		{
			var contamination = self.Map.GetContamination(c);
			self.pawn.AddContamination(contamination, null, ZombieSettings.Values.contamination.floorAdd);
			self.DoEffect(c);
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var replacement = SymbolExtensions.GetMethodInfo(() => DoEffect(default, default));
			return instructions.MethodReplacer(m_DoEffect, replacement);
		}
	}

	[HarmonyPatch]
	static class JobDriver_DisassembleMech_MakeNewToils_Patch
	{
		static readonly MethodInfo m_TryPlaceThing = SymbolExtensions.GetMethodInfo(() => GenPlace.TryPlaceThing(default, default, default, default, default, default, default));

		static bool Prepare() => Constants.CONTAMINATION;

		static MethodBase TargetMethod()
		{
			var method = AccessTools.FirstMethod(typeof(JobDriver_DisassembleMech), method => method.CallsMethod(m_TryPlaceThing));
			if (method == null)
				Patches.Error("Cannot find RimWorld.JobDriver_DisassembleMech product placement delegate");
			return method;
		}

		static bool TryPlaceThing(Thing thing, IntVec3 center, Map map, ThingPlaceMode mode, Action<Thing, int> placedAction, Predicate<IntVec3> nearPlaceValidator, Rot4? rot, int squareRadius, JobDriver_DisassembleMech driver)
		{
			var pawn = driver.pawn;
			var mech = driver.Mech;
			mech.TransferContamination(ZombieSettings.Values.contamination.disassembleTransfer, pawn, thing);
			return GenPlace.TryPlaceThing(thing, center, map, mode, placedAction, nearPlaceValidator, rot, squareRadius);
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> instructions.ExtraArgumentsTranspiler(typeof(GenPlace), () => TryPlaceThing(default, default, default, default, default, default, default, default, default), new[] { Ldarg_0 }, 1);
	}
}
