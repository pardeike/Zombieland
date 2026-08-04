using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace ZombieLand
{
	[StaticConstructorOnStartup]
	public class ZombieSymbiant : Pawn
	{
		public const int MAX_METABALLS = 4000;
		static readonly Color color = new(0, 0.8f, 0);
		static readonly float elementPower = 1f;
		static readonly float elementRadius = 0.011f;
		static readonly float[] elementSizes = [2.5f, 2.4f, 1.6f, 1.2f, 1f, 0.9f, 0.9f, 1f, 1f];
		static readonly HashSet<ZombieSymbiant> renderResourceOwners = [];
		static readonly Dictionary<Map, ZombieSymbiant> activeSymbiantByMap = [];
		static readonly HashSet<Map> mapsWithoutActiveSymbiant = [];
		internal static string DebugPerfProfile { get; private set; } = "default";
		internal static bool DebugDisableRendering { get; private set; }
		internal static bool DebugDisableSymbiantTick { get; private set; }
		internal static bool DebugDisablePathCost { get; private set; }
		internal static bool DebugDisableCellStatEffects { get; private set; }
		internal static bool DebugDisableHostHediffSync { get; private set; }
		internal static bool DebugDisableSymbiosisBenefits { get; private set; }
		internal static int DebugMaxCellsOverride { get; private set; }
		[ThreadStatic]
		static int suppressSymbiantSkillBonusDepth;
		const int MetaballTextureMinSize = 256;
		const int MetaballTextureMaxSize = 1024;
		const float MetaballTexturePixelsPerCell = 6f;
		const float MetaballInfluenceRadiusCells = 4.5f;
		const float MetaballCellRadiusFactor = 0.45f;
		const float MetaballCellRadiusMin = 0.55f;
		const float MetaballCellRadiusMax = 0.95f;
		const float MetaballAlphaStart = 0.45f;
		const float MetaballAlphaFull = 1.20f;
		const float MetaballMaxAlpha = 0.40f;
		const float MetaballEdgeStart = 0.45f;
		const float MetaballEdgeFull = 1.80f;
		const float SymbiantOpacityMin = 0.42f;
		const float SymbiantOpacityMax = 0.76f;
		const float SymbiantNoiseScale = 2.00f;
		const float SymbiantWavePhaseSpeed = 0.45f;
		const float SymbiantWaveShadeStrength = 0.68f;
		const float SymbiantEdgeContrast = 0.95f;
		const float SymbiantNormalTicksPerSecond = 60f;
		const float SymbiantRenderAltitudeOffset = -0.25f;
		const int SymbiosisMetricRefreshInterval = 250;
		const float HostAuraMinimumFactor = 0.22f;
		const float FullBenefitRoomCoverage = 0.20f;
		const float ConstructedWallPreferenceThreshold = 0.50f;
		const float NaturalWallRoomScoreFactor = 1.00f;
		const float ConstructedWallRoomScoreFactor = 2.00f;
		static int UprootedRelocationGraceTicks => GenDate.TicksPerHour * 4;
		const float UprootedIntegratedCellThreshold = 0.01f;
		const int AutoHealIntervalTicks = GenDate.TicksPerDay / 4;
		const int AmbientMovementRecentCellCapacity = 16;
		const int SelectionCoreWanderDwellTicks = GenDate.TicksPerHour * 6;
		const int SelectionCoreTextureSize = 64;
		const float SelectionCoreVisualSize = 0.93f;
		const float SelectionCoreSubtlePulseScale = 0.035f;
		const float SelectionCoreDiscoveryPulseScale = 0.085f;
		const float SelectionCoreHoverPulseScale = 0.10f;
		const float SelectionCorePulseSeconds = 1.8f;
		const float SelectionCoreRotationDegreesPerSecond = 6f;
		const int AmbientMovementCandidateLimit = 12;
		const int AmbientMovementSourceLimit = 8;
		const float AmbientMovementMinBenefitFactor = 0.55f;
		const float AmbientMovementHighBenefitFactor = 0.85f;
		const int MaxConstructedWallBreachDepth = 4;
		const float AmbientMovementTargetBestScoreFraction = 0.80f;
		const float AmbientMovementTargetRandomMin = 0.85f;
		const float AmbientMovementTargetRandomMax = 1.15f;
		const float AmbientMovementSourceRandomMin = 0.85f;
		const float AmbientMovementSourceRandomMax = 1.15f;
		const float AmbientMovementIntegrationFloorFactor = 0.55f;
		const float AmbientMovementMaxIntegrationLoss = 1f;
		const float AmbientMovementCenterSlack = 2f;
		const float AmbientMovementHighBenefitCenterSlack = 5f;
		const float SymbiantCompactnessCardinalBonus = 20f;
		const float SymbiantCompactnessDiagonalBonus = 10f;
		const float SymbiantCompactnessBonusMax = 100f;
		const float SymbiantFurnitureCellPenalty = 80f;
		const float SymbiantRecentCellScoreAdjustment = 40f;
		const float SymbiantCellSlowMin = 0.10f;
		const float SymbiantCellSlowMax = 0.50f;
		const float SymbiantFastGrowthDifficultyLimit = 2.5f;
		const float SymbiantLowDifficultyGrowthSpeedFactor = 2f;
		const float SymbiantHighDifficultyGrowthSpeedFactor = 1.5f;
		internal const float SymbiantContaminationStepReduction = 0.05f;
		const int SeveranceExtractCostMin = 10;
		const int SeveranceExtractCostMax = 50;
		const int CellMotionDurationTicks = 60;
		const int SymbiantRetreatSpeedFactor = 4;
		const float SymbiantSharedDamageLeakMin = 0.08f;
		const int SymbiantSharedHealthRecoveryDelayTicks = GenDate.TicksPerHour;
		const int SymbiantSharedHealthRecoveryIntervalTicks = GenDate.TicksPerHour;
		const float SymbiantSharedHealthRecoveryMissingFraction = 0.05f;
		const float SymbiantHostMarkerSeverity = 0.001f;
		const int SymbiantNamedDamageEchoLimit = 7;
		const string SymbiantOtherDamageEchoKey = "other";
		static readonly int SymbiantOpacityMinId = Shader.PropertyToID("_SymbiantOpacityMin");
		static readonly int SymbiantOpacityMaxId = Shader.PropertyToID("_SymbiantOpacityMax");
		static readonly int SymbiantNoiseScaleId = Shader.PropertyToID("_SymbiantNoiseScale");
		static readonly int SymbiantWavePhaseSpeedId = Shader.PropertyToID("_SymbiantFlowSpeed");
		static readonly int SymbiantWaveShadeStrengthId = Shader.PropertyToID("_SymbiantWaveShadeStrength");
		static readonly int SymbiantEdgeContrastId = Shader.PropertyToID("_SymbiantEdgeContrast");
		static readonly int SymbiantNoiseTimeId = Shader.PropertyToID("_SymbiantNoiseTime");
		static readonly int MetaballBufferId = Shader.PropertyToID("_MetaballBuffer");
		static readonly int MetaballCountId = Shader.PropertyToID("_MetaballCount");
		static readonly int MetaballWorldSizeId = Shader.PropertyToID("_MetaballWorldSize");
		// static readonly float[] elementSizes = [1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f];

		enum HostBenefit
		{
			MoodFixed,
			NoFoodOrRest,
			SkillBonus,
			MoveSpeed,
			ZombieIgnore,
			AutoHeal,
			Manipulation
		}

		enum HostBondTermination
		{
			SymbiantRemoved,
			SharedHealthExhausted
		}

		static readonly HostBenefit[] hostBenefitPool =
		[
			HostBenefit.MoodFixed,
			HostBenefit.NoFoodOrRest,
			HostBenefit.SkillBonus,
			HostBenefit.MoveSpeed,
			HostBenefit.Manipulation,
			HostBenefit.ZombieIgnore,
			HostBenefit.AutoHeal
		];

		sealed class SpawnRoomCandidate
		{
			public Room room;
			public float score;
			public bool hasSpawnCell;
			public IntVec3 bestCell;
			public float bestCellScore;
			public RoomWallProfile wallProfile;
		}

		sealed class RoomWallProfile
		{
			public int constructedWalls;
			public int naturalWalls;

			public int TotalWalls => constructedWalls + naturalWalls;
			public float ConstructedRatio => TotalWalls == 0 ? 0f : constructedWalls / (float)TotalWalls;
			public bool MostlyConstructed => TotalWalls > 0 && ConstructedRatio >= ConstructedWallPreferenceThreshold;
			public float PreferenceFactor => TotalWalls == 0
				? 1f
				: (float)GenMath.LerpDoubleClamped(0f, 1f, NaturalWallRoomScoreFactor, ConstructedWallRoomScoreFactor, ConstructedRatio);
		}

		HashSet<IntVec3> cells = [];
		List<IntVec3> orderedCells = [];
		readonly Dictionary<IntVec3, float> metaballRadiusByCell = [];
		readonly List<MetaballRenderElement> metaballRenderElements = [];
		readonly Dictionary<IntVec3, CellMotion> incomingCellMotions = [];
		readonly Dictionary<IntVec3, float> cellMotionWeights = [];
		readonly Queue<IntVec3> recentMovementCells = new();
		List<CellMotion> cellMotions = [];
		RenderTexture metaballTexture;
		Material metaballMaskMaterial;
		ComputeBuffer metaballBuffer;
		MetaballBufferData[] metaballBufferData = [];
		int metaballBufferCapacity;

		Mesh mesh = null;
		Material metaballMaterial;
		Mesh selectionCoreMesh;
		Material selectionCoreMaterial;
		Texture2D selectionCoreTexture;

		float radius, power, centerX, centerZ, renderMinX, renderMinZ, renderWidth = 1f, renderHeight = 1f;
		Vector2 drawCullSize = Vector2.one;
		int nextExpansionTick;
		int nextMovementTick;
		int nextAutoHealTick;
		int nextBenefitCellThreshold;
		int benefitStepCells;
		int feedPausedUntilTick;
		int lastSymbiantTick = -1;
		int lastRecessionPulseCells;
		int relocationCellDebt;
		int nextRelocationPulseTick;
		int uprootedSinceTick = -1;
		bool cancelNextBreach;
		Pawn host;
		string hostThingId;
		bool safeSeveranceInProgress;
		bool destructionInProgress;
		bool temporaryDespawnInProgress;
		bool symbiosisSevered;
		bool hostCollapseInProgress;
		bool uncontrolledDestroyHandled;
		bool sharedHealthFailureInProgress;
		bool hostBondStateInitialized;
		bool hostBondWasActive;
		float sharedHealth = -1f;
		int lastSharedHealthDamageTick = int.MinValue;
		int nextSharedHealthRecoveryTick;
		// Transient cadence guards; the serialized recovery deadline remains authoritative.
		int nextSharedHealthIdleCheckTick;
		int nextHostResolveAttemptTick;
		int lastSymbiosisMetricTick = int.MinValue;
		int lastRejectedDamageMessageTick = int.MinValue;
		int lastSharedDamageAbsorbMoteTick = int.MinValue;
		int cachedEligibleColonyRoomCells;
		int cachedFullBenefitCells = 20;
		float cachedIntegratedVisibleCells;
		float cachedBenefitFactor;
		CellRect relativeCellBounds;
		bool hasCellBounds;
		List<HostBenefit> hostBenefits = [];
		List<SymbiantDamageEchoRecord> damageEchoHistory = [];
		int lastCellMotionRenderTick = -1;
		bool renderGeometryDirty = true;
		bool metaballTextureDirty = true;
		bool destroyWhenCellMotionsFinish;
		int combatShapeVersion;
		IntVec3 selectionCoreRelative = IntVec3.Invalid;
		IntVec3 selectionCoreMotionFrom = IntVec3.Invalid;
		IntVec3 selectionCoreMotionTo = IntVec3.Invalid;
		int selectionCoreMotionStartTick = -1;
		int selectionCoreMotionEndTick = -1;
		int selectionCoreLastMoveTick = -1;
		bool selectionCoreDiscoveryCue;
		float selectionCoreHoverBlend;
		float selectionCoreHoverVelocity;
		float selectionCoreSelectedBlend;
		float selectionCoreSelectedVelocity;
		float selectionCoreDiscoveryBlend;
		float selectionCoreDiscoveryVelocity;
		float selectionCoreInteractionLastRealtime = -1f;
		bool debugForceMetaballFallback;
		bool lastFallbackSelectionCoreDrawSucceeded;

		public int CellCount => cells?.Count ?? 0;
		internal int CombatShapeVersion => combatShapeVersion;
		internal bool DebugCellsAreConnected => cells == null || cells.Count <= 1 || CellsAreConnectedToRoot(cells);
		public int NextExpansionTick => nextExpansionTick;
		public int CurrentExpansionIntervalTicks => AutomaticExpansionIntervalTicks();
		public int CurrentRetreatIntervalTicks => RetreatIntervalTicks();
		public static int RetreatSpeedFactor => SymbiantRetreatSpeedFactor;
		public int FeedPausedUntilTick => feedPausedUntilTick;
		public int LastRecessionPulseCells => lastRecessionPulseCells;
		public int RelocationCellDebt => relocationCellDebt;
		public int NextRelocationPulseTick => nextRelocationPulseTick;
		public int UprootedSinceTick => uprootedSinceTick;
		internal int RecentMovementCellCount => recentMovementCells.Count;
		internal static int RecentMovementCellCapacity => AmbientMovementRecentCellCapacity;
		public bool CancelNextBreach => cancelNextBreach;
		public static float CurrentGrowthSpeedFactor => SymbiantGrowthSpeedFactor();
		public IEnumerable<IntVec3> AbsoluteCells => orderedCells.Select(cell => Position + cell);
		CellRect AbsoluteCellBounds => relativeCellBounds.MovedBy(Position);
		public override Vector2 DrawSize => hasCellBounds ? drawCullSize : base.DrawSize;
		public IntVec3 SelectionCoreCell
		{
			get
			{
				EnsureSelectionCoreState();
				return SelectionCoreClickRelative.IsValid ? Position + SelectionCoreClickRelative : Position;
			}
		}
		public bool SelectionCoreValid
		{
			get
			{
				EnsureSelectionCoreState();
				return selectionCoreRelative.IsValid && cells?.Contains(selectionCoreRelative) == true;
			}
		}
		public bool SelectionCoreMotionActive => IsSelectionCoreMotionActive(GenTicks.TicksGame);
		public int SelectionCoreMotionEndTick => selectionCoreMotionEndTick;
		public int SelectionCoreLastMoveTick => selectionCoreLastMoveTick;
		public bool SelectionCoreDiscoveryCue => selectionCoreDiscoveryCue;
		internal float SelectionCoreHoverBlend => selectionCoreHoverBlend;
		internal float SelectionCoreSelectedBlend => selectionCoreSelectedBlend;
		internal float SelectionCoreDiscoveryBlend => selectionCoreDiscoveryBlend;
		internal Vector2 SelectionCoreVisualCenterRelative
		{
			get
			{
				EnsureSelectionCoreState();
				return SelectionCoreVisualCenter;
			}
		}
		public override CellRect? CustomRectForSelector => hasCellBounds ? CellRect.SingleCell(SelectionCoreCell) : base.CustomRectForSelector;
		public int RenderTextureWidth => metaballTexture?.width ?? 0;
		public int RenderTextureHeight => metaballTexture?.height ?? 0;
		public Vector2 RenderWorldSize => new(renderWidth, renderHeight);
		public string RenderShaderName => metaballMaterial?.shader?.name;
		public bool RenderUsesSymbiantShader => Assets.ZombieSymbiantShader != null && metaballMaterial?.shader == Assets.ZombieSymbiantShader;
		public bool RenderUsesGpuMetaballMask => Assets.MetaballShader != null && metaballMaskMaterial?.shader == Assets.MetaballShader && metaballTexture != null;
		public int RenderMetaballElementCount => metaballRenderElements?.Count ?? 0;
		public int ActiveCellMotionCount => CountActiveCellMotions();
		public bool RegisteredInMapPawnLists => (MapHeld?.mapPawns?.AllPawnsSpawned?.Contains(this) ?? false);
		public static float RenderOpacityMin => SymbiantOpacityMin;
		public static float RenderOpacityMax => SymbiantOpacityMax;
		public static float RenderNoiseScale => SymbiantNoiseScale;
		public static float RenderWavePhaseSpeed => SymbiantWavePhaseSpeed;
		public static float RenderWaveShadeStrength => SymbiantWaveShadeStrength;
		public static float RenderEdgeContrast => SymbiantEdgeContrast;
		public static float RenderNoiseTimeSeconds => GenTicks.TicksGame / SymbiantNormalTicksPerSecond;
		public static int MaxCells => Mathf.Clamp(DebugMaxCellsOverride > 0 ? DebugMaxCellsOverride : (ZombieSettings.Values?.symbiantMaxCells ?? 400), 1, MAX_METABALLS);
		Map SymbiantMap => Spawned ? Map : host?.MapHeld ?? MapHeld;
		public Pawn LinkedHost => ResolveHost();
		public string HostThingId => hostThingId;
		public int DamageAbsorptionBuffer => Mathf.RoundToInt(SharedHealthCurrent);
		public int DamageAbsorptionBufferMax => Mathf.RoundToInt(SharedHealthMax);
		public float SharedHealthFraction => SharedHealthPercent;
		public int LastSharedHealthDamageTick => lastSharedHealthDamageTick;
		public int NextSharedHealthRecoveryTick => nextSharedHealthRecoveryTick;
		public static int SharedHealthRecoveryDelayTicks => SymbiantSharedHealthRecoveryDelayTicks;
		public static int SharedHealthRecoveryIntervalTicks => SymbiantSharedHealthRecoveryIntervalTicks;
		public static float SharedHealthRecoveryMissingFraction => SymbiantSharedHealthRecoveryMissingFraction;
		public IReadOnlyList<SymbiantDamageEchoRecord> DamageEchoHistory => damageEchoHistory;
		public float DamageEchoHistoryTotal => damageEchoHistory?.Sum(record => Mathf.Max(0f, record?.amount ?? 0f)) ?? 0f;
		public float HealthScaleCellMultiplier => HealthScaleMultiplierForCells(CellCount);
		public int SharedHealthCurrentDisplay => DamageAbsorptionBuffer;
		public int SharedHealthMaxDisplay => DamageAbsorptionBufferMax;
		public int SharedDamageLeakPercentDisplay => Mathf.RoundToInt(SharedDamageLeakFactor * 100f);
		public int SharedDamageAbsorbPercentDisplay => Mathf.Clamp(100 - SharedDamageLeakPercentDisplay, 0, 100);
		public string SharedHealthSummary => "SymbiantSharedHealthSummary".Translate(ColoredSharedHealthPercent(), FormatSharedHealthCapacity(SharedHealthMaxDisplay)).ToString();
		public int EligibleColonyRoomCells { get { RefreshSymbiosisMetrics(); return cachedEligibleColonyRoomCells; } }
		public int FullBenefitCells { get { RefreshSymbiosisMetrics(); return cachedFullBenefitCells; } }
		public float IntegratedVisibleCells { get { RefreshSymbiosisMetrics(); return cachedIntegratedVisibleCells; } }
		public float BenefitFactor { get { RefreshSymbiosisMetrics(); return cachedBenefitFactor; } }
		public string GrowthState
		{
			get
			{
				if (Spawned == false || Destroyed || Dead)
					return "inactive";
				RefreshSymbiosisMetrics();
				var linkedHost = ResolveHost();
				if (linkedHost != null && cachedIntegratedVisibleCells <= UprootedIntegratedCellThreshold)
				{
					if (TryFindReseedPlan(linkedHost, out _, out _))
						return "uprooted";
					return HasReseedCandidateRoom(linkedHost) ? "contained" : "dormantNoRoom";
				}
				if (relocationCellDebt > 0 || HasMovableUnintegratedCells())
					return FindExpansionTarget(false) == null ? "contained" : "relocating";
				if (CellCount >= MaxCells)
					return "capped";
				var ticks = GenTicks.TicksGame;
				if (ticks < feedPausedUntilTick)
					return "pausedAfterFeeding";
				if (ticks < nextExpansionTick)
					return "waiting";
				return FindExpansionTarget(false) == null ? "contained" : "growing";
			}
		}
		public bool CanSafelySever => symbiosisSevered == false && IsActiveBondWith(LinkedHost);
		public static float HostHediffSeverity(float _) => SymbiantHostMarkerSeverity;
		public int NextBenefitCellSize
		{
			get
			{
				EnsureBenefitDefaults();
				return nextBenefitCellThreshold;
			}
		}
		public int HostBenefitCount => hostBenefits?.Count ?? 0;
		public bool SymbiosisSevered => symbiosisSevered;
		public int SharedHealthPercentDisplay => Mathf.RoundToInt(SharedHealthPercent * 100f);
		public string EffectSummary
		{
			get
			{
				var labels = new[]
				{
					"SymbiantEffectCells".Translate(CellCount, MaxCells).ToString(),
					MovementSlowdownDescription,
					WorkSlowdownDescription
				};
				return string.Join("\n", labels.Select(label => "- " + label));
			}
		}

		string DownsideSummary
		{
			get
			{
				var labels = new[] { MovementSlowdownDescription, WorkSlowdownDescription };
				return string.Join("\n", labels.Select(label => "- " + label));
			}
		}

		string MovementSlowdownDescription => "SymbiantEffectPathCost".Translate(SymbiantCellSlowPercent()).ToString();
		string WorkSlowdownDescription => "SymbiantEffectWorkSpeed".Translate(SymbiantCellSlowPercent()).ToString();

		public string BenefitSummary
		{
			get
			{
				EnsureBenefitDefaults();
				var labels = new List<string> { "SymbiantBenefitZombieInfectionImmunity".Translate().ToString() };
				if (hostBenefits.Count > 0)
					labels.AddRange(hostBenefits.Select(benefit => BenefitLabel(benefit).ToString()));
				return string.Join("\n", labels.Select(label => "- " + label));
			}
		}

		float SharedHealthPercent
		{
			get
			{
				var max = SharedHealthMax;
				if (max <= 0f)
					return 0f;
				return Mathf.Clamp01(SharedHealthCurrent / max);
			}
		}

		float SharedHealthCurrent
		{
			get
			{
				if (Dead || Destroyed)
					return 0f;
				EnsureSharedHealth();
				return sharedHealth;
			}
		}

		float SharedHealthMax
		{
			get
			{
				var core = RaceProps?.body?.corePart;
				if (core == null)
					return 0f;
				var lifeStageScale = ageTracker?.CurLifeStage?.healthScaleFactor ?? 1f;
				var raceScale = RaceProps?.baseHealthScale ?? 1f;
				return Mathf.CeilToInt(core.def.hitPoints * lifeStageScale * raceScale * HealthScaleCellMultiplier);
			}
		}

		void EnsureSharedHealth()
		{
			var max = SharedHealthMax;
			if (max <= 0f)
			{
				sharedHealth = 0f;
				return;
			}
			if (sharedHealth < 0f || float.IsNaN(sharedHealth) || float.IsInfinity(sharedHealth))
				sharedHealth = max;
			else if (sharedHealth > max)
				sharedHealth = max;
		}

		float SharedDamageLeakFactor => Mathf.Clamp(1f / Mathf.Max(1f, HealthScaleCellMultiplier), SymbiantSharedDamageLeakMin, 1f);

		static string FormatSharedHealthCapacity(int amount)
		{
			if (amount >= 1_000_000)
			{
				var millions = amount / 1_000_000f;
				return millions >= 10f ? Mathf.RoundToInt(millions) + "m" : millions.ToString("0.#") + "m";
			}
			if (amount >= 10_000)
				return Mathf.RoundToInt(amount / 1000f) + "k";
			return amount.ToString();
		}

		string ColoredSharedHealthPercent()
		{
			var percent = SharedHealthPercentDisplay;
			var color = percent >= 75 ? "#72d672" : percent >= 35 ? "#ffb35c" : percent > 0 ? "#ff6b5f" : "#ff4a4a";
			return "<color=" + color + ">" + percent + "%</color>";
		}

		float SharedDamageLeakAmount(float amount)
		{
			return Mathf.Max(0f, amount) * SharedDamageLeakFactor;
		}

		float DrainSharedHealth(float amount)
		{
			EnsureSharedHealth();
			if (amount <= 0f || sharedHealth <= 0f)
				return 0f;
			var drained = Mathf.Min(sharedHealth, amount);
			sharedHealth = Mathf.Max(0f, sharedHealth - drained);
			lastSharedHealthDamageTick = GenTicks.TicksGame;
			nextSharedHealthRecoveryTick = lastSharedHealthDamageTick + SymbiantSharedHealthRecoveryDelayTicks;
			if (sharedHealth <= 0.01f)
				CollapseFromSharedHealthFailure();
			return drained;
		}

		void TryRecoverSharedHealth(int ticks)
		{
			if (symbiosisSevered || Destroyed || Dead)
				return;
			if (nextSharedHealthRecoveryTick > 0 && ticks < nextSharedHealthRecoveryTick)
				return;
			if (nextSharedHealthRecoveryTick <= 0 && sharedHealth >= 0f && ticks < nextSharedHealthIdleCheckTick)
				return;

			EnsureSharedHealth();
			var max = SharedHealthMax;
			var missing = max - sharedHealth;
			if (missing <= 0.01f)
			{
				sharedHealth = max;
				nextSharedHealthRecoveryTick = 0;
				nextSharedHealthIdleCheckTick = ticks + SymbiantSharedHealthRecoveryIntervalTicks;
				return;
			}
			if (nextSharedHealthRecoveryTick <= 0)
			{
				nextSharedHealthRecoveryTick = ticks + SymbiantSharedHealthRecoveryDelayTicks;
				return;
			}
			if (host == null && ticks < nextHostResolveAttemptTick)
				return;
			if (ResolveHost() == null)
			{
				nextHostResolveAttemptTick = ticks + SymbiosisMetricRefreshInterval;
				return;
			}
			nextHostResolveAttemptTick = 0;
			var recovered = Mathf.Min(missing, Mathf.Max(1f, missing * SymbiantSharedHealthRecoveryMissingFraction));
			sharedHealth = Mathf.Min(max, sharedHealth + recovered);
			nextSharedHealthRecoveryTick = sharedHealth >= max - 0.01f
				? 0
				: ticks + SymbiantSharedHealthRecoveryIntervalTicks;
			if (nextSharedHealthRecoveryTick == 0)
				nextSharedHealthIdleCheckTick = ticks + SymbiantSharedHealthRecoveryIntervalTicks;
		}

		public static float HealthScaleMultiplierForCells(int cellCount)
		{
			return Mathf.Sqrt(Mathf.Max(1, cellCount));
		}

		internal static float NaturalSpawnPressure(Map map, bool ignoreActive = false)
		{
			if (map == null || ZombieSettings.Values?.symbiantEnabled != true)
				return 0f;
			if (ignoreActive == false && ActiveSymbiant(map) != null)
				return 0f;

			var hostCount = EligibleHosts(map, null).Count();
			if (hostCount == 0)
				return 0f;

			var candidates = SpawnRoomCandidates(map)
				.Where(candidate => candidate.score > 0f && candidate.hasSpawnCell)
				.ToArray();
			if (candidates.Length == 0)
				return 0f;

			var eligibleCells = candidates.Sum(candidate => candidate.room.CellCount);
			if (eligibleCells < MinimumNaturalSpawnEligibleCells())
				return 0f;

			var bestRoomScore = PreferredSpawnRoomCandidates(candidates).Select(candidate => candidate.score).DefaultIfEmpty(0f).Max();
			var footprintPressure = GenMath.LerpDoubleClamped(20f, 260f, 0.35f, 1.15f, eligibleCells);
			var hostPressure = GenMath.LerpDoubleClamped(1f, 8f, 0.65f, 1.15f, hostCount);
			var usePressure = GenMath.LerpDoubleClamped(80f, 900f, 0.55f, 1.15f, bestRoomScore);
			return Mathf.Clamp(footprintPressure * hostPressure * usePressure, 0.15f, 1.6f);
		}

		internal static object SetDebugPerfProfile(string profile)
		{
			var normalized = (profile ?? "default").Trim().ToLowerInvariant();
			DebugDisableRendering = false;
			DebugDisableSymbiantTick = false;
			DebugDisablePathCost = false;
			DebugDisableCellStatEffects = false;
			DebugDisableHostHediffSync = false;
			DebugDisableSymbiosisBenefits = false;

			switch (normalized)
			{
				case "":
				case "default":
				case "all":
					normalized = "default";
					break;
				case "inert":
					DebugDisableRendering = true;
					DebugDisableSymbiantTick = true;
					DebugDisablePathCost = true;
					DebugDisableCellStatEffects = true;
					DebugDisableHostHediffSync = true;
					DebugDisableSymbiosisBenefits = true;
					break;
				case "renderonly":
				case "render-only":
					normalized = "renderOnly";
					DebugDisableSymbiantTick = true;
					DebugDisablePathCost = true;
					DebugDisableCellStatEffects = true;
					DebugDisableHostHediffSync = true;
					DebugDisableSymbiosisBenefits = true;
					break;
				case "pathonly":
				case "path-only":
					normalized = "pathOnly";
					DebugDisableRendering = true;
					DebugDisableSymbiantTick = true;
					DebugDisableCellStatEffects = true;
					DebugDisableHostHediffSync = true;
					DebugDisableSymbiosisBenefits = true;
					break;
				case "symbiosisonly":
				case "symbiosis-only":
					normalized = "symbiosisOnly";
					DebugDisableRendering = true;
					DebugDisablePathCost = true;
					DebugDisableCellStatEffects = true;
					break;
				case "norender":
				case "no-render":
					normalized = "noRender";
					DebugDisableRendering = true;
					break;
				case "nopath":
				case "no-path":
					normalized = "noPath";
					DebugDisablePathCost = true;
					break;
				case "nocellstats":
				case "no-cell-stats":
					normalized = "noCellStats";
					DebugDisableCellStatEffects = true;
					break;
				case "notick":
				case "no-tick":
					normalized = "noTick";
					DebugDisableSymbiantTick = true;
					DebugDisableHostHediffSync = true;
					DebugDisableSymbiosisBenefits = true;
					break;
				default:
					normalized = "default";
					break;
			}

			DebugPerfProfile = normalized;
			foreach (var symbiant in ActiveSymbiants())
				NotifyHostCapacityBenefitsChanged(symbiant.LinkedHost);
			return DebugPerfState();
		}

		internal static object DebugPerfState()
		{
			return new
			{
				profile = DebugPerfProfile,
				rendering = DebugDisableRendering == false,
				symbiantTick = DebugDisableSymbiantTick == false,
				pathCost = DebugDisablePathCost == false,
				cellStatEffects = DebugDisableCellStatEffects == false,
				hostHediffSync = DebugDisableHostHediffSync == false,
				symbiosisBenefits = DebugDisableSymbiosisBenefits == false,
				maxCellsOverride = DebugMaxCellsOverride,
				effectiveMaxCells = MaxCells,
				technicalMaxCells = MAX_METABALLS
			};
		}

		internal static object SetDebugMaxCellsOverride(int maxCells)
		{
			DebugMaxCellsOverride = Mathf.Clamp(maxCells, 0, MAX_METABALLS);
			return DebugPerfState();
		}

		public static void Spawn(Map map, IntVec3 cell)
		{
			if (map == null || cell.InBounds(map) == false || ActiveSymbiant(map) != null)
				return;
			var symbiant = PawnGenerator.GeneratePawn(ZombieDefOf.ZombieSymbiant, null) as ZombieSymbiant;
			symbiant.Position = cell;
			symbiant.AddRelativeCell(IntVec3.Zero);
			symbiant.ResetExpansionClock();
			symbiant.UpdateAll();

			symbiant.SetFactionDirect(Find.FactionManager.FirstFactionOfDef(ZombieDefOf.Zombies));
			GenSpawn.Spawn(symbiant, cell, map, Rot4.Random, WipeMode.Vanish, false);
			RegisterActiveSymbiant(symbiant, map);

			symbiant.jobs.StartJob(JobMaker.MakeJob(CustomDefs.Symbiant));
			_ = symbiant.TryAssignRandomHost();
			symbiant.UpdateSymbiosisState();

			var sentLetter = false;
			var linkedHost = symbiant.LinkedHost;
			if (ZombieAwarenessCues.ShouldShowZombieEventLetter())
			{
				var roomLabel = SpawnRoomLabel(map, cell);
				var headline = linkedHost == null ? "LetterLabelZombieSymbiantNoHost".Translate() : "LetterLabelZombieSymbiant".Translate(linkedHost.LabelShortCap);
				var text = linkedHost == null ? "ZombieSymbiantNoHost".Translate(roomLabel) : "ZombieSymbiant".Translate(roomLabel, linkedHost.LabelShortCap);
				text += "\n\n" + "ZombieSymbiantCoreInstruction".Translate();
				symbiant.NotifySelectionCoreDiscoveryCue();
				Find.LetterStack.ReceiveLetter(headline, text, CustomDefs.SymbiantConnection ?? LetterDefOf.NeutralEvent, SpawnLookTargets(symbiant, linkedHost, map, cell));
				sentLetter = true;
			}

			if (sentLetter == false && ZombieAwarenessCues.ShouldPlaySpecialZombieAmbientSound())
				CustomDefs.SymbiantConnected?.PlayOneShotOnCamera(null);
		}

		internal static ZombieSymbiant DebugSpawnForRendering(Map map, IntVec3 root, IEnumerable<IntVec3> absoluteCells)
		{
			if (map == null || root.InBounds(map) == false || ActiveSymbiant(map) != null)
				return null;
			var cells = absoluteCells?
				.Where(cell => cell.InBounds(map))
				.Distinct()
				.Take(MaxCells)
				.ToList() ?? [];
			if (cells.Contains(root) == false)
				cells.Insert(0, root);

			var symbiant = PawnGenerator.GeneratePawn(ZombieDefOf.ZombieSymbiant, null) as ZombieSymbiant;
			symbiant.Position = root;
			symbiant.SetFactionDirect(Find.FactionManager.FirstFactionOfDef(ZombieDefOf.Zombies));
			foreach (var cell in cells)
				symbiant.AddRelativeCell(cell - root);
			symbiant.ResetExpansionClock();
			symbiant.UpdateAll();

			GenSpawn.Spawn(symbiant, root, map, Rot4.Random, WipeMode.Vanish, false);
			RegisterActiveSymbiant(symbiant, map);
			symbiant.EnsureVisibleToPawnSystems(map);
			symbiant.jobs.StartJob(JobMaker.MakeJob(CustomDefs.Symbiant));
			symbiant.UpdateSymbiosisState();
			return symbiant;
		}

		static TaggedString SpawnRoomLabel(Map map, IntVec3 cell)
		{
			var role = cell.GetRoom(map)?.Role;
			return role == null || role.defName == "None" || role.label.NullOrEmpty() ? "ZombieSymbiantUnknownRoom".Translate() : role.LabelCap;
		}

		static LookTargets SpawnLookTargets(ZombieSymbiant symbiant, Pawn linkedHost, Map map, IntVec3 cell)
		{
			var targets = new List<GlobalTargetInfo> { new(cell, map) };
			if (linkedHost != null && linkedHost.Destroyed == false)
				targets.Add(new GlobalTargetInfo(linkedHost));
			return new LookTargets(targets);
		}

		public static bool TrySpawnInBestRoom(Map map, bool requireNaturalPressure = true)
		{
			if (map == null || ZombieSettings.Values.symbiantEnabled == false)
				return false;
			if (ActiveSymbiant(map) != null)
				return false;
			if (EligibleHosts(map, null).Any() == false)
				return false;
			if (requireNaturalPressure && NaturalSpawnPressure(map) <= 0f)
				return false;

			var room = BestSpawnRoom(map);
			if (room == null)
				return false;

			if (TryFindBestSpawnCell(map, room, out var cell, out _) == false)
				return false;

			Spawn(map, cell);
			return true;
		}

		internal static bool CanNaturalSpawnNow(Map map)
		{
			return map != null
				&& ZombieSettings.Values.symbiantEnabled
				&& ActiveSymbiant(map) == null
				&& EligibleHosts(map, null).Any()
				&& BestSpawnRoom(map) != null
				&& NaturalSpawnPressure(map) > 0f;
		}

		internal static object DebugNaturalSpawnPlan(Map map, int limit = 8)
		{
			if (map == null)
				return new { success = false, error = "No current map is loaded." };

			var active = ActiveSymbiant(map);
			var hosts = EligibleHosts(map, null).ToArray();
			var candidates = SpawnRoomCandidates(map).ToArray();
			var eligibleRoomCells = candidates.Sum(candidate => candidate.room.CellCount);
			var naturalSpawnPressure = NaturalSpawnPressure(map);
			var scoredRooms = candidates
				.Where(candidate => candidate.score > 0f && candidate.hasSpawnCell)
				.Select(candidate => new
				{
					role = candidate.room.Role?.defName,
					roleLabel = candidate.room.Role?.LabelCap.ToString(),
					cellCount = candidate.room.CellCount,
					extents = DescribeDebugCellRect(candidate.room.ExtentsClose),
					score = candidate.score,
					bestCell = DescribeDebugCell(candidate.bestCell),
					bestCellScore = candidate.bestCellScore,
					valuableThingCount = candidate.room.ContainedAndAdjacentThings.Count(thing => ScoreRoomThing(thing) > 0f),
					wallProfile = DescribeRoomWallProfile(candidate.wallProfile),
					preferredByConstructedWalls = candidate.wallProfile.MostlyConstructed
				})
				.OrderByDescending(room => room.preferredByConstructedWalls)
				.ThenByDescending(room => room.score)
				.ToArray();
			var rooms = scoredRooms
				.Take(Mathf.Max(1, limit))
				.ToArray();

			return new
			{
				success = true,
				enabled = ZombieSettings.Values.symbiantEnabled,
				activeSymbiant = active?.ThingID,
				eligibleHostCount = hosts.Length,
				eligibleRoomCells,
				minimumNaturalSpawnEligibleCells = MinimumNaturalSpawnEligibleCells(),
				naturalSpawnPressure,
				eligibleHosts = hosts.Take(16).Select(host => new
				{
					id = host.ThingID,
					label = host.LabelShortCap,
					cell = host.Spawned ? DescribeDebugCell(host.Position) : null
				}).ToArray(),
				candidateRoomCount = scoredRooms.Length,
				preferredConstructedRoomCount = scoredRooms.Count(room => room.preferredByConstructedWalls),
				returnedRoomCount = rooms.Length,
				canSpawnNow = ZombieSettings.Values.symbiantEnabled && active == null && hosts.Length > 0 && scoredRooms.Length > 0 && naturalSpawnPressure > 0f,
				bestRoom = rooms.FirstOrDefault(),
				rooms
			};
		}

		static Room BestSpawnRoom(Map map)
		{
			var candidates = SpawnRoomCandidates(map)
				.Where(candidate => candidate.score > 0f && candidate.hasSpawnCell)
				.ToArray();
			return PreferredSpawnRoomCandidates(candidates)
				.OrderByDescending(entry => entry.score)
				.FirstOrDefault()?.room;
		}

		static IEnumerable<SpawnRoomCandidate> PreferredSpawnRoomCandidates(SpawnRoomCandidate[] candidates)
		{
			var preferred = candidates.Where(candidate => candidate.wallProfile.MostlyConstructed).ToArray();
			return preferred.Length > 0 ? preferred : candidates;
		}

		static IEnumerable<SpawnRoomCandidate> SpawnRoomCandidates(Map map)
		{
			return CandidateRooms(map)
				.Select(room =>
				{
					var wallProfile = RoomWallProfileFor(map, room);
					var hasSpawnCell = TryFindBestSpawnCell(map, room, out var bestCell, out var bestCellScore);
					return new SpawnRoomCandidate
					{
						room = room,
						score = ScoreSpawnRoom(map, room, wallProfile),
						hasSpawnCell = hasSpawnCell,
						bestCell = bestCell,
						bestCellScore = bestCellScore,
						wallProfile = wallProfile
					};
				});
		}

		static float ScoreSpawnRoom(Map map, Room room)
			=> ScoreSpawnRoom(map, room, RoomWallProfileFor(map, room));

		static float ScoreSpawnRoom(Map map, Room room, RoomWallProfile wallProfile)
		{
			if (map == null || room == null)
				return 0f;
			var traffic = room.Cells.Take(240).Sum(cell => ScoreTraffic(map, cell));
			if (traffic > 0f)
				return traffic * (wallProfile?.PreferenceFactor ?? 1f);
			return room.Cells.Take(240).Sum(cell => ScoreColonyCenterFallback(map, cell)) * (wallProfile?.PreferenceFactor ?? 1f);
		}

		static bool TryFindBestSpawnCell(Map map, Room room, out IntVec3 cell, out float score)
		{
			cell = IntVec3.Invalid;
			score = 0f;
			if (map == null || room == null)
				return false;

			var best = room.Cells
				.Where(candidate => CanOccupyInitialSpawnCell(map, candidate))
				.Select(candidate => new { cell = candidate, score = ScoreTraffic(map, candidate), fallback = ScoreColonyCenterFallback(map, candidate) })
				.Where(candidate => candidate.score > 0f)
				.OrderByDescending(candidate => candidate.score)
				.FirstOrDefault();
			best ??= room.Cells
				.Where(candidate => CanOccupyInitialSpawnCell(map, candidate))
				.Select(candidate => new { cell = candidate, score = ScoreColonyCenterFallback(map, candidate), fallback = 0f })
				.OrderByDescending(candidate => candidate.score)
				.FirstOrDefault();
			if (best == null)
				return false;

			cell = best.cell;
			score = best.score;
			return true;
		}

		static bool CanOccupyInitialSpawnCell(Map map, IntVec3 cell)
		{
			return CanOccupyOpenCell(map, cell)
				&& cell.GetEdifice(map) == null
				&& cell.GetThingList(map).Any(thing => thing is Pawn || thing.def.category == ThingCategory.Building) == false;
		}

		static IEnumerable<Room> CandidateRooms(Map map)
		{
			if (map?.regionGrid?.allRooms == null)
				return Enumerable.Empty<Room>();
			return map.regionGrid.allRooms.Where(room =>
				IsEligibleIndoorRoom(room)
				&& RoomHasColonyUseSignal(map, room));
		}

		static bool RoomHasHomeAreaCell(Area home, Room room)
		{
			return home != null && room != null && room.Cells.Any(cell => home[cell]);
		}

		static bool RoomHasColonyUseSignal(Map map, Room room)
		{
			if (map == null || room == null)
				return false;
			if (RoomHasHomeAreaCell(map.areaManager?.Home, room))
				return true;
			if (room.ContainedAndAdjacentThings.Any(thing => ScoreRoomThing(thing) > 0f))
				return true;
			return room.Cells.Take(120).Any(cell => ScoreTraffic(map, cell) > 0f);
		}

		static RoomWallProfile RoomWallProfileFor(Map map, Room room)
		{
			var profile = new RoomWallProfile();
			if (map == null || room == null)
				return profile;

			var counted = new HashSet<IntVec3>();
			foreach (var cell in room.Cells)
			{
				for (var i = 0; i < GenAdj.CardinalDirections.Length; i++)
				{
					var adjacent = cell + GenAdj.CardinalDirections[i];
					if (adjacent.InBounds(map) == false || counted.Add(adjacent) == false)
						continue;
					if (adjacent.GetRoom(map) == room)
						continue;

					var edifice = adjacent.GetEdifice(map);
					if (edifice == null)
						continue;
					if (IsNaturalBoundaryWall(edifice))
						profile.naturalWalls++;
					else if (IsConstructedBoundaryWall(edifice))
						profile.constructedWalls++;
				}
			}
			return profile;
		}

		static bool IsNaturalBoundaryWall(Building edifice)
			=> edifice is Mineable || edifice.def?.building?.isNaturalRock == true || edifice.def?.mineable == true;

		static bool IsConstructedBoundaryWall(Building edifice)
			=> edifice is Building_Door
				|| (edifice?.def?.IsWall == true && edifice.def.useHitPoints && IsNaturalBoundaryWall(edifice) == false);

		static object DescribeRoomWallProfile(RoomWallProfile profile)
		{
			profile ??= new RoomWallProfile();
			return new
			{
				constructedWalls = profile.constructedWalls,
				naturalWalls = profile.naturalWalls,
				totalWalls = profile.TotalWalls,
				constructedRatio = profile.ConstructedRatio,
				mostlyConstructed = profile.MostlyConstructed,
				preferenceFactor = profile.PreferenceFactor
			};
		}

		static object DescribeDebugCell(IntVec3 cell)
		{
			return cell.IsValid ? new { x = cell.x, z = cell.z } : null;
		}

		static object DescribeDebugCellRect(CellRect rect)
		{
			return new { rect.minX, rect.maxX, rect.minZ, rect.maxZ };
		}

		static bool CanBeLinkedHostIdentityFast(Pawn pawn, bool allowDead = false)
		{
			if (pawn == null || pawn.Destroyed)
				return false;
			if (allowDead == false && pawn.Dead)
				return false;
			if (pawn is Zombie || pawn is ZombieSymbiant || pawn is ZombieSpitter)
				return false;
			if (pawn.RaceProps?.Humanlike != true || pawn.RaceProps.IsFlesh == false)
				return false;
			return true;
		}

		static bool CanEverBeLinkedHostFast(Pawn pawn, bool allowDead = false)
		{
			if (CanBeLinkedHostIdentityFast(pawn, allowDead) == false)
				return false;
			if (allowDead == false && (pawn.Spawned == false || pawn.Map == null))
				return false;
			if (pawn.Faction?.IsPlayer != true || pawn.IsColonistPlayerControlled == false || pawn.IsPrisoner)
				return false;
			if (pawn.IsSlave || pawn.HostFaction != null || pawn.IsQuestLodger())
				return false;
			if (pawn.DevelopmentalStage == DevelopmentalStage.Newborn || pawn.DevelopmentalStage == DevelopmentalStage.Baby || pawn.DevelopmentalStage == DevelopmentalStage.Child)
				return false;
			return true;
		}

		static bool CanBeAffectedBySymbiantCellCandidateFast(Pawn pawn)
		{
			return pawn != null
				&& pawn.Destroyed == false
				&& pawn.Dead == false
				&& pawn.Spawned
				&& pawn.Map != null
				&& pawn is not Zombie
				&& pawn is not ZombieSymbiant
				&& pawn is not ZombieSpitter
				&& pawn.RaceProps?.Humanlike == true
				&& pawn.Faction?.IsPlayer == true
				&& pawn.IsColonistPlayerControlled;
		}

		static bool CanBeSlowedBySymbiantCellCandidateFast(Pawn pawn)
		{
			return pawn != null
				&& pawn.Destroyed == false
				&& pawn.Dead == false
				&& pawn.Spawned
				&& pawn.Map != null
				&& pawn.Flying == false
				&& pawn.RaceProps?.doesntMove != true
				&& pawn is not Zombie
				&& pawn is not ZombieSymbiant
				&& pawn is not ZombieSpitter;
		}

		static bool IsLinkedHostOnCurrentMapFast(Pawn pawn)
		{
			if (CanBeLinkedHostIdentityFast(pawn) == false || pawn.Spawned == false || pawn.Map == null)
				return false;
			return ActiveSymbiant(pawn.Map)?.IsLinkedTo(pawn) == true;
		}

		static bool IsActiveSymbiantOnMap(ZombieSymbiant symbiant, Map map)
		{
			return symbiant != null && symbiant.Destroyed == false && symbiant.Spawned && symbiant.Dead == false && symbiant.Map == map;
		}

		internal static IEnumerable<ZombieSymbiant> MapBoundSymbiants(Map map)
		{
			if (map == null)
				yield break;

			var seen = new HashSet<ZombieSymbiant>();
			foreach (var pawn in map.mapPawns?.AllPawns ?? Enumerable.Empty<Pawn>())
				if (pawn is ZombieSymbiant symbiant
					&& symbiant.Destroyed == false
					&& symbiant.MapHeld == map
					&& seen.Add(symbiant))
					yield return symbiant;
			foreach (var symbiant in SpawnedSymbiantThings(map))
				if (symbiant.Destroyed == false && symbiant.MapHeld == map && seen.Add(symbiant))
					yield return symbiant;
		}

		static void RegisterActiveSymbiant(ZombieSymbiant symbiant, Map map)
		{
			if (symbiant == null || map == null)
				return;
			activeSymbiantByMap[map] = symbiant;
			mapsWithoutActiveSymbiant.Remove(map);
		}

		static void ForgetActiveSymbiant(ZombieSymbiant symbiant)
		{
			foreach (var map in activeSymbiantByMap
				.Where(pair => ReferenceEquals(pair.Value, symbiant))
				.Select(pair => pair.Key)
				.ToArray())
				activeSymbiantByMap.Remove(map);
		}

		internal static void ForgetMap(Map map)
		{
			if (map == null)
				return;
			activeSymbiantByMap.Remove(map);
			mapsWithoutActiveSymbiant.Remove(map);
			if (ReferenceEquals(cachedColonyCenterMap, map))
			{
				cachedColonyCenterMap = null;
				cachedColonyCenterTick = -1;
				cachedColonyCenter = IntVec3.Invalid;
			}
		}

		public static ZombieSymbiant ActiveSymbiant(Map map)
		{
			if (map == null)
				return null;
			if (activeSymbiantByMap.TryGetValue(map, out var cached))
			{
				if (IsActiveSymbiantOnMap(cached, map))
					return cached;
				activeSymbiantByMap.Remove(map);
			}
			if (mapsWithoutActiveSymbiant.Contains(map))
				return null;

			foreach (var symbiant in SpawnedSymbiantThings(map))
			{
				if (IsActiveSymbiantOnMap(symbiant, map))
				{
					symbiant.EnsureVisibleToPawnSystems(map);
					RegisterActiveSymbiant(symbiant, map);
					return symbiant;
				}
			}
			mapsWithoutActiveSymbiant.Add(map);
			return null;
		}

		static IEnumerable<ZombieSymbiant> SpawnedSymbiantThings(Map map)
		{
			var lister = map?.listerThings;
			if (lister == null)
				yield break;

			var def = CustomDefs.ZombieSymbiant;
			if (def != null)
			{
				var things = lister.ThingsOfDef(def);
				if (things != null)
					for (var i = 0; i < things.Count; i++)
						if (things[i] is ZombieSymbiant symbiant)
							yield return symbiant;
				yield break;
			}

			foreach (var thing in lister.AllThings)
				if (thing is ZombieSymbiant symbiant)
					yield return symbiant;
		}

		static IEnumerable<ZombieSymbiant> ActiveSymbiants()
		{
			if (Find.Maps == null)
				yield break;
			foreach (var map in Find.Maps)
			{
				var symbiant = ActiveSymbiant(map);
				if (symbiant != null)
					yield return symbiant;
			}
		}

		bool IsLinkedTo(Pawn pawn)
		{
			if (pawn == null || symbiosisSevered)
				return false;
			if (ReferenceEquals(host, pawn))
			{
				hostThingId ??= pawn.ThingID;
				return true;
			}
			return hostThingId.NullOrEmpty() == false && hostThingId == pawn.ThingID;
		}

		public static ZombieSymbiant LinkedSymbiantFor(Pawn pawn)
		{
			return LinkedSymbiantFor(pawn, false);
		}

		static ZombieSymbiant LinkedSymbiantFor(Pawn pawn, bool allowDead)
		{
			if (CanBeLinkedHostIdentityFast(pawn, allowDead) == false)
				return null;
			if (pawn.Spawned && pawn.Map != null)
			{
				if (allowDead)
				{
					var mapSymbiant = SpawnedSymbiantThings(pawn.Map).FirstOrDefault(symbiant => symbiant.IsLinkedTo(pawn));
					if (mapSymbiant != null)
						return mapSymbiant;
				}
				else
				{
					var mapSymbiant = ActiveSymbiant(pawn.Map);
					if (mapSymbiant != null && mapSymbiant.IsLinkedTo(pawn))
						return mapSymbiant;
				}
			}
			if (allowDead && Find.Maps != null)
			{
				foreach (var map in Find.Maps)
				{
					var symbiant = SpawnedSymbiantThings(map).FirstOrDefault(candidate => candidate.IsLinkedTo(pawn));
					if (symbiant != null)
						return symbiant;
				}
				return null;
			}
			return ActiveSymbiants().FirstOrDefault(symbiant => symbiant.IsLinkedTo(pawn));
		}

		static bool TryGetSameMapLinkedSymbiant(Pawn pawn, out ZombieSymbiant symbiant)
		{
			symbiant = null;
			if (pawn?.MapHeld == null)
				return false;
			if (CanBeLinkedHostIdentityFast(pawn) == false)
				return false;
			symbiant = LinkedSymbiantFor(pawn);
			return symbiant?.IsActiveBondWith(pawn) == true;
		}

		public static bool HasZombieTargetingProtection(Pawn pawn)
		{
			if (DebugDisableSymbiosisBenefits)
				return false;
			return TryGetSameMapLinkedSymbiant(pawn, out var symbiant) && symbiant.HasBenefit(HostBenefit.ZombieIgnore);
		}

		public static float SymbiantBenefitFactor(Pawn pawn)
		{
			if (DebugDisableSymbiosisBenefits)
				return 0f;
			return TryGetSameMapLinkedSymbiant(pawn, out var symbiant) ? symbiant.BenefitFactor : 0f;
		}

		public static bool HasZombieInfectionImmunity(Pawn pawn)
		{
			if (DebugDisableSymbiosisBenefits)
				return false;
			return TryGetSameMapLinkedSymbiant(pawn, out _);
		}

		public static bool HasMoodFixedBenefit(Pawn pawn)
		{
			if (DebugDisableSymbiosisBenefits)
				return false;
			return TryGetSameMapLinkedSymbiant(pawn, out var symbiant) && symbiant.HasBenefit(HostBenefit.MoodFixed);
		}

		public static bool HasNoFoodOrRestBenefit(Pawn pawn)
		{
			if (DebugDisableSymbiosisBenefits)
				return false;
			return TryGetSameMapLinkedSymbiant(pawn, out var symbiant) && symbiant.HasBenefit(HostBenefit.NoFoodOrRest);
		}

		public static int MoveSpeedBenefitCount(Pawn pawn)
		{
			if (DebugDisableSymbiosisBenefits)
				return 0;
			return TryGetSameMapLinkedSymbiant(pawn, out var symbiant) ? symbiant.BenefitCount(HostBenefit.MoveSpeed) : 0;
		}

		public static int ManipulationBenefitCount(Pawn pawn)
		{
			if (DebugDisableSymbiosisBenefits)
				return 0;
			return TryGetSameMapLinkedSymbiant(pawn, out var symbiant) ? symbiant.BenefitCount(HostBenefit.Manipulation) : 0;
		}

		public static int SkillBonusBenefitCount(Pawn pawn)
		{
			if (DebugDisableSymbiosisBenefits)
				return 0;
			return TryGetSameMapLinkedSymbiant(pawn, out var symbiant) ? symbiant.BenefitCount(HostBenefit.SkillBonus) : 0;
		}

		public static int SkillBonusPerBenefit()
		{
			var difficulty = ZombieLand.Tools.Difficulty();
			if (difficulty < 2f)
				return 4;
			if (difficulty < 3f)
				return 3;
			if (difficulty < 4f)
				return 2;
			return 1;
		}

		public static float SymbiantCellEfficiencyFactor(Pawn pawn)
		{
			if (DebugDisableCellStatEffects)
				return 1f;
			if (pawn == null || IsSymbiantCellForAffectedPawn(pawn, pawn.Position, out _) == false)
				return 1f;
			return 1f - SymbiantCellSlowFactor();
		}

		public static int SymbiantMoveCost(Pawn pawn, float baseCost)
		{
			var roundedBaseCost = Mathf.RoundToInt(baseCost);
			if (DebugDisablePathCost || pawn == null || pawn.Spawned == false || pawn.Map == null)
				return roundedBaseCost;
			if (baseCost <= 0f)
				return roundedBaseCost;
			var slowedCost = Mathf.CeilToInt(baseCost * (1f + SymbiantCellSlowFactor()));
			return Mathf.Max(roundedBaseCost, slowedCost);
		}

		public static float SymbiantCellSlowFactor()
		{
			return DifficultyScaled(SymbiantCellSlowMin, SymbiantCellSlowMax);
		}

		public static int SymbiantCellSlowPercent()
		{
			return Mathf.RoundToInt(SymbiantCellSlowFactor() * 100f);
		}

		public static int SeveranceExtractCost()
		{
			return Mathf.RoundToInt(DifficultyScaled(SeveranceExtractCostMin, SeveranceExtractCostMax));
		}

		public static bool TryGetHostAuraFactor(Pawn pawn, out float factor)
		{
			factor = 0f;
			if (DebugDisableSymbiosisBenefits)
				return false;
			if (TryGetSameMapLinkedSymbiant(pawn, out var symbiant) == false)
				return false;
			factor = Mathf.Max(HostAuraMinimumFactor, symbiant.BenefitFactor);
			return true;
		}

		public static void ApplySymbiantSkillBonus(SkillRecord skill, ref int level)
		{
			if (suppressSymbiantSkillBonusDepth > 0)
				return;
			var pawn = skill?.Pawn;
			var bonus = 0;
			if (DebugDisableSymbiosisBenefits == false && TryGetSameMapLinkedSymbiant(pawn, out var symbiant))
				bonus = symbiant.BenefitCount(HostBenefit.SkillBonus) * SkillBonusPerBenefit();
			if (bonus <= 0)
				return;
			level = Mathf.Clamp(level + bonus, 0, SkillRecord.MaxLevel);
		}

		public static bool TryGetSymbiantSkillBonusBreakdown(
			SkillRecord skill,
			int effectiveLevel,
			bool forUi,
			out int baseLevel,
			out int appliedBonus,
			out int nominalBonus)
		{
			baseLevel = effectiveLevel;
			appliedBonus = 0;
			nominalBonus = 0;
			if (skill?.Pawn == null || skill.TotallyDisabled)
				return false;

			nominalBonus = SkillBonusBenefitCount(skill.Pawn) * SkillBonusPerBenefit();
			if (nominalBonus <= 0)
				return false;

			suppressSymbiantSkillBonusDepth++;
			try
			{
				baseLevel = forUi ? skill.GetLevelForUI() : skill.GetLevel();
			}
			finally
			{
				suppressSymbiantSkillBonusDepth--;
			}

			appliedBonus = Mathf.Clamp(effectiveLevel - baseLevel, 0, nominalBonus);
			return true;
		}

		public static string FormatSymbiantSkillLevel(int effectiveLevel, SkillRecord skill)
		{
			if (TryGetSymbiantSkillBonusBreakdown(skill, effectiveLevel, false, out var baseLevel, out var appliedBonus, out _) == false
				|| appliedBonus <= 0)
				return effectiveLevel.ToStringCached();
			return $"{baseLevel.ToStringCached()} + {appliedBonus.ToStringCached()}";
		}

		public static string SymbiantSkillBonusTooltipLine(SkillRecord skill)
		{
			if (skill == null)
				return null;
			var effectiveLevel = skill.GetLevelForUI();
			if (TryGetSymbiantSkillBonusBreakdown(skill, effectiveLevel, true, out _, out var appliedBonus, out var nominalBonus) == false)
				return null;
			var value = appliedBonus == nominalBonus
				? "SymbiantSkillBonusTooltipValue".Translate(appliedBonus)
				: "SymbiantSkillBonusTooltipCappedValue".Translate(appliedBonus, nominalBonus);
			return (("SymbiantSkillBonusTooltipLabel".Translate().CapitalizeFirst() + ": ").AsTipTitle() + value).Resolve();
		}

		public static bool CanSeverSymbiosis(Pawn pawn)
		{
			return TryGetSameMapLinkedSymbiant(pawn, out var symbiant) && symbiant.CanSafelySever;
		}

		public static void NotifyHostKilled(Pawn pawn, ZombieSymbiant linkedSymbiant = null)
		{
			if (pawn == null
				|| pawn is Zombie
				|| pawn is ZombieSymbiant
				|| pawn is ZombieSpitter
				|| pawn.RaceProps?.Humanlike != true
				|| pawn.RaceProps.IsFlesh == false)
				return;
			var symbiant = linkedSymbiant;
			if (symbiant == null && pawn.Destroyed == false)
				symbiant = LinkedSymbiantFor(pawn, true);
			if (symbiant == null)
			{
				_ = TryDestroyDeadLinkedSymbiantCorpse(pawn);
				return;
			}
			if (symbiant.Dead)
			{
				if (TryDestroyDeadLinkedSymbiantCorpse(pawn) == false)
					symbiant.DestroyWithoutHostTrauma(true);
				return;
			}
			symbiant.CollapseFromHostDeath();
		}

		static bool TryDestroyDeadLinkedSymbiantCorpse(Pawn pawn)
		{
			if (pawn == null || Find.Maps == null)
				return false;
			foreach (var map in Find.Maps)
			{
				var corpses = map?.listerThings?.ThingsInGroup(ThingRequestGroup.Corpse);
				if (corpses == null)
					continue;
				for (var i = 0; i < corpses.Count; i++)
				{
					if (corpses[i] is Corpse corpse && corpse.InnerPawn is ZombieSymbiant symbiant && symbiant.IsLinkedTo(pawn))
					{
						symbiant.DestroyWithoutHostTrauma(true);
						if (corpse.Destroyed == false)
							corpse.Destroy(DestroyMode.Vanish);
						return true;
					}
				}
			}
			return false;
		}

		public static void PreApplyHostLinkedDamage(Pawn pawn, ref DamageInfo dinfo, ref bool absorbed)
		{
			if (IsSharedHealthDamage(dinfo) == false)
				return;
			if (TryGetSameMapLinkedSymbiant(pawn, out var symbiant) == false)
				return;
			symbiant.PreApplyLinkedHostDamage(pawn, ref dinfo, ref absorbed);
		}

		public static bool IsSharedHealthDamage(DamageInfo dinfo)
		{
			if (dinfo.Amount <= 0f || dinfo.Def == null)
				return false;
			var extension = dinfo.Def.GetModExtension<SymbiantSharedHealthDamageExtension>();
			if (extension != null)
				return extension.shareWithSymbiant;
			var workerClass = dinfo.Def.workerClass;
			return workerClass != null && typeof(DamageWorker_AddInjury).IsAssignableFrom(workerClass);
		}

		void PreApplyLinkedHostDamage(Pawn pawn, ref DamageInfo dinfo, ref bool absorbed)
		{
			if (safeSeveranceInProgress || hostCollapseInProgress)
				return;
			if (pawn == null || pawn.Dead || pawn.Destroyed || dinfo.Amount <= 0f)
				return;

			var originalAmount = dinfo.Amount;
			var drained = DrainSharedHealth(originalAmount);
			if (pawn.Dead || Destroyed || Dead || drained >= originalAmount && SharedHealthCurrent <= 0f)
			{
				dinfo.SetAmount(0f);
				absorbed = true;
				return;
			}

			var hostAmount = SharedDamageLeakAmount(drained);
			NotifySharedDamageAbsorbed(drained, hostAmount, pawn);
			dinfo.SetAmount(hostAmount);
			if (hostAmount <= 0.01f)
				absorbed = true;
		}

		public static bool IsSymbiantCell(Map map, IntVec3 cell, out ZombieSymbiant symbiant)
		{
			symbiant = null;
			if (map == null || cell.InBounds(map) == false)
				return false;
			symbiant = ActiveSymbiant(map);
			return symbiant != null && symbiant.ContainsCell(cell);
		}

		internal static bool TryReduceContaminationOnLeavingSymbiantCell(Pawn pawn)
		{
			if (Constants.CONTAMINATION == false || CanBeAffectedBySymbiantCellCandidateFast(pawn) == false)
				return false;
			if (IsSymbiantCell(pawn.Map, pawn.Position, out _) == false)
				return false;
			var contamination = pawn.GetContamination(false);
			if (contamination <= 0f)
				return false;
			pawn.SetContamination(Mathf.Max(0f, contamination * (1f - SymbiantContaminationStepReduction)));
			return true;
		}

		public static bool IsSymbiantCellForAffectedPawn(Pawn pawn, IntVec3 cell, out ZombieSymbiant symbiant)
		{
			symbiant = null;
			if (CanBeAffectedBySymbiantCellCandidateFast(pawn) == false)
				return false;
			var map = pawn.Map;
			if (cell.InBounds(map) == false)
				return false;
			symbiant = ActiveSymbiant(map);
			if (symbiant == null)
				return false;
			if (symbiant.ContainsCell(cell) == false)
				return false;
			return symbiant.IsLinkedTo(pawn) == false;
		}

		public static bool IsSymbiantCellForSlowedPawn(Pawn pawn, IntVec3 cell, out ZombieSymbiant symbiant)
		{
			symbiant = null;
			if (CanBeSlowedBySymbiantCellCandidateFast(pawn) == false)
				return false;
			var map = pawn.Map;
			if (cell.InBounds(map) == false)
				return false;
			symbiant = ActiveSymbiant(map);
			if (symbiant == null)
				return false;
			if (symbiant.ContainsCell(cell) == false)
				return false;
			return symbiant.IsLinkedTo(pawn) == false;
		}

		public static int CountCellsInRoom(Room room)
		{
			var map = room?.Map;
			if (map == null)
				return 0;
			var symbiant = ActiveSymbiant(map);
			if (symbiant == null)
				return 0;
			return symbiant.CountCellsInRoomInternal(room);
		}

		int CountCellsInRoomInternal(Room room)
		{
			if (room == null || hasCellBounds == false || AbsoluteCellBounds.Overlaps(room.ExtentsClose) == false)
				return 0;
			var map = room.Map;
			var count = 0;
			for (var i = 0; i < orderedCells.Count; i++)
			{
				var cell = Position + orderedCells[i];
				if (cell.InBounds(map) && cell.GetRoom(map) == room)
					count++;
			}
			return count;
		}

		static int EligibleColonyRoomCellCount(Map map)
		{
			if (map == null)
				return 0;
			return CandidateRooms(map).ToArray().Sum(room => room.CellCount);
		}

		static float SymbiantDifficulty()
		{
			return Mathf.Clamp(ZombieLand.Tools.Difficulty(), 1f, 5f);
		}

		static float DifficultyScaled(float minAtOne, float maxAtFive)
		{
			return GenMath.LerpDoubleClamped(1f, 5f, minAtOne, maxAtFive, SymbiantDifficulty());
		}

		static float SymbiantGrowthSpeedFactor()
		{
			var difficulty = Mathf.Clamp(ZombieLand.Tools.Difficulty(), 0f, 5f);
			return difficulty <= SymbiantFastGrowthDifficultyLimit
				? SymbiantLowDifficultyGrowthSpeedFactor
				: SymbiantHighDifficultyGrowthSpeedFactor;
		}

		static int BenefitStepCells()
		{
			return Mathf.RoundToInt(DifficultyScaled(20f, 50f));
		}

		bool HasBenefit(HostBenefit benefit)
		{
			return hostBenefits?.Contains(benefit) == true;
		}

		int BenefitCount(HostBenefit benefit)
		{
			return hostBenefits?.Count(item => item == benefit) ?? 0;
		}

		static bool BenefitCanStack(HostBenefit benefit)
		{
			return benefit == HostBenefit.SkillBonus
				|| benefit == HostBenefit.MoveSpeed
				|| benefit == HostBenefit.Manipulation
				|| benefit == HostBenefit.AutoHeal;
		}

		void EnsureBenefitDefaults()
		{
			hostBenefits ??= [];
			if (benefitStepCells <= 0)
				benefitStepCells = MigratedBenefitStepCells();
			if (nextBenefitCellThreshold <= 0)
				nextBenefitCellThreshold = benefitStepCells;
		}

		int MigratedBenefitStepCells()
		{
			if (nextBenefitCellThreshold > 0)
			{
				var awardedCount = hostBenefits?.Count ?? 0;
				return Mathf.Max(1, Mathf.RoundToInt(nextBenefitCellThreshold / Mathf.Max(1f, awardedCount + 1f)));
			}
			return Mathf.Max(1, BenefitStepCells());
		}

		void AwardBenefitsForCurrentSize()
		{
			if (symbiosisSevered || LinkedHost == null || DebugDisableSymbiosisBenefits)
				return;
			EnsureBenefitDefaults();
			var step = Mathf.Max(1, benefitStepCells);
			while (CellCount >= nextBenefitCellThreshold)
			{
				AwardRandomBenefit();
				nextBenefitCellThreshold += step;
			}
		}

		void AwardRandomBenefit()
		{
			EnsureBenefitDefaults();
			var available = hostBenefitPool
				.Where(benefit => BenefitCanStack(benefit) || HasBenefit(benefit) == false)
				.ToArray();
			if (available.Length == 0)
				available = hostBenefitPool.Where(BenefitCanStack).ToArray();
			if (available.Length == 0)
				return;
			var benefit = available.RandomElement();
			hostBenefits.Add(benefit);
			EnsureHostHediff();
			if (benefit == HostBenefit.MoveSpeed || benefit == HostBenefit.Manipulation)
				NotifyHostCapacityBenefitsChanged(LinkedHost);
			NotifyBenefitAwarded(benefit);
		}

		void NotifyBenefitAwarded(HostBenefit benefit)
		{
			var linkedHost = LinkedHost;
			if (Spawned == false || linkedHost == null)
				return;
			var label = BenefitLabel(benefit);
			var targets = new LookTargets(this, linkedHost);
			Messages.Message("SymbiantBenefitGainedMessage".Translate(linkedHost.LabelShortCap, label), targets, MessageTypeDefOf.PositiveEvent, false);
			SendSymbiantEventLetter(
				"LetterLabelSymbiantBenefitGained".Translate(),
				"SymbiantBenefitGainedLetter".Translate(linkedHost.LabelShortCap, label, BenefitSummary),
				targets
			);
		}

		static TaggedString BenefitLabel(HostBenefit benefit)
		{
			return benefit switch
			{
				HostBenefit.MoodFixed => "SymbiantBenefitMoodFixed".Translate(),
				HostBenefit.NoFoodOrRest => "SymbiantBenefitNoFoodOrRest".Translate(),
				HostBenefit.SkillBonus => "SymbiantBenefitSkillBonus".Translate(SkillBonusPerBenefit()),
				HostBenefit.MoveSpeed => "SymbiantBenefitMoveSpeed".Translate(),
				HostBenefit.Manipulation => "SymbiantBenefitManipulation".Translate(),
				HostBenefit.ZombieIgnore => "SymbiantBenefitZombieIgnore".Translate(),
				HostBenefit.AutoHeal => "SymbiantBenefitAutoHeal".Translate(),
				_ => benefit.ToString()
			};
		}

		static int CalculateFullBenefitCells(Map map)
		{
			return CalculateFullBenefitCells(EligibleColonyRoomCellCount(map));
		}

		static int CalculateFullBenefitCells(int eligibleCells)
		{
			var maxCells = Mathf.Max(1, MaxCells);
			var target = Mathf.Max(20, Mathf.CeilToInt(eligibleCells * FullBenefitRoomCoverage));
			return Mathf.Clamp(target, 1, maxCells);
		}

		static int MinimumNaturalSpawnEligibleCells()
		{
			return 1;
		}

		float CalculateIntegratedVisibleCells(Map map)
		{
			if (map == null || orderedCells == null)
				return 0f;
			var total = 0f;
			foreach (var cell in orderedCells)
				total += IntegratedCellWeight(map, Position + cell);
			return total;
		}

		static bool IsEligibleIndoorRoom(Room room)
		{
			return room != null
				&& room.IsDoorway == false
				&& room.Fogged == false
				&& room.IsHuge == false
				&& room.UsesOutdoorTemperature == false
				&& room.ProperRoom;
		}

		static float IntegratedCellWeight(Map map, IntVec3 cell)
		{
			if (map == null || cell.InBounds(map) == false || cell.Fogged(map))
				return 0f;
			if (IsDoorCell(map, cell) == false)
			{
				if (cell.Roofed(map) == false)
					return 0f;
				var room = cell.GetRoom(map);
				if (IsEligibleIndoorRoom(room) == false)
					return 0f;
			}
			return ScoreTraffic(map, cell) > 0f ? 1f : 0.5f;
		}

		static bool IsValidSymbiantCell(Map map, IntVec3 cell)
		{
			if (map == null || cell.InBounds(map) == false)
				return false;
			return CanOccupyOpenCell(map, cell) || IsDoorCell(map, cell);
		}

		void RefreshSymbiosisMetrics(bool force = false)
		{
			var ticks = GenTicks.TicksGame;
			if (force == false && lastSymbiosisMetricTick != int.MinValue && ticks - lastSymbiosisMetricTick < SymbiosisMetricRefreshInterval)
				return;
			var map = SymbiantMap;
			cachedEligibleColonyRoomCells = EligibleColonyRoomCellCount(map);
			cachedFullBenefitCells = CalculateFullBenefitCells(cachedEligibleColonyRoomCells);
			cachedIntegratedVisibleCells = CalculateIntegratedVisibleCells(map);
			cachedBenefitFactor = Mathf.Clamp01(cachedIntegratedVisibleCells / Mathf.Max(1f, cachedFullBenefitCells));
			lastSymbiosisMetricTick = ticks;
		}

		void UpdateSymbiosisState(bool forceMetricRefresh = true)
		{
			if (Destroyed)
				return;
			RefreshSymbiosisMetrics(forceMetricRefresh);
			if (cachedIntegratedVisibleCells > UprootedIntegratedCellThreshold)
				uprootedSinceTick = -1;
			AwardBenefitsForCurrentSize();
		}

		static Pawn ResolvePawnByThingId(string thingId)
		{
			if (thingId.NullOrEmpty() || Find.Maps == null)
				return null;
			foreach (var map in Find.Maps)
			{
				var pawn = map?.mapPawns?.AllPawns?.FirstOrDefault(candidate => candidate?.ThingID == thingId);
				if (pawn != null)
					return pawn;
			}
			return null;
		}

		Pawn ResolveHost()
		{
			if (host != null && host.Destroyed == false)
			{
				hostThingId ??= host.ThingID;
				return host;
			}
			if (host != null)
				RemoveHostHediff(host);
			host = ResolvePawnByThingId(hostThingId);
			if (host != null)
				hostThingId = host.ThingID;
			return host;
		}

		internal bool IsActiveBondWith(Pawn pawn)
		{
			var hostMap = pawn?.MapHeld;
			return pawn != null
				&& pawn.Destroyed == false
				&& pawn.Dead == false
				&& hostMap != null
				&& IsActiveSymbiantOnMap(this, hostMap)
				&& IsLinkedTo(pawn);
		}

		public bool TryAssignRandomHost()
		{
			if (symbiosisSevered)
				return false;
			if (ResolveHost() != null)
				return true;
			var map = SymbiantMap;
			if (map == null)
				return false;
			var candidates = EligibleHosts(map, this).ToArray();
			if (candidates.Length == 0)
				return false;
			AssignHost(candidates.RandomElement());
			return true;
		}

		static IEnumerable<Pawn> EligibleHosts(Map map, ZombieSymbiant symbiant)
		{
			if (map?.mapPawns?.FreeColonistsSpawned == null)
				return Enumerable.Empty<Pawn>();
			return map.mapPawns.FreeColonistsSpawned.Where(pawn => IsEligibleHost(pawn, symbiant));
		}

		static bool IsEligibleHost(Pawn pawn, ZombieSymbiant symbiant)
		{
			if (pawn?.Spawned != true)
				return false;
			if (CanEverBeLinkedHostFast(pawn) == false)
				return false;
			if (AlienTools.IsFleshPawn(pawn) == false || SoSTools.IsHologram(pawn))
				return false;
			if (pawn.InfectionState() >= InfectionState.Infecting)
				return false;
			var linkedSymbiant = LinkedSymbiantFor(pawn);
			return linkedSymbiant == null || linkedSymbiant == symbiant;
		}

		void AssignHost(Pawn pawn)
		{
			host = pawn;
			hostThingId = pawn?.ThingID;
			RememberHostBondState(pawn);
			EnsureHostHediff();
		}

		void EnsureHostLink()
		{
			var linkedHost = ResolveHost();
			if (linkedHost == null)
			{
				if (hostThingId.NullOrEmpty() == false && symbiosisSevered == false)
					CollapseFromHostDeath();
				return;
			}
			if (linkedHost.Dead || linkedHost.Destroyed)
			{
				CollapseFromHostDeath();
				return;
			}
			NotifyHostBondStateTransition(linkedHost);
			EnsureHostHediff();
		}

		void RememberHostBondState(Pawn pawn)
		{
			hostBondStateInitialized = true;
			hostBondWasActive = IsActiveBondWith(pawn);
		}

		void NotifyHostBondStateTransition(Pawn pawn)
		{
			var active = IsActiveBondWith(pawn);
			if (hostBondStateInitialized == false)
			{
				RememberHostBondState(pawn);
				return;
			}
			var changed = hostBondWasActive != active;
			if (hostBondWasActive && active == false)
				Find.LetterStack?.ReceiveLetter(
					"LetterLabelSymbiantBondDormant".Translate(),
					"SymbiantHostRelocatedMessage".Translate(pawn.LabelShortCap),
					LetterDefOf.NeutralEvent,
					new LookTargets(this, pawn)
				);
			hostBondWasActive = active;
			if (changed)
				NotifyHostCapacityBenefitsChanged(pawn);
		}

		internal static void NotifyHostCapacityBenefitsChanged(Pawn pawn)
		{
			pawn?.health?.capacities?.Notify_CapacityLevelsDirty();
		}

		void EnsureHostHediff()
		{
			if (DebugDisableHostHediffSync)
				return;
			var pawn = ResolveHost();
			if (pawn?.health?.hediffSet == null || CustomDefs.SymbiantSymbiosis == null)
				return;
			if (symbiosisSevered || pawn.Destroyed || pawn.Dead)
			{
				RemoveHostHediff(pawn);
				return;
			}
			var hediffs = pawn.health.hediffSet.hediffs
				.Where(candidate => candidate.def == CustomDefs.SymbiantSymbiosis)
				.OfType<Hediff_SymbiantSymbiosis>()
				.ToArray();
			var hediff = hediffs.FirstOrDefault(candidate => candidate.symbiantThingId == ThingID)
				?? hediffs.FirstOrDefault();
			foreach (var duplicate in hediffs.Where(candidate => candidate != hediff))
				pawn.health.RemoveHediff(duplicate);
			var severity = HostHediffSeverity(SymbiantBenefitFactor(pawn));
			if (hediff == null)
			{
				hediff = HediffMaker.MakeHediff(CustomDefs.SymbiantSymbiosis, pawn) as Hediff_SymbiantSymbiosis;
				if (hediff != null)
				{
					hediff.symbiantThingId = ThingID;
					hediff.Severity = severity;
					pawn.health.AddHediff(hediff);
				}
			}
			if (hediff != null)
			{
				hediff.symbiantThingId = ThingID;
				hediff.Severity = severity;
			}
			SyncHostDamageEchoes(pawn);
		}

		static void RemoveHostHediff(Pawn pawn)
		{
			if (pawn?.health?.hediffSet == null)
				return;
			foreach (var hediff in pawn.health.hediffSet.hediffs
				.Where(hediff => hediff.def == CustomDefs.SymbiantSymbiosis || hediff is Hediff_SymbiantDamageEcho)
				.ToArray())
				pawn.health.RemoveHediff(hediff);
		}

		public static void AddCell(Map map, IntVec3 cell)
		{
			ActiveSymbiant(map)?.AddCell(cell);
		}

		public static int AddCells(Map map, IEnumerable<IntVec3> newCells)
		{
			if (map == null)
				return 0;
			var newCellArray = newCells?.ToArray() ?? Array.Empty<IntVec3>();
			if (newCellArray.Length == 0)
				return 0;
			return ActiveSymbiant(map)?.AddCells(newCellArray) ?? 0;
		}

		internal static void ReleaseAllRenderResources()
		{
			foreach (var symbiant in renderResourceOwners.ToArray())
				symbiant.ReleaseRenderResources(false);
			renderResourceOwners.Clear();
		}

		internal static void ReleaseRenderResourcesForMap(Map map)
		{
			if (map == null)
				return;
			foreach (var symbiant in renderResourceOwners.ToArray())
			{
				if (symbiant != null && (symbiant.MapHeld == map || symbiant.Map == map))
					symbiant.ReleaseRenderResources();
			}
		}

		internal static void ClearActiveSymbiantCaches()
		{
			activeSymbiantByMap.Clear();
			mapsWithoutActiveSymbiant.Clear();
		}

		internal static void ResetTransientStaticState()
		{
			ReleaseAllRenderResources();
			ClearActiveSymbiantCaches();
			cachedColonyCenterMap = null;
			cachedColonyCenterTick = -1;
			cachedColonyCenter = IntVec3.Invalid;
		}

		internal static int PurgeLegacyWorldPawnSymbiants()
		{
			var worldPawns = Find.WorldPawns;
			if (worldPawns == null)
				return 0;
			var legacySymbiants = worldPawns.AllPawnsAliveOrDead
				.OfType<ZombieSymbiant>()
				.ToArray();
			foreach (var symbiant in legacySymbiants)
				symbiant.DestroyWithoutHostTrauma(true);
			return legacySymbiants.Length;
		}

		internal static object DebugCacheState(Map map = null)
		{
			return new
			{
				activeCacheCount = activeSymbiantByMap.Count,
				emptyCacheCount = mapsWithoutActiveSymbiant.Count,
				currentMapActiveCached = map != null && activeSymbiantByMap.TryGetValue(map, out var cached) && IsActiveSymbiantOnMap(cached, map),
				currentMapCachedSymbiant = map != null && activeSymbiantByMap.TryGetValue(map, out var cachedSymbiant) ? cachedSymbiant.ThingID : null,
				currentMapMarkedEmpty = map != null && mapsWithoutActiveSymbiant.Contains(map)
			};
		}

		void ReleaseRenderResources(bool unregister = true)
		{
			selectionCoreHoverBlend = 0f;
			selectionCoreHoverVelocity = 0f;
			selectionCoreSelectedBlend = 0f;
			selectionCoreSelectedVelocity = 0f;
			selectionCoreDiscoveryBlend = 0f;
			selectionCoreDiscoveryVelocity = 0f;
			selectionCoreInteractionLastRealtime = -1f;
			if (selectionCoreMaterial != null)
			{
				UnityEngine.Object.Destroy(selectionCoreMaterial);
				selectionCoreMaterial = null;
			}
			if (selectionCoreTexture != null)
			{
				UnityEngine.Object.Destroy(selectionCoreTexture);
				selectionCoreTexture = null;
			}
			if (selectionCoreMesh != null)
			{
				UnityEngine.Object.Destroy(selectionCoreMesh);
				selectionCoreMesh = null;
			}
			if (metaballMaterial != null)
			{
				UnityEngine.Object.Destroy(metaballMaterial);
				metaballMaterial = null;
			}
			if (metaballMaskMaterial != null)
			{
				UnityEngine.Object.Destroy(metaballMaskMaterial);
				metaballMaskMaterial = null;
			}
			if (metaballBuffer != null)
			{
				metaballBuffer.Release();
				metaballBuffer = null;
				metaballBufferCapacity = 0;
			}
			metaballBufferData = [];
			if (metaballTexture != null)
			{
				UnityEngine.Object.Destroy(metaballTexture);
				metaballTexture = null;
			}
			if (mesh != null)
			{
				UnityEngine.Object.Destroy(mesh);
				mesh = null;
			}
			if (unregister)
				renderResourceOwners.Remove(this);
		}

		public override void SpawnSetup(Map map, bool respawningAfterLoad)
		{
			base.SpawnSetup(map, respawningAfterLoad);
			EnsureSymbiantDefaults();
			RegisterActiveSymbiant(this, map);
			EnsureVisibleToPawnSystems(map);
			UpdateAll();
			RememberHostBondState(ResolveHost());
		}

		void EnsureVisibleToPawnSystems(Map map = null)
		{
			map ??= MapHeld;
			if (map?.mapPawns == null || Spawned == false)
				return;
			if (map.mapPawns.AllPawnsSpawned.Contains(this) == false)
				map.mapPawns.RegisterPawn(this);
		}

		public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
		{
			var destroyAfterDespawn = destructionInProgress == false
				&& temporaryDespawnInProgress == false
				&& Dead == false
				&& health?.isBeingKilled != true;
			ForgetActiveSymbiant(this);
			ReleaseRenderResources();
			base.DeSpawn(mode);
			if (destroyAfterDespawn && Destroyed == false)
				DestroyWithoutHostTrauma(true);
		}

		public override void Kill(DamageInfo? dinfo, Hediff exactCulprit = null)
		{
			if (safeSeveranceInProgress == false && hostCollapseInProgress == false && sharedHealthFailureInProgress == false)
				HandleUncontrolledDestroy();
			base.Kill(dinfo, exactCulprit);
		}

		public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
		{
			ForgetActiveSymbiant(this);
			if (safeSeveranceInProgress == false && hostCollapseInProgress == false && sharedHealthFailureInProgress == false)
				HandleUncontrolledDestroy();
			ReleaseRenderResources();
			destructionInProgress = true;
			try
			{
				base.Destroy(mode);
			}
			finally
			{
				destructionInProgress = false;
			}
			if (health?.isBeingKilled != true)
				RemoveFromWorldPawnsAndDiscard();
		}

		internal void DebugDestroyWithoutHostTrauma()
		{
			DestroyWithoutHostTrauma(true);
		}

		internal void DestroyWithoutHostTrauma(bool discard)
		{
			safeSeveranceInProgress = true;
			try
			{
				EndLinkedHostBond(HostBondTermination.SymbiantRemoved);
				if (Destroyed == false)
					Destroy(DestroyMode.Vanish);
				if (discard)
					RemoveFromWorldPawnsAndDiscard();
			}
			finally
			{
				safeSeveranceInProgress = false;
			}
		}

		void RemoveFromWorldPawnsAndDiscard()
		{
			if (Find.WorldPawns?.Contains(this) == true)
				Find.WorldPawns.RemovePawn(this);
			if (Discarded == false)
				Discard(true);
		}

		bool AddRelativeCell(IntVec3 relative)
		{
			EnsureSharedHealth();
			var wasFullHealth = sharedHealth >= SharedHealthMax - 0.01f;
			if (cells.Add(relative) == false)
				return false;
			destroyWhenCellMotionsFinish = false;
			StartIncomingCellMotion(relative);
			orderedCells.Add(relative);
			combatShapeVersion++;
			ExpandCellBounds(relative);
			if (selectionCoreRelative.IsValid == false)
			{
				selectionCoreRelative = relative;
				selectionCoreLastMoveTick = GenTicks.TicksGame;
			}
			else if (selectionCoreDiscoveryCue == false && selectionCoreRelative == IntVec3.Zero && relative != IntVec3.Zero && cells.Count == 2)
				BeginSelectionCoreMove(IntVec3.Zero, relative);
			if (wasFullHealth)
				sharedHealth = SharedHealthMax;
			return true;
		}

		bool RemoveRelativeCell(IntVec3 relative, bool animate)
		{
			return RemoveRelativeCellWithCoreDestination(relative, animate, IntVec3.Invalid);
		}

		bool RemoveRelativeCellWithCoreDestination(IntVec3 relative, bool animate, IntVec3 selectionCoreDestination)
		{
			if (cells?.Contains(relative) != true)
				return false;
			var removesSelectionCore = selectionCoreRelative == relative;
			if (removesSelectionCore && selectionCoreDestination.IsValid == false)
			{
				var remaining = cells.Where(cell => cell != relative).ToArray();
				selectionCoreDestination = remaining.Length == 0
					? IntVec3.Invalid
					: remaining
						.OrderBy(SelectionCoreClutterScore)
						.ThenBy(cell => cell.DistanceToSquared(relative))
						.First();
			}
			if (animate)
				StartOutgoingCellMotion(relative);
			orderedCells.Remove(relative);
			var removed = cells.Remove(relative);
			if (removed)
			{
				combatShapeVersion++;
				if (removesSelectionCore)
					BeginSelectionCoreMove(relative, selectionCoreDestination);
				EnsureSharedHealth();
			}
			return removed;
		}

		bool WouldCellsStayConnectedAfterRemoval(IntVec3 removedRelative)
		{
			if (cells == null || cells.Count <= 1)
				return true;
			var testCells = new HashSet<IntVec3>(cells);
			testCells.Remove(removedRelative);
			return CellsAreConnectedToRoot(testCells);
		}

		bool WouldCellsStayConnectedAfterMove(IntVec3 removedRelative, IntVec3 addedRelative)
		{
			if (cells == null || cells.Count <= 1)
				return true;
			var testCells = new HashSet<IntVec3>(cells);
			testCells.Remove(removedRelative);
			testCells.Add(addedRelative);
			return CellsAreConnectedToRoot(testCells);
		}

		static bool CellsAreConnectedToRoot(HashSet<IntVec3> testCells)
		{
			if (testCells == null || testCells.Count <= 1)
				return true;
			if (testCells.Contains(IntVec3.Zero) == false)
				return false;
			return ConnectedCells(testCells, IntVec3.Zero).Count == testCells.Count;
		}

		static HashSet<IntVec3> ConnectedCells(HashSet<IntVec3> source, IntVec3 root)
		{
			var connected = new HashSet<IntVec3>();
			if (source == null || source.Contains(root) == false)
				return connected;

			var open = new Queue<IntVec3>();
			connected.Add(root);
			open.Enqueue(root);
			while (open.Count > 0)
			{
				var cell = open.Dequeue();
				for (var i = 0; i < GenAdj.CardinalDirections.Length; i++)
				{
					var neighbor = cell + GenAdj.CardinalDirections[i];
					if (source.Contains(neighbor) && connected.Add(neighbor))
						open.Enqueue(neighbor);
				}
			}
			return connected;
		}

		int PruneDisconnectedCells()
		{
			if (cells == null || cells.Count <= 1 || cells.Contains(IntVec3.Zero) == false)
				return 0;

			var connected = ConnectedCells(cells, IntVec3.Zero);
			if (connected.Count == cells.Count)
				return 0;

			var removed = cells.Where(cell => connected.Contains(cell) == false).ToArray();
			foreach (var cell in removed)
				cells.Remove(cell);
			orderedCells?.RemoveAll(cell => connected.Contains(cell) == false);
			cellMotions?.RemoveAll(motion => connected.Contains(motion.cell) == false);
			if (cells.Contains(selectionCoreRelative) == false)
			{
				selectionCoreRelative = BestSelectionCoreRelative(IntVec3.Zero, selectionCoreDiscoveryCue);
				selectionCoreLastMoveTick = GenTicks.TicksGame;
				ClearSelectionCoreMotion();
			}
			combatShapeVersion++;
			return removed.Length;
		}

		void StartIncomingCellMotion(IntVec3 relative)
		{
			var existingCells = cells.Where(cell => cell != relative).ToArray();
			var to = CellCenter(relative);
			var from = existingCells.Length == 0 ? to : NearestCellCenter(relative, existingCells);
			StartCellMotion(relative, from, to, false, GetSize(relative));
		}

		void StartOutgoingCellMotion(IntVec3 relative)
		{
			var remainingCells = cells.Where(cell => cell != relative).ToArray();
			var from = CellCenter(relative);
			var to = remainingCells.Length == 0 ? from : NearestCellCenter(relative, remainingCells);
			StartCellMotion(relative, from, to, true, GetSize(relative));
		}

		void StartCellMotion(IntVec3 relative, Vector2 from, Vector2 to, bool outgoing, float radius)
		{
			cellMotions ??= [];
			var ticks = GenTicks.TicksGame;
			if (outgoing)
				cellMotions.RemoveAll(motion => motion.cell == relative);
			else
				cellMotions.RemoveAll(motion => motion.outgoing == false && motion.cell == relative);
			cellMotions.Add(new CellMotion(relative, from, to, ticks, ticks + CellMotionDurationTicks, Mathf.Clamp(radius, MetaballCellRadiusMin, MetaballCellRadiusMax), outgoing));
			lastCellMotionRenderTick = -1;
		}

		static Vector2 CellCenter(IntVec3 relative)
		{
			return new Vector2(relative.x, relative.z);
		}

		static Vector2 NearestCellCenter(IntVec3 target, IEnumerable<IntVec3> candidates)
		{
			var nearest = candidates
				.OrderBy(cell => cell.DistanceToSquared(target))
				.FirstOrDefault();
			return CellCenter(nearest.IsValid ? nearest : target);
		}

		IntVec3 SelectionCoreClickRelative
		{
			get
			{
				var center = SelectionCoreVisualCenter;
				return new IntVec3(Mathf.RoundToInt(center.x), 0, Mathf.RoundToInt(center.y));
			}
		}

		Vector2 SelectionCoreVisualCenter
		{
			get
			{
				var ticks = GenTicks.TicksGame;
				if (IsSelectionCoreMotionActive(ticks) == false)
					return CellCenter(selectionCoreRelative.IsValid ? selectionCoreRelative : IntVec3.Zero);
				return Vector2.Lerp(CellCenter(selectionCoreMotionFrom), CellCenter(selectionCoreMotionTo), SelectionCoreMotionProgress(ticks));
			}
		}

		public IntVec3 SelectionCoreDestinationCell
		{
			get
			{
				EnsureSelectionCoreState();
				return selectionCoreRelative.IsValid ? Position + selectionCoreRelative : Position;
			}
		}

		public IntVec3 SelectionCoreMotionFromCell => selectionCoreMotionFrom.IsValid ? Position + selectionCoreMotionFrom : IntVec3.Invalid;
		public IntVec3 SelectionCoreMotionToCell => selectionCoreMotionTo.IsValid ? Position + selectionCoreMotionTo : IntVec3.Invalid;

		public bool IsSelectionCoreCell(IntVec3 absoluteCell)
		{
			return Spawned && Destroyed == false && absoluteCell == SelectionCoreCell;
		}

		internal void NotifySelectionCoreDiscoveryCue()
		{
			selectionCoreDiscoveryCue = true;
			selectionCoreRelative = IntVec3.Zero;
			selectionCoreLastMoveTick = GenTicks.TicksGame;
			ClearSelectionCoreMotion();
		}

		public override void Notify_ThingSelected()
		{
			base.Notify_ThingSelected();
			if (selectionCoreDiscoveryCue == false)
				return;
			selectionCoreDiscoveryCue = false;
			PromoteSelectionCoreFromRoot();
		}

		void PromoteSelectionCoreFromRoot()
		{
			EnsureSelectionCoreState();
			if (selectionCoreRelative != IntVec3.Zero || CellCount <= 1)
				return;
			var target = BestSelectionCoreRelative(IntVec3.Zero, false);
			if (target.IsValid && target != IntVec3.Zero)
				BeginSelectionCoreMove(IntVec3.Zero, target);
		}

		void EnsureSelectionCoreState()
		{
			if (cells == null || cells.Count == 0)
			{
				selectionCoreRelative = IntVec3.Invalid;
				ClearSelectionCoreMotion();
				return;
			}

			var ticks = GenTicks.TicksGame;
			if (selectionCoreMotionEndTick >= 0 && ticks >= selectionCoreMotionEndTick)
				CompleteSelectionCoreMotion();
			if (IsSelectionCoreMotionActive(ticks) && cells.Contains(selectionCoreRelative))
				return;
			if (selectionCoreRelative.IsValid && cells.Contains(selectionCoreRelative))
				return;

			selectionCoreRelative = BestSelectionCoreRelative(IntVec3.Zero, selectionCoreDiscoveryCue);
			selectionCoreLastMoveTick = ticks;
			ClearSelectionCoreMotion();
		}

		IntVec3 BestSelectionCoreRelative(IntVec3 near, bool allowRoot)
		{
			if (cells == null || cells.Count == 0)
				return IntVec3.Invalid;
			var candidates = orderedCells
				.Where(cell => cells.Contains(cell))
				.Where(cell => allowRoot || cell != IntVec3.Zero)
				.ToArray();
			if (candidates.Length == 0)
				candidates = orderedCells.Where(cell => cells.Contains(cell)).ToArray();
			return candidates
				.OrderBy(SelectionCoreClutterScore)
				.ThenBy(cell => WouldCellsStayConnectedAfterRemoval(cell) ? 0 : 1)
				.ThenBy(SelectionCoreCardinalNeighborCount)
				.ThenBy(cell => near.IsValid ? cell.DistanceToSquared(near) : 0)
				.ThenBy(cell => cell.x)
				.ThenBy(cell => cell.z)
				.FirstOrDefault();
		}

		int SelectionCoreCardinalNeighborCount(IntVec3 relative)
		{
			return GenAdj.CardinalDirections.Count(direction => cells.Contains(relative + direction));
		}

		int SelectionCoreClutterScore(IntVec3 relative)
		{
			var map = MapHeld;
			var absolute = Position + relative;
			if (map == null || absolute.InBounds(map) == false)
				return 0;
			return map.thingGrid.ThingsListAtFast(absolute)
				.Where(thing => thing != this && thing is not Pawn && thing?.def?.selectable == true)
				.Sum(thing => thing is Building ? 10 : 1);
		}

		void BeginSelectionCoreMove(IntVec3 from, IntVec3 to)
		{
			if (to.IsValid == false)
			{
				selectionCoreRelative = IntVec3.Invalid;
				ClearSelectionCoreMotion();
				return;
			}
			selectionCoreRelative = to;
			selectionCoreLastMoveTick = GenTicks.TicksGame;
			if (Spawned == false || from.IsValid == false || from == to)
			{
				ClearSelectionCoreMotion();
				return;
			}
			selectionCoreMotionFrom = from;
			selectionCoreMotionTo = to;
			selectionCoreMotionStartTick = GenTicks.TicksGame;
			selectionCoreMotionEndTick = selectionCoreMotionStartTick + CellMotionDurationTicks;
		}

		internal bool DebugBeginSelectionCoreHandoff(IntVec3 fromCell, IntVec3 toCell)
		{
			if (Spawned == false || fromCell.IsValid == false || toCell.IsValid == false)
				return false;
			var from = fromCell - Position;
			var to = toCell - Position;
			if (from == to || cells?.Contains(from) != true || cells.Contains(to) == false)
				return false;
			BeginSelectionCoreMove(from, to);
			return SelectionCoreMotionActive;
		}

		internal bool DebugSetSelectionCoreHandoffProgress(float progress)
		{
			var ticks = GenTicks.TicksGame;
			if (IsSelectionCoreMotionActive(ticks) == false)
				return false;
			var duration = Mathf.Max(1, selectionCoreMotionEndTick - selectionCoreMotionStartTick);
			var elapsed = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(progress) * duration), 0, duration - 1);
			selectionCoreMotionStartTick = ticks - elapsed;
			selectionCoreMotionEndTick = selectionCoreMotionStartTick + duration;
			return true;
		}

		internal bool DebugDrawSelectionCoreMetaballFallback()
		{
			var previous = debugForceMetaballFallback;
			try
			{
				ReleaseRenderResources();
				lastFallbackSelectionCoreDrawSucceeded = false;
				debugForceMetaballFallback = true;
				DrawAt(DrawPos);
				return lastFallbackSelectionCoreDrawSucceeded;
			}
			finally
			{
				debugForceMetaballFallback = previous;
			}
		}

		void CompleteSelectionCoreMotion()
		{
			var destination = selectionCoreMotionTo;
			ClearSelectionCoreMotion();
			if (destination.IsValid && cells?.Contains(destination) == true)
				selectionCoreRelative = destination;
			else
				selectionCoreRelative = BestSelectionCoreRelative(selectionCoreRelative, selectionCoreDiscoveryCue);
		}

		void ClearSelectionCoreMotion()
		{
			selectionCoreMotionFrom = IntVec3.Invalid;
			selectionCoreMotionTo = IntVec3.Invalid;
			selectionCoreMotionStartTick = -1;
			selectionCoreMotionEndTick = -1;
		}

		bool IsSelectionCoreMotionActive(int ticks)
		{
			return selectionCoreMotionFrom.IsValid
				&& selectionCoreMotionTo.IsValid
				&& selectionCoreMotionStartTick >= 0
				&& selectionCoreMotionEndTick > selectionCoreMotionStartTick
				&& ticks < selectionCoreMotionEndTick;
		}

		float SelectionCoreMotionProgress(int ticks)
		{
			var linear = Mathf.Clamp01((ticks - selectionCoreMotionStartTick) / (float)Mathf.Max(1, selectionCoreMotionEndTick - selectionCoreMotionStartTick));
			return linear * linear * (3f - 2f * linear);
		}

		void ExpandCellBounds(IntVec3 relative)
		{
			if (hasCellBounds)
				relativeCellBounds = relativeCellBounds.Encapsulate(relative);
			else
			{
				relativeCellBounds = CellRect.SingleCell(relative);
				hasCellBounds = true;
			}
		}

		void RebuildCellBounds()
		{
			hasCellBounds = false;
			if (cells == null)
				return;
			foreach (var cell in cells)
				ExpandCellBounds(cell);
			UpdateDrawCullSize(relativeCellBounds);
		}

		void UpdateDrawCullSize()
		{
			UpdateDrawCullSize(relativeCellBounds);
		}

		void UpdateDrawCullSize(CellRect bounds)
		{
			if (hasCellBounds == false)
			{
				drawCullSize = Vector2.one;
				return;
			}

			var minX = bounds.minX - 1f;
			var maxX = bounds.maxX + 1f;
			var minZ = bounds.minZ - 1f;
			var maxZ = bounds.maxZ + 1f;
			var width = Mathf.Max(Mathf.Abs(minX), Mathf.Abs(maxX)) * 2f + 1f;
			var height = Mathf.Max(Mathf.Abs(minZ), Mathf.Abs(maxZ)) * 2f + 1f;
			drawCullSize = new Vector2(Mathf.Max(1f, width), Mathf.Max(1f, height));
		}

		int AddRelativeCells(IEnumerable<IntVec3> relatives)
		{
			var added = 0;
			foreach (var relative in relatives)
			{
				if (CellCount >= MaxCells)
					break;
				if (AddRelativeCell(relative))
					added++;
			}
			return added;
		}

		int AddCells(IEnumerable<IntVec3> newCells)
		{
			var added = AddRelativeCells(newCells.Select(cell => cell - Position));
			if (added > 0)
			{
				UpdateAll();
				UpdateSymbiosisState();
			}
			return added;
		}

		bool AddCell(IntVec3 newCell)
		{
			if (CellCount >= MaxCells)
				return false;
			if (AddRelativeCell(newCell - Position))
			{
				UpdateAll();
				UpdateSymbiosisState();
				return true;
			}
			return false;
		}

		public bool ContainsCell(IntVec3 absoluteCell)
		{
			if (hasCellBounds == false)
				return false;
			var relative = absoluteCell - Position;
			return relativeCellBounds.Contains(relative) && cells?.Contains(relative) == true;
		}

		public bool CanExpand()
		{
			return Spawned && Destroyed == false && Dead == false && CellCount < MaxCells;
		}

		bool TryReseedIfUprooted()
		{
			if (Spawned == false || Destroyed || Dead)
				return false;
			var linkedHost = ResolveHost();
			if (linkedHost == null)
			{
				uprootedSinceTick = -1;
				return false;
			}

			RefreshSymbiosisMetrics(true);
			if (cachedIntegratedVisibleCells > UprootedIntegratedCellThreshold)
			{
				uprootedSinceTick = -1;
				return false;
			}

			var ticks = GenTicks.TicksGame;
			if (uprootedSinceTick < 0)
			{
				uprootedSinceTick = ticks;
				return false;
			}
			if (ticks - uprootedSinceTick < UprootedRelocationGraceTicks)
				return false;

			if (TryFindReseedPlan(linkedHost, out var anchor, out var reseedCells) == false)
				return false;

			ReseedAt(anchor, reseedCells, linkedHost, Mathf.Max(0, CellCount - 1));
			return true;
		}

		bool TryFindReseedPlan(Pawn linkedHost, out IntVec3 anchor, out List<IntVec3> reseedCells)
		{
			anchor = IntVec3.Invalid;
			reseedCells = null;
			var map = Map;
			if (map == null)
				return false;

			var wantedCells = 1;
			foreach (var room in ReseedCandidateRooms(map, linkedHost))
			{
				if (TryBuildReseedCells(map, room, wantedCells, out anchor, out reseedCells))
					return true;
			}
			return false;
		}

		static IEnumerable<Room> ReseedCandidateRooms(Map map, Pawn linkedHost)
		{
			var hostRoom = linkedHost?.Spawned == true && linkedHost.Map == map ? linkedHost.Position.GetRoom(map) : null;
			if (IsEligibleIndoorRoom(hostRoom))
				yield return hostRoom;

			foreach (var room in CandidateRooms(map)
				.Where(room => room != hostRoom)
				.Select(room => new { room, score = ScoreSpawnRoom(map, room) })
				.Where(entry => entry.score > 0f)
				.OrderByDescending(entry => entry.score)
				.Select(entry => entry.room))
				yield return room;
		}

		bool HasReseedCandidateRoom(Pawn linkedHost)
		{
			var map = Map;
			if (map == null)
				return false;
			var hostRoom = linkedHost?.Spawned == true && linkedHost.Map == map ? linkedHost.Position.GetRoom(map) : null;
			return IsEligibleIndoorRoom(hostRoom) || CandidateRooms(map).Any();
		}

		static bool TryBuildReseedCells(Map map, Room room, int wantedCells, out IntVec3 anchor, out List<IntVec3> reseedCells)
		{
			anchor = IntVec3.Invalid;
			reseedCells = null;
			if (TryFindBestSpawnCell(map, room, out anchor, out _) == false)
				return false;

			var targetCount = Mathf.Clamp(wantedCells, 1, MaxCells);
			var root = anchor;
			var cellsInOrder = room.Cells
				.Where(cell => cell != root && CanOccupyOpenCell(map, cell))
				.OrderBy(cell => cell.DistanceToSquared(root))
				.ThenByDescending(cell => ScoreColonyCenterFallback(map, cell));
			reseedCells = [anchor];
			foreach (var cell in cellsInOrder)
			{
				if (reseedCells.Count >= targetCount)
					break;
				reseedCells.Add(cell);
			}
			return true;
		}

		void ReseedAt(IntVec3 anchor, List<IntVec3> reseedCells, Pawn linkedHost, int relocationDebt)
		{
			var map = Map;
			if (map == null || anchor.IsValid == false || reseedCells == null || reseedCells.Count == 0)
				return;

			var targetCells = reseedCells.Distinct().Take(MaxCells).ToArray();
			temporaryDespawnInProgress = true;
			try
			{
				DeSpawn(DestroyMode.Vanish);
			}
			finally
			{
				temporaryDespawnInProgress = false;
			}
			Position = anchor;
			cells = [];
			orderedCells = [];
			selectionCoreRelative = IntVec3.Invalid;
			selectionCoreLastMoveTick = GenTicks.TicksGame;
			ClearSelectionCoreMotion();
			recentMovementCells.Clear();
			combatShapeVersion++;
			hasCellBounds = false;
			AddRelativeCells(targetCells.Select(cell => cell - anchor));
			RebuildCellBounds();
			relocationCellDebt = Mathf.Clamp(relocationCellDebt + relocationDebt, 0, Mathf.Max(0, MaxCells - CellCount));
			nextRelocationPulseTick = GenTicks.TicksGame + RelocationPulseIntervalTicks();
			uprootedSinceTick = -1;
			lastSymbiosisMetricTick = int.MinValue;

			GenSpawn.Spawn(this, anchor, map, Rot4.Random, WipeMode.Vanish, false);
			RegisterActiveSymbiant(this, map);
			EnsureVisibleToPawnSystems(map);
			jobs.StartJob(JobMaker.MakeJob(CustomDefs.Symbiant));
			ResetExpansionClock();
			UpdateAll();
			UpdateSymbiosisState();
			RememberMovementCell(anchor);

			PlayConnectedSound();
			if (linkedHost != null)
				Messages.Message("SymbiantReseededMessage".Translate(linkedHost.LabelShortCap), new TargetInfo(anchor, map), MessageTypeDefOf.NeutralEvent, false);
		}

		void HandleUncontrolledDestroy()
		{
			if (uncontrolledDestroyHandled)
				return;
			uncontrolledDestroyHandled = true;

			EndLinkedHostBond(HostBondTermination.SymbiantRemoved);
		}

		void EndLinkedHostBond(HostBondTermination termination)
		{
			var pawn = ResolveHost();
			var lethalCollapseAuthorized = termination == HostBondTermination.SharedHealthExhausted
				&& sharedHealthFailureInProgress
				&& sharedHealth >= 0f
				&& sharedHealth <= 0.01f;
			var killHost = lethalCollapseAuthorized
				&& pawn != null
				&& pawn.Destroyed == false
				&& pawn.Dead == false
				&& IsActiveBondWith(pawn);
			if (pawn != null && pawn.Destroyed == false && pawn.Dead == false)
				PlayDisconnectedSound();
			host = null;
			hostThingId = null;
			symbiosisSevered = true;
			RemoveHostHediff(pawn);
			ClearDamageEchoHistory();
			if (killHost && pawn.Destroyed == false && pawn.Dead == false)
				pawn.Kill(null);
		}

		void CollapseFromSharedHealthFailure()
		{
			if (Destroyed || sharedHealthFailureInProgress || safeSeveranceInProgress || hostCollapseInProgress)
				return;
			sharedHealthFailureInProgress = true;
			try
			{
				EndLinkedHostBond(HostBondTermination.SharedHealthExhausted);
				Destroy(DestroyMode.Vanish);
			}
			finally
			{
				sharedHealthFailureInProgress = false;
			}
		}

		void CollapseFromHostDeath()
		{
			if (Destroyed || hostCollapseInProgress || safeSeveranceInProgress)
				return;
			hostCollapseInProgress = true;
			try
			{
				var pawn = ResolveHost();
				if (uncontrolledDestroyHandled == false)
					PlayDisconnectedSound();
				host = null;
				hostThingId = null;
				symbiosisSevered = true;
				RemoveHostHediff(pawn);
				ClearDamageEchoHistory();
				nextExpansionTick = GenTicks.TicksGame + RetreatIntervalTicks();
				UpdateSymbiosisState();
			}
			finally
			{
				hostCollapseInProgress = false;
			}
		}

		void PlayConnectedSound()
		{
			if (ZombieAwarenessCues.ShouldPlaySpecialZombieAmbientSound())
				CustomDefs.SymbiantConnected?.PlayOneShotOnCamera(null);
		}

		void PlayDisconnectedSound()
		{
			if (ZombieAwarenessCues.ShouldPlaySpecialZombieAmbientSound())
				CustomDefs.SymbiantDisconnected?.PlayOneShotOnCamera(null);
		}

		static LetterDef SymbiantEventLetterDef => CustomDefs.SymbiantEvent ?? CustomDefs.SymbiantConnection ?? LetterDefOf.PositiveEvent;

		void SendSymbiantEventLetter(TaggedString headline, TaggedString text, LookTargets targets)
		{
			if (Spawned == false || ZombieAwarenessCues.ShouldShowZombieEventLetter() == false)
				return;
			Find.LetterStack?.ReceiveLetter(headline, text, SymbiantEventLetterDef, targets);
		}

		void NotifyDamageAbsorbed()
		{
			if (Spawned == false || Map == null)
				return;
			var ticks = GenTicks.TicksGame;
			if (ticks - lastRejectedDamageMessageTick < 600)
				return;
			lastRejectedDamageMessageTick = ticks;
			Messages.Message("SymbiantWeaponRejectedMessage".Translate(DamageAbsorptionBuffer, DamageAbsorptionBufferMax), this, MessageTypeDefOf.RejectInput, false);
			MoteMaker.ThrowText(DrawPos, Map, "SymbiantWeaponRejectedMote".Translate(), 3.65f);
		}

		void NotifySharedDamageAbsorbed(float drained, float leaked, Thing target)
		{
			if (Spawned == false || Map == null || drained <= 0f)
				return;
			var ticks = GenTicks.TicksGame;
			if (ticks - lastSharedDamageAbsorbMoteTick < 60)
				return;
			var absorbedPercent = Mathf.Clamp(Mathf.RoundToInt((1f - Mathf.Clamp01(leaked / Mathf.Max(0.001f, drained))) * 100f), 0, 100);
			if (absorbedPercent <= 0)
				return;
			lastSharedDamageAbsorbMoteTick = ticks;
			MoteMaker.ThrowText(target == null ? DrawPos : target.DrawPos, Map, "SymbiantDamageAbsorbedMote".Translate(absorbedPercent), 3.65f);
		}

		public void PreApplyLinkedDamage(ref DamageInfo dinfo, ref bool absorbed)
		{
			if (safeSeveranceInProgress || hostCollapseInProgress)
				return;
			if (dinfo.Amount <= 0f)
				return;
			if (dinfo.Def == CustomDefs.SeismicWave)
			{
				dinfo.SetAmount(0f);
				absorbed = true;
				return;
			}
			if (IsPlayerCausedDamage(dinfo))
			{
				dinfo.SetAmount(0f);
				absorbed = true;
				NotifyDamageAbsorbed();
				return;
			}
		}

		internal void CompleteDamageApplication(
			DamageInfo dinfo,
			DamageWorker.DamageResult result,
			IReadOnlyDictionary<Hediff, float> hediffSeveritiesBefore)
		{
			if (result == null)
				return;
			ResolveDamageEchoCategory(dinfo, result, hediffSeveritiesBefore, out var categoryKey, out var categoryLabel);
			_ = PruneAnatomyOnlyDamageHediffs();
			var actualDamage = Mathf.Max(0f, result.totalDamageDealt);
			if (actualDamage <= 0f || Destroyed || Dead || safeSeveranceInProgress || hostCollapseInProgress)
				return;
			var drained = DrainSharedHealth(actualDamage);
			if (drained <= 0f || Destroyed || Dead)
				return;
			RecordDamageEcho(categoryKey, categoryLabel, drained);
			NotifySharedDamageAbsorbed(drained, 0f, this);
			SyncHostDamageEchoes();
		}

		void ResolveDamageEchoCategory(
			DamageInfo dinfo,
			DamageWorker.DamageResult result,
			IReadOnlyDictionary<Hediff, float> hediffSeveritiesBefore,
			out string categoryKey,
			out string categoryLabel)
		{
			var source = result.hediffs?.FirstOrDefault(hediff => hediff is Hediff_Injury)
				?? result.hediffs?.FirstOrDefault();
			if (source == null && health?.hediffSet?.hediffs != null && hediffSeveritiesBefore != null)
				source = health.hediffSet.hediffs.FirstOrDefault(hediff =>
					hediffSeveritiesBefore.TryGetValue(hediff, out var before) == false
					|| Mathf.Approximately(before, hediff.Severity) == false);
			if (source?.def != null)
			{
				categoryKey = "hediff:" + source.def.defName;
				categoryLabel = source.def.LabelCap.ToString();
				return;
			}
			if (dinfo.Def != null)
			{
				categoryKey = "damage:" + dinfo.Def.defName;
				categoryLabel = dinfo.Def.LabelCap.ToString();
				return;
			}
			categoryKey = SymbiantOtherDamageEchoKey;
			categoryLabel = null;
		}

		internal int PruneAnatomyOnlyDamageHediffs()
		{
			var hediffSet = health?.hediffSet;
			if (hediffSet?.hediffs == null)
				return 0;
			var removable = hediffSet.hediffs.Where(IsAnatomyOnlyDamageHediff).ToArray();
			foreach (var hediff in removable)
				health.RemoveHediff(hediff);
			return removable.Length;
		}

		static bool IsAnatomyOnlyDamageHediff(Hediff hediff)
		{
			if (hediff?.GetType() != typeof(Hediff_Injury))
				return false;
			var injury = (Hediff_Injury)hediff;
			if (injury.comps.NullOrEmpty())
				return true;
			return injury.comps.All(IsAnatomyOnlyDamageComp);
		}

		static bool IsAnatomyOnlyDamageComp(HediffComp comp)
		{
			var type = comp?.GetType();
			if (type == typeof(HediffComp_TendDuration)
				|| type == typeof(HediffComp_GetsPermanent)
				|| type == typeof(HediffComp_Infecter))
				return true;
			return type?.FullName == "CombatExtended.HediffComp_Stabilize"
				|| type?.FullName == "CombatExtended.HediffComp_InfecterCE";
		}

		void RecordDamageEcho(string categoryKey, string categoryLabel, float amount)
		{
			if (amount <= 0f)
				return;
			NormalizeDamageEchoHistory();
			categoryKey = categoryKey.NullOrEmpty() ? SymbiantOtherDamageEchoKey : categoryKey;
			var record = damageEchoHistory.FirstOrDefault(candidate => candidate.categoryKey == categoryKey);
			if (record == null && categoryKey != SymbiantOtherDamageEchoKey
				&& damageEchoHistory.Count(candidate => candidate.categoryKey != SymbiantOtherDamageEchoKey) >= SymbiantNamedDamageEchoLimit)
			{
				categoryKey = SymbiantOtherDamageEchoKey;
				categoryLabel = null;
				record = damageEchoHistory.FirstOrDefault(candidate => candidate.categoryKey == categoryKey);
			}
			if (record == null)
			{
				record = new SymbiantDamageEchoRecord
				{
					categoryKey = categoryKey,
					cachedLabel = categoryLabel,
					amount = 0f
				};
				damageEchoHistory.Add(record);
			}
			if (record.cachedLabel.NullOrEmpty() && categoryLabel.NullOrEmpty() == false)
				record.cachedLabel = categoryLabel;
			record.amount += amount;
		}

		void NormalizeDamageEchoHistory()
		{
			damageEchoHistory ??= [];
			var normalized = new List<SymbiantDamageEchoRecord>();
			var byKey = new Dictionary<string, SymbiantDamageEchoRecord>();
			var otherAmount = 0f;
			foreach (var source in damageEchoHistory)
			{
				if (source == null || source.amount <= 0f)
					continue;
				var key = source.categoryKey.NullOrEmpty() ? SymbiantOtherDamageEchoKey : source.categoryKey;
				if (key == SymbiantOtherDamageEchoKey)
				{
					otherAmount += source.amount;
					continue;
				}
				if (byKey.TryGetValue(key, out var existing))
				{
					existing.amount += source.amount;
					if (existing.cachedLabel.NullOrEmpty())
						existing.cachedLabel = source.cachedLabel;
					continue;
				}
				if (normalized.Count >= SymbiantNamedDamageEchoLimit)
				{
					otherAmount += source.amount;
					continue;
				}
				var copy = new SymbiantDamageEchoRecord
				{
					categoryKey = key,
					cachedLabel = source.cachedLabel,
					amount = source.amount
				};
				normalized.Add(copy);
				byKey.Add(key, copy);
			}
			if (otherAmount > 0f)
				normalized.Add(new SymbiantDamageEchoRecord
				{
					categoryKey = SymbiantOtherDamageEchoKey,
					amount = otherAmount
				});
			damageEchoHistory = normalized;
		}

		public bool HasDamageEchoCategory(string categoryKey)
		{
			return categoryKey.NullOrEmpty() == false
				&& damageEchoHistory?.Any(record => record?.categoryKey == categoryKey && record.amount > 0f) == true;
		}

		string DamageEchoCategoryLabel(SymbiantDamageEchoRecord record)
		{
			if (record == null || record.categoryKey == SymbiantOtherDamageEchoKey)
				return "SymbiantDamageEchoOther".Translate();
			var separator = record.categoryKey.IndexOf(':');
			if (separator > 0 && separator < record.categoryKey.Length - 1)
			{
				var kind = record.categoryKey.Substring(0, separator);
				var defName = record.categoryKey.Substring(separator + 1);
				if (kind == "hediff")
				{
					var hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail(defName);
					if (hediffDef != null)
						return hediffDef.LabelCap.ToString();
				}
				else if (kind == "damage")
				{
					var damageDef = DefDatabase<DamageDef>.GetNamedSilentFail(defName);
					if (damageDef != null)
						return damageDef.LabelCap.ToString();
				}
			}
			return record.cachedLabel.NullOrEmpty() ? "SymbiantDamageEchoOther".Translate() : record.cachedLabel;
		}

		public static string FormatDamageEchoAmount(float amount)
		{
			return Mathf.Max(0f, amount).ToString("0.#");
		}

		internal void SyncHostDamageEchoes(Pawn pawn = null)
		{
			if (DebugDisableHostHediffSync)
				return;
			pawn ??= ResolveHost();
			if (pawn?.health?.hediffSet == null)
				return;
			NormalizeDamageEchoHistory();
			var echoes = pawn.health.hediffSet.hediffs.OfType<Hediff_SymbiantDamageEcho>().ToArray();
			if (symbiosisSevered || CustomDefs.SymbiantDamageEcho == null || IsActiveBondWith(pawn) == false)
			{
				foreach (var echo in echoes)
					pawn.health.RemoveHediff(echo);
				return;
			}

			var expectedKeys = damageEchoHistory
				.Where(record => record?.amount > 0f)
				.Select(record => record.categoryKey)
				.ToHashSet();
			foreach (var stale in echoes.Where(echo => echo.symbiantThingId != ThingID || expectedKeys.Contains(echo.categoryKey) == false).ToArray())
				pawn.health.RemoveHediff(stale);

			foreach (var record in damageEchoHistory.Where(record => record?.amount > 0f))
			{
				var matches = pawn.health.hediffSet.hediffs
					.OfType<Hediff_SymbiantDamageEcho>()
					.Where(echo => echo.symbiantThingId == ThingID && echo.categoryKey == record.categoryKey)
					.ToArray();
				var echo = matches.FirstOrDefault();
				foreach (var duplicate in matches.Skip(1))
					pawn.health.RemoveHediff(duplicate);
				var isNew = echo == null;
				if (isNew)
				{
					echo = HediffMaker.MakeHediff(CustomDefs.SymbiantDamageEcho, pawn) as Hediff_SymbiantDamageEcho;
					if (echo == null)
						continue;
				}
				echo.symbiantThingId = ThingID;
				echo.categoryKey = record.categoryKey;
				echo.cachedCategoryLabel = DamageEchoCategoryLabel(record);
				echo.displayAmount = record.amount;
				echo.Severity = 0.001f;
				if (isNew)
					pawn.health.AddHediff(echo);
			}
		}

		void ClearDamageEchoHistory()
		{
			damageEchoHistory?.Clear();
		}

		static bool IsPlayerCausedDamage(DamageInfo dinfo)
		{
			var instigator = dinfo.Instigator;
			if (instigator == null)
				return false;
			if (instigator.Faction == Faction.OfPlayer)
				return true;
			if (instigator is Pawn pawn && pawn.Faction?.IsPlayer == true)
				return true;
			return false;
		}

		public bool TrySeverSymbiosis(Pawn pawn, Pawn doctor)
		{
			if (pawn == null || pawn != LinkedHost || CanSafelySever == false)
				return false;

			safeSeveranceInProgress = true;
			try
			{
				PlayDisconnectedSound();
				var targets = new LookTargets(this, pawn);
				Messages.Message("SymbiantSeveredMessage".Translate(pawn.LabelShortCap), pawn, MessageTypeDefOf.PositiveEvent, false);
				SendSymbiantEventLetter(
					"LetterLabelSymbiantBondRemoved".Translate(),
					"SymbiantBondRemovedLetter".Translate(pawn.LabelShortCap),
					targets
				);
				host = null;
				hostThingId = null;
				symbiosisSevered = true;
				RemoveHostHediff(pawn);
				ClearDamageEchoHistory();
				nextExpansionTick = GenTicks.TicksGame + RetreatIntervalTicks();
				return true;
			}
			finally
			{
				safeSeveranceInProgress = false;
			}
		}

		public void SymbiantTick()
		{
			if (DebugDisableSymbiantTick)
				return;
			var ticks = GenTicks.TicksGame;
			if (lastSymbiantTick == ticks)
				return;
			lastSymbiantTick = ticks;
			if (destroyWhenCellMotionsFinish && HasActiveCellMotions() == false)
			{
				Destroy(DestroyMode.Vanish);
				return;
			}
			TryRecoverSharedHealth(ticks);
			if (ticks % SymbiosisMetricRefreshInterval == Mathf.Abs(thingIDNumber % SymbiosisMetricRefreshInterval))
			{
				_ = PruneAnatomyOnlyDamageHediffs();
				EnsureHostLink();
				if (TryReseedIfUprooted())
					return;
				if (uprootedSinceTick < 0)
				{
					UpdateSymbiosisState(false);
					if (relocationCellDebt <= 0 && nextRelocationPulseTick <= 0 && HasMovableUnintegratedCells())
						nextRelocationPulseTick = ticks;
				}
			}
			if (uprootedSinceTick >= 0)
				return;
			if (ticks >= nextAutoHealTick)
			{
				TryAutoHealHost();
				nextAutoHealTick = ticks + AutoHealIntervalTicks;
			}
			if (symbiosisSevered || LinkedHost == null)
			{
				if (ticks >= nextExpansionTick)
				{
					_ = ShrinkCells(1, 0);
					nextExpansionTick = ticks + RetreatIntervalTicks();
				}
				return;
			}
			if (relocationCellDebt > 0 || (nextRelocationPulseTick > 0 && ticks >= nextRelocationPulseTick))
			{
				_ = TryRelocationPulse();
				return;
			}
			if (ticks >= nextMovementTick)
			{
				_ = TryMovePulse(false);
				ResetMovementClock();
			}
			if (CanExpand() && ticks >= nextExpansionTick)
			{
				_ = TryExpansionPulse();
				ResetExpansionClock();
			}
		}

		void TryAutoHealHost()
		{
			var healCount = BenefitCount(HostBenefit.AutoHeal);
			if (healCount <= 0)
				return;
			var linkedHost = LinkedHost;
			if (IsActiveBondWith(linkedHost) == false || linkedHost.health?.hediffSet == null)
				return;
			var injuries = linkedHost.health.hediffSet.hediffs
				.Where(IsAutoHealableHediff)
				.Cast<Hediff_Injury>()
				.OrderByDescending(injury => injury.Severity)
				.Take(healCount)
				.ToArray();
			foreach (var injury in injuries)
				injury.Heal(injury.Severity + 1f);
		}

		static bool IsAutoHealableHediff(Hediff hediff)
		{
			return hediff is Hediff_Injury injury
				&& injury.def != CustomDefs.ContaminationEffect
				&& injury.Severity > 0f
				&& injury.Part != null;
		}

		public static bool IsAutoHealableHediffForDebug(Hediff hediff) => IsAutoHealableHediff(hediff);

		void ResetExpansionClock()
		{
			nextExpansionTick = GenTicks.TicksGame + AutomaticExpansionIntervalTicks();
		}

		void ResetMovementClock()
		{
			nextMovementTick = GenTicks.TicksGame + MovementIntervalTicks();
		}

		int AutomaticExpansionIntervalTicks()
		{
			var days = DifficultyScaled(0.5f, 2f);
			var ticks = Mathf.RoundToInt(days * GenDate.TicksPerDay / SymbiantGrowthSpeedFactor());
			return Mathf.Max(GenDate.TicksPerHour, ticks);
		}

		int RetreatIntervalTicks()
		{
			return Mathf.Max(GenDate.TicksPerHour, AutomaticExpansionIntervalTicks() / SymbiantRetreatSpeedFactor);
		}

		int MovementIntervalTicks()
		{
			var hours = DifficultyScaled(1.25f, 0.35f);
			return Mathf.Max(CellMotionDurationTicks * 4, Mathf.RoundToInt(hours * GenDate.TicksPerHour));
		}

		int RelocationPulseIntervalTicks()
		{
			return Mathf.Max(GenDate.TicksPerHour / 2, MovementIntervalTicks() / 2);
		}

		public bool TryExpansionPulse()
		{
			if (CanExpand() == false)
				return false;

			var target = FindExpansionTarget(true, true);
			if (target == null)
				return false;

			if (target.wall != null && target.wall.Destroyed == false)
				target.wall.Destroy(DestroyMode.KillFinalize);

			if (AddCell(target.cell) == false)
				return false;
			RememberMovementCell(target.cell);
			return true;
		}

		public bool CanAcceptFeed(Thing feed)
		{
			return IsValidFeed(feed) && CanApplyFeedGrowth(FeedGrowthCells(feed));
		}

		bool CanApplyFeedGrowth(int pulseSize)
		{
			if (pulseSize <= 0 || CanExpand() == false)
				return false;
			if (HasExpansionTarget(true, true))
				return true;
			return cancelNextBreach && pulseSize > 1 && HasExpansionTarget(true, false);
		}

		bool HasExpansionTarget(bool allowWallBreak, bool respectCancelNextBreach)
		{
			var map = Map;
			if (map == null)
				return false;
			var hasWallTarget = false;
			foreach (var cell in orderedCells.Select(relative => Position + relative).ToArray())
			{
				for (var i = 0; i < 4; i++)
				{
					var direction = GenAdj.CardinalDirections[i];
					var candidate = cell + direction;
					if (candidate.InBounds(map) == false || ContainsCell(candidate))
						continue;
					if (IsValidSymbiantCell(map, candidate))
						return true;
					if (allowWallBreak == false)
						continue;
					var wall = BreakableConstructedWall(map, candidate);
					if (wall == null)
						continue;
					if (TryFindConstructedWallBreachDestination(map, candidate, direction, out _))
						hasWallTarget = true;
				}
			}
			return hasWallTarget && (respectCancelNextBreach == false || cancelNextBreach == false);
		}

		internal bool DebugExpansionPulse()
		{
			var expanded = TryExpansionPulse();
			if (expanded)
				ResetExpansionClock();
			return expanded;
		}

		internal bool DebugShrinkPulse()
		{
			var removed = ShrinkCells(1) > 0;
			if (removed && Destroyed == false)
				UpdateSymbiosisState();
			return removed;
		}

		public bool TryMovePulse(bool allowWallBreak)
		{
			var map = Map;
			if (map == null || CellCount <= 1)
				return false;

			RefreshSymbiosisMetrics(false);
			var targets = MovementTargetCandidates(map);
			if (targets.Count == 0)
				return false;

			if (ShouldUseAmbientMovement() && TryAmbientMovePulse(map, targets))
				return true;
			return TryCorrectiveMovePulse(map, targets);
		}

		bool ShouldUseAmbientMovement()
		{
			return CellCount >= 4
				&& cachedBenefitFactor >= AmbientMovementMinBenefitFactor
				&& relocationCellDebt <= 0
				&& HasMovableUnintegratedCells() == false;
		}

		bool TryCorrectiveMovePulse(Map map, List<MovementTarget> targets)
		{
			var target = targets
				.OrderByDescending(candidate => candidate.score)
				.FirstOrDefault();
			if (target == null)
				return false;
			var source = MovementSourceCandidates(map, target.cell - Position)
				.OrderBy(candidate => candidate.score)
				.FirstOrDefault();
			return source != null && TryCommitMove(map, source, target);
		}

		bool TryAmbientMovePulse(Map map, List<MovementTarget> targets)
		{
			var currentIntegrated = CalculateIntegratedVisibleCells(map);
			EnsureSelectionCoreState();
			var preferSelectionCore = selectionCoreDiscoveryCue == false
				&& selectionCoreRelative != IntVec3.Zero
				&& GenTicks.TicksGame - selectionCoreLastMoveTick >= SelectionCoreWanderDwellTicks;
			var bestScore = targets.Select(target => target.score).DefaultIfEmpty(0f).Max();
			var scoreFloor = Mathf.Min(bestScore, Mathf.Max(0.01f, bestScore * AmbientMovementTargetBestScoreFraction));
			if (preferSelectionCore)
			{
				foreach (var target in targets.OrderByDescending(AmbientTargetWeight))
				{
					var targetRelative = target.cell - Position;
					var source = MovementSourceCandidates(map, targetRelative)
						.Where(candidate => IsAmbientMoveAllowed(map, currentIntegrated, candidate, target))
						.FirstOrDefault(candidate => candidate.relative == selectionCoreRelative);
					if (source != null && TryCommitMove(map, source, target))
						return true;
				}
			}
			var targetPool = targets
				.Where(target => target.score >= scoreFloor)
				.OrderByDescending(target => AmbientTargetWeight(target))
				.Take(AmbientMovementCandidateLimit)
				.ToArray();
			foreach (var target in targetPool)
			{
				var targetRelative = target.cell - Position;
				var sourceCandidates = MovementSourceCandidates(map, targetRelative)
					.Where(candidate => IsAmbientMoveAllowed(map, currentIntegrated, candidate, target))
					.ToArray();
				var source = sourceCandidates
					.OrderByDescending(candidate => AmbientSourceWeight(candidate))
					.Take(AmbientMovementSourceLimit)
					.FirstOrDefault();
				if (source != null && TryCommitMove(map, source, target))
					return true;
			}
			return false;
		}

		internal bool DebugTrySelectionCoreWanderPulse()
		{
			var map = Map;
			if (map == null || CellCount <= 1)
				return false;
			EnsureSelectionCoreState();
			selectionCoreDiscoveryCue = false;
			selectionCoreLastMoveTick = GenTicks.TicksGame - SelectionCoreWanderDwellTicks;
			RefreshSymbiosisMetrics(false);
			var targets = MovementTargetCandidates(map);
			return targets.Count > 0 && TryAmbientMovePulse(map, targets);
		}

		List<MovementTarget> MovementTargetCandidates(Map map)
		{
			var targets = new List<MovementTarget>();
			var seen = new HashSet<IntVec3>();
			foreach (var cell in orderedCells.Select(relative => Position + relative).ToArray())
			{
				for (var i = 0; i < 4; i++)
				{
					var candidate = cell + GenAdj.CardinalDirections[i];
					if (candidate.InBounds(map) == false || ContainsCell(candidate) || seen.Add(candidate) == false)
						continue;
					if (IsValidSymbiantCell(map, candidate) == false)
						continue;
					targets.Add(new MovementTarget(candidate, ScoreMovementTargetCell(map, candidate), IntegratedCellWeight(map, candidate)));
				}
			}
			return targets;
		}

		IEnumerable<MovementSource> MovementSourceCandidates(Map map, IntVec3 targetRelative)
		{
			return orderedCells
				.Where(relative => relative != IntVec3.Zero)
				.Where(relative => WouldCellsStayConnectedAfterMove(relative, targetRelative))
				.Select(relative =>
				{
					var absolute = Position + relative;
					return new MovementSource(relative, absolute, ScoreMovementSourceCell(map, absolute), IntegratedCellWeight(map, absolute));
				});
		}

		bool IsAmbientMoveAllowed(Map map, float currentIntegrated, MovementSource source, MovementTarget target)
		{
			if (source == null || target == null)
				return false;
			var projectedIntegrated = currentIntegrated - source.integratedWeight + target.integratedWeight;
			var integrationFloor = Mathf.Min(
				currentIntegrated,
				Mathf.Max(cachedFullBenefitCells * AmbientMovementIntegrationFloorFactor, currentIntegrated - AmbientMovementMaxIntegrationLoss)
			);
			if (projectedIntegrated + 0.001f < integrationFloor)
				return false;
			return BreaksAmbientCenterLeash(map, source, target) == false;
		}

		bool BreaksAmbientCenterLeash(Map map, MovementSource source, MovementTarget target)
		{
			var center = ColonyCenterFallbackCell(map);
			if (center.IsValid == false)
				return false;
			var sourceDistance = Mathf.Sqrt(source.absolute.DistanceToSquared(center));
			var targetDistance = Mathf.Sqrt(target.cell.DistanceToSquared(center));
			if (targetDistance <= sourceDistance + AmbientMovementCenterSlack)
				return false;
			if (cachedBenefitFactor >= AmbientMovementHighBenefitFactor
				&& targetDistance <= sourceDistance + AmbientMovementHighBenefitCenterSlack
				&& target.score >= source.score * 0.75f)
				return false;
			return target.score < source.score + 10f;
		}

		float AmbientTargetWeight(MovementTarget target)
		{
			var weight = Mathf.Max(1f, target.score);
			return weight * Rand.Range(AmbientMovementTargetRandomMin, AmbientMovementTargetRandomMax);
		}

		float AmbientSourceWeight(MovementSource source)
		{
			var weight = 100f / Mathf.Max(1f, source.score + 1f);
			if (source.integratedWeight <= 0.5f)
				weight *= 1.5f;
			return weight * Rand.Range(AmbientMovementSourceRandomMin, AmbientMovementSourceRandomMax);
		}

		bool TryCommitMove(Map map, MovementSource source, MovementTarget target)
		{
			if (map == null || source == null || target == null || IsValidSymbiantCell(map, target.cell) == false)
				return false;
			var targetRelative = target.cell - Position;
			if (ContainsCell(target.cell) || WouldCellsStayConnectedAfterMove(source.relative, targetRelative) == false)
				return false;
			var movingSelectionCore = selectionCoreRelative == source.relative;
			if (RemoveRelativeCellWithCoreDestination(source.relative, true, movingSelectionCore ? targetRelative : IntVec3.Invalid) == false)
				return false;
			if (AddRelativeCell(targetRelative) == false)
			{
				_ = AddRelativeCell(source.relative);
				if (movingSelectionCore)
				{
					selectionCoreRelative = source.relative;
					ClearSelectionCoreMotion();
				}
				return false;
			}
			RebuildCellBounds();
			UpdateAll();
			UpdateSymbiosisState();
			RememberMovement(source.absolute, target.cell);
			return true;
		}

		void RememberMovement(IntVec3 source, IntVec3 target)
		{
			RememberMovementCell(source);
			RememberMovementCell(target);
		}

		void RememberMovementCell(IntVec3 cell)
		{
			if (cell.IsValid == false)
				return;
			recentMovementCells.Enqueue(cell);
			while (recentMovementCells.Count > AmbientMovementRecentCellCapacity)
				recentMovementCells.Dequeue();
		}

		bool IsRecentMovementCell(IntVec3 cell)
		{
			return cell.IsValid && recentMovementCells.Contains(cell);
		}

		internal bool DebugIsRecentMovementCell(IntVec3 cell) => IsRecentMovementCell(cell);

		internal void DebugRememberMovementCell(IntVec3 cell) => RememberMovementCell(cell);

		bool HasMovableUnintegratedCells()
		{
			var map = Map;
			return map != null
				&& CellCount > 1
				&& orderedCells.Any(relative => relative != IntVec3.Zero && IntegratedCellWeight(map, Position + relative) <= UprootedIntegratedCellThreshold);
		}

		bool TryMoveUnintegratedCell(Map map, ExpansionTarget target)
		{
			if (map == null || target == null || CellCount <= 1)
				return false;

			var targetRelative = target.cell - Position;
			var relative = orderedCells
				.AsEnumerable()
				.Reverse()
				.FirstOrDefault(cell => cell != IntVec3.Zero
					&& IntegratedCellWeight(map, Position + cell) <= UprootedIntegratedCellThreshold
					&& WouldCellsStayConnectedAfterMove(cell, targetRelative));
			if (relative == IntVec3.Zero || relative.IsValid == false || cells.Contains(relative) == false)
				return false;

			if (target.wall != null && target.wall.Destroyed == false)
				target.wall.Destroy(DestroyMode.KillFinalize);

			var source = Position + relative;
			var movingSelectionCore = selectionCoreRelative == relative;
			if (RemoveRelativeCellWithCoreDestination(relative, true, movingSelectionCore ? targetRelative : IntVec3.Invalid) == false)
				return false;
			if (AddRelativeCell(target.cell - Position) == false)
			{
				if (AddRelativeCell(relative))
				{
					if (movingSelectionCore)
					{
						selectionCoreRelative = relative;
						ClearSelectionCoreMotion();
					}
					cellMotions?.RemoveAll(motion => motion.cell == relative);
					RebuildCellBounds();
					UpdateAll();
					UpdateSymbiosisState();
				}
				return false;
			}
			RebuildCellBounds();
			UpdateAll();
			UpdateSymbiosisState();
			RememberMovement(source, target.cell);
			return true;
		}

		bool TryRelocationPulse()
		{
			var ticks = GenTicks.TicksGame;
			if (ticks < nextRelocationPulseTick)
				return false;

			var map = Map;
			var target = FindExpansionTarget(true, false);
			if (target == null)
			{
				nextRelocationPulseTick = ticks + RelocationPulseIntervalTicks();
				return false;
			}

			if (TryMoveUnintegratedCell(map, target))
			{
				nextRelocationPulseTick = relocationCellDebt > 0 || HasMovableUnintegratedCells() ? ticks + RelocationPulseIntervalTicks() : 0;
				return true;
			}

			if (relocationCellDebt <= 0 || CanExpand() == false)
			{
				nextRelocationPulseTick = 0;
				return false;
			}

			if (target.wall != null && target.wall.Destroyed == false)
				target.wall.Destroy(DestroyMode.KillFinalize);

			if (AddCell(target.cell) == false)
			{
				nextRelocationPulseTick = ticks + RelocationPulseIntervalTicks();
				return false;
			}
			RememberMovementCell(target.cell);
			relocationCellDebt = Mathf.Max(0, relocationCellDebt - 1);
			nextRelocationPulseTick = relocationCellDebt > 0 || HasMovableUnintegratedCells() ? ticks + RelocationPulseIntervalTicks() : 0;
			if (relocationCellDebt == 0)
				ResetExpansionClock();
			return true;
		}

		ExpansionTarget FindExpansionTarget(bool consumeCancelNextBreach, bool allowWallBreak = true)
		{
			var map = Map;
			var targets = new List<ExpansionTarget>();
			foreach (var cell in orderedCells.Select(relative => Position + relative).ToArray())
			{
				for (var i = 0; i < 4; i++)
				{
					var direction = GenAdj.CardinalDirections[i];
					var candidate = cell + direction;
					if (candidate.InBounds(map) == false || ContainsCell(candidate))
						continue;

					if (IsValidSymbiantCell(map, candidate))
					{
						targets.Add(new ExpansionTarget(candidate, null, ScoreExpansionCell(map, candidate)));
						continue;
					}

					if (allowWallBreak == false)
						continue;

					var wall = BreakableConstructedWall(map, candidate);
					if (wall == null)
						continue;

					if (TryFindConstructedWallBreachDestination(map, candidate, direction, out var destination))
						targets.Add(new ExpansionTarget(candidate, wall, ScoreExpansionCell(map, destination) + 150f));
				}
			}
			var openTarget = targets
				.Where(target => target.wall == null)
				.OrderByDescending(target => target.score)
				.FirstOrDefault();
			if (openTarget != null)
				return openTarget;

			var wallTarget = targets
				.Where(target => target.wall != null)
				.OrderByDescending(target => target.score)
				.FirstOrDefault();
			if (wallTarget != null && cancelNextBreach)
			{
				if (consumeCancelNextBreach)
					cancelNextBreach = false;
				return null;
			}
			return wallTarget;
		}

		bool TryFindConstructedWallBreachDestination(Map map, IntVec3 firstWallCell, IntVec3 direction, out IntVec3 destination)
		{
			destination = IntVec3.Invalid;
			var cell = firstWallCell;
			for (var depth = 0; depth <= MaxConstructedWallBreachDepth; depth++)
			{
				if (cell.InBounds(map) == false || ContainsCell(cell))
					return false;
				if (IsValidSymbiantCell(map, cell))
				{
					destination = cell;
					return true;
				}
				if (depth == MaxConstructedWallBreachDepth)
					return false;
				if (BreakableConstructedWall(map, cell) == null)
					return false;
				cell += direction;
			}
			return false;
		}

		internal static Building BreakableConstructedWall(Map map, IntVec3 cell)
		{
			var edifice = cell.GetEdifice(map);
			if (edifice == null || edifice is Building_Door)
				return null;
			if (edifice.def == null || edifice.def.IsWall == false || edifice.def.useHitPoints == false)
				return null;
			if (edifice.def.building.isNaturalRock || edifice.def.mineable)
				return null;
			if (edifice.Faction != Faction.OfPlayer)
				return null;
			return edifice;
		}

		static bool CanOccupyOpenCell(Map map, IntVec3 cell)
		{
			if (cell.InBounds(map) == false || cell.Fogged(map))
				return false;
			if (cell.Roofed(map) == false)
				return false;
			if (cell.Walkable(map) == false)
				return false;
			var room = cell.GetRoom(map);
			return IsEligibleIndoorRoom(room);
		}

		static bool IsDoorCell(Map map, IntVec3 cell)
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
				.Any(IsEligibleIndoorRoom);
		}

		float ScoreExpansionCell(Map map, IntVec3 cell)
		{
			return ScoreMovementTargetCell(map, cell) + Rand.Value;
		}

		float ScoreMovementTargetCell(Map map, IntVec3 cell)
		{
			var score = ScoreSpreadLocationCell(map, cell);
			return IsRecentMovementCell(cell) ? score - SymbiantRecentCellScoreAdjustment : score;
		}

		float ScoreMovementSourceCell(Map map, IntVec3 cell)
		{
			if (IsValidSymbiantCell(map, cell) == false)
				return float.MinValue;
			var score = ScoreSpreadLocationCell(map, cell);
			return IsRecentMovementCell(cell) ? score + SymbiantRecentCellScoreAdjustment : score;
		}

		float ScoreSpreadLocationCell(Map map, IntVec3 cell)
		{
			var traffic = ScoreTraffic(map, cell);
			var fallback = ScoreOpenFloorFallback(map, cell);
			var compactness = ScoreCompactness(cell);
			return Mathf.Max(traffic > 0f ? traffic + 1f : 0f, fallback)
				+ compactness
				- ScoreFurnitureCellPenalty(map, cell);
		}

		float ScoreCompactness(IntVec3 cell)
		{
			var relative = cell - Position;
			var cardinal = GenAdj.CardinalDirections.Count(direction => cells.Contains(relative + direction));
			var diagonal = GenAdj.DiagonalDirections.Count(direction => cells.Contains(relative + direction));
			return Mathf.Min(SymbiantCompactnessBonusMax, cardinal * SymbiantCompactnessCardinalBonus + diagonal * SymbiantCompactnessDiagonalBonus);
		}

		static float ScoreFurnitureCellPenalty(Map map, IntVec3 cell)
		{
			return cell.GetThingList(map).Any(IsSymbiantFurnitureCellThing) ? SymbiantFurnitureCellPenalty : 0f;
		}

		internal static bool IsSymbiantFurnitureCellThing(Thing thing)
		{
			return thing is Building_Bed
				|| thing is Building_WorkTable
				|| thing is Building_Storage
				|| thing?.def?.surfaceType == SurfaceType.Eat;
		}

		internal float DebugSpreadLocationScore(Map map, IntVec3 cell) => IsValidSymbiantCell(map, cell) ? ScoreSpreadLocationCell(map, cell) : 0f;

		internal static bool DebugIsValidSymbiantCell(Map map, IntVec3 cell) => IsValidSymbiantCell(map, cell);

		internal float DebugMovementTargetScore(Map map, IntVec3 cell) => IsValidSymbiantCell(map, cell) ? ScoreMovementTargetCell(map, cell) : 0f;

		internal float DebugMovementSourceScore(Map map, IntVec3 cell) => ScoreMovementSourceCell(map, cell);

		internal float DebugCompactnessScore(IntVec3 cell) => ScoreCompactness(cell);

		internal static float DebugTrafficScore(Map map, IntVec3 cell) => ScoreTraffic(map, cell);

		static float ScoreTraffic(Map map, IntVec3 cell)
		{
			var pheromone = map.GetGrid()?.GetPheromone(cell, false);
			if (pheromone == null || pheromone.timestamp <= 0)
				return 0f;
			var age = Mathf.Max(0f, ZombieLand.Tools.Ticks() - pheromone.timestamp);
			return Mathf.Max(0f, 300f - age / 200f);
		}

		static float ScoreColonyUse(Map map, IntVec3 cell)
		{
			var score = ScoreHomeArea(map, cell);
			score += cell.GetThingList(map).Sum(ScoreRoomThing);
			return score;
		}

		static float ScoreHomeArea(Map map, IntVec3 cell)
		{
			var home = map.areaManager.Home;
			return home.TrueCount == 0 || home[cell] ? 40f : 0f;
		}

		static float ScoreColonyCenterFallback(Map map, IntVec3 cell)
		{
			if (map == null || cell.InBounds(map) == false)
				return 0f;
			var score = ScoreColonyUse(map, cell);
			score += ScoreColonyCenterProximity(map, cell);
			return score + 0.01f;
		}

		static float ScoreOpenFloorFallback(Map map, IntVec3 cell)
		{
			if (map == null || cell.InBounds(map) == false)
				return 0f;
			return ScoreHomeArea(map, cell) + ScoreColonyCenterProximity(map, cell) + 0.01f;
		}

		static float ScoreColonyCenterProximity(Map map, IntVec3 cell)
		{
			var score = 0f;
			var colonyCenter = ColonyCenterFallbackCell(map);
			if (colonyCenter.IsValid)
			{
				var distance = Mathf.Sqrt(cell.DistanceToSquared(colonyCenter));
				score += Mathf.Max(0f, 120f - distance * 2f);
			}
			return score;
		}

		static Map cachedColonyCenterMap;
		static int cachedColonyCenterTick = -1;
		static IntVec3 cachedColonyCenter;

		static IntVec3 ColonyCenterFallbackCell(Map map)
		{
			var tick = GenTicks.TicksGame;
			if (map == cachedColonyCenterMap && tick == cachedColonyCenterTick)
				return cachedColonyCenter;
			var colonists = map?.mapPawns?.FreeColonistsSpawned;
			IntVec3 result;
			if (colonists == null || colonists.Count == 0)
				result = map?.Center ?? IntVec3.Invalid;
			else
			{
				var x = 0;
				var z = 0;
				for (var i = 0; i < colonists.Count; i++)
				{
					x += colonists[i].Position.x;
					z += colonists[i].Position.z;
				}
				result = new IntVec3(Mathf.RoundToInt(x / (float)colonists.Count), 0, Mathf.RoundToInt(z / (float)colonists.Count));
			}
			cachedColonyCenterMap = map;
			cachedColonyCenterTick = tick;
			cachedColonyCenter = result;
			return result;
		}

		static float ScoreRoomThing(Thing thing)
		{
			if (thing is Building_Bed bed)
				return bed.OwnersForReading?.Count > 0 ? 180f : 80f;
			if (thing is Building_NutrientPasteDispenser)
				return 140f;
			if (thing is Building_WorkTable)
				return 120f;
			if (thing is Building_Storage)
				return 90f;
			if (thing is Building_PowerSwitch || thing is Building_Battery || thing is Building_TempControl || thing is Building_Cooler || thing is Building_Heater)
				return 70f;
			return 0f;
		}

		public bool TryFeed(Thing feed)
		{
			if (CanAcceptFeed(feed) == false)
				return false;

			UpdateSymbiosisState();
			var pulseSize = FeedGrowthCells(feed);
			var added = 0;
			for (var i = 0; i < pulseSize; i++)
			{
				if (TryExpansionPulse())
					added++;
			}
			if (added <= 0)
				return false;
			var consumed = feed.stackCount > 1 ? feed.SplitOff(1) : feed;
			consumed.Destroy(DestroyMode.Vanish);
			lastRecessionPulseCells = added;
			UpdateSymbiosisState();
			if (Spawned)
			{
				CustomDefs.ZombieEating.PlayOneShot(SoundInfo.InMap(this));
				MoteMaker.ThrowText(DrawPos, Map, "SymbiantFedMote".Translate(pulseSize, added), 3.65f);
			}
			return true;
		}

		static int FeedGrowthCells(Thing feed)
		{
			var baseCells = BaseFeedGrowthCells(feed);
			if (baseCells <= 0)
				return 0;
			return Mathf.Max(1, Mathf.CeilToInt(baseCells * SymbiantGrowthSpeedFactor()));
		}

		static int BaseFeedGrowthCells(Thing feed)
		{
			if (feed is Corpse corpse)
			{
				var pawn = corpse.InnerPawn;
				var cells = pawn?.RaceProps?.Humanlike == true ? 2 : 1;
				if (corpse.GetRotStage() == RotStage.Fresh)
					cells++;
				return cells;
			}

			return 0;
		}

		public static int FeedGrowthCellCount(Thing feed)
		{
			return IsValidFeed(feed) ? FeedGrowthCells(feed) : 0;
		}

		public static bool IsValidFeed(Thing feed)
		{
			if (feed == null || feed.Destroyed)
				return false;
			if (feed is Corpse corpse)
			{
				var pawn = corpse.InnerPawn;
				if (pawn == null)
					return false;
				return pawn is not Zombie && pawn is not ZombieSymbiant && pawn is not ZombieSpitter;
			}
			return false;
		}

		bool TrySelectShrinkCell(int minRemainingCells, out IntVec3 relative)
		{
			relative = IntVec3.Invalid;
			if (orderedCells == null || orderedCells.Count <= minRemainingCells)
				return false;
			if (orderedCells.Count == 1)
			{
				relative = orderedCells[0];
				return true;
			}
			foreach (var cell in orderedCells.AsEnumerable().Reverse())
			{
				if (WouldCellsStayConnectedAfterRemoval(cell))
				{
					relative = cell;
					return true;
				}
			}
			return false;
		}

		public int ShrinkCells(int count)
		{
			return ShrinkCells(count, 0);
		}

		void ClearContaminationOnRemovedCell(IntVec3 relative)
		{
			if (Constants.CONTAMINATION == false)
				return;
			var map = Map;
			if (map == null)
				return;
			var cell = Position + relative;
			if (cell.InBounds(map) == false)
				return;
			foreach (var thing in cell.GetThingList(map).ToArray())
				thing.ClearContamination(map);
			map.SetContamination(cell, 0f, true);
			map.ContaminationGridUpdate();
		}

		int ShrinkCells(int count, int minRemainingCells)
		{
			if (destroyWhenCellMotionsFinish)
				return 0;
			EnsureSymbiantDefaults();
			destroyWhenCellMotionsFinish = false;
			var removed = 0;
			var minRemaining = Mathf.Clamp(minRemainingCells, 0, Mathf.Max(0, orderedCells.Count));
			while (removed < count && orderedCells.Count > minRemaining)
			{
				if (TrySelectShrinkCell(minRemaining, out var cell) == false)
					break;
				if (RemoveRelativeCell(cell, true))
				{
					ClearContaminationOnRemovedCell(cell);
					removed++;
				}
			}
			if (cells.Count == 0)
			{
				if (removed > 0 && HasActiveCellMotions())
				{
					destroyWhenCellMotionsFinish = true;
					UpdateAll();
					return removed;
				}
				Destroy(DestroyMode.Vanish);
				return removed;
			}
			if (removed > 0)
			{
				RebuildCellBounds();
				UpdateAll();
			}
			return removed;
		}

		void UpdateAll()
		{
			if (destroyWhenCellMotionsFinish)
			{
				cells ??= [];
				cellMotions ??= [];
				orderedCells ??= [];
			}
			else
				EnsureSymbiantDefaults();
			if (hasCellBounds == false)
				return;

			var renderBounds = RenderCellBounds();
			var min_x = renderBounds.minX - 1f;
			var min_z = renderBounds.minZ - 1f;
			var max_x = renderBounds.maxX + 1f;
			var max_z = renderBounds.maxZ + 1f;

			centerX = (min_x + max_x) / 2;
			centerZ = (min_z + max_z) / 2;
			UpdateDrawCullSize(renderBounds);

			var dx = max_x - min_x;
			var dz = max_z - min_z;
			renderMinX = min_x;
			renderMinZ = min_z;
			renderWidth = Mathf.Max(1f, dx);
			renderHeight = Mathf.Max(1f, dz);
			renderGeometryDirty = true;
			metaballTextureDirty = true;

			var allCells = cells.ToArray();
			var cellCount = Mathf.Min(allCells.Length, MAX_METABALLS);
			metaballRadiusByCell.Clear();

			for (var i = 0; i < cellCount; i++)
			{
				var cell = allCells[i];
				var cellRadius = Mathf.Clamp(GetSize(cell) * MetaballCellRadiusFactor, MetaballCellRadiusMin, MetaballCellRadiusMax);
				metaballRadiusByCell[cell] = cellRadius;
			}

			BuildMetaballRenderElements();
		}

		CellRect RenderCellBounds()
		{
			var bounds = relativeCellBounds;
			if (hasCellBounds == false)
				bounds = CellRect.SingleCell(IntVec3.Zero);
			if (cellMotions == null || cellMotions.Count == 0)
				return bounds;
			foreach (var motion in cellMotions)
			{
				bounds = bounds.Encapsulate(new IntVec3(Mathf.FloorToInt(Mathf.Min(motion.from.x, motion.to.x)), 0, Mathf.FloorToInt(Mathf.Min(motion.from.y, motion.to.y))));
				bounds = bounds.Encapsulate(new IntVec3(Mathf.CeilToInt(Mathf.Max(motion.from.x, motion.to.x)), 0, Mathf.CeilToInt(Mathf.Max(motion.from.y, motion.to.y))));
			}
			return bounds;
		}

		bool PruneFinishedCellMotions()
		{
			if (cellMotions == null || cellMotions.Count == 0)
				return false;
			var ticks = GenTicks.TicksGame;
			return cellMotions.RemoveAll(motion => ticks >= motion.endTick) > 0;
		}

		bool HasActiveCellMotions()
		{
			if (cellMotions == null || cellMotions.Count == 0)
				return false;
			var ticks = GenTicks.TicksGame;
			for (var i = 0; i < cellMotions.Count; i++)
				if (ticks < cellMotions[i].endTick)
					return true;
			return false;
		}

		int CountActiveCellMotions()
		{
			if (cellMotions == null || cellMotions.Count == 0)
				return 0;
			var ticks = GenTicks.TicksGame;
			var count = 0;
			for (var i = 0; i < cellMotions.Count; i++)
				if (ticks < cellMotions[i].endTick)
					count++;
			return count;
		}

		void UpdateAnimatedMetaballs()
		{
			if (cellMotions == null || cellMotions.Count == 0)
				return;
			var ticks = GenTicks.TicksGame;
			if (lastCellMotionRenderTick == ticks)
				return;
			lastCellMotionRenderTick = ticks;
			var removed = PruneFinishedCellMotions();
			if (removed && destroyWhenCellMotionsFinish && HasActiveCellMotions() == false)
			{
				lastCellMotionRenderTick = -1;
				return;
			}
			if (removed)
				UpdateAll();
			else
			{
				BuildMetaballRenderElements();
				metaballTextureDirty = true;
			}
			if (removed && (cellMotions == null || cellMotions.Count == 0))
				lastCellMotionRenderTick = -1;
		}

		void BuildMetaballRenderElements()
		{
			metaballRenderElements.Clear();
			var ticks = GenTicks.TicksGame;
			incomingCellMotions.Clear();
			cellMotionWeights.Clear();
			var hasActiveMotions = false;
			if (cellMotions != null)
			{
				for (var i = 0; i < cellMotions.Count; i++)
				{
					var motion = cellMotions[i];
					if (ticks >= motion.endTick)
						continue;
					hasActiveMotions = true;
					cellMotionWeights[motion.cell] = motion.CurrentRadiusScale(ticks);
					if (motion.outgoing == false)
						incomingCellMotions[motion.cell] = motion;
				}
			}

			foreach (var pair in metaballRadiusByCell)
			{
				var center = CellCenter(pair.Key);
				var radius = hasActiveMotions ? CellRenderRadius(pair.Key) : pair.Value;
				var radiusScale = 1f;
				if (incomingCellMotions.TryGetValue(pair.Key, out var motion))
				{
					center = motion.CurrentCenter(ticks);
					radiusScale = motion.CurrentRadiusScale(ticks);
				}
				AddMetaballRenderElement(center, radius, radiusScale);
			}

			if (cellMotions == null)
				return;
			for (var i = 0; i < cellMotions.Count; i++)
			{
				var motion = cellMotions[i];
				if (ticks < motion.endTick && motion.outgoing)
					AddMetaballRenderElement(motion.CurrentCenter(ticks), motion.radius, motion.CurrentRadiusScale(ticks));
			}
		}

		float CellRenderRadius(IntVec3 cell)
		{
			return Mathf.Clamp(GetVisualSize(cell) * MetaballCellRadiusFactor, MetaballCellRadiusMin, MetaballCellRadiusMax);
		}

		float GetVisualSize(IntVec3 cell)
		{
			var (x, y) = (cell.x, cell.z);
			var weightedNeighbors = 0f;
			for (var dx = -1; dx <= 1; dx++)
				for (var dy = -1; dy <= 1; dy++)
				{
					if (dx == 0 && dy == 0)
						continue;
					weightedNeighbors += VisualCellWeight(new IntVec3(x + dx, 0, y + dy));
				}
			return ElementSizeForNeighborWeight(weightedNeighbors);
		}

		float VisualCellWeight(IntVec3 cell)
		{
			if (cellMotionWeights.TryGetValue(cell, out var weight))
				return Mathf.Clamp01(weight);
			return cells.Contains(cell) ? 1f : 0f;
		}

		void AddMetaballRenderElement(Vector2 center, float radius, float radiusScale)
		{
			if (radius <= 0.0001f)
				return;
			var element = new MetaballRenderElement(center, radius, radiusScale);
			metaballRenderElements.Add(element);
		}

		void UpdateMetaballTexture()
		{
			if (metaballTexture == null || metaballMaskMaterial == null)
				return;

			UploadMetaballBuffer();
			metaballMaskMaterial.SetInt(MetaballCountId, metaballRenderElements.Count);
			metaballMaskMaterial.SetVector(MetaballWorldSizeId, new Vector4(renderWidth, renderHeight, renderMinX, renderMinZ));
			var previous = RenderTexture.active;
			try
			{
				Graphics.Blit(Texture2D.blackTexture, metaballTexture, metaballMaskMaterial);
			}
			finally
			{
				RenderTexture.active = previous;
			}
		}

		void UploadMetaballBuffer()
		{
			var count = metaballRenderElements.Count;
			EnsureMetaballBufferCapacity(Mathf.Max(1, count));
			if (metaballBufferData.Length < Mathf.Max(1, count))
				metaballBufferData = new MetaballBufferData[Mathf.NextPowerOfTwo(Mathf.Max(1, count))];

			for (var i = 0; i < count; i++)
			{
				var element = metaballRenderElements[i];
				var centerU = Mathf.Clamp01((element.center.x - renderMinX) / Mathf.Max(0.0001f, renderWidth));
				var centerV = Mathf.Clamp01((element.center.y - renderMinZ) / Mathf.Max(0.0001f, renderHeight));
				metaballBufferData[i] = new MetaballBufferData
				{
					shape = new Vector4(element.radius, element.radiusScale, Mathf.Max(0f, power), 0f),
					motion = new Vector4(centerU, centerV, 0f, 0f),
					tint = new Vector4(color.r, color.g, color.b, color.a)
				};
			}
			if (count == 0)
				metaballBufferData[0] = default;

			metaballBuffer.SetData(metaballBufferData, 0, 0, Mathf.Max(1, count));
			metaballMaskMaterial.SetBuffer(MetaballBufferId, metaballBuffer);
		}

		void EnsureMetaballBufferCapacity(int required)
		{
			required = Mathf.Max(1, required);
			if (metaballBuffer != null && metaballBufferCapacity >= required)
				return;
			metaballBuffer?.Release();
			metaballBufferCapacity = Mathf.NextPowerOfTwo(required);
			metaballBuffer = new ComputeBuffer(metaballBufferCapacity, MetaballBufferData.Stride);
		}

		static int DesiredMetaballTextureSize(float worldSize)
		{
			var desired = Mathf.CeilToInt(Mathf.Max(1f, worldSize) * MetaballTexturePixelsPerCell);
			return Mathf.Clamp(Mathf.NextPowerOfTwo(desired), MetaballTextureMinSize, MetaballTextureMaxSize);
		}

		void EnsureMetaballTextureResolution(float worldWidth, float worldHeight)
		{
			var textureWidth = DesiredMetaballTextureSize(worldWidth);
			var textureHeight = DesiredMetaballTextureSize(worldHeight);
			if (metaballTexture != null && metaballTexture.width == textureWidth && metaballTexture.height == textureHeight && metaballTexture.IsCreated())
				return;

			if (metaballTexture != null)
				UnityEngine.Object.Destroy(metaballTexture);
			metaballTexture = new RenderTexture(textureWidth, textureHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
			{
				name = $"ZombieSymbiantMetaballs_{textureWidth}x{textureHeight}",
				wrapMode = TextureWrapMode.Clamp,
				filterMode = FilterMode.Bilinear,
				useMipMap = false,
				autoGenerateMips = false
			};
			metaballTexture.Create();
			metaballTextureDirty = true;
			if (metaballMaterial != null)
				ConfigureMetaballMaterial();
			renderResourceOwners.Add(this);
		}

		void EnsureSymbiantDefaults()
		{
			var previousCellCount = cells?.Count ?? -1;
			var previousOrderedCount = orderedCells?.Count ?? -1;
			cells ??= [];
			cellMotions ??= [];
			if (cells.Count == 0 && destroyWhenCellMotionsFinish == false)
				cells.Add(IntVec3.Zero);
			orderedCells ??= [];
			if (orderedCells.Count == 0)
				orderedCells.AddRange(cells);
			else
			{
				foreach (var cell in cells)
				{
					if (orderedCells.Contains(cell) == false)
						orderedCells.Add(cell);
				}
				orderedCells.RemoveAll(cell => cells.Contains(cell) == false);
			}
			while (orderedCells.Count > MAX_METABALLS)
			{
				var cell = orderedCells[^1];
				orderedCells.RemoveAt(orderedCells.Count - 1);
				cells.Remove(cell);
			}
			if (previousCellCount != cells.Count || previousOrderedCount != orderedCells.Count)
				combatShapeVersion++;
			RebuildCellBounds();
			if (radius <= 0f)
				radius = elementRadius * 9f;
			if (power <= 0f)
				power = elementPower;
			if (nextExpansionTick <= 0)
				ResetExpansionClock();
			if (nextMovementTick <= 0)
				ResetMovementClock();
			if (nextAutoHealTick <= 0)
				nextAutoHealTick = GenTicks.TicksGame + AutoHealIntervalTicks;
			EnsureSelectionCoreState();
			if (selectionCoreLastMoveTick < 0)
				selectionCoreLastMoveTick = GenTicks.TicksGame;
			EnsureBenefitDefaults();
			if (uprootedSinceTick < -1)
				uprootedSinceTick = -1;
			relocationCellDebt = Mathf.Clamp(relocationCellDebt, 0, Mathf.Max(0, MaxCells - CellCount));
			if (relocationCellDebt > 0 && nextRelocationPulseTick <= 0)
				nextRelocationPulseTick = GenTicks.TicksGame + RelocationPulseIntervalTicks();
		}

		static bool CanUseMetaballRenderingNow(Map map)
		{
			if (DebugDisableRendering)
				return false;
			if (Assets.MetaballShader == null)
				return false;
			if (SystemInfo.supportsComputeShaders == false)
				return false;
			if (Current.Game == null || Current.ProgramState != ProgramState.Playing || Scribe.mode != LoadSaveMode.Inactive)
				return false;
			if (LongEventHandler.AnyEventNowOrWaiting || LongEventHandler.ShouldWaitForEvent)
				return false;
			return ZombieLand.Tools.MapViewActiveFor(map);
		}

		bool EnsureRenderResources()
		{
			if (CanUseMetaballRenderingNow(MapHeld) == false)
				return false;

			try
			{
				if (destroyWhenCellMotionsFinish)
				{
					cells ??= [];
					cellMotions ??= [];
					orderedCells ??= [];
				}
				else
					EnsureSymbiantDefaults();
				if (metaballTexture == null)
					EnsureMetaballTextureResolution(renderWidth, renderHeight);
				EnsureMetaballMaskMaterial();
				EnsureMetaballMaterial();
				EnsureSelectionCoreResources();
				if (metaballTexture != null || metaballMaterial != null || metaballMaskMaterial != null || mesh != null || selectionCoreMaterial != null || selectionCoreTexture != null || selectionCoreMesh != null)
					renderResourceOwners.Add(this);
				return metaballTexture != null && metaballMaterial != null && metaballMaskMaterial != null;
			}
			catch (Exception ex)
			{
				ReleaseRenderResources();
				Log.WarningOnce($"Zombieland disabled symbiant metaball rendering after a render-resource error: {ex}", 928376711);
				return false;
			}
		}

		bool TryPrepareMetaballRendering()
		{
			if (hasCellBounds == false)
				UpdateAll();
			if (hasCellBounds == false)
				return false;
			if (debugForceMetaballFallback)
				return false;
			if (EnsureRenderResources() == false)
				return false;

			EnsureMetaballTextureResolution(renderWidth, renderHeight);
			if (renderGeometryDirty || mesh == null)
			{
				if (mesh != null)
					UnityEngine.Object.Destroy(mesh);
				mesh = MeshMakerPlanes.NewPlaneMesh(new Vector2(renderWidth, renderHeight), false, false, false);
				renderGeometryDirty = false;
			}
			if (metaballTextureDirty)
			{
				UpdateMetaballTexture();
				metaballTextureDirty = false;
			}
			return mesh != null && metaballMaterial != null;
		}

		bool TryPrepareSelectionCoreRendering()
		{
			try
			{
				EnsureSelectionCoreState();
				EnsureSelectionCoreResources();
				if (selectionCoreMaterial != null || selectionCoreTexture != null || selectionCoreMesh != null)
					renderResourceOwners.Add(this);
				return selectionCoreMaterial != null && selectionCoreTexture != null && selectionCoreMesh != null;
			}
			catch (Exception ex)
			{
				ReleaseRenderResources();
				Log.WarningOnce($"Zombieland could not prepare the Symbiant selection core for fallback rendering: {ex}", 184463729);
				return false;
			}
		}

		void EnsureMetaballMaskMaterial()
		{
			var shader = Assets.MetaballShader;
			if (shader == null)
				return;
			if (metaballMaskMaterial == null || metaballMaskMaterial.shader != shader)
			{
				if (metaballMaskMaterial != null)
					UnityEngine.Object.Destroy(metaballMaskMaterial);
				metaballMaskMaterial = new Material(shader)
				{
					name = "ZombieSymbiantMetaballMask"
				};
			}
		}

		void EnsureMetaballMaterial()
		{
			var shader = Assets.ZombieSymbiantShader ?? ShaderDatabase.Transparent;
			if (metaballMaterial == null || metaballMaterial.shader != shader)
			{
				if (metaballMaterial != null)
					UnityEngine.Object.Destroy(metaballMaterial);
				metaballMaterial = new Material(shader)
				{
					name = "ZombieSymbiantMetaballs",
					color = Color.white,
					mainTexture = metaballTexture
				};
			}
			ConfigureMetaballMaterial();
		}

		void EnsureSelectionCoreResources()
		{
			if (selectionCoreTexture == null)
				selectionCoreTexture = CreateSelectionCoreTexture();
			if (selectionCoreMesh == null)
				selectionCoreMesh = MeshMakerPlanes.NewPlaneMesh(new Vector2(SelectionCoreVisualSize, SelectionCoreVisualSize), false, false, false);
			var shader = ShaderDatabase.Transparent;
			if (selectionCoreMaterial == null || selectionCoreMaterial.shader != shader)
			{
				if (selectionCoreMaterial != null)
					UnityEngine.Object.Destroy(selectionCoreMaterial);
				selectionCoreMaterial = new Material(shader)
				{
					name = "ZombieSymbiantSelectionCore",
					mainTexture = selectionCoreTexture
				};
			}
			selectionCoreMaterial.mainTexture = selectionCoreTexture;
			SetMaterialFloatIfPresent(selectionCoreMaterial, SymbiantOpacityMinId, 0.82f);
			SetMaterialFloatIfPresent(selectionCoreMaterial, SymbiantOpacityMaxId, 0.98f);
			SetMaterialFloatIfPresent(selectionCoreMaterial, SymbiantNoiseScaleId, SymbiantNoiseScale * 1.35f);
			SetMaterialFloatIfPresent(selectionCoreMaterial, SymbiantWavePhaseSpeedId, SymbiantWavePhaseSpeed * 0.65f);
			SetMaterialFloatIfPresent(selectionCoreMaterial, SymbiantWaveShadeStrengthId, 0.42f);
			SetMaterialFloatIfPresent(selectionCoreMaterial, SymbiantEdgeContrastId, 1f);
		}

		static Texture2D CreateSelectionCoreTexture()
		{
			var texture = new Texture2D(SelectionCoreTextureSize, SelectionCoreTextureSize, TextureFormat.RGBA32, false, true)
			{
				name = "ZombieSymbiantSelectionCore",
				filterMode = FilterMode.Bilinear,
				wrapMode = TextureWrapMode.Clamp
			};
			var pixels = new Color[SelectionCoreTextureSize * SelectionCoreTextureSize];
			var dark = new Color(0.02f, 0.36f, 0.025f, 1f);
			var light = new Color(0.12f, 0.78f, 0.08f, 1f);
			for (var y = 0; y < SelectionCoreTextureSize; y++)
			{
				for (var x = 0; x < SelectionCoreTextureSize; x++)
				{
					var px = (x + 0.5f) / SelectionCoreTextureSize * 2f - 1f;
					var py = (y + 0.5f) / SelectionCoreTextureSize * 2f - 1f;
					var distance = Mathf.Sqrt(px * px + py * py);
					var angle = Mathf.Atan2(py, px);
					var boundary = 0.84f + 0.06f * Mathf.Sin(angle * 3f + 0.4f) + 0.035f * Mathf.Sin(angle * 5f - 1.1f);
					var alphaMask = Mathf.Clamp01((boundary + 0.16f - distance) / 0.40f);
					var alpha = alphaMask * alphaMask * alphaMask * (alphaMask * (alphaMask * 6f - 15f) + 10f);
					var ring = Mathf.Clamp01(1f - Mathf.Abs(distance - 0.52f) / 0.22f);
					var veins = Mathf.Pow(Mathf.Clamp01(Mathf.Cos(angle * 3f + distance * 10f)), 10f) * Mathf.Clamp01(distance * 1.8f);
					var highlight = Mathf.Clamp01(ring * 0.55f + veins * 0.35f + (1f - distance) * 0.18f);
					var pixel = Color.Lerp(dark, light, highlight);
					pixel.a = alpha;
					pixels[y * SelectionCoreTextureSize + x] = pixel;
				}
			}
			texture.SetPixels(pixels);
			texture.Apply(false, true);
			return texture;
		}

		void ConfigureMetaballMaterial()
		{
			if (metaballMaterial == null)
				return;
			metaballMaterial.name = "ZombieSymbiantMetaballs";
			metaballMaterial.color = Color.white;
			metaballMaterial.mainTexture = metaballTexture;
			SetMaterialFloatIfPresent(metaballMaterial, SymbiantOpacityMinId, SymbiantOpacityMin);
			SetMaterialFloatIfPresent(metaballMaterial, SymbiantOpacityMaxId, SymbiantOpacityMax);
			SetMaterialFloatIfPresent(metaballMaterial, SymbiantNoiseScaleId, SymbiantNoiseScale);
			SetMaterialFloatIfPresent(metaballMaterial, SymbiantWavePhaseSpeedId, SymbiantWavePhaseSpeed);
			SetMaterialFloatIfPresent(metaballMaterial, SymbiantWaveShadeStrengthId, SymbiantWaveShadeStrength);
			SetMaterialFloatIfPresent(metaballMaterial, SymbiantEdgeContrastId, SymbiantEdgeContrast);
		}

		static void SetMaterialFloatIfPresent(Material material, int propertyId, float value)
		{
			if (material.HasProperty(propertyId))
				material.SetFloat(propertyId, value);
		}

		void UpdateMetaballMaterialTime()
		{
			if (metaballMaterial != null && metaballMaterial.HasProperty(SymbiantNoiseTimeId))
				metaballMaterial.SetFloat(SymbiantNoiseTimeId, RenderNoiseTimeSeconds);
			if (selectionCoreMaterial != null && selectionCoreMaterial.HasProperty(SymbiantNoiseTimeId))
				selectionCoreMaterial.SetFloat(SymbiantNoiseTimeId, RenderNoiseTimeSeconds);
		}

		void UpdateSelectionCoreInteractionState()
		{
			var map = MapHeld;
			var hovered = ZombieLand.Tools.MapViewActiveFor(map) && Find.CurrentMap == map && UI.MouseCell() == SelectionCoreCell;
			var selected = Find.Selector?.IsSelected(this) == true;
			var now = Time.realtimeSinceStartup;
			var deltaTime = selectionCoreInteractionLastRealtime < 0f
				? 1f / 60f
				: Mathf.Clamp(now - selectionCoreInteractionLastRealtime, 0f, 0.1f);
			selectionCoreInteractionLastRealtime = now;
			selectionCoreHoverBlend = Mathf.SmoothDamp(selectionCoreHoverBlend, hovered ? 1f : 0f, ref selectionCoreHoverVelocity, 0.14f, Mathf.Infinity, deltaTime);
			selectionCoreSelectedBlend = Mathf.SmoothDamp(selectionCoreSelectedBlend, selected ? 1f : 0f, ref selectionCoreSelectedVelocity, 0.16f, Mathf.Infinity, deltaTime);
			selectionCoreDiscoveryBlend = Mathf.SmoothDamp(selectionCoreDiscoveryBlend, selectionCoreDiscoveryCue ? 1f : 0f, ref selectionCoreDiscoveryVelocity, 0.18f, Mathf.Infinity, deltaTime);
		}

		void UpdateSelectedAppearance()
		{
			if (metaballMaterial == null)
				return;
			metaballMaterial.color = Color.Lerp(Color.white, new Color(1.08f, 1.08f, 1.08f, 1f), selectionCoreSelectedBlend);
			SetMaterialFloatIfPresent(metaballMaterial, SymbiantOpacityMinId, SymbiantOpacityMin + 0.06f * selectionCoreSelectedBlend);
			SetMaterialFloatIfPresent(metaballMaterial, SymbiantOpacityMaxId, SymbiantOpacityMax + 0.08f * selectionCoreSelectedBlend);
		}

		bool DrawSelectionCore(Vector3 drawLoc)
		{
			EnsureSelectionCoreState();
			if (selectionCoreMesh == null || selectionCoreMaterial == null || selectionCoreRelative.IsValid == false)
				return false;
			var amplitude = Mathf.Lerp(SelectionCoreSubtlePulseScale, SelectionCoreDiscoveryPulseScale, selectionCoreDiscoveryBlend);
			amplitude = Mathf.Lerp(amplitude, SelectionCoreHoverPulseScale, selectionCoreHoverBlend);
			var pulse = 1f + Mathf.Sin(Time.realtimeSinceStartup / SelectionCorePulseSeconds * Mathf.PI * 2f) * amplitude;
			pulse += 0.04f * selectionCoreSelectedBlend;
			var center = SelectionCoreVisualCenter;
			var position = drawLoc + new Vector3(center.x, 0f, center.y);
			position.y = AltitudeLayer.MoteLow.AltitudeFor(SymbiantRenderAltitudeOffset + 0.05f);
			var emphasis = Mathf.Max(selectionCoreHoverBlend, selectionCoreSelectedBlend);
			selectionCoreMaterial.color = Color.Lerp(Color.white, new Color(1.18f, 1.18f, 1.05f, 1f), emphasis);
			var rotation = Quaternion.Euler(0f, -Time.realtimeSinceStartup * SelectionCoreRotationDegreesPerSecond, 0f);
			var matrix = Matrix4x4.TRS(position, rotation, new Vector3(pulse, 1f, pulse));
			Graphics.DrawMesh(selectionCoreMesh, matrix, selectionCoreMaterial, 0);
			return true;
		}

		float GetSize(IntVec3 cell)
		{
			var (x, y) = (cell.x, cell.z);
			var count = 0;
			for (var dx = -1; dx <= 1; dx++)
				for (var dy = -1; dy <= 1; dy++)
				{
					if (dx == 0 && dy == 0)
						continue;
					if (cells.Contains(new IntVec3(x + dx, 0, y + dy)))
						count++;
				}
			return ElementSizeForNeighborWeight(count);
		}

		static float ElementSizeForNeighborWeight(float weightedNeighbors)
		{
			var clamped = Mathf.Clamp(weightedNeighbors, 0f, elementSizes.Length - 1f);
			var lower = Mathf.FloorToInt(clamped);
			var upper = Mathf.CeilToInt(clamped);
			return Mathf.Lerp(elementSizes[lower], elementSizes[upper], clamped - lower);
		}

		public override void DynamicDrawPhaseAt(DrawPhase phase, Vector3 drawLoc, bool flip = false)
		{
			if (DebugDisableRendering)
				return;
			if (phase == DrawPhase.Draw)
			{
				UpdateAnimatedMetaballs();
				DrawAt(drawLoc, flip);
			}
		}

		public override void DrawAt(Vector3 drawLoc, bool flip = false)
		{
			if (DebugDisableRendering)
				return;
			lastFallbackSelectionCoreDrawSucceeded = false;
			if (TryPrepareMetaballRendering() == false)
			{
				if (ZombieLand.Tools.MapViewActiveFor(MapHeld))
				{
					if (TryPrepareSelectionCoreRendering())
					{
						base.DrawAt(drawLoc, flip);
						UpdateMetaballMaterialTime();
						UpdateSelectionCoreInteractionState();
						lastFallbackSelectionCoreDrawSucceeded = DrawSelectionCore(drawLoc);
					}
					else
					{
						var center = SelectionCoreVisualCenter;
						base.DrawAt(drawLoc + new Vector3(center.x, 0f, center.y), flip);
					}
				}
				return;
			}

			var offset = new Vector3(centerX, 0, centerZ);
			var position = drawLoc + offset;
			position.y = AltitudeLayer.MoteLow.AltitudeFor(SymbiantRenderAltitudeOffset);
			UpdateMetaballMaterialTime();
			UpdateSelectionCoreInteractionState();
			UpdateSelectedAppearance();
			Graphics.DrawMesh(mesh, position, Quaternion.identity, metaballMaterial, 0);
			DrawSelectionCore(drawLoc);
		}

		public override string GetInspectString()
		{
			var linkedHost = LinkedHost;
			var hostLabel = linkedHost == null ? "None".Translate().ToString() : linkedHost.LabelShortCap;
			var summary = "ZombieSymbiantInspect".Translate(hostLabel, CellCount, SharedHealthSummary, NextBenefitCellSize).ToString();
			if (linkedHost == null)
				return summary + "\n" + "SymbiantHostBondMissing".Translate();
			if (IsActiveBondWith(linkedHost) == false)
				return summary + " — " + "LetterLabelSymbiantBondDormant".Translate();
			return summary;
		}

		public override string DescriptionDetailed
		{
			get
			{
				return DescriptionFlavor;
			}
		}

		public override string DescriptionFlavor
		{
			get
			{
				return AppendInfoCardDetails(base.DescriptionFlavor);
			}
		}

		string InfoCardDetails
		{
			get
			{
				var linkedHost = LinkedHost;
				if (linkedHost == null)
					return "SymbiantEffectCells".Translate(CellCount, MaxCells)
						+ "\n" + SharedHealthSummary
						+ "\n\n" + "SymbiantHostBondMissing".Translate();
				if (IsActiveBondWith(linkedHost) == false)
					return "ZombieSymbiantInspect".Translate(linkedHost.LabelShortCap, CellCount, SharedHealthSummary, NextBenefitCellSize)
						+ "\n\n" + "SymbiantHostRelocatedMessage".Translate(linkedHost.LabelShortCap);
				var hostLabel = linkedHost.LabelShortCap;
				return "ZombieSymbiantInfoCardDetails".Translate(CellCount, NextBenefitCellSize, DownsideSummary, BenefitSummary, SharedHealthSummary, SharedDamageLeakPercentDisplay, hostLabel);
			}
		}

		string AppendInfoCardDetails(string baseDescription)
		{
			return (baseDescription ?? def?.description ?? "") + "\n\n" + InfoCardDetails;
		}

		public override IEnumerable<StatDrawEntry> SpecialDisplayStats()
		{
			foreach (var entry in base.SpecialDisplayStats())
				yield return entry;
			yield return new StatDrawEntry(
				StatCategoryDefOf.BasicsImportant,
				"SymbiantDetailsInfoCard".Translate(),
				"SymbiantDetailsInfoCardValue".Translate(CellCount),
				InfoCardDetails,
				99998
			);
		}

		public override IEnumerable<InspectTabBase> GetInspectTabs()
		{
			return Enumerable.Empty<InspectTabBase>();
		}

		public override IEnumerable<Gizmo> GetGizmos()
		{
			foreach (var gizmo in base.GetGizmos())
				yield return gizmo;
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Collections.Look(ref cells, "cells", LookMode.Value);
			Scribe_Collections.Look(ref orderedCells, "orderedCells", LookMode.Value);
			Scribe_Values.Look(ref radius, "radius", elementRadius * 9f);
			Scribe_Values.Look(ref power, "power", elementPower);
			Scribe_Values.Look(ref nextExpansionTick, "nextExpansionTick");
			Scribe_Values.Look(ref nextMovementTick, "nextMovementTick");
			Scribe_Values.Look(ref nextAutoHealTick, "nextAutoHealTick");
			Scribe_Values.Look(ref nextBenefitCellThreshold, "nextBenefitCellThreshold");
			Scribe_Values.Look(ref benefitStepCells, "benefitStepCells");
			Scribe_Values.Look(ref feedPausedUntilTick, "feedPausedUntilTick");
			Scribe_Values.Look(ref lastRecessionPulseCells, "lastRecessionPulseCells");
			Scribe_Values.Look(ref relocationCellDebt, "relocationCellDebt");
			Scribe_Values.Look(ref nextRelocationPulseTick, "nextRelocationPulseTick");
			Scribe_Values.Look(ref uprootedSinceTick, "uprootedSinceTick", -1);
			Scribe_Values.Look(ref cancelNextBreach, "cancelNextBreach");
			Scribe_Values.Look(ref selectionCoreRelative, "selectionCoreRelative", IntVec3.Invalid);
			Scribe_Values.Look(ref selectionCoreMotionFrom, "selectionCoreMotionFrom", IntVec3.Invalid);
			Scribe_Values.Look(ref selectionCoreMotionTo, "selectionCoreMotionTo", IntVec3.Invalid);
			Scribe_Values.Look(ref selectionCoreMotionStartTick, "selectionCoreMotionStartTick", -1);
			Scribe_Values.Look(ref selectionCoreMotionEndTick, "selectionCoreMotionEndTick", -1);
			Scribe_Values.Look(ref selectionCoreLastMoveTick, "selectionCoreLastMoveTick", -1);
			Scribe_Values.Look(ref selectionCoreDiscoveryCue, "selectionCoreDiscoveryCue");
			Scribe_Values.Look(ref sharedHealth, "sharedHealth", -1f);
			Scribe_Values.Look(ref lastSharedHealthDamageTick, "lastSharedHealthDamageTick", int.MinValue);
			Scribe_Values.Look(ref nextSharedHealthRecoveryTick, "nextSharedHealthRecoveryTick");
			Scribe_Values.Look(ref destroyWhenCellMotionsFinish, "destroyWhenCellMotionsFinish");
			Scribe_References.Look(ref host, "host");
			Scribe_Values.Look(ref hostThingId, "hostThingId");
			Scribe_Values.Look(ref symbiosisSevered, "symbiosisSevered");
			Scribe_Collections.Look(ref hostBenefits, "hostBenefits", LookMode.Value);
			Scribe_Collections.Look(ref damageEchoHistory, "damageEchoHistory", LookMode.Deep);
			if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
				if (CellCount == 0)
					destroyWhenCellMotionsFinish = true;
				EnsureSymbiantDefaults();
				if (PruneDisconnectedCells() > 0)
					RebuildCellBounds();
				if (host != null)
					hostThingId = host.ThingID;
				UpdateSymbiosisState();
				EnsureBenefitDefaults();
				EnsureHostHediff();
				EnsureSharedHealth();
				NormalizeDamageEchoHistory();
				_ = PruneAnatomyOnlyDamageHediffs();
				if (sharedHealth < SharedHealthMax - 0.01f && nextSharedHealthRecoveryTick <= 0)
					nextSharedHealthRecoveryTick = GenTicks.TicksGame + SymbiantSharedHealthRecoveryDelayTicks;
				SyncHostDamageEchoes();
			}
		}

		sealed class ExpansionTarget
		{
			public readonly IntVec3 cell;
			public readonly Building wall;
			public readonly float score;

			public ExpansionTarget(IntVec3 cell, Building wall, float score)
			{
				this.cell = cell;
				this.wall = wall;
				this.score = score;
			}
		}

		sealed class MovementTarget
		{
			public readonly IntVec3 cell;
			public readonly float score;
			public readonly float integratedWeight;

			public MovementTarget(IntVec3 cell, float score, float integratedWeight)
			{
				this.cell = cell;
				this.score = score;
				this.integratedWeight = integratedWeight;
			}
		}

		sealed class MovementSource
		{
			public readonly IntVec3 relative;
			public readonly IntVec3 absolute;
			public readonly float score;
			public readonly float integratedWeight;

			public MovementSource(IntVec3 relative, IntVec3 absolute, float score, float integratedWeight)
			{
				this.relative = relative;
				this.absolute = absolute;
				this.score = score;
				this.integratedWeight = integratedWeight;
			}
		}

		sealed class CellMotion
		{
			public readonly IntVec3 cell;
			public readonly Vector2 from;
			public readonly Vector2 to;
			public readonly int startTick;
			public readonly int endTick;
			public readonly float radius;
			public readonly bool outgoing;

			public CellMotion(IntVec3 cell, Vector2 from, Vector2 to, int startTick, int endTick, float radius, bool outgoing)
			{
				this.cell = cell;
				this.from = from;
				this.to = to;
				this.startTick = startTick;
				this.endTick = endTick;
				this.radius = radius;
				this.outgoing = outgoing;
			}

			public Vector2 CurrentCenter(int ticks)
			{
				var progress = SmoothProgress(ticks);
				return Vector2.Lerp(from, to, progress);
			}

			public float CurrentRadiusScale(int ticks)
			{
				var progress = LinearProgress(ticks);
				return outgoing ? (1f - progress) * (1f - progress) : progress * progress;
			}

			float LinearProgress(int ticks)
			{
				return Mathf.Clamp01((ticks - startTick) / (float)Mathf.Max(1, endTick - startTick));
			}

			float SmoothProgress(int ticks)
			{
				var progress = LinearProgress(ticks);
				return progress * progress * (3f - 2f * progress);
			}
		}

		struct MetaballRenderElement
		{
			public readonly Vector2 center;
			public readonly float radius;
			public readonly float radiusScale;

			public MetaballRenderElement(Vector2 center, float radius, float radiusScale)
			{
				this.center = center;
				this.radius = radius;
				this.radiusScale = Mathf.Clamp01(radiusScale);
			}
		}

		[StructLayout(LayoutKind.Sequential)]
		struct MetaballBufferData
		{
			public const int Stride = sizeof(float) * 12;
			public Vector4 shape;
			public Vector4 motion;
			public Vector4 tint;
		}
	}
}
