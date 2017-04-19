using System;
using System.Diagnostics;
using Verse;

namespace ZombieLand
{
	public class Pheromone
	{
		public static Pheromone empty = new Pheromone();

		public IntVec2 vector;
		public long timestamp;

		public Pheromone()
		{
			vector = IntVec2.Invalid;
			timestamp = 0;
		}

		public Pheromone(long timestamp = 0)
		{
			vector = IntVec2.Invalid;
			this.timestamp = timestamp != 0 ? timestamp : Tools.Ticks();
		}

		public Pheromone(IntVec2 pos, long timestamp = 0)
		{
			vector = pos;
			this.timestamp = timestamp != 0 ? timestamp : Tools.Ticks();
		}
	}

	public class PheromoneGrid
	{
		Pheromone[] grid;

		private int mapSizeX;
		private int mapSizeZ;

		public PheromoneGrid(Map map)
		{
			mapSizeX = map.Size.x;
			mapSizeZ = map.Size.z;
			grid = new Pheromone[mapSizeX * mapSizeZ];
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
			if (cell == null && create)
			{
				cell = Pheromone.empty;
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

		public void Set(IntVec3 position, IntVec2 target, long timestamp = 0)
		{
			if (position.x < 0 || position.x >= mapSizeX || position.z < 0 || position.z >= mapSizeZ) return;
			grid[CellToIndex(position)] = new Pheromone(target, timestamp);
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