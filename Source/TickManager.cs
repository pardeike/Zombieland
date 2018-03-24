using Harmony;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace ZombieLand
{
	public class TickManager : MapComponent
	{
		int populationSpawnCounter;
		int dequeedSpawnCounter;

		int visibleGridUpdateCounter;
		int incidentTickCounter;
		int avoidGridCounter;

		public IntVec3 centerOfInterest = IntVec3.Invalid;
		public int currentColonyPoints;

		public List<Zombie> allZombiesCached;
		public AvoidGrid avoidGrid;
		public AvoidGrid emptyAvoidGrid;

		Sustainer zombiesAmbientSound;
		float zombiesAmbientSoundVolume;

		public List<IntVec3> explosions = new List<IntVec3>();

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

			RecalculateVisibleMap();

			var destinations = Traverse.Create(map.pawnDestinationReservationManager).Field("reservedDestinations").GetValue<Dictionary<Faction, PawnDestinationReservationManager.PawnDestinationSet>>();
			var zombieFaction = Find.FactionManager.FirstFactionOfDef(ZombieDefOf.Zombies);
			if (!destinations.ContainsKey(zombieFaction)) map.pawnDestinationReservationManager.RegisterFaction(zombieFaction);

			if (ZombieSettings.Values.betterZombieAvoidance)
			{
				var specs = AllZombies().Select(zombie => new ZombieCostSpecs()
				{
					position = zombie.Position,
					radius = Tools.ZombieAvoidRadius(zombie),
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
			Scribe_Collections.Look(ref explosions, "explosions", LookMode.Value);
			Scribe_Deep.Look(ref incidentInfo, "incidentInfo", new object[0]);

			if (allZombiesCached == null)
				allZombiesCached = new List<Zombie>();
			allZombiesCached = allZombiesCached.Where(zombie => zombie != null && zombie.Spawned && zombie.Dead == false).ToList();

			if (incidentInfo == null)
			{
				incidentInfo = new IncidentInfo
				{
					parameters = new IncidentParameters()
				};
			}

			if (explosions == null)
				explosions = new List<IntVec3>();
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
			if (Constants.DEBUG_MAX_ZOMBIE_COUNT >= 0) return Constants.DEBUG_MAX_ZOMBIE_COUNT;
			var colonists = Tools.CapableColonists(map);
			var perColonistZombieCount = GenMath.LerpDouble(0f, 4f, 5, 30, Mathf.Min(4, Mathf.Sqrt(colonists)));
			var colonistMultiplier = Mathf.Sqrt(colonists) * 2;
			var baseStrengthFactor = GenMath.LerpDouble(0, 1000, 1f, 4f, Mathf.Min(1000, currentColonyPoints));
			var colonyMultiplier = ZombieSettings.Values.colonyMultiplier;
			var difficultyMultiplier = Find.Storyteller.difficulty.threatScale;
			var count = (int)(perColonistZombieCount * colonistMultiplier * baseStrengthFactor * colonyMultiplier * difficultyMultiplier);
			return Mathf.Min(ZombieSettings.Values.maximumNumberOfZombies, count);
		}

		public void ZombieTicking()
		{
			if (Find.TickManager.TickRateMultiplier == 0f) return;
			var zombies = allZombiesCached.Where(zombie => zombie.Spawned && zombie.Dead == false).ToList();

			for (var i = 0; i < 2; i++)
				foreach (var zombie in zombies)
					zombie.CustomTick();
		}

		public void DequeuAndSpawnZombies()
		{
			if (dequeedSpawnCounter-- < 0)
			{
				dequeedSpawnCounter = Rand.Range(10, 51);

				var result = Tools.generator.TryGetNextGeneratedZombie(map);
				if (result == null) return;
				if (result.isEvent == false && ZombieCount() >= GetMaxZombieCount())
				{
					Tools.generator.RequeueZombie(result);
					return;
				}

				// if zombie cannot spawn at location, we are wasting it here.
				// to solve this, we need to find a better location and only if we find
				// none, we can discard it
				//
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

		public float ZombieMaxCosts(Zombie zombie)
		{
			if (zombie.wasMapPawnBefore || zombie.raging > 0)
				return 3000f;
			return 1000f;
		}

		public void UpdateZombieAvoider()
		{
			var specs = allZombiesCached.Where(zombie => zombie.Spawned && zombie.Dead == false).Select(zombie => new ZombieCostSpecs()
			{
				position = zombie.Position,
				radius = Tools.ZombieAvoidRadius(zombie),
				maxCosts = ZombieMaxCosts(zombie)

			}).ToList();
			Tools.avoider.UpdateZombiePositions(map, specs);
		}

		void HandleIncidents()
		{
			if (incidentTickCounter++ < GenDate.TicksPerHour) return;
			incidentTickCounter = 0;

			if (ZombiesRising.ZombiesForNewIncident(this))
			{
				var success = ZombiesRising.TryExecute(map, incidentInfo.parameters.incidentSize, IntVec3.Invalid);
				if (success == false)
					Log.Warning("Incident creation failed. Most likely no valid spawn point found.");
			}
		}

		bool RepositionCondition(Pawn pawn)
		{
			return pawn.Spawned &&
				pawn.Downed == false &&
				pawn.Dead == false &&
				pawn.Drafted == false &&
				avoidGrid.GetCosts()[pawn.Position.x + pawn.Position.z * map.Size.x] > 0 &&
				pawn.InMentalState == false &&
				pawn.InContainerEnclosed == false &&
				(pawn.CurJob == null || (pawn.CurJob.def != JobDefOf.Goto && pawn.CurJob.playerForced == false));
		}

		void RepositionColonists()
		{
			var checkInterval = 15;
			var radius = 7f;
			var radiusSquared = (int)(radius * radius);

			map.mapPawns
					.FreeHumanlikesSpawnedOfFaction(Faction.OfPlayer)
					.Where(colonist => colonist.IsHashIntervalTick(checkInterval) && RepositionCondition(colonist))
					.Do(pawn =>
					{
						var pos = pawn.Position;

						var zombiesNearby = Tools.GetCircle(radius).Select(vec => pos + vec)
							.Where(vec => vec.InBounds(map) && avoidGrid.GetCosts()[vec.x + vec.z * map.Size.x] >= 3000)
							.SelectMany(vec => map.thingGrid.ThingsListAtFast(vec).OfType<Zombie>());

						var maxDistance = 0;
						var safeDestination = IntVec3.Invalid;
						map.floodFiller.FloodFill(pos, delegate (IntVec3 vec)
						{
							if (!vec.Walkable(map)) return false;
							if ((float)vec.DistanceToSquared(pos) > radiusSquared) return false;
							if (map.thingGrid.ThingAt<Zombie>(vec) != null) return false;
							if (vec.GetEdifice(map) is Building_Door building_Door && !building_Door.CanPhysicallyPass(pawn)) return false;
							return !PawnUtility.AnyPawnBlockingPathAt(vec, pawn, true, false);

						}, delegate (IntVec3 vec)
						{
							var distance = zombiesNearby.Select(zombie => (vec - zombie.Position).LengthHorizontalSquared).Sum();
							if (distance > maxDistance)
							{
								maxDistance = distance;
								safeDestination = vec;
							}
							return false;

						});

						if (safeDestination.IsValid)
						{
							var newJob = new Job(JobDefOf.Goto, safeDestination)
							{
								playerForced = true
							};
							pawn.jobs.StartJob(newJob, JobCondition.InterruptForced, null, false, true, null, null);
						}
					});
		}

		void FetchAvoidGrid()
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
			return allZombiesCached.Count(zombie => zombie.Spawned && zombie.Dead == false);
		}

		public void IncreaseZombiePopulation()
		{
			if (GenDate.DaysPassedFloat < ZombieSettings.Values.daysBeforeZombiesCome) return;
			if (ZombieSettings.Values.spawnWhenType == SpawnWhenType.InEventsOnly) return;

			if (populationSpawnCounter-- < 0)
			{
				populationSpawnCounter = (int)GenMath.LerpDouble(0, 1000, 300, 20, Math.Max(100, Math.Min(1000, currentColonyPoints)));

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
								if (CellFinder.TryFindRandomEdgeCellWith(Tools.ZombieSpawnLocator(map), map, CellFinder.EdgeRoadChance_Neutral, out var cell))
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

		public void AddExplosion(IntVec3 pos)
		{
			explosions.Add(pos);
		}

		public void ExecuteExplosions()
		{
			foreach (var position in explosions)
			{
				var explosion = new Explosion(map, position);
				explosion.Explode();
			}
			explosions.Clear();
		}

		public override void MapComponentTick()
		{
			RepositionColonists();
			HandleIncidents();
			FetchAvoidGrid();
			RecalculateVisibleMap();
			IncreaseZombiePopulation();
			DequeuAndSpawnZombies();
			UpdateZombieAvoider();
			ExecuteExplosions();

			var volume = 0f;
			if (allZombiesCached.Any())
			{
				var hour = GenLocalDate.HourFloat(Find.VisibleMap);
				if (hour < 12f) hour += 24f;
				if (hour > Constants.HOUR_START_OF_NIGHT && hour < Constants.HOUR_END_OF_NIGHT)
					volume = 1f;
				else if (hour >= Constants.HOUR_START_OF_DUSK && hour <= Constants.HOUR_START_OF_NIGHT)
					volume = GenMath.LerpDouble(Constants.HOUR_START_OF_DUSK, Constants.HOUR_START_OF_NIGHT, 0f, 1f, hour);
				else if (hour >= Constants.HOUR_END_OF_NIGHT && hour <= Constants.HOUR_START_OF_DAWN)
					volume = GenMath.LerpDouble(Constants.HOUR_END_OF_NIGHT, Constants.HOUR_START_OF_DAWN, 1f, 0f, hour);
			}

			if (Constants.USE_SOUND && ZombieSettings.Values.playCreepyAmbientSound)
			{
				if (zombiesAmbientSound == null)
					zombiesAmbientSound = CustomDefs.ZombiesClosingIn.TrySpawnSustainer(SoundInfo.OnCamera(MaintenanceType.None));

				if (volume < zombiesAmbientSoundVolume)
					zombiesAmbientSoundVolume -= 0.0001f;
				else if (volume > zombiesAmbientSoundVolume)
					zombiesAmbientSoundVolume += 0.0001f;
				zombiesAmbientSound.info.volumeFactor = zombiesAmbientSoundVolume;
			}
			else
			{
				if (zombiesAmbientSound != null)
				{
					zombiesAmbientSound.End();
					zombiesAmbientSound = null;
				}
			}
		}
	}
}