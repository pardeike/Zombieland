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
		public IntVec3 destination;

		public Thing eatTarget;
		public Pawn lastEatTarget;
		public IntVec3 lastEatTargetPosition;
		public int eatDelayCounter;
		public int eatDelay;
		public int nextDestinationValidationTick;
		public readonly List<IntVec3> adjacentMoveBuffer = new(8);

		void InitAction()
		{
			destination = IntVec3.Invalid;
			lastEatTargetPosition = IntVec3.Invalid;
			nextDestinationValidationTick = 0;
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref destination, "destination", IntVec3.Invalid);
			Scribe_References.Look(ref eatTarget, "eatTarget");
			if (Scribe.mode != LoadSaveMode.Saving)
				Scribe_References.Look(ref lastEatTarget, "lastEatTarget");
			Scribe_Values.Look(ref lastEatTargetPosition, "lastEatTargetPosition", IntVec3.Invalid);
			Scribe_Values.Look(ref eatDelayCounter, "eatDelayCounter");

			// previous versions of Zombieland stored the inner pawn of a corpse
			// in the eatTarget. We have since then changed it to contain the corpse
			// itself. For older saves, we need to convert this.
			//
			// we also need to update lastEatTargetPosition since it was not present
			// in older saves
			//
			if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
				if (eatTarget is Pawn p && p.Map != null)
				{
					// find corpse that points to the pawn we stored
					eatTarget = p.Map.thingGrid
						.ThingsListAt(eatTarget.Position)
						.OfType<Corpse>()
						.FirstOrDefault(c => c.InnerPawn == eatTarget);
				}

				// update lastEatTargetPosition
				lastEatTargetPosition = lastEatTarget?.Position ?? IntVec3.Invalid;
			}
		}

		//int ticker = 0;
		void TickAction()
		{
			var zombie = (Zombie)pawn;
			if (zombie.state == ZombieState.Emerging || zombie.state == ZombieState.Floating)
				return;

			/*
			// for debugging - let zombies only live for 600 ticks
			// --------------------------------------------------
			if (++ticker > 600)
			{
				EndJobWith(JobCondition.InterruptForced);
				zombie.Kill(null); return;
			}
			// --------------------------------------------------
			*/

			if (zombie.raging != 0)
				ZombieStateHandler.CheckEndRage(zombie);

			if (ZombieStateHandler.NeedsShouldDieTick(zombie, out var tick10))
				if (this.ShouldDie(zombie, tick10))
					return;

			if (this.HandleParalyzedTick(zombie))
				return;

			if (zombie.wallPushProgress >= 0f && ZombieStateHandler.WallPushing(zombie))
				return;

			if (zombie.ropedBy != null && this.Roping(zombie))
			{
				this.ExecuteMove(zombie, zombie.Map.GetGrid());
				return;
			}

			if (ZombieStateHandler.NeedsDownedOrUnconsciousnessTick(zombie))
			{
				if (ZombieStateHandler.DownedOrUnconsciousness(zombie))
					return;
			}
			else if (zombie.IsTanky == false)
			{
				var wasAffectingAvoidGrid = zombie.AffectsAvoidGrid;
				zombie.consciousness = 1f;
				zombie.RequestAvoidGridRefreshIfAffectingChanged(wasAffectingAvoidGrid);
			}

			if (ZombieStateHandler.NeedsAttackTick(zombie) && this.Attack(zombie))
				return;

			var grid = zombie.Map.GetGrid();
			if (ZombieStateHandler.NeedsWallPushStartTick(zombie) && ZombieStateHandler.CheckWallPushing(zombie, grid))
				return;

			if (this.ValidDestination(zombie))
				return;

			ZombieStateHandler.ApplyFire(zombie);

			var bodyType = zombie.story.bodyType;
			if (zombie.isMiner && (bodyType == BodyTypeDefOf.Fat || bodyType == BodyTypeDefOf.Hulk))
				if (this.Mine(zombie, true))
					return;

			if (this.Eat(zombie, grid))
				return;

			bool smashTime;
			if (zombie.IsTanky)
			{
				if (this.Smash(zombie, true, false))
					return;
				smashTime = true;
			}
			else
			{
				smashTime = this.Track(zombie, grid);
				if (smashTime)
				{
					if (zombie.checkSmashable == false)
						smashTime = false;
					zombie.checkSmashable = false;
				}
				if (this.Smash(zombie, smashTime, true))
					return;
			}

			var possibleMoves = this.PossibleMoves(zombie);
			if (possibleMoves.Count > 0)
			{
				if (zombie.raging > 0 || zombie.IsTanky || zombie.isAlbino || zombie.isDarkSlimer || (zombie.wasMapPawnBefore && zombie.state != ZombieState.Tracking))
					if (this.RageMove(zombie, grid, possibleMoves, smashTime))
						return;

				if (zombie.raging <= 0)
				{
					if (zombie.isMiner)
						if (this.Mine(zombie, false))
							return;

					this.Wander(zombie, grid, possibleMoves);
				}
			}

			this.ExecuteMove(zombie, grid);

			ZombieStateHandler.BeginRage(zombie, grid);
		}

		public override void Notify_PatherArrived()
		{
			base.Notify_PatherArrived();
			destination = IntVec3.Invalid;
			nextDestinationValidationTick = 0;

			var zombie = (Zombie)pawn;
			zombie.checkSmashable = true;

			if (zombie.IsActiveElectric)
				ZombieStateHandler.Electrify(zombie);
		}

		public override string GetReport()
		{
			return "Stumbling";
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
