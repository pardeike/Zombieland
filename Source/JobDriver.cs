using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
using Verse.Sound;

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

		int SortByTimestamp(IntVec3 p1, IntVec3 p2)
		{
			var c1 = Main.phGrid.Get(p1, false);
			var t1 = c1 == null ? 0 : c1.timestamp;

			var c2 = Main.phGrid.Get(p2, false);
			var t2 = c2 == null ? 0 : c2.timestamp;

			return -1 * t1.CompareTo(t2);
		}

		int SortByZombieCount(IntVec3 p1, IntVec3 p2)
		{
			var c1 = Main.phGrid.Get(p1, false);
			var z1 = c1 == null ? 0 : c1.zombieCount;

			var c2 = Main.phGrid.Get(p2, false);
			var z2 = c2 == null ? 0 : c2.zombieCount;

			return z1.CompareTo(z2);
		}

		public virtual void TickAction()
		{
			if (slowdownCounter-- > 0) return;
			slowdownCounter = slowdownDelay;

			var zombie = (Zombie)pawn;
			if (zombie.state == ZombieState.Emerging) return;

			if (zombie.Downed)
			{
				var injuries = zombie.health.hediffSet.GetHediffs<Hediff_Injury>().ToList();
				foreach (var injury in injuries)
				{
					if (injury.Part.def == BodyPartDefOf.Brain)
					{
						zombie.health.Kill(new DamageInfo(), injury);
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

			if (zombie.Dead || zombie.Destroyed)
			{
				EndJobWith(JobCondition.Incompletable);
				return;
			}

			if (zombie.Downed == false && HasValidDestination(destination.ToIntVec3) == false)
			{
				var target = CanAttack();
				if (target != null)
				{
					zombie.state = ZombieState.Tracking;
					var info = SoundInfo.InMap(target);
					SoundDef.Named("ZombieHit").PlayOneShot(info);

					Job job = new Job(JobDefOf.AttackMelee, target);
					zombie.jobs.StartJob(job, JobCondition.InterruptOptional, null, true, false, null);
					return;
				}

				destination = IntVec2.Invalid;
				var basePos = zombie.Position;

				var nextIndex = random.Next(8);
				var c = adjIndex[prevIndex];
				adjIndex[prevIndex] = adjIndex[nextIndex];
				adjIndex[nextIndex] = c;
				prevIndex = nextIndex;

				var possibleMoves = new List<IntVec3>();
				for (int i = 0; i < 8; i++)
				{
					var pos = basePos + GenAdj.AdjacentCells[adjIndex[i]];
					if (HasValidDestination(pos))
						possibleMoves.Add(pos);
				}
				if (possibleMoves.Count > 0)
				{
					possibleMoves.Sort((p1, p2) => SortByTimestamp(p1, p2));
					if (possibleMoves.Count > 3)
						possibleMoves = possibleMoves.GetRange(0, 3);
					possibleMoves.Sort((p1, p2) => SortByZombieCount(p1, p2));
					var nextMove = possibleMoves.First();

					destination = nextMove.ToIntVec2;
					var cell = Main.phGrid.Get(nextMove, false) ?? Pheromone.empty;
					if (Tools.Ticks() - cell.timestamp < Main.pheromoneFadeoff)
					{
						if (zombie.state == ZombieState.Wandering)
						{
							var baseTimestamp = cell.timestamp;
							for (int j = 0; j < 8; j++)
							{
								var pos2 = basePos + GenAdj.AdjacentCells[j];
								var timestamp = baseTimestamp - (int)pos2.DistanceToSquared(destination.ToIntVec3);
								Main.phGrid.Set(pos2, destination, timestamp);
							}
						}
						zombie.state = ZombieState.Tracking;
						var info = SoundInfo.InMap(new TargetInfo(basePos, pawn.Map, false));
						SoundDef.Named("ZombieTracking").PlayOneShot(info);
					}
				}
				if (destination.IsInvalid) zombie.state = ZombieState.Wandering;

				/*
				if (rand <= 10)
				{
					var randomTarget = zombie.Map.mapPawns
						.AllPawnsSpawned
						.Where(p => p.GetType() != Zombie.type && p.Position.x > 0 && p.Position.z > 0)
						.Where(p => p.Destroyed == false && p.Downed == false && p.Dead == false)
						.RandomElementByWeight(p => p.IsColonistPlayerControlled ? 10f : p.def == ThingDefOf.Human ? 3f : 1f);

					if (randomTarget != null && HasValidDestination(randomTarget.Position))
						destination = randomTarget.Position;
				}
				*/

				if (destination.IsInvalid && random.Next(100) <= 50)
				{
					int dx = Main.centerOfInterest.x - zombie.Position.x;
					int dz = Main.centerOfInterest.z - zombie.Position.z;
					destination = zombie.Position.ToIntVec2;
					if (Math.Abs(dx) > Math.Abs(dz))
						destination.x += Math.Sign(dx);
					else
						destination.z += Math.Sign(dz);

					if (HasValidDestination(destination.ToIntVec3) == false)
						destination = IntVec2.Invalid;
				}

				if (destination.IsInvalid)
				{
					var maxDanger = PawnUtility.ResolveMaxDanger(zombie, Danger.Deadly);
					destination = RCellFinder.RandomWanderDestFor(zombie, zombie.Position, 2f, (Pawn thePawn, IntVec3 thePos) => HasValidDestination(thePos), maxDanger).ToIntVec2;
				}

				if (destination.IsValid)
				{
					zombie.pather.StartPath(destination.ToIntVec3, PathEndMode.OnCell);
					Main.phGrid.ChangeZombieCount(destination.ToIntVec3, 1);
				}
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