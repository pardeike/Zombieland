using RimWorld;
using System;
using System.Linq;
using Verse;
using Harmony;
using RimWorld.Planet;

namespace ZombieLand
{
	public class ZombiesRising : IncidentWorker
	{
		public Predicate<IntVec3> SpotValidator(Map map)
		{
			var cellValidator = Tools.ZombieSpawnLocator(map);
			return cell =>
			{
				var count = 0;
				var minCount = Constants.MIN_ZOMBIE_SPAWN_CELL_COUNT;
				var vecs = Tools.GetCircle(Constants.SPAWN_INCIDENT_RADIUS).ToList();
				foreach (var vec in vecs)
					if (cellValidator(cell + vec))
					{
						if (++count >= minCount)
							break;
					}
				return count >= minCount;
			};
		}

		public override bool TryExecute(IncidentParms parms)
		{
			var map = (Map)parms.target;
			var zombieCount = Constants.NUMBER_OF_ZOMBIES_IN_INCIDENT;
			if (GenDate.DaysPassed <= 3)
				zombieCount /= 2;

			var validator = SpotValidator(map);
			RCellFinder.TryFindRandomSpotJustOutsideColony(map.TickManager().centerOfInterest, map, null, out IntVec3 spot, validator);
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
						Main.generator.SpawnZombieAt(map, cell);
						zombieCount--;
					});
			}

			var text = "ZombiesRisingNearYourBase".Translate();
			var location = new GlobalTargetInfo(spot, map);
			Find.LetterStack.ReceiveLetter("LetterLabelZombiesRisingNearYourBase".Translate(), text, LetterType.BadUrgent, location);
			return true;
		}
	}
}