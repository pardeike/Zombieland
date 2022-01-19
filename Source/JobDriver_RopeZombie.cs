using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	public class JobDriver_RopeZombie : JobDriver
	{
		bool ZombieIsRopable()
		{
			var thing = job.GetTarget(TargetIndex.A).Thing;
			if (!(thing is Zombie zombie) || zombie.Destroyed || zombie.Spawned == false)
				return false;
			return zombie.ropedBy == null;
		}

		public override bool TryMakePreToilReservations(bool errorOnFailed)
		{
			var thing = job.GetTarget(TargetIndex.A).Thing;
			if (pawn.CanReach(job.GetTarget(TargetIndex.A), PathEndMode.Touch, Danger.Deadly) == false)
				return false;
			return ZombieIsRopable();
		}

		public override IEnumerable<Toil> MakeNewToils()
		{
			_ = this.FailOnDespawnedOrNull(TargetIndex.A);
			yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
			yield return new Toil
			{
				initAction = delegate ()
				{
					var zombie = job.GetTarget(TargetIndex.A).Thing as Zombie;
					zombie.ropedBy = pawn;
				}
			};
		}
	}
}
