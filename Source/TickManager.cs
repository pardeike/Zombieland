using HarmonyLib;
using RimWorld;
using System;
using System.Collections;
using System.Collections.Generic;
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
				managers.Do(tickManager =>
				{
					switch (tickManager.isInitialized)
					{
						case 0:
							Log.Error("Fatal error! Zombieland's TickManager is not initialized. This should never happen unless you're using another mod that caused an error in MapComponent.FinalizeInit");
							break;
						case 1:
							Log.Error("Fatal error! Zombieland's TickManager is not initialized. The base implementation never returned which means another mod is causing an error in MapComponent.FinalizeInit");
							break;
						case 2:
							Log.Error("Fatal error! Zombieland's TickManager is not initialized because its FinalizeInit method caused an error. Maybe another mod caused this error indirectly, you should report this.");
							break;
						case 3:
							tickManager.ZombieTicking();
							break;
					}
				});
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
		public int isInitialized = 0;
		int populationSpawnCounter;

		int nextVisibleGridUpdate;
		int incidentTickCounter;
		int colonyPointsTickCounter;
		int avoidGridCounter;

		public IntVec3 centerOfInterest = IntVec3.Invalid;
		public IntVec3 nextCenterOfInterest = IntVec3.Invalid;
		public int centerOfInterestUpdateTicks = 0;
		public int currentColonyPoints;
		public int mapSpawnedTicks = 0;

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

		public readonly HashSet<Zombie> hummingZombies = new();
		Sustainer electricSustainer;

		public Queue<ThingWithComps> colonistsToConvert = new();
		public Queue<Action<Map>> rimConnectActions = new();

		public List<IntVec3> explosions = new();
		public IncidentInfo incidentInfo = new();
		public ZombiePathing zombiePathing;

		public List<SoSTools.Floater> floatingSpaceZombiesBack;
		public List<SoSTools.Floater> floatingSpaceZombiesFore;

		public List<VictimHead> victimHeads = new();

		public TickManager(Map map) : base(map)
		{
			zombiePathing = new ZombiePathing(map);
			zombiePathing.UpdateRegions();

			currentColonyPoints = 100;
			mapSpawnedTicks = 0;

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

		public override void MapGenerated()
		{
			mapSpawnedTicks = GenTicks.TicksGame;
			base.MapGenerated();
		}

		public override void FinalizeInit()
		{
			isInitialized = 1;
			base.FinalizeInit();
			isInitialized = 2;

			Tools.nextPlayerReachableRegionsUpdate = 0;

			var grid = map.GetGrid();
			grid.IterateCellsQuick(cell => cell.zombieCount = 0);

			colonyPointsTickCounter = -1;
			RecalculateColonyPoints();

			nextVisibleGridUpdate = 0;
			RecalculateZombieWanderDestination();

			var destinations = map.pawnDestinationReservationManager.reservedDestinations;
			var zombieFaction = Find.FactionManager.FirstFactionOfDef(ZombieDefOf.Zombies);
			if (!destinations.ContainsKey(zombieFaction))
				_ = map.pawnDestinationReservationManager.GetPawnDestinationSetFor(zombieFaction);

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

			taskTicker = TickTasks();
			while (taskTicker.Current as string != "end")
				_ = taskTicker.MoveNext();

			isInitialized = 3;
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
			Scribe_Values.Look(ref mapSpawnedTicks, "mapSpawnedTicks");

			if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
				allZombiesCached ??= new HashSet<Zombie>();
				allZombiesCached = allZombiesCached.Where(zombie => zombie != null && zombie.Spawned && zombie.Dead == false).ToHashSet();

				allZombieCorpses ??= new List<ZombieCorpse>();
				allZombieCorpses = allZombieCorpses.Where(corpse => corpse.DestroyedOrNull() == false && corpse.Spawned).ToList();

				runZombiesForNewIncident = true;
				explosions ??= new List<IntVec3>();
			}
		}

		static readonly Mesh headMesh = MeshPool.humanlikeHeadSet.MeshAt(Rot4.South);
		public override void MapComponentUpdate()
		{
			foreach (var head in victimHeads)
			{
				var mat = new Material(head.material);
				mat.color = new Color(mat.color.r, mat.color.g, mat.color.b, head.alpha);
				GraphicToolbox.DrawScaledMesh(headMesh, mat, head.Position, head.quat, 0.7f, 0.7f);
			}
		}

		public void RecalculateColonyPoints()
		{
			if (colonyPointsTickCounter-- >= 0)
				return;
			colonyPointsTickCounter = 100;

			currentColonyPoints = Tools.ColonyPoints().Sum();
		}

		public void RecalculateZombieWanderDestination()
		{
			var ticks = Find.TickManager.TicksGame;
			if (ticks < nextVisibleGridUpdate)
				return;
			nextVisibleGridUpdate = ticks + Constants.TICKMANAGER_RECALCULATE_DELAY;

			allZombiesCached = AllZombies().ToHashSet();
			var home = map.areaManager.Home;
			Room[] valuableRooms = null;
			if (home.TrueCount > 0)
			{
				var homeCells = home.ActiveCells.ToArray();
				allZombiesCached.Do(zombie => zombie.wanderDestination = homeCells.RandomElement());
				var tankys = allZombiesCached.Where(zombie => zombie.IsTanky && zombie.tankDestination.IsValid == false);
				if (tankys.Any())
				{
					valuableRooms ??= Tools.ValuableRooms(map).ToArray();
					if (valuableRooms.Length > 0)
						tankys.Do(zombie => zombie.tankDestination = valuableRooms.RandomElement().Cells.RandomElement());
				}

				if (ticks > centerOfInterestUpdateTicks)
				{
					centerOfInterestUpdateTicks = ticks + Constants.CENTER_OF_INTEREST_UPDATE;
					if (Rand.Bool)
						nextCenterOfInterest = homeCells.SafeRandomElement();
					else
					{
						valuableRooms ??= Tools.ValuableRooms(map).ToArray();
						if (valuableRooms.Length > 0)
							nextCenterOfInterest = valuableRooms.SelectMany(room => room.Cells).SafeRandomElement();
						else
							nextCenterOfInterest = homeCells.SafeRandomElement();
					}
				}
			}
			else
			{
				valuableRooms ??= Tools.ValuableRooms(map).ToArray();
				if (valuableRooms.Length > 0)
				{
					allZombiesCached.Do(zombie => zombie.wanderDestination = valuableRooms[Rand.Range(0, valuableRooms.Length - 1)].Cells.RandomElement());
					if (ticks > centerOfInterestUpdateTicks)
						nextCenterOfInterest = valuableRooms.SelectMany(room => room.Cells).SafeRandomElement();
				}
				else
					allZombiesCached.Do(zombie => zombie.wanderDestination = new IntVec3(Rand.Range(10, map.Size.x - 10), 0, Rand.Range(10, map.Size.z - 10)));
			}

			if (centerOfInterest.IsValid == false && nextCenterOfInterest.IsValid)
				centerOfInterest = nextCenterOfInterest;
			else if (nextCenterOfInterest.IsValid && centerOfInterest != nextCenterOfInterest)
				centerOfInterest += new IntVec3(Math.Sign(nextCenterOfInterest.x - centerOfInterest.x), 0, Math.Sign(nextCenterOfInterest.z - centerOfInterest.z));
		}

		public int GetMaxZombieCount()
		{
			if (map?.mapPawns == null)
				return 0;
			if (Constants.DEBUG_MAX_ZOMBIE_COUNT >= 0)
				return Constants.DEBUG_MAX_ZOMBIE_COUNT;
			var (capable, incapable) = Tools.ColonistsInfo(map);
			var perColonistZombieCount = GenMath.LerpDoubleClamped(0f, 4f, 5, 30, Mathf.Sqrt(capable));
			var colonistMultiplier = Mathf.Sqrt(capable) * 2 + incapable / 2f;
			var baseStrengthFactor = GenMath.LerpDoubleClamped(0, 40000, 1f, 8f, currentColonyPoints);
			var colonyMultiplier = ZombieSettings.Values.colonyMultiplier;
			var difficultyMultiplier = Tools.Difficulty();
			var count = (int)(perColonistZombieCount * colonistMultiplier * baseStrengthFactor * colonyMultiplier * difficultyMultiplier);
			var max = capable <= 1 && incapable <= 2 ? 25 * capable + 10 * (incapable - capable) : 99999;
			return Mathf.Min(ZombieSettings.Values.maximumNumberOfZombies, Mathf.Min(max, count));
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
				if (idx < 0)
					return;
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
			if (incidentTickCounter++ < GenDate.TicksPerHour)
				return;
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

		void UpdateGameSettings()
		{
			var ticks = Find.TickManager.TicksGame;
			ZombieSettings.Values = ZombieSettings.CalculateInterpolation(ZombieSettings.ValuesOverTime, ticks);
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
							if (!vec.Walkable(map))
								return false;
							if ((float)vec.DistanceToSquared(pos) > radiusSquared)
								return false;
							if (map.thingGrid.ThingAt<Zombie>(vec)?.health.Downed ?? true == false)
								return false;
							if (vec.GetEdifice(map) is Building_Door building_Door && !building_Door.CanPhysicallyPass(pawn))
								return false;
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
				emptyAvoidGrid ??= new AvoidGrid(map);
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
			if (map.mapPawns == null || map.mapPawns.AllPawns == null)
				return new List<Zombie>();
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

		public bool NewMapZombieDelay(int at)
		{
			if (mapSpawnedTicks == 0) return false;
			var ticksDelay = Tools.NewMapZombieTicksDelay();
			return at - mapSpawnedTicks < ticksDelay;
		}

		public void IncreaseZombiePopulation()
		{
			if (map.IsBlacklisted())
				return;
			if (GenDate.DaysPassedFloat < ZombieSettings.Values.daysBeforeZombiesCome)
				return;
			if (NewMapZombieDelay(GenTicks.TicksGame))
				return;
			if (ZombieSettings.Values.spawnWhenType == SpawnWhenType.InEventsOnly)
				return;

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
							{
								var zombie = ZombieGenerator.SpawnZombie(cell, map, ZombieType.Random);
								_ = allZombiesCached.Add(zombie);
							}
							return;
						}
						case SpawnHowType.FromTheEdges:
						{
							var cell = Tools.RandomSpawnCell(map, true, Tools.ZombieSpawnLocator(map));
							if (cell.IsValid)
							{
								var zombie = ZombieGenerator.SpawnZombie(cell, map, ZombieType.Random);
								_ = allZombiesCached.Add(zombie);
							}
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

		public void TickHeads()
		{
			var heads = victimHeads.ToArray();
			foreach (var head in heads)
				if (head.Tick())
					_ = victimHeads.Remove(head);
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

			electricSustainer ??= CustomDefs.ZombieElectricHum.TrySpawnSustainer(SoundInfo.OnCamera(MaintenanceType.None));

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
				UpdateGameSettings();
				yield return null;
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
						if (hour < 12f)
							hour += 24f;
						if (hour > Constants.ZOMBIE_SPAWNING_HOURS[1] && hour < Constants.ZOMBIE_SPAWNING_HOURS[2])
							volume = 1f;
						else if (hour >= Constants.ZOMBIE_SPAWNING_HOURS[0] && hour <= Constants.ZOMBIE_SPAWNING_HOURS[1])
							volume = GenMath.LerpDouble(Constants.ZOMBIE_SPAWNING_HOURS[0], Constants.ZOMBIE_SPAWNING_HOURS[1], 0f, 1f, hour);
						else if (hour >= Constants.ZOMBIE_SPAWNING_HOURS[2] && hour <= Constants.ZOMBIE_SPAWNING_HOURS[3])
							volume = GenMath.LerpDouble(Constants.ZOMBIE_SPAWNING_HOURS[2], Constants.ZOMBIE_SPAWNING_HOURS[3], 1f, 0f, hour);
					}
				}
				yield return null;
				if (Constants.USE_SOUND && ZombieSettings.Values.playCreepyAmbientSound)
				{
					zombiesAmbientSound ??= CustomDefs.ZombiesClosingIn.TrySpawnSustainer(SoundInfo.OnCamera(MaintenanceType.None));

					if (volume < zombiesAmbientSoundVolume)
						zombiesAmbientSoundVolume -= 0.0001f;
					else if (volume > zombiesAmbientSoundVolume)
						zombiesAmbientSoundVolume += 0.0001f;
					zombiesAmbientSound.info.volumeFactor = zombiesAmbientSoundVolume;
				}
				else
				{
					StopAmbientSound();
					yield return null;
				}

				if (colonistsToConvert.Count > 0 && map != null)
				{
					var pawn = colonistsToConvert.Dequeue();
					Tools.ConvertToZombie(pawn, map);
					yield return null;
				}
				if (rimConnectActions.Count > 0 && map != null)
				{
					var action = rimConnectActions.Dequeue();
					action(map);
					yield return null;
				}

				yield return "end"; // must be called "end"!
			}
		}

		public override void MapComponentTick()
		{
			base.MapComponentTick();

			_ = taskTicker.MoveNext();
			IncreaseZombiePopulation();
			SoSTools.GenerateSpaceZombies(this);
			TickHeads();
		}
	}
}
