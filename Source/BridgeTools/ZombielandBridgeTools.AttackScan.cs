using RimBridgeServer.Sdk;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	public sealed partial class ZombielandBridgeTools
	{
		sealed class AttackScanCaseState
		{
			public Thing expectedTarget;
			public readonly List<object> staged = new();
		}

		[Tool("zombieland/attack_scan_equivalence", Description = "Compare legacy adjacent zombie attack scanning with the indexed candidate path on current-map zombies without starting attack jobs.")]
		public static object AttackScanEquivalence(
			[ToolParameter(Description = "Maximum ordinary zombies to scan.", Required = false, DefaultValue = 400)] int limit = 400,
			[ToolParameter(Description = "When true, randomize each zombie's adjacent-cell order once before comparison. This advances Zombieland's scan RNG and is meant for paused diagnostics.", Required = false, DefaultValue = false)] bool randomizeOrder = false)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded." };

			var cappedLimit = Math.Max(1, Math.Min(limit, 1000));
			var index = map.GetComponent<ZombieAttackTargetIndex>();
			index?.Invalidate();
			var zombies = CurrentZombies(map)
				.OfType<Zombie>()
				.Where(zombie => zombie.Spawned && zombie.Dead == false)
				.Take(cappedLimit)
				.ToArray();

			var comparisons = zombies.Select(zombie => ZombieStateHandler.CompareCanAttackScans(zombie, randomizeOrder)).ToArray();
			var mismatches = comparisons.Where(comparison => comparison.Matches == false).ToArray();
			return new
			{
				success = mismatches.Length == 0,
				scanned = comparisons.Length,
				limit = cappedLimit,
				randomizeOrder,
				mismatchCount = mismatches.Length,
				index = DescribeAttackTargetIndex(index),
				mismatches = mismatches.Take(20).Select(DescribeAttackScanComparison).ToArray(),
				samples = comparisons.Take(12).Select(DescribeAttackScanComparison).ToArray()
			};
		}

		[Tool("zombieland/attack_scan_fixture_contract", Description = "Stage compact adjacent-target cases and compare legacy zombie attack scanning with the indexed candidate path.")]
		public static object AttackScanFixtureContract(
			[ToolParameter(Description = "Root x coordinate. Use -1 with z -1 to search near map center.", Required = false, DefaultValue = -1)] int x = -1,
			[ToolParameter(Description = "Root z coordinate. Use -1 with x -1 to search near map center.", Required = false, DefaultValue = -1)] int z = -1)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded." };

			var root = x >= 0 && z >= 0 ? new IntVec3(x, 0, z) : new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (root.InBounds(map) == false)
				return new { success = false, error = $"Cell ({root.x}, {root.z}) is outside the current map." };

			var settingsSnapshot = SnapshotZombieSettings();
			var spawned = new List<Thing>();
			var results = new List<object>();
			var caseIndex = 0;

			try
			{
				results.Add(RunAttackScanCase(map, root, caseIndex++, "empty-adjacent-cells", AttackMode.Everything, MakeAttackScanOrder(0), spawned, (zombie, center, state) => { }));

				for (var direction = 0; direction < 8; direction++)
				{
					var localDirection = direction;
					results.Add(RunAttackScanCase(map, root, caseIndex++, $"single-colonist-direction-{direction}", AttackMode.Everything, MakeAttackScanOrder(direction), spawned, (zombie, center, state) =>
					{
						var pawn = SpawnAttackScanColonist(map, center + ZombieStateHandler.AttackAdjacentOffset(localDirection), Faction.OfPlayer, $"ZL_AttackDir{localDirection}", spawned);
						state.expectedTarget = pawn;
						state.staged.Add(DescribePawn(pawn));
					}));
				}

				results.Add(RunAttackScanCase(map, root, caseIndex++, "two-cells-order-right-before-left", AttackMode.Everything, MakeAttackScanOrder(4, 0), spawned, (zombie, center, state) =>
				{
					var left = SpawnAttackScanColonist(map, center + ZombieStateHandler.AttackAdjacentOffset(0), Faction.OfPlayer, "ZL_AttackLeft", spawned);
					var right = SpawnAttackScanColonist(map, center + ZombieStateHandler.AttackAdjacentOffset(4), Faction.OfPlayer, "ZL_AttackRight", spawned);
					state.expectedTarget = right;
					state.staged.Add(DescribePawn(left));
					state.staged.Add(DescribePawn(right));
				}));

				results.Add(RunAttackScanCase(map, root, caseIndex++, "only-colonists-skips-hostile-human", AttackMode.OnlyColonists, MakeAttackScanOrder(4, 0), spawned, (zombie, center, state) =>
				{
					var colonist = SpawnAttackScanColonist(map, center + ZombieStateHandler.AttackAdjacentOffset(0), Faction.OfPlayer, "ZL_OnlyColonist", spawned);
					var hostile = SpawnAttackScanColonist(map, center + ZombieStateHandler.AttackAdjacentOffset(4), Faction.OfAncientsHostile, "ZL_HostileHuman", spawned);
					state.expectedTarget = colonist;
					state.staged.Add(DescribePawn(colonist));
					state.staged.Add(DescribePawn(hostile));
				}));

				results.Add(RunAttackScanCase(map, root, caseIndex++, "only-humans-allows-hostile-human", AttackMode.OnlyHumans, MakeAttackScanOrder(4), spawned, (zombie, center, state) =>
				{
					var hostile = SpawnAttackScanColonist(map, center + ZombieStateHandler.AttackAdjacentOffset(4), Faction.OfAncientsHostile, "ZL_OnlyHumansHostile", spawned);
					state.expectedTarget = hostile;
					state.staged.Add(DescribePawn(hostile));
				}));

				results.Add(RunAttackScanCase(map, root, caseIndex++, "everything-allows-animal", AttackMode.Everything, MakeAttackScanOrder(4), spawned, (zombie, center, state) =>
				{
					var animal = SpawnAttackScanAnimal(map, center + ZombieStateHandler.AttackAdjacentOffset(4), "ZL_AttackAnimal", spawned);
					state.expectedTarget = animal;
					state.staged.Add(DescribePawn(animal));
				}));

				results.Add(RunAttackScanCase(map, root, caseIndex++, "special-zombieland-pawns-ignored", AttackMode.Everything, MakeAttackScanOrder(4, 0), spawned, (zombie, center, state) =>
				{
					var spitter = SpawnAttackScanSpitter(map, center + ZombieStateHandler.AttackAdjacentOffset(4), "ZL_AttackSpitter", spawned);
					var symbiant = SpawnAttackScanSymbiant(map, center + ZombieStateHandler.AttackAdjacentOffset(0), "ZL_AttackSymbiant", spawned);
					state.staged.Add(DescribeZombie(spitter));
					state.staged.Add(DescribeZombie(symbiant));
				}));

				results.Add(RunAttackScanCase(map, root, caseIndex++, "corpse-ignored-by-attack-scan", AttackMode.Everything, MakeAttackScanOrder(4), spawned, (zombie, center, state) =>
				{
					var pawn = SpawnAttackScanColonist(map, center + ZombieStateHandler.AttackAdjacentOffset(4), Faction.OfPlayer, "ZL_AttackCorpse", spawned);
					pawn.Kill(null);
					if (pawn.Corpse != null)
					{
						spawned.Add(pawn.Corpse);
						state.staged.Add(DescribeFixtureThing(pawn.Corpse));
					}
				}));

				var resultArray = results.ToArray();
				return new
				{
					success = resultArray.All(ResultSucceeded),
					root = ZombieRuntimeActions.DescribeCell(root),
					caseCount = resultArray.Length,
					index = DescribeAttackTargetIndex(map.GetComponent<ZombieAttackTargetIndex>()),
					results = resultArray
				};
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
				foreach (var thing in spawned.Where(thing => thing != null).Distinct().Reverse().ToArray())
				{
					if (thing.Destroyed || thing.Spawned == false)
						continue;
					thing.Destroy(DestroyMode.Vanish);
				}
				map.GetComponent<ZombieAttackTargetIndex>()?.Invalidate();
			}
		}

		[Tool("zombieland/attack_scan_cadence_contract", Description = "Verify idle attack-scan cadence skips no-target scans but still attacks immediately when an adjacent candidate exists.")]
		public static object AttackScanCadenceContract(
			[ToolParameter(Description = "Root x coordinate. Use -1 with z -1 to search near map center.", Required = false, DefaultValue = -1)] int x = -1,
			[ToolParameter(Description = "Root z coordinate. Use -1 with x -1 to search near map center.", Required = false, DefaultValue = -1)] int z = -1)
		{
			var map = CurrentMap;
			if (map == null)
				return new { success = false, error = "No current map is loaded." };

			var root = x >= 0 && z >= 0 ? new IntVec3(x, 0, z) : new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (root.InBounds(map) == false)
				return new { success = false, error = $"Cell ({root.x}, {root.z}) is outside the current map." };

			var settingsSnapshot = SnapshotZombieSettings();
			var spawned = new List<Thing>();
			try
			{
				ApplyZombieSettingsOverride(settings => settings.attackMode = AttackMode.Everything);
				var noTarget = RunAttackScanCadenceCase(map, root, 0, false, spawned);
				var adjacentTarget = RunAttackScanCadenceCase(map, root, 1, true, spawned);
				var results = new[] { noTarget, adjacentTarget };
				return new
				{
					success = results.All(ResultSucceeded),
					root = ZombieRuntimeActions.DescribeCell(root),
					index = DescribeAttackTargetIndex(map.GetComponent<ZombieAttackTargetIndex>()),
					results
				};
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
				foreach (var thing in spawned.Where(thing => thing != null).Distinct().Reverse().ToArray())
				{
					if (thing.Destroyed || thing.Spawned == false)
						continue;
					thing.Destroy(DestroyMode.Vanish);
				}
				map.GetComponent<ZombieAttackTargetIndex>()?.Invalidate();
			}
		}

		static object RunAttackScanCadenceCase(Map map, IntVec3 root, int caseIndex, bool adjacentTarget, List<Thing> spawned)
		{
			var requested = root + new IntVec3(caseIndex * 6, 0, 12);
			if (TryFindAttackScanCenter(map, requested, 18f, out var center, out var centerError) == false)
				return centerError;

			var zombie = ZombieRuntimeActions.SpawnZombie(center, map, ZombieType.Normal, true);
			if (zombie == null)
				return new { success = false, label = "attack-cadence", error = "Could not spawn cadence zombie.", requested = ZombieRuntimeActions.DescribeCell(requested) };
			zombie.Name = new NameSingle(adjacentTarget ? "ZL_AttackCadence_Target" : "ZL_AttackCadence_None");
			spawned.Add(zombie);

			Thing target = null;
			if (adjacentTarget)
				target = SpawnAttackScanColonist(map, center + ZombieStateHandler.AttackAdjacentOffset(4), Faction.OfPlayer, "ZL_AttackCadenceColonist", spawned);

			map.GetComponent<ZombieAttackTargetIndex>()?.Invalidate();
			zombie.state = ZombieState.Wandering;
			zombie.raging = 0;
			zombie.nextAttackScanTick = GenTicks.TicksAbs + 600;
			var tickManager = map.GetComponent<TickManager>();
			TickManager.PrepareThreadedTicking(tickManager);
			var stampedNeighbor = zombie.hasAttackCandidateNeighbor;
			var stampedTick = zombie.attackCandidateNeighborTick;
			var expectedStampedNeighbor = adjacentTarget;
			zombie.jobs.StartJob(JobMaker.MakeJob(CustomDefs.Stumble), JobCondition.InterruptForced, null, true, false, null, null);
			var driver = zombie.jobs.curDriver as JobDriver_Stumble;
			if (driver == null)
				return new { success = false, label = adjacentTarget ? "adjacent-target" : "no-target", error = "Could not start Stumble job.", zombie = DescribeZombie(zombie) };

			var attacked = driver.Attack(zombie);
			var expectedAttack = adjacentTarget;
			var currentJobDef = zombie.CurJobDef?.defName;
			var targetJobMatch = adjacentTarget == false || currentJobDef == JobDefOf.AttackMelee.defName;

			return new
			{
				success = stampedNeighbor == expectedStampedNeighbor && stampedTick == GenTicks.TicksAbs && attacked == expectedAttack && targetJobMatch,
				label = adjacentTarget ? "adjacent-target-scans-immediately" : "no-target-scan-deferred",
				expectedStampedNeighbor,
				stampedNeighbor,
				stampedTick,
				expectedAttack,
				attacked,
				targetJobMatch,
				currentJobDef,
				center = ZombieRuntimeActions.DescribeCell(center),
				zombie = DescribeZombie(zombie),
				target = DescribeAttackScanTarget(target),
				nextAttackScanTick = zombie.nextAttackScanTick
			};
		}

		static object RunAttackScanCase(Map map, IntVec3 root, int caseIndex, string label, AttackMode attackMode, int[] order, List<Thing> spawned, Action<Zombie, IntVec3, AttackScanCaseState> configure)
		{
			var requested = root + new IntVec3(caseIndex * 6, 0, 0);
			if (TryFindAttackScanCenter(map, requested, 18f, out var center, out var centerError) == false)
				return centerError;

			ApplyZombieSettingsOverride(settings => settings.attackMode = attackMode);
			var state = new AttackScanCaseState();
			var zombie = ZombieRuntimeActions.SpawnZombie(center, map, ZombieType.Normal, true);
			if (zombie == null)
				return new { success = false, label, error = "Could not spawn comparison zombie.", requested = ZombieRuntimeActions.DescribeCell(requested) };
			zombie.Name = new NameSingle($"ZL_AttackScan_{caseIndex}");
			spawned.Add(zombie);

			configure?.Invoke(zombie, center, state);
			ZombieStateHandler.SetAttackScanOrder(zombie, order);
			map.GetComponent<ZombieAttackTargetIndex>()?.Invalidate();
			var comparison = ZombieStateHandler.CompareCanAttackScans(zombie, false);
			var expectedMatches = state.expectedTarget == null
				? comparison.legacyTarget == null && comparison.indexedTarget == null
				: comparison.legacyTarget == state.expectedTarget && comparison.indexedTarget == state.expectedTarget;

			return new
			{
				success = comparison.Matches && expectedMatches,
				label,
				attackMode = attackMode.ToString(),
				center = ZombieRuntimeActions.DescribeCell(center),
				order,
				expectedTarget = DescribeAttackScanTarget(state.expectedTarget),
				expectedMatches,
				comparison = DescribeAttackScanComparison(comparison),
				staged = state.staged.ToArray()
			};
		}

		static bool TryFindAttackScanCenter(Map map, IntVec3 requested, float radius, out IntVec3 center, out object error)
		{
			center = IntVec3.Invalid;
			error = null;

			foreach (var candidate in GenRadial.RadialCellsAround(requested, radius, true))
			{
				if (IsClearAttackScanCell(map, candidate) == false)
					continue;

				var clear = true;
				for (var i = 0; i < 8; i++)
				{
					if (IsClearAttackScanCell(map, candidate + ZombieStateHandler.AttackAdjacentOffset(i)) == false)
					{
						clear = false;
						break;
					}
				}
				if (clear == false)
					continue;

				center = candidate;
				return true;
			}

			error = new
			{
				success = false,
				requested = ZombieRuntimeActions.DescribeCell(requested),
				error = "No clear 3x3 attack-scan fixture area was found."
			};
			return false;
		}

		static bool IsClearAttackScanCell(Map map, IntVec3 cell)
		{
			return cell.InBounds(map)
				&& cell.Standable(map)
				&& cell.Fogged(map) == false
				&& cell.GetThingList(map).Any(thing => thing is Pawn) == false;
		}

		static int[] MakeAttackScanOrder(params int[] first)
		{
			var order = new List<int>(8);
			foreach (var index in first)
				if (index >= 0 && index < 8 && order.Contains(index) == false)
					order.Add(index);
			for (var i = 0; i < 8; i++)
				if (order.Contains(i) == false)
					order.Add(i);
			return order.ToArray();
		}

		static Pawn SpawnAttackScanColonist(Map map, IntVec3 cell, Faction faction, string name, List<Thing> spawned)
		{
			var request = new PawnGenerationRequest(PawnKindDefOf.Colonist, faction, forceGenerateNewPawn: true, canGeneratePawnRelations: false, allowAddictions: false);
			var pawn = PawnGenerator.GeneratePawn(request);
			pawn.Name = new NameSingle(name);
			GenSpawn.Spawn(pawn, cell, map, Rot4.South);
			DisablePawnWork(pawn);
			spawned.Add(pawn);
			return pawn;
		}

		static Pawn SpawnAttackScanAnimal(Map map, IntVec3 cell, string name, List<Thing> spawned)
		{
			var kindDef = DefDatabase<PawnKindDef>.GetNamed("Warg", false)
				?? DefDatabase<PawnKindDef>.GetNamed("Husky", false)
				?? DefDatabase<PawnKindDef>.AllDefs.FirstOrDefault(def => def.RaceProps?.Animal == true && def.RaceProps.IsFlesh);
			var request = new PawnGenerationRequest(kindDef, Faction.OfPlayer, forceGenerateNewPawn: true, canGeneratePawnRelations: false, allowAddictions: false);
			var pawn = PawnGenerator.GeneratePawn(request);
			pawn.Name = new NameSingle(name);
			GenSpawn.Spawn(pawn, cell, map, Rot4.South);
			spawned.Add(pawn);
			return pawn;
		}

		static ZombieSpitter SpawnAttackScanSpitter(Map map, IntVec3 cell, string name, List<Thing> spawned)
		{
			var spitter = SpawnTargetSpitter(map, cell, name, spawned);
			spitter.Rotation = Rot4.South;
			return spitter;
		}

		static ZombieSymbiant SpawnAttackScanSymbiant(Map map, IntVec3 cell, string name, List<Thing> spawned)
		{
			var symbiant = SpawnTargetSymbiant(map, cell, name, spawned);
			symbiant.Rotation = Rot4.South;
			return symbiant;
		}

		static object DescribeAttackScanComparison(ZombieStateHandler.AttackScanComparison comparison)
		{
			return new
			{
				matches = comparison.Matches,
				zombie = DescribeZombie(comparison.zombie),
				legacyTarget = DescribeAttackScanTarget(comparison.legacyTarget),
				indexedTarget = DescribeAttackScanTarget(comparison.indexedTarget),
				legacyTargetId = ZombieRuntimeActions.StableThingId(comparison.legacyTarget),
				indexedTargetId = ZombieRuntimeActions.StableThingId(comparison.indexedTarget),
				adjacentOrder = comparison.adjacentOrder
			};
		}

		static object DescribeAttackScanTarget(Thing thing)
		{
			if (thing == null)
				return null;
			if (thing is Pawn pawn)
				return pawn is Zombie || pawn is ZombieSpitter || pawn is ZombieSymbiant ? DescribeZombie(pawn) : DescribePawn(pawn);
			return DescribeFixtureThing(thing);
		}

		static object DescribeAttackTargetIndex(ZombieAttackTargetIndex index)
		{
			if (index == null)
				return new { available = false };
			return new
			{
				available = true,
				lastBuiltTick = index.LastBuiltTick,
				rebuildCount = index.RebuildCount,
				cachedCellCount = index.CachedCellCount,
				cachedCandidateNeighborCellCount = index.CachedCandidateNeighborCellCount,
				cachedCandidateCount = index.CachedCandidateCount,
				invalidated = index.Invalidated
			};
		}

		static bool ResultSucceeded(object result)
		{
			var property = result?.GetType().GetProperty("success");
			return property?.GetValue(result) is bool success && success;
		}
	}
}
