using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	public class JobDriver_ContaminationHallucination : JobDriver
	{
		public IntVec3 destination = IntVec3.Invalid;
		public Vector3 ghostVec;
		public Mote ghost;

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
			ghostVec = pawn.DrawPos + Vector3Utility.FromAngleFlat(Rand.Range(0, 360)) * 3;
			ghost = MoteMaker.MakeStaticMote(ghostVec, Map, CustomDefs.Ghost, 1f, false);
			UpdateDestination();
		}

		void UpdateDestination()
		{
			var cells = GenAdj.AdjacentCellsAround
				.Select(v => pawn.DrawPos + v.ToVector3())
				.OrderBy(v => (v - ghostVec).MagnitudeHorizontalSquared())
				.Select(v => v.ToIntVec3())
				.Where(c => c.WalkableBy(Map, pawn))
				.ToArray();
			if (cells.Length == 0)
				destination = IntVec3.Invalid;
			else
			{
				destination = cells.Last();
				pawn.pather.StartPath(destination, PathEndMode.OnCell);
			}
		}

		void TickAction()
		{
			ghostVec += (pawn.DrawPos - ghostVec) / 50f;
			ghost.exactPosition = ghostVec;
			ghost.Maintain();

			if (pawn.IsHashIntervalTick(30) == false)
				return;
			UpdateDestination();

		}

		public override void Notify_PatherArrived()
		{
			base.Notify_PatherArrived();
			destination = IntVec3.Invalid;
		}

		public override void Notify_PatherFailed()
		{
			base.Notify_PatherFailed();
			destination = IntVec3.Invalid;
		}
	}
}
