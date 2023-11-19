using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using UnityEngine;
using Verse;
using static HarmonyLib.Code;

namespace ZombieLand
{
	[HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.GenerateContentsIntoMap))]
	static class MapGenerator_GenerateContentsIntoMap_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var list = instructions.ToList();
			var idx = list.FindLastIndex(code => code.opcode == OpCodes.Leave || code.opcode == OpCodes.Leave_S);
			if (idx == -1)
			{
				Log.Error("Cannot find 'leave' instruction in MapGenerator.GenerateContentsIntoMap");
				return instructions;
			}

			var blocks = list[idx].blocks;
			var labels = list[idx].labels;
			list[idx].blocks.Clear();
			list[idx].labels.Clear();

			list.Insert(idx, new CodeInstruction(OpCodes.Ldarg_1));
			list[idx].blocks = blocks;
			list[idx].labels = labels;
			list.Insert(idx + 1, new CodeInstruction(OpCodes.Call, SymbolExtensions.GetMethodInfo(() => AddContamination(default))));

			return list;
		}

		static void AddContamination(Map map)
		{
			var minElevationBase = 0.7f; // default in source code
			var mGenerate = AccessTools.Method(typeof(GenStep_RocksFromGrid), nameof(GenStep_RocksFromGrid.Generate));
			var codes = PatchProcessor.ReadMethodBody(mGenerate).ToArray();
			var idx = codes.FirstIndexOf(code => code.Key == OpCodes.Ldc_R4);
			if (idx >= 0)
				minElevationBase = (float)codes[idx].Value; // replace it with the real value

			var grid = new ContaminationGrid(map);
			var elevation = MapGenerator.Elevation.grid;
			var cellCountAboveBase = elevation.Where(elevation => elevation > minElevationBase).Count();
			if (cellCountAboveBase > 0)
			{
				var p = ZombieSettings.Values.contamination.contaminationElevationPercentage;
				var n = (int)Math.Ceiling(cellCountAboveBase * p);
				var set = new SortedSet<(float, int)>();
				for (int i = 0; i < elevation.Length; i++)
				{
					var val = elevation[i];
					if (val > 0)
					{
						set.Add((val, i));
						if (set.Count > n)
							set.Remove(set.Min);
					}
				}
				var min = set.Min.Item1;
				var max = set.Max.Item1;
				static float easeInOutQuart(float x, float p) => x < 0.5f ? Mathf.Pow(2 * x, p) / 2 : 1 - Mathf.Pow(-2 * x + 2, p) / 2;
				var mapX = map.Size.x;
				var difficultyFactor = GenMath.LerpDoubleClamped(1, 4, 0.25f, 1, ZombieSettings.Values.contaminationBaseFactor);
				foreach (var item in set)
				{
					var cell = CellIndicesUtility.IndexToCell(item.Item2, mapX);
					var f = (item.Item1 - min) / (max - min);
					grid[cell] = easeInOutQuart(f, 4) * difficultyFactor;
				}
			}
			ContaminationManager.Instance.grounds[map.Index] = grid;
		}
	}

	[HarmonyPatch(typeof(Mineable), nameof(Mineable.Destroy))]
	static class Mineable_Destroy_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Prefix(Mineable __instance) => Mineable_TrySpawnYield_Patch.mineableMap = __instance.Map;
		static void Postfix() => Mineable_TrySpawnYield_Patch.mineableMap = null;
	}

	[HarmonyPatch(typeof(Mineable), nameof(Mineable.DestroyMined))]
	static class Mineable_DestroyMined_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Prefix(Mineable __instance) => Mineable_TrySpawnYield_Patch.mineableMap = __instance.Map;
		static void Postfix() => Mineable_TrySpawnYield_Patch.mineableMap = null;
	}

	[HarmonyPatch(typeof(Mineable), nameof(Mineable.TrySpawnYield))]
	static class Mineable_TrySpawnYield_Patch
	{
		public static Map mineableMap;

		static bool Prepare() => Constants.CONTAMINATION;

		static Thing MakeThing(ThingDef def, ThingDef stuff, Mineable mineable)
		{
			var thing = ThingMaker.MakeThing(def, stuff);
			if (mineableMap != null)
			{
				var contamination = mineableMap.GetContamination(mineable.Position);
				var savedMapIndex = thing.mapIndexOrState;
				thing.mapIndexOrState = (sbyte)mineableMap.Index;
				thing.AddContamination(contamination, ZombieSettings.Values.contamination.destroyMineableAdd);
				thing.mapIndexOrState = savedMapIndex;
			}
			return thing;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> instructions.ExtraArgumentsTranspiler(typeof(ThingMaker), () => MakeThing(default, default, default), new[] { Ldarg_0 }, 1);
	}

	[HarmonyPatch(typeof(CompDeepDrill), nameof(CompDeepDrill.TryProducePortion))]
	static class CompDeepDrill_TryProducePortion_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static Thing MakeThing(ThingDef def, ThingDef stuff, CompDeepDrill comp)
		{
			var thing = ThingMaker.MakeThing(def, stuff);
			_ = comp;
			thing.AddContamination(ZombieSettings.Values.contamination.deepDrillAdd);
			return thing;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> instructions.ExtraArgumentsTranspiler(typeof(ThingMaker), () => MakeThing(default, default, default), new[] { Ldarg_0 }, 1);
	}
}