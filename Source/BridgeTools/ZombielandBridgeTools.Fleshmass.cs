using HarmonyLib;
using RimBridgeServer.Sdk;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public sealed partial class ZombielandBridgeTools
	{
		sealed class FleshmassContractCase
		{
			public string id { get; set; }
			public bool success { get; set; }
			public object details { get; set; }
		}

		static readonly MethodInfo canSmashMethod = AccessTools.Method(typeof(ZombieStateHandler), "CanSmash", new[] { typeof(Zombie) });
		static readonly MethodInfo canSmashBuildingMethod = AccessTools.Method(typeof(ZombieStateHandler), "CanSmashBuilding", new[] { typeof(Zombie), typeof(Building), typeof(bool), typeof(Faction) });
		static readonly FieldInfo responseRemainingField = AccessTools.Field(typeof(CompGrowsFleshmassTendrils), "fleshmassUntilFleshbeastBirth");
		static readonly FieldInfo growthPointsField = AccessTools.Field(typeof(CompGrowsFleshmassTendrils), "growthPoints");
		static readonly FieldInfo heartNextFleshbeastField = AccessTools.Field(typeof(CompFleshmassHeart), "nextFleshbeast");

		[Tool("zombieland/fleshmass_collision_contract", Description = "Run the fast direct Fleshmass collision contract on real Anomaly buildings and Zombieland zombies: classifiers, category settings, CanSmash branches, heart immunity, OnlyColonists, settings timeline, patch registration, and response accounting.")]
		public static object FleshmassCollisionContract(
			[ToolParameter(Description = "Destroy every staged building and zombie and restore settings and letters afterward.", Required = false, DefaultValue = true)] bool cleanup = true)
		{
			var map = CurrentMap;
			if (map == null || Current.ProgramState != ProgramState.Playing)
				return new { success = false, error = "A playable current map is required." };
			if (ModsConfig.AnomalyActive == false)
				return new { success = false, error = "The Anomaly DLC is not active." };
			if (canSmashMethod == null || canSmashBuildingMethod == null || responseRemainingField == null)
			{
				return new
				{
					success = false,
					canSmashMethodPresent = canSmashMethod != null,
					canSmashBuildingMethodPresent = canSmashBuildingMethod != null,
					responseRemainingFieldPresent = responseRemainingField != null,
					error = "A required Zombieland or RimWorld 1.6 member was not found."
				};
			}
			if (TryFindFleshmassContractRoot(map, 48, 42, out var root, out var rootError) == false)
				return rootError;

			var settingsSnapshot = SnapshotZombieSettings();
			var beforeLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>()).ToHashSet();
			var spawned = new List<Thing>();
			var cases = new List<FleshmassContractCase>();
			var entities = Faction.OfEntities;
			var zombieFaction = Tools.GetZombieFaction();
			var stage = "required-defs";

			try
			{
				var requiredDefs = DescribeRequiredFleshmassDefs();
				cases.Add(new FleshmassContractCase
				{
					id = "required-defs",
					success = requiredDefs.All(item => item.present),
					details = requiredDefs
				});

				stage = "settings";
				var settingsCase = VerifyFleshmassSettingsContract();
				cases.Add(settingsCase);

				stage = "patch-registration";
				var patchCase = VerifyFleshmassPatchRegistration();
				cases.Add(patchCase);

				stage = "hearts";
				var heart = SpawnFleshmassBuilding("FleshmassHeart", root + new IntVec3(-19, 0, 10), map, entities, null, spawned) as Building_FleshmassHeart;
				if (heart == null)
					return new { success = false, error = "Could not create the contract FleshmassHeart." };
				var secondHeart = SpawnFleshmassBuilding("FleshmassHeart", root + new IntVec3(19, 0, 10), map, entities, null, spawned) as Building_FleshmassHeart;
				if (secondHeart == null)
					return new { success = false, error = "Could not create the second contract FleshmassHeart." };

				stage = "family-classification";
				var family = VerifyFleshmassFamilyClassification(map, root + new IntVec3(-19, 0, -12), heart, entities, spawned);
				cases.AddRange(family);

				stage = "selection-matrix";
				cases.AddRange(VerifyFleshmassSelectionMatrix(map, root + new IntVec3(2, 0, -14), heart, entities, spawned));
				stage = "response-accounting";
				cases.AddRange(VerifyFleshmassResponseAccounting(map, root + new IntVec3(2, 0, 0), heart, secondHeart, entities, zombieFaction, spawned));

				return new
				{
					success = cases.All(testCase => testCase.success),
					root = ZombieRuntimeActions.DescribeCell(root),
					caseCount = cases.Count,
					passed = cases.Count(testCase => testCase.success),
					failed = cases.Where(testCase => testCase.success == false).Select(testCase => testCase.id).ToArray(),
					cases = cases.ToArray(),
					staticEvidence = new
					{
						compFleshmassNotifyKilled = "RimWorld.CompFleshmass.Notify_Killed(Map, DamageInfo?)",
						responseMethod = "RimWorld.CompGrowsFleshmassTendrils.Notify_FleshmassDestroyedByPlayer(Thing)",
						installedSteamAssemblyMvid = typeof(CompFleshmass).Module.ModuleVersionId.ToString("N")
					},
					logNote = "Use rimbridge/list_logs minimumLevel=warning after this operation for the log-clean gate."
				};
			}
			catch (Exception ex)
			{
				return new
				{
					success = false,
					stage,
					error = $"{ex.GetType().Name}: {ex.Message}",
					stackTrace = ex.StackTrace,
					completedCases = cases.Select(testCase => new { testCase.id, testCase.success }).ToArray()
				};
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
				if (cleanup)
				{
					for (var i = spawned.Count - 1; i >= 0; i--)
						CleanupFleshmassContractThing(spawned[i]);
					RemoveNewLetters(beforeLetters);
					PruneDestroyedContractZombies(map);
				}
			}
		}

		static FleshmassContractCase VerifyFleshmassSettingsContract()
		{
			var defaults = new SettingsGroup();
			var lower = defaults.MakeCopy();
			var upper = defaults.MakeCopy();
			lower.ordinaryZombiesAttackFleshmass = false;
			lower.tankyAndSuicideZombiesAttackFleshmass = true;
			lower.formerColonistAndSpecialZombiesAttackFleshmass = false;
			upper.ordinaryZombiesAttackFleshmass = true;
			upper.tankyAndSuicideZombiesAttackFleshmass = false;
			upper.formerColonistAndSpecialZombiesAttackFleshmass = true;
			var frames = new List<SettingsKeyFrame>
			{
				new() { amount = 0, unit = SettingsKeyFrame.Unit.Days, values = lower },
				new() { amount = 2, unit = SettingsKeyFrame.Unit.Days, values = upper }
			};
			var day1 = ZombieSettings.CalculateInterpolation(frames, GenDate.TicksPerDay);
			var day2 = ZombieSettings.CalculateInterpolation(frames, 2 * GenDate.TicksPerDay);
			var copy = lower.MakeCopy();
			var clipboardBefore = GUIUtility.systemCopyBuffer;
			var imported = new List<SettingsKeyFrame>();
			try
			{
				frames.ToClipboard();
				imported.FromClipboard();
			}
			finally
			{
				GUIUtility.systemCopyBuffer = clipboardBefore;
			}
			var importExportValid = imported.Count == 2
				&& imported[0]?.values != null
				&& imported[1]?.values != null
				&& imported[0].values.ordinaryZombiesAttackFleshmass == false
				&& imported[0].values.tankyAndSuicideZombiesAttackFleshmass
				&& imported[0].values.formerColonistAndSpecialZombiesAttackFleshmass == false
				&& imported[1].values.ordinaryZombiesAttackFleshmass
				&& imported[1].values.tankyAndSuicideZombiesAttackFleshmass == false
				&& imported[1].values.formerColonistAndSpecialZombiesAttackFleshmass;

			var success = defaults.ordinaryZombiesAttackFleshmass
				&& defaults.tankyAndSuicideZombiesAttackFleshmass
				&& defaults.formerColonistAndSpecialZombiesAttackFleshmass
				&& copy.ordinaryZombiesAttackFleshmass == false
				&& copy.tankyAndSuicideZombiesAttackFleshmass
				&& copy.formerColonistAndSpecialZombiesAttackFleshmass == false
				&& day1.ordinaryZombiesAttackFleshmass == false
				&& day1.tankyAndSuicideZombiesAttackFleshmass
				&& day1.formerColonistAndSpecialZombiesAttackFleshmass == false
				&& day2.ordinaryZombiesAttackFleshmass
				&& day2.tankyAndSuicideZombiesAttackFleshmass == false
				&& day2.formerColonistAndSpecialZombiesAttackFleshmass
				&& importExportValid;

			return new FleshmassContractCase
			{
				id = "settings-default-copy-timeline",
				success = success,
				details = new
				{
					defaults = DescribeFleshmassSettings(defaults),
					copy = DescribeFleshmassSettings(copy),
					day1 = DescribeFleshmassSettings(day1),
					day2 = DescribeFleshmassSettings(day2),
					importExportValid,
					imported = imported.Select(frame => new
					{
						frame.amount,
						unit = frame.unit.ToString(),
						values = DescribeFleshmassSettings(frame.values)
					}).ToArray()
				}
			};
		}

		static FleshmassContractCase VerifyFleshmassPatchRegistration()
		{
			var killMethod = AccessTools.Method(typeof(CompFleshmass), nameof(CompFleshmass.Notify_Killed));
			var affectCell = AccessTools.Method(typeof(Verse.Explosion), "AffectCell");
			var killPatches = killMethod == null ? null : Harmony.GetPatchInfo(killMethod);
			var explosionPatches = affectCell == null ? null : Harmony.GetPatchInfo(affectCell);
			var killOwners = killPatches?.Owners?.ToArray() ?? Array.Empty<string>();
			var explosionOwners = explosionPatches?.Owners?.ToArray() ?? Array.Empty<string>();
			var killPrefix = FindNestedPatchMethod("CompFleshmass_Notify_Killed_Patch", "Prefix");
			var killPostfix = FindNestedPatchMethod("CompFleshmass_Notify_Killed_Patch", "Postfix");
			var explosionPrefix = FindNestedPatchMethod("Explosion_AffectCell_Patch", "Prefix");
			var explosionFinalizer = FindNestedPatchMethod("Explosion_AffectCell_Patch", "Finalizer");
			var killPrefixRegistered = killPrefix != null && killPatches?.Prefixes.Any(patch => patch.PatchMethod == killPrefix) == true;
			var killPostfixRegistered = killPostfix != null && killPatches?.Postfixes.Any(patch => patch.PatchMethod == killPostfix) == true;
			var explosionPrefixRegistered = explosionPrefix != null && explosionPatches?.Prefixes.Any(patch => patch.PatchMethod == explosionPrefix) == true;
			var explosionFinalizerRegistered = explosionFinalizer != null && explosionPatches?.Finalizers.Any(patch => patch.PatchMethod == explosionFinalizer) == true;
			return new FleshmassContractCase
			{
				id = "patch-registration",
				success = killMethod != null
					&& affectCell != null
					&& killPrefixRegistered
					&& killPostfixRegistered
					&& explosionPrefixRegistered
					&& explosionFinalizerRegistered,
				details = new
				{
					killMethod = killMethod?.FullDescription(),
					killOwners,
					killPrefix = killPrefix?.FullDescription(),
					killPrefixRegistered,
					killPostfix = killPostfix?.FullDescription(),
					killPostfixRegistered,
					affectCell = affectCell?.FullDescription(),
					explosionOwners,
					explosionPrefix = explosionPrefix?.FullDescription(),
					explosionPrefixRegistered,
					explosionFinalizer = explosionFinalizer?.FullDescription(),
					explosionFinalizerRegistered
				}
			};
		}

		static List<FleshmassContractCase> VerifyFleshmassFamilyClassification(
			Map map,
			IntVec3 root,
			Building_FleshmassHeart heart,
			Faction entities,
			List<Thing> spawned)
		{
			var results = new List<FleshmassContractCase>();
			var names = new[] { "Fleshmass", "Fleshmass_Active", "FleshSack", "FleshmassHeart", "Fleshbulb", "NerveBundle", "FleshmassSpitter" };
			for (var i = 0; i < names.Length; i++)
			{
				Building building;
				if (names[i] == "FleshmassHeart")
					building = heart;
				else
					building = SpawnFleshmassBuilding(names[i], root + new IntVec3((i % 4) * 5, 0, (i / 4) * 5), map, entities, heart, spawned);
				results.Add(new FleshmassContractCase
				{
					id = $"family-{names[i]}",
					success = building != null && FleshmassCollision.IsFleshFamily(building),
					details = DescribeFleshmassBuilding(building)
				});
			}

			var sourced = SpawnFleshmassBuilding("Fleshmass_Active", root + new IntVec3(0, 0, 10), map, entities, heart, spawned);
			var sourceless = SpawnFleshmassBuilding("Fleshmass_Active", root + new IntVec3(5, 0, 10), map, entities, null, spawned);
			var unspawnedHeart = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("FleshmassHeart")) as Building_FleshmassHeart;
			var sourceLost = SpawnFleshmassBuilding("Fleshmass_Active", root + new IntVec3(10, 0, 10), map, entities, unspawnedHeart, spawned);
			spawned.Add(unspawnedHeart);
			results.Add(new FleshmassContractCase
			{
				id = "sourced-active-classification",
				success = FleshmassCollision.IsSourcedActiveFlesh(sourced)
					&& FleshmassCollision.IsSourcedActiveFlesh(sourceless) == false
					&& FleshmassCollision.IsSourcedActiveFlesh(sourceLost) == false,
				details = new
				{
					sourced = DescribeFleshmassBuilding(sourced),
					sourceless = DescribeFleshmassBuilding(sourceless),
					sourceLost = DescribeFleshmassBuilding(sourceLost)
				}
			});
			return results;
		}

		static IEnumerable<FleshmassContractCase> VerifyFleshmassSelectionMatrix(
			Map map,
			IntVec3 root,
			Building_FleshmassHeart heart,
			Faction entities,
			List<Thing> spawned)
		{
			var cases = new List<FleshmassContractCase>();
			var targetCell = root + IntVec3.East;

			var ordinary = SpawnContractZombie(ZombieType.Normal, root, map, spawned);
			var active = SpawnFleshmassBuilding("Fleshmass_Active", targetCell, map, entities, heart, spawned);
			SetFleshmassContractSettings(AttackMode.Everything, SmashMode.DoorsOnly, ordinary: true, tankSuicide: true, special: true);
			cases.Add(SelectionCase("ordinary-doors-sourced-on", ordinary, active, selected: true));
			SetFleshmassContractSettings(AttackMode.Everything, SmashMode.DoorsOnly, ordinary: false, tankSuicide: true, special: true);
			cases.Add(SelectionCase("ordinary-doors-sourced-off", ordinary, active, selected: false));
			CleanupFleshmassContractThing(active);
			CleanupFleshmassContractThing(ordinary);

			ordinary = SpawnContractZombie(ZombieType.Normal, root, map, spawned);
			var bulb = SpawnFleshmassBuilding("Fleshbulb", targetCell, map, entities, heart, spawned);
			SetFleshmassContractSettings(AttackMode.Everything, SmashMode.AnyBuilding, ordinary: true, tankSuicide: true, special: true);
			cases.Add(SelectionCase("ordinary-any-organ-on", ordinary, bulb, selected: true));
			SetFleshmassContractSettings(AttackMode.Everything, SmashMode.AnyBuilding, ordinary: false, tankSuicide: true, special: true);
			cases.Add(SelectionCase("ordinary-any-organ-off", ordinary, bulb, selected: false));
			CleanupFleshmassContractThing(bulb);
			CleanupFleshmassContractThing(ordinary);

			ordinary = SpawnContractZombie(ZombieType.Normal, root, map, spawned);
			var sourceless = SpawnFleshmassBuilding("Fleshmass_Active", targetCell, map, entities, null, spawned);
			SetFleshmassContractSettings(AttackMode.Everything, SmashMode.AnyBuilding, ordinary: true, tankSuicide: true, special: true);
			cases.Add(SelectionCase("ordinary-any-sourceless-on", ordinary, sourceless, selected: true));
			SetFleshmassContractSettings(AttackMode.Everything, SmashMode.DoorsOnly, ordinary: true, tankSuicide: true, special: true);
			cases.Add(SelectionCase("ordinary-doors-sourceless-rejected", ordinary, sourceless, selected: false));
			CleanupFleshmassContractThing(sourceless);
			CleanupFleshmassContractThing(ordinary);

			ordinary = SpawnContractZombie(ZombieType.Normal, root, map, spawned);
			var wall = SpawnContractWall(targetCell, map, Faction.OfPlayer, spawned);
			SetFleshmassContractSettings(AttackMode.Everything, SmashMode.AnyBuilding, ordinary: false, tankSuicide: false, special: false);
			cases.Add(SelectionCase("unrelated-building-continues", ordinary, wall, selected: true));
			CleanupFleshmassContractThing(wall);
			CleanupFleshmassContractThing(ordinary);

			ordinary = SpawnContractZombie(ZombieType.Normal, root, map, spawned);
			active = SpawnFleshmassBuilding("Fleshmass_Active", targetCell, map, entities, heart, spawned);
			SetFleshmassContractSettings(AttackMode.OnlyColonists, SmashMode.AnyBuilding, ordinary: true, tankSuicide: true, special: true);
			cases.Add(SelectionCase("only-colonists-excludes-heart-flesh", ordinary, active, selected: false));
			CleanupFleshmassContractThing(active);
			CleanupFleshmassContractThing(ordinary);

			var heartInteractionCell = FindClearCardinalInteractionCell(heart);
			ordinary = SpawnContractZombie(ZombieType.Normal, heartInteractionCell, map, spawned);
			SetFleshmassContractSettings(AttackMode.Everything, SmashMode.AnyBuilding, ordinary: true, tankSuicide: true, special: true);
			var heartScan = SelectionCase("heart-never-selected", ordinary, heart, selected: false);
			var heartPredicate = InvokeCanSmashBuilding(ordinary, heart, attackColonistsOnly: false);
			heartScan.success &= heartInteractionCell.IsValid && heartPredicate == false;
			heartScan.details = new
			{
				heartInteractionCell = ZombieRuntimeActions.DescribeCell(heartInteractionCell),
				directSharedPredicate = heartPredicate,
				scan = heartScan.details
			};
			cases.Add(heartScan);
			CleanupFleshmassContractThing(ordinary);

			var suicide = SpawnContractZombie(ZombieType.SuicideBomber, root, map, spawned);
			active = SpawnFleshmassBuilding("Fleshmass_Active", targetCell, map, entities, heart, spawned);
			SetFleshmassContractSettings(AttackMode.Everything, SmashMode.Nothing, ordinary: false, tankSuicide: true, special: false);
			cases.Add(SuicideSelectionCase("suicide-arms-on", suicide, expectedArmed: true));
			suicide.bombWillGoOff = false;
			SetFleshmassContractSettings(AttackMode.Everything, SmashMode.Nothing, ordinary: true, tankSuicide: false, special: true);
			cases.Add(SuicideSelectionCase("suicide-arms-off", suicide, expectedArmed: false));
			CleanupFleshmassContractThing(active);
			CleanupFleshmassContractThing(suicide);

			var former = SpawnContractZombie(ZombieType.Normal, root, map, spawned);
			former.wasMapPawnBefore = true;
			active = SpawnFleshmassBuilding("Fleshmass_Active", targetCell, map, entities, heart, spawned);
			SetFleshmassContractSettings(AttackMode.Everything, SmashMode.AnyBuilding, ordinary: false, tankSuicide: false, special: true);
			cases.Add(SelectionCase("former-colonist-special-on", former, active, selected: true));
			SetFleshmassContractSettings(AttackMode.Everything, SmashMode.Nothing, ordinary: true, tankSuicide: true, special: true);
			cases.Add(SelectionCase("former-colonist-nothing", former, active, selected: false));
			CleanupFleshmassContractThing(active);
			CleanupFleshmassContractThing(former);

			var miner = SpawnContractZombie(ZombieType.Miner, root, map, spawned);
			active = SpawnFleshmassBuilding("Fleshmass_Active", targetCell, map, entities, heart, spawned);
			SetFleshmassContractSettings(AttackMode.Everything, SmashMode.AnyBuilding, ordinary: true, tankSuicide: true, special: false);
			cases.Add(SelectionCase("special-miner-off", miner, active, selected: false));
			SetFleshmassContractSettings(AttackMode.Everything, SmashMode.AnyBuilding, ordinary: false, tankSuicide: false, special: true);
			cases.Add(SelectionCase("special-miner-on", miner, active, selected: true));
			CleanupFleshmassContractThing(active);
			CleanupFleshmassContractThing(miner);

			var childChanceBefore = ZombieSettings.Values.childChance;
			ZombieSettings.Values.childChance = 1f;
			var child = SpawnContractZombie(ZombieType.Normal, root, map, spawned);
			var childSpecial = SpawnContractZombie(ZombieType.Normal, root + new IntVec3(0, 0, 4), map, spawned);
			var childFormer = SpawnContractZombie(ZombieType.Normal, root + new IntVec3(0, 0, 8), map, spawned);
			ZombieSettings.Values.childChance = childChanceBefore;
			if (childSpecial != null)
				childSpecial.isMiner = true;
			if (childFormer != null)
				childFormer.wasMapPawnBefore = true;
			var childDefinitionAvailable = BodyTypeDefOf.Child != null;
			var childBodiesMatch = childDefinitionAvailable == false
				|| (child?.story?.bodyType == BodyTypeDefOf.Child
					&& childSpecial?.story?.bodyType == BodyTypeDefOf.Child
					&& childFormer?.story?.bodyType == BodyTypeDefOf.Child);
			cases.Add(new FleshmassContractCase
			{
				id = "child-category-order",
				success = childBodiesMatch
					&& FleshmassCollision.CategoryFor(child) == FleshmassZombieCategory.Ordinary
					&& FleshmassCollision.CategoryFor(childSpecial) == FleshmassZombieCategory.FormerColonistAndSpecial
					&& FleshmassCollision.CategoryFor(childFormer) == FleshmassZombieCategory.FormerColonistAndSpecial,
				details = new
				{
					childDefinitionAvailable,
					biotechActive = ModsConfig.BiotechActive,
					childBody = child?.story?.bodyType?.defName,
					childCategory = FleshmassCollision.CategoryFor(child).ToString(),
					childSpecialBody = childSpecial?.story?.bodyType?.defName,
					childSpecialCategory = FleshmassCollision.CategoryFor(childSpecial).ToString(),
					childFormerBody = childFormer?.story?.bodyType?.defName,
					childFormerCategory = FleshmassCollision.CategoryFor(childFormer).ToString()
				}
			});
			CleanupFleshmassContractThing(child);
			CleanupFleshmassContractThing(childSpecial);
			CleanupFleshmassContractThing(childFormer);

			var tank = SpawnContractZombie(ZombieType.TankyOperator, root, map, spawned);
			var albino = SpawnContractZombie(ZombieType.Albino, root + new IntVec3(0, 0, 4), map, spawned);
			var tankSettings = new SettingsGroup { tankyAndSuicideZombiesAttackFleshmass = false };
			cases.Add(new FleshmassContractCase
			{
				id = "tank-albino-categories",
				success = FleshmassCollision.CategoryFor(tank) == FleshmassZombieCategory.TankyAndSuicide
					&& FleshmassCollision.CategoryEnabled(tank, tankSettings) == false
					&& FleshmassCollision.CategoryFor(albino) == FleshmassZombieCategory.FormerColonistAndSpecial
					&& InvokeCanSmash(albino) == null,
				details = new
				{
					tankCategory = FleshmassCollision.CategoryFor(tank).ToString(),
					albinoCategory = FleshmassCollision.CategoryFor(albino).ToString(),
					albinoCanSmash = InvokeCanSmash(albino)?.def?.defName
				}
			});
			CleanupFleshmassContractThing(tank);
			CleanupFleshmassContractThing(albino);

			var specialTypes = new[]
			{
				ZombieType.ToxicSplasher,
				ZombieType.Miner,
				ZombieType.Electrifier,
				ZombieType.Albino,
				ZombieType.DarkSlimer,
				ZombieType.Healer
			};
			var specialCategories = new List<object>();
			var allSpecialCategories = true;
			foreach (var type in specialTypes)
			{
				var specialZombie = SpawnContractZombie(type, root, map, spawned);
				var category = FleshmassCollision.CategoryFor(specialZombie);
				allSpecialCategories &= category == FleshmassZombieCategory.FormerColonistAndSpecial;
				specialCategories.Add(new { type = type.ToString(), category = category.ToString() });
				CleanupFleshmassContractThing(specialZombie);
			}
			cases.Add(new FleshmassContractCase
			{
				id = "all-current-special-categories",
				success = allSpecialCategories,
				details = specialCategories.ToArray()
			});

			return cases;
		}

		static IEnumerable<FleshmassContractCase> VerifyFleshmassResponseAccounting(
			Map map,
			IntVec3 root,
			Building_FleshmassHeart heart,
			Building_FleshmassHeart secondHeart,
			Faction entities,
			Faction zombieFaction,
			List<Thing> spawned)
		{
			var cases = new List<FleshmassContractCase>();
			var grower = heart.GetComp<CompGrowsFleshmassTendrils>();
			var zombie = SpawnContractZombie(ZombieType.Normal, root + new IntVec3(-4, 0, 0), map, spawned);
			if (zombie?.Faction != zombieFaction)
				zombie?.SetFaction(zombieFaction);

			var nonlethal = SpawnFleshmassBuilding("Fleshmass_Active", root, map, entities, heart, spawned);
			SetResponseRemaining(grower, 1000);
			var nonlethalBefore = ResponseRemaining(grower);
			_ = nonlethal.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 1f, 0f, -1f, zombie));
			var nonlethalAfter = ResponseRemaining(grower);
			cases.Add(new FleshmassContractCase
			{
				id = "response-nonlethal-zero-credit",
				success = nonlethal.Destroyed == false && nonlethalAfter == nonlethalBefore,
				details = new { nonlethalBefore, nonlethalAfter, hitPoints = nonlethal.HitPoints }
			});
			CleanupFleshmassContractThing(nonlethal);

			var zombieCells = SpawnFleshmassChain(map, root + new IntVec3(0, 0, 4), 12, heart, entities, spawned);
			SetResponseRemaining(grower, 1000);
			var zombieBefore = ResponseRemaining(grower);
			Rand.PushState(73451);
			try
			{
				zombieCells[0].Kill(new DamageInfo(DamageDefOf.Blunt, 99999f, 0f, -1f, zombie));
			}
			finally
			{
				Rand.PopState();
			}
			var zombieDeaths = zombieCells.Count(thing => thing.Destroyed || thing.Spawned == false);
			var zombieAfter = ResponseRemaining(grower);
			cases.Add(new FleshmassContractCase
			{
				id = "response-zombie-root-and-cascade-once",
				success = zombieDeaths > 1 && zombieBefore - zombieAfter == zombieDeaths,
				details = new { zombieBefore, zombieAfter, zombieDeaths, delta = zombieBefore - zombieAfter }
			});

			var playerPawn = GenerateAreaWorkflowPawn(Faction.OfPlayer, false);
			playerPawn.Name = new NameSingle("ZL Fleshmass Player Instigator");
			GenSpawn.Spawn(playerPawn, root + new IntVec3(-4, 0, 8), map, Rot4.South);
			DisablePawnWork(playerPawn);
			spawned.Add(playerPawn);
			var playerCells = SpawnFleshmassChain(map, root + new IntVec3(0, 0, 8), 12, heart, entities, spawned);
			SetResponseRemaining(grower, 1000);
			var playerBefore = ResponseRemaining(grower);
			Rand.PushState(73452);
			try
			{
				playerCells[0].Kill(new DamageInfo(DamageDefOf.Blunt, 99999f, 0f, -1f, playerPawn));
			}
			finally
			{
				Rand.PopState();
			}
			var playerDeaths = playerCells.Count(thing => thing.Destroyed || thing.Spawned == false);
			var playerAfter = ResponseRemaining(grower);
			cases.Add(new FleshmassContractCase
			{
				id = "response-player-path-once",
				success = playerPawn != null && playerDeaths > 1 && playerBefore - playerAfter == playerDeaths,
				details = new { playerPawnPresent = playerPawn != null, playerBefore, playerAfter, playerDeaths, delta = playerBefore - playerAfter }
			});

			var nullCells = SpawnFleshmassChain(map, root + new IntVec3(0, 0, 12), 12, heart, entities, spawned);
			SetResponseRemaining(grower, 1000);
			var nullBefore = ResponseRemaining(grower);
			Rand.PushState(73453);
			try
			{
				nullCells[0].Kill(new DamageInfo(DamageDefOf.Blunt, 99999f));
			}
			finally
			{
				Rand.PopState();
			}
			var nullDeaths = nullCells.Count(thing => thing.Destroyed || thing.Spawned == false);
			var nullAfter = ResponseRemaining(grower);
			cases.Add(new FleshmassContractCase
			{
				id = "response-null-path-once",
				success = nullDeaths > 1 && nullBefore - nullAfter == nullDeaths,
				details = new { nullBefore, nullAfter, nullDeaths, delta = nullBefore - nullAfter }
			});

			var unspawnedHeart = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("FleshmassHeart")) as Building_FleshmassHeart;
			spawned.Add(unspawnedHeart);
			var lostGrower = unspawnedHeart.GetComp<CompGrowsFleshmassTendrils>();
			var sourceLostCell = SpawnFleshmassBuilding("Fleshmass_Active", root + new IntVec3(16, 0, 0), map, entities, unspawnedHeart, spawned);
			SetResponseRemaining(lostGrower, 1000);
			var lostBefore = ResponseRemaining(lostGrower);
			sourceLostCell.Kill(new DamageInfo(DamageDefOf.Blunt, 99999f, 0f, -1f, zombie));
			var lostAfter = ResponseRemaining(lostGrower);
			cases.Add(new FleshmassContractCase
			{
				id = "response-source-loss-zero-credit",
				success = sourceLostCell.Destroyed && lostAfter == lostBefore,
				details = new { sourceSpawned = unspawnedHeart.Spawned, lostBefore, lostAfter }
			});

			var secondGrower = secondHeart.GetComp<CompGrowsFleshmassTendrils>();
			var touchingCells = new List<Building>();
			for (var i = 0; i < 12; i++)
			{
				var source = i % 2 == 0 ? (Thing)heart : secondHeart;
				var cell = SpawnFleshmassBuilding("Fleshmass_Active", root + new IntVec3(-12 + i, 0, 18), map, entities, source, spawned);
				if (cell != null)
					touchingCells.Add(cell);
			}
			SetResponseRemaining(grower, 1000);
			SetResponseRemaining(secondGrower, 1000);
			var touchingFirstBefore = ResponseRemaining(grower);
			var touchingSecondBefore = ResponseRemaining(secondGrower);
			Rand.PushState(73454);
			try
			{
				touchingCells[5].Kill(new DamageInfo(DamageDefOf.Blunt, 99999f, 0f, -1f, zombie));
			}
			finally
			{
				Rand.PopState();
			}
			var firstDeaths = touchingCells.Count(cell => (cell.Destroyed || cell.Spawned == false) && ReferenceEquals(cell.TryGetComp<CompFleshmass>()?.source, heart));
			var secondDeaths = touchingCells.Count(cell => (cell.Destroyed || cell.Spawned == false) && ReferenceEquals(cell.TryGetComp<CompFleshmass>()?.source, secondHeart));
			var touchingFirstAfter = ResponseRemaining(grower);
			var touchingSecondAfter = ResponseRemaining(secondGrower);
			cases.Add(new FleshmassContractCase
			{
				id = "response-touching-fields-stored-source",
				success = firstDeaths > 0
					&& secondDeaths > 0
					&& touchingFirstBefore - touchingFirstAfter == firstDeaths
					&& touchingSecondBefore - touchingSecondAfter == secondDeaths,
				details = new
				{
					firstDeaths,
					secondDeaths,
					firstDelta = touchingFirstBefore - touchingFirstAfter,
					secondDelta = touchingSecondBefore - touchingSecondAfter
				}
			});

			var compatibleSource = SpawnCompatibleFleshmassGrower(root + new IntVec3(17, 0, -8), map, entities, spawned);
			var compatibleGrower = compatibleSource?.TryGetComp<CompGrowsFleshmassTendrils>();
			var compatibleCell = SpawnFleshmassBuilding("Fleshmass_Active", root + new IntVec3(17, 0, -3), map, entities, compatibleSource, spawned);
			if (compatibleGrower != null)
				SetResponseRemaining(compatibleGrower, 1000);
			var compatibleBefore = ResponseRemaining(compatibleGrower);
			compatibleCell?.Kill(new DamageInfo(DamageDefOf.Blunt, 99999f, 0f, -1f, zombie));
			var compatibleAfter = ResponseRemaining(compatibleGrower);
			cases.Add(new FleshmassContractCase
			{
				id = "response-compatible-non-heart-grower",
				success = compatibleSource != null
					&& compatibleSource.Spawned
					&& compatibleSource is not Building_FleshmassHeart
					&& compatibleCell?.Destroyed == true
					&& compatibleBefore - compatibleAfter == 1
					&& FleshmassCollision.IsSourcedActiveFlesh(compatibleCell) == false,
				details = new
				{
					sourceType = compatibleSource?.GetType().FullName,
					sourceSpawned = compatibleSource?.Spawned,
					compatibleBefore,
					compatibleAfter,
					candidateAccepted = compatibleCell != null && FleshmassCollision.IsSourcedActiveFlesh(compatibleCell)
				}
			});

			CleanupFleshmassContractThing(zombie);
			return cases;
		}

		static FleshmassContractCase SelectionCase(string id, Zombie zombie, Building expectedTarget, bool selected)
		{
			var actual = InvokeCanSmash(zombie);
			return new FleshmassContractCase
			{
				id = id,
				success = selected ? ReferenceEquals(actual, expectedTarget) : actual == null,
				details = new
				{
					expectedSelected = selected,
					expectedTarget = expectedTarget?.def?.defName,
					actualTarget = actual?.def?.defName,
					category = FleshmassCollision.CategoryFor(zombie).ToString(),
					categoryEnabled = FleshmassCollision.CategoryEnabled(zombie)
				}
			};
		}

		static FleshmassContractCase SuicideSelectionCase(string id, Zombie zombie, bool expectedArmed)
		{
			var target = InvokeCanSmash(zombie);
			return new FleshmassContractCase
			{
				id = id,
				success = target == null && zombie.bombWillGoOff == expectedArmed,
				details = new
				{
					expectedArmed,
					actualArmed = zombie.bombWillGoOff,
					returnedTarget = target?.def?.defName,
					category = FleshmassCollision.CategoryFor(zombie).ToString(),
					categoryEnabled = FleshmassCollision.CategoryEnabled(zombie)
				}
			};
		}

		static Building InvokeCanSmash(Zombie zombie)
		{
			return canSmashMethod.Invoke(null, new object[] { zombie }) as Building;
		}

		static bool InvokeCanSmashBuilding(Zombie zombie, Building building, bool attackColonistsOnly)
		{
			return (bool)canSmashBuildingMethod.Invoke(null, new object[] { zombie, building, attackColonistsOnly, Faction.OfPlayer });
		}

		static IntVec3 FindClearCardinalInteractionCell(Building building)
		{
			if (building?.Map == null)
				return IntVec3.Invalid;
			var map = building.Map;
			return GenAdj.CellsAdjacent8Way(building)
				.Where(cell => cell.InBounds(map) && cell.Standable(map) && cell.Fogged(map) == false)
				.Where(cell => cell.GetEdifice(map) == null && cell.GetThingList(map).Any(thing => thing is Pawn) == false)
				.FirstOrDefault(cell => GenAdj.CardinalDirections.Any(direction => ReferenceEquals((cell + direction).GetEdifice(map), building)));
		}

		static void SetFleshmassContractSettings(AttackMode attackMode, SmashMode smashMode, bool ordinary, bool tankSuicide, bool special)
		{
			var settings = new SettingsGroup
			{
				attackMode = attackMode,
				smashMode = smashMode,
				smashOnlyWhenAgitated = false,
				ordinaryZombiesAttackFleshmass = ordinary,
				tankyAndSuicideZombiesAttackFleshmass = tankSuicide,
				formerColonistAndSpecialZombiesAttackFleshmass = special,
				zombiesDieOnZeroThreat = false,
				zombieFreeEvents = false
			};
			ZombieSettings.Values = settings;
			ZombieSettings.ValuesOverTime = new List<SettingsKeyFrame>
			{
				new() { amount = 0, unit = SettingsKeyFrame.Unit.Days, values = settings.MakeCopy() }
			};
		}

		static Zombie SpawnContractZombie(ZombieType type, IntVec3 cell, Map map, List<Thing> spawned)
		{
			var zombie = ZombieRuntimeActions.SpawnZombie(cell, map, type, true);
			if (zombie == null)
				return null;
			zombie.Name = new NameSingle($"ZL Fleshmass {type}");
			zombie.state = ZombieState.Tracking;
			zombie.raging = Math.Max(zombie.raging, GenTicks.TicksAbs + 600);
			zombie.checkSmashable = true;
			spawned.Add(zombie);
			return zombie;
		}

		static Building SpawnFleshmassBuilding(
			string defName,
			IntVec3 cell,
			Map map,
			Faction faction,
			Thing source,
			List<Thing> spawned)
		{
			var def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
			if (def == null)
				return null;
			var building = ThingMaker.MakeThing(def) as Building;
			if (building == null)
				return null;
			var flesh = building.TryGetComp<CompFleshmass>();
			if (flesh != null)
				flesh.source = source;
			if (faction != null)
				building.SetFaction(faction);
			GenSpawn.Spawn(building, cell, map, Rot4.North);
			spawned.Add(building);
			return building;
		}

		static Building SpawnContractWall(IntVec3 cell, Map map, Faction faction, List<Thing> spawned)
		{
			var wall = ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.WoodLog) as Building;
			wall.SetFaction(faction);
			GenSpawn.Spawn(wall, cell, map, Rot4.North);
			spawned.Add(wall);
			return wall;
		}

		static Building SpawnCompatibleFleshmassGrower(IntVec3 cell, Map map, Faction faction, List<Thing> spawned)
		{
			var heartDef = DefDatabase<ThingDef>.GetNamedSilentFail("FleshmassHeart");
			if (heartDef == null)
				return null;
			var source = new Building { def = heartDef };
			source.InitializeComps();
			if (source.TryGetComp<CompGrowsFleshmassTendrils>() == null)
				return null;
			if (faction != null)
				source.SetFaction(faction);
			GenSpawn.Spawn(source, cell, map, Rot4.North);
			spawned.Add(source);
			return source;
		}

		static Building[] SpawnFleshmassChain(
			Map map,
			IntVec3 start,
			int count,
			Building_FleshmassHeart heart,
			Faction faction,
			List<Thing> spawned)
		{
			var result = new List<Building>();
			for (var i = 0; i < count; i++)
			{
				var building = SpawnFleshmassBuilding("Fleshmass_Active", start + IntVec3.East * i, map, faction, heart, spawned);
				if (building != null)
					result.Add(building);
			}
			return result.ToArray();
		}

		static int ResponseRemaining(CompGrowsFleshmassTendrils grower)
		{
			return grower == null ? -1 : (int)responseRemainingField.GetValue(grower);
		}

		static void SetResponseRemaining(CompGrowsFleshmassTendrils grower, int value)
		{
			responseRemainingField.SetValue(grower, value);
		}

		static object DescribeFleshmassSettings(SettingsGroup settings)
		{
			return settings == null ? null : new
			{
				settings.ordinaryZombiesAttackFleshmass,
				settings.tankyAndSuicideZombiesAttackFleshmass,
				settings.formerColonistAndSpecialZombiesAttackFleshmass
			};
		}

		static object DescribeFleshmassBuilding(Building building)
		{
			var flesh = building?.TryGetComp<CompFleshmass>();
			return building == null ? null : new
			{
				building = DescribeAnomalyThing(building),
				isFleshFamily = FleshmassCollision.IsFleshFamily(building),
				isSourcedActive = FleshmassCollision.IsSourcedActiveFlesh(building),
				source = flesh?.source == null ? null : DescribeAnomalyThing(flesh.source)
			};
		}

		static (string defName, bool present)[] DescribeRequiredFleshmassDefs()
		{
			return new[] { "Fleshmass", "Fleshmass_Active", "FleshSack", "FleshmassHeart", "Fleshbulb", "NerveBundle", "FleshmassSpitter" }
				.Select(defName => (defName, DefDatabase<ThingDef>.GetNamedSilentFail(defName) != null))
				.ToArray();
		}

		static bool TryFindFleshmassContractRoot(Map map, int width, int height, out IntVec3 root, out object error)
		{
			root = IntVec3.Invalid;
			error = null;
			var searchRadius = Math.Min(75f, Math.Min(map.Size.x, map.Size.z) / 3f);
			foreach (var center in GenRadial.RadialCellsAround(map.Center, searchRadius, true))
			{
				var minX = center.x - width / 2;
				var minZ = center.z - height / 2;
				var rect = CellRect.FromLimits(minX, minZ, minX + width - 1, minZ + height - 1);
				if (rect.Cells.Any(cell => cell.InBounds(map) == false || cell.Fogged(map)))
					continue;
				if (rect.Cells.Any(cell => cell.GetEdifice(map) != null || cell.GetThingList(map).Any(thing => thing is Pawn)))
					continue;
				root = center;
				return true;
			}
			error = new { success = false, error = $"No clear {width}x{height} contract area was found on the current map." };
			return false;
		}

		static void CleanupFleshmassContractThing(Thing thing)
		{
			if (thing == null)
				return;
			if (thing is Pawn pawn && pawn.Corpse is { Destroyed: false } corpse)
				corpse.Destroy(DestroyMode.Vanish);
			if (thing.Destroyed)
				return;
			if (thing.def?.destroyable == false)
			{
				if (thing.Spawned)
					thing.DeSpawn(DestroyMode.Vanish);
				return;
			}
			thing.Destroy(DestroyMode.Vanish);
		}

		static void PruneDestroyedContractZombies(Map map)
		{
			map?.GetComponent<TickManager>()?.allZombiesCached?.RemoveWhere(zombie => zombie == null || zombie.Destroyed || zombie.Spawned == false);
		}

		static void RemoveNewLetters(HashSet<Letter> before)
		{
			if (Find.LetterStack == null)
				return;
			foreach (var letter in Find.LetterStack.LettersListForReading.Where(letter => before.Contains(letter) == false).ToArray())
				Find.LetterStack.RemoveLetter(letter);
		}
	}
}
