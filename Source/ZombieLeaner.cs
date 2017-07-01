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
			var zombieLeaner = __instance.leaner as ZombieLeaner;
			if (zombieLeaner != null)
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
			var zombieLeaner = __instance.leaner as ZombieLeaner;
			if (zombieLeaner != null)
				zombieLeaner.ZombieTick();
		}
	}

	class ZombieLeaner : PawnLeaner
	{
		private Zombie zombie;
		private Vector3 jitterOffset;

		private Vector3 extraOffsetInternal;
		public Vector3 extraOffset;

		private int randTickFrequency = Rand.Range(2, 8);
		private int randTickOffset = Rand.Range(0, 8);

		public ZombieLeaner(Pawn pawn) : base(pawn)
		{
			zombie = pawn as Zombie;
		}

		public void ZombieTick()
		{
			if (((GenTicks.TicksAbs + randTickOffset) % randTickFrequency) == 0)
			{
				jitterOffset.x = Mathf.Clamp(jitterOffset.x + Rand.Range(-0.025f, 0.025f), -0.25f, 0.25f);
				jitterOffset.z = Mathf.Clamp(jitterOffset.z + Rand.Range(-0.025f, 0.025f), -0.25f, 0.25f);
				extraOffsetInternal = (extraOffset + 3 * extraOffsetInternal) / 4;
			}
		}

		public Vector3 ZombieOffset { get { return jitterOffset + extraOffsetInternal; } }
	}
}