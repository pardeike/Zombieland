using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace ZombieLand
{
	public class IncidentParameters
	{
		public string spawnMode = "";
		public int daysBeforeZombies;
		public int maxNumberOfZombies;
		public int numberOfZombiesPerColonist;
		public float colonyMultiplier;

		public int capableColonists;
		public int incapableColonists;
		public int totalColonistCount;
		public int minimumCapableColonists;
		public float daysPassed;
		public int currentZombieCount;
		public int maxBaseLevelZombies;
		public int extendedCount;
		public int maxAdditionalZombies;
		public int calculatedZombies;
		public float rampUpDays;
		public float scaleFactor;
		public float daysStretched;

		public float deltaDays;
		public int incidentSize;
		public string skipReason = "";
	}

	public class IncidentInfo : IExposable
	{
		int lastIncident;
		public IncidentParameters parameters = new();

		public int NextIncident()
		{
			var startEvent = (int)(GenDate.TicksPerDay * (parameters.daysBeforeZombies + Rand.Range(0f, 1f)));
			var followEvent = lastIncident + (int)(GenDate.TicksPerDay * parameters.deltaDays);
			return Math.Max(startEvent, followEvent);
		}

		public void Update()
		{
			lastIncident = GenTicks.TicksAbs;
		}

		public void ExposeData()
		{
			// no base.ExposeData() too call

			Scribe_Values.Look(ref lastIncident, "lastIncident");

			parameters ??= new IncidentParameters();
		}
	}

	public static class ZombiesRising
	{
		[DefOf]
		public static class MissingDifficulty
		{
			public static DifficultyDef Peaceful;
		}

		public static bool ZombiesForNewIncident(TickManager tickManager)
		{
			var info = tickManager.incidentInfo;
			if (info == null)
				return false;

			tickManager.incidentInfo ??= new IncidentInfo();
			tickManager.incidentInfo.parameters ??= new IncidentParameters();
			var parameters = tickManager.incidentInfo.parameters;

			if (tickManager.map.IsBlacklisted())
			{
				parameters.skipReason = "no zombie events in this biome";
				return false;
			}

			var currentMax = Mathf.FloorToInt(tickManager.GetMaxZombieCount() * ZombieWeather.GetThreatLevel(tickManager.map));

			(parameters.capableColonists, parameters.incapableColonists) = Tools.ColonistsInfo(tickManager.map);
			parameters.daysBeforeZombies = ZombieSettings.Values.daysBeforeZombiesCome;
			parameters.totalColonistCount = tickManager.map.mapPawns.FreeHumanlikesSpawnedOfFaction(Faction.OfPlayer).Count();
			parameters.minimumCapableColonists = (parameters.totalColonistCount + 1) / 3;
			parameters.daysPassed = GenDate.DaysPassedFloat;
			parameters.spawnMode = ZombieSettings.Values.spawnWhenType.ToString();
			parameters.currentZombieCount = tickManager.AllZombies().Count();
			parameters.numberOfZombiesPerColonist = ZombieSettings.Values.baseNumberOfZombiesinEvent;
			parameters.colonyMultiplier = ZombieSettings.Values.colonyMultiplier;
			parameters.maxBaseLevelZombies = currentMax + ZombieGenerator.ZombiesSpawning;
			parameters.extendedCount = 0;
			parameters.maxNumberOfZombies = ZombieSettings.Values.maximumNumberOfZombies;
			parameters.maxAdditionalZombies = 0;
			parameters.calculatedZombies = 0;
			parameters.incidentSize = 0;
			parameters.rampUpDays = GenMath.LerpDoubleClamped(0, 5, 40, 0, Tools.Difficulty());
			//parameters.scaleFactor = Tools.Boxed(GenMath.LerpDouble(parameters.daysBeforeZombies, parameters.daysBeforeZombies + parameters.rampUpDays, 0.2f, 1f, GenDate.DaysPassedFloat), 0.2f, 1f);
			//parameters.daysStretched = 0;
			parameters.deltaDays = 0;
			parameters.skipReason = "-";

			// zombie free days
			if (parameters.daysPassed <= parameters.daysBeforeZombies)
			{
				parameters.skipReason = "waiting for zombies";
				return false;
			}

			// outside night period
			if (ZombieSettings.Values.spawnWhenType == SpawnWhenType.WhenDark)
			{
				var hour = GenLocalDate.HourOfDay(tickManager.map);
				if (hour < 12)
					hour += 24;

				if (hour < Constants.ZOMBIE_SPAWNING_HOURS[1] || hour > Constants.ZOMBIE_SPAWNING_HOURS[2])
				{
					parameters.skipReason = "outside night period";
					return false;
				}
			}

			// too few capable colonists (only in difficulty lower than Intense)
			if (Tools.Difficulty() < 1.5f)
			{
				if (parameters.capableColonists < parameters.minimumCapableColonists)
				{
					parameters.skipReason = "too few capable colonists";
					return false;
				}
			}

			if (parameters.daysStretched == 0)
			{
				var stretchFactor = 1f + parameters.incidentSize / 150f;
				parameters.daysStretched = Rand.Range(1.5f * stretchFactor, 4f * stretchFactor);
			}
			parameters.deltaDays = parameters.daysStretched + ZombieSettings.Values.extraDaysBetweenEvents;

			// not yet time for next incident
			var ticksNow = GenTicks.TicksAbs;
			var ticksNextIncident = tickManager.incidentInfo.NextIncident();
			if (ticksNow < ticksNextIncident)
			{
				parameters.skipReason = "wait " + (ticksNextIncident - ticksNow).ToStringTicksToPeriod();
				return false;
			}

			// too little new zombies
			if (Rand.Chance(1f / 24f) && Find.Storyteller.difficulty.allowBigThreats)
			{
				parameters.extendedCount = parameters.maxNumberOfZombies - parameters.maxBaseLevelZombies;
				if (parameters.extendedCount > 0)
				{
					parameters.extendedCount = Rand.RangeInclusive(0, parameters.extendedCount);
					parameters.maxBaseLevelZombies += parameters.extendedCount;
				}
			}
			parameters.maxAdditionalZombies = Math.Max(0, parameters.maxBaseLevelZombies - parameters.currentZombieCount);
			parameters.calculatedZombies = (int)((parameters.capableColonists + parameters.capableColonists / 2) * parameters.numberOfZombiesPerColonist * parameters.colonyMultiplier);
			parameters.incidentSize = Math.Min(parameters.maxAdditionalZombies, parameters.calculatedZombies);
			if (parameters.incidentSize == 0)
			{
				parameters.skipReason = "empty incident";
				return false;
			}

			// ramp it up
			if (parameters.scaleFactor == 0)
				parameters.scaleFactor = Tools.Boxed(GenMath.LerpDouble(parameters.daysBeforeZombies, parameters.daysBeforeZombies + parameters.rampUpDays, 0.2f, 1f, GenDate.DaysPassedFloat), 0.2f, 1f);
			parameters.scaleFactor *= (0.75f + Rand.Value / 2f);
			parameters.scaleFactor = Tools.Boxed(parameters.scaleFactor, 0f, 1f);
			parameters.incidentSize = Math.Max(1, (int)(parameters.incidentSize * parameters.scaleFactor + 0.5f));

			// success
			var stretchFactor2 = 1f + parameters.incidentSize / 150f;
			parameters.daysStretched = Rand.Range(1.5f * stretchFactor2, 4f * stretchFactor2);
			parameters.deltaDays = parameters.daysStretched + ZombieSettings.Values.extraDaysBetweenEvents;
			tickManager.incidentInfo.Update();
			return true;
		}

		public static Predicate<IntVec3> SpotValidator(Predicate<IntVec3> cellValidator)
		{
			return cell =>
			{
				var count = 0;
				var vecs = Tools.GetCircle(Constants.SPAWN_INCIDENT_RADIUS).ToList();
				foreach (var vec in vecs)
					if (cellValidator(cell + vec))
					{
						if (++count >= Constants.MIN_ZOMBIE_SPAWN_CELL_COUNT)
							break;
					}
				return count >= Constants.MIN_ZOMBIE_SPAWN_CELL_COUNT;
			};
		}

		static IEnumerator SpawnEventProcess(Map map, int incidentSize, IntVec3 spot, Predicate<IntVec3> cellValidator, bool useAlert, bool ignoreLimit, ZombieType zombieType = ZombieType.Random)
		{
			var zombiesSpawning = 0;
			var counter = 1;
			var tickManager = map.GetComponent<TickManager>();
			while (incidentSize > 0 && (ignoreLimit || tickManager.CanHaveMoreZombies()) && counter <= 10)
			{
				var cells = Tools.GetCircle(Constants.SPAWN_INCIDENT_RADIUS)
					.Select(vec => spot + vec)
					.Where(vec => cellValidator(vec))
					.InRandomOrder()
					.Take(incidentSize)
					.ToList();
				yield return null;

				foreach (var cell in cells)
				{
					var it = ZombieGenerator.SpawnZombieIterativ(cell, map, zombieType, zombie => tickManager.allZombiesCached.Add(zombie));
					while (it.MoveNext())
					{
						if (ZombielandMod.frameWatch.ElapsedMilliseconds >= 10)
							yield return null;
					}
					incidentSize--;
					zombiesSpawning++;
				}
				counter++;
			}

			if (zombiesSpawning > 3)
			{
				if (useAlert)
				{
					var headline = "LetterLabelZombiesRising".Translate();
					var text = "ZombiesRising".Translate();
					if (ZombieSettings.Values.spawnHowType == SpawnHowType.AllOverTheMap)
					{
						headline = "LetterLabelZombiesRisingNearYourBase".Translate();
						text = "ZombiesRisingNearYourBase".Translate();
					}

					var location = new GlobalTargetInfo(spot, map);
					Find.LetterStack.ReceiveLetter(headline, text, LetterDefOf.ThreatSmall, location);
				}

				var (capable, incapable) = Tools.ColonistsInfo(map);
				var isSubstantialZombieCount = zombiesSpawning > capable * 4;
				if (isSubstantialZombieCount && Constants.USE_SOUND && Prefs.VolumeAmbient > 0f)
					CustomDefs.ZombiesRising.PlayOneShotOnCamera(null);
			}
		}

		public static IntVec3 GetValidSpot(Map map, IntVec3 spot, Predicate<IntVec3> cellValidator)
		{
			var allOverTheMap = ZombieSettings.Values.spawnHowType == SpawnHowType.AllOverTheMap;
			var spotValidator = SpotValidator(cellValidator);
			for (var counter = 1; counter <= 10; counter++)
			{
				if (spot.IsValid && spot.InBounds(map))
					break;
				spot = Tools.RandomSpawnCell(map, allOverTheMap == false, spotValidator);
			}
			return spot;
		}

		/*
		static readonly Dictionary<string, (long, long)> elapsed = new();
		static IEnumerator DeltaTimeProxy(Map map, int incidentSize, IntVec3 spot, Predicate<IntVec3> cellValidator, bool useAlert, bool ignoreLimit, ZombieType zombieType = ZombieType.Random)
		{
			elapsed.Clear();
			var it = SpawnEventProcess(map, incidentSize, spot, cellValidator, useAlert, ignoreLimit, zombieType);
			while (true)
			{
				var sw = Stopwatch.StartNew();
				if (it.MoveNext() == false)
					break;
				var res = it.Current;
				if (res is string key)
				{
					var ms = sw.ElapsedMilliseconds;
					if (elapsed.TryGetValue(key, out var value) == false)
						elapsed.Add(key, (ms, ms));
					else
					{
						value.Item1 = Math.Min(value.Item1, ms);
						value.Item2 = Math.Max(value.Item2, ms);
						elapsed[key] = value;
					}
					yield return null;
				}
				else
					yield return res;
			}
			foreach (var item in elapsed)
				Log.Warning($"{item.Key}: {item.Value.Item1} - {item.Value.Item2}");
		}
		*/

		public static bool TryExecute(Map map, int incidentSize, IntVec3 spot, bool useAlert, bool ignoreLimit = false, ZombieType zombieType = ZombieType.Random)
		{
			if (map.IsBlacklisted())
				return false;
			var cellValidator = Tools.ZombieSpawnLocator(map, true);
			spot = GetValidSpot(map, spot, cellValidator);
			if (spot.IsValid == false)
				return false;
			_ = Find.CameraDriver.StartCoroutine(SpawnEventProcess(map, incidentSize, spot, cellValidator, useAlert, ignoreLimit, zombieType));
			return true;
		}
	}
}