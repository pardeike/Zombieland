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
		[Tool("zombieland/remove_all_zombies", Description = "Destroy all spawned Zombieland pawns on the current map and clear the cached zombie set.")]
		public static object RemoveAllZombies()
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

			return new
			{
				success = true,
				destroyed = ZombieRuntimeActions.DestroyZombies(map)
			};
		}

		[Tool("zombieland/get_pawn_infection", Description = "Read compact zombie bite and infection state for a spawned non-zombie pawn.")]
		public static object GetPawnInfection([ToolParameter(Description = "Pawn id, ThingID, label, or short name.", Required = true)] string target)
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

			return new
			{
				success = true,
				infection = ZombieRuntimeActions.DescribePawnInfection(pawn)
			};
		}

		[Tool("zombieland/apply_zombie_bite", Description = "Apply a Zombieland bite to a spawned pawn and return the resulting infection state.")]
		public static object ApplyZombieBite(
			[ToolParameter(Description = "Pawn id, ThingID, label, or short name.", Required = true)] string target,
			[ToolParameter(Description = "Bite state to apply: harmful, final, or harmless.", Required = false, DefaultValue = "harmful")] string stage = "harmful")
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

			if (ZombieRuntimeActions.AddZombieBite(pawn, stage, out var bite, out error) == false)
			{
				return new
				{
					success = false,
					error
				};
			}

			return new
			{
				success = true,
				biteLabel = bite.LabelCap,
				stage = stage ?? "harmful",
				infection = ZombieRuntimeActions.DescribePawnInfection(pawn)
			};
		}

		[Tool("zombieland/remove_pawn_infections", Description = "Make zombie bites harmless and remove active Zombieland infection hediffs from a spawned pawn.")]
		public static object RemovePawnInfections([ToolParameter(Description = "Pawn id, ThingID, label, or short name.", Required = true)] string target)
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

			return new
			{
				success = true,
				removedInfectionHediffs = ZombieRuntimeActions.RemoveZombieInfections(pawn),
				infection = ZombieRuntimeActions.DescribePawnInfection(pawn)
			};
		}

		[Tool("zombieland/infection_medical_state", Description = "Run compact medical/death patch contracts for zombie-bite healing, health-card UI, Pawn.Tick state sync, Pawn.Kill infection/loot behavior, ShouldRemove persistence, and remove-body-part surgery targeting.")]
		public static object InfectionMedicalState(
			[ToolParameter(Description = "Action mode: all, pawn-kill, health-card-living, or health-card-dead.", Required = false, DefaultValue = "all")] string actionMode = "all")
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

			var normalizedMode = (actionMode ?? "all").Trim().ToLowerInvariant();
			if (normalizedMode == "pawn-kill")
				return VerifyPawnKillPatch(map, new IntVec3(map.Size.x / 2, 0, map.Size.z / 2));
			if (normalizedMode == "health-card-living" || normalizedMode == "health-card-dead")
				return PrepareHealthCardFixture(map, normalizedMode == "health-card-dead");
			if (normalizedMode != "all")
			{
				return new
				{
					success = false,
					actionMode = normalizedMode,
					error = "Unsupported infection_medical_state actionMode. Use all, pawn-kill, health-card-living, or health-card-dead."
				};
			}

			var spawnedPawns = new List<Pawn>();
			var oldInfectionChance = ZombieSettings.Values.zombieBiteInfectionChance;
			try
			{
				ZombieSettings.Values.zombieBiteInfectionChance = 1f;
				if (TryFindClearSpawnCell(map, new IntVec3(map.Size.x / 2, 0, map.Size.z / 2), 16f, out var patientCell, out var spawnError) == false)
					return spawnError;

				var patient = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(patient, patientCell, map, WipeMode.Vanish);
				DisablePawnWork(patient);
				spawnedPawns.Add(patient);

				if (ZombieRuntimeActions.AddZombieBite(patient, "harmful", out var hiddenBite, out var error) == false)
					return InfectionMedicalFailure(patient, null, error);
				ForceHiddenBite(hiddenBite);
				if (ZombieRuntimeActions.AddZombieBite(patient, "harmless", out var harmlessBite, out error) == false)
					return InfectionMedicalFailure(patient, null, error);
				if (ZombieRuntimeActions.AddZombieBite(patient, "harmful", out var infectableBite, out error) == false)
					return InfectionMedicalFailure(patient, null, error);
				if (ZombieRuntimeActions.AddZombieBite(patient, "final", out var infectingBite, out error) == false)
					return InfectionMedicalFailure(patient, null, error);

				var ordinaryCut = CreateOrdinaryCut(patient);
				var animalCase = CreateAnimalBiteCase(map, patient.Position, spawnedPawns);

				var healCases = new[]
				{
					DescribeNaturalHealingCase("hidden", hiddenBite),
					DescribeNaturalHealingCase("harmless", harmlessBite),
					DescribeNaturalHealingCase("infectable", infectableBite),
					DescribeNaturalHealingCase("infecting", infectingBite),
					DescribeNaturalHealingCase("ordinaryCut", ordinaryCut)
				};
				var animalHealCase = animalCase.bite == null ? null : DescribeNaturalHealingCase("animalZombieBite", animalCase.bite);

				hiddenBite.Severity = 0f;
				harmlessBite.Severity = 0f;
				infectableBite.Severity = 0f;
				infectingBite.Severity = 0f;
				if (ordinaryCut != null)
					ordinaryCut.Severity = 0f;

				var removalCases = new[]
				{
					DescribeShouldRemoveCase("hidden", hiddenBite),
					DescribeShouldRemoveCase("harmless", harmlessBite),
					DescribeShouldRemoveCase("infectable", infectableBite),
					DescribeShouldRemoveCase("infecting", infectingBite),
					DescribeShouldRemoveCase("ordinaryCut", ordinaryCut)
				};

				var removePartWorker = RecipeDefOf.RemoveBodyPart.Worker;
				var parts = removePartWorker.GetPartsToApplyOn(patient, RecipeDefOf.RemoveBodyPart).ToArray();
				var bittenParts = new[] { hiddenBite, harmlessBite, infectableBite, infectingBite }
					.Select(bite => bite.Part)
					.Where(part => part != null)
					.Distinct()
					.ToArray();
				var duplicateParts = parts
					.Where(part => part != null)
					.GroupBy(part => part)
					.Where(group => group.Count() > 1)
					.Select(group => DescribeBodyPart(group.Key))
					.ToArray();
				var missingBittenParts = bittenParts
					.Where(part => parts.Contains(part) == false)
					.Select(DescribeBodyPart)
					.ToArray();

				var naturalHealingValid = healCases.First(c => c.name == "hidden").canHealNaturally == false
					&& healCases.First(c => c.name == "harmless").canHealNaturally
					&& healCases.First(c => c.name == "infectable").canHealNaturally == false
					&& healCases.First(c => c.name == "infecting").canHealNaturally == false
					&& healCases.First(c => c.name == "ordinaryCut").canHealNaturally
					&& (animalHealCase == null || animalHealCase.canHealNaturally);
				var shouldRemoveValid = removalCases.First(c => c.name == "hidden").shouldRemove == false
					&& removalCases.First(c => c.name == "harmless").shouldRemove
					&& removalCases.First(c => c.name == "infectable").shouldRemove == false
					&& removalCases.First(c => c.name == "infecting").shouldRemove == false
					&& removalCases.First(c => c.name == "ordinaryCut").shouldRemove;
				var removeBodyPartValid = bittenParts.Length > 0
					&& missingBittenParts.Length == 0
					&& duplicateParts.Length == 0;
				var pawnTick = VerifyPawnTickPatch(map, patient.Position + new IntVec3(12, 0, 0), spawnedPawns);
				var pawnKill = VerifyPawnKillPatch(map, patient.Position + new IntVec3(24, 0, 0));

				return new
				{
					success = naturalHealingValid && shouldRemoveValid && removeBodyPartValid && ObjectSuccess(pawnTick) && ObjectSuccess(pawnKill),
					actionMode = normalizedMode,
					patient = DescribePawn(patient),
					infection = ZombieRuntimeActions.DescribePawnInfection(patient),
					naturalHealingValid,
					shouldRemoveValid,
					removeBodyPartValid,
					pawnTick,
					pawnKill,
					healCases,
					animalHealCase,
					animalCase = animalCase.description,
					removalCases,
					removeBodyPart = new
					{
						totalPartCount = parts.Length,
						bittenParts = bittenParts.Select(DescribeBodyPart).ToArray(),
						missingBittenParts,
						duplicateParts,
						firstParts = parts.Take(12).Select(DescribeBodyPart).ToArray()
					}
				};
			}
			finally
			{
				ZombieSettings.Values.zombieBiteInfectionChance = oldInfectionChance;
				foreach (var pawn in spawnedPawns.Where(pawn => pawn != null && pawn.Destroyed == false).ToArray())
					pawn.Destroy(DestroyMode.Vanish);
			}
		}

		static object PrepareHealthCardFixture(Map map, bool dead)
		{
			var patchTargets = PatchedMethodsForPatchClass("HealthCardUtility_DrawOverviewTab_Patch");
			if (TryFindClearSpawnCell(map, new IntVec3(map.Size.x / 2, 0, map.Size.z / 2), 24f, out var cell, out var spawnError) == false)
				return spawnError;

			var oldHoursAfterDeath = ZombieSettings.Values.hoursAfterDeathToBecomeZombie;
			try
			{
				ZombieSettings.Values.hoursAfterDeathToBecomeZombie = 2;
				var pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(pawn, cell, map, WipeMode.Vanish);
				DisablePawnWork(pawn);

				if (ZombieRuntimeActions.AddZombieBite(pawn, "final", out var bite, out var biteError) == false)
				{
					return new
					{
						success = false,
						patchTargets,
						pawn = DescribePawn(pawn),
						error = biteError
					};
				}
				var biteState = bite.TendDuration?.GetInfectionState() ?? InfectionState.None;
				pawn.SetInfectionState(biteState);

				Thing selectedThing = pawn;
				Corpse corpse = null;
				if (dead)
				{
					if (ZombieRuntimeActions.KillPawnToCorpse(pawn, out corpse, out var killError) == false)
					{
						return new
						{
							success = false,
							patchTargets,
							pawn = DescribePawn(pawn),
							error = killError
						};
					}
					selectedThing = corpse;
				}

				Find.Selector.ClearSelection();
				Find.Selector.Select(selectedThing, false, false);
				var openedTab = InspectPaneUtility.OpenTab(typeof(ITab_Pawn_Health));
				var expectedLabel = "BodyIsInfectedLabel".Translate().ToString();
				var expectedTooltip = "BodyIsInfectedTooltip".Translate().ToString();
				var zombieBites = pawn.GetHediffsList<Hediff_Injury_ZombieBite>().ToArray();
				var infectionHediffs = new List<Hediff_ZombieInfection>();
				pawn.health.hediffSet.GetHediffs(ref infectionHediffs);
				var expectedEligible = patchTargets.Length > 0
					&& pawn.health?.hediffSet?.GetBrain() != null
					&& (dead
						? zombieBites.Any(zombieBite => zombieBite.mayBecomeZombieWhenDead)
						: pawn.InfectionState() >= InfectionState.BittenInfectable);

				return new
				{
					success = expectedEligible && openedTab != null && Find.Selector.IsSelected(selectedThing),
					action = dead ? "health-card-dead" : "health-card-living",
					patchTargets,
					expectedLabel,
					expectedTooltip,
					selected = new
					{
						id = ZombieRuntimeActions.StableThingId(selectedThing),
						thingId = selectedThing?.ThingID,
						defName = selectedThing?.def?.defName,
						className = selectedThing?.GetType().FullName,
						spawned = selectedThing?.Spawned ?? false,
						position = selectedThing?.Spawned == true ? ZombieRuntimeActions.DescribeCell(selectedThing.Position) : null
					},
					openedTab = openedTab?.GetType().FullName,
					pawn = DescribePawn(pawn),
					corpse = DescribeCorpse(corpse),
					bite = new
					{
						state = biteState.ToString(),
						bite.mayBecomeZombieWhenDead,
						partDefName = bite.Part?.def?.defName
					},
					zombieBiteCount = zombieBites.Length,
					mayBecomeZombieBiteCount = zombieBites.Count(zombieBite => zombieBite.mayBecomeZombieWhenDead),
					infectionHediffCount = infectionHediffs.Count,
					infectionTicks = infectionHediffs.Select(hediff => hediff.ticksWhenBecomingZombie).ToArray(),
					expectedEligible,
					selection = new
					{
						isSelected = Find.Selector.IsSelected(selectedThing),
						selectedCount = Find.Selector.SelectedObjects.Count
					}
				};
			}
			finally
			{
				ZombieSettings.Values.hoursAfterDeathToBecomeZombie = oldHoursAfterDeath;
			}
		}

		static object VerifyPawnKillPatch(Map map, IntVec3 root)
		{
			var patchTargets = PatchedMethodsForPatchClass("Pawn_Kill_Patch");
			var cells = new List<IntVec3>();
			var nextCellRoot = root;
			for (var i = 0; i < 5; i++)
				if (TryFindClearSpawnCell(map, nextCellRoot, 16f, out var cell, out var cellError))
				{
					cells.Add(cell);
					nextCellRoot = cell + new IntVec3(4, 0, 0);
				}
				else
				{
					return new
					{
						success = false,
						patchTargets,
						foundCells = cells.Count,
						error = cellError
					};
				}

			var animalKind = DefDatabase<PawnKindDef>.GetNamed("Muffalo", false)
				?? DefDatabase<PawnKindDef>.GetNamed("WildBoar", false)
				?? DefDatabase<PawnKindDef>.GetNamed("Hare", false);
			if (animalKind == null)
			{
				return new
				{
					success = false,
					patchTargets,
					error = "Could not find a vanilla animal PawnKindDef for Pawn.Kill negative verification."
				};
			}

			var oldLootExtractAmount = ZombieSettings.Values.lootExtractAmount;
			var oldHoursAfterDeath = ZombieSettings.Values.hoursAfterDeathToBecomeZombie;
			var spawnedThings = new List<Thing>();
			var spawnedPawns = new List<Pawn>();
			try
			{
				ZombieSettings.Values.lootExtractAmount = 1f;
				ZombieSettings.Values.hoursAfterDeathToBecomeZombie = 2;

				var normalZombie = ZombieRuntimeActions.SpawnZombie(cells[0], map, ZombieType.Normal, true) as Zombie;
				var spitter = SpawnFireFixturePawn(map, cells[1], "spitter") as ZombieSpitter;
				var infectedPawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				var harmlessPawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				var animalPawn = PawnGenerator.GeneratePawn(animalKind, null);
				GenSpawn.Spawn(infectedPawn, cells[2], map, WipeMode.Vanish);
				GenSpawn.Spawn(harmlessPawn, cells[3], map, WipeMode.Vanish);
				GenSpawn.Spawn(animalPawn, cells[4], map, WipeMode.Vanish);
				spawnedPawns.AddRange(new[] { normalZombie, spitter, infectedPawn, harmlessPawn, animalPawn }.Where(pawn => pawn != null));
				DisablePawnWork(infectedPawn);
				DisablePawnWork(harmlessPawn);

				if (normalZombie == null || spitter == null)
				{
					return new
					{
						success = false,
						patchTargets,
						normalZombie = DescribePawn(normalZombie),
						spitter = DescribePawn(spitter),
						error = "Could not spawn both a normal zombie and spitter for Pawn.Kill verification."
					};
				}

				var normalKill = VerifyPawnKillZombieJobAndLoot(map, normalZombie, spawnedThings);
				var spitterKill = VerifyPawnKillSpitterLoot(map, spitter);
				var infectedKill = VerifyPawnKillHumanInfection(infectedPawn, "final");
				var harmlessKill = VerifyPawnKillHumanInfection(harmlessPawn, "harmless");
				var animalKill = VerifyPawnKillAnimalNegative(animalPawn);

				return new
				{
					success = patchTargets.Length > 0
						&& ObjectSuccess(normalKill)
						&& ObjectSuccess(spitterKill)
						&& ObjectSuccess(infectedKill)
						&& ObjectSuccess(harmlessKill)
						&& ObjectSuccess(animalKill),
					patchTargets,
					normalKill,
					spitterKill,
					infectedKill,
					harmlessKill,
					animalKill,
					settings = new
					{
						lootExtractAmount = ZombieSettings.Values.lootExtractAmount,
						hoursAfterDeathToBecomeZombie = ZombieSettings.Values.hoursAfterDeathToBecomeZombie
					}
				};
			}
			finally
			{
				ZombieSettings.Values.lootExtractAmount = oldLootExtractAmount;
				ZombieSettings.Values.hoursAfterDeathToBecomeZombie = oldHoursAfterDeath;
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
				foreach (var pawn in spawnedPawns.Where(pawn => pawn != null && pawn.Destroyed == false).ToArray())
					pawn.Destroy(DestroyMode.Vanish);
				foreach (var corpse in map.listerThings.AllThings.OfType<Corpse>().Where(corpse => corpse.Position.DistanceToSquared(root) < 900).ToArray())
					corpse.Destroy(DestroyMode.Vanish);
			}
		}

		static object VerifyPawnKillZombieJobAndLoot(Map map, Zombie zombie, List<Thing> spawnedThings)
		{
			var apparelDef = DefDatabase<ThingDef>.GetNamed("Apparel_Pants", false);
			if (apparelDef == null)
			{
				return new
				{
					success = false,
					zombie = DescribePawn(zombie),
					error = "ThingDef Apparel_Pants was not found."
				};
			}

			var apparelStuff = GenStuff.DefaultStuffFor(apparelDef);
			var apparel = ThingMaker.MakeThing(apparelDef, apparelStuff) as Apparel;
			if (apparel == null)
			{
				return new
				{
					success = false,
					zombie = DescribePawn(zombie),
					apparelDef = apparelDef.defName,
					apparelStuff = apparelStuff?.defName,
					error = "Apparel_Pants did not create Apparel."
				};
			}

			spawnedThings.Add(apparel);
			zombie.apparel.Wear(apparel, false);
			EndCurrentPawnJob(zombie);
			var waitJob = JobMaker.MakeJob(JobDefOf.Wait);
			zombie.jobs.StartJob(waitJob, JobCondition.InterruptForced, null, false, true);
			var jobBefore = DescribeJob(zombie.CurJob);
			var wornBefore = zombie.apparel.WornApparel.Contains(apparel);
			var apparelSpawnedBefore = apparel.Spawned;
			var apparelCountBefore = map.listerThings.AllThings.Count(thing => thing.def == apparelDef);

			Rand.PushState(46812);
			try
			{
				zombie.Kill(null);
			}
			finally
			{
				Rand.PopState();
			}

			var apparelCountAfter = map.listerThings.AllThings.Count(thing => thing.def == apparelDef);
			var apparelDropped = apparel.Spawned && apparel.Map == map;
			var jobCleared = zombie.CurJob == null;
			var corpse = zombie.Corpse as ZombieCorpse
				?? map.listerThings.AllThings.OfType<ZombieCorpse>().OrderBy(thing => thing.Position.DistanceToSquared(zombie.PositionHeld)).FirstOrDefault();

			return new
			{
				success = wornBefore
					&& apparelSpawnedBefore == false
					&& apparelDropped
					&& apparelCountAfter > apparelCountBefore
					&& jobBefore != null
					&& jobCleared
					&& zombie.Dead,
				zombie = DescribeZombie(zombie),
				corpse = DescribeCorpse(corpse),
				jobBefore,
				jobAfter = DescribeJob(zombie.CurJob),
				wornBefore,
				apparel = ZombieRuntimeActions.StableThingId(apparel),
				apparelDef = apparelDef.defName,
				apparelStuff = apparelStuff?.defName,
				apparelSpawnedBefore,
				apparelDropped,
				apparelCountBefore,
				apparelCountAfter,
				jobCleared,
				zombieDead = zombie.Dead
			};
		}

		static object VerifyPawnKillSpitterLoot(Map map, ZombieSpitter spitter)
		{
			var serumDef = Tools.SpitterSerumDefForPlayer();
			if (serumDef == null)
			{
				return new
				{
					success = false,
					spitter = DescribePawn(spitter),
					error = "The expected player-faction spitter serum def was not found."
				};
			}

			spitter.aggressive = true;
			var serumIdsBefore = map.listerThings.ThingsOfDef(serumDef)
				.Select(ZombieRuntimeActions.StableThingId)
				.ToHashSet();

			Rand.PushState(46813);
			try
			{
				spitter.Kill(null);
			}
			finally
			{
				Rand.PopState();
			}

			var newSerums = map.listerThings.ThingsOfDef(serumDef)
				.Where(thing => serumIdsBefore.Contains(ZombieRuntimeActions.StableThingId(thing)) == false)
				.ToArray();

			return new
			{
				success = spitter.Dead && newSerums.Length == 1,
				spitter = DescribePawn(spitter),
				serumDef = serumDef.defName,
				makeshiftSerumAvailable = Tools.MakeshiftSerumAvailableForPlayer(),
				newSerumCount = newSerums.Length,
				newSerums = newSerums.Select(thing => new
				{
					id = ZombieRuntimeActions.StableThingId(thing),
					position = ZombieRuntimeActions.DescribeCell(thing.Position),
					forbidden = thing.IsForbidden(Faction.OfPlayer)
				}).ToArray(),
				spitterDead = spitter.Dead
			};
		}

		static object VerifyPawnKillHumanInfection(Pawn pawn, string biteStage)
		{
			if (ZombieRuntimeActions.AddZombieBite(pawn, biteStage, out var bite, out var biteError) == false)
			{
				return new
				{
					success = false,
					pawn = DescribePawn(pawn),
					biteStage,
					error = biteError
				};
			}

			bite.mayBecomeZombieWhenDead = false;
			var biteStateBefore = bite.TendDuration?.GetInfectionState() ?? InfectionState.None;
			var mayBecomeBefore = bite.mayBecomeZombieWhenDead;
			var tickBefore = GenTicks.TicksGame;
			var expectedTicksWhenBecomingZombie = tickBefore + GenDate.TicksPerHour * ZombieSettings.Values.hoursAfterDeathToBecomeZombie;
			if (ZombieRuntimeActions.KillPawnToCorpse(pawn, out var corpse, out var killError) == false)
			{
				return new
				{
					success = false,
					pawn = DescribePawn(pawn),
					biteStage,
					biteStateBefore = biteStateBefore.ToString(),
					error = killError
				};
			}

			var infectionHediffs = new List<Hediff_ZombieInfection>();
			pawn.health.hediffSet.GetHediffs(ref infectionHediffs);
			var infectionTicks = infectionHediffs.Select(hediff => hediff.ticksWhenBecomingZombie).ToArray();
			var expectedMayBecome = biteStateBefore >= InfectionState.BittenInfectable;
			var biteFlagMatches = bite.mayBecomeZombieWhenDead == expectedMayBecome;
			var infectionInstalled = infectionHediffs.Count == 1
				&& infectionTicks[0] == expectedTicksWhenBecomingZombie;

			return new
			{
				success = pawn.Dead
					&& corpse != null
					&& biteFlagMatches
					&& infectionInstalled,
				pawn = DescribePawn(pawn),
				corpse = DescribeCorpse(corpse),
				biteStage,
				biteStateBefore = biteStateBefore.ToString(),
				mayBecomeBefore,
				mayBecomeAfter = bite.mayBecomeZombieWhenDead,
				expectedMayBecome,
				biteFlagMatches,
				tickBefore,
				hoursAfterDeathToBecomeZombie = ZombieSettings.Values.hoursAfterDeathToBecomeZombie,
				expectedTicksWhenBecomingZombie,
				infectionHediffCount = infectionHediffs.Count,
				infectionTicks,
				infectionInstalled
			};
		}

		static object VerifyPawnKillAnimalNegative(Pawn animal)
		{
			var tickBefore = GenTicks.TicksGame;
			if (ZombieRuntimeActions.KillPawnToCorpse(animal, out var corpse, out var killError) == false)
			{
				return new
				{
					success = false,
					animal = DescribePawn(animal),
					error = killError
				};
			}

			var infectionHediffs = new List<Hediff_ZombieInfection>();
			animal.health.hediffSet.GetHediffs(ref infectionHediffs);
			return new
			{
				success = animal.Dead
					&& corpse != null
					&& infectionHediffs.Count == 0,
				animal = DescribePawn(animal),
				corpse = DescribeCorpse(corpse),
				tickBefore,
				infectionHediffCount = infectionHediffs.Count,
				infectionTicks = infectionHediffs.Select(hediff => hediff.ticksWhenBecomingZombie).ToArray()
			};
		}

		static object VerifyPawnTickPatch(Map map, IntVec3 root, List<Pawn> spawnedPawns)
		{
			var tickMethod = typeof(Pawn).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic);
			if (tickMethod == null)
			{
				return new
				{
					success = false,
					error = "Could not find protected Verse.Pawn.Tick() by reflection."
				};
			}

			if (TryFindClearSpawnCell(map, root, 16f, out var infectionCell, out var infectionSpawnError) == false)
				return infectionSpawnError;
			if (TryFindClearSpawnCell(map, infectionCell + new IntVec3(4, 0, 0), 16f, out var cleanCell, out var cleanSpawnError) == false)
				return cleanSpawnError;
			if (TryFindClearSpawnCell(map, cleanCell + new IntVec3(4, 0, 0), 16f, out var contaminatedCell, out var contaminatedSpawnError) == false)
				return contaminatedSpawnError;

			var oldContamination = Constants.CONTAMINATION;
			var oldEffectivenessPercentage = ZombieSettings.Values.contamination.contaminationEffectivenessPercentage;
			var spawnedThings = new List<Thing>();
			try
			{
				Constants.CONTAMINATION = true;
				ZombieSettings.Values.contamination.contaminationEffectivenessPercentage = 0.95f;

				var infectionPawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				var cleanPawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				var contaminatedPawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(infectionPawn, infectionCell, map, WipeMode.Vanish);
				GenSpawn.Spawn(cleanPawn, cleanCell, map, WipeMode.Vanish);
				GenSpawn.Spawn(contaminatedPawn, contaminatedCell, map, WipeMode.Vanish);
				spawnedPawns.AddRange(new[] { infectionPawn, cleanPawn, contaminatedPawn });
				DisablePawnWork(infectionPawn);
				DisablePawnWork(cleanPawn);
				DisablePawnWork(contaminatedPawn);

				if (ZombieRuntimeActions.AddZombieBite(infectionPawn, "final", out var bite, out var biteError) == false)
				{
					return new
					{
						success = false,
						pawn = DescribePawn(infectionPawn),
						error = biteError
					};
				}

				var biteState = bite.TendDuration?.GetInfectionState() ?? InfectionState.None;
				var customStateBefore = infectionPawn.InfectionState();
				tickMethod.Invoke(infectionPawn, Array.Empty<object>());
				var customStateAfter = infectionPawn.InfectionState();
				var infectionStateUpdated = customStateBefore == InfectionState.None
					&& customStateAfter == biteState
					&& biteState == InfectionState.Infecting;

				if (TryEquipRunningChainsaw(cleanPawn, cleanCell, spawnedThings, out var cleanChainsaw, out var cleanRefuelable, out var cleanError) == false)
					return cleanError;
				if (TryEquipRunningChainsaw(contaminatedPawn, contaminatedCell, spawnedThings, out var contaminatedChainsaw, out var contaminatedRefuelable, out var contaminatedError) == false)
					return contaminatedError;

				contaminatedPawn.SetContamination(0.7f);
				var cleanEffectiveness = cleanPawn.GetEffectiveness();
				var contaminatedEffectiveness = contaminatedPawn.GetEffectiveness();
				var cleanFuelBefore = cleanRefuelable.Fuel;
				var contaminatedFuelBefore = contaminatedRefuelable.Fuel;

				const int probeTicks = 40;
				Rand.PushState(94031);
				try
				{
					for (var i = 0; i < probeTicks; i++)
						tickMethod.Invoke(cleanPawn, Array.Empty<object>());
					for (var i = 0; i < probeTicks; i++)
						tickMethod.Invoke(contaminatedPawn, Array.Empty<object>());
				}
				finally
				{
					Rand.PopState();
				}

				var cleanFuelAfter = cleanRefuelable.Fuel;
				var contaminatedFuelAfter = contaminatedRefuelable.Fuel;
				var cleanFuelDelta = cleanFuelBefore - cleanFuelAfter;
				var contaminatedFuelDelta = contaminatedFuelBefore - contaminatedFuelAfter;
				var contaminationGateReducedTicks = cleanFuelDelta > 0f
					&& contaminatedPawn.Spawned
					&& contaminatedPawn.Dead == false
					&& contaminatedPawn.Downed == false
					&& contaminatedEffectiveness < cleanEffectiveness
					&& contaminatedFuelDelta >= 0f
					&& contaminatedFuelDelta < cleanFuelDelta * 0.5f;

				return new
				{
					success = infectionStateUpdated && contaminationGateReducedTicks,
					infection = new
					{
						pawn = DescribePawn(infectionPawn),
						biteState = biteState.ToString(),
						customStateBefore = customStateBefore.ToString(),
						customStateAfter = customStateAfter.ToString(),
						infectionStateUpdated
					},
					contamination = new
					{
						probeTicks,
						cleanPawn = DescribePawn(cleanPawn),
						contaminatedPawn = DescribePawn(contaminatedPawn),
						contaminatedPawnSpawnedAfterTicks = contaminatedPawn.Spawned,
						contaminatedPawnDeadAfterTicks = contaminatedPawn.Dead,
						contaminatedPawnDownedAfterTicks = contaminatedPawn.Downed,
						cleanEffectiveness,
						contaminatedEffectiveness,
						cleanChainsaw = ZombieRuntimeActions.StableThingId(cleanChainsaw),
						contaminatedChainsaw = ZombieRuntimeActions.StableThingId(contaminatedChainsaw),
						cleanFuelBefore,
						cleanFuelAfter,
						cleanFuelDelta,
						contaminatedFuelBefore,
						contaminatedFuelAfter,
						contaminatedFuelDelta,
						contaminationGateReducedTicks
					}
				};
			}
			finally
			{
				Constants.CONTAMINATION = oldContamination;
				ZombieSettings.Values.contamination.contaminationEffectivenessPercentage = oldEffectivenessPercentage;
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}
		}

		static bool TryEquipRunningChainsaw(Pawn pawn, IntVec3 pawnCell, List<Thing> spawnedThings, out Chainsaw chainsaw, out CompRefuelable refuelable, out object error)
		{
			chainsaw = null;
			refuelable = null;
			error = null;
			pawn.equipment?.DestroyAllEquipment(DestroyMode.Vanish);
			var chainsawCell = pawnCell + IntVec3.East;
			if (chainsawCell.InBounds(pawn.Map) == false || chainsawCell.Standable(pawn.Map) == false)
				chainsawCell = pawnCell;

			chainsaw = ThingMaker.MakeThing(CustomDefs.Chainsaw) as Chainsaw;
			if (chainsaw == null)
			{
				error = new
				{
					success = false,
					pawn = DescribePawn(pawn),
					error = "Could not create Chainsaw."
				};
				return false;
			}

			GenSpawn.Spawn(chainsaw, chainsawCell, pawn.Map, WipeMode.Vanish);
			refuelable = chainsaw.TryGetComp<CompRefuelable>();
			if (refuelable == null)
			{
				error = new
				{
					success = false,
					pawn = DescribePawn(pawn),
					chainsaw = ZombieRuntimeActions.StableThingId(chainsaw),
					error = "The spawned chainsaw did not have a refuelable comp."
				};
				return false;
			}

			var fuel = ThingMaker.MakeThing(ThingDefOf.Chemfuel);
			fuel.stackCount = Math.Min(ThingDefOf.Chemfuel.stackLimit, refuelable.GetFuelCountToFullyRefuel());
			GenSpawn.Spawn(fuel, chainsawCell + IntVec3.South, pawn.Map, WipeMode.Vanish);
			spawnedThings.Add(fuel);
			refuelable.Refuel(new List<Thing> { fuel });
			chainsaw.DeSpawn();
			pawn.equipment.AddEquipment(chainsaw);
			chainsaw.StartMotor(true);
			return true;
		}

		const string InfectionWorkflowPrefix = "ZL_Infection_";
		const string InfectionWorkflowDoctorName = "ZL_Infection_Doctor";
		const string InfectionWorkflowHiddenName = "ZL_Infection_Hidden_Alert";
		const string InfectionWorkflowCureName = "ZL_Infection_Cure_Patient";
		const string InfectionWorkflowInfectingName = "ZL_Infection_Infecting_Alert";
		const string InfectionWorkflowCountdownName = "ZL_Infection_Countdown_Corpse";
		const string InfectionWorkflowDoubleTapName = "ZL_Infection_DoubleTap_Corpse";
		const string InfectionWorkflowZombieCorpseName = "ZL_Infection_Extract_Zombie";

		sealed class WorkflowAlertSnapshot
		{
			public string label;
			public bool active;
			public int count;
			public string[] ids;
			public string[] names;
			public string explanation;
		}

		sealed class WorkflowAlertsSnapshot
		{
			public WorkflowAlertSnapshot hidden;
			public WorkflowAlertSnapshot imminent;
			public WorkflowAlertSnapshot infecting;
			public string[] needPatchTracked;
		}

		sealed class WorkflowCorpseConversionSnapshot
		{
			public int infectionCount;
			public int[] ticksWhenBecomingZombie;
			public int ticksUntilConversion;
			public bool hasBrain;
			public bool queued;
		}

		[Tool("zombieland/infection_workflow_state", Description = "Set up, read, or execute the reusable S-Infection-Medical workflow: alerts, cure, double-tap, serum extraction, and corpse-conversion save-load state.")]
		public static object InfectionWorkflowState(
			[ToolParameter(Description = "Action mode: setup, read, execute, or silencePause.", Required = false, DefaultValue = "read")] string actionMode = "read")
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

			var normalizedMode = (actionMode ?? "read").Trim().ToLowerInvariant();
			if (normalizedMode == "setup")
				return SetupInfectionWorkflow(map);
			if (normalizedMode == "execute")
				return ExecuteInfectionWorkflow(map);
			if (normalizedMode == "read")
				return ReadInfectionWorkflow(map, "read");
			if (normalizedMode == "silencepause")
				return VerifyWorkflowCountdownPauseDuringSilence(map);

			return new
			{
				success = false,
				actionMode = normalizedMode,
				error = "Unsupported infection_workflow_state actionMode. Use setup, read, execute, or silencePause."
			};
		}

		static object SetupInfectionWorkflow(Map map)
		{
			var patchTargets = InfectionWorkflowPatchTargets();
			CleanupInfectionWorkflow(map);
			ZombieSettings.Values.zombieBiteInfectionChance = 1f;
			ZombieSettings.Values.hoursAfterDeathToBecomeZombie = 2;
			ZombieSettings.Values.corpsesExtractAmount = Math.Max(1f, ZombieSettings.Values.corpsesExtractAmount);

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 20f, out var doctorCell, out var doctorError) == false)
				return doctorError;

			var doctor = CreateWorkflowColonist(map, doctorCell, InfectionWorkflowDoctorName, true);
			var spawned = new List<Thing> { doctor };
			try
			{
				var hidden = SpawnWorkflowColonistNear(map, doctorCell + new IntVec3(3, 0, 0), InfectionWorkflowHiddenName, spawned);
				var curePatient = SpawnWorkflowColonistNear(map, doctorCell + new IntVec3(6, 0, 0), InfectionWorkflowCureName, spawned);
				var infecting = SpawnWorkflowColonistNear(map, doctorCell + new IntVec3(9, 0, 0), InfectionWorkflowInfectingName, spawned);
				var countdownPawn = SpawnWorkflowColonistNear(map, doctorCell + new IntVec3(0, 0, 4), InfectionWorkflowCountdownName, spawned);
				var doubleTapPawn = SpawnWorkflowColonistNear(map, doctorCell + new IntVec3(3, 0, 4), InfectionWorkflowDoubleTapName, spawned);
				var zombieCell = FindWorkflowCell(map, doctorCell + new IntVec3(6, 0, 4));

				if (AddWorkflowZombieBite(hidden, "harmful", out var hiddenBite, out var error) == false)
					return InfectionWorkflowFailure("setup-hidden-bite", error, spawned);
				ForceHiddenBite(hiddenBite);
				SetPawnInfectionStateFromBite(hidden, hiddenBite);

				if (AddWorkflowZombieBite(curePatient, "harmful", out var cureBite, out error) == false)
					return InfectionWorkflowFailure("setup-cure-bite", error, spawned);
				SetPawnInfectionStateFromBite(curePatient, cureBite);

				if (AddWorkflowZombieBite(infecting, "final", out var infectingBite, out error) == false)
					return InfectionWorkflowFailure("setup-infecting-bite", error, spawned);
				SetPawnInfectionStateFromBite(infecting, infectingBite);

				if (AddWorkflowZombieBite(countdownPawn, "final", out var countdownBite, out error) == false)
					return InfectionWorkflowFailure("setup-countdown-bite", error, spawned);
				ClearWorkflowApparel(countdownPawn);
				if (ZombieRuntimeActions.KillPawnToCorpse(countdownPawn, out var countdownCorpse, out error) == false)
					return InfectionWorkflowFailure("setup-countdown-kill", error, spawned);
				var countdownTicks = SetCorpseConversionTicks(countdownCorpse, GenTicks.TicksGame + 2500);

				if (AddWorkflowZombieBite(doubleTapPawn, "final", out var doubleTapBite, out error) == false)
					return InfectionWorkflowFailure("setup-doubletap-bite", error, spawned);
				ClearWorkflowApparel(doubleTapPawn);
				if (ZombieRuntimeActions.KillPawnToCorpse(doubleTapPawn, out var doubleTapCorpse, out error) == false)
					return InfectionWorkflowFailure("setup-doubletap-kill", error, spawned);

				var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
				if (zombie == null)
					return InfectionWorkflowFailure("setup-zombie", "ZombieGenerator.SpawnZombie returned no zombie.", spawned);
				zombie.Name = new NameSingle(InfectionWorkflowZombieCorpseName);
				zombie.Kill(null);
				var zombieCorpse = zombie.Corpse as ZombieCorpse
					?? map.listerThings.AllThings.OfType<ZombieCorpse>().OrderBy(thing => thing.Position.DistanceToSquared(zombieCell)).FirstOrDefault();
				if (zombieCorpse == null)
					return InfectionWorkflowFailure("setup-zombie-corpse", "Killing the workflow zombie did not leave a ZombieCorpse.", spawned);
				TrackZombieCorpse(map, zombieCorpse);

				var state = ReadInfectionWorkflow(map, "setup");
				return new
				{
					success = ObjectSuccess(state),
					actionMode = "setup",
					patchTargets,
					doctor = DescribePawn(doctor),
					fixture = state,
					countdownTicks,
					bites = new
					{
						hidden = DescribeWorkflowBite(hiddenBite),
						cure = DescribeWorkflowBite(cureBite),
						infecting = DescribeWorkflowBite(infectingBite),
						countdown = DescribeWorkflowBite(countdownBite),
						doubleTap = DescribeWorkflowBite(doubleTapBite)
					},
					corpses = new
					{
						countdown = DescribeCorpse(countdownCorpse),
						doubleTap = DescribeCorpse(doubleTapCorpse),
						zombie = DescribeCorpse(zombieCorpse)
					}
				};
			}
			catch (Exception ex)
			{
				return new
				{
					success = false,
					actionMode = "setup",
					error = ex.ToString()
				};
			}
		}

		static object ReadInfectionWorkflow(Map map, string actionMode)
		{
			var doctor = FindWorkflowPawn(map, InfectionWorkflowDoctorName);
			var hidden = FindWorkflowPawn(map, InfectionWorkflowHiddenName);
			var curePatient = FindWorkflowPawn(map, InfectionWorkflowCureName);
			var infecting = FindWorkflowPawn(map, InfectionWorkflowInfectingName);
			var countdownCorpse = FindWorkflowCorpse(map, InfectionWorkflowCountdownName);
			var doubleTapCorpse = FindWorkflowCorpse(map, InfectionWorkflowDoubleTapName);
			var zombieCorpse = FindWorkflowCorpse(map, InfectionWorkflowZombieCorpseName) as ZombieCorpse;
			var conversionQueue = map.GetComponent<TickManager>()?.colonistsToConvert;
			var alerts = DescribeInfectionAlerts();
			var countdown = DescribeWorkflowCorpseConversion(countdownCorpse, conversionQueue);
			var doubleTap = DescribeWorkflowCorpseConversion(doubleTapCorpse, conversionQueue);

			return new
			{
				success = doctor != null
					&& hidden != null
					&& curePatient != null
					&& infecting != null
					&& countdownCorpse != null
					&& doubleTapCorpse != null
					&& zombieCorpse != null
					&& alerts.hidden.count == 1
					&& alerts.imminent.count == 1
					&& alerts.infecting.count == 1
					&& countdown.infectionCount == 1
					&& countdown.ticksUntilConversion > 0
					&& doubleTap.hasBrain
					&& zombieCorpse.Spawned,
				actionMode,
				tick = GenTicks.TicksGame,
				patchTargets = InfectionWorkflowPatchTargets(),
				pawns = new
				{
					doctor = DescribePawn(doctor),
					hidden = DescribePawn(hidden),
					curePatient = DescribePawn(curePatient),
					infecting = DescribePawn(infecting)
				},
				infections = new
				{
					hidden = ZombieRuntimeActions.DescribePawnInfection(hidden),
					curePatient = ZombieRuntimeActions.DescribePawnInfection(curePatient),
					infecting = ZombieRuntimeActions.DescribePawnInfection(infecting)
				},
				corpses = new
				{
					countdown = DescribeCorpse(countdownCorpse),
					doubleTap = DescribeCorpse(doubleTapCorpse),
					zombie = DescribeCorpse(zombieCorpse)
				},
				conversion = new
				{
					queueCount = conversionQueue?.Count ?? -1,
					countdown,
					doubleTap
				},
				alerts,
				extract = new
				{
					stackCount = CountWorkflowExtract(map),
					zombieCorpseTracked = map.GetComponent<TickManager>()?.allZombieCorpses?.Contains(zombieCorpse) ?? false
				}
			};
		}

		static object ExecuteInfectionWorkflow(Map map)
		{
			var before = ReadInfectionWorkflow(map, "before-execute");
			var doctor = FindWorkflowPawn(map, InfectionWorkflowDoctorName);
			var curePatient = FindWorkflowPawn(map, InfectionWorkflowCureName);
			var countdownCorpse = FindWorkflowCorpse(map, InfectionWorkflowCountdownName);
			var doubleTapCorpse = FindWorkflowCorpse(map, InfectionWorkflowDoubleTapName);
			var zombieCorpse = FindWorkflowCorpse(map, InfectionWorkflowZombieCorpseName) as ZombieCorpse;
			if (doctor == null || curePatient == null || countdownCorpse == null || doubleTapCorpse == null || zombieCorpse == null)
			{
				return new
				{
					success = false,
					actionMode = "execute",
					before,
					error = "The infection workflow fixture is incomplete. Run setup and save/load before execute."
				};
			}

			var cure = ExecuteWorkflowCure(doctor, curePatient);
			var doubleTap = ExecuteWorkflowDoubleTap(doctor, doubleTapCorpse);
			var extract = ExecuteWorkflowExtract(doctor, zombieCorpse);
			var conversion = ExecuteWorkflowCountdownConversion(map, countdownCorpse);
			var after = ReadInfectionWorkflowAfterExecute(map);

			return new
			{
				success = ObjectSuccess(before)
					&& ObjectSuccess(cure)
					&& ObjectSuccess(doubleTap)
					&& ObjectSuccess(extract)
					&& ObjectSuccess(conversion)
					&& ObjectSuccess(after),
				actionMode = "execute",
				before,
				cure,
				doubleTap,
				extract,
				conversion,
				after
			};
		}

		static object ReadInfectionWorkflowAfterExecute(Map map)
		{
			var doctor = FindWorkflowPawn(map, InfectionWorkflowDoctorName);
			var hidden = FindWorkflowPawn(map, InfectionWorkflowHiddenName);
			var curePatient = FindWorkflowPawn(map, InfectionWorkflowCureName);
			var infecting = FindWorkflowPawn(map, InfectionWorkflowInfectingName);
			var countdownCorpse = FindWorkflowCorpse(map, InfectionWorkflowCountdownName);
			var doubleTapCorpse = FindWorkflowCorpse(map, InfectionWorkflowDoubleTapName);
			var zombieCorpse = FindWorkflowCorpse(map, InfectionWorkflowZombieCorpseName);
			var converted = CurrentZombies(map)
				.Where(zombie => zombie.Name?.ToStringShort == InfectionWorkflowCountdownName)
				.Select(DescribeZombie)
				.ToArray();
			var alerts = DescribeInfectionAlerts();

			return new
			{
				success = doctor != null
					&& hidden != null
					&& curePatient != null
					&& infecting != null
					&& countdownCorpse == null
					&& doubleTapCorpse != null
					&& zombieCorpse == null
					&& converted.Length == 1
					&& alerts.hidden.count == 1
					&& alerts.imminent.count == 0
					&& alerts.infecting.count == 1,
				actionMode = "after-execute",
				pawns = new
				{
					doctor = DescribePawn(doctor),
					hidden = DescribePawn(hidden),
					curePatient = DescribePawn(curePatient),
					infecting = DescribePawn(infecting)
				},
				infections = new
				{
					hidden = ZombieRuntimeActions.DescribePawnInfection(hidden),
					curePatient = ZombieRuntimeActions.DescribePawnInfection(curePatient),
					infecting = ZombieRuntimeActions.DescribePawnInfection(infecting)
				},
				corpses = new
				{
					countdown = DescribeCorpse(countdownCorpse),
					doubleTap = DescribeCorpse(doubleTapCorpse),
					zombie = DescribeCorpse(zombieCorpse)
				},
				converted,
				alerts,
				extractStackCount = CountWorkflowExtract(map)
			};
		}

		static object ExecuteWorkflowCure(Pawn doctor, Pawn patient)
		{
			var recipe = CustomDefs.CureZombieInfection;
			var worker = recipe?.Worker;
			var serumDef = DefDatabase<ThingDef>.GetNamed("ZombieSerumSimple", false);
			var partsBefore = worker?.GetPartsToApplyOn(patient, recipe).ToArray() ?? Array.Empty<BodyPartRecord>();
			if (recipe == null || worker == null || serumDef == null || partsBefore.Length == 0)
			{
				return new
				{
					success = false,
					doctor = DescribePawn(doctor),
					patient = DescribePawn(patient),
					recipeFound = recipe != null,
					workerFound = worker != null,
					serumFound = serumDef != null,
					curablePartCount = partsBefore.Length,
					error = "The workflow cure step could not find a recipe worker, serum, or curable bite part."
				};
			}

			var infectionBefore = ZombieRuntimeActions.DescribePawnInfection(patient);
			var part = partsBefore.First();
			var serum = ThingMaker.MakeThing(serumDef);
			worker.ApplyOnPawn(patient, part, doctor, new List<Thing> { serum }, null);
			var bites = patient.GetHediffsList<Hediff_Injury_ZombieBite>().ToArray();
			foreach (var bite in bites)
				SetPawnInfectionStateFromBite(patient, bite);
			var partsAfter = worker.GetPartsToApplyOn(patient, recipe).ToArray();
			var infectionAfter = ZombieRuntimeActions.DescribePawnInfection(patient);
			var alertsAfter = DescribeInfectionAlerts();
			var patientId = ZombieRuntimeActions.StableThingId(patient);
			var patientStillAlerted = alertsAfter.hidden.ids.Contains(patientId)
				|| alertsAfter.imminent.ids.Contains(patientId)
				|| alertsAfter.infecting.ids.Contains(patientId);

			return new
			{
				success = partsBefore.Length > 0
					&& partsAfter.Length == 0
					&& patientStillAlerted == false
					&& bites.All(bite => bite.TendDuration?.GetInfectionState() == InfectionState.BittenHarmless)
					&& bites.All(bite => bite.mayBecomeZombieWhenDead == false),
				doctor = DescribePawn(doctor),
				patient = DescribePawn(patient),
				curedPart = DescribeBodyPart(part),
				curablePartCountBefore = partsBefore.Length,
				curablePartCountAfter = partsAfter.Length,
				infectionBefore,
				infectionAfter,
				bites = bites.Select(DescribeWorkflowBite).ToArray(),
				patientStillAlerted,
				alertsAfter
			};
		}

		static object ExecuteWorkflowDoubleTap(Pawn actor, Corpse corpse)
		{
			var brainBefore = corpse?.InnerPawn?.health?.hediffSet?.GetBrain();
			var queue = actor?.Map?.GetComponent<TickManager>()?.colonistsToConvert;
			var jobResult = RunWorkflowJob(
				actor,
				corpse,
				new WorkGiver_DoubleTap(),
				2 + (int)Math.Ceiling(100f / (Math.Max(0.1f, actor.GetStatValue(StatDefOf.MeleeDPS, true)) * 4f) + 1f) * 80,
				tick => corpse?.InnerPawn?.health?.hediffSet?.GetBrain() == null);
			var brainAfter = corpse?.InnerPawn?.health?.hediffSet?.GetBrain();
			var queueBeforeTrigger = queue?.Count ?? -1;
			SetCorpseConversionTicks(corpse, GenTicks.TicksGame - 1);
			corpse?.TickRare();
			var queueAfterTrigger = queue?.Count ?? -1;
			var queuedAfterTrigger = queue?.Contains(corpse) ?? false;

			return new
			{
				success = ObjectSuccess(jobResult)
					&& brainBefore != null
					&& brainAfter == null
					&& queuedAfterTrigger == false
					&& queueAfterTrigger == queueBeforeTrigger,
				actor = DescribePawn(actor),
				corpse = DescribeCorpse(corpse),
				brainBefore = brainBefore?.def?.defName,
				brainAfter = brainAfter?.def?.defName,
				jobResult,
				queueBeforeTrigger,
				queueAfterTrigger,
				queuedAfterTrigger,
				conversion = DescribeWorkflowCorpseConversion(corpse, queue)
			};
		}

		static object ExecuteWorkflowExtract(Pawn actor, ZombieCorpse corpse)
		{
			var map = actor?.Map;
			TrackZombieCorpse(map, corpse);
			var before = CountWorkflowExtract(map);
			var medicineBefore = actor?.skills?.GetSkill(SkillDefOf.Medicine);
			var medicineLevelBefore = medicineBefore?.Level ?? -1;
			var medicineXpBefore = medicineBefore?.xpSinceLastLevel ?? 0f;
			var jobResult = RunWorkflowJob(
				actor,
				corpse,
				new WorkGiver_ExtractZombieSerum(),
				120 + (int)Math.Ceiling(100f / (Math.Max(0.1f, actor.GetStatValue(StatDefOf.MedicalTendSpeed, true)) / 2f)),
				tick => (corpse?.Destroyed ?? true) || corpse?.Spawned == false || CountWorkflowExtract(map) > before);
			var after = CountWorkflowExtract(map);
			var corpseGone = corpse == null || corpse.Destroyed || corpse.Spawned == false;
			var medicineAfter = actor?.skills?.GetSkill(SkillDefOf.Medicine);
			var medicineLevelAfter = medicineAfter?.Level ?? -1;
			var medicineXpAfter = medicineAfter?.xpSinceLastLevel ?? 0f;
			var medicineImproved = medicineLevelAfter > medicineLevelBefore
				|| (medicineLevelAfter == medicineLevelBefore && medicineXpAfter > medicineXpBefore + 0.01f);

			return new
			{
				success = ObjectSuccess(jobResult)
					&& corpseGone
					&& after > before
					&& medicineImproved,
				actor = DescribePawn(actor),
				corpse = DescribeCorpse(corpse),
				jobResult,
				extractBefore = before,
				extractAfter = after,
				extractDelta = after - before,
				medicine = new
				{
					levelBefore = medicineLevelBefore,
					levelAfter = medicineLevelAfter,
					xpSinceLastLevelBefore = medicineXpBefore,
					xpSinceLastLevelAfter = medicineXpAfter,
					improved = medicineImproved
				},
				expectedExtractPerZombie = Tools.ExtractPerZombie(),
				corpseGone
			};
		}

		static object ExecuteWorkflowCountdownConversion(Map map, Corpse corpse)
		{
			var before = CurrentZombies(map).Select(ZombieRuntimeActions.StableThingId).ToHashSet();
			var persisted = DescribeWorkflowCorpseConversion(corpse, map.GetComponent<TickManager>()?.colonistsToConvert);
			var ticksBefore = SetCorpseConversionTicks(corpse, GenTicks.TicksGame - 1);
			var queue = map.GetComponent<TickManager>()?.colonistsToConvert;
			var queueCountBeforeTickRare = queue?.Count ?? -1;
			corpse?.TickRare();
			var queueCountAfterTickRare = queue?.Count ?? -1;
			var queuedAfterTickRare = queue?.Contains(corpse) ?? false;
			var convertedQueuedCorpse = ZombieRuntimeActions.RunQueuedConversion(map, corpse, out var queueCountBeforeRun, out var queueCountAfterRun, out var error);
			var newZombies = CurrentZombies(map)
				.Where(zombie => before.Contains(ZombieRuntimeActions.StableThingId(zombie)) == false)
				.Select(DescribeZombie)
				.ToArray();

			return new
			{
				success = persisted.infectionCount == 1
					&& persisted.ticksUntilConversion > 0
					&& queuedAfterTickRare
					&& convertedQueuedCorpse
					&& newZombies.Length == 1,
				persisted,
				ticksBefore,
				queueCountBeforeTickRare,
				queueCountAfterTickRare,
				queuedAfterTickRare,
				convertedQueuedCorpse,
				queueCountBeforeRun,
				queueCountAfterRun,
				error,
				newZombieCount = newZombies.Length,
				newZombies
			};
		}

		static object VerifyWorkflowCountdownPauseDuringSilence(Map map)
		{
			var manager = ZombieFreeEventManager.Current;
			var corpse = FindWorkflowCorpse(map, InfectionWorkflowCountdownName);
			var queue = map.GetComponent<TickManager>()?.colonistsToConvert;
			if (manager == null || corpse == null || queue == null)
			{
				return new
				{
					success = false,
					actionMode = "silencePause",
					managerPresent = manager != null,
					countdownCorpse = DescribeCorpse(corpse),
					queuePresent = queue != null,
					error = "The countdown corpse, conversion queue, or ZombieFreeEventManager is missing. Run setup first."
				};
			}

			var settingsSnapshot = SnapshotZombieSettings();
			if (TrySnapshotZombieFreeSchedule(manager, out var scheduleSnapshot, out var scheduleSnapshotError) == false)
			{
				return new
				{
					success = false,
					actionMode = "silencePause",
					error = scheduleSnapshotError
				};
			}
			var initialZombieIds = CurrentZombies(map)
				.Select(ZombieRuntimeActions.StableThingId)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
			try
			{
				ApplyZombieSettingsOverride(settings =>
				{
					settings.showZombieEventLetters = false;
					settings.useDynamicThreatLevel = false;
					settings.daysBeforeZombiesCome = 0;
					settings.threatScale = Math.Max(settings.threatScale, 1f);
					settings.zombieFreeEvents = true;
				});

				manager.DebugClearSchedule();
				var forcedWindow = manager.DebugForceWindowStartingNow(GenDate.TicksPerDay);
				var setTicks = SetCorpseConversionTicks(corpse, GenTicks.TicksGame - 1);
				var before = DescribeWorkflowCorpseConversion(corpse, queue);
				var targetQueuedBefore = queue.Contains(corpse);
				var queueCountBefore = queue.Count;
				corpse.TickRare();
				var after = DescribeWorkflowCorpseConversion(corpse, queue);
				var queueCountAfter = queue.Count;
				var pausedByTicks = before.ticksWhenBecomingZombie.Length == 0 || after.ticksWhenBecomingZombie.Length == 0
					? 0
					: after.ticksWhenBecomingZombie[0] - before.ticksWhenBecomingZombie[0];
				var newZombies = CurrentZombies(map)
					.Where(zombie => initialZombieIds.Contains(ZombieRuntimeActions.StableThingId(zombie)) == false)
					.Select(DescribeZombie)
					.ToArray();

				return new
				{
					success = ZombieFreeEventManager.IsActiveNow()
						&& setTicks.infectionCount == 1
						&& before.hasBrain
						&& before.ticksUntilConversion < 0
						&& targetQueuedBefore == false
						&& after.queued == false
						&& queueCountAfter == queueCountBefore
						&& pausedByTicks > 0
						&& corpse.Destroyed == false
						&& newZombies.Length == 0,
					actionMode = "silencePause",
					activeNow = ZombieFreeEventManager.IsActiveNow(),
					forcedWindow = new
					{
						offsetStartTicks = forcedWindow.startTick - GenTicks.TicksGame,
						offsetEndTicks = forcedWindow.endTick - GenTicks.TicksGame,
						forcedWindow.DurationTicks
					},
					setTicks,
					before,
					after,
					pausedByTicks,
					targetQueuedBefore,
					queueCountBefore,
					queueCountAfter,
					corpseDestroyed = corpse.Destroyed,
					newZombieCount = newZombies.Length,
					newZombies,
					sourcePath = "Corpse.TickRare postfix -> ZombieFreeEventManager.IsActiveNow -> Hediff_ZombieInfection.ticksWhenBecomingZombie pause"
				};
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
				RestoreZombieFreeSchedule(manager, scheduleSnapshot);
			}
		}

		static object RunWorkflowJob(Pawn actor, Thing target, WorkGiver_Scanner workGiver, int maxTicks, Func<int, bool> done)
		{
			if (actor == null || target == null || workGiver == null)
			{
				return new
				{
					success = false,
					actor = DescribePawn(actor),
					target = StableId(target),
					error = "RunWorkflowJob requires an actor, target, and workgiver."
				};
			}

			actor.pather?.StopDead();
			actor.jobs?.EndCurrentJob(JobCondition.InterruptForced);
			var hasForcedJob = workGiver.HasJobOnThing(actor, target, true);
			var job = hasForcedJob ? workGiver.JobOnThing(actor, target, true) : null;
			if (job == null)
			{
				return new
				{
					success = false,
					actor = DescribePawn(actor),
					target = StableId(target),
					workGiver = workGiver.GetType().Name,
					hasForcedJob,
					error = "The workflow workgiver did not create a forced job."
				};
			}

			job.playerForced = true;
			var jobDefName = job.def?.defName;
			var samples = new List<object>();
			actor.jobs.StartJob(job, JobCondition.InterruptForced, null, true, true);
			var startedJob = actor.CurJobDef?.defName;
			var tickHit = -1;
			for (var tick = 1; tick <= maxTicks; tick++)
			{
				AdvanceGameTicks(1);
				var complete = done?.Invoke(tick) ?? false;
				if (tick == 1 || tick == maxTicks || tick % 80 == 0 || complete)
				{
					samples.Add(new
					{
						tick,
						job = actor.CurJobDef?.defName,
						targetSpawned = target.Spawned,
						targetDestroyed = target.Destroyed
					});
				}
				if (complete)
				{
					tickHit = tick;
					break;
				}
			}

			return new
			{
				success = hasForcedJob && startedJob == jobDefName && tickHit > 0,
				actor = DescribePawn(actor),
				target = StableId(target),
				workGiver = workGiver.GetType().Name,
				hasForcedJob,
				jobDef = jobDefName,
				startedJob,
				maxTicks,
				tickHit,
				samples
			};
		}

		static (int infectionCount, int[] ticksWhenBecomingZombie) SetCorpseConversionTicks(Corpse corpse, int ticks)
		{
			var infections = new List<Hediff_ZombieInfection>();
			corpse?.InnerPawn?.health?.hediffSet?.GetHediffs(ref infections);
			foreach (var infection in infections)
				infection.ticksWhenBecomingZombie = ticks;
			return (infections.Count, infections.Select(infection => infection.ticksWhenBecomingZombie).ToArray());
		}

		static WorkflowCorpseConversionSnapshot DescribeWorkflowCorpseConversion(Corpse corpse, Queue<ThingWithComps> queue)
		{
			var infections = new List<Hediff_ZombieInfection>();
			corpse?.InnerPawn?.health?.hediffSet?.GetHediffs(ref infections);
			var ticks = infections.Select(infection => infection.ticksWhenBecomingZombie).ToArray();
			var firstTick = ticks.Length == 0 ? -1 : ticks.Min();
			return new WorkflowCorpseConversionSnapshot
			{
				infectionCount = infections.Count,
				ticksWhenBecomingZombie = ticks,
				ticksUntilConversion = firstTick < 0 ? -1 : firstTick - GenTicks.TicksGame,
				hasBrain = corpse?.InnerPawn?.health?.hediffSet?.GetBrain() != null,
				queued = queue?.Contains(corpse) ?? false
			};
		}

		static WorkflowAlertsSnapshot DescribeInfectionAlerts()
		{
			var hidden = DescribeWorkflowAlert(new Alert_ColonistsBittenByZombie());
			var imminent = DescribeWorkflowAlert(new Alert_ImminentZombiInfection());
			var infecting = DescribeWorkflowAlert(new Alert_ZombieInfection());
			var tracked = Patches.Need_CurLevel_Patch.infectedColonists ?? new HashSet<Pawn>();
			return new WorkflowAlertsSnapshot
			{
				hidden = hidden,
				imminent = imminent,
				infecting = infecting,
				needPatchTracked = tracked
					.Where(pawn => IsWorkflowPawnName(pawn?.Name?.ToStringShort))
					.Select(ZombieRuntimeActions.StableThingId)
					.ToArray()
			};
		}

		static WorkflowAlertSnapshot DescribeWorkflowAlert(Alert_ZombieInfectionProgress alert)
		{
			var affected = alert.AffectedColonists
				.Where(pawn => IsWorkflowPawnName(pawn?.Name?.ToStringShort))
				.ToArray();
			var report = alert.GetReport();
			return new WorkflowAlertSnapshot
			{
				label = alert.GetLabel().ToString(),
				active = report.active,
				count = affected.Length,
				ids = affected.Select(ZombieRuntimeActions.StableThingId).ToArray(),
				names = affected.Select(pawn => pawn.Name?.ToStringShort).ToArray(),
				explanation = alert.GetExplanation().ToString().Trim()
			};
		}

		static object DescribeWorkflowBite(Hediff_Injury_ZombieBite bite)
		{
			return new
			{
				hediffDef = bite?.def?.defName,
				part = DescribeBodyPart(bite?.Part),
				state = bite?.TendDuration?.GetInfectionState().ToString(),
				bite?.mayBecomeZombieWhenDead
			};
		}

		static object[] InfectionWorkflowPatchTargets()
		{
			return PatchedMethodsForPatchClass("Pawn_Kill_Patch")
				.Concat(PatchedMethodsForPatchClass("Corpse_TickRare_Patch"))
				.Concat(PatchedMethodsForPatchClass("Corpse_RotStageChanged_Patch"))
				.Concat(PatchedMethodsForPatchClass("HealthCardUtility_DrawOverviewTab_Patch"))
				.Where(target => target != null)
				.Distinct()
				.OrderBy(target => target.ToString())
				.ToArray();
		}

		static object InfectionWorkflowFailure(string stage, string error, List<Thing> spawned)
		{
			return new
			{
				success = false,
				stage,
				error,
				spawned = spawned
					.Where(thing => thing != null)
					.Select(StableId)
					.ToArray()
			};
		}

		static Pawn CreateWorkflowColonist(Map map, IntVec3 cell, string name, bool doctor)
		{
			var request = new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer, forceGenerateNewPawn: true, canGeneratePawnRelations: false, allowAddictions: false);
			Pawn pawn = null;
			for (var attempt = 0; attempt < 25; attempt++)
			{
				pawn = PawnGenerator.GeneratePawn(request);
				if (doctor == false || pawn.WorkTypeIsDisabled(WorkTypeDefOf.Doctor) == false)
					break;
				pawn = null;
			}
			pawn ??= PawnGenerator.GeneratePawn(request);
			pawn.Name = new NameSingle(name);
			GenSpawn.Spawn(pawn, cell, map, WipeMode.Vanish);
			pawn.workSettings?.EnableAndInitializeIfNotAlreadyInitialized();
			if (doctor)
			{
				if (pawn.WorkTypeIsDisabled(WorkTypeDefOf.Doctor) == false)
					pawn.workSettings?.SetPriority(WorkTypeDefOf.Doctor, 1);
				pawn.skills?.Learn(SkillDefOf.Medicine, 8000f, true);
			}
			else
				DisablePawnWork(pawn);
			return pawn;
		}

		static Pawn SpawnWorkflowColonistNear(Map map, IntVec3 root, string name, List<Thing> spawned)
		{
			var cell = FindWorkflowCell(map, root);
			var pawn = CreateWorkflowColonist(map, cell, name, false);
			spawned.Add(pawn);
			return pawn;
		}

		static void ClearWorkflowApparel(Pawn pawn)
		{
			pawn?.apparel?.DestroyAll(DestroyMode.Vanish);
		}

		static bool AddWorkflowZombieBite(Pawn pawn, string stage, out Hediff_Injury_ZombieBite bite, out string error)
		{
			bite = null;
			error = null;
			if (pawn == null || pawn is Zombie || pawn is ZombieSymbiant || pawn is ZombieSpitter)
			{
				error = "Target must be a non-zombie pawn.";
				return false;
			}

			var part = pawn.health?.hediffSet?
				.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Outside)
				.Where(candidate => candidate.def.IsSolid(candidate, pawn.health.hediffSet.hediffs) == false)
				.Where(candidate => candidate.def.defName != "Waist")
				.OrderBy(candidate => WorkflowBitePartRank(candidate))
				.FirstOrDefault();
			if (part == null)
			{
				error = "No valid workflow bite part was found.";
				return false;
			}

			bite = (Hediff_Injury_ZombieBite)HediffMaker.MakeHediff(HediffDef.Named("ZombieBite"), pawn, part);
			if (bite.TendDuration?.ZombieInfector == null)
			{
				error = "Zombie bite hediff has no infector comp.";
				return false;
			}

			bite.mayBecomeZombieWhenDead = true;
			ZombieRuntimeActions.ApplyBiteStage(bite, stage);
			pawn.health.AddHediff(bite, part, null);
			ZombieRuntimeActions.ApplyBiteStage(bite, stage);
			return true;
		}

		static int WorkflowBitePartRank(BodyPartRecord part)
		{
			var name = part?.def?.defName ?? "";
			if (name.Contains("Finger"))
				return 0;
			if (name.Contains("Toe"))
				return 1;
			if (name == "Ear" || name == "Nose")
				return 2;
			return 10;
		}

		static IntVec3 FindWorkflowCell(Map map, IntVec3 root)
		{
			if (TryFindClearSpawnCell(map, root, 12f, out var cell, out _) == false)
				throw new InvalidOperationException($"Could not find clear workflow cell near ({root.x}, {root.z}).");
			return cell;
		}

		static Pawn FindWorkflowPawn(Map map, string name)
		{
			return map?.mapPawns?.AllPawnsSpawned?
				.FirstOrDefault(pawn => pawn.Name?.ToStringShort == name);
		}

		static Corpse FindWorkflowCorpse(Map map, string innerPawnName)
		{
			return map?.listerThings?.AllThings?
				.OfType<Corpse>()
				.FirstOrDefault(corpse => corpse.InnerPawn?.Name?.ToStringShort == innerPawnName);
		}

		static bool IsWorkflowPawnName(string name)
		{
			return string.IsNullOrWhiteSpace(name) == false && name.StartsWith(InfectionWorkflowPrefix, StringComparison.Ordinal);
		}

		static void SetPawnInfectionStateFromBite(Pawn pawn, Hediff_Injury_ZombieBite bite)
		{
			pawn?.SetInfectionState(bite?.TendDuration?.GetInfectionState() ?? InfectionState.None);
		}

		static int CountWorkflowExtract(Map map)
		{
			return map?.listerThings?.AllThings?
				.Where(thing => thing.def == CustomDefs.ZombieExtract)
				.Sum(thing => thing.stackCount) ?? 0;
		}

		static void TrackZombieCorpse(Map map, ZombieCorpse corpse)
		{
			var corpses = map?.GetComponent<TickManager>()?.allZombieCorpses;
			if (corpse != null && corpses != null && corpses.Contains(corpse) == false)
				corpses.Add(corpse);
		}

		static void CleanupInfectionWorkflow(Map map)
		{
			if (map == null)
				return;

			foreach (var pawn in map.mapPawns.AllPawnsSpawned
				.Where(pawn => IsWorkflowPawnName(pawn.Name?.ToStringShort))
				.ToArray())
				pawn.Destroy(DestroyMode.Vanish);

			foreach (var corpse in map.listerThings.AllThings
				.OfType<Corpse>()
				.Where(corpse => IsWorkflowPawnName(corpse.InnerPawn?.Name?.ToStringShort))
				.ToArray())
				corpse.Destroy(DestroyMode.Vanish);

			foreach (var extract in map.listerThings.AllThings
				.Where(thing => thing.def == CustomDefs.ZombieExtract && thing.Position.DistanceToSquared(new IntVec3(map.Size.x / 2, 0, map.Size.z / 2)) < 1600)
				.ToArray())
				extract.Destroy(DestroyMode.Vanish);

			var tickManager = map.GetComponent<TickManager>();
			if (tickManager?.colonistsToConvert != null)
			{
				var kept = tickManager.colonistsToConvert
					.Where(thing => thing is not Corpse corpse || IsWorkflowPawnName(corpse.InnerPawn?.Name?.ToStringShort) == false)
					.ToArray();
				tickManager.colonistsToConvert.Clear();
				foreach (var thing in kept)
					tickManager.colonistsToConvert.Enqueue(thing);
			}
			tickManager?.allZombieCorpses?.RemoveAll(corpse => IsWorkflowPawnName(corpse?.InnerPawn?.Name?.ToStringShort));
		}

		[Tool("zombieland/cure_zombie_infection_recipe", Description = "Apply the real cure-infection recipe worker with 100% serum and verify the cured corpse no longer queues conversion.")]
		public static object CureZombieInfectionRecipe(string serumDefName = "ZombieSerumSimple")
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
			if (TryFindClearSpawnCell(map, root, 16f, out var doctorCell, out var doctorSpawnError) == false)
				return doctorSpawnError;

			var doctor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(doctor, doctorCell, map, WipeMode.Vanish);
			if (TryFindAdjacentClearCell(doctor, out var patientCell) == false
				&& TryFindClearSpawnCell(map, doctor.Position, 8f, out patientCell, out var patientSpawnError) == false)
				return patientSpawnError;

			var patient = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(patient, patientCell, map, WipeMode.Vanish);

			if (ZombieRuntimeActions.AddZombieBite(patient, "harmful", out var bite, out var error) == false)
			{
				return new
				{
					success = false,
					patient = DescribePawn(patient),
					error
				};
			}

			var recipe = CustomDefs.CureZombieInfection;
			var worker = recipe?.Worker;
			var partsBefore = worker?.GetPartsToApplyOn(patient, recipe).ToArray() ?? Array.Empty<BodyPartRecord>();
			var serumDef = DefDatabase<ThingDef>.GetNamed(serumDefName, false);
			if (recipe == null || worker == null || serumDef == null || partsBefore.Length == 0)
			{
				return new
				{
					success = false,
					doctor = DescribePawn(doctor),
					patient = DescribePawn(patient),
					infection = ZombieRuntimeActions.DescribePawnInfection(patient),
					recipeFound = recipe != null,
					workerFound = worker != null,
					serumFound = serumDef != null,
					curablePartCount = partsBefore.Length,
					error = "The cure recipe fixture could not find a recipe worker, serum, or curable bite part."
				};
			}

			var part = partsBefore.First();
			var serum = ThingMaker.MakeThing(serumDef);
			var infectionBefore = ZombieRuntimeActions.DescribePawnInfection(patient);
			worker.ApplyOnPawn(patient, part, doctor, new List<Thing> { serum }, null);
			var infectionAfter = ZombieRuntimeActions.DescribePawnInfection(patient);
			var partsAfter = worker.GetPartsToApplyOn(patient, recipe).ToArray();
			var biteStateAfter = bite.TendDuration?.GetInfectionState().ToString();
			var mayBecomeZombieWhenDeadAfter = bite.mayBecomeZombieWhenDead;

			if (ZombieRuntimeActions.KillPawnToCorpse(patient, out var corpse, out error) == false)
			{
				return new
				{
					success = false,
					doctor = DescribePawn(doctor),
					patient = DescribePawn(patient),
					infectionBefore,
					infectionAfter,
					error
				};
			}

			var queue = map.GetComponent<TickManager>()?.colonistsToConvert;
			var queueCountBeforeRot = queue?.Count ?? -1;
			var queuedBeforeRot = queue?.Contains(corpse) ?? false;
			var rotTriggered = ZombieRuntimeActions.TriggerCorpseRotStageChanged(corpse, out var rotStageBefore, out var rotStageAfter, out error);
			var queueCountAfterRot = queue?.Count ?? -1;
			var queuedAfterRot = queue?.Contains(corpse) ?? false;

			return new
			{
				success = partsBefore.Length > 0
					&& partsAfter.Length == 0
					&& mayBecomeZombieWhenDeadAfter == false
					&& rotTriggered
					&& queuedBeforeRot == false
					&& queuedAfterRot == false,
				doctor = DescribePawn(doctor),
				patientCorpse = DescribeCorpse(corpse),
				doctorCell = ZombieRuntimeActions.DescribeCell(doctorCell),
				patientCell = ZombieRuntimeActions.DescribeCell(patientCell),
				biteLabel = bite.LabelCap,
				curedPart = part.def?.defName,
				infectionBefore,
				infectionAfter,
				biteStateAfter,
				mayBecomeZombieWhenDeadAfter,
				curablePartCountBefore = partsBefore.Length,
				curablePartCountAfter = partsAfter.Length,
				serumDef = serum.def.defName,
				serumPurity = Tools.ZombieSerumPurity(serum.def),
				rotTriggered,
				rotStageBefore = rotStageBefore.ToString(),
				rotStageAfter = rotStageAfter.ToString(),
				rotError = error,
				queueCountBeforeRot,
				queueCountAfterRot,
				queuedBeforeRot,
				queuedAfterRot
			};
		}

		sealed class MedicalHealCase
		{
			public string name { get; set; }
			public string hediffDef { get; set; }
			public string part { get; set; }
			public float severity { get; set; }
			public string infectionState { get; set; }
			public bool canHealNaturally { get; set; }
		}

		sealed class MedicalRemovalCase
		{
			public string name { get; set; }
			public string hediffDef { get; set; }
			public string part { get; set; }
			public float severity { get; set; }
			public string infectionState { get; set; }
			public bool shouldRemove { get; set; }
		}

		static object InfectionMedicalFailure(Pawn patient, Pawn animal, string error)
		{
			return new
			{
				success = false,
				patient = DescribePawn(patient),
				animal = DescribePawn(animal),
				error
			};
		}

		static void ForceHiddenBite(Hediff_Injury_ZombieBite bite)
		{
			var infector = bite?.TendDuration?.ZombieInfector;
			if (infector == null)
				return;

			infector.infectionKnownDelay = GenTicks.TicksAbs + GenDate.TicksPerHour;
			infector.infectionStartTime = infector.infectionKnownDelay + GenDate.TicksPerHour;
			infector.infectionEndTime = infector.infectionStartTime + GenDate.TicksPerHour;
		}

		static Hediff_Injury CreateOrdinaryCut(Pawn pawn)
		{
			var part = pawn.health.hediffSet
				.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Outside)
				.Where(part => part.def.IsSolid(part, pawn.health.hediffSet.hediffs) == false)
				.FirstOrDefault();
			if (part == null)
				return null;

			var cut = (Hediff_Injury)HediffMaker.MakeHediff(HediffDefOf.Cut, pawn, part);
			cut.Severity = 2f;
			pawn.health.AddHediff(cut, part, new DamageInfo(DamageDefOf.Cut, 2f));
			return cut;
		}

		static (Hediff_Injury_ZombieBite bite, object description) CreateAnimalBiteCase(Map map, IntVec3 near, List<Pawn> spawnedPawns)
		{
			var animalKind = DefDatabase<PawnKindDef>.AllDefs
				.FirstOrDefault(kind => kind.race?.race?.Animal == true && kind.race.race.IsFlesh);
			if (animalKind == null)
			{
				return (null, new
				{
					success = false,
					reason = "No flesh animal pawn kind was available."
				});
			}
			if (TryFindClearSpawnCell(map, near + new IntVec3(3, 0, 0), 10f, out var animalCell, out var cellError) == false)
			{
				return (null, new
				{
					success = false,
					reason = "No animal spawn cell was available.",
					cellError
				});
			}

			var animal = PawnGenerator.GeneratePawn(animalKind);
			GenSpawn.Spawn(animal, animalCell, map, WipeMode.Vanish);
			spawnedPawns.Add(animal);
			var part = animal.health.hediffSet
				.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Outside)
				.Where(part => part.def.IsSolid(part, animal.health.hediffSet.hediffs) == false)
				.FirstOrDefault();
			if (part == null)
			{
				return (null, new
				{
					success = false,
					animal = DescribePawn(animal),
					reason = "The animal had no valid non-solid bite part."
				});
			}

			var bite = (Hediff_Injury_ZombieBite)HediffMaker.MakeHediff(HediffDef.Named("ZombieBite"), animal, part);
			bite.Severity = 2f;
			animal.health.AddHediff(bite, part, new DamageInfo(CustomDefs.ZombieBite, 2f));
			return (bite, new
			{
				success = true,
				animal = DescribePawn(animal),
				part = DescribeBodyPart(part),
				infectionState = bite.TendDuration?.GetInfectionState().ToString()
			});
		}

		static MedicalHealCase DescribeNaturalHealingCase(string name, Hediff_Injury hediff)
		{
			var bite = hediff as Hediff_Injury_ZombieBite;
			return new MedicalHealCase
			{
				name = name,
				hediffDef = hediff?.def?.defName,
				part = DescribeBodyPart(hediff?.Part),
				severity = hediff?.Severity ?? 0f,
				infectionState = bite?.TendDuration?.GetInfectionState().ToString(),
				canHealNaturally = hediff != null && HediffUtility.CanHealNaturally(hediff)
			};
		}

		static MedicalRemovalCase DescribeShouldRemoveCase(string name, Hediff_Injury hediff)
		{
			var bite = hediff as Hediff_Injury_ZombieBite;
			return new MedicalRemovalCase
			{
				name = name,
				hediffDef = hediff?.def?.defName,
				part = DescribeBodyPart(hediff?.Part),
				severity = hediff?.Severity ?? 0f,
				infectionState = bite?.TendDuration?.GetInfectionState().ToString(),
				shouldRemove = hediff?.ShouldRemove ?? false
			};
		}

		static string DescribeBodyPart(BodyPartRecord part)
		{
			return part == null ? null : $"{part.def?.defName}:{part.Label}";
		}

		[Tool("zombieland/convert_pawn_to_zombie", Description = "Convert a spawned non-zombie pawn to a Zombieland zombie and return before/after state for smoke tests.")]
		public static object ConvertPawnToZombie(
		[ToolParameter(Description = "Pawn id, ThingID, label, or short name.", Required = true)] string target,
		[ToolParameter(Description = "Pass true to force conversion even if the pawn normally would not convert.", Required = false, DefaultValue = true)] bool force = true)
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

			ZombieRuntimeActions.ConvertPawnToZombie(pawn, map, force);

			var after = CurrentZombies(map);
			var newZombies = after
				.Where(zombie => beforeIds.Contains(ZombieRuntimeActions.StableThingId(zombie)) == false)
				.Select(DescribeZombie)
				.ToArray();

			return new
			{
				success = newZombies.Length > 0,
				targetId,
				targetThingId,
				targetLabel,
				force,
				beforeCount = before.Length,
				afterCount = after.Length,
				newZombieCount = newZombies.Length,
				newZombies
			};
		}

	}
}
