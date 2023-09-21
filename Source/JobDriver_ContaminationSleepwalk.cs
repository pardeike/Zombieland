using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	public class JobDriver_ContaminationSleepwalk : JobDriver
	{
		public Building_Bed bed;
		public int waitUntil = -1;

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

		public override void ExposeData()
		{
			base.ExposeData();
		}

		void InitAction()
		{
			bed = Map.listerBuildings.allBuildingsColonist.OfType<Building_Bed>()
				.Where(bed => bed.CurOccupants.Count() > 0)
				.SafeRandomElement();
			if (bed == null)
				return;
			pawn.pather.StartPath(bed.Position, PathEndMode.ClosestTouch);
		}

		void TickAction()
		{
			// check waitUntil
			if (waitUntil != -1 && Find.TickManager.TicksGame < waitUntil)
				return;

			if (pawn.IsHashIntervalTick(25))
			{
				var fleckDef = FleckDefOf.SleepZ;
				float speed = 0.42f;
				if (pawn.ageTracker.CurLifeStage.developmentalStage == DevelopmentalStage.Baby || pawn.ageTracker.CurLifeStage.developmentalStage == DevelopmentalStage.Newborn)
				{
					fleckDef = FleckDefOf.SleepZ_Tiny;
					speed = 0.25f;
				}
				else if (pawn.ageTracker.CurLifeStage.developmentalStage == DevelopmentalStage.Child)
				{
					fleckDef = FleckDefOf.SleepZ_Small;
					speed = 0.33f;
				}
				FleckMaker.ThrowMetaIcon(pawn.Position, pawn.Map, fleckDef, speed);
			}

			if (bed == null)
			{
				bed = Map.listerBuildings.allBuildingsColonist.OfType<Building_Bed>()
					.Where(newBed => newBed != bed && newBed.CurOccupants.Count() > 0)
					.SafeRandomElement();
				if (bed != null)
					pawn.pather.StartPath(bed.Position, PathEndMode.ClosestTouch);
			}
		}

		public override void Notify_PatherArrived()
		{
			base.Notify_PatherArrived();
			bed?.CurOccupants.Do(p =>
			{
				RestUtility.WakeUp(p);
				if (CellFinderLoose.TryGetRandomCellWith(c => c.Standable(Map), Map, 10, out var intVec))
				{
					var job = JobMaker.MakeJob(JobDefOf.Flee, intVec, pawn);
					p.jobs.ClearQueuedJobs();
					p.jobs.StartJob(job, JobCondition.InterruptOptional);
				}
			});
			bed = null;
			waitUntil = Find.TickManager.TicksGame + 120;
			MoteMaker.MakeSpeechBubble(pawn, JobDriver_GiveSpeech.moteIcon);
		}

		public override void Notify_PatherFailed()
		{
			base.Notify_PatherFailed();
			bed = null;
		}
	}
}
