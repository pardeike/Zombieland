using System;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	public class JobDriver_Spitter : JobDriver
	{
		public IntVec3 destination = IntVec3.Invalid;

		void InitAction()
		{
			destination = IntVec3.Invalid;
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref destination, "destination", IntVec3.Invalid);
		}

		void TickAction()
		{
			var spitter = (ZombieSpitter)pawn;

			if (destination.IsValid)
				return;

			destination = CellFinder.RandomEdgeCell(spitter.Map);
			spitter.pather.StartPath(destination, PathEndMode.OnCell);
		}

		public override void Notify_PatherArrived()
		{
			base.Notify_PatherArrived();
			destination = IntVec3.Invalid;
		}

		public override void Notify_PatherFailed()
		{
			base.Notify_PatherFailed();
			InitAction();
		}

		public override string GetReport()
		{
			return "Spitting";
		}

		public override IEnumerable<Toil> MakeNewToils()
		{
			yield return new Toil()
			{
				initAction = new Action(InitAction),
				tickAction = new Action(TickAction),
				defaultCompleteMode = ToilCompleteMode.Never
			};
		}

		public override bool TryMakePreToilReservations(bool errorOnFailed)
		{
			return true;
		}
	}
}
