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
		IntVec3 destination;
		int slowdownCounter;

		public virtual void InitAction()
		{
			destination = IntVec3.Invalid;
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref destination, "destination", IntVec3.Invalid);
		}

		int SortByTimestamp(PheromoneGrid grid, IntVec3 p1, IntVec3 p2)
		{
			return grid.Get(p2, false).timestamp.CompareTo(grid.Get(p1, false).timestamp);
		}

		int SortByDirection(IntVec3 center, IntVec3 p1, IntVec3 p2)
		{
			return p1.DistanceToSquared(center).CompareTo(p2.DistanceToSquared(center));
		}

		public virtual void TickAction()
		{
			if (slowdownCounter-- > 0) return;
			slowdownCounter = Constants.JOBDRIVER_TICKS_DELAY;

			var zombie = (Zombie)pawn;
			if (zombie.state == ZombieState.Emerging) return;
			var map = zombie.Map;

			if (zombie.Dead || zombie.Destroyed)
			{
				EndJobWith(JobCondition.InterruptForced);
				return;
			}

			if (zombie.state == ZombieState.ShouldDie)
			{
				EndJobWith(JobCondition.InterruptForced);
				zombie.Kill(null);
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

			// handling invalid destinations
			//
			if (destination.x == 0 && destination.z == 0) destination = IntVec3.Invalid;
			if (zombie.HasValidDestination(destination)) return;

			// if we are near targets then attack them
			//
			var target = CanAttack();
			if (target != null)
			{
				zombie.state = ZombieState.Tracking;
				if (Constants.USE_SOUND)
				{
					var info = SoundInfo.InMap(target);
					SoundDef.Named("ZombieHit").PlayOneShot(info);
				}

				var job = new Job(JobDefOf.AttackMelee, target)
				{
					maxNumMeleeAttacks = 9999999,
					expiryInterval = 9999999,
					canBash = true,
					attackDoorIfTargetLost = true,
					ignoreForbidden = false,
				};

				zombie.jobs.StartJob(job, JobCondition.InterruptOptional, null, true, false, null);
				return;
			}

			var basePos = zombie.Position;

			// calculate possible moves, sort by pheromone value and take top 3
			// then choose the one with the lowest zombie count
			// also, emit a circle of timestamps when discovering a pheromone
			// trace so nearby zombies pick it up too (leads to a chain reaction)
			//
			var grid = zombie.Map.GetGrid();
			var possibleTrackingMoves = new List<IntVec3>();
			var currentTicks = Tools.Ticks();
			for (int i = 0; i < 8; i++)
			{
				var pos = basePos + GenAdj.AdjacentCells[i];
				if (currentTicks - grid.Get(pos, false).timestamp < Constants.PHEROMONE_FADEOFF && zombie.HasValidDestination(pos))
					possibleTrackingMoves.Add(pos);
			}
			if (possibleTrackingMoves.Count > 0)
			{
				possibleTrackingMoves.Sort((p1, p2) => SortByTimestamp(grid, p1, p2));
				possibleTrackingMoves = possibleTrackingMoves.Take(Constants.NUMBER_OF_TOP_MOVEMENT_PICKS).ToList();
				possibleTrackingMoves = possibleTrackingMoves.OrderBy(p => grid.Get(p, false).zombieCount).ToList();
				var nextMove = possibleTrackingMoves.First();

				destination = nextMove;
				if (zombie.state == ZombieState.Wandering)
				{
					Tools.ChainReact(grid, zombie.Map, basePos, nextMove);
					if (currentTicks - grid.Get(nextMove, false).timestamp < Constants.PHEROMONE_FADEOFF / 4)
					{
						Tools.CastThoughtBubble(zombie, Constants.BRRAINZ);

						if (Constants.USE_SOUND)
						{
							var info = SoundInfo.InMap(new TargetInfo(basePos, map, false));
							SoundDef.Named("ZombieTracking").PlayOneShot(info);
						}
					}
				}
				zombie.state = ZombieState.Tracking;
			}
			if (destination.IsValid == false) zombie.state = ZombieState.Wandering;

			if (destination.IsValid == false)
			{
				var hour = GenLocalDate.HourOfDay(Find.VisibleMap);

				// check for day/night and dust/dawn
				//
				var moveTowardsCenter = false;
				if (map.areaManager.Home[basePos] == false)
				{
					if (hour < 12) hour += 24;
					if (hour > Constants.HOUR_START_OF_NIGHT && hour < Constants.HOUR_END_OF_NIGHT)
						moveTowardsCenter = true;
					else if (hour >= Constants.HOUR_START_OF_DUSK && hour <= Constants.HOUR_START_OF_NIGHT)
						moveTowardsCenter = Rand.RangeInclusive(hour, Constants.HOUR_START_OF_NIGHT) == Constants.HOUR_START_OF_NIGHT;
					else if (hour >= Constants.HOUR_END_OF_NIGHT && hour <= Constants.HOUR_START_OF_DAWN)
						moveTowardsCenter = Rand.RangeInclusive(Constants.HOUR_END_OF_NIGHT, hour) == Constants.HOUR_END_OF_NIGHT;
				}

				var possibleMoves = new List<IntVec3>();
				for (int i = 0; i < 8; i++)
				{
					var pos = basePos + GenAdj.AdjacentCells[i];
					if (zombie.HasValidDestination(pos))
						possibleMoves.Add(pos);
				}
				if (possibleMoves.Count > 0)
				{
					// during night, zombies drift towards the colonies center
					//
					if (moveTowardsCenter)
					{
						var center = map.TickManager().centerOfInterest;
						possibleMoves.Sort((p1, p2) => SortByDirection(center, p1, p2));
						possibleMoves = possibleMoves.Take(Constants.NUMBER_OF_TOP_MOVEMENT_PICKS).ToList();
						possibleMoves = possibleMoves.OrderBy(p => grid.Get(p, false).zombieCount).ToList();
						destination = possibleMoves.First();
					}
					else
					{
						// otherwise they sometimes stand or walk towards a random direction
						//
						if (Rand.Chance(Constants.STANDING_STILL_CHANCE))
						{
							var n = possibleMoves.Count();
							destination = possibleMoves[Constants.random.Next(n)];
						}
					}
				}
			}

			// if we have a valid destination, go there
			//
			if (destination.IsValid)
				MoveToCell(destination);
		}

		void MoveToCell(LocalTargetInfo dest)
		{
			var zombie = (Zombie)pawn;
			zombie.Map.GetGrid().ChangeZombieCount(dest.Cell, 1);
			zombie.pather.StartPath(dest, PathEndMode.OnCell);
		}

		public override void Notify_PatherArrived()
		{
			base.Notify_PatherArrived();
			destination = IntVec3.Invalid;
		}

		static int[] adjIndex = new int[] { 0, 1, 2, 3, 4, 5, 6, 7 };
		static int prevIndex = 0;
		Thing CanAttack()
		{
			var nextIndex = Constants.random.Next(8);
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
				if (p != null && !(p is Zombie) && p.Dead == false && p.Downed == false)
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