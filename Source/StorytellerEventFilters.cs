using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	public static class StorytellerEventFilters
	{
		static readonly List<Thing> tmpZombieCorpseWealthThings = new();

		public static bool IsZombielandPawn(Thing thing)
		{
			return thing is Zombie || thing is ZombieSpitter || thing is ZombieSymbiant;
		}

		public static bool IsZombielandAttackTarget(IAttackTarget target)
		{
			return IsZombielandPawn(target?.Thing);
		}

		public static bool IsZombielandCorpse(Thing thing)
		{
			return thing is ZombieCorpse
				|| thing is ZombieSpitterCorpse
				|| thing is Corpse corpse && IsZombielandPawn(corpse.InnerPawn);
		}

		public static bool IsZombielandWealthHolder(IThingHolder holder)
		{
			return holder is ZombieCorpse
				|| holder is ZombieSpitterCorpse
				|| holder is Zombie
				|| holder is ZombieSpitter
				|| holder is ZombieSymbiant;
		}

		public static bool AffectsStoryDanger(Thing thing)
		{
			if (thing is Zombie zombie)
				return zombie.Spawned && zombie.Downed == false && zombie.IsRopedOrConfused == false && IsInHomeArea(zombie);
			if (thing is ZombieSpitter spitter)
				return spitter.Spawned && spitter.Downed == false && IsInHomeArea(spitter);
			return false;
		}

		public static float ZombieCorpseWealth(Map map)
		{
			if (map?.listerThings == null)
				return 0f;

			var total = 0f;
			try
			{
				ThingOwnerUtility.GetAllThingsRecursively(
					map,
					ThingRequest.ForGroup(ThingRequestGroup.HaulableEver),
					tmpZombieCorpseWealthThings,
					allowUnreal: false,
					WealthWatcher.WealthItemsFilter);
				foreach (var thing in tmpZombieCorpseWealthThings)
				{
					if (IsZombielandCorpse(thing) == false)
						continue;
					if (thing.SpawnedOrAnyParentSpawned == false || thing.PositionHeld.Fogged(map))
						continue;
					total += thing.MarketValue * thing.stackCount;
				}
			}
			finally
			{
				tmpZombieCorpseWealthThings.Clear();
			}
			return total;
		}

		static bool IsInHomeArea(Pawn pawn)
		{
			var map = pawn.Map;
			var position = pawn.Position;
			return map != null
				&& position.InBounds(map)
				&& map.areaManager?.Home != null
				&& map.areaManager.Home[position];
		}
	}
}
