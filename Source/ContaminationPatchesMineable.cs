using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using static HarmonyLib.Code;

namespace ZombieLand
{
	[HarmonyPatch(typeof(GenStep_Terrain), nameof(GenStep_Terrain.Generate))]
	static class RockNoises_Reset_TestPatches
	{
		static void Postfix(Map map)
		{
			var grid = new ContaminationGrid(map);
			var elevation = MapGenerator.Elevation;
			var allCells = map.AllCells.ToArray();
			var lowerBound = allCells.Max(cell => elevation[cell]) - ContaminationFactors.contaminationElevationDelta;
			var cCells = allCells
				.Where(cell => elevation[cell] >= lowerBound)
				.Select(cell => (cell, val: elevation[cell]))
				.ToArray();
			var (min, max) = (cCells.Min(c => c.val), cCells.Max(c => c.val));
			static float easeInOutQuart(float x, float p) => x < 0.5f ? Mathf.Pow(2 * x, p) / 2 : 1 - Mathf.Pow(-2 * x + 2, p) / 2;
			foreach (var (cell, level) in cCells)
			{
				var f = (level - min) / (max - min);
				grid[cell] = easeInOutQuart(f, 4);
			}
			ContaminationManager.Instance.grounds[map.Index] = grid;
		}
	}

	[HarmonyPatch(typeof(Mineable), nameof(Mineable.Destroy))]
	static class Mineable_Destroy_TestPatches
	{
		static void Prefix(Mineable __instance) => Mineable_TrySpawnYield_TestPatch.mineableMap = __instance.Map;
		static void Postfix() => Mineable_TrySpawnYield_TestPatch.mineableMap = null;
	}

	[HarmonyPatch(typeof(Mineable), nameof(Mineable.DestroyMined))]
	static class Mineable_DestroyMined_TestPatches
	{
		static void Prefix(Mineable __instance) => Mineable_TrySpawnYield_TestPatch.mineableMap = __instance.Map;
		static void Postfix() => Mineable_TrySpawnYield_TestPatch.mineableMap = null;
	}

	[HarmonyPatch(typeof(Mineable), nameof(Mineable.TrySpawnYield))]
	static class Mineable_TrySpawnYield_TestPatch
	{
		public static Map mineableMap;

		static Thing MakeThing(ThingDef def, ThingDef stuff, Mineable mineable)
		{
			var thing = ThingMaker.MakeThing(def, stuff);
			var contamination = mineableMap?.ExtractContamination(mineable.Position) ?? 0;
			thing.AddContamination(contamination, () => Log.Warning($"Yielded {thing} from {mineable}"), ContaminationFactors.destroyMineableAdd);
			return thing;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> instructions.ExtraArgumentsTranspiler(typeof(ThingMaker), () => MakeThing(default, default, default), new[] { Ldarg_0 }, 1);
	}

	[HarmonyPatch(typeof(CompDeepDrill), nameof(CompDeepDrill.TryProducePortion))]
	static class CompDeepDrill_TryProducePortion_TestPatches
	{
		static Thing MakeThing(ThingDef def, ThingDef stuff, CompDeepDrill comp)
		{
			var thing = ThingMaker.MakeThing(def, stuff);
			thing.AddContamination(ContaminationFactors.deepDrillAdd, () => Log.Warning($"Deep drill produced {thing} at {comp.parent.InteractionCell}"));
			return thing;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> instructions.ExtraArgumentsTranspiler(typeof(ThingMaker), () => MakeThing(default, default, default), new[] { Ldarg_0 }, 1);
	}
}