using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	public class JobDriver_Stumble : JobDriver
	{
		IntVec3 destination;
		static Random random = new Random();

		int slowdownCounter;
		static int slowdownDelay = GenTicks.SecondsToTicks(1f);

		public static IntVec3 center = IntVec3.Invalid;

		public virtual void InitAction()
		{
			destination = IntVec3.Invalid;
		}

		public virtual void TickAction()
		{
			if (slowdownCounter-- > 0) return;
			slowdownCounter = slowdownDelay;

			if (pawn.Downed)
			{
				var injuries = pawn.health.hediffSet.GetHediffs<Hediff_Injury>().ToList();
				foreach (var injury in injuries)
				{
					if (injury.Part.def == BodyPartDefOf.Brain)
					{
						pawn.health.Kill(new DamageInfo(), injury);
						EndJobWith(JobCondition.Incompletable);
						return;
					}
					if (injury.IsOld() == false)
					{
						injury.Heal(injury.Severity + 1f);
						break;
					}
				}
			}

			if (pawn.Dead || pawn.Destroyed)
			{
				EndJobWith(JobCondition.Incompletable);
				return;
			}

			if (pawn.Downed == false && HasValidDestination(destination) == false)
			{
				var target = CanAttack();
				if (target != null)
				{
					Job job = new Job(JobDefOf.AttackMelee, target);
					pawn.jobs.StartJob(job, JobCondition.InterruptOptional, null, true, false, null);
					return;
				}

				var rand = random.Next(100);
				destination = IntVec3.Invalid;

				if (rand <= 10)
				{
					var randomTarget = Find.VisibleMap.mapPawns
						.AllPawnsSpawned
						.Where(p => p.GetType() != Zombie.type && p.Position.x > 0 && p.Position.z > 0)
						.Where(p => p.Destroyed == false && p.Downed == false && p.Dead == false)
						.RandomElementByWeight(p => p.IsColonistPlayerControlled ? 10f : p.def == ThingDefOf.Human ? 3f : 1f);

					if (randomTarget != null && HasValidDestination(randomTarget.Position))
						destination = randomTarget.Position;
				}

				if (destination.IsValid == false && rand <= 50)
				{
					int dx = center.x - pawn.Position.x;
					int dz = center.z - pawn.Position.z;
					destination = pawn.Position;
					if (Math.Abs(dx) > Math.Abs(dz))
						destination.x += Math.Sign(dx);
					else
						destination.z += Math.Sign(dz);

					if (HasValidDestination(destination) == false)
						destination = IntVec3.Invalid;
				}

				if (destination.IsValid == false)
				{
					var maxDanger = PawnUtility.ResolveMaxDanger(pawn, Danger.Deadly);
					destination = RCellFinder.RandomWanderDestFor(pawn, pawn.Position, 2f, (Pawn thePawn, IntVec3 thePos) => HasValidDestination(thePos), maxDanger);
				}

				if (destination.IsValid)
					pawn.pather.StartPath(destination, PathEndMode.OnCell);
			}
		}

		public override void Notify_PatherArrived()
		{
			destination = IntVec3.Invalid;
		}

		static int[] adjIndex = new int[] { 0, 1, 2, 3, 4, 5, 6, 7 };
		static int prevIndex = 0;
		Pawn CanAttack()
		{
			var nextIndex = random.Next(8);
			var c = adjIndex[prevIndex];
			adjIndex[prevIndex] = adjIndex[nextIndex];
			adjIndex[nextIndex] = c;
			prevIndex = nextIndex;

			var grid = pawn.Map.thingGrid;
			var basePos = pawn.Position;
			for (int i = 0; i < 8; i++)
			{
				var pos = basePos + GenAdj.AdjacentCells[adjIndex[i]];
				var p = grid.ThingAt<Pawn>(pos);
				if (p != null && p.GetType() != Zombie.type && p.Dead == false && p.Downed == false)
					return p;
			}
			return null;
		}

		bool HasValidDestination(IntVec3 dest)
		{
			if (dest.IsValid == false) return false;
			if (dest.InBounds(Find.VisibleMap) == false) return false;
			if (GenGrid.Walkable(dest, pawn.Map) == false) return false;
			return pawn.Map.reachability.CanReach(pawn.Position, dest, PathEndMode.OnCell, TraverseParms.For(TraverseMode.NoPassClosedDoors));
		}

		public override string GetReport()
		{
			return "Stumbling";
		}

		protected override IEnumerable<Toil> MakeNewToils()
		{
			Toil toil = new Toil();
			toil.initAction = new Action(InitAction);
			toil.tickAction = new Action(TickAction);
			toil.defaultCompleteMode = ToilCompleteMode.Never;
			yield return toil;
		}
	}
}