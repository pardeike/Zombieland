using RimWorld;
using System;
using System.Linq;
using Verse;
using Harmony;
using RimWorld.Planet;
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
		public int totalColonistCount;
		public int minimumCapableColonists;
		public float daysPassed;
		public int storytellerDifficulty;
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
		public IncidentParameters parameters = new IncidentParameters();

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
			Scribe_Values.Look(ref lastIncident, "lastIncident");

			if (parameters == null)
				parameters = new IncidentParameters();
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
			if (info == null) return false;

			if (tickManager.incidentInfo == null)
				tickManager.incidentInfo = new IncidentInfo();
			if (tickManager.incidentInfo.parameters == null)
				tickManager.incidentInfo.parameters = new IncidentParameters();
			var parameters = tickManager.incidentInfo.parameters;

			parameters.capableColonists = Tools.CapableColonists(tickManager.map);
			parameters.daysBeforeZombies = ZombieSettings.Values.daysBeforeZombiesCome;
			parameters.totalColonistCount = tickManager.map.mapPawns.FreeHumanlikesSpawnedOfFaction(Faction.OfPlayer).Count();
			parameters.minimumCapableColonists = (parameters.totalColonistCount + 1) / 3;
			parameters.daysPassed = GenDate.DaysPassedFloat;
			parameters.spawnMode = ZombieSettings.Values.spawnWhenType.ToString();
			parameters.storytellerDifficulty = Find.Storyteller.difficulty.difficulty;
			parameters.currentZombieCount = tickManager.AllZombies().Count();
			parameters.numberOfZombiesPerColonist = ZombieSettings.Values.baseNumberOfZombiesinEvent;
			parameters.colonyMultiplier = ZombieSettings.Values.colonyMultiplier;
			parameters.maxBaseLevelZombies = tickManager.GetMaxZombieCount();
			parameters.extendedCount = 0;
			parameters.maxNumberOfZombies = ZombieSettings.Values.maximumNumberOfZombies;
			parameters.maxAdditionalZombies = 0;
			parameters.calculatedZombies = 0;
			parameters.incidentSize = 0;
			parameters.rampUpDays = GenMath.LerpDouble(1, 5, 40, 0, Math.Max(1, Find.Storyteller.difficulty.difficulty));
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
				if (hour < 12) hour += 24;

				if (hour < Constants.HOUR_START_OF_NIGHT || hour > Constants.HOUR_END_OF_NIGHT)
				{
					parameters.skipReason = "outside night period";
					return false;
				}
			}

			// too few capable colonists (only in difficulty lower than Intense)
			if (parameters.storytellerDifficulty < DifficultyDefOf.Hard.difficulty)
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
			parameters.calculatedZombies = (int)(parameters.capableColonists * parameters.numberOfZombiesPerColonist * parameters.colonyMultiplier);
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

		public static Predicate<IntVec3> SpotValidator(Map map)
		{
			var cellValidator = Tools.ZombieSpawnLocator(map, true);
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

		public static bool TryExecute(Map map, int incidentSize)
		{
			var spotValidator = SpotValidator(map);

			var spot = IntVec3.Invalid;
			var headline = "";
			var text = "";
			for (var counter = 1; counter <= 10; counter++)
			{
				if (ZombieSettings.Values.spawnHowType == SpawnHowType.AllOverTheMap)
				{
					var tickManager = map.GetComponent<TickManager>();
					if (tickManager == null) return false;
					var center = tickManager != null ? tickManager.centerOfInterest : IntVec3.Invalid;
					if (center.IsValid == false)
						center = Tools.CenterOfInterest(map);

					RCellFinder.TryFindRandomSpotJustOutsideColony(center, map, null, out spot, spotValidator);
					headline = "LetterLabelZombiesRisingNearYourBase".Translate();
					text = "ZombiesRisingNearYourBase".Translate();
				}
				else
				{
					RCellFinder.TryFindRandomPawnEntryCell(out spot, map, 0.5f, spotValidator);
					headline = "LetterLabelZombiesRising".Translate();
					text = "ZombiesRising".Translate();
				}

				if (spot.IsValid) break;
			}
			if (spot.IsValid == false) return false;

			var cellValidator = Tools.ZombieSpawnLocator(map, true);
			while (incidentSize > 0)
			{
				Tools.GetCircle(Constants.SPAWN_INCIDENT_RADIUS)
					.Select(vec => spot + vec)
					.Where(vec => cellValidator(vec))
					.InRandomOrder()
					.Take(incidentSize)
					.Do(cell =>
					{
						Tools.generator.SpawnZombieAt(map, cell, true);
						incidentSize--;
					});
			}

			var location = new GlobalTargetInfo(spot, map);
			Find.LetterStack.ReceiveLetter(headline, text, LetterDefOf.ThreatSmall, location);

			if (Constants.USE_SOUND)
				SoundDef.Named("ZombiesRising").PlayOneShotOnCamera(null);
			return true;
		}
	}
}