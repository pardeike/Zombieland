using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	static class AlbinoScreamVomit
	{
		const int durationMultiplier = 2;
		static readonly Dictionary<Pawn, int> pendingMultipliers = new();

		public static void Start(Pawn pawn)
		{
			if (pawn?.jobs == null)
				return;

			pendingMultipliers[pawn] = Math.Max(PendingMultiplierFor(pawn), durationMultiplier);
			pawn.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Vomit), JobCondition.InterruptForced, null, true, true);
			TryExtend(pawn.jobs.curDriver as JobDriver_Vomit);
		}

		public static void TryExtend(JobDriver_Vomit driver)
		{
			var pawn = driver?.pawn;
			if (pawn == null || pendingMultipliers.TryGetValue(pawn, out var multiplier) == false)
				return;

			if (driver.ticksLeft <= 0)
				return;

			pendingMultipliers.Remove(pawn);
			driver.ticksLeft = Math.Max(driver.ticksLeft, 300) * Math.Max(1, multiplier);
		}

		static int PendingMultiplierFor(Pawn pawn)
		{
			return pendingMultipliers.TryGetValue(pawn, out var multiplier) ? multiplier : 1;
		}
	}

	[HarmonyPatch]
	static class JobDriver_Vomit_AlbinoScreamDuration_Patch
	{
		static MethodBase TargetMethod()
		{
			var method = AccessTools.Method(typeof(JobDriver_Vomit), "<MakeNewToils>b__4_0");
			if (method == null)
				Patches.Error("Cannot find RimWorld.JobDriver_Vomit init delegate for albino scream duration patch");
			return method;
		}

		static void Postfix(JobDriver_Vomit __instance)
		{
			AlbinoScreamVomit.TryExtend(__instance);
		}
	}
}
