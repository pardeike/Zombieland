using RimBridgeServer.Sdk;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ZombieLand
{
	public sealed partial class ZombielandBridgeTools
	{
		sealed class ZombieTickingBenchmarkStageException : Exception
		{
			public readonly string stage;

			public ZombieTickingBenchmarkStageException(string stage, string message) : base(message)
			{
				this.stage = stage;
			}
		}

		public sealed class ZombieTickingBenchmarkResult
		{
			public bool success { get; set; }
			public string stage { get; set; }
			public string error { get; set; }
			public object initialLoad { get; set; }
			public object configuration { get; set; }
			public object testModeBegin { get; set; }
			public object testModeRestored { get; set; }
			public object feedback { get; set; }
			public object scheduler { get; set; }
			public object budget { get; set; }
			public object normal { get; set; }
			public object fast { get; set; }
			public object superfast { get; set; }
			public object ultrafast { get; set; }
			public object warnings { get; set; }
		}

		static readonly SemaphoreSlim zombieTickingBenchmarkGate = new(1, 1);

		static async Task<RimBridgeToolCallResult<object>> RequireBenchmarkCallAsync(
			IRimBridgeContext ctx,
			CancellationToken cancellationToken,
			string stage,
			string tool,
			object arguments = null)
		{
			var call = await ctx.Tools.CallAsync(tool, arguments, cancellationToken: cancellationToken);
			if (call.Succeeded())
				return call;

			var error = call?.Error;
			var message = error == null
				? $"{tool} returned status '{call?.Status ?? "unknown"}'."
				: $"{error.Code}: {error.Message}";
			throw new ZombieTickingBenchmarkStageException(stage, message);
		}

		static async Task<object> SetZombieTickingTestModeAsync(IRimBridgeContext ctx, bool enabled, CancellationToken cancellationToken)
		{
			return await ctx.MainThread.InvokeAsync(() => ZombieTickingTestMode(enabled), cancellationToken);
		}

		static async Task RunInZombieTickingTestModeAsync(
			IRimBridgeContext ctx,
			CancellationToken cancellationToken,
			Func<Task> body,
			Action<object> onBegin = null,
			Action<object> onRestored = null)
		{
			var begin = await SetZombieTickingTestModeAsync(ctx, true, cancellationToken);
			if (zombieTickingTestModeEnabled == false)
				throw new ZombieTickingBenchmarkStageException("test_mode.enable", "Could not enable the controlled zombie-ticking benchmark window.");

			try
			{
				onBegin?.Invoke(begin);
				await body();
			}
			finally
			{
				object restored;
				try
				{
					// Cleanup must outlive cancellation of the benchmark operation itself.
					restored = await SetZombieTickingTestModeAsync(ctx, false, CancellationToken.None);
				}
				catch
				{
					// The override only touches plain settings objects, so a direct fallback is
					// safe if the operation's main-thread dispatcher is already shutting down.
					restored = ZombieTickingTestMode(false);
				}
				onRestored?.Invoke(restored);
			}
		}

		static async Task<object> RunZombieTickingSpeedSampleAsync(
			IRimBridgeContext ctx,
			CancellationToken cancellationToken,
			string saveName,
			string speed,
			int spawnCount,
			int spawnX,
			int spawnZ,
			int spawnRadius,
			int cameraX,
			int cameraZ,
			int durationMs,
			bool forceRequestedSpeed)
		{
			var prefix = speed.ToLowerInvariant();
			var load = await RequireBenchmarkCallAsync(ctx, cancellationToken, $"{prefix}.load", "rimworld/load_game_ready", new
			{
				saveName,
				readiness = "visual",
				pauseIfNeeded = true,
				timeoutMs = 120000
			});

			// Loading swaps the active settings objects; keep the first snapshot, but
			// apply the temporary override to the newly loaded fixture.
			await SetZombieTickingTestModeAsync(ctx, true, cancellationToken);
			await RequireBenchmarkCallAsync(ctx, cancellationToken, $"{prefix}.camera", "rimworld/jump_camera_to_cell", new { x = cameraX, z = cameraZ });
			if (spawnCount > 0)
			{
				await RequireBenchmarkCallAsync(ctx, cancellationToken, $"{prefix}.spawn", "zombieland/spawn_zombie_group", new
				{
					x = spawnX,
					z = spawnZ,
					radius = spawnRadius,
					count = spawnCount,
					type = "Normal",
					appearDirectly = true
				});
			}

			var before = await RequireBenchmarkCallAsync(ctx, cancellationToken, $"{prefix}.before", "zombieland/zombie_lightweight_perf_state");
			var run = await RequireBenchmarkCallAsync(ctx, cancellationToken, $"{prefix}.play", "rimworld/play_for", new
			{
				speed,
				durationMs,
				forceRequestedSpeed
			});
			var after = await RequireBenchmarkCallAsync(ctx, cancellationToken, $"{prefix}.after", "zombieland/zombie_lightweight_perf_state");

			return new
			{
				load = load.Result,
				before = before.Result,
				run = run.Result,
				after = after.Result
			};
		}

		[Tool("zombieland/zombie_ticking_benchmark", Description = "Run the adaptive ticking contracts and independent speed samples inside a failure- and cancellation-safe test-mode scope.")]
		public static async Task<object> ZombieTickingBenchmark(
			IRimBridgeContext ctx,
			CancellationToken cancellationToken,
			[ToolParameter(Description = "Save name without .rws.")] string saveName,
			[ToolParameter(Description = "Normal zombies to spawn after each load; use zero for an existing dense fixture.", DefaultValue = 0)] int spawnCount = 0,
			[ToolParameter(Description = "Spawn center X.", DefaultValue = 10)] int spawnX = 10,
			[ToolParameter(Description = "Spawn center Z.", DefaultValue = 10)] int spawnZ = 10,
			[ToolParameter(Description = "Spawn search radius.", DefaultValue = 18)] int spawnRadius = 18,
			[ToolParameter(Description = "Remote camera X.", DefaultValue = 240)] int cameraX = 240,
			[ToolParameter(Description = "Remote camera Z.", DefaultValue = 240)] int cameraZ = 240,
			[ToolParameter(Description = "Real-time duration of each speed sample in milliseconds.", DefaultValue = 2500)] int durationMs = 2500,
			[ToolParameter(Description = "Suppress RimWorld forced slowdown while sampling.", DefaultValue = false)] bool forceRequestedSpeed = false,
			[ToolParameter(Description = "Run feedback, fairness, and path-payment contracts before the speed matrix.", DefaultValue = true)] bool runContracts = true)
		{
			var result = new ZombieTickingBenchmarkResult();
			if (string.IsNullOrWhiteSpace(saveName))
				return new { success = false, stage = "validate", error = "saveName is required." };
			if (spawnCount < 0 || spawnCount > 200)
				return new { success = false, stage = "validate", error = "spawnCount must be between 0 and 200." };
			if (spawnRadius < 1 || spawnRadius > 100)
				return new { success = false, stage = "validate", error = "spawnRadius must be between 1 and 100." };
			if (durationMs < 100 || durationMs > 60000)
				return new { success = false, stage = "validate", error = "durationMs must be between 100 and 60000." };

			await zombieTickingBenchmarkGate.WaitAsync(cancellationToken);
			try
			{
				// Recover an override left by an older script before loading the requested fixture.
				if (zombieTickingTestModeEnabled)
					await SetZombieTickingTestModeAsync(ctx, false, CancellationToken.None);

				var initialLoad = await RequireBenchmarkCallAsync(ctx, cancellationToken, "initial_load", "rimworld/load_game_ready", new
				{
					saveName,
					readiness = "visual",
					pauseIfNeeded = true,
					timeoutMs = 120000
				});
				result.initialLoad = initialLoad.Result;
				var configuration = await RequireBenchmarkCallAsync(ctx, cancellationToken, "configuration", "rimworld/get_mod_configuration_status");
				result.configuration = configuration.Result;

				await RunInZombieTickingTestModeAsync(ctx, cancellationToken, async () =>
				{
					if (runContracts)
					{
						result.feedback = (await RequireBenchmarkCallAsync(ctx, cancellationToken, "contracts.feedback", "zombieland/zombie_ticking_feedback_contract")).Result;
						result.scheduler = (await RequireBenchmarkCallAsync(ctx, cancellationToken, "contracts.scheduler", "zombieland/zombie_ticking_scheduler_contract", new { preparations = 240 })).Result;
						result.budget = (await RequireBenchmarkCallAsync(ctx, cancellationToken, "contracts.budget", "zombieland/zombie_ticking_budget_contract")).Result;
					}

					result.normal = await RunZombieTickingSpeedSampleAsync(ctx, cancellationToken, saveName, "Normal", spawnCount, spawnX, spawnZ, spawnRadius, cameraX, cameraZ, durationMs, forceRequestedSpeed);
					result.fast = await RunZombieTickingSpeedSampleAsync(ctx, cancellationToken, saveName, "Fast", spawnCount, spawnX, spawnZ, spawnRadius, cameraX, cameraZ, durationMs, forceRequestedSpeed);
					result.superfast = await RunZombieTickingSpeedSampleAsync(ctx, cancellationToken, saveName, "Superfast", spawnCount, spawnX, spawnZ, spawnRadius, cameraX, cameraZ, durationMs, forceRequestedSpeed);
					result.ultrafast = await RunZombieTickingSpeedSampleAsync(ctx, cancellationToken, saveName, "Ultrafast", spawnCount, spawnX, spawnZ, spawnRadius, cameraX, cameraZ, durationMs, forceRequestedSpeed);
					result.warnings = (await RequireBenchmarkCallAsync(ctx, cancellationToken, "logs", "rimbridge/list_logs", new { minimumLevel = "warning", limit = 100 })).Result;
					result.success = true;
					result.stage = "complete";
				}, begin => result.testModeBegin = begin, restored => result.testModeRestored = restored);
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
				zombieTickingBenchmarkGate.Release();
			}

			return result;
		}

		static string ZombieTickingTestModeFingerprint()
		{
			var values = ZombieSettings.Values;
			var current = values == null ? "null" : $"{values.zombiesDieOnZeroThreat}:{values.zombieFreeEvents}";
			var keyFrames = ZombieSettings.ValuesOverTime == null
				? "null"
				: string.Join(";", ZombieSettings.ValuesOverTime.Select(frame => frame?.values == null
					? "null"
					: $"{frame.values.zombiesDieOnZeroThreat}:{frame.values.zombieFreeEvents}"));
			return $"{current}|{keyFrames}";
		}

		[Tool("zombieland/zombie_ticking_benchmark_cleanup_contract", Description = "Inject an ordinary failure and cancellation through the benchmark scope and verify test-mode settings are restored in both cases.")]
		public static async Task<object> ZombieTickingBenchmarkCleanupContract(IRimBridgeContext ctx, CancellationToken cancellationToken)
		{
			if (ZombieSettings.Values == null)
				return new { success = false, error = "Zombieland settings are not initialized." };

			await zombieTickingBenchmarkGate.WaitAsync(cancellationToken);
			var initialSnapshot = SnapshotZombieSettings();
			var initialFingerprint = ZombieTickingTestModeFingerprint();
			var failureCaught = false;
			var cancellationCaught = false;
			try
			{
				try
				{
					await RunInZombieTickingTestModeAsync(ctx, cancellationToken,
						() => Task.FromException(new InvalidOperationException("Injected benchmark failure.")));
				}
				catch (InvalidOperationException)
				{
					failureCaught = true;
				}
				var afterFailureFingerprint = ZombieTickingTestModeFingerprint();
				var failureRestored = zombieTickingTestModeEnabled == false && afterFailureFingerprint == initialFingerprint;

				using (var source = new CancellationTokenSource())
				{
					try
					{
						await RunInZombieTickingTestModeAsync(ctx, cancellationToken, () =>
						{
							source.Cancel();
							return Task.FromCanceled(source.Token);
						});
					}
					catch (OperationCanceledException)
					{
						cancellationCaught = true;
					}
				}
				var afterCancellationFingerprint = ZombieTickingTestModeFingerprint();
				var cancellationRestored = zombieTickingTestModeEnabled == false && afterCancellationFingerprint == initialFingerprint;

				return new
				{
					success = failureCaught && failureRestored && cancellationCaught && cancellationRestored,
					failure = new { caught = failureCaught, restored = failureRestored, fingerprint = afterFailureFingerprint },
					cancellation = new { caught = cancellationCaught, restored = cancellationRestored, fingerprint = afterCancellationFingerprint },
					initialFingerprint
				};
			}
			finally
			{
				if (zombieTickingTestModeEnabled)
					ZombieTickingTestMode(false);
				RestoreZombieSettings(initialSnapshot);
				zombieTickingBenchmarkGate.Release();
			}
		}
	}
}
