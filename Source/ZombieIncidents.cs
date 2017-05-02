using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Harmony;
using RimWorld.Planet;

namespace ZombieLand
{
	public class ZombiesRising : IncidentWorker
	{
		static int spawnRadius = 10;

		public Predicate<IntVec3> SpotValidator(Map map)
		{
			var cellValidator = Tools.ZombieSpawnLocator(map);
			return cell =>
			{
				var count = 0;
				Tools.GetCircle(spawnRadius).Do(vec =>
				{
					if (cellValidator(cell + vec)) count++;
				});
				return count >= 6;
			};
		}

		public override bool TryExecute(IncidentParms parms)
		{
			var map = (Map)parms.target;
			var zombieCount = 40;
			if (GenDate.DaysPassed < 7)
				zombieCount = 20;

			var validator = SpotValidator(map);
			RCellFinder.TryFindRandomSpotJustOutsideColony(TickManager.centerOfInterest, map, null, out IntVec3 spot, validator);
			if (spot.IsValid == false) return false;

			var cellValidator = Tools.ZombieSpawnLocator(map);
			var spawnLocations = Tools.GetCircle(spawnRadius)
				.Select(vec => spot + vec)
				.Where(vec => cellValidator(vec))
				.InRandomOrder()
				.Take(zombieCount)
				.Do(cell => Main.spawnQueue.Enqueue(new TargetInfo(cell, map)));

			var text = "ZombiesRisingNearYourBase".Translate();
			var location = new GlobalTargetInfo(spot, map);
			Find.LetterStack.ReceiveLetter("LetterLabelZombiesRisingNearYourBase".Translate(), text, LetterType.BadUrgent, location);
			return true;
		}
	}
}