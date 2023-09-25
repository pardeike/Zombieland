﻿using RimWorld;
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
		public int idleCounter = 0;
		public bool firstShot = true;
		public bool aggressive = false;
		public int moveState = -1;
		public int tickCounter = 0;
		public int spitInterval = 0;
		public int waves = 0;
		public int remainingZombies = 0;

		void InitAction()
		{
			var f = ZombieSettings.Values.spitterThreat;
			aggressive = ShipCountdown.CountingDown || Rand.Chance(f / 2f);
			(pawn as ZombieSpitter).aggressive = aggressive;
			waves = Mathf.FloorToInt(f * (aggressive ? Rand.Range((1f, 2f).F(), (2f, 10f).F()) : Rand.Range((2f, 15f).F(), (4f, 30f).F())));
			if (waves < 1)
				waves = 1;
			idleCounter = 0;
			firstShot = true;
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref state, "state", SpitterState.Idle);
			Scribe_Values.Look(ref idleCounter, "idleCounter", 0);
			Scribe_Values.Look(ref firstShot, "firstShot", true);
			Scribe_Values.Look(ref aggressive, "aggressive", false);
			Scribe_Values.Look(ref moveState, "moveState", -1);
			Scribe_Values.Look(ref tickCounter, "tickCounter", 0);
			Scribe_Values.Look(ref spitInterval, "spitInterval", 0);
			Scribe_Values.Look(ref waves, "waves", 0);
			Scribe_Values.Look(ref remainingZombies, "remainingZombies", 0);
			if (Scribe.mode == LoadSaveMode.PostLoadInit)
				(pawn as ZombieSpitter).aggressive = aggressive;
		}

		void DoIdle(int minTicks, int maxTicks)
		{
			idleCounter++;
			if (idleCounter > 3)
			{
				DoShooting();
				return;
			}
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
			var f = ZombieSettings.Values.spitterThreat;
			var fReverse = 5f - f;
			spitInterval = Mathf.FloorToInt(fReverse * (aggressive ? Rand.Range((20f, 5f).F(), (40f, 10f).F()) : Rand.Range((60f, 30f).F(), (120f, 60f).F())));
			if (spitInterval < 4)
				spitInterval = 4;
			remainingZombies = Mathf.FloorToInt(f * (aggressive ? Rand.Range((1f, 5f).F(), (10f, 20f).F()) : Rand.Range(1, 2)));
			if (remainingZombies < 1)
				remainingZombies = 1;
			tickCounter = spitInterval;
			state = SpitterState.Spitting;
		}

		bool Shoot()
		{
			var target = TryFindNewTarget();
			if (target.IsValid && (target.x != 0 || target.z != 0))
			{
				CustomDefs.BallSpit.PlayOneShot(new TargetInfo(pawn.Position, pawn.Map, false));
				var projectile = (Projectile)GenSpawn.Spawn(CustomDefs.ZombieBall, pawn.Position, pawn.Map, WipeMode.Vanish);
				projectile.Launch(pawn, pawn.DrawPos + new Vector3(0, 0, 0.5f), target, target, ProjectileHitFlags.IntendedTarget);
				return true;
			}
			return false;
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
					{
						var distance = GenMath.LerpDouble(0, 5, 60, 12, Tools.Difficulty());
						destination = RCellFinder.RandomWanderDestFor(pawn, pawn.Position, distance, (pawn, c1, c2) => true, Danger.Deadly);
						if (destination.IsValid)
							DoMoving(destination);
						else
						{
							if (RCellFinder.TryFindSiegePosition(pawn.Position, 10, pawn.Map, false, out destination))
								DoMoving(destination);
							else
								DoIdle(120, 120);
						}
					}
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
					{
						firstShot = true;
						DoShooting();
					}
					break;

				case SpitterState.Spitting:
					if (remainingZombies <= 0)
					{
						waves--;
						if (waves > 0)
						{
							state = SpitterState.Searching;
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
					if (Shoot())
					{
						remainingZombies--;
						tickCounter = 0;
					}
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
			var modeStr = aggressive ? "Aggressive".Translate() : "Calm".Translate();
			var waveStr = waves < 1 ? "" : $"{waves} {"Waves".Translate()}";
			var stateStr = ("SpitterState" + Enum.GetName(typeof(SpitterState), state)).Translate();
			var zombieStr = state != SpitterState.Spitting ? "" : $", {remainingZombies} zombies";
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
