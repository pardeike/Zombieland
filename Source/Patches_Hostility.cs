using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace ZombieLand
{
	// patch to make raiders choose zombies less likely as a target
	// and to prefer non-downed zombies from downed one as targets
	//
	[HarmonyPatch(typeof(AttackTargetFinder))]
	[HarmonyPatch(nameof(AttackTargetFinder.GetAvailableShootingTargetsByScore))]
	static class AttackTargetFinder_GetAvailableShootingTargetsByScore_Patch
	{
		static void Prefix(List<IAttackTarget> rawTargets, IAttackTargetSearcher searcher, Verb verb)
		{
			if (searcher == null || verb == null)
				return;
			var attacker = searcher.Thing;
			if (attacker == null)
				return;

			var attackerFaction = attacker.Faction;
			var attackerRace = attacker.def.race;

			var isHuman = attackerRace?.IsFlesh ?? false;
			var isAnimal = attackerRace?.Animal ?? false;
			var isMech = attackerRace?.IsMechanoid ?? false;

			var attackerFactionDef = attackerFaction?.def;
			var isPlayer = isAnimal == false && (attackerFactionDef?.isPlayer ?? false);
			var isEnemy = isAnimal == false && attackerFactionDef != null && attackerFaction.HostileTo(Faction.OfPlayer);
			var isFriendly = isAnimal == false && isEnemy == false && isPlayer == false;

			if (isEnemy == false)
				rawTargets.RemoveAll(thing => thing.Thing is ZombieSymbiant);

			// remove spitter for everyone except player
			if (isPlayer == false)
				rawTargets.RemoveAll(thing => thing.Thing is ZombieSpitter);

			// remove electric zombies if verb is unsuited
			if (verb.CanHarmElectricZombies() == false)
				rawTargets.RemoveAll(thing => thing.Thing is Zombie zombie && zombie.IsActiveElectric);

			var removeSpitter = false;
			var removeAllZombies = false;
			var removeHarmlessZombies = false;
			var removeRopedZombies = false;
			var removeConfusedZombies = false;
			var removeDistantZombies = false;
			var removeLongDistanceMelee = false;
			var settings = ZombieSettings.Values;
			var zombiesAttackEverything = settings.attackMode == AttackMode.Everything;
			var zombiesAttackOnlyColonists = settings.attackMode == AttackMode.OnlyColonists;
			var zombiesAttackOnlyHumans = settings.attackMode == AttackMode.OnlyHumans;
			var animalsDoNotAttackZombies = settings.animalsAttackZombies == false;
			var enemiesDoNotAttackZombies = settings.enemiesAttackZombies == false;
			var anomalyAttacksZombies = false;
			var anomalyHostilityOverride = isPlayer == false && attacker is Pawn attackerPawn && AnomalyTargeting.TryGetZombieHostilityOverride(attackerPawn, out anomalyAttacksZombies);

			// handle all attacker cases: (player | friendly | enemy) x (human | mech | animal | thing)
			//
			if (anomalyHostilityOverride)
			{
				removeSpitter = true;
				if (anomalyAttacksZombies == false)
					removeAllZombies = true;
				else
				{
					removeHarmlessZombies = true;
					removeConfusedZombies = true;
					removeDistantZombies = true;
					removeLongDistanceMelee = true;
				}
			}
			else if (isPlayer)
			{
				removeRopedZombies = true;
				removeConfusedZombies = true;
				if (isHuman)
				{
				}
				else if (isMech)
				{
					if (zombiesAttackOnlyHumans)
						removeAllZombies = true;
				}
				else if (isAnimal)
				{
					if (animalsDoNotAttackZombies || zombiesAttackEverything == false)
						removeAllZombies = true;
				}
				else // isThing
				{
					removeHarmlessZombies = true;
				}
			}
			else if (isFriendly)
			{
				removeSpitter = true;
				if (isHuman)
				{
					if (zombiesAttackOnlyColonists)
						removeAllZombies = true;
					else
						removeHarmlessZombies = true;
					removeRopedZombies = true;
					removeConfusedZombies = true;
					removeDistantZombies = true;
					removeLongDistanceMelee = true;
				}
				else if (isMech)
				{
					if (zombiesAttackEverything == false)
						removeAllZombies = true;
					else
						removeHarmlessZombies = true;
					removeRopedZombies = true;
					removeConfusedZombies = true;
					removeDistantZombies = true;
				}
				else if (isAnimal)
				{
					if (animalsDoNotAttackZombies)
						removeAllZombies = true;
					else
					{
						if (zombiesAttackEverything == false)
							removeAllZombies = true;
						else
							removeHarmlessZombies = true;
						removeDistantZombies = true;
					}
				}
				else // isThing
				{
					if (zombiesAttackEverything == false)
						removeAllZombies = true;
					removeHarmlessZombies = true;
					removeRopedZombies = true;
				}
			}
			else if (isEnemy)
			{
				removeSpitter = true;
				if (isHuman)
				{
					if (enemiesDoNotAttackZombies)
						removeAllZombies = true;
					else
					{
						if (zombiesAttackOnlyColonists)
							removeAllZombies = true;
						else
							removeHarmlessZombies = true;
						removeConfusedZombies = true;
						removeDistantZombies = true;
						removeLongDistanceMelee = true;
					}
				}
				else if (isMech)
				{
					if (enemiesDoNotAttackZombies)
						removeAllZombies = true;
					else
					{
						if (zombiesAttackEverything == false)
							removeAllZombies = true;
						else
							removeHarmlessZombies = true;
						removeConfusedZombies = true;
					}
				}
				else if (isAnimal)
				{
					if (enemiesDoNotAttackZombies || animalsDoNotAttackZombies)
						removeAllZombies = true;
					else
					{
						if (zombiesAttackEverything == false)
							removeAllZombies = true;
						removeHarmlessZombies = true;
						removeRopedZombies = true;
						removeConfusedZombies = true;
						removeDistantZombies = true;
					}
				}
				else // isThing
				{
					if (enemiesDoNotAttackZombies)
						removeAllZombies = true;
					if (zombiesAttackEverything == false)
						removeAllZombies = true;
					removeHarmlessZombies = true;
				}
			}

			rawTargets.RemoveAll(target =>
			{
				if (target.Thing is not Pawn pawn)
					return false;
				if (pawn is ZombieSymbiant)
					return ZombieSymbiantCombat.IsPermittedHostileAttacker(attacker) == false;
				if (removeAllZombies && StorytellerEventFilters.IsZombielandPawn(pawn))
					return true;
				if (removeSpitter && pawn is ZombieSpitter)
					return true;
				if (pawn is not Zombie zombie)
					return false;
				var downed = zombie.Downed;
				if (removeHarmlessZombies && (downed || zombie.isAlbino))
					return true;
				var farAway = attacker.Position.DistanceToSquared(zombie.Position) > 81;
				if (removeDistantZombies && farAway)
					return true;
				if (removeRopedZombies && downed == false && zombie.ropedBy != null)
					return true;
				if (removeConfusedZombies && zombie.IsConfused)
					return true;
				if (removeLongDistanceMelee && farAway && verb.IsMeleeAttack)
					return true;
				return false;
			});
		}

		static void Postfix(List<Pair<IAttackTarget, float>> __result, IAttackTargetSearcher searcher, Verb verb)
		{
			var attacker = searcher?.Thing;
			if (attacker == null)
				return;

			const float delta = 1f;
			var maxDistance = verb.IsMeleeAttack ? 5f : verb.EffectiveRange;
			maxDistance *= maxDistance; // because we use DistanceToSquared
			var someoneIsAimingAtMe = __result.Any(pair => pair.first.TargetCurrentlyAimingAt.Thing == searcher);
			for (var i = 0; i < __result.Count; i++)
			{
				var pair = __result[i];
				if (pair.first is Zombie zombie)
				{
					var distance = attacker.Position.DistanceToSquared(zombie.Position);
					if (zombie.Downed || (someoneIsAimingAtMe && distance >= 81))
						pair.second /= 100f;
					else
						pair.second += GenMath.LerpDoubleClamped(0, maxDistance, delta, 0, distance);
				}
				__result[i] = pair;
			}
		}
	}
	//
	[HarmonyPatch(typeof(AttackTargetFinder))]
	[HarmonyPatch(nameof(AttackTargetFinder.BestAttackTarget))]
	static class AttackTargetFinder_BestAttackTarget_Patch
	{
		const float EnemyZombieEngagementDistanceSquared = 81f;

		static bool CanReachFallbackTarget(Thing searcher, Thing target, bool canBashDoors, bool canBashFences)
		{
			if (searcher is Pawn pawn)
				return pawn.CanReach(target, PathEndMode.Touch, Danger.Some, canBashDoors, canBashFences);
			var mode = canBashDoors ? TraverseMode.PassDoors : TraverseMode.NoPassClosedDoors;
			return searcher.Map.reachability.CanReach(searcher.Position, target, PathEndMode.Touch, TraverseParms.For(mode));
		}

		static bool IsValidFallbackZombie(
			Zombie zombie,
			Thing attacker,
			IAttackTargetSearcher searcher,
			Verb verb,
			TargetScanFlags flags,
			Predicate<Thing> validator,
			float minDist,
			float maxDist,
			IntVec3 locus,
			float maxTravelRadiusFromLocus,
			bool canBashDoors,
			bool canTakeTargetsCloserThanEffectiveMinRange,
			bool canBashFences,
			bool canHarmElectricZombies,
			int maxDownedRangeSquared,
			int maxRangeSquared)
		{
			if (zombie?.Spawned != true || zombie.Destroyed || zombie.Dead || zombie == attacker
				|| zombie.state == ZombieState.Emerging || zombie.IsRopedOrConfused)
				return false;
			if (canHarmElectricZombies == false && zombie.IsActiveElectric && zombie.Downed == false)
				return false;

			var distanceSquared = attacker.Position.DistanceToSquared(zombie.Position);
			if (distanceSquared < minDist * minDist || zombie.Position.InHorDistOf(attacker.Position, maxDist) == false)
				return false;
			if (canTakeTargetsCloserThanEffectiveMinRange == false)
			{
				var effectiveMinRange = verb.verbProps.EffectiveMinRange(zombie, attacker);
				if (effectiveMinRange > 0f && distanceSquared < effectiveMinRange * effectiveMinRange)
					return false;
			}
			var maxLocusDistance = maxTravelRadiusFromLocus + verb.EffectiveRange;
			if (maxTravelRadiusFromLocus < 9999f && zombie.Position.DistanceToSquared(locus) > maxLocusDistance * maxLocusDistance)
				return false;
			if (attacker.HostileTo(zombie) == false || (validator != null && validator(zombie) == false))
				return false;
			if (attacker is Pawn pawn)
			{
				var lord = pawn.GetLord();
				if (lord != null && lord.LordJob.ValidateAttackTarget(pawn, zombie) == false)
					return false;
			}
			if ((flags & TargetScanFlags.NeedNotUnderThickRoof) != 0 && zombie.Position.GetRoof(zombie.Map)?.isThickRoof == true)
				return false;

			Func<IntVec3, bool> losValidator = null;
			if ((flags & TargetScanFlags.LOSBlockableByGas) != 0
				&& (verb.EquipmentSource == null
					|| verb.EquipmentSource.TryGetComp<CompUniqueWeapon>() is not CompUniqueWeapon unique
					|| unique.IgnoreAccuracyMaluses == false))
				losValidator = cell => cell.AnyGas(attacker.Map, GasType.BlindSmoke) == false;
			if ((flags & TargetScanFlags.NeedLOSToAll) != 0)
			{
				if (losValidator != null && (losValidator(attacker.Position) == false || losValidator(zombie.Position) == false))
					return false;
				if (attacker.CanSee(zombie, losValidator) == false && (flags & TargetScanFlags.NeedLOSToPawns) != 0)
					return false;
			}
			if (((flags & TargetScanFlags.NeedThreat) != 0 || (flags & TargetScanFlags.NeedAutoTargetable) != 0)
				&& zombie.ThreatDisabled(searcher))
				return false;
			if ((flags & TargetScanFlags.NeedAutoTargetable) != 0 && AttackTargetFinder.IsAutoTargetable(zombie) == false)
				return false;
			if ((flags & TargetScanFlags.NeedActiveThreat) != 0 && GenHostility.IsActiveThreatTo(zombie, attacker.Faction) == false)
				return false;
			if (verb.IsEMP() && zombie.RaceProps?.IsFlesh == true)
				return false;
			if ((flags & TargetScanFlags.NeedNonBurning) != 0 && zombie.IsBurning())
				return false;
			if (attacker.def.race != null && (int)attacker.def.race.intelligence >= 2
				&& zombie.TryGetComp<CompExplosive>()?.wickStarted == true)
				return false;
			if ((attacker is Pawn attackerPawn && attackerPawn.IsColonist) || attacker.Faction == Faction.OfPlayer)
				if (zombie.Position.Fogged(zombie.Map))
					return false;
			if (zombie.IsCombatant() == false
				&& ((flags & TargetScanFlags.IgnoreNonCombatants) != 0
					|| GenSight.LineOfSightToThing(attacker.Position, zombie, attacker.Map) == false))
				return false;
			if ((flags & TargetScanFlags.NeedReachable) != 0
				&& CanReachFallbackTarget(attacker, zombie, canBashDoors, canBashFences) == false)
				return false;

			var downed = zombie.health.Downed;
			if (downed && (distanceSquared > maxDownedRangeSquared || ZombieSettings.Values.doubleTapRequired == false))
				return false;
			if (downed == false && distanceSquared > maxRangeSquared)
				return false;
			if (verb.CanHitTargetFrom(attacker.Position, zombie) == false)
				return false;
			return true;
		}

		static int FallbackZombieScore(Zombie zombie, IntVec3 attackerPosition, int maxRangeSquared)
		{
			var score = maxRangeSquared - attackerPosition.DistanceToSquared(zombie.Position);
			if (zombie.IsSuicideBomber)
				score += 30;
			if (zombie.IsTanky)
				score += 20;
			if (zombie.isDarkSlimer)
				score += 15;
			if (zombie.isToxicSplasher)
				score += 10;
			if (zombie.story.bodyType == BodyTypeDefOf.Thin)
				score += 5;
			if (zombie.state == ZombieState.Tracking)
				score += 5;
			return score;
		}

		static bool TryGetNearestReachableMeleeStandCell(
			ZombieSymbiantCombat.TargetScanContext context,
			Thing target,
			out IntVec3 standCell)
		{
			standCell = IntVec3.Invalid;
			if (context?.searcher?.Thing is not Pawn pawn
				|| target?.Spawned != true
				|| pawn.Map == null
				|| target.Map != pawn.Map
				|| pawn.Map.pathing == null)
				return false;

			// Installed RimWorld 1.6 BestAttackTarget (MVID
			// 967ddb80559449f0a776dafa26a855d1, token 0600655F) selects its
			// melee result through ClosestThingReachable with PathEndMode.Touch,
			// Danger.Deadly and the caller's door/fence permissions. The returned
			// Thing.Position is not a fair distance counterpart for the Symbiant's
			// explicitly selected B stand cell, especially for multi-cell targets.
			var map = pawn.Map;
			var targetInfo = new LocalTargetInfo(target);
			var traverseParms = TraverseParms.For(
				pawn,
				Danger.Deadly,
				TraverseMode.ByPawn,
				context.canBashDoors,
				alwaysUseAvoidGrid: false,
				canBashFences: context.canBashFences);
			var pathingContext = map.pathing.For(traverseParms);
			foreach (var candidate in GenAdj.CellsAdjacent8Way(target)
				.Where(cell => cell.InBounds(map))
				.OrderBy(cell => cell.DistanceToSquared(pawn.Position))
				.ThenBy(cell => cell.x)
				.ThenBy(cell => cell.z))
			{
				if (TouchPathEndModeUtility.IsAdjacentOrInsideAndAllowedToTouch(candidate, targetInfo, pathingContext) == false)
					continue;
				if (candidate != pawn.Position
					&& (candidate.Standable(map) == false || candidate.GetFirstPawn(map) != null))
					continue;
				if (pawn.CanReach(new LocalTargetInfo(candidate), PathEndMode.OnCell, Danger.Deadly, context.canBashDoors, context.canBashFences) == false)
					continue;
				standCell = candidate;
				return true;
			}
			return false;
		}

		static bool LogicalCandidateBeatsVanillaMeleeResult(ZombieSymbiantCombat.TargetScanContext context, Thing vanillaTarget)
		{
			if (context?.logicalUsesMeleeRanking != true
				|| context.searcher?.Thing == null
				|| context.logicalStandCell.IsValid == false
				|| TryGetNearestReachableMeleeStandCell(context, vanillaTarget, out var vanillaStandCell) == false)
				return false;

			var origin = context.searcher.Thing.Position;
			var logicalDistanceSquared = context.logicalStandCell.DistanceToSquared(origin);
			var vanillaDistanceSquared = vanillaStandCell.DistanceToSquared(origin);
			// A tie, or the inability to establish equivalent reachable stand cells,
			// deliberately preserves the non-null result already chosen by vanilla.
			return logicalDistanceSquared < vanillaDistanceSquared;
		}

		static bool LogicalCandidateMayReplaceVanillaResult(ZombieSymbiantCombat.TargetScanContext context, Thing vanillaTarget, ZombieSymbiant symbiant)
		{
			if (vanillaTarget == null)
				return context.logicalShootingPoolEvaluated == false;
			if (vanillaTarget == symbiant
				|| context.logicalCandidateEnteredShootingPool
				|| context.logicalShootingPoolEvaluated)
				return false;

			var verb = context.searcher?.CurrentEffectiveVerb;
			var pawn = context.searcher?.Thing as Pawn;
			// RimWorld 1.6 skips GetRandomShootingTargetByScore for aggro mental
			// states and continues through its reachable-target branch. The logical
			// Symbiant is not in that vanilla candidate set, so it has not competed
			// under the applicable ordering and must not displace a non-null result.
			if (verb?.IsMeleeAttack == false && pawn?.InAggroMentalState == true)
				return false;
			// A non-shootable ranged candidate did not compete in vanilla's shooting
			// pool. Preserve a non-null ranged fallback rather than comparing its
			// root against an unrelated logical acquisition cell.
			return LogicalCandidateBeatsVanillaMeleeResult(context, vanillaTarget);
		}

		static void Prefix(ref Predicate<Thing> validator, IAttackTargetSearcher searcher)
		{
			if (validator == null || searcher == null)
				return;
			var verb = searcher.CurrentEffectiveVerb;
			if (verb == null)
				return;

			var oldValidator = validator;

			// make ranged weapons (i.e. turrets) ignore electrical or roped zombies
			if (searcher is not Pawn attacker)
			{
				validator = (Thing t) =>
				{
					if (t is ZombieSymbiant)
						return false;
					if (verb.CanHarmElectricZombies() == false && t is Zombie zombie && (zombie.IsActiveElectric || zombie.IsRopedOrConfused))
						return false;
					return oldValidator(t);
				};

				return;
			}

			// attacker is zombie? use default
			if (attacker is Zombie)
			{
				validator = (Thing t) =>
				{
					if (t is ZombieSymbiant)
						return false;
					return oldValidator(t);
				};
				return;
			}

			var attackerFaction = attacker.Faction;
			var attackerFactionDef = attackerFaction?.def;
			var isAnimal = attacker.RaceProps?.Animal ?? false;

			// attacker is player
			if (isAnimal == false && (attackerFactionDef?.isPlayer ?? false))
			{
				validator = (Thing t) =>
				{
					if (t is ZombieSymbiant)
						return false;
					if (t is Zombie zombie && zombie.IsRopedOrConfused)
						return false;
					return oldValidator(t);
				};

				return;
			}

			if (AnomalyTargeting.TryGetZombieHostilityOverride(attacker, out var anomalyAttacksZombies))
			{
				validator = (Thing t) =>
				{
					if (t is ZombieSymbiant || t is ZombieSpitter)
						return false;
					if (t is Zombie)
						return anomalyAttacksZombies && oldValidator(t);
					return oldValidator(t);
				};

				return;
			}

			// attacker is animal
			if (isAnimal)
			{
				validator = (Thing t) =>
				{
					if (t is ZombieSymbiant || t is ZombieSpitter)
						return false;
					if (t is Zombie)
						return ZombieSettings.Values.animalsAttackZombies && oldValidator(t);
					return oldValidator(t);
				};

				return;
			}

			if (attackerFactionDef == null)
			{
				validator = (Thing t) =>
				{
					if (t is ZombieSymbiant)
						return false;
					return oldValidator(t);
				};
				return;
			}

			// attacker is friendly (disabled because the postfix deals with that)

			// attacker is enemy
			validator = (Thing t) =>
			{
				if (t is ZombieSymbiant)
					return ZombieSymbiantCombat.IsPermittedHostileAttacker(attacker) && oldValidator(t);
				if (t is ZombieSpitter)
					return false;

				if (t is Zombie zombie)
				{
					if (oldValidator(t) == false)
						return false;

					if (ZombieSettings.Values.enemiesAttackZombies == false)
						return false;

					if (zombie.IsActiveElectric && zombie.Downed == false)
						if (verb.GetDamageDef().isRanged)
							return false;

					var distanceToTarget = (float)(attacker.Position - zombie.Position).LengthHorizontalSquared;

					if (zombie.health.Downed && distanceToTarget <= 9)
						return true;

					if (zombie.state != ZombieState.Tracking)
						return false;

					var attackDistance = verb == null ? 1f : verb.verbProps.range * verb.verbProps.range;
					var zombieAvoidRadius = Tools.ZombieAvoidRadius(zombie, true);
					var maxZombieEngagementDistance = Math.Min(attackDistance, EnemyZombieEngagementDistanceSquared);

					if (attackDistance < zombieAvoidRadius && distanceToTarget >= zombieAvoidRadius)
						return false;

					if (distanceToTarget > maxZombieEngagementDistance)
						return false;

					return true;
				}

				return oldValidator(t);
			};
		}

		static void Postfix(
			ref IAttackTarget __result,
			TargetScanFlags flags,
			Predicate<Thing> validator,
			IAttackTargetSearcher searcher,
			float minDist,
			float maxDist,
			IntVec3 locus,
			float maxTravelRadiusFromLocus,
			bool canBashDoors,
			bool canTakeTargetsCloserThanEffectiveMinRange,
			bool canBashFences,
			bool onlyRanged)
		{
			var thing = __result as Thing;
			var logicalContext = ZombieSymbiantCombat.CurrentTargetScan(searcher);
			if (logicalContext != null
				&& ZombieSymbiantCombat.TryGetLogicalAttackTarget(logicalContext, out var symbiant)
				&& LogicalCandidateMayReplaceVanillaResult(logicalContext, thing, symbiant))
			{
				__result = symbiant;
				thing = symbiant;
			}

			if (thing == null)
			{
				// fix only friendlies

				Thing attacker = searcher as Pawn;
				attacker ??= searcher.Thing;

				var attackerFaction = attacker?.Faction;
				if (attackerFaction?.def != null && attackerFaction.HostileTo(Faction.OfPlayer) == false)
				{
					var verb = searcher.CurrentEffectiveVerb;
					if (verb != null)
					{
						var props = verb.verbProps;
						var canHarmElectricZombies = verb.CanHarmElectricZombies();
						if (props.IsMeleeAttack == false && props.range > 0)
						{
							var map = attacker.Map;
							if (map == null)
								return;

							var maxDownedRangeSquared = 6 * 6;
							var maxRangeSquared = (int)(props.range * props.range);
							var tickManager = map.GetComponent<TickManager>();
							var cachedZombies = tickManager?.RuntimeReady == true ? tickManager.allZombiesCached : null;
							if (cachedZombies == null)
								return;
							var pos = attacker.Position;
							Zombie bestZombie = null;
							var bestScore = int.MinValue;
							foreach (var zombie in cachedZombies)
							{
								// Source-pinned to RimWorld 1.6 AttackTargetFinder.BestAttackTarget's
								// per-candidate predicate before applying Zombieland's custom priority.
								if (IsValidFallbackZombie(zombie, attacker, searcher, verb, flags, validator, minDist, maxDist, locus,
									maxTravelRadiusFromLocus, canBashDoors, canTakeTargetsCloserThanEffectiveMinRange, canBashFences,
									canHarmElectricZombies, maxDownedRangeSquared, maxRangeSquared) == false)
									continue;
								var score = FallbackZombieScore(zombie, pos, maxRangeSquared);
								if (bestZombie == null || score > bestScore)
								{
									bestZombie = zombie;
									bestScore = score;
								}
							}
							__result = bestZombie;
							thing = __result as Thing;
						}
					}
				}
			}

			if (validator != null && thing != null && validator(thing) == false)
				__result = null;
		}
	}
	//
	[HarmonyPatch(typeof(AttackTargetFinder))]
	[HarmonyPatch(nameof(AttackTargetFinder.GetShootingTargetScore))]
	static class AttackTargetFinder_GetShootingTargetScore_Patch
	{
		[HarmonyPriority(Priority.First)]
		static bool Prefix(IAttackTargetSearcher searcher, IAttackTarget target, Verb verb, ref float __result)
		{
			if (searcher?.Thing is not Pawn pawn || verb == null || verb.IsMeleeAttack)
				return true;
			if (target is not Zombie zombie || (zombie.health.Downed && ZombieSettings.Values.doubleTapRequired == false))
				return true;
			var distance = (zombie.Position - pawn.Position).LengthHorizontal;
			var weaponRange = verb.verbProps.range;
			if (distance > weaponRange)
				return true;

			__result = 120f * (weaponRange - distance) / weaponRange;
			if (zombie.IsSuicideBomber)
				__result += 12f;
			if (zombie.isToxicSplasher)
				__result += 6f;
			if (zombie.story.bodyType == BodyTypeDefOf.Thin)
				__result += 3f;
			return false;
		}

		static void Postfix(IAttackTargetSearcher searcher, IAttackTarget target, Verb verb, ref float __result)
		{
			if (target?.Thing is not ZombieSymbiant symbiant || searcher?.Thing == null || verb == null || verb.IsMeleeAttack)
				return;
			if (ZombieSymbiantCombat.TryGetBoundRangedCell(verb, symbiant, out var cell) == false
				&& ZombieSymbiantCombat.TrySelectRangedCell(verb, searcher.Thing.Position, symbiant, out cell, out _) == false)
				return;

			var rootDistance = Math.Min(symbiant.Position.DistanceTo(searcher.Thing.Position), 40f);
			var cellDistance = Math.Min(cell.DistanceTo(searcher.Thing.Position), 40f);
			var rootCover = CoverUtility.CalculateOverallBlockChance(symbiant.Position, searcher.Thing.Position, searcher.Thing.Map);
			var cellCover = CoverUtility.CalculateOverallBlockChance(cell, searcher.Thing.Position, searcher.Thing.Map);
			var rootBlastFriendlyFire = LogicalBlastFriendlyFireScore(target, searcher, verb, symbiant.Position);
			var cellBlastFriendlyFire = LogicalBlastFriendlyFireScore(target, searcher, verb, cell);
			var priority = target.TargetPriorityFactor;
			__result += (rootDistance - cellDistance) * priority;
			__result += (rootCover - cellCover) * 10f * priority;
			__result += (cellBlastFriendlyFire - rootBlastFriendlyFire) * priority;
		}

		static float LogicalBlastFriendlyFireScore(IAttackTarget target, IAttackTargetSearcher searcher, Verb verb, IntVec3 center)
		{
			// Mirrors RimWorld 1.6 AttackTargetFinder.FriendlyFireBlastRadiusTargetScoreOffset
			// (installed MVID 967ddb80559449f0a776dafa26a855d1), with an explicit center and
			// Zombieland targets omitted. Keep its 40/10/7/18 weights and 0.6 hostile factor pinned.
			if (verb.verbProps.ai_AvoidFriendlyFireRadius <= 0f)
				return 0f;
			var map = target.Thing.Map;
			var cellCount = GenRadial.NumCellsInRadius(verb.verbProps.ai_AvoidFriendlyFireRadius);
			var score = 0f;
			for (var i = 0; i < cellCount; i++)
			{
				var cell = center + GenRadial.RadialPattern[i];
				if (cell.InBounds(map) == false)
					continue;
				var firstTarget = true;
				var things = cell.GetThingList(map);
				for (var j = 0; j < things.Count; j++)
				{
					var thing = things[j];
					if (thing is not IAttackTarget || thing == target.Thing || thing is Zombie || thing is ZombieSymbiant)
						continue;
					if (firstTarget)
					{
						if (GenSight.LineOfSight(center, cell, map, true) == false)
							break;
						firstTarget = false;
					}
					var value = thing == searcher.Thing ? 40f : thing is not Pawn ? 10f : thing.def.race.Animal ? 7f : 18f;
					score = searcher.Thing.HostileTo(thing) ? score + value * 0.6f : score - value;
				}
			}
			return score;
		}
	}

	// remove zombies from friendly fire calculations
	//
	[HarmonyPatch]
	static class AttackTargetFinder_FriendlyFire_Patch
	{
		static IEnumerable<MethodBase> TargetMethods()
		{
			yield return SymbolExtensions.GetMethodInfo(() => AttackTargetFinder.FriendlyFireConeTargetScoreOffset(default, default, default));
			yield return SymbolExtensions.GetMethodInfo(() => AttackTargetFinder.FriendlyFireBlastRadiusTargetScoreOffset(default, default, default));
		}

		static List<Thing> RemoveZombies(List<Thing> input) => input.Where(i => i is not Zombie && i is not ZombieSymbiant).ToList();

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var m_GetThingList = SymbolExtensions.GetMethodInfo(() => GridsUtility.GetThingList(default, default));
			var list = instructions.ToList();
			var idx = list.FirstIndexOf(instruction => instruction.operand is MethodInfo method && method == m_GetThingList);
			list.Insert(idx + 1, CodeInstruction.Call(() => RemoveZombies(default)));
			return list;
		}
	}

	// patch to control if raiders and animals see zombies as hostile
	//
	[HarmonyPatch(typeof(GenHostility))]
	[HarmonyPatch(nameof(GenHostility.HostileTo))]
	[HarmonyPatch(new Type[] { typeof(Thing), typeof(Thing) })]
	static class GenHostility_HostileTo_Thing_Thing_Patch
	{
		static bool IsHostileToSymbiant(Thing thing)
		{
			var faction = thing?.Faction;
			return faction != null && faction.HostileTo(Faction.OfPlayer);
		}

		[HarmonyPriority(Priority.First)]
		static bool Prefix(Thing a, Thing b, ref bool __result)
		{
			if (a is ZombieSymbiant || b is ZombieSymbiant)
			{
				__result = a is ZombieSymbiant ? IsHostileToSymbiant(b) : IsHostileToSymbiant(a);
				return false;
			}
			return true;
		}

		static void Postfix(Thing a, Thing b, ref bool __result)
		{
			if (a is not Pawn pawn || pawn.ActivePartOfColony() || pawn is Zombie || b is not Zombie)
				return;

			if (pawn.InfectionState() == InfectionState.Infecting)
				__result = false;
			else
				__result = Tools.IsHostileToZombies(pawn);
		}
	}
	//
	[HarmonyPatch(typeof(GenHostility))]
	[HarmonyPatch(nameof(GenHostility.HostileTo))]
	[HarmonyPatch(new Type[] { typeof(Thing), typeof(Faction) })]
	static class GenHostility_HostileTo_Thing_Faction_Patch
	{
		static bool Prefix(ref bool __result, Thing t, Faction fac)
		{
			if (t is ZombieSymbiant)
			{
				__result = fac != null && fac != Faction.OfPlayer && fac.HostileTo(Faction.OfPlayer);
				return false;
			}
			if (t is ZombieSpitter && (fac?.def?.isPlayer ?? false) == false)
			{
				__result = false;
				return false;
			}
			return true;
		}

		static void Postfix(Thing t, Faction fac, ref bool __result)
		{
			if (fac == null)
				return;
			if (fac.def != ZombieDefOf.Zombies)
				return;
			if (t is not Pawn pawn)
				return;
			if (pawn is Zombie)
				return;
			if (pawn.ActivePartOfColony())
				return;
			__result = Tools.IsHostileToZombies(pawn);
		}
	}

	// patch to remove zombies from hostile count so it does not
	// alter game logic (for example when a caravan leaves an enemy base)
	//
	[HarmonyPatch(typeof(GenHostility))]
	[HarmonyPatch(nameof(GenHostility.IsActiveThreatTo))]
	[HarmonyPatch(new Type[] { typeof(IAttackTarget), typeof(Faction), typeof(bool), typeof(bool) })]
	static class GenHostility_IsActiveThreat_Patch
	{
		static bool IsZombielandPawnTarget(IAttackTarget target)
		{
			return StorytellerEventFilters.IsZombielandAttackTarget(target);
		}

		[HarmonyPriority(Priority.First)]
		static bool Prefix(ref bool __result, IAttackTarget target, Faction faction)
		{
			if (target is ZombieSymbiant)
			{
				__result = faction != null && faction != Faction.OfPlayer && faction.HostileTo(Faction.OfPlayer);
				return false;
			}
			if (IsZombielandPawnTarget(target) == false) // must skip non zombies bc next patch requires it
				return true;

			if (faction == null)
			{
				__result = false;
				return false;
			}

			if (faction == Faction.OfPlayer)
			{
				__result = false; // fake non-hostile to prevent hostile count bc of zombies
				return false;
			}

			var anomalyFactionOverride = AnomalyTargeting.TryGetZombieHostilityOverride(faction, out var anomalyAttacksZombies);
			if (anomalyFactionOverride && anomalyAttacksZombies == false)
			{
				__result = false;
				return false;
			}

			if (anomalyFactionOverride == false && faction.HostileTo(Faction.OfPlayer))
				if (ZombieSettings.Values.enemiesAttackZombies == false)
				{
					__result = false;
					return false;
				}

			var attackMode = ZombieSettings.Values.attackMode;
			__result = attackMode switch
			{
				AttackMode.Everything => true,
				AttackMode.OnlyHumans => faction.def.humanlikeFaction,
				AttackMode.OnlyColonists => false,
				_ => false,
			};
			return false;
		}
	}

	[HarmonyPatch(typeof(Pawn))]
	[HarmonyPatch(nameof(Pawn.ThreatDisabled))]
	static class Pawn_ThreatDisabled_Symbiant_Patch
	{
		static void Postfix(Pawn __instance, ref bool __result)
		{
			if (__instance is ZombieSymbiant symbiant && symbiant.Spawned && symbiant.Dead == false && symbiant.Destroyed == false)
				__result = false;
		}
	}

	//
	// but let drafted pawns attack zombies
	//
	[HarmonyPatch(typeof(JobDriver_Wait))]
	[HarmonyPatch(nameof(JobDriver_Wait.CheckForAutoAttack))]
	static class JobDriver_Wait_CheckForAutoAttack_Patch
	{
		static bool IsActiveThreatTo(IAttackTarget target, Faction faction, bool ignoreHives, bool canBeFogged)
		{
			if (target is Zombie zombie)
				return zombie.IsRopedOrConfused == false;
			if (target is ZombieSymbiant)
				return faction != null && faction != Faction.OfPlayer && faction.HostileTo(Faction.OfPlayer);
			if (target is ZombieSpitter)
				return faction?.def?.isPlayer ?? false;
			return GenHostility.IsActiveThreatTo(target, faction, ignoreHives, canBeFogged); // ok to call patched method bc we filtered out zombies
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			return instructions.MethodReplacer(
				SymbolExtensions.GetMethodInfo(() => GenHostility.IsActiveThreatTo(null, null)),
				SymbolExtensions.GetMethodInfo(() => IsActiveThreatTo(null, null, default, default))
			);
		}
	}

	static class TargetCachePatches
	{
		// used to prevent zombies from being counted as hostiles
		// both in map exist and for danger music
		//
		static readonly Dictionary<Map, HashSet<IAttackTarget>> playerHostilesWithoutZombies = new();

		internal static void ResetMapOwnedState()
		{
			playerHostilesWithoutZombies.Clear();
		}

		static bool IsZombielandTarget(IAttackTarget target)
		{
			return StorytellerEventFilters.IsZombielandAttackTarget(target);
		}

		static HashSet<IAttackTarget> PlayerHostilesWithoutZombies(Map map)
		{
			if (map == null)
				return new HashSet<IAttackTarget>();
			if (playerHostilesWithoutZombies.TryGetValue(map, out var targets) == false)
			{
				targets = new HashSet<IAttackTarget>();
				playerHostilesWithoutZombies.Add(map, targets);
			}
			targets.RemoveWhere(target =>
			{
				var thing = target?.Thing;
				return thing == null
					|| thing.Destroyed
					|| thing.Spawned == false
					|| thing.Map != map
					|| thing.HostileTo(Faction.OfPlayer) == false;
			});
			return targets;
		}

		[HarmonyPatch(typeof(AttackTargetsCache))]
		[HarmonyPatch(nameof(AttackTargetsCache.TargetsHostileToColony), MethodType.Getter)]
		static class AttackTargetsCache_TargetsHostileToColony_Patch
		{
			static void Postfix(Map ___map, ref HashSet<IAttackTarget> __result)
			{
				__result = PlayerHostilesWithoutZombies(___map);
			}
		}

		// patch to remove the constant danger music because of the constant thread of zombies
		//
		[HarmonyPatch(typeof(AttackTargetsCache))]
		[HarmonyPatch(nameof(AttackTargetsCache.RegisterTarget))]
		static class AttackTargetsCache_RegisterTarget_Patch
		{
			static void Postfix(IAttackTarget target)
			{
				var thing = target.Thing;
				if (thing == null || IsZombielandTarget(target))
					return;
				if (thing.HostileTo(Faction.OfPlayer) == false)
					return;
				var map = thing.Map;
				if (map == null)
					return;
				_ = PlayerHostilesWithoutZombies(map).Add(target);
			}
		}

		[HarmonyPatch(typeof(AttackTargetsCache))]
		[HarmonyPatch(nameof(AttackTargetsCache.DeregisterTarget))]
		static class AttackTargetsCache_DeregisterTarget_Patch
		{
			static bool Prefix(IAttackTarget target)
			{
				var thing = target?.Thing;
				if (thing == null || IsZombielandTarget(target))
					return true;
				var map = thing.MapHeld;
				if (map == null)
					return true;
				if (playerHostilesWithoutZombies.TryGetValue(map, out var targets))
					_ = targets.Remove(target);
				return true;
			}
		}
	}
}
