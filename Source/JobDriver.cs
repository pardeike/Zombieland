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

		Thing eatTarget;
		Pawn lastEatTarget;

		int eatDelay;
		int eatDelayCounter;

		static readonly int[] adjIndex4 = { 0, 1, 2, 3 };
		static int prevIndex4;
		static readonly int[] adjIndex8 = { 0, 1, 2, 3, 4, 5, 6, 7 };
		static int prevIndex8;
		static readonly float combatExtendedHealAmount = 1f / GenTicks.SecondsToTicks(1f);

		void InitAction()
		{
			destination = IntVec3.Invalid;
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref destination, "destination", IntVec3.Invalid);
			Scribe_References.Look(ref eatTarget, "eatTarget");
			Scribe_References.Look(ref lastEatTarget, "lastEatTarget");
			Scribe_Values.Look(ref eatDelayCounter, "eatDelayCounter");

			// previous versions of Zombieland stored the inner pawn of a corpse
			// in the eatTarget. We have since then changed it to contain the corpse
			// itself. For older saves, we need to convert this.
			//
			if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
				var p = eatTarget as Pawn;
				if (p != null && p.Map != null)
				{
					// find corpse that points to the pawn we stored
					eatTarget = p.Map.thingGrid
						.ThingsListAt(eatTarget.Position)
						.OfType<Corpse>()
						.FirstOrDefault(c => c.InnerPawn == eatTarget);
				}
			}
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
			return grid.GetTimestamp(p2).CompareTo(grid.GetTimestamp(p1));
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

			if (zombie.Dead || zombie.Spawned == false)
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
			else
			{
				var hediffs = zombie.health.hediffSet.hediffs
				.Where(hediff => hediff.def == HediffDefOf.WoundInfection)
				.ToArray();
				foreach (var hediff in hediffs)
					pawn.health.RemoveHediff(hediff);
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

					if (Tools.IsCombatExtendedInstalled())
						injury.Heal(combatExtendedHealAmount);
					else
						injury.Heal(injury.Severity + 0.5f);
					break;
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

			var basePos = zombie.Position;
			var grid = zombie.Map.GetGrid();

			// eat pawns or corpses
			//
			if (eatTarget != null && eatTarget.Spawned == false)
			{
				eatTarget = null;
				lastEatTarget = null;
				eatDelayCounter = 0;
			}
			if (eatTarget == null && grid.GetZombieCount(basePos) <= 2)
				eatTarget = CanIngest();

			var eatTargetPawn = eatTarget as Pawn ?? (eatTarget as Corpse)?.InnerPawn;
			if (eatTargetPawn != null)
			{
				if (eatDelayCounter == 0)
				{
					if (eatTargetPawn != lastEatTarget)
					{
						lastEatTarget = eatTargetPawn;
						zombie.Drawer.rotator.FaceCell(eatTarget.Position);
						var zombieLeaner = zombie.Drawer.leaner as ZombieLeaner;
						if (zombieLeaner != null)
						{
							var offset = (eatTarget.Position.ToVector3() - zombie.Position.ToVector3()) * 0.5f;
							if (offset.magnitude < 1f)
								zombieLeaner.extraOffset = offset;
						}

						Tools.CastThoughtBubble(pawn, Constants.EATING);
					}
					CastEatingSound();
				}

				eatDelayCounter++;
				if (eatDelayCounter <= EatDelay)
					return;
				eatDelayCounter = 0;
				zombie.raging = 0;

				var bodyPartRecord = FirstEatablePart(eatTargetPawn);
				if (bodyPartRecord != null)
				{
					var eatTargetAlive = eatTarget is Pawn && ((Pawn)eatTarget).Dead == false;
					var hediff_MissingPart = (Hediff_MissingPart)HediffMaker.MakeHediff(HediffDefOf.MissingBodyPart, eatTargetPawn, bodyPartRecord);
					hediff_MissingPart.lastInjury = HediffDefOf.Bite;
					hediff_MissingPart.IsFresh = true;
					eatTargetPawn.health.AddHediff(hediff_MissingPart, null, null);

					var eatTargetStillAlive = eatTarget is Pawn && ((Pawn)eatTarget).Dead == false;
					if (eatTargetAlive && eatTargetStillAlive == false)
					{
						if (PawnUtility.ShouldSendNotificationAbout(eatTargetPawn) && eatTargetPawn.RaceProps.Humanlike)
						{
							Messages.Message("MessageEatenByPredator".Translate(new object[]
							{
								eatTarget.LabelShort,
								zombie.LabelIndefinite()
							}).CapitalizeFirst(), zombie, MessageSound.Negative);
						}

						eatTargetPawn.Strip();
					}

					return;
				}
				else
					eatTarget.Destroy(DestroyMode.Vanish);
			}
			else
			{
				var zombieLeaner = zombie.Drawer.leaner as ZombieLeaner;
				if (zombieLeaner != null)
					zombieLeaner.extraOffset = Vector3.zero;
			}

			// calculate possible moves, sort by pheromone value and take top 3
			// then choose the one with the lowest zombie count
			// also, emit a circle of timestamps when discovering a pheromone
			// trace so nearby zombies pick it up too (leads to a chain reaction)
			//
			var possibleTrackingMoves = new List<IntVec3>();
			var currentTicks = Tools.Ticks();
			var timeDelta = long.MaxValue;
			for (var i = 0; i < 8; i++)
			{
				var pos = basePos + GenAdj.AdjacentCells[i];
				if (currentTicks - grid.GetTimestamp(pos) < fadeOff && zombie.HasValidDestination(pos))
					possibleTrackingMoves.Add(pos);
			}
			if (possibleTrackingMoves.Count > 0 && zombie.raging == 0)
			{
				possibleTrackingMoves.Sort((p1, p2) => SortByTimestamp(grid, p1, p2));
				possibleTrackingMoves = possibleTrackingMoves.Take(Constants.NUMBER_OF_TOP_MOVEMENT_PICKS).ToList();
				possibleTrackingMoves = possibleTrackingMoves.OrderBy(p => grid.GetZombieCount(p)).ToList();
				var nextMove = possibleTrackingMoves.First();
				timeDelta = currentTicks - (grid.GetTimestamp(nextMove));

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

			var checkSmashable = timeDelta >= checkSmashableFadeoff;
			if (ZombieSettings.Values.smashOnlyWhenAgitated)
				checkSmashable &= (zombie.state == ZombieState.Tracking || zombie.raging > 0);

			if (destination.IsValid == false || checkSmashable)
			{
				var building = CanSmash(zombie);
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
				var possibleMoves = GenAdj.AdjacentCells.Select(vec => basePos + vec)
					.Where(pos => zombie.HasValidDestination(pos)).ToList();

				if (possibleMoves.Count > 0)
				{
					if (zombie.raging > 0)
					{
						var info = Tools.wanderer.GetMapInfo(map);
						var bestDestination = info.GetParent(basePos);

						if (ZombieSettings.Values.smashMode == SmashMode.Nothing)
						{
							if (bestDestination.InBounds(map))
							{
								var door = bestDestination.GetEdifice(map) as Building_Door;
								if (door != null && door.Open == false)
								{
									zombie.raging = 0;
									bestDestination = IntVec3.Invalid;
								}
							}
						}

						if (zombie.HasValidDestination(bestDestination))
						{
							var destZombieCount = grid.GetZombieCount(bestDestination);
							var multiplier = Math.Max(0, destZombieCount - 1);
							if (Rand.Chance(multiplier * Constants.CLUSTER_AVOIDANCE_CHANCE))
							{
								// stand still and let other pass by
								return;
							}

							destination = bestDestination;

							if (destZombieCount > 0 || (destination.IsValid && Rand.Chance(Constants.DIVERTING_FROM_RAGE)))
								TryToDivert(grid, basePos, possibleMoves);

							if (destination.IsValid == false)
							{
								var zCount = possibleMoves.Select(p => grid.GetZombieCount(p)).Min();
								destination = possibleMoves.Where(p => grid.GetZombieCount(p) == zCount).RandomElement();
							}
						}
					}

					if (destination.IsValid == false)
					{
						var hour = GenLocalDate.HourOfDay(Find.VisibleMap);

						// check for day/night and dust/dawn
						// during night, zombies drift towards the colonies center
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
						if (moveTowardsCenter)
						{
							var center = zombie.wanderDestination.IsValid ? zombie.wanderDestination : map.Center;
							possibleMoves.Sort((p1, p2) => SortByDirection(center, p1, p2));
							possibleMoves = possibleMoves.Take(Constants.NUMBER_OF_TOP_MOVEMENT_PICKS).ToList();
							possibleMoves = possibleMoves.OrderBy(p => grid.GetZombieCount(p)).ToList();
							destination = possibleMoves.First();
						}
						else
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
				MoveToCell(grid, destination);

			// check for tight groups of zombies
			if (zombie.raging == 0 && ZombieSettings.Values.ragingZombies)
			{
				var count = CountSurroundingZombies(zombie.Position, map, grid);
				if (count > Constants.SURROUNDING_ZOMBIES_TO_TRIGGER_RAGE)
					StartRage(zombie, count);
			}
			else
			{
				if (GenTicks.TicksAbs > zombie.raging || ZombieSettings.Values.ragingZombies == false)
					zombie.raging = 0;
			}
		}

		void TryToDivert(PheromoneGrid grid, IntVec3 basePos, List<IntVec3> possibleMoves)
		{
			var forward = destination - basePos;
			var rotation = Rand.Value > 0.5 ? Rot4.East : Rot4.West;
			var divert = basePos + forward.RotatedBy(rotation);
			if (possibleMoves.Contains(divert) && grid.GetZombieCount(divert) == 0)
				destination = divert;
			else
			{
				rotation = rotation == Rot4.East ? Rot4.West : Rot4.East;
				divert = basePos + forward.RotatedBy(rotation);
				if (possibleMoves.Contains(divert) && grid.GetZombieCount(divert) == 0)
					destination = divert;
				else
				{
					var zombieFreePossibleMoves = possibleMoves.Where(cell => grid.GetZombieCount(cell) == 0).ToArray();
					var n = zombieFreePossibleMoves.Length;
					if (n > 0)
						destination = zombieFreePossibleMoves[Constants.random.Next(n)];
				}
			}
		}

		int CountSurroundingZombies(IntVec3 pos, Map map, PheromoneGrid grid)
		{
			return GenAdj.AdjacentCellsAndInside.Select(vec => pos + vec)
				.Where(c => c.InBounds(map))
				.Select(c => grid.GetZombieCount(c))
				.Sum();
		}

		void StartRage(Zombie zombie, int count)
		{
			zombie.raging = GenTicks.TicksAbs + (int)(GenDate.TicksPerDay * (0.5f + 2f * Rand.Value));
			Tools.CastThoughtBubble(zombie, Constants.RAGING);

			if (Constants.USE_SOUND)
			{
				var info = SoundInfo.InMap(zombie);
				SoundDef.Named("ZombieRage").PlayOneShot(info);
			}
		}

		void MoveToCell(PheromoneGrid grid, IntVec3 cell)
		{
			var zombie = pawn as Zombie;

			grid.ChangeZombieCount(zombie.lastGotoPosition, -1);
			grid.ChangeZombieCount(cell, 1);
			zombie.lastGotoPosition = cell;

			zombie.pather.StartPath(cell, PathEndMode.OnCell);
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

		IEnumerable<T> GetAdjacted<T>() where T : ThingWithComps
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

		Thing CanAttack()
		{
			var mode = ZombieSettings.Values.attackMode;

			var enumerator = GetAdjacted<Pawn>().GetEnumerator();
			while (enumerator.MoveNext())
			{
				var target = enumerator.Current;
				if (target.Dead == false && target.Downed == false)
				{
					if (Tools.HasInfectionState(target, InfectionState.Infecting) == false)
					{
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
				}
			}
			return null;
		}

		BodyPartRecord FirstEatablePart(Pawn eatSubject)
		{
			if (eatSubject == null || eatSubject.health == null || eatSubject.health.hediffSet == null) return null;
			return eatSubject.health.hediffSet
						.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Undefined)
						.Where(new Func<BodyPartRecord, bool>(r => r.depth == BodyPartDepth.Outside))
						.InRandomOrder()
						.FirstOrDefault();
		}

		Thing CanIngest()
		{
			if (ZombieSettings.Values.zombiesEatDowned || ZombieSettings.Values.zombiesEatCorpses)
			{
				var enumerator = GetAdjacted<ThingWithComps>().GetEnumerator();
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

		Building CanSmash(Zombie zombie)
		{
			if (ZombieSettings.Values.smashMode == SmashMode.Nothing) return null;
			if (ZombieSettings.Values.smashOnlyWhenAgitated && zombie.state != ZombieState.Tracking && zombie.raging == 0) return null;

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
				for (var i = 0; i < 4; i++)
				{
					var pos = basePos + GenAdj.CardinalDirections[adjIndex4[i]];
					if (pos.InBounds(map))
					{
						var door = pos.GetEdifice(map) as Building_Door;
						if (door != null && door.Open == false && (attackColonistsOnly == false || door.Faction == playerFaction))
							return door;
					}
				}
			}

			if (ZombieSettings.Values.smashMode == SmashMode.AnyBuilding)
			{
				for (var i = 0; i < 4; i++)
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