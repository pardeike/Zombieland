using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	public class WorkGiver_ExtractZombieSerum : WorkGiver_Scanner
	{
		public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForDef(CustomDefs.Corpse_Zombie);
		public override PathEndMode PathEndMode => PathEndMode.ClosestTouch;
		public override int LocalRegionsToScanFirst => 4;

		public override Danger MaxPathDanger(Pawn pawn)
		{
			return Danger.None;
		}

		public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
		{
			var map = pawn.Map;
			var tickManager = map.GetComponent<TickManager>();
			return tickManager.allZombieCorpses
				.Cast<Thing>();
		}

		public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
		{
			var corpse = t as ZombieCorpse;
			if (corpse.DestroyedOrNull() || corpse.Spawned == false)
				return false;

			if (pawn.CanReach(corpse, PathEndMode.ClosestTouch, pawn.NormalMaxDanger(), false, TraverseMode.ByPawn) == false)
				return false;

			var result = pawn.CanReserve(corpse, 1, -1, null, forced);
			if (result)
			{
				var location = Tools.ZombiesNearby(pawn, corpse.Position);
				if (location.IsValid)
					result = false;
			}

			return result;
		}

		public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
		{
			var corpse = t as ZombieCorpse;
			if (corpse == null)
				return null;
			return new Job(CustomDefs.ExtractZombieSerum) { targetA = corpse };
		}
	}
}