using RimWorld;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	public class JobDriver_Stumble : JobDriver
	{
		IntVec2 destination;
		static Random random = new Random();

		int slowdownCounter;
		static int slowdownDelay = GenTicks.SecondsToTicks(1f);

		public virtual void InitAction()
		{
			destination = IntVec2.Invalid;
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

			if (pawn.Downed == false && HasValidDestination(destination.ToIntVec3) == false)
			{
				var target = CanAttack();
				if (target != null)
				{
					Job job = new Job(JobDefOf.AttackMelee, target);
					pawn.jobs.StartJob(job, JobCondition.InterruptOptional, null, true, false, null);
					return;
				}

				var nextIndex = random.Next(8);
				var c = adjIndex[prevIndex];
				adjIndex[prevIndex] = adjIndex[nextIndex];
				adjIndex[nextIndex] = c;
				prevIndex = nextIndex;

				destination = IntVec2.Invalid;

				long now = Tools.Ticks();
				var zombie = (Zombie)pawn;
				var basePos = pawn.Position;
				var shortestDiff = Main.pheromoneFadeoff;
				for (int i = 0; i < 8; i++)
				{
					var pos = basePos + GenAdj.AdjacentCells[adjIndex[i]];
					var cell = Main.phGrid.Get(pos, false);
					if (cell == null) continue;
					if (cell.vector.IsValid)
					{
						destination = cell.vector;
						zombie.isSniffing = false;
						break;
					}
					var diff = now - cell.timestamp;
					if (diff < shortestDiff)
					{
						shortestDiff = diff;
						destination = pos.ToIntVec2;
						if (zombie.isSniffing == false)
						{
							var baseTimestamp = now - shortestDiff;
							for (int j = 0; j < 8; j++)
							{
								var pos2 = basePos + GenAdj.AdjacentCells[adjIndex[j]];
								var timestamp = baseTimestamp - (int)pos2.DistanceToSquared(destination.ToIntVec3);
								Main.phGrid.Set(pos2, destination, timestamp);
							}
						}
						zombie.isSniffing = true;
					}
				}
				if (destination.IsValid == false) zombie.isSniffing = false;

				/*
				if (rand <= 10)
				{
					var randomTarget = pawn.Map.mapPawns
						.AllPawnsSpawned
						.Where(p => p.GetType() != Zombie.type && p.Position.x > 0 && p.Position.z > 0)
						.Where(p => p.Destroyed == false && p.Downed == false && p.Dead == false)
						.RandomElementByWeight(p => p.IsColonistPlayerControlled ? 10f : p.def == ThingDefOf.Human ? 3f : 1f);

					if (randomTarget != null && HasValidDestination(randomTarget.Position))
						destination = randomTarget.Position;
				}
				*/

				var rand = random.Next(100);
				if (destination.IsValid == false && rand <= 50)
				{
					int dx = Main.centerOfInterest.x - pawn.Position.x;
					int dz = Main.centerOfInterest.z - pawn.Position.z;
					destination = pawn.Position.ToIntVec2;
					if (Math.Abs(dx) > Math.Abs(dz))
						destination.x += Math.Sign(dx);
					else
						destination.z += Math.Sign(dz);

					if (HasValidDestination(destination.ToIntVec3) == false)
						destination = IntVec2.Invalid;
				}

				if (destination.IsValid == false)
				{
					var maxDanger = PawnUtility.ResolveMaxDanger(pawn, Danger.Deadly);
					destination = RCellFinder.RandomWanderDestFor(pawn, pawn.Position, 2f, (Pawn thePawn, IntVec3 thePos) => HasValidDestination(thePos), maxDanger).ToIntVec2;
				}

				if (destination.IsValid)
					pawn.pather.StartPath(destination.ToIntVec3, PathEndMode.OnCell);
			}
		}

		public override void Notify_PatherArrived()
		{
			destination = IntVec2.Invalid;
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
			if (dest.InBounds(pawn.Map) == false) return false;
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