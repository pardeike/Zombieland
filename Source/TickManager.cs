using Harmony;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Verse;

namespace ZombieLand
{
	static class TickManager
	{
		static int spawnCounter = 0;
		static bool unlimitedZombies = false;

		static int updateCounter = 0;
		static int updateDelay = GenTicks.SecondsToTicks(10f);

		public static int currentColonyPoints = 100;
		public static IntVec3 centerOfInterest = IntVec3.Invalid;

		public static HashSet<Zombie> allZombies = new HashSet<Zombie>();

		public static int GetMaxZombieCount()
		{
			var baseCount = GenMath.LerpDouble(0, 1000, 80, 200, Math.Min(1000, currentColonyPoints));
			var zombiesPerColonist = (int)(baseCount * Find.Storyteller.difficulty.threatScale);
			return zombiesPerColonist * Find.VisibleMap.mapPawns.ColonistCount;
		}

		public static void ZombieTicking(float currentMultiplier)
		{
			//var maxTickTime = (1f / 30f) / currentMultiplier * Stopwatch.Frequency;
			//var timer = new Stopwatch();
			//timer.Start();

			var zombies = allZombies.ToList();
			var aZombie = zombies.FirstOrDefault();
			if (aZombie == null) return;

			var grid = Tools.GetGrid(aZombie.Map);
			/*zombies.Sort(
				delegate (Zombie z1, Zombie z2)
				{
					var v1 = grid.Get(z1.Position).timestamp;
					var v2 = grid.Get(z2.Position).timestamp;
					var order = v2.CompareTo(v1);
					if (order != 0) return order;
					var d1 = z1.Position.DistanceToSquared(centerOfInterest);
					var d2 = z2.Position.DistanceToSquared(centerOfInterest);
					return d1.CompareTo(d2);
				}
			);*/
			var counter = 0;
			foreach (var zombie in zombies)
			{
				//if (timer.ElapsedTicks > maxTickTime) break;
				zombie.Tick();
				counter++;
			}
			//timer.Stop();
		}

		public static void UpdateCenterOfInterest()
		{
			int x = 0, z = 0, n = 0;
			int buildingMultiplier = 3;
			Find.VisibleMap.listerBuildings.allBuildingsColonist.Do(building =>
			{
				x += building.Position.x * buildingMultiplier;
				z += building.Position.z * buildingMultiplier;
				n += buildingMultiplier;
			});
			Find.VisibleMap.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer).Do(pawn =>
			{
				x += pawn.Position.x;
				z += pawn.Position.z;
				n++;
			});
			centerOfInterest = n == 0 ? Find.VisibleMap.Center : new IntVec3(x / n, 0, z / n);
		}

		public static void SpawnZombies()
		{
			// spawn queued zombies

			while (Main.spawnQueue.Count() > 0)
			{
				var target = Main.spawnQueue.Dequeue();
				if (target.Map == Find.VisibleMap && Tools.IsValidSpawnLocation(target))
				{
					var zombie = ZombieGenerator.GeneratePawn(target.Map);
					GenPlace.TryPlaceThing(zombie, target.Cell, target.Map, ThingPlaceMode.Direct, null);
				}
			}

			// spawn new zombies

			var zombieCount = allZombies.Count();
			var zombieDestCount = GetMaxZombieCount();
			if (unlimitedZombies || zombieCount < zombieDestCount)
			{
				var map = Find.VisibleMap;
				var cell = CellFinderLoose.RandomCellWith(Tools.ZombieSpawnLocator(map), map, 4);
				if (cell.IsValid)
				{
					var zombie = ZombieGenerator.GeneratePawn(map);
					GenPlace.TryPlaceThing(zombie, cell, map, ThingPlaceMode.Direct, null);

					// Log.Warning("New Zombie " + zombie.NameStringShort + " at " + cell.x + "/" + cell.z + " (" + zombieCount + " out of " + zombieDestCount + ")");
				}
			}
		}

		public static void Tick()
		{
			if (Constants.SPAWN_ALL_ZOMBIES)
			{
				Constants.SPAWN_ALL_ZOMBIES = false;
				var zombieDestCount = GetMaxZombieCount();
				while (allZombies.Count() < zombieDestCount)
				{
					var map = Find.VisibleMap;
					var cell = CellFinderLoose.RandomCellWith(Tools.ZombieSpawnLocator(map), map, 4); // new IntVec3(75, 0, 75);
					if (cell.IsValid)
					{
						var zombie = ZombieGenerator.GeneratePawn(map);
						GenPlace.TryPlaceThing(zombie, cell, map, ThingPlaceMode.Direct, null);

						// Log.Warning("New Zombie " + zombie.NameStringShort + " at " + cell.x + "/" + cell.z);
					}
				}
			}

			if (updateCounter-- < 0)
			{
				updateCounter = updateDelay;

				UpdateCenterOfInterest();
				currentColonyPoints = Tools.ColonyPoints();
			}

			if (spawnCounter-- < 0)
			{
				spawnCounter = (int)GenMath.LerpDouble(0, 1000, 300, 20, Math.Max(100, Math.Min(1000, currentColonyPoints)));

				SpawnZombies();
			}
		}
	}
}