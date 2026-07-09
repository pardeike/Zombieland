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
		Normal,
		Throttled,
		RemoteFrozen
	}

	[StaticConstructorOnStartup]
	public static class ZombieTicker
	{
		const float throttleEnterSeverity = 0.35f;
		const float throttleLeaveSeverity = 0.20f;
		const float freezeEnterSeverity = 0.70f;
		const float freezeLeaveSeverity = 0.45f;
		const float saturationSmoothing = 0.25f;
		const int saturationEnterUpdates = 8;
		const int throttleLeaveUpdates = 60;
		const int freezeLeaveUpdates = 45;

		public static IEnumerable<TickManager> managers;
		public static Type RimThreaded = AccessTools.TypeByName("RimThreaded.RimThreaded");

		public static float[] percentZombiesTicked = new[] { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f };
		public static int percentZombiesTickedIndex = 0;

		public static int zombiesTicked = 0;
		public static int maxTicking = 0;
		public static int currentTicking = 0;
		public static ZombieSaturationState saturationState = ZombieSaturationState.Normal;
		public static float saturationSeverity = 0f;
		public static float lastSaturationSampleSeverity = 0f;
		public static float lastSaturationFrameMilliseconds = 0f;
		public static float lastSaturationDebtTicks = 0f;
		public static float lastFrameSeverity = 0f;
		public static float lastDebtSeverity = 0f;
		public static int saturationSampleCount = 0;
		public static int throttleEnterCounter = 0;
		public static int throttleRecoveryCounter = 0;
		public static int freezeEnterCounter = 0;
		public static int freezeRecoveryCounter = 0;

		public static string SaturationStateName
		{
			get
			{
				switch (saturationState)
				{
					case ZombieSaturationState.Throttled:
						return "throttled";
					case ZombieSaturationState.RemoteFrozen:
						return "remoteFrozen";
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
					case ZombieSaturationState.RemoteFrozen:
						return 1f;
					case ZombieSaturationState.Throttled:
						return 0.02f;
					default:
						return 0.05f;
				}
			}
		}

		public static float RemoteSelectionScale
		{
			get
			{
				switch (saturationState)
				{
					case ZombieSaturationState.RemoteFrozen:
						return 0f;
					case ZombieSaturationState.Throttled:
					{
						var t = Mathf.InverseLerp(throttleEnterSeverity, freezeEnterSeverity, saturationSeverity);
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
			public float sampleSeverity;
			public float frameMilliseconds;
			public float debtTicks;
			public float frameSeverity;
			public float debtSeverity;
			public int sampleCount;
			public int throttleEnter;
			public int throttleRecovery;
			public int freezeEnter;
			public int freezeRecovery;
		}

		public static SaturationSnapshot CaptureSaturation()
		{
			return new SaturationSnapshot
			{
				state = saturationState,
				severity = saturationSeverity,
				sampleSeverity = lastSaturationSampleSeverity,
				frameMilliseconds = lastSaturationFrameMilliseconds,
				debtTicks = lastSaturationDebtTicks,
				frameSeverity = lastFrameSeverity,
				debtSeverity = lastDebtSeverity,
				sampleCount = saturationSampleCount,
				throttleEnter = throttleEnterCounter,
				throttleRecovery = throttleRecoveryCounter,
				freezeEnter = freezeEnterCounter,
				freezeRecovery = freezeRecoveryCounter
			};
		}

		public static void RestoreSaturation(SaturationSnapshot snapshot)
		{
			saturationState = snapshot.state;
			saturationSeverity = snapshot.severity;
			lastSaturationSampleSeverity = snapshot.sampleSeverity;
			lastSaturationFrameMilliseconds = snapshot.frameMilliseconds;
			lastSaturationDebtTicks = snapshot.debtTicks;
			lastFrameSeverity = snapshot.frameSeverity;
			lastDebtSeverity = snapshot.debtSeverity;
			saturationSampleCount = snapshot.sampleCount;
			throttleEnterCounter = snapshot.throttleEnter;
			throttleRecoveryCounter = snapshot.throttleRecovery;
			freezeEnterCounter = snapshot.freezeEnter;
			freezeRecoveryCounter = snapshot.freezeRecovery;
		}

		public static void ResetSaturation()
		{
			RestoreSaturation(new SaturationSnapshot { state = ZombieSaturationState.Normal });
		}

		public static void UpdateSaturation(float frameMilliseconds, float debtTicks)
		{
			lastSaturationFrameMilliseconds = Mathf.Max(0f, frameMilliseconds);
			lastSaturationDebtTicks = Mathf.Max(0f, debtTicks);
			lastFrameSeverity = Mathf.Clamp01((lastSaturationFrameMilliseconds - 22f) / 40f);
			lastDebtSeverity = Mathf.Clamp01((lastSaturationDebtTicks - 1.25f) / 2.75f);
			lastSaturationSampleSeverity = Mathf.Max(lastFrameSeverity, lastDebtSeverity);
			saturationSeverity = saturationSampleCount == 0
				? lastSaturationSampleSeverity
				: Mathf.Lerp(saturationSeverity, lastSaturationSampleSeverity, saturationSmoothing);
			saturationSampleCount++;
			UpdateSaturationState();
		}

		static void UpdateSaturationState()
		{
			if (saturationSeverity >= freezeEnterSeverity)
				freezeEnterCounter++;
			else
				freezeEnterCounter = 0;

			if (saturationState != ZombieSaturationState.RemoteFrozen && freezeEnterCounter >= saturationEnterUpdates)
			{
				saturationState = ZombieSaturationState.RemoteFrozen;
				throttleEnterCounter = 0;
				throttleRecoveryCounter = 0;
				freezeRecoveryCounter = 0;
				return;
			}

			if (saturationState == ZombieSaturationState.RemoteFrozen)
			{
				if (saturationSeverity < freezeLeaveSeverity)
					freezeRecoveryCounter++;
				else
					freezeRecoveryCounter = 0;

				if (freezeRecoveryCounter >= freezeLeaveUpdates)
				{
					saturationState = ZombieSaturationState.Throttled;
					freezeEnterCounter = 0;
					freezeRecoveryCounter = 0;
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
				freezeEnterCounter = 0;
				freezeRecoveryCounter = 0;
			}
		}

		public static object DescribeSaturation(TickManager tickManager)
		{
			return new
			{
				state = SaturationStateName,
				severity = saturationSeverity,
				sampleSeverity = lastSaturationSampleSeverity,
				frameMilliseconds = lastSaturationFrameMilliseconds,
				debtTicks = lastSaturationDebtTicks,
				frameSeverity = lastFrameSeverity,
				debtSeverity = lastDebtSeverity,
				sampleCount = saturationSampleCount,
				throttleEnterCounter,
				throttleRecoveryCounter,
				freezeEnterCounter,
				freezeRecoveryCounter,
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
					remoteTickRate = tickManager.lastZombieTickingRemoteTickRate
				}
			};
		}

		public static void DoSingleTick()
		{
			if (LongEventHandler.AnyEventNowOrWaiting || LongEventHandler.ShouldWaitForEvent)
				return;
			if (Current.Game == null || Current.ProgramState != ProgramState.Playing || Scribe.mode != LoadSaveMode.Inactive)
				return;
			if (managers == null)
				return;

			if (RimThreaded == null)
				managers.Do(tickManager =>
				{
					if (tickManager.TryEnsureRuntimeInitialized("ZombieTicker.DoSingleTick"))
					{
						tickManager.ZombieTicking();
						return;
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
				});
		}

		public static float PercentTicking
		{
			get
			{
				return percentZombiesTicked.Average();
			}
			set
			{
				percentZombiesTicked[percentZombiesTickedIndex] = value;
				percentZombiesTickedIndex = (percentZombiesTickedIndex + 1) % percentZombiesTicked.Length;
			}
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
		public bool lastZombieTickingSplit;
		public int lastZombieTickingTargetCount;
		public int lastZombieTickingPriorityCount;
		public int lastZombieTickingRemoteCount;
		public int lastZombieTickingSelectedRemoteCount;
		public float lastZombieTickingRemoteTickRate = 1f;

		public int CurrentZombiesTickingCandidatesCount => currentZombiesTickingCandidatesCount;
		public int CurrentZombiesTickingCandidatesCapacity => currentZombiesTickingCandidates?.Length ?? 0;
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

			taskTicker = TickTasks();
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
			StopAmbientSound();
			zombieHitSoundBuckets.Clear();
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
			var f = Mathf.Min(ZombieTicker.PercentTicking, CountBasedTickFraction(zombieCount));
			var targetNeighborCells = tickManager.map.GetComponent<ZombieAttackTargetIndex>()?.CurrentCandidateNeighborsByCell();
			var attackNeighborTick = targetNeighborCells == null ? -1 : GenTicks.TicksAbs;
			for (var i = 0; i < zombieCount; i++)
			{
				var zombie = zombies[i];
				zombie.hasAttackCandidateNeighbor = HasAttackCandidateNeighbor(tickManager.map, zombie, targetNeighborCells);
				zombie.attackCandidateNeighborTick = attackNeighborTick;
			}

			var previousTickingCount = tickManager.currentZombiesTickingCount;
			tickManager.lastZombieTickingSplit = false;
			tickManager.lastZombieTickingTargetCount = zombieCount;
			tickManager.lastZombieTickingPriorityCount = 0;
			tickManager.lastZombieTickingRemoteCount = 0;
			tickManager.lastZombieTickingSelectedRemoteCount = 0;
			tickManager.lastZombieTickingRemoteTickRate = 1f;
			if (f >= 1f && ZombieTicker.saturationState == ZombieSaturationState.Normal)
			{
				EnsureZombieBufferCapacity(ref tickManager.currentZombiesTicking, zombieCount);
				Array.Copy(zombies, tickManager.currentZombiesTicking, zombieCount);
				tickManager.currentZombiesTickingCount = zombieCount;
				for (var i = 0; i < zombieCount; i++)
					zombies[i].simulationTickRate = 1f;
			}
			else
			{
				var targetTickingCount = Mathf.FloorToInt(zombieCount * f);
				tickManager.lastZombieTickingTargetCount = targetTickingCount;
				if (targetTickingCount <= 0)
					tickManager.currentZombiesTickingCount = 0;
				else
				{
					EnsureZombieBufferCapacity(ref tickManager.priorityZombiesTickingCandidates, zombieCount);
					EnsureZombieBufferCapacity(ref tickManager.remoteZombiesTickingCandidates, zombieCount);
					var selected = tickManager.currentZombiesTicking;
					var priority = tickManager.priorityZombiesTickingCandidates;
					var remote = tickManager.remoteZombiesTickingCandidates;
					var priorityCount = 0;
					var remoteCount = 0;
					var hasViewRect = Find.CurrentMap == tickManager.map && Find.CameraDriver != null;
					var viewRect = default(CellRect);
					if (hasViewRect)
					{
						viewRect = Find.CameraDriver.CurrentViewRect.ExpandedBy(12);
						viewRect.ClipInsideMap(tickManager.map);
					}
					for (var i = 0; i < zombieCount; i++)
					{
						var zombie = zombies[i];
						if (ShouldPrioritizeZombie(tickManager, zombie, hasViewRect, viewRect))
							priority[priorityCount++] = zombie;
						else
							remote[remoteCount++] = zombie;
					}

					tickManager.lastZombieTickingSplit = true;
					tickManager.lastZombieTickingPriorityCount = priorityCount;
					tickManager.lastZombieTickingRemoteCount = remoteCount;
					var selectedCount = 0;
					if (priorityCount >= targetTickingCount)
					{
						EnsureZombieBufferCapacity(ref tickManager.currentZombiesTicking, priorityCount);
						selected = tickManager.currentZombiesTicking;
						for (var i = 0; i < priorityCount; i++)
						{
							priority[i].simulationTickRate = 1f;
							selected[selectedCount++] = priority[i];
						}
						var remoteTickRate = ZombieTicker.saturationState == ZombieSaturationState.RemoteFrozen ? 1f : ZombieTicker.RemoteTickFloor;
						for (var i = 0; i < remoteCount; i++)
							remote[i].simulationTickRate = remoteTickRate;
						tickManager.lastZombieTickingRemoteTickRate = remoteTickRate;
					}
					else
					{
						var remoteBudget = targetTickingCount - priorityCount;
						var remoteTickingCount = Math.Min(remoteCount, remoteBudget);
						if (ZombieTicker.saturationState != ZombieSaturationState.Normal)
							remoteTickingCount = Mathf.FloorToInt(remoteTickingCount * ZombieTicker.RemoteSelectionScale);
						var totalTickingCount = priorityCount + remoteTickingCount;
						EnsureZombieBufferCapacity(ref tickManager.currentZombiesTicking, totalTickingCount);
						selected = tickManager.currentZombiesTicking;
						for (var i = 0; i < priorityCount; i++)
						{
							priority[i].simulationTickRate = 1f;
							selected[selectedCount++] = priority[i];
						}
						var remoteTickRate = ZombieTicker.saturationState == ZombieSaturationState.RemoteFrozen ? 1f : TickRateFor(remoteTickingCount, remoteCount, ZombieTicker.RemoteTickFloor);
						selectedCount += SelectRandomZombies(remote, remoteCount, selected, selectedCount, remoteTickingCount, remoteTickRate);
						tickManager.lastZombieTickingSelectedRemoteCount = remoteTickingCount;
						tickManager.lastZombieTickingRemoteTickRate = remoteTickRate;
					}
					tickManager.currentZombiesTickingCount = selectedCount;
				}
			}
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

		static float TickRateFor(int tickCount, int candidateCount, float floor)
		{
			if (candidateCount <= 0)
				return 1f;
			return Mathf.Clamp(tickCount / (float)candidateCount, floor, 1f);
		}

		static int SelectRandomZombies(Zombie[] source, int sourceCount, Zombie[] destination, int destinationOffset, int count, float tickRate)
		{
			for (var i = 0; i < sourceCount; i++)
				source[i].simulationTickRate = tickRate;
			for (var i = 0; i < count; i++)
			{
				var idx = Rand.RangeInclusive(i, sourceCount - 1);
				var selected = source[idx];
				destination[destinationOffset + i] = selected;
				source[idx] = source[i];
				source[i] = selected;
			}
			return count;
		}

		static bool HasAttackCandidateNeighbor(Map map, Zombie zombie, bool[] targetNeighborCells)
		{
			if (targetNeighborCells == null)
				return false;
			var index = map.cellIndices.CellToIndex(zombie.Position);
			return index >= 0 && index < targetNeighborCells.Length && targetNeighborCells[index];
		}

		static bool ShouldPrioritizeZombie(TickManager tickManager, Zombie zombie, bool hasViewRect, CellRect viewRect)
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
			suicideBomberZombies.RemoveWhere(zombie => zombie == null || zombie.Spawned == false || zombie.Dead || zombie.Destroyed || zombie.Map != map || zombie.IsSuicideBomber == false);
			if (suicideBomberZombies.Count == 0)
				return;

			var realtime = Time.realtimeSinceStartup;
			var timeSpeed = Find.TickManager.CurTimeSpeed;
			var playSound = ZombieAwarenessCues.ShouldPlaySpecialZombieAmbientSound();
			foreach (var zombie in suicideBomberZombies)
			{
				UpdateSuicideBomberLight(zombie);
				UpdateSuicideBomberPiep(zombie, realtime, timeSpeed, playSound);
			}
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

		IEnumerator TickTasks()
		{
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
