using RimWorld;
using Verse;

namespace ZombieLand
{
	public class ZombieSerumFilterWorker : SpecialThingFilterWorker
	{
		private readonly ThingDef extractDef = ThingDef.Named("ZombieExtract");

		public override bool Matches(Thing t) // true = exclude
		{
			if (t.def == extractDef) return false; // ok
			if (t is Medicine) return false; // ok
			if (!(t is Corpse corpse)) return true; // exclude, need to be corpse
			var pawn = corpse.InnerPawn;
			if (pawn == null || pawn.RaceProps.Animal == false) return true; // exclude if not animal
			var compRottable = t.TryGetComp<CompRottable>();
			var dessicated = compRottable != null && compRottable.Stage == RotStage.Dessicated;
			if (dessicated == false) return true; // exclude non dessicated
			return pawn.BodySize > 0.75f; // exclude too big animals (adult goat is ok)
		}
	}
}
