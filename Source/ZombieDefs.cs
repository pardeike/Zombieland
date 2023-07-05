using RimWorld;
using Verse;

namespace ZombieLand
{
	public enum ZombieState
	{
		Emerging,
		Wandering,
		Tracking,
		ShouldDie,
		Floating
	}

	public class PawnKindDef_Zombie : PawnKindDef { }
	public class ThingDef_Zombie : ThingDef { }

	public class PawnKindDef_ZombieSpitter : PawnKindDef { }
	public class ThingDef_ZombieSpitter : ThingDef { }

	[DefOf]
	public static class ZombieDefOf
	{
		public static FactionDef Zombies;
		public static PawnKindDef Zombie;
		public static PawnKindDef ZombieSpitter;
		public static BodyDef ZombieSpitterBody;
		public static BodyPartDef ZombieSpitterBase;
	}
}
