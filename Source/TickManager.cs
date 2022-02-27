using HarmonyLib;
using RimWorld;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace ZombieLand
{
	[StaticConstructorOnStartup]
	public static class ZombieTicker
	{
		public static IEnumerable<TickManager> managers;
		public static Type RimThreaded = AccessTools.TypeByName("RimThreaded.RimThreaded");

		public static float[] percentZombiesTicked = new[] { 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f };
		public static int percentZombiesTickedIndex = 0;

		public static int zombiesTicked = 0;
		public static int maxTicking = 0;
		public static int currentTicking = 0;

		public static void DoSingleTick()
		{
			if (RimThreaded == null)
				managers.Do(tickManager => tickManager.ZombieTicking());
		}

		public static float PercentTicking
		{
			get
			{
				return percentZombiesTicked.Average();
			}
			set
			{
				percentZombiesTicked[percentZombiesTickedIndex] = value;
				percentZombiesTickedIndex = (percentZombiesTickedIndex + 1) % percentZombiesTicked.Length;
			}
		}
	}

	public class TickManager : MapComponent
	{
		int populationSpawnCounter;

		int nextVisibleGridUpdate;
		int incidentTickCounter;
		int colonyPointsTickCounter;
		int avoidGridCounter;

		public IntVec3 centerOfInterest = IntVec3.Invalid;
		public int currentColonyPoints;

		public HashSet<Zombie> allZombiesCached;
		IEnumerator taskTicker;
		bool runZombiesForNewIncident = false;

		public Zombie[] currentZombiesTicking;
		public int currentZombiesTickingIndex;

		public List<ZombieCorpse> allZombieCorpses;
		public AvoidGrid avoidGrid;
		public AvoidGrid emptyAvoidGrid;

		Sustainer zombiesAmbientSound;
		float zombiesAmbientSoundVolume;

		public readonly HashSet<Zombie> hummingZombies = new HashSet<Zombie>();
		Sustainer electricSustainer;

		public Queue<ThingWithComps> colonistsConverter = new Queue<ThingWithComps>();
		public Queue<Action<Map>> rimConnectActions = new Queue<Action<Map>>();

		public List<IntVec3> explosions = new List<IntVec3>();
		public IncidentInfo incidentInfo = new IncidentInfo();
		public ZombiePathing zombiePathing;

		public List<SoSTools.Floater> floatingSpaceZombiesBack = new List<SoSTools.Floater>();
		public List<SoSTools.Floater> floatingSpaceZombiesFore = new List<SoSTools.Floater>();

		public TickManager(Map map) : base(map)
		{
			zombiePathing = new ZombiePathing(map);
			zombiePathing.UpdateRegions();

			currentColonyPoints = 100;
			allZombiesCached = new HashSet<Zombie>();
			allZombieCorpses = new List<ZombieCorpse>();

			var type = ZombieTicker.RimThreaded;
			if (type != null)
			{
				var addNormalTicking = AccessTools.Method(type, "AddNormalTicking");
				if (addNormalTicking != null)
					_ = addNormalTicking.Invoke(null, new object[]
					{
						this,
						new Action<object>(PrepareThreadedTicking),
						new Action<object>(DoThreadedSingleTick)
					});
			}
		}

		public override void FinalizeInit()
		{
			base.FinalizeInit();

			Tools.nextPlayerReachableRegionsUpdate = 0;

			var grid = map.GetGrid();
			grid.IterateCellsQuick(cell => cell.zombieCount = 0);

			colonyPointsTickCounter = -1;
			RecalculateColonyPoints();

			nextVisibleGridUpdate = 0;
			RecalculateZombieWanderDestination();

			var destinations = map.pawnDestinationReservationManager.reservedDestinations;
			var zombieFaction = Find.FactionManager.FirstFactionOfDef(ZombieDefOf.Zombies);
			if (!destinations.ContainsKey(zombieFaction)) _ = map.pawnDestinationReservationManager.GetPawnDestinationSetFor(zombieFaction);

			var allZombies = AllZombies();
			if (Tools.ShouldAvoidZombies())
			{
				var specs = allZombies
					.Where(zombie => zombie.isAlbino == false)
					.Select(zombie => new ZombieCostSpecs()
					{
						position = zombie.Position,
						radius = Tools.ZombieAvoidRadius(zombie),
						maxCosts = ZombieMaxCosts(zombie)

					}).ToList();

				avoidGrid = Tools.avoider.UpdateZombiePositionsImmediately(map, specs);
			}
			else
				avoidGrid = new AvoidGrid(map);

			hummingZombies.Clear();
			allZombies.Where(zombie => zombie.IsActiveElectric).Do(zombie => hummingZombies.Add(zombie));

			if (map.Biome == SoSTools.sosOuterSpaceBiomeDef)
			{
				for (var i = 0; i < SoSTools.Floater.backCount; i++)
					Tools.CreateFakeZombie(map, mat => floatingSpaceZombiesBack.Add(new SoSTools.Floater() { mapSize = map.Size, material = mat, foreground = false }), false);
				for (var i = 0; i < SoSTools.Floater.foreCount; i++)
					Tools.CreateFakeZombie(map, mat => floatingSpaceZombiesFore.Add(new SoSTools.Floater() { mapSize = map.Size, material = mat, foreground = true }), true);
			}

			taskTicker = TickTasks();
			while (taskTicker.Current as string != "end")
				_ = taskTicker.MoveNext();
		}

		public override void MapRemoved()
		{
			base.MapRemoved();
			Cleanup();
		}

		public void Cleanup()
		{
			StopAmbientSound();
			zombiePathing.running = false;
			zombiePathing = null;
		}

		public override void ExposeData()
		{
			base.ExposeData();

			Scribe_Values.Look(ref currentColonyPoints, "colonyPoints");
			Scribe_Collections.Look(ref allZombiesCached, "prioritizedZombies", LookMode.Reference);
			Scribe_Collections.Look(ref explosions, "explosions", LookMode.Value);
			Scribe_Deep.Look(ref incidentInfo, "incidentInfo", Array.Empty<object>());

			if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
				if (allZombiesCached == null)
					allZombiesCached = new HashSet<Zombie>();
				allZombiesCached = allZombiesCached.Where(zombie => zombie != null && zombie.Spawned && zombie.Dead == false).ToHashSet();

				if (allZombieCorpses == null)
					allZombieCorpses = new List<ZombieCorpse>();
				allZombieCorpses = allZombieCorpses.Where(corpse => corpse.DestroyedOrNull() == false && corpse.Spawned).ToList();

				runZombiesForNewIncident = true;
				if (explosions == null)
					explosions = new List<IntVec3>();
			}
		}

		public void RecalculateColonyPoints()
		{
			if (colonyPointsTickCounter-- >= 0) return;
			colonyPointsTickCounter = 100;

			currentColonyPoints = Tools.ColonyPoints().Sum();
		}

		public void RecalculateZombieWanderDestination()
		{
			var ticks = GenTicks.TicksGame;
			if (ticks < nextVisibleGridUpdate) return;
			nextVisibleGridUpdate = ticks + Constants.TICKMANAGER_RECALCULATE_DELAY;

			allZombiesCached = AllZombies().ToHashSet();
			var home = map.areaManager.Home;
			if (home.TrueCount > 0)
			{
				var cells = home.ActiveCells.ToArray();
				var cellCount = cells.Length;
				allZombiesCached.Do(zombie => zombie.wanderDestination = cells[Constants.random.Next() % cellCount]);

				centerOfInterest = new IntVec3(
					(int)Math.Round(cells.Average(c => c.x)),
					0,
					(int)Math.Round(cells.Average(c => c.z))
				);
			}
			else
				allZombiesCached.Do(zombie => zombie.wanderDestination = new IntVec3(Rand.Range(10, map.Size.x - 10), 0, Rand.Range(10, map.Size.z - 10)));
		}

		public int GetMaxZombieCount()
		{
			if (map?.mapPawns == null) return 0;
			if (Constants.DEBUG_MAX_ZOMBIE_COUNT >= 0) return Constants.DEBUG_MAX_ZOMBIE_COUNT;
			var colonists = Tools.CapableColonists(map);
			var perColonistZombieCount = GenMath.LerpDoubleClamped(0f, 4f, 5, 30, Mathf.Sqrt(colonists));
			var colonistMultiplier = Mathf.Sqrt(colonists) * 2;
			var baseStrengthFactor = GenMath.LerpDoubleClamped(0, 40000, 1f, 8f, currentColonyPoints);
			var colonyMultiplier = ZombieSettings.Values.colonyMultiplier;
			var difficultyMultiplier = Tools.Difficulty();
			var count = (int)(perColonistZombieCount * colonistMultiplier * baseStrengthFactor * colonyMultiplier * difficultyMultiplier);
			return Mathf.Min(ZombieSettings.Values.maximumNumberOfZombies, count);
		}

		public void ZombieTicking()
		{
			PrepareThreadedTicking(this);
			var threatLevel = ZombieWeather.GetThreatLevel(map);
			for (var i = 0; i < currentZombiesTicking.Length; i++)
				currentZombiesTicking[i].CustomTick(threatLevel);
		}

		public static void PrepareThreadedTicking(object input)
		{
			var tickManager = (TickManager)input;
			var f = ZombieTicker.PercentTicking;
			var zombies = tickManager.allZombiesCached.Where(zombie => zombie.Spawned && zombie.Dead == false);
			if (f < 1f)
			{
				var partition = Mathf.FloorToInt(zombies.Count() * f);
				zombies = zombies.InRandomOrder().Take(partition);
			}
			tickManager.currentZombiesTicking = zombies.ToArray();
			tickManager.currentZombiesTickingIndex = tickManager.currentZombiesTicking.Length;
		}

		public static void DoThreadedSingleTick(object input)
		{
			// is being called by many threads at the same time
			var tickManager = (TickManager)input;
			var threatLevel = ZombieWeather.GetThreatLevel(tickManager.map);
			while (true)
			{
				var idx = Interlocked.Decrement(ref tickManager.currentZombiesTickingIndex);
				if (idx < 0) return;
				tickManager.currentZombiesTicking[idx].CustomTick(threatLevel);
			}
		}

		public static float ZombieMaxCosts(Zombie zombie)
		{
			return zombie.wasMapPawnBefore || zombie.raging > 0 ? 3000f : 1000f;
		}

		public Zombie GetRopableZombie(Vector3 clickPos)
		{
			return allZombiesCached.FirstOrDefault(zombie =>
			{
				if (zombie.consciousness > Constants.MIN_CONSCIOUSNESS)
					return false;
				return ((clickPos - zombie.DrawPos).MagnitudeHorizontalSquared() <= 0.8f);
			});
		}

		public void UpdateZombieAvoider()
		{
			var specs = allZombiesCached.Where(zombie =>
					zombie.isAlbino == false &&
					zombie.ropedBy == null &&
					zombie.paralyzedUntil == 0 &&
					zombie.Spawned &&
					zombie.Dead == false &&
					zombie.health.Downed == false
				)
				.Select(zombie => new ZombieCostSpecs()
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
				var success = ZombiesRising.TryExecute(map, incidentInfo.parameters.incidentSize, IntVec3.Invalid, true);
				if (success == false)
					Log.Warning("Incident creation failed. Most likely no valid spawn point found.");
			}
		}

		bool RepositionCondition(Pawn pawn)
		{
			return pawn.Spawned &&
				pawn.health.Downed == false &&
				pawn.Dead == false &&
				pawn.Drafted == false &&
				avoidGrid.InAvoidDanger(pawn) &&
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
							.SelectMany(vec => map.thingGrid.ThingsListAtFast(vec).OfType<Zombie>())
							.Where(zombie => zombie.health.Downed == false);

						var maxDistance = 0;
						var safeDestination = IntVec3.Invalid;
						map.floodFiller.FloodFill(pos, delegate (IntVec3 vec)
						{
							if (!vec.Walkable(map)) return false;
							if ((float)vec.DistanceToSquared(pos) > radiusSquared) return false;
							if (map.thingGrid.ThingAt<Zombie>(vec)?.health.Downed ?? true == false) return false;
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
							var newJob = JobMaker.MakeJob(JobDefOf.Goto, safeDestination);
							newJob.playerForced = true;
							pawn.jobs.StartJob(newJob, JobCondition.InterruptForced, null, false, true, null, null);
						}
					});
		}

		void FetchAvoidGrid()
		{
			if (Tools.ShouldAvoidZombies() == false)
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
			return allZombiesCached.Count(zombie => zombie.Spawned && zombie.Dead == false) + ZombieGenerator.ZombiesSpawning;
		}

		public bool CanHaveMoreZombies()
		{
			var currentMax = Mathf.FloorToInt(GetMaxZombieCount() * ZombieWeather.GetThreatLevel(map));
			return ZombieCount() < currentMax;
		}

		public void IncreaseZombiePopulation()
		{
			if (map.AllowsZombies()) return;
			if (GenDate.DaysPassedFloat < ZombieSettings.Values.daysBeforeZombiesCome) return;
			if (ZombieSettings.Values.spawnWhenType == SpawnWhenType.InEventsOnly) return;

			if (populationSpawnCounter-- < 0)
			{
				var min = GenMath.LerpDoubleClamped(1.5f, 5, 400, 15, Tools.Difficulty());
				var max = GenMath.LerpDoubleClamped(1.5f, 5, 15, 2, Tools.Difficulty());
				populationSpawnCounter = (int)GenMath.LerpDoubleClamped(0, 40000, min, max, currentColonyPoints);

				if (CanHaveMoreZombies())
				{
					switch (ZombieSettings.Values.spawnHowType)
					{
						case SpawnHowType.AllOverTheMap:
							{
								var cell = Tools.RandomSpawnCell(map, false, Tools.ZombieSpawnLocator(map));
								if (cell.IsValid)
									ZombieGenerator.SpawnZombie(cell, map, ZombieType.Random, (zombie) => { _ = allZombiesCached.Add(zombie); });
								return;
							}
						case SpawnHowType.FromTheEdges:
							{
								var cell = Tools.RandomSpawnCell(map, true, Tools.ZombieSpawnLocator(map));
								if (cell.IsValid)
									ZombieGenerator.SpawnZombie(cell, map, ZombieType.Random, (zombie) => { _ = allZombiesCached.Add(zombie); });
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

		public void UpdateElectricalHumming()
		{
			var ticks = DateTime.Now.Ticks;
			if ((ticks % 30) != 0)
				return;

			if (Constants.USE_SOUND == false || Prefs.VolumeAmbient <= 0f)
			{
				electricSustainer?.End();
				electricSustainer = null;
				return;
			}

			if (electricSustainer == null)
				electricSustainer = CustomDefs.ZombieElectricHum.TrySpawnSustainer(SoundInfo.OnCamera(MaintenanceType.None));

			if (hummingZombies.Count == 0)
			{
				electricSustainer.info.volumeFactor = 0f;
				return;
			}

			var cameraPos = Find.CameraDriver.transform.position;
			var nearestElectricalZombieDistance = hummingZombies
				.Select(zombie => (cameraPos - zombie.DrawPos).magnitude)
				.OrderBy(dist => dist)
				.First();

			electricSustainer.info.volumeFactor = 1f - Math.Min(1f, nearestElectricalZombieDistance / 36f);
		}

		public void StopAmbientSound()
		{
			zombiesAmbientSound?.End();
			zombiesAmbientSound = null;
		}

		IEnumerator TickTasks()
		{
			if (runZombiesForNewIncident && map != null)
			{
				runZombiesForNewIncident = false;
				_ = ZombiesRising.ZombiesForNewIncident(this);
			}

			while (true)
			{
				var sw = new Stopwatch();
				sw.Start();
				RepositionColonists();
				yield return null;
				HandleIncidents();
				yield return null;
				FetchAvoidGrid();
				yield return null;
				RecalculateColonyPoints();
				yield return null;
				RecalculateZombieWanderDestination();
				yield return null;
				UpdateZombieAvoider();
				yield return null;
				ExecuteExplosions();
				yield return null;
				var volume = 0f;
				if (allZombiesCached.Any())
				{
					if (map != null)
					{
						var hour = GenLocalDate.HourFloat(map);
						if (hour < 12f) hour += 24f;
						if (hour > Constants.HOUR_START_OF_NIGHT && hour < Constants.HOUR_END_OF_NIGHT)
							volume = 1f;
						else if (hour >= Constants.HOUR_START_OF_DUSK && hour <= Constants.HOUR_START_OF_NIGHT)
							volume = GenMath.LerpDouble(Constants.HOUR_START_OF_DUSK, Constants.HOUR_START_OF_NIGHT, 0f, 1f, hour);
						else if (hour >= Constants.HOUR_END_OF_NIGHT && hour <= Constants.HOUR_START_OF_DAWN)
							volume = GenMath.LerpDouble(Constants.HOUR_END_OF_NIGHT, Constants.HOUR_START_OF_DAWN, 1f, 0f, hour);
					}
				}
				yield return null;
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
					StopAmbientSound();

				yield return null;
				if (colonistsConverter.Count > 0 && map != null)
				{
					var pawn = colonistsConverter.Dequeue();
					Tools.ConvertToZombie(pawn, map);
				}
				yield return null;
				if (rimConnectActions.Count > 0 && map != null)
				{
					var action = rimConnectActions.Dequeue();
					action(map);
				}
				yield return "end";
			}
		}

		public override void MapComponentTick()
		{
			base.MapComponentTick();

			_ = taskTicker.MoveNext();
			IncreaseZombiePopulation();
		}
	}
}
