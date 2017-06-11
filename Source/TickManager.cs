using Harmony;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Verse;

namespace ZombieLand
{
	/*
	[HarmonyPatch(typeof(Verse.TickManager))]
	[HarmonyPatch("DoSingleTick")]
	static class TickManager_DoSingleTick_Patch
	{
		public static Stopwatch watch;
		public static long min;
		public static long max;
		public static long average;
		static readonly int tickTotal = 120;
		static long[] ticks = new long[tickTotal];
		static int ticksCounter = 0;

		static void Prefix()
		{
			watch = new Stopwatch();
			watch.Start();
		}

		static void Postfix()
		{
			ticks[ticksCounter] = watch.ElapsedTicks;
			min = ticks.Min();
			max = ticks.Max();
			average = (long)ticks.Average();
			ticksCounter = (ticksCounter + 1) % tickTotal;
			watch.Stop();
		}
	}
	*/

	class TickManager : MapComponent
	{
		int populationSpawnCounter;
		int dequeedSpawnCounter;

		int updateCounter;

		public int currentColonyPoints;

		public List<Zombie> prioritizedZombies;

		public TickManager(Map map) : base(map)
		{
			currentColonyPoints = 100;
			prioritizedZombies = new List<Zombie>();
		}

		public void Initialize()
		{
			var destinations = Traverse.Create(map.pawnDestinationManager).Field("reservedDestinations").GetValue<Dictionary<Faction, Dictionary<Pawn, IntVec3>>>();
			var zombieFaction = Find.FactionManager.FirstFactionOfDef(ZombieDefOf.Zombies);
			if (!destinations.ContainsKey(zombieFaction)) map.pawnDestinationManager.RegisterFaction(zombieFaction);
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref currentColonyPoints, "colonyPoints");
			Scribe_Collections.Look(ref prioritizedZombies, "prioritizedZombies", LookMode.Reference);
			prioritizedZombies = prioritizedZombies.Where(zombie => zombie != null).ToList();
		}

		public void RecalculateVisibleMap()
		{
			currentColonyPoints = Tools.ColonyPoints();

			prioritizedZombies = AllZombies().ToList();
			var home = map.areaManager.Home;
			if (home.TrueCount > 0)
				prioritizedZombies.Do(zombie => zombie.wanderDestination = home.ActiveCells.RandomElement());
			else
			{
				var center = Tools.CenterOfInterest(map);
				prioritizedZombies.Do(zombie => zombie.wanderDestination = center);
			}

			var grid = map.GetGrid();
			prioritizedZombies.Sort(
				delegate (Zombie z1, Zombie z2)
				{
					var v1 = grid.Get(z1.Position).timestamp;
					var v2 = grid.Get(z2.Position).timestamp;
					var order = v2.CompareTo(v1);
					if (order != 0) return order;
					var d1 = z1.Position.DistanceToSquared(z1.wanderDestination);
					var d2 = z2.Position.DistanceToSquared(z2.wanderDestination);
					return d1.CompareTo(d2);
				}
			);
		}

		public int GetMaxZombieCount()
		{
			if (map == null || map.mapPawns == null) return 0;
			var colonists = map.mapPawns.ColonistCount;
			var perColonistZombieCount = GenMath.LerpDouble(0f, 4f, 10, 40, (float)Math.Min(4, Math.Sqrt(colonists)));
			var colonistMultiplier = Math.Sqrt(colonists) * 2;
			var baseStrengthFactor = GenMath.LerpDouble(0, 1000, 1f, 4f, Math.Min(1000, currentColonyPoints));
			var difficultyMultiplier = Find.Storyteller.difficulty.threatScale;
			var count = (int)(perColonistZombieCount * colonistMultiplier * baseStrengthFactor * difficultyMultiplier);
			return Math.Min(ZombieSettings.Values.maximumNumberOfZombies, count);
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
			Patches.EditWindow_DebugInspector_CurrentDebugString_Patch.tickedZombies = ticked;
			Patches.EditWindow_DebugInspector_CurrentDebugString_Patch.ofTotalZombies = total;
		}

		public void DequeuAndSpawnZombies()
		{
			var result = Tools.generator.TryGetNextGeneratedZombie(map);
			if (result == null) return;
			if (ZombieCount() >= GetMaxZombieCount()) return;

			if (Tools.IsValidSpawnLocation(result.cell, result.map) == false) return;

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
			if (GenDate.DaysPassedFloat < ZombieSettings.Values.daysBeforeZombiesCome) return;
			if (ZombieSettings.Values.spawnWhenType == SpawnWhenType.InEventsOnly) return;

			var zombieCount = ZombieCount() + Tools.generator.ZombiesQueued(map);
			var zombieDestCount = GetMaxZombieCount();
			if (zombieCount < zombieDestCount)
			{
				switch (ZombieSettings.Values.spawnHowType)
				{
					case SpawnHowType.AllOverTheMap:
						{
							var cell = CellFinderLoose.RandomCellWith(Tools.ZombieSpawnLocator(map), map, 4);
							if (cell.IsValid) Tools.generator.SpawnZombieAt(map, cell);
							return;
						}
					case SpawnHowType.FromTheEdges:
						{
							IntVec3 cell;
							if (CellFinder.TryFindRandomEdgeCellWith(Tools.ZombieSpawnLocator(map), map, CellFinder.EdgeRoadChance_Neutral, out cell))
								Tools.generator.SpawnZombieAt(map, cell);
							return;
						}
					default:
						{
							Log.Error("Unknown spawn type " + ZombieSettings.Values.spawnHowType);
							return;
						}
				}
			}
		}

		public override void MapComponentTick()
		{
			var watch = new Stopwatch();
			watch.Start();

			if (updateCounter-- < 0)
			{
				updateCounter = Constants.TICKMANAGER_RECALCULATE_DELAY.SecondsToTicks();
				RecalculateVisibleMap();
			}

			if (populationSpawnCounter-- < 0)
			{
				populationSpawnCounter = (int)GenMath.LerpDouble(0, 1000, 300, 20, Math.Max(100, Math.Min(1000, currentColonyPoints)));
				IncreaseZombiePopulation();
			}

			if (dequeedSpawnCounter-- < 0)
			{
				dequeedSpawnCounter = Rand.Range(10, 51);
				DequeuAndSpawnZombies();
			}

			ZombieTicking(watch);

			watch.Stop();
		}
	}
}