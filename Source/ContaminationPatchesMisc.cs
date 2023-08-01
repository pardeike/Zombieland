using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using static HarmonyLib.Code;

namespace ZombieLand
{
	[HarmonyPatch(typeof(Fire), nameof(Fire.DoComplexCalcs))]
	static class Fire_DoComplexCalcs_TestPatch
	{
		static void Postfix(Fire __instance)
		{
			var map = __instance.Map;
			if (map == null)
				return;
			var cell = __instance.Position;
			var instance = ContaminationManager.Instance;
			map.thingGrid.ThingsListAtFast(cell).Do(thing => instance.Subtract(thing, ContaminationFactors.fire));
			var grid = map.GetContamination();
			var oldValue = grid[cell];
			if (oldValue > 0)
				grid[cell] = oldValue - ContaminationFactors.fire;
		}
	}

	[HarmonyPatch(typeof(GenRecipe), nameof(GenRecipe.MakeRecipeProducts))]
	static class GenReciepe_MakeRecipeProducts_TestPatch
	{
		static IEnumerable<Thing> Postfix(IEnumerable<Thing> things, Pawn worker, IBillGiver billGiver, List<Thing> ingredients)
		{
			var thingList = things.ToArray();
			var processorThing = billGiver as Thing;
			ingredients.TransferContamination(ContaminationFactors.receipe, thingList);
			processorThing?.TransferContamination(ContaminationFactors.billGiver, thingList);
			worker?.TransferContamination(ContaminationFactors.worker, thingList);
			Log.Warning($"Produce {thingList.Join(t => $"{t}")}, {worker} on {processorThing} from {ingredients.Join(t => $"{t}")}");
			foreach (var thing in thingList)
				yield return thing;
		}
	}

	[HarmonyPatch(typeof(Thing), nameof(Thing.TryAbsorbStack))]
	static class Thing_TryAbsorbStack_TestPatch
	{
		static void Prefix(Thing other, out (int, float) __state)
		{
			__state = other == null || Tools.MapInitialized() == false ? (0, 0f) : (other.stackCount, other.GetContamination());
		}

		static void Postfix(bool __result, Thing __instance, Thing other, (int, float) __state)
		{
			if (Tools.MapInitialized() == false)
				return;

			var (otherOldStack, otherOldContamination) = __state;
			var otherStack = __result ? 0 : other?.stackCount ?? 0;
			var factor = 1f - otherStack / otherOldStack;
			var transfer = otherOldContamination * factor;
			if (__result == false)
				other?.SubtractContamination(transfer);
			__instance.AddContamination(transfer);
		}
	}

	[HarmonyPatch(typeof(Thing), nameof(Thing.SplitOff))]
	static class Thing_SplitOff_TestPatch
	{
		static void Postfix(Thing __result, Thing __instance, int count)
		{
			if (__result == null || __result == __instance)
				return;
			if (Tools.MapInitialized() == false)
				return;
			
			var previousTotal = __instance.stackCount + count;
			if (previousTotal == 0)
				return;
			
			var factor = count / (float)previousTotal;
			__instance.TransferContamination(factor, __result);
			Log.Warning($"Split off {count}/{previousTotal} ({factor}) from {__instance} to get {__result}");
		}
	}

	[HarmonyPatch(typeof(Thing), nameof(Thing.Ingested))]
	static class Thing_Ingested_TestPatch
	{
		static void IngestedCalculateAmounts(Thing self, Pawn ingester, float nutritionWanted, out int numTaken, out float nutritionIngested)
		{
			var oldStackCount = self.stackCount;

			float totalNutrition = 0f;
			if (self is Plant plant)
				totalNutrition = plant.GetStatValue(StatDefOf.Nutrition);
			if (self is Pawn pawn)
				totalNutrition = FoodUtility.NutritionForEater(ingester, pawn);
			if (self is Corpse corpse)
				totalNutrition = FoodUtility.NutritionForEater(corpse.InnerPawn, self);

			self.IngestedCalculateAmounts(ingester, nutritionWanted, out numTaken, out nutritionIngested);
			var factor = numTaken == 0 ? (totalNutrition == 0 ? 1 : nutritionIngested / totalNutrition) : (oldStackCount == 0 ? 1 : numTaken / (float)oldStackCount);
			self.TransferContamination(factor, ingester);
			Log.Warning($"{ingester} ingested {self}");
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var from = SymbolExtensions.GetMethodInfo((Thing thing, int numToken, float nutritionIngested) => thing.IngestedCalculateAmounts(default, default, out numToken, out nutritionIngested));
			var to = SymbolExtensions.GetMethodInfo((int numToken, float nutritionIngested) => IngestedCalculateAmounts(default, default, default, out numToken, out nutritionIngested));
			return instructions.MethodReplacer(from, to);
		}
	}

	[HarmonyPatch(typeof(MinifiedThing), nameof(MinifiedThing.SplitOff))]
	static class MinifiedThing_SplitOff_TestPatch
	{
		static void Prefix(MinifiedThing __instance, out int __state)
		{
			__state = __instance.stackCount;
		}

		static void Postfix(Thing __result, MinifiedThing __instance, int __state)
		{
			if (__result == __instance)
				return;
			if (Tools.MapInitialized() == false)
				return;

			var remaining = __instance.Spawned == false ? 0 : __instance.stackCount;
			var factor = __state == 0 ? 1f : 1f - remaining / (float)__state;
			__instance.TransferContamination(factor, __result);
			Log.Warning($"Split off {factor} from {__instance} to get {__result}");
		}
	}

	[HarmonyPatch]
	static class ThingComp_MakeThing_TestPatches
	{
		static IEnumerable<MethodBase> TargetMethods()
		{
			yield return SymbolExtensions.GetMethodInfo((CompChangeableProjectile comp) => comp.RemoveShell());
			yield return SymbolExtensions.GetMethodInfo((CompDeepDrill comp) => comp.TryProducePortion(0f, null));
			yield return SymbolExtensions.GetMethodInfo((CompEggLayer comp) => comp.ProduceEgg());
			yield return SymbolExtensions.GetMethodInfo((CompHasGatherableBodyResource comp) => comp.Gathered(default));
			yield return AccessTools.Method(typeof(CompMechCarrier), nameof(CompMechCarrier.PostSpawnSetup));
			yield return SymbolExtensions.GetMethodInfo((CompPlantable comp) => comp.DoPlant(default, default, default));
			yield return SymbolExtensions.GetMethodInfo((CompPollutionPump comp) => comp.Pump());
			yield return AccessTools.Method(typeof(CompRefuelable), nameof(CompRefuelable.PostDestroy));
			yield return SymbolExtensions.GetMethodInfo((CompSpawnerItems comp) => comp.SpawnItems());
			yield return SymbolExtensions.GetMethodInfo((CompSpawner comp) => comp.TryDoSpawn());
			yield return AccessTools.Method(typeof(CompTreeConnection), nameof(CompTreeConnection.CompTick));
			yield return SymbolExtensions.GetMethodInfo((CompWasteProducer comp) => comp.ProduceWaste(0));
		}

		static Thing MakeThing(ThingDef def, ThingDef stuff, ThingComp thingComp)
		{
			var result = ThingMaker.MakeThing(def, stuff);
			if (thingComp?.parent is Thing thing)
			{
				thing.TransferContamination(ContaminationFactors.produce, result);
				Log.Warning($"Produce {result} from {thing}");
			}
			return result;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> instructions.ExtraThisTranspiler(typeof(ThingMaker), () => MakeThing(default, default, default));
	}

	[HarmonyPatch(typeof(ExecutionUtility), nameof(ExecutionUtility.ExecutionInt))]
	[HarmonyPatch(new[] { typeof(Pawn), typeof(Pawn), typeof(bool), typeof(int), typeof(bool) })]
	static class ExecutionUtility_ExecutionInt_TestPatches
	{
		static Thing Spawn(ThingDef def, IntVec3 loc, Map map, WipeMode wipeMode, Pawn victim)
		{
			var result = GenSpawn.Spawn(def, loc, map, wipeMode);
			victim.TransferContamination(ContaminationFactors.produce, result);
			Log.Warning($"Produce {result} from {victim}");
			return result;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase originalMethod)
			=> instructions.ExtraThisTranspiler(typeof(GenSpawn), () => Spawn(default, default, default, default, default));
	}

	[HarmonyPatch(typeof(TendUtility), nameof(TendUtility.DoTend))]
	static class TendUtility_DoTend_TestPatch
	{
		static void Postfix(Pawn doctor, Pawn patient, Medicine medicine)
		{
			if (medicine == null)
				return;
			if (doctor != null && doctor != patient)
			{
				medicine.TransferContamination(0.95f, patient);
				medicine.TransferContamination(0.05f, doctor);
				Log.Warning($"{doctor} tended {patient} with {medicine}");
			}
			else
			{
				medicine.TransferContamination(patient);
				Log.Warning($"Tended {patient} with {medicine}");
			}
		}
	}

	[HarmonyPatch(typeof(Pawn), nameof(Pawn.MakeCorpse))]
	[HarmonyPatch(new[] { typeof(Building_Grave), typeof(bool), typeof(float) } )]
	static class Pawn_MakeCorpse_TestPatch
	{
		static void Postfix(Pawn __instance, Corpse __result)
		{
			__result.AddContamination(__instance.GetContamination());
			Log.Warning($"Copied {__instance} to {__result}");
		}
	}

	[HarmonyPatch]
	static class Jobdriver_ClearPollution_Spawn_TestPatch
	{
		static readonly MethodInfo m_Spawn = SymbolExtensions.GetMethodInfo(() => GenSpawn.Spawn((ThingDef)default, default, default, default));

		static MethodBase TargetMethod()
		{
			return AccessTools.FirstMethod(typeof(JobDriver_ClearPollution), method =>
					PatchProcessor.ReadMethodBody(method).Any(pair => pair.Value is MethodInfo method && method == m_Spawn));
		}

		static Thing Spawn(ThingDef def, IntVec3 loc, Map map, WipeMode wipeMode)
		{
			var thing = GenSpawn.Spawn(def, loc, map, wipeMode);
			var contamination = map.GetContamination(loc);
			thing.AddContamination(contamination, ContaminationFactors.wastePack);
			return thing;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var from = m_Spawn;
			var to = SymbolExtensions.GetMethodInfo(() => Spawn(default, default, default, default));
			return instructions.MethodReplacer(from, to);
		}
	}
}