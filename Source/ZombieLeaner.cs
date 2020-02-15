using HarmonyLib;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	[HarmonyPatch(typeof(Pawn_DrawTracker))]
	[HarmonyPatch("DrawPos", MethodType.Getter)]
	static class Pawn_DrawTracker_DrawPos_Patch
	{
		[HarmonyPriority(Priority.Last)]
		static void Postfix(Pawn_DrawTracker __instance, ref Vector3 __result)
		{
			if (__instance.leaner is ZombieLeaner zombieLeaner)
				__result += zombieLeaner.ZombieOffset;
		}
	}

	[HarmonyPatch(typeof(Pawn_DrawTracker))]
	[HarmonyPatch("DrawTrackerTick")]
	static class Pawn_DrawTrackerDrawTrackerTick_Patch
	{
		[HarmonyPriority(Priority.Last)]
		static void Postfix(Pawn_DrawTracker __instance)
		{
			if (__instance.leaner is ZombieLeaner zombieLeaner)
				zombieLeaner.ZombieTick();
		}
	}

	class ZombieLeaner : PawnLeaner
	{
		readonly Zombie zombie;
		Vector3 jitterOffset = new Vector3(0, 0, 0);

		Vector3 extraOffsetInternal = new Vector3(0, 0, 0);
		public Vector3 extraOffset = new Vector3(0, 0, 0);
		readonly int randTickFrequency = Rand.Range(3, 9);
		readonly int randTickOffset = Rand.Range(0, 9);

		public ZombieLeaner(Pawn pawn) : base(pawn)
		{
			zombie = pawn as Zombie;
		}

		public void ZombieTick()
		{
			if (((GenTicks.TicksAbs + randTickOffset) % randTickFrequency) == 0)
			{
				if (zombie.state == ZombieState.Emerging || zombie.state == ZombieState.ShouldDie)
				{
					jitterOffset = Vector3.zero;
					extraOffsetInternal = Vector3.zero;
				}
				else
				{
					var f = zombie.hasTankySuit != -1f || zombie.hasTankyShield != -1f ? 0.1f : 1f;
					jitterOffset.x = Mathf.Clamp(jitterOffset.x + f * Rand.Range(-0.025f, 0.025f), f * -0.25f, f * 0.25f);
					jitterOffset.z = Mathf.Clamp(jitterOffset.z + f * Rand.Range(-0.025f, 0.025f), f * -0.25f, f * 0.25f);
				}
				extraOffsetInternal = (extraOffset + 3 * extraOffsetInternal) / 4;
			}
		}

		public Vector3 ZombieOffset => jitterOffset + extraOffsetInternal;

	}
}