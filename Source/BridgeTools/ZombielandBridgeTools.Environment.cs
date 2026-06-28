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
		[Tool("zombieland/zombie_records_suppression", Description = "Verify zombies cannot mutate/report vanilla records and are excluded from world-pawn mothballing while ordinary pawns keep vanilla behavior.")]
		public static object ZombieRecordsSuppression()
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
				var destroyedZombies = ZombieRuntimeActions.DestroyZombies(map);
				foreach (var corpse in map.listerThings.AllThings.OfType<ZombieCorpse>().ToArray())
					corpse.Destroy();

				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var humanCell, out var humanSpawnError) == false)
					return humanSpawnError;
				if (TryFindClearSpawnCell(map, humanCell + new IntVec3(4, 0, 0), 10f, out var zombieCell, out var zombieSpawnError) == false)
					return zombieSpawnError;

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
						destroyedZombies,
						human = DescribePawn(human),
						error = "ZombieGenerator.SpawnZombie returned no records test zombie."
					};
				}
				spawnedThings.Add(zombie);

				if (TryFindRawRecordDef(human, RecordType.Int, out var recordDef, out var recordError) == false)
				{
					return new
					{
						success = false,
						destroyedZombies,
						human = DescribePawn(human),
						zombie = DescribeZombie(zombie),
						error = recordError
					};
				}

				var humanBefore = DescribeRecord(human, recordDef);
				var zombieBefore = DescribeRecord(zombie, recordDef);
				human.records.Increment(recordDef);
				human.records.AddTo(recordDef, 2f);
				zombie.records.Increment(recordDef);
				zombie.records.AddTo(recordDef, 2f);
				var humanAfter = DescribeRecord(human, recordDef);
				var zombieAfter = DescribeRecord(zombie, recordDef);

				var humanRecordsMutated = humanAfter.rawValue == humanBefore.rawValue + 3f
					&& humanAfter.publicValue == humanBefore.publicValue + 3f
					&& humanAfter.publicInt == humanBefore.publicInt + 3;
				var zombieRecordsNotMutated = zombieAfter.rawValue == zombieBefore.rawValue;
				var zombieRecordsHidden = zombieAfter.publicValue == 0f && zombieAfter.publicInt == 0;

				if (TryShouldMothball(human, out var humanShouldMothball, out var humanMothballError) == false)
				{
					return new
					{
						success = false,
						human = DescribePawn(human),
						error = humanMothballError
					};
				}
				if (TryShouldMothball(zombie, out var zombieShouldMothball, out var zombieMothballError) == false)
				{
					return new
					{
						success = false,
						zombie = DescribeZombie(zombie),
						error = zombieMothballError
					};
				}
				var humanMothballVanilla = humanShouldMothball;
				var zombieMothballSuppressed = zombieShouldMothball == false;

				return new
				{
					success = humanRecordsMutated
						&& zombieRecordsNotMutated
						&& zombieRecordsHidden
						&& humanMothballVanilla
						&& zombieMothballSuppressed,
					destroyedZombies,
					recordDef = recordDef.defName,
					human = DescribePawn(human),
					zombie = DescribeZombie(zombie),
					humanBefore,
					humanAfter,
					zombieBefore,
					zombieAfter,
					humanRecordsMutated,
					zombieRecordsNotMutated,
					zombieRecordsHidden,
					humanShouldMothball,
					zombieShouldMothball,
					humanMothballVanilla,
					zombieMothballSuppressed
				};
			}
			finally
			{
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}
		}

		[Tool("zombieland/ambient_temperature_contract", Description = "Verify Zombieland pawns and zombie corpses report normal ambient temperature while ordinary spawned things keep vanilla map temperature.")]
		public static object AmbientTemperatureContract()
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
				foreach (var corpse in map.listerThings.AllThings.OfType<ZombieCorpse>().ToArray())
					corpse.Destroy();

				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var humanCell, out var humanError) == false)
					return humanError;
				if (TryFindClearSpawnCell(map, humanCell + new IntVec3(2, 0, 0), 8f, out var humanCorpseCell, out var humanCorpseError) == false)
					return humanCorpseError;
				if (TryFindClearSpawnCell(map, humanCell + new IntVec3(4, 0, 0), 10f, out var zombieCell, out var zombieError) == false)
					return zombieError;
				if (TryFindClearSpawnCell(map, humanCell + new IntVec3(6, 0, 0), 12f, out var zombieCorpseCell, out var zombieCorpseError) == false)
					return zombieCorpseError;
				if (TryFindClearSpawnCell(map, humanCell + new IntVec3(0, 0, 3), 8f, out var spitterCell, out var spitterError) == false)
					return spitterError;
				if (TryFindClearSpawnCell(map, humanCell + new IntVec3(2, 0, 3), 8f, out var symbiantCell, out var symbiantError) == false)
					return symbiantError;

				var human = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(human, humanCell, map, Rot4.South);
				DisablePawnWork(human);
				spawnedThings.Add(human);

				var humanCorpsePawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(humanCorpsePawn, humanCorpseCell, map, Rot4.South);
				DisablePawnWork(humanCorpsePawn);
				spawnedThings.Add(humanCorpsePawn);
				if (ZombieRuntimeActions.KillPawnToCorpse(humanCorpsePawn, out var humanCorpse, out var corpseError) == false)
				{
					return new
					{
						success = false,
						error = corpseError,
						humanCorpsePawn = DescribePawn(humanCorpsePawn)
					};
				}
				if (humanCorpse != null)
					spawnedThings.Add(humanCorpse);

				var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
				if (zombie == null)
				{
					return new
					{
						success = false,
						error = "ZombieGenerator.SpawnZombie returned no ambient-temperature test zombie."
					};
				}
				spawnedThings.Add(zombie);

				var zombieForCorpse = ZombieRuntimeActions.SpawnZombie(zombieCorpseCell, map, ZombieType.Normal, true);
				if (zombieForCorpse == null)
				{
					return new
					{
						success = false,
						error = "ZombieGenerator.SpawnZombie returned no ambient-temperature corpse zombie."
					};
				}
				spawnedThings.Add(zombieForCorpse);
				zombieForCorpse.Kill(null);
				var zombieCorpse = zombieForCorpse.Corpse as ZombieCorpse
					?? map.listerThings.AllThings.OfType<ZombieCorpse>().OrderBy(thing => thing.Position.DistanceToSquared(zombieCorpseCell)).FirstOrDefault();
				if (zombieCorpse != null)
					spawnedThings.Add(zombieCorpse);

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

				var ordinaryCases = new[]
				{
						DescribeAmbientTemperature("human", human, map, false),
						DescribeAmbientTemperature("humanCorpse", humanCorpse, map, false)
					};
				var zombielandCases = new[]
				{
						DescribeAmbientTemperature("zombie", zombie, map, true),
						DescribeAmbientTemperature("zombieCorpse", zombieCorpse, map, true),
						DescribeAmbientTemperature("spitter", spitter, map, true),
						DescribeAmbientTemperature("symbiant", symbiant, map, true)
					};

				return new
				{
					success = ordinaryCases.All(c => c.success) && zombielandCases.All(c => c.success),
					mapTemperature = new
					{
						outdoorTemp = map.mapTemperature.OutdoorTemp
					},
					ordinaryCases,
					zombielandCases
				};
			}
			finally
			{
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}
		}

		[Tool("zombieland/zombie_clamor_suppression", Description = "Verify all GenClamor overloads suppress zombie-originated clamors while ordinary pawn clamors still reach nearby hearers.")]
		public static object ZombieClamorSuppression()
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
			foreach (var corpse in map.listerThings.AllThings.OfType<ZombieCorpse>().ToArray())
				corpse.Destroy();

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var listenerCell, out var listenerSpawnError) == false)
				return listenerSpawnError;
			if (TryFindClearSpawnCell(map, listenerCell + new IntVec3(3, 0, 0), 8f, out var humanSourceCell, out var humanSourceSpawnError) == false)
				return humanSourceSpawnError;
			if (TryFindClearSpawnCell(map, listenerCell + new IntVec3(6, 0, 0), 10f, out var zombieCell, out var zombieSpawnError) == false)
				return zombieSpawnError;
			if (TryFindClearSpawnCell(map, listenerCell + new IntVec3(0, 0, 3), 8f, out var spitterCell, out var spitterSpawnError) == false)
				return spitterSpawnError;
			if (TryFindClearSpawnCell(map, listenerCell + new IntVec3(3, 0, 3), 10f, out var symbiantCell, out var symbiantSpawnError) == false)
				return symbiantSpawnError;

			var listener = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var humanSource = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(listener, listenerCell, map, Rot4.South);
			GenSpawn.Spawn(humanSource, humanSourceCell, map, Rot4.South);
			DisablePawnWork(listener);
			DisablePawnWork(humanSource);

			var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
			if (zombie == null)
			{
				return new
				{
					success = false,
					destroyedZombies,
					listener = DescribePawn(listener),
					humanSource = DescribePawn(humanSource),
					error = "ZombieGenerator.SpawnZombie returned no clamor test zombie."
				};
			}

			var existingSpitters = CurrentZombies(map).OfType<ZombieSpitter>().Select(ZombieRuntimeActions.StableThingId).ToHashSet();
			ZombieSpitter.Spawn(map, spitterCell);
			var spitter = CurrentZombies(map).OfType<ZombieSpitter>()
				.FirstOrDefault(candidate => existingSpitters.Contains(ZombieRuntimeActions.StableThingId(candidate)) == false)
				?? CurrentZombies(map).OfType<ZombieSpitter>().OrderBy(candidate => candidate.Position.DistanceToSquared(spitterCell)).FirstOrDefault();
			if (spitter == null)
			{
				return new
				{
					success = false,
					destroyedZombies,
					listener = DescribePawn(listener),
					humanSource = DescribePawn(humanSource),
					zombie = DescribeZombie(zombie),
					error = "ZombieSpitter.Spawn returned no clamor test spitter."
				};
			}

			var existingSymbiants = CurrentZombies(map).OfType<ZombieSymbiant>().Select(ZombieRuntimeActions.StableThingId).ToHashSet();
			ZombieSymbiant.Spawn(map, symbiantCell);
			var symbiant = CurrentZombies(map).OfType<ZombieSymbiant>()
				.FirstOrDefault(candidate => existingSymbiants.Contains(ZombieRuntimeActions.StableThingId(candidate)) == false)
				?? CurrentZombies(map).OfType<ZombieSymbiant>().OrderBy(candidate => candidate.Position.DistanceToSquared(symbiantCell)).FirstOrDefault();
			if (symbiant == null)
			{
				return new
				{
					success = false,
					destroyedZombies,
					listener = DescribePawn(listener),
					humanSource = DescribePawn(humanSource),
					zombie = DescribeZombie(zombie),
					spitter = DescribeZombie(spitter),
					error = "ZombieSymbiant.Spawn returned no clamor test symbiant."
				};
			}

			int ListenerEffectCount(Thing source)
			{
				var count = 0;
				GenClamor.DoClamor(source, source.Position, 20f, (clamorSource, hearer) =>
				{
					if (hearer == listener)
						count++;
				});
				return count;
			}

			var humanEffectCount = ListenerEffectCount(humanSource);
			var zombieEffectCount = ListenerEffectCount(zombie);
			var spitterEffectCount = ListenerEffectCount(spitter);
			var symbiantEffectCount = ListenerEffectCount(symbiant);

			var impactTick = Find.TickManager.TicksGame;
			var resetCanSleepTick = impactTick - 1000;
			listener.mindState.canSleepTick = resetCanSleepTick;
			zombie.mindState.canSleepTick = resetCanSleepTick;
			GenClamor.DoClamor(humanSource, humanSource.Position, 20f, ClamorDefOf.Impact);
			var expectedImpactCanSleepTick = impactTick + 1000;
			var listenerCanSleepAfterHumanImpact = listener.mindState.canSleepTick;
			var zombieCanSleepAfterHumanImpact = zombie.mindState.canSleepTick;
			var humanHearerReceivesHumanImpact = listenerCanSleepAfterHumanImpact == expectedImpactCanSleepTick;
			var zombieHearerIgnoresHumanImpact = zombieCanSleepAfterHumanImpact == resetCanSleepTick;

			var listenerDescription = DescribePawn(listener);
			var humanSourceDescription = DescribePawn(humanSource);
			var zombieDescription = DescribeZombie(zombie);
			var spitterDescription = DescribeZombie(spitter);
			var symbiantDescription = DescribeZombie(symbiant);

			foreach (var thing in new Thing[] { symbiant, spitter, zombie, humanSource, listener })
			{
				if (thing != null && thing.Destroyed == false)
					thing.Destroy(DestroyMode.Vanish);
			}

			return new
			{
				success = humanEffectCount > 0
					&& zombieEffectCount == 0
					&& spitterEffectCount == 0
					&& symbiantEffectCount == 0
					&& humanHearerReceivesHumanImpact
					&& zombieHearerIgnoresHumanImpact,
				destroyedZombies,
				listener = listenerDescription,
				humanSource = humanSourceDescription,
				zombie = zombieDescription,
				spitter = spitterDescription,
				symbiant = symbiantDescription,
				humanEffectCount,
				zombieEffectCount,
				spitterEffectCount,
				symbiantEffectCount,
				humanImpact = new
				{
					clamorType = ClamorDefOf.Impact.defName,
					tick = impactTick,
					resetCanSleepTick,
					expectedImpactCanSleepTick,
					listenerCanSleepAfterHumanImpact,
					zombieCanSleepAfterHumanImpact,
					humanHearerReceivesHumanImpact,
					zombieHearerIgnoresHumanImpact
				}
			};
		}

		[Tool("zombieland/tar_slime_move_cost_contract", Description = "Verify TarSlime applies the Zombieland movement-cost formula to zombies and spitters while ordinary pawns use the non-zombie formula.")]
		public static object TarSlimeMoveCostContract()
		{
			var patchTargets = PatchedMethodsForPatchClass("Pawn_PathFollower_CostToMoveIntoCell_Patch");
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
			foreach (var corpse in map.listerThings.AllThings.OfType<ZombieCorpse>().ToArray())
				corpse.Destroy();

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var humanCell, out var humanSpawnError) == false)
				return humanSpawnError;
			if (TryFindClearSpawnCell(map, humanCell + new IntVec3(3, 0, 0), 8f, out var zombieCell, out var zombieSpawnError) == false)
				return zombieSpawnError;
			if (TryFindClearSpawnCell(map, humanCell + new IntVec3(6, 0, 0), 10f, out var spitterCell, out var spitterSpawnError) == false)
				return spitterSpawnError;
			if (TryFindClearSpawnCell(map, humanCell + new IntVec3(0, 0, 3), 8f, out var clearCell, out var clearSpawnError) == false)
				return clearSpawnError;
			if (TryFindClearSpawnCell(map, humanCell + new IntVec3(3, 0, 3), 10f, out var tarCell, out var tarSpawnError) == false)
				return tarSpawnError;

			var human = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(human, humanCell, map, Rot4.South);
			DisablePawnWork(human);
			var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
			if (zombie == null)
			{
				return new
				{
					success = false,
					destroyedZombies,
					human = DescribePawn(human),
					error = "ZombieGenerator.SpawnZombie returned no tar-cost test zombie."
				};
			}

			var existingSpitters = CurrentZombies(map).OfType<ZombieSpitter>().Select(ZombieRuntimeActions.StableThingId).ToHashSet();
			ZombieSpitter.Spawn(map, spitterCell);
			var spitter = CurrentZombies(map).OfType<ZombieSpitter>()
				.FirstOrDefault(candidate => existingSpitters.Contains(ZombieRuntimeActions.StableThingId(candidate)) == false)
				?? CurrentZombies(map).OfType<ZombieSpitter>().OrderBy(candidate => candidate.Position.DistanceToSquared(spitterCell)).FirstOrDefault();
			if (spitter == null)
			{
				return new
				{
					success = false,
					destroyedZombies,
					human = DescribePawn(human),
					zombie = DescribeZombie(zombie),
					error = "ZombieSpitter.Spawn returned no tar-cost test spitter."
				};
			}

			FilthMaker.TryMakeFilth(tarCell, map, CustomDefs.TarSlime);
			var tarSlime = map.thingGrid.ThingAt<TarSlime>(tarCell);
			if (tarSlime == null)
			{
				return new
				{
					success = false,
					destroyedZombies,
					human = DescribePawn(human),
					zombie = DescribeZombie(zombie),
					spitter = DescribeZombie(spitter),
					tarCell = ZombieRuntimeActions.DescribeCell(tarCell),
					error = "Could not create TarSlime in the tar-cost test cell."
				};
			}

			if (TryCostToMoveIntoCell(human, clearCell, out var humanClearCost, out var error) == false)
				return new { success = false, error };
			if (TryCostToMoveIntoCell(human, tarCell, out var humanTarCost, out error) == false)
				return new { success = false, error };
			if (TryCostToMoveIntoCell(zombie, clearCell, out var zombieClearCost, out error) == false)
				return new { success = false, error };
			if (TryCostToMoveIntoCell(zombie, tarCell, out var zombieTarCost, out error) == false)
				return new { success = false, error };
			if (TryCostToMoveIntoCell(spitter, clearCell, out var spitterClearCost, out error) == false)
				return new { success = false, error };
			if (TryCostToMoveIntoCell(spitter, tarCell, out var spitterTarCost, out error) == false)
				return new { success = false, error };

			var difficulty = Tools.Difficulty();
			var expectedZombieTarCost = (float)GenMath.LerpDouble(0, 5, 150, 14, difficulty);
			var expectedHumanTarCost = (float)GenMath.LerpDouble(0, 5, 14, 400, difficulty);
			var humanMatchesTarFormula = Mathf.Abs(humanTarCost - expectedHumanTarCost) < 0.001f;
			var zombieMatchesTarFormula = Mathf.Abs(zombieTarCost - expectedZombieTarCost) < 0.001f;
			var spitterMatchesTarFormula = Mathf.Abs(spitterTarCost - expectedZombieTarCost) < 0.001f;
			var clearCostsDifferFromTar = Mathf.Abs(humanClearCost - humanTarCost) > 0.001f
				&& Mathf.Abs(zombieClearCost - zombieTarCost) > 0.001f
				&& Mathf.Abs(spitterClearCost - spitterTarCost) > 0.001f;
			var tarSlimeId = ZombieRuntimeActions.StableThingId(tarSlime);
			var humanDescription = DescribePawn(human);
			var zombieDescription = DescribeZombie(zombie);
			var spitterDescription = DescribeZombie(spitter);

			if (tarSlime.Destroyed == false)
				tarSlime.Destroy(DestroyMode.Vanish);
			if (spitter.Destroyed == false)
				spitter.Destroy();
			if (zombie.Destroyed == false)
				zombie.Destroy();
			if (human.Destroyed == false)
				human.Destroy();

			return new
			{
				success = patchTargets.Length > 0
					&& humanMatchesTarFormula
					&& zombieMatchesTarFormula
					&& spitterMatchesTarFormula
					&& clearCostsDifferFromTar,
				patchTargets,
				destroyedZombies,
				difficulty,
				clearCell = ZombieRuntimeActions.DescribeCell(clearCell),
				tarCell = ZombieRuntimeActions.DescribeCell(tarCell),
				tarSlimeId,
				human = humanDescription,
				zombie = zombieDescription,
				spitter = spitterDescription,
				humanClearCost,
				humanTarCost,
				zombieClearCost,
				zombieTarCost,
				spitterClearCost,
				spitterTarCost,
				expectedHumanTarCost,
				expectedZombieTarCost,
				humanMatchesTarFormula,
				zombieMatchesTarFormula,
				spitterMatchesTarFormula,
				clearCostsDifferFromTar
			};
		}

		[Tool("zombieland/zombie_blood_filth_contract", Description = "Verify zombie blood filth follows the Zombieland setting and tanky armor suppression while humans still use vanilla blood drops.")]
		public static object ZombieBloodFilthContract()
		{
			var patchTargets = PatchedMethodsForPatchClass("Pawn_HealthTracker_DropBloodFilth_Patch");
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
			foreach (var corpse in map.listerThings.AllThings.OfType<ZombieCorpse>().ToArray())
				corpse.Destroy();

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var humanCell, out var humanSpawnError) == false)
				return humanSpawnError;
			if (TryFindClearSpawnCell(map, humanCell + new IntVec3(3, 0, 0), 8f, out var zombieCell, out var zombieSpawnError) == false)
				return zombieSpawnError;
			if (TryFindClearSpawnCell(map, humanCell + new IntVec3(6, 0, 0), 10f, out var tankyCell, out var tankySpawnError) == false)
				return tankySpawnError;
			if (TryFindClearSpawnCell(map, humanCell + new IntVec3(0, 0, 3), 8f, out var spitterCell, out var spitterSpawnError) == false)
				return spitterSpawnError;

			var human = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(human, humanCell, map, Rot4.South);
			DisablePawnWork(human);
			var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
			var tanky = ZombieRuntimeActions.SpawnZombie(tankyCell, map, ZombieType.TankyOperator, true);
			if (zombie == null || tanky == null)
			{
				return new
				{
					success = false,
					destroyedZombies,
					human = DescribePawn(human),
					zombie = DescribeZombie(zombie),
					tanky = DescribeZombie(tanky),
					error = "ZombieGenerator.SpawnZombie returned no normal or tanky blood-filth test zombie."
				};
			}

			var tankyArmorForced = false;
			if (tanky.hasTankyShield <= 0f && tanky.hasTankySuit <= 0f)
			{
				tanky.hasTankyShield = 1f;
				tankyArmorForced = true;
			}

			var existingSpitters = CurrentZombies(map).OfType<ZombieSpitter>().Select(ZombieRuntimeActions.StableThingId).ToHashSet();
			ZombieSpitter.Spawn(map, spitterCell);
			var spitter = CurrentZombies(map).OfType<ZombieSpitter>()
				.FirstOrDefault(candidate => existingSpitters.Contains(ZombieRuntimeActions.StableThingId(candidate)) == false)
				?? CurrentZombies(map).OfType<ZombieSpitter>().OrderBy(candidate => candidate.Position.DistanceToSquared(spitterCell)).FirstOrDefault();
			if (spitter == null)
			{
				return new
				{
					success = false,
					destroyedZombies,
					human = DescribePawn(human),
					zombie = DescribeZombie(zombie),
					tanky = DescribeZombie(tanky),
					error = "ZombieSpitter.Spawn returned no blood-filth test spitter."
				};
			}

			BloodFilthSnapshot DropBloodSample(Pawn pawn)
			{
				var bloodDef = pawn?.RaceProps?.BloodDef;
				var cell = pawn?.Position ?? IntVec3.Invalid;
				ClearFilthAt(map, cell);
				var before = CountThingsAt(map, cell, bloodDef);
				pawn.health.DropBloodFilth();
				var after = CountThingsAt(map, cell, bloodDef);
				return new BloodFilthSnapshot
				{
					pawn = DescribePawn(pawn),
					bloodDef = bloodDef?.defName,
					cell = ZombieRuntimeActions.DescribeCell(cell),
					before = before,
					after = after,
					delta = after - before
				};
			}

			var originalZombiesDropBlood = ZombieSettings.Values.zombiesDropBlood;
			BloodFilthSnapshot humanEnabled;
			BloodFilthSnapshot zombieEnabled;
			BloodFilthSnapshot tankyEnabled;
			BloodFilthSnapshot spitterEnabled;
			BloodFilthSnapshot humanDisabled;
			BloodFilthSnapshot zombieDisabled;
			BloodFilthSnapshot spitterDisabled;
			try
			{
				ZombieSettings.Values.zombiesDropBlood = true;
				humanEnabled = DropBloodSample(human);
				zombieEnabled = DropBloodSample(zombie);
				tankyEnabled = DropBloodSample(tanky);
				spitterEnabled = DropBloodSample(spitter);

				ZombieSettings.Values.zombiesDropBlood = false;
				humanDisabled = DropBloodSample(human);
				zombieDisabled = DropBloodSample(zombie);
				spitterDisabled = DropBloodSample(spitter);
			}
			finally
			{
				ZombieSettings.Values.zombiesDropBlood = originalZombiesDropBlood;
			}

			var humanEnabledDropsBlood = humanEnabled.delta > 0;
			var zombieEnabledDropsBlood = zombieEnabled.delta > 0;
			var tankyEnabledDropsNoBlood = tankyEnabled.delta == 0;
			var spitterEnabledDropsBlood = spitterEnabled.delta > 0;
			var humanDisabledStillDropsBlood = humanDisabled.delta > 0;
			var zombieDisabledDropsNoBlood = zombieDisabled.delta == 0;
			var spitterDisabledDropsNoBlood = spitterDisabled.delta == 0;
			var humanDescription = DescribePawn(human);
			var zombieDescription = DescribeZombie(zombie);
			var tankyDescription = DescribeZombie(tanky);
			var tankyArmorDescription = DescribeTankyArmor(tanky);
			var spitterDescription = DescribeZombie(spitter);

			ClearFilthAt(map, humanCell);
			ClearFilthAt(map, zombieCell);
			ClearFilthAt(map, tankyCell);
			ClearFilthAt(map, spitterCell);
			if (spitter.Destroyed == false)
				spitter.Destroy();
			if (tanky.Destroyed == false)
				tanky.Destroy();
			if (zombie.Destroyed == false)
				zombie.Destroy();
			if (human.Destroyed == false)
				human.Destroy();

			return new
			{
				success = patchTargets.Length > 0
					&& humanEnabledDropsBlood
					&& zombieEnabledDropsBlood
					&& tankyEnabledDropsNoBlood
					&& spitterEnabledDropsBlood
					&& humanDisabledStillDropsBlood
					&& zombieDisabledDropsNoBlood
					&& spitterDisabledDropsNoBlood,
				patchTargets,
				destroyedZombies,
				originalZombiesDropBlood,
				tankyArmorForced,
				human = humanDescription,
				zombie = zombieDescription,
				tanky = tankyDescription,
				tankyArmor = tankyArmorDescription,
				spitter = spitterDescription,
				humanEnabled,
				zombieEnabled,
				tankyEnabled,
				spitterEnabled,
				humanDisabled,
				zombieDisabled,
				spitterDisabled,
				humanEnabledDropsBlood,
				zombieEnabledDropsBlood,
				tankyEnabledDropsNoBlood,
				spitterEnabledDropsBlood,
				humanDisabledStillDropsBlood,
				zombieDisabledDropsNoBlood,
				spitterDisabledDropsNoBlood
			};
		}

		[Tool("zombieland/tar_slime_fire_spread_contract", Description = "Verify real Fire.DoFireDamage on burning TarSlime raises fire size and ignites adjacent TarSlime.")]
		public static object TarSlimeFireSpreadContract()
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
			if (TryFindClearSpawnCell(map, root, 16f, out var sourceCell, out var sourceError) == false)
				return sourceError;

			var adjacentCell = GenAdj.AdjacentCellsAround
				.Select(offset => sourceCell + offset)
				.FirstOrDefault(cell => cell.InBounds(map) && cell.Standable(map) && cell.GetThingList(map).Any() == false);
			if (adjacentCell.IsValid == false)
			{
				return new
				{
					success = false,
					sourceCell = ZombieRuntimeActions.DescribeCell(sourceCell),
					error = "No clear adjacent TarSlime spread cell was found."
				};
			}

			ClearFilthAt(map, sourceCell);
			ClearFilthAt(map, adjacentCell);
			foreach (var fire in sourceCell.GetThingList(map).OfType<Fire>().Concat(adjacentCell.GetThingList(map).OfType<Fire>()).ToArray())
				fire.Destroy();

			FilthMaker.TryMakeFilth(sourceCell, map, CustomDefs.TarSlime);
			FilthMaker.TryMakeFilth(adjacentCell, map, CustomDefs.TarSlime);
			var sourceTar = map.thingGrid.ThingAt<TarSlime>(sourceCell);
			var adjacentTar = map.thingGrid.ThingAt<TarSlime>(adjacentCell);
			if (sourceTar == null || adjacentTar == null)
			{
				return new
				{
					success = false,
					sourceCell = ZombieRuntimeActions.DescribeCell(sourceCell),
					adjacentCell = ZombieRuntimeActions.DescribeCell(adjacentCell),
					sourceTar = ZombieRuntimeActions.StableThingId(sourceTar),
					adjacentTar = ZombieRuntimeActions.StableThingId(adjacentTar),
					error = "Could not create both TarSlime fixtures."
				};
			}

			FireUtility.TryStartFireIn(sourceCell, map, 0.1f, null);
			var sourceFire = sourceCell.GetThingList(map).OfType<Fire>().FirstOrDefault();
			if (sourceFire == null)
			{
				return new
				{
					success = false,
					sourceCell = ZombieRuntimeActions.DescribeCell(sourceCell),
					adjacentCell = ZombieRuntimeActions.DescribeCell(adjacentCell),
					sourceTar = ZombieRuntimeActions.StableThingId(sourceTar),
					adjacentTar = ZombieRuntimeActions.StableThingId(adjacentTar),
					error = "Could not start a real fire on the source TarSlime cell."
				};
			}

			var fireSizeBefore = sourceFire.fireSize;
			var adjacentBurningBefore = adjacentTar.IsBurning();
			if (TryDoFireDamage(sourceFire, sourceTar, out var error) == false)
			{
				return new
				{
					success = false,
					error
				};
			}
			var fireSizeAfter = sourceFire.fireSize;
			var adjacentBurningAfter = adjacentTar.IsBurning();
			var adjacentFiresAfter = adjacentCell.GetThingList(map).OfType<Fire>().ToArray();

			return new
			{
				success = adjacentBurningBefore == false
					&& adjacentBurningAfter
					&& fireSizeAfter >= 0.5f
					&& adjacentFiresAfter.Length > 0,
				sourceCell = ZombieRuntimeActions.DescribeCell(sourceCell),
				adjacentCell = ZombieRuntimeActions.DescribeCell(adjacentCell),
				sourceTar = ZombieRuntimeActions.StableThingId(sourceTar),
				adjacentTar = ZombieRuntimeActions.StableThingId(adjacentTar),
				sourceFire = ZombieRuntimeActions.StableThingId(sourceFire),
				adjacentFireIds = adjacentFiresAfter.Select(ZombieRuntimeActions.StableThingId).ToArray(),
				fireSizeBefore,
				fireSizeAfter,
				adjacentBurningBefore,
				adjacentBurningAfter,
				adjacentFireCountAfter = adjacentFiresAfter.Length
			};
		}

		[Tool("zombieland/tar_slime_fire_smoke_contract", Description = "Verify a real burning TarSlime fire tick emits Zombieland TarSmoke instead of only vanilla fire smoke.")]
		public static object TarSlimeFireSmokeContract()
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
			if (TryFindClearSpawnCell(map, root, 16f, out var cell, out var spawnError) == false)
				return spawnError;

			ClearFilthAt(map, cell);
			ClearGasAt(map, cell);
			foreach (var existingFire in cell.GetThingList(map).OfType<Fire>().ToArray())
				existingFire.Destroy();

			FilthMaker.TryMakeFilth(cell, map, CustomDefs.TarSlime);
			var tar = map.thingGrid.ThingAt<TarSlime>(cell);
			if (tar == null)
			{
				return new
				{
					success = false,
					cell = ZombieRuntimeActions.DescribeCell(cell),
					error = "Could not create TarSlime fixture."
				};
			}

			var tarSmokeBefore = CountThingsAt(map, cell, CustomDefs.TarSmoke);
			var gasAtCellBefore = cell.GetGas(map)?.def?.defName;
			FireUtility.TryStartFireIn(cell, map, 0.8f, null);
			var sourceFire = cell.GetThingList(map).OfType<Fire>().FirstOrDefault();
			if (sourceFire == null)
			{
				return new
				{
					success = false,
					cell = ZombieRuntimeActions.DescribeCell(cell),
					tar = ZombieRuntimeActions.StableThingId(tar),
					error = "Could not start a real fire on the TarSlime cell."
				};
			}

			const int sourceDerivedFireUpdateTicks = 15;
			var fireSizeBefore = sourceFire.fireSize;
			AdvanceGameTicks(sourceDerivedFireUpdateTicks);
			var tarSmokeAfter = CountThingsAt(map, cell, CustomDefs.TarSmoke);
			var gasAtCellAfter = cell.GetGas(map)?.def?.defName;

			return new
			{
				success = tarSmokeAfter > tarSmokeBefore && gasAtCellAfter == CustomDefs.TarSmoke.defName,
				cell = ZombieRuntimeActions.DescribeCell(cell),
				tar = ZombieRuntimeActions.StableThingId(tar),
				fire = ZombieRuntimeActions.StableThingId(sourceFire),
				fireSizeBefore,
				fireSizeAfter = sourceFire.Destroyed ? 0f : sourceFire.fireSize,
				ticksToRun = sourceDerivedFireUpdateTicks,
				gasAtCellBefore,
				gasAtCellAfter,
				tarSmokeBefore,
				tarSmokeAfter,
				tarSmokeDelta = tarSmokeAfter - tarSmokeBefore
			};
		}

		[Tool("zombieland/zombie_fire_watcher_excludes_attached_fires", Description = "Verify FireWatcher counts normal fires but excludes fires attached to Zombieland pawns.")]
		public static object ZombieFireWatcherExcludesAttachedFires()
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
			if (fireWatcherUpdateObservationsMethod == null)
			{
				return new
				{
					success = false,
					error = "Could not find FireWatcher.UpdateObservations."
				};
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var mapFireCell, out var mapFireError) == false)
				return mapFireError;
			if (TryFindClearSpawnCell(map, mapFireCell + new IntVec3(3, 0, 0), 10f, out var humanCell, out var humanError) == false)
				return humanError;
			if (TryFindClearSpawnCell(map, mapFireCell + new IntVec3(-3, 0, 0), 10f, out var zombieCell, out var zombieError) == false)
				return zombieError;
			if (TryFindClearSpawnCell(map, mapFireCell + new IntVec3(0, 0, 3), 10f, out var spitterCell, out var spitterError) == false)
				return spitterError;
			if (TryFindClearSpawnCell(map, mapFireCell + new IntVec3(0, 0, -3), 10f, out var symbiantCell, out var symbiantError) == false)
				return symbiantError;

			foreach (var fire in map.listerThings.ThingsOfDef(ThingDefOf.Fire).OfType<Fire>().ToArray())
				fire.Destroy(DestroyMode.Vanish);

			foreach (var cell in new[] { mapFireCell, humanCell, zombieCell, spitterCell, symbiantCell })
			{
				ClearGasAt(map, cell);
				foreach (var fire in cell.GetThingList(map).OfType<Fire>().ToArray())
					fire.Destroy();
			}

			Fire mapFire = null;
			Pawn human = null;
			Pawn zombie = null;
			Pawn spitter = null;
			Pawn symbiant = null;
			try
			{
				FireUtility.TryStartFireIn(mapFireCell, map, 1.25f, null);
				mapFire = mapFireCell.GetThingList(map).OfType<Fire>().FirstOrDefault();
				if (mapFire == null)
				{
					return new
					{
						success = false,
						mapFireCell = ZombieRuntimeActions.DescribeCell(mapFireCell),
						error = "Could not start a normal map fire."
					};
				}
				mapFire.fireSize = 1.25f;

				human = SpawnFireFixturePawn(map, humanCell, "human");
				FireUtility.TryAttachFire(human, 2f, null);
				var humanFire = human.GetAttachment(ThingDefOf.Fire) as Fire;

				zombie = SpawnFireFixturePawn(map, zombieCell, "normal");
				FireUtility.TryAttachFire(zombie, 3f, null);
				var zombieFire = zombie?.GetAttachment(ThingDefOf.Fire) as Fire;

				spitter = SpawnFireFixturePawn(map, spitterCell, "spitter");
				FireUtility.TryAttachFire(spitter, 1.5f, null);
				var spitterFire = spitter?.GetAttachment(ThingDefOf.Fire) as Fire;

				symbiant = SpawnFireFixturePawn(map, symbiantCell, "symbiant");
				FireUtility.TryAttachFire(symbiant, 1.75f, null);
				var symbiantFire = symbiant?.GetAttachment(ThingDefOf.Fire) as Fire;

				if (humanFire == null || zombieFire == null || spitterFire == null || symbiantFire == null)
				{
					return new
					{
						success = false,
						mapFire = ZombieRuntimeActions.StableThingId(mapFire),
						human = DescribePawn(human),
						zombie = DescribeZombie(zombie),
						spitter = DescribeZombie(spitter),
						symbiant = DescribeZombie(symbiant),
						humanFire = ZombieRuntimeActions.StableThingId(humanFire),
						zombieFire = ZombieRuntimeActions.StableThingId(zombieFire),
						spitterFire = ZombieRuntimeActions.StableThingId(spitterFire),
						symbiantFire = ZombieRuntimeActions.StableThingId(symbiantFire),
						error = "Could not attach human, normal zombie, spitter, and symbiant fires."
					};
				}

				fireWatcherUpdateObservationsMethod.Invoke(map.fireWatcher, null);
				var fireDangerAfter = map.fireWatcher.FireDanger;
				var expectedExcludingZombie = 0.5f + mapFire.fireSize + 0.5f + humanFire.fireSize;
				var expectedIncludingZombie = expectedExcludingZombie
					+ 0.5f + zombieFire.fireSize
					+ 0.5f + spitterFire.fireSize
					+ 0.5f + symbiantFire.fireSize;
				var tolerance = 0.001f;

				return new
				{
					success = Math.Abs(fireDangerAfter - expectedExcludingZombie) <= tolerance
						&& Math.Abs(fireDangerAfter - expectedIncludingZombie) > 0.1f,
					mapFireCell = ZombieRuntimeActions.DescribeCell(mapFireCell),
					humanCell = ZombieRuntimeActions.DescribeCell(humanCell),
					zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
					spitterCell = ZombieRuntimeActions.DescribeCell(spitterCell),
					symbiantCell = ZombieRuntimeActions.DescribeCell(symbiantCell),
					mapFire = ZombieRuntimeActions.StableThingId(mapFire),
					human = DescribePawn(human),
					zombie = DescribeZombie(zombie),
					spitter = DescribeZombie(spitter),
					symbiant = DescribeZombie(symbiant),
					humanFire = ZombieRuntimeActions.StableThingId(humanFire),
					zombieFire = ZombieRuntimeActions.StableThingId(zombieFire),
					spitterFire = ZombieRuntimeActions.StableThingId(spitterFire),
					symbiantFire = ZombieRuntimeActions.StableThingId(symbiantFire),
					humanFireParent = ZombieRuntimeActions.StableThingId(humanFire.parent),
					zombieFireParent = ZombieRuntimeActions.StableThingId(zombieFire.parent),
					spitterFireParent = ZombieRuntimeActions.StableThingId(spitterFire.parent),
					symbiantFireParent = ZombieRuntimeActions.StableThingId(symbiantFire.parent),
					mapFireSize = mapFire.fireSize,
					humanFireSize = humanFire.fireSize,
					zombieFireSize = zombieFire.fireSize,
					spitterFireSize = spitterFire.fireSize,
					symbiantFireSize = symbiantFire.fireSize,
					fireDangerAfter,
					expectedExcludingZombie,
					expectedIncludingZombie,
					tolerance
				};
			}
			finally
			{
				DestroyFireFixturePawn(symbiant);
				DestroyFireFixturePawn(spitter);
				DestroyFireFixturePawn(zombie);
				DestroyFireFixturePawn(human);
				if (mapFire != null && mapFire.Destroyed == false)
					mapFire.Destroy(DestroyMode.Vanish);
			}
		}

		[Tool("zombieland/zombie_fire_rain_vulnerability_contract", Description = "Verify zombiesBurnLonger lets attached zombie fires sometimes ignore rain while human fires remain vanilla.")]
		public static object ZombieFireRainVulnerabilityContract(
			[ToolParameter(Description = "Deterministic Rand seed used for the enabled zombie-fire sample.", Required = false, DefaultValue = 737373)] int seed = 737373,
			[ToolParameter(Description = "Number of Fire.VulnerableToRain samples to take while zombiesBurnLonger is enabled.", Required = false, DefaultValue = 100)] int samples = 100)
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

			var cappedSamples = Math.Max(20, Math.Min(samples, 500));
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			var fixtureCells = GenRadial.RadialCellsAround(root, 24f, true)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => map.roofGrid.RoofAt(cell) == null)
				.Where(cell => cell.GetThingList(map).Any(thing => thing is Pawn) == false)
				.Take(4)
				.ToArray();
			if (fixtureCells.Length < 4)
			{
				return new
				{
					success = false,
					error = "Could not find four clear unroofed fixture cells for rain-vulnerability sampling."
				};
			}

			var humanCell = fixtureCells[0];
			var zombieCell = fixtureCells[1];
			var spitterCell = fixtureCells[2];
			var symbiantCell = fixtureCells[3];
			foreach (var cell in fixtureCells)
			{
				ClearGasAt(map, cell);
				foreach (var existingFire in cell.GetThingList(map).OfType<Fire>().ToArray())
					existingFire.Destroy();
			}

			var human = SpawnFireFixturePawn(map, humanCell, "human");
			FireUtility.TryAttachFire(human, 1f, null);
			var humanFire = human.GetAttachment(ThingDefOf.Fire) as Fire;

			var zombie = SpawnFireFixturePawn(map, zombieCell, "normal");
			FireUtility.TryAttachFire(zombie, 1f, null);
			var zombieFire = zombie?.GetAttachment(ThingDefOf.Fire) as Fire;

			var spitter = SpawnFireFixturePawn(map, spitterCell, "spitter");
			FireUtility.TryAttachFire(spitter, 1f, null);
			var spitterFire = spitter?.GetAttachment(ThingDefOf.Fire) as Fire;

			var symbiant = SpawnFireFixturePawn(map, symbiantCell, "symbiant");
			FireUtility.TryAttachFire(symbiant, 1f, null);
			var symbiantFire = symbiant?.GetAttachment(ThingDefOf.Fire) as Fire;

			if (humanFire == null || zombieFire == null || spitterFire == null || symbiantFire == null)
			{
				return new
				{
					success = false,
					human = DescribePawn(human),
					zombie = DescribeZombie(zombie),
					spitter = DescribeZombie(spitter),
					symbiant = DescribeZombie(symbiant),
					humanFire = ZombieRuntimeActions.StableThingId(humanFire),
					zombieFire = ZombieRuntimeActions.StableThingId(zombieFire),
					spitterFire = ZombieRuntimeActions.StableThingId(spitterFire),
					symbiantFire = ZombieRuntimeActions.StableThingId(symbiantFire),
					error = "Could not attach human, normal zombie, spitter, and symbiant fires."
				};
			}

			var originalBurnLonger = ZombieSettings.Values.zombiesBurnLonger;
			try
			{
				ZombieSettings.Values.zombiesBurnLonger = true;
				if (TrySampleRainVulnerability(humanFire, cappedSamples, seed + 1, out var humanTrueEnabled, out var humanFalseEnabled, out var humanError) == false)
				{
					return new
					{
						success = false,
						human = DescribePawn(human),
						humanFire = ZombieRuntimeActions.StableThingId(humanFire),
						error = humanError
					};
				}
				if (TrySampleRainVulnerability(zombieFire, cappedSamples, seed, out var zombieTrueEnabled, out var zombieFalseEnabled, out var zombieError) == false)
				{
					return new
					{
						success = false,
						zombie = DescribeZombie(zombie),
						zombieFire = ZombieRuntimeActions.StableThingId(zombieFire),
						error = zombieError
					};
				}
				if (TrySampleRainVulnerability(spitterFire, cappedSamples, seed + 3, out var spitterTrueEnabled, out var spitterFalseEnabled, out var spitterError) == false)
				{
					return new
					{
						success = false,
						spitter = DescribeZombie(spitter),
						spitterFire = ZombieRuntimeActions.StableThingId(spitterFire),
						error = spitterError
					};
				}
				if (TrySampleRainVulnerability(symbiantFire, cappedSamples, seed + 4, out var symbiantTrueEnabled, out var symbiantFalseEnabled, out var symbiantError) == false)
				{
					return new
					{
						success = false,
						symbiant = DescribeZombie(symbiant),
						symbiantFire = ZombieRuntimeActions.StableThingId(symbiantFire),
						error = symbiantError
					};
				}

				ZombieSettings.Values.zombiesBurnLonger = false;
				if (TrySampleRainVulnerability(zombieFire, cappedSamples, seed + 2, out var zombieTrueDisabled, out var zombieFalseDisabled, out var disabledError) == false)
				{
					return new
					{
						success = false,
						zombie = DescribeZombie(zombie),
						zombieFire = ZombieRuntimeActions.StableThingId(zombieFire),
						error = disabledError
					};
				}
				if (TrySampleRainVulnerability(spitterFire, cappedSamples, seed + 5, out var spitterTrueDisabled, out var spitterFalseDisabled, out var spitterDisabledError) == false)
				{
					return new
					{
						success = false,
						spitter = DescribeZombie(spitter),
						spitterFire = ZombieRuntimeActions.StableThingId(spitterFire),
						error = spitterDisabledError
					};
				}
				if (TrySampleRainVulnerability(symbiantFire, cappedSamples, seed + 6, out var symbiantTrueDisabled, out var symbiantFalseDisabled, out var symbiantDisabledError) == false)
				{
					return new
					{
						success = false,
						symbiant = DescribeZombie(symbiant),
						symbiantFire = ZombieRuntimeActions.StableThingId(symbiantFire),
						error = symbiantDisabledError
					};
				}

				var humanVanilla = humanTrueEnabled == cappedSamples && humanFalseEnabled == 0;
				var zombieSometimesProtected = zombieTrueEnabled > 0 && zombieFalseEnabled > 0;
				var spitterSometimesProtected = spitterTrueEnabled > 0 && spitterFalseEnabled > 0;
				var symbiantSometimesProtected = symbiantTrueEnabled > 0 && symbiantFalseEnabled > 0;
				var zombieVanillaWhenDisabled = zombieTrueDisabled == cappedSamples && zombieFalseDisabled == 0;
				var spitterVanillaWhenDisabled = spitterTrueDisabled == cappedSamples && spitterFalseDisabled == 0;
				var symbiantVanillaWhenDisabled = symbiantTrueDisabled == cappedSamples && symbiantFalseDisabled == 0;
				return new
				{
					success = humanVanilla
						&& zombieSometimesProtected
						&& spitterSometimesProtected
						&& symbiantSometimesProtected
						&& zombieVanillaWhenDisabled
						&& spitterVanillaWhenDisabled
						&& symbiantVanillaWhenDisabled,
					seed,
					samples = cappedSamples,
					humanCell = ZombieRuntimeActions.DescribeCell(humanCell),
					zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
					spitterCell = ZombieRuntimeActions.DescribeCell(spitterCell),
					symbiantCell = ZombieRuntimeActions.DescribeCell(symbiantCell),
					human = DescribePawn(human),
					zombie = DescribeZombie(zombie),
					spitter = DescribeZombie(spitter),
					symbiant = DescribeZombie(symbiant),
					humanFire = ZombieRuntimeActions.StableThingId(humanFire),
					zombieFire = ZombieRuntimeActions.StableThingId(zombieFire),
					spitterFire = ZombieRuntimeActions.StableThingId(spitterFire),
					symbiantFire = ZombieRuntimeActions.StableThingId(symbiantFire),
					humanVanilla,
					zombieSometimesProtected,
					spitterSometimesProtected,
					symbiantSometimesProtected,
					zombieVanillaWhenDisabled,
					spitterVanillaWhenDisabled,
					symbiantVanillaWhenDisabled,
					humanTrueEnabled,
					humanFalseEnabled,
					zombieTrueEnabled,
					zombieFalseEnabled,
					spitterTrueEnabled,
					spitterFalseEnabled,
					symbiantTrueEnabled,
					symbiantFalseEnabled,
					zombieTrueDisabled,
					zombieFalseDisabled,
					spitterTrueDisabled,
					spitterFalseDisabled,
					symbiantTrueDisabled,
					symbiantFalseDisabled,
					originalBurnLonger,
					restoredBurnLonger = originalBurnLonger
				};
			}
			finally
			{
				ZombieSettings.Values.zombiesBurnLonger = originalBurnLonger;
				DestroyFireFixturePawn(symbiant);
				DestroyFireFixturePawn(spitter);
				DestroyFireFixturePawn(zombie);
				DestroyFireFixturePawn(human);
			}
		}

		[Tool("zombieland/zombie_fire_survival_boost_contract", Description = "Verify source-aware zombie fire survival boost only applies to natural/non-player animal fire and boosted-zombie propagation, gives boosted zombies quarter external fire damage, and keeps self-fire on the old half damage path.")]
		public static object ZombieFireSurvivalBoostContract(
			[ToolParameter(Description = "Deterministic Rand seed used for paired fire-damage samples.", Required = false, DefaultValue = 757575)] int seed = 757575)
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
			if (fireDoFireDamageMethod == null)
			{
				return new
				{
					success = false,
					error = "Could not find Fire.DoFireDamage(Thing)."
				};
			}

			var animalKind = DefDatabase<PawnKindDef>.AllDefs
				.FirstOrDefault(def => def?.race?.race?.Animal == true && def.race.race.IsMechanoid == false);
			if (animalKind == null)
			{
				return new
				{
					success = false,
					error = "Could not find an animal PawnKindDef for fire-source classification."
				};
			}

			var spawnedThings = new List<Thing>();
			var originalBurnLonger = ZombieSettings.Values.zombiesBurnLonger;
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			const int fixtureCellCount = 16;
			try
			{
				ZombieSettings.Values.zombiesBurnLonger = true;
				var fixtureCells = GenRadial.RadialCellsAround(root, 32f, true)
					.Where(cell => cell.InBounds(map))
					.Where(cell => cell.Standable(map))
					.Where(cell => cell.Fogged(map) == false)
					.Where(cell => cell.GetThingList(map).Any(thing => thing is Pawn) == false)
					.Take(fixtureCellCount)
					.ToArray();
				if (fixtureCells.Length < fixtureCellCount)
				{
					return new
					{
						success = false,
						error = "Could not find sixteen clear fixture cells for source-aware fire survival boost."
					};
				}

				foreach (var cell in fixtureCells)
				{
					ClearGasAt(map, cell);
					ClearFilthAt(map, cell);
					foreach (var existingFire in cell.GetThingList(map).OfType<Fire>().ToArray())
						existingFire.Destroy();
				}

				var wildAnimal = PawnGenerator.GeneratePawn(animalKind, null);
				GenSpawn.Spawn(wildAnimal, fixtureCells[0], map, Rot4.South);
				spawnedThings.Add(wildAnimal);

				var playerAnimal = PawnGenerator.GeneratePawn(animalKind, Faction.OfPlayer);
				GenSpawn.Spawn(playerAnimal, fixtureCells[1], map, Rot4.South);
				spawnedThings.Add(playerAnimal);

				var playerPawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(playerPawn, fixtureCells[2], map, Rot4.South);
				DisablePawnWork(playerPawn);
				spawnedThings.Add(playerPawn);

				var hostileFaction = Find.FactionManager.AllFactionsVisible
					.FirstOrDefault(faction => faction != null && faction.HostileTo(Faction.OfPlayer) && faction.def?.humanlikeFaction == true)
					?? Faction.OfAncientsHostile;
				var hostilePawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, hostileFaction);
				GenSpawn.Spawn(hostilePawn, fixtureCells[3], map, Rot4.South);
				DisablePawnWork(hostilePawn);
				spawnedThings.Add(hostilePawn);

				object AttachCase(string name, Thing instigator, IntVec3 cell, bool expectedBoost)
				{
					var zombie = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.Normal, true);
					if (zombie != null)
						spawnedThings.Add(zombie);
					FireUtility.TryAttachFire(zombie, 1f, instigator);

					var fire = zombie?.GetAttachment(ThingDefOf.Fire) as Fire;
					var boosted = zombie?.HasFireSurvivalBoost ?? false;
					var boostFlag = zombie?.fireSurvivalBoost ?? false;
					return new
					{
						name,
						success = zombie != null && fire != null && boosted == expectedBoost && boostFlag == expectedBoost,
						expectedBoost,
						boostFlag,
						boosted,
						zombie = DescribeZombie(zombie),
						fire = ZombieRuntimeActions.StableThingId(fire),
						instigator = DescribeFireInstigator(instigator)
					};
				}

				var naturalCase = AttachCase("naturalNullInstigator", null, fixtureCells[4], true);
				var wildAnimalCase = AttachCase("wildAnimalInstigator", wildAnimal, fixtureCells[5], true);
				var playerAnimalCase = AttachCase("playerAnimalInstigator", playerAnimal, fixtureCells[6], false);
				var playerPawnCase = AttachCase("playerPawnInstigator", playerPawn, fixtureCells[7], false);
				var hostilePawnCase = AttachCase("hostilePawnInstigator", hostilePawn, fixtureCells[8], false);

				var boostedSource = ZombieRuntimeActions.SpawnZombie(fixtureCells[9], map, ZombieType.Normal, true);
				if (boostedSource != null)
					spawnedThings.Add(boostedSource);
				FireUtility.TryAttachFire(boostedSource, 1f, null);
				var propagatedCase = AttachCase("boostedZombieInstigator", boostedSource, fixtureCells[10], true);

				var plainSource = ZombieRuntimeActions.SpawnZombie(fixtureCells[11], map, ZombieType.Normal, true);
				if (plainSource != null)
					spawnedThings.Add(plainSource);
				var plainPropagationCase = AttachCase("plainZombieInstigator", plainSource, fixtureCells[12], false);

				var damageFire = ThingMaker.MakeThing(ThingDefOf.Fire) as Fire;
				GenSpawn.Spawn(damageFire, fixtureCells[13], map, WipeMode.Vanish);
				damageFire.fireSize = Fire.MaxFireSize;
				damageFire.instigator = playerPawn;
				spawnedThings.Add(damageFire);

				var normalDamage = SampleSourceAwareZombieFireDamage(map, damageFire, fixtureCells[4] + new IntVec3(3, 0, 0), false, seed);
				var boostedDamage = SampleSourceAwareZombieFireDamage(map, damageFire, fixtureCells[4] + new IntVec3(6, 0, 0), true, seed);
				var realDamageReduced = normalDamage.error == null
					&& boostedDamage.error == null
					&& normalDamage.injuryDelta > 0f
					&& boostedDamage.injuryDelta > 0f
					&& boostedDamage.injuryDelta < normalDamage.injuryDelta;

				var synthetic = SyntheticFireDamagePatchSample(playerPawn, boostedSource, seed + 1000);
				var syntheticDamageShape = synthetic.error == null
					&& synthetic.humanDamage > 0
					&& synthetic.boostedExternalFireDamage > 0
					&& synthetic.boostedSelfFireDamage > 0
					&& synthetic.humanDamage == synthetic.boostedExternalFireDamage * 4
					&& synthetic.humanDamage == synthetic.boostedSelfFireDamage * 2;

				var eligibleExplosion = SampleSourceAwareExplosionDamage(map, wildAnimal, fixtureCells[14], true, seed + 2000);
				var playerExplosion = SampleSourceAwareExplosionDamage(map, playerPawn, fixtureCells[15], false, seed + 3000);
				var explosionDamageShape = SourceAwareExplosionDamageShapeSucceeded(eligibleExplosion, playerExplosion);

				var cases = new[]
				{
					naturalCase,
					wildAnimalCase,
					playerAnimalCase,
					playerPawnCase,
					hostilePawnCase,
					propagatedCase,
					plainPropagationCase
				};

				return new
				{
					success = cases.All(FireSurvivalCaseSucceeded) && realDamageReduced && syntheticDamageShape && explosionDamageShape,
					patchTargets = new
					{
						addAttachment = PatchedMethodsForPatchClass("CompAttachBase_AddAttachment_Patch"),
						removeAttachment = PatchedMethodsForPatchClass("CompAttachBase_RemoveAttachment_Patch"),
						fireDamage = PatchedMethodsForPatchClass("Fire_DoFireDamage_Patch"),
						explosionAffectCell = PatchedMethodsForPatchClass("Explosion_AffectCell_Patch")
					},
					animalKind = animalKind.defName,
					cases,
					damage = new
					{
						realDamageReduced,
						normal = normalDamage,
						boosted = boostedDamage,
						syntheticDamageShape,
						synthetic
					},
					explosion = new
					{
						explosionDamageShape,
						eligible = eligibleExplosion,
						player = playerExplosion
					}
				};
			}
			finally
			{
				ZombieSettings.Values.zombiesBurnLonger = originalBurnLonger;
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}
		}

		static bool FireSurvivalCaseSucceeded(object item)
		{
			var property = item?.GetType().GetProperty("success");
			return property?.GetValue(item) is bool success && success;
		}

		static bool SourceAwareExplosionDamageShapeSucceeded(ExplosionDamageSample eligibleExplosion, ExplosionDamageSample playerExplosion)
		{
			return eligibleExplosion.error == null
				&& playerExplosion.error == null
				&& eligibleExplosion.boosted
				&& eligibleExplosion.injuryDelta <= 0.001f
				&& playerExplosion.boosted == false
				&& playerExplosion.injuryDelta > 0f;
		}

		static object DescribeFireInstigator(Thing instigator)
		{
			var pawn = instigator as Pawn;
			var fire = instigator as Fire;
			return new
			{
				thingId = ZombieRuntimeActions.StableThingId(instigator),
				defName = instigator?.def?.defName,
				type = instigator?.GetType().Name,
				faction = instigator?.Faction?.def?.defName,
				isPawn = pawn != null,
				isAnimal = pawn?.RaceProps?.Animal,
				isHumanlike = pawn?.RaceProps?.Humanlike,
				isMechanoid = pawn?.RaceProps?.IsMechanoid,
				isZombie = pawn is Zombie,
				zombieBoosted = (pawn as Zombie)?.HasFireSurvivalBoost,
				fireParent = ZombieRuntimeActions.StableThingId(fire?.parent),
				fireSource = ZombieRuntimeActions.StableThingId(fire?.instigator)
			};
		}

		static FireDamageSample SampleSourceAwareZombieFireDamage(Map map, Fire fire, IntVec3 cell, bool boosted, int seed)
		{
			Zombie zombie = null;
			try
			{
				foreach (var existingPawn in cell.GetThingList(map).OfType<Pawn>().ToArray())
					existingPawn.Destroy(DestroyMode.Vanish);

				zombie = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.Normal, true);
				if (zombie == null)
				{
					return new FireDamageSample
					{
						kind = boosted ? "boosted" : "normal",
						seed = seed,
						burnLonger = ZombieSettings.Values.zombiesBurnLonger,
						error = "Could not spawn a normal zombie for source-aware fire damage sampling."
					};
				}

				NormalizeFireDamagePawn(zombie);
				zombie.NotifyFireAttached(boosted);

				var before = TotalInjurySeverity(zombie);
				Rand.PushState(seed);
				try
				{
					if (TryDoFireDamage(fire, zombie, out var error) == false)
					{
						return new FireDamageSample
						{
							kind = boosted ? "boosted" : "normal",
							seed = seed,
							burnLonger = ZombieSettings.Values.zombiesBurnLonger,
							injuryBefore = before,
							injuryAfter = TotalInjurySeverity(zombie),
							injuryDelta = TotalInjurySeverity(zombie) - before,
							deadAfter = zombie.Dead,
							pawn = DescribeZombie(zombie),
							error = error
						};
					}
				}
				finally
				{
					Rand.PopState();
				}

				var after = TotalInjurySeverity(zombie);
				return new FireDamageSample
				{
					kind = boosted ? "boosted" : "normal",
					seed = seed,
					burnLonger = ZombieSettings.Values.zombiesBurnLonger,
					injuryBefore = before,
					injuryAfter = after,
					injuryDelta = after - before,
					deadAfter = zombie.Dead,
					pawn = DescribeZombie(zombie)
				};
			}
			finally
			{
				if (zombie != null && zombie.Destroyed == false)
					zombie.Destroy(DestroyMode.Vanish);
			}
		}

		static ExplosionDamageSample SampleSourceAwareExplosionDamage(Map map, Thing instigator, IntVec3 cell, bool expectedBoost, int seed)
		{
			Zombie zombie = null;
			Verse.Explosion explosion = null;
			var last = new ExplosionDamageSample
			{
				kind = expectedBoost ? "eligible" : "ineligible",
				seed = seed,
				error = "No flame explosion attempt attached fire to the zombie."
			};

			for (var attempt = 0; attempt < 25; attempt++)
			{
				try
				{
					foreach (var existingPawn in cell.GetThingList(map).OfType<Pawn>().ToArray())
						existingPawn.Destroy(DestroyMode.Vanish);
					foreach (var existingFire in cell.GetThingList(map).OfType<Fire>().ToArray())
						existingFire.Destroy(DestroyMode.Vanish);

					zombie = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.Normal, true);
					if (zombie == null)
					{
						last = new ExplosionDamageSample
						{
							kind = expectedBoost ? "eligible" : "ineligible",
							seed = seed + attempt,
							error = "Could not spawn a normal zombie for source-aware explosion damage sampling."
						};
						continue;
					}

					NormalizeFireDamagePawn(zombie);
					var injuryBefore = TotalInjurySeverity(zombie);
					explosion = ThingMaker.MakeThing(ThingDefOf.Explosion) as Verse.Explosion;
					if (explosion == null)
					{
						last = new ExplosionDamageSample
						{
							kind = expectedBoost ? "eligible" : "ineligible",
							seed = seed + attempt,
							pawn = DescribeZombie(zombie),
							error = "ThingDefOf.Explosion did not create a Verse.Explosion instance."
						};
						continue;
					}

					explosion.radius = 0.9f;
					explosion.damType = DamageDefOf.Flame;
					explosion.damAmount = 30;
					explosion.armorPenetration = 0f;
					explosion.instigator = instigator;
					explosion.chanceToStartFire = 0f;
					explosion.propagationSpeed = 1f;
					explosion.doVisualEffects = false;
					explosion.doSoundEffects = false;
					GenSpawn.Spawn(explosion, cell, map, WipeMode.Vanish);
					Rand.PushState(seed + attempt);
					try
					{
						explosion.StartExplosion(null, null);
						AdvanceGameTicks(3);
					}
					finally
					{
						Rand.PopState();
					}

					var fire = zombie.GetAttachment(ThingDefOf.Fire) as Fire;
					var injuryAfter = TotalInjurySeverity(zombie);
					last = new ExplosionDamageSample
					{
						kind = expectedBoost ? "eligible" : "ineligible",
						seed = seed + attempt,
						attempt = attempt,
						injuryBefore = injuryBefore,
						injuryAfter = injuryAfter,
						injuryDelta = injuryAfter - injuryBefore,
						boosted = zombie.HasFireSurvivalBoost,
						boostFlag = zombie.fireSurvivalBoost,
						fire = ZombieRuntimeActions.StableThingId(fire),
						fireSize = fire == null || fire.Destroyed ? (float?)null : fire.fireSize,
						deadAfter = zombie.Dead,
						pawn = DescribeZombie(zombie),
						instigator = DescribeFireInstigator(instigator)
					};
					if (fire != null)
						return last;
				}
				finally
				{
					if (explosion != null && explosion.Destroyed == false)
						explosion.Destroy(DestroyMode.Vanish);
					if (zombie != null && zombie.Destroyed == false)
						zombie.Destroy(DestroyMode.Vanish);
					explosion = null;
					zombie = null;
				}
			}

			return last;
		}

		static SyntheticFireDamagePatchResult SyntheticFireDamagePatchSample(Pawn human, Zombie boostedZombie, int seed)
		{
			var nested = typeof(Patches).GetNestedType("Fire_DoFireDamage_Patch", BindingFlags.NonPublic);
			var method = nested?.GetMethod("FireDamagePatch", BindingFlags.Static | BindingFlags.NonPublic);
			if (method == null)
			{
				return new SyntheticFireDamagePatchResult
				{
					error = "Could not reflect Patches.Fire_DoFireDamage_Patch.FireDamagePatch(float, Fire, Thing)."
				};
			}
			if (boostedZombie == null)
			{
				return new SyntheticFireDamagePatchResult
				{
					error = "Boosted zombie fixture is null."
				};
			}

			boostedZombie.NotifyFireAttached(true);
			var selfFire = boostedZombie.GetAttachment(ThingDefOf.Fire) as Fire;
			if (selfFire == null)
			{
				selfFire = ThingMaker.MakeThing(ThingDefOf.Fire) as Fire;
				selfFire.fireSize = 1f;
				selfFire.AttachTo(boostedZombie);
			}
			var originalBurnLonger = ZombieSettings.Values.zombiesBurnLonger;
			try
			{
				ZombieSettings.Values.zombiesBurnLonger = true;
				Rand.PushState(seed);
				var humanDamage = Convert.ToInt32(method.Invoke(null, new object[] { 16f, null, human }));
				Rand.PopState();
				Rand.PushState(seed);
				var boostedExternalFireDamage = Convert.ToInt32(method.Invoke(null, new object[] { 16f, null, boostedZombie }));
				Rand.PopState();
				Rand.PushState(seed);
				var boostedSelfFireDamage = Convert.ToInt32(method.Invoke(null, new object[] { 16f, selfFire, boostedZombie }));
				Rand.PopState();
				return new SyntheticFireDamagePatchResult
				{
					input = 16f,
					humanDamage = humanDamage,
					boostedExternalFireDamage = boostedExternalFireDamage,
					boostedSelfFireDamage = boostedSelfFireDamage
				};
			}
			catch (Exception ex)
			{
				return new SyntheticFireDamagePatchResult
				{
					error = ex.InnerException?.Message ?? ex.Message
				};
			}
			finally
			{
				ZombieSettings.Values.zombiesBurnLonger = originalBurnLonger;
			}
		}

		sealed class SyntheticFireDamagePatchResult
		{
			public string error { get; set; }
			public float input { get; set; }
			public int humanDamage { get; set; }
			public int boostedExternalFireDamage { get; set; }
			public int boostedSelfFireDamage { get; set; }
		}

		sealed class ExplosionDamageSample
		{
			public string kind { get; set; }
			public string error { get; set; }
			public int seed { get; set; }
			public int attempt { get; set; }
			public float injuryBefore { get; set; }
			public float injuryAfter { get; set; }
			public float injuryDelta { get; set; }
			public bool boosted { get; set; }
			public bool boostFlag { get; set; }
			public string fire { get; set; }
			public float? fireSize { get; set; }
			public bool deadAfter { get; set; }
			public object pawn { get; set; }
			public object instigator { get; set; }
		}

		[Tool("zombieland/zombie_fire_damage_reduction_contract", Description = "Verify zombiesBurnLonger reduces real Fire.DoFireDamage for all Zombieland pawn fire fixtures while humans still take ordinary fire damage.")]
		public static object ZombieFireDamageReductionContract(
			[ToolParameter(Description = "Deterministic Rand seed used for paired Fire.DoFireDamage samples.", Required = false, DefaultValue = 747474)] int seed = 747474,
			[ToolParameter(Description = "Number of paired damage samples per pawn kind.", Required = false, DefaultValue = 12)] int samples = 12)
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
			if (fireDoFireDamageMethod == null)
			{
				return new
				{
					success = false,
					error = "Could not find Fire.DoFireDamage(Thing)."
				};
			}

			var cappedSamples = Math.Max(4, Math.Min(samples, 30));
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			var fixtureCells = GenRadial.RadialCellsAround(root, 24f, true)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetThingList(map).Any(thing => thing is Pawn) == false)
				.Take(5)
				.ToArray();
			if (fixtureCells.Length < 5)
			{
				return new
				{
					success = false,
					error = "Could not find five clear fixture cells for fire-damage sampling."
				};
			}

			var fireCell = fixtureCells[0];
			foreach (var cell in fixtureCells)
			{
				ClearGasAt(map, cell);
				ClearFilthAt(map, cell);
				foreach (var existingFire in cell.GetThingList(map).OfType<Fire>().ToArray())
					existingFire.Destroy();
			}

			FilthMaker.TryMakeFilth(fireCell, map, CustomDefs.TarSlime);
			var fireSubstrate = map.thingGrid.ThingAt<TarSlime>(fireCell);
			if (fireSubstrate == null)
			{
				return new
				{
					success = false,
					fireCell = ZombieRuntimeActions.DescribeCell(fireCell),
					error = "Could not create a flammable TarSlime substrate for fire-damage sampling."
				};
			}

			FireUtility.TryStartFireIn(fireCell, map, Fire.MaxFireSize, null);
			var fire = fireCell.GetThingList(map).OfType<Fire>().FirstOrDefault();
			if (fire == null)
			{
				return new
				{
					success = false,
					fireCell = ZombieRuntimeActions.DescribeCell(fireCell),
					fireSubstrate = ZombieRuntimeActions.StableThingId(fireSubstrate),
					error = "Could not start a real fire for fire-damage sampling."
				};
			}
			fire.fireSize = Fire.MaxFireSize;

			var human = CompareFireDamage(map, fire, fixtureCells[1], "human", cappedSamples, seed);
			var normal = CompareFireDamage(map, fire, fixtureCells[2], "normal", cappedSamples, seed + 1000);
			var spitter = CompareFireDamage(map, fire, fixtureCells[3], "spitter", cappedSamples, seed + 2000);
			var symbiant = CompareFireDamage(map, fire, fixtureCells[4], "symbiant", cappedSamples, seed + 3000);

			var tolerance = 0.001f;
			var humanFireDamageControl = human.disabledTotal > tolerance && human.enabledTotal > tolerance;
			var normalReduced = normal.enabledTotal < normal.disabledTotal - tolerance;
			var spitterReduced = spitter.enabledTotal < spitter.disabledTotal - tolerance;
			var symbiantReduced = symbiant.enabledTotal < symbiant.disabledTotal - tolerance;
			var noErrors = new[] { human, normal, spitter, symbiant }.Any(comparison => comparison.errors.Length > 0) == false;

			return new
			{
				success = noErrors && humanFireDamageControl && normalReduced && spitterReduced && symbiantReduced,
				seed,
				samples = cappedSamples,
				fireCell = ZombieRuntimeActions.DescribeCell(fireCell),
				fire = ZombieRuntimeActions.StableThingId(fire),
				fireSubstrate = ZombieRuntimeActions.StableThingId(fireSubstrate),
				fireSize = fire.fireSize,
				humanFireDamageControl,
				normalReduced,
				spitterReduced,
				symbiantReduced,
				noErrors,
				human,
				normal,
				spitter,
				symbiant,
				tolerance
			};
		}

		[Tool("zombieland/zombie_damage_log_association_suppression", Description = "Verify RimWorld DamageResult combat-log association fills human hediff logs but skips all Zombieland pawn types.")]
		public static object ZombieDamageLogAssociationSuppression()
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
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var humanCell, out var humanError) == false)
					return humanError;
				if (TryFindClearSpawnCell(map, humanCell + new IntVec3(3, 0, 0), 10f, out var zombieCell, out var zombieError) == false)
					return zombieError;
				if (TryFindClearSpawnCell(map, humanCell + new IntVec3(-3, 0, 0), 10f, out var spitterCell, out var spitterError) == false)
					return spitterError;
				if (TryFindClearSpawnCell(map, humanCell + new IntVec3(0, 0, 3), 10f, out var symbiantCell, out var symbiantError) == false)
					return symbiantError;

				var human = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(human, humanCell, map, Rot4.South);
				spawnedThings.Add(human);
				DisablePawnWork(human);

				var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
				if (zombie != null)
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

				if (zombie == null || spitter == null || symbiant == null)
				{
					return new
					{
						success = false,
						human = DescribePawn(human),
						zombie = DescribeZombie(zombie),
						spitter = DescribeZombie(spitter),
						symbiant = DescribeZombie(symbiant),
						error = "Could not create all damage-log fixture pawns."
					};
				}

				var humanResult = DamageAndAssociateWithLog(human, 2f);
				var zombieResult = DamageAndAssociateWithLog(zombie, 2f);
				var spitterResult = DamageAndAssociateWithLog(spitter, 0.5f);
				var symbiantResult = DamageAndAssociateWithLog(symbiant, 0.5f);

				var humanAssociated = humanResult.resultHediffCount > 0 && humanResult.combatTextDelta > 0;
				var zombieSuppressed = zombieResult.resultHediffCount > 0 && zombieResult.combatTextDelta == 0;
				var spitterSuppressed = spitterResult.resultHediffCount > 0 && spitterResult.combatTextDelta == 0;
				var symbiantSuppressed = symbiantResult.resultHediffCount > 0 && symbiantResult.combatTextDelta == 0;

				return new
				{
					success = humanAssociated && zombieSuppressed && spitterSuppressed && symbiantSuppressed,
					humanAssociated,
					zombieSuppressed,
					spitterSuppressed,
					symbiantSuppressed,
					human = humanResult,
					zombie = zombieResult,
					spitter = spitterResult,
					symbiant = symbiantResult
				};
			}
			finally
			{
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}
		}

		[Tool("zombieland/zombie_fire_attachment_state_contract", Description = "Verify real fire attachment add/remove keeps Zombie.isOnFire synchronized.")]
		public static object ZombieFireAttachmentStateContract()
		{
			var addTargets = PatchedMethodsForPatchClass("CompAttachBase_AddAttachment_Patch");
			var removeTargets = PatchedMethodsForPatchClass("CompAttachBase_RemoveAttachment_Patch");
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			Zombie zombie = null;
			Fire fire = null;
			Fire secondFire = null;
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var cell, out var spawnError) == false)
				return spawnError;

			try
			{
				zombie = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.Normal, true);
				if (zombie == null)
				{
					return new
					{
						success = false,
						patchTargets = new
						{
							add = addTargets,
							remove = removeTargets
						},
						cell = ZombieRuntimeActions.DescribeCell(cell),
						error = "Could not spawn a normal zombie for fire attachment state."
					};
				}

				var initialHasFire = zombie.HasAttachment(ThingDefOf.Fire);
				var initialIsOnFire = zombie.isOnFire;
				var initialFireAttachmentCount = FireAttachmentCount(zombie);
				FireUtility.TryAttachFire(zombie, 1f, null);
				fire = zombie.GetAttachment(ThingDefOf.Fire) as Fire;
				var afterAttachHasFire = zombie.HasAttachment(ThingDefOf.Fire);
				var afterAttachIsOnFire = zombie.isOnFire;
				var afterAttachFireAttachmentCount = FireAttachmentCount(zombie);

				secondFire = ThingMaker.MakeThing(ThingDefOf.Fire) as Fire;
				secondFire.fireSize = 0.75f;
				secondFire.AttachTo(zombie);
				var afterSecondAttachHasFire = zombie.HasAttachment(ThingDefOf.Fire);
				var afterSecondAttachIsOnFire = zombie.isOnFire;
				var afterSecondAttachFireAttachmentCount = FireAttachmentCount(zombie);

				var comp = zombie.TryGetComp<CompAttachBase>();
				if (fire != null)
					comp?.RemoveAttachment(fire);
				var afterRemoveFirstHasFire = zombie.HasAttachment(ThingDefOf.Fire);
				var afterRemoveFirstIsOnFire = zombie.isOnFire;
				var afterRemoveFirstFireAttachmentCount = FireAttachmentCount(zombie);

				if (secondFire != null)
					comp?.RemoveAttachment(secondFire);
				var afterRemoveSecondHasFire = zombie.HasAttachment(ThingDefOf.Fire);
				var afterRemoveSecondIsOnFire = zombie.isOnFire;
				var afterRemoveSecondFireAttachmentCount = FireAttachmentCount(zombie);

				return new
				{
					success = addTargets.Length > 0
						&& removeTargets.Length > 0
						&& initialHasFire == false
						&& initialIsOnFire == false
						&& initialFireAttachmentCount == 0
						&& fire != null
						&& afterAttachHasFire
						&& afterAttachIsOnFire
						&& afterAttachFireAttachmentCount == 1
						&& secondFire != null
						&& afterSecondAttachHasFire
						&& afterSecondAttachIsOnFire
						&& afterSecondAttachFireAttachmentCount == 2
						&& afterRemoveFirstHasFire
						&& afterRemoveFirstIsOnFire
						&& afterRemoveFirstFireAttachmentCount == 1
						&& afterRemoveSecondHasFire == false
						&& afterRemoveSecondIsOnFire == false
						&& afterRemoveSecondFireAttachmentCount == 0,
					patchTargets = new
					{
						add = addTargets,
						remove = removeTargets
					},
					zombie = DescribeZombie(zombie),
					fire = ZombieRuntimeActions.StableThingId(fire),
					secondFire = ZombieRuntimeActions.StableThingId(secondFire),
					initialHasFire,
					initialIsOnFire,
					initialFireAttachmentCount,
					afterAttachHasFire,
					afterAttachIsOnFire,
					afterAttachFireAttachmentCount,
					afterSecondAttachHasFire,
					afterSecondAttachIsOnFire,
					afterSecondAttachFireAttachmentCount,
					afterRemoveFirstHasFire,
					afterRemoveFirstIsOnFire,
					afterRemoveFirstFireAttachmentCount,
					afterRemoveSecondHasFire,
					afterRemoveSecondIsOnFire,
					afterRemoveSecondFireAttachmentCount
				};
			}
			finally
			{
				if (fire != null && fire.Destroyed == false)
					fire.Destroy(DestroyMode.Vanish);
				if (secondFire != null && secondFire.Destroyed == false)
					secondFire.Destroy(DestroyMode.Vanish);
				if (zombie != null && zombie.Destroyed == false)
					zombie.Destroy(DestroyMode.Vanish);
			}
		}

		static int FireAttachmentCount(Pawn pawn)
		{
			var comp = pawn?.TryGetComp<CompAttachBase>();
			var field = typeof(CompAttachBase).GetField("attachments", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			return (field?.GetValue(comp) as List<AttachableThing>)?.Count(attachment => attachment?.def == ThingDefOf.Fire) ?? 0;
		}

		sealed class AmbientTemperatureCase
		{
			public string name { get; set; }
			public string thingId { get; set; }
			public string thingType { get; set; }
			public string defName { get; set; }
			public object position { get; set; }
			public float ambientTemperature { get; set; }
			public float cellTemperature { get; set; }
			public bool expectNormal { get; set; }
			public bool success { get; set; }
		}

		static AmbientTemperatureCase DescribeAmbientTemperature(string name, Thing thing, Map map, bool expectNormal)
		{
			var ambientTemperature = thing?.AmbientTemperature ?? float.NaN;
			var cellTemperature = thing != null && thing.Spawned
				? GenTemperature.GetTemperatureForCell(thing.Position, map)
				: float.NaN;
			var success = thing != null
				&& (expectNormal
					? Mathf.Abs(ambientTemperature - 21f) <= 0.001f
					: Mathf.Abs(ambientTemperature - cellTemperature) <= 0.001f);
			return new AmbientTemperatureCase
			{
				name = name,
				thingId = ZombieRuntimeActions.StableThingId(thing),
				thingType = thing?.GetType().FullName,
				defName = thing?.def?.defName,
				position = thing == null || thing.Spawned == false ? null : ZombieRuntimeActions.DescribeCell(thing.Position),
				ambientTemperature = ambientTemperature,
				cellTemperature = cellTemperature,
				expectNormal = expectNormal,
				success = success
			};
		}

	}
}
