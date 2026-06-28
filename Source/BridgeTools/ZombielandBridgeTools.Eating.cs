using HarmonyLib;
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
		static void ClearEatingGridNeighborhood(Map map, params IntVec3[] centers)
		{
			var grid = map.GetGrid();
			var cells = new HashSet<IntVec3>();
			foreach (var center in centers)
			{
				if (center.IsValid == false)
					continue;
				foreach (var cell in GenRadial.RadialCellsAround(center, 4f, true))
				{
					if (cell.InBounds(map) == false || cells.Add(cell) == false)
						continue;
					grid.SetTimestamp(cell, 0);
					var count = grid.GetZombieCount(cell);
					if (count != 0)
						grid.ChangeZombieCount(cell, -count);
				}
			}
		}

		static object DescribeEatingGrid(Map map, IntVec3 zombieCell, IntVec3 targetCell)
		{
			var grid = map.GetGrid();
			return new
			{
				zombieCountAtZombie = grid.GetZombieCount(zombieCell),
				zombieCountAtTarget = grid.GetZombieCount(targetCell),
				pheromoneAtZombie = grid.GetTimestamp(zombieCell),
				pheromoneAtTarget = grid.GetTimestamp(targetCell)
			};
		}

		static PawnKindDef FindNonFleshPawnKind()
		{
			return DefDatabase<PawnKindDef>.AllDefsListForReading
				.Where(kind => kind?.race?.race != null && kind.race.race.IsFlesh == false)
				.OrderByDescending(kind => kind.race.race.IsMechanoid)
				.ThenBy(kind => kind.defName)
				.FirstOrDefault();
		}

		static PawnKindDef FindPawnKind(string defName)
		{
			return DefDatabase<PawnKindDef>.GetNamed(defName, false);
		}

		static object SkippedOptionalPawnKindCase(string name, string targetKind, string reason)
		{
			return new
			{
				name,
				success = true,
				skipped = true,
				targetKind,
				reason
			};
		}

		static bool TryFindEatingFixtureCells(Map map, IntVec3 rootCell, string targetLabel, out IntVec3 zombieCell, out IntVec3 targetCell, out object error)
		{
			const float searchRadius = 70f;
			const float isolationRadius = 32f;

			zombieCell = IntVec3.Invalid;
			targetCell = IntVec3.Invalid;
			error = null;

			bool HasPawnOrCorpse(IntVec3 cell)
				=> cell.GetThingList(map).Any(thing => thing is Pawn || thing is Corpse);

			bool IsIsolated(IntVec3 center)
			{
				foreach (var cell in GenRadial.RadialCellsAround(center, isolationRadius, true))
				{
					if (cell.InBounds(map) == false)
						continue;
					if (HasPawnOrCorpse(cell))
						return false;
				}
				return true;
			}

			foreach (var candidate in GenRadial.RadialCellsAround(rootCell, searchRadius, true))
			{
				if (candidate.InBounds(map) == false || candidate.Standable(map) == false || candidate.Fogged(map))
					continue;
				if (IsIsolated(candidate) == false)
					continue;
				foreach (var offset in GenAdj.AdjacentCellsAround)
				{
					var adjacent = candidate + offset;
					if (adjacent.InBounds(map) == false || adjacent.Standable(map) == false || adjacent.Fogged(map))
						continue;
					if (HasPawnOrCorpse(adjacent))
						continue;
					zombieCell = candidate;
					targetCell = adjacent;
					return true;
				}
			}

			error = new
			{
				success = false,
				error = $"No isolated adjacent zombie/{targetLabel} eating fixture cells were found near ({rootCell.x}, {rootCell.z}).",
				searchRadius,
				isolationRadius
			};
			return false;
		}

		[Tool("zombieland/zombie_eats_corpse_contract", Description = "Run a real Stumble job beside a flesh corpse and verify the source-derived eating delay removes a body part only when corpse eating is enabled.")]
		public static object ZombieEatsCorpseContract()
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
			var settingsSnapshot = SnapshotZombieSettings();
			var allCasesSucceeded = true;

			static int MissingPartCount(Pawn pawn)
				=> pawn?.health?.hediffSet?.hediffs?.OfType<Hediff_MissingPart>().Count() ?? 0;

			object RunCase(string name, bool eatCorpses, bool expectEating, PawnKindDef targetKind, Faction targetFaction, IntVec3 caseRoot, bool fullNegativeWindow = false)
			{
				if (TryFindEatingFixtureCells(map, caseRoot, "corpse", out var zombieCell, out var corpseCell, out var error) == false)
				{
					allCasesSucceeded = false;
					return new { name, success = false, error };
				}

				ApplyZombieSettingsOverride(settings =>
				{
					settings.zombiesEatDowned = false;
					settings.zombiesEatCorpses = eatCorpses;
					settings.attackMode = AttackMode.OnlyColonists;
				});

				var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
				if (zombie == null)
				{
					allCasesSucceeded = false;
					return new
					{
						name,
						success = false,
						error = "ZombieGenerator.SpawnZombie returned no eating test zombie.",
						zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell)
					};
				}

				zombie.story.bodyType = BodyTypeDefOf.Fat;
				zombie.pather?.StopDead();
				zombie.jobs?.EndCurrentJob(JobCondition.InterruptForced);
				zombie.state = ZombieState.Wandering;
				zombie.raging = 0;

				if (targetKind == null)
				{
					allCasesSucceeded = false;
					return new
					{
						name,
						success = false,
						error = "No target pawn kind was available for corpse-eating test."
					};
				}

				var corpsePawn = PawnGenerator.GeneratePawn(targetKind, targetFaction);
				GenSpawn.Spawn(corpsePawn, corpseCell, map, WipeMode.Vanish);
				DisablePawnWork(corpsePawn);
				if (ZombieRuntimeActions.KillPawnToCorpse(corpsePawn, out var corpse, out var corpseError) == false)
				{
					allCasesSucceeded = false;
					return new
					{
						name,
						success = false,
						zombie = DescribeZombie(zombie),
						corpsePawn = DescribePawn(corpsePawn),
						error = corpseError
					};
				}
				corpse.SetForbidden(true, false);
				ClearEatingGridNeighborhood(map, zombieCell, corpseCell);

				var missingBefore = MissingPartCount(corpse.InnerPawn);
				var eatDelayTicks = Constants.EAT_DELAY_TICKS / 4;
				var maxTicks = expectEating || fullNegativeWindow ? eatDelayTicks + 8 : 8;
				var tickHit = -1;
				var samples = new List<object>();
				var stumbleJob = JobMaker.MakeJob(CustomDefs.Stumble);
				stumbleJob.playerForced = true;
				zombie.jobs.StartJob(stumbleJob, JobCondition.InterruptForced, null, true, false, null, null);

				for (var tick = 1; tick <= maxTicks; tick++)
				{
					AdvanceGameTicks(1);
					var driver = zombie.jobs?.curDriver as JobDriver_Stumble;
					var missingNow = MissingPartCount(corpse.InnerPawn);
					if (tick == 1 || tick == maxTicks || tick % 60 == 0 || missingNow > missingBefore)
					{
						samples.Add(new
						{
							tick,
							zombieJob = zombie.CurJobDef?.defName,
							eatTarget = driver?.eatTarget == null ? null : ZombieRuntimeActions.StableThingId(driver.eatTarget),
							eatDelayCounter = driver?.eatDelayCounter ?? -1,
							eatDelay = driver?.eatDelay ?? -1,
							destination = ZombieRuntimeActions.DescribeCell(driver?.destination ?? IntVec3.Invalid),
							zombieState = zombie.state.ToString(),
							missingParts = missingNow,
							corpsePosition = ZombieRuntimeActions.DescribeCell(corpse.Position),
							corpseSpawned = corpse.Spawned,
							corpseDestroyed = corpse.Destroyed,
							grid = DescribeEatingGrid(map, zombieCell, corpseCell)
						});
					}

					if (missingNow > missingBefore)
					{
						tickHit = tick;
						break;
					}
				}

				var driverAfter = zombie.jobs?.curDriver as JobDriver_Stumble;
				var missingAfter = MissingPartCount(corpse.InnerPawn);
				var success = expectEating
					? tickHit > 0 && missingAfter > missingBefore && ReferenceEquals(driverAfter?.eatTarget, corpse)
					: tickHit == -1 && missingAfter == missingBefore && driverAfter?.eatTarget == null;
				allCasesSucceeded &= success;

				var result = new
				{
					name,
					success,
					eatCorpses,
					expectEating,
					sourcePath = "JobDriver_Stumble.TickAction -> ZombieStateHandler.Eat -> CanIngest -> EatDelay -> EatBodyPart",
					sourceDerivedTicks = new
					{
						baseEatDelay = Constants.EAT_DELAY_TICKS,
						bodyType = zombie.story.bodyType?.defName,
						eatDelayTicks,
						maxTicks
					},
					zombie = DescribeZombie(zombie),
					corpse = new
					{
						id = ZombieRuntimeActions.StableThingId(corpse),
						cell = ZombieRuntimeActions.DescribeCell(corpse.Position),
						spawned = corpse.Spawned,
						destroyed = corpse.Destroyed,
						innerPawn = DescribePawn(corpse.InnerPawn)
					},
					targetKind = targetKind.defName,
					targetHumanlike = corpse.InnerPawn.RaceProps.Humanlike,
					targetFlesh = corpse.InnerPawn.RaceProps.IsFlesh,
					targetAlienFlesh = AlienTools.IsFleshPawn(corpse.InnerPawn),
					cells = new
					{
						zombie = ZombieRuntimeActions.DescribeCell(zombieCell),
						corpse = ZombieRuntimeActions.DescribeCell(corpseCell)
					},
					missingBefore,
					missingAfter,
					missingDelta = missingAfter - missingBefore,
					tickHit,
					finalEatTarget = driverAfter?.eatTarget == null ? null : ZombieRuntimeActions.StableThingId(driverAfter.eatTarget),
					finalEatDelayCounter = driverAfter?.eatDelayCounter ?? -1,
					samples
				};

				if (zombie.Destroyed == false)
					zombie.Destroy();
				if (corpse.Destroyed == false)
					corpse.Destroy();
				return result;
			}

			try
			{
				var animalKind = DefDatabase<PawnKindDef>.GetNamed("Muffalo", false);
				var nonFleshKind = FindNonFleshPawnKind();
				var harNonFleshKind = FindPawnKind("ZLTestNonFleshAlienKind");
				var disabledCase = RunCase("corpseEatingDisabled", false, false, PawnKindDefOf.Colonist, Faction.OfPlayer, root + new IntVec3(-8, 0, 8));
				var enabledHumanCase = RunCase("corpseEatingEnabledHuman", true, true, PawnKindDefOf.Colonist, Faction.OfPlayer, root + new IntVec3(8, 0, 8));
				var enabledAnimalCase = RunCase("corpseEatingEnabledAnimal", true, true, animalKind, null, root + new IntVec3(8, 0, -8));
				var nonFleshCase = RunCase("corpseEatingRejectsNonFlesh", true, false, nonFleshKind, null, root + new IntVec3(-16, 0, 8), true);
				var harNonFleshCase = harNonFleshKind == null
					? SkippedOptionalPawnKindCase("corpseEatingRejectsHarNonFleshAlien", "ZLTestNonFleshAlienKind", "Optional local ZLTestAlienRace fixture is not loaded.")
					: RunCase("corpseEatingRejectsHarNonFleshAlien", true, false, harNonFleshKind, null, root + new IntVec3(-16, 0, -16), true);
				return new
				{
					success = allCasesSucceeded,
					destroyedZombies,
					cases = new[] { disabledCase, enabledHumanCase, enabledAnimalCase, nonFleshCase, harNonFleshCase }
				};
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
			}
		}

		[Tool("zombieland/zombie_eats_downed_pawn_contract", Description = "Run a real Stumble job beside downed flesh creatures and verify the source-derived eating delay removes a body part only when downed-pawn eating is enabled.")]
		public static object ZombieEatsDownedPawnContract()
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
			var settingsSnapshot = SnapshotZombieSettings();
			var allCasesSucceeded = true;

			static int MissingPartCount(Pawn pawn)
				=> pawn?.health?.hediffSet?.hediffs?.OfType<Hediff_MissingPart>().Count() ?? 0;

			static bool TryMakeDowned(Pawn pawn, out string error)
			{
				error = null;
				if (pawn.RaceProps.IsFlesh)
				{
					var bloodLoss = HediffMaker.MakeHediff(HediffDefOf.BloodLoss, pawn);
					bloodLoss.Severity = 0.45f;
					pawn.health.hediffSet.AddDirect(bloodLoss);
				}
				var anesthetic = HediffMaker.MakeHediff(HediffDefOf.Anesthetic, pawn);
				anesthetic.Severity = 1f;
				pawn.health.hediffSet.AddDirect(anesthetic);

				var makeDowned = AccessTools.Method(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.MakeDowned));
				if (makeDowned == null)
				{
					error = "Could not reflect Pawn_HealthTracker.MakeDowned.";
					return false;
				}
				makeDowned.Invoke(pawn.health, new object[makeDowned.GetParameters().Length]);
				if (pawn.health.Downed == false)
				{
					error = "Pawn_HealthTracker.MakeDowned did not leave the pawn downed.";
					return false;
				}
				return true;
			}

			object RunCase(string name, bool eatDowned, bool expectEating, PawnKindDef targetKind, IntVec3 caseRoot)
			{
				if (TryFindEatingFixtureCells(map, caseRoot, "downed-pawn", out var zombieCell, out var targetCell, out var error) == false)
				{
					allCasesSucceeded = false;
					return new { name, success = false, error };
				}

				if (targetKind == null)
				{
					allCasesSucceeded = false;
					return new
					{
						name,
						success = false,
						error = "No target pawn kind was available for downed-pawn eating test."
					};
				}

				ApplyZombieSettingsOverride(settings =>
				{
					settings.zombiesEatDowned = eatDowned;
					settings.zombiesEatCorpses = false;
					settings.attackMode = AttackMode.OnlyColonists;
				});

				var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
				if (zombie == null)
				{
					allCasesSucceeded = false;
					return new
					{
						name,
						success = false,
						error = "ZombieGenerator.SpawnZombie returned no downed-pawn eating test zombie.",
						zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell)
					};
				}

				zombie.story.bodyType = BodyTypeDefOf.Fat;
				zombie.pather?.StopDead();
				zombie.jobs?.EndCurrentJob(JobCondition.InterruptForced);
				zombie.state = ZombieState.Wandering;
				zombie.raging = 0;

				var target = PawnGenerator.GeneratePawn(targetKind, null);
				GenSpawn.Spawn(target, targetCell, map, WipeMode.Vanish);
				DisablePawnWork(target);
				if (TryMakeDowned(target, out var downedError) == false)
				{
					allCasesSucceeded = false;
					return new
					{
						name,
						success = false,
						zombie = DescribeZombie(zombie),
						target = DescribePawn(target),
						error = downedError
					};
				}
				ClearEatingGridNeighborhood(map, zombieCell, targetCell);

				var missingBefore = MissingPartCount(target);
				var eatDelayTicks = Constants.EAT_DELAY_TICKS / 4;
				var maxTicks = expectEating ? eatDelayTicks + 8 : 8;
				var tickHit = -1;
				var samples = new List<object>();
				var stumbleJob = JobMaker.MakeJob(CustomDefs.Stumble);
				stumbleJob.playerForced = true;
				zombie.jobs.StartJob(stumbleJob, JobCondition.InterruptForced, null, true, false, null, null);

				for (var tick = 1; tick <= maxTicks; tick++)
				{
					AdvanceGameTicks(1);
					var driver = zombie.jobs?.curDriver as JobDriver_Stumble;
					var missingNow = MissingPartCount(target);
					if (tick == 1 || tick == maxTicks || tick % 60 == 0 || missingNow > missingBefore || target.Dead)
					{
						samples.Add(new
						{
							tick,
							zombieJob = zombie.CurJobDef?.defName,
							eatTarget = driver?.eatTarget == null ? null : ZombieRuntimeActions.StableThingId(driver.eatTarget),
							eatDelayCounter = driver?.eatDelayCounter ?? -1,
							eatDelay = driver?.eatDelay ?? -1,
							lastEatTargetPosition = ZombieRuntimeActions.DescribeCell(driver?.lastEatTargetPosition ?? IntVec3.Invalid),
							destination = ZombieRuntimeActions.DescribeCell(driver?.destination ?? IntVec3.Invalid),
							zombieState = zombie.state.ToString(),
							targetDowned = target.health?.Downed ?? false,
							targetDead = target.Dead,
							targetSpawned = target.Spawned,
							targetCell = ZombieRuntimeActions.DescribeCell(target.Position),
							missingParts = missingNow,
							grid = DescribeEatingGrid(map, zombieCell, targetCell)
						});
					}

					if (missingNow > missingBefore)
					{
						tickHit = tick;
						break;
					}
				}

				var driverAfter = zombie.jobs?.curDriver as JobDriver_Stumble;
				var missingAfter = MissingPartCount(target);
				var success = expectEating
					? tickHit > 0 && missingAfter > missingBefore && ReferenceEquals(driverAfter?.eatTarget, target)
					: tickHit == -1 && missingAfter == missingBefore && driverAfter?.eatTarget == null;
				allCasesSucceeded &= success;

				var result = new
				{
					name,
					success,
					eatDowned,
					expectEating,
					attackMode = ZombieSettings.Values.attackMode.ToString(),
					sourcePath = "JobDriver_Stumble.TickAction -> ZombieStateHandler.Eat -> CanIngest(downed pawn) -> EatDelay -> EatBodyPart",
					sourceDerivedTicks = new
					{
						baseEatDelay = Constants.EAT_DELAY_TICKS,
						bodyType = zombie.story.bodyType?.defName,
						eatDelayTicks,
						maxTicks
					},
					zombie = DescribeZombie(zombie),
					target = DescribePawn(target),
					cells = new
					{
						zombie = ZombieRuntimeActions.DescribeCell(zombieCell),
						target = ZombieRuntimeActions.DescribeCell(targetCell)
					},
					targetKind = targetKind.defName,
					targetIsColonist = target.IsColonist,
					targetHumanlike = target.RaceProps.Humanlike,
					targetFlesh = target.RaceProps.IsFlesh,
					targetAlienFlesh = AlienTools.IsFleshPawn(target),
					targetDowned = target.health?.Downed ?? false,
					targetDead = target.Dead,
					missingBefore,
					missingAfter,
					missingDelta = missingAfter - missingBefore,
					tickHit,
					finalEatTarget = driverAfter?.eatTarget == null ? null : ZombieRuntimeActions.StableThingId(driverAfter.eatTarget),
					finalEatDelayCounter = driverAfter?.eatDelayCounter ?? -1,
					samples
				};

				if (zombie.Destroyed == false)
					zombie.Destroy();
				if (target.Corpse is { Destroyed: false } corpse)
					corpse.Destroy();
				if (target.Destroyed == false)
					target.Destroy();
				return result;
			}

			static object DescribeControlledDrop(Thing thing)
			{
				if (thing == null)
					return null;
				var apparel = thing as Apparel;
				return new
				{
					id = ZombieRuntimeActions.StableThingId(thing),
					thingId = thing.ThingID,
					defName = thing.def?.defName,
					label = thing.LabelShort,
					spawned = thing.Spawned,
					destroyed = thing.Destroyed,
					forbidden = thing.Spawned && thing.IsForbidden(Faction.OfPlayer),
					position = thing.Spawned ? ZombieRuntimeActions.DescribeCell(thing.Position) : null,
					stackCount = thing.stackCount,
					parentHolder = thing.ParentHolder?.GetType().FullName,
					apparel = apparel == null ? null : new
					{
						wearer = apparel.Wearer == null ? null : ZombieRuntimeActions.StableThingId(apparel.Wearer),
						wornByCorpse = apparel.WornByCorpse
					}
				};
			}

			static bool TryStageControlledEatingDeathGear(Pawn target, out Thing[] controlledGear, out object evidence, out string error)
			{
				controlledGear = Array.Empty<Thing>();
				evidence = null;
				error = null;
				if (target?.equipment == null || target.apparel == null || target.inventory?.innerContainer == null)
				{
					error = "Target pawn is missing equipment, apparel, or inventory trackers.";
					return false;
				}

				target.equipment.DestroyAllEquipment(DestroyMode.Vanish);
				target.apparel.DestroyAll();
				target.inventory.innerContainer.ClearAndDestroyContents(DestroyMode.Vanish);

				var preferredApparelDefs = new[] { "Apparel_CowboyHat", "Apparel_Tuque", "Apparel_SimpleHelmet", "Apparel_FlakHelmet" };
				var apparelDef = preferredApparelDefs
					.Select(defName => DefDatabase<ThingDef>.GetNamed(defName, false))
					.FirstOrDefault(def => def?.apparel != null && ApparelUtility.HasPartsToWear(target, def))
					?? DefDatabase<ThingDef>.AllDefsListForReading
						.Where(def => def?.apparel != null && ApparelUtility.HasPartsToWear(target, def))
						.OrderBy(def => def.defName)
						.FirstOrDefault();
				var apparelStuff = apparelDef == null || apparelDef.MadeFromStuff == false ? null : GenStuff.DefaultStuffFor(apparelDef);
				var apparel = apparelDef == null ? null : ThingMaker.MakeThing(apparelDef, apparelStuff) as Apparel;
				if (apparel == null)
				{
					error = "No controlled apparel def was available for the downed-pawn eating forbidden-drop probe.";
					return false;
				}
				target.apparel.Wear(apparel, false);
				if (target.apparel.WornApparel.Contains(apparel) == false)
				{
					apparel.Destroy(DestroyMode.Vanish);
					error = "Target pawn rejected the controlled apparel.";
					return false;
				}

				var weaponDef = DefDatabase<ThingDef>.GetNamed("Gun_BoltActionRifle", false)
					?? DefDatabase<ThingDef>.GetNamed("Gun_Pistol", false);
				var weapon = weaponDef == null ? null : ThingMaker.MakeThing(weaponDef) as ThingWithComps;
				if (weapon == null)
				{
					apparel.Destroy(DestroyMode.Vanish);
					error = "No Core ranged weapon def was available for the downed-pawn eating forbidden-drop probe.";
					return false;
				}
				target.equipment.AddEquipment(weapon);

				var inventoryThing = ThingMaker.MakeThing(ThingDefOf.Silver);
				inventoryThing.stackCount = 7;
				if (target.inventory.innerContainer.TryAdd(inventoryThing, false) == false)
				{
					apparel.Destroy(DestroyMode.Vanish);
					weapon.Destroy(DestroyMode.Vanish);
					inventoryThing.Destroy(DestroyMode.Vanish);
					error = "Target pawn inventory rejected the controlled silver stack.";
					return false;
				}

				controlledGear = new Thing[] { apparel, weapon, inventoryThing };
				evidence = new
				{
					apparel = DescribeControlledDrop(apparel),
					weapon = DescribeControlledDrop(weapon),
					inventory = DescribeControlledDrop(inventoryThing)
				};
				return true;
			}

			static bool TryPrepareOneBiteLethalEatingTarget(Pawn target, out object evidence, out string error)
			{
				evidence = null;
				error = null;
				if (target?.health?.hediffSet == null)
				{
					error = "Target pawn has no health tracker.";
					return false;
				}

				static bool IsLethalEatingPart(BodyPartRecord part)
				{
					var defName = part?.def?.defName;
					return defName == "Head" || defName == "Neck";
				}

				var initialParts = target.health.hediffSet
					.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Outside)
					.Where(part => Tools.IsSafeMissingPartTarget(target, part))
					.ToArray();
				var initialLethalParts = initialParts.Where(IsLethalEatingPart).ToArray();
				if (initialLethalParts.Length == 0)
				{
					error = "Target pawn had no head or neck eating target for the lethal eating probe.";
					return false;
				}

				var removedParts = new List<string>();
				foreach (var part in initialParts)
				{
					if (IsLethalEatingPart(part))
						continue;
					if (Tools.IsSafeMissingPartTarget(target, part) == false)
						continue;
					if (Tools.TryAddMissingPart(target, part, HediffDefOf.Bite, false) == false)
						continue;
					removedParts.Add(part.Label);
					if (target.Dead)
					{
						error = $"Preparing the lethal eating probe unexpectedly killed the target while removing {part.Label}.";
						return false;
					}
				}

				var remainingParts = target.health.hediffSet
					.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Outside)
					.Where(part => Tools.IsSafeMissingPartTarget(target, part))
					.ToArray();
				var remainingNonLethalParts = remainingParts
					.Where(part => IsLethalEatingPart(part) == false)
					.Select(part => part.Label)
					.ToArray();
				if (remainingParts.Length == 0 || remainingNonLethalParts.Length > 0)
				{
					error = "Could not reduce the target to only lethal eating parts.";
					evidence = new
					{
						removedParts,
						remainingParts = remainingParts.Select(part => part.Label).ToArray(),
						remainingNonLethalParts
					};
					return false;
				}

				evidence = new
				{
					initialEatableParts = initialParts.Select(part => part.Label).ToArray(),
					initialLethalParts = initialLethalParts.Select(part => part.Label).ToArray(),
					removedParts,
					remainingParts = remainingParts.Select(part => part.Label).ToArray()
				};
				return true;
			}

			object RunKillAndForbidDropsCase(IntVec3 caseRoot)
			{
				const string name = "downedEatingKillsAndForbidsDrops";
				if (TryFindEatingFixtureCells(map, caseRoot, "downed-pawn-forbidden-drops", out var zombieCell, out var targetCell, out var error) == false)
				{
					allCasesSucceeded = false;
					return new { name, success = false, error };
				}

				ApplyZombieSettingsOverride(settings =>
				{
					settings.zombiesEatDowned = true;
					settings.zombiesEatCorpses = false;
					settings.attackMode = AttackMode.OnlyColonists;
				});

				Zombie zombie = null;
				Pawn target = null;
				Thing[] controlledGear = Array.Empty<Thing>();
				try
				{
					zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true) as Zombie;
					if (zombie == null)
					{
						allCasesSucceeded = false;
						return new
						{
							name,
							success = false,
							error = "ZombieGenerator.SpawnZombie returned no downed-pawn forbidden-drop test zombie.",
							zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell)
						};
					}

					zombie.story.bodyType = BodyTypeDefOf.Fat;
					zombie.pather?.StopDead();
					zombie.jobs?.EndCurrentJob(JobCondition.InterruptForced);
					zombie.state = ZombieState.Wandering;
					zombie.raging = 0;

					target = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
					GenSpawn.Spawn(target, targetCell, map, WipeMode.Vanish);
					DisablePawnWork(target);
					if (TryMakeDowned(target, out var downedError) == false)
					{
						allCasesSucceeded = false;
						return new
						{
							name,
							success = false,
							zombie = DescribeZombie(zombie),
							target = DescribePawn(target),
							error = downedError
						};
					}
					if (TryPrepareOneBiteLethalEatingTarget(target, out var lethalPrep, out var lethalPrepError) == false)
					{
						allCasesSucceeded = false;
						return new
						{
							name,
							success = false,
							zombie = DescribeZombie(zombie),
							target = DescribePawn(target),
							lethalPrep,
							error = lethalPrepError
						};
					}
					if (TryStageControlledEatingDeathGear(target, out controlledGear, out var gearBefore, out var gearError) == false)
					{
						allCasesSucceeded = false;
						return new
						{
							name,
							success = false,
							zombie = DescribeZombie(zombie),
							target = DescribePawn(target),
							lethalPrep,
							error = gearError
						};
					}
					ClearEatingGridNeighborhood(map, zombieCell, targetCell);

					var eatDelayTicks = Constants.EAT_DELAY_TICKS / 4;
					var maxTicks = eatDelayTicks + 30;
					var deathTick = -1;
					var samples = new List<object>();
					var stumbleJob = JobMaker.MakeJob(CustomDefs.Stumble);
					stumbleJob.playerForced = true;
					zombie.jobs.StartJob(stumbleJob, JobCondition.InterruptForced, null, true, false, null, null);

					for (var tick = 1; tick <= maxTicks; tick++)
					{
						AdvanceGameTicks(1);
						var driver = zombie.jobs?.curDriver as JobDriver_Stumble;
						if (tick == 1 || tick == maxTicks || tick % 60 == 0 || target.Dead)
						{
							samples.Add(new
							{
								tick,
								zombieJob = zombie.CurJobDef?.defName,
								eatTarget = driver?.eatTarget == null ? null : ZombieRuntimeActions.StableThingId(driver.eatTarget),
								eatDelayCounter = driver?.eatDelayCounter ?? -1,
								eatDelay = driver?.eatDelay ?? -1,
								targetDowned = target.health?.Downed ?? false,
								targetDead = target.Dead,
								targetSpawned = target.Spawned,
								targetCell = ZombieRuntimeActions.DescribeCell(target.PositionHeld),
								drops = controlledGear.Select(DescribeControlledDrop).ToArray()
							});
						}
						if (target.Dead)
						{
							deathTick = tick;
							break;
						}
					}

					var dropsAfter = controlledGear.Select(DescribeControlledDrop).ToArray();
					var unforbiddenDrops = controlledGear
						.Where(thing => thing != null && thing.Destroyed == false && thing.Spawned && thing.IsForbidden(Faction.OfPlayer) == false)
						.Select(DescribeControlledDrop)
						.ToArray();
					var missingDrops = controlledGear
						.Where(thing => thing == null || thing.Destroyed || thing.Spawned == false)
						.Select(DescribeControlledDrop)
						.ToArray();
					var success = deathTick > 0
						&& controlledGear.Length == 3
						&& missingDrops.Length == 0
						&& unforbiddenDrops.Length == 0;
					allCasesSucceeded &= success;

					return new
					{
						name,
						success,
						eatDowned = true,
						expectEating = true,
						sourcePath = "JobDriver_Stumble.TickAction -> ZombieStateHandler.Eat -> EatBodyPart -> DropAndForbidGearFromZombieEating",
						sourceDerivedTicks = new
						{
							baseEatDelay = Constants.EAT_DELAY_TICKS,
							bodyType = zombie.story.bodyType?.defName,
							eatDelayTicks,
							maxTicks
						},
						zombie = DescribeZombie(zombie),
						target = DescribePawn(target),
						cells = new
						{
							zombie = ZombieRuntimeActions.DescribeCell(zombieCell),
							target = ZombieRuntimeActions.DescribeCell(targetCell)
						},
						gearBefore,
						lethalPrep,
						deathTick,
						dropsAfter,
						unforbiddenDrops,
						missingDrops,
						samples
					};
				}
				finally
				{
					foreach (var thing in controlledGear.Where(thing => thing != null && thing.Destroyed == false).ToArray())
						thing.Destroy(DestroyMode.Vanish);
					if (zombie?.Destroyed == false)
						zombie.Destroy(DestroyMode.Vanish);
					if (target?.Corpse is { Destroyed: false } corpse)
						corpse.Destroy(DestroyMode.Vanish);
					if (target?.Destroyed == false)
						target.Destroy(DestroyMode.Vanish);
				}
			}

			try
			{
				var animalKind = DefDatabase<PawnKindDef>.GetNamed("Muffalo", false);
				var nonFleshKind = FindNonFleshPawnKind();
				var harNonFleshKind = FindPawnKind("ZLTestNonFleshAlienKind");
				var disabledCase = RunCase("downedEatingDisabled", false, false, PawnKindDefOf.Colonist, root + new IntVec3(-8, 0, -8));
				var enabledHumanCase = RunCase("downedEatingEnabledHuman", true, true, PawnKindDefOf.Colonist, root + new IntVec3(8, 0, -8));
				var enabledAnimalCase = RunCase("downedEatingEnabledAnimal", true, true, animalKind, root + new IntVec3(8, 0, -16));
				var nonFleshCase = RunCase("downedEatingRejectsNonFlesh", true, false, nonFleshKind, root + new IntVec3(-16, 0, -8));
				var harNonFleshCase = harNonFleshKind == null
					? SkippedOptionalPawnKindCase("downedEatingRejectsHarNonFleshAlien", "ZLTestNonFleshAlienKind", "Optional local ZLTestAlienRace fixture is not loaded.")
					: RunCase("downedEatingRejectsHarNonFleshAlien", true, false, harNonFleshKind, root + new IntVec3(16, 0, 16));
				var forbiddenDropCase = RunKillAndForbidDropsCase(root + new IntVec3(24, 0, 0));
				return new
				{
					success = allCasesSucceeded,
					destroyedZombies,
					cases = new[] { disabledCase, enabledHumanCase, enabledAnimalCase, nonFleshCase, harNonFleshCase, forbiddenDropCase }
				};
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
			}
		}
	}
}
