using RimWorld;
using Verse;

namespace ZombieLand
{
	public enum ZombieState
	{
		Emerging,
		Wandering,
		Tracking,
		ShouldDie
	}

	public class PawnKindDef_Zombie : PawnKindDef { }
	public class ThingDef_Zombie : ThingDef { }

	[DefOf]
	public static class ZombieDefOf
	{
		public static FactionDef Zombies;
		public static PawnKindDef Zombie;
	}
}