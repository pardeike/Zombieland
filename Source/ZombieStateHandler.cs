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
	// all routines here returning a boolean stop the code flow by returning TRUE
	//
	public static class ZombieStateHandler
	{
		static readonly int[] adjIndex4 = { 0, 1, 2, 3 };
		static int prevIndex4;

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

			var tick10 = zombie.EveryNTick(NthTick.Every10);
			if (zombie.IsSuicideBomber)
			{
				if (zombie.bombWillGoOff && tick10)
					zombie.bombTickingInterval -= 1f + Tools.Difficulty();
				if (zombie.bombTickingInterval <= 0f)
				{
					zombie.Kill(null);
					return true;
				}
			}

			if (tick10)
			{
				if (ZombieSettings.Values.zombiesDieVeryEasily)
				{
					if (zombie.hasTankySuit <= 0f && zombie.HasHediff<Hediff_Injury>())
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

		// handle wall pushed zombies ===============================================================
		//

		public static bool WallPushing(Zombie zombie)
		{
			const float progressDelta = 0.01f;

			if (zombie.wallPushProgress < 0f)
				return false;

			if (zombie.wallPushProgress > (1f - progressDelta))
			{
				zombie.Position = zombie.wallPushDestination.ToIntVec3();
				zombie.wallPushProgress = -1f;
				zombie.wallPushStart = Vector3.zero;
				zombie.wallPushDestination = Vector3.zero;
				zombie.Notify_Teleported(false, false);

				zombie.Map.roofGrid.SetRoof(zombie.Position, null);

				return false;
			}

			zombie.wallPushProgress += progressDelta;
			return true;
		}

		static readonly IntVec3[] pushDirections = new IntVec3[] { new IntVec3(0, 0, 1), new IntVec3(0, 0, -1), new IntVec3(1, 0, 0), new IntVec3(-1, 0, 0) };
		public static bool CheckWallPushing(Zombie zombie, PheromoneGrid grid)
		{
			var minimum = ZombieSettings.Values.minimumZombiesForWallPushing;
			if (zombie.wallPushProgress >= 0f || minimum == 0)
				return false;

			var pos = zombie.Position;
			var map = zombie.Map;

			var totalZombies = grid.GetZombieCount(pos);
			var wallCount = 0;
			IntVec3 wallCell = IntVec3.Invalid;
			for (var i = 0; i < 4; i++)
			{
				var adjacent = pos + pushDirections[i];
				if (adjacent.InBounds(map) == false)
					continue;

				totalZombies += grid.GetZombieCount(adjacent);
				if (adjacent.IsWallOrDoor(map))
				{
					wallCell = adjacent;
					wallCount++;
				}
			}
			if (wallCount == 1)
				totalZombies += 4;

			if (totalZombies < minimum)
			{
				var diff = 3 - (minimum - totalZombies);
				if (diff >= 0)
					Tools.CastBumpMote(map, pos.ToVector3Shifted(), diff);
				return false;
			}

			if (wallCount != 1)
				return false;

			var destination = wallCell + wallCell - pos;
			if (destination.WalkableBy(map, zombie) == false)
				return false;

			var roof = zombie.Map.roofGrid.RoofAt(destination);
			if (roof == RoofDefOf.RoofRockThick || roof == RoofDefOf.RoofRockThin)
				return false;

			var tickManager = Find.CurrentMap.GetComponent<TickManager>();
			if (tickManager.allZombiesCached.Any(z => z.Position == destination))
				return false;

			zombie.wallPushProgress = 0f;
			zombie.wallPushStart = pos.ToVector3Shifted();
			zombie.wallPushDestination = destination.ToVector3Shifted();
			if (Constants.USE_SOUND)
				CustomDefs.WallPushing.PlayOneShot(SoundInfo.InMap(new TargetInfo(pos, map)));
			if (ZombieSettings.Values.dangerousSituationMessage)
				if ("DangerousSituation".RunThrottled(5f))
				{
					var text = "ZombiesAreBeingPushedOverYourWalls".Translate();
					Find.LetterStack.ReceiveLetter("DangerousSituation".Translate(), text, CustomDefs.DangerousSituation, zombie);
				}
			return true;
		}

		// handle roped zombies =====================================================================
		//
		public static bool Roping(this JobDriver_Stumble driver, Zombie zombie)
		{
			var master = zombie.ropedBy;
			if (master == null)
				return false;

			if (master.Drafted == false || master.IsColonistPlayerControlled == false)
			{
				zombie.Unrope();
				return false;
			}

			if (zombie.RopingFactorTo(master) > 1)
			{
				zombie.Unrope();
				return false;
			}

			if (zombie.EveryNTick(NthTick.Every45))
				_ = HealthUtility.FixWorstHealthCondition(zombie);

			driver.destination = IntVec3.Invalid;
			var possibleMoves = PossibleMoves(driver, zombie);
			var destination = master.Position;
			possibleMoves.Sort((p1, p2) => p1.DistanceToSquared(destination).CompareTo(p2.DistanceToSquared(destination)));
			var newCell = possibleMoves.FirstOrDefault();
			if (newCell != destination)
				driver.destination = newCell;
			return true;
		}

		// handle downed zombies ====================================================================
		//
		public static bool DownedOrUnconsciousness(Zombie zombie)
		{
			if (zombie.paralyzedUntil > 0)
			{
				if (GenTicks.TicksAbs < zombie.paralyzedUntil)
					return true;
				zombie.paralyzedUntil = 0;
			}

			if (zombie.IsTanky == false)
			{
				zombie.consciousness = zombie.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness);
				if (zombie.consciousness <= Constants.MIN_CONSCIOUSNESS)
				{
					if (zombie.EveryNTick(NthTick.Every960))
					{
						if (ZombieSettings.Values.doubleTapRequired && ZombieSettings.Values.zombiesDieVeryEasily == false)
						{
							var injury = zombie.health.hediffSet.GetInjuriesTendable().SafeRandomElement();
							if (injury != null)
								zombie.health.RemoveHediff(injury);
							else
							{
								var bleeding = zombie.health.hediffSet.hediffs.Where(hediff => hediff.def == HediffDefOf.BloodLoss).SafeRandomElement();
								if (bleeding != null)
									zombie.health.RemoveHediff(bleeding);
							}
						}
					}
					return zombie.ropedBy == null;
				}
			}

			var wasHealing = zombie.isHealing;
			if (zombie.health.Downed && zombie.isHealing == false)
				zombie.isHealing = true;

			if (zombie.Downed)
			{
				if (ZombieSettings.Values.zombiesDieVeryEasily || zombie.IsSuicideBomber || ZombieSettings.Values.doubleTapRequired == false)
				{
					zombie.Kill(null);
					return true;
				}
			}

			if (zombie.isHealing == false || zombie.stances.stunner.Stunned || zombie.IsBurning())
				return false;

			if ((wasHealing == false && zombie.isHealing) || zombie.EveryNTick(NthTick.Every480))
			{
				var injury = zombie.health.hediffSet.hediffs.Where(hediff => hediff is Hediff_Injury injury && injury.IsPermanent() == false).SafeRandomElement();
				if (injury != null)
					_ = HealthUtility.Cure(injury);
			}
			return false;
		}

		// handle things that affect zombies ====================================================================
		//
		public static void ApplyFire(Zombie zombie)
		{
			if (zombie.isOnFire || zombie.EveryNTick(NthTick.Every50) == false)
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
		public static void Electrify(Zombie zombie)
		{
			zombie.PerformOnAdjacted(thing =>
			{
				if (thing is Building building)
				{
					var powerNet = building?.PowerComp?.PowerNet;
					if (powerNet != null && building.IsBurning() == false)
					{
						FleckMaker.Static(building.TrueCenter(), building.Map, FleckDefOf.ExplosionFlash, 12f);
						FleckMaker.ThrowDustPuff(building.TrueCenter(), building.Map, Rand.Range(0.8f, 1.2f));

						if (powerNet.batteryComps.Any((CompPowerBattery x) => x.StoredEnergy > 20f))
						{
							ShortCircuitUtility.DrainBatteriesAndCauseExplosion(powerNet, building, out var _1, out var _2);
							zombie.DisableElectric(GenDate.TicksPerHour / 2);
						}
						else
						{
							_ = FireUtility.TryStartFireIn(building.Position, building.Map, Rand.Range(0.1f, 1.75f));
							zombie.DisableElectric(GenDate.TicksPerHour / 4);
						}

						return true;
					}
				}
				return false;
			});
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

		public struct TrackMove
		{
			public IntVec3 pos;
			public long tstamp;
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

			var currentFadeoff = zombie.wasMapPawnBefore ? wasColonistFadeoff : fadeOff;
			var currentTicks = Tools.Ticks();
			var treshhold = currentTicks - currentFadeoff;

			var topTrackingMoves = zombie.topTrackingMoves;
			var topTrackingMovesCount = 0;

			var zPos = zombie.Position;
			if (zombie.raging == 0)
			{
				for (var i = 0; i < 8; i++)
				{
					var pos = zPos + GenAdj.AdjacentCells[i];
					if (zombie.HasValidDestination(pos))
					{
						var tstamp = grid.GetTimestamp(pos);
						if (treshhold < tstamp)
						{
							for (var j = 0; j < Constants.NUMBER_OF_TOP_MOVEMENT_PICKS; j++)
								if (j >= topTrackingMovesCount || tstamp > topTrackingMoves[j].tstamp)
								{
									for (var k = Constants.NUMBER_OF_TOP_MOVEMENT_PICKS - 1; k >= j + 1; k--)
										topTrackingMoves[k] = topTrackingMoves[k - 1];
									topTrackingMoves[j].pos = pos;
									topTrackingMoves[j].tstamp = tstamp;
									if (topTrackingMovesCount < Constants.NUMBER_OF_TOP_MOVEMENT_PICKS)
										topTrackingMovesCount++;
									break;
								}
						}
					}
				}
			}

			var timeDelta = long.MaxValue;
			if (topTrackingMovesCount > 0)
			{
				var minZombieCount = int.MaxValue;
				var nextMove = IntVec3.Invalid;
				for (var i = 0; i < topTrackingMovesCount; i++)
				{
					var pos = topTrackingMoves[i].pos;
					var count = grid.GetZombieCount(pos);
					if (count < minZombieCount)
					{
						nextMove = pos;
						minZombieCount = count;
					}
				}
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
		public static bool Smash(this JobDriver_Stumble driver, Zombie zombie, bool checkSmashable, bool skipWhenRaging)
		{
			if (zombie.wasMapPawnBefore == false && zombie.IsSuicideBomber == false && zombie.IsTanky == false)
			{
				if (driver.destination.IsValid && checkSmashable == false)
					return false;

				if (skipWhenRaging && zombie.raging > 0)
					return false;
			}

			if (driver.Map.Biome == SoSTools.sosOuterSpaceBiomeDef)
				return false;

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
				if (zombie.IsTanky)
				{
					// reached goal?
					if (zombie.tankDestination == zombie.Position)
						zombie.tankDestination = IntVec3.Invalid;

					// tanky can get directly through walls
					newPos = info.GetParent(zombie.Position, true);
				}

				if (newPos.IsValid == false)
				{
					// no next move available
					zombie.raging = 0;
					return Smash(driver, zombie, checkSmashable, false);
				}
			}

			// next tanky move is on a building
			if (zombie.IsTanky && newPos.GetEdifice(zombie.Map) is Building building && (building as Mineable) == null)
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
			var zCount = possibleMoves.Select(grid.GetZombieCount).Min();
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
			var basePos = zombie.Position;
			if (zombie.Map.areaManager.Home[basePos] == false)
			{
				var moveTowardsCenter = false;

				var hour = GenLocalDate.HourOfDay(zombie.Map);
				if (hour < 12)
					hour += 24;
				if (hour > Constants.ZOMBIE_SPAWNING_HOURS[1] && hour < Constants.ZOMBIE_SPAWNING_HOURS[2])
					moveTowardsCenter = true;
				else if (hour >= Constants.ZOMBIE_SPAWNING_HOURS[0] && hour <= Constants.ZOMBIE_SPAWNING_HOURS[1])
					moveTowardsCenter = Rand.RangeInclusive(hour, Constants.ZOMBIE_SPAWNING_HOURS[1]) == Constants.ZOMBIE_SPAWNING_HOURS[1];
				else if (hour >= Constants.ZOMBIE_SPAWNING_HOURS[2] && hour <= Constants.ZOMBIE_SPAWNING_HOURS[3])
					moveTowardsCenter = Rand.RangeInclusive(Constants.ZOMBIE_SPAWNING_HOURS[2], hour) == Constants.ZOMBIE_SPAWNING_HOURS[2];

				if (moveTowardsCenter)
				{
					if (ZombieSettings.Values.wanderingStyle == WanderingStyle.Smart)
					{
						var pathing = zombie.Map.GetComponent<TickManager>()?.zombiePathing;
						if (pathing != null)
						{
							var destination = pathing.GetWanderDestination(basePos);
							if (destination.IsValid)
							{
								possibleMoves.Sort((p1, p2) => p1.DistanceToSquared(destination).CompareTo(p2.DistanceToSquared(destination)));
								possibleMoves = possibleMoves.Take(Constants.NUMBER_OF_TOP_MOVEMENT_PICKS).ToList();
								possibleMoves = possibleMoves.OrderBy(grid.GetZombieCount).ToList();
								driver.destination = possibleMoves.First();
								return;
							}
						}
					}

					if (ZombieSettings.Values.wanderingStyle == WanderingStyle.Simple)
					{
						var center = zombie.wanderDestination.IsValid ? zombie.wanderDestination : zombie.Map.Center;
						possibleMoves.Sort((p1, p2) => p1.DistanceToSquared(center).CompareTo(p2.DistanceToSquared(center)));
						possibleMoves = possibleMoves.Take(Constants.NUMBER_OF_TOP_MOVEMENT_PICKS).ToList();
						possibleMoves = possibleMoves.OrderBy(grid.GetZombieCount).ToList();
						driver.destination = possibleMoves.First();
						return;
					}
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
			if (zombie.IsTanky || zombie.isAlbino || zombie.isDarkSlimer)
				return;

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

			if (ZombieSettings.Values.zombiesEatDowned == false && ZombieSettings.Values.zombiesEatCorpses == false)
				return null;

			Thing result = null;
			zombie.PerformOnAdjacted(thing =>
			{
				if (thing is Zombie || thing is ZombieCorpse)
					return false;

				if (thing is Pawn p && ZombieSettings.Values.zombiesEatDowned)
					if (p.Spawned
						&& p.RaceProps.IsFlesh
						&& AlienTools.IsFleshPawn(p)
						&& SoSTools.IsHologram(p) == false
						&& (p.health.Downed || p.Dead)
					)
					{
						result = p;
						return true;
					}

				if (thing is Corpse c && ZombieSettings.Values.zombiesEatCorpses)
					if (c.Spawned
						&& c.InnerPawn != null
						&& c.InnerPawn.RaceProps.IsFlesh
						&& AlienTools.IsFleshPawn(c.InnerPawn)
						&& SoSTools.IsHologram(c.InnerPawn) == false
					)
					{
						result = c;
						return true;
					}

				return false;
			});
			return result;
		}

		static Thing CanAttack(Zombie zombie)
		{
			var map = zombie.Map;
			var size = map.Size;
			var grid = map.thingGrid.thingGrid;
			var basePos = zombie.Position;
			var (left, top, right, bottom) = (basePos.x > 0, basePos.z < size.z - 1, basePos.x < size.x - 1, basePos.z > 0);
			var baseIndex = map.cellIndices.CellToIndex(basePos);
			var rowOffset = size.z;
			var mode = ZombieSettings.Values.attackMode;

			List<Thing> items;
			zombie.Randomize8();
			for (var r = 0; r < 8; r++)
				switch (zombie.adjIndex8[r])
				{
					case 0:
						if (left)
						{
							items = grid[baseIndex - 1];
							for (var i = 0; i < items.Count; i++)
							{
								var item = items[i];
								if (item is not Zombie && Tools.Attackable(zombie, mode, item))
									return item;
							}
						}
						break;
					case 1:
						if (left && top)
						{
							items = grid[baseIndex - 1 + rowOffset];
							for (var i = 0; i < items.Count; i++)
							{
								var item = items[i];
								if (item is not Zombie && Tools.Attackable(zombie, mode, item))
									return item;
							}
						}
						break;
					case 2:
						if (left && bottom)
						{
							items = grid[baseIndex - 1 - rowOffset];
							for (var i = 0; i < items.Count; i++)
							{
								var item = items[i];
								if (item is not Zombie && Tools.Attackable(zombie, mode, item))
									return item;
							}
						}
						break;
					case 3:
						if (top)
						{
							items = grid[baseIndex + rowOffset];
							for (var i = 0; i < items.Count; i++)
							{
								var item = items[i];
								if (item is not Zombie && Tools.Attackable(zombie, mode, item))
									return item;
							}
						}
						break;
					case 4:
						if (right)
						{
							items = grid[baseIndex + 1];
							for (var i = 0; i < items.Count; i++)
							{
								var item = items[i];
								if (item is not Zombie && Tools.Attackable(zombie, mode, item))
									return item;
							}
						}
						break;
					case 5:
						if (right && bottom)
						{
							items = grid[baseIndex + 1 - rowOffset];
							for (var i = 0; i < items.Count; i++)
							{
								var item = items[i];
								if (item is not Zombie && Tools.Attackable(zombie, mode, item))
									return item;
							}
						}
						break;
					case 6:
						if (right && top)
						{
							items = grid[baseIndex + 1 + rowOffset];
							for (var i = 0; i < items.Count; i++)
							{
								var item = items[i];
								if (item is not Zombie && Tools.Attackable(zombie, mode, item))
									return item;
							}
						}
						break;
					case 7:
						if (bottom)
						{
							items = grid[baseIndex - rowOffset];
							for (var i = 0; i < items.Count; i++)
							{
								var item = items[i];
								if (item is not Zombie && Tools.Attackable(zombie, mode, item))
									return item;
							}
						}
						break;
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
				if (ZombieSettings.Values.smashMode == SmashMode.Nothing)
					return null;
				if (ZombieSettings.Values.smashOnlyWhenAgitated && zombie.state != ZombieState.Tracking && zombie.raging == 0)
					return null;
			}

			var nextIndex = Constants.random.Next(4);
			(adjIndex4[nextIndex], adjIndex4[prevIndex4]) = (adjIndex4[prevIndex4], adjIndex4[nextIndex]);
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
						if (thing is not Building building || (building as Mineable) != null)
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
				var info = SoundInfo.InMap(zombie);
				CustomDefs.ZombieEating.PlayOneShot(info);
			}
		}

		static void CastBrainzThought(Pawn pawn)
		{
			Tools.CastThoughtBubble(pawn, Constants.BRRAINZ);

			if (Constants.USE_SOUND && Prefs.VolumeAmbient > 0f)
				if ("CastBrainzThought".RunThrottled(2f))
				{
					var info = SoundInfo.InMap(pawn);
					CustomDefs.ZombieTracking.PlayOneShot(info);
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
			if (eatSubject == null || eatSubject.health == null || eatSubject.health.hediffSet == null)
				return null;
			return eatSubject.health.hediffSet
						.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Outside)
						.InRandomOrder()
						.FirstOrDefault();
		}

		static void AttackThing(Zombie zombie, Thing thing, JobDef def)
		{
			var job = JobMaker.MakeJob(def, thing);
			job.maxNumMeleeAttacks = 1;
			job.maxNumStaticAttacks = 1;
			job.expiryInterval = 600;
			job.canBashDoors = true;
			job.canBashFences = true;
			zombie.jobs.StartJob(job, JobCondition.Succeeded, null, true, false, null, null);
		}

		static int CountSurroundingZombies(IntVec3 pos, PheromoneGrid grid)
		{
			return GenAdj.AdjacentCellsAndInside.Select(vec => pos + vec)
				.Select(grid.GetZombieCount).Sum();
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
				if ("StartRage".RunThrottled(3f))
				{
					var info = SoundInfo.InMap(zombie);
					CustomDefs.ZombieRage.PlayOneShot(info);
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
