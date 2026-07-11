using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RimBridgeServer.Sdk;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Verse;

namespace ZombieLand
{
	public sealed partial class ZombielandBridgeTools
	{
		static class SlowHostLoadSimulator
		{
			const string harmonyId = "brrainz.zombieland.bridgetools.slow-host";
			static readonly object patchLock = new object();
			static readonly Harmony harmony = new Harmony(harmonyId);
			static readonly MethodInfo tickMethod = AccessTools.Method(typeof(Verse.TickManager), nameof(Verse.TickManager.DoSingleTick));
			static readonly MethodInfo prefixMethod = AccessTools.Method(typeof(SlowHostLoadSimulator), nameof(BeforeNativeGameTick));
			static bool installed;
			static int enabled;
			static long baseStopwatchTicks;
			static long spikeStopwatchTicks;
			static int spikeIntervalTicks;
			static long calls;
			static long spikes;
			static long elapsedStopwatchTicks;
			static long maximumElapsedStopwatchTicks;

			public static bool Enabled => Volatile.Read(ref enabled) != 0;

			static long MillisecondsToStopwatchTicks(double milliseconds)
			{
				return Math.Max(0L, (long)Math.Round(milliseconds * Stopwatch.Frequency / 1000d));
			}

			static double StopwatchTicksToMilliseconds(long ticks)
			{
				return ticks * 1000d / Stopwatch.Frequency;
			}

			static void EnsureInstalled()
			{
				lock (patchLock)
				{
					if (installed)
						return;
					if (tickMethod == null || prefixMethod == null)
						throw new InvalidOperationException("Could not resolve Verse.TickManager.DoSingleTick or the slow-host prefix.");
					harmony.Patch(tickMethod, prefix: new HarmonyMethod(prefixMethod) { priority = Priority.First });
					installed = true;
				}
			}

			public static object Configure(double baseMilliseconds, double spikeMilliseconds, int spikeInterval)
			{
				if (baseMilliseconds < 0d || baseMilliseconds > 50d)
					throw new ArgumentOutOfRangeException(nameof(baseMilliseconds));
				if (spikeMilliseconds < 0d || spikeMilliseconds > 100d)
					throw new ArgumentOutOfRangeException(nameof(spikeMilliseconds));
				if (spikeInterval < 1 || spikeInterval > 60000)
					throw new ArgumentOutOfRangeException(nameof(spikeInterval));

				EnsureInstalled();
				Interlocked.Exchange(ref baseStopwatchTicks, MillisecondsToStopwatchTicks(baseMilliseconds));
				Interlocked.Exchange(ref spikeStopwatchTicks, MillisecondsToStopwatchTicks(spikeMilliseconds));
				Volatile.Write(ref spikeIntervalTicks, spikeInterval);
				ResetCounters();
				Volatile.Write(ref enabled, 1);
				return Snapshot();
			}

			public static void ResetCounters()
			{
				Interlocked.Exchange(ref calls, 0);
				Interlocked.Exchange(ref spikes, 0);
				Interlocked.Exchange(ref elapsedStopwatchTicks, 0);
				Interlocked.Exchange(ref maximumElapsedStopwatchTicks, 0);
			}

			static void BeforeNativeGameTick()
			{
				if (Volatile.Read(ref enabled) == 0)
					return;

				var target = Interlocked.Read(ref baseStopwatchTicks);
				var interval = Volatile.Read(ref spikeIntervalTicks);
				var spike = interval > 0 && GenTicks.TicksGame % interval == 0;
				if (spike)
				{
					target += Interlocked.Read(ref spikeStopwatchTicks);
					Interlocked.Increment(ref spikes);
				}

				var start = Stopwatch.GetTimestamp();
				while (Stopwatch.GetTimestamp() - start < target)
					Thread.SpinWait(64);
				var elapsed = Stopwatch.GetTimestamp() - start;
				Interlocked.Increment(ref calls);
				Interlocked.Add(ref elapsedStopwatchTicks, elapsed);
				var observedMaximum = Interlocked.Read(ref maximumElapsedStopwatchTicks);
				while (elapsed > observedMaximum)
				{
					var previous = Interlocked.CompareExchange(ref maximumElapsedStopwatchTicks, elapsed, observedMaximum);
					if (previous == observedMaximum)
						break;
					observedMaximum = previous;
				}
			}

			public static object Snapshot()
			{
				var callCount = Interlocked.Read(ref calls);
				var elapsedTicks = Interlocked.Read(ref elapsedStopwatchTicks);
				return new
				{
					enabled = Enabled,
					patchInstalled = installed,
					patchOwner = harmonyId,
					placement = "Verse.TickManager.DoSingleTick prefix, Priority.First; before vanilla and Zombieland tick work",
					baseMillisecondsPerNativeTick = StopwatchTicksToMilliseconds(Interlocked.Read(ref baseStopwatchTicks)),
					spikeMilliseconds = StopwatchTicksToMilliseconds(Interlocked.Read(ref spikeStopwatchTicks)),
					spikeIntervalGameTicks = Volatile.Read(ref spikeIntervalTicks),
					calls = callCount,
					spikes = Interlocked.Read(ref spikes),
					measuredTotalMilliseconds = StopwatchTicksToMilliseconds(elapsedTicks),
					measuredAverageMilliseconds = callCount == 0 ? 0d : StopwatchTicksToMilliseconds(elapsedTicks) / callCount,
					measuredMaximumMilliseconds = StopwatchTicksToMilliseconds(Interlocked.Read(ref maximumElapsedStopwatchTicks))
				};
			}

			public static object DisableAndUnpatch()
			{
				Volatile.Write(ref enabled, 0);
				lock (patchLock)
				{
					if (installed && tickMethod != null)
						harmony.Unpatch(tickMethod, HarmonyPatchType.Prefix, harmonyId);
					installed = false;
				}
				return Snapshot();
			}
		}

		sealed class SlowHostProfile
		{
			public double baseMilliseconds;
			public double spikeMilliseconds;
			public int spikeIntervalTicks;
		}

		sealed class SlowHostEvidenceResult
		{
			public bool success { get; set; }
			public string stage { get; set; }
			public string error { get; set; }
			public string baselineSaveName { get; set; }
			public string[] fixtureSaveNames { get; set; }
			public double targetFastTicksPerSecond { get; set; }
			public object environment { get; set; }
			public object configuration { get; set; }
			public object execution { get; set; }
			public List<object> calibration { get; set; } = new List<object>();
			public object selectedProfile { get; set; }
			public List<object> hostOnlySamples { get; set; } = new List<object>();
			public List<object> fixtureSamples { get; set; } = new List<object>();
			public object warnings { get; set; }
			public object cleanup { get; set; }
			public string evidencePath { get; set; }
		}

		static (int advancedTicks, long elapsedMilliseconds, double ticksPerSecond) PlaybackRate(object result)
		{
			var token = result == null ? new JObject() : JToken.FromObject(result);
			var advancedTicks = token.Value<int?>("advancedTicks") ?? 0;
			var elapsedMilliseconds = token.Value<long?>("elapsedMs") ?? 0L;
			var ticksPerSecond = elapsedMilliseconds <= 0 ? 0d : advancedTicks * 1000d / elapsedMilliseconds;
			return (advancedTicks, elapsedMilliseconds, ticksPerSecond);
		}

		static async Task<object> RunHostOnlySlowSampleAsync(
			IRimBridgeContext ctx,
			CancellationToken cancellationToken,
			string saveName,
			string speed,
			int warmupMilliseconds,
			int measurementMilliseconds,
			SlowHostProfile profile)
		{
			var prefix = $"host_only.{speed.ToLowerInvariant()}";
			var load = await RequireBenchmarkCallAsync(ctx, cancellationToken, $"{prefix}.load", "rimworld/load_game_ready", new
			{
				saveName,
				readiness = "visual",
				pauseIfNeeded = true,
				timeoutMs = 120000
			});
			var prepared = await ctx.MainThread.InvokeAsync(() =>
			{
				var map = CurrentMap;
				var destroyed = map == null ? 0 : ZombieRuntimeActions.DestroyZombies(map);
				ApplyZombieSettingsOverride(values =>
				{
					values.maximumNumberOfZombies = 0;
					values.zombieFreeEvents = false;
					values.zombiesDieOnZeroThreat = false;
				});
				var slowHost = SlowHostLoadSimulator.Configure(profile.baseMilliseconds, profile.spikeMilliseconds, profile.spikeIntervalTicks);
				return new { destroyed, population = ZombieTickingTelemetry.Inspect(map), slowHost };
			}, cancellationToken);
			var playbackOptions = await RequireBenchmarkCallAsync(ctx, cancellationToken, $"{prefix}.player_speed_options", "rimworld/set_time_speed", new
			{
				speed = "Paused",
				ultraSpeedBoost = false
			});
			var warmup = await RequireBenchmarkCallAsync(ctx, cancellationToken, $"{prefix}.warmup", "rimworld/play_for", new
			{
				speed,
				durationMs = warmupMilliseconds,
				forceRequestedSpeed = false
			});

			object telemetry = null;
			var telemetryStopped = false;
			try
			{
				await ctx.MainThread.InvokeAsync(SlowHostLoadSimulator.ResetCounters, cancellationToken);
				var telemetryStart = await ctx.MainThread.InvokeAsync(() => ZombieTickingTelemetry.Start(CurrentMap), cancellationToken);
				var run = await RequireBenchmarkCallAsync(ctx, cancellationToken, $"{prefix}.play", "rimworld/play_for", new
				{
					speed,
					durationMs = measurementMilliseconds,
					forceRequestedSpeed = false
				});
				telemetry = await ctx.MainThread.InvokeAsync(() => ZombieTickingTelemetry.Stop(CurrentMap), CancellationToken.None);
				telemetryStopped = true;
				var slowHost = await ctx.MainThread.InvokeAsync(SlowHostLoadSimulator.Snapshot, CancellationToken.None);
				var rate = PlaybackRate(run.Result);
				return new
				{
					speed,
					load = load.Result,
					prepared,
					playbackOptions = playbackOptions.Result,
					warmup = warmup.Result,
					telemetryStart,
					run = run.Result,
					telemetry,
					slowHost,
					observed = new { rate.advancedTicks, rate.elapsedMilliseconds, rate.ticksPerSecond }
				};
			}
			finally
			{
				if (telemetryStopped == false)
					ZombieTickingTelemetry.Cancel(CurrentMap);
			}
		}

		[Tool("zombieland/zombie_ticking_slow_host_evidence", Description = "Calibrate a transient pre-Zombieland native-tick workload against a zero-zombie save, then run matched real-time player-speed matrices with predefined zombie fixtures.")]
		public static async Task<object> ZombieTickingSlowHostEvidence(
			IRimBridgeContext ctx,
			CancellationToken cancellationToken,
			[ToolParameter(Description = "Normal-map save used for the zero-zombie host-only calibration and baseline.", DefaultValue = "ZL_Dense_Performance_1500_base")] string baselineSaveName = "ZL_Dense_Performance_1500_base",
			[ToolParameter(Description = "Comma-separated predefined zombie fixture save names.", DefaultValue = "ZL_Ticking_Player_100,ZL_Ticking_Player_500,ZL_Ticking_Player_1000,ZL_Ticking_Player_2000")] string fixtureSaveNames = "ZL_Ticking_Player_100,ZL_Ticking_Player_500,ZL_Ticking_Player_1000,ZL_Ticking_Player_2000",
			[ToolParameter(Description = "Desired host-only Fast throughput used to calibrate aggregate pre-Zombieland tick cost.", DefaultValue = 105d)] double targetFastTicksPerSecond = 105d,
			[ToolParameter(Description = "Periodic extra main-thread cost in milliseconds.", DefaultValue = 8d)] double spikeMilliseconds = 8d,
			[ToolParameter(Description = "Game-tick interval between periodic load spikes.", DefaultValue = 60)] int spikeIntervalTicks = 60,
			[ToolParameter(Description = "Number of real-time binary-search calibration samples.", DefaultValue = 5)] int calibrationSamples = 5,
			[ToolParameter(Description = "Real-time duration of each calibration sample.", DefaultValue = 2500)] int calibrationMilliseconds = 2500,
			[ToolParameter(Description = "Real-time warmup before each evidence sample.", DefaultValue = 500)] int warmupMilliseconds = 500,
			[ToolParameter(Description = "Real-time duration of every final host-only and zombie sample.", DefaultValue = 10000)] int measurementMilliseconds = 10000,
			[ToolParameter(Description = "Camera target X matching the predefined fixtures.", DefaultValue = 125)] int cameraX = 125,
			[ToolParameter(Description = "Camera target Z matching the predefined fixtures.", DefaultValue = 125)] int cameraZ = 125,
			[ToolParameter(Description = "Directory for raw JSON. Empty uses Desktop/ZombieTickingEvidence/slow-host.", DefaultValue = "")] string outputDirectory = "")
		{
			var fixtures = (fixtureSaveNames ?? string.Empty)
				.Split(',')
				.Select(value => value.Trim())
				.Where(value => value.Length > 0)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToArray();
			if (string.IsNullOrWhiteSpace(baselineSaveName))
				return new { success = false, stage = "validate", error = "baselineSaveName is required." };
			if (fixtures.Length == 0 || fixtures.Length > 8)
				return new { success = false, stage = "validate", error = "Supply between one and eight fixture save names." };
			if (targetFastTicksPerSecond < 30d || targetFastTicksPerSecond > 175d)
				return new { success = false, stage = "validate", error = "targetFastTicksPerSecond must be between 30 and 175." };
			if (spikeMilliseconds < 0d || spikeMilliseconds > 100d || spikeIntervalTicks < 1 || spikeIntervalTicks > 60000)
				return new { success = false, stage = "validate", error = "The spike must be 0..100 ms at an interval of 1..60000 game ticks." };
			if (calibrationSamples < 3 || calibrationSamples > 8 || calibrationMilliseconds < 1500 || calibrationMilliseconds > 10000)
				return new { success = false, stage = "validate", error = "Use 3..8 calibration samples of 1500..10000 ms." };
			if (warmupMilliseconds < 500 || warmupMilliseconds > 30000 || measurementMilliseconds < 2000 || measurementMilliseconds > 60000)
				return new { success = false, stage = "validate", error = "Warmup must be 500..30000 ms and measurement 2000..60000 ms." };

			var result = new SlowHostEvidenceResult
			{
				baselineSaveName = baselineSaveName,
				fixtureSaveNames = fixtures,
				targetFastTicksPerSecond = targetFastTicksPerSecond,
				execution = new
				{
					mode = "real-time rimworld/play_for",
					forceRequestedSpeed = false,
					ultraSpeedBoost = false,
					internalTickStepping = false,
					freshReloadBeforeEverySpeed = true,
					syntheticPlacement = "DoSingleTick Priority.First prefix before vanilla and Zombieland processing",
					loadShape = "constant per-native-tick main-thread cost plus deterministic periodic spike"
				}
			};

			await zombieTickingBenchmarkGate.WaitAsync(cancellationToken);
			try
			{
				if (zombieTickingTestModeEnabled)
					await SetZombieTickingTestModeAsync(ctx, false, CancellationToken.None);
				result.environment = await ctx.MainThread.InvokeAsync(RuntimeEnvironmentEvidence, cancellationToken);
				result.configuration = (await RequireBenchmarkCallAsync(ctx, cancellationToken, "configuration", "rimworld/get_mod_configuration_status")).Result;

				var lowerMilliseconds = 0d;
				var upperMilliseconds = 25d;
				double bestMilliseconds = 0d;
				double bestDistance = double.MaxValue;
				for (var index = 0; index < calibrationSamples; index++)
				{
					var trialMilliseconds = (lowerMilliseconds + upperMilliseconds) / 2d;
					var profile = new SlowHostProfile
					{
						baseMilliseconds = trialMilliseconds,
						spikeMilliseconds = spikeMilliseconds,
						spikeIntervalTicks = spikeIntervalTicks
					};
					var sample = await RunHostOnlySlowSampleAsync(ctx, cancellationToken, baselineSaveName, "Fast", 500, calibrationMilliseconds, profile);
					var sampleToken = JToken.FromObject(sample);
					var observedTicksPerSecond = sampleToken.SelectToken("observed.ticksPerSecond")?.Value<double>() ?? 0d;
					var distance = Math.Abs(observedTicksPerSecond - targetFastTicksPerSecond);
					result.calibration.Add(new
					{
						index,
						trialBaseMilliseconds = trialMilliseconds,
						observedTicksPerSecond,
						distanceFromTarget = distance,
						sample
					});
					if (distance < bestDistance)
					{
						bestDistance = distance;
						bestMilliseconds = trialMilliseconds;
					}
					if (observedTicksPerSecond > targetFastTicksPerSecond)
						lowerMilliseconds = trialMilliseconds;
					else
						upperMilliseconds = trialMilliseconds;
				}

				var selectedProfile = new SlowHostProfile
				{
					baseMilliseconds = bestMilliseconds,
					spikeMilliseconds = spikeMilliseconds,
					spikeIntervalTicks = spikeIntervalTicks
				};
				result.selectedProfile = new
				{
					selectedProfile.baseMilliseconds,
					selectedProfile.spikeMilliseconds,
					selectedProfile.spikeIntervalTicks,
					bestDistanceFromTarget = bestDistance
				};

				foreach (var speed in new[] { "Normal", "Fast", "Superfast", "Ultrafast" })
					result.hostOnlySamples.Add(await RunHostOnlySlowSampleAsync(ctx, cancellationToken, baselineSaveName, speed,
						warmupMilliseconds, measurementMilliseconds, selectedProfile));

				foreach (var fixture in fixtures)
				{
					var samples = new List<object>();
					foreach (var speed in new[] { "Normal", "Fast", "Superfast", "Ultrafast" })
					{
						await ctx.MainThread.InvokeAsync(() => SlowHostLoadSimulator.Configure(selectedProfile.baseMilliseconds,
							selectedProfile.spikeMilliseconds, selectedProfile.spikeIntervalTicks), cancellationToken);
						samples.Add(await RunPlayerEvidenceSampleAsync(ctx, cancellationToken, fixture, speed,
							warmupMilliseconds, measurementMilliseconds, cameraX, cameraZ));
					}
					result.fixtureSamples.Add(new { saveName = fixture, samples });
				}

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
				try
				{
					result.cleanup = await ctx.MainThread.InvokeAsync(SlowHostLoadSimulator.DisableAndUnpatch, CancellationToken.None);
				}
				catch
				{
					result.cleanup = SlowHostLoadSimulator.DisableAndUnpatch();
				}
				try
				{
					await ctx.Tools.CallAsync("rimworld/set_time_speed", new { speed = "Paused", ultraSpeedBoost = true }, cancellationToken: CancellationToken.None);
				}
				catch
				{
				}
				zombieTickingBenchmarkGate.Release();
			}

			var directory = string.IsNullOrWhiteSpace(outputDirectory)
				? Path.Combine(ResolveEvidenceDirectory(string.Empty), "slow-host")
				: ResolveEvidenceDirectory(outputDirectory);
			Directory.CreateDirectory(directory);
			result.evidencePath = Path.Combine(directory, "ZL_SlowHost_Evidence.json");
			File.WriteAllText(result.evidencePath, JsonConvert.SerializeObject(result, Formatting.Indented));
			return result;
		}
	}
}
