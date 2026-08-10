using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	public class JobDriver_FeedZombieSymbiant : JobDriver
	{
		ZombieSymbiant TargetSymbiant => job.GetTarget(TargetIndex.A).Thing as ZombieSymbiant;
		Thing Feed => job.GetTarget(TargetIndex.B).Thing;
		LocalTargetInfo InteractionCell => job.GetTarget(TargetIndex.C);
		bool InteractionCellValid => InteractionCell.IsValid && TargetSymbiant?.ContainsCell(InteractionCell.Cell) == true;

		public override string GetReport()
		{
			return "FeedingZombieSymbiant".Translate();
		}

		public override bool TryMakePreToilReservations(bool errorOnFailed)
		{
			var symbiant = TargetSymbiant;
			var feed = Feed;
			return symbiant != null
				&& feed != null
				&& InteractionCellValid
				&& symbiant.CanAcceptFeed(feed)
				&& pawn.Reserve(feed, job, 1, 1, null, errorOnFailed);
		}

		public override IEnumerable<Toil> MakeNewToils()
		{
			_ = this.FailOnDespawnedOrNull(TargetIndex.A);
			_ = this.FailOnDestroyedOrNull(TargetIndex.B);
			_ = this.FailOn(() => InteractionCellValid == false);

			yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch)
				.FailOnDespawnedNullOrForbidden(TargetIndex.B)
				.FailOnSomeonePhysicallyInteracting(TargetIndex.B);
			yield return Toils_Haul.StartCarryThing(TargetIndex.B, false, true);
			yield return Toils_Goto.GotoCell(TargetIndex.C, PathEndMode.OnCell)
				.FailOnDespawnedOrNull(TargetIndex.A);

			var feed = Toils_General.Wait(90, TargetIndex.C);
			_ = feed.FailOnDespawnedOrNull(TargetIndex.A);
			_ = feed.FailOnCannotTouch(TargetIndex.C, PathEndMode.OnCell);
			_ = feed.WithProgressBarToilDelay(TargetIndex.C);
			yield return feed;

			var finish = ToilMaker.MakeToil("FeedZombieSymbiant");
			finish.initAction = delegate ()
			{
				var symbiant = TargetSymbiant;
				var carried = pawn.carryTracker?.CarriedThing;
				if (symbiant == null
					|| InteractionCellValid == false
					|| carried == null
					|| symbiant.CanAcceptFeed(carried) == false
					|| symbiant.TryFeed(carried) == false)
				{
					pawn.jobs.EndCurrentJob(JobCondition.Incompletable, true);
					return;
				}
				pawn.jobs.EndCurrentJob(JobCondition.Succeeded, true);
			};
			yield return finish;
		}
	}
}
