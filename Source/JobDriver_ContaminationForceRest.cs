using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	public class JobDriver_ContaminationForceRest : JobDriver
	{
		public override bool TryMakePreToilReservations(bool errorOnFailed) => true;

		public override IEnumerable<Toil> MakeNewToils()
		{
			var toil = ToilMaker.MakeToil("MakeNewToils");
			toil.socialMode = RandomSocialMode.Off;
			toil.defaultCompleteMode = ToilCompleteMode.Never;
			toil.handlingFacing = true;
			yield return toil;
		}

		public override void Notify_Starting()
		{
			base.Notify_Starting();
			pawn.pather.StopDead();
			pawn.Rotation = Rot4.South;
		}

		public override void ExposeData()
		{
			base.ExposeData();
		}

		void InitAction()
		{
		}

		void TickAction()
		{

		}
	}
}
