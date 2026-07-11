using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public static class ZombieTickingTelemetry
	{
		static readonly long[] candidateTypes = new long[9];
		static readonly long[] selectedTypes = new long[9];
		static readonly long[] candidateStates = new long[5];
		static readonly long[] selectedStates = new long[5];

		static bool enabled;
		static int mapId = -1;
		static int generation;
		static int startGameTick;
		static int startFrame;
		static long startTimestamp;
		static object startPopulation;

		static long preparations;
		static long candidateOpportunities;
		static long selectedOpportunities;
		static long actualCustomTicks;
		static long exactVisibleCandidates;
		static long exactVisibleSelected;
		static long protectedCandidates;
		static long protectedSelected;
		static long outsideProtectedCandidates;
		static long outsideProtectedSelected;
		static long priorityCandidates;
		static long prioritySelected;
		static long remoteCandidates;
		static long remoteSelected;

		static long tickUpdateSamples;
		static double tickUpdateMillisecondsTotal;
		static float tickUpdateMillisecondsMaximum;
		static long tickUpdatesAbove16Milliseconds;
		static long tickUpdatesAbove45Milliseconds;
		static long allowedNativeTicks;
		static long completedNativeTicks;
		static long nativeTickShortfalls;
		static long normalSamples;
		static long throttledSamples;
		static long emergencySamples;
		static readonly long[] timeSpeedSamples = new long[5];
		static int minimumEffectiveMultiplier;
		static int maximumEffectiveMultiplier;
		static long effectiveMultiplierTotal;

		public static bool Enabled => enabled;

		public static bool EnabledFor(Map map)
		{
			return enabled && map != null && map.uniqueID == mapId;
		}

		public static object Start(Map map)
		{
			if (map == null)
				return new { success = false, error = "No map was supplied." };

			generation++;
			if (generation <= 0)
				generation = 1;
			mapId = map.uniqueID;
			startGameTick = GenTicks.TicksGame;
			startFrame = Time.frameCount;
			startTimestamp = Stopwatch.GetTimestamp();
			preparations = 0;
			candidateOpportunities = 0;
			selectedOpportunities = 0;
			actualCustomTicks = 0;
			exactVisibleCandidates = 0;
			exactVisibleSelected = 0;
			protectedCandidates = 0;
			protectedSelected = 0;
			outsideProtectedCandidates = 0;
			outsideProtectedSelected = 0;
			priorityCandidates = 0;
			prioritySelected = 0;
			remoteCandidates = 0;
			remoteSelected = 0;
			tickUpdateSamples = 0;
			tickUpdateMillisecondsTotal = 0d;
			tickUpdateMillisecondsMaximum = 0f;
			tickUpdatesAbove16Milliseconds = 0;
			tickUpdatesAbove45Milliseconds = 0;
			allowedNativeTicks = 0;
			completedNativeTicks = 0;
			nativeTickShortfalls = 0;
			normalSamples = 0;
			throttledSamples = 0;
			emergencySamples = 0;
			Array.Clear(timeSpeedSamples, 0, timeSpeedSamples.Length);
			minimumEffectiveMultiplier = int.MaxValue;
			maximumEffectiveMultiplier = 0;
			effectiveMultiplierTotal = 0;
			Array.Clear(candidateTypes, 0, candidateTypes.Length);
			Array.Clear(selectedTypes, 0, selectedTypes.Length);
			Array.Clear(candidateStates, 0, candidateStates.Length);
			Array.Clear(selectedStates, 0, selectedStates.Length);
			startPopulation = CapturePopulation(map);
			enabled = true;

			return new { success = true, generation, mapId, startGameTick, startFrame, population = startPopulation };
		}

		public static void BeginPreparation(Map map)
		{
			if (EnabledFor(map))
				preparations++;
		}

		public static void RecordCandidate(Zombie zombie, bool exactVisible, bool cameraProtected, bool priority)
		{
			if (enabled == false || zombie == null || zombie.Map?.uniqueID != mapId)
				return;

			EnsureZombieGeneration(zombie);
			candidateOpportunities++;
			zombie.telemetryCandidateOpportunities++;
			if (exactVisible)
			{
				exactVisibleCandidates++;
				zombie.telemetryVisibleCandidateOpportunities++;
			}
			if (cameraProtected)
				protectedCandidates++;
			else
				outsideProtectedCandidates++;
			if (priority)
				priorityCandidates++;
			else
				remoteCandidates++;

			candidateTypes[(int)TypeOf(zombie)]++;
			candidateStates[Mathf.Clamp((int)zombie.state, 0, candidateStates.Length - 1)]++;
		}

		public static void RecordSelected(Zombie zombie, bool exactVisible, bool cameraProtected, bool priority)
		{
			if (enabled == false || zombie == null || zombie.Map?.uniqueID != mapId)
				return;

			EnsureZombieGeneration(zombie);
			selectedOpportunities++;
			zombie.telemetrySelections++;
			if (exactVisible)
			{
				exactVisibleSelected++;
				zombie.telemetryVisibleSelections++;
			}
			if (cameraProtected)
				protectedSelected++;
			else
				outsideProtectedSelected++;
			if (priority)
				prioritySelected++;
			else
				remoteSelected++;

			selectedTypes[(int)TypeOf(zombie)]++;
			selectedStates[Mathf.Clamp((int)zombie.state, 0, selectedStates.Length - 1)]++;
			var tick = GenTicks.TicksGame;
			var gap = Math.Max(0, tick - zombie.telemetryLastSelectedGameTick);
			zombie.telemetryMaximumSelectionGap = Math.Max(zombie.telemetryMaximumSelectionGap, gap);
			zombie.telemetryLastSelectedGameTick = tick;
		}

		public static void RecordActualTicks(Map map, int count)
		{
			if (EnabledFor(map))
				actualCustomTicks += Math.Max(0, count);
		}

		public static void RecordTickUpdate(ZombieTicker.TickUpdateState state, int completedTicks, float elapsedMilliseconds)
		{
			if (enabled == false || state.eligible == false || state.game != Current.Game)
				return;

			tickUpdateSamples++;
			var timeSpeed = Find.TickManager?.CurTimeSpeed ?? TimeSpeed.Paused;
			timeSpeedSamples[Mathf.Clamp((int)timeSpeed, 0, timeSpeedSamples.Length - 1)]++;
			var elapsed = Math.Max(0f, elapsedMilliseconds);
			tickUpdateMillisecondsTotal += elapsed;
			tickUpdateMillisecondsMaximum = Math.Max(tickUpdateMillisecondsMaximum, elapsed);
			if (elapsed > 16.6667f)
				tickUpdatesAbove16Milliseconds++;
			if (elapsed > 45.4546f)
				tickUpdatesAbove45Milliseconds++;
			allowedNativeTicks += Math.Max(0, state.allowedTicks);
			completedNativeTicks += Math.Max(0, completedTicks);
			minimumEffectiveMultiplier = Math.Min(minimumEffectiveMultiplier, state.effectiveMultiplier);
			maximumEffectiveMultiplier = Math.Max(maximumEffectiveMultiplier, state.effectiveMultiplier);
			effectiveMultiplierTotal += Math.Max(0, state.effectiveMultiplier);
			if (state.allowedTicks > 0 && completedTicks < state.allowedTicks)
				nativeTickShortfalls++;

			switch (ZombieTicker.saturationState)
			{
				case ZombieSaturationState.Throttled:
					throttledSamples++;
					break;
				case ZombieSaturationState.Emergency:
					emergencySamples++;
					break;
				default:
					normalSamples++;
					break;
			}
		}

		public static object Stop(Map map)
		{
			if (enabled == false)
				return new { success = false, error = "Zombie ticking telemetry is not active." };
			if (map == null || map.uniqueID != mapId)
				return new { success = false, error = "The telemetry map is no longer active.", mapId };

			enabled = false;
			var endGameTick = GenTicks.TicksGame;
			var endFrame = Time.frameCount;
			var wallMilliseconds = (Stopwatch.GetTimestamp() - startTimestamp) * 1000d / Stopwatch.Frequency;
			var endPopulation = CapturePopulation(map);
			var zombies = map.mapPawns.AllPawnsSpawned
				.OfType<Zombie>()
				.Where(zombie => zombie != null && zombie.Spawned && zombie.Dead == false && zombie.telemetryGeneration == generation)
				.ToArray();
			var selectionCounts = zombies.Select(zombie => zombie.telemetrySelections).OrderBy(value => value).ToArray();
			var candidateCounts = zombies.Select(zombie => zombie.telemetryCandidateOpportunities).OrderBy(value => value).ToArray();
			var maximumGaps = zombies.Select(zombie => Math.Max(zombie.telemetryMaximumSelectionGap,
				Math.Max(0, endGameTick - zombie.telemetryLastSelectedGameTick))).OrderBy(value => value).ToArray();
			var invisibleCandidates = Math.Max(0L, candidateOpportunities - exactVisibleCandidates);
			var invisibleSelected = Math.Max(0L, selectedOpportunities - exactVisibleSelected);
			var frameCount = Math.Max(0, endFrame - startFrame);
			var gameTicks = Math.Max(0, endGameTick - startGameTick);

			return new
			{
				success = true,
				generation,
				mapId,
				timing = new
				{
					startGameTick,
					endGameTick,
					gameTicks,
					startFrame,
					endFrame,
					frames = frameCount,
					wallMilliseconds,
					ticksPerRealSecond = wallMilliseconds <= 0d ? 0d : gameTicks * 1000d / wallMilliseconds,
					framesPerRealSecond = wallMilliseconds <= 0d ? 0d : frameCount * 1000d / wallMilliseconds
				},
				population = new { start = startPopulation, end = endPopulation },
				scheduler = new
				{
					preparations,
					candidateOpportunities,
					selectedOpportunities,
					skippedOpportunities = Math.Max(0L, candidateOpportunities - selectedOpportunities),
					actualCustomTicks,
					selectedEqualsActuallyTicked = selectedOpportunities == actualCustomTicks,
					selectionRate = Ratio(selectedOpportunities, candidateOpportunities),
					averageCandidatesPerPreparation = Ratio(candidateOpportunities, preparations),
					averageSelectedPerPreparation = Ratio(selectedOpportunities, preparations)
				},
				visibility = new
				{
					exactVisible = Category(exactVisibleCandidates, exactVisibleSelected),
					invisible = Category(invisibleCandidates, invisibleSelected),
					cameraProtected = Category(protectedCandidates, protectedSelected),
					outsideCameraProtection = Category(outsideProtectedCandidates, outsideProtectedSelected)
				},
				priority = new
				{
					priority = Category(priorityCandidates, prioritySelected),
					remote = Category(remoteCandidates, remoteSelected)
				},
				byType = CategoryDictionary(candidateTypes, selectedTypes, index => ((ZombieType)index).ToString()),
				byState = CategoryDictionary(candidateStates, selectedStates, index => ((ZombieState)index).ToString()),
				fairness = new
				{
					observedZombies = zombies.Length,
					neverSelected = zombies.Count(zombie => zombie.telemetrySelections == 0),
					candidateOpportunities = Distribution(candidateCounts),
					selections = Distribution(selectionCounts),
					maximumSelectionGapGameTicks = Distribution(maximumGaps)
				},
				updateLoop = new
				{
					samples = tickUpdateSamples,
					averageMilliseconds = tickUpdateSamples == 0 ? 0d : tickUpdateMillisecondsTotal / tickUpdateSamples,
					maximumMilliseconds = tickUpdateMillisecondsMaximum,
					above16_67Milliseconds = tickUpdatesAbove16Milliseconds,
					above45_45Milliseconds = tickUpdatesAbove45Milliseconds,
					allowedNativeTicks,
					completedNativeTicks,
					completionRate = Ratio(completedNativeTicks, allowedNativeTicks),
					nativeTickShortfalls,
					effectiveMultiplier = new
					{
						minimum = minimumEffectiveMultiplier == int.MaxValue ? 0 : minimumEffectiveMultiplier,
						maximum = maximumEffectiveMultiplier,
						average = tickUpdateSamples == 0 ? 0d : effectiveMultiplierTotal / (double)tickUpdateSamples
					},
					saturationSamples = new { normal = normalSamples, throttled = throttledSamples, emergency = emergencySamples },
					timeSpeedSamples = CountDictionary(timeSpeedSamples, index => ((TimeSpeed)index).ToString())
				}
			};
		}

		public static object Cancel(Map map)
		{
			var wasEnabled = enabled;
			enabled = false;
			return new { success = true, wasEnabled, mapId = map?.uniqueID ?? -1 };
		}

		public static object Inspect(Map map)
		{
			return CapturePopulation(map);
		}

		static void EnsureZombieGeneration(Zombie zombie)
		{
			if (zombie.telemetryGeneration == generation)
				return;
			zombie.telemetryGeneration = generation;
			zombie.telemetryCandidateOpportunities = 0;
			zombie.telemetryVisibleCandidateOpportunities = 0;
			zombie.telemetrySelections = 0;
			zombie.telemetryVisibleSelections = 0;
			zombie.telemetryLastSelectedGameTick = startGameTick;
			zombie.telemetryMaximumSelectionGap = 0;
		}

		static ZombieType TypeOf(Zombie zombie)
		{
			if (zombie.IsSuicideBomber) return ZombieType.SuicideBomber;
			if (zombie.isToxicSplasher) return ZombieType.ToxicSplasher;
			if (zombie.IsTanky) return ZombieType.TankyOperator;
			if (zombie.isMiner) return ZombieType.Miner;
			if (zombie.isElectrifier) return ZombieType.Electrifier;
			if (zombie.isAlbino) return ZombieType.Albino;
			if (zombie.isDarkSlimer) return ZombieType.DarkSlimer;
			if (zombie.isHealer) return ZombieType.Healer;
			return ZombieType.Normal;
		}

		static object CapturePopulation(Map map)
		{
			var manager = map?.GetComponent<TickManager>();
			var zombies = map?.mapPawns?.AllPawnsSpawned?
				.OfType<Zombie>()
				.Where(zombie => zombie != null && zombie.Spawned && zombie.Dead == false)
				.ToArray() ?? Array.Empty<Zombie>();
			var hasCamera = Find.CurrentMap == map && Find.CameraDriver != null;
			var exactView = default(CellRect);
			var protectedView = default(CellRect);
			if (hasCamera)
			{
				exactView = Find.CameraDriver.CurrentViewRect;
				exactView.ClipInsideMap(map);
				protectedView = exactView.ExpandedBy(12);
				protectedView.ClipInsideMap(map);
			}
			var visible = 0;
			var cameraProtected = 0;
			var priority = 0;
			foreach (var zombie in zombies)
			{
				if (hasCamera && exactView.Contains(zombie.Position)) visible++;
				if (hasCamera && protectedView.Contains(zombie.Position)) cameraProtected++;
				if (manager != null && TickManager.ShouldPrioritizeZombie(manager, zombie, hasCamera, protectedView)) priority++;
			}

			var typeCounts = new long[9];
			var stateCounts = new long[5];
			foreach (var zombie in zombies)
			{
				typeCounts[(int)TypeOf(zombie)]++;
				stateCounts[Mathf.Clamp((int)zombie.state, 0, stateCounts.Length - 1)]++;
			}

			var settings = ZombieSettings.Values;
			return new
			{
				mapId = map?.uniqueID ?? -1,
				mapSize = map == null ? null : new { x = map.Size.x, z = map.Size.z, area = map.Size.x * map.Size.z },
				camera = hasCamera ? new
				{
					exactView = ZombieRuntimeActions.DescribeCellRect(exactView),
					protectedView = ZombieRuntimeActions.DescribeCellRect(protectedView)
				} : null,
				count = zombies.Length,
				visible,
				invisible = zombies.Length - visible,
				cameraProtected,
				outsideCameraProtection = zombies.Length - cameraProtected,
				priority,
				remote = zombies.Length - priority,
				byType = CountDictionary(typeCounts, index => ((ZombieType)index).ToString()),
				byState = CountDictionary(stateCounts, index => ((ZombieState)index).ToString()),
				settings = settings == null ? null : new
				{
					settings.maximumNumberOfZombies,
					settings.zombiesDieOnZeroThreat,
					settings.zombieFreeEvents,
					settings.suicideBomberChance,
					settings.toxicSplasherChance,
					settings.tankyOperatorChance,
					settings.minerChance,
					settings.electrifierChance,
					settings.albinoChance,
					settings.darkSlimerChance,
					settings.healerChance
				}
			};
		}

		static object Category(long candidates, long selected)
		{
			return new { candidates, selected, skipped = Math.Max(0L, candidates - selected), selectionRate = Ratio(selected, candidates) };
		}

		static Dictionary<string, object> CategoryDictionary(long[] candidates, long[] selected, Func<int, string> name)
		{
			var result = new Dictionary<string, object>();
			for (var i = 0; i < candidates.Length; i++) result[name(i)] = Category(candidates[i], selected[i]);
			return result;
		}

		static Dictionary<string, long> CountDictionary(long[] counts, Func<int, string> name)
		{
			var result = new Dictionary<string, long>();
			for (var i = 0; i < counts.Length; i++) result[name(i)] = counts[i];
			return result;
		}

		static object Distribution(int[] sorted)
		{
			if (sorted == null || sorted.Length == 0)
				return new { count = 0, minimum = 0, p05 = 0, median = 0, p95 = 0, maximum = 0, average = 0d };
			return new
			{
				count = sorted.Length,
				minimum = sorted[0],
				p05 = Percentile(sorted, 0.05f),
				median = Percentile(sorted, 0.5f),
				p95 = Percentile(sorted, 0.95f),
				maximum = sorted[sorted.Length - 1],
				average = sorted.Average()
			};
		}

		static int Percentile(int[] sorted, float percentile)
		{
			if (sorted == null || sorted.Length == 0) return 0;
			var index = Mathf.Clamp(Mathf.CeilToInt(sorted.Length * percentile) - 1, 0, sorted.Length - 1);
			return sorted[index];
		}

		static double Ratio(long numerator, long denominator)
		{
			return denominator <= 0 ? 0d : numerator / (double)denominator;
		}
	}
}
