using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace ZombieLand
{
	public enum SpitterState
	{
		Idle,
		Searching,
		Moving,
		Preparing,
		Spitting,
		Leaving
	}

	public class JobDriver_Spitter : JobDriver
	{
		public SpitterState state = SpitterState.Idle;
		public bool aggressive = false;
		public int moveState = -1;
		public int tickCounter = 0;
		public int spitInterval = 0;
		public int waves = 0;
		public int remainingZombies = 0;

		void InitAction()
		{
			aggressive = Rand.Bool;
			waves = aggressive ? Rand.Range(1, 6) : Rand.Range(10, 20);
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref state, "state", SpitterState.Idle);
			Scribe_Values.Look(ref aggressive, "aggressive", false);
			Scribe_Values.Look(ref moveState, "moveState", -1);
			Scribe_Values.Look(ref tickCounter, "tickCounter", 0);
			Scribe_Values.Look(ref spitInterval, "spitInterval", 0);
			Scribe_Values.Look(ref waves, "waves", 0);
			Scribe_Values.Look(ref remainingZombies, "remainingZombies", 0);
		}

		void DoIdle(int minTicks, int maxTicks)
		{
			tickCounter = Rand.Range(minTicks, maxTicks);
			state = SpitterState.Idle;
		}

		void DoMoving(IntVec3 destination)
		{
			pawn.pather.StartPath(destination, PathEndMode.OnCell);
			state = SpitterState.Moving;
		}

		public IntVec3 TryFindNewTarget()
		{
			var map = Map;
			var cell = IntVec3.Invalid;

			if (Rand.Value < 0.5f && map.listerBuildings.allBuildingsColonist.TryRandomElement(out var building))
			{
				cell = CellFinder.StandableCellNear(building.Position, map, 10);
				if (cell.IsValid)
					return cell;
			}

			if (Rand.Value < 0.5f)
			{
				var colonists = map.mapPawns.FreeColonistsAndPrisonersSpawned;
				var n = colonists.Count();
				if (n > 1)
				{
					var x = colonists.Sum(p => p.Position.x);
					var z = colonists.Sum(p => p.Position.z);
					return new IntVec3(x / n, 0, z / n);
				}
			}

			map.areaManager.Home.ActiveCells.Where(c => c.Standable(map)).TryRandomElement(out cell);
			if (cell.IsValid)
				return cell;

			if (map.listerThings.AllThings.Where(t => t.Faction == Faction.OfPlayer).TryRandomElement(out var thing))
			{
				cell = CellFinder.StandableCellNear(thing.Position, map, 10);
				if (cell.IsValid)
					return cell;
			}

			return IntVec3.Invalid;
		}

		void DoShooting()
		{
			tickCounter = 0;
			spitInterval = aggressive ? Rand.Range(10, 20) : Rand.Range(360, 900);
			remainingZombies = aggressive ? Rand.Range(5, 10) : Rand.Range(20, 50);
			state = SpitterState.Spitting;
		}

		void Shoot()
		{
			var target = TryFindNewTarget();
			if (target.IsValid && (target.x != 0 || target.z != 0))
			{
				CustomDefs.BallSpit.PlayOneShot(new TargetInfo(pawn.Position, pawn.Map, false));
				var projectile = (Projectile)GenSpawn.Spawn(CustomDefs.ZombieBall, pawn.Position, pawn.Map, WipeMode.Vanish);
				projectile.Launch(pawn, pawn.DrawPos + new Vector3(0, 0, 0.5f), target, target, ProjectileHitFlags.IntendedTarget);
			}
		}

		void DoPreparing()
		{
			tickCounter = Rand.Range(120, 180);
			state = SpitterState.Preparing;
		}

		void TickAction()
		{
			switch (state)
			{
				case SpitterState.Idle:
					if (tickCounter > 0)
						tickCounter--;
					else
						state = SpitterState.Searching;
					break;

				case SpitterState.Searching:
					var destination = RCellFinder.FindSiegePositionFrom(pawn.Position, Map, false, false);
					if (destination.IsValid)
						DoMoving(destination);
					else
						DoIdle(120, 120);
					break;

				case SpitterState.Moving:
					var currentMoveState = Mathf.FloorToInt(pawn.Drawer.tweener.MovedPercent() * 3.999f);
					if (moveState != currentMoveState)
					{
						moveState = currentMoveState;
						CustomDefs.SpitterMove.PlayOneShot(new TargetInfo(pawn.Position, pawn.Map, false));
					}
					break;

				case SpitterState.Preparing:
					if (tickCounter > 0)
						tickCounter--;
					else
						DoShooting();
					break;

				case SpitterState.Spitting:
					if (remainingZombies <= 0)
					{
						waves--;
						if (waves > 0)
						{
							DoPreparing();
							return;
						}

						if (RCellFinder.TryFindBestExitSpot(pawn, out var exitCell, TraverseMode.ByPawn, false))
						{
							pawn.pather.StartPath(exitCell, PathEndMode.OnCell);
							state = SpitterState.Leaving;
							return;
						}

						DoIdle(300, 900);
						break;
					}
					if (tickCounter < spitInterval)
					{
						tickCounter++;
						return;
					}
					Shoot();
					remainingZombies--;
					tickCounter = 0;
					break;

				case SpitterState.Leaving:
					break;
			}
		}

		public override void Notify_PatherArrived()
		{
			base.Notify_PatherArrived();

			if (state == SpitterState.Leaving)
			{
				pawn.Destroy();
				return;
			}

			DoPreparing();
		}

		public override void Notify_PatherFailed()
		{
			base.Notify_PatherFailed();
			DoIdle(300, 900);
		}

		public override string GetReport()
		{
			return "Spitting";
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
