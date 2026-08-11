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
using Verse.AI.Group;

namespace ZombieLand
{
	public sealed partial class ZombielandBridgeTools
	{
		const string SymbiantHostDamageProbeHarmonyId = "net.pardeike.zombieland.bridge.symbiant-host-damage-probe";
		static readonly List<SymbiantHostDamagePacket> symbiantHostDamagePackets = new();
		static Pawn symbiantHostDamageProbePawn;

		sealed class SymbiantHostDamagePacket
		{
			public string damageDef { get; set; }
			public float amount { get; set; }
			public bool harmsHealth { get; set; }
			public string workerClass { get; set; }
			public bool sharesHealth { get; set; }
		}

		sealed class SymbiantHostDeathLifecycleEvidence
		{
			public bool success { get; set; }
			public int deathLetterCount { get; set; }
			public int funeralObligationCount { get; set; }
			public int funeralLetterCount { get; set; }
			public string funeralRitual { get; set; }
			public string[] letterLabels { get; set; }
			public string[] funeralLetterLabels { get; set; }
		}

		static void RecordSymbiantHostDamagePacket(Pawn __instance, ref DamageInfo dinfo)
		{
			if (__instance != symbiantHostDamageProbePawn)
				return;
			symbiantHostDamagePackets.Add(new SymbiantHostDamagePacket
			{
				damageDef = dinfo.Def?.defName,
				amount = dinfo.Amount,
				harmsHealth = dinfo.Def?.harmsHealth == true,
				workerClass = dinfo.Def?.workerClass?.FullName,
				sharesHealth = ZombieSymbiant.IsSharedHealthDamage(dinfo)
			});
		}

		static SymbiantHostDamagePacket[] SymbiantHostDamagePacketSnapshot()
		{
			return symbiantHostDamagePackets
				.Select(packet => new SymbiantHostDamagePacket
				{
					damageDef = packet.damageDef,
					amount = packet.amount,
					harmsHealth = packet.harmsHealth,
					workerClass = packet.workerClass,
					sharesHealth = packet.sharesHealth
				})
				.ToArray();
		}

		static float ExpectedSymbiantHostHealthDrain(IEnumerable<SymbiantHostDamagePacket> packets)
		{
			return packets
				.Where(packet => packet.sharesHealth && packet.amount > 0f)
				.Sum(packet => packet.amount);
		}

		static SymbiantHostDeathLifecycleEvidence KillLinkedHostAndCaptureDeathLifecycle(Pawn host)
		{
			var lettersBefore = (Find.LetterStack?.LettersListForReading ?? new List<Letter>()).ToHashSet();
			var funeralRitual = host?.Ideo?.PreceptsListForReading?
				.OfType<Precept_Ritual>()
				.FirstOrDefault(precept => precept.def == PreceptDefOf.Funeral);
			var obligationsBefore = (funeralRitual?.activeObligations ?? new List<RitualObligation>()).ToHashSet();

			host?.Kill(null);

			var newLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
				.Where(letter => lettersBefore.Contains(letter) == false)
				.ToArray();
			var newFuneralObligations = (funeralRitual?.activeObligations ?? new List<RitualObligation>())
				.Where(obligation => obligationsBefore.Contains(obligation) == false)
				.ToArray();
			var funeralLabels = newFuneralObligations
				.Select(obligation => obligation.LetterLabel.ToString())
				.ToHashSet();
			var funeralLetters = newLetters
				.Where(letter => funeralLabels.Contains(letter.Label.ToString()))
				.ToArray();
			foreach (var obligation in newFuneralObligations)
				funeralRitual.RemoveObligation(obligation, false);

			var deathLetterCount = newLetters.Count(letter => letter.def == LetterDefOf.Death);
			var expectedFuneralCount = funeralRitual == null ? 0 : 1;
			return new SymbiantHostDeathLifecycleEvidence
			{
				success = deathLetterCount == 1
						&& newFuneralObligations.Length == expectedFuneralCount
						&& funeralLetters.Length == expectedFuneralCount,
				deathLetterCount = deathLetterCount,
				funeralObligationCount = newFuneralObligations.Length,
				funeralLetterCount = funeralLetters.Length,
				funeralRitual = funeralRitual?.Label.ToString(),
				letterLabels = newLetters.Select(letter => letter.Label.ToString()).ToArray(),
				funeralLetterLabels = funeralLetters.Select(letter => letter.Label.ToString()).ToArray()
			};
		}

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
			if (fixture.host != null)
			{
				var removedHost = false;
				var corpse = fixture.host.Corpse
					?? Find.Maps
						.SelectMany(candidateMap => candidateMap.listerThings?.ThingsInGroup(ThingRequestGroup.Corpse)?.OfType<Corpse>() ?? Enumerable.Empty<Corpse>())
						.FirstOrDefault(candidate => candidate.InnerPawn == fixture.host);
				if (corpse != null && corpse.Destroyed == false)
				{
					corpse.Destroy(DestroyMode.Vanish);
					removedHost = true;
				}
				if (fixture.host.Destroyed == false)
				{
					fixture.host.Destroy(DestroyMode.Vanish);
					removedHost = true;
				}
				if (Find.WorldPawns?.Contains(fixture.host) == true)
					Find.WorldPawns.RemovePawn(fixture.host);
				if (fixture.host.Discarded == false && fixture.host.Destroyed)
					fixture.host.Discard(true);
				if (Find.WorldPawns?.Contains(fixture.host) == true)
					Find.WorldPawns.RemovePawn(fixture.host);
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

		[Tool("zombieland/symbiant_feed_menu_state", Description = "Inspect the live Symbiant feed-menu eligibility gates for a selected or named colonist.")]
		public static object SymbiantFeedMenuState(
			[ToolParameter(Description = "Optional colonist name; when empty, use the single selected colonist.", Required = false, DefaultValue = "")] string pawnName = "")
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded." };
			var symbiant = ZombieSymbiant.ActiveSymbiant(map);
			if (symbiant == null)
				return new { success = false, error = "No active Symbiant exists on the current map." };

			var pawn = pawnName.NullOrEmpty()
				? Find.Selector.SelectedPawns.SingleOrDefault()
				: map.mapPawns.FreeColonistsSpawned.FirstOrDefault(candidate => candidate.Name?.ToStringShort == pawnName || candidate.LabelShort == pawnName);
			if (pawn == null)
				return new { success = false, error = pawnName.NullOrEmpty() ? "Select exactly one colonist." : $"Could not find colonist '{pawnName}'." };

			var normalDanger = pawn.NormalMaxDanger();
			var normalTraverse = TraverseParms.For(pawn, normalDanger);
			var deadlyTraverse = TraverseParms.For(pawn, Danger.Deadly);
			var options = FloatMenuMakerMap.GetOptions(
				new List<Pawn> { pawn },
				symbiant.SelectionCoreCell.ToVector3Shifted(),
				out _);
			var corpses = map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse)
				.OfType<Corpse>()
				.Select(corpse => new
				{
					corpse = ZombieRuntimeActions.StableThingId(corpse),
					label = corpse.LabelShortCap.ToString(),
					position = ZombieRuntimeActions.DescribeCell(corpse.Position),
					forbiddenForPawn = corpse.IsForbidden(pawn),
					itemForbidden = corpse.IsForbidden(Faction.OfPlayer),
					insideAllowedArea = corpse.PositionHeld.InAllowedArea(pawn),
					validFeed = ZombieSymbiant.IsValidFeed(corpse),
					canAccept = symbiant.CanAcceptFeed(corpse),
					canReserve = pawn.CanReserve(corpse),
					canReachNormal = pawn.CanReach(corpse, PathEndMode.Touch, normalDanger),
					canReachDeadly = pawn.CanReach(corpse, PathEndMode.Touch, Danger.Deadly)
				})
				.ToArray();

			return new
			{
				success = true,
				pawn = new
				{
					id = ZombieRuntimeActions.StableThingId(pawn),
					label = pawn.LabelShortCap.ToString(),
					pawn.Drafted,
					job = pawn.CurJob?.def?.defName,
					jobPlayerForced = pawn.CurJob?.playerForced,
					normalDanger = normalDanger.ToString(),
					normalAvoidFog = normalTraverse.avoidFog,
					deadlyAvoidFog = deadlyTraverse.avoidFog
				},
				symbiant = new
				{
					id = ZombieRuntimeActions.StableThingId(symbiant),
					position = ZombieRuntimeActions.DescribeCell(symbiant.Position),
					core = ZombieRuntimeActions.DescribeCell(symbiant.SelectionCoreCell),
					canReserve = pawn.CanReserve(symbiant),
					canReachNormal = pawn.CanReach(symbiant, PathEndMode.Touch, normalDanger),
					canReachDeadly = pawn.CanReach(symbiant, PathEndMode.Touch, Danger.Deadly),
					canReachCoreNormal = pawn.CanReach(symbiant.SelectionCoreCell, PathEndMode.OnCell, normalDanger),
					canReachCoreDeadly = pawn.CanReach(symbiant.SelectionCoreCell, PathEndMode.OnCell, Danger.Deadly)
				},
				options = options.Select(option => new { option.Label, option.Disabled }).ToArray(),
				corpses
			};
		}

		[Tool("zombieland/symbiant_feeding_contract", Description = "Verify organic corpse grouping, non-root visible-core job routing, feeding pulse sizes, and growth behavior.")]
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
			object floatMenuRoute = null;
			object humanCorpseFeed = null;
			object animalCorpseFeed = null;
			object placementPerformance = null;
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
				var rootCell = symbiant.Position;
				var forcedCoreCell = GenAdj.CardinalDirections
					.Select(direction => rootCell + direction)
					.Where(cell => fixture.room.interiorRect.Contains(cell)
						&& cell.InBounds(map)
						&& cell.Standable(map)
						&& cell.GetEdifice(map) == null
						&& cell.GetThingList(map).Any(thing => thing is Pawn) == false)
					.OrderBy(cell => cell.x)
					.ThenBy(cell => cell.z)
					.DefaultIfEmpty(IntVec3.Invalid)
					.First();
				var forcedCoreCellCount = forcedCoreCell.IsValid
					? symbiant.DebugReinitializeSelectionCoreForScaleProbe(new[] { forcedCoreCell })
					: 0;
				var interactionCell = symbiant.SelectionCoreCell;
				if (forcedCoreCellCount != 1 || interactionCell != forcedCoreCell || interactionCell == rootCell)
					return new
					{
						success = false,
						error = "Could not move the Symbiant feeding fixture's visible core away from its canonical root.",
						root = ZombieRuntimeActions.DescribeCell(rootCell),
						forcedCore = ZombieRuntimeActions.DescribeCell(forcedCoreCell),
						actualCore = ZombieRuntimeActions.DescribeCell(interactionCell),
						forcedCoreCellCount
					};

				var feedCells = fixture.room.interiorRect.Cells
					.Where(cell => cell.InBounds(map)
						&& cell.Standable(map)
						&& cell.GetEdifice(map) == null
						&& symbiant.ContainsCell(cell) == false
						&& cell.GetThingList(map).Any(thing => thing is Pawn) == false)
					.OrderBy(cell => cell.DistanceToSquared(interactionCell))
					.ToArray();
				if (feedCells.Length < 2)
					return new { success = false, error = "The Symbiant feeding fixture has fewer than two clear interior feed cells." };
				var humanCorpseCell = feedCells[0];
				var animalCorpseCell = feedCells[1];
				var existingCorpseRaces = map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse)
					.OfType<Corpse>()
					.Select(corpse => corpse.InnerPawn?.def)
					.Where(def => def != null)
					.ToHashSet();
				var animalKindDef = DefDatabase<PawnKindDef>.AllDefs
					.Where(def => def.RaceProps?.Animal == true
						&& def.RaceProps.IsFlesh
						&& existingCorpseRaces.Contains(def.race) == false)
					.OrderByDescending(def => def.defName == "Rat")
					.ThenBy(def => def.defName)
					.FirstOrDefault();
				if (animalKindDef == null)
					return new { success = false, error = "Could not find an animal race without an existing corpse for the Symbiant feed fixture." };
				var eligibleHumanCorpses = new List<Corpse>();
				for (var i = 0; i < 2; i++)
				{
					if (TryCreateSymbiantFeedCorpse(map, humanCorpseCell, true, $"ZL_SymbiantFeed_Eligible_{i + 1:00}", spawnedThings, out var corpse, out var corpseError) == false)
						return corpseError;
					eligibleHumanCorpses.Add(corpse);
				}
				var freshAnimalCorpses = new List<Corpse>();
				for (var i = 0; i < 3; i++)
				{
					if (TryCreateSymbiantFeedCorpse(map, animalCorpseCell, false, $"ZL_SymbiantFeed_Animal_{i + 1:00}", spawnedThings, out var corpse, out var corpseError, animalKindDef) == false)
						return corpseError;
					freshAnimalCorpses.Add(corpse);
				}
				if (TryCreateSymbiantFeedCorpse(map, animalCorpseCell, false, "ZL_SymbiantFeed_Animal_Rotten", spawnedThings, out var rottenAnimalCorpse, out var rottenAnimalError, animalKindDef) == false)
					return rottenAnimalError;
				if (ZombieRuntimeActions.TriggerCorpseRotStageChanged(rottenAnimalCorpse, out _, out var rottenAnimalStage, out var rotError) == false
					|| rottenAnimalStage == RotStage.Fresh)
					return new { success = false, error = rotError ?? "The animal corpse remained fresh after the rot-stage transition." };

				if (TryCreateSymbiantFeedCorpse(map, humanCorpseCell, true, "ZL_SymbiantFeed_Forbidden", spawnedThings, out var forbiddenCorpse, out var forbiddenCorpseError) == false)
					return forbiddenCorpseError;
				forbiddenCorpse.SetForbidden(true, false);

				var inaccessibleCell = fixture.room.doorCell + IntVec3.South;
				if (TryCreateSymbiantFeedCorpse(map, inaccessibleCell, true, "ZL_SymbiantFeed_Inaccessible", spawnedThings, out var inaccessibleCorpse, out var inaccessibleCorpseError) == false)
					return inaccessibleCorpseError;
				fixture.room.door.Destroy(DestroyMode.Vanish);
				var sealWall = ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.WoodLog) as Building;
				if (sealWall == null)
					return new { success = false, error = "Could not create a sealing wall for the inaccessible Symbiant feed fixture." };
				GenSpawn.Spawn(sealWall, fixture.room.doorCell, map, WipeMode.Vanish);
				sealWall.SetFaction(Faction.OfPlayer);
				spawnedThings.Add(sealWall);
				map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();

				if (TryCreateSymbiantMechCorpse(map, humanCorpseCell, "ZL_SymbiantFeed_Mechanoid", spawnedThings, out var mechCorpse, out var mechCorpseError) == false)
					return mechCorpseError;

				var growthStateCapacityBefore = symbiant.DebugCapacityEvaluationCount;
				var growthStateRoomScansBefore = symbiant.DebugRoomCellScanCount;
				const int growthStateReadCount = 256;
				for (var i = 0; i < growthStateReadCount; i++)
					_ = symbiant.GrowthState;
				var growthStateCapacityAfter = symbiant.DebugCapacityEvaluationCount;
				var growthStateRoomScansAfter = symbiant.DebugRoomCellScanCount;

				var feedCapacityBefore = symbiant.DebugCapacityEvaluationCount;
				var feedExactAuditsBefore = symbiant.DebugExactCapacityAuditCount;
				var feedRoomScansBefore = symbiant.DebugRoomCellScanCount;
				const int repeatedFeedAcceptanceReads = 64;
				var feedAcceptanceResults = Enumerable.Range(0, repeatedFeedAcceptanceReads)
					.Select(_ => symbiant.CanAcceptFeed(freshAnimalCorpses[0]))
					.ToArray();
				var feedCapacityAfter = symbiant.DebugCapacityEvaluationCount;
				var feedExactAuditsAfter = symbiant.DebugExactCapacityAuditCount;
				var feedRoomScansAfter = symbiant.DebugRoomCellScanCount;
				placementPerformance = new
				{
					success = growthStateCapacityAfter == growthStateCapacityBefore
						&& growthStateRoomScansAfter == growthStateRoomScansBefore
						&& feedAcceptanceResults.All(result => result)
						&& feedCapacityAfter - feedCapacityBefore == 1
						&& feedExactAuditsAfter == feedExactAuditsBefore
						&& feedRoomScansAfter > feedRoomScansBefore,
					growthStateReadCount,
					growthStateCapacityDelta = growthStateCapacityAfter - growthStateCapacityBefore,
					growthStateRoomScanDelta = growthStateRoomScansAfter - growthStateRoomScansBefore,
					repeatedFeedAcceptanceReads,
					feedCapacityEvaluationDelta = feedCapacityAfter - feedCapacityBefore,
					feedExactAuditDelta = feedExactAuditsAfter - feedExactAuditsBefore,
					feedRoomCellScanDelta = feedRoomScansAfter - feedRoomScansBefore
				};
				fixture.host.jobs?.EndCurrentJob(JobCondition.InterruptForced);
				string FeedLabel(Corpse corpse)
				{
					var innerPawn = corpse.InnerPawn;
					var freshness = corpse.GetRotStage() == RotStage.Fresh ? "fresh" : "rotten";
					var name = innerPawn.RaceProps.Animal
						? innerPawn.def.LabelCap.ToString()
						: innerPawn.LabelShortCap.ToString();
					return "FeedZombieSymbiantFloatMenu".Translate(freshness, name, ZombieSymbiant.FeedGrowthCellCount(corpse));
				}

				var humanFeedLabels = eligibleHumanCorpses.Select(FeedLabel).ToArray();
				var freshAnimalFeedLabel = FeedLabel(freshAnimalCorpses[0]);
				var rottenAnimalFeedLabel = FeedLabel(rottenAnimalCorpse);
				var forbiddenFeedLabel = FeedLabel(forbiddenCorpse);
				var inaccessibleFeedLabel = FeedLabel(inaccessibleCorpse);
				var allOptions = FloatMenuMakerMap.GetOptions(
					new List<Pawn> { fixture.host },
					interactionCell.ToVector3Shifted(),
					out var floatMenuContext);
				var humanFeedOptions = allOptions.Where(option => humanFeedLabels.Contains(option.Label)).ToArray();
				var freshAnimalFeedOptions = allOptions.Where(option => option.Label == freshAnimalFeedLabel).ToArray();
				var rottenAnimalFeedOptions = allOptions.Where(option => option.Label == rottenAnimalFeedLabel).ToArray();
				var expectedAnimalRepresentative = freshAnimalCorpses
					.OrderBy(corpse => corpse.Position.DistanceToSquared(fixture.host.Position) + corpse.Position.DistanceToSquared(interactionCell))
					.ThenBy(corpse => corpse.thingIDNumber)
					.First();
				var offeredCorpses = eligibleHumanCorpses.Concat(new[] { expectedAnimalRepresentative, rottenAnimalCorpse }).ToArray();
				var allOfferedCorpsesAreEligible = offeredCorpses.All(corpse =>
					ZombieSymbiant.IsValidFeed(corpse)
					&& corpse.IsForbidden(fixture.host) == false
					&& fixture.host.CanReserve(corpse)
					&& fixture.host.CanReach(corpse, PathEndMode.Touch, fixture.host.NormalMaxDanger()));
				var forbiddenOptionCount = allOptions.Count(option => option.Label == forbiddenFeedLabel);
				var inaccessibleOptionCount = allOptions.Count(option => option.Label == inaccessibleFeedLabel);
				var mechOptionCount = allOptions.Count(option => option.Label.Contains(mechCorpse.InnerPawn.LabelShortCap));
				var rootOptions = FloatMenuMakerMap.GetOptions(
					new List<Pawn> { fixture.host },
					rootCell.ToVector3Shifted(),
					out _);
				var rootFeedOptionCount = rootOptions.Count(option => humanFeedLabels.Contains(option.Label)
					|| option.Label == freshAnimalFeedLabel
					|| option.Label == rottenAnimalFeedLabel);

				humanFeedOptions.FirstOrDefault()?.action?.Invoke();
				var humanFeedJob = fixture.host.CurJob;
				var selectedHumanCorpse = humanFeedJob?.targetB.Thing as Corpse;
				var humanJobInteractionCell = humanFeedJob == null ? IntVec3.Invalid : humanFeedJob.targetC.Cell;
				fixture.host.jobs?.EndCurrentJob(JobCondition.InterruptForced);
				humanCorpseFeed = FeedSymbiantThing(symbiant, selectedHumanCorpse, "fresh humanlike corpse", 6);

				freshAnimalFeedOptions.FirstOrDefault()?.action?.Invoke();
				var animalFeedJob = fixture.host.CurJob;
				var selectedAnimalCorpse = animalFeedJob?.targetB.Thing as Corpse;
				var animalJobInteractionCell = animalFeedJob == null ? IntVec3.Invalid : animalFeedJob.targetC.Cell;
				floatMenuRoute = new
				{
					success = interactionCell != rootCell
						&& fixture.host.CanReach(interactionCell, PathEndMode.OnCell, Danger.Deadly)
						&& rootFeedOptionCount == 0
						&& humanFeedOptions.Length == eligibleHumanCorpses.Count
						&& freshAnimalFeedOptions.Length == 1
						&& rottenAnimalFeedOptions.Length == 1
						&& allOfferedCorpsesAreEligible
						&& forbiddenCorpse.IsForbidden(fixture.host)
						&& forbiddenOptionCount == 0
						&& fixture.host.CanReach(inaccessibleCorpse, PathEndMode.Touch, fixture.host.NormalMaxDanger()) == false
						&& inaccessibleOptionCount == 0
						&& ZombieSymbiant.IsValidFeed(mechCorpse) == false
						&& mechOptionCount == 0
						&& humanFeedJob?.def == CustomDefs.FeedZombieSymbiant
						&& humanFeedJob.targetA.Thing == symbiant
						&& humanJobInteractionCell == interactionCell
						&& eligibleHumanCorpses.Contains(selectedHumanCorpse)
						&& animalFeedJob?.def == CustomDefs.FeedZombieSymbiant
						&& animalFeedJob.targetA.Thing == symbiant
						&& animalJobInteractionCell == interactionCell
						&& selectedAnimalCorpse == expectedAnimalRepresentative,
					root = ZombieRuntimeActions.DescribeCell(rootCell),
					interactionCore = ZombieRuntimeActions.DescribeCell(interactionCell),
					coreDiffersFromRoot = interactionCell != rootCell,
					coreReachable = fixture.host.CanReach(interactionCell, PathEndMode.OnCell, Danger.Deadly),
					rootFeedOptionCount,
					allOptionCount = allOptions.Count,
					humanCandidateCount = eligibleHumanCorpses.Count,
					humanOptionCount = humanFeedOptions.Length,
					freshAnimalCandidateCount = freshAnimalCorpses.Count,
					freshAnimalOptionCount = freshAnimalFeedOptions.Length,
					rottenAnimalOptionCount = rottenAnimalFeedOptions.Length,
					offeredCorpses = offeredCorpses.Select(ZombieRuntimeActions.StableThingId).ToArray(),
					allOfferedCorpsesAreEligible,
					forbidden = new
					{
						corpse = ZombieRuntimeActions.StableThingId(forbiddenCorpse),
						isForbidden = forbiddenCorpse.IsForbidden(fixture.host),
						optionCount = forbiddenOptionCount
					},
					inaccessible = new
					{
						corpse = ZombieRuntimeActions.StableThingId(inaccessibleCorpse),
						reachable = fixture.host.CanReach(inaccessibleCorpse, PathEndMode.Touch, fixture.host.NormalMaxDanger()),
						optionCount = inaccessibleOptionCount
					},
					mechanoid = new
					{
						corpse = ZombieRuntimeActions.StableThingId(mechCorpse),
						validFeed = ZombieSymbiant.IsValidFeed(mechCorpse),
						optionCount = mechOptionCount
					},
					contextMapId = floatMenuContext.map?.uniqueID ?? -1,
					humanJobTarget = ZombieRuntimeActions.StableThingId(selectedHumanCorpse),
					humanJobInteractionCell = ZombieRuntimeActions.DescribeCell(humanJobInteractionCell),
					animalJobTarget = ZombieRuntimeActions.StableThingId(selectedAnimalCorpse),
					animalJobInteractionCell = ZombieRuntimeActions.DescribeCell(animalJobInteractionCell),
					expectedAnimalRepresentative = ZombieRuntimeActions.StableThingId(expectedAnimalRepresentative)
				};
				fixture.host.jobs?.EndCurrentJob(JobCondition.InterruptForced);
				animalCorpseFeed = FeedSymbiantThing(symbiant, selectedAnimalCorpse, "fresh animal corpse", 4);
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
				&& ScenarioSucceeded(floatMenuRoute)
				&& ScenarioSucceeded(humanCorpseFeed)
				&& ScenarioSucceeded(animalCorpseFeed)
				&& ScenarioSucceeded(placementPerformance)
				&& (activeAfterCleanup == null || cleanup == false);

			return new
			{
				success,
				sourcePath = "FloatMenuMakerMap.GetOptions -> AddSymbiantFeedOptions -> JobDriver_FeedZombieSymbiant -> ZombieSymbiant.TryFeed",
				growthSpeedFactor = feedingGrowthSpeedFactor,
				error,
				fixtureSetup,
				floatMenuRoute,
				humanCorpseFeed,
				animalCorpseFeed,
				placementPerformance,
				cleanup = new
				{
					symbiant = cleanupSymbiant,
					fixture = fixtureCleanup,
					letters = letterCleanup,
					activeSymbiantAfterCleanup = ZombieRuntimeActions.StableThingId(activeAfterCleanup)
				}
			};
		}

		static bool TryCreateSymbiantFeedCorpse(Map map, IntVec3 cell, bool humanlike, string pawnName, List<Thing> spawnedThings, out Corpse corpse, out object error, PawnKindDef animalKindDef = null)
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
					var kindDef = animalKindDef
						?? DefDatabase<PawnKindDef>.GetNamed("Warg", false)
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
				corpse.SetForbidden(false, false);
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

		static bool TryCreateSymbiantMechCorpse(Map map, IntVec3 cell, string pawnName, List<Thing> spawnedThings, out Corpse corpse, out object error)
		{
			corpse = null;
			error = null;
			Pawn pawn = null;
			try
			{
				var kindDef = PawnKindDefOf.Mech_Scyther
					?? DefDatabase<PawnKindDef>.AllDefs.FirstOrDefault(def => def.race?.race?.IsMechanoid == true);
				if (kindDef == null)
				{
					error = new { success = false, error = "Could not find a mechanoid pawn kind for the Symbiant feed fixture." };
					return false;
				}

				var request = new PawnGenerationRequest(
					kindDef,
					Faction.OfMechanoids,
					PawnGenerationContext.NonPlayer,
					forceGenerateNewPawn: true,
					canGeneratePawnRelations: false,
					colonistRelationChanceFactor: 0f,
					forceNoIdeo: true);
				pawn = PawnGenerator.GeneratePawn(request);
				pawn.Name = new NameSingle(pawnName);
				GenSpawn.Spawn(pawn, cell, map, Rot4.South);
				if (ZombieRuntimeActions.KillPawnToCorpse(pawn, out corpse, out var corpseError) == false)
				{
					error = new { success = false, error = corpseError, pawn = DescribePawn(pawn) };
					return false;
				}
				corpse.SetForbidden(false, false);
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
						&& spawned.RegisteredInMapPawnLists
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

		[Tool("zombieland/symbiant_host_availability_contract", Description = "Verify a linked host remains the same pawn across unheld off-map, same-map containment, and genuine second-map states; same effective-map holders keep the bond active while cross-map absence makes it dormant.")]
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
			object seededBenefits = null;
			object beforeLeave = null;
			object offMap = null;
			object afterReturn = null;
			object secondMapSetup = null;
			object onSecondMap = null;
			object afterSecondMapReturn = null;
			object contained = null;
			object afterEject = null;
			object casketEvidence = null;
			SymbiantSecondMapFixture secondMapFixture = null;
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
				var seededBenefitSet = EnsureSymbiantHostBenefitsForProbe(symbiant,
					"MoodFixed",
					"NoFoodOrRest",
					"SkillBonus",
					"MoveSpeed",
					"Manipulation",
					"ZombieIgnore",
					"AutoHeal");
				var existingAutoHealableInjuries = host.health?.hediffSet?.hediffs?.Count(ZombieSymbiant.IsAutoHealableHediffForDebug) ?? 0;
				var autoHealCapacity = EnsureSymbiantHostBenefitCountForProbe(symbiant, "AutoHeal", existingAutoHealableInjuries + 1);
				seededBenefits = new
				{
					success = ScenarioSucceeded(seededBenefitSet) && ScenarioSucceeded(autoHealCapacity),
					seededBenefitSet,
					existingAutoHealableInjuries,
					autoHealCapacity
				};
				beforeLeave = DescribeHostAvailabilityState("beforeLeave", map, symbiant, host, true);

				var leavePosition = host.Position;
				host.DeSpawn(DestroyMode.Vanish);
				AccessTools.Method(typeof(ZombieSymbiant), "EnsureHostLink")?.Invoke(symbiant, null);
				offMap = DescribeHostAvailabilityState("offMap", map, symbiant, host, true);

				if (TryFindClearSpawnCell(map, leavePosition, 12f, out var returnCell, out var returnError) == false)
					return returnError;
				GenSpawn.Spawn(host, returnCell, map, Rot4.Random, WipeMode.Vanish);
				AccessTools.Method(typeof(ZombieSymbiant), "EnsureHostLink")?.Invoke(symbiant, null);
				afterReturn = DescribeHostAvailabilityState("afterReturn", map, symbiant, host, true);

				if (TryCreateSymbiantSecondMapFixture(map, out secondMapFixture, out var secondMapError) == false)
				{
					secondMapSetup = secondMapError;
				}
				else
				{
					secondMapSetup = DescribeSymbiantSecondMapFixture(secondMapFixture);
					var originCell = host.Position;
					var moveToSecondMap = MovePawnToSymbiantContractMap(host, secondMapFixture.map, secondMapFixture.map.Center, symbiant);
					onSecondMap = ScenarioSucceeded(moveToSecondMap)
						? DescribeHostAvailabilityState("spawnedOnSecondMap", map, symbiant, host, true)
						: moveToSecondMap;
					var moveBack = MovePawnToSymbiantContractMap(host, map, originCell, symbiant);
					afterSecondMapReturn = ScenarioSucceeded(moveBack)
						? DescribeHostAvailabilityState("afterSecondMapReturn", map, symbiant, host, true)
						: moveBack;
				}

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
						contained = DescribeHostAvailabilityState("containedInCryptosleep", map, symbiant, host, true);
						casket.EjectContents();
						AccessTools.Method(typeof(ZombieSymbiant), "EnsureHostLink")?.Invoke(symbiant, null);
						afterEject = DescribeHostAvailabilityState("afterCryptosleepEject", map, symbiant, host, true);
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
			var dormancyHost = symbiant?.LinkedHost;
			var expectedDormancyLabel = "LetterLabelSymbiantBondDormant".Translate().ToString();
			var expectedDormancyText = dormancyHost == null
				? null
				: "SymbiantHostRelocatedMessage".Translate(dormancyHost.LabelShortCap).ToString();
			var dormancyLetters = newLetters
				.Where(letter => letter?.def == LetterDefOf.NeutralEvent
					&& letter.Label.ToString() == expectedDormancyLabel
					&& (letter as ChoiceLetter)?.Text.ToString() == expectedDormancyText)
				.ToArray();
			var dormancyLetterEvidence = new
			{
				success = dormancyHost != null
					&& dormancyLetters.Length == 2
					&& dormancyLetters.All(letter => (letter.lookTargets?.targets?.Count ?? 0) >= 2),
				expectedDormancyLabel,
				expectedDormancyText,
				matchingLetterCount = dormancyLetters.Length,
				letters = dormancyLetters.Select(DescribeSymbiantDiscoveryLetter).ToArray()
			};
			var cleanupResult = CleanupTemporarySymbiant(map, symbiant, cleanup);
			var fixtureCleanup = CleanupSymbiantNaturalSpawnFixture(map, fixture, cleanup);
			var secondMapCleanup = CleanupSymbiantSecondMapFixture(secondMapFixture, cleanup);
			var letterCleanup = CleanupTemporaryLetters(newLetters, cleanup);
			var activeAfterCleanup = ZombieSymbiant.ActiveSymbiant(map);
			var success = error == null
				&& ScenarioSucceeded(seededBenefits)
				&& ScenarioSucceeded(beforeLeave)
				&& ScenarioSucceeded(offMap)
				&& ScenarioSucceeded(afterReturn)
				&& ScenarioSucceeded(secondMapSetup)
				&& ScenarioSucceeded(onSecondMap)
				&& ScenarioSucceeded(afterSecondMapReturn)
				&& ScenarioSucceeded(contained)
				&& ScenarioSucceeded(afterEject)
				&& ScenarioSucceeded(casketEvidence)
				&& ScenarioSucceeded(dormancyLetterEvidence)
				&& ScenarioSucceeded(secondMapCleanup)
				&& (activeAfterCleanup == null || cleanup == false);

			return new
			{
				success,
				sourcePath = "ZombieSymbiant.ResolveHost/EnsureHostLink/SymbiantBenefitFactor/CanSeverSymbiosis",
				error,
				fixtureSetup,
				seededBenefits,
				beforeLeave,
				offMap,
				afterReturn,
				secondMapSetup,
				onSecondMap,
				afterSecondMapReturn,
				contained,
				afterEject,
				casketEvidence,
				dormancyLetterEvidence,
				cleanup = new
				{
					symbiant = cleanupResult,
					fixture = fixtureCleanup,
					secondMap = secondMapCleanup,
					letters = letterCleanup,
					activeSymbiantAfterCleanup = ZombieRuntimeActions.StableThingId(activeAfterCleanup)
				}
			};
		}

		const string CrossMapSaveHostName = "ZL Symbiant Cross Map Save Host";
		const string CrossMapSaveMarkerName = "ZL Symbiant Cross Map Save Marker";

		[Tool("zombieland/symbiant_cross_map_save_contract", Description = "Stage, inspect, return, or clean a persistent two-map Symbiant fixture for a real save/load lifecycle check.")]
		public static object SymbiantCrossMapSaveContract(
			[ToolParameter(Description = "Mode: stage, read, return, cleanup.", Required = false, DefaultValue = "read")] string mode = "read")
		{
			var currentMap = CurrentMap;
			if (currentMap == null)
				return new { success = false, error = "No current map is loaded." };

			mode = (mode ?? "read").Trim();
			var host = Find.Maps
				.SelectMany(map => map.mapPawns?.AllPawns ?? Enumerable.Empty<Pawn>())
				.FirstOrDefault(pawn => pawn?.Name?.ToStringFull == CrossMapSaveHostName);
			var mapMarker = Find.Maps
				.SelectMany(map => map.mapPawns?.AllPawns ?? Enumerable.Empty<Pawn>())
				.FirstOrDefault(pawn => pawn?.Name?.ToStringFull == CrossMapSaveMarkerName);
			var symbiant = Find.Maps
				.Select(ZombieSymbiant.ActiveSymbiant)
				.FirstOrDefault(candidate => candidate?.HostThingId == host?.ThingID);

			if (mode.Equals("stage", StringComparison.OrdinalIgnoreCase))
			{
				if (host != null || symbiant != null || mapMarker != null)
					return new { success = false, error = "A cross-map save fixture already exists. Clean it before staging another." };
				if (ZombieSymbiant.ActiveSymbiant(currentMap) != null)
					return new { success = false, error = "The current map already has an active Symbiant." };
				if (TryFindClearSpawnCell(currentMap, currentMap.Center, 24f, out var hostCell, out var hostCellError) == false)
					return hostCellError;
				if (TryFindClearSpawnCell(currentMap, hostCell + new IntVec3(4, 0, 0), 24f, out var symbiantCell, out var symbiantCellError) == false)
					return symbiantCellError;

				SymbiantSecondMapFixture secondMapFixture = null;
				try
				{
					host = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
					host.Name = new NameSingle(CrossMapSaveHostName);
					GenSpawn.Spawn(host, hostCell, currentMap, Rot4.South, WipeMode.Vanish);
					symbiant = ZombieSymbiant.DebugSpawnForRendering(currentMap, symbiantCell, [symbiantCell]);
					AccessTools.Method(typeof(ZombieSymbiant), "AssignHost")?.Invoke(symbiant, [host]);
					var activeBefore = DescribeHostAvailabilityState("beforeCrossMapSave", currentMap, symbiant, host);

					if (TryCreateSymbiantSecondMapFixture(currentMap, out secondMapFixture, out var secondMapError) == false)
					{
						symbiant.DebugDestroyWithoutHostTrauma();
						DestroyAndDiscardTemporaryPawn(host);
						if (secondMapFixture?.map != null || secondMapFixture?.parent != null)
							_ = CleanupSymbiantSecondMapFixture(secondMapFixture, true, true);
						return new { success = false, error = "Could not create the second-map save fixture.", secondMapError };
					}
					mapMarker = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
					mapMarker.Name = new NameSingle(CrossMapSaveMarkerName);
					var markerCell = secondMapFixture.map.AllCells.First(cell => cell.Standable(secondMapFixture.map) && cell.GetThingList(secondMapFixture.map).Any(thing => thing is Pawn) == false);
					GenSpawn.Spawn(mapMarker, markerCell, secondMapFixture.map, Rot4.South, WipeMode.Vanish);
					secondMapFixture.trackedPawns.Add(mapMarker);
					secondMapFixture.trackedPawns.Add(host);
					var move = MovePawnToSymbiantContractMap(host, secondMapFixture.map, secondMapFixture.map.Center, symbiant);
					var dormant = ScenarioSucceeded(move)
						? DescribeHostAvailabilityState("stagedAcrossMaps", currentMap, symbiant, host)
						: move;
					return new
					{
						success = ScenarioSucceeded(activeBefore) && ScenarioSucceeded(move) && ScenarioSucceeded(dormant),
						mode,
						originMapId = currentMap.uniqueID,
						secondMapId = secondMapFixture.map.uniqueID,
						host = ZombieRuntimeActions.StableThingId(host),
						mapMarker = ZombieRuntimeActions.StableThingId(mapMarker),
						symbiant = ZombieRuntimeActions.StableThingId(symbiant),
						activeBefore,
						move,
						dormant
					};
				}
				catch (Exception ex)
				{
					symbiant?.DebugDestroyWithoutHostTrauma();
					if (secondMapFixture != null)
						_ = CleanupSymbiantSecondMapFixture(secondMapFixture, true, true);
					else if (host != null)
						DestroyAndDiscardTemporaryPawn(host);
					return new { success = false, error = ex.Message, exceptionType = ex.GetType().FullName, stackTrace = ex.StackTrace };
				}
			}

			if (host == null || symbiant == null || mapMarker == null)
				return new { success = false, error = "The persistent cross-map save fixture was not found.", hostFound = host != null, symbiantFound = symbiant != null, mapMarkerFound = mapMarker != null };
			var originMap = symbiant.Map;
			var secondMap = mapMarker.MapHeld;
			if (originMap == null || secondMap == null)
				return new { success = false, error = "The persistent fixture is missing one of its maps." };

			if (mode.Equals("read", StringComparison.OrdinalIgnoreCase))
			{
				var dormant = DescribeHostAvailabilityState("crossMapAfterLoad", originMap, symbiant, host);
				return new
				{
					success = originMap != secondMap && ScenarioSucceeded(dormant),
					mode,
					originMapId = originMap.uniqueID,
					secondMapId = secondMap.uniqueID,
					dormant
				};
			}

			if (mode.Equals("return", StringComparison.OrdinalIgnoreCase))
			{
				var move = MovePawnToSymbiantContractMap(host, originMap, symbiant.Position + new IntVec3(3, 0, 0), symbiant);
				var active = ScenarioSucceeded(move)
					? DescribeHostAvailabilityState("returnedAfterCrossMapLoad", originMap, symbiant, host)
					: move;
				return new { success = ScenarioSucceeded(move) && ScenarioSucceeded(active), mode, move, active };
			}

			if (mode.Equals("cleanup", StringComparison.OrdinalIgnoreCase))
			{
				symbiant.DebugDestroyWithoutHostTrauma();
				var fixtureMap = mapMarker.MapHeld;
				var fixture = fixtureMap == originMap ? null : new SymbiantSecondMapFixture
				{
					originMap = originMap,
					previousCurrentMap = originMap,
					map = fixtureMap,
					parent = fixtureMap?.Parent,
					tile = fixtureMap?.Tile ?? PlanetTile.Invalid
				};
				if (fixture != null)
				{
					fixture.trackedPawns.Add(host);
					fixture.trackedPawns.Add(mapMarker);
				}
				var hostCleanup = fixture == null
					? DestroyAndDiscardTemporaryPawn(host)
					: CleanupSymbiantSecondMapFixture(fixture, true, true);
				return new
				{
					success = ScenarioSucceeded(hostCleanup)
						&& symbiant.Destroyed
						&& symbiant.Discarded
						&& Find.WorldPawns?.Contains(symbiant) != true,
					mode,
					symbiantDestroyed = symbiant.Destroyed,
					symbiantDiscarded = symbiant.Discarded,
					symbiantWorldPawn = Find.WorldPawns?.Contains(symbiant) == true,
					hostCleanup
				};
			}

			return new { success = false, error = $"Unknown mode '{mode}'. Expected stage, read, return, or cleanup." };
		}

		static object DestroyAndDiscardTemporaryPawn(Pawn pawn)
		{
			if (pawn == null)
				return new { success = true, skipped = true };
			var id = ZombieRuntimeActions.StableThingId(pawn);
			var corpse = pawn.Corpse;
			if (corpse != null && corpse.Destroyed == false)
				corpse.Destroy(DestroyMode.Vanish);
			if (Find.WorldPawns?.Contains(pawn) == true)
				Find.WorldPawns.RemovePawn(pawn);
			if (pawn.Destroyed == false)
				pawn.Destroy(DestroyMode.Vanish);
			if (Find.WorldPawns?.Contains(pawn) == true)
				Find.WorldPawns.RemovePawn(pawn);
			if (pawn.Discarded == false)
				pawn.Discard(true);
			return new
			{
				success = pawn.Destroyed && pawn.Discarded && Find.WorldPawns?.Contains(pawn) != true,
				id,
				destroyed = pawn.Destroyed,
				discarded = pawn.Discarded,
				worldPawn = Find.WorldPawns?.Contains(pawn) == true
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

		static object DescribeHostAvailabilityState(string label, Map map, ZombieSymbiant symbiant, Pawn host, bool requireAllOptionalBenefits = false)
		{
			var linkedHost = symbiant?.LinkedHost;
			var linkedForHost = ZombieSymbiant.LinkedSymbiantFor(host);
			var hediffs = host?.health?.hediffSet?.hediffs?
				.Where(candidate => candidate?.def == CustomDefs.SymbiantSymbiosis)
				.ToArray() ?? Array.Empty<Hediff>();
			var hediff = hediffs.FirstOrDefault() as Hediff_SymbiantSymbiosis;
			var hediffDescription = hediff?.Description;
			var dormantDescription = host == null ? null : "SymbiantHostRelocatedMessage".Translate(host.LabelShortCap).ToString();
			var hostSpawned = host?.Spawned == true;
			var hostMap = host?.MapHeld;
			var hostMapMatches = hostMap == map;
			var linkPreserved = symbiant != null
				&& symbiant.Destroyed == false
				&& ReferenceEquals(linkedHost, host)
				&& symbiant.HostThingId == host?.ThingID
				&& linkedForHost == symbiant
				&& hediffs.Length == 1
				&& hediff != null
				&& hediff.symbiantThingId == symbiant.ThingID;
			var benefitFactor = ZombieSymbiant.SymbiantBenefitFactor(host);
			var canSever = ZombieSymbiant.CanSeverSymbiosis(host);
			var targetingProtection = ZombieSymbiant.HasZombieTargetingProtection(host);
			var infectionImmunity = ZombieSymbiant.HasZombieInfectionImmunity(host);
			var moodFixed = ZombieSymbiant.HasMoodFixedBenefit(host);
			var noFoodOrRest = ZombieSymbiant.HasNoFoodOrRestBenefit(host);
			var moveSpeedBenefits = ZombieSymbiant.MoveSpeedBenefitCount(host);
			var manipulationBenefits = ZombieSymbiant.ManipulationBenefitCount(host);
			var skillBonusBenefits = ZombieSymbiant.SkillBonusBenefitCount(host);
			var hostAuraActive = ZombieSymbiant.TryGetHostAuraFactor(host, out var hostAuraFactor);
			var dormant = symbiant?.IsActiveBondWith(host) != true;
			var autoHealBenefits = SymbiantHostBenefitCountForProbe(symbiant, "AutoHeal");
			var autoHeal = requireAllOptionalBenefits ? ProbeSymbiantAutoHealAvailability(symbiant, host, dormant == false) : null;
			var baseSuccess = dormant
				? linkPreserved
					&& hostMapMatches == false
					&& Mathf.Approximately(benefitFactor, 0f)
					&& canSever == false
					&& targetingProtection == false
					&& infectionImmunity == false
					&& moodFixed == false
					&& noFoodOrRest == false
					&& moveSpeedBenefits == 0
					&& manipulationBenefits == 0
					&& skillBonusBenefits == 0
					&& hostAuraActive == false
					&& hediffDescription == dormantDescription
				: linkPreserved
					&& hostMapMatches
					&& canSever
					&& infectionImmunity
					&& hostAuraActive
					&& hediffDescription != dormantDescription;
			var optionalBenefitsSuccess = requireAllOptionalBenefits == false
				|| (autoHealBenefits > 0
					&& ScenarioSucceeded(autoHeal)
					&& (dormant
						? targetingProtection == false
							&& moodFixed == false
							&& noFoodOrRest == false
							&& moveSpeedBenefits == 0
							&& manipulationBenefits == 0
							&& skillBonusBenefits == 0
						: targetingProtection
							&& moodFixed
							&& noFoodOrRest
							&& moveSpeedBenefits > 0
							&& manipulationBenefits > 0
							&& skillBonusBenefits > 0));
			var success = baseSuccess && optionalBenefitsSuccess;

			return new
			{
				success,
				label,
				host = ZombieRuntimeActions.StableThingId(host),
				hostSpawned,
				hostMapMatches,
				hostMapId = hostMap?.uniqueID ?? -1,
				hostPosition = hostMap == null ? null : ZombieRuntimeActions.DescribeCell(host.PositionHeld),
				linkedHost = ZombieRuntimeActions.StableThingId(linkedHost),
				hostThingId = symbiant?.HostThingId,
				linkedForHost = ZombieRuntimeActions.StableThingId(linkedForHost),
				linkPreserved,
				hasHediff = hediff != null,
				hediffDescription,
				dormantDescription,
				hediffCount = hediffs.Length,
				hediffSymbiantThingId = hediff?.symbiantThingId,
				benefitFactor,
				canSever,
				targetingProtection,
				infectionImmunity,
				moodFixed,
				noFoodOrRest,
				moveSpeedBenefits,
				manipulationBenefits,
				skillBonusBenefits,
				hostAuraActive,
				hostAuraFactor,
				requireAllOptionalBenefits,
				autoHealBenefits,
				autoHeal,
				symbiant = ZombieRuntimeActions.StableThingId(symbiant),
				symbiantSpawned = symbiant?.Spawned ?? false,
				symbiantDestroyed = symbiant?.Destroyed ?? false
			};
		}

		static int SymbiantHostBenefitCountForProbe(ZombieSymbiant symbiant, string benefitName)
		{
			var enumType = typeof(ZombieSymbiant).GetNestedType("HostBenefit", System.Reflection.BindingFlags.NonPublic);
			var hostBenefitsField = AccessTools.Field(typeof(ZombieSymbiant), "hostBenefits");
			var list = hostBenefitsField?.GetValue(symbiant) as IList;
			if (symbiant == null || enumType == null || list == null || benefitName.NullOrEmpty())
				return 0;
			var value = Enum.Parse(enumType, benefitName);
			return list.Cast<object>().Count(item => Equals(item, value));
		}

		static object EnsureSymbiantHostBenefitCountForProbe(ZombieSymbiant symbiant, string benefitName, int minimumCount)
		{
			var enumType = typeof(ZombieSymbiant).GetNestedType("HostBenefit", System.Reflection.BindingFlags.NonPublic);
			var hostBenefitsField = AccessTools.Field(typeof(ZombieSymbiant), "hostBenefits");
			var list = hostBenefitsField?.GetValue(symbiant) as IList;
			if (symbiant == null || enumType == null || list == null || benefitName.NullOrEmpty())
				return new { success = false, error = "Could not access the Symbiant host benefit list.", benefitName, minimumCount };

			minimumCount = Math.Max(0, minimumCount);
			var value = Enum.Parse(enumType, benefitName);
			var before = list.Cast<object>().Count(item => Equals(item, value));
			while (list.Cast<object>().Count(item => Equals(item, value)) < minimumCount)
				list.Add(value);
			RepairHostLink(symbiant);
			ZombieSymbiant.NotifyHostCapacityBenefitsChanged(symbiant.LinkedHost);
			var after = list.Cast<object>().Count(item => Equals(item, value));
			return new
			{
				success = after >= minimumCount,
				benefitName,
				minimumCount,
				before,
				after,
				added = after - before
			};
		}

		static object ProbeSymbiantAutoHealAvailability(ZombieSymbiant symbiant, Pawn host, bool expectActive)
		{
			var configuredCount = SymbiantHostBenefitCountForProbe(symbiant, "AutoHeal");
			var torso = host?.RaceProps?.body?.AllParts?.FirstOrDefault(part => part.def == BodyPartDefOf.Torso);
			if (symbiant == null || host?.health?.hediffSet == null || torso == null)
				return new { success = false, error = "Symbiant, host health, or torso is missing for the auto-heal availability probe.", expectActive, configuredCount };

			var injury = HediffMaker.MakeHediff(HediffDefOf.Cut, host, torso) as Hediff_Injury;
			if (injury == null)
				return new { success = false, error = "Could not create the auto-heal availability probe injury.", expectActive, configuredCount };
			try
			{
				injury.Severity = 7f;
				host.health.AddHediff(injury, torso);
				AccessTools.Method(typeof(ZombieSymbiant), "TryAutoHealHost")?.Invoke(symbiant, null);
				var injuryStillPresent = host.health.hediffSet.hediffs.Contains(injury);
				var injuryHealed = injuryStillPresent == false || injury.Severity <= 0.001f;
				return new
				{
					success = configuredCount > 0 && injuryHealed == expectActive,
					expectActive,
					configuredCount,
					injuryHealed,
					injuryStillPresent,
					injurySeverityAfter = injury.Severity
				};
			}
			finally
			{
				if (host.health?.hediffSet?.hediffs?.Contains(injury) == true)
					host.health.RemoveHediff(injury);
			}
		}

		sealed class SymbiantSecondMapFixture
		{
			public Map originMap;
			public Map previousCurrentMap;
			public Map map;
			public MapParent parent;
			public PlanetTile tile = PlanetTile.Invalid;
			public readonly HashSet<Pawn> trackedPawns = [];
			public object generatedPawnDisposal;
		}

		static bool TryCreateSymbiantSecondMapFixture(Map originMap, out SymbiantSecondMapFixture fixture, out object error)
		{
			fixture = new SymbiantSecondMapFixture
			{
				originMap = originMap,
				previousCurrentMap = Current.Game?.CurrentMap
			};
			error = null;
			try
			{
				var tile = FindSymbiantSecondMapTile(originMap);
				if (tile == PlanetTile.Invalid)
				{
					error = new { success = false, error = "Could not find an unused valid tile for the Symbiant second-map fixture." };
					return false;
				}

				var sitePart = DefDatabase<SitePartDef>.GetNamedSilentFail("PossibleUnknownThreatMarker");
				if (sitePart == null)
				{
					error = new { success = false, error = "The core PossibleUnknownThreatMarker SitePartDef is missing." };
					return false;
				}
				var parent = SiteMaker.MakeSite(sitePart, tile, null, false, 0f, null);
				if (parent == null)
				{
					error = new { success = false, error = "SiteMaker did not create the Symbiant second-map Site." };
					return false;
				}

				fixture.tile = tile;
				fixture.parent = parent;
				Find.WorldObjects.Add(parent);
				fixture.map = MapGenerator.GenerateMap(new IntVec3(150, 1, 150), parent, MapGeneratorDefOf.Encounter, null, null, false);
				fixture.map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
				var generatedPawns = SymbiantSecondMapPawns(fixture.map).ToArray();
				fixture.generatedPawnDisposal = DisposeSymbiantSecondMapPawns(fixture, generatedPawns, "afterMapGeneration");
				if (ScenarioSucceeded(fixture.generatedPawnDisposal) == false)
				{
					error = new
					{
						success = false,
						error = "Could not deterministically dispose every pawn generated for the Symbiant second-map fixture.",
						pawnDisposal = fixture.generatedPawnDisposal
					};
					return false;
				}
				return true;
			}
			catch (Exception ex)
			{
				error = new
				{
					success = false,
					error = ex.Message,
					exceptionType = ex.GetType().FullName,
					stackTrace = ex.StackTrace
				};
				return false;
			}
		}

		static PlanetTile FindSymbiantSecondMapTile(Map originMap)
		{
			for (var i = 0; i < 100; i++)
			{
				var tile = TileFinder.RandomStartingTile();
				if (tile != PlanetTile.Invalid
					&& (originMap == null || tile != originMap.Tile)
					&& Find.WorldObjects.MapParentAt(tile) == null
					&& TileFinder.IsValidTileForNewSettlement(tile))
					return tile;
			}
			return PlanetTile.Invalid;
		}

		static object DescribeSymbiantSecondMapFixture(SymbiantSecondMapFixture fixture)
		{
			var map = fixture?.map;
			return new
			{
				success = map != null && fixture.parent != null && map != fixture.originMap && map.Parent == fixture.parent,
				originMapId = fixture?.originMap?.uniqueID ?? -1,
				mapId = map?.uniqueID ?? -1,
				mapIndex = map?.Index ?? -1,
				mapSize = map == null ? null : new { x = map.Size.x, z = map.Size.z },
				parent = fixture?.parent?.def?.defName,
				tile = fixture?.tile.ToString(),
				generatedPawnDisposal = fixture?.generatedPawnDisposal
			};
		}

		static IEnumerable<Pawn> SymbiantSecondMapPawns(Map map)
		{
			if (map == null)
				yield break;
			var seen = new HashSet<Pawn>();
			foreach (var pawn in map.mapPawns == null ? Enumerable.Empty<Pawn>() : map.mapPawns.AllPawns)
				if (pawn != null && seen.Add(pawn))
					yield return pawn;
			foreach (var corpse in map.listerThings?.ThingsInGroup(ThingRequestGroup.Corpse)?.OfType<Corpse>() ?? Enumerable.Empty<Corpse>())
				if (corpse?.InnerPawn != null && seen.Add(corpse.InnerPawn))
					yield return corpse.InnerPawn;
		}

		static object DisposeSymbiantSecondMapPawns(
			SymbiantSecondMapFixture fixture,
			IEnumerable<Pawn> pawns,
			string stage,
			bool allowPassedToWorldBeforeDisposal = false)
		{
			var map = fixture?.map;
			var mapLoaded = map != null && Find.Maps.Contains(map);
			var requested = (pawns ?? Enumerable.Empty<Pawn>()).Where(pawn => pawn != null).Distinct().ToArray();
			var records = new List<object>();
			var passedToWorldBeforeDisposal = false;
			foreach (var pawn in requested)
			{
				fixture?.trackedPawns.Add(pawn);
				var id = ZombieRuntimeActions.StableThingId(pawn);
				var worldPawnBefore = Find.WorldPawns?.Contains(pawn) == true;
				passedToWorldBeforeDisposal |= worldPawnBefore;
				var corpse = pawn.Corpse;
				if (corpse != null && corpse.Destroyed == false)
					corpse.Destroy(DestroyMode.Vanish);
				if (Find.WorldPawns?.Contains(pawn) == true)
					Find.WorldPawns.RemovePawn(pawn);
				if (pawn.Destroyed == false)
					pawn.Destroy(DestroyMode.Vanish);
				var worldPawnAfterDestroy = Find.WorldPawns?.Contains(pawn) == true;
				if (worldPawnAfterDestroy)
					Find.WorldPawns.RemovePawn(pawn);
				if (pawn.Discarded == false)
					pawn.Discard(true);
				if (Find.WorldPawns?.Contains(pawn) == true)
					Find.WorldPawns.RemovePawn(pawn);
				var worldPawnAfter = Find.WorldPawns?.Contains(pawn) == true;
				var stillOnFixtureMap = mapLoaded && SymbiantSecondMapPawns(map).Contains(pawn);
				records.Add(new
				{
					id,
					worldPawnBefore,
					worldPawnAfterDestroy,
					worldPawnAfter,
					stillOnFixtureMap,
					destroyed = pawn.Destroyed,
					discarded = pawn.Discarded
				});
			}

			var trackedPawns = fixture == null ? Enumerable.Empty<Pawn>() : fixture.trackedPawns;
			var worldPawnIdsAfter = trackedPawns
				.Where(pawn => pawn != null && Find.WorldPawns?.Contains(pawn) == true)
				.Select(ZombieRuntimeActions.StableThingId)
				.ToArray();
			var fixtureMapPawnIdsAfter = (mapLoaded ? SymbiantSecondMapPawns(map) : Enumerable.Empty<Pawn>())
				.Select(ZombieRuntimeActions.StableThingId)
				.ToArray();
			return new
			{
				success = (passedToWorldBeforeDisposal == false || allowPassedToWorldBeforeDisposal)
					&& requested.All(pawn => pawn.Destroyed || pawn.Discarded)
					&& worldPawnIdsAfter.Length == 0
					&& fixtureMapPawnIdsAfter.Length == 0,
				stage,
				requested = requested.Length,
				passedToWorldBeforeDisposal,
				allowPassedToWorldBeforeDisposal,
				records = records.ToArray(),
				worldPawnIdsAfter,
				fixtureMapPawnIdsAfter
			};
		}

		static object MovePawnToSymbiantContractMap(Pawn pawn, Map targetMap, IntVec3 near, ZombieSymbiant symbiant)
		{
			if (pawn == null || pawn.Destroyed || pawn.Dead)
				return new { success = false, error = "The linked host is unavailable for a cross-map move." };
			if (targetMap == null)
				return new { success = false, error = "The target map is missing for a cross-map move." };

			var sourceMap = pawn.Map;
			var sourceMapId = sourceMap?.uniqueID ?? -1;
			var sourceThingId = pawn.ThingID;
			var targetCell = targetMap.AllCells
				.Where(cell => cell.Standable(targetMap)
					&& cell.GetEdifice(targetMap) == null
					&& cell.GetThingList(targetMap).Any(thing => thing is Pawn) == false)
				.OrderBy(cell => cell.DistanceToSquared(near))
				.FirstOrDefault();
			if (targetCell.IsValid == false)
				return new { success = false, error = "No clear standable cell exists on the target map.", targetMapId = targetMap.uniqueID };

			if (pawn.Spawned)
				_ = pawn.DeSpawnOrDeselect(DestroyMode.Vanish);
			targetMap.fogGrid.Unfog(targetCell);
			GenSpawn.Spawn(pawn, targetCell, targetMap, Rot4.Random, WipeMode.Vanish);
			var worldPawnAfterSpawn = Find.WorldPawns?.Contains(pawn) == true;
			if (worldPawnAfterSpawn)
				Find.WorldPawns.RemovePawn(pawn);
			AccessTools.Method(typeof(ZombieSymbiant), "EnsureHostLink")?.Invoke(symbiant, null);
			return new
			{
				success = pawn.Spawned
					&& pawn.Map == targetMap
					&& pawn.ThingID == sourceThingId
					&& Find.WorldPawns?.Contains(pawn) != true,
				host = ZombieRuntimeActions.StableThingId(pawn),
				hostThingId = pawn.ThingID,
				sourceMapId,
				targetMapId = targetMap.uniqueID,
				worldPawnAfterSpawn,
				worldPawnAfterCleanup = Find.WorldPawns?.Contains(pawn) == true,
				cell = ZombieRuntimeActions.DescribeCell(targetCell)
			};
		}

		static object CleanupSymbiantSecondMapFixture(
			SymbiantSecondMapFixture fixture,
			bool cleanup,
			bool allowPassedToWorldBeforeDisposal = false)
		{
			if (fixture == null)
				return new { success = true, skipped = true, removedMap = false, removedParent = false };
			if (cleanup == false)
				return new { success = true, skipped = true, removedMap = false, removedParent = false, mapId = fixture.map?.uniqueID ?? -1 };

			try
			{
				var map = fixture.map;
				var mapId = map?.uniqueID ?? -1;
				var parent = fixture.parent;
				var mapLoaded = map != null && Find.Maps.Contains(map);
				var cleanupPawns = (mapLoaded ? SymbiantSecondMapPawns(map) : Enumerable.Empty<Pawn>())
					.Concat(fixture.trackedPawns)
					.Distinct()
					.ToArray();
				var disposalStage = mapLoaded ? "beforeDeinitAndRemoveMap" : "afterMapRemoval";
				var pawnDisposalBeforeDeinit = DisposeSymbiantSecondMapPawns(
					fixture,
					cleanupPawns,
					disposalStage,
					allowPassedToWorldBeforeDisposal);
				var fixturePawnIdsBeforeDeinit = (mapLoaded ? SymbiantSecondMapPawns(map) : Enumerable.Empty<Pawn>())
					.Select(ZombieRuntimeActions.StableThingId)
					.ToArray();
				var worldPawnIdsBeforeDeinit = fixture.trackedPawns
					.Where(pawn => pawn != null && Find.WorldPawns?.Contains(pawn) == true)
					.Select(ZombieRuntimeActions.StableThingId)
					.ToArray();
				if (map != null && Find.Maps.Contains(map))
				{
					if (Current.Game.CurrentMap == map && fixture.originMap != null && Find.Maps.Contains(fixture.originMap))
						Current.Game.CurrentMap = fixture.originMap;
					Current.Game.DeinitAndRemoveMap(map, false);
				}
				if (parent != null && parent.Destroyed == false)
					parent.Destroy();
				if (fixture.previousCurrentMap != null && Find.Maps.Contains(fixture.previousCurrentMap))
					Current.Game.CurrentMap = fixture.previousCurrentMap;
				var worldPawnIdsAfterDeinit = fixture.trackedPawns
					.Where(pawn => pawn != null && Find.WorldPawns?.Contains(pawn) == true)
					.Select(ZombieRuntimeActions.StableThingId)
					.ToArray();
				return new
				{
					success = ScenarioSucceeded(pawnDisposalBeforeDeinit)
						&& fixturePawnIdsBeforeDeinit.Length == 0
						&& worldPawnIdsBeforeDeinit.Length == 0
						&& worldPawnIdsAfterDeinit.Length == 0
						&& (map == null || Find.Maps.Contains(map) == false)
						&& (parent == null || parent.Destroyed),
					skipped = false,
					mapId,
					generatedPawnDisposal = fixture.generatedPawnDisposal,
					pawnDisposalBeforeDeinit,
					fixturePawnIdsBeforeDeinit,
					worldPawnIdsBeforeDeinit,
					worldPawnIdsAfterDeinit,
					removedMap = map == null || Find.Maps.Contains(map) == false,
					removedParent = parent == null || parent.Destroyed
				};
			}
			catch (Exception ex)
			{
				return new { success = false, skipped = false, error = ex.ToString() };
			}
		}

		[Tool("zombieland/symbiant_combat_isolation_contract", Description = "Verify Symbiant combat-cell targeting, or stage/read/clean a real assault-colony AI scenario.")]
		public static object SymbiantCombatIsolationContract(
			[ToolParameter(Description = "contract, setup-assault, read-assault, or cleanup.", Required = false, DefaultValue = "contract")] string mode = "contract",
			[ToolParameter(Description = "Destroy temporary pawns, feed corpse, letter, and symbiant after capturing evidence.", Required = false, DefaultValue = true)] bool cleanup = true)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded." };
			mode = (mode ?? "contract").Trim().ToLowerInvariant();
			if (mode == "read-assault")
				return DescribeSymbiantAssaultState(map);
			if (mode == "cleanup")
			{
				var fixtureCaskets = map.listerThings.AllThings
					.OfType<Building_CryptosleepCasket>()
					.Where(casket => casket.GetDirectlyHeldThings().OfType<Pawn>()
						.Any(pawn => pawn?.Name?.ToStringShort?.StartsWith("ZL_SymbiantCombat_", StringComparison.Ordinal) == true))
					.ToArray();
				foreach (var casket in fixtureCaskets)
					casket.EjectContents();
				var fixturePawns = map.mapPawns.AllPawns
					.Where(pawn => pawn?.Name?.ToStringShort?.StartsWith("ZL_SymbiantCombat_", StringComparison.Ordinal) == true)
					.ToArray();
				foreach (var pawn in fixturePawns)
					if (pawn.Destroyed == false)
						pawn.Destroy(DestroyMode.Vanish);
				foreach (var casket in fixtureCaskets)
					if (casket.Destroyed == false)
						casket.Destroy(DestroyMode.Vanish);
				var fixtureSymbiant = ZombieSymbiant.ActiveSymbiant(map);
				var symbiantCleanup = CleanupTemporarySymbiant(map, fixtureSymbiant, true);
				return new { success = fixturePawns.All(pawn => pawn.Destroyed) && fixtureCaskets.All(casket => casket.Destroyed) && ZombieSymbiant.ActiveSymbiant(map) == null, removedPawns = fixturePawns.Length, removedCaskets = fixtureCaskets.Length, symbiantCleanup };
			}
			if (mode != "contract" && mode != "setup-assault")
				return new { success = false, error = "Unsupported mode.", mode, supported = new[] { "contract", "setup-assault", "read-assault", "cleanup" } };
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
			var debugMaxCellsBefore = ZombieSymbiant.DebugMaxCellsOverride;
			var beforeLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>()).ToHashSet();
			var spawnedThings = new List<Thing>();
			ZombieSymbiant symbiant = null;
			Pawn dedicatedHost = null;
			Building_CryptosleepCasket hostCasket = null;
			object result;

			try
			{
				ApplyZombieSettingsOverride(settings =>
				{
					settings.showZombieEventLetters = false;
					settings.attackMode = AttackMode.Everything;
					settings.enemyZombieResponse = ZombieResponsePolicy.Full;
					settings.animalsAttackZombies = true;
					settings.symbiantMaxCells = Math.Max(settings.symbiantMaxCells, 5);
				});
				ZombieSymbiant.SetDebugMaxCellsOverride(5);

				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindSymbiantCombatFixtureCells(map, root, 6, out var cells, out var cellError) == false)
					return cellError;

				var player = SpawnArmedAreaWorkflowPawn(map, "ZL_SymbiantCombat_Player", cells[0], Faction.OfPlayer, spawnedThings);
				var enemy = SpawnArmedAreaWorkflowPawn(map, "ZL_SymbiantCombat_Enemy", cells[1], hostileFaction, spawnedThings);
				var enemySecond = SpawnArmedAreaWorkflowPawn(map, "ZL_SymbiantCombat_EnemySecond", cells[5], hostileFaction, spawnedThings);
				var animal = SpawnAreaWorkflowAnimal(map, "ZL_SymbiantCombat_Animal", cells[2], Faction.OfPlayer, spawnedThings, def => def.combatPower > 0f);
				var predator = SpawnAreaWorkflowAnimal(map, "ZL_SymbiantCombat_Predator", cells[3], Faction.OfPlayer, spawnedThings, def => def.RaceProps?.predator == true || def.combatPower >= 1f);
				var noSymbiantScanGate = VerifyNoSymbiantTargetScanGate(enemy);
				var symbiantShape = SymbiantCombatCrossCells(cells[4]);
				symbiant = ZombieSymbiant.DebugSpawnForRendering(map, cells[4], symbiantShape);
				if (symbiant != null)
					symbiant.Name = new NameSingle("ZL_SymbiantCombat_Goo");
				if (TryFindClearBuildingCell(map, cells[4] + new IntVec3(8, 0, 8), 20f, out var hostCasketCell, out var hostCasketCellError) == false)
					return hostCasketCellError;
				var casketDef = DefDatabase<ThingDef>.GetNamedSilentFail("CryptosleepCasket");
				hostCasket = casketDef == null
					? null
					: GenSpawn.Spawn(ThingMaker.MakeThing(casketDef), hostCasketCell, map, Rot4.North, WipeMode.Vanish) as Building_CryptosleepCasket;
				if (hostCasket == null)
					return new { success = false, error = "Could not spawn the same-map host casket for the Symbiant combat fixture." };
				spawnedThings.Add(hostCasket);
				if (TryFindClearSpawnCell(map, hostCasketCell + IntVec3.East, 6f, out var hostSpawnCell, out var hostSpawnCellError) == false)
					return hostSpawnCellError;
				dedicatedHost = SpawnAreaWorkflowPawn(map, "ZL_SymbiantCombat_Host", hostSpawnCell, Faction.OfPlayer, spawnedThings);
				AccessTools.Method(typeof(ZombieSymbiant), "AssignHost")?.Invoke(symbiant, new object[] { dedicatedHost });
				dedicatedHost.DeSpawn();
				var hostAccepted = hostCasket.TryAcceptThing(dedicatedHost, false);
				var hostHeldByCasket = hostCasket.GetDirectlyHeldThings().Contains(dedicatedHost);
				var hostCasketSetup = new
				{
					success = hostAccepted
						&& hostHeldByCasket
						&& dedicatedHost.Spawned == false
						&& dedicatedHost.MapHeld == map
						&& symbiant?.LinkedHost == dedicatedHost
						&& symbiant.IsActiveBondWith(dedicatedHost),
					hostAccepted,
					hostHeldByCasket,
					host = DescribePawn(dedicatedHost),
					casket = ZombieRuntimeActions.StableThingId(hostCasket),
					cell = ZombieRuntimeActions.DescribeCell(hostCasketCell),
					bondActive = symbiant?.IsActiveBondWith(dedicatedHost) == true
				};
				RefreshZombieTargetCache(map);
				var enemyVerb = enemy?.CurrentEffectiveVerb;
				var enemySecondVerb = enemySecond?.CurrentEffectiveVerb;
				object ceAmmoSetup = null;
				object ceAmmoSetupSecond = null;
				var ceAmmoType = AccessTools.TypeByName("CombatExtended.CompAmmoUser");
				if (ceAmmoType != null)
				{
					var ceAmmo = FindCompAssignableTo(enemy?.equipment?.Primary as ThingWithComps, ceAmmoType);
					ceAmmoSetup = SetupCeAmmoForShot(ceAmmo, ceAmmoType);
					var ceAmmoSecond = FindCompAssignableTo(enemySecond?.equipment?.Primary as ThingWithComps, ceAmmoType);
					ceAmmoSetupSecond = SetupCeAmmoForShot(ceAmmoSecond, ceAmmoType);
				}
				var rangedCell = IntVec3.Invalid;
				var rangedLine = default(ShootLine);
				var selectedRanged = enemyVerb != null
					&& ZombieSymbiantCombat.TrySelectRangedCell(enemyVerb, enemy.Position, symbiant, out rangedCell, out rangedLine);
				var rangedCellSecond = IntVec3.Invalid;
				var rangedLineSecond = default(ShootLine);
				var selectedRangedSecond = enemySecondVerb != null
					&& ZombieSymbiantCombat.TrySelectRangedCell(enemySecondVerb, enemySecond.Position, symbiant, out rangedCellSecond, out rangedLineSecond);
				var meleeStand = IntVec3.Invalid;
				var meleeTarget = IntVec3.Invalid;
				var selectedMelee = ZombieSymbiantCombat.TrySelectMeleeCells(enemy, symbiant, out meleeStand, out meleeTarget);
				var logicalCells = ZombieSymbiantCombat.Cells(symbiant).ToArray();
				var thingGridRegistrations = logicalCells.Count(cell => cell.GetThingList(map).Contains(symbiant));
				var logicalVerbFamilies = VerifySymbiantLogicalVerbFamilies(enemyVerb);
				var combatGeometry = new
				{
					success = symbiantShape.Length == 5
						&& logicalCells.Length == symbiantShape.Length
						&& logicalCells.Length == symbiant.CellCount
						&& map.mapPawns.AllPawnsSpawned.Count(pawn => pawn is ZombieSymbiant) == 1
						&& thingGridRegistrations == 1
						&& selectedRanged
						&& symbiant.ContainsCell(rangedCell)
						&& selectedRangedSecond
						&& symbiant.ContainsCell(rangedCellSecond)
						&& selectedMelee
						&& symbiant.ContainsCell(meleeTarget)
						&& meleeStand.AdjacentTo8WayOrInside(meleeTarget),
					root = ZombieRuntimeActions.DescribeCell(symbiant.Position),
					expectedLogicalCellCount = symbiantShape.Length,
					logicalCellCount = logicalCells.Length,
					boundaryCellCount = ZombieSymbiantCombat.BoundaryCells(symbiant).Count,
					pawnIdentityCount = map.mapPawns.AllPawnsSpawned.Count(pawn => pawn is ZombieSymbiant),
					thingGridRegistrations,
					selectedRanged,
					rangedCell = rangedCell.IsValid ? ZombieRuntimeActions.DescribeCell(rangedCell) : null,
					rangedLine = selectedRanged ? new { source = ZombieRuntimeActions.DescribeCell(rangedLine.Source), dest = ZombieRuntimeActions.DescribeCell(rangedLine.Dest) } : null,
					selectedRangedSecond,
					rangedCellSecond = rangedCellSecond.IsValid ? ZombieRuntimeActions.DescribeCell(rangedCellSecond) : null,
					rangedLineSecond = selectedRangedSecond ? new { source = ZombieRuntimeActions.DescribeCell(rangedLineSecond.Source), dest = ZombieRuntimeActions.DescribeCell(rangedLineSecond.Dest) } : null,
					selectedMelee,
					meleeStand = meleeStand.IsValid ? ZombieRuntimeActions.DescribeCell(meleeStand) : null,
					meleeTarget = meleeTarget.IsValid ? ZombieRuntimeActions.DescribeCell(meleeTarget) : null
				};
				var nearestLogicalDistance = logicalCells.Min(cell => cell.DistanceTo(enemy.Position));
				var rootDistance = symbiant.Position.DistanceTo(enemy.Position);
				var logicalRangeLimit = (nearestLogicalDistance + rootDistance) / 2f;
				var logicalRangeTarget = rootDistance > nearestLogicalDistance
					? AttackTargetFinder.BestAttackTarget(enemy, TargetScanFlags.NeedThreat, thing => thing == symbiant, 0f, logicalRangeLimit)
					: null;
				var validatorRejectedTarget = AttackTargetFinder.BestAttackTarget(enemy, TargetScanFlags.NeedThreat, _ => false, 0f, 999f);
				var originalRoofs = logicalCells.ToDictionary(cell => cell, cell => map.roofGrid.RoofAt(cell));
				IAttackTarget thickRoofRejectedTarget = null;
				try
				{
					foreach (var cell in logicalCells)
						map.roofGrid.SetRoof(cell, RoofDefOf.RoofRockThick);
					thickRoofRejectedTarget = AttackTargetFinder.BestAttackTarget(
						enemy,
						TargetScanFlags.NeedThreat | TargetScanFlags.NeedNotUnderThickRoof,
						thing => thing == symbiant,
						0f,
						999f);
				}
				finally
				{
					foreach (var pair in originalRoofs)
						map.roofGrid.SetRoof(pair.Key, pair.Value);
				}
				var targetScanGates = new
				{
					success = rootDistance > logicalRangeLimit
						&& nearestLogicalDistance <= logicalRangeLimit
						&& logicalRangeTarget?.Thing == symbiant
						&& validatorRejectedTarget == null
						&& thickRoofRejectedTarget == null,
					rootDistance,
					nearestLogicalDistance,
					logicalRangeLimit,
					logicalRangeTarget = DescribeTarget(logicalRangeTarget),
					validatorRejectedTarget = DescribeTarget(validatorRejectedTarget),
					thickRoofRejectedTarget = DescribeTarget(thickRoofRejectedTarget)
				};
				var targetScanMemoization = VerifySymbiantTargetScanMemoization(enemy, player, enemyVerb, symbiant);
				var blastFriendlyFireParity = VerifySymbiantBlastFriendlyFireParity(enemy, symbiant);
				var blastCell = logicalCells.FirstOrDefault(cell => cell != symbiant.Position);
				var sharedHealthBeforeBlast = symbiant.DamageAbsorptionBuffer;
				var explosionMatchedBefore = ZombieSymbiantCombat.ExplosionMatchedCellCount;
				var explosionAppliedBefore = ZombieSymbiantCombat.ExplosionAppliedDamageCount;
				var explosionAlreadyDamagedBefore = ZombieSymbiantCombat.ExplosionAlreadyDamagedCount;
				if (blastCell.IsValid)
					GenExplosion.DoExplosion(
						blastCell,
						map,
						0.49f,
						DamageDefOf.Bomb,
						enemy,
						5,
						0f,
						doVisualEffects: false,
						doSoundEffects: false);
				if (blastCell.IsValid)
					AdvanceGameTicks(3);
				var sharedHealthAfterBlast = symbiant.DamageAbsorptionBuffer;
				var excludedHealthBefore = symbiant.DamageAbsorptionBuffer;
				var excludedMatchedBefore = ZombieSymbiantCombat.ExplosionMatchedCellCount;
				var excludedAppliedBefore = ZombieSymbiantCombat.ExplosionAppliedDamageCount;
				if (blastCell.IsValid)
					GenExplosion.DoExplosion(
						blastCell,
						map,
						0.49f,
						DamageDefOf.Bomb,
						enemy,
						5,
						0f,
						doVisualEffects: false,
						excludeRadius: 0.25f,
						doSoundEffects: false);
				if (blastCell.IsValid)
					AdvanceGameTicks(3);
				var excludedHealthAfter = symbiant.DamageAbsorptionBuffer;
				var excludedCellIgnored = excludedHealthAfter == excludedHealthBefore
					&& ZombieSymbiantCombat.ExplosionMatchedCellCount == excludedMatchedBefore
					&& ZombieSymbiantCombat.ExplosionAppliedDamageCount == excludedAppliedBefore;
				var explosionDamage = new
				{
					success = blastCell.IsValid
						&& symbiant.ContainsCell(blastCell)
						&& blastCell.GetThingList(map).Contains(symbiant) == false
						&& sharedHealthBeforeBlast - sharedHealthAfterBlast == 5
						&& excludedCellIgnored,
					blastCell = blastCell.IsValid ? ZombieRuntimeActions.DescribeCell(blastCell) : null,
					ownerRegisteredAtBlastCell = blastCell.IsValid && blastCell.GetThingList(map).Contains(symbiant),
					sharedHealthBeforeBlast,
					sharedHealthAfterBlast,
					delta = sharedHealthBeforeBlast - sharedHealthAfterBlast,
					matchedCells = ZombieSymbiantCombat.ExplosionMatchedCellCount - explosionMatchedBefore,
					appliedDamageCalls = ZombieSymbiantCombat.ExplosionAppliedDamageCount - explosionAppliedBefore,
					alreadyDamagedMatches = ZombieSymbiantCombat.ExplosionAlreadyDamagedCount - explosionAlreadyDamagedBefore,
					lastMatchedCell = ZombieSymbiantCombat.LastExplosionMatchedCell.IsValid ? ZombieRuntimeActions.DescribeCell(ZombieSymbiantCombat.LastExplosionMatchedCell) : null,
					excludedInnerCell = new
					{
						success = excludedCellIgnored,
						healthBefore = excludedHealthBefore,
						healthAfter = excludedHealthAfter,
						matchedCells = ZombieSymbiantCombat.ExplosionMatchedCellCount - excludedMatchedBefore,
						appliedDamageCalls = ZombieSymbiantCombat.ExplosionAppliedDamageCount - excludedAppliedBefore
					}
				};

				var pawnSystems = DescribeSymbiantCombatPawnSystems(map, symbiant, player, enemy);
				var targetFinding = new
				{
					player = DescribeBestSymbiantTarget(player, symbiant, false),
					enemy = DescribeBestSymbiantTarget(enemy, symbiant, true),
					enemySecond = DescribeBestSymbiantTarget(enemySecond, symbiant, true),
					animal = DescribeBestSymbiantTarget(animal, symbiant, false),
					predator = DescribeBestSymbiantTarget(predator, symbiant, false)
				};
				var forcedJobs = new
				{
					playerMelee = VerifySymbiantAttackJob(player, symbiant, JobDefOf.AttackMelee, false),
					playerStatic = VerifySymbiantAttackJob(player, symbiant, JobDefOf.AttackStatic, false),
					enemyMelee = VerifySymbiantAttackJob(enemy, symbiant, JobDefOf.AttackMelee, true),
					enemySecondMelee = VerifySymbiantAttackJob(enemySecond, symbiant, JobDefOf.AttackMelee, true),
					symbiantMelee = VerifySymbiantAttackJob(symbiant, player, JobDefOf.AttackMelee, false)
				};
				object meleeRebinding = mode == "setup-assault"
					? new { success = true, skipped = true, reason = "Preserve the full logical shape for the real ranged-impact scenario." }
					: VerifySymbiantMeleeRebinding(enemy, symbiant);
				var animalResponse = new
				{
					manhunterChance = animal == null || symbiant == null ? null : (float?)PawnUtility.GetManhunterOnDamageChance(animal, symbiant, animal.Position.DistanceTo(symbiant.Position)),
					preyScore = predator == null || symbiant == null ? null : (float?)FoodUtility.GetPreyScoreFor(predator, symbiant)
				};
				if (mode == "setup-assault")
				{
					foreach (var bystander in new[] { player, animal, predator })
						if (bystander?.Destroyed == false)
							bystander.Destroy(DestroyMode.Vanish);
					enemy.jobs?.StopAll(false, true);
					enemySecond.jobs?.StopAll(false, true);
					_ = LordMaker.MakeNewLord(
						hostileFaction,
						new LordJob_AssaultColony(hostileFaction, true, true, false, false, true, false, true),
						map,
						new[] { enemy, enemySecond });
				}
				var patchTargets = new
				{
					availableShootingTargets = PatchedMethodsForPatchClass("AttackTargetFinder_GetAvailableShootingTargetsByScore_Patch"),
					bestAttackTarget = PatchedMethodsForPatchClass("AttackTargetFinder_BestAttackTarget_Patch"),
					hostileThingThing = PatchedMethodsForPatchClass("GenHostility_HostileTo_Thing_Thing_Patch"),
					hostileThingFaction = PatchedMethodsForPatchClass("GenHostility_HostileTo_Thing_Faction_Patch"),
					activeThreat = PatchedMethodsForPatchClass("GenHostility_IsActiveThreat_Patch"),
					threatDisabled = PatchedMethodsForPatchClass("Pawn_ThreatDisabled_Symbiant_Patch"),
					registerTarget = PatchedMethodsForPatchClass("AttackTargetsCache_RegisterTarget_Patch"),
					startJob = PatchedMethodsForPatchClass("Pawn_JobTracker_StartJob_Patch"),
					dangerRating = PatchedMethodsForPatchClass("DangerWatcher_CalculateDangerRating_Patch"),
					flee = PatchedMethodsForPatchClass("FleeUtility_ShouldFleeFrom_Patch"),
					manhunter = PatchedMethodsForPatchClass("PawnUtility_GetManhunterOnDamageChance_Patch"),
					prey = PatchedMethodsForPatchClass("FoodUtility_GetPreyScoreFor_Patch")
					,
					shootableCells = PatchedMethodsForPatchClass("ShootLeanUtility_CalcShootableCellsOf_Symbiant_Patch")
					,
					shootLine = PatchedMethodsForPatchClass("Verb_TryFindShootLineFromTo_Symbiant_Patch")
					,
					projectileLaunch = PatchedMethodsForPatchClass("Projectile_Launch_SymbiantCell_Patch")
					,
					projectileImpact = PatchedMethodsForPatchClass("Projectile_ImpactSomething_SymbiantCell_Patch")
					,
					explosion = PatchedMethodsForPatchClass("Explosion_AffectCell_Patch")
				};

				var success = symbiant?.Spawned == true
					&& ScenarioSucceeded(hostCasketSetup)
					&& ScenarioSucceeded(noSymbiantScanGate)
					&& ScenarioSucceeded(combatGeometry)
					&& ScenarioSucceeded(logicalVerbFamilies)
					&& ScenarioSucceeded(targetScanGates)
					&& ScenarioSucceeded(targetScanMemoization)
					&& ScenarioSucceeded(blastFriendlyFireParity)
					&& ScenarioSucceeded(explosionDamage)
					&& ScenarioSucceeded(pawnSystems)
					&& ScenarioSucceeded(targetFinding.player)
					&& ScenarioSucceeded(targetFinding.enemy)
					&& ScenarioSucceeded(targetFinding.enemySecond)
					&& ScenarioSucceeded(targetFinding.animal)
					&& ScenarioSucceeded(targetFinding.predator)
					&& ScenarioSucceeded(forcedJobs.playerMelee)
					&& ScenarioSucceeded(forcedJobs.playerStatic)
					&& ScenarioSucceeded(forcedJobs.enemyMelee)
					&& ScenarioSucceeded(forcedJobs.enemySecondMelee)
					&& ScenarioSucceeded(forcedJobs.symbiantMelee)
					&& ScenarioSucceeded(meleeRebinding)
					&& animalResponse.manhunterChance == 0f
					&& animalResponse.preyScore <= -9999f
					&& patchTargets.bestAttackTarget.Length > 0
					&& patchTargets.hostileThingThing.Length > 0
					&& patchTargets.activeThreat.Length > 0
					&& patchTargets.threatDisabled.Length > 0
					&& patchTargets.startJob.Length > 0;

				result = new
				{
					success,
					sourcePath = "ZombieSymbiantCombat + Patches_Hostility + projectile/melee/explosion adapters",
					fixtureCells = cells.Select(ZombieRuntimeActions.DescribeCell).ToArray(),
					pawns = new
					{
						host = DescribePawn(dedicatedHost),
						player = DescribePawn(player),
						enemy = DescribePawn(enemy),
						enemySecond = DescribePawn(enemySecond),
						animal = DescribePawn(animal),
						predator = DescribePawn(predator),
						symbiant = DescribePawn(symbiant)
					},
					hostCasketSetup,
					noSymbiantScanGate,
					pawnSystems,
					combatGeometry,
					logicalVerbFamilies,
					targetScanGates,
					targetScanMemoization,
					blastFriendlyFireParity,
					ceAmmoSetup,
					ceAmmoSetupSecond,
					explosionDamage,
					targetFinding,
					forcedJobs,
					meleeRebinding,
					animalResponse,
					patchTargets
					,
					assault = mode == "setup-assault" ? DescribeSymbiantAssaultState(map) : null
				};
			}
			catch (Exception ex)
			{
				result = new { success = false, error = ex.ToString() };
			}
			finally
			{
				var shouldCleanup = cleanup && mode != "setup-assault";
				_ = CleanupTemporarySymbiant(map, symbiant, shouldCleanup);
				if (shouldCleanup)
				{
					if (hostCasket?.Destroyed == false)
						hostCasket.EjectContents();
					foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).Distinct().ToArray())
						thing.Destroy(DestroyMode.Vanish);
				}
				var newLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
					.Where(letter => beforeLetters.Contains(letter) == false)
					.ToArray();
				_ = CleanupTemporaryLetters(newLetters, shouldCleanup);
				ZombieSymbiant.SetDebugMaxCellsOverride(debugMaxCellsBefore);
				RestoreZombieSettings(settingsSnapshot);
			}

			return result;
		}

		static object DescribeSymbiantAssaultState(Map map)
		{
			var symbiant = ZombieSymbiant.ActiveSymbiant(map);
			var host = map.mapPawns.AllPawns
				.FirstOrDefault(pawn => pawn?.Name?.ToStringShort == "ZL_SymbiantCombat_Host");
			var attackers = map.mapPawns.AllPawnsSpawned
				.Where(pawn => pawn?.Name?.ToStringShort?.StartsWith("ZL_SymbiantCombat_Enemy", StringComparison.Ordinal) == true)
				.Select(pawn => new
				{
					pawn = DescribePawn(pawn),
					lordJob = pawn.GetLord()?.LordJob?.GetType().FullName,
					duty = pawn.mindState?.duty?.def?.defName,
					stance = pawn.stances?.curStance?.GetType().FullName,
					stanceFocus = pawn.stances?.curStance is Stance_Busy busy ? ZombieRuntimeActions.StableThingId(busy.focusTarg.Thing) : null,
					lastAttackedTarget = ZombieRuntimeActions.StableThingId(pawn.mindState?.lastAttackedTarget.Thing),
					targetAThing = ZombieRuntimeActions.StableThingId(pawn.CurJob?.targetA.Thing),
					targetACell = pawn.CurJob?.targetA.IsValid == true ? ZombieRuntimeActions.DescribeCell(pawn.CurJob.targetA.Cell) : null,
					targetB = pawn.CurJob?.targetB.IsValid == true ? ZombieRuntimeActions.DescribeCell(pawn.CurJob.targetB.Cell) : null,
					targetC = pawn.CurJob?.targetC.IsValid == true ? ZombieRuntimeActions.DescribeCell(pawn.CurJob.targetC.Cell) : null,
					distanceToNearestSymbiantCell = symbiant == null ? (float?)null : ZombieSymbiantCombat.Cells(symbiant).Min(cell => cell.DistanceTo(pawn.Position))
				})
				.ToArray();
			return new
			{
				success = symbiant?.Spawned == true
					&& host != null
					&& symbiant.IsActiveBondWith(host)
					&& attackers.Length > 0
					&& attackers.All(attacker => attacker.lordJob == typeof(LordJob_AssaultColony).FullName),
				symbiant = DescribePawn(symbiant),
				host = DescribePawn(host),
				damageFacade = DescribeSymbiantDamageFacade(host, symbiant),
				logicalCells = symbiant == null ? Array.Empty<object>() : ZombieSymbiantCombat.Cells(symbiant).Select(ZombieRuntimeActions.DescribeCell).ToArray(),
				combatExtendedLogicalCollisions = new
				{
					count = ZombieSymbiantCombat.CombatExtendedLogicalCollisionCount,
					lastCell = ZombieSymbiantCombat.LastCombatExtendedLogicalCollisionCell.IsValid
						? ZombieRuntimeActions.DescribeCell(ZombieSymbiantCombat.LastCombatExtendedLogicalCollisionCell)
						: null,
					lastCellIsNonRoot = symbiant != null
						&& ZombieSymbiantCombat.LastCombatExtendedLogicalCollisionCell.IsValid
						&& ZombieSymbiantCombat.LastCombatExtendedLogicalCollisionCell != symbiant.Position
				},
				attackers
			};
		}

		static IntVec3[] SymbiantCombatCrossCells(IntVec3 root)
		{
			return new[]
			{
				root,
				root + new IntVec3(0, 0, 1),
				root + new IntVec3(1, 0, 0),
				root + new IntVec3(0, 0, -1),
				root + new IntVec3(-1, 0, 0)
			};
		}

		static bool IsClearSymbiantCombatFixtureCell(Map map, IntVec3 cell)
		{
			return map != null
				&& cell.InBounds(map)
				&& cell.Standable(map)
				&& cell.Fogged(map) == false
				&& cell.GetEdifice(map) == null
				&& cell.GetFirstPawn(map) == null
				&& cell.GetGas(map) == null
				&& cell.GetThingList(map).All(thing => thing.def?.category != ThingCategory.Building);
		}

		static bool TryFindSymbiantCombatFixtureCells(Map map, IntVec3 root, int count, out IntVec3[] cells, out object error)
		{
			foreach (var candidate in GenRadial.RadialCellsAround(root, 48f, true))
			{
				var roleCells = new[]
				{
					candidate + new IntVec3(-12, 0, 10),
					candidate + new IntVec3(0, 0, -10),
					candidate + new IntVec3(-12, 0, -10),
					candidate + new IntVec3(12, 0, 10),
					candidate,
					candidate + new IntVec3(10, 0, 0)
				};
				if (roleCells.Length != count || roleCells.Any(cell => IsClearSymbiantCombatFixtureCell(map, cell) == false))
					continue;
				var openCombatRect = CellRect.FromLimits(candidate.x - 2, candidate.z - 10, candidate.x + 10, candidate.z + 2);
				if (openCombatRect.Cells.Any(cell => IsClearSymbiantCombatFixtureCell(map, cell) == false))
					continue;
				var cross = SymbiantCombatCrossCells(candidate);
				if (cross.Any(cell => IsClearSymbiantCombatFixtureCell(map, cell) == false))
					continue;
				var southEdge = candidate + new IntVec3(0, 0, -1);
				var eastEdge = candidate + new IntVec3(1, 0, 0);
				if (GenSight.LineOfSight(roleCells[1], southEdge, map, true) == false
					|| GenSight.LineOfSight(roleCells[5], eastEdge, map, true) == false
					|| southEdge.DistanceTo(roleCells[1]) >= candidate.DistanceTo(roleCells[1])
					|| eastEdge.DistanceTo(roleCells[5]) >= candidate.DistanceTo(roleCells[5]))
					continue;
				cells = roleCells;
				error = null;
				return true;
			}
			cells = Array.Empty<IntVec3>();
			error = new { success = false, error = "Could not find a clear deterministic cross and firing corridor for the Symbiant combat fixture.", requested = count };
			return false;
		}

		static object VerifyNoSymbiantTargetScanGate(Pawn searcher)
		{
			if (searcher == null)
				return new { success = false, error = "No hostile searcher was spawned for the empty-map target-scan gate." };
			ZombieSymbiantCombat.TargetScanContext context = null;
			try
			{
				ZombieSymbiantCombat.BeginTargetScan(
					searcher,
					TargetScanFlags.None,
					null,
					0f,
					999f,
					searcher.Position,
					9999f,
					false,
					true,
					false,
					false);
				context = ZombieSymbiantCombat.CurrentTargetScan(searcher);
			}
			finally
			{
				ZombieSymbiantCombat.EndTargetScan();
			}
			return new
			{
				success = context == null,
				activeSymbiant = ZombieRuntimeActions.StableThingId(ZombieSymbiant.ActiveSymbiant(searcher.Map)),
				contextCreated = context != null
			};
		}

		static object VerifySymbiantLogicalVerbFamilies(Verb projectileVerb)
		{
			try
			{
				var beam = CreateConcreteVerbFamilyProbe(typeof(Verb_ShootBeam));
				var spray = CreateConcreteVerbFamilyProbe(typeof(Verb_Spray));
				var projectileSupported = projectileVerb != null && ZombieSymbiantCombat.SupportsLogicalRangedCells(projectileVerb);
				var beamSupported = beam != null && ZombieSymbiantCombat.SupportsLogicalRangedCells(beam);
				var spraySupported = spray != null && ZombieSymbiantCombat.SupportsLogicalRangedCells(spray);
				return new
				{
					success = projectileSupported && beam != null && spray != null && beamSupported == false && spraySupported == false,
					projectileVerb = projectileVerb?.GetType().FullName,
					projectileSupported,
					beamVerb = beam?.GetType().FullName,
					beamSupported,
					sprayVerb = spray?.GetType().FullName,
					spraySupported
				};
			}
			catch (Exception ex)
			{
				return new { success = false, error = ex.ToString() };
			}
		}

		static Verb CreateConcreteVerbFamilyProbe(Type family)
		{
			var concreteType = GenTypes.AllTypes
				.Where(type => type != null
					&& type.IsAbstract == false
					&& type.ContainsGenericParameters == false
					&& family.IsAssignableFrom(type))
				.OrderBy(type => type.FullName)
				.FirstOrDefault();
			return concreteType == null ? null : Activator.CreateInstance(concreteType, true) as Verb;
		}

		static object VerifySymbiantTargetScanMemoization(Pawn hostile, Pawn irrelevantSearcher, Verb verb, ZombieSymbiant symbiant)
		{
			var patchType = AccessTools.TypeByName("ZombieLand.AttackTargetFinder_GetRandomShootingTargetByScore_Symbiant_Patch");
			var prefix = patchType == null ? null : AccessTools.Method(patchType, "Prefix");
			if (hostile == null || irrelevantSearcher == null || verb == null || symbiant == null || prefix == null)
				return new { success = false, error = "Target-scan memoization fixture prerequisites are missing.", patchType = patchType?.FullName, prefixFound = prefix != null };

			var validatorCalls = 0;
			var targets = new List<IAttackTarget>();
			ZombieSymbiantCombat.TargetScanContext context = null;
			try
			{
				ZombieSymbiantCombat.BeginTargetScan(
					hostile,
					TargetScanFlags.None,
					thing =>
					{
						validatorCalls++;
						return thing == symbiant;
					},
					0f,
					999f,
					hostile.Position,
					9999f,
					false,
					true,
					false,
					false);
				context = ZombieSymbiantCombat.CurrentTargetScan(hostile);
				prefix.Invoke(null, new object[] { targets, hostile, verb });
				prefix.Invoke(null, new object[] { targets, hostile, verb });
			}
			finally
			{
				ZombieSymbiantCombat.EndTargetScan();
			}

			ZombieSymbiantCombat.TargetScanContext irrelevantContext = null;
			try
			{
				ZombieSymbiantCombat.BeginTargetScan(
					irrelevantSearcher,
					TargetScanFlags.None,
					null,
					0f,
					999f,
					irrelevantSearcher.Position,
					9999f,
					false,
					true,
					false,
					false);
				irrelevantContext = ZombieSymbiantCombat.CurrentTargetScan(irrelevantSearcher);
			}
			finally
			{
				ZombieSymbiantCombat.EndTargetScan();
			}

			var candidateCount = targets.Count(target => target?.Thing == symbiant);
			var shootingPoolField = AccessTools.Field(typeof(ZombieSymbiantCombat.TargetScanContext), "logicalShootingPoolEvaluated");
			var shootingPoolEvaluated = context != null && shootingPoolField != null && (bool)shootingPoolField.GetValue(context);
			var memoized = context != null
				&& context.logicalCandidateEvaluated
				&& shootingPoolEvaluated
				&& context.logicalCandidateEnteredShootingPool
				&& context.logicalCandidate == symbiant
				&& context.logicalCandidateShootable
				&& validatorCalls == 1
				&& candidateCount == 1;
			return new
			{
				success = memoized && irrelevantContext == null,
				memoized,
				validatorCalls,
				candidateCount,
				logicalCandidateEvaluated = context?.logicalCandidateEvaluated ?? false,
				logicalCandidateShootable = context?.logicalCandidateShootable ?? false,
				logicalShootingPoolEvaluated = shootingPoolEvaluated,
				logicalCandidateEnteredShootingPool = context?.logicalCandidateEnteredShootingPool ?? false,
				irrelevantContextCreated = irrelevantContext != null
			};
		}

		static object VerifySymbiantBlastFriendlyFireParity(Pawn searcher, ZombieSymbiant symbiant)
		{
			var vanilla = AccessTools.Method(typeof(AttackTargetFinder), "FriendlyFireBlastRadiusTargetScoreOffset");
			var patchType = AccessTools.TypeByName("ZombieLand.AttackTargetFinder_GetShootingTargetScore_Patch");
			var logical = patchType == null ? null : AccessTools.Method(patchType, "LogicalBlastFriendlyFireScore");
			if (searcher == null || symbiant == null || vanilla == null || logical == null)
				return new { success = false, error = "Blast friendly-fire parity fixture prerequisites are missing.", vanillaFound = vanilla != null, logicalFound = logical != null };

			ThingWithComps weapon = null;
			Verb blastVerb = null;
			foreach (var defName in new[] { "Gun_DoomsdayRocket", "Gun_TripleRocket", "Gun_InfernoCannon" })
			{
				var def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
				var candidate = def == null ? null : ThingMaker.MakeThing(def) as ThingWithComps;
				var candidateVerb = candidate?.TryGetComp<CompEquippable>()?.PrimaryVerb;
				if (candidateVerb?.verbProps?.ai_AvoidFriendlyFireRadius > 0f)
				{
					weapon = candidate;
					blastVerb = candidateVerb;
					break;
				}
			}
			if (blastVerb == null)
				return new { success = false, error = "Could not create a Core weapon verb with an AI friendly-fire blast radius." };

			try
			{
				var vanillaScore = Convert.ToSingle(vanilla.Invoke(null, new object[] { symbiant, searcher, blastVerb }));
				var logicalScore = Convert.ToSingle(logical.Invoke(null, new object[] { symbiant, searcher, blastVerb, symbiant.Position }));
				var delta = logicalScore - vanillaScore;
				return new
				{
					success = blastVerb.verbProps.ai_AvoidFriendlyFireRadius > 0f && Mathf.Abs(delta) <= 0.0001f,
					weapon = weapon?.def?.defName,
					radius = blastVerb.verbProps.ai_AvoidFriendlyFireRadius,
					center = ZombieRuntimeActions.DescribeCell(symbiant.Position),
					vanillaScore,
					logicalScore,
					delta
				};
			}
			catch (Exception ex)
			{
				return new { success = false, error = ex.ToString(), weapon = weapon?.def?.defName };
			}
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
			var targetB = afterJob?.targetB ?? LocalTargetInfo.Invalid;
			var targetC = afterJob?.targetC ?? LocalTargetInfo.Invalid;
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
				targetB = targetB.IsValid ? ZombieRuntimeActions.DescribeCell(targetB.Cell) : null,
				targetC = targetC.IsValid ? ZombieRuntimeActions.DescribeCell(targetC.Cell) : null,
				meleeCellBinding = jobDef != JobDefOf.AttackMelee || expectAccepted == false || (targetB.IsValid && targetC.IsValid),
				accepted
			};
		}

		static object VerifySymbiantMeleeRebinding(Pawn actor, ZombieSymbiant symbiant)
		{
			if (actor == null || symbiant == null)
				return new { success = false, error = "Missing melee actor or Symbiant." };
			if (ZombieSymbiantCombat.TrySelectMeleeCells(actor, symbiant, out var originalStand, out var originalTarget, Danger.Deadly, cell => cell != symbiant.Position) == false)
				return new { success = false, error = "Could not select a removable non-root melee target cell." };

			var job = JobMaker.MakeJob(JobDefOf.AttackMelee, symbiant);
			job.targetB = originalStand;
			job.targetC = originalTarget;
			actor.jobs.StartJob(job, JobCondition.InterruptForced, null, false, true);
			if (actor.CurJob != job)
				return new { success = false, error = "The melee rebind fixture job was rejected." };
			job.targetB = originalStand;
			job.targetC = originalTarget;

			var removeCell = AccessTools.Method(typeof(ZombieSymbiant), "RemoveRelativeCell");
			var removed = removeCell != null
				&& (bool)removeCell.Invoke(symbiant, new object[] { originalTarget - symbiant.Position, false });
			var passiveQuery = ZombieSymbiantCombat.TryGetMeleeJobCells(actor, symbiant, out _, out _);
			var passiveTargetB = actor.CurJob?.targetB ?? LocalTargetInfo.Invalid;
			var passiveTargetC = actor.CurJob?.targetC ?? LocalTargetInfo.Invalid;
			var explicitlyRebound = ZombieSymbiantCombat.PrepareMeleeJob(actor, actor.CurJob);
			var rebound = ZombieSymbiantCombat.TryGetMeleeJobCells(actor, symbiant, out var reboundStand, out var reboundTarget);
			var targetB = actor.CurJob?.targetB ?? LocalTargetInfo.Invalid;
			var targetC = actor.CurJob?.targetC ?? LocalTargetInfo.Invalid;
			actor.jobs.StopAll(false, true);
			return new
			{
				success = removed
					&& passiveQuery == false
					&& passiveTargetB.Cell == originalStand
					&& passiveTargetC.Cell == originalTarget
					&& explicitlyRebound
					&& rebound
					&& reboundTarget != originalTarget
					&& symbiant.ContainsCell(originalTarget) == false
					&& symbiant.ContainsCell(reboundTarget)
					&& symbiant.ContainsCell(reboundStand) == false
					&& reboundStand.AdjacentTo8WayOrInside(reboundTarget)
					&& targetB.Cell == reboundStand
					&& targetC.Cell == reboundTarget,
				removed,
				passiveQuery,
				passiveQueryPreservedJobTargets = passiveTargetB.Cell == originalStand && passiveTargetC.Cell == originalTarget,
				explicitlyRebound,
				rebound,
				originalStand = ZombieRuntimeActions.DescribeCell(originalStand),
				originalTarget = ZombieRuntimeActions.DescribeCell(originalTarget),
				reboundStand = reboundStand.IsValid ? ZombieRuntimeActions.DescribeCell(reboundStand) : null,
				reboundTarget = reboundTarget.IsValid ? ZombieRuntimeActions.DescribeCell(reboundTarget) : null,
				jobTargetB = targetB.IsValid ? ZombieRuntimeActions.DescribeCell(targetB.Cell) : null,
				jobTargetC = targetC.IsValid ? ZombieRuntimeActions.DescribeCell(targetC.Cell) : null
			};
		}

		[Tool("zombieland/symbiant_severance_contract", Description = "Verify severance surgery visibility, zombie-extract ingredients, industrial-or-better medicine, extract consumption, and bond removal.")]
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

		[Tool("zombieland/symbiant_benefit_contract", Description = "Verify host display-hediff repair, low/high benefit scaling, zombie targeting threshold, stackable Moving/Manipulation capacities, and difficulty-scaled skill bonuses.")]
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
			object deduplication = null;
			object repair = null;
			object high = null;
			object capacities = null;
			object skill = null;
			object benefitLetter = null;
			object autoHeal = null;
			object immediateInfectionImmunity = null;
			object forcedBenefits = null;
			object stackedBenefits = null;
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
				deduplication = VerifySymbiantHediffDeduplication(symbiant, host);

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
				forcedBenefits = EnsureSymbiantHostBenefitsForProbe(symbiant, "ZombieIgnore", "SkillBonus", "MoveSpeed", "Manipulation", "AutoHeal");
				var stackedMoveSpeed = EnsureSymbiantHostBenefitCountForProbe(symbiant, "MoveSpeed", 2);
				var stackedManipulation = EnsureSymbiantHostBenefitCountForProbe(symbiant, "Manipulation", 2);
				var stackedSkill = EnsureSymbiantHostBenefitCountForProbe(symbiant, "SkillBonus", 2);
				stackedBenefits = new
				{
					success = ScenarioSucceeded(stackedMoveSpeed)
						&& ScenarioSucceeded(stackedManipulation)
						&& ScenarioSucceeded(stackedSkill),
					moveSpeed = stackedMoveSpeed,
					manipulation = stackedManipulation,
					skill = stackedSkill
				};
				RepairHostLink(symbiant);
				high = DescribeSymbiantBenefitCheck(symbiant, host);
				capacities = VerifySymbiantCapacityBenefits(host);
				skill = DescribeSymbiantSkillBonus(host);
				autoHeal = VerifySymbiantAutoHealKeepsContamination(symbiant, host);
				immediateInfectionImmunity = VerifySymbiantImmediateInfectionImmunity(host);
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
				&& ScenarioSucceeded(deduplication)
				&& ScenarioSucceeded(repair)
					&& addedCells > 0
					&& BenefitCheckFactor(high) >= 0.5f
					&& BenefitCheckHasZombieProtection(high)
					&& ScenarioSucceeded(benefitLetter)
					&& ScenarioSucceeded(forcedBenefits)
					&& ScenarioSucceeded(stackedBenefits)
					&& ScenarioSucceeded(capacities)
					&& ScenarioSucceeded(skill)
					&& ScenarioSucceeded(autoHeal)
					&& ScenarioSucceeded(immediateInfectionImmunity)
					&& (activeAfterCleanup == null || cleanup == false);

			return new
			{
				success,
				sourcePath = "ZombieSymbiant.EnsureHostLink/EnsureHostHediff -> Hediff_SymbiantSymbiosis.CurStage -> Moving/Manipulation capacities -> ApplySymbiantSkillBonus",
				error,
				fixtureSetup,
				initial,
				deduplication,
				repair,
				addedCells,
				high,
				capacities,
				benefitLetter,
				forcedBenefits,
				stackedBenefits,
				skill,
				autoHeal,
				immediateInfectionImmunity,
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

		static object VerifySymbiantHediffDeduplication(ZombieSymbiant symbiant, Pawn host)
		{
			if (symbiant == null || host?.health?.hediffSet == null || CustomDefs.SymbiantSymbiosis == null)
				return new { success = false, error = "Symbiant host display state is unavailable." };
			var duplicate = HediffMaker.MakeHediff(CustomDefs.SymbiantSymbiosis, host) as Hediff_SymbiantSymbiosis;
			if (duplicate == null)
				return new { success = false, error = "Could not create a duplicate Symbiant display hediff." };
			duplicate.symbiantThingId = "stale-test-link";
			host.health.hediffSet.hediffs.Add(duplicate);
			var countBefore = host.health.hediffSet.hediffs.Count(hediff => hediff.def == CustomDefs.SymbiantSymbiosis);
			RepairHostLink(symbiant);
			var remaining = host.health.hediffSet.hediffs
				.Where(hediff => hediff.def == CustomDefs.SymbiantSymbiosis)
				.OfType<Hediff_SymbiantSymbiosis>()
				.ToArray();
			return new
			{
				success = countBefore == 2
					&& remaining.Length == 1
					&& remaining[0].symbiantThingId == symbiant.ThingID,
				countBefore,
				countAfter = remaining.Length,
				remainingThingIds = remaining.Select(hediff => hediff.symbiantThingId).ToArray()
			};
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

		static object VerifySymbiantCapacityBenefits(Pawn host)
		{
			if (host?.health?.capacities == null)
				return new { success = false, error = "Linked host has no capacity tracker." };
			var movingBenefitCount = ZombieSymbiant.MoveSpeedBenefitCount(host);
			var manipulationBenefitCount = ZombieSymbiant.ManipulationBenefitCount(host);
			var hostHediff = host.health.hediffSet.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) as Hediff_SymbiantSymbiosis;
			var combinedHeading = "SymbiantCombinedCapacityEffects".Translate().Resolve();
			var previousProfile = ZombieSymbiant.DebugPerfProfile;
			try
			{
				_ = ZombieSymbiant.SetDebugPerfProfile("noTick");
				ZombieSymbiant.NotifyHostCapacityBenefitsChanged(host);
				var baselineMoving = host.health.capacities.GetLevel(PawnCapacityDefOf.Moving);
				var baselineManipulation = host.health.capacities.GetLevel(PawnCapacityDefOf.Manipulation);
				var baselineMoveSpeed = StatDefOf.MoveSpeed.Worker.GetValue(StatRequest.For(host), true);
				var baselineTicksPerMove = host.TicksPerMoveCardinal;
				var baselineHostHediffDescription = hostHediff?.Description;
				var baselineHostHediffTipStringExtra = hostHediff?.TipStringExtra;
				var baselineHostHediffTooltip = hostHediff?.GetTooltip(host, false);

				_ = ZombieSymbiant.SetDebugPerfProfile(previousProfile);
				ZombieSymbiant.NotifyHostCapacityBenefitsChanged(host);
				var moving = host.health.capacities.GetLevel(PawnCapacityDefOf.Moving);
				var manipulation = host.health.capacities.GetLevel(PawnCapacityDefOf.Manipulation);
				var statWorkerMoveSpeed = StatDefOf.MoveSpeed.Worker.GetValue(StatRequest.For(host), true);
				var extensionMoveSpeed = host.GetStatValue(StatDefOf.MoveSpeed);
				var ticksPerMove = host.TicksPerMoveCardinal;
				var hostHediffDescription = hostHediff?.Description;
				var hostHediffTipStringExtra = hostHediff?.TipStringExtra;
				var hostHediffTooltip = hostHediff?.GetTooltip(host, false);
				var expectedMovingFactor = 1f + movingBenefitCount * 0.25f;
				var expectedManipulationFactor = 1f + manipulationBenefitCount * 0.25f;
				var movingFactor = moving / Mathf.Max(0.001f, baselineMoving);
				var manipulationFactor = manipulation / Mathf.Max(0.001f, baselineManipulation);
				var moveSpeedFactor = statWorkerMoveSpeed / Mathf.Max(0.001f, baselineMoveSpeed);
				var explanation = StatDefOf.MoveSpeed.Worker.GetExplanationFull(
					StatRequest.For(host),
					ToStringNumberSense.Absolute,
					statWorkerMoveSpeed);
				return new
				{
					success = movingBenefitCount >= 2
						&& manipulationBenefitCount >= 2
						&& Mathf.Abs(movingFactor - expectedMovingFactor) <= 0.02f
						&& Mathf.Abs(manipulationFactor - expectedManipulationFactor) <= 0.02f
						&& Mathf.Approximately(moveSpeedFactor, movingFactor)
						&& Mathf.Approximately(extensionMoveSpeed, statWorkerMoveSpeed)
						&& ticksPerMove < baselineTicksPerMove
						&& baselineHostHediffDescription?.Contains(combinedHeading) == false
						&& hostHediffDescription?.Contains(combinedHeading) == false
						&& baselineHostHediffTipStringExtra?.Contains(combinedHeading) == false
						&& baselineHostHediffTooltip?.Contains(combinedHeading) == false
						&& hostHediffTipStringExtra?.StartsWith(combinedHeading + "\n  - ", StringComparison.Ordinal) == true
						&& hostHediffTooltip?.Contains(combinedHeading + "\n  - ") == true
						&& hostHediffTooltip?.Contains(combinedHeading + "\n\n") == false,
					movingBenefitCount,
					manipulationBenefitCount,
					expectedMovingFactor,
					expectedManipulationFactor,
					movingFactor,
					manipulationFactor,
					moveSpeedFactor,
					baselineMoving,
					moving,
					baselineManipulation,
					manipulation,
					baselineMoveSpeed,
					statWorkerMoveSpeed,
					extensionMoveSpeed,
					baselineTicksPerMove,
					ticksPerMove,
					combinedHeading,
					baselineHostHediffDescription,
					baselineHostHediffTipStringExtra,
					baselineHostHediffTooltip,
					hostHediffDescription,
					hostHediffTipStringExtra,
					hostHediffTooltip,
					explanation
				};
			}
			finally
			{
				if (ZombieSymbiant.DebugPerfProfile != previousProfile)
					_ = ZombieSymbiant.SetDebugPerfProfile(previousProfile);
				ZombieSymbiant.NotifyHostCapacityBenefitsChanged(host);
			}
		}

		static object DescribeSymbiantSkillBonus(Pawn host)
		{
			var skill = host?.skills?.GetSkill(SkillDefOf.Construction);
			if (skill == null)
				return new { success = false, error = "Linked host has no Construction skill record." };
			var drawSkillMethod = AccessTools.Method(
				typeof(SkillUI),
				nameof(SkillUI.DrawSkill),
				new Type[] { typeof(SkillRecord), typeof(Rect), typeof(SkillUI.SkillDrawMode), typeof(string) });
			var descriptionMethod = AccessTools.Method(typeof(SkillUI), "GetSkillDescription", new Type[] { typeof(SkillRecord) });
			var drawSkillPatchInfo = drawSkillMethod == null ? null : Harmony.GetPatchInfo(drawSkillMethod);
			var descriptionPatchInfo = descriptionMethod == null ? null : Harmony.GetPatchInfo(descriptionMethod);
			var uiPatches = new
			{
				success = drawSkillPatchInfo?.Transpilers?.Any(patch => patch.owner == "net.pardeike.zombieland") == true
					&& descriptionPatchInfo?.Postfixes?.Any(patch => patch.owner == "net.pardeike.zombieland") == true,
				drawSkillFound = drawSkillMethod != null,
				descriptionFound = descriptionMethod != null,
				drawSkillOwners = PatchOwners(drawSkillMethod),
				descriptionOwners = PatchOwners(descriptionMethod)
			};
			var previousProfile = ZombieSymbiant.DebugPerfProfile;
			var previousDifficulty = ZombieSettings.Values.threatScale;
			try
			{
				_ = ZombieSymbiant.SetDebugPerfProfile("noTick");
				skill.Level = 5;
				var raw = skill.Level;
				var dormantLabel = ZombieSymbiant.FormatSymbiantSkillLevel(skill.GetLevelForUI(), skill);
				var dormantTooltip = ZombieSymbiant.SymbiantSkillBonusTooltipLine(skill);
				_ = ZombieSymbiant.SetDebugPerfProfile(previousProfile);
				var bonusStacks = ZombieSymbiant.SkillBonusBenefitCount(host);
				var cases = new[]
				{
					new { difficulty = 1f, expectedPerStack = 4 },
					new { difficulty = 1.99f, expectedPerStack = 4 },
					new { difficulty = 2f, expectedPerStack = 3 },
					new { difficulty = 2.99f, expectedPerStack = 3 },
					new { difficulty = 3f, expectedPerStack = 2 },
					new { difficulty = 3.99f, expectedPerStack = 2 },
					new { difficulty = 4f, expectedPerStack = 1 },
					new { difficulty = 5f, expectedPerStack = 1 }
				};
				var rows = cases.Select(item =>
				{
					ZombieSettings.Values.threatScale = item.difficulty;
					var reportedPerStack = ZombieSymbiant.SkillBonusPerBenefit();
					var nominalBonus = bonusStacks * item.expectedPerStack;
					var expected = Mathf.Clamp(raw + nominalBonus, 0, SkillRecord.MaxLevel);
					var expectedAppliedBonus = expected - raw;
					var expectedLabel = expectedAppliedBonus > 0 ? $"{raw} + {expectedAppliedBonus}" : expected.ToString();
					var patched = skill.Level;
					var patchedForUi = skill.GetLevelForUI();
					var label = ZombieSymbiant.FormatSymbiantSkillLevel(patched, skill);
					var tooltip = ZombieSymbiant.SymbiantSkillBonusTooltipLine(skill);
					return new
					{
						success = reportedPerStack == item.expectedPerStack
							&& patched == expected
							&& patchedForUi == expected
							&& label == expectedLabel
							&& tooltip.NullOrEmpty() == false
							&& tooltip.Contains($"+{expectedAppliedBonus}"),
						item.difficulty,
						item.expectedPerStack,
						reportedPerStack,
						nominalBonus,
						expectedAppliedBonus,
						patched,
						patchedForUi,
						expected,
						label,
						expectedLabel,
						tooltip
					};
				}).ToArray();

				ZombieSettings.Values.threatScale = 1f;
				var nominalAtLowDifficulty = bonusStacks * 4;
				skill.Level = 18;
				var partialCapEffective = skill.GetLevelForUI();
				var partialCapLabel = ZombieSymbiant.FormatSymbiantSkillLevel(partialCapEffective, skill);
				var partialCapTooltip = ZombieSymbiant.SymbiantSkillBonusTooltipLine(skill);
				var partialCap = new
				{
					success = partialCapEffective == SkillRecord.MaxLevel
						&& partialCapLabel == "18 + 2"
						&& partialCapTooltip?.Contains("+2") == true
						&& partialCapTooltip.Contains($"+{nominalAtLowDifficulty}"),
					baseLevel = 18,
					nominalBonus = nominalAtLowDifficulty,
					effective = partialCapEffective,
					label = partialCapLabel,
					tooltip = partialCapTooltip
				};
				skill.Level = SkillRecord.MaxLevel;
				var fullCapEffective = skill.GetLevelForUI();
				var fullCapLabel = ZombieSymbiant.FormatSymbiantSkillLevel(fullCapEffective, skill);
				var fullCapTooltip = ZombieSymbiant.SymbiantSkillBonusTooltipLine(skill);
				var fullCap = new
				{
					success = fullCapEffective == SkillRecord.MaxLevel
						&& fullCapLabel == SkillRecord.MaxLevel.ToString()
						&& fullCapTooltip?.Contains("+0") == true
						&& fullCapTooltip.Contains($"+{nominalAtLowDifficulty}"),
					baseLevel = SkillRecord.MaxLevel,
					nominalBonus = nominalAtLowDifficulty,
					effective = fullCapEffective,
					label = fullCapLabel,
					tooltip = fullCapTooltip
				};
				skill.Level = raw;
				return new
				{
					success = raw == 5
						&& dormantLabel == "5"
						&& dormantTooltip.NullOrEmpty()
						&& uiPatches.success
						&& bonusStacks >= 2
						&& rows.All(row => row.success)
						&& partialCap.success
						&& fullCap.success,
					skill = skill.def.defName,
					raw,
					dormantLabel,
					dormantTooltip,
					bonusStacks,
					uiPatches,
					benefitFactor = ZombieSymbiant.SymbiantBenefitFactor(host),
					rows,
					partialCap,
					fullCap
				};
			}
			finally
			{
				ZombieSettings.Values.threatScale = previousDifficulty;
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
			ZombieSymbiant.NotifyHostCapacityBenefitsChanged(symbiant.LinkedHost);
			var configured = (benefitNames ?? Array.Empty<string>())
				.Select(benefitName => new
				{
					name = benefitName,
					count = SymbiantHostBenefitCountForProbe(symbiant, benefitName)
				})
				.ToArray();
			return new
			{
				success = configured.All(item => item.count > 0),
				requested = benefitNames ?? Array.Empty<string>(),
				added = added.ToArray(),
				configured
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

		static object VerifySymbiantImmediateInfectionImmunity(Pawn host)
		{
			var biteDef = HediffDef.Named("ZombieBite");
			if (host?.health?.hediffSet == null || biteDef == null || CustomDefs.ZombieBite == null)
				return new { success = false, error = "Host health or ZombieBite def is unavailable." };
			var part = host.health.hediffSet
				.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Outside)
				.FirstOrDefault(candidate => candidate.def.IsSolid(candidate, host.health.hediffSet.hediffs) == false);
			if (part == null)
				return new { success = false, error = "Host has no non-solid part for the infection-immunity probe." };

			var previousChance = ZombieSettings.Values.zombieBiteInfectionChance;
			Hediff_Injury_ZombieBite bite = null;
			try
			{
				ZombieSettings.Values.zombieBiteInfectionChance = 1f;
				bite = HediffMaker.MakeHediff(biteDef, host, part) as Hediff_Injury_ZombieBite;
				if (bite?.TendDuration?.ZombieInfector == null)
					return new { success = false, error = "Could not create a fully configured ZombieBite." };
				host.health.AddHediff(bite, part, new DamageInfo(CustomDefs.ZombieBite, 2f));
				var infector = bite.TendDuration.ZombieInfector;
				var stateImmediatelyAfterAdd = bite.TendDuration.GetInfectionState();
				return new
				{
					success = ZombieSymbiant.HasZombieInfectionImmunity(host)
						&& stateImmediatelyAfterAdd == InfectionState.BittenHarmless
						&& infector.infectionKnownDelay == 0
						&& infector.infectionStartTime == 0
						&& infector.infectionEndTime == 0,
					stateImmediatelyAfterAdd = stateImmediatelyAfterAdd.ToString(),
					infector.infectionKnownDelay,
					infector.infectionStartTime,
					infector.infectionEndTime
				};
			}
			finally
			{
				ZombieSettings.Values.zombieBiteInfectionChance = previousChance;
				if (bite != null && host.health?.hediffSet?.hediffs?.Contains(bite) == true)
					host.health.RemoveHediff(bite);
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
			var allowsIndustrialMedicine = filter?.Allows(ThingDefOf.MedicineIndustrial) == true;
			var allowsGlitterworldMedicine = filter?.Allows(ThingDefOf.MedicineUltratech) == true;
			var rejectsHerbalMedicine = filter?.Allows(ThingDefOf.MedicineHerbal) == false;
			var ingredientCount = recipe?.ingredients?.Count ?? 0;
			var hasExtractIngredient = recipe?.ingredients?.Any(ingredient => ingredient.filter.Allows(CustomDefs.ZombieExtract)) == true;
			var medicineIngredient = recipe?.ingredients?.FirstOrDefault(ingredient =>
				ingredient.filter.Allows(ThingDefOf.MedicineIndustrial)
				&& ingredient.filter.Allows(ThingDefOf.MedicineUltratech)
				&& ingredient.filter.Allows(ThingDefOf.MedicineHerbal) == false);
			var hasMedicineIngredient = medicineIngredient != null && Mathf.Approximately(medicineIngredient.GetBaseCount(), 1f);
			var extractIngredient = recipe?.ingredients?.FirstOrDefault(ingredient => ingredient.filter.Allows(CustomDefs.ZombieExtract));
			var dynamicExtractCount = extractIngredient == null ? 0f : recipe.Worker.GetIngredientCount(extractIngredient, null);
			var bill = recipe == null ? null : new Bill_Medical(recipe, null);
			var billAllowsIndustrialMedicine = bill?.IsFixedOrAllowedIngredient(ThingDefOf.MedicineIndustrial) == true;
			var billAllowsGlitterworldMedicine = bill?.IsFixedOrAllowedIngredient(ThingDefOf.MedicineUltratech) == true;
			var billRejectsHerbalMedicine = bill?.IsFixedOrAllowedIngredient(ThingDefOf.MedicineHerbal) == false;
			return new
			{
				success = recipe != null
					&& recipe.workerClass == typeof(Recipe_SeverSymbiantSymbiosis)
					&& recipe.targetsBodyPart
					&& ingredientCount == 2
					&& allowsExtract
					&& allowsIndustrialMedicine
					&& allowsGlitterworldMedicine
					&& rejectsHerbalMedicine
					&& hasExtractIngredient
					&& hasMedicineIngredient
					&& billAllowsIndustrialMedicine
					&& billAllowsGlitterworldMedicine
					&& billRejectsHerbalMedicine
					&& Mathf.Approximately(dynamicExtractCount, ZombieSymbiant.SeveranceExtractCost()),
				recipe = recipe?.defName,
				workerClass = recipe?.workerClass?.FullName,
				targetsBodyPart = recipe?.targetsBodyPart ?? false,
				ingredientCount,
				allowsExtract,
				allowsIndustrialMedicine,
				allowsGlitterworldMedicine,
				rejectsHerbalMedicine,
				hasExtractIngredient,
				hasMedicineIngredient,
				billAllowsIndustrialMedicine,
				billAllowsGlitterworldMedicine,
				billRejectsHerbalMedicine,
				dynamicExtractCount,
				currentRequiredExtract = ZombieSymbiant.SeveranceExtractCost()
			};
		}

		[Tool("zombieland/symbiant_host_effect_isolation_contract", Description = "Verify the Symbiant bond marker, ordinary host hediff transitions, effect-only damage packets, Symbiant-local effects, and administrative Symbiant removal cannot drain shared health or kill the linked host.")]
		public static object SymbiantHostEffectIsolationContract(
			[ToolParameter(Description = "Destroy the temporary Symbiant, host, fixture buildings, and added hediffs after capturing evidence.", Required = false, DefaultValue = true)] bool cleanup = true)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded." };
			var activeBefore = ZombieSymbiant.ActiveSymbiant(map);
			if (activeBefore != null)
				return new { success = false, error = "An active Symbiant already exists on the current map.", activeSymbiant = ZombieRuntimeActions.StableThingId(activeBefore) };

			var settingsSnapshot = SnapshotZombieSettings();
			SymbiantNaturalSpawnFixture fixture = null;
			ZombieSymbiant symbiant = null;
			Hediff pregnancyHediff = null;
			Hediff_LaborPushing laborPushingHediff = null;
			Hediff anestheticHediff = null;
			Pawn[] childbirthPawns = Array.Empty<Pawn>();
			Thing[] childbirthFilth = Array.Empty<Thing>();
			HashSet<Pawn> childbirthPawnsBefore = null;
			HashSet<Filth> childbirthFilthBefore = null;
			bool? originalBabiesAreHealthy = null;
			object fixtureSetup = null;
			object markerInvariant = null;
			object pregnancyTransition = null;
			object childbirthCompletion = null;
			object anestheticTransition = null;
			object effectOnlyHostDamage = null;
			object symbiantLocalEffect = null;
			object administrativeRemoval = null;
			object childbirthCleanup = null;
			object error = null;

			try
			{
				ApplyZombieSettingsOverride(settings => settings.showZombieEventLetters = false);
				if (TrySetupSymbiantNaturalSpawnFixture(map, out fixture, out var fixtureError) == false)
					throw new InvalidOperationException($"Could not create the Symbiant host-effect fixture: {fixtureError}");
				fixtureSetup = DescribeSymbiantNaturalSpawnFixture(fixture);
				symbiant = SpawnAssignedSymbiantForSeveranceContract(map, fixture);
				var host = fixture.host;
				var marker = host.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) as Hediff_SymbiantSymbiosis;
				if (marker == null)
					throw new InvalidOperationException("The temporary Symbiant host has no bond marker.");

				var sharedHealthBefore = symbiant.SharedHealthCurrentDisplay;
				marker.Severity = float.MaxValue;
				host.health.CheckForStateChange(null, marker);
				markerInvariant = new
				{
					def = marker.def?.defName,
					initialSeverity = marker.def?.initialSeverity ?? -1f,
					maxSeverity = marker.def?.maxSeverity ?? -1f,
					lethalSeverity = marker.def?.lethalSeverity ?? -1f,
					severityAfterHostileAssignment = marker.Severity,
					causeDeathNow = marker.CauseDeathNow(),
					summaryHealthPercentImpact = marker.SummaryHealthPercentImpact,
					bleedRate = marker.BleedRate,
					painOffset = marker.PainOffset,
					tendable = marker.TendableNow(false),
					hostDead = host.Dead,
					symbiantDestroyed = symbiant.Destroyed,
					sharedHealthBefore,
					sharedHealthAfter = symbiant.SharedHealthCurrentDisplay,
					success = marker.def != null
						&& marker.def.initialSeverity > 0f
						&& marker.def.maxSeverity <= 0.001f
						&& marker.def.lethalSeverity < 0f
						&& marker.Severity <= 0.001f
						&& marker.CauseDeathNow() == false
						&& Mathf.Approximately(marker.SummaryHealthPercentImpact, 0f)
						&& Mathf.Approximately(marker.BleedRate, 0f)
						&& Mathf.Approximately(marker.PainOffset, 0f)
						&& marker.TendableNow(false) == false
						&& host.Dead == false
						&& symbiant.Destroyed == false
						&& symbiant.SharedHealthCurrentDisplay == sharedHealthBefore
				};

				if (ModsConfig.BiotechActive == false)
				{
					pregnancyTransition = new { success = true, skipped = true, reason = "Biotech is inactive." };
				}
				else
				{
					pregnancyHediff = HediffMaker.MakeHediff(HediffDefOf.PregnantHuman, host);
					var pregnancySharedHealthBefore = symbiant.SharedHealthCurrentDisplay;
					host.health.AddHediff(pregnancyHediff);
					pregnancyHediff.PostDebugAdd();
					var pregnancyPresent = host.health.hediffSet.hediffs.Contains(pregnancyHediff);
					pregnancyTransition = new
					{
						def = pregnancyHediff.def?.defName,
						pregnancyPresent,
						hostDead = host.Dead,
						hostDowned = host.Downed,
						symbiantDestroyed = symbiant.Destroyed,
						bondActive = symbiant.IsActiveBondWith(host),
						linkedAfter = ZombieRuntimeActions.StableThingId(ZombieSymbiant.LinkedSymbiantFor(host)),
						sharedHealthBefore = pregnancySharedHealthBefore,
						sharedHealthAfter = symbiant.SharedHealthCurrentDisplay,
						success = pregnancyPresent
							&& host.Dead == false
							&& symbiant.Destroyed == false
							&& symbiant.IsActiveBondWith(host)
							&& ZombieSymbiant.LinkedSymbiantFor(host) == symbiant
							&& symbiant.SharedHealthCurrentDisplay == pregnancySharedHealthBefore
					};
					if (host.Dead == false && pregnancyPresent)
						host.health.RemoveHediff(pregnancyHediff);
					pregnancyHediff = null;
				}

				if (ModsConfig.BiotechActive == false)
				{
					childbirthCompletion = new { success = true, skipped = true, reason = "Biotech is inactive." };
				}
				else if (host.Ideo?.GetPrecept(PreceptDefOf.ChildBirth) is not Precept_Ritual childbirthRitual)
				{
					childbirthCompletion = new { success = true, skipped = true, reason = "The temporary host has no childbirth ritual precept." };
				}
				else
				{
					childbirthPawnsBefore = map.mapPawns.AllPawns.ToHashSet();
					childbirthFilthBefore = fixture.fixtureRect.Cells
						.SelectMany(cell => cell.GetThingList(map))
						.OfType<Filth>()
						.ToHashSet();
					var childbirthSharedHealthBefore = symbiant.SharedHealthCurrentDisplay;
					var birthQuality = PregnancyUtility.GetBirthQualityFor(host);
					var configuredMotherDeathChance = PregnancyUtility.ChanceMomDiesDuringBirth(birthQuality);
					var configuredBabiesAreHealthy = Find.Storyteller.difficulty.babiesAreHealthy;
					originalBabiesAreHealthy = configuredBabiesAreHealthy;
					try
					{
						// Exercise the real labor-removal -> ApplyBirthOutcome path while
						// disabling only RimWorld's intentional random maternal-death roll.
						Find.Storyteller.difficulty.babiesAreHealthy = true;
						laborPushingHediff = HediffMaker.MakeHediff(HediffDefOf.PregnancyLaborPushing, host) as Hediff_LaborPushing;
						if (laborPushingHediff == null)
							throw new InvalidOperationException("Could not create the labor-pushing hediff.");
						host.health.AddHediff(laborPushingHediff);
						var bestOutcome = childbirthRitual.outcomeEffect?.def?.BestOutcome;
						laborPushingHediff.ForceBirth(bestOutcome?.positivityIndex ?? 1, true);
						laborPushingHediff = null;
					}
					finally
					{
						Find.Storyteller.difficulty.babiesAreHealthy = originalBabiesAreHealthy.Value;
						originalBabiesAreHealthy = null;
					}

					childbirthPawns = map.mapPawns.AllPawns
						.Where(pawn => childbirthPawnsBefore.Contains(pawn) == false)
						.ToArray();
					childbirthFilth = fixture.fixtureRect.Cells
						.SelectMany(cell => cell.GetThingList(map))
						.OfType<Filth>()
						.Where(filth => childbirthFilthBefore.Contains(filth) == false)
						.Cast<Thing>()
						.Distinct()
						.ToArray();
					var newborn = childbirthPawns.FirstOrDefault();
					var laborRemoved = host.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.PregnancyLaborPushing) == null;
					var postpartumExhaustionPresent = host.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.PostpartumExhaustion) != null;
					var lactatingPresent = host.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.Lactating) != null;
					childbirthCompletion = new
					{
						birthQuality,
						configuredBabiesAreHealthy,
						configuredMotherDeathChance,
						controlledSafeBirth = true,
						laborRemoved,
						postpartumExhaustionPresent,
						lactatingPresent,
						newborn = ZombieRuntimeActions.StableThingId(newborn),
						newbornCount = childbirthPawns.Length,
						hostDead = host.Dead,
						hostDowned = host.Downed,
						symbiantDestroyed = symbiant.Destroyed,
						bondActive = symbiant.IsActiveBondWith(host),
						linkedAfter = ZombieRuntimeActions.StableThingId(ZombieSymbiant.LinkedSymbiantFor(host)),
						sharedHealthBefore = childbirthSharedHealthBefore,
						sharedHealthAfter = symbiant.SharedHealthCurrentDisplay,
						success = newborn != null
							&& laborRemoved
							&& host.Dead == false
							&& symbiant.Destroyed == false
							&& symbiant.IsActiveBondWith(host)
							&& ZombieSymbiant.LinkedSymbiantFor(host) == symbiant
							&& symbiant.SharedHealthCurrentDisplay == childbirthSharedHealthBefore
							&& postpartumExhaustionPresent
							&& lactatingPresent
					};
				}

				var anestheticSharedHealthBefore = symbiant.SharedHealthCurrentDisplay;
				anestheticHediff = HediffMaker.MakeHediff(HediffDefOf.Anesthetic, host);
				host.health.AddHediff(anestheticHediff);
				var anestheticPresent = host.health.hediffSet.hediffs.Contains(anestheticHediff);
				anestheticTransition = new
				{
					def = anestheticHediff.def?.defName,
					anestheticPresent,
					hostDead = host.Dead,
					hostDowned = host.Downed,
					symbiantDestroyed = symbiant.Destroyed,
					bondActive = symbiant.IsActiveBondWith(host),
					linkedAfter = ZombieRuntimeActions.StableThingId(ZombieSymbiant.LinkedSymbiantFor(host)),
					sharedHealthBefore = anestheticSharedHealthBefore,
					sharedHealthAfter = symbiant.SharedHealthCurrentDisplay,
					success = anestheticPresent
						&& host.Dead == false
						&& symbiant.Destroyed == false
						&& symbiant.IsActiveBondWith(host)
						&& ZombieSymbiant.LinkedSymbiantFor(host) == symbiant
						&& symbiant.SharedHealthCurrentDisplay == anestheticSharedHealthBefore
				};
				if (host.Dead == false && anestheticPresent)
					host.health.RemoveHediff(anestheticHediff);
				anestheticHediff = null;

				var originalBluntHarmsHealth = DamageDefOf.Blunt.harmsHealth;
				bool bluntSharesHealthWithFalseFlag;
				try
				{
					DamageDefOf.Blunt.harmsHealth = false;
					bluntSharesHealthWithFalseFlag = ZombieSymbiant.IsSharedHealthDamage(new DamageInfo(DamageDefOf.Blunt, 1f));
				}
				finally
				{
					DamageDefOf.Blunt.harmsHealth = originalBluntHarmsHealth;
				}
				var seismicDamage = symbiant.SharedHealthCurrentDisplay + 250f;
				var seismicInfo = new DamageInfo(CustomDefs.SeismicWave, seismicDamage, 0f, -1f, null);
				var seismicSharesHealth = ZombieSymbiant.IsSharedHealthDamage(seismicInfo);
				var seismicSharedHealthBefore = symbiant.SharedHealthCurrentDisplay;
				var seismicHostInjuryBefore = TotalInjurySeverity(host);
				var seismicResult = host.TakeDamage(seismicInfo);
				effectOnlyHostDamage = new
				{
					damageDef = CustomDefs.SeismicWave?.defName,
					harmsHealth = CustomDefs.SeismicWave?.harmsHealth ?? false,
					workerClass = CustomDefs.SeismicWave?.workerClass?.FullName,
					bluntWorkerClass = DamageDefOf.Blunt.workerClass?.FullName,
					bluntSharesHealthWithFalseFlag,
					seismicDamage,
					sharesHealth = seismicSharesHealth,
					damageDealt = seismicResult.totalDamageDealt,
					hostInjuryBefore = seismicHostInjuryBefore,
					hostInjuryAfter = TotalInjurySeverity(host),
					sharedHealthBefore = seismicSharedHealthBefore,
					sharedHealthAfter = symbiant.SharedHealthCurrentDisplay,
					hostDead = host.Dead,
					symbiantDestroyed = symbiant.Destroyed,
					success = CustomDefs.SeismicWave != null
						&& CustomDefs.SeismicWave.harmsHealth
						&& bluntSharesHealthWithFalseFlag
						&& seismicSharesHealth == false
						&& Mathf.Approximately(seismicResult.totalDamageDealt, 0f)
						&& Mathf.Approximately(TotalInjurySeverity(host), seismicHostInjuryBefore)
						&& symbiant.SharedHealthCurrentDisplay == seismicSharedHealthBefore
						&& host.Dead == false
						&& symbiant.Destroyed == false
				};

				var symbiantEffectSharedHealthBefore = symbiant.SharedHealthCurrentDisplay;
				var symbiantEffectHostInjuryBefore = TotalInjurySeverity(host);
				var symbiantEffectResult = symbiant.TakeDamage(new DamageInfo(DamageDefOf.Stun, 5f, 999f, -1f, null));
				symbiantLocalEffect = new
				{
					damageDef = DamageDefOf.Stun.defName,
					damageDealt = symbiantEffectResult.totalDamageDealt,
					symbiantStunned = symbiant.stances?.stunner?.Stunned == true,
					hostInjuryBefore = symbiantEffectHostInjuryBefore,
					hostInjuryAfter = TotalInjurySeverity(host),
					sharedHealthBefore = symbiantEffectSharedHealthBefore,
					sharedHealthAfter = symbiant.SharedHealthCurrentDisplay,
					hostDead = host.Dead,
					symbiantDestroyed = symbiant.Destroyed,
					success = Mathf.Approximately(symbiantEffectResult.totalDamageDealt, 0f)
						&& symbiant.stances?.stunner?.Stunned == true
						&& Mathf.Approximately(TotalInjurySeverity(host), symbiantEffectHostInjuryBefore)
						&& symbiant.SharedHealthCurrentDisplay == symbiantEffectSharedHealthBefore
						&& host.Dead == false
						&& symbiant.Destroyed == false
				};

				var removalHostInjuryBefore = TotalInjurySeverity(host);
				var removalMarkerBefore = host.health.hediffSet.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) != null;
				symbiant.Destroy(DestroyMode.Vanish);
				administrativeRemoval = new
				{
					markerBefore = removalMarkerBefore,
					markerAfter = host.health.hediffSet.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) != null,
					hostInjuryBefore = removalHostInjuryBefore,
					hostInjuryAfter = TotalInjurySeverity(host),
					hostDead = host.Dead,
					symbiantDestroyed = symbiant.Destroyed,
					symbiantDiscarded = symbiant.Discarded,
					linkedAfter = ZombieRuntimeActions.StableThingId(ZombieSymbiant.LinkedSymbiantFor(host)),
					success = removalMarkerBefore
						&& host.health.hediffSet.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) == null
						&& Mathf.Approximately(TotalInjurySeverity(host), removalHostInjuryBefore)
						&& host.Dead == false
						&& symbiant.Destroyed
						&& symbiant.Discarded
						&& ZombieSymbiant.LinkedSymbiantFor(host) == null
				};
			}
			catch (Exception ex)
			{
				error = ex.ToString();
			}
			finally
			{
				if (originalBabiesAreHealthy.HasValue)
					Find.Storyteller.difficulty.babiesAreHealthy = originalBabiesAreHealthy.Value;
				if (fixture?.host?.Dead == false)
				{
					if (pregnancyHediff != null && fixture.host.health?.hediffSet?.hediffs?.Contains(pregnancyHediff) == true)
						fixture.host.health.RemoveHediff(pregnancyHediff);
					if (anestheticHediff != null && fixture.host.health?.hediffSet?.hediffs?.Contains(anestheticHediff) == true)
						fixture.host.health.RemoveHediff(anestheticHediff);
				}
				RestoreZombieSettings(settingsSnapshot);
			}

			if (childbirthPawnsBefore != null)
			{
				childbirthPawns = map.mapPawns.AllPawns
					.Where(pawn => childbirthPawnsBefore.Contains(pawn) == false)
					.ToArray();
			}
			if (childbirthFilthBefore != null)
			{
				childbirthFilth = fixture.fixtureRect.Cells
					.SelectMany(cell => cell.GetThingList(map))
					.OfType<Filth>()
					.Where(filth => childbirthFilthBefore.Contains(filth) == false)
					.Cast<Thing>()
					.Distinct()
					.ToArray();
			}

			if (cleanup)
			{
				var pawnCleanup = childbirthPawns.Select(DestroyAndDiscardTemporaryPawn).ToArray();
				var removedFilth = 0;
				foreach (var filth in childbirthFilth)
				{
					if (filth?.Destroyed != false)
						continue;
					filth.Destroy(DestroyMode.Vanish);
					removedFilth++;
				}
				childbirthCleanup = new
				{
					success = pawnCleanup.All(ScenarioSucceeded) && childbirthFilth.All(filth => filth == null || filth.Destroyed),
					pawns = pawnCleanup,
					removedFilth
				};
			}
			else
			{
				childbirthCleanup = new { success = true, skipped = true, newbornCount = childbirthPawns.Length, filthCount = childbirthFilth.Length };
			}
			var symbiantCleanup = CleanupTemporarySymbiant(map, symbiant, cleanup);
			var fixtureCleanup = CleanupSymbiantNaturalSpawnFixture(map, fixture, cleanup);
			return new
			{
				success = error == null
					&& ScenarioSucceeded(fixtureSetup)
					&& ScenarioSucceeded(markerInvariant)
					&& ScenarioSucceeded(pregnancyTransition)
					&& ScenarioSucceeded(childbirthCompletion)
					&& ScenarioSucceeded(anestheticTransition)
					&& ScenarioSucceeded(effectOnlyHostDamage)
					&& ScenarioSucceeded(symbiantLocalEffect)
					&& ScenarioSucceeded(administrativeRemoval)
					&& ScenarioSucceeded(childbirthCleanup)
					&& (ZombieSymbiant.ActiveSymbiant(map) == null || cleanup == false),
				sourcePath = "Hediff_SymbiantSymbiosis inert marker -> ordinary AddHediff and labor-removal/ApplyBirthOutcome transitions stay host-local -> injury-worker-only host sharing -> post-worker Symbiant accounting -> explicit bond termination",
				error,
				fixtureSetup,
				markerInvariant,
				pregnancyTransition,
				childbirthCompletion,
				anestheticTransition,
				effectOnlyHostDamage,
				symbiantLocalEffect,
				administrativeRemoval,
				cleanup = new
				{
					childbirth = childbirthCleanup,
					symbiant = symbiantCleanup,
					fixture = fixtureCleanup,
					activeSymbiantAfter = ZombieRuntimeActions.StableThingId(ZombieSymbiant.ActiveSymbiant(map))
				}
			};
		}

		[Tool("zombieland/symbiant_unsafe_damage_contract", Description = "Verify shared-health damage, host effect damage semantics, cross-map isolation, explicit pool-exhaustion lethality, safe Symbiant removal, Pawn.Kill corpse integrity, non-relocation despawn cleanup, map-removal patch ownership, direct map deinitialization, real gravship abandonment, and host-death retreat.")]
		public static object SymbiantUnsafeDamageContract(
			[ToolParameter(Description = "Destroy temporary symbiants, colonists, fixture buildings, and letters after capturing evidence. The destructive gravship and direct-deinitialization subscenarios always remove their temporary remote hosts, map parents, and launch markers so they cannot contaminate the current map.", Required = false, DefaultValue = true)] bool cleanup = true)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded." };
			var activeBefore = ZombieSymbiant.ActiveSymbiant(map);
			if (activeBefore != null)
				return new { success = false, error = "An active symbiant already exists on the current map.", activeSymbiant = ZombieRuntimeActions.StableThingId(activeBefore) };

			var settingsSnapshot = SnapshotZombieSettings();
			var beforeLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>()).ToHashSet();
			var worldSymbiantsBefore = (Find.WorldPawns?.AllPawnsAliveOrDead ?? new List<Pawn>())
				.OfType<ZombieSymbiant>()
				.ToHashSet();
			object symbiantDamageCreatesEcho = null;
			object hostDamageSharesToSymbiant = null;
			object hostEffectDamageSemantics = null;
			object symbiantEffectPipeline = null;
			object sharedHealthRecovery = null;
			object damageEchoCap = null;
			object symbiantDestructionKillsHost = null;
			object sizeHealthScaling = null;
			object thumperDamageNoEffect = null;
			object inspectTabsHidden = null;
			object uncontrolledDestroySafelySeversHost = null;
			object hostDeathStartsRetreat = null;
			object destroyedHostStartsRetreat = null;
			object pawnKillCreatesValidCorpse = null;
			object deadLinkedCorpseHostDeathCleanup = null;
			object nonRelocationDespawnSelfDestructs = null;
			object ordinaryMapCloseMetalHellNoOp = null;
			object legacyWorldPawnMigration = null;
			object secondMapSetup = null;
			object crossMapDamageIsolation = null;
			object crossMapPoolExhaustionSparesHost = null;
			object crossMapUncontrolledDestroySparesHost = null;
			object crossMapHostDeathStartsRetreat = null;
			object gravshipAbandonMap = null;
			object directDeinitMapSetup = null;
			object directDeinitMap = null;
			SymbiantSecondMapFixture secondMapFixture = null;
			SymbiantSecondMapFixture directDeinitMapFixture = null;
			object error = null;

			try
			{
				ApplyZombieSettingsOverride(settings =>
				{
					settings.showZombieEventLetters = false;
					settings.symbiantMaxCells = Math.Max(settings.symbiantMaxCells, 400);
				});
				symbiantDamageCreatesEcho = RunSymbiantUnsafeDamageScenario(map, "symbiantDamageCreatesEcho", cleanup);
				hostDamageSharesToSymbiant = RunSymbiantUnsafeDamageScenario(map, "hostDamageSharesToSymbiant", cleanup);
				hostEffectDamageSemantics = RunSymbiantUnsafeDamageScenario(map, "hostEffectDamageSemantics", cleanup);
				symbiantEffectPipeline = RunSymbiantUnsafeDamageScenario(map, "symbiantEffectPipeline", cleanup);
				sharedHealthRecovery = RunSymbiantUnsafeDamageScenario(map, "sharedHealthRecovery", cleanup);
				damageEchoCap = RunSymbiantUnsafeDamageScenario(map, "damageEchoCap", cleanup);
				symbiantDestructionKillsHost = RunSymbiantUnsafeDamageScenario(map, "symbiantDestructionKillsHost", cleanup);
				sizeHealthScaling = RunSymbiantUnsafeDamageScenario(map, "sizeHealthScaling", cleanup);
				thumperDamageNoEffect = RunSymbiantUnsafeDamageScenario(map, "thumperDamageNoEffect", cleanup);
				inspectTabsHidden = RunSymbiantUnsafeDamageScenario(map, "inspectTabsHidden", cleanup);
				uncontrolledDestroySafelySeversHost = RunSymbiantUnsafeDamageScenario(map, "uncontrolledDestroySafelySeversHost", cleanup);
				hostDeathStartsRetreat = RunSymbiantUnsafeDamageScenario(map, "hostDeathStartsRetreat", cleanup);
				destroyedHostStartsRetreat = RunSymbiantUnsafeDamageScenario(map, "destroyedHostStartsRetreat", cleanup);
				pawnKillCreatesValidCorpse = RunSymbiantPawnKillScenario(map, cleanup);
				deadLinkedCorpseHostDeathCleanup = RunSymbiantDeadLinkedCorpseScenario(map, cleanup);
				nonRelocationDespawnSelfDestructs = RunSymbiantNonRelocationDespawnScenario(map, cleanup);
				ordinaryMapCloseMetalHellNoOp = RunSymbiantOrdinaryMapCloseMetalHellScenario(map, cleanup);
				legacyWorldPawnMigration = RunSymbiantLegacyWorldPawnMigrationScenario(map, cleanup);
				if (TryCreateSymbiantSecondMapFixture(map, out secondMapFixture, out var secondMapError) == false)
				{
					secondMapSetup = secondMapError;
				}
				else
				{
					secondMapSetup = DescribeSymbiantSecondMapFixture(secondMapFixture);
					crossMapDamageIsolation = RunSymbiantUnsafeDamageScenario(map, "crossMapDamageIsolation", cleanup, secondMapFixture.map);
					crossMapPoolExhaustionSparesHost = RunSymbiantUnsafeDamageScenario(map, "crossMapPoolExhaustionSparesHost", cleanup, secondMapFixture.map);
					crossMapUncontrolledDestroySparesHost = RunSymbiantUnsafeDamageScenario(map, "crossMapUncontrolledDestroySparesHost", cleanup, secondMapFixture.map);
					crossMapHostDeathStartsRetreat = RunSymbiantUnsafeDamageScenario(map, "crossMapHostDeathStartsRetreat", cleanup, secondMapFixture.map);
					gravshipAbandonMap = RunSymbiantGravshipAbandonScenario(map, secondMapFixture, cleanup);
				}
				if (TryCreateSymbiantSecondMapFixture(map, out directDeinitMapFixture, out var directDeinitMapError) == false)
				{
					directDeinitMapSetup = directDeinitMapError;
				}
				else
				{
					directDeinitMapSetup = DescribeSymbiantSecondMapFixture(directDeinitMapFixture);
					directDeinitMap = RunSymbiantDirectDeinitScenario(map, directDeinitMapFixture);
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
			var letterCleanup = CleanupTemporaryLetters(newLetters, cleanup);
			var secondMapCleanup = CleanupSymbiantSecondMapFixture(secondMapFixture, cleanup);
			var directDeinitMapCleanup = CleanupSymbiantSecondMapFixture(directDeinitMapFixture, true, true);
			var activeAfterCleanup = ZombieSymbiant.ActiveSymbiant(map);
			var newWorldSymbiants = (Find.WorldPawns?.AllPawnsAliveOrDead ?? new List<Pawn>())
				.OfType<ZombieSymbiant>()
				.Where(symbiant => worldSymbiantsBefore.Contains(symbiant) == false)
				.ToArray();
			var newWorldSymbiantIds = newWorldSymbiants.Select(ZombieRuntimeActions.StableThingId).ToArray();
			if (cleanup)
				foreach (var residue in newWorldSymbiants)
					_ = CleanupTemporarySymbiant(map, residue, true);
			var worldSymbiantResidue = new
			{
				success = cleanup == false || newWorldSymbiants.Length == 0,
				count = newWorldSymbiants.Length,
				ids = newWorldSymbiantIds,
				remainingAfterFallbackCleanup = cleanup
					? newWorldSymbiants.Count(symbiant => Find.WorldPawns?.Contains(symbiant) == true || symbiant.Discarded == false)
					: newWorldSymbiants.Length
			};
			var patchTargets = new
			{
				thingTakeDamage = PatchedMethodsForPatchClass("Thing_TakeDamage_Symbiant_Patch"),
				partHealth = PatchedMethodsForPatchClass("HediffSet_GetPartHealth_Symbiant_Patch"),
				shouldBeDead = PatchedMethodsForPatchClass("Pawn_HealthTracker_ShouldBeDead_Symbiant_Patch"),
				shouldBeDowned = PatchedMethodsForPatchClass("Pawn_HealthTracker_ShouldBeDowned_Symbiant_Patch"),
				summaryHealth = PatchedMethodsForPatchClass("SummaryHealthHandler_SummaryHealthPercent_Symbiant_Patch"),
				pawnPreApplyDamage = PatchedMethodsForPatchClass("Pawn_PreApplyDamage_Patch"),
				pawnKill = PatchedMethodsForPatchClass("Pawn_Kill_Patch"),
				gameDeinitAndRemoveMap = PatchedMethodsForPatchClass("Game_DeinitAndRemoveMap_Patch"),
				gravshipAbandonMap = PatchedMethodsForPatchClass("GravshipUtility_AbandonMap_Patch"),
				voidAwakeningCloseMetalHell = PatchedMethodsForPatchClass("VoidAwakeningUtility_CloseMetalHell_Patch"),
				mapParentAbandon = PatchedMethodsForPatchClass("MapParent_Abandon_Patch")
			};
			var patchTargetChecks = new
			{
				success = HasPatchTarget(patchTargets.gameDeinitAndRemoveMap, "Verse.Game", "DeinitAndRemoveMap")
					&& HasPatchTarget(patchTargets.mapParentAbandon, "RimWorld.Planet.MapParent", "Abandon")
					&& HasPatchTarget(patchTargets.mapParentAbandon, "RimWorld.Planet.Settlement", "Abandon")
					&& HasPatchTarget(patchTargets.mapParentAbandon, "RimWorld.Planet.ArchotechSettlement", "Abandon")
					&& (ModsConfig.AnomalyActive == false || HasPatchTarget(patchTargets.voidAwakeningCloseMetalHell, "RimWorld.Utility.VoidAwakeningUtility", "CloseMetalHell"))
					&& (ModsConfig.OdysseyActive == false || HasPatchTarget(patchTargets.gravshipAbandonMap, "RimWorld.GravshipUtility", "AbandonMap")),
				gameDeinitAndRemoveMap = HasPatchTarget(patchTargets.gameDeinitAndRemoveMap, "Verse.Game", "DeinitAndRemoveMap"),
				gravshipUtilityAbandonMap = HasPatchTarget(patchTargets.gravshipAbandonMap, "RimWorld.GravshipUtility", "AbandonMap"),
				voidAwakeningCloseMetalHell = HasPatchTarget(patchTargets.voidAwakeningCloseMetalHell, "RimWorld.Utility.VoidAwakeningUtility", "CloseMetalHell"),
				mapParentAbandon = HasPatchTarget(patchTargets.mapParentAbandon, "RimWorld.Planet.MapParent", "Abandon"),
				settlementAbandon = HasPatchTarget(patchTargets.mapParentAbandon, "RimWorld.Planet.Settlement", "Abandon"),
				archotechSettlementAbandon = HasPatchTarget(patchTargets.mapParentAbandon, "RimWorld.Planet.ArchotechSettlement", "Abandon")
			};
			var success = error == null
				&& patchTargets.pawnPreApplyDamage.Length > 0
				&& patchTargets.pawnKill.Length > 0
				&& ScenarioSucceeded(patchTargetChecks)
				&& patchTargets.thingTakeDamage.Length > 0
				&& patchTargets.partHealth.Length > 0
				&& patchTargets.shouldBeDead.Length > 0
				&& patchTargets.shouldBeDowned.Length > 0
				&& patchTargets.summaryHealth.Length > 0
				&& ScenarioSucceeded(symbiantDamageCreatesEcho)
				&& ScenarioSucceeded(hostDamageSharesToSymbiant)
				&& ScenarioSucceeded(hostEffectDamageSemantics)
				&& ScenarioSucceeded(symbiantEffectPipeline)
				&& ScenarioSucceeded(sharedHealthRecovery)
				&& ScenarioSucceeded(damageEchoCap)
				&& ScenarioSucceeded(symbiantDestructionKillsHost)
				&& ScenarioSucceeded(sizeHealthScaling)
				&& ScenarioSucceeded(thumperDamageNoEffect)
				&& ScenarioSucceeded(inspectTabsHidden)
				&& ScenarioSucceeded(uncontrolledDestroySafelySeversHost)
				&& ScenarioSucceeded(hostDeathStartsRetreat)
				&& ScenarioSucceeded(destroyedHostStartsRetreat)
				&& ScenarioSucceeded(pawnKillCreatesValidCorpse)
				&& ScenarioSucceeded(deadLinkedCorpseHostDeathCleanup)
				&& ScenarioSucceeded(nonRelocationDespawnSelfDestructs)
				&& ScenarioSucceeded(ordinaryMapCloseMetalHellNoOp)
				&& ScenarioSucceeded(legacyWorldPawnMigration)
				&& ScenarioSucceeded(secondMapSetup)
				&& ScenarioSucceeded(crossMapDamageIsolation)
				&& ScenarioSucceeded(crossMapPoolExhaustionSparesHost)
				&& ScenarioSucceeded(crossMapUncontrolledDestroySparesHost)
				&& ScenarioSucceeded(crossMapHostDeathStartsRetreat)
				&& ScenarioSucceeded(gravshipAbandonMap)
				&& ScenarioSucceeded(directDeinitMapSetup)
				&& ScenarioSucceeded(directDeinitMap)
				&& ScenarioSucceeded(secondMapCleanup)
				&& ScenarioSucceeded(directDeinitMapCleanup)
				&& ScenarioSucceeded(worldSymbiantResidue)
				&& (activeAfterCleanup == null || cleanup == false);

			return new
			{
				success,
				sourcePath = "Thing.TakeDamage -> real DamageWorker pipeline -> post-worker Symbiant accounting; Pawn.PreApplyDamage -> explicit injury-worker host sharing; inert host records; explicit shared-pool failure versus safe removal lifecycle",
				error,
				patchTargets,
				patchTargetChecks,
				symbiantDamageCreatesEcho,
				hostDamageSharesToSymbiant,
				hostEffectDamageSemantics,
				symbiantEffectPipeline,
				sharedHealthRecovery,
				damageEchoCap,
				symbiantDestructionKillsHost,
				sizeHealthScaling,
				thumperDamageNoEffect,
				inspectTabsHidden,
				uncontrolledDestroySafelySeversHost,
				hostDeathStartsRetreat,
				destroyedHostStartsRetreat,
				pawnKillCreatesValidCorpse,
				deadLinkedCorpseHostDeathCleanup,
				nonRelocationDespawnSelfDestructs,
				ordinaryMapCloseMetalHellNoOp,
				legacyWorldPawnMigration,
				secondMapSetup,
				crossMapDamageIsolation,
				crossMapPoolExhaustionSparesHost,
				crossMapUncontrolledDestroySparesHost,
				crossMapHostDeathStartsRetreat,
				gravshipAbandonMap,
				directDeinitMapSetup,
				directDeinitMap,
				cleanup = new
				{
					letters = letterCleanup,
					secondMap = secondMapCleanup,
					directDeinitMap = directDeinitMapCleanup,
					worldSymbiantResidue,
					activeSymbiantAfterCleanup = ZombieRuntimeActions.StableThingId(activeAfterCleanup)
				}
			};
		}

		static object RunSymbiantOrdinaryMapCloseMetalHellScenario(Map map, bool cleanup)
		{
			SymbiantNaturalSpawnFixture fixture = null;
			ZombieSymbiant symbiant = null;
			object action = null;
			object error = null;
			try
			{
				if (TrySetupSymbiantNaturalSpawnFixture(map, out fixture, out var fixtureError) == false)
					return fixtureError;
				symbiant = SpawnAssignedSymbiantForSeveranceContract(map, fixture);
				var host = fixture.host;
				var hediffBefore = host.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) != null;
				Patches.VoidAwakeningUtility_CloseMetalHell_Patch.Prefix(host);
				var hediffAfter = host.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) != null;
				action = new
				{
					mapId = map.uniqueID,
					isPocketMap = map.IsPocketMap,
					hediffBefore,
					hediffAfter,
					hostAlive = host.Dead == false,
					symbiantSpawned = symbiant.Spawned,
					symbiantDestroyed = symbiant.Destroyed,
					linkedAfter = ZombieRuntimeActions.StableThingId(ZombieSymbiant.LinkedSymbiantFor(host)),
					success = map.IsPocketMap == false
						&& hediffBefore
						&& hediffAfter
						&& host.Dead == false
						&& symbiant.Spawned
						&& symbiant.Destroyed == false
						&& ZombieSymbiant.LinkedSymbiantFor(host) == symbiant
				};
			}
			catch (Exception ex)
			{
				error = ex.ToString();
			}
			finally
			{
				if (cleanup)
				{
					_ = CleanupTemporarySymbiant(map, symbiant, true);
					_ = CleanupSymbiantNaturalSpawnFixture(map, fixture, true);
				}
			}

			return new
			{
				success = error == null && ScenarioSucceeded(action),
				sourcePath = "VoidAwakeningUtility.CloseMetalHell prefix -> pocket-map guard -> ordinary-map no-op",
				error,
				action
			};
		}

		static object RunSymbiantLegacyWorldPawnMigrationScenario(Map map, bool cleanup)
		{
			SymbiantNaturalSpawnFixture fixture = null;
			ZombieSymbiant symbiant = null;
			object action = null;
			object error = null;
			var temporaryDespawnField = AccessTools.Field(typeof(ZombieSymbiant), "temporaryDespawnInProgress");
			try
			{
				if (temporaryDespawnField == null)
					return new { success = false, error = "Could not access the Symbiant temporary-despawn lifecycle flag." };
				if (TrySetupSymbiantNaturalSpawnFixture(map, out fixture, out var fixtureError) == false)
					return fixtureError;
				symbiant = SpawnAssignedSymbiantForSeveranceContract(map, fixture);
				var host = fixture.host;
				var hediffBefore = host.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) != null;
				temporaryDespawnField.SetValue(symbiant, true);
				try
				{
					symbiant.DeSpawn(DestroyMode.Vanish);
				}
				finally
				{
					temporaryDespawnField.SetValue(symbiant, false);
				}
				Find.WorldPawns.PassToWorld(symbiant, PawnDiscardDecideMode.KeepForever);
				var stagedInWorldPawns = Find.WorldPawns.Contains(symbiant);
				var migratedCount = ZombieSymbiant.PurgeLegacyWorldPawnSymbiants();
				var hediffAfter = host.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) != null;
				action = new
				{
					stagedInWorldPawns,
					migratedCount,
					hostAlive = host.Dead == false,
					hediffBefore,
					hediffAfter,
					symbiantDestroyed = symbiant.Destroyed,
					symbiantDiscarded = symbiant.Discarded,
					symbiantInWorldPawnsAfter = Find.WorldPawns.Contains(symbiant),
					linkedAfter = ZombieRuntimeActions.StableThingId(ZombieSymbiant.LinkedSymbiantFor(host)),
					success = stagedInWorldPawns
						&& migratedCount >= 1
						&& host.Dead == false
						&& hediffBefore
						&& hediffAfter == false
						&& symbiant.Destroyed
						&& symbiant.Discarded
						&& Find.WorldPawns.Contains(symbiant) == false
						&& ZombieSymbiant.LinkedSymbiantFor(host) == null
				};
			}
			catch (Exception ex)
			{
				error = ex.ToString();
			}
			finally
			{
				if (symbiant != null)
					temporaryDespawnField?.SetValue(symbiant, false);
				if (cleanup)
				{
					_ = CleanupTemporarySymbiant(map, symbiant, true);
					_ = CleanupSymbiantNaturalSpawnFixture(map, fixture, true);
				}
			}

			return new
			{
				success = error == null && ScenarioSucceeded(action),
				sourcePath = "Game.FinalizeInit postfix -> PurgeLegacyWorldPawnSymbiants -> safe sever/destroy/remove/discard",
				error,
				action
			};
		}

		static object RunSymbiantNonRelocationDespawnScenario(Map map, bool cleanup)
		{
			ZombieSymbiant symbiant = null;
			object error = null;
			object action = null;
			try
			{
				if (TryFindClearSpawnCell(map, map.Center, 40f, out var cell, out var cellError) == false)
					return cellError;
				symbiant = ZombieSymbiant.DebugSpawnForRendering(map, cell, [cell]);
				if (symbiant == null)
					return new { success = false, error = "Could not spawn a hostless Symbiant for the non-relocation despawn probe." };

				var worldPawnBefore = Find.WorldPawns?.Contains(symbiant) == true;
				symbiant.DeSpawn(DestroyMode.Vanish);
				var worldPawnAfter = Find.WorldPawns?.Contains(symbiant) == true;
				action = new
				{
					cell = ZombieRuntimeActions.DescribeCell(cell),
					worldPawnBefore,
					worldPawnAfter,
					spawned = symbiant.Spawned,
					dead = symbiant.Dead,
					destroyed = symbiant.Destroyed,
					discarded = symbiant.Discarded,
					activeAfter = ZombieRuntimeActions.StableThingId(ZombieSymbiant.ActiveSymbiant(map)),
					success = worldPawnBefore == false
						&& worldPawnAfter == false
						&& symbiant.Spawned == false
						&& symbiant.Dead == false
						&& symbiant.Destroyed
						&& symbiant.Discarded
						&& ZombieSymbiant.ActiveSymbiant(map) == null
				};
			}
			catch (Exception ex)
			{
				error = ex.ToString();
			}
			finally
			{
				if (cleanup && symbiant != null)
				{
					if (Find.WorldPawns?.Contains(symbiant) == true)
						Find.WorldPawns.RemovePawn(symbiant);
					if (symbiant.Discarded == false)
						symbiant.Discard(true);
				}
			}

			return new
			{
				success = error == null && ScenarioSucceeded(action),
				sourcePath = "ZombieSymbiant.DeSpawn -> DestroyWithoutHostTrauma -> Pawn.Destroy/WorldPawns.RemovePawn/Pawn.Discard",
				error,
				action
			};
		}

		static object RunSymbiantDeadLinkedCorpseScenario(Map map, bool cleanup)
		{
			SymbiantNaturalSpawnFixture fixture = null;
			ZombieSymbiant symbiant = null;
			Corpse symbiantCorpse = null;
			object action = null;
			object error = null;
			var safeSeveranceField = AccessTools.Field(typeof(ZombieSymbiant), "safeSeveranceInProgress");
			try
			{
				if (safeSeveranceField == null)
					return new { success = false, error = "Could not access the Symbiant safe-severance lifecycle flag." };
				if (TrySetupSymbiantNaturalSpawnFixture(map, out fixture, out var fixtureError) == false)
					return fixtureError;
				symbiant = SpawnAssignedSymbiantForSeveranceContract(map, fixture);
				var host = fixture.host;
				var hediffBefore = host.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) != null;
				safeSeveranceField.SetValue(symbiant, true);
				try
				{
					symbiant.Kill(null);
				}
				finally
				{
					safeSeveranceField.SetValue(symbiant, false);
				}
				symbiantCorpse = symbiant.Corpse;
				var staleLinkBeforeHostDeath = symbiantCorpse?.InnerPawn == symbiant
					&& symbiant.Dead
					&& symbiant.Destroyed
					&& symbiant.Discarded == false
					&& symbiant.HostThingId == host.ThingID
					&& hediffBefore;
				host.Kill(null);
				var hediffAfter = host.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) != null;
				action = new
				{
					staleLinkBeforeHostDeath,
					hostDead = host.Dead,
					hediffAfter,
					symbiantCorpseDestroyed = symbiantCorpse == null || symbiantCorpse.Destroyed,
					symbiantDiscarded = symbiant.Discarded,
					symbiantWorldPawnAfter = Find.WorldPawns?.Contains(symbiant) == true,
					linkedAfter = ZombieRuntimeActions.StableThingId(ZombieSymbiant.LinkedSymbiantFor(host)),
					success = staleLinkBeforeHostDeath
						&& host.Dead
						&& hediffAfter == false
						&& (symbiantCorpse == null || symbiantCorpse.Destroyed)
						&& symbiant.Discarded
						&& Find.WorldPawns?.Contains(symbiant) != true
						&& ZombieSymbiant.LinkedSymbiantFor(host) == null
				};
			}
			catch (Exception ex)
			{
				error = ex.ToString();
			}
			finally
			{
				if (symbiant != null)
					safeSeveranceField?.SetValue(symbiant, false);
				if (cleanup)
				{
					if (symbiantCorpse?.Destroyed == false)
						symbiantCorpse.Destroy(DestroyMode.Vanish);
					_ = CleanupTemporarySymbiant(map, symbiant, true);
					_ = CleanupSymbiantNaturalSpawnFixture(map, fixture, true);
				}
			}

			return new
			{
				success = error == null && ScenarioSucceeded(action),
				sourcePath = "Pawn.Kill suppressed-destroy window -> Symbiant corpse -> linked host Pawn.Kill -> safe corpse/bond cleanup",
				error,
				action
			};
		}

		static object RunSymbiantPawnKillScenario(Map map, bool cleanup)
		{
			SymbiantNaturalSpawnFixture fixture = null;
			ZombieSymbiant symbiant = null;
			Corpse corpse = null;
			object error = null;
			object action = null;
			try
			{
				if (TrySetupSymbiantNaturalSpawnFixture(map, out fixture, out var fixtureError) == false)
					return fixtureError;
				symbiant = SpawnAssignedSymbiantForSeveranceContract(map, fixture);
				var host = fixture.host;
				var hediffBefore = host.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) != null;

				var worldPawnBefore = Find.WorldPawns?.Contains(symbiant) == true;
				symbiant.Kill(null);
				corpse = symbiant.Corpse;
				var worldPawnAfter = Find.WorldPawns?.Contains(symbiant) == true;
				var hediffAfter = host.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) != null;
				action = new
				{
					cell = ZombieRuntimeActions.DescribeCell(symbiant.PositionHeld),
					worldPawnBefore,
					worldPawnAfter,
					hediffBefore,
					hediffAfter,
					hostDead = host.Dead,
					hostCorpse = ZombieRuntimeActions.StableThingId(host.Corpse),
					dead = symbiant.Dead,
					destroyed = symbiant.Destroyed,
					discarded = symbiant.Discarded,
					symbiosisSevered = symbiant.SymbiosisSevered,
					isBeingKilled = symbiant.health?.isBeingKilled ?? false,
					corpse = ZombieRuntimeActions.StableThingId(corpse),
					corpseSpawned = corpse?.Spawned ?? false,
					corpseDestroyed = corpse?.Destroyed ?? true,
					corpseInnerPawnMatches = ReferenceEquals(corpse?.InnerPawn, symbiant),
					success = worldPawnBefore == false
						&& hediffBefore
						&& hediffAfter == false
						&& host.Dead == false
						&& host.Corpse == null
						&& symbiant.SymbiosisSevered
						&& symbiant.Dead
						&& symbiant.Destroyed
						&& symbiant.Discarded == false
						&& symbiant.health?.isBeingKilled == false
						&& corpse != null
						&& corpse.Spawned
						&& corpse.Destroyed == false
						&& ReferenceEquals(corpse.InnerPawn, symbiant)
				};
			}
			catch (Exception ex)
			{
				error = ex.ToString();
			}
			finally
			{
				if (cleanup)
				{
					if (corpse?.Destroyed == false)
						corpse.Destroy(DestroyMode.Vanish);
					if (symbiant != null && Find.WorldPawns?.Contains(symbiant) == true)
						Find.WorldPawns.RemovePawn(symbiant);
					if (symbiant?.Discarded == false)
						symbiant.Discard(true);
					_ = CleanupSymbiantNaturalSpawnFixture(map, fixture, true);
				}
			}

			return new
			{
				success = error == null && ScenarioSucceeded(action),
				sourcePath = "ZombieSymbiant.Kill -> safe host detach -> DeSpawnOrDeselect -> MakeCorpse",
				error,
				action
			};
		}

		static Hediff_SymbiantDamageEcho[] SymbiantDamageEchoes(Pawn host)
		{
			return host?.health?.hediffSet?.hediffs?.OfType<Hediff_SymbiantDamageEcho>().ToArray()
				?? Array.Empty<Hediff_SymbiantDamageEcho>();
		}

		static object DescribeSymbiantDamageFacade(Pawn host, ZombieSymbiant symbiant)
		{
			var echoes = SymbiantDamageEchoes(host);
			var sourceHediffs = symbiant?.health?.hediffSet?.hediffs ?? new List<Hediff>();
			var corePart = symbiant?.RaceProps?.body?.corePart;
			return new
			{
				pool = symbiant == null ? null : new
				{
					current = symbiant.SharedHealthCurrentDisplay,
					maximum = symbiant.SharedHealthMaxDisplay,
					fraction = symbiant.SharedHealthFraction,
					lastDamageTick = symbiant.LastSharedHealthDamageTick,
					nextRecoveryTick = symbiant.NextSharedHealthRecoveryTick,
					echoHistoryTotal = symbiant.DamageEchoHistoryTotal,
					echoHistory = symbiant.DamageEchoHistory.Select(record => new
					{
						record.categoryKey,
						record.cachedLabel,
						record.amount
					}).ToArray()
				},
				symbiant = symbiant == null ? null : new
				{
					partHealth = corePart == null ? 0f : symbiant.health.hediffSet.GetPartHealth(corePart),
					partMaxHealth = corePart?.def?.GetMaxHealth(symbiant) ?? 0f,
					summaryHealth = symbiant.health.summaryHealth.SummaryHealthPercent,
					pain = symbiant.health.hediffSet.PainTotal,
					moving = symbiant.health.capacities.GetLevel(PawnCapacityDefOf.Moving),
					manipulation = symbiant.health.capacities.GetLevel(PawnCapacityDefOf.Manipulation),
					consciousness = symbiant.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness),
					downed = symbiant.Downed,
					dead = symbiant.Dead,
					stunned = symbiant.stances?.stunner?.Stunned == true,
					fireAttachmentCount = FireAttachmentCount(symbiant),
					anatomyInjuryCount = sourceHediffs.Count(hediff => hediff?.GetType() == typeof(Hediff_Injury)),
					effectHediffs = sourceHediffs.Select(hediff => new
					{
						def = hediff.def?.defName,
						className = hediff.GetType().FullName,
						severity = hediff.Severity,
						part = hediff.Part?.def?.defName
					}).ToArray()
				},
				host = host == null ? null : new
				{
					realInjuryCount = host.health.hediffSet.hediffs.OfType<Hediff_Injury>().Count(),
					realInjurySeverity = TotalInjurySeverity(host),
					summaryHealth = host.health.summaryHealth.SummaryHealthPercent,
					pain = host.health.hediffSet.PainTotal,
					moving = host.health.capacities.GetLevel(PawnCapacityDefOf.Moving),
					manipulation = host.health.capacities.GetLevel(PawnCapacityDefOf.Manipulation),
					consciousness = host.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness),
					downed = host.Downed,
					dead = host.Dead,
					echoes = echoes.Select(echo => new
					{
						echo.categoryKey,
						echo.displayAmount,
						label = echo.Label,
						className = echo.GetType().FullName,
						def = echo.def?.defName,
						part = echo.Part?.def?.defName,
						visible = echo.Visible,
						shouldRemove = echo.ShouldRemove,
						tendable = echo.TendableNow(false),
						autoHealable = ZombieSymbiant.IsAutoHealableHediffForDebug(echo),
						summaryHealthImpact = echo.SummaryHealthPercentImpact,
						labelColor = new { echo.LabelColor.r, echo.LabelColor.g, echo.LabelColor.b, echo.LabelColor.a },
						tooltip = echo.GetTooltip(host, false)
					}).ToArray()
				}
			};
		}

		static object RunSymbiantUnsafeDamageScenario(Map map, string scenario, bool cleanup, Map secondMap = null)
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
				if (scenario == "crossMapDamageIsolation")
				{
					if (secondMap == null)
						return new { success = false, scenario, error = "The second-map fixture is missing." };
					var addedCells = GrowSymbiantForDamageProbe(map, symbiant, 40);
					var originCell = host.Position;
					var moveToSecondMap = MovePawnToSymbiantContractMap(host, secondMap, secondMap.Center, symbiant);
					var dormant = ScenarioSucceeded(moveToSecondMap)
						? DescribeHostAvailabilityState("crossMapDamageIsolation", map, symbiant, host)
						: moveToSecondMap;
					var echoesWhileDormantBeforeDamage = SymbiantDamageEchoes(host).Length;
					var sharedHealthBefore = symbiant.SharedHealthCurrentDisplay;
					var remoteHostDamage = 5f;
					var hostDamageResult = host.TakeDamage(new DamageInfo(DamageDefOf.Cut, remoteHostDamage, 999f, -1f, null));
					var sharedHealthAfterHostDamage = symbiant.SharedHealthCurrentDisplay;
					var hostInjuryAfterHostDamage = TotalInjurySeverity(host);
					var remoteSymbiantDamage = 40f;
					var symbiantDamageResult = symbiant.TakeDamage(new DamageInfo(DamageDefOf.Cut, remoteSymbiantDamage, 999f, -1f, null));
					var sharedHealthAfterSymbiantDamage = symbiant.SharedHealthCurrentDisplay;
					var hostInjuryAfterSymbiantDamage = TotalInjurySeverity(host);
					var echoesWhileDormantAfterDamage = SymbiantDamageEchoes(host).Length;
					var moveBack = MovePawnToSymbiantContractMap(host, map, originCell, symbiant);
					var reactivated = ScenarioSucceeded(moveBack)
						? DescribeHostAvailabilityState("crossMapDamageIsolationReturn", map, symbiant, host)
						: moveBack;
					var echoesAfterReturn = SymbiantDamageEchoes(host);
					action = new
					{
						addedCells,
						moveToSecondMap,
						dormant,
						remoteHostDamage,
						hostDamageDealt = hostDamageResult.totalDamageDealt,
						sharedHealthBefore,
						sharedHealthAfterHostDamage,
						hostInjuryBefore,
						hostInjuryAfterHostDamage,
						remoteSymbiantDamage,
						symbiantDamageDealt = symbiantDamageResult.totalDamageDealt,
						sharedHealthAfterSymbiantDamage,
						hostInjuryAfterSymbiantDamage,
						echoesWhileDormantBeforeDamage,
						echoesWhileDormantAfterDamage,
						echoesAfterReturn = echoesAfterReturn.Length,
						echoHistoryTotal = symbiant.DamageEchoHistoryTotal,
						moveBack,
						reactivated,
						success = addedCells > 0
							&& ScenarioSucceeded(dormant)
							&& hostDamageResult.totalDamageDealt > 0f
							&& hostInjuryAfterHostDamage > hostInjuryBefore
							&& Mathf.Approximately(sharedHealthAfterHostDamage, sharedHealthBefore)
							&& symbiantDamageResult.totalDamageDealt > 0f
							&& sharedHealthAfterSymbiantDamage < sharedHealthAfterHostDamage
							&& Mathf.Abs((sharedHealthAfterHostDamage - sharedHealthAfterSymbiantDamage) - symbiantDamageResult.totalDamageDealt) <= 1f
							&& Mathf.Approximately(hostInjuryAfterSymbiantDamage, hostInjuryAfterHostDamage)
							&& echoesWhileDormantBeforeDamage == 0
							&& echoesWhileDormantAfterDamage == 0
							&& host.Dead == false
							&& symbiant.Destroyed == false
							&& ScenarioSucceeded(reactivated)
							&& echoesAfterReturn.Length == 1
							&& echoesAfterReturn[0].Visible
					};
				}
				else if (scenario == "crossMapPoolExhaustionSparesHost")
				{
					if (secondMap == null)
						return new { success = false, scenario, error = "The second-map fixture is missing." };
					var moveToSecondMap = MovePawnToSymbiantContractMap(host, secondMap, secondMap.Center, symbiant);
					var dormant = ScenarioSucceeded(moveToSecondMap)
						? DescribeHostAvailabilityState("crossMapPoolExhaustion", map, symbiant, host)
						: moveToSecondMap;
					var hediffBefore = host.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) != null;
					var sharedHealthBefore = symbiant.SharedHealthCurrentDisplay;
					var damage = symbiant.SharedHealthMaxDisplay + 250f;
					var damageResult = symbiant.TakeDamage(new DamageInfo(DamageDefOf.Cut, damage, 1f, -1f, null));
					var linkedAfter = ZombieSymbiant.LinkedSymbiantFor(host);
					var hediffAfter = host.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) != null;
					var hostInjuryAfter = TotalInjurySeverity(host);
					var activeAfter = ZombieSymbiant.ActiveSymbiant(map);
					action = new
					{
						moveToSecondMap,
						dormant,
						damage,
						damageDealt = damageResult.totalDamageDealt,
						sharedHealthBefore,
						hediffBefore,
						hediffAfter,
						hostInjuryBefore,
						hostInjuryAfter,
						hostDead = host.Dead,
						symbiantDestroyed = symbiant.Destroyed,
						linkedAfter = ZombieRuntimeActions.StableThingId(linkedAfter),
						activeAfter = ZombieRuntimeActions.StableThingId(activeAfter),
						success = ScenarioSucceeded(dormant)
							&& damageResult.totalDamageDealt > 0f
							&& sharedHealthBefore > 0
							&& hediffBefore
							&& hediffAfter == false
							&& host.Dead == false
							&& Mathf.Approximately(hostInjuryAfter, hostInjuryBefore)
							&& symbiant.Destroyed
							&& linkedAfter == null
							&& activeAfter == null
					};
				}
				else if (scenario == "crossMapUncontrolledDestroySparesHost")
				{
					if (secondMap == null)
						return new { success = false, scenario, error = "The second-map fixture is missing." };
					var moveToSecondMap = MovePawnToSymbiantContractMap(host, secondMap, secondMap.Center, symbiant);
					var dormant = ScenarioSucceeded(moveToSecondMap)
						? DescribeHostAvailabilityState("crossMapUncontrolledDestroy", map, symbiant, host)
						: moveToSecondMap;
					var hediffBefore = host.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) != null;
					symbiant.Destroy(DestroyMode.Vanish);
					var worldPawnAfterDestroy = Find.WorldPawns?.Contains(symbiant) == true;
					var linkedAfter = ZombieSymbiant.LinkedSymbiantFor(host);
					var hediffAfter = host.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) != null;
					var hostInjuryAfter = TotalInjurySeverity(host);
					action = new
					{
						moveToSecondMap,
						dormant,
						hediffBefore,
						hediffAfter,
						hostInjuryBefore,
						hostInjuryAfter,
						hostDead = host.Dead,
						symbiantDestroyed = symbiant.Destroyed,
						symbiantDiscarded = symbiant.Discarded,
						worldPawnAfterDestroy,
						linkedAfter = ZombieRuntimeActions.StableThingId(linkedAfter),
						success = ScenarioSucceeded(dormant)
							&& hediffBefore
							&& hediffAfter == false
							&& host.Dead == false
							&& Mathf.Approximately(hostInjuryAfter, hostInjuryBefore)
							&& symbiant.Destroyed
							&& symbiant.Discarded
							&& worldPawnAfterDestroy == false
							&& linkedAfter == null
					};
				}
				else if (scenario == "crossMapHostDeathStartsRetreat")
				{
					if (secondMap == null)
						return new { success = false, scenario, error = "The second-map fixture is missing." };
					var moveToSecondMap = MovePawnToSymbiantContractMap(host, secondMap, secondMap.Center, symbiant);
					var dormant = ScenarioSucceeded(moveToSecondMap)
						? DescribeHostAvailabilityState("crossMapHostDeath", map, symbiant, host)
						: moveToSecondMap;
					var hediffBefore = host.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) != null;
					var deathLifecycle = KillLinkedHostAndCaptureDeathLifecycle(host);
					var activeAfter = ZombieSymbiant.ActiveSymbiant(map);
					var hediffAfter = host.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) != null;
					action = new
					{
						deathLifecycle,
						moveToSecondMap,
						dormant,
						hediffBefore,
						hediffAfter,
						hostDead = host.Dead,
						symbiantDestroyed = symbiant.Destroyed,
						symbiosisSevered = symbiant.SymbiosisSevered,
						activeAfter = ZombieRuntimeActions.StableThingId(activeAfter),
						success = ScenarioSucceeded(dormant)
							&& deathLifecycle.success
							&& hediffBefore
							&& hediffAfter == false
							&& host.Dead
							&& symbiant.Destroyed == false
							&& symbiant.SymbiosisSevered
							&& activeAfter == symbiant
					};
				}
				else if (scenario == "symbiantDamageCreatesEcho")
				{
					var addedCells = GrowSymbiantForDamageProbe(map, symbiant, 40);
					var damage = 40f;
					var symbiantInjuryBefore = TotalInjurySeverity(symbiant);
					var sharedHealthBefore = symbiant.SharedHealthCurrentDisplay;
					var hostSummaryBefore = host.health.summaryHealth.SummaryHealthPercent;
					var hostPainBefore = host.health.hediffSet.PainTotal;
					var hostMovingBefore = host.health.capacities.GetLevel(PawnCapacityDefOf.Moving);
					var hostManipulationBefore = host.health.capacities.GetLevel(PawnCapacityDefOf.Manipulation);
					var facadeBefore = DescribeSymbiantDamageFacade(host, symbiant);
					var damageResult = symbiant.TakeDamage(new DamageInfo(DamageDefOf.Cut, damage, 999f, -1f, null));
					var hostInjuryAfter = TotalInjurySeverity(host);
					var symbiantInjuryAfter = TotalInjurySeverity(symbiant);
					var hostInjuryDelta = hostInjuryAfter - hostInjuryBefore;
					var symbiantInjuryDelta = symbiantInjuryAfter - symbiantInjuryBefore;
					var sharedHealthAfter = symbiant.SharedHealthCurrentDisplay;
					var sharedHealthDelta = sharedHealthBefore - sharedHealthAfter;
					var echoes = SymbiantDamageEchoes(host);
					var echo = echoes.SingleOrDefault();
					var corePart = symbiant.RaceProps.body.corePart;
					var facadeAfter = DescribeSymbiantDamageFacade(host, symbiant);
					action = new
					{
						damage,
						cellCount = symbiant.CellCount,
						addedCells,
						damageDealt = damageResult.totalDamageDealt,
						sharedHealthBefore,
						sharedHealthAfter,
						sharedHealthDelta,
						hostInjuryBefore,
						hostInjuryAfter,
						hostInjuryDelta,
						symbiantInjuryBefore,
						symbiantInjuryAfter,
						symbiantInjuryDelta,
						echoCount = echoes.Length,
						echoAmount = echo?.displayAmount ?? 0f,
						echoLabel = echo?.Label,
						echoPart = echo?.Part?.def?.defName,
						echoVisible = echo?.Visible,
						echoTendable = echo?.TendableNow(false),
						echoAutoHealable = echo == null ? null : (bool?)ZombieSymbiant.IsAutoHealableHediffForDebug(echo),
						echoSummaryHealthImpact = echo?.SummaryHealthPercentImpact,
						echoHistoryTotal = symbiant.DamageEchoHistoryTotal,
						facadeBefore,
						facadeAfter,
						success = symbiant.CellCount >= 40
							&& damageResult.totalDamageDealt > 0f
							&& Mathf.Approximately(hostInjuryDelta, 0f)
							&& Mathf.Approximately(symbiantInjuryDelta, 0f)
							&& sharedHealthAfter < sharedHealthBefore
							&& Mathf.Abs(sharedHealthDelta - damageResult.totalDamageDealt) <= 1f
							&& echoes.Length == 1
							&& echo != null
							&& Mathf.Abs(echo.displayAmount - sharedHealthDelta) <= 1f
							&& echo.Part == null
							&& echo.Visible
							&& echo.ShouldRemove == false
							&& echo.TendableNow(false) == false
							&& ZombieSymbiant.IsAutoHealableHediffForDebug(echo) == false
							&& Mathf.Approximately(echo.SummaryHealthPercentImpact, 0f)
							&& Mathf.Abs(symbiant.DamageEchoHistoryTotal - sharedHealthDelta) <= 1f
							&& Mathf.Approximately(symbiant.health.hediffSet.GetPartHealth(corePart), corePart.def.GetMaxHealth(symbiant))
							&& Mathf.Approximately(symbiant.health.summaryHealth.SummaryHealthPercent, symbiant.SharedHealthFraction)
							&& Mathf.Approximately(host.health.summaryHealth.SummaryHealthPercent, hostSummaryBefore)
							&& Mathf.Approximately(host.health.hediffSet.PainTotal, hostPainBefore)
							&& Mathf.Approximately(host.health.capacities.GetLevel(PawnCapacityDefOf.Moving), hostMovingBefore)
							&& Mathf.Approximately(host.health.capacities.GetLevel(PawnCapacityDefOf.Manipulation), hostManipulationBefore)
							&& host.Downed == false
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
				else if (scenario == "hostEffectDamageSemantics")
				{
					var addedCells = GrowSymbiantForDamageProbe(map, symbiant, 40);
					var cellCountBefore = symbiant.CellCount;
					var sharedHealthBefore = symbiant.SharedHealthCurrentDisplay;
					var hostInjuryBeforeEffects = TotalInjurySeverity(host);
					var pawnPreApplyDamage = AccessTools.Method(typeof(Pawn), nameof(Pawn.PreApplyDamage));
					var damageProbePrefix = AccessTools.Method(typeof(ZombielandBridgeTools), nameof(RecordSymbiantHostDamagePacket));
					if (pawnPreApplyDamage == null || damageProbePrefix == null)
						throw new InvalidOperationException("Could not resolve the Symbiant host damage probe target or prefix.");
					var damageProbeHarmony = new Harmony(SymbiantHostDamageProbeHarmonyId);
					damageProbeHarmony.Unpatch(pawnPreApplyDamage, HarmonyPatchType.Prefix, SymbiantHostDamageProbeHarmonyId);
					symbiantHostDamageProbePawn = host;
					symbiantHostDamagePackets.Clear();
					damageProbeHarmony.Patch(
						pawnPreApplyDamage,
						prefix: new HarmonyMethod(damageProbePrefix)
						{
							priority = Priority.First,
							before = new[] { "net.pardeike.zombieland" }
						});
					try
					{
						var extinguishAmount = DamageDefOf.Extinguish.defaultDamage;
						var extinguishResult = host.TakeDamage(new DamageInfo(DamageDefOf.Extinguish, extinguishAmount, 0f, -1f, null));
						var extinguishPackets = SymbiantHostDamagePacketSnapshot();
						var expectedExtinguishDrain = ExpectedSymbiantHostHealthDrain(extinguishPackets);
						var sharedHealthAfterExtinguish = symbiant.SharedHealthCurrentDisplay;
						var actualExtinguishDrain = sharedHealthBefore - sharedHealthAfterExtinguish;
						var hostInjuryAfterExtinguish = TotalInjurySeverity(host);
						var firefoamHediff = DamageDefOf.Extinguish.hediff;
						var coveredInFirefoam = firefoamHediff != null && host.health?.hediffSet?.GetFirstHediffOfDef(firefoamHediff) != null;

						symbiantHostDamagePackets.Clear();
						var stunResult = host.TakeDamage(new DamageInfo(DamageDefOf.Stun, 5f, 999f, -1f, null));
						var stunPackets = SymbiantHostDamagePacketSnapshot();
						var expectedStunDrain = ExpectedSymbiantHostHealthDrain(stunPackets);
						var sharedHealthAfterStun = symbiant.SharedHealthCurrentDisplay;
						var actualStunDrain = sharedHealthAfterExtinguish - sharedHealthAfterStun;
						var hostInjuryAfterStun = TotalInjurySeverity(host);
						var stunnedAfterStun = host.stances?.stunner?.Stunned == true;

						symbiantHostDamagePackets.Clear();
						var empAmount = DamageDefOf.EMP.defaultDamage;
						var empResult = host.TakeDamage(new DamageInfo(DamageDefOf.EMP, empAmount, 0f, -1f, null));
						var empPackets = SymbiantHostDamagePacketSnapshot();
						var expectedEmpDrain = ExpectedSymbiantHostHealthDrain(empPackets);
						var sharedHealthAfterEmp = symbiant.SharedHealthCurrentDisplay;
						var actualEmpDrain = sharedHealthAfterStun - sharedHealthAfterEmp;
						var hostInjuryAfterEmp = TotalInjurySeverity(host);

						symbiantHostDamagePackets.Clear();
						var seismicAmount = symbiant.SharedHealthCurrentDisplay + 250f;
						var seismicResult = host.TakeDamage(new DamageInfo(CustomDefs.SeismicWave, seismicAmount, 0f, -1f, null));
						var seismicPackets = SymbiantHostDamagePacketSnapshot();
						var expectedSeismicDrain = ExpectedSymbiantHostHealthDrain(seismicPackets);
						var sharedHealthAfterSeismic = symbiant.SharedHealthCurrentDisplay;
						var actualSeismicDrain = sharedHealthAfterEmp - sharedHealthAfterSeismic;
						var hostInjuryAfterSeismic = TotalInjurySeverity(host);
						var linkedAfter = ZombieSymbiant.LinkedSymbiantFor(host);

						action = new
						{
							addedCells,
							cellCountBefore,
							cellCountAfter = symbiant.CellCount,
							extinguishAmount,
							extinguishHarmsHealth = DamageDefOf.Extinguish.harmsHealth,
							extinguishDamageDealt = extinguishResult.totalDamageDealt,
							extinguishPackets,
							expectedExtinguishDrain,
							actualExtinguishDrain,
							coveredInFirefoam,
							stunHarmsHealth = DamageDefOf.Stun.harmsHealth,
							stunDamageDealt = stunResult.totalDamageDealt,
							stunPackets,
							expectedStunDrain,
							actualStunDrain,
							stunnedAfterStun,
							empAmount,
							empHarmsHealth = DamageDefOf.EMP.harmsHealth,
							empDamageDealt = empResult.totalDamageDealt,
							empPackets,
							expectedEmpDrain,
							actualEmpDrain,
							seismicAmount,
							seismicHarmsHealth = CustomDefs.SeismicWave?.harmsHealth ?? false,
							seismicWorkerClass = CustomDefs.SeismicWave?.workerClass?.FullName,
							seismicDamageDealt = seismicResult.totalDamageDealt,
							seismicPackets,
							expectedSeismicDrain,
							actualSeismicDrain,
							sharedHealthBefore,
							sharedHealthAfterExtinguish,
							sharedHealthAfterStun,
							sharedHealthAfterEmp,
							sharedHealthAfterSeismic,
							hostInjuryBeforeEffects,
							hostInjuryAfterExtinguish,
							hostInjuryAfterStun,
							hostInjuryAfterEmp,
							hostInjuryAfterSeismic,
							hostDead = host.Dead,
							symbiantDestroyed = symbiant.Destroyed,
							linkedAfter = ZombieRuntimeActions.StableThingId(linkedAfter),
							success = addedCells > 0
								&& DamageDefOf.Extinguish.harmsHealth == false
								&& Mathf.Approximately(extinguishResult.totalDamageDealt, 0f)
								&& coveredInFirefoam
								&& DamageDefOf.Stun.harmsHealth == false
								&& Mathf.Approximately(stunResult.totalDamageDealt, 0f)
								&& stunnedAfterStun
								&& DamageDefOf.EMP.harmsHealth == false
								&& Mathf.Approximately(empResult.totalDamageDealt, 0f)
								&& CustomDefs.SeismicWave != null
								&& CustomDefs.SeismicWave.harmsHealth
								&& Mathf.Approximately(seismicResult.totalDamageDealt, 0f)
								&& Mathf.Approximately(actualExtinguishDrain, expectedExtinguishDrain)
								&& Mathf.Approximately(actualStunDrain, expectedStunDrain)
								&& Mathf.Approximately(actualEmpDrain, expectedEmpDrain)
								&& Mathf.Approximately(expectedSeismicDrain, 0f)
								&& Mathf.Approximately(actualSeismicDrain, expectedSeismicDrain)
								&& Mathf.Approximately(hostInjuryAfterExtinguish, hostInjuryBeforeEffects)
								&& Mathf.Approximately(hostInjuryAfterStun, hostInjuryBeforeEffects)
								&& (expectedEmpDrain > 0f || Mathf.Approximately(hostInjuryAfterEmp, hostInjuryBeforeEffects))
								&& Mathf.Approximately(hostInjuryAfterSeismic, hostInjuryAfterEmp)
								&& symbiant.CellCount == cellCountBefore
								&& host.Dead == false
								&& symbiant.Destroyed == false
								&& linkedAfter == symbiant
						};
					}
					finally
					{
						symbiantHostDamageProbePawn = null;
						symbiantHostDamagePackets.Clear();
						damageProbeHarmony.Unpatch(pawnPreApplyDamage, HarmonyPatchType.Prefix, SymbiantHostDamageProbeHarmonyId);
					}
				}
				else if (scenario == "symbiantEffectPipeline")
				{
					var addedCells = GrowSymbiantForDamageProbe(map, symbiant, 40);
					var hostInjuryBeforeEffects = TotalInjurySeverity(host);
					var sharedHealthBefore = symbiant.SharedHealthCurrentDisplay;
					var stunResult = symbiant.TakeDamage(new DamageInfo(DamageDefOf.Stun, 5f, 999f, -1f, null));
					var sharedHealthAfterStun = symbiant.SharedHealthCurrentDisplay;
					var stunnedAfterStun = symbiant.stances?.stunner?.Stunned == true;
					var flameResult = symbiant.TakeDamage(new DamageInfo(DamageDefOf.Flame, 12f, 999f, -1f, null));
					var sharedHealthAfterFlame = symbiant.SharedHealthCurrentDisplay;
					var echoes = SymbiantDamageEchoes(host);
					var facade = DescribeSymbiantDamageFacade(host, symbiant);
					action = new
					{
						addedCells,
						stunDamageDealt = stunResult.totalDamageDealt,
						stunnedAfterStun,
						flameDamageDealt = flameResult.totalDamageDealt,
						sharedHealthBefore,
						sharedHealthAfterStun,
						sharedHealthAfterFlame,
						hostInjuryBeforeEffects,
						hostInjuryAfterEffects = TotalInjurySeverity(host),
						symbiantAnatomyInjuryCount = symbiant.health.hediffSet.hediffs.Count(hediff => hediff?.GetType() == typeof(Hediff_Injury)),
						echoCount = echoes.Length,
						facade,
						success = addedCells > 0
							&& Mathf.Approximately(stunResult.totalDamageDealt, 0f)
							&& sharedHealthAfterStun == sharedHealthBefore
							&& stunnedAfterStun
							&& flameResult.totalDamageDealt > 0f
							&& Mathf.Abs((sharedHealthAfterStun - sharedHealthAfterFlame) - flameResult.totalDamageDealt) <= 1f
							&& Mathf.Approximately(TotalInjurySeverity(host), hostInjuryBeforeEffects)
							&& symbiant.health.hediffSet.hediffs.Any(hediff => hediff?.GetType() == typeof(Hediff_Injury)) == false
							&& echoes.Length == 1
							&& echoes[0].TendableNow(false) == false
							&& host.Downed == false
							&& host.Dead == false
							&& symbiant.Downed == false
							&& symbiant.Dead == false
					};
				}
				else if (scenario == "sharedHealthRecovery")
				{
					var addedCells = GrowSymbiantForDamageProbe(map, symbiant, 40);
					if (host.playerSettings != null)
						host.playerSettings.hostilityResponse = HostilityResponseMode.Ignore;
					host.jobs?.StopAll(false);
					host.stances?.stunner?.StunFor(ZombieSymbiant.SharedHealthRecoveryDelayTicks + 500, symbiant, false);
					var hostIsolatedOnSameMap = host.Spawned
						&& symbiant.IsActiveBondWith(host)
						&& host.playerSettings?.hostilityResponse == HostilityResponseMode.Ignore;
					var enemyZombieResponse = ZombieSettings.Values.enemyZombieResponse;
					var animalsAttackZombies = ZombieSettings.Values.animalsAttackZombies;
					const int settleTicks = 120;
					DamageWorker.DamageResult damageResult = null;
					var sharedHealthBefore = 0;
					var sharedHealthAfterDamage = 0;
					var sharedHealthBeforeRecoveryTick = 0;
					var historyBeforeDamage = 0f;
					var historyAfterDamage = 0f;
					var recoveryTick = 0;
					var damageTick = 0;
					var recoveryMethodResolved = false;
					try
					{
						ApplyZombieSettingsOverride(settings =>
						{
							settings.enemyZombieResponse = ZombieResponsePolicy.Minimal;
							settings.animalsAttackZombies = false;
						});
						foreach (var pawn in map.mapPawns.AllPawnsSpawned.Where(pawn => pawn?.CurJob?.targetA.Thing == symbiant).ToArray())
							pawn.jobs?.StopAll(false);
						RefreshZombieTargetCache(map);
						AdvanceGameTicks(settleTicks);
						foreach (var pawn in map.mapPawns.AllPawnsSpawned.Where(pawn => pawn?.CurJob?.targetA.Thing == symbiant).ToArray())
							pawn.jobs?.StopAll(false);
						sharedHealthBefore = symbiant.SharedHealthCurrentDisplay;
						historyBeforeDamage = symbiant.DamageEchoHistoryTotal;
						damageResult = symbiant.TakeDamage(new DamageInfo(DamageDefOf.Cut, 100f, 999f, -1f, null));
						damageTick = GenTicks.TicksGame;
						sharedHealthAfterDamage = symbiant.SharedHealthCurrentDisplay;
						historyAfterDamage = symbiant.DamageEchoHistoryTotal;
						recoveryTick = symbiant.NextSharedHealthRecoveryTick;
						var tryRecover = AccessTools.Method(typeof(ZombieSymbiant), "TryRecoverSharedHealth", new[] { typeof(int) });
						recoveryMethodResolved = tryRecover != null;
						tryRecover?.Invoke(symbiant, new object[] { Math.Max(damageTick, recoveryTick - 1) });
						sharedHealthBeforeRecoveryTick = symbiant.SharedHealthCurrentDisplay;
						tryRecover?.Invoke(symbiant, new object[] { recoveryTick });
					}
					finally
					{
						ApplyZombieSettingsOverride(settings =>
						{
							settings.enemyZombieResponse = enemyZombieResponse;
							settings.animalsAttackZombies = animalsAttackZombies;
						});
						RefreshZombieTargetCache(map);
					}
					var sharedHealthAfterRecovery = symbiant.SharedHealthCurrentDisplay;
					var missingBeforeRecovery = symbiant.SharedHealthMaxDisplay - sharedHealthBeforeRecoveryTick;
					var expectedRecovery = Mathf.Min(missingBeforeRecovery, Mathf.Max(1f, missingBeforeRecovery * ZombieSymbiant.SharedHealthRecoveryMissingFraction));
					action = new
					{
						addedCells,
						hostIsolatedOnSameMap,
						settleTicks,
						damageDealt = damageResult?.totalDamageDealt ?? 0f,
						sharedHealthBefore,
						sharedHealthAfterDamage,
						sharedHealthBeforeRecoveryTick,
						sharedHealthAfterRecovery,
						expectedRecovery,
						actualRecovery = sharedHealthAfterRecovery - sharedHealthBeforeRecoveryTick,
						historyBeforeDamage,
						historyAfterDamage,
						historyAfterRecovery = symbiant.DamageEchoHistoryTotal,
						damageTick,
						recoveryTick,
						recoveryMethodResolved,
						deadlineMatchesConfiguredDelay = recoveryTick == damageTick + ZombieSymbiant.SharedHealthRecoveryDelayTicks,
						nextRecoveryTick = symbiant.NextSharedHealthRecoveryTick,
						facade = DescribeSymbiantDamageFacade(host, symbiant),
						success = addedCells > 0
							&& hostIsolatedOnSameMap
							&& damageResult != null
							&& damageResult.totalDamageDealt > 0f
							&& recoveryMethodResolved
							&& recoveryTick == damageTick + ZombieSymbiant.SharedHealthRecoveryDelayTicks
							&& sharedHealthAfterDamage < sharedHealthBefore
							&& Mathf.Abs((historyAfterDamage - historyBeforeDamage) - damageResult.totalDamageDealt) <= 1f
							&& sharedHealthBeforeRecoveryTick == sharedHealthAfterDamage
							&& Mathf.Abs((sharedHealthAfterRecovery - sharedHealthBeforeRecoveryTick) - expectedRecovery) <= 1f
							&& Mathf.Approximately(symbiant.DamageEchoHistoryTotal, historyAfterDamage)
							&& SymbiantDamageEchoes(host).Length == 1
							&& host.Dead == false
							&& symbiant.Destroyed == false
					};
				}
				else if (scenario == "damageEchoCap")
				{
					var recordMethod = AccessTools.Method(typeof(ZombieSymbiant), "RecordDamageEcho");
					var syncMethod = AccessTools.Method(typeof(ZombieSymbiant), "SyncHostDamageEchoes", new[] { typeof(Pawn) });
					for (var i = 0; i < 10; i++)
						recordMethod?.Invoke(symbiant, new object[] { "damage:ZLTest" + i, "test damage " + i, (float)(i + 1) });
					syncMethod?.Invoke(symbiant, new object[] { host });
					var records = symbiant.DamageEchoHistory.ToArray();
					var echoes = SymbiantDamageEchoes(host);
					var other = records.SingleOrDefault(record => record.categoryKey == "other");
					action = new
					{
						recordMethod = recordMethod?.ToString(),
						syncMethod = syncMethod?.ToString(),
						recordCount = records.Length,
						namedCount = records.Count(record => record.categoryKey != "other"),
						otherAmount = other?.amount ?? 0f,
						historyTotal = symbiant.DamageEchoHistoryTotal,
						echoCount = echoes.Length,
						echoes = echoes.Select(echo => new
						{
							echo.categoryKey,
							echo.displayAmount,
							echoPart = echo.Part?.def?.defName,
							tendable = echo.TendableNow(false),
							autoHealable = ZombieSymbiant.IsAutoHealableHediffForDebug(echo)
						}).ToArray(),
						success = recordMethod != null
							&& syncMethod != null
							&& records.Length == 8
							&& records.Count(record => record.categoryKey != "other") == 7
							&& other != null
							&& Mathf.Approximately(other.amount, 27f)
							&& Mathf.Approximately(symbiant.DamageEchoHistoryTotal, 55f)
							&& echoes.Length == 8
							&& echoes.All(echo => echo.Part == null
								&& echo.TendableNow(false) == false
								&& ZombieSymbiant.IsAutoHealableHediffForDebug(echo) == false)
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
						success = damageResult.totalDamageDealt > 0f
							&& sharedHealthBefore > 0
							&& hediffBefore
							&& hediffAfter == false
							&& host.Dead
							&& symbiant.Destroyed
							&& linkedAfter == null
							&& activeAfter == null
					};
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
				else if (scenario == "uncontrolledDestroySafelySeversHost")
				{
					var hediffBefore = host.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) != null;
					symbiant.Destroy(DestroyMode.Vanish);
					var worldPawnAfterDestroy = Find.WorldPawns?.Contains(symbiant) == true;
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
						symbiantDiscarded = symbiant.Discarded,
						worldPawnAfterDestroy,
						linkedAfter = ZombieRuntimeActions.StableThingId(linkedAfter),
						success = hediffBefore
							&& hediffAfter == false
							&& host.Dead == false
							&& Mathf.Approximately(hostInjuryAfter, hostInjuryBefore)
							&& symbiant.Destroyed
							&& symbiant.Discarded
							&& worldPawnAfterDestroy == false
							&& linkedAfter == null
					};
				}
				else if (scenario == "hostDeathStartsRetreat")
				{
					var hediffBefore = host.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) != null;
					var deathLifecycle = KillLinkedHostAndCaptureDeathLifecycle(host);
					var activeAfter = ZombieSymbiant.ActiveSymbiant(map);
					var hediffAfter = host.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) != null;
					action = new
					{
						deathLifecycle,
						hediffBefore,
						hediffAfter,
						hostDead = host.Dead,
						symbiantDestroyed = symbiant.Destroyed,
						symbiosisSevered = symbiant.SymbiosisSevered,
						activeAfter = ZombieRuntimeActions.StableThingId(activeAfter),
						success = hediffBefore
							&& deathLifecycle.success
							&& hediffAfter == false
							&& host.Dead
							&& symbiant.Destroyed == false
							&& symbiant.SymbiosisSevered
							&& activeAfter == symbiant
					};
				}
				else if (scenario == "destroyedHostStartsRetreat")
				{
					var hostId = host.ThingID;
					var hediffBefore = host.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) != null;
					host.Destroy(DestroyMode.Vanish);
					AccessTools.Method(typeof(ZombieSymbiant), "EnsureHostLink")?.Invoke(symbiant, null);
					var hediffAfter = host.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) != null;
					action = new
					{
						hostId,
						hediffBefore,
						hediffAfter,
						hostDestroyed = host.Destroyed,
						symbiantDestroyed = symbiant.Destroyed,
						symbiosisSevered = symbiant.SymbiosisSevered,
						linkedHost = ZombieRuntimeActions.StableThingId(symbiant.LinkedHost),
						linkedForHost = ZombieRuntimeActions.StableThingId(ZombieSymbiant.LinkedSymbiantFor(host)),
						success = hediffBefore
							&& hediffAfter == false
							&& host.Destroyed
							&& symbiant.Destroyed == false
							&& symbiant.SymbiosisSevered
							&& symbiant.LinkedHost == null
							&& ZombieSymbiant.LinkedSymbiantFor(host) == null
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

		static object RunSymbiantGravshipAbandonScenario(Map hostMap, SymbiantSecondMapFixture secondMapFixture, bool cleanup)
		{
			if (ModsConfig.OdysseyActive == false)
				return new { success = true, skipped = true, reason = "Odyssey is inactive, so GravshipUtility.AbandonMap is unavailable." };
			var symbiantMap = secondMapFixture?.map;
			if (hostMap == null || symbiantMap == null || Find.Maps.Contains(symbiantMap) == false)
				return new { success = false, error = "The host or disposable Symbiant map is unavailable for the gravship-abandon scenario." };

			SymbiantNaturalSpawnFixture fixture = null;
			ZombieSymbiant symbiant = null;
			Pawn host = null;
			object fixtureSetup = null;
			object moveHost = null;
			object dormant = null;
			object action = null;
			object error = null;
			var worldObjectsBefore = Find.WorldObjects.AllWorldObjects.ToHashSet();

			try
			{
				if (TrySetupSymbiantNaturalSpawnFixture(symbiantMap, out fixture, out var fixtureError) == false)
				{
					error = fixtureError;
				}
				else
				{
					fixtureSetup = DescribeSymbiantNaturalSpawnFixture(fixture);
					symbiant = SpawnAssignedSymbiantForSeveranceContract(symbiantMap, fixture);
					host = fixture.host;
					secondMapFixture.trackedPawns.Add(symbiant);
					secondMapFixture.trackedPawns.Add(host);
					moveHost = MovePawnToSymbiantContractMap(host, hostMap, hostMap.Center, symbiant);
					dormant = ScenarioSucceeded(moveHost)
						? DescribeHostAvailabilityState("gravshipAbandonRemoteHost", symbiantMap, symbiant, host)
						: moveHost;

					foreach (var pawn in SymbiantSecondMapPawns(symbiantMap))
						secondMapFixture.trackedPawns.Add(pawn);
					var fixturePawnIdsBeforeRoute = secondMapFixture.trackedPawns
						.Where(pawn => pawn != null)
						.Select(ZombieRuntimeActions.StableThingId)
						.Distinct()
						.ToArray();
					var hediffBefore = host.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) != null;
					var hostInjuryBefore = TotalInjurySeverity(host);
					var bringAlongOnGravship = symbiant.def?.bringAlongOnGravship ?? true;
					var shouldBringMethod = AccessTools.Method(typeof(Gravship), "ShouldBringOnGravship");
					var shouldBringOnGravship = shouldBringMethod?.Invoke(null, new object[] { symbiant, symbiant.Position }) as bool?;
					var routeMapPawnIdsBeforeRoute = SymbiantSecondMapPawns(symbiantMap)
						.Select(ZombieRuntimeActions.StableThingId)
						.ToArray();
					var hostInRouteMapPawnsBeforeRoute = SymbiantSecondMapPawns(symbiantMap).Contains(host);
					var hostInWorldPawnsBeforeRoute = Find.WorldPawns?.Contains(host) == true;
					var currentMapBeforeRoute = Current.Game.CurrentMap;
					var hostMapParentBeforeRoute = hostMap.Parent;
					GravshipUtility.AbandonMap(symbiantMap);

					var mapRemoved = Find.Maps.Contains(symbiantMap) == false;
					var linkedAfter = ZombieSymbiant.LinkedSymbiantFor(host);
					var hediffAfter = host.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) != null;
					var hostInjuryAfter = TotalInjurySeverity(host);
					var currentMapAfterRoute = Current.Game.CurrentMap;
					var worldPawnIdsAfterRoute = secondMapFixture.trackedPawns
						.Where(pawn => pawn != null && Find.WorldPawns?.Contains(pawn) == true)
						.Select(ZombieRuntimeActions.StableThingId)
						.ToArray();
					var symbiantInWorldPawns = Find.WorldPawns?.Contains(symbiant) == true;
					var fixturePawnIdsAfterRoute = mapRemoved
						? Array.Empty<string>()
						: SymbiantSecondMapPawns(symbiantMap).Select(ZombieRuntimeActions.StableThingId).ToArray();
					var newWorldObjects = Find.WorldObjects.AllWorldObjects
						.Where(worldObject => worldObjectsBefore.Contains(worldObject) == false)
						.Select(worldObject => new
						{
							id = worldObject.ID,
							def = worldObject.def?.defName,
							tile = worldObject.Tile.ToString()
						})
						.ToArray();
					action = new
					{
						moveHost,
						dormant,
						fixturePawnIdsBeforeRoute,
						fixturePawnIdsAfterRoute,
						worldPawnIdsAfterRoute,
						symbiantInWorldPawns,
						hediffBefore,
						hediffAfter,
						bringAlongOnGravship,
						shouldBringOnGravship,
						routeMapPawnIdsBeforeRoute,
						hostInRouteMapPawnsBeforeRoute,
						hostInWorldPawnsBeforeRoute,
						hostInjuryBefore,
						hostInjuryAfter,
						hostDead = host.Dead,
						hostSpawned = host.Spawned,
						hostMapId = host.Map?.uniqueID ?? -1,
						currentMapBeforeRoute = currentMapBeforeRoute?.uniqueID ?? -1,
						currentMapAfterRoute = currentMapAfterRoute?.uniqueID ?? -1,
						hostMapStillLoaded = Find.Maps.Contains(hostMap),
						hostMapParentUnchanged = ReferenceEquals(hostMap.Parent, hostMapParentBeforeRoute),
						hostMapParentDestroyed = hostMapParentBeforeRoute?.Destroyed ?? false,
						symbiantDestroyed = symbiant.Destroyed,
						linkedAfter = ZombieRuntimeActions.StableThingId(linkedAfter),
						mapRemoved,
						parentDestroyed = secondMapFixture.parent?.Destroyed ?? true,
						newWorldObjects,
						success = ScenarioSucceeded(dormant)
							&& hediffBefore
							&& hediffAfter == false
							&& bringAlongOnGravship == false
							&& shouldBringOnGravship == false
							&& routeMapPawnIdsBeforeRoute.Length == 1
							&& routeMapPawnIdsBeforeRoute[0] == ZombieRuntimeActions.StableThingId(symbiant)
							&& hostInRouteMapPawnsBeforeRoute == false
							&& hostInWorldPawnsBeforeRoute == false
							&& host.Dead == false
							&& host.Spawned
							&& host.Map == hostMap
							&& ReferenceEquals(currentMapAfterRoute, currentMapBeforeRoute)
							&& Find.Maps.Contains(hostMap)
							&& ReferenceEquals(hostMap.Parent, hostMapParentBeforeRoute)
							&& (hostMapParentBeforeRoute?.Destroyed ?? false) == false
							&& Mathf.Approximately(hostInjuryAfter, hostInjuryBefore)
							&& symbiant.Destroyed
							&& linkedAfter == null
							&& mapRemoved
							&& (secondMapFixture.parent?.Destroyed ?? true)
							&& fixturePawnIdsAfterRoute.Length == 0
							&& symbiantInWorldPawns == false
							&& worldPawnIdsAfterRoute.Length == 0
					};
				}
			}
			catch (Exception ex)
			{
				error = ex.ToString();
			}

			var recovery = RecoverSymbiantGravshipAbandonScenario(secondMapFixture, symbiant, host, worldObjectsBefore, cleanup);
			return new
			{
				success = error == null && ScenarioSucceeded(action) && ScenarioSucceeded(recovery),
				skipped = false,
				sourcePath = "GravshipUtility.AbandonMap -> MapParent.Abandon -> Zombieland gravship/map-abandon prefixes",
				error,
				fixtureSetup,
				action,
				recovery
			};
		}

		static object RunSymbiantDirectDeinitScenario(Map hostMap, SymbiantSecondMapFixture secondMapFixture)
		{
			var symbiantMap = secondMapFixture?.map;
			if (hostMap == null || symbiantMap == null || Find.Maps.Contains(symbiantMap) == false)
				return new { success = false, error = "The host or disposable Symbiant map is unavailable for the direct-deinitialization scenario." };

			SymbiantNaturalSpawnFixture fixture = null;
			ZombieSymbiant symbiant = null;
			Pawn host = null;
			object fixtureSetup = null;
			object activeBondBeforeRoute = null;
			object action = null;
			object error = null;

			try
			{
				if (TrySetupSymbiantNaturalSpawnFixture(symbiantMap, out fixture, out var fixtureError) == false)
				{
					error = fixtureError;
				}
				else
				{
					fixtureSetup = DescribeSymbiantNaturalSpawnFixture(fixture);
					symbiant = SpawnAssignedSymbiantForSeveranceContract(symbiantMap, fixture);
					host = fixture.host;
					secondMapFixture.trackedPawns.Add(symbiant);
					secondMapFixture.trackedPawns.Add(host);
					activeBondBeforeRoute = DescribeHostAvailabilityState("directDeinitLocalHost", symbiantMap, symbiant, host);

					foreach (var pawn in SymbiantSecondMapPawns(symbiantMap))
						secondMapFixture.trackedPawns.Add(pawn);
					var routeMapPawnIdsBeforeRoute = SymbiantSecondMapPawns(symbiantMap)
						.Select(ZombieRuntimeActions.StableThingId)
						.ToArray();
					var hostInRouteMapPawnsBeforeRoute = SymbiantSecondMapPawns(symbiantMap).Contains(host);
					var hostInWorldPawnsBeforeRoute = Find.WorldPawns?.Contains(host) == true;
					var hediffBefore = host.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) != null;
					var hostInjuryBefore = TotalInjurySeverity(host);
					var currentMapBeforeRoute = Current.Game.CurrentMap;
					var hostMapParentBeforeRoute = hostMap.Parent;
					var mapCountBeforeRoute = Find.Maps.Count;

					Current.Game.DeinitAndRemoveMap(symbiantMap, false);

					var mapRemoved = Find.Maps.Contains(symbiantMap) == false;
					var linkedAfter = ZombieSymbiant.LinkedSymbiantFor(host);
					var hediffAfter = host.health?.hediffSet?.GetFirstHediffOfDef(CustomDefs.SymbiantSymbiosis) != null;
					var hostInjuryAfter = TotalInjurySeverity(host);
					var currentMapAfterRoute = Current.Game.CurrentMap;
					var worldPawnIdsAfterRoute = secondMapFixture.trackedPawns
						.Where(pawn => pawn != null && Find.WorldPawns?.Contains(pawn) == true)
						.Select(ZombieRuntimeActions.StableThingId)
						.ToArray();
					var fixturePawnIdsAfterRoute = mapRemoved
						? Array.Empty<string>()
						: SymbiantSecondMapPawns(symbiantMap).Select(ZombieRuntimeActions.StableThingId).ToArray();
					var symbiantInWorldPawns = Find.WorldPawns?.Contains(symbiant) == true;
					var hostInWorldPawnsAfterRoute = Find.WorldPawns?.Contains(host) == true;
					action = new
					{
						activeBondBeforeRoute,
						manualPawnDisposalBeforeRoute = false,
						routeMapPawnIdsBeforeRoute,
						hostInRouteMapPawnsBeforeRoute,
						hostInWorldPawnsBeforeRoute,
						hostInWorldPawnsAfterRoute,
						fixturePawnIdsAfterRoute,
						worldPawnIdsAfterRoute,
						symbiantInWorldPawns,
						hediffBefore,
						hediffAfter,
						hostInjuryBefore,
						hostInjuryAfter,
						hostDead = host.Dead,
						hostSpawned = host.Spawned,
						hostMapId = host.Map?.uniqueID ?? -1,
						currentMapBeforeRoute = currentMapBeforeRoute?.uniqueID ?? -1,
						currentMapAfterRoute = currentMapAfterRoute?.uniqueID ?? -1,
						hostMapStillLoaded = Find.Maps.Contains(hostMap),
						hostMapParentUnchanged = ReferenceEquals(hostMap.Parent, hostMapParentBeforeRoute),
						hostMapParentDestroyed = hostMapParentBeforeRoute?.Destroyed ?? false,
						symbiantDestroyed = symbiant.Destroyed,
						symbiantDiscarded = symbiant.Discarded,
						linkedAfter = ZombieRuntimeActions.StableThingId(linkedAfter),
						mapCountBeforeRoute,
						mapCountAfterRoute = Find.Maps.Count,
						mapRemoved,
						parentHasMapAfterRoute = secondMapFixture.parent?.HasMap ?? false,
						success = ScenarioSucceeded(activeBondBeforeRoute)
							&& hediffBefore
							&& hediffAfter == false
							&& routeMapPawnIdsBeforeRoute.Length == 2
							&& routeMapPawnIdsBeforeRoute.Contains(ZombieRuntimeActions.StableThingId(symbiant))
							&& routeMapPawnIdsBeforeRoute.Contains(ZombieRuntimeActions.StableThingId(host))
							&& hostInRouteMapPawnsBeforeRoute
							&& hostInWorldPawnsBeforeRoute == false
							&& host.Dead == false
							&& host.Spawned == false
							&& hostInWorldPawnsAfterRoute
							&& Mathf.Approximately(hostInjuryAfter, hostInjuryBefore)
							&& ReferenceEquals(currentMapAfterRoute, currentMapBeforeRoute)
							&& Find.Maps.Contains(hostMap)
							&& ReferenceEquals(hostMap.Parent, hostMapParentBeforeRoute)
							&& (hostMapParentBeforeRoute?.Destroyed ?? false) == false
							&& symbiant.Destroyed
							&& symbiant.Discarded
							&& symbiantInWorldPawns == false
							&& linkedAfter == null
							&& mapRemoved
							&& Find.Maps.Count == mapCountBeforeRoute - 1
							&& (secondMapFixture.parent?.HasMap ?? false) == false
							&& fixturePawnIdsAfterRoute.Length == 0
							&& worldPawnIdsAfterRoute.Length == 1
							&& worldPawnIdsAfterRoute[0] == ZombieRuntimeActions.StableThingId(host)
					};
				}
			}
			catch (Exception ex)
			{
				error = ex.ToString();
			}

			return new
			{
				success = error == null && ScenarioSucceeded(action),
				skipped = false,
				sourcePath = "Game.DeinitAndRemoveMap -> Zombieland pre-deinit prefix -> MapDeiniter.Deinit/PassPawnsToWorld",
				error,
				fixtureSetup,
				action
			};
		}

		static bool HasPatchTarget(IEnumerable<object> targets, string declaringType, string methodName)
		{
			return (targets ?? Enumerable.Empty<object>())
				.Select(target => target?.GetType().GetProperty("method")?.GetValue(target) as string)
				.Any(method => method.NullOrEmpty() == false
					&& method.Contains(declaringType)
					&& method.Contains(methodName));
		}

		static object RecoverSymbiantGravshipAbandonScenario(
			SymbiantSecondMapFixture secondMapFixture,
			ZombieSymbiant symbiant,
			Pawn host,
			HashSet<WorldObject> worldObjectsBefore,
			bool cleanupRequested)
		{
			var trackedPawns = secondMapFixture?.trackedPawns ?? new HashSet<Pawn>();
			var worldPawnIdsBeforeRecovery = trackedPawns
				.Where(pawn => pawn != null && Find.WorldPawns?.Contains(pawn) == true)
				.Select(ZombieRuntimeActions.StableThingId)
				.ToArray();
			foreach (var pawn in trackedPawns.Where(pawn => pawn != null).ToArray())
			{
				if (Find.WorldPawns?.Contains(pawn) == true)
					Find.WorldPawns.RemovePawn(pawn);
				if (ReferenceEquals(pawn, host) || ReferenceEquals(pawn, symbiant))
					continue;
				if (pawn.Destroyed == false)
					pawn.Destroy(DestroyMode.Vanish);
				if (Find.WorldPawns?.Contains(pawn) == true)
					Find.WorldPawns.RemovePawn(pawn);
				if (pawn.Discarded == false)
					pawn.Discard(true);
				if (Find.WorldPawns?.Contains(pawn) == true)
					Find.WorldPawns.RemovePawn(pawn);
			}
			if (symbiant != null)
				symbiant.DebugDestroyWithoutHostTrauma();
			if (symbiant != null && Find.WorldPawns?.Contains(symbiant) == true)
				Find.WorldPawns.RemovePawn(symbiant);
			if (symbiant != null && symbiant.Discarded == false)
				symbiant.Discard(true);

			if (host != null && host.Destroyed == false)
			{
				var corpse = host.Corpse;
				if (corpse != null && corpse.Destroyed == false)
					corpse.Destroy(DestroyMode.Vanish);
				else if (host.Dead == false)
					host.Destroy(DestroyMode.Vanish);
			}
			if (host != null && Find.WorldPawns?.Contains(host) == true)
				Find.WorldPawns.RemovePawn(host);
			if (host != null && host.Discarded == false)
				host.Discard(true);

			var newWorldObjects = Find.WorldObjects.AllWorldObjects
				.Where(worldObject => worldObjectsBefore.Contains(worldObject) == false)
				.ToArray();
			foreach (var worldObject in newWorldObjects)
				if (worldObject.Destroyed == false)
					worldObject.Destroy();

			object forcedMapCleanup = null;
			if (secondMapFixture?.map != null && Find.Maps.Contains(secondMapFixture.map))
				forcedMapCleanup = CleanupSymbiantSecondMapFixture(secondMapFixture, true);
			var worldPawnIdsAfterRecovery = trackedPawns
				.Where(pawn => pawn != null && Find.WorldPawns?.Contains(pawn) == true)
				.Select(ZombieRuntimeActions.StableThingId)
				.ToArray();
			return new
			{
				success = worldPawnIdsAfterRecovery.Length == 0
					&& (secondMapFixture?.map == null || Find.Maps.Contains(secondMapFixture.map) == false)
					&& (forcedMapCleanup == null || ScenarioSucceeded(forcedMapCleanup))
					&& newWorldObjects.All(worldObject => worldObject.Destroyed),
				cleanupRequested,
				cleanupPolicy = "Always remove the destructive gravship fixture's temporary host, leaked pawns, map, and launch marker.",
				forced = true,
				worldPawnIdsBeforeRecovery,
				worldPawnIdsAfterRecovery,
				removedWorldObjects = newWorldObjects.Select(worldObject => new { id = worldObject.ID, def = worldObject.def?.defName }).ToArray(),
				forcedMapCleanup
			};
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

		static object RunSymbiantFurnishedFoundingProbe(Map map)
		{
			SymbiantExpansionFixture fixture = null;
			ZombieSymbiant symbiant = null;
			var furniture = new List<Building>();
			object action = null;
			object error = null;
			object symbiantCleanup = null;
			object fixtureCleanup = null;
			try
			{
				if (TrySetupSymbiantExpansionFixture(map, out fixture, out var fixtureError) == false)
					return fixtureError;
				var furnitureDef = new[] { "ShelfSmall", "Stool", "DiningChair" }
					.Select(defName => DefDatabase<ThingDef>.GetNamedSilentFail(defName))
					.FirstOrDefault(def => def?.category == ThingCategory.Building
						&& def.size.x == 1
						&& def.size.z == 1
						&& def.passability != Traversability.Impassable);
				if (furnitureDef == null)
					return new { success = false, error = "Could not find a passable one-cell furniture definition for the founding probe." };

				foreach (var cell in fixture.rightInterior.Cells)
				{
					var building = ThingMaker.MakeThing(
						furnitureDef,
						furnitureDef.MadeFromStuff ? GenStuff.DefaultStuffFor(furnitureDef) : null
					) as Building;
					if (building == null)
						return new { success = false, furnitureDef = furnitureDef.defName, error = "Could not create furnished-room probe furniture." };
					GenSpawn.Spawn(building, cell, map, Rot4.North, WipeMode.Vanish);
					building.SetFaction(Faction.OfPlayer);
					furniture.Add(building);
					fixture.buildings.Add(building);
				}
				map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();

				ZombieSymbiant.Spawn(map, fixture.spawnCell);
				symbiant = ZombieSymbiant.ActiveSymbiant(map);
				if (symbiant == null)
					return new { success = false, error = "Could not spawn the furnished-room founding probe Symbiant." };
				var leftFillAdded = ZombieSymbiant.AddCells(map, fixture.leftInterior.Cells.Append(fixture.doorCell));
				var allRightCellsFurnished = fixture.rightInterior.Cells.All(cell =>
					cell.Walkable(map)
					&& cell.GetThingList(map).OfType<Building>().Any(furniture.Contains));
				var bareRightCells = fixture.rightInterior.Cells.Count(cell =>
					cell.GetThingList(map).Any(thing => thing.def.category == ThingCategory.Building) == false);
				var before = symbiant.AbsoluteCells.ToHashSet();
				var pulse = symbiant.TryExpansionPulse();
				var foundedCells = symbiant.AbsoluteCells.Where(cell => before.Contains(cell) == false).ToArray();
				var foundedCell = foundedCells.Length == 1 ? foundedCells[0] : IntVec3.Invalid;
				var furnitureAtFoundedCell = foundedCell.IsValid
					? furniture.FirstOrDefault(building => building.Destroyed == false && building.OccupiedRect().Contains(foundedCell))
					: null;
				action = new
				{
					success = leftFillAdded > 0
						&& allRightCellsFurnished
						&& bareRightCells == 0
						&& pulse
						&& foundedCells.Length == 1
						&& fixture.rightInterior.Contains(foundedCell)
						&& furnitureAtFoundedCell != null
						&& furniture.All(building => building.Destroyed == false)
						&& symbiant.DebugEstablishmentAnchorCell == foundedCell
						&& symbiant.SelectionCoreCell == foundedCell,
					furnitureDef = furnitureDef.defName,
					furnitureCount = furniture.Count,
					allRightCellsFurnished,
					bareRightCells,
					leftFillAdded,
					pulse,
					foundedCells = foundedCells.Select(ZombieRuntimeActions.DescribeCell).ToArray(),
					foundedOnFurniture = ZombieRuntimeActions.StableThingId(furnitureAtFoundedCell),
					furniturePreserved = furniture.All(building => building.Destroyed == false),
					anchor = ZombieRuntimeActions.DescribeCell(symbiant.DebugEstablishmentAnchorCell),
					core = ZombieRuntimeActions.DescribeCell(symbiant.SelectionCoreCell)
				};
			}
			catch (Exception ex)
			{
				error = ex.ToString();
			}
			finally
			{
				symbiantCleanup = CleanupTemporarySymbiant(map, symbiant, true);
				fixtureCleanup = CleanupSymbiantExpansionFixture(map, fixture, true);
			}

			var activeAfterCleanup = ZombieSymbiant.ActiveSymbiant(map);
			return new
			{
				success = error == null && ScenarioSucceeded(action) && activeAfterCleanup == null,
				error,
				action,
				cleanup = new
				{
					symbiant = symbiantCleanup,
					fixture = fixtureCleanup,
					activeSymbiantAfterCleanup = ZombieRuntimeActions.StableThingId(activeAfterCleanup)
				}
			};
		}

		static object RunSymbiantExteriorWallBreachProbe(Map map)
		{
			SymbiantExpansionFixture fixture = null;
			ZombieSymbiant symbiant = null;
			Corpse feedCorpse = null;
			object action = null;
			object error = null;
			object symbiantCleanup = null;
			object fixtureCleanup = null;
			try
			{
				if (TrySetupSymbiantExpansionFixture(map, out fixture, out var fixtureError) == false)
					return fixtureError;

				fixture.door.Destroy(DestroyMode.Vanish);
				var sealedDoorWall = ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.WoodLog) as Building;
				if (sealedDoorWall == null)
					return new { success = false, error = "Could not create the exterior-wall breach fixture's sealing wall." };
				GenSpawn.Spawn(sealedDoorWall, fixture.doorCell, map, WipeMode.Vanish);
				sealedDoorWall.SetFaction(Faction.OfPlayer);
				fixture.buildings.Add(sealedDoorWall);
				map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();

				ZombieSymbiant.Spawn(map, fixture.spawnCell);
				symbiant = ZombieSymbiant.ActiveSymbiant(map);
				if (symbiant == null)
					return new { success = false, error = "Could not spawn the exterior-wall breach probe Symbiant." };
				var filledCells = ZombieSymbiant.AddCells(map, fixture.leftInterior.Cells.Concat(fixture.rightInterior.Cells));
				var allIndoorFloorsFilled = fixture.leftInterior.Cells.Concat(fixture.rightInterior.Cells).All(symbiant.ContainsCell);
				var rollbackWallsBefore = fixture.fixtureRect.Cells
					.Select(cell => cell.GetEdifice(map) as Building)
					.Where(building => building?.def?.IsWall == true && building.Destroyed == false)
					.Distinct()
					.Select(building => new
					{
						building,
						id = ZombieRuntimeActions.StableThingId(building),
						cell = building.Position,
						hitPoints = building.HitPoints,
						building.def,
						building.Stuff,
						building.Faction
					})
					.ToArray();
				var cellCountBeforeRollbackProbe = symbiant.CellCount;
				var rollbackCell = symbiant.DebugForceExteriorWallCommitRollback();
				var rolledBackOriginal = rollbackWallsBefore.FirstOrDefault(snapshot => snapshot.cell == rollbackCell);
				var restoredWall = rollbackCell.IsValid ? rollbackCell.GetEdifice(map) as Building : null;
				var cellCountAfterRollbackProbe = symbiant.CellCount;
				var authorizationAfterRollbackProbe = symbiant.ExteriorOverflowAuthorized;
				if (restoredWall != null && fixture.buildings.Contains(restoredWall) == false)
					fixture.buildings.Add(restoredWall);
				var failedCommitRolledBack = rollbackCell.IsValid
					&& rolledBackOriginal != null
					&& rolledBackOriginal.building.Destroyed
					&& restoredWall != null
					&& restoredWall.Destroyed == false
					&& restoredWall.def == rolledBackOriginal.def
					&& restoredWall.Stuff == rolledBackOriginal.Stuff
					&& restoredWall.Faction == rolledBackOriginal.Faction
					&& restoredWall.HitPoints == rolledBackOriginal.hitPoints
					&& symbiant.ContainsCell(rollbackCell) == false
					&& cellCountAfterRollbackProbe == cellCountBeforeRollbackProbe
					&& authorizationAfterRollbackProbe == false
					&& rollbackWallsBefore.Where(snapshot => snapshot.cell != rollbackCell).All(snapshot =>
						snapshot.building.Destroyed == false && snapshot.building.HitPoints == snapshot.hitPoints);
				map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();

				var wallSnapshots = fixture.fixtureRect.Cells
					.Select(cell => cell.GetEdifice(map) as Building)
					.Where(building => building?.def?.IsWall == true && building.Destroyed == false)
					.Distinct()
					.Select(building => new
					{
						building,
						id = ZombieRuntimeActions.StableThingId(building),
						cell = building.Position,
						hitPoints = building.HitPoints
					})
					.ToArray();
				var dividerSnapshots = fixture.dividerWalls
					.Where(wall => wall != null && wall.Destroyed == false)
					.Select(wall => new { wall, id = ZombieRuntimeActions.StableThingId(wall), hitPoints = wall.HitPoints })
					.ToArray();

				var feedCell = fixture.leftInterior.Cells
					.Where(cell => cell != symbiant.Position && cell.Standable(map) && cell.GetFirstPawn(map) == null)
					.DefaultIfEmpty(IntVec3.Invalid)
					.First();
				object feedError = null;
				if (feedCell.IsValid == false
					|| TryCreateSymbiantFeedCorpse(map, feedCell, true, "ZL_SymbiantFeed_WallBreach", null, out feedCorpse, out feedError) == false)
					return feedError ?? new { success = false, error = "Could not find a feed cell for the exterior-wall breach probe." };
				var expectedFeedGrowth = ZombieSymbiant.FeedGrowthCellCount(feedCorpse);
				var beforeFeed = symbiant.AbsoluteCells.ToHashSet();
				var indoorCellsBeforeFeed = beforeFeed
					.Where(cell =>
					{
						var classification = ZombieSymbiant.ClassifySymbiantCell(map, cell);
						return classification == ZombieSymbiant.SymbiantCellClass.IndoorFloor
							|| classification == ZombieSymbiant.SymbiantCellClass.Door;
					})
					.ToHashSet();
				var fed = symbiant.TryFeed(feedCorpse);
				var immediateNewCells = symbiant.AbsoluteCells.Where(cell => beforeFeed.Contains(cell) == false).ToArray();
				var pendingAfterFeed = symbiant.DebugPendingFeedGrowthPulses;
				var topologyUnsafeAfterFeed = symbiant.DebugPlacementTopologySafe == false;
				var destroyedAfterFeed = wallSnapshots.Where(snapshot => snapshot.building.Destroyed).ToArray();
				map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
				var breachCellClassAfterRebuild = immediateNewCells.Length == 1
					? ZombieSymbiant.ClassifySymbiantCell(map, immediateNewCells[0])
					: ZombieSymbiant.SymbiantCellClass.InvalidBlocked;
				var overflowAuthorizedAfterFeed = symbiant.ExteriorOverflowAuthorized;
				var transitionedSourceCells = indoorCellsBeforeFeed
					.Where(cell => ZombieSymbiant.ClassifySymbiantCell(map, cell) == ZombieSymbiant.SymbiantCellClass.ExteriorOpen)
					.ToArray();
				var authorizedAfterRebuild = symbiant.DebugAuthorizedExteriorCells.ToHashSet();
				var breachedSourceComponentAuthorized = transitionedSourceCells.Length > 0
					&& transitionedSourceCells.All(authorizedAfterRebuild.Contains);

				var beforeDeferred = symbiant.AbsoluteCells.ToHashSet();
				var deferredApplied = symbiant.DebugApplyPendingFeedGrowthPulses();
				var afterDeferred = symbiant.AbsoluteCells.ToHashSet();
				var deferredNewCells = afterDeferred.Where(cell => beforeDeferred.Contains(cell) == false).ToArray();
				var pendingAfterDeferred = symbiant.DebugPendingFeedGrowthPulses;
				var destroyedAfterDeferred = wallSnapshots.Where(snapshot => snapshot.building.Destroyed).ToArray();
				var dividerWallsPreserved = dividerSnapshots.All(snapshot =>
					snapshot.wall.Destroyed == false
					&& ZombieRuntimeActions.StableThingId(snapshot.wall) == snapshot.id
					&& snapshot.wall.HitPoints == snapshot.hitPoints);
				var immediateCellMatchesBreach = immediateNewCells.Length == 1
					&& destroyedAfterFeed.Length == 1
					&& immediateNewCells[0] == destroyedAfterFeed[0].cell;
				var deferredCellsExteriorAndConnected = deferredNewCells.Length == deferredApplied
					&& deferredNewCells.All(cell =>
						ZombieSymbiant.ClassifySymbiantCell(map, cell) == ZombieSymbiant.SymbiantCellClass.ExteriorOpen
						&& GenAdj.CardinalDirections.Any(direction => afterDeferred.Contains(cell + direction)));
				var detachedMoveSource = afterDeferred
					.Where(cell => ZombieSymbiant.ClassifySymbiantCell(map, cell) == ZombieSymbiant.SymbiantCellClass.ExteriorOpen)
					.Where(cell => cell != symbiant.Position && cell != symbiant.SelectionCoreCell)
					.Where(cell => GenAdj.CardinalDirections.Count(direction => afterDeferred.Contains(cell + direction)) == 1)
					.DefaultIfEmpty(IntVec3.Invalid)
					.First();
				var detachedMoveTarget = detachedMoveSource.IsValid
					? GenAdj.CardinalDirections
						.Select(direction => detachedMoveSource + direction)
						.Where(cell => cell.InBounds(map) && afterDeferred.Contains(cell) == false)
						.Where(cell => ZombieSymbiant.ClassifySymbiantCell(map, cell) == ZombieSymbiant.SymbiantCellClass.ExteriorOpen)
						.Where(cell => GenAdj.CardinalDirections.Count(direction => afterDeferred.Contains(cell + direction)) == 1
							&& GenAdj.CardinalDirections.Any(direction => cell + direction == detachedMoveSource))
						.DefaultIfEmpty(IntVec3.Invalid)
						.First()
					: IntVec3.Invalid;
				var footprintBeforeDetachedMove = symbiant.AbsoluteCells.ToHashSet();
				var authorizationBeforeDetachedMove = symbiant.DebugAuthorizedExteriorCells.ToHashSet();
				var detachedMoveAccepted = detachedMoveSource.IsValid
					&& detachedMoveTarget.IsValid
					&& symbiant.DebugTryExteriorMove(detachedMoveSource, detachedMoveTarget);
				var footprintAfterDetachedMove = symbiant.AbsoluteCells.ToHashSet();
				var authorizationAfterDetachedMove = symbiant.DebugAuthorizedExteriorCells.ToHashSet();
				var detachedMoveRejectedAtomically = detachedMoveSource.IsValid
					&& detachedMoveTarget.IsValid
					&& detachedMoveAccepted == false
					&& footprintBeforeDetachedMove.SetEquals(footprintAfterDetachedMove)
					&& authorizationBeforeDetachedMove.SetEquals(authorizationAfterDetachedMove);
				var totalFeedGrowth = symbiant.CellCount - beforeFeed.Count;
				action = new
				{
					success = filledCells > 0
						&& allIndoorFloorsFilled
						&& failedCommitRolledBack
						&& fed
						&& feedCorpse.Destroyed
						&& expectedFeedGrowth > 1
						&& immediateCellMatchesBreach
						&& topologyUnsafeAfterFeed
						&& pendingAfterFeed == expectedFeedGrowth - 1
							&& breachCellClassAfterRebuild == ZombieSymbiant.SymbiantCellClass.ExteriorOpen
							&& overflowAuthorizedAfterFeed
							&& breachedSourceComponentAuthorized
							&& deferredApplied == expectedFeedGrowth - 1
						&& pendingAfterDeferred == 0
						&& totalFeedGrowth == expectedFeedGrowth
						&& deferredCellsExteriorAndConnected
						&& detachedMoveRejectedAtomically
						&& destroyedAfterDeferred.Length == 1
						&& destroyedAfterDeferred[0].id == destroyedAfterFeed[0].id
						&& dividerWallsPreserved,
					filledCells,
					allIndoorFloorsFilled,
					failureInjection = new
					{
						cell = rollbackCell.IsValid ? ZombieRuntimeActions.DescribeCell(rollbackCell) : null,
						failedCommitRolledBack,
						cellCountBefore = cellCountBeforeRollbackProbe,
						cellCountAfter = cellCountAfterRollbackProbe,
						authorizationAfter = authorizationAfterRollbackProbe,
						restoredWall = ZombieRuntimeActions.StableThingId(restoredWall)
					},
					feed = new
					{
						fed,
						feedDestroyed = feedCorpse.Destroyed,
						expectedGrowth = expectedFeedGrowth,
						immediateGrowth = immediateNewCells.Length,
						immediateCells = immediateNewCells.Select(ZombieRuntimeActions.DescribeCell).ToArray(),
						immediateCellMatchesBreach,
						topologyUnsafeAfterFeed,
						pendingAfterFeed,
						breachCellClassAfterRebuild = breachCellClassAfterRebuild.ToString(),
						overflowAuthorizedAfterFeed,
						breachedSourceComponentAuthorized,
						transitionedSourceCells = transitionedSourceCells.Select(ZombieRuntimeActions.DescribeCell).ToArray(),
						authorizedAfterRebuild = authorizedAfterRebuild.Select(ZombieRuntimeActions.DescribeCell).ToArray(),
						deferredApplied,
						deferredCells = deferredNewCells.Select(ZombieRuntimeActions.DescribeCell).ToArray(),
						pendingAfterDeferred,
						totalGrowth = totalFeedGrowth,
						deferredCellsExteriorAndConnected,
						detachedMove = new
						{
							source = detachedMoveSource.IsValid ? ZombieRuntimeActions.DescribeCell(detachedMoveSource) : null,
							target = detachedMoveTarget.IsValid ? ZombieRuntimeActions.DescribeCell(detachedMoveTarget) : null,
							accepted = detachedMoveAccepted,
							rejectedAtomically = detachedMoveRejectedAtomically,
							footprintUnchanged = footprintBeforeDetachedMove.SetEquals(footprintAfterDetachedMove),
							authorizationUnchanged = authorizationBeforeDetachedMove.SetEquals(authorizationAfterDetachedMove)
						}
					},
					breachedWallsAfterFeed = destroyedAfterFeed.Select(snapshot => new { snapshot.id, cell = ZombieRuntimeActions.DescribeCell(snapshot.cell) }).ToArray(),
					breachedWallsAfterDeferred = destroyedAfterDeferred.Select(snapshot => new { snapshot.id, cell = ZombieRuntimeActions.DescribeCell(snapshot.cell) }).ToArray(),
					dividerWallsPreserved
				};
			}
			catch (Exception ex)
			{
				error = ex.ToString();
			}
			finally
			{
				if (feedCorpse != null && feedCorpse.Destroyed == false)
					feedCorpse.Destroy(DestroyMode.Vanish);
				symbiantCleanup = CleanupTemporarySymbiant(map, symbiant, true);
				fixtureCleanup = CleanupSymbiantExpansionFixture(map, fixture, true);
			}

			var activeAfterCleanup = ZombieSymbiant.ActiveSymbiant(map);
			return new
			{
				success = error == null && ScenarioSucceeded(action) && activeAfterCleanup == null,
				error,
				action,
				cleanup = new { symbiant = symbiantCleanup, fixture = fixtureCleanup, activeSymbiantAfterCleanup = ZombieRuntimeActions.StableThingId(activeAfterCleanup) }
			};
		}

		[Tool("zombieland/symbiant_expansion_contract", Description = "Build reversible room fixtures and verify indoor spread, roof/door gating, bare-floor preference with furnished-room founding fallback, direct room founding, divider preservation, component-scoped overflow authorization, one-wall exterior breaching, rollback after a forced failed commit, deferred multi-pulse feeding after that breach, attachment-safe exterior movement, and no second breach.")]
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
			SymbiantExpansionFixture fixture = null;
			ZombieSymbiant symbiant = null;
			var completed = false;
			try
			{
				if (TrySetupSymbiantExpansionFixture(map, out fixture, out var fixtureError) == false)
					return fixtureError;

				var spreadScoring = RunSymbiantSpreadScoringProbe(map, fixture);
				var fixtureDescription = DescribeSymbiantExpansionFixture(fixture);
				var nonWallEdificeBeforeDestroyed = fixture.nonWallEdifice?.Destroyed ?? true;
				var nonWallEdificeAcceptedAsWall = fixture.nonWallEdifice != null
					&& ZombieSymbiant.BreakableConstructedWall(map, fixture.nonWallEdifice.Position) != null;

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
				var openNewCellRemembered = symbiant?.DebugIsRecentMovementCell(openNewCell) == true;

				var leftFillAdded = ZombieSymbiant.AddCells(map, fixture.leftInterior.Cells);
				var doorBeforeDestroyed = fixture.door.Destroyed;
				var topologyCapacityBefore = symbiant?.DebugCapacityEvaluationCount ?? 0;
				var topologyRoomScansBefore = symbiant?.DebugRoomCellScanCount ?? 0;
				var topologyInvalidationsBefore = symbiant?.DebugTopologyInvalidationCount ?? 0;
				var topologySettledBefore = symbiant?.DebugTopologySettledCount ?? 0;
				map.roofGrid.SetRoof(fixture.doorCell, null);
				map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
				var unroofedDoorRoofed = fixture.doorCell.Roofed(map);
				var unroofedDoorProductionValid = ZombieSymbiant.DebugIsValidSymbiantCell(map, fixture.doorCell);
				var unroofedDoorDiagnosticValid = IsValidSymbiantCellForDiagnostics(map, fixture.doorCell);
				map.roofGrid.SetRoof(fixture.doorCell, RoofDefOf.RoofConstructed);
				map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
				var doorRoofRestored = fixture.doorCell.Roofed(map);
				var topologyCapacityAfter = symbiant?.DebugCapacityEvaluationCount ?? 0;
				var topologyRoomScansAfter = symbiant?.DebugRoomCellScanCount ?? 0;
				var topologyInvalidationsAfter = symbiant?.DebugTopologyInvalidationCount ?? 0;
				var topologySettledAfter = symbiant?.DebugTopologySettledCount ?? 0;
				var topologyCallbacksCoalesced = symbiant?.DebugRoomCellMigrationRescanPending == true;
				var topologyEventPerformance = new
				{
					success = topologyCapacityAfter == topologyCapacityBefore
						&& topologyRoomScansAfter == topologyRoomScansBefore
						&& topologyInvalidationsAfter > topologyInvalidationsBefore
						&& topologySettledAfter > topologySettledBefore
						&& topologyCallbacksCoalesced,
					capacityEvaluationDelta = topologyCapacityAfter - topologyCapacityBefore,
					roomCellScanDelta = topologyRoomScansAfter - topologyRoomScansBefore,
					topologyInvalidationDelta = topologyInvalidationsAfter - topologyInvalidationsBefore,
					topologySettledDelta = topologySettledAfter - topologySettledBefore,
					oneLazyMigrationRescanPending = topologyCallbacksCoalesced
				};
				var doorAdded = ZombieSymbiant.AddCells(map, new[] { fixture.doorCell });
				var doorOccupied = symbiant?.ContainsCell(fixture.doorCell) == true;
				var doorAfterDestroyed = fixture.door.Destroyed;

				var unroofedCell = new IntVec3(fixture.leftInterior.minX, 0, fixture.leftInterior.minZ);
				var removeRelativeCell = AccessTools.Method(typeof(ZombieSymbiant), "RemoveRelativeCell");
				var removedForUnroofedProbe = symbiant != null
					&& removeRelativeCell != null
					&& (bool)removeRelativeCell.Invoke(symbiant, new object[] { unroofedCell - symbiant.Position, false });
				if (symbiant != null)
					AccessTools.Method(typeof(ZombieSymbiant), "UpdateAll")?.Invoke(symbiant, Array.Empty<object>());
				map.roofGrid.SetRoof(unroofedCell, null);
				map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
				var unroofedRoom = unroofedCell.GetRoom(map);
				var unroofedCellRoofed = unroofedCell.Roofed(map);
				var unroofedRoomDescription = DescribeRoom(unroofedRoom);
				var unroofedRoomProper = unroofedRoom?.ProperRoom;
				var unroofedRoomUsesOutdoorTemperature = unroofedRoom?.UsesOutdoorTemperature;
				var unroofedCellValid = IsValidSymbiantCellForDiagnostics(map, unroofedCell);

				var rightInteriorBefore = fixture.rightInterior.Cells.Any(cell => symbiant?.ContainsCell(cell) == true);
				var dividerBefore = fixture.dividerWalls
					.Select(wall => new { cell = ZombieRuntimeActions.DescribeCell(wall.Position), destroyed = wall.Destroyed })
					.ToArray();
				var cellsBeforeRoomFounding = symbiant?.AbsoluteCells.ToHashSet() ?? new HashSet<IntVec3>();
				var roomFoundingPulse = symbiant?.TryExpansionPulse() == true;
				var unroofedCellOccupiedAfterPulse = symbiant?.ContainsCell(unroofedCell) == true;
				var roomFoundingCells = symbiant?.AbsoluteCells
					.Where(cell => cellsBeforeRoomFounding.Contains(cell) == false)
					.ToArray() ?? [];
				var roomFoundingCell = roomFoundingCells.Length == 1 ? roomFoundingCells[0] : IntVec3.Invalid;
				var dividerWallsPreserved = fixture.dividerWalls.All(wall => wall.Destroyed == false);
				var nonWallEdificeAfterDestroyed = fixture.nonWallEdifice?.Destroyed ?? true;
				var rightFillAdded = ZombieSymbiant.AddCells(map, fixture.rightInterior.Cells);
				var secondDoorCell = new IntVec3(fixture.rightInterior.CenterCell.x, 0, fixture.fixtureRect.minZ);
				var secondDoorExteriorCell = new IntVec3(secondDoorCell.x, 0, fixture.fixtureRect.minZ - 1);
				var secondDoorWall = secondDoorCell.GetEdifice(map) as Building;
				Building_Door secondDoor = null;
				if (secondDoorWall?.def?.IsWall == true)
				{
					secondDoorWall.Destroy(DestroyMode.Vanish);
					fixture.buildings.Remove(secondDoorWall);
					secondDoor = ThingMaker.MakeThing(ThingDefOf.Door, ThingDefOf.WoodLog) as Building_Door;
					if (secondDoor != null)
					{
						GenSpawn.Spawn(secondDoor, secondDoorCell, map, WipeMode.Vanish);
						secondDoor.SetFaction(Faction.OfPlayer);
						fixture.buildings.Add(secondDoor);
					}
				}
				map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
				var secondDoorAdded = secondDoor != null ? ZombieSymbiant.AddCells(map, new[] { secondDoorCell }) : 0;
				var perimeterWalls = fixture.buildings
					.Where(building => building?.def?.IsWall == true)
					.Select(building => new
					{
						building,
						id = ZombieRuntimeActions.StableThingId(building),
						hitPoints = building.HitPoints
					})
					.ToArray();
				var cellsBeforeOverflow = symbiant?.AbsoluteCells.ToHashSet() ?? [];
				var hostEffectCellsBeforeOverflow = symbiant?.HostEffectCellCount ?? 0;
				var sharedHealthMaxBeforeOverflow = symbiant?.DamageAbsorptionBufferMax ?? 0;
				var hostBenefitsBeforeOverflow = symbiant?.HostBenefitCount ?? 0;
				var postFillPulse = symbiant?.TryExpansionPulse() == true;
				var postFillNewCells = symbiant?.AbsoluteCells.Where(cell => cellsBeforeOverflow.Contains(cell) == false).ToArray() ?? [];
				var overflowCell = postFillNewCells.Length == 1 ? postFillNewCells[0] : IntVec3.Invalid;
				var overflowCellClass = overflowCell.IsValid ? ZombieSymbiant.ClassifySymbiantCell(map, overflowCell) : ZombieSymbiant.SymbiantCellClass.InvalidBlocked;
				var overflowUsedOpenDoorRoute = overflowCell.IsValid && fixture.fixtureRect.Contains(overflowCell) == false;
				var perimeterWallsPreservedAfterOverflow = perimeterWalls.All(snapshot =>
					snapshot.building.Destroyed == false
					&& snapshot.building.HitPoints == snapshot.hitPoints
					&& ZombieRuntimeActions.StableThingId(snapshot.building) == snapshot.id);
				var hostEffectCellsAfterOverflow = symbiant?.HostEffectCellCount ?? 0;
				var sharedHealthMaxAfterOverflow = symbiant?.DamageAbsorptionBufferMax ?? 0;
				var hostBenefitsAfterOverflow = symbiant?.HostBenefitCount ?? 0;
				var overflowAuthorizedAfterAdd = symbiant?.ExteriorOverflowAuthorized == true;
				var firstDoorExteriorCell = new IntVec3(fixture.doorCell.x, 0, fixture.fixtureRect.minZ - 1);
				var unusedDoorExteriorCell = overflowCell == firstDoorExteriorCell ? secondDoorExteriorCell : firstDoorExteriorCell;
				var authorizedBeforeContinuation = symbiant?.DebugAuthorizedExteriorCells ?? [];
				var continuationCandidates = symbiant?.DebugExteriorOpenTargets() ?? [];
				var continuationCandidatesStayOnPatch = authorizedBeforeContinuation.Length > 0
					&& continuationCandidates.Length > 0
					&& continuationCandidates.All(candidate => GenAdj.CardinalDirections.Any(direction =>
						authorizedBeforeContinuation.Contains(candidate + direction)));
				var unusedDoorExcluded = unusedDoorExteriorCell.IsValid
					&& continuationCandidates.Contains(unusedDoorExteriorCell) == false;
				var cellsBeforeContinuation = symbiant?.AbsoluteCells.ToHashSet() ?? [];
				var continuationPulse = symbiant?.TryExpansionPulse() == true;
				var continuationCells = symbiant?.AbsoluteCells
					.Where(cell => cellsBeforeContinuation.Contains(cell) == false)
					.ToArray() ?? [];
				var continuationCell = continuationCells.Length == 1 ? continuationCells[0] : IntVec3.Invalid;
				var continuationTouchesAuthorizedPatch = continuationCell.IsValid
					&& GenAdj.CardinalDirections.Any(direction => authorizedBeforeContinuation.Contains(continuationCell + direction));
				var removedContinuationCell = symbiant != null
					&& continuationCell.IsValid
					&& removeRelativeCell != null
					&& (bool)removeRelativeCell.Invoke(symbiant, new object[] { continuationCell - symbiant.Position, false });
				if (removedContinuationCell)
				{
					AccessTools.Method(typeof(ZombieSymbiant), "UpdateAll")?.Invoke(symbiant, Array.Empty<object>());
					AccessTools.Method(typeof(ZombieSymbiant), "UpdateSymbiosisState")?.Invoke(symbiant, new object[] { true });
				}
				var authorizedExteriorBeforeDamage = symbiant?.DebugAuthorizedExteriorCells ?? [];
				var damageExposureCells = symbiant?.AbsoluteCells
					.Where(fixture.rightInterior.Contains)
					.ToArray() ?? [];
				var cellCountBeforeDamageExposure = symbiant?.CellCount ?? 0;
				foreach (var cell in fixture.rightInterior.Cells)
					map.roofGrid.SetRoof(cell, null);
				map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
				var damageCellsBecameExterior = damageExposureCells.Length > 0
					&& damageExposureCells.All(cell => ZombieSymbiant.ClassifySymbiantCell(map, cell) == ZombieSymbiant.SymbiantCellClass.ExteriorOpen);
				var damageExposureGrowthPulse = symbiant?.TryExpansionPulse() == true;
				var authorizedExteriorAfterDamage = symbiant?.DebugAuthorizedExteriorCells ?? [];
				var damagedComponentStayedUnauthorized = symbiant != null
					&& damageExposureCells.All(cell => symbiant.DebugIsAuthorizedExteriorCell(cell) == false);
				var overflowAuthorizationStayedScoped = authorizedExteriorBeforeDamage.ToHashSet().SetEquals(authorizedExteriorAfterDamage);
				var cellCountAfterDamageExposure = symbiant?.CellCount ?? 0;
				foreach (var cell in fixture.rightInterior.Cells)
					map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);
				map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();

				var returnVacancy = symbiant == null
					? IntVec3.Invalid
					: fixture.rightInterior.Cells
						.Where(cell =>
							symbiant.ContainsCell(cell)
							&& cell != symbiant.Position
							&& cell != symbiant.SelectionCoreCell)
						.DefaultIfEmpty(IntVec3.Invalid)
						.First();
				var removedReturnVacancy = symbiant != null
					&& returnVacancy.IsValid
					&& removeRelativeCell != null
					&& (bool)removeRelativeCell.Invoke(symbiant, new object[] { returnVacancy - symbiant.Position, false });
				if (removedReturnVacancy)
				{
					AccessTools.Method(typeof(ZombieSymbiant), "UpdateAll")?.Invoke(symbiant, Array.Empty<object>());
					AccessTools.Method(typeof(ZombieSymbiant), "UpdateSymbiosisState")?.Invoke(symbiant, new object[] { true });
				}
				var cellCountBeforeContainedGrowthProbe = symbiant?.CellCount ?? 0;
				var exteriorCellsBeforeReturn = symbiant?.AbsoluteCells.Count(cell =>
					ZombieSymbiant.ClassifySymbiantCell(map, cell) == ZombieSymbiant.SymbiantCellClass.ExteriorOpen) ?? 0;
				var containedGrowthPulse = symbiant?.TryExpansionPulse() == true;
				var cellCountAfterContainedGrowthProbe = symbiant?.CellCount ?? 0;
				ForceSymbiantRelocationPulseReady(symbiant);
				var returnRelocationPulse = InvokeSymbiantTryRelocationPulse(symbiant);
				var exteriorCellsAfterReturn = symbiant?.AbsoluteCells.Count(cell =>
					ZombieSymbiant.ClassifySymbiantCell(map, cell) == ZombieSymbiant.SymbiantCellClass.ExteriorOpen) ?? 0;
				var returnVacancyOccupied = symbiant?.ContainsCell(returnVacancy) == true;
				var overflowAuthorizationCleared = symbiant?.ExteriorOverflowAuthorized == false;
				var dividerWallsPreservedAfterBothRoomsFilled = fixture.dividerWalls.All(wall => wall.Destroyed == false);

				var newLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
					.Where(letter => beforeLetters.Contains(letter) == false)
					.ToArray();
				var letters = newLetters.Select(DescribeSymbiantDiscoveryLetter).ToArray();
				var cleanupResult = CleanupTemporarySymbiant(map, symbiant, cleanup);
				var letterCleanup = CleanupTemporaryLetters(newLetters, cleanup);
				var fixtureCleanup = CleanupSymbiantExpansionFixture(map, fixture, cleanup);
				var activeAfterCleanup = ZombieSymbiant.ActiveSymbiant(map);
				var exteriorWallBreach = cleanup && activeAfterCleanup == null
					? RunSymbiantExteriorWallBreachProbe(map)
					: new { success = cleanup == false, skipped = true, reason = "The destructive wall-breach probe requires cleanup=true and no active Symbiant." };
				var furnishedFounding = cleanup && ZombieSymbiant.ActiveSymbiant(map) == null
					? RunSymbiantFurnishedFoundingProbe(map)
					: new { success = cleanup == false, skipped = true, reason = "The furnished-room founding probe requires cleanup=true and no active Symbiant." };

				var success = ScenarioSucceeded(spreadScoring)
					&& ScenarioSucceeded(topologyEventPerformance)
					&& ScenarioSucceeded(exteriorWallBreach)
					&& ScenarioSucceeded(furnishedFounding)
					&& spawnError == null
					&& symbiant != null
					&& openPulse
					&& openNewCell.IsValid
					&& fixture.leftInterior.Contains(openNewCell)
					&& openNewCellRemembered
					&& leftFillAdded > 0
					&& doorAdded == 1
					&& doorOccupied
					&& doorBeforeDestroyed == false
					&& doorAfterDestroyed == false
					&& unroofedDoorRoofed == false
					&& unroofedDoorProductionValid == false
					&& unroofedDoorDiagnosticValid == false
					&& doorRoofRestored
					&& removedForUnroofedProbe
					&& unroofedCellRoofed == false
					&& unroofedRoomProper == true
					&& unroofedRoomUsesOutdoorTemperature == false
					&& unroofedCellValid == false
					&& unroofedCellOccupiedAfterPulse == false
					&& rightInteriorBefore == false
					&& roomFoundingPulse
					&& roomFoundingCell.IsValid
					&& fixture.rightInterior.Contains(roomFoundingCell)
					&& dividerWallsPreserved
					&& nonWallEdificeBeforeDestroyed == false
					&& nonWallEdificeAcceptedAsWall == false
					&& nonWallEdificeAfterDestroyed == false
					&& rightFillAdded > 0
					&& secondDoorWall != null
					&& secondDoorWall.Destroyed
					&& secondDoor != null
					&& secondDoor.Destroyed == false
					&& secondDoorAdded == 1
					&& postFillPulse
					&& postFillNewCells.Length == 1
					&& overflowCellClass == ZombieSymbiant.SymbiantCellClass.ExteriorOpen
					&& overflowUsedOpenDoorRoute
					&& overflowAuthorizedAfterAdd
					&& perimeterWallsPreservedAfterOverflow
					&& hostEffectCellsAfterOverflow == hostEffectCellsBeforeOverflow
					&& sharedHealthMaxAfterOverflow == sharedHealthMaxBeforeOverflow
					&& hostBenefitsAfterOverflow == hostBenefitsBeforeOverflow
					&& authorizedBeforeContinuation.Length == 1
					&& continuationCandidatesStayOnPatch
					&& unusedDoorExcluded
					&& continuationPulse
					&& continuationCells.Length == 1
					&& continuationTouchesAuthorizedPatch
					&& continuationCell != unusedDoorExteriorCell
					&& removedContinuationCell
					&& authorizedExteriorBeforeDamage.Length == 1
					&& authorizedExteriorBeforeDamage[0] == overflowCell
					&& damageCellsBecameExterior
					&& damageExposureGrowthPulse == false
					&& damagedComponentStayedUnauthorized
					&& overflowAuthorizationStayedScoped
					&& cellCountAfterDamageExposure == cellCountBeforeDamageExposure
					&& removedReturnVacancy
					&& exteriorCellsBeforeReturn == 1
					&& containedGrowthPulse == false
					&& cellCountAfterContainedGrowthProbe == cellCountBeforeContainedGrowthProbe
					&& returnRelocationPulse
					&& exteriorCellsAfterReturn == 0
					&& returnVacancyOccupied
					&& overflowAuthorizationCleared
					&& dividerWallsPreservedAfterBothRoomsFilled
					&& activeAfterCleanup == null;

				var result = new
				{
					success,
					sourcePath = "ZombieSymbiant.TryExpansionPulse -> FindExpansionTarget -> FindRoomFoundingTarget",
					spawnError,
					spreadScoring,
					topologyEventPerformance,
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
						inLeftInterior = openNewCell.IsValid && fixture.leftInterior.Contains(openNewCell),
						rememberedAgainstImmediateReversal = openNewCellRemembered
					},
					doorExpansion = new
					{
						leftFillAdded,
						added = doorAdded,
						doorCell = ZombieRuntimeActions.DescribeCell(fixture.doorCell),
						occupied = doorOccupied,
						doorBeforeDestroyed,
						doorAfterDestroyed,
						unroofedDoorRoofed,
						unroofedDoorProductionValid,
						unroofedDoorDiagnosticValid,
						doorRoofRestored
					},
					unroofedExpansion = new
					{
						cell = ZombieRuntimeActions.DescribeCell(unroofedCell),
						removedForProbe = removedForUnroofedProbe,
						roofed = unroofedCellRoofed,
						room = unroofedRoomDescription,
						roomProper = unroofedRoomProper,
						roomUsesOutdoorTemperature = unroofedRoomUsesOutdoorTemperature,
						validTarget = unroofedCellValid,
						occupiedAfterPulse = unroofedCellOccupiedAfterPulse
					},
					roomFounding = new
					{
						rightInteriorBefore,
						dividerBefore,
						pulse = roomFoundingPulse,
						cell = roomFoundingCell.IsValid ? ZombieRuntimeActions.DescribeCell(roomFoundingCell) : null,
						inRightRoom = roomFoundingCell.IsValid && fixture.rightInterior.Contains(roomFoundingCell),
						dividerWallsPreserved,
						postFillPulse,
						dividerWallsPreservedAfterBothRoomsFilled,
						nonWallEdificeBeforeDestroyed,
						nonWallEdificeAcceptedAsWall,
						nonWallEdificeAfterDestroyed,
						rightFillAdded,
						secondExteriorDoor = new
						{
							cell = ZombieRuntimeActions.DescribeCell(secondDoorCell),
							exteriorCell = ZombieRuntimeActions.DescribeCell(secondDoorExteriorCell),
							wallReplaced = secondDoorWall?.Destroyed == true,
							created = secondDoor != null,
							added = secondDoorAdded
						},
						overflow = new
						{
							pulse = postFillPulse,
							newCells = postFillNewCells.Select(ZombieRuntimeActions.DescribeCell).ToArray(),
							cellClass = overflowCellClass.ToString(),
							usedOpenDoorRoute = overflowUsedOpenDoorRoute,
							perimeterWallsPreserved = perimeterWallsPreservedAfterOverflow,
							authorizedAfterAdd = overflowAuthorizedAfterAdd,
							hostEffectCellsBefore = hostEffectCellsBeforeOverflow,
							hostEffectCellsAfter = hostEffectCellsAfterOverflow,
							sharedHealthMaxBefore = sharedHealthMaxBeforeOverflow,
							sharedHealthMaxAfter = sharedHealthMaxAfterOverflow,
							hostBenefitsBefore = hostBenefitsBeforeOverflow,
							hostBenefitsAfter = hostBenefitsAfterOverflow,
							continuation = new
							{
								candidates = continuationCandidates.Select(ZombieRuntimeActions.DescribeCell).ToArray(),
								candidatesStayOnPatch = continuationCandidatesStayOnPatch,
								unusedDoorCell = ZombieRuntimeActions.DescribeCell(unusedDoorExteriorCell),
								unusedDoorExcluded,
								pulse = continuationPulse,
								cells = continuationCells.Select(ZombieRuntimeActions.DescribeCell).ToArray(),
								touchesAuthorizedPatch = continuationTouchesAuthorizedPatch,
								removedAfterProbe = removedContinuationCell
							},
							damageExposure = new
							{
								cells = damageExposureCells.Select(ZombieRuntimeActions.DescribeCell).ToArray(),
								damageCellsBecameExterior,
								growthPulse = damageExposureGrowthPulse,
								damagedComponentStayedUnauthorized,
								authorizationStayedScoped = overflowAuthorizationStayedScoped,
								authorizedBefore = authorizedExteriorBeforeDamage.Select(ZombieRuntimeActions.DescribeCell).ToArray(),
								authorizedAfter = authorizedExteriorAfterDamage.Select(ZombieRuntimeActions.DescribeCell).ToArray(),
								cellCountBefore = cellCountBeforeDamageExposure,
								cellCountAfter = cellCountAfterDamageExposure
							}
						},
						returnToIndoor = new
						{
							vacancy = returnVacancy.IsValid ? ZombieRuntimeActions.DescribeCell(returnVacancy) : null,
							removedReturnVacancy,
							exteriorCellsBeforeReturn,
							containedGrowthPulse,
							cellCountBeforeContainedGrowthProbe,
							cellCountAfterContainedGrowthProbe,
							returnRelocationPulse,
							exteriorCellsAfterReturn,
							returnVacancyOccupied,
							overflowAuthorizationCleared
						}
					},
					letters,
					cleanup = cleanupResult,
					letterCleanup,
					fixtureCleanup,
					exteriorWallBreach,
					furnishedFounding,
					activeSymbiantAfterCleanup = ZombieRuntimeActions.StableThingId(activeAfterCleanup)
				};
				completed = true;
				return result;
			}
			finally
			{
				if (completed == false)
				{
					try
					{
						var newLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
							.Where(letter => beforeLetters.Contains(letter) == false)
							.ToArray();
						_ = CleanupTemporaryLetters(newLetters, true);
					}
					finally
					{
						try
						{
							_ = CleanupTemporarySymbiant(map, symbiant, true);
						}
						finally
						{
							_ = CleanupSymbiantExpansionFixture(map, fixture, true);
						}
					}
				}
			}
		}

		static object RunSymbiantValidPrimaryComponentProbe(Map map)
		{
			SymbiantExpansionFixture fixture = null;
			ZombieSymbiant symbiant = null;
			object action = null;
			object error = null;
			object symbiantCleanup = null;
			object fixtureCleanup = null;
			try
			{
				if (TrySetupSymbiantExpansionFixture(map, out fixture, out var fixtureError) == false)
					return fixtureError;
				ZombieSymbiant.Spawn(map, fixture.spawnCell);
				symbiant = ZombieSymbiant.ActiveSymbiant(map);
				if (symbiant == null)
					return new { success = false, error = "Could not spawn the valid-primary-component probe Symbiant." };

				var root = symbiant.Position;
				bool TouchesRoot(IntVec3 cell) => cell == root || GenAdj.CardinalDirections.Any(direction => root + direction == cell);
				var invalidComponent = fixture.leftInterior.Cells
					.Select(cell => new[] { cell, cell + IntVec3.East })
					.FirstOrDefault(pair => pair.All(fixture.leftInterior.Contains) && pair.All(cell => TouchesRoot(cell) == false));
				if (invalidComponent == null)
					return new { success = false, error = "Could not find a disconnected two-cell component for the valid-primary-component probe." };
				var addedInvalidComponent = ZombieSymbiant.AddCells(map, invalidComponent);
				var roomBeforeRoofChange = root.GetRoom(map);
				var componentsBeforeRoofChange = roomBeforeRoofChange == null ? 0 : symbiant.DebugRoomComponentCount(roomBeforeRoofChange);

				foreach (var cell in invalidComponent)
					map.roofGrid.SetRoof(cell, null);
				map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
				var roomAfterRoofChange = root.GetRoom(map);
				var roomStayedEligible = roomAfterRoofChange != null
					&& roomAfterRoofChange == invalidComponent[0].GetRoom(map)
					&& roomAfterRoofChange.ProperRoom
					&& roomAfterRoofChange.UsesOutdoorTemperature == false;
				var rootClassification = ZombieSymbiant.ClassifySymbiantCell(map, root);
				var invalidClassifications = invalidComponent
					.Select(cell => ZombieSymbiant.ClassifySymbiantCell(map, cell))
					.ToArray();
				var migrationCountBefore = symbiant.DebugInitializeRoomCellMigration();
				var queuedBefore = symbiant.DebugRoomCellMigrationCells.ToHashSet();
				var validRootRetained = queuedBefore.Contains(root) == false
					&& invalidComponent.All(queuedBefore.Contains);

				var repairSteps = new List<object>();
				var repairsSucceeded = true;
				for (var attempt = 0; symbiant.DebugRoomCellMigrationCount > 0 && attempt < invalidComponent.Length + 2; attempt++)
				{
					bool pulse;
					Rand.PushState(531901 + attempt);
					try
					{
						pulse = symbiant.DebugMovePulse();
					}
					finally
					{
						Rand.PopState();
					}
					repairsSucceeded &= pulse && symbiant.DebugLastMovePulseMigratedRoomCell;
					repairSteps.Add(new
					{
						pulse,
						migrationMove = symbiant.DebugLastMovePulseMigratedRoomCell,
						source = symbiant.DebugLastMigratedRoomCellSource.IsValid
							? ZombieRuntimeActions.DescribeCell(symbiant.DebugLastMigratedRoomCellSource)
							: null,
						destination = symbiant.DebugLastMigratedRoomCellDestination.IsValid
							? ZombieRuntimeActions.DescribeCell(symbiant.DebugLastMigratedRoomCellDestination)
							: null,
						remaining = symbiant.DebugRoomCellMigrationCount
					});
				}

				var migrationQueueDrained = symbiant.DebugRoomCellMigrationCount == 0;
				var invalidSourcesMoved = invalidComponent.All(cell => symbiant.ContainsCell(cell) == false);
				action = new
				{
					success = addedInvalidComponent == invalidComponent.Length
						&& componentsBeforeRoofChange == 2
						&& roomStayedEligible
						&& rootClassification == ZombieSymbiant.SymbiantCellClass.IndoorFloor
						&& invalidClassifications.All(classification => classification == ZombieSymbiant.SymbiantCellClass.InvalidBlocked)
						&& migrationCountBefore == invalidComponent.Length
						&& validRootRetained
						&& repairSteps.Count == invalidComponent.Length
						&& repairsSucceeded
						&& migrationQueueDrained
						&& invalidSourcesMoved
						&& symbiant.Position == root
						&& symbiant.ContainsCell(root)
						&& symbiant.CellCount == invalidComponent.Length + 1,
					root = ZombieRuntimeActions.DescribeCell(root),
					invalidComponent = invalidComponent.Select(ZombieRuntimeActions.DescribeCell).ToArray(),
					addedInvalidComponent,
					componentsBeforeRoofChange,
					roomStayedEligible,
					rootClassification = rootClassification.ToString(),
					invalidClassifications = invalidClassifications.Select(classification => classification.ToString()).ToArray(),
					migrationCountBefore,
					queuedBefore = queuedBefore.Select(ZombieRuntimeActions.DescribeCell).ToArray(),
					validRootRetained,
					repairSteps,
					repairsSucceeded,
					migrationQueueDrained,
					invalidSourcesMoved
				};
			}
			catch (Exception ex)
			{
				error = ex.ToString();
			}
			finally
			{
				symbiantCleanup = CleanupTemporarySymbiant(map, symbiant, true);
				fixtureCleanup = CleanupSymbiantExpansionFixture(map, fixture, true);
			}

			var activeAfterCleanup = ZombieSymbiant.ActiveSymbiant(map);
			return new
			{
				success = error == null && ScenarioSucceeded(action) && activeAfterCleanup == null,
				error,
				action,
				cleanup = new
				{
					symbiant = symbiantCleanup,
					fixture = fixtureCleanup,
					activeSymbiantAfterCleanup = ZombieRuntimeActions.StableThingId(activeAfterCleanup)
				}
			};
		}

		[Tool("zombieland/symbiant_multi_room_contract", Description = "Build reversible separated-room fixtures and verify the 25-percent room gate, ambient movement's empty-room exclusion, adjacent-room preference, remote patch founding, one connected patch per room, valid-component-first legacy migration, merged-room queue retirement, interaction-core handoff, component rendering, whole-body targeting, and distributed invalid-cell relocation.")]
		public static object SymbiantMultiRoomContract(
		[ToolParameter(Description = "Destroy the temporary Symbiant and room fixtures after capturing evidence.", Required = false, DefaultValue = true)] bool cleanup = true)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded." };
			var activeBefore = ZombieSymbiant.ActiveSymbiant(map);
			if (activeBefore != null)
				return new { success = false, error = "An active Symbiant already exists on the current map.", activeSymbiant = ZombieRuntimeActions.StableThingId(activeBefore) };

			var settingsSnapshot = SnapshotZombieSettings();
			var beforeLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>()).ToHashSet();
			SymbiantExpansionFixture firstFixture = null;
			SymbiantExpansionFixture remoteFixture = null;
			SymbiantExpansionFixture mergedRoomFixture = null;
			ZombieSymbiant symbiant = null;
			var completed = false;
			try
			{
				ApplyZombieSettingsOverride(settings =>
				{
					settings.showZombieEventLetters = false;
					settings.symbiantMaxCells = 120;
				});

				var firstRoot = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TrySetupSymbiantExpansionFixture(map, firstRoot, 56f, true, null, out firstFixture, out var firstFixtureError) == false)
					return firstFixtureError;
				var remoteRoot = new IntVec3(map.Size.x / 4, 0, map.Size.z / 4);
				if (TrySetupSymbiantExpansionFixture(map, remoteRoot, 48f, true, firstFixture.fixtureRect, out remoteFixture, out var remoteFixtureError) == false)
					return remoteFixtureError;

				var firstLeftRoom = firstFixture.leftInterior.CenterCell.GetRoom(map);
				var firstRightRoom = firstFixture.rightInterior.CenterCell.GetRoom(map);
				var controlledRemoteRooms = new[]
				{
					remoteFixture.leftInterior.CenterCell.GetRoom(map),
					remoteFixture.rightInterior.CenterCell.GetRoom(map)
				};
				var firstFixtureDescription = DescribeSymbiantExpansionFixture(firstFixture);
				var remoteFixtureDescription = DescribeSymbiantExpansionFixture(remoteFixture);
				var controlledRoomsSeparated = controlledRemoteRooms.All(remoteRoom => remoteRoom != null
					&& ZombieSymbiant.DebugRoomsAreAdjacent(map, firstLeftRoom, remoteRoom) == false
					&& ZombieSymbiant.DebugRoomsAreAdjacent(map, firstRightRoom, remoteRoom) == false);

				ZombieSymbiant.Spawn(map, firstFixture.spawnCell);
				symbiant = ZombieSymbiant.ActiveSymbiant(map);
				if (symbiant == null)
					return new { success = false, error = "Could not spawn the temporary multi-room Symbiant." };

				var leftRequirement = symbiant.DebugRoomEstablishmentRequirement(firstLeftRoom);
				var initialLeftCells = symbiant.DebugCellsInRoom(firstLeftRoom);
				var belowThresholdPulseCount = Mathf.Max(0, leftRequirement - initialLeftCells - 1);
				var belowThresholdPulses = new List<object>();
				var belowThresholdStayedInRoom = true;
				for (var pulseIndex = 0; pulseIndex < belowThresholdPulseCount; pulseIndex++)
				{
					var before = symbiant.AbsoluteCells.ToHashSet();
					var pulse = symbiant.TryExpansionPulse();
					var added = symbiant.AbsoluteCells.Where(cell => before.Contains(cell) == false).ToArray();
					var stayedInRoom = added.Length == 1 && added[0].GetRoom(map) == firstLeftRoom;
					belowThresholdStayedInRoom &= pulse && stayedInRoom;
					belowThresholdPulses.Add(new
					{
						pulse,
						added = added.Select(ZombieRuntimeActions.DescribeCell).ToArray(),
						stayedInActiveRoom = stayedInRoom,
						occupiedAfter = symbiant.DebugCellsInRoom(firstLeftRoom)
					});
				}

				var thresholdPulses = new List<object>();
				for (var attempt = 0; symbiant.DebugCellsInRoom(firstLeftRoom) < leftRequirement && attempt < leftRequirement + 4; attempt++)
				{
					var before = symbiant.AbsoluteCells.ToHashSet();
					var pulse = symbiant.TryExpansionPulse();
					var added = symbiant.AbsoluteCells.Where(cell => before.Contains(cell) == false).ToArray();
					thresholdPulses.Add(new
					{
						pulse,
						added = added.Select(ZombieRuntimeActions.DescribeCell).ToArray(),
						stayedInActiveRoom = added.Length == 1 && added[0].GetRoom(map) == firstLeftRoom,
						occupiedAfter = symbiant.DebugCellsInRoom(firstLeftRoom)
					});
					if (pulse == false)
						break;
				}
				var leftEstablished = symbiant.DebugCellsInRoom(firstLeftRoom) >= leftRequirement;
				var rightRoomEmptyBeforeAmbientMove = symbiant.DebugCellsInRoom(firstRightRoom) == 0;
				var connectorCellAddedForAmbientProbe = symbiant.ContainsCell(firstFixture.roomConnectorCell)
					? 0
					: ZombieSymbiant.AddCells(map, new[] { firstFixture.roomConnectorCell });
				var ambientTargetsBeforeFounding = symbiant.DebugMovementTargetCells();
				var emptyRightRoomExcludedFromAmbientTargets = ambientTargetsBeforeFounding
					.All(cell => cell.GetRoom(map) != firstRightRoom);
				Rand.PushState(914207);
				bool ambientMoveBeforeFounding;
				try
				{
					ambientMoveBeforeFounding = symbiant.DebugTrySelectionCoreWanderPulse();
				}
				finally
				{
					Rand.PopState();
				}
				var rightRoomStayedEmptyAfterAmbientMove = symbiant.DebugCellsInRoom(firstRightRoom) == 0;

				var adjacentSteps = new List<object>();
				IntVec3? firstRightCell = null;
				var jumpedRemoteBeforeAdjacent = false;
				for (var attempt = 0; firstRightCell.HasValue == false && attempt < 12; attempt++)
				{
					var before = symbiant.AbsoluteCells.ToHashSet();
					var pulse = symbiant.TryExpansionPulse();
					var added = symbiant.AbsoluteCells.Where(cell => before.Contains(cell) == false).ToArray();
					var rightCell = added
						.Where(firstFixture.rightInterior.Contains)
						.Select(cell => (IntVec3?)cell)
						.FirstOrDefault();
					if (rightCell.HasValue)
						firstRightCell = rightCell;
					jumpedRemoteBeforeAdjacent |= added.Any(cell => firstFixture.fixtureRect.Contains(cell) == false);
					adjacentSteps.Add(new
					{
						pulse,
						added = added.Select(ZombieRuntimeActions.DescribeCell).ToArray(),
						enteredAdjacentRoom = rightCell.HasValue,
						selectionCoreDestination = ZombieRuntimeActions.DescribeCell(symbiant.SelectionCoreDestinationCell)
					});
					if (pulse == false)
						break;
				}
				var adjacentCoreHandoff = firstRightCell.HasValue && symbiant.SelectionCoreDestinationCell == firstRightCell.Value;

				firstRightRoom = firstFixture.rightInterior.CenterCell.GetRoom(map);
				var rightRequirement = symbiant.DebugRoomEstablishmentRequirement(firstRightRoom);
				var rightEstablishmentPulses = new List<object>();
				var rightStayedInRoomUntilEstablished = true;
				for (var attempt = 0; symbiant.DebugCellsInRoom(firstRightRoom) < rightRequirement && attempt < rightRequirement + 4; attempt++)
				{
					var before = symbiant.AbsoluteCells.ToHashSet();
					var pulse = symbiant.TryExpansionPulse();
					var added = symbiant.AbsoluteCells.Where(cell => before.Contains(cell) == false).ToArray();
					var stayedInRoom = added.Length == 1 && added[0].GetRoom(map) == firstRightRoom;
					rightStayedInRoomUntilEstablished &= pulse && stayedInRoom;
					rightEstablishmentPulses.Add(new
					{
						pulse,
						added = added.Select(ZombieRuntimeActions.DescribeCell).ToArray(),
						stayedInActiveRoom = stayedInRoom,
						occupiedAfter = symbiant.DebugCellsInRoom(firstRightRoom)
					});
					if (pulse == false)
						break;
				}
				var rightEstablished = symbiant.DebugCellsInRoom(firstRightRoom) >= rightRequirement;

				var occupiedRoomsBeforeRemote = symbiant.AbsoluteCells
					.Select(cell => cell.GetRoom(map))
					.Where(room => room != null)
					.Distinct()
					.ToArray();
				var cellsBeforeRemote = symbiant.AbsoluteCells.ToHashSet();
				var remotePulse = symbiant.TryExpansionPulse();
				var remoteAddedCells = symbiant.AbsoluteCells.Where(cell => cellsBeforeRemote.Contains(cell) == false).ToArray();
				var remoteCell = remoteAddedCells.Length == 1 ? remoteAddedCells[0] : IntVec3.Invalid;
				var remoteRoom = remoteCell.IsValid ? remoteCell.GetRoom(map) : null;
				var remoteRoomDescription = DescribeRoom(remoteRoom);
				var remoteFoundedRoom = remoteRoom != null && occupiedRoomsBeforeRemote.Contains(remoteRoom) == false;
				var remoteWasNonAdjacent = remoteFoundedRoom && occupiedRoomsBeforeRemote.All(room => ZombieSymbiant.DebugRoomsAreAdjacent(map, room, remoteRoom) == false);
				var remoteCoreHandoff = remoteCell.IsValid && symbiant.SelectionCoreDestinationCell == remoteCell;
				var componentsAfterRemote = symbiant.DebugComponentCount;
				var renderPatchesAfterRemote = symbiant.RenderPatchCount;
				var genericTargetingParameters = new TargetingParameters
				{
					mustBeSelectable = true,
					canTargetPawns = true,
					canTargetBuildings = true,
					canTargetItems = true,
					mapObjectTargetsMustBeAutoAttackable = false
				};
				var originalBodyCell = symbiant.AbsoluteCells.FirstOrDefault(firstFixture.leftInterior.Contains);
				var originalBodyTargetable = GenUI.ThingsUnderMouse(originalBodyCell.ToVector3Shifted(), 0f, genericTargetingParameters).Contains(symbiant);
				var remoteBodyTargetable = remoteCell.IsValid
					&& GenUI.ThingsUnderMouse(remoteCell.ToVector3Shifted(), 0f, genericTargetingParameters).Contains(symbiant);
				var remoteInCombatGeometry = remoteCell.IsValid && ZombieSymbiantCombat.Cells(symbiant).Contains(remoteCell);
				var remoteInCombatBoundary = remoteCell.IsValid && ZombieSymbiantCombat.BoundaryCells(symbiant).Contains(remoteCell);
				var maxRoomComponentsAfterRemote = symbiant.DebugMaxRoomComponentCount;

				var disconnectedRepairCandidates = remoteRoom?.Cells
					.Where(cell => ZombieSymbiant.DebugIsValidSymbiantCell(map, cell)
						&& symbiant.ContainsCell(cell) == false
						&& GenAdj.CardinalDirections.All(direction => symbiant.ContainsCell(cell + direction) == false))
					.OrderByDescending(cell => cell.DistanceToSquared(remoteCell))
					.ToArray() ?? [];
				var disconnectedRepairCells = new List<IntVec3>();
				foreach (var candidate in disconnectedRepairCandidates)
				{
					if (disconnectedRepairCells.Any(existing => GenAdj.CardinalDirections.Any(direction => existing + direction == candidate)))
						continue;
					disconnectedRepairCells.Add(candidate);
					if (disconnectedRepairCells.Count == 2)
						break;
				}
				var cellsBeforeDisconnectedRepair = symbiant.CellCount;
				var disconnectedRepairCellsAdded = disconnectedRepairCells.Count(symbiant.DebugAddDisconnectedRoomCell);
				var roomComponentsBeforeRepair = remoteRoom == null ? 0 : symbiant.DebugRoomComponentCount(remoteRoom);
				var repairPulses = new List<bool>();
				var repairOrdinaryMoves = new List<bool>();
				var repairMigrationMoves = new List<bool>();
				var repairMigrationCellsRemaining = new List<int>();
				var repairRoomComponentCounts = new List<int>();
				var migratedSources = new List<IntVec3>();
				var migratedDestinations = new List<IntVec3>();
				var repairDestinationsOccupiedAfter = new List<bool>();
				var repairMovesWithoutAnimation = new List<bool>();
				for (var repairIndex = 0; repairIndex < disconnectedRepairCells.Count; repairIndex++)
				{
					bool repairPulse;
					Rand.PushState(824113 + repairIndex);
					try
					{
						repairPulse = symbiant.DebugMovePulse();
					}
					finally
					{
						Rand.PopState();
					}
					var migratedSource = symbiant.DebugLastMigratedRoomCellSource;
					var migratedDestination = symbiant.DebugLastMigratedRoomCellDestination;
					repairPulses.Add(repairPulse);
					repairOrdinaryMoves.Add(symbiant.DebugLastMovePulseOrdinaryMoved);
					repairMigrationMoves.Add(symbiant.DebugLastMovePulseMigratedRoomCell);
					repairMigrationCellsRemaining.Add(symbiant.DebugRoomCellMigrationCount);
					repairRoomComponentCounts.Add(remoteRoom == null ? 0 : symbiant.DebugRoomComponentCount(remoteRoom));
					migratedSources.Add(migratedSource);
					migratedDestinations.Add(migratedDestination);
					repairDestinationsOccupiedAfter.Add(migratedDestination.IsValid && symbiant.ContainsCell(migratedDestination));
					repairMovesWithoutAnimation.Add(
						migratedSource.IsValid
						&& migratedDestination.IsValid
						&& symbiant.DebugHasActiveCellMotionAt(migratedSource) == false
						&& symbiant.DebugHasActiveCellMotionAt(migratedDestination) == false
					);
				}
				var migrationInitializedAfterRepair = symbiant.DebugRoomCellMigrationInitialized;
				var migrationCellsRemainingAfterRepair = symbiant.DebugRoomCellMigrationCount;
				var migrationRescanPendingAfterRepair = symbiant.DebugRoomCellMigrationRescanPending;
				var roomComponentsAfterRepair = remoteRoom == null ? 0 : symbiant.DebugRoomComponentCount(remoteRoom);
				var migrationRepairPreservedCellCount = symbiant.CellCount == cellsBeforeDisconnectedRepair + disconnectedRepairCells.Count;
				var migrationRepair = new
				{
					candidates = disconnectedRepairCells.Select(ZombieRuntimeActions.DescribeCell).ToArray(),
					added = disconnectedRepairCellsAdded,
					roomComponentsBefore = roomComponentsBeforeRepair,
					migrationInitialized = migrationInitializedAfterRepair,
					migrationCellsRemaining = migrationCellsRemainingAfterRepair,
					migrationRescanPending = migrationRescanPendingAfterRepair,
					steps = repairPulses.Select((pulse, index) => new
					{
						pulse,
						ordinaryMove = repairOrdinaryMoves[index],
						migrationMove = repairMigrationMoves[index],
						migrationCellsRemaining = repairMigrationCellsRemaining[index],
						roomComponents = repairRoomComponentCounts[index],
						source = migratedSources[index].IsValid ? ZombieRuntimeActions.DescribeCell(migratedSources[index]) : null,
						destination = migratedDestinations[index].IsValid ? ZombieRuntimeActions.DescribeCell(migratedDestinations[index]) : null,
						destinationOccupiedAfter = repairDestinationsOccupiedAfter[index],
						withoutAnimation = repairMovesWithoutAnimation[index]
					}).ToArray(),
					roomComponentsAfter = roomComponentsAfterRepair,
					preservedCellCount = migrationRepairPreservedCellCount
				};

				foreach (var cell in firstFixture.leftInterior.Cells)
					map.roofGrid.SetRoof(cell, null);
				map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
				var migrationDiscoveryRearmedAfterRoomChange = symbiant.DebugRoomCellMigrationRescanPending;
				IntVec3[] InvalidCells() => symbiant.AbsoluteCells
					.Where(cell =>
					{
						var classification = ZombieSymbiant.ClassifySymbiantCell(map, cell);
						return classification != ZombieSymbiant.SymbiantCellClass.IndoorFloor
							&& classification != ZombieSymbiant.SymbiantCellClass.Door;
					})
					.ToArray();
				var invalidBefore = InvalidCells();
				var totalCellsBeforeRelocation = symbiant.CellCount;
				var relocationSteps = new List<object>();
				var relocationDestinationRoomIds = new HashSet<int>();
				var relocationCoreFollowedFounding = true;
				var maxRoomComponentsDuringRelocation = symbiant.DebugMaxRoomComponentCount;
				var successfulRelocations = 0;
				var relocationAttemptLimit = Mathf.Max(16, invalidBefore.Length * 4);
				for (var attempt = 0; InvalidCells().Length > 0 && attempt < relocationAttemptLimit; attempt++)
				{
					var before = symbiant.AbsoluteCells.ToHashSet();
					var validRoomsBefore = before
						.Where(cell => ZombieSymbiant.DebugIsValidSymbiantCell(map, cell))
						.Select(cell => cell.GetRoom(map))
						.Where(room => room != null)
						.ToHashSet();
					ForceSymbiantRelocationPulseReady(symbiant);
					var pulse = InvokeSymbiantTryRelocationPulse(symbiant);
					var after = symbiant.AbsoluteCells.ToHashSet();
					var added = after.Where(cell => before.Contains(cell) == false).ToArray();
					var target = added.Length == 1 ? added[0] : IntVec3.Invalid;
					var targetRoom = target.IsValid ? target.GetRoom(map) : null;
					var foundedRoom = targetRoom != null && validRoomsBefore.Contains(targetRoom) == false;
					var coreFollowed = foundedRoom == false || symbiant.SelectionCoreDestinationCell == target;
					relocationCoreFollowedFounding &= coreFollowed;
					if (targetRoom != null)
						relocationDestinationRoomIds.Add(targetRoom.ID);
					if (pulse && added.Length == 1)
						successfulRelocations++;
					maxRoomComponentsDuringRelocation = Mathf.Max(maxRoomComponentsDuringRelocation, symbiant.DebugMaxRoomComponentCount);
					relocationSteps.Add(new
					{
						pulse,
						target = target.IsValid ? ZombieRuntimeActions.DescribeCell(target) : null,
						targetRoom = targetRoom?.ID,
						foundedRoom,
						coreFollowed,
						invalidRemaining = InvalidCells().Length
					});
				}
				var invalidAfter = InvalidCells();
				var relocationPreservedCellCount = symbiant.CellCount == totalCellsBeforeRelocation;

				var mergedRoomRoot = new IntVec3(map.Size.x * 3 / 4, 0, map.Size.z * 3 / 4);
				if (TrySetupSymbiantExpansionFixture(map, mergedRoomRoot, 48f, true, firstFixture.fixtureRect, out mergedRoomFixture, out var mergedRoomFixtureError) == false)
					return mergedRoomFixtureError;
				var bridgeCell = mergedRoomFixture.roomConnectorCell;
				var leftBlobCells = new[] { bridgeCell + IntVec3.West, bridgeCell + IntVec3.West * 2 };
				var rightBlobCells = new[] { bridgeCell + IntVec3.East, bridgeCell + IntVec3.East * 2 };
				var mergedRoomBlobCellsAdded = ZombieSymbiant.AddCells(map, leftBlobCells.Concat(rightBlobCells));
				var leftRoomBeforeMerge = mergedRoomFixture.leftInterior.CenterCell.GetRoom(map);
				var rightRoomBeforeMerge = mergedRoomFixture.rightInterior.CenterCell.GetRoom(map);
				var separateRoomBlobsConnected = leftRoomBeforeMerge != null
					&& rightRoomBeforeMerge != null
					&& leftRoomBeforeMerge != rightRoomBeforeMerge
					&& symbiant.DebugRoomComponentCount(leftRoomBeforeMerge) == 1
					&& symbiant.DebugRoomComponentCount(rightRoomBeforeMerge) == 1;
				var migrationQueueBeforeMerge = symbiant.DebugInitializeRoomCellMigration();
				var removedMergeDividerBuildings = DestroyFixtureBuildings(
					map,
					mergedRoomFixture,
					building => building == mergedRoomFixture.roomConnector || mergedRoomFixture.dividerWalls.Contains(building)
				);
				var mergedRoomDiscoveryRearmed = symbiant.DebugRoomCellMigrationRescanPending;
				var mergedRoom = mergedRoomFixture.leftInterior.CenterCell.GetRoom(map);
				var roomsMerged = mergedRoom != null && mergedRoom == mergedRoomFixture.rightInterior.CenterCell.GetRoom(map);
				var mergedRoomComponentsBeforeBridge = mergedRoom == null ? 0 : symbiant.DebugRoomComponentCount(mergedRoom);
				var migrationQueueAfterMerge = symbiant.DebugInitializeRoomCellMigration();
				var queuedMergedRoomCells = symbiant.DebugRoomCellMigrationCells
					.Where(cell => mergedRoom != null && cell.GetRoom(map) == mergedRoom)
					.ToArray();
				var bridgeWasEmpty = symbiant.ContainsCell(bridgeCell) == false;
				var bridgeCellsAdded = ZombieSymbiant.AddCells(map, new[] { bridgeCell });
				var connectedQueueCellsRetired = symbiant.DebugRetireConnectedRoomCellMigrationComponents();
				var migrationQueueAfterBridge = symbiant.DebugRoomCellMigrationCount;
				var queuedCellsStayedInPlace = queuedMergedRoomCells.All(symbiant.ContainsCell);
				var mergedRoomComponentsAfterBridge = mergedRoom == null ? 0 : symbiant.DebugRoomComponentCount(mergedRoom);
				var mergedRoomQueueRetirement = new
				{
					fixture = DescribeSymbiantExpansionFixture(mergedRoomFixture),
					blobCellsAdded = mergedRoomBlobCellsAdded,
					separateRoomBlobsConnected,
					migrationQueueBeforeMerge,
					removedDividerBuildings = removedMergeDividerBuildings,
					discoveryRearmed = mergedRoomDiscoveryRearmed,
					roomsMerged,
					roomComponentsBeforeBridge = mergedRoomComponentsBeforeBridge,
					migrationQueueAfterMerge,
					queuedCells = queuedMergedRoomCells.Select(ZombieRuntimeActions.DescribeCell).ToArray(),
					bridgeCell = ZombieRuntimeActions.DescribeCell(bridgeCell),
					bridgeWasEmpty,
					bridgeCellsAdded,
					connectedQueueCellsRetired,
					migrationQueueAfterBridge,
					queuedCellsStayedInPlace,
					roomComponentsAfterBridge = mergedRoomComponentsAfterBridge
				};

				var newLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
					.Where(letter => beforeLetters.Contains(letter) == false)
					.ToArray();
				var symbiantCleanup = CleanupTemporarySymbiant(map, symbiant, cleanup);
				var letterCleanup = CleanupTemporaryLetters(newLetters, cleanup);
				var mergedRoomFixtureCleanup = CleanupSymbiantExpansionFixture(map, mergedRoomFixture, cleanup);
				var remoteFixtureCleanup = CleanupSymbiantExpansionFixture(map, remoteFixture, cleanup);
				var firstFixtureCleanup = CleanupSymbiantExpansionFixture(map, firstFixture, cleanup);
				var activeAfterCleanup = ZombieSymbiant.ActiveSymbiant(map);
				var validPrimaryComponent = cleanup && activeAfterCleanup == null
					? RunSymbiantValidPrimaryComponentProbe(map)
					: new { success = cleanup == false, skipped = true, reason = "The valid-primary-component probe requires cleanup=true and no active Symbiant." };
				var activeAfterAllProbes = ZombieSymbiant.ActiveSymbiant(map);

				var success = controlledRoomsSeparated
					&& leftRequirement >= 2
						&& belowThresholdStayedInRoom
						&& leftEstablished
						&& rightRoomEmptyBeforeAmbientMove
						&& connectorCellAddedForAmbientProbe == 1
						&& emptyRightRoomExcludedFromAmbientTargets
						&& rightRoomStayedEmptyAfterAmbientMove
						&& firstRightCell.HasValue
					&& jumpedRemoteBeforeAdjacent == false
					&& adjacentCoreHandoff
					&& rightStayedInRoomUntilEstablished
					&& rightEstablished
					&& remotePulse
					&& remoteAddedCells.Length == 1
					&& remoteFoundedRoom
					&& remoteWasNonAdjacent
					&& remoteCoreHandoff
					&& componentsAfterRemote >= 2
					&& renderPatchesAfterRemote >= 2
					&& maxRoomComponentsAfterRemote == 1
					&& originalBodyTargetable == false
					&& remoteBodyTargetable
					&& remoteInCombatGeometry
					&& remoteInCombatBoundary
					&& disconnectedRepairCells.Count == 2
					&& disconnectedRepairCellsAdded == 2
					&& roomComponentsBeforeRepair == 3
					&& repairPulses.All(value => value)
					&& repairOrdinaryMoves.All(value => value)
					&& repairMigrationMoves.All(value => value)
					&& migrationInitializedAfterRepair
					&& migrationCellsRemainingAfterRepair == 0
					&& migrationRescanPendingAfterRepair == false
					&& migrationDiscoveryRearmedAfterRoomChange
					&& repairMigrationCellsRemaining.SequenceEqual(new[] { 1, 0 })
					&& repairRoomComponentCounts.SequenceEqual(new[] { 2, 1 })
					&& disconnectedRepairCells.ToHashSet().SetEquals(migratedSources)
					&& migratedSources.All(source => symbiant.ContainsCell(source) == false)
					&& repairDestinationsOccupiedAfter.All(value => value)
					&& repairMovesWithoutAnimation.All(value => value)
					&& roomComponentsAfterRepair == 1
					&& migrationRepairPreservedCellCount
					&& invalidBefore.Length >= 2
					&& successfulRelocations == invalidBefore.Length
					&& invalidAfter.Length == 0
					&& relocationDestinationRoomIds.Count >= 2
					&& relocationCoreFollowedFounding
					&& relocationPreservedCellCount
					&& maxRoomComponentsDuringRelocation <= 1
					&& mergedRoomBlobCellsAdded == 4
					&& separateRoomBlobsConnected
					&& migrationQueueBeforeMerge == 0
					&& removedMergeDividerBuildings == 5
					&& mergedRoomDiscoveryRearmed
					&& roomsMerged
					&& mergedRoomComponentsBeforeBridge == 2
					&& migrationQueueAfterMerge == 2
					&& queuedMergedRoomCells.Length == 2
					&& bridgeWasEmpty
					&& bridgeCellsAdded == 1
					&& connectedQueueCellsRetired == 2
						&& migrationQueueAfterBridge == 0
						&& queuedCellsStayedInPlace
						&& mergedRoomComponentsAfterBridge == 1
						&& ScenarioSucceeded(validPrimaryComponent)
						&& (cleanup == false || activeAfterAllProbes == null);

				var result = new
				{
					success,
					sourcePath = "ZombieSymbiant.FindExpansionTarget/FindRoomFoundingTarget/TryMovePulse/TryRelocationPulse/RebuildRenderPatches",
					roomEstablishmentCoverage = ZombieSymbiant.RoomEstablishmentCoverage,
					fixtures = new
					{
						first = firstFixtureDescription,
						remote = remoteFixtureDescription,
						controlledRoomsSeparated
					},
					firstRoomEstablishment = new
					{
						requirement = leftRequirement,
						initialCells = initialLeftCells,
						belowThresholdPulses,
						thresholdPulses,
						leftEstablished
					},
					ambientFoundingGate = new
					{
						rightRoomEmptyBeforeAmbientMove,
						connectorCell = ZombieRuntimeActions.DescribeCell(firstFixture.roomConnectorCell),
						connectorCellAdded = connectorCellAddedForAmbientProbe,
						targets = ambientTargetsBeforeFounding.Select(ZombieRuntimeActions.DescribeCell).ToArray(),
						emptyRightRoomExcludedFromTargets = emptyRightRoomExcludedFromAmbientTargets,
						moveAttempted = ambientMoveBeforeFounding,
						rightRoomStayedEmptyAfterMove = rightRoomStayedEmptyAfterAmbientMove
					},
					adjacentRoom = new
					{
						steps = adjacentSteps,
						firstCell = firstRightCell.HasValue ? ZombieRuntimeActions.DescribeCell(firstRightCell.Value) : null,
						jumpedRemoteBeforeAdjacent,
						coreHandoff = adjacentCoreHandoff
					},
					secondRoomEstablishment = new
					{
						requirement = rightRequirement,
						pulses = rightEstablishmentPulses,
						rightStayedInRoomUntilEstablished,
						rightEstablished
					},
					remoteRoom = new
					{
						pulse = remotePulse,
						cell = remoteCell.IsValid ? ZombieRuntimeActions.DescribeCell(remoteCell) : null,
						room = remoteRoomDescription,
						inControlledRemoteFixture = remoteCell.IsValid && remoteFixture.fixtureRect.Contains(remoteCell),
						founded = remoteFoundedRoom,
						nonAdjacent = remoteWasNonAdjacent,
						coreHandoff = remoteCoreHandoff,
						componentCount = componentsAfterRemote,
						renderPatchCount = renderPatchesAfterRemote,
						maxRoomComponentCount = maxRoomComponentsAfterRemote,
						wholeBodyTargeting = new
						{
							originalBodyCell = ZombieRuntimeActions.DescribeCell(originalBodyCell),
							originalBodyTargetable,
							remoteBodyTargetable,
							remoteInCombatGeometry,
							remoteInCombatBoundary
						}
					},
					roomConnectivityRepair = migrationRepair,
					validPrimaryComponent,
					mergedRoomQueueRetirement,
					invalidCellRelocation = new
					{
						invalidBefore = invalidBefore.Select(ZombieRuntimeActions.DescribeCell).ToArray(),
						steps = relocationSteps,
						invalidAfter = invalidAfter.Select(ZombieRuntimeActions.DescribeCell).ToArray(),
						successfulRelocations,
						destinationRoomCount = relocationDestinationRoomIds.Count,
						destinationRoomIds = relocationDestinationRoomIds.OrderBy(id => id).ToArray(),
						coreFollowedFounding = relocationCoreFollowedFounding,
						preservedCellCount = relocationPreservedCellCount,
						maxRoomComponentCount = maxRoomComponentsDuringRelocation,
						migrationDiscoveryRearmedAfterRoomChange
					},
					cleanup = new
					{
						symbiant = symbiantCleanup,
						letters = letterCleanup,
						mergedRoomFixture = mergedRoomFixtureCleanup,
						remoteFixture = remoteFixtureCleanup,
						firstFixture = firstFixtureCleanup,
						activeSymbiantAfterCleanup = ZombieRuntimeActions.StableThingId(activeAfterAllProbes)
					}
				};
				completed = true;
				return result;
			}
			catch (Exception ex)
			{
				return new { success = false, error = ex.ToString() };
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
				if (completed == false)
				{
					_ = CleanupTemporarySymbiant(map, symbiant, true);
					var newLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
						.Where(letter => beforeLetters.Contains(letter) == false)
						.ToArray();
					_ = CleanupTemporaryLetters(newLetters, true);
					_ = CleanupSymbiantExpansionFixture(map, mergedRoomFixture, true);
					_ = CleanupSymbiantExpansionFixture(map, remoteFixture, true);
					_ = CleanupSymbiantExpansionFixture(map, firstFixture, true);
				}
			}
		}

		static object RunSymbiantSpreadScoringProbe(Map map, SymbiantExpansionFixture fixture)
		{
			ZombieSymbiant symbiant = null;
			PheromoneGrid grid = null;
			Building shelf = null;
			Thing bed = null;
			Thing diningTable = null;
			Thing workTable = null;
			Thing storage = null;
			var quietCell = IntVec3.Invalid;
			var furnitureCell = IntVec3.Invalid;
			var busyCell = IntVec3.Invalid;
			long quietTimestamp = 0;
			long furnitureTimestamp = 0;
			long busyTimestamp = 0;
			try
			{
				var root = fixture.spawnCell;
				var north = root + IntVec3.North;
				var south = root + IntVec3.South;
				var openCell = root + IntVec3.East;
				furnitureCell = root + IntVec3.West;
				var tipCell = north + IntVec3.North;
				symbiant = ZombieSymbiant.DebugSpawnForRendering(map, root, new[] { root, north, south });
				if (symbiant == null)
					return new { success = false, error = "Could not spawn the temporary Symbiant scoring probe." };

				shelf = MakeSymbiantFurnitureProbeThing("ShelfSmall") as Building;
				if (shelf == null)
					return new { success = false, error = "Could not create ShelfSmall for the Symbiant furniture scoring probe." };
				GenSpawn.Spawn(shelf, furnitureCell, map, Rot4.North, WipeMode.Vanish);
				shelf.SetFaction(Faction.OfPlayer);

				bed = MakeSymbiantFurnitureProbeThing("Bed");
				if (bed == null)
					return new { success = false, error = "Could not create Bed for the Symbiant furniture scoring probe." };
				diningTable = MakeSymbiantFurnitureProbeThing("Table1x2c");
				if (diningTable == null)
					return new { success = false, error = "Could not create Table1x2c for the Symbiant furniture scoring probe." };
				workTable = MakeSymbiantFurnitureProbeThing("TableButcher");
				if (workTable == null)
					return new { success = false, error = "Could not create TableButcher for the Symbiant furniture scoring probe." };
				storage = MakeSymbiantFurnitureProbeThing("ShelfSmall");
				if (storage == null)
					return new { success = false, error = "Could not create ShelfSmall for the Symbiant furniture scoring probe." };
				var bedMatched = ZombieSymbiant.IsSymbiantFurnitureCellThing(bed);
				var diningTableMatched = ZombieSymbiant.IsSymbiantFurnitureCellThing(diningTable);
				var workTableMatched = ZombieSymbiant.IsSymbiantFurnitureCellThing(workTable);
				var storageMatched = ZombieSymbiant.IsSymbiantFurnitureCellThing(storage);

				var openCellValid = IsValidSymbiantCellForDiagnostics(map, openCell);
				var furnitureCellValid = IsValidSymbiantCellForDiagnostics(map, furnitureCell);
				grid = map.GetGrid();
				quietCell = openCell;
				busyCell = tipCell;
				quietTimestamp = grid?.GetTimestamp(quietCell) ?? 0;
				furnitureTimestamp = grid?.GetTimestamp(furnitureCell) ?? 0;
				busyTimestamp = grid?.GetTimestamp(busyCell) ?? 0;
				grid?.SetTimestamp(quietCell, 0);
				grid?.SetTimestamp(furnitureCell, 0);
				var openScore = symbiant.DebugSpreadLocationScore(map, openCell);
				var furnitureScore = symbiant.DebugSpreadLocationScore(map, furnitureCell);
				var compactnessScore = symbiant.DebugCompactnessScore(openCell);
				var tipCompactnessScore = symbiant.DebugCompactnessScore(tipCell);

				grid?.SetTimestamp(busyCell, ZombieLand.Tools.Ticks());
				var quietCompactScore = symbiant.DebugMovementTargetScore(map, quietCell);
				var busyTipScore = symbiant.DebugMovementTargetScore(map, busyCell);

				var recentTarget = root + IntVec3.East * 2;
				var recentTargetBefore = symbiant.DebugMovementTargetScore(map, recentTarget);
				symbiant.DebugRememberMovementCell(recentTarget);
				var recentTargetAfter = symbiant.DebugMovementTargetScore(map, recentTarget);
				var recentSourceBefore = symbiant.DebugMovementSourceScore(map, north);
				symbiant.DebugRememberMovementCell(north);
				var recentSourceAfter = symbiant.DebugMovementSourceScore(map, north);
				foreach (var cell in fixture.leftInterior.Cells
					.Where(cell => cell != recentTarget && cell != north)
					.Take(ZombieSymbiant.RecentMovementCellCapacity))
					symbiant.DebugRememberMovementCell(cell);
				var oldestCellEvicted = symbiant.DebugIsRecentMovementCell(recentTarget) == false;

				var success = openCellValid
					&& furnitureCellValid
					&& bedMatched
					&& diningTableMatched
					&& workTableMatched
					&& storageMatched
					&& openScore > furnitureScore
					&& compactnessScore > tipCompactnessScore
					&& busyTipScore > quietCompactScore
					&& recentTargetAfter < recentTargetBefore
					&& recentSourceAfter > recentSourceBefore
					&& oldestCellEvicted
					&& symbiant.RecentMovementCellCount <= ZombieSymbiant.RecentMovementCellCapacity;

				return new
				{
					success,
					furniturePreference = new
					{
						openCell = ZombieRuntimeActions.DescribeCell(openCell),
						openCellValid,
						openScore,
						furnitureCell = ZombieRuntimeActions.DescribeCell(furnitureCell),
						furnitureCellValid,
						furnitureScore,
						bedMatched,
						diningTableMatched,
						workTableMatched,
						storageMatched
					},
					compactness = new
					{
						compactCell = ZombieRuntimeActions.DescribeCell(openCell),
						compactnessScore,
						tipCell = ZombieRuntimeActions.DescribeCell(tipCell),
						tipCompactnessScore
					},
					activityOverride = new
					{
						quietCompactScore,
						busyTipScore,
						busyAreaStillPreferred = busyTipScore > quietCompactScore
					},
					recentCellHistory = new
					{
						capacity = ZombieSymbiant.RecentMovementCellCapacity,
						count = symbiant.RecentMovementCellCount,
						serialized = false,
						targetPenalty = recentTargetBefore - recentTargetAfter,
						sourcePenalty = recentSourceAfter - recentSourceBefore,
						oldestCellEvicted
					}
				};
			}
			catch (Exception ex)
			{
				return new { success = false, error = ex.ToString() };
			}
			finally
			{
				if (grid != null)
				{
					if (quietCell.IsValid)
						grid.SetTimestamp(quietCell, quietTimestamp);
					if (furnitureCell.IsValid)
						grid.SetTimestamp(furnitureCell, furnitureTimestamp);
					if (busyCell.IsValid)
						grid.SetTimestamp(busyCell, busyTimestamp);
				}
				bed?.Destroy();
				diningTable?.Destroy();
				workTable?.Destroy();
				storage?.Destroy();
				_ = CleanupTemporarySymbiant(map, symbiant, true);
				shelf?.Destroy(DestroyMode.Vanish);
				map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
			}
		}

		static Thing MakeSymbiantFurnitureProbeThing(string defName)
		{
			var def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
			return def == null ? null : ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
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

		[Tool("zombieland/symbiant_relocation_contract", Description = "Verify immediate indoor return when capacity exists, founding state during relocation-debt repayment, authorized-overflow no-room grace and dormant retry cadence, movable outdoor-cell reuse, and atomic construction repair over root and non-root Symbiant cells.")]
		public static object SymbiantRelocationContract(
			[ToolParameter(Description = "Destroy temporary symbiants, colonists, fixture buildings, and letters after capturing evidence.", Required = false, DefaultValue = true)] bool cleanup = true,
			[ToolParameter(Description = "Run only the canonical-root construction/recovery subscenario for a focused regression check.", Required = false, DefaultValue = false)] bool constructionRootOnly = false,
			[ToolParameter(Description = "Run only the exposed one-cell root returning to an available indoor room subscenario.", Required = false, DefaultValue = false)] bool availableRoomRootOnly = false)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded." };
			if (constructionRootOnly && availableRoomRootOnly)
				return new { success = false, error = "Choose only one focused relocation-contract mode." };
			var activeBefore = ZombieSymbiant.ActiveSymbiant(map);
			if (activeBefore != null)
				return new { success = false, error = "An active symbiant already exists on the current map.", activeSymbiant = ZombieRuntimeActions.StableThingId(activeBefore) };

			var settingsSnapshot = SnapshotZombieSettings();
			var beforeLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>()).ToHashSet();
			object availableRoomRootRelocation = null;
			object movableCellReuse = null;
			object relocationDebtFounding = null;
			object noRoomDormancy = null;
			object constructionNonRootBatch = null;
			object constructionRoot = null;
			object error = null;

			try
			{
				ApplyZombieSettingsOverride(settings =>
				{
					settings.showZombieEventLetters = false;
					settings.symbiantMaxCells = 80;
				});

				if (constructionRootOnly)
					constructionRoot = RunSymbiantConstructionOverlapScenario(map, cleanup, true);
				else if (availableRoomRootOnly)
					availableRoomRootRelocation = RunSymbiantAvailableRoomRootRelocationScenario(map, cleanup);
				else
				{
					availableRoomRootRelocation = RunSymbiantAvailableRoomRootRelocationScenario(map, cleanup);
					movableCellReuse = RunSymbiantMovableCellReuseScenario(map, cleanup);
					relocationDebtFounding = RunSymbiantRelocationDebtFoundingScenario(map, cleanup);
					noRoomDormancy = RunSymbiantNoRoomDormancyScenario(map, cleanup);
					constructionNonRootBatch = RunSymbiantConstructionOverlapScenario(map, cleanup, false);
					constructionRoot = RunSymbiantConstructionOverlapScenario(map, cleanup, true);
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
			var letterCleanup = CleanupTemporaryLetters(newLetters, cleanup);
			var activeAfterCleanup = ZombieSymbiant.ActiveSymbiant(map);
			var success = error == null
				&& (constructionRootOnly
					? ScenarioSucceeded(constructionRoot)
					: availableRoomRootOnly
						? ScenarioSucceeded(availableRoomRootRelocation)
						: ScenarioSucceeded(availableRoomRootRelocation)
						&& ScenarioSucceeded(movableCellReuse)
						&& ScenarioSucceeded(relocationDebtFounding)
						&& ScenarioSucceeded(noRoomDormancy)
						&& ScenarioSucceeded(constructionNonRootBatch)
						&& ScenarioSucceeded(constructionRoot))
				&& (activeAfterCleanup == null || cleanup == false);

			return new
			{
				success,
				sourcePath = "ZombieSymbiant.TryReseedIfUprooted/TryRelocationPulse/FindExpansionTarget",
				error,
				availableRoomRootRelocation,
				movableCellReuse,
				relocationDebtFounding,
				noRoomDormancy,
				constructionNonRootBatch,
				constructionRoot,
				constructionRootOnly,
				availableRoomRootOnly,
				cleanup = new
				{
					letters = letterCleanup,
					activeSymbiantAfterCleanup = ZombieRuntimeActions.StableThingId(activeAfterCleanup)
				}
			};
		}

		static object RunSymbiantAvailableRoomRootRelocationScenario(Map map, bool cleanup)
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
				var beforeOpen = DescribeSymbiantRelocationState(symbiant, host);
				var removedLeftRoomRoofCells = ClearSymbiantFixtureRoof(map, fixture.leftInterior);
				var openedBuildings = DestroyFixtureBuildings(map, fixture, building => IsAdjacentToRect(building.Position, fixture.leftInterior) && fixture.dividerWalls.Contains(building) == false);
				var afterOpen = DescribeSymbiantRelocationState(symbiant, host);
				var originalCell = symbiant.Position;
				var cellCountBefore = symbiant.CellCount;
				var reseedProbe = InvokeSymbiantTryReseedIfUprooted(symbiant);
				var afterReseedProbe = DescribeSymbiantRelocationState(symbiant, host);
				ForceSymbiantRelocationPulseReady(symbiant);
				var relocationPulse = InvokeSymbiantTryRelocationPulse(symbiant);
				var afterRelocation = DescribeSymbiantRelocationState(symbiant, host);
				var relocatedIntoRightRoom = symbiant.Spawned && fixture.rightInterior.Contains(symbiant.Position);
				var success = removedLeftRoomRoofCells > 0
					&& openedBuildings > 0
					&& cellCountBefore == 1
					&& reseedProbe == false
					&& symbiant.UprootedSinceTick < 0
					&& relocationPulse
					&& relocatedIntoRightRoom
					&& symbiant.Position != originalCell
					&& symbiant.ContainsCell(originalCell) == false
					&& symbiant.CellCount == cellCountBefore
					&& symbiant.RelocationCellDebt == 0
					&& symbiant.LinkedHost == host;
				return new
				{
					success,
					fixtureSetup,
					host = DescribeRelocationHost(host),
					initial,
					beforeOpen,
					removedLeftRoomRoofCells,
					openedBuildings,
					afterOpen,
					originalCell = ZombieRuntimeActions.DescribeCell(originalCell),
					cellCountBefore,
					reseedProbe,
					afterReseedProbe,
					relocationPulse,
					afterRelocation,
					relocatedIntoRightRoom
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
				var removedLeftRoomRoofCells = ClearSymbiantFixtureRoof(map, fixture.leftInterior);
				var openedBuildings = DestroyFixtureBuildings(map, fixture, building => IsAdjacentToRect(building.Position, fixture.leftInterior) && fixture.dividerWalls.Contains(building) == false);
				var outdoorCell = fixture.leftInterior.CenterCell;
				var rightCellsBefore = CountSymbiantCellsInRect(symbiant, fixture.rightInterior);
				var addedOutdoorCells = ZombieSymbiant.AddCells(map, new[] { outdoorCell });
				var cellsBeforePulse = symbiant.AbsoluteCells.ToHashSet();
				var beforePulse = DescribeSymbiantRelocationState(symbiant, host);
				var containedOutdoorBefore = symbiant.ContainsCell(outdoorCell);
				var cellCountBefore = symbiant.CellCount;
				ForceSymbiantRelocationPulseReady(symbiant);
				var pulse = InvokeSymbiantTryRelocationPulse(symbiant);
				var afterPulse = DescribeSymbiantRelocationState(symbiant, host);
				var cellsAfterPulse = symbiant.AbsoluteCells.ToHashSet();
				var relocationTargets = cellsAfterPulse.Where(cell => cellsBeforePulse.Contains(cell) == false).ToArray();
				var relocationTarget = relocationTargets.Length == 1 ? relocationTargets[0] : IntVec3.Invalid;
				var sourceRemembered = symbiant.DebugIsRecentMovementCell(outdoorCell);
				var targetRemembered = relocationTarget.IsValid && symbiant.DebugIsRecentMovementCell(relocationTarget);
				var containedOutdoorAfter = symbiant.ContainsCell(outdoorCell);
				var rightCellsAfter = CountSymbiantCellsInRect(symbiant, fixture.rightInterior);
				var success = removedLeftRoomRoofCells > 0
					&& openedBuildings > 0
					&& addedOutdoorCells == 1
					&& containedOutdoorBefore
					&& pulse
					&& containedOutdoorAfter == false
					&& symbiant.CellCount == cellCountBefore
					&& rightCellsAfter > rightCellsBefore
					&& relocationTargets.Length == 1
					&& sourceRemembered
					&& targetRemembered;
				return new
				{
					success,
					fixtureSetup,
					host = DescribeRelocationHost(host),
					removedLeftRoomRoofCells,
					openedBuildings,
					outdoorCell = ZombieRuntimeActions.DescribeCell(outdoorCell),
					addedOutdoorCells,
					rightCellsBefore,
					beforePulse,
					containedOutdoorBefore,
					pulse,
					afterPulse,
					relocationTarget = relocationTarget.IsValid ? ZombieRuntimeActions.DescribeCell(relocationTarget) : null,
					sourceRememberedAgainstImmediateReversal = sourceRemembered,
					targetRememberedAgainstImmediateReversal = targetRemembered,
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

		static object RunSymbiantRelocationDebtFoundingScenario(Map map, bool cleanup)
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
				host = SpawnSymbiantRelocationHost(map, fixture.leftInterior.CenterCell);
				symbiant = SpawnAssignedSymbiantForRelocationContract(map, fixture.spawnCell, host);
				var debtField = AccessTools.Field(typeof(ZombieSymbiant), "relocationCellDebt");
				var discoveryCueField = AccessTools.Field(typeof(ZombieSymbiant), "selectionCoreDiscoveryCue");
				if (debtField == null || discoveryCueField == null)
					return new { success = false, fixtureSetup, error = "Could not resolve relocation-debt founding state fields." };

				debtField.SetValue(symbiant, 1);
				discoveryCueField.SetValue(symbiant, false);
				ForceSymbiantRelocationPulseReady(symbiant);
				var coreBefore = symbiant.SelectionCoreCell;
				var anchorBefore = symbiant.DebugEstablishmentAnchorCell;
				var cellsBefore = symbiant.AbsoluteCells.ToHashSet();
				var pulse = InvokeSymbiantTryRelocationPulse(symbiant);
				var foundedCells = symbiant.AbsoluteCells.Where(cell => cellsBefore.Contains(cell) == false).ToArray();
				var foundedCell = foundedCells.Length == 1 ? foundedCells[0] : IntVec3.Invalid;
				var coreAfter = symbiant.SelectionCoreCell;
				var anchorAfter = symbiant.DebugEstablishmentAnchorCell;
				var success = pulse
					&& foundedCells.Length == 1
					&& fixture.rightInterior.Contains(foundedCell)
					&& coreBefore != foundedCell
					&& anchorBefore != foundedCell
					&& coreAfter == foundedCell
					&& anchorAfter == foundedCell
					&& symbiant.SelectionCoreMotionActive == false
					&& symbiant.RelocationCellDebt == 0
					&& symbiant.LinkedHost == host;
				return new
				{
					success,
					fixtureSetup,
					host = DescribeRelocationHost(host),
					pulse,
					debtBefore = 1,
					debtAfter = symbiant.RelocationCellDebt,
					foundedCells = foundedCells.Select(ZombieRuntimeActions.DescribeCell).ToArray(),
					foundedInEmptyRoom = foundedCell.IsValid && fixture.rightInterior.Contains(foundedCell),
					coreBefore = ZombieRuntimeActions.DescribeCell(coreBefore),
					coreAfter = ZombieRuntimeActions.DescribeCell(coreAfter),
					anchorBefore = ZombieRuntimeActions.DescribeCell(anchorBefore),
					anchorAfter = ZombieRuntimeActions.DescribeCell(anchorAfter),
					coreMotionActiveAfter = symbiant.SelectionCoreMotionActive,
					hostStillLinked = symbiant.LinkedHost == host
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
				var fillCells = fixture.leftInterior.Cells
					.Concat(fixture.rightInterior.Cells)
					.Append(fixture.doorCell)
					.Where(cell => cell.InBounds(map) && cell.Standable(map));
				var addedCells = ZombieSymbiant.AddCells(map, fillCells);
				var cellsBeforeOverflow = symbiant.AbsoluteCells.ToHashSet();
				var overflowPulse = symbiant.TryExpansionPulse();
				var overflowCells = symbiant.AbsoluteCells.Where(cell => cellsBeforeOverflow.Contains(cell) == false).ToArray();
				var overflowCell = overflowCells.Length == 1 ? overflowCells[0] : IntVec3.Invalid;
				var overflowCellClass = overflowCell.IsValid
					? ZombieSymbiant.ClassifySymbiantCell(map, overflowCell)
					: ZombieSymbiant.SymbiantCellClass.InvalidBlocked;
				var overflowAuthorizedBeforeOpen = symbiant.ExteriorOverflowAuthorized;
				var beforeOpen = DescribeSymbiantRelocationState(symbiant, host);
				var cellCountBeforeOpen = symbiant.CellCount;
				var removedRoofCells = ClearSymbiantFixtureRoof(map, fixture.leftInterior)
					+ ClearSymbiantFixtureRoof(map, fixture.rightInterior);
				var removedBuildings = DestroyFixtureBuildings(map, fixture, _ => true);
				var afterOpen = DescribeSymbiantRelocationState(symbiant, host);
				var topologyAfterOpen = new
				{
					safe = symbiant.DebugPlacementTopologySafe,
					invalidated = symbiant.DebugRoomTopologyInvalidated,
					anythingToRebuild = map.regionAndRoomUpdater.AnythingToRebuild,
					migrationInitialized = symbiant.DebugRoomCellMigrationInitialized,
					migrationRescanPending = symbiant.DebugRoomCellMigrationRescanPending
				};
				var initialReseed = InvokeSymbiantTryReseedIfUprooted(symbiant);
				var uprootedSinceTickAfterOpen = symbiant.UprootedSinceTick;
				var overflowAuthorizedAfterOpen = symbiant.ExteriorOverflowAuthorized;
				ExpireSymbiantUprootedGrace(symbiant);
				ForceSymbiantRelocationPulseReady(symbiant);
				var capacityEvaluationsBeforeDormancy = symbiant.DebugCapacityEvaluationCount;
				var reseedAfterGrace = InvokeSymbiantTryReseedIfUprooted(symbiant);
				var capacityEvaluationsAfterDormancy = symbiant.DebugCapacityEvaluationCount;
				var dormantRetryDeadline = symbiant.NextRelocationPulseTick;
				var immediateDormantRetry = InvokeSymbiantTryReseedIfUprooted(symbiant);
				var capacityEvaluationsAfterImmediateRetry = symbiant.DebugCapacityEvaluationCount;
				var deadlineAfterImmediateRetry = symbiant.NextRelocationPulseTick;
				ForceSymbiantRelocationPulseReady(symbiant);
				var dueDormantRetry = InvokeSymbiantTryReseedIfUprooted(symbiant);
				var capacityEvaluationsAfterDueRetry = symbiant.DebugCapacityEvaluationCount;
				var deadlineAfterDueRetry = symbiant.NextRelocationPulseTick;
				var expansionPulse = symbiant.TryExpansionPulse();
				var afterPulses = DescribeSymbiantRelocationState(symbiant, host);
				var success = addedCells > 0
					&& overflowPulse
					&& overflowCells.Length == 1
					&& overflowCellClass == ZombieSymbiant.SymbiantCellClass.ExteriorOpen
					&& overflowAuthorizedBeforeOpen
					&& removedRoofCells > 0
					&& removedBuildings > 0
					&& initialReseed == false
					&& uprootedSinceTickAfterOpen >= 0
						&& overflowAuthorizedAfterOpen
						&& reseedAfterGrace == false
						&& capacityEvaluationsAfterDormancy > capacityEvaluationsBeforeDormancy
						&& dormantRetryDeadline > GenTicks.TicksGame
						&& immediateDormantRetry == false
						&& capacityEvaluationsAfterImmediateRetry == capacityEvaluationsAfterDormancy
						&& deadlineAfterImmediateRetry == dormantRetryDeadline
						&& dueDormantRetry == false
						&& capacityEvaluationsAfterDueRetry > capacityEvaluationsAfterImmediateRetry
						&& deadlineAfterDueRetry > GenTicks.TicksGame
						&& expansionPulse == false
					&& symbiant.CellCount == cellCountBeforeOpen
					&& symbiant.GrowthState == "dormantNoRoom";
				return new
				{
					success,
					fixtureSetup,
					host = DescribeRelocationHost(host),
					addedCells,
					authorizedOverflow = new
					{
						pulse = overflowPulse,
						cell = overflowCell.IsValid ? ZombieRuntimeActions.DescribeCell(overflowCell) : null,
						cellClass = overflowCellClass.ToString(),
						authorizedBeforeOpen = overflowAuthorizedBeforeOpen
					},
					beforeOpen,
					removedRoofCells,
					removedBuildings,
					afterOpen,
					topologyAfterOpen,
					initialReseed,
					uprootedSinceTickAfterOpen,
					overflowAuthorizedAfterOpen,
					reseedAfterGrace,
					dormantRetryCadence = new
					{
						capacityEvaluationsBeforeDormancy,
						capacityEvaluationsAfterDormancy,
						dormantRetryDeadline,
						immediateRetry = immediateDormantRetry,
						capacityEvaluationsAfterImmediateRetry,
						deadlineAfterImmediateRetry,
						dueRetry = dueDormantRetry,
						capacityEvaluationsAfterDueRetry,
						deadlineAfterDueRetry
					},
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

		static object RunSymbiantConstructionOverlapScenario(Map map, bool cleanup, bool coverRoot)
		{
			SymbiantExpansionFixture fixture = null;
			ZombieSymbiant symbiant = null;
			Pawn host = null;
			object fixtureSetup = null;
			var spawnedWalls = new List<Building>();
			try
			{
				var searchRoot = coverRoot && cleanup == false
					? new IntVec3(map.Size.x * 3 / 4, 0, map.Size.z / 4)
					: new IntVec3(map.Size.x / 4, 0, map.Size.z * 3 / 4);
				if (TrySetupSymbiantExpansionFixture(map, searchRoot, 44f, false, null, out fixture, out var fixtureError) == false)
					return fixtureError;
				fixtureSetup = DescribeSymbiantExpansionFixture(fixture);
				host = SpawnSymbiantRelocationHost(map, fixture.rightInterior.CenterCell);
				symbiant = SpawnAssignedSymbiantForRelocationContract(map, fixture.spawnCell, host);
				var initialShape = GenAdj.CardinalDirections
					.Select(direction => symbiant.Position + direction)
					.Where(cell => fixture.leftInterior.Contains(cell) && cell.GetEdifice(map) == null)
					.ToArray();
				var initialCellsAdded = ZombieSymbiant.AddCells(map, initialShape);
				_ = symbiant.DebugInitializeRoomCellMigration();
				var rootBefore = symbiant.Position;
				var coreBefore = symbiant.SelectionCoreCell;
				var cellsBefore = symbiant.AbsoluteCells.ToHashSet();
				var targets = coverRoot
					? new[] { rootBefore }
					: cellsBefore
						.Where(cell => cell != rootBefore && cell != coreBefore && fixture.leftInterior.Contains(cell))
						.OrderBy(cell => cell.x)
						.ThenBy(cell => cell.z)
						.Take(2)
						.ToArray();
				if (targets.Length != (coverRoot ? 1 : 2))
					return new { success = false, fixtureSetup, initialCellsAdded, error = "Could not select the required covered-cell construction targets." };

				var cellCountBefore = symbiant.CellCount;
				var relocationDebtBefore = symbiant.RelocationCellDebt;
				var repairBatchesBefore = symbiant.DebugConstructionRepairBatchCount;
				var relocatedBefore = symbiant.DebugConstructionRelocatedCellCount;
				var crushedBefore = symbiant.DebugConstructionCrushedCellCount;
				var placementPlansBefore = symbiant.DebugConstructionPlacementPlanCount;
				var placementScansBefore = symbiant.DebugConstructionPlacementCandidateScanCount;
				foreach (var target in targets)
				{
					var wall = ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.WoodLog) as Building;
					if (wall == null)
						return new { success = false, fixtureSetup, error = "Could not create a construction-overlap wall." };
					GenSpawn.Spawn(wall, target, map, WipeMode.Vanish);
					wall.SetFaction(Faction.OfPlayer);
					spawnedWalls.Add(wall);
					fixture.buildings.Add(wall);
				}

				var repairPendingAfterSpawn = symbiant.Destroyed == false && symbiant.DebugConstructionRepairPending;
				var repairCompletedSynchronously = symbiant.Destroyed == false
					&& symbiant.DebugConstructionRepairBatchCount > repairBatchesBefore;
				var repairScheduledOrCompletedAfterSpawn = repairPendingAfterSpawn || repairCompletedSynchronously;
				var rootStillSpawnedAfterSpawn = symbiant.Destroyed == false && symbiant.Spawned;
				var deferredRootRecoveryResult = coverRoot && rootStillSpawnedAfterSpawn
					? symbiant.pather.TryRecoverFromUnwalkablePosition(false)
					: (bool?)null;
				var rootClassAfterSpawn = rootStillSpawnedAfterSpawn
					? ZombieSymbiant.ClassifySymbiantCell(map, symbiant.Position)
					: ZombieSymbiant.SymbiantCellClass.InvalidBlocked;
				var rootRoomAfterSpawn = rootStillSpawnedAfterSpawn ? DescribeRoom(symbiant.Position.GetRoom(map)) : null;
				var topologyAfterSpawn = new
				{
					safe = symbiant.DebugPlacementTopologySafe,
					invalidated = symbiant.DebugRoomTopologyInvalidated,
					anythingToRebuild = map.regionAndRoomUpdater.AnythingToRebuild
				};
				map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
				var rootClassAfterRebuild = symbiant.Destroyed || symbiant.Spawned == false
					? ZombieSymbiant.SymbiantCellClass.InvalidBlocked
					: ZombieSymbiant.ClassifySymbiantCell(map, symbiant.Position);
				var rootRoomAfterRebuild = symbiant.Destroyed || symbiant.Spawned == false
					? null
					: DescribeRoom(symbiant.Position.GetRoom(map));
				var topologyAfterRebuild = new
				{
					safe = symbiant.DebugPlacementTopologySafe,
					invalidated = symbiant.DebugRoomTopologyInvalidated,
					anythingToRebuild = map.regionAndRoomUpdater.AnythingToRebuild
				};
				AdvanceGameTicks(1);
				var active = ZombieSymbiant.ActiveSymbiant(map) ?? symbiant;
				var cellsAfter = active.Destroyed ? new HashSet<IntVec3>() : active.AbsoluteCells.ToHashSet();
				var addedCells = cellsAfter.Where(cell => cellsBefore.Contains(cell) == false).ToArray();
				var untouchedCellsPreserved = cellsBefore.Except(targets).All(cellsAfter.Contains);
				var coveredCellsRemoved = targets.All(cell => active.ContainsCell(cell) == false);
				var wallsPreserved = spawnedWalls.All(wall => wall.Destroyed == false && wall.Map == map && wall.HitPoints > 0);
				var repairedRootClass = active.Destroyed || active.Spawned == false
					? ZombieSymbiant.SymbiantCellClass.InvalidBlocked
					: ZombieSymbiant.ClassifySymbiantCell(map, active.Position);
				var canonicalRootValid = active.Destroyed == false
					&& active.Spawned
					&& active.ContainsCell(active.Position)
					&& repairedRootClass != ZombieSymbiant.SymbiantCellClass.InvalidBlocked
					&& repairedRootClass != ZombieSymbiant.SymbiantCellClass.IndoorIneligible;
				var noRepairMotion = targets.Concat(addedCells).All(cell => active.DebugHasActiveCellMotionAt(cell) == false);
				var repairBatchDelta = active.DebugConstructionRepairBatchCount - repairBatchesBefore;
				var relocatedDelta = active.DebugConstructionRelocatedCellCount - relocatedBefore;
				var crushedDelta = active.DebugConstructionCrushedCellCount - crushedBefore;
				var placementPlanDelta = active.DebugConstructionPlacementPlanCount - placementPlansBefore;
				var placementScanDelta = active.DebugConstructionPlacementCandidateScanCount - placementScansBefore;
				var fixtureBuildingsDestroyedAfterRepair = fixture.buildings
					.Where(building => building != null && building.Destroyed)
					.Select(building => new
					{
						def = building.def?.defName,
						cell = ZombieRuntimeActions.DescribeCell(building.Position)
					})
					.ToArray();
				var rootBehaviorCorrect = coverRoot ? active.Position != rootBefore : active.Position == rootBefore;
				var success = initialCellsAdded >= 3
					&& repairScheduledOrCompletedAfterSpawn
					&& rootStillSpawnedAfterSpawn
					&& (coverRoot == false || deferredRootRecoveryResult == false)
					&& active.Destroyed == false
					&& active.CellCount == cellCountBefore
					&& active.RelocationCellDebt == relocationDebtBefore
					&& addedCells.Length == targets.Length
					&& untouchedCellsPreserved
					&& coveredCellsRemoved
					&& wallsPreserved
					&& canonicalRootValid
					&& active.SelectionCoreValid
					&& noRepairMotion
					&& repairBatchDelta == 1
					&& relocatedDelta == targets.Length
					&& crushedDelta == 0
					&& placementPlanDelta == 1
					&& placementScanDelta > 0
					&& active.DebugConstructionRepairPending == false
					&& active.LinkedHost == host
					&& host.Dead == false
					&& rootBehaviorCorrect;
				symbiant = active;
				return new
				{
					success,
					coverRoot,
					fixtureSetup,
					initialCellsAdded,
					rootBefore = ZombieRuntimeActions.DescribeCell(rootBefore),
					rootAfter = ZombieRuntimeActions.DescribeCell(active.Position),
					coreBefore = ZombieRuntimeActions.DescribeCell(coreBefore),
					targets = targets.Select(ZombieRuntimeActions.DescribeCell).ToArray(),
					addedCells = addedCells.Select(ZombieRuntimeActions.DescribeCell).ToArray(),
					repairPendingAfterSpawn,
					repairCompletedSynchronously,
					repairScheduledOrCompletedAfterSpawn,
					rootStillSpawnedAfterSpawn,
					deferredRootRecoveryResult,
					rootClassAfterSpawn = rootClassAfterSpawn.ToString(),
					rootRoomAfterSpawn,
					topologyAfterSpawn,
					rootClassAfterRebuild = rootClassAfterRebuild.ToString(),
					rootRoomAfterRebuild,
					topologyAfterRebuild,
					cellCountBefore,
					cellCountAfter = active.CellCount,
					relocationDebtBefore,
					relocationDebtAfter = active.RelocationCellDebt,
					untouchedCellsPreserved,
					coveredCellsRemoved,
					wallsPreserved,
					canonicalRootValid,
					repairedRootClass = repairedRootClass.ToString(),
					repairedRootRoofed = active.Destroyed == false && active.Spawned && active.Position.Roofed(map),
					repairedRootRoom = active.Destroyed == false && active.Spawned ? DescribeRoom(active.Position.GetRoom(map)) : null,
					selectionCoreValid = active.SelectionCoreValid,
					noRepairMotion,
					repairBatchDelta,
					relocatedDelta,
					crushedDelta,
					placementPlanDelta,
					placementScanDelta,
					fixtureBuildingsDestroyedAfterRepair,
					repairPendingAfter = active.DebugConstructionRepairPending,
					rootBehaviorCorrect,
					hostAlive = host.Dead == false,
					hostStillLinked = active.LinkedHost == host
				};
			}
			catch (Exception ex)
			{
				return new { success = false, coverRoot, error = ex.ToString(), fixtureSetup };
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
			var graceTicks = GenDate.TicksPerHour * 4;
			if (GenTicks.TicksGame <= graceTicks)
			{
				var previousProfile = ZombieSymbiant.DebugPerfProfile;
				try
				{
					_ = ZombieSymbiant.SetDebugPerfProfile("noTick");
					AdvanceGameTicks(graceTicks + 1 - GenTicks.TicksGame);
				}
				finally
				{
					_ = ZombieSymbiant.SetDebugPerfProfile(previousProfile);
				}
			}
			AccessTools.Field(typeof(ZombieSymbiant), "uprootedSinceTick")?.SetValue(symbiant, GenTicks.TicksGame - graceTicks - 1);
		}

		static void ForceSymbiantRelocationPulseReady(ZombieSymbiant symbiant)
		{
			AccessTools.Field(typeof(ZombieSymbiant), "nextRelocationPulseTick")?.SetValue(symbiant, GenTicks.TicksGame);
		}

		static int RestoreSymbiantFixtureRoof(Map map, CellRect rect)
		{
			var restored = 0;
			foreach (var cell in rect.Cells)
				if (cell.Roofed(map) == false)
				{
					map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);
					restored++;
				}
			map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
			return restored;
		}

		static int ClearSymbiantFixtureRoof(Map map, CellRect rect)
		{
			var removed = 0;
			foreach (var cell in rect.Cells)
				if (cell.Roofed(map))
				{
					map.roofGrid.SetRoof(cell, null);
					removed++;
				}
			return removed;
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
				recentMovementCellCount = symbiant.RecentMovementCellCount,
				recentMovementCellCapacity = ZombieSymbiant.RecentMovementCellCapacity,
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
			public IntVec3 roomConnectorCell = IntVec3.Invalid;
			public Building_Door door;
			public Building_Door roomConnector;
			public Building nonWallEdifice;
			public readonly List<Building> buildings = new();
			public readonly List<Building> dividerWalls = new();
			public readonly Dictionary<IntVec3, bool> originalHome = new();
			public readonly Dictionary<IntVec3, RoofDef> originalRoof = new();
		}

		static bool TrySetupSymbiantExpansionFixture(Map map, out SymbiantExpansionFixture fixture, out object error)
		{
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			return TrySetupSymbiantExpansionFixture(map, root, 56f, false, null, out fixture, out error);
		}

		static bool TrySetupSymbiantExpansionFixture(
			Map map,
			IntVec3 searchRoot,
			float searchRadius,
			bool connectRoomsWithDoor,
			CellRect? excludedRect,
			out SymbiantExpansionFixture fixture,
			out object error)
		{
			fixture = null;
			error = null;
			if (TryFindSymbiantExpansionFixtureRoot(map, searchRoot, searchRadius, excludedRect, out var center, out error) == false)
				return false;

			var leftInterior = CellRect.FromLimits(center.x - 5, center.z - 2, center.x - 1, center.z + 2);
			var rightInterior = CellRect.FromLimits(center.x + 1, center.z - 2, center.x + 5, center.z + 2);
			var fixtureRect = CellRect.FromLimits(center.x - 6, center.z - 3, center.x + 6, center.z + 3).ClipInsideMap(map);
			var doorCell = new IntVec3(center.x - 3, 0, center.z - 3);
			var roomConnectorCell = connectRoomsWithDoor ? new IntVec3(center.x, 0, center.z) : IntVec3.Invalid;
			var nonWallEdificeCell = new IntVec3(center.x - 3, 0, center.z + 3);
			fixture = new SymbiantExpansionFixture
			{
				fixtureRect = fixtureRect,
				leftInterior = leftInterior,
				rightInterior = rightInterior,
				spawnCell = leftInterior.CenterCell,
				doorCell = doorCell,
				roomConnectorCell = roomConnectorCell
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
					if (cell == roomConnectorCell)
					{
						var connector = ThingMaker.MakeThing(doorDef, stuffDef) as Building_Door;
						if (connector == null)
						{
							error = new { success = false, error = "Could not create the Symbiant expansion fixture's room connector." };
							return false;
						}
						GenSpawn.Spawn(connector, cell, map, WipeMode.Vanish);
						connector.SetFaction(Faction.OfPlayer);
						fixture.roomConnector = connector;
						fixture.buildings.Add(connector);
						continue;
					}
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
					if (cell == nonWallEdificeCell)
					{
						var nonWallEdifice = ThingMaker.MakeThing(ThingDefOf.Cooler) as Building;
						if (nonWallEdifice == null)
						{
							error = new { success = false, error = "Could not create the Symbiant expansion fixture's non-wall edifice." };
							return false;
						}
						GenSpawn.Spawn(nonWallEdifice, cell, map, Rot4.North, WipeMode.Vanish);
						nonWallEdifice.SetFaction(Faction.OfPlayer);
						fixture.nonWallEdifice = nonWallEdifice;
						fixture.buildings.Add(nonWallEdifice);
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

		static bool TryFindSymbiantExpansionFixtureRoot(
			Map map,
			IntVec3 root,
			float radius,
			CellRect? excludedRect,
			out IntVec3 center,
			out object error)
		{
			center = IntVec3.Invalid;
			error = null;
			foreach (var candidate in GenRadial.RadialCellsAround(root, radius, true))
			{
				var rect = CellRect.FromLimits(candidate.x - 6, candidate.z - 3, candidate.x + 6, candidate.z + 3);
				if (rect.InBounds(map) == false)
					continue;
				if (excludedRect.HasValue && CellRectsOverlapWithMargin(rect, excludedRect.Value, 8))
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

		static bool CellRectsOverlapWithMargin(CellRect first, CellRect second, int margin)
		{
			return first.minX <= second.maxX + margin
				&& first.maxX >= second.minX - margin
				&& first.minZ <= second.maxZ + margin
				&& first.maxZ >= second.minZ - margin;
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
				roomConnectorCell = fixture.roomConnectorCell.IsValid ? ZombieRuntimeActions.DescribeCell(fixture.roomConnectorCell) : null,
				nonWallEdifice = fixture.nonWallEdifice == null ? null : new
				{
					cell = ZombieRuntimeActions.DescribeCell(fixture.nonWallEdifice.Position),
					def = fixture.nonWallEdifice.def?.defName,
					isWall = fixture.nonWallEdifice.def?.IsWall ?? false
				},
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
					selectionCore = DescribeSymbiantSelectionCore(symbiant),
					selectorRect = selectorRect.HasValue ? ZombieRuntimeActions.DescribeCellRect(selectorRect.Value) : null,
					selectorIsSingleCoreCell = selectorRect.HasValue
						&& selectorRect.Value.Area == 1
						&& selectorRect.Value.Contains(symbiant.SelectionCoreCell),
					occupiedDrawRect = ZombieRuntimeActions.DescribeCellRect(symbiant.OccupiedDrawRect()),
					drawSize = new { x = symbiant.DrawSize.x, z = symbiant.DrawSize.y },
					renderWorldSize = new { x = symbiant.RenderWorldSize.x, z = symbiant.RenderWorldSize.y },
					renderTextureSize = new { x = symbiant.RenderTextureWidth, y = symbiant.RenderTextureHeight },
					renderShader = symbiant.RenderShaderName,
					renderUsesSymbiantShader = symbiant.RenderUsesSymbiantShader,
					renderUsesGpuMetaballMask = symbiant.RenderUsesGpuMetaballMask,
					renderMetaballElements = symbiant.RenderMetaballElementCount,
					renderPatchCount = symbiant.RenderPatchCount,
					activeCellMotions = symbiant.ActiveCellMotionCount,
					mapSize = new { x = map.Size.x, z = map.Size.z }
				}
			};
		}

		static object DescribeSymbiantSelectionCore(ZombieSymbiant symbiant)
		{
			if (symbiant == null)
				return null;
			return new
			{
				valid = symbiant.SelectionCoreValid,
				cell = ZombieRuntimeActions.DescribeCell(symbiant.SelectionCoreCell),
				destinationCell = ZombieRuntimeActions.DescribeCell(symbiant.SelectionCoreDestinationCell),
				isLogicalCell = symbiant.ContainsCell(symbiant.SelectionCoreCell),
				isVisibleDepartureCell = symbiant.SelectionCoreMotionActive
					&& symbiant.SelectionCoreCell == symbiant.SelectionCoreMotionFromCell,
				motionActive = symbiant.SelectionCoreMotionActive,
				motionFromCell = symbiant.SelectionCoreMotionFromCell.IsValid ? ZombieRuntimeActions.DescribeCell(symbiant.SelectionCoreMotionFromCell) : null,
				motionToCell = symbiant.SelectionCoreMotionToCell.IsValid ? ZombieRuntimeActions.DescribeCell(symbiant.SelectionCoreMotionToCell) : null,
				motionEndTick = symbiant.SelectionCoreMotionEndTick,
				lastMoveTick = symbiant.SelectionCoreLastMoveTick,
				discoveryCue = symbiant.SelectionCoreDiscoveryCue,
				interactionBlend = new
				{
					hover = symbiant.SelectionCoreHoverBlend,
					selected = symbiant.SelectionCoreSelectedBlend,
					discovery = symbiant.SelectionCoreDiscoveryBlend
				}
			};
		}

		[Tool("zombieland/symbiant_selection_core_contract", Description = "Verify the Symbiant's single-cell inspection core, whole-body active manual targeting, unobscured-map tooltip gate, ordinary click-through logical cells, bounded core initialization and repeated movement, non-metaball fallback rendering, selector patch installation, and outgoing-source hit-testing while the core hands off from a removed cell.")]
		public static object SymbiantSelectionCoreContract(
			[ToolParameter(Description = "Destroy the temporary contract Symbiant after capturing evidence.", Required = false, DefaultValue = true)] bool cleanup = true)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded." };
			var activeBefore = ZombieSymbiant.ActiveSymbiant(map);
			if (activeBefore != null)
				return new { success = false, error = "An active symbiant already exists on the current map.", activeSymbiant = ZombieRuntimeActions.StableThingId(activeBefore) };

			if (TrySetupSymbiantExpansionFixture(map, out var fixture, out var fixtureError) == false)
				return fixtureError;
			var clearRoot = fixture.spawnCell;
			var shape = SymbiantCombatCrossCells(clearRoot);

			ZombieSymbiant symbiant = null;
			object error = null;
			object result = null;
			try
			{
				symbiant = ZombieSymbiant.DebugSpawnForRendering(map, clearRoot, shape);
				if (symbiant == null)
					return new { success = false, error = "Could not create the selection-core fixture." };

				var clickParams = new TargetingParameters
				{
					mustBeSelectable = true,
					canTargetPawns = true,
					canTargetBuildings = true,
					canTargetItems = true,
					mapObjectTargetsMustBeAutoAttackable = false
				};
				var initialCoreCell = symbiant.SelectionCoreCell;
				var logicalTargeting = shape.Select(cell => new
				{
					cell = ZombieRuntimeActions.DescribeCell(cell),
					isCore = cell == initialCoreCell,
					targetable = GenUI.ThingsUnderMouse(cell.ToVector3Shifted(), 0f, clickParams).Contains(symbiant)
				}).ToArray();
				var logicalCellsClickThrough = logicalTargeting.All(probe => probe.targetable == probe.isCore);
				var bounds = CellRect.FromLimits(shape.Min(cell => cell.x), shape.Min(cell => cell.z), shape.Max(cell => cell.x), shape.Max(cell => cell.z));
				var gap = bounds.Cells.FirstOrDefault(cell => symbiant.ContainsCell(cell) == false);
				var gapTargetable = gap.IsValid && GenUI.ThingsUnderMouse(gap.ToVector3Shifted(), 0f, clickParams).Contains(symbiant);
				var targeterWasActive = Find.Targeter?.IsTargeting == true;
				var targeterStarted = false;
				var manuallyTargetableCells = Array.Empty<IntVec3>();
				var manualTargetingGapTargetable = false;
				try
				{
					if (targeterWasActive == false && Find.Targeter != null)
					{
						Find.Targeter.BeginTargeting(clickParams, (LocalTargetInfo _) => { }, requiresCastedSelected: false);
						targeterStarted = Find.Targeter.IsTargeting;
						manuallyTargetableCells = shape
							.Where(cell => GenUI.TargetsAt(cell.ToVector3Shifted(), clickParams, true).Any(target => target.Thing == symbiant))
							.ToArray();
						manualTargetingGapTargetable = gap.IsValid
							&& GenUI.TargetsAt(gap.ToVector3Shifted(), clickParams, true).Any(target => target.Thing == symbiant);
					}
				}
				finally
				{
					if (targeterStarted)
						Find.Targeter.StopTargeting();
				}
				var manualTargetingCoversWholeBody = targeterWasActive == false
					&& targeterStarted
					&& manuallyTargetableCells.ToHashSet().SetEquals(shape)
					&& manualTargetingGapTargetable == false;
				var selectorRect = symbiant.CustomRectForSelector;
				var selectorPatchTarget = AccessTools.DeclaredMethod(typeof(Selector), "SelectableObjectsUnderMouse", Type.EmptyTypes);
				var selectorPatchInfo = selectorPatchTarget == null ? null : Harmony.GetPatchInfo(selectorPatchTarget);
				var selectorPatchInstalled = selectorPatchInfo?.Postfixes.Any(patch => patch.owner == "net.pardeike.zombieland") == true;
				const float tooltipProbeWidth = 1920f;
				const float tooltipProbeHeight = 1080f;
				const float tooltipProbeAlertsHeight = 96f;
				const float tooltipProbeAlertsBottom = 720f;
				bool TooltipInput(Vector2 mouse, bool windowObscured = false, bool contextMenuOpen = false) => Patches.MapInterface_MapInterfaceOnGUI_AfterMainTabs_Patch.IsUnobscuredMapInput(
					mouse,
					tooltipProbeWidth,
					tooltipProbeHeight,
					windowObscured,
					contextMenuOpen,
					tooltipProbeAlertsHeight,
					tooltipProbeAlertsBottom);
				var tooltipOpenMapAllowed = TooltipInput(new Vector2(960f, 540f));
				var tooltipWindowBlocked = TooltipInput(new Vector2(960f, 540f), true) == false;
				var tooltipContextMenuBlocked = TooltipInput(new Vector2(960f, 540f), contextMenuOpen: true) == false;
				var tooltipMainButtonsBlocked = TooltipInput(new Vector2(960f, 1060f)) == false;
				var tooltipAlertBlocked = TooltipInput(new Vector2(1900f, 680f)) == false;
				var tooltipAdjacentMapAllowed = TooltipInput(new Vector2(1700f, 680f));
				var tooltipOffscreenBlocked = TooltipInput(new Vector2(-1f, 540f)) == false;
				var tooltipInputGated = tooltipOpenMapAllowed
					&& tooltipWindowBlocked
					&& tooltipContextMenuBlocked
					&& tooltipMainButtonsBlocked
					&& tooltipAlertBlocked
					&& tooltipAdjacentMapAllowed
					&& tooltipOffscreenBlocked;
				var tooltipInput = new
				{
					gated = tooltipInputGated,
					openMapAllowed = tooltipOpenMapAllowed,
					windowBlocked = tooltipWindowBlocked,
					contextMenuBlocked = tooltipContextMenuBlocked,
					mainButtonsBlocked = tooltipMainButtonsBlocked,
					alertBlocked = tooltipAlertBlocked,
					adjacentMapAllowed = tooltipAdjacentMapAllowed,
					offscreenBlocked = tooltipOffscreenBlocked
				};

				symbiant.NotifySelectionCoreDiscoveryCue();
				var discoveryCore = DescribeSymbiantSelectionCore(symbiant);
				Find.Selector.ClearSelection();
				Find.Selector.Select(symbiant, false, false);
				var afterSelection = DescribeSymbiantSelectionCore(symbiant);
				var discoveryCueCleared = symbiant.SelectionCoreDiscoveryCue == false;

				var wanderBeforeCells = symbiant.AbsoluteCells.ToHashSet();
				var wanderBeforeCore = symbiant.SelectionCoreDestinationCell;
				var wanderMoved = symbiant.DebugTrySelectionCoreWanderPulse();
				var wanderAfterCells = symbiant.AbsoluteCells.ToHashSet();
				var wanderSources = wanderBeforeCells.Where(cell => wanderAfterCells.Contains(cell) == false).ToArray();
				var wanderTargets = wanderAfterCells.Where(cell => wanderBeforeCells.Contains(cell) == false).ToArray();
				var wanderCarriedCore = wanderMoved
					&& wanderSources.Length == 1
					&& wanderTargets.Length == 1
					&& wanderSources[0] == wanderBeforeCore
					&& symbiant.SelectionCoreDestinationCell == wanderTargets[0];
				var movingCoreTargetable = symbiant.SelectionCoreMotionActive
					&& GenUI.ThingsUnderMouse(symbiant.SelectionCoreCell.ToVector3Shifted(), 0f, clickParams).Contains(symbiant);
				var wander = new
				{
					moved = wanderMoved,
					beforeCore = ZombieRuntimeActions.DescribeCell(wanderBeforeCore),
					sources = wanderSources.Select(ZombieRuntimeActions.DescribeCell).ToArray(),
					targets = wanderTargets.Select(ZombieRuntimeActions.DescribeCell).ToArray(),
					carriedCore = wanderCarriedCore,
					movingCoreTargetable,
					core = DescribeSymbiantSelectionCore(symbiant)
				};

				var repeatedWanderBeforeCells = symbiant.AbsoluteCells.ToHashSet();
				var repeatedWanderBeforeCore = symbiant.SelectionCoreDestinationCell;
				var repeatedWanderCoreWasLastOrdered = symbiant.DebugSelectionCoreIsLastOrdered;
				var repeatedWanderMoved = symbiant.DebugTrySelectionCoreWanderPulse();
				var repeatedWanderAfterCells = symbiant.AbsoluteCells.ToHashSet();
				var repeatedWanderSources = repeatedWanderBeforeCells.Where(cell => repeatedWanderAfterCells.Contains(cell) == false).ToArray();
				var repeatedWanderTargets = repeatedWanderAfterCells.Where(cell => repeatedWanderBeforeCells.Contains(cell) == false).ToArray();
				var repeatedWanderCarriedCore = repeatedWanderMoved
					&& repeatedWanderSources.Length == 1
					&& repeatedWanderTargets.Length == 1
					&& repeatedWanderSources[0] == repeatedWanderBeforeCore
					&& symbiant.SelectionCoreDestinationCell == repeatedWanderTargets[0];
				var repeatedWanderConnectivityChecks = symbiant.DebugLastSelectionCoreWanderConnectivityChecks;
				var repeatedWanderPreferredTargets = symbiant.DebugLastSelectionCoreWanderPreferredTargets;
				var repeatedWanderWorkBounded = repeatedWanderCoreWasLastOrdered
					&& repeatedWanderPreferredTargets > 0
					&& repeatedWanderPreferredTargets <= ZombieSymbiant.SelectionCorePreferredTargetLimit
					&& repeatedWanderConnectivityChecks <= repeatedWanderPreferredTargets + 1;
				var repeatedWander = new
				{
					moved = repeatedWanderMoved,
					beforeCore = ZombieRuntimeActions.DescribeCell(repeatedWanderBeforeCore),
					coreWasLastOrdered = repeatedWanderCoreWasLastOrdered,
					sources = repeatedWanderSources.Select(ZombieRuntimeActions.DescribeCell).ToArray(),
					targets = repeatedWanderTargets.Select(ZombieRuntimeActions.DescribeCell).ToArray(),
					carriedCore = repeatedWanderCarriedCore,
					preferredTargets = repeatedWanderPreferredTargets,
					preferredTargetLimit = ZombieSymbiant.SelectionCorePreferredTargetLimit,
					connectivityChecks = repeatedWanderConnectivityChecks,
					workBounded = repeatedWanderWorkBounded,
					core = DescribeSymbiantSelectionCore(symbiant)
				};

				var handoffFrom = shape[1];
				var handoffTo = shape[3];
				var handoffStarted = symbiant.DebugBeginSelectionCoreHandoff(handoffFrom, handoffTo);
				var handoffSamples = new[] { 0f, 0.1f, 0.25f, 0.5f, 0.75f, 0.9f }
					.Select(progress =>
					{
						var applied = handoffStarted && symbiant.DebugSetSelectionCoreHandoffProgress(progress);
						var visualCenter = symbiant.SelectionCoreVisualCenterRelative;
						var hitCell = symbiant.SelectionCoreCell;
						var hitRelative = hitCell - symbiant.Position;
						var selector = symbiant.CustomRectForSelector;
						var centerInsideHitCell = Mathf.Abs(visualCenter.x - hitRelative.x) <= 0.5001f
							&& Mathf.Abs(visualCenter.y - hitRelative.z) <= 0.5001f;
						var selectorTracksHitCell = selector.HasValue
							&& selector.Value.Area == 1
							&& selector.Value.Contains(hitCell);
						var targetable = GenUI.ThingsUnderMouse(hitCell.ToVector3Shifted(), 0f, clickParams).Contains(symbiant);
						return new
						{
							progress,
							applied,
							visualCenter = new { x = visualCenter.x, z = visualCenter.y },
							hitCell = ZombieRuntimeActions.DescribeCell(hitCell),
							centerInsideHitCell,
							selectorTracksHitCell,
							targetable
						};
					})
					.ToArray();
				var handoffAligned = handoffStarted
					&& handoffFrom.DistanceToSquared(handoffTo) > 1
					&& handoffSamples.All(sample => sample.applied && sample.centerInsideHitCell && sample.selectorTracksHitCell && sample.targetable);

				var fallbackVisualCenter = symbiant.SelectionCoreVisualCenterRelative;
				var fallbackCoreCell = symbiant.SelectionCoreCell;
				var fallbackCoreRelative = fallbackCoreCell - symbiant.Position;
				var fallbackCoreDrawn = symbiant.DebugDrawSelectionCoreMetaballFallback();
				var fallbackSelector = symbiant.CustomRectForSelector;
				var fallbackLogicalTargeting = symbiant.AbsoluteCells.Select(cell => new
				{
					cell = ZombieRuntimeActions.DescribeCell(cell),
					isCore = cell == fallbackCoreCell,
					targetable = GenUI.ThingsUnderMouse(cell.ToVector3Shifted(), 0f, clickParams).Contains(symbiant)
				}).ToArray();
				var fallbackVerified = fallbackCoreDrawn
					&& fallbackCoreCell != symbiant.Position
					&& Mathf.Abs(fallbackVisualCenter.x - fallbackCoreRelative.x) <= 0.5001f
					&& Mathf.Abs(fallbackVisualCenter.y - fallbackCoreRelative.z) <= 0.5001f
					&& fallbackSelector.HasValue
					&& fallbackSelector.Value.Area == 1
					&& fallbackSelector.Value.Contains(fallbackCoreCell)
					&& fallbackLogicalTargeting.All(probe => probe.targetable == probe.isCore);

				var outgoingTo = symbiant.SelectionCoreDestinationCell;
				var outgoingFrom = IntVec3.Invalid;
				var outgoingStarted = false;
				foreach (var candidate in symbiant.AbsoluteCells.Where(cell => cell != symbiant.Position && cell != outgoingTo).ToArray())
					if (symbiant.DebugRemoveSelectionCoreForHandoff(candidate, outgoingTo))
					{
						outgoingFrom = candidate;
						outgoingStarted = true;
						break;
					}
				var outgoingEarlyProgressApplied = outgoingStarted && symbiant.DebugSetSelectionCoreHandoffProgress(0.1f);
				var outgoingEarlyHitCell = symbiant.SelectionCoreCell;
				var outgoingSourceStillLogical = outgoingFrom.IsValid && symbiant.ContainsCell(outgoingFrom);
				var outgoingEarlySelector = symbiant.CustomRectForSelector;
				var outgoingSourceTargetable = outgoingFrom.IsValid
					&& GenUI.ThingsUnderMouse(outgoingFrom.ToVector3Shifted(), 0f, clickParams).Contains(symbiant);
				var outgoingDestinationTargetableEarly = outgoingTo.IsValid
					&& GenUI.ThingsUnderMouse(outgoingTo.ToVector3Shifted(), 0f, clickParams).Contains(symbiant);
				var outgoingFeedJobCell = symbiant.SelectionCoreJobCellForClick(outgoingFrom);
				var outgoingLateProgressApplied = outgoingStarted && symbiant.DebugSetSelectionCoreHandoffProgress(0.9f);
				var outgoingLateHitCell = symbiant.SelectionCoreCell;
				var outgoingDestinationTargetableLate = outgoingTo.IsValid
					&& GenUI.ThingsUnderMouse(outgoingTo.ToVector3Shifted(), 0f, clickParams).Contains(symbiant);
				var outgoingHandoffAligned = outgoingStarted
					&& outgoingEarlyProgressApplied
					&& outgoingFrom.IsValid
					&& outgoingSourceStillLogical == false
					&& outgoingEarlyHitCell == outgoingFrom
					&& outgoingEarlySelector.HasValue
					&& outgoingEarlySelector.Value.Area == 1
					&& outgoingEarlySelector.Value.Contains(outgoingFrom)
					&& outgoingSourceTargetable
					&& outgoingDestinationTargetableEarly == false
					&& outgoingFeedJobCell == outgoingTo
					&& outgoingLateProgressApplied
					&& outgoingLateHitCell == outgoingTo
					&& outgoingDestinationTargetableLate;

				var shrinkSteps = new List<object>();
				var shrinkCoresValid = true;
				while (symbiant.Destroyed == false && symbiant.CellCount > 1)
				{
					var beforeCount = symbiant.CellCount;
					var beforeCore = symbiant.SelectionCoreCell;
					var removed = symbiant.ShrinkCells(1);
					var coreValid = symbiant.SelectionCoreValid
						&& (symbiant.ContainsCell(symbiant.SelectionCoreCell)
							|| symbiant.SelectionCoreMotionActive && symbiant.SelectionCoreCell == symbiant.SelectionCoreMotionFromCell);
					shrinkCoresValid &= coreValid;
					shrinkSteps.Add(new
					{
						beforeCount,
						afterCount = symbiant.CellCount,
						beforeCore = ZombieRuntimeActions.DescribeCell(beforeCore),
						removed,
						coreValid,
						core = DescribeSymbiantSelectionCore(symbiant)
					});
					if (removed == 0)
						break;
				}

				var initializationProbeAdded = symbiant.DebugReinitializeSelectionCoreForScaleProbe(fixture.leftInterior.Cells);
				var initializationCandidateCount = symbiant.DebugLastSelectionCoreInitializationCandidateCount;
				var initializationShortlistCount = symbiant.DebugLastSelectionCoreInitializationShortlistCount;
				var initializationConnectivityChecks = symbiant.DebugLastSelectionCoreInitializationConnectivityChecks;
				var initializationWorkBounded = initializationProbeAdded > ZombieSymbiant.SelectionCoreInitializationCandidateLimit
					&& initializationCandidateCount > ZombieSymbiant.SelectionCoreInitializationCandidateLimit
					&& initializationShortlistCount == ZombieSymbiant.SelectionCoreInitializationCandidateLimit
					&& initializationConnectivityChecks == initializationShortlistCount
					&& symbiant.SelectionCoreValid;
				var initializationScaleProbe = new
				{
					addedCells = initializationProbeAdded,
					cellCount = symbiant.CellCount,
					candidateCount = initializationCandidateCount,
					candidateLimit = ZombieSymbiant.SelectionCoreInitializationCandidateLimit,
					shortlistCount = initializationShortlistCount,
					connectivityChecks = initializationConnectivityChecks,
					workBounded = initializationWorkBounded,
					core = DescribeSymbiantSelectionCore(symbiant)
				};
				result = new
				{
					success = selectorRect.HasValue
						&& selectorRect.Value.Area == 1
						&& selectorRect.Value.Contains(initialCoreCell)
							&& selectorPatchInstalled
							&& tooltipInputGated
							&& logicalCellsClickThrough
							&& manualTargetingCoversWholeBody
							&& gap.IsValid
						&& gapTargetable == false
						&& discoveryCueCleared
						&& wanderCarriedCore
						&& movingCoreTargetable
						&& repeatedWanderCarriedCore
						&& repeatedWanderWorkBounded
						&& handoffAligned
						&& fallbackVerified
						&& outgoingHandoffAligned
						&& shrinkSteps.Count > 0
						&& shrinkCoresValid
						&& initializationWorkBounded,
					sourcePath = "GenUI.ThingsUnderMouse consumer-specific filter + Selector.SelectableObjectsUnderMouse postfix + ZombieSymbiant selection-core state",
					shape = shape.Select(ZombieRuntimeActions.DescribeCell).ToArray(),
					selectorRect = ZombieRuntimeActions.DescribeCellRect(selectorRect.Value),
					selectorPatchInstalled,
					tooltipInput,
					logicalCellsClickThrough,
					logicalTargeting,
					manualTargeting = new
					{
						targeterWasActive,
						targeterStarted,
						wholeBodyTargetable = manualTargetingCoversWholeBody,
						targetableCells = manuallyTargetableCells.Select(ZombieRuntimeActions.DescribeCell).ToArray(),
						gapTargetable = manualTargetingGapTargetable
					},
					gap = ZombieRuntimeActions.DescribeCell(gap),
					gapTargetable,
					discoveryCore,
					afterSelection,
					discoveryCueCleared,
					wander,
					repeatedWander,
					handoff = new
					{
						from = ZombieRuntimeActions.DescribeCell(handoffFrom),
						to = ZombieRuntimeActions.DescribeCell(handoffTo),
						started = handoffStarted,
						aligned = handoffAligned,
						samples = handoffSamples
					},
					outgoingHandoff = new
					{
						aligned = outgoingHandoffAligned,
						started = outgoingStarted,
						from = outgoingFrom.IsValid ? ZombieRuntimeActions.DescribeCell(outgoingFrom) : null,
						to = outgoingTo.IsValid ? ZombieRuntimeActions.DescribeCell(outgoingTo) : null,
						sourceStillLogical = outgoingSourceStillLogical,
						earlyProgressApplied = outgoingEarlyProgressApplied,
						earlyHitCell = ZombieRuntimeActions.DescribeCell(outgoingEarlyHitCell),
						sourceTargetable = outgoingSourceTargetable,
						destinationTargetableEarly = outgoingDestinationTargetableEarly,
						feedJobCell = outgoingFeedJobCell.IsValid ? ZombieRuntimeActions.DescribeCell(outgoingFeedJobCell) : null,
						lateProgressApplied = outgoingLateProgressApplied,
						lateHitCell = ZombieRuntimeActions.DescribeCell(outgoingLateHitCell),
						destinationTargetableLate = outgoingDestinationTargetableLate
					},
					fallbackRendering = new
					{
						verified = fallbackVerified,
						coreDrawn = fallbackCoreDrawn,
						coreCell = ZombieRuntimeActions.DescribeCell(fallbackCoreCell),
						coreIsNonRoot = fallbackCoreCell != symbiant.Position,
						visualCenter = new { x = fallbackVisualCenter.x, z = fallbackVisualCenter.y },
						selectorRect = fallbackSelector.HasValue ? ZombieRuntimeActions.DescribeCellRect(fallbackSelector.Value) : null,
						logicalTargeting = fallbackLogicalTargeting
					},
					shrinkSteps,
					shrinkCoresValid,
					initializationScaleProbe
				};
			}
			catch (Exception ex)
			{
				error = ex.ToString();
			}
			finally
			{
				if (cleanup)
					symbiant?.DebugDestroyWithoutHostTrauma();
				_ = CleanupSymbiantExpansionFixture(map, fixture, cleanup);
			}
			return error == null ? result : new { success = false, error };
		}

		[Tool("zombieland/symbiant_infestation_state", Description = "Inspect or exercise the zombie symbiant state with spawn, createEvent, expand, move, shrink, feedCorpse, removeHostHediff, killHost, stageRetreatSave, contaminationStep, stress, and cleanup modes.")]
		public static object SymbiantInfestationState(
			[ToolParameter(Description = "Mode: read, spawn, createEvent, expand, move, shrink, feedCorpse, removeHostHediff, killHost, stageRetreatSave, contaminationStep, stress, cleanup.", Required = false, DefaultValue = "read")] string mode = "read",
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
			else if (mode.Equals("move", StringComparison.OrdinalIgnoreCase))
			{
				var before = DescribeSymbiantShapeDiagnostics(map, symbiant);
				var transitions = new List<object>();
				var pulses = 0;
				var requested = Mathf.Clamp(Math.Max(1, count), 1, 256);
				for (var i = 0; i < requested; i++)
				{
					var cellsBefore = symbiant?.AbsoluteCells.ToHashSet() ?? new HashSet<IntVec3>();
					if (symbiant?.TryMovePulse(false) != true)
						continue;
					var cellsAfter = symbiant.AbsoluteCells.ToHashSet();
					var sources = cellsBefore.Where(cell => cellsAfter.Contains(cell) == false).ToArray();
					var targets = cellsAfter.Where(cell => cellsBefore.Contains(cell) == false).ToArray();
					var source = sources.Length == 1 ? sources[0] : IntVec3.Invalid;
					var target = targets.Length == 1 ? targets[0] : IntVec3.Invalid;
					transitions.Add(new
					{
						index = i,
						oneSourceAndTarget = sources.Length == 1 && targets.Length == 1,
						source = source.IsValid ? ZombieRuntimeActions.DescribeCell(source) : null,
						target = target.IsValid ? ZombieRuntimeActions.DescribeCell(target) : null,
						sourceRemembered = source.IsValid && symbiant.DebugIsRecentMovementCell(source),
						targetRemembered = target.IsValid && symbiant.DebugIsRecentMovementCell(target)
					});
					pulses++;
				}
				action = new
				{
					requested,
					pulses,
					before,
					after = DescribeSymbiantShapeDiagnostics(map, symbiant),
					transitions
				};
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
			else if (mode.Equals("stageRetreatSave", StringComparison.OrdinalIgnoreCase))
			{
				if (symbiant != null)
					return new { success = false, error = "stageRetreatSave requires a map without an active Symbiant." };
				var root = x >= 0 && z >= 0 ? new IntVec3(x, 0, z) : map.Center;
				if (TryFindClearSpawnCell(map, root, 16f, out var cell, out var error) == false)
					return error;
				symbiant = ZombieSymbiant.DebugSpawnForRendering(map, cell, [cell]);
				var id = ZombieRuntimeActions.StableThingId(symbiant);
				var removed = symbiant?.ShrinkCells(int.MaxValue) ?? 0;
				action = new
				{
					id,
					removed,
					cellCount = symbiant?.CellCount ?? -1,
					activeCellMotions = symbiant?.ActiveCellMotionCount ?? -1,
					destroyed = symbiant?.Destroyed ?? true,
					staged = symbiant != null
						&& removed == 1
						&& symbiant.CellCount == 0
						&& symbiant.ActiveCellMotionCount > 0
						&& symbiant.Destroyed == false
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
			var worldSymbiants = (Find.WorldPawns?.AllPawnsAliveOrDead ?? new List<Pawn>())
				.OfType<ZombieSymbiant>()
				.Select(ZombieRuntimeActions.StableThingId)
				.ToArray();
			return new
			{
				success = true,
				mode,
				action,
				worldSymbiants,
				perf = ZombieSymbiant.DebugPerfState(),
				perfAction,
				maxCellsOverrideAction,
				symbiant = symbiant == null ? null : new
				{
					id = ZombieRuntimeActions.StableThingId(symbiant),
					position = ZombieRuntimeActions.DescribeCell(symbiant.Position),
					selectionCore = DescribeSymbiantSelectionCore(symbiant),
					selectorRect = selectorRect.HasValue ? ZombieRuntimeActions.DescribeCellRect(selectorRect.Value) : null,
					selectorIsSingleCoreCell = selectorRect.HasValue
						&& selectorRect.Value.Area == 1
						&& selectorRect.Value.Contains(symbiant.SelectionCoreCell),
					drawSize = new { x = symbiant.DrawSize.x, z = symbiant.DrawSize.y },
					occupiedDrawRect = ZombieRuntimeActions.DescribeCellRect(symbiant.OccupiedDrawRect()),
					renderWorldSize = new { x = symbiant.RenderWorldSize.x, z = symbiant.RenderWorldSize.y },
					renderTextureSize = new { x = symbiant.RenderTextureWidth, y = symbiant.RenderTextureHeight },
					renderShader = symbiant.RenderShaderName,
					renderUsesSymbiantShader = symbiant.RenderUsesSymbiantShader,
					renderUsesGpuMetaballMask = symbiant.RenderUsesGpuMetaballMask,
					renderMetaballElements = symbiant.RenderMetaballElementCount,
					renderPatchCount = symbiant.RenderPatchCount,
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
						symbiosisHediffSeverity = hostSymbiosisHediff?.Severity ?? 0f,
						movementBenefitCount = ZombieSymbiant.MoveSpeedBenefitCount(host),
						manipulationBenefitCount = ZombieSymbiant.ManipulationBenefitCount(host),
						movingCapacity = host.health?.capacities?.GetLevel(PawnCapacityDefOf.Moving) ?? 0f,
						manipulationCapacity = host.health?.capacities?.GetLevel(PawnCapacityDefOf.Manipulation) ?? 0f,
						moveSpeed = host.GetStatValue(StatDefOf.MoveSpeed),
						ticksPerMoveCardinal = host.TicksPerMoveCardinal
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
					nextExpansionTick = symbiant.NextExpansionTick,
					relocationCellDebt = symbiant.RelocationCellDebt,
					nextRelocationPulseTick = symbiant.NextRelocationPulseTick,
					uprootedSinceTick = symbiant.UprootedSinceTick,
					feedPausedUntilTick = symbiant.FeedPausedUntilTick,
					lastRecessionPulseCells = symbiant.LastRecessionPulseCells,
					exteriorOverflowAuthorized = symbiant.ExteriorOverflowAuthorized,
					hostEffectCellCount = symbiant.HostEffectCellCount,
					roomDisruption,
					sampleCells = symbiant.AbsoluteCells.Take(24).Select(ZombieRuntimeActions.DescribeCell).ToArray(),
					shapeDiagnostics = DescribeSymbiantShapeDiagnostics(map, symbiant),
					growthDiagnostics = DescribeSymbiantGrowthDiagnostics(map, symbiant)
				},
				settings = new
				{
					ZombieSettings.Values.symbiantEnabled,
					ZombieSettings.Values.symbiantMaxCells
				}
			};
		}

		static object DescribeSymbiantShapeDiagnostics(Map map, ZombieSymbiant symbiant)
		{
			if (map == null || symbiant == null)
				return null;

			var occupied = symbiant.AbsoluteCells.ToHashSet();
			var cardinalNeighborTotal = 0;
			var adjacentNeighborTotal = 0;
			var exposedCardinalPerimeter = 0;
			var diningTableCells = 0;
			var workTableCells = 0;
			var storageCells = 0;
			var furnitureCells = 0;
			var trafficByCell = new Dictionary<IntVec3, float>(occupied.Count);
			foreach (var cell in occupied)
			{
				var cardinalNeighbors = GenAdj.CardinalDirections.Count(direction => occupied.Contains(cell + direction));
				cardinalNeighborTotal += cardinalNeighbors;
				adjacentNeighborTotal += GenAdj.AdjacentCells.Count(direction => occupied.Contains(cell + direction));
				exposedCardinalPerimeter += 4 - cardinalNeighbors;

				var things = cell.GetThingList(map);
				var dining = things.Any(thing => thing?.def?.surfaceType == SurfaceType.Eat);
				var work = things.Any(thing => thing is Building_WorkTable);
				var storage = things.Any(thing => thing is Building_Storage);
				var furniture = things.Any(ZombieSymbiant.IsSymbiantFurnitureCellThing);
				if (dining)
					diningTableCells++;
				if (work)
					workTableCells++;
				if (storage)
					storageCells++;
				if (furniture)
					furnitureCells++;
				trafficByCell[cell] = ZombieSymbiant.DebugTrafficScore(map, cell);
			}

			var occupiedRooms = occupied
				.GroupBy(cell => cell.GetRoom(map))
				.Where(group => group.Key != null)
				.Select(group => new
				{
					id = group.Key.ID,
					role = group.Key.Role?.defName,
					cells = group.Count(),
					averageTraffic = group.Select(cell => trafficByCell[cell]).DefaultIfEmpty(0f).Average(),
					maxTraffic = group.Select(cell => trafficByCell[cell]).DefaultIfEmpty(0f).Max()
				})
				.OrderByDescending(room => room.cells)
				.ToArray();

			return new
			{
				cellCount = occupied.Count,
				cardinallyConnected = symbiant.DebugCellsAreConnected,
				componentCount = symbiant.DebugComponentCount,
				exposedCardinalPerimeter,
				averageCardinalNeighbors = occupied.Count == 0 ? 0f : cardinalNeighborTotal / (float)occupied.Count,
				averageAdjacentNeighbors = occupied.Count == 0 ? 0f : adjacentNeighborTotal / (float)occupied.Count,
				furnitureCells,
				diningTableCells,
				workTableCells,
				storageCells,
				averageTraffic = trafficByCell.Values.DefaultIfEmpty(0f).Average(),
				maxTraffic = trafficByCell.Values.DefaultIfEmpty(0f).Max(),
				recentCellHistory = new
				{
					count = symbiant.RecentMovementCellCount,
					capacity = ZombieSymbiant.RecentMovementCellCapacity,
					serialized = false
				},
				occupiedRooms
			};
		}

		static object DescribeSymbiantGrowthDiagnostics(Map map, ZombieSymbiant symbiant)
		{
			return map == null || symbiant == null ? null : symbiant.DebugPlacementDiagnostics();
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
			if (cell.Roofed(map) == false)
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
			if (cell.Roofed(map) == false)
				return false;
			return GenAdj.CardinalDirections
				.Select(dir => cell + dir)
				.Where(adjacent => adjacent.InBounds(map))
				.Select(adjacent => adjacent.GetRoom(map))
				.Any(IsEligibleIndoorRoomForDiagnostics);
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

			var id = ZombieRuntimeActions.StableThingId(symbiant);
			var destroyedBefore = symbiant.Destroyed;
			if (destroyedBefore == false)
				symbiant.DebugDestroyWithoutHostTrauma();
			else
			{
				if (Find.WorldPawns?.Contains(symbiant) == true)
					Find.WorldPawns.RemovePawn(symbiant);
				if (symbiant.Discarded == false)
					symbiant.Discard(true);
			}
			_ = ZombieSymbiant.ActiveSymbiant(map);
			var worldPawnAfter = Find.WorldPawns?.Contains(symbiant) == true;
			return new
			{
				requested = true,
				cleaned = symbiant.Destroyed && symbiant.Discarded && worldPawnAfter == false,
				destroyedBefore,
				destroyedAfter = symbiant.Destroyed,
				discardedAfter = symbiant.Discarded,
				worldPawnAfter,
				symbiant = id
			};
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
