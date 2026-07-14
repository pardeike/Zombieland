using System;
using System.Collections.Generic;
using Verse.AI;

namespace ZombieLand
{
	public class JobDriver_Symbiant : JobDriver
	{
		public ZombieSymbiant symbiant;

		void InitAction()
		{
			symbiant = pawn as ZombieSymbiant;
		}

		void TickAction()
		{
			symbiant?.SymbiantTick();
		}

		public override string GetReport()
		{
			return "zombie symbiant";
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
