using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	public class JobDriver_FixBrokenChainsaw : JobDriver
	{
		private const int TicksDuration = 1000;

		private Chainsaw Chainsaw => (Chainsaw)job.GetTarget(TargetIndex.A).Thing;
		private Thing Components => job.GetTarget(TargetIndex.B).Thing;

		public override bool TryMakePreToilReservations(bool errorOnFailed)
		{
			return pawn.Reserve(Chainsaw, job, 1, -1, null, errorOnFailed) && pawn.Reserve(Components, job, 1, -1, null, errorOnFailed);
		}

		public override IEnumerable<Toil> MakeNewToils()
		{
			_ = this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
			yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch)
				.FailOnDespawnedNullOrForbidden(TargetIndex.B)
				.FailOnSomeonePhysicallyInteracting(TargetIndex.B);
			yield return Toils_Haul.StartCarryThing(TargetIndex.B);
			yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch)
				.FailOnDespawnedOrNull(TargetIndex.A);
			var toil = Toils_General.Wait(TicksDuration, TargetIndex.None);
			_ = toil.FailOnDespawnedOrNull(TargetIndex.A);
			_ = toil.FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch);
			_ = toil.WithEffect(Chainsaw.def.repairEffect, TargetIndex.A, null);
			_ = toil.WithProgressBarToilDelay(TargetIndex.A);
			toil.activeSkill = () => SkillDefOf.Construction;
			yield return toil;
			var toil2 = ToilMaker.MakeToil("MakeNewToils");
			toil2.initAction = delegate ()
			{
				Components.Destroy(DestroyMode.Vanish);
				if (Rand.Value > pawn.GetStatValue(StatDefOf.FixBrokenDownBuildingSuccessChance)) // shameless reuse of stat
				{
					var text = "TextMote_FixBrokenDownBuildingFail".Translate(); // shameless reuse
					MoteMaker.ThrowText((pawn.DrawPos + Chainsaw.DrawPos) / 2f, Map, text, 3.65f);
					return;
				}
				Chainsaw.GetComp<CompBreakable>().Notify_Repaired();
			};
			yield return toil2;
		}
	}
}
