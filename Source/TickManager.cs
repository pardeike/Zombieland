using Harmony;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Verse;

namespace ZombieLand
{
	class TickManager : MapComponent
	{
		int populationSpawnCounter;
		int dequeedSpawnCounter;

		int visibleGridUpdateCounter;
		int incidentTickCounter;
		int avoidGridCounter;

		public IntVec3 centerOfInterest = IntVec3.Invalid;
		public int currentColonyPoints;

		public List<Zombie> allZombiesCached;
		public AvoidGrid avoidGrid = null;
		public AvoidGrid emptyAvoidGrid = null;

		public IncidentInfo incidentInfo = new IncidentInfo();

		public TickManager(Map map) : base(map)
		{
			currentColonyPoints = 100;
			allZombiesCached = new List<Zombie>();
		}

		public override void FinalizeInit()
		{
			base.FinalizeInit();

			var grid = map.GetGrid();
			grid.IterateCellsQuick(cell => cell.zombieCount = 0);

			var destinations = Traverse.Create(map.pawnDestinationManager).Field("reservedDestinations").GetValue<Dictionary<Faction, Dictionary<Pawn, IntVec3>>>();
			var zombieFaction = Find.FactionManager.FirstFactionOfDef(ZombieDefOf.Zombies);
			if (!destinations.ContainsKey(zombieFaction)) map.pawnDestinationManager.RegisterFaction(zombieFaction);

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
			Scribe_Collections.Look(ref allZombiesCached, "prioritizedZombies", LookMode.Reference);
			Scribe_Deep.Look(ref incidentInfo, "incidentInfo", new object[0]);
			allZombiesCached = allZombiesCached.Where(zombie => zombie != null).ToList();
			if (incidentInfo == null) incidentInfo = new IncidentInfo();
		}

		public void RecalculateVisibleMap()
		{
			if (visibleGridUpdateCounter-- < 0)
			{
				visibleGridUpdateCounter = Constants.TICKMANAGER_RECALCULATE_DELAY.SecondsToTicks();

				currentColonyPoints = Tools.ColonyPoints();

				allZombiesCached = AllZombies().ToList();
				var home = map.areaManager.Home;
				if (home.TrueCount > 0)
				{
					allZombiesCached.Do(zombie => zombie.wanderDestination = home.ActiveCells.RandomElement());
					var cells = home.ActiveCells;
					centerOfInterest = new IntVec3(
						(int)Math.Round(cells.Average(c => c.x)),
						0,
						(int)Math.Round(cells.Average(c => c.z))
					);
				}
				else
				{
					centerOfInterest = Tools.CenterOfInterest(map);
					allZombiesCached.Do(zombie => zombie.wanderDestination = centerOfInterest);
				}
			}
		}

		public int GetMaxZombieCount()
		{
			if (map == null || map.mapPawns == null) return 0;
			var colonists = Tools.CapableColonists(map);
			var perColonistZombieCount = GenMath.LerpDouble(0f, 4f, 5, 30, (float)Math.Min(4, Math.Sqrt(colonists)));
			var colonistMultiplier = Math.Sqrt(colonists) * 2;
			var baseStrengthFactor = GenMath.LerpDouble(0, 1000, 1f, 4f, Math.Min(1000, currentColonyPoints));
			var difficultyMultiplier = Find.Storyteller.difficulty.threatScale;
			var count = (int)(perColonistZombieCount * colonistMultiplier * baseStrengthFactor * difficultyMultiplier);
			return Math.Min(ZombieSettings.Values.maximumNumberOfZombies, count);
		}

		public void ZombieTicking(Stopwatch watch)
		{
			var maxTickTime = Constants.TOTAL_ZOMBIE_FRAME_TIME / Find.TickManager.TickRateMultiplier * Stopwatch.Frequency;
			var zombies = allZombiesCached.Where(zombie => zombie.Map == map).ToList();
			zombies.Shuffle();
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

			}
		}

		public float ZombieAvoidRadius(Zombie zombie)
		{
			if (zombie.wasColonist)
				return 10f;
			if (zombie.raging > 0)
				return 5f;
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
			if (zombie.wasColonist || zombie.raging > 0)
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

		private void HandleIncidents()
		{
			if (incidentTickCounter++ < GenDate.TicksPerHour) return;
			incidentTickCounter = 0;

			var incidentSize = ZombiesRising.ZombiesForNewIncident(map);
			if (incidentSize > 0)
			{
				Log.Warning("Zombieland incident with " + incidentSize + " zombies");
				var success = ZombiesRising.TryExecute(map, incidentSize);
				if (success == false)
				{
					Log.Warning("Incident creation failed. Most likely no valid spawn point found.");
					// TODO incident failed, so mark it for new executing asap
				}
			}
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

			HandleIncidents();
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