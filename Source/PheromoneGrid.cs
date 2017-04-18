using System;
using System.Collections.Generic;
using System.Diagnostics;
using Verse;

namespace ZombieLand
{
	public class Pheromone
	{
		public static Pheromone empty = new Pheromone();

		public int x;
		public int z;
		public long timestamp;

		public Pheromone()
		{
			x = -1;
			z = -1;
			timestamp = 0;
		}

		public Pheromone(IntVec3 pos, long timestamp = 0)
		{
			x = pos.x;
			z = pos.z;
			this.timestamp = timestamp != 0 ? timestamp : Stopwatch.GetTimestamp();
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

		public Pheromone Get(IntVec3 position)
		{
			if (position.x < 0 || position.x >= mapSizeX || position.z < 0 || position.z >= mapSizeZ)
				return Pheromone.empty;
			var cell = grid[CellToIndex(position)];
			if (cell == null)
			{
				cell = Pheromone.empty;
				grid[CellToIndex(position)] = cell;
			}
			return cell;
		}

		public void Set(IntVec3 position, IntVec3 target, long timestamp = 0)
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