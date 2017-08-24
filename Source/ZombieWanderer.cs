using Harmony;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	public class MapInfo
	{
		private readonly byte[][] vecGrids;
		private int publicIndex = 0;
		private int privateIndex = 1;

		private readonly int mapSize;
		private readonly int mapSizeX;
		private readonly int mapSizeZ;
		private readonly PathGrid pathGrid;
		private readonly EdificeGrid edificeGrid;
		private readonly TerrainGrid terrainGrid;
		private readonly PheromoneGrid pheromoneGrid;

		private Queue<IntVec3> openCellSet;
		private Queue<IntVec3> openDoorSet;
		private bool dirtyCells = true;

		private static readonly Random random = new Random();
		private static readonly int[] adjIndex = { 0, 1, 2, 3, 4, 5, 6, 7 };
		private static readonly byte endpoint = 255;
		private static int prevIndex;

		public MapInfo(Map map)
		{
			mapSizeX = map.Size.x;
			mapSizeZ = map.Size.z;
			mapSize = mapSizeX * mapSizeZ;
			pathGrid = map.pathGrid;
			edificeGrid = map.edificeGrid;
			terrainGrid = map.terrainGrid;
			pheromoneGrid = map.GetGrid();

			vecGrids = new byte[][] { new byte[mapSize], new byte[mapSize] };
			openCellSet = new Queue<IntVec3>();
			openDoorSet = new Queue<IntVec3>();
		}

		public bool IsInValidState()
		{
			if (mapSize == 0) return false;
			if (pathGrid == null) return false;
			if (edificeGrid == null) return false;
			if (terrainGrid == null) return false;
			return true;
		}

		private byte GetDirect(IntVec3 pos)
		{
			return vecGrids[privateIndex][pos.x + pos.z * mapSizeX];
		}

		private void SetDirect(IntVec3 pos, byte val)
		{
			vecGrids[privateIndex][pos.x + pos.z * mapSizeX] = val;
		}

		private void Set(IntVec3 pos, IntVec3 parent)
		{
			var d = pos - parent;
			var dx = 2 + Math.Sign(d.x);
			var dz = 2 + Math.Sign(d.z);
			vecGrids[privateIndex][pos.x + pos.z * mapSizeX] = (byte)(dx + 4 * dz);
		}

		public IntVec3 GetParent(IntVec3 pos)
		{
			if (pos.x < 0 || pos.x >= mapSizeX || pos.z < 0 || pos.z >= mapSizeZ)
				return IntVec3.Invalid;

			var n = vecGrids[publicIndex][pos.x + pos.z * mapSizeX];
			if (n < 5 || n > 15) // 1+4*1 .. 3+4*3
				return IntVec3.Invalid;

			var dx = (n % 4) - 2;
			var dz = (n / 4) - 2;
			return pos - new IntVec3(dx, 0, dz);
		}

		private void ClearCells()
		{
			var a = vecGrids[privateIndex];
			Array.Clear(a, 0, a.Length);
		}

		IEnumerable<IntVec3> GetAdjactedInRandomOrder(IntVec3 basePos)
		{
			var nextIndex = random.Next(8);
			var c = adjIndex[prevIndex];
			adjIndex[prevIndex] = adjIndex[nextIndex];
			adjIndex[nextIndex] = c;
			prevIndex = nextIndex;

			for (var i = 0; i < 8; i++)
				yield return basePos + GenAdj.AdjacentCells[adjIndex[i]];
		}

		private bool ValidFloodCell(IntVec3 cell)
		{
			if (cell.x < 0 || cell.x >= mapSizeX || cell.z < 0 || cell.z >= mapSizeZ) return false;
			if (GetDirect(cell) != 0) return false;

			// tracing through closed doors covers the case when all targets are inside
			// and thus are "unreachable". It's too much of an obvious mechanic when all
			// zombies suddenly go towards a target that steps outside the door
			//
			// var door = edificeGrid[cell] as Building_Door;
			// if (door != null && door.Open == false) return false;

			try
			{
				if (pathGrid.WalkableFast(cell) == false) return false;
				if (terrainGrid.TerrainAt(cell).DoesRepellZombies()) return false;
			}
			catch (Exception)
			{
				return false;
			}

			return true;
		}

		public void Recalculate(IntVec3[] positions)
		{
			if (dirtyCells)
			{
				ClearCells();
				dirtyCells = false;
			}
			if (ZombieSettings.Values.ragingZombies == false) return;

			dirtyCells = true;
			var sleepCounter = 0;
			var directions = GenAdj.AdjacentCells;
			positions.Where(cell => ValidFloodCell(cell) && GetDirect(cell) == 0).Do(c =>
			{
				SetDirect(c, endpoint);

				var door = edificeGrid[c] as Building_Door;
				if (door != null)
					openDoorSet.Enqueue(c);
				else
					openCellSet.Enqueue(c);
			});

			while (openCellSet.Count > 0 || openDoorSet.Count > 0)
			{
				var parent = openCellSet.Count == 0 ? openDoorSet.Dequeue() : openCellSet.Dequeue();
				GetAdjactedInRandomOrder(parent).Where(ValidFloodCell).Do(child =>
				{
					Set(child, parent);

					var door = edificeGrid[child] as Building_Door;
					if (door != null)
						openDoorSet.Enqueue(child);
					else
						openCellSet.Enqueue(child);
				});

				if (++sleepCounter > 200)
				{
					sleepCounter = 0;
					Thread.Sleep(1);
				}
			}

			publicIndex = privateIndex;
			privateIndex = 1 - privateIndex;
		}
	}

	[StaticConstructorOnStartup]
	public class ZombieWanderer
	{
		static Dictionary<Map, MapInfo> grids;
		const int cellSize = 5;
		const int halfCellSize = (int)(cellSize / 2f + 0.9f);
		Thread workerThread;

		public ZombieWanderer()
		{
			grids = new Dictionary<Map, MapInfo>();

			workerThread = new Thread(() =>
			{
				EndlessLoop:

				var wait = true;
				try
				{
					if (Current.Game != null && Current.ProgramState == ProgramState.Playing && Scribe.mode == LoadSaveMode.Inactive)
					{
						var maps = Find.Maps.ToArray();
						foreach (var map in maps)
						{
							if (Current.Game == null || Current.ProgramState != ProgramState.Playing || Scribe.mode != LoadSaveMode.Inactive)
								break;

							var info = GetMapInfo(map);
							if (info.IsInValidState() == false) continue;

							var mapPawns = map.mapPawns;
							if (mapPawns != null)
							{
								var colonistPositions = mapPawns.AllPawnsSpawned.ToArray()
									.Where(pawn => pawn.Spawned && (pawn is Zombie) == false && pawn.RaceProps.Humanlike && pawn.RaceProps.IsFlesh)
									.Select(pawn => pawn.Position)
									.ToArray();

								if (colonistPositions.Count() > 0)
								{
									info.Recalculate(colonistPositions);
									wait = false;
								}
							}
						}
					}
				}
				catch (Exception e)
				{
					Log.Warning("ZombieWanderer thread error: " + e);
				}

				if (wait) Thread.Sleep(500);
				goto EndlessLoop;
			});

			workerThread.Priority = ThreadPriority.Lowest;
			workerThread.Start();
		}

		public MapInfo GetMapInfo(Map map)
		{
			MapInfo result;
			if (grids.TryGetValue(map, out result) == false)
			{
				result = new MapInfo(map);
				grids[map] = result;
			}
			return result;
		}
	}
}