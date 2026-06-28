using RimBridgeServer.Sdk;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	public sealed partial class ZombielandBridgeTools
	{
		[Tool("zombieland/destination_validity_fast_path_contract", Description = "Compare legacy pawn path-context destination validity with the zombie normal-pathing fast path on live current-map zombies.")]
		public static object DestinationValidityFastPathContract(
			[ToolParameter(Description = "Maximum live zombies to scan.", Required = false, DefaultValue = 400)] int limit = 400)
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			var tickManager = map.GetComponent<TickManager>();
			var zombies = tickManager?.allZombiesCached?
				.Where(zombie => zombie != null && zombie.Spawned && zombie.Dead == false)
				.Take(Math.Max(1, limit))
				.ToList() ?? new List<Zombie>();

			var mismatches = new List<object>();
			var samples = new List<object>();
			var scannedCells = 0;
			var normalContextCount = 0;
			var specialContextCount = 0;
			var fenceBlockedCount = 0;
			var flyingCount = 0;

			foreach (var zombie in zombies)
			{
				var legacyContext = map.pathing.For(zombie);
				var usesNormalContext = ReferenceEquals(legacyContext, map.pathing.Normal);
				if (usesNormalContext)
					normalContextCount++;
				else
					specialContextCount++;
				if (zombie.FenceBlocked || zombie.ShouldAvoidFences)
					fenceBlockedCount++;
				if (zombie.Flying)
					flyingCount++;

				var cells = CandidateDestinationCells(zombie);
				var zombieMismatches = new List<object>();
				foreach (var cell in cells)
				{
					scannedCells++;
					var legacy = LegacyHasValidDestination(zombie, cell);
					var fast = FastHasValidDestination(zombie, cell);
					if (legacy == fast)
						continue;
					var mismatch = new
					{
						cell = ZombieRuntimeActions.DescribeCell(cell),
						legacy,
						fast
					};
					zombieMismatches.Add(mismatch);
					if (mismatches.Count < 20)
						mismatches.Add(new
						{
							zombie = DescribeZombie(zombie),
							mismatch
						});
				}

				if (samples.Count < 12 || zombieMismatches.Count > 0)
				{
					samples.Add(new
					{
						zombie = DescribeZombie(zombie),
						usesNormalContext,
						flying = zombie.Flying,
						fenceBlocked = zombie.FenceBlocked,
						shouldAvoidFences = zombie.ShouldAvoidFences,
						curJobDef = zombie.CurJobDef?.defName,
						curJobIgnoreFenceBlocked = zombie.CurJobDef?.ignoreFenceBlocked,
						candidateCellCount = cells.Count,
						mismatchCount = zombieMismatches.Count,
						mismatches = zombieMismatches.Take(5).ToArray()
					});
				}
			}

			return new
			{
				success = mismatches.Count == 0 && specialContextCount == 0 && fenceBlockedCount == 0 && flyingCount == 0,
				scannedZombies = zombies.Count,
				scannedCells,
				normalContextCount,
				specialContextCount,
				fenceBlockedCount,
				flyingCount,
				mismatchCount = mismatches.Count,
				mismatches,
				samples
			};
		}

		static List<IntVec3> CandidateDestinationCells(Zombie zombie)
		{
			var result = new List<IntVec3>(12);
			void Add(IntVec3 cell)
			{
				if (cell.IsValid && result.Contains(cell) == false)
					result.Add(cell);
			}

			Add(zombie.Position);
			Add(zombie.lastGotoPosition);
			if (zombie.pather?.Moving == true)
				Add(zombie.pather.Destination.Cell);
			if (zombie.jobs?.curDriver is JobDriver_Stumble stumble)
				Add(stumble.destination);

			for (var i = 0; i < GenAdj.AdjacentCells.Length; i++)
				Add(zombie.Position + GenAdj.AdjacentCells[i]);

			return result;
		}

		static bool LegacyHasValidDestination(Pawn pawn, IntVec3 dest)
		{
			var map = pawn.Map;
			var size = map.info.Size;
			if (dest.x < 0 || dest.x >= size.x || dest.z < 0 || dest.z >= size.z)
				return false;
			if (map.edificeGrid[dest] is Building_Door door && door.Open == false)
				return false;
			var idx = map.cellIndices.CellToIndex(dest);
			var pathGrid = map.pathing.For(pawn).pathGrid;
			return pathGrid.pathGrid[idx] < 10000;
		}

		static bool FastHasValidDestination(Pawn pawn, IntVec3 dest)
		{
			var map = pawn.Map;
			var size = map.info.Size;
			if (dest.x < 0 || dest.x >= size.x || dest.z < 0 || dest.z >= size.z)
				return false;
			if (map.edificeGrid[dest] is Building_Door door && door.Open == false)
				return false;
			var idx = map.cellIndices.CellToIndex(dest);
			var pathGrid = pawn is Zombie ? map.pathing.Normal.pathGrid : map.pathing.For(pawn).pathGrid;
			return pathGrid.pathGrid[idx] < 10000;
		}
	}
}
