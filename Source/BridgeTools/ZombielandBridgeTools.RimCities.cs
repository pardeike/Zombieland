using RimBridgeServer.Sdk;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace ZombieLand
{
	public sealed partial class ZombielandBridgeTools
	{
		sealed class RimCitiesRevealCandidate
		{
			public Room room;
			public HashSet<IntVec3> roomCells;
			public Building_Door door;
			public IntVec3 playerCell;
			public Building wall;
			public IntVec3 wallCell;
			public IntVec3 outsideCell;
			public IntVec3 roomCell;
		}

		[Tool("zombieland/rimcities_fog_reveal_contract", Description = "Generate real RimCities city maps and verify natural fogged city rooms spawn Zombieland zombies when revealed through a door or fog-blocking wall.")]
		public static object RimCitiesFogRevealContract(
			[ToolParameter(Description = "Square city map size to generate for each case.", Required = false, DefaultValue = 180)] int size = 180,
			[ToolParameter(Description = "Deterministic Verse.Rand seed used for the first generated city map.", Required = false, DefaultValue = 91601)] int seed = 91601,
			[ToolParameter(Description = "Temporarily suppress zombie-free events so this contract tests the room-reveal spawn path rather than the global silence gate.", Required = false, DefaultValue = true)] bool suppressZombieFreeEvents = true)
		{
			var currentMap = CurrentMap;
			if (currentMap == null)
				return new { success = false, error = "No current map is loaded; start a debug game first." };

			var rimCitiesMod = LoadedModManager.RunningModsListForReading
				.FirstOrDefault(mod => string.Equals(mod.PackageId, "cabbage.rimcities", StringComparison.OrdinalIgnoreCase)
					|| string.Equals(mod.PackageIdPlayerFacing, "Cabbage.RimCities", StringComparison.OrdinalIgnoreCase));
			var cityDef = DefDatabase<WorldObjectDef>.GetNamedSilentFail("City_Abandoned");
			var cityMapGenerator = DefDatabase<MapGeneratorDef>.GetNamedSilentFail("City_Abandoned");
			if (rimCitiesMod == null || cityDef == null || cityMapGenerator == null)
			{
				return new
				{
					success = false,
					error = "RimCities is not fully active; missing mod metadata, City_Abandoned world object, or City_Abandoned map generator.",
					rimCitiesActive = rimCitiesMod != null,
					cityWorldObjectDef = cityDef?.defName,
					cityMapGenerator = cityMapGenerator?.defName
				};
			}

			size = Math.Max(100, Math.Min(size, 250));
			var door = RunRimCitiesRevealCase("door", cityDef, cityMapGenerator, size, seed, suppressZombieFreeEvents);
			var wall = RunRimCitiesRevealCase("wall", cityDef, cityMapGenerator, size, seed + 1, suppressZombieFreeEvents);

			return new
			{
				success = ObjectSuccess(door) && ObjectSuccess(wall),
				rimCities = new
				{
					packageId = rimCitiesMod.PackageId,
					name = rimCitiesMod.Name,
					rootDir = rimCitiesMod.RootDir,
					cityWorldObjectDef = cityDef.defName,
					cityWorldObjectClass = cityDef.worldObjectClass?.FullName,
					cityMapGenerator = cityMapGenerator.defName,
					suppressZombieFreeEvents
				},
				door,
				wall
			};
		}

		static object RunRimCitiesRevealCase(string mode, WorldObjectDef cityDef, MapGeneratorDef cityMapGenerator, int size, int seed, bool suppressZombieFreeEvents)
		{
			var previousMap = CurrentMap;
			Map generatedMap = null;
			MapParent city = null;
			var settingsSnapshot = SnapshotZombieSettings();
			var zombieFreeManager = ZombieFreeEventManager.Current;
			ZombieFreeScheduleSnapshot scheduleSnapshot = null;
			try
			{
				if (suppressZombieFreeEvents && zombieFreeManager != null && TrySnapshotZombieFreeSchedule(zombieFreeManager, out scheduleSnapshot, out var scheduleSnapshotError) == false)
				{
					return new
					{
						success = false,
						mode,
						error = scheduleSnapshotError
					};
				}

				city = WorldObjectMaker.MakeWorldObject(cityDef) as MapParent;
				if (city == null)
				{
					return new
					{
						success = false,
						mode,
						error = "City_Abandoned did not create a MapParent.",
						worldObjectClass = cityDef.worldObjectClass?.FullName
					};
				}

				city.SetFaction(Faction.OfPlayer);
				city.Tile = FindRimCitiesTestTile(previousMap);
				Find.WorldObjects.Add(city);

				Rand.PushState(seed);
				try
				{
					generatedMap = MapGenerator.GenerateMap(new IntVec3(size, 1, size), city, cityMapGenerator, null, null, false);
				}
				finally
				{
					Rand.PopState();
				}
				Current.Game.CurrentMap = generatedMap;
				generatedMap.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
				_ = ZombieRuntimeActions.DestroyZombies(generatedMap);

				var oldMaximumNumberOfZombies = ZombieSettings.Values.maximumNumberOfZombies;
				ApplyZombieSettingsOverride(settings =>
				{
					settings.infectedRaidsChance = 1f;
					settings.useDynamicThreatLevel = false;
					settings.maximumNumberOfZombies = Math.Max(oldMaximumNumberOfZombies, 100);
					if (suppressZombieFreeEvents)
					{
						settings.daysBeforeZombiesCome = 0;
						settings.zombieFreeEvents = false;
					}
				});
				if (suppressZombieFreeEvents && zombieFreeManager != null)
				{
					zombieFreeManager.DebugClearSchedule();
					zombieFreeManager.DebugRefreshCurrentWindowState();
				}

				var candidates = RimCitiesFoggedRoomCandidates(generatedMap).ToArray();
				var candidate = mode == "door"
					? candidates.Select(room => TryFindRimCitiesDoorCandidate(generatedMap, room)).FirstOrDefault(item => item != null)
					: candidates.Select(room => TryFindRimCitiesWallCandidate(generatedMap, room)).FirstOrDefault(item => item != null);
				if (candidate == null)
				{
					return new
					{
						success = false,
						mode,
						error = $"No natural fogged RimCities room with a usable {mode} reveal candidate was found.",
						map = DescribeRimCitiesMap(generatedMap, city, cityMapGenerator),
						foggedRoomCandidateCount = candidates.Length,
						foggedRoomSamples = candidates.Take(8).Select(room => DescribeRimCitiesRoom(room, generatedMap)).ToArray()
					};
				}

				return mode == "door"
					? RevealRimCitiesDoor(generatedMap, city, cityMapGenerator, candidate)
					: RevealRimCitiesWall(generatedMap, city, cityMapGenerator, candidate);
			}
			catch (Exception ex)
			{
				return new
				{
					success = false,
					mode,
					error = ex.Message,
					exceptionType = ex.GetType().FullName,
					stackTrace = ex.StackTrace,
					map = generatedMap == null ? null : DescribeRimCitiesMap(generatedMap, city, cityMapGenerator)
				};
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
				RestoreZombieFreeSchedule(zombieFreeManager, scheduleSnapshot);
			}
		}

		static object RevealRimCitiesDoor(Map map, MapParent city, MapGeneratorDef mapGenerator, RimCitiesRevealCandidate candidate)
		{
			Pawn player = null;
			try
			{
				map.fogGrid.Unfog(candidate.playerCell);
				player = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(player, candidate.playerCell, map, Rot4.North);
				DisablePawnWork(player);

				var before = CurrentZombies(map).Length;
				var spawnGateBefore = DescribeRimCitiesRoomSpawnGate(map, candidate.room, candidate.roomCells);
				var zombieSnapshotBefore = DescribeRimCitiesZombieSnapshot(map, candidate.roomCells);
				var roomFoggedBefore = candidate.room.Fogged;
				var foggedCellsBefore = candidate.roomCells.Count(cell => cell.Fogged(map));
				map.fogGrid.Notify_PawnEnteringDoor(candidate.door, player);
				var after = CurrentZombies(map).Length;
				var zombieSnapshotAfter = DescribeRimCitiesZombieSnapshot(map, candidate.roomCells);
				var spawned = CurrentZombies(map)
					.OfType<Zombie>()
					.Where(zombie => candidate.roomCells.Contains(zombie.Position))
					.ToArray();
				var foggedCellsAfter = candidate.roomCells.Count(cell => cell.Fogged(map));

				return new
				{
					success = roomFoggedBefore && after > before && spawned.Length > 0,
					mode = "door",
					sourcePath = "RimCities City_Abandoned MapGenerator -> natural room -> FogGrid.Notify_PawnEnteringDoor prefix",
					map = DescribeRimCitiesMap(map, city, mapGenerator),
					room = DescribeRimCitiesRoom(candidate.room, map),
					door = new
					{
						id = ZombieRuntimeActions.StableThingId(candidate.door),
						position = ZombieRuntimeActions.DescribeCell(candidate.door.Position),
						open = candidate.door.Open
					},
					player = DescribePawn(player),
					playerCell = ZombieRuntimeActions.DescribeCell(candidate.playerCell),
					roomFoggedBefore,
					foggedCellsBefore,
					foggedCellsAfter,
					spawnGateBefore,
					zombieSnapshotBefore,
					zombieSnapshotAfter,
					zombiesBefore = before,
					zombiesAfter = after,
					zombieDelta = after - before,
					spawnedZombies = spawned.Select(DescribeZombie).ToArray()
				};
			}
			finally
			{
				if (player != null && player.Destroyed == false)
					player.Destroy(DestroyMode.Vanish);
			}
		}

		static object RevealRimCitiesWall(Map map, MapParent city, MapGeneratorDef mapGenerator, RimCitiesRevealCandidate candidate)
		{
			var before = CurrentZombies(map).Length;
			var spawnGateBefore = DescribeRimCitiesRoomSpawnGate(map, candidate.room, candidate.roomCells);
			var zombieSnapshotBefore = DescribeRimCitiesZombieSnapshot(map, candidate.roomCells);
			var roomFoggedBefore = candidate.room.Fogged;
			var foggedCellsBefore = candidate.roomCells.Count(cell => cell.Fogged(map));
			map.fogGrid.Unfog(candidate.outsideCell);
			candidate.wall.Destroy(DestroyMode.Deconstruct);
			var after = CurrentZombies(map).Length;
			var zombieSnapshotAfter = DescribeRimCitiesZombieSnapshot(map, candidate.roomCells);
			var spawned = CurrentZombies(map)
				.OfType<Zombie>()
				.Where(zombie => candidate.roomCells.Contains(zombie.Position))
				.ToArray();
			var foggedCellsAfter = candidate.roomCells.Count(cell => cell.Fogged(map));

			return new
			{
				success = roomFoggedBefore && candidate.wall.Destroyed && after > before && spawned.Length > 0,
				mode = "wall",
				sourcePath = "RimCities City_Abandoned MapGenerator -> natural room -> Building.DeSpawn prefix",
				map = DescribeRimCitiesMap(map, city, mapGenerator),
				room = DescribeRimCitiesRoom(candidate.room, map),
				targetWall = new
				{
					id = ZombieRuntimeActions.StableThingId(candidate.wall),
					position = ZombieRuntimeActions.DescribeCell(candidate.wallCell),
					destroyed = candidate.wall.Destroyed,
					defName = candidate.wall.def?.defName,
					makeFog = candidate.wall.def?.MakeFog ?? false
				},
				outsideCell = ZombieRuntimeActions.DescribeCell(candidate.outsideCell),
				roomCell = ZombieRuntimeActions.DescribeCell(candidate.roomCell),
				roomFoggedBefore,
				foggedCellsBefore,
				foggedCellsAfter,
				spawnGateBefore,
				zombieSnapshotBefore,
				zombieSnapshotAfter,
				zombiesBefore = before,
				zombiesAfter = after,
				zombieDelta = after - before,
				spawnedZombies = spawned.Select(DescribeZombie).ToArray()
			};
		}

		static PlanetTile FindRimCitiesTestTile(Map previousMap)
		{
			for (var i = 0; i < 100; i++)
			{
				var tile = TileFinder.RandomStartingTile();
				if (tile != PlanetTile.Invalid
					&& Find.WorldObjects.MapParentAt(tile) == null
					&& TileFinder.IsValidTileForNewSettlement(tile))
				{
					return tile;
				}
			}

			return previousMap?.Tile ?? TileFinder.RandomStartingTile();
		}

		static IEnumerable<Room> RimCitiesFoggedRoomCandidates(Map map)
		{
			var maxCount = (int)GenMath.LerpDoubleClamped(0, 5, 200, 800, Tools.Difficulty());
			return map.regionGrid.allRooms
				.Where(room => room != null && room.IsDoorway == false && room.IsHuge == false && room.TouchesMapEdge == false && room.Fogged)
				.Where(room => room.CellCount >= 10 && room.CellCount <= maxCount)
				.Where(room => room.Regions.SelectMany(region => region.ListerThings.ThingsInGroup(ThingRequestGroup.Pawn)).Any() == false)
				.OrderByDescending(room => room.CellCount);
		}

		static RimCitiesRevealCandidate TryFindRimCitiesDoorCandidate(Map map, Room room)
		{
			var roomCells = room.Cells.ToHashSet();
			foreach (var roomCell in roomCells)
			{
				foreach (var offset in GenAdj.CardinalDirections)
				{
					var doorCell = roomCell + offset;
					if (doorCell.InBounds(map) == false)
						continue;
					if (doorCell.GetEdifice(map) is not Building_Door door)
						continue;

					var playerCell = doorCell + offset;
					if (playerCell.InBounds(map) == false || roomCells.Contains(playerCell))
						continue;
					if (playerCell.Standable(map) == false || playerCell.GetFirstPawn(map) != null)
						continue;

					return new RimCitiesRevealCandidate
					{
						room = room,
						roomCells = roomCells,
						door = door,
						playerCell = playerCell,
						roomCell = roomCell
					};
				}
			}

			return null;
		}

		static RimCitiesRevealCandidate TryFindRimCitiesWallCandidate(Map map, Room room)
		{
			var roomCells = room.Cells.ToHashSet();
			foreach (var roomCell in roomCells)
			{
				foreach (var offset in GenAdj.CardinalDirections)
				{
					var wallCell = roomCell + offset;
					if (wallCell.InBounds(map) == false)
						continue;
					var wall = wallCell.GetEdifice(map) as Building;
					if (wall?.def?.MakeFog != true)
						continue;

					var outsideCell = wallCell + offset;
					if (outsideCell.InBounds(map) == false || roomCells.Contains(outsideCell))
						continue;
					if (outsideCell.GetEdifice(map) != null || outsideCell.Standable(map) == false || outsideCell.GetFirstPawn(map) != null)
						continue;

					return new RimCitiesRevealCandidate
					{
						room = room,
						roomCells = roomCells,
						wall = wall,
						wallCell = wallCell,
						outsideCell = outsideCell,
						roomCell = roomCell
					};
				}
			}

			return null;
		}

		static object DescribeRimCitiesMap(Map map, MapParent city, MapGeneratorDef mapGenerator)
		{
			return new
			{
				mapId = map?.uniqueID ?? -1,
				mapIndex = map?.Index ?? -1,
				mapSize = map == null ? null : new { x = map.Size.x, z = map.Size.z },
				mapBiome = map?.Biome?.defName,
				parentDef = city?.def?.defName,
				parentType = city?.GetType().FullName,
				parentFaction = city?.Faction?.def?.defName,
				parentTile = city?.Tile.ToString(),
				mapGenerator = mapGenerator?.defName,
				roomCount = map?.regionGrid?.allRooms?.Count ?? 0
			};
		}

		static object DescribeRimCitiesRoom(Room room, Map map)
		{
			if (room == null)
				return null;
			var cells = room.Cells.ToArray();
			var sampleCell = cells.FirstOrDefault();
			return new
			{
				cellCount = room.CellCount,
				fogged = room.Fogged,
				foggedCells = cells.Count(cell => cell.Fogged(map)),
				isHuge = room.IsHuge,
				touchesMapEdge = room.TouchesMapEdge,
				usesOutdoorTemperature = room.UsesOutdoorTemperature,
				properRoom = room.ProperRoom,
				sampleCell = sampleCell.IsValid ? ZombieRuntimeActions.DescribeCell(sampleCell) : null
			};
		}

		static object DescribeRimCitiesRoomSpawnGate(Map map, Room room, HashSet<IntVec3> roomCells)
		{
			if (map == null || room == null)
				return new { pass = false, error = "Map or room is null." };

			var cellCount = room.CellCount;
			var maxCount = (int)GenMath.LerpDoubleClamped(0, 5, 200, 800, Tools.Difficulty());
			var pawns = room.Regions
				.SelectMany(region => region.ListerThings.ThingsInGroup(ThingRequestGroup.Pawn))
				.OfType<Pawn>()
				.Distinct()
				.ToArray();
			var threatLevel = ZombieWeather.GetThreatLevel(map);
			var pass = room.IsHuge == false
				&& room.TouchesMapEdge == false
				&& room.Fogged
				&& cellCount >= 10
				&& cellCount <= maxCount
				&& pawns.Length == 0
				&& ZombieSettings.Values.infectedRaidsChance > 0f
				&& threatLevel != 0f;

			return new
			{
				pass,
				parentDef = map.Parent?.def?.defName,
				mapBlacklisted = map.IsBlacklisted(),
				roomIsHuge = room.IsHuge,
				roomTouchesMapEdge = room.TouchesMapEdge,
				roomFogged = room.Fogged,
				cellCount,
				maxCount,
				expectedSpawnAttempts = pass ? cellCount / 10 : 0,
				pawnCount = pawns.Length,
				pawnSamples = pawns.Take(8).Select(pawn => DescribePawn(pawn)).ToArray(),
				infectedRaidsChance = ZombieSettings.Values.infectedRaidsChance,
				useDynamicThreatLevel = ZombieSettings.Values.useDynamicThreatLevel,
				threatLevel,
				zombieFreeEventActive = ZombieFreeEventManager.IsActiveNow(),
				sampleCells = roomCells
					.OrderBy(cell => cell.x)
					.ThenBy(cell => cell.z)
					.Take(20)
					.Select(cell => DescribeRimCitiesRoomCell(map, room, cell))
					.ToArray()
			};
		}

		static object DescribeRimCitiesRoomCell(Map map, Room room, IntVec3 cell)
		{
			var edifice = cell.GetEdifice(map);
			var firstPawn = cell.GetFirstPawn(map);
			var things = cell.GetThingList(map);
			var cellRoom = cell.GetRoom(map);
			return new
			{
				cell = ZombieRuntimeActions.DescribeCell(cell),
				fogged = cell.Fogged(map),
				standable = cell.Standable(map),
				walkable = cell.Walkable(map),
				roofed = cell.Roofed(map),
				sameRoom = ReferenceEquals(cellRoom, room),
				terrain = cell.GetTerrain(map)?.defName,
				edifice = edifice?.def?.defName,
				edificeMakeFog = edifice?.def?.MakeFog,
				edificePassability = edifice?.def?.passability.ToString(),
				firstPawn = firstPawn == null ? null : DescribePawn(firstPawn),
				thingDefs = things
					.Where(thing => thing != null)
					.Take(8)
					.Select(thing => thing.def?.defName)
					.ToArray()
			};
		}

		static object DescribeRimCitiesZombieSnapshot(Map map, HashSet<IntVec3> roomCells)
		{
			var spawned = CurrentZombies(map)
				.OfType<Zombie>()
				.ToArray();
			var cached = map?.GetComponent<TickManager>()?.allZombiesCached?
				.Where(zombie => zombie != null)
				.ToArray() ?? Array.Empty<Zombie>();

			return new
			{
				spawnedTotal = spawned.Length,
				spawnedInRoom = spawned.Count(zombie => roomCells.Contains(zombie.Position)),
				cachedTotal = cached.Length,
				cachedSpawned = cached.Count(zombie => zombie.Spawned),
				cachedUnspawned = cached.Count(zombie => zombie.Spawned == false && zombie.Destroyed == false),
				cachedDestroyed = cached.Count(zombie => zombie.Destroyed),
				cachedDead = cached.Count(zombie => zombie.Dead),
				cachedInRoom = cached.Count(zombie => zombie.Position.IsValid && roomCells.Contains(zombie.Position)),
				cachedSamples = cached
					.Take(20)
					.Select(zombie => DescribeRimCitiesCachedZombie(zombie, roomCells))
					.ToArray()
			};
		}

		static object DescribeRimCitiesCachedZombie(Zombie zombie, HashSet<IntVec3> roomCells)
		{
			return new
			{
				id = ZombieRuntimeActions.StableThingId(zombie),
				spawned = zombie.Spawned,
				destroyed = zombie.Destroyed,
				dead = zombie.Dead,
				position = zombie.Position.IsValid ? ZombieRuntimeActions.DescribeCell(zombie.Position) : null,
				positionInRoom = zombie.Position.IsValid && roomCells.Contains(zombie.Position),
				mapId = zombie.Map?.uniqueID
			};
		}
	}
}
