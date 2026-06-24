using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using Verse;
using static HarmonyLib.Code;

namespace ZombieLand
{
	[HarmonyPatch]
	static class GenSpawn_Spawn_Replacement_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static IEnumerable<MethodBase> TargetMethods()
		{
			var method = AccessTools.Method(typeof(TunnelJellySpawner), "Spawn", new[] { typeof(Map), typeof(IntVec3) });
			if (method == null)
			{
				Patches.Error("Cannot find RimWorld.TunnelJellySpawner protected spawn method");
				yield break;
			}
			yield return method;
		}

		static Thing Spawn(Thing newThing, IntVec3 loc, Map map, WipeMode wipeMode)
		{
			var thing = GenSpawn.Spawn(newThing, loc, map, wipeMode);
			if (Tools.IsPlaying())
			{
				var contamination = map.GetContamination(loc);
				var factor = thing.def.IsPlant ? ZombieSettings.Values.contamination.plantAdd : ZombieSettings.Values.contamination.jellyAdd;
				thing.AddContamination(contamination, null, factor);
			}
			return thing;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var from = SymbolExtensions.GetMethodInfo(() => GenSpawn.Spawn((Thing)null, IntVec3.Zero, null, WipeMode.Vanish));
			var to = SymbolExtensions.GetMethodInfo(() => Spawn(null, IntVec3.Zero, null, WipeMode.Vanish));
			return instructions.MethodReplacer(from, to);
		}
	}

	[HarmonyPatch(typeof(WildPlantSpawner), nameof(WildPlantSpawner.SpawnPlant))]
	static class WildPlantSpawner_SpawnPlant_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Postfix(Plant __result, Map map, IntVec3 cell)
		{
			if (Tools.IsPlaying() == false || __result == null || map == null)
				return;
			var contamination = map.GetContamination(cell);
			__result.AddContamination(contamination, null, ZombieSettings.Values.contamination.plantAdd);
		}
	}

	[HarmonyPatch]
	static class JobDriver_PlantWork_MakeNewToils_Patch
	{
		static readonly MethodInfo m_MakeThing = SymbolExtensions.GetMethodInfo(() => ThingMaker.MakeThing(default, default));
		static FieldInfo f_JobDriver;
		static Plant activeHarvestPlant;

		static bool Prepare() => Constants.CONTAMINATION;

		static MethodBase TargetMethod()
		{
			var type = AccessTools.FirstInner(typeof(JobDriver_PlantWork), type => type.Name.Contains("DisplayClass"));
			if (type == null)
			{
				Patches.Error("Cannot find RimWorld.JobDriver_PlantWork MakeNewToils display class");
				return null;
			}

			f_JobDriver = AccessTools.Field(type, "<>4__this");
			if (f_JobDriver == null)
			{
				Patches.Error("Cannot find RimWorld.JobDriver_PlantWork display class driver field");
				return null;
			}

			var method = AccessTools.FirstMethod(type, method => method.CallsMethod(m_MakeThing));
			if (method == null)
				Patches.Error("Cannot find RimWorld.JobDriver_PlantWork harvest product delegate");
			return method;
		}

		static void Prefix(object __instance)
		{
			var jobDriver = f_JobDriver?.GetValue(__instance) as JobDriver_PlantWork;
			activeHarvestPlant = jobDriver?.Plant;
		}

		static void Postfix() => activeHarvestPlant = null;

		static void Finalizer() => activeHarvestPlant = null;

		internal static void TransferHarvestContamination(Thing thing)
			=> activeHarvestPlant?.TransferContamination(ZombieSettings.Values.contamination.plantTransfer, thing);
	}

	[HarmonyPatch(typeof(ThingMaker), nameof(ThingMaker.MakeThing))]
	[HarmonyPatch(new[] { typeof(ThingDef), typeof(ThingDef) })]
	static class ThingMaker_MakeThing_PlantHarvestContext_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Postfix(Thing __result)
			=> JobDriver_PlantWork_MakeNewToils_Patch.TransferHarvestContamination(__result);
	}

	[HarmonyPatch(typeof(IncidentWorker_AmbrosiaSprout), nameof(IncidentWorker_AmbrosiaSprout.TryExecuteWorker))]
	static class IncidentWorker_AmbrosiaSprout_TryExecuteWorker_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static Thing Spawn(ThingDef def, IntVec3 loc, Map map, WipeMode wipeMode)
		{
			var thing = GenSpawn.Spawn(def, loc, map, wipeMode);
			var contamination = map.GetContamination(loc);
			thing.AddContamination(contamination, null, ZombieSettings.Values.contamination.ambrosiaAdd);
			return thing;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> instructions.ExtraArgumentsTranspiler(typeof(GenSpawn), () => Spawn(default, default, default, default), new CodeInstruction[0], 0);
	}

	[HarmonyPatch(typeof(Plant), nameof(Plant.TrySpawnStump))]
	static class Plant_TrySpawnStump_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static Thing Spawn(ThingDef def, IntVec3 loc, Map map, WipeMode wipeMode, Plant plant)
		{
			var result = GenSpawn.Spawn(def, loc, map, wipeMode);
			plant.TransferContamination(ZombieSettings.Values.contamination.stumpTransfer, result);
			return result;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> instructions.ExtraArgumentsTranspiler(typeof(GenSpawn), () => Spawn(default, default, default, default, default), new[] { Ldarg_0 }, 1);
	}

	[HarmonyPatch]
	static class JobDriver_PlantSow_MakeNewToils_Patch
	{
		static readonly Expression<Action> m_Spawn = () => Spawn(default, default, default, default, default);

		static bool Prepare() => Constants.CONTAMINATION;

		static MethodBase TargetMethod()
		{
			var type = AccessTools.FirstInner(typeof(JobDriver_PlantSow), type => type.Name.Contains("DisplayClass"));
			if (type == null)
			{
				Patches.Error("Cannot find RimWorld.JobDriver_PlantSow MakeNewToils display class");
				return null;
			}

			var method = Tools.FirstMethodForReplacement(type, typeof(GenSpawn), m_Spawn);
			if (method == null)
				Patches.Error("Cannot find RimWorld.JobDriver_PlantSow spawn delegate");
			return method;
		}

		static Thing Spawn(ThingDef def, IntVec3 loc, Map map, WipeMode wipeMode, JobDriver_PlantSow driver)
		{
			var thing = GenSpawn.Spawn(def, loc, map, wipeMode);
			var pawn = driver.pawn;
			var contamination = map.GetContamination(loc);
			thing.AddContamination(contamination, null, ZombieSettings.Values.contamination.sowedPlantAdd);
			ZombieSettings.Values.contamination.sowingPawnEqualize.Equalize(pawn, thing);
			return thing;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> instructions.ExtraArgumentsTranspiler(typeof(GenSpawn), m_Spawn, default, 1, true);
	}
}
