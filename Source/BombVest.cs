using HarmonyLib;
using RimWorld;
using Verse;
using Verse.Sound;

namespace ZombieLand
{
	class Explosion
	{
		readonly Map map;
		readonly IntVec3 pos;

		public Explosion(Map map, IntVec3 pos)
		{
			this.map = map;
			this.pos = pos;
		}

		public void Explode()
		{
			var damageDef = new SuicideBombDamage();
			var radius = 1f + 2f * Tools.Difficulty();
			GenExplosion.DoExplosion(pos, map, radius, damageDef, null);

			var r2 = (radius - 1) * (radius - 1);
			map.GetComponent<TickManager>().allZombiesCached
				.DoIf(zombie => zombie.IsTanky && zombie.Position.DistanceToSquared(pos) <= r2, zombie =>
				{
					zombie.hasTankyShield = -1;
					zombie.hasTankySuit = -1;
					if (Tools.Difficulty() <= 3)
						zombie.hasTankyHelmet = -1;
					SoundDefOf.Crunch.PlayOneShot(SoundInfo.InMap(new TargetInfo(zombie.Position, zombie.Map, false), MaintenanceType.None));
				});
		}
	}

	class SuicideBombDamage : DamageDef
	{
		static int ScaledValueBetween(int a, int b)
		{
			return (int)(a + b * Tools.Difficulty() / 5f);
		}

		public SuicideBombDamage()
		{
			var baseDef = CustomDefs.SuicideBomb;
			Traverse.IterateFields(baseDef, this, Traverse.CopyFields);

			defaultDamage = ScaledValueBetween(8, 120);
			var damageFactor = ScaledValueBetween(10, 320);
			buildingDamageFactor = damageFactor;
			plantDamageFactor = damageFactor;
			explosionHeatEnergyPerCell = ScaledValueBetween(8, 128);
		}
	}
}
