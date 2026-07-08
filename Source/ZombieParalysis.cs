using RimWorld;
using System;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	public static class ZombieParalysis
	{
		public static int ShockerParalysisTicks => GenDate.TicksPerHour / 2;

		public static int SkipAbductionParalysisTicks()
		{
			return Math.Max(1, Mathf.RoundToInt((float)GenMath.LerpDoubleClamped(
				1f,
				5f,
				GenDate.TicksPerHour,
				GenDate.TicksPerHour / 6f,
				Tools.Difficulty()
			)));
		}

		public static bool IsParalyzed(this Zombie zombie)
		{
			return zombie != null && zombie.paralyzedUntil > GenTicks.TicksAbs;
		}

		public static bool AffectsAvoidGridBeforeClearingExpiredParalysis(this Zombie zombie)
		{
			if (zombie == null)
				return false;
			if (zombie.paralyzedUntil > 0 && GenTicks.TicksAbs >= zombie.paralyzedUntil)
				return false;
			return zombie.AffectsAvoidGrid;
		}

		internal static ZombieAvoidGridSnapshot AvoidGridSnapshotBeforeClearingExpiredParalysis(this Zombie zombie)
		{
			if (zombie == null)
				return default;
			if (zombie.paralyzedUntil > 0 && GenTicks.TicksAbs >= zombie.paralyzedUntil)
				return default;
			return zombie.CaptureAvoidGridSnapshot();
		}

		public static bool CanParalyze(this Zombie zombie, out string error)
		{
			error = null;
			if (zombie == null)
			{
				error = "Zombie is null.";
				return false;
			}
			if (zombie.Destroyed)
			{
				error = "Zombie is destroyed.";
				return false;
			}
			if (zombie.Dead)
			{
				error = "Zombie is dead.";
				return false;
			}
			if (zombie.Spawned == false || zombie.Map == null)
			{
				error = "Zombie is not spawned on a map.";
				return false;
			}
			if (zombie.state == ZombieState.Emerging || zombie.state == ZombieState.Floating)
			{
				error = $"Zombie is in {zombie.state} state.";
				return false;
			}
			if (zombie.state == ZombieState.ShouldDie)
			{
				error = "Zombie is already scheduled to die.";
				return false;
			}
			return true;
		}

		public static bool TryParalyze(this Zombie zombie, int ticks, out string error, bool clearRope = true, bool stopDangerousJob = true)
		{
			error = null;
			if (ticks <= 0)
			{
				error = $"Paralysis ticks must be positive, got {ticks}.";
				return false;
			}
			if (zombie.CanParalyze(out error) == false)
				return false;

			var avoidGridSnapshot = zombie.CaptureAvoidGridSnapshot();
			var until = (int)Math.Min(int.MaxValue, (long)GenTicks.TicksAbs + ticks);
			zombie.paralyzedUntil = Math.Max(zombie.paralyzedUntil, until);
			if (clearRope)
				zombie.ropedBy = null;
			if (stopDangerousJob)
				StopDangerousState(zombie);
			zombie.RequestAvoidGridRefreshIfSpecChanged(avoidGridSnapshot);
			return true;
		}

		public static bool HandleParalyzedTick(this JobDriver_Stumble driver, Zombie zombie)
		{
			if (KeepParalyzedStanding(zombie) == false)
				return false;

			ClearStumbleState(driver);
			return true;
		}

		public static bool HandleParalyzedTick(this JobDriver_Sabotage driver, Zombie zombie)
		{
			if (KeepParalyzedStanding(zombie) == false)
				return false;

			ClearSabotageState(driver, zombie);
			return true;
		}

		static bool KeepParalyzedStanding(Zombie zombie)
		{
			if (zombie.paralyzedUntil <= 0)
				return false;
			if (GenTicks.TicksAbs >= zombie.paralyzedUntil)
			{
				var avoidGridSnapshot = zombie.AvoidGridSnapshotBeforeClearingExpiredParalysis();
				zombie.paralyzedUntil = 0;
				zombie.RequestAvoidGridRefreshIfSpecChanged(avoidGridSnapshot);
				return false;
			}

			zombie.pather?.StopDead();
			return true;
		}

		static void StopDangerousState(Zombie zombie)
		{
			zombie.pather?.StopDead();
			zombie.bombWillGoOff = false;
			zombie.wallPushProgress = -1f;
			zombie.wallPushStart = Vector3.zero;
			zombie.wallPushDestination = Vector3.zero;
			zombie.tankDestination = IntVec3.Invalid;

			if (zombie.jobs?.curDriver is JobDriver_Stumble stumble)
				ClearStumbleState(stumble);
			else if (zombie.jobs?.curDriver is JobDriver_Sabotage sabotage)
				ClearSabotageState(sabotage, zombie);
			else if (zombie.CurJobDef != null)
				zombie.jobs.EndCurrentJob(JobCondition.InterruptForced);
		}

		static void ClearStumbleState(JobDriver_Stumble driver)
		{
			if (driver == null)
				return;

			driver.destination = IntVec3.Invalid;
			driver.eatTarget = null;
			driver.lastEatTarget = null;
			driver.lastEatTargetPosition = IntVec3.Invalid;
			driver.eatDelayCounter = 0;
			driver.eatDelay = 0;
		}

		static void ClearSabotageState(JobDriver_Sabotage driver, Zombie zombie)
		{
			if (driver == null)
				return;

			driver.ResetActionState(resetScream: true);
		}
	}
}
