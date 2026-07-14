using RimBridgeServer.Sdk;
using Newtonsoft.Json;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
		sealed class AlbinoRealtimeSample
		{
			public string sampleReason { get; set; }
			public string[] changedEntities { get; set; }
			public int ticksGame { get; set; }
			public int ticksAdvanced { get; set; }
			public int framesAdvanced { get; set; }
			public bool targetActive { get; set; }
			public bool destroyed { get; set; }
			public bool spawned { get; set; }
			public bool dead { get; set; }
			public bool downed { get; set; }
			public object position { get; set; }
			public int? hitPoints { get; set; }
			public float injurySeverity { get; set; }
			public string currentJob { get; set; }
			public string currentJobReport { get; set; }
			public bool patherMoving { get; set; }
			public object patherDestination { get; set; }
			public string screamPhase { get; set; }
			public int? albinoScream { get; set; }
			public int? albinoNextScreamTick { get; set; }
			public int? albinoScreamAffectedCount { get; set; }
			public AlbinoRealtimeDriverSample driver { get; set; }
			public int totalColonists { get; set; }
			public AlbinoRealtimeColonistSample[] colonists { get; set; }
			public AlbinoRealtimeDefensiveScreamSample defensiveScream { get; set; }
		}

		sealed class AlbinoRealtimeDriverSample
		{
			public bool active { get; set; }
			public object destination { get; set; }
			public string doorId { get; set; }
			public string doorDef { get; set; }
			public object doorPosition { get; set; }
			public object doorExitCell { get; set; }
			public string hackTargetId { get; set; }
			public string hackTargetDef { get; set; }
			public string hackTargetLabel { get; set; }
			public bool hackTargetSpawned { get; set; }
			public object hackTargetPosition { get; set; }
			public object hackApproachCell { get; set; }
			public int recentlyHackedTargetCount { get; set; }
			public object[] recentlyHackedTargets { get; set; }
			public int enoughHackedItemCount { get; set; }
			public object[] enoughHackedItems { get; set; }
			public bool noSafeHackRoute { get; set; }
			public bool interruptibleDestination { get; set; }
			public bool safetyDestination { get; set; }
			public bool fallbackDestination { get; set; }
			public int nextStrategicRecheckTick { get; set; }
			public int ticksUntilStrategicRecheck { get; set; }
			public object lastStrategicRecheckCell { get; set; }
			public object lastFallbackStartCell { get; set; }
			public object lastFallbackDestination { get; set; }
			public int nextFallbackMoveTick { get; set; }
			public int ticksUntilFallbackMove { get; set; }
			public string deferredHackTargetId { get; set; }
			public string deferredHackTargetLabel { get; set; }
			public int ticksUntilDeferredHackTarget { get; set; }
			public string rushHackTargetId { get; set; }
			public string rushHackTargetLabel { get; set; }
			public int ticksUntilRushHackTarget { get; set; }
			public object queuedScreamCell { get; set; }
			public object queuedMoveCell { get; set; }
			public int waitCounter { get; set; }
			public int hackCounter { get; set; }
			public int nextDefensiveScreamCheckTick { get; set; }
			public int ticksUntilDefensiveScreamCheck { get; set; }
			public object lastDefensiveScreamCheckCell { get; set; }
			public int nextDefensiveScreamCellCheckTick { get; set; }
			public int ticksUntilDefensiveScreamCellCheck { get; set; }
			public bool defensiveScreamQueued { get; set; }
			public bool screamReady { get; set; }
			public int? ticksUntilScreamReady { get; set; }
			public int? pathNodesLeft { get; set; }
			public int? pathNodeCount { get; set; }
			public object[] pathPreview { get; set; }
		}

		sealed class AlbinoRealtimeColonistSample
		{
			public string pawnId { get; set; }
			public string thingId { get; set; }
			public string label { get; set; }
			public string shortName { get; set; }
			public object position { get; set; }
			public int? distanceToAlbinoSquared { get; set; }
			public bool spawned { get; set; }
			public bool dead { get; set; }
			public bool downed { get; set; }
			public bool drafted { get; set; }
			public bool mentalState { get; set; }
			public bool vomiting { get; set; }
			public bool stunned { get; set; }
			public bool screamAffectable { get; set; }
			public bool pressureCapable { get; set; }
			public int? nextScreamPulseRadius { get; set; }
			public int? ticksUntilScreamPulseCanReach { get; set; }
			public string stance { get; set; }
			public string stanceType { get; set; }
			public string currentJob { get; set; }
			public string currentJobReport { get; set; }
			public AlbinoRealtimeTargetSample jobTargetA { get; set; }
			public AlbinoRealtimeTargetSample jobTargetB { get; set; }
			public AlbinoRealtimeTargetSample jobTargetC { get; set; }
			public bool patherMoving { get; set; }
			public object patherDestination { get; set; }
			public bool approachingAlbino { get; set; }
			public AlbinoRealtimeTargetSample aimingTarget { get; set; }
			public bool aimingAtAlbino { get; set; }
			public bool attackingOrApproachingAlbino { get; set; }
			public object effectiveVerb { get; set; }
			public bool rangedVerb { get; set; }
			public bool canShootAlbino { get; set; }
			public int pressureAtAlbino { get; set; }
		}

		sealed class AlbinoRealtimeTargetSample
		{
			public bool valid { get; set; }
			public string thingId { get; set; }
			public string thingDef { get; set; }
			public string label { get; set; }
			public object cell { get; set; }
			public bool pointsAtAlbino { get; set; }
		}

		sealed class AlbinoRealtimeDefensiveScreamSample
		{
			public bool hasHackTarget { get; set; }
			public bool hasActiveRoute { get; set; }
			public bool interruptibleQueuedPlannedScream { get; set; }
			public bool screamIdle { get; set; }
			public bool screamReady { get; set; }
			public bool emergencyScreamReady { get; set; }
			public int? ticksUntilScreamReady { get; set; }
			public int screamTargetsInRange { get; set; }
			public int screamTargetsInEarlyRange { get; set; }
			public int vomitingColonistsIgnored { get; set; }
			public int localPawnPressure { get; set; }
			public int localTurretPressure { get; set; }
			public int localPressure { get; set; }
			public int immediateMovementPressure { get; set; }
			public int? routePressureMax { get; set; }
			public bool routeUnsafe { get; set; }
			public bool urgentRangedThreatsInEarlyReach { get; set; }
			public bool pressureThresholdMet { get; set; }
			public bool defensivePayoffReady { get; set; }
			public bool wouldSwitchIfCheckedNow { get; set; }
			public bool wouldRedirectUnsafeRoute { get; set; }
			public object[] routeSamples { get; set; }
			public object[] turrets { get; set; }
		}

		sealed class AlbinoRealtimePressureSources
		{
			public List<Pawn> pawns = new();
			public List<Building_TurretGun> turrets = new();
		}

		sealed class AlbinoRealtimeTrackedState
		{
			public Dictionary<string, string> entries = new(StringComparer.Ordinal);
		}

		[Tool("zombieland/albino_realtime_until_dead", Description = "Spawn an albino at a current-map cell, set live RimWorld speed, and wait asynchronously until that albino is dead, destroyed, or despawned without stepping ticks in a tight loop.")]
		public static async Task<object> AlbinoRealtimeUntilDead(
			IRimBridgeContext ctx,
			CancellationToken cancellationToken,
			[ToolParameter(Description = "Target spawn x coordinate.", Required = false, DefaultValue = 120)] int x = 120,
			[ToolParameter(Description = "Target spawn z coordinate.", Required = false, DefaultValue = 162)] int z = 162,
			[ToolParameter(Description = "Maximum real-time wait in milliseconds.", Required = false, DefaultValue = 180000)] int timeoutMs = 180000,
			[ToolParameter(Description = "RimWorld play speed while watching: Normal, Fast, Superfast, or Ultrafast.", Required = false, DefaultValue = "Normal")] string speed = "Normal",
			[ToolParameter(Description = "Minimum game ticks between retained change samples. Use 1 to keep every tracked cell/state change.", Required = false, DefaultValue = 1)] int sampleEveryTicks = 1,
			[ToolParameter(Description = "Maximum intermediate change samples returned.", Required = false, DefaultValue = 300)] int maxSamples = 300,
			[ToolParameter(Description = "When true, pause RimWorld after the watch completes or times out.", Required = false, DefaultValue = false)] bool pauseWhenDone = false,
			[ToolParameter(Description = "When true, move the camera to the spawned albino.", Required = false, DefaultValue = false)] bool jumpCamera = false,
			[ToolParameter(Description = "When true, write every retained realtime sample to a JSONL trace file as it happens.", Required = false, DefaultValue = true)] bool writeTrace = true)
		{
			if (ctx == null)
				return new { success = false, error = "RimBridge context was not injected." };
			if (TryParseAlbinoRealtimeSpeed(speed, out var parsedSpeed, out var speedError) == false)
				return new { success = false, error = speedError };
			if (TryFindSpawnCell(x, z, out var map, out var cell, out var spawnError) == false)
				return spawnError;
			if (Current.ProgramState != ProgramState.Playing || Current.Game == null || Find.TickManager == null)
				return new { success = false, error = "No playable game is currently loaded." };
			if (LongEventHandler.AnyEventNowOrWaiting)
				return new { success = false, error = "RimWorld is busy with a long event." };

			var albino = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.Albino, true);
			if (albino == null)
			{
				return new
				{
					success = false,
					error = "ZombieGenerator.SpawnZombie returned no albino.",
					requestedCell = ZombieRuntimeActions.DescribeCell(new IntVec3(x, 0, z)),
					spawnCell = ZombieRuntimeActions.DescribeCell(cell)
				};
			}

			albino.Name = new NameSingle("ZL Realtime Albino");
			var targetId = ZombieRuntimeActions.StableThingId(albino);
			if (jumpCamera)
				Find.CameraDriver?.JumpToCurrentMapLoc(albino.Position);

			var clampedTimeoutMs = Math.Max(1000, Math.Min(timeoutMs, 600000));
			var clampedSampleEveryTicks = Math.Max(1, Math.Min(sampleEveryTicks, GenDate.TicksPerHour));
			var clampedMaxSamples = Math.Max(0, Math.Min(maxSamples, 1000));
			var samples = new List<AlbinoRealtimeSample>();
			var startTick = Find.TickManager.TicksGame;
			var startFrame = Time.frameCount;
			var previousSpeed = Find.TickManager.CurTimeSpeed;
			var stopwatch = Stopwatch.StartNew();
			var lastSampleTick = int.MinValue;
			AlbinoRealtimeTrackedState lastTrackedState = null;
			var finalSample = SampleAlbinoRealtime(albino, startTick, startFrame);
			var tracePath = writeTrace ? AlbinoRealtimeTracePath(targetId, startTick) : null;
			string traceError = null;
			StreamWriter traceWriter = null;
			if (tracePath != null)
			{
				try
				{
					Directory.CreateDirectory(Path.GetDirectoryName(tracePath));
					traceWriter = new StreamWriter(tracePath, false) { AutoFlush = true };
					traceWriter.WriteLine(JsonConvert.SerializeObject(new
					{
						kind = "metadata",
						targetId,
						requestedCell = ZombieRuntimeActions.DescribeCell(new IntVec3(x, 0, z)),
						spawnCell = ZombieRuntimeActions.DescribeCell(cell),
						startTick,
						speed = parsedSpeed.ToString()
					}));
				}
				catch (Exception ex)
				{
					traceError = $"{ex.GetType().Name}: {ex.Message}";
					tracePath = null;
					traceWriter?.Dispose();
					traceWriter = null;
				}
			}

			void RecordSample(bool force, string reason = null)
			{
				var ticksAdvanced = Math.Max(0, Find.TickManager.TicksGame - startTick);
				var trackedState = CaptureAlbinoRealtimeTrackedState(albino);
				var changedEntities = ChangedAlbinoRealtimeEntities(lastTrackedState, trackedState);
				if (force == false && changedEntities.Length == 0)
					return;
				if (force == false && ticksAdvanced - lastSampleTick < clampedSampleEveryTicks)
					return;

				var sample = SampleAlbinoRealtime(albino, startTick, startFrame);
				sample.sampleReason = reason ?? (changedEntities.Length == 0 ? "forced" : "tracked-change");
				sample.changedEntities = changedEntities;
				lastSampleTick = ticksAdvanced;
				lastTrackedState = trackedState;
				finalSample = sample;
				if (force || samples.Count < clampedMaxSamples)
					samples.Add(sample);
				if (traceWriter != null)
				{
					try
					{
						traceWriter.WriteLine(JsonConvert.SerializeObject(new
						{
							kind = "sample",
							sample
						}));
					}
					catch (Exception ex)
					{
						traceError = $"{ex.GetType().Name}: {ex.Message}";
						traceWriter.Dispose();
						traceWriter = null;
					}
				}
			}

			RecordSample(true, "initial");
			Find.TickManager.CurTimeSpeed = parsedSpeed;
			if (Find.TickManager.Paused)
				Find.TickManager.TogglePaused();

			RimBridgeWaitResult wait;
			var manualPauseMatched = false;
			try
			{
				wait = await ctx.Game.RunUntilAsync(() =>
				{
					RecordSample(false);
					if (AlbinoRealtimeTargetDone(albino))
						return true;
					manualPauseMatched = Find.TickManager?.CurTimeSpeed == TimeSpeed.Paused;
					return manualPauseMatched;
				}, new RimBridgeWaitOptions
				{
					TimeoutMs = clampedTimeoutMs,
					FailIfBusy = true
				}, cancellationToken);
			}
			finally
			{
				RecordSample(true, "final");
				traceWriter?.Dispose();
				if (pauseWhenDone && Current.Game != null && Find.TickManager != null)
					Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
			}

			var conditionMatched = AlbinoRealtimeTargetDone(albino);
			var stoppedBecause = conditionMatched ? "targetDone" : manualPauseMatched ? "manualPause" : wait.Status.ToString();
			return new
			{
				success = wait.Success && (conditionMatched || manualPauseMatched),
				conditionMatched,
				manualPauseMatched,
				stoppedBecause,
				targetId,
				requestedCell = ZombieRuntimeActions.DescribeCell(new IntVec3(x, 0, z)),
				spawnCell = ZombieRuntimeActions.DescribeCell(cell),
				timeoutMs = clampedTimeoutMs,
				speed = parsedSpeed.ToString(),
				previousSpeed = previousSpeed.ToString(),
				finalSpeed = Find.TickManager?.CurTimeSpeed.ToString(),
				pauseWhenDone,
				elapsedMilliseconds = stopwatch.ElapsedMilliseconds,
				wait = new
				{
					wait.Success,
					wait.Status,
					wait.Message,
					wait.ElapsedFrames,
					wait.StartTicksGame,
					wait.EndTicksGame,
					wait.AdvancedTicks
				},
				zombie = AlbinoRealtimeTargetDone(albino) ? null : DescribeZombie(albino),
				initial = samples.FirstOrDefault(),
				final = finalSample,
				sampleEveryTicks = clampedSampleEveryTicks,
				sampleMode = "tracked cell/state changes only",
				sampleLimit = clampedMaxSamples,
				sampleLimitReached = samples.Count >= clampedMaxSamples,
				sampleCount = samples.Count,
				tracePath,
				traceError,
				samples = samples.ToArray()
			};
		}

		static string AlbinoRealtimeTracePath(string targetId, int startTick)
		{
			var configRoot = GenFilePaths.ConfigFolderPath;
			var saveDataRoot = Directory.GetParent(configRoot)?.FullName ?? configRoot;
			var safeTargetId = string.Concat((targetId ?? "albino").Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
			return Path.Combine(saveDataRoot, "Logs", $"zombieland-albino-realtime-{startTick}-{safeTargetId}.jsonl");
		}

		static bool TryParseAlbinoRealtimeSpeed(string value, out TimeSpeed speed, out string error)
		{
			error = null;
			if (string.IsNullOrWhiteSpace(value))
			{
				speed = TimeSpeed.Normal;
				return true;
			}
			if (Enum.TryParse(value.Trim(), true, out speed) == false)
			{
				var names = string.Join(", ", Enum.GetNames(typeof(TimeSpeed)));
				error = $"Unknown time speed '{value}'. Supported values: {names}.";
				return false;
			}
			if (speed == TimeSpeed.Paused)
			{
				error = "The realtime albino watcher requires a non-paused play speed.";
				return false;
			}
			return true;
		}

		static bool AlbinoRealtimeTargetDone(Pawn pawn)
		{
			return pawn == null || pawn.Destroyed || pawn.Spawned == false || pawn.Dead;
		}

		static AlbinoRealtimeSample SampleAlbinoRealtime(Pawn pawn, int startTick, int startFrame)
		{
			var zombie = pawn as Zombie;
			var map = pawn?.Map ?? Find.CurrentMap;
			var driver = pawn?.jobs?.curDriver as JobDriver_Sabotage;
			return new AlbinoRealtimeSample
			{
				ticksGame = Find.TickManager?.TicksGame ?? 0,
				ticksAdvanced = Math.Max(0, (Find.TickManager?.TicksGame ?? 0) - startTick),
				framesAdvanced = Math.Max(0, Time.frameCount - startFrame),
				targetActive = AlbinoRealtimeTargetDone(pawn) == false,
				destroyed = pawn?.Destroyed ?? true,
				spawned = pawn?.Spawned ?? false,
				dead = pawn?.Dead ?? false,
				downed = pawn?.Downed ?? false,
				position = pawn?.Spawned == true ? ZombieRuntimeActions.DescribeCell(pawn.Position) : null,
				hitPoints = pawn?.Destroyed == false ? pawn.HitPoints : null,
				injurySeverity = pawn?.health?.hediffSet?.hediffs?.OfType<Hediff_Injury>().Sum(hediff => hediff.Severity) ?? 0f,
				currentJob = pawn?.CurJobDef?.defName,
				currentJobReport = SafeCurrentJobReport(pawn),
				patherMoving = pawn?.pather?.Moving ?? false,
				patherDestination = pawn?.pather?.Moving == true ? ZombieRuntimeActions.DescribeCell(pawn.pather.Destination.Cell) : null,
				screamPhase = DescribeAlbinoRealtimeScreamPhase(zombie, driver),
				albinoScream = zombie?.scream,
				albinoNextScreamTick = zombie?.albinoNextScreamTick,
				albinoScreamAffectedCount = zombie?.albinoScreamAffectedCount,
				driver = DescribeAlbinoRealtimeDriver(pawn),
				totalColonists = map?.mapPawns?.FreeColonistsSpawned?.Count() ?? 0,
				colonists = DescribeAlbinoRealtimeColonists(zombie),
				defensiveScream = DescribeAlbinoRealtimeDefensiveScream(zombie)
			};
		}

		static AlbinoRealtimeTrackedState CaptureAlbinoRealtimeTrackedState(Pawn pawn)
		{
			var state = new AlbinoRealtimeTrackedState();
			var zombie = pawn as Zombie;
			state.entries["albino"] = $"{CellKey(pawn)};job={pawn?.CurJobDef?.defName ?? "-"};move={pawn?.pather?.Moving ?? false};dest={CellKey(pawn?.pather?.Destination.Cell ?? IntVec3.Invalid)};scream={AlbinoRealtimeScreamStateKey(zombie)};dead={pawn?.Dead ?? true};spawned={pawn?.Spawned ?? false}";

			var driver = pawn?.jobs?.curDriver as JobDriver_Sabotage;
			if (driver == null)
				state.entries["driver"] = "-";
			else
			{
				var target = driver.hackTarget;
				state.entries["driver"] = $"dest={CellKey(driver.destination)};door={ZombieRuntimeActions.StableThingId(driver.door) ?? "-"};doorExit={CellKey(driver.doorExitCell)};target={ZombieRuntimeActions.StableThingId(target) ?? "-"};targetCell={CellKey(target)};approach={CellKey(driver.hackApproachCell)};deferred={ZombieRuntimeActions.StableThingId(driver.deferredHackTarget) ?? "-"};rush={ZombieRuntimeActions.StableThingId(driver.rushHackTarget) ?? "-"};noSafe={driver.noSafeHackRoute};interruptible={driver.interruptibleDestination};safety={driver.safetyDestination};queuedScream={CellKey(driver.queuedScreamCell)};queuedMove={CellKey(driver.queuedMoveCell)};hack={driver.hackCounter > 0};wait={driver.waitCounter > 0};defensive={driver.defensiveScreamQueued}";
			}

			foreach (var colonist in AlbinoRealtimeTrackedColonists(zombie))
			{
				var key = $"colonist:{colonist.ThingID}";
				var aimingTarget = colonist.TargetCurrentlyAimingAt;
				var attackingOrApproaching = zombie != null && IsAlbinoRealtimeAttackingOrApproaching(colonist, zombie);
				var verb = AlbinoRealtimeRangedVerb(colonist, out var rangedVerb);
				var stance = DescribeAlbinoRealtimeColonistStance(colonist, zombie, rangedVerb, AlbinoRealtimeLocalTargetPointsAtZombie(aimingTarget, zombie), attackingOrApproaching);
				state.entries[key] = $"{CellKey(colonist)};job={colonist.CurJobDef?.defName ?? "-"};stance={stance};drafted={colonist.drafter?.Drafted ?? false};move={colonist.pather?.Moving ?? false};dest={CellKey(colonist.pather?.Destination.Cell ?? IntVec3.Invalid)};aim={TargetKey(aimingTarget)};verb={verb?.GetType().Name ?? "-"}";
			}

			return state;
		}

		static string[] ChangedAlbinoRealtimeEntities(AlbinoRealtimeTrackedState previous, AlbinoRealtimeTrackedState current)
		{
			if (current == null)
				return Array.Empty<string>();
			if (previous == null)
				return current.entries.Keys.OrderBy(key => key).ToArray();

			var changed = new List<string>();
			foreach (var entry in current.entries)
				if (previous.entries.TryGetValue(entry.Key, out var previousValue) == false || previousValue != entry.Value)
					changed.Add(entry.Key);
			foreach (var key in previous.entries.Keys)
				if (current.entries.ContainsKey(key) == false)
					changed.Add(key);
			return changed.OrderBy(key => key).ToArray();
		}

		static string CellKey(Thing thing)
		{
			return thing?.Spawned == true ? CellKey(thing.Position) : "-";
		}

		static string CellKey(IntVec3 cell)
		{
			return cell.IsValid ? $"{cell.x},{cell.z}" : "-";
		}

		static string TargetKey(LocalTargetInfo target)
		{
			var thing = ZombieRuntimeActions.StableThingId(target.Thing);
			var cell = target.Cell.IsValid ? CellKey(target.Cell) : "-";
			return $"{thing ?? "-"}@{cell}";
		}

		static IEnumerable<Pawn> AlbinoRealtimeTrackedColonists(Zombie albino)
		{
			var map = albino?.Map ?? Find.CurrentMap;
			if (map?.mapPawns == null)
				return Enumerable.Empty<Pawn>();

			return map.mapPawns.FreeColonistsSpawned
				.Where(pawn => pawn?.Spawned == true)
				.OrderBy(pawn => albino?.Spawned == true ? pawn.Position.DistanceToSquared(albino.Position) : int.MaxValue)
				.ThenBy(pawn => pawn.ThingID)
				.Take(3)
				.ToArray();
		}

		static AlbinoRealtimeDriverSample DescribeAlbinoRealtimeDriver(Pawn pawn)
		{
			var driver = pawn?.jobs?.curDriver as JobDriver_Sabotage;
			if (driver == null)
				return new AlbinoRealtimeDriverSample { active = false, pathPreview = Array.Empty<object>() };

			var ticks = GenTicks.TicksGame;
			var zombie = pawn as Zombie;
			var screamReady = zombie != null && zombie.albinoNextScreamTick >= 0 && ticks >= zombie.albinoNextScreamTick;
			var path = pawn?.pather?.curPath;
			var target = driver.hackTarget;
			var door = driver.door;
			var deferredTarget = driver.deferredHackTarget;
			var rushTarget = driver.rushHackTarget;
			driver.PausedHackTargetCount();
			return new AlbinoRealtimeDriverSample
			{
				active = true,
				destination = driver.destination.IsValid ? ZombieRuntimeActions.DescribeCell(driver.destination) : null,
				doorId = ZombieRuntimeActions.StableThingId(door),
				doorDef = door?.def?.defName,
				doorPosition = door?.Spawned == true ? ZombieRuntimeActions.DescribeCell(door.Position) : null,
				doorExitCell = driver.doorExitCell.IsValid ? ZombieRuntimeActions.DescribeCell(driver.doorExitCell) : null,
				hackTargetId = ZombieRuntimeActions.StableThingId(target),
				hackTargetDef = target?.def?.defName,
				hackTargetLabel = target?.LabelCap.ToString(),
				hackTargetSpawned = target?.Spawned ?? false,
				hackTargetPosition = target?.Spawned == true ? ZombieRuntimeActions.DescribeCell(target.Position) : null,
				hackApproachCell = driver.hackApproachCell.IsValid ? ZombieRuntimeActions.DescribeCell(driver.hackApproachCell) : null,
				recentlyHackedTargetCount = driver.recentlyHackedTargets?.Count ?? 0,
				recentlyHackedTargets = DescribeAlbinoRealtimePausedHackTargets(driver, ticks),
				enoughHackedItemCount = AlbinoEnoughHackedItemCount(pawn?.Map),
				enoughHackedItems = DescribeAlbinoRealtimeEnoughHackedItems(pawn?.Map),
				noSafeHackRoute = driver.noSafeHackRoute,
				interruptibleDestination = driver.interruptibleDestination,
				safetyDestination = driver.safetyDestination,
				fallbackDestination = driver.fallbackDestination,
				nextStrategicRecheckTick = driver.nextStrategicRecheckTick,
				ticksUntilStrategicRecheck = Math.Max(0, driver.nextStrategicRecheckTick - ticks),
				lastStrategicRecheckCell = driver.lastStrategicRecheckCell.IsValid ? ZombieRuntimeActions.DescribeCell(driver.lastStrategicRecheckCell) : null,
				lastFallbackStartCell = driver.lastFallbackStartCell.IsValid ? ZombieRuntimeActions.DescribeCell(driver.lastFallbackStartCell) : null,
				lastFallbackDestination = driver.lastFallbackDestination.IsValid ? ZombieRuntimeActions.DescribeCell(driver.lastFallbackDestination) : null,
				nextFallbackMoveTick = driver.nextFallbackMoveTick,
				ticksUntilFallbackMove = Math.Max(0, driver.nextFallbackMoveTick - ticks),
				deferredHackTargetId = ZombieRuntimeActions.StableThingId(deferredTarget),
				deferredHackTargetLabel = deferredTarget?.LabelCap.ToString(),
				ticksUntilDeferredHackTarget = Math.Max(0, driver.deferredHackTargetPauseUntilTick - ticks),
				rushHackTargetId = ZombieRuntimeActions.StableThingId(rushTarget),
				rushHackTargetLabel = rushTarget?.LabelCap.ToString(),
				ticksUntilRushHackTarget = Math.Max(0, driver.rushHackTargetUntilTick - ticks),
				queuedScreamCell = driver.queuedScreamCell.IsValid ? ZombieRuntimeActions.DescribeCell(driver.queuedScreamCell) : null,
				queuedMoveCell = driver.queuedMoveCell.IsValid ? ZombieRuntimeActions.DescribeCell(driver.queuedMoveCell) : null,
				waitCounter = driver.waitCounter,
				hackCounter = driver.hackCounter,
				nextDefensiveScreamCheckTick = driver.nextDefensiveScreamCheckTick,
				ticksUntilDefensiveScreamCheck = Math.Max(0, driver.nextDefensiveScreamCheckTick - ticks),
				lastDefensiveScreamCheckCell = driver.lastDefensiveScreamCheckCell.IsValid ? ZombieRuntimeActions.DescribeCell(driver.lastDefensiveScreamCheckCell) : null,
				nextDefensiveScreamCellCheckTick = driver.nextDefensiveScreamCellCheckTick,
				ticksUntilDefensiveScreamCellCheck = Math.Max(0, driver.nextDefensiveScreamCellCheckTick - ticks),
				defensiveScreamQueued = driver.defensiveScreamQueued,
				screamReady = screamReady,
				ticksUntilScreamReady = zombie == null || zombie.albinoNextScreamTick < 0 ? null : Math.Max(0, zombie.albinoNextScreamTick - ticks),
				pathNodesLeft = path?.NodesLeftCount,
				pathNodeCount = path?.NodesReversed?.Count,
				pathPreview = DescribeAlbinoRealtimePath(path, 8)
			};
		}

		static object[] DescribeAlbinoRealtimePausedHackTargets(JobDriver_Sabotage driver, int ticks)
		{
			if (driver?.recentlyHackedTargets == null || driver.recentlyHackedTargetPauseUntilTicks == null)
				return Array.Empty<object>();

			var count = Math.Min(driver.recentlyHackedTargets.Count, driver.recentlyHackedTargetPauseUntilTicks.Count);
			var result = new List<object>(count);
			for (var i = 0; i < count; i++)
			{
				var target = driver.recentlyHackedTargets[i];
				if (target == null)
					continue;
				result.Add(new
				{
					id = ZombieRuntimeActions.StableThingId(target),
					def = target.def?.defName,
					label = target.LabelCap.ToString(),
					position = target.Spawned ? ZombieRuntimeActions.DescribeCell(target.Position) : null,
					ticksRemaining = Math.Max(0, driver.recentlyHackedTargetPauseUntilTicks[i] - ticks)
				});
			}
			return result.ToArray();
		}

		static object[] DescribeAlbinoRealtimeEnoughHackedItems(Map map)
		{
			return AlbinoEnoughHackedItemsSnapshot(map)
				.Select(target => new
				{
					id = ZombieRuntimeActions.StableThingId(target),
					def = target.def?.defName,
					label = target.LabelCap.ToString(),
					position = target.Spawned ? ZombieRuntimeActions.DescribeCell(target.Position) : null
				})
				.ToArray();
		}

		static AlbinoRealtimeColonistSample[] DescribeAlbinoRealtimeColonists(Zombie albino)
		{
			return AlbinoRealtimeTrackedColonists(albino)
				.Select(pawn => DescribeAlbinoRealtimeColonist(pawn, albino))
				.ToArray();
		}

		static AlbinoRealtimeColonistSample DescribeAlbinoRealtimeColonist(Pawn pawn, Zombie albino)
		{
			var distanceSquared = pawn?.Spawned == true && albino?.Spawned == true ? pawn.Position.DistanceToSquared(albino.Position) : (int?)null;
			var verb = AlbinoRealtimeRangedVerb(pawn, out var rangedVerb);
			var aimingTarget = DescribeAlbinoRealtimeTarget(pawn?.TargetCurrentlyAimingAt ?? LocalTargetInfo.Invalid, albino);
			var aimingAtAlbino = aimingTarget.pointsAtAlbino;
			var attackingOrApproaching = albino != null && IsAlbinoRealtimeAttackingOrApproaching(pawn, albino);
			var approaching = pawn?.pather?.Moving == true && albino?.Spawned == true
				&& pawn.pather.Destination.Cell.DistanceToSquared(albino.Position) < pawn.Position.DistanceToSquared(albino.Position);
			var canShootAlbino = albino?.Spawned == true && CanAlbinoRealtimeShootCell(pawn, albino.Position);
			var screamAffectable = AlbinoRealtimeCanScreamAffect(pawn, albino);
			var pressureCapable = AlbinoRealtimeCanPressurePawn(pawn, albino);
			return new AlbinoRealtimeColonistSample
			{
				pawnId = ZombieRuntimeActions.StableThingId(pawn),
				thingId = pawn?.ThingID,
				label = pawn?.LabelCap.ToString(),
				shortName = pawn?.Name?.ToStringShort,
				position = pawn?.Spawned == true ? ZombieRuntimeActions.DescribeCell(pawn.Position) : null,
				distanceToAlbinoSquared = distanceSquared,
				spawned = pawn?.Spawned ?? false,
				dead = pawn?.Dead ?? false,
				downed = pawn?.Downed ?? false,
				drafted = pawn?.drafter?.Drafted ?? false,
				mentalState = pawn?.InMentalState ?? false,
				vomiting = IsAlbinoRealtimeVomiting(pawn),
				stunned = pawn?.stances?.stunner?.Stunned ?? false,
				screamAffectable = screamAffectable,
				pressureCapable = pressureCapable,
				nextScreamPulseRadius = AlbinoRealtimeNextScreamPulseRadius(albino),
				ticksUntilScreamPulseCanReach = AlbinoRealtimeTicksUntilScreamPulseCanReach(albino, distanceSquared, screamAffectable),
				stance = DescribeAlbinoRealtimeColonistStance(pawn, albino, rangedVerb, aimingAtAlbino, attackingOrApproaching),
				stanceType = pawn?.stances?.curStance?.GetType().Name,
				currentJob = pawn?.CurJobDef?.defName,
				currentJobReport = SafeCurrentJobReport(pawn),
				jobTargetA = DescribeAlbinoRealtimeTarget(pawn?.CurJob?.targetA ?? LocalTargetInfo.Invalid, albino),
				jobTargetB = DescribeAlbinoRealtimeTarget(pawn?.CurJob?.targetB ?? LocalTargetInfo.Invalid, albino),
				jobTargetC = DescribeAlbinoRealtimeTarget(pawn?.CurJob?.targetC ?? LocalTargetInfo.Invalid, albino),
				patherMoving = pawn?.pather?.Moving ?? false,
				patherDestination = pawn?.pather?.Moving == true ? ZombieRuntimeActions.DescribeCell(pawn.pather.Destination.Cell) : null,
				approachingAlbino = approaching,
				aimingTarget = aimingTarget,
				aimingAtAlbino = aimingAtAlbino,
				attackingOrApproachingAlbino = attackingOrApproaching,
				effectiveVerb = DescribeAlbinoRealtimeVerb(verb),
				rangedVerb = rangedVerb,
				canShootAlbino = canShootAlbino,
				pressureAtAlbino = albino?.Spawned == true ? AlbinoRealtimePawnPressureAtCell(albino, albino.Position, pawn) : 0
			};
		}

		static AlbinoRealtimeDefensiveScreamSample DescribeAlbinoRealtimeDefensiveScream(Zombie albino)
		{
			if (albino?.Spawned != true)
				return null;

			const int minPressure = 6;
			const int softPressure = 4;
			const float maxRadius = 12f;
			var ticks = GenTicks.TicksGame;
			var driver = albino.jobs?.curDriver as JobDriver_Sabotage;
			Thing target = driver?.hackTarget;
			if (target == null && driver?.door != null)
				target = driver.door;
			var hasActiveRoute = target != null || driver?.destination.IsValid == true || albino.pather?.Moving == true;
			var interruptibleQueuedPlannedScream = albino.scream == -2 && driver?.defensiveScreamQueued == false && driver?.destination.IsValid == true;
			var sources = AlbinoRealtimePressureSourcesFor(albino);
			var localPawnPressure = AlbinoRealtimePawnPressureAtCell(albino, albino.Position, sources.pawns);
			var localTurretPressure = AlbinoRealtimeTurretPressureAtCell(albino.Position, sources.turrets);
			var immediateMovementPressure = AlbinoRealtimeImmediateMovementPressure(albino, sources);
			var routeSamples = hasActiveRoute == false
				? Array.Empty<IntVec3>()
				: AlbinoRealtimeRouteSamples(albino, target).Distinct().ToArray();
			var routePressureMax = routeSamples.Length == 0
				? (int?)null
				: routeSamples.Max(cell => AlbinoRealtimePressureAtCell(albino, cell, sources));
			var screamTargets = sources.pawns.Where(pawn => AlbinoRealtimeCanScreamAffect(pawn, albino)).ToList();
			var screamTargetsInRange = screamTargets.Count(pawn => pawn.Position.DistanceToSquared(albino.Position) <= maxRadius * maxRadius);
			var screamTargetsInEarlyRange = screamTargets.Count(pawn => pawn.Position.DistanceToSquared(albino.Position) <= 64);
			var localPressure = localPawnPressure + localTurretPressure;
			var maximumPressure = Math.Max(localPressure, routePressureMax ?? 0);
			var immediateThreat = HasImmediateAlbinoRealtimeScreamThreat(albino, sources);
			var urgentRangedThreatsInEarlyReach = AlbinoRealtimeUrgentRangedThreatsAreInEarlyScreamReach(albino, sources);
			var pressureThresholdMet = urgentRangedThreatsInEarlyReach && ((screamTargetsInEarlyRange >= 2 && maximumPressure >= softPressure)
				|| (immediateThreat && maximumPressure >= softPressure)
				|| (maximumPressure >= minPressure && immediateThreat));
			var defensivePayoffReady = AlbinoRealtimeDefensiveScreamPayoff(albino, sources);
			var routeUnsafe = maximumPressure >= softPressure || driver?.noSafeHackRoute == true;
			var emergencyScreamReady = AlbinoRealtimeDefensiveEmergencyScreamReady(albino, sources, ticks);
			var screamReady = interruptibleQueuedPlannedScream || albino.albinoNextScreamTick >= 0 && ticks >= albino.albinoNextScreamTick || emergencyScreamReady;
			var canSwitchScreamState = albino.scream == -1 || interruptibleQueuedPlannedScream;
			var routeConsidered = hasActiveRoute || driver?.noSafeHackRoute == true;
			var safetyMoveConsidered = routeConsidered || immediateMovementPressure >= softPressure;
			return new AlbinoRealtimeDefensiveScreamSample
			{
				hasHackTarget = target != null,
				hasActiveRoute = hasActiveRoute,
				interruptibleQueuedPlannedScream = interruptibleQueuedPlannedScream,
				screamIdle = albino.scream == -1,
				screamReady = screamReady,
				emergencyScreamReady = emergencyScreamReady,
				ticksUntilScreamReady = albino.albinoNextScreamTick < 0 ? null : Math.Max(0, albino.albinoNextScreamTick - ticks),
				screamTargetsInRange = screamTargetsInRange,
				screamTargetsInEarlyRange = screamTargetsInEarlyRange,
				vomitingColonistsIgnored = albino.Map.mapPawns.FreeColonistsSpawned.Count(IsAlbinoRealtimeVomiting),
				localPawnPressure = localPawnPressure,
				localTurretPressure = localTurretPressure,
				localPressure = localPressure,
				immediateMovementPressure = immediateMovementPressure,
				routePressureMax = routePressureMax,
				routeUnsafe = routeUnsafe,
				urgentRangedThreatsInEarlyReach = urgentRangedThreatsInEarlyReach,
				pressureThresholdMet = pressureThresholdMet,
				defensivePayoffReady = defensivePayoffReady,
				wouldSwitchIfCheckedNow = routeConsidered && canSwitchScreamState && screamReady && (pressureThresholdMet || (driver?.noSafeHackRoute == true && defensivePayoffReady)),
				wouldRedirectUnsafeRoute = safetyMoveConsidered && canSwitchScreamState && routeUnsafe && immediateMovementPressure >= softPressure && (screamReady == false || defensivePayoffReady == false),
				routeSamples = routeSamples.Select(ZombieRuntimeActions.DescribeCell).ToArray(),
				turrets = sources.turrets.Select(turret => new
				{
					id = ZombieRuntimeActions.StableThingId(turret),
					defName = turret.def?.defName,
					label = turret.LabelCap.ToString(),
					position = ZombieRuntimeActions.DescribeCell(turret.Position),
					canShootAlbino = CanAlbinoRealtimeShootCell(turret, albino.Position),
					pressureAtAlbino = CanAlbinoRealtimeShootCell(turret, albino.Position) ? 4 : 0
				}).ToArray()
			};
		}

		static object[] DescribeAlbinoRealtimePath(PawnPath path, int maxCells)
		{
			if (path?.Found != true || path.NodesReversed == null || path.NodesReversed.Count == 0)
				return Array.Empty<object>();

			return path.NodesReversed
				.Take(Math.Max(0, maxCells))
				.Select(ZombieRuntimeActions.DescribeCell)
				.ToArray();
		}

		static AlbinoRealtimeTargetSample DescribeAlbinoRealtimeTarget(LocalTargetInfo target, Zombie albino)
		{
			var thing = target.Thing;
			var cell = target.Cell;
			return new AlbinoRealtimeTargetSample
			{
				valid = thing != null || cell.IsValid,
				thingId = ZombieRuntimeActions.StableThingId(thing),
				thingDef = thing?.def?.defName,
				label = thing?.LabelCap.ToString(),
				cell = cell.IsValid ? ZombieRuntimeActions.DescribeCell(cell) : null,
				pointsAtAlbino = albino != null && AlbinoRealtimeLocalTargetPointsAtZombie(target, albino)
			};
		}

		static string DescribeAlbinoRealtimeColonistStance(Pawn pawn, Zombie albino, bool rangedVerb, bool aimingAtAlbino, bool attackingOrApproaching)
		{
			if (pawn == null || pawn.Destroyed)
				return "missing";
			if (pawn.Dead)
				return "dead";
			if (pawn.Downed)
				return "downed";
			if (IsAlbinoRealtimeVomiting(pawn))
				return "vomiting";
			if (aimingAtAlbino && rangedVerb)
				return "aiming";
			if (pawn.pather?.Moving == true)
				return "walking";
			if (attackingOrApproaching && rangedVerb)
				return "aiming";
			if (attackingOrApproaching)
				return "attacking";
			return "standing";
		}

		static object DescribeAlbinoRealtimeVerb(Verb verb)
		{
			if (verb == null)
				return null;

			return new
			{
				type = verb.GetType().Name,
				label = verb.verbProps?.label,
				range = verb.verbProps?.range ?? 0f,
				isMeleeAttack = verb.IsMeleeAttack
			};
		}

		static Verb AlbinoRealtimeRangedVerb(Pawn pawn, out bool rangedVerb)
		{
			var verb = (pawn as IAttackTargetSearcher)?.CurrentEffectiveVerb;
			rangedVerb = verb != null && verb.IsMeleeAttack == false;
			return verb;
		}

		static bool IsAlbinoRealtimeVomiting(Pawn pawn)
		{
			return pawn?.CurJobDef == JobDefOf.Vomit;
		}

		static bool IsAlbinoRealtimeDrafted(Pawn pawn)
		{
			return pawn?.drafter?.Drafted == true;
		}

		static bool IsAlbinoRealtimeZombie(Pawn pawn)
		{
			return pawn is Zombie || pawn is ZombieSymbiant || pawn is ZombieSpitter;
		}

		static bool IsAlbinoRealtimeHumanlikeFleshPawn(Pawn pawn)
		{
			var raceProps = pawn?.RaceProps;
			return raceProps?.Humanlike == true
				&& raceProps.IsFlesh
				&& AlienTools.IsFleshPawn(pawn)
				&& SoSTools.IsHologram(pawn) == false;
		}

		static bool AlbinoRealtimeCanScreamAffect(Pawn pawn, Zombie albino)
		{
			return pawn != null
				&& albino != null
				&& pawn.Spawned
				&& pawn.Map == albino.Map
				&& pawn.Dead == false
				&& IsAlbinoRealtimeZombie(pawn) == false
				&& Customization.DoesAttractsZombies(pawn)
				&& IsAlbinoRealtimeHumanlikeFleshPawn(pawn)
				&& pawn.health?.Downed == false
				&& pawn.jobs != null
				&& pawn.stances != null
				&& pawn.InMentalState == false
				&& IsAlbinoRealtimeVomiting(pawn) == false;
		}

		static bool AlbinoRealtimeCanPressurePawn(Pawn pawn, Zombie albino)
		{
			if (pawn == null
				|| albino == null
				|| pawn.Spawned == false
				|| pawn.Map != albino.Map
				|| pawn.Dead
				|| pawn.health?.Downed == true
				|| pawn.jobs == null
				|| pawn.stances == null
				|| IsAlbinoRealtimeZombie(pawn)
				|| IsAlbinoRealtimeVomiting(pawn)
				|| pawn.activity?.IsDormant == true
				|| pawn.activity?.Deactivated == true
				|| pawn.canBeDormant?.Awake == false
				|| (pawn.RaceProps?.Humanlike == true && pawn.InfectionState() >= InfectionState.Infecting))
				return false;

			if (IsAlbinoRealtimeAttackingOrApproaching(pawn, albino))
				return true;

			var raceProps = pawn.RaceProps;
			if (raceProps == null)
				return false;

			var faction = pawn.Faction;
			var settings = ZombieSettings.Values;
			var isHuman = IsAlbinoRealtimeHumanlikeFleshPawn(pawn);
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
				if (settings.enemiesAttackZombies == false)
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

		static string AlbinoRealtimeScreamStateKey(Zombie zombie)
		{
			if (zombie == null)
				return "-";
			if (zombie.scream < 0)
				return zombie.scream.ToString();

			return $"active:{zombie.scream / 40}";
		}

		static string DescribeAlbinoRealtimeScreamPhase(Zombie zombie, JobDriver_Sabotage driver)
		{
			if (zombie == null)
				return "missing";
			if (zombie.scream == -1)
				return "idle";
			if (zombie.scream == -2)
				return driver?.defensiveScreamQueued == true ? "queued-defensive" : "queued-planned";
			if (zombie.scream == 0 && driver?.waitCounter > 0)
				return driver.defensiveScreamQueued ? "windup-defensive" : "windup";
			return zombie.scream >= 300 ? "pulse-released" : "pulse-blocking";
		}

		static int AlbinoRealtimeScreamPulseRadiusAt(int screamTick)
		{
			return 1 + (int)(Math.Min(Math.Max(0, screamTick), 400) * 12f / 401);
		}

		static int? AlbinoRealtimeNextScreamPulseRadius(Zombie zombie)
		{
			if (zombie == null || zombie.scream < 0)
				return null;

			var nextPulseTick = Math.Max(40, ((zombie.scream + 39) / 40) * 40);
			if (nextPulseTick > 400)
				return null;

			return AlbinoRealtimeScreamPulseRadiusAt(nextPulseTick);
		}

		static int? AlbinoRealtimeTicksUntilScreamPulseCanReach(Zombie zombie, int? distanceSquared, bool screamAffectable)
		{
			if (zombie == null || zombie.scream < 0 || distanceSquared == null || screamAffectable == false)
				return null;

			var nextPulseTick = Math.Max(40, ((zombie.scream + 39) / 40) * 40);
			for (var tick = nextPulseTick; tick <= 400; tick += 40)
			{
				var radius = AlbinoRealtimeScreamPulseRadiusAt(tick);
				if (distanceSquared <= radius * radius)
					return Math.Max(0, tick - zombie.scream);
			}
			return null;
		}

		static bool AlbinoRealtimeLocalTargetPointsAtZombie(LocalTargetInfo target, Zombie albino)
		{
			if (target.HasThing)
				return target.Thing == albino;

			return target.Cell.IsValid && albino?.Spawned == true && target.Cell.DistanceToSquared(albino.Position) <= 2;
		}

		static bool IsAlbinoRealtimeAttackingOrApproaching(Pawn pawn, Zombie albino)
		{
			if (pawn == null || albino?.Spawned != true)
				return false;
			if (AlbinoRealtimeLocalTargetPointsAtZombie(pawn.TargetCurrentlyAimingAt, albino))
				return true;

			var job = pawn.CurJob;
			if (job != null && (AlbinoRealtimeLocalTargetPointsAtZombie(job.targetA, albino) || AlbinoRealtimeLocalTargetPointsAtZombie(job.targetB, albino) || AlbinoRealtimeLocalTargetPointsAtZombie(job.targetC, albino)))
				return true;

			if (pawn.pather?.Moving == true)
			{
				var currentDistance = pawn.Position.DistanceToSquared(albino.Position);
				var destinationDistance = pawn.pather.Destination.Cell.DistanceToSquared(albino.Position);
				if (destinationDistance < currentDistance && currentDistance <= 324)
					return true;
			}

			return false;
		}

		static bool AlbinoRealtimeHasUsableRangedVerb(IAttackTargetSearcher searcher, out Verb verb)
		{
			verb = searcher?.CurrentEffectiveVerb;
			return verb != null && verb.IsMeleeAttack == false;
		}

		static bool CanAlbinoRealtimeShootCell(IAttackTargetSearcher searcher, IntVec3 cell)
		{
			if (searcher?.Thing?.Spawned != true || cell.IsValid == false || AlbinoRealtimeHasUsableRangedVerb(searcher, out var verb) == false)
				return false;

			var origin = searcher.Thing.Position;
			var range = verb.verbProps?.range ?? 0f;
			if (range > 0f && origin.DistanceToSquared(cell) > range * range)
				return false;

			return verb.CanHitTargetFrom(origin, new LocalTargetInfo(cell));
		}

		static bool IsAlbinoRealtimeActiveTurret(Building_TurretGun turret)
		{
			if (turret?.Spawned != true || turret.Faction != Faction.OfPlayer)
				return false;
			var power = turret.powerComp ?? turret.TryGetComp<CompPowerTrader>();
			return power == null || power.PowerOn;
		}

		static AlbinoRealtimePressureSources AlbinoRealtimePressureSourcesFor(Zombie albino)
		{
			var sources = new AlbinoRealtimePressureSources();
			if (albino?.Map == null)
				return sources;

			var seen = new HashSet<Pawn>();
			void AddPawn(Pawn pawn)
			{
				if (AlbinoRealtimeCanPressurePawn(pawn, albino) && seen.Add(pawn))
					sources.pawns.Add(pawn);
			}

			foreach (var pawn in albino.Map.mapPawns.AllPawnsSpawned)
				if (pawn.IsColonist || pawn.Faction == Faction.OfPlayer || pawn.Faction?.HostileTo(Faction.OfPlayer) == true)
					AddPawn(pawn);
			foreach (var pawn in albino.Map.attackTargetsCache.TargetsHostileToColony.OfType<Pawn>())
				AddPawn(pawn);
			foreach (var turret in albino.Map.listerBuildings.allBuildingsColonist.OfType<Building_TurretGun>())
				if (IsAlbinoRealtimeActiveTurret(turret))
					sources.turrets.Add(turret);

			return sources;
		}

		static int AlbinoRealtimePawnPressureAtCell(Zombie albino, IntVec3 cell, List<Pawn> pawns)
		{
			var score = 0;
			foreach (var pawn in pawns)
				score += AlbinoRealtimePawnPressureAtCell(albino, cell, pawn);
			return score;
		}

		static int AlbinoRealtimePawnPressureAtCell(Zombie albino, IntVec3 cell, Pawn pawn)
		{
			if (pawn?.Spawned != true || albino?.Spawned != true || cell.IsValid == false)
				return 0;

			var distance = pawn.Position.DistanceToSquared(cell);
			var activeResponse = IsAlbinoRealtimeAttackingOrApproaching(pawn, albino);
			var rangedShooter = AlbinoRealtimeHasUsableRangedVerb(pawn, out _);
			if (rangedShooter && CanAlbinoRealtimeShootCell(pawn, cell))
				return 4;
			if (distance > 144)
				return 0;
			if (distance <= 4)
				return activeResponse ? 4 : 2;
			if (distance <= 25)
				return activeResponse ? 3 : 1;
			return activeResponse ? 2 : 0;
		}

		static int AlbinoRealtimeTurretPressureAtCell(IntVec3 cell, List<Building_TurretGun> turrets)
		{
			var score = 0;
			foreach (var turret in turrets)
				if (CanAlbinoRealtimeShootCell(turret, cell))
					score += 4;
			return score;
		}

		static int AlbinoRealtimePressureAtCell(Zombie albino, IntVec3 cell, AlbinoRealtimePressureSources sources)
		{
			return AlbinoRealtimePawnPressureAtCell(albino, cell, sources.pawns) + AlbinoRealtimeTurretPressureAtCell(cell, sources.turrets);
		}

		static int AlbinoRealtimeImmediateMovementPressure(Zombie albino, AlbinoRealtimePressureSources sources)
		{
			if (albino?.Spawned != true || sources == null)
				return 0;

			var maxPressure = AlbinoRealtimePressureAtCell(albino, albino.Position, sources);
			if (albino.pather?.Moving != true)
				return maxPressure;

			var destination = albino.pather.Destination.Cell;
			if (destination.IsValid)
				maxPressure = Math.Max(maxPressure, AlbinoRealtimePressureAtCell(albino, destination, sources));

			var path = albino.pather.curPath;
			if (path?.Found != true || path.NodesLeftCount <= 0)
				return maxPressure;

			var count = Math.Min(path.NodesLeftCount, 5);
			for (var i = 0; i < count; i++)
			{
				var cell = path.Peek(i);
				if (cell.IsValid)
					maxPressure = Math.Max(maxPressure, AlbinoRealtimePressureAtCell(albino, cell, sources));
			}
			return maxPressure;
		}

		static bool HasImmediateAlbinoRealtimeScreamThreat(Zombie albino, AlbinoRealtimePressureSources sources)
		{
			foreach (var pawn in sources.pawns.Where(pawn => AlbinoRealtimeCanScreamAffect(pawn, albino)))
			{
				var distance = pawn.Position.DistanceToSquared(albino.Position);
				if (distance <= 9)
					return true;
				if (distance <= 25 && IsAlbinoRealtimeAttackingOrApproaching(pawn, albino))
					return true;
			}
			return false;
		}

		static bool IsAlbinoRealtimeUrgentRangedThreat(Pawn pawn, Zombie albino)
		{
			return pawn?.Spawned == true
				&& albino?.Spawned == true
				&& AlbinoRealtimeLocalTargetPointsAtZombie(pawn.TargetCurrentlyAimingAt, albino)
				&& AlbinoRealtimeHasUsableRangedVerb(pawn, out _)
				&& CanAlbinoRealtimeShootCell(pawn, albino.Position);
		}

		static bool AlbinoRealtimeUrgentRangedThreatsAreInEarlyScreamReach(Zombie albino, AlbinoRealtimePressureSources sources)
		{
			if (albino?.Spawned != true || sources == null)
				return true;

			foreach (var pawn in sources.pawns.Where(pawn => AlbinoRealtimeCanScreamAffect(pawn, albino)))
				if (IsAlbinoRealtimeUrgentRangedThreat(pawn, albino)
					&& pawn.Position.DistanceToSquared(albino.Position) > 36)
					return false;
			return true;
		}

		static bool AlbinoRealtimeDefensiveScreamPayoff(Zombie albino, AlbinoRealtimePressureSources sources)
		{
			if (AlbinoRealtimeUrgentRangedThreatsAreInEarlyScreamReach(albino, sources) == false)
				return false;

			var inEarlyRange = sources.pawns.Count(pawn => AlbinoRealtimeCanScreamAffect(pawn, albino) && pawn.Position.DistanceToSquared(albino.Position) <= 64);
			return inEarlyRange >= 2 || HasImmediateAlbinoRealtimeScreamThreat(albino, sources);
		}

		static bool AlbinoRealtimeDefensiveEmergencyScreamReady(Zombie albino, AlbinoRealtimePressureSources sources, int ticks)
		{
			const int maxRemainingCooldownTicks = 2400;
			const int threatRadiusSquared = 16;
			if (albino?.Spawned != true || sources == null || albino.albinoNextScreamTick < 0)
				return false;
			var ticksUntilReady = Math.Max(0, albino.albinoNextScreamTick - ticks);
			if (ticksUntilReady <= 0 || ticksUntilReady > maxRemainingCooldownTicks)
				return false;
			if (AlbinoRealtimeDefensiveScreamPayoff(albino, sources) == false)
				return false;

			foreach (var pawn in sources.pawns.Where(pawn => AlbinoRealtimeCanScreamAffect(pawn, albino)))
			{
				if (pawn.Position.DistanceToSquared(albino.Position) > threatRadiusSquared)
					continue;
				if (IsAlbinoRealtimeUrgentRangedThreat(pawn, albino) || IsAlbinoRealtimeAttackingOrApproaching(pawn, albino) || CanAlbinoRealtimeShootCell(pawn, albino.Position))
					return true;
			}
			return false;
		}

		static IEnumerable<IntVec3> AlbinoRealtimeRouteSamples(Zombie albino, Thing target)
		{
			yield return albino.Position;
			if (albino.pather?.Moving == true && albino.pather.Destination.Cell.IsValid)
				yield return albino.pather.Destination.Cell;
			if (target?.Spawned == true)
				yield return target.Position;

			var path = albino.pather?.curPath;
			if (path?.Found != true || path.NodesLeftCount <= 0)
				yield break;

			const int maxSamples = 12;
			var step = Math.Max(1, path.NodesReversed.Count / maxSamples);
			var yielded = 0;
			for (var i = 0; i < path.NodesReversed.Count && yielded < maxSamples; i += step)
			{
				var cell = path.NodesReversed[i];
				if (cell.IsValid)
				{
					yielded++;
					yield return cell;
				}
			}
		}
	}
}
