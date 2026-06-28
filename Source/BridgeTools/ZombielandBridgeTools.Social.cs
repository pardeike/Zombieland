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
		[Tool("zombieland/zombie_selection_respects_former_colonist", Description = "Verify map-click and selector behavior distinguishes ordinary zombies from former-colonist zombies and corpses.")]
		public static object ZombieSelectionRespectsFormerColonist()
		{
			var inspectTabTargets = PatchedMethodsForPatchClass("MainTabWindow_Inspect_CurTabs_Patch");
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			Find.Selector.ClearSelection();
			var destroyedZombies = ZombieRuntimeActions.DestroyZombies(map);
			var destroyedZombieCorpses = 0;
			foreach (var corpse in map.listerThings.AllThings.OfType<ZombieCorpse>().ToArray())
			{
				corpse.Destroy();
				destroyedZombieCorpses++;
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var normalCell, out var normalSpawnError) == false)
				return normalSpawnError;
			if (TryFindClearSpawnCell(map, normalCell + new IntVec3(4, 0, 0), 10f, out var formerPawnCell, out var formerSpawnError) == false)
				return formerSpawnError;

			var normalZombie = ZombieRuntimeActions.SpawnZombie(normalCell, map, ZombieType.Normal, true);
			if (normalZombie == null)
			{
				return new
				{
					success = false,
					normalCell = ZombieRuntimeActions.DescribeCell(normalCell),
					error = "ZombieGenerator.SpawnZombie returned no ordinary zombie."
				};
			}

			var beforeConversionIds = new HashSet<string>(CurrentZombies(map).Select(ZombieRuntimeActions.StableThingId));
			var formerPawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(formerPawn, formerPawnCell, map, Rot4.South);
			DisablePawnWork(formerPawn);
			formerPawn.apparel?.DestroyAll();
			formerPawn.equipment?.DestroyAllEquipment(DestroyMode.Vanish);
			formerPawn.inventory?.DestroyAll();
			var formerPawnBeforeConversion = DescribePawn(formerPawn);
			ZombieRuntimeActions.ConvertPawnToZombie(formerPawn, map, true);
			var formerZombie = CurrentZombies(map)
				.OfType<Zombie>()
				.Where(zombie => beforeConversionIds.Contains(ZombieRuntimeActions.StableThingId(zombie)) == false)
				.OrderBy(zombie => zombie.Position.DistanceToSquared(formerPawnCell))
				.FirstOrDefault();
			if (formerZombie == null)
			{
				return new
				{
					success = false,
					destroyedZombies,
					destroyedZombieCorpses,
					formerPawn = formerPawnBeforeConversion,
					formerPawnCell = ZombieRuntimeActions.DescribeCell(formerPawnCell),
					error = "Converting the pawn did not produce a new zombie."
				};
			}

			var expectedFormerColor = new Color(0.7f, 1f, 0.7f);
			var normalLiveSelectableByMapClick = ThingSelectionUtility.SelectableByMapClick(normalZombie);
			var formerLiveSelectableByMapClick = ThingSelectionUtility.SelectableByMapClick(formerZombie);
			var normalLiveLabelColor = PawnNameColorUtility.PawnNameColorOf(normalZombie);
			var formerLiveLabelColor = PawnNameColorUtility.PawnNameColorOf(formerZombie);
			var normalLiveHasFormerColor = ColorsApproximatelyEqual(normalLiveLabelColor, expectedFormerColor);
			var formerLiveHasFormerColor = ColorsApproximatelyEqual(formerLiveLabelColor, expectedFormerColor);

			normalZombie.Kill(null);
			formerZombie.Kill(null);
			var normalCorpse = normalZombie.Corpse as ZombieCorpse
				?? map.listerThings.AllThings.OfType<ZombieCorpse>().OrderBy(thing => thing.Position.DistanceToSquared(normalCell)).FirstOrDefault();
			var formerCorpse = formerZombie.Corpse as ZombieCorpse
				?? map.listerThings.AllThings.OfType<ZombieCorpse>().OrderBy(thing => thing.Position.DistanceToSquared(formerPawnCell)).FirstOrDefault();
			if (normalCorpse == null || formerCorpse == null)
			{
				return new
				{
					success = false,
					destroyedZombies,
					destroyedZombieCorpses,
					normalZombie = DescribeZombie(normalZombie),
					formerZombie = DescribeZombie(formerZombie),
					normalCorpse = DescribeCorpse(normalCorpse),
					formerCorpse = DescribeCorpse(formerCorpse),
					error = "Killing the test zombies did not leave both zombie corpses."
				};
			}

			var normalCorpseSelectableByMapClick = ThingSelectionUtility.SelectableByMapClick(normalCorpse);
			var formerCorpseSelectableByMapClick = ThingSelectionUtility.SelectableByMapClick(formerCorpse);
			var normalCorpseSelectedBySelector = SelectsThroughSelector(normalCorpse);
			var formerCorpseSelectedBySelector = SelectsThroughSelector(formerCorpse);
			var inspectTabs = VerifyZombieCorpseInspectTabs(normalCorpse, formerCorpse, out var inspectTabsSuccess);
			Find.Selector.ClearSelection();

			return new
			{
				success = normalLiveSelectableByMapClick == false
					&& formerLiveSelectableByMapClick
					&& normalCorpseSelectableByMapClick == false
					&& formerCorpseSelectableByMapClick
					&& normalCorpseSelectedBySelector == false
					&& formerCorpseSelectedBySelector
					&& normalLiveHasFormerColor == false
					&& formerLiveHasFormerColor
					&& inspectTabTargets.Length > 0
					&& inspectTabsSuccess,
				destroyedZombies,
				destroyedZombieCorpses,
				normalZombie = DescribeZombie(normalZombie),
				formerZombie = DescribeZombie(formerZombie),
				formerPawnBeforeConversion,
				normalCorpse = DescribeCorpse(normalCorpse),
				formerCorpse = DescribeCorpse(formerCorpse),
				normalLiveSelectableByMapClick,
				formerLiveSelectableByMapClick,
				normalCorpseSelectableByMapClick,
				formerCorpseSelectableByMapClick,
				normalCorpseSelectedBySelector,
				formerCorpseSelectedBySelector,
				normalLiveLabelColor = DescribeColor(normalLiveLabelColor),
				formerLiveLabelColor = DescribeColor(formerLiveLabelColor),
				normalLiveHasFormerColor,
				formerLiveHasFormerColor,
				patchTargets = new
				{
					inspectTabs = inspectTabTargets
				},
				inspectTabs
			};
		}

		static object VerifyZombieCorpseInspectTabs(ZombieCorpse normalCorpse, ZombieCorpse formerCorpse, out bool success)
		{
			var postfix = FindNestedPatchMethod("MainTabWindow_Inspect_CurTabs_Patch", "Postfix");
			var nullArgs = new object[] { null };
			postfix?.Invoke(null, nullArgs);
			var nullAfterPostfix = nullArgs[0] as IEnumerable<InspectTabBase>;

			var normalSelection = ReadInspectTabsForSelection(normalCorpse);
			var formerSelection = ReadInspectTabsForSelection(formerCorpse);
			success = postfix != null
				&& nullAfterPostfix != null
				&& normalSelection.selected == false
				&& formerSelection.selected
				&& formerSelection.tabsNull == false
				&& formerSelection.tabCount == 0;

			return new
			{
				success,
				postfixFound = postfix != null,
				nullPostfix = new
				{
					returnedNull = nullAfterPostfix == null,
					count = nullAfterPostfix?.Count() ?? -1
				},
				normalSelection,
				formerSelection
			};
		}

		sealed class InspectTabSelectionProbe
		{
			public string selectedId;
			public bool isZombieCorpse;
			public bool isFormerMapPawnCorpse;
			public string defName;
			public bool selected;
			public bool tabsNull;
			public int tabCount;
			public string[] tabTypes;
			public bool directTabsNull;
			public int directTabCount;
			public int defInspectorTabTypeCount;
			public int defInspectorTabResolvedCount;
		}

		static InspectTabSelectionProbe ReadInspectTabsForSelection(object selected)
		{
			Find.Selector.ClearSelection();
			Find.Selector.Select(selected, false, false);
			var isSelected = Find.Selector.IsSelected(selected);
			var tabs = new MainTabWindow_Inspect().CurTabs;
			var tabArray = tabs?.ToArray();
			Find.Selector.ClearSelection();

			var thing = selected as Thing;
			var directTabs = thing?.GetInspectTabs();
			var directTabArray = directTabs?.ToArray();
			return new InspectTabSelectionProbe
			{
				selectedId = StableId(selected),
				isZombieCorpse = selected is ZombieCorpse,
				isFormerMapPawnCorpse = selected is ZombieCorpse corpse && corpse.InnerPawn is Zombie zombie && zombie.wasMapPawnBefore,
				defName = thing?.def?.defName,
				selected = isSelected,
				tabsNull = tabArray == null,
				tabCount = tabArray?.Length ?? -1,
				tabTypes = tabArray?.Select(tab => tab?.GetType().Name ?? "<null>").ToArray() ?? Array.Empty<string>(),
				directTabsNull = directTabArray == null,
				directTabCount = directTabArray?.Length ?? -1,
				defInspectorTabTypeCount = thing?.def?.inspectorTabs?.Count ?? -1,
				defInspectorTabResolvedCount = thing?.def?.inspectorTabsResolved?.Count ?? -1
			};
		}

		[Tool("zombieland/zombie_social_thought_suppression", Description = "Verify zombie pawns and zombie corpses are ignored by RimWorld social-memory, interaction, and observed-corpse thought APIs.")]
		public static object ZombieSocialThoughtSuppression()
		{
			var situationalThoughtTargets = PatchedMethodsForPatchClass("SituationalThoughtHandler_AppendSocialThoughts_Patch");
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
			var destroyedZombieCorpses = 0;
			foreach (var corpse in map.listerThings.AllThings.OfType<ZombieCorpse>().ToArray())
			{
				corpse.Destroy();
				destroyedZombieCorpses++;
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var actorCell, out var actorSpawnError) == false)
				return actorSpawnError;
			if (TryFindClearSpawnCell(map, actorCell + new IntVec3(4, 0, 0), 10f, out var zombieCell, out var zombieSpawnError) == false)
				return zombieSpawnError;
			if (TryFindClearSpawnCell(map, actorCell + new IntVec3(-4, 0, 0), 10f, out var humanOtherCell, out var humanOtherSpawnError) == false)
				return humanOtherSpawnError;

			var actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var humanOther = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(actor, actorCell, map, Rot4.South);
			GenSpawn.Spawn(humanOther, humanOtherCell, map, Rot4.South);
			DisablePawnWork(actor);
			DisablePawnWork(humanOther);
			var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
			if (zombie == null)
			{
				return new
				{
					success = false,
					destroyedZombies,
					destroyedZombieCorpses,
					actor = DescribePawn(actor),
					humanOther = DescribePawn(humanOther),
					zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
					error = "ZombieGenerator.SpawnZombie returned no social/thought test zombie."
				};
			}

			if (TryHasAnySocialMemoryWith(actor, zombie, out var hasAnySocialMemoryWithZombie, out var socialMemoryError) == false)
			{
				return new
				{
					success = false,
					destroyedZombies,
					destroyedZombieCorpses,
					actor = DescribePawn(actor),
					humanOther = DescribePawn(humanOther),
					zombie = DescribeZombie(zombie),
					error = socialMemoryError
				};
			}

			var thoughtDef = ThoughtDefOf.DebugBad;
			var actorCanGetDebugThought = thoughtDef != null && ThoughtUtility.CanGetThought(actor, thoughtDef);
			var zombieCanGetDebugThought = thoughtDef != null && ThoughtUtility.CanGetThought(zombie, thoughtDef);
			var pawnsKnowEachOther = RelationsUtility.PawnsKnowEachOther(actor, zombie);
			var pawnsKnowEachOtherReverse = RelationsUtility.PawnsKnowEachOther(zombie, actor);
			var actorOpinionOfZombie = actor.relations?.OpinionOf(zombie) ?? int.MinValue;
			var zombieOpinionOfActor = zombie.relations?.OpinionOf(actor) ?? int.MinValue;
			var socialThoughtsAboutZombie = new List<ISocialThought>();
			actor.needs?.mood?.thoughts?.GetSocialThoughts(zombie, socialThoughtsAboutZombie);
			if (TryProbeSituationalSocialThoughtPrefix(actor, humanOther, zombie, out var situationalThoughtSuppressed, out var situationalThoughtProbe, out var situationalThoughtError) == false)
			{
				return new
				{
					success = false,
					destroyedZombies,
					destroyedZombieCorpses,
					actor = DescribePawn(actor),
					humanOther = DescribePawn(humanOther),
					zombie = DescribeZombie(zombie),
					error = situationalThoughtError
				};
			}
			var actorInteractWithZombie = actor.interactions?.TryInteractWith(zombie, InteractionDefOf.Chitchat) ?? false;
			var zombieInteractWithActor = zombie.interactions?.TryInteractWith(actor, InteractionDefOf.Chitchat) ?? false;
			if (TryProbeInteractionTickSuppression(actor, zombie, out var interactionTickSuppressed, out var interactionTickProbe, out var interactionTickError) == false)
			{
				return new
				{
					success = false,
					destroyedZombies,
					destroyedZombieCorpses,
					actor = DescribePawn(actor),
					humanOther = DescribePawn(humanOther),
					zombie = DescribeZombie(zombie),
					error = interactionTickError
				};
			}

			zombie.Kill(null);
			var zombieCorpse = zombie.Corpse as ZombieCorpse
				?? map.listerThings.AllThings.OfType<ZombieCorpse>().OrderBy(thing => thing.Position.DistanceToSquared(zombieCell)).FirstOrDefault();
			if (zombieCorpse == null)
			{
				return new
				{
					success = false,
					destroyedZombies,
					destroyedZombieCorpses,
					actor = DescribePawn(actor),
					humanOther = DescribePawn(humanOther),
					zombie = DescribeZombie(zombie),
					error = "Killing the social/thought test zombie did not leave a ZombieCorpse."
				};
			}

			var observedZombieCorpseThought = zombieCorpse.GiveObservedThought(actor);
			var observedZombieCorpseHistoryEvent = zombieCorpse.GiveObservedHistoryEvent(actor);

			return new
			{
				success = thoughtDef != null
					&& actorCanGetDebugThought
					&& zombieCanGetDebugThought == false
					&& pawnsKnowEachOther == false
					&& pawnsKnowEachOtherReverse == false
					&& hasAnySocialMemoryWithZombie == false
					&& actorOpinionOfZombie == 0
					&& zombieOpinionOfActor == 0
					&& socialThoughtsAboutZombie.Count == 0
					&& situationalThoughtTargets.Length > 0
					&& situationalThoughtSuppressed
					&& actorInteractWithZombie == false
					&& zombieInteractWithActor == false
					&& interactionTickSuppressed
					&& observedZombieCorpseThought == null
					&& observedZombieCorpseHistoryEvent == null,
				destroyedZombies,
				destroyedZombieCorpses,
				actor = DescribePawn(actor),
				humanOther = DescribePawn(humanOther),
				zombie = DescribeZombie(zombie),
				zombieCorpse = DescribeCorpse(zombieCorpse),
				patchTargets = new
				{
					situationalThoughts = situationalThoughtTargets
				},
				thoughtDef = thoughtDef?.defName,
				actorCanGetDebugThought,
				zombieCanGetDebugThought,
				pawnsKnowEachOther,
				pawnsKnowEachOtherReverse,
				hasAnySocialMemoryWithZombie,
				actorOpinionOfZombie,
				zombieOpinionOfActor,
				socialThoughtCountAboutZombie = socialThoughtsAboutZombie.Count,
				situationalThoughtProbe,
				actorInteractWithZombie,
				zombieInteractWithActor,
				interactionTickProbe,
				observedZombieCorpseThoughtDef = observedZombieCorpseThought?.def?.defName,
				observedZombieCorpseHistoryEventDef = observedZombieCorpseHistoryEvent?.defName
			};
		}

		static bool TryProbeSituationalSocialThoughtPrefix(Pawn actor, Pawn humanOther, Pawn zombie, out bool suppressed, out object evidence, out string error)
		{
			suppressed = false;
			evidence = null;
			error = null;
			var prefix = typeof(Patches)
				.GetNestedType("SituationalThoughtHandler_AppendSocialThoughts_Patch", BindingFlags.NonPublic)
				?.GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic);
			var situational = actor?.needs?.mood?.thoughts?.situational;
			if (prefix == null || situational == null || humanOther == null || zombie == null)
			{
				error = "Could not reflect SituationalThoughtHandler_AppendSocialThoughts_Patch.Prefix or build a situational thought fixture.";
				evidence = new
				{
					prefix = prefix != null,
					situational = situational != null,
					humanOther = humanOther != null,
					zombie = zombie != null
				};
				return false;
			}

			var humanOtherResult = (bool)prefix.Invoke(null, new object[] { situational, humanOther });
			var zombieOtherResult = (bool)prefix.Invoke(null, new object[] { situational, zombie });
			suppressed = humanOtherResult && zombieOtherResult == false;
			evidence = new
			{
				success = suppressed,
				humanOtherResult,
				zombieOtherResult,
				humanOther = DescribePawn(humanOther),
				zombie = DescribeZombie(zombie)
			};
			return true;
		}

		[Tool("zombieland/zombie_corpse_alert_forbid_contract", Description = "Verify normal and former-colonist zombie corpses stay out of vanilla colonist-corpse alerts and outside-home forbidding.")]
		public static object ZombieCorpseAlertForbidContract()
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
			var destroyedZombieCorpses = 0;
			foreach (var corpse in map.listerThings.AllThings.OfType<ZombieCorpse>().ToArray())
			{
				corpse.Destroy();
				destroyedZombieCorpses++;
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var workerCell, out var workerSpawnError) == false)
				return workerSpawnError;
			if (TryFindClearSpawnCell(map, workerCell + new IntVec3(3, 0, 0), 10f, out var normalZombieCell, out var normalZombieSpawnError) == false)
				return normalZombieSpawnError;
			if (TryFindClearSpawnCell(map, workerCell + new IntVec3(6, 0, 0), 12f, out var formerPawnCell, out var formerPawnSpawnError) == false)
				return formerPawnSpawnError;
			if (TryFindClearSpawnCell(map, workerCell + new IntVec3(0, 0, 3), 10f, out var humanCorpseCell, out var humanCorpseSpawnError) == false)
				return humanCorpseSpawnError;
			if (TryFindClearSpawnCell(map, workerCell + new IntVec3(-3, 0, 0), 10f, out var reserveControlCell, out var reserveControlSpawnError) == false)
				return reserveControlSpawnError;
			if (TryFindClearSpawnCell(map, workerCell + new IntVec3(0, 0, 6), 12f, out var stripHumanCell, out var stripHumanSpawnError) == false)
				return stripHumanSpawnError;
			if (TryFindClearSpawnCell(map, stripHumanCell + new IntVec3(3, 0, 0), 10f, out var stripNormalCell, out var stripNormalSpawnError) == false)
				return stripNormalSpawnError;
			if (TryFindClearSpawnCell(map, stripNormalCell + new IntVec3(3, 0, 0), 10f, out var stripSpitterCell, out var stripSpitterSpawnError) == false)
				return stripSpitterSpawnError;
			if (TryFindClearSpawnCell(map, stripSpitterCell + new IntVec3(3, 0, 0), 10f, out var stripSymbiantCell, out var stripSymbiantSpawnError) == false)
				return stripSymbiantSpawnError;

			var worker = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(worker, workerCell, map, Rot4.South);
			DisablePawnWork(worker);

			var normalZombie = ZombieRuntimeActions.SpawnZombie(normalZombieCell, map, ZombieType.Normal, true);
			if (normalZombie == null)
			{
				return new
				{
					success = false,
					destroyedZombies,
					destroyedZombieCorpses,
					worker = DescribePawn(worker),
					error = "ZombieGenerator.SpawnZombie returned no ordinary corpse test zombie."
				};
			}

			var beforeConversionIds = new HashSet<string>(CurrentZombies(map).Select(ZombieRuntimeActions.StableThingId));
			var formerPawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(formerPawn, formerPawnCell, map, Rot4.South);
			DisablePawnWork(formerPawn);
			var formerPawnBeforeConversion = DescribePawn(formerPawn);
			ZombieRuntimeActions.ConvertPawnToZombie(formerPawn, map, true);
			var formerZombie = CurrentZombies(map)
				.OfType<Zombie>()
				.Where(zombie => beforeConversionIds.Contains(ZombieRuntimeActions.StableThingId(zombie)) == false)
				.OrderBy(zombie => zombie.Position.DistanceToSquared(formerPawnCell))
				.FirstOrDefault();
			if (formerZombie == null)
			{
				return new
				{
					success = false,
					destroyedZombies,
					destroyedZombieCorpses,
					worker = DescribePawn(worker),
					formerPawn = formerPawnBeforeConversion,
					error = "Converting the former-colonist corpse test pawn did not produce a zombie."
				};
			}

			if (TryProbeZombieReservations(map, worker, normalZombie, formerZombie, reserveControlCell, out var reservationProbe, out var reservationError) == false)
			{
				return new
				{
					success = false,
					destroyedZombies,
					destroyedZombieCorpses,
					worker = DescribePawn(worker),
					normalZombie = DescribeZombie(normalZombie),
					formerZombie = DescribeZombie(formerZombie),
					reservationProbe,
					error = reservationError
				};
			}

			var stripHuman = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(stripHuman, stripHumanCell, map, Rot4.South);
			DisablePawnWork(stripHuman);
			var stripNormal = ZombieRuntimeActions.SpawnZombie(stripNormalCell, map, ZombieType.Normal, true);
			var stripSpitter = SpawnFireFixturePawn(map, stripSpitterCell, "spitter") as ZombieSpitter;
			var stripSymbiant = SpawnFireFixturePawn(map, stripSymbiantCell, "symbiant") as ZombieSymbiant;
			if (stripNormal == null || stripSpitter == null || stripSymbiant == null)
			{
				return new
				{
					success = false,
					destroyedZombies,
					destroyedZombieCorpses,
					worker = DescribePawn(worker),
					stripHuman = DescribePawn(stripHuman),
					stripNormal = DescribeZombie(stripNormal),
					stripSpitter = DescribeZombie(stripSpitter),
					stripSymbiant = DescribeZombie(stripSymbiant),
					error = "Could not spawn all strip-probe pawns."
				};
			}
			if (TryProbeAnythingToStrip(stripHuman, stripNormal, stripSpitter, stripSymbiant, out var stripProbe, out var stripError) == false)
			{
				return new
				{
					success = false,
					destroyedZombies,
					destroyedZombieCorpses,
					worker = DescribePawn(worker),
					stripProbe,
					error = stripError
				};
			}
			stripNormal.DeSpawn(DestroyMode.Vanish);
			stripSpitter.DeSpawn(DestroyMode.Vanish);
			stripSymbiant.DeSpawn(DestroyMode.Vanish);

			var humanPawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(humanPawn, humanCorpseCell, map, Rot4.South);
			DisablePawnWork(humanPawn);
			var humanPawnBeforeDeath = DescribePawn(humanPawn);
			if (ZombieRuntimeActions.KillPawnToCorpse(humanPawn, out var humanCorpse, out var killError) == false)
			{
				return new
				{
					success = false,
					destroyedZombies,
					destroyedZombieCorpses,
					worker = DescribePawn(worker),
					humanPawn = humanPawnBeforeDeath,
					error = killError
				};
			}

			normalZombie.Kill(null);
			formerZombie.Kill(null);
			var normalZombieCorpse = normalZombie.Corpse as ZombieCorpse
				?? map.listerThings.AllThings.OfType<ZombieCorpse>().OrderBy(thing => thing.Position.DistanceToSquared(normalZombieCell)).FirstOrDefault();
			var formerZombieCorpse = formerZombie.Corpse as ZombieCorpse
				?? map.listerThings.AllThings.OfType<ZombieCorpse>().OrderBy(thing => thing.Position.DistanceToSquared(formerPawnCell)).FirstOrDefault();
			if (normalZombieCorpse == null || formerZombieCorpse == null)
			{
				return new
				{
					success = false,
					destroyedZombies,
					destroyedZombieCorpses,
					normalZombie = DescribeZombie(normalZombie),
					formerZombie = DescribeZombie(formerZombie),
					normalZombieCorpse = DescribeCorpse(normalZombieCorpse),
					formerZombieCorpse = DescribeCorpse(formerZombieCorpse),
					error = "Killing the corpse test zombies did not leave both ZombieCorpse instances."
				};
			}

			var humanCorpseIsColonist = Alert_ColonistLeftUnburied.IsCorpseOfColonist(humanCorpse);
			var normalZombieCorpseIsColonist = Alert_ColonistLeftUnburied.IsCorpseOfColonist(normalZombieCorpse);
			var formerZombieCorpseIsColonist = Alert_ColonistLeftUnburied.IsCorpseOfColonist(formerZombieCorpse);

			foreach (var corpse in new Corpse[] { humanCorpse, normalZombieCorpse, formerZombieCorpse })
			{
				corpse.SetForbidden(false, false);
				map.areaManager.Home[corpse.Position] = false;
				ForbidUtility.SetForbiddenIfOutsideHomeArea(corpse);
			}
			var humanCorpseForbiddenAfterOutsideHome = humanCorpse.IsForbidden(worker);
			var normalZombieCorpseForbiddenAfterOutsideHome = normalZombieCorpse.IsForbidden(worker);
			var formerZombieCorpseForbiddenAfterOutsideHome = formerZombieCorpse.IsForbidden(worker);

			var extractWorkGiver = new WorkGiver_ExtractZombieSerum();
			var doubleTapWorkGiver = new WorkGiver_DoubleTap();
			var normalZombieCorpseHasExtractJob = extractWorkGiver.HasJobOnThing(worker, normalZombieCorpse, true);
			var formerZombieCorpseHasExtractJob = extractWorkGiver.HasJobOnThing(worker, formerZombieCorpse, true);
			var normalZombieCorpseHasDoubleTapJob = doubleTapWorkGiver.HasJobOnThing(worker, normalZombieCorpse, true);
			var formerZombieCorpseHasDoubleTapJob = doubleTapWorkGiver.HasJobOnThing(worker, formerZombieCorpse, true);
			humanCorpse.SetForbidden(false, false);
			normalZombieCorpse.SetForbidden(false, false);
			formerZombieCorpse.SetForbidden(false, false);
			if (TryProbeZombieCorpseHaul(map, worker, humanCorpse, normalZombieCorpse, out var haulProbe, out var haulError) == false)
			{
				return new
				{
					success = false,
					destroyedZombies,
					destroyedZombieCorpses,
					worker = DescribePawn(worker),
					humanCorpse = DescribeCorpse(humanCorpse),
					normalZombieCorpse = DescribeCorpse(normalZombieCorpse),
					error = haulError,
					haulProbe
				};
			}
			if (TryProbeThingMakerZombieCorpseDefs(out var thingMakerCorpseSuccess, out var thingMakerCorpseProbe, out var thingMakerCorpseError) == false)
			{
				return new
				{
					success = false,
					destroyedZombies,
					destroyedZombieCorpses,
					worker = DescribePawn(worker),
					normalZombieCorpse = DescribeCorpse(normalZombieCorpse),
					formerZombieCorpse = DescribeCorpse(formerZombieCorpse),
					error = thingMakerCorpseError
				};
			}

			return new
			{
				success = humanCorpseIsColonist
					&& normalZombieCorpseIsColonist == false
					&& formerZombieCorpseIsColonist == false
					&& humanCorpseForbiddenAfterOutsideHome
					&& normalZombieCorpseForbiddenAfterOutsideHome == false
					&& formerZombieCorpseForbiddenAfterOutsideHome == false
						&& normalZombieCorpseHasExtractJob
						&& formerZombieCorpseHasExtractJob
						&& normalZombieCorpseHasDoubleTapJob == false
						&& formerZombieCorpseHasDoubleTapJob == false
						&& ReservationProbeSuccess(reservationProbe)
						&& StripProbeSuccess(stripProbe)
						&& HaulProbeSuccess(haulProbe)
						&& thingMakerCorpseSuccess,
				destroyedZombies,
				destroyedZombieCorpses,
				worker = DescribePawn(worker),
				humanPawnBeforeDeath,
				formerPawnBeforeConversion,
				normalZombie = DescribeZombie(normalZombie),
				formerZombie = DescribeZombie(formerZombie),
				humanCorpse = DescribeCorpse(humanCorpse),
				normalZombieCorpse = DescribeCorpse(normalZombieCorpse),
				formerZombieCorpse = DescribeCorpse(formerZombieCorpse),
				humanCorpseIsColonist,
				normalZombieCorpseIsColonist,
				formerZombieCorpseIsColonist,
				humanCorpseForbiddenAfterOutsideHome,
				normalZombieCorpseForbiddenAfterOutsideHome,
				formerZombieCorpseForbiddenAfterOutsideHome,
				normalZombieCorpseHasExtractJob,
				formerZombieCorpseHasExtractJob,
				normalZombieCorpseHasDoubleTapJob,
				formerZombieCorpseHasDoubleTapJob,
				reservationProbe,
				stripProbe,
				haulProbe,
				thingMakerCorpseProbe
			};
		}

		static bool TryProbeZombieReservations(Map map, Pawn worker, Zombie normalZombie, Zombie formerZombie, IntVec3 controlCell, out object evidence, out string error)
		{
			error = null;
			var manager = map?.reservationManager;
			if (manager == null)
			{
				evidence = null;
				error = "Map has no reservation manager.";
				return false;
			}
			var controlThing = ThingMaker.MakeThing(ThingDefOf.Steel);
			controlThing.stackCount = 1;
			GenSpawn.Spawn(controlThing, controlCell, map, WipeMode.Vanish);
			controlThing.SetForbidden(false, false);
			var controlJob = JobMaker.MakeJob(JobDefOf.Wait_Combat);
			var normalJob = JobMaker.MakeJob(JobDefOf.Wait_Combat);
			var formerJob = JobMaker.MakeJob(JobDefOf.Wait_Combat);
			var controlCanReserve = manager.CanReserve(worker, controlThing, 1, -1, null, false);
			var normalCanReserve = manager.CanReserve(worker, normalZombie, 1, -1, null, false);
			var formerCanReserve = manager.CanReserve(worker, formerZombie, 1, -1, null, false);
			var controlReserve = manager.Reserve(worker, controlJob, controlThing, 1, -1, null, false, false, true);
			manager.ReleaseAllClaimedBy(worker);
			var normalReserve = manager.Reserve(worker, normalJob, normalZombie, 1, -1, null, false, false, true);
			manager.ReleaseAllClaimedBy(worker);
			var formerReserve = manager.Reserve(worker, formerJob, formerZombie, 1, -1, null, false, false, true);
			manager.ReleaseAllClaimedBy(worker);
			var success = controlCanReserve
				&& controlReserve
				&& normalCanReserve == false
				&& normalReserve == false
				&& formerCanReserve
				&& formerReserve == false;
			evidence = new
			{
				success,
				controlThing = ZombieRuntimeActions.StableThingId(controlThing),
				controlCell = ZombieRuntimeActions.DescribeCell(controlCell),
				normalZombie = DescribeZombie(normalZombie),
				formerZombie = DescribeZombie(formerZombie),
				normalWasMapPawnBefore = normalZombie.wasMapPawnBefore,
				formerWasMapPawnBefore = formerZombie.wasMapPawnBefore,
				controlCanReserve,
				controlReserve,
				normalCanReserve,
				normalReserve,
				formerCanReserve,
				formerReserve
			};
			if (success == false)
				error = "Zombie reservation probe failed.";
			return success;
		}

		static bool TryProbeAnythingToStrip(Pawn human, Zombie normal, ZombieSpitter spitter, ZombieSymbiant symbiant, out object evidence, out string error)
		{
			error = null;
			var humanPayload = TryAddStripPayload(human, out var humanPayloadEvidence, out var humanPayloadError);
			var normalPayload = TryAddStripPayload(normal, out var normalPayloadEvidence, out var normalPayloadError);
			var spitterPayload = TryAddStripPayload(spitter, out var spitterPayloadEvidence, out var spitterPayloadError);
			var symbiantPayload = TryAddStripPayload(symbiant, out var symbiantPayloadEvidence, out var symbiantPayloadError);
			var humanAnythingToStrip = human.AnythingToStrip();
			var normalAnythingToStrip = normal.AnythingToStrip();
			var spitterAnythingToStrip = spitter.AnythingToStrip();
			var symbiantAnythingToStrip = symbiant.AnythingToStrip();
			var success = humanPayload
				&& normalPayload
				&& spitterPayload
				&& symbiantPayload
				&& humanAnythingToStrip
				&& normalAnythingToStrip == false
				&& spitterAnythingToStrip == false
				&& symbiantAnythingToStrip == false;
			evidence = new
			{
				success,
				human = DescribePawn(human),
				normal = DescribeZombie(normal),
				spitter = DescribeZombie(spitter),
				symbiant = DescribeZombie(symbiant),
				humanPayload,
				normalPayload,
				spitterPayload,
				symbiantPayload,
				humanPayloadEvidence,
				normalPayloadEvidence,
				spitterPayloadEvidence,
				symbiantPayloadEvidence,
				humanPayloadError,
				normalPayloadError,
				spitterPayloadError,
				symbiantPayloadError,
				humanAnythingToStrip,
				normalAnythingToStrip,
				spitterAnythingToStrip,
				symbiantAnythingToStrip
			};
			if (success == false)
				error = "AnythingToStrip probe failed.";
			return success;
		}

		static bool TryAddStripPayload(Pawn pawn, out object evidence, out string error)
		{
			error = null;
			if (pawn?.inventory?.innerContainer == null)
			{
				evidence = new
				{
					added = false,
					error = "Pawn has no inventory inner container."
				};
				error = "Pawn has no inventory inner container.";
				return false;
			}
			var item = ThingMaker.MakeThing(ThingDefOf.Steel);
			item.stackCount = 1;
			var added = pawn.inventory.innerContainer.TryAdd(item, true);
			evidence = new
			{
				added,
				payloadDef = item.def?.defName,
				inventoryCount = pawn.inventory.innerContainer.Count,
				inventoryContents = pawn.inventory.innerContainer.Select(thing => thing.def?.defName ?? "<null>").OrderBy(name => name).ToArray()
			};
			if (added == false)
				error = $"Could not add strip payload to {pawn.LabelShort}.";
			return added;
		}

		static bool TryProbeZombieCorpseHaul(Map map, Pawn worker, Corpse humanCorpse, ZombieCorpse zombieCorpse, out object evidence, out string error)
		{
			evidence = null;
			error = null;
			if (TryFindClearSpawnCell(map, worker.Position + new IntVec3(0, 0, -5), 16f, out var stockpileCell, out var stockpileError) == false)
			{
				error = stockpileError?.ToString() ?? "Could not find a stockpile cell for haul probe.";
				return false;
			}

			var zone = new Zone_Stockpile(StorageSettingsPreset.CorpseStockpile, map.zoneManager);
			map.zoneManager.RegisterZone(zone);
			zone.AddCell(stockpileCell);
			var workGiver = new WorkGiver_HaulCorpses();
			var humanUnforcedJob = workGiver.JobOnThing(worker, humanCorpse, false);
			var zombieUnforcedJob = workGiver.JobOnThing(worker, zombieCorpse, false);
			var zombieForcedJob = workGiver.JobOnThing(worker, zombieCorpse, true);
			var success = humanUnforcedJob != null
				&& zombieUnforcedJob == null
				&& zombieForcedJob == null
				&& zone.Accepts(humanCorpse)
				&& zone.Accepts(zombieCorpse) == false;
			evidence = new
			{
				success,
				stockpileCell = ZombieRuntimeActions.DescribeCell(stockpileCell),
				zoneCellCount = zone.cells.Count,
				zoneAcceptsHumanCorpse = zone.Accepts(humanCorpse),
				zoneAcceptsZombieCorpse = zone.Accepts(zombieCorpse),
				humanCorpse = DescribeCorpse(humanCorpse),
				zombieCorpse = DescribeCorpse(zombieCorpse),
				humanUnforcedJobDef = humanUnforcedJob?.def?.defName,
				zombieUnforcedJobDef = zombieUnforcedJob?.def?.defName,
				zombieForcedJobDef = zombieForcedJob?.def?.defName,
				humanUnforcedJobTarget = DescribeJobTarget(humanUnforcedJob),
				zombieForcedJobTarget = DescribeJobTarget(zombieForcedJob)
			};
			if (success == false)
				error = "Zombie corpse haul probe failed.";
			return success;
		}

		static bool StripProbeSuccess(object probe)
		{
			return (bool)(probe?.GetType().GetProperty("success")?.GetValue(probe) ?? false);
		}

		static bool HaulProbeSuccess(object probe)
		{
			return (bool)(probe?.GetType().GetProperty("success")?.GetValue(probe) ?? false);
		}

		static bool ReservationProbeSuccess(object probe)
		{
			return (bool)(probe?.GetType().GetProperty("success")?.GetValue(probe) ?? false);
		}

		static object DescribeJobTarget(Job job)
		{
			if (job == null)
				return null;
			return new
			{
				targetA = DescribeLocalTarget(job.targetA),
				targetB = DescribeLocalTarget(job.targetB),
				targetC = DescribeLocalTarget(job.targetC)
			};
		}

		static object DescribeLocalTarget(LocalTargetInfo target)
		{
			return new
			{
				isValid = target.IsValid,
				hasThing = target.HasThing,
				thing = target.Thing == null ? null : ZombieRuntimeActions.StableThingId(target.Thing),
				cell = target.Cell.IsValid ? ZombieRuntimeActions.DescribeCell(target.Cell) : null
			};
		}

		static bool TryProbeThingMakerZombieCorpseDefs(out bool success, out object evidence, out string error)
		{
			success = false;
			evidence = null;
			error = null;
			var normalCorpseDef = CustomDefs.Corpse_Zombie;
			var spitterCorpseDef = DefDatabase<ThingDef>.AllDefs.FirstOrDefault(def => def.IsCorpse && def.ingestible?.sourceDef == CustomDefs.ZombieSpitter);
			if (normalCorpseDef == null || spitterCorpseDef == null)
			{
				error = $"Could not resolve zombie corpse defs. normal={normalCorpseDef?.defName ?? "null"}, spitter={spitterCorpseDef?.defName ?? "null"}.";
				return false;
			}

			var normalThing = ThingMaker.MakeThing(normalCorpseDef);
			var spitterThing = ThingMaker.MakeThing(spitterCorpseDef);
			var normalProps = DescribeThingMakerCorpseDef(normalCorpseDef, normalThing, typeof(ZombieCorpse));
			var spitterProps = DescribeThingMakerCorpseDef(spitterCorpseDef, spitterThing, typeof(ZombieSpitterCorpse));
			var normalOk = normalThing is ZombieCorpse && ThingMakerCorpseDefFixed(normalCorpseDef);
			var spitterOk = spitterThing is ZombieSpitterCorpse && ThingMakerCorpseDefFixed(spitterCorpseDef);
			success = normalOk && spitterOk;
			evidence = new
			{
				success,
				normal = normalProps,
				spitter = spitterProps
			};
			return true;
		}

		static object DescribeThingMakerCorpseDef(ThingDef def, Thing thing, Type expectedThingClass)
		{
			return new
			{
				defName = def.defName,
				sourceDef = def.ingestible?.sourceDef?.defName,
				thingClass = def.thingClass?.FullName,
				expectedThingClass = expectedThingClass.FullName,
				madeThingType = thing?.GetType().FullName,
				smeltable = def.smeltable,
				mineable = def.mineable,
				stealable = def.stealable,
				burnableByRecipe = def.burnableByRecipe,
				canLoadIntoCaravan = def.canLoadIntoCaravan,
				neverMultiSelect = def.neverMultiSelect,
				butcherProductsNull = def.butcherProducts == null,
				smeltProductsNull = def.smeltProducts == null,
				drawGUIOverlay = def.drawGUIOverlay,
				hasTooltip = def.hasTooltip,
				inspectorTabCount = def.inspectorTabs?.Count ?? -1,
				passability = def.passability.ToString(),
				stackLimit = def.stackLimit,
				selectable = def.selectable
			};
		}

		static bool ThingMakerCorpseDefFixed(ThingDef def)
		{
			return def.smeltable == false
				&& def.mineable == false
				&& def.stealable == false
				&& def.burnableByRecipe == false
				&& def.canLoadIntoCaravan == false
				&& def.neverMultiSelect
				&& def.butcherProducts == null
				&& def.smeltProducts == null
				&& def.drawGUIOverlay == false
				&& def.hasTooltip == false
				&& def.inspectorTabs != null
				&& def.inspectorTabs.Count == 0
				&& def.passability == Traversability.Standable
				&& def.stackLimit == 1
				&& def.selectable;
		}

		[Tool("zombieland/zombie_death_thought_suppression", Description = "Verify RimWorld death-thought delivery gives colonist death memories but suppresses adult zombie death memories.")]
		public static object ZombieDeathThoughtSuppression()
		{
			var deathThoughtTargets = PatchedMethodsForPatchClass("PawnDiedOrDownedThoughtsUtility_TryGiveThoughts_Patch");
			var nullTaleDefTargets = PatchedMethodsForPatchClass("TaleRecorder_RecordTale_Patch");
			var killedChildConstructorTargets = PatchedMethodsForPatchClass("IndividualThoughtToAdd_Constructor_Patch");
			var killedChildTaleTargets = PatchedMethodsForPatchClass("Thought_Tale_OpinionOffset_Patch");
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
			var destroyedZombieCorpses = 0;
			foreach (var corpse in map.listerThings.AllThings.OfType<ZombieCorpse>().ToArray())
			{
				corpse.Destroy();
				destroyedZombieCorpses++;
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var observerCell, out var observerSpawnError) == false)
				return observerSpawnError;
			if (TryFindClearSpawnCell(map, observerCell + new IntVec3(3, 0, 0), 10f, out var humanVictimCell, out var humanSpawnError) == false)
				return humanSpawnError;
			if (TryFindClearSpawnCell(map, observerCell + new IntVec3(6, 0, 0), 12f, out var zombieCell, out var zombieSpawnError) == false)
				return zombieSpawnError;
			if (TryFindClearSpawnCell(map, observerCell + new IntVec3(9, 0, 0), 14f, out var childZombieCell, out var childZombieSpawnError) == false)
				return childZombieSpawnError;

			var observer = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(observer, observerCell, map, Rot4.South);
			DisablePawnWork(observer);
			var humanVictim = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(humanVictim, humanVictimCell, map, Rot4.South);
			DisablePawnWork(humanVictim);
			var zombieVictim = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
			if (zombieVictim == null)
			{
				return new
				{
					success = false,
					destroyedZombies,
					destroyedZombieCorpses,
					observer = DescribePawn(observer),
					error = "ZombieGenerator.SpawnZombie returned no death-thought test zombie."
				};
			}
			var oldChildChance = ZombieSettings.Values.childChance;
			Zombie childZombieVictim;
			try
			{
				ZombieSettings.Values.childChance = 1f;
				Rand.PushState(5378);
				try
				{
					childZombieVictim = ZombieRuntimeActions.SpawnZombie(childZombieCell, map, ZombieType.Normal, true);
				}
				finally
				{
					Rand.PopState();
				}
			}
			finally
			{
				ZombieSettings.Values.childChance = oldChildChance;
			}
			if (childZombieVictim == null)
			{
				return new
				{
					success = false,
					destroyedZombies,
					destroyedZombieCorpses,
					observer = DescribePawn(observer),
					humanVictim = DescribePawn(humanVictim),
					zombieVictim = DescribeZombie(zombieVictim),
					error = "ZombieGenerator.SpawnZombie returned no child death-thought test zombie."
				};
			}
			var childZombieIsChild = childZombieVictim.DevelopmentalStage.Child();

			var memoriesBefore = TotalMemoryCount(observer);
			var memoryDefsBefore = MemoryDefCounts(observer);
			if (ZombieRuntimeActions.KillPawnToCorpse(humanVictim, out var humanCorpse, out var killHumanError) == false)
			{
				return new
				{
					success = false,
					destroyedZombies,
					destroyedZombieCorpses,
					observer = DescribePawn(observer),
					humanVictim = DescribePawn(humanVictim),
					error = killHumanError
				};
			}
			var memoriesAfterHumanKill = TotalMemoryCount(observer);
			PawnDiedOrDownedThoughtsUtility.TryGiveThoughts(humanVictim, null, PawnDiedOrDownedThoughtsKind.Died);
			var memoriesAfterHumanTryGive = TotalMemoryCount(observer);
			var humanThoughtDelta = memoriesAfterHumanTryGive - memoriesAfterHumanKill;
			var humanTotalDelta = memoriesAfterHumanTryGive - memoriesBefore;
			var memoryDefsAfterHuman = MemoryDefCounts(observer);

			zombieVictim.Kill(null);
			var zombieCorpse = zombieVictim.Corpse as ZombieCorpse
				?? map.listerThings.AllThings.OfType<ZombieCorpse>().OrderBy(thing => thing.Position.DistanceToSquared(zombieCell)).FirstOrDefault();
			if (zombieCorpse == null)
			{
				return new
				{
					success = false,
					destroyedZombies,
					destroyedZombieCorpses,
					observer = DescribePawn(observer),
					zombieVictim = DescribeZombie(zombieVictim),
					error = "Killing the death-thought test zombie did not leave a ZombieCorpse."
				};
			}

			PawnDiedOrDownedThoughtsUtility.TryGiveThoughts(zombieVictim, null, PawnDiedOrDownedThoughtsKind.Died);
			var memoriesAfterZombieTryGive = TotalMemoryCount(observer);
			var zombieThoughtDelta = memoriesAfterZombieTryGive - memoriesAfterHumanTryGive;
			var memoryDefsAfterZombie = MemoryDefCounts(observer);
			var memoriesBeforeChildTryGive = TotalMemoryCount(observer);
			var childDamageInfo = new DamageInfo(DamageDefOf.Cut, 1f, instigator: observer);
			PawnDiedOrDownedThoughtsUtility.TryGiveThoughts(childZombieVictim, childDamageInfo, PawnDiedOrDownedThoughtsKind.Died);
			var memoriesAfterChildTryGive = TotalMemoryCount(observer);
			Tale nullTaleDefResult = null;
			string nullTaleDefError = null;
			try
			{
				nullTaleDefResult = TaleRecorder.RecordTale(null, Array.Empty<object>());
			}
			catch (Exception ex)
			{
				nullTaleDefError = $"{ex.GetType().Name}: {ex.Message}";
			}
			var nullTaleDefHandled = nullTaleDefResult == null && nullTaleDefError == null;
			var childThoughtDelta = memoriesAfterChildTryGive - memoriesBeforeChildTryGive;
			var memoryDefsAfterChild = MemoryDefCounts(observer);
			var childKilledThoughtMemories = observer.needs?.mood?.thoughts?.memories?.Memories?
				.Where(memory => memory.def == ThoughtDefOf.KilledChild && memory.otherPawn == childZombieVictim)
				.ToArray() ?? Array.Empty<Thought_Memory>();
			var zombieKilledChildMoodFactor = childKilledThoughtMemories.FirstOrDefault()?.moodPowerFactor ?? -1f;
			var killedChildMoodReduced = Mathf.Abs(zombieKilledChildMoodFactor - 0.5f) < 0.0001f;

			var killedChildTaleThoughtDef = DefDatabase<ThoughtDef>.AllDefsListForReading
				.FirstOrDefault(def => def.taleDef == TaleDefOf.KilledChild && typeof(Thought_Tale).IsAssignableFrom(def.thoughtClass));
			var killedChildTaleDef = TaleDefOf.KilledChild;
			var killedChildBaseOpinion = killedChildTaleThoughtDef?.stages?.FirstOrDefault()?.baseOpinionOffset ?? 0f;
			var zombieKilledChildTale = killedChildTaleDef == null ? null : new Tale_DoublePawn(childZombieVictim, childZombieVictim)
			{
				def = killedChildTaleDef,
				date = Find.TickManager.TicksAbs
			};
			if (zombieKilledChildTale != null)
				Find.TaleManager.Add(zombieKilledChildTale);
			var killedChildTaleThought = killedChildTaleThoughtDef == null ? null : ThoughtMaker.MakeThought(killedChildTaleThoughtDef) as Thought_Tale;
			var killedChildTaleStateActiveAfterRecalculate = false;
			var killedChildTaleStateForced = false;
			if (killedChildTaleThought != null)
			{
				killedChildTaleThought.pawn = observer;
				killedChildTaleThought.otherPawn = childZombieVictim;
				killedChildTaleThought.RecalculateState();
				killedChildTaleStateActiveAfterRecalculate = killedChildTaleThought.Active;
				if (killedChildTaleThought.Active == false && killedChildTaleThoughtDef.stages.Count > 0)
				{
					typeof(Thought_Situational)
						.GetField("curStageIndex", BindingFlags.Instance | BindingFlags.NonPublic)
						?.SetValue(killedChildTaleThought, 0);
					killedChildTaleStateForced = true;
				}
			}
			var zombieKilledChildOpinionOffset = killedChildTaleThought?.OpinionOffset() ?? 0f;
			var expectedZombieKilledChildOpinionOffset = killedChildBaseOpinion * 0.25f;
			var killedChildOpinionReduced = killedChildTaleThought != null
				&& Mathf.Abs(zombieKilledChildOpinionOffset - expectedZombieKilledChildOpinionOffset) < 0.0001f;
			if (zombieKilledChildTale != null)
				Find.TaleManager.AllTalesListForReading.Remove(zombieKilledChildTale);
			var childThoughtSurfaceUnavailable = BodyTypeDefOf.Child == null
				|| ThoughtDefOf.KilledChild == null
				|| TaleDefOf.KilledChild == null
				|| killedChildTaleThoughtDef == null;
			var childThoughtEvidenceSatisfied = childThoughtSurfaceUnavailable
				|| (childZombieIsChild
					&& childThoughtDelta > 0
					&& killedChildMoodReduced
					&& killedChildOpinionReduced);
			var childThoughtSkipReason = childThoughtSurfaceUnavailable
				? "Child/killed-child thought surface is unavailable in the active RimWorld configuration."
				: null;

			var observerDescription = DescribePawn(observer);
			var humanVictimDescription = DescribePawn(humanVictim);
			var humanCorpseDescription = DescribeCorpse(humanCorpse);
			var zombieVictimDescription = DescribeZombie(zombieVictim);
			var zombieCorpseDescription = DescribeCorpse(zombieCorpse);
			var childZombieDescription = DescribeZombie(childZombieVictim);
			if (zombieCorpse.Destroyed == false)
				zombieCorpse.Destroy();
			if (childZombieVictim.Destroyed == false)
				childZombieVictim.Destroy();
			if (humanCorpse != null && humanCorpse.Destroyed == false)
				humanCorpse.Destroy();
			if (observer.Destroyed == false)
				observer.Destroy();

			return new
			{
				success = deathThoughtTargets.Length > 0
					&& nullTaleDefTargets.Length > 0
					&& nullTaleDefHandled
					&& killedChildConstructorTargets.Length > 0
					&& killedChildTaleTargets.Length > 0
					&& humanThoughtDelta > 0
					&& humanTotalDelta > 0
					&& zombieThoughtDelta == 0
					&& childThoughtEvidenceSatisfied,
				patchTargets = new
				{
					deathThoughts = deathThoughtTargets,
					nullTaleDef = nullTaleDefTargets,
					killedChildConstructor = killedChildConstructorTargets,
					killedChildTaleOpinion = killedChildTaleTargets
				},
				destroyedZombies,
				destroyedZombieCorpses,
				observer = observerDescription,
				humanVictim = humanVictimDescription,
				humanCorpse = humanCorpseDescription,
				zombieVictim = zombieVictimDescription,
				zombieCorpse = zombieCorpseDescription,
				childZombieVictim = childZombieDescription,
				childZombieIsChild,
				childThoughtSurfaceUnavailable,
				childThoughtSkipReason,
				childThoughtEvidenceSatisfied,
				nullTaleDefHandled,
				nullTaleDefError,
				memoriesBefore,
				memoriesAfterHumanKill,
				memoriesAfterHumanTryGive,
				memoriesAfterZombieTryGive,
				memoriesBeforeChildTryGive,
				memoriesAfterChildTryGive,
				humanThoughtDelta,
				humanTotalDelta,
				zombieThoughtDelta,
				childThoughtDelta,
				memoryDefsBefore,
				memoryDefsAfterHuman,
				memoryDefsAfterZombie,
				memoryDefsAfterChild,
				childKilledThoughtMemoryCount = childKilledThoughtMemories.Length,
				zombieKilledChildMoodFactor,
				killedChildMoodReduced,
				killedChildTaleThoughtDef = killedChildTaleThoughtDef?.defName,
				killedChildBaseOpinion,
				killedChildTaleStateActiveAfterRecalculate,
				killedChildTaleStateForced,
				zombieKilledChildOpinionOffset,
				expectedZombieKilledChildOpinionOffset,
				killedChildOpinionReduced
			};
		}

		[Tool("zombieland/zombie_damage_memory_suppression", Description = "Verify normal pawn damage can create harm memories while zombie-instigated damage does not create social memories about zombies.")]
		public static object ZombieDamageMemorySuppression()
		{
			var patchTargets = PatchedMethodsForPatchClass("Pawn_HealthTracker_PreApplyDamage_Patch");
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
			var spawnedThings = new List<Thing>();

			try
			{
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var humanAttackerCell, out var humanAttackerSpawnError) == false)
					return humanAttackerSpawnError;
				if (TryFindClearSpawnCell(map, humanAttackerCell + new IntVec3(3, 0, 0), 10f, out var humanVictimCell, out var humanVictimSpawnError) == false)
					return humanVictimSpawnError;
				if (TryFindClearSpawnCell(map, humanAttackerCell + new IntVec3(6, 0, 0), 12f, out var zombieAttackerCell, out var zombieAttackerSpawnError) == false)
					return zombieAttackerSpawnError;
				if (TryFindClearSpawnCell(map, humanAttackerCell + new IntVec3(9, 0, 0), 14f, out var zombieVictimCell, out var zombieVictimSpawnError) == false)
					return zombieVictimSpawnError;

				var humanAttacker = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				var humanVictim = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				var zombieVictim = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(humanAttacker, humanAttackerCell, map, Rot4.South);
				GenSpawn.Spawn(humanVictim, humanVictimCell, map, Rot4.South);
				GenSpawn.Spawn(zombieVictim, zombieVictimCell, map, Rot4.South);
				spawnedThings.Add(humanAttacker);
				spawnedThings.Add(humanVictim);
				spawnedThings.Add(zombieVictim);
				DisablePawnWork(humanAttacker);
				DisablePawnWork(humanVictim);
				DisablePawnWork(zombieVictim);
				var zombieAttacker = ZombieRuntimeActions.SpawnZombie(zombieAttackerCell, map, ZombieType.Normal, true);
				if (zombieAttacker != null)
					spawnedThings.Add(zombieAttacker);
				if (zombieAttacker == null)
				{
					return new
					{
						success = false,
						destroyedZombies,
						patchTargets,
						humanAttacker = DescribePawn(humanAttacker),
						humanVictim = DescribePawn(humanVictim),
						zombieVictim = DescribePawn(zombieVictim),
						error = "ZombieGenerator.SpawnZombie returned no damage-memory test zombie."
					};
				}

				var humanVictimMemoriesBefore = TotalMemoryCount(humanVictim);
				var humanVictimDefsBefore = MemoryDefCounts(humanVictim);
				var humanDamage = humanVictim.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 2f, 0f, -1f, humanAttacker, null, null, DamageInfo.SourceCategory.ThingOrUnknown, humanVictim, true, true));
				var humanVictimMemoriesAfter = TotalMemoryCount(humanVictim);
				var humanVictimDefsAfter = MemoryDefCounts(humanVictim);
				var humanDamageMemoryDelta = humanVictimMemoriesAfter - humanVictimMemoriesBefore;

				var zombieVictimMemoriesBefore = TotalMemoryCount(zombieVictim);
				var zombieVictimDefsBefore = MemoryDefCounts(zombieVictim);
				var zombieDamage = zombieVictim.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 2f, 0f, -1f, zombieAttacker, null, null, DamageInfo.SourceCategory.ThingOrUnknown, zombieVictim, true, true));
				var zombieVictimMemoriesAfter = TotalMemoryCount(zombieVictim);
				var zombieVictimDefsAfter = MemoryDefCounts(zombieVictim);
				var zombieDamageMemoryDelta = zombieVictimMemoriesAfter - zombieVictimMemoriesBefore;

				return new
				{
					success = patchTargets.Length > 0
						&& humanDamageMemoryDelta > 0
						&& zombieDamageMemoryDelta == 0,
					destroyedZombies,
					patchTargets,
					humanAttacker = DescribePawn(humanAttacker),
					humanVictim = DescribePawn(humanVictim),
					zombieAttacker = DescribeZombie(zombieAttacker),
					zombieVictim = DescribePawn(zombieVictim),
					humanDamageTotal = humanDamage.totalDamageDealt,
					zombieDamageTotal = zombieDamage.totalDamageDealt,
					humanVictimMemoriesBefore,
					humanVictimMemoriesAfter,
					humanDamageMemoryDelta,
					zombieVictimMemoriesBefore,
					zombieVictimMemoriesAfter,
					zombieDamageMemoryDelta,
					humanVictimDefsBefore,
					humanVictimDefsAfter,
					zombieVictimDefsBefore,
					zombieVictimDefsAfter
				};
			}
			finally
			{
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}
		}

		[Tool("zombieland/zombie_health_needs_upkeep_suppression", Description = "Verify zombie needs/upkeep suppression and alert-tracked infected-colonist health overrides while normal pawns keep vanilla behavior.")]
		public static object ZombieHealthNeedsUpkeepSuppression()
		{
			var allNeedsTargets = PatchedMethodsForPatchClass("Pawn_NeedsTracker_AllNeeds_Patch");
			var addOrRemoveNeedsTargets = PatchedMethodsForPatchClass("Pawn_NeedsTracker_AddOrRemoveNeedsAsAppropriate_Patch");
			var needsTickTargets = PatchedMethodsForPatchClass("Pawn_NeedsTracker_NeedsTrackerTickInterval_Patch");
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
			if (TryFindClearSpawnCell(map, humanCell + new IntVec3(4, 0, 0), 10f, out var zombieCell, out var zombieSpawnError) == false)
				return zombieSpawnError;

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
					error = "ZombieGenerator.SpawnZombie returned no health/needs test zombie."
				};
			}

			var needDef = NeedDefOf.Food;
			var humanNeedsBefore = DescribeNeeds(human);
			human.needs.AddOrRemoveNeedsAsAppropriate();
			var humanNeedsAfterReconcile = DescribeNeeds(human);

			var zombieNeedsBefore = DescribeNeeds(zombie);
			if (TryForceAddNeed(zombie, needDef, out var needError) == false)
			{
				return new
				{
					success = false,
					destroyedZombies,
					human = DescribePawn(human),
					zombie = DescribeZombie(zombie),
					zombieNeedsBefore,
					error = needError
				};
			}
			var zombieNeedsAfterForcedNeed = DescribeNeeds(zombie);
			zombie.needs.AddOrRemoveNeedsAsAppropriate();
			var zombieNeedsAfterReconcile = DescribeNeeds(zombie);

			if (TryForceAddNeed(zombie, needDef, out needError) == false)
			{
				return new
				{
					success = false,
					destroyedZombies,
					human = DescribePawn(human),
					zombie = DescribeZombie(zombie),
					zombieNeedsAfterReconcile,
					error = needError
				};
			}
			var zombieFoodBeforeTick = zombie.needs.TryGetNeed(needDef)?.CurLevel ?? -1f;
			zombie.needs.NeedsTrackerTickInterval(150);
			var zombieFoodAfterTick = zombie.needs.TryGetNeed(needDef)?.CurLevel ?? -1f;
			var zombieNeedsAfterTick = DescribeNeeds(zombie);
			zombie.needs.AddOrRemoveNeedsAsAppropriate();
			var zombieNeedsAfterFinalReconcile = DescribeNeeds(zombie);

			var diseaseDef = HediffDefOf.Plague;
			human.health.AddHediff(HediffMaker.MakeHediff(diseaseDef, human));
			zombie.health.AddHediff(HediffMaker.MakeHediff(diseaseDef, zombie));
			var humanImmunityBefore = ImmunityFor(human, diseaseDef);
			var zombieImmunityBefore = ImmunityFor(zombie, diseaseDef);
			var humanImmunityRecordCountBefore = ImmunityRecordCount(human);
			var zombieImmunityRecordCountBefore = ImmunityRecordCount(zombie);
			const int oneDayTicks = 60000;
			human.health.immunity.ImmunityHandlerTickInterval(oneDayTicks);
			zombie.health.immunity.ImmunityHandlerTickInterval(oneDayTicks);
			var humanImmunityAfter = ImmunityFor(human, diseaseDef);
			var zombieImmunityAfter = ImmunityFor(zombie, diseaseDef);
			var humanImmunityRecordCountAfter = ImmunityRecordCount(human);
			var zombieImmunityRecordCountAfter = ImmunityRecordCount(zombie);

			var humanNeedsPopulated = humanNeedsAfterReconcile.internalCount > 0;
			var zombieForcedNeedVisibleInternally = zombieNeedsAfterForcedNeed.internalCount > 0;
			var zombieNeedsClearedByReconcile = zombieNeedsAfterReconcile.internalCount == 0
				&& zombieNeedsAfterReconcile.allCount == 0
				&& zombieNeedsAfterReconcile.hasFoodField == false;
			var zombieNeedTickSkipped = zombieFoodAfterTick == zombieFoodBeforeTick;
			var zombieNeedsClearedAfterTick = zombieNeedsAfterFinalReconcile.internalCount == 0
				&& zombieNeedsAfterFinalReconcile.allCount == 0;
			var humanImmunityAdvanced = humanImmunityAfter > humanImmunityBefore && humanImmunityRecordCountAfter > humanImmunityRecordCountBefore;
			var zombieImmunitySuppressed = zombieImmunityAfter == zombieImmunityBefore && zombieImmunityRecordCountAfter == zombieImmunityRecordCountBefore;
			var infectedColonistHealthSuppressed = TryProbeInfectedColonistHealthSuppression(map, zombieCell + new IntVec3(4, 0, 0), out var infectedColonistHealthEvidence, out var infectedColonistHealthError);

			return new
			{
				success = allNeedsTargets.Length > 0
					&& addOrRemoveNeedsTargets.Length > 0
					&& needsTickTargets.Length > 0
					&& humanNeedsPopulated
					&& zombieForcedNeedVisibleInternally
					&& zombieNeedsClearedByReconcile
					&& zombieNeedTickSkipped
					&& zombieNeedsClearedAfterTick
					&& humanImmunityAdvanced
					&& zombieImmunitySuppressed
					&& infectedColonistHealthSuppressed,
				patchTargets = new
				{
					allNeeds = allNeedsTargets,
					addOrRemoveNeedsAsAppropriate = addOrRemoveNeedsTargets,
					needsTrackerTickInterval = needsTickTargets
				},
				destroyedZombies,
				human = DescribePawn(human),
				zombie = DescribeZombie(zombie),
				needDef = needDef.defName,
				diseaseDef = diseaseDef.defName,
				immunityTickWindow = oneDayTicks,
				humanNeedsBefore,
				humanNeedsAfterReconcile,
				zombieNeedsBefore,
				zombieNeedsAfterForcedNeed,
				zombieNeedsAfterReconcile,
				zombieFoodBeforeTick,
				zombieFoodAfterTick,
				zombieNeedsAfterTick,
				zombieNeedsAfterFinalReconcile,
				humanNeedsPopulated,
				zombieForcedNeedVisibleInternally,
				zombieNeedsClearedByReconcile,
				zombieNeedTickSkipped,
				zombieNeedsClearedAfterTick,
				humanImmunityBefore,
				humanImmunityAfter,
				zombieImmunityBefore,
				zombieImmunityAfter,
				humanImmunityRecordCountBefore,
				humanImmunityRecordCountAfter,
				zombieImmunityRecordCountBefore,
				zombieImmunityRecordCountAfter,
				humanImmunityAdvanced,
				zombieImmunitySuppressed,
				infectedColonistHealthSuppressed,
				infectedColonistHealthError,
				infectedColonistHealthEvidence
			};
		}

		static bool TryProbeInfectedColonistHealthSuppression(Map map, IntVec3 root, out object evidence, out string error)
		{
			evidence = null;
			error = null;
			if (TryFindClearSpawnCell(map, root, 16f, out var controlCell, out var controlSpawnError) == false)
			{
				error = controlSpawnError?.ToString() ?? "Could not find control colonist spawn cell.";
				return false;
			}
			if (TryFindClearSpawnCell(map, controlCell + new IntVec3(4, 0, 0), 10f, out var infectedCell, out var infectedSpawnError) == false)
			{
				error = infectedSpawnError?.ToString() ?? "Could not find infected colonist spawn cell.";
				return false;
			}
			if (TryFindClearSpawnCell(map, infectedCell + new IntVec3(4, 0, 0), 10f, out var deadInfectedCell, out var deadSpawnError) == false)
			{
				error = deadSpawnError?.ToString() ?? "Could not find dead infected colonist spawn cell.";
				return false;
			}
			if (TryFindClearSpawnCell(map, deadInfectedCell + new IntVec3(4, 0, 0), 10f, out var controlMentalCell, out var controlMentalSpawnError) == false)
			{
				error = controlMentalSpawnError?.ToString() ?? "Could not find control mental-state spawn cell.";
				return false;
			}
			if (TryFindClearSpawnCell(map, controlMentalCell + new IntVec3(4, 0, 0), 10f, out var infectedMentalCell, out var infectedMentalSpawnError) == false)
			{
				error = infectedMentalSpawnError?.ToString() ?? "Could not find infected mental-state spawn cell.";
				return false;
			}

			var control = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var infected = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var deadInfected = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var controlMental = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var infectedMental = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(control, controlCell, map, Rot4.South);
			GenSpawn.Spawn(infected, infectedCell, map, Rot4.South);
			GenSpawn.Spawn(deadInfected, deadInfectedCell, map, Rot4.South);
			GenSpawn.Spawn(controlMental, controlMentalCell, map, Rot4.South);
			GenSpawn.Spawn(infectedMental, infectedMentalCell, map, Rot4.South);
			DisablePawnWork(control);
			DisablePawnWork(infected);
			DisablePawnWork(deadInfected);
			DisablePawnWork(controlMental);
			DisablePawnWork(infectedMental);
			controlMental.equipment?.DestroyAllEquipment(DestroyMode.Vanish);
			var controlMentalChainsaw = ThingMaker.MakeThing(CustomDefs.Chainsaw) as Chainsaw;
			if (controlMentalChainsaw == null)
			{
				error = "Could not create Chainsaw for mental-state drop probe.";
				return false;
			}
			controlMental.equipment.AddEquipment(controlMentalChainsaw);
			var chainsawId = ZombieRuntimeActions.StableThingId(controlMentalChainsaw);
			var chainsawEquippedBeforeMental = ReferenceEquals(controlMental.equipment.Primary, controlMentalChainsaw)
				&& controlMentalChainsaw.Spawned == false
				&& ReferenceEquals(controlMentalChainsaw.pawn, controlMental);

			if (ZombieRuntimeActions.AddZombieBite(infected, "final", out var infectedBite, out error) == false)
				return false;
			if (ZombieRuntimeActions.AddZombieBite(infectedMental, "final", out var infectedMentalBite, out error) == false)
				return false;

			var tracked = Patches.Need_CurLevel_Patch.infectedColonists ??= new HashSet<Pawn>();
			var controlWasTracked = tracked.Contains(control);
			var infectedWasTracked = tracked.Contains(infected);
			var deadInfectedWasTracked = tracked.Contains(deadInfected);
			var controlMentalWasTracked = tracked.Contains(controlMental);
			var infectedMentalWasTracked = tracked.Contains(infectedMental);
			try
			{
				tracked.Remove(control);
				tracked.Add(infected);
				tracked.Add(deadInfected);
				tracked.Remove(controlMental);
				tracked.Add(infectedMental);

				control.needs.AddOrRemoveNeedsAsAppropriate();
				infected.needs.AddOrRemoveNeedsAsAppropriate();
				var controlNeed = control.needs.TryGetNeed(NeedDefOf.Food);
				var infectedNeed = infected.needs.TryGetNeed(NeedDefOf.Food);
				if (controlNeed == null || infectedNeed == null)
				{
					error = $"Food need missing. control={controlNeed != null}, infected={infectedNeed != null}.";
					return false;
				}

				const float requestedNeedLevel = 0.13f;
				controlNeed.CurLevel = requestedNeedLevel;
				infectedNeed.CurLevel = requestedNeedLevel;
				var controlNeedAfterSet = controlNeed.CurLevel;
				var infectedNeedAfterLowSet = infectedNeed.CurLevel;
				infectedNeed.CurLevel = 0.93f;
				var infectedNeedAfterHighSet = infectedNeed.CurLevel;

				if (TryApplyPainfulCut(control, out error) == false)
					return false;
				if (TryApplyPainfulCut(infected, out error) == false)
					return false;
				var controlPainAfterCut = control.health.hediffSet.PainTotal;
				var infectedPainAfterCut = infected.health.hediffSet.PainTotal;

				ApplyAnestheticCapacitySuppressor(control);
				ApplyAnestheticCapacitySuppressor(infected);
				var controlConsciousnessAfterAnesthetic = control.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness);
				var infectedConsciousnessAfterAnesthetic = infected.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness);
				var infectedMovingAfterAnesthetic = infected.health.capacities.GetLevel(PawnCapacityDefOf.Moving);
				if (ZombieRuntimeActions.KillPawnToCorpse(deadInfected, out var deadInfectedCorpse, out error) == false)
					return false;
				var deadInfectedConsciousness = deadInfected.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness);

				var controlMentalStarted = controlMental.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.Berserk, "Zombieland bridge control", true, true);
				var chainsawDroppedAfterMental = controlMentalStarted
					&& controlMental.equipment.Primary == null
					&& controlMentalChainsaw.Spawned
					&& ReferenceEquals(controlMentalChainsaw.pawn, null)
					&& controlMentalChainsaw.Position == controlMental.Position;
				var infectedMentalStarted = infectedMental.mindState.mentalStateHandler.TryStartMentalState(MentalStateDefOf.Berserk, "Zombieland bridge infected", true, true);

				var controlNeedVanilla = Approximately(controlNeedAfterSet, requestedNeedLevel);
				var infectedNeedForcedAverage = Approximately(infectedNeedAfterLowSet, 0.5f) && Approximately(infectedNeedAfterHighSet, 0.5f);
				var controlPainVanilla = controlPainAfterCut > 0.001f;
				var infectedPainSuppressed = Approximately(infectedPainAfterCut, 0f);
				var controlCapacitySuppressed = controlConsciousnessAfterAnesthetic < 0.5f;
				var infectedCapacityFull = Approximately(infectedConsciousnessAfterAnesthetic, 1f) && Approximately(infectedMovingAfterAnesthetic, 1f);
				var deadInfectedCapacityVanilla = deadInfectedConsciousness <= 0.001f;
				var infectedMentalSuppressed = controlMentalStarted && infectedMentalStarted == false;

				evidence = new
				{
					control = DescribePawn(control),
					infected = DescribePawn(infected),
					deadInfected = DescribePawn(deadInfected),
					controlMental = DescribePawn(controlMental),
					infectedMental = DescribePawn(infectedMental),
					infectedBite = infectedBite.LabelCap,
					infectedMentalBite = infectedMentalBite.LabelCap,
					trackedCount = tracked.Count,
					chainsawId,
					chainsawEquippedBeforeMental,
					chainsawDroppedAfterMental,
					chainsawSpawnedAfterMental = controlMentalChainsaw.Spawned,
					chainsawPositionAfterMental = ZombieRuntimeActions.DescribeCell(controlMentalChainsaw.Position),
					requestedNeedLevel,
					controlNeedAfterSet,
					infectedNeedAfterLowSet,
					infectedNeedAfterHighSet,
					controlPainAfterCut,
					infectedPainAfterCut,
					controlConsciousnessAfterAnesthetic,
					infectedConsciousnessAfterAnesthetic,
					infectedMovingAfterAnesthetic,
					deadInfectedConsciousness,
					deadInfectedCorpse = DescribeCorpse(deadInfectedCorpse),
					controlMentalStarted,
					infectedMentalStarted,
					controlNeedVanilla,
					infectedNeedForcedAverage,
					controlPainVanilla,
					infectedPainSuppressed,
					controlCapacitySuppressed,
					infectedCapacityFull,
					deadInfectedCapacityVanilla,
					infectedMentalSuppressed
				};
				return controlNeedVanilla
					&& infectedNeedForcedAverage
					&& controlPainVanilla
					&& infectedPainSuppressed
					&& controlCapacitySuppressed
					&& infectedCapacityFull
					&& deadInfectedCapacityVanilla
					&& infectedMentalSuppressed
					&& chainsawEquippedBeforeMental
					&& chainsawDroppedAfterMental;
			}
			finally
			{
				RestoreTrackedPawn(tracked, control, controlWasTracked);
				RestoreTrackedPawn(tracked, infected, infectedWasTracked);
				RestoreTrackedPawn(tracked, deadInfected, deadInfectedWasTracked);
				RestoreTrackedPawn(tracked, controlMental, controlMentalWasTracked);
				RestoreTrackedPawn(tracked, infectedMental, infectedMentalWasTracked);
			}
		}

		static void RestoreTrackedPawn(HashSet<Pawn> tracked, Pawn pawn, bool wasTracked)
		{
			if (wasTracked)
				tracked.Add(pawn);
			else
				tracked.Remove(pawn);
		}

		static bool TryApplyPainfulCut(Pawn pawn, out string error)
		{
			error = null;
			var part = pawn.health.hediffSet.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Outside)
				.FirstOrDefault(record => record.def == BodyPartDefOf.Torso)
				?? pawn.health.hediffSet.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Outside).FirstOrDefault();
			if (part == null)
			{
				error = $"Could not find a cut target body part for {pawn.LabelShort}.";
				return false;
			}
			var cut = HediffMaker.MakeHediff(HediffDefOf.Cut, pawn, part);
			cut.Severity = 8f;
			pawn.health.AddHediff(cut, part, new DamageInfo(DamageDefOf.Cut, 8f));
			return true;
		}

		static readonly FieldInfo thingFactionIntField = typeof(Thing).GetField("factionInt", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		static readonly FieldInfo interactionsWantsRandomInteractField = typeof(Pawn_InteractionsTracker).GetField("wantsRandomInteract", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

		static bool TryProbeInteractionTickSuppression(Pawn controlPawn, Pawn zombie, out bool success, out object evidence, out string error)
		{
			success = false;
			evidence = null;
			error = null;
			if (controlPawn?.interactions == null || zombie?.interactions == null)
			{
				error = "Interaction tick probe requires both pawns to have interaction trackers.";
				return false;
			}
			if (thingFactionIntField == null || interactionsWantsRandomInteractField == null)
			{
				error = "Interaction tick probe could not resolve required RimWorld private fields.";
				return false;
			}

			var controlFactionBefore = controlPawn.Faction;
			var zombieFactionBefore = zombie.Faction;
			try
			{
				thingFactionIntField.SetValue(controlPawn, null);
				thingFactionIntField.SetValue(zombie, null);
				interactionsWantsRandomInteractField.SetValue(controlPawn.interactions, true);
				interactionsWantsRandomInteractField.SetValue(zombie.interactions, true);

				var controlBeforeTick = (bool)interactionsWantsRandomInteractField.GetValue(controlPawn.interactions);
				var zombieBeforeTick = (bool)interactionsWantsRandomInteractField.GetValue(zombie.interactions);
				const int tickInterval = 60;
				controlPawn.interactions.InteractionsTrackerTickInterval(tickInterval);
				zombie.interactions.InteractionsTrackerTickInterval(tickInterval);
				var controlAfterTick = (bool)interactionsWantsRandomInteractField.GetValue(controlPawn.interactions);
				var zombieAfterTick = (bool)interactionsWantsRandomInteractField.GetValue(zombie.interactions);
				success = controlBeforeTick
					&& zombieBeforeTick
					&& controlAfterTick == false
					&& zombieAfterTick;

				evidence = new
				{
					success,
					tickInterval,
					controlFactionForcedNull = controlPawn.Faction == null,
					zombieFactionForcedNull = zombie.Faction == null,
					controlBeforeTick,
					controlAfterTick,
					zombieBeforeTick,
					zombieAfterTick
				};
				return true;
			}
			finally
			{
				thingFactionIntField.SetValue(controlPawn, controlFactionBefore);
				thingFactionIntField.SetValue(zombie, zombieFactionBefore);
			}
		}

	}
}
