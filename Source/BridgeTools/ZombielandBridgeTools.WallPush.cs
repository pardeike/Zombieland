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
		[Tool("zombieland/wall_push_over_wall_contract", Description = "Build a real single-wall fixture, run the real Stumble job, and verify a zombie is pushed over the wall across the source-derived progress window.")]
		public static object WallPushOverWallContract(
			[ToolParameter(Description = "Optional x cell near which the fixture should be placed. Negative values use map center.", Required = false, DefaultValue = -1)] int x = -1,
			[ToolParameter(Description = "Optional z cell near which the fixture should be placed. Negative values use map center.", Required = false, DefaultValue = -1)] int z = -1,
			[ToolParameter(Description = "Temporary minimumZombiesForWallPushing value for the contract. Defaults to the vanilla Zombieland setting.", Required = false, DefaultValue = 18)] int minimumZombies = 18)
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

			if (TryCreateWallPushFixture(map, root, 20f, out var fixture, out var error) == false)
				return error;

			var zombie = fixture.zombie;
			var wall = fixture.wall;
			var zombieCell = fixture.zombieCell;
			var wallCell = fixture.wallCell;
			var destinationCell = fixture.destinationCell;

			var grid = map.GetGrid();
			ClearWallPushGridNeighborhood(map, zombieCell);

			var sourceDerivedProgressDelta = 0.01f;
			var expectedLandingTicks = 102;
			var maxStartDelayTicks = Zombie.nthTickValues[(int)NthTick.Every8] + 1;
			var maxTicks = expectedLandingTicks + maxStartDelayTicks + 10;
			var effectiveMinimum = Math.Max(1, minimumZombies);
			var primedGridCount = Math.Max(0, effectiveMinimum - 4);
			grid.ChangeZombieCount(zombieCell, primedGridCount);

			var roofBefore = map.roofGrid.RoofAt(destinationCell);
			map.roofGrid.SetRoof(destinationCell, RoofDefOf.RoofConstructed);
			var roofAfterSetup = map.roofGrid.RoofAt(destinationCell);
			var originalMinimum = ZombieSettings.Values.minimumZombiesForWallPushing;
			var originalDangerousSituationMessage = ZombieSettings.Values.dangerousSituationMessage;

			PrepareWallPushZombie(map, zombie, zombieCell);

			object CaptureSample(int tick)
			{
				return new
				{
					tick,
					gameTick = Find.TickManager.TicksGame,
					absoluteTick = GenTicks.TicksAbs,
					position = ZombieRuntimeActions.DescribeCell(zombie.Position),
					drawPos = DescribeVector(zombie.DrawPos),
					progress = zombie.wallPushProgress,
					pushStart = DescribeVector(zombie.wallPushStart),
					pushDestination = DescribeVector(zombie.wallPushDestination),
					wallPushCooldown = zombie.wallPushCooldown,
					rotation = zombie.Rotation.AsInt,
					currentJob = zombie.CurJobDef?.defName,
					spawned = zombie.Spawned,
					dead = zombie.Dead
				};
			}

			var samples = new List<object>();
			var startedPush = false;
			var startTick = -1;
			var landed = false;
			var landingTick = -1;
			try
			{
				ZombieSettings.Values.minimumZombiesForWallPushing = effectiveMinimum;
				ZombieSettings.Values.dangerousSituationMessage = false;
				zombie.jobs.StartJob(JobMaker.MakeJob(CustomDefs.Stumble), JobCondition.InterruptForced, null, true, false, null, null);
				samples.Add(CaptureSample(0));

				for (var tick = 1; tick <= maxTicks; tick++)
				{
					AdvanceGameTicks(1);
					if (zombie.wallPushProgress >= 0f)
					{
						startedPush = true;
						if (startTick < 0)
							startTick = tick;
					}
					if (tick == 1 || tick % 10 == 0 || zombie.Position == destinationCell || zombie.wallPushProgress < 0f && startedPush)
						samples.Add(CaptureSample(tick));
					if (zombie.Position == destinationCell && zombie.wallPushProgress < 0f)
					{
						landed = true;
						landingTick = tick;
						break;
					}
				}
			}
			finally
			{
				ZombieSettings.Values.minimumZombiesForWallPushing = originalMinimum;
				ZombieSettings.Values.dangerousSituationMessage = originalDangerousSituationMessage;
			}

			var roofAfterLanding = map.roofGrid.RoofAt(destinationCell);
			var wallDestroyed = wall.Destroyed;
			var wallAtCellAfter = wallCell.GetEdifice(map);
			var gridCountAtZombieCell = grid.GetZombieCount(zombieCell);
			var gridCountAtDestinationCell = grid.GetZombieCount(destinationCell);
			var wallStillPresent = wallDestroyed == false && wallAtCellAfter == wall;
			var roofCleared = roofAfterLanding == null;

			return new
			{
				success = startedPush
					&& landed
					&& zombie.Position == destinationCell
					&& wallStillPresent
					&& roofCleared,
				sourceDerivedProgressDelta,
				expectedLandingTicks,
				maxStartDelayTicks,
				maxTicks,
				startedPush,
				startTick,
				landed,
				landingTick,
				landingTicksAfterStart = startTick < 0 || landingTick < 0 ? -1 : landingTick - startTick + 1,
				effectiveMinimum,
				primedGridCount,
				gridCountAtZombieCell,
				gridCountAtDestinationCell,
				root = ZombieRuntimeActions.DescribeCell(root),
				zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
				wallCell = ZombieRuntimeActions.DescribeCell(wallCell),
				destinationCell = ZombieRuntimeActions.DescribeCell(destinationCell),
				wallId = ZombieRuntimeActions.StableThingId(wall),
				wallDestroyed,
				wallStillPresent,
				wallAtCellAfter = wallAtCellAfter?.def?.defName,
				roofBefore = roofBefore?.defName,
				roofAfterSetup = roofAfterSetup?.defName,
				roofAfterLanding = roofAfterLanding?.defName,
				roofCleared,
				zombie = DescribeZombie(zombie),
				samples
			};
		}

		[Tool("zombieland/wall_push_gate_contract", Description = "Verify the source-derived wall-push rejection gates with real one-tick Stumble job samples.")]
		public static object WallPushGateContract(
			[ToolParameter(Description = "Temporary minimumZombiesForWallPushing value for the contract. Defaults to the vanilla Zombieland setting.", Required = false, DefaultValue = 18)] int minimumZombies = 18)
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
			var effectiveMinimum = Math.Max(1, minimumZombies);
			var enoughWithSingleWall = Math.Max(0, effectiveMinimum - 4);
			var belowThreshold = Math.Max(0, enoughWithSingleWall - 1);
			var originalMinimum = ZombieSettings.Values.minimumZombiesForWallPushing;
			var originalDangerousSituationMessage = ZombieSettings.Values.dangerousSituationMessage;
			var caseIndex = 0;
			var allCasesSucceeded = true;

			object RunCase(string name, int settingMinimum, int primedGridCount, Action<WallPushFixture> mutate)
			{
				var caseRoot = root + new IntVec3((caseIndex % 4 - 1) * 12, 0, (caseIndex / 4 - 1) * 12);
				caseIndex++;
				if (caseRoot.InBounds(map) == false)
					caseRoot = root;

				if (TryCreateWallPushFixture(map, caseRoot, 10f, out var fixture, out var setupError) == false)
				{
					allCasesSucceeded = false;
					return new
					{
						name,
						success = false,
						setupError
					};
				}

				var zombie = fixture.zombie;
				var grid = map.GetGrid();
				ClearWallPushGridNeighborhood(map, fixture.zombieCell);
				grid.ChangeZombieCount(fixture.zombieCell, Math.Max(0, primedGridCount));
				map.roofGrid.SetRoof(fixture.destinationCell, null);
				PrepareWallPushZombie(map, zombie, fixture.zombieCell);

				mutate?.Invoke(fixture);

				var before = new
				{
					position = ZombieRuntimeActions.DescribeCell(zombie.Position),
					progress = zombie.wallPushProgress,
					cooldown = zombie.wallPushCooldown,
					gridAtZombie = grid.GetZombieCount(fixture.zombieCell),
					gridAtDestination = grid.GetZombieCount(fixture.destinationCell),
					roofAtDestination = map.roofGrid.RoofAt(fixture.destinationCell)?.defName,
					wallCount = new[] { IntVec3.East, IntVec3.West, IntVec3.North, IntVec3.South }
						.Count(direction =>
						{
							var adjacent = fixture.zombieCell + direction;
							return adjacent.InBounds(map) && adjacent.IsWallOrDoor(map);
						}),
					destinationWalkable = fixture.destinationCell.WalkableBy(map, zombie),
					destinationCachedZombie = map.GetComponent<TickManager>()?.allZombiesCached?.Any(cached => cached.Position == fixture.destinationCell) ?? false
				};

				try
				{
					ZombieSettings.Values.minimumZombiesForWallPushing = settingMinimum;
					ZombieSettings.Values.dangerousSituationMessage = false;
					zombie.jobs.StartJob(JobMaker.MakeJob(CustomDefs.Stumble), JobCondition.InterruptForced, null, true, false, null, null);
					AdvanceGameTicks(1);
				}
				finally
				{
					ZombieSettings.Values.minimumZombiesForWallPushing = originalMinimum;
					ZombieSettings.Values.dangerousSituationMessage = originalDangerousSituationMessage;
				}

				var stayedOutOfPush = zombie.wallPushProgress < 0f;
				allCasesSucceeded &= stayedOutOfPush;
				return new
				{
					name,
					success = stayedOutOfPush,
					settingMinimum,
					primedGridCount,
					stayedOutOfPush,
					before,
					after = new
					{
						position = ZombieRuntimeActions.DescribeCell(zombie.Position),
						drawPos = DescribeVector(zombie.DrawPos),
						progress = zombie.wallPushProgress,
						pushStart = DescribeVector(zombie.wallPushStart),
						pushDestination = DescribeVector(zombie.wallPushDestination),
						cooldown = zombie.wallPushCooldown,
						currentJob = zombie.CurJobDef?.defName,
						roofAtDestination = map.roofGrid.RoofAt(fixture.destinationCell)?.defName
					},
					zombieCell = ZombieRuntimeActions.DescribeCell(fixture.zombieCell),
					wallCell = ZombieRuntimeActions.DescribeCell(fixture.wallCell),
					destinationCell = ZombieRuntimeActions.DescribeCell(fixture.destinationCell)
				};
			}

			IntVec3 ExtraAdjacentCell(WallPushFixture fixture)
			{
				foreach (var direction in new[] { IntVec3.North, IntVec3.South, IntVec3.East, IntVec3.West })
				{
					var cell = fixture.zombieCell + direction;
					if (cell == fixture.wallCell || cell.InBounds(map) == false || cell.Fogged(map))
						continue;
					if (cell.GetEdifice(map) != null || cell.GetFirstThing<Mineable>(map) != null)
						continue;
					if (cell.GetThingList(map).Any(thing => thing is Pawn))
						continue;
					return cell;
				}
				return IntVec3.Invalid;
			}

			var cases = new[]
			{
				RunCase("settingDisabled", 0, effectiveMinimum + 8, null),
				RunCase("belowThreshold", effectiveMinimum, belowThreshold, null),
				RunCase("noAdjacentWall", effectiveMinimum, effectiveMinimum, fixture => fixture.wall.Destroy(DestroyMode.Vanish)),
				RunCase("multipleAdjacentWalls", effectiveMinimum, effectiveMinimum, fixture =>
				{
					var extraWallCell = ExtraAdjacentCell(fixture);
					if (extraWallCell.IsValid)
						_ = SpawnWoodWall(map, extraWallCell);
				}),
				RunCase("blockedDestination", effectiveMinimum, enoughWithSingleWall, fixture => _ = SpawnWoodWall(map, fixture.destinationCell)),
				RunCase("rockRoofDestination", effectiveMinimum, enoughWithSingleWall, fixture => map.roofGrid.SetRoof(fixture.destinationCell, RoofDefOf.RoofRockThick)),
				RunCase("occupiedDestination", effectiveMinimum, enoughWithSingleWall, fixture =>
				{
					var blocker = ZombieRuntimeActions.SpawnZombie(fixture.destinationCell, map, ZombieType.Normal, true);
					if (blocker != null)
						_ = map.GetComponent<TickManager>()?.allZombiesCached?.Add(blocker);
				}),
				RunCase("cooldownActive", effectiveMinimum, enoughWithSingleWall, fixture => fixture.zombie.wallPushCooldown = GenTicks.TicksAbs + 100)
			};

			return new
			{
				success = allCasesSucceeded,
				effectiveMinimum,
				enoughWithSingleWall,
				belowThreshold,
				sourceGates = new[]
				{
					"minimumZombiesForWallPushing == 0",
					"totalZombies < minimum",
					"wallCount != 1",
					"destination.WalkableBy == false",
					"rock roof at destination",
					"cached zombie at destination",
					"wallPushCooldown active"
				},
				cases
			};
		}

		[Tool("zombieland/wall_push_warning_letter_contract", Description = "Verify wall-push warning letters fire only for home-area walls after a real Stumble wall-push start.")]
		public static object WallPushWarningLetterContract(
			[ToolParameter(Description = "Temporary minimumZombiesForWallPushing value for the contract. Defaults to the vanilla Zombieland setting.", Required = false, DefaultValue = 18)] int minimumZombies = 18)
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
			var effectiveMinimum = Math.Max(1, minimumZombies);
			var primedGridCount = Math.Max(0, effectiveMinimum - 4);
			var originalMinimum = ZombieSettings.Values.minimumZombiesForWallPushing;
			var originalDangerousSituationMessage = ZombieSettings.Values.dangerousSituationMessage;
			var allCasesSucceeded = true;

			int DangerousLetterCount()
			{
				return Find.LetterStack?.LettersListForReading?
					.Count(letter => letter?.def == CustomDefs.DangerousSituation) ?? 0;
			}

			object[] DangerousLetters()
			{
				return Find.LetterStack?.LettersListForReading?
					.Where(letter => letter?.def == CustomDefs.DangerousSituation)
					.Select(letter => new
					{
						label = letter.Label,
						defName = letter.def?.defName,
						arrivalTick = letter.arrivalTick
					})
					.Cast<object>()
					.ToArray() ?? Array.Empty<object>();
			}

			object RunCase(string name, IntVec3 caseRoot, bool homeWall)
			{
				if (TryCreateWallPushFixture(map, caseRoot, 10f, out var fixture, out var setupError) == false)
				{
					allCasesSucceeded = false;
					return new
					{
						name,
						success = false,
						setupError
					};
				}

				var zombie = fixture.zombie;
				var grid = map.GetGrid();
				ClearWallPushGridNeighborhood(map, fixture.zombieCell);
				grid.ChangeZombieCount(fixture.zombieCell, primedGridCount);
				map.roofGrid.SetRoof(fixture.destinationCell, null);
				PrepareWallPushZombie(map, zombie, fixture.zombieCell);

				var originalHome = map.areaManager.Home[fixture.wallCell];
				var beforeLetters = DangerousLetterCount();
				var startedPush = false;
				var startTick = -1;
				try
				{
					map.areaManager.Home[fixture.wallCell] = homeWall;
					ClearThrottleKey("DangerousSituation");
					ZombieSettings.Values.minimumZombiesForWallPushing = effectiveMinimum;
					ZombieSettings.Values.dangerousSituationMessage = true;
					zombie.jobs.StartJob(JobMaker.MakeJob(CustomDefs.Stumble), JobCondition.InterruptForced, null, true, false, null, null);
					var maxStartDelayTicks = Zombie.nthTickValues[(int)NthTick.Every8] + 1;
					for (var tick = 1; tick <= maxStartDelayTicks; tick++)
					{
						AdvanceGameTicks(1);
						startedPush = zombie.wallPushProgress >= 0f;
						if (startedPush)
						{
							startTick = tick;
							break;
						}
					}
				}
				finally
				{
					map.areaManager.Home[fixture.wallCell] = originalHome;
					ZombieSettings.Values.minimumZombiesForWallPushing = originalMinimum;
					ZombieSettings.Values.dangerousSituationMessage = originalDangerousSituationMessage;
				}

				var afterLetters = DangerousLetterCount();
				var letterDelta = afterLetters - beforeLetters;
				var expectedDelta = homeWall ? 1 : 0;
				var success = startedPush && letterDelta == expectedDelta;
				allCasesSucceeded &= success;
				return new
				{
					name,
					success,
					homeWall,
					startedPush,
					startTick,
					beforeLetters,
					afterLetters,
					letterDelta,
					expectedDelta,
					zombieCell = ZombieRuntimeActions.DescribeCell(fixture.zombieCell),
					wallCell = ZombieRuntimeActions.DescribeCell(fixture.wallCell),
					destinationCell = ZombieRuntimeActions.DescribeCell(fixture.destinationCell),
					progress = zombie.wallPushProgress,
					letters = DangerousLetters()
				};
			}

			var outsideHome = RunCase("outsideHomeWall", root + new IntVec3(-36, 0, -24), false);
			var insideHome = RunCase("insideHomeWall", root + new IntVec3(36, 0, -24), true);
			var caseResults = new[] { outsideHome, insideHome };

			return new
			{
				success = allCasesSucceeded,
				effectiveMinimum,
				primedGridCount,
				sourcePath = "ZombieStateHandler.CheckWallPushing -> dangerousSituationMessage && Home[wallCell] -> DangerousSituation letter",
				cases = caseResults
			};
		}

	}
}
