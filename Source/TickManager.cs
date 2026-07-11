using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace ZombieLand
{
	public enum ZombieSaturationState
	{
		Normal = 0,
		Throttled = 1,
		Emergency = 2,
		[Obsolete("Remote zombies are no longer frozen; use Emergency instead.")]
		RemoteFrozen = Emergency
	}

	[StaticConstructorOnStartup]
	public static class ZombieTicker
	{
		const float throttleEnterSeverity = 0.35f;
		const float throttleLeaveSeverity = 0.20f;
		const float emergencyEnterSeverity = 0.70f;
		const float emergencyLeaveSeverity = 0.45f;
		const float saturationSmoothing = 0.25f;
		const int saturationEnterUpdates = 8;
		const int throttleLeaveUpdates = 60;
		const int emergencyLeaveUpdates = 45;
		const float simulationSeverityStartMilliseconds = 12f;
		const float simulationSeverityLimitMilliseconds = 45.4545f;
		const float globalSeverityStartMilliseconds = 22f;
		const float globalSeverityLimitMilliseconds = 62f;

		public const float NormalRemoteTickFloor = 0.05f;
		public const float ThrottledRemoteTickFloor = 0.02f;
		public const float EmergencyRemoteTickFloor = 1f / 120f;
		public const float MinimumMovementCompensationRate = 0.05f;
		public const float MaximumMovementCompensation = 20f;

		public static List<TickManager> managers = new();
		public static Type RimThreaded = AccessTools.TypeByName("RimThreaded.RimThreaded");
		static Game adaptiveGame;
		static bool ignoreNextGlobalSample = true;

		public static float[] percentZombiesTicked = new[] { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f };
		public static int percentZombiesTickedIndex = 0;

		public static int zombiesTicked = 0;
		public static int maxTicking = 0;
		public static int currentTicking = 0;
		public static ZombieSaturationState saturationState = ZombieSaturationState.Normal;
		public static float saturationSeverity = 0f;
		public static float simulationSaturationSeverity = 0f;
		public static float lastSaturationSampleSeverity = 0f;
		public static float lastSimulationSeverity = 0f;
		public static float lastGlobalSeverity = 0f;
		public static float lastTickUpdateMilliseconds = 0f;
		public static float lastGlobalPressureMilliseconds = 0f;
		public static float lastRawDemandTicks = 0f;
		public static float lastNormalizedDemand = 0f;
		public static float lastCompletionRatio = 1f;
		public static float lastMeanTickTime = 0f;
		public static int lastEffectiveMultiplier = 0;
		public static int lastDueTicks = 0;
		public static int lastTickCap = 0;
		public static int lastAllowedTicks = 0;
		public static int lastCompletedTicks = 0;
		public static bool lastTickShortfall = false;
		public static int saturationSampleCount = 0;
		public static int throttleEnterCounter = 0;
		public static int throttleRecoveryCounter = 0;
		public static int emergencyEnterCounter = 0;
		public static int emergencyRecoveryCounter = 0;

		public struct TickUpdateState
		{
			public bool eligible;
			public Game game;
			public long startTimestamp;
			public int effectiveMultiplier;
			public float rawDemandTicks;
			public float normalizedDemand;
			public int dueTicks;
			public int tickCap;
			public int allowedTicks;
			public bool hasLiveZombies;
		}

		public static string SaturationStateName
		{
			get
			{
				switch (saturationState)
				{
					case ZombieSaturationState.Throttled:
						return "throttled";
					case ZombieSaturationState.Emergency:
						return "emergency";
					default:
						return "normal";
				}
			}
		}

		public static float RemoteTickFloor
		{
			get
			{
				switch (saturationState)
				{
					case ZombieSaturationState.Emergency:
						return EmergencyRemoteTickFloor;
					case ZombieSaturationState.Throttled:
						return ThrottledRemoteTickFloor;
					default:
						return NormalRemoteTickFloor;
				}
			}
		}

		public static float RemoteSelectionScale
		{
			get
			{
				switch (saturationState)
				{
					case ZombieSaturationState.Emergency:
						return 0f;
					case ZombieSaturationState.Throttled:
					{
						var t = Mathf.InverseLerp(throttleEnterSeverity, emergencyEnterSeverity, saturationSeverity);
						return Mathf.Lerp(0.5f, 0.1f, t);
					}
					default:
						return 1f;
				}
			}
		}

		public struct SaturationSnapshot
		{
			public ZombieSaturationState state;
			public float severity;
			public float simulationSeverity;
			public float sampleSeverity;
			public float lastSimulationSeverity;
			public float lastGlobalSeverity;
			public float tickUpdateMilliseconds;
			public float globalPressureMilliseconds;
			public float rawDemandTicks;
			public float normalizedDemand;
			public float completionRatio;
			public float meanTickTime;
			public int effectiveMultiplier;
			public int dueTicks;
			public int tickCap;
			public int allowedTicks;
			public int completedTicks;
			public bool tickShortfall;
			public int sampleCount;
			public int throttleEnter;
			public int throttleRecovery;
			public int emergencyEnter;
			public int emergencyRecovery;
			public bool ignoreGlobalSample;
		}

		public static SaturationSnapshot CaptureSaturation()
		{
			return new SaturationSnapshot
			{
				state = saturationState,
				severity = saturationSeverity,
				simulationSeverity = simulationSaturationSeverity,
				sampleSeverity = lastSaturationSampleSeverity,
				lastSimulationSeverity = ZombieTicker.lastSimulationSeverity,
				lastGlobalSeverity = ZombieTicker.lastGlobalSeverity,
				tickUpdateMilliseconds = lastTickUpdateMilliseconds,
				globalPressureMilliseconds = lastGlobalPressureMilliseconds,
				rawDemandTicks = lastRawDemandTicks,
				normalizedDemand = lastNormalizedDemand,
				completionRatio = lastCompletionRatio,
				meanTickTime = lastMeanTickTime,
				effectiveMultiplier = lastEffectiveMultiplier,
				dueTicks = lastDueTicks,
				tickCap = lastTickCap,
				allowedTicks = lastAllowedTicks,
				completedTicks = lastCompletedTicks,
				tickShortfall = lastTickShortfall,
				sampleCount = saturationSampleCount,
				throttleEnter = throttleEnterCounter,
				throttleRecovery = throttleRecoveryCounter,
				emergencyEnter = emergencyEnterCounter,
				emergencyRecovery = emergencyRecoveryCounter,
				ignoreGlobalSample = ignoreNextGlobalSample
			};
		}

		public static void RestoreSaturation(SaturationSnapshot snapshot)
		{
			saturationState = snapshot.state;
			saturationSeverity = snapshot.severity;
			simulationSaturationSeverity = snapshot.simulationSeverity;
			lastSaturationSampleSeverity = snapshot.sampleSeverity;
			lastSimulationSeverity = snapshot.lastSimulationSeverity;
			lastGlobalSeverity = snapshot.lastGlobalSeverity;
			lastTickUpdateMilliseconds = snapshot.tickUpdateMilliseconds;
			lastGlobalPressureMilliseconds = snapshot.globalPressureMilliseconds;
			lastRawDemandTicks = snapshot.rawDemandTicks;
			lastNormalizedDemand = snapshot.normalizedDemand;
			lastCompletionRatio = snapshot.completionRatio;
			lastMeanTickTime = snapshot.meanTickTime;
			lastEffectiveMultiplier = snapshot.effectiveMultiplier;
			lastDueTicks = snapshot.dueTicks;
			lastTickCap = snapshot.tickCap;
			lastAllowedTicks = snapshot.allowedTicks;
			lastCompletedTicks = snapshot.completedTicks;
			lastTickShortfall = snapshot.tickShortfall;
			saturationSampleCount = snapshot.sampleCount;
			throttleEnterCounter = snapshot.throttleEnter;
			throttleRecoveryCounter = snapshot.throttleRecovery;
			emergencyEnterCounter = snapshot.emergencyEnter;
			emergencyRecoveryCounter = snapshot.emergencyRecovery;
			ignoreNextGlobalSample = snapshot.ignoreGlobalSample;
		}

		public static void ResetSaturation()
		{
			RestoreSaturation(new SaturationSnapshot
			{
				state = ZombieSaturationState.Normal,
				completionRatio = 1f,
				ignoreGlobalSample = true
			});
		}

		public static void ResetAdaptiveState(Game game = null)
		{
			adaptiveGame = game;
			managers ??= new List<TickManager>();
			managers.Clear();
			zombiesTicked = 0;
			maxTicking = 0;
			currentTicking = 0;
			percentZombiesTicked = Enumerable.Repeat(1f, 8).ToArray();
			percentZombiesTickedIndex = 0;
			ResetSaturation();
		}

		public static bool EnsureAdaptiveGame(Game game)
		{
			if (ReferenceEquals(adaptiveGame, game))
				return true;
			ResetAdaptiveState(game);
			return false;
		}

		public static void SetManagersFromMaps(List<Map> maps)
		{
			managers ??= new List<TickManager>();
			managers.Clear();
			if (maps == null)
				return;
			for (var i = 0; i < maps.Count; i++)
			{
				var manager = maps[i]?.GetComponent<TickManager>();
				if (manager != null)
					managers.Add(manager);
			}
		}

		public static TickUpdateState CreateTickUpdateState(Game game, long startTimestamp, int effectiveMultiplier, float rawDemandTicks, bool hasLiveZombies)
		{
			var multiplier = Math.Max(1, effectiveMultiplier);
			var demand = float.IsNaN(rawDemandTicks) || float.IsInfinity(rawDemandTicks)
				? 0f
				: Mathf.Max(0f, rawDemandTicks);
			var dueTicks = Mathf.CeilToInt(demand);
			var tickCap = multiplier * 2;
			return new TickUpdateState
			{
				eligible = true,
				game = game,
				startTimestamp = startTimestamp,
				effectiveMultiplier = multiplier,
				rawDemandTicks = demand,
				normalizedDemand = demand / multiplier,
				dueTicks = dueTicks,
				tickCap = tickCap,
				allowedTicks = Math.Min(dueTicks, tickCap),
				hasLiveZombies = hasLiveZombies
			};
		}

		public static void RecordTickUpdate(TickUpdateState state, int completedTicks, float elapsedMilliseconds, float meanTickTime, bool controllerValid, bool completionComparable)
		{
			lastEffectiveMultiplier = state.effectiveMultiplier;
			lastRawDemandTicks = Mathf.Max(0f, state.rawDemandTicks);
			lastNormalizedDemand = Mathf.Max(0f, state.normalizedDemand);
			lastDueTicks = Math.Max(0, state.dueTicks);
			lastTickCap = Math.Max(0, state.tickCap);
			lastAllowedTicks = Math.Max(0, state.allowedTicks);
			lastCompletedTicks = Math.Max(0, completedTicks);
			lastTickUpdateMilliseconds = Mathf.Max(0f, elapsedMilliseconds);
			lastMeanTickTime = Mathf.Max(0f, meanTickTime);
			lastTickShortfall = completionComparable && lastCompletedTicks < lastAllowedTicks;
			lastCompletionRatio = lastAllowedTicks <= 0
				? 1f
				: Mathf.Clamp01(lastCompletedTicks / (float)lastAllowedTicks);

			if (state.hasLiveZombies == false)
			{
				FillPercentTicking(1f);
				ResetSaturation();
				return;
			}
			if (controllerValid == false)
				return;

			if (completionComparable && lastAllowedTicks > 0)
				PercentTicking = lastCompletionRatio;

			var simulationSeverity = Mathf.InverseLerp(simulationSeverityStartMilliseconds, simulationSeverityLimitMilliseconds, lastTickUpdateMilliseconds);
			if (lastTickShortfall)
				simulationSeverity = 1f;

			lastGlobalPressureMilliseconds = lastNormalizedDemand * (1000f / 60f);
			var globalSeverity = ignoreNextGlobalSample || completionComparable == false
				? 0f
				: Mathf.InverseLerp(globalSeverityStartMilliseconds, globalSeverityLimitMilliseconds, lastGlobalPressureMilliseconds);
			ignoreNextGlobalSample = false;
			ApplySaturationSample(globalSeverity, simulationSeverity);
		}

		public static void ApplySaturationSample(float globalSeverity, float simulationSeverity)
		{
			lastGlobalSeverity = Mathf.Clamp01(globalSeverity);
			lastSimulationSeverity = Mathf.Clamp01(simulationSeverity);
			lastSaturationSampleSeverity = Mathf.Max(lastGlobalSeverity, lastSimulationSeverity);
			if (saturationSampleCount == 0)
			{
				saturationSeverity = lastSaturationSampleSeverity;
				simulationSaturationSeverity = lastSimulationSeverity;
			}
			else
			{
				saturationSeverity = Mathf.Lerp(saturationSeverity, lastSaturationSampleSeverity, saturationSmoothing);
				simulationSaturationSeverity = Mathf.Lerp(simulationSaturationSeverity, lastSimulationSeverity, saturationSmoothing);
			}
			saturationSampleCount++;
			UpdateSaturationState();
		}

		static void UpdateSaturationState()
		{
			if (simulationSaturationSeverity >= emergencyEnterSeverity)
				emergencyEnterCounter++;
			else
				emergencyEnterCounter = 0;

			if (saturationState != ZombieSaturationState.Emergency && emergencyEnterCounter >= saturationEnterUpdates)
			{
				saturationState = ZombieSaturationState.Emergency;
				throttleEnterCounter = 0;
				throttleRecoveryCounter = 0;
				emergencyRecoveryCounter = 0;
				return;
			}

			if (saturationState == ZombieSaturationState.Emergency)
			{
				if (simulationSaturationSeverity < emergencyLeaveSeverity)
					emergencyRecoveryCounter++;
				else
					emergencyRecoveryCounter = 0;

				if (emergencyRecoveryCounter >= emergencyLeaveUpdates)
				{
					saturationState = ZombieSaturationState.Throttled;
					emergencyEnterCounter = 0;
					emergencyRecoveryCounter = 0;
					throttleRecoveryCounter = 0;
				}
				return;
			}

			if (saturationState == ZombieSaturationState.Normal)
			{
				if (saturationSeverity >= throttleEnterSeverity)
					throttleEnterCounter++;
				else
					throttleEnterCounter = 0;

				if (throttleEnterCounter >= saturationEnterUpdates)
				{
					saturationState = ZombieSaturationState.Throttled;
					throttleEnterCounter = 0;
					throttleRecoveryCounter = 0;
				}
				return;
			}

			if (saturationSeverity < throttleLeaveSeverity)
				throttleRecoveryCounter++;
			else
				throttleRecoveryCounter = 0;

			if (throttleRecoveryCounter >= throttleLeaveUpdates)
			{
				saturationState = ZombieSaturationState.Normal;
				throttleEnterCounter = 0;
				throttleRecoveryCounter = 0;
				emergencyEnterCounter = 0;
				emergencyRecoveryCounter = 0;
			}
		}

		public static float MovementPaymentMultiplier(float simulationTickRate)
		{
			if (float.IsNaN(simulationTickRate) || float.IsInfinity(simulationTickRate) || simulationTickRate <= 0f)
				return MaximumMovementCompensation;
			var rate = Mathf.Clamp(simulationTickRate, MinimumMovementCompensationRate, 1f);
			return Mathf.Clamp(1f / rate, 1f, MaximumMovementCompensation);
		}

		public static object DescribeSaturation(TickManager tickManager)
		{
			return new
			{
				state = SaturationStateName,
				severity = saturationSeverity,
				simulationSeverity = simulationSaturationSeverity,
				sampleSeverity = lastSaturationSampleSeverity,
				lastGlobalSeverity,
				lastSimulationSeverity,
				tickUpdateMilliseconds = lastTickUpdateMilliseconds,
				globalPressureMilliseconds = lastGlobalPressureMilliseconds,
				rawDemandTicks = lastRawDemandTicks,
				normalizedDemand = lastNormalizedDemand,
				effectiveMultiplier = lastEffectiveMultiplier,
				dueTicks = lastDueTicks,
				tickCap = lastTickCap,
				allowedTicks = lastAllowedTicks,
				completedTicks = lastCompletedTicks,
				completionRatio = lastCompletionRatio,
				tickShortfall = lastTickShortfall,
				meanTickTime = lastMeanTickTime,
				sampleCount = saturationSampleCount,
				throttleEnterCounter,
				throttleRecoveryCounter,
				emergencyEnterCounter,
				emergencyRecoveryCounter,
				remoteSelectionScale = RemoteSelectionScale,
				remoteTickFloor = RemoteTickFloor,
				selection = tickManager == null ? null : new
				{
					split = tickManager.lastZombieTickingSplit,
					targetCount = tickManager.lastZombieTickingTargetCount,
					priorityCount = tickManager.lastZombieTickingPriorityCount,
					remoteCount = tickManager.lastZombieTickingRemoteCount,
					selectedRemoteCount = tickManager.lastZombieTickingSelectedRemoteCount,
					selectedCount = tickManager.currentZombiesTickingCount,
					remoteTickRate = tickManager.lastZombieTickingRemoteTickRate,
					remoteWorkCarry = tickManager.RemoteWorkCarry,
					remoteQueueCount = tickManager.RemoteSchedulerQueueCount,
					remoteQueueStaleDiscards = tickManager.remoteQueueStaleDiscards,
					remoteQueueCompactions = tickManager.remoteQueueCompactions
				}
			};
		}

		public static void DoSingleTick()
		{
			if (LongEventHandler.AnyEventNowOrWaiting || LongEventHandler.ShouldWaitForEvent)
				return;
			if (Current.Game == null || Current.ProgramState != ProgramState.Playing || Scribe.mode != LoadSaveMode.Inactive)
				return;
			if (managers == null || managers.Count == 0)
				return;

			if (RimThreaded == null)
				for (var i = 0; i < managers.Count; i++)
				{
					var tickManager = managers[i];
					if (tickManager.TryEnsureRuntimeInitialized("ZombieTicker.DoSingleTick"))
					{
						tickManager.ZombieTicking();
						continue;
					}

					switch (tickManager.isInitialized)
					{
						case 0:
							tickManager.ReportInitializationProblemOnce("Zombieland's TickManager was never initialized. This usually means RimWorld or another mod failed before MapComponent.FinalizeInit reached Zombieland.");
							break;
						case 1:
							tickManager.ReportInitializationProblemOnce("Zombieland's TickManager stopped while entering MapComponent.FinalizeInit.");
							break;
						case 2:
							tickManager.ReportInitializationProblemOnce("Zombieland's TickManager stopped while finalizing its map state.");
							break;
					}
				}
		}

		public static float PercentTicking
		{
			get
			{
				return percentZombiesTicked.Average();
			}
			set
			{
				percentZombiesTicked[percentZombiesTickedIndex] = Mathf.Clamp01(value);
				percentZombiesTickedIndex = (percentZombiesTickedIndex + 1) % percentZombiesTicked.Length;
			}
		}

		public static void FillPercentTicking(float value)
		{
			var clamped = Mathf.Clamp01(value);
			for (var i = 0; i < percentZombiesTicked.Length; i++)
				percentZombiesTicked[i] = clamped;
			percentZombiesTickedIndex = 0;
		}
	}

	public class TickManager : MapComponent
	{
		public const int InitializationReady = 3;
		const int MinimumTicksBetweenZombieSymbiants = GenDate.TicksPerDay * 4;

		public int isInitialized = 0;
		bool initializationProblemLogged;
		bool initializationPlayerNoticeQueued;
		bool initializationPlayerNoticeShown;
		int nextInitializationRetryTick;
		int populationSpawnCounter;

		int nextVisibleGridUpdate;
		int incidentTickCounter;
		int colonyPointsTickCounter;
		int avoidGridCounter;
		bool avoidGridRefreshRequested;
		bool promptAvoidGridResultPending;
		public int lastAvoidGridRequestTick;
		public int lastAvoidGridResultTick;
		public long lastAvoidGridRequestId;
		public long lastAvoidGridResultId;

		public IntVec3 centerOfInterest = IntVec3.Invalid;
		public IntVec3 nextCenterOfInterest = IntVec3.Invalid;
		public int centerOfInterestUpdateTicks = 0;
		public int currentColonyPoints;
		public int mapSpawnedTicks = 0;

		public HashSet<Zombie> allZombiesCached;
		IEnumerator taskTicker;
		bool runZombiesForNewIncident = false;

		public Zombie[] currentZombiesTicking = Array.Empty<Zombie>();
		public int currentZombiesTickingCount;
		public int currentZombiesTickingIndex;
		Zombie[] currentZombiesTickingCandidates = Array.Empty<Zombie>();
		int currentZombiesTickingCandidatesCount;
		Zombie[] priorityZombiesTickingCandidates = Array.Empty<Zombie>();
		Zombie[] remoteZombiesTickingCandidates = Array.Empty<Zombie>();
		readonly Queue<Zombie> remoteSchedulerQueue = new();
		int remoteSchedulerGeneration = 1;
		int remoteEligibilityGeneration = 0;
		double remoteWorkCarry = 0d;
		public bool lastZombieTickingSplit;
		public int lastZombieTickingTargetCount;
		public int lastZombieTickingPriorityCount;
		public int lastZombieTickingRemoteCount;
		public int lastZombieTickingSelectedRemoteCount;
		public float lastZombieTickingRemoteTickRate = 1f;
		public int remoteQueueStaleDiscards;
		public int remoteQueueCompactions;

		public int CurrentZombiesTickingCandidatesCount => currentZombiesTickingCandidatesCount;
		public int CurrentZombiesTickingCandidatesCapacity => currentZombiesTickingCandidates?.Length ?? 0;
		public int PriorityZombiesTickingCandidatesCapacity => priorityZombiesTickingCandidates?.Length ?? 0;
		public int RemoteZombiesTickingCandidatesCapacity => remoteZombiesTickingCandidates?.Length ?? 0;
		public int RemoteSchedulerQueueCount => remoteSchedulerQueue.Count;
		public double RemoteWorkCarry => remoteWorkCarry;
		public bool RuntimeReady => isInitialized == InitializationReady && taskTicker != null;
		public List<ZombieCorpse> allZombieCorpses;
		public AvoidGrid avoidGrid;
		public AvoidGrid emptyAvoidGrid;

		Sustainer zombiesAmbientSound;
		float zombiesAmbientSoundVolume;

		public readonly HashSet<Zombie> hummingZombies = new();
		Sustainer electricSustainer;

		public readonly HashSet<Zombie> tankZombies = new();
		Sustainer tankSustainer;

		public readonly HashSet<Zombie> suicideBomberZombies = new();
		int nextSuicideBomberCleanupFrame;

		readonly List<ZombieHitSoundBucket> zombieHitSoundBuckets = new();
		float nextGlobalZombieHitSoundRealtime = -1f;

		public Queue<ThingWithComps> colonistsToConvert = new();
		public Queue<Action<Map>> rimConnectActions = new();

		public List<IntVec3> explosions = new();
		public IncidentInfo incidentInfo = new();
		public ZombiePathing zombiePathing;

		public List<SoSTools.Floater> floatingSpaceZombiesBack;
		public List<SoSTools.Floater> floatingSpaceZombiesFore;

		public List<VictimHead> victimHeads = new();
		public ContaminationEffectManager contaminationEffects = Constants.CONTAMINATION ? new() : null;

		public int lastZombieContact = 0;
		public int lastZombieSpitter = 0;
		public bool zombieSpitterInited = false;
		public int lastZombieSymbiant = 0;
		public int nextZombieSymbiant = 0;
		public bool zombieSymbiantInited = false;
		public int lastZombieSymbiantGone = -1;
		public bool zombieSymbiantWasActive = false;

		public TickManager(Map map) : base(map)
		{
			zombiePathing = new ZombiePathing(map);
			zombiePathing.UpdateRegions();

			currentColonyPoints = 100;
			mapSpawnedTicks = 0;

			allZombiesCached = new HashSet<Zombie>();
			allZombieCorpses = new List<ZombieCorpse>();

			var type = ZombieTicker.RimThreaded;
			if (type != null)
			{
				var addNormalTicking = AccessTools.Method(type, "AddNormalTicking");
				if (addNormalTicking != null)
					_ = addNormalTicking.Invoke(null, new object[]
					{
						this,
						new Action<object>(PrepareThreadedTicking),
						new Action<object>(DoThreadedSingleTick)
					});
			}
		}

		public override void MapGenerated()
		{
			var ticks = GenTicks.TicksGame;
			mapSpawnedTicks = ticks;
			if (zombieSpitterInited == false)
			{
				lastZombieContact = ticks;
				lastZombieSpitter = ticks;
				zombieSpitterInited = true;
			}
			if (zombieSymbiantInited == false)
				InitializeZombieSymbiantSchedule(ticks);
			base.MapGenerated();
		}

		public override void FinalizeInit()
		{
			isInitialized = 1;
			try
			{
				base.FinalizeInit();
			}
			finally
			{
				_ = EnsureRuntimeInitialized("TickManager.FinalizeInit");
			}
		}

		public bool TryEnsureRuntimeInitialized(string phase)
		{
			if (RuntimeReady)
				return true;
			if (CanRetryRuntimeInitialization() == false)
				return false;
			return EnsureRuntimeInitialized(phase);
		}

		public bool EnsureRuntimeInitialized(string phase)
			=> EnsureRuntimeInitialized(phase, out _);

		public bool EnsureRuntimeInitialized(string phase, out bool changed)
		{
			changed = false;
			if (RuntimeReady)
				return true;
			if (isInitialized == 0)
			{
				ReportInitializationProblemOnce($"runtime initialization skipped during {phase}: TickManager.FinalizeInit has not run, so Zombieland will not synthesize the vanilla map-component lifecycle.");
				return false;
			}
			if (HasRuntimePrerequisites(phase) == false)
				return false;

			try
			{
				// Late retries rebuild only Zombieland runtime fields; they must not
				// compensate for vanilla map initialization that did not finish.
				InitializeRuntimeState(phase);
				ClearInitializationProblemState();
				nextInitializationRetryTick = 0;
				changed = true;
				return true;
			}
			catch (Exception ex)
			{
				taskTicker = null;
				isInitialized = 2;
				ReportInitializationProblemOnce($"runtime initialization failed during {phase}: {ex}");
				return false;
			}
		}

		bool CanRetryRuntimeInitialization()
		{
			if (RuntimeReady)
				return true;
			var ticks = GenTicks.TicksGame;
			if (ticks < nextInitializationRetryTick)
				return false;
			nextInitializationRetryTick = ticks + 250;
			return true;
		}

		bool HasRuntimePrerequisites(string phase)
		{
			if (map == null)
			{
				ReportInitializationProblemOnce($"runtime initialization skipped during {phase}: map is missing.");
				return false;
			}

			var missing = new List<string>();
			if (map.components == null)
				missing.Add("components");
			if (map.mapPawns == null)
				missing.Add("mapPawns");
			if (map.areaManager?.Home == null)
				missing.Add("home area");
			if (map.regionGrid?.allRooms == null)
				missing.Add("region rooms");
			if (map.listerThings == null)
				missing.Add("thing lister");
			if (map.thingGrid == null)
				missing.Add("thing grid");
			if (map.edificeGrid == null)
				missing.Add("edifice grid");
			if (map.floodFiller == null)
				missing.Add("flood filler");
			if (map.pathing == null)
				missing.Add("pathing");
			if (map.listerBuildings == null)
				missing.Add("building lister");
			if (map.pawnDestinationReservationManager == null)
				missing.Add("destination reservations");

			if (missing.Count == 0)
				return true;

			ReportInitializationProblemOnce($"runtime initialization skipped during {phase}: map services missing ({string.Join(", ", missing)}).");
			return false;
		}

		void InitializeRuntimeState(string phase)
		{
			isInitialized = 2;
			ClearZombieTickingBuffers();
			ResetRemoteScheduler(resetDiagnostics: true);

			Tools.nextPlayerReachableRegionsUpdate = 0;

			ZombieBootstrap.ResetZombieGrid(phase, map, rebuildLiveZombieCounts: true);

			colonyPointsTickCounter = -1;
			RecalculateColonyPoints();

			nextVisibleGridUpdate = 0;
			RecalculateZombieWanderDestination();

			var zombieFaction = ZombieBootstrap.EnsureZombieFaction(phase);
			if (zombieFaction == null)
				throw new InvalidOperationException("zombie faction is missing");
			if (ZombieBootstrap.EnsureZombieDestinationReservations(phase, map, zombieFaction) == false)
				throw new InvalidOperationException("zombie destination reservations are not ready");

			var allZombies = AllZombies().ToList();
			var shouldAvoidZombies = Tools.ShouldAvoidZombies();
			if (shouldAvoidZombies)
			{
				var specs = BuildAvoidGridSpecs(allZombies);
				avoidGrid = Tools.avoider.UpdateZombiePositionsImmediately(map, specs);
			}
			else
				avoidGrid = new AvoidGrid(map);
			avoidGridRefreshRequested = false;
			promptAvoidGridResultPending = false;
			lastAvoidGridRequestTick = GenTicks.TicksGame;
			lastAvoidGridResultTick = lastAvoidGridRequestTick;
			lastAvoidGridRequestId = avoidGrid?.requestId ?? 0;
			lastAvoidGridResultId = lastAvoidGridRequestId;
			if (shouldAvoidZombies)
				SeedElectricAvoidGridSnapshots(allZombies);

			hummingZombies.Clear();
			allZombies.Where(zombie => zombie.IsActiveElectric).Do(zombie => hummingZombies.Add(zombie));
			tankZombies.Clear();
			allZombies.Where(zombie => zombie.IsTanky).Do(zombie => tankZombies.Add(zombie));
			suicideBomberZombies.Clear();
			allZombies.Where(zombie => zombie.IsSuicideBomber).Do(zombie => suicideBomberZombies.Add(zombie));

			taskTicker = TickTasks(skipFirstIncidentPass: true);
			while (taskTicker.Current as string != "end")
				_ = taskTicker.MoveNext();

			isInitialized = InitializationReady;
		}

		public void ReportInitializationProblemOnce(string reason)
		{
			if (initializationProblemLogged)
			{
				QueuePlayerInitializationProblemNotice();
				return;
			}
			initializationProblemLogged = true;
			QueuePlayerInitializationProblemNotice();

			var mapLabel = "unknown map";
			try
			{
				if (map != null)
					mapLabel = $"map {map.uniqueID}";
			}
			catch
			{
			}

			Log.Error($"Zombieland is skipping zombie ticking for {mapLabel} because required map initialization is incomplete. This is a downstream safety guard; the original cause is usually the first earlier exception during map or mod initialization. Details: {reason}");
		}

		void ClearInitializationProblemState()
		{
			initializationProblemLogged = false;
			initializationPlayerNoticeQueued = false;
			initializationPlayerNoticeShown = false;
		}

		void QueuePlayerInitializationProblemNotice()
		{
			if (initializationPlayerNoticeQueued || initializationPlayerNoticeShown)
				return;

			initializationPlayerNoticeQueued = true;
		}

		void ShowQueuedPlayerInitializationProblemNoticeIfStillBroken()
		{
			try
			{
				if (initializationPlayerNoticeQueued == false || initializationPlayerNoticeShown || RuntimeReady)
					return;
				if (Current.Game == null || Current.ProgramState != ProgramState.Playing)
					return;
				if (map == null || Find.Maps?.Contains(map) != true || Find.LetterStack == null)
					return;

				initializationPlayerNoticeQueued = false;
				var letter = new ChoiceLetter_ZombielandMapSetupFailed
				{
					def = LetterDefOf.NegativeEvent,
					ID = Find.UniqueIDsManager.GetNextLetterID(),
					Label = "LetterLabelZombielandMapSetupFailed".Translate(),
					Text = "ZombielandMapSetupFailed".Translate(),
					lookTargets = new LookTargets(map.Center, map)
				};
				Find.LetterStack.ReceiveLetter(letter);
				initializationPlayerNoticeShown = true;
			}
			catch (Exception ex)
			{
				Log.Error($"Zombieland failed to show its player-facing initialization problem notice: {ex}");
			}
		}

		public override void MapRemoved()
		{
			base.MapRemoved();
			Cleanup();
			ZombieSymbiant.ReleaseRenderResourcesForMap(map);
			ZombieSymbiant.ForgetMap(map);
		}

		public void Cleanup()
		{
			ClearZombieTickingBuffers();
			ResetRemoteScheduler(resetDiagnostics: true);
			StopAmbientSound();
			zombieHitSoundBuckets.Clear();
			nextSuicideBomberCleanupFrame = 0;
			if (zombiePathing != null)
				zombiePathing.running = false;
			zombiePathing = null;
		}

		public override void ExposeData()
		{
			base.ExposeData();

			Scribe_Values.Look(ref currentColonyPoints, "colonyPoints");
			Scribe_Collections.Look(ref allZombiesCached, "prioritizedZombies", LookMode.Reference);
			Scribe_Collections.Look(ref explosions, "explosions", LookMode.Value);
			Scribe_Deep.Look(ref incidentInfo, "incidentInfo", Array.Empty<object>());
			Scribe_Values.Look(ref mapSpawnedTicks, "mapSpawnedTicks");
			Scribe_Values.Look(ref lastZombieContact, "lastZombieContact");
			Scribe_Values.Look(ref lastZombieSpitter, "lastZombieSpitter");
			Scribe_Values.Look(ref zombieSpitterInited, "zombieSpitterInited");
			Scribe_Values.Look(ref lastZombieSymbiant, "lastZombieSymbiant");
			Scribe_Values.Look(ref nextZombieSymbiant, "nextZombieSymbiant");
			Scribe_Values.Look(ref zombieSymbiantInited, "zombieSymbiantInited");
			Scribe_Values.Look(ref lastZombieSymbiantGone, "lastZombieSymbiantGone", -1);
			Scribe_Values.Look(ref zombieSymbiantWasActive, "zombieSymbiantWasActive");

			if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
				allZombiesCached ??= new HashSet<Zombie>();
				allZombiesCached = allZombiesCached.Where(zombie => zombie != null && zombie.Spawned && zombie.Dead == false).ToHashSet();
				ClearZombieTickingBuffers();
				ResetRemoteScheduler(resetDiagnostics: true);

				allZombieCorpses ??= new List<ZombieCorpse>();
				allZombieCorpses = allZombieCorpses.Where(corpse => corpse.DestroyedOrNull() == false && corpse.Spawned).ToList();

				runZombiesForNewIncident = true;
				explosions ??= new List<IntVec3>();

				if (zombieSpitterInited == false)
				{
					var ticks = GenTicks.TicksGame;
					lastZombieContact = ticks;
					lastZombieSpitter = ticks;
					zombieSpitterInited = true;
				}
				if (zombieSymbiantInited == false)
					InitializeZombieSymbiantSchedule(GenTicks.TicksGame);
				else if (nextZombieSymbiant <= 0)
					nextZombieSymbiant = Mathf.Max(GenTicks.TicksGame + GenDate.TicksPerHour, lastZombieSymbiant + ZombieSymbiantDelayTicks(true));
				zombieSymbiantWasActive = ZombieSymbiant.ActiveSymbiant(map) != null;
			}

		}

		static Mesh headMesh;
		static Mesh HeadMesh => headMesh ??= MeshPool.GetMeshSetForWidth(MeshPool.HumanlikeHeadAverageWidth).MeshAt(Rot4.South);

		public override void MapComponentUpdate()
		{
			if (victimHeads.Count == 0)
				return;
			if (Tools.MapViewActiveFor(map) == false)
				return;

			foreach (var head in victimHeads)
			{
				var material = head.material;
				var color = material.color;
				color.a = head.alpha;
				material.color = color;
				GraphicToolbox.DrawScaledMesh(HeadMesh, material, head.Position, head.quat, 0.7f, 0.7f);
			}
		}

		public void RecalculateColonyPoints()
		{
			if (colonyPointsTickCounter-- >= 0)
				return;
			colonyPointsTickCounter = 100;

			currentColonyPoints = Tools.ColonyPoints(map).Sum();
		}

		public void RecalculateZombieWanderDestination()
		{
			var ticks = GenTicks.TicksGame;
			if (ticks < nextVisibleGridUpdate)
				return;
			nextVisibleGridUpdate = ticks + Constants.TICKMANAGER_RECALCULATE_DELAY;

			allZombiesCached = AllZombies().ToHashSet();
			var home = map.areaManager.Home;
			Room[] valuableRooms = null;
			var homeCells = home.TrueCount > 0 ? home.ActiveCells.ToArray() : Array.Empty<IntVec3>();
			if (homeCells.Length > 0)
			{
				allZombiesCached.Do(zombie => zombie.wanderDestination = homeCells.SafeRandomElement(IntVec3.Invalid));
				var tankys = allZombiesCached.Where(zombie => zombie.IsTanky && zombie.tankDestination.IsValid == false);
				if (tankys.Any())
				{
					valuableRooms ??= Tools.ValuableRooms(map).ToArray();
					var valuableCells = valuableRooms.SelectMany(room => room.Cells).ToArray();
					if (valuableCells.Length > 0)
						tankys.Do(zombie => zombie.tankDestination = valuableCells.SafeRandomElement(IntVec3.Invalid));
				}

				if (ticks > centerOfInterestUpdateTicks)
				{
					centerOfInterestUpdateTicks = ticks + Constants.CENTER_OF_INTEREST_UPDATE;
					if (Rand.Bool)
						nextCenterOfInterest = homeCells.SafeRandomElement(IntVec3.Invalid);
					else
					{
						valuableRooms ??= Tools.ValuableRooms(map).ToArray();
						if (valuableRooms.Length > 0)
							nextCenterOfInterest = valuableRooms.SelectMany(room => room.Cells).SafeRandomElement(IntVec3.Invalid);
						else
							nextCenterOfInterest = homeCells.SafeRandomElement(IntVec3.Invalid);
					}
				}
			}
			else
			{
				valuableRooms ??= Tools.ValuableRooms(map).ToArray();
				if (valuableRooms.Length > 0)
				{
					var valuableCells = valuableRooms.SelectMany(room => room.Cells).ToArray();
					if (valuableCells.Length > 0)
					{
						allZombiesCached.Do(zombie => zombie.wanderDestination = valuableCells.SafeRandomElement(IntVec3.Invalid));
						if (ticks > centerOfInterestUpdateTicks)
							nextCenterOfInterest = valuableCells.SafeRandomElement(IntVec3.Invalid);
					}
					else
						allZombiesCached.Do(zombie => zombie.wanderDestination = new IntVec3(Rand.Range(10, map.Size.x - 10), 0, Rand.Range(10, map.Size.z - 10)));
				}
				else
					allZombiesCached.Do(zombie => zombie.wanderDestination = new IntVec3(Rand.Range(10, map.Size.x - 10), 0, Rand.Range(10, map.Size.z - 10)));
			}

			if (centerOfInterest.IsValid == false && nextCenterOfInterest.IsValid)
				centerOfInterest = nextCenterOfInterest;
			else if (nextCenterOfInterest.IsValid && centerOfInterest != nextCenterOfInterest)
				centerOfInterest += new IntVec3(Math.Sign(nextCenterOfInterest.x - centerOfInterest.x), 0, Math.Sign(nextCenterOfInterest.z - centerOfInterest.z));
		}

		public int GetMaxZombieCount()
		{
			if (map?.mapPawns == null)
				return 0;
			if (Constants.DEBUG_MAX_ZOMBIE_COUNT >= 0)
				return Constants.DEBUG_MAX_ZOMBIE_COUNT;
			var (capable, incapable) = Tools.ColonistsInfo(map);
			var perColonistZombieCount = GenMath.LerpDoubleClamped(0f, 4f, 5, 30, Mathf.Sqrt(capable));
			var colonistMultiplier = Mathf.Sqrt(capable) * 2 + incapable / 2f;
			var baseStrengthFactor = GenMath.LerpDoubleClamped(0, 40000, 1f, 8f, currentColonyPoints);
			var colonyMultiplier = ZombieSettings.Values.colonyMultiplier;
			var difficultyMultiplier = Tools.Difficulty();
			var count = (int)(perColonistZombieCount * colonistMultiplier * baseStrengthFactor * colonyMultiplier * difficultyMultiplier);
			var max = capable <= 1 && incapable <= 2 ? 25 * capable + 10 * (incapable - capable) : 99999;
			return Mathf.Min(ZombieSettings.Values.maximumNumberOfZombies, Mathf.Min(max, count));
		}

		public void ZombieTicking()
		{
			PrepareThreadedTicking(this);
			var threatLevel = ZombieWeather.GetThreatLevel(map);
			var ticking = currentZombiesTicking;
			for (var i = 0; i < currentZombiesTickingCount; i++)
			{
				ticking[i].CustomTick(threatLevel);
				ZombieTicker.zombiesTicked++;
			}
			ZombieTickingTelemetry.RecordActualTicks(map, currentZombiesTickingCount);
			ZombieSymbiant.ActiveSymbiant(map)?.SymbiantTick();
		}

		public int LiveZombieCount()
		{
			if (RuntimeReady == false || allZombiesCached == null)
				return 0;
			var count = 0;
			foreach (var zombie in allZombiesCached)
				if (zombie != null && zombie.Spawned && zombie.Dead == false)
					count++;
			return count;
		}

		public static void PrepareThreadedTicking(object input)
		{
			var tickManager = (TickManager)input;
			if (tickManager.RuntimeReady == false)
			{
				tickManager.ReportInitializationProblemOnce($"RimThreaded prepare skipped because TickManager is not ready (state {tickManager.isInitialized}).");
				tickManager.ClearZombieTickingBuffers();
				tickManager.ResetRemoteScheduler();
				return;
			}

			var previousCandidateCount = tickManager.currentZombiesTickingCandidatesCount;
			var allZombies = tickManager.allZombiesCached;
			var candidateCapacity = allZombies?.Count ?? 0;
			EnsureZombieBufferCapacity(ref tickManager.currentZombiesTickingCandidates, candidateCapacity);
			var zombies = tickManager.currentZombiesTickingCandidates;
			var zombieCount = 0;
			if (allZombies != null)
				foreach (var zombie in allZombies)
					if (zombie != null && zombie.Spawned && zombie.Dead == false)
						zombies[zombieCount++] = zombie;

			ClearZombieBuffer(zombies, zombieCount, previousCandidateCount);
			tickManager.currentZombiesTickingCandidatesCount = zombieCount;
			var tickFraction = Mathf.Min(ZombieTicker.PercentTicking, CountBasedTickFraction(zombieCount));
			var targetNeighborCells = tickManager.map.GetComponent<ZombieAttackTargetIndex>()?.CurrentCandidateNeighborsByCell();
			var attackNeighborTick = targetNeighborCells == null ? -1 : GenTicks.TicksAbs;
			for (var i = 0; i < zombieCount; i++)
			{
				var zombie = zombies[i];
				zombie.hasAttackCandidateNeighbor = HasAttackCandidateNeighbor(tickManager.map, zombie, targetNeighborCells);
				zombie.attackCandidateNeighborTick = attackNeighborTick;
			}

			var previousTickingCount = tickManager.currentZombiesTickingCount;
			var previousPriorityCount = tickManager.lastZombieTickingPriorityCount;
			var previousRemoteCount = tickManager.lastZombieTickingRemoteCount;
			tickManager.lastZombieTickingSplit = false;
			tickManager.lastZombieTickingTargetCount = zombieCount;
			tickManager.lastZombieTickingPriorityCount = 0;
			tickManager.lastZombieTickingRemoteCount = 0;
			tickManager.lastZombieTickingSelectedRemoteCount = 0;
			tickManager.lastZombieTickingRemoteTickRate = 1f;
			var telemetryEnabled = ZombieTickingTelemetry.EnabledFor(tickManager.map);
			var splitRequired = tickFraction < 1f || ZombieTicker.saturationState != ZombieSaturationState.Normal;
			var hasViewRect = (telemetryEnabled || splitRequired) && Find.CurrentMap == tickManager.map && Find.CameraDriver != null;
			var exactViewRect = default(CellRect);
			var protectedViewRect = default(CellRect);
			if (hasViewRect)
			{
				exactViewRect = Find.CameraDriver.CurrentViewRect;
				exactViewRect.ClipInsideMap(tickManager.map);
				protectedViewRect = exactViewRect.ExpandedBy(12);
				protectedViewRect.ClipInsideMap(tickManager.map);
			}
			if (telemetryEnabled)
				ZombieTickingTelemetry.BeginPreparation(tickManager.map);

			if (splitRequired == false)
			{
				EnsureZombieBufferCapacity(ref tickManager.currentZombiesTicking, zombieCount);
				Array.Copy(zombies, tickManager.currentZombiesTicking, zombieCount);
				tickManager.currentZombiesTickingCount = zombieCount;
				for (var i = 0; i < zombieCount; i++)
				{
					var zombie = zombies[i];
					zombie.simulationTickRate = 1f;
					if (telemetryEnabled)
					{
						var exactVisible = hasViewRect && exactViewRect.Contains(zombie.Position);
						var cameraProtected = hasViewRect && protectedViewRect.Contains(zombie.Position);
						var priorityForDiagnostics = ShouldPrioritizeZombie(tickManager, zombie, hasViewRect, protectedViewRect);
						ZombieTickingTelemetry.RecordCandidate(zombie, exactVisible, cameraProtected, priorityForDiagnostics);
						ZombieTickingTelemetry.RecordSelected(zombie, exactVisible, cameraProtected, priorityForDiagnostics);
					}
				}
				tickManager.ResetRemoteScheduler();
			}
			else
			{
				var targetTickingCount = Mathf.FloorToInt(zombieCount * tickFraction);
				tickManager.lastZombieTickingTargetCount = targetTickingCount;
				EnsureZombieBufferCapacity(ref tickManager.priorityZombiesTickingCandidates, zombieCount);
				EnsureZombieBufferCapacity(ref tickManager.remoteZombiesTickingCandidates, zombieCount);
				var selected = tickManager.currentZombiesTicking;
				var priority = tickManager.priorityZombiesTickingCandidates;
				var remote = tickManager.remoteZombiesTickingCandidates;
				var priorityCount = 0;
				var remoteCount = 0;
				var eligibilityGeneration = tickManager.NextRemoteEligibilityGeneration();
				for (var i = 0; i < zombieCount; i++)
				{
					var zombie = zombies[i];
					var prioritize = ShouldPrioritizeZombie(tickManager, zombie, hasViewRect, protectedViewRect);
					if (telemetryEnabled)
					{
						var exactVisible = hasViewRect && exactViewRect.Contains(zombie.Position);
						var cameraProtected = hasViewRect && protectedViewRect.Contains(zombie.Position);
						ZombieTickingTelemetry.RecordCandidate(zombie, exactVisible, cameraProtected, prioritize);
					}
					if (prioritize)
						priority[priorityCount++] = zombie;
					else
					{
						remote[remoteCount++] = zombie;
						zombie.remoteEligibilityGeneration = eligibilityGeneration;
						tickManager.EnsureRemoteQueueMembership(zombie);
					}
				}

				tickManager.lastZombieTickingSplit = true;
				tickManager.lastZombieTickingPriorityCount = priorityCount;
				tickManager.lastZombieTickingRemoteCount = remoteCount;
				if (remoteCount == 0)
					tickManager.ResetRemoteScheduler();
				else if (tickManager.remoteSchedulerQueue.Count > zombieCount + 32)
					tickManager.CompactRemoteQueue(remote, remoteCount);

				var nominalRemoteRate = remoteCount == 0
					? 1f
					: Mathf.Clamp01(Math.Max(0, targetTickingCount - priorityCount) / (float)remoteCount);
				var remoteTickRate = remoteCount == 0
					? 1f
					: Mathf.Clamp(Mathf.Max(nominalRemoteRate * ZombieTicker.RemoteSelectionScale, ZombieTicker.RemoteTickFloor), 0f, 1f);
				tickManager.lastZombieTickingRemoteTickRate = remoteTickRate;
				for (var i = 0; i < remoteCount; i++)
					remote[i].simulationTickRate = remoteTickRate;

				var remoteQuota = 0;
				if (remoteCount > 0)
				{
					tickManager.remoteWorkCarry += remoteCount * (double)remoteTickRate;
					remoteQuota = Math.Min(remoteCount, (int)Math.Floor(tickManager.remoteWorkCarry + 0.000000001d));
					tickManager.remoteWorkCarry -= remoteQuota;
				}

				EnsureZombieBufferCapacity(ref tickManager.currentZombiesTicking, priorityCount + remoteQuota);
				selected = tickManager.currentZombiesTicking;
				var selectedCount = 0;
				for (var i = 0; i < priorityCount; i++)
				{
					var zombie = priority[i];
					zombie.simulationTickRate = 1f;
					selected[selectedCount++] = zombie;
					if (telemetryEnabled)
					{
						var exactVisible = hasViewRect && exactViewRect.Contains(zombie.Position);
						var cameraProtected = hasViewRect && protectedViewRect.Contains(zombie.Position);
						ZombieTickingTelemetry.RecordSelected(zombie, exactVisible, cameraProtected, true);
					}
				}
				var selectedRemoteCount = tickManager.SelectFairRemoteZombies(selected, selectedCount, remoteQuota, eligibilityGeneration);
				if (selectedRemoteCount < remoteQuota)
					tickManager.remoteWorkCarry = Math.Min(remoteCount, tickManager.remoteWorkCarry + remoteQuota - selectedRemoteCount);
				if (telemetryEnabled)
					for (var i = 0; i < selectedRemoteCount; i++)
					{
						var zombie = selected[selectedCount + i];
						var exactVisible = hasViewRect && exactViewRect.Contains(zombie.Position);
						var cameraProtected = hasViewRect && protectedViewRect.Contains(zombie.Position);
						ZombieTickingTelemetry.RecordSelected(zombie, exactVisible, cameraProtected, false);
					}
				selectedCount += selectedRemoteCount;
				tickManager.lastZombieTickingSelectedRemoteCount = selectedRemoteCount;
				tickManager.currentZombiesTickingCount = selectedCount;
			}
			ClearZombieBuffer(tickManager.priorityZombiesTickingCandidates, tickManager.lastZombieTickingPriorityCount, previousPriorityCount);
			ClearZombieBuffer(tickManager.remoteZombiesTickingCandidates, tickManager.lastZombieTickingRemoteCount, previousRemoteCount);
			ClearZombieBuffer(tickManager.currentZombiesTicking, tickManager.currentZombiesTickingCount, previousTickingCount);
			tickManager.currentZombiesTickingIndex = tickManager.currentZombiesTickingCount;
		}

		static float CountBasedTickFraction(int zombieCount)
		{
			var fullRateZombieTickBudget = Math.Max(1, ZombieSettings.Values.maximumNumberOfZombies / 2);
			if (zombieCount <= fullRateZombieTickBudget)
				return 1f;
			return Math.Max(fullRateZombieTickBudget / (float)zombieCount, 1f / zombieCount);
		}

		int NextRemoteEligibilityGeneration()
		{
			if (remoteEligibilityGeneration == int.MaxValue)
				remoteEligibilityGeneration = 1;
			else
				remoteEligibilityGeneration++;
			return remoteEligibilityGeneration;
		}

		void EnsureRemoteQueueMembership(Zombie zombie)
		{
			if (zombie.remoteSchedulerOwner == this && zombie.remoteSchedulerGeneration == remoteSchedulerGeneration)
				return;
			zombie.remoteSchedulerOwner = this;
			zombie.remoteSchedulerGeneration = remoteSchedulerGeneration;
			remoteSchedulerQueue.Enqueue(zombie);
		}

		int SelectFairRemoteZombies(Zombie[] destination, int destinationOffset, int count, int eligibilityGeneration)
		{
			if (count <= 0)
				return 0;
			var selected = 0;
			var inspectCount = remoteSchedulerQueue.Count;
			for (var i = 0; i < inspectCount; i++)
			{
				var zombie = remoteSchedulerQueue.Dequeue();
				if (zombie == null || zombie.remoteSchedulerOwner != this || zombie.remoteSchedulerGeneration != remoteSchedulerGeneration
					|| zombie.Spawned == false || zombie.Dead || zombie.Map != map)
				{
					remoteQueueStaleDiscards++;
					if (zombie != null && zombie.remoteSchedulerOwner == this && zombie.remoteSchedulerGeneration == remoteSchedulerGeneration)
					{
						zombie.remoteSchedulerOwner = null;
						zombie.remoteSchedulerGeneration = 0;
					}
					continue;
				}

				remoteSchedulerQueue.Enqueue(zombie);
				if (selected >= count || zombie.remoteEligibilityGeneration != eligibilityGeneration)
					continue;
				destination[destinationOffset + selected] = zombie;
				selected++;
				if (selected >= count)
					break;
			}
			return selected;
		}

		void CompactRemoteQueue(Zombie[] remote, int remoteCount)
		{
			ResetRemoteScheduler();
			remoteQueueCompactions++;
			for (var i = 0; i < remoteCount; i++)
				EnsureRemoteQueueMembership(remote[i]);
		}

		public void ResetRemoteScheduler(bool resetDiagnostics = false)
		{
			remoteSchedulerQueue.Clear();
			remoteWorkCarry = 0d;
			remoteSchedulerGeneration = remoteSchedulerGeneration == int.MaxValue ? 1 : remoteSchedulerGeneration + 1;
			if (resetDiagnostics)
			{
				remoteQueueStaleDiscards = 0;
				remoteQueueCompactions = 0;
			}
		}

		static bool HasAttackCandidateNeighbor(Map map, Zombie zombie, bool[] targetNeighborCells)
		{
			if (targetNeighborCells == null)
				return false;
			var index = map.cellIndices.CellToIndex(zombie.Position);
			return index >= 0 && index < targetNeighborCells.Length && targetNeighborCells[index];
		}

		internal static bool ShouldPrioritizeZombie(TickManager tickManager, Zombie zombie, bool hasViewRect, CellRect viewRect)
		{
			if (zombie.state == ZombieState.Tracking || zombie.raging > 0 || zombie.wasMapPawnBefore || zombie.ropedBy != null || zombie.wallPushProgress >= 0f)
				return true;
			if (zombie.IsTanky || zombie.IsSuicideBomber || zombie.isAlbino || zombie.isDarkSlimer || zombie.isElectrifier || zombie.isHealer || zombie.isMiner || zombie.isToxicSplasher || zombie.isOnFire)
				return true;
			var pos = zombie.Position;
			var map = tickManager.map;
			if (zombie.attackCandidateNeighborTick == GenTicks.TicksAbs && zombie.hasAttackCandidateNeighbor)
				return true;
			if (hasViewRect && viewRect.Contains(pos))
				return true;
			if (map.areaManager.Home[pos])
				return true;
			return tickManager.centerOfInterest.IsValid && pos.DistanceToSquared(tickManager.centerOfInterest) <= 2025;
		}

		static void EnsureZombieBufferCapacity(ref Zombie[] buffer, int capacity)
		{
			if (buffer != null && buffer.Length >= capacity)
				return;

			var current = buffer?.Length ?? 0;
			var next = Math.Max(capacity, Math.Max(16, current * 2));
			Array.Resize(ref buffer, next);
		}

		static void ClearZombieBuffer(Zombie[] buffer, int from, int previousCount)
		{
			if (buffer == null || previousCount <= from)
				return;
			Array.Clear(buffer, from, previousCount - from);
		}

		public void ClearZombieTickingBuffers()
		{
			ClearZombieBuffer(currentZombiesTicking, 0, currentZombiesTickingCount);
			currentZombiesTickingCount = 0;
			currentZombiesTickingIndex = 0;
			ClearZombieBuffer(currentZombiesTickingCandidates, 0, currentZombiesTickingCandidatesCount);
			currentZombiesTickingCandidatesCount = 0;
			ClearZombieBuffer(priorityZombiesTickingCandidates, 0, lastZombieTickingPriorityCount);
			ClearZombieBuffer(remoteZombiesTickingCandidates, 0, lastZombieTickingRemoteCount);
			lastZombieTickingSplit = false;
			lastZombieTickingTargetCount = 0;
			lastZombieTickingPriorityCount = 0;
			lastZombieTickingRemoteCount = 0;
			lastZombieTickingSelectedRemoteCount = 0;
			lastZombieTickingRemoteTickRate = 1f;
		}

		public static void DoThreadedSingleTick(object input)
		{
			// is being called by many threads at the same time
			var tickManager = (TickManager)input;
			if (tickManager.RuntimeReady == false || tickManager.currentZombiesTickingCount <= 0)
				return;

			var threatLevel = ZombieWeather.GetThreatLevel(tickManager.map);
			while (true)
			{
				var idx = Interlocked.Decrement(ref tickManager.currentZombiesTickingIndex);
				if (idx < 0)
					return;
				tickManager.currentZombiesTicking[idx].CustomTick(threatLevel);
				Interlocked.Increment(ref ZombieTicker.zombiesTicked);
			}
		}

		public static float ZombieMaxCosts(Zombie zombie)
		{
			return zombie.wasMapPawnBefore || zombie.raging > 0 ? 3000f : 1000f;
		}

		public Zombie GetRopableZombie(Vector3 clickPos)
		{
			if (allZombiesCached == null)
				return null;
			return allZombiesCached.FirstOrDefault(zombie => zombie.IsConfused && (clickPos - zombie.DrawPos).MagnitudeHorizontalSquared() <= 0.5f);
		}

		public void RequestAvoidGridRefresh()
		{
			if (map == null || RuntimeReady == false)
				return;
			avoidGridRefreshRequested = true;
		}

		bool FlushRequestedAvoidGridRefresh()
		{
			if (avoidGridRefreshRequested == false)
				return false;
			avoidGridRefreshRequested = false;
			UpdateZombieAvoider(true);
			return true;
		}

		List<ZombieCostSpecs> BuildAvoidGridSpecs(IEnumerable<Zombie> zombies = null)
		{
			var source = zombies ?? allZombiesCached ?? Enumerable.Empty<Zombie>();
			return BuildAvoidGridSpecsFor(source);
		}

		internal static void SeedElectricAvoidGridSnapshots(IEnumerable<Zombie> zombies)
		{
			foreach (var zombie in zombies ?? Enumerable.Empty<Zombie>())
				if (zombie?.isElectrifier == true)
					zombie.SeedElectricAvoidGridSnapshot();
		}

		internal static List<ZombieCostSpecs> BuildAvoidGridSpecsFor(IEnumerable<Zombie> zombies)
		{
			return (zombies ?? Enumerable.Empty<Zombie>()).Where(ShouldAffectAvoidGrid)
				.Select(zombie => new ZombieCostSpecs()
				{
					position = zombie.Position,
					radius = Tools.ZombieAvoidRadius(zombie),
					maxCosts = ZombieMaxCosts(zombie)

				}).ToList();
		}

		void AcceptAvoidGridSnapshot(AvoidGrid grid)
		{
			if (grid == null)
				return;
			avoidGrid = grid;
			lastAvoidGridRequestTick = GenTicks.TicksGame;
			lastAvoidGridResultTick = lastAvoidGridRequestTick;
			lastAvoidGridRequestId = grid.requestId;
			lastAvoidGridResultId = grid.requestId;
			ClearPromptAvoidGridRequest(grid.requestId);
		}

		bool PromptAvoidGridResultPending()
		{
			return promptAvoidGridResultPending && lastAvoidGridResultId < lastAvoidGridRequestId;
		}

		void ClearPromptAvoidGridRequest(long resultId)
		{
			if (promptAvoidGridResultPending && resultId >= lastAvoidGridRequestId)
				promptAvoidGridResultPending = false;
		}

		public void UpdateZombieAvoider(bool force = false)
		{
			if (force == false && lastAvoidGridRequestId > lastAvoidGridResultId && AvoidGridIsStale() == false)
				return;

			if (Tools.ShouldAvoidZombies() == false)
			{
				emptyAvoidGrid ??= new AvoidGrid(map);
				AcceptAvoidGridSnapshot(emptyAvoidGrid);
				return;
			}

			var specs = BuildAvoidGridSpecs();
			if (force && specs.Count == 0)
			{
				emptyAvoidGrid = Tools.avoider.UpdateZombiePositionsImmediately(map, specs);
				AcceptAvoidGridSnapshot(emptyAvoidGrid);
				return;
			}

			var requestId = Tools.avoider.UpdateZombiePositions(map, specs);
			if (requestId > 0)
			{
				lastAvoidGridRequestId = requestId;
				if (force)
					promptAvoidGridResultPending = true;
			}
			lastAvoidGridRequestTick = GenTicks.TicksGame;
			if (force)
				avoidGridCounter = -1;
		}

		static bool ShouldAffectAvoidGrid(Zombie zombie)
		{
			return zombie?.AffectsAvoidGrid == true;
		}

		public void MarkZombieContact()
		{
			lastZombieContact = GenTicks.TicksGame;
		}

		void HandleIncidents()
		{
			HandleSymbiantIncident();

			if (ZombieFreeEventManager.IsActiveNow())
			{
				var ticks = GenTicks.TicksGame;
				lastZombieContact = Mathf.Max(lastZombieContact, ticks);
				lastZombieSpitter = Mathf.Max(lastZombieSpitter, ticks);
				incidentTickCounter = 0;
				return;
			}

			if (ZombieSettings.Values.spitterThreat > 0f && zombieSpitterInited)
			{
				var ticks = GenTicks.TicksGame;
				var (minTicksForSpitter, deltaContact, deltaSpitter) = Tools.ZombieSpitterParameter();
				var isCountingDown = ShipCountdown.CountingDown;
				if (isCountingDown)
				{
					deltaContact = 0;
					deltaSpitter -= deltaSpitter / 3;
				}
				if (ticks > minTicksForSpitter && ticks - lastZombieContact > deltaContact && ticks - lastZombieSpitter > deltaSpitter)
				{
					if (isCountingDown || CanHaveMoreZombies())
					{
						lastZombieContact = ticks;
						lastZombieSpitter = ticks;
						ZombieSpitter.Spawn(map);
					}
				}
			}

			if (incidentTickCounter++ < GenDate.TicksPerHour)
				return;
			incidentTickCounter = 0;

			if (ZombiesRising.ZombiesForNewIncident(this))
			{
				var success = ZombiesRising.TryExecute(map, incidentInfo.parameters.incidentSize, IntVec3.Invalid, true);
				if (success == false)
					Log.Warning("Incident creation failed. Most likely no valid spawn point found.");
			}
		}

		bool RepositionCondition(Pawn pawn)
		{
			return pawn.Spawned &&
				pawn.health.Downed == false &&
				pawn.Dead == false &&
				pawn.Drafted == false &&
				avoidGrid.InAvoidDanger(pawn) &&
				pawn.InMentalState == false &&
				pawn.InContainerEnclosed == false &&
				(pawn.CurJob == null || (pawn.CurJob.def != JobDefOf.Goto && pawn.CurJob.playerForced == false));
		}

		void UpdateGameSettings()
		{
			var ticks = GenTicks.TicksGame;
			ZombieSettings.Values = ZombieSettings.CalculateInterpolation(ZombieSettings.ValuesOverTime, ticks);
		}

		void RepositionColonists()
		{
			var checkInterval = 15;
			var radius = 7f;
			var radiusSquared = (int)(radius * radius);

			map.mapPawns
					.FreeHumanlikesSpawnedOfFaction(Faction.OfPlayer)
					.Where(colonist => colonist.IsHashIntervalTick(checkInterval) && RepositionCondition(colonist))
					.Do(pawn =>
					{
						var pos = pawn.Position;

						var zombiesNearby = Tools.GetCircle(radius).Select(vec => pos + vec)
							.Where(vec => vec.InBounds(map) && avoidGrid.GetCosts()[vec.x + vec.z * map.Size.x] >= 3000)
							.SelectMany(vec => map.thingGrid.ThingsListAtFast(vec).OfType<Zombie>())
							.Where(zombie => zombie.health.Downed == false);

						var maxDistance = 0;
						var safeDestination = IntVec3.Invalid;
						map.floodFiller.FloodFill(pos, delegate (IntVec3 vec)
						{
							if (!vec.Walkable(map))
								return false;
							if ((float)vec.DistanceToSquared(pos) > radiusSquared)
								return false;
							if (map.thingGrid.ThingAt<Zombie>(vec)?.health.Downed ?? true == false)
								return false;
							if (vec.GetEdifice(map) is Building_Door building_Door && !building_Door.CanPhysicallyPass(pawn))
								return false;
							return !PawnUtility.AnyPawnBlockingPathAt(vec, pawn, true, false);

						}, delegate (IntVec3 vec)
						{
							var distance = zombiesNearby.Select(zombie => (vec - zombie.Position).LengthHorizontalSquared).Sum();
							if (distance > maxDistance)
							{
								maxDistance = distance;
								safeDestination = vec;
							}
							return false;

						});

						if (safeDestination.IsValid)
						{
							var newJob = JobMaker.MakeJob(JobDefOf.Goto, safeDestination);
							newJob.playerForced = true;
							pawn.jobs.StartJob(newJob, JobCondition.InterruptForced, null, false, true, null, null);
						}
					});
		}

		void FetchAvoidGrid()
		{
			if (Tools.ShouldAvoidZombies() == false)
			{
				emptyAvoidGrid ??= new AvoidGrid(map);
				avoidGrid = emptyAvoidGrid;
				lastAvoidGridRequestId = avoidGrid.requestId;
				lastAvoidGridRequestTick = GenTicks.TicksGame;
				lastAvoidGridResultTick = GenTicks.TicksGame;
				lastAvoidGridResultId = avoidGrid.requestId;
				ClearPromptAvoidGridRequest(avoidGrid.requestId);
				return;
			}

			if (avoidGridCounter-- < 0)
			{
				var promptRequestPending = PromptAvoidGridResultPending();
				avoidGridCounter = promptRequestPending ? -1 : Constants.TICKMANAGER_AVOIDGRID_DELAY.SecondsToTicks();

				var result = Tools.avoider.GetCostsGrid(map);
				if (result != null)
				{
					if (result.requestId > lastAvoidGridResultId && result.requestId <= lastAvoidGridRequestId)
					{
						avoidGrid = result;
						lastAvoidGridResultTick = GenTicks.TicksGame;
						lastAvoidGridResultId = result.requestId;
						ClearPromptAvoidGridRequest(result.requestId);
						if (result.requestId == lastAvoidGridRequestId && avoidGridRefreshRequested == false)
							avoidGridCounter = Constants.TICKMANAGER_AVOIDGRID_DELAY.SecondsToTicks();
					}
				}
				else if (AvoidGridIsStale())
				{
					emptyAvoidGrid ??= new AvoidGrid(map);
					avoidGrid = emptyAvoidGrid;
					lastAvoidGridResultTick = GenTicks.TicksGame;
					lastAvoidGridResultId = lastAvoidGridRequestId;
					ClearPromptAvoidGridRequest(lastAvoidGridResultId);
					UpdateZombieAvoider();
					Tools.avoider.RecoverWorkerIfStale(map, lastAvoidGridRequestId);
				}
			}
		}

		bool AvoidGridIsStale()
		{
			var staleTicks = Math.Max(600, Constants.TICKMANAGER_AVOIDGRID_DELAY.SecondsToTicks() * 40);
			var ticks = GenTicks.TicksGame;
			return lastAvoidGridRequestId > lastAvoidGridResultId
				&& lastAvoidGridRequestTick > lastAvoidGridResultTick
				&& lastAvoidGridResultTick > 0
				&& ticks - lastAvoidGridResultTick > staleTicks;
		}

		public IEnumerable<Zombie> AllZombies()
		{
			if (map.mapPawns == null || map.mapPawns.AllPawns == null)
				return new List<Zombie>();
			return map.mapPawns.AllPawns.OfType<Zombie>().Where(zombie => zombie != null);
		}

		public int ZombieCount()
		{
			return (allZombiesCached?.Count(zombie => zombie.Spawned && zombie.Dead == false) ?? 0) + ZombieGenerator.ZombiesSpawning;
		}

		public bool CanHaveMoreZombies()
		{
			var currentMax = Mathf.FloorToInt(GetMaxZombieCount() * ZombieWeather.GetThreatLevel(map));
			return ZombieCount() < currentMax;
		}

		public bool NewMapZombieDelay(int at)
		{
			if (mapSpawnedTicks == 0)
				return false;
			var ticksDelay = Tools.NewMapZombieTicksDelay();
			return at - mapSpawnedTicks < ticksDelay;
		}

		public void IncreaseZombiePopulation()
		{
			if (map.IsBlacklisted())
				return;
			if (GenDate.DaysPassedFloat < ZombieSettings.Values.daysBeforeZombiesCome)
				return;
			if (NewMapZombieDelay(GenTicks.TicksGame))
				return;
			if (ZombieSettings.Values.spawnWhenType == SpawnWhenType.InEventsOnly)
				return;

			if (populationSpawnCounter-- < 0)
			{
				var min = GenMath.LerpDoubleClamped(1.5f, 5, 400, 15, Tools.Difficulty());
				var max = GenMath.LerpDoubleClamped(1.5f, 5, 15, 2, Tools.Difficulty());
				populationSpawnCounter = (int)GenMath.LerpDoubleClamped(0, 40000, min, max, currentColonyPoints);

				if (CanHaveMoreZombies())
				{
					switch (ZombieSettings.Values.spawnHowType)
					{
						case SpawnHowType.AllOverTheMap:
						{
							var cell = Tools.RandomSpawnCell(map, false, Tools.ZombieSpawnLocator(map));
							if (cell.IsValid)
							{
								var zombie = ZombieGenerator.SpawnZombie(cell, map, ZombieType.Random);
								_ = allZombiesCached.Add(zombie);
							}
							return;
						}
						case SpawnHowType.FromTheEdges:
						{
							var cell = Tools.RandomSpawnCell(map, true, Tools.ZombieSpawnLocator(map));
							if (cell.IsValid)
							{
								var zombie = ZombieGenerator.SpawnZombie(cell, map, ZombieType.Random);
								_ = allZombiesCached.Add(zombie);
							}
							return;
						}
						default:
						{
							Log.Error("Unknown spawn type " + ZombieSettings.Values.spawnHowType);
							return;
						}
					}
				}
			}
		}

		void InitializeZombieSymbiantSchedule(int ticks)
		{
			lastZombieSymbiant = ticks;
			zombieSymbiantInited = true;
			nextZombieSymbiant = ticks + ZombieSymbiantDelayTicks(true);
		}

		void ScheduleNextZombieSymbiant(int ticks, bool afterSuccess)
		{
			nextZombieSymbiant = ticks + ZombieSymbiantDelayTicks(afterSuccess);
		}

		void UpdateZombieSymbiantPresence(int ticks, ZombieSymbiant activeSymbiant)
		{
			if (activeSymbiant != null)
			{
				zombieSymbiantWasActive = true;
				return;
			}

			if (zombieSymbiantWasActive == false)
				return;

			zombieSymbiantWasActive = false;
			lastZombieSymbiantGone = ticks;
			nextZombieSymbiant = Mathf.Max(nextZombieSymbiant, ticks + MinimumTicksBetweenZombieSymbiants);
		}

		bool ZombieSymbiantMinimumPauseActive(int ticks)
		{
			if (lastZombieSymbiantGone <= 0)
				return false;
			var pauseUntil = lastZombieSymbiantGone + MinimumTicksBetweenZombieSymbiants;
			if (ticks >= pauseUntil)
				return false;
			nextZombieSymbiant = Mathf.Max(nextZombieSymbiant, pauseUntil);
			return true;
		}

		int ZombieSymbiantDelayTicks(bool afterSuccess)
		{
			var difficulty = Mathf.Clamp(Tools.Difficulty(), 0f, 5f);
			if (afterSuccess == false)
			{
				var retryDays = Rand.Range(0.75f, 2.5f) * GenMath.LerpDoubleClamped(0f, 5f, 1.35f, 0.75f, difficulty);
				return Mathf.Max(GenDate.TicksPerHour, Mathf.RoundToInt(retryDays * GenDate.TicksPerDay));
			}

			var pressure = Mathf.Max(0.35f, ZombieSymbiant.NaturalSpawnPressure(map, true));
			var threat = Mathf.Max(0.1f, ZombieWeather.GetThreatLevelIgnoringZombieFreeEvent(map));
			var minDays = GenMath.LerpDoubleClamped(0f, 5f, 22f, 5f, difficulty);
			var maxDays = GenMath.LerpDoubleClamped(0f, 5f, 38f, 11f, difficulty);
			var pressureFactor = GenMath.LerpDoubleClamped(0.35f, 1.6f, 1.25f, 0.70f, pressure);
			var threatFactor = GenMath.LerpDoubleClamped(0f, 1f, 1.20f, 0.85f, threat);
			var colonyFactor = GenMath.LerpDoubleClamped(0f, 40000f, 1.15f, 0.75f, currentColonyPoints);
			var days = Rand.Range(minDays, maxDays) * pressureFactor * threatFactor * colonyFactor;
			return Mathf.RoundToInt(Mathf.Clamp(days, 3f, 60f) * GenDate.TicksPerDay);
		}

		void HandleSymbiantIncident()
		{
			var ticks = GenTicks.TicksGame;
			if (zombieSymbiantInited == false)
				InitializeZombieSymbiantSchedule(ticks);
			var activeSymbiant = ZombieSymbiant.ActiveSymbiant(map);
			UpdateZombieSymbiantPresence(ticks, activeSymbiant);
			if (ZombieSettings.Values.symbiantEnabled == false)
				return;
			if (nextZombieSymbiant <= 0)
				ScheduleNextZombieSymbiant(ticks, false);
			if (ticks < nextZombieSymbiant)
				return;
			if (activeSymbiant != null)
			{
				ScheduleNextZombieSymbiant(ticks, false);
				return;
			}
			if (ZombieSymbiantMinimumPauseActive(ticks))
				return;
			if (map.IsBlacklisted() || GenDate.DaysPassedFloat < ZombieSettings.Values.daysBeforeZombiesCome || NewMapZombieDelay(ticks) || ZombieWeather.GetThreatLevelIgnoringZombieFreeEvent(map) <= 0f)
			{
				ScheduleNextZombieSymbiant(ticks, false);
				return;
			}
			if (ZombieSymbiant.NaturalSpawnPressure(map) <= 0f)
			{
				ScheduleNextZombieSymbiant(ticks, false);
				return;
			}
			if (ZombieSymbiant.TrySpawnInBestRoom(map))
			{
				lastZombieSymbiant = ticks;
				zombieSymbiantWasActive = true;
				ScheduleNextZombieSymbiant(ticks, true);
				return;
			}
			ScheduleNextZombieSymbiant(ticks, false);
		}

		public void TickHeads()
		{
			for (var i = victimHeads.Count - 1; i >= 0; i--)
			{
				var head = victimHeads[i];
				if (head.Tick())
				{
					head.Cleanup();
					victimHeads.RemoveAt(i);
				}
			}
		}

		public void AddExplosion(IntVec3 pos)
		{
			explosions.Add(pos);
		}

		public void RequestZombieHitSound(Thing target)
		{
			if (target?.Spawned != true || target.Map != map)
				return;

			var realtime = Time.realtimeSinceStartup;
			var bucket = FindZombieHitSoundBucket(target.Position);
			if (bucket == null)
			{
				bucket = new ZombieHitSoundBucket { target = target, center = target.Position, nextPlayRealtime = -1f };
				zombieHitSoundBuckets.Add(bucket);
			}

			bucket.target = target;
			bucket.center = target.Position;
			bucket.pendingRequests++;
			bucket.lastRequestRealtime = realtime;
		}

		ZombieHitSoundBucket FindZombieHitSoundBucket(IntVec3 position)
		{
			for (var i = 0; i < zombieHitSoundBuckets.Count; i++)
			{
				var bucket = zombieHitSoundBuckets[i];
				if ((bucket.center - position).LengthHorizontalSquared <= ZombieHitSoundClusterRadiusSquared)
					return bucket;
			}
			return null;
		}

		public void ExecuteExplosions()
		{
			foreach (var position in explosions)
			{
				var explosion = new Explosion(map, position);
				explosion.Explode();
			}
			explosions.Clear();
		}

		public void UpdateElectricalHumming()
		{
			var ticks = DateTime.Now.Ticks;
			if ((ticks % 30) != 0)
				return;

			if (ZombieAwarenessCues.ShouldPlaySpecialZombieAmbientSound() == false)
			{
				electricSustainer?.End();
				electricSustainer = null;
				return;
			}

			electricSustainer ??= CustomDefs.ZombieElectricHum.TrySpawnSustainer(SoundInfo.OnCamera(MaintenanceType.None));

			if (hummingZombies.Count == 0)
			{
				electricSustainer.info.volumeFactor = 0f;
				return;
			}

			var cameraPos = Find.CameraDriver.transform.position;
			var nearestElectricalZombieDistance = float.MaxValue;
			foreach (var zombie in hummingZombies)
			{
				if (zombie == null)
					continue;
				var distance = (cameraPos - zombie.DrawPos).magnitude;
				if (distance < nearestElectricalZombieDistance)
					nearestElectricalZombieDistance = distance;
			}

			electricSustainer.info.volumeFactor = GenMath.LerpDoubleClamped(12f, 36f, 1f, 0f, nearestElectricalZombieDistance);
		}

		public void UpdateTankMovement()
		{
			var ticks = DateTime.Now.Ticks;
			if ((ticks % 30) != 0)
				return;

			if (ZombieAwarenessCues.ShouldPlaySpecialZombieAmbientSound() == false)
			{
				tankSustainer?.End();
				tankSustainer = null;
				return;
			}

			tankSustainer ??= CustomDefs.ZombieTankMovement.TrySpawnSustainer(SoundInfo.OnCamera(MaintenanceType.None));

			if (tankZombies.Count == 0)
			{
				tankSustainer.info.volumeFactor = 0f;
				return;
			}

			var cameraPos = Find.CameraDriver.transform.position;
			var nearestTankZombieDistance = float.MaxValue;
			foreach (var zombie in tankZombies)
			{
				if (zombie == null)
					continue;
				var distance = (cameraPos - zombie.DrawPos).magnitude;
				if (distance < nearestTankZombieDistance)
					nearestTankZombieDistance = distance;
			}

			tankSustainer.info.volumeFactor = GenMath.LerpDoubleClamped(24f, 64f, 1f, 0f, nearestTankZombieDistance);
		}

		public void UpdateZombieHitSounds()
		{
			if (zombieHitSoundBuckets.Count == 0)
				return;

			var timeSpeed = Find.TickManager.CurTimeSpeed;
			if (timeSpeed == TimeSpeed.Paused || CustomDefs.ZombieHit == null || Prefs.VolumeAmbient <= 0f)
				return;

			var realtime = Time.realtimeSinceStartup;
			for (var i = zombieHitSoundBuckets.Count - 1; i >= 0; i--)
			{
				var bucket = zombieHitSoundBuckets[i];
				if (bucket.target == null || bucket.target.Spawned == false || bucket.target.Destroyed || bucket.target.Map != map || realtime - bucket.lastRequestRealtime > ZombieHitSoundBucketTtl)
				{
					zombieHitSoundBuckets.RemoveAt(i);
					continue;
				}
				if (bucket.pendingRequests <= 0)
					continue;
				if (bucket.nextPlayRealtime >= 0f && realtime < bucket.nextPlayRealtime)
					continue;
				if (nextGlobalZombieHitSoundRealtime >= 0f && realtime < nextGlobalZombieHitSoundRealtime)
					continue;

				CustomDefs.ZombieHit.PlayOneShot(SoundInfo.InMap(bucket.target));
				var requestCount = bucket.pendingRequests;
				bucket.pendingRequests = 0;
				bucket.nextPlayRealtime = realtime + ZombieHitSoundInterval(requestCount);
				nextGlobalZombieHitSoundRealtime = realtime + ZombieHitSoundGlobalMinInterval;
			}
		}

		const float ZombieHitSoundBucketTtl = 1f;
		const float ZombieHitSoundGlobalMinInterval = 0.20f;
		const int ZombieHitSoundClusterRadiusSquared = 196;

		static float ZombieHitSoundInterval(int requestCount)
		{
			return GenMath.LerpDoubleClamped(1f, 8f, 0.75f, 0.48f, Mathf.Clamp(requestCount, 1, 8));
		}

		public void UpdateSuicideBomberPieps()
		{
			if (suicideBomberZombies.Count == 0)
				return;
			if (Time.frameCount >= nextSuicideBomberCleanupFrame)
			{
				suicideBomberZombies.RemoveWhere(zombie => IsTrackedSuicideBomber(zombie, map) == false);
				nextSuicideBomberCleanupFrame = Time.frameCount + 60;
				if (suicideBomberZombies.Count == 0)
					return;
			}

			var realtime = Time.realtimeSinceStartup;
			var timeSpeed = Find.TickManager.CurTimeSpeed;
			var playSound = ZombieAwarenessCues.ShouldPlaySpecialZombieAmbientSound();
			foreach (var zombie in suicideBomberZombies)
			{
				if (IsTrackedSuicideBomber(zombie, map) == false)
					continue;
				UpdateSuicideBomberLight(zombie);
				UpdateSuicideBomberPiep(zombie, realtime, timeSpeed, playSound);
			}
		}

		static bool IsTrackedSuicideBomber(Zombie zombie, Map map)
		{
			return zombie?.Spawned == true
				&& zombie.Dead == false
				&& zombie.Destroyed == false
				&& zombie.Map == map
				&& zombie.IsSuicideBomber;
		}

		static void UpdateSuicideBomberLight(Zombie zombie)
		{
			var currentTick = Find.TickManager.TicksAbs;
			var interval = Mathf.Max(1, Mathf.RoundToInt(zombie.bombTickingInterval));
			if (currentTick >= zombie.lastBombTick + interval)
			{
				zombie.lastBombTick = currentTick;
				zombie.bombLightOn = false;
				return;
			}

			zombie.bombLightOn = currentTick <= zombie.lastBombTick + interval / 2;
		}

		static void UpdateSuicideBomberPiep(Zombie zombie, float realtime, TimeSpeed timeSpeed, bool playSound)
		{
			var lightStarted = zombie.bombLightOn && zombie.bombPiepLightWasOn == false;
			zombie.bombPiepLightWasOn = zombie.bombLightOn;
			if (playSound == false || CustomDefs.Piep == null || timeSpeed == TimeSpeed.Paused)
				return;

			if (timeSpeed == TimeSpeed.Normal)
			{
				if (lightStarted == false)
					return;
				CustomDefs.Piep.PlayOneShot(SoundInfo.InMap(zombie));
				zombie.nextBombPiepRealtime = realtime + SuicideBomberPiepPeriod(zombie);
				return;
			}

			var period = SuicideBomberPiepPeriod(zombie);
			if (zombie.nextBombPiepRealtime < 0f || realtime - zombie.nextBombPiepRealtime > period)
				zombie.nextBombPiepRealtime = realtime;
			if (realtime < zombie.nextBombPiepRealtime)
				return;

			CustomDefs.Piep.PlayOneShot(SoundInfo.InMap(zombie));
			do
			{
				zombie.nextBombPiepRealtime += period;
			}
			while (zombie.nextBombPiepRealtime <= realtime);
		}

		static float SuicideBomberPiepPeriod(Zombie zombie)
		{
			return Mathf.Max(0.1f, zombie.bombTickingInterval / 60f);
		}

		sealed class ZombieHitSoundBucket
		{
			public Thing target;
			public IntVec3 center;
			public int pendingRequests;
			public float lastRequestRealtime;
			public float nextPlayRealtime;
		}

		public void StopAmbientSound()
		{
			zombiesAmbientSound?.End();
			zombiesAmbientSound = null;
		}

		IEnumerator TickTasks(bool skipFirstIncidentPass = false)
		{
			var skipIncidents = skipFirstIncidentPass;
			if (runZombiesForNewIncident && map != null)
			{
				runZombiesForNewIncident = false;
				_ = ZombiesRising.ZombiesForNewIncident(this);
			}

			while (true)
			{
				UpdateGameSettings();
				yield return null;
				RepositionColonists();
				yield return null;
				if (Constants.CONTAMINATION)
				{
					contaminationEffects.Tick();
					yield return null;
				}
				if (skipIncidents)
					skipIncidents = false;
				else
					HandleIncidents();
				yield return null;
				FetchAvoidGrid();
				yield return null;
				RecalculateColonyPoints();
				yield return null;
				RecalculateZombieWanderDestination();
				yield return null;
				if (FlushRequestedAvoidGridRefresh() == false)
					UpdateZombieAvoider();
				yield return null;
				ExecuteExplosions();
				yield return null;
				var volume = 0f;
				var zombieFreeEventActive = ZombieFreeEventManager.IsActiveNow();
				if (zombieFreeEventActive == false && allZombiesCached.Any())
				{
					if (map != null)
					{
						var hour = GenLocalDate.HourFloat(map);
						if (hour < 12f)
							hour += 24f;
						if (hour > Constants.ZOMBIE_SPAWNING_HOURS[1] && hour < Constants.ZOMBIE_SPAWNING_HOURS[2])
							volume = 1f;
						else if (hour >= Constants.ZOMBIE_SPAWNING_HOURS[0] && hour <= Constants.ZOMBIE_SPAWNING_HOURS[1])
							volume = GenMath.LerpDouble(Constants.ZOMBIE_SPAWNING_HOURS[0], Constants.ZOMBIE_SPAWNING_HOURS[1], 0f, 1f, hour);
						else if (hour >= Constants.ZOMBIE_SPAWNING_HOURS[2] && hour <= Constants.ZOMBIE_SPAWNING_HOURS[3])
							volume = GenMath.LerpDouble(Constants.ZOMBIE_SPAWNING_HOURS[2], Constants.ZOMBIE_SPAWNING_HOURS[3], 1f, 0f, hour);
					}
				}
				ZombieStateHandler.creepyAmbientSoundVolumes[map.uniqueID] = volume;
				yield return null;
				if (zombieFreeEventActive)
				{
					zombiesAmbientSoundVolume = 0f;
					StopAmbientSound();
					yield return null;
				}
				else if (Constants.USE_SOUND && ZombieSettings.Values.playCreepyAmbientSound)
				{
					zombiesAmbientSound ??= CustomDefs.ZombiesClosingIn.TrySpawnSustainer(SoundInfo.OnCamera(MaintenanceType.None));

					if (volume < zombiesAmbientSoundVolume)
						zombiesAmbientSoundVolume -= 0.0001f;
					else if (volume > zombiesAmbientSoundVolume)
						zombiesAmbientSoundVolume += 0.0001f;
					zombiesAmbientSound.info.volumeFactor = zombiesAmbientSoundVolume;
				}
				else
				{
					StopAmbientSound();
					yield return null;
				}

				if (colonistsToConvert.Count > 0 && map != null)
				{
					var pawn = colonistsToConvert.Dequeue();
					Tools.ConvertToZombie(pawn, map);
					yield return null;
				}
				if (rimConnectActions.Count > 0 && map != null)
				{
					var action = rimConnectActions.Dequeue();
					action(map);
					yield return null;
				}

				yield return "end"; // must be called "end"!
			}
		}

		public override void MapComponentTick()
		{
			base.MapComponentTick();
			ShowQueuedPlayerInitializationProblemNoticeIfStillBroken();

			if (TryEnsureRuntimeInitialized("TickManager.MapComponentTick") == false)
			{
				ReportInitializationProblemOnce($"MapComponentTick skipped because TickManager is not ready (state {isInitialized}, taskTicker {(taskTicker == null ? "missing" : "present")}).");
				ShowQueuedPlayerInitializationProblemNoticeIfStillBroken();
				return;
			}

			_ = taskTicker.MoveNext();
			IncreaseZombiePopulation();
			SoSTools.GenerateSpaceZombies(this);
			TickHeads();
		}
	}
}
