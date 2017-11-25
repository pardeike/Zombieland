using Harmony;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	[HarmonyPatch(typeof(Pawn_DrawTracker))]
	[HarmonyPatch("DrawPos", PropertyMethod.Getter)]
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
		Zombie zombie;
		Vector3 jitterOffset = new Vector3(0, 0, 0);

		Vector3 extraOffsetInternal = new Vector3(0, 0, 0);
		public Vector3 extraOffset = new Vector3(0, 0, 0);

		int randTickFrequency = Rand.Range(3, 9);
		int randTickOffset = Rand.Range(0, 9);

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
					if (zombie.Downed)
					{
						jitterOffset.x /= 1.1f;
						jitterOffset.z /= 1.1f;
					}
					else
					{
						jitterOffset.x = Mathf.Clamp(jitterOffset.x + Rand.Range(-0.025f, 0.025f), -0.25f, 0.25f);
						jitterOffset.z = Mathf.Clamp(jitterOffset.z + Rand.Range(-0.025f, 0.025f), -0.25f, 0.25f);
					}
				}
				extraOffsetInternal = (extraOffset + 3 * extraOffsetInternal) / 4;
			}
		}

		public Vector3 ZombieOffset => jitterOffset + extraOffsetInternal;

	}
}