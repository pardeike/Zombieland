using RimWorld;
using System;
using System.Linq;
using Verse;
using Harmony;
using RimWorld.Planet;
using Verse.Sound;

namespace ZombieLand
{
	public class IncidentInfo : IExposable
	{
		int nextIncident;

		public int NextIncident
		{
			get
			{
				if (nextIncident == 0)
					nextIncident = GenTicks.TicksAbs + (int)(GenDate.TicksPerDay * (ZombieSettings.Values.daysBeforeZombiesCome + Rand.Range(1f, 2f)));
				return nextIncident;
			}
		}

		public void Update(float deltaDays)
		{
			nextIncident = GenTicks.TicksAbs + (int)(GenDate.TicksPerDay * deltaDays);
		}

		public void ExposeData()
		{
			Scribe_Values.Look(ref nextIncident, "nextIncident");
		}
	}

	public static class ZRdebug
	{
		public static string spawnMode = "";
		public static int daysBeforeZombies;
		public static int maxNumberOfZombies;
		public static int numberOfZombiesPerColonist;

		public static int capableColonists;
		public static int totalColonistCount;
		public static int minimumCapableColonists;
		public static float daysPassed;
		public static int storytellerDifficulty;
		public static int currentZombieCount;
		public static int maxBaseLevelZombies;
		public static int extendedCount;
		public static int maxAdditionalZombies;
		public static int calculatedZombies;
		public static float rampUpDays;
		public static float scaleFactor;
		public static float dayStretchFactor;

		public static float deltaDays;
		public static int incidentSize;
		public static string skipReason = "";
	}

	public class ZombiesRising
	{
		[DefOf]
		public static class MissingDifficulty
		{
			public static DifficultyDef Peaceful;
		}

		public static int ZombiesForNewIncident(Map map)
		{
			var tickManager = map.GetComponent<TickManager>();
			var info = tickManager.incidentInfo;
			var ticksNow = GenTicks.TicksAbs;
			ZRdebug.capableColonists = Tools.CapableColonists(map);
			ZRdebug.daysBeforeZombies = ZombieSettings.Values.daysBeforeZombiesCome;
			ZRdebug.totalColonistCount = map.mapPawns.FreeHumanlikesSpawnedOfFaction(Faction.OfPlayer).Count();
			ZRdebug.minimumCapableColonists = (ZRdebug.totalColonistCount + 1) / 3;
			ZRdebug.daysPassed = GenDate.DaysPassedFloat;
			ZRdebug.spawnMode = ZombieSettings.Values.spawnWhenType.ToString();
			ZRdebug.storytellerDifficulty = Find.Storyteller.difficulty.difficulty;
			ZRdebug.currentZombieCount = tickManager.AllZombies().Count();
			ZRdebug.numberOfZombiesPerColonist = ZombieSettings.Values.baseNumberOfZombiesinEvent;
			ZRdebug.maxBaseLevelZombies = tickManager.GetMaxZombieCount();
			ZRdebug.extendedCount = 0;
			ZRdebug.maxNumberOfZombies = ZombieSettings.Values.maximumNumberOfZombies;
			ZRdebug.maxAdditionalZombies = 0;
			ZRdebug.calculatedZombies = 0;
			ZRdebug.incidentSize = 0;
			ZRdebug.rampUpDays = GenMath.LerpDouble(1, 5, 40, 0, Math.Max(1, ZRdebug.storytellerDifficulty));
			ZRdebug.scaleFactor = Tools.Boxed(GenMath.LerpDouble(ZRdebug.daysBeforeZombies, ZRdebug.daysBeforeZombies + ZRdebug.rampUpDays, 0.2f, 1f, ZRdebug.daysPassed), 0.2f, 1f);
			ZRdebug.dayStretchFactor = 0;
			ZRdebug.deltaDays = 0;
			ZRdebug.skipReason = "";

			// zombie free days
			if (ZRdebug.daysPassed <= ZRdebug.daysBeforeZombies)
			{
				ZRdebug.skipReason = "waiting for zombies";
				return 0;
			}

			// outside night period
			if (ZombieSettings.Values.spawnWhenType == SpawnWhenType.WhenDark)
			{
				var hour = GenLocalDate.HourOfDay(map);
				if (hour < 12) hour += 24;

				if (hour < Constants.HOUR_START_OF_NIGHT || hour > Constants.HOUR_END_OF_NIGHT)
				{
					ZRdebug.skipReason = "outside night period";
					return 0;
				}
			}

			// too few capable colonists (only in difficulty lower than Intense)
			if (ZRdebug.storytellerDifficulty < DifficultyDefOf.Hard.difficulty)
			{
				if (ZRdebug.capableColonists <= ZRdebug.minimumCapableColonists)
				{
					ZRdebug.skipReason = "too few capable colonists";
					return 0;
				}
			}

			// not yet time for next incident
			if (ticksNow < info.NextIncident)
			{
				ZRdebug.skipReason = "wait " + (info.NextIncident - ticksNow).ToStringTicksToPeriod();
				return 0;
			}

			// too little new zombies
			if (Rand.Chance(1f / 24f) && Find.Storyteller.difficulty.allowBigThreats)
			{
				ZRdebug.extendedCount = ZRdebug.maxNumberOfZombies - ZRdebug.maxBaseLevelZombies;
				if (ZRdebug.extendedCount > 0)
				{
					ZRdebug.extendedCount = Rand.RangeInclusive(0, ZRdebug.extendedCount);
					ZRdebug.maxBaseLevelZombies += ZRdebug.extendedCount;
				}
			}
			ZRdebug.maxAdditionalZombies = Math.Max(0, ZRdebug.maxBaseLevelZombies - ZRdebug.currentZombieCount);
			ZRdebug.calculatedZombies = ZRdebug.capableColonists * ZRdebug.numberOfZombiesPerColonist;
			ZRdebug.incidentSize = Math.Min(ZRdebug.maxAdditionalZombies, ZRdebug.calculatedZombies);
			if (ZRdebug.incidentSize == 0)
			{
				ZRdebug.skipReason = "empty incident";
				return 0;
			}

			// ramp it up
			ZRdebug.scaleFactor *= (0.75f + Rand.Value / 2f);
			ZRdebug.scaleFactor = Tools.Boxed(ZRdebug.scaleFactor, 0f, 1f);
			ZRdebug.incidentSize = Math.Max(1, (int)(ZRdebug.incidentSize * ZRdebug.scaleFactor + 0.5f));

			// success
			ZRdebug.dayStretchFactor = 1f + ZRdebug.incidentSize / 150f;
			ZRdebug.deltaDays = Rand.Range(1.5f * ZRdebug.dayStretchFactor, 4f * ZRdebug.dayStretchFactor);
			info.Update(ZRdebug.deltaDays);
			ZRdebug.skipReason = "-";
			return ZRdebug.incidentSize;
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