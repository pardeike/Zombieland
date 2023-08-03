﻿using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using static HarmonyLib.Code;

namespace ZombieLand
{
	[HarmonyPatch(typeof(Mineable), nameof(Mineable.Destroy))]
	static class Mineable_Destroy_TestPatches
	{
		static void Prefix(Mineable __instance) => Mineable_TrySpawnYield_TestPatch.mineableContamination = __instance.GetContamination();
		static void Postfix() => Mineable_TrySpawnYield_TestPatch.mineableContamination = 0f;
	}

	[HarmonyPatch(typeof(Mineable), nameof(Mineable.DestroyMined))]
	static class Mineable_DestroyMined_TestPatches
	{
		static void Prefix(Mineable __instance) => Mineable_TrySpawnYield_TestPatch.mineableContamination = __instance.GetContamination();
		static void Postfix() => Mineable_TrySpawnYield_TestPatch.mineableContamination = 0f;
	}

	[HarmonyPatch(typeof(Mineable), nameof(Mineable.TrySpawnYield))]
	static class Mineable_TrySpawnYield_TestPatch
	{
		public static float mineableContamination = 0f;

		static Thing MakeThing(ThingDef def, ThingDef stuff, Mineable mineable)
		{
			var thing = ThingMaker.MakeThing(def, stuff);
			thing.AddContamination(mineableContamination, () => Log.Warning($"Yielded {thing} from {mineable}"), ContaminationFactors.destroyMineableAdd);
			return thing;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> instructions.ExtraArgumentsTranspiler(typeof(ThingMaker), () => MakeThing(default, default, default), new[] { Ldarg_0 }, 1);
	}
}