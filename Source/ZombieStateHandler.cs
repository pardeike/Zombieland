using HarmonyLib;
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

		static readonly float combatExtendedHealAmount = 1f / 1f.SecondsToTicks();

		static float lastTrackingSoundPlayed = 0f;
		static float lastRageSoundPlayed = 0f;

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

			if (zombie.IsSuicideBomber)
			{
				if (zombie.bombWillGoOff && zombie.EveryNTick(NthTick.Every10))
					zombie.bombTickingInterval -= 1f + Tools.Difficulty();
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
					if (zombie.hasTankySuit <= 0f && zombie.health.hediffSet.GetHediffs<Hediff_Injury>().Any())
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
		public static bool Downed(Zombie zombie)
		{
			if (zombie.IsDowned() == false)
				return false;

			if (ZombieSettings.Values.zombiesDieVeryEasily || zombie.IsSuicideBomber || ZombieSettings.Values.doubleTapRequired == false)
			{
				zombie.Kill(null);
				return true;
			}

			var walkCapacity = PawnCapacityUtility.CalculateCapacityLevel(zombie.health.hediffSet, PawnCapacityDefOf.Moving);
			var missingBrain = zombie.health.hediffSet.GetBrain() == null;
			if (walkCapacity < 0.25f || missingBrain)
			{
				zombie.Kill(null);
				return true;
			}

			if (++zombie.healCounter >= Constants.ZOMBIE_HEALING_TICKS)
			{
				zombie.healCounter = 0;

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

			return false;
		}

		// handle things that affect zombies ====================================================================
		//
		public static void Affects(Zombie zombie)
		{
			if (zombie.EveryNTick(NthTick.Every60) == false)
				return;

			if (zombie.HasAttachment(ThingDefOf.Fire))
				return;

			var temp = GenTemperature.GetTemperatureForCell(zombie.Position, zombie.Map);
			if (temp >= 200f)
				FireUtility.TryAttachFire(zombie, GenMath.LerpDoubleClamped(200f, 1000f, 0.01f, 1f, temp));
		}

		// invalidate destination if necessary ======================================================
		//
		public static bool ValidDestination(this JobDriver_Stumble driver, Zombie zombie)
		{
			// find out if we still need to check for 0,0 as an invalid location
			if (driver.destination.x == 0 && driver.destination.z == 0)
				driver.destination = IntVec3.Invalid;
			return zombie.HasValidDestination(driver.destination);
		}

		// attack nearby enemies ====================================================================
		//
		public static bool Attack(this JobDriver_Stumble driver, Zombie zombie)
		{
			if (zombie.Downed && ZombieSettings.Values.doubleTapRequired == false)
				return false;

			var enemy = CanAttack(zombie);
			if (enemy == null)
				return false;

			driver.destination = enemy.Position;

			zombie.state = ZombieState.Tracking;
			if (Constants.USE_SOUND && Prefs.VolumeAmbient > 0f)
			{
				var info = SoundInfo.InMap(enemy);
				CustomDefs.ZombieHit.PlayOneShot(info);
			}

			AttackThing(zombie, enemy, JobDefOf.AttackMelee);
			return true;
		}

		// electrify nearby stuff ====================================================================
		//
		static readonly FastInvokeHandler _DrainBatteriesAndCauseExplosion = MethodInvoker.GetHandler(AccessTools.Method(typeof(ShortCircuitUtility), "DrainBatteriesAndCauseExplosion"));
		public static void Electrify(Zombie zombie)
		{
			var buildings = GetAdjacted<ThingWithComps>(zombie).OfType<Building>();
			foreach (var building in buildings)
			{
				var powerNet = building?.PowerComp?.PowerNet;
				if (powerNet != null && building.IsBurning() == false)
				{
					_ = MoteMaker.MakeStaticMote(building.TrueCenter(), building.Map, ThingDefOf.Mote_ExplosionFlash, 12f);
					MoteMaker.ThrowDustPuff(building.TrueCenter(), building.Map, Rand.Range(0.8f, 1.2f));

					if (powerNet.batteryComps.Any((CompPowerBattery x) => x.StoredEnergy > 20f))
					{
						var arguments = new object[] { powerNet, building, 0f, 0f };
						_ = _DrainBatteriesAndCauseExplosion(null, arguments);
					}
					else
						_ = FireUtility.TryStartFireIn(building.Position, building.Map, Rand.Range(0.1f, 1.75f));

					return;
				}
			}
		}

		// lean in and eat bodies made out of flesh =================================================
		//
		public static bool Eat(this JobDriver_Stumble driver, Zombie zombie, PheromoneGrid grid)
		{
			if (zombie.hasTankyShield != -1f || zombie.hasTankyHelmet != -1f || zombie.hasTankySuit != -1f)
				return false;

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
				if (zombie.Drawer.leaner is ZombieLeaner zombieLeaner)
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
					zombie.rotationTracker.FaceCell(driver.eatTarget.Position);
					if (zombie.Drawer.leaner is ZombieLeaner zombieLeaner)
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

			var eatTargetAlive = driver.eatTarget is Pawn eatTarget1 && eatTarget1.Dead == false;
			var hediff_MissingPart = (Hediff_MissingPart)HediffMaker.MakeHediff(HediffDefOf.MissingBodyPart, eatTargetPawn, bodyPartRecord);
			hediff_MissingPart.lastInjury = HediffDefOf.Bite;
			hediff_MissingPart.IsFresh = true;
			eatTargetPawn.health.AddHediff(hediff_MissingPart, null, null);

			var eatTargetStillAlive = driver.eatTarget is Pawn eatTarget2 && eatTarget2.Dead == false;
			if (eatTargetAlive && eatTargetStillAlive == false)
			{
				if (PawnUtility.ShouldSendNotificationAbout(eatTargetPawn) && eatTargetPawn.RaceProps.Humanlike)
				{
					var msg = "MessageEatenByPredator".Translate(new NamedArgument(driver.eatTarget.LabelShort, null), zombie.LabelIndefinite().Named("PREDATOR"), driver.eatTarget.Named("EATEN"));
					Messages.Message(msg.CapitalizeFirst(), zombie, MessageTypeDefOf.NegativeEvent);
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
		// returns true if zombies are non-busy and can actually look
		// for things to smash
		//
		static int fadeOff = -1;
		static int wasColonistFadeoff;
		static int agitatedFadeoff;
		static int checkSmashableFadeoff1;
		static int checkSmashableFadeoff2;
		public static bool Track(this JobDriver_Stumble driver, Zombie zombie, PheromoneGrid grid)
		{
			if (zombie.EveryNTick(NthTick.Every60) || fadeOff == -1)
			{
				fadeOff = Tools.PheromoneFadeoff();
				wasColonistFadeoff = fadeOff / 6;
				agitatedFadeoff = fadeOff / 4;
				checkSmashableFadeoff1 = agitatedFadeoff / 4;
				checkSmashableFadeoff2 = agitatedFadeoff * 3 / 4;
			}

			var trackingMoves = new List<IntVec3>(8);
			var currentTicks = Tools.Ticks();
			var timeDelta = long.MaxValue;

			var fmin = long.MaxValue;
			if (zombie.raging == 0)
			{
				for (var i = 0; i < 8; i++)
				{
					var pos = zombie.Position + GenAdj.AdjacentCells[i];
					if (zombie.HasValidDestination(pos))
					{
						var f = zombie.wasMapPawnBefore ? wasColonistFadeoff : fadeOff;
						var tdiff = currentTicks - grid.GetTimestamp(pos);
						fmin = Math.Min(fmin, tdiff);
						if (tdiff < f)
							trackingMoves.Add(pos);
					}
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

			if (zombie.wasMapPawnBefore)
				return true;

			var checkSmashable = timeDelta >= checkSmashableFadeoff1 && timeDelta < checkSmashableFadeoff2;
			if (ZombieSettings.Values.smashOnlyWhenAgitated)
				checkSmashable &= (zombie.state == ZombieState.Tracking || zombie.raging > 0);

			return checkSmashable;
		}

		// smash nearby build stuff =================================================================
		//
		public static bool Smash(this JobDriver_Stumble driver, Zombie zombie, bool checkSmashable, bool onylWhenNotRaging)
		{
			if (zombie.wasMapPawnBefore == false && zombie.IsSuicideBomber == false && zombie.IsTanky == false)
			{
				if (driver.destination.IsValid && checkSmashable == false)
					return false;

				if (onylWhenNotRaging && zombie.raging > 0)
					return false;
			}

			var building = CanSmash(zombie);
			if (building == null)
				return false;

			driver.destination = building.Position;

			if (Constants.USE_SOUND && Prefs.VolumeAmbient > 0f)
			{
				var info = SoundInfo.InMap(building);
				CustomDefs.ZombieHit.PlayOneShot(info);
			}

			AttackThing(zombie, building, JobDefOf.AttackStatic);
			return true;
		}

		// mine mountains ===========================================================================
		//
		static readonly Effecter effecter = EffecterDefOf.Mine.Spawn();
		public static bool Mine(this JobDriver_Stumble driver, Zombie zombie, bool allDirections = false)
		{
			_ = driver;

			if (zombie.miningCounter > 0)
			{
				zombie.miningCounter--;
				return true;
			}

			var map = zombie.Map;
			var basePos = zombie.Position;

			var delta = (zombie.wanderDestination.IsValid ? zombie.wanderDestination : zombie.Map.Center) - basePos;
			var idx = Tools.CellsAroundIndex(delta);
			if (idx == -1)
				return false;
			var adjacted = GenAdj.AdjacentCellsAround;
			var cells = allDirections ? adjacted.ToList() : new List<IntVec3>() { adjacted[idx], adjacted[(idx + 1) % 8], adjacted[(idx + 7) % 8] };

			var mineable = cells
				.Select(c => basePos + c)
				.Where(c => c.InBounds(map))
				.Select(c => c.GetFirstThing<Mineable>(map))
				.FirstOrDefault();
			if (mineable == null)
				return false;

			zombie.rotationTracker.FaceCell(mineable.Position);
			effecter.Trigger(zombie, mineable);
			var baseDamage = (int)GenMath.LerpDoubleClamped(0, 5, 1, 10, Tools.Difficulty());
			var damage = (!mineable.def.building.isNaturalRock) ? baseDamage : baseDamage * 2;
			if (mineable.HitPoints > damage)
				_ = mineable.TakeDamage(new DamageInfo(DamageDefOf.Mining, damage));
			else
				mineable.Destroy(DestroyMode.KillFinalize);

			zombie.miningCounter = (int)GenMath.LerpDoubleClamped(0, 5, 180, 90, Tools.Difficulty());
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
		public static bool RageMove(this JobDriver_Stumble driver, Zombie zombie, PheromoneGrid grid, List<IntVec3> possibleMoves, bool checkSmashable)
		{
			var info = ZombieWanderer.GetMapInfo(zombie.Map);
			var newPos = info.GetParent(zombie.Position, false);

			if (newPos.IsValid == false)
			{
				// tanky can get directly through walls
				if (zombie.IsTanky)
					newPos = info.GetParent(zombie.Position, true);

				if (newPos.IsValid == false)
				{
					// no next move available
					zombie.raging = 0;
					return Smash(driver, zombie, checkSmashable, false);
				}
			}

			// next tanky move is on a building
			if (newPos.GetEdifice(zombie.Map) is Building building && (building as Mineable) == null)
				return Smash(driver, zombie, checkSmashable, false);

			// next move is on a door
			if (newPos.GetEdifice(zombie.Map) is Building_Door door)
			{
				if (door.Open)
				{
					driver.destination = newPos;
					return false;
				}
				return Smash(driver, zombie, checkSmashable, false);
			}

			// move into places where there is max 0/1 zombie already
			var destZombieCount = grid.GetZombieCount(newPos);
			if (destZombieCount < (zombie.IsTanky ? 1 : 2))
			{
				driver.destination = newPos;
				return false;
			}

			// cannot move? lets smash things
			if (Smash(driver, zombie, checkSmashable, false))
				return true;

			// cannot smash? look for alternative ways to move orthogonal
			if (TryToDivert(ref newPos, grid, zombie.Position, possibleMoves))
			{
				driver.destination = newPos;
				return false;
			}

			// move to least populated place
			var zCount = possibleMoves.Select(p => grid.GetZombieCount(p)).Min();
			driver.destination = possibleMoves.Where(p => grid.GetZombieCount(p) == zCount).SafeRandomElement();
			return false;
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

				var hour = GenLocalDate.HourOfDay(zombie.Map);
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
		static readonly int[] rageLevels = new int[] { 40, 32, 21, 18, 12 };
		public static void BeginRage(Zombie zombie, PheromoneGrid grid)
		{
			if (zombie.IsTanky || zombie.isAlbino || zombie.isDarkSlimer) return;

			if (zombie.raging == 0 && ZombieSettings.Values.ragingZombies)
			{
				var count = CountSurroundingZombies(zombie.Position, grid);
				if (count >= rageLevels[ZombieSettings.Values.zombieRageLevel - 1])
					StartRage(zombie);
				return;
			}

			if (GenTicks.TicksAbs > zombie.raging || ZombieSettings.Values.ragingZombies == false)
				zombie.raging = 0;
		}

		public static void CheckEndRage(Zombie zombie)
		{
			if (zombie.raging == 0)
				return;

			if (zombie.isAlbino || zombie.isDarkSlimer || GenTicks.TicksAbs > zombie.raging || ZombieSettings.Values.ragingZombies == false)
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

					if (twc is Pawn p && ZombieSettings.Values.zombiesEatDowned)
					{
						if (p.Spawned && p.RaceProps.IsFlesh && AlienTools.IsFleshPawn(p) && (p.IsDowned() || p.Dead))
							return p;
					}

					if (twc is Corpse c && ZombieSettings.Values.zombiesEatCorpses)
					{
						if (c.Spawned && c.InnerPawn != null && c.InnerPawn.RaceProps.IsFlesh && AlienTools.IsFleshPawn(c.InnerPawn))
							return c;
					}
				}
			}
			return null;
		}

		static Thing CanAttack(Zombie zombie)
		{
			var mode = ZombieSettings.Values.attackMode;

			var targets = GetAdjacted<Pawn>(zombie).ToList();
			foreach (var target in targets)
			{
				if (target.Dead || target.IsDowned())
					continue;

				// if (TouchPathEndModeUtility.IsAdjacentOrInsideAndAllowedToTouch(zombie.Position, target.Position, zombie.Map) == false)
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

				if (mode == AttackMode.OnlyHumans && target.RaceProps.Humanlike && target.RaceProps.IsFlesh && AlienTools.IsFleshPawn(target))
					return target;

				if (mode == AttackMode.OnlyColonists && target.IsColonist)
					return target;
			}
			return null;
		}

		static Building CanSmash(Zombie zombie)
		{
			var map = zombie.Map;
			var basePos = zombie.Position;
			var attackColonistsOnly = (ZombieSettings.Values.attackMode == AttackMode.OnlyColonists);
			var playerFaction = Faction.OfPlayer;

			if (zombie.IsTanky)
			{
				var info = ZombieWanderer.GetMapInfo(map);
				var pos = info.GetParent(basePos, false);
				if (pos.IsValid == false)
					pos = info.GetParent(basePos, true);
				if (pos.IsValid && pos.GetEdifice(zombie.Map) is Building building && (building as Mineable) == null && (attackColonistsOnly == false || building.Faction == playerFaction))
					return building;
				return null;
			}

			if (zombie.IsSuicideBomber == false && zombie.IsTanky == false && zombie.wasMapPawnBefore == false)
			{
				if (ZombieSettings.Values.smashMode == SmashMode.Nothing) return null;
				if (ZombieSettings.Values.smashOnlyWhenAgitated && zombie.state != ZombieState.Tracking && zombie.raging == 0) return null;
			}

			var nextIndex = Constants.random.Next(4);
			var c = adjIndex4[prevIndex4];
			adjIndex4[prevIndex4] = adjIndex4[nextIndex];
			adjIndex4[nextIndex] = c;
			prevIndex4 = nextIndex;

			if (ZombieSettings.Values.smashMode == SmashMode.DoorsOnly && zombie.IsSuicideBomber == false)
			{
				for (var i = 0; i < 4; i++)
				{
					var pos = basePos + GenAdj.CardinalDirections[adjIndex4[i]];
					if (pos.InBounds(map) == false)
						continue;

					if (pos.GetEdifice(map) is Building_Door door && door.Open == false && (attackColonistsOnly == false || door.Faction == playerFaction))
						return door;
				}
			}

			if (ZombieSettings.Values.smashMode == SmashMode.AnyBuilding || zombie.IsSuicideBomber || zombie.IsTanky)
			{
				var grid = map.thingGrid;
				for (var i = 0; i < 4; i++)
				{
					var pos = basePos + GenAdj.CardinalDirections[adjIndex4[i]];
					if (pos.InBounds(map) == false)
						continue;

					foreach (var thing in grid.ThingsListAtFast(pos))
					{
						if (!(thing is Building building) || (building as Mineable) != null)
							continue;

						var buildingDef = building.def;
						var factionCondition = (attackColonistsOnly == false || building.Faction == playerFaction);
						if (buildingDef.useHitPoints && buildingDef.building.isNaturalRock == false && factionCondition)
						{
							if (zombie.IsSuicideBomber)
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
			if (Constants.USE_SOUND && Prefs.VolumeAmbient > 0f)
			{
				var map = zombie.Map;
				if (map != null)
				{
					var info = SoundInfo.InMap(new TargetInfo(zombie.Position, map, false));
					CustomDefs.ZombieEating.PlayOneShot(info);
				}
			}
		}

		static void CastBrainzThought(Pawn pawn)
		{
			Tools.CastThoughtBubble(pawn, Constants.BRRAINZ);

			if (Constants.USE_SOUND && Prefs.VolumeAmbient > 0f)
			{
				var map = pawn.Map;
				if (map != null)
				{
					var timeNow = Time.realtimeSinceStartup;
					if (timeNow > lastTrackingSoundPlayed + 2f)
					{
						lastTrackingSoundPlayed = timeNow;

						var info = SoundInfo.InMap(new TargetInfo(pawn.Position, map, false));
						CustomDefs.ZombieTracking.PlayOneShot(info);
					}
				}
			}
		}

		static int EatDelay(this JobDriver_Stumble driver, Zombie zombie)
		{
			if (driver.eatDelay == 0)
			{
				driver.eatDelay = Constants.EAT_DELAY_TICKS;
				var bodyType = zombie.story.bodyType;
				if (bodyType == BodyTypeDefOf.Thin)
					driver.eatDelay *= 3;
				else if (bodyType == BodyTypeDefOf.Hulk)
					driver.eatDelay /= 2;
				else if (bodyType == BodyTypeDefOf.Fat)
					driver.eatDelay /= 4;
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
					if (enumerator.Current is T t && (t is Zombie) == false && (t is ZombieCorpse) == false)
						yield return t;
				}
			}
		}

		static void AttackThing(Zombie zombie, Thing thing, JobDef def)
		{
			var job = JobMaker.MakeJob(def, thing);
			job.maxNumMeleeAttacks = 1;
			job.maxNumStaticAttacks = 1;
			job.expiryInterval = 600;
			job.canBash = true;
			zombie.jobs.StartJob(job, JobCondition.Succeeded, null, true, false, null, null);
		}

		static int CountSurroundingZombies(IntVec3 pos, PheromoneGrid grid)
		{
			return GenAdj.AdjacentCellsAndInside.Select(vec => pos + vec)
				.Select(c => grid.GetZombieCount(c)).Sum();
		}

		static readonly float[] minRageLength = new float[] { 0.1f, 0.2f, 0.5f, 1f, 2f };
		static readonly float[] maxRageLength = new float[] { 1f, 2f, 4f, 6f, 8f };
		public static void StartRage(Zombie zombie)
		{
			var min = minRageLength[ZombieSettings.Values.zombieRageLevel - 1];
			var max = maxRageLength[ZombieSettings.Values.zombieRageLevel - 1];
			zombie.raging = GenTicks.TicksAbs + (int)(GenDate.TicksPerHour * Rand.Range(min, max));
			Tools.CastThoughtBubble(zombie, Constants.RAGING);

			if (Constants.USE_SOUND && Prefs.VolumeAmbient > 0f)
			{
				var timeNow = Time.realtimeSinceStartup;
				if (timeNow > lastRageSoundPlayed + 3f)
				{
					lastRageSoundPlayed = timeNow;

					var info = SoundInfo.InMap(zombie);
					CustomDefs.ZombieRage.PlayOneShot(info);
				}
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
