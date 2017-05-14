using System.Collections.Generic;
using Verse;
using RimWorld;

namespace ZombieLand
{
	class ColonyEvaluation
	{
		const float PreIndustrialWeaponMultiplier = 0.25f;
		const float PointsPerColonist = 99;
		const float PointsPer1000ArmouryWealth = 2.5f;

		static float GetMapArmouryPoints(Map map)
		{
			float result = 0f;

			var armourlist = map.listerThings.ThingsInGroup(ThingRequestGroup.Apparel);
			for (int a = 0; a < armourlist.Count; a++)
			{
				Thing thing = armourlist[a];
				if (!thing.Position.Fogged(map))
				{
					if (thing.def.techLevel >= TechLevel.Industrial)
						result += thing.MarketValue;
				}
			}

			var weaponlist = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon);
			for (int w = 0; w < weaponlist.Count; w++)
			{
				Thing thing = weaponlist[w];
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
			float result = 0f;
			if (dude.equipment != null)
			{
				foreach (Thing equipment in dude.equipment.AllEquipment)
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
				List<Apparel> wornApparel = dude.apparel.WornApparel;
				for (int j = 0; j < wornApparel.Count; j++)
				{
					if (wornApparel[j].def.techLevel >= TechLevel.Industrial)
						result += wornApparel[j].MarketValue;
				}
			}
			return result;
		}

		public static void GetColonistArmouryPoints(IEnumerable<Pawn> colonists, Map map, out float colonistPoints, out float armouryPoints)
		{
			float colonistPointTally = 0f;
			float armouryWealthTally = 0f;

			foreach (Pawn dude in colonists)
			{
				if (dude.story.WorkTagIsDisabled(WorkTags.Violent)) continue; // Non-violent colonists are exempt

				if (dude.health.capacities.GetEfficiency(PawnCapacityDefOf.Moving) < 0.15) continue; // Colonists with extremely poor movement are exempt

				float battlescore = 0.5f * dude.health.capacities.GetEfficiency(PawnCapacityDefOf.Consciousness); // Half comes from consciousness
				battlescore += 0.5f * dude.health.capacities.GetEfficiency(PawnCapacityDefOf.Sight); // Half comes from sight
				battlescore *= dude.health.capacities.GetEfficiency(PawnCapacityDefOf.Manipulation); // Multiplied by manipulation, should give 1.0 for normal healthy colonist

				if (battlescore < 0.2f) continue; // This pawn is too useless to be counted

				if (battlescore > 1.0f) battlescore = 1.0f; // To not penalise having bionic limbs.
				battlescore *= PointsPerColonist;

				colonistPointTally += battlescore;

				armouryWealthTally += GetDudeArmouryPoints(dude);
			}

			armouryWealthTally += GetMapArmouryPoints(map);

			armouryPoints = armouryWealthTally / 1000f * PointsPer1000ArmouryWealth;
			colonistPoints = colonistPointTally;
		}
	}
}