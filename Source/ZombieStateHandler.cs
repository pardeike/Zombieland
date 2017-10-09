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
	// all routines here returning a boolean stop the code flow by returning TRUE
	//
	public static class ZombieStateHandler
	{
		static readonly int[] adjIndex4 = { 0, 1, 2, 3 };
		static int prevIndex4;
		static readonly int[] adjIndex8 = { 0, 1, 2, 3, 4, 5, 6, 7 };
		static int prevIndex8;

		static readonly float combatExtendedHealAmount = 1f / GenTicks.SecondsToTicks(1f);

		// make zombies die if necessary ============================================================
		//
		public static bool ShouldDie(this JobDriver_Stumble driver, Zombie zombie)
		{
			if (zombie.Dead || zombie.Spawned == false)
			{
				driver.EndJobWith(JobCondition.InterruptForced);
				return true;
			}

			if (zombie.state == ZombieState.ShouldDie)
			{
				driver.EndJobWith(JobCondition.InterruptForced);
				zombie.Kill(null);
				return true;
			}

			if (zombie.bombTickingInterval != -1f)
			{
				if (zombie.bombWillGoOff && zombie.EveryNTick(NthTick.Every10))
					zombie.bombTickingInterval -= 2f;
				if (zombie.bombTickingInterval <= 0f)
				{
					zombie.Kill(null);
					return true;
				}
			}

			if (zombie.EveryNTick(NthTick.Every10))
			{
				if (ZombieSettings.Values.zombiesDieVeryEasily)
				{
					if (zombie.health.hediffSet.GetHediffs<Hediff_Injury>().Any())
					{
						zombie.Kill(null);
						return true;
					}
				}
				else
				{
					var hediffs = zombie.health.hediffSet.hediffs
						.Where(hediff => hediff.def == HediffDefOf.WoundInfection)
						.ToArray();
					foreach (var hediff in hediffs)
						zombie.health.RemoveHediff(hediff);
				}
			}

			return false;
		}

		// handle downed zombies ====================================================================
		//
		public static bool Downed(this JobDriver_Stumble driver, Zombie zombie)
		{
			if (zombie.Downed == false)
				return false;

			if (ZombieSettings.Values.zombiesDieVeryEasily || ZombieSettings.Values.doubleTapRequired == false)
			{
				zombie.Kill(null);
				return true;
			}

			if (zombie.EveryNTick(NthTick.Every10))
			{
				var walkCapacity = PawnCapacityUtility.CalculateCapacityLevel(zombie.health.hediffSet, PawnCapacityDefOf.Moving);
				var missingBrain = zombie.health.hediffSet.GetBrain() == null;
				if (walkCapacity < 0.25f || missingBrain)
				{
					zombie.Kill(null);
					return true;
				}

				var injuries = zombie.health.hediffSet.GetHediffs<Hediff_Injury>();
				foreach (var injury in injuries)
				{
					if (ZombieSettings.Values.zombiesDieVeryEasily)
					{
						zombie.Kill(null);
						return true;
					}

					if (Tools.IsCombatExtendedInstalled())
						injury.Heal(combatExtendedHealAmount);
					else
						injury.Heal(injury.Severity + 0.5f);
					break;
				}
			}

			return (zombie.Downed);
		}

		// invalidate destination if necessary ======================================================
		//
		public static bool ValidDestination(this JobDriver_Stumble driver, Zombie zombie)
		{
			// TODO find out if we still need to check for 0,0 as being an invalid location
			if (driver.destination.x == 0 && driver.destination.z == 0)
				driver.destination = IntVec3.Invalid;
			return zombie.HasValidDestination(driver.destination);
		}

		// attack nearby enemies ====================================================================
		//
		public static bool Attack(this JobDriver_Stumble driver, Zombie zombie)
		{
			var enemy = CanAttack(zombie);
			if (enemy == null)
				return false;

			driver.destination = enemy.Position;

			zombie.state = ZombieState.Tracking;
			if (Constants.USE_SOUND)
			{
				var info = SoundInfo.InMap(enemy);
				SoundDef.Named("ZombieHit").PlayOneShot(info);
			}

			AttackThing(zombie, enemy, JobDefOf.AttackMelee);
			return true;
		}

		// lean in and eat bodies made out of flesh =================================================
		//
		public static bool Eat(this JobDriver_Stumble driver, Zombie zombie, PheromoneGrid grid)
		{
			if (driver.eatTarget != null && driver.eatTarget.Spawned == false)
			{
				driver.eatTarget = null;
				driver.lastEatTarget = null;
				driver.eatDelayCounter = 0;
			}
			if (driver.eatTarget == null && grid.GetZombieCount(zombie.Position) <= 2)
				driver.eatTarget = CanIngest(zombie);

			var eatTargetPawn = driver.eatTarget as Pawn ?? (driver.eatTarget as Corpse)?.InnerPawn;
			if (eatTargetPawn != null)
			{
				if (driver.LeanAndDelay(zombie, eatTargetPawn))
					return true;

				if (driver.EatBodyPart(zombie, eatTargetPawn))
					return true;
			}
			else
			{
				var zombieLeaner = zombie.Drawer.leaner as ZombieLeaner;
				if (zombieLeaner != null)
					zombieLeaner.extraOffset = Vector3.zero;
			}

			return false;
		}
		//
		static bool LeanAndDelay(this JobDriver_Stumble driver, Zombie zombie, Pawn eatTargetPawn)
		{
			if (driver.eatDelayCounter == 0)
			{
				if (eatTargetPawn != driver.lastEatTarget)
				{
					driver.lastEatTarget = eatTargetPawn;
					zombie.Drawer.rotator.FaceCell(driver.eatTarget.Position);
					var zombieLeaner = zombie.Drawer.leaner as ZombieLeaner;
					if (zombieLeaner != null)
					{
						var offset = (driver.eatTarget.Position.ToVector3() - zombie.Position.ToVector3()) * 0.5f;
						if (offset.magnitude < 1f)
							zombieLeaner.extraOffset = offset;
					}

					Tools.CastThoughtBubble(zombie, Constants.EATING);
				}
				CastEatingSound(zombie);
			}

			driver.eatDelayCounter++;
			if (driver.eatDelayCounter <= EatDelay(driver, zombie))
				return true;

			driver.eatDelayCounter = 0;
			zombie.raging = 0;
			return false;
		}
		//
		static bool EatBodyPart(this JobDriver_Stumble driver, Zombie zombie, Pawn eatTargetPawn)
		{
			var bodyPartRecord = FirstEatablePart(eatTargetPawn);
			if (bodyPartRecord == null)
			{
				driver.eatTarget.Destroy(DestroyMode.Vanish);
				return false;
			}

			var eatTargetAlive = driver.eatTarget is Pawn && ((Pawn)driver.eatTarget).Dead == false;
			var hediff_MissingPart = (Hediff_MissingPart)HediffMaker.MakeHediff(HediffDefOf.MissingBodyPart, eatTargetPawn, bodyPartRecord);
			hediff_MissingPart.lastInjury = HediffDefOf.Bite;
			hediff_MissingPart.IsFresh = true;
			eatTargetPawn.health.AddHediff(hediff_MissingPart, null, null);

			var eatTargetStillAlive = driver.eatTarget is Pawn && ((Pawn)driver.eatTarget).Dead == false;
			if (eatTargetAlive && eatTargetStillAlive == false)
			{
				if (PawnUtility.ShouldSendNotificationAbout(eatTargetPawn) && eatTargetPawn.RaceProps.Humanlike)
				{
					Messages.Message("MessageEatenByPredator".Translate(new object[]
					{
								driver.eatTarget.LabelShort,
								zombie.LabelIndefinite()
					}).CapitalizeFirst(), zombie, MessageSound.Negative);
				}

				eatTargetPawn.Strip();
			}

			return true;
		}

		// ==========================================================================================
		// calculate possible moves, sort by pheromone value and take top 3
		// then choose the one with the lowest zombie count
		// also, emit a circle of timestamps when discovering a pheromone
		// trace so nearby zombies pick it up too (leads to a chain reaction)
		//
		// returns true if zombies are non-busy anc can actually look
		// for things to smash
		//
		static int fadeOff = -1;
		static int agitatedFadeoff;
		static int checkSmashableFadeoff;
		public static bool Track(this JobDriver_Stumble driver, Zombie zombie, PheromoneGrid grid)
		{
			if (zombie.EveryNTick(NthTick.Every60) || fadeOff == -1)
			{
				fadeOff = Tools.PheromoneFadeoff();
				agitatedFadeoff = fadeOff / 4;
				checkSmashableFadeoff = agitatedFadeoff / 2;
			}

			var trackingMoves = new List<IntVec3>(8);
			var currentTicks = Tools.Ticks();
			var timeDelta = long.MaxValue;

			if (zombie.raging == 0)
			{
				for (var i = 0; i < 8; i++)
				{
					var pos = zombie.Position + GenAdj.AdjacentCells[i];
					if (currentTicks - grid.GetTimestamp(pos) < fadeOff && zombie.HasValidDestination(pos))
						trackingMoves.Add(pos);
				}
			}

			if (trackingMoves.Count > 0)
			{
				trackingMoves.Sort((p1, p2) => grid.GetTimestamp(p2).CompareTo(grid.GetTimestamp(p1)));
				trackingMoves = trackingMoves.Take(Constants.NUMBER_OF_TOP_MOVEMENT_PICKS).ToList();
				trackingMoves = trackingMoves.OrderBy(p => grid.GetZombieCount(p)).ToList();
				var nextMove = trackingMoves.First();
				timeDelta = currentTicks - (grid.GetTimestamp(nextMove));

				driver.destination = nextMove;
				if (zombie.state == ZombieState.Wandering)
				{
					Tools.ChainReact(zombie.Map, zombie.Position, nextMove);
					if (timeDelta <= agitatedFadeoff)
						CastBrainzThought(zombie);
				}
				zombie.state = ZombieState.Tracking;
			}

			if (driver.destination.IsValid == false)
				zombie.state = ZombieState.Wandering;

			var checkSmashable = timeDelta >= checkSmashableFadeoff;
			if (ZombieSettings.Values.smashOnlyWhenAgitated)
				checkSmashable &= (zombie.state == ZombieState.Tracking || zombie.raging > 0);

			return checkSmashable;
		}

		// smash nearby build stuff =================================================================
		//
		public static bool Smash(this JobDriver_Stumble driver, Zombie zombie, bool checkSmashable)
		{
			if (driver.destination.IsValid && checkSmashable == false)
				return false;

			var building = CanSmash(zombie);
			if (building == null)
				return false;

			driver.destination = building.Position;

			if (Constants.USE_SOUND)
			{
				var info = SoundInfo.InMap(building);
				SoundDef.Named("ZombieHit").PlayOneShot(info);
			}

			AttackThing(zombie, building, JobDefOf.AttackStatic);
			return true;
		}

		// calculate possible moves =================================================================
		//
		public static List<IntVec3> PossibleMoves(this JobDriver_Stumble driver, Zombie zombie)
		{
			if (driver.destination.IsValid)
				return new List<IntVec3>();

			var result = new List<IntVec3>(8);
			var pos = zombie.Position;
			foreach (var vec in GenAdj.AdjacentCells)
			{
				var cell = pos + vec;
				if (zombie.HasValidDestination(cell))
					result.Add(cell);
			}
			return result;
		}

		// use rage grid to get to colonists ========================================================
		//
		public static void RageMove(this JobDriver_Stumble driver, Zombie zombie, PheromoneGrid grid, List<IntVec3> possibleMoves)
		{
			if (zombie.raging <= 0)
				return;

			var info = Tools.wanderer.GetMapInfo(zombie.Map);
			driver.destination = info.GetParent(zombie.Position);
			if (driver.destination.IsValid == false)
			{
				zombie.raging = 0;
				return;
			}

			// if next move is on a door, end raging
			if (ZombieSettings.Values.smashMode == SmashMode.Nothing)
			{
				var door = driver.destination.GetEdifice(zombie.Map) as Building_Door;
				if (door != null && door.Open == false)
				{
					zombie.raging = 0;
					return;
				}
			}

			var destZombieCount = grid.GetZombieCount(driver.destination);
			if (destZombieCount > 0 || Rand.Chance(Constants.DIVERTING_FROM_RAGE))
			{
				var success = TryToDivert(ref driver.destination, grid, zombie.Position, possibleMoves);
				if (success == false)
				{
					var zCount = possibleMoves.Select(p => grid.GetZombieCount(p)).Min();
					driver.destination = possibleMoves.Where(p => grid.GetZombieCount(p) == zCount).RandomElement();
				}
			}
		}

		// during night, drift towards colony =======================================================
		//
		public static void Wander(this JobDriver_Stumble driver, Zombie zombie, PheromoneGrid grid, List<IntVec3> possibleMoves)
		{
			if (driver.destination.IsValid)
				return;

			// check for day/night and dust/dawn
			// during night, zombies drift towards the colonies center
			//
			if (zombie.Map.areaManager.Home[zombie.Position] == false)
			{
				var moveTowardsCenter = false;

				var hour = GenLocalDate.HourOfDay(Find.VisibleMap);
				if (hour < 12) hour += 24;
				if (hour > Constants.HOUR_START_OF_NIGHT && hour < Constants.HOUR_END_OF_NIGHT)
					moveTowardsCenter = true;
				else if (hour >= Constants.HOUR_START_OF_DUSK && hour <= Constants.HOUR_START_OF_NIGHT)
					moveTowardsCenter = Rand.RangeInclusive(hour, Constants.HOUR_START_OF_NIGHT) == Constants.HOUR_START_OF_NIGHT;
				else if (hour >= Constants.HOUR_END_OF_NIGHT && hour <= Constants.HOUR_START_OF_DAWN)
					moveTowardsCenter = Rand.RangeInclusive(Constants.HOUR_END_OF_NIGHT, hour) == Constants.HOUR_END_OF_NIGHT;

				if (moveTowardsCenter)
				{
					var center = zombie.wanderDestination.IsValid ? zombie.wanderDestination : zombie.Map.Center;
					possibleMoves.Sort((p1, p2) => p1.DistanceToSquared(center).CompareTo(p2.DistanceToSquared(center)));
					possibleMoves = possibleMoves.Take(Constants.NUMBER_OF_TOP_MOVEMENT_PICKS).ToList();
					possibleMoves = possibleMoves.OrderBy(p => grid.GetZombieCount(p)).ToList();
					driver.destination = possibleMoves.First();
					return;
				}
			}

			// random wandering
			var n = possibleMoves.Count();
			driver.destination = possibleMoves[Constants.random.Next(n)];
		}

		// if we have a valid destination, go there =================================================
		//
		public static void ExecuteMove(this JobDriver_Stumble driver, Zombie zombie, PheromoneGrid grid)
		{
			if (driver.destination.IsValid)
			{
				grid.ChangeZombieCount(zombie.lastGotoPosition, -1);
				grid.ChangeZombieCount(driver.destination, 1);
				zombie.lastGotoPosition = driver.destination;

				zombie.pather.StartPath(driver.destination, PathEndMode.OnCell);
			}
		}

		// check for tight groups of zombies ========================================================
		//
		public static void BeginRage(this JobDriver_Stumble driver, Zombie zombie, PheromoneGrid grid)
		{
			if (zombie.raging == 0 && ZombieSettings.Values.ragingZombies)
			{
				var count = CountSurroundingZombies(zombie.Position, zombie.Map, grid);
				if (count > Constants.SURROUNDING_ZOMBIES_TO_TRIGGER_RAGE)
					StartRage(zombie, count);
				return;
			}

			if (GenTicks.TicksAbs > zombie.raging || ZombieSettings.Values.ragingZombies == false)
				zombie.raging = 0;
		}

		// subroutines ==============================================================================

		static Thing CanIngest(Zombie zombie)
		{
			if (zombie.EveryNTick(NthTick.Every2) == false)
				return null;

			if (ZombieSettings.Values.zombiesEatDowned || ZombieSettings.Values.zombiesEatCorpses)
			{
				var enumerator = GetAdjacted<ThingWithComps>(zombie).GetEnumerator();
				while (enumerator.MoveNext())
				{
					var twc = enumerator.Current;

					var p = twc as Pawn;
					if (p != null && ZombieSettings.Values.zombiesEatDowned)
					{
						if (p.Spawned && p.RaceProps.IsFlesh && (p.Downed || p.Dead))
							return p;
					}

					var c = twc as Corpse;
					if (c != null && ZombieSettings.Values.zombiesEatCorpses)
					{
						if (c.Spawned && c.InnerPawn != null && c.InnerPawn.RaceProps.IsFlesh)
							return c;
					}
				}
			}
			return null;
		}

		static Thing CanAttack(Zombie zombie)
		{
			var mode = ZombieSettings.Values.attackMode;

			var enumerator = GetAdjacted<Pawn>(zombie).GetEnumerator();
			while (enumerator.MoveNext())
			{
				var target = enumerator.Current;
				if (target.Dead || target.Downed)
					continue;

				var distance = (target.DrawPos - zombie.DrawPos).MagnitudeHorizontalSquared();
				if (distance > Constants.MIN_ATTACKDISTANCE_SQUARED)
					continue;

				if (Tools.HasInfectionState(target, InfectionState.Infecting))
					continue;

				if (mode == AttackMode.Everything)
					return target;

				if (target.MentalState != null)
				{
					var msDef = target.MentalState.def;
					if (msDef == MentalStateDefOf.Manhunter || msDef == MentalStateDefOf.ManhunterPermanent)
						return target;
				}

				if (mode == AttackMode.OnlyHumans && target.RaceProps.Humanlike)
					return target;

				if (mode == AttackMode.OnlyColonists && target.IsColonist)
					return target;
			}
			return null;
		}

		static Building CanSmash(Zombie zombie)
		{
			if (zombie.EveryNTick(NthTick.Every15) == false)
				return null;

			var isSuicideBomber = zombie.bombTickingInterval != -1f;
			if (isSuicideBomber == false)
			{
				if (ZombieSettings.Values.smashMode == SmashMode.Nothing) return null;
				if (ZombieSettings.Values.smashOnlyWhenAgitated && zombie.state != ZombieState.Tracking && zombie.raging == 0) return null;
			}

			var nextIndex = Constants.random.Next(4);
			var c = adjIndex4[prevIndex4];
			adjIndex4[prevIndex4] = adjIndex4[nextIndex];
			adjIndex4[nextIndex] = c;
			prevIndex4 = nextIndex;

			var map = zombie.Map;
			var basePos = zombie.Position;
			var attackColonistsOnly = (ZombieSettings.Values.attackMode == AttackMode.OnlyColonists);
			var playerFaction = Faction.OfPlayer;

			if (ZombieSettings.Values.smashMode == SmashMode.DoorsOnly && isSuicideBomber == false)
			{
				for (var i = 0; i < 4; i++)
				{
					var pos = basePos + GenAdj.CardinalDirections[adjIndex4[i]];
					if (pos.InBounds(map) == false)
						continue;

					var door = pos.GetEdifice(map) as Building_Door;
					if (door != null && door.Open == false && (attackColonistsOnly == false || door.Faction == playerFaction))
						return door;
				}
			}

			if (ZombieSettings.Values.smashMode == SmashMode.AnyBuilding || isSuicideBomber)
			{
				var grid = map.thingGrid;
				for (var i = 0; i < 4; i++)
				{
					var pos = basePos + GenAdj.CardinalDirections[adjIndex4[i]];
					if (pos.InBounds(map) == false)
						continue;

					foreach (var thing in grid.ThingsListAtFast(pos))
					{
						var building = thing as Building;
						if (building == null)
							continue;

						var buildingDef = building.def;
						var factionCondition = (attackColonistsOnly == false || building.Faction == playerFaction);
						if (buildingDef.useHitPoints && buildingDef.building.isNaturalRock == false && factionCondition)
						{
							if (isSuicideBomber)
							{
								zombie.bombWillGoOff = true;
								return null;
							}

							return building;
						}
					}
				}
			}

			return null;
		}

		// helpers ==================================================================================

		static void CastEatingSound(Zombie zombie)
		{
			if (Constants.USE_SOUND)
			{
				var info = SoundInfo.InMap(new TargetInfo(zombie.Position, zombie.Map, false));
				SoundDef.Named("ZombieEating").PlayOneShot(info);
			}
		}

		static void CastBrainzThought(Pawn pawn)
		{
			Tools.CastThoughtBubble(pawn, Constants.BRRAINZ);

			if (Constants.USE_SOUND)
			{
				var info = SoundInfo.InMap(new TargetInfo(pawn.Position, pawn.Map, false));
				SoundDef.Named("ZombieTracking").PlayOneShot(info);
			}
		}

		static int EatDelay(this JobDriver_Stumble driver, Zombie zombie)
		{
			if (driver.eatDelay == 0)
			{
				driver.eatDelay = Constants.EAT_DELAY_TICKS;
				switch (zombie.story.bodyType)
				{
					case BodyType.Thin:
						driver.eatDelay *= 3;
						break;
					case BodyType.Hulk:
						driver.eatDelay /= 4;
						break;
					case BodyType.Fat:
						driver.eatDelay = (int)(driver.eatDelay / 1.5f);
						break;
				}
			}
			return driver.eatDelay;
		}

		static BodyPartRecord FirstEatablePart(Pawn eatSubject)
		{
			if (eatSubject == null || eatSubject.health == null || eatSubject.health.hediffSet == null) return null;
			return eatSubject.health.hediffSet
						.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Undefined)
						.Where(new Func<BodyPartRecord, bool>(r => r.depth == BodyPartDepth.Outside))
						.InRandomOrder()
						.FirstOrDefault();
		}

		static IEnumerable<T> GetAdjacted<T>(Pawn pawn) where T : ThingWithComps
		{
			var nextIndex = Constants.random.Next(8);
			var c = adjIndex8[prevIndex8];
			adjIndex8[prevIndex8] = adjIndex8[nextIndex];
			adjIndex8[nextIndex] = c;
			prevIndex8 = nextIndex;

			var grid = pawn.Map.thingGrid;
			var basePos = pawn.Position;
			for (var i = 0; i < 8; i++)
			{
				var pos = basePos + GenAdj.AdjacentCells[adjIndex8[i]];
				var enumerator = grid.ThingsAt(pos).GetEnumerator();
				while (enumerator.MoveNext())
				{
					var t = enumerator.Current as T;
					if (t != null && (t is Zombie) == false && (t is ZombieCorpse) == false)
						yield return t;
				}
			}
		}

		static void AttackThing(Zombie zombie, Thing thing, JobDef def)
		{
			var job = new Job(def, thing)
			{
				maxNumMeleeAttacks = 1,
				maxNumStaticAttacks = 1,
				expiryInterval = 600,
				canBash = false,
				attackDoorIfTargetLost = false,
				ignoreForbidden = false,
				locomotionUrgency = LocomotionUrgency.Amble
			};

			zombie.jobs.StartJob(job, JobCondition.Succeeded, null, true, false, null, null);
		}

		static int CountSurroundingZombies(IntVec3 pos, Map map, PheromoneGrid grid)
		{
			return GenAdj.AdjacentCellsAndInside.Select(vec => pos + vec)
				.Select(c => grid.GetZombieCount(c)).Sum();
		}

		static void StartRage(Zombie zombie, int count)
		{
			zombie.raging = GenTicks.TicksAbs + (int)(GenDate.TicksPerHour * Rand.Range(1f, 8f));
			Tools.CastThoughtBubble(zombie, Constants.RAGING);

			if (Constants.USE_SOUND)
			{
				var info = SoundInfo.InMap(zombie);
				SoundDef.Named("ZombieRage").PlayOneShot(info);
			}
		}

		static bool TryToDivert(ref IntVec3 destination, PheromoneGrid grid, IntVec3 basePos, List<IntVec3> possibleMoves)
		{
			var forward = destination - basePos;
			var rotation = Rand.Value > 0.5 ? Rot4.East : Rot4.West;
			var divert = basePos + forward.RotatedBy(rotation);
			if (possibleMoves.Contains(divert) && grid.GetZombieCount(divert) == 0)
			{
				destination = divert;
				return true;
			}

			rotation = rotation == Rot4.East ? Rot4.West : Rot4.East;
			divert = basePos + forward.RotatedBy(rotation);
			if (possibleMoves.Contains(divert) && grid.GetZombieCount(divert) == 0)
			{
				destination = divert;
				return true;
			}

			var zombieFreePossibleMoves = possibleMoves.Where(cell => grid.GetZombieCount(cell) == 0).ToArray();
			var n = zombieFreePossibleMoves.Length;
			if (n > 0)
			{
				destination = zombieFreePossibleMoves[Constants.random.Next(n)];
				return true;
			}

			return false;
		}
	}
}