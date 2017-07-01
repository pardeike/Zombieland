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
	public class JobDriver_Stumble : JobDriver
	{
		IntVec3 destination;

		Pawn eatTarget;
		bool eatTargetIsCorpse;
		bool eatTargetDied;
		Pawn lastEatTarget;

		int eatDelay;
		int eatDelayCounter;

		void InitAction()
		{
			destination = IntVec3.Invalid;
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref destination, "destination", IntVec3.Invalid);
			Scribe_References.Look(ref eatTarget, "eatTarget");
			Scribe_Values.Look(ref eatTargetIsCorpse, "eatTargetIsCorpse", false);
			Scribe_Values.Look(ref eatTargetDied, "eatTargetDied", false);
			Scribe_References.Look(ref lastEatTarget, "lastEatTarget");
			Scribe_Values.Look(ref eatDelayCounter, "eatDelayCounter");
		}

		int EatDelay
		{
			get
			{
				if (eatDelay == 0)
				{
					eatDelay = Constants.EAT_DELAY_TICKS;
					var zombie = (Zombie)pawn;
					switch (zombie.story.bodyType)
					{
						case BodyType.Thin:
							eatDelay *= 3;
							break;
						case BodyType.Hulk:
							eatDelay /= 4;
							break;
						case BodyType.Fat:
							eatDelay = (int)(eatDelay / 1.5f);
							break;
					}
				}
				return eatDelay;
			}
		}

		int SortByTimestamp(PheromoneGrid grid, IntVec3 p1, IntVec3 p2)
		{
			return grid.Get(p2, false).timestamp.CompareTo(grid.Get(p1, false).timestamp);
		}

		int SortByDirection(IntVec3 center, IntVec3 p1, IntVec3 p2)
		{
			return p1.DistanceToSquared(center).CompareTo(p2.DistanceToSquared(center));
		}

		void TickAction()
		{
			var fadeOff = Tools.PheromoneFadeoff();
			var agitatedFadeoff = fadeOff / 4;
			var checkSmashableFadeoff = agitatedFadeoff / 2;

			var zombie = (Zombie)pawn;
			if (zombie.state == ZombieState.Emerging) return;
			var map = zombie.Map;

			if (zombie.Dead || zombie.Destroyed)
			{
				EndJobWith(JobCondition.InterruptForced);
				return;
			}

			if (zombie.state == ZombieState.ShouldDie)
			{
				EndJobWith(JobCondition.InterruptForced);
				zombie.Kill(null);
				return;
			}

			if (ZombieSettings.Values.zombiesDieVeryEasily)
			{
				if (zombie.health.hediffSet.GetHediffs<Hediff_Injury>().Any())
				{
					zombie.Kill(null);
					return;
				}
			}

			if (zombie.Downed)
			{
				if (ZombieSettings.Values.zombiesDieVeryEasily || ZombieSettings.Values.doubleTapRequired == false)
				{
					zombie.Kill(null);
					return;
				}

				var walkCapacity = PawnCapacityUtility.CalculateCapacityLevel(zombie.health.hediffSet, PawnCapacityDefOf.Moving);
				var missingBrain = zombie.health.hediffSet.GetBrain() == null;
				if (walkCapacity < 0.25f || missingBrain)
				{
					zombie.Kill(null);
					return;
				}

				var injuries = zombie.health.hediffSet.GetHediffs<Hediff_Injury>();
				foreach (var injury in injuries)
				{
					if (ZombieSettings.Values.zombiesDieVeryEasily)
					{
						zombie.Kill(null);
						return;
					}

					if (injury.IsOld() == false)
					{
						if (Tools.IsCombatExtendedInstalled())
							injury.Heal(1f / GenTicks.SecondsToTicks(1f));
						else
							injury.Heal(injury.Severity + 0.5f);
						break;
					}
				}

				if (zombie.Downed) return;
			}

			// handling invalid destinations
			//
			if (destination.x == 0 && destination.z == 0) destination = IntVec3.Invalid;
			if (zombie.HasValidDestination(destination)) return;

			// if we are near targets then attack them
			//
			var enemy = CanAttack();
			if (enemy != null)
			{
				destination = enemy.Position;

				zombie.state = ZombieState.Tracking;
				if (Constants.USE_SOUND)
				{
					var info = SoundInfo.InMap(enemy);
					SoundDef.Named("ZombieHit").PlayOneShot(info);
				}

				AttackThing(enemy, JobDefOf.AttackMelee);
				return;
			}

			// eat a downed or dead pawn
			//
			if (eatTarget == null)
			{
				eatTarget = CanIngest(out eatTargetIsCorpse);
				if (eatTarget != null)
					eatTargetDied = eatTarget.Dead;
			}

			if (eatTarget != null)
			{
				if (eatDelayCounter == 0)
				{
					if (eatTarget != lastEatTarget)
					{
						lastEatTarget = eatTarget;
						zombie.Drawer.rotator.FaceCell(eatTarget.Position);
						var zombieLeaner = zombie.Drawer.leaner as ZombieLeaner;
						if (zombieLeaner != null)
							zombieLeaner.extraOffset = (eatTarget.Position.ToVector3() - zombie.Position.ToVector3()) * 0.5f;

						Tools.CastThoughtBubble(pawn, Constants.EATING);
					}
					CastEatingSound();
				}

				eatDelayCounter++;
				if (eatDelayCounter <= EatDelay)
					return;
				eatDelayCounter = 0;

				var bodyPartRecord = FirstEatablePart(eatTarget);
				if (bodyPartRecord != null)
				{
					var hediff_MissingPart = (Hediff_MissingPart)HediffMaker.MakeHediff(HediffDefOf.MissingBodyPart, eatTarget, bodyPartRecord);
					hediff_MissingPart.lastInjury = HediffDefOf.Bite;
					hediff_MissingPart.IsFresh = true;
					eatTarget.health.AddHediff(hediff_MissingPart, null, null);

					if (eatTargetIsCorpse == false && eatTargetDied == false && eatTarget.Dead)
					{
						Tools.DoWithAllZombies(map, z =>
						{
							if (z.jobs != null)
							{
								var driver = z.jobs.curDriver as JobDriver_Stumble;
								if (driver != null && driver.eatTarget == eatTarget)
								{
									driver.eatTargetDied = true;
									driver.eatTargetIsCorpse = true;
								}
							}
						});

						if (PawnUtility.ShouldSendNotificationAbout(eatTarget) && eatTarget.RaceProps.Humanlike)
						{
							Messages.Message("MessageEatenByPredator".Translate(new object[]
							{
								eatTarget.LabelShort,
								zombie.LabelIndefinite()
							}).CapitalizeFirst(), zombie, MessageSound.Negative);
						}

						eatTarget.Strip();
					}

					return;
				}
				else
				{
					var corpse = map.thingGrid
						.ThingsListAt(eatTarget.Position)
						.OfType<Corpse>()
						.FirstOrDefault(c => c.InnerPawn == eatTarget);
					if (corpse != null)
						corpse.Destroy(DestroyMode.Vanish);

					Tools.DoWithAllZombies(map, z =>
					{
						if (z.jobs != null)
						{
							var driver = z.jobs.curDriver as JobDriver_Stumble;
							if (driver != null && driver.eatTarget == eatTarget)
							{
								driver.eatTarget = null;
								driver.lastEatTarget = null;
								driver.eatDelayCounter = 0;
							}
						}
					});
				}
			}
			else
			{
				var zombieLeaner = zombie.Drawer.leaner as ZombieLeaner;
				if (zombieLeaner != null)
					zombieLeaner.extraOffset = Vector3.zero;
			}

			var basePos = zombie.Position;

			// calculate possible moves, sort by pheromone value and take top 3
			// then choose the one with the lowest zombie count
			// also, emit a circle of timestamps when discovering a pheromone
			// trace so nearby zombies pick it up too (leads to a chain reaction)
			//
			var grid = zombie.Map.GetGrid();
			var possibleTrackingMoves = new List<IntVec3>();
			var currentTicks = Tools.Ticks();
			var timeDelta = long.MaxValue;
			for (int i = 0; i < 8; i++)
			{
				var pos = basePos + GenAdj.AdjacentCells[i];
				if (currentTicks - grid.Get(pos, false).timestamp < fadeOff && zombie.HasValidDestination(pos))
					possibleTrackingMoves.Add(pos);
			}
			if (possibleTrackingMoves.Count > 0)
			{
				possibleTrackingMoves.Sort((p1, p2) => SortByTimestamp(grid, p1, p2));
				possibleTrackingMoves = possibleTrackingMoves.Take(Constants.NUMBER_OF_TOP_MOVEMENT_PICKS).ToList();
				possibleTrackingMoves = possibleTrackingMoves.OrderBy(p => grid.Get(p, false).zombieCount).ToList();
				var nextMove = possibleTrackingMoves.First();
				timeDelta = currentTicks - grid.Get(nextMove, false).timestamp;

				destination = nextMove;
				if (zombie.state == ZombieState.Wandering)
				{
					Tools.ChainReact(zombie.Map, basePos, nextMove);
					if (timeDelta <= agitatedFadeoff)
						CastBrainzThought();
				}
				zombie.state = ZombieState.Tracking;
			}
			if (destination.IsValid == false) zombie.state = ZombieState.Wandering;

			bool checkSmashable = timeDelta >= checkSmashableFadeoff;
			if (ZombieSettings.Values.smashOnlyWhenAgitated)
				checkSmashable &= zombie.state == ZombieState.Tracking;

			if (destination.IsValid == false || checkSmashable)
			{
				var building = CanSmash();
				if (building != null)
				{
					destination = building.Position;

					if (Constants.USE_SOUND)
					{
						var info = SoundInfo.InMap(enemy);
						SoundDef.Named("ZombieHit").PlayOneShot(info);
					}

					AttackThing(building, JobDefOf.AttackStatic);
					return;
				}
			}

			if (destination.IsValid == false)
			{
				var hour = GenLocalDate.HourOfDay(Find.VisibleMap);

				// check for day/night and dust/dawn
				//
				var moveTowardsCenter = false;
				if (map.areaManager.Home[basePos] == false)
				{
					if (hour < 12) hour += 24;
					if (hour > Constants.HOUR_START_OF_NIGHT && hour < Constants.HOUR_END_OF_NIGHT)
						moveTowardsCenter = true;
					else if (hour >= Constants.HOUR_START_OF_DUSK && hour <= Constants.HOUR_START_OF_NIGHT)
						moveTowardsCenter = Rand.RangeInclusive(hour, Constants.HOUR_START_OF_NIGHT) == Constants.HOUR_START_OF_NIGHT;
					else if (hour >= Constants.HOUR_END_OF_NIGHT && hour <= Constants.HOUR_START_OF_DAWN)
						moveTowardsCenter = Rand.RangeInclusive(Constants.HOUR_END_OF_NIGHT, hour) == Constants.HOUR_END_OF_NIGHT;
				}

				var possibleMoves = new List<IntVec3>();
				for (int i = 0; i < 8; i++)
				{
					var pos = basePos + GenAdj.AdjacentCells[i];
					if (zombie.HasValidDestination(pos))
						possibleMoves.Add(pos);
				}
				if (possibleMoves.Count > 0)
				{
					// during night, zombies drift towards the colonies center
					//
					if (moveTowardsCenter)
					{
						var center = zombie.wanderDestination.IsValid ? zombie.wanderDestination : map.Center;
						possibleMoves.Sort((p1, p2) => SortByDirection(center, p1, p2));
						possibleMoves = possibleMoves.Take(Constants.NUMBER_OF_TOP_MOVEMENT_PICKS).ToList();
						possibleMoves = possibleMoves.OrderBy(p => grid.Get(p, false).zombieCount).ToList();
						destination = possibleMoves.First();
					}
					else
					{
						// otherwise they sometimes stand or walk towards a random direction
						//
						if (Rand.Chance(Constants.STANDING_STILL_CHANCE))
						{
							var n = possibleMoves.Count();
							destination = possibleMoves[Constants.random.Next(n)];
						}
					}
				}
			}

			// if we have a valid destination, go there
			//
			if (destination.IsValid)
				MoveToCell(destination);
		}

		void MoveToCell(LocalTargetInfo dest)
		{
			var zombie = (Zombie)pawn;
			zombie.Map.GetGrid().ChangeZombieCount(dest.Cell, 1);
			zombie.pather.StartPath(dest, PathEndMode.OnCell);
		}

		void CastEatingSound()
		{
			if (Constants.USE_SOUND)
			{
				var info = SoundInfo.InMap(new TargetInfo(pawn.Position, pawn.Map, false));
				SoundDef.Named("ZombieEating").PlayOneShot(info);
			}
		}

		void CastBrainzThought()
		{
			Tools.CastThoughtBubble(pawn, Constants.BRRAINZ);

			if (Constants.USE_SOUND)
			{
				var info = SoundInfo.InMap(new TargetInfo(pawn.Position, pawn.Map, false));
				SoundDef.Named("ZombieTracking").PlayOneShot(info);
			}
		}

		void AttackThing(Thing thing, JobDef def)
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

			pawn.jobs.StartJob(job, JobCondition.Succeeded, null, true, false, null, null);
		}

		public override void Notify_PatherArrived()
		{
			base.Notify_PatherArrived();
			destination = IntVec3.Invalid;
		}

		static int[] adjIndex4 = { 0, 1, 2, 3 };
		static int prevIndex4;
		static int[] adjIndex8 = { 0, 1, 2, 3, 4, 5, 6, 7 };
		static int prevIndex8;

		IEnumerable<T> GetAdjacted<T>() where T : ThingWithComps
		{
			var nextIndex = Constants.random.Next(8);
			var c = adjIndex8[prevIndex8];
			adjIndex8[prevIndex8] = adjIndex8[nextIndex];
			adjIndex8[nextIndex] = c;
			prevIndex8 = nextIndex;

			var grid = pawn.Map.thingGrid;
			var basePos = pawn.Position;
			for (int i = 0; i < 8; i++)
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

		Thing CanAttack()
		{
			var mode = ZombieSettings.Values.attackMode;

			var enumerator = GetAdjacted<Pawn>().GetEnumerator();
			while (enumerator.MoveNext())
			{
				var p = enumerator.Current;
				if (p.Dead == false && p.Downed == false)
				{
					if (mode == AttackMode.Everything)
						return p;

					if (p.MentalState != null)
					{
						var msDef = p.MentalState.def;
						if (msDef == MentalStateDefOf.Manhunter || msDef == MentalStateDefOf.ManhunterPermanent)
							return p;
					}

					if (mode == AttackMode.OnlyHumans && p.RaceProps.Humanlike)
						return p;

					if (mode == AttackMode.OnlyColonists && p.IsColonist)
						return p;
				}
			}
			return null;
		}

		BodyPartRecord FirstEatablePart(Pawn pawn)
		{
			if (pawn == null || pawn.health == null || pawn.health.hediffSet == null) return null;
			return pawn.health.hediffSet
						.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Undefined)
						.Where(new Func<BodyPartRecord, bool>(r => r.depth == BodyPartDepth.Outside))
						.InRandomOrder()
						.FirstOrDefault();
		}

		Pawn CanIngest(out bool isCorpse)
		{
			isCorpse = false;

			if (ZombieSettings.Values.zombiesEatDowned || ZombieSettings.Values.zombiesEatCorpses)
			{
				var enumerator = GetAdjacted<ThingWithComps>().GetEnumerator();
				while (enumerator.MoveNext())
				{
					var twc = enumerator.Current;

					if (ZombieSettings.Values.zombiesEatDowned)
					{
						var p = twc as Pawn;
						if (p != null && p.RaceProps.IsFlesh
							&& (p.Dead == true || p.Downed == true && p.Destroyed == false))
						{
							isCorpse = false;
							return p;
						}
					}

					if (ZombieSettings.Values.zombiesEatCorpses)
					{
						var c = twc as Corpse;
						if (c != null && c.InnerPawn != null && c.InnerPawn.RaceProps.IsFlesh
							&& c.Bugged == false && c.Destroyed == false && c.IsDessicated() == false)
						{
							isCorpse = true;
							return c.InnerPawn;
						}
					}
				}
			}
			return null;
		}

		Building CanSmash()
		{
			if (ZombieSettings.Values.smashMode == SmashMode.Nothing) return null;
			if (ZombieSettings.Values.smashOnlyWhenAgitated && (pawn as Zombie).state != ZombieState.Tracking) return null;

			var nextIndex = Constants.random.Next(4);
			var c = adjIndex4[prevIndex4];
			adjIndex4[prevIndex4] = adjIndex4[nextIndex];
			adjIndex4[nextIndex] = c;
			prevIndex4 = nextIndex;

			var playerFaction = Faction.OfPlayer;
			var map = pawn.Map;
			var grid = map.thingGrid;
			var basePos = pawn.Position;
			var attackColonistsOnly = (ZombieSettings.Values.attackMode == AttackMode.OnlyColonists);

			if (ZombieSettings.Values.smashMode == SmashMode.DoorsOnly)
			{
				for (int i = 0; i < 4; i++)
				{
					var pos = basePos + GenAdj.CardinalDirections[adjIndex4[i]];
					if (pos.InBounds(map))
					{
						foreach (var thing in grid.ThingsListAtFast(pos))
						{
							var door = thing as Building_Door;
							if (door != null && door.Open == false && (attackColonistsOnly == false || door.Faction == playerFaction))
								return door;
						}
					}
				}
			}

			if (ZombieSettings.Values.smashMode == SmashMode.AnyBuilding)
			{
				for (int i = 0; i < 4; i++)
				{
					var pos = basePos + GenAdj.CardinalDirections[adjIndex4[i]];
					if (pos.InBounds(map))
					{
						foreach (var thing in grid.ThingsListAtFast(pos))
						{
							var building = thing as Building;
							if (building != null)
							{
								var buildingDef = building.def;
								var factionCondition = (attackColonistsOnly == false || building.Faction == playerFaction);
								if (buildingDef.useHitPoints && buildingDef.building.isNaturalRock == false && factionCondition)
									return building;
							}
						}
					}
				}
			}

			return null;
		}

		public override string GetReport()
		{
			return "Stumbling";
		}

		protected override IEnumerable<Toil> MakeNewToils()
		{
			yield return new Toil()
			{
				initAction = new Action(InitAction),
				tickAction = new Action(TickAction),
				defaultCompleteMode = ToilCompleteMode.Never
			};
		}
	}
}