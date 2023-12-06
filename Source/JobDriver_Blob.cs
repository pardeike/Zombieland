using System;
using System.Collections.Generic;
using Verse.AI;

namespace ZombieLand
{
	public class JobDriver_Blob : JobDriver
	{
		public ZombieBlob blob;

		void InitAction()
		{
			blob = pawn as ZombieBlob;
		}

		void TickAction()
		{
		}

		public override void Notify_PatherArrived()
		{
			base.Notify_PatherArrived();
		}

		public override void Notify_PatherFailed()
		{
			base.Notify_PatherFailed();
		}

		public override string GetReport()
		{
			return "zombie blob";
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
