using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using Verse.AI;

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

			var isPlayer = isAnimal == false && attackerFaction.IsPlayer;
			var isEnemy = isAnimal == false && attackerFaction.HostileTo(Faction.OfPlayer);
			var isFriendly = isAnimal == false && isEnemy == false && isPlayer == false;

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

			// handle all attacker cases: (player | friendly | enemy) x (human | mech | animal | thing)
			//
			if (isPlayer)
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
				if (removeSpitter && pawn is ZombieSpitter)
					return true;
				if (pawn is not Zombie zombie)
					return false;
				var downed = zombie.Downed;
				if (removeAllZombies)
					return true;
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
				if (verb.CanHarmElectricZombies())
					return;

				validator = (Thing t) =>
				{
					if (t is Zombie zombie && (zombie.IsActiveElectric || zombie.IsRopedOrConfused))
						return false;
					return oldValidator(t);
				};

				return;
			}

			// attacker is zombie? use default
			if (attacker is Zombie)
				return;

			// attacker is animal
			if (attacker.RaceProps?.Animal ?? false)
			{
				validator = (Thing t) =>
				{
					if (t is ZombieSpitter)
						return false;
					if (t is Zombie)
						return ZombieSettings.Values.animalsAttackZombies;
					return oldValidator(t);
				};

				return;
			}

			// attacker is player
			if (attacker.Faction.IsPlayer)
			{
				validator = (Thing t) =>
				{
					if (t is Zombie zombie && zombie.IsRopedOrConfused)
						return false;
					return oldValidator(t);
				};

				return;
			}

			// attacker is friendly (disabled because the postfix deals with that)

			// attacker is enemy
			validator = (Thing t) =>
			{
				if (t is ZombieSpitter)
					return false;

				if (t is Zombie zombie)
				{
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

					if (attackDistance < zombieAvoidRadius && distanceToTarget >= zombieAvoidRadius)
						return false;

					if (distanceToTarget > attackDistance)
						return false;
				}

				return oldValidator(t);
			};
		}

		static void Postfix(ref IAttackTarget __result, TargetScanFlags flags, Predicate<Thing> validator, IAttackTargetSearcher searcher)
		{
			var thing = __result as Thing;

			if (thing == null)
			{
				// fix only friendlies

				Thing attacker = searcher as Pawn;
				attacker ??= searcher.Thing;

				if (attacker != null && attacker.Faction.HostileTo(Faction.OfPlayer) == false)
				{
					var verb = searcher.CurrentEffectiveVerb;
					if (verb != null)
					{
						var props = verb.verbProps;
						var canHarmElectricZombies = verb.CanHarmElectricZombies();
						if (props.IsMeleeAttack == false && props.range > 0)
						{
							var maxDownedRangeSquared = 6 * 6;
							var maxRangeSquared = (int)(props.range * props.range);
							var tickManager = attacker.Map.GetComponent<TickManager>();
							var pos = attacker.Position;
							int zombiePrioritySorter(Zombie zombie)
							{
								var score = maxRangeSquared - pos.DistanceToSquared(zombie.Position);
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
								return -score;
							}
							var losFlags = TargetScanFlags.NeedLOSToPawns | TargetScanFlags.NeedLOSToAll;
							__result = tickManager.allZombiesCached
								.Where(zombie =>
								{
									if (zombie.state == ZombieState.Emerging || zombie.IsRopedOrConfused)
										return false;
									if (canHarmElectricZombies == false && zombie.IsActiveElectric && zombie.Downed == false)
										return false;
									var d = pos.DistanceToSquared(zombie.Position);
									var dn = zombie.health.Downed;
									if (dn && (d > maxDownedRangeSquared || ZombieSettings.Values.doubleTapRequired == false))
										return false;
									if (dn == false && d > maxRangeSquared)
										return false;
									if (verb.CanHitTargetFrom(pos, zombie) == false)
										return false;
									if ((flags & losFlags) != 0 && attacker.CanSee(zombie, null) == false)
										return false;
									return true;
								})
								.OrderBy(zombiePrioritySorter).FirstOrDefault();
							return;
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

		static List<Thing> RemoveZombies(List<Thing> input) => input.Where(i => i is not Zombie).ToList();

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var m_GetThingList = SymbolExtensions.GetMethodInfo(() => GridsUtility.GetThingList(default, default));
			var list = instructions.ToList();
			var idx = list.FirstIndexOf(instruction => instruction.operand is MethodInfo method && method == m_GetThingList);
			list.Insert(idx + 1, CodeInstruction.Call(() => RemoveZombies(default)));
			return list.AsEnumerable();
		}
	}

	// patch to control if raiders and animals see zombies as hostile
	//
	[HarmonyPatch(typeof(GenHostility))]
	[HarmonyPatch(nameof(GenHostility.HostileTo))]
	[HarmonyPatch(new Type[] { typeof(Thing), typeof(Thing) })]
	static class GenHostility_HostileTo_Thing_Thing_Patch
	{
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
			if (t is ZombieSpitter && fac.IsPlayer == false)
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
	[HarmonyPatch(new Type[] { typeof(IAttackTarget), typeof(Faction) })]
	static class GenHostility_IsActiveThreat_Patch
	{
		[HarmonyPriority(Priority.First)]
		static bool Prefix(ref bool __result, IAttackTarget target, Faction faction)
		{
			if (target is not Zombie) // must skip non zombies bc next patch requires it
				return true;

			if (faction == Faction.OfPlayer)
			{
				__result = false; // fake non-hostile to prevent hostile count bc of zombies
				return false;
			}

			if (faction.HostileTo(Faction.OfPlayer))
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
				_ => throw new NotImplementedException(),
			};
			return false;
		}
	}
	//
	// but let drafted pawns attack zombies
	//
	[HarmonyPatch(typeof(JobDriver_Wait))]
	[HarmonyPatch(nameof(JobDriver_Wait.CheckForAutoAttack))]
	static class JobDriver_Wait_CheckForAutoAttack_Patch
	{
		static bool IsActiveThreatTo(IAttackTarget target, Faction faction)
		{
			if (target is Zombie zombie)
				return zombie.IsRopedOrConfused == false;
			if (target is ZombieSpitter)
				return faction.IsPlayer;
			return GenHostility.IsActiveThreatTo(target, faction); // ok to call patched method bc we filtered out zombies
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			return Tools.DownedReplacer(instructions, 1).MethodReplacer(
				SymbolExtensions.GetMethodInfo(() => GenHostility.IsActiveThreatTo(null, null)),
				SymbolExtensions.GetMethodInfo(() => IsActiveThreatTo(null, null))
			);
		}
	}

	static class TargetCachePatches
	{
		// used to prevent zombies from being counted as hostiles
		// both in map exist and for danger music
		//
		static readonly Dictionary<Map, HashSet<IAttackTarget>> playerHostilesWithoutZombies = new();

		// patch to remove the constant danger music because of the constant thread of zombies
		//
		[HarmonyPatch(typeof(AttackTargetsCache))]
		[HarmonyPatch(nameof(AttackTargetsCache.RegisterTarget))]
		static class AttackTargetsCache_RegisterTarget_Patch
		{
			static void Postfix(IAttackTarget target)
			{
				var thing = target.Thing;
				if (thing == null || thing is Zombie)
					return;
				if (thing.HostileTo(Faction.OfPlayer) == false)
					return;
				var map = thing.Map;
				if (map == null)
					return;
				if (playerHostilesWithoutZombies.ContainsKey(map) == false)
					playerHostilesWithoutZombies.Add(map, new HashSet<IAttackTarget>());
				_ = playerHostilesWithoutZombies[map].Add(target);
			}
		}

		[HarmonyPatch(typeof(AttackTargetsCache))]
		[HarmonyPatch(nameof(AttackTargetsCache.DeregisterTarget))]
		static class AttackTargetsCache_DeregisterTarget_Patch
		{
			static void Postfix(IAttackTarget target)
			{
				var thing = target.Thing;
				if (thing == null || thing is Zombie)
					return;
				var map = thing.Map;
				if (map == null)
					return;
				if (playerHostilesWithoutZombies.ContainsKey(map))
					_ = playerHostilesWithoutZombies[map].Remove(target);
			}
		}
	}
}