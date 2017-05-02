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

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.LookValue(ref destination, "destination", IntVec2.Invalid);
		}

		int SortByTimestamp(IntVec3 p1, IntVec3 p2)
		{
			var t1 = Main.phGrid.Get(p1, false).timestamp;
			var t2 = Main.phGrid.Get(p2, false).timestamp;
			return -1 * t1.CompareTo(t2);
		}

		public virtual void TickAction()
		{
			if (slowdownCounter-- > 0) return;
			slowdownCounter = slowdownDelay;

			var zombie = (Zombie)pawn;
			if (zombie.state == ZombieState.Emerging) return;

			if (zombie.Dead || zombie.Destroyed)
			{
				EndJobWith(JobCondition.InterruptForced);
				return;
			}

			if (zombie.state == ZombieState.ShouldDie)
			{
				EndJobWith(JobCondition.InterruptForced);
				zombie.health.Kill(null, null);
				return;
			}

			if (zombie.Downed)
			{
				var injuries = zombie.health.hediffSet.GetHediffs<Hediff_Injury>().ToList();
				foreach (var injury in injuries)
				{
					if (injury.IsOld() == false)
					{
						injury.Heal(injury.Severity + 1f);
						break;
					}
				}
				if (zombie.Downed) return;
			}

			// does not help with the Zs standing still bug
			//
			//	if (zombie.jobs == null || zombie.jobs.curJob == null)
			//		destination = IntVec2.Invalid;

			if (destination.x == 0 && destination.z == 0) destination = IntVec2.Invalid;
			if (HasValidDestination(destination.ToIntVec3)) return;

			// if we are near targets then attack them
			//
			var target = CanAttack();
			if (target != null)
			{
				zombie.state = ZombieState.Tracking;
				if (Main.USE_SOUND)
				{
					var info = SoundInfo.InMap(target);
					SoundDef.Named("ZombieHit").PlayOneShot(info);
				}

				Job job = new Job(JobDefOf.AttackMelee, target);
				zombie.jobs.StartJob(job, JobCondition.InterruptOptional, null, true, false, null);
				return;
			}

			var basePos = zombie.Position;

			// calculate possible moves, sort by pheromone value and take top 3
			// then choose the one with the lowest zombie count
			// also, emit a circle of timestamps when discovering a pheromone
			// trace so nearby zombies pick it up too (leads to a chain reaction)
			//
			var possibleMoves = new List<IntVec3>();
			for (int i = 0; i < 8; i++)
			{
				var pos = basePos + GenAdj.AdjacentCells[i];
				if (HasValidDestination(pos))
					possibleMoves.Add(pos);
			}
			if (possibleMoves.Count > 0)
			{
				possibleMoves.Sort((p1, p2) => SortByTimestamp(p1, p2));
				possibleMoves = possibleMoves.Take(3).ToList();
				possibleMoves = possibleMoves.OrderBy(p => Main.phGrid.Get(p, false).zombieCount).ToList();
				for (int i = 0; i < possibleMoves.Count(); i++)
				{
					var nextMove = possibleMoves[i];
					var cell = Main.phGrid.Get(nextMove, false);
					if (Tools.Ticks() - cell.timestamp < Main.pheromoneFadeoff)
					{
						destination = nextMove.ToIntVec2;
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
						if (Main.USE_SOUND)
						{
							var info = SoundInfo.InMap(new TargetInfo(basePos, pawn.Map, false));
							SoundDef.Named("ZombieTracking").PlayOneShot(info);
						}
						break;
					}
				}
			}
			if (destination.IsInvalid) zombie.state = ZombieState.Wandering;

			// during night, zombies drift towards the colonies center
			//
			if (destination.IsInvalid)
			{
				var hour = GenLocalDate.HourOfDay(Find.VisibleMap);
				if (hour >= 22 || hour <= 5)
				{
					int dx = TickManager.centerOfInterest.x - zombie.Position.x;
					int dz = TickManager.centerOfInterest.z - zombie.Position.z;
					destination = zombie.Position.ToIntVec2;
					if (Math.Abs(dx) > Math.Abs(dz))
						destination.x += Math.Sign(dx);
					else
						destination.z += Math.Sign(dz);

					if (HasValidDestination(destination.ToIntVec3) == false)
						destination = IntVec2.Invalid;
				}
			}

			// still no valid destination? go random adjected cell
			//
			if (destination.IsInvalid)
			{
				var maxDanger = PawnUtility.ResolveMaxDanger(zombie, Danger.Deadly);
				destination = RCellFinder.RandomWanderDestFor(zombie, zombie.Position, 2f, (Pawn thePawn, IntVec3 thePos) => HasValidDestination(thePos), maxDanger).ToIntVec2;
			}

			// if we have a valid destination, go there
			//
			if (destination.IsValid)
			{
				zombie.pather.StartPath(destination.ToIntVec3, PathEndMode.OnCell);
				Main.phGrid.ChangeZombieCount(destination.ToIntVec3, 1);
			}
		}

		public override void Notify_PatherArrived()
		{
			base.Notify_PatherArrived();
			destination = IntVec2.Invalid;
		}

		static int[] adjIndex = new int[] { 0, 1, 2, 3, 4, 5, 6, 7 };
		static int prevIndex = 0;
		Thing CanAttack()
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
			for (int i = 0; i < 8; i++)
			{
				var pos = basePos + GenAdj.AdjacentCells[adjIndex[i]];
				var p = grid.ThingAt<Building_Door>(pos);
				if (p != null)
					return p;
			}
			return null;
		}

		bool HasValidDestination(IntVec3 dest)
		{
			if (dest.IsValid == false) return false;
			return pawn.Map.reachability.CanReach(pawn.Position, dest, PathEndMode.OnCell, TraverseParms.For(TraverseMode.PassDoors));
		}

		public override string GetReport()
		{
			return "Stumbling";
		}

		protected override IEnumerable<Toil> MakeNewToils()
		{
			yield return new Toil()
			{
				initAction = new Action(InitAction),
				tickAction = new Action(TickAction),
				defaultCompleteMode = ToilCompleteMode.Never
			};
		}
	}
}