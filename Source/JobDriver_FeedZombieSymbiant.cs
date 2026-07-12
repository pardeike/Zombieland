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
				&& symbiant.CanAcceptFeed(feed)
				&& pawn.Reserve(feed, job, 1, 1, null, errorOnFailed);
		}

		public override IEnumerable<Toil> MakeNewToils()
		{
			_ = this.FailOnDespawnedOrNull(TargetIndex.A);
			_ = this.FailOnDestroyedOrNull(TargetIndex.B);

			yield return Toils_Goto.GotoThing(TargetIndex.B, PathEndMode.Touch)
				.FailOnDespawnedNullOrForbidden(TargetIndex.B)
				.FailOnSomeonePhysicallyInteracting(TargetIndex.B);
			yield return Toils_Haul.StartCarryThing(TargetIndex.B, false, true);
			yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch)
				.FailOnDespawnedOrNull(TargetIndex.A);

			var feed = Toils_General.Wait(90, TargetIndex.A);
			_ = feed.FailOnDespawnedOrNull(TargetIndex.A);
			_ = feed.FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch);
			_ = feed.WithProgressBarToilDelay(TargetIndex.A);
			yield return feed;

			var finish = ToilMaker.MakeToil("FeedZombieSymbiant");
			finish.initAction = delegate ()
			{
				var symbiant = TargetSymbiant;
				var carried = pawn.carryTracker?.CarriedThing;
				if (symbiant == null || carried == null || symbiant.CanAcceptFeed(carried) == false || symbiant.TryFeed(carried) == false)
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
