using HarmonyLib;
using RimBridgeServer.Sdk;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	public sealed partial class ZombielandBridgeTools
	{
		[Tool("zombieland/zombie_skin_color_contract", Description = "Verify Zombieland pawns short-circuit RimWorld skin-color/gene fallback and report white SkinColorBase while ordinary humans keep vanilla story color.")]
		public static object ZombieSkinColorContract()
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			var spawnedThings = new List<Thing>();
			try
			{
				_ = ZombieRuntimeActions.DestroyZombies(map);

				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var humanCell, out var humanError) == false)
					return humanError;
				if (TryFindClearSpawnCell(map, humanCell + new IntVec3(3, 0, 0), 10f, out var zombieCell, out var zombieError) == false)
					return zombieError;
				if (TryFindClearSpawnCell(map, humanCell + new IntVec3(0, 0, 3), 10f, out var spitterCell, out var spitterError) == false)
					return spitterError;
				if (TryFindClearSpawnCell(map, humanCell + new IntVec3(3, 0, 3), 10f, out var symbiantCell, out var symbiantError) == false)
					return symbiantError;

				var human = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(human, humanCell, map, Rot4.South);
				DisablePawnWork(human);
				spawnedThings.Add(human);
				var humanControlColor = new Color(0.25f, 0.2f, 0.15f, 1f);
				if (human.story != null)
					human.story.SkinColorBase = humanControlColor;

				var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
				if (zombie == null)
				{
					return new
					{
						success = false,
						error = "ZombieGenerator.SpawnZombie returned no skin-color test zombie."
					};
				}
				spawnedThings.Add(zombie);

				var existingSpitters = CurrentZombies(map).OfType<ZombieSpitter>()
					.Select(ZombieRuntimeActions.StableThingId)
					.ToHashSet(StringComparer.OrdinalIgnoreCase);
				ZombieSpitter.Spawn(map, spitterCell);
				var spitter = CurrentZombies(map).OfType<ZombieSpitter>()
					.FirstOrDefault(candidate => existingSpitters.Contains(ZombieRuntimeActions.StableThingId(candidate)) == false)
					?? CurrentZombies(map).OfType<ZombieSpitter>().OrderBy(candidate => candidate.Position.DistanceToSquared(spitterCell)).FirstOrDefault();
				if (spitter != null)
					spawnedThings.Add(spitter);

				var existingSymbiants = CurrentZombies(map).OfType<ZombieSymbiant>()
					.Select(ZombieRuntimeActions.StableThingId)
					.ToHashSet(StringComparer.OrdinalIgnoreCase);
				ZombieSymbiant.Spawn(map, symbiantCell);
				var symbiant = CurrentZombies(map).OfType<ZombieSymbiant>()
					.FirstOrDefault(candidate => existingSymbiants.Contains(ZombieRuntimeActions.StableThingId(candidate)) == false)
					?? CurrentZombies(map).OfType<ZombieSymbiant>().OrderBy(candidate => candidate.Position.DistanceToSquared(symbiantCell)).FirstOrDefault();
				if (symbiant != null)
					spawnedThings.Add(symbiant);

				var spitterStoryInjected = EnsureStoryTrackerForSkinColorProbe(spitter);
				var symbiantStoryInjected = EnsureStoryTrackerForSkinColorProbe(symbiant);

				var humanCase = DescribeSkinColorCase("human", human, humanControlColor, false, false);
				var zombielandCases = new[]
				{
					DescribeSkinColorCase("zombie", zombie, Color.white, true, false),
					DescribeSkinColorCase("spitter", spitter, Color.white, true, spitterStoryInjected),
					DescribeSkinColorCase("symbiant", symbiant, Color.white, true, symbiantStoryInjected)
				};

				return new
				{
					success = humanCase.success && zombielandCases.All(result => result.success),
					humanCase,
					zombielandCases
				};
			}
			finally
			{
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}
		}

		[Tool("zombieland/zombie_gene_rejection_contract", Description = "Verify Zombieland pawns reject both RimWorld Pawn_GeneTracker.AddGene overloads while ordinary humans keep the vanilla gene path.")]
		public static object ZombieGeneRejectionContract()
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			var spawnedThings = new List<Thing>();
			try
			{
				_ = ZombieRuntimeActions.DestroyZombies(map);

				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var humanCell, out var humanError) == false)
					return humanError;
				if (TryFindClearSpawnCell(map, humanCell + new IntVec3(3, 0, 0), 10f, out var zombieCell, out var zombieError) == false)
					return zombieError;
				if (TryFindClearSpawnCell(map, humanCell + new IntVec3(0, 0, 3), 10f, out var spitterCell, out var spitterError) == false)
					return spitterError;
				if (TryFindClearSpawnCell(map, humanCell + new IntVec3(3, 0, 3), 10f, out var symbiantCell, out var symbiantError) == false)
					return symbiantError;

				var privateAddGene = typeof(Pawn_GeneTracker).GetMethod(
					nameof(Pawn_GeneTracker.AddGene),
					BindingFlags.Instance | BindingFlags.NonPublic,
					null,
					new[] { typeof(Gene), typeof(bool) },
					null);
				if (privateAddGene == null)
				{
					return new
					{
						success = false,
						error = "Could not resolve private Pawn_GeneTracker.AddGene(Gene, bool)."
					};
				}

				var human = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(human, humanCell, map, Rot4.South);
				DisablePawnWork(human);
				spawnedThings.Add(human);

				var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
				if (zombie == null)
				{
					return new
					{
						success = false,
						error = "ZombieGenerator.SpawnZombie returned no gene test zombie."
					};
				}
				spawnedThings.Add(zombie);

				var existingSpitters = CurrentZombies(map).OfType<ZombieSpitter>()
					.Select(ZombieRuntimeActions.StableThingId)
					.ToHashSet(StringComparer.OrdinalIgnoreCase);
				ZombieSpitter.Spawn(map, spitterCell);
				var spitter = CurrentZombies(map).OfType<ZombieSpitter>()
					.FirstOrDefault(candidate => existingSpitters.Contains(ZombieRuntimeActions.StableThingId(candidate)) == false)
					?? CurrentZombies(map).OfType<ZombieSpitter>().OrderBy(candidate => candidate.Position.DistanceToSquared(spitterCell)).FirstOrDefault();
				if (spitter != null)
					spawnedThings.Add(spitter);

				var existingSymbiants = CurrentZombies(map).OfType<ZombieSymbiant>()
					.Select(ZombieRuntimeActions.StableThingId)
					.ToHashSet(StringComparer.OrdinalIgnoreCase);
				ZombieSymbiant.Spawn(map, symbiantCell);
				var symbiant = CurrentZombies(map).OfType<ZombieSymbiant>()
					.FirstOrDefault(candidate => existingSymbiants.Contains(ZombieRuntimeActions.StableThingId(candidate)) == false)
					?? CurrentZombies(map).OfType<ZombieSymbiant>().OrderBy(candidate => candidate.Position.DistanceToSquared(symbiantCell)).FirstOrDefault();
				if (symbiant != null)
					spawnedThings.Add(symbiant);

				var humanCase = DescribeGeneRejectionCase("human", human, false, privateAddGene);
				var zombielandCases = new[]
				{
					DescribeGeneRejectionCase("zombie", zombie, true, privateAddGene),
					DescribeGeneRejectionCase("spitter", spitter, true, privateAddGene),
					DescribeGeneRejectionCase("symbiant", symbiant, true, privateAddGene)
				};

				return new
				{
					success = humanCase.success && zombielandCases.All(result => result.success),
					biotechActive = ModsConfig.BiotechActive,
					humanCase,
					zombielandCases
				};
			}
			finally
			{
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}
		}

		[Tool("zombieland/fix_broken_chainsaw_job", Description = "Break a spawned chainsaw, run the real FixBrokenChainsaw workgiver/job with a component, and verify repair.")]
		public static object FixBrokenChainsawJob()
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			var destroyedZombies = ZombieRuntimeActions.DestroyZombies(map);
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var actorCell, out var actorSpawnError) == false)
				return actorSpawnError;

			var actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(actor, actorCell, map, WipeMode.Vanish);
			DisablePawnWork(actor);
			actor.equipment?.DestroyAllEquipment(DestroyMode.Vanish);
			actor.skills?.GetSkill(SkillDefOf.Construction).Notify_SkillDisablesChanged();
			actor.skills.GetSkill(SkillDefOf.Construction).Level = 20;

			if (TryFindAdjacentClearCell(actor, out var chainsawCell) == false)
			{
				return new
				{
					success = false,
					actor = DescribePawn(actor),
					error = "No adjacent cell was available for the broken chainsaw."
				};
			}

			var componentCell = actorCell + IntVec3.South;
			if (componentCell.InBounds(map) == false || componentCell.Standable(map) == false)
				componentCell = actorCell;

			var chainsaw = ThingMaker.MakeThing(CustomDefs.Chainsaw) as Chainsaw;
			if (chainsaw == null)
			{
				return new
				{
					success = false,
					error = "Could not create Chainsaw."
				};
			}
			GenSpawn.Spawn(chainsaw, chainsawCell, map, WipeMode.Vanish);
			var breakable = chainsaw.TryGetComp<CompBreakable>();
			if (breakable == null)
			{
				return new
				{
					success = false,
					chainsaw = ZombieRuntimeActions.StableThingId(chainsaw),
					error = "The spawned chainsaw did not have a breakable comp."
				};
			}
			breakable.DoBreakdown(map);
			map.areaManager.Home[chainsaw.Position] = true;
			chainsaw.SetForbidden(false, false);

			var component = ThingMaker.MakeThing(ThingDefOf.ComponentIndustrial);
			component.stackCount = 1;
			GenSpawn.Spawn(component, componentCell, map, WipeMode.Vanish);
			component.SetForbidden(false, false);

			var manager = map.GetComponent<BrokenManager>();
			var workGiver = new WorkGiver_FixBrokenChainsaw();
			var hasJob = workGiver.HasJobOnThing(actor, chainsaw, true);
			var job = hasJob ? workGiver.JobOnThing(actor, chainsaw, true) : null;
			if (job != null)
				job.playerForced = true;

			var started = job != null && actor.jobs.TryTakeOrderedJob(job, new JobTag?(JobTag.Misc), false);
			var maxTicks = 1250;
			var tickHit = -1;
			var samples = new List<object>();

			Rand.PushState(3);
			try
			{
				for (var tick = 1; tick <= maxTicks; tick++)
				{
					AdvanceGameTicks(1);
					var brokenNow = breakable.broken;
					if (tick == 1 || tick == maxTicks || tick % 200 == 0 || brokenNow == false)
					{
						samples.Add(new
						{
							tick,
							actorJob = actor.CurJobDef?.defName,
							broken = brokenNow,
							componentSpawned = component.Spawned,
							managerBrokenCount = manager?.brokenThings?.Count ?? 0
						});
					}

					if (brokenNow == false)
					{
						tickHit = tick;
						break;
					}
				}
			}
			finally
			{
				Rand.PopState();
			}

			var trackedAfter = manager?.brokenThings?.Contains(chainsaw) ?? false;

			return new
			{
				success = hasJob
					&& job != null
					&& started
					&& tickHit > 0
					&& breakable.broken == false
					&& trackedAfter == false
					&& component.Destroyed,
				destroyedZombies,
				actor = DescribePawn(actor),
				chainsaw = new
				{
					id = ZombieRuntimeActions.StableThingId(chainsaw),
					cell = ZombieRuntimeActions.DescribeCell(chainsawCell),
					spawned = chainsaw.Spawned,
					faction = chainsaw.Faction?.Name,
					forbidden = chainsaw.IsForbidden(actor),
					breakable.broken,
					trackedAsBroken = trackedAfter
				},
				component = new
				{
					id = ZombieRuntimeActions.StableThingId(component),
					cell = ZombieRuntimeActions.DescribeCell(componentCell),
					spawned = component.Spawned,
					destroyed = component.Destroyed
				},
				hasJob,
				jobDef = job?.def?.defName,
				started,
				maxTicks,
				tickHit,
				samples
			};
		}

		[Tool("zombieland/damage_dark_slimer", Description = "Apply real bullet damage to a dark slimer and verify the damage-worker patch creates custom TarSmoke.")]
		public static object DamageDarkSlimer(
			[ToolParameter(Description = "Optional dark slimer zombie id, ThingID, label, or short name. When omitted, a fresh dark slimer is spawned near map center.", Required = false, DefaultValue = "")] string target = "",
			[ToolParameter(Description = "Bullet damage amount.", Required = false, DefaultValue = 1)] int damage = 1)
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			Zombie darkSlimer;
			var spawnedDarkSlimer = false;
			if (string.IsNullOrWhiteSpace(target))
			{
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var cell, out var error) == false)
					return error;

				darkSlimer = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.DarkSlimer, true);
				spawnedDarkSlimer = true;
			}
			else if (TryFindZombie(map, target, out var pawn, out var error) == false)
			{
				return new
				{
					success = false,
					error
				};
			}
			else
			{
				darkSlimer = pawn as Zombie;
			}

			if (darkSlimer == null || darkSlimer.isDarkSlimer == false)
			{
				return new
				{
					success = false,
					target = DescribeZombie(darkSlimer),
					error = "Target is not a dark slimer."
				};
			}

			var cappedDamage = Math.Max(1, Math.Min(damage, 20));
			var position = darkSlimer.Position;
			var smokeRadius = 1f + Tools.Difficulty();
			var countRadius = smokeRadius + 1f;
			var ticksToRun = Math.Max(1, (int)Math.Ceiling(smokeRadius * 1.5f) + 2);
			var tarSmokeThingsBefore = CountThingsNear(map, position, CustomDefs.TarSmoke, countRadius);
			var gasAtPositionBefore = position.GetGas(map)?.def?.defName;
			var before = DescribeZombie(darkSlimer);
			var dinfo = new DamageInfo(DamageDefOf.Bullet, cappedDamage, 0f, -1f, null, null, null, DamageInfo.SourceCategory.ThingOrUnknown, null, true, true);
			var damageResult = darkSlimer.TakeDamage(dinfo);
			AdvanceGameTicks(ticksToRun);
			var tarSmokeThingsAfter = CountThingsNear(map, position, CustomDefs.TarSmoke, countRadius);
			var gasAtPositionAfter = position.GetGas(map)?.def?.defName;

			return new
			{
				success = tarSmokeThingsAfter > tarSmokeThingsBefore && gasAtPositionAfter == CustomDefs.TarSmoke.defName,
				spawnedDarkSlimer,
				damage = cappedDamage,
				damageTotal = damageResult.totalDamageDealt,
				smokeRadius,
				countRadius,
				ticksToRun,
				position = ZombieRuntimeActions.DescribeCell(position),
				gasAtPositionBefore,
				gasAtPositionAfter,
				tarSmokeThingsBefore,
				tarSmokeThingsAfter,
				tarSmokeThingDelta = tarSmokeThingsAfter - tarSmokeThingsBefore,
				before,
				after = DescribeZombie(darkSlimer)
			};
		}

		[Tool("zombieland/tar_smoke_blocks_ranged_targeting", Description = "Verify real TarSmoke from damaging a dark slimer blocks a real ranged verb from targeting that zombie.")]
		public static object TarSmokeBlocksRangedTargeting()
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			var destroyedZombies = ZombieRuntimeActions.DestroyZombies(map);
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var actorCell, out var actorSpawnError) == false)
				return actorSpawnError;

			var targetCell = GenRadial.RadialCellsAround(actorCell, 12f, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.Where(cell => cell.DistanceTo(actorCell) >= 7f)
				.Where(cell => GenSight.LineOfSight(actorCell, cell, map, true))
				.OrderBy(cell => cell.DistanceToSquared(actorCell))
				.FirstOrDefault();
			if (targetCell.IsValid == false)
			{
				return new
				{
					success = false,
					actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
					error = "No clear line-of-sight target cell was found for the TarSmoke targeting fixture."
				};
			}

			var actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(actor, actorCell, map, Rot4.South);
			DisablePawnWork(actor);
			actor.equipment?.DestroyAllEquipment(DestroyMode.Vanish);
			var weaponDef = DefDatabase<ThingDef>.GetNamed("Gun_BoltActionRifle", false)
				?? DefDatabase<ThingDef>.GetNamed("Gun_Pistol", false);
			var weapon = weaponDef == null ? null : ThingMaker.MakeThing(weaponDef) as ThingWithComps;
			if (weapon == null)
			{
				return new
				{
					success = false,
					actor = DescribePawn(actor),
					error = "No test ranged weapon def was available."
				};
			}
			actor.equipment.AddEquipment(weapon);
			actor.drafter.Drafted = true;

			var darkSlimer = ZombieRuntimeActions.SpawnZombie(targetCell, map, ZombieType.DarkSlimer, true);
			if (darkSlimer == null)
			{
				return new
				{
					success = false,
					actor = DescribePawn(actor),
					targetCell = ZombieRuntimeActions.DescribeCell(targetCell),
					error = "ZombieGenerator.SpawnZombie returned no dark slimer."
				};
			}

			var verb = actor.equipment?.PrimaryEq?.PrimaryVerb;
			if (verb == null)
			{
				return new
				{
					success = false,
					actor = DescribePawn(actor),
					weaponDef = weaponDef.defName,
					error = "The equipped ranged weapon had no primary verb."
				};
			}

			var canHitBeforeSmoke = verb.CanHitTargetFrom(actor.Position, darkSlimer);
			var aimChanceBeforeSmoke = ShotReport.HitReportFor(actor, verb, darkSlimer).AimOnTargetChance_StandardTarget;
			var gasAtTargetBefore = darkSlimer.Position.GetGas(map)?.def?.defName;
			var tarSmokeThingsBefore = CountThingsNear(map, darkSlimer.Position, CustomDefs.TarSmoke, 3f);
			var damageResult = darkSlimer.TakeDamage(new DamageInfo(DamageDefOf.Bullet, 1, 0f, -1f, actor, null, weaponDef, DamageInfo.SourceCategory.ThingOrUnknown, darkSlimer, true, true));
			AdvanceGameTicks(5);
			var gasAtTargetAfter = darkSlimer.Position.GetGas(map)?.def?.defName;
			var tarSmokeThingsAfter = CountThingsNear(map, darkSlimer.Position, CustomDefs.TarSmoke, 3f);
			var canHitAfterSmoke = verb.CanHitTargetFrom(actor.Position, darkSlimer);
			var aimChanceAfterSmoke = ShotReport.HitReportFor(actor, verb, darkSlimer).AimOnTargetChance_StandardTarget;

			return new
			{
				success = canHitBeforeSmoke
					&& aimChanceBeforeSmoke > 0f
					&& gasAtTargetBefore == null
					&& gasAtTargetAfter == CustomDefs.TarSmoke.defName
					&& tarSmokeThingsAfter > tarSmokeThingsBefore
					&& canHitAfterSmoke == false
					&& aimChanceAfterSmoke == 0f,
				destroyedZombies,
				actor = DescribePawn(actor),
				darkSlimer = DescribeZombie(darkSlimer),
				weaponDef = weaponDef.defName,
				verbLabel = verb.verbProps?.label,
				actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
				targetCell = ZombieRuntimeActions.DescribeCell(targetCell),
				canHitBeforeSmoke,
				canHitAfterSmoke,
				aimChanceBeforeSmoke,
				aimChanceAfterSmoke,
				gasAtTargetBefore,
				gasAtTargetAfter,
				tarSmokeThingsBefore,
				tarSmokeThingsAfter,
				tarSmokeDelta = tarSmokeThingsAfter - tarSmokeThingsBefore,
				damageTotal = damageResult.totalDamageDealt
			};
		}

		[Tool("zombieland/tar_smoke_blocks_human_ranged_targeting", Description = "Verify TarSmoke blocks ranged targeting for ordinary human targets too, matching its dense visual-obstruction role.")]
		public static object TarSmokeBlocksHumanRangedTargeting()
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var actorCell, out var actorSpawnError) == false)
				return actorSpawnError;

			var targetCell = GenRadial.RadialCellsAround(actorCell, 12f, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.Where(cell => cell.DistanceTo(actorCell) >= 7f)
				.Where(cell => GenSight.LineOfSight(actorCell, cell, map, true))
				.OrderBy(cell => cell.DistanceToSquared(actorCell))
				.FirstOrDefault();
			if (targetCell.IsValid == false)
			{
				return new
				{
					success = false,
					actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
					error = "No clear line-of-sight human target cell was found for the TarSmoke targeting fixture."
				};
			}

			ClearGasAt(map, targetCell);
			var actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(actor, actorCell, map, Rot4.South);
			DisablePawnWork(actor);
			actor.equipment?.DestroyAllEquipment(DestroyMode.Vanish);
			var weaponDef = DefDatabase<ThingDef>.GetNamed("Gun_BoltActionRifle", false)
				?? DefDatabase<ThingDef>.GetNamed("Gun_Pistol", false);
			var weapon = weaponDef == null ? null : ThingMaker.MakeThing(weaponDef) as ThingWithComps;
			if (weapon == null)
			{
				return new
				{
					success = false,
					actor = DescribePawn(actor),
					error = "No test ranged weapon def was available."
				};
			}
			actor.equipment.AddEquipment(weapon);
			actor.drafter.Drafted = true;

			var target = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(target, targetCell, map, Rot4.South);
			DisablePawnWork(target);

			var verb = actor.equipment?.PrimaryEq?.PrimaryVerb;
			if (verb == null)
			{
				return new
				{
					success = false,
					actor = DescribePawn(actor),
					weaponDef = weaponDef.defName,
					error = "The equipped ranged weapon had no primary verb."
				};
			}

			var canHitBeforeSmoke = verb.CanHitTargetFrom(actor.Position, target);
			var aimChanceBeforeSmoke = ShotReport.HitReportFor(actor, verb, target).AimOnTargetChance_StandardTarget;
			var gasAtTargetBefore = target.Position.GetGas(map)?.def?.defName;
			var smoke = GenSpawn.Spawn(ThingMaker.MakeThing(CustomDefs.TarSmoke), target.Position, map);
			var gasAtTargetAfter = target.Position.GetGas(map)?.def?.defName;
			var canHitAfterSmoke = verb.CanHitTargetFrom(actor.Position, target);
			var aimChanceAfterSmoke = ShotReport.HitReportFor(actor, verb, target).AimOnTargetChance_StandardTarget;

			return new
			{
				success = canHitBeforeSmoke
					&& aimChanceBeforeSmoke > 0f
					&& gasAtTargetBefore == null
					&& smoke?.def == CustomDefs.TarSmoke
					&& gasAtTargetAfter == CustomDefs.TarSmoke.defName
					&& canHitAfterSmoke == false
					&& aimChanceAfterSmoke == 0f,
				actor = DescribePawn(actor),
				target = DescribePawn(target),
				weaponDef = weaponDef.defName,
				verbLabel = verb.verbProps?.label,
				actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
				targetCell = ZombieRuntimeActions.DescribeCell(targetCell),
				smoke = ZombieRuntimeActions.StableThingId(smoke),
				canHitBeforeSmoke,
				canHitAfterSmoke,
				aimChanceBeforeSmoke,
				aimChanceAfterSmoke,
				gasAtTargetBefore,
				gasAtTargetAfter
			};
		}

		[Tool("zombieland/sticky_goo_toxic_buildup_contract", Description = "Move a real colonist off StickyGoo and verify the Position patch applies source-derived toxic buildup.")]
		public static object StickyGooToxicBuildupContract()
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var startCell, out var spawnError) == false)
				return spawnError;

			static float ToxicBuildupSeverity(Pawn pawn)
			{
				return pawn.health?.hediffSet?.GetFirstHediffOfDef(HediffDefOf.ToxicBuildup)?.Severity ?? 0f;
			}

			static void RemoveStickyGooAt(Map map, IntVec3 cell)
			{
				foreach (var thing in cell.GetThingList(map).Where(thing => thing.def == CustomDefs.StickyGoo).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}

			bool TryFindMovePair(Pawn pawn, out IntVec3 cleanDestination, out IntVec3 gooDestination)
			{
				cleanDestination = IntVec3.Invalid;
				gooDestination = IntVec3.Invalid;
				foreach (var cleanOffset in GenAdj.AdjacentCells)
				{
					var cleanCandidate = pawn.Position + cleanOffset;
					if (cleanCandidate.InBounds(map) == false || cleanCandidate.Standable(map) == false || cleanCandidate.Fogged(map))
						continue;
					if (cleanCandidate.GetThingList(map).Any(thing => thing is Pawn && thing != pawn))
						continue;
					foreach (var gooOffset in GenAdj.AdjacentCells)
					{
						var gooCandidate = cleanCandidate + gooOffset;
						if (gooCandidate == pawn.Position)
							continue;
						if (gooCandidate.InBounds(map) == false || gooCandidate.Standable(map) == false || gooCandidate.Fogged(map))
							continue;
						if (gooCandidate.GetThingList(map).Any(thing => thing is Pawn && thing != pawn))
							continue;
						cleanDestination = cleanCandidate;
						gooDestination = gooCandidate;
						return true;
					}
				}
				return false;
			}

			var actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(actor, startCell, map, WipeMode.Vanish);
			DisablePawnWork(actor);
			actor.jobs?.EndCurrentJob(JobCondition.InterruptForced, false, true);
			if (TryFindMovePair(actor, out var cleanDestination, out var gooDestination) == false)
			{
				return new
				{
					success = false,
					actor = DescribePawn(actor),
					error = "No two-step movement fixture was available for StickyGoo toxic buildup."
				};
			}

			RemoveStickyGooAt(map, startCell);
			RemoveStickyGooAt(map, cleanDestination);
			RemoveStickyGooAt(map, gooDestination);

			var beforeCleanMove = ToxicBuildupSeverity(actor);
			actor.Position = cleanDestination;
			actor.Notify_Teleported(false, false);
			var afterCleanMove = ToxicBuildupSeverity(actor);

			var madeGoo = FilthMaker.TryMakeFilth(cleanDestination, map, CustomDefs.StickyGoo, actor.Name?.ToStringShort, 1);
			var stickyGooCount = cleanDestination.GetThingList(map).Count(thing => thing.def == CustomDefs.StickyGoo);
			var expectedPerFilth = 0.023006668f * Mathf.Max(1f - actor.GetStatValue(StatDefOf.ToxicResistance, true, -1), 0f);
			if (ModsConfig.BiotechActive)
				expectedPerFilth *= Mathf.Max(1f - actor.GetStatValue(StatDefOf.ToxicEnvironmentResistance, true, -1), 0f);
			var expectedDelta = expectedPerFilth * stickyGooCount;

			var beforeGooMove = ToxicBuildupSeverity(actor);
			actor.Position = gooDestination;
			actor.Notify_Teleported(false, false);
			var afterGooMove = ToxicBuildupSeverity(actor);
			var cleanDelta = afterCleanMove - beforeCleanMove;
			var gooDelta = afterGooMove - beforeGooMove;
			var tolerance = 0.0001f;

			return new
			{
				success = Mathf.Abs(cleanDelta) <= tolerance
					&& madeGoo
					&& stickyGooCount > 0
					&& expectedDelta > 0f
					&& Mathf.Abs(gooDelta - expectedDelta) <= tolerance,
				sourcePath = "Thing.Position setter prefix -> StickyGoo at pawn.Position -> HealthUtility.AdjustSeverity(ToxicBuildup)",
				actor = DescribePawn(actor),
				cells = new
				{
					start = ZombieRuntimeActions.DescribeCell(startCell),
					cleanDestination = ZombieRuntimeActions.DescribeCell(cleanDestination),
					gooDestination = ZombieRuntimeActions.DescribeCell(gooDestination)
				},
				madeGoo,
				stickyGooCount,
				expectedPerFilth,
				expectedDelta,
				beforeCleanMove,
				afterCleanMove,
				cleanDelta,
				beforeGooMove,
				afterGooMove,
				gooDelta
			};
		}

		[Tool("zombieland/position_side_effects_contract", Description = "Verify remaining Thing.Position prefix side effects as one reusable patch-row contract: spitter trail, zombie clogging, colonist contact, attraction gates, and optional vehicle hook.")]
		public static object PositionSideEffectsContract()
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			var spawnedThings = new List<Thing>();
			var touchedCenters = new List<IntVec3>();
			var settingsSnapshot = SnapshotZombieSettings();
			var tickManager = map.GetComponent<TickManager>();
			var originalAvoidGrid = tickManager?.avoidGrid;

			try
			{
				var positionSetter = typeof(Thing).GetProperty(nameof(Thing.Position), BindingFlags.Instance | BindingFlags.Public)?.GetSetMethod();
				var patchOwners = PatchOwners(positionSetter);
				var patchTargets = PatchedMethodsForPatchClass("Thing_Position_Patch");

				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				ApplyZombieSettingsOverride(values =>
				{
					values.spitterThreat = Mathf.Max(values.spitterThreat, 1f);
				});

				var spitterTrail = VerifyPositionSpitterTrail(map, root + new IntVec3(-18, 0, -18), spawnedThings, touchedCenters);
				var ordinaryZombieClogging = VerifyPositionZombieClogging(map, root + new IntVec3(-6, 0, -18), spawnedThings, touchedCenters);
				var colonistContact = VerifyPositionColonistContact(map, root + new IntVec3(6, 0, -18), spawnedThings, touchedCenters);
				var attractionGates = VerifyPositionAttractionGates(map, root + new IntVec3(18, 0, -18), spawnedThings, touchedCenters);
				var customSupportOverrides = VerifyCustomSupportOverrideGates(map, root + new IntVec3(18, 0, 0), spawnedThings, touchedCenters);
				var vehicleHook = VerifyPositionVehicleHook(map, root + new IntVec3(-18, 0, 0), spawnedThings, touchedCenters);

				return new
				{
					success = patchOwners.Contains("net.pardeike.zombieland")
						&& patchTargets.Length > 0
						&& ObjectSuccess(spitterTrail)
						&& ObjectSuccess(ordinaryZombieClogging)
						&& ObjectSuccess(colonistContact)
						&& ObjectSuccess(attractionGates)
						&& ObjectSuccess(customSupportOverrides)
						&& ObjectSuccess(vehicleHook),
					sourcePath = "Thing.Position setter prefix -> spitter pheromone trail, ordinary zombie clogging, colonist contact timestamp, attraction gates, optional ZombielandSupport gates, and optional Vehicle Framework timestamp hook",
					patchOwners,
					patchTargets,
					spitterTrail,
					ordinaryZombieClogging,
					colonistContact,
					attractionGates,
					customSupportOverrides,
					vehicleHook
				};
			}
			finally
			{
				if (tickManager != null)
					tickManager.avoidGrid = originalAvoidGrid;
				RestoreZombieSettings(settingsSnapshot);
				foreach (var center in touchedCenters)
					if (center.InBounds(map))
						ClearPheromonesAndZombieCounts(map, center, 36f);
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}
		}

		static object VerifyPositionSpitterTrail(Map map, IntVec3 root, List<Thing> spawnedThings, List<IntVec3> touchedCenters)
		{
			if (TryFindClearSpawnCell(map, root, 18f, out var startCell, out var spawnError) == false)
				return spawnError;

			var spitter = SpawnFireFixturePawn(map, startCell, "spitter") as ZombieSpitter;
			if (spitter == null)
			{
				return new
				{
					success = false,
					error = "Could not spawn a spitter for the Thing.Position trail probe."
				};
			}
			spawnedThings.Add(spitter);
			touchedCenters.Add(startCell);

			if (TryFindAdjacentMoveCell(spitter, out var destination) == false)
			{
				return new
				{
					success = false,
					spitter = DescribeZombie(spitter),
					error = "No adjacent destination was available for the spitter trail probe."
				};
			}

			var radius = GenMath.LerpDouble(0, 5, 4, 32, ZombieSettings.Values.spitterThreat);
			ClearPheromones(map, destination, radius + 2f);
			var before = SnapshotPheromones(map, destination, radius + 2f);
			MovePawnThroughPositionSetter(spitter, destination);
			var change = DescribePheromoneChange(map, before, out var changedCount);

			return new
			{
				success = changedCount > 0,
				spitter = DescribeZombie(spitter),
				start = ZombieRuntimeActions.DescribeCell(startCell),
				destination = ZombieRuntimeActions.DescribeCell(destination),
				radius,
				change
			};
		}

		static object VerifyPositionZombieClogging(Map map, IntVec3 root, List<Thing> spawnedThings, List<IntVec3> touchedCenters)
		{
			if (TryFindClearSpawnCell(map, root, 18f, out var startCell, out var spawnError) == false)
				return spawnError;

			var zombie = ZombieRuntimeActions.SpawnZombie(startCell, map, ZombieType.Normal, true);
			if (zombie == null)
			{
				return new
				{
					success = false,
					error = "Could not spawn an ordinary zombie for the Thing.Position clogging probe."
				};
			}
			spawnedThings.Add(zombie);
			touchedCenters.Add(startCell);

			if (TryFindAdjacentMoveCell(zombie, out var destination) == false)
			{
				return new
				{
					success = false,
					zombie = DescribeZombie(zombie),
					error = "No adjacent destination was available for the ordinary zombie clogging probe."
				};
			}

			var grid = map.GetGrid();
			var zombieCount = 3;
			var timestampBefore = Tools.Ticks();
			SetZombieCount(map, destination, zombieCount);
			grid.SetTimestamp(destination, timestampBefore);
			MovePawnThroughPositionSetter(zombie, destination);
			var timestampAfter = grid.GetTimestamp(destination);
			var expected = Math.Max(timestampBefore - zombieCount * Constants.ZOMBIE_CLOGGING_FACTOR, Tools.Ticks() - Tools.PheromoneFadeoff());

			return new
			{
				success = timestampAfter == expected,
				zombie = DescribeZombie(zombie),
				start = ZombieRuntimeActions.DescribeCell(startCell),
				destination = ZombieRuntimeActions.DescribeCell(destination),
				zombieCount,
				cloggingFactor = Constants.ZOMBIE_CLOGGING_FACTOR,
				timestampBefore,
				timestampAfter,
				expected,
				delta = timestampBefore - timestampAfter
			};
		}

		static object VerifyPositionColonistContact(Map map, IntVec3 root, List<Thing> spawnedThings, List<IntVec3> touchedCenters)
		{
			if (TryFindClearSpawnCell(map, root, 18f, out var zombieCell, out var spawnError) == false)
				return spawnError;

			var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
			if (zombie == null)
			{
				return new
				{
					success = false,
					error = "Could not spawn a zombie for the colonist contact probe."
				};
			}
			spawnedThings.Add(zombie);
			touchedCenters.Add(zombieCell);

			if (TryFindAdjacentClearCell(zombie, out var colonistCell) == false)
			{
				return new
				{
					success = false,
					zombie = DescribeZombie(zombie),
					error = "No adjacent colonist cell was available for the contact probe."
				};
			}

			var colonist = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(colonist, colonistCell, map, Rot4.South);
			DisablePawnWork(colonist);
			spawnedThings.Add(colonist);

			var tickManager = map.GetComponent<TickManager>();
			var avoidGrid = BuildAvoidGridForZombie(map, zombie as Zombie);
			var avoidCost = AvoidCost(avoidGrid, map, colonist.Position);
			var inDangerBefore = avoidGrid.InAvoidDanger(colonist);
			var beforeContact = tickManager.lastZombieContact;
			tickManager.lastZombieContact = -12345;

			if (TryFindAdjacentMoveCell(colonist, out var destination) == false)
			{
				return new
				{
					success = false,
					colonist = DescribePawn(colonist),
					avoidCost,
					inDangerBefore,
					error = "No adjacent destination was available for the colonist contact probe."
				};
			}

			var ticksBeforeMove = GenTicks.TicksGame;
			MovePawnThroughPositionSetter(colonist, destination);
			var contactAfter = tickManager.lastZombieContact;

			return new
			{
				success = inDangerBefore && avoidCost > 0 && contactAfter == ticksBeforeMove,
				zombie = DescribeZombie(zombie),
				colonist = DescribePawn(colonist),
				start = ZombieRuntimeActions.DescribeCell(colonistCell),
				destination = ZombieRuntimeActions.DescribeCell(destination),
				avoidCost,
				inDangerBefore,
				beforeContact,
				ticksBeforeMove,
				contactAfter
			};
		}

		static object VerifyPositionAttractionGates(Map map, IntVec3 root, List<Thing> spawnedThings, List<IntVec3> touchedCenters)
		{
			var previousAttackMode = ZombieSettings.Values.attackMode;
			try
			{
				ApplyZombieSettingsOverride(values => values.attackMode = AttackMode.OnlyColonists);

				if (TryFindClearSpawnCell(map, root, 18f, out var colonistCell, out var colonistSpawnError) == false)
					return colonistSpawnError;
				if (TryFindClearSpawnCell(map, colonistCell + new IntVec3(5, 0, 0), 12f, out var nonColonistCell, out var nonColonistSpawnError) == false)
					return nonColonistSpawnError;

				var colonist = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(colonist, colonistCell, map, Rot4.South);
				DisablePawnWork(colonist);
				spawnedThings.Add(colonist);

				var nonColonist = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfAncientsHostile);
				GenSpawn.Spawn(nonColonist, nonColonistCell, map, Rot4.South);
				DisablePawnWork(nonColonist);
				spawnedThings.Add(nonColonist);
				touchedCenters.Add(colonistCell);
				touchedCenters.Add(nonColonistCell);

				var colonistTrail = VerifyPawnTrailCase(map, colonist, true);
				var nonColonistBlocked = VerifyPawnTrailCase(map, nonColonist, false);
				var manhunterStarted = nonColonist.mindState?.mentalStateHandler?.TryStartMentalState(MentalStateDefOf.Manhunter, "Zombieland bridge Thing.Position manhunter probe", true, true) ?? false;
				var manhunterCase = VerifyPawnTrailCase(map, nonColonist, false);

				return new
				{
					success = ObjectSuccess(colonistTrail)
						&& ObjectSuccess(nonColonistBlocked)
						&& ObjectSuccess(manhunterCase),
					attackMode = ZombieSettings.Values.attackMode.ToString(),
					colonistTrail,
					nonColonistBlocked,
					manhunterCase,
					manhunterStarted,
					semanticNote = "The Thing.Position prefix skips the direct attack-mode early return for manhunters, but the final Customization.DoesAttractsZombies gate still applies in the base mod without an external ZombielandSupport evaluator."
				};
			}
			finally
			{
				ApplyZombieSettingsOverride(values => values.attackMode = previousAttackMode);
			}
		}

		static object VerifyCustomSupportOverrideGates(Map map, IntVec3 root, List<Thing> spawnedThings, List<IntVec3> touchedCenters)
		{
			var testSupportAssemblyLoaded = AppDomain.CurrentDomain.GetAssemblies()
				.Any(assembly => assembly.GetName().Name == "ZLTestSupport");
			if (testSupportAssemblyLoaded == false)
			{
				return new
				{
					success = true,
					status = "skipped: ZLTestSupport is not installed in the active mod set"
				};
			}

			var previousAttackMode = ZombieSettings.Values.attackMode;
			try
			{
				ApplyZombieSettingsOverride(values => values.attackMode = AttackMode.OnlyColonists);

				if (TryFindClearSpawnCell(map, root, 18f, out var ignoredCell, out var ignoredSpawnError) == false)
					return ignoredSpawnError;
				if (TryFindClearSpawnCell(map, ignoredCell + new IntVec3(5, 0, 0), 12f, out var forcedCell, out var forcedSpawnError) == false)
					return forcedSpawnError;
				if (TryFindClearSpawnCell(map, ignoredCell + new IntVec3(0, 0, 5), 12f, out var controlCell, out var controlSpawnError) == false)
					return controlSpawnError;
				if (TryFindClearSpawnCell(map, ignoredCell + new IntVec3(5, 0, 5), 12f, out var blockedCell, out var blockedSpawnError) == false)
					return blockedSpawnError;
				if (TryFindClearSpawnCell(map, ignoredCell + new IntVec3(10, 0, 5), 12f, out var infectedControlCell, out var infectedControlSpawnError) == false)
					return infectedControlSpawnError;

				var ignoredColonist = GenerateNamedSupportPawn("ZL_IgnoreZombies", Faction.OfPlayer);
				GenSpawn.Spawn(ignoredColonist, ignoredCell, map, Rot4.South);
				DisablePawnWork(ignoredColonist);
				spawnedThings.Add(ignoredColonist);

				var forcedInfectedColonist = GenerateNamedSupportPawn("ZL_AttractsZombies", Faction.OfPlayer);
				forcedInfectedColonist.SetInfectionState(InfectionState.Infecting);
				GenSpawn.Spawn(forcedInfectedColonist, forcedCell, map, Rot4.South);
				DisablePawnWork(forcedInfectedColonist);
				spawnedThings.Add(forcedInfectedColonist);

				var controlColonist = GenerateNamedSupportPawn("ZL_Neutral", Faction.OfPlayer);
				GenSpawn.Spawn(controlColonist, controlCell, map, Rot4.South);
				DisablePawnWork(controlColonist);
				spawnedThings.Add(controlColonist);

				var blockedColonist = GenerateNamedSupportPawn("ZL_BlockZombie", Faction.OfPlayer);
				GenSpawn.Spawn(blockedColonist, blockedCell, map, Rot4.South);
				DisablePawnWork(blockedColonist);
				spawnedThings.Add(blockedColonist);

				var infectedControlColonist = GenerateNamedSupportPawn("ZL_InfectedNeutral", Faction.OfPlayer);
				infectedControlColonist.SetInfectionState(InfectionState.Infecting);
				GenSpawn.Spawn(infectedControlColonist, infectedControlCell, map, Rot4.South);
				DisablePawnWork(infectedControlColonist);
				spawnedThings.Add(infectedControlColonist);
				touchedCenters.Add(ignoredCell);
				touchedCenters.Add(forcedCell);
				touchedCenters.Add(controlCell);
				touchedCenters.Add(blockedCell);
				touchedCenters.Add(infectedControlCell);

				var ignoredTrail = VerifyPawnTrailCase(map, ignoredColonist, false);
				var forcedDoesAttractsZombies = Customization.DoesAttractsZombies(forcedInfectedColonist);
				var forcedTrail = VerifyPawnTrailCase(map, forcedInfectedColonist, false);
				var controlTrail = VerifyPawnTrailCase(map, controlColonist, true);
				var infectedControlDoesAttractsZombies = Customization.DoesAttractsZombies(infectedControlColonist);
				var infectedControlTrail = VerifyPawnTrailCase(map, infectedControlColonist, false);
				var blockedCannotBecomeZombie = Customization.CannotBecomeZombie(blockedColonist);
				var controlCannotBecomeZombie = Customization.CannotBecomeZombie(controlColonist);

				return new
				{
					success = ObjectSuccess(ignoredTrail)
						&& ObjectSuccess(forcedTrail)
						&& forcedDoesAttractsZombies
						&& ObjectSuccess(controlTrail)
						&& ObjectSuccess(infectedControlTrail)
						&& infectedControlDoesAttractsZombies == false
						&& blockedCannotBecomeZombie
						&& controlCannotBecomeZombie == false,
					status = "active: ZLTestSupport fixture return values affected attraction and infection gates",
					attackMode = ZombieSettings.Values.attackMode.ToString(),
					ignoredTrail,
					forcedTrail,
					forcedDoesAttractsZombies,
					controlTrail,
					infectedControlTrail,
					infectedControlDoesAttractsZombies,
					blockedCannotBecomeZombie,
					controlCannotBecomeZombie,
					blockedPawn = DescribePawn(blockedColonist),
					controlPawn = DescribePawn(controlColonist)
				};
			}
			finally
			{
				ApplyZombieSettingsOverride(values => values.attackMode = previousAttackMode);
			}
		}

		static Pawn GenerateNamedSupportPawn(string name, Faction faction)
		{
			var pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, faction);
			pawn.Name = new NameSingle(name);
			return pawn;
		}

		static object VerifyPawnTrailCase(Map map, Pawn pawn, bool expectTrail)
		{
			if (TryFindAdjacentMoveCell(pawn, out var destination) == false)
			{
				return new
				{
					success = false,
					pawn = DescribePawn(pawn),
					expectTrail,
					error = "No adjacent destination was available for the pawn trail probe."
				};
			}

			var radius = Tools.RadiusForPawn(pawn) + 2f;
			ClearPheromones(map, destination, radius);
			var before = SnapshotPheromones(map, destination, radius);
			MovePawnThroughPositionSetter(pawn, destination);
			var change = DescribePheromoneChange(map, before, out var changedCount);

			return new
			{
				success = expectTrail ? changedCount > 0 : changedCount == 0,
				pawn = DescribePawn(pawn),
				expectTrail,
				destination = ZombieRuntimeActions.DescribeCell(destination),
				doesAttractsZombies = Customization.DoesAttractsZombies(pawn),
				change
			};
		}

		static object VerifyPositionVehicleHook(Map map, IntVec3 root, List<Thing> spawnedThings, List<IntVec3> touchedCenters)
		{
			if (VehicleTools.vehicleType == null)
			{
				return new
				{
					success = true,
					vehicleFrameworkInstalled = false,
					vehicleType = (string)null,
					status = "skipped: Vehicle Framework is not installed in the active mod set"
				};
			}

			var vehicleDefType = VehicleTools.vehicleType.Assembly.GetType("Vehicles.VehicleDef");
			var vehicleSpawnerType = VehicleTools.vehicleType.Assembly.GetType("Vehicles.VehicleSpawner");
			var generateVehicle = vehicleSpawnerType?
				.GetMethods(BindingFlags.Public | BindingFlags.Static)
				.FirstOrDefault(method =>
				{
					if (method.Name != "GenerateVehicle")
						return false;
					var parameters = method.GetParameters();
					return parameters.Length == 2
						&& parameters[0].ParameterType == vehicleDefType
						&& parameters[1].ParameterType == typeof(Faction);
				});

			var concreteDefs = vehicleDefType == null
				? Array.Empty<ThingDef>()
				: DefDatabase<ThingDef>.AllDefsListForReading
					.Where(def => def.defName.NullOrEmpty() == false)
					.Where(def => vehicleDefType.IsAssignableFrom(def.GetType()))
					.Where(def => def.defName.StartsWith("ZLTest", StringComparison.OrdinalIgnoreCase))
					.ToArray();

			var vehicleDef = concreteDefs.FirstOrDefault();
			if (vehicleDefType == null || generateVehicle == null || vehicleDef == null)
			{
				return new
				{
					success = false,
					vehicleFrameworkInstalled = true,
					vehicleType = VehicleTools.vehicleType.FullName,
					vehicleDefType = vehicleDefType?.FullName,
					generateVehicleResolved = generateVehicle != null,
					concreteVehicleDefs = concreteDefs.Select(def => def.defName).ToArray(),
					error = "Vehicle Framework is active, but no local ZLTest concrete VehicleDef fixture was available for BumpTimestamps."
				};
			}

			if (TryFindClearSpawnCell(map, root, 16f, out var vehicleCell, out var spawnError) == false)
				return spawnError;

			Pawn vehicle;
			try
			{
				vehicle = generateVehicle.Invoke(null, new object[] { vehicleDef, Faction.OfPlayer }) as Pawn;
			}
			catch (Exception ex)
			{
				return new
				{
					success = false,
					vehicleFrameworkInstalled = true,
					vehicleType = VehicleTools.vehicleType.FullName,
					vehicleDef = vehicleDef.defName,
					error = $"VehicleSpawner.GenerateVehicle failed: {ex.GetType().Name}: {ex.Message}"
				};
			}

			if (vehicle == null)
			{
				return new
				{
					success = false,
					vehicleFrameworkInstalled = true,
					vehicleType = VehicleTools.vehicleType.FullName,
					vehicleDef = vehicleDef.defName,
					error = "VehicleSpawner.GenerateVehicle returned null."
				};
			}

			GenSpawn.Spawn(vehicle, vehicleCell, map, Rot4.South);
			spawnedThings.Add(vehicle);
			if (TryFindAdjacentClearCell(vehicle, out var destination) == false)
			{
				return new
				{
					success = false,
					vehicleFrameworkInstalled = true,
					vehicleType = VehicleTools.vehicleType.FullName,
					vehicleDef = vehicleDef.defName,
					vehicle = DescribePawn(vehicle),
					error = "No adjacent clear destination was available for the vehicle timestamp probe."
				};
			}

			float moveSpeed;
			try
			{
				moveSpeed = vehicle.GetMoveSpeed();
			}
			catch (Exception ex)
			{
				return new
				{
					success = false,
					vehicleFrameworkInstalled = true,
					vehicleType = VehicleTools.vehicleType.FullName,
					vehicleDef = vehicleDef.defName,
					vehicle = DescribePawn(vehicle),
					error = $"VehicleTools.GetMoveSpeed failed: {ex.GetType().Name}: {ex.Message}"
				};
			}

			var radius = Mathf.Max(2f, 1.5f * moveSpeed + 1f);
			ClearPheromones(map, destination, radius);
			touchedCenters.Add(destination);
			var before = SnapshotPheromones(map, destination, radius);
			MovePawnThroughPositionSetter(vehicle, destination);
			var timestampAfter = map.GetGrid().GetTimestamp(destination);
			var change = DescribePheromoneChange(map, before, out var changedCount);

			return new
			{
				success = changedCount > 0 && timestampAfter > 0,
				vehicleFrameworkInstalled = true,
				vehicleType = VehicleTools.vehicleType.FullName,
				vehicleDef = vehicleDef.defName,
				vehicle = DescribePawn(vehicle),
				cells = new
				{
					start = ZombieRuntimeActions.DescribeCell(vehicleCell),
					destination = ZombieRuntimeActions.DescribeCell(destination)
				},
				moveSpeed,
				radius,
				timestampAfter,
				change,
				status = "proved: concrete VehiclePawn movement through Thing.Position called VehicleTools.BumpTimestamps"
			};
		}

		static void MovePawnThroughPositionSetter(Pawn pawn, IntVec3 destination)
		{
			pawn.Position = destination;
			pawn.Notify_Teleported(false, false);
		}

		static void SetZombieCount(Map map, IntVec3 cell, int count)
		{
			var grid = map.GetGrid();
			var current = grid.GetZombieCount(cell);
			if (current != count)
				grid.ChangeZombieCount(cell, count - current);
		}

		static void ClearPheromonesAndZombieCounts(Map map, IntVec3 center, float radius)
		{
			var grid = map.GetGrid();
			foreach (var cell in GenRadial.RadialCellsAround(center, radius, true))
			{
				if (cell.InBounds(map) == false)
					continue;
				grid.SetTimestamp(cell, 0);
				var zombieCount = grid.GetZombieCount(cell);
				if (zombieCount != 0)
					grid.ChangeZombieCount(cell, -zombieCount);
			}
		}

		[Tool("zombieland/mine_with_miner", Description = "Place a mineable block next to a miner zombie and verify Zombieland's mining code damages it.")]
		public static object MineWithMiner(
			[ToolParameter(Description = "Optional miner zombie id, ThingID, label, or short name. When omitted, a fresh miner is spawned near map center.", Required = false, DefaultValue = "")] string target = "")
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			Zombie miner;
			var spawnedMiner = false;
			if (string.IsNullOrWhiteSpace(target))
			{
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var cell, out var error) == false)
					return error;

				miner = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.Miner, true);
				spawnedMiner = true;
			}
			else if (TryFindZombie(map, target, out var pawn, out var error) == false)
			{
				return new
				{
					success = false,
					error
				};
			}
			else
			{
				miner = pawn as Zombie;
			}

			if (miner == null || miner.isMiner == false)
			{
				return new
				{
					success = false,
					target = DescribeZombie(miner),
					error = "Target is not a miner."
				};
			}

			if (TryFindAdjacentClearCell(miner, out var mineableCell) == false)
			{
				return new
				{
					success = false,
					target = DescribeZombie(miner),
					error = "No clear adjacent cell was found for the mineable test block."
				};
			}

			var mineable = GenSpawn.Spawn(ThingDefOf.MineableSteel, mineableCell, map, WipeMode.Vanish) as Mineable;
			if (mineable == null)
			{
				return new
				{
					success = false,
					target = DescribeZombie(miner),
					cell = ZombieRuntimeActions.DescribeCell(mineableCell),
					error = "Spawning MineableSteel did not produce a Mineable."
				};
			}

			var hitPointsBefore = mineable.HitPoints;
			var miningCounterBefore = miner.miningCounter;
			var mined = ZombieStateHandler.Mine(null, miner, true);
			var mineableDestroyed = mineable.Destroyed;
			var hitPointsAfter = mineableDestroyed ? 0 : mineable.HitPoints;
			var miningCounterAfter = miner.miningCounter;

			return new
			{
				success = mined && hitPointsAfter < hitPointsBefore && miningCounterAfter > miningCounterBefore,
				spawnedMiner,
				mined,
				miner = DescribeZombie(miner),
				mineableCell = ZombieRuntimeActions.DescribeCell(mineableCell),
				mineableDef = mineable.def.defName,
				mineableDestroyed,
				hitPointsBefore,
				hitPointsAfter,
				hitPointDelta = hitPointsAfter - hitPointsBefore,
				miningCounterBefore,
				miningCounterAfter
			};
		}

		[Tool("zombieland/mine_with_miner_job", Description = "Put a mineable in a miner's wander direction and verify the real Stumble job mines it.")]
		public static object MineWithMinerJob(
			[ToolParameter(Description = "Optional miner zombie id, ThingID, label, or short name. When omitted, a fresh miner is spawned near map center.", Required = false, DefaultValue = "")] string target = "")
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			Zombie miner;
			var spawnedMiner = false;
			if (string.IsNullOrWhiteSpace(target))
			{
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var cell, out var error) == false)
					return error;

				miner = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.Miner, true);
				spawnedMiner = true;
			}
			else if (TryFindZombie(map, target, out var pawn, out var error) == false)
			{
				return new
				{
					success = false,
					error
				};
			}
			else
			{
				miner = pawn as Zombie;
			}

			if (miner == null || miner.isMiner == false)
			{
				return new
				{
					success = false,
					target = DescribeZombie(miner),
					error = "Target is not a miner."
				};
			}

			if (TryFindAdjacentClearCell(miner, out var mineableCell) == false)
			{
				return new
				{
					success = false,
					target = DescribeZombie(miner),
					error = "No clear adjacent cell was found for the mineable test block."
				};
			}

			var mineable = GenSpawn.Spawn(ThingDefOf.MineableSteel, mineableCell, map, WipeMode.Vanish) as Mineable;
			if (mineable == null)
			{
				return new
				{
					success = false,
					target = DescribeZombie(miner),
					cell = ZombieRuntimeActions.DescribeCell(mineableCell),
					error = "Spawning MineableSteel did not produce a Mineable."
				};
			}

			var bodyTypeBefore = miner.story?.bodyType?.defName;
			if (miner.story != null)
				miner.story.bodyType = BodyTypeDefOf.Male;
			miner.pather?.StopDead();
			miner.jobs?.EndCurrentJob(JobCondition.InterruptForced);
			miner.state = ZombieState.Wandering;
			miner.wanderDestination = mineableCell;
			miner.miningCounter = 0;
			var clearedPheromoneRadius = 2f;
			ClearPheromones(map, miner.Position, clearedPheromoneRadius);

			var before = DescribeZombie(miner);
			var hitPointsBefore = mineable.HitPoints;
			var samples = new List<object>();
			miner.jobs.StartJob(JobMaker.MakeJob(CustomDefs.Stumble), JobCondition.InterruptForced, null, true, false, null, null);
			if (miner.jobs.curDriver is JobDriver_Stumble stumbleDriver)
				stumbleDriver.destination = IntVec3.Invalid;

			for (var i = 0; i < 2; i++)
			{
				AdvanceGameTicks(1);
				var currentJob = miner.CurJobDef?.defName;
				var stumbleDestination = miner.jobs.curDriver is JobDriver_Stumble currentStumbleDriver
					? currentStumbleDriver.destination
					: IntVec3.Invalid;
				samples.Add(new
				{
					tick = i + 1,
					currentJob,
					stumbleDestination = ZombieRuntimeActions.DescribeCell(stumbleDestination),
					mineableDestroyed = mineable.Destroyed,
					mineableHitPoints = mineable.Destroyed ? 0 : mineable.HitPoints,
					miner.miningCounter
				});
				if (mineable.Destroyed || mineable.HitPoints < hitPointsBefore)
					break;
			}

			var mineableDestroyed = mineable.Destroyed;
			var hitPointsAfter = mineableDestroyed ? 0 : mineable.HitPoints;

			return new
			{
				success = (mineableDestroyed || hitPointsAfter < hitPointsBefore) && miner.miningCounter > 0,
				spawnedMiner,
				bodyTypeBefore,
				bodyTypeDuringTest = miner.story?.bodyType?.defName,
				clearedPheromoneRadius,
				minerCell = ZombieRuntimeActions.DescribeCell(miner.Position),
				mineableCell = ZombieRuntimeActions.DescribeCell(mineableCell),
				mineableDef = mineable.def.defName,
				mineableDestroyed,
				hitPointsBefore,
				hitPointsAfter,
				hitPointDelta = hitPointsAfter - hitPointsBefore,
				miningCounterAfter = miner.miningCounter,
				before,
				after = DescribeZombie(miner),
				samples
			};
		}

		[Tool("zombieland/move_tanky", Description = "Move a tanky zombie one valid adjacent cell and verify that it leaves a pheromone trace for other zombies.")]
		public static object MoveTanky(
			[ToolParameter(Description = "Optional tanky zombie id, ThingID, label, or short name. When omitted, a fresh tanky zombie is spawned near map center.", Required = false, DefaultValue = "")] string target = "")
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			Zombie tanky;
			var spawnedTanky = false;
			if (string.IsNullOrWhiteSpace(target))
			{
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var cell, out var error) == false)
					return error;

				tanky = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.TankyOperator, true);
				spawnedTanky = true;
			}
			else if (TryFindZombie(map, target, out var pawn, out var error) == false)
			{
				return new
				{
					success = false,
					error
				};
			}
			else
			{
				tanky = pawn as Zombie;
			}

			if (tanky == null || tanky.IsTanky == false)
			{
				return new
				{
					success = false,
					target = DescribeZombie(tanky),
					error = "Target is not a tanky zombie."
				};
			}

			if (TryFindAdjacentMoveCell(tanky, out var destination) == false)
			{
				return new
				{
					success = false,
					target = DescribeZombie(tanky),
					error = "No valid adjacent move cell was found."
				};
			}

			var radius = Constants.TANKY_PHEROMONE_RADIUS + 1f;
			var before = DescribeZombie(tanky);
			var origin = tanky.Position;
			ClearPheromones(map, destination, radius);
			var pheromonesBefore = SnapshotPheromones(map, destination, radius);
			tanky.pather?.StopDead();
			tanky.Position = destination;
			tanky.Notify_Teleported(false, false);
			var pheromoneChange = DescribePheromoneChange(map, pheromonesBefore, out var changedCount);

			return new
			{
				success = tanky.Position == destination && changedCount > 0,
				spawnedTanky,
				radius,
				origin = ZombieRuntimeActions.DescribeCell(origin),
				destination = ZombieRuntimeActions.DescribeCell(destination),
				before,
				after = DescribeZombie(tanky),
				pheromoneChange
			};
		}

		[Tool("zombieland/damage_albino", Description = "Apply real bullet and explosive damage to an albino zombie and verify its damage filter blocks only non-explosive hits.")]
		public static object DamageAlbino(
			[ToolParameter(Description = "Optional albino zombie id, ThingID, label, or short name. When omitted, a fresh albino zombie is spawned near map center.", Required = false, DefaultValue = "")] string target = "",
			[ToolParameter(Description = "Deterministic Rand seed for the repeated bullet damage sample.", Required = false, DefaultValue = 31337)] int seed = 31337,
			[ToolParameter(Description = "Number of one-damage bullet attempts to sample.", Required = false, DefaultValue = 20)] int bulletAttempts = 20)
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			Zombie albino;
			var spawnedAlbino = false;
			IntVec3 spawnRoot;
			if (string.IsNullOrWhiteSpace(target))
			{
				spawnRoot = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, spawnRoot, 16f, out var cell, out var error) == false)
					return error;

				albino = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.Albino, true);
				spawnedAlbino = true;
			}
			else if (TryFindZombie(map, target, out var pawn, out var error) == false)
			{
				return new
				{
					success = false,
					error
				};
			}
			else
			{
				albino = pawn as Zombie;
				spawnRoot = albino?.Position ?? new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			}

			if (albino == null || albino.isAlbino == false)
			{
				return new
				{
					success = false,
					target = DescribeZombie(albino),
					error = "Target is not an albino zombie."
				};
			}

			var cappedAttempts = Math.Max(4, Math.Min(bulletAttempts, 60));
			var before = DescribeZombie(albino);
			var bulletDamageTotals = new List<float>(cappedAttempts);
			Rand.PushState(seed);
			try
			{
				for (var i = 0; i < cappedAttempts; i++)
				{
					var dinfo = new DamageInfo(DamageDefOf.Bullet, 1f, 0f, -1f, null, null, null, DamageInfo.SourceCategory.ThingOrUnknown, null, true, true);
					bulletDamageTotals.Add(albino.TakeDamage(dinfo).totalDamageDealt);
					if ((bulletDamageTotals.Any(total => total > 0f) && bulletDamageTotals.Any(total => total <= 0f)) || albino.Dead)
						break;
				}
			}
			finally
			{
				Rand.PopState();
			}

			var explosiveAlbino = albino;
			var spawnedExplosiveAlbino = false;
			if (albino.Dead || string.IsNullOrWhiteSpace(target) == false)
			{
				if (TryFindClearSpawnCell(map, spawnRoot + new IntVec3(3, 0, 0), 16f, out var explosiveCell, out var explosiveError) == false)
					return explosiveError;
				explosiveAlbino = ZombieRuntimeActions.SpawnZombie(explosiveCell, map, ZombieType.Albino, true);
				spawnedExplosiveAlbino = true;
			}
			var explosiveBefore = DescribeZombie(explosiveAlbino);
			var explosiveInfo = new DamageInfo(DamageDefOf.Bomb, 1f, 0f, -1f, null, null, null, DamageInfo.SourceCategory.ThingOrUnknown, null, true, true);
			var explosiveDamage = explosiveAlbino.TakeDamage(explosiveInfo).totalDamageDealt;
			var bulletHits = bulletDamageTotals.Count(total => total > 0f);
			var bulletBlocked = bulletDamageTotals.Count(total => total <= 0f);

			return new
			{
				success = bulletHits > 0 && bulletBlocked > 0 && explosiveDamage > 0f,
				spawnedAlbino,
				spawnedExplosiveAlbino,
				seed,
				bulletAttempts = bulletDamageTotals.Count,
				bulletHits,
				bulletBlocked,
				bulletDamageTotal = bulletDamageTotals.Sum(),
				bulletDamageTotals = bulletDamageTotals.ToArray(),
				explosiveDamage,
				before,
				after = DescribeZombie(albino),
				explosiveBefore,
				explosiveAfter = DescribeZombie(explosiveAlbino)
			};
		}

		[Tool("zombieland/scream_with_albino", Description = "Start a real albino sabotage job and verify its 40-tick scream pulse forces a nearby colonist to vomit and stuns them.")]
		public static object ScreamWithAlbino()
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			var colonist = map.mapPawns.FreeColonists
				.Where(pawn => pawn.Spawned && pawn.Dead == false && pawn.health.Downed == false && pawn.InMentalState == false)
				.OrderBy(pawn => pawn.Position.x)
				.ThenBy(pawn => pawn.Position.z)
				.FirstOrDefault();
			if (colonist == null)
			{
				return new
				{
					success = false,
					error = "No spawned free colonist was available as an albino scream target."
				};
			}

			if (TryFindAdjacentClearCell(colonist, out var albinoCell) == false)
			{
				return new
				{
					success = false,
					colonist = DescribePawn(colonist),
					error = "No clear adjacent cell was found for the albino scream test."
				};
			}

			var albino = ZombieRuntimeActions.SpawnZombie(albinoCell, map, ZombieType.Albino, true);
			if (albino == null)
			{
				return new
				{
					success = false,
					colonist = DescribePawn(colonist),
					error = "ZombieGenerator.SpawnZombie returned no albino test zombie."
				};
			}
			albino.SetFaction(Faction.OfPlayer);

			var jobBefore = colonist.CurJobDef?.defName;
			var stunnedBefore = colonist.stances?.stunner?.Stunned ?? false;
			albino.jobs.StartJob(JobMaker.MakeJob(CustomDefs.Sabotage), JobCondition.InterruptForced, null, true, true);
			AdvanceGameTicks(1);

			var driver = albino.jobs.curDriver as JobDriver_Sabotage;
			if (driver == null)
			{
				return new
				{
					success = false,
					albino = DescribeZombie(albino),
					colonist = DescribePawn(colonist),
					error = "Albino did not enter the sabotage job driver."
				};
			}

			albino.pather?.StopDead();
			if (albino.Position != albinoCell)
			{
				albino.Position = albinoCell;
				albino.Notify_Teleported(false, false);
			}
			driver.destination = IntVec3.Invalid;
			driver.door = null;
			driver.hackTarget = null;
			driver.waitCounter = 0;
			driver.hackCounter = 0;
			albino.scream = 0;
			var pulseTick = 40;
			var samples = new List<object>();
			for (var tick = 1; tick <= pulseTick; tick++)
			{
				AdvanceGameTicks(1);
				if (tick == 1 || tick == pulseTick || tick % 10 == 0)
				{
					samples.Add(new
					{
						tick,
						scream = albino.scream,
						colonistJob = colonist.CurJobDef?.defName,
						colonistStunned = colonist.stances?.stunner?.Stunned ?? false
					});
				}
			}

			var jobAfter = colonist.CurJobDef?.defName;
			var stunnedAfter = colonist.stances?.stunner?.Stunned ?? false;
			var distanceSquared = colonist.Position.DistanceToSquared(albino.Position);

			return new
			{
				success = albino.scream >= pulseTick && jobAfter == JobDefOf.Vomit.defName && stunnedAfter,
				pulseTick,
				distanceSquared,
				albino = DescribeZombie(albino),
				colonist = DescribePawn(colonist),
				albinoCell = ZombieRuntimeActions.DescribeCell(albinoCell),
				jobBefore,
				jobAfter,
				stunnedBefore,
				stunnedAfter,
				screamAfter = albino.scream,
				samples
			};
		}

		[Tool("zombieland/hack_flickable_with_albino", Description = "Start a real albino sabotage job and verify its 240-tick hacking branch switches off a flickable building.")]
		public static object HackFlickableWithAlbino()
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var albinoCell, out var error) == false)
				return error;

			var albino = ZombieRuntimeActions.SpawnZombie(albinoCell, map, ZombieType.Albino, true);
			if (albino == null)
			{
				return new
				{
					success = false,
					error = "ZombieGenerator.SpawnZombie returned no albino test zombie."
				};
			}

			if (TryFindAdjacentBuildingCell(albino, out var buildingCell) == false)
			{
				return new
				{
					success = false,
					albino = DescribeZombie(albino),
					error = "No clear adjacent building cell was found for the albino hacking test."
				};
			}

			var lampDef = DefDatabase<ThingDef>.GetNamed("StandingLamp", false);
			if (lampDef == null)
			{
				return new
				{
					success = false,
					error = "ThingDef StandingLamp was not found."
				};
			}

			var lamp = GenSpawn.Spawn(ThingMaker.MakeThing(lampDef), buildingCell, map, WipeMode.Vanish) as Building;
			lamp?.SetFaction(Faction.OfPlayer);
			var flickable = lamp?.TryGetComp<CompFlickable>();
			if (lamp == null || flickable == null)
			{
				return new
				{
					success = false,
					albino = DescribeZombie(albino),
					buildingCell = ZombieRuntimeActions.DescribeCell(buildingCell),
					error = "The spawned StandingLamp did not provide a flickable building."
				};
			}

			flickable.SwitchIsOn = true;
			var switchBefore = flickable.SwitchIsOn;
			albino.jobs.StartJob(JobMaker.MakeJob(CustomDefs.Sabotage), JobCondition.InterruptForced, null, true, true);
			AdvanceGameTicks(1);

			var driver = albino.jobs.curDriver as JobDriver_Sabotage;
			if (driver == null)
			{
				return new
				{
					success = false,
					albino = DescribeZombie(albino),
					building = lamp.LabelCap,
					error = "Albino did not enter the sabotage job driver."
				};
			}

			albino.pather?.StopDead();
			driver.destination = IntVec3.Invalid;
			driver.door = null;
			driver.hackTarget = lamp;
			driver.waitCounter = 0;
			driver.hackCounter = 0;
			albino.scream = -1;

			var hackStartTick = 1;
			var hackActionTicks = 240;
			var totalTicks = hackStartTick + hackActionTicks;
			var samples = new List<object>();
			var currentDriver = driver;
			for (var tick = 1; tick <= totalTicks; tick++)
			{
				AdvanceGameTicks(1);
				currentDriver = albino.jobs.curDriver as JobDriver_Sabotage;
				if (currentDriver == null)
					break;
				if (tick == 1 || tick == totalTicks || tick % 60 == 0)
				{
					samples.Add(new
					{
						tick,
						currentDriver.hackCounter,
						switchIsOn = flickable.SwitchIsOn,
						hackTarget = currentDriver.hackTarget?.ThingID
					});
				}
			}

			var switchAfter = flickable.SwitchIsOn;

			return new
			{
				success = switchBefore && switchAfter == false && currentDriver?.hackCounter == 0 && currentDriver?.hackTarget == null,
				totalTicks,
				hackActionTicks,
				albino = DescribeZombie(albino),
				building = lamp.LabelCap,
				buildingCell = ZombieRuntimeActions.DescribeCell(buildingCell),
				switchBefore,
				switchAfter,
				driverStillCurrent = currentDriver != null,
				hackCounterAfter = currentDriver?.hackCounter,
				hackTargetAfter = currentDriver?.hackTarget?.ThingID,
				samples
			};
		}

		sealed class AlbinoSabotageCase
		{
			public string name;
			public bool success;
			public object details;
			public string error;
		}

		[Tool("zombieland/albino_sabotage_contract", Description = "Run a combined albino sabotage evidence suite for scream vomit job resume/cleanup, scream cooldown, target preference, opportunistic raiders, attack-mode scream filtering, false thing-target proximity, paralysis cancellation, door-resume, externally opened door resume, externally opened door hack-target re-path, stale target clearing, pure power target exclusion, flickable, breakdownable, and weapon hacks.")]
		public static object AlbinoSabotageContract(
			[ToolParameter(Description = "Return only case names, pass/fail, and errors to keep routine validation output compact.", Required = false, DefaultValue = false)] bool summaryOnly = false)
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			var cases = new List<AlbinoSabotageCase>();
			void AddCase(string name, Func<AlbinoSabotageCase> run)
			{
				try
				{
					cases.Add(run());
				}
				catch (Exception ex)
				{
					cases.Add(new AlbinoSabotageCase
					{
						name = name,
						success = false,
						error = $"{ex.GetType().Name}: {ex.Message}"
					});
				}
			}

			AddCase("target_preference", () => AlbinoTargetPreferenceCase(map));
			AddCase("opportunistic_raider", () => AlbinoOpportunisticRaiderCase(map));
			AddCase("scream_respects_attack_mode", () => AlbinoScreamRespectsAttackModeCase(map));
			AddCase("pressure_uses_reverse_hostility", () => AlbinoPressureUsesReverseHostilityCase(map));
			AddCase("raider_attacking_nearby_colonist", () => AlbinoRaiderAttackingNearbyColonistCase(map));
			AddCase("no_destination_pressure_backoff", () => AlbinoNoDestinationPressureBackoffCase(map));
			AddCase("scream_vomit_resumes_job", () => AlbinoScreamVomitResumesJobCase(map));
			AddCase("scream_vomit_failed_start_cleans_pending", () => AlbinoScreamVomitFailedStartCleansPendingCase(map));
			AddCase("scream_cooldown", () => AlbinoScreamCooldownCase(map));
			AddCase("scream_releases_movement_after_75_percent", () => AlbinoScreamReleasesMovementAfter75PercentCase(map));
			AddCase("paralysis_clears_queued_scream", () => AlbinoParalysisClearsQueuedScreamCase(map));
			AddCase("door_resume", () => AlbinoDoorResumeCase(map, false));
			AddCase("door_open_resume", () => AlbinoDoorResumeCase(map, true));
			AddCase("door_open_hack_target_resume", () => AlbinoDoorOpenHackTargetResumeCase(map));
			AddCase("door_route_intercepts_closed_door", () => AlbinoDoorRouteInterceptsClosedDoorCase(map));
			AddCase("stale_hack_target_clears", () => AlbinoStaleHackTargetClearsCase(map));
			AddCase("defensive_scream_switch", () => AlbinoDefensiveScreamSwitchCase(map));
			AddCase("power_trader_only_ignored", () => AlbinoPowerTraderOnlyIgnoredCase(map));
			AddCase("flickable_hack", () => AlbinoFlickableHackCase(map));
			AddCase("breakdownable_hack", () => AlbinoBreakdownableHackCase(map));
			AddCase("weapon_hack", () => AlbinoWeaponHackCase(map));

			var success = cases.All(item => item.success);
			if (summaryOnly)
			{
				return new
				{
					success,
					cases = cases.Select(item => new { item.name, item.success, item.error }).ToArray()
				};
			}

			return new
			{
				success,
				cases
			};
		}

		static AlbinoSabotageCase AlbinoCase(string name, bool success, object details = null, string error = null)
		{
			return new AlbinoSabotageCase
			{
				name = name,
				success = success,
				details = details,
				error = error
			};
		}

		static readonly FieldInfo pawnJobTrackerPawnField = AccessTools.Field(typeof(Pawn_JobTracker), "pawn");
		static Pawn albinoScreamVomitBlockedStartJobPawn;

		static bool BlockAlbinoScreamVomitStartJobPrefix(Pawn_JobTracker __instance, Job newJob)
		{
			if (newJob?.def != JobDefOf.Vomit)
				return true;

			var pawn = pawnJobTrackerPawnField?.GetValue(__instance) as Pawn;
			return ReferenceEquals(pawn, albinoScreamVomitBlockedStartJobPawn) == false;
		}

		static Pawn SpawnAlbinoTestColonist(Map map, IntVec3 cell, List<Thing> spawnedThings, bool drafted = false)
		{
			var pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(pawn, cell, map, Rot4.South);
			DisablePawnWork(pawn);
			if (pawn.drafter != null)
				pawn.drafter.Drafted = drafted;
			spawnedThings.Add(pawn);
			return pawn;
		}

		static Pawn SpawnAlbinoTestRaider(Map map, IntVec3 cell, List<Thing> spawnedThings)
		{
			var hostileFaction = Find.FactionManager.AllFactionsVisible
				.FirstOrDefault(faction => faction != null && faction.HostileTo(Faction.OfPlayer) && faction.def?.humanlikeFaction == true)
				?? Find.FactionManager.AllFactionsVisible.FirstOrDefault(faction => faction != null && faction.HostileTo(Faction.OfPlayer));
			if (hostileFaction == null)
				return null;

			var pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, hostileFaction);
			GenSpawn.Spawn(pawn, cell, map, Rot4.South);
			DisablePawnWork(pawn);
			spawnedThings.Add(pawn);
			map.attackTargetsCache.UpdateTarget(pawn);
			return pawn;
		}

		static PawnKindDef FindAlbinoTestMechKindDef()
		{
			return PawnKindDefOf.Mech_Scyther
				?? DefDatabase<PawnKindDef>.AllDefs.FirstOrDefault(def => def.race?.race?.IsMechanoid == true);
		}

		static Pawn SpawnAlbinoTestMech(Map map, IntVec3 cell, Faction faction, List<Thing> spawnedThings, string name)
		{
			var kindDef = FindAlbinoTestMechKindDef();
			if (kindDef == null)
				return null;

			var request = new PawnGenerationRequest(
				kindDef,
				faction,
				PawnGenerationContext.NonPlayer,
				forceGenerateNewPawn: true,
				canGeneratePawnRelations: false,
				colonistRelationChanceFactor: 0f,
				forceNoIdeo: true);
			var pawn = PawnGenerator.GeneratePawn(request);
			pawn.Name = new NameSingle(name);
			GenSpawn.Spawn(pawn, cell, map, Rot4.South);
			spawnedThings.Add(pawn);
			map.attackTargetsCache.UpdateTarget(pawn);
			return pawn;
		}

		static Zombie SpawnAlbinoTestZombie(Map map, IntVec3 cell, List<Thing> spawnedThings)
		{
			var albino = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.Albino, true);
			if (albino == null)
				return null;
			albino.SetFaction(Faction.OfPlayer);
			albino.albinoNextScreamTick = GenTicks.TicksGame;
			albino.health?.hediffSet?.hediffs?.RemoveAll(hediff => hediff is Hediff_Injury);
			spawnedThings.Add(albino);
			return albino;
		}

		static void DestroyAlbinoCaseThings(List<Thing> spawnedThings)
		{
			foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
				thing.Destroy(DestroyMode.Vanish);
		}

		static JobDriver_Sabotage StartAlbinoSabotageDriver(Zombie albino)
		{
			if (albino == null)
				return null;
			albino.jobs.StartJob(JobMaker.MakeJob(CustomDefs.Sabotage), JobCondition.InterruptForced, null, true, true);
			AdvanceGameTicks(1);
			albino.pather?.StopDead();
			return albino.jobs.curDriver as JobDriver_Sabotage;
		}

		static void ResetAlbinoPlannerState(JobDriver_Sabotage driver)
		{
			if (driver == null)
				return;

			driver.pawn?.pather?.StopDead();
			driver.destination = IntVec3.Invalid;
			driver.door = null;
			driver.doorExitCell = IntVec3.Invalid;
			driver.hackTarget = null;
			driver.hackApproachCell = IntVec3.Invalid;
			driver.queuedScreamCell = IntVec3.Invalid;
			driver.queuedMoveCell = IntVec3.Invalid;
			driver.waitCounter = 0;
			driver.hackCounter = 0;
			driver.nextDefensiveScreamCheckTick = 0;
			driver.lastDefensiveScreamCheckCell = IntVec3.Invalid;
			driver.nextDefensiveScreamCellCheckTick = 0;
			driver.defensiveScreamQueued = false;
			driver.noSafeHackRoute = false;
			driver.interruptibleDestination = false;
			driver.safetyDestination = false;
			driver.fallbackDestination = false;
			driver.nextStrategicRecheckTick = 0;
			driver.lastStrategicRecheckCell = IntVec3.Invalid;
			driver.lastFallbackStartCell = IntVec3.Invalid;
			driver.lastFallbackDestination = IntVec3.Invalid;
			driver.nextFallbackMoveTick = 0;
			driver.deferredHackTarget = null;
			driver.deferredHackTargetPauseUntilTick = 0;
			driver.rushHackTarget = null;
			driver.rushHackTargetUntilTick = 0;

			if (driver.pawn is Zombie albino)
			{
				albino.scream = -1;
				albino.albinoNextScreamTick = GenTicks.TicksGame;
				albino.albinoScreamAffectedCount = 0;
			}
		}

		static void FreezeAlbinoFixturePawn(Pawn pawn)
		{
			if (pawn == null)
				return;

			pawn.jobs?.StopAll(false, true);
			pawn.pather?.StopDead();
		}

		static void ForceAlbinoHackTarget(JobDriver_Sabotage driver, Thing target)
		{
			driver.pawn.pather?.StopDead();
			driver.destination = IntVec3.Invalid;
			driver.door = null;
			driver.doorExitCell = IntVec3.Invalid;
			driver.hackTarget = target;
			driver.hackApproachCell = IntVec3.Invalid;
			driver.queuedScreamCell = IntVec3.Invalid;
			driver.queuedMoveCell = IntVec3.Invalid;
			driver.waitCounter = 0;
			driver.hackCounter = 0;
			driver.nextDefensiveScreamCheckTick = 0;
			driver.lastDefensiveScreamCheckCell = IntVec3.Invalid;
			driver.nextDefensiveScreamCellCheckTick = 0;
			driver.defensiveScreamQueued = false;
			driver.noSafeHackRoute = false;
			driver.interruptibleDestination = false;
			driver.safetyDestination = false;
			driver.fallbackDestination = false;
			driver.nextStrategicRecheckTick = 0;
			driver.lastStrategicRecheckCell = IntVec3.Invalid;
			driver.lastFallbackStartCell = IntVec3.Invalid;
			driver.lastFallbackDestination = IntVec3.Invalid;
			driver.nextFallbackMoveTick = 0;
			driver.deferredHackTarget = null;
			driver.deferredHackTargetPauseUntilTick = 0;
			driver.rushHackTarget = null;
			driver.rushHackTargetUntilTick = 0;
			if (driver.pawn is Zombie albino)
			{
				albino.scream = -1;
				albino.albinoNextScreamTick = GenTicks.TicksGame;
				albino.albinoScreamAffectedCount = 0;
			}
		}

		static (JobDriver_Sabotage driver, bool driverStillCurrent, List<object> samples) RunForcedAlbinoHack(Zombie albino, Thing target)
		{
			var samples = new List<object>();
			if (albino == null || target == null)
				return (null, false, samples);

			var driver = StartAlbinoSabotageDriver(albino);
			if (driver == null)
				return (null, false, samples);

			ForceAlbinoHackTarget(driver, target);
			for (var tick = 1; tick <= 241; tick++)
			{
				if (TryInvokeAlbinoHackThing(driver, out _, out _) == false)
					return (driver, false, samples);
				if (tick == 1 || tick == 241 || tick % 60 == 0)
				{
					samples.Add(new
					{
						tick,
						driver.hackCounter,
						hackTarget = driver.hackTarget?.ThingID,
						albino = DescribeZombie(albino)
					});
				}
			}
			return (driver, ReferenceEquals(albino.jobs.curDriver, driver), samples);
		}

		static Type SabotageHandlerType => typeof(JobDriver_Sabotage).Assembly.GetType("ZombieLand.SabotageHandler");

		static bool TryInvokeAlbinoHackThing(JobDriver_Sabotage driver, out bool handled, out string error)
		{
			handled = false;
			error = null;
			var method = SabotageHandlerType?.GetMethod("HackThing", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			if (method == null)
			{
				error = "Could not find SabotageHandler.HackThing by reflection.";
				return false;
			}

			handled = (bool)method.Invoke(null, new object[] { driver });
			return true;
		}

		static bool TryInvokeAlbinoDefensiveScreamSwitch(JobDriver_Sabotage driver, out bool handled, out string error)
		{
			handled = false;
			error = null;
			var method = SabotageHandlerType?.GetMethod("TrySwitchContestedHackToScream", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			if (method == null)
			{
				error = "Could not find SabotageHandler.TrySwitchContestedHackToScream by reflection.";
				return false;
			}

			handled = (bool)method.Invoke(null, new object[] { driver });
			return true;
		}

		static bool TryInvokeAlbinoGotoCell(JobDriver_Sabotage driver, IntVec3 cell, out bool handled, out string error)
		{
			handled = false;
			error = null;
			var method = SabotageHandlerType?
				.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
				.FirstOrDefault(candidate =>
				{
					if (candidate.Name != "Goto")
						return false;
					var parameters = candidate.GetParameters();
					return parameters.Length == 3
						&& parameters[0].ParameterType == typeof(JobDriver_Sabotage)
						&& parameters[1].ParameterType == typeof(IntVec3);
				});
			if (method == null)
			{
				error = "Could not find SabotageHandler.Goto(JobDriver_Sabotage, IntVec3, Action) by reflection.";
				return false;
			}

			handled = (bool)method.Invoke(null, new object[] { driver, cell, null });
			return true;
		}

		static bool TryInvokeAlbinoGotoThing(JobDriver_Sabotage driver, Thing target, out bool handled, out string error)
		{
			handled = false;
			error = null;
			var method = SabotageHandlerType?
				.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
				.FirstOrDefault(candidate =>
				{
					if (candidate.Name != "Goto")
						return false;
					var parameters = candidate.GetParameters();
					return parameters.Length == 2
						&& parameters[0].ParameterType == typeof(JobDriver_Sabotage)
						&& parameters[1].ParameterType == typeof(Thing);
				});
			if (method == null)
			{
				error = "Could not find SabotageHandler.Goto(JobDriver_Sabotage, Thing) by reflection.";
				return false;
			}

			handled = (bool)method.Invoke(null, new object[] { driver, target });
			return true;
		}

		static bool TryInvokeAlbinoCanHackBuilding(Building building, out bool canHack, out string error)
		{
			canHack = false;
			error = null;
			var method = SabotageHandlerType?.GetMethod("CanHackBuilding", BindingFlags.Static | BindingFlags.NonPublic);
			if (method == null)
			{
				error = "Could not find SabotageHandler.CanHackBuilding by reflection.";
				return false;
			}

			canHack = (bool)method.Invoke(null, new object[] { building });
			return true;
		}

		static bool TryInvokeAlbinoScream(JobDriver_Sabotage driver, out bool handled, out string error)
		{
			handled = false;
			error = null;
			var method = SabotageHandlerType?.GetMethod("Scream", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			if (method == null)
			{
				error = "Could not find SabotageHandler.Scream by reflection.";
				return false;
			}

			handled = (bool)method.Invoke(null, new object[] { driver });
			return true;
		}

		static bool TryInvokeAlbinoWait(JobDriver_Sabotage driver, out bool handled, out string error)
		{
			handled = false;
			error = null;
			var method = SabotageHandlerType?.GetMethod("Wait", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			if (method == null)
			{
				error = "Could not find SabotageHandler.Wait by reflection.";
				return false;
			}

			handled = (bool)method.Invoke(null, new object[] { driver });
			return true;
		}

		static bool TryInvokeAlbinoImmediateMovementPressure(Zombie zombie, out int pressure, out string error)
		{
			pressure = 0;
			error = null;
			var sourcesMethod = SabotageHandlerType?.GetMethod("AlbinoPressureSourcesFor", BindingFlags.Static | BindingFlags.NonPublic);
			var pressureMethod = SabotageHandlerType?.GetMethod("ImmediateAlbinoMovementPressure", BindingFlags.Static | BindingFlags.NonPublic);
			if (sourcesMethod == null || pressureMethod == null)
			{
				error = "Could not find SabotageHandler pressure methods by reflection.";
				return false;
			}

			var sources = sourcesMethod.Invoke(null, new object[] { zombie });
			pressure = (int)pressureMethod.Invoke(null, new object[] { zombie, sources });
			return true;
		}

		static bool TryInvokeAlbinoScreamPlan(JobDriver_Sabotage driver, out IntVec3 cell, out string reason, out string error)
		{
			cell = IntVec3.Invalid;
			reason = null;
			error = null;
			var method = SabotageHandlerType?.GetMethod("TryFindAlbinoScreamCell", BindingFlags.Static | BindingFlags.NonPublic);
			if (method == null)
			{
				error = "Could not find SabotageHandler.TryFindAlbinoScreamCell by reflection.";
				return false;
			}

			var args = new object[] { driver, IntVec3.Invalid, null };
			var success = (bool)method.Invoke(null, args);
			cell = (IntVec3)args[1];
			reason = args[2] as string;
			return success;
		}

		static bool TryInvokeAlbinoIsAttackingOrApproaching(Pawn pawn, Zombie zombie, out bool result, out string error)
		{
			result = false;
			error = null;
			var method = SabotageHandlerType?.GetMethod("IsAttackingOrApproaching", BindingFlags.Static | BindingFlags.NonPublic);
			if (method == null)
			{
				error = "Could not find SabotageHandler.IsAttackingOrApproaching by reflection.";
				return false;
			}

			result = (bool)method.Invoke(null, new object[] { pawn, zombie });
			return true;
		}

		static bool TryInvokeAlbinoCanPressurePawn(Pawn pawn, Zombie zombie, out bool result, out string error)
		{
			result = false;
			error = null;
			var method = SabotageHandlerType?.GetMethod("CanAlbinoPressurePawn", BindingFlags.Static | BindingFlags.NonPublic);
			if (method == null)
			{
				error = "Could not find SabotageHandler.CanAlbinoPressurePawn by reflection.";
				return false;
			}

			result = (bool)method.Invoke(null, new object[] { pawn, zombie });
			return true;
		}

		static bool TryInvokeAlbinoCanScreamAffect(Pawn pawn, Zombie zombie, out bool result, out string error)
		{
			result = false;
			error = null;
			var method = SabotageHandlerType?.GetMethod("CanAlbinoScreamAffect", BindingFlags.Static | BindingFlags.NonPublic);
			if (method == null)
			{
				error = "Could not find SabotageHandler.CanAlbinoScreamAffect by reflection.";
				return false;
			}

			result = (bool)method.Invoke(null, new object[] { pawn, zombie });
			return true;
		}

		static bool TryFindAlbinoIsolatedFixtureRoot(Map map, out IntVec3 root, out object error)
		{
			root = IntVec3.Invalid;
			error = null;
			if (map == null)
			{
				error = new
				{
					success = false,
					error = "No current map is loaded."
				};
				return false;
			}

			var preferred = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			var searchRadius = Math.Min(Math.Max(map.Size.x, map.Size.z) / 2f, 75f);
			const int margin = 18;
			const int pawnClearRadiusSquared = 900;
			foreach (var candidate in GenRadial.RadialCellsAround(preferred, searchRadius, true))
			{
				if (candidate.InBounds(map) == false)
					continue;
				if (candidate.x < margin || candidate.z < margin || candidate.x >= map.Size.x - margin || candidate.z >= map.Size.z - margin)
					continue;
				if (candidate.Standable(map) == false || candidate.Fogged(map))
					continue;
				if (candidate.GetFirstThing<Mineable>(map) != null)
					continue;
				if (candidate.GetThingList(map).Any(thing => thing is Pawn))
					continue;
				if (map.mapPawns.AllPawnsSpawned.Any(pawn => pawn.Spawned && pawn.Position.DistanceToSquared(candidate) <= pawnClearRadiusSquared))
					continue;

				root = candidate;
				return true;
			}

			error = new
			{
				success = false,
				error = "No isolated albino fixture root was found away from existing pawns."
			};
			return false;
		}

		static AlbinoSabotageCase AlbinoTargetPreferenceCase(Map map)
		{
			var spawnedThings = new List<Thing>();
			var draftSnapshot = map.mapPawns.FreeColonistsSpawned
				.Where(pawn => pawn.drafter != null)
				.Select(pawn => new { pawn, pawn.drafter.Drafted })
				.ToList();
			try
			{
				_ = ZombieRuntimeActions.DestroyZombies(map);
				foreach (var snapshot in draftSnapshot)
					snapshot.pawn.drafter.Drafted = true;

				if (TryFindAlbinoIsolatedFixtureRoot(map, out var root, out var rootError) == false)
					return AlbinoCase("target_preference", false, error: rootError?.ToString());
				if (TryFindClearSpawnCell(map, root, 16f, out var albinoCell, out var albinoError) == false)
					return AlbinoCase("target_preference", false, error: albinoError?.ToString());
				if (TryFindClearSpawnCell(map, albinoCell + new IntVec3(8, 0, 0), 10f, out var isolatedCell, out var isolatedError) == false)
					return AlbinoCase("target_preference", false, error: isolatedError?.ToString());
				if (TryFindClearSpawnCell(map, albinoCell + new IntVec3(-8, 0, 0), 10f, out var draftedCellA, out var draftedErrorA) == false)
					return AlbinoCase("target_preference", false, error: draftedErrorA?.ToString());
				if (TryFindClearSpawnCell(map, draftedCellA + IntVec3.North, 6f, out var draftedCellB, out var draftedErrorB) == false)
					return AlbinoCase("target_preference", false, error: draftedErrorB?.ToString());

				var albino = SpawnAlbinoTestZombie(map, albinoCell, spawnedThings);
				var isolated = SpawnAlbinoTestColonist(map, isolatedCell, spawnedThings);
				var draftedA = SpawnAlbinoTestColonist(map, draftedCellA, spawnedThings, true);
				var draftedB = SpawnAlbinoTestColonist(map, draftedCellB, spawnedThings, true);
				var driver = StartAlbinoSabotageDriver(albino);
				if (driver == null)
					return AlbinoCase("target_preference", false, error: "Albino did not enter the sabotage driver.");

				ResetAlbinoPlannerState(driver);
				var reflected = TryInvokeAlbinoScreamPlan(driver, out var planCell, out var reason, out var error);
				var isolatedDistance = planCell.DistanceToSquared(isolated.Position);
				var draftedDistance = Math.Min(planCell.DistanceToSquared(draftedA.Position), planCell.DistanceToSquared(draftedB.Position));
				var success = reflected && reason == "isolatedColonist" && isolatedDistance < draftedDistance;
				return AlbinoCase("target_preference", success, new
				{
					reflected,
					reason,
					planCell = ZombieRuntimeActions.DescribeCell(planCell),
					isolatedDistance,
					draftedDistance,
					albino = DescribeZombie(albino),
					isolated = DescribePawn(isolated),
					draftedA = DescribePawn(draftedA),
					draftedB = DescribePawn(draftedB)
				}, error);
			}
			finally
			{
				foreach (var snapshot in draftSnapshot)
					if (snapshot.pawn?.drafter != null)
						snapshot.pawn.drafter.Drafted = snapshot.Drafted;
				DestroyAlbinoCaseThings(spawnedThings);
			}
		}

		static AlbinoSabotageCase AlbinoOpportunisticRaiderCase(Map map)
		{
			var spawnedThings = new List<Thing>();
			try
			{
				_ = ZombieRuntimeActions.DestroyZombies(map);
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var albinoCell, out var albinoError) == false)
					return AlbinoCase("opportunistic_raider", false, error: albinoError?.ToString());
				if (TryFindClearSpawnCell(map, albinoCell + new IntVec3(4, 0, 0), 8f, out var raiderCell, out var raiderError) == false)
					return AlbinoCase("opportunistic_raider", false, error: raiderError?.ToString());
				if (TryFindClearSpawnCell(map, albinoCell + new IntVec3(10, 0, 0), 10f, out var colonistCell, out var colonistError) == false)
					return AlbinoCase("opportunistic_raider", false, error: colonistError?.ToString());

				var albino = SpawnAlbinoTestZombie(map, albinoCell, spawnedThings);
				var raider = SpawnAlbinoTestRaider(map, raiderCell, spawnedThings);
				var colonist = SpawnAlbinoTestColonist(map, colonistCell, spawnedThings);
				if (albino == null || raider == null || colonist == null)
					return AlbinoCase("opportunistic_raider", false, error: "Could not create albino, raider, or colonist fixture pawn.");

				AdvanceGameTicks(1);
				map.attackTargetsCache.UpdateTarget(raider);
				var driver = StartAlbinoSabotageDriver(albino);
				if (driver == null)
					return AlbinoCase("opportunistic_raider", false, error: "Albino did not enter the sabotage driver.");

				ResetAlbinoPlannerState(driver);
				var reflected = TryInvokeAlbinoScreamPlan(driver, out var planCell, out var reason, out var error);
				var raiderDistance = planCell.DistanceToSquared(raider.Position);
				var colonistDistance = planCell.DistanceToSquared(colonist.Position);
				var success = reflected && reason == "opportunisticEnemy" && raiderDistance < colonistDistance;
				return AlbinoCase("opportunistic_raider", success, new
				{
					reflected,
					reason,
					planCell = ZombieRuntimeActions.DescribeCell(planCell),
					raiderDistance,
					colonistDistance,
					albino = DescribeZombie(albino),
					raider = DescribePawn(raider),
					colonist = DescribePawn(colonist)
				}, error);
			}
			finally
			{
				DestroyAlbinoCaseThings(spawnedThings);
			}
		}

		static AlbinoSabotageCase AlbinoScreamRespectsAttackModeCase(Map map)
		{
			const string caseName = "scream_respects_attack_mode";
			var spawnedThings = new List<Thing>();
			var previousAttackMode = ZombieSettings.Values.attackMode;
			try
			{
				_ = ZombieRuntimeActions.DestroyZombies(map);
				ApplyZombieSettingsOverride(values => values.attackMode = AttackMode.OnlyColonists);

				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var albinoCell, out var albinoError) == false)
					return AlbinoCase(caseName, false, error: albinoError?.ToString());

				var raiderCell = GenAdj.AdjacentCells
					.Select(offset => albinoCell + offset)
					.Where(cell => cell.InBounds(map))
					.Where(cell => cell.Standable(map))
					.Where(cell => cell.Fogged(map) == false)
					.Where(cell => cell.GetEdifice(map) == null)
					.Where(cell => cell.GetFirstThing<Mineable>(map) == null)
					.Where(cell => cell.GetThingList(map).Any(thing => thing is Pawn) == false)
					.FirstOrDefault();
				if (raiderCell.IsValid == false)
					return AlbinoCase(caseName, false, error: "No adjacent raider cell was available for the attack-mode scream fixture.");

				var albino = SpawnAlbinoTestZombie(map, albinoCell, spawnedThings);
				albino?.SetFaction(Tools.GetZombieFaction());
				var raider = SpawnAlbinoTestRaider(map, raiderCell, spawnedThings);
				if (albino == null || raider == null)
					return AlbinoCase(caseName, false, error: "Could not create albino or raider fixture pawn.");

				AdvanceGameTicks(1);
				map.attackTargetsCache.UpdateTarget(raider);
				var attractsUnderOnlyColonists = Customization.DoesAttractsZombies(raider);
				var driver = StartAlbinoSabotageDriver(albino);
				if (driver == null)
					return AlbinoCase(caseName, false, error: "Albino did not enter the sabotage driver.");

				ResetAlbinoPlannerState(driver);
				var reflectedPlan = TryInvokeAlbinoScreamPlan(driver, out var planCell, out var reason, out var planError);
				var planDistanceToRaider = planCell.IsValid ? planCell.DistanceToSquared(raider.Position) : int.MaxValue;
				var plannerTargetsRaider = reflectedPlan && reason == "opportunisticEnemy" && planDistanceToRaider <= 25;

				driver.destination = IntVec3.Invalid;
				driver.door = null;
				driver.hackTarget = null;
				driver.queuedScreamCell = IntVec3.Invalid;
				driver.waitCounter = 0;
				driver.hackCounter = 0;
				albino.scream = 39;
				albino.albinoScreamAffectedCount = 0;
				var raiderJobBefore = raider.CurJobDef?.defName;
				var raiderStunnedBefore = raider.stances?.stunner?.Stunned ?? false;
				var invokedPulse = TryInvokeAlbinoScream(driver, out var pulseHandled, out var pulseError);
				var raiderJobAfter = raider.CurJobDef?.defName;
				var raiderStunnedAfter = raider.stances?.stunner?.Stunned ?? false;

				var success = attractsUnderOnlyColonists == false
					&& plannerTargetsRaider == false
					&& invokedPulse
					&& pulseHandled
					&& raider.CurJobDef != JobDefOf.Vomit
					&& raiderStunnedAfter == false;

				return AlbinoCase(caseName, success, new
				{
					attackMode = ZombieSettings.Values.attackMode.ToString(),
					attractsUnderOnlyColonists,
					reflectedPlan,
					reason,
					plannerTargetsRaider,
					planCell = planCell.IsValid ? ZombieRuntimeActions.DescribeCell(planCell) : null,
					planDistanceToRaider,
					invokedPulse,
					pulseHandled,
					pulseError,
					albinoScreamAfterPulse = albino.scream,
					albino.albinoScreamAffectedCount,
					raiderJobBefore,
					raiderJobAfter,
					raiderStunnedBefore,
					raiderStunnedAfter,
					albino = DescribeZombie(albino),
					raider = DescribePawn(raider)
				}, planError ?? pulseError);
			}
			finally
			{
				ApplyZombieSettingsOverride(values => values.attackMode = previousAttackMode);
				DestroyAlbinoCaseThings(spawnedThings);
			}
		}

		static AlbinoSabotageCase AlbinoPressureUsesReverseHostilityCase(Map map)
		{
			const string caseName = "pressure_uses_reverse_hostility";
			var spawnedThings = new List<Thing>();
			var settingsSnapshot = SnapshotZombieSettings();
			try
			{
				_ = ZombieRuntimeActions.DestroyZombies(map);
				if (TryFindAlbinoIsolatedFixtureRoot(map, out var root, out var rootError) == false)
					return AlbinoCase(caseName, false, error: rootError?.ToString());
				if (TryFindClearSpawnCell(map, root, 16f, out var albinoCell, out var albinoError) == false)
					return AlbinoCase(caseName, false, error: albinoError?.ToString());
				if (TryFindClearSpawnCell(map, albinoCell + new IntVec3(4, 0, 0), 8f, out var raiderCell, out var raiderError) == false)
					return AlbinoCase(caseName, false, error: raiderError?.ToString());
				if (TryFindClearSpawnCell(map, albinoCell + new IntVec3(0, 0, 4), 8f, out var playerMechCell, out var playerMechError) == false)
					return AlbinoCase(caseName, false, error: playerMechError?.ToString());
				if (TryFindClearSpawnCell(map, albinoCell + new IntVec3(-4, 0, 0), 8f, out var hostileMechCell, out var hostileMechError) == false)
					return AlbinoCase(caseName, false, error: hostileMechError?.ToString());

				var mechKindDef = FindAlbinoTestMechKindDef();
				if (mechKindDef == null)
					return AlbinoCase(caseName, false, error: "No mech pawn kind was available for the reverse-hostility fixture.");

				var albino = SpawnAlbinoTestZombie(map, albinoCell, spawnedThings);
				albino?.SetFaction(Tools.GetZombieFaction());
				var raider = SpawnAlbinoTestRaider(map, raiderCell, spawnedThings);
				var playerMech = SpawnAlbinoTestMech(map, playerMechCell, Faction.OfPlayer, spawnedThings, "ZL Player Mech Pressure");
				var hostileMechFaction = Faction.OfMechanoids
					?? Find.FactionManager.AllFactionsVisible.FirstOrDefault(faction => faction != null && faction.HostileTo(Faction.OfPlayer));
				var hostileMech = SpawnAlbinoTestMech(map, hostileMechCell, hostileMechFaction, spawnedThings, "ZL Hostile Mech Pressure");
				if (albino == null || raider == null || playerMech == null || hostileMech == null)
					return AlbinoCase(caseName, false, error: "Could not create albino, raider, player mech, or hostile mech fixture pawns.");

				AdvanceGameTicks(1);
				map.attackTargetsCache.UpdateTarget(raider);
				map.attackTargetsCache.UpdateTarget(hostileMech);
				var driver = StartAlbinoSabotageDriver(albino);
				if (driver == null)
					return AlbinoCase(caseName, false, error: "Albino did not enter the sabotage driver.");
				FreezeAlbinoFixturePawn(raider);
				FreezeAlbinoFixturePawn(playerMech);
				FreezeAlbinoFixturePawn(hostileMech);

				ApplyZombieSettingsOverride(values =>
				{
					values.attackMode = AttackMode.Everything;
					values.enemiesAttackZombies = false;
					values.animalsAttackZombies = true;
				});

				var raiderAttractsReverseDisabled = Customization.DoesAttractsZombies(raider);
				var invokeRaiderPressureDisabled = TryInvokeAlbinoCanPressurePawn(raider, albino, out var raiderPressureReverseDisabled, out var raiderPressureDisabledError);
				var invokeRaiderScream = TryInvokeAlbinoCanScreamAffect(raider, albino, out var raiderScreamAffectable, out var raiderScreamError);
				ResetAlbinoPlannerState(driver);
				var reflectedPlanReverseDisabled = TryInvokeAlbinoScreamPlan(driver, out var planCellReverseDisabled, out var reasonReverseDisabled, out var planReverseDisabledError);
				var plannerTargetsRaiderReverseDisabled = reflectedPlanReverseDisabled
					&& reasonReverseDisabled == "opportunisticEnemy"
					&& planCellReverseDisabled.DistanceToSquared(raider.Position) <= 25;

				ApplyZombieSettingsOverride(values =>
				{
					values.attackMode = AttackMode.Everything;
					values.enemiesAttackZombies = true;
					values.animalsAttackZombies = true;
				});

				var invokeRaiderPressureEnabled = TryInvokeAlbinoCanPressurePawn(raider, albino, out var raiderPressureReverseEnabled, out var raiderPressureEnabledError);
				var invokePlayerMechPressureEverything = TryInvokeAlbinoCanPressurePawn(playerMech, albino, out var playerMechPressureEverything, out var playerMechPressureEverythingError);
				var invokeHostileMechPressureEverything = TryInvokeAlbinoCanPressurePawn(hostileMech, albino, out var hostileMechPressureEverything, out var hostileMechPressureEverythingError);
				var invokePlayerMechScream = TryInvokeAlbinoCanScreamAffect(playerMech, albino, out var playerMechScreamAffectable, out var playerMechScreamError);
				var invokeHostileMechScream = TryInvokeAlbinoCanScreamAffect(hostileMech, albino, out var hostileMechScreamAffectable, out var hostileMechScreamError);
				ResetAlbinoPlannerState(driver);
				var reflectedPlanReverseEnabled = TryInvokeAlbinoScreamPlan(driver, out var planCellReverseEnabled, out var reasonReverseEnabled, out var planReverseEnabledError);
				var plannerTargetsRaiderReverseEnabled = reflectedPlanReverseEnabled
					&& reasonReverseEnabled == "opportunisticEnemy"
					&& planCellReverseEnabled.DistanceToSquared(raider.Position) <= 25;

				ApplyZombieSettingsOverride(values =>
				{
					values.attackMode = AttackMode.OnlyHumans;
					values.enemiesAttackZombies = true;
					values.animalsAttackZombies = true;
				});

				var invokePlayerMechPressureOnlyHumans = TryInvokeAlbinoCanPressurePawn(playerMech, albino, out var playerMechPressureOnlyHumans, out var playerMechPressureOnlyHumansError);
				var invokeHostileMechPressureOnlyHumans = TryInvokeAlbinoCanPressurePawn(hostileMech, albino, out var hostileMechPressureOnlyHumans, out var hostileMechPressureOnlyHumansError);

				var success = raiderAttractsReverseDisabled
					&& invokeRaiderPressureDisabled
					&& raiderPressureReverseDisabled == false
					&& invokeRaiderScream
					&& raiderScreamAffectable
					&& plannerTargetsRaiderReverseDisabled == false
					&& invokeRaiderPressureEnabled
					&& raiderPressureReverseEnabled
					&& plannerTargetsRaiderReverseEnabled
					&& invokePlayerMechPressureEverything
					&& playerMechPressureEverything
					&& invokeHostileMechPressureEverything
					&& hostileMechPressureEverything
					&& invokePlayerMechScream
					&& playerMechScreamAffectable == false
					&& invokeHostileMechScream
					&& hostileMechScreamAffectable == false
					&& invokePlayerMechPressureOnlyHumans
					&& playerMechPressureOnlyHumans == false
					&& invokeHostileMechPressureOnlyHumans
					&& hostileMechPressureOnlyHumans == false;

				return AlbinoCase(caseName, success, new
				{
					mechKindDef = mechKindDef.defName,
					raiderAttractsReverseDisabled,
					invokeRaiderPressureDisabled,
					raiderPressureReverseDisabled,
					invokeRaiderScream,
					raiderScreamAffectable,
					reflectedPlanReverseDisabled,
					reasonReverseDisabled,
					plannerTargetsRaiderReverseDisabled,
					planCellReverseDisabled = planCellReverseDisabled.IsValid ? ZombieRuntimeActions.DescribeCell(planCellReverseDisabled) : null,
					invokeRaiderPressureEnabled,
					raiderPressureReverseEnabled,
					reflectedPlanReverseEnabled,
					reasonReverseEnabled,
					plannerTargetsRaiderReverseEnabled,
					planCellReverseEnabled = planCellReverseEnabled.IsValid ? ZombieRuntimeActions.DescribeCell(planCellReverseEnabled) : null,
					invokePlayerMechPressureEverything,
					playerMechPressureEverything,
					invokeHostileMechPressureEverything,
					hostileMechPressureEverything,
					invokePlayerMechScream,
					playerMechScreamAffectable,
					invokeHostileMechScream,
					hostileMechScreamAffectable,
					invokePlayerMechPressureOnlyHumans,
					playerMechPressureOnlyHumans,
					invokeHostileMechPressureOnlyHumans,
					hostileMechPressureOnlyHumans,
					albino = DescribeZombie(albino),
					raider = DescribePawn(raider),
					playerMech = DescribePawn(playerMech),
					hostileMech = DescribePawn(hostileMech)
				}, raiderPressureDisabledError
					?? raiderScreamError
					?? planReverseDisabledError
					?? raiderPressureEnabledError
					?? playerMechPressureEverythingError
					?? hostileMechPressureEverythingError
					?? playerMechScreamError
					?? hostileMechScreamError
					?? planReverseEnabledError
					?? playerMechPressureOnlyHumansError
					?? hostileMechPressureOnlyHumansError);
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
				DestroyAlbinoCaseThings(spawnedThings);
			}
		}

		static AlbinoSabotageCase AlbinoRaiderAttackingNearbyColonistCase(Map map)
		{
			var spawnedThings = new List<Thing>();
			try
			{
				_ = ZombieRuntimeActions.DestroyZombies(map);
				if (TryFindAlbinoIsolatedFixtureRoot(map, out var root, out var rootError) == false)
					return AlbinoCase("raider_attacking_nearby_colonist", false, error: rootError?.ToString());
				if (TryFindClearSpawnCell(map, root, 16f, out var albinoCell, out var albinoError) == false)
					return AlbinoCase("raider_attacking_nearby_colonist", false, error: albinoError?.ToString());

				var albino = SpawnAlbinoTestZombie(map, albinoCell, spawnedThings);
				if (albino == null)
					return AlbinoCase("raider_attacking_nearby_colonist", false, error: "Could not create an albino fixture pawn.");

				if (TryFindAdjacentClearCell(albino, out var colonistCell) == false)
					return AlbinoCase("raider_attacking_nearby_colonist", false, error: "No adjacent colonist cell was available for the thing-target proximity fixture.");

				var colonist = SpawnAlbinoTestColonist(map, colonistCell, spawnedThings);
				FreezeAlbinoFixturePawn(colonist);
				var raiderCell = GenRadial.RadialCellsAround(albinoCell, 28f, false)
					.Where(cell => cell.InBounds(map))
					.Where(cell => cell.Standable(map))
					.Where(cell => cell.Fogged(map) == false)
					.Where(cell => cell.GetThingList(map).Any(thing => thing is Pawn) == false)
					.Where(cell => cell.DistanceToSquared(albinoCell) > 100)
					.Where(cell => GenSight.LineOfSight(cell, colonistCell, map, true))
					.OrderBy(cell => cell.DistanceToSquared(colonistCell))
					.FirstOrDefault();
				if (raiderCell.IsValid == false)
					return AlbinoCase("raider_attacking_nearby_colonist", false, error: "No far line-of-sight raider cell was available for the thing-target proximity fixture.");

				var raider = SpawnAlbinoTestRaider(map, raiderCell, spawnedThings);
				var verb = EquipAreaWorkflowRangedWeapon(raider);
				if (raider == null || verb == null)
					return AlbinoCase("raider_attacking_nearby_colonist", false, error: "Could not create an armed hostile raider for the thing-target proximity fixture.");
				FreezeAlbinoFixturePawn(raider);

				var attackJob = JobMaker.MakeJob(JobDefOf.AttackStatic, colonist);
				attackJob.canUseRangedWeapon = true;
				raider.jobs.StartJob(attackJob, JobCondition.InterruptForced, null, true, true);
				AdvanceGameTicks(1);
				map.attackTargetsCache.UpdateTarget(raider);

				var driver = StartAlbinoSabotageDriver(albino);
				if (driver == null)
					return AlbinoCase("raider_attacking_nearby_colonist", false, error: "Albino did not enter the sabotage driver.");

				ResetAlbinoPlannerState(driver);
				var reflectedAttack = TryInvokeAlbinoIsAttackingOrApproaching(raider, albino, out var attackingOrApproaching, out var attackError);
				var reflectedPlan = TryInvokeAlbinoScreamPlan(driver, out var planCell, out var reason, out var planError);
				var raiderDistanceToAlbino = raider.Position.DistanceToSquared(albino.Position);
				var targetDistanceToAlbino = colonist.Position.DistanceToSquared(albino.Position);
				var planDistanceToColonist = planCell.DistanceToSquared(colonist.Position);
				var planDistanceToRaider = planCell.DistanceToSquared(raider.Position);
				var plannerAvoidedRaider = reflectedPlan
					&& reason != "opportunisticEnemy"
					&& planDistanceToRaider > 25;
				var success = reflectedAttack
					&& attackingOrApproaching == false
					&& plannerAvoidedRaider;
				return AlbinoCase("raider_attacking_nearby_colonist", success, new
				{
					reflectedAttack,
					attackingOrApproaching,
					reflectedPlan,
					reason,
					plannerAvoidedRaider,
					planCell = ZombieRuntimeActions.DescribeCell(planCell),
					targetDistanceToAlbino,
					raiderDistanceToAlbino,
					planDistanceToColonist,
					planDistanceToRaider,
					albino = DescribeZombie(albino),
					colonist = DescribePawn(colonist),
					raider = DescribePawn(raider),
					raiderJob = raider.CurJobDef?.defName,
					raiderTarget = ZombieRuntimeActions.StableThingId(raider.CurJob?.targetA.Thing),
					verb = DescribeVerb(verb)
				}, attackError ?? planError);
			}
			finally
			{
				DestroyAlbinoCaseThings(spawnedThings);
			}
		}

		static AlbinoSabotageCase AlbinoNoDestinationPressureBackoffCase(Map map)
		{
			const string caseName = "no_destination_pressure_backoff";
			var spawnedThings = new List<Thing>();
			try
			{
					_ = ZombieRuntimeActions.DestroyZombies(map);
					if (TryFindAlbinoIsolatedFixtureRoot(map, out var root, out var rootError) == false)
						return AlbinoCase(caseName, false, error: rootError?.ToString());
					if (TryFindClearSpawnCell(map, root, 16f, out var albinoCell, out var albinoError) == false)
						return AlbinoCase(caseName, false, error: albinoError?.ToString());

					var shooterCell = GenRadial.RadialCellsAround(albinoCell, 14f, false)
						.Where(cell => cell.InBounds(map))
						.Where(cell => cell.Standable(map))
						.Where(cell => cell.Fogged(map) == false)
						.Where(cell => cell.GetThingList(map).Any(thing => thing is Pawn) == false)
						.Where(cell => cell.DistanceToSquared(albinoCell) >= 16)
						.Where(cell => GenSight.LineOfSight(cell, albinoCell, map, true))
						.OrderBy(cell => cell.DistanceToSquared(albinoCell))
						.FirstOrDefault();
					if (shooterCell.IsValid == false)
						return AlbinoCase(caseName, false, error: "No line-of-sight shooter cell was available for the pressure backoff fixture.");

					var albino = SpawnAlbinoTestZombie(map, albinoCell, spawnedThings);
					var shooter = SpawnAlbinoTestColonist(map, shooterCell, spawnedThings, true);
					var verb = EquipAreaWorkflowRangedWeapon(shooter);
					if (albino == null || shooter == null || verb == null)
						return AlbinoCase(caseName, false, error: "Could not create an albino and armed drafted colonist fixture.");

					AdvanceGameTicks(1);
					map.attackTargetsCache.UpdateTarget(shooter);
					var driver = StartAlbinoSabotageDriver(albino);
					if (driver == null)
						return AlbinoCase(caseName, false, error: "Albino did not enter the sabotage driver.");

					ResetAlbinoPlannerState(driver);
					driver.destination = IntVec3.Invalid;
					driver.door = null;
					driver.hackTarget = null;
					driver.waitCounter = 60;
					albino.scream = -1;
					albino.pather?.StopDead();

					var canPressureInvoked = TryInvokeAlbinoCanPressurePawn(shooter, albino, out var canPressure, out var canPressureError);
					var pressureInvoked = TryInvokeAlbinoImmediateMovementPressure(albino, out var pressure, out var pressureError);
					var waitBefore = driver.waitCounter;
					var firstWaitInvoked = TryInvokeAlbinoWait(driver, out var firstWaitHandled, out var firstWaitError);
					var waitAfterFirst = driver.waitCounter;
					var secondWaitInvoked = TryInvokeAlbinoWait(driver, out var secondWaitHandled, out var secondWaitError);
					var waitAfterSecond = driver.waitCounter;
					var shortenedButStillWaiting = firstWaitHandled
						&& waitAfterFirst > 0
						&& waitAfterFirst <= 20
						&& waitAfterFirst < waitBefore - 1;
					var countdownContinues = secondWaitHandled
						&& waitAfterSecond == waitAfterFirst - 1;
					var success = canPressureInvoked
						&& canPressure
						&& pressureInvoked
						&& pressure >= 4
						&& firstWaitInvoked
						&& secondWaitInvoked
						&& shortenedButStillWaiting
						&& countdownContinues
						&& driver.destination.IsValid == false;

					return AlbinoCase(caseName, success, new
					{
						canPressureInvoked,
						canPressure,
						pressureInvoked,
						pressure,
						waitBefore,
						firstWaitInvoked,
						firstWaitHandled,
						waitAfterFirst,
						secondWaitInvoked,
						secondWaitHandled,
						waitAfterSecond,
						shortenedButStillWaiting,
						countdownContinues,
						destinationValid = driver.destination.IsValid,
						albino = DescribeZombie(albino),
						shooter = DescribePawn(shooter),
						verb = DescribeVerb(verb)
					}, canPressureError ?? pressureError ?? firstWaitError ?? secondWaitError);
			}
			finally
			{
				DestroyAlbinoCaseThings(spawnedThings);
			}
		}

		static AlbinoSabotageCase AlbinoScreamVomitResumesJobCase(Map map)
		{
			const string caseName = "scream_vomit_resumes_job";
			var spawnedThings = new List<Thing>();
			try
			{
				_ = ZombieRuntimeActions.DestroyZombies(map);
				if (TryFindAlbinoIsolatedFixtureRoot(map, out var root, out var rootError) == false)
					return AlbinoCase(caseName, false, error: rootError?.ToString());
				if (TryFindClearSpawnCell(map, root, 16f, out var victimCell, out var victimError) == false)
					return AlbinoCase(caseName, false, error: victimError?.ToString());
				if (TryFindClearSpawnCell(map, victimCell + new IntVec3(4, 0, 0), 8f, out var destinationCell, out var destinationError) == false)
					return AlbinoCase(caseName, false, error: destinationError?.ToString());

				var victim = SpawnAlbinoTestColonist(map, victimCell, spawnedThings);
				if (victim == null)
					return AlbinoCase(caseName, false, error: "Could not create a colonist victim fixture.");

				victim.jobs.StopAll(false, true);
				victim.jobs.ClearQueuedJobs();
				victim.pather?.StopDead();

				var interruptedJob = JobMaker.MakeJob(JobDefOf.Goto, destinationCell);
				interruptedJob.expiryInterval = 3000;
				var interruptedJobSuspendable = interruptedJob.def?.suspendable == true;
				if (interruptedJobSuspendable == false)
				{
					return AlbinoCase(caseName, false, new
					{
						interruptedJob = interruptedJob.def?.defName,
						destinationCell = ZombieRuntimeActions.DescribeCell(destinationCell),
						interruptedJobSuspendable
					}, "The fixture job is not suspendable, so it cannot prove resume behavior.");
				}

				victim.jobs.StartJob(interruptedJob, JobCondition.InterruptForced, null, false, true);
				var currentBeforeIsInterruptedJob = ReferenceEquals(victim.jobs.curJob, interruptedJob);
				var queueCountBefore = victim.jobs.jobQueue.Count;
				var jobBefore = victim.CurJobDef?.defName;

				AlbinoScreamVomit.Start(victim);

				var jobAfterStart = victim.CurJobDef?.defName;
				var queueCountAfterStart = victim.jobs.jobQueue.Count;
				var queuedAfterStart = queueCountAfterStart > 0 ? victim.jobs.jobQueue.Peek()?.job : null;
				var queuedAfterStartIsInterruptedJob = ReferenceEquals(queuedAfterStart, interruptedJob);

				AdvanceGameTicks(1);

				var jobAfterTick = victim.CurJobDef?.defName;
				var queueCountAfterTick = victim.jobs.jobQueue.Count;
				var queuedAfterTick = queueCountAfterTick > 0 ? victim.jobs.jobQueue.Peek()?.job : null;
				var queuedAfterTickIsInterruptedJob = ReferenceEquals(queuedAfterTick, interruptedJob);
				var vomitDriver = victim.jobs.curDriver as JobDriver_Vomit;
				var success = currentBeforeIsInterruptedJob
					&& queueCountBefore == 0
					&& jobAfterStart == JobDefOf.Vomit.defName
					&& queuedAfterStartIsInterruptedJob
					&& queueCountAfterStart == 1
					&& jobAfterTick == JobDefOf.Vomit.defName
					&& queuedAfterTickIsInterruptedJob
					&& queueCountAfterTick == 1
					&& vomitDriver?.ticksLeft > 0;

				return AlbinoCase(caseName, success, new
				{
					victim = DescribePawn(victim),
					victimCell = ZombieRuntimeActions.DescribeCell(victimCell),
					destinationCell = ZombieRuntimeActions.DescribeCell(destinationCell),
					interruptedJob = interruptedJob.def?.defName,
					interruptedJobSuspendable,
					currentBeforeIsInterruptedJob,
					jobBefore,
					jobAfterStart,
					jobAfterTick,
					queueCountBefore,
					queueCountAfterStart,
					queueCountAfterTick,
					queuedAfterStart = queuedAfterStart?.def?.defName,
					queuedAfterTick = queuedAfterTick?.def?.defName,
					queuedAfterStartIsInterruptedJob,
					queuedAfterTickIsInterruptedJob,
					vomitTicksLeft = vomitDriver?.ticksLeft
				});
			}
			finally
			{
				DestroyAlbinoCaseThings(spawnedThings);
			}
		}

		static AlbinoSabotageCase AlbinoScreamVomitFailedStartCleansPendingCase(Map map)
		{
			const string caseName = "scream_vomit_failed_start_cleans_pending";
			var spawnedThings = new List<Thing>();
			Harmony harmony = null;
			MethodInfo startJobMethod = null;
			try
			{
				_ = ZombieRuntimeActions.DestroyZombies(map);
				if (TryFindAlbinoIsolatedFixtureRoot(map, out var root, out var rootError) == false)
					return AlbinoCase(caseName, false, error: rootError?.ToString());
				if (TryFindClearSpawnCell(map, root, 16f, out var victimCell, out var victimError) == false)
					return AlbinoCase(caseName, false, error: victimError?.ToString());

				startJobMethod = AccessTools.Method(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob));
				var prefixMethod = AccessTools.Method(typeof(ZombielandBridgeTools), nameof(BlockAlbinoScreamVomitStartJobPrefix));
				if (startJobMethod == null || prefixMethod == null || pawnJobTrackerPawnField == null)
				{
					return AlbinoCase(caseName, false, new
					{
						hasStartJobMethod = startJobMethod != null,
						hasPrefixMethod = prefixMethod != null,
						hasPawnField = pawnJobTrackerPawnField != null
					}, "Could not install the temporary StartJob blocker.");
				}

				var victim = SpawnAlbinoTestColonist(map, victimCell, spawnedThings);
				if (victim == null)
					return AlbinoCase(caseName, false, error: "Could not create a colonist victim fixture.");

				victim.jobs.StopAll(false, true);
				victim.jobs.ClearQueuedJobs();
				victim.pather?.StopDead();
				AlbinoScreamVomit.ClearPendingMultipliers();
				var pendingBefore = AlbinoScreamVomit.PendingMultiplierCount;

				harmony = new Harmony("net.pardeike.zombieland.bridge.albino_scream_vomit_failed_start");
				harmony.Unpatch(startJobMethod, HarmonyPatchType.Prefix, harmony.Id);
				albinoScreamVomitBlockedStartJobPawn = victim;
				harmony.Patch(startJobMethod, prefix: new HarmonyMethod(prefixMethod) { priority = Priority.First });

				AlbinoScreamVomit.Start(victim);

				var pendingAfterFailedStart = AlbinoScreamVomit.PendingMultiplierCount;
				var vomitStarted = victim.jobs.curDriver is JobDriver_Vomit;
				var jobAfterFailedStart = victim.CurJobDef?.defName;
				var success = pendingBefore == 0
					&& pendingAfterFailedStart == 0
					&& vomitStarted == false;

				return AlbinoCase(caseName, success, new
				{
					victim = DescribePawn(victim),
					victimCell = ZombieRuntimeActions.DescribeCell(victimCell),
					pendingBefore,
					pendingAfterFailedStart,
					vomitStarted,
					jobAfterFailedStart,
					patchInstalled = true
				});
			}
			finally
			{
				albinoScreamVomitBlockedStartJobPawn = null;
				if (harmony != null && startJobMethod != null)
					harmony.Unpatch(startJobMethod, HarmonyPatchType.Prefix, harmony.Id);
				AlbinoScreamVomit.ClearPendingMultipliers();
				DestroyAlbinoCaseThings(spawnedThings);
			}
		}

		static AlbinoSabotageCase AlbinoScreamCooldownCase(Map map)
		{
			var spawnedThings = new List<Thing>();
			try
			{
				_ = ZombieRuntimeActions.DestroyZombies(map);
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var colonistCell, out var colonistError) == false)
					return AlbinoCase("scream_cooldown", false, error: colonistError?.ToString());
				if (TryFindClearSpawnCell(map, colonistCell + IntVec3.East, 6f, out var albinoCell, out var albinoError) == false)
					return AlbinoCase("scream_cooldown", false, error: albinoError?.ToString());

				var colonist = SpawnAlbinoTestColonist(map, colonistCell, spawnedThings);
				var albino = SpawnAlbinoTestZombie(map, albinoCell, spawnedThings);
				var driver = StartAlbinoSabotageDriver(albino);
				if (driver == null)
					return AlbinoCase("scream_cooldown", false, error: "Albino did not enter the sabotage driver.");

				driver.destination = IntVec3.Invalid;
				driver.door = null;
				driver.hackTarget = null;
				driver.queuedScreamCell = IntVec3.Invalid;
				driver.waitCounter = 0;
				driver.hackCounter = 0;
				albino.scream = 0;
				albino.albinoNextScreamTick = GenTicks.TicksGame;
				albino.albinoScreamAffectedCount = 0;

				var samples = new List<object>();
				for (var tick = 1; tick <= 40; tick++)
				{
					if (TryInvokeAlbinoScream(driver, out _, out var screamError) == false)
						return AlbinoCase("scream_cooldown", false, error: screamError);
					if (tick == 1 || tick == 40)
					{
						samples.Add(new
						{
							tick,
							albino.scream,
							albino.albinoScreamAffectedCount,
							albino.albinoNextScreamTick,
							colonistJob = colonist.CurJobDef?.defName,
							colonistStunned = colonist.stances?.stunner?.Stunned ?? false
						});
					}
				}

				driver = albino.jobs.curDriver as JobDriver_Sabotage;
				if (driver != null)
				{
					driver.waitCounter = 0;
					driver.destination = IntVec3.Invalid;
					driver.door = null;
					driver.hackTarget = null;
					driver.queuedScreamCell = IntVec3.Invalid;
					albino.scream = 399;
					if (TryInvokeAlbinoScream(driver, out _, out var screamError) == false)
						return AlbinoCase("scream_cooldown", false, error: screamError);
					samples.Add(new
					{
						tick = 400,
						albino.scream,
						albino.albinoScreamAffectedCount,
						albino.albinoNextScreamTick,
						colonistJob = colonist.CurJobDef?.defName,
						colonistStunned = colonist.stances?.stunner?.Stunned ?? false
					});
				}

				var success = colonist.CurJobDef == JobDefOf.Vomit
					&& (colonist.stances?.stunner?.Stunned ?? false)
					&& albino.scream == -1
					&& albino.albinoScreamAffectedCount > 0
					&& albino.albinoNextScreamTick > GenTicks.TicksGame;
				return AlbinoCase("scream_cooldown", success, new
				{
					albino = DescribeZombie(albino),
					colonist = DescribePawn(colonist),
					cooldownRemaining = albino.albinoNextScreamTick - GenTicks.TicksGame,
					samples
				});
			}
			finally
			{
				DestroyAlbinoCaseThings(spawnedThings);
			}
		}

		static AlbinoSabotageCase AlbinoScreamReleasesMovementAfter75PercentCase(Map map)
		{
			const string caseName = "scream_releases_movement_after_75_percent";
			var spawnedThings = new List<Thing>();
			try
			{
				_ = ZombieRuntimeActions.DestroyZombies(map);
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var albinoCell, out var albinoError) == false)
					return AlbinoCase(caseName, false, error: albinoError?.ToString());

				var albino = SpawnAlbinoTestZombie(map, albinoCell, spawnedThings);
				if (albino == null)
					return AlbinoCase(caseName, false, error: "Could not create an albino fixture pawn.");
				if (TryFindAdjacentClearCell(albino, out var colonistCell) == false)
					return AlbinoCase(caseName, false, error: "No adjacent colonist cell was available.");

				var colonist = SpawnAlbinoTestColonist(map, colonistCell, spawnedThings);
				var driver = StartAlbinoSabotageDriver(albino);
				if (driver == null)
					return AlbinoCase(caseName, false, error: "Albino did not enter the sabotage driver.");

				driver.waitCounter = 0;
				driver.destination = IntVec3.Invalid;
				driver.door = null;
				driver.hackTarget = null;
				driver.queuedScreamCell = IntVec3.Invalid;
				albino.albinoNextScreamTick = GenTicks.TicksGame;
				albino.albinoScreamAffectedCount = 0;

				albino.scream = 39;
				var invokedBeforeRelease = TryInvokeAlbinoScream(driver, out var blocksBeforeRelease, out var beforeReleaseError);
				var screamAfterBeforeRelease = albino.scream;
				var affectedAfterBeforeRelease = albino.albinoScreamAffectedCount;

				albino.scream = 299;
				var nextScreamBeforeRelease = albino.albinoNextScreamTick;
				var invokedAtRelease = TryInvokeAlbinoScream(driver, out var blocksAtRelease, out var releaseError);
				var screamAfterRelease = albino.scream;
				var nextScreamAfterRelease = albino.albinoNextScreamTick;

				albino.scream = 399;
				var invokedCompletion = TryInvokeAlbinoScream(driver, out var blocksAtCompletion, out var completionError);
				var success = invokedBeforeRelease
					&& blocksBeforeRelease
					&& screamAfterBeforeRelease == 40
					&& affectedAfterBeforeRelease > 0
					&& invokedAtRelease
					&& blocksAtRelease == false
					&& screamAfterRelease == 300
					&& nextScreamAfterRelease == nextScreamBeforeRelease
					&& invokedCompletion
					&& blocksAtCompletion == false
					&& albino.scream == -1
					&& albino.albinoNextScreamTick > GenTicks.TicksGame;

				return AlbinoCase(caseName, success, new
				{
					invokedBeforeRelease,
					blocksBeforeRelease,
					beforeReleaseError,
					screamAfterBeforeRelease,
					affectedAfterBeforeRelease,
					invokedAtRelease,
					blocksAtRelease,
					releaseError,
					screamAfterRelease,
					nextScreamBeforeRelease,
					nextScreamAfterRelease,
					invokedCompletion,
					blocksAtCompletion,
					completionError,
					screamAfterCompletion = albino.scream,
					cooldownRemaining = albino.albinoNextScreamTick - GenTicks.TicksGame,
					albino = DescribeZombie(albino),
					colonist = DescribePawn(colonist)
				}, beforeReleaseError ?? releaseError ?? completionError);
			}
			finally
			{
				DestroyAlbinoCaseThings(spawnedThings);
			}
		}

		static AlbinoSabotageCase AlbinoParalysisClearsQueuedScreamCase(Map map)
		{
			var spawnedThings = new List<Thing>();
			try
			{
				_ = ZombieRuntimeActions.DestroyZombies(map);
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var albinoCell, out var albinoError) == false)
					return AlbinoCase("paralysis_clears_queued_scream", false, error: albinoError?.ToString());

				var doorCell = IntVec3.Invalid;
				foreach (var offset in GenAdj.CardinalDirections)
				{
					var candidate = albinoCell + offset;
					if (candidate.InBounds(map)
						&& candidate.Fogged(map) == false
						&& candidate.GetEdifice(map) == null
						&& candidate.GetThingList(map).Any(thing => thing is Pawn) == false)
					{
						doorCell = candidate;
						break;
					}
				}
				if (doorCell.IsValid == false)
					return AlbinoCase("paralysis_clears_queued_scream", false, error: "No adjacent clear door cell was available.");
				if (TryFindClearSpawnCell(map, doorCell + IntVec3.East, 8f, out var screamCell, out var screamError) == false)
					return AlbinoCase("paralysis_clears_queued_scream", false, error: screamError?.ToString());

				var door = ThingMaker.MakeThing(ThingDefOf.Door, GenStuff.DefaultStuffFor(ThingDefOf.Door)) as Building_Door;
				if (door == null)
					return AlbinoCase("paralysis_clears_queued_scream", false, error: "Could not create a test door.");
				GenSpawn.Spawn(door, doorCell, map, WipeMode.Vanish);
				door.SetFaction(Faction.OfPlayer);
				spawnedThings.Add(door);

				var colonist = SpawnAlbinoTestColonist(map, screamCell, spawnedThings);
				var albino = SpawnAlbinoTestZombie(map, albinoCell, spawnedThings);
				var driver = StartAlbinoSabotageDriver(albino);
				if (driver == null)
					return AlbinoCase("paralysis_clears_queued_scream", false, error: "Albino did not enter the sabotage driver.");

				driver.destination = doorCell;
				driver.door = door;
				driver.doorExitCell = screamCell;
				driver.hackTarget = door;
				driver.hackApproachCell = doorCell;
				driver.queuedScreamCell = screamCell;
				driver.queuedMoveCell = albinoCell;
				driver.waitCounter = 17;
				driver.hackCounter = 73;
				driver.defensiveScreamQueued = true;
				driver.noSafeHackRoute = true;
				driver.interruptibleDestination = true;
				driver.safetyDestination = true;
				driver.fallbackDestination = true;
				driver.nextStrategicRecheckTick = GenTicks.TicksGame + 111;
				driver.lastStrategicRecheckCell = doorCell;
				driver.lastFallbackStartCell = albinoCell;
				driver.lastFallbackDestination = screamCell;
				driver.nextFallbackMoveTick = GenTicks.TicksGame + 222;
				driver.deferredHackTarget = door;
				driver.deferredHackTargetPauseUntilTick = GenTicks.TicksGame + 333;
				driver.rushHackTarget = door;
				driver.rushHackTargetUntilTick = GenTicks.TicksGame + 444;
				albino.scream = -2;

				var queuedBefore = driver.queuedScreamCell.IsValid;
				var extendedStateSeeded = driver.doorExitCell.IsValid
					&& driver.hackTarget == door
					&& driver.hackApproachCell.IsValid
					&& driver.queuedMoveCell.IsValid
					&& driver.defensiveScreamQueued
					&& driver.noSafeHackRoute
					&& driver.interruptibleDestination
					&& driver.safetyDestination
					&& driver.fallbackDestination
					&& driver.nextStrategicRecheckTick > GenTicks.TicksGame
					&& driver.lastStrategicRecheckCell.IsValid
					&& driver.lastFallbackStartCell.IsValid
					&& driver.lastFallbackDestination.IsValid
					&& driver.nextFallbackMoveTick > GenTicks.TicksGame
					&& driver.deferredHackTarget == door
					&& driver.deferredHackTargetPauseUntilTick > GenTicks.TicksGame
					&& driver.rushHackTarget == door
					&& driver.rushHackTargetUntilTick > GenTicks.TicksGame;
				var paralyzed = albino.TryParalyze(600, out var paralysisError);
				var currentDriver = albino.jobs.curDriver as JobDriver_Sabotage;
				var queuedAfterParalysis = currentDriver?.queuedScreamCell.IsValid == true;
				var extendedStateCleared = currentDriver != null
					&& currentDriver.doorExitCell.IsValid == false
					&& currentDriver.hackApproachCell.IsValid == false
					&& currentDriver.queuedMoveCell.IsValid == false
					&& currentDriver.defensiveScreamQueued == false
					&& currentDriver.noSafeHackRoute == false
					&& currentDriver.interruptibleDestination == false
					&& currentDriver.safetyDestination == false
					&& currentDriver.fallbackDestination == false
					&& currentDriver.nextStrategicRecheckTick == 0
					&& currentDriver.lastStrategicRecheckCell.IsValid == false
					&& currentDriver.lastFallbackStartCell.IsValid == false
					&& currentDriver.lastFallbackDestination.IsValid == false
					&& currentDriver.nextFallbackMoveTick == 0
					&& currentDriver.deferredHackTarget == null
					&& currentDriver.deferredHackTargetPauseUntilTick == 0
					&& currentDriver.rushHackTarget == null
					&& currentDriver.rushHackTargetUntilTick == 0;
				var handledAfterClear = false;
				string hackError = null;
				var invokeAfterClear = currentDriver != null && TryInvokeAlbinoHackThing(currentDriver, out handledAfterClear, out hackError);
				var destinationAfterInvoke = currentDriver?.destination.IsValid == true;
				var success = queuedBefore
					&& extendedStateSeeded
					&& paralyzed
					&& currentDriver != null
					&& queuedAfterParalysis == false
					&& extendedStateCleared
					&& currentDriver.destination.IsValid == false
					&& currentDriver.door == null
					&& currentDriver.hackTarget == null
					&& currentDriver.hackCounter == 0
					&& albino.scream == -1
					&& invokeAfterClear
					&& handledAfterClear == false
					&& destinationAfterInvoke == false
					&& albino.scream == -1;

				return AlbinoCase("paralysis_clears_queued_scream", success, new
				{
					queuedBefore,
					extendedStateSeeded,
					paralyzed,
					paralysisError,
					queuedAfterParalysis,
					extendedStateCleared,
					invokeAfterClear,
					handledAfterClear,
					hackError,
					destinationAfterInvoke,
					albino = DescribeZombie(albino),
					colonist = DescribePawn(colonist),
					doorCell = ZombieRuntimeActions.DescribeCell(doorCell),
					screamCell = ZombieRuntimeActions.DescribeCell(screamCell),
					driverPresent = currentDriver != null,
					destination = currentDriver?.destination.IsValid == true ? ZombieRuntimeActions.DescribeCell(currentDriver.destination) : null,
					queuedScreamCell = currentDriver?.queuedScreamCell.IsValid == true ? ZombieRuntimeActions.DescribeCell(currentDriver.queuedScreamCell) : null,
					queuedMoveCell = currentDriver?.queuedMoveCell.IsValid == true ? ZombieRuntimeActions.DescribeCell(currentDriver.queuedMoveCell) : null,
					doorExitCell = currentDriver?.doorExitCell.IsValid == true ? ZombieRuntimeActions.DescribeCell(currentDriver.doorExitCell) : null,
					hackApproachCell = currentDriver?.hackApproachCell.IsValid == true ? ZombieRuntimeActions.DescribeCell(currentDriver.hackApproachCell) : null,
					door = currentDriver?.door == null ? null : ZombieRuntimeActions.StableThingId(currentDriver.door),
					hackTarget = currentDriver?.hackTarget == null ? null : ZombieRuntimeActions.StableThingId(currentDriver.hackTarget),
					hackCounter = currentDriver?.hackCounter,
					defensiveScreamQueued = currentDriver?.defensiveScreamQueued,
					noSafeHackRoute = currentDriver?.noSafeHackRoute,
					interruptibleDestination = currentDriver?.interruptibleDestination,
					safetyDestination = currentDriver?.safetyDestination,
					fallbackDestination = currentDriver?.fallbackDestination,
					nextStrategicRecheckTick = currentDriver?.nextStrategicRecheckTick,
					lastStrategicRecheckCell = currentDriver?.lastStrategicRecheckCell.IsValid == true ? ZombieRuntimeActions.DescribeCell(currentDriver.lastStrategicRecheckCell) : null,
					lastFallbackStartCell = currentDriver?.lastFallbackStartCell.IsValid == true ? ZombieRuntimeActions.DescribeCell(currentDriver.lastFallbackStartCell) : null,
					lastFallbackDestination = currentDriver?.lastFallbackDestination.IsValid == true ? ZombieRuntimeActions.DescribeCell(currentDriver.lastFallbackDestination) : null,
					nextFallbackMoveTick = currentDriver?.nextFallbackMoveTick,
					deferredHackTarget = currentDriver?.deferredHackTarget == null ? null : ZombieRuntimeActions.StableThingId(currentDriver.deferredHackTarget),
					deferredHackTargetPauseUntilTick = currentDriver?.deferredHackTargetPauseUntilTick,
					rushHackTarget = currentDriver?.rushHackTarget == null ? null : ZombieRuntimeActions.StableThingId(currentDriver.rushHackTarget),
					rushHackTargetUntilTick = currentDriver?.rushHackTargetUntilTick,
					albino.scream
				}, paralysisError ?? hackError);
			}
			finally
			{
				DestroyAlbinoCaseThings(spawnedThings);
			}
		}

		static AlbinoSabotageCase AlbinoDoorResumeCase(Map map, bool doorAlreadyPassable)
		{
			var caseName = doorAlreadyPassable ? "door_open_resume" : "door_resume";
			var spawnedThings = new List<Thing>();
			try
			{
				_ = ZombieRuntimeActions.DestroyZombies(map);
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var albinoCell, out var albinoError) == false)
					return AlbinoCase(caseName, false, error: albinoError?.ToString());
				var doorCell = IntVec3.Invalid;
				foreach (var offset in GenAdj.CardinalDirections)
				{
					var candidate = albinoCell + offset;
					if (candidate.InBounds(map)
						&& candidate.Fogged(map) == false
						&& candidate.GetEdifice(map) == null
						&& candidate.GetThingList(map).Any(thing => thing is Pawn) == false)
					{
						doorCell = candidate;
						break;
					}
				}
				if (doorCell.IsValid == false)
					return AlbinoCase(caseName, false, error: "No adjacent clear door cell was available.");
				if (TryFindClearSpawnCell(map, doorCell + IntVec3.East, 8f, out var screamCell, out var screamError) == false)
					return AlbinoCase(caseName, false, error: screamError?.ToString());

				var door = ThingMaker.MakeThing(ThingDefOf.Door, GenStuff.DefaultStuffFor(ThingDefOf.Door)) as Building_Door;
				if (door == null)
					return AlbinoCase(caseName, false, error: "Could not create a test door.");
				GenSpawn.Spawn(door, doorCell, map, WipeMode.Vanish);
				door.SetFaction(Faction.OfPlayer);
				spawnedThings.Add(door);

				var colonist = SpawnAlbinoTestColonist(map, screamCell, spawnedThings);
				var albino = SpawnAlbinoTestZombie(map, albinoCell, spawnedThings);
				albino?.SetFaction(Tools.GetZombieFaction());
				var driver = StartAlbinoSabotageDriver(albino);
				if (driver == null)
					return AlbinoCase(caseName, false, error: "Albino did not enter the sabotage driver.");

				driver.destination = IntVec3.Invalid;
				driver.door = door;
				driver.hackTarget = null;
				driver.queuedScreamCell = screamCell;
				driver.waitCounter = 0;
				driver.hackCounter = doorAlreadyPassable ? 123 : 0;
				albino.scream = -1;
				var hackCounterBeforeResume = driver.hackCounter;

				if (doorAlreadyPassable)
					door.StartManualOpenBy(colonist);

				var hackTicks = doorAlreadyPassable ? 1 : 241;
				if (doorAlreadyPassable)
					AdvanceGameTicks(1);
				else
				{
					for (var tick = 1; tick <= hackTicks; tick++)
						if (TryInvokeAlbinoHackThing(driver, out _, out var hackError) == false)
							return AlbinoCase(caseName, false, error: hackError);
				}

				var currentDriver = albino.jobs.curDriver as JobDriver_Sabotage;
				var success = door.Open
					&& currentDriver != null
					&& currentDriver.waitCounter == 0
					&& (doorAlreadyPassable == false || currentDriver.hackCounter == 0)
					&& (albino.scream == -2 || currentDriver.destination.IsValid || currentDriver.queuedScreamCell.IsValid == false);
				return AlbinoCase(caseName, success, new
				{
					doorAlreadyPassable,
					hackTicks,
					hackCounterBeforeResume,
					doorOpen = door.Open,
					doorCanPhysicallyPass = door.CanPhysicallyPass(albino),
					albino = DescribeZombie(albino),
					colonist = DescribePawn(colonist),
					doorCell = ZombieRuntimeActions.DescribeCell(doorCell),
					screamCell = ZombieRuntimeActions.DescribeCell(screamCell),
					destination = currentDriver?.destination.IsValid == true ? ZombieRuntimeActions.DescribeCell(currentDriver.destination) : null,
					queuedScreamCell = currentDriver?.queuedScreamCell.IsValid == true ? ZombieRuntimeActions.DescribeCell(currentDriver.queuedScreamCell) : null,
					hackCounterAfterResume = currentDriver?.hackCounter,
					waitCounterAfterResume = currentDriver?.waitCounter,
					albino.scream
				});
			}
			finally
			{
				DestroyAlbinoCaseThings(spawnedThings);
			}
		}

		static AlbinoSabotageCase AlbinoDoorOpenHackTargetResumeCase(Map map)
		{
			const string caseName = "door_open_hack_target_resume";
			var spawnedThings = new List<Thing>();

			bool IsClearFixtureCell(IntVec3 cell)
			{
				return cell.InBounds(map)
					&& cell.Standable(map)
					&& cell.Fogged(map) == false
					&& cell.GetEdifice(map) == null
					&& cell.GetFirstThing<Mineable>(map) == null
					&& cell.GetThingList(map).Any(thing => thing is Pawn) == false;
			}

			try
			{
				_ = ZombieRuntimeActions.DestroyZombies(map);
				if (TryFindAlbinoIsolatedFixtureRoot(map, out var root, out var rootError) == false)
					return AlbinoCase(caseName, false, error: rootError?.ToString());
				if (TryFindClearSpawnCell(map, root, 16f, out var albinoCell, out var albinoError) == false)
					return AlbinoCase(caseName, false, error: albinoError?.ToString());

				var doorCell = IntVec3.Invalid;
				var behindDoorCell = IntVec3.Invalid;
				var lampCell = IntVec3.Invalid;
				foreach (var offset in GenAdj.CardinalDirections)
				{
					var candidateDoor = albinoCell + offset;
					var candidateBehindDoor = candidateDoor + offset;
					var candidateLamp = candidateBehindDoor + offset;
					if (IsClearFixtureCell(candidateDoor) && IsClearFixtureCell(candidateBehindDoor) && IsClearFixtureCell(candidateLamp))
					{
						doorCell = candidateDoor;
						behindDoorCell = candidateBehindDoor;
						lampCell = candidateLamp;
						break;
					}
				}
				if (doorCell.IsValid == false || behindDoorCell.IsValid == false || lampCell.IsValid == false)
					return AlbinoCase(caseName, false, error: "No adjacent door, behind-door path, and lamp cells were available.");

				var colonistCell = GenRadial.RadialCellsAround(doorCell, 4f, false)
					.Where(cell => cell != albinoCell && cell != doorCell && cell != behindDoorCell && cell != lampCell)
					.Where(IsClearFixtureCell)
					.OrderBy(cell => cell.DistanceToSquared(doorCell))
					.FirstOrDefault();
				if (colonistCell.IsValid == false)
					return AlbinoCase(caseName, false, error: "No clear colonist cell was available to open the queued door externally.");

				var door = ThingMaker.MakeThing(ThingDefOf.Door, GenStuff.DefaultStuffFor(ThingDefOf.Door)) as Building_Door;
				if (door == null)
					return AlbinoCase(caseName, false, error: "Could not create a test door.");
				GenSpawn.Spawn(door, doorCell, map, WipeMode.Vanish);
				door.SetFaction(Faction.OfPlayer);
				spawnedThings.Add(door);

				var lampDef = DefDatabase<ThingDef>.GetNamed("StandingLamp", false);
				var lamp = lampDef == null ? null : GenSpawn.Spawn(ThingMaker.MakeThing(lampDef), lampCell, map, WipeMode.Vanish) as Building;
				lamp?.SetFaction(Faction.OfPlayer);
				if (lamp == null)
					return AlbinoCase(caseName, false, error: "Could not create StandingLamp.");
				spawnedThings.Add(lamp);
				var flickable = lamp.TryGetComp<CompFlickable>();
				if (flickable == null)
					return AlbinoCase(caseName, false, error: "StandingLamp has no CompFlickable.");
				flickable.SwitchIsOn = true;

				var colonist = SpawnAlbinoTestColonist(map, colonistCell, spawnedThings);
				var albino = SpawnAlbinoTestZombie(map, albinoCell, spawnedThings);
				albino?.SetFaction(Tools.GetZombieFaction());
				var driver = StartAlbinoSabotageDriver(albino);
				if (driver == null)
					return AlbinoCase(caseName, false, error: "Albino did not enter the sabotage driver.");

				door.StartManualOpenBy(colonist);
				AdvanceGameTicks(1);

				driver = albino.jobs.curDriver as JobDriver_Sabotage;
				if (driver == null)
					return AlbinoCase(caseName, false, error: "Albino left the sabotage driver after the external door open.");

				driver.destination = IntVec3.Invalid;
				driver.door = door;
				driver.hackTarget = lamp;
				driver.queuedScreamCell = IntVec3.Invalid;
				driver.waitCounter = 0;
				driver.hackCounter = 123;
				albino.scream = -1;
				var hackCounterBeforeResume = driver.hackCounter;

				var invoked = TryInvokeAlbinoHackThing(driver, out var handled, out var hackError);
				var currentDriver = albino.jobs.curDriver as JobDriver_Sabotage;
				var destination = currentDriver?.destination ?? IntVec3.Invalid;
				var destinationValid = destination.IsValid;
				var success = invoked
					&& handled
					&& door.Open
					&& currentDriver != null
					&& currentDriver.door == null
					&& currentDriver.hackTarget == lamp
					&& currentDriver.hackCounter == 0
					&& currentDriver.waitCounter == 0
					&& destinationValid
					&& flickable.SwitchIsOn;

				return AlbinoCase(caseName, success, new
				{
					invoked,
					handled,
					hackError,
					hackCounterBeforeResume,
					hackCounterAfterResume = currentDriver?.hackCounter,
					waitCounterAfterResume = currentDriver?.waitCounter,
					doorOpen = door.Open,
					doorCanPhysicallyPass = door.CanPhysicallyPass(albino),
					lampSwitchIsOn = flickable.SwitchIsOn,
					albino = DescribeZombie(albino),
					colonist = DescribePawn(colonist),
					doorCell = ZombieRuntimeActions.DescribeCell(doorCell),
					behindDoorCell = ZombieRuntimeActions.DescribeCell(behindDoorCell),
					lampCell = ZombieRuntimeActions.DescribeCell(lampCell),
					colonistCell = ZombieRuntimeActions.DescribeCell(colonistCell),
					destination = destinationValid ? ZombieRuntimeActions.DescribeCell(destination) : null,
					patherMoving = albino.pather?.Moving,
					hackTarget = currentDriver?.hackTarget == null ? null : ZombieRuntimeActions.StableThingId(currentDriver.hackTarget)
				}, hackError);
			}
			finally
			{
				DestroyAlbinoCaseThings(spawnedThings);
			}
		}

		static AlbinoSabotageCase AlbinoDoorRouteInterceptsClosedDoorCase(Map map)
		{
			const string caseName = "door_route_intercepts_closed_door";
			var spawnedThings = new List<Thing>();

			bool IsClearFixtureCell(IntVec3 cell)
			{
				return cell.InBounds(map)
					&& cell.Standable(map)
					&& cell.Fogged(map) == false
					&& cell.GetEdifice(map) == null
					&& cell.GetFirstThing<Mineable>(map) == null
					&& cell.GetThingList(map).Any(thing => thing is Pawn) == false;
			}

			try
			{
				_ = ZombieRuntimeActions.DestroyZombies(map);
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				var albinoCell = IntVec3.Invalid;
				var direction = IntVec3.Invalid;
				var doorCell = IntVec3.Invalid;
				var targetTouchCell = IntVec3.Invalid;
				var lampCell = IntVec3.Invalid;
				List<IntVec3> wallCells = null;
				var barrierHalfWidth = 0;
				foreach (var candidateAlbinoCell in GenRadial.RadialCellsAround(root, 70f, true)
					.Where(cell => IsClearFixtureCell(cell))
					.OrderBy(cell => cell.DistanceToSquared(root)))
				{
					foreach (var offset in GenAdj.CardinalDirections)
					{
						var perpendicular = new IntVec3(-offset.z, 0, offset.x);
						var candidateDoor = candidateAlbinoCell + offset;
						var candidateTargetTouch = candidateDoor + offset;
						var candidateLamp = candidateTargetTouch + offset;
						foreach (var halfWidth in new[] { 10, 8, 6 })
						{
							var candidateWalls = new List<IntVec3>();
							for (var side = -halfWidth; side <= halfWidth; side++)
							{
								if (side != 0)
									candidateWalls.Add(candidateDoor + perpendicular * side);
							}

							if (IsClearFixtureCell(candidateDoor)
								&& IsClearFixtureCell(candidateTargetTouch)
								&& IsClearFixtureCell(candidateLamp)
								&& candidateWalls.All(IsClearFixtureCell))
							{
								albinoCell = candidateAlbinoCell;
								direction = offset;
								doorCell = candidateDoor;
								targetTouchCell = candidateTargetTouch;
								lampCell = candidateLamp;
								wallCells = candidateWalls;
								barrierHalfWidth = halfWidth;
								break;
							}
						}
						if (wallCells != null)
							break;
					}
					if (wallCells != null)
						break;
				}
				if (albinoCell.IsValid == false || direction.IsValid == false || doorCell.IsValid == false || targetTouchCell.IsValid == false || lampCell.IsValid == false || wallCells == null)
					return AlbinoCase(caseName, false, error: "No clear door-route fixture cells were available.");

				foreach (var wallCell in wallCells)
				{
					var wall = ThingMaker.MakeThing(ThingDefOf.Wall, GenStuff.DefaultStuffFor(ThingDefOf.Wall)) as Building;
					if (wall == null)
						return AlbinoCase(caseName, false, error: "Could not create a test wall.");
					GenSpawn.Spawn(wall, wallCell, map, WipeMode.Vanish);
					wall.SetFaction(Faction.OfPlayer);
					spawnedThings.Add(wall);
				}

				var door = ThingMaker.MakeThing(ThingDefOf.Door, GenStuff.DefaultStuffFor(ThingDefOf.Door)) as Building_Door;
				if (door == null)
					return AlbinoCase(caseName, false, error: "Could not create a test door.");
				GenSpawn.Spawn(door, doorCell, map, WipeMode.Vanish);
				door.SetFaction(Faction.OfPlayer);
				spawnedThings.Add(door);

				var lampDef = DefDatabase<ThingDef>.GetNamed("StandingLamp", false);
				var lamp = lampDef == null ? null : GenSpawn.Spawn(ThingMaker.MakeThing(lampDef), lampCell, map, WipeMode.Vanish) as Building;
				lamp?.SetFaction(Faction.OfPlayer);
				if (lamp == null)
					return AlbinoCase(caseName, false, error: "Could not create StandingLamp.");
				spawnedThings.Add(lamp);
				var flickable = lamp.TryGetComp<CompFlickable>();
				if (flickable == null)
					return AlbinoCase(caseName, false, error: "StandingLamp has no CompFlickable.");
				flickable.SwitchIsOn = true;

				var albino = SpawnAlbinoTestZombie(map, albinoCell, spawnedThings);
				albino?.SetFaction(Tools.GetZombieFaction());
				var driver = StartAlbinoSabotageDriver(albino);
				if (driver == null)
					return AlbinoCase(caseName, false, error: "Albino did not enter the sabotage driver.");

				var invokedGoto = TryInvokeAlbinoGotoThing(driver, lamp, out var gotoHandled, out var gotoError);
				var destinationBeforeHack = driver.destination;
				var patherMovingBeforeHack = albino.pather?.Moving == true;
				var selectedDoorBeforeHack = driver.door == door;
				var targetRetainedBeforeHack = driver.hackTarget == lamp;
				var stoppedForAdjacentDoor = destinationBeforeHack.IsValid == false || destinationBeforeHack == albino.Position;

				var hackTicks = 0;
				var invokedHack = false;
				var hackHandled = false;
				string hackError = null;
				for (var tick = 1; tick <= 241 && door.Open == false; tick++)
				{
					invokedHack = TryInvokeAlbinoHackThing(driver, out hackHandled, out hackError);
					if (invokedHack == false)
						return AlbinoCase(caseName, false, error: hackError);
					hackTicks = tick;
				}

				var currentDriver = albino.jobs.curDriver as JobDriver_Sabotage;
				var success = invokedGoto
					&& gotoHandled
					&& selectedDoorBeforeHack
					&& targetRetainedBeforeHack
					&& stoppedForAdjacentDoor
					&& patherMovingBeforeHack == false
					&& invokedHack
					&& hackHandled
					&& door.Open
					&& currentDriver != null
					&& currentDriver.door == null
					&& currentDriver.hackTarget == lamp
					&& currentDriver.hackCounter == 0
					&& currentDriver.waitCounter == 0
					&& flickable.SwitchIsOn;

				return AlbinoCase(caseName, success, new
				{
					invokedGoto,
					gotoHandled,
					gotoError,
					selectedDoorBeforeHack,
					targetRetainedBeforeHack,
					stoppedForAdjacentDoor,
					patherMovingBeforeHack,
					invokedHack,
					hackHandled,
					hackError,
					hackTicks,
					doorOpen = door.Open,
					doorCanPhysicallyPass = door.CanPhysicallyPass(albino),
					lampSwitchIsOn = flickable.SwitchIsOn,
					hackCounterAfterDoorHack = currentDriver?.hackCounter,
					waitCounterAfterDoorHack = currentDriver?.waitCounter,
					direction = ZombieRuntimeActions.DescribeCell(direction),
					albino = DescribeZombie(albino),
					doorCell = ZombieRuntimeActions.DescribeCell(doorCell),
					targetTouchCell = ZombieRuntimeActions.DescribeCell(targetTouchCell),
					lampCell = ZombieRuntimeActions.DescribeCell(lampCell),
					barrierHalfWidth,
					wallCells = wallCells.Select(ZombieRuntimeActions.DescribeCell).ToArray(),
					destinationBeforeHack = destinationBeforeHack.IsValid ? ZombieRuntimeActions.DescribeCell(destinationBeforeHack) : null,
					destinationAfterDoorHack = currentDriver?.destination.IsValid == true ? ZombieRuntimeActions.DescribeCell(currentDriver.destination) : null,
					hackTargetAfterDoorHack = currentDriver?.hackTarget == null ? null : ZombieRuntimeActions.StableThingId(currentDriver.hackTarget)
				}, gotoError ?? hackError);
			}
			finally
			{
				DestroyAlbinoCaseThings(spawnedThings);
			}
		}

		static AlbinoSabotageCase AlbinoStaleHackTargetClearsCase(Map map)
		{
			const string caseName = "stale_hack_target_clears";
			var spawnedThings = new List<Thing>();
			try
			{
				_ = ZombieRuntimeActions.DestroyZombies(map);
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var albinoCell, out var albinoError) == false)
					return AlbinoCase(caseName, false, error: albinoError?.ToString());

				var albino = SpawnAlbinoTestZombie(map, albinoCell, spawnedThings);
				if (albino == null)
					return AlbinoCase(caseName, false, error: "Could not create an albino fixture pawn.");
				if (TryFindAdjacentClearCell(albino, out var weaponCell) == false)
					return AlbinoCase(caseName, false, error: "No adjacent weapon cell was available for the stale-target fixture.");

				var weaponDef = DefDatabase<ThingDef>.GetNamed("Gun_BoltActionRifle", false)
					?? DefDatabase<ThingDef>.GetNamed("Gun_Pistol", false);
				var weapon = weaponDef == null ? null : ThingMaker.MakeThing(weaponDef);
				if (weapon == null)
					return AlbinoCase(caseName, false, error: "No ranged weapon def was available.");
				GenSpawn.Spawn(weapon, weaponCell, map, WipeMode.Vanish);
				spawnedThings.Add(weapon);

				var driver = StartAlbinoSabotageDriver(albino);
				if (driver == null)
					return AlbinoCase(caseName, false, error: "Albino did not enter the sabotage driver.");

				ForceAlbinoHackTarget(driver, weapon);
				var samples = new List<object>();
				for (var tick = 1; tick <= 61; tick++)
				{
					if (TryInvokeAlbinoHackThing(driver, out var handled, out var hackError) == false)
						return AlbinoCase(caseName, false, error: hackError);
					if (tick == 1 || tick == 61)
					{
						samples.Add(new
						{
							tick,
							handled,
							driver.hackCounter,
							hackTarget = driver.hackTarget?.ThingID,
							hitPoints = weapon.HitPoints
						});
					}
				}

				var counterBeforeMove = driver.hackCounter;
				var hitPointsBeforeMove = weapon.HitPoints;
				if (TryFindClearSpawnCell(map, albino.Position + new IntVec3(20, 0, 0), 30f, out var movedWeaponCell, out var movedWeaponError) == false)
					return AlbinoCase(caseName, false, error: movedWeaponError?.ToString());

				weapon.DeSpawn(DestroyMode.Vanish);
				GenSpawn.Spawn(weapon, movedWeaponCell, map, WipeMode.Vanish);
				var invokedAfterMove = TryInvokeAlbinoHackThing(driver, out var handledAfterMove, out var hackAfterMoveError);
				var destinationAfterMove = driver.destination;
				var movedTargetRetainedForRepath = driver.hackTarget == weapon && driver.hackCounter == 0 && destinationAfterMove.IsValid;
				var movedTargetAbandoned = driver.hackTarget == null && driver.hackCounter == 0;
				var remoteDamagePrevented = weapon.HitPoints == hitPointsBeforeMove;

				if (TryFindClearSpawnCell(map, albino.Position + new IntVec3(3, 0, 0), 8f, out var cellTarget, out var cellTargetError) == false)
					return AlbinoCase(caseName, false, error: cellTargetError?.ToString());

				driver.pawn.pather?.StopDead();
				driver.destination = IntVec3.Invalid;
				driver.door = null;
				driver.hackTarget = weapon;
				driver.queuedScreamCell = IntVec3.Invalid;
				driver.waitCounter = 0;
				driver.hackCounter = 77;
				((Zombie)driver.pawn).scream = -1;
				var invokedGotoCell = TryInvokeAlbinoGotoCell(driver, cellTarget, out var gotoCellHandled, out var gotoCellError);
				var cellTargetClearedHack = driver.hackTarget == null && driver.hackCounter == 0;

				var success = counterBeforeMove > 0
					&& invokedAfterMove
					&& handledAfterMove
					&& remoteDamagePrevented
					&& (movedTargetRetainedForRepath || movedTargetAbandoned)
					&& invokedGotoCell
					&& cellTargetClearedHack;

				return AlbinoCase(caseName, success, new
				{
					counterBeforeMove,
					hitPointsBeforeMove,
					hitPointsAfterMove = weapon.HitPoints,
					remoteDamagePrevented,
					invokedAfterMove,
					handledAfterMove,
					hackAfterMoveError,
					movedTargetRetainedForRepath,
					movedTargetAbandoned,
					destinationAfterMove = destinationAfterMove.IsValid ? ZombieRuntimeActions.DescribeCell(destinationAfterMove) : null,
					hackCounterAfterMove = driver.hackCounter,
					hackTargetAfterMove = driver.hackTarget == null ? null : ZombieRuntimeActions.StableThingId(driver.hackTarget),
					invokedGotoCell,
					gotoCellHandled,
					gotoCellError,
					cellTargetClearedHack,
					cellTarget = ZombieRuntimeActions.DescribeCell(cellTarget),
					movedWeaponCell = ZombieRuntimeActions.DescribeCell(movedWeaponCell),
					albino = DescribeZombie(albino),
					weapon = ZombieRuntimeActions.StableThingId(weapon),
					samples
				}, hackAfterMoveError ?? gotoCellError);
			}
			finally
			{
				DestroyAlbinoCaseThings(spawnedThings);
			}
		}

		static IntVec3 FindAlbinoLineOfSightCell(Map map, IntVec3 root, IntVec3 target, float radius)
		{
			return GenRadial.RadialCellsAround(root, radius, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetThingList(map).Any(thing => thing is Pawn) == false)
				.Where(cell => GenSight.LineOfSight(cell, target, map, true))
				.OrderBy(cell => cell.DistanceToSquared(root))
				.FirstOrDefault();
		}

		static Thing SpawnAlbinoHackWeapon(Map map, IntVec3 root, List<Thing> spawnedThings, out string error)
		{
			error = null;
			if (TryFindClearSpawnCell(map, root, 12f, out var weaponCell, out var cellError) == false)
			{
				error = cellError?.ToString();
				return null;
			}

			var weaponDef = DefDatabase<ThingDef>.GetNamed("Gun_BoltActionRifle", false)
				?? DefDatabase<ThingDef>.GetNamed("Gun_Pistol", false);
			var weapon = weaponDef == null ? null : ThingMaker.MakeThing(weaponDef);
			if (weapon == null)
			{
				error = "No ranged weapon def was available.";
				return null;
			}

			GenSpawn.Spawn(weapon, weaponCell, map, WipeMode.Vanish);
			spawnedThings.Add(weapon);
			return weapon;
		}

		static AlbinoSabotageCase AlbinoDefensiveScreamSwitchCase(Map map)
		{
			const string caseName = "defensive_scream_switch";
			var spawnedThings = new List<Thing>();
			try
			{
				_ = ZombieRuntimeActions.DestroyZombies(map);
				if (TryFindAlbinoIsolatedFixtureRoot(map, out var root, out var rootError) == false)
					return AlbinoCase(caseName, false, error: rootError?.ToString());
				if (TryFindClearSpawnCell(map, root, 16f, out var albinoCell, out var albinoError) == false)
					return AlbinoCase(caseName, false, error: albinoError?.ToString());

				var albino = SpawnAlbinoTestZombie(map, albinoCell, spawnedThings);
				if (albino == null)
					return AlbinoCase(caseName, false, error: "Could not create an albino fixture pawn.");

				var shooterCell = FindAlbinoLineOfSightCell(map, albino.Position + new IntVec3(8, 0, 0), albino.Position, 12f);
				if (shooterCell.IsValid == false)
					return AlbinoCase(caseName, false, error: "No clear line-of-sight shooter cell was available.");

				if (TryFindAdjacentClearCell(albino, out var vomitingCell) == false)
					return AlbinoCase(caseName, false, error: "No adjacent vomiting-pawn cell was available.");
				var blockerCell = GenAdj.CellsAdjacent8Way(albino)
					.Where(cell => cell.InBounds(map))
					.Where(cell => cell.Standable(map))
					.Where(cell => cell.Fogged(map) == false)
					.Where(cell => cell != vomitingCell)
					.Where(cell => cell.GetThingList(map).Any(thing => thing is Pawn) == false)
					.FirstOrDefault();
				if (blockerCell.IsValid == false)
					return AlbinoCase(caseName, false, error: "No adjacent non-vomiting pressure-pawn cell was available.");

				var weapon = SpawnAlbinoHackWeapon(map, albino.Position + new IntVec3(12, 0, 0), spawnedThings, out var weaponError);
				if (weapon == null)
					return AlbinoCase(caseName, false, error: weaponError);

				var shooter = SpawnAlbinoTestColonist(map, shooterCell, spawnedThings, true);
				var shooterVerb = EquipAreaWorkflowRangedWeapon(shooter);
				var shooterCanHitAlbino = shooterVerb != null && shooterVerb.CanHitTargetFrom(shooter.Position, albino);
				if (shooter == null || shooterVerb == null || shooterCanHitAlbino == false)
					return AlbinoCase(caseName, false, error: "Could not create a drafted ranged colonist with line of fire to the albino.");

				var vomiting = SpawnAlbinoTestColonist(map, vomitingCell, spawnedThings);
				vomiting.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Vomit), JobCondition.InterruptForced, null, true, true);
				AdvanceGameTicks(1);

				var driver = StartAlbinoSabotageDriver(albino);
				if (driver == null)
					return AlbinoCase(caseName, false, error: "Albino did not enter the sabotage driver.");

				ForceAlbinoHackTarget(driver, weapon);
				driver.nextDefensiveScreamCheckTick = 0;
				albino.albinoNextScreamTick = GenTicks.TicksGame;
				var invokedBefore = TryInvokeAlbinoDefensiveScreamSwitch(driver, out var handledBefore, out var beforeError);
				var singlePressureRedirected = handledBefore && driver.destination.IsValid;
				var singlePressureRedirectedWithDeferredTarget = singlePressureRedirected
					&& driver.hackTarget == null
					&& driver.deferredHackTarget == weapon;
				var singlePressureStayed = handledBefore == false && driver.hackTarget == weapon;
				var vomitingIgnored = invokedBefore
					&& driver.hackCounter == 0
					&& albino.scream == -1
					&& vomiting.CurJobDef == JobDefOf.Vomit
					&& (singlePressureRedirectedWithDeferredTarget || singlePressureStayed);

				var blocker = SpawnAlbinoTestColonist(map, blockerCell, spawnedThings);
				ForceAlbinoHackTarget(driver, weapon);
				driver.noSafeHackRoute = true;
				driver.nextDefensiveScreamCheckTick = 0;
				albino.albinoNextScreamTick = GenTicks.TicksGame;
				var invokedAfter = TryInvokeAlbinoDefensiveScreamSwitch(driver, out var handledAfter, out var afterError);
				var switchTriggered = invokedAfter
					&& handledAfter
					&& driver.hackTarget == weapon
					&& driver.hackCounter == 0
					&& driver.destination.IsValid == false
					&& albino.scream == -2;
				var success = vomitingIgnored && switchTriggered;

				return AlbinoCase(caseName, success, new
				{
					vomitingIgnored,
					singlePressureRedirected,
					singlePressureRedirectedWithDeferredTarget,
					singlePressureStayed,
					switchTriggered,
					invokedBefore,
					handledBefore,
					beforeError,
					invokedAfter,
					handledAfter,
					afterError,
					shooterCanHitAlbino,
					albinoScream = albino.scream,
					hackTargetAfter = driver.hackTarget == null ? null : ZombieRuntimeActions.StableThingId(driver.hackTarget),
					deferredHackTargetAfter = driver.deferredHackTarget == null ? null : ZombieRuntimeActions.StableThingId(driver.deferredHackTarget),
					vomitingJob = vomiting.CurJobDef?.defName,
					albino = DescribeZombie(albino),
					shooter = DescribePawn(shooter),
					vomiting = DescribePawn(vomiting),
					blocker = DescribePawn(blocker),
					weapon = ZombieRuntimeActions.StableThingId(weapon)
				}, beforeError ?? afterError);
			}
			finally
			{
				DestroyAlbinoCaseThings(spawnedThings);
			}
		}

		static bool HasAlbinoComp<T>(ThingDef def)
		{
			return def?.comps?.Any(comp => comp?.compClass != null && typeof(T).IsAssignableFrom(comp.compClass)) == true;
		}

		static ThingDef FindAlbinoPowerTraderOnlyBuildingDef()
		{
			var preferred = new[] { "PowerConduit", "HiddenPowerConduit", "VanometricPowerCell" };
			foreach (var defName in preferred)
			{
				var def = DefDatabase<ThingDef>.GetNamed(defName, false);
				if (HasAlbinoComp<CompPowerTrader>(def) && HasAlbinoComp<CompFlickable>(def) == false && HasAlbinoComp<CompBreakdownable>(def) == false)
					return def;
			}

			return DefDatabase<ThingDef>.AllDefsListForReading
				.FirstOrDefault(def => typeof(Building).IsAssignableFrom(def.thingClass)
					&& HasAlbinoComp<CompPowerTrader>(def)
					&& HasAlbinoComp<CompFlickable>(def) == false
					&& HasAlbinoComp<CompBreakdownable>(def) == false);
		}

		static AlbinoSabotageCase AlbinoPowerTraderOnlyIgnoredCase(Map map)
		{
			const string caseName = "power_trader_only_ignored";
			var spawnedThings = new List<Thing>();
			try
			{
				var def = FindAlbinoPowerTraderOnlyBuildingDef();
				if (def == null)
					return AlbinoCase(caseName, false, error: "No pure CompPowerTrader building def was found.");

				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root + new IntVec3(5, 0, 0), 16f, out var buildingCell, out var buildingError) == false)
					return AlbinoCase(caseName, false, error: buildingError?.ToString());

				var stuff = def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null;
				var building = GenSpawn.Spawn(ThingMaker.MakeThing(def, stuff), buildingCell, map, WipeMode.Vanish) as Building;
				building?.SetFaction(Faction.OfPlayer);
				if (building == null)
					return AlbinoCase(caseName, false, error: $"Could not spawn {def.defName} as a building.");
				spawnedThings.Add(building);

				var powerTrader = building.TryGetComp<CompPowerTrader>();
				if (powerTrader != null)
					powerTrader.PowerOn = true;
				var invoked = TryInvokeAlbinoCanHackBuilding(building, out var canHack, out var error);
				var flickable = building.TryGetComp<CompFlickable>();
				var breakdownable = building.TryGetComp<CompBreakdownable>();
				var success = invoked
					&& powerTrader != null
					&& flickable == null
					&& breakdownable == null
					&& powerTrader.PowerOn
					&& canHack == false;

				return AlbinoCase(caseName, success, new
				{
					def = def.defName,
					building = ZombieRuntimeActions.StableThingId(building),
					buildingCell = ZombieRuntimeActions.DescribeCell(buildingCell),
					invoked,
					canHack,
					powerOn = powerTrader?.PowerOn,
					hasFlickable = flickable != null,
					hasBreakdownable = breakdownable != null
				}, error);
			}
			finally
			{
				DestroyAlbinoCaseThings(spawnedThings);
			}
		}

		static AlbinoSabotageCase AlbinoFlickableHackCase(Map map)
		{
			var spawnedThings = new List<Thing>();
			try
			{
				_ = ZombieRuntimeActions.DestroyZombies(map);
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var albinoCell, out var albinoError) == false)
					return AlbinoCase("flickable_hack", false, error: albinoError?.ToString());
				if (TryFindClearSpawnCell(map, albinoCell + IntVec3.East, 6f, out var lampCell, out var lampError) == false)
					return AlbinoCase("flickable_hack", false, error: lampError?.ToString());

				var lampDef = DefDatabase<ThingDef>.GetNamed("StandingLamp", false);
				var lamp = lampDef == null ? null : GenSpawn.Spawn(ThingMaker.MakeThing(lampDef), lampCell, map, WipeMode.Vanish) as Building;
				lamp?.SetFaction(Faction.OfPlayer);
				if (lamp == null)
					return AlbinoCase("flickable_hack", false, error: "Could not create StandingLamp.");
				spawnedThings.Add(lamp);
				var flickable = lamp.TryGetComp<CompFlickable>();
				if (flickable == null)
					return AlbinoCase("flickable_hack", false, error: "StandingLamp has no CompFlickable.");
				flickable.SwitchIsOn = true;

				var albino = SpawnAlbinoTestZombie(map, albinoCell, spawnedThings);
				var run = RunForcedAlbinoHack(albino, lamp);
				var success = run.driverStillCurrent && flickable.SwitchIsOn == false && run.driver?.hackTarget == null;
				return AlbinoCase("flickable_hack", success, new
				{
					albino = DescribeZombie(albino),
					lamp = ZombieRuntimeActions.StableThingId(lamp),
					switchIsOn = flickable.SwitchIsOn,
					run.driverStillCurrent,
					hackCounter = run.driver?.hackCounter,
					samples = run.samples
				});
			}
			finally
			{
				DestroyAlbinoCaseThings(spawnedThings);
			}
		}

		static ThingDef FindAlbinoBreakdownableBuildingDef()
		{
			var preferred = new[] { "ElectricTailoringBench", "ElectricSmithy", "HiTechResearchBench", "CommsConsole", "Battery" };
			foreach (var defName in preferred)
			{
				var def = DefDatabase<ThingDef>.GetNamed(defName, false);
				if (def?.comps?.Any(comp => comp.compClass == typeof(CompBreakdownable)) == true)
					return def;
			}

			return DefDatabase<ThingDef>.AllDefsListForReading
				.FirstOrDefault(def => typeof(Building).IsAssignableFrom(def.thingClass) && def.comps?.Any(comp => comp.compClass == typeof(CompBreakdownable)) == true);
		}

		static AlbinoSabotageCase AlbinoBreakdownableHackCase(Map map)
		{
			var spawnedThings = new List<Thing>();
			try
			{
				_ = ZombieRuntimeActions.DestroyZombies(map);
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var albinoCell, out var albinoError) == false)
					return AlbinoCase("breakdownable_hack", false, error: albinoError?.ToString());
				if (TryFindClearSpawnCell(map, albinoCell + IntVec3.East, 8f, out var buildingCell, out var buildingError) == false)
					return AlbinoCase("breakdownable_hack", false, error: buildingError?.ToString());

				var def = FindAlbinoBreakdownableBuildingDef();
				if (def == null)
					return AlbinoCase("breakdownable_hack", false, error: "No breakdownable building def was found.");
				var stuff = def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null;
				var building = GenSpawn.Spawn(ThingMaker.MakeThing(def, stuff), buildingCell, map, WipeMode.Vanish) as Building;
				building?.SetFaction(Faction.OfPlayer);
				if (building == null)
					return AlbinoCase("breakdownable_hack", false, error: $"Could not spawn {def.defName} as a building.");
				spawnedThings.Add(building);
				var flickable = building.TryGetComp<CompFlickable>();
				if (flickable != null)
					flickable.SwitchIsOn = false;
				var breakdownable = building.TryGetComp<CompBreakdownable>();
				if (breakdownable == null)
					return AlbinoCase("breakdownable_hack", false, error: $"{def.defName} did not expose CompBreakdownable.");

				var albino = SpawnAlbinoTestZombie(map, albinoCell, spawnedThings);
				var brokenBefore = breakdownable.BrokenDown;
				var run = RunForcedAlbinoHack(albino, building);
				var success = run.driverStillCurrent && brokenBefore == false && breakdownable.BrokenDown && run.driver?.hackTarget == null;
				return AlbinoCase("breakdownable_hack", success, new
				{
					albino = DescribeZombie(albino),
					building = ZombieRuntimeActions.StableThingId(building),
					def = def.defName,
					brokenBefore,
					brokenAfter = breakdownable.BrokenDown,
					run.driverStillCurrent,
					hackCounter = run.driver?.hackCounter,
					samples = run.samples
				});
			}
			finally
			{
				DestroyAlbinoCaseThings(spawnedThings);
			}
		}

		static AlbinoSabotageCase AlbinoWeaponHackCase(Map map)
		{
			var spawnedThings = new List<Thing>();
			try
			{
				_ = ZombieRuntimeActions.DestroyZombies(map);
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var albinoCell, out var albinoError) == false)
					return AlbinoCase("weapon_hack", false, error: albinoError?.ToString());
				if (TryFindClearSpawnCell(map, albinoCell + IntVec3.East, 6f, out var weaponCell, out var weaponError) == false)
					return AlbinoCase("weapon_hack", false, error: weaponError?.ToString());

				var weaponDef = DefDatabase<ThingDef>.GetNamed("Gun_BoltActionRifle", false)
					?? DefDatabase<ThingDef>.GetNamed("Gun_Pistol", false);
				var weapon = weaponDef == null ? null : ThingMaker.MakeThing(weaponDef);
				if (weapon == null)
					return AlbinoCase("weapon_hack", false, error: "No ranged weapon def was available.");
				GenSpawn.Spawn(weapon, weaponCell, map, WipeMode.Vanish);
				spawnedThings.Add(weapon);
				var hitPointsBefore = weapon.HitPoints;

				var albino = SpawnAlbinoTestZombie(map, albinoCell, spawnedThings);
				var run = RunForcedAlbinoHack(albino, weapon);
				var pausedAfterHack = run.driver?.recentlyHackedTargets?.Contains(weapon) == true;
				var pauseRemainingTicks = 0;
				if (pausedAfterHack && run.driver.recentlyHackedTargetPauseUntilTicks != null)
				{
					var pauseIndex = run.driver.recentlyHackedTargets.IndexOf(weapon);
					if (pauseIndex >= 0 && pauseIndex < run.driver.recentlyHackedTargetPauseUntilTicks.Count)
						pauseRemainingTicks = Math.Max(0, run.driver.recentlyHackedTargetPauseUntilTicks[pauseIndex] - GenTicks.TicksGame);
				}
				var immediatelyRetargetedWeapon = run.driver?.BestReachableHackTarget(new[] { weapon }) == weapon;
				var success = run.driverStillCurrent
					&& weapon.HitPoints < hitPointsBefore
					&& run.driver?.hackTarget == null
					&& pausedAfterHack
					&& immediatelyRetargetedWeapon == false;
				return AlbinoCase("weapon_hack", success, new
				{
					albino = DescribeZombie(albino),
					weapon = ZombieRuntimeActions.StableThingId(weapon),
					weaponDef = weaponDef.defName,
					hitPointsBefore,
					hitPointsAfter = weapon.HitPoints,
					pausedAfterHack,
					pauseRemainingTicks,
					immediatelyRetargetedWeapon,
					run.driverStillCurrent,
					hackCounter = run.driver?.hackCounter,
					samples = run.samples
				});
			}
			finally
			{
				DestroyAlbinoCaseThings(spawnedThings);
			}
		}

		[Tool("zombieland/damage_tanky_armor", Description = "Apply real bullet damage to a tanky zombie and verify the tanky armor patch absorbs it by degrading armor.")]
		public static object DamageTankyArmor(
			[ToolParameter(Description = "Optional tanky zombie id, ThingID, label, or short name. When omitted, a fresh tanky zombie is spawned near map center.", Required = false, DefaultValue = "")] string target = "",
			[ToolParameter(Description = "Bullet damage amount used for the absorption sample.", Required = false, DefaultValue = 50)] int damage = 50,
			[ToolParameter(Description = "Deterministic Rand seed for hit-part selection.", Required = false, DefaultValue = 424242)] int seed = 424242)
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			Zombie tanky;
			var spawnedTanky = false;
			if (string.IsNullOrWhiteSpace(target))
			{
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var cell, out var error) == false)
					return error;

				tanky = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.TankyOperator, true);
				spawnedTanky = true;
			}
			else if (TryFindZombie(map, target, out var pawn, out var error) == false)
			{
				return new
				{
					success = false,
					error
				};
			}
			else
			{
				tanky = pawn as Zombie;
			}

			if (tanky == null || tanky.IsTanky == false)
			{
				return new
				{
					success = false,
					target = DescribeZombie(tanky),
					error = "Target is not a tanky zombie."
				};
			}

			var cappedDamage = Math.Max(1, Math.Min(damage, 500));
			var before = DescribeZombie(tanky);
			var armorBefore = DescribeTankyArmor(tanky);
			var healthBefore = tanky.health.summaryHealth.SummaryHealthPercent;
			DamageWorker.DamageResult result;
			Rand.PushState(seed);
			try
			{
				var dinfo = new DamageInfo(DamageDefOf.Bullet, cappedDamage, 0f, -1f, null, null, null, DamageInfo.SourceCategory.ThingOrUnknown, null, true, true);
				result = tanky.TakeDamage(dinfo);
			}
			finally
			{
				Rand.PopState();
			}
			var healthAfter = tanky.health.summaryHealth.SummaryHealthPercent;

			var shieldChanged = tanky.hasTankyShield < 1f;
			var helmetChanged = tanky.hasTankyHelmet < 1f;
			var suitChanged = tanky.hasTankySuit < 1f;
			var anyArmorChanged = shieldChanged || helmetChanged || suitChanged;

			return new
			{
				success = anyArmorChanged && result.totalDamageDealt <= 0f && healthAfter >= healthBefore,
				spawnedTanky,
				seed,
				damage = cappedDamage,
				totalDamageDealt = result.totalDamageDealt,
				healthBefore,
				healthAfter,
				armorBefore,
				armorAfter = DescribeTankyArmor(tanky),
				shieldChanged,
				helmetChanged,
				suitChanged,
				before,
				after = DescribeZombie(tanky)
			};
		}

		[Tool("zombieland/smash_with_tanky", Description = "Put a wall on a tanky zombie route and verify the real stumble-to-AttackStatic job path damages it.")]
		public static object SmashWithTanky(
			[ToolParameter(Description = "Optional tanky zombie id, ThingID, label, or short name. When omitted, a fresh tanky zombie is spawned near map center.", Required = false, DefaultValue = "")] string target = "",
			[ToolParameter(Description = "Deterministic Rand seed for the melee attack sample.", Required = false, DefaultValue = 616161)] int seed = 616161)
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			Zombie tanky;
			var spawnedTanky = false;
			if (string.IsNullOrWhiteSpace(target))
			{
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var cell, out var error) == false)
					return error;

				tanky = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.TankyOperator, true);
				spawnedTanky = true;
			}
			else if (TryFindZombie(map, target, out var pawn, out var error) == false)
			{
				return new
				{
					success = false,
					error
				};
			}
			else
			{
				tanky = pawn as Zombie;
			}

			if (tanky == null || tanky.IsTanky == false)
			{
				return new
				{
					success = false,
					target = DescribeZombie(tanky),
					error = "Target is not a tanky zombie."
				};
			}

			if (TryFindAdjacentBuildingCell(tanky, out var buildingCell) == false)
			{
				return new
				{
					success = false,
					target = DescribeZombie(tanky),
					error = "No clear adjacent wall cell was found."
				};
			}

			var wall = ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.WoodLog) as Building;
			if (wall == null)
			{
				return new
				{
					success = false,
					error = "Could not create test wall."
				};
			}
			GenSpawn.Spawn(wall, buildingCell, map, WipeMode.Vanish);
			wall.SetFaction(Faction.OfPlayer);

			tanky.pather?.StopDead();
			tanky.jobs?.EndCurrentJob(JobCondition.InterruptForced);
			tanky.state = ZombieState.Wandering;
			tanky.checkSmashable = true;
			tanky.tankDestination = buildingCell;

			var info = ZombieWanderer.GetMapInfo(map);
			var recalc = info.RecalculateAll(new[] { buildingCell }, CurrentZombies(map).OfType<Zombie>());
			var recalcSteps = 0;
			while (recalcSteps < 2048 && recalc.MoveNext())
				recalcSteps++;
			var routeParentIgnoringBuildings = info.GetParent(tanky.Position, true);
			var routeParentRespectingBuildings = info.GetParent(tanky.Position, false);

			var before = DescribeZombie(tanky);
			var hitPointsBefore = wall.HitPoints;
			var wallId = ZombieRuntimeActions.StableThingId(wall);
			var samples = new List<object>();
			var sawAttackStaticJob = false;
			tanky.jobs.StartJob(JobMaker.MakeJob(CustomDefs.Stumble), JobCondition.InterruptForced, null, true, false, null, null);
			if (tanky.jobs.curDriver is JobDriver_Stumble stumbleDriver)
				stumbleDriver.destination = IntVec3.Invalid;

			Rand.PushState(seed);
			try
			{
				for (var i = 0; i < 3; i++)
				{
					AdvanceGameTicks(1);
					var currentJob = tanky.CurJobDef?.defName;
					var stumbleDestination = tanky.jobs.curDriver is JobDriver_Stumble currentStumbleDriver
						? currentStumbleDriver.destination
						: IntVec3.Invalid;
					if (currentJob == JobDefOf.AttackStatic.defName)
						sawAttackStaticJob = true;
					samples.Add(new
					{
						tick = i + 1,
						currentJob,
						stumbleDestination = ZombieRuntimeActions.DescribeCell(stumbleDestination),
						fullBodyBusy = tanky.stances?.FullBodyBusy ?? false,
						wallDestroyed = wall.Destroyed,
						wallHitPoints = wall.Destroyed ? 0 : wall.HitPoints
					});
					if (wall.Destroyed || wall.HitPoints < hitPointsBefore)
						break;
				}
			}
			finally
			{
				Rand.PopState();
			}

			var wallDestroyed = wall.Destroyed;
			var hitPointsAfter = wallDestroyed ? 0 : wall.HitPoints;

			return new
			{
				success = (wallDestroyed || hitPointsAfter < hitPointsBefore)
					&& sawAttackStaticJob,
				spawnedTanky,
				seed,
				sawAttackStaticJob,
				tankyCell = ZombieRuntimeActions.DescribeCell(tanky.Position),
				buildingCell = ZombieRuntimeActions.DescribeCell(buildingCell),
				routeParentIgnoringBuildings = ZombieRuntimeActions.DescribeCell(routeParentIgnoringBuildings),
				routeParentRespectingBuildings = ZombieRuntimeActions.DescribeCell(routeParentRespectingBuildings),
				recalcSteps,
				wallId,
				wallDef = wall.def.defName,
				wallDestroyed,
				hitPointsBefore,
				hitPointsAfter,
				hitPointDelta = hitPointsAfter - hitPointsBefore,
				before,
				after = DescribeZombie(tanky),
				samples
			};
		}

		[Tool("zombieland/former_zombie_hidden_conduit_track_contract", Description = "Put former-pawn zombies on pheromone trails through hidden conduits and inert spot/grave buildings, verifying they do not loop AttackStatic while a normal wall is still smashed.")]
		public static object FormerZombieHiddenConduitTrackContract(
			[ToolParameter(Description = "Root x coordinate. Use -1 with z -1 to search near map center.", Required = false, DefaultValue = -1)] int x = -1,
			[ToolParameter(Description = "Root z coordinate. Use -1 with x -1 to search near map center.", Required = false, DefaultValue = -1)] int z = -1,
			[ToolParameter(Description = "Number of ticks to sample each case. Clamped to 10..240.", Required = false, DefaultValue = 80)] int sampleTicks = 80,
			[ToolParameter(Description = "Deterministic Rand seed for melee attack samples.", Required = false, DefaultValue = 777331)] int seed = 777331)
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			var root = x >= 0 && z >= 0 ? new IntVec3(x, 0, z) : new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (root.InBounds(map) == false)
			{
				return new
				{
					success = false,
					error = $"Cell ({root.x}, {root.z}) is outside the current map."
				};
			}

			var noAttackDefs = ExpectedNoAttackBuildingDefNames()
				.Select(defName => DefDatabase<ThingDef>.GetNamed(defName, false))
				.Where(def => def != null)
				.ToArray();
			if (TryFindFormerConduitFixtureCells(map, root, noAttackDefs.Length, out var noAttackCells, out var wallCells, out var cellError) == false)
				return cellError;

			var settingsSnapshot = SnapshotZombieSettings();
			var spawnedThings = new List<Thing>();
			var pheromoneSnapshot = SnapshotPheromones(map, root, 24f);
			var sampleCount = Math.Max(10, Math.Min(sampleTicks, 240));
			try
			{
				ApplyZombieSettingsOverride(settings =>
				{
					settings.smashMode = SmashMode.AnyBuilding;
					settings.smashOnlyWhenAgitated = true;
					settings.zombiesEatCorpses = false;
					settings.zombiesEatDowned = false;
					settings.ragingZombies = false;
				});

				ClearPheromones(map, root, 24f);
				var noAttackCases = noAttackDefs
					.Select((def, index) =>
						RunFormerZombieConduitSmashCase(
							map,
							$"no-attack-{def.defName}",
							noAttackCells[index].ZombieCell,
							noAttackCells[index].BuildingCell,
							noAttackCells[index].TrailCell,
							def.defName,
							expectedAttack: false,
							spawnedThings,
							sampleCount,
							seed + index))
					.ToArray();
				var wallCase = RunFormerZombieConduitSmashCase(
					map,
					"wall-control",
					wallCells.ZombieCell,
					wallCells.BuildingCell,
					wallCells.TrailCell,
					"Wall",
					expectedAttack: true,
					spawnedThings,
					sampleCount,
					seed + noAttackDefs.Length);

				return new
				{
					success = noAttackCases.All(FormerConduitCaseSucceeded) && FormerConduitCaseSucceeded(wallCase),
					root = ZombieRuntimeActions.DescribeCell(root),
					settingsOverride = new
					{
						smashMode = SmashMode.AnyBuilding.ToString(),
						zombiesEatCorpses = false,
						zombiesEatDowned = false,
						ragingZombies = false
					},
					noAttackDefNames = noAttackDefs.Select(def => def.defName).ToArray(),
					missingNoAttackDefNames = ExpectedNoAttackBuildingDefNames().Except(noAttackDefs.Select(def => def.defName)).ToArray(),
					noAttackCases,
					wallCase
				};
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
				RestorePheromones(map, pheromoneSnapshot);
				foreach (var thing in spawnedThings.Where(thing => thing != null).Distinct().Reverse().ToArray())
				{
					if (thing.Destroyed || thing.Spawned == false)
						continue;
					thing.Destroy(DestroyMode.Vanish);
				}
			}
		}

		static object RunFormerZombieConduitSmashCase(Map map, string label, IntVec3 zombieCell, IntVec3 buildingCell, IntVec3 trailCell, string buildingDefName, bool expectedAttack, List<Thing> spawnedThings, int sampleTicks, int seed)
		{
			var zombie = SpawnFormerPawnZombie(map, zombieCell, $"ZL_Conduit_{label}", spawnedThings, out var spawnError);
			if (zombie == null)
			{
				return new
				{
					success = false,
					label,
					expectedAttack,
					error = spawnError,
					zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell)
				};
			}

			var building = SpawnConduitContractBuilding(map, buildingCell, buildingDefName, spawnedThings, out var buildingError);
			if (building == null)
			{
				return new
				{
					success = false,
					label,
					expectedAttack,
					error = buildingError,
					zombie = DescribeZombie(zombie),
					buildingCell = ZombieRuntimeActions.DescribeCell(buildingCell)
				};
			}

			zombie.pather?.StopDead();
			zombie.jobs?.EndCurrentJob(JobCondition.InterruptForced);
			zombie.state = ZombieState.Wandering;
			zombie.raging = 0;
			zombie.checkSmashable = true;
			zombie.wasMapPawnBefore = true;

			var grid = map.GetGrid();
			var now = Tools.Ticks();
			grid.SetTimestamp(buildingCell, now);
			grid.SetTimestamp(trailCell, now + 1);

			var hitPointsBefore = building.HitPoints;
			var before = DescribeZombie(zombie);
			var samples = new List<object>();
			var sawAttackStaticJob = false;
			var sawStumbleMovementIntent = false;

			zombie.jobs.StartJob(JobMaker.MakeJob(CustomDefs.Stumble), JobCondition.InterruptForced, null, true, false, null, null);
			if (zombie.jobs.curDriver is JobDriver_Stumble initialStumbleDriver)
				initialStumbleDriver.destination = IntVec3.Invalid;

			Rand.PushState(seed);
			try
			{
				for (var i = 0; i < sampleTicks; i++)
				{
					AdvanceGameTicks(1);
					var currentJob = zombie.CurJobDef?.defName;
					var stumbleDestination = zombie.jobs.curDriver is JobDriver_Stumble currentStumbleDriver
						? currentStumbleDriver.destination
						: IntVec3.Invalid;
					if (currentJob == JobDefOf.AttackStatic.defName)
						sawAttackStaticJob = true;
					if (currentJob == CustomDefs.Stumble.defName && (zombie.pather?.Moving == true || stumbleDestination.IsValid))
						sawStumbleMovementIntent = true;

					if (i < 12 || i % 10 == 9 || i == sampleTicks - 1)
						samples.Add(new
						{
							tick = i + 1,
							currentJob,
							stumbleDestination = ZombieRuntimeActions.DescribeCell(stumbleDestination),
							zombiePosition = ZombieRuntimeActions.DescribeCell(zombie.Position),
							patherMoving = zombie.pather?.Moving ?? false,
							patherDestination = zombie.pather?.Moving == true ? ZombieRuntimeActions.DescribeCell(zombie.pather.Destination.Cell) : null,
							fullBodyBusy = zombie.stances?.FullBodyBusy ?? false,
							buildingDestroyed = building.Destroyed,
							buildingHitPoints = building.Destroyed ? 0 : building.HitPoints
						});

					if (building.Destroyed || building.HitPoints < hitPointsBefore)
						break;
					if (expectedAttack == false && sawStumbleMovementIntent && sawAttackStaticJob == false)
						break;
				}
			}
			finally
			{
				Rand.PopState();
			}

			var buildingDestroyed = building.Destroyed;
			var hitPointsAfter = buildingDestroyed ? 0 : building.HitPoints;
			var movedFromStart = zombie.Position != zombieCell;

			return new
			{
				success = expectedAttack == false
					? sawAttackStaticJob == false && hitPointsAfter == hitPointsBefore && (sawStumbleMovementIntent || movedFromStart)
					: sawAttackStaticJob && (buildingDestroyed || hitPointsAfter < hitPointsBefore),
				label,
				expectedAttack,
				seed,
				zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
				buildingCell = ZombieRuntimeActions.DescribeCell(buildingCell),
				trailCell = ZombieRuntimeActions.DescribeCell(trailCell),
				buildingDefName,
				buildingId = ZombieRuntimeActions.StableThingId(building),
				buildingDestroyed,
				hitPointsBefore,
				hitPointsAfter,
				hitPointDelta = hitPointsAfter - hitPointsBefore,
				buildingProperties = DescribeBuildingSmashProperties(building),
				sawAttackStaticJob,
				sawStumbleMovementIntent,
				movedFromStart,
				before,
				after = DescribeZombie(zombie),
				samples
			};
		}

		static string[] ExpectedNoAttackBuildingDefNames()
		{
			return new[]
			{
				"HiddenConduit",
				"Grave",
				"SleepingSpot",
				"DoubleSleepingSpot",
				"AnimalSleepingSpot",
				"BabySleepingSpot",
				"MarriageSpot",
				"PartySpot",
				"CaravanPackingSpot",
				"CraftingSpot",
				"ButcherSpot",
				"MeditationSpot",
				"RitualSpot",
				"PsychicRitualSpot",
				"HoldingSpot"
			};
		}

		static bool FormerConduitCaseSucceeded(object result)
		{
			var property = result?.GetType().GetProperty("success");
			return property?.GetValue(result) is bool success && success;
		}

		static Zombie SpawnFormerPawnZombie(Map map, IntVec3 cell, string name, List<Thing> spawnedThings, out string error)
		{
			error = null;
			var beforeIds = CurrentZombies(map).Select(ZombieRuntimeActions.StableThingId).ToHashSet(StringComparer.OrdinalIgnoreCase);
			var request = new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, forceGenerateNewPawn: true, canGeneratePawnRelations: false, allowAddictions: false);
			var pawn = PawnGenerator.GeneratePawn(request);
			pawn.Name = new NameSingle(name);
			GenSpawn.Spawn(pawn, cell, map, Rot4.East);
			DisablePawnWork(pawn);
			pawn.apparel?.DestroyAll();
			pawn.equipment?.DestroyAllEquipment(DestroyMode.Vanish);
			pawn.inventory?.DestroyAll();
			spawnedThings.Add(pawn);
			if (ZombieRuntimeActions.KillPawnToCorpse(pawn, out var corpse, out error) == false)
				return null;
			if (corpse != null)
				spawnedThings.Add(corpse);

			Tools.ConvertToZombie(corpse, map, true);
			var zombie = CurrentZombies(map)
				.OfType<Zombie>()
				.Where(candidate => beforeIds.Contains(ZombieRuntimeActions.StableThingId(candidate)) == false)
				.OrderBy(candidate => candidate.Position.DistanceToSquared(cell))
				.FirstOrDefault();
			if (zombie == null)
			{
				error = "Corpse conversion did not create a new former-pawn zombie.";
				return null;
			}
			spawnedThings.Add(zombie);
			return zombie;
		}

		static Building SpawnConduitContractBuilding(Map map, IntVec3 cell, string defName, List<Thing> spawnedThings, out string error)
		{
			error = null;
			var def = DefDatabase<ThingDef>.GetNamed(defName, false);
			if (def == null)
			{
				error = $"ThingDef {defName} was not found.";
				return null;
			}

			var stuffDef = def.MadeFromStuff ? ThingDefOf.WoodLog : null;
			var building = ThingMaker.MakeThing(def, stuffDef) as Building;
			if (building == null)
			{
				error = $"ThingDef {defName} did not create a Building.";
				return null;
			}

			GenSpawn.Spawn(building, cell, map, WipeMode.Vanish);
			building.SetFaction(Faction.OfPlayer);
			spawnedThings.Add(building);
			return building;
		}

		static object DescribeBuildingSmashProperties(Building building)
		{
			var props = building?.def?.building;
			return new
			{
				defName = building?.def?.defName,
				useHitPoints = building?.def?.useHitPoints,
				isNaturalRock = props?.isNaturalRock,
				isTargetable = props?.isTargetable,
				canBeDamagedByAttacks = props?.canBeDamagedByAttacks,
				isPowerConduit = props?.isPowerConduit,
				passability = building?.def?.passability.ToString(),
				edifice = building?.Position.GetEdifice(building.Map)?.def?.defName
			};
		}

		static bool TryFindFormerConduitFixtureCells(Map map, IntVec3 root, int noAttackCaseCount, out FormerConduitFixtureCells[] noAttackCells, out FormerConduitFixtureCells wallCells, out object error)
		{
			noAttackCells = Array.Empty<FormerConduitFixtureCells>();
			wallCells = null;
			error = null;

			foreach (var candidate in GenRadial.RadialCellsAround(root, 24f, true))
			{
				var cells = Enumerable.Range(0, noAttackCaseCount + 1)
					.Select(index => new FormerConduitFixtureCells
					{
						ZombieCell = candidate + new IntVec3(0, 0, index * 5),
						BuildingCell = candidate + new IntVec3(1, 0, index * 5),
						TrailCell = candidate + new IntVec3(2, 0, index * 5)
					})
					.ToArray();
				if (cells
					.SelectMany(cellSet => new[] { cellSet.ZombieCell, cellSet.BuildingCell, cellSet.TrailCell })
					.All(cell => IsClearFormerConduitFixtureCell(map, cell)) == false)
					continue;

				noAttackCells = cells.Take(noAttackCaseCount).ToArray();
				wallCells = cells[noAttackCaseCount];
				return true;
			}

			error = new
			{
				success = false,
				requested = ZombieRuntimeActions.DescribeCell(root),
				error = "No clear former-zombie conduit fixture area was found."
			};
			return false;
		}

		sealed class FormerConduitFixtureCells
		{
			public IntVec3 ZombieCell { get; set; }
			public IntVec3 BuildingCell { get; set; }
			public IntVec3 TrailCell { get; set; }
		}

		static bool IsClearFormerConduitFixtureCell(Map map, IntVec3 cell)
		{
			return cell.InBounds(map)
				&& cell.Standable(map)
				&& cell.Fogged(map) == false
				&& cell.GetThingList(map).Any(thing => thing is Pawn || thing is Building || thing is Corpse) == false;
		}

		static void RestorePheromones(Map map, Dictionary<IntVec3, long> snapshot)
		{
			var grid = map.GetGrid();
			foreach (var pair in snapshot)
				grid.SetTimestamp(pair.Key, pair.Value);
		}

		sealed class SkinColorCase
		{
			public string name { get; set; }
			public string pawnId { get; set; }
			public string type { get; set; }
			public string defName { get; set; }
			public bool hasStory { get; set; }
			public bool storyInjectedForProbe { get; set; }
			public bool expectWhite { get; set; }
			public object color { get; set; }
			public object expectedColor { get; set; }
			public bool success { get; set; }
		}

		sealed class GeneRejectionCase
		{
			public string name { get; set; }
			public string pawnId { get; set; }
			public string type { get; set; }
			public string defName { get; set; }
			public bool expectReject { get; set; }
			public bool hadGeneTracker { get; set; }
			public bool geneTrackerInjectedForProbe { get; set; }
			public int initialGeneCount { get; set; }
			public int afterPublicGeneDefCount { get; set; }
			public int afterPrivateGeneCount { get; set; }
			public bool publicGeneDefResultNull { get; set; }
			public bool privateGeneResultNull { get; set; }
			public string publicGeneDefError { get; set; }
			public string privateGeneError { get; set; }
			public bool success { get; set; }
		}

		static bool EnsureStoryTrackerForSkinColorProbe(Pawn pawn)
		{
			if (pawn == null || pawn.story != null)
				return false;
			pawn.story = new Pawn_StoryTracker(pawn);
			return true;
		}

		static bool EnsureGeneTrackerForProbe(Pawn pawn)
		{
			if (pawn == null || pawn.genes != null)
				return false;
			pawn.genes = new Pawn_GeneTracker(pawn);
			return true;
		}

		static SkinColorCase DescribeSkinColorCase(string name, Pawn pawn, Color expectedColor, bool expectWhite, bool storyInjectedForProbe)
		{
			var hasStory = pawn?.story != null;
			var color = hasStory ? pawn.story.SkinColorBase : Color.clear;
			return new SkinColorCase
			{
				name = name,
				pawnId = ZombieRuntimeActions.StableThingId(pawn),
				type = pawn?.GetType().FullName,
				defName = pawn?.def?.defName,
				hasStory = hasStory,
				storyInjectedForProbe = storyInjectedForProbe,
				expectWhite = expectWhite,
				color = DescribeColor(color),
				expectedColor = DescribeColor(expectedColor),
				success = pawn != null && hasStory && ColorsApproximatelyEqual(color, expectedColor)
			};
		}

		static GeneRejectionCase DescribeGeneRejectionCase(string name, Pawn pawn, bool expectReject, MethodInfo privateAddGene)
		{
			var hadGeneTracker = pawn?.genes != null;
			var geneTrackerInjectedForProbe = EnsureGeneTrackerForProbe(pawn);
			var initialGeneCount = CountGenes(pawn);
			var publicGeneDef = MakeProbeGeneDef(name, "public");
			var privateGeneDef = MakeProbeGeneDef(name, "private");

			Gene publicResult = null;
			string publicError = null;
			try
			{
				publicResult = pawn?.genes?.AddGene(publicGeneDef, false);
			}
			catch (Exception ex)
			{
				publicError = DescribeException(ex);
			}
			var afterPublicGeneDefCount = CountGenes(pawn);

			var privateGene = MakeProbeGene(privateGeneDef, pawn);
			var privateResult = InvokePrivateAddGene(privateAddGene, pawn?.genes, privateGene, false, out var privateError);
			var afterPrivateGeneCount = CountGenes(pawn);

			var publicRejected = publicResult == null && publicError == null && afterPublicGeneDefCount == initialGeneCount;
			var privateRejected = privateResult == null && privateError == null && afterPrivateGeneCount == afterPublicGeneDefCount;
			var publicPreserved = publicResult != null && publicError == null && afterPublicGeneDefCount == initialGeneCount + 1;
			var privatePreserved = privateResult != null && privateError == null && afterPrivateGeneCount == afterPublicGeneDefCount + 1;

			return new GeneRejectionCase
			{
				name = name,
				pawnId = ZombieRuntimeActions.StableThingId(pawn),
				type = pawn?.GetType().FullName,
				defName = pawn?.def?.defName,
				expectReject = expectReject,
				hadGeneTracker = hadGeneTracker,
				geneTrackerInjectedForProbe = geneTrackerInjectedForProbe,
				initialGeneCount = initialGeneCount,
				afterPublicGeneDefCount = afterPublicGeneDefCount,
				afterPrivateGeneCount = afterPrivateGeneCount,
				publicGeneDefResultNull = publicResult == null,
				privateGeneResultNull = privateResult == null,
				publicGeneDefError = publicError,
				privateGeneError = privateError,
				success = expectReject ? publicRejected && privateRejected : publicPreserved && privatePreserved
			};
		}

		static GeneDef MakeProbeGeneDef(string caseName, string overloadName)
		{
			return new GeneDef
			{
				defName = $"ZL_ProbeGene_{caseName}_{overloadName}_{Guid.NewGuid():N}",
				label = $"ZL probe gene {caseName} {overloadName}",
				geneClass = typeof(Gene)
			};
		}

		static Gene MakeProbeGene(GeneDef geneDef, Pawn pawn)
		{
			return new Gene
			{
				def = geneDef,
				pawn = pawn
			};
		}

		static int CountGenes(Pawn pawn)
		{
			return pawn?.genes?.GenesListForReading?.Count ?? -1;
		}

		static Gene InvokePrivateAddGene(MethodInfo privateAddGene, Pawn_GeneTracker tracker, Gene gene, bool addAsXenogene, out string error)
		{
			error = null;
			if (privateAddGene == null || tracker == null)
			{
				error = "Missing private AddGene method or gene tracker.";
				return null;
			}
			try
			{
				return privateAddGene.Invoke(tracker, new object[] { gene, addAsXenogene }) as Gene;
			}
			catch (TargetInvocationException ex)
			{
				error = DescribeException(ex.InnerException ?? ex);
				return null;
			}
			catch (Exception ex)
			{
				error = DescribeException(ex);
				return null;
			}
		}

		static string DescribeException(Exception ex)
		{
			return ex == null ? null : $"{ex.GetType().FullName}: {ex.Message}";
		}

	}
}
