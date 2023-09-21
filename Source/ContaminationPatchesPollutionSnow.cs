using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Reflection;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	[HarmonyPatch(typeof(JobDriver_ClearPollution), nameof(JobDriver_ClearPollution.MakeNewToils))]
	static class JobDriver_ClearPollution_ClearPollutionAt_TestPatch
	{
		static bool Prepare() => Constants.CONTAMINATION > 0;

		static IEnumerable<Toil> Postfix(IEnumerable<Toil> toils, JobDriver_ClearPollution __instance)
		{
			foreach (var toil in toils)
			{
				GridUtility_Unpollute_TestPatch.subject = __instance.pawn;
				yield return toil;
			}
			GridUtility_Unpollute_TestPatch.subject = null;
		}
	}

	[HarmonyPatch(typeof(GridsUtility), nameof(GridsUtility.Unpollute))]
	static class GridUtility_Unpollute_TestPatch
	{
		public static Thing subject = null;

		static bool Prepare() => Constants.CONTAMINATION > 0;

		static void Postfix(IntVec3 c, Map map)
		{
			var contamination = map.GetContamination(c);
			subject?.AddContamination(contamination, null/*() => Log.Warning($"{subject} cleaned pollution at {c}")*/, ZombieSettings.Values.contamination.pollutionAdd);
		}
	}

	[HarmonyPatch]
	static class JobDriver_ClearSnow_MakeNewToils_TestPatch
	{
		static bool Prepare() => Constants.CONTAMINATION > 0;

		static MethodBase TargetMethod()
		{
			var m_SetDepth = SymbolExtensions.GetMethodInfo((SnowGrid grid) => grid.SetDepth(default, default));
			var type = AccessTools.FirstInner(typeof(JobDriver_ClearSnow), type => type.Name.Contains("DisplayClass"));
			return AccessTools.FirstMethod(type, method => method.CallsMethod(m_SetDepth));
		}

		static void SetDepth(SnowGrid self, IntVec3 c, float newDepth, Toil toil)
		{
			var contamination = self.map.GetContamination(c);
			var pawn = toil.actor;
			pawn.AddContamination(contamination, null/*() => Log.Warning($"{pawn} cleaned snow at {c}")*/, ZombieSettings.Values.contamination.snowAdd);
			self.SetDepth(c, newDepth);
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> instructions.ExtraArgumentsTranspiler(typeof(SnowGrid), () => SetDepth(default, default, default, default), default, 1, true);
	}
}