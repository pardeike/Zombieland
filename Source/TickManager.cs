using Harmony;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	class TickManager : MapComponent
	{
		int populationSpawnCounter;
		int dequeedSpawnCounter;

		int visibleGridUpdateCounter;
		int avoidGridCounter;

		public IntVec3 centerOfInterest = IntVec3.Invalid;
		public int currentColonyPoints;

		public List<Zombie> prioritizedZombies;
		public AvoidGrid avoidGrid = null;
		public AvoidGrid emptyAvoidGrid = null;

		public TickManager(Map map) : base(map)
		{
			currentColonyPoints = 100;
			prioritizedZombies = new List<Zombie>();
		}

		public override void FinalizeInit()
		{
			base.FinalizeInit();

			var destinations = Traverse.Create(map.pawnDestinationManager).Field("reservedDestinations").GetValue<Dictionary<Faction, Dictionary<Pawn, IntVec3>>>();
			var zombieFaction = Find.FactionManager.FirstFactionOfDef(ZombieDefOf.Zombies);
			if (!destinations.ContainsKey(zombieFaction)) map.pawnDestinationManager.RegisterFaction(zombieFaction);

			var grid = map.GetGrid();
			grid.IterateCellsQuick(cell => cell.zombieCount = 0);
			AllZombies().Do(zombie => grid.ChangeZombieCount(zombie.Position, 1));

			if (ZombieSettings.Values.betterZombieAvoidance)
			{
				var specs = AllZombies().Select(zombie => new ZombieCostSpecs()
				{
					position = zombie.Position,
					radius = ZombieAvoidRadius(zombie),
					maxCosts = ZombieMaxCosts(zombie)

				}).ToList();

				avoidGrid = Tools.avoider.UpdateZombiePositionsImmediately(map, specs);
			}
			else
				avoidGrid = new AvoidGrid(map);
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
			if (visibleGridUpdateCounter-- < 0)
			{
				visibleGridUpdateCounter = Constants.TICKMANAGER_RECALCULATE_DELAY.SecondsToTicks();

				currentColonyPoints = Tools.ColonyPoints();

				prioritizedZombies = AllZombies().ToList();
				var home = map.areaManager.Home;
				if (home.TrueCount > 0)
				{
					prioritizedZombies.Do(zombie => zombie.wanderDestination = home.ActiveCells.RandomElement());
					var cells = home.ActiveCells;
					centerOfInterest = new IntVec3(
						(int)Math.Round(cells.Average(c => c.x)),
						0/*(int)Math.Round(cells.Average(c => c.y))*/,
						(int)Math.Round(cells.Average(c => c.z))
					);
				}
				else
				{
					centerOfInterest = Tools.CenterOfInterest(map);
					prioritizedZombies.Do(zombie => zombie.wanderDestination = centerOfInterest);
				}

				var grid = map.GetGrid();
				prioritizedZombies.Sort(
					delegate (Zombie z1, Zombie z2)
					{
						var v1 = grid.GetTimestamp(z1.Position);
						var v2 = grid.GetTimestamp(z2.Position);
						var order = v2.CompareTo(v1);
						if (order != 0) return order;
						var d1 = z1.Position.DistanceToSquared(z1.wanderDestination);
						var d2 = z2.Position.DistanceToSquared(z2.wanderDestination);
						return d1.CompareTo(d2);
					}
				);

			}
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
			if (dequeedSpawnCounter-- < 0)
			{
				dequeedSpawnCounter = Rand.Range(10, 51);

				var result = Tools.generator.TryGetNextGeneratedZombie(map);
				if (result == null) return;
				if (result.isEvent == false && ZombieCount() >= GetMaxZombieCount()) return;

				if (Tools.IsValidSpawnLocation(result.cell, result.map) == false) return;

				var existingZombies = result.map.thingGrid.ThingsListAtFast(result.cell).OfType<Zombie>();
				if (existingZombies.Any(zombie => zombie.state == ZombieState.Emerging))
				{
					Tools.generator.RequeueZombie(result);
					return;
				}

				ZombieGenerator.FinalizeZombieGeneration(result.zombie);
				GenPlace.TryPlaceThing(result.zombie, result.cell, result.map, ThingPlaceMode.Direct);

				var grid = result.map.GetGrid();
				grid.ChangeZombieCount(result.cell, 1);

			}
		}

		public float ZombieAvoidRadius(Zombie zombie)
		{
			if (zombie.wasColonist)
				return 10f;
			switch (zombie.state)
			{
				case ZombieState.Wandering:
					return 3f;
				case ZombieState.Tracking:
					return 5f;
				default:
					return 1f;
			}
		}

		public float ZombieMaxCosts(Zombie zombie)
		{
			if (zombie.wasColonist)
				return 8000f;
			return 3000f;
		}

		public void UpdateZombieAvoider()
		{
			var specs = AllZombies().Select(zombie => new ZombieCostSpecs()
			{
				position = zombie.Position,
				radius = ZombieAvoidRadius(zombie),
				maxCosts = ZombieMaxCosts(zombie)

			}).ToList();
			Tools.avoider.UpdateZombiePositions(map, specs);
		}

		private void FetchAvoidGrid()
		{
			if (ZombieSettings.Values.betterZombieAvoidance == false)
			{
				if (emptyAvoidGrid == null)
					emptyAvoidGrid = new AvoidGrid(map);
				avoidGrid = emptyAvoidGrid;
				return;
			}

			if (avoidGridCounter-- < 0)
			{
				avoidGridCounter = Constants.TICKMANAGER_AVOIDGRID_DELAY.SecondsToTicks();

				var result = Tools.avoider.GetCostsGrid(map);
				if (result != null)
					avoidGrid = result;
			}
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
			if (populationSpawnCounter-- < 0)
			{
				populationSpawnCounter = (int)GenMath.LerpDouble(0, 1000, 300, 20, Math.Max(100, Math.Min(1000, currentColonyPoints)));

				if (GenDate.DaysPassedFloat < ZombieSettings.Values.daysBeforeZombiesCome) return;
				if (ZombieSettings.Values.spawnWhenType == SpawnWhenType.InEventsOnly) return;

				var numberOfZombies = ZombieCount() + Tools.generator.ZombiesQueued(map);
				if (numberOfZombies < GetMaxZombieCount())
				{
					switch (ZombieSettings.Values.spawnHowType)
					{
						case SpawnHowType.AllOverTheMap:
							{
								var cell = CellFinderLoose.RandomCellWith(Tools.ZombieSpawnLocator(map), map, 4);
								if (cell.IsValid) Tools.generator.SpawnZombieAt(map, cell, false);
								return;
							}
						case SpawnHowType.FromTheEdges:
							{
								IntVec3 cell;
								if (CellFinder.TryFindRandomEdgeCellWith(Tools.ZombieSpawnLocator(map), map, CellFinder.EdgeRoadChance_Neutral, out cell))
									Tools.generator.SpawnZombieAt(map, cell, false);
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
		}

		public override void MapComponentTick()
		{
			var watch = new Stopwatch();
			watch.Start();

			FetchAvoidGrid();
			RecalculateVisibleMap();
			IncreaseZombiePopulation();
			DequeuAndSpawnZombies();

			ZombieTicking(watch);
			UpdateZombieAvoider();

			watch.Stop();
		}
	}
}