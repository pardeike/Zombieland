using RimWorld;
using System;
using Verse;

namespace ZombieLand
{
	internal enum FleshmassZombieCategory
	{
		Ordinary,
		TankyAndSuicide,
		FormerColonistAndSpecial
	}

	internal static class FleshmassCollision
	{
		[ThreadStatic]
		static int silentResponseDepth;

		internal static bool IsFleshFamily(Building building)
		{
			if (ModsConfig.AnomalyActive == false || building == null)
				return false;

			return building.TryGetComp<CompFleshmass>() != null
				|| building.TryGetComp<CompFleshmassBase>() != null;
		}

		internal static bool IsSourcedActiveFlesh(Building building)
		{
			if (building?.def != ThingDefOf.Fleshmass_Active)
				return false;

			var source = building.TryGetComp<CompFleshmass>()?.source;
			return source is Building_FleshmassHeart heart
				&& heart.Spawned
				&& heart.Map == building.Map;
		}

		internal static FleshmassZombieCategory CategoryFor(Zombie zombie)
		{
			if (zombie?.IsTanky == true || zombie?.IsSuicideBomber == true)
				return FleshmassZombieCategory.TankyAndSuicide;

			if (zombie?.wasMapPawnBefore == true || IsSpecial(zombie))
				return FleshmassZombieCategory.FormerColonistAndSpecial;

			return FleshmassZombieCategory.Ordinary;
		}

		internal static bool CategoryEnabled(Zombie zombie, SettingsGroup settings = null)
		{
			settings ??= ZombieSettings.Values;
			return CategoryFor(zombie) switch
			{
				FleshmassZombieCategory.TankyAndSuicide => settings?.tankyAndSuicideZombiesAttackFleshmass ?? true,
				FleshmassZombieCategory.FormerColonistAndSpecial => settings?.formerColonistAndSpecialZombiesAttackFleshmass ?? true,
				_ => settings?.ordinaryZombiesAttackFleshmass ?? true
			};
		}

		internal static bool AllowsDeliberateTarget(Zombie zombie, Building building, bool ordinaryBuildingRulesAllow)
		{
			if (ordinaryBuildingRulesAllow == false)
				return false;
			if (IsFleshFamily(building) == false)
				return true;
			return CategoryEnabled(zombie);
		}

		internal static bool IsZombieFactionKill(DamageInfo? dinfo)
		{
			return dinfo?.Instigator?.Faction?.def == ZombieDefOf.Zombies;
		}

		internal static void NotifyZombieKill(CompFleshmass flesh)
		{
			var source = flesh?.source;
			if (source == null || source.Spawned == false)
				return;
			var grower = source.TryGetComp<CompGrowsFleshmassTendrils>();
			if (grower == null)
				return;
			silentResponseDepth++;
			try
			{
				grower.Notify_FleshmassDestroyedByPlayer(flesh.parent);
			}
			finally
			{
				silentResponseDepth--;
			}
		}

		internal static bool SuppressesResponseLetter(Letter letter)
		{
			return silentResponseDepth > 0
				&& letter?.def == LetterDefOf.ThreatBig
				&& letter.Label == "FleshmassResponseLabel".Translate().ToString();
		}

		internal static bool BeginSuicideExplosion(Verse.Explosion explosion)
		{
			if (explosion?.damType is not SuicideBombDamage)
				return false;
			silentResponseDepth++;
			return true;
		}

		internal static void EndSuicideExplosion(bool entered)
		{
			if (entered)
				silentResponseDepth = Math.Max(0, silentResponseDepth - 1);
		}

		static bool IsSpecial(Zombie zombie)
		{
			return zombie?.isToxicSplasher == true
				|| zombie?.isMiner == true
				|| zombie?.isElectrifier == true
				|| zombie?.isAlbino == true
				|| zombie?.isDarkSlimer == true
				|| zombie?.isHealer == true;
		}
	}
}
