using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI.Group;

namespace ZombieLand
{
	internal enum GroupResponseMode
	{
		Minimal,
		Full
	}

	internal readonly struct GroupResponseEvaluation
	{
		internal readonly int tick;
		internal readonly int harmAge;
		internal readonly int capableShooters;
		internal readonly int zombiePressure;
		internal readonly float confidence;
		internal readonly float threshold;
		internal readonly GroupResponseMode previousMode;
		internal readonly GroupResponseMode mode;
		internal readonly bool cacheHit;
		internal readonly Lord lord;
		internal readonly Pawn anchor;

		internal GroupResponseEvaluation(int tick, int harmAge, int capableShooters, int zombiePressure, float confidence, float threshold, GroupResponseMode previousMode, GroupResponseMode mode, Lord lord, Pawn anchor, bool cacheHit)
		{
			this.tick = tick;
			this.harmAge = harmAge;
			this.capableShooters = capableShooters;
			this.zombiePressure = zombiePressure;
			this.confidence = confidence;
			this.threshold = threshold;
			this.previousMode = previousMode;
			this.mode = mode;
			this.cacheHit = cacheHit;
			this.lord = lord;
			this.anchor = anchor;
		}
	}

	internal enum GroupCombatObservationKind
	{
		ZombieMeleeAttempt,
		ZombieLandedHarm,
		GroupMeleeAttack,
		GroupRangedShot,
		ZombieDeath
	}

	internal readonly struct GroupCombatObservation
	{
		internal readonly int tick;
		internal readonly GroupCombatObservationKind kind;
		internal readonly Pawn actor;
		internal readonly Pawn target;

		internal GroupCombatObservation(int tick, GroupCombatObservationKind kind, Pawn actor, Pawn target)
		{
			this.tick = tick;
			this.kind = kind;
			this.actor = actor;
			this.target = target;
		}
	}

	internal static class GroupZombieResponse
	{
		internal const int CacheTicks = 120;
		internal const int ProvocationTicks = 600;
		internal const int SupportRadiusSquared = 24 * 24;
		internal const int ZombieRadiusSquared = 16 * 16;
		internal const int ZombiesPerShooter = 2;
		internal const int TankyPressure = 3;
		internal const float EnterFullRatio = 1.25f;
		internal const float StayFullRatio = 0.85f;

		readonly struct CacheEntry
		{
			internal readonly GroupResponseMode mode;
			internal readonly int tick;

			internal CacheEntry(GroupResponseMode mode, int tick)
			{
				this.mode = mode;
				this.tick = tick;
			}
		}

		static Dictionary<Lord, CacheEntry> cache = new();
		internal static Action<GroupResponseEvaluation> evaluationObserver;
		internal static Action<GroupCombatObservation> combatObserver;
		internal static bool disablePawnRelationsForEvidence;

		internal static ZombieResponsePolicy PolicyFor(Pawn pawn)
		{
			return PolicyFor(pawn?.Faction);
		}

		internal static ZombieResponsePolicy PolicyFor(Faction faction)
		{
			if (faction == null || faction == Faction.OfPlayer)
				return ZombieResponsePolicy.Minimal;
			return faction.HostileTo(Faction.OfPlayer)
				? ZombieSettings.Values.enemyZombieResponse
				: ZombieSettings.Values.friendlyZombieResponse;
		}

		internal static bool AllowsFullResponse(Thing thing)
		{
			if (thing is Pawn pawn)
				return ModeFor(pawn) == GroupResponseMode.Full;
			return PolicyFor(thing?.Faction) != ZombieResponsePolicy.Minimal;
		}

		internal static GroupResponseMode ModeFor(Pawn pawn)
		{
			var policy = PolicyFor(pawn);
			if (policy == ZombieResponsePolicy.Minimal)
				return GroupResponseMode.Minimal;
			if (policy == ZombieResponsePolicy.Full)
				return GroupResponseMode.Full;
			if (pawn?.Spawned != true)
				return GroupResponseMode.Minimal;

			var lord = pawn.GetLord();
			if (lord == null)
				return GroupResponseMode.Minimal;

			var now = Find.TickManager?.TicksGame ?? 0;
			var hasPrevious = cache.TryGetValue(lord, out var previous);
			if (hasPrevious && now >= previous.tick && now - previous.tick < CacheTicks)
			{
				var observer = evaluationObserver;
				if (observer != null)
					observer(new GroupResponseEvaluation(now, -1, -1, -1, float.NaN, previous.mode == GroupResponseMode.Full ? StayFullRatio : EnterFullRatio, previous.mode, previous.mode, lord, null, true));
				return previous.mode;
			}

			var previousMode = hasPrevious ? HysteresisSeedMode(previous.mode, previous.tick, now) : GroupResponseMode.Minimal;
			var mode = Evaluate(lord, now, previousMode);
			cache[lord] = new CacheEntry(mode, now);
			return mode;
		}

		internal static GroupResponseMode HysteresisSeedMode(GroupResponseMode cachedMode, int evaluatedAtTick, int now)
		{
			var age = now - evaluatedAtTick;
			return age >= 0 && age < ProvocationTicks ? cachedMode : GroupResponseMode.Minimal;
		}

		static GroupResponseMode Evaluate(Lord lord, int now, GroupResponseMode previousMode)
		{
			var members = lord.ownedPawns;
			Pawn anchor = null;
			var anchorHarmTick = int.MinValue;
			for (var i = 0; i < members.Count; i++)
			{
				var member = members[i];
				if (member?.Spawned != true || member.Dead || member.mindState == null)
					continue;
				var harmTick = member.mindState.lastHarmTick;
				var age = now - harmTick;
				if (harmTick <= 0 || age < 0 || age >= ProvocationTicks || harmTick <= anchorHarmTick)
					continue;
				anchor = member;
				anchorHarmTick = harmTick;
			}

			if (anchor == null)
				return Observe(now, -1, 0, 0, 0f, previousMode, GroupResponseMode.Minimal, lord, null);

			var capableShooters = 0;
			for (var i = 0; i < members.Count; i++)
				if (IsCapableShooter(members[i], anchor))
					capableShooters++;

			if (capableShooters == 0)
				return Observe(now, now - anchorHarmTick, 0, 0, 0f, previousMode, GroupResponseMode.Minimal, lord, anchor);

			var tickManager = anchor.Map?.GetComponent<TickManager>();
			var zombies = tickManager?.RuntimeReady == true ? tickManager.allZombiesCached : null;
			if (zombies == null)
				return Observe(now, now - anchorHarmTick, capableShooters, 0, 0f, previousMode, GroupResponseMode.Minimal, lord, anchor);

			var pressure = 0;
			foreach (var zombie in zombies)
				if (IsRelevantZombie(zombie, anchor))
					pressure += zombie.IsTanky ? TankyPressure : 1;

			var mode = DecideMode(capableShooters, pressure, previousMode);
			var confidence = capableShooters * ZombiesPerShooter / (float)Math.Max(1, pressure);
			return Observe(now, now - anchorHarmTick, capableShooters, pressure, confidence, previousMode, mode, lord, anchor);
		}

		static GroupResponseMode Observe(int tick, int harmAge, int capableShooters, int pressure, float confidence, GroupResponseMode previousMode, GroupResponseMode mode, Lord lord, Pawn anchor)
		{
			var threshold = previousMode == GroupResponseMode.Full ? StayFullRatio : EnterFullRatio;
			evaluationObserver?.Invoke(new GroupResponseEvaluation(tick, harmAge, capableShooters, pressure, confidence, threshold, previousMode, mode, lord, anchor, false));
			return mode;
		}

		internal static GroupResponseMode DecideMode(int capableShooters, int zombiePressure, GroupResponseMode previousMode)
		{
			if (capableShooters <= 0)
				return GroupResponseMode.Minimal;
			if (zombiePressure <= 0)
				return previousMode == GroupResponseMode.Full ? GroupResponseMode.Full : GroupResponseMode.Minimal;
			var confidence = capableShooters * ZombiesPerShooter / (float)Math.Max(1, zombiePressure);
			var threshold = previousMode == GroupResponseMode.Full ? StayFullRatio : EnterFullRatio;
			return confidence >= threshold ? GroupResponseMode.Full : GroupResponseMode.Minimal;
		}

		internal static bool IsCapableShooter(Pawn pawn, Pawn anchor)
		{
			if (pawn?.Spawned != true || pawn.Map != anchor.Map || pawn.Dead || pawn.Downed || pawn.InMentalState || pawn.InContainerEnclosed)
				return false;
			if (pawn.Position.DistanceToSquared(anchor.Position) > SupportRadiusSquared || pawn.WorkTagIsDisabled(WorkTags.Violent))
				return false;
			if ((pawn.health?.summaryHealth?.SummaryHealthPercent ?? 0f) <= 0.25f)
				return false;
			if (pawn.health.capacities.GetLevel(PawnCapacityDefOf.Moving) < 0.25f)
				return false;
			var verb = pawn.equipment?.PrimaryEq?.PrimaryVerb;
			return verb != null && verb.IsMeleeAttack == false && verb.Available();
		}

		static bool IsRelevantZombie(Zombie zombie, Pawn anchor)
		{
			return zombie?.Spawned == true
				&& zombie.Map == anchor.Map
				&& zombie.Destroyed == false
				&& zombie.Dead == false
				&& zombie.Downed == false
				&& zombie.state != ZombieState.Emerging
				&& zombie.IsRopedOrConfused == false
				&& zombie.isAlbino == false
				&& zombie.Position.DistanceToSquared(anchor.Position) <= ZombieRadiusSquared;
		}

		internal static void ObserveMeleeAttack(Verb_MeleeAttack verb)
		{
			var observer = combatObserver;
			if (observer == null || verb?.CasterPawn == null || verb.currentTarget.Thing is not Pawn target)
				return;
			var actor = verb.CasterPawn;
			if (actor is Zombie)
			{
				observer(new GroupCombatObservation(Find.TickManager.TicksGame, GroupCombatObservationKind.ZombieMeleeAttempt, actor, target));
				return;
			}
			if (target is Zombie)
				observer(new GroupCombatObservation(Find.TickManager.TicksGame, GroupCombatObservationKind.GroupMeleeAttack, actor, target));
		}

		internal static void ObserveMeleeResult(Verb_MeleeAttack verb)
		{
			var observer = combatObserver;
			if (observer == null || verb?.CasterPawn is not Zombie actor || verb.currentTarget.Thing is not Pawn target)
				return;
			if (target.mindState?.lastHarmTick == Find.TickManager.TicksGame)
				observer(new GroupCombatObservation(Find.TickManager.TicksGame, GroupCombatObservationKind.ZombieLandedHarm, actor, target));
		}

		internal static void ObserveRangedShot(Pawn actor, Pawn target)
		{
			var observer = combatObserver;
			if (observer == null || actor == null || target is not Zombie)
				return;
			observer(new GroupCombatObservation(Find.TickManager.TicksGame, GroupCombatObservationKind.GroupRangedShot, actor, target));
		}

		internal static void ObserveZombieDeath(Zombie zombie, DamageInfo? damageInfo)
		{
			var observer = combatObserver;
			if (observer == null || zombie == null)
				return;
			observer(new GroupCombatObservation(Find.TickManager.TicksGame, GroupCombatObservationKind.ZombieDeath, damageInfo?.Instigator as Pawn, zombie));
		}

		internal static void ResetMapOwnedState()
		{
			cache = new Dictionary<Lord, CacheEntry>();
			evaluationObserver = null;
			combatObserver = null;
			disablePawnRelationsForEvidence = false;
		}
	}
}
