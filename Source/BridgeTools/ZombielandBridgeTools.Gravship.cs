using HarmonyLib;
using RimBridgeServer.Sdk;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace ZombieLand
{
	public sealed partial class ZombielandBridgeTools
	{
		[Tool("zombieland/symbiant_gravship_travel_contract", Description = "Drive a Symbiant through the real Odyssey gravship capture and placement. Phase launch builds a ship beside a base room, captures it with GravshipUtility.GenerateGravship and puts it in flight. Save and load between launch and land to cover a mid-flight save. Phase land places the ship back with a rotation. Phase observe reports the Symbiant after ticks.")]
		public static object SymbiantGravshipTravelContract(
			[ToolParameter(Description = "launch, land or observe.", Required = false, DefaultValue = "launch")] string phase = "launch",
			[ToolParameter(Description = "launch: add a grav anchor and put most Symbiant cells ashore, so the Symbiant must stay.", Required = false, DefaultValue = false)] bool anchor = false,
			[ToolParameter(Description = "land: rotation 0-3 (North, East, South, West).", Required = false, DefaultValue = 1)] int rotation = 1,
			[ToolParameter(Description = "land: spawn a resident Symbiant first, so the arriving one must yield.", Required = false, DefaultValue = false)] bool resident = false)
		{
			try
			{
				if (CurrentMap == null || ModsConfig.OdysseyActive == false)
					return new { success = false, error = "Needs a loaded map and Odyssey." };
				return phase switch
				{
					"launch" => GravshipLaunchPhase(CurrentMap, anchor),
					"land" => GravshipLandPhase(CurrentMap, new Rot4(rotation), resident),
					"observe" => GravshipObservePhase(CurrentMap),
					_ => new { success = false, error = $"Unknown phase {phase}." }
				};
			}
			catch (Exception ex)
			{
				return new { success = false, error = ex.ToString() };
			}
		}

		static object GravshipLaunchPhase(Map map, bool anchor)
		{
			if (ZombieSymbiant.ActiveSymbiant(map) != null)
				return new { success = false, error = "An active Symbiant already exists on the current map." };
			// 9x9 ship with hull walls, its east wall shared with a 7x7 base room.
			var center = FindGravshipFixtureCenter(map);
			if (center.IsValid == false)
				return new
				{
					success = false,
					error = "No clear area for the gravship fixture.",
					mapSize = map.Size.ToString(),
					fogged = map.AllCells.Count(cell => cell.Fogged(map)),
					roofed = map.AllCells.Count(cell => cell.Roofed(map)),
					notStandable = map.AllCells.Count(cell => cell.Standable(map) == false),
					foundation = map.AllCells.Count(cell => map.terrainGrid.FoundationAt(cell) != null),
					noHeavy = map.AllCells.Count(cell => cell.GetTerrain(map).affordances?.Contains(TerrainAffordanceDefOf.Heavy) != true)
				};
			var ship = CellRect.CenteredOn(center, 4);
			var baseRoom = new CellRect(ship.maxX, center.z - 3, 7, 7);
			var hullDoor = new IntVec3(ship.maxX, 0, center.z);
			foreach (var cell in ship.ExpandedBy(1).Cells.Concat(baseRoom.ExpandedBy(1).Cells))
				foreach (var thing in cell.GetThingList(map).ToArray())
					if (thing is not Pawn && thing.Destroyed == false)
						thing.Destroy(DestroyMode.Vanish);
			var substructure = DefDatabase<TerrainDef>.GetNamed("Substructure");
			foreach (var cell in ship.Cells)
			{
				// Substructure needs natural ground, so lift any laid floor first.
				if (map.terrainGrid.UnderTerrainAt(cell) != null)
					map.terrainGrid.RemoveTopLayer(cell, false);
				map.terrainGrid.SetFoundation(cell, substructure);
			}
			foreach (var cell in ship.EdgeCells.Concat(baseRoom.EdgeCells).Distinct())
				SpawnPlayerBuilding(map, cell == hullDoor ? ThingDefOf.Door : ThingDefOf.Wall, cell);
			foreach (var cell in ship.ContractedBy(1).Cells.Concat(baseRoom.ContractedBy(1).Cells))
				map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);
			var engine = (Building_GravEngine)SpawnPlayerBuilding(map, ThingDefOf.GravEngine, center);
			// Vanilla takes the launch direction from the pilot console.
			var console = SpawnPlayerBuilding(map, ThingDefOf.PilotConsole, new IntVec3(center.x - 1, 0, ship.maxZ - 1));
			if (anchor)
				_ = SpawnPlayerBuilding(map, ThingDefOf.GravAnchor, new IntVec3(baseRoom.maxX + 4, 0, center.z));
			map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();

			var engineRect = engine.OccupiedRect();
			var consoleRect = console.OccupiedRect();
			var hostCell = new IntVec3(ship.minX + 1, 0, ship.minZ + 1);
			var shipCells = ship.ContractedBy(1).Cells
				.Where(cell => engineRect.Contains(cell) == false && consoleRect.Contains(cell) == false && cell != hostCell)
				.OrderBy(cell => cell.DistanceToSquared(hullDoor))
				.ToList();
			var baseCells = baseRoom.ContractedBy(1).Cells
				.OrderBy(cell => cell.DistanceToSquared(hullDoor))
				.ToList();
			// Board: root ashore, 10 of 14 cells aboard. Stay: root aboard, 4 of 14 cells aboard.
			var aboardCells = new[] { hullDoor }.Concat(shipCells.Take(anchor ? 3 : 9)).ToList();
			var ashoreCells = baseCells.Take(anchor ? 10 : 4).ToList();
			var root = anchor ? shipCells[0] : baseCells[0];
			var symbiant = ZombieSymbiant.DebugSpawnForRendering(map, root, aboardCells.Concat(ashoreCells));
			if (symbiant == null)
				return new { success = false, error = "Symbiant spawn failed." };
			var host = SpawnGravshipFixtureHost(map, hostCell, symbiant);
			if (host == null)
				return new { success = false, error = "No eligible host could be assigned." };

			var substructureCount = engine.ValidSubstructure.Count;
			var cellsBefore = symbiant.CellCount;
			var debtBefore = symbiant.RelocationCellDebt;
			var gravship = GravshipUtility.GenerateGravship(engine);
			var inGravship = gravship?.ContainsPawn(symbiant) == true;
			var evidence = new
			{
				center = center.ToString(),
				substructureCount,
				shipSize = ship.Area,
				cellsBefore,
				aboardBefore = aboardCells.Count,
				ashoreBefore = ashoreCells.Count,
				symbiant = ZombieRuntimeActions.StableThingId(symbiant),
				host = ZombieRuntimeActions.StableThingId(host),
				boardingEnabled = symbiant.def.bringAlongOnGravship,
				inGravship,
				hostInGravship = gravship?.ContainsPawn(host) == true,
				symbiantSpawned = symbiant.Spawned,
				symbiantDestroyed = symbiant.Destroyed,
				inTransit = symbiant.DebugInGravshipTransit,
				symbiantInWorldPawns = Find.WorldPawns.Contains(symbiant),
				// GravshipUtility_AbandonMap_Patch destroys exactly these Symbiants.
				stillMapBound = ZombieSymbiant.MapBoundSymbiants(map).Contains(symbiant),
				cellsAfter = symbiant.CellCount,
				debtAdded = symbiant.RelocationCellDebt - debtBefore,
				rootAshoreAfter = symbiant.Spawned && ship.Contains(symbiant.Position) == false,
				linked = symbiant.LinkedHost == host
			};
			if (gravship != null)
			{
				GravshipUtility.TravelTo(gravship, map.Tile, map.Tile);
				Find.TickManager.Pause();
				CameraJumper.TryJump(new GlobalTargetInfo(center, map));
			}
			var success = gravship != null
				&& substructureCount == ship.Area
				&& evidence.boardingEnabled
				&& evidence.hostInGravship
				&& evidence.symbiantDestroyed == false
				&& evidence.symbiantInWorldPawns == false
				&& evidence.linked
				&& (anchor
					? evidence.inGravship == false && evidence.symbiantSpawned && evidence.inTransit == false && evidence.rootAshoreAfter && evidence.cellsAfter == cellsBefore && evidence.debtAdded == 0
					: evidence.inGravship && evidence.symbiantSpawned == false && evidence.stillMapBound == false && evidence.inTransit && evidence.cellsAfter == aboardCells.Count && evidence.debtAdded == ashoreCells.Count);
			return new { success, variant = anchor ? "stay" : "board", evidence };
		}

		static object GravshipLandPhase(Map map, Rot4 rotation, bool resident)
		{
			var gravship = Find.WorldObjects.AllWorldObjects.OfType<Gravship>().FirstOrDefault() ?? Current.Game.Gravship;
			if (gravship == null)
				return new { success = false, error = "No gravship in flight." };
			var arriving = gravship.Pawns.OfType<ZombieSymbiant>().FirstOrDefault();
			var stayed = ZombieSymbiant.ActiveSymbiant(map);
			var host = (arriving ?? stayed)?.LinkedHost;
			var transitRelative = arriving?.AbsoluteCells.Select(cell => cell - arriving.Position).ToArray() ?? [];
			var transitFlag = arriving?.DebugInGravshipTransit ?? false;
			// Gravship does not save originalPosition, so land at the engine's last position.
			var root = gravship.Engine?.Position ?? gravship.originalPosition;
			var residentSymbiant = default(ZombieSymbiant);
			if (resident && stayed == null)
			{
				var cell = GenRadial.RadialCellsAround(root, 40f, true)
					.FirstOrDefault(c => c.InBounds(map) && c.DistanceTo(root) > 20f && c.Standable(map) && c.GetFirstPawn(map) == null);
				residentSymbiant = ZombieSymbiant.DebugSpawnForRendering(map, cell, [cell]);
			}
			var lettersBefore = Find.LetterStack.LettersListForReading.ToHashSet();

			AccessTools.Field(typeof(Gravship), "rotation").SetValue(gravship, rotation);
			if (Find.WorldObjects.Contains(gravship))
				Find.WorldObjects.Remove(gravship);
			_ = AccessTools.Method(typeof(WorldComponent_GravshipController), "PlaceGravship")
				.Invoke(Find.GravshipController, [gravship, root, map]);
			Current.Game.Gravship = null;
			// A flight to the same tile arrives on its first tick and opens vanilla's landing
			// confirmation. This contract places the ship itself, so close that confirmation.
			var landingMarker = AccessTools.Field(typeof(WorldComponent_GravshipController), "landingMarker");
			if (landingMarker.GetValue(Find.GravshipController) is Thing marker && marker.Spawned)
				marker.Destroy(DestroyMode.Vanish);
			landingMarker.SetValue(Find.GravshipController, null);
			Find.DesignatorManager.Deselect();

			var newLetters = Find.LetterStack.LettersListForReading.Where(letter => lettersBefore.Contains(letter) == false).Select(letter => letter.Label.ToString()).ToArray();
			var landed = arriving != null && arriving.Spawned ? arriving : null;
			var expected = transitRelative.Select(relative => PrefabUtility.GetAdjustedLocalPosition(relative, rotation)).ToHashSet();
			var landedCells = landed?.AbsoluteCells.ToArray() ?? [];
			var active = ZombieSymbiant.ActiveSymbiant(map);
			var evidence = new
			{
				rotation = rotation.ToStringHuman(),
				transitFlag,
				arrivingCells = transitRelative.Length,
				arrivingDestroyed = arriving?.Destroyed ?? false,
				landedSpawned = landed != null,
				landedInTransit = landed?.DebugInGravshipTransit ?? false,
				rotatedAsExpected = landed != null && landedCells.Select(cell => cell - landed.Position).ToHashSet().SetEquals(expected),
				landedOnSubstructure = landedCells.All(cell => map.terrainGrid.FoundationAt(cell)?.IsSubstructure == true),
				landedWalkable = landedCells.All(cell => cell.Walkable(map)),
				hostSpawned = host?.Spawned == true && host.Map == map,
				hostLinkedTo = ZombieRuntimeActions.StableThingId(host != null ? ZombieSymbiant.LinkedSymbiantFor(host) : null),
				bondActive = active != null && host != null && active.IsActiveBondWith(host),
				residentStillActive = residentSymbiant != null && active == residentSymbiant,
				stayedSymbiant = ZombieRuntimeActions.StableThingId(stayed),
				newLetters
			};
			var lostLetter = "LetterLabelSymbiantLostOnLanding".Translate().ToString();
			var success = evidence.hostSpawned && (
				resident
					? evidence.arrivingDestroyed && evidence.residentStillActive && evidence.hostLinkedTo == null && newLetters.Contains(lostLetter)
					: stayed != null
						? active == stayed && evidence.bondActive
						: transitFlag && evidence.landedSpawned && evidence.landedInTransit == false && evidence.rotatedAsExpected && evidence.landedOnSubstructure && evidence.landedWalkable && active == landed && evidence.bondActive);
			return new { success, variant = resident ? "resident" : stayed != null ? "stay" : "board", evidence };
		}

		static object GravshipObservePhase(Map map)
		{
			var symbiant = ZombieSymbiant.ActiveSymbiant(map);
			if (symbiant == null)
				return new { success = false, error = "No active Symbiant on the current map." };
			var host = symbiant.LinkedHost;
			return new
			{
				success = symbiant.Spawned && host != null && symbiant.IsActiveBondWith(host),
				cells = symbiant.CellCount,
				relocationDebt = symbiant.RelocationCellDebt,
				bondActive = host != null && symbiant.IsActiveBondWith(host),
				placement = symbiant.DebugPlacementDiagnostics()
			};
		}

		static IntVec3 FindGravshipFixtureCenter(Map map)
		{
			bool Clear(IntVec3 cell) => cell.InBounds(map)
				&& cell.Fogged(map) == false
				&& cell.Roofed(map) == false
				&& cell.Standable(map)
				&& cell.GetEdifice(map) == null
				&& cell.GetFirstPawn(map) == null
				&& map.terrainGrid.FoundationAt(cell) == null
				&& cell.GetTerrain(map).affordances?.Contains(TerrainAffordanceDefOf.Heavy) == true;
			// Summed-area table over clear cells, so each candidate rectangle costs O(1).
			var width = map.Size.x;
			var height = map.Size.z;
			var sums = new int[(width + 1) * (height + 1)];
			for (var z = 0; z < height; z++)
				for (var x = 0; x < width; x++)
					sums[(z + 1) * (width + 1) + x + 1] = (Clear(new IntVec3(x, 0, z)) ? 1 : 0)
						+ sums[z * (width + 1) + x + 1]
						+ sums[(z + 1) * (width + 1) + x]
						- sums[z * (width + 1) + x];
			int ClearCount(int minX, int minZ, int maxX, int maxZ) => sums[(maxZ + 1) * (width + 1) + maxX + 1]
				- sums[minZ * (width + 1) + maxX + 1]
				- sums[(maxZ + 1) * (width + 1) + minX]
				+ sums[minZ * (width + 1) + minX];
			return map.AllCells
				.Where(center => center.x - 7 >= 0 && center.z - 7 >= 0 && center.x + 18 < width && center.z + 7 < height)
				.OrderBy(center => center.DistanceToSquared(map.Center))
				.Where(center => ClearCount(center.x - 7, center.z - 7, center.x + 18, center.z + 7) == 26 * 15)
				.DefaultIfEmpty(IntVec3.Invalid)
				.First();
		}

		static Thing SpawnPlayerBuilding(Map map, ThingDef def, IntVec3 cell)
		{
			var thing = ThingMaker.MakeThing(def, def.MadeFromStuff ? ThingDefOf.Steel : null);
			thing.SetFaction(Faction.OfPlayer);
			return GenSpawn.Spawn(thing, cell, map, WipeMode.Vanish);
		}

		static Pawn SpawnGravshipFixtureHost(Map map, IntVec3 cell, ZombieSymbiant symbiant)
		{
			for (var attempt = 0; attempt < 10; attempt++)
			{
				var pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				_ = GenSpawn.Spawn(pawn, cell, map);
				if (symbiant.DebugAssignHost(pawn))
					return pawn;
				pawn.Destroy(DestroyMode.Vanish);
			}
			return null;
		}
	}
}
