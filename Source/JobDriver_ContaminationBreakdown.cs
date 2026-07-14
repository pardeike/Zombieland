using RimWorld;
using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace ZombieLand
{
	public class JobDriver_ContaminationBreakdown : JobDriver
	{
		public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

		public override IEnumerable<Toil> MakeNewToils()
		{
			yield return new Toil()
			{
				initAction = new Action(InitAction),
				tickAction = new Action(TickAction),
				defaultCompleteMode = ToilCompleteMode.Never
			};
		}

		void Flee()
		{
			if (RCellFinder.TryFindDirectFleeDestination(pawn.Position, 16f, pawn, out var destination))
				pawn.pather.StartPath(destination, PathEndMode.OnCell);
			else
				EndJobWith(JobCondition.Succeeded);
		}

		void InitAction()
		{
			Flee();
		}

		void TickAction()
		{
			if (pawn.IsHashIntervalTick(240))
			{
				Tools.CastThoughtBubble(pawn, Constants.BRRAINZ);
				if (ZombieAwarenessCues.ShouldPlayZombieActionSound())
				{
					var info = SoundInfo.InMap(pawn);
					CustomDefs.ZombieTracking.PlayOneShot(info);
				}
			}
		}

		public override void Notify_PatherArrived()
		{
			base.Notify_PatherArrived();
			Flee();
		}

		public override void Notify_PatherFailed()
		{
			base.Notify_PatherFailed();
			Flee();
		}
	}
}
