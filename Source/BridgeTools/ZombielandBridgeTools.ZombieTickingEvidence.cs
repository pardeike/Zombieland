using Newtonsoft.Json;
using RimBridgeServer.Sdk;
using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public sealed partial class ZombielandBridgeTools
	{
		sealed class ZombieTickingFixturePlan
		{
			public Map map;
			public List<IntVec3> cells;
			public int visibleTarget;
			public int visibleCells;
			public int offscreenCells;
			public int colonists;
			public CellRect exactView;
			public CellRect protectedView;
			public string error;
		}

		public sealed class ZombieTickingEvidenceResult
		{
			public bool success { get; set; }
			public string stage { get; set; }
			public string error { get; set; }
			public string saveName { get; set; }
			public int requestedZombieCount { get; set; }
			public int warmupMilliseconds { get; set; }
			public int measurementMilliseconds { get; set; }
			public object execution { get; set; }
			public object environment { get; set; }
			public object configuration { get; set; }
			public List<object> samples { get; set; } = new List<object>();
			public object warnings { get; set; }
			public string evidencePath { get; set; }
		}

		static void Shuffle<T>(IList<T> values, System.Random random)
		{
			for (var i = values.Count - 1; i > 0; i--)
			{
				var j = random.Next(i + 1);
				(values[i], values[j]) = (values[j], values[i]);
			}
		}

		static bool ValidEvidenceSpawnCell(Map map, IntVec3 cell)
		{
			return cell.InBounds(map)
				&& cell.Standable(map)
				&& cell.Fogged(map) == false
				&& cell.GetEdifice(map) == null
				&& cell.GetFirstPawn(map) == null;
		}

		static ZombieTickingFixturePlan BuildZombieTickingFixturePlan(int zombieCount, int seed)
		{
			var map = CurrentMap;
			if (map == null)
				return new ZombieTickingFixturePlan { error = "No current map is loaded." };
			if (map.Size.x < 200 || map.Size.x > 300 || map.Size.z < 200 || map.Size.z > 300)
				return new ZombieTickingFixturePlan { map = map, error = $"Expected a normal 200..300 cell map, got {map.Size.x}x{map.Size.z}." };

			var colonists = map.mapPawns.FreeColonistsSpawned.Count;
			if (colonists == 0)
				return new ZombieTickingFixturePlan { map = map, error = "The base save has no spawned player colonist, so it cannot provide a real non-zero-threat player fixture." };

			var exactView = Find.CameraDriver.CurrentViewRect;
			exactView.ClipInsideMap(map);
			var protectedView = exactView.ExpandedBy(12);
			protectedView.ClipInsideMap(map);
			var random = new System.Random(seed);
			var visibleTarget = Math.Min(zombieCount, Math.Min(200, Math.Max(20, zombieCount / 10)));
			var visible = exactView.Cells.Where(cell => ValidEvidenceSpawnCell(map, cell)).ToList();
			Shuffle(visible, random);
			var selectedVisible = visible.Take(visibleTarget).ToList();

			var manager = map.GetComponent<TickManager>();
			var center = manager?.centerOfInterest ?? IntVec3.Invalid;
			var strictOffscreen = map.AllCells
				.Where(cell => protectedView.Contains(cell) == false)
				.Where(cell => ValidEvidenceSpawnCell(map, cell))
				.Where(cell => map.areaManager.Home[cell] == false)
				.Where(cell => center.IsValid == false || cell.DistanceToSquared(center) > 2025)
				.ToList();
			Shuffle(strictOffscreen, random);
			var neededOffscreen = zombieCount - selectedVisible.Count;
			var selectedOffscreen = strictOffscreen.Take(neededOffscreen).ToList();
			if (selectedOffscreen.Count < neededOffscreen)
			{
				var selected = new HashSet<IntVec3>(selectedVisible.Concat(selectedOffscreen));
				var relaxed = map.AllCells
					.Where(cell => protectedView.Contains(cell) == false)
					.Where(cell => selected.Contains(cell) == false)
					.Where(cell => ValidEvidenceSpawnCell(map, cell))
					.ToList();
				Shuffle(relaxed, random);
				selectedOffscreen.AddRange(relaxed.Take(neededOffscreen - selectedOffscreen.Count));
			}

			var cells = selectedVisible.Concat(selectedOffscreen).ToList();
			if (cells.Count != zombieCount)
				return new ZombieTickingFixturePlan
				{
					map = map,
					error = $"Only {cells.Count} valid visible/off-screen spawn cells were available for {zombieCount} zombies."
				};

			return new ZombieTickingFixturePlan
			{
				map = map,
				cells = cells,
				visibleTarget = visibleTarget,
				visibleCells = selectedVisible.Count,
				offscreenCells = selectedOffscreen.Count,
				colonists = colonists,
				exactView = exactView,
				protectedView = protectedView
			};
		}

		static object RuntimeEnvironmentEvidence()
		{
			return new
			{
				SystemInfo.operatingSystem,
				SystemInfo.deviceModel,
				SystemInfo.processorType,
				SystemInfo.processorCount,
				SystemInfo.systemMemorySize,
				SystemInfo.graphicsDeviceName,
				SystemInfo.graphicsMemorySize,
				unityVersion = Application.unityVersion,
				gameVersion = VersionControl.CurrentVersionStringWithRev
			};
		}

		static string ResolveEvidenceDirectory(string outputDirectory)
		{
			if (string.IsNullOrWhiteSpace(outputDirectory))
				return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "ZombieTickingEvidence");
			if (outputDirectory.StartsWith("~/", StringComparison.Ordinal))
				return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), outputDirectory.Substring(2));
			return Path.GetFullPath(outputDirectory);
		}

		[Tool("zombieland/zombie_ticking_create_player_fixture", Description = "Create one deterministic normal-map save with production-random zombie types, a visible cluster, and off-screen remote zombies.")]
		public static async Task<object> ZombieTickingCreatePlayerFixture(
			IRimBridgeContext ctx,
			CancellationToken cancellationToken,
			[ToolParameter(Description = "Existing normal-map player save used as the clean base.", DefaultValue = "quick-test")] string baseSaveName = "quick-test",
			[ToolParameter(Description = "Fixture save name. Empty uses ZL_Ticking_Player_COUNT.", DefaultValue = "")] string saveName = "",
			[ToolParameter(Description = "Number of production-random zombies to place.", DefaultValue = 100)] int zombieCount = 100,
			[ToolParameter(Description = "Configured maximum zombie count stored in the fixture.", DefaultValue = 2000)] int maximumZombies = 2000,
			[ToolParameter(Description = "Deterministic seed for cell order and production type generation.", DefaultValue = 711100)] int seed = 711100,
			[ToolParameter(Description = "Camera target X for the visible cluster.", DefaultValue = 125)] int cameraX = 125,
			[ToolParameter(Description = "Camera target Z for the visible cluster.", DefaultValue = 125)] int cameraZ = 125)
		{
			if (zombieCount < 25 || zombieCount > 2500)
				return new { success = false, stage = "validate", error = "zombieCount must be between 25 and 2500." };
			if (maximumZombies < zombieCount || maximumZombies > 5000)
				return new { success = false, stage = "validate", error = "maximumZombies must be at least zombieCount and at most 5000." };
			if (string.IsNullOrWhiteSpace(saveName))
				saveName = $"ZL_Ticking_Player_{zombieCount}";

			await zombieTickingBenchmarkGate.WaitAsync(cancellationToken);
			try
			{
				var load = await RequireBenchmarkCallAsync(ctx, cancellationToken, "fixture.load", "rimworld/load_game_ready", new
				{
					saveName = baseSaveName,
					readiness = "visual",
					pauseIfNeeded = true,
					timeoutMs = 120000
				});
				await RequireBenchmarkCallAsync(ctx, cancellationToken, "fixture.camera", "rimworld/jump_camera_to_cell", new { x = cameraX, z = cameraZ });

				var plan = await ctx.MainThread.InvokeAsync(() =>
				{
					var map = CurrentMap;
					ZombieTickingTelemetry.Cancel(map);
					ZombieRuntimeActions.DestroyZombies(map);
					ApplyZombieSettingsOverride(values =>
					{
						values.maximumNumberOfZombies = maximumZombies;
						// Scheduled zombie-free events intentionally change the population and
						// would make equal-wall-time speed samples incomparable.
						values.zombieFreeEvents = false;
						values.zombiesDieOnZeroThreat = false;
					});
					return BuildZombieTickingFixturePlan(zombieCount, seed);
				}, cancellationToken);
				if (plan == null || plan.error != null)
					return new { success = false, stage = "fixture.plan", error = plan?.error ?? "Fixture planning returned null." };

				var spawned = 0;
				const int batchSize = 20;
				for (var offset = 0; offset < plan.cells.Count; offset += batchSize)
				{
					var batchOffset = offset;
					var batchCount = Math.Min(batchSize, plan.cells.Count - batchOffset);
					spawned += await ctx.MainThread.InvokeAsync(() =>
					{
						var count = 0;
						for (var i = 0; i < batchCount; i++)
						{
							var index = batchOffset + i;
							Rand.PushState(seed + index * 7919);
							try
							{
								if (ZombieRuntimeActions.SpawnZombie(plan.cells[index], plan.map, ZombieType.Random, true) != null)
									count++;
							}
							finally
							{
								Rand.PopState();
							}
						}
						return count;
					}, cancellationToken);
					await ctx.Game.NextFrameAsync(cancellationToken);
				}

				if (spawned != zombieCount)
					return new { success = false, stage = "fixture.spawn", error = $"Spawned {spawned} of {zombieCount} zombies." };
				var population = await ctx.MainThread.InvokeAsync(() => ZombieTickingTelemetry.Inspect(plan.map), cancellationToken);
				var save = await RequireBenchmarkCallAsync(ctx, cancellationToken, "fixture.save", "rimworld/save_game", new { saveName });

				return new
				{
					success = true,
					baseSaveName,
					saveName,
					zombieCount,
					maximumZombies,
					seed,
					mapSize = new { x = plan.map.Size.x, z = plan.map.Size.z, area = plan.map.Size.x * plan.map.Size.z },
					plan.colonists,
					plan.visibleTarget,
					plannedVisible = plan.visibleCells,
					plannedOffscreen = plan.offscreenCells,
					exactView = ZombieRuntimeActions.DescribeCellRect(plan.exactView),
					protectedView = ZombieRuntimeActions.DescribeCellRect(plan.protectedView),
					population,
					load = load.Result,
					save = save.Result
				};
			}
			finally
			{
				zombieTickingBenchmarkGate.Release();
			}
		}

		static async Task<object> RunPlayerEvidenceSampleAsync(
			IRimBridgeContext ctx,
			CancellationToken cancellationToken,
			string saveName,
			string speed,
			int warmupMilliseconds,
			int measurementMilliseconds,
			int cameraX,
			int cameraZ)
		{
			var prefix = speed.ToLowerInvariant();
			var load = await RequireBenchmarkCallAsync(ctx, cancellationToken, $"{prefix}.load", "rimworld/load_game_ready", new
			{
				saveName,
				readiness = "visual",
				pauseIfNeeded = true,
				timeoutMs = 120000
			});
			// RimBridge enables RimWorld's private ultrafast debug boost at startup.
			// Disable it explicitly so these rows match the speeds available to a player.
			var playbackOptions = await RequireBenchmarkCallAsync(ctx, cancellationToken, $"{prefix}.player_speed_options", "rimworld/set_time_speed", new
			{
				speed = "Paused",
				ultraSpeedBoost = false
			});
			await RequireBenchmarkCallAsync(ctx, cancellationToken, $"{prefix}.camera", "rimworld/jump_camera_to_cell", new { x = cameraX, z = cameraZ });
			var warmup = await RequireBenchmarkCallAsync(ctx, cancellationToken, $"{prefix}.warmup", "rimworld/play_for", new
			{
				speed,
				durationMs = warmupMilliseconds,
				forceRequestedSpeed = false
			});
			// Match a player panning back to the horde after simulation warmup and
			// establish the same visible/off-screen split at measurement start.
			await RequireBenchmarkCallAsync(ctx, cancellationToken, $"{prefix}.camera_after_warmup", "rimworld/jump_camera_to_cell", new { x = cameraX, z = cameraZ });

			object telemetry = null;
			var telemetryStopped = false;
			try
			{
				var telemetryStart = await ctx.MainThread.InvokeAsync(() => ZombieTickingTelemetry.Start(CurrentMap), cancellationToken);
				if (SlowHostLoadSimulator.Enabled)
					await ctx.MainThread.InvokeAsync(SlowHostLoadSimulator.ResetCounters, cancellationToken);
				var before = await RequireBenchmarkCallAsync(ctx, cancellationToken, $"{prefix}.before", "zombieland/zombie_lightweight_perf_state");
				var run = await RequireBenchmarkCallAsync(ctx, cancellationToken, $"{prefix}.play", "rimworld/play_for", new
				{
					speed,
					durationMs = measurementMilliseconds,
					forceRequestedSpeed = false
				});
				telemetry = await ctx.MainThread.InvokeAsync(() => ZombieTickingTelemetry.Stop(CurrentMap), CancellationToken.None);
				telemetryStopped = true;
				var slowHost = SlowHostLoadSimulator.Enabled
					? await ctx.MainThread.InvokeAsync(SlowHostLoadSimulator.Snapshot, CancellationToken.None)
					: null;
				var after = await RequireBenchmarkCallAsync(ctx, cancellationToken, $"{prefix}.after", "zombieland/zombie_lightweight_perf_state");
				return new
				{
					speed,
					load = load.Result,
					playbackOptions = playbackOptions.Result,
					warmup = warmup.Result,
					telemetryStart,
					run = run.Result,
					telemetry,
					slowHost,
					after = after.Result
				};
			}
			finally
			{
				if (telemetryStopped == false)
				{
					try
					{
						await ctx.MainThread.InvokeAsync(() => ZombieTickingTelemetry.Cancel(CurrentMap), CancellationToken.None);
					}
					catch
					{
						ZombieTickingTelemetry.Cancel(CurrentMap);
					}
				}
			}
		}

		[Tool("zombieland/zombie_ticking_run_player_evidence", Description = "Reload one predefined player fixture per speed and measure real-time Normal/Fast/Superfast/Ultrafast scheduler behavior without internal tick stepping.")]
		public static async Task<object> ZombieTickingRunPlayerEvidence(
			IRimBridgeContext ctx,
			CancellationToken cancellationToken,
			[ToolParameter(Description = "Predefined fixture save name.")] string saveName,
			[ToolParameter(Description = "Real-time warmup before each measured sample in milliseconds.", DefaultValue = 2000)] int warmupMilliseconds = 2000,
			[ToolParameter(Description = "Real-time measured duration per speed in milliseconds.", DefaultValue = 10000)] int measurementMilliseconds = 10000,
			[ToolParameter(Description = "Camera target X matching the fixture's visible cluster.", DefaultValue = 125)] int cameraX = 125,
			[ToolParameter(Description = "Camera target Z matching the fixture's visible cluster.", DefaultValue = 125)] int cameraZ = 125,
			[ToolParameter(Description = "Directory for the raw JSON evidence. Empty uses Desktop/ZombieTickingEvidence.", DefaultValue = "")] string outputDirectory = "")
		{
			if (string.IsNullOrWhiteSpace(saveName))
				return new { success = false, stage = "validate", error = "saveName is required." };
			if (warmupMilliseconds < 500 || warmupMilliseconds > 30000)
				return new { success = false, stage = "validate", error = "warmupMilliseconds must be between 500 and 30000." };
			if (measurementMilliseconds < 2000 || measurementMilliseconds > 60000)
				return new { success = false, stage = "validate", error = "measurementMilliseconds must be between 2000 and 60000." };

			var result = new ZombieTickingEvidenceResult
			{
				saveName = saveName,
				warmupMilliseconds = warmupMilliseconds,
				measurementMilliseconds = measurementMilliseconds,
				execution = new
				{
					mode = "real-time rimworld/play_for",
					forceRequestedSpeed = false,
					ultraSpeedBoost = false,
					internalTickStepping = false,
					freshReloadBeforeEverySpeed = true,
					speeds = new[] { "Normal", "Fast", "Superfast", "Ultrafast" }
				}
			};

			await zombieTickingBenchmarkGate.WaitAsync(cancellationToken);
			try
			{
				if (zombieTickingTestModeEnabled)
					await SetZombieTickingTestModeAsync(ctx, false, CancellationToken.None);
				result.environment = await ctx.MainThread.InvokeAsync(RuntimeEnvironmentEvidence, cancellationToken);
				result.configuration = (await RequireBenchmarkCallAsync(ctx, cancellationToken, "configuration", "rimworld/get_mod_configuration_status")).Result;
				foreach (var speed in new[] { "Normal", "Fast", "Superfast", "Ultrafast" })
					result.samples.Add(await RunPlayerEvidenceSampleAsync(ctx, cancellationToken, saveName, speed,
						warmupMilliseconds, measurementMilliseconds, cameraX, cameraZ));
				result.warnings = (await RequireBenchmarkCallAsync(ctx, cancellationToken, "logs", "rimbridge/list_logs", new
				{
					minimumLevel = "warning",
					limit = 100
				})).Result;
				result.success = true;
				result.stage = "complete";
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (ZombieTickingBenchmarkStageException ex)
			{
				result.success = false;
				result.stage = ex.stage;
				result.error = ex.Message;
			}
			catch (Exception ex)
			{
				result.success = false;
				result.stage = "unexpected";
				result.error = $"{ex.GetType().Name}: {ex.Message}";
			}
			finally
			{
				ZombieTickingTelemetry.Cancel(CurrentMap);
				SlowHostLoadSimulator.DisableAndUnpatch();
				try
				{
					// Restore the bridge session's pre-existing debug default after the
					// player-representative samples, including failure and cancellation.
					await ctx.Tools.CallAsync("rimworld/set_time_speed", new { speed = "Paused", ultraSpeedBoost = true }, cancellationToken: CancellationToken.None);
				}
				catch
				{
					// The game may already be shutting down; there is no serialized state.
				}
				zombieTickingBenchmarkGate.Release();
			}

			var directory = ResolveEvidenceDirectory(outputDirectory);
			Directory.CreateDirectory(directory);
			result.evidencePath = Path.Combine(directory, Path.GetFileName(saveName) + ".json");
			File.WriteAllText(result.evidencePath, JsonConvert.SerializeObject(result, Formatting.Indented));
			return result;
		}
	}
}
