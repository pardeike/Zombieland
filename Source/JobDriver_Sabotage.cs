using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace ZombieLand
{
	public class JobDriver_Sabotage : JobDriver
	{
		const int albinoFallbackArrivalWaitTicks = 90;
		const int albinoFallbackReplanCooldownTicks = 180;

		public IntVec3 destination = IntVec3.Invalid;
		public Building_Door door = null;
		public IntVec3 doorExitCell = IntVec3.Invalid;
		public Thing hackTarget = null;
		public IntVec3 hackApproachCell = IntVec3.Invalid;
		public IntVec3 queuedScreamCell = IntVec3.Invalid;
		public IntVec3 queuedMoveCell = IntVec3.Invalid;
		public int waitCounter = 0;
		public int hackCounter = 0;
		public int nextDefensiveScreamCheckTick = 0;
		public IntVec3 lastDefensiveScreamCheckCell = IntVec3.Invalid;
		public int nextDefensiveScreamCellCheckTick = 0;
		public bool defensiveScreamQueued = false;
		public bool noSafeHackRoute = false;
		public bool interruptibleDestination = false;
		public bool safetyDestination = false;
		public bool fallbackDestination = false;
		public int nextStrategicRecheckTick = 0;
		public IntVec3 lastStrategicRecheckCell = IntVec3.Invalid;
		public IntVec3 lastFallbackStartCell = IntVec3.Invalid;
		public IntVec3 lastFallbackDestination = IntVec3.Invalid;
		public int nextFallbackMoveTick = 0;
		public Thing deferredHackTarget = null;
		public int deferredHackTargetPauseUntilTick = 0;
		public Thing rushHackTarget = null;
		public int rushHackTargetUntilTick = 0;
		public List<Thing> recentlyHackedTargets = new();
		public List<int> recentlyHackedTargetPauseUntilTicks = new();

		void NormalizeRecentlyHackedTargets()
		{
			recentlyHackedTargets ??= new List<Thing>();
			recentlyHackedTargetPauseUntilTicks ??= new List<int>();
			while (recentlyHackedTargets.Count > recentlyHackedTargetPauseUntilTicks.Count)
				recentlyHackedTargets.RemoveAt(recentlyHackedTargets.Count - 1);
			while (recentlyHackedTargetPauseUntilTicks.Count > recentlyHackedTargets.Count)
				recentlyHackedTargetPauseUntilTicks.RemoveAt(recentlyHackedTargetPauseUntilTicks.Count - 1);
		}

		internal void ResetActionState(bool resetScream)
		{
			destination = IntVec3.Invalid;
			door = null;
			doorExitCell = IntVec3.Invalid;
			hackTarget = null;
			hackApproachCell = IntVec3.Invalid;
			queuedScreamCell = IntVec3.Invalid;
			queuedMoveCell = IntVec3.Invalid;
			waitCounter = 0;
			hackCounter = 0;
			nextDefensiveScreamCheckTick = 0;
			lastDefensiveScreamCheckCell = IntVec3.Invalid;
			nextDefensiveScreamCellCheckTick = 0;
			defensiveScreamQueued = false;
			noSafeHackRoute = false;
			interruptibleDestination = false;
			safetyDestination = false;
			fallbackDestination = false;
			nextStrategicRecheckTick = 0;
			lastStrategicRecheckCell = IntVec3.Invalid;
			lastFallbackStartCell = IntVec3.Invalid;
			lastFallbackDestination = IntVec3.Invalid;
			nextFallbackMoveTick = 0;
			deferredHackTarget = null;
			deferredHackTargetPauseUntilTick = 0;
			rushHackTarget = null;
			rushHackTargetUntilTick = 0;
			NormalizeRecentlyHackedTargets();
			if (resetScream && pawn is Zombie zombie)
				zombie.scream = -1;
		}

		void InitAction()
		{
			ResetActionState(true);
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref destination, "destination", IntVec3.Invalid);
			Scribe_References.Look(ref door, "door");
			Scribe_Values.Look(ref doorExitCell, "doorExitCell", IntVec3.Invalid);
			Scribe_References.Look(ref hackTarget, "hackTarget");
			Scribe_Values.Look(ref hackApproachCell, "hackApproachCell", IntVec3.Invalid);
			Scribe_Values.Look(ref queuedScreamCell, "queuedScreamCell", IntVec3.Invalid);
			Scribe_Values.Look(ref queuedMoveCell, "queuedMoveCell", IntVec3.Invalid);
			Scribe_Values.Look(ref waitCounter, "waitCounter", 0);
			Scribe_Values.Look(ref hackCounter, "hackCounter", 0);
			Scribe_Values.Look(ref nextDefensiveScreamCheckTick, "nextDefensiveScreamCheckTick", 0);
			Scribe_Values.Look(ref lastDefensiveScreamCheckCell, "lastDefensiveScreamCheckCell", IntVec3.Invalid);
			Scribe_Values.Look(ref nextDefensiveScreamCellCheckTick, "nextDefensiveScreamCellCheckTick", 0);
			Scribe_Values.Look(ref defensiveScreamQueued, "defensiveScreamQueued", false);
			Scribe_Values.Look(ref noSafeHackRoute, "noSafeHackRoute", false);
			Scribe_Values.Look(ref interruptibleDestination, "interruptibleDestination", false);
			Scribe_Values.Look(ref safetyDestination, "safetyDestination", false);
			Scribe_Values.Look(ref fallbackDestination, "fallbackDestination", false);
			Scribe_Values.Look(ref nextStrategicRecheckTick, "nextStrategicRecheckTick", 0);
			Scribe_Values.Look(ref lastStrategicRecheckCell, "lastStrategicRecheckCell", IntVec3.Invalid);
			Scribe_Values.Look(ref lastFallbackStartCell, "lastFallbackStartCell", IntVec3.Invalid);
			Scribe_Values.Look(ref lastFallbackDestination, "lastFallbackDestination", IntVec3.Invalid);
			Scribe_Values.Look(ref nextFallbackMoveTick, "nextFallbackMoveTick", 0);
			Scribe_References.Look(ref deferredHackTarget, "deferredHackTarget");
			Scribe_Values.Look(ref deferredHackTargetPauseUntilTick, "deferredHackTargetPauseUntilTick", 0);
			Scribe_References.Look(ref rushHackTarget, "rushHackTarget");
			Scribe_Values.Look(ref rushHackTargetUntilTick, "rushHackTargetUntilTick", 0);
			Scribe_Collections.Look(ref recentlyHackedTargets, "recentlyHackedTargets", LookMode.Reference);
			Scribe_Collections.Look(ref recentlyHackedTargetPauseUntilTicks, "recentlyHackedTargetPauseUntilTicks", LookMode.Value);
			if (Scribe.mode == LoadSaveMode.PostLoadInit)
				NormalizeRecentlyHackedTargets();
		}

		void TickAction()
		{
			var zombie = (Zombie)pawn;
			if (zombie.state == ZombieState.Emerging)
				return;

			if (this.DieEasily())
				return;

			if (this.HandleParalyzedTick(zombie))
				return;

			if (zombie.scream > 0 && this.Scream())
				return;

			if (this.ReconsiderInterruptibleDestination())
				return;

			if (this.TrySwitchContestedHackToScream())
				return;

			if (this.Wait())
				return;

			if (zombie.scream <= 0 && this.Scream())
				return;

			if (this.HackThing())
				return;

			if (this.CheckAndFindDestination())
				return;

			waitCounter = 60;
		}

		public override void Notify_PatherArrived()
		{

			base.Notify_PatherArrived();
			if (fallbackDestination && door == null)
			{
				waitCounter = Math.Max(waitCounter, albinoFallbackArrivalWaitTicks);
				nextFallbackMoveTick = Math.Max(nextFallbackMoveTick, GenTicks.TicksGame + albinoFallbackReplanCooldownTicks);
			}
			destination = IntVec3.Invalid;
			interruptibleDestination = false;
			safetyDestination = false;
			fallbackDestination = false;
		}

		public override void Notify_PatherFailed()
		{

			var zombie = pawn as Zombie;
			var preserveActiveScream = zombie != null && zombie.scream >= 0;
			var scream = zombie?.scream ?? -1;
			var affectedCount = zombie?.albinoScreamAffectedCount ?? 0;
			var preservedWaitCounter = waitCounter;
			base.Notify_PatherFailed();
			ResetActionState(preserveActiveScream == false);
			if (preserveActiveScream)
			{
				zombie.scream = scream;
				zombie.albinoScreamAffectedCount = affectedCount;
				if (scream == 0 && preservedWaitCounter > 0)
					waitCounter = preservedWaitCounter;
			}
		}

		public override string GetReport()
		{
			return "Sabotaging";
		}

		public override IEnumerable<Toil> MakeNewToils()
		{
			yield return new Toil()
			{
				initAction = new Action(InitAction),
				tickAction = new Action(TickAction),
				defaultCompleteMode = ToilCompleteMode.Never
			};
		}

		public override bool TryMakePreToilReservations(bool errorOnFailed)
		{
			return true;
		}
	}

	static class SabotageHandler
	{
		const int albinoScreamDurationTicks = 400;
		const int albinoScreamMovementReleaseTicks = albinoScreamDurationTicks * 3 / 4;
		const float albinoScreamMaxRadius = 12f;
		const int albinoScreamMinWindupTicks = 45;
		const int albinoScreamMaxWindupTicks = 90;
		const int albinoDefensiveScreamWindupTicks = 30;
		const int albinoScreamInitialMinCooldown = 600;
		const int albinoScreamInitialMaxCooldown = 1800;
		const int albinoScreamWastedMinCooldown = 1800;
		const int albinoScreamWastedMaxCooldown = 3000;
		const int albinoScreamSuccessMinCooldown = 2500;
		const int albinoScreamSuccessMaxCooldown = 4500;
		const int albinoScreamSuccessAffectedCooldownTicks = 300;
		const int albinoScreamSuccessAffectedCooldownMax = 1500;
		const int albinoScreamMaxCooldown = 6000;
		const int albinoDefensiveScreamEarlyRadiusSquared = 64;
		const int albinoUrgentScreamRadiusSquared = 36;
		const int albinoDefensiveEmergencyScreamMaxRemainingTicks = 2400;
		const int albinoDefensiveEmergencyScreamThreatRadiusSquared = 16;
		const int albinoDefensiveScreamMinPressure = 6;
		const int albinoDefensiveScreamSoftPressure = 4;
		const int albinoDefensiveScreamCheckIntervalTicks = 30;
		const int albinoDefensiveScreamCellChangeCooldownTicks = 10;
		const int albinoDefensiveScreamPathSamples = 12;
		const int albinoNoSafeHackRoutePressure = 4;
		const int albinoHackApproachCandidateLimit = 48;
		const int albinoHackTargetCandidateLimit = 32;
		const int albinoSafetyMoveCandidateLimit = 96;
		const int albinoFallbackMoveCandidateLimit = 80;
		const int albinoNearbyFallbackMoveCandidateLimit = 64;
		const int albinoStrategicRecheckCooldownTicks = 12;
		const int albinoFallbackFailureCooldownTicks = 180;
		const int albinoPressureRetryWaitTicks = 12;
		const int albinoDoorOpenResumeWaitTicks = 0;
		const int albinoLostScreamTargetRecheckWaitTicks = 12;
		const int albinoRecentlyHackedTargetPauseTicks = 4500;
		const int albinoUnsafeHackTargetPauseTicks = 900;
		const int albinoDesperationScreamMaxWaitTicks = 420;

		static bool TryFindLastCellBeforeBlockingDoor(this PawnPath path, Pawn pawn, out IntVec3 result, out Building_Door door)
		{
			return path.TryFindFirstAlbinoDoorTransition(pawn, true, out result, out door, out _);
		}

		static bool TryFindLastCellBeforeBlockingDoor(this PawnPath path, Pawn pawn, out IntVec3 result, out Building_Door door, out IntVec3 exitCell)
		{
			return path.TryFindFirstAlbinoDoorTransition(pawn, true, out result, out door, out exitCell);
		}

		static bool TryFindFirstAlbinoDoorTransition(this PawnPath path, Pawn pawn, bool blockingOnly, out IntVec3 beforeDoor, out Building_Door door, out IntVec3 exitCell)
		{
			beforeDoor = IntVec3.Invalid;
			door = null;
			exitCell = IntVec3.Invalid;

			var map = pawn?.Map;
			var nodesReversed = path?.NodesReversed;
			if (map == null || nodesReversed == null || nodesReversed.Count == 0)
			{
				return false;
			}

			for (var num = nodesReversed.Count - 1; num >= 0; num--)
			{
				door = nodesReversed[num].GetEdifice(map) as Building_Door;
				if (door?.Spawned != true || door.Destroyed)
					continue;
				if (blockingOnly && door.BlocksAlbinoSabotagePath(pawn) == false)
					continue;

				beforeDoor = num == nodesReversed.Count - 1 ? pawn.Position : nodesReversed[num + 1];
				exitCell = num > 0 ? nodesReversed[num - 1] : IntVec3.Invalid;
				return true;
			}

			door = null;
			return false;
		}

		static bool TryFindDangerousAlbinoDoorExit(Zombie zombie, PawnPath path, AlbinoPressureSources sources, out Building_Door door, out IntVec3 exitCell, out int pressure)
		{
			door = null;
			exitCell = IntVec3.Invalid;
			pressure = 0;

			var map = zombie?.Map;
			var nodesReversed = path?.NodesReversed;
			if (map == null || nodesReversed == null || nodesReversed.Count == 0 || HasAlbinoPressureSources(sources) == false)
				return false;

			for (var num = nodesReversed.Count - 1; num >= 0; num--)
			{
				var candidateDoor = nodesReversed[num].GetEdifice(map) as Building_Door;
				if (candidateDoor?.Spawned != true || candidateDoor.Destroyed)
					continue;

				var candidateExitCell = num > 0 ? nodesReversed[num - 1] : IntVec3.Invalid;
				if (candidateExitCell.IsValid == false)
					continue;

				var candidatePressure = AlbinoDoorOpenPressureAtCell(zombie, candidateDoor, candidateExitCell, sources);
				if (candidatePressure >= albinoNoSafeHackRoutePressure)
				{
					door = candidateDoor;
					exitCell = candidateExitCell;
					pressure = candidatePressure;
					return true;
				}
			}

			return false;
		}

		static void AddAlbinoDoorTransitionPressure(Zombie zombie, PawnPath path, AlbinoPressureSources sources, ref int summedPressure, ref int maxPressure)
		{
			var map = zombie?.Map;
			var nodesReversed = path?.NodesReversed;
			if (map == null || nodesReversed == null || nodesReversed.Count == 0 || HasAlbinoPressureSources(sources) == false)
				return;

			for (var num = nodesReversed.Count - 1; num >= 0; num--)
			{
				var door = nodesReversed[num].GetEdifice(map) as Building_Door;
				if (door?.Spawned != true || door.Destroyed)
					continue;

				var beforeDoor = num == nodesReversed.Count - 1 ? zombie.Position : nodesReversed[num + 1];
				if (beforeDoor.IsValid)
				{
					var pressure = AlbinoPressureAtCell(zombie, beforeDoor, sources);
					maxPressure = Math.Max(maxPressure, pressure);
					summedPressure += pressure * 2;
				}

				var exitCell = num > 0 ? nodesReversed[num - 1] : IntVec3.Invalid;
				if (exitCell.IsValid)
				{
					var pressure = AlbinoDoorOpenPressureAtCell(zombie, door, exitCell, sources);
					maxPressure = Math.Max(maxPressure, pressure);
					summedPressure += pressure * 3;
				}
			}
		}

		static bool BlocksAlbinoSabotagePath(this Building_Door door, Pawn pawn)
		{
			return door?.Spawned == true
				&& door.Destroyed == false
				&& door.CanPhysicallyPass(pawn) == false;
		}

		static void ClearHackTarget(this JobDriver_Sabotage driver)
		{
			if (driver.rushHackTarget == driver.hackTarget)
			{
				driver.rushHackTarget = null;
				driver.rushHackTargetUntilTick = 0;
			}
			driver.hackTarget = null;
			driver.hackApproachCell = IntVec3.Invalid;
			driver.hackCounter = 0;
			driver.noSafeHackRoute = false;
		}

		static void ClearStrategicDestination(this JobDriver_Sabotage driver)
		{
			driver.destination = IntVec3.Invalid;
			driver.door = null;
			driver.doorExitCell = IntVec3.Invalid;
			driver.queuedScreamCell = IntVec3.Invalid;
			driver.queuedMoveCell = IntVec3.Invalid;
			driver.interruptibleDestination = false;
			driver.safetyDestination = false;
			driver.fallbackDestination = false;
			driver.nextStrategicRecheckTick = 0;
			driver.lastStrategicRecheckCell = IntVec3.Invalid;
		}

		static void MarkStrategicDestination(this JobDriver_Sabotage driver, bool interruptible, bool safety, bool fallback = false)
		{
			driver.interruptibleDestination = interruptible;
			driver.safetyDestination = safety;
			driver.fallbackDestination = fallback;
			driver.nextStrategicRecheckTick = 0;
			driver.lastStrategicRecheckCell = IntVec3.Invalid;
		}

		static bool CanResumeHackTarget(this JobDriver_Sabotage driver)
		{
			var target = driver?.hackTarget;
			return driver?.pawn?.Spawned == true
				&& target?.Spawned == true
				&& target.Map == driver.pawn.Map
				&& driver.IsDeferredHackTarget(target) == false
				&& driver.CanSelectHackThing(target);
		}

		static void InterruptHackProgress(this JobDriver_Sabotage driver, bool preserveHackTarget)
		{
			if (preserveHackTarget && driver.CanResumeHackTarget())
			{
				driver.hackApproachCell = IntVec3.Invalid;
				driver.hackCounter = 0;
				driver.noSafeHackRoute = false;
				return;
			}

			driver.ClearHackTarget();
		}

		static void SetHackTarget(this JobDriver_Sabotage driver, Thing thing)
		{
			driver.CleanupRushHackTarget();
			if (driver.hackTarget != thing)
			{
				driver.hackCounter = 0;
				driver.hackApproachCell = IntVec3.Invalid;
				driver.noSafeHackRoute = false;
			}
			driver.hackTarget = thing;
		}

		static void CleanupRecentlyHackedTargets(this JobDriver_Sabotage driver)
		{
			driver.recentlyHackedTargets ??= new List<Thing>();
			driver.recentlyHackedTargetPauseUntilTicks ??= new List<int>();
			var count = Math.Min(driver.recentlyHackedTargets.Count, driver.recentlyHackedTargetPauseUntilTicks.Count);
			while (driver.recentlyHackedTargets.Count > count)
				driver.recentlyHackedTargets.RemoveAt(driver.recentlyHackedTargets.Count - 1);
			while (driver.recentlyHackedTargetPauseUntilTicks.Count > count)
				driver.recentlyHackedTargetPauseUntilTicks.RemoveAt(driver.recentlyHackedTargetPauseUntilTicks.Count - 1);

			var ticks = GenTicks.TicksGame;
			for (var i = count - 1; i >= 0; i--)
			{
				var target = driver.recentlyHackedTargets[i];
				if (target == null || target.Destroyed || target.Spawned == false || driver.recentlyHackedTargetPauseUntilTicks[i] <= ticks)
				{
					driver.recentlyHackedTargets.RemoveAt(i);
					driver.recentlyHackedTargetPauseUntilTicks.RemoveAt(i);
				}
			}
		}

		static bool IsRecentlyHackedTargetPaused(this JobDriver_Sabotage driver, Thing thing)
		{
			if (thing == null)
				return false;

			driver.CleanupRecentlyHackedTargets();
			return driver.recentlyHackedTargets.Contains(thing);
		}

		static AlbinoSabotageMemory AlbinoMemory(this JobDriver_Sabotage driver)
		{
			return AlbinoSabotageMemory.GetOrCreate(driver?.pawn?.Map);
		}

		static bool IsEnoughHackedItem(this JobDriver_Sabotage driver, Thing thing)
		{
			return driver.AlbinoMemory()?.IsEnoughHackedItem(thing) == true;
		}

		static void RememberEnoughHackedItem(this JobDriver_Sabotage driver, Thing thing)
		{
			driver.AlbinoMemory()?.RememberEnoughHackedItem(thing);
		}

		static bool CanSelectHackThing(this JobDriver_Sabotage driver, Thing thing)
		{
			return driver.CanSelectHackThing(thing, driver.AlbinoMemory());
		}

		static bool CanSelectHackThing(this JobDriver_Sabotage driver, Thing thing, AlbinoSabotageMemory memory)
		{
			return CanHackThing(thing) && memory?.IsEnoughHackedItem(thing) != true;
		}

		static void CleanupDeferredHackTarget(this JobDriver_Sabotage driver)
		{
			if (driver?.deferredHackTarget == null)
				return;
			if (driver.deferredHackTarget.Destroyed
				|| driver.deferredHackTarget.Spawned == false
				|| driver.deferredHackTargetPauseUntilTick <= GenTicks.TicksGame)
			{
				driver.deferredHackTarget = null;
				driver.deferredHackTargetPauseUntilTick = 0;
			}
		}

		static bool IsDeferredHackTarget(this JobDriver_Sabotage driver, Thing thing)
		{
			if (thing == null)
				return false;

			driver.CleanupDeferredHackTarget();
			return driver.deferredHackTarget == thing;
		}

		static void DeferUnsafeHackTarget(this JobDriver_Sabotage driver, Thing thing)
		{
			if (thing == null || thing.Destroyed || thing.Spawned == false)
				return;

			driver.deferredHackTarget = thing;
			driver.deferredHackTargetPauseUntilTick = GenTicks.TicksGame + albinoUnsafeHackTargetPauseTicks;
		}

		static void CleanupRushHackTarget(this JobDriver_Sabotage driver)
		{
			if (driver?.rushHackTarget == null)
				return;
			if (driver.rushHackTarget.Destroyed
				|| driver.rushHackTarget.Spawned == false
				|| driver.rushHackTargetUntilTick <= GenTicks.TicksGame)
			{
				driver.rushHackTarget = null;
				driver.rushHackTargetUntilTick = 0;
			}
		}

		static bool IsRushingHackTarget(this JobDriver_Sabotage driver, Thing thing)
		{
			if (thing == null)
				return false;

			driver.CleanupRushHackTarget();
			return driver.rushHackTarget == thing;
		}

		static void StartHackTargetRush(this JobDriver_Sabotage driver, Thing thing)
		{
			if (thing == null || thing.Destroyed || thing.Spawned == false)
				return;

			driver.rushHackTarget = thing;
			driver.rushHackTargetUntilTick = GenTicks.TicksGame + albinoUnsafeHackTargetPauseTicks;
		}

		static void PauseRecentlyHackedTarget(this JobDriver_Sabotage driver, Thing thing)
		{
			if (thing == null || thing.Destroyed || thing.Spawned == false)
				return;

			driver.CleanupRecentlyHackedTargets();
			var until = GenTicks.TicksGame + albinoRecentlyHackedTargetPauseTicks;
			var index = driver.recentlyHackedTargets.IndexOf(thing);
			if (index >= 0)
				driver.recentlyHackedTargetPauseUntilTicks[index] = Math.Max(driver.recentlyHackedTargetPauseUntilTicks[index], until);
			else
			{
				driver.recentlyHackedTargets.Add(thing);
				driver.recentlyHackedTargetPauseUntilTicks.Add(until);
			}
		}

		internal static int PausedHackTargetCount(this JobDriver_Sabotage driver)
		{
			driver.CleanupRecentlyHackedTargets();
			return driver.recentlyHackedTargets.Count;
		}

		static PathEndMode HackPathEndMode(Thing thing)
		{
			return thing.Position.Standable(thing.Map) ? PathEndMode.ClosestTouch : PathEndMode.Touch;
		}

		internal static bool CanHackThing(Thing thing)
		{
			if (thing?.Spawned != true)
				return false;
			if (thing is Building building)
				return CanHackBuilding(building);
			return thing.def.IsRangedWeapon && thing.def.useHitPoints;
		}

		static bool CanHackTargetNow(this JobDriver_Sabotage driver, Thing thing)
		{
			return driver?.pawn?.Spawned == true
				&& thing?.Spawned == true
				&& thing.Map == driver.pawn.Map
				&& driver.CanSelectHackThing(thing)
				&& thing.OccupiedRect().ExpandedBy(1).Contains(driver.pawn.Position);
		}

		static bool RepathOrClearHackTarget(this JobDriver_Sabotage driver, Thing thing)
		{
			driver.hackCounter = 0;
			if (thing?.Spawned == true && driver.pawn?.Map == thing.Map && driver.CanSelectHackThing(thing) && driver.Goto(thing))
				return true;

			driver.ClearHackTarget();
			return false;
		}

		static bool IsVomiting(Pawn pawn)
		{
			return pawn?.CurJobDef == JobDefOf.Vomit;
		}

		static bool HasUsableRangedVerb(IAttackTargetSearcher searcher, out Verb verb)
		{
			verb = searcher?.CurrentEffectiveVerb;
			return verb != null && verb.IsMeleeAttack == false;
		}

		static bool CanShootCell(IAttackTargetSearcher searcher, IntVec3 cell)
		{
			if (cell.IsValid == false || searcher?.Thing?.Spawned != true || HasUsableRangedVerb(searcher, out var verb) == false)
				return false;

			var origin = searcher.Thing.Position;
			var range = verb.verbProps?.range ?? 0f;
			if (range > 0f && origin.DistanceToSquared(cell) > range * range)
				return false;

			return verb.CanHitTargetFrom(origin, new LocalTargetInfo(cell));
		}

		static bool CanShootThing(IAttackTargetSearcher searcher, Thing thing)
		{
			if (thing?.Spawned != true || searcher?.Thing?.Spawned != true || HasUsableRangedVerb(searcher, out var verb) == false)
				return false;

			var origin = searcher.Thing.Position;
			var range = verb.verbProps?.range ?? 0f;
			if (range > 0f && origin.DistanceToSquared(thing.Position) > range * range)
				return false;

			return verb.CanHitTargetFrom(origin, thing);
		}

		static bool CanPotentiallyShootThroughOpenDoor(IAttackTargetSearcher searcher, Building_Door door, IntVec3 exitCell)
		{
			if (door?.Spawned != true || exitCell.IsValid == false || searcher?.Thing?.Spawned != true || HasUsableRangedVerb(searcher, out var verb) == false)
				return false;

			var origin = searcher.Thing.Position;
			var range = verb.verbProps?.range ?? 0f;
			if (range > 0f
				&& origin.DistanceToSquared(exitCell) > range * range
				&& origin.DistanceToSquared(door.Position) > range * range)
				return false;

			return CanShootCell(searcher, exitCell)
				|| CanShootThing(searcher, door)
				|| GenSight.LineOfSight(origin, door.Position, door.Map, true);
		}

		static int AlbinoDoorOpenPressureAtCell(Zombie zombie, Building_Door door, IntVec3 exitCell, AlbinoPressureSources sources)
		{
			if (zombie?.Spawned != true || door?.Spawned != true || exitCell.IsValid == false || sources == null)
				return 0;

			var score = AlbinoPressureAtCell(zombie, exitCell, sources);
			foreach (var pawn in sources.pawns)
				if (CanShootCell(pawn, exitCell) == false && CanPotentiallyShootThroughOpenDoor(pawn, door, exitCell))
					score += IsAttackingOrApproaching(pawn, zombie) || IsDrafted(pawn) ? 4 : 3;
			foreach (var turret in sources.turrets)
				if (CanShootCell(turret, exitCell) == false && CanPotentiallyShootThroughOpenDoor(turret, door, exitCell))
					score += 4;
			return score;
		}

		static bool IsActiveTurret(Building_TurretGun turret)
		{
			if (turret?.Spawned != true || turret.Faction != Faction.OfPlayer)
				return false;
			var power = turret.powerComp ?? turret.TryGetComp<CompPowerTrader>();
			return power == null || power.PowerOn;
		}

		static bool IsHumanlikeFleshPawn(Pawn pawn)
		{
			var raceProps = pawn?.RaceProps;
			return raceProps?.Humanlike == true
				&& raceProps.IsFlesh
				&& AlienTools.IsFleshPawn(pawn)
				&& SoSTools.IsHologram(pawn) == false;
		}

		static bool CanAlbinoPressurePawn(Pawn pawn, Zombie zombie)
		{
			if (pawn == null
				|| zombie == null
				|| pawn.Spawned == false
				|| pawn.Map != zombie.Map
				|| pawn.Dead
				|| pawn.health?.Downed == true
				|| pawn.jobs == null
				|| pawn.stances == null
				|| IsZombielandZombie(pawn)
				|| IsVomiting(pawn)
				|| pawn.activity?.IsDormant == true
				|| pawn.activity?.Deactivated == true
				|| pawn.canBeDormant?.Awake == false
				|| (pawn.RaceProps?.Humanlike == true && pawn.InfectionState() >= InfectionState.Infecting))
				return false;

			if (IsAttackingOrApproaching(pawn, zombie))
				return true;

			var raceProps = pawn.RaceProps;
			if (raceProps == null)
				return false;

			var faction = pawn.Faction;
			var settings = ZombieSettings.Values;
			var isHuman = IsHumanlikeFleshPawn(pawn);
			var isMech = raceProps.IsMechanoid;
			var isAnimal = raceProps.Animal;

			if (faction?.def?.isPlayer == true)
			{
				if (isHuman)
					return true;
				if (isMech)
					return settings.attackMode != AttackMode.OnlyHumans;
				if (isAnimal)
					return settings.animalsAttackZombies && settings.attackMode == AttackMode.Everything;
				return settings.attackMode == AttackMode.Everything;
			}

			if (AnomalyTargeting.TryGetZombieHostilityOverride(pawn, out var anomalyAttacksZombies))
				return anomalyAttacksZombies;

			if (faction != null && faction.HostileTo(Faction.OfPlayer))
			{
				if (GroupZombieResponse.ModeFor(pawn) != GroupResponseMode.Full)
					return false;
				if (isHuman)
					return settings.attackMode != AttackMode.OnlyColonists;
				if (isMech)
					return settings.attackMode == AttackMode.Everything;
				if (isAnimal)
					return settings.animalsAttackZombies && settings.attackMode == AttackMode.Everything;
				return settings.attackMode == AttackMode.Everything;
			}

			return isAnimal && settings.animalsAttackZombies && settings.attackMode == AttackMode.Everything;
		}

		sealed class AlbinoPressureSources
		{
			public List<Pawn> pawns = new();
			public List<Building_TurretGun> turrets = new();
		}

		static AlbinoPressureSources AlbinoPressureSourcesFor(Zombie zombie)
		{
			var sources = new AlbinoPressureSources();
			var seen = new HashSet<Pawn>();
			void Add(Pawn pawn)
			{
				if (CanAlbinoPressurePawn(pawn, zombie) && seen.Add(pawn))
					sources.pawns.Add(pawn);
			}

			foreach (var pawn in zombie.Map.mapPawns.AllPawnsSpawned)
				if (pawn.IsColonist || pawn.Faction == Faction.OfPlayer || pawn.Faction?.HostileTo(Faction.OfPlayer) == true)
					Add(pawn);
			foreach (var pawn in zombie.Map.attackTargetsCache.TargetsHostileToColony.OfType<Pawn>())
				Add(pawn);
			foreach (var turret in zombie.Map.listerBuildings.allBuildingsColonist.OfType<Building_TurretGun>())
				if (IsActiveTurret(turret))
					sources.turrets.Add(turret);
			return sources;
		}

		static int AlbinoPawnPressureAtCell(Zombie zombie, IntVec3 cell, List<Pawn> pawns)
		{
			var score = 0;
			foreach (var pawn in pawns)
			{
				var distance = pawn.Position.DistanceToSquared(cell);
				var activeResponse = IsAttackingOrApproaching(pawn, zombie);
				var rangedShooter = HasUsableRangedVerb(pawn, out _);
				if (rangedShooter && CanShootCell(pawn, cell))
					score += 4;
				else if (distance > 144)
					continue;
				else if (distance <= 4)
					score += activeResponse ? 4 : 2;
				else if (distance <= 25)
					score += activeResponse ? 3 : 1;
				else if (activeResponse)
					score += 2;
			}
			return score;
		}

		static int AlbinoTurretPressureAtCell(IntVec3 cell, List<Building_TurretGun> turrets)
		{
			var score = 0;
			foreach (var turret in turrets)
				if (CanShootCell(turret, cell))
					score += 4;
			return score;
		}

		static int AlbinoPressureAtCell(Zombie zombie, IntVec3 cell, AlbinoPressureSources sources)
		{
			return AlbinoPawnPressureAtCell(zombie, cell, sources.pawns) + AlbinoTurretPressureAtCell(cell, sources.turrets);
		}

		static bool HasAlbinoPressureSources(AlbinoPressureSources sources)
		{
			return sources != null && (sources.pawns.Count > 0 || sources.turrets.Count > 0);
		}

		static IEnumerable<Pawn> AlbinoScreamAffectablePressurePawns(Zombie zombie, IEnumerable<Pawn> pawns)
		{
			foreach (var pawn in pawns)
				if (CanAlbinoScreamAffect(pawn, zombie))
					yield return pawn;
		}

		static IEnumerable<IntVec3> HackApproachCandidates(Pawn pawn, Thing target)
		{
			if (pawn?.Map == null || target?.Spawned != true)
				yield break;

			var map = pawn.Map;
			var targetRect = target.OccupiedRect();
			var candidateRect = targetRect.ExpandedBy(1).ClipInsideMap(map);
			foreach (var cell in candidateRect.Cells.Distinct())
			{
				if (cell.InBounds(map) == false || cell.Standable(map) == false)
					continue;
				if (targetRect.ExpandedBy(1).Contains(cell) == false)
					continue;
				if (targetRect.Contains(cell) && cell != target.Position)
					continue;
				if (cell.Fogged(map))
					continue;
				yield return cell;
			}
		}

		static int AlbinoPathPressureScore(Zombie zombie, PawnPath path, AlbinoPressureSources sources, out int maxPressure)
		{
			maxPressure = 0;
			if (path?.Found != true)
				return int.MaxValue;

			var score = path.NodesLeftCount;
			var summedPressure = 0;
			var samples = 0;
			var step = Math.Max(1, path.NodesLeftCount / albinoDefensiveScreamPathSamples);
			for (var i = 0; i < path.NodesLeftCount && samples < albinoDefensiveScreamPathSamples; i += step)
			{
				var cell = path.Peek(i);
				if (cell.IsValid == false)
					continue;

				var pressure = AlbinoPressureAtCell(zombie, cell, sources);
				maxPressure = Math.Max(maxPressure, pressure);
				summedPressure += pressure;
				samples++;
			}

			AddAlbinoDoorTransitionPressure(zombie, path, sources, ref summedPressure, ref maxPressure);

			return score + maxPressure * 80 + summedPressure * 10;
		}

		static bool AlbinoHackPathIsBetter(int score, int maxPressure, int bestScore, int bestMaxPressure)
		{
			var unsafePath = maxPressure >= albinoNoSafeHackRoutePressure;
			var unsafeBest = bestMaxPressure >= albinoNoSafeHackRoutePressure;
			if (unsafePath != unsafeBest)
				return unsafePath == false;
			if (unsafePath && maxPressure != bestMaxPressure)
				return maxPressure < bestMaxPressure;
			return score < bestScore;
		}

		static float AlbinoHackTargetValue(Map map, Thing target)
		{
			if (target == null)
				return 0f;
			if (target.def.IsRangedWeapon)
				return WeaponSabotageScore(map, target);
			return target.MarketValue;
		}

		static bool TryScoreAlbinoHackTarget(JobDriver_Sabotage driver, Thing target, AlbinoPressureSources sources, out int score, out int maxPressure, out int pathLength)
		{
			score = int.MaxValue;
			maxPressure = 0;
			pathLength = int.MaxValue;
			var zombie = driver.pawn as Zombie;
			if (zombie?.Spawned != true || target?.Spawned != true || target.Map != zombie.Map)
				return false;

			var path = zombie.Map.pathFinder.FindPathNow(
				zombie.Position,
				target,
				TraverseParms.For(zombie, Danger.None, TraverseMode.PassDoors, false),
				null,
				HackPathEndMode(target),
				null);
			try
			{
				if (path.Found == false)
					return false;

				if (TryFindDangerousAlbinoDoorExit(zombie, path, sources, out _, out _, out _))
					return false;

				pathLength = path.NodesLeftCount;
				score = AlbinoPathPressureScore(zombie, path, sources, out maxPressure);
				return true;
			}
			finally
			{
				path.ReleaseToPool();
			}
		}

		static TraverseParms AlbinoFallbackTraverseParms(Pawn pawn)
		{
			return TraverseParms.For(pawn, Danger.None, TraverseMode.NoPassClosedDoors, false);
		}

		static void CooldownAlbinoFallbackSearch(this JobDriver_Sabotage driver)
		{
			var ticks = GenTicks.TicksGame;
			if (ticks < driver.nextFallbackMoveTick)
				return;
			driver.nextFallbackMoveTick = ticks + albinoFallbackFailureCooldownTicks;
		}

		static bool AlbinoHackTargetIsBetter(
			int score,
			int maxPressure,
			int pathLength,
			float value,
			int distance,
			int bestScore,
			int bestMaxPressure,
			int bestPathLength,
			float bestValue,
			int bestDistance)
		{
			var unsafeTarget = maxPressure >= albinoNoSafeHackRoutePressure;
			var unsafeBest = bestMaxPressure >= albinoNoSafeHackRoutePressure;
			if (unsafeTarget != unsafeBest)
				return unsafeTarget == false;
			if (maxPressure != bestMaxPressure)
				return maxPressure < bestMaxPressure;
			if (score != bestScore)
				return score < bestScore;
			if (pathLength != bestPathLength)
				return pathLength < bestPathLength;
			if (Math.Abs(value - bestValue) > 0.01f)
				return value > bestValue;
			return distance < bestDistance;
		}

		internal static Thing BestReachableHackTarget(this JobDriver_Sabotage driver, IEnumerable<Thing> targets)
		{
			var zombie = driver.pawn as Zombie;
			if (zombie?.Spawned != true || targets == null)
				return null;

			var map = zombie.Map;
			var memory = driver.AlbinoMemory();
			var targetArray = targets
				.Where(target => driver.CanSelectHackThing(target, memory))
				.Where(target => driver.IsDeferredHackTarget(target) == false)
				.Where(target => driver.IsRecentlyHackedTargetPaused(target) == false)
				.ToArray();
			if (targetArray.Length == 0)
				return null;

			var sources = AlbinoPressureSourcesFor(zombie);
			var candidates = targetArray
				.OrderBy(target => zombie.Position.DistanceToSquared(target.Position))
				.Take(albinoHackTargetCandidateLimit)
				.Concat(targetArray.OrderByDescending(target => AlbinoHackTargetValue(map, target)).Take(albinoHackTargetCandidateLimit / 2))
				.Distinct()
				.ToArray();

			Thing best = null;
			var bestScore = int.MaxValue;
			var bestMaxPressure = 0;
			var bestPathLength = int.MaxValue;
			var bestValue = 0f;
			var bestDistance = int.MaxValue;
			foreach (var target in candidates)
			{
				if (TryScoreAlbinoHackTarget(driver, target, sources, out var score, out var maxPressure, out var pathLength) == false)
					continue;
				if (maxPressure >= albinoNoSafeHackRoutePressure)
					continue;

				var value = AlbinoHackTargetValue(map, target);
				var distance = zombie.Position.DistanceToSquared(target.Position);
				if (best == null || AlbinoHackTargetIsBetter(score, maxPressure, pathLength, value, distance, bestScore, bestMaxPressure, bestPathLength, bestValue, bestDistance))
				{
					best = target;
					bestScore = score;
					bestMaxPressure = maxPressure;
					bestPathLength = pathLength;
					bestValue = value;
					bestDistance = distance;
				}
			}

			return best;
		}

		static PawnPath FindPressureAwareHackPath(this JobDriver_Sabotage driver, Thing target)
		{
			var zombie = driver.pawn as Zombie;
			if (zombie?.Spawned != true || target?.Spawned != true)
				return null;

			var map = zombie.Map;
			var traverseParms = TraverseParms.For(zombie, Danger.None, TraverseMode.PassDoors, false);
			var sources = AlbinoPressureSourcesFor(zombie);
			var hasPressureSources = HasAlbinoPressureSources(sources);
			PawnPath cachedPath = null;
			var cachedScore = int.MaxValue;
			var cachedMaxPressure = 0;
			if (driver.hackApproachCell.IsValid)
			{
				cachedPath = map.pathFinder.FindPathNow(zombie.Position, driver.hackApproachCell, traverseParms, null, PathEndMode.OnCell, null);
				if (cachedPath.Found)
				{
					if (cachedPath.TryFindLastCellBeforeBlockingDoor(zombie, out _, out _))
					{
						cachedPath.ReleaseToPool();
						cachedPath = null;
						driver.hackApproachCell = IntVec3.Invalid;
					}
					else if (TryFindDangerousAlbinoDoorExit(zombie, cachedPath, sources, out _, out _, out _))
					{
						cachedPath.ReleaseToPool();
						cachedPath = null;
						driver.hackApproachCell = IntVec3.Invalid;
						driver.noSafeHackRoute = true;
					}
					else if (hasPressureSources)
					{
						cachedScore = AlbinoPathPressureScore(zombie, cachedPath, sources, out cachedMaxPressure);
						driver.noSafeHackRoute = cachedMaxPressure >= albinoNoSafeHackRoutePressure;
						if (driver.noSafeHackRoute == false)
							return cachedPath;
					}
					else
					{
						driver.noSafeHackRoute = false;
						return cachedPath;
					}
				}
				else
				{
					cachedPath.ReleaseToPool();
					cachedPath = null;
					driver.hackApproachCell = IntVec3.Invalid;
				}
			}

			if (hasPressureSources == false)
			{
				driver.noSafeHackRoute = false;
				return null;
			}

			PawnPath bestPath = null;
			var bestCell = IntVec3.Invalid;
			var bestScore = int.MaxValue;
			var bestMaxPressure = 0;
			foreach (var candidate in HackApproachCandidates(zombie, target)
				.OrderBy(cell => zombie.Position.DistanceToSquared(cell))
				.Take(albinoHackApproachCandidateLimit))
			{
				var path = map.pathFinder.FindPathNow(zombie.Position, candidate, traverseParms, null, PathEndMode.OnCell, null);
				if (path.Found == false)
				{
					path.ReleaseToPool();
					continue;
				}

				if (TryFindDangerousAlbinoDoorExit(zombie, path, sources, out _, out _, out _))
				{
					path.ReleaseToPool();
					continue;
				}

				var score = AlbinoPathPressureScore(zombie, path, sources, out var maxPressure);
				if (bestPath == null || AlbinoHackPathIsBetter(score, maxPressure, bestScore, bestMaxPressure))
				{
					bestPath?.ReleaseToPool();
					bestPath = path;
					bestCell = candidate;
					bestScore = score;
					bestMaxPressure = maxPressure;
				}
				else
					path.ReleaseToPool();
			}

			if (cachedPath != null && (bestPath == null || AlbinoHackPathIsBetter(cachedScore, cachedMaxPressure, bestScore, bestMaxPressure)))
			{
				bestPath?.ReleaseToPool();
				bestPath = cachedPath;
				bestCell = driver.hackApproachCell;
				bestScore = cachedScore;
				bestMaxPressure = cachedMaxPressure;
			}
			else
				cachedPath?.ReleaseToPool();

			if (bestPath != null)
			{
				driver.hackApproachCell = bestCell;
				driver.noSafeHackRoute = bestMaxPressure >= albinoNoSafeHackRoutePressure;
			}
			else
				driver.noSafeHackRoute = false;
			return bestPath;
		}

		static PawnPath FindPressureAwareCellPath(this JobDriver_Sabotage driver, IntVec3 cell, bool allowPressure)
		{
			var zombie = driver.pawn as Zombie;
			if (zombie?.Spawned != true || cell.IsValid == false)
				return null;

			var path = zombie.Map.pathFinder.FindPathNow(
				zombie.Position,
				cell,
				TraverseParms.For(zombie, Danger.None, TraverseMode.PassDoors, false),
				null,
				PathEndMode.OnCell,
				null);
			if (path.Found == false)
			{
				path.ReleaseToPool();
				return null;
			}

			var sources = AlbinoPressureSourcesFor(zombie);
			if (TryFindDangerousAlbinoDoorExit(zombie, path, sources, out _, out _, out _))
			{
				driver.noSafeHackRoute = true;
				path.ReleaseToPool();
				return null;
			}

			if (HasAlbinoPressureSources(sources))
			{
				_ = AlbinoPathPressureScore(zombie, path, sources, out var maxPressure);
				driver.noSafeHackRoute = maxPressure >= albinoNoSafeHackRoutePressure;
				if (allowPressure == false && driver.noSafeHackRoute)
				{
					path.ReleaseToPool();
					return null;
				}
			}
			else
				driver.noSafeHackRoute = false;

			return path;
		}

		static PawnPath FindFallbackCellPath(this JobDriver_Sabotage driver, IntVec3 cell, AlbinoPressureSources sources, TraverseParms traverseParms)
		{
			var zombie = driver.pawn as Zombie;
			if (zombie?.Spawned != true || cell.IsValid == false || cell == zombie.Position)
				return null;

			var map = zombie.Map;
			if (map.reachability.CanReach(zombie.Position, cell, PathEndMode.OnCell, traverseParms) == false)
				return null;

			var path = map.pathFinder.FindPathNow(zombie.Position, cell, traverseParms, null, PathEndMode.OnCell, null);
			if (path.Found == false)
			{
				path.ReleaseToPool();
				return null;
			}

			if (path.TryFindLastCellBeforeBlockingDoor(zombie, out _, out _)
				|| TryFindDangerousAlbinoDoorExit(zombie, path, sources, out _, out _, out _))
			{
				path.ReleaseToPool();
				return null;
			}

			if (HasAlbinoPressureSources(sources))
			{
				_ = AlbinoPathPressureScore(zombie, path, sources, out var maxPressure);
				driver.noSafeHackRoute = maxPressure >= albinoNoSafeHackRoutePressure;
				if (driver.noSafeHackRoute)
				{
					path.ReleaseToPool();
					return null;
				}
			}
			else
				driver.noSafeHackRoute = false;

			return path;
		}

		static IEnumerable<IntVec3> DefensiveHackRouteSamples(Zombie zombie, Thing target)
		{
			yield return zombie.Position;
			if (zombie.pather?.Moving == true && zombie.pather.Destination.Cell.IsValid)
				yield return zombie.pather.Destination.Cell;
			if (target?.Spawned == true)
				yield return target.Position;

			var path = zombie.pather?.curPath;
			if (path?.Found != true || path.NodesLeftCount <= 0)
				yield break;

			var step = Math.Max(1, path.NodesReversed.Count / albinoDefensiveScreamPathSamples);
			var yielded = 0;
			for (var i = 0; i < path.NodesReversed.Count && yielded < albinoDefensiveScreamPathSamples; i += step)
			{
				var cell = path.NodesReversed[i];
				if (cell.IsValid)
				{
					yielded++;
					yield return cell;
				}
			}
		}

		static bool HackRouteIsContested(Zombie zombie, Thing target, AlbinoPressureSources sources)
		{
			if (UrgentRangedThreatsAreInEarlyScreamReach(zombie, sources) == false)
				return false;

			var screamTargets = AlbinoScreamAffectablePressurePawns(zombie, sources.pawns).ToList();
			var screamTargetsInRange = screamTargets.Count(pawn => pawn.Position.DistanceToSquared(zombie.Position) <= albinoScreamMaxRadius * albinoScreamMaxRadius);
			if (screamTargetsInRange == 0)
				return false;
			var screamTargetsInEarlyRange = screamTargets.Count(pawn => pawn.Position.DistanceToSquared(zombie.Position) <= albinoDefensiveScreamEarlyRadiusSquared);

			var localPressure = AlbinoPressureAtCell(zombie, zombie.Position, sources);
			var maxPressure = 0;
			foreach (var cell in DefensiveHackRouteSamples(zombie, target))
				maxPressure = Math.Max(maxPressure, AlbinoPressureAtCell(zombie, cell, sources));

			var pressure = Math.Max(localPressure, maxPressure);
			if (screamTargetsInEarlyRange >= 2 && pressure >= albinoDefensiveScreamSoftPressure)
				return true;

			var immediateThreat = HasImmediateAlbinoScreamThreat(zombie, sources);
			if (immediateThreat && pressure >= albinoDefensiveScreamSoftPressure)
				return true;

			return pressure >= albinoDefensiveScreamMinPressure && immediateThreat;
		}

		static int CurrentAlbinoRoutePressure(Zombie zombie, Thing target, AlbinoPressureSources sources)
		{
			var maxPressure = AlbinoPressureAtCell(zombie, zombie.Position, sources);
			foreach (var cell in DefensiveHackRouteSamples(zombie, target))
				maxPressure = Math.Max(maxPressure, AlbinoPressureAtCell(zombie, cell, sources));
			return maxPressure;
		}

		static int ImmediateAlbinoMovementPressure(Zombie zombie, AlbinoPressureSources sources)
		{
			if (zombie?.Spawned != true || sources == null)
				return 0;

			var maxPressure = AlbinoPressureAtCell(zombie, zombie.Position, sources);
			if (zombie?.pather?.Moving != true)
				return maxPressure;

			var destination = zombie.pather.Destination.Cell;
			if (destination.IsValid)
				maxPressure = Math.Max(maxPressure, AlbinoPressureAtCell(zombie, destination, sources));

			var path = zombie.pather.curPath;
			if (path?.Found != true || path.NodesLeftCount <= 0)
				return maxPressure;

			var count = Math.Min(path.NodesLeftCount, 5);
			for (var i = 0; i < count; i++)
			{
				var cell = path.Peek(i);
				if (cell.IsValid)
					maxPressure = Math.Max(maxPressure, AlbinoPressureAtCell(zombie, cell, sources));
			}
			return maxPressure;
		}

		static bool TryStartAlbinoSafetyMove(this JobDriver_Sabotage driver, Zombie zombie, AlbinoPressureSources sources, bool forceUnsafeRouteMove = false)
		{
			var map = zombie?.Map;
			if (map == null)
				return false;
			var currentPressure = ImmediateAlbinoMovementPressure(zombie, sources);
			if (forceUnsafeRouteMove == false && currentPressure < albinoNoSafeHackRoutePressure)
				return false;
			var pressureLimit = forceUnsafeRouteMove ? Math.Max(currentPressure, albinoNoSafeHackRoutePressure) : currentPressure;

			var traverseParms = TraverseParms.For(zombie, Danger.None, TraverseMode.PassDoors, false);
			var target = driver.hackTarget ?? (Thing)driver.door;
			var hasTarget = target?.Spawned == true && target.Map == map;
			var currentTargetDistance = hasTarget ? zombie.Position.DistanceToSquared(target.Position) : 0;
			PawnPath bestPath = null;
			var bestCell = IntVec3.Invalid;
			var bestScore = int.MaxValue;
			var bestMaxPressure = int.MaxValue;
			var bestCellPressure = int.MaxValue;
			var bestTargetDistance = int.MaxValue;
			var checkedPaths = 0;
			foreach (var cell in GenRadial.RadialCellsAround(zombie.Position, 14f, false))
			{
				if (cell.InBounds(map) == false || cell.Standable(map) == false || cell.Fogged(map))
					continue;
				if (cell.GetEdifice(map) != null)
					continue;
				if (cell.GetThingList(map).Any(thing => thing is Pawn))
					continue;

				var cellPressure = AlbinoPressureAtCell(zombie, cell, sources);
				if (cellPressure >= pressureLimit)
					continue;
				if (checkedPaths >= albinoSafetyMoveCandidateLimit)
					break;

				checkedPaths++;
				var path = map.pathFinder.FindPathNow(zombie.Position, cell, traverseParms, null, PathEndMode.OnCell, null);
				if (path.Found == false)
				{
					path.ReleaseToPool();
					continue;
				}
				if (path.TryFindLastCellBeforeBlockingDoor(zombie, out _, out _) || TryFindDangerousAlbinoDoorExit(zombie, path, sources, out _, out _, out _))
				{
					path.ReleaseToPool();
					continue;
				}

				var score = AlbinoPathPressureScore(zombie, path, sources, out var maxPressure);
				var targetDistance = hasTarget ? cell.DistanceToSquared(target.Position) : currentTargetDistance;
				if (cellPressure < bestCellPressure
					|| cellPressure == bestCellPressure && maxPressure < bestMaxPressure
					|| cellPressure == bestCellPressure && maxPressure == bestMaxPressure && score < bestScore
					|| cellPressure == bestCellPressure && maxPressure == bestMaxPressure && score == bestScore && targetDistance < bestTargetDistance)
				{
					bestPath?.ReleaseToPool();
					bestPath = path;
					bestCell = cell;
					bestScore = score;
					bestMaxPressure = maxPressure;
					bestCellPressure = cellPressure;
					bestTargetDistance = targetDistance;
				}
				else
					path.ReleaseToPool();
			}

			if (bestPath == null)
				return false;

			driver.pawn.pather?.StopDead();
			driver.destination = bestCell;
			driver.door = null;
			driver.doorExitCell = IntVec3.Invalid;
			driver.queuedScreamCell = IntVec3.Invalid;
			driver.queuedMoveCell = IntVec3.Invalid;
			driver.MarkStrategicDestination(true, true);
			driver.waitCounter = 0;
			if (target != null && target == driver.hackTarget)
				driver.DeferUnsafeHackTarget(target);
			driver.InterruptHackProgress(true);
			if (zombie.scream == -2)
				zombie.scream = -1;
			driver.defensiveScreamQueued = false;
			bestPath.ReleaseToPool();
			zombie.pather.StartPath(bestCell, PathEndMode.OnCell);
			return true;
		}

		static bool TryRedirectUnsafeRouteWithoutScream(this JobDriver_Sabotage driver, Zombie zombie, Thing target, AlbinoPressureSources sources)
		{
			var routePressure = CurrentAlbinoRoutePressure(zombie, target, sources);
			if (routePressure < albinoNoSafeHackRoutePressure)
				return false;
			if (HasAlbinoDefensiveScreamPayoff(zombie, sources))
				return false;

			return driver.TryStartAlbinoSafetyMove(zombie, sources);
		}

		public static bool ReconsiderInterruptibleDestination(this JobDriver_Sabotage driver)
		{
			var zombie = driver.pawn as Zombie;
			if (zombie?.Spawned != true || driver.interruptibleDestination == false || driver.destination.IsValid == false)
				return false;

			var ticks = GenTicks.TicksGame;
			var cellChanged = driver.lastStrategicRecheckCell != zombie.Position;
			if (cellChanged == false && ticks < driver.nextStrategicRecheckTick)
				return false;
			if (cellChanged && ticks < driver.nextStrategicRecheckTick)
				return false;

			driver.lastStrategicRecheckCell = zombie.Position;
			driver.nextStrategicRecheckTick = ticks + albinoStrategicRecheckCooldownTicks;

			var sources = AlbinoPressureSourcesFor(zombie);
			var target = driver.hackTarget ?? (Thing)driver.door;
			var routePressure = CurrentAlbinoRoutePressure(zombie, target, sources);
			var routeUnsafe = routePressure >= albinoNoSafeHackRoutePressure;
			var immediatePressure = ImmediateAlbinoMovementPressure(zombie, sources);

			if (driver.safetyDestination)
			{
				if (driver.CanResumeHackTarget())
				{
					var hackTarget = driver.hackTarget;
					if (TryScoreAlbinoHackTarget(driver, hackTarget, sources, out _, out var targetPressure, out _) && targetPressure < albinoNoSafeHackRoutePressure)
					{
						zombie.pather?.StopDead();
						driver.ClearStrategicDestination();
						driver.waitCounter = 0;
						return driver.Goto(hackTarget);
					}
				}

				if (zombie.pather?.Moving == true && zombie.Position != driver.destination)
					return false;

				if (immediatePressure >= albinoNoSafeHackRoutePressure)
					return false;

				zombie.pather?.StopDead();
				driver.ClearStrategicDestination();
				driver.waitCounter = 12;
				if (driver.TryChoosePrimarySabotageTarget())
					return true;
				if (driver.TryChooseDesperationSabotageTarget())
					return true;
				return true;
			}

			return driver.TryChoosePrimarySabotageTarget();
		}

		static bool HasImmediateAlbinoScreamThreat(Zombie zombie, AlbinoPressureSources sources)
		{
			foreach (var pawn in AlbinoScreamAffectablePressurePawns(zombie, sources.pawns))
			{
				var distance = pawn.Position.DistanceToSquared(zombie.Position);
				if (distance <= 9)
					return true;
				if (distance <= 25 && IsAttackingOrApproaching(pawn, zombie))
					return true;
			}
			return false;
		}

		static bool IsAimingAtZombie(Pawn pawn, Zombie zombie)
		{
			return pawn?.Spawned == true
				&& zombie?.Spawned == true
				&& LocalTargetPointsAtZombie(pawn.TargetCurrentlyAimingAt, zombie);
		}

		static bool IsUrgentRangedAlbinoThreat(Pawn pawn, Zombie zombie)
		{
			return IsAimingAtZombie(pawn, zombie)
				&& HasUsableRangedVerb(pawn, out _)
				&& CanShootThing(pawn, zombie);
		}

		static bool UrgentRangedThreatsAreInEarlyScreamReach(Zombie zombie, AlbinoPressureSources sources)
		{
			if (zombie?.Spawned != true || sources == null)
				return true;

			foreach (var pawn in AlbinoScreamAffectablePressurePawns(zombie, sources.pawns))
				if (IsUrgentRangedAlbinoThreat(pawn, zombie)
					&& pawn.Position.DistanceToSquared(zombie.Position) > albinoUrgentScreamRadiusSquared)
					return false;
			return true;
		}

		static bool HasAlbinoDefensiveScreamPayoff(Zombie zombie, AlbinoPressureSources sources)
		{
			if (UrgentRangedThreatsAreInEarlyScreamReach(zombie, sources) == false)
				return false;

			var inRange = AlbinoScreamAffectablePressurePawns(zombie, sources.pawns)
				.Where(pawn => pawn.Position.DistanceToSquared(zombie.Position) <= albinoScreamMaxRadius * albinoScreamMaxRadius)
				.ToList();
			var earlyRange = inRange.Count(pawn => pawn.Position.DistanceToSquared(zombie.Position) <= albinoDefensiveScreamEarlyRadiusSquared);
			return earlyRange >= 2 || HasImmediateAlbinoScreamThreat(zombie, sources);
		}

		static bool AlbinoDefensiveEmergencyScreamReady(Zombie zombie, AlbinoPressureSources sources)
		{
			if (zombie?.Spawned != true || sources == null || zombie.albinoNextScreamTick < 0)
				return false;
			var ticksUntilReady = AlbinoScreamTicksUntilReady(zombie);
			if (ticksUntilReady <= 0 || ticksUntilReady > albinoDefensiveEmergencyScreamMaxRemainingTicks)
				return false;
			return HasAlbinoDefensiveScreamPayoff(zombie, sources)
				&& HasAlbinoDefensiveEmergencyThreat(zombie, sources);
		}

		static bool HasAlbinoDefensiveEmergencyThreat(Zombie zombie, AlbinoPressureSources sources)
		{
			foreach (var pawn in AlbinoScreamAffectablePressurePawns(zombie, sources.pawns))
			{
				if (pawn.Position.DistanceToSquared(zombie.Position) > albinoDefensiveEmergencyScreamThreatRadiusSquared)
					continue;
				if (IsUrgentRangedAlbinoThreat(pawn, zombie) || IsAttackingOrApproaching(pawn, zombie) || CanShootThing(pawn, zombie))
					return true;
			}
			return false;
		}

		public static bool TrySwitchContestedHackToScream(this JobDriver_Sabotage driver)
		{
			var zombie = driver.pawn as Zombie;
			var target = driver.hackTarget ?? (Thing)driver.door;
			if (zombie?.Spawned != true)
				return false;
			var interruptQueuedPlannedScream = zombie.scream == -2
				&& driver.defensiveScreamQueued == false
				&& driver.destination.IsValid;
			if (zombie.scream != -1 && interruptQueuedPlannedScream == false)
				return false;
			var hasActiveRoute = target != null || driver.destination.IsValid || zombie.pather?.Moving == true;
			if (hasActiveRoute == false && driver.noSafeHackRoute == false)
				return false;
			var ticks = GenTicks.TicksGame;
			var cellChanged = driver.lastDefensiveScreamCheckCell != zombie.Position;
			var timedCheckReady = ticks >= driver.nextDefensiveScreamCheckTick;
			var cellCheckReady = cellChanged && ticks >= driver.nextDefensiveScreamCellCheckTick;
			if (timedCheckReady == false && cellCheckReady == false)
				return false;

			driver.lastDefensiveScreamCheckCell = zombie.Position;
			driver.nextDefensiveScreamCheckTick = ticks + albinoDefensiveScreamCheckIntervalTicks;
			driver.nextDefensiveScreamCellCheckTick = ticks + albinoDefensiveScreamCellChangeCooldownTicks;
			var pressureSources = AlbinoPressureSourcesFor(zombie);
			var routePressure = CurrentAlbinoRoutePressure(zombie, target, pressureSources);
			var routeContested = HackRouteIsContested(zombie, target, pressureSources);
			var screamReady = interruptQueuedPlannedScream || AlbinoScreamReady(zombie) || AlbinoDefensiveEmergencyScreamReady(zombie, pressureSources);
			if (screamReady == false)
			{
				if (routePressure >= albinoNoSafeHackRoutePressure && driver.IsRushingHackTarget(target) == false)
					return driver.TryStartAlbinoSafetyMove(zombie, pressureSources);
				return false;
			}

			if (routeContested == false && driver.TryRedirectUnsafeRouteWithoutScream(zombie, target, pressureSources))
				return true;
			if (routeContested == false && (driver.noSafeHackRoute == false || HasAlbinoDefensiveScreamPayoff(zombie, pressureSources) == false))
				return false;

			driver.pawn.pather?.StopDead();
			driver.ClearStrategicDestination();
			driver.waitCounter = 0;
			driver.InterruptHackProgress(true);
			zombie.scream = -2;
			driver.defensiveScreamQueued = true;
			zombie.Rotation = Rot4.FromAngleFlat((ClosestAlbinoScreamTargetPosition(zombie) - zombie.Position).AngleFlat);
			return true;
		}

		static IntVec3 ClosestAlbinoScreamTargetPosition(Zombie zombie)
		{
			return zombie.Map.mapPawns.AllPawnsSpawned
				.Where(pawn => CanAlbinoScreamAffect(pawn, zombie))
				.Where(pawn => pawn.Position.DistanceToSquared(zombie.Position) <= albinoScreamMaxRadius * albinoScreamMaxRadius)
				.OrderBy(pawn => pawn.Position.DistanceToSquared(zombie.Position))
				.Select(pawn => pawn.Position)
				.DefaultIfEmpty(zombie.Position)
				.First();
		}

		static bool Goto(this JobDriver_Sabotage driver, Thing thing)
		{
			driver.MarkStrategicDestination(false, false);
			if (driver.CanSelectHackThing(thing) == false || driver.pawn?.Map != thing.Map)
			{
				driver.ClearHackTarget();
				return false;
			}
			if (driver.IsDeferredHackTarget(thing) && driver.IsRushingHackTarget(thing) == false)
			{
				driver.ClearHackTarget();
				return false;
			}
			if (driver.hackTarget != thing && driver.IsRecentlyHackedTargetPaused(thing))
			{
				driver.ClearHackTarget();
				return false;
			}

			driver.queuedScreamCell = IntVec3.Invalid;
			driver.queuedMoveCell = IntVec3.Invalid;
			driver.SetHackTarget(thing);
			var zombie = driver.pawn;
			var mode = HackPathEndMode(thing);
			var path = driver.FindPressureAwareHackPath(thing);
			if (path == null)
			{
				driver.noSafeHackRoute = false;
				path = zombie.Map.pathFinder.FindPathNow(zombie.Position, thing, TraverseParms.For(zombie, Danger.None, TraverseMode.PassDoors, false), null, mode, null);
			}
			if (path.Found)
			{
				var sources = AlbinoPressureSourcesFor((Zombie)zombie);
				if (TryFindDangerousAlbinoDoorExit((Zombie)zombie, path, sources, out _, out _, out _))
				{
					driver.noSafeHackRoute = true;
					path.ReleaseToPool();
					driver.ClearHackTarget();
					return false;
				}
				if (HasAlbinoPressureSources(sources))
				{
					_ = AlbinoPathPressureScore((Zombie)zombie, path, sources, out var maxPressure);
					driver.noSafeHackRoute = maxPressure >= albinoNoSafeHackRoutePressure;
				}
				else
					driver.noSafeHackRoute = false;

				if (path.TryFindLastCellBeforeBlockingDoor(zombie, out var doorCell, out var door, out var doorExitCell) && doorCell.IsValid)
				{
					driver.door = door;
					driver.doorExitCell = doorExitCell;
					driver.destination = doorCell == zombie.Position ? IntVec3.Invalid : doorCell;
					driver.SetHackTarget(thing);
					path.ReleaseToPool();
					if (doorCell == zombie.Position)
					{
						zombie.pather?.StopDead();
						return true;
					}
					zombie.pather.StartPath(doorCell, PathEndMode.OnCell);
					return true;
				}
				else if (path.NodesLeftCount > 0)
				{
					var cell = path.NodesReversed[0];
					if (cell.IsValid)
					{
						driver.destination = cell == zombie.Position ? IntVec3.Invalid : cell;
						driver.SetHackTarget(thing);
						path.ReleaseToPool();
						if (cell == zombie.Position)
						{
							zombie.pather?.StopDead();
							return true;
						}
						zombie.pather.StartPath(cell, PathEndMode.OnCell);
						return true;
					}
				}
			}
			path.ReleaseToPool();
			driver.ClearHackTarget();
			return false;
		}

		static bool Goto(this JobDriver_Sabotage driver, IntVec3 cell, Action arrivalAction = null)
		{
			driver.ClearHackTarget();
			driver.MarkStrategicDestination(false, false);
			if (cell.IsValid == false)
				return false;

			driver.queuedScreamCell = IntVec3.Invalid;
			driver.queuedMoveCell = IntVec3.Invalid;
			var zombie = driver.pawn;
			if (cell == zombie.Position)
			{
				driver.destination = IntVec3.Invalid;
				zombie.pather?.StopDead();
				if (arrivalAction == null)
					return false;
				arrivalAction();
				return true;
			}

			var path = driver.FindPressureAwareCellPath(cell, arrivalAction != null);
			if (path == null)
				return false;
			if (path.Found)
			{
				driver.MarkStrategicDestination(arrivalAction == null, false);
				if (path.TryFindLastCellBeforeBlockingDoor(zombie, out var doorCell, out var door, out var doorExitCell) && doorCell.IsValid)
				{
					driver.door = door;
					driver.doorExitCell = doorExitCell;
					driver.destination = doorCell == zombie.Position ? IntVec3.Invalid : doorCell;
					if (arrivalAction != null)
						driver.queuedScreamCell = cell;
					else
						driver.queuedMoveCell = cell;
					path.ReleaseToPool();
					if (doorCell == zombie.Position)
					{
						zombie.pather?.StopDead();
						return true;
					}
					zombie.pather.StartPath(doorCell, PathEndMode.OnCell);
					return true;
				}
				else
				{
					driver.destination = cell;
					driver.queuedScreamCell = IntVec3.Invalid;
					driver.queuedMoveCell = IntVec3.Invalid;
					path.ReleaseToPool();
					zombie.pather.StartPath(cell, PathEndMode.OnCell);
					arrivalAction?.Invoke();
					return true;
				}
			}
			path.ReleaseToPool();
			return false;
		}

		static bool TryContinueAfterDoorOpened(this JobDriver_Sabotage driver, Thing hackTarget, IntVec3 queuedScreamCell, IntVec3 queuedMoveCell)
		{
			driver.destination = IntVec3.Invalid;
			driver.door = null;
			driver.doorExitCell = IntVec3.Invalid;
			driver.hackCounter = 0;

			if (hackTarget?.Spawned == true && driver.pawn?.Map == hackTarget.Map && driver.CanSelectHackThing(hackTarget))
			{
				if (driver.Goto(hackTarget))
					return true;

				var zombie = driver.pawn as Zombie;
				if (zombie?.Spawned != true)
				{
					driver.ClearHackTarget();
					return false;
				}

				driver.SetHackTarget(hackTarget);
				driver.destination = IntVec3.Invalid;
				driver.door = null;
				driver.doorExitCell = IntVec3.Invalid;
				driver.queuedScreamCell = IntVec3.Invalid;
				driver.queuedMoveCell = IntVec3.Invalid;
				driver.noSafeHackRoute = true;
				driver.waitCounter = 0;
				driver.nextDefensiveScreamCheckTick = 0;
				driver.nextDefensiveScreamCellCheckTick = 0;
				driver.lastDefensiveScreamCheckCell = IntVec3.Invalid;
				if (driver.TrySwitchContestedHackToScream())
					return true;

				return driver.TryStartAlbinoSafetyMove(zombie, AlbinoPressureSourcesFor(zombie), true);
			}

			if (hackTarget != null)
				driver.ClearHackTarget();

			if (queuedScreamCell.IsValid)
				return driver.Goto(queuedScreamCell, () => ((Zombie)driver.pawn).scream = -2);
			if (queuedMoveCell.IsValid)
				return driver.Goto(queuedMoveCell);
			return false;
		}

		static bool TryAvoidDangerousDoorHack(this JobDriver_Sabotage driver, Building_Door door)
		{
			var zombie = driver.pawn as Zombie;
			if (zombie?.Spawned != true || door?.Spawned != true || driver.doorExitCell.IsValid == false)
				return false;

			var sources = AlbinoPressureSourcesFor(zombie);
			var exitPressure = AlbinoDoorOpenPressureAtCell(zombie, door, driver.doorExitCell, sources);
			if (exitPressure < albinoNoSafeHackRoutePressure)
				return false;

			driver.hackCounter = 0;
			driver.noSafeHackRoute = true;
			driver.waitCounter = 0;
			driver.nextDefensiveScreamCheckTick = 0;
			driver.nextDefensiveScreamCellCheckTick = 0;
			driver.lastDefensiveScreamCheckCell = IntVec3.Invalid;

			if ((AlbinoScreamReady(zombie) || AlbinoDefensiveEmergencyScreamReady(zombie, sources)) && HasAlbinoDefensiveScreamPayoff(zombie, sources))
			{
				zombie.pather?.StopDead();
				driver.ClearStrategicDestination();
				driver.InterruptHackProgress(true);
				zombie.scream = -2;
				driver.defensiveScreamQueued = true;
				zombie.Rotation = Rot4.FromAngleFlat((ClosestAlbinoScreamTargetPosition(zombie) - zombie.Position).AngleFlat);
				return true;
			}

			if (driver.TryStartAlbinoSafetyMove(zombie, sources, true))
				return true;

			zombie.pather?.StopDead();
			driver.ClearStrategicDestination();
			driver.ClearHackTarget();
			driver.waitCounter = 12;
			return true;
		}

		static bool Hack(this JobDriver_Sabotage driver, Thing thing, Action action)
		{
			if (driver.hackCounter == 0)
			{
				driver.PlayHackStartSound(thing);
				Tools.CastThoughtBubble(driver.pawn, Constants.HACKING);
				driver.hackCounter = 240;
				return true;
			}

			if (driver.hackCounter > 0)
			{
				driver.hackCounter--;
				if (driver.hackCounter == 0)
					action();
				return true;
			}

			return false;
		}

		static void PlayHackStartSound(this JobDriver_Sabotage driver, Thing thing)
		{
			if (ZombieAwarenessCues.ShouldPlayWallAndSabotageSound() == false || thing?.Spawned != true)
				return;

			var target = new TargetInfo(thing.Position, thing.Map, false);
			if (thing is Building_Door)
			{
				CustomDefs.Hacking.PlayOneShot(target);
				return;
			}

			var sound = CustomDefs.HackingLocal ?? CustomDefs.Hacking;
			sound?.PlayOneShot(SoundInfo.InMap(target));
		}

		public static bool HackThing(this JobDriver_Sabotage driver)
		{
			if (driver.destination.IsValid)
				return false;

			if (driver.ResumeDoorTargetIfPassable())
				return true;

			var door = driver.door;
			if (door.BlocksAlbinoSabotagePath(driver.pawn))
			{
				if (driver.TryAvoidDangerousDoorHack(door))
					return true;

				return driver.Hack(door, () =>
				{
					var hackTarget = driver.hackTarget;
					var queuedScreamCell = driver.queuedScreamCell;
					var queuedMoveCell = driver.queuedMoveCell;
					driver.pawn.rotationTracker.FaceTarget(door);
					door.StartManualOpenBy(driver.pawn);
					door.ticksUntilClose *= 4;
					driver.door = null;
					driver.doorExitCell = IntVec3.Invalid;
					driver.waitCounter = albinoDoorOpenResumeWaitTicks;
					_ = driver.TryContinueAfterDoorOpened(hackTarget, queuedScreamCell, queuedMoveCell);
				});
			}

			var thing = driver.hackTarget;
			if (thing != null)
			{
				if (driver.IsDeferredHackTarget(thing) && driver.IsRushingHackTarget(thing) == false)
				{
					driver.ClearHackTarget();
					return false;
				}

				if (driver.CanHackTargetNow(thing) == false)
					return driver.RepathOrClearHackTarget(thing);

				return driver.Hack(thing, () =>
				{
					var compFlickable = thing.TryGetComp<CompFlickable>();
					if (compFlickable != null && compFlickable.SwitchIsOn)
					{
						compFlickable.SwitchIsOn = false;
						driver.pawn.rotationTracker.FaceTarget(thing);
						if (ZombieAwarenessCues.ShouldPlayWallAndSabotageSound())
							SoundDefOf.FlickSwitch.PlayOneShot(new TargetInfo(thing.Position, thing.Map, false));
						Tools.CastThoughtBubble(driver.pawn, Constants.HACKING);
						driver.PauseRecentlyHackedTarget(thing);
						driver.ClearHackTarget();
						return;
					}

					var compBreakdownable = thing.TryGetComp<CompBreakdownable>();
					if (compBreakdownable != null && compBreakdownable.BrokenDown == false)
					{
						compBreakdownable.DoBreakdown();
						driver.pawn.rotationTracker.FaceTarget(thing);
						if (ZombieAwarenessCues.ShouldPlayWallAndSabotageSound())
							SoundDefOf.FlickSwitch.PlayOneShot(new TargetInfo(thing.Position, thing.Map, false));
						Tools.CastThoughtBubble(driver.pawn, Constants.HACKING);
						driver.PauseRecentlyHackedTarget(thing);
						driver.ClearHackTarget();
						return;
					}

					if (thing.def.IsRangedWeapon && thing.def.useHitPoints)
					{
						driver.pawn.rotationTracker.FaceTarget(thing);
						Tools.CastThoughtBubble(driver.pawn, Constants.HACKING);
						var amount = Math.Max(1, thing.HitPoints / 2);
						_ = thing.TakeDamage(new DamageInfo(DamageDefOf.Deterioration, amount, 0, -1, driver.pawn));
						driver.RememberEnoughHackedItem(thing);
						driver.PauseRecentlyHackedTarget(thing);
						driver.ClearHackTarget();
						return;
					}

					driver.ClearHackTarget();
				});
			}

			return false;
		}

		static bool ResumeDoorTargetIfPassable(this JobDriver_Sabotage driver)
		{
			var door = driver.door;
			if (door == null)
				return false;

			if (door.BlocksAlbinoSabotagePath(driver.pawn))
				return false;

			driver.door = null;
			driver.doorExitCell = IntVec3.Invalid;
			driver.hackCounter = 0;

			var thing = driver.hackTarget;
			if (thing != null)
			{
				_ = driver.TryContinueAfterDoorOpened(thing, IntVec3.Invalid, IntVec3.Invalid);
				return true;
			}

			if (driver.queuedScreamCell.IsValid == false)
			{
				if (driver.queuedMoveCell.IsValid == false)
					return false;

				var moveCell = driver.queuedMoveCell;
				return driver.Goto(moveCell);
			}

			var screamCell = driver.queuedScreamCell;
			return driver.Goto(screamCell, () => ((Zombie)driver.pawn).scream = -2);
		}

		public static bool Scream(this JobDriver_Sabotage driver)
		{
			var zombie = driver.pawn as Zombie;

			if (zombie.scream == -1)
				return false;

			if (zombie.scream == -2)
			{
				if (driver.destination.IsValid == false)
				{
					var sources = AlbinoPressureSourcesFor(zombie);
					if (AlbinoScreamReady(zombie) == false
						&& (driver.defensiveScreamQueued == false || AlbinoDefensiveEmergencyScreamReady(zombie, sources) == false))
						return true;

					if (UrgentRangedThreatsAreInEarlyScreamReach(zombie, sources) == false
						|| driver.defensiveScreamQueued && HasAlbinoDefensiveScreamPayoff(zombie, sources) == false)
					{
						zombie.scream = -1;
						driver.queuedScreamCell = IntVec3.Invalid;
						driver.queuedMoveCell = IntVec3.Invalid;
						driver.defensiveScreamQueued = false;
						driver.waitCounter = albinoLostScreamTargetRecheckWaitTicks;
						return false;
					}

					if (zombie.HasAlbinoScreamTargetInRange(albinoScreamMaxRadius) == false)
					{
						zombie.scream = -1;
						driver.queuedScreamCell = IntVec3.Invalid;
						driver.queuedMoveCell = IntVec3.Invalid;
						driver.defensiveScreamQueued = false;
						SetAlbinoScreamCooldown(zombie, false);
						driver.waitCounter = albinoLostScreamTargetRecheckWaitTicks;
						return false;
					}

					driver.waitCounter = driver.defensiveScreamQueued
						? albinoDefensiveScreamWindupTicks
						: Rand.RangeInclusive(albinoScreamMinWindupTicks, albinoScreamMaxWindupTicks);
					zombie.scream = 0;
					zombie.Rotation = Rot4.FromAngleFlat((ClosestAlbinoScreamTargetPosition(zombie) - zombie.Position).AngleFlat);
				}
				return true;
			}

			if (zombie.scream == 0)
			{
				zombie.albinoScreamAffectedCount = 0;
				if (ZombieAwarenessCues.ShouldPlayWallAndSabotageSound())
					CustomDefs.Scream.PlayOneShot(new TargetInfo(zombie.Position, zombie.Map, false));
				Tools.CastThoughtBubble(driver.pawn, Constants.RAGING);
				driver.defensiveScreamQueued = false;
			}

			zombie.scream += 1;

			if (zombie.scream % 40 == 0)
			{
				var pos = zombie.Position;
				var d = 1 + (int)(zombie.scream * 12f / 401);
				var dist = d * d;
				var stunTicks = 60 * (14 - d);
				foreach (var pawn in zombie.Map.mapPawns.AllPawnsSpawned)
					if (CanAlbinoScreamAffect(pawn, zombie) && pawn.Position.DistanceToSquared(pos) <= dist)
					{
						if (RestUtility.Awake(pawn) == false)
							RestUtility.WakeUp(pawn);
						AlbinoScreamVomit.Start(pawn);
						pawn.stances.stunner.StunFor(stunTicks, zombie, true);
						zombie.albinoScreamAffectedCount++;
					}
			}

			if (zombie.scream >= albinoScreamDurationTicks)
			{
				SetAlbinoScreamCooldown(zombie, zombie.albinoScreamAffectedCount > 0);
				zombie.scream = -1;
				return false;
			}

			return zombie.scream < albinoScreamMovementReleaseTicks;
		}

		static List<Hediff_Injury> tmpHediffInjury = new();
		public static bool DieEasily(this JobDriver_Sabotage driver)
		{
			if (driver.pawn.health.Downed)
			{
				driver.pawn.Kill(null);
				return true;
			}
			tmpHediffInjury.Clear();
			driver.pawn.health.hediffSet.GetHediffs(ref tmpHediffInjury);
			if (tmpHediffInjury.Any())
			{
				driver.pawn.Kill(null);
				return true;
			}
			return false;
		}

		public static bool Wait(this JobDriver_Sabotage driver)
		{
			if (driver.waitCounter > 0)
			{
				var zombie = driver.pawn as Zombie;
				if (zombie?.Spawned == true && zombie.scream != 0)
				{
					var sources = AlbinoPressureSourcesFor(zombie);
					if (ImmediateAlbinoMovementPressure(zombie, sources) >= albinoNoSafeHackRoutePressure)
						driver.waitCounter = Math.Min(driver.waitCounter, albinoPressureRetryWaitTicks);
				}
				driver.waitCounter--;
				return true;
			}

			return false;
		}

		public static bool CheckAndFindDestination(this JobDriver_Sabotage driver)
		{
			if (driver.destination.IsValid)
				return true;

			var zombie = driver.pawn as Zombie;
			var map = zombie.Map;
			var pressureSources = AlbinoPressureSourcesFor(zombie);
			if (ImmediateAlbinoMovementPressure(zombie, pressureSources) >= albinoNoSafeHackRoutePressure)
				if (driver.TryStartAlbinoSafetyMove(zombie, pressureSources))
					return true;

			if (driver.ChooseSabotageTarget())
				return true;

			if (Rand.Chance(0.1f) && RCellFinder.TryFindRandomSpotJustOutsideColony(zombie.Position, map, null, out var cell))
				if (driver.GotoReachableFallbackCell(cell))
					return true;

			if (ImmediateAlbinoMovementPressure(zombie, pressureSources) >= albinoNoSafeHackRoutePressure && RCellFinder.TryFindDirectFleeDestination(zombie.Position, 16f, zombie, out cell))
				if (driver.Goto(cell))
					return true;

			if (driver.TryGotoNearbyFallbackCell())
				return true;

			driver.destination = IntVec3.Invalid;
			driver.CooldownAlbinoFallbackSearch();
			driver.waitCounter = 30;
			return false;
		}

		static bool TryGotoNearbyFallbackCell(this JobDriver_Sabotage driver)
		{
			var zombie = driver.pawn as Zombie;
			var map = zombie?.Map;
			if (map == null)
				return false;
			if (GenTicks.TicksGame < driver.nextFallbackMoveTick)
				return false;

			var sources = AlbinoPressureSourcesFor(zombie);
			var traverseParms = AlbinoFallbackTraverseParms(zombie);
			PawnPath bestPath = null;
			var bestCell = IntVec3.Invalid;
			var bestScore = int.MaxValue;
			var bestMaxPressure = int.MaxValue;
			var bestCellPressure = int.MaxValue;
			var checkedPaths = 0;

			foreach (var cell in GenRadial.RadialCellsAround(zombie.Position, 10f, false))
			{
				if (checkedPaths >= albinoNearbyFallbackMoveCandidateLimit)
					break;
				if (cell.InBounds(map) == false || cell.Standable(map) == false || cell.Fogged(map))
					continue;
				if (cell == driver.lastFallbackStartCell)
					continue;
				if (cell.DistanceToSquared(zombie.Position) < 9)
					continue;
				if (cell.GetEdifice(map) != null)
					continue;
				if (cell.GetFirstThing<Mineable>(map) != null)
					continue;
				if (cell.GetThingList(map).Any(thing => thing is Pawn))
					continue;

				checkedPaths++;
				if (map.reachability.CanReach(zombie.Position, cell, PathEndMode.OnCell, traverseParms) == false)
					continue;

				var path = map.pathFinder.FindPathNow(zombie.Position, cell, traverseParms, null, PathEndMode.OnCell, null);
				if (path.Found == false)
				{
					path.ReleaseToPool();
					continue;
				}
				if (path.TryFindLastCellBeforeBlockingDoor(zombie, out _, out _) || TryFindDangerousAlbinoDoorExit(zombie, path, sources, out _, out _, out _))
				{
					path.ReleaseToPool();
					continue;
				}

				var score = AlbinoPathPressureScore(zombie, path, sources, out var maxPressure);
				var cellPressure = AlbinoPressureAtCell(zombie, cell, sources);
				if (cellPressure < bestCellPressure
					|| cellPressure == bestCellPressure && maxPressure < bestMaxPressure
					|| cellPressure == bestCellPressure && maxPressure == bestMaxPressure && score < bestScore)
				{
					bestPath?.ReleaseToPool();
					bestPath = path;
					bestCell = cell;
					bestScore = score;
					bestMaxPressure = maxPressure;
					bestCellPressure = cellPressure;
				}
				else
					path.ReleaseToPool();
			}

			if (bestPath == null)
				return false;

			driver.destination = bestCell;
			driver.door = null;
			driver.doorExitCell = IntVec3.Invalid;
			driver.queuedScreamCell = IntVec3.Invalid;
			driver.queuedMoveCell = IntVec3.Invalid;
			driver.MarkStrategicDestination(true, false, true);
			driver.lastFallbackStartCell = zombie.Position;
			driver.lastFallbackDestination = bestCell;
			driver.waitCounter = 0;
			driver.noSafeHackRoute = bestMaxPressure >= albinoNoSafeHackRoutePressure;
			bestPath.ReleaseToPool();
			zombie.pather.StartPath(bestCell, PathEndMode.OnCell);
			return true;
		}

		static bool GotoReachableFallbackCell(this JobDriver_Sabotage driver, IntVec3 cell, AlbinoPressureSources sources = null, TraverseParms? traverseParms = null)
		{
			var zombie = driver.pawn as Zombie;
			if (zombie?.Spawned != true || cell.IsValid == false || cell == zombie.Position)
				return false;

			sources ??= AlbinoPressureSourcesFor(zombie);
			var path = driver.FindFallbackCellPath(cell, sources, traverseParms ?? AlbinoFallbackTraverseParms(zombie));
			if (path == null)
				return false;

			driver.ClearHackTarget();
			driver.destination = cell;
			driver.door = null;
			driver.doorExitCell = IntVec3.Invalid;
			driver.queuedScreamCell = IntVec3.Invalid;
			driver.queuedMoveCell = IntVec3.Invalid;
			driver.MarkStrategicDestination(true, false, true);
			driver.lastFallbackStartCell = zombie.Position;
			driver.lastFallbackDestination = cell;
			driver.waitCounter = 0;
			path.ReleaseToPool();
			zombie.pather.StartPath(cell, PathEndMode.OnCell);
			return true;
		}

		static bool IsZombielandZombie(Pawn pawn)
		{
			return pawn is Zombie || pawn is ZombieSymbiant || pawn is ZombieSpitter;
		}

		static bool CanAlbinoScreamAffect(Pawn pawn, Zombie zombie)
		{
			return pawn != null
				&& zombie != null
				&& pawn.Spawned
				&& pawn.Map == zombie.Map
				&& pawn.Dead == false
				&& IsZombielandZombie(pawn) == false
				&& Customization.DoesAttractsZombies(pawn)
				&& IsHumanlikeFleshPawn(pawn)
				&& pawn.health?.Downed == false
				&& pawn.jobs != null
				&& pawn.stances != null
				&& pawn.InMentalState == false
				&& IsVomiting(pawn) == false;
		}

		static bool HasAlbinoScreamTargetInRange(this Zombie zombie, float radius)
		{
			if (zombie?.Spawned != true)
				return false;

			var dist = radius * radius;
			foreach (var pawn in zombie.Map.mapPawns.AllPawnsSpawned)
				if (CanAlbinoScreamAffect(pawn, zombie) && pawn.Position.DistanceToSquared(zombie.Position) <= dist)
					return true;
			return false;
		}

		static void SetAlbinoScreamCooldown(Zombie zombie, bool successful)
		{
			var affectedCount = Math.Max(0, zombie.albinoScreamAffectedCount);
			var cooldown = successful
				? Rand.Range(albinoScreamSuccessMinCooldown, albinoScreamSuccessMaxCooldown) + Math.Min(albinoScreamSuccessAffectedCooldownMax, affectedCount * albinoScreamSuccessAffectedCooldownTicks)
				: Rand.Range(albinoScreamWastedMinCooldown, albinoScreamWastedMaxCooldown);
			zombie.albinoNextScreamTick = GenTicks.TicksGame + Math.Min(cooldown, albinoScreamMaxCooldown);
		}

		static bool AlbinoScreamReady(Zombie zombie)
		{
			return AlbinoScreamTicksUntilReady(zombie) <= 0;
		}

		static int AlbinoScreamTicksUntilReady(Zombie zombie)
		{
			if (zombie.albinoNextScreamTick < 0)
				zombie.albinoNextScreamTick = GenTicks.TicksGame + Rand.Range(albinoScreamInitialMinCooldown, albinoScreamInitialMaxCooldown);
			return Math.Max(0, zombie.albinoNextScreamTick - GenTicks.TicksGame);
		}

		static bool IsDrafted(Pawn pawn)
		{
			return pawn?.drafter?.Drafted == true;
		}

		static bool IsBusy(Pawn pawn)
		{
			var defName = pawn?.CurJobDef?.defName;
			return defName != null
				&& defName != "Wait"
				&& defName != "Wait_Combat"
				&& defName != "Wait_MaintainPosture"
				&& defName != "Goto"
				&& defName != "LayDown";
		}

		static bool IsNearDraftedColonist(Pawn pawn, List<Pawn> draftedColonists)
		{
			return draftedColonists.Any(drafted => drafted != pawn && drafted.Position.DistanceToSquared(pawn.Position) <= 100);
		}

		static int NearbyPawnScore(Pawn pawn, List<Pawn> candidates, int radiusSquared)
		{
			var count = 0;
			foreach (var candidate in candidates)
				if (candidate.Position.DistanceToSquared(pawn.Position) <= radiusSquared)
					count++;
			return count;
		}

		static bool TryFindStandableScreamCell(Map map, IntVec3 root, out IntVec3 cell)
		{
			cell = IntVec3.Invalid;
			foreach (var candidate in GenRadial.RadialCellsAround(root, 5f, true))
			{
				if (candidate.InBounds(map) == false)
					continue;
				if (candidate.Standable(map) == false)
					continue;
				if (candidate.Fogged(map))
					continue;
				if (candidate.GetEdifice(map) != null)
					continue;
				if (candidate.GetFirstThing<Mineable>(map) != null)
					continue;
				if (candidate.GetThingList(map).Any(thing => thing is Pawn))
					continue;

				cell = candidate;
				return true;
			}
			return false;
		}

		static bool TryFindScreamCellForBestPawn(Zombie zombie, List<Pawn> candidates, Func<Pawn, int> scorePawn, out IntVec3 cell)
		{
			cell = IntVec3.Invalid;
			var bestScore = int.MinValue;
			foreach (var pawn in candidates)
			{
				if (TryFindStandableScreamCell(zombie.Map, pawn.Position, out var candidateCell) == false)
					continue;

				var score = scorePawn(pawn) - zombie.Position.DistanceToSquared(candidateCell) / 16;
				if (score <= bestScore)
					continue;

				bestScore = score;
				cell = candidateCell;
			}
			return cell.IsValid;
		}

		static int ColonistScreamScore(Zombie zombie, Pawn pawn, List<Pawn> colonists)
		{
			var score = 1000;
			score += NearbyPawnScore(pawn, colonists, 100) * 45;
			if (zombie.Map.areaManager.Home[pawn.Position])
				score += 35;
			if (IsBusy(pawn))
				score += 30;
			if (IsDrafted(pawn) == false)
				score += 20;
			return score;
		}

		static bool LocalTargetPointsAtZombie(LocalTargetInfo target, Zombie zombie)
		{
			if (target.HasThing)
				return target.Thing == zombie;

			return target.Cell.IsValid && target.Cell.DistanceToSquared(zombie.Position) <= 2;
		}

		static bool IsAttackingOrApproaching(Pawn pawn, Zombie zombie)
		{
			if (pawn.TargetCurrentlyAimingAt.Thing == zombie)
				return true;

			var job = pawn.CurJob;
			if (job != null && (LocalTargetPointsAtZombie(job.targetA, zombie) || LocalTargetPointsAtZombie(job.targetB, zombie) || LocalTargetPointsAtZombie(job.targetC, zombie)))
				return true;

			if (pawn.pather?.Moving == true)
			{
				var currentDistance = pawn.Position.DistanceToSquared(zombie.Position);
				var destinationDistance = pawn.pather.Destination.Cell.DistanceToSquared(zombie.Position);
				if (destinationDistance < currentDistance && currentDistance <= 324)
					return true;
			}

			return false;
		}

		static bool TryFindOpportunisticEnemyScreamCell(Zombie zombie, out IntVec3 cell)
		{
			var enemies = zombie.Map.attackTargetsCache.TargetsHostileToColony
				.OfType<Pawn>()
				.Where(pawn => CanAlbinoScreamAffect(pawn, zombie))
				.Where(pawn => CanAlbinoPressurePawn(pawn, zombie))
				.Where(pawn => IsAttackingOrApproaching(pawn, zombie) || pawn.Position.DistanceToSquared(zombie.Position) <= 100)
				.ToList();

			return TryFindScreamCellForBestPawn(
				zombie,
				enemies,
				pawn => 1300 + NearbyPawnScore(pawn, enemies, 100) * 35 + (IsAttackingOrApproaching(pawn, zombie) ? 250 : 0),
				out cell);
		}

		static bool TryFindGroupedColonistScreamCell(Zombie zombie, List<Pawn> colonists, out IntVec3 cell)
		{
			cell = IntVec3.Invalid;
			var grouped = colonists
				.Where(pawn => NearbyPawnScore(pawn, colonists, 100) >= 2)
				.ToList();
			return TryFindScreamCellForBestPawn(zombie, grouped, pawn => ColonistScreamScore(zombie, pawn, colonists), out cell);
		}

		static bool TryFindAlbinoScreamCell(this JobDriver_Sabotage driver, out IntVec3 cell, out string reason)
		{
			cell = IntVec3.Invalid;
			reason = null;
			var zombie = driver.pawn as Zombie;
			if (zombie?.Spawned != true || zombie.scream != -1 || AlbinoScreamReady(zombie) == false)
				return false;

			if (TryFindOpportunisticEnemyScreamCell(zombie, out cell))
			{
				reason = "opportunisticEnemy";
				return true;
			}

			var colonists = zombie.Map.mapPawns.FreeColonistsSpawned
				.Where(pawn => CanAlbinoScreamAffect(pawn, zombie))
				.ToList();
			if (colonists.Count == 0)
				return false;

			var draftedColonists = colonists.Where(IsDrafted).ToList();
			var isolatedColonists = colonists
				.Where(pawn => IsDrafted(pawn) == false)
				.Where(pawn => IsBusy(pawn) || IsNearDraftedColonist(pawn, draftedColonists) == false)
				.ToList();
			if (TryFindScreamCellForBestPawn(
				zombie,
				isolatedColonists,
				pawn => ColonistScreamScore(zombie, pawn, colonists) + (IsBusy(pawn) ? 100 : 0) + (IsNearDraftedColonist(pawn, draftedColonists) ? -80 : 80),
				out cell))
			{
				reason = "isolatedColonist";
				return true;
			}

			if (TryFindGroupedColonistScreamCell(zombie, colonists, out cell))
			{
				reason = "groupedColonists";
				return true;
			}

			var homeColonists = colonists.Where(pawn => zombie.Map.areaManager.Home[pawn.Position]).ToList();
			if (TryFindScreamCellForBestPawn(zombie, homeColonists, pawn => ColonistScreamScore(zombie, pawn, colonists), out cell))
			{
				reason = "homeColonist";
				return true;
			}

			if (TryFindScreamCellForBestPawn(zombie, colonists, pawn => -pawn.Position.DistanceToSquared(zombie.Position), out cell))
			{
				reason = "nearestColonist";
				return true;
			}

			return false;
		}

		static bool TryFindDesperationScreamCell(this JobDriver_Sabotage driver, out IntVec3 cell)
		{
			cell = IntVec3.Invalid;
			var zombie = driver.pawn as Zombie;
			if (zombie?.Spawned != true || zombie.scream != -1)
				return false;
			if (AlbinoScreamTicksUntilReady(zombie) > albinoDesperationScreamMaxWaitTicks)
				return false;

			var sources = AlbinoPressureSourcesFor(zombie);
			var rangedPawns = sources.pawns
				.Where(pawn => CanAlbinoScreamAffect(pawn, zombie))
				.Where(pawn => HasUsableRangedVerb(pawn, out _))
				.Where(pawn => IsUrgentRangedAlbinoThreat(pawn, zombie) || IsAttackingOrApproaching(pawn, zombie) || CanShootCell(pawn, zombie.Position) || pawn.Position.DistanceToSquared(zombie.Position) <= 400)
				.ToList();

			return TryFindScreamCellForBestPawn(
				zombie,
				rangedPawns,
				pawn => 1600
					+ (IsUrgentRangedAlbinoThreat(pawn, zombie) ? 700 : 0)
					+ (IsAttackingOrApproaching(pawn, zombie) ? 300 : 0)
					+ (CanShootCell(pawn, zombie.Position) ? 250 : 0)
					- pawn.Position.DistanceToSquared(zombie.Position) / 4,
			out cell);
		}

		static bool CanDesperationRushTurret(Zombie zombie, Building_TurretGun turret)
		{
			if (zombie?.Spawned != true || IsActiveTurret(turret) == false)
				return false;

			var sources = AlbinoPressureSourcesFor(zombie);
			foreach (var pawn in AlbinoScreamAffectablePressurePawns(zombie, sources.pawns))
				if (HasUsableRangedVerb(pawn, out _) && (IsAttackingOrApproaching(pawn, zombie) || CanShootCell(pawn, zombie.Position)))
					return false;

			foreach (var otherTurret in sources.turrets)
				if (otherTurret != turret && CanShootCell(otherTurret, zombie.Position))
					return false;

			return true;
		}

		static bool TryChooseDesperationSabotageTarget(this JobDriver_Sabotage driver)
		{
			var zombie = driver.pawn as Zombie;
			if (zombie?.Spawned != true)
				return false;

			if (driver.TryFindDesperationScreamCell(out var screamCell))
				if (driver.Goto(screamCell, () => zombie.scream = -2))
					return true;

			driver.CleanupDeferredHackTarget();
			if (driver.deferredHackTarget is Building_TurretGun turret && CanDesperationRushTurret(zombie, turret))
			{
				driver.StartHackTargetRush(turret);
				if (driver.Goto(turret))
					return true;
			}

			return false;
		}

		internal static bool CanHackBuilding(Building building)
		{
			if (building?.Spawned != true)
				return false;

			var compFlickable = building.TryGetComp<CompFlickable>();
			if (compFlickable != null && compFlickable.SwitchIsOn)
				return true;

			var compBreakdownable = building.TryGetComp<CompBreakdownable>();
			if (compBreakdownable != null && compBreakdownable.BrokenDown == false)
				return true;

			return false;
		}

		internal static float WeaponSabotageScore(Map map, Thing weapon)
		{
			var score = weapon.MarketValue;
			if (map.areaManager.Home[weapon.Position])
				score += 2000f;
			return score;
		}

		static bool ChooseSabotageTarget(this JobDriver_Sabotage driver)
		{
			var zombie = driver.pawn as Zombie;
			var map = zombie.Map;

			if (driver.TryChoosePrimarySabotageTarget())
				return true;

			var options = new int[] { 0, 1 }.InRandomOrder().ToArray();

			for (var i = 0; i < options.Length; i++)
				switch (options[i])
				{
					// hack door of a room
					case 0:
						if (driver.TryGotoNearestFallbackCell(Tools.ValuableRooms(map).SelectMany(room => room.Cells)))
							return true;
						break;

					// move to home zone
					case 1:
						if (driver.TryGotoNearestFallbackCell(map.areaManager.Home.ActiveCells))
							return true;
						break;
				}

			if (driver.TryChooseDesperationSabotageTarget())
				return true;

			return false;
		}

		static bool TryGotoNearestFallbackCell(this JobDriver_Sabotage driver, IEnumerable<IntVec3> cells)
		{
			var zombie = driver.pawn as Zombie;
			var map = zombie?.Map;
			if (map == null || cells == null)
				return false;
			if (GenTicks.TicksGame < driver.nextFallbackMoveTick)
				return false;

			var sources = AlbinoPressureSourcesFor(zombie);
			var traverseParms = AlbinoFallbackTraverseParms(zombie);
			foreach (var cell in cells
				.Where(cell => cell.InBounds(map) && cell.Standable(map) && cell.Fogged(map) == false)
				.Where(cell => cell != driver.lastFallbackStartCell)
				.Where(cell => cell.DistanceToSquared(zombie.Position) >= 9)
				.Where(cell => cell.GetEdifice(map) == null)
				.Distinct()
				.OrderBy(cell => zombie.Position.DistanceToSquared(cell))
				.Take(albinoFallbackMoveCandidateLimit))
				if (driver.GotoReachableFallbackCell(cell, sources, traverseParms))
				{
					return true;
				}
			return false;
		}

		static bool TryChoosePrimarySabotageTarget(this JobDriver_Sabotage driver)
		{
			var zombie = driver.pawn as Zombie;
			var map = zombie.Map;
			IntVec3 cell;

			var directTarget = driver.BestReachableHackTarget(
				map.listerBuildings.allBuildingsColonist
					.Where(CanHackBuilding)
					.Cast<Thing>()
					.Concat(map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon).Where(CanHackThing)));
			if (directTarget != null && driver.Goto(directTarget))
				return true;

			if (driver.TryFindAlbinoScreamCell(out cell, out _))
				if (driver.Goto(cell, () => zombie.scream = -2))
					return true;

			return false;
		}
	}
}
