using System;
using System.Collections.Generic;
using Verse;

namespace ZombieLand
{
	public class ZombieAttackTargetIndex : MapComponent
	{
		List<Thing>[] candidatesByCell;
		bool[] touchedCell;
		bool[] candidateNeighborCell;
		int[] touchedCells = Array.Empty<int>();
		int[] candidateNeighborCells = Array.Empty<int>();
		int touchedCount;
		int candidateNeighborCount;
		int candidateCount;
		int lastPawnCount = -1;
		bool invalidated = true;

		public int LastBuiltTick { get; private set; } = int.MinValue;
		public int RebuildCount { get; private set; }
		public int CachedCellCount => touchedCount;
		public int CachedCandidateNeighborCellCount => candidateNeighborCount;
		public int CachedCandidateCount => candidateCount;
		public bool Invalidated => invalidated;

		public ZombieAttackTargetIndex(Map map) : base(map)
		{
		}

		public void Invalidate()
		{
			invalidated = true;
		}

		public void InvalidateFor(Pawn pawn)
		{
			if (pawn is not Zombie)
				invalidated = true;
		}

		public List<Thing>[] CurrentCandidatesByCell()
		{
			EnsureCurrent();
			return candidatesByCell;
		}

		public bool[] CurrentCandidateNeighborsByCell()
		{
			EnsureCurrent();
			return candidateNeighborCell;
		}

		void EnsureCurrent()
		{
			EnsureCapacity();

			var pawnCount = map.mapPawns?.AllPawnsSpawned?.Count ?? 0;
			var tick = GenTicks.TicksGame;
			if (invalidated == false && lastPawnCount == pawnCount)
				return;

			Rebuild(pawnCount, tick);
		}

		void EnsureCapacity()
		{
			var cellCount = map.Size.x * map.Size.z;
			if (candidatesByCell?.Length == cellCount)
				return;

			candidatesByCell = new List<Thing>[cellCount];
			touchedCell = new bool[cellCount];
			candidateNeighborCell = new bool[cellCount];
			touchedCells = Array.Empty<int>();
			candidateNeighborCells = Array.Empty<int>();
			touchedCount = 0;
			candidateNeighborCount = 0;
			candidateCount = 0;
			invalidated = true;
		}

		void Rebuild(int pawnCount, int tick)
		{
			ClearTouchedCells();

			var pawns = map.mapPawns?.AllPawnsSpawned;
			if (pawns != null)
				foreach (var pawn in pawns)
				{
					if (pawn == null || pawn.Spawned == false || pawn.Destroyed || pawn.Dead)
						continue;
					if (pawn is Zombie)
						continue;
					if (pawn.Map != map)
						continue;
					TouchCell(map.cellIndices.CellToIndex(pawn.Position));
					TouchCandidateNeighborCells(pawn.Position);
				}

			var thingGrid = map.thingGrid.thingGrid;
			for (var i = 0; i < touchedCount; i++)
			{
				var idx = touchedCells[i];
				var candidates = candidatesByCell[idx] ??= new List<Thing>();
				var things = thingGrid[idx];
				for (var j = 0; j < things.Count; j++)
				{
					if (things[j] is Pawn { Spawned: true, Destroyed: false, Dead: false } pawn && pawn is not Zombie)
					{
						candidates.Add(pawn);
						candidateCount++;
					}
				}
			}

			LastBuiltTick = tick;
			lastPawnCount = pawnCount;
			invalidated = false;
			RebuildCount++;
		}

		void ClearTouchedCells()
		{
			for (var i = 0; i < touchedCount; i++)
			{
				var idx = touchedCells[i];
				touchedCell[idx] = false;
				candidatesByCell[idx]?.Clear();
				touchedCells[i] = 0;
			}
			touchedCount = 0;

			for (var i = 0; i < candidateNeighborCount; i++)
			{
				var idx = candidateNeighborCells[i];
				candidateNeighborCell[idx] = false;
				candidateNeighborCells[i] = 0;
			}
			candidateNeighborCount = 0;
			candidateCount = 0;
		}

		void TouchCell(int idx)
		{
			if (touchedCell[idx])
				return;

			if (touchedCount == touchedCells.Length)
			{
				var newLength = touchedCells.Length == 0 ? 32 : touchedCells.Length * 2;
				Array.Resize(ref touchedCells, newLength);
			}

			touchedCell[idx] = true;
			touchedCells[touchedCount++] = idx;
		}

		void TouchCandidateNeighborCells(IntVec3 candidateCell)
		{
			for (var i = 0; i < 8; i++)
			{
				var offset = ZombieStateHandler.AttackAdjacentOffset(i);
				var zombieCell = candidateCell + new IntVec3(-offset.x, 0, -offset.z);
				if (zombieCell.InBounds(map))
					TouchCandidateNeighborCell(map.cellIndices.CellToIndex(zombieCell));
			}
		}

		void TouchCandidateNeighborCell(int idx)
		{
			if (candidateNeighborCell[idx])
				return;

			if (candidateNeighborCount == candidateNeighborCells.Length)
			{
				var newLength = candidateNeighborCells.Length == 0 ? 64 : candidateNeighborCells.Length * 2;
				Array.Resize(ref candidateNeighborCells, newLength);
			}

			candidateNeighborCell[idx] = true;
			candidateNeighborCells[candidateNeighborCount++] = idx;
		}
	}
}
