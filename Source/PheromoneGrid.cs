using System;
using Verse;

namespace ZombieLand
{
	public class Pheromone : IExposable
	{
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
			Scribe_Values.Look(ref timestamp, "tstamp");
			Scribe_Values.Look(ref zombieCount, "zcount");
		}
	}

	public class PheromoneGrid : MapComponent
	{
		Pheromone[] grid;

		int mapSizeX;
		int mapSizeZ;

		public PheromoneGrid(Map map) : base(map)
		{
			mapSizeX = map.Size.x;
			mapSizeZ = map.Size.z;
			grid = new Pheromone[mapSizeX * mapSizeZ];
		}

		public override void ExposeData()
		{
			base.ExposeData();

			Tools.Look(ref grid, "pheromones", new object[0]);
			Scribe_Values.Look(ref mapSizeX, "mapx");
			Scribe_Values.Look(ref mapSizeZ, "mapz");

			if (mapSizeX == 0 || mapSizeZ == 0)
			{
				mapSizeX = (int)Math.Sqrt(grid.Length);
				mapSizeZ = mapSizeX;
			}
		}

		public void IterateCells(Action<int, int, Pheromone> callback)
		{
			for (var z = 0; z < mapSizeZ; z++)
			{
				var baseIndex = z * mapSizeX;
				for (var x = 0; x < mapSizeX; x++)
				{
					var cell = grid[baseIndex + x];
					if (cell != null)
						callback(x, z, cell);
				}
			}
		}

		public void IterateCellsQuick(Action<Pheromone> callback)
		{
			foreach (var cell in grid)
				if (cell != null)
					callback(cell);
		}

		public Pheromone GetPheromone(IntVec3 position, bool create = true)
		{
			if (position.x < 0 || position.x >= mapSizeX || position.z < 0 || position.z >= mapSizeZ)
				return null;

			var idx = (position.z * mapSizeX) + position.x;
			var cell = grid[idx];
			if (cell == null && create)
			{
				cell = new Pheromone();
				grid[idx] = cell;
			}
			return cell;
		}

		public long GetTimestamp(IntVec3 position)
		{
			return GetPheromone(position, false)?.timestamp ?? 0;
		}

		public void SetTimestamp(IntVec3 position, long timestamp)
		{
			var cell = GetPheromone(position);
			if (cell != null) cell.timestamp = timestamp;
		}

		public int GetZombieCount(IntVec3 position)
		{
			var cell = GetPheromone(position);
			return GetPheromone(position, false)?.zombieCount ?? 0;
		}

		public void ChangeZombieCount(IntVec3 position, int change)
		{
			var cell = GetPheromone(position);
			if (cell != null) cell.zombieCount = cell.zombieCount + change;
		}
	}
}