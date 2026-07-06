using RimWorld;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	public static class StorytellerEventFilters
	{
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
			foreach (var corpse in map.listerThings.AllThings)
			{
				if (IsZombielandCorpse(corpse) == false)
					continue;
				if (corpse.Spawned == false || corpse.PositionHeld.Fogged(map))
					continue;
				total += corpse.MarketValue * corpse.stackCount;
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
