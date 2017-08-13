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

	public class ZombiesRising
	{
		[DefOf]
		public static class MissingDifficulty
		{
			public static DifficultyDef Peaceful;
		}

		static float skippingColonistCountCheckAnormality = 0.1f;
		static float skippingLastIncidentTimeCheckAnormality = 0.1f;
		static float goingBeyondMaxZombiesAnormality = 0.25f;

		static int rampUpDaysBase = 20;
		static float minNextDays = 2f;
		static float maxNextDays = 6f;

		public static int ZombiesForNewIncident(Map map)
		{
			// Log.Warning("");

			var tickManager = map.GetComponent<TickManager>();
			var minThreadScale = MissingDifficulty.Peaceful.threatScale;
			var baseThreadScale = DifficultyDefOf.Medium.threatScale;
			var threatScale = GenMath.LerpDouble(minThreadScale, baseThreadScale, 0f, 1f, Find.Storyteller.difficulty.threatScale);
			var info = map.GetComponent<TickManager>().incidentInfo;
			var capableColonists = Tools.CapableColonists(map);
			var daysBeforeZombies = ZombieSettings.Values.daysBeforeZombiesCome;
			var colonists = map.mapPawns.FreeHumanlikesSpawnedOfFaction(Faction.OfPlayer);
			var totalColonistCount = colonists.Count();
			var minimumCapableColonists = (totalColonistCount + 1) / 3;
			var hour = GenLocalDate.HourOfDay(map);
			if (hour < 12) hour += 24;

			// Log.Warning("TickManager.info " + info);
			// Log.Warning("hour " + hour);
			// Log.Warning("minThreadScale " + minThreadScale);
			// Log.Warning("baseThreadScale " + baseThreadScale);
			// Log.Warning("threatScale " + threatScale);
			// Log.Warning("capableColonists " + capableColonists);
			// Log.Warning("daysBeforeZombies " + daysBeforeZombies);
			// Log.Warning("tickManager.GetMaxZombieCount " + tickManager.GetMaxZombieCount());
			// Log.Warning("ZombieSettings.Values.baseNumberOfZombiesinEvent " + ZombieSettings.Values.baseNumberOfZombiesinEvent);
			// Log.Warning("ZombieSettings.Values.maximumNumberOfZombies " + ZombieSettings.Values.maximumNumberOfZombies);
			// Log.Warning("storyteller.difficulty " + Find.Storyteller.difficulty.difficulty);
			// Log.Warning("GenDate.DaysPassedFloat " + GenDate.DaysPassedFloat);
			// Log.Warning("totalColonistCount " + map.mapPawns.FreeHumanlikesSpawnedOfFaction(Faction.OfPlayer).Count());
			// Log.Warning("minimumCapableColonists " + minimumCapableColonists);
			// Log.Warning("ZombieSettings.Values.spawnWhenType " + ZombieSettings.Values.spawnWhenType);

			// zombie free days
			if (GenDate.DaysPassedFloat <= daysBeforeZombies)
			{
				// Log.Warning("No event because " + GenDate.DaysPassedFloat + " <= daysBeforeZombies");
				return 0;
			}

			// outside night period
			if (ZombieSettings.Values.spawnWhenType == SpawnWhenType.WhenDark)
			{
				if (hour < Constants.HOUR_START_OF_NIGHT || hour > Constants.HOUR_END_OF_NIGHT)
				{
					// Log.Warning("No event because " + hour + " is outside night (" + Constants.HOUR_START_OF_NIGHT + " - " + Constants.HOUR_END_OF_NIGHT + ")");
					return 0;
				}
			}

			// too few capable colonists (only in difficulty lower than Intense)
			if (Find.Storyteller.difficulty.difficulty < DifficultyDefOf.Hard.difficulty)
			{
				if (Rand.Chance(skippingColonistCountCheckAnormality) == false)
				{
					if (capableColonists <= minimumCapableColonists)
					{
						// Log.Warning("No event because capableColonists <=" + minimumCapableColonists);
						return 0;
					}
				}
				else
				{
					// Log.Warning("Anormality: skipping colonist count check");
				}
			}

			// not yet time for next incident
			if (Rand.Chance(skippingLastIncidentTimeCheckAnormality) == false)
			{
				if (GenTicks.TicksAbs < info.NextIncident)
				{
					// Log.Warning("No event because " + GenTicks.TicksAbs + " < nextIncident (" + info.NextIncident + ")");
					return 0;
				}
			}
			else
			{
				// Log.Warning("Anormality: skipping last incident time check");
			}

			// too little new zombies
			var currentZombieCount = map.GetComponent<TickManager>().AllZombies().Count();
			var maxTotalZombies = tickManager.GetMaxZombieCount();
			// Log.Warning("currentZombieCount " + currentZombieCount);
			// Log.Warning("maxTotalZombies " + maxTotalZombies);
			if (Rand.Chance(goingBeyondMaxZombiesAnormality) && Find.Storyteller.difficulty.allowBigThreats)
			{
				var extendedCount = ZombieSettings.Values.maximumNumberOfZombies - maxTotalZombies;
				if (extendedCount > 0)
				{
					extendedCount = Rand.RangeInclusive(0, extendedCount);
					// Log.Warning("Anormality: going beyond max " + maxTotalZombies + " zombies to " + (maxTotalZombies + extendedCount));
					maxTotalZombies += extendedCount;
				}
			}
			var maxAdditionalZombies = Math.Max(0, maxTotalZombies - currentZombieCount);
			var calculatedZombies = capableColonists * ZombieSettings.Values.baseNumberOfZombiesinEvent;
			var incidentSize = Math.Min(maxAdditionalZombies, calculatedZombies);
			// Log.Warning("maxAdditionalZombies " + maxAdditionalZombies);
			// Log.Warning("calculatedZombies " + calculatedZombies);
			// Log.Warning("incidentSize (base) " + incidentSize);
			if (incidentSize == 0)
			{
				// Log.Warning("No event because incidentSize == 0");
				return 0;
			}

			// ramp it up
			var rampUpDays = Math.Max(1, rampUpDaysBase * (1f - threatScale));
			// Log.Warning("rampUpDays " + rampUpDays);
			var scaleFactor = GenMath.LerpDouble(daysBeforeZombies, daysBeforeZombies + rampUpDays, 0.1f, 1f, GenDate.DaysPassedFloat);
			// Log.Warning("scaleFactor1 " + scaleFactor);
			scaleFactor *= (0.75f + Rand.Value / 2f);
			// Log.Warning("scaleFactor2 " + scaleFactor);
			scaleFactor = Tools.Boxed(scaleFactor, 0f, 1f);
			// Log.Warning("scaleFactor4 " + scaleFactor);
			incidentSize = Math.Max(1, (int)(incidentSize * scaleFactor + 0.5f));

			// success
			var dayStretchFactor = GenMath.LerpDouble(1, 500, 1f, 5f, incidentSize);
			var dMin = minNextDays * dayStretchFactor;
			var dMax = maxNextDays * dayStretchFactor;
			var deltaDays = Rand.Range(dMin, dMax);
			info.Update(deltaDays);

			// Log.Warning("deltaDays = " + deltaDays);
			// Log.Warning("final incidentSize = " + incidentSize);

			return incidentSize;
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
			Find.LetterStack.ReceiveLetter(headline, text, LetterDefOf.BadUrgent, location);

			if (Constants.USE_SOUND)
				SoundDef.Named("ZombiesRising").PlayOneShotOnCamera(null);
			return true;
		}
	}
}