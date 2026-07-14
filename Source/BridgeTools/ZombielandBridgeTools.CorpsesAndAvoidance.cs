using HarmonyLib;
using RimBridgeServer.Sdk;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unity.Collections;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	public sealed partial class ZombielandBridgeTools
	{
		[Tool("zombieland/convert_infected_corpse_to_zombie", Description = "Create an infected corpse from a spawned pawn, verify rot-stage or rare-tick conversion queuing, then run that queued conversion.")]
		public static object ConvertInfectedCorpseToZombie(
			[ToolParameter(Description = "Pawn id, ThingID, label, or short name.", Required = true)] string target,
			[ToolParameter(Description = "Bite state to apply before death: harmful, final, or harmless.", Required = false, DefaultValue = "final")] string stage = "final",
			[ToolParameter(Description = "Conversion trigger to exercise: rotStage or tickRare.", Required = false, DefaultValue = "rotStage")] string conversionTrigger = "rotStage",
			[ToolParameter(Description = "Stage a weapon and inventory stack on the pawn, then verify conversion preserves them as recoverable zombie inventory and drops them when the zombie dies.", Required = false, DefaultValue = false)] bool recoverableLootProbe = false)
		{
			var map = CurrentMap;
			if (ZombieRuntimeActions.TryFindPawn(map, target, out var pawn, out var error) == false)
			{
				return new
				{
					success = false,
					error
				};
			}
			if (pawn is Zombie || pawn is ZombieSymbiant || pawn is ZombieSpitter)
			{
				return new
				{
					success = false,
					error = "Target is already a Zombieland pawn."
				};
			}

			var before = CurrentZombies(map);
			var beforeIds = new HashSet<string>(before.Select(ZombieRuntimeActions.StableThingId));
			var targetId = ZombieRuntimeActions.StableThingId(pawn);
			var targetThingId = pawn.ThingID;
			var targetLabel = pawn.LabelCap;
			RecoverableLootProbeState stagedRecoverableLoot = null;

			if (ZombieRuntimeActions.AddZombieBite(pawn, stage, out var bite, out error) == false)
			{
				return new
				{
					success = false,
					targetId,
					targetThingId,
					targetLabel,
					recoverableLootProbe = DescribeRecoverableLootProbe(stagedRecoverableLoot, null, false),
					error
				};
			}

			if (ZombieRuntimeActions.KillPawnToCorpse(pawn, out var corpse, out error) == false)
			{
				return new
				{
					success = false,
					targetId,
					targetThingId,
					targetLabel,
					biteLabel = bite.LabelCap,
					recoverableLootProbe = DescribeRecoverableLootProbe(stagedRecoverableLoot, null, false),
					error
				};
			}

			if (recoverableLootProbe && TryStageRecoverableLootProbe(corpse.InnerPawn, out stagedRecoverableLoot, out error) == false)
			{
				return new
				{
					success = false,
					targetId,
					targetThingId,
					targetLabel,
					biteLabel = bite.LabelCap,
					corpse = DescribeCorpse(corpse),
					recoverableLootProbe = new { requested = true, error },
					error
				};
			}

			var normalizedTrigger = (conversionTrigger ?? "rotStage").Trim().ToLowerInvariant();
			var corpseBeforeTrigger = DescribeCorpse(corpse);
			object triggerEvidence;
			if (TryTriggerCorpseConversion(corpse, map, normalizedTrigger, out triggerEvidence, out error) == false)
			{
				return new
				{
					success = false,
					targetId,
					targetThingId,
					targetLabel,
					biteLabel = bite.LabelCap,
					conversionTrigger = normalizedTrigger,
					corpse = corpseBeforeTrigger,
					recoverableLootProbe = DescribeRecoverableLootProbe(stagedRecoverableLoot, null, false),
					error
				};
			}

			var corpseAfterTrigger = DescribeCorpse(corpse);
			var convertedQueuedCorpse = ZombieRuntimeActions.RunQueuedConversion(map, corpse, out var queueCountBeforeRun, out var queueCountAfterRun, out error);
			var after = CurrentZombies(map);
			var newZombiePawns = after
				.Where(zombie => beforeIds.Contains(ZombieRuntimeActions.StableThingId(zombie)) == false)
				.ToArray();
			var recoverableLootEvidence = VerifyRecoverableLootProbe(map, newZombiePawns.OfType<Zombie>().FirstOrDefault(), stagedRecoverableLoot);
			var newZombies = newZombiePawns
				.Select(DescribeZombie)
				.ToArray();

			return new
			{
				success = convertedQueuedCorpse && newZombies.Length > 0 && RecoverableLootProbeSucceeded(recoverableLootEvidence),
				targetId,
				targetThingId,
				targetLabel,
				stage = stage ?? "final",
				conversionTrigger = normalizedTrigger,
				biteLabel = bite.LabelCap,
				triggerEvidence,
				corpseBeforeTrigger,
				corpseAfterTrigger,
				queuedConversionFound = convertedQueuedCorpse,
				queueCountBeforeRun,
				queueCountAfterRun,
				error,
				beforeCount = before.Length,
				afterCount = after.Length,
				newZombieCount = newZombies.Length,
				recoverableLootProbe = recoverableLootEvidence,
				newZombies
			};
		}

		sealed class RecoverableLootProbeState
		{
			public ThingWithComps Weapon;
			public Thing InventoryThing;
			public string WeaponThingId;
			public string InventoryThingId;
			public string WeaponDefName;
			public string InventoryDefName;
			public int InventoryCount;
			public bool WeaponStagedAsEquipment;
		}

		static bool TryStageRecoverableLootProbe(Pawn pawn, out RecoverableLootProbeState state, out string error)
		{
			state = null;
			error = null;
			if (pawn?.equipment == null)
			{
				error = "Target pawn has no equipment tracker for the recoverable loot probe.";
				return false;
			}
			if (pawn.inventory?.innerContainer == null)
			{
				error = "Target pawn has no inventory container for the recoverable loot probe.";
				return false;
			}

			var weaponDef = DefDatabase<ThingDef>.GetNamed("Gun_BoltActionRifle", false)
				?? DefDatabase<ThingDef>.GetNamed("Gun_Pistol", false);
			var weapon = weaponDef == null ? null : ThingMaker.MakeThing(weaponDef) as ThingWithComps;
			if (weapon == null)
			{
				error = "No Core ranged weapon def was available for the recoverable loot probe.";
				return false;
			}

			var inventoryThing = ThingMaker.MakeThing(ThingDefOf.Silver);
			inventoryThing.stackCount = 7;
			var weaponStagedAsEquipment = pawn.mindState != null;
			if (weaponStagedAsEquipment)
				pawn.equipment.AddEquipment(weapon);
			else if (pawn.inventory.innerContainer.TryAdd(weapon, false) == false)
			{
				weapon.Destroy(DestroyMode.Vanish);
				inventoryThing.Destroy(DestroyMode.Vanish);
				error = "Target pawn inventory rejected the staged weapon.";
				return false;
			}
			if (pawn.inventory.innerContainer.TryAdd(inventoryThing, false) == false)
			{
				weapon.Destroy(DestroyMode.Vanish);
				inventoryThing.Destroy(DestroyMode.Vanish);
				error = "Target pawn inventory rejected the staged silver stack.";
				return false;
			}

			state = new RecoverableLootProbeState
			{
				Weapon = weapon,
				InventoryThing = inventoryThing,
				WeaponThingId = weapon.ThingID,
				InventoryThingId = inventoryThing.ThingID,
				WeaponDefName = weapon.def.defName,
				InventoryDefName = inventoryThing.def.defName,
				InventoryCount = inventoryThing.stackCount,
				WeaponStagedAsEquipment = weaponStagedAsEquipment
			};
			return true;
		}

		static object VerifyRecoverableLootProbe(Map map, Zombie zombie, RecoverableLootProbeState staged)
		{
			if (staged == null)
				return new { requested = false, skipped = true };
			if (zombie == null)
				return DescribeRecoverableLootProbe(staged, "No ordinary zombie was created for the recoverable loot probe.", false);

			var inventoryThings = zombie.inventory?.innerContainer?.ToArray() ?? Array.Empty<Thing>();
			var weaponInInventory = inventoryThings.Any(thing => thing.ThingID == staged.WeaponThingId);
			var inventoryStackInZombie = inventoryThings
				.Where(thing => thing.ThingID == staged.InventoryThingId)
				.Sum(thing => thing.stackCount);
			var inventoryStackInInventory = inventoryStackInZombie >= staged.InventoryCount;
			var hasEquippedWeapon = zombie.equipment?.Primary != null;
			var killMap = zombie.Map ?? map;
			var killPosition = zombie.Position;

			var previousProgramState = Current.ProgramState;
			try
			{
				Current.ProgramState = ProgramState.Entry;
				zombie.Kill(null);
			}
			finally
			{
				Current.ProgramState = previousProgramState;
			}

			var mapThings = killMap?.listerThings?.AllThings.AsEnumerable() ?? Array.Empty<Thing>();
			var droppedWeapon = mapThings.FirstOrDefault(thing => thing.ThingID == staged.WeaponThingId);
			var droppedInventoryStacks = mapThings
				.Where(thing => thing.ThingID == staged.InventoryThingId)
				.ToArray();
			var droppedInventoryCount = droppedInventoryStacks.Sum(thing => thing.stackCount);

			return new
			{
				requested = true,
				staged = DescribeRecoverableLootProbe(staged, null, true),
				zombie = DescribeZombie(zombie),
				killPosition = ZombieRuntimeActions.DescribeCell(killPosition),
				hasEquippedWeapon,
				weaponInInventory,
				inventoryStackInZombie,
				inventoryStackInInventory,
				droppedWeapon = droppedWeapon == null ? null : DescribeThingForRecoverableLootProbe(droppedWeapon),
				droppedInventory = droppedInventoryStacks.Select(DescribeThingForRecoverableLootProbe).ToArray(),
				droppedInventoryCount,
				weaponDropped = droppedWeapon != null,
				inventoryDropped = droppedInventoryCount >= staged.InventoryCount,
				success = hasEquippedWeapon == false && weaponInInventory && inventoryStackInInventory && droppedWeapon != null && droppedInventoryCount >= staged.InventoryCount
			};
		}

		static object DescribeRecoverableLootProbe(RecoverableLootProbeState staged, string error, bool success)
		{
			if (staged == null)
				return new { requested = false, skipped = true };
			return new
			{
				requested = true,
				weaponThingId = staged.WeaponThingId,
				weaponDefName = staged.WeaponDefName,
				weaponStagedAsEquipment = staged.WeaponStagedAsEquipment,
				inventoryThingId = staged.InventoryThingId,
				inventoryDefName = staged.InventoryDefName,
				inventoryCount = staged.InventoryCount,
				success,
				error
			};
		}

		static object DescribeThingForRecoverableLootProbe(Thing thing)
		{
			return new
			{
				thingId = thing?.ThingID,
				defName = thing?.def?.defName,
				stackCount = thing?.stackCount ?? 0,
				spawned = thing?.Spawned ?? false,
				position = thing == null || thing.Spawned == false ? null : ZombieRuntimeActions.DescribeCell(thing.Position)
			};
		}

		static bool RecoverableLootProbeSucceeded(object evidence)
		{
			if (evidence == null)
				return true;
			var successProperty = evidence.GetType().GetProperty("success");
			return successProperty == null || successProperty.GetValue(evidence) is not bool success || success;
		}

		static bool TryTriggerCorpseConversion(Corpse corpse, Map map, string conversionTrigger, out object evidence, out string error)
		{
			evidence = null;
			error = null;
			if (conversionTrigger == "rotstage")
			{
				if (ZombieRuntimeActions.TriggerCorpseRotStageChanged(corpse, out var rotStageBefore, out var rotStageAfter, out error) == false)
					return false;
				evidence = new
				{
					trigger = "rotStage",
					rotStageBefore = rotStageBefore.ToString(),
					rotStageAfter = rotStageAfter.ToString()
				};
				return true;
			}
			if (conversionTrigger == "tickrare")
				return TryTriggerCorpseTickRareConversion(corpse, map, out evidence, out error);

			error = "conversionTrigger must be rotStage or tickRare.";
			return false;
		}

		static bool TryTriggerCorpseTickRareConversion(Corpse corpse, Map map, out object evidence, out string error)
		{
			evidence = null;
			error = null;
			if (corpse == null || corpse.Destroyed)
			{
				error = "Target corpse is missing or destroyed.";
				return false;
			}
			var pawn = corpse.InnerPawn;
			if (pawn?.health?.hediffSet == null)
			{
				error = "Target corpse has no inner pawn health tracker.";
				return false;
			}
			var queue = map?.GetComponent<TickManager>()?.colonistsToConvert;
			if (queue == null)
			{
				error = "The current map has no Zombieland conversion queue.";
				return false;
			}

			var infections = new List<Hediff_ZombieInfection>();
			pawn.health.hediffSet.GetHediffs(ref infections);
			if (infections.Count == 0)
			{
				error = "Target corpse inner pawn has no zombie infection hediff.";
				return false;
			}

			var ticks = GenTicks.TicksGame;
			var ticksBefore = infections.Select(infection => infection.ticksWhenBecomingZombie).ToArray();
			foreach (var infection in infections)
				infection.ticksWhenBecomingZombie = ticks - 1;
			var queueCountBefore = queue.Count;
			var queuedBefore = queue.Contains(corpse);
			corpse.TickRare();
			var queueCountAfter = queue.Count;
			var queuedAfter = queue.Contains(corpse);

			evidence = new
			{
				trigger = "tickRare",
				ticksGame = ticks,
				infectionCount = infections.Count,
				ticksWhenBecomingZombieBefore = ticksBefore,
				ticksWhenBecomingZombieAfter = infections.Select(infection => infection.ticksWhenBecomingZombie).ToArray(),
				rotStage = corpse.GetRotStage().ToString(),
				queueCountBefore,
				queueCountAfter,
				queuedBefore,
				queuedAfter
			};
			return true;
		}

		[Tool("zombieland/double_tap_infected_corpse", Description = "Run the real DoubleTap job on an infected corpse and verify the missing brain prevents corpse conversion.")]
		public static object DoubleTapInfectedCorpse()
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
			var zombieCorpses = map.listerThings.AllThings.OfType<ZombieCorpse>().ToArray();
			foreach (var zombieCorpse in zombieCorpses)
				zombieCorpse.Destroy();
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var actorCell, out var actorSpawnError) == false)
				return actorSpawnError;

			var actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(actor, actorCell, map, WipeMode.Vanish);
			DisablePawnWork(actor);
			if (TryFindAdjacentClearCell(actor, out var victimCell) == false
				&& TryFindClearSpawnCell(map, actor.Position, 8f, out victimCell, out var spawnError) == false)
				return spawnError;

			var victim = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(victim, victimCell, map, WipeMode.Vanish);
			if (ZombieRuntimeActions.AddZombieBite(victim, "final", out var bite, out var error) == false)
			{
				return new
				{
					success = false,
					victim = DescribePawn(victim),
					error
				};
			}

			if (ZombieRuntimeActions.KillPawnToCorpse(victim, out var corpse, out error) == false)
			{
				return new
				{
					success = false,
					victim = DescribePawn(victim),
					biteLabel = bite.LabelCap,
					error
				};
			}

			var oldHours = ZombieSettings.Values.hoursAfterDeathToBecomeZombie;
			ZombieSettings.Values.hoursAfterDeathToBecomeZombie = Math.Max(1, oldHours);
			try
			{
				actor.pather?.StopDead();
				actor.jobs?.EndCurrentJob(JobCondition.InterruptForced);

				var workGiver = new WorkGiver_DoubleTap();
				var hasForcedJob = workGiver.HasJobOnThing(actor, corpse, true);
				var job = workGiver.JobOnThing(actor, corpse, true);
				if (hasForcedJob == false || job == null)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						corpse = DescribeCorpse(corpse),
						hasForcedJob,
						jobDef = job?.def?.defName,
						error = "WorkGiver_DoubleTap did not create a forced DoubleTap job."
					};
				}

				var meleeDps = Math.Max(0.1f, actor.GetStatValue(StatDefOf.MeleeDPS, true));
				var maxHitWindows = (int)Math.Ceiling(100f / (meleeDps * 4f)) + 1;
				var maxTicks = 2 + maxHitWindows * 80;
				var samples = new List<object>();
				var brainBefore = corpse.InnerPawn?.health?.hediffSet?.GetBrain()?.def?.defName;
				job.playerForced = true;
				var jobDefName = job.def?.defName;
				actor.jobs.StartJob(job, JobCondition.InterruptForced, null, true, true);
				var startedJob = actor.CurJobDef?.defName;

				var tickHit = -1;
				for (var tick = 1; tick <= maxTicks; tick++)
				{
					AdvanceGameTicks(1);
					var brainMissing = corpse.InnerPawn?.health?.hediffSet?.GetBrain() == null;
					if (tick == 1 || tick == maxTicks || tick % 80 == 0 || brainMissing)
					{
						samples.Add(new
						{
							tick,
							actorJob = actor.CurJobDef?.defName,
							brainMissing,
							corpseSpawned = corpse.Spawned,
							corpseDestroyed = corpse.Destroyed
						});
					}

					if (brainMissing)
					{
						tickHit = tick;
						break;
					}
				}

				var brainMissingAfter = corpse.InnerPawn?.health?.hediffSet?.GetBrain() == null;
				var queue = map.GetComponent<TickManager>()?.colonistsToConvert;
				var queueCountBeforeRot = queue?.Count ?? -1;
				var queuedBeforeRot = queue?.Contains(corpse) ?? false;
				var rotTriggered = ZombieRuntimeActions.TriggerCorpseRotStageChanged(corpse, out var rotStageBefore, out var rotStageAfter, out error);
				var queueCountAfterRot = queue?.Count ?? -1;
				var queuedAfterRot = queue?.Contains(corpse) ?? false;

				return new
				{
					success = brainBefore != null
						&& brainMissingAfter
						&& tickHit > 0
						&& rotTriggered
						&& queuedBeforeRot == false
						&& queuedAfterRot == false,
					destroyedZombies,
					destroyedZombieCorpses = zombieCorpses.Length,
					actor = DescribePawn(actor),
					actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
					corpse = DescribeCorpse(corpse),
					victimCell = ZombieRuntimeActions.DescribeCell(victimCell),
					biteLabel = bite.LabelCap,
					restoredHoursAfterDeathToBecomeZombie = oldHours,
					hasForcedJob,
					jobDef = jobDefName,
					startedJob,
					meleeDps,
					maxHitWindows,
					maxTicks,
					tickHit,
					brainBefore,
					brainMissingAfter,
					rotTriggered,
					rotStageBefore = rotStageBefore.ToString(),
					rotStageAfter = rotStageAfter.ToString(),
					rotError = error,
					queueCountBeforeRot,
					queueCountAfterRot,
					queuedBeforeRot,
					queuedAfterRot,
					samples
				};
			}
			finally
			{
				ZombieSettings.Values.hoursAfterDeathToBecomeZombie = oldHours;
			}
		}

		[Tool("zombieland/extract_serum_from_zombie_corpse", Description = "Kill a real zombie into a ZombieCorpse, run the ExtractZombieSerum job, and verify extract is produced.")]
		public static object ExtractSerumFromZombieCorpse()
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

			var oldAmount = ZombieSettings.Values.corpsesExtractAmount;
			ZombieSettings.Values.corpsesExtractAmount = Math.Max(1f, oldAmount);
			try
			{
				_ = ZombieRuntimeActions.DestroyZombies(map);
				foreach (var existingCorpse in map.listerThings.AllThings.OfType<ZombieCorpse>().ToArray())
					existingCorpse.Destroy();

				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var actorCell, out var actorSpawnError) == false)
					return actorSpawnError;

				var actor = GenerateAreaWorkflowPawn(Faction.OfPlayer, false);
				GenSpawn.Spawn(actor, actorCell, map, WipeMode.Vanish);
				DisablePawnWork(actor);

				if (TryFindAdjacentClearCell(actor, out var zombieCell) == false
					&& TryFindClearSpawnCell(map, actor.Position, 8f, out zombieCell, out var zombieSpawnError) == false)
					return zombieSpawnError;

				var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
				if (zombie == null)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						error = "ZombieGenerator.SpawnZombie returned no zombie."
					};
				}

				zombie.Kill(null);
				var corpse = zombie.Corpse as ZombieCorpse
					?? map.listerThings.AllThings.OfType<ZombieCorpse>().OrderBy(thing => thing.Position.DistanceToSquared(zombieCell)).FirstOrDefault();
				if (corpse == null)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						zombie = DescribeZombie(zombie),
						zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
						error = "Killing the zombie did not leave a ZombieCorpse."
					};
				}

				var tickManager = map.GetComponent<TickManager>();
				if (tickManager?.allZombieCorpses?.Contains(corpse) == false)
					tickManager.allZombieCorpses.Add(corpse);

				var workGiver = new WorkGiver_ExtractZombieSerum();
				var hasForcedJob = workGiver.HasJobOnThing(actor, corpse, true);
				var job = workGiver.JobOnThing(actor, corpse, true);
				if (hasForcedJob == false || job == null)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						corpse = DescribeCorpse(corpse),
						hasForcedJob,
						jobDef = job?.def?.defName,
						error = "WorkGiver_ExtractZombieSerum did not create a forced extract job."
					};
				}

				var extractBefore = map.listerThings.AllThings.Where(thing => thing.def == CustomDefs.ZombieExtract).Sum(thing => thing.stackCount);
				var medicineBefore = actor.skills?.GetSkill(SkillDefOf.Medicine);
				var medicineLevelBefore = medicineBefore?.Level ?? -1;
				var medicineXpBefore = medicineBefore?.xpSinceLastLevel ?? 0f;
				var tendSpeed = Math.Max(0.1f, actor.GetStatValue(StatDefOf.MedicalTendSpeed, true));
				var maxTicks = 120 + (int)Math.Ceiling(100f / (tendSpeed / 2f));
				var samples = new List<object>();
				job.playerForced = true;
				var jobDefName = job.def?.defName;
				actor.jobs.StartJob(job, JobCondition.InterruptForced, null, true, true);
				var startedJob = actor.CurJobDef?.defName;
				var tickHit = -1;

				for (var tick = 1; tick <= maxTicks; tick++)
				{
					AdvanceGameTicks(1);
					var extractNow = map.listerThings.AllThings.Where(thing => thing.def == CustomDefs.ZombieExtract).Sum(thing => thing.stackCount);
					var corpseGone = corpse.Destroyed || corpse.Spawned == false;
					if (tick == 1 || tick == maxTicks || tick % 80 == 0 || corpseGone || extractNow > extractBefore)
					{
						samples.Add(new
						{
							tick,
							actorJob = actor.CurJobDef?.defName,
							corpseGone,
							extractNow
						});
					}

					if (corpseGone && extractNow > extractBefore)
					{
						tickHit = tick;
						break;
					}
				}

				var extractAfter = map.listerThings.AllThings.Where(thing => thing.def == CustomDefs.ZombieExtract).Sum(thing => thing.stackCount);
				var corpseDestroyed = corpse.Destroyed || corpse.Spawned == false;
				var medicineAfter = actor.skills?.GetSkill(SkillDefOf.Medicine);
				var medicineLevelAfter = medicineAfter?.Level ?? -1;
				var medicineXpAfter = medicineAfter?.xpSinceLastLevel ?? 0f;
				var medicineImproved = medicineLevelAfter > medicineLevelBefore
					|| (medicineLevelAfter == medicineLevelBefore && medicineXpAfter > medicineXpBefore + 0.01f);

				return new
				{
					success = corpseDestroyed && extractAfter > extractBefore && tickHit > 0 && medicineImproved,
					actor = DescribePawn(actor),
					actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
					zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
					corpse = DescribeCorpse(corpse),
					restoredCorpsesExtractAmount = oldAmount,
					hasForcedJob,
					jobDef = jobDefName,
					startedJob,
					tendSpeed,
					maxTicks,
					tickHit,
					extractBefore,
					extractAfter,
					extractDelta = extractAfter - extractBefore,
					medicine = new
					{
						levelBefore = medicineLevelBefore,
						levelAfter = medicineLevelAfter,
						xpSinceLastLevelBefore = medicineXpBefore,
						xpSinceLastLevelAfter = medicineXpAfter,
						improved = medicineImproved
					},
					expectedExtractPerZombie = Tools.ExtractPerZombie(),
					corpseDestroyed,
					trackedCorpseCount = tickManager?.allZombieCorpses?.Count ?? -1,
					samples
				};
			}
			finally
			{
				ZombieSettings.Values.corpsesExtractAmount = oldAmount;
			}
		}

		[Tool("zombieland/extract_serum_respects_allowed_area", Description = "Verify automatic zombie extract harvesting respects a doctor's assigned allowed area while manual forced harvesting still works.")]
		public static object ExtractSerumRespectsAllowedArea()
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

			var oldAmount = ZombieSettings.Values.corpsesExtractAmount;
			var oldExtractArea = ZombieSettings.Values.extractZombieArea;
			var actor = (Pawn)null;
			var corpse = (ZombieCorpse)null;
			var allowedArea = (Area_Allowed)null;
			try
			{
				ZombieSettings.Values.corpsesExtractAmount = Math.Max(1f, oldAmount);
				ZombieSettings.Values.extractZombieArea = "";

				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var actorCell, out var actorSpawnError) == false)
					return actorSpawnError;
				actor = CreateWorkflowColonist(map, actorCell, "Extract area doctor", true);
				var config = ColonistSettings.Values.ConfigFor(actor);
				if (config != null)
					config.autoExtractZombieSerum = true;

				if (TryFindClearSpawnCell(map, actor.Position + new IntVec3(6, 0, 0), 8f, out var zombieCell, out var zombieSpawnError) == false)
					return zombieSpawnError;
				var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
				if (zombie == null)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
						error = "ZombieGenerator.SpawnZombie returned no zombie."
					};
				}

				zombie.Kill(null);
				corpse = zombie.Corpse as ZombieCorpse
					?? map.listerThings.AllThings.OfType<ZombieCorpse>().OrderBy(thing => thing.Position.DistanceToSquared(zombieCell)).FirstOrDefault();
				if (corpse == null)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
						error = "Killing the zombie did not leave a ZombieCorpse."
					};
				}

				if (map.areaManager.TryMakeNewAllowed(out allowedArea) == false)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						corpse = DescribeCorpse(corpse),
						error = "Could not create a temporary allowed area."
					};
				}
				allowedArea.labelInt = "ZL extract area gate";
				foreach (var cell in allowedArea.ActiveCells.ToArray())
					allowedArea[cell] = false;
				allowedArea[actor.Position] = true;
				actor.playerSettings.AreaRestrictionInPawnCurrentMap = allowedArea;

				var tickManager = map.GetComponent<TickManager>();
				if (tickManager?.allZombieCorpses?.Contains(corpse) == false)
					tickManager.allZombieCorpses.Add(corpse);

				var workGiver = new WorkGiver_ExtractZombieSerum();
				var candidates = workGiver.PotentialWorkThingsGlobal(actor).ToArray();
				var listedOutsideAreaCorpse = candidates.Contains(corpse);
				var hasUnforcedJob = workGiver.HasJobOnThing(actor, corpse, false);
				var unforcedJob = workGiver.JobOnThing(actor, corpse, false);
				var hasForcedJob = workGiver.HasJobOnThing(actor, corpse, true);
				var forcedJob = hasForcedJob ? workGiver.JobOnThing(actor, corpse, true) : null;

				return new
				{
					success = listedOutsideAreaCorpse == false
						&& hasUnforcedJob == false
						&& unforcedJob == null
						&& hasForcedJob
						&& forcedJob?.def == CustomDefs.ExtractZombieSerum,
					actor = DescribePawn(actor),
					corpse = DescribeCorpse(corpse),
					allowedArea = allowedArea.Label,
					actorCell = ZombieRuntimeActions.DescribeCell(actor.Position),
					corpseCell = ZombieRuntimeActions.DescribeCell(corpse.Position),
					actorCellAllowed = allowedArea[actor.Position],
					corpseCellAllowed = allowedArea[corpse.Position],
					candidateCount = candidates.Length,
					listedOutsideAreaCorpse,
					hasUnforcedJob,
					unforcedJobDef = unforcedJob?.def?.defName,
					hasForcedJob,
					forcedJobDef = forcedJob?.def?.defName,
					restoredCorpsesExtractAmount = oldAmount,
					restoredExtractArea = oldExtractArea
				};
			}
			finally
			{
				ZombieSettings.Values.corpsesExtractAmount = oldAmount;
				ZombieSettings.Values.extractZombieArea = oldExtractArea;
				if (actor != null && actor.Destroyed == false)
				{
					actor.jobs?.EndCurrentJob(JobCondition.InterruptForced);
					actor.Destroy(DestroyMode.Vanish);
				}
				if (corpse != null && corpse.Destroyed == false)
					corpse.Destroy(DestroyMode.Vanish);
				if (allowedArea != null && map.areaManager.AllAreas.Contains(allowedArea))
					map.areaManager.Remove(allowedArea);
			}
		}

		[Tool("zombieland/zombie_extract_filter_visibility", Description = "Verify the broad zombie ThingFilter patch still allows zombie extract and serum defs while blocking actual zombie defs.")]
		public static object ZombieExtractFilterVisibility()
		{
			var serumDef = DefDatabase<ThingDef>.GetNamed("ZombieSerumSimple", false);
			var labSerumDef = DefDatabase<ThingDef>.GetNamed("Zombie100Serum", false);
			var simpleSerumRecipe = DefDatabase<RecipeDef>.GetNamed("MakeZombieSerum", false);
			if (serumDef == null || labSerumDef == null || simpleSerumRecipe == null)
			{
				return new
				{
					success = false,
					error = "ZombieSerumSimple, Zombie100Serum, or MakeZombieSerum def was not loaded."
				};
			}
			var playerRecipePrerequisiteTags = Faction.OfPlayer?.def?.recipePrerequisiteTags ?? new List<string>();
			var recipeFactionPrerequisites = simpleSerumRecipe.factionPrerequisiteTags ?? new List<string>();
			var playerMeetsRecipeFactionPrerequisites = recipeFactionPrerequisites.All(playerRecipePrerequisiteTags.Contains);
			var simpleSerumRecipeAvailabilityMatchesFaction = simpleSerumRecipe.AvailableNow == playerMeetsRecipeFactionPrerequisites;
			var makeshiftSerumShouldBeVisible = simpleSerumRecipe.AvailableNow;
			var extractCategoryDefNames = CustomDefs.ZombieExtract.thingCategories.Select(category => category.defName).ToArray();
			var extractCategorizedAsRawResourceOnly = extractCategoryDefNames.Contains("ResourcesRaw")
				&& extractCategoryDefNames.Contains("ZombieSerum") == false;
			var simpleSerumRecipeAllowsExtract = simpleSerumRecipe.fixedIngredientFilter.Allows(CustomDefs.ZombieExtract);

			var filter = new ThingFilter();
			filter.SetAllow(CustomDefs.ZombieExtract, true);
			filter.SetAllow(serumDef, true);
			filter.SetAllow(labSerumDef, true);
			filter.SetAllow(CustomDefs.Corpse_Zombie, true);
			filter.SetAllow(CustomDefs.Zombie, true);
			var allowedDefs = filter.AllowedThingDefs.ToHashSet();
			var extractAllowed = allowedDefs.Contains(CustomDefs.ZombieExtract);
			var serumAllowed = allowedDefs.Contains(serumDef);
			var labSerumAllowed = allowedDefs.Contains(labSerumDef);
			var zombieCorpseAllowed = allowedDefs.Contains(CustomDefs.Corpse_Zombie);
			var zombiePawnAllowed = allowedDefs.Contains(CustomDefs.Zombie);
			var serumLabelsDistinct = string.Equals(serumDef.label, labSerumDef.label, StringComparison.OrdinalIgnoreCase) == false;
			var makeshiftSerumPurity = Tools.ZombieSerumPurity(serumDef);
			var labSerumPurity = Tools.ZombieSerumPurity(labSerumDef);
			var simpleSerumSurgeryFactor = serumDef.GetStatValueAbstract(StatDefOf.SurgerySuccessChanceFactor);
			var labSerumSurgeryFactor = labSerumDef.GetStatValueAbstract(StatDefOf.SurgerySuccessChanceFactor);
			var serumsMedicallyEquivalent = makeshiftSerumPurity == 100
				&& labSerumPurity == 100
				&& Math.Abs(simpleSerumSurgeryFactor - 1f) < 0.0001f
				&& Math.Abs(labSerumSurgeryFactor - 1f) < 0.0001f;
			var expectedSpitterSerumDef = Tools.SpitterSerumDefForPlayer();
			var spitterSerumMatchesAvailability = expectedSpitterSerumDef == (makeshiftSerumShouldBeVisible ? serumDef : labSerumDef);
			var attemptedMakeshiftThing = ThingMaker.MakeThing(serumDef);
			var expectedCreatedSerumDef = makeshiftSerumShouldBeVisible ? serumDef : labSerumDef;
			var makeshiftCreationResolvesForFaction = attemptedMakeshiftThing?.def == expectedCreatedSerumDef;
			var makeshiftExcludedFromGenericAcquisition = serumDef.tradeability == Tradeability.None
				&& serumDef.generateCommonality <= 0f
				&& serumDef.generateAllowChance <= 0f
				&& serumDef.scatterableOnMapGen == false
				&& serumDef.forceDebugSpawnable == false;

			var extractThing = ThingMaker.MakeThing(CustomDefs.ZombieExtract);
			var serumFilterWorker = new ZombieSerumFilterWorker();
			var extractExcludedBySerumFilter = serumFilterWorker.Matches(extractThing);
			if (TryProbeTreeThingFilterVisibility(serumDef, labSerumDef, makeshiftSerumShouldBeVisible, out var treeVisibilitySuccess, out var treeVisibility, out var treeVisibilityError) == false)
			{
				return new
				{
					success = false,
					extract = new
					{
						defName = CustomDefs.ZombieExtract.defName,
						allowed = extractAllowed,
						excludedBySerumFilter = extractExcludedBySerumFilter
					},
					serum = new
					{
						defName = serumDef.defName,
						label = serumDef.label,
						allowed = serumAllowed
					},
					labSerum = new
					{
						defName = labSerumDef.defName,
						label = labSerumDef.label,
						allowed = labSerumAllowed
					},
					serumLabelsDistinct,
					serumFunction = new
					{
						medicallyEquivalent = serumsMedicallyEquivalent,
						makeshiftPurity = makeshiftSerumPurity,
						labPurity = labSerumPurity,
						makeshiftSurgerySuccessFactor = simpleSerumSurgeryFactor,
						labSurgerySuccessFactor = labSerumSurgeryFactor,
						expectedSpitterSerumDef = expectedSpitterSerumDef?.defName,
						spitterSerumMatchesAvailability,
						attemptedMakeshiftResolvedTo = attemptedMakeshiftThing?.def?.defName,
						makeshiftCreationResolvesForFaction,
						makeshiftExcludedFromGenericAcquisition,
						tradeability = serumDef.tradeability.ToString(),
						serumDef.generateCommonality,
						serumDef.generateAllowChance,
						serumDef.scatterableOnMapGen,
						serumDef.forceDebugSpawnable
					},
					simpleSerumRecipe = new
					{
						defName = simpleSerumRecipe.defName,
						availableNow = simpleSerumRecipe.AvailableNow,
						factionPrerequisiteTags = recipeFactionPrerequisites,
						playerFactionDef = Faction.OfPlayer?.def?.defName,
						playerRecipePrerequisiteTags,
						playerMeetsRecipeFactionPrerequisites,
						availabilityMatchesFaction = simpleSerumRecipeAvailabilityMatchesFaction
					},
					extractCategories = new
					{
						defNames = extractCategoryDefNames,
						rawResourceOnly = extractCategorizedAsRawResourceOnly,
						simpleSerumRecipeAllowsExtract
					},
					blockedZombieDefs = new
					{
						corpse = new
						{
							defName = CustomDefs.Corpse_Zombie.defName,
							allowed = zombieCorpseAllowed
						},
						pawn = new
						{
							defName = CustomDefs.Zombie.defName,
							allowed = zombiePawnAllowed
						}
					},
					error = treeVisibilityError
				};
			}

			return new
			{
				success = extractAllowed
					&& serumAllowed
					&& labSerumAllowed
					&& serumLabelsDistinct
					&& serumsMedicallyEquivalent
					&& spitterSerumMatchesAvailability
					&& makeshiftCreationResolvesForFaction
					&& makeshiftExcludedFromGenericAcquisition
					&& simpleSerumRecipeAvailabilityMatchesFaction
					&& extractCategorizedAsRawResourceOnly
					&& simpleSerumRecipeAllowsExtract
					&& zombieCorpseAllowed == false
					&& zombiePawnAllowed == false
					&& extractExcludedBySerumFilter == false
					&& treeVisibilitySuccess,
				extract = new
				{
					defName = CustomDefs.ZombieExtract.defName,
					allowed = extractAllowed,
					excludedBySerumFilter = extractExcludedBySerumFilter
				},
				serum = new
				{
					defName = serumDef.defName,
					label = serumDef.label,
					allowed = serumAllowed
				},
				labSerum = new
				{
					defName = labSerumDef.defName,
					label = labSerumDef.label,
					allowed = labSerumAllowed
				},
				serumLabelsDistinct,
				serumFunction = new
				{
					medicallyEquivalent = serumsMedicallyEquivalent,
					makeshiftPurity = makeshiftSerumPurity,
					labPurity = labSerumPurity,
					makeshiftSurgerySuccessFactor = simpleSerumSurgeryFactor,
					labSurgerySuccessFactor = labSerumSurgeryFactor,
					expectedSpitterSerumDef = expectedSpitterSerumDef?.defName,
					spitterSerumMatchesAvailability,
					attemptedMakeshiftResolvedTo = attemptedMakeshiftThing?.def?.defName,
					makeshiftCreationResolvesForFaction,
					makeshiftExcludedFromGenericAcquisition,
					tradeability = serumDef.tradeability.ToString(),
					serumDef.generateCommonality,
					serumDef.generateAllowChance,
					serumDef.scatterableOnMapGen,
					serumDef.forceDebugSpawnable
				},
				simpleSerumRecipe = new
				{
					defName = simpleSerumRecipe.defName,
					availableNow = simpleSerumRecipe.AvailableNow,
					factionPrerequisiteTags = recipeFactionPrerequisites,
					playerFactionDef = Faction.OfPlayer?.def?.defName,
					playerRecipePrerequisiteTags,
					playerMeetsRecipeFactionPrerequisites,
					availabilityMatchesFaction = simpleSerumRecipeAvailabilityMatchesFaction
				},
				extractCategories = new
				{
					defNames = extractCategoryDefNames,
					rawResourceOnly = extractCategorizedAsRawResourceOnly,
					simpleSerumRecipeAllowsExtract
				},
				blockedZombieDefs = new
				{
					corpse = new
					{
						defName = CustomDefs.Corpse_Zombie.defName,
						allowed = zombieCorpseAllowed
					},
					pawn = new
					{
						defName = CustomDefs.Zombie.defName,
						allowed = zombiePawnAllowed
					}
				},
				treeVisibility
			};
		}

		static readonly MethodInfo listingTreeThingFilterVisibleMethod = typeof(Listing_TreeThingFilter).GetMethod(
			"Visible",
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
			null,
			new[] { typeof(ThingDef) },
			null);

		static bool TryProbeTreeThingFilterVisibility(ThingDef serumDef, ThingDef labSerumDef, bool makeshiftSerumShouldBeVisible, out bool success, out object evidence, out string error)
		{
			success = false;
			evidence = null;
			error = null;
			if (listingTreeThingFilterVisibleMethod == null)
			{
				error = "Could not resolve Listing_TreeThingFilter.Visible(ThingDef).";
				return false;
			}

			var listing = new Listing_TreeThingFilter(
				new ThingFilter(),
				null,
				null,
				null,
				null,
				new QuickSearchFilter());
			var extractVisible = (bool)listingTreeThingFilterVisibleMethod.Invoke(listing, new object[] { CustomDefs.ZombieExtract });
			var serumVisible = (bool)listingTreeThingFilterVisibleMethod.Invoke(listing, new object[] { serumDef });
			var labSerumVisible = (bool)listingTreeThingFilterVisibleMethod.Invoke(listing, new object[] { labSerumDef });
			var zombieCorpseVisible = (bool)listingTreeThingFilterVisibleMethod.Invoke(listing, new object[] { CustomDefs.Corpse_Zombie });
			var zombiePawnVisible = (bool)listingTreeThingFilterVisibleMethod.Invoke(listing, new object[] { CustomDefs.Zombie });
			success = extractVisible
				&& serumVisible == makeshiftSerumShouldBeVisible
				&& labSerumVisible
				&& zombieCorpseVisible == false
				&& zombiePawnVisible == false;
			evidence = new
			{
				success,
				extractVisible,
				serumVisible,
				makeshiftSerumShouldBeVisible,
				labSerumVisible,
				zombieCorpseVisible,
				zombiePawnVisible
			};
			return true;
		}

		[Tool("zombieland/rope_zombie_job", Description = "Run the real RopeZombie job from a colonist to a live zombie and verify the zombie becomes roped.")]
		public static object RopeZombieJob()
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

			if (TryFindAdjacentClearCell(actor, out var zombieCell) == false
				&& TryFindClearSpawnCell(map, actor.Position, 8f, out zombieCell, out var zombieSpawnError) == false)
				return zombieSpawnError;

			var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
			if (zombie == null)
			{
				return new
				{
					success = false,
					actor = DescribePawn(actor),
					error = "ZombieGenerator.SpawnZombie returned no zombie."
				};
			}

			var job = JobMaker.MakeJob(CustomDefs.RopeZombie, zombie);
			job.playerForced = true;
			var canReserveAndReach = actor.CanReach(zombie, PathEndMode.Touch, Danger.Deadly)
				&& zombie.ropedBy == null;
			actor.drafter.Drafted = true;
			_ = actor.jobs.TryTakeOrderedJob(job, new JobTag?(JobTag.Misc), false);
			var startedJob = actor.CurJobDef?.defName;
			var maxTicks = 180;
			var tickHit = -1;
			var samples = new List<object>();

			for (var tick = 1; tick <= maxTicks; tick++)
			{
				AdvanceGameTicks(1);
				var roped = ReferenceEquals(zombie.ropedBy, actor);
				if (tick == 1 || tick == maxTicks || tick % 30 == 0 || roped)
				{
					samples.Add(new
					{
						tick,
						actorJob = actor.CurJobDef?.defName,
						zombieRopedBy = zombie.ropedBy?.ThingID,
						zombie.IsRopedOrConfused
					});
				}

				if (roped)
				{
					tickHit = tick;
					break;
				}
			}

			return new
			{
				success = canReserveAndReach && tickHit > 0 && ReferenceEquals(zombie.ropedBy, actor) && zombie.IsRopedOrConfused,
				destroyedZombies,
				actor = DescribePawn(actor),
				zombie = DescribeZombie(zombie),
				actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
				zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
				canReserveAndReach,
				startedJob,
				maxTicks,
				tickHit,
				ropedBy = zombie.ropedBy?.ThingID,
				isRopedOrConfused = zombie.IsRopedOrConfused,
				samples
			};
		}

		[Tool("zombieland/flee_ignores_harmless_zombies", Description = "Call RimWorld FleeUtility.ShouldFleeFrom for real zombies and verify roped/confused/electrical/albino zombies are not flee threats.")]
		public static object FleeIgnoresHarmlessZombies()
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
			GenSpawn.Spawn(actor, actorCell, map, Rot4.South);

			var zombieCells = GenRadial.RadialCellsAround(actorCell, 7f, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.DistanceTo(actorCell) <= 7.5f)
				.Where(cell => cell != actorCell)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.Take(5)
				.ToArray();
			if (zombieCells.Length < 5)
			{
				return new
				{
					success = false,
					actor = DescribePawn(actor),
					error = "Could not find enough nearby cells for flee-threat zombies."
				};
			}

			var normal = ZombieRuntimeActions.SpawnZombie(zombieCells[0], map, ZombieType.Normal, true);
			var roped = ZombieRuntimeActions.SpawnZombie(zombieCells[1], map, ZombieType.Normal, true);
			var confused = ZombieRuntimeActions.SpawnZombie(zombieCells[2], map, ZombieType.Normal, true);
			var electrifier = ZombieRuntimeActions.SpawnZombie(zombieCells[3], map, ZombieType.Electrifier, true);
			var albino = ZombieRuntimeActions.SpawnZombie(zombieCells[4], map, ZombieType.Albino, true);

			if (normal == null || roped == null || confused == null || electrifier == null || albino == null)
			{
				return new
				{
					success = false,
					actor = DescribePawn(actor),
					error = "ZombieGenerator.SpawnZombie returned no zombie for one or more flee-threat cases."
				};
			}

			roped.ropedBy = actor;
			confused.paralyzedUntil = GenTicks.TicksAbs + 2500;
			electrifier.electricDisabledUntil = GenTicks.TicksGame - 1;

			var normalThreat = FleeUtility.ShouldFleeFrom(normal, actor, true, false);
			var ropedThreat = FleeUtility.ShouldFleeFrom(roped, actor, true, false);
			var confusedThreat = FleeUtility.ShouldFleeFrom(confused, actor, true, false);
			var electrifierThreat = FleeUtility.ShouldFleeFrom(electrifier, actor, true, false);
			var albinoThreat = FleeUtility.ShouldFleeFrom(albino, actor, true, false);

			return new
			{
				success = normalThreat
					&& ropedThreat == false
					&& confusedThreat == false
					&& electrifierThreat == false
					&& albinoThreat == false,
				destroyedZombies,
				actor = DescribePawn(actor),
				normal = DescribeZombie(normal),
				roped = DescribeZombie(roped),
				confused = DescribeZombie(confused),
				electrifier = DescribeZombie(electrifier),
				albino = DescribeZombie(albino),
				threats = new
				{
					normal = normalThreat,
					roped = ropedThreat,
					confused = confusedThreat,
					electrifier = electrifierThreat,
					albino = albinoThreat
				},
				seesAsThreat = new
				{
					normal = actor.SeesZombieAsThreat(normal),
					roped = actor.SeesZombieAsThreat(roped),
					confused = actor.SeesZombieAsThreat(confused),
					electrifier = actor.SeesZombieAsThreat(electrifier),
					albino = actor.SeesZombieAsThreat(albino)
				}
			};
		}

		[Tool("zombieland/colonist_avoidance_interrupts_job", Description = "Build a real avoid grid around a zombie and verify a non-forced colonist job is interrupted into a Flee job.")]
		public static object ColonistAvoidanceInterruptsJob()
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

			var oldBetterAvoidance = ZombieSettings.Values.betterZombieAvoidance;
			ZombieSettings.Values.betterZombieAvoidance = true;
			try
			{
				var destroyedZombies = ZombieRuntimeActions.DestroyZombies(map);
				map.GetComponent<TickManager>().avoidGrid = new AvoidGrid(map);
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var actorCell, out var actorSpawnError) == false)
					return actorSpawnError;

				var actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(actor, actorCell, map, Rot4.South);
				DisablePawnWork(actor);
				var config = ColonistSettings.Values.ConfigFor(actor);
				if (config != null)
					config.autoAvoidZombies = true;

				var zombieCell = GenRadial.RadialCellsAround(actorCell, 3f, false)
					.Where(cell => cell.InBounds(map))
					.Where(cell => cell.Standable(map))
					.Where(cell => cell.Fogged(map) == false)
					.Where(cell => cell.GetFirstPawn(map) == null)
					.OrderBy(cell => cell.DistanceToSquared(actorCell))
					.FirstOrDefault();
				if (zombieCell.IsValid == false)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						error = "No nearby clear zombie cell was found."
					};
				}

				var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
				if (zombie == null)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						error = "ZombieGenerator.SpawnZombie returned no zombie."
					};
				}

				zombie.state = ZombieState.Tracking;
				var avoidGrid = BuildAvoidGridForZombie(map, zombie);
				var actorAvoidCost = AvoidCost(avoidGrid, map, actor.Position);
				var inAvoidDangerBefore = avoidGrid.InAvoidDanger(actor);
				var safeCells = GenRadial.RadialCellsAround(actor.Position, 8f, true)
					.Where(cell => cell.InBounds(map))
					.Where(cell => cell.Standable(map))
					.Where(cell => cell.Fogged(map) == false)
					.Where(cell => avoidGrid.ShouldAvoid(map, cell) == false)
					.Take(8)
					.Select(ZombieRuntimeActions.DescribeCell)
					.ToArray();

				var waitJob = JobMaker.MakeJob(JobDefOf.Wait);
				waitJob.playerForced = false;
				actor.jobs.StartJob(waitJob, JobCondition.InterruptForced, null, false, true);
				var startedJob = actor.CurJobDef?.defName;
				var samples = new List<object>();
				var tickHit = -1;
				const int maxTicks = 30;

				for (var tick = 1; tick <= maxTicks; tick++)
				{
					AdvanceGameTicks(1);
					var currentJob = actor.CurJob;
					if (tick == 1 || tick == maxTicks || currentJob?.def == JobDefOf.Flee)
					{
						samples.Add(new
						{
							tick,
							job = actor.CurJobDef?.defName,
							currentJob?.playerForced,
							target = currentJob?.targetA.Cell.IsValid == true ? ZombieRuntimeActions.DescribeCell(currentJob.targetA.Cell) : null
						});
					}

					if (currentJob?.def == JobDefOf.Flee)
					{
						tickHit = tick;
						break;
					}
				}

				var fleeJob = actor.CurJob;
				var fleeDestination = fleeJob?.targetA.Cell ?? IntVec3.Invalid;
				var fleeDestinationAvoids = fleeDestination.IsValid && avoidGrid.ShouldAvoid(map, fleeDestination) == false;

				return new
				{
					success = inAvoidDangerBefore
						&& startedJob == JobDefOf.Wait.defName
						&& tickHit > 0
						&& fleeJob?.playerForced == true
						&& fleeDestinationAvoids,
					destroyedZombies,
					actor = DescribePawn(actor),
					zombie = DescribeZombie(zombie),
					actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
					zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
					startedJob,
					inAvoidDangerBefore,
					actorAvoidCost,
					safeCells,
					tickHit,
					maxTicks,
					fleeDestination = fleeDestination.IsValid ? ZombieRuntimeActions.DescribeCell(fleeDestination) : null,
					fleeDestinationAvoids,
					finalJob = actor.CurJobDef?.defName,
					finalJobPlayerForced = actor.CurJob?.playerForced,
					samples
				};
			}
			finally
			{
				ZombieSettings.Values.betterZombieAvoidance = oldBetterAvoidance;
			}
		}

		[Tool("zombieland/avoid_grid_death_refresh_contract", Description = "Verify killed, downed, roped, confused, paralysis-expired, consciousness-reset, recovered, electric-state, movement-state, first-sample electric expiry, and rage-state zombies request prompt avoid-grid refreshes, including non-empty overlap rebuilds and stale-result rejection.")]
		public static object AvoidGridDeathRefreshContract(
			[ToolParameter(Description = "Destroy staged colonists, zombies, and corpses at the end.", Required = false, DefaultValue = true)] bool cleanup = true)
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

			var tickManager = map.GetComponent<TickManager>();
			if (tickManager == null)
			{
				return new
				{
					success = false,
					error = "Current map has no Zombieland TickManager."
				};
			}

			var oldBetterAvoidance = ZombieSettings.Values.betterZombieAvoidance;
			var oldDoubleTapRequired = ZombieSettings.Values.doubleTapRequired;
			var spawned = new List<Thing>();
			ZombieSettings.Values.betterZombieAvoidance = true;
			ZombieSettings.Values.doubleTapRequired = true;
			try
			{
				var destroyedZombies = ZombieRuntimeActions.DestroyZombies(map);
				if (TryFindAvoidGridDeathRefreshFixtureRoot(map, out var root, out var rootError) == false)
					return rootError;
				tickManager.avoidGrid = new AvoidGrid(map);

				var killed = VerifyAvoidGridClearsAfterZombieTransition(map, root + new IntVec3(-10, 0, 0), "killed", withSurvivor: false, spawned);
				var downed = VerifyAvoidGridClearsAfterZombieTransition(map, root, "downed", withSurvivor: false, spawned);
				var overlap = VerifyAvoidGridClearsAfterZombieTransition(map, root + new IntVec3(10, 0, 0), "killed", withSurvivor: true, spawned);
				var roped = VerifyAvoidGridClearsAfterZombieTransition(map, root + new IntVec3(0, 0, 10), "roped", withSurvivor: false, spawned);
				var confused = VerifyAvoidGridClearsAfterZombieTransition(map, root + new IntVec3(-10, 0, 10), "confused", withSurvivor: false, spawned);
				var undowned = VerifyAvoidGridAddsAfterZombieRecovery(map, root + new IntVec3(10, 0, 10), spawned);
				var paralysisExpiredJobDriver = VerifyAvoidGridAddsAfterParalysisExpiry(map, root + new IntVec3(-10, 0, -10), "jobDriver", spawned);
				var paralysisExpiredStateTick = VerifyAvoidGridAddsAfterParalysisExpiry(map, root + new IntVec3(10, 0, -10), "stateTick", spawned);
				var consciousnessReset = VerifyAvoidGridAddsAfterConsciousnessReset(map, root + new IntVec3(0, 0, -16), spawned);
				var firstElectricSampleExpiry = VerifyAvoidGridRefreshesAfterFirstElectricSampleExpiry(map, root + new IntVec3(16, 0, 28), spawned);
				var specTransitions = new[]
				{
						VerifyAvoidGridRefreshesAfterSpecTransition(map, root + new IntVec3(-16, 0, 16), "electricDisabled", spawned),
						VerifyAvoidGridRefreshesAfterSpecTransition(map, root + new IntVec3(0, 0, 16), "electricWaterEnter", spawned),
						VerifyAvoidGridRefreshesAfterSpecTransition(map, root + new IntVec3(16, 0, 16), "electricWaterLeave", spawned),
						VerifyAvoidGridRefreshesAfterSpecTransition(map, root + new IntVec3(-16, 0, 28), "rageStart", spawned),
						VerifyAvoidGridRefreshesAfterSpecTransition(map, root + new IntVec3(0, 0, 28), "rageEnd", spawned),
						VerifyAvoidGridRefreshesAfterSpecTransition(map, root + new IntVec3(-16, 0, -28), "wanderingToTracking", spawned),
						VerifyAvoidGridRefreshesAfterSpecTransition(map, root + new IntVec3(16, 0, -28), "trackingToWandering", spawned)
					};

				return new
				{
					success = ObjectSuccess(killed)
						&& ObjectSuccess(downed)
						&& ObjectSuccess(overlap)
						&& ObjectSuccess(roped)
						&& ObjectSuccess(confused)
						&& ObjectSuccess(undowned)
						&& ObjectSuccess(paralysisExpiredJobDriver)
						&& ObjectSuccess(paralysisExpiredStateTick)
						&& ObjectSuccess(consciousnessReset)
						&& ObjectSuccess(firstElectricSampleExpiry)
						&& specTransitions.All(ObjectSuccess),
					cleanup,
					destroyedZombies,
					fixtureRoot = ZombieRuntimeActions.DescribeCell(root),
					killed,
					downed,
					overlap,
					roped,
					confused,
					undowned,
					paralysisExpiredJobDriver,
					paralysisExpiredStateTick,
					consciousnessReset,
					firstElectricSampleExpiry,
					specTransitions
				};
			}
			finally
			{
				ZombieSettings.Values.betterZombieAvoidance = oldBetterAvoidance;
				ZombieSettings.Values.doubleTapRequired = oldDoubleTapRequired;
				if (cleanup)
				{
					_ = CleanupAvoidGridDeathRefreshFixtures(spawned);
					var cleanupGrid = BuildAvoidGridForZombies(map, tickManager.AllZombies());
					tickManager.lastAvoidGridRequestId = cleanupGrid.requestId;
					tickManager.lastAvoidGridResultId = cleanupGrid.requestId;
					tickManager.lastAvoidGridRequestTick = GenTicks.TicksGame;
					tickManager.lastAvoidGridResultTick = GenTicks.TicksGame;
				}
			}
		}

		static bool TryFindAvoidGridDeathRefreshFixtureRoot(Map map, out IntVec3 root, out object error)
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

			var fixtureOffsets = new[]
			{
						new IntVec3(-10, 0, 0),
						IntVec3.Zero,
						new IntVec3(10, 0, 0),
						new IntVec3(0, 0, 10),
						new IntVec3(-10, 0, 10),
						new IntVec3(10, 0, 10),
						new IntVec3(-10, 0, -10),
						new IntVec3(10, 0, -10),
						new IntVec3(0, 0, -16),
						new IntVec3(-16, 0, 16),
						new IntVec3(0, 0, 16),
						new IntVec3(16, 0, 16),
						new IntVec3(-16, 0, 28),
						new IntVec3(0, 0, 28),
						new IntVec3(16, 0, 28),
						new IntVec3(-16, 0, -28),
						new IntVec3(16, 0, -28)
					};
			var existingColonists = map.mapPawns?.FreeColonistsSpawned?
				.Where(pawn => pawn != null && pawn.Dead == false && pawn.Spawned)
				.ToArray() ?? Array.Empty<Pawn>();
			var candidates = AvoidGridDeathRefreshRootCandidates(map).ToArray();
			foreach (var candidate in candidates)
			{
				if (candidate.InBounds(map) == false)
					continue;
				var clearCells = new List<IntVec3>();
				var allFixturesReachable = true;
				foreach (var offset in fixtureOffsets)
				{
					var fixtureRoot = candidate + offset;
					if (fixtureRoot.InBounds(map) == false || TryFindClearSpawnCell(map, fixtureRoot, 16f, out var clearCell, out _) == false)
					{
						allFixturesReachable = false;
						break;
					}
					clearCells.Add(clearCell);
				}
				if (allFixturesReachable == false)
					continue;
				if (existingColonists.Any(colonist => clearCells.Any(cell => colonist.Position.DistanceTo(cell) < 32f)))
					continue;
				root = candidate;
				return true;
			}

			error = new
			{
				success = false,
				error = "No non-invasive avoid-grid fixture area was found far enough from existing free colonists.",
				existingColonists = existingColonists.Select(DescribePawn).Take(12).ToArray(),
				candidateCount = candidates.Length
			};
			return false;
		}

		static IEnumerable<IntVec3> AvoidGridDeathRefreshRootCandidates(Map map)
		{
			var center = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			yield return center;
			yield return new IntVec3(map.Size.x / 4, 0, map.Size.z / 4);
			yield return new IntVec3(map.Size.x * 3 / 4, 0, map.Size.z / 4);
			yield return new IntVec3(map.Size.x / 4, 0, map.Size.z * 3 / 4);
			yield return new IntVec3(map.Size.x * 3 / 4, 0, map.Size.z * 3 / 4);

			var step = Math.Max(20, map.Size.x / 8);
			for (var x = step; x < map.Size.x - step; x += step)
			{
				for (var z = step; z < map.Size.z - step; z += step)
					yield return new IntVec3(x, 0, z);
			}
		}

		static object VerifyAvoidGridClearsAfterZombieTransition(Map map, IntVec3 root, string transition, bool withSurvivor, List<Thing> spawned)
		{
			var tickManager = map.GetComponent<TickManager>();
			if (TryFindClearSpawnCell(map, root, 16f, out var actorCell, out var actorSpawnError) == false)
				return actorSpawnError;

			var actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(actor, actorCell, map, Rot4.South);
			DisablePawnWork(actor);
			actor.drafter.Drafted = true;
			var waitJob = JobMaker.MakeJob(JobDefOf.Wait);
			waitJob.playerForced = true;
			actor.jobs.StartJob(waitJob, JobCondition.InterruptForced, null, false, true);
			spawned.Add(actor);
			var config = ColonistSettings.Values.ConfigFor(actor);
			if (config != null)
				config.autoAvoidZombies = true;

			var zombieCell = GenRadial.RadialCellsAround(actorCell, 8f, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.Where(cell => cell.DistanceTo(actorCell) >= 4f)
				.OrderBy(cell => cell.DistanceToSquared(actorCell))
				.FirstOrDefault();
			if (zombieCell.IsValid == false)
			{
				return new
				{
					success = false,
					transition,
					withSurvivor,
					actor = DescribePawn(actor),
					actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
					error = "No zombie cell was found for the avoid-grid death refresh fixture."
				};
			}

			var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
			if (zombie == null)
			{
				return new
				{
					success = false,
					transition,
					withSurvivor,
					actor = DescribePawn(actor),
					zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
					error = "ZombieGenerator.SpawnZombie returned no zombie."
				};
			}

			zombie.state = ZombieState.Tracking;
			spawned.Add(zombie);
			Zombie survivor = null;
			if (withSurvivor)
			{
				var radius = Tools.ZombieAvoidRadius(zombie);
				var survivorCell = GenRadial.RadialCellsAround(zombieCell, radius * 1.25f, false)
					.Where(cell => cell.InBounds(map))
					.Where(cell => cell.Standable(map))
					.Where(cell => cell.Fogged(map) == false)
					.Where(cell => cell.GetFirstPawn(map) == null)
					.Where(cell => cell.DistanceTo(zombieCell) >= radius * 0.7f)
					.OrderBy(cell => Math.Abs(cell.DistanceTo(zombieCell) - radius))
					.FirstOrDefault();
				if (survivorCell.IsValid == false)
				{
					return new
					{
						success = false,
						transition,
						withSurvivor,
						actor = DescribePawn(actor),
						zombie = DescribeZombie(zombie),
						zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
						error = "No survivor zombie cell was found for the overlap fixture."
					};
				}
				survivor = ZombieRuntimeActions.SpawnZombie(survivorCell, map, ZombieType.Normal, true);
				if (survivor == null)
				{
					return new
					{
						success = false,
						transition,
						withSurvivor,
						actor = DescribePawn(actor),
						zombie = DescribeZombie(zombie),
						survivorCell = ZombieRuntimeActions.DescribeCell(survivorCell),
						error = "ZombieGenerator.SpawnZombie returned no survivor zombie."
					};
				}
				survivor.state = ZombieState.Tracking;
				spawned.Add(survivor);
			}

			tickManager.allZombiesCached = tickManager.AllZombies().ToHashSet();
			var zombies = survivor == null ? new[] { zombie } : new[] { zombie, survivor };
			var realGrid = BuildAvoidGridForZombies(map, zombies);
			tickManager.lastAvoidGridRequestId = realGrid.requestId;
			tickManager.lastAvoidGridResultId = realGrid.requestId;
			tickManager.lastAvoidGridRequestTick = GenTicks.TicksGame;
			tickManager.lastAvoidGridResultTick = GenTicks.TicksGame;

			var zombieAvoidRadius = Tools.ZombieAvoidRadius(zombie);
			var formerDangerCells = GenRadial.RadialCellsAround(zombieCell, zombieAvoidRadius, true)
				.Where(cell => cell.InBounds(map))
				.ToArray();
			AvoidGrid survivorOnlyGrid = null;
			var deadExclusiveCells = formerDangerCells.Where(cell => realGrid.ShouldAvoid(map, cell)).ToArray();
			var sharedDangerCells = Array.Empty<IntVec3>();
			if (survivor != null)
			{
				survivorOnlyGrid = BuildAvoidGridForZombies(map, new[] { survivor }, install: false);
				tickManager.avoidGrid = realGrid;
				deadExclusiveCells = formerDangerCells
					.Where(cell => realGrid.ShouldAvoid(map, cell))
					.Where(cell => survivorOnlyGrid.ShouldAvoid(map, cell) == false)
					.ToArray();
				sharedDangerCells = formerDangerCells
					.Where(cell => realGrid.ShouldAvoid(map, cell))
					.Where(cell => survivorOnlyGrid.ShouldAvoid(map, cell))
					.ToArray();
			}
			var pathTargetCandidates = deadExclusiveCells
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.ToArray();
			var pathTargetCell = survivor == null
				? pathTargetCandidates
					.OrderBy(cell => cell.DistanceToSquared(actor.Position))
					.FirstOrDefault()
				: pathTargetCandidates
					.OrderByDescending(cell => cell.DistanceToSquared(survivor.Position))
					.ThenBy(cell => cell.DistanceToSquared(actor.Position))
					.FirstOrDefault();
			if (pathTargetCell.IsValid == false)
				pathTargetCell = zombieCell;
			var requestIdBefore = tickManager.lastAvoidGridRequestId;
			var zombieCostBefore = AvoidCost(realGrid, map, zombieCell);
			var dangerCellsBefore = formerDangerCells.Count(cell => realGrid.ShouldAvoid(map, cell));
			var deadExclusiveBefore = deadExclusiveCells.Length;
			var sharedDangerBefore = sharedDangerCells.Length;

			if (withSurvivor && (deadExclusiveBefore == 0 || sharedDangerBefore == 0))
			{
				return new
				{
					success = false,
					transition,
					withSurvivor,
					actor = DescribePawn(actor),
					zombie = DescribeZombie(zombie),
					survivor = DescribeZombie(survivor),
					zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
					survivorCell = ZombieRuntimeActions.DescribeCell(survivor.Position),
					deadExclusiveBefore,
					sharedDangerBefore,
					error = "The overlap fixture did not produce both dead-exclusive and shared danger cells."
				};
			}

			string transitionError = null;
			if (transition == "downed")
			{
				if (TryMakeDownedForCombat(zombie, out transitionError) == false)
				{
					return new
					{
						success = false,
						transition,
						withSurvivor,
						actor = DescribePawn(actor),
						zombie = DescribeZombie(zombie),
						error = transitionError
					};
				}
			}
			else if (transition == "roped")
				zombie.SetRopedBy(actor);
			else if (transition == "confused")
			{
				if (zombie.TryParalyze(GenDate.TicksPerHour, out transitionError) == false)
				{
					return new
					{
						success = false,
						transition,
						withSurvivor,
						actor = DescribePawn(actor),
						zombie = DescribeZombie(zombie),
						error = transitionError
					};
				}
			}
			else
				zombie.Kill(null);

			var transitioned = transition switch
			{
				"downed" => zombie.health?.Downed == true,
				"roped" => ReferenceEquals(zombie.ropedBy, actor),
				"confused" => zombie.IsRopedOrConfused,
				_ => zombie.Dead
			};
			var samples = new List<object>();
			var clearedAtTick = -1;
			var zombieCostAfter = -1;
			var dangerCellsAfter = -1;
			var deadExclusiveAfter = -1;
			var sharedDangerAfter = -1;
			var requestIdAfter = tickManager.lastAvoidGridRequestId;
			var resultIdAfter = tickManager.lastAvoidGridResultId;
			object staleResultRejection = withSurvivor ? null : new { success = true, skipped = true, reason = "single-zombie transition uses the immediate empty-grid path." };
			var staleResultInjected = false;
			var pathToFormerAreaFound = false;
			var pathToFormerAreaAvoidanceClear = false;
			var pathToFormerAreaNodes = 0;
			var pathToFormerAreaAvoidNodes = -1;
			var pathToFormerAreaAvoidCost = -1;
			var pathToFormerAreaIgnoredSurvivorAvoidNodes = 0;
			// TickTasks reaches FetchAvoidGrid once per coroutine cycle; keep this in sync with the yields between FetchAvoidGrid calls.
			var tickTasksAvoidGridCycleYields = 14 + (Constants.CONTAMINATION ? 1 : 0);
			var normalAvoidGridDelayTicks = Constants.TICKMANAGER_AVOIDGRID_DELAY.SecondsToTicks() * tickTasksAvoidGridCycleYields;
			var promptMaxTicks = Math.Max(90, normalAvoidGridDelayTicks - 1);
			const int maxTicks = 240;
			for (var tick = 1; tick <= maxTicks; tick++)
			{
				AdvanceGameTicks(1);
				if (withSurvivor && staleResultInjected == false && tickManager.lastAvoidGridRequestId > requestIdBefore)
				{
					staleResultRejection = VerifyOlderAvoidGridResultRejected(tickManager, map, actor.Position, tickManager.lastAvoidGridRequestId);
					staleResultInjected = true;
					tickManager.UpdateZombieAvoider(true);
				}
				var grid = tickManager.avoidGrid;
				zombieCostAfter = grid == null ? -1 : AvoidCost(grid, map, zombieCell);
				dangerCellsAfter = grid == null ? -1 : formerDangerCells.Count(cell => grid.ShouldAvoid(map, cell));
				deadExclusiveAfter = grid == null ? -1 : deadExclusiveCells.Count(cell => grid.ShouldAvoid(map, cell));
				sharedDangerAfter = grid == null ? -1 : sharedDangerCells.Count(cell => grid.ShouldAvoid(map, cell));
				requestIdAfter = tickManager.lastAvoidGridRequestId;
				resultIdAfter = tickManager.lastAvoidGridResultId;
				var gridCleared = deadExclusiveAfter == 0
					&& (withSurvivor == false ? dangerCellsAfter == 0 : sharedDangerAfter > 0);
				if (gridCleared)
				{
					pathToFormerAreaAvoidanceClear = TryFindPathWithoutAvoidCells(
						map,
						actor,
						pathTargetCell,
						grid,
						survivorOnlyGrid,
						out pathToFormerAreaFound,
						out pathToFormerAreaNodes,
						out pathToFormerAreaAvoidNodes,
						out pathToFormerAreaAvoidCost,
						out pathToFormerAreaIgnoredSurvivorAvoidNodes);
				}
				var cleared = gridCleared && pathToFormerAreaFound && pathToFormerAreaAvoidanceClear;
				if (tick == 1 || tick == maxTicks || cleared)
				{
					samples.Add(new
					{
						tick,
						zombieCostAfter,
						dangerCellsAfter,
						deadExclusiveAfter,
						sharedDangerAfter,
						requestIdAfter,
						resultIdAfter,
						pathToFormerAreaFound,
						pathToFormerAreaAvoidanceClear,
						pathToFormerAreaNodes,
						pathToFormerAreaAvoidNodes,
						pathToFormerAreaAvoidCost,
						pathToFormerAreaIgnoredSurvivorAvoidNodes
					});
				}
				if (cleared)
				{
					clearedAtTick = tick;
					break;
				}
			}

			return new
			{
				success = transitioned
					&& zombieCostBefore > 0
					&& dangerCellsBefore > 0
					&& deadExclusiveBefore > 0
					&& clearedAtTick > 0
					&& clearedAtTick <= promptMaxTicks
					&& resultIdAfter > requestIdBefore
					&& pathToFormerAreaFound
					&& pathToFormerAreaAvoidanceClear
					&& ObjectSuccess(staleResultRejection),
				transition,
				withSurvivor,
				actor = DescribePawn(actor),
				zombie = DescribeZombie(zombie),
				survivor = survivor == null ? null : DescribeZombie(survivor),
				actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
				zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
				survivorCell = survivor == null ? null : ZombieRuntimeActions.DescribeCell(survivor.Position),
				zombieAvoidRadius,
				zombieCostBefore,
				dangerCellsBefore,
				deadExclusiveBefore,
				sharedDangerBefore,
				transitioned,
				clearedAtTick,
				promptMaxTicks,
				normalAvoidGridDelayTicks,
				tickTasksAvoidGridCycleYields,
				maxTicks,
				zombieCostAfter,
				dangerCellsAfter,
				deadExclusiveAfter,
				sharedDangerAfter,
				pathTargetCell = ZombieRuntimeActions.DescribeCell(pathTargetCell),
				pathToFormerAreaFound,
				pathToFormerAreaAvoidanceClear,
				pathToFormerAreaNodes,
				pathToFormerAreaAvoidNodes,
				pathToFormerAreaAvoidCost,
				pathToFormerAreaIgnoredSurvivorAvoidNodes,
				requestIdBefore,
				requestIdAfter,
				resultIdAfter,
				staleResultRejection,
				samples = samples.ToArray()
			};
		}

		static object VerifyAvoidGridRefreshesAfterSpecTransition(Map map, IntVec3 root, string transition, List<Thing> spawned)
		{
			var tickManager = map.GetComponent<TickManager>();
			if (TryFindClearSpawnCell(map, root, 16f, out var zombieCell, out var zombieSpawnError) == false)
				return zombieSpawnError;

			var isElectricCase = transition.StartsWith("electric", StringComparison.Ordinal);
			var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, isElectricCase ? ZombieType.Electrifier : ZombieType.Normal, true);
			if (zombie == null)
			{
				return new
				{
					success = false,
					transition,
					zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
					error = "ZombieGenerator.SpawnZombie returned no zombie for the avoid-grid spec transition fixture."
				};
			}

			var initialState = transition == "trackingToWandering" ? ZombieState.Tracking : ZombieState.Wandering;
			zombie.SetState(initialState);
			zombie.paralyzedUntil = 0;
			zombie.ropedBy = null;
			zombie.consciousness = 1f;
			zombie.pather?.StopDead();
			var waitJob = JobMaker.MakeJob(JobDefOf.Wait);
			waitJob.playerForced = true;
			zombie.jobs?.StartJob(waitJob, JobCondition.InterruptForced, null, false, true);
			spawned.Add(zombie);

			var oldTerrain = map.terrainGrid.TerrainAt(zombieCell);
			var waterTerrain = DefDatabase<TerrainDef>.GetNamedSilentFail("WaterShallow");
			if ((transition == "electricWaterEnter" || transition == "electricWaterLeave") && waterTerrain == null)
			{
				return new
				{
					success = false,
					transition,
					zombie = DescribeZombie(zombie),
					zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
					error = "WaterShallow terrain def is unavailable."
				};
			}

			try
			{
				if (transition == "electricWaterLeave")
					map.terrainGrid.SetTerrain(zombieCell, waterTerrain);
				else if (oldTerrain?.IsWater == true && isElectricCase)
					map.terrainGrid.SetTerrain(zombieCell, TerrainDefOf.Soil);

				if (transition == "rageEnd")
					zombie.raging = GenTicks.TicksAbs + 60000;

				if (isElectricCase)
					zombie.TrackElectricAvoidGridSpec();

				tickManager.allZombiesCached = tickManager.AllZombies().ToHashSet();
				var gridBefore = BuildAvoidGridForZombies(map, tickManager.allZombiesCached);
				InstallAvoidGridBaseline(tickManager, gridBefore);

				var radiusBefore = Tools.ZombieAvoidRadius(zombie);
				var maxCostsBefore = TickManager.ZombieMaxCosts(zombie);
				var activeElectricBefore = zombie.IsActiveElectric;
				var inWaterBefore = zombie.InWater();
				var ragingBefore = zombie.raging;
				var stateBefore = zombie.state;
				var zombieCostBefore = AvoidCost(gridBefore, map, zombieCell);
				var requestIdBefore = tickManager.lastAvoidGridRequestId;

				switch (transition)
				{
					case "electricDisabled":
						zombie.DisableElectric(GenDate.TicksPerHour);
						break;
					case "electricWaterEnter":
						map.terrainGrid.SetTerrain(zombieCell, waterTerrain);
						zombie.TrackElectricAvoidGridSpec();
						break;
					case "electricWaterLeave":
						map.terrainGrid.SetTerrain(zombieCell, oldTerrain?.IsWater == true ? TerrainDefOf.Soil : oldTerrain ?? TerrainDefOf.Soil);
						zombie.TrackElectricAvoidGridSpec();
						break;
					case "rageStart":
						ZombieStateHandler.StartRage(zombie);
						break;
					case "rageEnd":
						zombie.raging = GenTicks.TicksAbs - 1;
						ZombieStateHandler.CheckEndRage(zombie);
						break;
					case "wanderingToTracking":
						zombie.SetState(ZombieState.Tracking);
						break;
					case "trackingToWandering":
						zombie.SetState(ZombieState.Wandering);
						break;
					default:
						return new
						{
							success = false,
							transition,
							error = "Unknown avoid-grid spec transition."
						};
				}

				var radiusAfterTransition = Tools.ZombieAvoidRadius(zombie);
				var maxCostsAfterTransition = TickManager.ZombieMaxCosts(zombie);
				var activeElectricAfterTransition = zombie.IsActiveElectric;
				var inWaterAfterTransition = zombie.InWater();
				var ragingAfterTransition = zombie.raging;
				var stateAfterTransition = zombie.state;
				var afterDangerCells = GenRadial.RadialCellsAround(zombieCell, Math.Max(radiusAfterTransition, 0f), true)
					.Where(cell => cell.InBounds(map))
					.ToArray();

				var tickTasksAvoidGridCycleYields = 14 + (Constants.CONTAMINATION ? 1 : 0);
				var normalAvoidGridDelayTicks = Constants.TICKMANAGER_AVOIDGRID_DELAY.SecondsToTicks() * tickTasksAvoidGridCycleYields;
				var promptMaxTicks = Math.Max(90, normalAvoidGridDelayTicks - 1);
				const int maxTicks = 240;
				var settledAtTick = -1;
				var zombieCostAfter = -1;
				var dangerCellsAfter = -1;
				var requestIdAfter = tickManager.lastAvoidGridRequestId;
				var resultIdAfter = tickManager.lastAvoidGridResultId;
				var samples = new List<object>();
				for (var tick = 1; tick <= maxTicks; tick++)
				{
					AdvanceGameTicks(1);
					var grid = tickManager.avoidGrid;
					zombieCostAfter = grid == null ? -1 : AvoidCost(grid, map, zombieCell);
					dangerCellsAfter = grid == null ? -1 : afterDangerCells.Count(cell => grid.ShouldAvoid(map, cell));
					requestIdAfter = tickManager.lastAvoidGridRequestId;
					resultIdAfter = tickManager.lastAvoidGridResultId;
					var expectedAfterSettled = radiusAfterTransition <= 0f
						? zombieCostAfter == 0 && dangerCellsAfter == 0
						: zombieCostAfter == (int)maxCostsAfterTransition && dangerCellsAfter > 0;
					var settled = expectedAfterSettled && resultIdAfter > requestIdBefore;
					if (tick == 1 || tick == maxTicks || settled)
					{
						samples.Add(new
						{
							tick,
							zombieCostAfter,
							dangerCellsAfter,
							requestIdAfter,
							resultIdAfter
						});
					}
					if (settled)
					{
						settledAtTick = tick;
						break;
					}
				}

				var expectedBefore = radiusBefore <= 0f
					? zombieCostBefore == 0
					: zombieCostBefore == (int)maxCostsBefore;
				var radiusChanged = radiusBefore != radiusAfterTransition;
				var maxCostsChanged = maxCostsBefore != maxCostsAfterTransition;
				return new
				{
					success = expectedBefore
						&& (radiusChanged || maxCostsChanged)
						&& settledAtTick > 0
						&& settledAtTick <= promptMaxTicks,
					transition,
					zombie = DescribeZombie(zombie),
					zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
					terrainBefore = oldTerrain?.defName,
					terrainAfterTransition = zombieCell.GetTerrain(map)?.defName,
					activeElectricBefore,
					activeElectricAfterTransition,
					inWaterBefore,
					inWaterAfterTransition,
					ragingBefore,
					ragingAfterTransition,
					stateBefore = stateBefore.ToString(),
					stateAfterTransition = stateAfterTransition.ToString(),
					radiusBefore,
					radiusAfterTransition,
					maxCostsBefore,
					maxCostsAfterTransition,
					radiusChanged,
					maxCostsChanged,
					zombieCostBefore,
					settledAtTick,
					promptMaxTicks,
					normalAvoidGridDelayTicks,
					tickTasksAvoidGridCycleYields,
					maxTicks,
					zombieCostAfter,
					dangerCellsAfter,
					requestIdBefore,
					requestIdAfter,
					resultIdAfter,
					samples = samples.ToArray()
				};
			}
			finally
			{
				if (oldTerrain != null && zombieCell.InBounds(map))
					map.terrainGrid.SetTerrain(zombieCell, oldTerrain);
			}
		}

		static object VerifyAvoidGridRefreshesAfterFirstElectricSampleExpiry(Map map, IntVec3 root, List<Thing> spawned)
		{
			const string transition = "firstElectricSampleExpiry";
			var tickManager = map.GetComponent<TickManager>();
			if (TryFindClearSpawnCell(map, root, 16f, out var zombieCell, out var zombieSpawnError) == false)
				return zombieSpawnError;

			var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Electrifier, true);
			if (zombie == null)
			{
				return new
				{
					success = false,
					transition,
					zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
					error = "ZombieGenerator.SpawnZombie returned no electrifier for the first-sample avoid-grid fixture."
				};
			}

			zombie.state = ZombieState.Tracking;
			zombie.paralyzedUntil = 0;
			zombie.ropedBy = null;
			zombie.consciousness = 1f;
			zombie.pather?.StopDead();
			var waitJob = JobMaker.MakeJob(JobDefOf.Wait);
			waitJob.playerForced = true;
			zombie.jobs?.StartJob(waitJob, JobCondition.InterruptForced, null, false, true);
			spawned.Add(zombie);

			var oldTerrain = map.terrainGrid.TerrainAt(zombieCell);
			try
			{
				if (oldTerrain?.IsWater == true)
					map.terrainGrid.SetTerrain(zombieCell, TerrainDefOf.Soil);

				zombie.electricDisabledUntil = GenTicks.TicksGame;
				tickManager.allZombiesCached = tickManager.AllZombies().ToHashSet();

				var disabledGrid = BuildAvoidGridForZombies(map, new[] { zombie }, install: false);
				InstallAvoidGridBaseline(tickManager, disabledGrid, new[] { zombie });

				var radiusBefore = Tools.ZombieAvoidRadius(zombie);
				var maxCostsBefore = TickManager.ZombieMaxCosts(zombie);
				var activeElectricBefore = zombie.IsActiveElectric;
				var disabledUntilBefore = zombie.electricDisabledUntil;
				var formerDangerCells = GenRadial.RadialCellsAround(zombieCell, radiusBefore, true)
					.Where(cell => cell.InBounds(map))
					.ToArray();
				var zombieCostBefore = AvoidCost(disabledGrid, map, zombieCell);
				var dangerCellsBefore = formerDangerCells.Count(cell => disabledGrid.ShouldAvoid(map, cell));
				var requestIdBefore = tickManager.lastAvoidGridRequestId;

				var tickTasksAvoidGridCycleYields = 14 + (Constants.CONTAMINATION ? 1 : 0);
				var normalAvoidGridDelayTicks = Constants.TICKMANAGER_AVOIDGRID_DELAY.SecondsToTicks() * tickTasksAvoidGridCycleYields;
				var promptMaxTicks = Math.Max(90, normalAvoidGridDelayTicks - 1);
				const int maxTicks = 240;
				var activeAtTick = -1;
				var clearedAtTick = -1;
				var radiusAfter = radiusBefore;
				var activeElectricAfter = activeElectricBefore;
				var zombieCostAfter = -1;
				var dangerCellsAfter = -1;
				var requestIdAfter = tickManager.lastAvoidGridRequestId;
				var resultIdAfter = tickManager.lastAvoidGridResultId;
				var samples = new List<object>();
				for (var tick = 1; tick <= maxTicks; tick++)
				{
					AdvanceGameTicks(1);
					activeElectricAfter = zombie.IsActiveElectric;
					radiusAfter = Tools.ZombieAvoidRadius(zombie);
					if (activeAtTick < 0 && activeElectricAfter)
						activeAtTick = tick;

					var grid = tickManager.avoidGrid;
					zombieCostAfter = grid == null ? -1 : AvoidCost(grid, map, zombieCell);
					dangerCellsAfter = grid == null ? -1 : formerDangerCells.Count(cell => grid.ShouldAvoid(map, cell));
					requestIdAfter = tickManager.lastAvoidGridRequestId;
					resultIdAfter = tickManager.lastAvoidGridResultId;
					var cleared = activeElectricAfter
						&& radiusAfter == 0f
						&& zombieCostAfter == 0
						&& dangerCellsAfter == 0
						&& resultIdAfter > requestIdBefore;
					if (tick == 1 || tick == maxTicks || tick == activeAtTick || cleared)
					{
						samples.Add(new
						{
							tick,
							activeElectric = activeElectricAfter,
							radius = radiusAfter,
							zombieCostAfter,
							dangerCellsAfter,
							requestIdAfter,
							resultIdAfter
						});
					}
					if (cleared)
					{
						clearedAtTick = tick;
						break;
					}
				}

				return new
				{
					success = activeElectricBefore == false
						&& radiusBefore > 0f
						&& zombieCostBefore == (int)maxCostsBefore
						&& dangerCellsBefore > 0
						&& activeAtTick > 0
						&& activeElectricAfter
						&& radiusAfter == 0f
						&& clearedAtTick > 0
						&& clearedAtTick <= promptMaxTicks
						&& resultIdAfter > requestIdBefore,
					transition,
					zombie = DescribeZombie(zombie),
					zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
					terrainBefore = oldTerrain?.defName,
					terrainAfterTransition = zombieCell.GetTerrain(map)?.defName,
					disabledUntilBefore,
					disabledUntilAfter = zombie.electricDisabledUntil,
					activeElectricBefore,
					activeElectricAfter,
					radiusBefore,
					radiusAfter,
					maxCostsBefore,
					zombieCostBefore,
					dangerCellsBefore,
					activeAtTick,
					clearedAtTick,
					promptMaxTicks,
					normalAvoidGridDelayTicks,
					tickTasksAvoidGridCycleYields,
					maxTicks,
					zombieCostAfter,
					dangerCellsAfter,
					requestIdBefore,
					requestIdAfter,
					resultIdAfter,
					samples = samples.ToArray()
				};
			}
			finally
			{
				if (oldTerrain != null && zombieCell.InBounds(map))
					map.terrainGrid.SetTerrain(zombieCell, oldTerrain);
			}
		}

		static void InstallAvoidGridBaseline(TickManager tickManager, AvoidGrid grid, IEnumerable<Zombie> zombies = null)
		{
			tickManager.avoidGrid = grid;
			tickManager.lastAvoidGridRequestId = grid.requestId;
			tickManager.lastAvoidGridResultId = grid.requestId;
			tickManager.lastAvoidGridRequestTick = GenTicks.TicksGame;
			tickManager.lastAvoidGridResultTick = GenTicks.TicksGame;
			typeof(TickManager).GetField("avoidGridRefreshRequested", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(tickManager, false);
			typeof(TickManager).GetField("promptAvoidGridResultPending", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(tickManager, false);
			TickManager.SeedElectricAvoidGridSnapshots(zombies ?? tickManager.allZombiesCached ?? tickManager.AllZombies());
		}

		static object VerifyAvoidGridAddsAfterParalysisExpiry(Map map, IntVec3 root, string path, List<Thing> spawned)
		{
			const string transition = "paralysisExpired";
			var tickManager = map.GetComponent<TickManager>();
			if (TryFindClearSpawnCell(map, root, 16f, out var actorCell, out var actorSpawnError) == false)
				return actorSpawnError;

			var actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(actor, actorCell, map, Rot4.South);
			DisablePawnWork(actor);
			actor.drafter.Drafted = true;
			var waitJob = JobMaker.MakeJob(JobDefOf.Wait);
			waitJob.playerForced = true;
			actor.jobs.StartJob(waitJob, JobCondition.InterruptForced, null, false, true);
			spawned.Add(actor);
			var config = ColonistSettings.Values.ConfigFor(actor);
			if (config != null)
				config.autoAvoidZombies = true;

			var zombieCell = GenRadial.RadialCellsAround(actorCell, 8f, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.Where(cell => cell.DistanceTo(actorCell) >= 4f)
				.OrderBy(cell => cell.DistanceToSquared(actorCell))
				.FirstOrDefault();
			if (zombieCell.IsValid == false)
			{
				return new
				{
					success = false,
					transition,
					path,
					actor = DescribePawn(actor),
					actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
					error = "No zombie cell was found for the avoid-grid paralysis-expiry fixture."
				};
			}

			var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
			if (zombie == null)
			{
				return new
				{
					success = false,
					transition,
					path,
					actor = DescribePawn(actor),
					zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
					error = "ZombieGenerator.SpawnZombie returned no zombie."
				};
			}
			zombie.state = ZombieState.Tracking;
			zombie.paralyzedUntil = GenTicks.TicksAbs;
			spawned.Add(zombie);
			tickManager.allZombiesCached = tickManager.AllZombies().ToHashSet();

			var emptyGrid = Tools.avoider.UpdateZombiePositionsImmediately(map, new List<ZombieCostSpecs>());
			tickManager.avoidGrid = emptyGrid;
			tickManager.lastAvoidGridRequestId = emptyGrid.requestId;
			tickManager.lastAvoidGridResultId = emptyGrid.requestId;
			tickManager.lastAvoidGridRequestTick = GenTicks.TicksGame;
			tickManager.lastAvoidGridResultTick = GenTicks.TicksGame;

			var zombieAvoidRadius = Tools.ZombieAvoidRadius(zombie);
			var formerDangerCells = GenRadial.RadialCellsAround(zombieCell, zombieAvoidRadius, true)
				.Where(cell => cell.InBounds(map))
				.ToArray();
			var zombieCostBefore = AvoidCost(emptyGrid, map, zombieCell);
			var dangerCellsBefore = formerDangerCells.Count(cell => emptyGrid.ShouldAvoid(map, cell));
			var requestIdBefore = tickManager.lastAvoidGridRequestId;
			var paralyzedUntilBefore = zombie.paralyzedUntil;
			var affectsAvoidGridSampleBefore = zombie.AffectsAvoidGrid;
			var previousAffectsAvoidGrid = zombie.AffectsAvoidGridBeforeClearingExpiredParalysis();

			var triggerReturned = path switch
			{
				"jobDriver" => ZombieParalysis.HandleParalyzedTick((JobDriver_Stumble)null, zombie),
				"stateTick" => ZombieStateHandler.DownedOrUnconsciousness(zombie),
				_ => false
			};
			var transitioned = zombie.paralyzedUntil == 0 && zombie.AffectsAvoidGrid;

			var tickTasksAvoidGridCycleYields = 14 + (Constants.CONTAMINATION ? 1 : 0);
			var normalAvoidGridDelayTicks = Constants.TICKMANAGER_AVOIDGRID_DELAY.SecondsToTicks() * tickTasksAvoidGridCycleYields;
			var promptMaxTicks = Math.Max(90, normalAvoidGridDelayTicks - 1);
			const int maxTicks = 240;
			var addedAtTick = -1;
			var zombieCostAfter = -1;
			var dangerCellsAfter = -1;
			var requestIdAfter = tickManager.lastAvoidGridRequestId;
			var resultIdAfter = tickManager.lastAvoidGridResultId;
			var samples = new List<object>();
			for (var tick = 1; tick <= maxTicks; tick++)
			{
				AdvanceGameTicks(1);
				var grid = tickManager.avoidGrid;
				zombieCostAfter = grid == null ? -1 : AvoidCost(grid, map, zombieCell);
				dangerCellsAfter = grid == null ? -1 : formerDangerCells.Count(cell => grid.ShouldAvoid(map, cell));
				requestIdAfter = tickManager.lastAvoidGridRequestId;
				resultIdAfter = tickManager.lastAvoidGridResultId;
				var gridAdded = zombieCostAfter > 0 && dangerCellsAfter > 0;
				if (tick == 1 || tick == maxTicks || gridAdded)
				{
					samples.Add(new
					{
						tick,
						zombieCostAfter,
						dangerCellsAfter,
						requestIdAfter,
						resultIdAfter
					});
				}
				if (gridAdded)
				{
					addedAtTick = tick;
					break;
				}
			}

			return new
			{
				success = transitioned
					&& triggerReturned == false
					&& affectsAvoidGridSampleBefore
					&& previousAffectsAvoidGrid == false
					&& zombieCostBefore == 0
					&& dangerCellsBefore == 0
					&& addedAtTick > 0
					&& addedAtTick <= promptMaxTicks
					&& resultIdAfter > requestIdBefore,
				transition,
				path,
				actor = DescribePawn(actor),
				zombie = DescribeZombie(zombie),
				actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
				zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
				zombieAvoidRadius,
				paralyzedUntilBefore,
				affectsAvoidGridSampleBefore,
				previousAffectsAvoidGrid,
				triggerReturned,
				zombieCostBefore,
				dangerCellsBefore,
				transitioned,
				addedAtTick,
				promptMaxTicks,
				normalAvoidGridDelayTicks,
				tickTasksAvoidGridCycleYields,
				maxTicks,
				zombieCostAfter,
				dangerCellsAfter,
				requestIdBefore,
				requestIdAfter,
				resultIdAfter,
				samples = samples.ToArray()
			};
		}

		static object VerifyAvoidGridAddsAfterConsciousnessReset(Map map, IntVec3 root, List<Thing> spawned)
		{
			const string transition = "consciousnessReset";
			var tickManager = map.GetComponent<TickManager>();
			if (TryFindClearSpawnCell(map, root, 16f, out var zombieCell, out var zombieSpawnError) == false)
				return zombieSpawnError;

			var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
			if (zombie == null)
			{
				return new
				{
					success = false,
					transition,
					zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
					error = "ZombieGenerator.SpawnZombie returned no zombie."
				};
			}
			zombie.state = ZombieState.Tracking;
			spawned.Add(zombie);

			foreach (var hediff in zombie.health.hediffSet.hediffs.ToList())
				zombie.health.RemoveHediff(hediff);
			var hediffCountAfterClear = zombie.health.hediffSet.hediffs.Count;
			zombie.paralyzedUntil = 0;
			zombie.ropedBy = null;
			zombie.isHealing = false;
			zombie.consciousness = 0f;
			zombie.jobs.StartJob(JobMaker.MakeJob(CustomDefs.Stumble), JobCondition.InterruptForced, null, true, false, null, null);
			var driverStarted = zombie.jobs?.curDriver is JobDriver_Stumble;
			tickManager.allZombiesCached = tickManager.AllZombies().ToHashSet();

			var emptyGrid = Tools.avoider.UpdateZombiePositionsImmediately(map, new List<ZombieCostSpecs>());
			tickManager.avoidGrid = emptyGrid;
			tickManager.lastAvoidGridRequestId = emptyGrid.requestId;
			tickManager.lastAvoidGridResultId = emptyGrid.requestId;
			tickManager.lastAvoidGridRequestTick = GenTicks.TicksGame;
			tickManager.lastAvoidGridResultTick = GenTicks.TicksGame;

			var zombieAvoidRadius = Tools.ZombieAvoidRadius(zombie);
			var initialDangerCells = GenRadial.RadialCellsAround(zombieCell, zombieAvoidRadius, true)
				.Where(cell => cell.InBounds(map))
				.ToArray();
			var zombieCostBefore = AvoidCost(emptyGrid, map, zombieCell);
			var dangerCellsBefore = initialDangerCells.Count(cell => emptyGrid.ShouldAvoid(map, cell));
			var requestIdBefore = tickManager.lastAvoidGridRequestId;
			var affectsAvoidGridBefore = zombie.AffectsAvoidGrid;
			var needsDownedTickBefore = ZombieStateHandler.NeedsDownedOrUnconsciousnessTick(zombie);

			var tickTasksAvoidGridCycleYields = 14 + (Constants.CONTAMINATION ? 1 : 0);
			var normalAvoidGridDelayTicks = Constants.TICKMANAGER_AVOIDGRID_DELAY.SecondsToTicks() * tickTasksAvoidGridCycleYields;
			var promptMaxTicks = Math.Max(90, normalAvoidGridDelayTicks - 1);
			const int maxTicks = 240;
			var resetAtTick = -1;
			var addedAtTick = -1;
			var zombieCostAfter = -1;
			var dangerCellsAfter = -1;
			var requestIdAfter = tickManager.lastAvoidGridRequestId;
			var resultIdAfter = tickManager.lastAvoidGridResultId;
			var finalZombieCell = zombie.Position;
			var samples = new List<object>();
			for (var tick = 1; tick <= maxTicks; tick++)
			{
				AdvanceGameTicks(1);
				finalZombieCell = zombie.Position;
				if (resetAtTick < 0 && zombie.consciousness > Constants.MIN_CONSCIOUSNESS && zombie.AffectsAvoidGrid)
					resetAtTick = tick;

				var grid = tickManager.avoidGrid;
				var currentDangerCells = GenRadial.RadialCellsAround(finalZombieCell, zombieAvoidRadius, true)
					.Where(cell => cell.InBounds(map))
					.ToArray();
				zombieCostAfter = grid == null ? -1 : AvoidCost(grid, map, finalZombieCell);
				dangerCellsAfter = grid == null ? -1 : currentDangerCells.Count(cell => grid.ShouldAvoid(map, cell));
				requestIdAfter = tickManager.lastAvoidGridRequestId;
				resultIdAfter = tickManager.lastAvoidGridResultId;
				var gridAdded = zombieCostAfter > 0 && dangerCellsAfter > 0;
				if (tick == 1 || tick == maxTicks || tick == resetAtTick || gridAdded)
				{
					samples.Add(new
					{
						tick,
						consciousness = zombie.consciousness,
						affectsAvoidGrid = zombie.AffectsAvoidGrid,
						zombieCell = ZombieRuntimeActions.DescribeCell(finalZombieCell),
						zombieCostAfter,
						dangerCellsAfter,
						requestIdAfter,
						resultIdAfter
					});
				}
				if (gridAdded)
				{
					addedAtTick = tick;
					break;
				}
			}

			return new
			{
				success = driverStarted
					&& hediffCountAfterClear == 0
					&& affectsAvoidGridBefore == false
					&& needsDownedTickBefore == false
					&& zombieCostBefore == 0
					&& dangerCellsBefore == 0
					&& resetAtTick > 0
					&& addedAtTick > 0
					&& addedAtTick <= promptMaxTicks
					&& resultIdAfter > requestIdBefore,
				transition,
				zombie = DescribeZombie(zombie),
				zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
				finalZombieCell = ZombieRuntimeActions.DescribeCell(finalZombieCell),
				zombieAvoidRadius,
				driverStarted,
				hediffCountAfterClear,
				affectsAvoidGridBefore,
				needsDownedTickBefore,
				zombieCostBefore,
				dangerCellsBefore,
				resetAtTick,
				addedAtTick,
				promptMaxTicks,
				normalAvoidGridDelayTicks,
				tickTasksAvoidGridCycleYields,
				maxTicks,
				zombieCostAfter,
				dangerCellsAfter,
				requestIdBefore,
				requestIdAfter,
				resultIdAfter,
				samples = samples.ToArray()
			};
		}

		static object VerifyAvoidGridAddsAfterZombieRecovery(Map map, IntVec3 root, List<Thing> spawned)
		{
			const string transition = "undowned";
			var tickManager = map.GetComponent<TickManager>();
			if (TryFindClearSpawnCell(map, root, 16f, out var actorCell, out var actorSpawnError) == false)
				return actorSpawnError;

			var actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(actor, actorCell, map, Rot4.South);
			DisablePawnWork(actor);
			actor.drafter.Drafted = true;
			var waitJob = JobMaker.MakeJob(JobDefOf.Wait);
			waitJob.playerForced = true;
			actor.jobs.StartJob(waitJob, JobCondition.InterruptForced, null, false, true);
			spawned.Add(actor);
			var config = ColonistSettings.Values.ConfigFor(actor);
			if (config != null)
				config.autoAvoidZombies = true;

			var zombieCell = GenRadial.RadialCellsAround(actorCell, 8f, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.Where(cell => cell.DistanceTo(actorCell) >= 4f)
				.OrderBy(cell => cell.DistanceToSquared(actorCell))
				.FirstOrDefault();
			if (zombieCell.IsValid == false)
			{
				return new
				{
					success = false,
					transition,
					actor = DescribePawn(actor),
					actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
					error = "No zombie cell was found for the avoid-grid undowned refresh fixture."
				};
			}

			var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
			if (zombie == null)
			{
				return new
				{
					success = false,
					transition,
					actor = DescribePawn(actor),
					zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
					error = "ZombieGenerator.SpawnZombie returned no zombie."
				};
			}
			zombie.state = ZombieState.Tracking;
			spawned.Add(zombie);
			tickManager.allZombiesCached = tickManager.AllZombies().ToHashSet();

			var makeDowned = typeof(Pawn_HealthTracker).GetMethod("MakeDowned", BindingFlags.Instance | BindingFlags.NonPublic);
			var makeUndowned = typeof(Pawn_HealthTracker).GetMethod("MakeUndowned", BindingFlags.Instance | BindingFlags.NonPublic);
			var avoidGridRefreshRequestedField = typeof(TickManager).GetField("avoidGridRefreshRequested", BindingFlags.Instance | BindingFlags.NonPublic);
			var promptAvoidGridResultPendingField = typeof(TickManager).GetField("promptAvoidGridResultPending", BindingFlags.Instance | BindingFlags.NonPublic);
			if (makeDowned == null || makeUndowned == null || avoidGridRefreshRequestedField == null || promptAvoidGridResultPendingField == null)
			{
				return new
				{
					success = false,
					transition,
					error = "Could not reflect Pawn_HealthTracker.MakeDowned, MakeUndowned, avoidGridRefreshRequested, or promptAvoidGridResultPending."
				};
			}
			bool AvoidGridRefreshRequested() => (bool)avoidGridRefreshRequestedField.GetValue(tickManager);
			bool PromptAvoidGridResultPending() => (bool)promptAvoidGridResultPendingField.GetValue(tickManager);

			makeDowned.Invoke(zombie.health, new object[makeDowned.GetParameters().Length]);
			if (zombie.health?.Downed != true)
			{
				return new
				{
					success = false,
					transition,
					actor = DescribePawn(actor),
					zombie = DescribeZombie(zombie),
					error = "MakeDowned did not leave the zombie health-downed."
				};
			}

			var emptyGrid = Tools.avoider.UpdateZombiePositionsImmediately(map, new List<ZombieCostSpecs>());
			tickManager.avoidGrid = emptyGrid;
			tickManager.lastAvoidGridRequestId = emptyGrid.requestId;
			tickManager.lastAvoidGridResultId = emptyGrid.requestId;
			tickManager.lastAvoidGridRequestTick = GenTicks.TicksGame;
			tickManager.lastAvoidGridResultTick = GenTicks.TicksGame;
			avoidGridRefreshRequestedField.SetValue(tickManager, false);
			promptAvoidGridResultPendingField.SetValue(tickManager, false);
			var pendingRefreshClearedBeforeUndowned = AvoidGridRefreshRequested() == false && PromptAvoidGridResultPending() == false;

			var zombieAvoidRadius = Tools.ZombieAvoidRadius(zombie);
			var formerDangerCells = GenRadial.RadialCellsAround(zombieCell, zombieAvoidRadius, true)
				.Where(cell => cell.InBounds(map))
				.ToArray();
			var zombieCostBefore = AvoidCost(emptyGrid, map, zombieCell);
			var dangerCellsBefore = formerDangerCells.Count(cell => emptyGrid.ShouldAvoid(map, cell));
			var requestIdBefore = tickManager.lastAvoidGridRequestId;

			zombie.consciousness = 1f;
			var consciousnessBeforeUndowned = zombie.consciousness;
			makeUndowned.Invoke(zombie.health, new object[makeUndowned.GetParameters().Length]);
			var transitioned = zombie.health?.Downed == false;
			var refreshRequestedByUndowned = AvoidGridRefreshRequested();
			var promptPendingAfterUndowned = PromptAvoidGridResultPending();
			var requestIdAfterUndowned = tickManager.lastAvoidGridRequestId;
			zombie.jobs.StartJob(JobMaker.MakeJob(CustomDefs.Stumble), JobCondition.InterruptForced, null, true, false, null, null);
			var driverStarted = zombie.jobs?.curDriver is JobDriver_Stumble;
			var tickTasksAvoidGridCycleYields = 14 + (Constants.CONTAMINATION ? 1 : 0);
			var normalAvoidGridDelayTicks = Constants.TICKMANAGER_AVOIDGRID_DELAY.SecondsToTicks() * tickTasksAvoidGridCycleYields;
			var promptMaxTicks = Math.Max(90, normalAvoidGridDelayTicks - 1);
			const int maxTicks = 240;
			var addedAtTick = -1;
			var zombieCostAfter = -1;
			var dangerCellsAfter = -1;
			var requestIdAfter = tickManager.lastAvoidGridRequestId;
			var resultIdAfter = tickManager.lastAvoidGridResultId;
			var finalZombieCell = zombie.Position;
			var samples = new List<object>();
			for (var tick = 1; tick <= maxTicks; tick++)
			{
				AdvanceGameTicks(1);
				var grid = tickManager.avoidGrid;
				finalZombieCell = zombie.Position;
				var currentDangerCells = GenRadial.RadialCellsAround(finalZombieCell, zombieAvoidRadius, true)
					.Where(cell => cell.InBounds(map))
					.ToArray();
				zombieCostAfter = grid == null ? -1 : AvoidCost(grid, map, finalZombieCell);
				dangerCellsAfter = grid == null ? -1 : currentDangerCells.Count(cell => grid.ShouldAvoid(map, cell));
				requestIdAfter = tickManager.lastAvoidGridRequestId;
				resultIdAfter = tickManager.lastAvoidGridResultId;
				var gridAdded = zombieCostAfter > 0 && dangerCellsAfter > 0;
				if (tick == 1 || tick == maxTicks || gridAdded)
				{
					samples.Add(new
					{
						tick,
						zombieCell = ZombieRuntimeActions.DescribeCell(finalZombieCell),
						zombieCostAfter,
						dangerCellsAfter,
						requestIdAfter,
						resultIdAfter
					});
				}
				if (gridAdded)
				{
					addedAtTick = tick;
					break;
				}
			}

			return new
			{
				success = transitioned
					&& driverStarted
					&& pendingRefreshClearedBeforeUndowned
					&& refreshRequestedByUndowned
					&& zombieCostBefore == 0
					&& dangerCellsBefore == 0
					&& addedAtTick > 0
					&& addedAtTick <= promptMaxTicks
					&& resultIdAfter > requestIdBefore,
				transition,
				actor = DescribePawn(actor),
				zombie = DescribeZombie(zombie),
				actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
				zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
				finalZombieCell = ZombieRuntimeActions.DescribeCell(finalZombieCell),
				zombieAvoidRadius,
				driverStarted,
				zombieCostBefore,
				dangerCellsBefore,
				consciousnessBeforeUndowned,
				transitioned,
				pendingRefreshClearedBeforeUndowned,
				refreshRequestedByUndowned,
				promptPendingAfterUndowned,
				requestIdAfterUndowned,
				addedAtTick,
				promptMaxTicks,
				normalAvoidGridDelayTicks,
				tickTasksAvoidGridCycleYields,
				maxTicks,
				zombieCostAfter,
				dangerCellsAfter,
				requestIdBefore,
				requestIdAfter,
				resultIdAfter,
				samples = samples.ToArray()
			};
		}

		static AvoidGrid BuildAvoidGridForZombies(Map map, IEnumerable<Zombie> zombies, bool install = true)
		{
			var specs = TickManager.BuildAvoidGridSpecsFor(zombies);
			var grid = Tools.avoider.UpdateZombiePositionsImmediately(map, specs);
			if (install)
				map.GetComponent<TickManager>().avoidGrid = grid;
			return grid;
		}

		static object VerifyOlderAvoidGridResultRejected(TickManager tickManager, Map map, IntVec3 poisonCell, long currentRequestId)
		{
			var fetchMethod = typeof(TickManager).GetMethod("FetchAvoidGrid", BindingFlags.Instance | BindingFlags.NonPublic);
			var avoidGridCounterField = typeof(TickManager).GetField("avoidGridCounter", BindingFlags.Instance | BindingFlags.NonPublic);
			var queueForMapMethod = typeof(ZombieAvoider).GetMethod("QueueForMap", BindingFlags.Instance | BindingFlags.NonPublic);
			if (fetchMethod == null || avoidGridCounterField == null || queueForMapMethod == null)
			{
				return new
				{
					success = false,
					error = "Avoid-grid reflection contract could not find FetchAvoidGrid, avoidGridCounter, or QueueForMap."
				};
			}

			var staleRequestId = currentRequestId - 1;
			var beforeGrid = tickManager.avoidGrid;
			var resultIdBefore = tickManager.lastAvoidGridResultId;
			var costBefore = beforeGrid == null ? -1 : AvoidCost(beforeGrid, map, poisonCell);
			var staleGrid = new AvoidGrid(map);
			var costs = staleGrid.GetNewCosts();
			costs[poisonCell.x + poisonCell.z * map.Size.x] = 4321;
			staleGrid.requestId = staleRequestId;
			staleGrid.FinalizeCosts();

			var queue = (ConcurrentQueue<AvoidGrid>)queueForMapMethod.Invoke(Tools.avoider, new object[] { map });
			queue.Replace(staleGrid);
			avoidGridCounterField.SetValue(tickManager, -1);
			fetchMethod.Invoke(tickManager, Array.Empty<object>());

			var costAfter = tickManager.avoidGrid == null ? -1 : AvoidCost(tickManager.avoidGrid, map, poisonCell);
			var resultIdAfter = tickManager.lastAvoidGridResultId;
			var staleResultAccepted = resultIdAfter == staleRequestId || costAfter == 4321;
			return new
			{
				success = staleResultAccepted == false,
				currentRequestId,
				staleRequestId,
				resultIdBefore,
				resultIdAfter,
				staleResultAccepted,
				costBefore,
				costAfter
			};
		}

		static bool TryFindPathWithoutAvoidCells(Map map, Pawn actor, IntVec3 destination, AvoidGrid grid, AvoidGrid allowedAvoidGrid, out bool found, out int nodes, out int avoidNodes, out int avoidCost, out int ignoredAvoidNodes)
		{
			found = false;
			nodes = 0;
			avoidNodes = 0;
			avoidCost = 0;
			ignoredAvoidNodes = 0;
			if (actor == null || destination.IsValid == false || grid == null)
				return false;
			var path = map.pathFinder.FindPathNow(actor.Position, destination, actor, null, PathEndMode.OnCell);
			try
			{
				found = path?.Found == true;
				if (found == false)
					return false;
				var pathNodes = path.NodesReversed;
				nodes = pathNodes.Count;
				for (var i = 0; i < pathNodes.Count; i++)
				{
					var cell = pathNodes[i];
					if (grid.ShouldAvoid(map, cell) == false)
						continue;
					if (allowedAvoidGrid != null && allowedAvoidGrid.ShouldAvoid(map, cell))
					{
						ignoredAvoidNodes++;
						continue;
					}
					avoidNodes++;
					avoidCost += AvoidCost(grid, map, cell);
				}
				return avoidNodes == 0;
			}
			finally
			{
				path?.ReleaseToPool();
			}
		}

		static int CleanupAvoidGridDeathRefreshFixtures(List<Thing> spawned)
		{
			var cleaned = 0;
			for (var i = spawned.Count - 1; i >= 0; i--)
			{
				if (CleanupAvoidGridDeathRefreshThing(spawned[i]))
					cleaned++;
			}
			spawned.Clear();
			return cleaned;
		}

		static bool CleanupAvoidGridDeathRefreshThing(Thing thing)
		{
			if (thing == null)
				return false;
			if (thing is Pawn pawn)
			{
				Find.World?.GetComponent<ColonistSettings>()?.RemoveColonist(pawn);
				var cleanedPawn = false;
				if (pawn.Corpse != null && pawn.Corpse.Destroyed == false)
				{
					pawn.Corpse.Destroy(DestroyMode.Vanish);
					cleanedPawn = true;
				}
				if (pawn.Destroyed == false)
				{
					pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced);
					pawn.Destroy(DestroyMode.Vanish);
					cleanedPawn = true;
				}
				return cleanedPawn;
			}
			if (thing.Destroyed)
				return false;
			thing.Destroy(DestroyMode.Vanish);
			return true;
		}

		[Tool("zombieland/avoid_grid_async_recovery_contract", Description = "Verify poisoned or emptied zombie avoidance grids recover to no ghost danger and do not force colonists to flee.")]
		public static object AvoidGridAsyncRecoveryContract()
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

			var tickManager = map.GetComponent<TickManager>();
			if (tickManager == null)
			{
				return new
				{
					success = false,
					error = "Current map has no Zombieland TickManager."
				};
			}

			var oldBetterAvoidance = ZombieSettings.Values.betterZombieAvoidance;
			ZombieSettings.Values.betterZombieAvoidance = true;
			try
			{
				var destroyedZombies = ZombieRuntimeActions.DestroyZombies(map);
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var actorCell, out var actorSpawnError) == false)
					return actorSpawnError;

				var actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(actor, actorCell, map, Rot4.South);
				DisablePawnWork(actor);
				var config = ColonistSettings.Values.ConfigFor(actor);
				if (config != null)
					config.autoAvoidZombies = true;

				var emptySpecs = new List<ZombieCostSpecs>();
				var emptyGrid = Tools.avoider.UpdateZombiePositionsImmediately(map, emptySpecs);
				tickManager.avoidGrid = emptyGrid;

				var actorIndex = actor.Position.x + actor.Position.z * map.Size.x;
				var poisonedCosts = emptyGrid.GetNewCosts();
				poisonedCosts[actorIndex] = 3000;
				var poisonedInactiveCost = poisonedCosts[actorIndex];

				var recoveredGrid = Tools.avoider.UpdateZombiePositionsImmediately(map, emptySpecs);
				tickManager.avoidGrid = recoveredGrid;
				var actorCostAfterRecovery = AvoidCost(recoveredGrid, map, actor.Position);
				var actorInDangerAfterRecovery = recoveredGrid.InAvoidDanger(actor);
				var dangerAfterRecovery = actor.Position.GetDangerFor(actor, map);

				var waitJob = JobMaker.MakeJob(JobDefOf.Wait);
				waitJob.playerForced = false;
				actor.jobs.StartJob(waitJob, JobCondition.InterruptForced, null, false, true);
				var startedJob = actor.CurJobDef?.defName;
				var samples = new List<object>();
				var fleeTick = -1;
				const int maxTicks = 12;
				for (var tick = 1; tick <= maxTicks; tick++)
				{
					AdvanceGameTicks(1);
					var currentJob = actor.CurJob;
					if (tick == 1 || tick == maxTicks || currentJob?.def == JobDefOf.Flee)
					{
						samples.Add(new
						{
							tick,
							job = actor.CurJobDef?.defName,
							currentJob?.playerForced,
							target = currentJob?.targetA.Cell.IsValid == true ? ZombieRuntimeActions.DescribeCell(currentJob.targetA.Cell) : null
						});
					}

					if (currentJob?.def == JobDefOf.Flee)
					{
						fleeTick = tick;
						break;
					}
				}

				var zombieCell = GenRadial.RadialCellsAround(actorCell, 3f, false)
					.Where(cell => cell.InBounds(map))
					.Where(cell => cell.Standable(map))
					.Where(cell => cell.Fogged(map) == false)
					.Where(cell => cell.GetFirstPawn(map) == null)
					.OrderBy(cell => cell.DistanceToSquared(actorCell))
					.FirstOrDefault();
				if (zombieCell.IsValid == false)
				{
					return new
					{
						success = false,
						destroyedZombies,
						actor = DescribePawn(actor),
						actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
						error = "No nearby clear zombie cell was found."
					};
				}

				var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
				if (zombie == null)
				{
					return new
					{
						success = false,
						destroyedZombies,
						actor = DescribePawn(actor),
						actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
						zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
						error = "ZombieGenerator.SpawnZombie returned no zombie."
					};
				}

				zombie.state = ZombieState.Tracking;
				var realGrid = BuildAvoidGridForZombie(map, zombie);
				var zombieAvoidRadius = Tools.ZombieAvoidRadius(zombie);
				var formerDangerCells = GenRadial.RadialCellsAround(zombieCell, zombieAvoidRadius, true)
					.Where(cell => cell.InBounds(map))
					.ToArray();
				var zombieCostBeforeCleanup = AvoidCost(realGrid, map, zombieCell);
				var dangerCellsBeforeCleanup = formerDangerCells.Count(cell => realGrid.ShouldAvoid(map, cell));

				var destroyedAfterZombie = ZombieRuntimeActions.DestroyZombies(map);
				var clearedGrid = Tools.avoider.UpdateZombiePositionsImmediately(map, emptySpecs);
				var liveGridUnaffectedByEmptyRebuild = AvoidCost(realGrid, map, zombieCell) == zombieCostBeforeCleanup;
				tickManager.avoidGrid = clearedGrid;
				var zombieCostAfterCleanup = AvoidCost(clearedGrid, map, zombieCell);
				var dangerCellsAfterCleanup = formerDangerCells.Count(cell => clearedGrid.ShouldAvoid(map, cell));

				var fetchMethod = typeof(TickManager).GetMethod("FetchAvoidGrid", BindingFlags.Instance | BindingFlags.NonPublic);
				var flushMethod = typeof(TickManager).GetMethod("FlushRequestedAvoidGridRefresh", BindingFlags.Instance | BindingFlags.NonPublic);
				var avoidGridCounterField = typeof(TickManager).GetField("avoidGridCounter", BindingFlags.Instance | BindingFlags.NonPublic);
				var avoidGridRefreshRequestedField = typeof(TickManager).GetField("avoidGridRefreshRequested", BindingFlags.Instance | BindingFlags.NonPublic);
				var promptAvoidGridResultPendingField = typeof(TickManager).GetField("promptAvoidGridResultPending", BindingFlags.Instance | BindingFlags.NonPublic);
				var queueForMapMethod = typeof(ZombieAvoider).GetMethod("QueueForMap", BindingFlags.Instance | BindingFlags.NonPublic);
				if (fetchMethod == null || flushMethod == null || avoidGridCounterField == null || avoidGridRefreshRequestedField == null || promptAvoidGridResultPendingField == null || queueForMapMethod == null)
				{
					return new
					{
						success = false,
						error = "Avoid-grid reflection contract could not find FetchAvoidGrid, FlushRequestedAvoidGridRefresh, avoidGridCounter, avoidGridRefreshRequested, promptAvoidGridResultPending, or QueueForMap."
					};
				}

				AvoidGrid ManualGrid(long requestId, IntVec3 cell, int cost)
				{
					var grid = new AvoidGrid(map);
					var costs = grid.GetNewCosts();
					costs[cell.x + cell.z * map.Size.x] = cost;
					grid.requestId = requestId;
					grid.FinalizeCosts();
					return grid;
				}

				void QueueAndFetch(AvoidGrid grid)
				{
					var queue = (ConcurrentQueue<AvoidGrid>)queueForMapMethod.Invoke(Tools.avoider, new object[] { map });
					queue.Replace(grid);
					avoidGridCounterField.SetValue(tickManager, -1);
					fetchMethod.Invoke(tickManager, Array.Empty<object>());
				}

				bool PromptAvoidGridResultPending() => (bool)promptAvoidGridResultPendingField.GetValue(tickManager);
				bool AvoidGridRefreshRequested() => (bool)avoidGridRefreshRequestedField.GetValue(tickManager);
				bool FlushRequestedAvoidGridRefresh() => (bool)flushMethod.Invoke(tickManager, Array.Empty<object>());

				var exactRequestId = Math.Max(tickManager.lastAvoidGridRequestId, clearedGrid.requestId) + 1000;
				tickManager.avoidGrid = clearedGrid;
				tickManager.lastAvoidGridRequestId = exactRequestId;
				tickManager.lastAvoidGridResultId = exactRequestId - 2;
				tickManager.lastAvoidGridRequestTick = GenTicks.TicksGame;
				tickManager.lastAvoidGridResultTick = GenTicks.TicksGame;
				avoidGridRefreshRequestedField.SetValue(tickManager, false);
				promptAvoidGridResultPendingField.SetValue(tickManager, false);

				var intermediateResultGrid = ManualGrid(exactRequestId - 1, actor.Position, 3000);
				QueueAndFetch(intermediateResultGrid);
				var intermediateResultAccepted = ReferenceEquals(tickManager.avoidGrid, intermediateResultGrid)
					&& tickManager.lastAvoidGridResultId == exactRequestId - 1
					&& AvoidCost(tickManager.avoidGrid, map, actor.Position) == 3000;

				var exactResultGrid = ManualGrid(exactRequestId, actor.Position, 1234);
				QueueAndFetch(exactResultGrid);
				var exactResultReferenceAccepted = ReferenceEquals(tickManager.avoidGrid, exactResultGrid);
				var exactResultIdAfter = tickManager.lastAvoidGridResultId;
				var exactResultCostAfter = AvoidCost(tickManager.avoidGrid, map, actor.Position);
				var exactRefreshRequestedAfter = AvoidGridRefreshRequested();
				var exactPromptPendingAfter = PromptAvoidGridResultPending();
				var exactResultAccepted = exactResultReferenceAccepted
					&& exactResultIdAfter == exactRequestId
					&& exactResultCostAfter == 1234;

				var futureResultGrid = ManualGrid(exactRequestId + 1, actor.Position, 3000);
				QueueAndFetch(futureResultGrid);
				var futureResultRejected = ReferenceEquals(tickManager.avoidGrid, exactResultGrid)
					&& tickManager.lastAvoidGridResultId == exactRequestId
					&& AvoidCost(tickManager.avoidGrid, map, actor.Position) == 1234;
				var obsoleteResultGrid = ManualGrid(exactRequestId - 1, actor.Position, 5678);
				QueueAndFetch(obsoleteResultGrid);
				var obsoleteResultRejected = ReferenceEquals(tickManager.avoidGrid, exactResultGrid)
					&& tickManager.lastAvoidGridResultId == exactRequestId
					&& AvoidCost(tickManager.avoidGrid, map, actor.Position) == 1234;

				var coalesceCell = zombieCell;
				if (coalesceCell.GetFirstPawn(map) != null || coalesceCell.Standable(map) == false)
				{
					if (TryFindClearSpawnCell(map, actorCell, 8f, out var foundCoalesceCell, out var coalesceCellError) == false)
						return coalesceCellError;
					coalesceCell = foundCoalesceCell;
				}
				var coalesceZombie = ZombieRuntimeActions.SpawnZombie(coalesceCell, map, ZombieType.Normal, true);
				if (coalesceZombie == null)
				{
					return new
					{
						success = false,
						coalesceCell = ZombieRuntimeActions.DescribeCell(coalesceCell),
						error = "ZombieGenerator.SpawnZombie returned no zombie for the prompt coalescing fixture."
					};
				}
				coalesceZombie.state = ZombieState.Tracking;
				tickManager.allZombiesCached ??= new HashSet<Zombie>();
				tickManager.allZombiesCached.Add(coalesceZombie);

				var coalesceBaseGrid = Tools.avoider.UpdateZombiePositionsImmediately(map, emptySpecs);
				tickManager.avoidGrid = coalesceBaseGrid;
				tickManager.lastAvoidGridRequestId = coalesceBaseGrid.requestId;
				tickManager.lastAvoidGridResultId = coalesceBaseGrid.requestId;
				tickManager.lastAvoidGridRequestTick = GenTicks.TicksGame;
				tickManager.lastAvoidGridResultTick = GenTicks.TicksGame;
				avoidGridRefreshRequestedField.SetValue(tickManager, false);
				promptAvoidGridResultPendingField.SetValue(tickManager, false);
				tickManager.UpdateZombieAvoider(true);
				var pendingPromptRequestId = tickManager.lastAvoidGridRequestId;
				var pendingPromptResultPending = PromptAvoidGridResultPending();

				tickManager.RequestAvoidGridRefresh();
				var coalesceRequestedBeforeStaleFetch = AvoidGridRefreshRequested();
				var coalesceRequestIdBeforeStaleFetch = tickManager.lastAvoidGridRequestId;
				var coalescePromptPendingBeforeStaleFetch = PromptAvoidGridResultPending();
				var coalescedPromptResultGrid = ManualGrid(pendingPromptRequestId, actor.Position, 4321);
				QueueAndFetch(coalescedPromptResultGrid);
				var coalescedPromptResultAcceptedBeforeFlush = ReferenceEquals(tickManager.avoidGrid, coalescedPromptResultGrid)
					&& tickManager.lastAvoidGridResultId == pendingPromptRequestId
					&& PromptAvoidGridResultPending() == false
					&& AvoidGridRefreshRequested();

				var coalescedFlushReturned = FlushRequestedAvoidGridRefresh();
				var coalesceRequestIdAfterFlush = tickManager.lastAvoidGridRequestId;
				var coalescePromptPendingAfterFlush = PromptAvoidGridResultPending();
				var coalesceRequestedAfterFlush = AvoidGridRefreshRequested();

				var supersedingPromptResultGrid = ManualGrid(coalesceRequestIdAfterFlush, actor.Position, 1234);
				QueueAndFetch(supersedingPromptResultGrid);
				var supersedingPromptResultAccepted = ReferenceEquals(tickManager.avoidGrid, supersedingPromptResultGrid)
					&& tickManager.lastAvoidGridResultId == coalesceRequestIdAfterFlush
					&& PromptAvoidGridResultPending() == false;
				var coalesceZombieBeforeCleanup = DescribeZombie(coalesceZombie);

				var promptRefreshCoalescing = new
				{
					success = coalesceRequestedBeforeStaleFetch
						&& coalescedFlushReturned
						&& pendingPromptRequestId > 0
						&& pendingPromptResultPending
						&& coalesceRequestIdBeforeStaleFetch == pendingPromptRequestId
						&& coalescePromptPendingBeforeStaleFetch
						&& coalescedPromptResultAcceptedBeforeFlush
						&& coalesceRequestIdAfterFlush > coalesceRequestIdBeforeStaleFetch
						&& coalescePromptPendingAfterFlush
						&& coalesceRequestedAfterFlush == false
						&& supersedingPromptResultAccepted,
					coalesceZombie = coalesceZombieBeforeCleanup,
					coalesceCell = ZombieRuntimeActions.DescribeCell(coalesceCell),
					pendingPromptRequestId,
					pendingPromptResultPending,
					coalesceRequestedBeforeStaleFetch,
					coalesceRequestIdBeforeStaleFetch,
					coalescePromptPendingBeforeStaleFetch,
					coalescedPromptResultAcceptedBeforeFlush,
					coalescedFlushReturned,
					coalesceRequestIdAfterFlush,
					coalescePromptPendingAfterFlush,
					coalesceRequestedAfterFlush,
					supersedingPromptResultAccepted
				};

				coalesceZombie.state = ZombieState.Wandering;
				avoidGridRefreshRequestedField.SetValue(tickManager, false);
				promptAvoidGridResultPendingField.SetValue(tickManager, false);
				var stateFlipBaseRequestId = tickManager.lastAvoidGridRequestId;
				var stateFlipBaseResultId = tickManager.lastAvoidGridResultId;
				coalesceZombie.SetState(ZombieState.Tracking);
				coalesceZombie.SetState(ZombieState.Wandering);
				coalesceZombie.SetState(ZombieState.Wandering);
				var stateFlipRequestedBeforeFlush = AvoidGridRefreshRequested();
				var stateFlipRequestIdBeforeFlush = tickManager.lastAvoidGridRequestId;
				var stateFlipResultIdBeforeFlush = tickManager.lastAvoidGridResultId;
				var stateFlipPromptPendingBeforeFlush = PromptAvoidGridResultPending();
				var stateFlipFlushReturned = FlushRequestedAvoidGridRefresh();
				var stateFlipRequestIdAfterFlush = tickManager.lastAvoidGridRequestId;
				var stateFlipResultIdAfterFlush = tickManager.lastAvoidGridResultId;
				var stateFlipPromptPendingAfterFlush = PromptAvoidGridResultPending();
				var stateFlipRequestedAfterFlush = AvoidGridRefreshRequested();
				var stateFlipPromptResultGrid = ManualGrid(stateFlipRequestIdAfterFlush, actor.Position, 2468);
				QueueAndFetch(stateFlipPromptResultGrid);
				var stateFlipPromptResultAccepted = ReferenceEquals(tickManager.avoidGrid, stateFlipPromptResultGrid)
					&& tickManager.lastAvoidGridResultId == stateFlipRequestIdAfterFlush
					&& PromptAvoidGridResultPending() == false;

				var stateFlipRefreshCoalescing = new
				{
					success = stateFlipRequestedBeforeFlush
						&& stateFlipRequestIdBeforeFlush == stateFlipBaseRequestId
						&& stateFlipResultIdBeforeFlush == stateFlipBaseResultId
						&& stateFlipPromptPendingBeforeFlush == false
						&& stateFlipFlushReturned
						&& stateFlipRequestIdAfterFlush == stateFlipRequestIdBeforeFlush + 1
						&& stateFlipResultIdAfterFlush == stateFlipResultIdBeforeFlush
						&& stateFlipPromptPendingAfterFlush
						&& stateFlipRequestedAfterFlush == false
						&& stateFlipPromptResultAccepted,
					stateAfterFlips = coalesceZombie.state.ToString(),
					stateFlipBaseRequestId,
					stateFlipBaseResultId,
					stateFlipRequestedBeforeFlush,
					stateFlipRequestIdBeforeFlush,
					stateFlipResultIdBeforeFlush,
					stateFlipPromptPendingBeforeFlush,
					stateFlipFlushReturned,
					stateFlipRequestIdAfterFlush,
					stateFlipResultIdAfterFlush,
					stateFlipPromptPendingAfterFlush,
					stateFlipRequestedAfterFlush,
					stateFlipPromptResultAccepted
				};

				var snapshotReferencesAreDistinct = ReferenceEquals(emptyGrid, recoveredGrid) == false
					&& ReferenceEquals(realGrid, clearedGrid) == false
					&& ReferenceEquals(clearedGrid, exactResultGrid) == false;

				var coalesceZombieDestroyed = CleanupAvoidGridDeathRefreshThing(coalesceZombie);
				var coalesceZombieSpawnedAfterCleanup = coalesceZombie.Spawned && coalesceZombie.Destroyed == false;
				var liveZombiesAfterCoalesceCleanup = tickManager.AllZombies()
					.Where(liveZombie => liveZombie.Spawned && liveZombie.Dead == false)
					.ToHashSet();
				tickManager.allZombiesCached = liveZombiesAfterCoalesceCleanup;
				var finalGrid = BuildAvoidGridForZombies(map, liveZombiesAfterCoalesceCleanup);
				tickManager.lastAvoidGridRequestId = finalGrid.requestId;
				tickManager.lastAvoidGridResultId = finalGrid.requestId;
				tickManager.lastAvoidGridRequestTick = GenTicks.TicksGame;
				tickManager.lastAvoidGridResultTick = GenTicks.TicksGame;
				promptAvoidGridResultPendingField.SetValue(tickManager, false);
				avoidGridRefreshRequestedField.SetValue(tickManager, false);

				return new
				{
					success = poisonedInactiveCost > 0
						&& actorCostAfterRecovery == 0
						&& actorInDangerAfterRecovery == false
						&& dangerAfterRecovery != Danger.Deadly
						&& startedJob == JobDefOf.Wait.defName
						&& fleeTick == -1
						&& zombieCostBeforeCleanup > 0
						&& dangerCellsBeforeCleanup > 0
						&& liveGridUnaffectedByEmptyRebuild
						&& zombieCostAfterCleanup == 0
						&& dangerCellsAfterCleanup == 0
						&& snapshotReferencesAreDistinct
						&& intermediateResultAccepted
						&& futureResultRejected
						&& obsoleteResultRejected
						&& exactResultAccepted
						&& promptRefreshCoalescing.success
						&& stateFlipRefreshCoalescing.success
						&& coalesceZombieDestroyed
						&& coalesceZombieSpawnedAfterCleanup == false,
					destroyedZombies,
					destroyedAfterZombie,
					actor = DescribePawn(actor),
					zombie = DescribeZombie(zombie),
					actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
					zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
					poisonedInactiveCost,
					actorCostAfterRecovery,
					actorInDangerAfterRecovery,
					dangerAfterRecovery = dangerAfterRecovery.ToString(),
					startedJob,
					fleeTick,
					maxTicks,
					finalJob = actor.CurJobDef?.defName,
					finalJobPlayerForced = actor.CurJob?.playerForced,
					samples,
					zombieCostBeforeCleanup,
					dangerCellsBeforeCleanup,
					liveGridUnaffectedByEmptyRebuild,
					zombieCostAfterCleanup,
					dangerCellsAfterCleanup,
					snapshotReferencesAreDistinct,
					intermediateResultAccepted,
					futureResultRejected,
					obsoleteResultRejected,
					exactResultAccepted,
					exactResultReferenceAccepted,
					exactResultIdAfter,
					exactResultCostAfter,
					exactRefreshRequestedAfter,
					exactPromptPendingAfter,
					coalesceZombieDestroyed,
					coalesceZombieSpawnedAfterCleanup,
					liveZombiesAfterCoalesceCleanup = liveZombiesAfterCoalesceCleanup.Count,
					finalGridRequestId = finalGrid.requestId,
					promptRefreshCoalescing,
					stateFlipRefreshCoalescing
				};
			}
			finally
			{
				ZombieSettings.Values.betterZombieAvoidance = oldBetterAvoidance;
			}
		}

		[Tool("zombieland/workgiver_respects_avoid_grid", Description = "Verify a non-forced DoubleTap workgiver rejects an infected corpse in avoid danger while a forced command still creates the job.")]
		public static object WorkgiverRespectsAvoidGrid()
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

			var oldBetterAvoidance = ZombieSettings.Values.betterZombieAvoidance;
			var oldHours = ZombieSettings.Values.hoursAfterDeathToBecomeZombie;
			ZombieSettings.Values.betterZombieAvoidance = true;
			ZombieSettings.Values.hoursAfterDeathToBecomeZombie = Math.Max(1, oldHours);
			try
			{
				var destroyedZombies = ZombieRuntimeActions.DestroyZombies(map);
				foreach (var zombieCorpse in map.listerThings.AllThings.OfType<ZombieCorpse>().ToArray())
					zombieCorpse.Destroy();

				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var actorCell, out var actorSpawnError) == false)
					return actorSpawnError;

				var actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(actor, actorCell, map, Rot4.South);
				DisablePawnWork(actor);
				var config = ColonistSettings.Values.ConfigFor(actor);
				if (config != null)
					config.autoDoubleTap = true;

				var victimCell = GenRadial.RadialCellsAround(actor.Position, 14f, false)
					.Where(cell => cell.InBounds(map))
					.Where(cell => cell.Standable(map))
					.Where(cell => cell.Fogged(map) == false)
					.Where(cell => cell.DistanceTo(actor.Position) >= 10f)
					.Where(cell => cell.GetFirstPawn(map) == null)
					.OrderBy(cell => cell.DistanceToSquared(actor.Position))
					.FirstOrDefault();
				if (victimCell.IsValid == false)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						error = "No distant victim cell was found for the avoid-grid workgiver fixture."
					};
				}

				var victim = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(victim, victimCell, map, WipeMode.Vanish);
				if (ZombieRuntimeActions.AddZombieBite(victim, "final", out var bite, out var error) == false)
				{
					return new
					{
						success = false,
						victim = DescribePawn(victim),
						error
					};
				}

				if (ZombieRuntimeActions.KillPawnToCorpse(victim, out var corpse, out error) == false)
				{
					return new
					{
						success = false,
						victim = DescribePawn(victim),
						biteLabel = bite.LabelCap,
						error
					};
				}

				var zombieCell = GenRadial.RadialCellsAround(corpse.Position, 3f, false)
					.Where(cell => cell.InBounds(map))
					.Where(cell => cell.Standable(map))
					.Where(cell => cell.Fogged(map) == false)
					.Where(cell => cell.GetFirstPawn(map) == null)
					.OrderBy(cell => cell.DistanceToSquared(corpse.Position))
					.FirstOrDefault();
				if (zombieCell.IsValid == false)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						corpse = DescribeCorpse(corpse),
						error = "No nearby zombie cell was found for the avoid-grid workgiver fixture."
					};
				}

				var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
				if (zombie == null)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						corpse = DescribeCorpse(corpse),
						error = "ZombieGenerator.SpawnZombie returned no avoid-grid zombie."
					};
				}

				zombie.state = ZombieState.Tracking;
				var avoidGrid = BuildAvoidGridForZombie(map, zombie);
				var targetAvoidCost = AvoidCost(avoidGrid, map, corpse.Position);
				var targetShouldAvoid = avoidGrid.ShouldAvoid(map, corpse.Position);
				var actorShouldAvoid = avoidGrid.ShouldAvoid(map, actor.Position);

				var workGiver = new WorkGiver_DoubleTap();
				var hasUnforcedJob = workGiver.HasJobOnThing(actor, corpse, false);
				var unforcedJob = hasUnforcedJob ? workGiver.JobOnThing(actor, corpse, false) : null;
				var hasForcedJob = workGiver.HasJobOnThing(actor, corpse, true);
				var forcedJob = workGiver.JobOnThing(actor, corpse, true);

				return new
				{
					success = targetShouldAvoid
						&& actorShouldAvoid == false
						&& hasUnforcedJob == false
						&& unforcedJob == null
						&& hasForcedJob
						&& forcedJob?.def == CustomDefs.DoubleTap,
					destroyedZombies,
					actor = DescribePawn(actor),
					corpse = DescribeCorpse(corpse),
					zombie = DescribeZombie(zombie),
					actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
					victimCell = ZombieRuntimeActions.DescribeCell(victimCell),
					zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
					targetAvoidCost,
					targetShouldAvoid,
					actorShouldAvoid,
					hasUnforcedJob,
					unforcedJobDef = unforcedJob?.def?.defName,
					hasForcedJob,
					forcedJobDef = forcedJob?.def?.defName
				};
			}
			finally
			{
				ZombieSettings.Values.betterZombieAvoidance = oldBetterAvoidance;
				ZombieSettings.Values.hoursAfterDeathToBecomeZombie = oldHours;
			}
		}

		[Tool("zombieland/avoid_grid_blocks_door_and_danger", Description = "Verify avoid-grid danger affects vanilla door and danger checks for normal colonist behavior but not drafted or player-forced commands.")]
		public static object AvoidGridBlocksDoorAndDanger()
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

			var oldBetterAvoidance = ZombieSettings.Values.betterZombieAvoidance;
			ZombieSettings.Values.betterZombieAvoidance = true;
			try
			{
				var destroyedZombies = ZombieRuntimeActions.DestroyZombies(map);
				map.GetComponent<TickManager>().avoidGrid = new AvoidGrid(map);
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var actorCell, out var actorSpawnError) == false)
					return actorSpawnError;

				var actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(actor, actorCell, map, Rot4.South);
				DisablePawnWork(actor);
				var config = ColonistSettings.Values.ConfigFor(actor);
				if (config != null)
					config.autoAvoidZombies = true;

				var doorCell = GenRadial.RadialCellsAround(actor.Position, 14f, false)
					.Where(cell => cell.InBounds(map))
					.Where(cell => cell.Fogged(map) == false)
					.Where(cell => cell.GetEdifice(map) == null)
					.Where(cell => cell.GetFirstPawn(map) == null)
					.Where(cell => cell.DistanceTo(actor.Position) >= 10f)
					.OrderBy(cell => cell.DistanceToSquared(actor.Position))
					.FirstOrDefault();
				if (doorCell.IsValid == false)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						error = "No distant clear door cell was found for the avoid-grid fixture."
					};
				}

				var zombieCell = GenRadial.RadialCellsAround(doorCell, 3f, false)
					.Where(cell => cell.InBounds(map))
					.Where(cell => cell.Standable(map))
					.Where(cell => cell.Fogged(map) == false)
					.Where(cell => cell.GetFirstPawn(map) == null)
					.Where(cell => cell != doorCell)
					.OrderBy(cell => cell.DistanceToSquared(doorCell))
					.FirstOrDefault();
				if (zombieCell.IsValid == false)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						doorCell = ZombieRuntimeActions.DescribeCell(doorCell),
						error = "No nearby zombie cell was found for the avoid-grid door fixture."
					};
				}

				var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
				if (zombie == null)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						doorCell = ZombieRuntimeActions.DescribeCell(doorCell),
						error = "ZombieGenerator.SpawnZombie returned no avoid-grid zombie."
					};
				}
				zombie.state = ZombieState.Tracking;

				var avoidGrid = BuildAvoidGridForZombie(map, zombie);
				var doorAvoidCost = AvoidCost(avoidGrid, map, doorCell);
				var doorShouldAvoid = avoidGrid.ShouldAvoid(map, doorCell);
				var actorShouldAvoid = avoidGrid.ShouldAvoid(map, actor.Position);

				var door = ThingMaker.MakeThing(ThingDefOf.Door, GenStuff.DefaultStuffFor(ThingDefOf.Door)) as Building_Door;
				if (door == null)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						zombie = DescribeZombie(zombie),
						doorCell = ZombieRuntimeActions.DescribeCell(doorCell),
						error = "Could not create test door."
					};
				}
				GenSpawn.Spawn(door, doorCell, map, WipeMode.Vanish);
				door.SetFaction(Faction.OfPlayer);
				map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();

				actor.drafter.Drafted = false;
				actor.jobs.EndCurrentJob(JobCondition.InterruptForced);
				var normalDoorCanOpen = door.PawnCanOpen(actor);
				var normalDanger = doorCell.GetDangerFor(actor, map);

				actor.drafter.Drafted = true;
				var draftedDoorCanOpen = door.PawnCanOpen(actor);
				actor.drafter.Drafted = false;

				var forcedWait = JobMaker.MakeJob(JobDefOf.Wait);
				forcedWait.playerForced = true;
				actor.jobs.StartJob(forcedWait, JobCondition.InterruptForced, null, false, true);
				var forcedDoorCanOpen = door.PawnCanOpen(actor);
				var forcedDanger = doorCell.GetDangerFor(actor, map);

				return new
				{
					success = doorShouldAvoid
						&& actorShouldAvoid == false
						&& normalDoorCanOpen == false
						&& normalDanger == Danger.Deadly
						&& draftedDoorCanOpen
						&& forcedDoorCanOpen
						&& forcedDanger != Danger.Deadly,
					destroyedZombies,
					actor = DescribePawn(actor),
					zombie = DescribeZombie(zombie),
					door = new
					{
						id = ZombieRuntimeActions.StableThingId(door),
						defName = door.def?.defName,
						faction = door.Faction?.Name,
						position = ZombieRuntimeActions.DescribeCell(door.Position),
						freePassage = door.FreePassage
					},
					actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
					doorCell = ZombieRuntimeActions.DescribeCell(doorCell),
					zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
					doorAvoidCost,
					doorShouldAvoid,
					actorShouldAvoid,
					normalDoorCanOpen,
					normalDanger = normalDanger.ToString(),
					draftedDoorCanOpen,
					forcedDoorCanOpen,
					forcedDanger = forcedDanger.ToString(),
					forcedJob = actor.CurJobDef?.defName,
					forcedJobPlayerForced = actor.CurJob?.playerForced
				};
			}
			finally
			{
				ZombieSettings.Values.betterZombieAvoidance = oldBetterAvoidance;
			}
		}

		[Tool("zombieland/avoid_grid_interrupts_existing_path", Description = "Verify an already-started colonist path asks for a new path when its source-derived lookahead cell becomes zombie avoid danger.")]
		public static object AvoidGridInterruptsExistingPath()
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

			var oldBetterAvoidance = ZombieSettings.Values.betterZombieAvoidance;
			ZombieSettings.Values.betterZombieAvoidance = true;
			try
			{
				var destroyedZombies = ZombieRuntimeActions.DestroyZombies(map);
				map.GetComponent<TickManager>().avoidGrid = new AvoidGrid(map);
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var actorCell, out var actorSpawnError) == false)
					return actorSpawnError;

				var actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(actor, actorCell, map, Rot4.South);
				DisablePawnWork(actor);
				var config = ColonistSettings.Values.ConfigFor(actor);
				if (config != null)
					config.autoAvoidZombies = true;

				var destination = GenRadial.RadialCellsAround(actor.Position, 18f, false)
					.Where(cell => cell.InBounds(map))
					.Where(cell => cell.Standable(map))
					.Where(cell => cell.Fogged(map) == false)
					.Where(cell => cell.GetFirstPawn(map) == null)
					.Where(cell => cell.DistanceTo(actor.Position) >= 14f)
					.Where(cell => actor.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
					.OrderByDescending(cell => cell.DistanceToSquared(actor.Position))
					.FirstOrDefault();
				if (destination.IsValid == false)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						error = "No reachable distant destination was found for the avoid-grid path fixture."
					};
				}

				var gotoJob = JobMaker.MakeJob(JobDefOf.Goto, destination);
				gotoJob.playerForced = false;
				var startedJob = actor.jobs.TryTakeOrderedJob(gotoJob, JobTag.Misc, false);
				if (startedJob == false)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						destination = ZombieRuntimeActions.DescribeCell(destination),
						error = "Could not start the real Goto job for the avoid-grid path fixture."
					};
				}

				const int maxPathTicks = 60;
				var pathReadyTick = -1;
				for (var tick = 0; tick <= maxPathTicks; tick++)
				{
					if (actor.pather.curPath?.Found == true && actor.pather.curPath.NodesLeftCount >= 6)
					{
						pathReadyTick = tick;
						break;
					}
					AdvanceGameTicks(1);
				}

				var path = actor.pather.curPath;
				if (path?.Found != true || path.NodesLeftCount < 6)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						destination = ZombieRuntimeActions.DescribeCell(destination),
						pathReadyTick,
						nodesLeft = path?.NodesLeftCount ?? 0,
						error = "Pawn path did not become available with enough nodes for the lookahead fixture."
					};
				}

				var lookAhead = path.Peek(4);
				var lastNode = path.LastNode;
				if ((lookAhead - lastNode).LengthHorizontalSquared < 25)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						destination = ZombieRuntimeActions.DescribeCell(destination),
						lookAhead = ZombieRuntimeActions.DescribeCell(lookAhead),
						lastNode = ZombieRuntimeActions.DescribeCell(lastNode),
						nodesLeft = path.NodesLeftCount,
						error = "Source-derived lookahead cell was too close to destination for the NeedNewPath patch."
					};
				}

				var needNewPathBefore = actor.pather.NeedNewPath();
				var pathCells = Enumerable.Range(0, path.NodesLeftCount)
					.Select(path.Peek)
					.ToHashSet();
				var zombieCell = GenRadial.RadialCellsAround(lookAhead, 3f, false)
					.Where(cell => cell.InBounds(map))
					.Where(cell => cell.Standable(map))
					.Where(cell => cell.Fogged(map) == false)
					.Where(cell => cell.GetFirstPawn(map) == null)
					.Where(cell => pathCells.Contains(cell) == false)
					.OrderBy(cell => cell.DistanceToSquared(lookAhead))
					.FirstOrDefault();
				if (zombieCell.IsValid == false)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						lookAhead = ZombieRuntimeActions.DescribeCell(lookAhead),
						nodesLeft = path.NodesLeftCount,
						needNewPathBefore,
						error = "No off-path zombie cell was found near the lookahead cell."
					};
				}

				var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
				if (zombie == null)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
						lookAhead = ZombieRuntimeActions.DescribeCell(lookAhead),
						error = "ZombieGenerator.SpawnZombie returned no avoid-grid zombie."
					};
				}
				zombie.state = ZombieState.Tracking;
				var avoidGrid = BuildAvoidGridForZombie(map, zombie);
				var lookAheadAvoidCost = AvoidCost(avoidGrid, map, lookAhead);
				var lookAheadShouldAvoid = avoidGrid.ShouldAvoid(map, lookAhead);
				var needNewPathAfter = actor.pather.NeedNewPath();

				return new
				{
					success = needNewPathBefore == false
						&& lookAheadShouldAvoid
						&& needNewPathAfter,
					destroyedZombies,
					startedJob,
					actor = DescribePawn(actor),
					zombie = DescribeZombie(zombie),
					destination = ZombieRuntimeActions.DescribeCell(destination),
					lookAhead = ZombieRuntimeActions.DescribeCell(lookAhead),
					zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
					lastNode = ZombieRuntimeActions.DescribeCell(lastNode),
					pathReadyTick,
					nodesLeft = path.NodesLeftCount,
					lookAheadAvoidCost,
					lookAheadShouldAvoid,
					needNewPathBefore,
					needNewPathAfter
				};
			}
			finally
			{
				ZombieSettings.Values.betterZombieAvoidance = oldBetterAvoidance;
			}
		}

		[Tool("zombieland/avoid_grid_costs_route_new_path", Description = "Verify RimWorld 1.6 path requests, avoid-grid costs, and key Pawn_PathFollower.StartPath zombie branches.")]
		public static object AvoidGridCostsRouteNewPath()
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

			var oldBetterAvoidance = ZombieSettings.Values.betterZombieAvoidance;
			Pawn actor = null;
			Zombie zombie = null;
			ZombieSettings.Values.betterZombieAvoidance = false;
			try
			{
				var destroyedZombies = ZombieRuntimeActions.DestroyZombies(map);
				map.GetComponent<TickManager>().avoidGrid = new AvoidGrid(map);
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				var actorCell = map.AllCells
					.Where(cell => cell.Standable(map)
						&& cell.Fogged(map) == false
						&& cell.GetFirstPawn(map) == null
						&& cell.GetRoom(map)?.IsHuge == true)
					.OrderBy(cell => cell.DistanceToSquared(root))
					.DefaultIfEmpty(IntVec3.Invalid)
					.First();
				if (actorCell.IsValid == false)
					return new { success = false, error = "No clear cell in a large reachable region was found for the avoid-grid route fixture." };

				actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(actor, actorCell, map, Rot4.South);
				DisablePawnWork(actor);
				var config = ColonistSettings.Values.ConfigFor(actor);
				if (config != null)
					config.autoAvoidZombies = false;

				var destination = GenRadial.RadialCellsAround(actor.Position, 45f, false)
					.Where(cell => cell.InBounds(map))
					.Where(cell => cell.Standable(map))
					.Where(cell => cell.Fogged(map) == false)
					.Where(cell => cell.GetFirstPawn(map) == null)
					.Where(cell => cell.DistanceTo(actor.Position) >= 12f)
					.Where(cell => actor.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
					.OrderByDescending(cell => cell.DistanceToSquared(actor.Position))
					.DefaultIfEmpty(IntVec3.Invalid)
					.First();
				if (destination.IsValid == false)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						error = "No reachable distant destination was found for the avoid-grid route fixture."
					};
				}

				var baselinePath = map.pathFinder.FindPathNow(actor.Position, destination, actor, null, PathEndMode.OnCell);
				var baselineCells = DescribePathCells(baselinePath);
				if (baselinePath?.Found != true || baselineCells.Length < 10)
				{
					baselinePath?.ReleaseToPool();
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						destination = ZombieRuntimeActions.DescribeCell(destination),
						baselinePathFound = baselinePath?.Found ?? false,
						baselineCells = baselineCells.Length,
						error = "Baseline path did not become available with enough cells for the avoid-grid route fixture."
					};
				}

				var zombieCell = baselineCells
					.Skip(Math.Max(2, baselineCells.Length / 3))
					.Take(Math.Max(1, baselineCells.Length / 3))
					.Where(cell => cell.InBounds(map))
					.Where(cell => cell.Standable(map))
					.Where(cell => cell.Fogged(map) == false)
					.Where(cell => cell.GetFirstPawn(map) == null)
					.Where(cell => cell.DistanceTo(actor.Position) >= 6f)
					.Where(cell => cell.DistanceTo(destination) >= 6f)
					.DefaultIfEmpty(IntVec3.Invalid)
					.First();
				if (zombieCell.IsValid == false)
				{
					baselinePath.ReleaseToPool();
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						destination = ZombieRuntimeActions.DescribeCell(destination),
						baselineCells = baselineCells.Length,
						error = "No usable zombie cell was found on the baseline path."
					};
				}

				zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
				if (zombie == null)
				{
					baselinePath.ReleaseToPool();
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
						error = "ZombieGenerator.SpawnZombie returned no avoid-grid route zombie."
					};
				}
				zombie.state = ZombieState.Tracking;
				var avoidGrid = BuildAvoidGridForZombie(map, zombie);
				var baselineAvoidCells = baselineCells.Count(cell => avoidGrid.ShouldAvoid(map, cell));
				var baselineAvoidCost = baselineCells.Sum(cell => AvoidCost(avoidGrid, map, cell));
				var nativeAllocationsBefore = AvoidGrid.NativeSnapshotAllocationCount;
				var nativeCopiedCellsBefore = AvoidGrid.NativeSnapshotCopiedCellCount;
				var customizersBefore = AvoidGrid.PathCustomizerCreationCount;

				ZombieSettings.Values.betterZombieAvoidance = true;
				if (config != null)
					config.autoAvoidZombies = true;

				var avoidedPath = map.pathFinder.FindPathNow(actor.Position, destination, actor, null, PathEndMode.OnCell);
				var avoidedCells = DescribePathCells(avoidedPath);
				var avoidedAvoidCells = avoidedCells.Count(cell => avoidGrid.ShouldAvoid(map, cell));
				var avoidedAvoidCost = avoidedCells.Sum(cell => AvoidCost(avoidGrid, map, cell));
				var avoidedPathFound = avoidedPath?.Found == true;
				var asyncPathRequest = VerifyAsyncAvoidGridPathRequest(map, actor, destination);
				var nativeAllocationsAfter = AvoidGrid.NativeSnapshotAllocationCount;
				var nativeCopiedCellsAfter = AvoidGrid.NativeSnapshotCopiedCellCount;
				var customizersAfter = AvoidGrid.PathCustomizerCreationCount;
				var nativeAllocationDelta = nativeAllocationsAfter - nativeAllocationsBefore;
				var nativeCopiedCellDelta = nativeCopiedCellsAfter - nativeCopiedCellsBefore;
				var customizerDelta = customizersAfter - customizersBefore;
				var snapshotActiveBeforeDisable = HasActiveAvoidGridSnapshot(map.pathFinder);
				ZombieSettings.Values.betterZombieAvoidance = false;
				if (config != null)
					config.autoAvoidZombies = false;
				var disabledPath = map.pathFinder.FindPathNow(actor.Position, destination, actor, null, PathEndMode.OnCell);
				disabledPath?.ReleaseToPool();
				var snapshotActiveAfterDisable = HasActiveAvoidGridSnapshot(map.pathFinder);
				ZombieSettings.Values.betterZombieAvoidance = true;
				if (config != null)
					config.autoAvoidZombies = true;
				var startPath = VerifyPawnPathFollowerStartPathPatch(map, actorCell + new IntVec3(7, 0, 7));
				baselinePath.ReleaseToPool();
				avoidedPath?.ReleaseToPool();

				return new
				{
					success = avoidedPathFound
						&& baselineAvoidCells > 0
						&& avoidedAvoidCells < baselineAvoidCells
						&& avoidedAvoidCost < baselineAvoidCost
						&& nativeAllocationDelta == 1
						&& nativeCopiedCellDelta == map.cellIndices.NumGridCells
						&& customizerDelta == 4
						&& snapshotActiveBeforeDisable == true
						&& snapshotActiveAfterDisable == false
						&& ObjectSuccess(asyncPathRequest)
						&& ObjectSuccess(startPath),
					destroyedZombies,
					actor = DescribePawn(actor),
					zombie = DescribeZombie(zombie),
					destination = ZombieRuntimeActions.DescribeCell(destination),
					zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
					baseline = new
					{
						pathFound = true,
						cellCount = baselineCells.Length,
						avoidCells = baselineAvoidCells,
						avoidCost = baselineAvoidCost
					},
					avoided = new
					{
						pathFound = avoidedPathFound,
						cellCount = avoidedCells.Length,
						avoidCells = avoidedAvoidCells,
						avoidCost = avoidedAvoidCost
					},
					nativeSnapshot = new
					{
						allocationsBefore = nativeAllocationsBefore,
						allocationsAfter = nativeAllocationsAfter,
						allocationDelta = nativeAllocationDelta,
						copiedCellsBefore = nativeCopiedCellsBefore,
						copiedCellsAfter = nativeCopiedCellsAfter,
						copiedCellDelta = nativeCopiedCellDelta,
						expectedCopiedCells = map.cellIndices.NumGridCells,
						customizersBefore,
						customizersAfter,
						customizerDelta,
						snapshotActiveBeforeDisable,
						snapshotActiveAfterDisable
					},
					asyncPathRequest,
					startPath
				};
			}
			finally
			{
				if (zombie?.Destroyed == false)
					zombie.Destroy(DestroyMode.Vanish);
				if (actor?.Destroyed == false)
					actor.Destroy(DestroyMode.Vanish);
				ZombieSettings.Values.betterZombieAvoidance = oldBetterAvoidance;
			}
		}

		static bool? HasActiveAvoidGridSnapshot(PathFinder pathFinder)
		{
			var registry = AccessTools.TypeByName("ZombieLand.Patches+ZombieAvoidGridPathCustomizerRegistry");
			var snapshots = registry == null ? null : AccessTools.Field(registry, "activeSnapshots")?.GetValue(null) as System.Collections.IDictionary;
			return snapshots == null ? null : (bool?)snapshots.Contains(pathFinder);
		}

		class ProbePathGridCustomizer : PathRequest.IPathGridCustomizer, IDisposable
		{
			NativeArray<ushort> offsets;

			public ProbePathGridCustomizer(Map map)
			{
				offsets = new NativeArray<ushort>(map.cellIndices.NumGridCells, Allocator.Persistent);
			}

			public NativeArray<ushort> GetOffsetGrid()
			{
				return offsets;
			}

			public void Dispose()
			{
				if (offsets.IsCreated)
					offsets.Dispose();
			}
		}

		static object VerifyAsyncAvoidGridPathRequest(Map map, Pawn actor, IntVec3 destination)
		{
			var customizerField = typeof(PathRequest).GetField("customizer", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (customizerField == null)
			{
				return new
				{
					success = false,
					error = "PathRequest.customizer field was not found."
				};
			}

			PathRequest injectedResolveRequest = null;
			PathRequest injectedDisposeRequest = null;
			PathRequest scheduledCancelledRequest = null;
			PathRequest existingRequest = null;
			ProbePathGridCustomizer probeCustomizer = null;
			try
			{
				var traverseParms = TraverseParms.For(actor, Danger.Deadly, TraverseMode.ByPawn);
				injectedResolveRequest = map.pathFinder.CreateRequest(actor.Position, destination, null, traverseParms, null, PathEndMode.OnCell, actor, null);
				var injectedResolveCustomizer = customizerField.GetValue(injectedResolveRequest);
				var injectedResolveType = injectedResolveCustomizer?.GetType().Name;
				var injectedResolveGeneration = CustomizerGenerationId(injectedResolveCustomizer);
				var injectedResolveBefore = IsCustomizerGridCreated(injectedResolveCustomizer);
				injectedResolveRequest.Resolve(null);
				var injectedResolveAfter = IsCustomizerGridCreated(injectedResolveCustomizer);
				var injectedResolveReleaseState = CustomizerReleaseState(injectedResolveCustomizer);
				injectedResolveRequest = null;

				injectedDisposeRequest = map.pathFinder.CreateRequest(actor.Position, destination, null, traverseParms, null, PathEndMode.OnCell, actor, null);
				var injectedDisposeCustomizer = customizerField.GetValue(injectedDisposeRequest);
				var injectedDisposeType = injectedDisposeCustomizer?.GetType().Name;
				var injectedDisposeGeneration = CustomizerGenerationId(injectedDisposeCustomizer);
				var injectedDisposeBefore = IsCustomizerGridCreated(injectedDisposeCustomizer);
				injectedDisposeRequest.Dispose();
				var injectedDisposeAfter = IsCustomizerGridCreated(injectedDisposeCustomizer);
				var injectedDisposeDeferredState = CustomizerReleaseState(injectedDisposeCustomizer);
				injectedDisposeRequest = null;

				var scheduledPathsField = typeof(PathFinder).GetField("scheduledPathJobs", BindingFlags.Instance | BindingFlags.NonPublic);
				scheduledCancelledRequest = map.pathFinder.CreateRequest(actor.Position, destination, null, traverseParms, null, PathEndMode.OnCell, actor, null);
				var scheduledCustomizer = customizerField.GetValue(scheduledCancelledRequest);
				var scheduledGeneration = CustomizerGenerationId(scheduledCustomizer);
				map.pathFinder.PushRequest(scheduledCancelledRequest);
				map.pathFinder.PathFinderTick();
				var scheduledPathCount = (scheduledPathsField?.GetValue(map.pathFinder) as System.Collections.ICollection)?.Count ?? -1;
				var scheduledGridCreatedBeforeDispose = IsCustomizerGridCreated(scheduledCustomizer);
				scheduledCancelledRequest.Dispose();
				var scheduledCancelled = scheduledCancelledRequest.Cancelled;
				var scheduledReleaseStateAfterDispose = CustomizerReleaseState(scheduledCustomizer);
				var scheduledGridCreatedAfterDispose = IsCustomizerGridCreated(scheduledCustomizer);
				map.pathFinder.PathFinderTick();
				var scheduledReleaseStateAfterCompletion = CustomizerReleaseState(scheduledCustomizer);
				var scheduledGridCreatedAfterCompletion = IsCustomizerGridCreated(scheduledCustomizer);
				var disposeReleaseStateAfterCompletion = CustomizerReleaseState(injectedDisposeCustomizer);
				scheduledCancelledRequest = null;

				probeCustomizer = new ProbePathGridCustomizer(map);
				existingRequest = map.pathFinder.CreateRequest(actor.Position, destination, null, traverseParms, null, PathEndMode.OnCell, actor, probeCustomizer);
				var existingCustomizer = customizerField.GetValue(existingRequest);
				var existingPreserved = ReferenceEquals(existingCustomizer, probeCustomizer);
				existingRequest.Dispose();
				existingRequest = null;
				var probeStillCreated = probeCustomizer.GetOffsetGrid().IsCreated;
				probeCustomizer.Dispose();
				var probeDisposedManually = probeCustomizer.GetOffsetGrid().IsCreated == false;
				probeCustomizer = null;

				var resolveInjected = injectedResolveCustomizer != null && injectedResolveType == "ZombieAvoidGridPathCustomizer";
				var disposeInjected = injectedDisposeCustomizer != null && injectedDisposeType == "ZombieAvoidGridPathCustomizer";
				return new
				{
					success = resolveInjected
						&& injectedResolveBefore
						&& injectedResolveAfter
						&& injectedResolveReleaseState == 2
						&& disposeInjected
						&& injectedDisposeBefore
						&& injectedDisposeAfter
						&& injectedDisposeDeferredState == 1
						&& disposeReleaseStateAfterCompletion == 2
						&& injectedResolveGeneration > 0
						&& injectedResolveGeneration == injectedDisposeGeneration
						&& injectedResolveGeneration == scheduledGeneration
						&& scheduledPathCount > 0
						&& scheduledGridCreatedBeforeDispose
						&& scheduledCancelled
						&& scheduledReleaseStateAfterDispose == 1
						&& scheduledGridCreatedAfterDispose
						&& scheduledReleaseStateAfterCompletion == 2
						&& scheduledGridCreatedAfterCompletion
						&& existingPreserved
						&& probeStillCreated
						&& probeDisposedManually,
					resolve = new
					{
						injected = resolveInjected,
						customizerType = injectedResolveType,
						generation = injectedResolveGeneration,
						offsetGridCreatedBefore = injectedResolveBefore,
						offsetGridCreatedAfterResolve = injectedResolveAfter,
						releaseStateAfterResolve = injectedResolveReleaseState
					},
					dispose = new
					{
						injected = disposeInjected,
						customizerType = injectedDisposeType,
						generation = injectedDisposeGeneration,
						offsetGridCreatedBefore = injectedDisposeBefore,
						offsetGridCreatedAfterDispose = injectedDisposeAfter,
						releaseStateAfterDispose = injectedDisposeDeferredState,
						releaseStateAfterNextJobCompletion = disposeReleaseStateAfterCompletion
					},
					cancelledScheduled = new
					{
						generation = scheduledGeneration,
						scheduledPathCount,
						gridCreatedBeforeDispose = scheduledGridCreatedBeforeDispose,
						scheduledCancelled,
						releaseStateAfterDispose = scheduledReleaseStateAfterDispose,
						gridCreatedAfterDispose = scheduledGridCreatedAfterDispose,
						releaseStateAfterCompletion = scheduledReleaseStateAfterCompletion,
						gridCreatedAfterCompletion = scheduledGridCreatedAfterCompletion
					},
					existing = new
					{
						preserved = existingPreserved,
						probeStillCreatedAfterRequestDispose = probeStillCreated,
						probeDisposedManually
					}
				};
			}
			finally
			{
				injectedResolveRequest?.Dispose();
				injectedDisposeRequest?.Dispose();
				scheduledCancelledRequest?.Dispose();
				existingRequest?.Dispose();
				probeCustomizer?.Dispose();
			}
		}

		static object VerifyPawnPathFollowerStartPathPatch(Map map, IntVec3 root)
		{
			var patchOwners = PatchOwners(typeof(Pawn_PathFollower), nameof(Pawn_PathFollower.StartPath));
			var helperProbe = VerifyStartPathHelperBranches(map, root);
			var downedPosture = VerifyStartPathDownedZombiePosture(map, root + new IntVec3(6, 0, 0));

			return new
			{
				success = patchOwners.Contains("net.pardeike.zombieland")
					&& ObjectSuccess(helperProbe)
					&& ObjectSuccess(downedPosture),
				patchOwners,
				helperProbe,
				downedPosture
			};
		}

		static object VerifyStartPathHelperBranches(Map map, IntVec3 root)
		{
			var patchType = typeof(Patches).GetNestedType("Pawn_PathFollower_StartPath_Patch", BindingFlags.NonPublic);
			var helper = patchType?.GetMethod("ThingDestroyedAndNotZombie", BindingFlags.Static | BindingFlags.NonPublic);
			if (helper == null)
			{
				return new
				{
					success = false,
					error = "Could not reflect Pawn_PathFollower_StartPath_Patch.ThingDestroyedAndNotZombie."
				};
			}

			if (TryFindClearSpawnCell(map, root, 12f, out var zombieCell, out var zombieSpawnError) == false)
				return zombieSpawnError;
			if (TryFindClearSpawnCell(map, zombieCell + new IntVec3(3, 0, 0), 12f, out var colonistCell, out var colonistSpawnError) == false)
				return colonistSpawnError;
			if (TryFindClearSpawnCell(map, zombieCell + new IntVec3(-3, 0, 0), 12f, out var actorCell, out var actorSpawnError) == false)
				return actorSpawnError;

			var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
			var colonist = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(colonist, colonistCell, map, Rot4.South);
			GenSpawn.Spawn(actor, actorCell, map, Rot4.South);
			DisablePawnWork(colonist);
			DisablePawnWork(actor);
			if (zombie == null)
			{
				return new
				{
					success = false,
					zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
					error = "ZombieGenerator.SpawnZombie returned no StartPath helper zombie."
				};
			}

			var zombieTarget = new LocalTargetInfo(zombie);
			var colonistTarget = new LocalTargetInfo(colonist);
			zombie.Destroy(DestroyMode.Vanish);
			colonist.Destroy(DestroyMode.Vanish);

			var destroyedZombieBlocked = (bool)helper.Invoke(null, new object[] { zombieTarget });
			var destroyedColonistBlocked = (bool)helper.Invoke(null, new object[] { colonistTarget });
			actor.pather.StartPath(zombieTarget, PathEndMode.ClosestTouch);
			var actorMovingAfterDestroyedZombieTarget = actor.pather.Moving;

			return new
			{
				success = destroyedZombieBlocked == false && destroyedColonistBlocked,
				destroyedZombie = new
				{
					thingDestroyed = zombieTarget.ThingDestroyed,
					helperBlocked = destroyedZombieBlocked
				},
				destroyedColonist = new
				{
					thingDestroyed = colonistTarget.ThingDestroyed,
					helperBlocked = destroyedColonistBlocked
				},
				realStartPathToDestroyedZombieTarget = new
				{
					called = true,
					actorMovingAfterDestroyedZombieTarget
				}
			};
		}

		static object VerifyStartPathDownedZombiePosture(Map map, IntVec3 root)
		{
			if (TryFindClearSpawnCell(map, root, 12f, out var zombieCell, out var zombieSpawnError) == false)
				return zombieSpawnError;
			var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
			if (zombie == null)
			{
				return new
				{
					success = false,
					zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
					error = "ZombieGenerator.SpawnZombie returned no StartPath posture zombie."
				};
			}
			if (TryFindClearSpawnCell(map, zombieCell + new IntVec3(6, 0, 0), 16f, out var destination, out var destinationError) == false)
				return destinationError;

			var oldDoubleTapRequired = ZombieSettings.Values.doubleTapRequired;
			try
			{
				ZombieSettings.Values.doubleTapRequired = true;
				if (TryMakeDownedForCombat(zombie, out var downedError) == false)
				{
					return new
					{
						success = false,
						zombie = DescribeZombie(zombie),
						error = downedError
					};
				}

				var healthDowned = zombie.health.Downed;
				var publicDowned = zombie.Downed;
				zombie.jobs.posture = PawnPosture.Standing;
				zombie.pather.StopDead();
				zombie.pather.StartPath(destination, PathEndMode.OnCell);
				var postureAfterStartPath = zombie.jobs.posture;

				return new
				{
					success = healthDowned
						&& publicDowned == false
						&& postureAfterStartPath == PawnPosture.LayingOnGroundNormal,
					zombie = DescribeZombie(zombie),
					destination = ZombieRuntimeActions.DescribeCell(destination),
					healthDowned,
					publicDowned,
					postureAfterStartPath = postureAfterStartPath.ToString(),
					movingAfterStartPath = zombie.pather.Moving
				};
			}
			finally
			{
				ZombieSettings.Values.doubleTapRequired = oldDoubleTapRequired;
			}
		}

		static bool IsCustomizerGridCreated(object customizer)
		{
			if (customizer == null)
				return false;
			var getter = customizer.GetType().GetMethod("GetOffsetGrid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (getter?.Invoke(customizer, Array.Empty<object>()) is NativeArray<ushort> offsets)
				return offsets.IsCreated;
			return false;
		}

		static long CustomizerGenerationId(object customizer)
		{
			return (long)(customizer?.GetType().GetProperty("GenerationId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(customizer) ?? -1L);
		}

		static int CustomizerReleaseState(object customizer)
		{
			return (int)(customizer?.GetType().GetField("releaseState", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(customizer) ?? -1);
		}

		[Tool("zombieland/zombie_manual_door_close_ignored", Description = "Verify a zombie cannot manually schedule a door to close while a normal colonist still can.")]
		public static object ZombieManualDoorCloseIgnored()
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

			var doorCell = GenRadial.RadialCellsAround(actorCell, 8f, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetEdifice(map) == null)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.OrderBy(cell => cell.DistanceToSquared(actorCell))
				.FirstOrDefault();
			if (doorCell.IsValid == false)
			{
				return new
				{
					success = false,
					actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
					error = "No clear door cell was found for the zombie manual-close fixture."
				};
			}

			var zombieCell = GenRadial.RadialCellsAround(doorCell, 3f, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.Where(cell => cell != doorCell)
				.OrderBy(cell => cell.DistanceToSquared(doorCell))
				.FirstOrDefault();
			if (zombieCell.IsValid == false)
			{
				return new
				{
					success = false,
					doorCell = ZombieRuntimeActions.DescribeCell(doorCell),
					error = "No nearby zombie cell was found for the zombie manual-close fixture."
				};
			}

			var actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(actor, actorCell, map, Rot4.South);
			DisablePawnWork(actor);
			var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
			if (zombie == null)
			{
				return new
				{
					success = false,
					actor = DescribePawn(actor),
					zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
					error = "ZombieGenerator.SpawnZombie returned no door-close zombie."
				};
			}

			var door = ThingMaker.MakeThing(ThingDefOf.Door, GenStuff.DefaultStuffFor(ThingDefOf.Door)) as Building_Door;
			if (door == null)
			{
				return new
				{
					success = false,
					actor = DescribePawn(actor),
					zombie = DescribeZombie(zombie),
					error = "Could not create test door."
				};
			}
			GenSpawn.Spawn(door, doorCell, map, WipeMode.Vanish);
			door.SetFaction(Faction.OfPlayer);
			map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
			door.StartManualOpenBy(actor);

			var ticksUntilCloseField = typeof(Building_Door).GetField("ticksUntilClose", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			if (ticksUntilCloseField == null)
			{
				return new
				{
					success = false,
					door = ZombieRuntimeActions.StableThingId(door),
					error = "Could not access Building_Door.ticksUntilClose."
				};
			}

			const int sentinelTicksUntilClose = 12345;
			ticksUntilCloseField.SetValue(door, sentinelTicksUntilClose);
			door.StartManualCloseBy(zombie);
			var ticksAfterZombie = (int)ticksUntilCloseField.GetValue(door);
			door.StartManualCloseBy(actor);
			var ticksAfterActor = (int)ticksUntilCloseField.GetValue(door);

			return new
			{
				success = door.Open
					&& ticksAfterZombie == sentinelTicksUntilClose
					&& ticksAfterActor != sentinelTicksUntilClose,
				destroyedZombies,
				actor = DescribePawn(actor),
				zombie = DescribeZombie(zombie),
				door = new
				{
					id = ZombieRuntimeActions.StableThingId(door),
					defName = door.def?.defName,
					faction = door.Faction?.Name,
					position = ZombieRuntimeActions.DescribeCell(door.Position),
					door.Open
				},
				actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
				doorCell = ZombieRuntimeActions.DescribeCell(doorCell),
				zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
				sentinelTicksUntilClose,
				ticksAfterZombie,
				ticksAfterActor
			};
		}

		[Tool("zombieland/albino_does_not_hold_door_open", Description = "Verify an albino zombie in an open door does not reset the auto-close delay while a normal zombie still does.")]
		public static object AlbinoDoesNotHoldDoorOpen()
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

			var ticksUntilCloseField = typeof(Building_Door).GetField("ticksUntilClose", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			if (ticksUntilCloseField == null)
			{
				return new
				{
					success = false,
					error = "Could not access Building_Door.ticksUntilClose."
				};
			}

			var destroyedZombies = ZombieRuntimeActions.DestroyZombies(map);
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var normalDoorCell, out var spawnError) == false)
				return spawnError;

			var albinoDoorCell = GenRadial.RadialCellsAround(normalDoorCell, 8f, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetEdifice(map) == null)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.Where(cell => cell.DistanceTo(normalDoorCell) >= 2f)
				.OrderBy(cell => cell.DistanceToSquared(normalDoorCell))
				.FirstOrDefault();
			if (albinoDoorCell.IsValid == false)
			{
				return new
				{
					success = false,
					normalDoorCell = ZombieRuntimeActions.DescribeCell(normalDoorCell),
					error = "No second clear door cell was found for the albino door fixture."
				};
			}

			var actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var actorCell = GenRadial.RadialCellsAround(normalDoorCell, 4f, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.OrderByDescending(cell => cell.DistanceToSquared(normalDoorCell))
				.FirstOrDefault();
			if (actorCell.IsValid == false)
				actorCell = normalDoorCell;
			GenSpawn.Spawn(actor, actorCell, map, Rot4.South);
			DisablePawnWork(actor);

			var normalDoor = ThingMaker.MakeThing(ThingDefOf.Door, GenStuff.DefaultStuffFor(ThingDefOf.Door)) as Building_Door;
			var albinoDoor = ThingMaker.MakeThing(ThingDefOf.Door, GenStuff.DefaultStuffFor(ThingDefOf.Door)) as Building_Door;
			if (normalDoor == null || albinoDoor == null)
			{
				return new
				{
					success = false,
					error = "Could not create one or both test doors."
				};
			}
			GenSpawn.Spawn(normalDoor, normalDoorCell, map, WipeMode.Vanish);
			GenSpawn.Spawn(albinoDoor, albinoDoorCell, map, WipeMode.Vanish);
			normalDoor.SetFaction(Faction.OfPlayer);
			albinoDoor.SetFaction(Faction.OfPlayer);
			map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
			normalDoor.StartManualOpenBy(actor);
			albinoDoor.StartManualOpenBy(actor);

			var normalZombie = ZombieRuntimeActions.SpawnZombie(normalDoorCell, map, ZombieType.Normal, true);
			var albinoZombie = ZombieRuntimeActions.SpawnZombie(albinoDoorCell, map, ZombieType.Albino, true);
			if (normalZombie == null || albinoZombie == null)
			{
				return new
				{
					success = false,
					normalDoorCell = ZombieRuntimeActions.DescribeCell(normalDoorCell),
					albinoDoorCell = ZombieRuntimeActions.DescribeCell(albinoDoorCell),
					error = "ZombieGenerator.SpawnZombie returned no normal or albino test zombie."
				};
			}

			const int initialTicksUntilClose = 10;
			ticksUntilCloseField.SetValue(normalDoor, initialTicksUntilClose);
			ticksUntilCloseField.SetValue(albinoDoor, initialTicksUntilClose);
			AdvanceGameTicks(1);
			var normalTicksAfter = (int)ticksUntilCloseField.GetValue(normalDoor);
			var albinoTicksAfter = (int)ticksUntilCloseField.GetValue(albinoDoor);

			return new
			{
				success = normalDoor.Open
					&& albinoDoor.Open
					&& normalTicksAfter > initialTicksUntilClose
					&& albinoTicksAfter == initialTicksUntilClose - 1,
				destroyedZombies,
				actor = DescribePawn(actor),
				normalZombie = DescribeZombie(normalZombie),
				albinoZombie = DescribeZombie(albinoZombie),
				normalDoor = new
				{
					id = ZombieRuntimeActions.StableThingId(normalDoor),
					position = ZombieRuntimeActions.DescribeCell(normalDoor.Position),
					normalDoor.Open
				},
				albinoDoor = new
				{
					id = ZombieRuntimeActions.StableThingId(albinoDoor),
					position = ZombieRuntimeActions.DescribeCell(albinoDoor.Position),
					albinoDoor.Open
				},
				initialTicksUntilClose,
				normalTicksAfter,
				albinoTicksAfter
			};
		}

	}
}
