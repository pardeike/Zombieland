using HarmonyLib;
using RimWorld;
using System;
using System.Reflection;
using Verse;

namespace ZombieLand
{
	internal static class StorytellerEventFilterSupport
	{
		static readonly Type[] safeDropSpotSignature =
		[
			typeof(IntVec3),
			typeof(Map),
			typeof(Faction),
			typeof(IntVec2?),
			typeof(int),
			typeof(int),
			typeof(int),
			typeof(IntVec3?)
		];

		internal static MethodInfo SafeDropSpotMethod()
		{
			return AccessTools.Method(typeof(DropCellFinder), "IsSafeDropSpot", safeDropSpotSignature);
		}

		internal static int StoryDangerRank(StoryDanger danger)
		{
			return danger switch
			{
				StoryDanger.High => 2,
				StoryDanger.Low => 1,
				_ => 0,
			};
		}
	}
}
