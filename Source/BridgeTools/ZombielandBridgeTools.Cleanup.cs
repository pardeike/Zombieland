using RimBridgeServer.Sdk;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	public sealed partial class ZombielandBridgeTools
	{
		const int DefaultStorytellerRunDays = 120;
		const int DefaultStorytellerSettleTicks = 2500;
		const int DefaultStorytellerWaitTimeoutMs = 1800000;
		const int DefaultStorytellerMaxEvents = 180;
		const string DefaultStorytellerSpeed = "Ultrafast";

		sealed class StorytellerRunEventRecord
		{
			public int sequence { get; set; }
			public int tick { get; set; }
			public float dayFromStart { get; set; }
			public string id { get; set; }
			public string letterDef { get; set; }
			public string label { get; set; }
			public string category { get; set; }
			public bool isRaid { get; set; }
			public bool cleanupRan { get; set; }
			public int settleTicks { get; set; }
			public int removedPawns { get; set; }
			public int removedCorpses { get; set; }
			public int removedBuildings { get; set; }
			public int removedSkyfallers { get; set; }
			public int outsideHomeViolationCount { get; set; }
			public string notes { get; set; }
		}

		sealed class StorytellerCleanupSnapshot
		{
			public bool success { get; set; }
			public bool dryRun { get; set; }
			public int currentTick { get; set; }
			public int removedPawnCount { get; set; }
			public int removedCorpseCount { get; set; }
			public int removedBuildingCount { get; set; }
			public int removedSkyfallerCount { get; set; }
			public int preservedZombieCount { get; set; }
			public int outsideHomePawnCount { get; set; }
			public int outsideHomeAllowedPawnCount { get; set; }
			public int outsideHomeViolationCount { get; set; }
			public object[] removedPawns { get; set; }
			public object[] removedCorpses { get; set; }
			public object[] removedBuildings { get; set; }
			public object[] removedSkyfallers { get; set; }
			public object[] outsideHomePawns { get; set; }
			public object[] outsideHomeViolations { get; set; }
		}

		[Tool("zombieland/remove_hostile_pawns", Description = "Remove spawned current-map pawns hostile to the player faction, optionally including Zombieland pawns and hostile corpses.")]
		public static object RemoveHostilePawns(
			[ToolParameter(Description = "When true, include Zombieland zombies, spitters, and symbiants in the cleanup.", Required = false, DefaultValue = false)] bool includeZombies = false,
			[ToolParameter(Description = "When true, also remove corpses whose inner pawn is hostile to the player faction.", Required = false, DefaultValue = true)] bool includeCorpses = true,
			[ToolParameter(Description = "When true, report matching pawns/corpses without destroying them.", Required = false, DefaultValue = false)] bool dryRun = false)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, message = "No current map is active." };

			var pawnCandidates = map.mapPawns.AllPawnsSpawned
				.Where(pawn => ShouldRemoveHostilePawn(pawn, includeZombies))
				.ToArray();

			var corpseCandidates = includeCorpses
				? map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse)
					.OfType<Corpse>()
					.Where(corpse => corpse != null && ShouldRemoveHostilePawn(corpse.InnerPawn, includeZombies))
					.ToArray()
				: Array.Empty<Corpse>();

			var pawns = pawnCandidates.Select(DescribeHostileCleanupPawn).ToArray();
			var corpses = corpseCandidates.Select(DescribeHostileCleanupCorpse).ToArray();

			if (dryRun == false)
			{
				foreach (var pawn in pawnCandidates)
					DestroyThingQuietly(pawn);

				foreach (var corpse in corpseCandidates)
					DestroyThingQuietly(corpse);
			}

			return new
			{
				success = true,
				dryRun,
				includeZombies,
				includeCorpses,
				mapId = map.uniqueID,
				matchedPawnCount = pawnCandidates.Length,
				matchedCorpseCount = corpseCandidates.Length,
				removedPawnCount = dryRun ? 0 : pawnCandidates.Length,
				removedCorpseCount = dryRun ? 0 : corpseCandidates.Length,
				pawns,
				corpses
			};
		}

		[Tool("zombieland/storyteller_event_cleanup", Description = "Clean up storyteller event residue outside the home area while preserving all Zombieland zombies and zombie corpses.")]
		public static object StorytellerEventCleanup(
			[ToolParameter(Description = "When true, report matching pawns, corpses, buildings, and skyfallers without destroying them.", Required = false, DefaultValue = false)] bool dryRun = false,
			[ToolParameter(Description = "When true, remove non-zombie hostile corpses outside the home area.", Required = false, DefaultValue = true)] bool includeCorpses = true,
			[ToolParameter(Description = "When true, remove hostile event buildings and artificial structures outside the home area.", Required = false, DefaultValue = true)] bool includeStructures = true,
			[ToolParameter(Description = "Maximum number of removed or violation entries returned in detail arrays.", Required = false, DefaultValue = 40)] int detailLimit = 40)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, message = "No current map is active." };

			return RunStorytellerEventCleanup(map, dryRun, includeCorpses, includeStructures, detailLimit);
		}

		[Tool("zombieland/run_storyteller_event_cadence", Description = "Autonomously run until right-side letters, settle event spawns, clean non-zombie event residue outside home, and return a compact event ledger.")]
		public static async Task<object> RunStorytellerEventCadence(
			IRimBridgeContext ctx,
			CancellationToken cancellationToken,
			[ToolParameter(Description = "Number of in-game days to run from the current tick.", Required = false, DefaultValue = DefaultStorytellerRunDays)] int days = DefaultStorytellerRunDays,
			[ToolParameter(Description = "Maximum number of letter events to collect before stopping.", Required = false, DefaultValue = DefaultStorytellerMaxEvents)] int maxEvents = DefaultStorytellerMaxEvents,
			[ToolParameter(Description = "Ticks to advance after each letter before cleanup so pods/raids fully materialize.", Required = false, DefaultValue = DefaultStorytellerSettleTicks)] int settleTicks = DefaultStorytellerSettleTicks,
			[ToolParameter(Description = "RimWorld play speed while waiting: Normal, Fast, Superfast, or Ultrafast.", Required = false, DefaultValue = DefaultStorytellerSpeed)] string speed = DefaultStorytellerSpeed,
			[ToolParameter(Description = "Maximum real-time wait per letter before stopping the run.", Required = false, DefaultValue = DefaultStorytellerWaitTimeoutMs)] int waitTimeoutMs = DefaultStorytellerWaitTimeoutMs,
			[ToolParameter(Description = "When true, use RimBridgeServer's forced-speed path, including RimWorld's UltraSpeedBoost when supported.", Required = false, DefaultValue = true)] bool forceRequestedSpeed = true,
			[ToolParameter(Description = "When true, ignore letters that were already present when the run starts.", Required = false, DefaultValue = true)] bool ignoreExistingLetters = true)
		{
			if (ctx == null)
				return new { success = false, error = "RimBridge context was not injected." };

			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is active." };

			var clampedDays = Mathf.Clamp(days, 1, 240);
			var clampedMaxEvents = Mathf.Clamp(maxEvents, 1, 500);
			var clampedSettleTicks = Mathf.Clamp(settleTicks, 0, GenDate.TicksPerHour);
			var clampedWaitTimeoutMs = Mathf.Clamp(waitTimeoutMs, 1000, 7200000);
			var startTick = Find.TickManager.TicksGame;
			var targetTick = startTick + clampedDays * GenDate.TicksPerDay;
			var baselineLetters = SnapshotLetterIds();
			var records = new List<StorytellerRunEventRecord>();
			var cleanupSnapshots = new List<StorytellerCleanupSnapshot>();
			var stopReason = "target_tick";
			var lastWait = (object)null;
			var errorsClosed = CloseKnownErrorWindows();

			while (Find.TickManager.TicksGame < targetTick && records.Count < clampedMaxEvents)
			{
				cancellationToken.ThrowIfCancellationRequested();
				CloseKnownErrorWindows();

				var wait = await ctx.Tools.CallAsync(
					"rimworld/play_until_letter",
					new
					{
						timeoutMs = clampedWaitTimeoutMs,
						speed = speed ?? DefaultStorytellerSpeed,
						pollIntervalMs = 250,
						forceRequestedSpeed,
						includeExistingLetters = false
					},
					new RimBridgeToolCallOptions { TimeoutMs = clampedWaitTimeoutMs + 30000 },
					cancellationToken);
				lastWait = wait.Result;
				errorsClosed += CloseKnownErrorWindows();

				var letters = SnapshotNewLetters(baselineLetters)
					.OrderBy(letter => letter.arrivalTick)
					.ToArray();
				if (letters.Length == 0)
				{
					stopReason = wait.Succeeded() ? "no_new_letter" : "wait_failed_or_timeout";
					break;
				}

				var firstNewRecordIndex = records.Count;
				foreach (var letter in letters)
				{
					if (ignoreExistingLetters && letter.arrivalTick < startTick)
					{
						baselineLetters.Add(letter.id);
						continue;
					}

					var record = DescribeStorytellerRunLetter(records.Count + 1, letter, startTick);
					records.Add(record);
					baselineLetters.Add(letter.id);
				}

				var newRecords = records.Skip(firstNewRecordIndex).ToArray();
				if (newRecords.Length == 0)
					continue;

				var shouldRunCleanup = newRecords.Any(RequiresPostLetterCleanup);
				if (shouldRunCleanup && clampedSettleTicks > 0 && Find.TickManager.TicksGame < targetTick)
				{
					var runTicks = Math.Min(clampedSettleTicks, targetTick - Find.TickManager.TicksGame);
					await ctx.MainThread.InvokeAsync(() => AdvanceGameTicks(runTicks), cancellationToken);
				}

				errorsClosed += CloseKnownErrorWindows();
				if (shouldRunCleanup)
				{
					var cleanup = RunStorytellerEventCleanup(map, false, true, true, 20);
					cleanupSnapshots.Add(cleanup);
					foreach (var record in newRecords)
					{
						record.cleanupRan = true;
						record.settleTicks = clampedSettleTicks;
						record.removedPawns = cleanup.removedPawnCount;
						record.removedCorpses = cleanup.removedCorpseCount;
						record.removedBuildings = cleanup.removedBuildingCount;
						record.removedSkyfallers = cleanup.removedSkyfallerCount;
						record.outsideHomeViolationCount = cleanup.outsideHomeViolationCount;
					}
				}

				DismissLetters(baselineLetters);
				CloseKnownErrorWindows();
			}

			if (records.Count >= clampedMaxEvents)
				stopReason = "max_events";
			if (Find.TickManager.TicksGame >= targetTick)
				stopReason = "target_tick";

			Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
			var finalCleanup = RunStorytellerEventCleanup(map, false, true, true, 20);
			cleanupSnapshots.Add(finalCleanup);
			var negativeCount = records.Count(record => record.category == "negative_threat");
			var environmentalCount = records.Count(record => record.category == "environmental");
			var raidCount = records.Count(record => record.isRaid);

			return new
			{
				success = finalCleanup.outsideHomeViolationCount == 0,
				stopReason,
				startTick,
				targetTick,
				endTick = Find.TickManager.TicksGame,
				requestedDays = clampedDays,
				elapsedDays = (Find.TickManager.TicksGame - startTick) / (float)GenDate.TicksPerDay,
				eventCount = records.Count,
				negativeThreatCount = negativeCount,
				environmentalCount,
				raidCount,
				speed = speed ?? DefaultStorytellerSpeed,
				forceRequestedSpeed,
				settleTicks = clampedSettleTicks,
				waitTimeoutMs = clampedWaitTimeoutMs,
				errorsClosed,
				lastWait,
				finalCleanup,
				events = records.ToArray(),
				cleanupSummary = cleanupSnapshots.Select((cleanup, index) => new
				{
					index,
					cleanup.currentTick,
					cleanup.success,
					cleanup.removedPawnCount,
					cleanup.removedCorpseCount,
					cleanup.removedBuildingCount,
					cleanup.removedSkyfallerCount,
					cleanup.preservedZombieCount,
					cleanup.outsideHomePawnCount,
					cleanup.outsideHomeViolationCount
				}).ToArray()
			};
		}

		static StorytellerCleanupSnapshot RunStorytellerEventCleanup(Map map, bool dryRun, bool includeCorpses, bool includeStructures, int detailLimit)
		{
			var limit = Mathf.Clamp(detailLimit, 0, 200);
			var pawnCandidates = map.mapPawns.AllPawnsSpawned
				.Where(pawn => ShouldRemoveStorytellerRunPawn(pawn, map))
				.ToArray();
			var corpseCandidates = includeCorpses
				? map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse)
					.OfType<Corpse>()
					.Where(corpse => ShouldRemoveStorytellerRunCorpse(corpse, map))
					.ToArray()
				: Array.Empty<Corpse>();
			var buildingCandidates = includeStructures
				? map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial)
					.Where(thing => ShouldRemoveStorytellerRunStructure(thing, map))
					.ToArray()
				: Array.Empty<Thing>();
			var skyfallerCandidates = map.listerThings.AllThings
				.Where(thing => thing is Skyfaller || IsDropPodLike(thing))
				.Where(thing => IsOutsideHome(thing.PositionHeld, map))
				.Where(thing => StorytellerEventFilters.IsZombielandPawn(thing) == false)
				.ToArray();

			var removedPawns = pawnCandidates.Select(DescribeStorytellerCleanupPawn).Take(limit).ToArray();
			var removedCorpses = corpseCandidates.Select(DescribeHostileCleanupCorpse).Take(limit).ToArray();
			var removedBuildings = buildingCandidates.Select(DescribeCleanupThing).Take(limit).ToArray();
			var removedSkyfallers = skyfallerCandidates.Select(DescribeCleanupThing).Take(limit).ToArray();

			if (dryRun == false)
			{
				foreach (var pawn in pawnCandidates)
					DestroyThingQuietly(pawn);
				foreach (var corpse in corpseCandidates)
					DestroyThingQuietly(corpse);
				foreach (var skyfaller in skyfallerCandidates)
					DestroyThingQuietly(skyfaller);
				foreach (var building in buildingCandidates)
					DestroyThingQuietly(building);
			}

			var outsideHomePawns = map.mapPawns.AllPawnsSpawned
				.Where(pawn => pawn?.Destroyed == false)
				.Where(pawn => IsOutsideHome(pawn.Position, map))
				.ToArray();
			var outsideHomeViolations = outsideHomePawns
				.Where(pawn => IsAllowedOutsideHomeAfterStorytellerCleanup(pawn, map) == false)
				.ToArray();

			return new StorytellerCleanupSnapshot
			{
				success = outsideHomeViolations.Length == 0,
				dryRun = dryRun,
				currentTick = Find.TickManager?.TicksGame ?? 0,
				removedPawnCount = dryRun ? 0 : pawnCandidates.Length,
				removedCorpseCount = dryRun ? 0 : corpseCandidates.Length,
				removedBuildingCount = dryRun ? 0 : buildingCandidates.Length,
				removedSkyfallerCount = dryRun ? 0 : skyfallerCandidates.Length,
				preservedZombieCount = outsideHomePawns.Count(StorytellerEventFilters.IsZombielandPawn),
				outsideHomePawnCount = outsideHomePawns.Length,
				outsideHomeAllowedPawnCount = outsideHomePawns.Length - outsideHomeViolations.Length,
				outsideHomeViolationCount = outsideHomeViolations.Length,
				removedPawns = removedPawns,
				removedCorpses = removedCorpses,
				removedBuildings = removedBuildings,
				removedSkyfallers = removedSkyfallers,
				outsideHomePawns = outsideHomePawns.Select(DescribeStorytellerCleanupPawn).Take(limit).ToArray(),
				outsideHomeViolations = outsideHomeViolations.Select(DescribeStorytellerCleanupPawn).Take(limit).ToArray()
			};
		}

		static bool ShouldRemoveHostilePawn(Pawn pawn, bool includeZombies)
		{
			if (pawn == null || pawn.Destroyed)
				return false;
			if (includeZombies == false && StorytellerEventFilters.IsZombielandPawn(pawn))
				return false;
			return IsHostileToPlayer(pawn);
		}

		static bool ShouldRemoveStorytellerRunPawn(Pawn pawn, Map map)
		{
			if (pawn == null || pawn.Destroyed || pawn.Dead)
				return false;
			if (StorytellerEventFilters.IsZombielandPawn(pawn))
				return false;
			if (IsOutsideHome(pawn.Position, map) == false)
				return false;
			if (pawn.RaceProps?.Animal == true && pawn.RaceProps?.IsMechanoid != true)
				return IsHostileToPlayer(pawn);
			if (IsWildHuman(pawn))
				return IsHostileToPlayer(pawn);
			if (pawn.RaceProps?.Humanlike == true)
				return true;
			if (pawn.RaceProps?.IsMechanoid == true)
				return true;
			if (pawn.RaceProps?.FleshType == FleshTypeDefOf.Insectoid)
				return true;
			return IsHostileToPlayer(pawn);
		}

		static bool ShouldRemoveStorytellerRunCorpse(Corpse corpse, Map map)
		{
			if (corpse == null || corpse.Destroyed || StorytellerEventFilters.IsZombielandCorpse(corpse))
				return false;
			if (IsOutsideHome(corpse.PositionHeld, map) == false)
				return false;
			var innerPawn = corpse.InnerPawn;
			return innerPawn?.RaceProps?.Humanlike == true
				|| innerPawn?.RaceProps?.IsMechanoid == true
				|| innerPawn?.RaceProps?.FleshType == FleshTypeDefOf.Insectoid
				|| IsHostileToPlayer(innerPawn);
		}

		static bool ShouldRemoveStorytellerRunStructure(Thing thing, Map map)
		{
			if (thing == null || thing.Destroyed)
				return false;
			if (IsOutsideHome(thing.PositionHeld, map) == false)
				return false;
			if (thing.Faction == Faction.OfPlayer)
				return false;
			if (thing is Building_Turret)
				return true;
			if (thing.def?.building?.isTrap == true && thing.Faction != Faction.OfPlayer)
				return true;
			if (thing.Faction != null && thing.Faction != Faction.OfPlayer)
				return true;
			var defName = thing.def?.defName ?? "";
			return defName.IndexOf("Ship", StringComparison.OrdinalIgnoreCase) >= 0
				|| defName.IndexOf("Defoliator", StringComparison.OrdinalIgnoreCase) >= 0
				|| defName.IndexOf("Psychic", StringComparison.OrdinalIgnoreCase) >= 0
				|| defName.IndexOf("MechCluster", StringComparison.OrdinalIgnoreCase) >= 0
				|| defName.IndexOf("Mech", StringComparison.OrdinalIgnoreCase) >= 0 && thing.def?.building != null;
		}

		static bool IsAllowedOutsideHomeAfterStorytellerCleanup(Pawn pawn, Map map)
		{
			if (pawn == null || pawn.Destroyed || pawn.Dead)
				return true;
			if (StorytellerEventFilters.IsZombielandPawn(pawn))
				return true;
			if (IsOutsideHome(pawn.Position, map) == false)
				return true;
			if (pawn.RaceProps?.Animal == true && pawn.RaceProps?.IsMechanoid != true && IsHostileToPlayer(pawn) == false)
				return true;
			if (IsWildHuman(pawn) && IsHostileToPlayer(pawn) == false)
				return true;
			return false;
		}

		static bool IsHostileToPlayer(Pawn pawn)
		{
			var playerFaction = Faction.OfPlayer;
			if (pawn == null || playerFaction == null || pawn.Faction == playerFaction)
				return false;

			try
			{
				if (pawn.HostileTo(playerFaction))
					return true;
			}
			catch
			{
			}

			try
			{
				if (pawn is IAttackTarget attackTarget && GenHostility.IsActiveThreatTo(attackTarget, playerFaction, false, true))
					return true;
			}
			catch
			{
			}

			var mentalState = pawn.MentalStateDef;
			return mentalState == MentalStateDefOf.Manhunter || mentalState == MentalStateDefOf.ManhunterPermanent;
		}

		static bool IsOutsideHome(IntVec3 cell, Map map)
		{
			if (map == null || cell.InBounds(map) == false)
				return true;
			var home = map.areaManager?.Home;
			return home == null || home[cell] == false;
		}

		static bool IsWildHuman(Pawn pawn)
		{
			return pawn?.RaceProps?.Humanlike == true
				&& pawn.Faction == null
				&& pawn.IsPrisoner == false
				&& pawn.IsSlave == false
				&& pawn.IsColonist == false;
		}

		static bool IsDropPodLike(Thing thing)
		{
			if (thing == null)
				return false;
			var defName = thing.def?.defName ?? "";
			var typeName = thing.GetType().Name;
			return defName.IndexOf("DropPod", StringComparison.OrdinalIgnoreCase) >= 0
				|| defName.IndexOf("TransportPod", StringComparison.OrdinalIgnoreCase) >= 0
				|| typeName.IndexOf("DropPod", StringComparison.OrdinalIgnoreCase) >= 0
				|| typeName.IndexOf("TransportPod", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		static object DescribeHostileCleanupPawn(Pawn pawn)
		{
			return new
			{
				pawn = DescribePawn(pawn),
				isZombielandPawn = StorytellerEventFilters.IsZombielandPawn(pawn),
				factionDef = pawn?.Faction?.def?.defName,
				mentalState = pawn?.MentalStateDef?.defName,
				hostileToPlayer = pawn != null && IsHostileToPlayer(pawn)
			};
		}

		static object DescribeStorytellerCleanupPawn(Pawn pawn)
		{
			return new
			{
				pawn = DescribePawn(pawn),
				isZombielandPawn = StorytellerEventFilters.IsZombielandPawn(pawn),
				isWildHuman = IsWildHuman(pawn),
				isColonist = pawn?.IsColonist,
				isAnimal = pawn?.RaceProps?.Animal,
				isHumanlike = pawn?.RaceProps?.Humanlike,
				isMechanoid = pawn?.RaceProps?.IsMechanoid,
				isInsectoid = pawn?.RaceProps?.FleshType == FleshTypeDefOf.Insectoid,
				factionDef = pawn?.Faction?.def?.defName,
				mentalState = pawn?.MentalStateDef?.defName,
				hostileToPlayer = pawn != null && IsHostileToPlayer(pawn)
			};
		}

		static object DescribeHostileCleanupCorpse(Corpse corpse)
		{
			var innerPawn = corpse?.InnerPawn;
			return new
			{
				corpseId = ZombieRuntimeActions.StableThingId(corpse),
				label = corpse?.LabelCap,
				position = corpse == null ? null : ZombieRuntimeActions.DescribeCell(corpse.Position),
				isZombielandCorpse = StorytellerEventFilters.IsZombielandCorpse(corpse),
				innerPawn = DescribeHostileCleanupPawn(innerPawn)
			};
		}

		static object DescribeCleanupThing(Thing thing)
		{
			return new
			{
				id = ZombieRuntimeActions.StableThingId(thing),
				defName = thing?.def?.defName,
				type = thing?.GetType().FullName,
				label = thing?.LabelCap,
				position = thing == null ? null : ZombieRuntimeActions.DescribeCell(thing.PositionHeld),
				factionDef = thing?.Faction?.def?.defName
			};
		}

		static void DestroyThingQuietly(Thing thing)
		{
			if (thing == null || thing.Destroyed)
				return;
			thing.Destroy(DestroyMode.Vanish);
		}

		static HashSet<string> SnapshotLetterIds()
		{
			return (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
				.Where(letter => letter != null)
				.Select(letter => letter.GetUniqueLoadID())
				.ToHashSet(StringComparer.Ordinal);
		}

		static StorytellerRunLetterSnapshot[] SnapshotNewLetters(HashSet<string> baseline)
		{
			return (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
				.Where(letter => letter != null)
				.Where(letter => baseline.Contains(letter.GetUniqueLoadID()) == false)
				.Select(DescribeStorytellerRunLetterSnapshot)
				.ToArray();
		}

		sealed class StorytellerRunLetterSnapshot
		{
			public string id { get; set; }
			public int arrivalTick { get; set; }
			public string letterDef { get; set; }
			public string label { get; set; }
			public string text { get; set; }
		}

		static StorytellerRunLetterSnapshot DescribeStorytellerRunLetterSnapshot(Letter letter)
		{
			return new StorytellerRunLetterSnapshot
			{
				id = letter.GetUniqueLoadID(),
				arrivalTick = letter.arrivalTick,
				letterDef = letter.def?.defName,
				label = letter.Label.ToString(),
				text = letter is ChoiceLetter choiceLetter ? choiceLetter.Text.ToString() : null
			};
		}

		static StorytellerRunEventRecord DescribeStorytellerRunLetter(int sequence, StorytellerRunLetterSnapshot letter, int startTick)
		{
			var category = StorytellerLetterCategory(letter);
			return new StorytellerRunEventRecord
			{
				sequence = sequence,
				tick = letter.arrivalTick,
				dayFromStart = (letter.arrivalTick - startTick) / (float)GenDate.TicksPerDay,
				id = letter.id,
				letterDef = letter.letterDef,
				label = letter.label,
				category = category,
				isRaid = IsRaidLetter(letter),
				notes = BuildLetterNotes(letter)
			};
		}

		static string StorytellerLetterCategory(StorytellerRunLetterSnapshot letter)
		{
			var def = letter?.letterDef ?? "";
			if (IsEnvironmentalLetter(letter))
				return "environmental";
			if (def == "ThreatBig" || def == "ThreatSmall" || def == "NegativeEvent")
				return "negative_threat";
			if (def == "PositiveEvent")
				return "positive";
			if (def == "NeutralEvent")
				return "neutral";
			if (def == "NewQuest")
				return "quest";
			if (def == "Death")
				return "death";
			return "other";
		}

		static bool IsRaidLetter(StorytellerRunLetterSnapshot letter)
		{
			var label = letter?.label ?? "";
			return label.StartsWith("Raid:", StringComparison.OrdinalIgnoreCase)
				|| label.IndexOf(" raid", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		static string BuildLetterNotes(StorytellerRunLetterSnapshot letter)
		{
			if (letter == null)
				return null;
			if (string.Equals(letter.letterDef, "ThreatSmall", StringComparison.Ordinal) && letter.label?.IndexOf("ship", StringComparison.OrdinalIgnoreCase) >= 0)
				return "ship/object threat";
			if (ContainsIgnoreCase(letter.label, "manhunter") || ContainsIgnoreCase(letter.label, "mad "))
				return "animal threat";
			if (IsEnvironmentalLetter(letter))
				return "environmental/no cleanup";
			return null;
		}

		static bool RequiresPostLetterCleanup(StorytellerRunEventRecord record)
		{
			if (record == null || record.category == "environmental" || record.category == "death")
				return false;
			if (record.isRaid)
				return true;
			var label = record.label ?? "";
			return record.letterDef == "ThreatBig"
				|| record.letterDef == "ThreatSmall"
				|| ContainsIgnoreCase(label, "trader")
				|| ContainsIgnoreCase(label, "merchant")
				|| ContainsIgnoreCase(label, "slaver")
				|| ContainsIgnoreCase(label, "visitors")
				|| ContainsIgnoreCase(label, "transport pod")
				|| ContainsIgnoreCase(label, "self-tamed")
				|| ContainsIgnoreCase(label, " join")
				|| ContainsIgnoreCase(label, "wanders in")
				|| ContainsIgnoreCase(label, "wild man")
				|| ContainsIgnoreCase(label, "wild woman")
				|| ContainsIgnoreCase(label, "thrumbos")
				|| ContainsIgnoreCase(label, "migration");
		}

		static bool IsEnvironmentalLetter(StorytellerRunLetterSnapshot letter)
		{
			var label = letter?.label ?? "";
			return ContainsIgnoreCase(label, "eclipse")
				|| ContainsIgnoreCase(label, "solar flare")
				|| ContainsIgnoreCase(label, "flashstorm")
				|| ContainsIgnoreCase(label, "psychic drone")
				|| ContainsIgnoreCase(label, "psychic soothe")
				|| ContainsIgnoreCase(label, "aurora")
				|| ContainsIgnoreCase(label, "cold snap")
				|| ContainsIgnoreCase(label, "heat wave")
				|| ContainsIgnoreCase(label, "toxic fallout")
				|| ContainsIgnoreCase(label, "volcanic winter");
		}

		static bool ContainsIgnoreCase(string value, string needle)
		{
			return value != null && value.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
		}

		static void DismissLetters(HashSet<string> ids)
		{
			var letters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
				.Where(letter => letter != null && ids.Contains(letter.GetUniqueLoadID()))
				.Where(letter => letter.CanDismissWithRightClick)
				.ToArray();
			foreach (var letter in letters)
				Find.LetterStack.RemoveLetter(letter);
		}

		static int CloseKnownErrorWindows()
		{
			var stack = Find.WindowStack;
			if (stack == null)
				return 0;

			var removed = 0;
			string[] typeNames =
			{
				"Verse.EditWindow_Log",
				"Verse.Dialog_Error",
				"ZombieLand.Dialog_ErrorMessage"
			};
			foreach (var typeName in typeNames)
			{
				var type = GenTypes.GetTypeInAnyAssembly(typeName);
				if (type != null && stack.TryRemoveAssignableFromType(type, false))
					removed++;
			}
			return removed;
		}
	}
}
