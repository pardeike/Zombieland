using HarmonyLib;
using RimBridgeServer.Sdk;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	public sealed partial class ZombielandBridgeTools
	{
		[Tool("zombieland/symbiant_discovery_letter_contract", Description = "Spawn a temporary symbiant through the runtime spawn path and verify the green discovery letter, sound def, look targets, host link, and cleanup behavior.")]
		public static object SymbiantDiscoveryLetterContract(
			[ToolParameter(Description = "Target x coordinate. Use -1 with z -1 for automatic placement.", Required = false, DefaultValue = -1)] int x = -1,
			[ToolParameter(Description = "Target z coordinate. Use -1 with x -1 for automatic placement.", Required = false, DefaultValue = -1)] int z = -1,
			[ToolParameter(Description = "Destroy the temporary contract symbiant without host trauma after capturing evidence.", Required = false, DefaultValue = true)] bool cleanup = true)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded." };

			var existingActive = ZombieSymbiant.ActiveSymbiant(map);
			var existingActiveId = ZombieRuntimeActions.StableThingId(existingActive);
			var root = x >= 0 && z >= 0 ? new IntVec3(x, 0, z) : new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 18f, out var cell, out var cellError) == false)
				return cellError;

			var originalShowLetters = ZombieSettings.Values.showZombieEventLetters;
			var beforeSymbiantIds = CurrentZombies(map)
				.OfType<ZombieSymbiant>()
				.Select(ZombieRuntimeActions.StableThingId)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
			var beforeLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
				.ToHashSet();
			ZombieSymbiant spawned = null;
			object spawnError = null;
			object result;

			try
			{
				ZombieSettings.Values.showZombieEventLetters = true;
				ZombieSymbiant.Spawn(map, cell);
				spawned = CurrentZombies(map)
					.OfType<ZombieSymbiant>()
					.Where(symbiant => beforeSymbiantIds.Contains(ZombieRuntimeActions.StableThingId(symbiant)) == false)
					.OrderBy(symbiant => symbiant.Position.DistanceToSquared(cell))
					.FirstOrDefault();
				spawned ??= ZombieSymbiant.ActiveSymbiant(map);
			}
			catch (Exception ex)
			{
				spawnError = ex.ToString();
			}
			finally
			{
				ZombieSettings.Values.showZombieEventLetters = originalShowLetters;
			}

			var newLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
				.Where(letter => beforeLetters.Contains(letter) == false)
				.ToArray();
			var matchingLetters = newLetters
				.Where(letter => letter?.def == CustomDefs.SymbiantConnection)
				.ToArray();
			var host = spawned?.LinkedHost;
			var expectedLabel = host == null
				? "LetterLabelZombieSymbiantNoHost".Translate().ToString()
				: "LetterLabelZombieSymbiant".Translate(host.LabelShortCap).ToString();
			var primaryLetter = matchingLetters.FirstOrDefault();
			var lookTargetCount = primaryLetter?.lookTargets?.targets?.Count ?? 0;
			var expectedLookTargetCount = host == null ? 1 : 2;
			var connectionColor = CustomDefs.SymbiantConnection?.color;
			var colorOk = connectionColor != null
				&& connectionColor.Value.g > connectionColor.Value.r
				&& connectionColor.Value.g > connectionColor.Value.b
				&& connectionColor.Value.g >= 0.4f;
			var defOk = CustomDefs.SymbiantConnection != null
				&& CustomDefs.SymbiantConnected != null
				&& CustomDefs.SymbiantDisconnected != null
				&& CustomDefs.SymbiantConnection.arriveSound == CustomDefs.SymbiantConnected
				&& colorOk;
			var success = spawnError == null
				&& spawned?.Spawned == true
				&& matchingLetters.Length == 1
				&& primaryLetter?.Label.ToString() == expectedLabel
				&& lookTargetCount >= expectedLookTargetCount
				&& defOk;

			var cleanupResult = CleanupTemporarySymbiant(map, spawned, cleanup);
			var activeAfterCleanup = ZombieSymbiant.ActiveSymbiant(map);
			var letters = newLetters.Select(DescribeSymbiantDiscoveryLetter).ToArray();
			var letterCleanup = CleanupTemporaryLetters(newLetters, cleanup);

			result = new
			{
				success,
				sourcePath = "ZombieSymbiant.Spawn -> CustomDefs.SymbiantConnection -> Find.LetterStack.ReceiveLetter",
				spawnError,
				requestedCell = ZombieRuntimeActions.DescribeCell(root),
				spawnCell = ZombieRuntimeActions.DescribeCell(cell),
				existingActiveSymbiantBefore = existingActiveId,
				activeSymbiantAfterCleanup = ZombieRuntimeActions.StableThingId(activeAfterCleanup),
				restoredExistingActive = existingActive == null
					? activeAfterCleanup == null || cleanup == false
					: activeAfterCleanup == existingActive || cleanup == false,
				spawned = spawned == null ? null : new
				{
					id = ZombieRuntimeActions.StableThingId(spawned),
					spawned = spawned.Spawned,
					destroyed = spawned.Destroyed,
					cellCount = spawned.CellCount,
					position = spawned.Spawned ? ZombieRuntimeActions.DescribeCell(spawned.Position) : null,
					host = host == null ? null : new
					{
						id = ZombieRuntimeActions.StableThingId(host),
						label = host.LabelShortCap,
						position = host.Spawned ? ZombieRuntimeActions.DescribeCell(host.Position) : null,
						hasSymbiosisHediff = host.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) != null
					}
				},
				defs = new
				{
					connectionLetter = CustomDefs.SymbiantConnection?.defName,
					connectionLetterArriveSound = CustomDefs.SymbiantConnection?.arriveSound?.defName,
					connectedSound = CustomDefs.SymbiantConnected?.defName,
					disconnectedSound = CustomDefs.SymbiantDisconnected?.defName,
					connectionLetterColor = CustomDefs.SymbiantConnection == null ? null : DescribeColor(CustomDefs.SymbiantConnection.color),
					colorOk,
					defOk
				},
				expectedLabel,
				expectedLookTargetCount,
				newLetterCount = newLetters.Length,
				matchingLetterCount = matchingLetters.Length,
				letters,
				cleanup = cleanupResult,
				letterCleanup
			};

			return result;
		}

		[Tool("zombieland/symbiant_natural_spawn_contract", Description = "Inspect the natural symbiant spawn plan and optionally exercise TrySpawnInBestRoom with cleanup.")]
		public static object SymbiantNaturalSpawnContract(
			[ToolParameter(Description = "Run TrySpawnInBestRoom after inspecting the plan. If false, this is read-only.", Required = false, DefaultValue = false)] bool spawn = false,
			[ToolParameter(Description = "Create a reversible bedroom fixture first when no active symbiant exists, so the positive natural-spawn path can be tested.", Required = false, DefaultValue = false)] bool setupFixture = false,
			[ToolParameter(Description = "Destroy a symbiant and fixture created by this contract without host trauma and remove generated letters.", Required = false, DefaultValue = true)] bool cleanup = true)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded." };

			var activeBefore = ZombieSymbiant.ActiveSymbiant(map);
			var activeBeforeId = ZombieRuntimeActions.StableThingId(activeBefore);
			var initialPlan = ZombieSymbiant.DebugNaturalSpawnPlan(map);
			SymbiantNaturalSpawnFixture fixture = null;
			object fixtureSetup = null;
			if (setupFixture && activeBefore == null)
				fixtureSetup = TrySetupSymbiantNaturalSpawnFixture(map, out fixture, out var fixtureError) ? DescribeSymbiantNaturalSpawnFixture(fixture) : fixtureError;

			var planBefore = ZombieSymbiant.DebugNaturalSpawnPlan(map);
			var expectedCanSpawn = ZombieSymbiant.CanNaturalSpawnNow(map);
			var beforeLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
				.ToHashSet();
			var originalShowLetters = ZombieSettings.Values.showZombieEventLetters;
			ZombieSymbiant spawned = null;
			var trySpawnResult = false;
			object spawnError = null;

			if (spawn)
			{
				try
				{
					ZombieSettings.Values.showZombieEventLetters = true;
					trySpawnResult = ZombieSymbiant.TrySpawnInBestRoom(map);
					spawned = activeBefore == null ? ZombieSymbiant.ActiveSymbiant(map) : null;
				}
				catch (Exception ex)
				{
					spawnError = ex.ToString();
				}
				finally
				{
					ZombieSettings.Values.showZombieEventLetters = originalShowLetters;
				}
			}

			var newLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
				.Where(letter => beforeLetters.Contains(letter) == false)
				.ToArray();
			var letters = newLetters.Select(DescribeSymbiantDiscoveryLetter).ToArray();
			var host = spawned?.LinkedHost;
			var spawnedRoom = spawned?.Spawned == true ? spawned.Position.GetRoom(map) : null;
			var spawnedRoomInfo = spawnedRoom == null ? null : new
			{
				role = spawnedRoom.Role?.defName,
				roleLabel = spawnedRoom.Role?.LabelCap.ToString(),
				cellCount = spawnedRoom.CellCount
			};
			var spawnedInFixtureRoom = fixture?.room.interiorRect.Contains(spawned?.Position ?? IntVec3.Invalid) == true;
			var cleanupResult = activeBefore == null ? CleanupTemporarySymbiant(map, spawned, cleanup) : new { requested = cleanup, cleaned = false, reason = "Existing active symbiant was present before the contract." };
			var letterCleanup = CleanupTemporaryLetters(newLetters, cleanup);
			var activeAfterCleanup = ZombieSymbiant.ActiveSymbiant(map);
			var fixtureCleanup = CleanupSymbiantNaturalSpawnFixture(map, fixture, cleanup);
			var activeAfterFixtureCleanup = ZombieSymbiant.ActiveSymbiant(map);

			var success = spawn == false
				? true
				: expectedCanSpawn
					? spawnError == null && trySpawnResult && spawned != null && host != null && newLetters.Any(letter => letter?.def == CustomDefs.SymbiantConnection) && (setupFixture == false || spawnedInFixtureRoom)
					: spawnError == null && trySpawnResult == false && spawned == null && activeAfterFixtureCleanup == activeBefore;

			return new
			{
				success,
				sourcePath = "ZombieSymbiant.TrySpawnInBestRoom -> BestSpawnRoom -> TryFindBestSpawnCell -> ZombieSymbiant.Spawn",
				spawnRequested = spawn,
				setupFixture,
				expectedCanSpawn,
				trySpawnResult,
				spawnError,
				activeSymbiantBefore = activeBeforeId,
				activeSymbiantAfterCleanup = ZombieRuntimeActions.StableThingId(activeAfterFixtureCleanup),
				restoredExistingActive = activeBefore == null
					? activeAfterFixtureCleanup == null || cleanup == false
					: activeAfterFixtureCleanup == activeBefore,
				initialPlan,
				fixtureSetup,
				planBefore,
				spawned = spawned == null ? null : new
				{
					id = ZombieRuntimeActions.StableThingId(spawned),
					spawned = spawned.Spawned,
					destroyed = spawned.Destroyed,
					cellCount = spawned.CellCount,
					position = spawned.Spawned ? ZombieRuntimeActions.DescribeCell(spawned.Position) : null,
					room = spawnedRoomInfo,
					host = host == null ? null : new
					{
						id = ZombieRuntimeActions.StableThingId(host),
						label = host.LabelShortCap,
						position = host.Spawned ? ZombieRuntimeActions.DescribeCell(host.Position) : null,
						hasSymbiosisHediff = host.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) != null
					}
				},
				spawnedInFixtureRoom,
				newLetterCount = newLetters.Length,
				matchingLetterCount = newLetters.Count(letter => letter?.def == CustomDefs.SymbiantConnection),
				letters,
				cleanup = cleanupResult,
				letterCleanup,
				fixtureCleanup,
				planAfter = ZombieSymbiant.DebugNaturalSpawnPlan(map)
			};
		}

		sealed class SymbiantNaturalSpawnFixture
		{
			public FogRoomFixture room;
			public CellRect fixtureRect;
			public Building_Bed bed;
			public Pawn host;
			public readonly Dictionary<IntVec3, bool> originalHome = new();
			public readonly Dictionary<IntVec3, RoofDef> originalRoof = new();
		}

		static bool TrySetupSymbiantNaturalSpawnFixture(Map map, out SymbiantNaturalSpawnFixture fixture, out object error)
		{
			fixture = null;
			error = null;
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryBuildFogRoomFixture(map, root, 48f, out var room, out error) == false)
				return false;

			var fixtureRect = CellRect.FromLimits(room.interiorRect.minX - 1, room.interiorRect.minZ - 1, room.interiorRect.maxX + 1, room.interiorRect.maxZ + 1).ClipInsideMap(map);
			fixture = new SymbiantNaturalSpawnFixture
			{
				room = room,
				fixtureRect = fixtureRect
			};

			foreach (var cell in fixtureRect.Cells)
			{
				fixture.originalHome[cell] = map.areaManager.Home[cell];
				fixture.originalRoof[cell] = map.roofGrid.RoofAt(cell);
				map.areaManager.Home[cell] = true;
			}
			foreach (var cell in room.interiorRect.ClipInsideMap(map).Cells)
				map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);

			var bedCell = room.interiorRect.CenterCell;
			var bed = ThingMaker.MakeThing(ThingDefOf.Bed, GenStuff.DefaultStuffFor(ThingDefOf.Bed)) as Building_Bed;
			if (bed == null)
			{
				error = new { success = false, error = "Could not create a bed for the symbiant natural-spawn fixture." };
				return false;
			}
			bed.SetFactionDirect(Faction.OfPlayer);
			GenSpawn.Spawn(bed, bedCell, map, Rot4.North, WipeMode.Vanish, false);
			fixture.bed = bed;

			var hostCell = room.interiorRect.Cells
				.Where(cell => cell.InBounds(map)
					&& cell.Standable(map)
					&& cell.GetEdifice(map) == null
					&& cell.GetThingList(map).Any(thing => thing is Pawn || thing.def.category == ThingCategory.Building) == false)
				.OrderByDescending(cell => cell.DistanceToSquared(bedCell))
				.FirstOrDefault();
			if (hostCell.IsValid == false)
			{
				error = new { success = false, error = "Could not find a clear host cell in the symbiant natural-spawn fixture." };
				return false;
			}

			var host = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(host, hostCell, map, Rot4.South);
			DisablePawnWork(host);
			host.needs?.AddOrRemoveNeedsAsAppropriate();
			host.mindState?.mentalStateHandler?.Reset();
			fixture.host = host;
			bed.CompAssignableToPawn?.TryAssignPawn(host);
			bed.NotifyRoomAssignedPawnsChanged();

			map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
			return true;
		}

		static object DescribeSymbiantNaturalSpawnFixture(SymbiantNaturalSpawnFixture fixture)
		{
			if (fixture == null)
				return null;
			var room = fixture.bed?.GetRoom(RegionType.Set_All) ?? fixture.room.interiorRect.CenterCell.GetRoom(fixture.bed?.Map);
			return new
			{
				success = true,
				fixtureRect = ZombieRuntimeActions.DescribeCellRect(fixture.fixtureRect),
				interiorRect = ZombieRuntimeActions.DescribeCellRect(fixture.room.interiorRect),
				bed = ZombieRuntimeActions.StableThingId(fixture.bed),
				bedCell = fixture.bed?.Spawned == true ? ZombieRuntimeActions.DescribeCell(fixture.bed.Position) : null,
				host = ZombieRuntimeActions.StableThingId(fixture.host),
				hostLabel = fixture.host?.LabelShortCap,
				hostCell = fixture.host?.Spawned == true ? ZombieRuntimeActions.DescribeCell(fixture.host.Position) : null,
				room = room == null ? null : new
				{
					role = room.Role?.defName,
					roleLabel = room.Role?.LabelCap.ToString(),
					cellCount = room.CellCount,
					isHuge = room.IsHuge,
					properRoom = room.ProperRoom,
					usesOutdoorTemperature = room.UsesOutdoorTemperature
				}
			};
		}

		static object CleanupSymbiantNaturalSpawnFixture(Map map, SymbiantNaturalSpawnFixture fixture, bool cleanup)
		{
			if (fixture == null)
				return new { removed = 0, restoredCells = 0, skipped = cleanup == false };
			if (cleanup == false)
				return new { removed = 0, restoredCells = 0, skipped = true };

			var removed = 0;
			if (fixture.host != null && fixture.host.Destroyed == false)
			{
				var removedHost = false;
				var corpse = fixture.host.Corpse
					?? map?.listerThings?.ThingsInGroup(ThingRequestGroup.Corpse)?.OfType<Corpse>().FirstOrDefault(corpse => corpse.InnerPawn == fixture.host);
				if (corpse != null && corpse.Destroyed == false)
				{
					corpse.Destroy(DestroyMode.Vanish);
					removedHost = true;
				}
				else if (fixture.host.Dead == false)
				{
					fixture.host.Destroy(DestroyMode.Vanish);
					removedHost = true;
				}
				if (removedHost)
					removed++;
			}

			foreach (var thing in fixture.fixtureRect.Cells
				.SelectMany(cell => cell.GetThingList(map))
				.Where(thing => thing is Building)
				.Distinct()
				.ToArray())
			{
				if (thing.Destroyed)
					continue;
				thing.Destroy(DestroyMode.Vanish);
				removed++;
			}

			foreach (var pair in fixture.originalHome)
				map.areaManager.Home[pair.Key] = pair.Value;
			foreach (var pair in fixture.originalRoof)
				map.roofGrid.SetRoof(pair.Key, pair.Value);
			map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
			return new { removed, restoredCells = fixture.originalHome.Count, skipped = false };
		}

		[Tool("zombieland/symbiant_feeding_contract", Description = "Verify corpse-only symbiant feeding pulse sizes and growth behavior.")]
		public static object SymbiantFeedingContract(
			[ToolParameter(Description = "Destroy temporary symbiant, host, feed corpses, fixture buildings, and letters after capturing evidence.", Required = false, DefaultValue = true)] bool cleanup = true)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded." };
			var activeBefore = ZombieSymbiant.ActiveSymbiant(map);
			if (activeBefore != null)
				return new { success = false, error = "An active symbiant already exists on the current map.", activeSymbiant = ZombieRuntimeActions.StableThingId(activeBefore) };

			var settingsSnapshot = SnapshotZombieSettings();
			var beforeLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>()).ToHashSet();
			var spawnedThings = new List<Thing>();
			SymbiantNaturalSpawnFixture fixture = null;
			ZombieSymbiant symbiant = null;
			object fixtureSetup = null;
			object humanCorpseFeed = null;
			object animalCorpseFeed = null;
			object cleanupSymbiant = null;
			object fixtureCleanup = null;
			object error = null;
			float feedingGrowthSpeedFactor = 0f;

			try
			{
				ApplyZombieSettingsOverride(settings =>
				{
					settings.showZombieEventLetters = false;
					settings.threatScale = 1f;
					settings.symbiantMaxCells = Math.Max(settings.symbiantMaxCells, 400);
				});
				feedingGrowthSpeedFactor = ZombieSymbiant.CurrentGrowthSpeedFactor;

				if (TrySetupSymbiantNaturalSpawnFixture(map, out fixture, out var fixtureError) == false)
					return fixtureError;
				fixtureSetup = DescribeSymbiantNaturalSpawnFixture(fixture);
				symbiant = SpawnAssignedSymbiantForSeveranceContract(map, fixture);

				if (TryFindClearSpawnCell(map, symbiant.Position + new IntVec3(2, 0, 0), 16f, out var humanCorpseCell, out var humanCellError) == false)
					return humanCellError;
				if (TryCreateSymbiantFeedCorpse(map, humanCorpseCell, true, "ZL_SymbiantFeed_Human", spawnedThings, out var humanCorpse, out var humanCorpseError) == false)
					return humanCorpseError;
				humanCorpseFeed = FeedSymbiantThing(symbiant, humanCorpse, "fresh humanlike corpse", 6);

				if (TryFindClearSpawnCell(map, symbiant.Position + new IntVec3(4, 0, 0), 16f, out var animalCorpseCell, out var animalCellError) == false)
					return animalCellError;
				if (TryCreateSymbiantFeedCorpse(map, animalCorpseCell, false, "ZL_SymbiantFeed_Animal", spawnedThings, out var animalCorpse, out var animalCorpseError) == false)
					return animalCorpseError;
				animalCorpseFeed = FeedSymbiantThing(symbiant, animalCorpse, "fresh animal corpse", 4);
			}
			catch (Exception ex)
			{
				error = ex.ToString();
			}
			finally
			{
				cleanupSymbiant = CleanupTemporarySymbiant(map, symbiant, cleanup);
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					if (cleanup)
						thing.Destroy(DestroyMode.Vanish);
				fixtureCleanup = CleanupSymbiantNaturalSpawnFixture(map, fixture, cleanup);
				RestoreZombieSettings(settingsSnapshot);
			}

			var newLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
				.Where(letter => beforeLetters.Contains(letter) == false)
				.ToArray();
			var letterCleanup = CleanupTemporaryLetters(newLetters, cleanup);
			var activeAfterCleanup = ZombieSymbiant.ActiveSymbiant(map);
			var success = error == null
				&& ScenarioSucceeded(humanCorpseFeed)
				&& ScenarioSucceeded(animalCorpseFeed)
				&& (activeAfterCleanup == null || cleanup == false);

			return new
			{
				success,
				sourcePath = "ZombieSymbiant.TryFeed -> FeedGrowthCells -> TryExpansionPulse",
				growthSpeedFactor = feedingGrowthSpeedFactor,
				error,
				fixtureSetup,
				humanCorpseFeed,
				animalCorpseFeed,
				cleanup = new
				{
					symbiant = cleanupSymbiant,
					fixture = fixtureCleanup,
					letters = letterCleanup,
					activeSymbiantAfterCleanup = ZombieRuntimeActions.StableThingId(activeAfterCleanup)
				}
			};
		}

		static bool TryCreateSymbiantFeedCorpse(Map map, IntVec3 cell, bool humanlike, string pawnName, List<Thing> spawnedThings, out Corpse corpse, out object error)
		{
			corpse = null;
			error = null;
			Pawn pawn = null;
			try
			{
				if (humanlike)
					pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				else
				{
					var kindDef = DefDatabase<PawnKindDef>.GetNamed("Warg", false)
						?? DefDatabase<PawnKindDef>.GetNamed("Husky", false)
						?? DefDatabase<PawnKindDef>.AllDefs.FirstOrDefault(def => def.RaceProps?.Animal == true && def.RaceProps.IsFlesh);
					if (kindDef == null)
					{
						error = new { success = false, error = "Could not find an animal pawn kind for the symbiant feed fixture." };
						return false;
					}
					pawn = PawnGenerator.GeneratePawn(kindDef, Faction.OfPlayer);
				}
				pawn.Name = new NameSingle(pawnName);
				GenSpawn.Spawn(pawn, cell, map, Rot4.South);
				DisablePawnWork(pawn);
				if (ZombieRuntimeActions.KillPawnToCorpse(pawn, out corpse, out var corpseError) == false)
				{
					error = new { success = false, error = corpseError, pawn = DescribePawn(pawn) };
					return false;
				}
				spawnedThings?.Add(corpse);
				return true;
			}
			catch (Exception ex)
			{
				error = new { success = false, error = ex.ToString() };
				return false;
			}
			finally
			{
				if (corpse == null && pawn != null && pawn.Destroyed == false)
					pawn.Destroy(DestroyMode.Vanish);
			}
		}

		sealed class SymbiantFeedStep
		{
			public string label { get; set; }
			public string feed { get; set; }
			public string feedDef { get; set; }
			public string rotStage { get; set; }
			public bool validBefore { get; set; }
			public int expectedGrowth { get; set; }
			public int predictedGrowth { get; set; }
			public int beforeTick { get; set; }
			public int beforeCells { get; set; }
			public int afterCells { get; set; }
			public int addedCells { get; set; }
			public int reportedGrowthCells { get; set; }
			public bool fed { get; set; }
			public bool feedDestroyed { get; set; }
			public bool success { get; set; }
		}

		static SymbiantFeedStep FeedSymbiantThing(ZombieSymbiant symbiant, Thing feed, string label, int expectedGrowth)
		{
			var beforeTick = GenTicks.TicksGame;
			var beforeCells = symbiant?.CellCount ?? 0;
			var validBefore = ZombieSymbiant.IsValidFeed(feed);
			var predictedGrowth = ZombieSymbiant.FeedGrowthCellCount(feed);
			var fed = symbiant?.TryFeed(feed) == true;
			if (fed == false && feed?.Destroyed == false)
				feed.Destroy(DestroyMode.Vanish);
			var afterCells = symbiant?.Destroyed == true ? 0 : symbiant?.CellCount ?? 0;
			var addedCells = afterCells - beforeCells;
			var reportedGrowthCells = symbiant?.LastRecessionPulseCells ?? 0;
			var success = fed
				&& validBefore
				&& predictedGrowth == expectedGrowth
				&& addedCells == expectedGrowth
				&& reportedGrowthCells == expectedGrowth
				&& feed?.Destroyed == true;
			return new SymbiantFeedStep
			{
				label = label,
				feed = ZombieRuntimeActions.StableThingId(feed),
				feedDef = feed?.def?.defName,
				rotStage = (feed as Corpse)?.GetRotStage().ToString(),
				validBefore = validBefore,
				expectedGrowth = expectedGrowth,
				predictedGrowth = predictedGrowth,
				beforeTick = beforeTick,
				beforeCells = beforeCells,
				afterCells = afterCells,
				addedCells = addedCells,
				reportedGrowthCells = reportedGrowthCells,
				fed = fed,
				feedDestroyed = feed?.Destroyed ?? false,
				success = success
			};
		}

		static object DescribeSymbiantFeedingState(ZombieSymbiant symbiant)
		{
			if (symbiant == null)
				return null;
			return new
			{
				cellCount = symbiant.CellCount,
				lastFeedGrowthCells = symbiant.LastRecessionPulseCells
			};
		}

		[Tool("zombieland/symbiant_settings_contract", Description = "Verify symbiant enable/disable and max-cell setting edges without deleting or growing the active symbiant unexpectedly.")]
		public static object SymbiantSettingsContract(
			[ToolParameter(Description = "Destroy temporary symbiant, host, fixture buildings, and letters after capturing evidence.", Required = false, DefaultValue = true)] bool cleanup = true)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded." };
			var activeBefore = ZombieSymbiant.ActiveSymbiant(map);
			if (activeBefore != null)
				return new { success = false, error = "An active symbiant already exists on the current map.", activeSymbiant = ZombieRuntimeActions.StableThingId(activeBefore) };

			var settingsSnapshot = SnapshotZombieSettings();
			var beforeLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>()).ToHashSet();
			SymbiantNaturalSpawnFixture fixture = null;
			ZombieSymbiant symbiant = null;
			object fixtureSetup = null;
			object disabledBeforeSpawn = null;
			object enabledBeforeSpawn = null;
			object disabledWithActive = null;
			object loweredCap = null;
			object error = null;

			try
			{
				ApplyZombieSettingsOverride(settings =>
				{
					settings.showZombieEventLetters = false;
					settings.symbiantEnabled = true;
					settings.symbiantMaxCells = 40;
				});

				if (TrySetupSymbiantNaturalSpawnFixture(map, out fixture, out var fixtureError) == false)
					return fixtureError;
				fixtureSetup = DescribeSymbiantNaturalSpawnFixture(fixture);

				ApplyZombieSettingsOverride(settings => settings.symbiantEnabled = false);
				var disabledPlan = ZombieSymbiant.DebugNaturalSpawnPlan(map);
				disabledBeforeSpawn = new
				{
					enabled = ZombieSettings.Values.symbiantEnabled,
					canNaturalSpawnNow = ZombieSymbiant.CanNaturalSpawnNow(map),
					plan = disabledPlan,
					success = ZombieSettings.Values.symbiantEnabled == false && ZombieSymbiant.CanNaturalSpawnNow(map) == false
				};

				ApplyZombieSettingsOverride(settings => settings.symbiantEnabled = true);
				var enabledPlan = ZombieSymbiant.DebugNaturalSpawnPlan(map);
				enabledBeforeSpawn = new
				{
					enabled = ZombieSettings.Values.symbiantEnabled,
					canNaturalSpawnNow = ZombieSymbiant.CanNaturalSpawnNow(map),
					plan = enabledPlan,
					success = ZombieSettings.Values.symbiantEnabled && ZombieSymbiant.CanNaturalSpawnNow(map)
				};

				symbiant = SpawnAssignedSymbiantForSeveranceContract(map, fixture);
				var activeAfterSpawn = ZombieSymbiant.ActiveSymbiant(map);
				var activeId = ZombieRuntimeActions.StableThingId(activeAfterSpawn);
				ApplyZombieSettingsOverride(settings => settings.symbiantEnabled = false);
				disabledWithActive = new
				{
					enabled = ZombieSettings.Values.symbiantEnabled,
					activeSymbiant = activeId,
					activeStillExists = activeAfterSpawn != null && activeAfterSpawn.Destroyed == false && ZombieSymbiant.ActiveSymbiant(map) == activeAfterSpawn,
					canNaturalSpawnNow = ZombieSymbiant.CanNaturalSpawnNow(map),
					success = ZombieSettings.Values.symbiantEnabled == false
						&& activeAfterSpawn != null
						&& activeAfterSpawn.Destroyed == false
						&& ZombieSymbiant.ActiveSymbiant(map) == activeAfterSpawn
						&& ZombieSymbiant.CanNaturalSpawnNow(map) == false
				};

				ApplyZombieSettingsOverride(settings =>
				{
					settings.symbiantEnabled = true;
					settings.symbiantMaxCells = 40;
				});
				var targetCells = fixture.room.interiorRect.Cells
					.Where(cell => cell.InBounds(map) && cell.Standable(map))
					.ToArray();
				var addedCells = ZombieSymbiant.AddCells(map, targetCells);
				var cellsBeforeLower = symbiant.CellCount;
				var loweredMax = Mathf.Max(1, cellsBeforeLower - 1);
				ApplyZombieSettingsOverride(settings => settings.symbiantMaxCells = loweredMax);
				var effectiveMaxAfterLower = ZombieSymbiant.MaxCells;
				var pulse = symbiant.TryExpansionPulse();
				var cellsAfterPulse = symbiant.CellCount;
				loweredCap = new
				{
					addedCells,
					cellsBeforeLower,
					requestedMax = loweredMax,
					effectiveMaxAfterLower,
					pulse,
					cellsAfterPulse,
					activeStillExists = symbiant.Destroyed == false && ZombieSymbiant.ActiveSymbiant(map) == symbiant,
					success = addedCells > 0
						&& cellsBeforeLower > effectiveMaxAfterLower
						&& pulse == false
						&& cellsAfterPulse == cellsBeforeLower
						&& symbiant.Destroyed == false
						&& ZombieSymbiant.ActiveSymbiant(map) == symbiant
				};
			}
			catch (Exception ex)
			{
				error = ex.ToString();
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
			}

			var newLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
				.Where(letter => beforeLetters.Contains(letter) == false)
				.ToArray();
			var cleanupResult = CleanupTemporarySymbiant(map, symbiant, cleanup);
			var fixtureCleanup = CleanupSymbiantNaturalSpawnFixture(map, fixture, cleanup);
			var letterCleanup = CleanupTemporaryLetters(newLetters, cleanup);
			var activeAfterCleanup = ZombieSymbiant.ActiveSymbiant(map);
			var success = error == null
				&& ScenarioSucceeded(disabledBeforeSpawn)
				&& ScenarioSucceeded(enabledBeforeSpawn)
				&& ScenarioSucceeded(disabledWithActive)
				&& ScenarioSucceeded(loweredCap)
				&& (activeAfterCleanup == null || cleanup == false);

			return new
			{
				success,
				sourcePath = "ZombieSymbiant.CanNaturalSpawnNow/TryExpansionPulse + symbiantEnabled/symbiantMaxCells settings",
				error,
				fixtureSetup,
				disabledBeforeSpawn,
				enabledBeforeSpawn,
				disabledWithActive,
				loweredCap,
				cleanup = new
				{
					symbiant = cleanupResult,
					fixture = fixtureCleanup,
					letters = letterCleanup,
					activeSymbiantAfterCleanup = ZombieRuntimeActions.StableThingId(activeAfterCleanup)
				}
			};
		}

		static bool ScenarioSucceeded(object scenario)
		{
			if (scenario == null)
				return false;
			var property = scenario.GetType().GetProperty("success");
			return property?.GetValue(scenario) is bool success && success;
		}

		[Tool("zombieland/symbiant_map_cache_contract", Description = "Verify active/empty symbiant map-cache invalidation across empty probes, spawn, cleanup, and explicit cache reset.")]
		public static object SymbiantMapCacheContract(
			[ToolParameter(Description = "Destroy the temporary contract symbiant and letters after capturing evidence.", Required = false, DefaultValue = true)] bool cleanup = true)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded." };

			var activeBefore = ZombieSymbiant.ActiveSymbiant(map);
			if (activeBefore != null)
				return new { success = false, error = "An active symbiant already exists on the current map.", activeSymbiant = ZombieRuntimeActions.StableThingId(activeBefore) };

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 18f, out var cell, out var cellError) == false)
				return cellError;

			var settingsSnapshot = SnapshotZombieSettings();
			var beforeLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>()).ToHashSet();
			ZombieSymbiant spawned = null;
			object emptyProbe = null;
			object cacheAfterEmptyProbe = null;
			object spawnStep = null;
			object cleanupStep = null;
			object cacheAfterCleanupProbe = null;
			object cacheAfterExplicitReset = null;
			object resetProbe = null;
			object error = null;

			try
			{
				emptyProbe = new
				{
					active = ZombieRuntimeActions.StableThingId(ZombieSymbiant.ActiveSymbiant(map))
				};
				cacheAfterEmptyProbe = ZombieSymbiant.DebugCacheState(map);

				ApplyZombieSettingsOverride(settings => settings.showZombieEventLetters = false);
				try
				{
					ZombieSymbiant.Spawn(map, cell);
					spawned = ZombieSymbiant.ActiveSymbiant(map);
				}
				catch (Exception ex)
				{
					error = ex.ToString();
				}

				var cacheAfterSpawn = ZombieSymbiant.DebugCacheState(map);
				spawnStep = new
				{
					error,
					cell = ZombieRuntimeActions.DescribeCell(cell),
					active = ZombieRuntimeActions.StableThingId(spawned),
					spawned = spawned?.Spawned == true,
					destroyed = spawned?.Destroyed ?? false,
					cellCount = spawned?.CellCount ?? 0,
					registeredInMapPawnLists = spawned?.RegisteredInMapPawnLists ?? false,
					cache = cacheAfterSpawn,
					success = error == null
						&& spawned?.Spawned == true
						&& spawned.Destroyed == false
						&& spawned.CellCount == 1
						&& spawned.RegisteredInMapPawnLists == false
				};

				if (cleanup)
					spawned?.DebugDestroyWithoutHostTrauma();
				var activeAfterCleanup = ZombieSymbiant.ActiveSymbiant(map);
				cacheAfterCleanupProbe = ZombieSymbiant.DebugCacheState(map);
				cleanupStep = new
				{
					requested = cleanup,
					spawnedDestroyed = spawned?.Destroyed ?? false,
					active = ZombieRuntimeActions.StableThingId(activeAfterCleanup),
					cache = cacheAfterCleanupProbe,
					success = cleanup == false || (spawned?.Destroyed == true && activeAfterCleanup == null)
				};

				ZombieSymbiant.ClearActiveSymbiantCaches();
				cacheAfterExplicitReset = ZombieSymbiant.DebugCacheState(map);
				resetProbe = new
				{
					active = ZombieRuntimeActions.StableThingId(ZombieSymbiant.ActiveSymbiant(map)),
					cache = ZombieSymbiant.DebugCacheState(map)
				};
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
			}

			var newLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
				.Where(letter => beforeLetters.Contains(letter) == false)
				.ToArray();
			var letterCleanup = CleanupTemporaryLetters(newLetters, cleanup);
			if (cleanup)
				spawned?.DebugDestroyWithoutHostTrauma();
			var finalActive = ZombieSymbiant.ActiveSymbiant(map);
			var finalCache = ZombieSymbiant.DebugCacheState(map);
			var success = ScenarioSucceeded(spawnStep)
				&& ScenarioSucceeded(cleanupStep)
				&& (cleanup == false || finalActive == null);

			return new
			{
				success,
				sourcePath = "ZombieSymbiant.ActiveSymbiant/RegisterActiveSymbiant/DebugDestroyWithoutHostTrauma/ClearActiveSymbiantCaches",
				emptyProbe,
				cacheAfterEmptyProbe,
				spawnStep,
				cleanupStep,
				cacheAfterCleanupProbe,
				cacheAfterExplicitReset,
				resetProbe,
				letterCleanup,
				final = new
				{
					activeSymbiant = ZombieRuntimeActions.StableThingId(finalActive),
					cache = finalCache
				}
			};
		}

		[Tool("zombieland/symbiant_host_availability_contract", Description = "Verify a linked host temporarily leaving the map preserves the link while disabling benefits and surgery until the host returns.")]
		public static object SymbiantHostAvailabilityContract(
			[ToolParameter(Description = "Destroy temporary symbiant, host, fixture buildings, and letters after capturing evidence.", Required = false, DefaultValue = true)] bool cleanup = true)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded." };
			var activeBefore = ZombieSymbiant.ActiveSymbiant(map);
			if (activeBefore != null)
				return new { success = false, error = "An active symbiant already exists on the current map.", activeSymbiant = ZombieRuntimeActions.StableThingId(activeBefore) };

			var settingsSnapshot = SnapshotZombieSettings();
			var beforeLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>()).ToHashSet();
			SymbiantNaturalSpawnFixture fixture = null;
			ZombieSymbiant symbiant = null;
			object fixtureSetup = null;
			object beforeLeave = null;
			object offMap = null;
			object afterReturn = null;
			object contained = null;
			object afterEject = null;
			object casketEvidence = null;
			object error = null;

			try
			{
				ApplyZombieSettingsOverride(settings =>
				{
					settings.showZombieEventLetters = false;
					settings.symbiantMaxCells = Math.Max(settings.symbiantMaxCells, 80);
				});

				if (TrySetupSymbiantNaturalSpawnFixture(map, out fixture, out var fixtureError) == false)
					return fixtureError;
				fixtureSetup = DescribeSymbiantNaturalSpawnFixture(fixture);
				symbiant = SpawnAssignedSymbiantForSeveranceContract(map, fixture);
				var host = fixture.host;
				beforeLeave = DescribeHostAvailabilityState("beforeLeave", map, symbiant, host);

				var leavePosition = host.Position;
				host.DeSpawn(DestroyMode.Vanish);
				AccessTools.Method(typeof(ZombieSymbiant), "EnsureHostLink")?.Invoke(symbiant, null);
				offMap = DescribeHostAvailabilityState("offMap", map, symbiant, host);

				if (TryFindClearSpawnCell(map, leavePosition, 12f, out var returnCell, out var returnError) == false)
					return returnError;
				GenSpawn.Spawn(host, returnCell, map, Rot4.Random, WipeMode.Vanish);
				AccessTools.Method(typeof(ZombieSymbiant), "EnsureHostLink")?.Invoke(symbiant, null);
				afterReturn = DescribeHostAvailabilityState("afterReturn", map, symbiant, host);

				var casketDef = DefDatabase<ThingDef>.GetNamedSilentFail("CryptosleepCasket");
				if (casketDef == null)
				{
					casketEvidence = new { success = false, error = "CryptosleepCasket ThingDef is missing." };
				}
				else if (TryFindClearBuildingCell(map, host.Position, 14f, out var casketCell, out var casketCellError) == false)
				{
					casketEvidence = casketCellError;
				}
				else
				{
					var casket = GenSpawn.Spawn(ThingMaker.MakeThing(casketDef), casketCell, map, Rot4.North, WipeMode.Vanish) as Building_CryptosleepCasket;
					if (casket == null)
					{
						casketEvidence = new { success = false, error = "CryptosleepCasket did not spawn as Building_CryptosleepCasket.", cell = ZombieRuntimeActions.DescribeCell(casketCell) };
					}
					else
					{
						var hostSpawnedBeforeCasket = host.Spawned;
						_ = host.DeSpawnOrDeselect(DestroyMode.Vanish);
						var hostSpawnedAfterCasketDespawn = host.Spawned;
						var accepted = casket.TryAcceptThing(host, false);
						AccessTools.Method(typeof(ZombieSymbiant), "EnsureHostLink")?.Invoke(symbiant, null);
						contained = DescribeHostAvailabilityState("containedInCryptosleep", map, symbiant, host);
						casket.EjectContents();
						AccessTools.Method(typeof(ZombieSymbiant), "EnsureHostLink")?.Invoke(symbiant, null);
						afterEject = DescribeHostAvailabilityState("afterCryptosleepEject", map, symbiant, host);
						casketEvidence = new
						{
							success = hostSpawnedBeforeCasket
								&& hostSpawnedAfterCasketDespawn == false
								&& accepted
								&& ScenarioSucceeded(contained)
								&& ScenarioSucceeded(afterEject),
							casket = ZombieRuntimeActions.StableThingId(casket),
							cell = ZombieRuntimeActions.DescribeCell(casketCell),
							hostSpawnedBeforeCasket,
							hostSpawnedAfterCasketDespawn,
							accepted,
							destroyedAfterEject = casket.Destroyed
						};
					}
				}
			}
			catch (Exception ex)
			{
				error = ex.ToString();
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
			}

			var newLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
				.Where(letter => beforeLetters.Contains(letter) == false)
				.ToArray();
			var cleanupResult = CleanupTemporarySymbiant(map, symbiant, cleanup);
			var fixtureCleanup = CleanupSymbiantNaturalSpawnFixture(map, fixture, cleanup);
			var letterCleanup = CleanupTemporaryLetters(newLetters, cleanup);
			var activeAfterCleanup = ZombieSymbiant.ActiveSymbiant(map);
			var success = error == null
				&& ScenarioSucceeded(beforeLeave)
				&& ScenarioSucceeded(offMap)
				&& ScenarioSucceeded(afterReturn)
				&& ScenarioSucceeded(contained)
				&& ScenarioSucceeded(afterEject)
				&& ScenarioSucceeded(casketEvidence)
				&& (activeAfterCleanup == null || cleanup == false);

			return new
			{
				success,
				sourcePath = "ZombieSymbiant.ResolveHost/EnsureHostLink/SymbiantBenefitFactor/CanSeverSymbiosis",
				error,
				fixtureSetup,
				beforeLeave,
				offMap,
				afterReturn,
				contained,
				afterEject,
				casketEvidence,
				cleanup = new
				{
					symbiant = cleanupResult,
					fixture = fixtureCleanup,
					letters = letterCleanup,
					activeSymbiantAfterCleanup = ZombieRuntimeActions.StableThingId(activeAfterCleanup)
				}
			};
		}

		static bool TryFindClearBuildingCell(Map map, IntVec3 root, float radius, out IntVec3 cell, out object error)
		{
			cell = IntVec3.Invalid;
			error = null;
			if (map == null)
			{
				error = new { success = false, error = "No current map is loaded." };
				return false;
			}

			foreach (var candidate in GenRadial.RadialCellsAround(root, radius, true))
			{
				if (candidate.InBounds(map) == false)
					continue;
				if (candidate.Standable(map) == false || candidate.Fogged(map))
					continue;
				if (candidate.GetEdifice(map) != null)
					continue;
				if (candidate.GetThingList(map).Any(thing => thing is Pawn || thing.def.category == ThingCategory.Building))
					continue;
				cell = candidate;
				return true;
			}

			error = new { success = false, error = $"No clear building cell was found near ({root.x}, {root.z})." };
			return false;
		}

		static object DescribeHostAvailabilityState(string label, Map map, ZombieSymbiant symbiant, Pawn host)
		{
			var linkedHost = symbiant?.LinkedHost;
			var linkedForHost = ZombieSymbiant.LinkedSymbiantFor(host);
			var hediff = host?.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) as Hediff_SymbiantSymbiosis;
			var hostSpawned = host?.Spawned == true;
			var hostMapMatches = hostSpawned && host.Map == map;
			var linkPreserved = symbiant != null
				&& symbiant.Destroyed == false
				&& ReferenceEquals(linkedHost, host)
				&& linkedForHost == symbiant
				&& hediff != null
				&& hediff.symbiantThingId == symbiant.ThingID;
			var benefitFactor = ZombieSymbiant.SymbiantBenefitFactor(host);
			var canSever = ZombieSymbiant.CanSeverSymbiosis(host);
			var targetingProtection = ZombieSymbiant.HasZombieTargetingProtection(host);
			var dormant = label == "offMap" || label == "containedInCryptosleep";
			var success = dormant
				? linkPreserved && hostSpawned == false && Mathf.Approximately(benefitFactor, 0f) && canSever == false && targetingProtection == false
				: linkPreserved && hostSpawned && hostMapMatches;

			return new
			{
				success,
				label,
				host = ZombieRuntimeActions.StableThingId(host),
				hostSpawned,
				hostMapMatches,
				hostPosition = hostSpawned ? ZombieRuntimeActions.DescribeCell(host.Position) : null,
				linkedHost = ZombieRuntimeActions.StableThingId(linkedHost),
				linkedForHost = ZombieRuntimeActions.StableThingId(linkedForHost),
				linkPreserved,
				hasHediff = hediff != null,
				hediffSymbiantThingId = hediff?.symbiantThingId,
				benefitFactor,
				canSever,
				targetingProtection,
				symbiant = ZombieRuntimeActions.StableThingId(symbiant),
				symbiantSpawned = symbiant?.Spawned ?? false,
				symbiantDestroyed = symbiant?.Destroyed ?? false
			};
		}

		[Tool("zombieland/symbiant_combat_isolation_contract", Description = "Verify the symbiant Pawn shell stays minimal while hostile enemies can target it and feed jobs can still discover it.")]
		public static object SymbiantCombatIsolationContract(
			[ToolParameter(Description = "Destroy temporary pawns, feed corpse, letter, and symbiant after capturing evidence.", Required = false, DefaultValue = true)] bool cleanup = true)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded." };
			var activeBefore = ZombieSymbiant.ActiveSymbiant(map);
			if (activeBefore != null)
				return new { success = false, error = "An active symbiant already exists on the current map.", activeSymbiant = ZombieRuntimeActions.StableThingId(activeBefore) };

			var hostileFaction = Find.FactionManager?.AllFactionsListForReading?
				.FirstOrDefault(faction => faction != null && faction.HostileTo(Faction.OfPlayer) && faction.def?.humanlikeFaction == true)
				?? Find.FactionManager?.AllFactionsListForReading?
					.FirstOrDefault(faction => faction != null && faction.HostileTo(Faction.OfPlayer));
			if (hostileFaction == null)
				return new { success = false, error = "Could not find a hostile faction for the symbiant combat fixture." };

			var settingsSnapshot = SnapshotZombieSettings();
			var beforeLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>()).ToHashSet();
			var spawnedThings = new List<Thing>();
			ZombieSymbiant symbiant = null;
			Corpse feedCorpse = null;
			object result;

			try
			{
				ApplyZombieSettingsOverride(settings =>
				{
					settings.showZombieEventLetters = false;
					settings.attackMode = AttackMode.Everything;
					settings.enemiesAttackZombies = true;
					settings.animalsAttackZombies = true;
				});

				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindSymbiantCombatFixtureCells(map, root, 6, out var cells, out var cellError) == false)
					return cellError;

				var player = SpawnArmedAreaWorkflowPawn(map, "ZL_SymbiantCombat_Player", cells[0], Faction.OfPlayer, spawnedThings);
				var enemy = SpawnArmedAreaWorkflowPawn(map, "ZL_SymbiantCombat_Enemy", cells[1], hostileFaction, spawnedThings);
				var animal = SpawnAreaWorkflowAnimal(map, "ZL_SymbiantCombat_Animal", cells[2], Faction.OfPlayer, spawnedThings, def => def.combatPower > 0f);
				var predator = SpawnAreaWorkflowAnimal(map, "ZL_SymbiantCombat_Predator", cells[3], Faction.OfPlayer, spawnedThings, def => def.RaceProps?.predator == true || def.combatPower >= 1f);
				ZombieSymbiant.Spawn(map, cells[4]);
				symbiant = ZombieSymbiant.ActiveSymbiant(map);
				if (symbiant != null)
					symbiant.Name = new NameSingle("ZL_SymbiantCombat_Goo");
				if (TryCreateSymbiantFeedCorpse(map, cells[5], true, "ZL_SymbiantCombat_FeedCorpse", spawnedThings, out feedCorpse, out var feedCorpseError) == false)
					return feedCorpseError;

				RefreshZombieTargetCache(map);
				symbiant?.RequestFeed(true);

				var pawnSystems = DescribeSymbiantCombatPawnSystems(map, symbiant, player, enemy);
				var targetFinding = new
				{
					player = DescribeBestSymbiantTarget(player, symbiant, false),
					enemy = DescribeBestSymbiantTarget(enemy, symbiant, true),
					animal = DescribeBestSymbiantTarget(animal, symbiant, false),
					predator = DescribeBestSymbiantTarget(predator, symbiant, false)
				};
				var forcedJobs = new
				{
					playerMelee = VerifySymbiantAttackJob(player, symbiant, JobDefOf.AttackMelee, false),
					playerStatic = VerifySymbiantAttackJob(player, symbiant, JobDefOf.AttackStatic, false),
					enemyMelee = VerifySymbiantAttackJob(enemy, symbiant, JobDefOf.AttackMelee, true),
					symbiantMelee = VerifySymbiantAttackJob(symbiant, player, JobDefOf.AttackMelee, false)
				};
				var animalResponse = new
				{
					manhunterChance = animal == null || symbiant == null ? null : (float?)PawnUtility.GetManhunterOnDamageChance(animal, symbiant, animal.Position.DistanceTo(symbiant.Position)),
					preyScore = predator == null || symbiant == null ? null : (float?)FoodUtility.GetPreyScoreFor(predator, symbiant)
				};
				var feed = WorkGiver_FeedZombieSymbiant.FindClosestFeed(player, symbiant);
				var feedJob = new WorkGiver_FeedZombieSymbiant().JobOnThing(player, symbiant, true);
				var feedDiscovery = new
				{
					feedRequested = symbiant?.FeedRequested ?? false,
					closestFeed = feed == null ? null : ZombieRuntimeActions.StableThingId(feed),
					closestFeedDef = feed?.def?.defName,
					closestFeedIsValid = feed != null && ZombieSymbiant.IsValidFeed(feed),
					foundSpawnedFeedCorpse = feed == feedCorpse,
					jobDef = feedJob?.def?.defName,
					jobTargetA = ZombieRuntimeActions.StableThingId(feedJob?.targetA.Thing),
					jobTargetB = ZombieRuntimeActions.StableThingId(feedJob?.targetB.Thing),
					success = feed != null
						&& ZombieSymbiant.IsValidFeed(feed)
						&& feedJob?.def == CustomDefs.FeedZombieSymbiant
						&& feedJob.targetA.Thing == symbiant
						&& feedJob.targetB.Thing == feed
				};
				var patchTargets = new
				{
					availableShootingTargets = PatchedMethodsForPatchClass("AttackTargetFinder_GetAvailableShootingTargetsByScore_Patch"),
					bestAttackTarget = PatchedMethodsForPatchClass("AttackTargetFinder_BestAttackTarget_Patch"),
					hostileThingThing = PatchedMethodsForPatchClass("GenHostility_HostileTo_Thing_Thing_Patch"),
					hostileThingFaction = PatchedMethodsForPatchClass("GenHostility_HostileTo_Thing_Faction_Patch"),
					activeThreat = PatchedMethodsForPatchClass("GenHostility_IsActiveThreat_Patch"),
					registerTarget = PatchedMethodsForPatchClass("AttackTargetsCache_RegisterTarget_Patch"),
					startJob = PatchedMethodsForPatchClass("Pawn_JobTracker_StartJob_Patch"),
					dangerRating = PatchedMethodsForPatchClass("DangerWatcher_CalculateDangerRating_Patch"),
					flee = PatchedMethodsForPatchClass("FleeUtility_ShouldFleeFrom_Patch"),
					manhunter = PatchedMethodsForPatchClass("PawnUtility_GetManhunterOnDamageChance_Patch"),
					prey = PatchedMethodsForPatchClass("FoodUtility_GetPreyScoreFor_Patch")
				};

				var success = symbiant?.Spawned == true
					&& ScenarioSucceeded(pawnSystems)
					&& ScenarioSucceeded(targetFinding.player)
					&& ScenarioSucceeded(targetFinding.enemy)
					&& ScenarioSucceeded(targetFinding.animal)
					&& ScenarioSucceeded(targetFinding.predator)
					&& ScenarioSucceeded(forcedJobs.playerMelee)
					&& ScenarioSucceeded(forcedJobs.playerStatic)
					&& ScenarioSucceeded(forcedJobs.enemyMelee)
					&& ScenarioSucceeded(forcedJobs.symbiantMelee)
					&& animalResponse.manhunterChance == 0f
					&& animalResponse.preyScore <= -9999f
					&& feedDiscovery.success
					&& patchTargets.bestAttackTarget.Length > 0
					&& patchTargets.hostileThingThing.Length > 0
					&& patchTargets.activeThreat.Length > 0
					&& patchTargets.startJob.Length > 0;

				result = new
				{
					success,
					sourcePath = "Patches_Hostility + AttackTargetsCache + Pawn_JobTracker_StartJob_Patch + WorkGiver_FeedZombieSymbiant",
					fixtureCells = cells.Select(ZombieRuntimeActions.DescribeCell).ToArray(),
					pawns = new
					{
						player = DescribePawn(player),
						enemy = DescribePawn(enemy),
						animal = DescribePawn(animal),
						predator = DescribePawn(predator),
						symbiant = DescribePawn(symbiant)
					},
					pawnSystems,
					targetFinding,
					forcedJobs,
					animalResponse,
					feedDiscovery,
					patchTargets
				};
			}
			catch (Exception ex)
			{
				result = new { success = false, error = ex.ToString() };
			}
			finally
			{
				_ = CleanupTemporarySymbiant(map, symbiant, cleanup);
				if (cleanup)
					foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).Distinct().ToArray())
						thing.Destroy(DestroyMode.Vanish);
				var newLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
					.Where(letter => beforeLetters.Contains(letter) == false)
					.ToArray();
				_ = CleanupTemporaryLetters(newLetters, cleanup);
				RestoreZombieSettings(settingsSnapshot);
			}

			return result;
		}

		static bool TryFindSymbiantCombatFixtureCells(Map map, IntVec3 root, int count, out IntVec3[] cells, out object error)
		{
			cells = GenRadial.RadialCellsAround(root, 24f, true)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.Where(cell => cell.GetEdifice(map) == null)
				.Distinct()
				.Take(count)
				.ToArray();
			error = cells.Length >= count
				? null
				: new { success = false, error = "Could not find enough clear cells for the symbiant combat fixture.", requested = count, found = cells.Length };
			return error == null;
		}

		static object DescribeSymbiantCombatPawnSystems(Map map, ZombieSymbiant symbiant, Pawn player, Pawn enemy)
		{
			var playerFaction = Find.FactionManager?.AllFactionsListForReading?.FirstOrDefault(faction => faction?.def?.isPlayer == true);
			var dangerMethod = AccessTools.Method(typeof(DangerWatcher), "AffectsStoryDanger");
			var danger = dangerMethod != null && symbiant != null && (bool)dangerMethod.Invoke(null, new object[] { symbiant });
			var flee = player != null && symbiant != null && FleeUtility.ShouldFleeFrom(symbiant, player, true, false);
			var targetsHostile = map?.attackTargetsCache?.TargetsHostileToColony?.Contains(symbiant) ?? false;
			var hostileToPlayer = symbiant != null && playerFaction != null && symbiant.HostileTo(playerFaction);
			var playerHostileToSymbiant = player != null && symbiant != null && player.HostileTo(symbiant);
			var enemyHostileToSymbiant = enemy != null && symbiant != null && enemy.HostileTo(symbiant);
			var activeThreatToPlayer = symbiant != null && playerFaction != null && GenHostility.IsActiveThreatTo(symbiant, playerFaction, false, false);
			var activeThreatToEnemy = symbiant != null && enemy?.Faction != null && GenHostility.IsActiveThreatTo(symbiant, enemy.Faction, false, false);
			var symbiantHostileToEnemyFaction = symbiant != null && enemy?.Faction != null && symbiant.HostileTo(enemy.Faction);
			var success = symbiant != null
				&& symbiant.RegisteredInMapPawnLists
				&& targetsHostile == false
				&& hostileToPlayer == false
				&& playerHostileToSymbiant == false
				&& enemyHostileToSymbiant
				&& activeThreatToPlayer == false
				&& activeThreatToEnemy
				&& symbiantHostileToEnemyFaction
				&& danger == false
				&& flee == false
				&& symbiant.kindDef?.isFighter == false
				&& Mathf.Approximately(symbiant.kindDef?.combatPower ?? 0f, 0f);
			return new
			{
				success,
				registeredInMapPawnLists = symbiant?.RegisteredInMapPawnLists ?? false,
				attackTargetsHostileToColony = targetsHostile,
				hostileToPlayer,
				symbiantHostileToEnemyFaction,
				playerHostileToSymbiant,
				enemyHostileToSymbiant,
				activeThreatToPlayer,
				activeThreatToEnemy,
				affectsStoryDanger = danger,
				shouldFleeFrom = flee,
				kindIsFighter = symbiant?.kindDef?.isFighter ?? false,
				combatPower = symbiant?.kindDef?.combatPower ?? 0f
			};
		}

		static object DescribeBestSymbiantTarget(Pawn searcher, ZombieSymbiant symbiant, bool expectTarget)
		{
			var target = searcher == null || symbiant == null
				? null
				: AttackTargetFinder.BestAttackTarget(searcher, TargetScanFlags.NeedThreat, thing => thing == symbiant, 0f, 999f);
			var foundTarget = target?.Thing == symbiant;
			return new
			{
				success = foundTarget == expectTarget,
				expectTarget,
				searcher = ZombieRuntimeActions.StableThingId(searcher),
				searcherDef = searcher?.def?.defName,
				searcherKind = searcher?.kindDef?.defName,
				currentVerb = searcher?.CurrentEffectiveVerb?.ToString(),
				target = DescribeTarget(target)
			};
		}

		static object VerifySymbiantAttackJob(Pawn actor, Thing target, JobDef jobDef, bool expectAccepted)
		{
			if (actor == null || target == null || jobDef == null)
				return new { success = false, error = "Missing actor, target, or jobDef.", actor = ZombieRuntimeActions.StableThingId(actor), target = ZombieRuntimeActions.StableThingId(target), jobDef = jobDef?.defName };
			var beforeJob = actor.CurJob;
			var beforeJobDef = beforeJob?.def?.defName;
			var beforeTarget = ZombieRuntimeActions.StableThingId(beforeJob?.targetA.Thing);
			var job = JobMaker.MakeJob(jobDef, target);
			actor.jobs.StartJob(job, JobCondition.InterruptForced, null, false, true);
			var afterJob = actor.CurJob;
			var accepted = afterJob != null && afterJob.def == jobDef && afterJob.targetA.Thing == target;
			actor.jobs.StopAll(false, true);
			return new
			{
				success = accepted == expectAccepted,
				expectAccepted,
				actor = ZombieRuntimeActions.StableThingId(actor),
				target = ZombieRuntimeActions.StableThingId(target),
				jobDef = jobDef.defName,
				beforeJobDef,
				beforeTarget,
				afterJobDef = afterJob?.def?.defName,
				afterTarget = ZombieRuntimeActions.StableThingId(afterJob?.targetA.Thing),
				accepted
			};
		}

		[Tool("zombieland/symbiant_severance_contract", Description = "Verify severance surgery visibility, zombie-extract ingredients, extract consumption, and bond removal.")]
		public static object SymbiantSeveranceContract(
			[ToolParameter(Description = "Destroy temporary symbiants, colonists, fixture buildings, and letters after capturing evidence.", Required = false, DefaultValue = true)] bool cleanup = true)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded." };
			var activeBefore = ZombieSymbiant.ActiveSymbiant(map);
			if (activeBefore != null)
				return new { success = false, error = "An active symbiant already exists on the current map.", activeSymbiant = ZombieRuntimeActions.StableThingId(activeBefore) };
			if (CustomDefs.SeverSymbiantSymbiosis == null)
				return new { success = false, error = "SeverSymbiantSymbiosis recipe def is missing." };

			var settingsSnapshot = SnapshotZombieSettings();
			var beforeLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>()).ToHashSet();
			SymbiantNaturalSpawnFixture fixture = null;
			Pawn doctor = null;
			ZombieSymbiant symbiant = null;
			object fixtureSetup = null;
			object severanceScenario = null;
			object cleanupSymbiant = null;
			object cleanupDoctor = null;
			object fixtureCleanup = null;
			object error = null;

			try
			{
				ApplyZombieSettingsOverride(settings =>
				{
					settings.showZombieEventLetters = false;
					settings.symbiantMaxCells = Math.Max(settings.symbiantMaxCells, 400);
				});

				if (TrySetupSymbiantNaturalSpawnFixture(map, out fixture, out var fixtureError) == false)
					return fixtureError;
				fixtureSetup = DescribeSymbiantNaturalSpawnFixture(fixture);
				if (TryFindClearSpawnCell(map, fixture.room.interiorRect.CenterCell + new IntVec3(5, 0, 0), 24f, out var doctorCell, out var doctorCellError) == false)
					return doctorCellError;
				doctor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(doctor, doctorCell, map, Rot4.South);
				DisablePawnWork(doctor);
				doctor.needs?.AddOrRemoveNeedsAsAppropriate();
				doctor.mindState?.mentalStateHandler?.Reset();

				symbiant = SpawnAssignedSymbiantForSeveranceContract(map, fixture);
				severanceScenario = RunSymbiantSeveranceScenario(map, fixture, doctor, symbiant);
			}
			catch (Exception ex)
			{
				error = ex.ToString();
			}
			finally
			{
				cleanupSymbiant = CleanupTemporarySymbiant(map, symbiant, cleanup);
				if (cleanup && doctor != null && doctor.Destroyed == false)
				{
					var id = ZombieRuntimeActions.StableThingId(doctor);
					doctor.Destroy(DestroyMode.Vanish);
					cleanupDoctor = new { cleaned = doctor.Destroyed, doctor = id };
				}
				else
					cleanupDoctor = new { cleaned = false, skipped = cleanup == false, doctor = ZombieRuntimeActions.StableThingId(doctor) };
				fixtureCleanup = CleanupSymbiantNaturalSpawnFixture(map, fixture, cleanup);
				RestoreZombieSettings(settingsSnapshot);
			}

			var newLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
				.Where(letter => beforeLetters.Contains(letter) == false)
				.ToArray();
			var letterCleanup = CleanupTemporaryLetters(newLetters, cleanup);
			var ingredients = DescribeSymbiantSeveranceRecipeIngredients();
			var activeAfterCleanup = ZombieSymbiant.ActiveSymbiant(map);
			var success = error == null
				&& ScenarioSucceeded(ingredients)
				&& ScenarioSucceeded(severanceScenario)
				&& (activeAfterCleanup == null || cleanup == false);

			return new
			{
				success,
				sourcePath = "Recipe_SeverSymbiantSymbiosis.GetPartsToApplyOn/ApplyOnPawn -> ZombieSymbiant.TrySeverSymbiosis",
				error,
				fixtureSetup,
				ingredients,
				severanceScenario,
				cleanup = new
				{
					symbiant = cleanupSymbiant,
					doctor = cleanupDoctor,
					fixture = fixtureCleanup,
					letters = letterCleanup,
					activeSymbiantAfterCleanup = ZombieRuntimeActions.StableThingId(activeAfterCleanup)
				}
			};
		}

		static ZombieSymbiant SpawnAssignedSymbiantForSeveranceContract(Map map, SymbiantNaturalSpawnFixture fixture)
		{
			var spawnCell = fixture.room.interiorRect.Cells
				.Where(cell => cell.InBounds(map)
					&& cell.Standable(map)
					&& cell.GetEdifice(map) == null
					&& cell.GetThingList(map).Any(thing => thing is Pawn) == false)
				.OrderBy(cell => cell.DistanceToSquared(fixture.room.interiorRect.CenterCell))
				.FirstOrDefault();
			if (spawnCell.IsValid == false)
				throw new InvalidOperationException("Could not find a clear symbiant severance spawn cell.");

			ZombieSymbiant.Spawn(map, spawnCell);
			var symbiant = ZombieSymbiant.ActiveSymbiant(map) ?? throw new InvalidOperationException("Symbiant spawn did not create an active symbiant.");
			var originalHost = symbiant.LinkedHost;
			if (originalHost != null && originalHost != fixture.host)
				AccessTools.Method(typeof(ZombieSymbiant), "RemoveHostHediff")?.Invoke(null, new object[] { originalHost });
			AccessTools.Method(typeof(ZombieSymbiant), "AssignHost")?.Invoke(symbiant, new object[] { fixture.host });
			return symbiant;
		}

		static object RunSymbiantSeveranceScenario(Map map, SymbiantNaturalSpawnFixture fixture, Pawn doctor, ZombieSymbiant symbiant)
		{
			if (symbiant == null)
				return new { success = false, error = "No symbiant was spawned for the severance scenario." };
			var recipe = CustomDefs.SeverSymbiantSymbiosis;
			var worker = recipe.Worker as Recipe_SeverSymbiantSymbiosis;
			var host = fixture.host;
			if (worker == null || host == null || doctor == null)
				return new { success = false, error = "Recipe worker, host, or doctor is missing." };

			var beforeReadyParts = worker.GetPartsToApplyOn(host, recipe).ToArray();
			var torso = beforeReadyParts.FirstOrDefault(part => part.def == BodyPartDefOf.Torso);
			if (torso == null)
				return new
				{
					success = false,
					error = "Linked symbiant did not expose torso surgery target.",
					beforeReadyParts = beforeReadyParts.Select(part => part.def.defName).ToArray()
				};

			var hediffBefore = host.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) != null;
			var requiredExtract = ZombieSymbiant.SeveranceExtractCost();
			var extractIngredientDef = recipe.ingredients.FirstOrDefault(ingredient => ingredient.filter.Allows(CustomDefs.ZombieExtract));
			var ingredientPathCheck = new
			{
				requiredExtract,
				operationVisibleWithoutIngredients = beforeReadyParts.Length > 0,
				dynamicExtractCount = extractIngredientDef == null ? 0f : worker.GetIngredientCount(extractIngredientDef, null),
				manualMissingIngredientCallSkipped = true,
				reason = "The simplified design relies on RimWorld's bill ingredient availability path; direct ApplyOnPawn calls do not represent a real missing-ingredient surgery.",
				success = extractIngredientDef != null
					&& beforeReadyParts.Length > 0
					&& Mathf.Approximately(worker.GetIngredientCount(extractIngredientDef, null), requiredExtract)
			};

				var extractIngredient = ThingMaker.MakeThing(CustomDefs.ZombieExtract);
				extractIngredient.stackCount = requiredExtract;
				var extractBeforeSuccess = CountSpawnedThingsOfDef(map, CustomDefs.ZombieExtract);
				var beforeSeveranceLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
					.ToHashSet();
				var originalShowLetters = ZombieSettings.Values.showZombieEventLetters;
				try
				{
					ZombieSettings.Values.showZombieEventLetters = true;
					worker.ApplyOnPawn(host, torso, doctor, new List<Thing> { extractIngredient }, null);
				}
				finally
				{
					ZombieSettings.Values.showZombieEventLetters = originalShowLetters;
				}
				var severanceLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
					.Where(letter => beforeSeveranceLetters.Contains(letter) == false)
					.ToArray();
				var activeAfter = ZombieSymbiant.ActiveSymbiant(map);
				var linkedAfter = ZombieSymbiant.LinkedSymbiantFor(host);
				var hediffAfter = host.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) != null;
				var extractAfterSuccess = CountSpawnedThingsOfDef(map, CustomDefs.ZombieExtract);
				var consumedMapExtract = extractBeforeSuccess - extractAfterSuccess;
				var expectedRetreatIntervalTicks = Mathf.Max(GenDate.TicksPerHour, symbiant.CurrentExpansionIntervalTicks / ZombieSymbiant.RetreatSpeedFactor);
				var success = hediffBefore
					&& ingredientPathCheck.success
					&& symbiant.SymbiosisSevered
				&& symbiant.Destroyed == false
				&& activeAfter == symbiant
					&& linkedAfter == null
					&& hediffAfter == false
					&& host.Dead == false
					&& consumedMapExtract == 0
					&& severanceLetters.Any(letter => letter?.def == CustomDefs.SymbiantEvent && IsGreenLetterColor(letter.def.color))
					&& symbiant.CurrentRetreatIntervalTicks == expectedRetreatIntervalTicks;

			return new
			{
				success,
				beforeReadyParts = beforeReadyParts.Select(part => part.def.defName).ToArray(),
				requiredExtract,
				ingredientPathCheck,
				extractBeforeSuccess,
					extractAfterSuccess,
					consumedMapExtract,
					providedIngredientExtract = extractIngredient.stackCount,
					symbiantDestroyed = symbiant.Destroyed,
					symbiosisSevered = symbiant.SymbiosisSevered,
					expansionIntervalTicks = symbiant.CurrentExpansionIntervalTicks,
					retreatIntervalTicks = symbiant.CurrentRetreatIntervalTicks,
					expectedRetreatIntervalTicks,
					retreatSpeedFactor = ZombieSymbiant.RetreatSpeedFactor,
					severanceLetters = severanceLetters.Select(DescribeSymbiantDiscoveryLetter).ToArray(),
					activeAfter = ZombieRuntimeActions.StableThingId(activeAfter),
				linkedAfter = ZombieRuntimeActions.StableThingId(linkedAfter),
				hediffBefore,
				hediffAfter,
				hostDead = host.Dead
			};
		}

		static int CountSpawnedThingsOfDef(Map map, ThingDef def)
		{
			if (map == null || def == null)
				return 0;
			return map.listerThings.ThingsOfDef(def)?.Where(thing => thing.Destroyed == false).Sum(thing => thing.stackCount) ?? 0;
		}

		[Tool("zombieland/symbiant_benefit_contract", Description = "Verify host display-hediff repair, low/high benefit scaling, zombie targeting threshold, and skill bonus behavior.")]
		public static object SymbiantBenefitContract(
			[ToolParameter(Description = "Destroy the temporary symbiant, colonist, fixture buildings, and letters after capturing evidence.", Required = false, DefaultValue = true)] bool cleanup = true)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded." };
			var activeBefore = ZombieSymbiant.ActiveSymbiant(map);
			if (activeBefore != null)
				return new { success = false, error = "An active symbiant already exists on the current map.", activeSymbiant = ZombieRuntimeActions.StableThingId(activeBefore) };

			var settingsSnapshot = SnapshotZombieSettings();
			var beforeLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>()).ToHashSet();
			SymbiantNaturalSpawnFixture fixture = null;
			ZombieSymbiant symbiant = null;
			object fixtureSetup = null;
			object error = null;
			object initial = null;
				object repair = null;
				object high = null;
				object skill = null;
				object benefitLetter = null;
				object autoHeal = null;
				object forcedBenefits = null;
				var addedCells = 0;

			try
			{
				ApplyZombieSettingsOverride(settings =>
				{
					settings.showZombieEventLetters = false;
					settings.symbiantMaxCells = 25;
				});
				if (TrySetupSymbiantNaturalSpawnFixture(map, out fixture, out var fixtureError) == false)
					return fixtureError;
				fixtureSetup = DescribeSymbiantNaturalSpawnFixture(fixture);
				symbiant = SpawnAssignedSymbiantForSeveranceContract(map, fixture);
				var host = fixture.host;
				RepairHostLink(symbiant);
				initial = DescribeSymbiantBenefitCheck(symbiant, host);

				var removedHediffs = RemoveSymbiantHediffs(host);
				var afterRemoval = DescribeSymbiantBenefitCheck(symbiant, host);
				RepairHostLink(symbiant);
				var afterRepair = DescribeSymbiantBenefitCheck(symbiant, host);
				repair = new
				{
					removedHediffs,
					afterRemoval,
					afterRepair,
					minZeroBenefitSeverity = ZombieSymbiant.HostHediffSeverity(0f),
					success = removedHediffs > 0
						&& BenefitCheckHasHediff(afterRemoval) == false
						&& BenefitCheckHasHediff(afterRepair)
						&& BenefitCheckHediffSeverity(afterRepair) >= 0.001f
						&& ZombieSymbiant.HostHediffSeverity(0f) >= 0.001f
				};

					var roomCells = fixture.room.interiorRect.Cells
						.Where(cell => cell.InBounds(map) && cell.Standable(map))
						.ToArray();
					var beforeBenefitLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
						.ToHashSet();
					ZombieSettings.Values.showZombieEventLetters = true;
					addedCells = ZombieSymbiant.AddCells(map, roomCells);
					var benefitLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
						.Where(letter => beforeBenefitLetters.Contains(letter) == false)
						.ToArray();
					var symbiantEventColor = CustomDefs.SymbiantEvent?.color ?? CustomDefs.SymbiantConnection?.color ?? Color.clear;
					benefitLetter = new
					{
						success = benefitLetters.Any(letter => letter?.def == CustomDefs.SymbiantEvent && IsGreenLetterColor(letter.def.color)),
						eventLetterDef = CustomDefs.SymbiantEvent?.defName,
						eventLetterColor = DescribeColor(symbiantEventColor),
						colorOk = IsGreenLetterColor(symbiantEventColor),
						letters = benefitLetters.Select(DescribeSymbiantDiscoveryLetter).ToArray()
					};
					forcedBenefits = EnsureSymbiantHostBenefitsForProbe(symbiant, "ZombieIgnore", "SkillBonus", "AutoHeal");
					RepairHostLink(symbiant);
					high = DescribeSymbiantBenefitCheck(symbiant, host);
					skill = DescribeSymbiantSkillBonus(host);
					autoHeal = VerifySymbiantAutoHealKeepsContamination(symbiant, host);
				}
			catch (Exception ex)
			{
				error = ex.ToString();
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
			}

			var newLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
				.Where(letter => beforeLetters.Contains(letter) == false)
				.ToArray();
			var cleanupResult = CleanupTemporarySymbiant(map, symbiant, cleanup);
			var fixtureCleanup = CleanupSymbiantNaturalSpawnFixture(map, fixture, cleanup);
			var letterCleanup = CleanupTemporaryLetters(newLetters, cleanup);
			var activeAfterCleanup = ZombieSymbiant.ActiveSymbiant(map);
			var success = error == null
				&& BenefitCheckHasHediff(initial)
				&& BenefitCheckFactor(initial) < 0.5f
				&& BenefitCheckHasZombieProtection(initial) == false
				&& ScenarioSucceeded(repair)
					&& addedCells > 0
					&& BenefitCheckFactor(high) >= 0.5f
					&& BenefitCheckHasZombieProtection(high)
					&& ScenarioSucceeded(benefitLetter)
					&& ScenarioSucceeded(forcedBenefits)
					&& ScenarioSucceeded(skill)
					&& ScenarioSucceeded(autoHeal)
					&& (activeAfterCleanup == null || cleanup == false);

			return new
			{
				success,
				sourcePath = "ZombieSymbiant.EnsureHostLink/EnsureHostHediff -> BenefitFactor -> HasZombieTargetingProtection -> ApplySymbiantSkillBonus",
				error,
				fixtureSetup,
				initial,
					repair,
					addedCells,
					high,
					benefitLetter,
					forcedBenefits,
					skill,
					autoHeal,
					cleanup = new
				{
					symbiant = cleanupResult,
					fixture = fixtureCleanup,
					letters = letterCleanup,
					activeSymbiantAfterCleanup = ZombieRuntimeActions.StableThingId(activeAfterCleanup)
				}
			};
		}

		static void RepairHostLink(ZombieSymbiant symbiant)
		{
			AccessTools.Method(typeof(ZombieSymbiant), "EnsureHostLink")?.Invoke(symbiant, null);
		}

		static int RemoveSymbiantHediffs(Pawn host)
		{
			var hediffs = host?.health?.hediffSet?.hediffs?
				.Where(hediff => hediff.def == CustomDefs.SymbiantSymbiosis)
				.ToArray() ?? Array.Empty<Hediff>();
			foreach (var hediff in hediffs)
				host.health.RemoveHediff(hediff);
			return hediffs.Length;
		}

		static object DescribeSymbiantBenefitCheck(ZombieSymbiant symbiant, Pawn host)
		{
			var hediff = host?.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) as Hediff_SymbiantSymbiosis;
			return new
			{
				cellCount = symbiant?.CellCount ?? 0,
				fullBenefitCells = symbiant?.FullBenefitCells ?? 0,
				integratedVisibleCells = symbiant?.IntegratedVisibleCells ?? 0f,
				benefitFactor = symbiant?.BenefitFactor ?? 0f,
				hasZombieTargetingProtection = ZombieSymbiant.HasZombieTargetingProtection(host),
				hasHediff = hediff != null,
				hediffSeverity = hediff?.Severity ?? 0f,
				hediffSymbiantThingId = hediff?.symbiantThingId,
				expectedHostHediffSeverity = ZombieSymbiant.HostHediffSeverity(symbiant?.BenefitFactor ?? 0f)
			};
		}

			static object DescribeSymbiantSkillBonus(Pawn host)
			{
				var skill = host?.skills?.GetSkill(SkillDefOf.Construction);
				if (skill == null)
					return new { success = false, error = "Linked host has no Construction skill record." };
			var previousProfile = ZombieSymbiant.DebugPerfProfile;
			object restoreAction = null;
			try
			{
				_ = ZombieSymbiant.SetDebugPerfProfile("noTick");
				skill.Level = 10;
				var raw = skill.Level;
				restoreAction = ZombieSymbiant.SetDebugPerfProfile(previousProfile);
				var patched = skill.Level;
				var bonus = ZombieSymbiant.SkillBonusBenefitCount(host);
				var expected = Mathf.Clamp(raw + bonus, 0, SkillRecord.MaxLevel);
				return new
				{
					success = raw == 10 && patched == expected && bonus > 0 && patched > raw,
					skill = skill.def.defName,
					raw,
					patched,
					bonus,
					expected,
					benefitFactor = ZombieSymbiant.SymbiantBenefitFactor(host),
					previousProfile,
					restoreAction
				};
			}
			finally
			{
					if (ZombieSymbiant.DebugPerfProfile != previousProfile)
						_ = ZombieSymbiant.SetDebugPerfProfile(previousProfile);
					}
				}

			static object EnsureSymbiantHostBenefitsForProbe(ZombieSymbiant symbiant, params string[] benefitNames)
			{
				var enumType = typeof(ZombieSymbiant).GetNestedType("HostBenefit", System.Reflection.BindingFlags.NonPublic);
				var hostBenefitsField = AccessTools.Field(typeof(ZombieSymbiant), "hostBenefits");
				var list = hostBenefitsField?.GetValue(symbiant) as IList;
				if (symbiant == null || enumType == null || list == null)
					return new { success = false, error = "Could not access Symbiant host benefit list." };

				var added = new List<string>();
				foreach (var benefitName in benefitNames ?? Array.Empty<string>())
				{
					var value = Enum.Parse(enumType, benefitName);
					if (list.Contains(value))
						continue;
					list.Add(value);
					added.Add(benefitName);
				}
				RepairHostLink(symbiant);
				return new
				{
					success = true,
					requested = benefitNames ?? Array.Empty<string>(),
					added = added.ToArray()
				};
			}

			static object VerifySymbiantAutoHealKeepsContamination(ZombieSymbiant symbiant, Pawn host)
			{
				if (symbiant == null || host?.health?.hediffSet == null)
					return new { success = false, error = "Symbiant or host health is missing." };
				if (Constants.CONTAMINATION == false)
					return new { success = true, skipped = true, reason = "Contamination is disabled." };

				var originalContamination = host.GetContamination(false);
				var torso = host.RaceProps?.body?.AllParts?.FirstOrDefault(part => part.def == BodyPartDefOf.Torso);
				if (torso == null)
					return new { success = false, error = "Host has no torso body part for the injury probe." };

				Hediff_Injury injury = null;
				try
				{
					host.SetContamination(0.8f);
					var contaminationHediff = host.health.hediffSet.GetFirstHediffOfDef(CustomDefs.ContaminationEffect);
					injury = HediffMaker.MakeHediff(HediffDefOf.Cut, host, torso) as Hediff_Injury;
					if (injury == null)
						return new { success = false, error = "Could not create a cut injury." };
					injury.Severity = 7f;
					host.health.AddHediff(injury, torso);

					var enumType = typeof(ZombieSymbiant).GetNestedType("HostBenefit", System.Reflection.BindingFlags.NonPublic);
					var hostBenefitsField = AccessTools.Field(typeof(ZombieSymbiant), "hostBenefits");
					var list = hostBenefitsField?.GetValue(symbiant) as IList;
					if (enumType == null || list == null)
						return new { success = false, error = "Could not access Symbiant host benefit list." };
					list.Add(Enum.Parse(enumType, "AutoHeal"));
					AccessTools.Method(typeof(ZombieSymbiant), "TryAutoHealHost")?.Invoke(symbiant, null);

					var contaminationAfter = host.GetContamination(false);
					var contaminationHediffAfter = host.health.hediffSet.GetFirstHediffOfDef(CustomDefs.ContaminationEffect);
					var injuryStillPresent = host.health.hediffSet.hediffs.Contains(injury);
					var injuryHealed = injury.Severity <= 0.001f;
					var contaminationHediffAutoHealable = ZombieSymbiant.IsAutoHealableHediffForDebug(contaminationHediff);
					return new
					{
						success = contaminationHediff != null
							&& contaminationHediffAfter != null
							&& contaminationAfter > 0.75f
							&& injuryHealed
							&& contaminationHediffAutoHealable == false,
						contaminationBefore = 0.8f,
						contaminationAfter,
						contaminationHediffBefore = contaminationHediff?.def?.defName,
						contaminationHediffAfter = contaminationHediffAfter?.def?.defName,
						contaminationHediffAutoHealable,
						injuryHealed,
						injuryStillPresent,
						injurySeverityAfter = injury.Severity
					};
				}
				finally
				{
					if (injury != null && host.health?.hediffSet?.hediffs?.Contains(injury) == true)
						host.health.RemoveHediff(injury);
					host.SetContamination(originalContamination);
				}
			}

			static bool BenefitCheckHasHediff(object check)
		{
			return (bool?)check?.GetType().GetProperty("hasHediff")?.GetValue(check) == true;
		}

		static float BenefitCheckHediffSeverity(object check)
		{
			return (float?)check?.GetType().GetProperty("hediffSeverity")?.GetValue(check) ?? 0f;
		}

		static float BenefitCheckFactor(object check)
		{
			return (float?)check?.GetType().GetProperty("benefitFactor")?.GetValue(check) ?? 0f;
		}

		static bool BenefitCheckHasZombieProtection(object check)
		{
			return (bool?)check?.GetType().GetProperty("hasZombieTargetingProtection")?.GetValue(check) == true;
		}

		static int FindSurgeryOutcomeSeed(float chance, bool desiredOutcome)
		{
			for (var seed = 1; seed < 10000; seed++)
			{
				Rand.PushState(seed);
				bool outcome;
				try
				{
					outcome = Rand.Chance(chance);
				}
				finally
				{
					Rand.PopState();
				}
				if (outcome == desiredOutcome)
					return seed;
			}
			throw new InvalidOperationException($"Could not find deterministic surgery seed for chance {chance:0.000} and desired outcome {desiredOutcome}.");
		}

		static object DescribeSymbiantSeveranceRecipeIngredients()
		{
			var recipe = CustomDefs.SeverSymbiantSymbiosis;
			var filter = recipe?.fixedIngredientFilter;
			var allowsExtract = filter?.Allows(CustomDefs.ZombieExtract) == true;
			var allowsMedicine = filter?.Allows(ThingDefOf.MedicineIndustrial) == true;
			var ingredientCount = recipe?.ingredients?.Count ?? 0;
			var hasExtractIngredient = recipe?.ingredients?.Any(ingredient => ingredient.filter.Allows(CustomDefs.ZombieExtract)) == true;
			var hasMedicineIngredient = recipe?.ingredients?.Any(ingredient => ingredient.filter.Allows(ThingDefOf.MedicineIndustrial) && Mathf.Approximately(ingredient.GetBaseCount(), 1f)) == true;
			var extractIngredient = recipe?.ingredients?.FirstOrDefault(ingredient => ingredient.filter.Allows(CustomDefs.ZombieExtract));
			var dynamicExtractCount = extractIngredient == null ? 0f : recipe.Worker.GetIngredientCount(extractIngredient, null);
			return new
			{
				success = recipe != null
					&& recipe.workerClass == typeof(Recipe_SeverSymbiantSymbiosis)
					&& recipe.targetsBodyPart
					&& ingredientCount == 2
					&& allowsExtract
					&& allowsMedicine
					&& hasExtractIngredient
					&& hasMedicineIngredient
					&& Mathf.Approximately(dynamicExtractCount, ZombieSymbiant.SeveranceExtractCost()),
				recipe = recipe?.defName,
				workerClass = recipe?.workerClass?.FullName,
				targetsBodyPart = recipe?.targetsBodyPart ?? false,
				ingredientCount,
				allowsExtract,
				allowsMedicine,
				hasExtractIngredient,
				hasMedicineIngredient,
				dynamicExtractCount,
				currentRequiredExtract = ZombieSymbiant.SeveranceExtractCost()
			};
		}

		[Tool("zombieland/symbiant_unsafe_damage_contract", Description = "Verify size-scaled shared health damage, thumper no-effect behavior, inspect-tab isolation, uncontrolled destruction, and host-death retreat.")]
		public static object SymbiantUnsafeDamageContract(
			[ToolParameter(Description = "Destroy temporary symbiants, colonists, fixture buildings, and letters after capturing evidence.", Required = false, DefaultValue = true)] bool cleanup = true)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded." };
			var activeBefore = ZombieSymbiant.ActiveSymbiant(map);
			if (activeBefore != null)
				return new { success = false, error = "An active symbiant already exists on the current map.", activeSymbiant = ZombieRuntimeActions.StableThingId(activeBefore) };

			var settingsSnapshot = SnapshotZombieSettings();
			var beforeLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>()).ToHashSet();
			object symbiantDamageLeaksToHost = null;
			object hostDamageSharesToSymbiant = null;
			object symbiantDestructionKillsHost = null;
			object sizeHealthScaling = null;
			object thumperDamageNoEffect = null;
			object inspectTabsHidden = null;
			object uncontrolledDestroyKillsHost = null;
			object hostDeathStartsRetreat = null;
			object error = null;

			try
			{
				ApplyZombieSettingsOverride(settings =>
				{
					settings.showZombieEventLetters = false;
					settings.symbiantMaxCells = Math.Max(settings.symbiantMaxCells, 400);
				});
				symbiantDamageLeaksToHost = RunSymbiantUnsafeDamageScenario(map, "symbiantDamageLeaksToHost", cleanup);
				hostDamageSharesToSymbiant = RunSymbiantUnsafeDamageScenario(map, "hostDamageSharesToSymbiant", cleanup);
				symbiantDestructionKillsHost = RunSymbiantUnsafeDamageScenario(map, "symbiantDestructionKillsHost", cleanup);
				sizeHealthScaling = RunSymbiantUnsafeDamageScenario(map, "sizeHealthScaling", cleanup);
				thumperDamageNoEffect = RunSymbiantUnsafeDamageScenario(map, "thumperDamageNoEffect", cleanup);
				inspectTabsHidden = RunSymbiantUnsafeDamageScenario(map, "inspectTabsHidden", cleanup);
				uncontrolledDestroyKillsHost = RunSymbiantUnsafeDamageScenario(map, "uncontrolledDestroyKillsHost", cleanup);
				hostDeathStartsRetreat = RunSymbiantUnsafeDamageScenario(map, "hostDeathStartsRetreat", cleanup);
			}
			catch (Exception ex)
			{
				error = ex.ToString();
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
			}

			var newLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
				.Where(letter => beforeLetters.Contains(letter) == false)
				.ToArray();
			var letterCleanup = CleanupTemporaryLetters(newLetters, cleanup);
			var activeAfterCleanup = ZombieSymbiant.ActiveSymbiant(map);
			var patchTargets = new
			{
				pawnPreApplyDamage = PatchedMethodsForPatchClass("Pawn_PreApplyDamage_Patch"),
				pawnKill = PatchedMethodsForPatchClass("Pawn_Kill_Patch")
			};
			var success = error == null
				&& patchTargets.pawnPreApplyDamage.Length > 0
				&& patchTargets.pawnKill.Length > 0
				&& ScenarioSucceeded(symbiantDamageLeaksToHost)
				&& ScenarioSucceeded(hostDamageSharesToSymbiant)
				&& ScenarioSucceeded(symbiantDestructionKillsHost)
				&& ScenarioSucceeded(sizeHealthScaling)
				&& ScenarioSucceeded(thumperDamageNoEffect)
				&& ScenarioSucceeded(inspectTabsHidden)
				&& ScenarioSucceeded(uncontrolledDestroyKillsHost)
				&& ScenarioSucceeded(hostDeathStartsRetreat)
				&& (activeAfterCleanup == null || cleanup == false);

			return new
			{
				success,
				sourcePath = "Pawn.TakeDamage -> Pawn.PreApplyDamage -> ZombieSymbiant.PreApplyLinkedDamage/PreApplyHostLinkedDamage; Pawn.HealthScale -> cell-count health scaling; ZombieSymbiant.Destroy; Pawn.Kill -> ZombieSymbiant.NotifyHostKilled",
				error,
				patchTargets,
				symbiantDamageLeaksToHost,
				hostDamageSharesToSymbiant,
				symbiantDestructionKillsHost,
				sizeHealthScaling,
				thumperDamageNoEffect,
				inspectTabsHidden,
				uncontrolledDestroyKillsHost,
				hostDeathStartsRetreat,
				cleanup = new
				{
					letters = letterCleanup,
					activeSymbiantAfterCleanup = ZombieRuntimeActions.StableThingId(activeAfterCleanup)
				}
			};
		}

		static object RunSymbiantUnsafeDamageScenario(Map map, string scenario, bool cleanup)
		{
			SymbiantNaturalSpawnFixture fixture = null;
			ZombieSymbiant symbiant = null;
			object fixtureSetup = null;
			object action;

			try
			{
				if (TrySetupSymbiantNaturalSpawnFixture(map, out fixture, out var fixtureError) == false)
					return fixtureError;
				fixtureSetup = DescribeSymbiantNaturalSpawnFixture(fixture);
				symbiant = SpawnAssignedSymbiantForSeveranceContract(map, fixture);
				var host = fixture.host;

				var hostInjuryBefore = TotalInjurySeverity(host);
				if (scenario == "symbiantDamageLeaksToHost")
				{
					var addedCells = GrowSymbiantForDamageProbe(map, symbiant, 40);
					var damage = 40f;
					var symbiantInjuryBefore = TotalInjurySeverity(symbiant);
					var hostSharedHealthBefore = symbiant.SharedHealthCurrentDisplay;
					var damageResult = symbiant.TakeDamage(new DamageInfo(DamageDefOf.Cut, damage, 0f, -1f, null));
					var hostInjuryAfter = TotalInjurySeverity(host);
					var symbiantInjuryAfter = TotalInjurySeverity(symbiant);
					var hostInjuryDelta = hostInjuryAfter - hostInjuryBefore;
					var symbiantInjuryDelta = symbiantInjuryAfter - symbiantInjuryBefore;
					var sharedHealthAfter = symbiant.SharedHealthCurrentDisplay;
					action = new
					{
						damage,
						cellCount = symbiant.CellCount,
						addedCells,
						damageDealt = damageResult.totalDamageDealt,
						sharedHealthBefore = hostSharedHealthBefore,
						sharedHealthAfter,
						sharedHealthDelta = hostSharedHealthBefore - sharedHealthAfter,
						leakPercent = symbiant.SharedDamageLeakPercentDisplay,
						hostInjuryBefore,
						hostInjuryAfter,
						hostInjuryDelta,
						symbiantInjuryBefore,
						symbiantInjuryAfter,
						symbiantInjuryDelta,
						success = symbiant.CellCount >= 40
							&& Mathf.Approximately(damageResult.totalDamageDealt, 0f)
							&& hostInjuryDelta > 0f
							&& hostInjuryDelta < damage
							&& Mathf.Approximately(symbiantInjuryDelta, 0f)
							&& sharedHealthAfter < hostSharedHealthBefore
							&& hostSharedHealthBefore - sharedHealthAfter >= damage - 1f
							&& hostSharedHealthBefore - sharedHealthAfter <= damage + 1f
							&& symbiant.Destroyed == false
							&& host.Dead == false
					};
				}
				else if (scenario == "hostDamageSharesToSymbiant")
				{
					var addedCells = GrowSymbiantForDamageProbe(map, symbiant, 40);
					var damage = 30f;
					var symbiantInjuryBefore = TotalInjurySeverity(symbiant);
					var sharedHealthBefore = symbiant.SharedHealthCurrentDisplay;
					var damageResult = host.TakeDamage(new DamageInfo(DamageDefOf.Blunt, damage, 0f, -1f, null));
					var hostInjuryAfter = TotalInjurySeverity(host);
					var symbiantInjuryAfter = TotalInjurySeverity(symbiant);
					var hostInjuryDelta = hostInjuryAfter - hostInjuryBefore;
					var symbiantInjuryDelta = symbiantInjuryAfter - symbiantInjuryBefore;
					var sharedHealthAfter = symbiant.SharedHealthCurrentDisplay;
					action = new
					{
						damage,
						cellCount = symbiant.CellCount,
						addedCells,
						damageDealtToHost = damageResult.totalDamageDealt,
						sharedHealthBefore,
						sharedHealthAfter,
						sharedHealthDelta = sharedHealthBefore - sharedHealthAfter,
						leakPercent = symbiant.SharedDamageLeakPercentDisplay,
						hostInjuryBefore,
						hostInjuryAfter,
						hostInjuryDelta,
						symbiantInjuryBefore,
						symbiantInjuryAfter,
						symbiantInjuryDelta,
						hostDead = host.Dead,
						symbiantDestroyed = symbiant.Destroyed,
						success = symbiant.CellCount >= 40
							&& hostInjuryDelta > 0f
							&& hostInjuryDelta < damage
							&& Mathf.Approximately(symbiantInjuryDelta, 0f)
							&& sharedHealthAfter < sharedHealthBefore
							&& sharedHealthBefore - sharedHealthAfter >= damage - 1f
							&& sharedHealthBefore - sharedHealthAfter <= damage + 1f
							&& host.Dead == false
							&& symbiant.Destroyed == false
					};
				}
				else if (scenario == "symbiantDestructionKillsHost")
				{
					var hediffBefore = host.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) != null;
					var sharedHealthBefore = symbiant.SharedHealthCurrentDisplay;
					var damage = symbiant.SharedHealthMaxDisplay + 250f;
					var damageResult = symbiant.TakeDamage(new DamageInfo(DamageDefOf.Cut, damage, 0f, -1f, null));
					var linkedAfter = ZombieSymbiant.LinkedSymbiantFor(host);
					var hediffAfter = host.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) != null;
					var activeAfter = ZombieSymbiant.ActiveSymbiant(map);
					action = new
					{
						damage,
						damageDealt = damageResult.totalDamageDealt,
						sharedHealthBefore,
						sharedHealthAfter = symbiant.SharedHealthCurrentDisplay,
						hediffBefore,
						hediffAfter,
						hostDead = host.Dead,
						symbiantDestroyed = symbiant.Destroyed,
						linkedAfter = ZombieRuntimeActions.StableThingId(linkedAfter),
						activeAfter = ZombieRuntimeActions.StableThingId(activeAfter),
						success = Mathf.Approximately(damageResult.totalDamageDealt, 0f)
							&& sharedHealthBefore > 0
							&& hediffBefore
							&& hediffAfter == false
							&& host.Dead
							&& symbiant.Destroyed
							&& linkedAfter == null
							&& activeAfter == null
					};
					symbiant = null;
				}
				else if (scenario == "sizeHealthScaling")
				{
					var oneCellMax = symbiant.SharedHealthMaxDisplay;
					var targetCells = GenRadial.RadialCellsAround(symbiant.Position, 8f, true)
						.Where(cell => cell.InBounds(map) && cell.Standable(map))
						.Take(40)
						.ToArray();
					var addedCells = ZombieSymbiant.AddCells(map, targetCells);
					symbiant = ZombieSymbiant.ActiveSymbiant(map) ?? symbiant;
					var scaledMax = symbiant.SharedHealthMaxDisplay;
					var expectedMultiplier = ZombieSymbiant.HealthScaleMultiplierForCells(symbiant.CellCount);
					action = new
					{
						oneCellMax,
						cellCount = symbiant.CellCount,
						addedCells,
						scaledMax,
						expectedMultiplier,
						healthScaleCellMultiplier = symbiant.HealthScaleCellMultiplier,
						success = symbiant.CellCount > 1
							&& addedCells > 0
							&& scaledMax > oneCellMax
							&& Mathf.Approximately(symbiant.HealthScaleCellMultiplier, expectedMultiplier)
					};
				}
				else if (scenario == "thumperDamageNoEffect")
				{
					var damageDef = CustomDefs.SeismicWave;
					var damage = symbiant.SharedHealthMaxDisplay + 250f;
					var symbiantInjuryBefore = TotalInjurySeverity(symbiant);
					var sharedHealthBefore = symbiant.SharedHealthCurrentDisplay;
					var damageResult = damageDef == null ? null : symbiant.TakeDamage(new DamageInfo(damageDef, damage, 0f, -1f, null));
					var hostInjuryAfter = TotalInjurySeverity(host);
					var symbiantInjuryAfter = TotalInjurySeverity(symbiant);
					var sharedHealthAfter = symbiant.SharedHealthCurrentDisplay;
					action = new
					{
						damageDef = damageDef?.defName,
						damage,
						damageDealt = damageResult?.totalDamageDealt ?? -1f,
						sharedHealthBefore,
						sharedHealthAfter,
						hostInjuryBefore,
						hostInjuryAfter,
						symbiantInjuryBefore,
						symbiantInjuryAfter,
						success = damageDef != null
							&& Mathf.Approximately(damageResult.totalDamageDealt, 0f)
							&& sharedHealthAfter == sharedHealthBefore
							&& Mathf.Approximately(hostInjuryAfter, hostInjuryBefore)
							&& Mathf.Approximately(symbiantInjuryAfter, symbiantInjuryBefore)
							&& symbiant.Destroyed == false
							&& host.Dead == false
					};
				}
				else if (scenario == "inspectTabsHidden")
				{
					Find.Selector.ClearSelection();
					Find.Selector.Select(symbiant, false, false);
					var selected = Find.Selector.IsSelected(symbiant);
					var curTabs = new MainTabWindow_Inspect().CurTabs?.ToArray();
					var directTabs = symbiant.GetInspectTabs()?.ToArray();
					var inspectString = symbiant.GetInspectString();
					Find.Selector.ClearSelection();
					action = new
					{
						selected,
						curTabsNull = curTabs == null,
						curTabCount = curTabs?.Length ?? -1,
						curTabTypes = curTabs?.Select(tab => tab?.GetType().FullName).ToArray(),
						directTabsNull = directTabs == null,
						directTabCount = directTabs?.Length ?? -1,
						directTabTypes = directTabs?.Select(tab => tab?.GetType().FullName).ToArray(),
						inspectString,
						success = selected
							&& curTabs != null
							&& curTabs.Length == 0
							&& directTabs != null
							&& directTabs.Length == 0
					};
				}
				else if (scenario == "uncontrolledDestroyKillsHost")
				{
					var hediffBefore = host.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) != null;
					symbiant.Destroy(DestroyMode.Vanish);
					var linkedAfter = ZombieSymbiant.LinkedSymbiantFor(host);
					var hediffAfter = host.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) != null;
					var hostInjuryAfter = TotalInjurySeverity(host);
					action = new
					{
						hediffBefore,
						hediffAfter,
						hostInjuryBefore,
						hostInjuryAfter,
						hostDead = host.Dead,
						symbiantDestroyed = symbiant.Destroyed,
						linkedAfter = ZombieRuntimeActions.StableThingId(linkedAfter),
						success = hediffBefore
							&& hediffAfter == false
							&& host.Dead
							&& symbiant.Destroyed
							&& linkedAfter == null
					};
					symbiant = null;
				}
				else if (scenario == "hostDeathStartsRetreat")
				{
					var hediffBefore = host.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) != null;
					host.Kill(null);
					var activeAfter = ZombieSymbiant.ActiveSymbiant(map);
					var hediffAfter = host.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) != null;
					action = new
					{
						hediffBefore,
						hediffAfter,
						hostDead = host.Dead,
						symbiantDestroyed = symbiant.Destroyed,
						symbiosisSevered = symbiant.SymbiosisSevered,
						activeAfter = ZombieRuntimeActions.StableThingId(activeAfter),
						success = hediffBefore
							&& hediffAfter == false
							&& host.Dead
							&& symbiant.Destroyed == false
							&& symbiant.SymbiosisSevered
							&& activeAfter == symbiant
					};
				}
				else
					action = new { success = false, error = $"Unknown unsafe-damage scenario '{scenario}'." };

				return new
				{
					success = ScenarioSucceeded(action),
					scenario,
					fixtureSetup,
					action
				};
			}
			finally
			{
				_ = CleanupTemporarySymbiant(map, symbiant, cleanup);
				_ = CleanupSymbiantNaturalSpawnFixture(map, fixture, cleanup);
			}
		}

		static int GrowSymbiantForDamageProbe(Map map, ZombieSymbiant symbiant, int targetCells)
		{
			if (map == null || symbiant == null || targetCells <= symbiant.CellCount)
				return 0;
			var targetCellsArray = GenRadial.RadialCellsAround(symbiant.Position, 12f, true)
				.Where(cell => cell.InBounds(map) && cell.Standable(map))
				.Take(targetCells)
				.ToArray();
			_ = ZombieSymbiant.AddCells(map, targetCellsArray);
			return symbiant.CellCount - 1;
		}

		[Tool("zombieland/symbiant_expansion_contract", Description = "Build a reversible two-room fixture and verify symbiant expansion into room cells, under a closed door, and through one constructed wall.")]
		public static object SymbiantExpansionContract(
			[ToolParameter(Description = "Destroy the temporary symbiant and two-room fixture after capturing evidence.", Required = false, DefaultValue = true)] bool cleanup = true)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded." };

			var activeBefore = ZombieSymbiant.ActiveSymbiant(map);
			if (activeBefore != null)
				return new { success = false, error = "An active symbiant already exists on the current map.", activeSymbiant = ZombieRuntimeActions.StableThingId(activeBefore) };

			var beforeLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>()).ToHashSet();
			if (TrySetupSymbiantExpansionFixture(map, out var fixture, out var fixtureError) == false)
				return fixtureError;
			var fixtureDescription = DescribeSymbiantExpansionFixture(fixture);

			ZombieSymbiant symbiant = null;
			object spawnError = null;
			try
			{
				ZombieSymbiant.Spawn(map, fixture.spawnCell);
				symbiant = ZombieSymbiant.ActiveSymbiant(map);
			}
			catch (Exception ex)
			{
				spawnError = ex.ToString();
			}

			var openBefore = symbiant?.AbsoluteCells.ToHashSet() ?? new HashSet<IntVec3>();
			var openPulse = symbiant?.TryExpansionPulse() == true;
			var openNewCell = symbiant?.AbsoluteCells.FirstOrDefault(cell => openBefore.Contains(cell) == false) ?? IntVec3.Invalid;

			var leftFillAdded = ZombieSymbiant.AddCells(map, fixture.leftInterior.Cells);
			var doorBeforeDestroyed = fixture.door.Destroyed;
			var doorPulse = symbiant?.TryExpansionPulse() == true;
			var doorOccupied = symbiant?.ContainsCell(fixture.doorCell) == true;
			var doorAfterDestroyed = fixture.door.Destroyed;

			var rightInteriorBefore = fixture.rightInterior.Cells.Any(cell => symbiant?.ContainsCell(cell) == true);
			var dividerBefore = fixture.dividerWalls
				.Select(wall => new { cell = ZombieRuntimeActions.DescribeCell(wall.Position), destroyed = wall.Destroyed })
				.ToArray();
			var breachPulse = symbiant?.TryExpansionPulse() == true;
			var breachedCell = fixture.dividerWalls
				.Select(wall => wall.Position)
				.FirstOrDefault(cell => symbiant?.ContainsCell(cell) == true);
			var breachedWallGone = breachedCell.IsValid && breachedCell.GetEdifice(map) == null;
			var rightFillAdded = ZombieSymbiant.AddCells(map, fixture.rightInterior.Cells);

			var newLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
				.Where(letter => beforeLetters.Contains(letter) == false)
				.ToArray();
			var letters = newLetters.Select(DescribeSymbiantDiscoveryLetter).ToArray();
			var cleanupResult = CleanupTemporarySymbiant(map, symbiant, cleanup);
			var letterCleanup = CleanupTemporaryLetters(newLetters, cleanup);
			var fixtureCleanup = CleanupSymbiantExpansionFixture(map, fixture, cleanup);
			var activeAfterCleanup = ZombieSymbiant.ActiveSymbiant(map);

			var success = spawnError == null
				&& symbiant != null
				&& openPulse
				&& openNewCell.IsValid
				&& fixture.leftInterior.Contains(openNewCell)
				&& leftFillAdded > 0
				&& doorPulse
				&& doorOccupied
				&& doorBeforeDestroyed == false
				&& doorAfterDestroyed == false
				&& rightInteriorBefore == false
				&& breachPulse
				&& breachedCell.IsValid
				&& breachedWallGone
				&& rightFillAdded > 0
				&& activeAfterCleanup == null;

			return new
			{
				success,
				sourcePath = "ZombieSymbiant.TryExpansionPulse -> FindExpansionTarget -> room/door target or BreakableConstructedWall",
				spawnError,
				fixture = fixtureDescription,
				spawned = symbiant == null ? null : new
				{
					id = ZombieRuntimeActions.StableThingId(symbiant),
					destroyed = symbiant.Destroyed,
					cellCount = symbiant.Destroyed ? 0 : symbiant.CellCount
				},
				openExpansion = new
				{
					pulse = openPulse,
					newCell = openNewCell.IsValid ? ZombieRuntimeActions.DescribeCell(openNewCell) : null,
					inLeftInterior = openNewCell.IsValid && fixture.leftInterior.Contains(openNewCell)
				},
				doorExpansion = new
				{
					leftFillAdded,
					pulse = doorPulse,
					doorCell = ZombieRuntimeActions.DescribeCell(fixture.doorCell),
					occupied = doorOccupied,
					doorBeforeDestroyed,
					doorAfterDestroyed
				},
				wallBreach = new
				{
					rightInteriorBefore,
					dividerBefore,
					pulse = breachPulse,
					breachedCell = breachedCell.IsValid ? ZombieRuntimeActions.DescribeCell(breachedCell) : null,
					breachedWallGone,
					rightFillAdded
				},
				letters,
				cleanup = cleanupResult,
				letterCleanup,
				fixtureCleanup,
				activeSymbiantAfterCleanup = ZombieRuntimeActions.StableThingId(activeAfterCleanup)
				};
			}

			[Tool("zombieland/symbiant_door_path_cost_contract", Description = "Build a reversible door fixture and verify a Symbiant-covered door cell applies the difficulty-scaled slowdown to the actual path follower door-entry cost.")]
			public static object SymbiantDoorPathCostContract(
				[ToolParameter(Description = "Destroy the temporary pawn, symbiant, and door fixture after capturing evidence.", Required = false, DefaultValue = true)] bool cleanup = true)
			{
				var map = CurrentMap;
				if (map == null)
					return new { success = false, error = "No current map is loaded." };

				var activeBefore = ZombieSymbiant.ActiveSymbiant(map);
				if (activeBefore != null)
					return new { success = false, error = "An active symbiant already exists on the current map.", activeSymbiant = ZombieRuntimeActions.StableThingId(activeBefore) };

				if (TrySetupSymbiantExpansionFixture(map, out var fixture, out var fixtureError) == false)
					return fixtureError;

				ZombieSymbiant symbiant = null;
				Pawn actor = null;
				try
				{
					var insideCell = fixture.doorCell + IntVec3.South;
					var outsideCell = fixture.doorCell + IntVec3.North;
					if (insideCell.InBounds(map) == false || outsideCell.InBounds(map) == false || insideCell.Standable(map) == false || outsideCell.Standable(map) == false)
					{
						return new
						{
							success = false,
							fixture = DescribeSymbiantExpansionFixture(fixture),
							insideCell = ZombieRuntimeActions.DescribeCell(insideCell),
							outsideCell = ZombieRuntimeActions.DescribeCell(outsideCell),
							error = "Door path-cost fixture did not have standable cells on both sides of the door."
						};
					}

					symbiant = ZombieSymbiant.DebugSpawnForRendering(map, fixture.spawnCell, new[] { fixture.spawnCell, fixture.doorCell });
					if (symbiant == null)
						return new { success = false, fixture = DescribeSymbiantExpansionFixture(fixture), error = "Could not spawn temporary Symbiant for door path-cost contract." };

					actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
					GenSpawn.Spawn(actor, insideCell, map, Rot4.North, WipeMode.Vanish);
					actor.jobs?.EndCurrentJob(JobCondition.InterruptForced);
				if (actor.CanReach(outsideCell, PathEndMode.OnCell, Danger.Deadly) == false)
					return new
					{
						success = false,
						fixture = DescribeSymbiantExpansionFixture(fixture),
							actor = DescribePawn(actor),
							outsideCell = ZombieRuntimeActions.DescribeCell(outsideCell),
							error = "Temporary colonist could not path through the fixture door."
					};

					var previousProfile = ZombieSymbiant.DebugPerfProfile;
					float baseDoorCellCost = 0f;
					string baseCostError = null;
					var baseCostSuccess = false;
					try
					{
						_ = ZombieSymbiant.SetDebugPerfProfile("noPath");
						baseCostSuccess = TryCostToMoveIntoCell(actor, fixture.doorCell, out baseDoorCellCost, out baseCostError);
					}
					finally
					{
						_ = ZombieSymbiant.SetDebugPerfProfile(previousProfile);
					}
					var expectedCost = baseCostSuccess ? ZombieSymbiant.SymbiantMoveCost(actor, baseDoorCellCost) : 0;
					var fixtureDescription = DescribeSymbiantExpansionFixture(fixture);
					var actorDescription = DescribePawn(actor);
					var symbiantId = ZombieRuntimeActions.StableThingId(symbiant);
					var symbiantContainsDoor = symbiant.ContainsCell(fixture.doorCell);
					var staticCostSuccess = TryCostToMoveIntoCell(actor, fixture.doorCell, out var doorCellCost, out var costError);
					var staticCost = new
					{
						success = baseCostSuccess && staticCostSuccess && expectedCost > baseDoorCellCost && doorCellCost >= expectedCost,
						baseCost = baseDoorCellCost,
						cost = doorCellCost,
						expectedCost,
						slowPercent = ZombieSymbiant.SymbiantCellSlowPercent(),
						error = baseCostError ?? costError
						};

					actor.pather.StartPath(outsideCell, PathEndMode.OnCell);
					object inflatedSample = null;
					var samples = new List<object>();
					for (var tick = 0; tick <= 30; tick++)
					{
						if (tick > 0)
							AdvanceGameTicks(1);

						var nextCell = actor.pather.nextCell;
						var sample = new
						{
							tick,
							position = ZombieRuntimeActions.DescribeCell(actor.Position),
							nextCell = nextCell.IsValid ? ZombieRuntimeActions.DescribeCell(nextCell) : null,
							actor.pather.Moving,
							actor.pather.MovingNow,
							actor.pather.nextCellCostTotal,
							actor.pather.nextCellCostLeft,
							doorOpen = fixture.door?.Open,
							doorTicksUntilClose = fixture.door?.ticksUntilClose
						};
						samples.Add(sample);
						if (nextCell == fixture.doorCell && actor.pather.nextCellCostTotal >= expectedCost)
						{
							inflatedSample = sample;
							break;
						}
					}

					var cleanupResult = CleanupTemporarySymbiant(map, symbiant, cleanup);
					var actorCleanup = CleanupTemporaryPawn(actor, cleanup);
					var fixtureCleanup = CleanupSymbiantExpansionFixture(map, fixture, cleanup);
					var activeAfterCleanup = ZombieSymbiant.ActiveSymbiant(map);
					return new
					{
						success = symbiantContainsDoor
							&& ScenarioSucceeded(staticCost)
							&& inflatedSample != null
							&& activeAfterCleanup == null,
						expectedCost,
						fixture = fixtureDescription,
						symbiant = symbiantId,
						actor = actorDescription,
						doorCell = ZombieRuntimeActions.DescribeCell(fixture.doorCell),
						symbiantContainsDoor,
						staticCost,
						inflatedSample,
						samples = samples.ToArray(),
						cleanup = cleanupResult,
						actorCleanup,
						fixtureCleanup,
						activeSymbiantAfterCleanup = ZombieRuntimeActions.StableThingId(activeAfterCleanup)
					};
				}
				finally
				{
					_ = CleanupTemporarySymbiant(map, symbiant, cleanup);
					_ = CleanupTemporaryPawn(actor, cleanup);
					_ = CleanupSymbiantExpansionFixture(map, fixture, cleanup);
				}
			}

			[Tool("zombieland/symbiant_relocation_contract", Description = "Verify uprooted relocation, relocation grace, movable outdoor-cell reuse, and no-room dormancy.")]
			public static object SymbiantRelocationContract(
				[ToolParameter(Description = "Destroy temporary symbiants, colonists, fixture buildings, and letters after capturing evidence.", Required = false, DefaultValue = true)] bool cleanup = true)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded." };
			var activeBefore = ZombieSymbiant.ActiveSymbiant(map);
			if (activeBefore != null)
				return new { success = false, error = "An active symbiant already exists on the current map.", activeSymbiant = ZombieRuntimeActions.StableThingId(activeBefore) };

			var settingsSnapshot = SnapshotZombieSettings();
			var beforeLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>()).ToHashSet();
			object graceAndReseed = null;
			object movableCellReuse = null;
			object noRoomDormancy = null;
			object error = null;

			try
			{
				ApplyZombieSettingsOverride(settings =>
				{
					settings.showZombieEventLetters = false;
					settings.symbiantMaxCells = 80;
				});

				graceAndReseed = RunSymbiantGraceAndReseedScenario(map, cleanup);
				movableCellReuse = RunSymbiantMovableCellReuseScenario(map, cleanup);
				noRoomDormancy = RunSymbiantNoRoomDormancyScenario(map, cleanup);
			}
			catch (Exception ex)
			{
				error = ex.ToString();
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
			}

			var newLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
				.Where(letter => beforeLetters.Contains(letter) == false)
				.ToArray();
			var letterCleanup = CleanupTemporaryLetters(newLetters, cleanup);
			var activeAfterCleanup = ZombieSymbiant.ActiveSymbiant(map);
			var success = error == null
				&& ScenarioSucceeded(graceAndReseed)
				&& ScenarioSucceeded(movableCellReuse)
				&& ScenarioSucceeded(noRoomDormancy)
				&& (activeAfterCleanup == null || cleanup == false);

			return new
			{
				success,
				sourcePath = "ZombieSymbiant.TryReseedIfUprooted/TryRelocationPulse/FindExpansionTarget",
				error,
				graceAndReseed,
				movableCellReuse,
				noRoomDormancy,
				cleanup = new
				{
					letters = letterCleanup,
					activeSymbiantAfterCleanup = ZombieRuntimeActions.StableThingId(activeAfterCleanup)
				}
			};
		}

		static object RunSymbiantGraceAndReseedScenario(Map map, bool cleanup)
		{
			SymbiantExpansionFixture fixture = null;
			ZombieSymbiant symbiant = null;
			Pawn host = null;
			object fixtureSetup = null;
			try
			{
				if (TrySetupSymbiantExpansionFixture(map, out fixture, out var fixtureError) == false)
					return fixtureError;
				fixtureSetup = DescribeSymbiantExpansionFixture(fixture);
				host = SpawnSymbiantRelocationHost(map, fixture.rightInterior.CenterCell);
				symbiant = SpawnAssignedSymbiantForRelocationContract(map, fixture.spawnCell, host);
				var initial = DescribeSymbiantRelocationState(symbiant, host);
				var addedCells = ZombieSymbiant.AddCells(map, fixture.leftInterior.Cells.Where(cell => cell.InBounds(map) && cell.Standable(map)));
				var beforeOpen = DescribeSymbiantRelocationState(symbiant, host);
				var openedBuildings = DestroyFixtureBuildings(map, fixture, building => IsAdjacentToRect(building.Position, fixture.leftInterior) && fixture.dividerWalls.Contains(building) == false);
				var afterOpen = DescribeSymbiantRelocationState(symbiant, host);
				var positionBeforeGrace = symbiant.Position;
				var beforeGraceReseed = InvokeSymbiantTryReseedIfUprooted(symbiant);
				var positionAfterGraceProbe = symbiant.Position;
				var afterGraceProbe = DescribeSymbiantRelocationState(symbiant, host);
				ExpireSymbiantUprootedGrace(symbiant);
				var afterGraceReseed = InvokeSymbiantTryReseedIfUprooted(symbiant);
				var activeAfterReseed = ZombieSymbiant.ActiveSymbiant(map) ?? symbiant;
				var afterReseed = DescribeSymbiantRelocationState(activeAfterReseed, host);
				var reseededIntoRightRoom = activeAfterReseed?.Spawned == true && fixture.rightInterior.Contains(activeAfterReseed.Position);
				var success = addedCells > 0
					&& openedBuildings > 0
					&& beforeGraceReseed == false
					&& positionAfterGraceProbe == positionBeforeGrace
					&& afterGraceReseed
					&& reseededIntoRightRoom
					&& activeAfterReseed.CellCount == 1
					&& activeAfterReseed.RelocationCellDebt > 0
					&& activeAfterReseed.LinkedHost == host;
				symbiant = activeAfterReseed;
				return new
				{
					success,
					fixtureSetup,
					host = DescribeRelocationHost(host),
					initial,
					addedCells,
					beforeOpen,
					openedBuildings,
					afterOpen,
					beforeGraceReseed,
					graceKeptOriginalPosition = positionAfterGraceProbe == positionBeforeGrace,
					afterGraceProbe,
					afterGraceReseed,
					afterReseed,
					reseededIntoRightRoom
				};
			}
			catch (Exception ex)
			{
				return new { success = false, error = ex.ToString(), fixtureSetup };
			}
			finally
			{
				_ = CleanupTemporarySymbiant(map, symbiant, cleanup);
				_ = CleanupTemporaryPawn(host, cleanup);
				_ = CleanupSymbiantExpansionFixture(map, fixture, cleanup);
			}
		}

		static object RunSymbiantMovableCellReuseScenario(Map map, bool cleanup)
		{
			SymbiantExpansionFixture fixture = null;
			ZombieSymbiant symbiant = null;
			Pawn host = null;
			object fixtureSetup = null;
			try
			{
				if (TrySetupSymbiantExpansionFixture(map, out fixture, out var fixtureError) == false)
					return fixtureError;
				fixtureSetup = DescribeSymbiantExpansionFixture(fixture);
				host = SpawnSymbiantRelocationHost(map, fixture.rightInterior.CenterCell);
				symbiant = SpawnAssignedSymbiantForRelocationContract(map, fixture.rightInterior.CenterCell, host);
				var openedBuildings = DestroyFixtureBuildings(map, fixture, building => IsAdjacentToRect(building.Position, fixture.leftInterior) && fixture.dividerWalls.Contains(building) == false);
				var outdoorCell = fixture.leftInterior.CenterCell;
				var rightCellsBefore = CountSymbiantCellsInRect(symbiant, fixture.rightInterior);
				var addedOutdoorCells = ZombieSymbiant.AddCells(map, new[] { outdoorCell });
				var beforePulse = DescribeSymbiantRelocationState(symbiant, host);
				var containedOutdoorBefore = symbiant.ContainsCell(outdoorCell);
				var cellCountBefore = symbiant.CellCount;
				ForceSymbiantRelocationPulseReady(symbiant);
				var pulse = InvokeSymbiantTryRelocationPulse(symbiant);
				var afterPulse = DescribeSymbiantRelocationState(symbiant, host);
				var containedOutdoorAfter = symbiant.ContainsCell(outdoorCell);
				var rightCellsAfter = CountSymbiantCellsInRect(symbiant, fixture.rightInterior);
				var success = openedBuildings > 0
					&& addedOutdoorCells == 1
					&& containedOutdoorBefore
					&& pulse
					&& containedOutdoorAfter == false
					&& symbiant.CellCount == cellCountBefore
					&& rightCellsAfter > rightCellsBefore;
				return new
				{
					success,
					fixtureSetup,
					host = DescribeRelocationHost(host),
					openedBuildings,
					outdoorCell = ZombieRuntimeActions.DescribeCell(outdoorCell),
					addedOutdoorCells,
					rightCellsBefore,
					beforePulse,
					containedOutdoorBefore,
					pulse,
					afterPulse,
					containedOutdoorAfter,
					rightCellsAfter
				};
			}
			catch (Exception ex)
			{
				return new { success = false, error = ex.ToString(), fixtureSetup };
			}
			finally
			{
				_ = CleanupTemporarySymbiant(map, symbiant, cleanup);
				_ = CleanupTemporaryPawn(host, cleanup);
				_ = CleanupSymbiantExpansionFixture(map, fixture, cleanup);
			}
		}

		static object RunSymbiantNoRoomDormancyScenario(Map map, bool cleanup)
		{
			SymbiantExpansionFixture fixture = null;
			ZombieSymbiant symbiant = null;
			Pawn host = null;
			object fixtureSetup = null;
			try
			{
				if (TrySetupSymbiantExpansionFixture(map, out fixture, out var fixtureError) == false)
					return fixtureError;
				fixtureSetup = DescribeSymbiantExpansionFixture(fixture);
				host = SpawnSymbiantRelocationHost(map, fixture.rightInterior.CenterCell);
				symbiant = SpawnAssignedSymbiantForRelocationContract(map, fixture.spawnCell, host);
				var addedCells = ZombieSymbiant.AddCells(map, fixture.leftInterior.Cells.Where(cell => cell.InBounds(map) && cell.Standable(map)));
				var beforeOpen = DescribeSymbiantRelocationState(symbiant, host);
				var cellCountBeforeOpen = symbiant.CellCount;
				var removedBuildings = DestroyFixtureBuildings(map, fixture, _ => true);
				var afterOpen = DescribeSymbiantRelocationState(symbiant, host);
				_ = InvokeSymbiantTryReseedIfUprooted(symbiant);
				ExpireSymbiantUprootedGrace(symbiant);
				var reseedAfterGrace = InvokeSymbiantTryReseedIfUprooted(symbiant);
				var expansionPulse = symbiant.TryExpansionPulse();
				var afterPulses = DescribeSymbiantRelocationState(symbiant, host);
				var success = addedCells > 0
					&& removedBuildings > 0
					&& reseedAfterGrace == false
					&& expansionPulse == false
					&& symbiant.CellCount == cellCountBeforeOpen
					&& symbiant.GrowthState == "dormantNoRoom";
				return new
				{
					success,
					fixtureSetup,
					host = DescribeRelocationHost(host),
					addedCells,
					beforeOpen,
					removedBuildings,
					afterOpen,
					reseedAfterGrace,
					expansionPulse,
					afterPulses
				};
			}
			catch (Exception ex)
			{
				return new { success = false, error = ex.ToString(), fixtureSetup };
			}
			finally
			{
				_ = CleanupTemporarySymbiant(map, symbiant, cleanup);
				_ = CleanupTemporaryPawn(host, cleanup);
				_ = CleanupSymbiantExpansionFixture(map, fixture, cleanup);
			}
		}

		static Pawn SpawnSymbiantRelocationHost(Map map, IntVec3 cell)
		{
			var host = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(host, cell, map, Rot4.South);
			DisablePawnWork(host);
			host.needs?.AddOrRemoveNeedsAsAppropriate();
			host.mindState?.mentalStateHandler?.Reset();
			return host;
		}

		static ZombieSymbiant SpawnAssignedSymbiantForRelocationContract(Map map, IntVec3 spawnCell, Pawn host)
		{
			ZombieSymbiant.Spawn(map, spawnCell);
			var symbiant = ZombieSymbiant.ActiveSymbiant(map) ?? throw new InvalidOperationException("Symbiant spawn did not create an active symbiant.");
			var originalHost = symbiant.LinkedHost;
			if (originalHost != null && originalHost != host)
				AccessTools.Method(typeof(ZombieSymbiant), "RemoveHostHediff")?.Invoke(null, new object[] { originalHost });
			AccessTools.Method(typeof(ZombieSymbiant), "AssignHost")?.Invoke(symbiant, new object[] { host });
			RepairHostLink(symbiant);
			return symbiant;
		}

		static bool InvokeSymbiantTryReseedIfUprooted(ZombieSymbiant symbiant)
		{
			if (symbiant == null)
				return false;
			var result = AccessTools.Method(typeof(ZombieSymbiant), "TryReseedIfUprooted")?.Invoke(symbiant, Array.Empty<object>());
			return result is bool value && value;
		}

		static bool InvokeSymbiantTryRelocationPulse(ZombieSymbiant symbiant)
		{
			if (symbiant == null)
				return false;
			var result = AccessTools.Method(typeof(ZombieSymbiant), "TryRelocationPulse")?.Invoke(symbiant, Array.Empty<object>());
			return result is bool value && value;
		}

		static void ExpireSymbiantUprootedGrace(ZombieSymbiant symbiant)
		{
			AccessTools.Field(typeof(ZombieSymbiant), "uprootedSinceTick")?.SetValue(symbiant, GenTicks.TicksGame - GenDate.TicksPerHour * 4 - 1);
		}

		static void ForceSymbiantRelocationPulseReady(ZombieSymbiant symbiant)
		{
			AccessTools.Field(typeof(ZombieSymbiant), "nextRelocationPulseTick")?.SetValue(symbiant, GenTicks.TicksGame);
		}

		static int DestroyFixtureBuildings(Map map, SymbiantExpansionFixture fixture, Func<Building, bool> predicate)
		{
			if (map == null || fixture == null)
				return 0;
			var removed = 0;
			foreach (var building in fixture.buildings.Where(building => building != null && building.Destroyed == false && predicate(building)).ToArray())
			{
				building.Destroy(DestroyMode.Vanish);
				removed++;
			}
			map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
			return removed;
		}

		static bool IsAdjacentToRect(IntVec3 cell, CellRect rect)
		{
			return rect.Cells.Any(interior => GenAdj.CardinalDirections.Any(direction => interior + direction == cell));
		}

		static int CountSymbiantCellsInRect(ZombieSymbiant symbiant, CellRect rect)
		{
			if (symbiant == null)
				return 0;
			return rect.Cells.Count(symbiant.ContainsCell);
		}

		static object CleanupTemporaryPawn(Pawn pawn, bool cleanup)
		{
			if (pawn == null)
				return new { removed = false, skipped = cleanup == false };
			if (cleanup == false)
				return new { removed = false, skipped = true, pawn = ZombieRuntimeActions.StableThingId(pawn) };
			if (pawn.Destroyed)
				return new { removed = false, skipped = false, pawn = ZombieRuntimeActions.StableThingId(pawn) };
			var id = ZombieRuntimeActions.StableThingId(pawn);
			if (pawn.Corpse != null && pawn.Corpse.Destroyed == false)
				pawn.Corpse.Destroy(DestroyMode.Vanish);
			else if (pawn.Dead == false)
				pawn.Destroy(DestroyMode.Vanish);
			return new { removed = pawn.Destroyed || pawn.Corpse?.Destroyed == true, skipped = pawn.Dead && pawn.Corpse == null, pawn = id };
		}

		static object DescribeSymbiantRelocationState(ZombieSymbiant symbiant, Pawn expectedHost)
		{
			if (symbiant == null)
				return null;
			var map = symbiant.Spawned ? symbiant.Map : null;
			var host = symbiant.LinkedHost;
			var position = symbiant.Spawned ? symbiant.Position : IntVec3.Invalid;
			return new
			{
				symbiant = ZombieRuntimeActions.StableThingId(symbiant),
				spawned = symbiant.Spawned,
				destroyed = symbiant.Destroyed,
				position = position.IsValid ? ZombieRuntimeActions.DescribeCell(position) : null,
				room = map == null || position.IsValid == false ? null : DescribeRoom(position.GetRoom(map)),
				cellCount = symbiant.CellCount,
				growthState = symbiant.GrowthState,
				relocationCellDebt = symbiant.RelocationCellDebt,
				nextRelocationPulseTick = symbiant.NextRelocationPulseTick,
				linkedHost = ZombieRuntimeActions.StableThingId(host),
				expectedHost = ZombieRuntimeActions.StableThingId(expectedHost),
				linkPreserved = host == expectedHost,
				hostRoom = host?.Spawned == true ? DescribeRoom(host.Position.GetRoom(host.Map)) : null
			};
		}

		static object DescribeRelocationHost(Pawn host)
		{
			if (host == null)
				return null;
			return new
			{
				host = ZombieRuntimeActions.StableThingId(host),
				label = host.LabelShortCap,
				spawned = host.Spawned,
				position = host.Spawned ? ZombieRuntimeActions.DescribeCell(host.Position) : null,
				room = host.Spawned ? DescribeRoom(host.Position.GetRoom(host.Map)) : null
			};
		}

		sealed class SymbiantExpansionFixture
		{
			public CellRect fixtureRect;
			public CellRect leftInterior;
			public CellRect rightInterior;
			public IntVec3 spawnCell;
			public IntVec3 doorCell;
			public Building_Door door;
			public readonly List<Building> buildings = new();
			public readonly List<Building> dividerWalls = new();
			public readonly Dictionary<IntVec3, bool> originalHome = new();
			public readonly Dictionary<IntVec3, RoofDef> originalRoof = new();
		}

		static bool TrySetupSymbiantExpansionFixture(Map map, out SymbiantExpansionFixture fixture, out object error)
		{
			fixture = null;
			error = null;
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindSymbiantExpansionFixtureRoot(map, root, 56f, out var center, out error) == false)
				return false;

			var leftInterior = CellRect.FromLimits(center.x - 5, center.z - 2, center.x - 1, center.z + 2);
			var rightInterior = CellRect.FromLimits(center.x + 1, center.z - 2, center.x + 5, center.z + 2);
			var fixtureRect = CellRect.FromLimits(center.x - 6, center.z - 3, center.x + 6, center.z + 3).ClipInsideMap(map);
			var doorCell = new IntVec3(center.x - 3, 0, center.z - 3);
			fixture = new SymbiantExpansionFixture
			{
				fixtureRect = fixtureRect,
				leftInterior = leftInterior,
				rightInterior = rightInterior,
				spawnCell = leftInterior.CenterCell,
				doorCell = doorCell
			};

			foreach (var cell in fixtureRect.Cells)
			{
				fixture.originalHome[cell] = map.areaManager.Home[cell];
				fixture.originalRoof[cell] = map.roofGrid.RoofAt(cell);
				map.areaManager.Home[cell] = true;
				map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);
			}

			var wallDef = ThingDefOf.Wall;
			var doorDef = ThingDefOf.Door;
			var stuffDef = ThingDefOf.WoodLog;
			for (var x = fixtureRect.minX; x <= fixtureRect.maxX; x++)
				for (var z = fixtureRect.minZ; z <= fixtureRect.maxZ; z++)
				{
					var cell = new IntVec3(x, 0, z);
					var edge = x == fixtureRect.minX || x == fixtureRect.maxX || z == fixtureRect.minZ || z == fixtureRect.maxZ;
					var divider = x == center.x && z >= center.z - 2 && z <= center.z + 2;
					if (edge == false && divider == false)
						continue;
					if (cell == doorCell)
					{
						var door = ThingMaker.MakeThing(doorDef, stuffDef) as Building_Door;
						if (door == null)
						{
							error = new { success = false, error = "Could not create symbiant expansion fixture door." };
							return false;
						}
						GenSpawn.Spawn(door, cell, map, WipeMode.Vanish);
						door.SetFaction(Faction.OfPlayer);
						fixture.door = door;
						fixture.buildings.Add(door);
						continue;
					}

					var wall = ThingMaker.MakeThing(wallDef, stuffDef) as Building;
					if (wall == null)
						continue;
					GenSpawn.Spawn(wall, cell, map, WipeMode.Vanish);
					wall.SetFaction(Faction.OfPlayer);
					fixture.buildings.Add(wall);
					if (divider)
						fixture.dividerWalls.Add(wall);
				}

			map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
			var leftRoom = fixture.spawnCell.GetRoom(map);
			var rightRoom = rightInterior.CenterCell.GetRoom(map);
			if (leftRoom == null || rightRoom == null || leftRoom == rightRoom || leftRoom.ProperRoom == false || rightRoom.ProperRoom == false || leftRoom.UsesOutdoorTemperature || rightRoom.UsesOutdoorTemperature)
			{
				error = new
				{
					success = false,
					leftRoom = DescribeRoom(leftRoom),
					rightRoom = DescribeRoom(rightRoom),
					error = "The symbiant expansion fixture did not produce two distinct proper indoor rooms."
				};
				return false;
			}
			return true;
		}

		static bool TryFindSymbiantExpansionFixtureRoot(Map map, IntVec3 root, float radius, out IntVec3 center, out object error)
		{
			center = IntVec3.Invalid;
			error = null;
			foreach (var candidate in GenRadial.RadialCellsAround(root, radius, true))
			{
				var rect = CellRect.FromLimits(candidate.x - 6, candidate.z - 3, candidate.x + 6, candidate.z + 3);
				if (rect.InBounds(map) == false)
					continue;
				var clear = true;
				foreach (var cell in rect.Cells)
				{
					if (cell.Fogged(map)
						|| cell.Standable(map) == false
						|| cell.GetEdifice(map) != null
						|| cell.GetFirstThing<Mineable>(map) != null
						|| cell.GetThingList(map).Any(thing => thing is Pawn))
					{
						clear = false;
						break;
					}
				}
				if (clear)
				{
					center = candidate;
					return true;
				}
			}
			error = new
			{
				success = false,
				error = $"No clear symbiant expansion fixture area was found near ({root.x}, {root.z})."
			};
			return false;
		}

		static object DescribeSymbiantExpansionFixture(SymbiantExpansionFixture fixture)
		{
			if (fixture == null)
				return null;
			var map = fixture.door?.Map;
			return new
			{
				fixtureRect = ZombieRuntimeActions.DescribeCellRect(fixture.fixtureRect),
				leftInterior = ZombieRuntimeActions.DescribeCellRect(fixture.leftInterior),
				rightInterior = ZombieRuntimeActions.DescribeCellRect(fixture.rightInterior),
				spawnCell = ZombieRuntimeActions.DescribeCell(fixture.spawnCell),
				doorCell = ZombieRuntimeActions.DescribeCell(fixture.doorCell),
				leftRoom = map == null ? null : DescribeRoom(fixture.spawnCell.GetRoom(map)),
				rightRoom = map == null ? null : DescribeRoom(fixture.rightInterior.CenterCell.GetRoom(map)),
				dividerWallCells = fixture.dividerWalls.Select(wall => ZombieRuntimeActions.DescribeCell(wall.Position)).ToArray()
			};
		}

		static object DescribeRoom(Room room)
		{
			if (room == null)
				return null;
			return new
			{
				role = room.Role?.defName,
				roleLabel = room.Role?.LabelCap.ToString(),
				cellCount = room.CellCount,
				isHuge = room.IsHuge,
				properRoom = room.ProperRoom,
				usesOutdoorTemperature = room.UsesOutdoorTemperature
			};
		}

		static object CleanupSymbiantExpansionFixture(Map map, SymbiantExpansionFixture fixture, bool cleanup)
		{
			if (fixture == null)
				return new { removed = 0, restoredCells = 0, skipped = cleanup == false };
			if (cleanup == false)
				return new { removed = 0, restoredCells = 0, skipped = true };

			var removed = 0;
			foreach (var thing in fixture.buildings.Where(thing => thing != null).ToArray())
			{
				if (thing.Destroyed)
					continue;
				thing.Destroy(DestroyMode.Vanish);
				removed++;
			}
			foreach (var pair in fixture.originalHome)
				map.areaManager.Home[pair.Key] = pair.Value;
			foreach (var pair in fixture.originalRoof)
				map.roofGrid.SetRoof(pair.Key, pair.Value);
			map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
			return new { removed, restoredCells = fixture.originalHome.Count, skipped = false };
		}

		[Tool("zombieland/symbiant_render_blob", Description = "Create, inspect, or clean up a hostless zombie symbiant from an explicit cell list for blob rendering tests.")]
		public static object SymbiantRenderBlob(
			[ToolParameter(Description = "Mode: create, read, cleanup.", Required = false, DefaultValue = "create")] string mode = "create",
			[ToolParameter(Description = "Cell list. Use x,z entries separated by semicolon, pipe, or newline. Relative offsets by default, e.g. 0,0;1,0;0,1;1,1.", Required = false, DefaultValue = "0,0;1,0;0,1;1,1")] string cells = "0,0;1,0;0,1;1,1",
			[ToolParameter(Description = "Origin x coordinate for relative cells. Use -1 with z -1 for automatic placement.", Required = false, DefaultValue = -1)] int x = -1,
			[ToolParameter(Description = "Origin z coordinate for relative cells. Use -1 with x -1 for automatic placement.", Required = false, DefaultValue = -1)] int z = -1,
			[ToolParameter(Description = "Treat the cell list as absolute map coordinates instead of offsets from the origin.", Required = false, DefaultValue = false)] bool absolute = false,
			[ToolParameter(Description = "Destroy the current active symbiant without host trauma before creating the render test blob.", Required = false, DefaultValue = true)] bool replaceExisting = true,
			[ToolParameter(Description = "Select the created/read symbiant after the action.", Required = false, DefaultValue = true)] bool select = true,
			[ToolParameter(Description = "Jump the camera to the created/read symbiant after the action.", Required = false, DefaultValue = true)] bool jump = true,
			[ToolParameter(Description = "Bridge-only debug performance profile to apply before the action. Empty keeps current profile; renderOnly is useful for visual testing.", Required = false, DefaultValue = "")] string perfProfile = "")
		{
			var perfAction = perfProfile.NullOrEmpty() ? null : ZombieSymbiant.SetDebugPerfProfile(perfProfile);
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded.", perfAction };

			mode = (mode ?? "create").Trim();
			var before = ZombieSymbiant.ActiveSymbiant(map);
			object action;
			if (mode.Equals("cleanup", StringComparison.OrdinalIgnoreCase))
			{
				var beforeId = ZombieRuntimeActions.StableThingId(before);
				before?.DebugDestroyWithoutHostTrauma();
				var after = ZombieSymbiant.ActiveSymbiant(map);
				action = new
				{
					cleaned = before != null && (after == null || after.Destroyed),
					before = beforeId,
					after = ZombieRuntimeActions.StableThingId(after)
				};
			}
			else if (mode.Equals("read", StringComparison.OrdinalIgnoreCase))
				action = new { readOnly = true };
			else if (mode.Equals("create", StringComparison.OrdinalIgnoreCase))
			{
				if (TryParseSymbiantRenderCells(cells, out var parsedCells, out var parseError) == false)
					return new { success = false, error = parseError, perfAction };
				if (parsedCells.Length == 0)
					return new { success = false, error = "At least one render-test cell is required.", perfAction };

				var root = ResolveSymbiantRenderRoot(map, parsedCells, absolute, x, z);
				if (root.InBounds(map) == false)
					return new { success = false, error = "The requested symbiant render-test root is outside the current map.", root = ZombieRuntimeActions.DescribeCell(root), perfAction };

				if (before != null)
				{
					if (replaceExisting == false)
						return new { success = false, error = "An active symbiant already exists on the current map.", activeSymbiant = ZombieRuntimeActions.StableThingId(before), perfAction };
					before.DebugDestroyWithoutHostTrauma();
				}

				var absoluteCells = absolute
					? parsedCells
					: parsedCells.Select(cell => root + cell).ToArray();
				absoluteCells = absoluteCells
					.Where(cell => cell.InBounds(map))
					.Distinct()
					.Take(ZombieSymbiant.MaxCells)
					.ToArray();
				var symbiant = ZombieSymbiant.DebugSpawnForRendering(map, root, absoluteCells);
				action = new
				{
					created = symbiant != null,
					replaced = before != null,
					root = ZombieRuntimeActions.DescribeCell(root),
					requestedCells = parsedCells.Length,
					absoluteInput = absolute,
					inBoundsCells = absoluteCells.Length,
					truncatedToMaxCells = absoluteCells.Length >= ZombieSymbiant.MaxCells && parsedCells.Length > absoluteCells.Length
				};
			}
			else
				return new { success = false, error = $"Unknown mode '{mode}'. Expected create, read, or cleanup.", perfAction };

			var current = ZombieSymbiant.ActiveSymbiant(map);
			if (current != null && select)
			{
				Find.Selector.ClearSelection();
				Find.Selector.Select(current, false, false);
			}
			if (current != null && jump)
				CameraJumper.TryJump(new GlobalTargetInfo(current.Position, map));

			return DescribeSymbiantRenderBlobResult(map, mode, action, perfAction, current);
		}

		static IntVec3 ResolveSymbiantRenderRoot(Map map, IntVec3[] parsedCells, bool absolute, int x, int z)
		{
			if (x >= 0 && z >= 0)
				return new IntVec3(x, 0, z);
			if (absolute && parsedCells.Length > 0)
				return parsedCells[0];

			var center = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			return TryFindClearSpawnCell(map, center, 24f, out var cell, out _) ? cell : center;
		}

		static bool TryParseSymbiantRenderCells(string value, out IntVec3[] cells, out string error)
		{
			var result = new List<IntVec3>();
			foreach (var entry in (value ?? "").Split(new[] { ';', '|', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
			{
				var parts = entry.Split(new[] { ',', ':' }, StringSplitOptions.RemoveEmptyEntries);
				if (parts.Length != 2)
				{
					cells = Array.Empty<IntVec3>();
					error = $"Could not parse cell '{entry}'. Expected x,z.";
					return false;
				}
				if (int.TryParse(parts[0].Trim(), out var x) == false || int.TryParse(parts[1].Trim(), out var z) == false)
				{
					cells = Array.Empty<IntVec3>();
					error = $"Could not parse cell '{entry}'. Expected integer x,z.";
					return false;
				}
				result.Add(new IntVec3(x, 0, z));
			}
			cells = result.Distinct().ToArray();
			error = null;
			return true;
		}

		static object DescribeSymbiantRenderBlobResult(Map map, string mode, object action, object perfAction, ZombieSymbiant symbiant)
		{
			var selectorRect = symbiant?.CustomRectForSelector;
			return new
			{
				success = true,
				mode,
				action,
				perf = ZombieSymbiant.DebugPerfState(),
				perfAction,
				selected = symbiant != null && Find.Selector.IsSelected(symbiant),
				symbiant = symbiant == null ? null : new
				{
					id = ZombieRuntimeActions.StableThingId(symbiant),
					position = ZombieRuntimeActions.DescribeCell(symbiant.Position),
					cellCount = symbiant.CellCount,
					cells = symbiant.AbsoluteCells.Select(ZombieRuntimeActions.DescribeCell).ToArray(),
					selectorRect = selectorRect.HasValue ? ZombieRuntimeActions.DescribeCellRect(selectorRect.Value) : null,
					selectorCoversAllCells = selectorRect.HasValue && symbiant.AbsoluteCells.All(cell => selectorRect.Value.Contains(cell)),
					occupiedDrawRect = ZombieRuntimeActions.DescribeCellRect(symbiant.OccupiedDrawRect()),
					drawSize = new { x = symbiant.DrawSize.x, z = symbiant.DrawSize.y },
					renderWorldSize = new { x = symbiant.RenderWorldSize.x, z = symbiant.RenderWorldSize.y },
					renderTextureSize = new { x = symbiant.RenderTextureWidth, y = symbiant.RenderTextureHeight },
					renderShader = symbiant.RenderShaderName,
					renderUsesSymbiantShader = symbiant.RenderUsesSymbiantShader,
					renderUsesGpuMetaballMask = symbiant.RenderUsesGpuMetaballMask,
					renderMetaballElements = symbiant.RenderMetaballElementCount,
					activeCellMotions = symbiant.ActiveCellMotionCount,
					mapSize = new { x = map.Size.x, z = map.Size.z }
				}
			};
		}

		[Tool("zombieland/symbiant_infestation_state", Description = "Inspect or exercise the zombie symbiant state with spawn, createEvent, expand, shrink, feedCorpse, removeHostHediff, killHost, contaminationStep, stress, and cleanup modes.")]
		public static object SymbiantInfestationState(
			[ToolParameter(Description = "Mode: read, spawn, createEvent, expand, shrink, feedCorpse, removeHostHediff, killHost, contaminationStep, stress, cleanup.", Required = false, DefaultValue = "read")] string mode = "read",
			[ToolParameter(Description = "Target x coordinate for spawn/stress. Use -1 with z -1 for automatic placement.", Required = false, DefaultValue = -1)] int x = -1,
			[ToolParameter(Description = "Target z coordinate for spawn/stress. Use -1 with x -1 for automatic placement.", Required = false, DefaultValue = -1)] int z = -1,
			[ToolParameter(Description = "Number of expansion pulses or stress cells.", Required = false, DefaultValue = 1)] int count = 1,
			[ToolParameter(Description = "Bridge-only debug performance profile: default, inert, renderOnly, pathOnly, symbiosisOnly, noRender, noPath, noCellStats, or noTick.", Required = false, DefaultValue = "")] string perfProfile = "",
			[ToolParameter(Description = "Bridge-only max-cell override for stress testing. Use 0 to keep normal settings.", Required = false, DefaultValue = 0)] int maxCellsOverride = 0)
		{
			object perfAction = null;
			if (perfProfile.NullOrEmpty() == false)
				perfAction = ZombieSymbiant.SetDebugPerfProfile(perfProfile);
			object maxCellsOverrideAction = null;
			if (maxCellsOverride >= 0)
				maxCellsOverrideAction = ZombieSymbiant.SetDebugMaxCellsOverride(maxCellsOverride);

			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded.", perf = ZombieSymbiant.DebugPerfState(), perfAction, maxCellsOverrideAction };

			mode = (mode ?? "read").Trim();
			var symbiant = ZombieSymbiant.ActiveSymbiant(map);
			object action = null;

			if (mode.Equals("profile", StringComparison.OrdinalIgnoreCase))
				action = ZombieSymbiant.DebugPerfState();
			else if (mode.Equals("spawn", StringComparison.OrdinalIgnoreCase))
			{
				if (symbiant == null)
				{
					if (x >= 0 && z >= 0)
						ZombieSymbiant.Spawn(map, new IntVec3(x, 0, z));
					else if (ZombieSymbiant.TrySpawnInBestRoom(map) == false)
					{
						var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
						if (TryFindClearSpawnCell(map, root, 16f, out var cell, out var error) == false)
							return error;
						ZombieSymbiant.Spawn(map, cell);
					}
					symbiant = ZombieSymbiant.ActiveSymbiant(map);
				}
				action = new { spawned = symbiant?.Spawned == true };
			}
			else if (mode.Equals("createEvent", StringComparison.OrdinalIgnoreCase))
			{
				var before = symbiant == null ? null : ZombieRuntimeActions.StableThingId(symbiant);
				var created = false;
				if (symbiant == null)
				{
					created = ZombieSymbiant.TrySpawnInBestRoom(map, false);
					symbiant = ZombieSymbiant.ActiveSymbiant(map);
				}
				action = new
				{
					before,
					created,
					after = symbiant == null ? null : ZombieRuntimeActions.StableThingId(symbiant),
					cellCount = symbiant?.CellCount ?? 0
				};
			}
			else if (mode.Equals("expand", StringComparison.OrdinalIgnoreCase))
			{
				var before = symbiant?.CellCount ?? 0;
				var pulses = 0;
				for (var i = 0; i < Math.Max(1, count); i++)
					if (symbiant?.DebugExpansionPulse() == true)
						pulses++;
				action = new { before, pulses, after = symbiant?.CellCount ?? 0 };
			}
			else if (mode.Equals("shrink", StringComparison.OrdinalIgnoreCase))
			{
				var before = symbiant?.CellCount ?? 0;
				var pulses = 0;
				for (var i = 0; i < Math.Max(1, count); i++)
					if (symbiant?.DebugShrinkPulse() == true)
						pulses++;
				action = new { before, pulses, after = symbiant?.Destroyed == true ? 0 : symbiant?.CellCount ?? 0 };
			}
			else if (mode.Equals("feedCorpse", StringComparison.OrdinalIgnoreCase))
			{
				var before = symbiant?.CellCount ?? 0;
				Corpse feedCorpse = null;
				object feedError = null;
				var fed = false;
				var expectedGrowth = 0;
				if (symbiant == null)
					feedError = "No active symbiant.";
				else if (TryFindClearSpawnCell(map, symbiant.Position + new IntVec3(2, 0, 0), 16f, out var feedCell, out var feedCellError) == false)
					feedError = feedCellError;
				else if (TryCreateSymbiantFeedCorpse(map, feedCell, true, "ZL_SymbiantState_FeedCorpse", null, out feedCorpse, out var corpseError) == false)
					feedError = corpseError;
				else
				{
					expectedGrowth = ZombieSymbiant.FeedGrowthCellCount(feedCorpse);
					fed = symbiant.TryFeed(feedCorpse);
					if (fed == false && feedCorpse.Destroyed == false)
						feedCorpse.Destroy(DestroyMode.Vanish);
				}
				action = new
				{
					before,
					feedError,
					feed = ZombieRuntimeActions.StableThingId(feedCorpse),
					feedDef = feedCorpse?.def?.defName,
					expectedGrowth,
					fed,
					feedGrowthCells = symbiant?.LastRecessionPulseCells ?? 0,
					after = symbiant?.Destroyed == true ? 0 : symbiant?.CellCount ?? 0,
					feedDestroyed = feedCorpse?.Destroyed ?? false
				};
			}
			else if (mode.Equals("removeHostHediff", StringComparison.OrdinalIgnoreCase))
			{
				var linkedHost = symbiant?.LinkedHost;
				var hediffs = linkedHost?.health?.hediffSet?.hediffs?
					.Where(hediff => hediff.def == CustomDefs.SymbiantSymbiosis)
					.ToArray() ?? Array.Empty<Hediff>();
				foreach (var hediff in hediffs)
					linkedHost.health.RemoveHediff(hediff);
				action = new
				{
					host = linkedHost == null ? null : ZombieRuntimeActions.StableThingId(linkedHost),
					removed = hediffs.Length
				};
			}
			else if (mode.Equals("killHost", StringComparison.OrdinalIgnoreCase))
			{
				var linkedHost = symbiant?.LinkedHost;
				var before = new
				{
					symbiant = ZombieRuntimeActions.StableThingId(symbiant),
					host = ZombieRuntimeActions.StableThingId(linkedHost),
					hostDead = linkedHost?.Dead ?? false,
					cellCount = symbiant?.CellCount ?? 0,
					symbiosisSevered = symbiant?.SymbiosisSevered ?? false
				};
				if (linkedHost != null && linkedHost.Dead == false)
					linkedHost.Kill(null);
				symbiant = ZombieSymbiant.ActiveSymbiant(map);
				var afterHost = symbiant?.LinkedHost;
				action = new
				{
					before,
					after = new
					{
						symbiant = ZombieRuntimeActions.StableThingId(symbiant),
						host = ZombieRuntimeActions.StableThingId(afterHost),
						hostDead = linkedHost?.Dead ?? false,
						cellCount = symbiant?.CellCount ?? 0,
						symbiosisSevered = symbiant?.SymbiosisSevered ?? false,
						ticksUntilNextRetreat = symbiant == null ? 0 : symbiant.NextExpansionTick - GenTicks.TicksGame
					}
				};
			}
			else if (mode.Equals("contaminationStep", StringComparison.OrdinalIgnoreCase))
				action = RunSymbiantContaminationStepProbe(map, x, z);
			else if (mode.Equals("cleanup", StringComparison.OrdinalIgnoreCase))
			{
				var id = symbiant == null ? null : ZombieRuntimeActions.StableThingId(symbiant);
				var before = symbiant?.CellCount ?? 0;
				symbiant?.DebugDestroyWithoutHostTrauma();
				symbiant = ZombieSymbiant.ActiveSymbiant(map);
				action = new
				{
					symbiant = id,
					before,
					cleaned = symbiant == null || symbiant.Destroyed
				};
			}
			else if (mode.Equals("stress", StringComparison.OrdinalIgnoreCase))
			{
				if (symbiant == null)
				{
					var root = x >= 0 && z >= 0 ? new IntVec3(x, 0, z) : new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
					if (TryFindClearSpawnCell(map, root, 16f, out var cell, out var error) == false)
						return error;
					ZombieSymbiant.Spawn(map, cell);
					symbiant = ZombieSymbiant.ActiveSymbiant(map);
				}
				var before = symbiant?.CellCount ?? 0;
				var requested = Math.Max(1, count);
				var targetBudget = Math.Max(requested, requested + before);
				var targetCells = new List<IntVec3>(targetBudget);
				var seen = new HashSet<IntVec3>();
				var stressRadius = Math.Max(30d, Math.Sqrt(requested / Math.PI) + 8d);
				foreach (var cell in GenRadial.RadialCellsAround(symbiant.Position, (float)stressRadius, true))
				{
					if (targetCells.Count >= targetBudget)
						break;
					if (cell.InBounds(map) && cell.Walkable(map) && seen.Add(cell))
						targetCells.Add(cell);
				}
				var radialCells = targetCells.Count;
				var squareRadius = Math.Max((int)Math.Ceiling(stressRadius), (int)Math.Ceiling(Math.Sqrt(requested)) / 2 + 8);
				if (targetCells.Count < targetBudget)
				{
					foreach (var cell in CellRect.CenteredOn(symbiant.Position, squareRadius).ClipInsideMap(map).Cells)
					{
						if (targetCells.Count >= targetBudget)
							break;
						if (cell.Walkable(map) && seen.Add(cell))
							targetCells.Add(cell);
					}
				}
				var squareCells = targetCells.Count - radialCells;
				var added = ZombieSymbiant.AddCells(map, targetCells);
				action = new
				{
					before,
					requested = count,
					targetBudget,
					added,
					after = symbiant?.CellCount ?? 0,
					stressRadius,
					radialCells,
					squareRadius,
					squareCells,
					targetCells = targetCells.Count,
					shape = radialCells >= targetBudget ? "circle" : "squareFill"
				};
			}

			symbiant = ZombieSymbiant.ActiveSymbiant(map);
			var host = symbiant?.LinkedHost;
			var severanceOperation = DescribeLiveSymbiantSeveranceOperation(host);
			var selectorRect = symbiant?.CustomRectForSelector;
			var hostSymbiosisHediff = host?.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) as Hediff_SymbiantSymbiosis;
			var room = symbiant?.Position.GetRoom(map);
			var roomDisruption = room == null ? null : new
			{
				role = room.Role?.defName,
				cellCount = ZombieSymbiant.CountCellsInRoom(room),
				beauty = room.GetStat(RoomStatDefOf.Beauty),
				impressiveness = room.GetStat(RoomStatDefOf.Impressiveness)
			};
			var playerFaction = Find.FactionManager?.AllFactionsListForReading?.FirstOrDefault(faction => faction?.def?.isPlayer == true);
			var symbiantHostileToPlayer = symbiant != null && playerFaction != null && symbiant.HostileTo(playerFaction);
			var symbiantActiveThreatToPlayer = symbiant != null && playerFaction != null && GenHostility.IsActiveThreatTo(symbiant, playerFaction, false, false);
			return new
			{
				success = true,
				mode,
				action,
				perf = ZombieSymbiant.DebugPerfState(),
				perfAction,
				maxCellsOverrideAction,
				symbiant = symbiant == null ? null : new
				{
					id = ZombieRuntimeActions.StableThingId(symbiant),
					position = ZombieRuntimeActions.DescribeCell(symbiant.Position),
					selectorRect = selectorRect.HasValue ? ZombieRuntimeActions.DescribeCellRect(selectorRect.Value) : null,
					selectorCoversAllCells = selectorRect.HasValue && symbiant.AbsoluteCells.All(cell => selectorRect.Value.Contains(cell)),
					drawSize = new { x = symbiant.DrawSize.x, z = symbiant.DrawSize.y },
					occupiedDrawRect = ZombieRuntimeActions.DescribeCellRect(symbiant.OccupiedDrawRect()),
					renderWorldSize = new { x = symbiant.RenderWorldSize.x, z = symbiant.RenderWorldSize.y },
						renderTextureSize = new { x = symbiant.RenderTextureWidth, y = symbiant.RenderTextureHeight },
						renderShader = symbiant.RenderShaderName,
						renderUsesSymbiantShader = symbiant.RenderUsesSymbiantShader,
						renderUsesGpuMetaballMask = symbiant.RenderUsesGpuMetaballMask,
						renderMetaballElements = symbiant.RenderMetaballElementCount,
						activeCellMotions = symbiant.ActiveCellMotionCount,
						renderOpacity = new
					{
						min = ZombieSymbiant.RenderOpacityMin,
						max = ZombieSymbiant.RenderOpacityMax,
						noiseScale = ZombieSymbiant.RenderNoiseScale,
						wavePhaseSpeed = ZombieSymbiant.RenderWavePhaseSpeed,
						waveShadeStrength = ZombieSymbiant.RenderWaveShadeStrength,
						edgeContrast = ZombieSymbiant.RenderEdgeContrast,
						noiseTimeSeconds = ZombieSymbiant.RenderNoiseTimeSeconds
					},
					pawnSystems = new
					{
						registeredInMapPawnLists = symbiant.RegisteredInMapPawnLists,
						hostileToPlayer = symbiantHostileToPlayer,
						activeThreatToPlayer = symbiantActiveThreatToPlayer,
						faction = symbiant.Faction?.def?.defName,
						kindIsFighter = symbiant.kindDef?.isFighter ?? false,
						combatPower = symbiant.kindDef?.combatPower ?? 0f
					},
					cellCount = symbiant.CellCount,
					maxCells = ZombieSymbiant.MaxCells,
					technicalMaxCells = ZombieSymbiant.MAX_METABALLS,
					debugMaxCellsOverride = ZombieSymbiant.DebugMaxCellsOverride,
					capped = symbiant.CellCount >= ZombieSymbiant.MaxCells,
					growthState = symbiant.GrowthState,
					nextBenefitCellSize = symbiant.NextBenefitCellSize,
						hostBenefitCount = symbiant.HostBenefitCount,
						benefitSummary = symbiant.BenefitSummary,
						effectSummary = symbiant.EffectSummary,
						inspectString = symbiant.GetInspectString(),
						descriptionFlavor = symbiant.DescriptionFlavor,
						descriptionDetailed = symbiant.DescriptionDetailed,
						specialDisplayStats = symbiant.SpecialDisplayStats().Select(DescribeStatDrawEntry).ToArray(),
						sharedHealthPercent = symbiant.SharedHealthPercentDisplay,
						sharedHealthSummary = symbiant.SharedHealthSummary,
						sharedDamageLeakPercent = symbiant.SharedDamageLeakPercentDisplay,
						sharedDamageAbsorbPercent = symbiant.SharedDamageAbsorbPercentDisplay,
					symbiosisSevered = symbiant.SymbiosisSevered,
					host = host == null ? null : new
					{
						id = ZombieRuntimeActions.StableThingId(host),
						label = host.LabelShortCap,
						position = host.Spawned ? ZombieRuntimeActions.DescribeCell(host.Position) : null,
						infectionState = host.InfectionState().ToString(),
						hasSymbiosisHediff = hostSymbiosisHediff != null,
						symbiosisHediffSeverity = hostSymbiosisHediff?.Severity ?? 0f
					},
					severanceOperation,
					hostThingId = symbiant.HostThingId,
					eligibleColonyRoomCells = symbiant.EligibleColonyRoomCells,
					fullBenefitCells = symbiant.FullBenefitCells,
					integratedVisibleCells = symbiant.IntegratedVisibleCells,
					benefitFactor = symbiant.BenefitFactor,
					hasZombieTargetingProtection = ZombieSymbiant.HasZombieTargetingProtection(host),
					damageAbsorptionBuffer = symbiant.DamageAbsorptionBuffer,
					damageAbsorptionBufferMax = symbiant.DamageAbsorptionBufferMax,
					canSafelySever = symbiant.CanSafelySever,
					feedRequested = symbiant.FeedRequested,
					nextExpansionTick = symbiant.NextExpansionTick,
					relocationCellDebt = symbiant.RelocationCellDebt,
					nextRelocationPulseTick = symbiant.NextRelocationPulseTick,
					uprootedSinceTick = symbiant.UprootedSinceTick,
					feedPausedUntilTick = symbiant.FeedPausedUntilTick,
					lastRecessionPulseCells = symbiant.LastRecessionPulseCells,
					cancelNextBreach = symbiant.CancelNextBreach,
					roomDisruption,
					sampleCells = symbiant.AbsoluteCells.Take(24).Select(ZombieRuntimeActions.DescribeCell).ToArray(),
					growthDiagnostics = DescribeSymbiantGrowthDiagnostics(map, symbiant)
				},
				settings = new
				{
					ZombieSettings.Values.symbiantEnabled,
					ZombieSettings.Values.symbiantMaxCells
				}
			};
		}

		static object DescribeSymbiantGrowthDiagnostics(Map map, ZombieSymbiant symbiant)
		{
			if (map == null || symbiant == null)
				return null;

			var occupied = symbiant.AbsoluteCells.ToHashSet();
			var candidates = new List<object>();
			var openTargets = 0;
			var wallTargets = 0;
			foreach (var from in occupied)
			{
				for (var i = 0; i < 4; i++)
				{
					var direction = GenAdj.CardinalDirections[i];
					var cell = from + direction;
					if (cell.InBounds(map) == false || occupied.Contains(cell))
						continue;

					var validOpenOrDoor = IsValidSymbiantCellForDiagnostics(map, cell);
					if (validOpenOrDoor)
						openTargets++;

					var wall = BreakableConstructedWallForDiagnostics(map, cell);
					var beyond = cell + direction;
					var beyondValid = beyond.InBounds(map)
						&& occupied.Contains(beyond) == false
						&& IsValidSymbiantCellForDiagnostics(map, beyond);
					var breachDestination = IntVec3.Invalid;
					var breachWallDepth = 0;
					var breachLeadsToValidCell = wall != null && TryFindConstructedWallBreachDestinationForDiagnostics(map, symbiant, cell, direction, out breachDestination, out breachWallDepth);
					if (breachLeadsToValidCell)
						wallTargets++;

					candidates.Add(new
					{
						from = ZombieRuntimeActions.DescribeCell(from),
						direction = direction.ToString(),
						cell = ZombieRuntimeActions.DescribeCell(cell),
						containsCell = symbiant.ContainsCell(cell),
						walkable = cell.Walkable(map),
						fogged = cell.Fogged(map),
						roof = cell.GetRoof(map)?.defName,
						openCell = CanOccupyOpenCellForDiagnostics(map, cell),
						doorCell = IsDoorCellForDiagnostics(map, cell),
						validOpenOrDoor,
						room = DescribeRoomForSymbiantGrowth(map, cell.GetRoom(map)),
						edifice = DescribeGrowthEdifice(map, cell),
						breakableWall = wall != null,
						beyond = beyond.InBounds(map) ? ZombieRuntimeActions.DescribeCell(beyond) : null,
						beyondValid,
						breachLeadsToValidCell,
						breachDestination = breachDestination.IsValid ? ZombieRuntimeActions.DescribeCell(breachDestination) : null,
						breachWallDepth,
						beyondRoom = beyond.InBounds(map) ? DescribeRoomForSymbiantGrowth(map, beyond.GetRoom(map)) : null
					});
				}
			}

			var occupiedRooms = occupied
				.Select(cell => cell.GetRoom(map))
				.Where(room => room != null)
				.Distinct()
				.Select(room => DescribeRoomForSymbiantGrowth(map, room))
				.ToArray();
			var host = symbiant.LinkedHost;
			return new
			{
				rootRoom = DescribeRoomForSymbiantGrowth(map, symbiant.Position.GetRoom(map)),
				hostRoom = host?.Spawned == true && host.Map == map ? DescribeRoomForSymbiantGrowth(map, host.Position.GetRoom(map)) : null,
				occupiedRooms,
				candidateCount = candidates.Count,
				openTargets,
				wallTargets,
				candidates = candidates.Take(80).ToArray()
			};
		}

		static object DescribeRoomForSymbiantGrowth(Map map, Room room)
		{
			if (room == null)
				return null;
			var hasHomeCell = map?.areaManager?.Home != null && room.Cells.Any(cell => map.areaManager.Home[cell]);
			return new
			{
				id = room.ID,
				role = room.Role?.defName,
				cellCount = room.CellCount,
				openRoofCount = room.OpenRoofCount,
				districtCount = room.DistrictCount,
				regionCount = room.RegionCount,
				touchesMapEdge = room.TouchesMapEdge,
				isDoorway = room.IsDoorway,
				fogged = room.Fogged,
				isHuge = room.IsHuge,
				usesOutdoorTemperature = room.UsesOutdoorTemperature,
				properRoom = room.ProperRoom,
				hasHomeCell,
				eligibleIndoorRoom = IsEligibleIndoorRoomForDiagnostics(room)
			};
		}

		static object DescribeGrowthEdifice(Map map, IntVec3 cell)
		{
			var edifice = cell.GetEdifice(map);
			if (edifice == null)
				return null;
			return new
			{
				def = edifice.def?.defName,
				label = edifice.LabelCap.ToString(),
				thingId = ZombieRuntimeActions.StableThingId(edifice),
				hitPoints = edifice.HitPoints,
				faction = edifice.Faction?.def?.defName,
				isDoor = edifice is Building_Door,
				useHitPoints = edifice.def?.useHitPoints ?? false,
				isNaturalRock = edifice.def?.building?.isNaturalRock ?? false,
				mineable = edifice.def?.mineable ?? false
			};
		}

		static bool TryFindConstructedWallBreachDestinationForDiagnostics(Map map, ZombieSymbiant symbiant, IntVec3 firstWallCell, IntVec3 direction, out IntVec3 destination, out int wallDepth)
		{
			destination = IntVec3.Invalid;
			wallDepth = 0;
			var cell = firstWallCell;
			for (var depth = 0; depth <= 4; depth++)
			{
				if (cell.InBounds(map) == false || symbiant.ContainsCell(cell))
					return false;
				if (IsValidSymbiantCellForDiagnostics(map, cell))
				{
					destination = cell;
					return true;
				}
				if (depth == 4)
					return false;
				if (BreakableConstructedWallForDiagnostics(map, cell) == null)
					return false;
				wallDepth++;
				cell += direction;
			}
			return false;
		}

		static bool IsEligibleIndoorRoomForDiagnostics(Room room)
		{
			return room != null
				&& room.IsDoorway == false
				&& room.Fogged == false
				&& room.IsHuge == false
				&& room.UsesOutdoorTemperature == false
				&& room.ProperRoom;
		}

		static bool IsValidSymbiantCellForDiagnostics(Map map, IntVec3 cell)
		{
			if (map == null || cell.InBounds(map) == false)
				return false;
			return CanOccupyOpenCellForDiagnostics(map, cell) || IsDoorCellForDiagnostics(map, cell);
		}

		static bool CanOccupyOpenCellForDiagnostics(Map map, IntVec3 cell)
		{
			if (cell.InBounds(map) == false || cell.Fogged(map))
				return false;
			if (cell.Walkable(map) == false)
				return false;
			return IsEligibleIndoorRoomForDiagnostics(cell.GetRoom(map));
		}

		static bool IsDoorCellForDiagnostics(Map map, IntVec3 cell)
		{
			var door = cell.GetEdifice(map) as Building_Door;
			if (door == null)
				return false;
			return GenAdj.CardinalDirections
				.Select(dir => cell + dir)
				.Where(adjacent => adjacent.InBounds(map))
				.Select(adjacent => adjacent.GetRoom(map))
				.Any(IsEligibleIndoorRoomForDiagnostics);
		}

		static Building BreakableConstructedWallForDiagnostics(Map map, IntVec3 cell)
		{
			var edifice = cell.GetEdifice(map);
			if (edifice == null || edifice is Building_Door)
				return null;
			if (edifice.def == null || edifice.def.building == null || edifice.def.useHitPoints == false)
				return null;
			if (edifice.def.building.isNaturalRock || edifice.def.mineable)
				return null;
			if (edifice.Faction != Faction.OfPlayer)
				return null;
			return edifice;
		}

		static object RunSymbiantContaminationStepProbe(Map map, int x, int z)
		{
			if (Constants.CONTAMINATION == false)
				return new { success = false, skipped = true, error = "Contamination is disabled." };

			var activeBefore = ZombieSymbiant.ActiveSymbiant(map);
			var activeBeforeId = ZombieRuntimeActions.StableThingId(activeBefore);
			var createdSymbiant = false;
			ZombieSymbiant symbiant = activeBefore;
			Pawn actor = null;
			var startCell = IntVec3.Invalid;
			var destinationCell = IntVec3.Invalid;
			var startGroundBefore = 0f;
			var destinationGroundBefore = 0f;

			try
			{
				if (symbiant == null)
				{
					var root = x >= 0 && z >= 0 ? new IntVec3(x, 0, z) : new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
					if (TryFindTemporarySymbiantStepCells(map, root, out var symbiantRoot, out startCell, out destinationCell, out var tempCellError) == false)
						return tempCellError;

					symbiant = ZombieSymbiant.DebugSpawnForRendering(map, symbiantRoot, new[] { symbiantRoot, startCell });
					createdSymbiant = symbiant != null;
					if (symbiant == null)
						return new { success = false, error = "Could not create temporary Symbiant for contamination step probe." };
				}
				else if (TryFindExistingSymbiantStepCells(map, symbiant, out startCell, out destinationCell, out var existingCellError) == false)
					return existingCellError;

				startGroundBefore = map.GetContamination(startCell, true);
				destinationGroundBefore = map.GetContamination(destinationCell, true);
				map.SetContamination(startCell, 0f, true);
				map.SetContamination(destinationCell, 0f, true);
				map.ContaminationGridUpdate();

				actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(actor, startCell, map, Rot4.Random, WipeMode.Vanish);
				DisablePawnWork(actor);
				actor.needs?.AddOrRemoveNeedsAsAppropriate();
				actor.jobs?.EndCurrentJob(JobCondition.InterruptForced);
				actor.pather.StopDead();
				actor.SetContamination(0.8f);

				var containsStart = ZombieSymbiant.IsSymbiantCell(map, startCell, out var checkedSymbiant) && checkedSymbiant == symbiant;
				var destinationContainsSymbiant = ZombieSymbiant.IsSymbiantCell(map, destinationCell, out _);
				var before = actor.GetContamination(false);
				var expected = before * (1f - ZombieSymbiant.SymbiantContaminationStepReduction);
				var canReach = actor.CanReach(destinationCell, PathEndMode.OnCell, Danger.Deadly);
				actor.pather.StartPath(destinationCell, PathEndMode.OnCell);
				var afterStartPath = actor.GetContamination(false);
				var samples = new List<object>();
				for (var tick = 0; tick <= 12; tick++)
				{
					if (tick > 0)
						AdvanceGameTicks(1);
					samples.Add(new
					{
						tick,
						position = ZombieRuntimeActions.DescribeCell(actor.Position),
						nextCell = actor.pather.nextCell.IsValid ? ZombieRuntimeActions.DescribeCell(actor.pather.nextCell) : null,
						actor.pather.Moving,
						actor.pather.MovingNow,
						contamination = actor.GetContamination(false)
					});
					if (actor.Position == destinationCell || actor.pather.Moving == false)
						break;
				}
				var afterTicks = actor.GetContamination(false);
				var reducedOnce = Mathf.Abs(afterTicks - expected) <= 0.002f || Mathf.Abs(afterStartPath - expected) <= 0.002f;

				return new
				{
					success = containsStart
						&& destinationContainsSymbiant == false
						&& canReach
						&& reducedOnce,
					activeSymbiantBefore = activeBeforeId,
					createdTemporarySymbiant = createdSymbiant,
					symbiant = ZombieRuntimeActions.StableThingId(symbiant),
					actor = DescribePawn(actor),
					startCell = ZombieRuntimeActions.DescribeCell(startCell),
					destinationCell = ZombieRuntimeActions.DescribeCell(destinationCell),
					containsStart,
					destinationContainsSymbiant,
					canReach,
					reduction = ZombieSymbiant.SymbiantContaminationStepReduction,
					before,
					expected,
					afterStartPath,
					afterTicks,
					samples
				};
			}
			catch (Exception ex)
			{
				return new { success = false, error = ex.ToString() };
			}
			finally
			{
				if (startCell.IsValid)
					map.SetContamination(startCell, startGroundBefore, true);
				if (destinationCell.IsValid)
					map.SetContamination(destinationCell, destinationGroundBefore, true);
				map.ContaminationGridUpdate();
				_ = CleanupTemporaryPawn(actor, true);
				if (createdSymbiant)
					_ = CleanupTemporarySymbiant(map, symbiant, true);
			}
		}

		static bool TryFindTemporarySymbiantStepCells(Map map, IntVec3 root, out IntVec3 symbiantRoot, out IntVec3 startCell, out IntVec3 destinationCell, out object error)
		{
			foreach (var candidate in GenRadial.RadialCellsAround(root, 24f, true))
			{
				if (IsClearStepProbeCell(map, candidate) == false)
					continue;
				foreach (var direction in GenAdj.CardinalDirections)
				{
					var start = candidate + direction;
					var destination = start + direction;
					if (IsClearStepProbeCell(map, start) && IsClearStepProbeCell(map, destination))
					{
						symbiantRoot = candidate;
						startCell = start;
						destinationCell = destination;
						error = null;
						return true;
					}
				}
			}

			symbiantRoot = IntVec3.Invalid;
			startCell = IntVec3.Invalid;
			destinationCell = IntVec3.Invalid;
			error = new { success = false, error = "Could not find three clear adjacent cells for the temporary Symbiant contamination step probe.", requestedRoot = ZombieRuntimeActions.DescribeCell(root) };
			return false;
		}

		static bool TryFindExistingSymbiantStepCells(Map map, ZombieSymbiant symbiant, out IntVec3 startCell, out IntVec3 destinationCell, out object error)
		{
			foreach (var cell in symbiant.AbsoluteCells)
			{
				if (IsClearStepProbeCell(map, cell) == false)
					continue;
				foreach (var direction in GenAdj.CardinalDirections)
				{
					var destination = cell + direction;
					if (IsClearStepProbeCell(map, destination) && ZombieSymbiant.IsSymbiantCell(map, destination, out _) == false)
					{
						startCell = cell;
						destinationCell = destination;
						error = null;
						return true;
					}
				}
			}

			startCell = IntVec3.Invalid;
			destinationCell = IntVec3.Invalid;
			error = new { success = false, error = "Could not find a clear Symbiant cell with a clear adjacent non-Symbiant destination.", symbiant = ZombieRuntimeActions.StableThingId(symbiant) };
			return false;
		}

		static bool IsClearStepProbeCell(Map map, IntVec3 cell)
		{
			return cell.InBounds(map)
				&& cell.Standable(map)
				&& cell.GetThingList(map).Any(thing => thing is Pawn || thing.def?.category == ThingCategory.Building) == false;
		}

		static object DescribeLiveSymbiantSeveranceOperation(Pawn host)
		{
			var recipe = CustomDefs.SeverSymbiantSymbiosis;
			var worker = recipe?.Worker as Recipe_SeverSymbiantSymbiosis;
			var map = host?.MapHeld;
			var missingIngredients = recipe == null || map == null
				? Array.Empty<ThingDef>()
				: recipe.PotentiallyMissingIngredients(null, map).ToArray();
			object DescribeIngredient(IngredientCount ingredient) => new
			{
				defs = ingredient.filter.AllowedThingDefs.Select(def => def.defName).ToArray(),
				count = worker == null ? 0f : worker.GetIngredientCount(ingredient, null)
			};
			var hiddenByIngredientPrefilter = recipe != null && missingIngredients.Any(def =>
				def != null && (def.isTechHediff || def.IsDrug || recipe.dontShowIfAnyIngredientMissing));
			var parts = host == null || recipe == null || worker == null
				? Array.Empty<BodyPartRecord>()
				: worker.GetPartsToApplyOn(host, recipe).ToArray();
			var torso = parts.FirstOrDefault(part => part.def == BodyPartDefOf.Torso);
			var hostDefHasRecipe = host?.def?.recipes?.Contains(recipe) == true;
			return new
			{
				success = host != null
					&& recipe != null
					&& worker != null
					&& torso != null
					&& hostDefHasRecipe
					&& recipe.AvailableOnNow(host, torso),
				recipe = recipe?.defName,
				workerClass = worker?.GetType().FullName,
				workAmount = recipe?.workAmount ?? 0f,
				host = ZombieRuntimeActions.StableThingId(host),
				hostDef = host?.def?.defName,
				hostDefHasRecipe,
				parts = parts.Select(part => part.def.defName).ToArray(),
				torsoAvailable = torso != null,
				availableOnTorso = host != null && torso != null && recipe?.AvailableOnNow(host, torso) == true,
				missingIngredients = missingIngredients.Select(def => def?.defName).ToArray(),
				hiddenByIngredientPrefilter,
				configuredIngredients = recipe?.ingredients.Select(DescribeIngredient).ToArray() ?? Array.Empty<object>(),
				labels = parts.Select(part => worker.GetLabelWhenUsedOn(host, part).ToString()).ToArray()
			};
		}

		static object CleanupTemporarySymbiant(Map map, ZombieSymbiant symbiant, bool cleanup)
		{
			if (symbiant == null)
				return new { requested = cleanup, cleaned = false, reason = "No temporary symbiant was spawned." };
			if (cleanup == false)
				return new { requested = false, cleaned = false, reason = "Cleanup disabled.", symbiant = ZombieRuntimeActions.StableThingId(symbiant) };
			if (symbiant.Destroyed)
				return new { requested = true, cleaned = false, reason = "Temporary symbiant was already destroyed.", symbiant = ZombieRuntimeActions.StableThingId(symbiant) };

			var id = ZombieRuntimeActions.StableThingId(symbiant);
			symbiant.DebugDestroyWithoutHostTrauma();
			_ = ZombieSymbiant.ActiveSymbiant(map);
			return new { requested = true, cleaned = symbiant.Destroyed, symbiant = id };
		}

		static object CleanupTemporaryLetters(Letter[] letters, bool cleanup)
		{
			if (cleanup == false || letters == null || letters.Length == 0 || Find.LetterStack == null)
				return new { removed = 0, skipped = cleanup == false };

			var removed = 0;
			foreach (var letter in letters)
			{
				if (letter == null)
					continue;
				Find.LetterStack.RemoveLetter(letter);
				removed++;
			}

			return new { removed, skipped = false };
		}

			static object DescribeSymbiantDiscoveryLetter(Letter letter)
			{
				if (letter == null)
					return null;

			var choice = letter as ChoiceLetter;
			return new
			{
				label = letter.Label.ToString(),
				text = choice?.Text.ToString(),
				defName = letter.def?.defName,
				arriveSound = letter.def?.arriveSound?.defName,
				color = letter.def == null ? null : DescribeColor(letter.def.color),
				letter.arrivalTick,
				lookTargetCount = letter.lookTargets?.targets?.Count ?? 0,
				lookTargets = letter.lookTargets?.targets?
					.Select(target => new
					{
						valid = target.IsValid,
						label = target.Label,
						hasThing = target.HasThing,
						thing = ZombieRuntimeActions.StableThingId(target.Thing),
						cell = target.IsMapTarget ? ZombieRuntimeActions.DescribeCell(target.Cell) : null,
						mapId = target.Map?.uniqueID
					})
						.ToArray()
				};
			}

			static bool IsGreenLetterColor(Color color)
			{
				return color.g > color.r
					&& color.g > color.b
					&& color.g >= 0.4f;
			}
		}
	}
