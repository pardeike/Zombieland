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
		readonly byte[][] vecGrids;
		int publicIndex;
		int privateIndex = 1;

		readonly int mapSize;
		readonly int mapSizeX;
		readonly int mapSizeZ;
		readonly PathGrid pathGrid;
		readonly EdificeGrid edificeGrid;
		readonly TerrainGrid terrainGrid;
		readonly PheromoneGrid pheromoneGrid;

		readonly Queue<IntVec3> highPrioSet;
		readonly Queue<IntVec3> lowPrioSet;
		bool dirtyCells = true;

		static readonly int[][] randomOrders = {
			new int[] { 0, 1, 2, 3 },
			new int[] { 1, 0, 2, 3 },
			new int[] { 2, 0, 1, 3 },
			new int[] { 0, 2, 1, 3 },
			new int[] { 1, 2, 0, 3 },
			new int[] { 2, 1, 0, 3 },
			new int[] { 2, 1, 3, 0 },
			new int[] { 1, 2, 3, 0 },
			new int[] { 3, 2, 1, 0 },
			new int[] { 2, 3, 1, 0 },
			new int[] { 1, 3, 2, 0 },
			new int[] { 3, 1, 2, 0 },
			new int[] { 3, 0, 2, 1 },
			new int[] { 0, 3, 2, 1 },
			new int[] { 2, 3, 0, 1 },
			new int[] { 3, 2, 0, 1 },
			new int[] { 0, 2, 3, 1 },
			new int[] { 2, 0, 3, 1 },
			new int[] { 1, 0, 3, 2 },
			new int[] { 0, 1, 3, 2 },
			new int[] { 3, 1, 0, 2 },
			new int[] { 1, 3, 0, 2 },
			new int[] { 0, 3, 1, 2 },
			new int[] { 3, 0, 1, 2 }
		};

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
			highPrioSet = new Queue<IntVec3>();
			lowPrioSet = new Queue<IntVec3>();
		}

		public bool IsInValidState()
		{
			if (mapSize == 0) return false;
			if (pathGrid == null) return false;
			if (edificeGrid == null) return false;
			if (terrainGrid == null) return false;
			return true;
		}

		public int GetDirectDebug(IntVec3 pos)
		{
			return vecGrids[publicIndex][pos.x + pos.z * mapSizeX];
		}

		public int GetDirect(IntVec3 pos, bool ignoreBuildings)
		{
			return GetDirectInternal(pos, ignoreBuildings, true);
		}

		int GetDirectInternal(IntVec3 pos, bool ignoreBuildings, bool publicAccess)
		{
			int b = vecGrids[publicAccess ? publicIndex : privateIndex][pos.x + pos.z * mapSizeX];
			if (ignoreBuildings) b >>= 4;
			return b & 0x0f;
		}

		/*unsafe void SetDirect(IntVec3 pos, int val, bool ignoreBuildings)
		{
			var b = (byte*)vecGrids[privateIndex][pos.x + pos.z * mapSizeX];
			if (ignoreBuildings)
			{
				val <<= 4;
				*b &= 0x0f;
			}
			else
				*b &= 0xf0;
			*b |= (byte)val;
		}*/
		void SetDirect(IntVec3 pos, int val, bool ignoreBuildings)
		{
			var grid = vecGrids[privateIndex];
			var idx = pos.x + pos.z * mapSizeX;
			var b = grid[idx];
			if (ignoreBuildings)
			{
				val <<= 4;
				b &= 0x0f;
			}
			else
				b &= 0xf0;
			b |= (byte)val;
			grid[idx] = b;
		}

		void Set(IntVec3 pos, IntVec3 parent, bool ignoreBuildings)
		{
			var d = pos - parent;
			var dx = 2 + Math.Sign(d.x);
			var dz = 2 + Math.Sign(d.z);
			SetDirect(pos, dx + 4 * dz, ignoreBuildings);
		}

		public IntVec3 GetParent(IntVec3 pos, bool ignoreBuildings)
		{
			if (pos.x < 0 || pos.x >= mapSizeX || pos.z < 0 || pos.z >= mapSizeZ)
				return IntVec3.Invalid;

			var val = GetDirectInternal(pos, ignoreBuildings, true);
			if (val < 5 || val > 15) // 1+4*1 .. 3+4*3
				return IntVec3.Invalid;

			var dx = (val % 4) - 2;
			var dz = (val / 4) - 2;
			return pos - new IntVec3(dx, 0, dz);
		}

		void ClearCells()
		{
			var a = vecGrids[privateIndex];
			Array.Clear(a, 0, a.Length);
		}

		IEnumerable<IntVec3> GetValidAdjactedCellsInRandomOrder(Map map, IntVec3 basePos, bool ignoreBuildings)
		{
			UnityEngine.Random.InitState(basePos.x + basePos.z * 1000);
			int[] rndices;
			int i;

			rndices = randomOrders[UnityEngine.Random.Range(0, randomOrders.Length)];
			for (i = 0; i < 4; i++)
			{
				var cell = basePos + GenAdj.CardinalDirections[rndices[i]];
				if (ValidFloodCell(map, cell, basePos, ignoreBuildings))
					yield return cell;
			}

			rndices = randomOrders[UnityEngine.Random.Range(0, randomOrders.Length)];
			for (i = 0; i < 4; i++)
			{
				var cell = basePos + GenAdj.DiagonalDirections[rndices[i]];
				if (ValidFloodCell(map, cell, basePos, ignoreBuildings))
					yield return cell;
			}
		}

		bool BlocksDiagonalMovement(int x, int z)
		{
			var idx = x + mapSizeX * z;
			return pathGrid.WalkableFast(idx) == false || (edificeGrid != null && edificeGrid[idx] is Building_Door);
		}

		bool ValidFloodCell(Map map, IntVec3 cell, IntVec3 from, bool ignoreBuildings)
		{
			if (cell.x < 0 || cell.x >= mapSizeX || cell.z < 0 || cell.z >= mapSizeZ)
				return false;

			if (GetDirectInternal(cell, ignoreBuildings, false) != 0)
				return false;

			// wrap things in try/catch because of concurrent access to data structures
			// used by the main thread
			try
			{
				if (pathGrid.WalkableFast(cell) == false)
				{
					if (ignoreBuildings)
						return edificeGrid != null && edificeGrid[cell] is Building;
					return false;
				}

				// walking diagonal works only when not across a diagonal gap in a wall
				// so lets check for that case
				if (ignoreBuildings == false && from.AdjacentToDiagonal(cell))
					if (BlocksDiagonalMovement(from.x, cell.z) || BlocksDiagonalMovement(cell.x, from.z))
						return false;

				// For now, we disable this to gain execution speed
				//if (terrainGrid.TerrainAt(cell).DoesRepellZombies()) return false;

				return true;
			}
			catch
			{
				return false;
			}
		}

		void Recalculate(Map map, IntVec3[] positions, bool hasTankyZombies, bool ignoreBuildings)
		{
			positions
				.Where(cell => GetDirectInternal(cell, ignoreBuildings, false) == 0)
				.Do(c =>
				{
					SetDirect(c, 1, ignoreBuildings);
					highPrioSet.Enqueue(c);
				});

			var sleepCounter = 0;
			while (highPrioSet.Count + lowPrioSet.Count > 0)
			{
				var parent = highPrioSet.Count == 0 ? lowPrioSet.Dequeue() : highPrioSet.Dequeue();
				GetValidAdjactedCellsInRandomOrder(map, parent, ignoreBuildings).Do(child =>
				{
					Set(child, parent, ignoreBuildings);

					if (ignoreBuildings)
					{
						if ((edificeGrid != null && edificeGrid[child] is Building_Door) == false)
							highPrioSet.Enqueue(child);
					}
					else
					{
						if (edificeGrid != null && edificeGrid[child] is Building_Door door && door.Open == false)
							lowPrioSet.Enqueue(child);
						else
							highPrioSet.Enqueue(child);
					}
				});

				if (++sleepCounter > 200)
				{
					sleepCounter = 0;
					Thread.Sleep(1);
				}
			}
		}

		public void RecalculateAll(Map map, IntVec3[] positions, bool hasTankyZombies)
		{
			if (dirtyCells)
			{
				ClearCells();
				dirtyCells = false;
			}
			if (ZombieSettings.Values.ragingZombies == false && hasTankyZombies == false) return;

			dirtyCells = true;
			Recalculate(map, positions, hasTankyZombies, false);
			Recalculate(map, positions, hasTankyZombies, true);

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

		struct PawnProps
		{
			public bool valid;
			public IntVec3 position;

			public PawnProps(Pawn pawn)
			{
				valid = pawn != null
					&& pawn.Spawned
					&& (ZombieSettings.Values.attackMode != AttackMode.OnlyColonists || (ZombieSettings.Values.attackMode == AttackMode.OnlyColonists && pawn.IsColonist))
					&& (pawn is Zombie) == false
					&& (ZombieSettings.Values.attackMode == AttackMode.OnlyHumans == false || pawn.RaceProps.Humanlike)
					&& pawn.RaceProps.IsFlesh
					&& pawn.Dead == false
					&& pawn.Downed == false;
				position = pawn?.Position ?? IntVec3.Invalid;
			}
		}

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

							var mapPawns = map?.mapPawns?.AllPawnsSpawned;
							if (mapPawns != null)
							{
								var pawnArray = new Pawn[0];
								for (var i = 0; i < 3; i++)
								{
									try
									{
										pawnArray = mapPawns.ToArray();
										break;
									}
									catch
									{
									}
								}
								var colonistPositions = pawnArray
									.Select(pawn => new PawnProps(pawn))
									.Where(props => props.valid)
									.Select(props => props.position)
									.ToArray();
								if (colonistPositions.Any())
								{
									var hasTankyZombies = pawnArray.OfType<Zombie>().Any(zombie => zombie.IsTanky);
									info.RecalculateAll(map, colonistPositions, hasTankyZombies);
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
			})
			{
				Priority = ThreadPriority.Lowest
			};
			workerThread.Start();
		}

		public MapInfo GetMapInfo(Map map)
		{
			if (grids.TryGetValue(map, out var result) == false)
			{
				result = new MapInfo(map);
				grids[map] = result;
			}
			return result;
		}
	}
}