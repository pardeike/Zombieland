using Harmony;
using System;
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

		public static int GetMaxZombieCount()
		{
			var baseCount = GenMath.LerpDouble(0, 1000, 80, 200, Math.Min(1000, currentColonyPoints));
			var zombiesPerColonist = (int)(baseCount * Find.Storyteller.difficulty.threatScale);
			return zombiesPerColonist * Find.VisibleMap.mapPawns.ColonistCount;
		}

		public static void Tick()
		{
			var allPawns = Find.VisibleMap.mapPawns.AllPawnsSpawned;

			// update last positions

			allPawns.Do(pawn =>
			{
				var pos = pawn.Position;
				var id = pawn.ThingID;
				if (Main.lastPositions.ContainsKey(id) == false || pos != Main.lastPositions[id])
				{
					if (pawn.GetType() == Zombie.type)
					{
						if (Main.lastPositions.ContainsKey(id) == false) Main.lastPositions[id] = IntVec3.Invalid;
						Main.phGrid.ChangeZombieCount(Main.lastPositions[id], -1);
					}
					else
					{
						var now = Tools.Ticks();
						var radius = pawn.RaceProps.Animal ? 3f : 5f;
						Tools.GetCircle(radius).Do(vec => Main.phGrid.SetTimestamp(pos + vec, now - Math.Min(4, (int)vec.LengthHorizontal)));
					}
				}
				Main.lastPositions[id] = pos;
			});

			if (updateCounter-- < 0)
			{
				updateCounter = updateDelay;

				// update center of interest

				int x = 0, z = 0, n = 0;
				int buildingMultiplier = 3;
				Find.VisibleMap.listerBuildings.allBuildingsColonist.Do(building =>
				{
					x += building.Position.x * buildingMultiplier;
					z += building.Position.z * buildingMultiplier;
					n += buildingMultiplier;
				});
				allPawns.Where(pawn => pawn.GetType() != Zombie.type).Do(pawn =>
				{
					x += pawn.Position.x;
					z += pawn.Position.z;
					n++;
				});
				centerOfInterest = new IntVec3(x / n, 0, z / n);
				currentColonyPoints = Tools.ColonyPoints();
			}

			if (spawnCounter-- < 0)
			{
				spawnCounter = (int)GenMath.LerpDouble(0, 1000, 300, 20, Math.Max(100, Math.Min(1000, currentColonyPoints)));

				// spawn queued zombies

				if (Main.spawnQueue.Count() > 0)
				{
					var target = Main.spawnQueue.Dequeue();
					if (target.Map == Find.VisibleMap && Tools.IsValidSpawnLocation(target))
					{
						var zombie = ZombieGenerator.GeneratePawn(target.Map);
						GenPlace.TryPlaceThing(zombie, target.Cell, target.Map, ThingPlaceMode.Direct, null);
					}
				}

				// spawn new zombies

				var zombieCount = allPawns.OfType<Zombie>().Count();
				var zombieDestCount = GetMaxZombieCount();
				if (unlimitedZombies || zombieCount < zombieDestCount)
				{
					var map = Find.VisibleMap;
					var cell = CellFinderLoose.RandomCellWith(Tools.ZombieSpawnLocator(map), map, 4); // new IntVec3(75, 0, 75);
					if (cell.IsValid)
					{
						var zombie = ZombieGenerator.GeneratePawn(map);
						GenPlace.TryPlaceThing(zombie, cell, map, ThingPlaceMode.Direct, null);

						Log.Warning("New Zombie " + zombie.NameStringShort + " at " + cell.x + "/" + cell.z + " (" + zombieCount + " out of " + zombieDestCount + ")");
					}
				}
			}
		}
	}
}