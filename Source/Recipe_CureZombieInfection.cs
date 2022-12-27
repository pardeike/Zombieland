using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace ZombieLand
{
	public class Recipe_CureZombieInfection : Recipe_Surgery
	{
		private List<Hediff_Injury_ZombieBite> tmpHediffInjuryZombieBite = new();

		private bool BiteIsCurable(Hediff_Injury_ZombieBite bite)
		{
			var state = bite.TendDuration.GetInfectionState();
			if (state < InfectionState.BittenInfectable || state > InfectionState.Infecting)
				return false;
			return state == InfectionState.BittenInfectable || Tools.Difficulty() <= 1.5f;
		}

		private IEnumerable<Hediff_Injury_ZombieBite> GetInfectingBites(Pawn pawn)
		{
			tmpHediffInjuryZombieBite.Clear();
			pawn.health.hediffSet.GetHediffs(ref tmpHediffInjuryZombieBite);
			return tmpHediffInjuryZombieBite.Where(BiteIsCurable);
		}

		public override IEnumerable<BodyPartRecord> GetPartsToApplyOn(Pawn pawn, RecipeDef recipe)
		{
			return pawn.health.hediffSet
				.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Undefined, null, null)
				.Intersect(GetInfectingBites(pawn).Select(bite => bite.Part));
		}

		public override void ApplyOnPawn(Pawn pawn, BodyPartRecord part, Pawn billDoer, List<Thing> ingredients, Bill bill)
		{
			if (pawn.DestroyedOrNull() || pawn.Dead || pawn.Map != billDoer.Map || pawn.IsInAnyStorage())
				return;

			var serum = ingredients.FirstOrDefault();
			if (serum == null)
				return;

			var extract = serum.CostListAdjusted().FirstOrDefault(d => d.thingDef.defName == "ZombieExtract");
			if (extract == null && serum.def.defName != "ZombieSerumSimple")
				return;

			var bite = GetInfectingBites(pawn).FirstOrDefault(b => b.Part == part);
			if (bite == null)
				return;

			var chance = Rand.RangeInclusive(0, 100);
			var purity = serum.def.defName == "ZombieSerumSimple" ? 100 : extract.count;
			var failure = chance > purity;
			if (failure)
			{
				HealthUtility.GiveRandomSurgeryInjuries(pawn, 65, part);
				pawn.needs.mood.thoughts.memories.TryGainMemory(ThoughtDefOf.BotchedMySurgery, billDoer);
				Messages.Message("MessageMedicalOperationFailureMinor".Translate(billDoer.LabelShort, pawn.LabelShort, billDoer.Named("SURGEON"), pawn.Named("PATIENT"), recipe.Named("RECIPE")), pawn, MessageTypeDefOf.NegativeHealthEvent, true);
				return;
			}

			Log.Warning($"bite={bite}");

			bite.mayBecomeZombieWhenDead = false;
			var tendDuration = bite.TryGetComp<HediffComp_Zombie_TendDuration>();
			Log.Warning($"tendDuration={tendDuration}");

			tendDuration.ZombieInfector.MakeHarmless();

			_ = TaleRecorder.RecordTale(TaleDefOf.DidSurgery, new object[] { billDoer, pawn });
		}

		public override string GetLabelWhenUsedOn(Pawn pawn, BodyPartRecord part)
		{
			return "CureZombieInfection".Translate();
		}
	}
}
