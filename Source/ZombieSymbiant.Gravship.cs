using RimWorld;
using RimWorld.Planet;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace ZombieLand
{
	// Odyssey gravship travel. Vanilla despawns the pawns aboard at launch, keeps them in the
	// Gravship world object during the flight and in saves, and respawns them at a rotated
	// offset on landing. The footprint state is relative to the root cell, so landing rotates
	// it and then reruns the post-load repair on the first topology-safe tick.
	public partial class ZombieSymbiant
	{
		static ZombieSymbiant launchingSymbiant;
		static Rot4? landingRotation;
		bool gravshipTransit;

		internal bool DebugInGravshipTransit => gravshipTransit;

		// Prefix of GravshipUtility.GenerateGravship, before vanilla captures the ship.
		internal static void PrepareGravshipLaunch(Building_GravEngine engine)
		{
			launchingSymbiant = null;
			var map = engine?.Map;
			var symbiant = ActiveSymbiant(map);
			var substructure = engine?.ValidSubstructure;
			if (symbiant == null || symbiant.Spawned == false || substructure == null)
				return;
			// Same test as WorldComponent_GravshipController.InitiateTakeoff.
			var originAbandoned = map.listerThings.AnyThingWithDef(ThingDefOf.GravAnchor) == false;
			if (symbiant.PrepareGravshipLaunch(map, substructure, originAbandoned))
				launchingSymbiant = symbiant;
		}

		// The Symbiant boards when most of its cells are aboard. If the origin map will be
		// abandoned, one cell aboard is enough. Cells that stay ashore become relocation debt.
		bool PrepareGravshipLaunch(Map map, HashSet<IntVec3> substructure, bool originAbandoned)
		{
			gravshipTransit = false;
			EnsureSymbiantDefaults();
			bool Aboard(IntVec3 relative) => substructure.Contains(Position + relative);
			var aboard = orderedCells.Where(Aboard).ToList();
			var ashore = orderedCells.Where(relative => Aboard(relative) == false).ToList();
			var boards = aboard.Count > 0 && (originAbandoned || aboard.Count * 2 > orderedCells.Count);
			if (boards == false)
			{
				// Vanilla takes every pawn whose root cell is aboard, so keep the root ashore.
				if (Aboard(IntVec3.Zero) && (ashore.Count == 0 || RebaseFootprint(map, PreferredGravshipRoot(map, ashore)) == false))
					DestroyWithoutHostTrauma(true);
				return false;
			}
			if (Aboard(IntVec3.Zero) == false && RebaseFootprint(map, PreferredGravshipRoot(map, aboard)) == false)
				return false;

			var cellsLeftAshore = orderedCells.Where(relative => Aboard(relative) == false).ToArray();
			foreach (var relative in cellsLeftAshore)
			{
				ClearContaminationOnRemovedCell(relative);
				_ = RemoveRelativeCell(relative, false);
			}
			relocationCellDebt += cellsLeftAshore.Length;
			if (cells.Contains(selectionCoreRelative) == false)
				selectionCoreRelative = IntVec3.Zero;
			if (cells.Contains(establishmentAnchorRelative) == false)
				establishmentAnchorRelative = selectionCoreRelative;
			ClearSelectionCoreMotion();
			cellMotions?.Clear();
			hasCellBounds = false;
			RebuildCellBounds();
			gravshipTransit = true;
			return true;
		}

		IntVec3 PreferredGravshipRoot(Map map, List<IntVec3> candidates)
		{
			var core = selectionCoreRelative.IsValid ? selectionCoreRelative : IntVec3.Zero;
			return candidates
				.Where(relative => relative != IntVec3.Zero)
				.OrderBy(relative => ClassifySymbiantCell(map, Position + relative) is SymbiantCellClass.IndoorFloor or SymbiantCellClass.Door ? 0 : 1)
				.ThenBy(relative => relative == core ? 0 : 1)
				.ThenBy(relative => relative.DistanceToSquared(core))
				.FirstOrDefault();
		}

		// Finalizer of GravshipUtility.GenerateGravship, so it also runs when vanilla throws.
		// A prepared Symbiant that vanilla despawned without taking along must not survive
		// outside a map.
		internal static void ConfirmGravshipLaunch(Gravship gravship)
		{
			var symbiant = launchingSymbiant;
			launchingSymbiant = null;
			if (symbiant == null || symbiant.gravshipTransit == false || symbiant.Destroyed)
				return;
			if (symbiant.Spawned == false && gravship?.ContainsPawn(symbiant) == true)
				return;
			symbiant.gravshipTransit = false;
			if (symbiant.Spawned == false)
				symbiant.DestroyWithoutHostTrauma(true);
		}

		// Prefix of GravshipPlacementUtility.PlaceGravshipInMap, before vanilla spawns the pawns.
		internal static void PrepareGravshipLanding(Gravship gravship, Map map)
		{
			landingRotation = gravship?.Rotation;
			var arriving = gravship?.Pawns?
				.OfType<ZombieSymbiant>()
				.Where(symbiant => symbiant.Destroyed == false && symbiant.Discarded == false)
				.ToArray() ?? [];
			var resident = ActiveSymbiant(map);
			foreach (var symbiant in arriving)
			{
				if (resident == null && symbiant.gravshipTransit)
				{
					resident = symbiant;
					continue;
				}
				// One Symbiant per map: the one already living here stays.
				symbiant.gravshipTransit = false;
				symbiant.DestroyWithoutHostTrauma(true);
				Find.LetterStack?.ReceiveLetter(
					"LetterLabelSymbiantLostOnLanding".Translate(),
					"SymbiantLostOnLanding".Translate(),
					LetterDefOf.NeutralEvent,
					resident != null ? new LookTargets(resident) : null
				);
			}
		}

		internal static void FinishGravshipLanding()
		{
			landingRotation = null;
		}

		// Prefix of GravshipUtility.AbandonMap: a Symbiant that stayed on the abandoned map dies.
		internal static void NotifyLeftBehindByGravship(IEnumerable<ZombieSymbiant> symbiants)
		{
			if (symbiants.Any() == false)
				return;
			Find.LetterStack?.ReceiveLetter(
				"LetterLabelSymbiantLeftBehind".Translate(),
				"SymbiantLeftBehind".Translate(),
				LetterDefOf.NeutralEvent
			);
		}

		void RotateFootprintForGravshipLanding(Rot4 rotation)
		{
			EnsureSymbiantDefaults();
			IntVec3 Rotate(IntVec3 relative) => relative.IsValid ? PrefabUtility.GetAdjustedLocalPosition(relative, rotation) : relative;
			cells = cells.Select(Rotate).ToHashSet();
			orderedCells = orderedCells.Select(Rotate).ToList();
			authorizedExteriorCells = authorizedExteriorCells.Select(Rotate).ToHashSet();
			roomCellMigrationCells = roomCellMigrationCells?.Select(Rotate).ToList() ?? [];
			RebuildRoomCellMigrationLookup();
			selectionCoreRelative = Rotate(selectionCoreRelative);
			establishmentAnchorRelative = Rotate(establishmentAnchorRelative);
			selectionCoreLastMoveTick = GenTicks.TicksGame;
			ClearSelectionCoreMotion();
			cellMotions?.Clear();
			recentMovementCells.Clear();
			hasCellBounds = false;
			combatShapeVersion++;
			RebuildCellBounds();
		}

		void FinishGravshipArrival()
		{
			gravshipTransit = false;
			roomCellMigrationNormalizationPending = true;
			roomCellMigrationRescanPending = true;
			postLoadConstructionValidationPending = true;
			pendingConstructionCoveredCells.Clear();
			pendingConstructionFootprintCells.Clear();
			lastSymbiosisMetricTick = int.MinValue;
			if (relocationCellDebt > 0)
				nextRelocationPulseTick = GenTicks.TicksGame + RelocationPulseIntervalTicks();
			// The bond may stay active across the flight, so no bond transition dirties the
			// host's capacities. Anything that read them during the flight saw no link.
			NotifyHostCapacityBenefitsChanged(ResolveHost());
		}
	}
}
