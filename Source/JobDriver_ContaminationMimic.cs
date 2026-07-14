using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace ZombieLand
{
	public class JobDriver_ContaminationMimic : JobDriver
	{
		public Pawn previousVictims;
		public Pawn victim;

		public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

		public override IEnumerable<Toil> MakeNewToils()
		{
			yield return new Toil()
			{
				tickAction = new Action(TickAction),
				defaultCompleteMode = ToilCompleteMode.Never
			};
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_References.Look(ref previousVictims, "previousVictims");
			Scribe_References.Look(ref victim, "victim");
		}

		void TrackVictim()
		{
			victim ??= Map.mapPawns.FreeColonists
				.Where(p => p != pawn && p != previousVictims)
				.OrderBy(p => p.Position.DistanceToSquared(pawn.Position))
				.FirstOrDefault();

			if (victim == null)
				EndJobWith(JobCondition.Succeeded);
			else if (pawn.pather.Moving == false || pawn.IsHashIntervalTick(30) && pawn.pather.Destination.Cell != victim.Position)
				pawn.pather.StartPath(victim.Position, PathEndMode.ClosestTouch);
		}

		void TickAction()
		{
			TrackVictim();

			if (pawn.IsHashIntervalTick(120))
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
			if (victim != null)
			{
				Tools.CastThoughtBubble(pawn, Constants.RAGING);
				previousVictims = victim;
				victim.needs?.mood?.thoughts.memories.TryGainMemory(CustomDefs.ZombieScare, pawn);
				if (RCellFinder.TryFindDirectFleeDestination(pawn.Position, 16f, victim, out var destination))
				{
					var flee = JobMaker.MakeJob(JobDefOf.Flee, destination);
					victim.jobs.ClearQueuedJobs();
					victim.jobs.StartJob(flee, JobCondition.Incompletable, null);
				}
			}
			victim = null;
		}

		public override void Notify_PatherFailed()
		{
			base.Notify_PatherFailed();
			victim = null;
		}
	}
}
