using System;
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
			if (Tools.MapInitialized())
			{
				var contamination = map.GetContamination(loc);
				thing.AddContamination(contamination, thing.def.IsPlant ? ContaminationFactors.plant : ContaminationFactors.jelly);
				Log.Warning($"Spawned {thing} at {loc}");
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
			return AccessTools.FirstMethod(type, method =>
					PatchProcessor.ReadMethodBody(method).Any(pair => pair.Value is MethodInfo method && method == m_MakeThing));
		}

		static Thing MakeThing(ThingDef def, ThingDef stuff, Plant plant)
		{
			var result = ThingMaker.MakeThing(def, stuff);
			if (plant != null)
			{
				plant.TransferContamination(ContaminationFactors.plant, result);
				Log.Warning($"Produce {result} from {plant}");
			}
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
			thing.AddContamination(contamination, ContaminationFactors.plant);
			Log.Warning($"Spawned {thing} at {loc}");
			return thing;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> instructions.ReplaceTranspiler(typeof(GenSpawn), () => Spawn(default, default, default, default));
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
			var contamination = map.GetContamination(loc);
			thing.AddContamination(contamination, ContaminationFactors.sowedPlant);
			driver.pawn.TransferContamination(ContaminationFactors.sowingPawn, thing);
			Log.Warning($"Spawned {thing} at {loc}");
			return thing;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> instructions.ExtraThisTranspiler(typeof(GenSpawn), m_Spawn, true);
	}
}