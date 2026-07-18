using RimBridgeServer.Sdk;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace ZombieLand
{
	public sealed partial class ZombielandBridgeTools
	{
		sealed class GroupResponseContractCase
		{
			public bool success { get; set; }
			public string name { get; set; }
			public int shooters { get; set; }
			public int pressure { get; set; }
			public string previous { get; set; }
			public string expected { get; set; }
			public string actual { get; set; }
		}

		sealed class GroupResponseMapCase
		{
			public bool success { get; set; }
			public string name { get; set; }
			public string expected { get; set; }
			public string actual { get; set; }
		}

		sealed class GroupResponseCacheSeedCase
		{
			public bool success { get; set; }
			public string name { get; set; }
			public int cacheAge { get; set; }
			public string cached { get; set; }
			public string expected { get; set; }
			public string actual { get; set; }
		}

		[Tool("zombieland/group_response_contract", Description = "Verify the deterministic confidence boundary and asymmetric hysteresis used by adaptive group response.")]
		public static object GroupResponseContract()
		{
			var cases = new[]
			{
				GroupResponseCase("one_shooter_one_zombie_enters_full", 1, 1, GroupResponseMode.Minimal, GroupResponseMode.Full),
				GroupResponseCase("three_shooters_four_zombies_enter_full", 3, 4, GroupResponseMode.Minimal, GroupResponseMode.Full),
				GroupResponseCase("three_shooters_five_zombies_stay_minimal", 3, 5, GroupResponseMode.Minimal, GroupResponseMode.Minimal),
				GroupResponseCase("three_shooters_seven_pressure_stay_full", 3, 7, GroupResponseMode.Full, GroupResponseMode.Full),
				GroupResponseCase("three_shooters_eight_pressure_leave_full", 3, 8, GroupResponseMode.Full, GroupResponseMode.Minimal),
				GroupResponseCase("zero_pressure_does_not_enter_full", 1, 0, GroupResponseMode.Minimal, GroupResponseMode.Minimal),
				GroupResponseCase("zero_pressure_keeps_existing_full", 1, 0, GroupResponseMode.Full, GroupResponseMode.Full),
				GroupResponseCase("zero_shooters_never_full", 0, 0, GroupResponseMode.Full, GroupResponseMode.Minimal)
			};
			var now = GroupZombieResponse.ProvocationTicks * 2;
			var cacheSeedCases = new[]
			{
				CreateGroupResponseCacheSeedCase("recent_full_seeds_stay_threshold", GroupResponseMode.Full, GroupZombieResponse.ProvocationTicks - 1, now, GroupResponseMode.Full),
				CreateGroupResponseCacheSeedCase("expired_full_seeds_enter_threshold", GroupResponseMode.Full, GroupZombieResponse.ProvocationTicks, now, GroupResponseMode.Minimal)
			};

			return new
			{
				success = cases.All(testCase => testCase.success) && cacheSeedCases.All(testCase => testCase.success),
				constants = new
				{
					cacheTicks = GroupZombieResponse.CacheTicks,
					provocationTicks = GroupZombieResponse.ProvocationTicks,
					zombiesPerShooter = GroupZombieResponse.ZombiesPerShooter,
					enterFullRatio = GroupZombieResponse.EnterFullRatio,
					stayFullRatio = GroupZombieResponse.StayFullRatio
				},
				cases,
				cacheSeedCases
			};
		}

		static GroupResponseCacheSeedCase CreateGroupResponseCacheSeedCase(string name, GroupResponseMode cached, int cacheAge, int now, GroupResponseMode expected)
		{
			var actual = GroupZombieResponse.HysteresisSeedMode(cached, now - cacheAge, now);
			return new GroupResponseCacheSeedCase
			{
				success = actual == expected,
				name = name,
				cacheAge = cacheAge,
				cached = cached.ToString(),
				expected = expected.ToString(),
				actual = actual.ToString()
			};
		}

		static GroupResponseContractCase GroupResponseCase(string name, int shooters, int pressure, GroupResponseMode previous, GroupResponseMode expected)
		{
			var actual = GroupZombieResponse.DecideMode(shooters, pressure, previous);
			return new GroupResponseContractCase
			{
				success = actual == expected,
				name = name,
				shooters = shooters,
				pressure = pressure,
				previous = previous.ToString(),
				expected = expected.ToString(),
				actual = actual.ToString()
			};
		}

		[Tool("zombieland/group_response_performance_contract", Description = "Measure forced adaptive response recomputations against the current map's real cached zombie population.")]
		public static object GroupResponsePerformanceContract(
			[ToolParameter(Description = "Forced cache-miss recomputations to measure, clamped to 10..1000.", Required = false, DefaultValue = 100)] int repetitions = 100,
			[ToolParameter(Description = "Cached ModeFor calls to measure, clamped to 1000..100000.", Required = false, DefaultValue = 10000)] int cachedCalls = 10000)
		{
			var map = CurrentMap;
			var tickManager = map?.GetComponent<TickManager>();
			if (map == null || tickManager?.RuntimeReady != true)
				return new { success = false, error = "A loaded playable map with a runtime-ready Zombieland TickManager is required." };

			var friendlyFaction = Find.FactionManager.AllFactionsVisible.FirstOrDefault(faction => faction != null
				&& faction != Faction.OfPlayer
				&& faction.def?.humanlikeFaction == true
				&& faction.HostileTo(Faction.OfPlayer) == false);
			var provoker = tickManager.allZombiesCached.FirstOrDefault(zombie => zombie?.Spawned == true && zombie.Dead == false);
			if (friendlyFaction == null || provoker == null)
				return new { success = false, error = "A friendly faction and at least one live cached zombie are required." };
			if (TryFindClearSpawnCell(map, map.Center, 60f, out var center, out var cellError) == false)
				return cellError;

			repetitions = Math.Max(10, Math.Min(1000, repetitions));
			cachedCalls = Math.Max(1000, Math.Min(100000, cachedCalls));
			var settings = ZombieSettings.Values;
			var oldAttackMode = settings.attackMode;
			var oldFriendlyResponse = settings.friendlyZombieResponse;
			var spawned = new List<Thing>();
			Lord lord = null;
			try
			{
				settings.attackMode = AttackMode.Everything;
				settings.friendlyZombieResponse = ZombieResponsePolicy.Adaptive;
				var shooter = SpawnArmedAreaWorkflowPawn(map, "ZL_Response_Performance", center, friendlyFaction, spawned);
				lord = LordMaker.MakeNewLord(friendlyFaction, new LordJob_DefendPoint(center, 8f, 12f, false, false), map, new[] { shooter });
				shooter.mindState.meleeThreat = provoker;
				shooter.mindState.lastHarmTick = Find.TickManager.TicksGame;

				var elapsed = new long[repetitions];
				var modes = new GroupResponseMode[repetitions];
				for (var i = 0; i < repetitions; i++)
				{
					GroupZombieResponse.ResetMapOwnedState();
					var watch = System.Diagnostics.Stopwatch.StartNew();
					modes[i] = GroupZombieResponse.ModeFor(shooter);
					watch.Stop();
					elapsed[i] = watch.ElapsedTicks;
				}

				var cachedWatch = System.Diagnostics.Stopwatch.StartNew();
				for (var i = 0; i < cachedCalls; i++)
					_ = GroupZombieResponse.ModeFor(shooter);
				cachedWatch.Stop();

				shooter.mindState.meleeThreat = null;
				shooter.mindState.lastHarmTick = 0;
				GroupZombieResponse.ResetMapOwnedState();
				var fallbackModeCalls = 0;
				GroupZombieResponse.evaluationObserver = _ => fallbackModeCalls++;
				var fallbackMethod = typeof(AttackTargetFinder_BestAttackTarget_Patch).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic);
				var fallbackArgs = new object[]
				{
					null,
					TargetScanFlags.None,
					(Predicate<Thing>)(thing => thing is Zombie),
					shooter,
					0f,
					9999f,
					IntVec3.Invalid,
					9999f,
					false,
					true,
					false,
					false
				};
				var fallbackWatch = System.Diagnostics.Stopwatch.StartNew();
				fallbackMethod?.Invoke(null, fallbackArgs);
				fallbackWatch.Stop();
				GroupZombieResponse.evaluationObserver = null;

				var tickToMicroseconds = 1000000d / System.Diagnostics.Stopwatch.Frequency;
				var meanMicroseconds = elapsed.Average(value => value * tickToMicroseconds);
				var maxMicroseconds = elapsed.Max() * tickToMicroseconds;
				var cachedMeanMicroseconds = cachedWatch.ElapsedTicks * tickToMicroseconds / cachedCalls;
				var cachedZombieCount = tickManager.allZombiesCached.Count;
				return new
				{
					success = cachedZombieCount >= 1000 && meanMicroseconds < 2000d && fallbackMethod != null && fallbackModeCalls <= 1,
					cachedZombieCount,
					repetitions,
					meanRecomputeMicroseconds = meanMicroseconds,
					maxRecomputeMicroseconds = maxMicroseconds,
					amortizedMeanMicrosecondsPerGameTick = meanMicroseconds / GroupZombieResponse.CacheTicks,
					cachedCalls,
					meanCachedCallMicroseconds = cachedMeanMicroseconds,
					minimalFallback = new
					{
						methodFound = fallbackMethod != null,
						modeCalls = fallbackModeCalls,
						elapsedMicroseconds = fallbackWatch.ElapsedTicks * tickToMicroseconds,
						returnedTarget = fallbackArgs[0] != null
					},
					modeCounts = modes.GroupBy(mode => mode.ToString()).ToDictionary(group => group.Key, group => group.Count()),
					acceptance = "at least 1000 real cached zombies, under 2 ms per forced recomputation, and at most one mode check before skipping a Minimal friendly fallback"
				};
			}
			finally
			{
				GroupZombieResponse.evaluationObserver = null;
				if (lord != null && map.lordManager.lords.Contains(lord))
					map.lordManager.RemoveLord(lord);
				for (var i = 0; i < spawned.Count; i++)
					if (spawned[i]?.Destroyed == false)
						spawned[i].Destroy(DestroyMode.Vanish);
				settings.attackMode = oldAttackMode;
				settings.friendlyZombieResponse = oldFriendlyResponse;
				GroupZombieResponse.ResetMapOwnedState();
			}
		}

		[Tool("zombieland/group_response_map_contract", Description = "Verify adaptive provocation, Lord scoping, cache behavior, readiness gates, relationship policies, and faction-level active-threat precedence on the current map.")]
		public static object GroupResponseMapContract()
		{
			var map = CurrentMap;
			var tickManager = map?.GetComponent<TickManager>();
			if (map == null || tickManager?.RuntimeReady != true)
				return new { success = false, error = "A loaded playable map with a runtime-ready Zombieland TickManager is required." };

			var friendlyFaction = Find.FactionManager.AllFactionsVisible
				.FirstOrDefault(faction => faction != null
					&& faction != Faction.OfPlayer
					&& faction.def?.humanlikeFaction == true
					&& faction.HostileTo(Faction.OfPlayer) == false);
			var enemyFaction = Find.FactionManager.AllFactionsVisible
				.FirstOrDefault(faction => faction != null
					&& faction != Faction.OfPlayer
					&& faction.def?.humanlikeFaction == true
					&& faction.HostileTo(Faction.OfPlayer));
			if (friendlyFaction == null || enemyFaction == null)
				return new { success = false, error = "Both a friendly humanlike faction and a hostile humanlike faction are required." };

			if (TryFindClearSpawnCell(map, new IntVec3(map.Size.x / 2, 0, map.Size.z / 2), 40f, out var center, out var cellError) == false)
				return cellError;

			var settings = ZombieSettings.Values;
			var oldAttackMode = settings.attackMode;
			var oldFriendlyResponse = settings.friendlyZombieResponse;
			var oldEnemyResponse = settings.enemyZombieResponse;
			var spawned = new List<Thing>();
			var lords = new List<Lord>();
			var cases = new List<GroupResponseMapCase>();
			var evaluations = new List<object>();
			try
			{
				settings.attackMode = AttackMode.Everything;
				settings.friendlyZombieResponse = ZombieResponsePolicy.Adaptive;
				settings.enemyZombieResponse = ZombieResponsePolicy.Adaptive;

				var shooter = SpawnArmedAreaWorkflowPawn(map, "ZL_Response_Friendly", center, friendlyFaction, spawned);
				var lord = LordMaker.MakeNewLord(friendlyFaction, new LordJob_DefendPoint(center, 8f, 12f, false, false), map, new[] { shooter });
				lords.Add(lord);
				var zombieCell = CellFinder.RandomClosewalkCellNear(center, map, 2);
				var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
				if (zombie == null)
					return new { success = false, error = "Could not spawn the ordinary response-contract zombie." };
				spawned.Add(zombie);
				zombie.state = ZombieState.Tracking;
				_ = tickManager.allZombiesCached.Add(zombie);

				var now = Find.TickManager.TicksGame;
				shooter.mindState.meleeThreat = zombie;
				shooter.mindState.lastHarmTick = now;
				ResetGroupResponseContractCache(evaluations);
				AddGroupResponseMapCase(cases, "recent_zombie_hit_enters_full", GroupResponseMode.Full, GroupZombieResponse.ModeFor(shooter));
				AddGroupResponseMapCase(cases, "full_mode_makes_zombie_hostile", true, shooter.HostileTo(zombie));

				shooter.mindState.lastHarmTick = now - GroupZombieResponse.ProvocationTicks;
				ResetGroupResponseContractCache(evaluations);
				AddGroupResponseMapCase(cases, "expired_provocation_is_minimal", GroupResponseMode.Minimal, GroupZombieResponse.ModeFor(shooter));

				shooter.mindState.lastHarmTick = now;
				shooter.mindState.meleeThreat = null;
				ResetGroupResponseContractCache(evaluations);
				AddGroupResponseMapCase(cases, "recent_harm_survives_cleared_melee_threat", GroupResponseMode.Full, GroupZombieResponse.ModeFor(shooter));

				shooter.mindState.meleeThreat = zombie;
				zombie.hasTankySuit = 1f;
				ResetGroupResponseContractCache(evaluations);
				AddGroupResponseMapCase(cases, "one_tanky_outweighs_one_shooter", GroupResponseMode.Minimal, GroupZombieResponse.ModeFor(shooter));

				zombie.hasTankySuit = -1f;
				ResetGroupResponseContractCache(evaluations);
				var beforeAddedPressure = GroupZombieResponse.ModeFor(shooter);
				for (var i = 0; i < 5; i++)
				{
					var extraCell = CellFinder.RandomClosewalkCellNear(center, map, 5);
					var extra = ZombieRuntimeActions.SpawnZombie(extraCell, map, ZombieType.Normal, true);
					if (extra == null)
						continue;
					extra.state = ZombieState.Tracking;
					spawned.Add(extra);
					_ = tickManager.allZombiesCached.Add(extra);
				}
				var cachedAfterAddedPressure = GroupZombieResponse.ModeFor(shooter);
				AddGroupResponseMapCase(cases, "fresh_lord_cache_holds_mode", beforeAddedPressure, cachedAfterAddedPressure);
				ResetGroupResponseContractCache(evaluations);
				AddGroupResponseMapCase(cases, "recalculation_sees_added_pressure", GroupResponseMode.Minimal, GroupZombieResponse.ModeFor(shooter));

				var lordless = SpawnArmedAreaWorkflowPawn(map, "ZL_Response_Lordless", CellFinder.RandomClosewalkCellNear(center, map, 12), friendlyFaction, spawned);
				lordless.mindState.meleeThreat = zombie;
				lordless.mindState.lastHarmTick = now;
				ResetGroupResponseContractCache(evaluations);
				AddGroupResponseMapCase(cases, "adaptive_lordless_is_minimal", GroupResponseMode.Minimal, GroupZombieResponse.ModeFor(lordless));

				var enemy = SpawnArmedAreaWorkflowPawn(map, "ZL_Response_Enemy", CellFinder.RandomClosewalkCellNear(center, map, 16), enemyFaction, spawned);
				settings.enemyZombieResponse = ZombieResponsePolicy.Minimal;
				AddGroupResponseMapCase(cases, "fixed_enemy_minimal_without_lord", GroupResponseMode.Minimal, GroupZombieResponse.ModeFor(enemy));
				settings.enemyZombieResponse = ZombieResponsePolicy.Full;
				AddGroupResponseMapCase(cases, "fixed_enemy_full_without_lord", GroupResponseMode.Full, GroupZombieResponse.ModeFor(enemy));
				settings.enemyZombieResponse = ZombieResponsePolicy.Adaptive;
				AddGroupResponseMapCase(cases, "adaptive_enemy_without_lord_is_minimal", GroupResponseMode.Minimal, GroupZombieResponse.ModeFor(enemy));

				settings.friendlyZombieResponse = ZombieResponsePolicy.Minimal;
				AddGroupResponseMapCase(cases, "friendly_active_threat_preserves_attack_mode", true, GenHostility.IsActiveThreatTo(zombie, friendlyFaction, false, false));
				settings.enemyZombieResponse = ZombieResponsePolicy.Minimal;
				AddGroupResponseMapCase(cases, "enemy_minimal_is_not_active_threat", false, GenHostility.IsActiveThreatTo(zombie, enemyFaction, false, false));
				settings.enemyZombieResponse = ZombieResponsePolicy.Adaptive;
				AddGroupResponseMapCase(cases, "enemy_adaptive_preserves_attack_mode_threat", true, GenHostility.IsActiveThreatTo(zombie, enemyFaction, false, false));
				settings.enemyZombieResponse = ZombieResponsePolicy.Full;
				AddGroupResponseMapCase(cases, "enemy_full_preserves_attack_mode_threat", true, GenHostility.IsActiveThreatTo(zombie, enemyFaction, false, false));

				settings.attackMode = AttackMode.OnlyColonists;
				settings.enemyZombieResponse = ZombieResponsePolicy.Full;
				if (RepositionGroupResponseZombieAdjacent(zombie, enemy) == false)
					return new { success = false, error = "Could not position the response-contract zombie beside the enemy human." };
				AddGroupResponseMapCase(cases, "enemy_human_ranged_respects_only_colonists", false, ReferenceEquals(BestSpecificTarget(enemy, zombie, 5f), zombie));
				enemy.equipment?.DestroyAllEquipment(DestroyMode.Vanish);
				AddGroupResponseMapCase(cases, "enemy_human_melee_preserves_legacy_only_colonists", true, ReferenceEquals(BestSpecificTarget(enemy, zombie, 5f), zombie));

				if (TryFindClearSpawnCell(map, center + new IntVec3(14, 0, 10), 12f, out var mechCell, out var mechCellError) == false)
					return mechCellError;
				var enemyMech = SpawnAreaWorkflowMech(map, "ZL_Response_EnemyScyther", mechCell, Faction.OfMechanoids ?? enemyFaction, spawned);
				if (enemyMech == null || enemyMech.CurrentEffectiveVerb?.IsMeleeAttack != true)
					return new { success = false, error = "Could not spawn a melee-only hostile mechanoid for the response contract." };
				settings.attackMode = AttackMode.OnlyHumans;
				if (RepositionGroupResponseZombieAdjacent(zombie, enemyMech) == false)
					return new { success = false, error = "Could not position the response-contract zombie beside the hostile mechanoid." };
				AddGroupResponseMapCase(cases, "enemy_mech_melee_preserves_legacy_only_humans", true, ReferenceEquals(BestSpecificTarget(enemyMech, zombie, 5f), zombie));

				if (SpawnAreaWorkflowTurretGun(map, center + new IntVec3(-12, 0, 10), friendlyFaction, spawned, out var friendlyTurret, out var turretError) == false)
					return turretError;
				var friendlyTurretVerb = friendlyTurret.CurrentEffectiveVerb;
				if (friendlyTurretVerb == null)
					return new { success = false, error = "The response-contract friendly turret had no effective verb." };
				if (RepositionGroupResponseZombieNearSearcher(zombie, friendlyTurret) == false)
					return new { success = false, error = "Could not position the response-contract zombie within the friendly turret's targeting range." };
				RefreshZombieTargetCache(map);
				settings.attackMode = AttackMode.Everything;
				settings.friendlyZombieResponse = ZombieResponsePolicy.Adaptive;
				var adaptiveTurretTargets = TargetIds(InvokeAvailableTargetsPatch(new List<IAttackTarget> { zombie }, friendlyTurret, friendlyTurretVerb));
				AddGroupResponseMapCase(cases, "friendly_nonpawn_adaptive_preserves_everything_target", true, ContainsTarget(adaptiveTurretTargets, zombie));
				AddGroupResponseMapCase(cases, "friendly_nonpawn_adaptive_best_target", zombie, BestSpecificTarget(friendlyTurret, zombie, 40f));
				settings.friendlyZombieResponse = ZombieResponsePolicy.Minimal;
				var minimalTurretTargets = TargetIds(InvokeAvailableTargetsPatch(new List<IAttackTarget> { zombie }, friendlyTurret, friendlyTurretVerb));
				AddGroupResponseMapCase(cases, "friendly_nonpawn_minimal_excludes_zombie", false, ContainsTarget(minimalTurretTargets, zombie));
				AddGroupResponseMapCase(cases, "friendly_nonpawn_minimal_best_target_is_null", null, BestSpecificTarget(friendlyTurret, zombie, 40f));

				var distantCell = GenRadial.RadialCellsAround(center, 15f, true)
					.Where(cell => cell.InBounds(map)
						&& cell.Standable(map)
						&& cell.GetFirstPawn(map) == null
						&& cell.DistanceToSquared(shooter.Position) > 81
						&& cell.DistanceToSquared(shooter.Position) <= 225)
					.OrderBy(cell => cell.DistanceToSquared(shooter.Position))
					.FirstOrDefault();
				if (distantCell.IsValid)
				{
					zombie.DeSpawn(DestroyMode.Vanish);
					GenSpawn.Spawn(zombie, distantCell, map, Rot4.South);
					zombie.state = ZombieState.Tracking;
				}
				settings.friendlyZombieResponse = ZombieResponsePolicy.Full;
				settings.enemyZombieResponse = ZombieResponsePolicy.Minimal;
				ResetGroupResponseContractCache(evaluations);
				AddGroupResponseMapCase(cases, "friendly_full_targets_beyond_nine_with_enemy_minimal", zombie, BestSpecificTarget(shooter, zombie, 40f));

				shooter.equipment.DestroyAllEquipment(DestroyMode.Vanish);
				settings.friendlyZombieResponse = ZombieResponsePolicy.Adaptive;
				ResetGroupResponseContractCache(evaluations);
				AddGroupResponseMapCase(cases, "no_usable_ranged_weapon_is_minimal", GroupResponseMode.Minimal, GroupZombieResponse.ModeFor(shooter));

				return new
				{
					success = cases.All(testCase => testCase.success),
					mapId = map.uniqueID,
					friendlyFaction = friendlyFaction.def.defName,
					enemyFaction = enemyFaction.def.defName,
					cases,
					evaluations
				};
			}
			finally
			{
				GroupZombieResponse.evaluationObserver = null;
				for (var i = 0; i < lords.Count; i++)
					if (lords[i] != null && map.lordManager.lords.Contains(lords[i]))
						map.lordManager.RemoveLord(lords[i]);
				for (var i = 0; i < spawned.Count; i++)
					if (spawned[i]?.Destroyed == false)
						spawned[i].Destroy(DestroyMode.Vanish);
				settings.attackMode = oldAttackMode;
				settings.friendlyZombieResponse = oldFriendlyResponse;
				settings.enemyZombieResponse = oldEnemyResponse;
				GroupZombieResponse.ResetMapOwnedState();
			}
		}

		static bool RepositionGroupResponseZombieAdjacent(Zombie zombie, Pawn pawn)
		{
			if (zombie == null || pawn?.Spawned != true)
				return false;
			var map = pawn.Map;
			var cell = GenAdj.CardinalDirections
				.Select(offset => pawn.Position + offset)
				.FirstOrDefault(candidate => candidate.InBounds(map) && candidate.Standable(map) && candidate.GetFirstPawn(map) == null);
			if (cell.IsValid == false)
				return false;
			if (zombie.Spawned)
				zombie.DeSpawn(DestroyMode.Vanish);
			GenSpawn.Spawn(zombie, cell, map, Rot4.South);
			zombie.state = ZombieState.Tracking;
			return true;
		}

		static bool RepositionGroupResponseZombieNearSearcher(Zombie zombie, Thing searcher)
		{
			if (zombie == null || searcher?.Spawned != true)
				return false;
			var map = searcher.Map;
			var cell = GenRadial.RadialCellsAround(searcher.Position, 8f, false)
				.Where(candidate => candidate.InBounds(map)
					&& candidate.Standable(map)
					&& candidate.GetFirstPawn(map) == null
					&& candidate.DistanceToSquared(searcher.Position) >= 16
					&& GenSight.LineOfSight(searcher.Position, candidate, map, true))
				.OrderBy(candidate => candidate.DistanceToSquared(searcher.Position))
				.FirstOrDefault();
			if (cell.IsValid == false)
				return false;
			if (zombie.Spawned)
				zombie.DeSpawn(DestroyMode.Vanish);
			GenSpawn.Spawn(zombie, cell, map, Rot4.South);
			zombie.state = ZombieState.Tracking;
			return true;
		}

		static void ResetGroupResponseContractCache(List<object> evaluations)
		{
			GroupZombieResponse.ResetMapOwnedState();
			GroupZombieResponse.evaluationObserver = evaluation => evaluations.Add(new
			{
				evaluation.tick,
				evaluation.harmAge,
				evaluation.capableShooters,
				evaluation.zombiePressure,
				evaluation.confidence,
				evaluation.threshold,
				evaluation.cacheHit,
				previousMode = evaluation.previousMode.ToString(),
				mode = evaluation.mode.ToString(),
				lordId = evaluation.lord?.GetHashCode(),
				anchor = evaluation.anchor?.Name?.ToStringShort
			});
		}

		static void AddGroupResponseMapCase<T>(List<GroupResponseMapCase> cases, string name, T expected, T actual)
		{
			cases.Add(new GroupResponseMapCase
			{
				success = EqualityComparer<T>.Default.Equals(expected, actual),
				name = name,
				expected = Convert.ToString(expected),
				actual = Convert.ToString(actual)
			});
		}
	}
}
