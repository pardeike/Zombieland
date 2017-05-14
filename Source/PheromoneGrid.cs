using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace ZombieLand
{
	public class Pheromone : IExposable
	{
		public static Pheromone empty = new Pheromone();

		public long timestamp;
		public int zombieCount;

		public Pheromone()
		{
			timestamp = 0;
			zombieCount = 0;
		}

		public Pheromone(long timestamp = 0)
		{
			this.timestamp = timestamp != 0 ? timestamp : Tools.Ticks();
		}

		public void ExposeData()
		{
			Scribe_Values.LookValue(ref timestamp, "tstamp");
			Scribe_Values.LookValue(ref zombieCount, "zcount");
		}
	}

	public class PheromoneGrid : MapComponent
	{
		List<Pheromone> grid;

		private int mapSizeX;
		private int mapSizeZ;

		public PheromoneGrid(Map map) : base(map)
		{
			mapSizeX = map.Size.x;
			mapSizeZ = map.Size.z;
			grid = new Pheromone[mapSizeX * mapSizeZ].ToList();
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Collections.LookList(ref grid, "pheromones", LookMode.Deep, new object[0]);
		}

		public int Count()
		{
			return grid.Count;
		}

		public void IterateCells(Action<int, int, Pheromone> callback)
		{
			for (int z = 0; z < mapSizeZ; z++)
				for (int x = 0; x < mapSizeX; x++)
				{
					var cell = grid[CellToIndex(x, z)];
					if (cell != null) callback(x, z, cell);
				}
		}

		public Pheromone Get(IntVec3 position, bool create = true)
		{
			if (position.x < 0 || position.x >= mapSizeX || position.z < 0 || position.z >= mapSizeZ)
				return Pheromone.empty;
			var cell = grid[CellToIndex(position)];
			if (cell == null)
			{
				cell = Pheromone.empty;
				if (create)
					grid[CellToIndex(position)] = cell;
			}
			return cell;
		}

		public void SetTimestamp(IntVec3 position, long timestamp = 0)
		{
			if (position.x < 0 || position.x >= mapSizeX || position.z < 0 || position.z >= mapSizeZ) return;
			var cell = grid[CellToIndex(position)];
			if (cell == null)
				grid[CellToIndex(position)] = new Pheromone(timestamp);
			else
				grid[CellToIndex(position)].timestamp = timestamp;
		}

		public void ChangeZombieCount(IntVec3 position, int change)
		{
			if (position.x < 0 || position.x >= mapSizeX || position.z < 0 || position.z >= mapSizeZ) return;
			var cell = grid[CellToIndex(position)];
			if (cell == null)
			{
				grid[CellToIndex(position)] = new Pheromone()
				{
					zombieCount = Math.Max(0, change)
				};
			}
			else
				grid[CellToIndex(position)].zombieCount = Math.Max(0, cell.zombieCount + change);
		}

		//

		int CellToIndex(IntVec3 c)
		{
			return ((c.z * mapSizeX) + c.x);
		}

		int CellToIndex(int x, int z)
		{
			return ((z * mapSizeX) + x);
		}
	}
}