using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
			thing.AddContamination(thing.def.IsPlant ? ContaminationFactors.wildPlant : ContaminationFactors.jelly);
			Log.Warning($"Spawned {thing} at {loc}");
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
}