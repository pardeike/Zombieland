using Harmony;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Verse;

namespace ZombieLand
{
	static class MapExtension
	{
		static Dictionary<int, TickManager> tickManagerCache = new Dictionary<int, TickManager>();
		public static TickManager TickManager(this Map map)
		{
			if (tickManagerCache.TryGetValue(map.uniqueID, out TickManager tickManager))
				return tickManager;

			tickManager = map.GetComponent<TickManager>();
			if (tickManager == null)
			{
				tickManager = new TickManager(map);
				map.components.Add(tickManager);
			}
			tickManagerCache[map.uniqueID] = tickManager;
			return tickManager;
		}
	}

	class TickManager : MapComponent
	{
		int populationSpawnCounter;
		int dequeedSpawnCounter;

		int updateCounter;

		public int currentColonyPoints;
		public IntVec3 centerOfInterest;

		public List<Zombie> prioritizedZombies;

		public TickManager(Map map) : base(map)
		{
			currentColonyPoints = 100;
			centerOfInterest = IntVec3.Invalid;
			prioritizedZombies = new List<Zombie>();
		}

		public void Initialize()
		{
			var destinations = Traverse.Create(map.pawnDestinationManager).Field("reservedDestinations").GetValue<Dictionary<Faction, Dictionary<Pawn, IntVec3>>>();
			var zombieFaction = Find.FactionManager.FirstFactionOfDef(ZombieDefOf.Zombies);
			if (!destinations.ContainsKey(zombieFaction)) map.pawnDestinationManager.RegisterFaction(zombieFaction);

			// TODO - can't do that in this thread, need to be on the main thread
			/*
			AllZombies()
				.Select(zombie => zombie.Drawer.renderer.graphics)
				.Where(graphics => !graphics.AllResolved)
				.Do(graphics => graphics.ResolveAllGraphics());
			*/
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref currentColonyPoints, "colonyPoints");
			Scribe_Values.Look(ref centerOfInterest, "centerOfInterest");
			Scribe_Collections.Look(ref prioritizedZombies, "prioritizedZombies", LookMode.Reference);
			prioritizedZombies = prioritizedZombies.Where(zombie => zombie != null).ToList();
		}

		public void RecalculateVisibleMap()
		{
			currentColonyPoints = Tools.ColonyPoints();

			int x = 0, z = 0, n = 0;
			int buildingMultiplier = 3;
			if (map.listerBuildings != null && map.listerBuildings.allBuildingsColonist != null)
			{
				map.listerBuildings.allBuildingsColonist.Do(building =>
				{
					x += building.Position.x * buildingMultiplier;
					z += building.Position.z * buildingMultiplier;
					n += buildingMultiplier;
				});
			}
			if (map.mapPawns != null)
			{
				map.mapPawns.SpawnedPawnsInFaction(Faction.OfPlayer).Do(pawn =>
				{
					x += pawn.Position.x;
					z += pawn.Position.z;
					n++;
				});
			}
			centerOfInterest = n == 0 ? map.Center : new IntVec3(x / n, 0, z / n);

			prioritizedZombies = AllZombies().ToList();
			var grid = map.GetGrid();
			prioritizedZombies.Sort(
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
			);
		}

		public int GetMaxZombieCount(bool log)
		{
			if (map == null || map.mapPawns == null) return 0;
			var colonists = map.mapPawns.ColonistCount;
			var perColonistZombieCount = GenMath.LerpDouble(0f, 4f, 10, 40, (float)Math.Min(4, Math.Sqrt(colonists)));
			var colonistMultiplier = Math.Sqrt(colonists) * 2;
			var baseStrengthFactor = GenMath.LerpDouble(0, 1000, 1f, 4f, Math.Min(1000, currentColonyPoints));
			var difficultyMultiplier = Find.Storyteller.difficulty.threatScale;
			return (int)(perColonistZombieCount * colonistMultiplier * baseStrengthFactor * difficultyMultiplier);
		}

		public void ZombieTicking(Stopwatch watch)
		{
			var maxTickTime = (1f / (60f / Constants.FRAME_TIME_FACTOR)) / Find.TickManager.TickRateMultiplier * Stopwatch.Frequency;
			var zombies = prioritizedZombies.Where(zombie => zombie.Map == map).ToList();
			var total = zombies.Count;
			var ticked = 0;
			foreach (var zombie in zombies)
			{
				zombie.CustomTick();
				ticked++;
				if (watch.ElapsedTicks > maxTickTime) break;
			}
			ZombielandMod.EditWindow_DebugInspector_CurrentDebugString_Patch.tickedZombies = ticked;
			ZombielandMod.EditWindow_DebugInspector_CurrentDebugString_Patch.ofTotalZombies = total;
		}

		public void DequeuAndSpawnZombies()
		{
			var result = Tools.generator.TryGetNextGeneratedZombie(map);
			if (result == null) return;
			if (ZombieCount() >= GetMaxZombieCount(false)) return;

			if (Tools.IsValidSpawnLocation(result.cell, result.map) == false) return;

			var grid = result.map.GetGrid();
			var existingZombies = result.map.thingGrid.ThingsListAtFast(result.cell).OfType<Zombie>();
			if (existingZombies.Any(zombie => zombie.state == ZombieState.Emerging))
			{
				Tools.generator.RequeueZombie(result);
				return;
			}

			ZombieGenerator.FinalizeZombieGeneration(result.zombie);
			GenPlace.TryPlaceThing(result.zombie, result.cell, result.map, ThingPlaceMode.Direct, null);
			result.map.GetGrid().ChangeZombieCount(result.cell, 1);
		}

		public IEnumerable<Zombie> AllZombies()
		{
			if (map.mapPawns == null || map.mapPawns.AllPawns == null) return new List<Zombie>();
			return map.mapPawns.AllPawns.OfType<Zombie>().Where(zombie => zombie != null);
		}

		public int ZombieCount()
		{
			return AllZombies().Count();
		}

		public void IncreaseZombiePopulation()
		{
			var zombieCount = ZombieCount() + Tools.generator.ZombiesQueued(map);
			var zombieDestCount = GetMaxZombieCount(true);
			if (zombieCount < zombieDestCount)
			{
				var cell = CellFinderLoose.RandomCellWith(Tools.ZombieSpawnLocator(map), map, 4);
				if (cell.IsValid)
					Tools.generator.SpawnZombieAt(map, cell);
			}
		}

		public override void MapComponentTick()
		{
			var watch = new Stopwatch();
			watch.Start();

			if (updateCounter-- < 0)
			{
				updateCounter = Constants.TICKMANAGER_RECALCULATE_DELAY;
				RecalculateVisibleMap();
			}

			if (populationSpawnCounter-- < 0)
			{
				populationSpawnCounter = (int)GenMath.LerpDouble(0, 1000, 300, 20, Math.Max(100, Math.Min(1000, currentColonyPoints)));
				IncreaseZombiePopulation();
			}

			if (dequeedSpawnCounter-- < 0)
			{
				dequeedSpawnCounter = Rand.Range(10, 50);
				DequeuAndSpawnZombies();
			}

			ZombieTicking(watch);

			watch.Stop();
		}
	}
}