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
		public IntVec3 queuedScreamCell = IntVec3.Invalid;
		public int waitCounter = 0;
		public int hackCounter = 0;

		void InitAction()
		{
			destination = IntVec3.Invalid;
			door = null;
			hackTarget = null;
			queuedScreamCell = IntVec3.Invalid;
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
			Scribe_Values.Look(ref queuedScreamCell, "queuedScreamCell", IntVec3.Invalid);
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

			if (this.HandleParalyzedTick(zombie))
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
		const int albinoScreamDurationTicks = 400;
		const float albinoScreamMaxRadius = 12f;
		const int albinoScreamWindupTicks = 120;
		const int albinoScreamInitialMinCooldown = 600;
		const int albinoScreamInitialMaxCooldown = 1800;
		const int albinoScreamWastedMinCooldown = 1800;
		const int albinoScreamWastedMaxCooldown = 3000;
		const int albinoScreamSuccessMinCooldown = 5000;
		const int albinoScreamSuccessMaxCooldown = 9000;
		const int albinoScreamMaxCooldown = 12000;

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

			driver.queuedScreamCell = IntVec3.Invalid;
			var zombie = driver.pawn;
			var mode = thing.Position.Standable(thing.Map) ? PathEndMode.ClosestTouch : PathEndMode.Touch;
			var path = zombie.Map.pathFinder.FindPathNow(zombie.Position, thing, TraverseParms.For(zombie, Danger.None, TraverseMode.PassDoors, false), null, mode, null);
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

			driver.queuedScreamCell = IntVec3.Invalid;
			var zombie = driver.pawn;
			var path = zombie.Map.pathFinder.FindPathNow(zombie.Position, cell, TraverseParms.For(zombie, Danger.None, TraverseMode.PassDoors, false), null, PathEndMode.OnCell, null);
			if (path.Found)
			{
				if (path.TryFindLastCellBeforeBlockingDoor(zombie, out var doorCell, out var door) && doorCell.IsValid)
				{
					driver.door = door;
					driver.destination = doorCell;
					if (arrivalAction != null)
						driver.queuedScreamCell = cell;
					path.ReleaseToPool();
					zombie.pather.StartPath(doorCell, PathEndMode.OnCell);
					return true;
				}
				else
				{
					driver.destination = cell;
					driver.queuedScreamCell = IntVec3.Invalid;
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
				if (ZombieAwarenessCues.ShouldPlayWallAndSabotageSound())
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

			if (driver.ResumeDoorTargetIfPassable())
				return true;

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
					else if (driver.queuedScreamCell.IsValid)
					{
						var screamCell = driver.queuedScreamCell;
						_ = driver.Goto(screamCell, () => ((Zombie)driver.pawn).scream = -2);
					}
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
						if (ZombieAwarenessCues.ShouldPlayWallAndSabotageSound())
							SoundDefOf.FlickSwitch.PlayOneShot(new TargetInfo(thing.Position, thing.Map, false));
						Tools.CastThoughtBubble(driver.pawn, Constants.HACKING);
						driver.hackTarget = null;
						return;
					}

					var compBreakdownable = thing.TryGetComp<CompBreakdownable>();
					if (compBreakdownable != null && compBreakdownable.BrokenDown == false)
					{
						compBreakdownable.DoBreakdown();
						driver.pawn.rotationTracker.FaceTarget(thing);
						if (ZombieAwarenessCues.ShouldPlayWallAndSabotageSound())
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
						if (ZombieAwarenessCues.ShouldPlayWallAndSabotageSound())
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

		static bool ResumeDoorTargetIfPassable(this JobDriver_Sabotage driver)
		{
			var door = driver.door;
			if (door == null)
				return false;

			if (door.Spawned && door.CanPhysicallyPass(driver.pawn) == false)
				return false;

			driver.door = null;
			driver.hackCounter = 0;

			var thing = driver.hackTarget;
			if (thing != null)
			{
				if (thing.Spawned && driver.Goto(thing))
					return true;

				driver.hackTarget = null;
				return true;
			}

			if (driver.queuedScreamCell.IsValid == false)
				return false;

			var screamCell = driver.queuedScreamCell;
			return driver.Goto(screamCell, () => ((Zombie)driver.pawn).scream = -2);
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
					if (zombie.HasAlbinoScreamTargetInRange(albinoScreamMaxRadius) == false)
					{
						zombie.scream = -1;
						driver.queuedScreamCell = IntVec3.Invalid;
						SetAlbinoScreamCooldown(zombie, false);
						driver.waitCounter = 60;
						return false;
					}

					driver.waitCounter = Rand.Range(80, 181);
					zombie.scream = 0;
					zombie.Rotation = Rot4.South;
				}
				return true;
			}

			if (zombie.scream == 0)
			{
				zombie.albinoScreamAffectedCount = 0;
				if (ZombieAwarenessCues.ShouldPlayWallAndSabotageSound())
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
				foreach (var pawn in zombie.Map.mapPawns.AllPawnsSpawned)
					if (CanAlbinoScreamAffect(pawn, zombie) && pawn.Position.DistanceToSquared(pos) < dist)
					{
						if (RestUtility.Awake(pawn) == false)
							RestUtility.WakeUp(pawn);
						pawn.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Vomit), JobCondition.InterruptForced, null, true, true);
						pawn.stances.stunner.StunFor(stunTicks, zombie, true);
						zombie.albinoScreamAffectedCount++;
					}
			}

			if (zombie.scream >= albinoScreamDurationTicks)
			{
				SetAlbinoScreamCooldown(zombie, zombie.albinoScreamAffectedCount > 0);
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

		static bool IsZombielandZombie(Pawn pawn)
		{
			return pawn is Zombie || pawn is ZombieSymbiant || pawn is ZombieSpitter;
		}

		static bool CanAlbinoScreamAffect(Pawn pawn, Zombie zombie)
		{
			return pawn != null
				&& zombie != null
				&& pawn.Spawned
				&& pawn.Map == zombie.Map
				&& pawn.Dead == false
				&& IsZombielandZombie(pawn) == false
				&& Customization.DoesAttractsZombies(pawn)
				&& pawn.RaceProps.Humanlike
				&& pawn.RaceProps.IsFlesh
				&& AlienTools.IsFleshPawn(pawn)
				&& SoSTools.IsHologram(pawn) == false
				&& pawn.health?.Downed == false
				&& pawn.jobs != null
				&& pawn.stances != null
				&& pawn.InMentalState == false
				&& pawn.CurJobDef != JobDefOf.Vomit;
		}

		static bool HasAlbinoScreamTargetInRange(this Zombie zombie, float radius)
		{
			if (zombie?.Spawned != true)
				return false;

			var dist = radius * radius;
			foreach (var pawn in zombie.Map.mapPawns.AllPawnsSpawned)
				if (CanAlbinoScreamAffect(pawn, zombie) && pawn.Position.DistanceToSquared(zombie.Position) <= dist)
					return true;
			return false;
		}

		static void SetAlbinoScreamCooldown(Zombie zombie, bool successful)
		{
			var affectedCount = Math.Max(0, zombie.albinoScreamAffectedCount);
			var cooldown = successful
				? Rand.Range(albinoScreamSuccessMinCooldown, albinoScreamSuccessMaxCooldown) + Math.Min(3000, affectedCount * 600)
				: Rand.Range(albinoScreamWastedMinCooldown, albinoScreamWastedMaxCooldown);
			zombie.albinoNextScreamTick = GenTicks.TicksGame + Math.Min(cooldown, albinoScreamMaxCooldown);
		}

		static bool AlbinoScreamReady(Zombie zombie)
		{
			if (zombie.albinoNextScreamTick < 0)
			{
				zombie.albinoNextScreamTick = GenTicks.TicksGame + Rand.Range(albinoScreamInitialMinCooldown, albinoScreamInitialMaxCooldown);
				return false;
			}

			return GenTicks.TicksGame >= zombie.albinoNextScreamTick;
		}

		static bool IsDrafted(Pawn pawn)
		{
			return pawn?.drafter?.Drafted == true;
		}

		static bool IsBusy(Pawn pawn)
		{
			var defName = pawn?.CurJobDef?.defName;
			return defName != null
				&& defName != "Wait"
				&& defName != "Wait_Combat"
				&& defName != "Wait_MaintainPosture"
				&& defName != "Goto"
				&& defName != "LayDown";
		}

		static bool IsNearDraftedColonist(Pawn pawn, List<Pawn> draftedColonists)
		{
			return draftedColonists.Any(drafted => drafted != pawn && drafted.Position.DistanceToSquared(pawn.Position) <= 100);
		}

		static int NearbyPawnScore(Pawn pawn, List<Pawn> candidates, int radiusSquared)
		{
			var count = 0;
			foreach (var candidate in candidates)
				if (candidate.Position.DistanceToSquared(pawn.Position) <= radiusSquared)
					count++;
			return count;
		}

		static bool TryFindStandableScreamCell(Map map, IntVec3 root, out IntVec3 cell)
		{
			cell = IntVec3.Invalid;
			foreach (var candidate in GenRadial.RadialCellsAround(root, 5f, true))
			{
				if (candidate.InBounds(map) == false)
					continue;
				if (candidate.Standable(map) == false)
					continue;
				if (candidate.Fogged(map))
					continue;
				if (candidate.GetEdifice(map) != null)
					continue;
				if (candidate.GetFirstThing<Mineable>(map) != null)
					continue;
				if (candidate.GetThingList(map).Any(thing => thing is Pawn))
					continue;

				cell = candidate;
				return true;
			}
			return false;
		}

		static bool TryFindScreamCellForBestPawn(Zombie zombie, List<Pawn> candidates, Func<Pawn, int> scorePawn, out IntVec3 cell)
		{
			cell = IntVec3.Invalid;
			var bestScore = int.MinValue;
			foreach (var pawn in candidates)
			{
				if (TryFindStandableScreamCell(zombie.Map, pawn.Position, out var candidateCell) == false)
					continue;

				var score = scorePawn(pawn) - zombie.Position.DistanceToSquared(candidateCell) / 16;
				if (score <= bestScore)
					continue;

				bestScore = score;
				cell = candidateCell;
			}
			return cell.IsValid;
		}

		static int ColonistScreamScore(Zombie zombie, Pawn pawn, List<Pawn> colonists)
		{
			var score = 1000;
			score += NearbyPawnScore(pawn, colonists, 100) * 45;
			if (zombie.Map.areaManager.Home[pawn.Position])
				score += 35;
			if (IsBusy(pawn))
				score += 30;
			if (IsDrafted(pawn) == false)
				score += 20;
			return score;
		}

		static bool LocalTargetPointsAtZombie(LocalTargetInfo target, Zombie zombie)
		{
			if (target.HasThing)
				return target.Thing == zombie;

			return target.Cell.IsValid && target.Cell.DistanceToSquared(zombie.Position) <= 2;
		}

		static bool IsAttackingOrApproaching(Pawn pawn, Zombie zombie)
		{
			if (pawn.TargetCurrentlyAimingAt.Thing == zombie)
				return true;

			var job = pawn.CurJob;
			if (job != null && (LocalTargetPointsAtZombie(job.targetA, zombie) || LocalTargetPointsAtZombie(job.targetB, zombie) || LocalTargetPointsAtZombie(job.targetC, zombie)))
				return true;

			if (pawn.pather?.Moving == true)
			{
				var currentDistance = pawn.Position.DistanceToSquared(zombie.Position);
				var destinationDistance = pawn.pather.Destination.Cell.DistanceToSquared(zombie.Position);
				if (destinationDistance < currentDistance && currentDistance <= 324)
					return true;
			}

			return false;
		}

		static bool TryFindOpportunisticEnemyScreamCell(Zombie zombie, out IntVec3 cell)
		{
			var enemies = zombie.Map.attackTargetsCache.TargetsHostileToColony
				.OfType<Pawn>()
				.Where(pawn => CanAlbinoScreamAffect(pawn, zombie))
				.Where(pawn => IsAttackingOrApproaching(pawn, zombie) || pawn.Position.DistanceToSquared(zombie.Position) <= 100)
				.ToList();

			return TryFindScreamCellForBestPawn(
				zombie,
				enemies,
				pawn => 1300 + NearbyPawnScore(pawn, enemies, 100) * 35 + (IsAttackingOrApproaching(pawn, zombie) ? 250 : 0),
				out cell);
		}

		static bool TryFindGroupedColonistScreamCell(Zombie zombie, List<Pawn> colonists, out IntVec3 cell)
		{
			cell = IntVec3.Invalid;
			var grouped = colonists
				.Where(pawn => NearbyPawnScore(pawn, colonists, 100) >= 2)
				.ToList();
			return TryFindScreamCellForBestPawn(zombie, grouped, pawn => ColonistScreamScore(zombie, pawn, colonists), out cell);
		}

		static bool TryFindAlbinoScreamCell(this JobDriver_Sabotage driver, out IntVec3 cell, out string reason)
		{
			cell = IntVec3.Invalid;
			reason = null;
			var zombie = driver.pawn as Zombie;
			if (zombie?.Spawned != true || AlbinoScreamReady(zombie) == false)
				return false;

			if (TryFindOpportunisticEnemyScreamCell(zombie, out cell))
			{
				reason = "opportunisticEnemy";
				return true;
			}

			var colonists = zombie.Map.mapPawns.FreeColonistsSpawned
				.Where(pawn => CanAlbinoScreamAffect(pawn, zombie))
				.ToList();
			if (colonists.Count == 0)
				return false;

			var draftedColonists = colonists.Where(IsDrafted).ToList();
			var isolatedColonists = colonists
				.Where(pawn => IsDrafted(pawn) == false)
				.Where(pawn => IsBusy(pawn) || IsNearDraftedColonist(pawn, draftedColonists) == false)
				.ToList();
			if (TryFindScreamCellForBestPawn(
				zombie,
				isolatedColonists,
				pawn => ColonistScreamScore(zombie, pawn, colonists) + (IsBusy(pawn) ? 100 : 0) + (IsNearDraftedColonist(pawn, draftedColonists) ? -80 : 80),
				out cell))
			{
				reason = "isolatedColonist";
				return true;
			}

			if (TryFindGroupedColonistScreamCell(zombie, colonists, out cell))
			{
				reason = "groupedColonists";
				return true;
			}

			var homeColonists = colonists.Where(pawn => zombie.Map.areaManager.Home[pawn.Position]).ToList();
			if (TryFindScreamCellForBestPawn(zombie, homeColonists, pawn => ColonistScreamScore(zombie, pawn, colonists), out cell))
			{
				reason = "homeColonist";
				return true;
			}

			if (TryFindScreamCellForBestPawn(zombie, colonists, pawn => -pawn.Position.DistanceToSquared(zombie.Position), out cell))
			{
				reason = "nearestColonist";
				return true;
			}

			return false;
		}

		static bool CanHackBuilding(Building building)
		{
			if (building?.Spawned != true)
				return false;

			var compFlickable = building.TryGetComp<CompFlickable>();
			if (compFlickable != null && compFlickable.SwitchIsOn)
				return true;

			var compBreakdownable = building.TryGetComp<CompBreakdownable>();
			if (compBreakdownable != null && compBreakdownable.BrokenDown == false)
				return true;

			var compPowerTrader = building.TryGetComp<CompPowerTrader>();
			return compPowerTrader != null && compPowerTrader.PowerOn;
		}

		static float WeaponSabotageScore(Map map, Thing weapon)
		{
			var score = weapon.MarketValue;
			if (map.areaManager.Home[weapon.Position])
				score += 2000f;
			return score;
		}

		static bool ChooseSabotageTarget(this JobDriver_Sabotage driver)
		{
			var zombie = driver.pawn as Zombie;
			var map = zombie.Map;
			IntVec3 cell;

			if (driver.TryFindAlbinoScreamCell(out cell, out _))
				if (driver.Goto(cell, () => zombie.scream = -2))
					return true;

			var options = new int[] { 0, 1, 2, 3 }.InRandomOrder().ToArray();

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
						var building = map.listerBuildings.allBuildingsColonist.Where(CanHackBuilding).SafeRandomElement();
						if (driver.Goto(building))
							return true;
						break;

					// degrade a weapon
					case 3:
						var weapon = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)
							.Where(t => t.Spawned && t.def.IsRangedWeapon && t.def.useHitPoints)
							.OrderByDescending(t => WeaponSabotageScore(map, t)).FirstOrDefault();
						if (driver.Goto(weapon))
							return true;
						break;
				}

			return false;
		}
	}
}
