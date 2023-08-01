using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

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
			if (mineableContamination > 0f)
			{
				thing.AddContamination(mineableContamination);
				Log.Warning($"Yielded {thing} from {mineable}");
			}
			return thing;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> Tools.ExtraThisTranspiler(instructions, typeof(ThingMaker), () => MakeThing(default, default, default));
	}
}