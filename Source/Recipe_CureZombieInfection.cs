using RimWorld;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace ZombieLand
{
	public class Recipe_CureZombieInfection : Recipe_Surgery
	{
		private IEnumerable<Hediff_Injury_ZombieBite> GetInfectingBites(Pawn pawn)
		{
			return pawn.health.hediffSet
				.GetHediffs<Hediff_Injury_ZombieBite>()
				.Where(bite =>
				{
					var state = bite.TendDuration.GetInfectionState();
					if (Tools.Difficulty() > 1.5f)
						return state > InfectionState.BittenInfectable && state < InfectionState.Infecting;
					return state > InfectionState.BittenHarmless;
				});
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
			if (extract == null)
				return;

			var bite = GetInfectingBites(pawn).FirstOrDefault(b => b.Part == part);
			if (bite == null)
				return;

			var chance = Rand.RangeInclusive(0, 100);
			var purity = extract.count;
			var failure = chance > purity;
			var catastrophic = chance > purity + (100 - purity) * 3 / 4;

			if (failure)
			{
				if (catastrophic)
				{
					HealthUtility.GiveInjuriesOperationFailureCatastrophic(pawn, part);
					if (!pawn.Dead)
						pawn.Kill(null, null);
					Messages.Message("MessageMedicalOperationFailureFatal".Translate(billDoer.LabelShort, pawn.LabelShort, recipe.LabelCap, billDoer.Named("SURGEON"), pawn.Named("PATIENT")), pawn, MessageTypeDefOf.NegativeHealthEvent, true);
				}
				else
				{
					HealthUtility.GiveInjuriesOperationFailureMinor(pawn, part);
					pawn.needs.mood.thoughts.memories.TryGainMemory(ThoughtDefOf.BotchedMySurgery, billDoer);
					Messages.Message("MessageMedicalOperationFailureMinor".Translate(billDoer.LabelShort, pawn.LabelShort, billDoer.Named("SURGEON"), pawn.Named("PATIENT"), recipe.Named("RECIPE")), pawn, MessageTypeDefOf.NegativeHealthEvent, true);
				}

				return;
			}

			bite.mayBecomeZombieWhenDead = false;
			var tendDuration = bite.TryGetComp<HediffComp_Zombie_TendDuration>();
			tendDuration.ZombieInfector.MakeHarmless();

			_ = TaleRecorder.RecordTale(TaleDefOf.DidSurgery, new object[] { billDoer, pawn });
		}

		public override string GetLabelWhenUsedOn(Pawn pawn, BodyPartRecord part)
		{
			return "CureZombieInfection".Translate();
		}
	}
}
