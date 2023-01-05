using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	public class JobDriver_ZapZombies : JobDriver
	{
		public override bool TryMakePreToilReservations(bool errorOnFailed)
		{
			return pawn.Reserve(TargetA, job, 1, -1, null, errorOnFailed);
		}

		public override IEnumerable<Toil> MakeNewToils()
		{
			_ = this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
			_ = this.FailOnForbidden(TargetIndex.A);

			AddFailCondition(() =>
			{
				if (TargetA.Thing is not ZombieShocker shocker)
					return true;

				if (shocker.compPowerTrader.PowerNet.batteryComps.Count == 0)
				{
					Messages.Message("ZombieShockerHasNoBattery".Translate(), shocker, MessageTypeDefOf.RejectInput, null, false);
					return true;
				}

				if (shocker.HasValidRoom() == false)
				{
					Messages.Message("ZombieShockerHasNoRoom".Translate(), shocker, MessageTypeDefOf.RejectInput, null, false);
					return true;
				}

				return false;
			});

			yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.InteractionCell);
			yield return Toils_General.Wait(90, TargetIndex.None)
				.FailOnDestroyedNullOrForbidden(TargetIndex.A)
				.FailOnCannotTouch(TargetIndex.A, PathEndMode.InteractionCell)
				.WithProgressBarToilDelay(TargetIndex.A, false, -0.5f);
			yield return new Toil()
			{
				initAction = () =>
				{
					if (TargetA.Thing is ZombieShocker shocker)
						shocker.ReceiveCompSignal("Activate");
				}
			};
		}
	}
}
