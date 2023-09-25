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
		public ZombieSpitter spitter;

		void InitAction()
		{
			spitter = pawn as ZombieSpitter;
		}

		void DoIdle(int minTicks, int maxTicks)
		{
			spitter.idleCounter++;
			if (spitter.idleCounter > 3)
			{
				DoShooting();
				return;
			}
			spitter.tickCounter = Rand.Range(minTicks, maxTicks);
			spitter.state = SpitterState.Idle;
		}

		void DoMoving(IntVec3 destination)
		{
			spitter.pather.StartPath(destination, PathEndMode.OnCell);
			spitter.state = SpitterState.Moving;
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
			spitter.spitInterval = Mathf.FloorToInt(spitter.aggressive ? Tools.SpitterRandRange(20, 5, 40, 10) : Tools.SpitterRandRange(60, 30, 120, 60));
			if (spitter.spitInterval < 4)
				spitter.spitInterval = 4;
			spitter.remainingZombies = Mathf.FloorToInt(spitter.aggressive ? Tools.SpitterRandRange(1, 5, 10, 20) : Rand.Range(1, 2));
			if (spitter.remainingZombies < 1)
				spitter.remainingZombies = 1;
			spitter.tickCounter = spitter.spitInterval;
			spitter.state = SpitterState.Spitting;
		}

		bool Shoot()
		{
			var target = TryFindNewTarget();
			if (target.IsValid && (target.x != 0 || target.z != 0))
			{
				CustomDefs.BallSpit.PlayOneShot(new TargetInfo(spitter.Position, spitter.Map, false));
				var projectile = (Projectile)GenSpawn.Spawn(CustomDefs.ZombieBall, spitter.Position, spitter.Map, WipeMode.Vanish);
				projectile.Launch(spitter, spitter.DrawPos + new Vector3(0, 0, 0.5f), target, target, ProjectileHitFlags.IntendedTarget);
				return true;
			}
			return false;
		}

		void DoPreparing()
		{
			spitter.tickCounter = Rand.Range(120, 180);
			spitter.state = SpitterState.Preparing;
		}

		void TickAction()
		{
			switch (spitter.state)
			{
				case SpitterState.Idle:
					if (spitter.tickCounter > 0)
						spitter.tickCounter--;
					else
						spitter.state = SpitterState.Searching;
					break;

				case SpitterState.Searching:
					var destination = RCellFinder.FindSiegePositionFrom(spitter.Position, Map, false, false);
					if (destination.IsValid)
						DoMoving(destination);
					else
					{
						var distance = GenMath.LerpDouble(0, 5, 60, 12, ZombieSettings.Values.spitterThreat);
						destination = RCellFinder.RandomWanderDestFor(spitter, spitter.Position, distance, (spitter, c1, c2) => true, Danger.Deadly);
						if (destination.IsValid)
							DoMoving(destination);
						else
						{
							if (RCellFinder.TryFindSiegePosition(spitter.Position, 10, spitter.Map, false, out destination))
								DoMoving(destination);
							else
								DoIdle(120, 120);
						}
					}
					break;

				case SpitterState.Moving:
					var currentMoveState = Mathf.FloorToInt(spitter.Drawer.tweener.MovedPercent() * 3.999f);
					if (spitter.moveState != currentMoveState)
					{
						spitter.moveState = currentMoveState;
						CustomDefs.SpitterMove.PlayOneShot(new TargetInfo(spitter.Position, spitter.Map, false));
					}
					break;

				case SpitterState.Preparing:
					if (spitter.tickCounter > 0)
						spitter.tickCounter--;
					else
					{
						spitter.firstShot = true;
						DoShooting();
					}
					break;

				case SpitterState.Spitting:
					if (spitter.remainingZombies <= 0)
					{
						spitter.waves--;
						if (spitter.waves > 0)
						{
							spitter.state = SpitterState.Searching;
							return;
						}

						if (RCellFinder.TryFindBestExitSpot(spitter, out var exitCell, TraverseMode.ByPawn, false))
						{
							spitter.pather.StartPath(exitCell, PathEndMode.OnCell);
							spitter.state = SpitterState.Leaving;
							return;
						}

						DoIdle(300, 900);
						break;
					}
					if (spitter.tickCounter < spitter.spitInterval)
					{
						spitter.tickCounter++;
						return;
					}
					if (Shoot())
					{
						spitter.remainingZombies--;
						spitter.tickCounter = 0;
					}
					break;

				case SpitterState.Leaving:
					break;
			}
		}

		public override void Notify_PatherArrived()
		{
			base.Notify_PatherArrived();

			if (spitter.state == SpitterState.Leaving)
			{
				spitter.Destroy();
				return;
			}

			DoPreparing();
		}

		public override void Notify_PatherFailed()
		{
			base.Notify_PatherFailed();
			DoIdle(30, 90);
		}

		public override string GetReport()
		{
			var modeStr = spitter.aggressive ? "Aggressive".Translate() : "Calm".Translate();
			var waveStr = spitter.waves < 1 ? "" : $"{spitter.waves} {"Waves".Translate()}";
			var stateStr = ("SpitterState" + Enum.GetName(typeof(SpitterState), spitter.state)).Translate();
			var zombieStr = spitter.state != SpitterState.Spitting ? "" : $", {spitter.remainingZombies} zombies";
			return $"{modeStr}, {waveStr}, {stateStr}{zombieStr}";
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
