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
		sealed class AnomalyMatrixCase
		{
			public string id;
			public string kindDef;
			public string faction;
			public string activityState;
			public string dormancyState;
		}

		sealed class AnomalyMatrixRuntimeRow
		{
			public AnomalyMatrixCase testCase;
			public Pawn pawn;
			public Zombie zombie;
		}

		sealed class AnomalyTargetingOverrideCase
		{
			public string id;
			public string kindDef;
			public string faction;
			public string activityState;
			public string dormancyState;
			public AttackMode attackMode;
			public AnomalyTargetingCategory category;
			public AnomalyTargetingOverride mode;
			public bool expectedOverridePresent;
			public bool expectedOverrideAttracts;
			public bool expectedAttracts;
			public bool expectedAttackable;
		}

		sealed class AnomalyZombieHostilityOverrideCase
		{
			public string id;
			public string kindDef;
			public string faction;
			public string activityState;
			public string dormancyState;
			public bool enemiesAttackZombies;
			public bool animalsAttackZombies;
			public AnomalyTargetingOverride mode;
			public bool expectedOverridePresent;
			public bool expectedOverrideAttacks;
			public bool expectedHostile;
		}

		sealed class AnomalyEngagementState
		{
			public string testCase;
			public string mode;
			public object pawn;
			public object zombie;
			public object activity;
			public object dormancy;
			public object vanillaThreat;
			public bool attracts;
			public bool attackable;
			public object reverseHostility;
			public object grid;
			public long targetTimestamp;
			public long zombieTimestamp;
			public bool anyAdjacentTimestamp;
			public bool anyAdjacentZombieCount;
			public object stumbleDriver;
			public bool zombieTracking;
			public bool zombiePatherMoving;
		}

		static readonly AnomalyMatrixCase[] anomalyMatrixCases =
		{
			// Nociosphere skip/onslaught AI is not safe in this static zombie-pair matrix.
			new() { id = "FleshmassNucleus-active", kindDef = "FleshmassNucleus", faction = "entities", activityState = "active" },
			new() { id = "FleshmassNucleus-passive", kindDef = "FleshmassNucleus", faction = "entities", activityState = "passive" },
			new() { id = "Metalhorror-awake", kindDef = "Metalhorror", faction = "entities", dormancyState = "awake" },
			new() { id = "Metalhorror-dormant", kindDef = "Metalhorror", faction = "entities", dormancyState = "dormant" },
			new() { id = "Revenant", kindDef = "Revenant", faction = "entities" },
			new() { id = "Sightstealer", kindDef = "Sightstealer", faction = "entities" },
			new() { id = "Noctol", kindDef = "Noctol", faction = "entities" },
			new() { id = "Gorehulk", kindDef = "Gorehulk", faction = "entities" },
			new() { id = "Devourer", kindDef = "Devourer", faction = "entities" },
			new() { id = "Chimera", kindDef = "Chimera", faction = "entities" },
			new() { id = "Bulbfreak", kindDef = "Bulbfreak", faction = "entities" },
			new() { id = "Fingerspike-awake", kindDef = "Fingerspike", faction = "entities", dormancyState = "awake" },
			new() { id = "Fingerspike-dormant", kindDef = "Fingerspike", faction = "entities", dormancyState = "dormant" },
			new() { id = "Toughspike", kindDef = "Toughspike", faction = "entities" },
			new() { id = "Trispike", kindDef = "Trispike", faction = "entities" },
			new() { id = "Dreadmeld", kindDef = "Dreadmeld", faction = "entities" },
			new() { id = "ShamblerSwarmer-awake", kindDef = "ShamblerSwarmer", faction = "entities", dormancyState = "awake" },
			new() { id = "ShamblerSoldier-awake", kindDef = "ShamblerSoldier", faction = "entities", dormancyState = "awake" },
			new() { id = "ShamblerGorehulk-awake", kindDef = "ShamblerGorehulk", faction = "entities", dormancyState = "awake" },
			new() { id = "Ghoul-player", kindDef = "Ghoul", faction = "player" },
			new() { id = "Ghoul-entities", kindDef = "Ghoul", faction = "entities" },
			new() { id = "Horaxian_Underthrall", kindDef = "Horaxian_Underthrall", faction = "horax" },
			new() { id = "Horaxian_Gunner", kindDef = "Horaxian_Gunner", faction = "horax" },
			new() { id = "Horaxian_Highthrall", kindDef = "Horaxian_Highthrall", faction = "horax" },
		};

		static readonly AnomalyTargetingOverrideCase[] anomalyTargetingOverrideCases =
		{
			new() { id = "ghoul-automatic-onlyHumans", kindDef = "Ghoul", faction = "player", attackMode = AttackMode.OnlyHumans, category = AnomalyTargetingCategory.Ghouls, mode = AnomalyTargetingOverride.Automatic, expectedAttracts = true, expectedAttackable = true },
			new() { id = "ghoul-automatic-onlyColonists", kindDef = "Ghoul", faction = "player", attackMode = AttackMode.OnlyColonists, category = AnomalyTargetingCategory.Ghouls, mode = AnomalyTargetingOverride.Automatic, expectedAttracts = false, expectedAttackable = false },
			new() { id = "ghoul-never-everything", kindDef = "Ghoul", faction = "player", attackMode = AttackMode.Everything, category = AnomalyTargetingCategory.Ghouls, mode = AnomalyTargetingOverride.Never, expectedOverridePresent = true, expectedOverrideAttracts = false, expectedAttracts = false, expectedAttackable = false },
			new() { id = "ghoul-allow-onlyColonists", kindDef = "Ghoul", faction = "player", attackMode = AttackMode.OnlyColonists, category = AnomalyTargetingCategory.Ghouls, mode = AnomalyTargetingOverride.Allow, expectedOverridePresent = true, expectedOverrideAttracts = true, expectedAttracts = true, expectedAttackable = true },
			new() { id = "shambler-human-automatic-onlyHumans", kindDef = "ShamblerSoldier", faction = "entities", dormancyState = "awake", attackMode = AttackMode.OnlyHumans, category = AnomalyTargetingCategory.ShamblerMutants, mode = AnomalyTargetingOverride.Automatic, expectedAttracts = true, expectedAttackable = true },
			new() { id = "shambler-gorehulk-automatic-onlyHumans", kindDef = "ShamblerGorehulk", faction = "entities", dormancyState = "awake", attackMode = AttackMode.OnlyHumans, category = AnomalyTargetingCategory.ShamblerMutants, mode = AnomalyTargetingOverride.Automatic, expectedAttracts = false, expectedAttackable = false },
			new() { id = "shambler-never-everything", kindDef = "ShamblerSoldier", faction = "entities", dormancyState = "awake", attackMode = AttackMode.Everything, category = AnomalyTargetingCategory.ShamblerMutants, mode = AnomalyTargetingOverride.Never, expectedOverridePresent = true, expectedOverrideAttracts = false, expectedAttracts = false, expectedAttackable = false },
			new() { id = "shambler-allow-onlyHumans", kindDef = "ShamblerGorehulk", faction = "entities", dormancyState = "awake", attackMode = AttackMode.OnlyHumans, category = AnomalyTargetingCategory.ShamblerMutants, mode = AnomalyTargetingOverride.Allow, expectedOverridePresent = true, expectedOverrideAttracts = true, expectedAttracts = true, expectedAttackable = true },
			new() { id = "entity-automatic-onlyHumans", kindDef = "Metalhorror", faction = "entities", dormancyState = "awake", attackMode = AttackMode.OnlyHumans, category = AnomalyTargetingCategory.OtherEntities, mode = AnomalyTargetingOverride.Automatic, expectedAttracts = false, expectedAttackable = false },
			new() { id = "entity-allow-onlyHumans", kindDef = "Metalhorror", faction = "entities", dormancyState = "awake", attackMode = AttackMode.OnlyHumans, category = AnomalyTargetingCategory.OtherEntities, mode = AnomalyTargetingOverride.Allow, expectedOverridePresent = true, expectedOverrideAttracts = true, expectedAttracts = true, expectedAttackable = true },
			new() { id = "entity-never-everything", kindDef = "Gorehulk", faction = "entities", attackMode = AttackMode.Everything, category = AnomalyTargetingCategory.OtherEntities, mode = AnomalyTargetingOverride.Never, expectedOverridePresent = true, expectedOverrideAttracts = false, expectedAttracts = false, expectedAttackable = false },
			new() { id = "entity-dormant-allow-hardgate", kindDef = "Metalhorror", faction = "entities", dormancyState = "dormant", attackMode = AttackMode.Everything, category = AnomalyTargetingCategory.OtherEntities, mode = AnomalyTargetingOverride.Allow, expectedOverridePresent = true, expectedOverrideAttracts = true, expectedAttracts = false, expectedAttackable = false },
			new() { id = "nociosphere-automatic-onlyHumans", kindDef = "Nociosphere", faction = "entities", activityState = "active", attackMode = AttackMode.OnlyHumans, category = AnomalyTargetingCategory.Nociosphere, mode = AnomalyTargetingOverride.Automatic, expectedAttracts = false, expectedAttackable = false },
			new() { id = "nociosphere-allow-onlyHumans", kindDef = "Nociosphere", faction = "entities", activityState = "active", attackMode = AttackMode.OnlyHumans, category = AnomalyTargetingCategory.Nociosphere, mode = AnomalyTargetingOverride.Allow, expectedOverridePresent = true, expectedOverrideAttracts = true, expectedAttracts = true, expectedAttackable = true },
			new() { id = "nociosphere-never-everything", kindDef = "Nociosphere", faction = "entities", activityState = "active", attackMode = AttackMode.Everything, category = AnomalyTargetingCategory.Nociosphere, mode = AnomalyTargetingOverride.Never, expectedOverridePresent = true, expectedOverrideAttracts = false, expectedAttracts = false, expectedAttackable = false },
		};

		static readonly AnomalyZombieHostilityOverrideCase[] anomalyZombieHostilityOverrideCases =
		{
			new() { id = "entity-automatic-disabled", kindDef = "Metalhorror", faction = "entities", dormancyState = "awake", enemiesAttackZombies = false, animalsAttackZombies = false, mode = AnomalyTargetingOverride.Automatic, expectedHostile = false },
			new() { id = "entity-automatic-enabled", kindDef = "Metalhorror", faction = "entities", dormancyState = "awake", enemiesAttackZombies = true, animalsAttackZombies = true, mode = AnomalyTargetingOverride.Automatic, expectedHostile = true },
			new() { id = "entity-allow-disabled", kindDef = "Metalhorror", faction = "entities", dormancyState = "awake", enemiesAttackZombies = false, animalsAttackZombies = false, mode = AnomalyTargetingOverride.Allow, expectedOverridePresent = true, expectedOverrideAttacks = true, expectedHostile = true },
			new() { id = "entity-never-enabled", kindDef = "Gorehulk", faction = "entities", enemiesAttackZombies = true, animalsAttackZombies = true, mode = AnomalyTargetingOverride.Never, expectedOverridePresent = true, expectedOverrideAttacks = false, expectedHostile = false },
			new() { id = "shambler-allow-disabled", kindDef = "ShamblerSoldier", faction = "entities", dormancyState = "awake", enemiesAttackZombies = false, animalsAttackZombies = false, mode = AnomalyTargetingOverride.Allow, expectedOverridePresent = true, expectedOverrideAttacks = true, expectedHostile = true },
			new() { id = "nociosphere-allow-disabled", kindDef = "Nociosphere", faction = "entities", activityState = "active", enemiesAttackZombies = false, animalsAttackZombies = false, mode = AnomalyTargetingOverride.Allow, expectedOverridePresent = true, expectedOverrideAttacks = true, expectedHostile = true },
			new() { id = "dormant-allow-hardgate", kindDef = "Metalhorror", faction = "entities", dormancyState = "dormant", enemiesAttackZombies = true, animalsAttackZombies = true, mode = AnomalyTargetingOverride.Allow, expectedOverridePresent = true, expectedOverrideAttacks = false, expectedHostile = false },
		};

		[Tool("zombieland/anomaly_zombie_matrix", Description = "Stage Anomaly pawn-kind/state rows and report Zombieland zombie attraction, attack-mode, hostility, infection, and short tick outcomes.")]
		public static object AnomalyZombieMatrix(
			[ToolParameter(Description = "Comma-separated case ids or PawnKindDef names. Empty runs the built-in first Anomaly matrix.", Required = false, DefaultValue = "")] string cases = "",
			[ToolParameter(Description = "Ticks to step after staging all rows. Use 0 for a pure static matrix.", Required = false, DefaultValue = 60)] int ticks = 60,
			[ToolParameter(Description = "Destroy staged Anomaly pawns and zombies at the end.", Required = false, DefaultValue = true)] bool cleanup = true)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded." };

			var selectedCases = SelectAnomalyMatrixCases(cases);
			var missingKinds = selectedCases
				.Select(testCase => testCase.kindDef)
				.Distinct()
				.Where(kind => DefDatabase<PawnKindDef>.GetNamedSilentFail(kind) == null)
				.ToArray();
			var zombieFaction = Find.FactionManager.FirstFactionOfDef(ZombieDefOf.Zombies);
			if (missingKinds.Length > 0 || zombieFaction == null)
			{
				return new
				{
					success = false,
					missingKinds,
					zombieFactionPresent = zombieFaction != null,
					error = missingKinds.Length > 0
						? "One or more Anomaly PawnKindDefs were not loaded."
						: "Zombie faction was not loaded."
				};
			}

			var settings = ZombieSettings.Values;
			var originalAttackMode = settings.attackMode;
			var originalEnemyZombieResponse = settings.enemyZombieResponse;
			var originalAnimalsAttackZombies = settings.animalsAttackZombies;
			var spawned = new List<Thing>();
			var rows = new List<object>();
			var runtimeRows = new List<AnomalyMatrixRuntimeRow>();
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			ticks = Mathf.Clamp(ticks, 0, 600);

			try
			{
				ZombieRuntimeActions.DestroyZombies(map);
				for (var i = 0; i < selectedCases.Length; i++)
				{
					var testCase = selectedCases[i];
					var rowRoot = root + new IntVec3((i % 4) * 10 - 15, 0, (i / 4) * 7 - 18);
					if (rowRoot.InBounds(map) == false)
						rowRoot = root;
					if (TryFindClearSpawnCell(map, rowRoot, 18f, out var targetCell, out var targetCellError) == false)
						return targetCellError;
					if (TryFindClearSpawnCell(map, targetCell + new IntVec3(2, 0, 0), 8f, out var zombieCell, out var zombieCellError) == false)
						return zombieCellError;

					var kindDef = DefDatabase<PawnKindDef>.GetNamed(testCase.kindDef);
					var faction = ResolveAnomalyMatrixFaction(testCase.faction);
					var pawn = PawnGenerator.GeneratePawn(kindDef, faction);
					GenSpawn.Spawn(pawn, targetCell, map, Rot4.South);
					DisablePawnWork(pawn);
					ApplyAnomalyState(pawn, testCase.activityState, testCase.dormancyState);
					var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
					if (zombie == null)
						return new { success = false, testCase.id, error = "ZombieGenerator.SpawnZombie returned no normal zombie." };
					spawned.Add(pawn);
					spawned.Add(zombie);
					runtimeRows.Add(new AnomalyMatrixRuntimeRow { testCase = testCase, pawn = pawn, zombie = zombie });

					rows.Add(DescribeAnomalyMatrixRow(testCase, pawn, zombie, zombieFaction, settings));
				}

				if (ticks > 0)
					AdvanceGameTicks(ticks);

				var afterTickRows = runtimeRows
					.Select(row =>
					{
						var pawn = row.pawn;
						return pawn == null ? null : new
						{
							row.testCase.id,
							pawn = DescribePawn(pawn),
							zombie = DescribeZombie(row.zombie),
							activity = DescribeAnomalyActivity(pawn),
							dormancy = DescribeAnomalyDormancy(pawn),
							vanillaThreat = DescribeAnomalyVanillaThreat(pawn, row.zombie),
							infectionState = pawn.InfectionState().ToString(),
							attractsCurrentSettings = Customization.DoesAttractsZombies(pawn)
						};
					})
					.Where(row => row != null)
					.ToArray();

				return new
				{
					success = true,
					caseCount = selectedCases.Length,
					ticks,
					matrix = rows.ToArray(),
					afterTicks = afterTickRows,
					logNote = "Use rimbridge/list_logs minimumLevel=warning after this operation for the log-clean gate."
				};
			}
			finally
			{
				settings.attackMode = originalAttackMode;
				settings.enemyZombieResponse = originalEnemyZombieResponse;
				settings.animalsAttackZombies = originalAnimalsAttackZombies;
				if (cleanup)
				{
					for (var i = spawned.Count - 1; i >= 0; i--)
					{
						if (spawned[i] != null && spawned[i].Destroyed == false)
							CleanupAnomalyThing(spawned[i]);
					}
					ZombieRuntimeActions.DestroyZombies(map);
				}
			}
		}

		[Tool("zombieland/anomaly_sightstealer_ambush_contract", Description = "Verify sightstealer ambush job selection rejects doorless targets without logging the vanilla missing-door error while preserving door ambush targets.")]
		public static object AnomalySightstealerAmbushContract(
			[ToolParameter(Description = "Destroy staged sightstealer, target, and door fixtures at the end.", Required = false, DefaultValue = true)] bool cleanup = true)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded." };
			if (ModsConfig.AnomalyActive == false)
				return new { success = true, skipped = true, reason = "Anomaly is not active in the current mod list." };

			var sightstealerKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Sightstealer");
			if (sightstealerKind == null)
				return new { success = false, error = "PawnKindDef Sightstealer was not loaded." };

			var tryGiveJobMethod = typeof(JobGiver_AIWaitAmbush).GetMethod("TryGiveJob", BindingFlags.Instance | BindingFlags.NonPublic);
			var targetHelper = typeof(Patches)
				.GetNestedType("JobGiver_AIWaitAmbush_TryGiveJob_Patch", BindingFlags.NonPublic)
				?.GetMethod("TryFindDoorAmbushCellForTarget", BindingFlags.Static | BindingFlags.NonPublic);
			if (tryGiveJobMethod == null || targetHelper == null)
			{
				return new
				{
					success = false,
					tryGiveJobMethodFound = tryGiveJobMethod != null,
					targetHelperFound = targetHelper != null,
					error = "Could not reflect sightstealer ambush patch helpers."
				};
			}

			var spawned = new List<Thing>();
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			try
			{
				ZombieRuntimeActions.DestroyZombies(map);
				var patchOwners = PatchOwners(typeof(JobGiver_AIWaitAmbush), "TryGiveJob");
				var doorless = VerifySightstealerAmbushFixture(map, root + new IntVec3(-12, 0, 0), sightstealerKind, targetHelper, tryGiveJobMethod, "doorless", spawned);
				var doorTarget = VerifySightstealerAmbushFixture(map, root, sightstealerKind, targetHelper, tryGiveJobMethod, "targetOnDoor", spawned);
				var behindDoor = VerifySightstealerAmbushFixture(map, root + new IntVec3(12, 0, 0), sightstealerKind, targetHelper, tryGiveJobMethod, "behindClosedDoor", spawned);

				return new
				{
					success = patchOwners.Contains("net.pardeike.zombieland")
						&& ObjectSuccess(doorless)
						&& ObjectSuccess(doorTarget)
						&& ObjectSuccess(behindDoor),
					patchOwners,
					doorless,
					doorTarget,
					behindDoor
				};
			}
			finally
			{
				if (cleanup)
				{
					for (var i = spawned.Count - 1; i >= 0; i--)
					{
						if (spawned[i] != null && spawned[i].Destroyed == false)
							CleanupAnomalyThing(spawned[i]);
					}
					ZombieRuntimeActions.DestroyZombies(map);
				}
			}
		}

		static object VerifySightstealerAmbushFixture(
			Map map,
			IntVec3 root,
			PawnKindDef sightstealerKind,
			MethodInfo targetHelper,
			MethodInfo tryGiveJobMethod,
			string fixtureKind,
			List<Thing> spawned)
		{
			Building_Door door = null;
			IntVec3 sightstealerCell;
			IntVec3 targetCell;
			if (fixtureKind == "behindClosedDoor")
			{
				if (TryBuildSightstealerDoorRoomFixture(map, root, spawned, out sightstealerCell, out targetCell, out door, out var roomError) == false)
					return roomError;
			}
			else
			{
				if (TryFindClearSpawnCell(map, root, 18f, out sightstealerCell, out var sightstealerCellError) == false)
					return sightstealerCellError;
				targetCell = GenRadial.RadialCellsAround(sightstealerCell, 8f, false)
					.Where(cell => cell.InBounds(map))
					.Where(cell => cell.Standable(map))
					.Where(cell => cell.Fogged(map) == false)
					.Where(cell => cell.GetFirstPawn(map) == null)
					.Where(cell => cell.GetEdifice(map) == null)
					.Where(cell => cell.DistanceTo(sightstealerCell) >= 3f)
					.Where(cell => GenSight.LineOfSight(sightstealerCell, cell, map, true))
					.OrderBy(cell => cell.DistanceToSquared(sightstealerCell))
					.FirstOrDefault();
				if (targetCell.IsValid == false)
				{
					return new
					{
						success = false,
						fixtureKind,
						sightstealerCell = ZombieRuntimeActions.DescribeCell(sightstealerCell),
						error = "No reachable sightstealer ambush target cell was found."
					};
				}
				if (fixtureKind == "targetOnDoor")
				{
					door = ThingMaker.MakeThing(ThingDefOf.Door, ThingDefOf.WoodLog) as Building_Door;
					if (door == null)
						return new { success = false, fixtureKind, error = "Could not create a wooden door fixture." };
					GenSpawn.Spawn(door, targetCell, map, WipeMode.Vanish);
					door.SetFaction(Faction.OfPlayer);
					spawned.Add(door);
					map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
				}
			}

			var sightstealer = PawnGenerator.GeneratePawn(sightstealerKind, ResolveAnomalyMatrixFaction("entities"));
			var target = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(sightstealer, sightstealerCell, map, Rot4.South);
			GenSpawn.Spawn(target, targetCell, map, Rot4.South);
			DisablePawnWork(target);
			spawned.Add(sightstealer);
			spawned.Add(target);

			var targetDoor = target.Position.GetDoor(map);
			var doorCanPhysicallyPass = door == null ? (bool?)null : door.CanPhysicallyPass(sightstealer);
			var doorBlocksPawn = door == null ? (bool?)null : door.BlocksPawn(sightstealer);
			var doorPawnCanOpen = door == null ? (bool?)null : door.PawnCanOpen(sightstealer);
			var doorFreePassage = door == null ? (bool?)null : door.FreePassage;
			if (fixtureKind == "behindClosedDoor" && doorCanPhysicallyPass != false)
			{
				return new
				{
					success = false,
					fixtureKind,
					fixtureSetupFailed = true,
					error = "Behind-closed-door sightstealer fixture produced a door that is not physically blocking this sightstealer.",
					sightstealer = DescribePawn(sightstealer),
					target = DescribePawn(target),
					sightstealerCell = ZombieRuntimeActions.DescribeCell(sightstealerCell),
					targetCell = ZombieRuntimeActions.DescribeCell(targetCell),
					targetDoor = targetDoor?.def?.defName,
					doorCell = door == null ? null : ZombieRuntimeActions.DescribeCell(door.Position),
					doorOpen = door?.Open,
					doorCanPhysicallyPass,
					doorBlocksPawn,
					doorPawnCanOpen,
					doorFreePassage
				};
			}

			map.attackTargetsCache.UpdateTarget(sightstealer);
			map.attackTargetsCache.UpdateTarget(target);

			var path = map.pathFinder.FindPathNow(sightstealer.Position, target.Position, TraverseParms.For(sightstealer, Danger.Deadly, TraverseMode.PassDoors));
			bool pathFound;
			bool pathHasBlockingDoor;
			var pathNodeCount = 0;
			var pathFirstNodes = Array.Empty<object>();
			var pathDoorCells = Array.Empty<object>();
			var pathBlockingDoorCells = Array.Empty<object>();
			try
			{
				pathFound = path?.Found == true;
				if (pathFound)
				{
					var nodes = path.NodesReversed;
					pathNodeCount = nodes.Count;
					pathFirstNodes = nodes.Take(12).Select(ZombieRuntimeActions.DescribeCell).ToArray();
					pathDoorCells = nodes
						.Where(cell => cell.GetDoor(map) != null)
						.Select(ZombieRuntimeActions.DescribeCell)
						.ToArray();
					pathBlockingDoorCells = nodes
						.Where(cell => cell.GetEdifice(map) is Building_Door pathDoor && pathDoor.CanPhysicallyPass(sightstealer) == false)
						.Select(ZombieRuntimeActions.DescribeCell)
						.ToArray();
				}
				pathHasBlockingDoor = pathFound && path.TryFindLastCellBeforeBlockingDoor(sightstealer, out _);
			}
			finally
			{
				path?.ReleaseToPool();
			}

			var args = new object[] { sightstealer, target, IntVec3.Invalid };
			var targetAccepted = false;
			string targetHelperError = null;
			try
			{
				targetAccepted = (bool)targetHelper.Invoke(null, args);
			}
			catch (TargetInvocationException ex)
			{
				targetHelperError = ex.InnerException?.Message ?? ex.Message;
			}
			var ambushCell = (IntVec3)args[2];
			Job job = null;
			string invocationError = null;
			var missingDoorLogsBefore = CountSightstealerMissingDoorLogs();
			try
			{
				job = tryGiveJobMethod.Invoke(new JobGiver_AIWaitAmbush(), new object[] { sightstealer }) as Job;
			}
			catch (TargetInvocationException ex)
			{
				invocationError = ex.InnerException?.Message ?? ex.Message;
			}
			var missingDoorLogsAfter = CountSightstealerMissingDoorLogs();

			var expectedBlockingDoor = fixtureKind == "behindClosedDoor";
			var expectedTargetOnDoor = fixtureKind == "targetOnDoor";
			var expectedTargetAccepted = expectedTargetOnDoor || expectedBlockingDoor;
			var expectedJob = expectedTargetAccepted;
			var fullJobDecision = expectedTargetAccepted
				? "Full TryGiveJob must return an ambush wait/goto job for this fixture."
				: "Full TryGiveJob is not asserted null for the doorless row because it scans every live attack target on the map; target-specific helper rejection is the deterministic assertion.";
			var fullJobMatchesExpectation = expectedJob == false || job != null;
			return new
			{
				success = pathFound
					&& pathHasBlockingDoor == expectedBlockingDoor
					&& (targetDoor != null) == expectedTargetOnDoor
					&& targetAccepted == expectedTargetAccepted
					&& targetHelperError == null
					&& fullJobMatchesExpectation
					&& invocationError == null
					&& missingDoorLogsAfter == missingDoorLogsBefore,
				fixtureKind,
				sightstealer = DescribePawn(sightstealer),
				target = DescribePawn(target),
				sightstealerCell = ZombieRuntimeActions.DescribeCell(sightstealerCell),
				targetCell = ZombieRuntimeActions.DescribeCell(targetCell),
				pathFound,
				pathHasBlockingDoor,
				pathNodeCount,
				pathFirstNodes,
				pathDoorCells,
				pathBlockingDoorCells,
				expectedBlockingDoor,
				targetDoor = targetDoor?.def?.defName,
				doorCell = door == null ? null : ZombieRuntimeActions.DescribeCell(door.Position),
				doorOpen = door?.Open,
				doorCanPhysicallyPass,
				doorBlocksPawn,
				doorPawnCanOpen,
				doorFreePassage,
				targetAccepted,
				expectedTargetAccepted,
				ambushCell = ambushCell.IsValid ? ZombieRuntimeActions.DescribeCell(ambushCell) : null,
				fullJobDecision,
				fullJobMatchesExpectation,
				job = job?.def?.defName,
				jobTarget = job?.targetA.Cell.IsValid == true ? ZombieRuntimeActions.DescribeCell(job.targetA.Cell) : null,
				targetHelperError,
				invocationError,
				missingDoorLogsBefore,
				missingDoorLogsAfter
			};
		}

		static bool TryBuildSightstealerDoorRoomFixture(Map map, IntVec3 root, List<Thing> spawned, out IntVec3 sightstealerCell, out IntVec3 targetCell, out Building_Door door, out object error)
		{
			sightstealerCell = IntVec3.Invalid;
			targetCell = IntVec3.Invalid;
			door = null;
			error = null;

			foreach (var candidate in GenRadial.RadialCellsAround(root, 24f, true))
			{
				if (candidate.InBounds(map) == false || candidate.Fogged(map))
					continue;

				var corridorCells = new[]
				{
					candidate + new IntVec3(-2, 0, 0),
					candidate + new IntVec3(-1, 0, 0),
					candidate,
					candidate + new IntVec3(1, 0, 0),
					candidate + new IntVec3(2, 0, 0)
				};
				var blockerCells = Enumerable.Range(-2, 5)
					.SelectMany(dx => new[]
					{
						candidate + new IntVec3(dx, 0, -1),
						candidate + new IntVec3(dx, 0, 1)
					})
					.ToArray();
				var fixtureCells = corridorCells.Concat(blockerCells).ToArray();
				var candidateSightstealerCell = candidate + new IntVec3(-2, 0, 0);
				var candidateTargetCell = candidate + new IntVec3(2, 0, 0);
				if (fixtureCells.Any(cell => cell.InBounds(map) == false || cell.Fogged(map)))
					continue;
				if (corridorCells.Any(cell => cell.Standable(map) == false))
					continue;
				if (fixtureCells.Any(cell => cell.GetEdifice(map) != null || cell.GetFirstPawn(map) != null))
					continue;

				var wallDef = ThingDefOf.Wall;
				var stuffDef = ThingDefOf.WoodLog;
				var created = new List<Thing>();
				var fixtureFailed = false;
				foreach (var cell in blockerCells)
				{
					var wall = ThingMaker.MakeThing(wallDef, stuffDef) as Building;
					if (wall == null)
					{
						fixtureFailed = true;
						break;
					}
					GenSpawn.Spawn(wall, cell, map, WipeMode.Vanish);
					wall.SetFaction(Faction.OfPlayer);
					created.Add(wall);
				}
				if (fixtureFailed)
				{
					for (var i = created.Count - 1; i >= 0; i--)
						CleanupAnomalyThing(created[i]);
					continue;
				}

				door = ThingMaker.MakeThing(ThingDefOf.Door, stuffDef) as Building_Door;
				if (door == null)
				{
					for (var i = created.Count - 1; i >= 0; i--)
						CleanupAnomalyThing(created[i]);
					error = new { success = false, error = "Could not create a wooden door fixture." };
					return false;
				}
				GenSpawn.Spawn(door, candidate, map, WipeMode.Vanish);
				door.SetFaction(Faction.OfPlayer);
				created.Add(door);
				map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();

				var missingWallCells = blockerCells
					.Where(cell => cell.GetEdifice(map)?.def?.passability != Traversability.Impassable)
					.Select(ZombieRuntimeActions.DescribeCell)
					.ToArray();
				if (missingWallCells.Length > 0 || candidate.GetDoor(map) != door)
				{
					for (var i = created.Count - 1; i >= 0; i--)
						CleanupAnomalyThing(created[i]);
					map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
					continue;
				}

				spawned.AddRange(created);
				sightstealerCell = candidateSightstealerCell;
				targetCell = candidateTargetCell;
				return true;
			}

			error = new
			{
				success = false,
				root = ZombieRuntimeActions.DescribeCell(root),
				error = "No clear closed-door room fixture area was found for the sightstealer ambush contract."
			};
			return false;
		}

		static int CountSightstealerMissingDoorLogs()
		{
			const string needle = "did TryFindLastCellBeforeDoor";
			return Log.Messages.Count(message =>
				(message.type == LogMessageType.Warning || message.type == LogMessageType.Error)
				&& (message.text?.IndexOf(needle, StringComparison.OrdinalIgnoreCase) ?? -1) >= 0);
		}

		[Tool("zombieland/anomaly_targeting_overrides", Description = "Verify Anomaly-specific zombie targeting overrides preserve Automatic base behavior and apply Never/Allow only to active valid Anomaly target categories.")]
		public static object AnomalyTargetingOverrides(
			[ToolParameter(Description = "Destroy staged Anomaly pawns and zombies at the end.", Required = false, DefaultValue = true)] bool cleanup = true)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded." };
			if (ModsConfig.AnomalyActive == false)
				return new { success = true, skipped = true, reason = "Anomaly is not active in the current mod list." };

			var missingKinds = anomalyTargetingOverrideCases
				.Select(testCase => testCase.kindDef)
				.Distinct()
				.Where(kind => DefDatabase<PawnKindDef>.GetNamedSilentFail(kind) == null)
				.ToArray();
			if (missingKinds.Length > 0)
				return new { success = false, missingKinds, error = "One or more required Anomaly PawnKindDefs were not loaded." };

			var settingsSnapshot = SnapshotZombieSettings();
			var spawned = new List<Thing>();
			var rows = new List<object>();
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);

			try
			{
				ZombieRuntimeActions.DestroyZombies(map);
				for (var i = 0; i < anomalyTargetingOverrideCases.Length; i++)
				{
					var testCase = anomalyTargetingOverrideCases[i];
					var rowRoot = root + new IntVec3((i % 4) * 10 - 15, 0, (i / 4) * 8 - 16);
					if (rowRoot.InBounds(map) == false)
						rowRoot = root;
					if (TryFindClearSpawnCell(map, rowRoot, 20f, out var targetCell, out var targetCellError) == false)
						return targetCellError;
					if (TryFindClearSpawnCell(map, targetCell + new IntVec3(2, 0, 0), 8f, out var zombieCell, out var zombieCellError) == false)
						return zombieCellError;

					if (TrySpawnAnomalyPawn(map, testCase.kindDef, testCase.faction, targetCell, spawned, out var pawn, out var pawnError, testCase.activityState, testCase.dormancyState, testCase.kindDef != "Nociosphere") == false)
						return pawnError;
					var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
					if (zombie == null)
						return new { success = false, testCase.id, error = "ZombieGenerator.SpawnZombie returned no normal zombie." };
					spawned.Add(zombie);

					ApplyZombieSettingsOverride(settings => ConfigureAnomalyTargetingOverride(settings, testCase.attackMode, testCase.category, testCase.mode));

					var categoryPresent = AnomalyTargeting.TryGetCategory(pawn, out var category);
					var overridePresent = AnomalyTargeting.TryGetAttractionOverride(pawn, out var overrideAttracts);
					var attracts = Customization.DoesAttractsZombies(pawn);
					var attackable = Tools.Attackable(zombie, testCase.attackMode, pawn);
					var rowSuccess = categoryPresent
						&& category == testCase.category
						&& overridePresent == testCase.expectedOverridePresent
						&& (overridePresent == false || overrideAttracts == testCase.expectedOverrideAttracts)
						&& attracts == testCase.expectedAttracts
						&& attackable == testCase.expectedAttackable;

					rows.Add(new
					{
						testCase.id,
						testCase.kindDef,
						testCase.faction,
						mode = testCase.attackMode.ToString(),
						overrideMode = testCase.mode.ToString(),
						expectedCategory = testCase.category.ToString(),
						categoryPresent,
						category = category.ToString(),
						overridePresent,
						overrideAttracts,
						attracts,
						attackable,
						expected = new
						{
							testCase.expectedOverridePresent,
							testCase.expectedOverrideAttracts,
							testCase.expectedAttracts,
							testCase.expectedAttackable
						},
						activity = DescribeAnomalyActivity(pawn),
						dormancy = DescribeAnomalyDormancy(pawn),
						pawn = DescribePawn(pawn),
						zombie = DescribeZombie(zombie),
						success = rowSuccess
					});
				}

				var rowArray = rows.ToArray();
				return new
				{
					success = rowArray.All(row => (bool)row.GetType().GetProperty("success").GetValue(row)),
					caseCount = rowArray.Length,
					rows = rowArray,
					logNote = "Use rimbridge/list_logs minimumLevel=warning after this operation for the log-clean gate."
				};
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
				if (cleanup)
				{
					for (var i = spawned.Count - 1; i >= 0; i--)
						if (spawned[i] != null && spawned[i].Destroyed == false)
							CleanupAnomalyThing(spawned[i]);
					ZombieRuntimeActions.DestroyZombies(map);
				}
			}
		}

		[Tool("zombieland/anomaly_zombie_hostility_overrides", Description = "Verify Anomaly-specific overrides for non-player Anomaly pawns choosing zombies as targets.")]
		public static object AnomalyZombieHostilityOverrides(
			[ToolParameter(Description = "Destroy staged Anomaly pawns and zombies at the end.", Required = false, DefaultValue = true)] bool cleanup = true)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded." };
			if (ModsConfig.AnomalyActive == false)
				return new { success = true, skipped = true, reason = "Anomaly is not active in the current mod list." };

			var missingKinds = anomalyZombieHostilityOverrideCases
				.Select(testCase => testCase.kindDef)
				.Distinct()
				.Where(kind => DefDatabase<PawnKindDef>.GetNamedSilentFail(kind) == null)
				.ToArray();
			var zombieFaction = Find.FactionManager.FirstFactionOfDef(ZombieDefOf.Zombies);
			if (missingKinds.Length > 0 || zombieFaction == null)
			{
				return new
				{
					success = false,
					missingKinds,
					zombieFactionPresent = zombieFaction != null,
					error = missingKinds.Length > 0
						? "One or more required Anomaly PawnKindDefs were not loaded."
						: "Zombie faction was not loaded."
				};
			}

			var settingsSnapshot = SnapshotZombieSettings();
			var spawned = new List<Thing>();
			var rows = new List<object>();
			var successes = new List<bool>();
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);

			try
			{
				ZombieRuntimeActions.DestroyZombies(map);
				for (var i = 0; i < anomalyZombieHostilityOverrideCases.Length; i++)
				{
					var testCase = anomalyZombieHostilityOverrideCases[i];
					var rowRoot = root + new IntVec3((i % 4) * 10 - 15, 0, (i / 4) * 8 - 16);
					if (rowRoot.InBounds(map) == false)
						rowRoot = root;
					if (TryFindClearSpawnCell(map, rowRoot, 20f, out var pawnCell, out var pawnCellError) == false)
						return pawnCellError;
					if (TryFindClearSpawnCell(map, pawnCell + new IntVec3(2, 0, 0), 8f, out var zombieCell, out var zombieCellError) == false)
						return zombieCellError;

					if (TrySpawnAnomalyPawn(map, testCase.kindDef, testCase.faction, pawnCell, spawned, out var pawn, out var pawnError, testCase.activityState, testCase.dormancyState, testCase.kindDef != "Nociosphere") == false)
						return pawnError;
					var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
					if (zombie == null)
						return new { success = false, testCase.id, error = "ZombieRuntimeActions.SpawnZombie returned no normal zombie." };
					spawned.Add(zombie);

					ApplyZombieSettingsOverride(settings =>
					{
						settings.enemyZombieResponse = testCase.enemiesAttackZombies ? ZombieResponsePolicy.Full : ZombieResponsePolicy.Minimal;
						settings.animalsAttackZombies = testCase.animalsAttackZombies;
						settings.anomalyAttacksZombies = testCase.mode;
					});

					var categoryPresent = AnomalyTargeting.TryGetCategory(pawn, out var category);
					var overridePresent = AnomalyTargeting.TryGetZombieHostilityOverride(pawn, out var overrideAttacks);
					var isHostileToZombies = Tools.IsHostileToZombies(pawn);
					var thingHostility = TryHostileTo(pawn, zombie);
					var factionHostility = TryHostileTo(pawn, zombieFaction);
					var rowSuccess = categoryPresent
						&& overridePresent == testCase.expectedOverridePresent
						&& (overridePresent == false || overrideAttacks == testCase.expectedOverrideAttacks)
						&& isHostileToZombies == testCase.expectedHostile
						&& thingHostility.error == null
						&& factionHostility.error == null
						&& thingHostility.value == testCase.expectedHostile
						&& factionHostility.value == testCase.expectedHostile;
					successes.Add(rowSuccess);

					rows.Add(new
					{
						testCase.id,
						testCase.kindDef,
						testCase.faction,
						testCase.enemiesAttackZombies,
						testCase.animalsAttackZombies,
						overrideMode = testCase.mode.ToString(),
						categoryPresent,
						category = category.ToString(),
						overridePresent,
						overrideAttacks,
						isHostileToZombies,
						toZombieThing = DescribeHostility(thingHostility),
						toZombieFaction = DescribeHostility(factionHostility),
						expected = new
						{
							testCase.expectedOverridePresent,
							testCase.expectedOverrideAttacks,
							testCase.expectedHostile
						},
						activity = DescribeAnomalyActivity(pawn),
						dormancy = DescribeAnomalyDormancy(pawn),
						pawn = DescribePawn(pawn),
						zombie = DescribeZombie(zombie),
						success = rowSuccess
					});
				}

				return new
				{
					success = successes.All(success => success),
					caseCount = rows.Count,
					rows = rows.ToArray(),
					logNote = "Use rimbridge/list_logs minimumLevel=warning after this operation for the log-clean gate."
				};
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
				if (cleanup)
				{
					for (var i = spawned.Count - 1; i >= 0; i--)
						if (spawned[i] != null && spawned[i].Destroyed == false)
							CleanupAnomalyThing(spawned[i]);
					ZombieRuntimeActions.DestroyZombies(map);
				}
			}
		}

		[Tool("zombieland/death_pall_zombie_corpse_contract", Description = "Verify Death Pall raises a Zombieland zombie corpse as a stronger fresh Zombieland zombie without invoking vanilla shambler resurrection.")]
		public static object DeathPallZombieCorpseContract(
			[ToolParameter(Description = "Destroy the staged corpse and raised zombie at the end.", Required = false, DefaultValue = true)] bool cleanup = true)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded." };
			if (ModsConfig.AnomalyActive == false)
				return new { success = true, skipped = true, reason = "Anomaly is not active in the current mod list." };

			var tickManager = map.GetComponent<TickManager>();
			if (tickManager == null)
				return new { success = false, error = "No Zombieland TickManager is attached to the current map." };

			Zombie source = null;
			Zombie raised = null;
			ZombieCorpse corpse = null;
			try
			{
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 24f, out var cell, out var cellError) == false)
					return cellError;

				source = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.Electrifier, true);
				if (source == null)
					return new { success = false, error = "ZombieRuntimeActions.SpawnZombie returned no source zombie." };

				source.Name = new NameTriple("Death", "Pall", "Probe");
				source.wasMapPawnBefore = false;
				var expectedApparelDef = EquipDeathPallProbeApparel(source);
				if (expectedApparelDef == null)
					return new { success = false, source = DescribeZombie(source), error = "Could not equip source zombie with probe head apparel." };
				var expectedGender = source.gender;
				var expectedBodyType = source.story?.bodyType;
				var expectedHeadType = source.story?.headType;

				source.Kill(null);
				corpse = source.Corpse as ZombieCorpse;
				if (corpse == null || corpse.Destroyed)
					return new { success = false, source = DescribeZombie(source), error = "Killing the source zombie did not create a live ZombieCorpse." };

				var corpseBefore = DescribeCorpse(corpse);
				var canRaiseDefault = MutantUtility.CanResurrectAsShambler(corpse);
				var canRaiseIgnoringIndoors = MutantUtility.CanResurrectAsShambler(corpse, true);
				var liveZombieIdsBeforeRaise = map.mapPawns.AllPawnsSpawned
					.OfType<Zombie>()
					.Select(zombie => zombie.ThingID)
					.ToHashSet();

				MutantUtility.ResurrectAsShambler(corpse.InnerPawn, 60000);

				raised = map.mapPawns.AllPawnsSpawned
					.OfType<Zombie>()
					.FirstOrDefault(zombie => liveZombieIdsBeforeRaise.Contains(zombie.ThingID) == false);

				var raisedApparelDefs = raised?.apparel?.WornApparel
					.Select(apparel => apparel.def.defName)
					.ToArray() ?? Array.Empty<string>();
				var apparelMatches = raisedApparelDefs.Contains(expectedApparelDef.defName);
				var appearanceMatches = raised != null
					&& raised.gender == expectedGender
					&& raised.story?.bodyType == expectedBodyType
					&& raised.story?.headType == expectedHeadType
					&& raised.isElectrifier
					&& raised.wasMapPawnBefore;
				var success = canRaiseIgnoringIndoors
					&& raised != null
					&& raised.Dead == false
					&& raised.IsMutant == false
					&& raised.Position == cell
					&& tickManager.allZombiesCached.Contains(raised)
					&& (corpse.Destroyed || corpse.Spawned == false)
					&& appearanceMatches
					&& apparelMatches;

				return new
				{
					success,
					canRaiseDefault,
					canRaiseIgnoringIndoors,
					corpseBefore,
					corpseAfterDestroyed = corpse.Destroyed,
					raised = DescribeZombie(raised),
					appearanceMatches,
					apparelMatches,
					expected = new
					{
						gender = expectedGender.ToString(),
						bodyType = expectedBodyType?.defName,
						headType = expectedHeadType?.defName,
						isElectrifier = true,
						wasMapPawnBefore = true,
						apparelDef = expectedApparelDef.defName
					},
					raisedAppearance = new
					{
						gender = raised?.gender.ToString(),
						bodyType = raised?.story?.bodyType?.defName,
						headType = raised?.story?.headType?.defName,
						isElectrifier = raised?.isElectrifier,
						wasMapPawnBefore = raised?.wasMapPawnBefore,
						isMutant = raised?.IsMutant,
						apparelDefs = raisedApparelDefs
					},
					logNote = "Use rimbridge/list_logs minimumLevel=warning after this operation for the log-clean gate."
				};
			}
			finally
			{
				if (cleanup)
				{
					if (raised != null && raised.Destroyed == false)
						raised.Destroy();
					if (corpse != null && corpse.Destroyed == false)
						corpse.Destroy();
					if (source != null && source.Spawned && source.Destroyed == false)
						source.Destroy();
				}
			}
		}

		static ThingDef EquipDeathPallProbeApparel(Pawn pawn)
		{
			if (pawn?.apparel == null)
				return null;

			var apparelDef = DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_SimpleHelmet")
				?? DefDatabase<ThingDef>.GetNamedSilentFail("Apparel_CowboyHat")
				?? DefDatabase<ThingDef>.AllDefsListForReading
					.Where(def => def.IsApparel && def.apparel?.wornGraphicPath.NullOrEmpty() == false)
					.FirstOrDefault(def => PawnApparelGenerator.IsHeadgear(def));
			if (apparelDef == null)
				return null;

			pawn.apparel.DestroyAll();
			var apparel = ThingMaker.MakeThing(apparelDef, GenStuff.DefaultStuffFor(apparelDef)) as Apparel;
			if (apparel == null)
				return null;

			pawn.apparel.Wear(apparel, false);
			return pawn.apparel.WornApparel.Contains(apparel) ? apparelDef : null;
		}

		[Tool("zombieland/anomaly_policy_edges", Description = "Verify narrow Anomaly zombie policy edges that do not belong in the broad pawn matrix: active Nociosphere and FleshmassHeart.")]
		public static object AnomalyPolicyEdges(
			[ToolParameter(Description = "Optional short tick window after staging. Default 0 keeps the probe static and avoids Nociosphere onslaught AI.", Required = false, DefaultValue = 0)] int ticks = 0,
			[ToolParameter(Description = "Destroy staged Anomaly things and zombies at the end.", Required = false, DefaultValue = true)] bool cleanup = true)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded." };

			var nociosphereKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Nociosphere");
			var heartDef = DefDatabase<ThingDef>.GetNamedSilentFail("FleshmassHeart");
			var zombieFaction = Find.FactionManager.FirstFactionOfDef(ZombieDefOf.Zombies);
			var entitiesFaction = ResolveAnomalyMatrixFaction("entities");
			if (nociosphereKind == null || heartDef == null || zombieFaction == null)
			{
				return new
				{
					success = false,
					nociosphereKindPresent = nociosphereKind != null,
					fleshmassHeartPresent = heartDef != null,
					zombieFactionPresent = zombieFaction != null,
					error = "One or more required Anomaly/Zombieland defs were not loaded."
				};
			}

			var settings = ZombieSettings.Values;
			var spawned = new List<Thing>();
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			var clampedTicks = Mathf.Clamp(ticks, 0, 30);

			try
			{
				ZombieRuntimeActions.DestroyZombies(map);

				if (TryFindClearSpawnCell(map, root + new IntVec3(-12, 0, -8), 18f, out var nociosphereCell, out var nociosphereCellError) == false)
					return nociosphereCellError;
				if (TryFindClearSpawnCell(map, nociosphereCell + new IntVec3(2, 0, 0), 8f, out var nociosphereZombieCell, out var nociosphereZombieCellError) == false)
					return nociosphereZombieCellError;
				if (TryFindClearSpawnCell(map, root + new IntVec3(12, 0, -8), 18f, out var heartCell, out var heartCellError) == false)
					return heartCellError;
				if (TryFindClearSpawnCell(map, heartCell + new IntVec3(2, 0, 0), 8f, out var heartZombieCell, out var heartZombieCellError) == false)
					return heartZombieCellError;

				var nociosphere = PawnGenerator.GeneratePawn(nociosphereKind, entitiesFaction);
				GenSpawn.Spawn(nociosphere, nociosphereCell, map, Rot4.South);
				DisablePawnWork(nociosphere);
				var nociosphereZombie = ZombieRuntimeActions.SpawnZombie(nociosphereZombieCell, map, ZombieType.Normal, true);
				if (nociosphereZombie == null)
					return new { success = false, error = "Could not spawn Nociosphere policy zombie." };
				spawned.Add(nociosphere);
				spawned.Add(nociosphereZombie);
				ApplyAnomalyState(nociosphere, "active", null);

				var heartThing = ThingMaker.MakeThing(heartDef);
				if (heartThing is not Building_FleshmassHeart heart)
					return new { success = false, defName = heartThing?.def?.defName, type = heartThing?.GetType().FullName, error = "FleshmassHeart did not create the expected building type." };
				heart.SetFaction(entitiesFaction);
				GenSpawn.Spawn(heart, heartCell, map, Rot4.South);
				var heartZombie = ZombieRuntimeActions.SpawnZombie(heartZombieCell, map, ZombieType.Normal, true);
				if (heartZombie == null)
					return new { success = false, error = "Could not spawn FleshmassHeart policy zombie." };
				spawned.Add(heart);
				spawned.Add(heartZombie);

				var beforeTicks = new
				{
					nociosphere = DescribeNociospherePolicy(nociosphere, nociosphereZombie, zombieFaction, settings),
					fleshmassHeart = DescribeThingPolicy("FleshmassHeart", heart, heartZombie, zombieFaction, settings)
				};

				if (clampedTicks > 0)
				{
					AdvanceGameTicks(clampedTicks);
					settings = ZombieSettings.Values;
				}

				var afterTicks = new
				{
					nociosphere = DescribeNociospherePolicy(nociosphere, nociosphereZombie, zombieFaction, settings),
					fleshmassHeart = DescribeThingPolicy("FleshmassHeart", heart, heartZombie, zombieFaction, settings)
				};

				return new
				{
					success = true,
					ticks = clampedTicks,
					beforeTicks,
					afterTicks,
					logNote = "Use rimbridge/list_logs minimumLevel=warning after this operation for the log-clean gate."
				};
			}
			finally
			{
				if (cleanup)
				{
					for (var i = spawned.Count - 1; i >= 0; i--)
					{
						if (spawned[i] != null && spawned[i].Destroyed == false)
							CleanupAnomalyThing(spawned[i]);
					}
					ZombieRuntimeActions.DestroyZombies(map);
				}
			}
		}

		[Tool("zombieland/anomaly_engagement_matrix", Description = "Stage representative Anomaly pawn rows under each zombie attack mode and verify real pheromone/tracking engagement signals.")]
		public static object AnomalyEngagementMatrix(
			[ToolParameter(Description = "Comma-separated case ids or PawnKindDef names. Empty runs a small representative engagement set.", Required = false, DefaultValue = "")] string cases = "",
			[ToolParameter(Description = "Ticks to step after each row. Clamped to 1..180.", Required = false, DefaultValue = 90)] int ticks = 90,
			[ToolParameter(Description = "Whether enemy/entity pawns should be allowed to attack zombies during reverse-hostility probes.", Required = false, DefaultValue = true)] bool enemiesAttackZombies = true,
			[ToolParameter(Description = "Destroy staged Anomaly pawns and zombies at the end.", Required = false, DefaultValue = true)] bool cleanup = true,
			[ToolParameter(Description = "Include full before/after row details. Default false keeps the runtime evidence compact.", Required = false, DefaultValue = false)] bool includeDetails = false)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded." };

			var selectedCases = SelectAnomalyEngagementCases(cases);
			var missingKinds = selectedCases
				.Select(testCase => testCase.kindDef)
				.Distinct()
				.Where(kind => DefDatabase<PawnKindDef>.GetNamedSilentFail(kind) == null)
				.ToArray();
			var zombieFaction = Find.FactionManager.FirstFactionOfDef(ZombieDefOf.Zombies);
			if (missingKinds.Length > 0 || zombieFaction == null)
			{
				return new
				{
					success = false,
					missingKinds,
					zombieFactionPresent = zombieFaction != null,
					error = missingKinds.Length > 0
						? "One or more Anomaly PawnKindDefs were not loaded."
						: "Zombie faction was not loaded."
				};
			}

			var clampedTicks = Mathf.Clamp(ticks, 1, 180);
			var spawned = new List<Thing>();
			var rows = new List<object>();
			var summaries = new List<object>();
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			var settingsSnapshot = SnapshotZombieSettings();

			try
			{
				ZombieRuntimeActions.DestroyZombies(map);
				var modes = new[] { AttackMode.OnlyColonists, AttackMode.OnlyHumans, AttackMode.Everything };
				for (var caseIndex = 0; caseIndex < selectedCases.Length; caseIndex++)
				{
					var testCase = selectedCases[caseIndex];
					for (var modeIndex = 0; modeIndex < modes.Length; modeIndex++)
					{
						var mode = modes[modeIndex];
						var rowRoot = root + new IntVec3((caseIndex % 3) * 14 - 14, 0, (caseIndex / 3) * 18 + modeIndex * 5 - 22);
						if (rowRoot.InBounds(map) == false)
							rowRoot = root;
						if (TryFindClearSpawnCell(map, rowRoot, 22f, out var targetCell, out var targetCellError) == false)
							return targetCellError;
						if (TryFindClearSpawnCell(map, targetCell + new IntVec3(-4, 0, 0), 12f, out var zombieCell, out var zombieCellError) == false)
							return zombieCellError;

						ApplyZombieSettingsOverride(values =>
						{
							values.attackMode = mode;
							values.enemyZombieResponse = enemiesAttackZombies ? ZombieResponsePolicy.Full : ZombieResponsePolicy.Minimal;
							values.animalsAttackZombies = enemiesAttackZombies;
						});

						var kindDef = DefDatabase<PawnKindDef>.GetNamed(testCase.kindDef);
						var faction = ResolveAnomalyMatrixFaction(testCase.faction);
						var pawn = PawnGenerator.GeneratePawn(kindDef, faction);
						GenSpawn.Spawn(pawn, targetCell, map, Rot4.South);
						DisablePawnWork(pawn);
						ApplyAnomalyState(pawn, testCase.activityState, testCase.dormancyState);
						var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
						if (zombie == null)
							return new { success = false, testCase.id, mode = mode.ToString(), error = "ZombieGenerator.SpawnZombie returned no normal zombie." };
						spawned.Add(pawn);
						spawned.Add(zombie);
						PrepareWallPushZombie(map, zombie, zombieCell);

						ClearAnomalyEngagementGrid(map, pawn.Position, zombie.Position);
						var before = DescribeAnomalyEngagementState(testCase, mode, pawn, zombie, zombieFaction);
						var moved = TryFindAdjacentMoveCell(pawn, out var moveCell);
						if (moved)
						{
							pawn.Position = moveCell;
							pawn.Notify_Teleported(false, false);
						}
						var afterMove = DescribeAnomalyEngagementState(testCase, mode, pawn, zombie, zombieFaction);
						AdvanceGameTicks(clampedTicks);
						var afterTicks = DescribeAnomalyEngagementState(testCase, mode, pawn, zombie, zombieFaction);
						var expectedEngagement = Customization.DoesAttractsZombies(pawn) && Tools.Attackable(zombie, mode, pawn);
						var classification = ClassifyAnomalyEngagement(expectedEngagement, moved, before, afterMove, afterTicks);
						summaries.Add(new
						{
							testCase.id,
							testCase.kindDef,
							mode = mode.ToString(),
							expectedEngagement,
							classification,
							attractsAfterMove = afterMove.attracts,
							attackableAfterMove = afterMove.attackable,
							seededAfterMove = afterMove.targetTimestamp > 0 || afterMove.zombieTimestamp > 0 || afterMove.anyAdjacentTimestamp,
							zombieTrackingAfterTicks = afterTicks.zombieTracking,
							zombieMovingAfterTicks = afterTicks.zombiePatherMoving,
							targetTimestampAfterMove = afterMove.targetTimestamp,
							zombieTimestampAfterTicks = afterTicks.zombieTimestamp
						});
						if (includeDetails)
							rows.Add(new
							{
								testCase.id,
								testCase.kindDef,
								mode = mode.ToString(),
								ticks = clampedTicks,
								enemiesAttackZombies,
								expectedEngagement,
								moved,
								moveCell = moved ? ZombieRuntimeActions.DescribeCell(moveCell) : null,
								before,
								afterMove,
								afterTicks,
								classification
							});

						if (cleanup)
						{
							CleanupAnomalyThing(zombie);
							CleanupAnomalyThing(pawn);
							ClearAnomalyEngagementGrid(map);
							_ = spawned.Remove(zombie);
							_ = spawned.Remove(pawn);
						}
					}
				}

				return new
				{
					success = true,
					caseCount = selectedCases.Length,
					rowCount = summaries.Count,
					ticks = clampedTicks,
					classificationCounts = summaries
						.GroupBy(row => row.GetType().GetProperty("classification")?.GetValue(row)?.ToString() ?? "unknown")
						.ToDictionary(group => group.Key, group => group.Count()),
					summaries = summaries.ToArray(),
					rows = rows.ToArray(),
					logNote = "Use rimbridge/list_logs minimumLevel=warning after this operation for the log-clean gate."
				};
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
				if (cleanup)
				{
					for (var i = spawned.Count - 1; i >= 0; i--)
					{
						if (spawned[i] != null && spawned[i].Destroyed == false)
							CleanupAnomalyThing(spawned[i]);
					}
					ZombieRuntimeActions.DestroyZombies(map);
					ClearAnomalyEngagementGrid(map);
				}
			}
		}

		[Tool("zombieland/anomaly_story_smoke", Description = "Run bounded Anomaly fun/perf/exploit scenario smokes: Horaxian moat, player ghoul choke, stealth Everything, Fleshmass field, and Nociosphere field.")]
		public static object AnomalyStorySmoke(
			[ToolParameter(Description = "Scenario names: all, horaxianMoat, ghoulChoke, stealthEverything, fleshmassField, or nociosphereField. Comma-separated.", Required = false, DefaultValue = "all")] string scenarios = "all",
			[ToolParameter(Description = "Ticks to step inside each scenario. Clamped to 60..900.", Required = false, DefaultValue = 360)] int ticks = 360,
			[ToolParameter(Description = "Zombie count for field scenarios. Clamped to 6..80.", Required = false, DefaultValue = 36)] int zombieCount = 36,
			[ToolParameter(Description = "Destroy staged Anomaly pawns, zombies, walls, and buildings at the end of each scenario.", Required = false, DefaultValue = true)] bool cleanup = true)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded." };

			var selectedScenarios = SelectAnomalyStoryScenarios(scenarios);
			var clampedTicks = Mathf.Clamp(ticks, 60, 900);
			var clampedZombieCount = Mathf.Clamp(zombieCount, 6, 80);
			var results = new List<object>();
			var settingsSnapshot = SnapshotZombieSettings();

			try
			{
				foreach (var scenario in selectedScenarios)
				{
					var spawned = new List<Thing>();
					try
					{
						ZombieRuntimeActions.DestroyZombies(map);
						ClearAnomalyEngagementGrid(map);
						var result = scenario switch
						{
							"horaxianMoat" => RunHoraxianMoatSmoke(map, clampedTicks, clampedZombieCount, spawned),
							"ghoulChoke" => RunGhoulChokeSmoke(map, clampedTicks, clampedZombieCount, spawned),
							"stealthEverything" => RunStealthEverythingSmoke(map, clampedTicks, clampedZombieCount, spawned),
							"fleshmassField" => RunFleshmassFieldSmoke(map, clampedTicks, clampedZombieCount, spawned),
							"nociosphereField" => RunNociosphereFieldSmoke(map, clampedTicks, clampedZombieCount, spawned),
							_ => new { id = scenario, success = false, classification = "wrong", error = "Unknown scenario." } as object
						};
						results.Add(result);
					}
					finally
					{
						if (cleanup)
						{
							for (var i = spawned.Count - 1; i >= 0; i--)
							{
								if (spawned[i] != null && spawned[i].Destroyed == false)
									CleanupAnomalyThing(spawned[i]);
							}
							ZombieRuntimeActions.DestroyZombies(map);
							ClearAnomalyEngagementGrid(map);
						}
					}
				}

				return new
				{
					success = results.All(result => GetAnonymousString(result, "classification") == "nominal"),
					scenarioCount = results.Count,
					ticks = clampedTicks,
					zombieCount = clampedZombieCount,
					classificationCounts = results
						.GroupBy(result => GetAnonymousString(result, "classification") ?? "unknown")
						.ToDictionary(group => group.Key, group => group.Count()),
					results = results.ToArray(),
					logNote = "Use rimbridge/list_logs minimumLevel=warning after this operation for the log-clean gate. Screenshots or footage still own subjective fun/exploit judgement."
				};
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
				if (cleanup)
				{
					ZombieRuntimeActions.DestroyZombies(map);
					ClearAnomalyEngagementGrid(map);
				}
			}
		}

		static AnomalyMatrixCase[] SelectAnomalyMatrixCases(string cases)
		{
			if (string.IsNullOrWhiteSpace(cases))
				return anomalyMatrixCases;

			var requested = cases.Split(',')
				.Select(item => item.Trim())
				.Where(item => item.Length > 0)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);

			var selected = anomalyMatrixCases
				.Where(testCase => requested.Contains(testCase.id) || requested.Contains(testCase.kindDef))
				.ToArray();
			if (selected.Length > 0)
				return selected;

			return requested
				.Select(kind => new AnomalyMatrixCase { id = kind, kindDef = kind, faction = "entities" })
				.ToArray();
		}

		static string[] SelectAnomalyStoryScenarios(string scenarios)
		{
			var all = new[] { "horaxianMoat", "ghoulChoke", "stealthEverything", "fleshmassField", "nociosphereField" };
			if (string.IsNullOrWhiteSpace(scenarios) || string.Equals(scenarios.Trim(), "all", StringComparison.OrdinalIgnoreCase))
				return all;
			var requested = scenarios.Split(',')
				.Select(item => item.Trim())
				.Where(item => item.Length > 0)
				.ToArray();
			if (requested.Any(item => string.Equals(item, "all", StringComparison.OrdinalIgnoreCase)))
				return all;
			return requested;
		}

		static AnomalyMatrixCase[] SelectAnomalyEngagementCases(string cases)
		{
			if (string.IsNullOrWhiteSpace(cases))
			{
				var ids = new HashSet<string>(new[]
				{
					"FleshmassNucleus-passive",
					"Metalhorror-awake",
					"Metalhorror-dormant",
					"ShamblerSoldier-awake",
					"Ghoul-player",
					"Horaxian_Gunner"
				}, StringComparer.OrdinalIgnoreCase);
				return anomalyMatrixCases.Where(testCase => ids.Contains(testCase.id)).ToArray();
			}
			return SelectAnomalyMatrixCases(cases);
		}

		static object RunHoraxianMoatSmoke(Map map, int ticks, int zombieCount, List<Thing> spawned)
		{
			ApplyZombieSettingsOverride(values =>
			{
				values.attackMode = AttackMode.OnlyHumans;
				values.enemyZombieResponse = ZombieResponsePolicy.Minimal;
				values.animalsAttackZombies = false;
			});
			var root = map.Center + new IntVec3(-32, 0, -26);
			if (TrySpawnAnomalyPawn(map, "Horaxian_Gunner", "horax", root + new IntVec3(6, 0, 0), spawned, out var horaxian, out var pawnError) == false)
				return pawnError;
			var walls = SpawnScenarioWallLine(map, root + new IntVec3(2, 0, -5), 11, IntVec3.North, spawned);
			var zombies = SpawnScenarioZombies(map, root, Math.Min(zombieCount, 28), 10, spawned);
			var attackable = zombies.Any(zombie => Tools.Attackable(zombie, ZombieSettings.Values.attackMode, horaxian));
			var moved = MovePawnForPheromone(horaxian);
			AdvanceGameTicks(ticks);
			var tracking = CountTracking(zombies);
			var damaged = horaxian.health?.hediffSet?.hediffs?.Any(hediff => hediff is Hediff_Injury) ?? false;
			var classification = attackable && moved && tracking > 0 ? "nominal" : "unclear";
			return new
			{
				id = "horaxianMoat",
				classification,
				success = classification == "nominal",
				ticks,
				nominalBehavior = "Hostile human DLC pawns should turn a zombie perimeter into a real raid hazard under OnlyHumans.",
				wallCount = walls.Length,
				zombiesSpawned = zombies.Length,
				zombiesTracking = tracking,
				horaxianAttackable = attackable,
				horaxianMovedForPheromone = moved,
				horaxianDamaged = damaged,
				horaxian = DescribePawn(horaxian),
				sampleZombies = CompactZombieSamples(zombies)
			};
		}

		static object RunGhoulChokeSmoke(Map map, int ticks, int zombieCount, List<Thing> spawned)
		{
			var root = map.Center + new IntVec3(26, 0, -26);
			if (TrySpawnAnomalyPawn(map, "Ghoul", "player", root, spawned, out var ghoul, out var pawnError) == false)
				return pawnError;
			var walls = SpawnScenarioWallLine(map, root + new IntVec3(-1, 0, -4), 9, IntVec3.North, spawned)
				.Concat(SpawnScenarioWallLine(map, root + new IntVec3(1, 0, -4), 9, IntVec3.North, spawned))
				.ToArray();

			ApplyZombieSettingsOverride(values =>
			{
				values.attackMode = AttackMode.OnlyColonists;
				values.enemyZombieResponse = ZombieResponsePolicy.Minimal;
				values.animalsAttackZombies = false;
			});
			var onlyColonistsZombies = SpawnScenarioZombies(map, root + new IntVec3(-4, 0, 0), Math.Min(zombieCount, 24), 8, spawned);
			var onlyColonistsAttackable = onlyColonistsZombies.Any(zombie => Tools.Attackable(zombie, ZombieSettings.Values.attackMode, ghoul));
			var onlyColonistsMoved = MovePawnForPheromone(ghoul);
			AdvanceGameTicks(ticks);
			var onlyColonistsTracking = CountTracking(onlyColonistsZombies);
			foreach (var zombie in onlyColonistsZombies)
				CleanupAnomalyThing(zombie);
			ClearAnomalyEngagementGrid(map);

			ApplyZombieSettingsOverride(values =>
			{
				values.attackMode = AttackMode.OnlyHumans;
				values.enemyZombieResponse = ZombieResponsePolicy.Minimal;
				values.animalsAttackZombies = false;
			});
			var onlyHumansZombies = SpawnScenarioZombies(map, root + new IntVec3(-4, 0, 0), Math.Min(zombieCount, 24), 8, spawned);
			var onlyHumansAttackable = onlyHumansZombies.Any(zombie => Tools.Attackable(zombie, ZombieSettings.Values.attackMode, ghoul));
			var onlyHumansMoved = MovePawnForPheromone(ghoul);
			AdvanceGameTicks(ticks);
			var onlyHumansTracking = CountTracking(onlyHumansZombies);
			var classification = onlyColonistsAttackable == false && onlyColonistsTracking == 0 && onlyHumansAttackable && onlyHumansTracking > 0 ? "nominal" : "unclear";
			return new
			{
				id = "ghoulChoke",
				classification,
				success = classification == "nominal",
				ticks,
				nominalBehavior = "Player ghoul is not a colonist target under OnlyColonists but becomes zombie-attracting under OnlyHumans.",
				wallCount = walls.Length,
				onlyColonists = new
				{
					zombiesSpawned = onlyColonistsZombies.Length,
					moved = onlyColonistsMoved,
					attackable = onlyColonistsAttackable,
					zombiesTracking = onlyColonistsTracking
				},
				onlyHumans = new
				{
					zombiesSpawned = onlyHumansZombies.Length,
					moved = onlyHumansMoved,
					attackable = onlyHumansAttackable,
					zombiesTracking = onlyHumansTracking
				},
				ghoul = DescribePawn(ghoul)
			};
		}

		static object RunStealthEverythingSmoke(Map map, int ticks, int zombieCount, List<Thing> spawned)
		{
			ApplyZombieSettingsOverride(values =>
			{
				values.attackMode = AttackMode.Everything;
				values.enemyZombieResponse = ZombieResponsePolicy.Minimal;
				values.animalsAttackZombies = false;
			});
			var root = map.Center + new IntVec3(-30, 0, 20);
			var rows = new List<object>();
			foreach (var kindDef in new[] { "Sightstealer", "Revenant" })
			{
				if (TrySpawnAnomalyPawn(map, kindDef, "entities", root + new IntVec3(rows.Count * 18, 0, 0), spawned, out var entity, out var pawnError) == false)
					return pawnError;
				var zombies = SpawnScenarioZombies(map, entity.Position + new IntVec3(-5, 0, 0), Math.Min(zombieCount / 2, 18), 8, spawned);
				var attackable = zombies.Any(zombie => Tools.Attackable(zombie, ZombieSettings.Values.attackMode, entity));
				var moved = MovePawnForPheromone(entity);
				AdvanceGameTicks(ticks);
				var tracking = CountTracking(zombies);
				rows.Add(new
				{
					kindDef,
					moved,
					attackable,
					zombiesSpawned = zombies.Length,
					zombiesTracking = tracking,
					entity = DescribePawn(entity),
					sampleZombies = CompactZombieSamples(zombies)
				});
			}
			var classification = rows.All(row => (bool)row.GetType().GetProperty("attackable").GetValue(row) && (int)row.GetType().GetProperty("zombiesTracking").GetValue(row) > 0)
				? "nominal"
				: "unclear";
			return new
			{
				id = "stealthEverything",
				classification,
				success = classification == "nominal",
				ticks,
				nominalBehavior = "Everything mode allows zombies to track invisible/stealth Anomaly entities as unhinged zombie-smell behavior.",
				rows = rows.ToArray()
			};
		}

		static object RunFleshmassFieldSmoke(Map map, int ticks, int zombieCount, List<Thing> spawned)
		{
			ApplyZombieSettingsOverride(values =>
			{
				values.attackMode = AttackMode.Everything;
				values.enemyZombieResponse = ZombieResponsePolicy.Minimal;
				values.animalsAttackZombies = false;
			});
			var root = map.Center + new IntVec3(28, 0, 20);
			var heartDef = DefDatabase<ThingDef>.GetNamedSilentFail("FleshmassHeart");
			if (heartDef == null)
				return new { id = "fleshmassField", classification = "wrong", success = false, error = "FleshmassHeart def was not loaded." };
			if (TryFindClearSpawnCell(map, root, 16f, out var heartCell, out var heartCellError) == false)
				return heartCellError;
			var heartThing = ThingMaker.MakeThing(heartDef);
			if (heartThing is not Building_FleshmassHeart heart)
				return new { id = "fleshmassField", classification = "wrong", success = false, error = "FleshmassHeart did not create expected building type." };
			heart.SetFaction(ResolveAnomalyMatrixFaction("entities"));
			GenSpawn.Spawn(heart, heartCell, map, Rot4.South);
			spawned.Add(heart);
			var heartHitPointsBefore = heart.HitPoints;

			if (TrySpawnAnomalyPawn(map, "Gorehulk", "entities", heartCell + new IntVec3(5, 0, 0), spawned, out var fleshbeast, out var pawnError) == false)
				return pawnError;
			var zombies = SpawnScenarioZombies(map, heartCell + new IntVec3(-5, 0, 0), Math.Min(zombieCount, 32), 10, spawned);
			var heartAttackable = zombies.Any(zombie => Tools.Attackable(zombie, ZombieSettings.Values.attackMode, heart));
			var fleshbeastAttackable = zombies.Any(zombie => Tools.Attackable(zombie, ZombieSettings.Values.attackMode, fleshbeast));
			var moved = MovePawnForPheromone(fleshbeast);
			AdvanceGameTicks(ticks);
			var tracking = CountTracking(zombies);
			var heartUnchanged = heart.Destroyed == false && heart.HitPoints == heartHitPointsBefore;
			var classification = heartAttackable == false && heartUnchanged && fleshbeastAttackable && moved && tracking > 0 ? "nominal" : "unclear";
			return new
			{
				id = "fleshmassField",
				classification,
				success = classification == "nominal",
				ticks,
				nominalBehavior = "Zombie fields should disrupt fleshbeast pawns but not trivialize the FleshmassHeart building.",
				zombiesSpawned = zombies.Length,
				zombiesTracking = tracking,
				heartAttackable,
				heartHitPointsBefore,
				heartHitPointsAfter = heart.Destroyed ? 0 : heart.HitPoints,
				heartUnchanged,
				fleshbeastAttackable,
				fleshbeastMovedForPheromone = moved,
				heart = DescribeAnomalyThing(heart),
				fleshbeast = DescribePawn(fleshbeast)
			};
		}

		static object RunNociosphereFieldSmoke(Map map, int ticks, int zombieCount, List<Thing> spawned)
		{
			ApplyZombieSettingsOverride(values =>
			{
				values.attackMode = AttackMode.Everything;
				values.enemyZombieResponse = ZombieResponsePolicy.Full;
				values.animalsAttackZombies = true;
			});
			var root = map.Center + new IntVec3(0, 0, 34);
			if (TrySpawnAnomalyPawn(map, "Nociosphere", "entities", root, spawned, out var nociosphere, out var pawnError, activityState: "active", disableWork: false) == false)
				return pawnError;
			var zombies = SpawnScenarioZombies(map, root + new IntVec3(-5, 0, 0), zombieCount, 14, spawned);
			var attackable = zombies.Any(zombie => Tools.Attackable(zombie, ZombieSettings.Values.attackMode, nociosphere));
			var moved = MovePawnForPheromone(nociosphere);
			var zombiesTickedBefore = ZombieTicker.zombiesTicked;
			var startedAt = DateTime.UtcNow;
			AdvanceGameTicks(ticks);
			var elapsedMs = (int)(DateTime.UtcNow - startedAt).TotalMilliseconds;
			var liveZombies = zombies.Count(zombie => zombie != null && zombie.Destroyed == false && zombie.Dead == false && zombie.Spawned);
			var downedZombies = zombies.Count(zombie => zombie != null && zombie.Destroyed == false && zombie.Downed);
			var destroyedOrDead = zombies.Length - liveZombies;
			var classification = attackable && moved && zombies.Length > 0 && nociosphere.Destroyed == false ? "nominal" : "unclear";
			return new
			{
				id = "nociosphereField",
				classification,
				success = classification == "nominal",
				ticks,
				nominalBehavior = "Active Nociosphere crossing a zombie-heavy field should stay log/perf clean while exposing whether it becomes chaos or a meat blender.",
				zombiesSpawned = zombies.Length,
				liveZombies,
				downedZombies,
				destroyedOrDead,
				attackable,
				movedForPheromone = moved,
				elapsedMs,
				zombiesTickedDelta = ZombieTicker.zombiesTicked - zombiesTickedBefore,
				nociosphere = DescribePawn(nociosphere),
				sampleZombies = CompactZombieSamples(zombies)
			};
		}

		static bool TrySpawnAnomalyPawn(Map map, string kindDefName, string factionKey, IntVec3 root, List<Thing> spawned, out Pawn pawn, out object error, string activityState = null, string dormancyState = null, bool disableWork = true)
		{
			pawn = null;
			error = null;
			var kindDef = DefDatabase<PawnKindDef>.GetNamedSilentFail(kindDefName);
			if (kindDef == null)
			{
				error = new { id = kindDefName, classification = "wrong", success = false, error = $"PawnKindDef {kindDefName} was not loaded." };
				return false;
			}
			if (TryFindClearSpawnCell(map, root, 18f, out var cell, out var cellError) == false)
			{
				error = cellError;
				return false;
			}
			pawn = PawnGenerator.GeneratePawn(kindDef, ResolveAnomalyMatrixFaction(factionKey));
			GenSpawn.Spawn(pawn, cell, map, Rot4.South);
			if (disableWork)
				DisablePawnWork(pawn);
			ApplyAnomalyState(pawn, activityState, dormancyState);
			spawned.Add(pawn);
			return true;
		}

		static Zombie[] SpawnScenarioZombies(Map map, IntVec3 center, int count, int radius, List<Thing> spawned)
		{
			var zombies = new List<Zombie>();
			foreach (var cell in GenRadial.RadialCellsAround(center, radius, true)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetThingList(map).OfType<Pawn>().Any() == false)
				.Take(count))
			{
				var zombie = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.Normal, true);
				if (zombie == null)
					continue;
				PrepareWallPushZombie(map, zombie, cell);
				spawned.Add(zombie);
				zombies.Add(zombie);
			}
			return zombies.ToArray();
		}

		static Building[] SpawnScenarioWallLine(Map map, IntVec3 start, int length, IntVec3 step, List<Thing> spawned)
		{
			var walls = new List<Building>();
			var stuffDef = ThingDefOf.WoodLog;
			for (var i = 0; i < length; i++)
			{
				var cell = start + step * i;
				if (cell.InBounds(map) == false || cell.Fogged(map))
					continue;
				foreach (var existing in cell.GetThingList(map).Where(thing => thing.def.category == ThingCategory.Building).ToArray())
					existing.Destroy(DestroyMode.Vanish);
				var wall = ThingMaker.MakeThing(ThingDefOf.Wall, stuffDef) as Building;
				if (wall == null)
					continue;
				GenSpawn.Spawn(wall, cell, map, WipeMode.Vanish);
				wall.SetFaction(Faction.OfPlayer);
				spawned.Add(wall);
				walls.Add(wall);
			}
			map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
			return walls.ToArray();
		}

		static bool MovePawnForPheromone(Pawn pawn)
		{
			if (pawn?.Spawned != true || TryFindAdjacentMoveCell(pawn, out var moveCell) == false)
				return false;
			pawn.Position = moveCell;
			pawn.Notify_Teleported(false, false);
			return true;
		}

		static int CountTracking(IEnumerable<Zombie> zombies)
		{
			return zombies.Count(zombie => zombie != null && zombie.Destroyed == false && zombie.Spawned && zombie.state == ZombieState.Tracking);
		}

		static object[] CompactZombieSamples(IEnumerable<Zombie> zombies, int count = 3)
		{
			return zombies
				.Where(zombie => zombie != null)
				.Take(count)
				.Select(zombie => new
				{
					id = ZombieRuntimeActions.StableThingId(zombie),
					spawned = zombie.Spawned,
					destroyed = zombie.Destroyed,
					dead = zombie.Dead,
					downed = zombie.Downed,
					state = zombie.state.ToString(),
					currentJob = zombie.CurJobDef?.defName,
					position = zombie.Spawned ? ZombieRuntimeActions.DescribeCell(zombie.Position) : null
				})
				.Cast<object>()
				.ToArray();
		}

		static string GetAnonymousString(object instance, string propertyName)
		{
			return instance?.GetType().GetProperty(propertyName)?.GetValue(instance)?.ToString();
		}

		static object DescribeAnomalyMatrixRow(AnomalyMatrixCase testCase, Pawn pawn, Zombie zombie, Faction zombieFaction, SettingsGroup settings)
		{
			var oldAttackMode = settings.attackMode;
			var oldEnemyZombieResponse = settings.enemyZombieResponse;
			var oldAnimalsAttackZombies = settings.animalsAttackZombies;
			var oldAnomalyAttacksZombies = settings.anomalyAttacksZombies;
			try
			{
				var attackModes = new[] { AttackMode.OnlyColonists, AttackMode.OnlyHumans, AttackMode.Everything }
					.Select(mode =>
					{
						settings.attackMode = mode;
						return new
						{
							mode = mode.ToString(),
							attracts = Customization.DoesAttractsZombies(pawn),
							attackable = Tools.Attackable(zombie, mode, pawn)
						};
					})
					.ToArray();

				settings.enemyZombieResponse = ZombieResponsePolicy.Minimal;
				settings.animalsAttackZombies = false;
				settings.anomalyAttacksZombies = AnomalyTargetingOverride.Automatic;
				var reverseDisabled = new
				{
					toZombieThing = DescribeHostility(TryHostileTo(pawn, zombie)),
					toZombieFaction = DescribeHostility(TryHostileTo(pawn, zombieFaction)),
					isHostileToZombies = Tools.IsHostileToZombies(pawn)
				};

				settings.enemyZombieResponse = ZombieResponsePolicy.Full;
				settings.animalsAttackZombies = true;
				settings.anomalyAttacksZombies = AnomalyTargetingOverride.Automatic;
				var reverseEnabled = new
				{
					toZombieThing = DescribeHostility(TryHostileTo(pawn, zombie)),
					toZombieFaction = DescribeHostility(TryHostileTo(pawn, zombieFaction)),
					isHostileToZombies = Tools.IsHostileToZombies(pawn)
				};

				return new
				{
					testCase.id,
					testCase.kindDef,
					requestedFaction = testCase.faction,
					requestedActivityState = testCase.activityState,
					requestedDormancyState = testCase.dormancyState,
					pawn = DescribePawn(pawn),
					zombie = DescribeZombie(zombie),
					race = new
					{
						pawn.RaceProps.Humanlike,
						pawn.RaceProps.Animal,
						pawn.RaceProps.IsFlesh,
						pawn.RaceProps.IsMechanoid,
						pawn.IsShambler
					},
					activity = DescribeAnomalyActivity(pawn),
					dormancy = DescribeAnomalyDormancy(pawn),
					vanillaThreat = DescribeAnomalyVanillaThreat(pawn, zombie),
					cannotBecomeZombie = Customization.CannotBecomeZombie(pawn),
					infectionState = pawn.InfectionState().ToString(),
					attackModes,
					reverseHostility = new
					{
						enemiesAndAnimalsDisabled = reverseDisabled,
						enemiesAndAnimalsEnabled = reverseEnabled
					}
				};
			}
			finally
			{
				settings.attackMode = oldAttackMode;
				settings.enemyZombieResponse = oldEnemyZombieResponse;
				settings.animalsAttackZombies = oldAnimalsAttackZombies;
				settings.anomalyAttacksZombies = oldAnomalyAttacksZombies;
			}
		}

		static void ConfigureAnomalyTargetingOverride(SettingsGroup settings, AttackMode attackMode, AnomalyTargetingCategory category, AnomalyTargetingOverride mode)
		{
			settings.attackMode = attackMode;
			settings.enemyZombieResponse = ZombieResponsePolicy.Minimal;
			settings.animalsAttackZombies = false;
			settings.anomalyGhoulTargeting = AnomalyTargetingOverride.Automatic;
			settings.anomalyShamblerTargeting = AnomalyTargetingOverride.Automatic;
			settings.anomalyEntityTargeting = AnomalyTargetingOverride.Automatic;
			settings.anomalyNociosphereTargeting = AnomalyTargetingOverride.Automatic;
			settings.anomalyAttacksZombies = AnomalyTargetingOverride.Automatic;
			switch (category)
			{
				case AnomalyTargetingCategory.Ghouls:
					settings.anomalyGhoulTargeting = mode;
					break;
				case AnomalyTargetingCategory.ShamblerMutants:
					settings.anomalyShamblerTargeting = mode;
					break;
				case AnomalyTargetingCategory.OtherEntities:
					settings.anomalyEntityTargeting = mode;
					break;
				case AnomalyTargetingCategory.Nociosphere:
					settings.anomalyNociosphereTargeting = mode;
					break;
			}
		}

		static void ClearAnomalyEngagementGrid(Map map, IntVec3 pawnCell, IntVec3 zombieCell)
		{
			if (map == null)
				return;
			var grid = map.GetGrid();
			foreach (var cell in GenRadial.RadialCellsAround(pawnCell, 24f, true)
				.Concat(GenRadial.RadialCellsAround(zombieCell, 24f, true))
				.Where(cell => cell.InBounds(map))
				.Distinct())
			{
				grid.SetTimestamp(cell, 0);
				var count = grid.GetZombieCount(cell);
				if (count != 0)
					grid.ChangeZombieCount(cell, -count);
			}
		}

		static void ClearAnomalyEngagementGrid(Map map)
		{
			map?.GetGrid()?.IterateCellsQuick(cell =>
			{
				cell.timestamp = 0;
				cell.zombieCount = 0;
			});
		}

		static AnomalyEngagementState DescribeAnomalyEngagementState(AnomalyMatrixCase testCase, AttackMode mode, Pawn pawn, Zombie zombie, Faction zombieFaction)
		{
			var grid = pawn?.Map?.GetGrid() ?? zombie?.Map?.GetGrid();
			var targetTimestamp = pawn?.Spawned == true && grid != null ? grid.GetTimestamp(pawn.Position) : 0;
			var zombieTimestamp = zombie?.Spawned == true && grid != null ? grid.GetTimestamp(zombie.Position) : 0;
			var nextToZombie = zombie?.Spawned == true && grid != null
				? GenAdj.AdjacentCells
					.Select(offset => zombie.Position + offset)
					.Where(cell => cell.InBounds(zombie.Map))
					.Select(cell => new
					{
						cell = ZombieRuntimeActions.DescribeCell(cell),
						timestamp = grid.GetTimestamp(cell),
						zombieCount = grid.GetZombieCount(cell)
					})
					.Where(sample => sample.timestamp > 0 || sample.zombieCount != 0)
					.Cast<object>()
					.ToArray()
				: Array.Empty<object>();
			var driver = zombie?.jobs?.curDriver as JobDriver_Stumble;
			var anyAdjacentTimestamp = zombie?.Spawned == true && grid != null
				&& GenAdj.AdjacentCells
					.Select(offset => zombie.Position + offset)
					.Where(cell => cell.InBounds(zombie.Map))
					.Any(cell => grid.GetTimestamp(cell) > 0);
			var anyAdjacentZombieCount = zombie?.Spawned == true && grid != null
				&& GenAdj.AdjacentCells
					.Select(offset => zombie.Position + offset)
					.Where(cell => cell.InBounds(zombie.Map))
					.Any(cell => grid.GetZombieCount(cell) != 0);
			return new AnomalyEngagementState
			{
				testCase = testCase.id,
				mode = mode.ToString(),
				pawn = DescribePawn(pawn),
				zombie = DescribeZombie(zombie),
				activity = DescribeAnomalyActivity(pawn),
				dormancy = DescribeAnomalyDormancy(pawn),
				vanillaThreat = DescribeAnomalyVanillaThreat(pawn, zombie),
				attracts = pawn != null && Customization.DoesAttractsZombies(pawn),
				attackable = pawn != null && zombie != null && Tools.Attackable(zombie, mode, pawn),
				reverseHostility = DescribeAnomalyReverseHostility(pawn, zombie, zombieFaction, ZombieSettings.Values),
				targetTimestamp = targetTimestamp,
				zombieTimestamp = zombieTimestamp,
				anyAdjacentTimestamp = anyAdjacentTimestamp,
				anyAdjacentZombieCount = anyAdjacentZombieCount,
				zombieTracking = zombie?.state == ZombieState.Tracking,
				zombiePatherMoving = zombie?.pather?.Moving ?? false,
				grid = new
				{
					targetTimestamp,
					zombieTimestamp,
					targetZombieCount = pawn?.Spawned == true && grid != null ? grid.GetZombieCount(pawn.Position) : 0,
					zombieCellZombieCount = zombie?.Spawned == true && grid != null ? grid.GetZombieCount(zombie.Position) : 0,
					nextToZombie
				},
				stumbleDriver = new
				{
					present = driver != null,
					destination = driver?.destination.IsValid == true ? ZombieRuntimeActions.DescribeCell(driver.destination) : null
				}
			};
		}

		static string ClassifyAnomalyEngagement(bool expectedEngagement, bool moved, AnomalyEngagementState before, AnomalyEngagementState afterMove, AnomalyEngagementState afterTicks)
		{
			var seeded = afterMove.targetTimestamp > 0 || afterMove.zombieTimestamp > 0 || afterMove.anyAdjacentTimestamp;
			var seededWithinWindow = seeded || afterTicks.targetTimestamp > 0 || afterTicks.zombieTimestamp > 0 || afterTicks.anyAdjacentTimestamp;
			var tracking = afterTicks.zombieTracking;
			var quietBefore = before.targetTimestamp == 0 && before.zombieTimestamp == 0 && before.anyAdjacentTimestamp == false;
			if (expectedEngagement)
				return moved && quietBefore && seededWithinWindow && tracking ? "nominal" : "unclear";
			return quietBefore && seeded == false && tracking == false ? "nominal" : "wrong";
		}

		static Faction ResolveAnomalyMatrixFaction(string key)
		{
			var normalized = (key ?? "entities").Trim().ToLowerInvariant();
			if (normalized == "player")
				return Faction.OfPlayer;
			if (normalized == "hostile")
				return Faction.OfAncientsHostile;
			if (normalized == "none")
				return null;
			if (normalized == "horax")
			{
				var horax = DefDatabase<FactionDef>.GetNamedSilentFail("HoraxCult");
				return horax == null ? Faction.OfAncientsHostile : Find.FactionManager.FirstFactionOfDef(horax) ?? Faction.OfAncientsHostile;
			}
			var entities = DefDatabase<FactionDef>.GetNamedSilentFail("Entities");
			if (entities != null)
				return Find.FactionManager.FirstFactionOfDef(entities);
			return null;
		}

		static void ApplyAnomalyState(Pawn pawn, string activityState, string dormancyState)
		{
			if (pawn == null)
				return;

			if (pawn.activity != null)
			{
				var normalized = (activityState ?? "").Trim().ToLowerInvariant();
				if (normalized == "active")
					pawn.activity.EnterActiveState();
				else if (normalized == "passive")
					pawn.activity.EnterPassiveState();
				else if (normalized == "deactivated")
					pawn.activity.Deactivate();
			}

			if (pawn.canBeDormant != null)
			{
				var normalized = (dormancyState ?? "").Trim().ToLowerInvariant();
				if (normalized == "awake")
					pawn.canBeDormant.WakeUp();
				else if (normalized == "dormant" || normalized == "sleep")
					pawn.canBeDormant.ToSleep();
			}
		}

		static object DescribeAnomalyActivity(Pawn pawn)
		{
			var activity = pawn?.activity;
			if (activity == null)
				return new { present = false };
			return new
			{
				present = true,
				state = activity.State.ToString(),
				activityLevel = activity.ActivityLevel,
				deactivated = activity.Deactivated,
				canBeSuppressed = activity.CanBeSuppressed,
				isDormant = activity.IsDormant
			};
		}

		static object DescribeAnomalyDormancy(Pawn pawn)
		{
			var dormancy = pawn?.canBeDormant;
			if (dormancy == null)
				return new { present = false };
			return new
			{
				present = true,
				awake = dormancy.Awake,
				waitingToWakeUp = dormancy.WaitingToWakeUp,
				showZs = dormancy.ShowZs,
				sleepJob = dormancy.SleepJob?.defName
			};
		}

		static object DescribeAnomalyVanillaThreat(Pawn pawn, Zombie zombie)
		{
			var zombieFaction = zombie?.Faction;
			var attackTarget = pawn as IAttackTarget;
			var canQueryThreat = attackTarget != null && pawn?.Spawned == true && pawn.Map != null;
			return new
			{
				implementsAttackTarget = attackTarget != null,
				hostileToZombieThing = DescribeHostility(TryHostileTo(pawn, zombie)),
				hostileToZombieFaction = DescribeHostility(TryHostileTo(pawn, zombieFaction)),
				hostileToPlayerFaction = DescribeHostility(TryHostileTo(pawn, Faction.OfPlayer)),
				threatQuerySkipped = canQueryThreat == false,
				potentialThreat = canQueryThreat == false ? (bool?)null : GenHostility.IsPotentialThreat(attackTarget),
				activeThreatToPlayer = canQueryThreat == false ? (bool?)null : GenHostility.IsActiveThreatTo(attackTarget, Faction.OfPlayer, false, false),
				activeThreatToZombies = canQueryThreat == false || zombieFaction == null ? (bool?)null : GenHostility.IsActiveThreatTo(attackTarget, zombieFaction, false, false)
			};
		}

		static object DescribeNociospherePolicy(Pawn nociosphere, Zombie zombie, Faction zombieFaction, SettingsGroup settings)
		{
			return new
			{
				pawn = DescribePawn(nociosphere),
				zombie = DescribeZombie(zombie),
				activity = DescribeAnomalyActivity(nociosphere),
				vanillaThreat = DescribeAnomalyVanillaThreat(nociosphere, zombie),
				attackModes = DescribeAnomalyThingAttackModes(zombie, nociosphere, settings),
				reverseHostility = DescribeAnomalyReverseHostility(nociosphere, zombie, zombieFaction, settings)
			};
		}

		static object DescribeThingPolicy(string id, Thing thing, Zombie zombie, Faction zombieFaction, SettingsGroup settings)
		{
			return new
			{
				id,
				thing = DescribeAnomalyThing(thing),
				zombie = DescribeZombie(zombie),
				attackModes = DescribeAnomalyThingAttackModes(zombie, thing, settings),
				reverseHostility = new
				{
					toZombieThing = DescribeHostility(TryHostileTo(thing, zombie)),
					toZombieFaction = DescribeHostility(TryHostileTo(thing, zombieFaction))
				}
			};
		}

		static object[] DescribeAnomalyThingAttackModes(Zombie zombie, Thing thing, SettingsGroup settings)
		{
			var oldAttackMode = settings.attackMode;
			try
			{
				return new[] { AttackMode.OnlyColonists, AttackMode.OnlyHumans, AttackMode.Everything }
					.Select(mode =>
					{
						settings.attackMode = mode;
						return new
						{
							mode = mode.ToString(),
							attackable = Tools.Attackable(zombie, mode, thing),
							attracts = thing is Pawn pawn ? Customization.DoesAttractsZombies(pawn) : (bool?)null
						};
					})
					.Cast<object>()
					.ToArray();
			}
			finally
			{
				settings.attackMode = oldAttackMode;
			}
		}

		static object DescribeAnomalyReverseHostility(Pawn pawn, Zombie zombie, Faction zombieFaction, SettingsGroup settings)
		{
			var oldEnemyZombieResponse = settings.enemyZombieResponse;
			var oldAnimalsAttackZombies = settings.animalsAttackZombies;
			var oldAnomalyAttacksZombies = settings.anomalyAttacksZombies;
			try
			{
				settings.enemyZombieResponse = ZombieResponsePolicy.Minimal;
				settings.animalsAttackZombies = false;
				settings.anomalyAttacksZombies = AnomalyTargetingOverride.Automatic;
				var disabled = new
				{
					toZombieThing = DescribeHostility(TryHostileTo(pawn, zombie)),
					toZombieFaction = DescribeHostility(TryHostileTo(pawn, zombieFaction)),
					isHostileToZombies = Tools.IsHostileToZombies(pawn)
				};

				settings.enemyZombieResponse = ZombieResponsePolicy.Full;
				settings.animalsAttackZombies = true;
				settings.anomalyAttacksZombies = AnomalyTargetingOverride.Automatic;
				var enabled = new
				{
					toZombieThing = DescribeHostility(TryHostileTo(pawn, zombie)),
					toZombieFaction = DescribeHostility(TryHostileTo(pawn, zombieFaction)),
					isHostileToZombies = Tools.IsHostileToZombies(pawn)
				};

				return new
				{
					enemiesAndAnimalsDisabled = disabled,
					enemiesAndAnimalsEnabled = enabled
				};
			}
			finally
			{
				settings.enemyZombieResponse = oldEnemyZombieResponse;
				settings.animalsAttackZombies = oldAnimalsAttackZombies;
				settings.anomalyAttacksZombies = oldAnomalyAttacksZombies;
			}
		}

		static object DescribeAnomalyThing(Thing thing)
		{
			return thing == null ? null : new
			{
				id = ZombieRuntimeActions.StableThingId(thing),
				thingId = thing.ThingID,
				defName = thing.def?.defName,
				type = thing.GetType().FullName,
				label = thing.LabelCap.ToString(),
				faction = thing.Faction?.Name,
				spawned = thing.Spawned,
				destroyed = thing.Destroyed,
				position = thing.Spawned ? ZombieRuntimeActions.DescribeCell(thing.Position) : null,
				hitPoints = thing.HitPoints,
				maxHitPoints = thing.MaxHitPoints
			};
		}

		static void CleanupAnomalyThing(Thing thing)
		{
			if (thing == null || thing.Destroyed)
				return;
			if (thing.Spawned && thing.def?.destroyable == false)
				thing.DeSpawn(DestroyMode.Vanish);
			else
				thing.Destroy(DestroyMode.Vanish);
		}
	}
}
