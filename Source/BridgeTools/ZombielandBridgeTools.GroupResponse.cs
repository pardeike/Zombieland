using RimBridgeServer.Sdk;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
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

			return new
			{
				success = cases.All(testCase => testCase.success),
				constants = new
				{
					cacheTicks = GroupZombieResponse.CacheTicks,
					provocationTicks = GroupZombieResponse.ProvocationTicks,
					zombiesPerShooter = GroupZombieResponse.ZombiesPerShooter,
					enterFullRatio = GroupZombieResponse.EnterFullRatio,
					stayFullRatio = GroupZombieResponse.StayFullRatio
				},
				cases
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
			[ToolParameter(Description = "Forced cache-miss recomputations to measure, clamped to 10..1000.", Required = false, DefaultValue = 100)] int repetitions = 100)
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

				const int cachedCalls = 10000;
				var cachedWatch = System.Diagnostics.Stopwatch.StartNew();
				for (var i = 0; i < cachedCalls; i++)
					_ = GroupZombieResponse.ModeFor(shooter);
				cachedWatch.Stop();

				var tickToMicroseconds = 1000000d / System.Diagnostics.Stopwatch.Frequency;
				var meanMicroseconds = elapsed.Average(value => value * tickToMicroseconds);
				var maxMicroseconds = elapsed.Max() * tickToMicroseconds;
				var cachedMeanMicroseconds = cachedWatch.ElapsedTicks * tickToMicroseconds / cachedCalls;
				var cachedZombieCount = tickManager.allZombiesCached.Count;
				return new
				{
					success = cachedZombieCount >= 1000 && meanMicroseconds < 2000d,
					cachedZombieCount,
					repetitions,
					meanRecomputeMicroseconds = meanMicroseconds,
					maxRecomputeMicroseconds = maxMicroseconds,
					amortizedMeanMicrosecondsPerGameTick = meanMicroseconds / GroupZombieResponse.CacheTicks,
					cachedCalls,
					meanCachedCallMicroseconds = cachedMeanMicroseconds,
					modeCounts = modes.GroupBy(mode => mode.ToString()).ToDictionary(group => group.Key, group => group.Count()),
					acceptance = "at least 1000 real cached zombies and under 2 ms per forced recomputation"
				};
			}
			finally
			{
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
