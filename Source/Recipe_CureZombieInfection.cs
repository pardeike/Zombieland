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
					return state == InfectionState.BittenInfectable && state < InfectionState.Infecting;
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
			var serum = ingredients.First();
			var purity = serum.CostListAdjusted().First(d => d.thingDef.defName == "ZombieExtract").count;

			var chance = Rand.RangeInclusive(0, 100);
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
					Messages.Message("MessageMedicalOperationFailureMinor".Translate(billDoer.LabelShort, pawn.LabelShort, billDoer.Named("SURGEON"), pawn.Named("PATIENT")), pawn, MessageTypeDefOf.NegativeHealthEvent, true);
					HealthUtility.GiveInjuriesOperationFailureMinor(pawn, part);
					pawn.needs.mood.thoughts.memories.TryGainMemory(ThoughtDefOf.BotchedMySurgery, billDoer);
				}

				return;
			}

			var bite = GetInfectingBites(pawn).First(b => b.Part == part);
			bite.mayBecomeZombieWhenDead = false;
			var tendDuration = bite.TryGetComp<HediffComp_Zombie_TendDuration>();
			tendDuration.ZombieInfector.MakeHarmless();

			_ = TaleRecorder.RecordTale(TaleDefOf.DidSurgery, new object[] { billDoer, pawn });
		}

		public override string GetLabelWhenUsedOn(Pawn pawn, BodyPartRecord part)
		{
			return "CuringZombieInfection".Translate();
		}
	}
}