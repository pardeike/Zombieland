using HarmonyLib;
using RimWorld;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

		public static TraverseParms traverseParms = TraverseParms.For(TraverseMode.NoPassClosedDoorsOrWater, Danger.Deadly, true, false, true);
		public MapInfo(Map map)
		{
			mapSizeX = map.Size.x;
			mapSizeZ = map.Size.z;
			mapSize = mapSizeX * mapSizeZ;
			pathGrid = map.pathing.For(traverseParms).pathGrid;
			edificeGrid = map.edificeGrid;
			terrainGrid = map.terrainGrid;

			vecGrids = new byte[][] { new byte[mapSize], new byte[mapSize] };
			highPrioSet = new Queue<IntVec3>();
			lowPrioSet = new Queue<IntVec3>();
		}

		public bool IsInValidState()
		{
			if (mapSize == 0)
				return false;
			if (pathGrid == null)
				return false;
			if (edificeGrid == null)
				return false;
			if (terrainGrid == null)
				return false;
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
			if (ignoreBuildings)
				b >>= 4;
			return b & 0x0f;
		}

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

		IEnumerable<IntVec3> GetValidAdjactedCellsInRandomOrder(IntVec3 basePos, bool ignoreBuildings)
		{
			int[] rndices;
			int i;
			var t = (int)(DateTime.Now.Ticks % 1000);
			var random = new Random(basePos.x + basePos.z * 1000 + t * 1000000);

			rndices = randomOrders[random.Next(0, 24)];
			for (i = 0; i < 4; i++)
			{
				var cell = basePos + GenAdj.CardinalDirections[rndices[i]];
				if (ValidFloodCell(cell, basePos, ignoreBuildings))
					yield return cell;
			}

			rndices = randomOrders[random.Next(0, 24)];
			for (i = 0; i < 4; i++)
			{
				var cell = basePos + GenAdj.DiagonalDirections[rndices[i]];
				if (ValidFloodCell(cell, basePos, ignoreBuildings))
					yield return cell;
			}
		}

		bool BlocksDiagonalMovement(int x, int z)
		{
			var idx = x + mapSizeX * z;
			return pathGrid.WalkableFast(idx) == false || (edificeGrid != null && edificeGrid[idx] is Building_Door);
		}

		bool ValidFloodCell(IntVec3 cell, IntVec3 from, bool ignoreBuildings)
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
						return edificeGrid != null && edificeGrid[cell] is Building building && (building as Mineable) == null;
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

		static readonly Stopwatch watch = new();
		IEnumerator Recalculate(IntVec3[] positions, bool ignoreBuildings)
		{
			positions
				.Where(cell => GetDirectInternal(cell, ignoreBuildings, false) == 0)
				.Do(c =>
				{
					SetDirect(c, 1, ignoreBuildings);
					highPrioSet.Enqueue(c);
				});

			yield return null;
			watch.Reset();
			watch.Start();

			while (highPrioSet.Count + lowPrioSet.Count > 0)
			{
				if (highPrioSet.Count == 0)
					while (lowPrioSet.Count > 0)
						highPrioSet.Enqueue(lowPrioSet.Dequeue());

				var parent = highPrioSet.Count == 0 ? lowPrioSet.Dequeue() : highPrioSet.Dequeue();
				GetValidAdjactedCellsInRandomOrder(parent, ignoreBuildings).Do(child =>
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

				var tick = watch.ElapsedTicks * (double)60 / 10000000;
				var speed = (int)Find.TickManager.CurTimeSpeed;
				var maxTick = speed == 0 ? 0.1f : (ZombieGenerator.ZombiesSpawning > 0 ? 0.07f : 0.12f) - speed * 0.0025f;
				if (tick > maxTick)
				{
					yield return null;
					watch.Reset();
					watch.Start();
				}
			}
		}

		public IEnumerator RecalculateAll(IntVec3[] positions, IEnumerable<Zombie> zombies)
		{
			if (dirtyCells)
			{
				ClearCells();
				dirtyCells = false;
			}

			if (zombies.Any(zombie => zombie.raging > 0 || zombie.isDarkSlimer))
			{
				dirtyCells = true;
				var it1 = Recalculate(positions, false);
				while (it1.MoveNext())
					yield return null;
			}

			var tankys = zombies.Where(zombie => zombie.IsTanky);
			if (tankys.Any())
			{
				var tankysPositions = tankys.Select(zombie => zombie.tankDestination).Where(pos => pos.IsValid).ToArray();
				if (tankysPositions.Length > 0)
					positions = tankysPositions;

				dirtyCells = true;
				var it2 = Recalculate(positions, true);
				while (it2.MoveNext())
					yield return null;
			}

			publicIndex = privateIndex;
			privateIndex = 1 - privateIndex;
		}
	}

	public static class ZombieWanderer
	{
		public static readonly IEnumerator processor = Process();

		static readonly Dictionary<Map, MapInfo> grids = new();

		public static MapInfo GetMapInfo(Map map)
		{
			if (grids.TryGetValue(map, out var result) == false)
			{
				result = new MapInfo(map);
				grids[map] = result;
			}
			return result;
		}

		public static IEnumerator Process()
		{
			while (true)
			{
				var didNothing = true;
				if (Current.Game != null && Current.ProgramState == ProgramState.Playing && Scribe.mode == LoadSaveMode.Inactive)
				{
					var maps = Find.Maps.ToArray();
					foreach (var map in maps)
					{
						if (Current.Game == null || Current.ProgramState != ProgramState.Playing || Scribe.mode != LoadSaveMode.Inactive)
							break;

						var info = GetMapInfo(map);
						if (info.IsInValidState() == false)
							continue;

						var mapPawns = map?.mapPawns?.AllPawnsSpawned?.ToArray();
						if (mapPawns != null)
						{
							var colonistPositions = mapPawns
								.OfType<Pawn>()
								.Where(pawn => Customization.DoesAttractsZombies(pawn) && pawn is not ZombieBlob && pawn is not ZombieSpitter)
								.Select(pawn => pawn.Position).ToArray();
							didNothing = false;
							yield return null;
							if (colonistPositions.Any())
							{
								var it = info.RecalculateAll(colonistPositions, mapPawns.OfType<Zombie>());
								while (it.MoveNext())
									yield return null;
							}
						}
					}
				}
				if (didNothing)
					yield return null;
			}
		}
	}
}
