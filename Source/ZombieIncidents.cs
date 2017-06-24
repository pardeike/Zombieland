using RimWorld;
using System;
using System.Linq;
using Verse;
using Harmony;
using RimWorld.Planet;
using Verse.Sound;

namespace ZombieLand
{
	// Constants.MIN_ZOMBIE_SPAWN_CELL_COUNT

	public class ZombiesRisingSmall : ZombiesRising
	{
		public ZombiesRisingSmall() : base()
		{
			zombieCountFactor = 0.5f;
		}
	}

	public class ZombiesRisingLarge : ZombiesRising
	{
		public ZombiesRisingLarge() : base()
		{
			zombieCountFactor = 1f;
		}
	}

	public class ZombiesRising : IncidentWorker
	{
		internal float zombieCountFactor;

		public Predicate<IntVec3> SpotValidator(Map map)
		{
			var cellValidator = Tools.ZombieSpawnLocator(map);
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

		public override bool TryExecute(IncidentParms parms)
		{
			if (GenDate.DaysPassedFloat < ZombieSettings.Values.daysBeforeZombiesCome) return false;

			var map = (Map)parms.target;
			var zombieCount = (int)(zombieCountFactor * ZombieSettings.Values.baseNumberOfZombiesinEvent) + 1;
			zombieCount *= Math.Max(1, map.mapPawns.FreeColonists.Count());

			var spotValidator = SpotValidator(map);

			IntVec3 spot = IntVec3.Invalid;
			string headline = "";
			string text = "";
			for (int counter = 1; counter <= 10; counter++)
			{
				if (ZombieSettings.Values.spawnHowType == SpawnHowType.AllOverTheMap)
				{
					RCellFinder.TryFindRandomSpotJustOutsideColony(Tools.CenterOfInterest(map), map, null, out spot, spotValidator);
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

			var cellValidator = Tools.ZombieSpawnLocator(map);
			while (zombieCount > 0)
			{
				Tools.GetCircle(Constants.SPAWN_INCIDENT_RADIUS)
					.Select(vec => spot + vec)
					.Where(vec => cellValidator(vec))
					.InRandomOrder()
					.Take(zombieCount)
					.Do(cell =>
					{
						Tools.generator.SpawnZombieAt(map, cell);
						zombieCount--;
					});
			}

			var location = new GlobalTargetInfo(spot, map);
			Find.LetterStack.ReceiveLetter(headline, text, LetterDefOf.BadUrgent, location);

			SoundDef.Named("ZombiesRising").PlayOneShotOnCamera(null);
			return true;
		}
	}
}