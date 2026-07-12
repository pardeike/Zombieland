using HarmonyLib;
using RimBridgeServer.Sdk;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	public sealed partial class ZombielandBridgeTools
	{
		static bool zombieTickingTestModeEnabled;
		static SettingsGroup zombieTickingTestModeOriginalValues;
		static List<SettingsKeyFrame> zombieTickingTestModeOriginalValuesOverTime;

		[Tool("zombieland/zombie_ticking_test_mode", Description = "Temporarily suppress ordinary and zombie-free-event zero-threat cleanup for a controlled scheduler benchmark, then restore the exact prior settings timeline.")]
		public static object ZombieTickingTestMode(
			[ToolParameter(Description = "True to begin/idempotently maintain the controlled benchmark window; false to restore the setting captured by the first begin call.")] bool enabled)
		{
			var settings = ZombieSettings.Values;
			if (enabled && settings == null)
				return new { success = false, enabled, error = "Zombieland settings are not initialized." };

			if (enabled)
			{
				if (zombieTickingTestModeEnabled == false)
				{
					var snapshot = SnapshotZombieSettings();
					zombieTickingTestModeOriginalValues = snapshot.values;
					zombieTickingTestModeOriginalValuesOverTime = snapshot.valuesOverTime;
				}
				zombieTickingTestModeEnabled = true;
				ApplyZombieSettingsOverride(values =>
				{
					values.zombiesDieOnZeroThreat = false;
					values.zombieFreeEvents = false;
				});
			}
			else if (zombieTickingTestModeEnabled)
			{
				RestoreZombieSettings((zombieTickingTestModeOriginalValues, zombieTickingTestModeOriginalValuesOverTime));
				zombieTickingTestModeEnabled = false;
				zombieTickingTestModeOriginalValues = null;
				zombieTickingTestModeOriginalValuesOverTime = null;
			}
			settings = ZombieSettings.Values;

			return new
			{
				success = true,
				requestedEnabled = enabled,
				testModeEnabled = zombieTickingTestModeEnabled,
				zombiesDieOnZeroThreat = settings?.zombiesDieOnZeroThreat,
				zombieFreeEvents = settings?.zombieFreeEvents
			};
		}

		[Tool("zombieland/fogged_door_spawns_room_zombies", Description = "Build a fogged sealed room, call RimWorld FogGrid.Notify_PawnEnteringDoor, and verify Zombieland spawns sudden room zombies before vanilla unfogging.")]
		public static object FoggedDoorSpawnsRoomZombies()
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			var destroyedZombies = ZombieRuntimeActions.DestroyZombies(map);
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryBuildFogRoomFixture(map, root, 32f, out var fixture, out var fixtureError) == false)
				return fixtureError;

			var doorCell = fixture.doorCell;
			var interiorRect = fixture.interiorRect;
			var door = fixture.door;
			var playerCell = doorCell + IntVec3.South;
			var hostileCell = doorCell + IntVec3.South + IntVec3.East;
			var player = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var hostile = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfAncientsHostile);
			GenSpawn.Spawn(player, playerCell, map, Rot4.North);
			GenSpawn.Spawn(hostile, hostileCell, map, Rot4.North);
			DisablePawnWork(player);
			DisablePawnWork(hostile);

			map.fogGrid.Refog(interiorRect);
			map.fogGrid.Unfog(doorCell);
			map.fogGrid.Unfog(playerCell);
			map.fogGrid.Unfog(hostileCell);
			var roomBefore = interiorRect.CenterCell.GetRoom(map);
			var roomFoggedBefore = roomBefore?.Fogged ?? false;
			var interiorFoggedBefore = interiorRect.Cells.Count(cell => cell.Fogged(map));
			var roomCellCount = roomBefore?.CellCount ?? 0;
			var zombiesBeforeHostile = CurrentZombies(map).Length;
			var oldInfectedRaidsChance = ZombieSettings.Values.infectedRaidsChance;
			var oldUseDynamicThreatLevel = ZombieSettings.Values.useDynamicThreatLevel;
			try
			{
				ZombieSettings.Values.infectedRaidsChance = 1f;
				ZombieSettings.Values.useDynamicThreatLevel = false;

				map.fogGrid.Notify_PawnEnteringDoor(door, hostile);
				var zombiesAfterHostile = CurrentZombies(map).Length;
				var roomFoggedAfterHostile = roomBefore?.Fogged ?? false;
				var interiorFoggedAfterHostile = interiorRect.Cells.Count(cell => cell.Fogged(map));

				map.fogGrid.Notify_PawnEnteringDoor(door, player);
				var zombiesAfterPlayer = CurrentZombies(map).Length;
				var roomAfter = interiorRect.CenterCell.GetRoom(map);
				var roomFoggedAfterPlayer = roomAfter?.Fogged ?? false;
				var interiorFoggedAfterPlayer = interiorRect.Cells.Count(cell => cell.Fogged(map));
				var spawnedZombies = CurrentZombies(map)
					.OfType<Zombie>()
					.Where(zombie => interiorRect.Contains(zombie.Position))
					.Select(DescribeZombie)
					.ToArray();

				return new
				{
					success = roomBefore != null
						&& roomFoggedBefore
						&& roomCellCount >= 10
						&& zombiesAfterHostile == zombiesBeforeHostile
						&& roomFoggedAfterHostile
						&& zombiesAfterPlayer > zombiesAfterHostile
						&& spawnedZombies.Length > 0
						&& roomFoggedAfterPlayer == false,
					destroyedZombies,
					door = new
					{
						id = ZombieRuntimeActions.StableThingId(door),
						position = ZombieRuntimeActions.DescribeCell(door.Position),
						door.Open
					},
					player = DescribePawn(player),
					hostile = DescribePawn(hostile),
					room = new
					{
						center = ZombieRuntimeActions.DescribeCell(interiorRect.CenterCell),
						cellCountBefore = roomCellCount,
						foggedBefore = roomFoggedBefore,
						foggedAfterHostile = roomFoggedAfterHostile,
						foggedAfterPlayer = roomFoggedAfterPlayer,
						interiorCellCount = interiorRect.Area,
						interiorFoggedBefore,
						interiorFoggedAfterHostile,
						interiorFoggedAfterPlayer
					},
					settings = new
					{
						infectedRaidsChance = ZombieSettings.Values.infectedRaidsChance,
						useDynamicThreatLevel = ZombieSettings.Values.useDynamicThreatLevel,
						threatLevel = ZombieWeather.GetThreatLevel(map)
					},
					zombiesBeforeHostile,
					zombiesAfterHostile,
					zombiesAfterPlayer,
					zombieDelta = zombiesAfterPlayer - zombiesAfterHostile,
					spawnedZombies
				};
			}
			finally
			{
				ZombieSettings.Values.infectedRaidsChance = oldInfectedRaidsChance;
				ZombieSettings.Values.useDynamicThreatLevel = oldUseDynamicThreatLevel;
			}
		}

		[Tool("zombieland/fog_blocker_removal_spawns_room_zombies", Description = "Build a fogged sealed room, destroy one fog-blocking wall, and verify Zombieland spawns sudden room zombies before vanilla unfogging.")]
		public static object FogBlockerRemovalSpawnsRoomZombies()
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			var destroyedZombies = ZombieRuntimeActions.DestroyZombies(map);
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryBuildFogRoomFixture(map, root, 32f, out var fixture, out var fixtureError) == false)
				return fixtureError;

			var doorCell = fixture.doorCell;
			var interiorRect = fixture.interiorRect;
			var targetWallCell = fixture.targetWallCell;
			var door = fixture.door;
			var targetWall = fixture.targetWall;
			map.fogGrid.Refog(interiorRect);
			map.fogGrid.Unfog(doorCell);
			map.fogGrid.Unfog(targetWallCell + IntVec3.South);
			var roomBefore = interiorRect.CenterCell.GetRoom(map);
			var roomFoggedBefore = roomBefore?.Fogged ?? false;
			var interiorFoggedBefore = interiorRect.Cells.Count(cell => cell.Fogged(map));
			var roomCellCount = roomBefore?.CellCount ?? 0;
			var zombiesBefore = CurrentZombies(map).Length;
			var oldInfectedRaidsChance = ZombieSettings.Values.infectedRaidsChance;
			var oldUseDynamicThreatLevel = ZombieSettings.Values.useDynamicThreatLevel;
			try
			{
				ZombieSettings.Values.infectedRaidsChance = 1f;
				ZombieSettings.Values.useDynamicThreatLevel = false;

				targetWall.Destroy(DestroyMode.Deconstruct);
				var zombiesAfter = CurrentZombies(map).Length;
				var roomAfter = interiorRect.CenterCell.GetRoom(map);
				var roomFoggedAfter = roomAfter?.Fogged ?? false;
				var interiorFoggedAfter = interiorRect.Cells.Count(cell => cell.Fogged(map));
				var spawnedZombies = CurrentZombies(map)
					.OfType<Zombie>()
					.Where(zombie => interiorRect.Contains(zombie.Position))
					.Select(DescribeZombie)
					.ToArray();

				return new
				{
					success = roomBefore != null
						&& targetWall.Destroyed
						&& targetWall.def.MakeFog
						&& roomFoggedBefore
						&& roomCellCount >= 10
						&& zombiesAfter > zombiesBefore
						&& spawnedZombies.Length > 0
						&& roomFoggedAfter == false,
					destroyedZombies,
					door = new
					{
						id = ZombieRuntimeActions.StableThingId(door),
						position = ZombieRuntimeActions.DescribeCell(door.Position),
						door.Open
					},
					targetWall = new
					{
						id = ZombieRuntimeActions.StableThingId(targetWall),
						position = ZombieRuntimeActions.DescribeCell(targetWallCell),
						destroyed = targetWall.Destroyed,
						defName = targetWall.def?.defName,
						makeFog = targetWall.def?.MakeFog ?? false
					},
					room = new
					{
						center = ZombieRuntimeActions.DescribeCell(interiorRect.CenterCell),
						cellCountBefore = roomCellCount,
						foggedBefore = roomFoggedBefore,
						foggedAfter = roomFoggedAfter,
						interiorCellCount = interiorRect.Area,
						interiorFoggedBefore,
						interiorFoggedAfter
					},
					settings = new
					{
						infectedRaidsChance = ZombieSettings.Values.infectedRaidsChance,
						useDynamicThreatLevel = ZombieSettings.Values.useDynamicThreatLevel,
						threatLevel = ZombieWeather.GetThreatLevel(map)
					},
					zombiesBefore,
					zombiesAfter,
					zombieDelta = zombiesAfter - zombiesBefore,
					spawnedZombies
				};
			}
			finally
			{
				ZombieSettings.Values.infectedRaidsChance = oldInfectedRaidsChance;
				ZombieSettings.Values.useDynamicThreatLevel = oldUseDynamicThreatLevel;
			}
		}

		[Tool("zombieland/fog_blocker_replacement_does_not_spawn_room_zombies", Description = "Build a fogged sealed room, destroy one fog-blocking wall with WillReplace, and verify replacement mode does not spawn sudden room zombies.")]
		public static object FogBlockerReplacementDoesNotSpawnRoomZombies()
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			var destroyedZombies = ZombieRuntimeActions.DestroyZombies(map);
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryBuildFogRoomFixture(map, root, 32f, out var fixture, out var fixtureError) == false)
				return fixtureError;

			var doorCell = fixture.doorCell;
			var interiorRect = fixture.interiorRect;
			var targetWallCell = fixture.targetWallCell;
			var door = fixture.door;
			var targetWall = fixture.targetWall;
			map.fogGrid.Refog(interiorRect);
			map.fogGrid.Unfog(doorCell);
			map.fogGrid.Unfog(targetWallCell + IntVec3.South);
			var roomBefore = interiorRect.CenterCell.GetRoom(map);
			var roomFoggedBefore = roomBefore?.Fogged ?? false;
			var interiorFoggedBefore = interiorRect.Cells.Count(cell => cell.Fogged(map));
			var roomCellCount = roomBefore?.CellCount ?? 0;
			var zombiesBefore = CurrentZombies(map).Length;
			var oldInfectedRaidsChance = ZombieSettings.Values.infectedRaidsChance;
			var oldUseDynamicThreatLevel = ZombieSettings.Values.useDynamicThreatLevel;
			try
			{
				ZombieSettings.Values.infectedRaidsChance = 1f;
				ZombieSettings.Values.useDynamicThreatLevel = false;

				targetWall.Destroy(DestroyMode.WillReplace);
				var zombiesAfter = CurrentZombies(map).Length;
				var roomAfter = interiorRect.CenterCell.GetRoom(map);
				var roomFoggedAfter = roomAfter?.Fogged ?? false;
				var interiorFoggedAfter = interiorRect.Cells.Count(cell => cell.Fogged(map));
				var spawnedZombies = CurrentZombies(map)
					.OfType<Zombie>()
					.Where(zombie => interiorRect.Contains(zombie.Position))
					.Select(DescribeZombie)
					.ToArray();

				return new
				{
					success = roomBefore != null
						&& targetWall.Destroyed
						&& targetWall.def.MakeFog
						&& roomFoggedBefore
						&& roomCellCount >= 10
						&& zombiesAfter == zombiesBefore
						&& spawnedZombies.Length == 0
						&& interiorFoggedAfter == interiorFoggedBefore,
					destroyedZombies,
					door = new
					{
						id = ZombieRuntimeActions.StableThingId(door),
						position = ZombieRuntimeActions.DescribeCell(door.Position),
						door.Open
					},
					targetWall = new
					{
						id = ZombieRuntimeActions.StableThingId(targetWall),
						position = ZombieRuntimeActions.DescribeCell(targetWallCell),
						destroyed = targetWall.Destroyed,
						defName = targetWall.def?.defName,
						makeFog = targetWall.def?.MakeFog ?? false,
						destroyMode = DestroyMode.WillReplace.ToString()
					},
					room = new
					{
						center = ZombieRuntimeActions.DescribeCell(interiorRect.CenterCell),
						cellCountBefore = roomCellCount,
						foggedBefore = roomFoggedBefore,
						foggedAfter = roomFoggedAfter,
						interiorCellCount = interiorRect.Area,
						interiorFoggedBefore,
						interiorFoggedAfter
					},
					settings = new
					{
						infectedRaidsChance = ZombieSettings.Values.infectedRaidsChance,
						useDynamicThreatLevel = ZombieSettings.Values.useDynamicThreatLevel,
						threatLevel = ZombieWeather.GetThreatLevel(map)
					},
					zombiesBefore,
					zombiesAfter,
					zombieDelta = zombiesAfter - zombiesBefore,
					spawnedZombies
				};
			}
			finally
			{
				ZombieSettings.Values.infectedRaidsChance = oldInfectedRaidsChance;
				ZombieSettings.Values.useDynamicThreatLevel = oldUseDynamicThreatLevel;
			}
		}

		[Tool("zombieland/detonate_suicide_bomber", Description = "Kill a suicide bomber through Zombie.Kill, verify it queued a Zombieland explosion, then execute the explosion.")]
		public static object DetonateSuicideBomber(
			[ToolParameter(Description = "Optional zombie id, ThingID, label, or short name. When omitted, the first spawned suicide bomber is used.", Required = false, DefaultValue = "")] string target = "")
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			Pawn pawn;
			string error;
			if (string.IsNullOrWhiteSpace(target))
			{
				pawn = CurrentZombies(map).OfType<Zombie>().FirstOrDefault(zombie => zombie.IsSuicideBomber);
				if (pawn == null)
				{
					var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
					if (TryFindClearSpawnCell(map, root, 16f, out var cell, out var spawnError) == false)
						return spawnError;

					pawn = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.SuicideBomber, true);
					if (pawn == null)
					{
						return new
						{
							success = false,
							cell = ZombieRuntimeActions.DescribeCell(cell),
							error = "ZombieGenerator.SpawnZombie returned no suicide bomber."
						};
					}

					if (pawn is not Zombie spawnedZombie || spawnedZombie.IsSuicideBomber == false)
					{
						return new
						{
							success = false,
							target = DescribeZombie(pawn),
							error = "Freshly spawned suicide bomber target was not a suicide bomber."
						};
					}
				}
			}
			else if (TryFindZombie(map, target, out pawn, out error) == false)
			{
				return new
				{
					success = false,
					error
				};
			}

			if (pawn is not Zombie zombie || zombie.IsSuicideBomber == false)
			{
				return new
				{
					success = false,
					target = DescribeZombie(pawn),
					error = "Target is not a suicide bomber."
				};
			}

			var tickManager = map.GetComponent<TickManager>();
			if (tickManager == null)
			{
				return new
				{
					success = false,
					target = DescribeZombie(zombie),
					error = "The current map has no Zombieland tick manager."
				};
			}

			var beforeZombieCount = CurrentZombies(map).Length;
			var before = DescribeZombie(zombie);
			var position = zombie.Position;
			var queuedBeforeKill = tickManager.explosions?.Count ?? 0;
			zombie.Kill(null);
			var queuedAfterKill = tickManager.explosions?.Count ?? 0;
			tickManager.ExecuteExplosions();
			var queuedAfterExecute = tickManager.explosions?.Count ?? 0;
			var afterZombieCount = CurrentZombies(map).Length;

			return new
			{
				success = zombie.Dead && queuedAfterKill == queuedBeforeKill + 1 && queuedAfterExecute == 0,
				position = ZombieRuntimeActions.DescribeCell(position),
				before,
				dead = zombie.Dead,
				destroyed = zombie.Destroyed,
				queuedBeforeKill,
				queuedAfterKill,
				queuedAfterExecute,
				beforeZombieCount,
				afterZombieCount,
				explosionQueued = queuedAfterKill == queuedBeforeKill + 1,
				explosionExecuted = queuedAfterExecute == 0
			};
		}

		[Tool("zombieland/suicide_bomber_countdown_contract", Description = "Verify a suicide bomber only detonates through the real Stumble countdown after bombWillGoOff is armed.")]
		public static object SuicideBomberCountdownContract()
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			var maxTicks = 12;
			var sourceCadence = "ZombieStateHandler.ShouldDie uses zombie.EveryNTick(NthTick.Every10)";
			var explosionQueueObservation = "Full game ticks drain TickManager.explosions after the countdown kill; zombieland/detonate_suicide_bomber covers direct Kill queueing.";
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			var allCasesSucceeded = true;

			bool TryFindCountdownCell(IntVec3 rootCell, out IntVec3 cell, out object error)
			{
				cell = IntVec3.Invalid;
				error = null;
				foreach (var candidate in GenRadial.RadialCellsAround(rootCell, 20f, true))
				{
					if (candidate.InBounds(map) == false || candidate.Fogged(map) || candidate.Standable(map) == false)
						continue;
					if (candidate.GetEdifice(map) != null || candidate.GetFirstThing<Mineable>(map) != null)
						continue;
					if (candidate.GetThingList(map).Any(thing => thing is Pawn))
						continue;
					var adjacentHasBuilding = false;
					foreach (var direction in GenAdj.CardinalDirections)
					{
						var adjacent = candidate + direction;
						if (adjacent.InBounds(map) == false)
							continue;
						if (adjacent.GetEdifice(map) != null || adjacent.GetFirstThing<Mineable>(map) != null)
						{
							adjacentHasBuilding = true;
							break;
						}
					}
					if (adjacentHasBuilding)
						continue;

					cell = candidate;
					return true;
				}

				error = new
				{
					success = false,
					error = $"No clear suicide-bomber countdown cell was found near ({rootCell.x}, {rootCell.z})."
				};
				return false;
			}

			object RunCase(string name, bool armed, IntVec3 caseRoot)
			{
				if (TryFindCountdownCell(caseRoot, out var cell, out var error) == false)
				{
					allCasesSucceeded = false;
					return new { name, success = false, error };
				}

				var zombie = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.SuicideBomber, true);
				if (zombie == null || zombie.IsSuicideBomber == false)
				{
					allCasesSucceeded = false;
					return new
					{
						name,
						success = false,
						error = "Could not spawn a suicide bomber.",
						cell = ZombieRuntimeActions.DescribeCell(cell),
						zombie = DescribeZombie(zombie)
					};
				}

				var tickManager = map.GetComponent<TickManager>();
				tickManager?.allZombiesCached?.RemoveWhere(cached => cached == null || cached.Destroyed || cached.Spawned == false || cached.Dead);
				_ = tickManager?.allZombiesCached?.Add(zombie);
				zombie.pather?.StopDead();
				zombie.jobs?.EndCurrentJob(JobCondition.InterruptForced);
				zombie.state = ZombieState.Wandering;
				zombie.bombWillGoOff = armed;
				zombie.bombTickingInterval = 1f;
				zombie.lastBombTick = GenTicks.TicksAbs;
				zombie.Rotation = Rot4.South;

				var before = DescribeZombie(zombie);
				var queuedBefore = tickManager?.explosions?.Count ?? 0;
				var samples = new List<object>();
				zombie.jobs.StartJob(JobMaker.MakeJob(CustomDefs.Stumble), JobCondition.InterruptForced, null, true, false, null, null);
				for (var tick = 1; tick <= maxTicks; tick++)
				{
					AdvanceGameTicks(1);
					var queued = tickManager?.explosions?.Count ?? 0;
					samples.Add(new
					{
						tick,
						gameTick = Find.TickManager.TicksGame,
						dead = zombie.Dead,
						destroyed = zombie.Destroyed,
						bombWillGoOff = zombie.bombWillGoOff,
						bombTickingInterval = zombie.bombTickingInterval,
						queuedExplosions = queued,
						currentJob = zombie.CurJobDef?.defName
					});
					if (zombie.Dead || queued > queuedBefore)
						break;
				}

				var queuedAfterCountdown = tickManager?.explosions?.Count ?? 0;
				if (armed && tickManager != null)
					tickManager.ExecuteExplosions();
				var queuedAfterExecute = tickManager?.explosions?.Count ?? 0;
				var success = armed
					? zombie.Dead && queuedAfterCountdown == queuedBefore && queuedAfterExecute == queuedBefore
					: zombie.Dead == false && queuedAfterCountdown == queuedBefore;
				allCasesSucceeded &= success;

				return new
				{
					name,
					success,
					armed,
					sourceCadence,
					explosionQueueObservation,
					maxTicks,
					cell = ZombieRuntimeActions.DescribeCell(cell),
					before,
					after = DescribeZombie(zombie),
					queuedBefore,
					queuedAfterCountdown,
					queuedAfterExecute,
					samples
				};
			}

			var unarmedCase = RunCase("unarmedIntervalDoesNotDetonate", false, root + new IntVec3(-10, 0, 10));
			var armedCase = RunCase("armedIntervalDetonates", true, root + new IntVec3(10, 0, 10));
			var cases = new[] { unarmedCase, armedCase };
			return new
			{
				success = allCasesSucceeded,
				sourceCadence,
				explosionQueueObservation,
				maxTicks,
				cases
			};
		}

		[Tool("zombieland/kill_toxic_splasher", Description = "Kill a toxic splasher through Zombie.Kill and verify that it drops StickyGoo around its death cell.")]
		public static object KillToxicSplasher(
			[ToolParameter(Description = "Optional zombie id, ThingID, label, or short name. When omitted, the first spawned toxic splasher is used.", Required = false, DefaultValue = "")] string target = "",
			[ToolParameter(Description = "Radius around the death cell used to count StickyGoo before and after death.", Required = false, DefaultValue = 8)] int radius = 8)
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			Pawn pawn;
			string error;
			if (string.IsNullOrWhiteSpace(target))
			{
				pawn = CurrentZombies(map).OfType<Zombie>().FirstOrDefault(zombie => zombie.isToxicSplasher);
				if (pawn == null)
				{
					return new
					{
						success = false,
						error = "No spawned toxic splasher was found."
					};
				}
			}
			else if (TryFindZombie(map, target, out pawn, out error) == false)
			{
				return new
				{
					success = false,
					error
				};
			}

			if (pawn is not Zombie zombie || zombie.isToxicSplasher == false)
			{
				return new
				{
					success = false,
					target = DescribeZombie(pawn),
					error = "Target is not a toxic splasher."
				};
			}

			var cappedRadius = Math.Max(1, Math.Min(radius, 24));
			var beforeZombieCount = CurrentZombies(map).Length;
			var before = DescribeZombie(zombie);
			var position = zombie.Position;
			var stickyGooBefore = CountThingsNear(map, position, CustomDefs.StickyGoo, cappedRadius);
			zombie.Kill(null);
			var stickyGooAfter = CountThingsNear(map, position, CustomDefs.StickyGoo, cappedRadius);
			var afterZombieCount = CurrentZombies(map).Length;

			return new
			{
				success = zombie.Dead && stickyGooAfter > stickyGooBefore,
				position = ZombieRuntimeActions.DescribeCell(position),
				radius = cappedRadius,
				before,
				dead = zombie.Dead,
				destroyed = zombie.Destroyed,
				stickyGooBefore,
				stickyGooAfter,
				stickyGooDelta = stickyGooAfter - stickyGooBefore,
				beforeZombieCount,
				afterZombieCount
			};
		}

		[Tool("zombieland/move_dark_slimer", Description = "Move a dark slimer one valid adjacent cell and verify that it leaves TarSlime through the position-change patch.")]
		public static object MoveDarkSlimer(
			[ToolParameter(Description = "Optional zombie id, ThingID, label, or short name. When omitted, the first spawned dark slimer is used.", Required = false, DefaultValue = "")] string target = "",
			[ToolParameter(Description = "Radius around the start cell used to count TarSlime before and after the move.", Required = false, DefaultValue = 4)] int radius = 4)
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			Pawn pawn;
			string error;
			if (string.IsNullOrWhiteSpace(target))
			{
				pawn = CurrentZombies(map).OfType<Zombie>().FirstOrDefault(zombie => zombie.isDarkSlimer);
				if (pawn == null)
				{
					return new
					{
						success = false,
						error = "No spawned dark slimer was found."
					};
				}
			}
			else if (TryFindZombie(map, target, out pawn, out error) == false)
			{
				return new
				{
					success = false,
					error
				};
			}

			if (pawn is not Zombie zombie || zombie.isDarkSlimer == false)
			{
				return new
				{
					success = false,
					target = DescribeZombie(pawn),
					error = "Target is not a dark slimer."
				};
			}

			if (TryFindAdjacentMoveCell(zombie, out var destination) == false)
			{
				return new
				{
					success = false,
					target = DescribeZombie(zombie),
					error = "No valid adjacent move cell was found."
				};
			}

			var cappedRadius = Math.Max(1, Math.Min(radius, 12));
			var before = DescribeZombie(zombie);
			var origin = zombie.Position;
			var tarSlimeBefore = CountThingsNear(map, origin, CustomDefs.TarSlime, cappedRadius);
			zombie.pather?.StopDead();
			zombie.Position = destination;
			zombie.Notify_Teleported(false, false);
			var tarSlimeAfter = CountThingsNear(map, origin, CustomDefs.TarSlime, cappedRadius);
			var after = DescribeZombie(zombie);

			return new
			{
				success = zombie.Position == destination && tarSlimeAfter > tarSlimeBefore,
				radius = cappedRadius,
				origin = ZombieRuntimeActions.DescribeCell(origin),
				destination = ZombieRuntimeActions.DescribeCell(destination),
				before,
				after,
				tarSlimeBefore,
				tarSlimeAfter,
				tarSlimeDelta = tarSlimeAfter - tarSlimeBefore
			};
		}

		[Tool("zombieland/heal_wounded_zombie", Description = "Use a healer zombie to clear a nearby wounded zombie's hediffs and verify the heal effect queue.")]
		public static object HealWoundedZombie(
			[ToolParameter(Description = "Optional healer zombie id, ThingID, label, or short name. When omitted, the first spawned healer is used, or one is spawned near map center.", Required = false, DefaultValue = "")] string target = "")
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			Zombie healer;
			var spawnedHealer = false;
			if (string.IsNullOrWhiteSpace(target))
			{
				healer = CurrentZombies(map).OfType<Zombie>().FirstOrDefault(zombie => zombie.isHealer);
				if (healer == null)
				{
					var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
					if (TryFindClearSpawnCell(map, root, 16f, out var healerCell, out var error) == false)
						return error;

					healer = ZombieRuntimeActions.SpawnZombie(healerCell, map, ZombieType.Healer, true);
					spawnedHealer = true;
				}
			}
			else if (TryFindZombie(map, target, out var pawn, out var error) == false)
			{
				return new
				{
					success = false,
					error
				};
			}
			else
			{
				healer = pawn as Zombie;
			}

			if (healer == null || healer.isHealer == false)
			{
				return new
				{
					success = false,
					target = DescribeZombie(healer),
					error = "Target is not a healer."
				};
			}

			if (TryFindAdjacentMoveCell(healer, out var targetCell) == false)
			{
				return new
				{
					success = false,
					target = DescribeZombie(healer),
					error = "No clear adjacent cell was found for the wounded test zombie."
				};
			}

			var wounded = ZombieRuntimeActions.SpawnZombie(targetCell, map, ZombieType.Normal, true);
			if (wounded == null)
			{
				return new
				{
					success = false,
					target = DescribeZombie(healer),
					error = "ZombieGenerator.SpawnZombie returned no wounded test zombie."
				};
			}

			var wound = HediffMaker.MakeHediff(HediffDefOf.BloodLoss, wounded);
			wounded.health.AddHediff(wound);
			var healerInfoBefore = healer.healInfo.Count;
			var hediffsBefore = wounded.health.hediffSet.hediffs.Count;
			healer.CustomTick(1f);
			var healerInfoAfter = healer.healInfo.Count;
			var hediffsAfter = wounded.health.hediffSet.hediffs.Count;
			var queuedHealEffect = healer.healInfo.Any(info => ReferenceEquals(info.pawn, wounded));

			return new
			{
				success = hediffsBefore > 0 && hediffsAfter == 0 && healerInfoAfter > healerInfoBefore && queuedHealEffect,
				spawnedHealer,
				healer = DescribeZombie(healer),
				wounded = DescribeZombie(wounded),
				woundedCell = ZombieRuntimeActions.DescribeCell(targetCell),
				hediffsBefore,
				hediffsAfter,
				healerInfoBefore,
				healerInfoAfter,
				queuedHealEffect
			};
		}

		[Tool("zombieland/heal_wounded_zombie_tick", Description = "Use the real zombie tick loop to verify a healer clears a nearby wounded zombie on its Every12 interval.")]
		public static object HealWoundedZombieTick(
			[ToolParameter(Description = "Optional healer zombie id, ThingID, label, or short name. When omitted, a fresh healer is spawned near map center for deterministic tick timing.", Required = false, DefaultValue = "")] string target = "")
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			Zombie healer;
			var spawnedHealer = false;
			if (string.IsNullOrWhiteSpace(target))
			{
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var healerCell, out var error) == false)
					return error;

				healer = ZombieRuntimeActions.SpawnZombie(healerCell, map, ZombieType.Healer, true);
				spawnedHealer = true;
			}
			else if (TryFindZombie(map, target, out var pawn, out var error) == false)
			{
				return new
				{
					success = false,
					error
				};
			}
			else
			{
				healer = pawn as Zombie;
			}

			if (healer == null || healer.isHealer == false)
			{
				return new
				{
					success = false,
					target = DescribeZombie(healer),
					error = "Target is not a healer."
				};
			}

			if (TryFindAdjacentMoveCell(healer, out var targetCell) == false)
			{
				return new
				{
					success = false,
					target = DescribeZombie(healer),
					error = "No clear adjacent cell was found for the wounded test zombie."
				};
			}

			var wounded = ZombieRuntimeActions.SpawnZombie(targetCell, map, ZombieType.Normal, true);
			if (wounded == null)
			{
				return new
				{
					success = false,
					target = DescribeZombie(healer),
					error = "ZombieGenerator.SpawnZombie returned no wounded test zombie."
				};
			}

			healer.healInfo.Clear();
			var wound = HediffMaker.MakeHediff(HediffDefOf.BloodLoss, wounded);
			wounded.health.AddHediff(wound);

			var healerInfoBefore = healer.healInfo.Count;
			var hediffsBefore = wounded.health.hediffSet.hediffs.Count;
			var interval = Zombie.nthTickValues[(int)NthTick.Every12];
			var maxTicks = interval + 1;
			var tickHit = -1;
			var samples = new List<object>();

			for (var tick = 1; tick <= maxTicks; tick++)
			{
				AdvanceGameTicks(1);
				var hediffsNow = wounded.health.hediffSet.hediffs.Count;
				var queuedNow = healer.healInfo.Any(info => ReferenceEquals(info.pawn, wounded));
				samples.Add(new
				{
					tick,
					hediffs = hediffsNow,
					healerInfo = healer.healInfo.Count,
					queuedHealEffect = queuedNow
				});

				if (hediffsNow == 0 && queuedNow)
				{
					tickHit = tick;
					break;
				}
			}

			var healerInfoAfter = healer.healInfo.Count;
			var hediffsAfter = wounded.health.hediffSet.hediffs.Count;
			var queuedHealEffect = healer.healInfo.Any(info => ReferenceEquals(info.pawn, wounded));

			return new
			{
				success = hediffsBefore > 0 && hediffsAfter == 0 && healerInfoAfter > healerInfoBefore && queuedHealEffect && tickHit > 0 && tickHit <= maxTicks,
				spawnedHealer,
				interval,
				maxTicks,
				tickHit,
				healer = DescribeZombie(healer),
				wounded = DescribeZombie(wounded),
				woundedCell = ZombieRuntimeActions.DescribeCell(targetCell),
				hediffsBefore,
				hediffsAfter,
				healerInfoBefore,
				healerInfoAfter,
				queuedHealEffect,
				samples
			};
		}

		[Tool("zombieland/emp_electrifier", Description = "Apply real EMP damage to an electrifier zombie and verify the deactivation patch disables its electric state.")]
		public static object EmpElectrifier(
			[ToolParameter(Description = "Optional electrifier zombie id, ThingID, label, or short name. When omitted, the first spawned electrifier is used, or one is spawned near map center.", Required = false, DefaultValue = "")] string target = "",
			[ToolParameter(Description = "EMP damage amount. The disable duration should increase by this amount times 60 ticks.", Required = false, DefaultValue = 5)] int damage = 5)
		{
			var patchTargets = PatchedMethodsForPatchClass("DamageFlasher_Notify_DamageApplied_Patch");
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					patchTargets,
					error = "No current map is loaded."
				};
			}

			Zombie electrifier = null;
			var spawnedElectrifier = false;
			try
			{
				if (string.IsNullOrWhiteSpace(target))
				{
					electrifier = CurrentZombies(map).OfType<Zombie>().FirstOrDefault(zombie => zombie.isElectrifier);
					if (electrifier == null)
					{
						var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
						if (TryFindClearSpawnCell(map, root, 16f, out var cell, out var error) == false)
							return error;

						electrifier = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.Electrifier, true);
						spawnedElectrifier = true;
					}
				}
				else if (TryFindZombie(map, target, out var pawn, out var error) == false)
				{
					return new
					{
						success = false,
						patchTargets,
						error
					};
				}
				else
				{
					electrifier = pawn as Zombie;
				}

				if (electrifier == null || electrifier.isElectrifier == false)
				{
					return new
					{
						success = false,
						patchTargets,
						target = DescribeZombie(electrifier),
						error = "Target is not an electrifier."
					};
				}

				var cappedDamage = Math.Max(1, Math.Min(damage, 60));
				var tickBefore = GenTicks.TicksGame;
				var disabledUntilBefore = electrifier.electricDisabledUntil;
				var activeBefore = electrifier.IsActiveElectric;
				var before = DescribeZombie(electrifier);
				var dinfo = new DamageInfo(DamageDefOf.EMP, cappedDamage, 0f, -1f, null, null, null, DamageInfo.SourceCategory.ThingOrUnknown, null, true, true);
				var damageResult = electrifier.TakeDamage(dinfo);
				var disabledUntilAfter = electrifier.electricDisabledUntil;
				var activeAfter = electrifier.IsActiveElectric;

				return new
				{
					success = patchTargets.Length > 0
						&& activeBefore
						&& activeAfter == false
						&& disabledUntilAfter >= tickBefore + cappedDamage * 60,
					patchTargets,
					spawnedElectrifier,
					damage = cappedDamage,
					tickBefore,
					disabledUntilBefore,
					disabledUntilAfter,
					activeBefore,
					activeAfter,
					damageTotal = damageResult.totalDamageDealt,
					before,
					after = DescribeZombie(electrifier)
				};
			}
			finally
			{
				if (spawnedElectrifier && electrifier != null && electrifier.Destroyed == false)
					electrifier.Destroy(DestroyMode.Vanish);
			}
		}

		[Tool("zombieland/electrify_powered_building", Description = "Place an active electrifier next to a real power conduit and verify the electrify handler disables it.")]
		public static object ElectrifyPoweredBuilding()
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var electrifierCell, out var error) == false)
				return error;

			var electrifier = ZombieRuntimeActions.SpawnZombie(electrifierCell, map, ZombieType.Electrifier, true);
			if (electrifier == null)
			{
				return new
				{
					success = false,
					error = "ZombieGenerator.SpawnZombie returned no electrifier test zombie."
				};
			}

			if (TryFindAdjacentBuildingCell(electrifier, out var buildingCell) == false)
			{
				return new
				{
					success = false,
					electrifier = DescribeZombie(electrifier),
					error = "No clear adjacent building cell was found for the electrifier test."
				};
			}

			var conduitDef = DefDatabase<ThingDef>.GetNamed("PowerConduit", false);
			var lampDef = DefDatabase<ThingDef>.GetNamed("StandingLamp", false);
			if (conduitDef == null || lampDef == null)
			{
				return new
				{
					success = false,
					error = "ThingDef PowerConduit or StandingLamp was not found."
				};
			}

			var conduit = GenSpawn.Spawn(ThingMaker.MakeThing(conduitDef), buildingCell, map, WipeMode.Vanish) as Building;
			var lamp = GenSpawn.Spawn(ThingMaker.MakeThing(lampDef), buildingCell, map, WipeMode.Vanish) as Building;
			conduit?.SetFaction(Faction.OfPlayer);
			lamp?.SetFaction(Faction.OfPlayer);
			var conduitPower = conduit?.GetComp<CompPower>();
			var lampPower = lamp?.GetComp<CompPowerTrader>();
			if (conduitPower != null)
			{
				map.powerNetManager.Notify_TransmitterSpawned(conduitPower);
				map.powerNetManager.UpdatePowerNetsAndConnections_First();
			}
			if (lampPower?.PowerNet == null && conduitPower != null)
				lampPower.ConnectToTransmitter(conduitPower);
			var powerNetBefore = lampPower?.PowerNet;
			electrifier.electricDisabledUntil = GenTicks.TicksGame - 1;
			var activeBefore = electrifier.IsActiveElectric;
			var disabledUntilBefore = electrifier.electricDisabledUntil;
			var ticksGameBefore = GenTicks.TicksGame;
			var fireBefore = CountThingsNear(map, buildingCell, ThingDefOf.Fire, 1.5f);

			ZombieStateHandler.Electrify(electrifier);

			var fireAfter = CountThingsNear(map, buildingCell, ThingDefOf.Fire, 1.5f);
			var disabledUntilAfter = electrifier.electricDisabledUntil;
			var activeAfter = electrifier.IsActiveElectric;
			var expectedMinimumDisableUntil = ticksGameBefore + GenDate.TicksPerHour / 4;

			return new
			{
				success = conduit != null && lamp != null && powerNetBefore != null && activeBefore && activeAfter == false && disabledUntilAfter >= expectedMinimumDisableUntil,
				electrifier = DescribeZombie(electrifier),
				electrifierCell = ZombieRuntimeActions.DescribeCell(electrifierCell),
				buildingCell = ZombieRuntimeActions.DescribeCell(buildingCell),
				buildingDef = lamp?.def?.defName,
				conduitDef = conduit?.def?.defName,
				hadConduitPower = conduitPower != null,
				hadConduitPowerNet = conduitPower?.PowerNet != null,
				hadPowerNet = powerNetBefore != null,
				activeBefore,
				activeAfter,
				ticksGameBefore,
				disabledUntilBefore,
				disabledUntilAfter,
				expectedMinimumDisableUntil,
				fireBefore,
				fireAfter,
				fireDelta = fireAfter - fireBefore
			};
		}

		[Tool("zombieland/active_electrifier_attack_verb_contract", Description = "Verify ordinary ranged pawns use ranged verbs on normal zombies but not on active electrifiers.")]
		public static object ActiveElectrifierAttackVerbContract()
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			var destroyedZombies = ZombieRuntimeActions.DestroyZombies(map);
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var actorCell, out var actorSpawnError) == false)
				return actorSpawnError;

			var targetCells = GenRadial.RadialCellsAround(actorCell, 14f, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.Where(cell => cell.DistanceTo(actorCell) >= 7f)
				.Where(cell => GenSight.LineOfSight(actorCell, cell, map, true))
				.OrderBy(cell => cell.DistanceToSquared(actorCell))
				.Take(3)
				.ToArray();
			if (targetCells.Length < 3)
			{
				return new
				{
					success = false,
					actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
					error = "Fewer than three clear line-of-sight target cells were found for the active electrifier attack-verb fixture."
				};
			}

			var closeZombieCell = GenRadial.RadialCellsAround(actorCell, Constants.HUMAN_PHEROMONE_RADIUS - 0.5f, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.Where(cell => cell.DistanceTo(actorCell) > 1f)
				.Where(cell => cell.DistanceTo(actorCell) < Constants.HUMAN_PHEROMONE_RADIUS)
				.Where(cell => GenSight.LineOfSight(actorCell, cell, map, true))
				.OrderByDescending(cell => cell.DistanceToSquared(actorCell))
				.FirstOrDefault();
			if (closeZombieCell.IsValid == false)
			{
				return new
				{
					success = false,
					actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
					error = "No close line-of-sight target cell was found for the time-slowdown fixture."
				};
			}
			if (TryFindAdjacentPawnPairCells(map, actorCell + new IntVec3(-10, 0, 0), out var attackZombieCell, out var attackTargetCell, out var attackCellError) == false)
				return attackCellError;

			var patchTargets = PatchedMethodsForPatchClass("Pawn_TryGetAttackVerb_Patch");
			var slowdownPatchTargets = PatchedMethodsForPatchClass("Verb_CausesTimeSlowdown_Patch");
			var startAttackPatchTargets = PatchedMethodsForPatchClass("Pawn_TryStartAttack_Patch");
			var causesTimeSlowdownMethod = AccessTools.Method(typeof(Verb), "CausesTimeSlowdown");
			var originalZombieKindDisabledWorkTags = ZombieDefOf.Zombie.disabledWorkTags;
			Pawn actor = null;
			Pawn spitter = null;
			Pawn hostileHuman = null;
			Pawn attackTarget = null;
			Zombie normal = null;
			Zombie closeNormal = null;
			Zombie electrifier = null;
			Zombie attackZombie = null;
			try
			{
				actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(actor, actorCell, map, Rot4.South);
				DisablePawnWork(actor);
				actor.equipment?.DestroyAllEquipment(DestroyMode.Vanish);
				actor.drafter.Drafted = true;

				var weaponDef = DefDatabase<ThingDef>.GetNamed("Gun_BoltActionRifle", false)
					?? DefDatabase<ThingDef>.GetNamed("Gun_Pistol", false);
				var weapon = weaponDef == null ? null : ThingMaker.MakeThing(weaponDef) as ThingWithComps;
				if (weapon == null)
				{
					return new
					{
						success = false,
						patchTargets,
						actor = DescribePawn(actor),
						error = "No test ranged weapon def was available."
					};
				}
				actor.equipment.AddEquipment(weapon);

				normal = ZombieRuntimeActions.SpawnZombie(targetCells[0], map, ZombieType.Normal, true);
				electrifier = ZombieRuntimeActions.SpawnZombie(targetCells[1], map, ZombieType.Electrifier, true);
				closeNormal = ZombieRuntimeActions.SpawnZombie(closeZombieCell, map, ZombieType.Normal, true);
				attackZombie = ZombieRuntimeActions.SpawnZombie(attackZombieCell, map, ZombieType.Normal, true);
				spitter = SpawnFireFixturePawn(map, targetCells[0] + new IntVec3(0, 0, 1), "spitter");
				hostileHuman = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfAncientsHostile);
				GenSpawn.Spawn(hostileHuman, targetCells[2], map, Rot4.South);
				DisablePawnWork(hostileHuman);
				attackTarget = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(attackTarget, attackTargetCell, map, Rot4.South);
				DisablePawnWork(attackTarget);
				if (normal == null || electrifier == null || closeNormal == null || attackZombie == null || spitter == null || hostileHuman == null || attackTarget == null)
				{
					return new
					{
						success = false,
						patchTargets,
						slowdownPatchTargets,
						startAttackPatchTargets,
						actor = DescribePawn(actor),
						normal = DescribeZombie(normal),
						electrifier = DescribeZombie(electrifier),
						closeNormal = DescribeZombie(closeNormal),
						attackZombie = DescribeZombie(attackZombie),
						spitter = DescribePawn(spitter),
						hostileHuman = DescribePawn(hostileHuman),
						attackTarget = DescribePawn(attackTarget),
						error = "ZombieGenerator.SpawnZombie returned no normal zombie, close zombie, attack zombie, electrifier, spitter, hostile-human, or attack-target test pawn."
					};
				}
				if (causesTimeSlowdownMethod == null)
				{
					return new
					{
						success = false,
						patchTargets,
						slowdownPatchTargets,
						startAttackPatchTargets,
						error = "Could not reflect Verb.CausesTimeSlowdown."
					};
				}

				electrifier.electricDisabledUntil = GenTicks.TicksGame - 1;
				var electrifierActive = electrifier.IsActiveElectric;
				var normalVerb = actor.TryGetAttackVerb(normal);
				var electrifierVerb = actor.TryGetAttackVerb(electrifier);
				var spitterVerb = spitter.TryGetAttackVerb(actor);
				var normalRanged = normalVerb != null && normalVerb.IsMeleeAttack == false;
				var electrifierSafe = electrifierVerb == null || electrifierVerb.CanHarmElectricZombies();
				var spitterBlocked = spitterVerb == null;
				var farZombieSlowdown = normalVerb != null && (bool)causesTimeSlowdownMethod.Invoke(normalVerb, new object[] { new LocalTargetInfo(normal) });
				var closeZombieSlowdown = normalVerb != null && (bool)causesTimeSlowdownMethod.Invoke(normalVerb, new object[] { new LocalTargetInfo(closeNormal) });
				var hostileHumanSlowdown = normalVerb != null && (bool)causesTimeSlowdownMethod.Invoke(normalVerb, new object[] { new LocalTargetInfo(hostileHuman) });
				var slowdownSuppressionDistance = actor.Position.DistanceToSquared(normal.Position);
				var closeSlowdownDistance = actor.Position.DistanceToSquared(closeNormal.Position);
				var humanSlowdownDistance = actor.Position.DistanceToSquared(hostileHuman.Position);
				var slowdownDistanceThreshold = Constants.HUMAN_PHEROMONE_RADIUS * Constants.HUMAN_PHEROMONE_RADIUS;
				var timeSlowdownCovered = slowdownPatchTargets.Length > 0
					&& slowdownSuppressionDistance >= slowdownDistanceThreshold
					&& closeSlowdownDistance < slowdownDistanceThreshold
					&& farZombieSlowdown == false
					&& closeZombieSlowdown
					&& hostileHumanSlowdown;

				ZombieDefOf.Zombie.disabledWorkTags = originalZombieKindDisabledWorkTags | WorkTags.Violent;
				var attackZombieViolentDisabled = attackZombie.WorkTagIsDisabled(WorkTags.Violent);
				var attackVerb = attackZombie.TryGetAttackVerb(attackTarget);
				var attackTargetInjuryBefore = TotalInjurySeverity(attackTarget);
				var attackStarted = attackZombie.TryStartAttack(new LocalTargetInfo(attackTarget));
				var attackTargetInjuryAfter = TotalInjurySeverity(attackTarget);
				var attackCurrentStance = attackZombie.stances?.curStance?.GetType().Name;
				var startAttackCovered = startAttackPatchTargets.Length > 0
					&& attackZombieViolentDisabled
					&& attackVerb != null
					&& attackStarted;

				var locationWeaponDef = DefDatabase<ThingDef>.GetNamed("Weapon_GrenadeFrag", false)
					?? DefDatabase<ThingDef>.GetNamed("Weapon_GrenadeEMP", false)
					?? DefDatabase<ThingDef>.GetNamed("Weapon_GrenadeMolotov", false);
				actor.equipment.DestroyAllEquipment(DestroyMode.Vanish);
				var locationWeapon = locationWeaponDef == null ? null : ThingMaker.MakeThing(locationWeaponDef) as ThingWithComps;
				if (locationWeapon != null)
					actor.equipment.AddEquipment(locationWeapon);
				var equippedLocationVerb = actor.equipment?.PrimaryEq?.PrimaryVerb;
				var primaryTargetsLocations = equippedLocationVerb?.targetParams?.canTargetLocations ?? false;
				var locationVerb = actor.TryGetAttackVerb(electrifier);
				var locationPassThrough = locationWeapon != null
					&& primaryTargetsLocations
					&& locationVerb != null
					&& locationVerb.IsMeleeAttack == false
					&& (locationVerb.targetParams?.canTargetLocations ?? false);

				return new
				{
					success = patchTargets.Length > 0
						&& timeSlowdownCovered
						&& startAttackCovered
						&& normalRanged
						&& electrifierSafe
						&& electrifierActive
						&& spitterBlocked
						&& locationPassThrough,
					destroyedZombies,
					patchTargets,
					slowdownPatchTargets,
					startAttackPatchTargets,
					actor = DescribePawn(actor),
					weaponDef = weapon.def?.defName,
					locationWeaponDef = locationWeaponDef?.defName,
					normal = DescribeZombie(normal),
					closeNormal = DescribeZombie(closeNormal),
					electrifier = DescribeZombie(electrifier),
					attackZombie = DescribeZombie(attackZombie),
					attackTarget = DescribePawn(attackTarget),
					spitter = DescribePawn(spitter),
					hostileHuman = DescribePawn(hostileHuman),
					electrifierActive,
					normalVerb = DescribeVerb(normalVerb),
					electrifierVerb = DescribeVerb(electrifierVerb),
					spitterVerb = DescribeVerb(spitterVerb),
					attackVerb = DescribeVerb(attackVerb),
					locationVerb = DescribeVerb(locationVerb),
					normalRanged,
					electrifierSafe,
					spitterBlocked,
					farZombieSlowdown,
					closeZombieSlowdown,
					hostileHumanSlowdown,
					slowdownSuppressionDistance,
					closeSlowdownDistance,
					humanSlowdownDistance,
					slowdownDistanceThreshold,
					timeSlowdownCovered,
					attackZombieViolentDisabled,
					attackStarted,
					attackCurrentStance,
					attackTargetInjuryBefore,
					attackTargetInjuryAfter,
					attackTargetInjuryDelta = attackTargetInjuryAfter - attackTargetInjuryBefore,
					startAttackCovered,
					primaryTargetsLocations,
					locationPassThrough
				};
			}
			finally
			{
				ZombieDefOf.Zombie.disabledWorkTags = originalZombieKindDisabledWorkTags;
				if (actor != null)
				{
					actor.equipment?.DestroyAllEquipment(DestroyMode.Vanish);
					if (actor.Destroyed == false)
						actor.Destroy(DestroyMode.Vanish);
				}
				if (hostileHuman != null && hostileHuman.Destroyed == false)
					hostileHuman.Destroy(DestroyMode.Vanish);
				if (attackTarget != null && attackTarget.Destroyed == false)
					attackTarget.Destroy(DestroyMode.Vanish);
				DestroyFireFixturePawn(spitter);
				DestroyFireFixturePawn(electrifier);
				DestroyFireFixturePawn(closeNormal);
				DestroyFireFixturePawn(attackZombie);
				DestroyFireFixturePawn(normal);
			}
		}

		[Tool("zombieland/active_electrifier_bullet_absorption_contract", Description = "Verify active electrifiers absorb ordinary bullets while disabled electrifiers still take normal zombie injury.")]
		public static object ActiveElectrifierBulletAbsorptionContract()
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			var destroyedZombies = ZombieRuntimeActions.DestroyZombies(map);
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var activeCell, out var activeSpawnError) == false)
				return activeSpawnError;
			if (TryFindClearSpawnCell(map, activeCell + new IntVec3(4, 0, 0), 10f, out var disabledCell, out var disabledSpawnError) == false)
				return disabledSpawnError;

			var active = ZombieRuntimeActions.SpawnZombie(activeCell, map, ZombieType.Electrifier, true);
			var disabled = ZombieRuntimeActions.SpawnZombie(disabledCell, map, ZombieType.Electrifier, true);
			if (active == null || disabled == null)
			{
				return new
				{
					success = false,
					active = DescribeZombie(active),
					disabled = DescribeZombie(disabled),
					error = "ZombieGenerator.SpawnZombie returned no active or disabled electrifier test zombie."
				};
			}

			NormalizeFireDamagePawn(active);
			NormalizeFireDamagePawn(disabled);
			active.electricDisabledUntil = GenTicks.TicksGame - 1;
			disabled.electricDisabledUntil = GenTicks.TicksGame + GenDate.TicksPerHour;
			active.absorbAttack.Clear();
			disabled.absorbAttack.Clear();

			var activeInjuryBefore = TotalInjurySeverity(active);
			var disabledInjuryBefore = TotalInjurySeverity(disabled);
			var activeAbsorbBefore = active.absorbAttack.Count;
			var disabledAbsorbBefore = disabled.absorbAttack.Count;
			var activeWasElectric = active.IsActiveElectric;
			var disabledWasElectric = disabled.IsActiveElectric;

			var activeDamage = active.TakeDamage(new DamageInfo(DamageDefOf.Bullet, 20f, 0f, -1f, null, null, null, DamageInfo.SourceCategory.ThingOrUnknown, null, true, true));
			var disabledDamage = disabled.TakeDamage(new DamageInfo(DamageDefOf.Bullet, 20f, 0f, -1f, null, null, null, DamageInfo.SourceCategory.ThingOrUnknown, null, true, true));

			var activeInjuryAfter = TotalInjurySeverity(active);
			var disabledInjuryAfter = TotalInjurySeverity(disabled);
			var activeAbsorbAfter = active.absorbAttack.Count;
			var disabledAbsorbAfter = disabled.absorbAttack.Count;
			var activeInjuryDelta = activeInjuryAfter - activeInjuryBefore;
			var disabledInjuryDelta = disabledInjuryAfter - disabledInjuryBefore;
			var activeAbsorbed = activeInjuryDelta <= 0f && activeDamage.totalDamageDealt <= 0f && activeAbsorbAfter > activeAbsorbBefore;
			var disabledDamaged = disabledInjuryDelta > 0f && disabledDamage.totalDamageDealt > 0f && disabledAbsorbAfter == disabledAbsorbBefore;

			return new
			{
				success = activeWasElectric && disabledWasElectric == false && activeAbsorbed && disabledDamaged,
				destroyedZombies,
				active = DescribeZombie(active),
				disabled = DescribeZombie(disabled),
				activeWasElectric,
				disabledWasElectric,
				activeDamageTotal = activeDamage.totalDamageDealt,
				disabledDamageTotal = disabledDamage.totalDamageDealt,
				activeInjuryBefore,
				activeInjuryAfter,
				activeInjuryDelta,
				disabledInjuryBefore,
				disabledInjuryAfter,
				disabledInjuryDelta,
				activeAbsorbBefore,
				activeAbsorbAfter,
				disabledAbsorbBefore,
				disabledAbsorbAfter,
				activeAbsorbed,
				disabledDamaged
			};
		}

		[Tool("zombieland/active_electrifier_melee_shock_contract", Description = "Verify active electrifier melee hides zombie bite and converts a smokepop-belt hit into ElectricalShock.")]
		public static object ActiveElectrifierMeleeShockContract()
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			var destroyedZombies = ZombieRuntimeActions.DestroyZombies(map);
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var activeCell, out var activeSpawnError) == false)
				return activeSpawnError;
			if (TryFindClearSpawnCell(map, activeCell + new IntVec3(2, 0, 0), 8f, out var activeTargetCell, out var activeTargetError) == false)
				return activeTargetError;
			if (TryFindClearSpawnCell(map, activeCell + new IntVec3(5, 0, 0), 10f, out var disabledCell, out var disabledSpawnError) == false)
				return disabledSpawnError;
			if (TryFindClearSpawnCell(map, disabledCell + new IntVec3(2, 0, 0), 8f, out var disabledTargetCell, out var disabledTargetError) == false)
				return disabledTargetError;

			var apparelDef = DefDatabase<ThingDef>.GetNamed("Apparel_SmokepopBelt", false);
			if (apparelDef == null)
			{
				return new
				{
					success = false,
					error = "ThingDef Apparel_SmokepopBelt was not found."
				};
			}

			var active = ZombieRuntimeActions.SpawnZombie(activeCell, map, ZombieType.Electrifier, true);
			var disabled = ZombieRuntimeActions.SpawnZombie(disabledCell, map, ZombieType.Electrifier, true);
			var activeTarget = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var disabledTarget = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(activeTarget, activeTargetCell, map, Rot4.South);
			GenSpawn.Spawn(disabledTarget, disabledTargetCell, map, Rot4.South);
			DisablePawnWork(activeTarget);
			DisablePawnWork(disabledTarget);
			NormalizeFireDamagePawn(activeTarget);
			NormalizeFireDamagePawn(disabledTarget);
			if (active == null || disabled == null)
			{
				return new
				{
					success = false,
					active = DescribeZombie(active),
					disabled = DescribeZombie(disabled),
					error = "ZombieGenerator.SpawnZombie returned no active or disabled electrifier test zombie."
				};
			}
			NormalizeFireDamagePawn(active);
			NormalizeFireDamagePawn(disabled);
			active.electricDisabledUntil = GenTicks.TicksGame - 1;
			disabled.electricDisabledUntil = GenTicks.TicksGame + GenDate.TicksPerHour;

			var activeBelt = ThingMaker.MakeThing(apparelDef) as Apparel;
			var disabledBelt = ThingMaker.MakeThing(apparelDef) as Apparel;
			if (activeBelt == null || disabledBelt == null)
			{
				return new
				{
					success = false,
					error = "Apparel_SmokepopBelt did not create Apparel."
				};
			}
			activeTarget.apparel.Wear(activeBelt, false);
			disabledTarget.apparel.Wear(disabledBelt, false);

			var activeWasElectric = active.IsActiveElectric;
			var disabledWasElectric = disabled.IsActiveElectric;
			var activeAvailableVerbs = active.meleeVerbs.GetUpdatedAvailableVerbsList(false);
			var activeHasZombieBite = activeAvailableVerbs.Any(entry => entry.verb.GetDamageDef() == CustomDefs.ZombieBite);
			var activeAvailableDamageDefs = activeAvailableVerbs.Select(entry => entry.verb.GetDamageDef()?.defName).ToArray();
			var disabledAvailableVerbs = disabled.meleeVerbs.GetUpdatedAvailableVerbsList(false);
			var disabledHasZombieBite = disabledAvailableVerbs.Any(entry => entry.verb.GetDamageDef() == CustomDefs.ZombieBite);
			var disabledAvailableDamageDefs = disabledAvailableVerbs.Select(entry => entry.verb.GetDamageDef()?.defName).ToArray();
			var activeVerb = active.meleeVerbs.TryGetMeleeVerb(activeTarget);
			var disabledVerb = disabled.meleeVerbs.TryGetMeleeVerb(disabledTarget);

			if (TryMeleeDamageInfosToApply(activeVerb, activeTarget, out var activeDamageInfos, out var activeError) == false)
			{
				return new
				{
					success = false,
					active = DescribeZombie(active),
					activeVerb = DescribeVerb(activeVerb),
					error = activeError
				};
			}
			if (TryMeleeDamageInfosToApply(disabledVerb, disabledTarget, out var disabledDamageInfos, out var disabledError) == false)
			{
				return new
				{
					success = false,
					disabled = DescribeZombie(disabled),
					disabledVerb = DescribeVerb(disabledVerb),
					error = disabledError
				};
			}

			var activeElectricalShock = activeDamageInfos.Any(info => info.Def == CustomDefs.ElectricalShock && info.Weapon == CustomDefs.ElectricalField);
			var disabledElectricalShock = disabledDamageInfos.Any(info => info.Def == CustomDefs.ElectricalShock);
			var activeBiteHidden = activeHasZombieBite == false;
			var disabledBiteHidden = disabledHasZombieBite == false;

			return new
			{
				success = activeWasElectric
					&& disabledWasElectric == false
					&& activeBiteHidden
					&& disabledBiteHidden
					&& activeElectricalShock
					&& disabledElectricalShock == false,
				destroyedZombies,
				active = DescribeZombie(active),
				disabled = DescribeZombie(disabled),
				activeTarget = DescribePawn(activeTarget),
				disabledTarget = DescribePawn(disabledTarget),
				activeWasElectric,
				disabledWasElectric,
				activeVerb = DescribeVerb(activeVerb),
				disabledVerb = DescribeVerb(disabledVerb),
				activeAvailableDamageDefs,
				disabledAvailableDamageDefs,
				activeBiteHidden,
				disabledBiteHidden,
				activeElectricalShock,
				disabledElectricalShock,
				activeDamageInfos = DescribeDamageInfos(activeDamageInfos),
				disabledDamageInfos = DescribeDamageInfos(disabledDamageInfos),
				activeBeltDef = activeBelt.def?.defName,
				disabledBeltDef = disabledBelt.def?.defName
			};
		}

		[Tool("zombieland/albino_melee_bite_hidden_contract", Description = "Verify albino zombies hide ZombieBite from real melee verb selection while normal zombies still expose it.")]
		public static object AlbinoMeleeBiteHiddenContract()
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			var destroyedZombies = ZombieRuntimeActions.DestroyZombies(map);
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var albinoCell, out var albinoSpawnError) == false)
				return albinoSpawnError;
			if (TryFindClearSpawnCell(map, albinoCell + new IntVec3(4, 0, 0), 10f, out var normalCell, out var normalSpawnError) == false)
				return normalSpawnError;
			if (TryFindClearSpawnCell(map, albinoCell + new IntVec3(2, 0, 0), 8f, out var targetCell, out var targetSpawnError) == false)
				return targetSpawnError;

			var albino = ZombieRuntimeActions.SpawnZombie(albinoCell, map, ZombieType.Albino, true);
			var normal = ZombieRuntimeActions.SpawnZombie(normalCell, map, ZombieType.Normal, true);
			var target = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(target, targetCell, map, Rot4.South);
			DisablePawnWork(target);
			NormalizeFireDamagePawn(target);
			if (albino == null || normal == null)
			{
				return new
				{
					success = false,
					albino = DescribeZombie(albino),
					normal = DescribeZombie(normal),
					error = "ZombieGenerator.SpawnZombie returned no albino or normal zombie."
				};
			}

			var albinoAvailableVerbs = albino.meleeVerbs.GetUpdatedAvailableVerbsList(false);
			var albinoHasZombieBite = albinoAvailableVerbs.Any(entry => entry.verb.GetDamageDef() == CustomDefs.ZombieBite);
			var albinoAvailableDamageDefs = albinoAvailableVerbs.Select(entry => entry.verb.GetDamageDef()?.defName).ToArray();
			var normalAvailableVerbs = normal.meleeVerbs.GetUpdatedAvailableVerbsList(false);
			var normalHasZombieBite = normalAvailableVerbs.Any(entry => entry.verb.GetDamageDef() == CustomDefs.ZombieBite);
			var normalAvailableDamageDefs = normalAvailableVerbs.Select(entry => entry.verb.GetDamageDef()?.defName).ToArray();
			var albinoVerb = albino.meleeVerbs.TryGetMeleeVerb(target);
			var normalVerb = normal.meleeVerbs.TryGetMeleeVerb(target);
			var albinoBiteHidden = albinoHasZombieBite == false;

			return new
			{
				success = albino?.isAlbino == true && albinoBiteHidden && normalHasZombieBite,
				destroyedZombies,
				albino = DescribeZombie(albino),
				normal = DescribeZombie(normal),
				target = DescribePawn(target),
				albinoVerb = DescribeVerb(albinoVerb),
				normalVerb = DescribeVerb(normalVerb),
				albinoAvailableDamageDefs,
				normalAvailableDamageDefs,
				albinoBiteHidden,
				normalHasZombieBite
			};
		}

		[Tool("zombieland/hostility_to_zombies_contract", Description = "Verify real GenHostility.HostileTo zombie rules for player, hostile, animal, and factionless pawns.")]
		public static object HostilityToZombiesContract()
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			var animalKind = DefDatabase<PawnKindDef>.GetNamed("Muffalo", false);
			var zombieFaction = Find.FactionManager.FirstFactionOfDef(ZombieDefOf.Zombies);
			if (animalKind == null || zombieFaction == null)
			{
				return new
				{
					success = false,
					error = "PawnKindDef Muffalo or zombie faction was not found."
				};
			}

			var destroyedZombies = ZombieRuntimeActions.DestroyZombies(map);
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var zombieCell, out var zombieSpawnError) == false)
				return zombieSpawnError;
			if (TryFindClearSpawnCell(map, zombieCell + new IntVec3(3, 0, 0), 8f, out var playerCell, out var playerSpawnError) == false)
				return playerSpawnError;
			if (TryFindClearSpawnCell(map, zombieCell + new IntVec3(6, 0, 0), 10f, out var hostileCell, out var hostileSpawnError) == false)
				return hostileSpawnError;
			if (TryFindClearSpawnCell(map, zombieCell + new IntVec3(9, 0, 0), 12f, out var factionlessCell, out var factionlessSpawnError) == false)
				return factionlessSpawnError;
			if (TryFindClearSpawnCell(map, zombieCell + new IntVec3(0, 0, 3), 8f, out var animalCell, out var animalSpawnError) == false)
				return animalSpawnError;

			var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
			var player = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var hostile = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfAncientsHostile);
			var factionless = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, null);
			var animal = PawnGenerator.GeneratePawn(animalKind, null);
			GenSpawn.Spawn(player, playerCell, map, Rot4.South);
			GenSpawn.Spawn(hostile, hostileCell, map, Rot4.South);
			GenSpawn.Spawn(factionless, factionlessCell, map, Rot4.South);
			GenSpawn.Spawn(animal, animalCell, map, Rot4.South);
			DisablePawnWork(player);
			DisablePawnWork(hostile);
			DisablePawnWork(factionless);
			if (zombie == null)
			{
				return new
				{
					success = false,
					error = "ZombieGenerator.SpawnZombie returned no normal zombie."
				};
			}

			var settings = ZombieSettings.Values;
			var originalEnemiesAttackZombies = settings.enemiesAttackZombies;
			var originalAnimalsAttackZombies = settings.animalsAttackZombies;
			(bool value, string error) playerThing;
			(bool value, string error) hostileThingDisabled;
			(bool value, string error) hostileThingEnabled;
			(bool value, string error) animalThingDisabled;
			(bool value, string error) animalThingEnabled;
			(bool value, string error) factionlessThing;
			(bool value, string error) factionlessFaction;
			try
			{
				settings.enemiesAttackZombies = false;
				settings.animalsAttackZombies = false;
				playerThing = TryHostileTo(player, zombie);
				hostileThingDisabled = TryHostileTo(hostile, zombie);
				animalThingDisabled = TryHostileTo(animal, zombie);
				factionlessThing = TryHostileTo(factionless, zombie);
				factionlessFaction = TryHostileTo(factionless, zombieFaction);

				settings.enemiesAttackZombies = true;
				settings.animalsAttackZombies = true;
				hostileThingEnabled = TryHostileTo(hostile, zombie);
				animalThingEnabled = TryHostileTo(animal, zombie);
			}
			finally
			{
				settings.enemiesAttackZombies = originalEnemiesAttackZombies;
				settings.animalsAttackZombies = originalAnimalsAttackZombies;
			}

			var noErrors = new[] { playerThing, hostileThingDisabled, hostileThingEnabled, animalThingDisabled, animalThingEnabled, factionlessThing, factionlessFaction }
				.All(sample => sample.error == null);

			return new
			{
				success = noErrors
					&& playerThing.value
					&& hostileThingDisabled.value == false
					&& hostileThingEnabled.value
					&& animalThingDisabled.value == false
					&& animalThingEnabled.value
					&& factionlessThing.value == false
					&& factionlessFaction.value == false,
				destroyedZombies,
				zombie = DescribeZombie(zombie),
				player = DescribePawn(player),
				hostile = DescribePawn(hostile),
				factionless = DescribePawn(factionless),
				animal = DescribePawn(animal),
				zombieFaction = zombieFaction.def?.defName,
				playerThing = DescribeHostility(playerThing),
				hostileThingDisabled = DescribeHostility(hostileThingDisabled),
				hostileThingEnabled = DescribeHostility(hostileThingEnabled),
				animalThingDisabled = DescribeHostility(animalThingDisabled),
				animalThingEnabled = DescribeHostility(animalThingEnabled),
				factionlessThing = DescribeHostility(factionlessThing),
				factionlessFaction = DescribeHostility(factionlessFaction),
				noErrors
			};
		}

		[Tool("zombieland/zombie_faction_world_contract", Description = "Verify the loaded world has exactly one zombie faction and mutual hostility relations.")]
		public static object ZombieFactionWorldContract()
		{
			if (Find.FactionManager == null)
			{
				return new
				{
					success = false,
					error = "No RimWorld faction manager is available."
				};
			}

			var factions = Find.FactionManager.AllFactions.ToArray();
			var zombieFactions = factions
				.Where(faction => faction?.def == ZombieDefOf.Zombies)
				.ToArray();
			var zombieFaction = Find.FactionManager.FirstFactionOfDef(ZombieDefOf.Zombies);
			var playerFaction = Faction.OfPlayer;
			var nonZombieFactions = factions
				.Where(faction => faction != null && faction.def != ZombieDefOf.Zombies)
				.ToArray();
			var relations = zombieFaction == null
				? Array.Empty<object>()
				: nonZombieFactions.Select(faction => new
				{
					faction = faction.def?.defName,
					factionName = faction.Name,
					factionHostileToZombies = faction.HostileTo(zombieFaction),
					zombiesHostileToFaction = zombieFaction.HostileTo(faction),
					factionRelationKind = faction.RelationKindWith(zombieFaction).ToString(),
					zombieRelationKind = zombieFaction.RelationKindWith(faction).ToString()
				}).Cast<object>().ToArray();
			var mutualHostility = zombieFaction != null
				&& nonZombieFactions.All(faction => faction.HostileTo(zombieFaction) && zombieFaction.HostileTo(faction));
			var playerHostility = zombieFaction != null
				&& playerFaction != null
				&& playerFaction.HostileTo(zombieFaction)
				&& zombieFaction.HostileTo(playerFaction);
			var goodwillSuppression = VerifyZombieFactionGoodwillSuppression(zombieFaction, playerFaction, nonZombieFactions);
			var newWorldPrefix = VerifyZombieFactionNewWorldPrefix();

			return new
			{
				success = zombieFactions.Length == 1
					&& zombieFaction != null
					&& playerHostility
					&& mutualHostility
					&& ObjectSuccess(goodwillSuppression)
					&& ObjectSuccess(newWorldPrefix),
				zombieFactionCount = zombieFactions.Length,
				zombieFaction = zombieFaction == null ? null : new
				{
					defName = zombieFaction.def?.defName,
					zombieFaction.Name,
					hidden = zombieFaction.Hidden,
					temporary = zombieFaction.temporary
				},
				playerFaction = playerFaction?.def?.defName,
				playerHostility,
				mutualHostility,
				goodwillSuppression,
				newWorldPrefix,
				factionCount = factions.Length,
				nonZombieFactionCount = nonZombieFactions.Length,
				relations
			};
		}

		static object VerifyZombieFactionNewWorldPrefix()
		{
			var patchOwners = PatchOwners(typeof(FactionGenerator), nameof(FactionGenerator.GenerateFactionsIntoWorldLayer));
			var patchType = typeof(Patches).GetNestedType("FactionGenerator_GenerateFactionsIntoWorldLayer_Patch", BindingFlags.NonPublic);
			var prefix = patchType?.GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic);
			if (prefix == null)
			{
				return new
				{
					success = false,
					patchOwners,
					error = "Could not reflect FactionGenerator_GenerateFactionsIntoWorldLayer_Patch.Prefix."
				};
			}

			var empty = new List<FactionDef>();
			var alreadyPresent = new List<FactionDef> { ZombieDefOf.Zombies };
			prefix.Invoke(null, new object[] { empty });
			prefix.Invoke(null, new object[] { alreadyPresent });
			prefix.Invoke(null, new object[] { null });

			var emptyZombieCount = empty.Count(def => def == ZombieDefOf.Zombies);
			var existingZombieCount = alreadyPresent.Count(def => def == ZombieDefOf.Zombies);
			return new
			{
				success = patchOwners.Contains("net.pardeike.zombieland")
					&& emptyZombieCount == 1
					&& existingZombieCount == 1,
				sourcePath = "FactionGenerator.GenerateFactionsIntoWorldLayer prefix synthetic faction-def list",
				patchOwners,
				emptyList = empty.Select(def => def?.defName).ToArray(),
				emptyZombieCount,
				preexistingList = alreadyPresent.Select(def => def?.defName).ToArray(),
				preexistingZombieCount = existingZombieCount,
				nullListAccepted = true
			};
		}

		static object VerifyZombieFactionGoodwillSuppression(Faction zombieFaction, Faction playerFaction, Faction[] nonZombieFactions)
		{
			if (zombieFaction == null || playerFaction == null)
			{
				return new
				{
					success = false,
					error = "Zombie and player factions are required for the goodwill suppression probe."
				};
			}

			var target = typeof(Faction).GetMethod(nameof(Faction.TryAffectGoodwillWith), new[]
			{
				typeof(Faction),
				typeof(int),
				typeof(bool),
				typeof(bool),
				typeof(HistoryEventDef),
				typeof(GlobalTargetInfo?)
			});
			var patchInfo = target == null ? null : HarmonyLib.Harmony.GetPatchInfo(target);
			var prefix = patchInfo?.Prefixes
				.Select(patch => patch.PatchMethod)
				.FirstOrDefault(method => method.DeclaringType?.Name?.Contains("Faction_TryAffectGoodwillWith_Patch") == true);
			if (target == null || prefix == null)
			{
				return new
				{
					success = false,
					targetFound = target != null,
					prefixFound = prefix != null,
					error = "Could not find the installed Faction.TryAffectGoodwillWith prefix."
				};
			}

			var prefixArgs = new object[] { true, zombieFaction, playerFaction };
			var prefixContinue = (bool)prefix.Invoke(null, prefixArgs);
			var prefixResult = (bool)prefixArgs[0];

			var zombieToPlayerBefore = DescribeFactionRelation(zombieFaction, playerFaction);
			var playerToZombieBefore = DescribeFactionRelation(playerFaction, zombieFaction);
			var zombieCallResult = zombieFaction.TryAffectGoodwillWith(playerFaction, 25, false, false);
			var reverseZombieCallResult = playerFaction.TryAffectGoodwillWith(zombieFaction, 25, false, false);
			var zombieToPlayerAfter = DescribeFactionRelation(zombieFaction, playerFaction);
			var playerToZombieAfter = DescribeFactionRelation(playerFaction, zombieFaction);

			var ordinaryPair = FindOrdinaryGoodwillPair(playerFaction, nonZombieFactions);
			if (ordinaryPair == null)
			{
				return new
				{
					success = false,
					prefix = $"{prefix.DeclaringType?.FullName}.{prefix.Name}",
					prefixContinue,
					prefixResult,
					zombieCallResult,
					reverseZombieCallResult,
					zombieToPlayerBefore,
					zombieToPlayerAfter,
					playerToZombieBefore,
					playerToZombieAfter,
					error = "No ordinary goodwill-capable faction pair was available."
				};
			}

			var ordinaryRelation = ordinaryPair.RelationWith(playerFaction);
			var playerRelation = playerFaction.RelationWith(ordinaryPair);
			var ordinaryBefore = DescribeFactionRelation(ordinaryPair, playerFaction);
			var playerBefore = DescribeFactionRelation(playerFaction, ordinaryPair);
			var ordinaryResult = false;
			object ordinaryAfter;
			object playerAfter;
			try
			{
				ordinaryRelation.baseGoodwill = 0;
				ordinaryRelation.kind = FactionRelationKind.Neutral;
				playerRelation.baseGoodwill = 0;
				playerRelation.kind = FactionRelationKind.Neutral;
				ordinaryResult = ordinaryPair.TryAffectGoodwillWith(playerFaction, 15, false, false);
				ordinaryAfter = DescribeFactionRelation(ordinaryPair, playerFaction);
				playerAfter = DescribeFactionRelation(playerFaction, ordinaryPair);
			}
			finally
			{
				RestoreFactionRelation(ordinaryRelation, ordinaryBefore);
				RestoreFactionRelation(playerRelation, playerBefore);
			}

			return new
			{
				success = prefixContinue == false
					&& prefixResult == false
					&& zombieCallResult == false
					&& reverseZombieCallResult == false
					&& FactionRelationUnchanged(zombieToPlayerBefore, zombieToPlayerAfter)
					&& FactionRelationUnchanged(playerToZombieBefore, playerToZombieAfter)
					&& ordinaryResult
					&& (int)ordinaryAfter.GetType().GetProperty("baseGoodwill")?.GetValue(ordinaryAfter) == 15
					&& (int)playerAfter.GetType().GetProperty("baseGoodwill")?.GetValue(playerAfter) == 15,
				target = $"{target.DeclaringType?.FullName}.{target.Name}",
				prefix = $"{prefix.DeclaringType?.FullName}.{prefix.Name}",
				prefixContinue,
				prefixResult,
				zombie = new
				{
					zombieCallResult,
					reverseZombieCallResult,
					zombieToPlayerBefore,
					zombieToPlayerAfter,
					playerToZombieBefore,
					playerToZombieAfter
				},
				ordinary = new
				{
					faction = ordinaryPair.def?.defName,
					factionName = ordinaryPair.Name,
					ordinaryResult,
					before = ordinaryBefore,
					after = ordinaryAfter,
					playerBefore,
					playerAfter
				}
			};
		}

		static Faction FindOrdinaryGoodwillPair(Faction playerFaction, IEnumerable<Faction> factions)
		{
			return factions
				.Where(faction => faction != null && faction != playerFaction)
				.Where(faction => faction.def != ZombieDefOf.Zombies)
				.Where(faction => faction.HasGoodwill && playerFaction.HasGoodwill)
				.Where(faction => faction.def.permanentEnemy == false && faction.defeated == false)
				.Where(faction => faction.RelationWith(playerFaction, true) != null)
				.Where(faction => playerFaction.RelationWith(faction, true) != null)
				.FirstOrDefault();
		}

		static object DescribeFactionRelation(Faction faction, Faction other)
		{
			var relation = faction.RelationWith(other);
			return new
			{
				faction = faction.def?.defName,
				other = other.def?.defName,
				relation.baseGoodwill,
				kind = relation.kind.ToString()
			};
		}

		static void RestoreFactionRelation(FactionRelation relation, object snapshot)
		{
			relation.baseGoodwill = (int)snapshot.GetType().GetProperty("baseGoodwill").GetValue(snapshot);
			relation.kind = (FactionRelationKind)Enum.Parse(typeof(FactionRelationKind), (string)snapshot.GetType().GetProperty("kind").GetValue(snapshot));
		}

		static bool FactionRelationUnchanged(object before, object after)
		{
			return (int)before.GetType().GetProperty("baseGoodwill").GetValue(before) == (int)after.GetType().GetProperty("baseGoodwill").GetValue(after)
				&& (string)before.GetType().GetProperty("kind").GetValue(before) == (string)after.GetType().GetProperty("kind").GetValue(after);
		}

		static bool CurrentTickingSliceAllLive(TickManager tickManager)
		{
			if (tickManager?.currentZombiesTicking == null)
				return false;
			var count = tickManager.currentZombiesTickingCount;
			if (count < 0 || count > tickManager.currentZombiesTicking.Length)
				return false;
			for (var i = 0; i < count; i++)
			{
				var zombie = tickManager.currentZombiesTicking[i];
				if (zombie == null || zombie.Spawned == false || zombie.Dead)
					return false;
			}
			return true;
		}

		static IntVec3 BudgetContractRoot(Map map)
		{
			var hasViewRect = Find.CurrentMap == map && Find.CameraDriver != null;
			var viewRect = default(CellRect);
			if (hasViewRect)
			{
				viewRect = Find.CameraDriver.CurrentViewRect.ExpandedBy(12);
				viewRect.ClipInsideMap(map);
			}

			var candidates = new[]
			{
				new IntVec3(50, 0, 50),
				new IntVec3(map.Size.x - 60, 0, 50),
				new IntVec3(50, 0, map.Size.z - 60),
				new IntVec3(map.Size.x - 60, 0, map.Size.z - 60),
				map.Center
			};
			for (var i = 0; i < candidates.Length; i++)
			{
				var candidate = candidates[i];
				if (candidate.InBounds(map) == false)
					continue;
				if (hasViewRect && viewRect.Contains(candidate))
					continue;
				if (map.areaManager.Home[candidate])
					continue;
				return candidate;
			}
			return map.Center;
		}

		[Tool("zombieland/zombie_ticking_budget_contract", Description = "Verify reduced zombie ticking counts production ticks while keeping displayed move speed stable and compensating bounded path payment.")]
		public static object ZombieTickingBudgetContract()
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			var tickManager = map.GetComponent<TickManager>();
			if (tickManager == null)
			{
				return new
				{
					success = false,
					error = "No Zombieland tick manager is available on the current map."
				};
			}

			var originalPercents = (float[])ZombieTicker.percentZombiesTicked.Clone();
			var originalPercentIndex = ZombieTicker.percentZombiesTickedIndex;
			var originalZombiesTicked = ZombieTicker.zombiesTicked;
			var originalMaxTicking = ZombieTicker.maxTicking;
			var originalCurrentTicking = ZombieTicker.currentTicking;
			var originalSaturation = ZombieTicker.CaptureSaturation();
			var originalCenterOfInterest = tickManager.centerOfInterest;
			var originalNextCenterOfInterest = tickManager.nextCenterOfInterest;
			var destroyedExisting = ZombieRuntimeActions.DestroyZombies(map);
			var spawned = new List<Zombie>();
			ZombieTicker.ResetSaturation();
			tickManager.ResetRemoteScheduler(resetDiagnostics: true);

			try
			{
				const int targetCount = 20;
				const float targetPercent = 0.25f;
				var root = BudgetContractRoot(map);
				for (var i = 0; i < targetCount; i++)
				{
					var candidateRoot = root + new IntVec3((i % 5) * 3, 0, (i / 5) * 3);
					if (TryFindClearSpawnCell(map, candidateRoot, 16f, out var cell, out var spawnError) == false)
						return spawnError;

					var zombie = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.Normal, true);
					if (zombie == null)
					{
						return new
						{
							success = false,
							destroyedExisting,
							spawnedCount = spawned.Count,
							error = "ZombieGenerator.SpawnZombie returned no normal zombie."
						};
					}
					spawned.Add(zombie);
				}

				tickManager.allZombiesCached.RemoveWhere(zombie => zombie == null || zombie.Destroyed || zombie.Spawned == false || zombie.Dead);
				foreach (var zombie in spawned)
					_ = tickManager.allZombiesCached.Add(zombie);

				var liveCachedBefore = tickManager.allZombiesCached.Count(zombie => zombie.Spawned && zombie.Dead == false);
				FillZombieTickPercent(targetPercent);
				var percentBeforeTicking = ZombieTicker.PercentTicking;
				var expectedTicking = Mathf.FloorToInt(liveCachedBefore * percentBeforeTicking);
				var sample = spawned.FirstOrDefault(zombie => zombie.Spawned && zombie.Dead == false);
				var gridRepopulation = VerifyZombieGridRepopulationAfterReset(map, sample);
				var prioritySample = sample;
				var remoteSample = spawned.LastOrDefault(zombie => zombie != prioritySample && zombie.Spawned && zombie.Dead == false);
				if (prioritySample != null)
					prioritySample.state = ZombieState.Tracking;
				if (remoteSample != null)
					remoteSample.state = ZombieState.Wandering;
				tickManager.centerOfInterest = IntVec3.Invalid;
				tickManager.nextCenterOfInterest = IntVec3.Invalid;
				foreach (var zombie in spawned)
					zombie.simulationTickRate = 1f;
				var priorityNormalSpeed = prioritySample?.GetStatValue(StatDefOf.MoveSpeed) ?? 0f;
				var remoteNormalSpeed = remoteSample?.GetStatValue(StatDefOf.MoveSpeed) ?? 0f;
				var remoteNormalPathPayment = ReadPathPayment(remoteSample);
				ZombieTicker.zombiesTicked = 0;
				tickManager.ZombieTicking();
				var subsetCount = tickManager.currentZombiesTickingCount;
				var subsetCapacity = tickManager.currentZombiesTicking?.Length ?? 0;
				var tickedCount = ZombieTicker.zombiesTicked;
				var allTickedWereLive = CurrentTickingSliceAllLive(tickManager);

				var priorityTickRate = prioritySample?.simulationTickRate ?? 0f;
				var remoteTickRate = remoteSample?.simulationTickRate ?? 0f;
				var priorityThrottledSpeed = prioritySample?.GetStatValue(StatDefOf.MoveSpeed) ?? 0f;
				var remoteThrottledSpeed = remoteSample?.GetStatValue(StatDefOf.MoveSpeed) ?? 0f;
				var remoteThrottledPathPayment = ReadPathPayment(remoteSample);
				var prioritySpeedRatio = priorityNormalSpeed == 0f ? 0f : priorityThrottledSpeed / priorityNormalSpeed;
				var remoteSpeedRatio = remoteNormalSpeed == 0f ? 0f : remoteThrottledSpeed / remoteNormalSpeed;
				var remotePathPaymentRatio = remoteNormalPathPayment.GetValueOrDefault() == 0f
					? 0f
					: remoteThrottledPathPayment.GetValueOrDefault() / remoteNormalPathPayment.Value;
				var expectedPathPaymentRatio = ZombieTicker.MovementPaymentMultiplier(remoteTickRate);
				var prioritySpeedStable = priorityTickRate > 0.99f && Mathf.Abs(prioritySpeedRatio - 1f) < 0.05f;
				var remoteSpeedStable = remoteTickRate > 0f && remoteTickRate < 1f && Mathf.Abs(remoteSpeedRatio - 1f) < 0.05f;
				var remotePathPaymentCompensated = remoteNormalPathPayment.HasValue
					&& remoteThrottledPathPayment.HasValue
					&& Mathf.Abs(remotePathPaymentRatio - expectedPathPaymentRatio) < 0.05f;
				var awakeCapacity = VerifyZombieAwakeCapacity(map, sample, root + new IntVec3(-8, 0, -8));
				var makeDowned = VerifyMakeDownedPatch(map, root + new IntVec3(-16, 0, -8));
				var killedCleanup = VerifyRemoveComponentsOnKilledPatch(map, root + new IntVec3(-24, 0, -8));
				var idleState = VerifyNothingHappeningSpawnGate();
				var gunshotPheromones = VerifyGunshotPheromoneBump(map, root + new IntVec3(-18, 0, 10));
				var collisionSuppression = VerifyZombieCollisionSuppression(map, sample);

				return new
				{
					success = spawned.Count == targetCount
						&& liveCachedBefore == targetCount
						&& expectedTicking > 0
						&& expectedTicking < liveCachedBefore
						&& subsetCount == expectedTicking
						&& tickedCount == subsetCount
						&& allTickedWereLive
						&& prioritySpeedStable
						&& remoteSpeedStable
						&& remotePathPaymentCompensated
						&& ObjectSuccess(awakeCapacity)
						&& ObjectSuccess(makeDowned)
						&& ObjectSuccess(killedCleanup)
						&& ObjectSuccess(gunshotPheromones)
						&& ObjectSuccess(gridRepopulation)
						&& ObjectSuccess(collisionSuppression),
					destroyedExisting,
					spawnedCount = spawned.Count,
					liveCachedBefore,
					targetPercent,
					percentBeforeTicking,
					expectedTicking,
					subsetCount,
					subsetCapacity,
					tickedCount,
					allTickedWereLive,
					priorityTickRate,
					remoteTickRate,
					priorityNormalSpeed,
					priorityThrottledSpeed,
					prioritySpeedRatio,
					prioritySpeedStable,
					remoteNormalSpeed,
					remoteThrottledSpeed,
					remoteSpeedRatio,
					remoteSpeedStable,
					remoteNormalPathPayment,
					remoteThrottledPathPayment,
					remotePathPaymentRatio,
					expectedPathPaymentRatio,
					remotePathPaymentCompensated,
					awakeCapacity,
					makeDowned,
					killedCleanup,
					idleState,
					gunshotPheromones,
					gridRepopulation,
					collisionSuppression,
					sample = DescribeZombie(sample)
				};
			}
			finally
			{
				ZombieRuntimeActions.DestroyZombies(map);
				tickManager.centerOfInterest = originalCenterOfInterest;
				tickManager.nextCenterOfInterest = originalNextCenterOfInterest;
				ZombieTicker.percentZombiesTicked = originalPercents;
				ZombieTicker.percentZombiesTickedIndex = originalPercentIndex;
				ZombieTicker.zombiesTicked = originalZombiesTicked;
				ZombieTicker.maxTicking = originalMaxTicking;
				ZombieTicker.currentTicking = originalCurrentTicking;
				ZombieTicker.RestoreSaturation(originalSaturation);
				tickManager.ClearZombieTickingBuffers();
				tickManager.ResetRemoteScheduler(resetDiagnostics: true);
			}
		}

		static float? ReadPathPayment(Zombie zombie)
		{
			var method = AccessTools.Method(typeof(Pawn_PathFollower), "CostToPayThisTick");
			if (zombie?.pather == null || method == null)
				return null;
			return Convert.ToSingle(method.Invoke(zombie.pather, null));
		}

		[Tool("zombieland/zombie_ticking_scheduler_contract", Description = "Verify emergency-floor fairness, immediate priority service, queue churn recovery, and RNG isolation for the production zombie scheduler.")]
		public static object ZombieTickingSchedulerContract(
			[ToolParameter(Description = "Scheduler preparations to observe; 240 proves two complete 120-tick emergency-floor rounds for 25 zombies.", Required = false, DefaultValue = 240)] int preparations = 240)
		{
			var map = CurrentMap;
			var tickManager = map?.GetComponent<TickManager>();
			if (map == null || tickManager == null)
				return new { success = false, error = "No current map with a Zombieland tick manager is loaded." };

			preparations = Mathf.Clamp(preparations, 240, 1200);
			var originalPercents = (float[])ZombieTicker.percentZombiesTicked.Clone();
			var originalPercentIndex = ZombieTicker.percentZombiesTickedIndex;
			var originalSaturation = ZombieTicker.CaptureSaturation();
			var originalCenterOfInterest = tickManager.centerOfInterest;
			var originalNextCenterOfInterest = tickManager.nextCenterOfInterest;
			var destroyedExisting = ZombieRuntimeActions.DestroyZombies(map);
			var spawned = new List<Zombie>();

			try
			{
				const int targetCount = 25;
				var root = BudgetContractRoot(map);
				for (var i = 0; i < targetCount; i++)
				{
					var candidateRoot = root + new IntVec3((i % 5) * 3, 0, (i / 5) * 3);
					if (TryFindClearSpawnCell(map, candidateRoot, 16f, out var cell, out var spawnError) == false)
						return spawnError;
					var zombie = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.Normal, true);
					if (zombie == null)
						return new { success = false, destroyedExisting, spawnedCount = spawned.Count, error = "Could not spawn the scheduler fixture." };
					zombie.state = ZombieState.Wandering;
					zombie.raging = 0;
					spawned.Add(zombie);
				}

				tickManager.allZombiesCached.RemoveWhere(zombie => zombie == null || zombie.Destroyed || zombie.Spawned == false || zombie.Dead);
				foreach (var zombie in spawned)
					_ = tickManager.allZombiesCached.Add(zombie);
				tickManager.centerOfInterest = IntVec3.Invalid;
				tickManager.nextCenterOfInterest = IntVec3.Invalid;
				tickManager.ResetRemoteScheduler(resetDiagnostics: true);
				FillZombieTickPercent(0f);
				ZombieTicker.ResetSaturation();
				ZombieTicker.saturationState = ZombieSaturationState.Emergency;
				ZombieTicker.saturationSeverity = 1f;
				ZombieTicker.simulationSaturationSeverity = 1f;

				var selectionCounts = spawned.ToDictionary(zombie => ZombieRuntimeActions.StableThingId(zombie), _ => 0);
				var lastSelection = spawned.ToDictionary(zombie => ZombieRuntimeActions.StableThingId(zombie), _ => 0);
				var maxSilence = spawned.ToDictionary(zombie => ZombieRuntimeActions.StableThingId(zombie), _ => 0);
				var remoteClassificationStable = true;
				for (var preparation = 1; preparation <= preparations; preparation++)
				{
					TickManager.PrepareThreadedTicking(tickManager);
					remoteClassificationStable &= tickManager.lastZombieTickingPriorityCount == 0
						&& tickManager.lastZombieTickingRemoteCount == targetCount;
					for (var i = 0; i < tickManager.currentZombiesTickingCount; i++)
					{
						var id = ZombieRuntimeActions.StableThingId(tickManager.currentZombiesTicking[i]);
						if (selectionCounts.ContainsKey(id) == false)
							continue;
						maxSilence[id] = Math.Max(maxSilence[id], preparation - lastSelection[id]);
						lastSelection[id] = preparation;
						selectionCounts[id]++;
					}
				}
				foreach (var id in selectionCounts.Keys.ToArray())
					maxSilence[id] = Math.Max(maxSilence[id], preparations - lastSelection[id]);

				var expectedSelections = Mathf.FloorToInt(targetCount * ZombieTicker.EmergencyRemoteTickFloor * preparations + 0.0001f);
				var minSelections = selectionCounts.Values.Min();
				var maxSelections = selectionCounts.Values.Max();
				var maxSilenceTicks = maxSilence.Values.Max();
				var fairnessSuccess = remoteClassificationStable
					&& selectionCounts.Values.Sum() == expectedSelections
					&& minSelections > 0
					&& maxSelections - minSelections <= 1
					&& maxSilenceTicks <= 121;

				var priority = spawned[0];
				priority.state = ZombieState.Tracking;
				var priorityAlwaysSelected = true;
				for (var i = 0; i < 20; i++)
				{
					TickManager.PrepareThreadedTicking(tickManager);
					priorityAlwaysSelected &= tickManager.currentZombiesTicking.Take(tickManager.currentZombiesTickingCount).Contains(priority)
						&& tickManager.lastZombieTickingPriorityCount == 1;
				}

				const int rngSeed = 19790427;
				Rand.PushState(rngSeed);
				var controlFirst = Rand.Value;
				var controlSecond = Rand.Value;
				Rand.PopState();
				Rand.PushState(rngSeed);
				var observedFirst = Rand.Value;
				TickManager.PrepareThreadedTicking(tickManager);
				var observedSecond = Rand.Value;
				Rand.PopState();
				var rngIsolated = Mathf.Approximately(controlFirst, observedFirst) && Mathf.Approximately(controlSecond, observedSecond);

				var staleBeforeChurn = tickManager.remoteQueueStaleDiscards;
				var removed = spawned.Take(5).ToArray();
				var replacementCells = removed.Select(zombie => zombie.Position).ToArray();
				foreach (var zombie in removed)
					if (zombie.Destroyed == false)
						zombie.Destroy(DestroyMode.Vanish);
				spawned.RemoveAll(zombie => removed.Contains(zombie));
				for (var i = 0; i < replacementCells.Length; i++)
				{
					var replacement = ZombieRuntimeActions.SpawnZombie(replacementCells[i], map, ZombieType.Normal, true);
					if (replacement == null)
						return new { success = false, error = "Could not spawn a churn replacement.", replacementIndex = i };
					replacement.state = ZombieState.Wandering;
					replacement.raging = 0;
					spawned.Add(replacement);
					_ = tickManager.allZombiesCached.Add(replacement);
				}
				foreach (var zombie in spawned)
				{
					zombie.state = ZombieState.Wandering;
					zombie.raging = 0;
				}

				var churnCounts = spawned.ToDictionary(zombie => ZombieRuntimeActions.StableThingId(zombie), _ => 0);
				for (var preparation = 0; preparation < 240; preparation++)
				{
					TickManager.PrepareThreadedTicking(tickManager);
					for (var i = 0; i < tickManager.currentZombiesTickingCount; i++)
					{
						var id = ZombieRuntimeActions.StableThingId(tickManager.currentZombiesTicking[i]);
						if (churnCounts.ContainsKey(id))
							churnCounts[id]++;
					}
				}
				var churnMin = churnCounts.Values.Min();
				var churnMax = churnCounts.Values.Max();
				var churnSuccess = churnMin > 0
					&& churnMax - churnMin <= 1
					&& tickManager.remoteQueueStaleDiscards >= staleBeforeChurn + removed.Length
					&& tickManager.RemoteSchedulerQueueCount <= spawned.Count + 32;

				return new
				{
					success = fairnessSuccess && priorityAlwaysSelected && rngIsolated && churnSuccess,
					destroyedExisting,
					fixtureCount = spawned.Count,
					preparations,
					emergencyFloor = ZombieTicker.EmergencyRemoteTickFloor,
					expectedSelections,
					actualSelections = selectionCounts.Values.Sum(),
					minSelections,
					maxSelections,
					maxSilenceTicks,
					remoteClassificationStable,
					fairnessSuccess,
					priorityAlwaysSelected,
					rngIsolated,
					churn = new
					{
						removed = removed.Length,
						churnMin,
						churnMax,
						staleDiscards = tickManager.remoteQueueStaleDiscards - staleBeforeChurn,
						queueCount = tickManager.RemoteSchedulerQueueCount,
						queueCompactions = tickManager.remoteQueueCompactions,
						churnSuccess
					}
				};
			}
			finally
			{
				ZombieRuntimeActions.DestroyZombies(map);
				ZombieTicker.percentZombiesTicked = originalPercents;
				ZombieTicker.percentZombiesTickedIndex = originalPercentIndex;
				ZombieTicker.RestoreSaturation(originalSaturation);
				tickManager.centerOfInterest = originalCenterOfInterest;
				tickManager.nextCenterOfInterest = originalNextCenterOfInterest;
				tickManager.ClearZombieTickingBuffers();
				tickManager.ResetRemoteScheduler(resetDiagnostics: true);
			}
		}

		static object VerifyZombieGridRepopulationAfterReset(Map map, Zombie zombie)
		{
			if (zombie == null)
			{
				return new
				{
					success = false,
					error = "No live zombie sample was available for grid repopulation verification."
				};
			}

			var destination = IntVec3.Invalid;
			foreach (var offset in GenAdj.AdjacentCells)
			{
				var cell = zombie.Position + offset;
				if (cell.InBounds(map) && zombie.HasValidDestination(cell))
				{
					destination = cell;
					break;
				}
			}

			if (destination.IsValid == false)
			{
				return new
				{
					success = false,
					error = "No valid adjacent destination was available for grid repopulation verification.",
					position = ZombieRuntimeActions.DescribeCell(zombie.Position)
				};
			}

			var grid = map.GetGrid();
			ClearZombieCount(grid, zombie.Position);
			ClearZombieCount(grid, destination);
			if (zombie.lastGotoPosition.IsValid)
				ClearZombieCount(grid, zombie.lastGotoPosition);

			zombie.pather?.StopDead();
			zombie.lastGotoPosition = IntVec3.Invalid;
			var beforeAtPosition = grid.GetZombieCount(zombie.Position);
			var beforeAtDestination = grid.GetZombieCount(destination);
			var beforeLastGotoValid = zombie.lastGotoPosition.IsValid;

			zombie.jobs.StartJob(JobMaker.MakeJob(CustomDefs.Stumble), JobCondition.InterruptForced, null, true, false, null, null);
			if (zombie.jobs.curDriver is not JobDriver_Stumble stumbleDriver)
			{
				return new
				{
					success = false,
					error = "The sample zombie did not start a Stumble job.",
					currentJob = zombie.CurJobDef?.defName
				};
			}

			stumbleDriver.destination = destination;
			stumbleDriver.ExecuteMove(zombie, grid);

			var afterAtPosition = grid.GetZombieCount(zombie.Position);
			var afterAtDestination = grid.GetZombieCount(destination);
			var afterLastGoto = zombie.lastGotoPosition;
			var moving = zombie.pather?.Moving ?? false;
			var pathDestination = moving ? zombie.pather.Destination.Cell : IntVec3.Invalid;

			return new
			{
				success = beforeAtPosition == 0
					&& beforeAtDestination == 0
					&& beforeLastGotoValid == false
					&& afterAtPosition == 0
					&& afterAtDestination == 1
					&& afterLastGoto == destination
					&& moving
					&& pathDestination == destination,
				position = ZombieRuntimeActions.DescribeCell(zombie.Position),
				destination = ZombieRuntimeActions.DescribeCell(destination),
				beforeAtPosition,
				beforeAtDestination,
				beforeLastGotoValid,
				afterAtPosition,
				afterAtDestination,
				afterLastGoto = ZombieRuntimeActions.DescribeCell(afterLastGoto),
				moving,
				pathDestination = pathDestination.IsValid ? ZombieRuntimeActions.DescribeCell(pathDestination) : null,
				currentJob = zombie.CurJobDef?.defName
			};
		}

		static void ClearZombieCount(PheromoneGrid grid, IntVec3 cell)
		{
			if (cell.IsValid == false)
				return;

			var current = grid.GetZombieCount(cell);
			if (current != 0)
				grid.ChangeZombieCount(cell, -current);
		}

		static object VerifyNothingHappeningSpawnGate()
		{
			var originalSpawning = ZombieGenerator.ZombiesSpawning;
			try
			{
				ZombieGenerator.ZombiesSpawning = 0;
				if (TryNothingHappeningInGame(out var noSpawnNothingHappening, out var noSpawnError) == false)
				{
					return new
					{
						success = false,
						error = noSpawnError
					};
				}

				ZombieGenerator.ZombiesSpawning = 1;
				if (TryNothingHappeningInGame(out var spawningNothingHappening, out var spawnError) == false)
				{
					return new
					{
						success = false,
						error = spawnError
					};
				}

				return new
				{
					success = noSpawnNothingHappening && spawningNothingHappening == false,
					noSpawnNothingHappening,
					spawningNothingHappening,
					originalSpawning
				};
			}
			finally
			{
				ZombieGenerator.ZombiesSpawning = originalSpawning;
			}
		}

		static object VerifyGunshotPheromoneBump(Map map, IntVec3 root)
		{
			var spawnedThings = new List<Thing>();
			var originalInstinct = ZombieSettings.Values.zombieInstinct;
			try
			{
				if (TryFindClearSpawnCell(map, root, 16f, out var shooterCell, out var shooterError) == false)
					return shooterError;
				var targetCell = GenRadial.RadialCellsAround(shooterCell, 12f, false)
					.Where(cell => cell.InBounds(map))
					.Where(cell => cell.Standable(map))
					.Where(cell => cell.Fogged(map) == false)
					.Where(cell => cell.DistanceTo(shooterCell) >= 6f)
					.Where(cell => GenSight.LineOfSight(shooterCell, cell, map, true))
					.OrderBy(cell => cell.DistanceToSquared(shooterCell))
					.FirstOrDefault();
				if (targetCell.IsValid == false)
				{
					return new
					{
						success = false,
						shooterCell = ZombieRuntimeActions.DescribeCell(shooterCell),
						error = "No line-of-sight target cell was found for the gunshot pheromone fixture."
					};
				}
				if (TryFindClearSpawnCell(map, shooterCell + new IntVec3(0, 0, 3), 10f, out var spitterCell, out var spitterError) == false)
					return spitterError;

				ZombieSettings.Values.zombieInstinct = ZombieInstinct.Normal;
				var shooter = SpawnArmedAreaWorkflowPawn(map, "ZL_Core_GunshotPheromoneShooter", shooterCell, Faction.OfPlayer, spawnedThings);
				var projectileDef = shooter?.equipment?.PrimaryEq?.PrimaryVerb?.verbProps?.defaultProjectile;
				if (shooter == null || projectileDef == null)
				{
					return new
					{
						success = false,
						shooter = DescribePawn(shooter),
						error = "Could not create an armed pawn with a default projectile."
					};
				}

				const float radius = 16f;
				ClearPheromones(map, shooterCell, radius);
				var beforeHumanShot = SnapshotPheromones(map, shooterCell, radius);
				var humanProjectile = (Projectile)GenSpawn.Spawn(ThingMaker.MakeThing(projectileDef), shooterCell, map, WipeMode.Vanish);
				spawnedThings.Add(humanProjectile);
				humanProjectile.Launch(shooter, shooter.DrawPos, targetCell, targetCell, ProjectileHitFlags.IntendedTarget, false, shooter.equipment.Primary);
				var humanChange = DescribePheromoneChange(map, beforeHumanShot, out var humanChangedCount);

				var spitter = PawnGenerator.GeneratePawn(ZombieDefOf.ZombieSpitter, Find.FactionManager.FirstFactionOfDef(ZombieDefOf.Zombies)) as ZombieSpitter;
				GenSpawn.Spawn(spitter, spitterCell, map, Rot4.South, WipeMode.Vanish, false);
				spawnedThings.Add(spitter);
				ClearPheromones(map, spitterCell, radius);
				var beforeSpitterShot = SnapshotPheromones(map, spitterCell, radius);
				var spitterProjectile = (Projectile)GenSpawn.Spawn(ThingMaker.MakeThing(projectileDef), spitterCell, map, WipeMode.Vanish);
				spawnedThings.Add(spitterProjectile);
				spitterProjectile.Launch(spitter, spitter.DrawPos, targetCell, targetCell, ProjectileHitFlags.IntendedTarget, false, null);
				var spitterChange = DescribePheromoneChange(map, beforeSpitterShot, out var spitterChangedCount);

				return new
				{
					success = humanChangedCount > 0 && spitterChangedCount == 0,
					shooter = DescribePawn(shooter),
					spitter = DescribeZombie(spitter),
					projectileDef = projectileDef.defName,
					shooterCell = ZombieRuntimeActions.DescribeCell(shooterCell),
					spitterCell = ZombieRuntimeActions.DescribeCell(spitterCell),
					targetCell = ZombieRuntimeActions.DescribeCell(targetCell),
					humanChangedCount,
					spitterChangedCount,
					humanChange,
					spitterChange
				};
			}
			finally
			{
				ZombieSettings.Values.zombieInstinct = originalInstinct;
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}
		}

		static object VerifyZombieCollisionSuppression(Map map, Zombie zombie)
		{
			if (zombie == null)
			{
				return new
				{
					success = false,
					error = "No sample zombie was available for collision suppression."
				};
			}

			var probeCell = zombie.Position + IntVec3.East;
			if (probeCell.InBounds(map) == false)
				probeCell = zombie.Position;
			if (TryWillCollideWithPawnAt(zombie, probeCell, out var zombieWillCollide, out var collideError) == false)
			{
				return new
				{
					success = false,
					zombie = DescribeZombie(zombie),
					error = collideError
				};
			}
			var collisionOffset = PawnCollisionTweenerUtility.PawnCollisionPosOffsetFor(zombie);
			return new
			{
				success = zombieWillCollide == false && collisionOffset == Vector3.zero,
				zombie = DescribeZombie(zombie),
				probeCell = ZombieRuntimeActions.DescribeCell(probeCell),
				zombieWillCollide,
				collisionOffset = new
				{
					collisionOffset.x,
					collisionOffset.y,
					collisionOffset.z
				}
			};
		}

		static object VerifyZombieAwakeCapacity(Map map, Zombie zombie, IntVec3 humanRoot)
		{
			if (zombie == null)
			{
				return new
				{
					success = false,
					error = "No sample zombie was available for the awake-capacity probe."
				};
			}

			Pawn human = null;
			try
			{
				if (TryFindClearSpawnCell(map, humanRoot, 16f, out var humanCell, out var spawnError) == false)
					return spawnError;

				human = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(human, humanCell, map, WipeMode.Vanish);
				DisablePawnWork(human);

				ApplyAnestheticCapacitySuppressor(zombie);
				ApplyAnestheticCapacitySuppressor(human);
				var zombieCase = DescribeAwakeCapacityCase("zombie", zombie);
				var humanCase = DescribeAwakeCapacityCase("humanControl", human);
				var zombieConsciousness = zombie.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness);
				var humanConsciousness = human.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness);

				return new
				{
					success = zombieConsciousness < 0.3f
						&& zombie.health.capacities.CanBeAwake
						&& humanConsciousness < 0.3f
						&& human.health.capacities.CanBeAwake == false,
					zombie = zombieCase,
					humanControl = humanCase
				};
			}
			finally
			{
				if (human != null && human.Destroyed == false)
					human.Destroy(DestroyMode.Vanish);
			}
		}

		static object VerifyMakeDownedPatch(Map map, IntVec3 root)
		{
			var makeDowned = AccessTools.Method(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.MakeDowned));
			if (makeDowned == null)
			{
				return new
				{
					success = false,
					error = "Could not reflect Pawn_HealthTracker.MakeDowned."
				};
			}

			var pawns = new List<Pawn>();
			var oldKillCircleMultiplier = Constants.KILL_CIRCLE_RADIUS_MULTIPLIER;
			try
			{
				if (TryFindClearSpawnCell(map, root, 18f, out var humanCell, out var humanError) == false)
					return humanError;
				if (TryFindClearSpawnCell(map, humanCell + new IntVec3(4, 0, 0), 10f, out var spitterCell, out var spitterError) == false)
					return spitterError;
				if (TryFindClearSpawnCell(map, humanCell + new IntVec3(8, 0, 0), 10f, out var symbiantCell, out var symbiantError) == false)
					return symbiantError;

				var human = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(human, humanCell, map, WipeMode.Vanish);
				DisablePawnWork(human);
				pawns.Add(human);

				var spitter = SpawnMakeDownedSpitter(map, spitterCell);
				var symbiant = SpawnMakeDownedSymbiant(map, symbiantCell);
				if (spitter == null || symbiant == null)
				{
					return new
					{
						success = false,
						error = "Could not spawn both spitter and symbiant for MakeDowned probe.",
						spitterSpawned = spitter != null,
						symbiantSpawned = symbiant != null
					};
				}
				pawns.Add(spitter);
				pawns.Add(symbiant);

				Constants.KILL_CIRCLE_RADIUS_MULTIPLIER = 2f;
				var grid = map.GetGrid();
				var timestamp = Tools.Ticks();
				var radius = Tools.RadiusForPawn(human) * Constants.KILL_CIRCLE_RADIUS_MULTIPLIER;
				radius /= ZombieSettings.Values.zombieInstinct.HalfToDoubleValue();
				var seededCells = GenRadial.RadialCellsAround(human.Position, radius, true)
					.Where(cell => cell.InBounds(map))
					.ToArray();
				foreach (var cell in seededCells)
					grid.SetTimestamp(cell, timestamp - 1);
				grid.SetTimestamp(human.Position, timestamp);
				var seededBefore = seededCells.Count(cell => grid.GetTimestamp(cell) > 0);

				InvokeMakeDowned(makeDowned, human);
				InvokeMakeDowned(makeDowned, spitter);
				InvokeMakeDowned(makeDowned, symbiant);

				var unclearedAfter = seededCells.Count(cell => grid.GetTimestamp(cell) > 0);
				var humanDowned = human.health.Downed;
				var spitterDowned = spitter.health.Downed;
				var symbiantDowned = symbiant.health.Downed;

				return new
				{
					success = seededBefore > 0
						&& unclearedAfter == 0
						&& humanDowned
						&& spitterDowned == false
						&& symbiantDowned == false,
					seededBefore,
					unclearedAfter,
					radius,
					human = DescribePawn(human),
					spitter = DescribeZombie(spitter),
					symbiant = DescribeZombie(symbiant),
					humanHealthDowned = humanDowned,
					spitterHealthDowned = spitterDowned,
					symbiantHealthDowned = symbiantDowned
				};
			}
			finally
			{
				Constants.KILL_CIRCLE_RADIUS_MULTIPLIER = oldKillCircleMultiplier;
				foreach (var pawn in pawns)
					if (pawn != null && pawn.Destroyed == false)
						pawn.Destroy(DestroyMode.Vanish);
			}
		}

		static object VerifyRemoveComponentsOnKilledPatch(Map map, IntVec3 root)
		{
			var patchTargets = PatchedMethodsForPatchClass("PawnComponentsUtility_RemoveComponentsOnKilled_Patch");
			var oldKillCircleMultiplier = Constants.KILL_CIRCLE_RADIUS_MULTIPLIER;
			Pawn human = null;
			Corpse corpse = null;
			try
			{
				if (TryFindClearSpawnCell(map, root, 18f, out var humanCell, out var humanError) == false)
					return humanError;

				human = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(human, humanCell, map, WipeMode.Vanish);
				DisablePawnWork(human);

				Constants.KILL_CIRCLE_RADIUS_MULTIPLIER = 2f;
				var grid = map.GetGrid();
				var timestamp = Tools.Ticks();
				var radius = Tools.RadiusForPawn(human) * Constants.KILL_CIRCLE_RADIUS_MULTIPLIER;
				radius /= ZombieSettings.Values.zombieInstinct.HalfToDoubleValue();
				var seededCells = GenRadial.RadialCellsAround(human.Position, radius, true)
					.Where(cell => cell.InBounds(map))
					.ToArray();
				foreach (var cell in seededCells)
					grid.SetTimestamp(cell, timestamp - 1);
				grid.SetTimestamp(human.Position, timestamp);
				var seededBefore = seededCells.Count(cell => grid.GetTimestamp(cell) > 0);

				var positionBeforeKill = human.Position;
				human.Kill(null);
				corpse = human.Corpse;

				var unclearedAfter = seededCells.Count(cell => grid.GetTimestamp(cell) > 0);
				return new
				{
					success = patchTargets.Length > 0
						&& seededBefore > 0
						&& human.Dead
						&& human.Destroyed
						&& unclearedAfter == 0,
					patchTargets,
					seededBefore,
					unclearedAfter,
					radius,
					pawn = new
					{
						thingId = human.ThingID,
						defName = human.def?.defName,
						kindDef = human.kindDef?.defName,
						dead = human.Dead,
						destroyed = human.Destroyed,
						spawned = human.Spawned,
						positionBeforeKill = ZombieRuntimeActions.DescribeCell(positionBeforeKill),
						mapAfterKillIsNull = human.Map == null,
						mapHeldAfterKillIsNull = human.MapHeld == null,
						carryTrackerRemoved = human.carryTracker == null,
						needsRemoved = human.needs == null,
						jobsRemoved = human.jobs == null,
						stancesRemoved = human.stances == null
					},
					corpse = corpse == null ? null : new
					{
						id = ZombieRuntimeActions.StableThingId(corpse),
						defName = corpse.def?.defName,
						spawned = corpse.Spawned,
						position = ZombieRuntimeActions.DescribeCell(corpse.Position)
					}
				};
			}
			finally
			{
				Constants.KILL_CIRCLE_RADIUS_MULTIPLIER = oldKillCircleMultiplier;
				if (corpse != null && corpse.Destroyed == false)
					corpse.Destroy(DestroyMode.Vanish);
				if (human != null && human.Destroyed == false)
					human.Destroy(DestroyMode.Vanish);
			}
		}

		static void InvokeMakeDowned(MethodInfo makeDowned, Pawn pawn)
		{
			makeDowned.Invoke(pawn.health, new object[makeDowned.GetParameters().Length]);
		}

		static ZombieSpitter SpawnMakeDownedSpitter(Map map, IntVec3 cell)
		{
			var existing = CurrentZombies(map).OfType<ZombieSpitter>().Select(StableId).ToHashSet();
			ZombieSpitter.Spawn(map, cell);
			var spitter = CurrentZombies(map).OfType<ZombieSpitter>()
				.FirstOrDefault(candidate => existing.Contains(StableId(candidate)) == false)
				?? CurrentZombies(map).OfType<ZombieSpitter>().OrderBy(candidate => candidate.Position.DistanceToSquared(cell)).FirstOrDefault();
			if (spitter != null)
				spitter.state = SpitterState.Idle;
			return spitter;
		}

		static ZombieSymbiant SpawnMakeDownedSymbiant(Map map, IntVec3 cell)
		{
			var existing = CurrentZombies(map).OfType<ZombieSymbiant>().Select(StableId).ToHashSet();
			ZombieSymbiant.Spawn(map, cell);
			return CurrentZombies(map).OfType<ZombieSymbiant>()
				.FirstOrDefault(candidate => existing.Contains(StableId(candidate)) == false)
				?? CurrentZombies(map).OfType<ZombieSymbiant>().OrderBy(candidate => candidate.Position.DistanceToSquared(cell)).FirstOrDefault();
		}

		static void ApplyAnestheticCapacitySuppressor(Pawn pawn)
		{
			var anesthetic = HediffMaker.MakeHediff(HediffDefOf.Anesthetic, pawn);
			anesthetic.Severity = 1f;
			pawn.health.hediffSet.AddDirect(anesthetic);
		}

		static object DescribeAwakeCapacityCase(string label, Pawn pawn)
		{
			return new
			{
				label,
				pawn = DescribePawn(pawn),
				consciousness = pawn.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness),
				canBeAwake = pawn.health.capacities.CanBeAwake,
				raceAlwaysAwake = pawn.RaceProps.alwaysAwake,
				deactivated = pawn.IsDeactivated(),
				healthDowned = pawn.health.Downed,
				publicDowned = pawn.Downed
			};
		}

		[Tool("zombieland/zombie_ticking_feedback_contract", Description = "Verify speed-normalized demand, completion feedback, saturation hysteresis, and the live TickManagerUpdate patch boundary.")]
		public static object ZombieTickingFeedbackContract()
		{
			var map = CurrentMap;
			var tickManager = map?.GetComponent<TickManager>();
			var gameTickManager = Find.TickManager;
			if (map == null || tickManager == null || gameTickManager == null)
				return new { success = false, error = "No current map with both tick managers is loaded." };

			var patch = typeof(Patches).GetNestedType("Verse_TickManager_TickManagerUpdate_Patch", BindingFlags.NonPublic);
			var prefix = patch?.GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic);
			var postfix = patch?.GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic);
			if (prefix == null || postfix == null)
				return new { success = false, error = "Could not find TickManagerUpdate prefix/postfix by reflection.", prefixFound = prefix != null, postfixFound = postfix != null };

			var originalTimeSpeed = gameTickManager.CurTimeSpeed;
			var originalRealTimeToTickThrough = gameTickManager.realTimeToTickThrough;
			var originalPercents = (float[])ZombieTicker.percentZombiesTicked.Clone();
			var originalPercentIndex = ZombieTicker.percentZombiesTickedIndex;
			var originalZombiesTicked = ZombieTicker.zombiesTicked;
			var originalMaxTicking = ZombieTicker.maxTicking;
			var originalCurrentTicking = ZombieTicker.currentTicking;
			var originalSaturation = ZombieTicker.CaptureSaturation();
			var originalManagers = ZombieTicker.managers?.ToList() ?? new List<TickManager>();
			var originalCenterOfInterest = tickManager.centerOfInterest;
			var originalNextCenterOfInterest = tickManager.nextCenterOfInterest;
			var destroyedExisting = ZombieRuntimeActions.DestroyZombies(map);
			var spawned = new List<Zombie>();

			try
			{
				var normalizationCases = new[]
				{
					ZombieTicker.CreateTickUpdateState(Current.Game, 0, 1, 1f, true),
					ZombieTicker.CreateTickUpdateState(Current.Game, 0, 3, 3f, true),
					ZombieTicker.CreateTickUpdateState(Current.Game, 0, 6, 6f, true),
					ZombieTicker.CreateTickUpdateState(Current.Game, 0, 15, 15f, true)
				};
				var normalizationSuccess = normalizationCases.All(state => Mathf.Abs(state.normalizedDemand - 1f) < 0.0001f
					&& state.dueTicks == state.effectiveMultiplier
					&& state.tickCap == state.effectiveMultiplier * 2
					&& state.allowedTicks == state.effectiveMultiplier);
				var burstState = ZombieTicker.CreateTickUpdateState(Current.Game, 0, 15, 45f, true);
				var capSuccess = burstState.normalizedDemand == 3f && burstState.dueTicks == 45 && burstState.tickCap == 30 && burstState.allowedTicks == 30;

				ZombieTicker.ResetSaturation();
				for (var i = 0; i < 24; i++)
					ZombieTicker.ApplySaturationSample(1f, 0f);
				var globalOnlyState = ZombieTicker.saturationState;
				var globalPressureThrottlesButNeverEmergencies = globalOnlyState == ZombieSaturationState.Throttled;

				ZombieTicker.ResetSaturation();
				for (var i = 0; i < 8; i++)
					ZombieTicker.ApplySaturationSample(0f, 1f);
				var simulationPressureState = ZombieTicker.saturationState;
				var simulationPressureEmergencies = simulationPressureState == ZombieSaturationState.Emergency;
				var recoveryUpdates = 0;
				while (ZombieTicker.saturationState != ZombieSaturationState.Normal && recoveryUpdates < 240)
				{
					ZombieTicker.ApplySaturationSample(0f, 0f);
					recoveryUpdates++;
				}
				var recovered = ZombieTicker.saturationState == ZombieSaturationState.Normal;

				ZombieTicker.ResetSaturation();
				FillZombieTickPercent(1f);
				var halfCompletionState = ZombieTicker.CreateTickUpdateState(Current.Game, 0, 6, 6f, true);
				ZombieTicker.RecordTickUpdate(halfCompletionState, 3, 5f, 0f, controllerValid: true, completionComparable: true);
				var halfCompletionSlot = ZombieTicker.percentZombiesTicked[0];
				var halfCompletionAverage = ZombieTicker.PercentTicking;
				var completionFeedbackSuccess = Mathf.Abs(halfCompletionSlot - 0.5f) < 0.0001f
					&& Mathf.Abs(halfCompletionAverage - 0.9375f) < 0.0001f
					&& Mathf.Abs(ZombieTicker.lastCompletionRatio - 0.5f) < 0.0001f;

				const int targetCount = 20;
				const float prefixPercent = 0.25f;
				var root = BudgetContractRoot(map);
				for (var i = 0; i < targetCount; i++)
				{
					var candidateRoot = root + new IntVec3((i % 5) * 3, 0, (i / 5) * 3);
					if (TryFindClearSpawnCell(map, candidateRoot, 16f, out var cell, out var spawnError) == false)
						return spawnError;
					var zombie = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.Normal, true);
					if (zombie == null)
						return new { success = false, destroyedExisting, spawnedCount = spawned.Count, error = "Could not spawn the feedback fixture." };
					zombie.state = ZombieState.Wandering;
					spawned.Add(zombie);
				}
				tickManager.allZombiesCached.RemoveWhere(zombie => zombie == null || zombie.Destroyed || zombie.Spawned == false || zombie.Dead);
				foreach (var zombie in spawned)
					_ = tickManager.allZombiesCached.Add(zombie);

				ZombieTicker.EnsureAdaptiveGame(Current.Game);
				FillZombieTickPercent(prefixPercent);
				ZombieTicker.ResetSaturation();
				tickManager.centerOfInterest = IntVec3.Invalid;
				tickManager.nextCenterOfInterest = IntVec3.Invalid;
				tickManager.ResetRemoteScheduler(resetDiagnostics: true);
				ZombieTicker.zombiesTicked = 12345;
				gameTickManager.CurTimeSpeed = TimeSpeed.Normal;
				gameTickManager.realTimeToTickThrough = gameTickManager.CurTimePerTick * 2f;
				var prefixArguments = new object[] { gameTickManager, null };
				prefix.Invoke(null, prefixArguments);
				var prefixState = (ZombieTicker.TickUpdateState)prefixArguments[1];
				var wandererTimingIncluded = prefixState.startTimestamp > 0
					&& ZombieWanderer.lastProcessStartTimestamp >= prefixState.startTimestamp
					&& ZombieWanderer.lastProcessMilliseconds >= 0f;

				var liveCached = tickManager.allZombiesCached.Count(zombie => zombie.Spawned && zombie.Dead == false);
				var prefixPercentRead = ZombieTicker.PercentTicking;
				var prefixExpectedMax = prefixState.allowedTicks * liveCached;
				var prefixExpectedCurrent = Mathf.FloorToInt(prefixExpectedMax * prefixPercentRead);
				var prefixResetCounter = ZombieTicker.zombiesTicked == 0;
				var prefixSizedBudget = prefixState.eligible
					&& prefixState.hasLiveZombies
					&& liveCached == targetCount
					&& ZombieTicker.maxTicking == prefixExpectedMax
					&& ZombieTicker.currentTicking == prefixExpectedCurrent
					&& prefixExpectedCurrent > 0
					&& prefixExpectedCurrent < prefixExpectedMax;

				ZombieTicker.zombiesTicked = 0;
				gameTickManager.DoSingleTick();
				var singleTickExpected = Mathf.FloorToInt(liveCached * prefixPercentRead);
				var singleTickTickedCount = ZombieTicker.zombiesTicked;
				var singleTickSubsetCount = tickManager.currentZombiesTickingCount;
				var singleTickAllLive = CurrentTickingSliceAllLive(tickManager);
				var singleTickRanBudget = singleTickTickedCount == singleTickExpected
					&& singleTickSubsetCount == singleTickExpected
					&& singleTickAllLive;

				var neutralState = ZombieTicker.CreateTickUpdateState(Current.Game, System.Diagnostics.Stopwatch.GetTimestamp(),
					Math.Max(1, Mathf.RoundToInt(gameTickManager.TickRateMultiplier)), 0f, true);
				var samplesBeforePostfix = ZombieTicker.saturationSampleCount;
				postfix.Invoke(null, new object[] { gameTickManager, neutralState });
				var reflectedPostfixRan = ZombieTicker.saturationSampleCount == samplesBeforePostfix + 1;

				return new
				{
					success = normalizationSuccess
						&& capSuccess
						&& globalPressureThrottlesButNeverEmergencies
						&& simulationPressureEmergencies
						&& recovered
						&& completionFeedbackSuccess
						&& prefixResetCounter
						&& prefixSizedBudget
						&& wandererTimingIncluded
						&& singleTickRanBudget
						&& reflectedPostfixRan,
					destroyedExisting,
					normalization = new
					{
						success = normalizationSuccess,
						cases = normalizationCases.Select(state => new { state.effectiveMultiplier, state.rawDemandTicks, state.normalizedDemand, state.dueTicks, state.tickCap, state.allowedTicks }).ToArray(),
						burst = new { burstState.effectiveMultiplier, burstState.rawDemandTicks, burstState.normalizedDemand, burstState.dueTicks, burstState.tickCap, burstState.allowedTicks, success = capSuccess }
					},
					saturation = new
					{
						globalOnlyState = globalOnlyState.ToString(),
						globalPressureThrottlesButNeverEmergencies,
						simulationPressureState = simulationPressureState.ToString(),
						simulationPressureEmergencies,
						recoveryUpdates,
						recovered
					},
					completion = new { halfCompletionSlot, halfCompletionAverage, success = completionFeedbackSuccess },
					prefix = new
					{
						prefixState.effectiveMultiplier,
						prefixState.rawDemandTicks,
						prefixState.normalizedDemand,
						prefixState.dueTicks,
						prefixState.tickCap,
						prefixState.allowedTicks,
						prefixExpectedMax,
						prefixExpectedCurrent,
						actualMax = ZombieTicker.maxTicking,
						actualCurrent = ZombieTicker.currentTicking,
						wandererStartTimestamp = ZombieWanderer.lastProcessStartTimestamp,
						wandererMilliseconds = ZombieWanderer.lastProcessMilliseconds,
						wandererTimingIncluded,
						prefixResetCounter,
						prefixSizedBudget
					},
					singleTick = new { singleTickExpected, singleTickTickedCount, singleTickSubsetCount, singleTickAllLive, singleTickRanBudget },
					reflectedPostfixRan
				};
			}
			finally
			{
				ZombieRuntimeActions.DestroyZombies(map);
				gameTickManager.CurTimeSpeed = originalTimeSpeed;
				gameTickManager.realTimeToTickThrough = originalRealTimeToTickThrough;
				ZombieTicker.percentZombiesTicked = originalPercents;
				ZombieTicker.percentZombiesTickedIndex = originalPercentIndex;
				ZombieTicker.zombiesTicked = originalZombiesTicked;
				ZombieTicker.maxTicking = originalMaxTicking;
				ZombieTicker.currentTicking = originalCurrentTicking;
				ZombieTicker.RestoreSaturation(originalSaturation);
				ZombieTicker.managers = originalManagers;
				tickManager.centerOfInterest = originalCenterOfInterest;
				tickManager.nextCenterOfInterest = originalNextCenterOfInterest;
				tickManager.ClearZombieTickingBuffers();
				tickManager.ResetRemoteScheduler(resetDiagnostics: true);
			}
		}

		[Tool("zombieland/infected_incident_hooks_contract", Description = "Verify Zombieland's incident pawn-group infection and post-incident bite-harmless hooks.")]
		public static object InfectedIncidentHooksContract()
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			var generatePawnsPatch = typeof(Patches).GetNestedType("IncidentWorker_Patches", BindingFlags.NonPublic);
			var generatePawnsPostfix = generatePawnsPatch?.GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic);
			var incidentTryExecutePatch = typeof(Patches).GetNestedType("IncidentWorker_TryExecute_Patch", BindingFlags.NonPublic);
			var incidentTryExecutePostfix = incidentTryExecutePatch?.GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic);
			if (generatePawnsPostfix == null || incidentTryExecutePostfix == null)
			{
				return new
				{
					success = false,
					error = "Could not find both infected incident postfixes by reflection.",
					generatePawnsPostfixFound = generatePawnsPostfix != null,
					incidentTryExecutePostfixFound = incidentTryExecutePostfix != null
				};
			}

			var animalKind = DefDatabase<PawnKindDef>.GetNamed("Muffalo", false);
			if (animalKind == null)
			{
				return new
				{
					success = false,
					error = "PawnKindDef Muffalo was not found."
				};
			}

			var pawns = new List<Pawn>();
			var oldInfectedRaidsChance = ZombieSettings.Values.infectedRaidsChance;
			var oldUseDynamicThreatLevel = ZombieSettings.Values.useDynamicThreatLevel;
			try
			{
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root + new IntVec3(-8, 0, 12), 18f, out var raidHumanCell, out var raidHumanError) == false)
					return raidHumanError;
				if (TryFindClearSpawnCell(map, raidHumanCell + new IntVec3(3, 0, 0), 10f, out var raidAnimalCell, out var raidAnimalError) == false)
					return raidAnimalError;
				if (TryFindClearSpawnCell(map, raidHumanCell + new IntVec3(6, 0, 0), 12f, out var incidentPawnCell, out var incidentPawnError) == false)
					return incidentPawnError;

				var raidHuman = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfAncientsHostile);
				var raidAnimal = PawnGenerator.GeneratePawn(animalKind, null);
				var incidentPawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfAncientsHostile);
				pawns.AddRange(new[] { raidHuman, raidAnimal, incidentPawn });
				GenSpawn.Spawn(raidHuman, raidHumanCell, map, Rot4.South);
				GenSpawn.Spawn(raidAnimal, raidAnimalCell, map, Rot4.South);
				GenSpawn.Spawn(incidentPawn, incidentPawnCell, map, Rot4.South);
				DisablePawnWork(raidHuman);
				DisablePawnWork(incidentPawn);

				ZombieSettings.Values.infectedRaidsChance = 1f;
				ZombieSettings.Values.useDynamicThreatLevel = false;
				var generatedPawns = new List<Pawn> { raidHuman, raidAnimal };
				generatePawnsPostfix.Invoke(null, new object[] { generatedPawns });
				var raidHumanInfection = ZombieRuntimeActions.DescribePawnInfection(raidHuman);
				var raidAnimalInfection = ZombieRuntimeActions.DescribePawnInfection(raidAnimal);
				var raidHumanBites = new List<Hediff_Injury_ZombieBite>();
				raidHuman.health.hediffSet.GetHediffs(ref raidHumanBites);
				var raidAnimalBites = new List<Hediff_Injury_ZombieBite>();
				raidAnimal.health.hediffSet.GetHediffs(ref raidAnimalBites);

				if (ZombieRuntimeActions.AddZombieBite(incidentPawn, "final", out var bite, out var biteError) == false)
				{
					return new
					{
						success = false,
						error = biteError
					};
				}

				var incidentBefore = ZombieRuntimeActions.DescribePawnInfection(incidentPawn);
				var parms = new IncidentParms
				{
					pawnGroups = new Dictionary<Pawn, int>
					{
						[incidentPawn] = 0
					}
				};
				Rand.PushState(6300);
				try
				{
					incidentTryExecutePostfix.Invoke(null, new object[] { parms });
				}
				finally
				{
					Rand.PopState();
				}
				var incidentAfter = ZombieRuntimeActions.DescribePawnInfection(incidentPawn);
				var incidentBites = new List<Hediff_Injury_ZombieBite>();
				incidentPawn.health.hediffSet.GetHediffs(ref incidentBites);
				var allIncidentBitesHarmless = incidentBites.Count > 0
					&& incidentBites.All(current => current.mayBecomeZombieWhenDead == false
						&& (current.TendDuration?.GetInfectionState() ?? InfectionState.None) == InfectionState.BittenHarmless);

				return new
				{
					success = raidHumanBites.Count == 1
						&& raidAnimalBites.Count == 0
						&& allIncidentBitesHarmless,
					settings = new
					{
						ZombieSettings.Values.infectedRaidsChance,
						ZombieSettings.Values.useDynamicThreatLevel,
						threatLevel = ZombieWeather.GetThreatLevel(map)
					},
					raidGeneration = new
					{
						human = DescribePawn(raidHuman),
						animal = DescribePawn(raidAnimal),
						humanInfection = raidHumanInfection,
						animalInfection = raidAnimalInfection,
						humanBiteCount = raidHumanBites.Count,
						animalBiteCount = raidAnimalBites.Count
					},
					incidentReduction = new
					{
						pawn = DescribePawn(incidentPawn),
						before = incidentBefore,
						after = incidentAfter,
						biteCount = incidentBites.Count,
						allIncidentBitesHarmless
					},
					sourcePath = "PawnGroupKindWorker.GeneratePawns postfix and IncidentWorker.TryExecute postfix"
				};
			}
			finally
			{
				ZombieSettings.Values.infectedRaidsChance = oldInfectedRaidsChance;
				ZombieSettings.Values.useDynamicThreatLevel = oldUseDynamicThreatLevel;
				foreach (var pawn in pawns)
				{
					if (pawn.Corpse != null && pawn.Corpse.Destroyed == false)
						pawn.Corpse.Destroy(DestroyMode.Vanish);
					if (pawn.Destroyed == false)
						pawn.Destroy(DestroyMode.Vanish);
				}
			}
		}

		[Tool("zombieland/zombie_faction_pawn_generation_contract", Description = "Verify Zombie faction PawnGenerator requests route normal zombies through Zombieland while preserving symbiant/spitter generation.")]
		public static object ZombieFactionPawnGenerationContract()
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			var tickManager = map.GetComponent<TickManager>();
			if (tickManager == null)
			{
				return new
				{
					success = false,
					error = "No Zombieland TickManager is attached to the current map."
				};
			}

			var zombieFaction = Find.FactionManager.FirstFactionOfDef(ZombieDefOf.Zombies);
			if (zombieFaction == null)
			{
				return new
				{
					success = false,
					error = "No Zombies faction was found."
				};
			}

			Pawn generatedNormal = null;
			Pawn generatedSpitter = null;
			Pawn generatedSymbiant = null;
			try
			{
				var beforeIds = CurrentZombies(map)
					.Select(ZombieRuntimeActions.StableThingId)
					.ToHashSet(StringComparer.OrdinalIgnoreCase);

				Rand.PushState(6400);
				try
				{
					generatedNormal = PawnGenerator.GeneratePawn(ZombieDefOf.Zombie, zombieFaction);
					generatedSpitter = PawnGenerator.GeneratePawn(ZombieDefOf.ZombieSpitter, zombieFaction);
					generatedSymbiant = PawnGenerator.GeneratePawn(ZombieDefOf.ZombieSymbiant, zombieFaction);
				}
				finally
				{
					Rand.PopState();
				}

				var normal = generatedNormal as Zombie;
				if (normal != null)
					_ = tickManager.allZombiesCached.Add(normal);
				var newZombieIds = CurrentZombies(map)
					.Select(ZombieRuntimeActions.StableThingId)
					.Where(id => beforeIds.Contains(id) == false)
					.ToArray();

				return new
				{
					success = normal != null
						&& normal.Spawned
						&& normal.Faction == zombieFaction
						&& normal.kindDef == ZombieDefOf.Zombie
						&& newZombieIds.Length == 1
						&& generatedSpitter is ZombieSpitter
						&& generatedSpitter.Spawned == false
						&& generatedSymbiant is ZombieSymbiant
						&& generatedSymbiant.Spawned == false,
					zombieFaction = zombieFaction.def?.defName,
					sourcePath = "PawnGenerator.GenerateNewPawnInternal prefix",
					normal = DescribeZombie(generatedNormal),
					normalSpawnedThroughPatch = normal?.Spawned ?? false,
					newZombieIds,
					spitter = DescribeZombie(generatedSpitter),
					spitterType = generatedSpitter?.GetType().FullName,
					spitterSpawned = generatedSpitter?.Spawned ?? false,
					symbiant = DescribeZombie(generatedSymbiant),
					symbiantType = generatedSymbiant?.GetType().FullName,
					symbiantSpawned = generatedSymbiant?.Spawned ?? false
				};
			}
			finally
			{
				if (generatedNormal is Zombie zombie)
				{
					_ = tickManager.allZombiesCached?.Remove(zombie);
					_ = tickManager.hummingZombies?.Remove(zombie);
					_ = tickManager.tankZombies?.Remove(zombie);
				}
				foreach (var pawn in new[] { generatedNormal, generatedSpitter, generatedSymbiant }.Where(pawn => pawn != null).Distinct())
				{
					if (pawn is ZombieSymbiant symbiant)
					{
						symbiant.DebugDestroyWithoutHostTrauma();
						continue;
					}
					if (pawn.Spawned && pawn.Destroyed == false)
						pawn.Destroy(DestroyMode.Vanish);
				}
			}
		}

		[Tool("zombieland/zombie_active_threat_count_contract", Description = "Verify GenHostility.IsActiveThreatTo excludes all Zombieland pawn types from player hostile counts.")]
		public static object ZombieActiveThreatCountContract()
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			var destroyedZombies = ZombieRuntimeActions.DestroyZombies(map);
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var normalCell, out var normalSpawnError) == false)
				return normalSpawnError;
			if (TryFindClearSpawnCell(map, normalCell + new IntVec3(3, 0, 0), 8f, out var spitterCell, out var spitterSpawnError) == false)
				return spitterSpawnError;
			if (TryFindClearSpawnCell(map, normalCell + new IntVec3(6, 0, 0), 10f, out var symbiantCell, out var symbiantSpawnError) == false)
				return symbiantSpawnError;

			var normal = SpawnFireFixturePawn(map, normalCell, "normal");
			var spitter = SpawnFireFixturePawn(map, spitterCell, "spitter");
			var symbiant = SpawnFireFixturePawn(map, symbiantCell, "symbiant");
			var normalThreat = GenHostility.IsActiveThreatTo(normal, Faction.OfPlayer, false, false);
			var spitterThreat = GenHostility.IsActiveThreatTo(spitter, Faction.OfPlayer, false, false);
			var symbiantThreat = GenHostility.IsActiveThreatTo(symbiant, Faction.OfPlayer, false, false);

			return new
			{
				success = normal is Zombie
					&& spitter is ZombieSpitter
					&& symbiant is ZombieSymbiant
					&& normalThreat == false
					&& spitterThreat == false
					&& symbiantThreat == false,
				destroyedZombies,
				normal = DescribePawn(normal),
				spitter = DescribePawn(spitter),
				symbiant = DescribePawn(symbiant),
				threatsToPlayer = new
				{
					normal = normalThreat,
					spitter = spitterThreat,
					symbiant = symbiantThreat
				}
			};
		}

		[Tool("zombieland/zombie_target_cache_excludes_specials", Description = "Verify AttackTargetsCache.TargetsHostileToColony excludes normal, spitter, and symbiant zombies.")]
		public static object ZombieTargetCacheExcludesSpecials()
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			var destroyedZombies = ZombieRuntimeActions.DestroyZombies(map);
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var normalCell, out var normalSpawnError) == false)
				return normalSpawnError;
			if (TryFindClearSpawnCell(map, normalCell + new IntVec3(3, 0, 0), 8f, out var spitterCell, out var spitterSpawnError) == false)
				return spitterSpawnError;
			if (TryFindClearSpawnCell(map, normalCell + new IntVec3(6, 0, 0), 10f, out var symbiantCell, out var symbiantSpawnError) == false)
				return symbiantSpawnError;

			var normal = SpawnFireFixturePawn(map, normalCell, "normal");
			var spitter = SpawnFireFixturePawn(map, spitterCell, "spitter");
			var symbiant = SpawnFireFixturePawn(map, symbiantCell, "symbiant");
			map.attackTargetsCache.UpdateTarget(normal);
			map.attackTargetsCache.UpdateTarget(spitter);
			map.attackTargetsCache.UpdateTarget(symbiant);
			var hostileTargets = map.attackTargetsCache.TargetsHostileToColony;
			var containsNormal = hostileTargets.Contains(normal);
			var containsSpitter = hostileTargets.Contains(spitter);
			var containsSymbiant = hostileTargets.Contains(symbiant);
			var zTypesInCache = hostileTargets
				.Select(target => target.Thing)
				.Where(thing => thing is Zombie || thing is ZombieSpitter || thing is ZombieSymbiant)
				.Select(thing => thing.def?.defName)
				.Distinct()
				.OrderBy(defName => defName)
				.ToArray();

			return new
			{
				success = normal is Zombie
					&& spitter is ZombieSpitter
					&& symbiant is ZombieSymbiant
					&& containsNormal == false
					&& containsSpitter == false
					&& containsSymbiant == false,
				destroyedZombies,
				normal = DescribePawn(normal),
				spitter = DescribePawn(spitter),
				symbiant = DescribePawn(symbiant),
				hostileCount = hostileTargets.Count,
				contains = new
				{
					normal = containsNormal,
					spitter = containsSpitter,
					symbiant = containsSymbiant
				},
				zombielandDefsInCache = zTypesInCache
			};
		}

	}
}
