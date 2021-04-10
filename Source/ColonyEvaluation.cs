using RimWorld;
using System.Collections.Generic;
using Verse;

namespace ZombieLand
{
	class ColonyEvaluation
	{
		const float PreIndustrialWeaponMultiplier = 0.25f;
		const float PointsPerColonist = 150;
		const float wornArmouryMultiplier = 10;
		const float armourFactor = 1f / 60;

		static float GetMapArmouryPoints(Map map)
		{
			var result = 0f;

			var armourlist = map.listerThings.ThingsInGroup(ThingRequestGroup.Apparel);
			for (var a = 0; a < armourlist.Count; a++)
			{
				var thing = armourlist[a];
				if (!thing.Position.Fogged(map))
				{
					if (thing.def.techLevel >= TechLevel.Industrial)
						result += thing.MarketValue;
				}
			}

			var weaponlist = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon);
			for (var w = 0; w < weaponlist.Count; w++)
			{
				var thing = weaponlist[w];
				if (!thing.Position.Fogged(map))
				{
					if (thing.def.techLevel >= TechLevel.Industrial)
						result += thing.MarketValue;
					else
						result += thing.MarketValue * PreIndustrialWeaponMultiplier;
				}
			}

			return result;
		}

		static float GetDudeArmouryPoints(Pawn dude)
		{
			var result = 0f;
			if (dude.equipment != null)
			{
				foreach (var equipment in dude.equipment.AllEquipmentListForReading)
				{
					if (equipment.def.IsRangedWeapon || equipment.def.IsMeleeWeapon)
					{
						if (equipment.def.techLevel >= TechLevel.Industrial)
							result += equipment.MarketValue;
						else
							result += equipment.MarketValue * PreIndustrialWeaponMultiplier;
					}
				}
			}
			if (dude.apparel != null)
			{
				var wornApparel = dude.apparel.WornApparel;
				for (var j = 0; j < wornApparel.Count; j++)
				{
					if (wornApparel[j].def.techLevel >= TechLevel.Industrial)
						result += wornApparel[j].MarketValue;
				}
			}
			return result;
		}

		public static void GetColonistArmouryPoints(IEnumerable<Pawn> colonists, Map map, out float colonistPoints, out float armouryPoints)
		{
			var colonistPointTally = 0f;
			var armouryWealthTally = 0f;

			foreach (var colonist in colonists)
			{
				if (colonist.WorkTagIsDisabled(WorkTags.Violent)) continue; // Non-violent colonists are exempt

				if (colonist.health.capacities.GetLevel(PawnCapacityDefOf.Moving) < 0.15) continue; // Colonists with extremely poor movement are exempt

				var battlescore = 0.5f * colonist.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness); // Half comes from consciousness
				battlescore += 0.5f * colonist.health.capacities.GetLevel(PawnCapacityDefOf.Sight); // Half comes from sight
				battlescore *= colonist.health.capacities.GetLevel(PawnCapacityDefOf.Manipulation); // Multiplied by manipulation, should give 1.0 for normal healthy colonist

				if (battlescore < 0.2f) continue; // This pawn is too useless to be counted
				if (battlescore > 1.0f) battlescore = 1.0f; // To not penalise having bionic limbs.
				colonistPointTally += battlescore;

				armouryWealthTally += GetDudeArmouryPoints(colonist) * wornArmouryMultiplier;
			}

			armouryWealthTally += GetMapArmouryPoints(map);

			armouryPoints = armouryWealthTally * armourFactor;
			colonistPoints = colonistPointTally * PointsPerColonist;
		}
	}
}
