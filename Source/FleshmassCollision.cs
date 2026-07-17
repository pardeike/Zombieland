using RimWorld;
using System;
using System.Collections.Generic;
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
		static int suicideExplosionDepth;

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

		internal static bool IsSuicideExplosionKill(DamageInfo? dinfo)
		{
			return suicideExplosionDepth > 0 && dinfo?.Instigator?.Faction == null;
		}

		internal static HashSet<Letter> CaptureResponseLetters(DamageInfo? dinfo)
		{
			if (IsZombieFactionKill(dinfo) == false && IsSuicideExplosionKill(dinfo) == false)
				return null;
			return new HashSet<Letter>(Find.LetterStack?.LettersListForReading ?? new List<Letter>());
		}

		internal static void SuppressNewResponseLetters(HashSet<Letter> before)
		{
			if (before == null || Find.LetterStack == null)
				return;

			var expectedLabel = "FleshmassResponseLabel".Translate().ToString();
			var letters = Find.LetterStack.LettersListForReading;
			for (var i = letters.Count - 1; i >= 0; i--)
			{
				var letter = letters[i];
				if (before.Contains(letter) == false
					&& letter?.def == LetterDefOf.ThreatBig
					&& letter.Label == expectedLabel)
					Find.LetterStack.RemoveLetter(letter);
			}
		}

		internal static bool BeginSuicideExplosion(Verse.Explosion explosion)
		{
			if (explosion?.damType is not SuicideBombDamage)
				return false;
			suicideExplosionDepth++;
			return true;
		}

		internal static void EndSuicideExplosion(bool entered)
		{
			if (entered)
				suicideExplosionDepth = Math.Max(0, suicideExplosionDepth - 1);
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
