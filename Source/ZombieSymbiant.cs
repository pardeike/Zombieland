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
		const float SymbiantRoomEstablishmentCoverage = 0.25f;
		const float ConstructedWallPreferenceThreshold = 0.50f;
		const float NaturalWallRoomScoreFactor = 1.00f;
		const float ConstructedWallRoomScoreFactor = 2.00f;
		static int UprootedRelocationGraceTicks => GenDate.TicksPerHour * 4;
		static int PlacementBlockedRetryTicks => GenDate.TicksPerHour * 6;
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
		const int SelectionCoreConnectivityCandidateLimit = 12;
		const int AmbientMovementCandidateLimit = 12;
		const int AmbientMovementSourceLimit = 8;
		const float AmbientMovementMinBenefitFactor = 0.55f;
		const float AmbientMovementHighBenefitFactor = 0.85f;
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
		static readonly int MainTextureId = Shader.PropertyToID("_MainTex");
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

		internal enum SymbiantCellClass
		{
			IndoorFloor,
			Door,
			ExteriorOpen,
			IndoorIneligible,
			InvalidBlocked
		}

		internal enum IndoorCapacityState
		{
			NoRelevantRooms,
			PlacementAvailable,
			NonFullButBlocked,
			AllFull
		}

		enum FootprintMutationKind
		{
			Expansion,
			Feeding,
			Movement,
			MigrationRepair,
			Relocation,
			ConstructionRepair,
			Reseed,
			Retreat,
			Debug
		}

		enum ExpansionTargetKind
		{
			IndoorLocal,
			Door,
			RoomFounding,
			ExteriorOpen,
			ExteriorWallBreach
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

		sealed class RoomCapacityRecord
		{
			public Room room;
			public int capacity;
			public int occupied;
			public bool hasPlacement;
			public IntVec3 placementCell = IntVec3.Invalid;
			public float placementScore;
			public float roomScore;

			public bool Empty => occupied == 0;
			public bool Full => capacity > 0 && occupied >= capacity;
			public float ProjectedCoverage => capacity <= 0 ? 1f : (occupied + 1f) / capacity;
		}

		sealed class IndoorCapacityEvaluation
		{
			public IndoorCapacityState state;
			public readonly List<RoomCapacityRecord> rooms = [];
			public IntVec3 doorTarget = IntVec3.Invalid;
			public float doorTargetScore;
			public int roomCellScans;
			public int TotalCapacity => rooms.Sum(room => room.capacity);
			public int TotalOccupied => rooms.Sum(room => room.occupied);
			public bool HasDoorTarget => doorTarget.IsValid;
		}

		sealed class ConstructionRoomPlacementPlan
		{
			public readonly RoomCapacityRecord capacity = new();
			public readonly HashSet<IntVec3> freeCells = [];
			public readonly HashSet<IntVec3> frontier = [];
			public readonly List<IntVec3> foundingCells = [];
			public HashSet<IntVec3> excludedEstablishedSources;
		}

		sealed class ConstructionPlacementCandidate
		{
			public ConstructionRoomPlacementPlan room;
			public IntVec3 cell;
			public float score;
		}

		sealed class ConstructionPlacementPlanner
		{
			readonly ZombieSymbiant owner;
			readonly Map map;
			readonly HashSet<IntVec3> planned;
			readonly HashSet<IntVec3> excluded;
			readonly List<ConstructionRoomPlacementPlan> rooms = [];
			readonly HashSet<IntVec3> doorTargets = [];

			public ConstructionPlacementPlanner(
				ZombieSymbiant owner,
				Map map,
				HashSet<IntVec3> planned,
				HashSet<IntVec3> excluded)
			{
				this.owner = owner;
				this.map = map;
				this.planned = planned;
				this.excluded = excluded;
				BuildRoomPlans();
				RefreshDoorTargets(planned);
			}

			void BuildRoomPlans()
			{
				var queuedAbsolute = owner.roomCellMigrationLookup
					.Select(relative => owner.Position + relative)
					.ToHashSet();
				var relevantRooms = CandidateRooms(map)
					.Concat(planned
						.Where(cell => cell.InBounds(map))
						.Select(cell => cell.GetRoom(map))
						.Where(IsEligibleIndoorRoom))
					.Distinct()
					.ToArray();
				foreach (var room in relevantRooms)
				{
					var plan = new ConstructionRoomPlacementPlan();
					plan.capacity.room = room;
					plan.capacity.roomScore = ScoreSpawnRoom(map, room);
					var hasEstablishedCell = planned.Any(cell =>
						cell.InBounds(map)
						&& cell.GetRoom(map) == room
						&& queuedAbsolute.Contains(cell) == false);
					plan.excludedEstablishedSources = hasEstablishedCell ? queuedAbsolute : [];
					foreach (var cell in room.Cells)
					{
						owner.roomCellScanCount++;
						owner.constructionPlacementCandidateScanCount++;
						if (ClassifySymbiantCell(map, cell) != SymbiantCellClass.IndoorFloor)
							continue;
						plan.capacity.capacity++;
						if (planned.Contains(cell))
							plan.capacity.occupied++;
						else if (excluded.Contains(cell) == false)
							plan.freeCells.Add(cell);
					}
					if (plan.capacity.capacity <= 0)
						continue;
					foreach (var cell in plan.freeCells)
					{
						if (plan.capacity.occupied > 0)
						{
							if (owner.TouchesRoomPatch(map, cell, room, planned, plan.excludedEstablishedSources))
								plan.frontier.Add(cell);
						}
						else if (CanOccupyInitialSpawnCell(map, cell))
							plan.foundingCells.Add(cell);
					}
					if (plan.capacity.occupied == 0 && plan.foundingCells.Count == 0)
						plan.foundingCells.AddRange(plan.freeCells.Where(cell => CanOccupyFurnishedFoundingCell(map, cell)));
					rooms.Add(plan);
				}
			}

			ConstructionPlacementCandidate CandidateFor(ConstructionRoomPlacementPlan plan)
			{
				IEnumerable<IntVec3> candidates = plan.capacity.occupied == 0 ? plan.foundingCells : plan.frontier;
				var cell = candidates
					.Where(plan.freeCells.Contains)
					.OrderByDescending(candidate => owner.ScoreMovementTargetCell(map, candidate))
					.ThenBy(candidate => candidate.x)
					.ThenBy(candidate => candidate.z)
					.FirstOrDefault();
				if (cell.IsValid == false || plan.freeCells.Contains(cell) == false)
					return null;
				return new ConstructionPlacementCandidate
				{
					room = plan,
					cell = cell,
					score = owner.ScoreMovementTargetCell(map, cell)
				};
			}

			public IntVec3 NextTarget(Room preferredRoom = null)
			{
				var candidates = rooms
					.Select(CandidateFor)
					.Where(candidate => candidate != null)
					.ToArray();
				var preferred = preferredRoom == null
					? null
					: candidates
						.Where(candidate => candidate.room.capacity.room == preferredRoom)
						.OrderByDescending(candidate => candidate.score)
						.FirstOrDefault();
				if (preferred != null)
					return preferred.cell;
				var selected = candidates
					.OrderBy(candidate => candidate.room.capacity.Empty ? 0 : 1)
					.ThenBy(candidate => candidate.room.capacity.ProjectedCoverage)
					.ThenByDescending(candidate => candidate.room.capacity.roomScore)
					.ThenByDescending(candidate => candidate.score)
					.ThenBy(candidate => candidate.cell.x)
					.ThenBy(candidate => candidate.cell.z)
					.FirstOrDefault();
				if (selected != null)
					return selected.cell;
				return doorTargets
					.Where(cell => ClassifySymbiantCell(map, cell) == SymbiantCellClass.Door)
					.OrderByDescending(cell => owner.ScoreMovementTargetCell(map, cell))
					.ThenBy(cell => cell.x)
					.ThenBy(cell => cell.z)
					.DefaultIfEmpty(IntVec3.Invalid)
					.First();
			}

			public bool Commit(IntVec3 target)
			{
				if (target.IsValid == false || excluded.Contains(target) || planned.Add(target) == false)
					return false;
				doorTargets.Remove(target);
				var plan = rooms.FirstOrDefault(candidate => candidate.freeCells.Contains(target));
				if (plan != null)
				{
					plan.freeCells.Remove(target);
					plan.frontier.Remove(target);
					plan.foundingCells.Remove(target);
					plan.capacity.occupied++;
					foreach (var direction in GenAdj.CardinalDirections)
					{
						var neighbor = target + direction;
						if (plan.freeCells.Contains(neighbor) && neighbor.GetRoom(map) == plan.capacity.room)
							plan.frontier.Add(neighbor);
					}
				}
				RefreshDoorTargets([target]);
				return true;
			}

			void RefreshDoorTargets(IEnumerable<IntVec3> sources)
			{
				var relevantRooms = rooms.Select(plan => plan.capacity.room).ToHashSet();
				foreach (var source in sources)
				{
					foreach (var direction in GenAdj.CardinalDirections)
					{
						var candidate = source + direction;
						if (planned.Contains(candidate)
							|| excluded.Contains(candidate)
							|| ClassifySymbiantCell(map, candidate) != SymbiantCellClass.Door)
							continue;
						var belongsToRelevantRoom = GenAdj.CardinalDirections
							.Select(adjacentDirection => candidate + adjacentDirection)
							.Where(adjacent => adjacent.InBounds(map))
							.Select(adjacent => adjacent.GetRoom(map))
							.Any(relevantRooms.Contains);
						if (belongsToRelevantRoom)
							doorTargets.Add(candidate);
					}
				}
			}
		}

		sealed class MetaballRenderPatch
		{
			public readonly HashSet<IntVec3> cells = [];
			public readonly List<MetaballRenderElement> elements = [];
			public CellRect bounds;
			public bool hasBounds;
			public float centerX;
			public float centerZ;
			public float renderMinX;
			public float renderMinZ;
			public float renderWidth = 1f;
			public float renderHeight = 1f;
			public RenderTexture texture;
			public Mesh mesh;
			public bool geometryDirty = true;
			public bool textureDirty = true;
		}

		HashSet<IntVec3> cells = [];
		List<IntVec3> orderedCells = [];
		List<IntVec3> roomCellMigrationCells = [];
		readonly HashSet<IntVec3> roomCellMigrationLookup = [];
		bool roomCellMigrationInitialized;
		bool roomCellMigrationRescanPending;
		bool roomCellMigrationNormalizationPending;
		bool roomTopologyInvalidated;
		readonly HashSet<IntVec3> pendingConstructionCoveredCells = [];
		readonly HashSet<IntVec3> pendingConstructionFootprintCells = [];
		bool postLoadConstructionValidationPending;
		readonly Dictionary<IntVec3, float> metaballRadiusByCell = [];
		readonly List<MetaballRenderElement> metaballRenderElements = [];
		readonly Dictionary<IntVec3, CellMotion> incomingCellMotions = [];
		readonly Dictionary<IntVec3, float> cellMotionWeights = [];
		readonly Queue<IntVec3> recentMovementCells = new();
		readonly HashSet<IntVec3> articulationCells = [];
		readonly List<MetaballRenderPatch> renderPatches = [];
		readonly Dictionary<IntVec3, MetaballRenderPatch> renderPatchByCell = [];
		readonly Dictionary<CellMotion, MetaballRenderPatch> renderPatchByMotion = [];
		List<CellMotion> cellMotions = [];
		int articulationShapeVersion = -1;
		Material metaballMaskMaterial;
		ComputeBuffer metaballBuffer;
		MetaballBufferData[] metaballBufferData = [];
		int metaballBufferCapacity;

		Material metaballMaterial;
		MaterialPropertyBlock metaballPropertyBlock;
		Mesh selectionCoreMesh;
		Material selectionCoreMaterial;
		Texture2D selectionCoreTexture;

		float radius, power;
		Vector2 drawCullSize = Vector2.one;
		int nextExpansionTick;
		int nextMovementTick;
		int nextAutoHealTick;
		int nextBenefitCellThreshold;
		int benefitStepCells;
		int feedPausedUntilTick;
		int pendingFeedGrowthPulses;
		int lastSymbiantTick = -1;
		int lastRecessionPulseCells;
		int relocationCellDebt;
		int nextRelocationPulseTick;
		int uprootedSinceTick = -1;
		bool exteriorOverflowAuthorized;
		HashSet<IntVec3> authorizedExteriorCells = [];
		bool exteriorOverflowScopeInitialized;
		IntVec3 establishmentAnchorRelative = IntVec3.Invalid;
		IndoorCapacityState lastIndoorCapacityState = IndoorCapacityState.NoRelevantRooms;
		string lastPlacementGrowthState = "waiting";
		int lastPlacementEvaluationTick = -1;
		int lastFeedAcceptanceEvaluationTick = -1;
		int lastFeedAcceptanceShapeVersion = -1;
		bool lastFeedAcceptanceResult;
		int capacityEvaluationCount;
		int exactCapacityAuditCount;
		int roomCellScanCount;
		int topologyInvalidationCount;
		int topologySettledCount;
		int constructionRepairBatchCount;
		int constructionRelocatedCellCount;
		int constructionCrushedCellCount;
		int constructionPlacementPlanCount;
		int constructionPlacementCandidateScanCount;
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
		int cachedHostEffectCells = 1;
		float cachedBenefitFactor;
		CellRect relativeCellBounds;
		bool hasCellBounds;
		List<HostBenefit> hostBenefits = [];
		List<SymbiantDamageEchoRecord> damageEchoHistory = [];
		int lastCellMotionRenderTick = -1;
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
		bool debugTrackSelectionCoreWander;
		int debugSelectionCoreWanderConnectivityChecks;
		int debugSelectionCoreWanderPreferredTargets;
		bool debugForceMetaballFallback;
		bool lastFallbackSelectionCoreDrawSucceeded;

		public int CellCount => cells?.Count ?? 0;
		internal int CombatShapeVersion => combatShapeVersion;
		internal bool DebugCellsAreConnected => cells == null || cells.Count <= 1 || ConnectedCells(cells, cells.First()).Count == cells.Count;
		internal int DebugComponentCount => ConnectedComponents(cells).Count;
		internal int DebugRoomCellMigrationCount => roomCellMigrationCells?.Count ?? 0;
		internal int DebugRoomCellMigrationLookupCount => roomCellMigrationLookup.Count;
		internal bool DebugRoomCellMigrationInitialized => roomCellMigrationInitialized;
		internal bool DebugRoomCellMigrationRescanPending => roomCellMigrationRescanPending;
		internal int DebugPendingFeedGrowthPulses => pendingFeedGrowthPulses;
		internal bool DebugExteriorOverflowAuthorized => exteriorOverflowAuthorized;
		internal IntVec3[] DebugAuthorizedExteriorCells => authorizedExteriorCells
			.Select(relative => Position + relative)
			.ToArray();
		internal bool DebugIsAuthorizedExteriorCell(IntVec3 absolute) => authorizedExteriorCells.Contains(absolute - Position);
		internal IntVec3[] DebugExteriorOpenTargets()
		{
			SynchronizeExteriorOverflowAuthorization(Map);
			return ExteriorOpenTargets(Map).Select(target => target.cell).ToArray();
		}
		internal IndoorCapacityState DebugLastIndoorCapacityState => lastIndoorCapacityState;
		internal IntVec3 DebugEstablishmentAnchorCell => establishmentAnchorRelative.IsValid ? Position + establishmentAnchorRelative : IntVec3.Invalid;
		internal bool DebugConstructionRepairPending => HasPendingConstructionRepair;
		internal int DebugCapacityEvaluationCount => capacityEvaluationCount;
		internal int DebugExactCapacityAuditCount => exactCapacityAuditCount;
		internal int DebugRoomCellScanCount => roomCellScanCount;
		internal int DebugTopologyInvalidationCount => topologyInvalidationCount;
		internal int DebugTopologySettledCount => topologySettledCount;
		internal int DebugConstructionRepairBatchCount => constructionRepairBatchCount;
		internal int DebugConstructionRelocatedCellCount => constructionRelocatedCellCount;
		internal int DebugConstructionCrushedCellCount => constructionCrushedCellCount;
		internal int DebugConstructionPlacementPlanCount => constructionPlacementPlanCount;
		internal int DebugConstructionPlacementCandidateScanCount => constructionPlacementCandidateScanCount;
		internal bool DebugPlacementTopologySafe => IsPlacementTopologySafe(Map);
		internal bool DebugRoomTopologyInvalidated => roomTopologyInvalidated;
		internal bool DebugLastMovePulseOrdinaryMoved { get; private set; }
		internal bool DebugLastMovePulseMigratedRoomCell { get; private set; }
		internal int DebugLastMovePulseConnectedRoomCellsRetired { get; private set; }
		internal IntVec3 DebugLastMigratedRoomCellSource { get; private set; } = IntVec3.Invalid;
		internal IntVec3 DebugLastMigratedRoomCellDestination { get; private set; } = IntVec3.Invalid;
		internal int DebugMaxRoomComponentCount => OccupiedRoomCounts(Map)
			.Keys
			.Select(room => DebugRoomComponentCount(room))
			.DefaultIfEmpty(0)
			.Max();
		internal static float RoomEstablishmentCoverage => SymbiantRoomEstablishmentCoverage;
		internal int DebugRoomEstablishmentRequirement(Room room) => RoomEstablishmentRequirement(Map, room);
		internal int DebugCellsInRoom(Room room) => CountCellsInRoomInternal(room);
		internal int DebugRoomComponentCount(Room room) => RoomCellComponents(Map, room).Count;
		internal IntVec3[] DebugRoomCellMigrationCells => roomCellMigrationCells
			.Select(relative => Position + relative)
			.ToArray();
		internal int DebugInitializeRoomCellMigration()
		{
			EnsureRoomCellMigrationInitialized(Map);
			return DebugRoomCellMigrationCount;
		}
		internal int DebugRetireConnectedRoomCellMigrationComponents()
		{
			return RetireConnectedRoomCellMigrationComponents(Map);
		}
		internal object DebugPlacementDiagnostics()
		{
			var map = Map;
			if (map == null)
				return new { success = false, error = "Symbiant is not on a map." };
			var evaluation = IsPlacementTopologySafe(map) ? EvaluateIndoorCapacity(map) : null;
			var absoluteCells = orderedCells.Select(relative => Position + relative).ToArray();
			var classified = absoluteCells
				.GroupBy(cell => ClassifySymbiantCell(map, cell))
				.ToDictionary(group => group.Key, group => group.Count());
			return new
			{
				success = true,
				topologySafe = IsPlacementTopologySafe(map),
				roomTopologyInvalidated,
				exteriorOverflowAuthorized,
				authorizedExteriorCells = DebugAuthorizedExteriorCells.Select(DescribeDebugCell).ToArray(),
				establishmentAnchor = DebugEstablishmentAnchorCell.IsValid ? DescribeDebugCell(DebugEstablishmentAnchorCell) : null,
				cellClasses = Enum.GetValues(typeof(SymbiantCellClass))
					.Cast<SymbiantCellClass>()
					.ToDictionary(value => value.ToString(), value => classified.TryGetValue(value, out var count) ? count : 0),
				indoorCapacityState = evaluation?.state.ToString() ?? lastIndoorCapacityState.ToString(),
				relevantRooms = evaluation?.rooms.Select(room => new
				{
					id = room.room.ID,
					capacity = room.capacity,
					occupied = room.occupied,
					room.Empty,
					room.Full,
					hasPlacement = room.hasPlacement,
					placement = room.placementCell.IsValid ? DescribeDebugCell(room.placementCell) : null
				}).ToArray(),
				doorTarget = evaluation?.HasDoorTarget == true ? DescribeDebugCell(evaluation.doorTarget) : null,
				pendingConstructionRepair = HasPendingConstructionRepair,
				pendingConstructionCoveredCells = pendingConstructionCoveredCells.Count,
				pendingConstructionFootprintCells = pendingConstructionFootprintCells.Count,
				migration = new
				{
					initialized = roomCellMigrationInitialized,
					rescanPending = roomCellMigrationRescanPending,
					normalizationPending = roomCellMigrationNormalizationPending,
					queueCount = roomCellMigrationCells.Count,
					lookupCount = roomCellMigrationLookup.Count
				},
				counters = new
				{
					capacityEvaluationCount,
					exactCapacityAuditCount,
					roomCellScanCount,
					topologyInvalidationCount,
					topologySettledCount,
					constructionRepairBatchCount,
					constructionRelocatedCellCount,
					constructionCrushedCellCount,
					constructionPlacementPlanCount,
					constructionPlacementCandidateScanCount,
					lastPlacementEvaluationTick
				},
				capacityModel = "freshSlowDecision"
			};
		}
		internal bool DebugAddDisconnectedRoomCell(IntVec3 absolute)
		{
			var map = Map;
			if (map == null
				|| TryEnterFootprintMutation(FootprintMutationKind.Debug, false, out _) == false
				|| ContainsCell(absolute)
				|| CanOccupyOpenCell(map, absolute) == false)
				return false;
			var room = absolute.GetRoom(map);
			if (IsEligibleIndoorRoom(room) == false || DebugCellsInRoom(room) == 0)
				return false;
			if (GenAdj.CardinalDirections.Any(direction => ContainsCell(absolute + direction)))
				return false;
			roomCellMigrationCells.Clear();
			roomCellMigrationLookup.Clear();
			roomCellMigrationInitialized = false;
			roomCellMigrationRescanPending = false;
			roomCellMigrationNormalizationPending = false;
			if (AddRelativeCell(absolute - Position, false, false) == false)
				return false;
			RebuildCellBounds();
			UpdateAll();
			UpdateSymbiosisState();
			return true;
		}

		internal bool DebugHasActiveCellMotionAt(IntVec3 absolute)
		{
			var relative = absolute - Position;
			var ticks = GenTicks.TicksGame;
			return cellMotions?.Any(motion => motion.cell == relative && ticks < motion.endTick) == true;
		}
		internal static bool DebugRoomsAreAdjacent(Map map, Room source, Room destination) => RoomsAreAdjacentForSymbiant(map, source, destination);
		internal static void NotifyRoomTopologyInvalidated(Map map)
		{
			if (map == null
				|| activeSymbiantByMap.TryGetValue(map, out var symbiant) == false
				|| IsActiveSymbiantOnMap(symbiant, map) == false)
				return;
			symbiant.roomTopologyInvalidated = true;
			symbiant.roomCellMigrationRescanPending = true;
			symbiant.lastSymbiosisMetricTick = int.MinValue;
			symbiant.lastPlacementEvaluationTick = -1;
			symbiant.topologyInvalidationCount++;
			symbiant.lastPlacementGrowthState = "waitingForRoomTopology";
		}

		internal static void NotifyRoomTopologySettled(Map map)
		{
			if (map == null
				|| map.regionAndRoomUpdater?.AnythingToRebuild == true
				|| activeSymbiantByMap.TryGetValue(map, out var symbiant) == false
				|| IsActiveSymbiantOnMap(symbiant, map) == false)
				return;
			symbiant.roomTopologyInvalidated = false;
			symbiant.roomCellMigrationRescanPending = true;
			symbiant.lastSymbiosisMetricTick = int.MinValue;
			symbiant.topologySettledCount++;
			if (symbiant.exteriorOverflowAuthorized
				|| symbiant.HasPendingConstructionRepair
				|| symbiant.relocationCellDebt > 0
				|| symbiant.nextRelocationPulseTick > 0
				|| symbiant.uprootedSinceTick >= 0)
				symbiant.nextRelocationPulseTick = GenTicks.TicksGame;
		}

		internal static void NotifyCellClassificationChanged(Map map)
		{
			if (map == null
				|| activeSymbiantByMap.TryGetValue(map, out var symbiant) == false
				|| IsActiveSymbiantOnMap(symbiant, map) == false)
				return;
			symbiant.roomCellMigrationRescanPending = true;
			symbiant.lastSymbiosisMetricTick = int.MinValue;
			symbiant.lastPlacementEvaluationTick = -1;
			symbiant.nextRelocationPulseTick = GenTicks.TicksGame;
		}
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
		public bool ExteriorOverflowAuthorized => exteriorOverflowAuthorized;
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
		internal int DebugLastSelectionCoreWanderConnectivityChecks { get; private set; }
		internal int DebugLastSelectionCoreWanderPreferredTargets { get; private set; }
		internal int DebugLastSelectionCoreInitializationCandidateCount { get; private set; }
		internal int DebugLastSelectionCoreInitializationShortlistCount { get; private set; }
		internal int DebugLastSelectionCoreInitializationConnectivityChecks { get; private set; }
		internal bool DebugSelectionCoreIsLastOrdered => orderedCells?.LastOrDefault() == selectionCoreRelative;
		internal static int SelectionCorePreferredTargetLimit => AmbientMovementCandidateLimit;
		internal static int SelectionCoreInitializationCandidateLimit => SelectionCoreConnectivityCandidateLimit;
		internal Vector2 SelectionCoreVisualCenterRelative
		{
			get
			{
				EnsureSelectionCoreState();
				return SelectionCoreVisualCenter;
			}
		}
		public override CellRect? CustomRectForSelector => hasCellBounds ? CellRect.SingleCell(SelectionCoreCell) : base.CustomRectForSelector;
		public int RenderTextureWidth => renderPatches.Select(patch => patch.texture?.width ?? 0).DefaultIfEmpty(0).Max();
		public int RenderTextureHeight => renderPatches.Select(patch => patch.texture?.height ?? 0).DefaultIfEmpty(0).Max();
		public Vector2 RenderWorldSize => new(
			renderPatches.Select(patch => patch.renderWidth).DefaultIfEmpty(0f).Max(),
			renderPatches.Select(patch => patch.renderHeight).DefaultIfEmpty(0f).Max()
		);
		public int RenderPatchCount => renderPatches.Count;
		public string RenderShaderName => metaballMaterial?.shader?.name;
		public bool RenderUsesSymbiantShader => Assets.ZombieSymbiantShader != null && metaballMaterial?.shader == Assets.ZombieSymbiantShader;
		public bool RenderUsesGpuMetaballMask => Assets.MetaballShader != null && metaballMaskMaterial?.shader == Assets.MetaballShader && renderPatches.Any(patch => patch.texture != null);
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
		public int HostEffectCellCount { get { RefreshSymbiosisMetrics(); return cachedHostEffectCells; } }
		public float HealthScaleCellMultiplier => HealthScaleMultiplierForCells(HostEffectCellCount);
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
				if (HasPendingConstructionRepair)
					return "repairingConstruction";
				if (uprootedSinceTick >= 0)
					return lastPlacementGrowthState == "dormantNoRoom" ? "dormantNoRoom" : "uprooted";
				if (relocationCellDebt > 0 || nextRelocationPulseTick > 0)
					return lastPlacementGrowthState == "contained" ? "contained" : "relocating";
				if (CellCount >= MaxCells)
					return "capped";
				var ticks = GenTicks.TicksGame;
				if (ticks < feedPausedUntilTick)
					return "pausedAfterFeeding";
				if (ticks < nextExpansionTick)
					return "waiting";
				return lastPlacementGrowthState;
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

		static bool CanOccupyFurnishedFoundingCell(Map map, IntVec3 cell)
		{
			return CanOccupyOpenCell(map, cell)
				&& cell.GetThingList(map).Any(thing => thing is Pawn) == false;
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
			while (cachedHostEffectCells >= nextBenefitCellThreshold)
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

		static bool IsGenuineExteriorRoom(Room room)
		{
			return room != null && (room.UsesOutdoorTemperature || room.TouchesMapEdge);
		}

		internal static SymbiantCellClass ClassifySymbiantCell(Map map, IntVec3 cell)
		{
			if (map == null || cell.InBounds(map) == false || cell.Fogged(map))
				return SymbiantCellClass.InvalidBlocked;
			if (cell.GetEdifice(map) is Building_Door)
				return IsDoorCell(map, cell) ? SymbiantCellClass.Door : SymbiantCellClass.InvalidBlocked;
			if (cell.Walkable(map) == false)
				return SymbiantCellClass.InvalidBlocked;

			var room = cell.GetRoom(map);
			if (IsEligibleIndoorRoom(room))
				return cell.Roofed(map) ? SymbiantCellClass.IndoorFloor : SymbiantCellClass.InvalidBlocked;
			if (IsGenuineExteriorRoom(room))
				return SymbiantCellClass.ExteriorOpen;
			return SymbiantCellClass.IndoorIneligible;
		}

		static float IntegratedCellWeight(Map map, IntVec3 cell)
		{
			var classification = ClassifySymbiantCell(map, cell);
			if (classification != SymbiantCellClass.IndoorFloor && classification != SymbiantCellClass.Door)
				return 0f;
			return ScoreTraffic(map, cell) > 0f ? 1f : 0.5f;
		}

		static bool IsValidSymbiantCell(Map map, IntVec3 cell)
		{
			var classification = ClassifySymbiantCell(map, cell);
			return classification == SymbiantCellClass.IndoorFloor
				|| classification == SymbiantCellClass.Door
				|| classification == SymbiantCellClass.ExteriorOpen;
		}

		int CalculateHostEffectCellCount(Map map)
		{
			if (map == null || orderedCells == null)
				return 0;
			return orderedCells.Count(relative =>
			{
				var classification = ClassifySymbiantCell(map, Position + relative);
				return classification == SymbiantCellClass.IndoorFloor || classification == SymbiantCellClass.Door;
			});
		}

		void RefreshSymbiosisMetrics(bool force = false)
		{
			var ticks = GenTicks.TicksGame;
			if (force == false && lastSymbiosisMetricTick != int.MinValue && ticks - lastSymbiosisMetricTick < SymbiosisMetricRefreshInterval)
				return;
			var map = SymbiantMap;
			if (map != null && Spawned && IsPlacementTopologySafe(map) == false)
			{
				lastSymbiosisMetricTick = int.MinValue;
				return;
			}
			cachedEligibleColonyRoomCells = EligibleColonyRoomCellCount(map);
			cachedFullBenefitCells = CalculateFullBenefitCells(cachedEligibleColonyRoomCells);
			cachedIntegratedVisibleCells = CalculateIntegratedVisibleCells(map);
			cachedHostEffectCells = CalculateHostEffectCellCount(map);
			cachedBenefitFactor = Mathf.Clamp01(cachedIntegratedVisibleCells / Mathf.Max(1f, cachedFullBenefitCells));
			lastSymbiosisMetricTick = ticks;
		}

		void UpdateSymbiosisState(bool forceMetricRefresh = true)
		{
			if (Destroyed)
				return;
			var map = SymbiantMap;
			if (map != null && Spawned && IsPlacementTopologySafe(map) == false)
			{
				lastSymbiosisMetricTick = int.MinValue;
				return;
			}
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

		internal Pawn[] DebugEligibleHosts()
		{
			var map = SymbiantMap;
			if (map == null)
				return [];
			return EligibleHosts(map, this)
				.OrderBy(pawn => pawn.LabelShortCap.ToString(), StringComparer.CurrentCultureIgnoreCase)
				.ThenBy(pawn => pawn.ThingID)
				.ToArray();
		}

		internal bool DebugAssignHost(Pawn pawn)
		{
			if (ResolveHost() != null || IsEligibleHost(pawn, this) == false)
				return false;
			symbiosisSevered = false;
			AssignHost(pawn);
			UpdateSymbiosisState();
			NotifyHostCapacityBenefitsChanged(pawn);
			return LinkedHost == pawn;
		}

		internal bool DebugUnassignHost()
		{
			var previousHost = ResolveHost();
			if (previousHost == null)
				return false;
			host = null;
			hostThingId = null;
			symbiosisSevered = false;
			RemoveHostHediff(previousHost);
			ClearDamageEchoHistory();
			RememberHostBondState(null);
			NotifyHostCapacityBenefitsChanged(previousHost);
			UpdateSymbiosisState();
			return LinkedHost == null;
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
			ReleaseMetaballPatchResources();
			metaballPropertyBlock = null;
			if (unregister)
				renderResourceOwners.Remove(this);
		}

		void ReleaseMetaballPatchResources()
		{
			foreach (var patch in renderPatches)
			{
				if (patch.texture != null)
					UnityEngine.Object.Destroy(patch.texture);
				if (patch.mesh != null)
					UnityEngine.Object.Destroy(patch.mesh);
			}
			renderPatches.Clear();
			renderPatchByCell.Clear();
			renderPatchByMotion.Clear();
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

		bool AddRelativeCell(IntVec3 relative, bool travelFromExistingCell = true, bool animate = true)
		{
			EnsureSharedHealth();
			var wasFullHealth = sharedHealth >= SharedHealthMax - 0.01f;
			if (cells.Add(relative) == false)
				return false;
			lastSymbiosisMetricTick = int.MinValue;
			destroyWhenCellMotionsFinish = false;
			if (animate)
				StartIncomingCellMotion(relative, travelFromExistingCell);
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
			if (establishmentAnchorRelative.IsValid == false)
				establishmentAnchorRelative = relative;
			if (wasFullHealth)
				sharedHealth = SharedHealthMax;
			return true;
		}

		bool RemoveRelativeCell(IntVec3 relative, bool animate)
		{
			return RemoveRelativeCellWithCoreDestination(relative, animate, IntVec3.Invalid);
		}

		bool RemoveRelativeCellWithCoreDestination(IntVec3 relative, bool animate, IntVec3 selectionCoreDestination, bool travelToRemainingCell = true)
		{
			if (cells?.Contains(relative) != true)
				return false;
			var removesSelectionCore = selectionCoreRelative == relative;
			if (removesSelectionCore && selectionCoreDestination.IsValid == false)
			{
				var remaining = cells.Where(cell => cell != relative).ToArray();
				var map = MapHeld;
				var sourceRoom = map != null ? (Position + relative).GetRoom(map) : null;
				selectionCoreDestination = remaining.Length == 0
					? IntVec3.Invalid
					: remaining
						.OrderBy(cell => sourceRoom != null && (Position + cell).GetRoom(map) == sourceRoom ? 0 : 1)
						.ThenBy(SelectionCoreClutterScore)
						.ThenBy(cell => cell.DistanceToSquared(relative))
						.First();
			}
			if (animate)
				StartOutgoingCellMotion(relative, travelToRemainingCell);
			orderedCells.Remove(relative);
			var removed = cells.Remove(relative);
			if (removed)
			{
				authorizedExteriorCells?.Remove(relative);
				if (exteriorOverflowScopeInitialized && authorizedExteriorCells?.Count == 0)
					exteriorOverflowAuthorized = false;
				roomCellMigrationCells?.Remove(relative);
				roomCellMigrationLookup.Remove(relative);
				if (establishmentAnchorRelative == relative)
					establishmentAnchorRelative = IntVec3.Invalid;
				lastSymbiosisMetricTick = int.MinValue;
				combatShapeVersion++;
				if (removesSelectionCore)
					BeginSelectionCoreMove(relative, selectionCoreDestination);
				EnsureSharedHealth();
			}
			return removed;
		}

		bool WouldCellsStayConnectedAfterRemoval(IntVec3 removedRelative)
		{
			if (cells == null || cells.Contains(removedRelative) == false || cells.Count <= 1)
				return true;
			EnsureArticulationCells();
			return articulationCells.Contains(removedRelative) == false;
		}

		bool WouldCellsStayConnectedAfterMove(IntVec3 removedRelative, IntVec3 addedRelative)
		{
			if (cells == null || cells.Count <= 1)
				return true;
			if (debugTrackSelectionCoreWander)
				debugSelectionCoreWanderConnectivityChecks++;
			return WouldCellsStayConnectedAfterRemoval(removedRelative);
		}

		void EnsureArticulationCells()
		{
			if (articulationShapeVersion == combatShapeVersion)
				return;
			articulationShapeVersion = combatShapeVersion;
			articulationCells.Clear();
			if (cells == null || cells.Count <= 2)
				return;

			var discovered = new Dictionary<IntVec3, int>(cells.Count);
			var low = new Dictionary<IntVec3, int>(cells.Count);
			var parent = new Dictionary<IntVec3, IntVec3>(cells.Count);
			var childCounts = new Dictionary<IntVec3, int>(cells.Count);
			var time = 0;
			foreach (var root in cells)
			{
				if (discovered.ContainsKey(root))
					continue;
				discovered[root] = ++time;
				low[root] = discovered[root];
				parent[root] = IntVec3.Invalid;
				childCounts[root] = 0;
				var open = new Stack<(IntVec3 cell, int directionIndex)>();
				open.Push((root, 0));
				while (open.Count > 0)
				{
					var frame = open.Pop();
					if (frame.directionIndex < GenAdj.CardinalDirections.Length)
					{
						open.Push((frame.cell, frame.directionIndex + 1));
						var neighbor = frame.cell + GenAdj.CardinalDirections[frame.directionIndex];
						if (cells.Contains(neighbor) == false)
							continue;
						if (discovered.ContainsKey(neighbor) == false)
						{
							parent[neighbor] = frame.cell;
							childCounts[neighbor] = 0;
							childCounts[frame.cell] = childCounts[frame.cell] + 1;
							discovered[neighbor] = ++time;
							low[neighbor] = discovered[neighbor];
							open.Push((neighbor, 0));
						}
						else if (parent[frame.cell] != neighbor)
							low[frame.cell] = Mathf.Min(low[frame.cell], discovered[neighbor]);
						continue;
					}

					var parentCell = parent[frame.cell];
					if (parentCell.IsValid)
					{
						low[parentCell] = Mathf.Min(low[parentCell], low[frame.cell]);
						if (parent[parentCell].IsValid && low[frame.cell] >= discovered[parentCell])
							articulationCells.Add(parentCell);
					}
					else if (childCounts[frame.cell] > 1)
						articulationCells.Add(frame.cell);
				}
			}
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

		static List<HashSet<IntVec3>> ConnectedComponents(HashSet<IntVec3> source)
		{
			var components = new List<HashSet<IntVec3>>();
			if (source == null || source.Count == 0)
				return components;
			var remaining = new HashSet<IntVec3>(source);
			while (remaining.Count > 0)
			{
				var component = ConnectedCells(source, remaining.First());
				components.Add(component);
				remaining.ExceptWith(component);
			}
			return components;
		}

		List<HashSet<IntVec3>> RoomCellComponents(Map map, Room room)
		{
			if (map == null || room == null || cells == null)
				return [];
			var roomCells = orderedCells
				.Where(relative =>
				{
					var absolute = Position + relative;
					return absolute.InBounds(map) && absolute.GetRoom(map) == room;
				})
				.ToHashSet();
			return ConnectedComponents(roomCells);
		}

		HashSet<IntVec3> PrimaryRoomComponent(IEnumerable<HashSet<IntVec3>> components)
		{
			return components
				.OrderByDescending(component => component.Count)
				.ThenByDescending(component => component.Contains(IntVec3.Zero))
				.ThenByDescending(component => selectionCoreRelative.IsValid && component.Contains(selectionCoreRelative))
				.FirstOrDefault();
		}

		void RebuildRoomCellMigrationLookup()
		{
			roomCellMigrationLookup.Clear();
			if (roomCellMigrationCells != null)
				roomCellMigrationLookup.UnionWith(roomCellMigrationCells);
		}

		void NormalizeRoomCellMigrationQueue(Map map)
		{
			roomCellMigrationCells ??= [];
			roomCellMigrationCells = roomCellMigrationCells
				.Distinct()
				.Where(relative =>
				{
					if (cells?.Contains(relative) != true)
						return false;
					var absolute = Position + relative;
					return absolute.InBounds(map) && IsEligibleIndoorRoom(absolute.GetRoom(map));
				})
				.ToList();
			RebuildRoomCellMigrationLookup();
			roomCellMigrationNormalizationPending = false;
		}

		void EnsureRoomCellMigrationInitialized(Map map)
		{
			if (map == null || IsPlacementTopologySafe(map) == false)
				return;
			if (roomCellMigrationNormalizationPending)
				NormalizeRoomCellMigrationQueue(map);
			if (roomCellMigrationInitialized && roomCellMigrationRescanPending == false)
				return;
			roomCellMigrationCells ??= [];
			roomCellMigrationCells.Clear();
			roomCellMigrationLookup.Clear();
			var roomCells = new Dictionary<Room, HashSet<IntVec3>>();
			foreach (var relative in orderedCells)
			{
				var absolute = Position + relative;
				var room = absolute.InBounds(map) ? absolute.GetRoom(map) : null;
				if (IsEligibleIndoorRoom(room) == false)
					continue;
				if (roomCells.TryGetValue(room, out var cellsInRoom) == false)
				{
					cellsInRoom = [];
					roomCells.Add(room, cellsInRoom);
				}
				cellsInRoom.Add(relative);
			}
			foreach (var cellsInRoom in roomCells.Values)
			{
				var components = ConnectedComponents(cellsInRoom);
				if (components.Count <= 1)
					continue;
				var primary = PrimaryRoomComponent(components);
				foreach (var component in components)
				{
					if (component != primary)
						roomCellMigrationCells.AddRange(component);
				}
			}
			RebuildRoomCellMigrationLookup();
			roomCellMigrationInitialized = true;
			roomCellMigrationRescanPending = false;
			if (roomCellMigrationLookup.Contains(IntVec3.Zero))
			{
				var replacementRoot = orderedCells
					.Where(relative => roomCellMigrationLookup.Contains(relative) == false)
					.Where(relative =>
					{
						var classification = ClassifySymbiantCell(map, Position + relative);
						return classification == SymbiantCellClass.IndoorFloor || classification == SymbiantCellClass.Door;
					})
					.OrderBy(relative => relative == selectionCoreRelative ? 0 : 1)
					.ThenBy(relative => relative.DistanceToSquared(IntVec3.Zero))
					.FirstOrDefault();
				if (replacementRoot.IsValid && replacementRoot != IntVec3.Zero && RebaseFootprint(map, replacementRoot))
				{
					roomCellMigrationInitialized = false;
					roomCellMigrationRescanPending = true;
					EnsureRoomCellMigrationInitialized(map);
				}
			}
		}

		bool PromoteQueuedRoomComponentIfNecessary(Map map, Room room)
		{
			if (map == null || room == null || roomCellMigrationCells.Count == 0)
				return false;
			var queuedCells = roomCellMigrationLookup;
			var hasEstablishedCell = orderedCells.Any(relative =>
				queuedCells.Contains(relative) == false
				&& (Position + relative).InBounds(map)
				&& (Position + relative).GetRoom(map) == room);
			if (hasEstablishedCell)
				return false;

			var queuedRoomCells = roomCellMigrationCells
				.Where(relative => cells.Contains(relative)
					&& (Position + relative).InBounds(map)
					&& (Position + relative).GetRoom(map) == room)
				.ToHashSet();
			var primary = PrimaryRoomComponent(ConnectedComponents(queuedRoomCells));
			if (primary == null || primary.Count == 0)
				return false;
			roomCellMigrationCells.RemoveAll(primary.Contains);
			RebuildRoomCellMigrationLookup();
			return true;
		}

		int RetireConnectedRoomCellMigrationComponents(Map map)
		{
			if (map == null || roomCellMigrationCells == null || roomCellMigrationCells.Count == 0)
				return 0;

			var queuedCells = roomCellMigrationLookup;
			var queuedCellsByRoom = new Dictionary<Room, HashSet<IntVec3>>();
			foreach (var relative in queuedCells)
			{
				var absolute = Position + relative;
				var room = absolute.InBounds(map) ? absolute.GetRoom(map) : null;
				if (IsEligibleIndoorRoom(room) == false)
					continue;
				if (queuedCellsByRoom.TryGetValue(room, out var roomCells) == false)
				{
					roomCells = [];
					queuedCellsByRoom.Add(room, roomCells);
				}
				roomCells.Add(relative);
			}

			var connectedQueuedCells = new HashSet<IntVec3>();
			foreach (var pair in queuedCellsByRoom)
			{
				var room = pair.Key;
				var queuedRoomCells = pair.Value;
				var open = new Queue<IntVec3>();
				foreach (var relative in queuedRoomCells)
				{
					var touchesEstablishedPatch = false;
					foreach (var direction in GenAdj.CardinalDirections)
					{
						var neighborRelative = relative + direction;
						if (cells.Contains(neighborRelative) == false || queuedCells.Contains(neighborRelative))
							continue;
						var neighbor = Position + neighborRelative;
						if (neighbor.InBounds(map) && neighbor.GetRoom(map) == room)
						{
							touchesEstablishedPatch = true;
							break;
						}
					}
					if (touchesEstablishedPatch && connectedQueuedCells.Add(relative))
						open.Enqueue(relative);
				}

				while (open.Count > 0)
				{
					var relative = open.Dequeue();
					foreach (var direction in GenAdj.CardinalDirections)
					{
						var neighbor = relative + direction;
						if (queuedRoomCells.Contains(neighbor) && connectedQueuedCells.Add(neighbor))
							open.Enqueue(neighbor);
					}
				}
			}

			if (connectedQueuedCells.Count > 0)
			{
				roomCellMigrationCells.RemoveAll(connectedQueuedCells.Contains);
				roomCellMigrationLookup.ExceptWith(connectedQueuedCells);
			}
			return connectedQueuedCells.Count;
		}

		IntVec3[] RoomMigrationTargetCandidates(Map map, Room room)
		{
			var queuedCells = roomCellMigrationLookup;
			return orderedCells
				.Where(relative => queuedCells.Contains(relative) == false)
				.Select(relative => Position + relative)
				.Where(absolute => absolute.InBounds(map) && absolute.GetRoom(map) == room)
				.SelectMany(absolute => GenAdj.CardinalDirections.Select(direction => absolute + direction))
				.Where(candidate => candidate.InBounds(map)
					&& candidate.GetRoom(map) == room
					&& ContainsCell(candidate) == false
					&& CanOccupyOpenCell(map, candidate))
				.Distinct()
				.ToArray();
		}

		void StartIncomingCellMotion(IntVec3 relative, bool travelFromExistingCell)
		{
			var existingCells = cells.Where(cell => cell != relative).ToArray();
			var to = CellCenter(relative);
			var from = travelFromExistingCell && existingCells.Length > 0 ? NearestCellCenter(relative, existingCells) : to;
			StartCellMotion(relative, from, to, false, GetSize(relative));
		}

		void StartOutgoingCellMotion(IntVec3 relative, bool travelToRemainingCell = true)
		{
			var remainingCells = cells.Where(cell => cell != relative).ToArray();
			var from = CellCenter(relative);
			var to = travelToRemainingCell && remainingCells.Length > 0 ? NearestCellCenter(relative, remainingCells) : from;
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
				var visualCell = new IntVec3(Mathf.RoundToInt(center.x), 0, Mathf.RoundToInt(center.y));
				if (cells?.Contains(visualCell) == true)
					return visualCell;
				if (IsSelectionCoreMotionActive(GenTicks.TicksGame))
				{
					var occupiedEndpoint = new[] { selectionCoreMotionFrom, selectionCoreMotionTo }
						.Where(cell => cell.IsValid && cells?.Contains(cell) == true)
						.OrderBy(cell => (CellCenter(cell) - center).sqrMagnitude)
						.DefaultIfEmpty(IntVec3.Invalid)
						.First();
					if (occupiedEndpoint.IsValid)
						return occupiedEndpoint;
				}
				if (selectionCoreRelative.IsValid && cells?.Contains(selectionCoreRelative) == true)
					return selectionCoreRelative;
				return BestSelectionCoreRelative(IntVec3.Zero, selectionCoreDiscoveryCue);
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
			var candidateCells = orderedCells
				.Where(cell => cells.Contains(cell))
				.Where(cell => allowRoot || cell != IntVec3.Zero)
				.ToArray();
			if (candidateCells.Length == 0)
				candidateCells = orderedCells.Where(cell => cells.Contains(cell)).ToArray();

			DebugLastSelectionCoreInitializationCandidateCount = candidateCells.Length;
			var shortlist = candidateCells
				.Select(cell => new
				{
					cell,
					clutter = SelectionCoreClutterScore(cell),
					cardinalNeighbors = SelectionCoreCardinalNeighborCount(cell),
					distance = near.IsValid ? cell.DistanceToSquared(near) : 0
				})
				.OrderBy(candidate => candidate.clutter)
				.ThenBy(candidate => candidate.cardinalNeighbors)
				.ThenBy(candidate => candidate.distance)
				.ThenBy(candidate => candidate.cell.x)
				.ThenBy(candidate => candidate.cell.z)
				.Take(SelectionCoreConnectivityCandidateLimit)
				.ToArray();
			DebugLastSelectionCoreInitializationShortlistCount = shortlist.Length;

			var finalists = shortlist
				.Select(candidate => new
				{
					candidate.cell,
					candidate.clutter,
					candidate.cardinalNeighbors,
					candidate.distance,
					removable = WouldCellsStayConnectedAfterRemoval(candidate.cell)
				})
				.ToArray();
			DebugLastSelectionCoreInitializationConnectivityChecks = finalists.Length;

			return finalists
				.OrderBy(candidate => candidate.clutter)
				.ThenBy(candidate => candidate.removable ? 0 : 1)
				.ThenBy(candidate => candidate.cardinalNeighbors)
				.ThenBy(candidate => candidate.distance)
				.ThenBy(candidate => candidate.cell.x)
				.ThenBy(candidate => candidate.cell.z)
				.Select(candidate => candidate.cell)
				.FirstOrDefault();
		}

		internal int DebugReinitializeSelectionCoreForScaleProbe(IEnumerable<IntVec3> absoluteCells)
		{
			var added = AddCells(absoluteCells);
			selectionCoreDiscoveryCue = false;
			selectionCoreRelative = IntVec3.Invalid;
			ClearSelectionCoreMotion();
			EnsureSelectionCoreState();
			return added;
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
			if (TryEnterFootprintMutation(FootprintMutationKind.Debug, false, out _) == false)
				return 0;
			var added = AddRelativeCells(newCells.Select(cell => cell - Position));
			if (added > 0)
			{
				roomCellMigrationRescanPending = true;
				UpdateAll();
				UpdateSymbiosisState();
				SynchronizeExteriorOverflowAuthorization(Map);
			}
			return added;
		}

		bool AddCell(IntVec3 newCell, bool travelFromExistingCell = true)
		{
			var map = Map;
			if (TryEnterFootprintMutation(FootprintMutationKind.Debug, false, out _) == false
				|| CellCount >= MaxCells
				|| IsValidSymbiantCell(map, newCell) == false
				|| CanPlaceConnectedWithinRoom(map, newCell) == false)
				return false;
			if (AddRelativeCell(newCell - Position, travelFromExistingCell))
			{
				UpdateAll();
				UpdateSymbiosisState();
				roomCellMigrationRescanPending = true;
				SynchronizeExteriorOverflowAuthorization(map);
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
			var map = Map;
			if (map == null || IsPlacementTopologySafe(map) == false)
				return false;
			var linkedHost = ResolveHost();
			if (linkedHost == null)
			{
				uprootedSinceTick = -1;
				return false;
			}
			var ticks = GenTicks.TicksGame;
			if (uprootedSinceTick >= 0 && nextRelocationPulseTick > ticks)
				return false;
			SynchronizeExteriorOverflowAuthorization(map);
			RefreshSymbiosisMetrics(true);
			if (cachedIntegratedVisibleCells > UprootedIntegratedCellThreshold)
			{
				uprootedSinceTick = -1;
				return false;
			}
			var graceExpired = uprootedSinceTick >= 0 && ticks - uprootedSinceTick >= UprootedRelocationGraceTicks;

			var capacity = EvaluateIndoorCapacity(map);
			if (capacity.state != IndoorCapacityState.NoRelevantRooms)
			{
				uprootedSinceTick = -1;
				if (capacity.state == IndoorCapacityState.PlacementAvailable && HasPriorityRelocationCells(map, capacity))
				{
					nextRelocationPulseTick = ticks;
					lastPlacementGrowthState = "relocating";
				}
				else if (capacity.state == IndoorCapacityState.NonFullButBlocked)
				{
					nextRelocationPulseTick = ticks + PlacementBlockedRetryTicks;
					lastPlacementGrowthState = "contained";
				}
				return false;
			}

			if (uprootedSinceTick < 0)
			{
				uprootedSinceTick = ticks;
				nextRelocationPulseTick = ticks + RelocationPulseIntervalTicks();
				return false;
			}
			if (graceExpired == false)
			{
				nextRelocationPulseTick = Mathf.Min(
					uprootedSinceTick + UprootedRelocationGraceTicks,
					ticks + RelocationPulseIntervalTicks()
				);
				return false;
			}
			if (HasReseedCandidateRoom(linkedHost) == false)
			{
				lastPlacementGrowthState = "dormantNoRoom";
				nextRelocationPulseTick = ticks + PlacementBlockedRetryTicks;
				return false;
			}
			if (TryEnterFootprintMutation(FootprintMutationKind.Reseed, false, out _) == false)
				return false;

			if (TryFindReseedPlan(linkedHost, out var anchor, out var reseedCells) == false)
			{
				lastPlacementGrowthState = HasReseedCandidateRoom(linkedHost) ? "uprooted" : "dormantNoRoom";
				return false;
			}

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
			roomCellMigrationCells = [];
			roomCellMigrationLookup.Clear();
			roomCellMigrationInitialized = false;
			roomCellMigrationRescanPending = false;
			roomCellMigrationNormalizationPending = false;
			exteriorOverflowAuthorized = false;
			authorizedExteriorCells.Clear();
			exteriorOverflowScopeInitialized = true;
			establishmentAnchorRelative = IntVec3.Invalid;
			selectionCoreRelative = IntVec3.Invalid;
			selectionCoreLastMoveTick = GenTicks.TicksGame;
			ClearSelectionCoreMotion();
			recentMovementCells.Clear();
			combatShapeVersion++;
			hasCellBounds = false;
			AddRelativeCells(targetCells.Select(cell => cell - anchor));
			RebuildCellBounds();
			relocationCellDebt = Mathf.Max(0, relocationCellDebt + relocationDebt);
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

		bool HasPendingConstructionRepair => postLoadConstructionValidationPending
			|| pendingConstructionCoveredCells.Count > 0
			|| pendingConstructionFootprintCells.Count > 0;

		bool IsPlacementTopologySafe(Map map)
		{
			return map != null
				&& roomTopologyInvalidated == false
				&& map.regionAndRoomUpdater?.AnythingToRebuild != true;
		}

		bool TryEnterFootprintMutation(FootprintMutationKind kind, bool netGrowth, out string blocker)
		{
			blocker = null;
			var map = Map;
			if (map == null || Spawned == false || Destroyed || Dead)
			{
				blocker = "inactive";
				return false;
			}
			if (IsPlacementTopologySafe(map) == false)
			{
				blocker = "roomTopology";
				lastPlacementGrowthState = "waitingForRoomTopology";
				return false;
			}
			if (postLoadConstructionValidationPending)
				DiscoverConstructionOverlapAfterLoad(map);
			if (kind != FootprintMutationKind.ConstructionRepair && HasPendingConstructionRepair)
			{
				blocker = "constructionRepair";
				lastPlacementGrowthState = "repairingConstruction";
				return false;
			}
			EnsureRoomCellMigrationInitialized(map);
			if (roomCellMigrationInitialized == false)
			{
				blocker = "migrationInitialization";
				return false;
			}
			if (netGrowth && roomCellMigrationLookup.Count > 0)
			{
				blocker = "roomMigration";
				lastPlacementGrowthState = "repairingRoomConnectivity";
				return false;
			}
			return true;
		}

		internal static void NotifyImpassableBuildingSpawned(Building building)
		{
			var map = building?.Map;
			if (map == null || building.def?.passability != Traversability.Impassable)
				return;
			var symbiant = ActiveSymbiant(map);
			if (symbiant == null || symbiant.hasCellBounds == false)
				return;
			var footprint = building.OccupiedRect();
			if (symbiant.AbsoluteCellBounds.Overlaps(footprint) == false)
				return;
			var covered = footprint.Where(symbiant.ContainsCell).ToArray();
			if (covered.Length == 0)
				return;
			symbiant.pendingConstructionFootprintCells.UnionWith(footprint);
			symbiant.pendingConstructionCoveredCells.UnionWith(covered);
			symbiant.lastSymbiosisMetricTick = int.MinValue;
			symbiant.lastPlacementGrowthState = "repairingConstruction";
		}

		internal static bool TryHandleUnwalkableRootRecovery(Pawn pawn, out bool recovered)
		{
			recovered = false;
			if (pawn is not ZombieSymbiant symbiant)
				return false;
			var map = symbiant.Map;
			if (map == null || symbiant.Spawned == false || symbiant.Destroyed)
				return true;
			var edifice = symbiant.Position.GetEdifice(map);
			if (edifice?.def?.passability != Traversability.Impassable)
			{
				recovered = symbiant.Position.Walkable(map) || IsDoorCell(map, symbiant.Position);
				if (recovered == false)
					symbiant.nextRelocationPulseTick = GenTicks.TicksGame;
				return true;
			}
			NotifyImpassableBuildingSpawned(edifice);
			// Suppress vanilla teleport/destruction, but report failure to path callers until the next
			// settled placement boundary has repaired the root. GenSpawn ignores this return value;
			// Pawn_PathFollower.StartPath correctly aborts while the logical footprint is still blocked.
			recovered = false;
			return true;
		}

		void DiscoverConstructionOverlapAfterLoad(Map map)
		{
			postLoadConstructionValidationPending = false;
			if (map == null || orderedCells == null)
				return;
			foreach (var absolute in orderedCells.Select(relative => Position + relative).ToArray())
			{
				var building = absolute.InBounds(map) ? absolute.GetEdifice(map) : null;
				if (building?.def?.passability != Traversability.Impassable)
					continue;
				pendingConstructionCoveredCells.Add(absolute);
				pendingConstructionFootprintCells.UnionWith(building.OccupiedRect());
			}
		}

		void RevalidatePendingConstructionRepair(Map map)
		{
			pendingConstructionFootprintCells.RemoveWhere(cell =>
				cell.InBounds(map) == false || cell.GetEdifice(map)?.def?.passability != Traversability.Impassable);
			pendingConstructionCoveredCells.RemoveWhere(cell =>
				ContainsCell(cell) == false
				|| cell.InBounds(map) == false
				|| cell.GetEdifice(map)?.def?.passability != Traversability.Impassable);
			foreach (var cell in pendingConstructionCoveredCells.ToArray())
			{
				var building = cell.GetEdifice(map);
				if (building != null)
					pendingConstructionFootprintCells.UnionWith(building.OccupiedRect());
			}
		}

		static bool CanUseAsCanonicalRoot(Map map, IntVec3 cell)
		{
			if (map == null || cell.InBounds(map) == false)
				return false;
			var classification = ClassifySymbiantCell(map, cell);
			return classification == SymbiantCellClass.IndoorFloor
				|| classification == SymbiantCellClass.Door
				|| classification == SymbiantCellClass.ExteriorOpen;
		}

		bool ReplaceAbsoluteFootprint(
			Map map,
			IReadOnlyList<IntVec3> absoluteCells,
			IntVec3 newRoot,
			IntVec3 newCore,
			IntVec3 newEstablishmentAnchor)
		{
			if (map == null || absoluteCells == null || absoluteCells.Count == 0 || absoluteCells.Contains(newRoot) == false)
				return false;
			SynchronizeExteriorOverflowAuthorization(map);
			var wasSelected = Find.Selector?.IsSelected(this) == true;
			var authorizedAbsoluteCells = authorizedExteriorCells
				.Select(relative => Position + relative)
				.Where(absoluteCells.Contains)
				.ToArray();
			temporaryDespawnInProgress = true;
			try
			{
				DeSpawn(DestroyMode.Vanish);
			}
			finally
			{
				temporaryDespawnInProgress = false;
			}

			Position = newRoot;
			orderedCells = absoluteCells.Distinct().Select(cell => cell - newRoot).ToList();
			cells = orderedCells.ToHashSet();
			authorizedExteriorCells = authorizedAbsoluteCells.Select(cell => cell - newRoot).ToHashSet();
			exteriorOverflowAuthorized = authorizedExteriorCells.Count > 0;
			roomCellMigrationCells = [];
			roomCellMigrationLookup.Clear();
			roomCellMigrationInitialized = false;
			roomCellMigrationRescanPending = true;
			roomCellMigrationNormalizationPending = false;
			selectionCoreRelative = (absoluteCells.Contains(newCore) ? newCore : newRoot) - newRoot;
			selectionCoreLastMoveTick = GenTicks.TicksGame;
			ClearSelectionCoreMotion();
			establishmentAnchorRelative = (absoluteCells.Contains(newEstablishmentAnchor) ? newEstablishmentAnchor : newRoot) - newRoot;
			cellMotions?.Clear();
			recentMovementCells.Clear();
			hasCellBounds = false;
			lastSymbiosisMetricTick = int.MinValue;
			combatShapeVersion++;
			RebuildCellBounds();

			GenSpawn.Spawn(this, newRoot, map, Rot4.Random, WipeMode.Vanish, false);
			RegisterActiveSymbiant(this, map);
			EnsureVisibleToPawnSystems(map);
			jobs.StartJob(JobMaker.MakeJob(CustomDefs.Symbiant));
			UpdateAll();
			UpdateSymbiosisState();
			if (wasSelected)
			{
				Find.Selector.ClearSelection();
				Find.Selector.Select(this, false, false);
			}
			return true;
		}

		bool TryProcessPendingConstructionRepair()
		{
			if (HasPendingConstructionRepair == false)
				return true;
			if (TryEnterFootprintMutation(FootprintMutationKind.ConstructionRepair, false, out _) == false)
				return false;
			var map = Map;
			RevalidatePendingConstructionRepair(map);
			if (pendingConstructionCoveredCells.Count == 0)
			{
				pendingConstructionFootprintCells.Clear();
				lastPlacementGrowthState = "waiting";
				return true;
			}

			var oldRoot = Position;
			EnsureSelectionCoreState();
			EnsureEstablishmentAnchorState(map);
			var oldCore = selectionCoreRelative.IsValid ? Position + selectionCoreRelative : oldRoot;
			var oldAnchor = establishmentAnchorRelative.IsValid ? Position + establishmentAnchorRelative : oldRoot;
			var covered = pendingConstructionCoveredCells
				.OrderBy(cell => cell == oldRoot ? 0 : cell == oldCore ? 1 : cell == oldAnchor ? 2 : 3)
				.ThenBy(cell => orderedCells.FindIndex(relative => Position + relative == cell))
				.ToArray();
			var planned = orderedCells
				.Select(relative => Position + relative)
				.Where(cell => pendingConstructionCoveredCells.Contains(cell) == false)
				.ToHashSet();
			constructionPlacementPlanCount++;
			var placementPlanner = new ConstructionPlacementPlanner(this, map, planned, pendingConstructionFootprintCells);
			var replacements = new Dictionary<IntVec3, IntVec3>();
			foreach (var source in covered)
			{
				var preferredRoom = source == oldAnchor && oldAnchor.InBounds(map) ? oldAnchor.GetRoom(map) : null;
				var target = placementPlanner.NextTarget(IsEligibleIndoorRoom(preferredRoom) ? preferredRoom : null);
				if (target.IsValid == false || placementPlanner.Commit(target) == false)
					continue;
				replacements[source] = target;
			}

			foreach (var source in covered)
				ClearContaminationOnRemovedCell(source - Position);
			if (planned.Count == 0)
			{
				constructionRepairBatchCount++;
				constructionCrushedCellCount += covered.Length;
				pendingConstructionCoveredCells.Clear();
				pendingConstructionFootprintCells.Clear();
				DestroyWithoutHostTrauma(true);
				return true;
			}

			var absoluteOrder = orderedCells
				.Select(relative => Position + relative)
				.Where(planned.Contains)
				.Concat(covered.Where(replacements.ContainsKey).Select(source => replacements[source]))
				.Distinct()
				.ToList();
			var relocatedRoot = replacements.TryGetValue(oldRoot, out var rootReplacement) ? rootReplacement : IntVec3.Invalid;
			var root = new[] { planned.Contains(oldRoot) ? oldRoot : IntVec3.Invalid, relocatedRoot, planned.Contains(oldCore) ? oldCore : IntVec3.Invalid }
				.Concat(absoluteOrder)
				.Where(cell => cell.IsValid && planned.Contains(cell))
				.Distinct()
				.OrderBy(cell => CanUseAsCanonicalRoot(map, cell) ? 0 : 1)
				.ThenBy(cell => cell == oldRoot ? 0 : cell == relocatedRoot ? 1 : cell == oldCore ? 2 : 3)
				.First();
			var core = planned.Contains(oldCore) ? oldCore : replacements.TryGetValue(oldCore, out var relocatedCore) ? relocatedCore : root;
			var anchor = planned.Contains(oldAnchor) ? oldAnchor : replacements.TryGetValue(oldAnchor, out var relocatedAnchor) ? relocatedAnchor : root;
			var repaired = ReplaceAbsoluteFootprint(map, absoluteOrder, root, core, anchor);
			if (repaired)
			{
				constructionRepairBatchCount++;
				constructionRelocatedCellCount += replacements.Count;
				constructionCrushedCellCount += covered.Length - replacements.Count;
				pendingConstructionCoveredCells.Clear();
				pendingConstructionFootprintCells.Clear();
				SynchronizeExteriorOverflowAuthorization(map);
			}
			return repaired;
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
			if (HasPendingConstructionRepair)
			{
				_ = TryProcessPendingConstructionRepair();
				// Construction repair owns this mutation boundary even when the pending set becomes empty.
				// Ordinary movement, migration, growth, and reseeding resume on a later tick.
				return;
			}
			if (IsPlacementTopologySafe(Map))
			{
				var migrationQueueRefreshPending = roomCellMigrationInitialized == false
					|| roomCellMigrationRescanPending
					|| roomCellMigrationNormalizationPending;
				EnsureRoomCellMigrationInitialized(Map);
				if (migrationQueueRefreshPending && roomCellMigrationLookup.Count > 0 && nextMovementTick > ticks)
					nextMovementTick = ticks;
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
			if (pendingFeedGrowthPulses > 0
				&& IsPlacementTopologySafe(Map)
				&& ApplyPendingFeedGrowthPulses() > 0)
				return;
			if (roomCellMigrationLookup.Count == 0
				&& (relocationCellDebt > 0 || nextRelocationPulseTick > 0)
				&& (nextRelocationPulseTick <= 0 || ticks >= nextRelocationPulseTick))
			{
				_ = TryRelocationPulse();
				return;
			}
			if (ticks >= nextMovementTick)
			{
				_ = TryMovePulse(false);
				ResetMovementClock();
			}
			if (pendingFeedGrowthPulses == 0 && CanExpand() && ticks >= nextExpansionTick)
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
			if (TryEnterFootprintMutation(FootprintMutationKind.Expansion, true, out _) == false)
				return false;

			var map = Map;
			SynchronizeExteriorOverflowAuthorization(map);
			var capacity = EvaluateIndoorCapacity(map);
			var target = FindExpansionTarget(capacity, true);
			if (target == null)
				return false;
			var added = target.kind == ExpansionTargetKind.ExteriorWallBreach
				? TryCommitExteriorWallBreach(target)
				: TryCommitExpansionTarget(target);
			if (added == false)
				return false;
			if (target.kind == ExpansionTargetKind.RoomFounding)
			{
				SetEstablishmentAnchor(target.cell);
				SetSelectionCoreInstant(target.cell);
			}
			if (target.kind == ExpansionTargetKind.ExteriorOpen)
				AuthorizeExteriorCell(target.cell);
			SynchronizeExteriorOverflowAuthorization(map);
			RememberMovementCell(target.cell);
			lastPlacementGrowthState = "growing";
			return true;
		}

		public bool CanAcceptFeed(Thing feed)
		{
			if (pendingFeedGrowthPulses > 0 || IsValidFeed(feed) == false || FeedGrowthCells(feed) <= 0)
				return false;
			var ticks = GenTicks.TicksGame;
			if (lastFeedAcceptanceEvaluationTick == ticks && lastFeedAcceptanceShapeVersion == combatShapeVersion)
				return lastFeedAcceptanceResult;
			lastFeedAcceptanceEvaluationTick = ticks;
			lastFeedAcceptanceShapeVersion = combatShapeVersion;
			lastFeedAcceptanceResult = CanApplyFeedGrowth();
			return lastFeedAcceptanceResult;
		}

		bool CanApplyFeedGrowth()
		{
			if (CanExpand() == false)
				return false;
			if (TryEnterFootprintMutation(FootprintMutationKind.Feeding, true, out _) == false)
				return false;
			var map = Map;
			SynchronizeExteriorOverflowAuthorization(map);
			return FindExpansionTarget(EvaluateIndoorCapacity(map), true) != null;
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

		internal bool DebugMovePulse()
		{
			var moved = TryMovePulse(false);
			if (moved)
				ResetMovementClock();
			return moved;
		}

		public bool TryMovePulse(bool allowWallBreak)
		{
			DebugLastMovePulseOrdinaryMoved = false;
			DebugLastMovePulseMigratedRoomCell = false;
			DebugLastMovePulseConnectedRoomCellsRetired = 0;
			DebugLastMigratedRoomCellSource = IntVec3.Invalid;
			DebugLastMigratedRoomCellDestination = IntVec3.Invalid;
			var map = Map;
			if (map == null || CellCount <= 1)
				return false;
			if (TryEnterFootprintMutation(FootprintMutationKind.Movement, false, out _) == false)
				return false;
			SynchronizeExteriorOverflowAuthorization(map);
			RefreshSymbiosisMetrics(false);
			if (orderedCells.Any(relative => ClassifySymbiantCell(map, Position + relative) == SymbiantCellClass.ExteriorOpen))
			{
				// Exterior movement is already a slow pulse. Refresh capacity here so a newly relevant
				// room that did not change topology (for example, newly marked home area) still wins
				// over another outdoor shuffle without adding work to the normal indoor path.
				var capacity = EvaluateIndoorCapacity(map);
				if (capacity.state == IndoorCapacityState.PlacementAvailable)
				{
					nextRelocationPulseTick = GenTicks.TicksGame;
					lastPlacementGrowthState = "relocating";
					return false;
				}
			}
			var targets = MovementTargetCandidates(map);
			if (targets.Count > 0)
			{
				DebugLastMovePulseOrdinaryMoved = ShouldUseAmbientMovement() && TryAmbientMovePulse(map, targets)
					|| TryCorrectiveMovePulse(map, targets);
			}
			if (TryEnterFootprintMutation(FootprintMutationKind.MigrationRepair, false, out _))
				DebugLastMovePulseMigratedRoomCell = TryMigrateQueuedRoomCell(map);
			return DebugLastMovePulseOrdinaryMoved || DebugLastMovePulseMigratedRoomCell;
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
			var source = MovementSourceCandidates(map, target)
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
			var targetPool = targets
				.Where(target => target.score >= scoreFloor)
				.OrderByDescending(target => AmbientTargetWeight(target))
				.Take(AmbientMovementCandidateLimit)
				.ToArray();
			if (preferSelectionCore)
			{
				var coreRoom = SelectionCoreRoom(map);
				foreach (var target in targetPool)
				{
					if (debugTrackSelectionCoreWander)
						debugSelectionCoreWanderPreferredTargets++;
					if (coreRoom == null || target.cell.GetRoom(map) != coreRoom)
						continue;
					var source = MovementSourceCandidate(map, selectionCoreRelative, target);
					if (IsAmbientMoveAllowed(map, currentIntegrated, source, target) == false)
						continue;
					if (source != null && TryCommitMove(map, source, target))
						return true;
				}
			}
			foreach (var target in targetPool)
			{
				var sourceCandidates = MovementSourceCandidates(map, target)
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
			DebugLastSelectionCoreWanderConnectivityChecks = 0;
			DebugLastSelectionCoreWanderPreferredTargets = 0;
			var map = Map;
			if (map == null || CellCount <= 1)
				return false;
			debugSelectionCoreWanderConnectivityChecks = 0;
			debugSelectionCoreWanderPreferredTargets = 0;
			debugTrackSelectionCoreWander = true;
			try
			{
				EnsureSelectionCoreState();
				selectionCoreDiscoveryCue = false;
				selectionCoreLastMoveTick = GenTicks.TicksGame - SelectionCoreWanderDwellTicks;
				RefreshSymbiosisMetrics(false);
				var targets = MovementTargetCandidates(map);
				return targets.Count > 0 && TryAmbientMovePulse(map, targets);
			}
			finally
			{
				debugTrackSelectionCoreWander = false;
				DebugLastSelectionCoreWanderConnectivityChecks = debugSelectionCoreWanderConnectivityChecks;
				DebugLastSelectionCoreWanderPreferredTargets = debugSelectionCoreWanderPreferredTargets;
			}
		}

		List<MovementTarget> MovementTargetCandidates(Map map)
		{
			var targets = new List<MovementTarget>();
			var occupiedRooms = OccupiedRoomCounts(map);
			var seen = new HashSet<IntVec3>();
			foreach (var relative in orderedCells.ToArray())
			{
				if (roomCellMigrationLookup.Contains(relative))
					continue;
				var cell = Position + relative;
				if (ClassifySymbiantCell(map, cell) == SymbiantCellClass.ExteriorOpen
					&& authorizedExteriorCells.Contains(relative) == false)
					continue;
				for (var i = 0; i < 4; i++)
				{
					var candidate = cell + GenAdj.CardinalDirections[i];
					if (candidate.InBounds(map) == false || ContainsCell(candidate) || seen.Add(candidate) == false)
						continue;
					var classification = ClassifySymbiantCell(map, candidate);
					if (classification != SymbiantCellClass.IndoorFloor
						&& classification != SymbiantCellClass.Door
						&& (classification != SymbiantCellClass.ExteriorOpen
							|| exteriorOverflowAuthorized == false
							|| lastIndoorCapacityState == IndoorCapacityState.PlacementAvailable
							|| lastIndoorCapacityState == IndoorCapacityState.NonFullButBlocked))
						continue;
					var room = candidate.GetRoom(map);
					if (IsEligibleIndoorRoom(room)
						&& occupiedRooms.ContainsKey(room)
						&& TouchesEstablishedRoomPatch(map, candidate, room) == false)
						continue;
					targets.Add(new MovementTarget(candidate, ScoreMovementTargetCell(map, candidate), IntegratedCellWeight(map, candidate), classification));
				}
			}
			return targets;
		}

		IEnumerable<MovementSource> MovementSourceCandidates(Map map, MovementTarget target)
		{
			var targetRelative = target.cell - Position;
			var targetComponents = ComponentsTouchingTarget(targetRelative);
			return orderedCells
				.Where(relative => relative != IntVec3.Zero && relative != selectionCoreRelative && targetComponents.Contains(relative))
				.Select(relative => MovementSourceCandidate(map, relative, target, targetComponents))
				.Where(candidate => candidate != null);
		}

		MovementSource MovementSourceCandidate(Map map, IntVec3 relative, MovementTarget target)
		{
			return MovementSourceCandidate(map, relative, target, ComponentsTouchingTarget(target.cell - Position));
		}

		MovementSource MovementSourceCandidate(Map map, IntVec3 relative, MovementTarget target, HashSet<IntVec3> targetComponents)
		{
			var targetRelative = target.cell - Position;
			if (relative == IntVec3.Zero
				|| cells?.Contains(relative) != true
				|| roomCellMigrationLookup.Contains(relative)
				|| targetComponents?.Contains(relative) != true
				|| WouldCellsStayConnectedAfterMove(relative, targetRelative) == false)
				return null;
			var absolute = Position + relative;
			var sourceClassification = ClassifySymbiantCell(map, absolute);
			if (sourceClassification == SymbiantCellClass.ExteriorOpen && authorizedExteriorCells.Contains(relative) == false)
				return null;
			var sourceIndoor = sourceClassification == SymbiantCellClass.IndoorFloor || sourceClassification == SymbiantCellClass.Door;
			var targetIndoor = target.classification == SymbiantCellClass.IndoorFloor || target.classification == SymbiantCellClass.Door;
			if (sourceIndoor != targetIndoor || sourceIndoor == false && sourceClassification != SymbiantCellClass.ExteriorOpen)
				return null;
			return new MovementSource(relative, absolute, ScoreMovementSourceCell(map, absolute), IntegratedCellWeight(map, absolute), sourceClassification);
		}

		HashSet<IntVec3> ComponentsTouchingTarget(IntVec3 targetRelative)
		{
			var result = new HashSet<IntVec3>();
			if (cells == null)
				return result;
			for (var i = 0; i < GenAdj.CardinalDirections.Length; i++)
			{
				var neighbor = targetRelative + GenAdj.CardinalDirections[i];
				if (cells.Contains(neighbor) == false || result.Contains(neighbor))
					continue;
				result.UnionWith(ConnectedCells(cells, neighbor));
			}
			return result;
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
			if (map == null
				|| source == null
				|| target == null
				|| TryEnterFootprintMutation(FootprintMutationKind.Movement, false, out _) == false
				|| ClassifySymbiantCell(map, target.cell) != target.classification)
				return false;
			var targetRelative = target.cell - Position;
			if (ContainsCell(target.cell)
				|| CanPlaceConnectedWithinRoom(map, target.cell, source.relative) == false
				|| WouldCellsStayConnectedAfterMove(source.relative, targetRelative) == false)
				return false;
			var movingSelectionCore = selectionCoreRelative == source.relative;
			var movingEstablishmentAnchor = establishmentAnchorRelative == source.relative;
			var movingAuthorizedExteriorCell = authorizedExteriorCells.Contains(source.relative);
			if (RemoveRelativeCellWithCoreDestination(source.relative, true, movingSelectionCore ? targetRelative : IntVec3.Invalid) == false)
				return false;
			if (AddRelativeCell(targetRelative) == false)
			{
				_ = AddRelativeCell(source.relative);
				if (movingAuthorizedExteriorCell)
					AuthorizeExteriorCell(source.absolute);
				if (movingSelectionCore)
				{
					selectionCoreRelative = source.relative;
					ClearSelectionCoreMotion();
				}
				return false;
			}
			if (movingAuthorizedExteriorCell && target.classification == SymbiantCellClass.ExteriorOpen)
				AuthorizeExteriorCell(target.cell);
			if (movingEstablishmentAnchor)
				SetEstablishmentAnchor(target.cell);
			RebuildCellBounds();
			UpdateAll();
			UpdateSymbiosisState();
			_ = RetireConnectedRoomCellMigrationComponents(map);
			SynchronizeExteriorOverflowAuthorization(map);
			RememberMovement(source.absolute, target.cell);
			return true;
		}

		bool TryMigrateQueuedRoomCell(Map map)
		{
			if (map == null || roomCellMigrationCells == null || roomCellMigrationCells.Count == 0)
				return false;
			roomCellMigrationCells.RemoveAll(relative =>
			{
				if (cells.Contains(relative) == false)
					return true;
				var absolute = Position + relative;
				return absolute.InBounds(map) == false || IsEligibleIndoorRoom(absolute.GetRoom(map)) == false;
			});
			RebuildRoomCellMigrationLookup();
			if (roomCellMigrationCells.Count == 0)
				return false;
			DebugLastMovePulseConnectedRoomCellsRetired += RetireConnectedRoomCellMigrationComponents(map);
			if (roomCellMigrationCells.Count == 0)
				return false;

			var sourceCandidates = roomCellMigrationCells
				.Where(relative => relative != IntVec3.Zero && WouldCellsStayConnectedAfterRemoval(relative))
				.ToList();
			var targetCandidatesByRoom = new Dictionary<Room, IntVec3[]>();
			while (sourceCandidates.Count > 0)
			{
				var sourceIndex = Rand.Range(0, sourceCandidates.Count);
				var sourceRelative = sourceCandidates[sourceIndex];
				sourceCandidates.RemoveAt(sourceIndex);
				var source = Position + sourceRelative;
				var room = source.InBounds(map) ? source.GetRoom(map) : null;
				if (IsEligibleIndoorRoom(room) == false)
					continue;

				_ = PromoteQueuedRoomComponentIfNecessary(map, room);
				if (roomCellMigrationLookup.Contains(sourceRelative) == false)
					continue;
				if (targetCandidatesByRoom.TryGetValue(room, out var targetCandidates) == false)
				{
					targetCandidates = RoomMigrationTargetCandidates(map, room);
					targetCandidatesByRoom.Add(room, targetCandidates);
				}
				if (targetCandidates.Length == 0)
					continue;

				var target = targetCandidates.RandomElement();
				var targetRelative = target - Position;
				var movingSelectionCore = selectionCoreRelative == sourceRelative;
				var movingEstablishmentAnchor = establishmentAnchorRelative == sourceRelative;
				if (TryEnterFootprintMutation(FootprintMutationKind.MigrationRepair, false, out _) == false)
					return false;
				if (RemoveRelativeCellWithCoreDestination(sourceRelative, false, movingSelectionCore ? targetRelative : IntVec3.Invalid, false) == false)
					continue;
				if (AddRelativeCell(targetRelative, false, false) == false)
				{
					_ = AddRelativeCell(sourceRelative, false, false);
					if (roomCellMigrationLookup.Add(sourceRelative))
						roomCellMigrationCells.Add(sourceRelative);
					if (movingSelectionCore)
					{
						selectionCoreRelative = sourceRelative;
						ClearSelectionCoreMotion();
					}
					continue;
				}
				roomCellMigrationCells.Remove(sourceRelative);
				roomCellMigrationLookup.Remove(sourceRelative);
				DebugLastMovePulseConnectedRoomCellsRetired += RetireConnectedRoomCellMigrationComponents(map);
				if (movingSelectionCore)
				{
					selectionCoreRelative = targetRelative;
					selectionCoreLastMoveTick = GenTicks.TicksGame;
					ClearSelectionCoreMotion();
				}
				if (movingEstablishmentAnchor)
					SetEstablishmentAnchor(target);
				cellMotions?.RemoveAll(motion => motion.cell == sourceRelative || motion.cell == targetRelative);
				RebuildCellBounds();
				UpdateAll();
				UpdateSymbiosisState();
				SynchronizeExteriorOverflowAuthorization(map);
				DebugLastMigratedRoomCellSource = source;
				DebugLastMigratedRoomCellDestination = target;
				return true;
			}
			return false;
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
			if (map == null || CellCount == 0)
				return false;
			return orderedCells.Any(relative =>
			{
				var classification = ClassifySymbiantCell(map, Position + relative);
				if (classification == SymbiantCellClass.IndoorIneligible || classification == SymbiantCellClass.InvalidBlocked)
					return true;
				return classification == SymbiantCellClass.ExteriorOpen
					&& (authorizedExteriorCells.Contains(relative) == false
						|| lastIndoorCapacityState == IndoorCapacityState.PlacementAvailable);
			});
		}

		bool HasPriorityRelocationCells(Map map, IndoorCapacityEvaluation capacity)
		{
			if (map == null || orderedCells == null)
				return false;
			return orderedCells.Any(relative =>
			{
				var classification = ClassifySymbiantCell(map, Position + relative);
				if (classification == SymbiantCellClass.IndoorIneligible || classification == SymbiantCellClass.InvalidBlocked)
					return true;
				return classification == SymbiantCellClass.ExteriorOpen
					&& (authorizedExteriorCells.Contains(relative) == false
						|| capacity?.state == IndoorCapacityState.PlacementAvailable);
			});
		}

		bool TryMoveUnintegratedCell(Map map, ExpansionTarget target)
		{
			if (map == null || target == null || CellCount == 0)
				return false;

			var targetRelative = target.cell - Position;
			var relative = orderedCells
				.AsEnumerable()
				.Reverse()
				.Select(cell => new { cell, classification = ClassifySymbiantCell(map, Position + cell) })
				.Where(entry => entry.classification == SymbiantCellClass.ExteriorOpen
					|| entry.classification == SymbiantCellClass.IndoorIneligible
					|| entry.classification == SymbiantCellClass.InvalidBlocked)
				.OrderBy(entry => entry.classification == SymbiantCellClass.ExteriorOpen && authorizedExteriorCells.Contains(entry.cell) == false ? 0 : 1)
				.Where(entry => WouldCellsStayConnectedAfterMove(entry.cell, targetRelative))
				.Select(entry => (IntVec3?)entry.cell)
				.FirstOrDefault();
			if (relative.HasValue == false || relative.Value.IsValid == false || cells.Contains(relative.Value) == false)
				return false;
			var sourceRelative = relative.Value;
			if (CanPlaceConnectedWithinRoom(map, target.cell, sourceRelative) == false)
				return false;

			var source = Position + sourceRelative;
			var movingRoot = sourceRelative == IntVec3.Zero;
			var movingSelectionCore = selectionCoreRelative == sourceRelative;
			var movingEstablishmentAnchor = establishmentAnchorRelative == sourceRelative;
			var movingAuthorizedExteriorCell = authorizedExteriorCells.Contains(sourceRelative);
			if (RemoveRelativeCellWithCoreDestination(sourceRelative, false, movingSelectionCore ? targetRelative : IntVec3.Invalid, false) == false)
				return false;
			if (AddRelativeCell(target.cell - Position, false, false) == false)
			{
				if (AddRelativeCell(sourceRelative, false, false))
				{
					if (movingAuthorizedExteriorCell)
						AuthorizeExteriorCell(source);
					if (movingSelectionCore)
					{
						selectionCoreRelative = sourceRelative;
						ClearSelectionCoreMotion();
					}
					cellMotions?.RemoveAll(motion => motion.cell == sourceRelative);
					RebuildCellBounds();
					UpdateAll();
					UpdateSymbiosisState();
				}
				return false;
			}
			if (movingSelectionCore || target.kind == ExpansionTargetKind.RoomFounding)
				SetSelectionCoreInstant(target.cell);
			if (movingEstablishmentAnchor || target.kind == ExpansionTargetKind.RoomFounding)
				SetEstablishmentAnchor(target.cell);
			cellMotions?.RemoveAll(motion => motion.cell == sourceRelative || motion.cell == targetRelative);
			if (movingRoot)
			{
				if (RebaseFootprint(map, targetRelative) == false)
					return false;
				DebugLastMovePulseConnectedRoomCellsRetired += RetireConnectedRoomCellMigrationComponents(map);
				SynchronizeExteriorOverflowAuthorization(map);
				RememberMovement(source, target.cell);
				return true;
			}
			RebuildCellBounds();
			UpdateAll();
			UpdateSymbiosisState();
			DebugLastMovePulseConnectedRoomCellsRetired += RetireConnectedRoomCellMigrationComponents(map);
			SynchronizeExteriorOverflowAuthorization(map);
			RememberMovement(source, target.cell);
			return true;
		}

		bool TryRelocationPulse()
		{
			var ticks = GenTicks.TicksGame;
			if (ticks < nextRelocationPulseTick)
				return false;

			var map = Map;
			if (map == null)
				return false;
			if (TryEnterFootprintMutation(FootprintMutationKind.Relocation, false, out _) == false)
				return false;
			SynchronizeExteriorOverflowAuthorization(map);
			_ = ReanchorFromInvalidRoot(map);
			var capacity = EvaluateIndoorCapacity(map);
			var target = FindRelocationTarget(map, capacity);
			var priorityRelocationPending = HasPriorityRelocationCells(map, capacity);

			if (target != null && TryMoveUnintegratedCell(map, target))
			{
				nextRelocationPulseTick = relocationCellDebt > 0 || HasMovableUnintegratedCells() ? ticks + RelocationPulseIntervalTicks() : 0;
				lastPlacementGrowthState = "relocating";
				return true;
			}
			if (priorityRelocationPending)
			{
				nextRelocationPulseTick = ticks + PlacementBlockedRetryTicks;
				lastPlacementGrowthState = "contained";
				return false;
			}

			if (relocationCellDebt <= 0)
			{
				nextRelocationPulseTick = 0;
				lastPlacementGrowthState = "contained";
				return false;
			}
			if (CellCount >= MaxCells)
			{
				nextRelocationPulseTick = ticks + PlacementBlockedRetryTicks;
				lastPlacementGrowthState = "contained";
				return false;
			}
			if (target == null && exteriorOverflowAuthorized
				&& (capacity.state == IndoorCapacityState.AllFull || capacity.state == IndoorCapacityState.NoRelevantRooms))
				target = FindExteriorOpenTarget(map);
			if (target == null && exteriorOverflowAuthorized == false && capacity.state == IndoorCapacityState.AllFull)
			{
				var exact = EvaluateIndoorCapacity(map, exactAudit: true);
				if (exact.state == IndoorCapacityState.AllFull)
					target = FindExteriorOpenTarget(map) ?? FindExteriorWallBreachTarget(map);
			}
			var restored = target?.kind == ExpansionTargetKind.ExteriorWallBreach
				? TryCommitExteriorWallBreach(target)
				: target != null && TryCommitExpansionTarget(target);
			if (restored == false)
			{
				nextRelocationPulseTick = ticks + PlacementBlockedRetryTicks;
				lastPlacementGrowthState = "contained";
				return false;
			}
			if (target.kind == ExpansionTargetKind.RoomFounding)
			{
				SetEstablishmentAnchor(target.cell);
				SetSelectionCoreInstant(target.cell);
			}
			RememberMovementCell(target.cell);
			relocationCellDebt = Mathf.Max(0, relocationCellDebt - 1);
			if (target.kind == ExpansionTargetKind.ExteriorOpen)
				AuthorizeExteriorCell(target.cell);
			nextRelocationPulseTick = relocationCellDebt > 0 || HasMovableUnintegratedCells() ? ticks + RelocationPulseIntervalTicks() : 0;
			if (relocationCellDebt == 0)
				ResetExpansionClock();
			return true;
		}

		bool ReanchorFromInvalidRoot(Map map)
		{
			if (map == null || Spawned == false || cells?.Contains(IntVec3.Zero) != true)
				return false;
			var rootClassification = ClassifySymbiantCell(map, Position);
			if (rootClassification == SymbiantCellClass.IndoorFloor
				|| rootClassification == SymbiantCellClass.Door
				|| rootClassification == SymbiantCellClass.ExteriorOpen && authorizedExteriorCells.Contains(IntVec3.Zero))
				return false;
			var newRootRelative = orderedCells
				.Where(relative => relative != IntVec3.Zero)
				.Select(relative => new { relative, classification = ClassifySymbiantCell(map, Position + relative) })
				.Where(entry => entry.classification == SymbiantCellClass.IndoorFloor
					|| entry.classification == SymbiantCellClass.Door
					|| entry.classification == SymbiantCellClass.ExteriorOpen && authorizedExteriorCells.Contains(entry.relative))
				.OrderBy(entry => entry.classification == SymbiantCellClass.IndoorFloor || entry.classification == SymbiantCellClass.Door ? 0 : 1)
				.ThenBy(entry => entry.relative == selectionCoreRelative ? 0 : 1)
				.ThenBy(entry => SelectionCoreClutterScore(entry.relative))
				.ThenBy(entry => entry.relative.DistanceToSquared(selectionCoreRelative.IsValid ? selectionCoreRelative : IntVec3.Zero))
				.Select(entry => entry.relative)
				.FirstOrDefault();
			if (newRootRelative == IntVec3.Zero || cells.Contains(newRootRelative) == false)
				return false;
			return RebaseFootprint(map, newRootRelative);
		}

		bool RebaseFootprint(Map map, IntVec3 newRootRelative)
		{
			if (map == null || Spawned == false || newRootRelative == IntVec3.Zero || cells?.Contains(newRootRelative) != true)
				return false;
			var oldPosition = Position;
			var newPosition = oldPosition + newRootRelative;
			var absoluteCells = orderedCells.Select(relative => oldPosition + relative).ToArray();
			var authorizedAbsoluteCells = authorizedExteriorCells
				.Select(relative => oldPosition + relative)
				.ToArray();
			var coreCell = selectionCoreRelative.IsValid ? oldPosition + selectionCoreRelative : newPosition;
			var establishmentCell = establishmentAnchorRelative.IsValid ? oldPosition + establishmentAnchorRelative : newPosition;
			var wasSelected = Find.Selector?.IsSelected(this) == true;
			temporaryDespawnInProgress = true;
			try
			{
				DeSpawn(DestroyMode.Vanish);
			}
			finally
			{
				temporaryDespawnInProgress = false;
			}

			Position = newPosition;
			cells = absoluteCells.Select(cell => cell - newPosition).ToHashSet();
			orderedCells = absoluteCells.Select(cell => cell - newPosition).ToList();
			authorizedExteriorCells = authorizedAbsoluteCells.Select(cell => cell - newPosition).ToHashSet();
			if (roomCellMigrationCells != null)
				roomCellMigrationCells = roomCellMigrationCells
					.Select(relative => relative - newRootRelative)
					.ToList();
			RebuildRoomCellMigrationLookup();
			selectionCoreRelative = coreCell - newPosition;
			establishmentAnchorRelative = establishmentCell - newPosition;
			selectionCoreLastMoveTick = GenTicks.TicksGame;
			ClearSelectionCoreMotion();
			cellMotions?.Clear();
			hasCellBounds = false;
			lastSymbiosisMetricTick = int.MinValue;
			combatShapeVersion++;
			RebuildCellBounds();

			GenSpawn.Spawn(this, newPosition, map, Rot4.Random, WipeMode.Vanish, false);
			RegisterActiveSymbiant(this, map);
			EnsureVisibleToPawnSystems(map);
			jobs.StartJob(JobMaker.MakeJob(CustomDefs.Symbiant));
			UpdateAll();
			UpdateSymbiosisState();
			if (wasSelected)
			{
				Find.Selector.ClearSelection();
				Find.Selector.Select(this, false, false);
			}
			return true;
		}

		ExpansionTarget FindExpansionTarget(IndoorCapacityEvaluation capacity, bool allowWallBreach)
		{
			var map = Map;
			if (map == null || capacity == null || orderedCells == null || orderedCells.Count == 0)
				return null;
			var classifications = orderedCells
				.Select(relative => new
				{
					relative,
					classification = ClassifySymbiantCell(map, Position + relative)
				})
				.ToArray();
			var hasAuthorizedExterior = classifications.Any(entry =>
				entry.classification == SymbiantCellClass.ExteriorOpen && authorizedExteriorCells.Contains(entry.relative));
			var hasUnauthorizedExterior = classifications.Any(entry =>
				entry.classification == SymbiantCellClass.ExteriorOpen && authorizedExteriorCells.Contains(entry.relative) == false);
			var hasInvalid = classifications.Any(entry =>
				entry.classification == SymbiantCellClass.IndoorIneligible || entry.classification == SymbiantCellClass.InvalidBlocked);
			if (hasInvalid || hasUnauthorizedExterior)
			{
				if (capacity.state == IndoorCapacityState.PlacementAvailable)
					nextRelocationPulseTick = GenTicks.TicksGame;
				lastPlacementGrowthState = capacity.state == IndoorCapacityState.NoRelevantRooms
					? "dormantNoRoom"
					: "contained";
				return null;
			}
			if (hasAuthorizedExterior)
			{
				if (capacity.state == IndoorCapacityState.NoRelevantRooms)
				{
					lastPlacementGrowthState = "dormantNoRoom";
					return null;
				}
				if (capacity.state == IndoorCapacityState.PlacementAvailable)
				{
					nextRelocationPulseTick = GenTicks.TicksGame;
					lastPlacementGrowthState = "relocating";
					return null;
				}
				if (capacity.state == IndoorCapacityState.NonFullButBlocked)
				{
					lastPlacementGrowthState = "contained";
					return null;
				}
				return FindExteriorOpenTarget(map);
			}

			if (capacity.state == IndoorCapacityState.PlacementAvailable)
			{
				var establishmentRoom = EnsureEstablishmentAnchorState(map);
				var establishment = capacity.rooms.FirstOrDefault(room => room.room == establishmentRoom);
				if (establishment != null
					&& establishment.occupied < Mathf.Max(1, Mathf.CeilToInt(establishment.capacity * SymbiantRoomEstablishmentCoverage)))
				{
					if (establishment.hasPlacement)
						return new ExpansionTarget(ExpansionTargetKind.IndoorLocal, establishment.placementCell, null, establishment.placementScore, establishment.room);
					lastPlacementGrowthState = "contained";
					return null;
				}

				var foundingTarget = FindRoomFoundingTarget(map, capacity, establishmentRoom);
				if (foundingTarget != null)
					return foundingTarget;
				var occupiedRoomTarget = capacity.rooms
					.Where(room => room.occupied > 0 && room.hasPlacement)
					.OrderBy(room => room.ProjectedCoverage)
					.ThenByDescending(room => room.roomScore)
					.ThenByDescending(room => room.placementScore)
					.FirstOrDefault();
				if (occupiedRoomTarget != null)
					return new ExpansionTarget(ExpansionTargetKind.IndoorLocal, occupiedRoomTarget.placementCell, null, occupiedRoomTarget.placementScore, occupiedRoomTarget.room);
				if (capacity.HasDoorTarget)
					return new ExpansionTarget(ExpansionTargetKind.Door, capacity.doorTarget, null, capacity.doorTargetScore);
				return null;
			}
			if (capacity.state != IndoorCapacityState.AllFull)
			{
				lastPlacementGrowthState = capacity.state == IndoorCapacityState.NoRelevantRooms ? "dormantNoRoom" : "contained";
				return null;
			}

			var exact = EvaluateIndoorCapacity(map, exactAudit: true);
			if (exact.state != IndoorCapacityState.AllFull)
			{
				lastPlacementGrowthState = exact.state == IndoorCapacityState.PlacementAvailable ? "relocating" : "contained";
				return null;
			}
			var openExterior = FindExteriorOpenTarget(map);
			if (openExterior != null)
				return openExterior;
			return allowWallBreach ? FindExteriorWallBreachTarget(map) : null;
		}

		Room EnsureEstablishmentAnchorState(Map map)
		{
			if (map == null)
				return null;
			var previousCell = establishmentAnchorRelative.IsValid ? Position + establishmentAnchorRelative : IntVec3.Invalid;
			var previousRoom = previousCell.InBounds(map) ? previousCell.GetRoom(map) : null;
			if (establishmentAnchorRelative.IsValid
				&& cells?.Contains(establishmentAnchorRelative) == true
				&& ClassifySymbiantCell(map, previousCell) == SymbiantCellClass.IndoorFloor
				&& IsEligibleIndoorRoom(previousRoom))
				return previousRoom;

			var candidates = orderedCells
				.Select(relative => new { relative, absolute = Position + relative })
				.Where(entry => ClassifySymbiantCell(map, entry.absolute) == SymbiantCellClass.IndoorFloor)
				.Select(entry => new { entry.relative, entry.absolute, room = entry.absolute.GetRoom(map) })
				.Where(entry => IsEligibleIndoorRoom(entry.room))
				.ToArray();
			var replacement = candidates
				.Where(entry => entry.room == previousRoom)
				.OrderBy(entry => entry.absolute.DistanceToSquared(previousCell))
				.FirstOrDefault();
			replacement ??= candidates
				.GroupBy(entry => entry.room)
				.OrderByDescending(group => group.Count())
				.ThenByDescending(group => group.Any(entry => entry.relative == IntVec3.Zero))
				.SelectMany(group => group.OrderBy(entry => entry.relative == IntVec3.Zero ? 0 : 1))
				.FirstOrDefault();
			if (replacement == null)
			{
				establishmentAnchorRelative = IntVec3.Invalid;
				return null;
			}
			establishmentAnchorRelative = replacement.relative;
			return replacement.room;
		}

		Room SelectionCoreRoom(Map map)
		{
			if (map == null)
				return null;
			EnsureSelectionCoreState();
			var cell = selectionCoreRelative.IsValid ? Position + selectionCoreRelative : Position;
			var room = cell.InBounds(map) ? cell.GetRoom(map) : null;
			return IsEligibleIndoorRoom(room) ? room : null;
		}

		void SetEstablishmentAnchor(IntVec3 absolute)
		{
			establishmentAnchorRelative = absolute.IsValid && ContainsCell(absolute) ? absolute - Position : IntVec3.Invalid;
		}

		void SetSelectionCoreInstant(IntVec3 absolute)
		{
			if (absolute.IsValid == false || ContainsCell(absolute) == false)
				return;
			selectionCoreRelative = absolute - Position;
			selectionCoreLastMoveTick = GenTicks.TicksGame;
			ClearSelectionCoreMotion();
		}

		void SynchronizeExteriorOverflowAuthorization(Map map)
		{
			authorizedExteriorCells ??= [];
			if (map == null || IsPlacementTopologySafe(map) == false)
				return;

			if (exteriorOverflowScopeInitialized == false)
			{
				authorizedExteriorCells.Clear();
				if (exteriorOverflowAuthorized)
				{
					var exteriorCells = orderedCells
						.Where(relative => ClassifySymbiantCell(map, Position + relative) == SymbiantCellClass.ExteriorOpen)
						.ToHashSet();
					var seed = orderedCells
						.AsEnumerable()
						.Reverse()
						.Where(exteriorCells.Contains)
						.Select(relative => (IntVec3?)relative)
						.FirstOrDefault();
					if (seed.HasValue)
						authorizedExteriorCells.UnionWith(ConnectedCells(exteriorCells, seed.Value));
				}
				exteriorOverflowScopeInitialized = true;
			}

			authorizedExteriorCells.RemoveWhere(relative =>
				cells?.Contains(relative) != true
				|| ClassifySymbiantCell(map, Position + relative) != SymbiantCellClass.ExteriorOpen);
			exteriorOverflowAuthorized = authorizedExteriorCells.Count > 0;
		}

		void AuthorizeExteriorCell(IntVec3 absolute)
		{
			if (absolute.IsValid == false || ContainsCell(absolute) == false)
				return;
			authorizedExteriorCells ??= [];
			authorizedExteriorCells.Add(absolute - Position);
			exteriorOverflowScopeInitialized = true;
			exteriorOverflowAuthorized = true;
		}

		int RoomEstablishmentRequirement(Map map, Room room)
		{
			if (map == null || room == null)
				return 1;
			var validCells = room.Cells.Count(cell => CanOccupyOpenCell(map, cell));
			return Mathf.Max(1, Mathf.CeilToInt(validCells * SymbiantRoomEstablishmentCoverage));
		}

		Dictionary<Room, int> OccupiedRoomCounts(Map map)
		{
			var counts = new Dictionary<Room, int>();
			if (map == null || orderedCells == null)
				return counts;
			foreach (var relative in orderedCells)
			{
				var cell = Position + relative;
				var room = cell.InBounds(map) ? cell.GetRoom(map) : null;
				if (IsEligibleIndoorRoom(room) == false)
					continue;
				counts[room] = counts.TryGetValue(room, out var count) ? count + 1 : 1;
			}
			return counts;
		}

		bool CanPlaceConnectedWithinRoom(Map map, IntVec3 candidate, IntVec3? movingSourceRelative = null)
		{
			if (map == null || candidate.InBounds(map) == false)
				return false;
			var room = candidate.GetRoom(map);
			if (IsEligibleIndoorRoom(room) == false)
				return true;

			var hasOccupiedRoomCell = orderedCells.Any(relative =>
			{
				if (movingSourceRelative.HasValue && relative == movingSourceRelative.Value)
					return false;
				var absolute = Position + relative;
				return absolute.InBounds(map) && absolute.GetRoom(map) == room;
			});
			return hasOccupiedRoomCell == false || TouchesEstablishedRoomPatch(map, candidate, room, movingSourceRelative);
		}

		bool TouchesEstablishedRoomPatch(Map map, IntVec3 candidate, Room room, IntVec3? movingSourceRelative = null)
		{
			if (map == null || room == null)
				return false;
			foreach (var direction in GenAdj.CardinalDirections)
			{
				var neighbor = candidate + direction;
				var relative = neighbor - Position;
				if (movingSourceRelative.HasValue && relative == movingSourceRelative.Value)
					continue;
				if (cells.Contains(relative) == false || neighbor.InBounds(map) == false || neighbor.GetRoom(map) != room)
					continue;
				if (roomCellMigrationLookup.Contains(relative))
					continue;
				return true;
			}
			return false;
		}

		ExpansionTarget FindRoomFoundingTarget(Map map, IndoorCapacityEvaluation capacity, Room activeRoom)
		{
			if (map == null || capacity == null)
				return null;
			var occupiedRooms = capacity.rooms.Where(room => room.occupied > 0).ToArray();
			var nextRoom = capacity.rooms
				.Where(room => room.Empty && room.hasPlacement)
				.Select(room => new
				{
					room,
					adjacentToActive = activeRoom != null && RoomsAreAdjacentForSymbiant(map, activeRoom, room.room),
					adjacent = occupiedRooms.Any(source => RoomsAreAdjacentForSymbiant(map, source.room, room.room))
				})
				.OrderBy(candidate => candidate.adjacentToActive ? 0 : 1)
				.ThenBy(candidate => candidate.adjacent ? 0 : 1)
				.ThenByDescending(candidate => candidate.room.roomScore)
				.ThenByDescending(candidate => candidate.room.placementScore)
				.FirstOrDefault();
			return nextRoom == null
				? null
				: new ExpansionTarget(ExpansionTargetKind.RoomFounding, nextRoom.room.placementCell, null, nextRoom.room.placementScore, nextRoom.room.room, true);
		}

		ExpansionTarget FindRelocationTarget(Map map, IndoorCapacityEvaluation capacity = null)
		{
			if (map == null)
				return null;
			capacity ??= EvaluateIndoorCapacity(map);
			if (capacity.state != IndoorCapacityState.PlacementAvailable)
				return null;
			var roomTarget = capacity.rooms
				.Where(room => room.hasPlacement)
				.OrderBy(room => room.Empty ? 0 : 1)
				.ThenBy(room => room.ProjectedCoverage)
				.ThenByDescending(room => room.roomScore)
				.ThenByDescending(room => room.placementScore)
				.FirstOrDefault();
			if (roomTarget != null)
				return new ExpansionTarget(
					roomTarget.Empty ? ExpansionTargetKind.RoomFounding : ExpansionTargetKind.IndoorLocal,
					roomTarget.placementCell,
					null,
					roomTarget.placementScore,
					roomTarget.room,
					true
				);
			return capacity.HasDoorTarget
				? new ExpansionTarget(ExpansionTargetKind.Door, capacity.doorTarget, null, capacity.doorTargetScore, null, true)
				: null;
		}

		static bool RoomsAreAdjacentForSymbiant(Map map, Room source, Room destination)
		{
			if (map == null || source == null || destination == null || source == destination)
				return false;
			foreach (var sourceCell in source.Cells)
			{
				foreach (var direction in GenAdj.CardinalDirections)
				{
					var boundary = sourceCell + direction;
					if (boundary.InBounds(map) == false)
						continue;
					if (boundary.GetRoom(map) == destination)
						return true;
					if (IsDoorCell(map, boundary) == false && BreakableConstructedWall(map, boundary) == null)
						continue;
					if ((boundary + direction).InBounds(map) && (boundary + direction).GetRoom(map) == destination)
						return true;
				}
			}
			return false;
		}

		ExpansionTarget FindExteriorOpenTarget(Map map)
		{
			return ExteriorOpenTargets(map)
				.OrderByDescending(target => target.score)
				.FirstOrDefault();
		}

		List<ExpansionTarget> ExteriorOpenTargets(Map map)
		{
			var targets = new List<ExpansionTarget>();
			if (map == null)
				return targets;
			var seen = new HashSet<IntVec3>();
			IEnumerable<IntVec3> sourceCells = exteriorOverflowAuthorized
				? orderedCells.Where(relative => authorizedExteriorCells.Contains(relative))
				: orderedCells;
			foreach (var relative in sourceCells)
			{
				if (roomCellMigrationLookup.Contains(relative))
					continue;
				var source = Position + relative;
				if (exteriorOverflowAuthorized
					&& ClassifySymbiantCell(map, source) != SymbiantCellClass.ExteriorOpen)
					continue;
				foreach (var direction in GenAdj.CardinalDirections)
				{
					var candidate = source + direction;
					if (candidate.InBounds(map) == false || ContainsCell(candidate) || seen.Add(candidate) == false)
						continue;
					if (ClassifySymbiantCell(map, candidate) == SymbiantCellClass.ExteriorOpen)
						targets.Add(new ExpansionTarget(ExpansionTargetKind.ExteriorOpen, candidate, null, ScoreExpansionCell(map, candidate), candidate.GetRoom(map)));
				}
			}
			return targets;
		}

		bool TouchesAuthorizedExteriorFootprint(IntVec3 candidate)
		{
			return GenAdj.CardinalDirections.Any(direction =>
				authorizedExteriorCells.Contains(candidate + direction - Position));
		}

		bool WallRemovalKeepsIndoorRoomsSeparated(Map map, IntVec3 wallCell, Room sourceRoom)
		{
			if (map == null || sourceRoom == null)
				return false;
			var adjacentIndoorRooms = GenAdj.CardinalDirections
				.Select(direction => wallCell + direction)
				.Where(cell => cell.InBounds(map))
				.Select(cell => cell.GetRoom(map))
				.Where(IsEligibleIndoorRoom)
				.Distinct()
				.ToArray();
			return adjacentIndoorRooms.Length == 1 && adjacentIndoorRooms[0] == sourceRoom;
		}

		ExpansionTarget FindExteriorWallBreachTarget(Map map)
		{
			if (map == null || exteriorOverflowAuthorized)
				return null;
			var targets = new List<ExpansionTarget>();
			var seen = new HashSet<IntVec3>();
			foreach (var relative in orderedCells)
			{
				if (roomCellMigrationLookup.Contains(relative))
					continue;
				var source = Position + relative;
				if (ClassifySymbiantCell(map, source) != SymbiantCellClass.IndoorFloor)
					continue;
				var sourceRoom = source.GetRoom(map);
				foreach (var direction in GenAdj.CardinalDirections)
				{
					var wallCell = source + direction;
					if (wallCell.InBounds(map) == false || ContainsCell(wallCell) || seen.Add(wallCell) == false)
						continue;
					var wall = BreakableConstructedWall(map, wallCell);
					var beyond = wallCell + direction;
					if (wall == null
						|| beyond.InBounds(map) == false
						|| IsMapEdgeCell(map, beyond)
						|| ClassifySymbiantCell(map, beyond) != SymbiantCellClass.ExteriorOpen
						|| WallRemovalKeepsIndoorRoomsSeparated(map, wallCell, sourceRoom) == false)
						continue;
					targets.Add(new ExpansionTarget(
						ExpansionTargetKind.ExteriorWallBreach,
						wallCell,
						wall,
						ScoreExpansionCell(map, beyond),
						beyond.GetRoom(map),
						false,
						beyond
					));
				}
			}
			return targets.OrderByDescending(target => target.score).FirstOrDefault();
		}

		bool TryCommitExpansionTarget(ExpansionTarget target)
		{
			var map = Map;
			if (map == null || target == null || target.kind == ExpansionTargetKind.ExteriorWallBreach || ContainsCell(target.cell))
				return false;
			var classification = ClassifySymbiantCell(map, target.cell);
			var valid = target.kind switch
			{
				ExpansionTargetKind.IndoorLocal => classification == SymbiantCellClass.IndoorFloor && CanPlaceConnectedWithinRoom(map, target.cell),
				ExpansionTargetKind.RoomFounding => classification == SymbiantCellClass.IndoorFloor && CanPlaceConnectedWithinRoom(map, target.cell),
				ExpansionTargetKind.Door => classification == SymbiantCellClass.Door && GenAdj.CardinalDirections.Any(direction => ContainsCell(target.cell + direction)),
				ExpansionTargetKind.ExteriorOpen => classification == SymbiantCellClass.ExteriorOpen
					&& (exteriorOverflowAuthorized
						? TouchesAuthorizedExteriorFootprint(target.cell)
						: GenAdj.CardinalDirections.Any(direction => ContainsCell(target.cell + direction))),
				_ => false
			};
			if (valid == false || CellCount >= MaxCells)
				return false;
			if (AddRelativeCell(target.cell - Position, target.remote == false) == false)
				return false;
			RebuildCellBounds();
			UpdateAll();
			UpdateSymbiosisState();
			return true;
		}

		bool TryCommitExteriorWallBreach(ExpansionTarget target, bool forceFailureAfterDestroy = false)
		{
			var map = Map;
			if (map == null
				|| target?.kind != ExpansionTargetKind.ExteriorWallBreach
				|| exteriorOverflowAuthorized
				|| CellCount >= MaxCells
				|| target.wall == null
				|| target.wall.Destroyed
				|| ContainsCell(target.cell)
				|| BreakableConstructedWall(map, target.cell) != target.wall
				|| IsMapEdgeCell(map, target.exteriorDestination)
				|| ClassifySymbiantCell(map, target.exteriorDestination) != SymbiantCellClass.ExteriorOpen)
				return false;
			var sourceRoom = GenAdj.CardinalDirections
				.Select(direction => target.cell + direction)
				.Where(cell => cell.InBounds(map) && ContainsCell(cell))
				.Select(cell => cell.GetRoom(map))
				.FirstOrDefault(IsEligibleIndoorRoom);
			if (sourceRoom == null || WallRemovalKeepsIndoorRoomsSeparated(map, target.cell, sourceRoom) == false)
				return false;
			var wallDef = target.wall.def;
			var wallStuff = target.wall.Stuff;
			var wallFaction = target.wall.Faction;
			var wallRotation = target.wall.Rotation;
			var wallHitPoints = target.wall.HitPoints;
			EnsureSharedHealth();
			var added = false;
			try
			{
				target.wall.Destroy(DestroyMode.KillFinalize);
				added = forceFailureAfterDestroy == false
					&& Destroyed == false
					&& Spawned
					&& AddRelativeCell(target.cell - Position, false, false);
			}
			catch (Exception exception)
			{
				Log.Error($"Symbiant exterior-wall commit failed at {target.cell}: {exception}");
			}
			if (added == false)
			{
				var targetRelative = target.cell - Position;
				if (cells?.Contains(targetRelative) == true)
					_ = RemoveRelativeCell(targetRelative, false);
				if (target.cell.InBounds(map) && target.cell.GetEdifice(map) == null)
				{
					var replacement = ThingMaker.MakeThing(wallDef, wallStuff) as Building;
					if (replacement != null)
					{
						GenSpawn.Spawn(replacement, target.cell, map, wallRotation, WipeMode.Vanish, false);
						replacement.SetFaction(wallFaction);
						replacement.HitPoints = Mathf.Clamp(wallHitPoints, 1, replacement.MaxHitPoints);
					}
				}
				RebuildCellBounds();
				UpdateAll();
				UpdateSymbiosisState();
				return false;
			}
			RebuildCellBounds();
			UpdateAll();
			UpdateSymbiosisState();
			AuthorizeExteriorCell(target.cell);
			return true;
		}

		internal IntVec3 DebugForceExteriorWallCommitRollback()
		{
			if (CanExpand() == false
				|| TryEnterFootprintMutation(FootprintMutationKind.Debug, true, out _) == false)
				return IntVec3.Invalid;
			var map = Map;
			SynchronizeExteriorOverflowAuthorization(map);
			var target = FindExpansionTarget(EvaluateIndoorCapacity(map), true);
			if (target?.kind != ExpansionTargetKind.ExteriorWallBreach)
				return IntVec3.Invalid;
			var wallCell = target.cell;
			return TryCommitExteriorWallBreach(target, true) ? IntVec3.Invalid : wallCell;
		}

		static bool IsMapEdgeCell(Map map, IntVec3 cell)
		{
			return map == null
				|| cell.x <= 0
				|| cell.z <= 0
				|| cell.x >= map.Size.x - 1
				|| cell.z >= map.Size.z - 1;
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

		bool TouchesRoomPatch(Map map, IntVec3 candidate, Room room, HashSet<IntVec3> footprint, HashSet<IntVec3> excludedSources = null)
		{
			if (map == null || room == null || footprint == null)
				return false;
			foreach (var direction in GenAdj.CardinalDirections)
			{
				var neighbor = candidate + direction;
				if (footprint.Contains(neighbor) == false || excludedSources?.Contains(neighbor) == true)
					continue;
				if (neighbor.InBounds(map) && neighbor.GetRoom(map) == room)
					return true;
			}
			return false;
		}

		IndoorCapacityEvaluation EvaluateIndoorCapacity(Map map, bool exactAudit = false)
		{
			var evaluation = new IndoorCapacityEvaluation();
			if (map == null)
				return evaluation;

			capacityEvaluationCount++;
			if (exactAudit)
				exactCapacityAuditCount++;
			var footprint = orderedCells.Select(relative => Position + relative).ToHashSet();
			var excludedEstablishedSources = roomCellMigrationLookup
				.Select(relative => Position + relative)
				.ToHashSet();
			var relevantRooms = CandidateRooms(map)
				.Concat(footprint
					.Where(cell => cell.InBounds(map))
					.Select(cell => cell.GetRoom(map))
					.Where(IsEligibleIndoorRoom))
				.Distinct()
				.ToArray();

			foreach (var room in relevantRooms)
			{
				var record = new RoomCapacityRecord
				{
					room = room,
					roomScore = ScoreSpawnRoom(map, room)
				};
				var usableCells = new List<IntVec3>();
				var bestScore = float.MinValue;
				foreach (var cell in room.Cells)
				{
					evaluation.roomCellScans++;
					if (ClassifySymbiantCell(map, cell) != SymbiantCellClass.IndoorFloor)
						continue;
					usableCells.Add(cell);
					record.capacity++;
					if (footprint.Contains(cell))
						record.occupied++;
				}
				foreach (var cell in usableCells)
				{
					if (footprint.Contains(cell))
						continue;
					var legal = record.occupied > 0
						? TouchesRoomPatch(map, cell, room, footprint, excludedEstablishedSources)
						: CanOccupyInitialSpawnCell(map, cell);
					if (legal == false)
						continue;
					var score = ScoreMovementTargetCell(map, cell);
					if (record.hasPlacement == false || score > bestScore)
					{
						record.hasPlacement = true;
						record.placementCell = cell;
						record.placementScore = score;
						bestScore = score;
					}
				}
				if (record.Empty && record.hasPlacement == false)
				{
					foreach (var cell in usableCells.Where(cell => footprint.Contains(cell) == false && CanOccupyFurnishedFoundingCell(map, cell)))
					{
						var score = ScoreMovementTargetCell(map, cell);
						if (record.hasPlacement && score <= bestScore)
							continue;
						record.hasPlacement = true;
						record.placementCell = cell;
						record.placementScore = score;
						bestScore = score;
					}
				}
				if (record.capacity > 0)
					evaluation.rooms.Add(record);
			}

			var roomSet = evaluation.rooms.Select(record => record.room).ToHashSet();
			var bestDoorScore = float.MinValue;
			foreach (var source in footprint)
			{
				if (roomCellMigrationLookup.Contains(source - Position))
					continue;
				foreach (var direction in GenAdj.CardinalDirections)
				{
					var candidate = source + direction;
					if (footprint.Contains(candidate))
						continue;
					if (ClassifySymbiantCell(map, candidate) != SymbiantCellClass.Door)
						continue;
					var belongsToRelevantRoom = GenAdj.CardinalDirections
						.Select(adjacentDirection => candidate + adjacentDirection)
						.Where(adjacent => adjacent.InBounds(map))
						.Select(adjacent => adjacent.GetRoom(map))
						.Any(roomSet.Contains);
					if (belongsToRelevantRoom == false)
						continue;
					var score = ScoreMovementTargetCell(map, candidate);
					if (evaluation.HasDoorTarget == false || score > bestDoorScore)
					{
						evaluation.doorTarget = candidate;
						evaluation.doorTargetScore = score;
						bestDoorScore = score;
					}
				}
			}

			roomCellScanCount += evaluation.roomCellScans;
			if (evaluation.rooms.Count == 0)
				evaluation.state = IndoorCapacityState.NoRelevantRooms;
			else if (evaluation.HasDoorTarget || evaluation.rooms.Any(room => room.hasPlacement))
				evaluation.state = IndoorCapacityState.PlacementAvailable;
			else if (evaluation.rooms.Any(room => room.Full == false))
				evaluation.state = IndoorCapacityState.NonFullButBlocked;
			else
				evaluation.state = IndoorCapacityState.AllFull;

			lastIndoorCapacityState = evaluation.state;
			lastPlacementEvaluationTick = GenTicks.TicksGame;
			return evaluation;
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
				{
					added++;
					var remaining = pulseSize - i - 1;
					if (remaining > 0 && IsPlacementTopologySafe(Map) == false)
					{
						pendingFeedGrowthPulses += remaining;
						break;
					}
				}
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

		int ApplyPendingFeedGrowthPulses()
		{
			var added = 0;
			while (pendingFeedGrowthPulses > 0 && TryExpansionPulse())
			{
				pendingFeedGrowthPulses--;
				added++;
			}
			if (added <= 0)
				return 0;
			lastRecessionPulseCells += added;
			if (Spawned)
				MoteMaker.ThrowText(DrawPos, Map, "SymbiantFedMote".Translate(added, added), 3.65f);
			return added;
		}

		internal int DebugApplyPendingFeedGrowthPulses() => ApplyPendingFeedGrowthPulses();

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
				if (pawn?.RaceProps?.IsFlesh != true || AlienTools.IsFleshPawn(pawn) == false)
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
				if (cell != IntVec3.Zero && WouldCellsStayConnectedAfterRemoval(cell))
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
			if (TryEnterFootprintMutation(FootprintMutationKind.Retreat, false, out _) == false)
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
				SynchronizeExteriorOverflowAuthorization(Map);
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
			UpdateDrawCullSize(renderBounds);

			var allCells = cells.ToArray();
			var cellCount = Mathf.Min(allCells.Length, MAX_METABALLS);
			metaballRadiusByCell.Clear();

			for (var i = 0; i < cellCount; i++)
			{
				var cell = allCells[i];
				var cellRadius = Mathf.Clamp(GetSize(cell) * MetaballCellRadiusFactor, MetaballCellRadiusMin, MetaballCellRadiusMax);
				metaballRadiusByCell[cell] = cellRadius;
			}

			RebuildRenderPatches();
			BuildMetaballRenderElements();
		}

		void RebuildRenderPatches()
		{
			ReleaseMetaballPatchResources();
			var components = ConnectedComponents(cells)
				.OrderBy(component => orderedCells.FindIndex(component.Contains))
				.ToArray();
			foreach (var component in components)
			{
				var patch = new MetaballRenderPatch();
				patch.cells.UnionWith(component);
				foreach (var cell in component)
				{
					renderPatchByCell[cell] = patch;
					ExpandRenderPatchBounds(patch, cell);
				}
				renderPatches.Add(patch);
			}

			if (cellMotions != null)
			{
				foreach (var motion in cellMotions.Where(motion => GenTicks.TicksGame < motion.endTick))
				{
					if (renderPatchByCell.TryGetValue(motion.cell, out var patch) == false)
					{
						patch = renderPatches
							.OrderBy(candidate => DistanceToRenderPatchSquared(candidate, motion.to))
							.FirstOrDefault();
						if (patch != null && DistanceToRenderPatchSquared(patch, motion.to) > MetaballInfluenceRadiusCells * MetaballInfluenceRadiusCells)
							patch = null;
						if (patch == null)
						{
							patch = new MetaballRenderPatch();
							renderPatches.Add(patch);
						}
					}
					renderPatchByMotion[motion] = patch;
					ExpandRenderPatchBounds(patch, motion.from);
					ExpandRenderPatchBounds(patch, motion.to);
				}
			}

			foreach (var patch in renderPatches)
				ConfigureRenderPatchBounds(patch);
		}

		static float DistanceToRenderPatchSquared(MetaballRenderPatch patch, Vector2 point)
		{
			if (patch?.hasBounds != true)
				return float.MaxValue;
			var x = Mathf.Clamp(point.x, patch.bounds.minX, patch.bounds.maxX);
			var z = Mathf.Clamp(point.y, patch.bounds.minZ, patch.bounds.maxZ);
			return (point - new Vector2(x, z)).sqrMagnitude;
		}

		static void ExpandRenderPatchBounds(MetaballRenderPatch patch, IntVec3 cell)
		{
			if (patch.hasBounds)
				patch.bounds = patch.bounds.Encapsulate(cell);
			else
			{
				patch.bounds = CellRect.SingleCell(cell);
				patch.hasBounds = true;
			}
		}

		static void ExpandRenderPatchBounds(MetaballRenderPatch patch, Vector2 point)
		{
			ExpandRenderPatchBounds(patch, new IntVec3(Mathf.FloorToInt(point.x), 0, Mathf.FloorToInt(point.y)));
			ExpandRenderPatchBounds(patch, new IntVec3(Mathf.CeilToInt(point.x), 0, Mathf.CeilToInt(point.y)));
		}

		static void ConfigureRenderPatchBounds(MetaballRenderPatch patch)
		{
			if (patch.hasBounds == false)
				return;
			var minX = patch.bounds.minX - 1f;
			var minZ = patch.bounds.minZ - 1f;
			var maxX = patch.bounds.maxX + 1f;
			var maxZ = patch.bounds.maxZ + 1f;
			patch.centerX = (minX + maxX) / 2f;
			patch.centerZ = (minZ + maxZ) / 2f;
			patch.renderMinX = minX;
			patch.renderMinZ = minZ;
			patch.renderWidth = Mathf.Max(1f, maxX - minX);
			patch.renderHeight = Mathf.Max(1f, maxZ - minZ);
			patch.geometryDirty = true;
			patch.textureDirty = true;
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
			}
			if (removed && (cellMotions == null || cellMotions.Count == 0))
				lastCellMotionRenderTick = -1;
		}

		void BuildMetaballRenderElements()
		{
			metaballRenderElements.Clear();
			foreach (var patch in renderPatches)
				patch.elements.Clear();
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
				renderPatchByCell.TryGetValue(pair.Key, out var patch);
				AddMetaballRenderElement(center, radius, radiusScale, patch);
			}

			if (cellMotions == null)
				return;
			for (var i = 0; i < cellMotions.Count; i++)
			{
				var motion = cellMotions[i];
				if (ticks < motion.endTick && motion.outgoing)
				{
					renderPatchByMotion.TryGetValue(motion, out var patch);
					AddMetaballRenderElement(motion.CurrentCenter(ticks), motion.radius, motion.CurrentRadiusScale(ticks), patch);
				}
			}
			foreach (var patch in renderPatches)
				patch.textureDirty = true;
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

		void AddMetaballRenderElement(Vector2 center, float radius, float radiusScale, MetaballRenderPatch patch)
		{
			if (radius <= 0.0001f || patch == null)
				return;
			var element = new MetaballRenderElement(center, radius, radiusScale);
			metaballRenderElements.Add(element);
			patch.elements.Add(element);
		}

		void UpdateMetaballTexture(MetaballRenderPatch patch)
		{
			if (patch?.texture == null || metaballMaskMaterial == null)
				return;

			UploadMetaballBuffer(patch);
			metaballMaskMaterial.SetInt(MetaballCountId, patch.elements.Count);
			metaballMaskMaterial.SetVector(MetaballWorldSizeId, new Vector4(patch.renderWidth, patch.renderHeight, patch.renderMinX, patch.renderMinZ));
			var previous = RenderTexture.active;
			try
			{
				Graphics.Blit(Texture2D.blackTexture, patch.texture, metaballMaskMaterial);
			}
			finally
			{
				RenderTexture.active = previous;
			}
		}

		void UploadMetaballBuffer(MetaballRenderPatch patch)
		{
			var count = patch.elements.Count;
			EnsureMetaballBufferCapacity(Mathf.Max(1, count));
			if (metaballBufferData.Length < Mathf.Max(1, count))
				metaballBufferData = new MetaballBufferData[Mathf.NextPowerOfTwo(Mathf.Max(1, count))];

			for (var i = 0; i < count; i++)
			{
				var element = patch.elements[i];
				var centerU = Mathf.Clamp01((element.center.x - patch.renderMinX) / Mathf.Max(0.0001f, patch.renderWidth));
				var centerV = Mathf.Clamp01((element.center.y - patch.renderMinZ) / Mathf.Max(0.0001f, patch.renderHeight));
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

		void EnsureMetaballTextureResolution(MetaballRenderPatch patch)
		{
			if (patch == null)
				return;
			var textureWidth = DesiredMetaballTextureSize(patch.renderWidth);
			var textureHeight = DesiredMetaballTextureSize(patch.renderHeight);
			if (patch.texture != null && patch.texture.width == textureWidth && patch.texture.height == textureHeight && patch.texture.IsCreated())
				return;

			if (patch.texture != null)
				UnityEngine.Object.Destroy(patch.texture);
			patch.texture = new RenderTexture(textureWidth, textureHeight, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
			{
				name = $"ZombieSymbiantMetaballs_{renderPatches.IndexOf(patch)}_{textureWidth}x{textureHeight}",
				wrapMode = TextureWrapMode.Clamp,
				filterMode = FilterMode.Bilinear,
				useMipMap = false,
				autoGenerateMips = false
			};
			patch.texture.Create();
			patch.textureDirty = true;
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
			roomCellMigrationCells ??= [];
			authorizedExteriorCells ??= [];
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
			var removedMigrationCells = roomCellMigrationCells.RemoveAll(cell => cells.Contains(cell) == false);
			if (removedMigrationCells > 0
				|| roomCellMigrationNormalizationPending == false && roomCellMigrationLookup.Count != roomCellMigrationCells.Count)
				RebuildRoomCellMigrationLookup();
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
			pendingFeedGrowthPulses = Mathf.Max(0, pendingFeedGrowthPulses);
			relocationCellDebt = Mathf.Max(0, relocationCellDebt);
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
				EnsureMetaballMaskMaterial();
				EnsureMetaballMaterial();
				EnsureSelectionCoreResources();
				metaballPropertyBlock ??= new MaterialPropertyBlock();
				if (renderPatches.Count > 0 || metaballMaterial != null || metaballMaskMaterial != null || selectionCoreMaterial != null || selectionCoreTexture != null || selectionCoreMesh != null)
					renderResourceOwners.Add(this);
				return renderPatches.Count > 0 && metaballMaterial != null && metaballMaskMaterial != null;
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
			if (hasCellBounds == false || renderPatches.Count == 0)
				UpdateAll();
			if (hasCellBounds == false || renderPatches.Count == 0)
				return false;
			if (debugForceMetaballFallback)
				return false;
			if (EnsureRenderResources() == false)
				return false;

			var prepared = 0;
			foreach (var patch in renderPatches)
			{
				EnsureMetaballTextureResolution(patch);
				if (patch.geometryDirty || patch.mesh == null)
				{
					if (patch.mesh != null)
						UnityEngine.Object.Destroy(patch.mesh);
					patch.mesh = MeshMakerPlanes.NewPlaneMesh(new Vector2(patch.renderWidth, patch.renderHeight), false, false, false);
					patch.geometryDirty = false;
				}
				if (patch.textureDirty)
				{
					UpdateMetaballTexture(patch);
					patch.textureDirty = false;
				}
				if (patch.mesh != null && patch.texture != null)
					prepared++;
			}
			return prepared == renderPatches.Count && prepared > 0;
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
					color = Color.white
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

			UpdateMetaballMaterialTime();
			UpdateSelectionCoreInteractionState();
			UpdateSelectedAppearance();
			foreach (var patch in renderPatches)
			{
				var offset = new Vector3(patch.centerX, 0f, patch.centerZ);
				var position = drawLoc + offset;
				position.y = AltitudeLayer.MoteLow.AltitudeFor(SymbiantRenderAltitudeOffset);
				metaballPropertyBlock.Clear();
				metaballPropertyBlock.SetTexture(MainTextureId, patch.texture);
				Graphics.DrawMesh(patch.mesh, position, Quaternion.identity, metaballMaterial, 0, null, 0, metaballPropertyBlock);
			}
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

			if (DebugSettings.ShowDevGizmos == false)
				yield break;

			yield return new Command_Action
			{
				defaultLabel = "DEV: Add Cell",
				defaultDesc = "Trigger one normal Symbiant expansion pulse.",
				action = () =>
				{
					if (DebugExpansionPulse() == false)
						Log.Warning("Could not add a Symbiant cell. The Symbiant may be capped or have no valid expansion target.");
				}
			};
			yield return new Command_Action
			{
				defaultLabel = "DEV: Remove Cell",
				defaultDesc = "Trigger one normal Symbiant shrink pulse.",
				action = () =>
				{
					if (DebugShrinkPulse() == false)
						Log.Warning("Could not remove a Symbiant cell.");
				}
			};
			yield return new Command_Action
			{
				defaultLabel = "DEV: Move Symbiant",
				defaultDesc = "Trigger one normal Symbiant cell relocation.",
				action = () =>
				{
					if (DebugMovePulse() == false)
						Log.Warning("Could not move a Symbiant cell.");
				}
			};
			yield return new Command_Action
			{
				defaultLabel = "DEV: Assign/Unassign",
				defaultDesc = LinkedHost == null
					? "Choose an eligible colonist to assign to this Symbiant."
					: $"Unassign {LinkedHost.LabelShortCap} from this Symbiant.",
				action = () =>
				{
					if (LinkedHost != null)
					{
						if (DebugUnassignHost() == false)
							Log.Warning("Could not unassign the Symbiant host.");
						return;
					}
					OpenDebugHostAssignmentMenu();
				}
			};
		}

		void OpenDebugHostAssignmentMenu()
		{
			var candidates = DebugEligibleHosts();
			if (candidates.Length == 0)
			{
				Messages.Message("No eligible colonists are available for this Symbiant.", MessageTypeDefOf.RejectInput, false);
				return;
			}
			var options = candidates
				.Select(pawn => new FloatMenuOption(
					pawn.LabelShortCap.ToString(),
					() =>
					{
						if (DebugAssignHost(pawn) == false)
							Log.Warning($"Could not assign {pawn.LabelShortCap} to the Symbiant.");
					}
				))
				.ToList();
			Find.WindowStack.Add(new FloatMenu(options));
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Collections.Look(ref cells, "cells", LookMode.Value);
			Scribe_Collections.Look(ref orderedCells, "orderedCells", LookMode.Value);
			Scribe_Collections.Look(ref roomCellMigrationCells, "roomCellMigrationCells", LookMode.Value);
			Scribe_Values.Look(ref roomCellMigrationInitialized, "roomCellMigrationInitialized");
			Scribe_Values.Look(ref roomCellMigrationRescanPending, "roomCellMigrationRescanPending");
			Scribe_Values.Look(ref radius, "radius", elementRadius * 9f);
			Scribe_Values.Look(ref power, "power", elementPower);
			Scribe_Values.Look(ref nextExpansionTick, "nextExpansionTick");
			Scribe_Values.Look(ref nextMovementTick, "nextMovementTick");
			Scribe_Values.Look(ref nextAutoHealTick, "nextAutoHealTick");
			Scribe_Values.Look(ref nextBenefitCellThreshold, "nextBenefitCellThreshold");
			Scribe_Values.Look(ref benefitStepCells, "benefitStepCells");
			Scribe_Values.Look(ref feedPausedUntilTick, "feedPausedUntilTick");
			Scribe_Values.Look(ref pendingFeedGrowthPulses, "pendingFeedGrowthPulses");
			Scribe_Values.Look(ref lastRecessionPulseCells, "lastRecessionPulseCells");
			Scribe_Values.Look(ref relocationCellDebt, "relocationCellDebt");
			Scribe_Values.Look(ref nextRelocationPulseTick, "nextRelocationPulseTick");
			Scribe_Values.Look(ref uprootedSinceTick, "uprootedSinceTick", -1);
			Scribe_Values.Look(ref exteriorOverflowAuthorized, "exteriorOverflowAuthorized");
			Scribe_Collections.Look(ref authorizedExteriorCells, "authorizedExteriorCells", LookMode.Value);
			Scribe_Values.Look(ref exteriorOverflowScopeInitialized, "exteriorOverflowScopeInitialized");
			Scribe_Values.Look(ref establishmentAnchorRelative, "establishmentAnchorRelative", IntVec3.Invalid);
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
				roomCellMigrationNormalizationPending = true;
				roomCellMigrationRescanPending = true;
				postLoadConstructionValidationPending = true;
				pendingConstructionCoveredCells.Clear();
				pendingConstructionFootprintCells.Clear();
				if (host != null)
					hostThingId = host.ThingID;
				RefreshSymbiosisMetrics(true);
				EnsureBenefitDefaults();
				EnsureHostHediff();
				NormalizeDamageEchoHistory();
				_ = PruneAnatomyOnlyDamageHediffs();
				if (sharedHealth >= 0f && nextSharedHealthRecoveryTick <= 0)
					nextSharedHealthRecoveryTick = GenTicks.TicksGame + SymbiantSharedHealthRecoveryDelayTicks;
				SyncHostDamageEchoes();
			}
		}

		sealed class ExpansionTarget
		{
			public readonly ExpansionTargetKind kind;
			public readonly IntVec3 cell;
			public readonly Building wall;
			public readonly float score;
			public readonly Room room;
			public readonly bool remote;
			public readonly IntVec3 exteriorDestination;

			public ExpansionTarget(
				ExpansionTargetKind kind,
				IntVec3 cell,
				Building wall,
				float score,
				Room room = null,
				bool remote = false,
				IntVec3 exteriorDestination = default)
			{
				this.kind = kind;
				this.cell = cell;
				this.wall = wall;
				this.score = score;
				this.room = room;
				this.remote = remote;
				this.exteriorDestination = exteriorDestination;
			}
		}

		sealed class MovementTarget
		{
			public readonly IntVec3 cell;
			public readonly float score;
			public readonly float integratedWeight;
			public readonly SymbiantCellClass classification;

			public MovementTarget(IntVec3 cell, float score, float integratedWeight, SymbiantCellClass classification)
			{
				this.cell = cell;
				this.score = score;
				this.integratedWeight = integratedWeight;
				this.classification = classification;
			}
		}

		sealed class MovementSource
		{
			public readonly IntVec3 relative;
			public readonly IntVec3 absolute;
			public readonly float score;
			public readonly float integratedWeight;
			public readonly SymbiantCellClass classification;

			public MovementSource(IntVec3 relative, IntVec3 absolute, float score, float integratedWeight, SymbiantCellClass classification)
			{
				this.relative = relative;
				this.absolute = absolute;
				this.score = score;
				this.integratedWeight = integratedWeight;
				this.classification = classification;
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
