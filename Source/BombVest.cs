using Harmony;
using RimWorld;
using Verse;

namespace ZombieLand
{
	public class BombVest : Apparel { }

	[DefOf]
	public static class DamageDefOf
	{
		public static DamageDef SuicideBomb;
	}

	[StaticConstructorOnStartup]
	class Explosion
	{
		Map map;
		IntVec3 pos;

		public Explosion(Map map, IntVec3 pos)
		{
			this.map = map;
			this.pos = pos;
		}

		public void Explode()
		{
			var damageDef = new SuicideBombDamage();
			var radius = 1f + Find.Storyteller.difficulty.difficulty;
			GenExplosion.DoExplosion(pos, map, radius, damageDef, null, null, null, null, null, 1f, 1, false, null, 0f, 1);
		}
	}

	class SuicideBombDamage : DamageDef
	{
		static int ScaledValueBetween(int a, int b)
		{
			var n = Find.Storyteller.difficulty.difficulty;
			return (int)GenMath.LerpDouble(0, 5, a, b, n);
		}

		public SuicideBombDamage()
		{
			var baseDef = DamageDefOf.SuicideBomb;
			Traverse.IterateFields(baseDef, this, (from, to) => { to.SetValue(from.GetValue()); });

			explosionDamage = ScaledValueBetween(8, 120);
			explosionBuildingDamageFactor = ScaledValueBetween(10, 320);
			explosionHeatEnergyPerCell = ScaledValueBetween(8, 128);
		}
	}
}