using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace ZombieLand
{
	public class JobDriver_Sabotage : JobDriver
	{
		public IntVec3 destination = IntVec3.Invalid;
		public Building_Door door = null;
		public Thing hackTarget = null;
		public int waitCounter = 0;
		public int hackCounter = 0;

		void InitAction()
		{
			destination = IntVec3.Invalid;
			door = null;
			hackTarget = null;
			waitCounter = 0;
			hackCounter = 0;
			(pawn as Zombie).scream = -1;
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref destination, "destination", IntVec3.Invalid);
			Scribe_References.Look(ref door, "door");
			Scribe_References.Look(ref hackTarget, "hackTarget");
			Scribe_Values.Look(ref waitCounter, "waitCounter", 0);
			Scribe_Values.Look(ref hackCounter, "hackCounter", 0);
		}

		void TickAction()
		{
			var zombie = (Zombie)pawn;
			if (zombie.state == ZombieState.Emerging)
				return;

			if (this.DieEasily())
				return;

			if (this.Wait())
				return;

			if (this.Scream())
				return;

			if (this.HackThing())
				return;

			if (this.CheckAndFindDestination())
				return;

			waitCounter = 60;
		}

		public override void Notify_PatherArrived()
		{

			base.Notify_PatherArrived();
			destination = IntVec3.Invalid;
		}

		public override void Notify_PatherFailed()
		{

			base.Notify_PatherFailed();
			InitAction();
		}

		public override string GetReport()
		{
			return "Sabotaging";
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

	static class SabotageHandler
	{
		static bool TryFindLastCellBeforeBlockingDoor(this PawnPath path, Pawn pawn, out IntVec3 result, out Building_Door door)
		{
			if (path.NodesReversed.Count == 1)
			{
				result = path.NodesReversed[0];
				door = null;
				return false;
			}

			var nodesReversed = path.NodesReversed;
			for (var num = nodesReversed.Count - 2; num >= 1; num--)
			{
				door = nodesReversed[num].GetEdifice(pawn.Map) as Building_Door;
				if (door != null && !door.CanPhysicallyPass(pawn))
				{
					result = nodesReversed[num + 1];
					return true;
				}
			}

			result = nodesReversed[0];
			door = null;
			return false;
		}

		static bool Goto(this JobDriver_Sabotage driver, Thing thing)
		{
			if (thing == null || thing.Spawned == false)
				return false;

			var zombie = driver.pawn;
			var mode = thing.Position.Standable(thing.Map) ? PathEndMode.ClosestTouch : PathEndMode.Touch;
			var path = zombie.Map.pathFinder.FindPath(zombie.Position, thing, TraverseParms.For(zombie, Danger.None, TraverseMode.PassDoors, false), mode);
			if (path.Found)
			{
				if (path.TryFindLastCellBeforeBlockingDoor(zombie, out var doorCell, out var door) && doorCell.IsValid)
				{
					driver.door = door;
					driver.destination = doorCell;
					driver.hackTarget = thing;
					path.ReleaseToPool();
					zombie.pather.StartPath(doorCell, PathEndMode.OnCell);
					return true;
				}
				else if (path.NodesLeftCount > 0)
				{
					var cell = path.NodesLeftCount > 1 ? path.NodesReversed[1] : path.NodesReversed[0];
					if (cell.IsValid)
					{
						driver.destination = cell;
						driver.hackTarget = thing;
						path.ReleaseToPool();
						zombie.pather.StartPath(cell, PathEndMode.OnCell);
						return true;
					}
				}
			}
			path.ReleaseToPool();
			return false;
		}

		static bool Goto(this JobDriver_Sabotage driver, IntVec3 cell, Action arrivalAction = null)
		{
			if (cell.IsValid == false)
				return false;

			var zombie = driver.pawn;
			var path = zombie.Map.pathFinder.FindPath(zombie.Position, cell, TraverseParms.For(zombie, Danger.None, TraverseMode.PassDoors, false), PathEndMode.OnCell);
			if (path.Found)
			{
				if (path.TryFindLastCellBeforeBlockingDoor(zombie, out var doorCell, out var door) && doorCell.IsValid)
				{
					driver.door = door;
					driver.destination = doorCell;
					path.ReleaseToPool();
					zombie.pather.StartPath(doorCell, PathEndMode.OnCell);
					return true;
				}
				else
				{
					driver.destination = cell;
					path.ReleaseToPool();
					zombie.pather.StartPath(cell, PathEndMode.OnCell);
					arrivalAction?.Invoke();
					return true;
				}
			}
			path.ReleaseToPool();
			return false;
		}

		static bool Hack(this JobDriver_Sabotage driver, Thing thing, Action action)
		{
			if (driver.hackCounter == 0)
			{
				CustomDefs.Hacking.PlayOneShot(new TargetInfo(thing.Position, thing.Map, false));
				Tools.CastThoughtBubble(driver.pawn, Constants.HACKING);
				driver.hackCounter = 240;
				return true;
			}

			if (driver.hackCounter > 0)
			{
				driver.hackCounter--;
				if (driver.hackCounter == 0)
					action();
				return true;
			}

			return false;
		}

		public static bool HackThing(this JobDriver_Sabotage driver)
		{
			if (driver.destination.IsValid)
				return false;

			var door = driver.door;
			if (door != null && door.Spawned && door.CanPhysicallyPass(driver.pawn) == false)
				return driver.Hack(door, () =>
				{
					driver.pawn.rotationTracker.FaceTarget(door);
					door.StartManualOpenBy(driver.pawn);
					door.ticksUntilClose *= 4;
					driver.door = null;
					driver.waitCounter = 90;

					if (driver.hackTarget != null)
						_ = driver.Goto(driver.hackTarget);
				});

			var thing = driver.hackTarget;
			if (thing != null && thing.Spawned)
				return driver.Hack(thing, () =>
				{
					var compFlickable = thing.TryGetComp<CompFlickable>();
					if (compFlickable != null && compFlickable.SwitchIsOn)
					{
						compFlickable.SwitchIsOn = false;
						driver.pawn.rotationTracker.FaceTarget(thing);
						SoundDefOf.FlickSwitch.PlayOneShot(new TargetInfo(thing.Position, thing.Map, false));
						Tools.CastThoughtBubble(driver.pawn, Constants.HACKING);
						driver.hackTarget = null;
						return;
					}

					var compPowerTrader = thing.TryGetComp<CompPowerTrader>();
					if (compPowerTrader != null && compPowerTrader.PowerOn)
					{
						compPowerTrader.PowerOn = false;
						driver.pawn.rotationTracker.FaceTarget(thing);
						SoundDefOf.FlickSwitch.PlayOneShot(new TargetInfo(thing.Position, thing.Map, false));
						Tools.CastThoughtBubble(driver.pawn, Constants.HACKING);
						driver.hackTarget = null;
						return;
					}

					if (thing.def.IsRangedWeapon && thing.def.useHitPoints)
					{
						driver.pawn.rotationTracker.FaceTarget(thing);
						Tools.CastThoughtBubble(driver.pawn, Constants.HACKING);
						var amount = Math.Max(1, thing.HitPoints / 2);
						_ = thing.TakeDamage(new DamageInfo(DamageDefOf.Deterioration, amount, 0, -1, driver.pawn));
						driver.hackTarget = null;
						return;
					}

					driver.hackTarget = null;
				});

			return false;
		}

		public static bool Scream(this JobDriver_Sabotage driver)
		{
			var zombie = driver.pawn as Zombie;

			if (zombie.scream == -1)
				return false;

			if (zombie.scream == -2)
			{
				if (driver.destination.IsValid == false)
				{
					driver.waitCounter = 120;
					zombie.scream = 0;
					zombie.Rotation = Rot4.South;
				}
				return true;
			}

			if (zombie.scream == 0)
			{
				CustomDefs.Scream.PlayOneShot(new TargetInfo(zombie.Position, zombie.Map, false));
				Tools.CastThoughtBubble(driver.pawn, Constants.RAGING);
			}

			zombie.scream += 1;

			if (zombie.scream % 40 == 0)
			{
				var pos = zombie.Position;
				var d = 1 + (int)(zombie.scream * 12f / 401);
				var dist = d * d;
				var stunTicks = 60 * (14 - d);
				zombie.Map.mapPawns.AllPawns.ToArray().DoIf(
					pawn => pawn is not Zombie && pawn is not ZombieBlob && pawn is not ZombieSpitter
						&& pawn.RaceProps.Humanlike
						&& pawn.RaceProps.IsFlesh
						&& AlienTools.IsFleshPawn(pawn)
						&& SoSTools.IsHologram(pawn) == false
						&& pawn.Position.DistanceToSquared(pos) < dist
						&& pawn.health.Downed == false
						&& pawn.InMentalState == false
						&& pawn.CurJobDef != JobDefOf.Vomit,
					pawn =>
					{
						if (RestUtility.Awake(pawn) == false)
							RestUtility.WakeUp(pawn);
						pawn.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Vomit), JobCondition.InterruptForced, null, true, true);
						pawn.stances.stunner.StunFor(stunTicks, zombie, true);
					});
			}

			if (zombie.scream >= 400)
			{
				zombie.scream = -1;
				return false;
			}

			return true;
		}

		static List<Hediff_Injury> tmpHediffInjury = new();
		public static bool DieEasily(this JobDriver_Sabotage driver)
		{
			if (driver.pawn.health.Downed)
			{
				driver.pawn.Kill(null);
				return true;
			}
			tmpHediffInjury.Clear();
			driver.pawn.health.hediffSet.GetHediffs(ref tmpHediffInjury);
			if (tmpHediffInjury.Any())
			{
				driver.pawn.Kill(null);
				return true;
			}
			return false;
		}

		public static bool Wait(this JobDriver_Sabotage driver)
		{
			if (driver.waitCounter > 0)
			{
				driver.waitCounter--;
				return true;
			}

			return false;
		}

		public static bool CheckAndFindDestination(this JobDriver_Sabotage driver)
		{
			if (driver.destination.IsValid)
				return true;

			var zombie = driver.pawn as Zombie;
			var map = zombie.Map;

			if (Rand.Chance(0.8f) && driver.ChooseSabotageTarget())
				return true;

			if (Rand.Chance(0.1f) && RCellFinder.TryFindRandomSpotJustOutsideColony(zombie.Position, map, null, out var cell))
				if (driver.Goto(cell))
					return true;

			if (RCellFinder.TryFindDirectFleeDestination(zombie.Position, 16f, zombie, out cell))
				if (driver.Goto(cell))
					return true;

			driver.destination = IntVec3.Invalid;
			driver.waitCounter = 30;
			return false;
		}

		static IntVec3 PawnCenter(Map map, IEnumerable<Pawn> pawns)
		{
			var count = pawns.Count();
			if (count == 0)
				return IntVec3.Invalid;
			var vec = pawns.Select(p => p.Position.ToVector3()).Aggregate((prev, pos) => prev + pos) / count;
			using var it = GenRadial.RadialCellsAround(vec.ToIntVec3(), 6, true).GetEnumerator();
			while (it.MoveNext())
				if (it.Current.Standable(map))
					return it.Current;
			return IntVec3.Invalid;
		}

		static bool ChooseSabotageTarget(this JobDriver_Sabotage driver)
		{
			var zombie = driver.pawn as Zombie;
			var map = zombie.Map;
			IntVec3 cell;
			var options = new int[] { 0, 1, 2, 3, 4, 5 }.InRandomOrder().ToArray();

			for (var i = 0; i < options.Length; i++)
				switch (options[i])
				{
					// hack door of a room
					case 0:
						var valuableRoom = Tools.ValuableRooms(map).SafeRandomElement();
						if (valuableRoom != null)
						{
							var cells = valuableRoom.Cells.Where(c => c.Standable(map));
							cell = cells.SafeRandomElement(IntVec3.Invalid);
							if (driver.Goto(cell))
								return true;
						}
						break;

					// move to home zone
					case 1:
						var homeCell = map.areaManager.Home.ActiveCells.SafeRandomElement(IntVec3.Invalid);
						if (driver.Goto(homeCell))
							return true;
						break;

					// turn off a flickable thing
					case 2:
						var building = map.listerBuildings.allBuildingsColonist.Where(b =>
						{
							var compFlickable = b.Spawned ? b.TryGetComp<CompFlickable>() : null;
							if (compFlickable != null && compFlickable.SwitchIsOn)
								return true;
							var compPowerTrader = b.TryGetComp<CompPowerTrader>();
							if (compPowerTrader != null && compPowerTrader.PowerOn)
								return true;
							return false;

						}).SafeRandomElement();
						if (driver.Goto(building))
							return true;
						break;

					// degrade a weapon
					case 3:
						var weapon = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)
							.Where(t => t.def.IsRangedWeapon && t.def.useHitPoints)
							.OrderBy(t => -t.MarketValue).FirstOrDefault();
						if (driver.Goto(weapon))
							return true;
						break;

					// scream on colonists
					case 4:
						cell = PawnCenter(map, map.mapPawns.FreeColonists);
						if (driver.Goto(cell, () => zombie.scream = -2))
							return true;
						break;

					// scream on enemies
					case 5:
						var enemies = map.attackTargetsCache
							.TargetsHostileToColony.OfType<Pawn>()
							.Where(p => p is not Zombie && p is not ZombieBlob && p is not ZombieSpitter
								&& p.RaceProps.Humanlike
								&& p.RaceProps.IsFlesh
								&& AlienTools.IsFleshPawn(p)
								&& SoSTools.IsHologram(p) == false
								&& p.health.Downed == false
							);
						cell = PawnCenter(map, enemies);
						if (driver.Goto(cell, () => zombie.scream = -2))
							return true;
						break;
				}

			return false;
		}
	}
}
