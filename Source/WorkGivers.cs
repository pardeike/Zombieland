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
		public override bool Prioritized => true;
		public override int MaxRegionsToScanBeforeGlobalSearch => 4;

		public override Danger MaxPathDanger(Pawn pawn)
		{
			return Danger.None;
		}

		public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
		{
			if (ZombieSettings.Values.corpsesExtractAmount == 0) return Enumerable.Empty<Thing>();
			if (pawn.IsColonist == false) return Enumerable.Empty<Thing>();
			if (pawn.CanDoctor() == false) return Enumerable.Empty<Thing>();

			var map = pawn.Map;
			var pos = pawn.Position;
			var area = map.areaManager.AllAreas.FirstOrDefault(area => area.Label == ZombieSettings.Values.extractZombieArea);

			var tickManager = map.GetComponent<TickManager>();
			return tickManager.allZombieCorpses
				.Where(corpse => corpse.DestroyedOrNull() == false && corpse.Spawned && (area == null || area[corpse.Position]))
				.OrderBy(corpse => corpse.Position.DistanceToSquared(pos)).Take(8); // just consider the nearest 8
		}

		public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
		{
			if (pawn.IsColonist == false || !(t is ZombieCorpse corpse))
				return false;

			if (forced == false && ColonistSettings.Values.ConfigFor(pawn).autoExtractZombieSerum == false)
				return false;

			var map = pawn.Map;
			if (forced == false)
			{
				var area = map.areaManager.AllAreas.FirstOrDefault(area => area.Label == ZombieSettings.Values.extractZombieArea);
				if (area != null && area[t.Position] == false)
					return false;
			}

			if (pawn.CanReach(corpse, PathEndMode.ClosestTouch, forced ? Danger.Deadly : Danger.None) == false)
				return false;

			var result = pawn.CanReserve(corpse, 1, -1, null, forced);
			if (result && forced == false && ZombieSettings.Values.betterZombieAvoidance)
			{
				var tickManager = map.GetComponent<TickManager>();
				if (tickManager != null)
				{
					var avoidGrid = tickManager.avoidGrid;
					var path = pawn.Map.pathFinder.FindPath(pawn.Position, t, pawn, PathEndMode.ClosestTouch);
					var shouldAvoid = path.NodesReversed.Any(cell => avoidGrid.ShouldAvoid(map, cell));
					path.ReleaseToPool();
					if (shouldAvoid)
						return false;
				}
			}
			return result;
		}

		public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
		{
			if (!(t is ZombieCorpse corpse)) return null;
			return JobMaker.MakeJob(CustomDefs.ExtractZombieSerum, corpse);
		}
	}

	public class WorkGiver_DoubleTap : WorkGiver_Scanner
	{
		public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForDef(ThingDefOf.Human);
		public override PathEndMode PathEndMode => PathEndMode.ClosestTouch;
		public override bool Prioritized => true;
		public override int MaxRegionsToScanBeforeGlobalSearch => 4;

		public override Danger MaxPathDanger(Pawn pawn)
		{
			return Danger.None;
		}

		public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
		{
			var pos = pawn.Position;
			return pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse)
				.OfType<Corpse>()
				.Where(corpse =>
				{
					if (corpse.Spawned == false) return false;
					var hediffSet = corpse.InnerPawn?.health?.hediffSet;
					if (hediffSet == null) return false;
					if (hediffSet.GetBrain() == null) return false;
					return hediffSet.HasHediff(CustomDefs.ZombieInfection);
				})
				.OrderBy(corpse => corpse.Position.DistanceToSquared(pos)).Take(8); // just consider the nearest 8
		}

		public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
		{
			if (pawn.IsColonist == false || t is ZombieCorpse)
				return false;

			var corpse = t as Corpse;
			if (corpse.DestroyedOrNull() || corpse.Spawned == false)
				return false;

			if (ZombieSettings.Values.hoursAfterDeathToBecomeZombie == -1)
				return false;

			if (forced == false && ColonistSettings.Values.ConfigFor(pawn).autoDoubleTap == false)
				return false;

			if (corpse.InnerPawn.health.hediffSet.GetBrain() == null)
				return false;

			if (pawn.CanReach(corpse, PathEndMode.ClosestTouch, forced ? Danger.Deadly : Danger.None) == false)
				return false;

			var result = pawn.CanReserve(corpse, 1, -1, null, forced);
			if (result && forced == false && ZombieSettings.Values.betterZombieAvoidance)
			{
				var map = pawn.Map;
				var tickManager = map.GetComponent<TickManager>();
				if (tickManager != null)
				{
					var avoidGrid = tickManager.avoidGrid;
					var path = pawn.Map.pathFinder.FindPath(pawn.Position, t, pawn, PathEndMode.ClosestTouch);
					var shouldAvoid = path.NodesReversed.Any(cell => avoidGrid.ShouldAvoid(map, cell));
					path.ReleaseToPool();
					if (shouldAvoid) return false;
				}
			}
			return result;
		}

		public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
		{
			if (!(t is Corpse corpse))
				return null;
			return JobMaker.MakeJob(CustomDefs.DoubleTap, corpse);
		}
	}
}
