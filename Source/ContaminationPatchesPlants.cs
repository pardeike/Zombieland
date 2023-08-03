﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;
using static HarmonyLib.Code;

namespace ZombieLand
{
	[HarmonyPatch]
	static class GenSpawn_Spawn_Replacement_TestPatch
	{
		static IEnumerable<MethodBase> TargetMethods()
		{
			yield return SymbolExtensions.GetMethodInfo((WildPlantSpawner spawner) => spawner.CheckSpawnWildPlantAt(IntVec3.Zero, 0f, 0f, false));
			yield return SymbolExtensions.GetMethodInfo((TunnelJellySpawner spawner) => spawner.Spawn(null, IntVec3.Zero));
		}

		static Thing Spawn(Thing newThing, IntVec3 loc, Map map, WipeMode wipeMode)
		{
			var thing = GenSpawn.Spawn(newThing, loc, map, wipeMode);
			if (Tools.IsPlaying())
			{
				var contamination = map.GetContamination(loc);
				thing.AddContamination(contamination, () => Log.Warning($"Spawned {thing} at {loc}"), thing.def.IsPlant ? ContaminationFactors.plantAdd : ContaminationFactors.jellyAdd);
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

	[HarmonyPatch]
	static class JobDriver_PlantWork_MakeNewToils_TestPatch
	{
		static readonly MethodInfo m_MakeThing = SymbolExtensions.GetMethodInfo(() => ThingMaker.MakeThing(default, default));
		static MethodBase TargetMethod()
		{
			var type = AccessTools.FirstInner(typeof(JobDriver_PlantWork), type => type.Name.Contains("DisplayClass"));
			return AccessTools.FirstMethod(type, method => method.CallsMethod(m_MakeThing));
		}

		static Thing MakeThing(ThingDef def, ThingDef stuff, Plant plant)
		{
			var result = ThingMaker.MakeThing(def, stuff);
			plant?.TransferContamination(ContaminationFactors.plantTransfer, () => Log.Warning($"Produce {result} from {plant}"), result);
			return result;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var from = m_MakeThing;
			var to = SymbolExtensions.GetMethodInfo(() => MakeThing(default, default, default));

			var matcher = new CodeMatcher(instructions)
				.MatchEndForward(
					new CodeMatch(name: "thing_var"),
					new CodeMatch(Ldfld),
					new CodeMatch(Ldfld),
					new CodeMatch(Ldfld),
					new CodeMatch(Ldnull),
					new CodeMatch(operand: from)
				)
				.ThrowIfInvalid($"Cannot find {from.FullDescription()}");

			return matcher.InsertAndAdvance(matcher.NamedMatch("thing_var"))
				.SetInstruction(Call[to])
				.InstructionEnumeration();
		}
	}

	[HarmonyPatch(typeof(IncidentWorker_AmbrosiaSprout), nameof(IncidentWorker_AmbrosiaSprout.TryExecuteWorker))]
	static class IncidentWorker_AmbrosiaSprout_TryExecuteWorker_TestPatches
	{
		static Thing Spawn(ThingDef def, IntVec3 loc, Map map, WipeMode wipeMode)
		{
			var thing = GenSpawn.Spawn(def, loc, map, wipeMode);
			var contamination = map.GetContamination(loc);
			thing.AddContamination(contamination, () => Log.Warning($"Spawned {thing} at {loc}"), ContaminationFactors.ambrosiaAdd);
			return thing;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> instructions.ExtraArgumentsTranspiler(typeof(GenSpawn), () => Spawn(default, default, default, default), new CodeInstruction[0], 0);
	}

	[HarmonyPatch(typeof(Plant), nameof(Plant.TrySpawnStump))]
	static class Plant_TrySpawnStump_TestPatches
	{
		static Thing Spawn(ThingDef def, IntVec3 loc, Map map, WipeMode wipeMode, Plant plant)
		{
			var result = GenSpawn.Spawn(def, loc, map, wipeMode);
			plant.TransferContamination(ContaminationFactors.stumpTransfer, () => Log.Warning($"Produce {result} from {plant}"), result);
			return result;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> instructions.ExtraArgumentsTranspiler(typeof(GenSpawn), () => Spawn(default, default, default, default, default), new[] { Ldarg_0 }, 1);
	}

	[HarmonyPatch]
	static class JobDriver_PlantSow_MakeNewToils_TestPatch
	{
		static readonly Expression<Action> m_Spawn = () => Spawn(default, default, default, default, default);

		static MethodBase TargetMethod()
		{
			var type = AccessTools.FirstInner(typeof(JobDriver_PlantSow), type => type.Name.Contains("DisplayClass"));
			return Tools.FirstMethodForReplacement(type, typeof(GenSpawn), m_Spawn);
		}

		static Thing Spawn(ThingDef def, IntVec3 loc, Map map, WipeMode wipeMode, JobDriver_PlantSow driver)
		{
			var thing = GenSpawn.Spawn(def, loc, map, wipeMode);
			var pawn = driver.pawn;
			var contamination = map.GetContamination(loc);
			thing.AddContamination(contamination, () => Log.Warning($"Spawned {thing} at {loc}"), ContaminationFactors.sowedPlantAdd);
			ContaminationFactors.sowingPawnEqualize.Equalize(pawn, thing, () => Log.Warning($"{pawn} sowed {thing}"));
			return thing;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> instructions.ExtraArgumentsTranspiler(typeof(GenSpawn), m_Spawn, default, 1, true);
	}
}