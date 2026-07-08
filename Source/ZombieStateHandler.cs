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

		public static Dictionary<int, float> creepyAmbientSoundVolumes = new();

		// make zombies die if necessary ============================================================
		//
		public static bool NeedsShouldDieTick(Zombie zombie, out bool tick10)
		{
			tick10 = false;
			if (zombie.Dead || zombie.Spawned == false || zombie.state == ZombieState.ShouldDie)
				return true;
			tick10 = zombie.EveryNTick(NthTick.Every10);
			return zombie.IsSuicideBomber || tick10;
		}

		public static bool ShouldDie(this JobDriver_Stumble driver, Zombie zombie)
		{
			if (NeedsShouldDieTick(zombie, out var tick10) == false)
				return false;
			return driver.ShouldDie(zombie, tick10);
		}

		public static bool ShouldDie(this JobDriver_Stumble driver, Zombie zombie, bool tick10)
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

		public static bool NeedsWallPushStartTick(Zombie zombie)
		{
			var ticks = GenTicks.TicksAbs;
			if (zombie.wallPushCooldown > 0 && ticks < zombie.wallPushCooldown)
				return false;
			if (zombie.wallPushProgress >= 0f || ZombieSettings.Values.minimumZombiesForWallPushing == 0)
				return false;
			return zombie.EveryNTick(NthTick.Every8);
		}

		public static bool CheckWallPushing(Zombie zombie, PheromoneGrid grid)
		{
			var ticks = GenTicks.TicksAbs;
			if (zombie.wallPushCooldown > 0 && ticks < zombie.wallPushCooldown)
				return false;

			var minimum = ZombieSettings.Values.minimumZombiesForWallPushing;
			if (zombie.wallPushProgress >= 0f || minimum == 0)
				return false;

			var pos = zombie.Position;
			var map = zombie.Map;

			var totalZombies = grid.GetZombieCountInBounds(pos);
			var wallCount = 0;
			IntVec3 wallCell = IntVec3.Invalid;
			var edificeGrid = map.edificeGrid;
			for (var i = 0; i < 4; i++)
			{
				var adjacent = pos + pushDirections[i];
				if (adjacent.InBounds(map) == false)
					continue;

				totalZombies += grid.GetZombieCountInBounds(adjacent);
				var edifice = edificeGrid[adjacent];
				if (edifice is Building building && building is not Mineable)
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

			var cachedZombies = map.GetComponent<TickManager>()?.allZombiesCached;
			if (cachedZombies != null)
				foreach (var z in cachedZombies)
					if (z.Position == destination)
						return false;

			zombie.wallPushProgress = 0f;
			zombie.wallPushStart = pos.ToVector3Shifted();
			zombie.wallPushDestination = destination.ToVector3Shifted();
			zombie.wallPushCooldown = ticks + GenDate.TicksPerHour;
			if (ZombieAwarenessCues.ShouldPlayWallAndSabotageSound())
				CustomDefs.WallPushing.PlayOneShot(SoundInfo.InMap(new TargetInfo(pos, map)));
			if (ZombieSettings.Values.dangerousSituationMessage && map.areaManager.Home[wallCell])
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
			var newCell = possibleMoves.Count > 0 ? possibleMoves[0] : default;
			if (newCell != destination)
				driver.destination = newCell;
			return true;
		}

		// handle downed zombies ====================================================================
		//
		public static bool NeedsDownedOrUnconsciousnessTick(Zombie zombie)
		{
			if (zombie.paralyzedUntil > 0 || zombie.Downed || zombie.isHealing)
				return true;
			if (zombie.IsTanky)
				return false;
			if (zombie.EveryNTick(NthTick.Every30) == false)
				return false;
			return zombie.health.hediffSet.hediffs.Count > 0;
		}

		public static bool DownedOrUnconsciousness(Zombie zombie)
		{
			var avoidGridSnapshot = zombie.AvoidGridSnapshotBeforeClearingExpiredParalysis();
			bool Return(bool result)
			{
				zombie.RequestAvoidGridRefreshIfSpecChanged(avoidGridSnapshot);
				return result;
			}

			if (zombie.paralyzedUntil > 0)
			{
				if (GenTicks.TicksAbs < zombie.paralyzedUntil)
					return Return(true);
				zombie.paralyzedUntil = 0;
			}

			if (zombie.IsTanky == false)
			{
				var health = zombie.health;
				var hediffSet = health.hediffSet;
				if (zombie.Downed == false && zombie.isHealing == false && hediffSet.hediffs.Count == 0)
				{
					zombie.consciousness = 1f;
					return Return(false);
				}

				zombie.consciousness = health.capacities.GetLevel(PawnCapacityDefOf.Consciousness);
				if (zombie.consciousness <= Constants.MIN_CONSCIOUSNESS)
				{
					if (zombie.EveryNTick(NthTick.Every960))
					{
						if (ZombieSettings.Values.doubleTapRequired && ZombieSettings.Values.zombiesDieVeryEasily == false)
						{
							var injury = hediffSet.GetHediffsTendable().SafeRandomElement();
							if (injury != null)
								health.RemoveHediff(injury);
							else
							{
								var bleeding = hediffSet.hediffs.Where(hediff => hediff.def == HediffDefOf.BloodLoss).SafeRandomElement();
								if (bleeding != null)
									health.RemoveHediff(bleeding);
							}
						}
					}
					return Return(zombie.ropedBy == null);
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
					return Return(true);
				}
			}

			if (zombie.isHealing == false || zombie.stances.stunner.Stunned || zombie.IsBurning())
				return Return(false);

			if ((wasHealing == false && zombie.isHealing) || zombie.EveryNTick(NthTick.Every480))
			{
				var injury = zombie.health.hediffSet.hediffs.Where(hediff => hediff is Hediff_Injury injury && injury.IsPermanent() == false).SafeRandomElement();
				if (injury != null)
					_ = HealthUtility.Cure(injury);
			}
			return Return(false);
		}

		// handle things that affect zombies ====================================================================
		//
		public static void ApplyFire(Zombie zombie)
		{
			if (zombie.isOnFire || zombie.EveryNTick(NthTick.Every50) == false)
				return;

			var temp = GenTemperature.GetTemperatureForCell(zombie.Position, zombie.Map);
			if (temp >= 200f)
				FireUtility.TryAttachFire(zombie, GenMath.LerpDoubleClamped(200f, 1000f, 0.01f, 1f, temp), null);
		}

		// invalidate destination if necessary ======================================================
		//
		const int movingDestinationValidationInterval = 8;

		public static bool ValidDestination(this JobDriver_Stumble driver, Zombie zombie)
		{
			if (driver.destination.x == 0 && driver.destination.z == 0)
			{
				driver.destination = IntVec3.Invalid;
				driver.nextDestinationValidationTick = 0;
				return false;
			}

			var pather = zombie.pather;
			var movingToDestination = pather.Moving && pather.Destination.Cell == driver.destination;
			var ticks = GenTicks.TicksAbs;
			if (movingToDestination && ticks < driver.nextDestinationValidationTick)
				return true;

			if (zombie.HasValidDestination(driver.destination) == false)
			{
				driver.destination = IntVec3.Invalid;
				driver.nextDestinationValidationTick = 0;
				return false;
			}
			if (pather.Moving && pather.Destination.Cell == driver.destination)
			{
				driver.nextDestinationValidationTick = ticks + movingDestinationValidationInterval;
				return true;
			}
			driver.nextDestinationValidationTick = 0;
			if (pather.curPath == null || pather.curPath.Found == false || pather.curPath.NodesLeftCount == 0)
			{
				driver.destination = IntVec3.Invalid;
				driver.nextDestinationValidationTick = 0;
				return false;
			}
			return true;
		}

		// attack nearby enemies ====================================================================
		//
		public static bool Attack(this JobDriver_Stumble driver, Zombie zombie)
		{
			var enemy = CanAttack(zombie);
			if (enemy == null)
				return false;

			driver.destination = enemy.Position;

			zombie.SetState(ZombieState.Tracking);
			if (ZombieAwarenessCues.ShouldPlayZombieActionSound() && Prefs.VolumeAmbient > 0f)
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
							_ = FireUtility.TryStartFireIn(building.Position, building.Map, Rand.Range(0.1f, 1.75f), zombie);
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

			if (driver.eatTarget != null && (driver.eatTarget.Spawned == false || driver.eatTarget.Position != driver.lastEatTargetPosition))
			{
				driver.eatTarget = null;
				driver.lastEatTarget = null;
				driver.lastEatTargetPosition = IntVec3.Invalid;
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
					driver.lastEatTargetPosition = driver.eatTarget.Position;
					zombie.rotationTracker.FaceCell(driver.lastEatTargetPosition);
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

			var avoidGridSnapshot = zombie.CaptureAvoidGridSnapshot();
			driver.eatDelayCounter = 0;
			zombie.raging = 0;
			zombie.RequestAvoidGridRefreshIfSpecChanged(avoidGridSnapshot);
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
			var gearToForbid = eatTargetAlive ? GearToForbidOnZombieEating(eatTargetPawn) : null;
			var dropPos = driver.eatTarget?.PositionHeld ?? eatTargetPawn.PositionHeld;
			var dropMap = driver.eatTarget?.MapHeld ?? eatTargetPawn.MapHeld ?? zombie.MapHeld;
			if (Tools.TryAddMissingPart(eatTargetPawn, bodyPartRecord, HediffDefOf.Bite) == false)
				return false;

			if (eatTargetAlive)
				ForbidOrPlaceReleasedGearFromZombieEating(gearToForbid, dropPos, dropMap);

			var eatTargetStillAlive = driver.eatTarget is Pawn eatTarget2 && eatTarget2.Dead == false;
			if (eatTargetAlive && eatTargetStillAlive == false)
			{
				if (PawnUtility.ShouldSendNotificationAbout(eatTargetPawn) && eatTargetPawn.RaceProps.Humanlike)
				{
					var msg = "MessageEatenByPredator".Translate(new NamedArgument(driver.eatTarget.LabelShort, null), zombie.LabelIndefinite().Named("PREDATOR"), driver.eatTarget.Named("EATEN"));
					Messages.Message(msg.CapitalizeFirst(), zombie, MessageTypeDefOf.NegativeEvent);
				}

				DropAndForbidGearFromZombieEating(eatTargetPawn, gearToForbid, dropPos, dropMap);
			}

			return true;
		}

		static HashSet<Thing> GearToForbidOnZombieEating(Pawn pawn)
		{
			var result = new HashSet<Thing>();
			if (pawn == null)
				return result;

			foreach (var equipment in pawn.equipment?.AllEquipmentListForReading ?? Enumerable.Empty<ThingWithComps>())
				if (equipment != null)
					_ = result.Add(equipment);
			foreach (var apparel in pawn.apparel?.WornApparel ?? Enumerable.Empty<Apparel>())
				if (apparel != null)
					_ = result.Add(apparel);
			foreach (var inventoryThing in pawn.inventory?.innerContainer ?? Enumerable.Empty<Thing>())
				if (inventoryThing != null)
					_ = result.Add(inventoryThing);
			var carriedThing = pawn.carryTracker?.CarriedThing;
			if (carriedThing != null)
				_ = result.Add(carriedThing);

			return result;
		}

		static void DropAndForbidGearFromZombieEating(Pawn pawn, HashSet<Thing> gearToForbid, IntVec3 fallbackPos, Map fallbackMap)
		{
			if (pawn == null)
				return;

			gearToForbid ??= new HashSet<Thing>();
			var pos = pawn.Corpse?.PositionHeld ?? (fallbackPos.IsValid ? fallbackPos : pawn.PositionHeld);
			var map = pawn.Corpse?.MapHeld ?? fallbackMap ?? pawn.MapHeld;

			foreach (var equipment in pawn.equipment?.AllEquipmentListForReading?.ToArray() ?? Array.Empty<ThingWithComps>())
			{
				_ = gearToForbid.Add(equipment);
				if (pawn.equipment.TryDropEquipment(equipment, out var droppedEquipment, pos, true) && droppedEquipment != null)
					_ = gearToForbid.Add(droppedEquipment);
			}

			var apparelToDrop = pawn.apparel == null
				? Array.Empty<Apparel>()
				: pawn.apparel.WornApparel
					.Concat(gearToForbid.OfType<Apparel>().Where(apparel => pawn.apparel.Contains(apparel)))
					.Distinct()
					.ToArray();
			foreach (var apparel in apparelToDrop)
			{
				_ = gearToForbid.Add(apparel);
				if (TryDropApparelFromZombieEating(pawn, apparel, pos, map, out var droppedApparel) && droppedApparel != null)
					_ = gearToForbid.Add(droppedApparel);
			}

			if (map != null && pos.IsValid)
			{
				foreach (var inventoryThing in pawn.inventory?.innerContainer?.ToArray() ?? Array.Empty<Thing>())
					{
						_ = gearToForbid.Add(inventoryThing);
						if (pawn.inventory.innerContainer.TryDrop(inventoryThing, pos, map, ThingPlaceMode.Near, out Thing droppedInventory, (thing, _) => thing.SetForbidden(true, false)) && droppedInventory != null)
							_ = gearToForbid.Add(droppedInventory);
					}
				}

			ForbidOrPlaceReleasedGearFromZombieEating(gearToForbid, pos, map);

			pawn.Faction?.Notify_MemberStripped(pawn, Faction.OfPlayer);
		}

		static void ForbidOrPlaceReleasedGearFromZombieEating(HashSet<Thing> gearToForbid, IntVec3 pos, Map map)
		{
			if (gearToForbid == null)
				return;

			foreach (var thing in gearToForbid.ToArray())
			{
				if (thing == null || thing.Destroyed)
					continue;
				if (thing.Spawned)
				{
					thing.SetForbidden(true, false);
					continue;
				}
				if (thing.ParentHolder != null || map == null || pos.IsValid == false)
					continue;

				if (GenPlace.TryPlaceThing(thing, pos, map, ThingPlaceMode.Near, out var placedThing, (placed, _) => placed.SetForbidden(true, false)) && placedThing != null)
				{
					_ = gearToForbid.Add(placedThing);
					placedThing.SetForbidden(true, false);
				}
			}
		}

		static bool TryDropApparelFromZombieEating(Pawn pawn, Apparel apparel, IntVec3 pos, Map map, out Apparel droppedApparel)
		{
			droppedApparel = null;
			if (pawn?.apparel == null || apparel == null || pos.IsValid == false)
				return false;

			if (map != null && pawn.apparel.GetDirectlyHeldThings().TryDrop(apparel, pos, map, ThingPlaceMode.Near, out Thing droppedThing))
			{
				droppedApparel = droppedThing as Apparel;
				droppedThing?.SetForbidden(true, false);
				return true;
			}

			return pawn.apparel.TryDrop(apparel, out droppedApparel, pos, true);
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
				zombie.SetState(ZombieState.Tracking);
			}

			if (driver.destination.IsValid == false)
				zombie.SetState(ZombieState.Wandering);

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

			if (ZombieAwarenessCues.ShouldPlayWallAndSabotageSound() && Prefs.VolumeAmbient > 0f)
			{
				var info = SoundInfo.InMap(building);
				CustomDefs.ZombieHit.PlayOneShot(info);
			}

			AttackThing(zombie, building, JobDefOf.AttackStatic);
			return true;
		}

		// mine mountains ===========================================================================
		//
		static Effecter mineEffecter;
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
			var adjacent = GenAdj.AdjacentCellsAround;
			Mineable mineable = null;
			if (allDirections)
			{
				for (var i = 0; i < adjacent.Length; i++)
				{
					var cell = basePos + adjacent[i];
					if (cell.InBounds(map) == false)
						continue;
					mineable = cell.GetFirstThing<Mineable>(map);
					if (mineable != null)
						break;
				}
			}
			else
			{
				var cell = basePos + adjacent[idx];
				if (cell.InBounds(map))
					mineable = cell.GetFirstThing<Mineable>(map);
				if (mineable == null)
				{
					cell = basePos + adjacent[(idx + 1) % 8];
					if (cell.InBounds(map))
						mineable = cell.GetFirstThing<Mineable>(map);
				}
				if (mineable == null)
				{
					cell = basePos + adjacent[(idx + 7) % 8];
					if (cell.InBounds(map))
						mineable = cell.GetFirstThing<Mineable>(map);
				}
			}
			if (mineable == null)
				return false;

			zombie.rotationTracker.FaceCell(mineable.Position);
			mineEffecter ??= EffecterDefOf.Mine?.Spawn();
			mineEffecter?.Trigger(zombie, mineable);
			var baseDamage = (int)GenMath.LerpDoubleClamped(0, 5, 2, 40, Tools.Difficulty());
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
			var result = driver.adjacentMoveBuffer;
			result.Clear();
			if (driver.destination.IsValid)
				return result;

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
					var avoidGridSnapshot = zombie.CaptureAvoidGridSnapshot();
					zombie.raging = 0;
					zombie.RequestAvoidGridRefreshIfSpecChanged(avoidGridSnapshot);
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
			var zCount = int.MaxValue;
			var candidateCount = 0;
			for (var i = 0; i < possibleMoves.Count; i++)
			{
				var count = grid.GetZombieCount(possibleMoves[i]);
				if (count < zCount)
				{
					zCount = count;
					candidateCount = 1;
				}
				else if (count == zCount)
					candidateCount++;
			}

			var chosen = Rand.Range(0, candidateCount);
			for (var i = 0; i < possibleMoves.Count; i++)
			{
				var cell = possibleMoves[i];
				if (grid.GetZombieCount(cell) != zCount)
					continue;
				if (chosen-- == 0)
				{
					driver.destination = cell;
					break;
				}
			}
			return false;
		}

		// during night, drift towards colony =======================================================
		//
		public static void Wander(this JobDriver_Stumble driver, Zombie zombie, PheromoneGrid grid, List<IntVec3> possibleMoves)
		{
			if (driver.destination.IsValid)
				return;

			var map = zombie.Map;

			// check for day/night and dust/dawn
			// during night, zombies drift towards the colonies center
			//
			var basePos = zombie.Position;
			if (map.areaManager.Home[basePos] == false)
			{
				var volume = creepyAmbientSoundVolumes.TryGetValue(map.uniqueID, 0f);
				if (zombie.GetHashCode() % 16 + 1 <= volume * 16f)
				{
					var style = ZombieSettings.Values.wanderingStyle;
					if (style == WanderingStyle.Smart)
					{
						var pathing = map.GetComponent<TickManager>()?.zombiePathing;
						if (pathing != null)
						{
							var destination = pathing.GetWanderDestination(basePos);
							if (destination.IsValid)
							{
								possibleMoves.Sort((p1, p2) => p1.DistanceToSquared(destination).CompareTo(p2.DistanceToSquared(destination)));
								driver.destination = FirstLowestZombieCountInTopMoves(possibleMoves, grid);
								return;
							}
							else
								style = WanderingStyle.Simple; // use fallback
						}
					}
					if (style == WanderingStyle.Simple)
					{
						var center = zombie.wanderDestination.IsValid ? zombie.wanderDestination : map.Center;
						possibleMoves.Sort((p1, p2) => p1.DistanceToSquared(center).CompareTo(p2.DistanceToSquared(center)));
						driver.destination = FirstLowestZombieCountInTopMoves(possibleMoves, grid);
						return;
					}
				}
			}

			// random wandering
			var n = possibleMoves.Count;
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
				var threshold = Constants.ZOMBIE_COUNT_TO_TRIGGER_RAGE > 0 ? Constants.ZOMBIE_COUNT_TO_TRIGGER_RAGE : rageLevels[ZombieSettings.Values.zombieRageLevel - 1];
				if (count >= threshold)
					StartRage(zombie);
				return;
			}

			if (GenTicks.TicksAbs > zombie.raging || ZombieSettings.Values.ragingZombies == false)
			{
				var avoidGridSnapshot = zombie.CaptureAvoidGridSnapshot();
				zombie.raging = 0;
				zombie.RequestAvoidGridRefreshIfSpecChanged(avoidGridSnapshot);
			}
		}

		public static void CheckEndRage(Zombie zombie)
		{
			if (zombie.raging == 0)
				return;

			if (zombie.isAlbino || zombie.isDarkSlimer || GenTicks.TicksAbs > zombie.raging || ZombieSettings.Values.ragingZombies == false)
			{
				var avoidGridSnapshot = zombie.CaptureAvoidGridSnapshot();
				zombie.raging = 0;
				zombie.RequestAvoidGridRefreshIfSpecChanged(avoidGridSnapshot);
			}
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
				if (thing is Zombie || thing is ZombieSymbiant || thing is ZombieSpitter || thing is ZombieCorpse)
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

		static readonly IntVec3[] attackAdjacentOffsets = new[]
		{
			new IntVec3(-1, 0, 0),
			new IntVec3(-1, 0, 1),
			new IntVec3(-1, 0, -1),
			new IntVec3(0, 0, 1),
			new IntVec3(1, 0, 0),
			new IntVec3(1, 0, -1),
			new IntVec3(1, 0, 1),
			new IntVec3(0, 0, -1)
		};

		internal static IntVec3 AttackAdjacentOffset(int index) => attackAdjacentOffsets[index];

		internal sealed class AttackScanComparison
		{
			public Zombie zombie;
			public Thing legacyTarget;
			public Thing indexedTarget;
			public int[] adjacentOrder;
			public bool Matches => legacyTarget == indexedTarget;
		}

		const int idleAttackScanInterval = 6;

		static Thing CanAttack(Zombie zombie)
		{
			var map = zombie.Map;
			ZombieAttackTargetIndex index = null;
			var hasCandidateNeighbor = false;
			if (zombie.attackCandidateNeighborTick == GenTicks.TicksAbs)
				hasCandidateNeighbor = zombie.hasAttackCandidateNeighbor;
			else
			{
				index = map.GetComponent<ZombieAttackTargetIndex>();
				if (index == null)
					return CanAttackLegacy(zombie, true);

				var baseIndex = map.cellIndices.CellToIndex(zombie.Position);
				var candidateNeighbors = index.CurrentCandidateNeighborsByCell();
				hasCandidateNeighbor = baseIndex >= 0 && baseIndex < candidateNeighbors.Length && candidateNeighbors[baseIndex];
			}

			if (hasCandidateNeighbor == false)
			{
				if (ShouldDeferIdleAttackScan(zombie))
					return null;
				zombie.Randomize8();
				return null;
			}

			index ??= map.GetComponent<ZombieAttackTargetIndex>();
			if (index == null)
				return CanAttackLegacy(zombie, true);
			return CanAttackIndexed(zombie, true, index, true);
		}

		public static bool NeedsAttackTick(Zombie zombie)
		{
			if (zombie.attackCandidateNeighborTick != GenTicks.TicksAbs)
				return true;
			if (zombie.hasAttackCandidateNeighbor)
				return true;
			if (AlwaysScanForAttack(zombie))
				return true;
			return GenTicks.TicksAbs >= zombie.nextAttackScanTick;
		}

		static bool AlwaysScanForAttack(Zombie zombie)
		{
			if (zombie.state == ZombieState.Tracking || zombie.raging > 0 || zombie.wasMapPawnBefore || zombie.ropedBy != null || zombie.wallPushProgress >= 0f)
				return true;
			if (zombie.IsTanky || zombie.IsSuicideBomber || zombie.isAlbino || zombie.isDarkSlimer || zombie.isElectrifier || zombie.isHealer || zombie.isMiner || zombie.isToxicSplasher || zombie.isOnFire)
				return true;
			return false;
		}

		static bool ShouldDeferIdleAttackScan(Zombie zombie)
		{
			if (AlwaysScanForAttack(zombie))
				return false;

			var ticks = GenTicks.TicksAbs;
			if (ticks >= zombie.nextAttackScanTick)
			{
				zombie.nextAttackScanTick = ticks + idleAttackScanInterval + (zombie.thingIDNumber & 3);
				return false;
			}
			return true;
		}

		internal static AttackScanComparison CompareCanAttackScans(Zombie zombie, bool randomizeOrder)
		{
			var savedPrevIndex = zombie.prevIndex8;
			var savedOrder = new int[8];
			Array.Copy(zombie.adjIndex8, savedOrder, savedOrder.Length);

			if (randomizeOrder)
				zombie.Randomize8();

			var comparisonPrevIndex = zombie.prevIndex8;
			var comparisonOrder = new int[8];
			Array.Copy(zombie.adjIndex8, comparisonOrder, comparisonOrder.Length);

			RestoreAttackScanOrder(zombie, comparisonOrder, comparisonPrevIndex);
			var legacy = CanAttackLegacy(zombie, false);
			RestoreAttackScanOrder(zombie, comparisonOrder, comparisonPrevIndex);
			var indexed = CanAttackIndexed(zombie, false);
			RestoreAttackScanOrder(zombie, savedOrder, savedPrevIndex);

			return new AttackScanComparison
			{
				zombie = zombie,
				legacyTarget = legacy,
				indexedTarget = indexed,
				adjacentOrder = comparisonOrder
			};
		}

		internal static void SetAttackScanOrder(Zombie zombie, int[] order)
		{
			if (zombie == null || order == null || order.Length != 8)
				return;
			Array.Copy(order, zombie.adjIndex8, 8);
			zombie.prevIndex8 = 0;
		}

		static void RestoreAttackScanOrder(Zombie zombie, int[] order, int prevIndex)
		{
			Array.Copy(order, zombie.adjIndex8, 8);
			zombie.prevIndex8 = prevIndex;
		}

		static Thing CanAttackIndexed(Zombie zombie, bool randomize) => CanAttackIndexed(zombie, randomize, null, null);

		static Thing CanAttackIndexed(Zombie zombie, bool randomize, ZombieAttackTargetIndex index, bool? hasCandidateNeighbor)
		{
			var map = zombie.Map;
			var mode = ZombieSettings.Values.attackMode;
			index ??= map.GetComponent<ZombieAttackTargetIndex>();
			if (index == null)
				return CanAttackLegacy(zombie, randomize);

			if (randomize)
				zombie.Randomize8();

			var basePos = zombie.Position;
			var baseIndex = map.cellIndices.CellToIndex(basePos);
			if ((hasCandidateNeighbor ?? index.CurrentCandidateNeighborsByCell()[baseIndex]) == false)
				return null;

			var candidatesByCell = index.CurrentCandidatesByCell();
			for (var r = 0; r < 8; r++)
			{
				var adjacent = basePos + attackAdjacentOffsets[zombie.adjIndex8[r]];
				if (adjacent.InBounds(map) == false)
					continue;

				var result = FirstAttackable(zombie, mode, candidatesByCell[map.cellIndices.CellToIndex(adjacent)]);
				if (result != null)
					return result;
			}
			return null;
		}

		static Thing CanAttackLegacy(Zombie zombie, bool randomize)
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
			if (randomize)
				zombie.Randomize8();
			for (var r = 0; r < 8; r++)
				switch (zombie.adjIndex8[r])
				{
					case 0:
						if (left)
						{
							items = grid[baseIndex - 1];
							var result = FirstAttackable(zombie, mode, items);
							if (result != null)
								return result;
						}
						break;
					case 1:
						if (left && top)
						{
							items = grid[baseIndex - 1 + rowOffset];
							var result = FirstAttackable(zombie, mode, items);
							if (result != null)
								return result;
						}
						break;
					case 2:
						if (left && bottom)
						{
							items = grid[baseIndex - 1 - rowOffset];
							var result = FirstAttackable(zombie, mode, items);
							if (result != null)
								return result;
						}
						break;
					case 3:
						if (top)
						{
							items = grid[baseIndex + rowOffset];
							var result = FirstAttackable(zombie, mode, items);
							if (result != null)
								return result;
						}
						break;
					case 4:
						if (right)
						{
							items = grid[baseIndex + 1];
							var result = FirstAttackable(zombie, mode, items);
							if (result != null)
								return result;
						}
						break;
					case 5:
						if (right && bottom)
						{
							items = grid[baseIndex + 1 - rowOffset];
							var result = FirstAttackable(zombie, mode, items);
							if (result != null)
								return result;
						}
						break;
					case 6:
						if (right && top)
						{
							items = grid[baseIndex + 1 + rowOffset];
							var result = FirstAttackable(zombie, mode, items);
							if (result != null)
								return result;
						}
						break;
					case 7:
						if (bottom)
						{
							items = grid[baseIndex - rowOffset];
							var result = FirstAttackable(zombie, mode, items);
							if (result != null)
								return result;
						}
						break;
				}
			return null;
		}

		static Thing FirstAttackable(Zombie zombie, AttackMode mode, List<Thing> items)
		{
			if (items == null)
				return null;

			for (var i = 0; i < items.Count; i++)
			{
				var item = items[i];
				if (item is Zombie)
					continue;
				if (item is Pawn pawn && (pawn.Spawned == false || pawn.Destroyed || pawn.Dead))
					continue;
				if (Tools.Attackable(zombie, mode, item))
					return item;
			}
			return null;
		}

		static Building CanSmash(Zombie zombie)
		{
			var map = zombie.Map;
			var basePos = zombie.Position;
			var attackColonistsOnly = (ZombieSettings.Values.attackMode == AttackMode.OnlyColonists);
			var playerFaction = Faction.OfPlayer;

			if (zombie.isAlbino)
				return null;

			if (zombie.IsTanky)
			{
				var info = ZombieWanderer.GetMapInfo(map);
				var pos = info.GetParent(basePos, false);
				if (pos.IsValid == false)
					pos = info.GetParent(basePos, true);
				if (pos.IsValid && pos.GetEdifice(zombie.Map) is Building building && CanSmashBuilding(building, attackColonistsOnly, playerFaction))
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

					if (pos.GetEdifice(map) is Building_Door door && door.Open == false && CanSmashBuilding(door, attackColonistsOnly, playerFaction))
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

						if (CanSmashBuilding(building, attackColonistsOnly, playerFaction))
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

		static bool CanSmashBuilding(Building building, bool attackColonistsOnly, Faction playerFaction)
		{
			if (building == null || (building as Mineable) != null)
				return false;

			var buildingDef = building.def;
			var buildingProperties = buildingDef?.building;
			return buildingDef?.useHitPoints == true
				&& buildingProperties != null
				&& buildingProperties.isNaturalRock == false
				&& buildingProperties.isTargetable
				&& buildingProperties.canBeDamagedByAttacks
				&& (attackColonistsOnly == false || building.Faction == playerFaction);
		}

		// helpers ==================================================================================

		static void CastEatingSound(Zombie zombie)
		{
			if (ZombieAwarenessCues.ShouldPlayZombieActionSound() && Prefs.VolumeAmbient > 0f)
			{
				var info = SoundInfo.InMap(zombie);
				CustomDefs.ZombieEating.PlayOneShot(info);
			}
		}

		public static void CastBrainzThought(Pawn pawn)
		{
			Tools.CastThoughtBubble(pawn, Constants.BRRAINZ);

			if (ZombieAwarenessCues.ShouldPlayZombieActionSound() && Prefs.VolumeAmbient > 0f)
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
						.Where(part => Tools.IsSafeMissingPartTarget(eatSubject, part))
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
			var count = 0;
			var adjacent = GenAdj.AdjacentCellsAndInside;
			for (var i = 0; i < adjacent.Length; i++)
				count += grid.GetZombieCount(pos + adjacent[i]);
			return count;
		}

		static IntVec3 FirstLowestZombieCountInTopMoves(List<IntVec3> possibleMoves, PheromoneGrid grid)
		{
			var count = Math.Min(Constants.NUMBER_OF_TOP_MOVEMENT_PICKS, possibleMoves.Count);
			var bestCell = IntVec3.Invalid;
			var bestZombieCount = int.MaxValue;
			for (var i = 0; i < count; i++)
			{
				var cell = possibleMoves[i];
				var zombieCount = grid.GetZombieCount(cell);
				if (zombieCount < bestZombieCount)
				{
					bestZombieCount = zombieCount;
					bestCell = cell;
				}
			}
			return bestCell;
		}

		static readonly float[] minRageLength = new float[] { 0.1f, 0.2f, 0.5f, 1f, 2f };
		static readonly float[] maxRageLength = new float[] { 1f, 2f, 4f, 6f, 8f };
		public static void StartRage(Zombie zombie)
		{
			var avoidGridSnapshot = zombie.CaptureAvoidGridSnapshot();
			var min = minRageLength[ZombieSettings.Values.zombieRageLevel - 1];
			var max = maxRageLength[ZombieSettings.Values.zombieRageLevel - 1];
			zombie.raging = GenTicks.TicksAbs + (int)(GenDate.TicksPerHour * Rand.Range(min, max));
			zombie.RequestAvoidGridRefreshIfSpecChanged(avoidGridSnapshot);
			Tools.CastThoughtBubble(zombie, Constants.RAGING);

			if (ZombieAwarenessCues.ShouldPlayZombieActionSound() && Prefs.VolumeAmbient > 0f)
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

			var n = 0;
			for (var i = 0; i < possibleMoves.Count; i++)
				if (grid.GetZombieCount(possibleMoves[i]) == 0)
					n++;
			if (n > 0)
			{
				var chosen = Constants.random.Next(n);
				for (var i = 0; i < possibleMoves.Count; i++)
				{
					var cell = possibleMoves[i];
					if (grid.GetZombieCount(cell) != 0)
						continue;
					if (chosen-- == 0)
					{
						destination = cell;
						return true;
					}
				}
			}

			return false;
		}
	}
}
