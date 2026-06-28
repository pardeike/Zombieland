using RimBridgeServer.Annotations;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace ZombieLand
{
	public sealed partial class ZombielandBridgeTools
	{
		[Tool("zombieland/read_contamination_state", Description = "Read contamination values for current-map cells, pawns, and spawned things using reusable target lists.")]
		public static object ReadContaminationState(
			[ToolParameter(Description = "Optional semicolon-separated cell list like '100,100;101,100'.", Required = false, DefaultValue = "")] string cells = "",
			[ToolParameter(Description = "Optional semicolon-separated pawn id, ThingID, label, or short-name list.", Required = false, DefaultValue = "")] string pawns = "",
			[ToolParameter(Description = "Optional semicolon-separated thing id, ThingID, or label list.", Required = false, DefaultValue = "")] string things = "")
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

			var cellTargets = SplitTargetList(cells);
			var pawnTargets = SplitTargetList(pawns);
			var thingTargets = SplitTargetList(things);
			var cellEntries = cellTargets.Select(entry => DescribeContaminationCell(map, entry)).ToArray();
			var pawnEntries = pawnTargets.Select(entry => DescribeContaminationPawn(map, entry)).ToArray();
			var thingEntries = thingTargets.Select(entry => DescribeContaminationThing(map, entry)).ToArray();
			var missing = cellTargets.Count(entry => TryParseCell(entry, out var cell) == false || cell.InBounds(map) == false)
				+ pawnTargets.Count(entry => ZombieRuntimeActions.TryFindPawn(map, entry, out _, out _) == false)
				+ thingTargets.Count(entry => FindThing(map, entry) == null);

			return new
			{
				success = missing == 0,
				map = new
				{
					index = map.Index,
					uniqueId = map.uniqueID,
					size = new
					{
						x = map.Size.x,
						z = map.Size.z
					}
				},
				requested = new
				{
					cellCount = cellEntries.Length,
					pawnCount = pawnEntries.Length,
					thingCount = thingEntries.Length
				},
				missing,
				cells = cellEntries,
				pawns = pawnEntries,
				things = thingEntries
			};
		}

		[Tool("zombieland/read_contamination_effect_state", Description = "Read active contamination mental/job driver state for reusable pawn target lists, optionally advancing ticks first.")]
		public static object ReadContaminationEffectState(
			[ToolParameter(Description = "Optional semicolon-separated pawn id, ThingID, label, or short-name list. Empty returns all spawned pawns with contamination, contamination mental states, or contamination jobs.", Required = false, DefaultValue = "")] string pawns = "",
			[ToolParameter(Description = "Optional game ticks to advance before reading; clamped to 0..5000.", Required = false, DefaultValue = 0)] int advanceTicks = 0)
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

			var clampedAdvanceTicks = Mathf.Clamp(advanceTicks, 0, 5000);
			if (clampedAdvanceTicks > 0)
				AdvanceGameTicks(clampedAdvanceTicks);

			var targets = SplitTargetList(pawns);
			var entries = new List<object>();
			var missing = 0;
			if (targets.Length == 0)
			{
				entries.AddRange(map.mapPawns.AllPawnsSpawned
					.Where(IsContaminationEffectPawn)
					.OrderBy(pawn => pawn.thingIDNumber)
					.Select(DescribeContaminationEffectPawn));
			}
			else
			{
				foreach (var target in targets)
				{
					if (ZombieRuntimeActions.TryFindPawn(map, target, out var pawn, out var error))
					{
						entries.Add(DescribeContaminationEffectPawn(pawn));
					}
					else
					{
						missing++;
						entries.Add(new
						{
							query = target,
							found = false,
							error
						});
					}
				}
			}

			return new
			{
				success = missing == 0,
				ticksGame = Find.TickManager.TicksGame,
				advancedTicks = clampedAdvanceTicks,
				requestedTargets = targets.Length,
				missing,
				pawnCount = entries.Count,
				pawns = entries.ToArray()
			};
		}

		[Tool("zombieland/write_contamination_state", Description = "Set one contamination value on reusable current-map cell, pawn, and thing target lists, then read the resulting state.")]
		public static object WriteContaminationState(
		[ToolParameter(Description = "Contamination value to set; clamped to 0..1.", Required = true)] float value,
		[ToolParameter(Description = "Optional semicolon-separated cell list like '100,100;101,100'.", Required = false, DefaultValue = "")] string cells = "",
		[ToolParameter(Description = "Optional semicolon-separated pawn id, ThingID, label, or short-name list.", Required = false, DefaultValue = "")] string pawns = "",
		[ToolParameter(Description = "Optional semicolon-separated thing id, ThingID, or label list.", Required = false, DefaultValue = "")] string things = "")
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

			var clampedValue = Mathf.Clamp01(value);
			var failures = 0;
			var cellResults = new List<object>();
			foreach (var entry in SplitTargetList(cells))
			{
				if (WriteContaminationCell(map, entry, clampedValue, out var result) == false)
					failures++;
				cellResults.Add(result);
			}
			var pawnResults = new List<object>();
			foreach (var entry in SplitTargetList(pawns))
			{
				if (WriteContaminationPawn(map, entry, clampedValue, out var result) == false)
					failures++;
				pawnResults.Add(result);
			}
			var thingResults = new List<object>();
			foreach (var entry in SplitTargetList(things))
			{
				if (WriteContaminationThing(map, entry, clampedValue, out var result) == false)
					failures++;
				thingResults.Add(result);
			}

			return new
			{
				success = failures == 0,
				requestedValue = value,
				value = clampedValue,
				failures,
				cells = cellResults.ToArray(),
				pawns = pawnResults.ToArray(),
				things = thingResults.ToArray(),
				state = ReadContaminationState(cells, pawns, things)
			};
		}

		[Tool("zombieland/contamination_ui_quest_contract", Description = "Prepare and verify contamination player-facing UI state plus decontamination quest availability from a reusable current-map fixture.")]
		public static object ContaminationUiQuestContract(
			[ToolParameter(Description = "Create a contaminated thing/cell fixture and select it for inspect-pane and mouseover UI checks.", Required = false, DefaultValue = true)] bool createVisualFixture = true,
			[ToolParameter(Description = "Contamination value for the created visual fixture. Defaults to a clearly visible 65%; use values below 0.001 for visual-threshold edge checks.", Required = false, DefaultValue = 0.65f)] float visualFixtureContamination = 0.65f,
			[ToolParameter(Description = "Create temporary quest preconditions and generate a decontamination quest if none is present.", Required = false, DefaultValue = true)] bool createQuest = true,
			[ToolParameter(Description = "Destroy only the newly created visual fixture before returning. Leave false for screenshot/save-load testing.", Required = false, DefaultValue = false)] bool cleanupVisualFixture = false)
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var fixtureContamination = Mathf.Clamp01(visualFixtureContamination);
			var createdThings = new List<Thing>();
			var createdColonists = new List<Pawn>();
			var createdQuestException = (string)null;
			var visualCell = IntVec3.Invalid;
			Thing visualThing = null;

			object DescribeStatEntry(StatDrawEntry entry) => new
			{
				category = entry.category?.defName,
				label = entry.LabelCap,
				value = entry.ValueString
			};

			object DescribeQuest(Quest quest)
			{
				if (quest == null)
					return null;

				var parts = quest.PartsListForReading ?? new List<QuestPart>();
				return new
				{
					id = quest.id,
					name = quest.name,
					root = quest.root?.defName,
					state = quest.State.ToString(),
					challengeRating = quest.challengeRating,
					partCount = parts.Count,
					hasDecontaminationPart = parts.Any(part => part is QuestPart_DecontaminateColonists),
					partTypes = parts.Select(part => part.GetType().Name).ToArray()
				};
			}

			object InvokePatchHelper(string typeName, string methodName, params object[] args)
			{
				var type = typeof(ZombielandBridgeTools).Assembly.GetType(typeName);
				var method = type?.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
				if (method == null)
				{
					return new
					{
						success = false,
						error = $"Could not find patch helper {typeName}.{methodName}."
					};
				}

				try
				{
					return new
					{
						success = true,
						value = method.Invoke(null, args)
					};
				}
				catch (Exception ex)
				{
					return new
					{
						success = false,
						error = ex.GetBaseException().Message
					};
				}
			}

			bool EnsureTemporaryColonists(int desiredCount, out object error)
			{
				error = null;
				var existing = map.mapPawns.FreeHumanlikesSpawnedOfFaction(Faction.OfPlayer).Count();
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				for (var i = existing; i < desiredCount; i++)
				{
					if (TryFindClearSpawnCell(map, root + new IntVec3(i * 2, 0, 4), 32f, out var cell, out error) == false)
						return false;

					var pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
					GenSpawn.Spawn(pawn, cell, map, Rot4.South);
					DisablePawnWork(pawn);
					createdColonists.Add(pawn);
				}
				return true;
			}

			Faction EnsureAlliedFaction()
			{
				var existingLeader = QuestNode_GetRandomAlliedFactionLeader.GetAlliedFactionLeader();
				if (existingLeader?.Faction != null)
					return existingLeader.Faction;

				var faction = Find.FactionManager.GetFactions(false, false, true, TechLevel.Medieval, false)
					.FirstOrDefault(candidate => candidate != null
						&& candidate != Faction.OfPlayer
						&& candidate.def != null
						&& candidate.leader != null);
				if (faction == null)
					return null;

				faction.TryAffectGoodwillWith(Faction.OfPlayer, 200, false, false);
				if (faction.PlayerRelationKind != FactionRelationKind.Ally)
				{
					var relation = faction.RelationWith(Faction.OfPlayer);
					relation.baseGoodwill = 100;
					relation.kind = FactionRelationKind.Ally;
					var inverse = Faction.OfPlayer.RelationWith(faction);
					inverse.baseGoodwill = 100;
					inverse.kind = FactionRelationKind.Ally;
				}
				return faction;
			}

			object DescribeInspectPaneWidth(Thing thing, string role)
			{
				if (thing == null || thing.Destroyed)
				{
					return new
					{
						success = false,
						role,
						error = "No selectable thing was available for inspect-pane width measurement."
					};
				}

				Find.Selector.ClearSelection();
				Find.Selector.Select(thing, false, false);
				var pane = new MainTabWindow_Inspect();
				var tabs = (pane.CurTabs ?? Enumerable.Empty<InspectTabBase>()).ToArray();
				var visibleTabs = tabs.Count(tab => tab.IsVisible && tab.Hidden == false);
				var vanillaBaseWidth = 72f * Mathf.Max(6, visibleTabs);
				var patchedWidth = InspectPaneUtility.PaneWidthFor(pane);
				return new
				{
					success = Find.Selector.IsSelected(thing)
						&& patchedWidth > vanillaBaseWidth
						&& Approximately(patchedWidth - vanillaBaseWidth, 146f, 0.001f),
					role,
					selected = Find.Selector.IsSelected(thing),
					thing = DescribeContaminationThing(thing),
					visibleTabs,
					vanillaBaseWidth,
					patchedWidth,
					addedWidth = patchedWidth - vanillaBaseWidth,
					expectedAddedWidth = 146f
				};
			}

			string PatchValueString(object patchResult)
			{
				if (patchResult == null)
					return null;
				var successProperty = patchResult.GetType().GetProperty("success");
				var valueProperty = patchResult.GetType().GetProperty("value");
				if (successProperty == null || valueProperty == null || (bool)successProperty.GetValue(patchResult) == false)
					return null;
				return valueProperty.GetValue(patchResult)?.ToString();
			}

			object DescribeUiPresentation(Thing thing, IntVec3 cell, string role)
			{
				if (thing == null || thing.Destroyed || cell.IsValid == false)
				{
					return new
					{
						success = false,
						role,
						error = "No spawned thing/cell was available for UI presentation checks."
					};
				}

				var statEntryValues = thing.SpecialDisplayStats().ToArray();
				var contaminationStatValues = statEntryValues
					.Where(entry => string.Equals(entry.LabelCap, "ZombieContamination".Translate().ToString(), StringComparison.OrdinalIgnoreCase))
					.Select(entry => entry.ValueString)
					.ToArray();
				var thingMouseover = PatchValueString(InvokePatchHelper("ZombieLand.MouseoverReadout_MouseoverReadoutOnGUI_Patch", "LabelMouseover", thing));
				var glowMouseover = PatchValueString(InvokePatchHelper("ZombieLand.MouseoverReadout_MouseoverReadoutOnGUI_Patch", "GetGlowLabelByValue", map.glowGrid.GroundGlowAt(cell), cell));
				var thingContamination = thing.GetContamination();
				var cellContamination = map.GetContamination(cell);
				var expectedStatValue = $"{100 * thingContamination:F2}%";
				var thingMouseoverHasContamination = thingMouseover?.IndexOf("contaminated", StringComparison.OrdinalIgnoreCase) >= 0;
				var glowMouseoverHasContamination = glowMouseover?.IndexOf("Contaminated", StringComparison.OrdinalIgnoreCase) >= 0;
				var expectedThingMouseoverContamination = ContaminationThresholds.IsVisible(thingContamination);
				var expectedGlowContamination = ContaminationThresholds.IsVisible(cellContamination);

				return new
				{
					success = contaminationStatValues.Length == 1
						&& contaminationStatValues[0] == expectedStatValue
						&& thingMouseover != null
						&& glowMouseover != null
						&& thingMouseoverHasContamination == expectedThingMouseoverContamination
						&& glowMouseoverHasContamination == expectedGlowContamination,
					role,
					thing = DescribeContaminationThing(thing),
					cell = ZombieRuntimeActions.DescribeCell(cell),
					cellContamination,
					expectedStatValue,
					contaminationStatValues,
					thingMouseover,
					thingMouseoverHasContamination,
					expectedThingMouseoverContamination,
					glowMouseover,
					glowMouseoverHasContamination,
					expectedGlowContamination
				};
			}

			object DescribeInspectPaneWidthComparison()
			{
				if (visualThing == null || visualThing.Destroyed || visualCell.IsValid == false)
				{
					return new
					{
						success = false,
						error = "The visual fixture was not available for inspect-pane width comparison."
					};
				}

				Thing cleanThing = null;
				try
				{
					var cleanCell = visualCell + new IntVec3(1, 0, 0);
					if (cleanCell.InBounds(map) == false || cleanCell.Walkable(map) == false)
						cleanCell = visualCell;
					cleanThing = ThingMaker.MakeThing(ThingDefOf.Steel);
					cleanThing.stackCount = 25;
					GenSpawn.Spawn(cleanThing, cleanCell, map, WipeMode.Vanish);
					map.SetContamination(cleanCell, 0f);
					cleanThing.SetContamination(0f);

					var clean = DescribeInspectPaneWidth(cleanThing, "clean");
					var contaminated = DescribeInspectPaneWidth(visualThing, "contaminated");
					var cleanPresentation = DescribeUiPresentation(cleanThing, cleanCell, "clean");
					var contaminatedPresentation = DescribeUiPresentation(visualThing, visualCell, "contaminated");
					return new
					{
						success = (bool)clean.GetType().GetProperty("success").GetValue(clean)
							&& (bool)contaminated.GetType().GetProperty("success").GetValue(contaminated)
							&& ObjectSuccess(cleanPresentation)
							&& ObjectSuccess(contaminatedPresentation),
						clean,
						contaminated,
						cleanPresentation,
						contaminatedPresentation
					};
				}
				finally
				{
					if (cleanThing != null && cleanThing.Destroyed == false)
						cleanThing.Destroy(DestroyMode.Vanish);
					Find.Selector.ClearSelection();
					Find.Selector.Select(visualThing, false, false);
				}
			}

			if (createVisualFixture)
			{
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 32f, out visualCell, out var visualCellError) == false)
					return visualCellError;

				visualThing = ThingMaker.MakeThing(ThingDefOf.Steel);
				visualThing.stackCount = 25;
				GenSpawn.Spawn(visualThing, visualCell, map, WipeMode.Vanish);
				createdThings.Add(visualThing);
				map.SetContamination(visualCell, fixtureContamination);
				visualThing.SetContamination(fixtureContamination);

				Find.Selector.ClearSelection();
				Find.Selector.Select(visualThing, false, false);
				ContaminationManager.Instance.showContaminationOverlay = true;
				Find.PlaySettings.showBeauty = true;
			}
			else
			{
				visualThing = map.listerThings.AllThings
					.Where(thing => thing.Destroyed == false && thing.GetContamination() > 0f)
					.OrderByDescending(thing => thing.GetContamination())
					.FirstOrDefault();
				visualCell = visualThing?.Position ?? IntVec3.Invalid;
			}

			var questCountBefore = Find.QuestManager.QuestsListForReading.Count;
			var questIdsBefore = Find.QuestManager.QuestsListForReading.Select(quest => quest.id).ToHashSet();
			var alliedFaction = QuestNode_GetRandomAlliedFactionLeader.GetAlliedFactionLeader()?.Faction;
			var oldPickupUtilityDefPresent = DefDatabase<QuestScriptDef>.GetNamedSilentFail("Util_TransportShip_Pickup") != null;
			var pickupPathAvailable = DefDatabase<QuestScriptDef>.GetNamedSilentFail("Decontamination") != null;
			if (createQuest)
			{
				if (EnsureTemporaryColonists(2, out var colonistError) == false)
					return colonistError;
				alliedFaction = EnsureAlliedFaction();
				if (pickupPathAvailable
					&& alliedFaction != null
					&& Find.QuestManager.QuestsListForReading.Any(quest => quest.PartsListForReading.Any(part => part is QuestPart_DecontaminateColonists)) == false)
				{
					try
					{
						ContaminationManager.Instance.DecontaminationQuest();
					}
					catch (Exception ex)
					{
						createdQuestException = ex.GetBaseException().Message;
					}
				}
			}

			var quests = Find.QuestManager.QuestsListForReading;
			var decontaminationQuest = quests
				.Where(quest => quest.PartsListForReading.Any(part => part is QuestPart_DecontaminateColonists))
				.OrderByDescending(quest => questIdsBefore.Contains(quest.id) ? 0 : 1)
				.ThenByDescending(quest => quest.id)
				.FirstOrDefault();
			var ordinaryQuest = Quest.MakeRaw();
			ordinaryQuest.challengeRating = 3;
			var decontaminationMax = decontaminationQuest == null
				? null
				: InvokePatchHelper("ZombieLand.MainTabWindow_Quests_DoRow_Patch", "Max", 3, 1, decontaminationQuest);
			var ordinaryMax = InvokePatchHelper("ZombieLand.MainTabWindow_Quests_DoRow_Patch", "Max", ordinaryQuest.challengeRating, 1, ordinaryQuest);

			var statEntryValues = visualThing == null
				? Array.Empty<StatDrawEntry>()
				: visualThing.SpecialDisplayStats().ToArray();
			var contaminationStatPresent = statEntryValues
				.Any(entry => string.Equals(entry.LabelCap, "ZombieContamination".Translate().ToString(), StringComparison.OrdinalIgnoreCase));
			var statEntries = statEntryValues.Select(DescribeStatEntry).ToArray();
			var patchedThingMouseover = visualThing == null
				? null
				: InvokePatchHelper("ZombieLand.MouseoverReadout_MouseoverReadoutOnGUI_Patch", "LabelMouseover", visualThing);
			var patchedGlowMouseover = visualCell.IsValid == false
				? null
				: InvokePatchHelper("ZombieLand.MouseoverReadout_MouseoverReadoutOnGUI_Patch", "GetGlowLabelByValue", map.glowGrid.GroundGlowAt(visualCell), visualCell);
			var inspectPaneWidth = DescribeInspectPaneWidthComparison();

			var selected = visualThing != null && Find.Selector.IsSelected(visualThing);
			var manager = ContaminationManager.Instance;
			var questCountAfter = quests.Count;
			var hasNewQuest = quests.Any(quest => questIdsBefore.Contains(quest.id) == false);
			var decontaminationQuestPresent = decontaminationQuest != null;
			var decontaminationRatingSuppressed = decontaminationMax is { } maxResult
				&& (bool)maxResult.GetType().GetProperty("success").GetValue(maxResult)
				&& (int)maxResult.GetType().GetProperty("value").GetValue(maxResult) == 0;
			var ordinaryRatingPreserved = ordinaryMax is { } ordinaryResult
				&& (bool)ordinaryResult.GetType().GetProperty("success").GetValue(ordinaryResult)
				&& (int)ordinaryResult.GetType().GetProperty("value").GetValue(ordinaryResult) == 3;

			if (cleanupVisualFixture)
			{
				foreach (var thing in createdThings.Where(thing => thing.Destroyed == false))
					thing.Destroy(DestroyMode.Vanish);
			}

			return new
			{
				success = contaminationStatPresent
					&& (createVisualFixture == false || selected)
					&& manager.showContaminationOverlay
					&& (createQuest == false || alliedFaction != null)
					&& (createQuest == false || pickupPathAvailable)
					&& createdQuestException == null
					&& (createQuest == false || decontaminationQuestPresent)
					&& (createQuest == false || decontaminationRatingSuppressed)
					&& ordinaryRatingPreserved
					&& ObjectSuccess(inspectPaneWidth),
				fixture = new
				{
					createdVisualFixture = createVisualFixture,
					createdThingCount = createdThings.Count,
					createdColonistCount = createdColonists.Count,
					cleanupVisualFixture,
					visualCell = visualCell.IsValid ? ZombieRuntimeActions.DescribeCell(visualCell) : null,
					visualThing = DescribeContaminationThing(visualThing),
					selected,
					overlayEnabled = manager.showContaminationOverlay,
					beautyOverlayEnabled = Find.PlaySettings.showBeauty
				},
				ui = new
				{
					statEntries,
					contaminationStatPresent,
					patchedThingMouseover,
					patchedGlowMouseover,
					inspectPaneWidth
				},
				quest = new
				{
					createQuest,
					questCountBefore,
					questCountAfter,
					hasNewQuest,
					alliedFaction = alliedFaction?.Name,
					alliedFactionRelation = alliedFaction?.PlayerRelationKind.ToString(),
					pickupPath = nameof(QuestNode_CreateDecontaminationPickupTransporter),
					pickupPathAvailable,
					oldPickupUtilityDefPresent,
					createdQuestException,
					decontaminationQuestPresent,
					decontaminationQuest = DescribeQuest(decontaminationQuest),
					decontaminationMax,
					decontaminationRatingSuppressed,
					ordinaryMax,
					ordinaryRatingPreserved
				}
			};
		}

		[Tool("zombieland/contamination_accepted_quest_contract", Description = "Advance an accepted decontamination quest until its Core-safe pickup transporter arrives, optionally load one colonist, and verify the SentSatisfied treatment path.")]
		public static object ContaminationAcceptedQuestContract(
			[ToolParameter(Description = "Accept a not-yet-accepted decontamination quest before advancing it.", Required = false, DefaultValue = false)] bool acceptQuest = false,
			[ToolParameter(Description = "Maximum ticks to advance while waiting for the pickup transporter to spawn.", Required = false, DefaultValue = 5000)] int maxArrivalTicks = 5000,
			[ToolParameter(Description = "Move one eligible spawned colonist into the pickup transporter to simulate a completed player load.", Required = false, DefaultValue = true)] bool loadColonist = true,
			[ToolParameter(Description = "Ticks to advance after loading the colonist so quest parts can process SentSatisfied.", Required = false, DefaultValue = 60)] int ticksAfterLoad = 60,
			[ToolParameter(Description = "Shorten the treatment return timer after SentSatisfied and advance enough ticks to verify quest completion.", Required = false, DefaultValue = false)] bool forceReturn = false,
			[ToolParameter(Description = "Outcome branch to verify: success, destroyPickup, sendUnsatisfied, or killSentPawn.", Required = false, DefaultValue = "success")] string outcomeMode = "success")
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

			object DescribePickup(QuestPart_DecontaminationPickupTransporter pickupPart)
			{
				var pickup = pickupPart?.PickupTransporterForTest;
				var transporter = pickup?.TryGetComp<CompTransporter>();
				return new
				{
					exists = pickup != null,
					def = pickup?.def?.defName,
					spawned = pickup?.Spawned ?? false,
					destroyed = pickup?.Destroyed ?? false,
					position = pickup != null && pickup.Spawned ? ZombieRuntimeActions.DescribeCell(pickup.Position) : null,
					hasTransporterComp = transporter != null,
					loadedCount = transporter?.innerContainer.Count ?? 0,
					loadedPawns = transporter?.innerContainer.OfType<Pawn>().Select(DescribePawn).ToArray() ?? Array.Empty<object>(),
					partSpawned = pickupPart?.SpawnedForTest ?? false,
					partSentSatisfied = pickupPart?.SentSatisfiedForTest ?? false
				};
			}

			var normalizedOutcomeMode = (outcomeMode ?? "success").Trim().ToLowerInvariant();
			if (normalizedOutcomeMode != "success"
				&& normalizedOutcomeMode != "destroypickup"
				&& normalizedOutcomeMode != "sendunsatisfied"
				&& normalizedOutcomeMode != "killsentpawn")
			{
				return new
				{
					success = false,
					error = "outcomeMode must be one of: success, destroyPickup, sendUnsatisfied, killSentPawn.",
					outcomeMode
				};
			}
			var shouldDestroyPickup = normalizedOutcomeMode == "destroypickup";
			var shouldSendUnsatisfied = normalizedOutcomeMode == "sendunsatisfied";
			var shouldKillSentPawn = normalizedOutcomeMode == "killsentpawn";
			var shouldLoadColonist = loadColonist && shouldDestroyPickup == false && shouldSendUnsatisfied == false;
			var shouldForceReturn = forceReturn && normalizedOutcomeMode == "success";

			var quests = Find.QuestManager.QuestsListForReading;
			var quest = quests
				.Where(candidate => candidate.State == QuestState.Ongoing || (acceptQuest && candidate.State == QuestState.NotYetAccepted))
				.Where(candidate => candidate.PartsListForReading.Any(part => part is QuestPart_DecontaminateColonists)
					|| candidate.PartsListForReading.Any(part => part is QuestPart_DecontaminationPickupTransporter))
				.OrderByDescending(candidate => candidate.id)
				.FirstOrDefault();
			if (quest == null)
			{
				return new
				{
					success = false,
					error = "No ongoing decontamination quest with pickup/treatment parts was found."
				};
			}

			var pickupPart = quest.PartsListForReading.OfType<QuestPart_DecontaminationPickupTransporter>().FirstOrDefault();
			if (pickupPart == null)
			{
				return new
				{
					success = false,
					questId = quest.id,
					questName = quest.name,
					error = "The ongoing decontamination quest has no pickup transporter part."
				};
			}

			var acceptedByContract = false;
			if (quest.State == QuestState.NotYetAccepted && acceptQuest)
			{
				quest.Accept(null);
				acceptedByContract = quest.State == QuestState.Ongoing;
			}

			var startTick = Find.TickManager.TicksGame;
			var clampedArrivalTicks = Mathf.Clamp(maxArrivalTicks, 0, 20000);
			var advancedToArrival = 0;
			while (pickupPart.SpawnedForTest == false && advancedToArrival < clampedArrivalTicks)
			{
				var step = Mathf.Min(250, clampedArrivalTicks - advancedToArrival);
				AdvanceGameTicks(step);
				advancedToArrival += step;
			}

			var pickup = pickupPart.PickupTransporterForTest;
			var transporter = pickup?.TryGetComp<CompTransporter>();
			Pawn loadedPawn = null;
			var loadedPawnContaminationBeforeTreatment = (ContaminationSnapshot)null;
			var loadedByContract = false;
			var loadError = (string)null;
			var destroyedPickupByContract = false;
			var sentUnsatisfiedByContract = false;
			if (shouldDestroyPickup)
			{
				if (pickup == null || pickup.Spawned == false)
				{
					loadError = "Pickup transporter is not spawned and cannot be destroyed.";
				}
				else
				{
					pickup.Destroy(DestroyMode.Vanish);
					AdvanceGameTicks(1);
					destroyedPickupByContract = true;
				}
			}
			else if (shouldSendUnsatisfied)
			{
				if (pickup == null)
				{
					loadError = "Pickup transporter is missing and cannot send SentUnsatisfied.";
				}
				else
				{
					QuestUtility.SendQuestTargetSignals(pickup.questTags, "SentUnsatisfied", new SignalArgs(new LookTargets(pickup).Named("SUBJECT")));
					AdvanceGameTicks(1);
					sentUnsatisfiedByContract = true;
				}
			}
			if (shouldLoadColonist)
			{
				if (pickup == null || pickup.Spawned == false || transporter == null)
				{
					loadError = "Pickup transporter is not spawned or has no CompTransporter.";
				}
				else
				{
					loadedPawn = map.mapPawns.FreeHumanlikesSpawnedOfFaction(Faction.OfPlayer)
						.OfType<Pawn>()
						.Where(pawn => pawn.Dead == false && pawn.Downed == false && pawn.RaceProps.Humanlike)
						.OrderBy(pawn => pawn.thingIDNumber)
						.FirstOrDefault();
					if (loadedPawn == null)
					{
						loadError = "No eligible spawned free colonist was found to load.";
					}
					else
					{
						if (Constants.CONTAMINATION)
						{
							loadedPawn.SetContamination(0.35f);
							loadedPawnContaminationBeforeTreatment = DescribeContamination(loadedPawn);
						}
						loadedPawn.DeSpawn(DestroyMode.Vanish);
						loadedByContract = transporter.innerContainer.TryAdd(loadedPawn, false);
						if (loadedByContract == false)
							loadError = "CompTransporter.innerContainer rejected the selected colonist.";
					}
				}
			}

			var clampedTicksAfterLoad = Mathf.Clamp(ticksAfterLoad, 0, 2000);
			if (loadedByContract && clampedTicksAfterLoad > 0)
				AdvanceGameTicks(clampedTicksAfterLoad);

			var treatmentParts = quest.PartsListForReading.OfType<QuestPart_DecontaminateColonists>().ToArray();
			var enabledTreatmentPartCount = treatmentParts.Count(part => part.State == QuestPartState.Enabled);
			var killedSentPawnByContract = false;
			if (shouldKillSentPawn && loadedPawn != null && enabledTreatmentPartCount > 0)
			{
				loadedPawn.Kill(null);
				AdvanceGameTicks(1);
				killedSentPawnByContract = true;
			}
			var forcedReturn = false;
			if (shouldForceReturn && enabledTreatmentPartCount > 0)
			{
				var returnTickField = typeof(QuestPart_DecontaminateColonists).GetField("returnColonistsOnTick", BindingFlags.Instance | BindingFlags.NonPublic);
				foreach (var treatmentPart in treatmentParts.Where(part => part.State == QuestPartState.Enabled))
					returnTickField?.SetValue(treatmentPart, Find.TickManager.TicksGame + 1);
				AdvanceGameTicks(2);
				forcedReturn = true;
			}

			var returnContaminationBeforeProbe = (ContaminationSnapshot)null;
			var returnContaminationAfterProbe = (ContaminationSnapshot)null;
			var returnImmunitySucceeded = true;
			const float returnProbeContamination = 0.40f;
			if (shouldForceReturn && loadedPawn != null && Constants.CONTAMINATION)
			{
				returnContaminationBeforeProbe = DescribeContamination(loadedPawn);
				loadedPawn.AddContamination(returnProbeContamination);
				returnContaminationAfterProbe = DescribeContamination(loadedPawn);
				returnImmunitySucceeded = returnContaminationBeforeProbe.hasDecontaminationImmunity
					&& returnContaminationBeforeProbe.stored <= 0.001f
					&& returnContaminationAfterProbe.stored <= 0.001f
					&& returnContaminationAfterProbe.hasHediff == false;
			}

			treatmentParts = quest.PartsListForReading.OfType<QuestPart_DecontaminateColonists>().ToArray();
			enabledTreatmentPartCount = treatmentParts.Count(part => part.State == QuestPartState.Enabled);
			var branchSucceeded = normalizedOutcomeMode switch
			{
				"destroypickup" => destroyedPickupByContract && quest.State == QuestState.EndedFailed,
				"sendunsatisfied" => sentUnsatisfiedByContract && quest.State == QuestState.EndedFailed,
				"killsentpawn" => killedSentPawnByContract && loadedPawn?.Dead == true && quest.State == QuestState.EndedSuccess,
				_ => pickupPart.SpawnedForTest
					&& (shouldLoadColonist == false || loadedByContract)
					&& (shouldLoadColonist == false || pickupPart.SentSatisfiedForTest)
					&& (shouldLoadColonist == false || enabledTreatmentPartCount > 0 || quest.State == QuestState.EndedSuccess)
					&& (shouldForceReturn == false || quest.State == QuestState.EndedSuccess)
					&& returnImmunitySucceeded
			};
			return new
			{
				success = loadError == null && branchSucceeded,
				questId = quest.id,
				questName = quest.name,
				questState = quest.State.ToString(),
				outcomeMode = normalizedOutcomeMode,
				acceptQuest,
				acceptedByContract,
				startTick,
				endTick = Find.TickManager.TicksGame,
				advancedToArrival,
				ticksAfterLoad = clampedTicksAfterLoad,
				forceReturn,
				forcedReturn,
				destroyedPickupByContract,
				sentUnsatisfiedByContract,
				killedSentPawnByContract,
				loadColonist,
				effectiveLoadColonist = shouldLoadColonist,
				loadedByContract,
				loadError,
				loadedPawn = loadedPawn == null ? null : DescribePawn(loadedPawn),
				loadedPawnContaminationBeforeTreatment,
				decontaminationReturnProbe = loadedPawn == null || shouldForceReturn == false ? null : new
				{
					requestedAdd = returnProbeContamination,
					expectedImmunityTicks = ContaminationManager.DecontaminationImmunityTicks(),
					expectedImmunityDays = ContaminationManager.DecontaminationImmunityTicks() / (float)GenDate.TicksPerDay,
					returnImmunitySucceeded,
					beforeAdd = returnContaminationBeforeProbe,
					afterAdd = returnContaminationAfterProbe
				},
				pickup = DescribePickup(pickupPart),
				treatmentPartCount = treatmentParts.Length,
				enabledTreatmentPartCount,
				treatmentPartStates = treatmentParts.Select(part => part.State.ToString()).ToArray()
			};
		}

		[Tool("zombieland/contamination_core_contract", Description = "Verify core contamination storage, pawn need/hediff sync, clearing, clamping, and stack split propagation.")]
		public static object ContaminationCoreContract()
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var humanCell, out var humanSpawnError) == false)
				return humanSpawnError;
			if (TryFindClearSpawnCell(map, humanCell + new IntVec3(3, 0, 0), 8f, out var itemCell, out var itemSpawnError) == false)
				return itemSpawnError;

			var human = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(human, humanCell, map, Rot4.South);
			DisablePawnWork(human);
			human.needs?.AddOrRemoveNeedsAsAppropriate();
			var needGate = VerifyContaminationNeedGate(human);
			human.ClearContamination();
			var initial = DescribeContamination(human);

			const float addValue = 0.4f;
			human.AddContamination(addValue);
			var afterAdd = DescribeContamination(human);

			const float setValue = 0.25f;
			human.SetContamination(setValue);
			var afterSet = DescribeContamination(human);

			human.ClearContamination();
			var afterClear = DescribeContamination(human);

			const float highNonLethalValue = 0.75f;
			human.AddContamination(highNonLethalValue);
			var afterHighNonLethalAdd = DescribeContamination(human);
			human.ClearContamination();
			var afterSecondClear = DescribeContamination(human);

			var clampedComponent = ThingMaker.MakeThing(ThingDefOf.ComponentIndustrial);
			const float clampInput = 1.5f;
			clampedComponent.AddContamination(clampInput, (sbyte)map.Index);
			var clampedComponentContamination = clampedComponent.GetContamination();

			var component = ThingMaker.MakeThing(ThingDefOf.ComponentIndustrial);
			component.stackCount = 10;
			GenSpawn.Spawn(component, itemCell, map, WipeMode.Vanish);
			const float stackContamination = 0.6f;
			component.AddContamination(stackContamination);
			var componentBeforeSplitCount = component.stackCount;
			var componentBeforeSplitContamination = component.GetContamination();
			var split = component.SplitOff(4);
			var componentAfterSplitCount = component.stackCount;
			var splitCount = split?.stackCount ?? 0;
			var componentAfterSplitContamination = component.GetContamination();
			var splitContamination = split?.GetContamination() ?? 0f;
			var groundRepair = VerifyGroundRepair(map);
			var destroyCleanup = VerifyDestroyCleanup(map, itemCell + new IntVec3(3, 0, 0));
			var thingCompMakeThing = VerifyThingCompMakeThingFanout(map, itemCell + new IntVec3(6, 0, 0));
			var minifiedSplit = VerifyMinifiedThingSplit(map, itemCell + new IntVec3(9, 0, 0));
			var worldGenerationReset = VerifyWorldGenerationReset();

			static bool Close(float? value, float expected) => value.HasValue && Mathf.Abs(value.Value - expected) < 0.0001f;
			static bool CloseFloat(float value, float expected) => Mathf.Abs(value - expected) < 0.0001f;

			static object VerifyContaminationNeedGate(Pawn pawn)
			{
				var shouldHaveNeedMethod = typeof(Pawn_NeedsTracker).GetMethod("ShouldHaveNeed", BindingFlags.Instance | BindingFlags.NonPublic);
				if (pawn?.needs == null || CustomDefs.Contamination == null || shouldHaveNeedMethod == null)
				{
					return new
					{
						success = false,
						error = "Need gate probe requires pawn needs, CustomDefs.Contamination, and Pawn_NeedsTracker.ShouldHaveNeed."
					};
				}

				var originalContamination = Constants.CONTAMINATION;
				try
				{
					Constants.CONTAMINATION = true;
					pawn.needs.AddOrRemoveNeedsAsAppropriate();
					var enabledShouldHaveNeed = (bool)shouldHaveNeedMethod.Invoke(pawn.needs, new object[] { CustomDefs.Contamination });
					var enabledHasNeed = pawn.needs.TryGetNeed<ContaminationNeed>() != null;

					Constants.CONTAMINATION = false;
					var disabledShouldHaveNeed = (bool)shouldHaveNeedMethod.Invoke(pawn.needs, new object[] { CustomDefs.Contamination });
					pawn.needs.AddOrRemoveNeedsAsAppropriate();
					var disabledHasNeed = pawn.needs.TryGetNeed<ContaminationNeed>() != null;

					return new
					{
						success = enabledShouldHaveNeed
							&& enabledHasNeed
							&& disabledShouldHaveNeed == false
							&& disabledHasNeed == false,
						enabledShouldHaveNeed,
						enabledHasNeed,
						disabledShouldHaveNeed,
						disabledHasNeed
					};
				}
				finally
				{
					Constants.CONTAMINATION = originalContamination;
					pawn.needs.AddOrRemoveNeedsAsAppropriate();
				}
			}

			static object VerifyGroundRepair(Map map)
			{
				var manager = ContaminationManager.Instance;
				var mapIndex = map.Index;
				var hadOriginal = manager.grounds.TryGetValue(mapIndex, out var originalGrid);
				try
				{
					manager.grounds.Remove(mapIndex);
					var missingBeforeRepair = manager.grounds.ContainsKey(mapIndex) == false;
					manager.FixGrounds();
					var repaired = manager.grounds.TryGetValue(mapIndex, out var repairedGrid)
						&& repairedGrid.map == map
						&& repairedGrid.cells.Length == map.Size.x * map.Size.z
						&& repairedGrid.mapSizeX == map.Size.x;

					return new
					{
						success = missingBeforeRepair && repaired,
						mapIndex,
						hadOriginal,
						missingBeforeRepair,
						repaired,
						repairedCellCount = repairedGrid?.cells?.Length ?? 0,
						expectedCellCount = map.Size.x * map.Size.z
					};
				}
				finally
				{
					if (hadOriginal)
						manager.grounds[mapIndex] = originalGrid;
					else
						manager.grounds.Remove(mapIndex);
				}
			}

			static object VerifyDestroyCleanup(Map map, IntVec3 root)
			{
				var manager = ContaminationManager.Instance;
				if (TryFindClearSpawnCell(map, root, 8f, out var destroyCell, out var spawnError) == false)
					return spawnError;

				var thing = ThingMaker.MakeThing(ThingDefOf.ComponentIndustrial);
				try
				{
					GenSpawn.Spawn(thing, destroyCell, map, WipeMode.Vanish);
					thing.SetContamination(0.37f);
					var id = thing.thingIDNumber;
					var contaminationBeforeDestroy = thing.GetContamination();
					var entryBeforeDestroy = manager.contaminations.ContainsKey(id);
					thing.Destroy(DestroyMode.Vanish);
					var destroyed = thing.Destroyed;
					var entryAfterDestroy = manager.contaminations.ContainsKey(id);

					return new
					{
						success = entryBeforeDestroy
							&& destroyed
							&& entryAfterDestroy == false,
						thing = ZombieRuntimeActions.StableThingId(thing),
						cell = ZombieRuntimeActions.DescribeCell(destroyCell),
						contaminationBeforeDestroy,
						entryBeforeDestroy,
						destroyed,
						entryAfterDestroy
					};
				}
				finally
				{
					if (thing.Destroyed == false)
						thing.Destroy(DestroyMode.Vanish);
				}
			}

			static object VerifyMinifiedThingSplit(Map map, IntVec3 root)
			{
				var patchTargets = PatchedMethodsForPatchClass("MinifiedThing_SplitOff_Patch");
				var buildingDef = DefDatabase<ThingDef>.GetNamed("Stool", false);
				var stuffDef = ThingDefOf.WoodLog;
				if (buildingDef?.Minifiable != true || buildingDef.minifiedDef == null || stuffDef == null)
				{
					return new
					{
						success = false,
						targets = CompactPatchTargets(patchTargets),
						error = "The Stool building def is not available as a minifiable split fixture."
					};
				}
				if (TryFindClearSpawnCell(map, root, 12f, out var splitCell, out var splitCellError) == false)
					return splitCellError;

				Building sourceBuilding = null;
				MinifiedThing minified = null;
				MinifiedThing split = null;
				try
				{
					sourceBuilding = GenSpawn.Spawn(ThingMaker.MakeThing(buildingDef, stuffDef), splitCell, map, Rot4.South) as Building;
					if (sourceBuilding == null)
					{
						return new
						{
							success = false,
							targets = CompactPatchTargets(patchTargets),
							error = "Could not spawn the Stool split fixture."
						};
					}
					minified = MinifyUtility.MakeMinified(sourceBuilding);
					if (minified == null)
					{
						return new
						{
							success = false,
							targets = CompactPatchTargets(patchTargets),
							error = "MinifyUtility.MakeMinified returned null."
						};
					}
					if (minified.Spawned == false)
						GenSpawn.Spawn(minified, splitCell, map, Rot4.South, WipeMode.Vanish, false);

					const int startCount = 5;
					const int splitOffCount = 2;
					const float inputContamination = 0.5f;
					minified.stackCount = startCount;
					minified.SetContamination(inputContamination);
					var beforeCount = minified.stackCount;
					var beforeContamination = minified.GetContamination();
					split = minified.SplitOff(splitOffCount) as MinifiedThing;
					var remainingCount = minified.stackCount;
					var splitCount = split?.stackCount ?? 0;
					var remainingContamination = minified.GetContamination();
					var splitContamination = split?.GetContamination() ?? 0f;
					var expectedSplitContamination = inputContamination * splitOffCount / startCount;
					var expectedRemainingContamination = inputContamination - expectedSplitContamination;
					var splitInnerDef = split?.InnerThing?.def?.defName;

					return new
					{
						success = patchTargets.Length > 0
							&& split != null
							&& split != minified
							&& beforeCount == startCount
							&& remainingCount == startCount - splitOffCount
							&& splitCount == splitOffCount
							&& CloseFloat(beforeContamination, inputContamination)
							&& CloseFloat(remainingContamination, expectedRemainingContamination)
							&& CloseFloat(splitContamination, expectedSplitContamination)
							&& splitInnerDef == buildingDef.defName,
						targets = CompactPatchTargets(patchTargets),
						method = "RimWorld.MinifiedThing.SplitOff(Int32)",
						cell = ZombieRuntimeActions.DescribeCell(splitCell),
						minified = ZombieRuntimeActions.StableThingId(minified),
						split = ZombieRuntimeActions.StableThingId(split),
						innerDef = minified.InnerThing?.def?.defName,
						splitInnerDef,
						beforeCount,
						remainingCount,
						splitCount,
						beforeContamination,
						remainingContamination,
						splitContamination,
						expectedRemainingContamination,
						expectedSplitContamination
					};
				}
				finally
				{
					if (split is { Destroyed: false })
					{
						split.ClearContamination();
						if (split.Spawned)
							split.Destroy(DestroyMode.Vanish);
					}
					if (minified is { Destroyed: false })
					{
						minified.ClearContamination();
						if (minified.Spawned)
							minified.Destroy(DestroyMode.Vanish);
					}
					if (sourceBuilding is { Destroyed: false, Spawned: true })
					{
						sourceBuilding.ClearContamination();
						sourceBuilding.Destroy(DestroyMode.Vanish);
					}
				}
			}

			var expectedEffectivenessAfterAdd = Mathf.Max(0.05f, 1f - addValue * ZombieSettings.Values.contamination.contaminationEffectivenessPercentage);
			var expectedEffectivenessAfterSet = Mathf.Max(0.05f, 1f - setValue * ZombieSettings.Values.contamination.contaminationEffectivenessPercentage);

			var initialClean = CloseFloat(initial.stored, 0f) && initial.hasHediff == false;
			var addSynced = CloseFloat(afterAdd.stored, addValue)
				&& afterAdd.hasNeed
				&& Close(afterAdd.needLevel, addValue)
				&& afterAdd.hasHediff
				&& Close(afterAdd.hediffSeverity, addValue)
				&& CloseFloat(afterAdd.effectiveness, expectedEffectivenessAfterAdd);
			var setSynced = CloseFloat(afterSet.stored, setValue)
				&& Close(afterSet.needLevel, setValue)
				&& afterSet.hasHediff
				&& Close(afterSet.hediffSeverity, setValue)
				&& CloseFloat(afterSet.effectiveness, expectedEffectivenessAfterSet);
			var clearSynced = CloseFloat(afterClear.stored, 0f)
				&& Close(afterClear.needLevel, 0f)
				&& afterClear.hasHediff == false;
			var highNonLethalAddSynced = CloseFloat(afterHighNonLethalAdd.stored, highNonLethalValue)
				&& Close(afterHighNonLethalAdd.needLevel, highNonLethalValue)
				&& afterHighNonLethalAdd.hasHediff
				&& Close(afterHighNonLethalAdd.hediffSeverity, highNonLethalValue);
			var clampSynced = CloseFloat(clampedComponentContamination, 1f);
			var secondClearSynced = CloseFloat(afterSecondClear.stored, 0f)
				&& Close(afterSecondClear.needLevel, 0f)
				&& afterSecondClear.hasHediff == false
				&& human.Dead == false;
			var splitPropagated = componentBeforeSplitCount == 10
				&& componentAfterSplitCount == 6
				&& splitCount == 4
				&& CloseFloat(componentBeforeSplitContamination, stackContamination)
				&& CloseFloat(componentAfterSplitContamination, stackContamination)
				&& CloseFloat(splitContamination, stackContamination);

			return new
			{
				success = initialClean
					&& addSynced
					&& setSynced
					&& clearSynced
					&& highNonLethalAddSynced
					&& clampSynced
					&& secondClearSynced
						&& splitPropagated
						&& ObjectSuccess(needGate)
						&& ObjectSuccess(groundRepair)
					&& ObjectSuccess(destroyCleanup)
					&& ObjectSuccess(thingCompMakeThing)
						&& ObjectSuccess(minifiedSplit)
						&& ObjectSuccess(worldGenerationReset),
				human = DescribePawn(human),
				humanCell = ZombieRuntimeActions.DescribeCell(humanCell),
				itemCell = ZombieRuntimeActions.DescribeCell(itemCell),
				needGate,
				initial,
				afterAdd,
				afterSet,
				afterClear,
				afterHighNonLethalAdd,
				afterSecondClear,
				expectedEffectivenessAfterAdd,
				expectedEffectivenessAfterSet,
				clampedComponent = ZombieRuntimeActions.StableThingId(clampedComponent),
				clampedComponentContamination,
				component = ZombieRuntimeActions.StableThingId(component),
				split = ZombieRuntimeActions.StableThingId(split),
				componentBeforeSplitCount,
				componentAfterSplitCount,
				splitCount,
				componentBeforeSplitContamination,
				componentAfterSplitContamination,
				splitContamination,
				groundRepair,
				worldGenerationReset,
				destroyCleanup,
				minifiedSplit,
				initialClean,
				addSynced,
				setSynced,
				clearSynced,
				highNonLethalAddSynced,
				clampSynced,
				secondClearSynced,
				splitPropagated
					,
				thingCompMakeThing
			};
		}

		static object VerifyWorldGenerationReset()
		{
			var patchTargets = PatchedMethodsForPatchClass("WorldGenerator_GenerateWorld_Patch");
			var instanceField = typeof(ContaminationManager).GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic);
			var currentWorld = Current.Game?.World;
			var realComponent = currentWorld?.GetComponent<ContaminationManager>();
			var postfix = typeof(WorldGenerator_GenerateWorld_Patch).GetMethod("Postfix", BindingFlags.Static | BindingFlags.Public);
			if (instanceField == null || currentWorld == null || realComponent == null || postfix == null)
			{
				return new
				{
					success = false,
					targets = CompactPatchTargets(patchTargets),
					error = "World generation reset probe requires the ContaminationManager singleton field, current world component, and patch postfix."
				};
			}

			var originalCached = instanceField.GetValue(null);
			var sentinel = new ContaminationManager(currentWorld);
			try
			{
				instanceField.SetValue(null, sentinel);
				var sentinelInstalled = ReferenceEquals(instanceField.GetValue(null), sentinel);
				postfix.Invoke(null, null);
				var fieldAfterPostfix = instanceField.GetValue(null);
				var clearedByPostfix = fieldAfterPostfix == null;
				var rebound = ContaminationManager.Instance;
				return new
				{
					success = patchTargets.Length > 0
						&& sentinelInstalled
						&& clearedByPostfix
						&& ReferenceEquals(rebound, realComponent)
						&& ReferenceEquals(rebound, sentinel) == false,
					targets = CompactPatchTargets(patchTargets),
					target = "RimWorld.Planet.WorldGenerator.GenerateWorld",
					sentinelInstalled,
					clearedByPostfix,
					reboundToCurrentWorldComponent = ReferenceEquals(rebound, realComponent),
					reboundToSentinel = ReferenceEquals(rebound, sentinel),
					currentWorldComponentPresent = realComponent != null
				};
			}
			finally
			{
				instanceField.SetValue(null, originalCached ?? realComponent);
			}
		}

		static object VerifyThingCompMakeThingFanout(Map map, IntVec3 root)
		{
			var patchTargets = PatchedMethodsForPatchClass("ThingComp_MakeThing_Patch");
			var gatherableCase = VerifyGatherableBodyResourceThingCompTransfer(map, root);
			return new
			{
				success = patchTargets.Length > 0
					&& ObjectSuccess(gatherableCase),
				targets = CompactPatchTargets(patchTargets),
				gatherableCase
			};
		}

		static object VerifyGatherableBodyResourceThingCompTransfer(Map map, IntVec3 root)
		{
			var animalKind = DefDatabase<PawnKindDef>.AllDefs
				.Where(kind => kind?.race?.race?.Animal == true)
				.Where(kind => kind.race.comps != null)
				.Where(kind => kind.race.comps.Any(props => typeof(CompHasGatherableBodyResource).IsAssignableFrom(props.compClass)))
				.OrderBy(kind => kind.defName)
				.FirstOrDefault();
			if (animalKind == null)
			{
				return new
				{
					success = false,
					error = "No animal PawnKindDef with CompHasGatherableBodyResource is available in the active def set."
				};
			}

			if (TryFindClearSpawnCell(map, root, 16f, out var doerCell, out var doerCellError) == false)
				return doerCellError;
			if (TryFindClearSpawnCell(map, doerCell + IntVec3.East, 8f, out var animalCell, out var animalCellError) == false)
				return animalCellError;

			var doer = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var animal = PawnGenerator.GeneratePawn(animalKind, Faction.OfPlayer);
			var producedThings = new List<Thing>();
			try
			{
				GenSpawn.Spawn(doer, doerCell, map, Rot4.South);
				GenSpawn.Spawn(animal, animalCell, map, Rot4.South);
				DisablePawnWork(doer);
				DisablePawnWork(animal);

				var comp = animal.AllComps.OfType<CompHasGatherableBodyResource>().FirstOrDefault();
				var fullnessField = typeof(CompHasGatherableBodyResource).GetField("fullness", BindingFlags.Instance | BindingFlags.NonPublic);
				var resourceDef = GetProtectedThingDef(comp, "ResourceDef");
				var resourceAmount = GetProtectedInt(comp, "ResourceAmount");
				if (comp == null || fullnessField == null || resourceDef == null)
				{
					return new
					{
						success = false,
						animalKind = animalKind.defName,
						hasComp = comp != null,
						hasFullnessField = fullnessField != null,
						resourceDef = resourceDef?.defName,
						error = "Gatherable-body-resource probe requires a comp, protected fullness field, and resource def."
					};
				}

				const float parentContaminationBefore = 0.72f;
				animal.SetContamination(parentContaminationBefore);
				fullnessField.SetValue(comp, 1f);
				var beforeProducts = map.listerThings.ThingsOfDef(resourceDef).ToHashSet();
				var animalGatherYield = doer.GetStatValue(StatDefOf.AnimalGatherYield);
				var seed = FindRandChanceSeed(true, animalGatherYield);
				var animalBefore = animal.GetContamination();
				Rand.PushState(seed);
				try
				{
					comp.Gathered(doer);
				}
				finally
				{
					Rand.PopState();
				}
				producedThings.AddRange(map.listerThings.ThingsOfDef(resourceDef).Where(thing => beforeProducts.Contains(thing) == false));
				var animalAfter = animal.GetContamination();
				var productContaminations = producedThings.Select(thing => thing.GetContamination()).ToArray();
				var productTotalContamination = productContaminations.Sum();
				var expectedFirstProductContamination = animalBefore * ZombieSettings.Values.contamination.generalTransfer;

				return new
				{
					success = producedThings.Count > 0
						&& productContaminations.All(value => value > 0f)
						&& productTotalContamination > 0f
						&& animalAfter < animalBefore
						&& Approximately(productContaminations.FirstOrDefault(), expectedFirstProductContamination, 0.0001f),
					animalKind = animalKind.defName,
					animal = DescribePawn(animal),
					doer = DescribePawn(doer),
					doerCell = ZombieRuntimeActions.DescribeCell(doerCell),
					animalCell = ZombieRuntimeActions.DescribeCell(animalCell),
					compType = comp.GetType().Name,
					resourceDef = resourceDef.defName,
					resourceAmount,
					animalGatherYield,
					seed,
					animalBefore,
					animalAfter,
					expectedFirstProductContamination,
					producedCount = producedThings.Count,
					produced = producedThings.Select(DescribeContaminationThing).ToArray(),
					productContaminations,
					productTotalContamination
				};
			}
			finally
			{
				foreach (var thing in producedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
				if (animal.Destroyed == false)
					animal.Destroy(DestroyMode.Vanish);
				if (doer.Destroyed == false)
					doer.Destroy(DestroyMode.Vanish);
			}
		}

		static ThingDef GetProtectedThingDef(object instance, string propertyName)
			=> GetProtectedValue<ThingDef>(instance, propertyName);

		static int GetProtectedInt(object instance, string propertyName)
			=> GetProtectedValue<int>(instance, propertyName);

		static T GetProtectedValue<T>(object instance, string propertyName)
		{
			var type = instance?.GetType();
			while (type != null)
			{
				var property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
				if (property != null)
					return (T)property.GetValue(instance);
				type = type.BaseType;
			}
			return default;
		}

		[Tool("zombieland/contamination_stack_absorb_contract", Description = "Verify real Thing.TryAbsorbStack preserves weighted contamination when stacks merge.")]
		public static object ContaminationStackAbsorbContract()
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var targetCell, out var targetSpawnError) == false)
				return targetSpawnError;
			if (TryFindClearSpawnCell(map, targetCell + IntVec3.East, 8f, out var sourceCell, out var sourceSpawnError) == false)
				return sourceSpawnError;

			var target = ThingMaker.MakeThing(ThingDefOf.Steel);
			var source = ThingMaker.MakeThing(ThingDefOf.Steel);
			try
			{
				const int targetCountBefore = 6;
				const int sourceCountBefore = 4;
				const float targetContaminationBefore = 0.2f;
				const float sourceContaminationBefore = 0.8f;
				target.stackCount = targetCountBefore;
				source.stackCount = sourceCountBefore;
				GenSpawn.Spawn(target, targetCell, map, WipeMode.Vanish);
				GenSpawn.Spawn(source, sourceCell, map, WipeMode.Vanish);
				target.SetContamination(targetContaminationBefore);
				source.SetContamination(sourceContaminationBefore);

				var targetBefore = target.GetContamination();
				var sourceBefore = source.GetContamination();
				var fullyAbsorbed = target.TryAbsorbStack(source, false);
				var targetCountAfter = target.stackCount;
				var sourceCountAfter = source.Destroyed ? 0 : source.stackCount;
				var targetAfter = target.GetContamination();
				var sourceDestroyed = source.Destroyed;
				var expectedTargetAfter = (targetCountBefore * targetContaminationBefore + sourceCountBefore * sourceContaminationBefore)
					/ (targetCountBefore + sourceCountBefore);

				static bool CloseFloat(float value, float expected) => Mathf.Abs(value - expected) < 0.0001f;
				var weightedContamination = CloseFloat(targetBefore, targetContaminationBefore)
					&& CloseFloat(sourceBefore, sourceContaminationBefore)
					&& CloseFloat(targetAfter, expectedTargetAfter);
				var stackMerged = fullyAbsorbed
					&& targetCountAfter == targetCountBefore + sourceCountBefore
					&& sourceDestroyed
					&& sourceCountAfter == 0;

				return new
				{
					success = stackMerged && weightedContamination,
					target = ZombieRuntimeActions.StableThingId(target),
					source = ZombieRuntimeActions.StableThingId(source),
					targetCell = ZombieRuntimeActions.DescribeCell(targetCell),
					sourceCell = ZombieRuntimeActions.DescribeCell(sourceCell),
					targetCountBefore,
					sourceCountBefore,
					targetCountAfter,
					sourceCountAfter,
					fullyAbsorbed,
					sourceDestroyed,
					targetBefore,
					sourceBefore,
					targetAfter,
					expectedTargetAfter,
					stackMerged,
					weightedContamination
				};
			}
			finally
			{
				target?.ClearContamination();
				source?.ClearContamination();
				if (target is { Destroyed: false, Spawned: true })
					target.Destroy();
				if (source is { Destroyed: false, Spawned: true })
					source.Destroy();
			}
		}

		[Tool("zombieland/contamination_generation_handoffs_contract", Description = "Verify generated reward/trade/mech-cluster things receive contamination through generic generation handoff patches.")]
		public static object ContaminationGenerationHandoffsContract(
			[ToolParameter(Description = "Destroy generated/spawned test things before returning.", Required = false, DefaultValue = true)] bool cleanup = true)
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2) + new IntVec3(72, 0, 0);
			var oldRandomThingCreateChance = ZombieSettings.Values.contamination.randomThingCreateChance;
			var oldMechClusterChance = ZombieSettings.Values.contamination.mechClusterChance;
			try
			{
				ZombieSettings.Values.contamination.randomThingCreateChance = 1f;
				ZombieSettings.Values.contamination.mechClusterChance = 1f;

				var thingSetMaker = VerifyThingSetMakerGenerateHandoff(cleanup);
				var tradeDeal = VerifyTradeDealHandoff(cleanup);
				var mechCluster = VerifyMechClusterHandoff(map, root, cleanup);
				var skyfallerProducts = VerifySkyfallerSpawnThingsHandoff(map, root + new IntVec3(0, 0, 8), cleanup);

				return new
				{
					success = ObjectSuccess(thingSetMaker)
						&& ObjectSuccess(tradeDeal)
						&& ObjectSuccess(mechCluster)
						&& ObjectSuccess(skyfallerProducts),
					cleanup,
					randomThingCreateChance = ZombieSettings.Values.contamination.randomThingCreateChance,
					mechClusterChance = ZombieSettings.Values.contamination.mechClusterChance,
					thingSetMaker,
					tradeDeal,
					mechCluster,
					skyfallerProducts
				};
			}
			finally
			{
				ZombieSettings.Values.contamination.randomThingCreateChance = oldRandomThingCreateChance;
				ZombieSettings.Values.contamination.mechClusterChance = oldMechClusterChance;
			}
		}

		static object VerifySkyfallerSpawnThingsHandoff(Map map, IntVec3 root, bool cleanup)
		{
			var patchTargets = PatchedMethodsForPatchClass("Skyfaller_SpawnThings_Patch");
			var spawnThingsMethod = typeof(Skyfaller).GetMethod("SpawnThings", BindingFlags.Instance | BindingFlags.NonPublic);
			var shipChunkCase = VerifySkyfallerSpawnThingsCase(
				map,
				root,
				ThingDefOf.ShipChunkIncoming,
				ThingDefOf.ShipChunk,
				"shipChunk",
				spawnThingsMethod,
				cleanup);
			var mineableCase = VerifySkyfallerSpawnThingsCase(
				map,
				root + new IntVec3(5, 0, 0),
				ThingDefOf.MeteoriteIncoming,
				ThingDefOf.MineableSteel,
				"meteoriteMineable",
				spawnThingsMethod,
				cleanup);

			return new
			{
				success = patchTargets.Length > 0
					&& spawnThingsMethod != null
					&& ObjectSuccess(shipChunkCase)
					&& ObjectSuccess(mineableCase),
				targets = CompactPatchTargets(patchTargets),
				method = "RimWorld.Skyfaller::<SpawnThings>b__59_0(Thing, Int32)",
				spawnThingsMethodFound = spawnThingsMethod != null,
				shipChunkCase,
				mineableCase
			};
		}

		static object VerifySkyfallerSpawnThingsCase(
			Map map,
			IntVec3 root,
			ThingDef skyfallerDef,
			ThingDef innerDef,
			string caseName,
			MethodInfo spawnThingsMethod,
			bool cleanup)
		{
			if (skyfallerDef == null || innerDef == null || spawnThingsMethod == null)
			{
				return new
				{
					success = false,
					caseName,
					skyfallerDef = skyfallerDef?.defName,
					innerDef = innerDef?.defName,
					spawnThingsMethodFound = spawnThingsMethod != null,
					error = "Skyfaller fixture definitions or SpawnThings method were unavailable."
				};
			}
			if (TryFindClearSpawnCell(map, root, 24f, out var skyfallerCell, out var spawnError) == false)
				return spawnError;

			var skyfaller = (Skyfaller)null;
			var innerThing = (Thing)null;
			var placedThings = new List<Thing>();
			try
			{
				innerThing = ThingMaker.MakeThing(innerDef);
				if (innerThing == null)
				{
					return new
					{
						success = false,
						caseName,
						skyfallerDef = skyfallerDef.defName,
						innerDef = innerDef.defName,
						error = "Could not create skyfaller inner thing."
					};
				}

				var existing = map.listerThings.ThingsOfDef(innerDef).ToHashSet();
				skyfaller = SkyfallerMaker.MakeSkyfaller(skyfallerDef, innerThing);
				GenSpawn.Spawn(skyfaller, skyfallerCell, map, WipeMode.Vanish);

				spawnThingsMethod.Invoke(skyfaller, Array.Empty<object>());

				placedThings = map.listerThings.ThingsOfDef(innerDef)
					.Where(thing => existing.Contains(thing) == false)
					.ToList();
				var meteoriteAdd = ZombieSettings.Values.contamination.meteoriteAdd;
				var placedProducts = placedThings.Select(thing => new
				{
					thing = ZombieRuntimeActions.StableThingId(thing),
					def = thing.def?.defName,
					cell = thing.Spawned ? ZombieRuntimeActions.DescribeCell(thing.Position) : null,
					isMineable = thing is Mineable,
					isShipChunk = thing.def == ThingDefOf.ShipChunk,
					contamination = thing.GetContamination()
				}).ToArray();
				var productsContaminated = placedThings.Count > 0
					&& placedThings.All(thing => Approximately(thing.GetContamination(), meteoriteAdd));
				var branchEligibleProducts = placedThings.Count > 0
					&& placedThings.All(thing => thing is Mineable || thing.def == ThingDefOf.ShipChunk);

				return new
				{
					success = productsContaminated && branchEligibleProducts,
					caseName,
					skyfallerDef = skyfallerDef.defName,
					innerDef = innerDef.defName,
					skyfallerCell = ZombieRuntimeActions.DescribeCell(skyfallerCell),
					meteoriteAdd,
					placedCount = placedThings.Count,
					productsContaminated,
					branchEligibleProducts,
					placedProducts
				};
			}
			catch (TargetInvocationException ex)
			{
				return new
				{
					success = false,
					caseName,
					skyfallerDef = skyfallerDef.defName,
					innerDef = innerDef.defName,
					error = ex.InnerException?.Message ?? ex.Message
				};
			}
			finally
			{
				if (cleanup)
				{
					foreach (var thing in placedThings.Where(thing => thing is { Destroyed: false }).ToArray())
					{
						thing.ClearContamination();
						if (thing.Spawned)
							thing.Destroy(DestroyMode.Vanish);
						else
							thing.Destroy();
					}
				}
				if (skyfaller is { Destroyed: false, Spawned: true })
					skyfaller.Destroy(DestroyMode.Vanish);
				else if (skyfaller is { Destroyed: false })
					skyfaller.Destroy();
			}
		}

		static object VerifyThingSetMakerGenerateHandoff(bool cleanup)
		{
			var patchTargets = PatchedMethodsForPatchClass("ThingSetMaker_Generate_Patch");
			var generated = new List<Thing>();
			try
			{
				var maker = new BridgeThingSetMaker();
				Rand.PushState(7101);
				try
				{
					generated = maker.Generate(default(ThingSetMakerParams));
				}
				finally
				{
					Rand.PopState();
				}

				var contaminations = generated.Select(thing => thing.GetContamination()).ToArray();
				var generatedContaminated = generated.Count > 0
					&& contaminations.All(value => value > 0f && value <= 1f);
				return new
				{
					success = patchTargets.Length > 0 && generatedContaminated,
					targets = CompactPatchTargets(patchTargets),
					method = "RimWorld.ThingSetMaker.Generate(ThingSetMakerParams)",
					generated = generated.Select(thing => new
					{
						thing = ZombieRuntimeActions.StableThingId(thing),
						def = thing.def.defName,
						stackCount = thing.stackCount,
						contamination = thing.GetContamination()
					}).ToArray(),
					generatedCount = generated.Count,
					generatedContaminated
				};
			}
			finally
			{
				if (cleanup)
				{
					foreach (var thing in generated.Where(thing => thing is { Destroyed: false }).ToArray())
					{
						thing.ClearContamination();
						thing.Destroy(DestroyMode.Vanish);
					}
				}
			}
		}

		static object VerifyTradeDealHandoff(bool cleanup)
		{
			var patchTargets = PatchedMethodsForPatchClass("TradeDeal_AddAllTradeables_Patch");
			var oldTrader = TradeSession.trader;
			var oldPlayerNegotiator = TradeSession.playerNegotiator;
			var oldDeal = TradeSession.deal;
			var oldGiftMode = TradeSession.giftMode;
			var goods = new List<Thing>();
			try
			{
				var component = ThingMaker.MakeThing(ThingDefOf.ComponentIndustrial);
				component.stackCount = 3;
				var steel = ThingMaker.MakeThing(ThingDefOf.Steel);
				steel.stackCount = 25;
				goods.Add(component);
				goods.Add(steel);

				var trader = new BridgeTrader(goods);
				Rand.PushState(7102);
				try
				{
					TradeSession.SetupWith(trader, null, giftMode: false);
				}
				finally
				{
					Rand.PopState();
				}

				var tradeables = TradeSession.deal?.AllTradeables?.ToArray() ?? Array.Empty<Tradeable>();
				var traderThings = tradeables.SelectMany(tradeable => tradeable.thingsTrader).ToArray();
				var goodContaminations = goods.Select(thing => thing.GetContamination()).ToArray();
				var goodsContaminated = goods.Count > 0
					&& goodContaminations.All(value => value > 0f && value <= 1f);
				var tradeableGoodsVisible = goods.All(good => traderThings.Contains(good));

				return new
				{
					success = patchTargets.Length > 0
						&& goodsContaminated
						&& tradeableGoodsVisible,
					targets = CompactPatchTargets(patchTargets),
					method = "RimWorld.TradeDeal.AddAllTradeables()",
					tradeableCount = tradeables.Length,
					traderThingCount = traderThings.Length,
					tradeableGoodsVisible,
					goods = goods.Select(thing => new
					{
						thing = ZombieRuntimeActions.StableThingId(thing),
						def = thing.def.defName,
						stackCount = thing.stackCount,
						contamination = thing.GetContamination()
					}).ToArray(),
					goodsContaminated
				};
			}
			finally
			{
				TradeSession.trader = oldTrader;
				TradeSession.playerNegotiator = oldPlayerNegotiator;
				TradeSession.deal = oldDeal;
				TradeSession.giftMode = oldGiftMode;
				if (cleanup)
				{
					foreach (var thing in goods.Where(thing => thing is { Destroyed: false }).ToArray())
					{
						thing.ClearContamination();
						thing.Destroy(DestroyMode.Vanish);
					}
				}
			}
		}

		static object VerifyMechClusterHandoff(Map map, IntVec3 root, bool cleanup)
		{
			var patchTargets = PatchedMethodsForPatchClass("MechClusterUtility_SpawnCluster_Patch");
			if (Faction.OfMechanoids == null)
			{
				return new
				{
					success = false,
					targets = CompactPatchTargets(patchTargets),
					error = "No mechanoid faction exists in the loaded world."
				};
			}
			if (TryFindClearSpawnCell(map, root, 24f, out var center, out var spawnError) == false)
				return spawnError;

			var spawned = new List<Thing>();
			var createdLords = new List<Lord>();
			try
			{
				var barricadeDef = ThingDefOf.Barricade;
				var sketch = new Sketch();
				sketch.AddThing(barricadeDef, IntVec3.Zero, Rot4.North, ThingDefOf.Steel);
				var mechSketch = new MechClusterSketch(sketch, new List<MechClusterSketch.Mech>(), startDormant: true);
				var lordsBefore = map.lordManager.lords.ToHashSet();

				Rand.PushState(7103);
				try
				{
					spawned = MechClusterUtility.SpawnCluster(center, map, mechSketch, dropInPods: false, canAssaultColony: false, questTag: "ZL_ContaminationGenerationTest");
				}
				finally
				{
					Rand.PopState();
				}
				createdLords = map.lordManager.lords.Where(lord => lordsBefore.Contains(lord) == false).ToList();

				var contaminations = spawned.Select(thing => thing.GetContamination()).ToArray();
				var spawnedContaminated = spawned.Count > 0
					&& contaminations.All(value => value > 0f && value <= 1f);
				var uniformContamination = contaminations.Length == 0
					|| contaminations.All(value => Approximately(value, contaminations[0], 0.0001f));

				return new
				{
					success = patchTargets.Length > 0
						&& spawnedContaminated
						&& uniformContamination,
					targets = CompactPatchTargets(patchTargets),
					method = "RimWorld.MechClusterUtility.SpawnCluster(IntVec3, Map, MechClusterSketch, Boolean, Boolean, String)",
					center = ZombieRuntimeActions.DescribeCell(center),
					spawned = spawned.Select(thing => new
					{
						thing = ZombieRuntimeActions.StableThingId(thing),
						def = thing.def.defName,
						cell = thing.Spawned ? ZombieRuntimeActions.DescribeCell(thing.Position) : null,
						contamination = thing.GetContamination()
					}).ToArray(),
					spawnedCount = spawned.Count,
					createdLordCount = createdLords.Count,
					spawnedContaminated,
					uniformContamination
				};
			}
			finally
			{
				if (cleanup)
				{
					foreach (var thing in spawned.Where(thing => thing is { Destroyed: false, Spawned: true }).ToArray())
					{
						thing.ClearContamination();
						thing.Destroy(DestroyMode.Vanish);
					}
					foreach (var lord in createdLords.Where(lord => map.lordManager.lords.Contains(lord)).ToArray())
						map.lordManager.RemoveLord(lord);
				}
			}
		}

		[Tool("zombieland/contamination_thing_owner_transfer_contract", Description = "Verify real ThingOwner.TryTransferToContainer preserves contamination through partial and full frame resource-container transfers.")]
		public static object ContaminationThingOwnerTransferContract(
			[ToolParameter(Description = "Destroy fixture frames and materials before returning.", Required = false, DefaultValue = true)] bool cleanup = true)
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var wallDef = ThingDefOf.Wall;
			var stuffDef = ThingDefOf.WoodLog;
			var frameDef = wallDef?.frameDef;
			if (wallDef == null || stuffDef == null || frameDef == null)
			{
				return new
				{
					success = false,
					error = "Wall, WoodLog, or Wall.frameDef is unavailable."
				};
			}

			if (TryFindClearSpawnCell(map, new IntVec3(map.Size.x / 2, 0, map.Size.z / 2), 16f, out var sourceCell, out var sourceCellError) == false)
				return sourceCellError;
			if (TryFindClearSpawnCell(map, sourceCell + new IntVec3(3, 0, 0), 16f, out var targetCell, out var targetCellError) == false)
				return targetCellError;

			var sourceFrame = ThingMaker.MakeThing(frameDef, stuffDef) as Frame;
			var targetFrame = ThingMaker.MakeThing(frameDef, stuffDef) as Frame;
			var material = ThingMaker.MakeThing(stuffDef);
			try
			{
				if (sourceFrame == null || targetFrame == null)
				{
					return new
					{
						success = false,
						error = "Could not create wall frame fixtures."
					};
				}

				sourceFrame.SetFactionDirect(Faction.OfPlayer);
				targetFrame.SetFactionDirect(Faction.OfPlayer);
				GenSpawn.Spawn(sourceFrame, sourceCell, map, Rot4.South, WipeMode.Vanish, false);
				GenSpawn.Spawn(targetFrame, targetCell, map, Rot4.South, WipeMode.Vanish, false);

				const int sourceCountBefore = 10;
				const int partialTransferCount = 4;
				const float materialContamination = 0.75f;
				material.stackCount = sourceCountBefore;
				material.AddContamination(materialContamination, (sbyte)map.Index);
				var acceptedMaterial = sourceFrame.resourceContainer.TryAdd(material, canMergeWithExistingStacks: true);
				var sourceItemBefore = sourceFrame.resourceContainer.FirstOrDefault();
				var sourceItemBeforeContamination = sourceItemBefore?.GetContamination() ?? -1f;

				var partialTransferred = sourceFrame.resourceContainer.TryTransferToContainer(sourceItemBefore, targetFrame.resourceContainer, partialTransferCount, out var partialThing, false);
				var sourceItemAfterPartial = sourceFrame.resourceContainer.FirstOrDefault();
				var targetItemAfterPartial = targetFrame.resourceContainer.FirstOrDefault();
				var sourceCountAfterPartial = sourceItemAfterPartial?.stackCount ?? 0;
				var targetCountAfterPartial = targetItemAfterPartial?.stackCount ?? 0;
				var sourceContaminationAfterPartial = sourceItemAfterPartial?.GetContamination() ?? -1f;
				var targetContaminationAfterPartial = targetItemAfterPartial?.GetContamination() ?? -1f;
				var targetFrameContaminationAfterPartial = targetFrame.GetContamination(false);
				var targetFrameIncludingHoldingsAfterPartial = targetFrame.GetContamination(true);

				var fullTransferCount = sourceCountAfterPartial;
				var fullTransferred = sourceFrame.resourceContainer.TryTransferToContainer(sourceItemAfterPartial, targetFrame.resourceContainer, fullTransferCount, out var fullThing, true);
				var sourceCountAfterFull = sourceFrame.resourceContainer.Sum(thing => thing.stackCount);
				var targetItemAfterFull = targetFrame.resourceContainer.FirstOrDefault();
				var targetCountAfterFull = targetFrame.resourceContainer.Sum(thing => thing.stackCount);
				var targetContaminationAfterFull = targetItemAfterFull?.GetContamination() ?? -1f;
				var targetFrameContaminationAfterFull = targetFrame.GetContamination(false);
				var targetFrameIncludingHoldingsAfterFull = targetFrame.GetContamination(true);

				static bool CloseFloat(float value, float expected) => Mathf.Abs(value - expected) < 0.0001f;
				var partialValid = acceptedMaterial
					&& partialTransferred == partialTransferCount
					&& partialThing != null
					&& sourceCountAfterPartial == sourceCountBefore - partialTransferCount
					&& targetCountAfterPartial == partialTransferCount
					&& CloseFloat(sourceItemBeforeContamination, materialContamination)
					&& CloseFloat(sourceContaminationAfterPartial, materialContamination)
					&& CloseFloat(targetContaminationAfterPartial, materialContamination)
					&& CloseFloat(targetFrameContaminationAfterPartial, materialContamination)
					&& CloseFloat(targetFrameIncludingHoldingsAfterPartial, materialContamination * 2f);
				var fullValid = fullThing != null
					&& sourceCountAfterFull == 0
					&& targetCountAfterFull == sourceCountBefore
					&& CloseFloat(targetContaminationAfterFull, materialContamination)
					&& CloseFloat(targetFrameContaminationAfterFull, materialContamination)
					&& CloseFloat(targetFrameIncludingHoldingsAfterFull, materialContamination * 2f);

				return new
				{
					success = partialValid && fullValid,
					cleanup,
					sourceFrame = ZombieRuntimeActions.StableThingId(sourceFrame),
					targetFrame = ZombieRuntimeActions.StableThingId(targetFrame),
					sourceCell = ZombieRuntimeActions.DescribeCell(sourceCell),
					targetCell = ZombieRuntimeActions.DescribeCell(targetCell),
					acceptedMaterial,
					materialContamination,
					sourceItemBefore = ZombieRuntimeActions.StableThingId(sourceItemBefore),
					sourceItemBeforeContamination,
					partialTransferred,
					partialThing = ZombieRuntimeActions.StableThingId(partialThing),
					fullTransferred,
					fullThing = ZombieRuntimeActions.StableThingId(fullThing),
					sourceCountBefore,
					partialTransferCount,
					sourceCountAfterPartial,
					targetCountAfterPartial,
					sourceContaminationAfterPartial,
					targetContaminationAfterPartial,
					targetFrameContaminationAfterPartial,
					targetFrameIncludingHoldingsAfterPartial,
					fullTransferCount,
					sourceCountAfterFull,
					targetCountAfterFull,
					targetContaminationAfterFull,
					targetFrameContaminationAfterFull,
					targetFrameIncludingHoldingsAfterFull,
					partialValid,
					fullValid,
					targetFrameState = DescribeContaminationThing(targetFrame)
				};
			}
			finally
			{
				if (cleanup)
				{
					sourceFrame?.ClearContamination();
					targetFrame?.ClearContamination();
					material?.ClearContamination();
					if (sourceFrame is { Destroyed: false, Spawned: true })
						sourceFrame.Destroy();
					if (targetFrame is { Destroyed: false, Spawned: true })
						targetFrame.Destroy();
					if (material is { Destroyed: false, Spawned: true })
						material.Destroy();
				}
			}
		}

		[Tool("zombieland/complete_frame_by_id", Description = "Complete a current-map frame by id, ThingID, or label with a generated worker and return contamination handoff state.")]
		public static object CompleteFrameById(
			[ToolParameter(Description = "Frame id, ThingID, label, or short label.", Required = true)] string target,
			[ToolParameter(Description = "Destroy the completed building and generated worker before returning.", Required = false, DefaultValue = true)] bool cleanup = true)
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			if (FindThing(map, target) is not Frame frame)
			{
				return new
				{
					success = false,
					target,
					error = $"No current-map frame matched '{target}'."
				};
			}

			var frameCell = frame.Position;
			var frameId = ZombieRuntimeActions.StableThingId(frame);
			var frameThingId = frame.ThingID;
			if (TryFindClearSpawnCell(map, frameCell + new IntVec3(-2, 0, 0), 12f, out var workerCell, out var workerSpawnError) == false)
				return workerSpawnError;

			var worker = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			Thing finalThing = null;
			try
			{
				GenSpawn.Spawn(worker, workerCell, map, Rot4.South);
				DisablePawnWork(worker);
				worker.needs?.AddOrRemoveNeedsAsAppropriate();
				worker.ClearContamination();

				var heldBefore = frame.resourceContainer?.ToArray() ?? Array.Empty<Thing>();
				var heldBeforeState = heldBefore.Select(DescribeContaminationThing).ToArray();
				var heldContaminationBefore = heldBefore.Sum(thing => thing.GetContamination());
				var frameContaminationBefore = frame.GetContamination(false);
				var frameIncludingHoldingsBefore = frame.GetContamination(true);
				var contaminationSource = heldContaminationBefore > 0f ? heldContaminationBefore : frameContaminationBefore;
				var workerBefore = worker.GetContamination();
				var expectedFinalContamination = contaminationSource
					* ZombieSettings.Values.contamination.constructionAdd
					* (1f - ZombieSettings.Values.contamination.constructionTransfer);
				var expectedWorkerContamination = contaminationSource
					* ZombieSettings.Values.contamination.constructionAdd
					* ZombieSettings.Values.contamination.constructionTransfer;
				var finalDef = frame.def?.entityDefToBuild as ThingDef;
				var finalStuff = frame.Stuff;

				frame.CompleteConstruction(worker);
				finalThing = frameCell.GetEdifice(map);
				var finalContamination = finalThing?.GetContamination() ?? -1f;
				var workerAfter = worker.GetContamination();

				static bool CloseFloat(float value, float expected) => Mathf.Abs(value - expected) < 0.0001f;
				var finalBuilt = frame.Destroyed
					&& finalThing != null
					&& (finalDef == null || finalThing.def == finalDef)
					&& finalThing.Stuff == finalStuff;
				var contaminationTransferred = finalBuilt
					&& CloseFloat(workerBefore, 0f)
					&& CloseFloat(finalContamination, expectedFinalContamination)
					&& CloseFloat(workerAfter, expectedWorkerContamination);

				return new
				{
					success = finalBuilt && contaminationTransferred,
					target,
					cleanup,
					frame = frameId,
					frameThingId,
					frameCell = ZombieRuntimeActions.DescribeCell(frameCell),
					frameDef = frame.def?.defName,
					frameStuff = frame.Stuff?.defName,
					frameDestroyed = frame.Destroyed,
					worker = DescribePawn(worker),
					workerCell = ZombieRuntimeActions.DescribeCell(workerCell),
					heldBefore = heldBeforeState,
					heldContaminationBefore,
					frameContaminationBefore,
					frameIncludingHoldingsBefore,
					contaminationSource,
					finalThing = ZombieRuntimeActions.StableThingId(finalThing),
					finalDef = finalThing?.def?.defName,
					finalStuff = finalThing?.Stuff?.defName,
					finalContamination,
					expectedFinalContamination,
					workerBefore,
					workerAfter,
					expectedWorkerContamination,
					constructionAdd = ZombieSettings.Values.contamination.constructionAdd,
					constructionTransfer = ZombieSettings.Values.contamination.constructionTransfer,
					finalBuilt,
					contaminationTransferred
				};
			}
			finally
			{
				finalThing?.ClearContamination();
				worker?.ClearContamination();
				if (cleanup)
				{
					if (finalThing is { Destroyed: false, Spawned: true })
						finalThing.Destroy();
					if (worker is { Destroyed: false, Spawned: true })
						worker.Destroy();
				}
			}
		}

		[Tool("zombieland/contamination_cell_fire_contract", Description = "Verify contaminated ground affects pawns on cell entry and real Fire.DoComplexCalcs burns contamination down.")]
		public static object ContaminationCellFireContract()
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var entryCell, out var entrySpawnError) == false)
				return entrySpawnError;
			if (TryFindClearSpawnCell(map, entryCell + new IntVec3(4, 0, 0), 10f, out var fireCell, out var fireSpawnError) == false)
				return fireSpawnError;

			foreach (var existingFire in fireCell.GetThingList(map).OfType<Fire>().ToArray())
				existingFire.Destroy();
			map.SetContamination(entryCell, 0f);
			map.SetContamination(fireCell, 0f);

			var human = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(human, entryCell, map, Rot4.South);
			DisablePawnWork(human);
			human.needs?.AddOrRemoveNeedsAsAppropriate();
			human.ClearContamination();

			const float entryCellContamination = 0.8f;
			map.SetContamination(entryCell, entryCellContamination);
			var humanBeforeEntry = DescribeContamination(human);
			var entryGroundBefore = map.GetContamination(entryCell);
			human.filth.Notify_EnteredNewCell();
			var humanAfterEntry = DescribeContamination(human);
			var entryGroundAfter = map.GetContamination(entryCell);
			var expectedEntryGain = Mathf.Max(0f, entryGroundBefore * ZombieSettings.Values.contamination.cellFactor - humanBeforeEntry.stored)
				* ZombieSettings.Values.contamination.enterCellAdd;

			const float fireContaminationBefore = 0.4f;
			map.SetContamination(fireCell, fireContaminationBefore);
			var component = ThingMaker.MakeThing(ThingDefOf.ComponentIndustrial);
			GenSpawn.Spawn(component, fireCell, map, WipeMode.Vanish);
			component.SetContamination(fireContaminationBefore);
			var componentBeforeFire = component.GetContamination();
			var groundBeforeFire = map.GetContamination(fireCell);

			FireUtility.TryStartFireIn(fireCell, map, 0.5f, null);
			var fire = fireCell.GetThingList(map).OfType<Fire>().FirstOrDefault();
			if (fire == null)
			{
				return new
				{
					success = false,
					entryCell = ZombieRuntimeActions.DescribeCell(entryCell),
					fireCell = ZombieRuntimeActions.DescribeCell(fireCell),
					error = "Could not start a real fire for the contamination cleanup fixture."
				};
			}
			if (TryDoFireComplexCalcs(fire, out var fireError) == false)
			{
				return new
				{
					success = false,
					entryCell = ZombieRuntimeActions.DescribeCell(entryCell),
					fireCell = ZombieRuntimeActions.DescribeCell(fireCell),
					fire = ZombieRuntimeActions.StableThingId(fire),
					error = fireError
				};
			}

			var componentAfterFire = component.GetContamination();
			var groundAfterFire = map.GetContamination(fireCell);
			var expectedFireReduction = ZombieSettings.Values.contamination.fireReduction;
			var expectedComponentAfterFire = Mathf.Max(0f, componentBeforeFire - expectedFireReduction);
			var expectedGroundAfterFire = Mathf.Max(0f, groundBeforeFire - expectedFireReduction);

			static bool Close(float? value, float expected) => value.HasValue && Mathf.Abs(value.Value - expected) < 0.0001f;
			static bool CloseFloat(float value, float expected) => Mathf.Abs(value - expected) < 0.0001f;

			var entryApplied = CloseFloat(humanBeforeEntry.stored, 0f)
				&& CloseFloat(entryGroundBefore, entryCellContamination)
				&& CloseFloat(entryGroundAfter, entryGroundBefore)
				&& CloseFloat(humanAfterEntry.stored, expectedEntryGain)
				&& Close(humanAfterEntry.needLevel, expectedEntryGain)
				&& humanAfterEntry.hasHediff
				&& Close(humanAfterEntry.hediffSeverity, expectedEntryGain);
			var fireReduced = CloseFloat(componentAfterFire, expectedComponentAfterFire)
				&& CloseFloat(groundAfterFire, expectedGroundAfterFire);

			return new
			{
				success = entryApplied && fireReduced,
				human = DescribePawn(human),
				entryCell = ZombieRuntimeActions.DescribeCell(entryCell),
				fireCell = ZombieRuntimeActions.DescribeCell(fireCell),
				fire = ZombieRuntimeActions.StableThingId(fire),
				component = ZombieRuntimeActions.StableThingId(component),
				humanBeforeEntry,
				humanAfterEntry,
				entryGroundBefore,
				entryGroundAfter,
				expectedEntryGain,
				fireReduction = expectedFireReduction,
				componentBeforeFire,
				componentAfterFire,
				expectedComponentAfterFire,
				groundBeforeFire,
				groundAfterFire,
				expectedGroundAfterFire,
				entryApplied,
				fireReduced
			};
		}

		[Tool("zombieland/contamination_melee_equalize_contract", Description = "Verify real melee damage equalizes contamination between attacker and target.")]
		public static object ContaminationMeleeEqualizeContract()
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var attackerCell, out var attackerSpawnError) == false)
				return attackerSpawnError;
			if (TryFindClearSpawnCell(map, attackerCell + IntVec3.East, 8f, out var targetCell, out var targetSpawnError) == false)
				return targetSpawnError;

			var attacker = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var target = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			try
			{
				GenSpawn.Spawn(attacker, attackerCell, map, Rot4.East);
				GenSpawn.Spawn(target, targetCell, map, Rot4.West);
				DisablePawnWork(attacker);
				DisablePawnWork(target);
				attacker.ClearContamination();
				target.ClearContamination();

				const float targetContaminationBefore = 0.8f;
				target.SetContamination(targetContaminationBefore);
				var attackerBefore = DescribeContamination(attacker);
				var targetBefore = DescribeContamination(target);
				var verb = attacker.meleeVerbs.TryGetMeleeVerb(target);
				if (TryApplyMeleeDamageToTarget(verb, target, out var damageResult, out var meleeError) == false)
				{
					return new
					{
						success = false,
						attacker = DescribePawn(attacker),
						target = DescribePawn(target),
						attackerCell = ZombieRuntimeActions.DescribeCell(attackerCell),
						targetCell = ZombieRuntimeActions.DescribeCell(targetCell),
						verb = DescribeVerb(verb),
						error = meleeError
					};
				}

				var attackerAfter = DescribeContamination(attacker);
				var targetAfter = DescribeContamination(target);
				var meleeEqualize = ZombieSettings.Values.contamination.meleeEqualize;
				var expectedTransfer = (targetBefore.stored - attackerBefore.stored) * meleeEqualize;
				var expectedAttackerAfter = attackerBefore.stored + expectedTransfer;
				var expectedTargetAfter = targetBefore.stored - expectedTransfer;
				static bool Close(float? value, float expected) => value.HasValue && Mathf.Abs(value.Value - expected) < 0.0001f;
				static bool CloseFloat(float value, float expected) => Mathf.Abs(value - expected) < 0.0001f;

				var damageApplied = damageResult != null && damageResult.totalDamageDealt > 0f;
				var contaminationEqualized = damageApplied
					&& CloseFloat(attackerBefore.stored, 0f)
					&& CloseFloat(targetBefore.stored, targetContaminationBefore)
					&& CloseFloat(attackerAfter.stored, expectedAttackerAfter)
					&& CloseFloat(targetAfter.stored, expectedTargetAfter)
					&& Close(attackerAfter.needLevel, expectedAttackerAfter)
					&& Close(targetAfter.needLevel, expectedTargetAfter)
					&& attackerAfter.hasHediff
					&& targetAfter.hasHediff;

				return new
				{
					success = contaminationEqualized,
					attacker = DescribePawn(attacker),
					target = DescribePawn(target),
					attackerCell = ZombieRuntimeActions.DescribeCell(attackerCell),
					targetCell = ZombieRuntimeActions.DescribeCell(targetCell),
					verb = DescribeVerb(verb),
					damageApplied,
					damageTotal = damageResult?.totalDamageDealt ?? 0f,
					meleeEqualize,
					expectedTransfer,
					expectedAttackerAfter,
					expectedTargetAfter,
					attackerBefore,
					attackerAfter,
					targetBefore,
					targetAfter,
					contaminationEqualized
				};
			}
			finally
			{
				attacker?.ClearContamination();
				target?.ClearContamination();
				if (attacker is { Destroyed: false, Spawned: true })
					attacker.Destroy();
				if (target is { Destroyed: false, Spawned: true })
					target.Destroy();
			}
		}

		[Tool("zombieland/contamination_filth_leavings_contract", Description = "Verify contaminated pawns/things create contaminated blood, carried filth, execution blood, vomit, birth, dissolve-gear, liquid projectile, tunnel-hive, explosion, comp-spawned, damage, butcher, and leavings products.")]
		public static object ContaminationFilthLeavingsContract(
			[ToolParameter(Description = "Destroy generated filth and leavings before returning.", Required = false, DefaultValue = true)] bool cleanup = true)
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var shipChunkDef = DefDatabase<ThingDef>.GetNamedSilentFail("ShipChunk");
			if (shipChunkDef == null)
			{
				return new
				{
					success = false,
					error = "ShipChunk def is unavailable."
				};
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var pawnCell, out var pawnSpawnError) == false)
				return pawnSpawnError;
			if (TryFindClearBuildingFootprint(map, shipChunkDef, pawnCell + new IntVec3(5, 0, 0), 16f, out var shipChunkCell, out var shipChunkSpawnError) == false)
				return shipChunkSpawnError;

			var pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var shipChunk = (Thing)null;
			var bloodFilth = (Filth)null;
			var leavings = new List<Thing>();
			try
			{
				ClearFilthAt(map, pawnCell);
				GenSpawn.Spawn(pawn, pawnCell, map, Rot4.South);
				DisablePawnWork(pawn);
				pawn.ClearContamination();

				const float pawnContamination = 0.7f;
				pawn.SetContamination(pawnContamination);
				pawn.health.DropBloodFilth();
				bloodFilth = pawnCell.GetThingList(map).OfType<Filth>().OrderByDescending(filth => filth.GetContamination()).FirstOrDefault();
				var bloodContamination = bloodFilth?.GetContamination() ?? -1f;
				var expectedBloodContamination = pawnContamination * ZombieSettings.Values.contamination.filthEqualize;
				var bloodProduced = bloodFilth != null && bloodFilth.def == ThingDefOf.Filth_Blood;

				shipChunk = ThingMaker.MakeThing(shipChunkDef);
				GenSpawn.Spawn(shipChunk, shipChunkCell, map, Rot4.North, WipeMode.Vanish, false);
				const float shipChunkContamination = 0.8f;
				shipChunk.SetContamination(shipChunkContamination);
				var shipChunkBefore = shipChunk.GetContamination();
				var leavingsTransfer = ZombieSettings.Values.contamination.leavingsTransfer;

				GenLeaving.DoLeavingsFor(shipChunk, map, DestroyMode.KillFinalize, leavings);
				var butcherProducts = VerifyCorpseButcherProductsTransfer(map, shipChunkCell + new IntVec3(6, 0, 0));
				var vomitFilth = VerifyVomitJobFilthTransfer(map, shipChunkCell + new IntVec3(12, 0, 0));
				var compSpawnerFilth = VerifyCompSpawnerFilthTransfer(map, shipChunkCell + new IntVec3(18, 0, 0));
				var damageFilth = VerifyThingTakeDamageFilthTransfer(map, shipChunkCell + new IntVec3(24, 0, 0));
				var birthFilth = VerifyBirthFilthTransfer(map, shipChunkCell + new IntVec3(30, 0, 0));
				var flameAsh = VerifyFlameDamageAshTransfer(map, shipChunkCell + new IntVec3(36, 0, 0));
				var dissolveGearFilth = VerifyDissolveGearOnDeathFilthTransfer(map, shipChunkCell + new IntVec3(42, 0, 0));
				var liquidProjectileFilth = VerifyLiquidProjectileFilthTransfer(map, shipChunkCell + new IntVec3(48, 0, 0));
				var tunnelHiveFilth = VerifyTunnelHiveSpawnerTickFilthTransfer(map, shipChunkCell + new IntVec3(54, 0, 0));
				var explosionFilth = VerifyExplosionSpawnedFilthTransfer(map, shipChunkCell + new IntVec3(60, 0, 0));
				var executionBlood = VerifyExecutionBloodTransfer(map, shipChunkCell + new IntVec3(66, 0, 0));
				var carriedFilth = VerifyPawnGainFilthTransfer(map, shipChunkCell + new IntVec3(72, 0, 0));

				var shipChunkAfter = shipChunk.GetContamination();
				var leavingsContamination = leavings.Select(thing => thing.GetContamination()).ToArray();
				var totalLeavingsContamination = leavingsContamination.Sum();
				var expectedTotalLeavingsContamination = shipChunkBefore * leavingsTransfer;
				var expectedShipChunkAfter = shipChunkBefore * (1f - leavingsTransfer);

				static bool CloseFloat(float value, float expected) => Mathf.Abs(value - expected) < 0.0001f;
				var bloodContaminated = bloodProduced && CloseFloat(bloodContamination, expectedBloodContamination);
				var leavingsContaminated = leavings.Count > 0
					&& leavings.All(thing => thing.Spawned)
					&& leavingsContamination.All(value => value > 0f)
					&& CloseFloat(totalLeavingsContamination, expectedTotalLeavingsContamination)
					&& CloseFloat(shipChunkAfter, expectedShipChunkAfter);

				return new
				{
					success = bloodContaminated
						&& leavingsContaminated
						&& ObjectSuccess(butcherProducts)
						&& ObjectSuccess(vomitFilth)
						&& ObjectSuccess(compSpawnerFilth)
						&& ObjectSuccess(damageFilth)
						&& ObjectSuccess(birthFilth)
						&& ObjectSuccess(flameAsh)
						&& ObjectSuccess(dissolveGearFilth)
						&& ObjectSuccess(liquidProjectileFilth)
						&& ObjectSuccess(tunnelHiveFilth)
						&& ObjectSuccess(explosionFilth)
						&& ObjectSuccess(executionBlood)
						&& ObjectSuccess(carriedFilth),
					cleanup,
					pawn = DescribePawn(pawn),
					pawnCell = ZombieRuntimeActions.DescribeCell(pawnCell),
					pawnContamination,
					bloodFilth = ZombieRuntimeActions.StableThingId(bloodFilth),
					bloodDef = bloodFilth?.def?.defName,
					bloodContamination,
					expectedBloodContamination,
					bloodProduced,
					bloodContaminated,
					shipChunk = ZombieRuntimeActions.StableThingId(shipChunk),
					shipChunkCell = ZombieRuntimeActions.DescribeCell(shipChunkCell),
					shipChunkBefore,
					shipChunkAfter,
					expectedShipChunkAfter,
					leavingsTransfer,
					leavings = leavings.Select(thing => new
					{
						thing = ZombieRuntimeActions.StableThingId(thing),
						def = thing.def?.defName,
						stackCount = thing.stackCount,
						contamination = thing.GetContamination()
					}).ToArray(),
					totalLeavingsContamination,
					expectedTotalLeavingsContamination,
					leavingsContaminated,
					butcherProducts,
					vomitFilth,
					compSpawnerFilth,
					damageFilth,
					birthFilth,
					flameAsh,
					dissolveGearFilth,
					liquidProjectileFilth,
					tunnelHiveFilth,
					explosionFilth,
					executionBlood,
					carriedFilth
				};
			}
			finally
			{
				pawn?.ClearContamination();
				if (cleanup && bloodFilth is { Destroyed: false, Spawned: true })
					bloodFilth.Destroy();
				if (cleanup)
				{
					foreach (var leaving in leavings)
					{
						leaving.ClearContamination();
						if (leaving is { Destroyed: false, Spawned: true })
							leaving.Destroy();
					}
				}
				if (cleanup)
					shipChunk?.ClearContamination();
				if (cleanup && shipChunk is { Destroyed: false, Spawned: true })
					shipChunk.Destroy();
				if (pawn is { Destroyed: false, Spawned: true })
					pawn.Destroy();
			}
		}

		static object VerifyPawnGainFilthTransfer(Map map, IntVec3 root)
		{
			var patchTargets = PatchedMethodsForPatchClass("Filth_MakeThing_Patch");
			var gainFilthMethod = typeof(Pawn_FilthTracker).GetMethods()
				.FirstOrDefault(method =>
					method.Name == nameof(Pawn_FilthTracker.GainFilth)
					&& method.GetParameters().Length == 2);
			if (gainFilthMethod == null)
			{
				return new
				{
					success = false,
					targets = CompactPatchTargets(patchTargets),
					error = "Pawn_FilthTracker.GainFilth two-argument overload was not found."
				};
			}
			if (TryFindClearSpawnCell(map, root, 16f, out var pawnCell, out var pawnCellError) == false)
				return pawnCellError;

			var pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			try
			{
				GenSpawn.Spawn(pawn, pawnCell, map, Rot4.South);
				DisablePawnWork(pawn);
				pawn.ClearContamination();
				pawn.filth.CarriedFilthListForReading.Clear();

				const float pawnContamination = 0.58f;
				pawn.SetContamination(pawnContamination);
				var pawnBefore = pawn.GetContamination();
				var filthDef = ThingDefOf.Filth_Dirt;
				var filthEqualize = ZombieSettings.Values.contamination.filthEqualize;
				var expectedFilthContamination = pawnBefore * filthEqualize;

				Filth_MakeThing_Patch.filthSource = pawn;
				try
				{
					pawn.filth.GainFilth(filthDef, new[] { "zombieland-bridge-gainfilth" });
				}
				finally
				{
					Filth_MakeThing_Patch.filthSource = null;
				}

				var carried = pawn.filth.CarriedFilthListForReading
					.Where(filth => filth.def == filthDef)
					.OrderByDescending(filth => filth.GetContamination())
					.ToArray();
				var carriedFilth = carried.FirstOrDefault();
				var carriedContamination = carriedFilth?.GetContamination() ?? -1f;
				var carriedFilthContaminated = carried.Length == 1
					&& Approximately(carriedContamination, expectedFilthContamination, 0.0001f);

				return new
				{
					success = patchTargets.Length > 0
						&& carriedFilthContaminated,
					targets = CompactPatchTargets(patchTargets),
					method = $"{gainFilthMethod.DeclaringType?.FullName}::{gainFilthMethod}",
					pawn = DescribePawn(pawn),
					pawnCell = ZombieRuntimeActions.DescribeCell(pawnCell),
					pawnBefore,
					filthDef = filthDef.defName,
					filthEqualize,
					expectedFilthContamination,
					carriedFilth = DescribeContaminationThing(carriedFilth),
					carriedFilthCount = carried.Length,
					carriedContamination,
					carriedFilthContaminated
				};
			}
			finally
			{
				Filth_MakeThing_Patch.filthSource = null;
				Filth_MakeThing_Patch.filthCell = null;
				pawn?.filth?.CarriedFilthListForReading.Clear();
				pawn?.ClearContamination();
				if (pawn is { Destroyed: false, Spawned: true })
					pawn.Destroy();
			}
		}

		static object VerifyExecutionBloodTransfer(Map map, IntVec3 root)
		{
			var patchTargets = PatchedMethodsForPatchClass("ExecutionUtility_ExecutionInt_Patch");
			var executionInt = typeof(ExecutionUtility).GetMethod("ExecutionInt", BindingFlags.Static | BindingFlags.NonPublic);
			if (executionInt == null)
			{
				return new
				{
					success = false,
					targets = CompactPatchTargets(patchTargets),
					error = "ExecutionUtility.ExecutionInt private helper was not found."
				};
			}
			if (TryFindClearSpawnCell(map, root, 16f, out var executionerCell, out var executionerCellError) == false)
				return executionerCellError;
			if (TryFindClearSpawnCell(map, executionerCell + IntVec3.East, 8f, out var victimCell, out var victimCellError) == false)
				return victimCellError;

			var executioner = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var victim = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var bloodFilths = new List<Filth>();
			Corpse corpse = null;
			try
			{
				foreach (var cell in GenRadial.RadialCellsAround(victimCell, 2f, true).Where(cell => cell.InBounds(map)))
					ClearFilthAt(map, cell);
				GenSpawn.Spawn(executioner, executionerCell, map, Rot4.East);
				GenSpawn.Spawn(victim, victimCell, map, Rot4.West);
				DisablePawnWork(executioner);
				DisablePawnWork(victim);
				executioner.ClearContamination();
				const float victimContamination = 0.68f;
				victim.SetContamination(victimContamination);
				var victimBefore = victim.GetContamination();

				Rand.PushState(7104);
				try
				{
					executionInt.Invoke(null, new object[] { executioner, victim, false, 3, true });
				}
				finally
				{
					Rand.PopState();
				}

				corpse = victim.Corpse;
				bloodFilths = GenRadial.RadialCellsAround(victimCell, 2f, true)
					.Where(cell => cell.InBounds(map))
					.SelectMany(cell => cell.GetThingList(map).OfType<Filth>())
					.Where(filth => filth.def == ThingDefOf.Filth_Blood)
					.OrderByDescending(filth => filth.GetContamination())
					.ToList();
				var bloodContaminations = bloodFilths.Select(filth => filth.GetContamination()).ToArray();
				var expectedBloodContamination = victimBefore * ZombieSettings.Values.contamination.filthEqualize;
				var bloodContaminated = bloodFilths.Count > 0
					&& bloodContaminations.All(value => Approximately(value, expectedBloodContamination, 0.0001f));

				return new
				{
					success = patchTargets.Length > 0
						&& victim.Dead
						&& bloodContaminated,
					targets = CompactPatchTargets(patchTargets),
					method = $"{executionInt.DeclaringType?.FullName}::{executionInt}",
					executioner = DescribePawn(executioner),
					executionerCell = ZombieRuntimeActions.DescribeCell(executionerCell),
					victim = DescribePawn(victim),
					victimCell = ZombieRuntimeActions.DescribeCell(victimCell),
					victimBefore,
					victimDead = victim.Dead,
					corpse = ZombieRuntimeActions.StableThingId(corpse),
					bloodPerWeight = 3,
					filthEqualize = ZombieSettings.Values.contamination.filthEqualize,
					expectedBloodContamination,
					bloodFilths = bloodFilths.Select(filth => new
					{
						thing = ZombieRuntimeActions.StableThingId(filth),
						cell = filth.Spawned ? ZombieRuntimeActions.DescribeCell(filth.Position) : null,
						contamination = filth.GetContamination()
					}).ToArray(),
					bloodFilthCount = bloodFilths.Count,
					bloodContaminated
				};
			}
			finally
			{
				foreach (var filth in bloodFilths.Where(filth => filth is { Destroyed: false, Spawned: true }).ToArray())
				{
					filth.ClearContamination();
					filth.Destroy();
				}
				executioner?.ClearContamination();
				victim?.ClearContamination();
				corpse?.ClearContamination();
				if (corpse is { Destroyed: false, Spawned: true })
					corpse.Destroy();
				if (victim is { Destroyed: false, Spawned: true })
					victim.Destroy();
				if (executioner is { Destroyed: false, Spawned: true })
					executioner.Destroy();
			}
		}

		static object VerifyExplosionSpawnedFilthTransfer(Map map, IntVec3 root)
		{
			var patchTargets = PatchedMethodsForPatchClass("Verse_Explosion_TrySpawnExplosionThing_Patch");
			var trySpawnExplosionThing = typeof(Verse.Explosion).GetMethod("TrySpawnExplosionThing", BindingFlags.Instance | BindingFlags.NonPublic);
			var damagedThingsField = typeof(Verse.Explosion).GetField("damagedThings", BindingFlags.Instance | BindingFlags.NonPublic);
			if (trySpawnExplosionThing == null || damagedThingsField == null)
			{
				return new
				{
					success = false,
					targets = CompactPatchTargets(patchTargets),
					error = "Explosion private spawn helper or damagedThings field was not found."
				};
			}
			if (TryFindClearSpawnCell(map, root, 16f, out var lowSourceCell, out var lowSourceCellError) == false)
				return lowSourceCellError;
			if (TryFindClearSpawnCell(map, lowSourceCell + IntVec3.East, 8f, out var highSourceCell, out var highSourceCellError) == false)
				return highSourceCellError;
			if (TryFindClearSpawnCell(map, highSourceCell + IntVec3.East, 8f, out var explosionCell, out var explosionCellError) == false)
				return explosionCellError;
			if (TryFindClearSpawnCell(map, explosionCell + IntVec3.East, 8f, out var targetCell, out var targetCellError) == false)
				return targetCellError;

			var lowSource = ThingMaker.MakeThing(ThingDefOf.Steel);
			var highSource = ThingMaker.MakeThing(ThingDefOf.Steel);
			var explosion = ThingMaker.MakeThing(ThingDefOf.Explosion) as Verse.Explosion;
			var spawnedFilths = new List<Filth>();
			try
			{
				if (explosion == null)
				{
					return new
					{
						success = false,
						targets = CompactPatchTargets(patchTargets),
						error = "ThingDefOf.Explosion did not create a Verse.Explosion instance."
					};
				}

				foreach (var cell in GenRadial.RadialCellsAround(targetCell, 2f, true).Where(cell => cell.InBounds(map)))
					ClearFilthAt(map, cell);
				lowSource.stackCount = 1;
				highSource.stackCount = 1;
				GenSpawn.Spawn(lowSource, lowSourceCell, map, WipeMode.Vanish);
				GenSpawn.Spawn(highSource, highSourceCell, map, WipeMode.Vanish);
				GenSpawn.Spawn(explosion, explosionCell, map, WipeMode.Vanish);

				const float lowSourceContamination = 0.22f;
				const float highSourceContamination = 0.74f;
				lowSource.SetContamination(lowSourceContamination);
				highSource.SetContamination(highSourceContamination);
				var lowSourceBefore = lowSource.GetContamination();
				var highSourceBefore = highSource.GetContamination();
				var damagedThings = damagedThingsField.GetValue(explosion) as List<Thing>;
				if (damagedThings == null)
				{
					return new
					{
						success = false,
						targets = CompactPatchTargets(patchTargets),
						explosion = ZombieRuntimeActions.StableThingId(explosion),
						error = "Explosion.damagedThings was not initialized after spawning."
					};
				}
				damagedThings.Clear();
				damagedThings.Add(lowSource);
				damagedThings.Add(highSource);

				var filthDef = ThingDefOf.Filth_Dirt;
				const int filthCount = 1;
				Rand.PushState(29);
				try
				{
					trySpawnExplosionThing.Invoke(explosion, new object[] { filthDef, targetCell, filthCount });
				}
				finally
				{
					Rand.PopState();
				}

				spawnedFilths = GenRadial.RadialCellsAround(targetCell, 2f, true)
					.Where(cell => cell.InBounds(map))
					.SelectMany(cell => cell.GetThingList(map).OfType<Filth>())
					.Where(filth => filth.def == filthDef)
					.OrderByDescending(filth => filth.GetContamination())
					.ToList();
				var contaminations = spawnedFilths.Select(filth => filth.GetContamination()).ToArray();
				var expectedFilthContamination = highSourceBefore * ZombieSettings.Values.contamination.filthEqualize;
				var ignoredLowerSource = spawnedFilths.Count > 0
					&& contaminations.All(value => Mathf.Abs(value - lowSourceBefore * ZombieSettings.Values.contamination.filthEqualize) > 0.0001f);
				var filthsContaminated = spawnedFilths.Count > 0
					&& contaminations.All(value => Approximately(value, expectedFilthContamination, 0.0001f));

				return new
				{
					success = patchTargets.Length > 0
						&& filthsContaminated
						&& ignoredLowerSource,
					targets = CompactPatchTargets(patchTargets),
					method = $"{trySpawnExplosionThing.DeclaringType?.FullName}::{trySpawnExplosionThing}",
					explosion = ZombieRuntimeActions.StableThingId(explosion),
					explosionCell = ZombieRuntimeActions.DescribeCell(explosionCell),
					lowSource = ZombieRuntimeActions.StableThingId(lowSource),
					lowSourceCell = ZombieRuntimeActions.DescribeCell(lowSourceCell),
					lowSourceBefore,
					highSource = ZombieRuntimeActions.StableThingId(highSource),
					highSourceCell = ZombieRuntimeActions.DescribeCell(highSourceCell),
					highSourceBefore,
					targetCell = ZombieRuntimeActions.DescribeCell(targetCell),
					damagedThingCount = damagedThings.Count,
					selectedSourceContamination = highSourceBefore,
					filthDef = filthDef.defName,
					filthCountRequested = filthCount,
					filthEqualize = ZombieSettings.Values.contamination.filthEqualize,
					expectedFilthContamination,
					lowerSourceWouldHaveProduced = lowSourceBefore * ZombieSettings.Values.contamination.filthEqualize,
					ignoredLowerSource,
					filths = spawnedFilths.Select(filth => new
					{
						thing = ZombieRuntimeActions.StableThingId(filth),
						cell = filth.Spawned ? ZombieRuntimeActions.DescribeCell(filth.Position) : null,
						contamination = filth.GetContamination()
					}).ToArray(),
					filthCount = spawnedFilths.Count,
					filthsContaminated
				};
			}
			finally
			{
				foreach (var filth in spawnedFilths.Where(filth => filth is { Destroyed: false, Spawned: true }).ToArray())
				{
					filth.ClearContamination();
					filth.Destroy();
				}
				lowSource?.ClearContamination();
				highSource?.ClearContamination();
				if (lowSource is { Destroyed: false, Spawned: true })
					lowSource.Destroy();
				if (highSource is { Destroyed: false, Spawned: true })
					highSource.Destroy();
				if (explosion is { Destroyed: false, Spawned: true })
					explosion.Destroy();
			}
		}

		static object VerifyTunnelHiveSpawnerTickFilthTransfer(Map map, IntVec3 root)
		{
			var patchTargets = PatchedMethodsForPatchClass("TunnelHiveSpawner_Tick_Patch");
			var spawnerDef = ThingDefOf.TunnelHiveSpawner;
			if (spawnerDef == null)
			{
				return new
				{
					success = false,
					targets = CompactPatchTargets(patchTargets),
					error = "ThingDefOf.TunnelHiveSpawner was unavailable for the tunnel-hive filth probe."
				};
			}
			if (TryFindClearSpawnCell(map, root, 16f, out var spawnerCell, out var spawnerCellError) == false)
				return spawnerCellError;

			var filthSpawnMtbField = typeof(GroundSpawner).GetField("filthSpawnMTB", BindingFlags.Instance | BindingFlags.NonPublic);
			var filthSpawnRadiusField = typeof(GroundSpawner).GetField("filthSpawnRadius", BindingFlags.Instance | BindingFlags.NonPublic);
			var secondarySpawnTickField = typeof(GroundSpawner).GetField("secondarySpawnTick", BindingFlags.Instance | BindingFlags.NonPublic);
			if (filthSpawnMtbField == null || filthSpawnRadiusField == null || secondarySpawnTickField == null)
			{
				return new
				{
					success = false,
					targets = CompactPatchTargets(patchTargets),
					error = "GroundSpawner private tick-control fields were not found."
				};
			}

			var spawner = ThingMaker.MakeThing(spawnerDef) as TunnelHiveSpawner;
			var spawnedFilths = new List<Filth>();
			var oldGroundContamination = map.GetContamination(spawnerCell);
			try
			{
				if (spawner == null)
				{
					return new
					{
						success = false,
						targets = CompactPatchTargets(patchTargets),
						spawnerDef = spawnerDef.defName,
						error = "Selected tunnel hive spawner def did not create a TunnelHiveSpawner instance."
					};
				}

				foreach (var cell in GenRadial.RadialCellsAround(spawnerCell, 4f, true).Where(cell => cell.InBounds(map)))
					ClearFilthAt(map, cell);
				GenSpawn.Spawn(spawner, spawnerCell, map, WipeMode.Vanish);
				spawner.spawnHive = false;
				spawner.insectsPoints = 0f;
				spawner.ClearContamination();
				const float groundContamination = 0.63f;
				map.SetContamination(spawnerCell, groundContamination);
				var groundBefore = map.GetContamination(spawnerCell);
				var spawnerBefore = spawner.GetContamination();
				filthSpawnMtbField.SetValue(spawner, 0.0001f);
				filthSpawnRadiusField.SetValue(spawner, 3f);
				secondarySpawnTickField.SetValue(spawner, Find.TickManager.TicksGame + 5000);

				var ticksRun = 0;
				const int maxTicks = 120;
				Rand.PushState(23);
				try
				{
					while (ticksRun < maxTicks && spawnedFilths.Count == 0)
					{
						AdvanceGameTicks(1);
						ticksRun++;
						spawnedFilths = GenRadial.RadialCellsAround(spawnerCell, 4f, true)
							.Where(cell => cell.InBounds(map))
							.SelectMany(cell => cell.GetThingList(map).OfType<Filth>())
							.Where(filth => filth.def == ThingDefOf.Filth_Dirt || filth.def == ThingDefOf.Filth_RubbleRock)
							.OrderByDescending(filth => filth.GetContamination())
							.ToList();
					}
				}
				finally
				{
					Rand.PopState();
				}

				var contaminations = spawnedFilths.Select(filth => filth.GetContamination()).ToArray();
				var expectedFilthContamination = groundBefore * ZombieSettings.Values.contamination.filthEqualize;
				var filthsContaminated = spawnedFilths.Count > 0
					&& contaminations.All(value => Approximately(value, expectedFilthContamination, 0.0001f));
				var groundAfter = map.GetContamination(spawnerCell);

				return new
				{
					success = patchTargets.Length > 0
						&& filthsContaminated,
					targets = CompactPatchTargets(patchTargets),
					method = "RimWorld.GroundSpawner::Tick() filtered to TunnelHiveSpawner",
					spawner = DescribeContaminationThing(spawner),
					spawnerCell = ZombieRuntimeActions.DescribeCell(spawnerCell),
					groundBefore,
					groundAfter,
					spawnerBefore,
					filthEqualize = ZombieSettings.Values.contamination.filthEqualize,
					expectedFilthContamination,
					ticksRun,
					filths = spawnedFilths.Select(filth => new
					{
						thing = ZombieRuntimeActions.StableThingId(filth),
						def = filth.def?.defName,
						cell = filth.Spawned ? ZombieRuntimeActions.DescribeCell(filth.Position) : null,
						contamination = filth.GetContamination()
					}).ToArray(),
					filthCount = spawnedFilths.Count,
					filthsContaminated
				};
			}
			finally
			{
				foreach (var filth in spawnedFilths.Where(filth => filth is { Destroyed: false, Spawned: true }).ToArray())
				{
					filth.ClearContamination();
					filth.Destroy();
				}
				map.SetContamination(spawnerCell, oldGroundContamination);
				spawner?.ClearContamination();
				if (spawner is { Destroyed: false, Spawned: true })
					spawner.Destroy();
			}
		}

		static object VerifyLiquidProjectileFilthTransfer(Map map, IntVec3 root)
		{
			var patchTargets = PatchedMethodsForPatchClass("Projectile_Liquid_DoImpact_Patch");
			var doImpact = typeof(Projectile_Liquid).GetMethod("DoImpact", BindingFlags.Instance | BindingFlags.NonPublic);
			if (doImpact == null)
			{
				return new
				{
					success = false,
					targets = CompactPatchTargets(patchTargets),
					error = "Projectile_Liquid.DoImpact private helper was not found."
				};
			}
			var liquidDef = DefDatabase<ThingDef>.AllDefs
				.Where(def => typeof(Projectile_Liquid).IsAssignableFrom(def.thingClass))
				.Where(def => def.projectile?.filth != null && def.projectile.filthCount.TrueMax > 0)
				.OrderBy(def => def.defName)
				.FirstOrDefault();
			if (liquidDef == null)
			{
				return new
				{
					success = false,
					targets = CompactPatchTargets(patchTargets),
					error = "No Projectile_Liquid def with configured filth was available."
				};
			}
			if (TryFindClearSpawnCell(map, root, 16f, out var sourceCell, out var sourceCellError) == false)
				return sourceCellError;
			if (TryFindClearSpawnCell(map, sourceCell + IntVec3.East, 8f, out var projectileCell, out var projectileCellError) == false)
				return projectileCellError;
			if (TryFindClearSpawnCell(map, projectileCell + IntVec3.East, 8f, out var targetCell, out var targetCellError) == false)
				return targetCellError;

			var source = ThingMaker.MakeThing(ThingDefOf.Steel);
			var projectile = ThingMaker.MakeThing(liquidDef) as Projectile_Liquid;
			var spawnedFilths = new List<Filth>();
			var oldFilthChance = liquidDef.projectile.filthChance;
			try
			{
				if (projectile == null)
				{
					return new
					{
						success = false,
						targets = CompactPatchTargets(patchTargets),
						projectileDef = liquidDef.defName,
						error = "Selected liquid projectile def did not create a Projectile_Liquid instance."
					};
				}

				foreach (var cell in GenRadial.RadialCellsAround(targetCell, 2f, true).Where(cell => cell.InBounds(map)))
					ClearFilthAt(map, cell);
				source.stackCount = 1;
				GenSpawn.Spawn(source, sourceCell, map, WipeMode.Vanish);
				GenSpawn.Spawn(projectile, projectileCell, map, WipeMode.Vanish);

				const float sourceContamination = 0.57f;
				source.SetContamination(sourceContamination);
				var sourceBefore = source.GetContamination();
				var filthDef = liquidDef.projectile.filth;
				liquidDef.projectile.filthChance = 1f;
				Rand.PushState(17);
				try
				{
					doImpact.Invoke(projectile, new object[] { source, targetCell });
				}
				finally
				{
					Rand.PopState();
				}

				spawnedFilths = GenRadial.RadialCellsAround(targetCell, 2f, true)
					.Where(cell => cell.InBounds(map))
					.SelectMany(cell => cell.GetThingList(map).OfType<Filth>())
					.Where(filth => filth.def == filthDef)
					.OrderByDescending(filth => filth.GetContamination())
					.ToList();
				var contaminations = spawnedFilths.Select(filth => filth.GetContamination()).ToArray();
				var expectedFilthContamination = sourceBefore * ZombieSettings.Values.contamination.filthEqualize;
				var filthsContaminated = spawnedFilths.Count > 0
					&& contaminations.All(value => Approximately(value, expectedFilthContamination, 0.0001f));

				return new
				{
					success = patchTargets.Length > 0
						&& filthsContaminated,
					targets = CompactPatchTargets(patchTargets),
					method = $"{doImpact.DeclaringType?.FullName}::{doImpact}",
					projectile = ZombieRuntimeActions.StableThingId(projectile),
					projectileDef = liquidDef.defName,
					projectileCell = ZombieRuntimeActions.DescribeCell(projectileCell),
					source = ZombieRuntimeActions.StableThingId(source),
					sourceCell = ZombieRuntimeActions.DescribeCell(sourceCell),
					sourceBefore,
					targetCell = ZombieRuntimeActions.DescribeCell(targetCell),
					filthDef = filthDef.defName,
					filthEqualize = ZombieSettings.Values.contamination.filthEqualize,
					expectedFilthContamination,
					filths = spawnedFilths.Select(filth => new
					{
						thing = ZombieRuntimeActions.StableThingId(filth),
						cell = filth.Spawned ? ZombieRuntimeActions.DescribeCell(filth.Position) : null,
						contamination = filth.GetContamination()
					}).ToArray(),
					filthCount = spawnedFilths.Count,
					filthsContaminated
				};
			}
			finally
			{
				liquidDef.projectile.filthChance = oldFilthChance;
				foreach (var filth in spawnedFilths.Where(filth => filth is { Destroyed: false, Spawned: true }).ToArray())
				{
					filth.ClearContamination();
					filth.Destroy();
				}
				source?.ClearContamination();
				if (source is { Destroyed: false, Spawned: true })
					source.Destroy();
				if (projectile is { Destroyed: false, Spawned: true })
					projectile.Destroy();
			}
		}

		static object VerifyDissolveGearOnDeathFilthTransfer(Map map, IntVec3 root)
		{
			var patchTargets = PatchedMethodsForPatchClass("HediffComp_DissolveGearOnDeath_Notify_PawnKilled_Patch");
			var filthDef = ThingDefOf.Filth_Slime ?? ThingDefOf.Filth_Blood;
			if (filthDef == null)
			{
				return new
				{
					success = false,
					targets = CompactPatchTargets(patchTargets),
					error = "No filth def was available for the dissolve-gear-on-death probe."
				};
			}
			if (TryFindClearSpawnCell(map, root, 16f, out var pawnCell, out var pawnCellError) == false)
				return pawnCellError;

			var pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var spawnedFilths = new List<Filth>();
			try
			{
				foreach (var cell in GenRadial.RadialCellsAround(pawnCell, 3f, true).Where(cell => cell.InBounds(map)))
					ClearFilthAt(map, cell);
				GenSpawn.Spawn(pawn, pawnCell, map, Rot4.South);
				DisablePawnWork(pawn);
				pawn.ClearContamination();

				const float pawnContamination = 0.49f;
				pawn.SetContamination(pawnContamination);
				var pawnBefore = pawn.GetContamination();
				var props = new HediffCompProperties_DissolveGearOnDeath
				{
					filth = filthDef,
					filthCount = 3,
					fleck = null,
					mote = null,
					sound = null
				};
				var parent = new HediffWithComps
				{
					pawn = pawn,
					def = DefDatabase<HediffDef>.AllDefs.FirstOrDefault(def => def?.comps?.Contains(props) == true)
				};
				var comp = new HediffComp_DissolveGearOnDeath
				{
					parent = parent,
					props = props
				};
				parent.comps = new List<HediffComp> { comp };

				Rand.PushState(13);
				try
				{
					comp.Notify_PawnKilled();
				}
				finally
				{
					Rand.PopState();
				}

				spawnedFilths = GenRadial.RadialCellsAround(pawnCell, 3f, true)
					.Where(cell => cell.InBounds(map))
					.SelectMany(cell => cell.GetThingList(map).OfType<Filth>())
					.Where(filth => filth.def == filthDef)
					.OrderByDescending(filth => filth.GetContamination())
					.ToList();
				var contaminations = spawnedFilths.Select(filth => filth.GetContamination()).ToArray();
				var expectedFilthContamination = pawnBefore * ZombieSettings.Values.contamination.filthEqualize;
				var filthsContaminated = spawnedFilths.Count > 0
					&& contaminations.All(value => Approximately(value, expectedFilthContamination, 0.0001f));

				return new
				{
					success = patchTargets.Length > 0
						&& filthsContaminated,
					targets = CompactPatchTargets(patchTargets),
					method = "RimWorld.HediffComp_DissolveGearOnDeath::Notify_PawnKilled()",
					pawn = DescribePawn(pawn),
					pawnCell = ZombieRuntimeActions.DescribeCell(pawnCell),
					pawnBefore,
					filthDef = filthDef.defName,
					filthCountRequested = props.filthCount,
					filthEqualize = ZombieSettings.Values.contamination.filthEqualize,
					expectedFilthContamination,
					filths = spawnedFilths.Select(filth => new
					{
						thing = ZombieRuntimeActions.StableThingId(filth),
						cell = filth.Spawned ? ZombieRuntimeActions.DescribeCell(filth.Position) : null,
						contamination = filth.GetContamination()
					}).ToArray(),
					filthCount = spawnedFilths.Count,
					filthsContaminated
				};
			}
			finally
			{
				foreach (var filth in spawnedFilths.Where(filth => filth is { Destroyed: false, Spawned: true }).ToArray())
				{
					filth.ClearContamination();
					filth.Destroy();
				}
				pawn?.ClearContamination();
				if (pawn is { Destroyed: false, Spawned: true })
					pawn.Destroy();
			}
		}

		static object VerifyFlameDamageAshTransfer(Map map, IntVec3 root)
		{
			var patchTargets = PatchedMethodsForPatchClass("DamageWorker_Flame_Apply_Patch");
			var thumperDef = CustomDefs.Thumper;
			if (thumperDef == null)
			{
				return new
				{
					success = false,
					targets = CompactPatchTargets(patchTargets),
					error = "CustomDefs.Thumper was unavailable for the flame-ash probe."
				};
			}
			if (TryFindClearBuildingFootprint(map, thumperDef, root, 18f, out var thumperCell, out var thumperCellError) == false)
				return thumperCellError;

			var thumper = ThingMaker.MakeThing(thumperDef);
			var ashFilths = new List<Filth>();
			try
			{
				foreach (var cell in GenRadial.RadialCellsAround(thumperCell, 5f, true).Where(cell => cell.InBounds(map)))
					ClearFilthAt(map, cell);

				thumper.SetFactionDirect(Faction.OfPlayer);
				GenSpawn.Spawn(thumper, thumperCell, map, Rot4.North, WipeMode.Vanish, false);
				const float thumperContamination = 0.73f;
				thumper.SetContamination(thumperContamination);
				var thumperBefore = thumper.GetContamination();
				var hitPointsBefore = thumper.HitPoints;
				const float damageAmount = 5000f;
				var damageInfo = new DamageInfo(DamageDefOf.Flame, damageAmount, 0f, -1f, null, null, null, DamageInfo.SourceCategory.ThingOrUnknown, thumper, true, true);
				DamageWorker.DamageResult damageResult;
				Rand.PushState(11);
				try
				{
					damageResult = DamageDefOf.Flame.Worker.Apply(damageInfo, thumper);
				}
				finally
				{
					Rand.PopState();
				}

				ashFilths = GenRadial.RadialCellsAround(thumperCell, 5f, true)
					.Where(cell => cell.InBounds(map))
					.SelectMany(cell => cell.GetThingList(map).OfType<Filth>())
					.Where(filth => filth.def == ThingDefOf.Filth_Ash)
					.OrderByDescending(filth => filth.GetContamination())
					.ToList();
				var contaminations = ashFilths.Select(filth => filth.GetContamination()).ToArray();
				var expectedAshContamination = thumperBefore * ZombieSettings.Values.contamination.filthEqualize;
				var ashContaminated = ashFilths.Count > 0
					&& contaminations.All(value => Approximately(value, expectedAshContamination, 0.0001f));

				return new
				{
					success = patchTargets.Length > 0
						&& thumper.Destroyed
						&& damageResult.totalDamageDealt > 0f
						&& ashContaminated,
					targets = CompactPatchTargets(patchTargets),
					thumper = DescribeContaminationThing(thumper),
					thumperCell = ZombieRuntimeActions.DescribeCell(thumperCell),
					thumperBefore,
					hitPointsBefore,
					hitPointsAfter = thumper.HitPoints,
					thumperDestroyed = thumper.Destroyed,
					damageDef = damageInfo.Def?.defName,
					damageAmount,
					damageDealt = damageResult.totalDamageDealt,
					filthDef = ThingDefOf.Filth_Ash.defName,
					filthEqualize = ZombieSettings.Values.contamination.filthEqualize,
					expectedAshContamination,
					ashFilths = ashFilths.Select(filth => new
					{
						thing = ZombieRuntimeActions.StableThingId(filth),
						cell = filth.Spawned ? ZombieRuntimeActions.DescribeCell(filth.Position) : null,
						contamination = filth.GetContamination()
					}).ToArray(),
					ashFilthCount = ashFilths.Count,
					ashContaminated
				};
			}
			finally
			{
				foreach (var filth in ashFilths.Where(filth => filth is { Destroyed: false, Spawned: true }).ToArray())
				{
					filth.ClearContamination();
					filth.Destroy();
				}
				thumper?.ClearContamination();
				if (thumper is { Destroyed: false, Spawned: true })
					thumper.Destroy();
			}
		}

		static object VerifyBirthFilthTransfer(Map map, IntVec3 root)
		{
			var patchTargets = PatchedMethodsForPatchClass("PregnancyUtility_SpawnBirthFilth_Patch");
			var spawnBirthFilth = typeof(PregnancyUtility).GetMethod("SpawnBirthFilth", BindingFlags.Static | BindingFlags.NonPublic);
			if (spawnBirthFilth == null)
			{
				return new
				{
					success = false,
					targets = CompactPatchTargets(patchTargets),
					error = "PregnancyUtility.SpawnBirthFilth private helper was not found."
				};
			}
			if (TryFindClearSpawnCell(map, root, 16f, out var motherCell, out var motherCellError) == false)
				return motherCellError;

			var mother = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var birthFilths = new List<Filth>();
			try
			{
				foreach (var cell in GenRadial.RadialCellsAround(motherCell, 4f, true).Where(cell => cell.InBounds(map)))
					ClearFilthAt(map, cell);
				GenSpawn.Spawn(mother, motherCell, map, Rot4.South);
				DisablePawnWork(mother);
				mother.ClearContamination();

				const float motherContamination = 0.52f;
				mother.SetContamination(motherContamination);
				var motherBefore = mother.GetContamination();
				const int radius = 2;
				Rand.PushState(7);
				try
				{
					spawnBirthFilth.Invoke(null, new object[] { mother, motherCell, ThingDefOf.Filth_AmnioticFluid, radius });
				}
				finally
				{
					Rand.PopState();
				}

				birthFilths = GenRadial.RadialCellsAround(motherCell, radius + 2f, true)
					.Where(cell => cell.InBounds(map))
					.SelectMany(cell => cell.GetThingList(map).OfType<Filth>())
					.Where(filth => filth.def == ThingDefOf.Filth_AmnioticFluid)
					.OrderByDescending(filth => filth.GetContamination())
					.ToList();
				var contaminations = birthFilths.Select(filth => filth.GetContamination()).ToArray();
				var expectedFilthContamination = motherBefore * ZombieSettings.Values.contamination.filthEqualize;
				var filthsContaminated = birthFilths.Count > 0
					&& contaminations.All(value => Approximately(value, expectedFilthContamination, 0.0001f));

				return new
				{
					success = patchTargets.Length > 0
						&& filthsContaminated,
					targets = CompactPatchTargets(patchTargets),
					method = $"{spawnBirthFilth.DeclaringType?.FullName}::{spawnBirthFilth}",
					mother = DescribePawn(mother),
					motherCell = ZombieRuntimeActions.DescribeCell(motherCell),
					motherBefore,
					filthDef = ThingDefOf.Filth_AmnioticFluid.defName,
					filthEqualize = ZombieSettings.Values.contamination.filthEqualize,
					expectedFilthContamination,
					radius,
					filths = birthFilths.Select(filth => new
					{
						thing = ZombieRuntimeActions.StableThingId(filth),
						cell = filth.Spawned ? ZombieRuntimeActions.DescribeCell(filth.Position) : null,
						contamination = filth.GetContamination()
					}).ToArray(),
					filthCount = birthFilths.Count,
					filthsContaminated
				};
			}
			finally
			{
				foreach (var filth in birthFilths.Where(filth => filth is { Destroyed: false, Spawned: true }).ToArray())
				{
					filth.ClearContamination();
					filth.Destroy();
				}
				mother?.ClearContamination();
				if (mother is { Destroyed: false, Spawned: true })
					mother.Destroy();
			}
		}

		static object VerifyThingTakeDamageFilthTransfer(Map map, IntVec3 root)
		{
			var patchTargets = PatchedMethodsForPatchClass("Thing_TakeDamage_Patch");
			var thumperDef = CustomDefs.Thumper;
			if (thumperDef == null)
			{
				return new
				{
					success = false,
					targets = CompactPatchTargets(patchTargets),
					error = "CustomDefs.Thumper was unavailable for the damage-filth probe."
				};
			}
			if (thumperDef.useHitPoints == false || thumperDef.filthLeaving == null)
			{
				return new
				{
					success = false,
					targets = CompactPatchTargets(patchTargets),
					def = thumperDef.defName,
					useHitPoints = thumperDef.useHitPoints,
					filthLeaving = thumperDef.filthLeaving?.defName,
					error = "The selected damage-filth probe def does not satisfy GenLeaving.DropFilthDueToDamage prerequisites."
				};
			}

			if (TryFindClearBuildingFootprint(map, thumperDef, root, 18f, out var thumperCell, out var thumperCellError) == false)
				return thumperCellError;

			var thumper = ThingMaker.MakeThing(thumperDef);
			var spawnedFilths = new List<Filth>();
			try
			{
				foreach (var cell in GenRadial.RadialCellsAround(thumperCell, 5f, true).Where(cell => cell.InBounds(map)))
					ClearFilthAt(map, cell);

				thumper.SetFactionDirect(Faction.OfPlayer);
				GenSpawn.Spawn(thumper, thumperCell, map, Rot4.North, WipeMode.Vanish, false);
				const float thumperContamination = 0.64f;
				thumper.SetContamination(thumperContamination);
				var thumperBefore = thumper.GetContamination();
				var hitPointsBefore = thumper.HitPoints;
				const float damageAmount = 240f;
				var damageInfo = new DamageInfo(DamageDefOf.Crush, damageAmount, 0f, -1f, null, null, null, DamageInfo.SourceCategory.ThingOrUnknown, thumper, true, true);
				DamageWorker.DamageResult damageResult;
				Rand.PushState(5);
				try
				{
					damageResult = thumper.TakeDamage(damageInfo);
				}
				finally
				{
					Rand.PopState();
				}

				var filthDef = thumperDef.filthLeaving;
				spawnedFilths = GenRadial.RadialCellsAround(thumperCell, 5f, true)
					.Where(cell => cell.InBounds(map))
					.SelectMany(cell => cell.GetThingList(map).OfType<Filth>())
					.Where(filth => filth.def == filthDef)
					.OrderByDescending(filth => filth.GetContamination())
					.ToList();
				var filth = spawnedFilths.FirstOrDefault();
				var filthContamination = filth?.GetContamination() ?? -1f;
				var expectedFilthContamination = thumperBefore * ZombieSettings.Values.contamination.filthEqualize;
				var filthProduced = filth != null;
				var filthContaminated = filthProduced
					&& Approximately(filthContamination, expectedFilthContamination, 0.0001f);

				return new
				{
					success = patchTargets.Length > 0
						&& damageResult.totalDamageDealt > 0f
						&& filthContaminated,
					targets = CompactPatchTargets(patchTargets),
					thumper = DescribeContaminationThing(thumper),
					thumperCell = ZombieRuntimeActions.DescribeCell(thumperCell),
					thumperBefore,
					hitPointsBefore,
					hitPointsAfter = thumper.HitPoints,
					damageDef = damageInfo.Def?.defName,
					damageAmount,
					damageDealt = damageResult.totalDamageDealt,
					filthDef = filthDef.defName,
					filthEqualize = ZombieSettings.Values.contamination.filthEqualize,
					expectedFilthContamination,
					filth = ZombieRuntimeActions.StableThingId(filth),
					filthCell = filth?.Spawned == true ? ZombieRuntimeActions.DescribeCell(filth.Position) : null,
					filthContamination,
					filthProduced,
					filthContaminated,
					spawnedFilthCount = spawnedFilths.Count
				};
			}
			finally
			{
				foreach (var filth in spawnedFilths.Where(filth => filth is { Destroyed: false, Spawned: true }).ToArray())
				{
					filth.ClearContamination();
					filth.Destroy();
				}
				thumper?.ClearContamination();
				if (thumper is { Destroyed: false, Spawned: true })
					thumper.Destroy();
			}
		}

		static object VerifyCompSpawnerFilthTransfer(Map map, IntVec3 root)
		{
			var patchTargets = PatchedMethodsForPatchClass("CompSpawnerFilth_TrySpawnFilth_Patch");
			var filthDef = DefDatabase<ThingDef>.GetNamedSilentFail("Filth_Dirt") ?? ThingDefOf.Filth_Blood;
			if (filthDef == null)
			{
				return new
				{
					success = false,
					targets = CompactPatchTargets(patchTargets),
					error = "No filth def was available for the comp-spawner-filth probe."
				};
			}

			if (TryFindClearSpawnCell(map, root, 16f, out var parentCell, out var parentCellError) == false)
				return parentCellError;

			var parent = ThingMaker.MakeThing(ThingDefOf.Steel);
			var spawnedFilths = new List<Filth>();
			try
			{
				foreach (var cell in GenRadial.RadialCellsAround(parentCell, 3f, true).Where(cell => cell.InBounds(map)))
					ClearFilthAt(map, cell);
				parent.stackCount = 1;
				GenSpawn.Spawn(parent, parentCell, map, WipeMode.Vanish);
				const float parentContamination = 0.62f;
				parent.SetContamination(parentContamination);
				var parentBefore = parent.GetContamination();
				var props = new CompProperties_SpawnerFilth
				{
					filthDef = filthDef,
					spawnRadius = 2f,
					spawnCountOnSpawn = 0,
					spawnMtbHours = 0f,
					spawnEveryDays = -1f
				};
				var comp = new CompSpawnerFilth
				{
					parent = parent as ThingWithComps,
					props = props
				};
				if (comp.parent == null)
				{
					return new
					{
						success = false,
						targets = CompactPatchTargets(patchTargets),
						parent = DescribeContaminationThing(parent),
						error = "The selected parent thing is not ThingWithComps and cannot host CompSpawnerFilth."
					};
				}

				Rand.PushState(3);
				try
				{
					comp.TrySpawnFilth();
				}
				finally
				{
					Rand.PopState();
				}

				spawnedFilths = GenRadial.RadialCellsAround(parentCell, 3f, true)
					.Where(cell => cell.InBounds(map))
					.SelectMany(cell => cell.GetThingList(map).OfType<Filth>())
					.Where(filth => filth.def == filthDef)
					.OrderByDescending(filth => filth.GetContamination())
					.ToList();
				var filth = spawnedFilths.FirstOrDefault();
				var filthContamination = filth?.GetContamination() ?? -1f;
				var expectedFilthContamination = parentBefore * ZombieSettings.Values.contamination.filthEqualize;
				var filthProduced = filth != null;
				var filthContaminated = filthProduced
					&& Approximately(filthContamination, expectedFilthContamination, 0.0001f);

				return new
				{
					success = patchTargets.Length > 0
						&& filthContaminated,
					targets = CompactPatchTargets(patchTargets),
					parent = DescribeContaminationThing(parent),
					parentCell = ZombieRuntimeActions.DescribeCell(parentCell),
					parentBefore,
					filthDef = filthDef.defName,
					filthEqualize = ZombieSettings.Values.contamination.filthEqualize,
					expectedFilthContamination,
					spawnRadius = props.spawnRadius,
					filth = ZombieRuntimeActions.StableThingId(filth),
					filthCell = filth?.Spawned == true ? ZombieRuntimeActions.DescribeCell(filth.Position) : null,
					filthContamination,
					filthProduced,
					filthContaminated,
					spawnedFilthCount = spawnedFilths.Count
				};
			}
			finally
			{
				foreach (var filth in spawnedFilths.Where(filth => filth is { Destroyed: false, Spawned: true }).ToArray())
				{
					filth.ClearContamination();
					filth.Destroy();
				}
				parent?.ClearContamination();
				if (parent is { Destroyed: false, Spawned: true })
					parent.Destroy();
			}
		}

		static object VerifyVomitJobFilthTransfer(Map map, IntVec3 root)
		{
			var patchTargets = PatchedMethodsForPatchClass("JobDriver_Vomit_MakeNewToils_Patch");
			if (TryFindClearSpawnCell(map, root, 16f, out var pawnCell, out var pawnCellError) == false)
				return pawnCellError;

			var pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var vomitFilths = new List<Filth>();
			try
			{
				foreach (var cell in GenRadial.RadialCellsAround(pawnCell, 2f, true).Where(cell => cell.InBounds(map)))
					ClearFilthAt(map, cell);
				GenSpawn.Spawn(pawn, pawnCell, map, Rot4.South);
				DisablePawnWork(pawn);
				pawn.ClearContamination();
				pawn.jobs?.StopAll(false, true);
				pawn.pather?.StopDead();

				const float pawnContamination = 0.58f;
				pawn.SetContamination(pawnContamination);
				var pawnBefore = pawn.GetContamination();
				var foodNeedBefore = pawn.needs?.food?.CurLevel ?? -1f;
				var job = JobMaker.MakeJob(JobDefOf.Vomit);
				pawn.jobs.StartJob(job, JobCondition.InterruptForced, null, false, true);
				var targetCell = pawn.CurJob?.targetA.Cell ?? IntVec3.Invalid;
				if (targetCell.IsValid)
					ClearFilthAt(map, targetCell);

				var ticksRun = 0;
				const int maxTicks = 180;
				while (ticksRun < maxTicks)
				{
					AdvanceGameTicks(1);
					ticksRun++;
					var cells = targetCell.IsValid
						? new[] { targetCell }
						: GenRadial.RadialCellsAround(pawnCell, 2f, true).Where(cell => cell.InBounds(map)).ToArray();
					vomitFilths = cells
						.SelectMany(cell => cell.GetThingList(map).OfType<Filth>())
						.Where(filth => filth.def == ThingDefOf.Filth_Vomit)
						.OrderByDescending(filth => filth.GetContamination())
						.ToList();
					if (vomitFilths.Count > 0)
						break;
				}

				var vomit = vomitFilths.FirstOrDefault();
				var vomitContamination = vomit?.GetContamination() ?? -1f;
				var expectedVomitContamination = pawnBefore * ZombieSettings.Values.contamination.filthEqualize;
				var foodNeedAfter = pawn.needs?.food?.CurLevel ?? -1f;
				var targetValid = targetCell.IsValid && targetCell.InBounds(map);
				var vomitProduced = vomit != null;
				var vomitContaminated = vomitProduced
					&& Approximately(vomitContamination, expectedVomitContamination, 0.0001f);

				return new
				{
					success = patchTargets.Length > 0
						&& targetValid
						&& vomitContaminated,
					targets = CompactPatchTargets(patchTargets),
					pawn = DescribePawn(pawn),
					pawnCell = ZombieRuntimeActions.DescribeCell(pawnCell),
					targetCell = targetValid ? ZombieRuntimeActions.DescribeCell(targetCell) : null,
					pawnBefore,
					filthEqualize = ZombieSettings.Values.contamination.filthEqualize,
					expectedVomitContamination,
					vomitFilth = ZombieRuntimeActions.StableThingId(vomit),
					vomitContamination,
					vomitProduced,
					vomitContaminated,
					ticksRun,
					currentJob = pawn.CurJobDef?.defName,
					foodNeedBefore,
					foodNeedAfter
				};
			}
			finally
			{
				foreach (var filth in vomitFilths.Where(filth => filth is { Destroyed: false, Spawned: true }).ToArray())
				{
					filth.ClearContamination();
					filth.Destroy();
				}
				pawn?.ClearContamination();
				if (pawn is { Destroyed: false, Spawned: true })
					pawn.Destroy();
			}
		}

		static object VerifyCorpseButcherProductsTransfer(Map map, IntVec3 root)
		{
			var patchTargets = PatchedMethodsForPatchClass("Corpse_ButcherProducts_Patch");
			var animalKind = DefDatabase<PawnKindDef>.AllDefs
				.Where(kind => kind?.race?.race?.Animal == true)
				.Where(kind => kind.race.race.meatDef != null && kind.race.race.leatherDef != null)
				.OrderBy(kind => kind.defName == "Alpaca" ? 0 : 1)
				.ThenBy(kind => kind.defName)
				.FirstOrDefault();
			if (animalKind == null)
			{
				return new
				{
					success = false,
					targets = CompactPatchTargets(patchTargets),
					error = "No animal PawnKindDef with meat and leather butcher products is available."
				};
			}

			if (TryFindClearSpawnCell(map, root, 16f, out var butcherCell, out var butcherCellError) == false)
				return butcherCellError;
			if (TryFindClearSpawnCell(map, butcherCell + new IntVec3(3, 0, 0), 8f, out var animalCell, out var animalCellError) == false)
				return animalCellError;

			var butcher = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var animal = PawnGenerator.GeneratePawn(animalKind, Faction.OfPlayer);
			Corpse corpse = null;
			Thing[] products = Array.Empty<Thing>();
			Filth bloodFilth = null;
			try
			{
				ClearFilthAt(map, butcherCell);
				GenSpawn.Spawn(butcher, butcherCell, map, Rot4.South);
				GenSpawn.Spawn(animal, animalCell, map, Rot4.South);
				DisablePawnWork(butcher);
				DisablePawnWork(animal);
				butcher.ClearContamination();
				animal.ClearContamination();

				const float animalContamination = 0.66f;
				animal.SetContamination(animalContamination);
				var animalBeforeDeath = animal.GetContamination();
				if (ZombieRuntimeActions.KillPawnToCorpse(animal, out corpse, out var corpseError) == false)
				{
					return new
					{
						success = false,
						targets = CompactPatchTargets(patchTargets),
						animalKind = animalKind.defName,
						animal = DescribePawn(animal),
						error = corpseError
					};
				}

				var corpseBefore = corpse.GetContamination();
				var filthEqualize = ZombieSettings.Values.contamination.filthEqualize;
				var expectedProductContamination = corpseBefore * filthEqualize;
				Rand.PushState(11);
				try
				{
					products = corpse.ButcherProducts(butcher, 1f).Where(thing => thing != null).ToArray();
				}
				finally
				{
					Rand.PopState();
				}

				bloodFilth = butcherCell.GetThingList(map)
					.OfType<Filth>()
					.OrderByDescending(filth => filth.GetContamination())
					.FirstOrDefault();
				var productContaminations = products.Select(thing => thing.GetContamination()).ToArray();
				var firstProductContamination = productContaminations.Length == 0 ? -1f : productContaminations[0];
				var bloodContamination = bloodFilth?.GetContamination() ?? -1f;
				var productsContaminated = products.Length > 0
					&& productContaminations.All(value => Approximately(value, expectedProductContamination, 0.0001f));
				var bloodContaminated = bloodFilth != null
					&& Approximately(bloodContamination, expectedProductContamination, 0.0001f);
				var corpseInherited = Approximately(animalBeforeDeath, animalContamination, 0.0001f)
					&& Approximately(corpseBefore, animalContamination, 0.0001f);

				return new
				{
					success = patchTargets.Length > 0
						&& corpseInherited
						&& productsContaminated
						&& bloodContaminated,
					targets = CompactPatchTargets(patchTargets),
					animalKind = animalKind.defName,
					animal = DescribePawn(animal),
					butcher = DescribePawn(butcher),
					butcherCell = ZombieRuntimeActions.DescribeCell(butcherCell),
					animalCell = ZombieRuntimeActions.DescribeCell(animalCell),
					corpse = DescribeCorpse(corpse),
					animalBeforeDeath,
					corpseBefore,
					filthEqualize,
					expectedProductContamination,
					productCount = products.Length,
					firstProductContamination,
					products = products.Select(DescribeContaminationThing).ToArray(),
					productContaminations,
					bloodFilth = ZombieRuntimeActions.StableThingId(bloodFilth),
					bloodDef = bloodFilth?.def?.defName,
					bloodContamination,
					corpseInherited,
					productsContaminated,
					bloodContaminated
				};
			}
			finally
			{
				foreach (var product in products)
					product?.ClearContamination();
				bloodFilth?.ClearContamination();
				if (bloodFilth is { Destroyed: false, Spawned: true })
					bloodFilth.Destroy();
				corpse?.ClearContamination();
				animal?.ClearContamination();
				butcher?.ClearContamination();
				if (corpse is { Destroyed: false, Spawned: true })
					corpse.Destroy();
				if (animal is { Destroyed: false, Spawned: true })
					animal.Destroy();
				if (butcher is { Destroyed: false, Spawned: true })
					butcher.Destroy();
			}
		}

		[Tool("zombieland/contamination_building_install_contract", Description = "Verify contamination follows minified buildings through install and reinstall blueprints into the final building.")]
		public static object ContaminationBuildingInstallContract(
			[ToolParameter(Description = "Destroy generated fixture buildings before returning.", Required = false, DefaultValue = true)] bool cleanup = true)
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var buildingDef = DefDatabase<ThingDef>.GetNamed("Stool", false);
			var stuffDef = ThingDefOf.WoodLog;
			if (buildingDef?.Minifiable != true || buildingDef.installBlueprintDef == null || buildingDef.minifiedDef == null || stuffDef == null)
			{
				return new
				{
					success = false,
					error = "The Stool building def is not available as a minifiable installable fixture."
				};
			}

			var reservedCells = new HashSet<IntVec3>();

			bool TryFindBuildingCell(IntVec3 root, float radius, out IntVec3 cell, out object error)
			{
				cell = IntVec3.Invalid;
				error = null;
				foreach (var candidate in GenRadial.RadialCellsAround(root, radius, true))
				{
					if (reservedCells.Contains(candidate))
						continue;
					if (candidate.InBounds(map) == false)
						continue;
					if (candidate.Standable(map) == false)
						continue;
					if (candidate.Fogged(map))
						continue;
					if (candidate.GetEdifice(map) != null)
						continue;
					if (candidate.GetThingList(map).Any(thing =>
						thing is Pawn
						|| thing is Blueprint
						|| thing.def.category == ThingCategory.Plant
						|| thing.def.category == ThingCategory.Building
						|| thing.def.passability == Traversability.Impassable))
						continue;

					cell = candidate;
					reservedCells.Add(candidate);
					return true;
				}

				error = new
				{
					success = false,
					error = $"No clear 1x1 building fixture cell was found near ({root.x}, {root.z})."
				};
				return false;
			}

			void DestroySpawned(Thing thing)
			{
				if (thing is { Destroyed: false, Spawned: true })
					thing.Destroy();
			}

			static object DescribeBlueprint(Blueprint blueprint)
				=> blueprint == null
					? null
					: new
					{
						id = ZombieRuntimeActions.StableThingId(blueprint),
						spawned = blueprint.Spawned,
						destroyed = blueprint.Destroyed,
						mapIndex = blueprint.Map?.Index,
						position = ZombieRuntimeActions.DescribeCell(blueprint.Position),
						contamination = blueprint.GetContamination()
					};

			static bool TryReplaceBlueprint(Blueprint blueprint, Pawn worker, out Thing createdThing, out bool jobEnded, out string error)
			{
				createdThing = null;
				jobEnded = false;
				error = null;
				try
				{
					return blueprint?.TryReplaceWithSolidThing(worker, out createdThing, out jobEnded) ?? false;
				}
				catch (Exception ex)
				{
					error = ex.GetType().Name + ": " + ex.Message;
					return false;
				}
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var workerCell, out var workerSpawnError) == false)
				return workerSpawnError;
			if (TryFindBuildingCell(workerCell + new IntVec3(3, 0, 0), 16f, out var sourceCell, out var sourceCellError) == false)
				return sourceCellError;
			if (TryFindBuildingCell(sourceCell + new IntVec3(3, 0, 0), 16f, out var installCell, out var installCellError) == false)
				return installCellError;
			if (TryFindBuildingCell(installCell + new IntVec3(3, 0, 0), 16f, out var reinstallSourceCell, out var reinstallSourceError) == false)
				return reinstallSourceError;
			if (TryFindBuildingCell(reinstallSourceCell + new IntVec3(3, 0, 0), 16f, out var reinstallDestCell, out var reinstallDestError) == false)
				return reinstallDestError;

			var worker = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(worker, workerCell, map, Rot4.South);
			DisablePawnWork(worker);

			var sourceBuilding = GenSpawn.Spawn(ThingMaker.MakeThing(buildingDef, stuffDef), sourceCell, map, Rot4.South) as Building;
			var reinstallBuilding = GenSpawn.Spawn(ThingMaker.MakeThing(buildingDef, stuffDef), reinstallSourceCell, map, Rot4.South) as Building;
			if (sourceBuilding == null || reinstallBuilding == null)
			{
				DestroySpawned(worker);
				DestroySpawned(sourceBuilding);
				DestroySpawned(reinstallBuilding);
				return new
				{
					success = false,
					error = "Could not spawn Stool building fixtures."
				};
			}

			const float installInputContamination = 0.6f;
			const float reinstallInputContamination = 0.45f;
			sourceBuilding.SetContamination(installInputContamination);
			reinstallBuilding.SetContamination(reinstallInputContamination);

			var sourceBeforeMinify = sourceBuilding.GetContamination();
			var minified = MinifyUtility.MakeMinified(sourceBuilding);
			var sourceAfterMinify = sourceBuilding.GetContamination();
			var minifiedAfterMinify = minified?.GetContamination() ?? -1f;
			var minifiedSpawnedForInstall = false;
			if (minified != null && minified.Spawned == false)
			{
				GenSpawn.Spawn(minified, sourceCell, map, Rot4.South, WipeMode.Vanish, false);
				minifiedSpawnedForInstall = minified.Spawned;
			}
			var minifiedAfterSpawn = minified?.GetContamination() ?? -1f;
			var installBlueprint = minified == null ? null : GenConstruct.PlaceBlueprintForInstall(minified, installCell, map, Rot4.South, Faction.OfPlayer, false);
			var minifiedAfterBlueprint = minified?.GetContamination() ?? -1f;
			var installBlueprintAfterPlace = installBlueprint?.GetContamination() ?? -1f;
			var installBlueprintStateAfterPlace = DescribeBlueprint(installBlueprint);
			var installInnerForcedUnspawned = false;
			if (minified?.InnerThing != null)
			{
				minified.InnerThing.ForceSetStateToUnspawned();
				installInnerForcedUnspawned = minified.InnerThing.Spawned == false;
			}
			var installReplaced = TryReplaceBlueprint(installBlueprint, worker, out var installedThing, out var installJobEnded, out var installReplaceError);
			var installBlueprintAfterReplace = installBlueprint?.Destroyed == false ? installBlueprint.GetContamination() : 0f;
			var installedContamination = installedThing?.GetContamination() ?? -1f;

			var reinstallSourceBefore = reinstallBuilding.GetContamination();
			var reinstallBlueprint = GenConstruct.PlaceBlueprintForReinstall(reinstallBuilding, reinstallDestCell, map, Rot4.South, Faction.OfPlayer, false);
			var reinstallSourceAfterBlueprint = reinstallBuilding.GetContamination();
			var reinstallBlueprintAfterPlace = reinstallBlueprint?.GetContamination() ?? -1f;
			var reinstallBlueprintStateAfterPlace = DescribeBlueprint(reinstallBlueprint);
			if (reinstallBuilding.Spawned)
				reinstallBuilding.DeSpawn();
			var reinstallReplaced = TryReplaceBlueprint(reinstallBlueprint, worker, out var reinstalledThing, out var reinstallJobEnded, out var reinstallReplaceError);
			var reinstallBlueprintAfterReplace = reinstallBlueprint?.Destroyed == false ? reinstallBlueprint.GetContamination() : 0f;
			var reinstalledContamination = reinstalledThing?.GetContamination() ?? -1f;

			static bool CloseFloat(float value, float expected) => Mathf.Abs(value - expected) < 0.0001f;

			var minifyTransferred = CloseFloat(sourceBeforeMinify, installInputContamination)
				&& CloseFloat(sourceAfterMinify, 0f)
				&& CloseFloat(minifiedAfterMinify, installInputContamination)
				&& minifiedSpawnedForInstall
				&& CloseFloat(minifiedAfterSpawn, installInputContamination);
			var installBlueprintTransferred = installBlueprint != null
				&& CloseFloat(minifiedAfterBlueprint, 0f)
				&& CloseFloat(installBlueprintAfterPlace, installInputContamination);
			var installFinalTransferred = installReplaced
				&& installJobEnded == false
				&& installedThing != null
				&& CloseFloat(installBlueprintAfterReplace, 0f)
				&& CloseFloat(installedContamination, installInputContamination);
			var reinstallBlueprintTransferred = reinstallBlueprint != null
				&& CloseFloat(reinstallSourceBefore, reinstallInputContamination)
				&& CloseFloat(reinstallSourceAfterBlueprint, 0f)
				&& CloseFloat(reinstallBlueprintAfterPlace, reinstallInputContamination);
			var reinstallFinalTransferred = reinstallReplaced
				&& reinstallJobEnded == false
				&& reinstalledThing != null
				&& CloseFloat(reinstallBlueprintAfterReplace, 0f)
				&& CloseFloat(reinstalledContamination, reinstallInputContamination);

			var workerDescription = DescribePawn(worker);

			DestroySpawned(worker);
			if (cleanup)
			{
				DestroySpawned(installedThing);
				DestroySpawned(reinstalledThing);
				DestroySpawned(sourceBuilding);
				DestroySpawned(minified);
				DestroySpawned(reinstallBuilding);
				DestroySpawned(installBlueprint);
				DestroySpawned(reinstallBlueprint);
			}

			return new
			{
				success = minifyTransferred
					&& installBlueprintTransferred
					&& installFinalTransferred
					&& reinstallBlueprintTransferred
					&& reinstallFinalTransferred,
				cleanup,
				worker = workerDescription,
				workerCell = ZombieRuntimeActions.DescribeCell(workerCell),
				sourceCell = ZombieRuntimeActions.DescribeCell(sourceCell),
				installCell = ZombieRuntimeActions.DescribeCell(installCell),
				reinstallSourceCell = ZombieRuntimeActions.DescribeCell(reinstallSourceCell),
				reinstallDestCell = ZombieRuntimeActions.DescribeCell(reinstallDestCell),
				sourceBeforeMinify,
				sourceAfterMinify,
				minifiedAfterMinify,
				minifiedSpawnedForInstall,
				minifiedAfterSpawn,
				minifiedAfterBlueprint,
				installBlueprintAfterPlace,
				installBlueprintStateAfterPlace,
				installInnerForcedUnspawned,
				installReplaced,
				installJobEnded,
				installReplaceError,
				installBlueprintAfterReplace,
				installedThing = ZombieRuntimeActions.StableThingId(installedThing),
				installedState = DescribeContaminationThing(installedThing),
				installedContamination,
				reinstallSourceBefore,
				reinstallSourceAfterBlueprint,
				reinstallBlueprintAfterPlace,
				reinstallBlueprintStateAfterPlace,
				reinstallReplaced,
				reinstallJobEnded,
				reinstallReplaceError,
				reinstallBlueprintAfterReplace,
				reinstalledThing = ZombieRuntimeActions.StableThingId(reinstalledThing),
				reinstalledState = DescribeContaminationThing(reinstalledThing),
				reinstalledContamination,
				minifyTransferred,
				installBlueprintTransferred,
				installFinalTransferred,
				reinstallBlueprintTransferred,
				reinstallFinalTransferred
			};
		}

		[Tool("zombieland/contamination_smooth_wall_contract", Description = "Verify contaminated natural smoothable walls transfer through real SmoothableWallUtility.SmoothWall into the smoothed wall.")]
		public static object ContaminationSmoothWallContract(
			[ToolParameter(Description = "Destroy generated smooth-wall fixtures before returning.", Required = false, DefaultValue = true)] bool cleanup = true)
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var smoothableDef = DefDatabase<ThingDef>.AllDefs
				.Where(def => def.category == ThingCategory.Building && def.building?.smoothedThing != null)
				.OrderBy(def => def.defName)
				.FirstOrDefault();
			if (smoothableDef == null)
			{
				return new
				{
					success = false,
					error = "No loaded smoothable building def with a smoothedThing was found."
				};
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var wallCell, out var wallCellError) == false)
				return wallCellError;
			if (TryFindClearSpawnCell(map, wallCell + new IntVec3(3, 0, 0), 8f, out var workerCell, out var workerCellError) == false)
				return workerCellError;

			var worker = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var naturalWall = (Thing)null;
			var smoothedWall = (Thing)null;
			try
			{
				GenSpawn.Spawn(worker, workerCell, map, Rot4.South);
				DisablePawnWork(worker);
				naturalWall = ThingMaker.MakeThing(smoothableDef);
				GenSpawn.Spawn(naturalWall, wallCell, map, WipeMode.Vanish);

				const float wallContaminationBefore = 0.58f;
				naturalWall.SetContamination(wallContaminationBefore);
				var naturalBefore = naturalWall.GetContamination();
				var groundBefore = map.GetContamination(wallCell);

				smoothedWall = SmoothableWallUtility.SmoothWall(naturalWall, worker);
				var smoothedContamination = smoothedWall?.GetContamination() ?? -1f;
				var groundAfter = map.GetContamination(wallCell);

				static bool CloseFloat(float value, float expected) => Mathf.Abs(value - expected) < 0.0001f;
				var smoothed = naturalWall.Destroyed
					&& smoothedWall != null
					&& smoothedWall.Spawned
					&& smoothedWall.Position == wallCell
					&& smoothedWall.def == smoothableDef.building.smoothedThing;
				var contaminationTransferred = smoothed
					&& CloseFloat(naturalBefore, wallContaminationBefore)
					&& CloseFloat(smoothedContamination, wallContaminationBefore);
				var reversionResult = ContaminationSmoothWallReversionContract(cleanup);
				var reversionSuccessProperty = reversionResult?.GetType().GetProperty("success");
				var reversionSucceeded = reversionSuccessProperty?.GetValue(reversionResult) is bool reversionSuccess && reversionSuccess;

				return new
				{
					success = smoothed && contaminationTransferred && reversionSucceeded,
					cleanup,
					worker = DescribePawn(worker),
					workerCell = ZombieRuntimeActions.DescribeCell(workerCell),
					wallCell = ZombieRuntimeActions.DescribeCell(wallCell),
					smoothableDef = smoothableDef.defName,
					smoothedDef = smoothableDef.building.smoothedThing.defName,
					naturalWall = ZombieRuntimeActions.StableThingId(naturalWall),
					naturalDestroyed = naturalWall.Destroyed,
					smoothedWall = ZombieRuntimeActions.StableThingId(smoothedWall),
					smoothedWallDef = smoothedWall?.def?.defName,
					smoothedWallSpawned = smoothedWall?.Spawned,
					naturalBefore,
					groundBefore,
					smoothedContamination,
					groundAfter,
					expectedSmoothedContamination = wallContaminationBefore,
					smoothed,
					contaminationTransferred,
					reversionSucceeded,
					reversion = reversionResult
				};
			}
			finally
			{
				if (cleanup)
					smoothedWall?.ClearContamination();
				if (cleanup && smoothedWall is { Destroyed: false, Spawned: true })
					smoothedWall.Destroy(DestroyMode.Vanish);
				if (naturalWall is { Destroyed: false, Spawned: true })
					naturalWall.Destroy(DestroyMode.Vanish);
				if (cleanup && wallCell.IsValid && wallCell.InBounds(map))
					map.SetContamination(wallCell, 0f);
				if (worker is { Destroyed: false, Spawned: true })
					worker.Destroy();
			}
		}

		[Tool("zombieland/contamination_smooth_wall_reversion_contract", Description = "Verify real SmoothableWallUtility.Notify_BuildingDestroying preserves contamination from the smoothed wall that reverts.")]
		public static object ContaminationSmoothWallReversionContract(
			[ToolParameter(Description = "Destroy generated reversion fixtures before returning.", Required = false, DefaultValue = true)] bool cleanup = true)
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var smoothableDef = DefDatabase<ThingDef>.AllDefs
				.Where(def => def.category == ThingCategory.Building && def.building?.smoothedThing != null)
				.OrderBy(def => def.defName)
				.FirstOrDefault();
			var smoothedDef = smoothableDef?.building?.smoothedThing;
			if (smoothableDef == null || smoothedDef?.building?.unsmoothedThing == null)
			{
				return new
				{
					success = false,
					error = "No loaded smoothable/smoothed building def pair was found."
				};
			}

			bool TryFindFixtureCells(IntVec3 root, float radius, out IntVec3 center, out object error)
			{
				center = IntVec3.Invalid;
				error = null;
				foreach (var candidate in GenRadial.RadialCellsAround(root, radius, true))
				{
					if (candidate.InBounds(map) == false)
						continue;
					var cells = GenAdj.CardinalDirections.Select(direction => candidate + direction).Append(candidate).ToArray();
					if (cells.All(cell =>
						cell.InBounds(map)
						&& cell.Fogged(map) == false
						&& cell.GetEdifice(map) == null
						&& cell.GetThingList(map).Any(thing => thing is Pawn || thing is Blueprint || thing is Frame || thing.def.category == ThingCategory.Building) == false))
					{
						center = candidate;
						return true;
					}
				}
				error = new
				{
					success = false,
					error = $"No clear five-cell smooth-wall reversion fixture was found near ({root.x}, {root.z})."
				};
				return false;
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindFixtureCells(root, 20f, out var centerCell, out var fixtureError) == false)
				return fixtureError;

			var destroyingCell = centerCell + IntVec3.West;
			var blockerCells = new[] { centerCell + IntVec3.North, centerCell + IntVec3.East, centerCell + IntVec3.South };
			var spawned = new List<Thing>();
			Thing destroyingWall = null;
			Thing revertedWall = null;
			try
			{
				destroyingWall = GenSpawn.Spawn(ThingMaker.MakeThing(smoothedDef), destroyingCell, map, WipeMode.Vanish);
				spawned.Add(destroyingWall);
				var targetWall = GenSpawn.Spawn(ThingMaker.MakeThing(smoothedDef), centerCell, map, WipeMode.Vanish);
				spawned.Add(targetWall);
				foreach (var blockerCell in blockerCells)
				{
					var blocker = GenSpawn.Spawn(ThingMaker.MakeThing(smoothableDef), blockerCell, map, WipeMode.Vanish);
					spawned.Add(blocker);
				}

				const float destroyingContaminationBefore = 0.18f;
				const float targetContaminationBefore = 0.63f;
				destroyingWall.SetContamination(destroyingContaminationBefore);
				targetWall.SetContamination(targetContaminationBefore);
				var destroyingBefore = destroyingWall.GetContamination();
				var targetBefore = targetWall.GetContamination();

				SmoothableWallUtility.Notify_BuildingDestroying(destroyingWall, DestroyMode.Deconstruct);
				revertedWall = centerCell.GetEdifice(map);
				var revertedContamination = revertedWall?.GetContamination() ?? -1f;
				var destroyingAfter = destroyingWall.GetContamination();

				static bool CloseFloat(float value, float expected) => Mathf.Abs(value - expected) < 0.0001f;
				var reverted = targetWall.Destroyed
					&& revertedWall != null
					&& revertedWall.Spawned
					&& revertedWall.Position == centerCell
					&& revertedWall.def == smoothableDef;
				var contaminationTransferred = reverted
					&& CloseFloat(destroyingBefore, destroyingContaminationBefore)
					&& CloseFloat(targetBefore, targetContaminationBefore)
					&& CloseFloat(revertedContamination, targetContaminationBefore)
					&& CloseFloat(destroyingAfter, destroyingContaminationBefore);

				return new
				{
					success = reverted && contaminationTransferred,
					cleanup,
					centerCell = ZombieRuntimeActions.DescribeCell(centerCell),
					destroyingCell = ZombieRuntimeActions.DescribeCell(destroyingCell),
					blockerCells = blockerCells.Select(ZombieRuntimeActions.DescribeCell).ToArray(),
					smoothableDef = smoothableDef.defName,
					smoothedDef = smoothedDef.defName,
					destroyingWall = ZombieRuntimeActions.StableThingId(destroyingWall),
					targetWall = ZombieRuntimeActions.StableThingId(targetWall),
					targetDestroyed = targetWall.Destroyed,
					revertedWall = ZombieRuntimeActions.StableThingId(revertedWall),
					revertedWallDef = revertedWall?.def?.defName,
					revertedWallSpawned = revertedWall?.Spawned,
					destroyingBefore,
					destroyingAfter,
					targetBefore,
					revertedContamination,
					expectedRevertedContamination = targetContaminationBefore,
					reverted,
					contaminationTransferred
				};
			}
			finally
			{
				if (cleanup)
				{
					foreach (var thing in spawned.Append(revertedWall).Where(thing => thing != null).Distinct().ToArray())
					{
						thing.ClearContamination();
						if (thing is { Destroyed: false, Spawned: true })
							thing.Destroy(DestroyMode.Vanish);
					}
					foreach (var cell in blockerCells.Append(centerCell).Append(destroyingCell))
						if (cell.InBounds(map))
							map.SetContamination(cell, 0f);
				}
			}
		}

		[Tool("zombieland/contamination_rest_comfort_contract", Description = "Verify the real GainComfortFromCellIfPossible cadence equalizes contaminated ground into a resting pawn.")]
		public static object ContaminationRestComfortContract()
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var pawnCell, out var pawnSpawnError) == false)
				return pawnSpawnError;

			var pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var oldGroundContamination = map.GetContamination(pawnCell);
			try
			{
				GenSpawn.Spawn(pawn, pawnCell, map, Rot4.South);
				DisablePawnWork(pawn);
				pawn.needs?.AddOrRemoveNeedsAsAppropriate();
				pawn.ClearContamination();
				pawn.jobs?.StopAll(false, true);
				pawn.pather?.StopDead();
				if (pawn.drafter != null)
					pawn.drafter.Drafted = true;
				var waitJob = JobMaker.MakeJob(JobDefOf.Wait_Combat);
				waitJob.playerForced = true;
				pawn.jobs.StartJob(waitJob, JobCondition.InterruptForced, null, false, true);

				const float groundContaminationBefore = 0.8f;
				map.SetContamination(pawnCell, groundContaminationBefore);
				var pawnBefore = pawn.GetContamination();
				var groundBefore = map.GetContamination(pawnCell);
				var restEqualize = ZombieSettings.Values.contamination.restEqualize;
				var expectedPawnAfter = groundBefore * restEqualize;
				var expectedGroundAfter = groundBefore * (1f - restEqualize);
				var tickStart = Find.TickManager.TicksGame;
				var cadenceTick = pawn.thingIDNumber % 1000;
				var ticksToCadence = (cadenceTick - tickStart % 1000 + 1000) % 1000;
				var tickHit = -1;
				for (var tick = 0; tick <= ticksToCadence; tick++)
				{
					PawnUtility.GainComfortFromCellIfPossible(pawn, 1, false);
					if (pawn.GetContamination() > pawnBefore)
					{
						tickHit = tick;
						break;
					}
					AdvanceGameTicks(1);
				}

				var pawnAfter = pawn.GetContamination();
				var groundAfter = map.GetContamination(pawnCell);
				static bool CloseFloat(float value, float expected) => Mathf.Abs(value - expected) < 0.0001f;
				var cadenceReached = tickHit >= 0 && tickHit <= 1000;
				var equalized = cadenceReached
					&& CloseFloat(pawnBefore, 0f)
					&& CloseFloat(groundBefore, groundContaminationBefore)
					&& CloseFloat(pawnAfter, expectedPawnAfter)
					&& CloseFloat(groundAfter, expectedGroundAfter);

				return new
				{
					success = equalized,
					pawn = DescribePawn(pawn),
					pawnCell = ZombieRuntimeActions.DescribeCell(pawnCell),
					pawnPositionAfter = ZombieRuntimeActions.DescribeCell(pawn.Position),
					startedJob = pawn.CurJobDef?.defName,
					tickStart,
					cadenceTick,
					ticksToCadence,
					tickHit,
					restEqualize,
					pawnBefore,
					pawnAfter,
					expectedPawnAfter,
					groundBefore,
					groundAfter,
					expectedGroundAfter,
					cadenceReached,
					equalized
				};
			}
			finally
			{
				pawn?.ClearContamination();
				if (pawn is { Destroyed: false, Spawned: true })
					pawn.Destroy();
				if (pawnCell.IsValid && pawnCell.InBounds(map))
					map.SetContamination(pawnCell, oldGroundContamination);
			}
		}

		[Tool("zombieland/contamination_carry_tracker_contract", Description = "Verify carried contaminated items equalize into the carrier and both Pawn_CarryTracker.TryStartCarry overloads preserve contamination.")]
		public static object ContaminationCarryTrackerContract()
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var pawnCell, out var pawnSpawnError) == false)
				return pawnSpawnError;
			if (TryFindClearSpawnCell(map, pawnCell + IntVec3.East, 8f, out var itemCell, out var itemSpawnError) == false)
				return itemSpawnError;

			var pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var item = (Thing)null;
			try
			{
				GenSpawn.Spawn(pawn, pawnCell, map, Rot4.South);
				DisablePawnWork(pawn);
				pawn.ClearContamination();
				pawn.jobs?.StopAll(false, true);
				pawn.pather?.StopDead();
				if (pawn.drafter != null)
					pawn.drafter.Drafted = true;
				var waitJob = JobMaker.MakeJob(JobDefOf.Wait_Combat);
				waitJob.playerForced = true;
				pawn.jobs.StartJob(waitJob, JobCondition.InterruptForced, null, false, true);

				item = ThingMaker.MakeThing(ThingDefOf.Steel);
				item.stackCount = 20;
				GenSpawn.Spawn(item, itemCell, map, WipeMode.Vanish);
				const float itemContaminationBefore = 0.7f;
				item.SetContamination(itemContaminationBefore);
				var itemSpawnedBeforeCarry = item.Spawned;
				item.DeSpawn();
				var startedCarry = pawn.carryTracker.TryStartCarry(item);
				var carriedThing = pawn.carryTracker.CarriedThing;
				var carryingExpectedThing = ReferenceEquals(carriedThing, item);
				var itemSpawnedAfterCarry = item.Spawned;
				if (startedCarry == false || carryingExpectedThing == false)
				{
					return new
					{
						success = false,
						error = "Pawn_CarryTracker.TryStartCarry did not put the contaminated item in the carrier's hands.",
						pawn = DescribePawn(pawn),
						item = ZombieRuntimeActions.StableThingId(item),
						pawnCell = ZombieRuntimeActions.DescribeCell(pawnCell),
						itemCell = ZombieRuntimeActions.DescribeCell(itemCell),
						itemSpawnedBeforeCarry,
						itemSpawnedAfterCarry,
						startedCarry,
						carryingExpectedThing
					};
				}

				var pawnBefore = pawn.GetContamination(false);
				var carriedBefore = carriedThing.GetContamination();
				var carryEqualize = ZombieSettings.Values.contamination.carryEqualize;
				var expectedPawnAfter = carriedBefore * carryEqualize;
				var expectedCarriedAfter = carriedBefore * (1f - carryEqualize);
				var countReserveCarry = VerifyTryStartCarryCountReserveOverload(map, itemCell + new IntVec3(4, 0, 0));
				var tickStart = Find.TickManager.TicksGame;
				var cadenceTick = pawn.thingIDNumber % 900;
				var ticksToCadence = (cadenceTick - tickStart % 900 + 900) % 900;
				if (ticksToCadence > 0)
					AdvanceGameTicks(ticksToCadence);
				var tickAtCall = Find.TickManager.TicksGame;
				var tickAligned = tickAtCall % 900 == cadenceTick;
				var pawnAtCadence = pawn.GetContamination(false);
				var carriedAtCadence = carriedThing.GetContamination();
				var cadenceTickAlreadyRan = pawnAtCadence > pawnBefore;
				if (cadenceTickAlreadyRan == false)
					pawn.carryTracker.CarryHandsTickInterval(1);

				var pawnAfter = pawn.GetContamination(false);
				var carriedAfter = carriedThing.GetContamination();
				var carriedStillHeld = ReferenceEquals(pawn.carryTracker.CarriedThing, item);
				static bool CloseFloat(float value, float expected) => Mathf.Abs(value - expected) < 0.0001f;
				var cadenceReached = tickAligned && pawnAfter > pawnBefore;
				var equalized = cadenceReached
					&& carriedStillHeld
					&& CloseFloat(pawnBefore, 0f)
					&& CloseFloat(carriedBefore, itemContaminationBefore)
					&& CloseFloat(pawnAfter, expectedPawnAfter)
					&& CloseFloat(carriedAfter, expectedCarriedAfter);

				return new
				{
					success = equalized
						&& ObjectSuccess(countReserveCarry),
					pawn = DescribePawn(pawn),
					item = ZombieRuntimeActions.StableThingId(item),
					pawnCell = ZombieRuntimeActions.DescribeCell(pawnCell),
					itemCell = ZombieRuntimeActions.DescribeCell(itemCell),
					itemSpawnedBeforeCarry,
					itemSpawnedAfterCarry,
					startedCarry,
					carryingExpectedThing,
					carriedStillHeld,
					tickStart,
					cadenceTick,
					ticksToCadence,
					tickAtCall,
					tickAligned,
					cadenceTickAlreadyRan,
					carryEqualize,
					pawnBefore,
					pawnAtCadence,
					pawnAfter,
					expectedPawnAfter,
					carriedBefore,
					carriedAtCadence,
					carriedAfter,
					expectedCarriedAfter,
					cadenceReached,
					equalized,
					countReserveCarry
				};
			}
			finally
			{
				pawn?.ClearContamination();
				item?.ClearContamination();
				pawn?.carryTracker?.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
				if (item is { Destroyed: false, Spawned: true })
					item.Destroy();
				if (pawn is { Destroyed: false, Spawned: true })
					pawn.Destroy();
			}
		}

		static object VerifyTryStartCarryCountReserveOverload(Map map, IntVec3 root)
		{
			var patchTargets = PatchedMethodsForPatchClass("Pawn_CarryTracker_TryStartCarry_Patch_Patch");
			if (TryFindClearSpawnCell(map, root, 16f, out var pawnCell, out var pawnCellError) == false)
				return pawnCellError;
			if (TryFindClearSpawnCell(map, pawnCell + IntVec3.East, 8f, out var itemCell, out var itemCellError) == false)
				return itemCellError;

			var pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var item = (Thing)null;
			Thing carriedThing = null;
			try
			{
				GenSpawn.Spawn(pawn, pawnCell, map, Rot4.South);
				DisablePawnWork(pawn);
				pawn.ClearContamination();
				pawn.jobs?.StopAll(false, true);
				pawn.pather?.StopDead();

				item = ThingMaker.MakeThing(ThingDefOf.Steel);
				item.stackCount = 20;
				GenSpawn.Spawn(item, itemCell, map, WipeMode.Vanish);
				const float itemContaminationBefore = 0.7f;
				item.SetContamination(itemContaminationBefore);
				const int requestedCount = 5;
				var stackCountBefore = item.stackCount;
				var itemSpawnedBeforeCarry = item.Spawned;
				var carriedCount = pawn.carryTracker.TryStartCarry(item, requestedCount, reserve: false);
				carriedThing = pawn.carryTracker.CarriedThing;
				var leftoverCount = item.stackCount;
				var carriedContamination = carriedThing?.GetContamination() ?? -1f;
				var leftoverContamination = item.GetContamination();
				var carriedSplitValid = carriedThing != null
					&& ReferenceEquals(carriedThing, item) == false
					&& carriedThing.stackCount == requestedCount
					&& carriedCount == requestedCount
					&& carriedThing.Spawned == false
					&& item.Spawned
					&& leftoverCount == stackCountBefore - requestedCount;
				var contaminationPreserved = Approximately(carriedContamination, itemContaminationBefore, 0.0001f)
					&& Approximately(leftoverContamination, itemContaminationBefore, 0.0001f);

				return new
				{
					success = patchTargets.Length > 0
						&& carriedSplitValid
						&& contaminationPreserved,
					targets = CompactPatchTargets(patchTargets),
					pawn = DescribePawn(pawn),
					pawnCell = ZombieRuntimeActions.DescribeCell(pawnCell),
					item = DescribeContaminationThing(item),
					itemCell = ZombieRuntimeActions.DescribeCell(itemCell),
					carriedThing = DescribeContaminationThing(carriedThing),
					requestedCount,
					carriedCount,
					stackCountBefore,
					leftoverCount,
					itemSpawnedBeforeCarry,
					carriedContamination,
					leftoverContamination,
					expectedContamination = itemContaminationBefore,
					carriedSplitValid,
					contaminationPreserved
				};
			}
			finally
			{
				pawn?.ClearContamination();
				item?.ClearContamination();
				carriedThing?.ClearContamination();
				pawn?.carryTracker?.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out _);
				if (carriedThing is { Destroyed: false, Spawned: true })
					carriedThing.Destroy();
				if (item is { Destroyed: false, Spawned: true })
					item.Destroy();
				if (pawn is { Destroyed: false, Spawned: true })
					pawn.Destroy();
			}
		}

		[Tool("zombieland/contamination_clear_snow_contract", Description = "Verify real cleaning jobs transfer contaminated snow or sand cell ground and filth into the worker when cleared.")]
		public static object ContaminationClearSnowContract(
			[ToolParameter(Description = "Clear mode: snow or sand.", Required = false, DefaultValue = "snow")] string mode = "snow",
			[ToolParameter(Description = "Destroy the worker and restore snow/cell contamination before returning.", Required = false, DefaultValue = true)] bool cleanup = true)
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}
			mode = string.IsNullOrWhiteSpace(mode) ? "snow" : mode.Trim().ToLowerInvariant();
			if (mode != "snow" && mode != "sand")
			{
				return new
				{
					success = false,
					error = "Unsupported clear mode. Use snow or sand."
				};
			}
			var odysseyInstalled = ModLister.OdysseyInstalled;
			var odysseyActive = ModsConfig.OdysseyActive;
			var sandGridPresent = map.sandGrid != null;
			var sandGridAvailable = sandGridPresent && odysseyActive;
			if (mode == "sand" && sandGridAvailable == false)
			{
				return new
				{
					success = true,
					skipped = true,
					mode,
					odysseyInstalled,
					odysseyActive,
					sandGridPresent,
					sandGridAvailable,
					reason = "Sand clearing requires active Odyssey SandGrid support; this load order/save cannot honestly exercise it."
				};
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var snowCell, out var snowCellError) == false)
				return snowCellError;
			if (TryFindClearSpawnCell(map, snowCell + IntVec3.East, 8f, out var workerCell, out var workerCellError) == false)
				return workerCellError;

			var worker = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var oldGroundContamination = map.GetContamination(snowCell);
			var oldSnowDepth = map.snowGrid.GetDepth(snowCell);
			var oldSandDepth = sandGridPresent ? map.sandGrid.GetDepth(snowCell) : 0f;
			var oldTerrain = snowCell.GetTerrain(map);
			try
			{
				GenSpawn.Spawn(worker, workerCell, map, Rot4.South);
				DisablePawnWork(worker);
				worker.ClearContamination();
				worker.jobs?.StopAll(false, true);
				worker.pather?.StopDead();

				const float snowDepthBefore = 1f;
				const float groundContaminationBefore = 0.72f;
				if (mode == "sand")
				{
					var sandHoldingTerrain = TerrainDefOf.Soil ?? DefDatabase<TerrainDef>.GetNamedSilentFail("Soil");
					if (sandHoldingTerrain == null)
					{
						return new
						{
							success = false,
							error = "TerrainDef Soil is unavailable."
						};
					}
					map.terrainGrid.SetTerrain(snowCell, sandHoldingTerrain);
					map.snowGrid.SetDepth(snowCell, 0f);
					map.sandGrid.SetDepth(snowCell, snowDepthBefore);
				}
				else
				{
					map.snowGrid.SetDepth(snowCell, snowDepthBefore);
					if (sandGridPresent)
						map.sandGrid.SetDepth(snowCell, 0f);
				}
				map.SetContamination(snowCell, groundContaminationBefore);

				var workerBefore = worker.GetContamination(false);
				var snowBefore = map.snowGrid.GetDepth(snowCell);
				var sandBefore = sandGridPresent ? map.sandGrid.GetDepth(snowCell) : 0f;
				var targetDepthBefore = mode == "sand" ? sandBefore : snowBefore;
				var groundBefore = map.GetContamination(snowCell);
				var snowAdd = ZombieSettings.Values.contamination.snowAdd;
				var expectedWorkerAfter = groundBefore * snowAdd;
				var laborSpeed = worker.GetStatValue(StatDefOf.GeneralLaborSpeed);
				var sourceDerivedWorkTicks = Mathf.CeilToInt(50f * targetDepthBefore / laborSpeed);
				var maxTicks = sourceDerivedWorkTicks * 70 + 120;

				var job = JobMaker.MakeJob(JobDefOf.ClearSnow, snowCell);
				var jobDefAtCreation = job.def.defName;
				job.playerForced = true;
				worker.jobs.StartJob(job, JobCondition.InterruptForced, null, false, true);

				var ticksRun = 0;
				while (ticksRun < maxTicks && (mode == "sand" ? map.sandGrid.GetDepth(snowCell) : map.snowGrid.GetDepth(snowCell)) > 0f)
				{
					AdvanceGameTicks(1);
					ticksRun++;
				}

				var workerAfter = worker.GetContamination(false);
				var snowAfter = map.snowGrid.GetDepth(snowCell);
				var sandAfter = sandGridPresent ? map.sandGrid.GetDepth(snowCell) : 0f;
				var targetDepthAfter = mode == "sand" ? sandAfter : snowAfter;
				var groundAfter = map.GetContamination(snowCell);
				var cleanFilth = VerifyCleanFilthJobTransfer(map, snowCell + new IntVec3(3, 0, 0));
				static bool CloseFloat(float value, float expected) => Mathf.Abs(value - expected) < 0.0001f;
				var snowCleared = targetDepthBefore > 0f && CloseFloat(targetDepthAfter, 0f);
				var contaminationTransferred = snowCleared
					&& CloseFloat(workerBefore, 0f)
					&& CloseFloat(groundBefore, groundContaminationBefore)
					&& CloseFloat(workerAfter, expectedWorkerAfter);

				return new
				{
					success = snowCleared
						&& contaminationTransferred
						&& ObjectSuccess(cleanFilth),
					cleanup,
					mode,
					odysseyInstalled,
					odysseyActive,
					sandGridPresent,
					sandGridAvailable,
					worker = DescribePawn(worker),
					workerCell = ZombieRuntimeActions.DescribeCell(workerCell),
					snowCell = ZombieRuntimeActions.DescribeCell(snowCell),
					terrain = snowCell.GetTerrain(map)?.defName,
					jobDefAtCreation,
					finalJob = worker.CurJobDef?.defName,
					laborSpeed,
					sourceDerivedWorkTicks,
					maxTicks,
					ticksRun,
					snowAdd,
					workerBefore,
					workerAfter,
					expectedWorkerAfter,
					snowBefore,
					snowAfter,
					sandBefore,
					sandAfter,
					targetDepthBefore,
					targetDepthAfter,
					groundBefore,
					groundAfter,
					snowCleared,
					contaminationTransferred,
					cleanFilth
				};
			}
			finally
			{
				if (cleanup)
				{
					worker?.ClearContamination();
					if (worker is { Destroyed: false, Spawned: true })
						worker.Destroy();
					if (snowCell.IsValid && snowCell.InBounds(map))
					{
						if (oldTerrain != null)
							map.terrainGrid.SetTerrain(snowCell, oldTerrain);
						map.snowGrid.SetDepth(snowCell, oldSnowDepth);
						if (sandGridPresent)
							map.sandGrid.SetDepth(snowCell, oldSandDepth);
						map.SetContamination(snowCell, oldGroundContamination);
					}
				}
			}
		}

		static object VerifyCleanFilthJobTransfer(Map map, IntVec3 root)
		{
			var patchTargets = PatchedMethodsForPatchClass("JobDriver_CleanFilth_MakeNewToils_Patch");
			var filthDef = DefDatabase<ThingDef>.GetNamedSilentFail("Filth_Dirt") ?? ThingDefOf.Filth_Blood;
			if (filthDef == null)
			{
				return new
				{
					success = false,
					error = "No filth def was available for the clean-filth contamination probe."
				};
			}

			if (TryFindClearSpawnCell(map, root, 12f, out var filthCell, out var filthCellError) == false)
				return filthCellError;
			if (TryFindClearSpawnCell(map, filthCell + IntVec3.East, 8f, out var workerCell, out var workerCellError) == false)
				return workerCellError;

			var worker = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var madeFilth = (Filth)null;
			var oldGroundContamination = map.GetContamination(filthCell);
			var oldHome = map.areaManager.Home[filthCell];
			try
			{
				ClearFilthAt(map, filthCell);
				map.SetContamination(filthCell, 0f);
				map.areaManager.Home[filthCell] = true;
				GenSpawn.Spawn(worker, workerCell, map, Rot4.South);
				DisablePawnWork(worker);
				worker.ClearContamination();
				worker.jobs?.StopAll(false, true);
				worker.pather?.StopDead();

				var made = FilthMaker.TryMakeFilth(filthCell, map, filthDef, out madeFilth, 1, FilthSourceFlags.None, false);
				if (made == false || madeFilth == null)
				{
					return new
					{
						success = false,
						filthDef = filthDef.defName,
						filthCell = ZombieRuntimeActions.DescribeCell(filthCell),
						made,
						error = "FilthMaker.TryMakeFilth did not create filth."
					};
				}

				const float filthContaminationBefore = 0.64f;
				typeof(Filth).GetField("growTick", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(madeFilth, Find.TickManager.TicksGame - 601);
				madeFilth.SetContamination(filthContaminationBefore);
				var filthBefore = madeFilth.GetContamination();
				var ticksSinceThickened = madeFilth.TicksSinceThickened;
				var workerBefore = worker.GetContamination(false);
				var filthTransfer = ZombieSettings.Values.contamination.filthTransfer;
				var expectedWorkerAfter = filthBefore * filthTransfer;
				var expectedFilthAfter = filthBefore * (1f - filthTransfer);
				var workGiver = new WorkGiver_CleanFilth();
				var hasJob = workGiver.HasJobOnThing(worker, madeFilth, true);
				var job = hasJob ? workGiver.JobOnThing(worker, madeFilth, true) : null;
				var jobTargetCount = job?.GetTargetQueue(TargetIndex.A)?.Count ?? 0;
				var laborSpeed = worker.GetStatValue(StatDefOf.CleaningSpeed);
				var terrainFactor = filthCell.GetTerrain(map).GetStatValueAbstract(StatDefOf.CleaningTimeFactor);
				var sourceDerivedWorkTicks = Mathf.CeilToInt(madeFilth.def.filth.cleaningWorkToReduceThickness * madeFilth.thickness * terrainFactor / laborSpeed);
				var maxTicks = sourceDerivedWorkTicks + 60;
				if (job != null)
				{
					job.playerForced = true;
					worker.jobs.StartJob(job, JobCondition.InterruptForced, null, false, true);
				}

				var ticksRun = 0;
				while (ticksRun < maxTicks && madeFilth.Destroyed == false)
				{
					AdvanceGameTicks(1);
					ticksRun++;
				}

				var workerAfter = worker.GetContamination(false);
				var filthDestroyed = madeFilth.Destroyed;
				var filthAfter = madeFilth.Destroyed ? 0f : madeFilth.GetContamination();
				var recordValue = worker.records?.GetValue(RecordDefOf.MessesCleaned) ?? 0f;
				var patchInstalled = patchTargets.Length > 0;
				static bool CloseFloat(float value, float expected) => Mathf.Abs(value - expected) < 0.0001f;
				var contaminationTransferred = CloseFloat(workerBefore, 0f)
					&& CloseFloat(filthBefore, filthContaminationBefore)
					&& CloseFloat(workerAfter, expectedWorkerAfter)
					&& (filthDestroyed || CloseFloat(filthAfter, expectedFilthAfter));

				return new
				{
					success = patchInstalled
						&& hasJob
						&& job != null
						&& jobTargetCount > 0
						&& filthDestroyed
						&& contaminationTransferred,
					targets = CompactPatchTargets(patchTargets),
					filthDef = filthDef.defName,
					filthCell = ZombieRuntimeActions.DescribeCell(filthCell),
					worker = DescribePawn(worker),
					workerCell = ZombieRuntimeActions.DescribeCell(workerCell),
					made,
					ticksSinceThickened,
					hasJob,
					jobDef = job?.def?.defName,
					jobTargetCount,
					laborSpeed,
					terrainFactor,
					sourceDerivedWorkTicks,
					maxTicks,
					ticksRun,
					filthTransfer,
					workerBefore,
					workerAfter,
					expectedWorkerAfter,
					filthBefore,
					filthAfter,
					expectedFilthAfter,
					filthDestroyed,
					recordValue,
					contaminationTransferred
				};
			}
			finally
			{
				worker?.ClearContamination();
				if (worker is { Destroyed: false, Spawned: true })
					worker.Destroy();
				if (madeFilth is { Destroyed: false, Spawned: true })
					madeFilth.Destroy();
				map.SetContamination(filthCell, oldGroundContamination);
				map.areaManager.Home[filthCell] = oldHome;
			}
		}

		[Tool("zombieland/contamination_social_transfer_contract", Description = "Verify real mating and lovin contamination transfer patches on prepared pawns and the JobDriver_Lovin lay-down toil.")]
		public static object ContaminationSocialTransferContract()
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var mated = VerifyMatedContaminationTransfer(map, new IntVec3(map.Size.x / 2, 0, map.Size.z / 2));
			var lovin = VerifyLovinContaminationTransfer(map, new IntVec3(map.Size.x / 2 + 8, 0, map.Size.z / 2));
			return new
			{
				success = ObjectSuccess(mated) && ObjectSuccess(lovin),
				mated,
				lovin
			};
		}

		static object VerifyMatedContaminationTransfer(Map map, IntVec3 root)
		{
			var patchTargets = PatchedMethodsForPatchClass("PawnUtility_Mated_Patch");
			if (TryFindClearSpawnCell(map, root, 16f, out var maleCell, out var maleCellError) == false)
				return maleCellError;
			if (TryFindClearSpawnCell(map, maleCell + IntVec3.East, 8f, out var femaleCell, out var femaleCellError) == false)
				return femaleCellError;

			var male = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var female = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			try
			{
				GenSpawn.Spawn(male, maleCell, map, Rot4.East);
				GenSpawn.Spawn(female, femaleCell, map, Rot4.West);
				DisablePawnWork(male);
				DisablePawnWork(female);
				male.ClearContamination();
				female.ClearContamination();
				const float maleContamination = 0.8f;
				const float femaleContamination = 0.2f;
				male.SetContamination(maleContamination);
				female.SetContamination(femaleContamination);
				var maleBefore = male.GetContamination(false);
				var femaleBefore = female.GetContamination(false);
				var expectedMaleAfter = maleBefore * 0.5f + femaleBefore * 0.5f;
				var expectedFemaleAfter = maleBefore + femaleBefore - expectedMaleAfter;

				Rand.PushState(7105);
				try
				{
					PawnUtility.Mated(male, female);
				}
				finally
				{
					Rand.PopState();
				}

				var maleAfter = male.GetContamination(false);
				var femaleAfter = female.GetContamination(false);
				var equalized = Approximately(maleAfter, expectedMaleAfter, 0.0001f)
					&& Approximately(femaleAfter, expectedFemaleAfter, 0.0001f);

				return new
				{
					success = patchTargets.Length > 0 && equalized,
					targets = CompactPatchTargets(patchTargets),
					male = DescribePawn(male),
					female = DescribePawn(female),
					maleCell = ZombieRuntimeActions.DescribeCell(maleCell),
					femaleCell = ZombieRuntimeActions.DescribeCell(femaleCell),
					maleBefore,
					femaleBefore,
					maleAfter,
					femaleAfter,
					expectedMaleAfter,
					expectedFemaleAfter,
					equalized
				};
			}
			finally
			{
				male?.ClearContamination();
				female?.ClearContamination();
				if (male is { Destroyed: false, Spawned: true })
					male.Destroy();
				if (female is { Destroyed: false, Spawned: true })
					female.Destroy();
			}
		}

		static object VerifyLovinContaminationTransfer(Map map, IntVec3 root)
		{
			var patchTargets = PatchedMethodsForPatchClass("JobDriver_Lovin_MakeNewToils_Patch");
			if (TryFindClearSpawnCell(map, root, 16f, out var loverCell, out var loverCellError) == false)
				return loverCellError;
			if (TryFindClearSpawnCell(map, loverCell + IntVec3.East, 8f, out var partnerCell, out var partnerCellError) == false)
				return partnerCellError;
			var bedDef = DefDatabase<ThingDef>.GetNamedSilentFail("DoubleBed") ?? ThingDefOf.Bed;
			if (TryFindClearBuildingFootprint(map, bedDef, partnerCell + new IntVec3(2, 0, 0), 12f, out var bedCell, out _) == false)
			{
				return new
				{
					success = false,
					root = ZombieRuntimeActions.DescribeCell(root),
					error = "No clear bed footprint was found for the lovin contamination transfer fixture."
				};
			}

			var lover = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var partner = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var bed = ThingMaker.MakeThing(bedDef, GenStuff.DefaultStuffFor(bedDef)) as Building_Bed;
			if (bed == null)
			{
				return new
				{
					success = false,
					error = "Could not create a bed for the lovin contamination transfer fixture."
				};
			}

			try
			{
				bed.SetFactionDirect(Faction.OfPlayer);
				GenSpawn.Spawn(bed, bedCell, map, Rot4.North, WipeMode.Vanish, false);
				GenSpawn.Spawn(lover, loverCell, map, Rot4.East);
				GenSpawn.Spawn(partner, partnerCell, map, Rot4.West);
				DisablePawnWork(lover);
				DisablePawnWork(partner);
				lover.needs?.AddOrRemoveNeedsAsAppropriate();
				partner.needs?.AddOrRemoveNeedsAsAppropriate();
				lover.ClearContamination();
				partner.ClearContamination();
				lover.jobs?.StopAll(false, true);
				partner.jobs?.StopAll(false, true);
				lover.pather?.StopDead();
				partner.pather?.StopDead();
				lover.relations?.AddDirectRelation(PawnRelationDefOf.Lover, partner);
				bed.CompAssignableToPawn?.TryAssignPawn(lover);
				bed.CompAssignableToPawn?.TryAssignPawn(partner);
				bed.NotifyRoomAssignedPawnsChanged();

				const float loverContamination = 0.8f;
				const float partnerContamination = 0.2f;
				lover.SetContamination(loverContamination);
				partner.SetContamination(partnerContamination);
				var loverBefore = lover.GetContamination(false);
				var partnerBefore = partner.GetContamination(false);
				const float equalizeWeight = 0.1f;
				var expectedLoverAfterOne = loverBefore * (1f - equalizeWeight) + partnerBefore * equalizeWeight;
				var expectedPartnerAfterOne = loverBefore + partnerBefore - expectedLoverAfterOne;
				var expectedLoverAfterTwo = expectedLoverAfterOne * (1f - equalizeWeight) + expectedPartnerAfterOne * equalizeWeight;
				var expectedPartnerAfterTwo = loverBefore + partnerBefore - expectedLoverAfterTwo;

				var job = JobMaker.MakeJob(JobDefOf.Lovin, partner, bed);
				job.playerForced = true;
				Exception startException = null;
				try
				{
					lover.jobs.StartJob(job, JobCondition.InterruptForced, null, false, true);
				}
				catch (Exception ex)
				{
					startException = ex;
				}

				var startedJob = startException == null && lover.CurJobDef == JobDefOf.Lovin;
				var ticksRun = 0;
				var firstTransferTick = -1;
				const int maxTicks = 2000;
				while (startedJob && ticksRun < maxTicks)
				{
					if (lover.jobs?.curDriver is JobDriver_Lovin activeLoverDriver && activeLoverDriver.ticksLeft > 100)
						activeLoverDriver.ticksLeft = 100;
					if (partner.jobs?.curDriver is JobDriver_Lovin activePartnerDriver && activePartnerDriver.ticksLeft > 100)
						activePartnerDriver.ticksLeft = 100;
					AdvanceGameTicks(1);
					ticksRun++;
					var loverNow = lover.GetContamination(false);
					var partnerNow = partner.GetContamination(false);
					if (firstTransferTick < 0 && Mathf.Abs(loverNow - loverBefore) > 0.01f)
						firstTransferTick = ticksRun;
					if (firstTransferTick >= 0 && ticksRun - firstTransferTick >= 10)
						break;
				}

				var loverAfter = lover.GetContamination(false);
				var partnerAfter = partner.GetContamination(false);
				var matchedOneTransfer = Approximately(loverAfter, expectedLoverAfterOne)
					&& Approximately(partnerAfter, expectedPartnerAfterOne);
				var matchedTwoTransfers = Approximately(loverAfter, expectedLoverAfterTwo)
					&& Approximately(partnerAfter, expectedPartnerAfterTwo);
				var equalized = startException == null && (matchedOneTransfer || matchedTwoTransfers);
				var loverDriver = lover.jobs?.curDriver as JobDriver_Lovin;
				var partnerDriver = partner.jobs?.curDriver as JobDriver_Lovin;

				return new
				{
					success = patchTargets.Length > 0
						&& startedJob
						&& firstTransferTick >= 0
						&& equalized,
					targets = CompactPatchTargets(patchTargets),
					lover = DescribePawn(lover),
					partner = DescribePawn(partner),
					bed = ZombieRuntimeActions.StableThingId(bed),
					loverCell = ZombieRuntimeActions.DescribeCell(loverCell),
					partnerCell = ZombieRuntimeActions.DescribeCell(partnerCell),
					bedCell = ZombieRuntimeActions.DescribeCell(bedCell),
					bedDef = bed.def?.defName,
					bedOccupants = bed.CurOccupants.Select(ZombieRuntimeActions.StableThingId).ToArray(),
					jobDef = job.def?.defName,
					startedJob,
					startException = startException?.GetType().FullName,
					startExceptionMessage = startException?.Message,
					ticksRun,
					maxTicks,
					firstTransferTick,
					durationGate = 25000,
					driverTicksLeftClamp = 100,
					loverJobAfter = lover.CurJobDef?.defName,
					partnerJobAfter = partner.CurJobDef?.defName,
					loverDriverTicksLeft = loverDriver?.ticksLeft ?? -1,
					partnerDriverTicksLeft = partnerDriver?.ticksLeft ?? -1,
					equalizeWeight,
					loverBefore,
					partnerBefore,
					loverAfter,
					partnerAfter,
					expectedLoverAfterOne,
					expectedPartnerAfterOne,
					expectedLoverAfterTwo,
					expectedPartnerAfterTwo,
					matchedOneTransfer,
					matchedTwoTransfers,
					equalized
				};
			}
			finally
			{
				lover.jobs?.StopAll(false, true);
				partner.jobs?.StopAll(false, true);
				lover?.ClearContamination();
				partner?.ClearContamination();
				if (lover is { Destroyed: false, Spawned: true })
					lover.Destroy();
				if (partner is { Destroyed: false, Spawned: true })
					partner.Destroy();
				if (bed is { Destroyed: false, Spawned: true })
					bed.Destroy();
			}
		}

		[Tool("zombieland/contamination_misc_spawn_handoffs_contract", Description = "Verify miscellaneous spawn handoffs that route contamination through GenSpawn/GenPlace replacements.")]
		public static object ContaminationMiscSpawnHandoffsContract()
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			var lifespanContaminated = VerifyLifespanExpirePlantSpawnTransfer(map, root + new IntVec3(0, 0, 6), 0.75f, "contaminated");
			var lifespanClean = VerifyLifespanExpirePlantSpawnTransfer(map, root + new IntVec3(4, 0, 6), 0f, "clean");
			var medicalHediffThings = VerifyMedicalHediffSpawnTransfer(map, root + new IntVec3(8, 0, 6));
			var medicalNaturalPart = VerifyMedicalNaturalPartSpawnTransfer(map, root + new IntVec3(12, 0, 6));
			var removedImplant = VerifyRecipeRemoveImplantSpawnTransfer(map, root + new IntVec3(16, 0, 6));
			var tunnelJelly = VerifyTunnelJellySpawnTransfer(map, root + new IntVec3(20, 0, 6));
			return new
			{
				success = ObjectSuccess(lifespanContaminated)
					&& ObjectSuccess(lifespanClean)
					&& ObjectSuccess(medicalHediffThings)
					&& ObjectSuccess(medicalNaturalPart)
					&& ObjectSuccess(removedImplant)
					&& ObjectSuccess(tunnelJelly),
				lifespanContaminated,
				lifespanClean,
				medicalHediffThings,
				medicalNaturalPart,
				removedImplant,
				tunnelJelly
			};
		}

		static object VerifyTunnelJellySpawnTransfer(Map map, IntVec3 root)
		{
			var patchTargets = PatchedMethodsForPatchClass("GenSpawn_Spawn_Replacement_Patch");
			var spawnMethod = typeof(TunnelJellySpawner).GetMethod("Spawn", BindingFlags.Instance | BindingFlags.NonPublic);
			if (spawnMethod == null)
			{
				return new
				{
					success = false,
					targets = CompactPatchTargets(patchTargets),
					error = "TunnelJellySpawner.Spawn protected method was not found."
				};
			}

			if (TryFindClearSpawnCell(map, root, 24f, out var cell, out var cellError) == false)
				return cellError;

			const float groundContamination = 0.58f;
			const int jellyCount = 18;
			var radiusCells = GenRadial.RadialCellsAround(cell, 2f, true)
				.Where(candidate => candidate.InBounds(map))
				.ToArray();
			var oldContamination = radiusCells.ToDictionary(candidate => candidate, candidate => map.GetContamination(candidate));
			var existingThings = radiusCells.SelectMany(candidate => candidate.GetThingList(map)).ToHashSet();
			var spawner = new TunnelJellySpawner { jellyCount = jellyCount };
			var spawnedJelly = new List<Thing>();
			try
			{
				foreach (var radiusCell in radiusCells)
					map.SetContamination(radiusCell, groundContamination);

				spawnMethod.Invoke(spawner, new object[] { map, cell });

				spawnedJelly = radiusCells.SelectMany(candidate => candidate.GetThingList(map))
					.Where(thing => existingThings.Contains(thing) == false && thing.def == ThingDefOf.InsectJelly)
					.ToList();
				var contaminations = spawnedJelly.Select(thing => thing.GetContamination()).ToArray();
				var expectedContamination = groundContamination * ZombieSettings.Values.contamination.jellyAdd;
				var allJellySpawned = spawnedJelly.Count > 0 && spawnedJelly.All(thing => thing.Spawned);
				var stackCount = spawnedJelly.Sum(thing => thing.stackCount);
				var contaminationTransferred = allJellySpawned
					&& contaminations.All(value => Approximately(value, expectedContamination, 0.0001f));

				return new
				{
					success = patchTargets.Length > 0
						&& spawner.jellyCount == 0
						&& allJellySpawned
						&& stackCount == jellyCount
						&& contaminationTransferred,
					targets = CompactPatchTargets(patchTargets),
					method = $"{spawnMethod.DeclaringType?.FullName}::{spawnMethod}",
					cell = ZombieRuntimeActions.DescribeCell(cell),
					radiusCellCount = radiusCells.Length,
					jellyCount,
					spawnerJellyCountAfter = spawner.jellyCount,
					jellyAdd = ZombieSettings.Values.contamination.jellyAdd,
					expectedContamination,
					spawnedThings = spawnedJelly.Select(thing => new
					{
						thing = ZombieRuntimeActions.StableThingId(thing),
						cell = ZombieRuntimeActions.DescribeCell(thing.Position),
						stackCount = thing.stackCount,
						contamination = thing.GetContamination()
					}).ToArray(),
					spawnedThingCount = spawnedJelly.Count,
					stackCount,
					contaminations,
					allJellySpawned,
					contaminationTransferred
				};
			}
			finally
			{
				foreach (var thing in spawnedJelly.Where(thing => thing is { Destroyed: false, Spawned: true }).ToArray())
				{
					thing.ClearContamination();
					thing.Destroy();
				}
				foreach (var entry in oldContamination)
				{
					if (entry.Key.InBounds(map))
						map.SetContamination(entry.Key, entry.Value);
				}
			}
		}

		static object VerifyLifespanExpirePlantSpawnTransfer(Map map, IntVec3 root, float parentContamination, string caseName)
		{
			var patchTargets = PatchedMethodsForPatchClass("CompLifespan_Expire_Patch");
			var expireMethod = typeof(CompLifespan).GetMethod("Expire", BindingFlags.Instance | BindingFlags.NonPublic);
			if (expireMethod == null)
			{
				return new
				{
					success = false,
					targets = CompactPatchTargets(patchTargets),
					error = "CompLifespan.Expire protected method was not found."
				};
			}

			var plantDef = DefDatabase<ThingDef>.GetNamedSilentFail("Plant_Grass")
				?? DefDatabase<ThingDef>.GetNamedSilentFail("Plant_Rice");
			if (plantDef == null)
			{
				return new
				{
					success = false,
					targets = CompactPatchTargets(patchTargets),
					error = "No plant def was available for the lifespan-expire fixture."
				};
			}

			if (TryFindClearSpawnCell(map, root, 24f, out var cell, out var cellError) == false)
				return cellError;

			var parentDef = ThingDefOf.ComponentIndustrial;
			var parent = ThingMaker.MakeThing(parentDef) as ThingWithComps;
			if (parent == null)
			{
				return new
				{
					success = false,
					targets = CompactPatchTargets(patchTargets),
					parentDef = parentDef?.defName,
					error = "The selected lifespan-expire parent def did not create a ThingWithComps."
				};
			}

			var comp = new CompLifespan();
			var props = new CompProperties_Lifespan
			{
				lifespanTicks = 1,
				plantDefToSpawn = plantDef
			};
			Plant spawnedPlant = null;
			var oldTerrain = cell.GetTerrain(map);
			try
			{
				map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil);
				if (plantDef.CanNowPlantAt(cell, map) == false)
				{
					return new
					{
						success = false,
						targets = CompactPatchTargets(patchTargets),
						plantDef = plantDef.defName,
						cell = ZombieRuntimeActions.DescribeCell(cell),
						terrain = cell.GetTerrain(map)?.defName,
						error = "The lifespan-expire fixture cell was not plantable even after setting temporary soil."
					};
				}

				comp.parent = parent;
				comp.props = props;
				typeof(ThingWithComps).GetField("comps", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(parent, new List<ThingComp> { comp });
				GenSpawn.Spawn(parent, cell, map, WipeMode.Vanish);
				parent.SetContamination(parentContamination);
				var parentBefore = parent.GetContamination();
				var existingThings = cell.GetThingList(map).ToHashSet();

				expireMethod.Invoke(comp, Array.Empty<object>());

				spawnedPlant = cell.GetThingList(map)
					.OfType<Plant>()
					.FirstOrDefault(plant => existingThings.Contains(plant) == false && plant.def == plantDef);
				var plantContamination = spawnedPlant?.GetContamination() ?? -1f;
				var generalTransfer = ZombieSettings.Values.contamination.generalTransfer;
				var expectedPlantContamination = parentBefore * generalTransfer;
				var plantSpawned = spawnedPlant != null && spawnedPlant.Spawned;
				var parentDestroyed = parent.Destroyed;
				var contaminationTransferred = plantSpawned
					&& Approximately(plantContamination, expectedPlantContamination);

				return new
				{
					success = patchTargets.Length > 0
						&& parentDestroyed
						&& plantSpawned
						&& contaminationTransferred,
					targets = CompactPatchTargets(patchTargets),
					caseName,
					method = $"{expireMethod.DeclaringType?.FullName}::{expireMethod}",
					parent = ZombieRuntimeActions.StableThingId(parent),
					parentDef = parent.def?.defName,
					parentBefore,
					parentDestroyed,
					plant = ZombieRuntimeActions.StableThingId(spawnedPlant),
					plantDef = plantDef.defName,
					cell = ZombieRuntimeActions.DescribeCell(cell),
					generalTransfer,
					plantContamination,
					expectedPlantContamination,
					plantSpawned,
					contaminationTransferred
				};
			}
			finally
			{
				parent?.ClearContamination();
				spawnedPlant?.ClearContamination();
				if (spawnedPlant is { Destroyed: false, Spawned: true })
					spawnedPlant.Destroy();
				if (parent is { Destroyed: false, Spawned: true })
					parent.Destroy();
				if (oldTerrain != null && cell.InBounds(map))
					map.terrainGrid.SetTerrain(cell, oldTerrain);
			}
		}

		static object VerifyMedicalHediffSpawnTransfer(Map map, IntVec3 root)
		{
			var patchTargets = PatchedMethodsForPatchClass("MedicalRecipesUtility_GenSpawn_Spawn_Patches");
			if (TryFindClearSpawnCell(map, root, 24f, out var pawnCell, out var pawnCellError) == false)
				return pawnCellError;
			if (TryFindClearSpawnCell(map, pawnCell + IntVec3.East, 8f, out var spawnCell, out var spawnCellError) == false)
				return spawnCellError;

			var hediffDef = DefDatabase<HediffDef>.AllDefsListForReading
				.Where(def => def?.spawnThingOnRemoved != null)
				.OrderBy(def => def.defName)
				.FirstOrDefault();
			if (hediffDef == null)
			{
				return new
				{
					success = true,
					skipped = true,
					reason = "No HediffDef with spawnThingOnRemoved is available in the active configuration.",
					targets = CompactPatchTargets(patchTargets)
				};
			}

			var pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var spawnedThings = new List<Thing>();
			try
			{
				GenSpawn.Spawn(pawn, pawnCell, map, Rot4.South);
				DisablePawnWork(pawn);
				pawn.ClearContamination();
				pawn.jobs?.StopAll(false, true);
				pawn.pather?.StopDead();

				var part = pawn.health.hediffSet.GetNotMissingParts()
					.Where(record => record != null)
					.FirstOrDefault(record => record.def == BodyPartDefOf.Arm)
					?? pawn.health.hediffSet.GetNotMissingParts().FirstOrDefault();
				if (part == null)
				{
					return new
					{
						success = false,
						targets = CompactPatchTargets(patchTargets),
						pawn = DescribePawn(pawn),
						error = "No not-missing body part was available for the medical hediff spawn fixture."
					};
				}

				const float pawnContamination = 0.69f;
				pawn.SetContamination(pawnContamination);
				var pawnBefore = pawn.GetContamination();
				var hediff = HediffMaker.MakeHediff(hediffDef, pawn, part);
				pawn.health.AddHediff(hediff, part);
				var hediffAdded = pawn.health.hediffSet.hediffs.Contains(hediff);
				var existingThings = spawnCell.GetThingList(map).ToHashSet();

				MedicalRecipesUtility.SpawnThingsFromHediffs(pawn, part, spawnCell, map);

				spawnedThings = spawnCell.GetThingList(map)
					.Where(thing => existingThings.Contains(thing) == false && thing.def == hediffDef.spawnThingOnRemoved)
					.ToList();
				var pawnAfter = pawn.GetContamination();
				var spawnedContaminations = spawnedThings.Select(thing => thing.GetContamination()).ToArray();
				var totalSpawnedContamination = spawnedContaminations.Sum();
				var generalTransfer = ZombieSettings.Values.contamination.generalTransfer;
				var nominalTransfer = pawnBefore * generalTransfer;
				var expectedSpawnedContamination = pawnBefore - pawnAfter;
				var innerThingCount = ThingOwnerUtility.GetAllThingsRecursively(pawn, false).Count;
				var thingSpawned = spawnedThings.Count > 0 && spawnedThings.All(thing => thing.Spawned);
				var contaminationTransferred = thingSpawned
					&& expectedSpawnedContamination > 0f
					&& Approximately(totalSpawnedContamination, expectedSpawnedContamination);

				return new
				{
					success = patchTargets.Length > 0
						&& hediffAdded
						&& thingSpawned
						&& contaminationTransferred,
					targets = CompactPatchTargets(patchTargets),
					method = "RimWorld.MedicalRecipesUtility::SpawnThingsFromHediffs(Pawn, BodyPartRecord, IntVec3, Map)",
					pawn = DescribePawn(pawn),
					pawnCell = ZombieRuntimeActions.DescribeCell(pawnCell),
					spawnCell = ZombieRuntimeActions.DescribeCell(spawnCell),
					part = part.def?.defName,
					hediffDef = hediffDef.defName,
					spawnThingOnRemoved = hediffDef.spawnThingOnRemoved?.defName,
					pawnBefore,
					pawnAfter,
					innerThingCount,
					generalTransfer,
					nominalTransfer,
					spawnedThings = spawnedThings.Select(thing => new
					{
						thing = ZombieRuntimeActions.StableThingId(thing),
						contamination = thing.GetContamination()
					}).ToArray(),
					spawnedThingCount = spawnedThings.Count,
					spawnedContaminations,
					totalSpawnedContamination,
					expectedSpawnedContamination,
					hediffAdded,
					thingSpawned,
					contaminationTransferred
				};
			}
			finally
			{
				pawn?.ClearContamination();
				foreach (var thing in spawnedThings.Where(thing => thing is { Destroyed: false, Spawned: true }).ToArray())
				{
					thing.ClearContamination();
					thing.Destroy();
				}
				if (pawn is { Destroyed: false, Spawned: true })
					pawn.Destroy();
			}
		}

		static object VerifyMedicalNaturalPartSpawnTransfer(Map map, IntVec3 root)
		{
			var patchTargets = PatchedMethodsForPatchClass("MedicalRecipesUtility_GenSpawn_Spawn_Patches");
			if (TryFindClearSpawnCell(map, root, 24f, out var pawnCell, out var pawnCellError) == false)
				return pawnCellError;
			if (TryFindClearSpawnCell(map, pawnCell + IntVec3.East, 8f, out var spawnCell, out var spawnCellError) == false)
				return spawnCellError;

			var pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			Thing spawnedThing = null;
			BodyPartDef partDef = null;
			ThingDef oldSpawnThingOnRemoved = null;
			try
			{
				GenSpawn.Spawn(pawn, pawnCell, map, Rot4.South);
				DisablePawnWork(pawn);
				pawn.ClearContamination();
				pawn.jobs?.StopAll(false, true);
				pawn.pather?.StopDead();

				var part = pawn.health.hediffSet.GetNotMissingParts()
					.Where(record => record != null)
					.FirstOrDefault(record => MedicalRecipesUtility.IsClean(pawn, record) && record.def == BodyPartDefOf.Arm)
					?? pawn.health.hediffSet.GetNotMissingParts()
						.FirstOrDefault(record => record != null && MedicalRecipesUtility.IsClean(pawn, record));
				if (part == null)
				{
					return new
					{
						success = false,
						targets = CompactPatchTargets(patchTargets),
						pawn = DescribePawn(pawn),
						error = "No clean not-missing body part was available for the natural-part spawn fixture."
					};
				}

				partDef = part.def;
				oldSpawnThingOnRemoved = partDef.spawnThingOnRemoved;
				partDef.spawnThingOnRemoved = ThingDefOf.ComponentIndustrial;

				const float pawnContamination = 0.64f;
				pawn.SetContamination(pawnContamination);
				var pawnBefore = pawn.GetContamination();
				var existingThings = spawnCell.GetThingList(map).ToHashSet();
				var cleanAndDroppable = MedicalRecipesUtility.IsCleanAndDroppable(pawn, part);

				spawnedThing = MedicalRecipesUtility.SpawnNaturalPartIfClean(pawn, part, spawnCell, map);

				var spawnedThingAtCell = spawnedThing != null
					&& spawnedThing.Spawned
					&& spawnedThing.Position == spawnCell
					&& existingThings.Contains(spawnedThing) == false;
				var pawnAfter = pawn.GetContamination();
				var spawnedContamination = spawnedThing?.GetContamination() ?? -1f;
				var generalTransfer = ZombieSettings.Values.contamination.generalTransfer;
				var nominalTransfer = pawnBefore * generalTransfer;
				var expectedSpawnedContamination = pawnBefore - pawnAfter;
				var innerThingCount = ThingOwnerUtility.GetAllThingsRecursively(pawn, false).Count;
				var contaminationTransferred = spawnedThingAtCell
					&& expectedSpawnedContamination > 0f
					&& Approximately(spawnedContamination, expectedSpawnedContamination);

				return new
				{
					success = patchTargets.Length > 0
						&& cleanAndDroppable
						&& spawnedThingAtCell
						&& contaminationTransferred,
					targets = CompactPatchTargets(patchTargets),
					method = "RimWorld.MedicalRecipesUtility::SpawnNaturalPartIfClean(Pawn, BodyPartRecord, IntVec3, Map)",
					pawn = DescribePawn(pawn),
					pawnCell = ZombieRuntimeActions.DescribeCell(pawnCell),
					spawnCell = ZombieRuntimeActions.DescribeCell(spawnCell),
					part = part.def?.defName,
					temporarySpawnThingOnRemoved = partDef.spawnThingOnRemoved?.defName,
					pawnBefore,
					pawnAfter,
					innerThingCount,
					generalTransfer,
					nominalTransfer,
					expectedSpawnedContamination,
					spawnedThing = DescribeContaminationThing(spawnedThing),
					spawnedContamination,
					cleanAndDroppable,
					spawnedThingAtCell,
					contaminationTransferred
				};
			}
			finally
			{
				if (partDef != null)
					partDef.spawnThingOnRemoved = oldSpawnThingOnRemoved;
				pawn?.ClearContamination();
				if (spawnedThing is { Destroyed: false, Spawned: true })
				{
					spawnedThing.ClearContamination();
					spawnedThing.Destroy();
				}
				if (pawn is { Destroyed: false, Spawned: true })
					pawn.Destroy();
			}
		}

		static object VerifyRecipeRemoveImplantSpawnTransfer(Map map, IntVec3 root)
		{
			var patchTargets = PatchedMethodsForPatchClass("Recipe_RemoveImplant_ApplyOnPawn_Patches");
			var applyMethod = typeof(Recipe_RemoveImplant).GetMethod(nameof(Recipe_RemoveImplant.ApplyOnPawn), BindingFlags.Instance | BindingFlags.Public);
			if (applyMethod == null)
			{
				return new
				{
					success = false,
					targets = CompactPatchTargets(patchTargets),
					error = "Recipe_RemoveImplant.ApplyOnPawn public method was not found."
				};
			}

			if (TryFindClearSpawnCell(map, root, 24f, out var patientCell, out var patientCellError) == false)
				return patientCellError;
			if (TryFindClearSpawnCell(map, patientCell + IntVec3.East, 8f, out var doctorCell, out var doctorCellError) == false)
				return doctorCellError;

			var hediffDef = DefDatabase<HediffDef>.AllDefsListForReading
				.Where(def => def?.spawnThingOnRemoved != null)
				.OrderBy(def => def.defName)
				.FirstOrDefault();
			if (hediffDef == null)
			{
				return new
				{
					success = true,
					skipped = true,
					reason = "No HediffDef with spawnThingOnRemoved is available in the active configuration.",
					targets = CompactPatchTargets(patchTargets)
				};
			}

			var patient = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var doctor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var spawnedThings = new List<Thing>();
			try
			{
				GenSpawn.Spawn(patient, patientCell, map, Rot4.South);
				GenSpawn.Spawn(doctor, doctorCell, map, Rot4.South);
				DisablePawnWork(patient);
				DisablePawnWork(doctor);
				patient.ClearContamination();
				doctor.ClearContamination();
				patient.jobs?.StopAll(false, true);
				doctor.jobs?.StopAll(false, true);
				patient.pather?.StopDead();
				doctor.pather?.StopDead();

				var part = patient.health.hediffSet.GetNotMissingParts()
					.Where(record => record != null)
					.FirstOrDefault(record => record.def == BodyPartDefOf.Arm)
					?? patient.health.hediffSet.GetNotMissingParts().FirstOrDefault();
				if (part == null)
				{
					return new
					{
						success = false,
						targets = CompactPatchTargets(patchTargets),
						patient = DescribePawn(patient),
						error = "No not-missing body part was available for the remove-implant fixture."
					};
				}

				const float patientContamination = 0.67f;
				patient.SetContamination(patientContamination);
				var patientBefore = patient.GetContamination();
				var hediff = HediffMaker.MakeHediff(hediffDef, patient, part);
				patient.health.AddHediff(hediff, part);
				var hediffAdded = patient.health.hediffSet.hediffs.Contains(hediff);
				var existingThings = doctorCell.GetThingList(map).ToHashSet();
				var worker = new Recipe_RemoveImplant
				{
					recipe = new RecipeDef
					{
						defName = "ZombielandBridgeRemoveImplantFixture",
						label = "Zombieland bridge remove implant fixture",
						removesHediff = hediffDef,
						isViolation = false,
						surgeryOutcomeEffect = null
					}
				};

				worker.ApplyOnPawn(patient, part, doctor, new List<Thing>(), null);

				spawnedThings = doctorCell.GetThingList(map)
					.Where(thing => existingThings.Contains(thing) == false && thing.def == hediffDef.spawnThingOnRemoved)
					.ToList();
				var patientAfter = patient.GetContamination();
				var hediffStillPresent = patient.health.hediffSet.hediffs.Contains(hediff);
				var spawnedContaminations = spawnedThings.Select(thing => thing.GetContamination()).ToArray();
				var totalSpawnedContamination = spawnedContaminations.Sum();
				var generalTransfer = ZombieSettings.Values.contamination.generalTransfer;
				var nominalTransfer = patientBefore * generalTransfer;
				var expectedSpawnedContamination = patientBefore - patientAfter;
				var innerThingCount = ThingOwnerUtility.GetAllThingsRecursively(patient, false).Count;
				var thingSpawned = spawnedThings.Count > 0 && spawnedThings.All(thing => thing.Spawned);
				var contaminationTransferred = thingSpawned
					&& expectedSpawnedContamination > 0f
					&& Approximately(totalSpawnedContamination, expectedSpawnedContamination);

				return new
				{
					success = patchTargets.Length > 0
						&& hediffAdded
						&& hediffStillPresent == false
						&& thingSpawned
						&& contaminationTransferred,
					targets = CompactPatchTargets(patchTargets),
					method = $"{applyMethod.DeclaringType?.FullName}::{applyMethod}",
					patient = DescribePawn(patient),
					doctor = DescribePawn(doctor),
					patientCell = ZombieRuntimeActions.DescribeCell(patientCell),
					doctorCell = ZombieRuntimeActions.DescribeCell(doctorCell),
					part = part.def?.defName,
					hediffDef = hediffDef.defName,
					spawnThingOnRemoved = hediffDef.spawnThingOnRemoved?.defName,
					patientBefore,
					patientAfter,
					innerThingCount,
					generalTransfer,
					nominalTransfer,
					spawnedThings = spawnedThings.Select(thing => new
					{
						thing = ZombieRuntimeActions.StableThingId(thing),
						contamination = thing.GetContamination()
					}).ToArray(),
					spawnedThingCount = spawnedThings.Count,
					spawnedContaminations,
					totalSpawnedContamination,
					expectedSpawnedContamination,
					hediffAdded,
					hediffStillPresent,
					thingSpawned,
					contaminationTransferred
				};
			}
			finally
			{
				patient?.ClearContamination();
				doctor?.ClearContamination();
				foreach (var thing in spawnedThings.Where(thing => thing is { Destroyed: false, Spawned: true }).ToArray())
				{
					thing.ClearContamination();
					thing.Destroy();
				}
				if (patient is { Destroyed: false, Spawned: true })
					patient.Destroy();
				if (doctor is { Destroyed: false, Spawned: true })
					doctor.Destroy();
			}
		}

		[Tool("zombieland/contamination_clear_pollution_contract", Description = "Verify real JobDriver_ClearPollution transfers contaminated polluted ground into the worker and spawned wastepack.")]
		public static object ContaminationClearPollutionContract(
		[ToolParameter(Description = "Destroy the worker/wastepack and restore pollution/cell contamination before returning.", Required = false, DefaultValue = true)] bool cleanup = true)
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}
			if (ThingDefOf.Wastepack == null || JobDefOf.ClearPollution == null)
			{
				return new
				{
					success = true,
					skipped = true,
					reason = "Wastepack or ClearPollution is unavailable in the active RimWorld configuration."
				};
			}
			if (ModsConfig.BiotechActive == false)
			{
				return new
				{
					success = true,
					skipped = true,
					reason = "Wastepack and ClearPollution defs are available, but RimWorld pollution writes are still gated behind active Biotech.",
					wastepackDefAvailable = ThingDefOf.Wastepack != null,
					clearPollutionJobDefAvailable = JobDefOf.ClearPollution != null,
					biotechActive = false
				};
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var pollutionCell, out var pollutionCellError) == false)
				return pollutionCellError;
			if (TryFindClearSpawnCell(map, pollutionCell + IntVec3.East, 8f, out var workerCell, out var workerCellError) == false)
				return workerCellError;

			var worker = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var oldGroundContamination = map.GetContamination(pollutionCell);
			var oldPolluted = pollutionCell.IsPolluted(map);
			var pollutionClearArea = map.areaManager.PollutionClear;
			var createdPollutionClearArea = false;
			if (pollutionClearArea == null)
			{
				pollutionClearArea = new Area_PollutionClear(map.areaManager);
				map.areaManager.AllAreas.Add(pollutionClearArea);
				createdPollutionClearArea = true;
			}
			var oldPollutionClear = pollutionClearArea[pollutionCell];
			var wastepacks = new List<Thing>();
			try
			{
				GenSpawn.Spawn(worker, workerCell, map, Rot4.South);
				DisablePawnWork(worker);
				worker.ClearContamination();
				worker.jobs?.StopAll(false, true);
				worker.pather?.StopDead();

				const float groundContaminationBefore = 0.66f;
				pollutionCell.Pollute(map, true);
				pollutionClearArea[pollutionCell] = true;
				map.SetContamination(pollutionCell, groundContaminationBefore);
				if (pollutionCell.IsPolluted(map) == false)
				{
					return new
					{
						success = true,
						skipped = true,
						reason = "PollutionGrid.SetPolluted did not mark the cell polluted. RimWorld gates pollution writes behind active Biotech even when Wastepack and ClearPollution defs are fixture-provided.",
						cleanup,
						createdPollutionClearArea,
						pollutionCell = ZombieRuntimeActions.DescribeCell(pollutionCell),
						workerCell = ZombieRuntimeActions.DescribeCell(workerCell),
						wastepackDefAvailable = ThingDefOf.Wastepack != null,
						clearPollutionJobDefAvailable = JobDefOf.ClearPollution != null,
						pollutedBefore = oldPolluted,
						pollutedAfterSetup = false,
						groundContaminationBefore
					};
				}

				var existingWastepacks = map.listerThings.ThingsOfDef(ThingDefOf.Wastepack).ToHashSet();
				var workerBefore = worker.GetContamination(false);
				var groundBefore = map.GetContamination(pollutionCell);
				var pollutedBefore = pollutionCell.IsPolluted(map);
				var pollutionAdd = ZombieSettings.Values.contamination.pollutionAdd;
				var wastePackAdd = ZombieSettings.Values.contamination.wastePackAdd;
				var expectedWorkerAfter = groundBefore * pollutionAdd;
				var laborSpeed = worker.GetStatValue(StatDefOf.GeneralLaborSpeed);
				var sourceDerivedLaborTicks = Mathf.CeilToInt(5600f / laborSpeed);
				var maxGameTicks = sourceDerivedLaborTicks + 120;

				var job = JobMaker.MakeJob(JobDefOf.ClearPollution, pollutionCell);
				var jobDefAtCreation = job.def.defName;
				job.playerForced = true;
				worker.jobs.StartJob(job, JobCondition.InterruptForced, null, false, true);

				var ticksRun = 0;
				while (ticksRun < maxGameTicks && pollutionCell.IsPolluted(map))
				{
					AdvanceGameTicks(1);
					ticksRun++;
				}

				wastepacks = map.listerThings.ThingsOfDef(ThingDefOf.Wastepack)
					.Where(thing => existingWastepacks.Contains(thing) == false)
					.ToList();
				var wastepack = wastepacks.FirstOrDefault();
				var workerAfter = worker.GetContamination(false);
				var pollutedAfter = pollutionCell.IsPolluted(map);
				var groundAfter = map.GetContamination(pollutionCell);
				var wastepackGround = wastepack == null ? -1f : map.GetContamination(wastepack.Position);
				var wastepackContamination = wastepack?.GetContamination() ?? -1f;
				var expectedWastepackContamination = wastepack == null ? -1f : groundBefore * wastePackAdd;
				static bool CloseFloat(float value, float expected) => Mathf.Abs(value - expected) < 0.0001f;
				var pollutionCleared = pollutedBefore && pollutedAfter == false;
				var workerContaminated = pollutionCleared
					&& CloseFloat(workerBefore, 0f)
					&& CloseFloat(workerAfter, expectedWorkerAfter);
				var wastepackContaminated = wastepacks.Count == 1
					&& wastepack.Spawned
					&& CloseFloat(wastepackContamination, expectedWastepackContamination);

				return new
				{
					success = pollutionCleared && workerContaminated && wastepackContaminated,
					cleanup,
					worker = DescribePawn(worker),
					workerCell = ZombieRuntimeActions.DescribeCell(workerCell),
					pollutionCell = ZombieRuntimeActions.DescribeCell(pollutionCell),
					jobDefAtCreation,
					finalJob = worker.CurJobDef?.defName,
					createdPollutionClearArea,
					laborSpeed,
					sourceDerivedLaborTicks,
					maxGameTicks,
					ticksRun,
					pollutionAdd,
					wastePackAdd,
					workerBefore,
					workerAfter,
					expectedWorkerAfter,
					pollutedBefore,
					pollutedAfter,
					groundBefore,
					groundAfter,
					wastepackCount = wastepacks.Count,
					wastepack = ZombieRuntimeActions.StableThingId(wastepack),
					wastepackCell = wastepack == null ? null : ZombieRuntimeActions.DescribeCell(wastepack.Position),
					wastepackGround,
					wastepackContamination,
					expectedWastepackContamination,
					pollutionCleared,
					workerContaminated,
					wastepackContaminated
				};
			}
			finally
			{
				if (cleanup)
				{
					worker?.ClearContamination();
					if (worker is { Destroyed: false, Spawned: true })
						worker.Destroy();
					foreach (var wastepack in wastepacks)
					{
						wastepack.ClearContamination();
						if (wastepack is { Destroyed: false, Spawned: true })
							wastepack.Destroy();
					}
					if (pollutionCell.IsValid && pollutionCell.InBounds(map))
					{
						pollutionClearArea[pollutionCell] = oldPollutionClear;
						if (oldPolluted)
							pollutionCell.Pollute(map, true);
						else if (pollutionCell.IsPolluted(map))
							pollutionCell.Unpollute(map);
						map.SetContamination(pollutionCell, oldGroundContamination);
					}
				}
				if (createdPollutionClearArea)
					map.areaManager.AllAreas.Remove(pollutionClearArea);
			}
		}

		[Tool("zombieland/contamination_recipe_product_contract", Description = "Verify contamination transfers from spawned recipe ingredients into unspawned recipe products.")]
		public static object ContaminationRecipeProductContract()
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var benchDef = DefDatabase<ThingDef>.GetNamed("TableButcher", false);
			var bench = benchDef == null ? null : ThingMaker.MakeThing(benchDef, ThingDefOf.WoodLog);
			if (bench is not IBillGiver billGiver)
			{
				return new
				{
					success = false,
					error = "Could not create a butcher-table bill giver fixture."
				};
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var workerCell, out var workerSpawnError) == false)
				return workerSpawnError;
			if (TryFindClearSpawnCell(map, workerCell + new IntVec3(3, 0, 0), 8f, out var ingredientCell, out var ingredientSpawnError) == false)
				return ingredientSpawnError;

			var oldRecipeTransfer = ZombieSettings.Values.contamination.receipeTransfer;
			var oldProduceEqualize = ZombieSettings.Values.contamination.produceEqualize;
			var oldBenchEqualize = ZombieSettings.Values.contamination.benchEqualize;
			var oldWorkerTransfer = ZombieSettings.Values.contamination.workerTransfer;
			var worker = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var ingredient = ThingMaker.MakeThing(ThingDefOf.Steel);
			Thing[] products = Array.Empty<Thing>();

			try
			{
				GenSpawn.Spawn(worker, workerCell, map, Rot4.South);
				DisablePawnWork(worker);
				ingredient.stackCount = 10;
				GenSpawn.Spawn(ingredient, ingredientCell, map, WipeMode.Vanish);

				ZombieSettings.Values.contamination.receipeTransfer = 0.5f;
				ZombieSettings.Values.contamination.produceEqualize = 0f;
				ZombieSettings.Values.contamination.benchEqualize = 0f;
				ZombieSettings.Values.contamination.workerTransfer = 0f;

				const float ingredientInputContamination = 0.8f;
				ingredient.SetContamination(ingredientInputContamination);
				var ingredientBefore = ingredient.GetContamination();
				var recipe = new RecipeDef
				{
					defName = "ZombielandBridgeRecipeProductContract",
					products = new List<ThingDefCountClass>
					{
						new(ThingDefOf.ComponentIndustrial, 1)
					}
				};

				products = GenRecipe.MakeRecipeProducts(recipe, worker, new List<Thing> { ingredient }, ingredient, billGiver).ToArray();
				var product = products.FirstOrDefault();
				var ingredientAfter = ingredient.GetContamination();
				var productContamination = product?.GetContamination() ?? -1f;
				var expectedProductContamination = ingredientInputContamination * ZombieSettings.Values.contamination.receipeTransfer;
				var expectedIngredientContamination = ingredientInputContamination - expectedProductContamination;

				static bool CloseFloat(float value, float expected) => Mathf.Abs(value - expected) < 0.0001f;

				var ingredientTransferred = CloseFloat(ingredientBefore, ingredientInputContamination)
					&& CloseFloat(ingredientAfter, expectedIngredientContamination);
				var productReceived = products.Length == 1
					&& product?.Spawned == false
					&& product?.def == ThingDefOf.ComponentIndustrial
					&& CloseFloat(productContamination, expectedProductContamination);

				return new
				{
					success = ingredientTransferred && productReceived,
					worker = DescribePawn(worker),
					workerCell = ZombieRuntimeActions.DescribeCell(workerCell),
					ingredient = ZombieRuntimeActions.StableThingId(ingredient),
					ingredientCell = ZombieRuntimeActions.DescribeCell(ingredientCell),
					ingredientBefore,
					ingredientAfter,
					expectedIngredientContamination,
					product = ZombieRuntimeActions.StableThingId(product),
					productDef = product?.def?.defName,
					productSpawned = product?.Spawned,
					productContamination,
					expectedProductContamination,
					ingredientTransferred,
					productReceived
				};
			}
			finally
			{
				ZombieSettings.Values.contamination.receipeTransfer = oldRecipeTransfer;
				ZombieSettings.Values.contamination.produceEqualize = oldProduceEqualize;
				ZombieSettings.Values.contamination.benchEqualize = oldBenchEqualize;
				ZombieSettings.Values.contamination.workerTransfer = oldWorkerTransfer;
				ingredient.ClearContamination();
				bench.ClearContamination();
				foreach (var product in products)
					product.ClearContamination();
				if (ingredient is { Destroyed: false, Spawned: true })
					ingredient.Destroy();
				if (worker is { Destroyed: false, Spawned: true })
					worker.Destroy();
			}
		}

		[Tool("zombieland/contamination_building_products_contract", Description = "Verify building product handoffs transfer contamination from the producing building into returned products.")]
		public static object ContaminationBuildingProductsContract()
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			var fermentedBarrel = VerifyFermentingBarrelProductTransfer(map, root + new IntVec3(0, 0, 10));
			var geneAssembler = VerifyGeneAssemblerProductTransfer(map, root + new IntVec3(6, 0, 10));
			return new
			{
				success = ObjectSuccess(fermentedBarrel)
					&& ObjectSuccess(geneAssembler),
				fermentedBarrel,
				geneAssembler
			};
		}

		static object VerifyFermentingBarrelProductTransfer(Map map, IntVec3 root)
		{
			var patchTargets = PatchedMethodsForPatchClass("Misc_Building_Patch");
			var barrelDef = DefDatabase<ThingDef>.GetNamedSilentFail("FermentingBarrel");
			var beerDef = ThingDefOf.Beer;
			if (barrelDef == null || beerDef == null)
			{
				return new
				{
					success = false,
					targets = CompactPatchTargets(patchTargets),
					barrelDef = barrelDef?.defName,
					beerDef = beerDef?.defName,
					error = "FermentingBarrel or Beer def was unavailable."
				};
			}

			if (TryFindClearBuildingFootprint(map, barrelDef, root, 24f, out var barrelCell, out var barrelCellError) == false)
				return barrelCellError;

			var barrel = (Building_FermentingBarrel)null;
			var beer = (Thing)null;
			try
			{
				barrel = ThingMaker.MakeThing(barrelDef) as Building_FermentingBarrel;
				if (barrel == null)
				{
					return new
					{
						success = false,
						targets = CompactPatchTargets(patchTargets),
						barrelDef = barrelDef.defName,
						error = "FermentingBarrel def did not create Building_FermentingBarrel."
					};
				}

				GenSpawn.Spawn(barrel, barrelCell, map, Rot4.South, WipeMode.Vanish, false);
				var wortField = typeof(Building_FermentingBarrel).GetField("wortCount", BindingFlags.Instance | BindingFlags.NonPublic);
				var progressField = typeof(Building_FermentingBarrel).GetField("progressInt", BindingFlags.Instance | BindingFlags.NonPublic);
				if (wortField == null || progressField == null)
				{
					return new
					{
						success = false,
						targets = CompactPatchTargets(patchTargets),
						error = "FermentingBarrel private state fields were not found."
					};
				}

				const int wortCount = 17;
				const float barrelContamination = 0.66f;
				wortField.SetValue(barrel, wortCount);
				progressField.SetValue(barrel, 1f);
				barrel.SetContamination(barrelContamination);
				var barrelBefore = barrel.GetContamination();
				var fermentedBefore = barrel.Fermented;

				beer = barrel.TakeOutBeer();

				var barrelAfter = barrel.GetContamination();
				var beerContamination = beer?.GetContamination() ?? -1f;
				var beerStackCount = beer?.stackCount ?? 0;
				var expectedBeerContamination = barrelBefore - barrelAfter;
				var transferFactor = ZombieSettings.Values.contamination.fermentingBarrelTransfer;
				var nominalTransfer = barrelBefore * transferFactor;
				var fermentedAfter = barrel.Fermented;
				var productCreated = beer != null
					&& beer.Spawned == false
					&& beer.def == beerDef
					&& beerStackCount == wortCount;
				var contaminationTransferred = productCreated
					&& expectedBeerContamination > 0f
					&& Approximately(beerContamination, expectedBeerContamination);
				var barrelReset = fermentedBefore
					&& fermentedAfter == false
					&& (int)wortField.GetValue(barrel) == 0
					&& Approximately((float)progressField.GetValue(barrel), 0f);

				return new
				{
					success = patchTargets.Length > 0
						&& productCreated
						&& contaminationTransferred
						&& barrelReset,
					targets = CompactPatchTargets(patchTargets),
					method = "RimWorld.Building_FermentingBarrel::TakeOutBeer()",
					barrel = ZombieRuntimeActions.StableThingId(barrel),
					barrelCell = ZombieRuntimeActions.DescribeCell(barrelCell),
					barrelDef = barrel.def?.defName,
					barrelBefore,
					barrelAfter,
					transferFactor,
					nominalTransfer,
					beer = ZombieRuntimeActions.StableThingId(beer),
					beerDef = beer?.def?.defName,
					beerStackCount,
					beerSpawned = beer?.Spawned,
					beerContamination,
					expectedBeerContamination,
					fermentedBefore,
					fermentedAfter,
					productCreated,
					contaminationTransferred,
					barrelReset
				};
			}
			finally
			{
				barrel?.ClearContamination();
				beer?.ClearContamination();
				if (beer is { Destroyed: false, Spawned: true })
					beer.Destroy();
				if (barrel is { Destroyed: false, Spawned: true })
					barrel.Destroy();
			}
		}

		static object VerifyGeneAssemblerProductTransfer(Map map, IntVec3 root)
		{
			var patchTargets = PatchedMethodsForPatchClass("Misc_Building_Patch");
			var assemblerDef = DefDatabase<ThingDef>.GetNamedSilentFail("GeneAssembler");
			var genepackDef = ThingDefOf.Genepack;
			var xenogermDef = ThingDefOf.Xenogerm;
			var geneDef = DefDatabase<GeneDef>.AllDefsListForReading
				.OrderBy(def => def.defName)
				.FirstOrDefault();
			if (assemblerDef == null || genepackDef == null || xenogermDef == null || geneDef == null)
			{
				return new
				{
					success = true,
					skipped = true,
					reason = "GeneAssembler, Genepack, Xenogerm, or GeneDef is unavailable in the active configuration.",
					targets = CompactPatchTargets(patchTargets),
					assemblerDef = assemblerDef?.defName,
					genepackDef = genepackDef?.defName,
					xenogermDef = xenogermDef?.defName,
					geneDef = geneDef?.defName
				};
			}

			if (TryFindClearBuildingFootprint(map, assemblerDef, root, 24f, out var assemblerCell, out var assemblerCellError) == false)
				return assemblerCellError;

			var assembler = (Building_GeneAssembler)null;
			var genepack = (Genepack)null;
			var xenogerm = (Thing)null;
			try
			{
				assembler = ThingMaker.MakeThing(assemblerDef) as Building_GeneAssembler;
				genepack = ThingMaker.MakeThing(genepackDef) as Genepack;
				if (assembler == null || genepack == null)
				{
					return new
					{
						success = false,
						targets = CompactPatchTargets(patchTargets),
						assemblerDef = assemblerDef.defName,
						genepackDef = genepackDef.defName,
						assemblerCreated = assembler != null,
						genepackCreated = genepack != null,
						error = "Could not create GeneAssembler or Genepack fixture."
					};
				}

				GenSpawn.Spawn(assembler, assemblerCell, map, Rot4.South, WipeMode.Vanish, false);
				genepack.Initialize(new List<GeneDef> { geneDef });
				assembler.Start(new List<Genepack> { genepack }, 0, "Zombieland bridge xenogerm", XenotypeIconDefOf.Basic);

				const float assemblerContamination = 0.62f;
				assembler.SetContamination(assemblerContamination);
				var assemblerBefore = assembler.GetContamination();
				var existingXenogerms = map.listerThings.ThingsOfDef(xenogermDef).ToHashSet();
				var workingBefore = assembler.Working;

				assembler.Finish();

				xenogerm = map.listerThings.ThingsOfDef(xenogermDef)
					.FirstOrDefault(thing => existingXenogerms.Contains(thing) == false);
				var assemblerAfter = assembler.GetContamination();
				var productContamination = xenogerm?.GetContamination() ?? -1f;
				var expectedProductContamination = assemblerBefore - assemblerAfter;
				var transferFactor = ZombieSettings.Values.contamination.geneAssemblerTransfer;
				var nominalTransfer = assemblerBefore * transferFactor;
				var productCreated = xenogerm != null
					&& xenogerm.Spawned
					&& xenogerm.def == xenogermDef;
				var contaminationTransferred = productCreated
					&& expectedProductContamination > 0f
					&& Approximately(productContamination, expectedProductContamination);
				var reset = workingBefore
					&& assembler.Working == false
					&& assembler.xenotypeName == null
					&& assembler.iconDef == XenotypeIconDefOf.Basic;

				return new
				{
					success = patchTargets.Length > 0
						&& productCreated
						&& contaminationTransferred
						&& reset,
					targets = CompactPatchTargets(patchTargets),
					method = "RimWorld.Building_GeneAssembler::Finish()",
					assembler = ZombieRuntimeActions.StableThingId(assembler),
					assemblerCell = ZombieRuntimeActions.DescribeCell(assemblerCell),
					assemblerDef = assembler.def?.defName,
					assemblerBefore,
					assemblerAfter,
					transferFactor,
					nominalTransfer,
					genepack = ZombieRuntimeActions.StableThingId(genepack),
					genepackDef = genepack.def?.defName,
					geneDef = geneDef.defName,
					xenogerm = ZombieRuntimeActions.StableThingId(xenogerm),
					xenogermDef = xenogerm?.def?.defName,
					xenogermSpawned = xenogerm?.Spawned,
					xenogermPosition = xenogerm?.Spawned == true ? ZombieRuntimeActions.DescribeCell(xenogerm.Position) : null,
					productContamination,
					expectedProductContamination,
					workingBefore,
					workingAfter = assembler.Working,
					productCreated,
					contaminationTransferred,
					reset
				};
			}
			finally
			{
				assembler?.ClearContamination();
				genepack?.ClearContamination();
				xenogerm?.ClearContamination();
				if (xenogerm is { Destroyed: false, Spawned: true })
					xenogerm.Destroy();
				if (assembler is { Destroyed: false, Spawned: true })
					assembler.Destroy();
			}
		}

		[Tool("zombieland/contamination_biotech_product_handoffs_contract", Description = "Verify Biotech gene extractor, subcore scanner, and mech disassembly product handoffs transfer contamination.")]
		public static object ContaminationBiotechProductHandoffsContract()
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			var geneExtractor = VerifyGeneExtractorProductTransfer(map, root + new IntVec3(-8, 0, 8));
			var subcoreScanner = VerifySubcoreScannerProductTransfer(map, root + new IntVec3(0, 0, 8));
			var disassembleMech = VerifyDisassembleMechProductTransfer(map, root + new IntVec3(8, 0, 8));
			return new
			{
				success = ObjectSuccess(geneExtractor)
					&& ObjectSuccess(subcoreScanner)
					&& ObjectSuccess(disassembleMech),
				geneExtractor,
				subcoreScanner,
				disassembleMech
			};
		}

		static object VerifyGeneExtractorProductTransfer(Map map, IntVec3 root)
		{
			var patchTargets = PatchedMethodsForPatchClass("Building_GeneExtractor_Finish_Patch");
			var extractorDef = DefDatabase<ThingDef>.GetNamedSilentFail("GeneExtractor");
			var genepackDef = ThingDefOf.Genepack;
			var geneDef = DefDatabase<GeneDef>.AllDefsListForReading
				.Where(def => def.biostatArc == 0 && def.endogeneCategory != EndogeneCategory.Melanin && def.passOnDirectly)
				.OrderBy(def => def.defName)
				.FirstOrDefault();
			var finishMethod = typeof(Building_GeneExtractor).GetMethod("Finish", BindingFlags.Instance | BindingFlags.NonPublic);
			if (ModLister.BiotechInstalled == false || extractorDef == null || genepackDef == null || geneDef == null || finishMethod == null)
			{
				return new
				{
					success = patchTargets.Length > 0,
					skipped = true,
					targets = CompactPatchTargets(patchTargets),
					reason = "Biotech gene extractor product path cannot run in the active configuration.",
					biotechInstalled = ModLister.BiotechInstalled,
					extractorDef = extractorDef?.defName,
					genepackDef = genepackDef?.defName,
					geneDef = geneDef?.defName,
					finishMethodFound = finishMethod != null
				};
			}

			if (TryFindClearBuildingFootprint(map, extractorDef, root, 24f, out var extractorCell, out var extractorCellError) == false)
				return extractorCellError;
			if (TryFindClearSpawnCell(map, extractorCell + IntVec3.East, 8f, out var pawnCell, out var pawnCellError) == false)
				return pawnCellError;

			var extractor = (Building_GeneExtractor)null;
			var pawn = (Pawn)null;
			var genepack = (Thing)null;
			try
			{
				extractor = ThingMaker.MakeThing(extractorDef) as Building_GeneExtractor;
				if (extractor == null)
				{
					return new
					{
						success = false,
						targets = CompactPatchTargets(patchTargets),
						extractorDef = extractorDef.defName,
						error = "GeneExtractor def did not create Building_GeneExtractor."
					};
				}

				GenSpawn.Spawn(extractor, extractorCell, map, Rot4.South, WipeMode.Vanish, false);
				var powerComp = extractor.TryGetComp<CompPowerTrader>();
				if (powerComp != null)
					powerComp.PowerOn = true;
				pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(pawn, pawnCell, map, Rot4.West, WipeMode.Vanish);
				if (pawn.genes != null && pawn.genes.GenesListForReading.All(gene => gene.def != geneDef))
					pawn.genes.AddGene(geneDef, false);
				var geneCountBefore = pawn.genes?.GenesListForReading.Count ?? -1;
				var existingGenepacks = map.listerThings.ThingsOfDef(genepackDef).ToHashSet();

				extractor.TryAcceptPawn(pawn);
				var containedBeforeFinish = extractor.ContainedPawn != null;
				const float pawnContamination = 0.68f;
				pawn.SetContamination(pawnContamination);
				var pawnBefore = pawn.GetContamination(false);
				Rand.PushState(81643);
				try
				{
					finishMethod.Invoke(extractor, Array.Empty<object>());
				}
				finally
				{
					Rand.PopState();
				}

				genepack = map.listerThings.ThingsOfDef(genepackDef)
					.FirstOrDefault(thing => existingGenepacks.Contains(thing) == false);
				var pawnAfter = pawn.GetContamination(false);
				var productContamination = genepack?.GetContamination() ?? -1f;
				var expectedProductContamination = pawnBefore - pawnAfter;
				var transferFactor = ZombieSettings.Values.contamination.geneExtractorTransfer;
				var productCreated = genepack != null
					&& genepack.Spawned
					&& genepack.def == genepackDef;
				var contaminationTransferred = productCreated
					&& expectedProductContamination > 0f
					&& Approximately(productContamination, expectedProductContamination);

				return new
				{
					success = patchTargets.Length > 0
						&& containedBeforeFinish
						&& productCreated
						&& contaminationTransferred,
					targets = CompactPatchTargets(patchTargets),
					method = "RimWorld.Building_GeneExtractor::Finish()",
					extractor = ZombieRuntimeActions.StableThingId(extractor),
					extractorCell = ZombieRuntimeActions.DescribeCell(extractorCell),
					extractorDef = extractor.def?.defName,
					pawn = DescribePawn(pawn),
					pawnBefore,
					pawnAfter,
					geneDef = geneDef.defName,
					geneCountBefore,
					transferFactor,
					genepack = ZombieRuntimeActions.StableThingId(genepack),
					genepackDef = genepack?.def?.defName,
					genepackPosition = genepack?.Spawned == true ? ZombieRuntimeActions.DescribeCell(genepack.Position) : null,
					productContamination,
					expectedProductContamination,
					containedBeforeFinish,
					productCreated,
					contaminationTransferred
				};
			}
			finally
			{
				extractor?.ClearContamination();
				pawn?.ClearContamination();
				genepack?.ClearContamination();
				if (genepack is { Destroyed: false, Spawned: true })
					genepack.Destroy();
				if (pawn is { Destroyed: false, Spawned: true })
					pawn.Destroy();
				if (extractor is { Destroyed: false, Spawned: true })
					extractor.Destroy();
			}
		}

		static object VerifySubcoreScannerProductTransfer(Map map, IntVec3 root)
		{
			var patchTargets = PatchedMethodsForPatchClass("Building_SubcoreScanner_Tick_Patch");
			var scannerDef = DefDatabase<ThingDef>.GetNamedSilentFail("SubcoreSoftscanner")
				?? DefDatabase<ThingDef>.GetNamedSilentFail("SubcoreRipscanner");
			var outputDef = scannerDef?.building?.subcoreScannerOutputDef;
			var tickMethod = typeof(Building_SubcoreScanner).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic);
			var initScannerField = typeof(Building_SubcoreScanner).GetField("initScanner", BindingFlags.Instance | BindingFlags.NonPublic);
			var disableIngredientsField = typeof(Building_SubcoreScanner).GetField("debugDisableNeedForIngredients", BindingFlags.Instance | BindingFlags.NonPublic);
			var fabricationTicksField = typeof(Building_SubcoreScanner).GetField("fabricationTicksLeft", BindingFlags.Instance | BindingFlags.NonPublic);
			if (ModLister.BiotechInstalled == false || scannerDef == null || outputDef == null || tickMethod == null || initScannerField == null || disableIngredientsField == null || fabricationTicksField == null)
			{
				return new
				{
					success = patchTargets.Length > 0,
					skipped = true,
					targets = CompactPatchTargets(patchTargets),
					reason = "Biotech subcore scanner product path cannot run in the active configuration.",
					biotechInstalled = ModLister.BiotechInstalled,
					scannerDef = scannerDef?.defName,
					outputDef = outputDef?.defName,
					tickMethodFound = tickMethod != null,
					initScannerFieldFound = initScannerField != null,
					disableIngredientsFieldFound = disableIngredientsField != null,
					fabricationTicksFieldFound = fabricationTicksField != null
				};
			}

			if (TryFindClearBuildingFootprint(map, scannerDef, root, 24f, out var scannerCell, out var scannerCellError) == false)
				return scannerCellError;
			if (TryFindClearSpawnCell(map, scannerCell + IntVec3.East, 8f, out var occupantCell, out var occupantCellError) == false)
				return occupantCellError;

			var scanner = (Building_SubcoreScanner)null;
			var occupant = (Pawn)null;
			var product = (Thing)null;
			try
			{
				scanner = ThingMaker.MakeThing(scannerDef) as Building_SubcoreScanner;
				if (scanner == null)
				{
					return new
					{
						success = false,
						targets = CompactPatchTargets(patchTargets),
						scannerDef = scannerDef.defName,
						error = "Subcore scanner def did not create Building_SubcoreScanner."
					};
				}

				GenSpawn.Spawn(scanner, scannerCell, map, Rot4.South, WipeMode.Vanish, false);
				var powerComp = scanner.TryGetComp<CompPowerTrader>();
				if (powerComp != null)
					powerComp.PowerOn = true;
				initScannerField.SetValue(scanner, true);
				disableIngredientsField.SetValue(scanner, true);

				occupant = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(occupant, occupantCell, map, Rot4.West, WipeMode.Vanish);
				scanner.TryAcceptPawn(occupant);
				const float scannerContamination = 0.58f;
				scanner.SetContamination(scannerContamination);
				var scannerBefore = scanner.GetContamination();
				var existingProducts = map.listerThings.ThingsOfDef(outputDef).ToHashSet();
				var stateBefore = scanner.State.ToString();
				fabricationTicksField.SetValue(scanner, 1);

				tickMethod.Invoke(scanner, Array.Empty<object>());

				product = map.listerThings.ThingsOfDef(outputDef)
					.FirstOrDefault(thing => existingProducts.Contains(thing) == false);
				var scannerAfter = scanner.GetContamination();
				var productContamination = product?.GetContamination() ?? -1f;
				var expectedProductContamination = scannerBefore - scannerAfter;
				var transferFactor = ZombieSettings.Values.contamination.subcoreScannerTransfer;
				var productCreated = product != null
					&& product.Spawned
					&& product.def == outputDef;
				var contaminationTransferred = productCreated
					&& expectedProductContamination > 0f
					&& Approximately(productContamination, expectedProductContamination);

				return new
				{
					success = patchTargets.Length > 0
						&& stateBefore == SubcoreScannerState.Occupied.ToString()
						&& productCreated
						&& contaminationTransferred,
					targets = CompactPatchTargets(patchTargets),
					method = "RimWorld.Building_SubcoreScanner::Tick()",
					scanner = ZombieRuntimeActions.StableThingId(scanner),
					scannerCell = ZombieRuntimeActions.DescribeCell(scannerCell),
					scannerDef = scanner.def?.defName,
					stateBefore,
					stateAfter = scanner.State.ToString(),
					powerOn = scanner.PowerOn,
					occupant = DescribePawn(occupant),
					outputDef = outputDef.defName,
					product = ZombieRuntimeActions.StableThingId(product),
					productPosition = product?.Spawned == true ? ZombieRuntimeActions.DescribeCell(product.Position) : null,
					scannerBefore,
					scannerAfter,
					transferFactor,
					productContamination,
					expectedProductContamination,
					productCreated,
					contaminationTransferred
				};
			}
			finally
			{
				scanner?.ClearContamination();
				occupant?.ClearContamination();
				product?.ClearContamination();
				if (product is { Destroyed: false, Spawned: true })
					product.Destroy();
				if (occupant is { Destroyed: false, Spawned: true })
					occupant.Destroy();
				if (scanner is { Destroyed: false, Spawned: true })
					scanner.Destroy();
			}
		}

		static object VerifyDisassembleMechProductTransfer(Map map, IntVec3 root)
		{
			var patchTargets = PatchedMethodsForPatchClass("JobDriver_DisassembleMech_MakeNewToils_Patch");
			var disassembleJobDef = DefDatabase<JobDef>.GetNamedSilentFail("DisassembleMech");
			var mechKindDef = PawnKindDefOf.Mech_Scyther
				?? DefDatabase<PawnKindDef>.AllDefs.FirstOrDefault(def => def.race?.race?.IsMechanoid == true);
			if (ModLister.BiotechInstalled == false || disassembleJobDef == null || mechKindDef == null)
			{
				return new
				{
					success = patchTargets.Length > 0,
					skipped = true,
					targets = CompactPatchTargets(patchTargets),
					reason = "Biotech mech disassembly product path cannot run in the active configuration.",
					biotechInstalled = ModLister.BiotechInstalled,
					disassembleJobDef = disassembleJobDef?.defName,
					mechKindDef = mechKindDef?.defName
				};
			}

			if (TryFindClearSpawnCell(map, root, 16f, out var mechCell, out var mechCellError) == false)
				return mechCellError;
			if (TryFindClearSpawnCell(map, mechCell + IntVec3.East, 8f, out var workerCell, out var workerCellError) == false)
				return workerCellError;

			var worker = (Pawn)null;
			var mech = (Pawn)null;
			var products = new List<Thing>();
			try
			{
				worker = GenerateWorkCapableColonist(WorkTypeDefOf.Crafting, SkillDefOf.Crafting, 20);
				GenSpawn.Spawn(worker, workerCell, map, Rot4.West, WipeMode.Vanish);
				worker.ClearContamination();
				worker.jobs?.StopAll(false, true);
				worker.pather?.StopDead();

				var request = new PawnGenerationRequest(
					mechKindDef,
					Faction.OfPlayer,
					PawnGenerationContext.NonPlayer,
					forceGenerateNewPawn: true,
					canGeneratePawnRelations: false,
					colonistRelationChanceFactor: 0f,
					forceNoIdeo: true);
				mech = PawnGenerator.GeneratePawn(request);
				GenSpawn.Spawn(mech, mechCell, map, Rot4.East, WipeMode.Vanish);
				mech.ClearContamination();
				mech.jobs?.StopAll(false, true);
				mech.pather?.StopDead();

				var expectedIngredients = MechanitorUtility.IngredientsFromDisassembly(mech.def).ToArray();
				if (expectedIngredients.Length == 0)
				{
					return new
					{
						success = false,
						targets = CompactPatchTargets(patchTargets),
						mech = DescribePawn(mech),
						error = "Selected mech has no disassembly products."
					};
				}

				const float mechContamination = 0.74f;
				const float workerContamination = 0.12f;
				mech.SetContamination(mechContamination);
				worker.SetContamination(workerContamination);
				var workerBefore = worker.GetContamination(false);
				var mechBefore = mech.GetContamination(false);
				var existingThings = map.listerThings.AllThings.ToHashSet();
				var expectedDefs = expectedIngredients.Select(ingredient => ingredient.thingDef).ToHashSet();
				var job = JobMaker.MakeJob(disassembleJobDef, mech);
				job.playerForced = true;
				var started = worker.jobs.TryTakeOrderedJob(job, new JobTag?(JobTag.Misc), false);
				var tickHit = -1;
				var samples = new List<object>();

				for (var tick = 1; tick <= 420; tick++)
				{
					AdvanceGameTicks(1);
					products = map.listerThings.AllThings
						.Where(thing => existingThings.Contains(thing) == false)
						.Where(thing => expectedDefs.Contains(thing.def))
						.ToList();
					var mechConsumed = mech.Destroyed;
					if (tick == 1 || tick % 60 == 0 || mechConsumed || products.Count > 0)
					{
						samples.Add(new
						{
							tick,
							workerJob = worker.CurJobDef?.defName,
							mechDestroyed = mech.Destroyed,
							productCount = products.Count,
							workerContamination = worker.GetContamination(false),
							productContaminationTotal = products.Sum(thing => thing.GetContamination())
						});
					}
					if (mechConsumed && products.Count > 0)
					{
						tickHit = tick;
						break;
					}
				}

				var workerAfter = worker.GetContamination(false);
				var productContaminationTotal = products.Sum(thing => thing.GetContamination());
				var expectedWorkerGain = productContaminationTotal;
				var workerGain = workerAfter - workerBefore;
				var contaminationTransferred = products.Count > 0
					&& productContaminationTotal > 0f
					&& workerGain > 0f
					&& Approximately(workerGain, expectedWorkerGain, 0.0001f);

				return new
				{
					success = patchTargets.Length > 0
						&& started
						&& tickHit > 0
						&& mech.Destroyed
						&& products.Count > 0
						&& contaminationTransferred,
					targets = CompactPatchTargets(patchTargets),
					method = "RimWorld.JobDriver_DisassembleMech::<MakeNewToils>b__placedAction",
					worker = DescribePawn(worker),
					workerCell = ZombieRuntimeActions.DescribeCell(workerCell),
					mech = DescribePawn(mech),
					mechCell = ZombieRuntimeActions.DescribeCell(mechCell),
					mechKindDef = mechKindDef.defName,
					disassembleJobDef = disassembleJobDef.defName,
					started,
					tickHit,
					mechBefore,
					workerBefore,
					workerAfter,
					workerGain,
					expectedWorkerGain,
					transferFactor = ZombieSettings.Values.contamination.disassembleTransfer,
					expectedProducts = expectedIngredients.Select(ingredient => new
					{
						defName = ingredient.thingDef?.defName,
						count = ingredient.count
					}).ToArray(),
					products = products.Select(thing => new
					{
						id = ZombieRuntimeActions.StableThingId(thing),
						defName = thing.def?.defName,
						stackCount = thing.stackCount,
						contamination = thing.GetContamination(),
						position = thing.Spawned ? ZombieRuntimeActions.DescribeCell(thing.Position) : null
					}).ToArray(),
					productContaminationTotal,
					contaminationTransferred,
					samples
				};
			}
			finally
			{
				worker?.ClearContamination();
				mech?.ClearContamination();
				foreach (var product in products)
				{
					product?.ClearContamination();
					if (product is { Destroyed: false, Spawned: true })
						product.Destroy();
				}
				if (worker is { Destroyed: false, Spawned: true })
					worker.Destroy();
				if (mech is { Destroyed: false, Spawned: true })
					mech.Destroy();
			}
		}

		[Tool("zombieland/contamination_repair_contract", Description = "Verify real repair jobs transfer contamination through patched repair handoff points.")]
		public static object ContaminationRepairContract()
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			var buildingRepair = VerifyBuildingRepairTransfer(map, root + new IntVec3(0, 0, 14));
			var mechRepair = VerifyMechRepairTransfer(map, root + new IntVec3(0, 0, 20));
			return new
			{
				success = ObjectSuccess(buildingRepair)
					&& ObjectSuccess(mechRepair),
				buildingRepair,
				mechRepair
			};
		}

		static Pawn GenerateWorkCapableColonist(WorkTypeDef workType, SkillDef skillDef, int skillLevel)
		{
			for (var attempt = 0; attempt < 24; attempt++)
			{
				var candidate = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				if (workType == null || candidate.WorkTypeIsDisabled(workType) == false)
				{
					candidate.workSettings?.EnableAndInitializeIfNotAlreadyInitialized();
					if (workType != null)
						candidate.workSettings?.SetPriority(workType, 1);
					if (skillDef != null)
						candidate.skills?.GetSkill(skillDef).Level = skillLevel;
					return candidate;
				}

				candidate.Destroy();
			}

			var fallback = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			fallback.workSettings?.EnableAndInitializeIfNotAlreadyInitialized();
			if (workType != null && fallback.WorkTypeIsDisabled(workType) == false)
				fallback.workSettings?.SetPriority(workType, 1);
			if (skillDef != null)
				fallback.skills?.GetSkill(skillDef).Level = skillLevel;
			return fallback;
		}

		static object VerifyBuildingRepairTransfer(Map map, IntVec3 root)
		{
			var patchTargets = PatchedMethodsForPatchClass("JobDriver_Repair_MakeNewToils_Patch");
			var repairJobDef = JobDefOf.Repair;
			var buildingDef = ThingDefOf.Wall;
			var stuffDef = ThingDefOf.WoodLog;
			if (repairJobDef == null || buildingDef == null || stuffDef == null)
			{
				return new
				{
					success = false,
					targets = CompactPatchTargets(patchTargets),
					error = "Repair job, Wall, or WoodLog def was unavailable."
				};
			}

			if (TryFindClearBuildingFootprint(map, buildingDef, root, 24f, out var buildingCell, out var buildingCellError) == false)
				return buildingCellError;
			if (TryFindClearSpawnCell(map, buildingCell + IntVec3.East, 8f, out var workerCell, out var workerCellError) == false)
				return workerCellError;

			var worker = (Pawn)null;
			var building = (Building)null;
			try
			{
				worker = GenerateWorkCapableColonist(WorkTypeDefOf.Construction, SkillDefOf.Construction, 20);
				GenSpawn.Spawn(worker, workerCell, map, Rot4.West, WipeMode.Vanish);
				worker.ClearContamination();
				worker.jobs?.StopAll(false, true);
				worker.pather?.StopDead();

				building = ThingMaker.MakeThing(buildingDef, stuffDef) as Building;
				if (building == null)
				{
					return new
					{
						success = false,
						targets = CompactPatchTargets(patchTargets),
						error = "Could not create repair building fixture."
					};
				}

				building.SetFactionDirect(Faction.OfPlayer);
				GenSpawn.Spawn(building, buildingCell, map, Rot4.South, WipeMode.Vanish, false);
				var maxHitPoints = building.MaxHitPoints;
				building.HitPoints = Mathf.Max(1, maxHitPoints - 1);
				const float workerContamination = 0.15f;
				const float buildingContamination = 0.75f;
				worker.SetContamination(workerContamination);
				building.SetContamination(buildingContamination);

				var workerBefore = worker.GetContamination(false);
				var buildingBefore = building.GetContamination();
				var hitPointsBefore = building.HitPoints;
				var repairTransfer = ZombieSettings.Values.contamination.repairTransfer;
				var expectedBuildingAfter = buildingBefore * (1f - repairTransfer) + workerBefore * repairTransfer;
				var expectedWorkerAfter = workerBefore + (buildingBefore - expectedBuildingAfter);
				var job = JobMaker.MakeJob(repairJobDef, building);
				job.playerForced = true;
				var started = worker.jobs.TryTakeOrderedJob(job, new JobTag?(JobTag.Misc), false);
				var tickHit = -1;
				var samples = new List<object>();

				for (var tick = 1; tick <= 180; tick++)
				{
					AdvanceGameTicks(1);
					var repaired = building.HitPoints == maxHitPoints;
					if (tick == 1 || tick % 30 == 0 || repaired)
					{
						samples.Add(new
						{
							tick,
							workerJob = worker.CurJobDef?.defName,
							hitPoints = building.HitPoints,
							workerContamination = worker.GetContamination(false),
							buildingContamination = building.GetContamination()
						});
					}
					if (repaired)
					{
						tickHit = tick;
						break;
					}
				}

				var workerAfter = worker.GetContamination(false);
				var buildingAfter = building.GetContamination();
				var hitPointsAfter = building.HitPoints;
				var repairedToFull = hitPointsBefore < maxHitPoints && hitPointsAfter == maxHitPoints;
				var contaminationTransferred = repairedToFull
					&& Approximately(workerAfter, expectedWorkerAfter)
					&& Approximately(buildingAfter, expectedBuildingAfter);

				return new
				{
					success = patchTargets.Length > 0
						&& started
						&& tickHit > 0
						&& repairedToFull
						&& contaminationTransferred,
					targets = CompactPatchTargets(patchTargets),
					method = "RimWorld.JobDriver_Repair::<MakeNewToils>b__tickAction",
					worker = DescribePawn(worker),
					workerCell = ZombieRuntimeActions.DescribeCell(workerCell),
					building = ZombieRuntimeActions.StableThingId(building),
					buildingCell = ZombieRuntimeActions.DescribeCell(buildingCell),
					buildingDef = building.def?.defName,
					stuffDef = building.Stuff?.defName,
					started,
					tickHit,
					hitPointsBefore,
					hitPointsAfter,
					maxHitPoints,
					repairTransfer,
					workerBefore,
					workerAfter,
					expectedWorkerAfter,
					buildingBefore,
					buildingAfter,
					expectedBuildingAfter,
					repairedToFull,
					contaminationTransferred,
					samples
				};
			}
			finally
			{
				worker?.ClearContamination();
				building?.ClearContamination();
				if (worker is { Destroyed: false, Spawned: true })
					worker.Destroy();
				if (building is { Destroyed: false, Spawned: true })
					building.Destroy();
			}
		}

		static object VerifyMechRepairTransfer(Map map, IntVec3 root)
		{
			var patchTargets = PatchedMethodsForPatchClass("JobDriver_RepairMech_MakeNewToils_Patch");
			var repairJobDef = JobDefOf.RepairMech;
			var mechKindDef = PawnKindDefOf.Mech_Scyther
				?? DefDatabase<PawnKindDef>.AllDefs.FirstOrDefault(def => def.race?.race?.IsMechanoid == true);
			if (ModLister.BiotechInstalled == false || repairJobDef == null || mechKindDef == null)
			{
				return new
				{
					success = patchTargets.Length > 0,
					skipped = true,
					targets = CompactPatchTargets(patchTargets),
					reason = "Biotech mech repair cannot run in the active configuration.",
					biotechInstalled = ModLister.BiotechInstalled,
					repairJobDef = repairJobDef?.defName,
					mechKindDef = mechKindDef?.defName
				};
			}

			if (TryFindClearSpawnCell(map, root, 16f, out var mechCell, out var mechCellError) == false)
				return mechCellError;
			if (TryFindClearSpawnCell(map, mechCell + IntVec3.East, 8f, out var workerCell, out var workerCellError) == false)
				return workerCellError;

			var worker = (Pawn)null;
			var mech = (Pawn)null;
			Hediff_Injury wound = null;
			try
			{
				worker = GenerateWorkCapableColonist(WorkTypeDefOf.Crafting, SkillDefOf.Crafting, 20);
				GenSpawn.Spawn(worker, workerCell, map, Rot4.West, WipeMode.Vanish);
				worker.ClearContamination();
				worker.jobs?.StopAll(false, true);
				worker.pather?.StopDead();

				var request = new PawnGenerationRequest(
					mechKindDef,
					Faction.OfPlayer,
					PawnGenerationContext.NonPlayer,
					forceGenerateNewPawn: true,
					canGeneratePawnRelations: false,
					colonistRelationChanceFactor: 0f,
					forceNoIdeo: true);
				mech = PawnGenerator.GeneratePawn(request);
				GenSpawn.Spawn(mech, mechCell, map, Rot4.East, WipeMode.Vanish);
				mech.ClearContamination();
				mech.jobs?.StopAll(false, true);
				mech.pather?.StopDead();
				if (mech.needs?.energy != null)
					mech.needs.energy.CurLevel = Mathf.Max(mech.needs.energy.CurLevel, 0.5f);

				wound = CreateMechRepairWound(mech, 1f);
				if (wound == null)
				{
					return new
					{
						success = false,
						targets = CompactPatchTargets(patchTargets),
						mech = DescribePawn(mech),
						error = "Could not create a repairable mech wound."
					};
				}

				const float workerContamination = 0.15f;
				const float mechContamination = 0.75f;
				worker.SetContamination(workerContamination);
				mech.SetContamination(mechContamination);

				var workerBefore = worker.GetContamination(false);
				var mechBefore = mech.GetContamination(false);
				var woundBefore = wound.Severity;
				var repairTransfer = ZombieSettings.Values.contamination.repairTransfer;
				var expectedMechAfter = mechBefore * (1f - repairTransfer) + workerBefore * repairTransfer;
				var expectedWorkerAfter = workerBefore + (mechBefore - expectedMechAfter);
				var job = JobMaker.MakeJob(repairJobDef, mech);
				job.playerForced = true;
				var started = worker.jobs.TryTakeOrderedJob(job, new JobTag?(JobTag.Misc), false);
				var tickHit = -1;
				var samples = new List<object>();

				for (var tick = 1; tick <= 600; tick++)
				{
					AdvanceGameTicks(1);
					var woundPresent = wound != null && mech.health.hediffSet.hediffs.Contains(wound);
					var woundSeverity = woundPresent ? wound.Severity : 0f;
					var repaired = woundPresent == false || woundSeverity < woundBefore;
					if (tick == 1 || tick % 60 == 0 || repaired)
					{
						samples.Add(new
						{
							tick,
							workerJob = worker.CurJobDef?.defName,
							woundPresent,
							woundSeverity,
							workerContamination = worker.GetContamination(false),
							mechContamination = mech.GetContamination(false)
						});
					}
					if (repaired)
					{
						tickHit = tick;
						break;
					}
				}

				var workerAfter = worker.GetContamination(false);
				var mechAfter = mech.GetContamination(false);
				var woundStillPresent = wound != null && mech.health.hediffSet.hediffs.Contains(wound);
				var woundAfter = woundStillPresent ? wound.Severity : 0f;
				var woundRepaired = woundStillPresent == false || woundAfter < woundBefore;
				var contaminationTransferred = woundRepaired
					&& Approximately(workerAfter, expectedWorkerAfter)
					&& Approximately(mechAfter, expectedMechAfter);

				return new
				{
					success = patchTargets.Length > 0
						&& started
						&& tickHit > 0
						&& woundRepaired
						&& contaminationTransferred,
					targets = CompactPatchTargets(patchTargets),
					method = "RimWorld.JobDriver_RepairMech::<MakeNewToils>b__tickIntervalAction",
					worker = DescribePawn(worker),
					workerCell = ZombieRuntimeActions.DescribeCell(workerCell),
					mech = DescribePawn(mech),
					mechCell = ZombieRuntimeActions.DescribeCell(mechCell),
					mechKindDef = mechKindDef.defName,
					started,
					tickHit,
					woundBefore,
					woundAfter,
					woundPresent = woundStillPresent,
					repairTransfer,
					workerBefore,
					workerAfter,
					expectedWorkerAfter,
					mechBefore,
					mechAfter,
					expectedMechAfter,
					woundRepaired,
					contaminationTransferred,
					samples
				};
			}
			finally
			{
				worker?.ClearContamination();
				mech?.ClearContamination();
				if (worker is { Destroyed: false, Spawned: true })
					worker.Destroy();
				if (mech is { Destroyed: false, Spawned: true })
					mech.Destroy();
			}
		}

		static Hediff_Injury CreateMechRepairWound(Pawn mech, float severity)
		{
			var part = mech.health?.hediffSet
				.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Outside)
				.FirstOrDefault(record => record.def.IsSolid(record, mech.health.hediffSet.hediffs) == false)
				?? mech.health?.hediffSet.GetNotMissingParts().FirstOrDefault(record => record.def.alive)
				?? mech.health?.hediffSet.GetNotMissingParts().FirstOrDefault();
			if (part == null)
				return null;

			var wound = (Hediff_Injury)HediffMaker.MakeHediff(HediffDefOf.Cut, mech, part);
			wound.Severity = severity;
			mech.health.AddHediff(wound, part, new DamageInfo(DamageDefOf.Cut, severity));
			return wound;
		}

		[Tool("zombieland/contamination_nutrient_paste_contract", Description = "Verify contaminated hopper feed transfers through real Building_NutrientPasteDispenser.TryDispenseFood into the produced meal.")]
		public static object ContaminationNutrientPasteContract()
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var dispenserDef = DefDatabase<ThingDef>.GetNamedSilentFail("NutrientPasteDispenser");
			var hopperDef = DefDatabase<ThingDef>.GetNamedSilentFail("Hopper");
			var feedDef = ThingDefOf.MealSurvivalPack;
			if (dispenserDef == null || hopperDef == null || feedDef == null)
			{
				return new
				{
					success = false,
					error = "NutrientPasteDispenser, Hopper, or MealSurvivalPack is unavailable."
				};
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearBuildingFootprint(map, dispenserDef, root, 24f, out var dispenserCell, out var dispenserCellError) == false)
				return dispenserCellError;

			var dispenser = (Building_NutrientPasteDispenser)null;
			var hopper = (Thing)null;
			var feed = (Thing)null;
			var meal = (Thing)null;
			try
			{
				dispenser = ThingMaker.MakeThing(dispenserDef) as Building_NutrientPasteDispenser;
				if (dispenser == null)
				{
					return new
					{
						success = false,
						error = "Could not create NutrientPasteDispenser fixture."
					};
				}
				dispenser.SetFactionDirect(Faction.OfPlayer);
				GenSpawn.Spawn(dispenser, dispenserCell, map, Rot4.North, WipeMode.Vanish, false);

				var hopperCell = dispenser.AdjCellsCardinalInBounds.FirstOrDefault(cell =>
					cell.InBounds(map)
					&& cell.Fogged(map) == false
					&& cell.GetEdifice(map) == null
					&& cell.GetThingList(map).Any(thing => thing is Pawn || thing is Blueprint || thing is Frame || thing.def.category == ThingCategory.Building) == false);
				if (hopperCell.IsValid == false)
				{
					return new
					{
						success = false,
						error = "Could not find a clear adjacent hopper cell for the dispenser fixture."
					};
				}

				hopper = ThingMaker.MakeThing(hopperDef);
				hopper.SetFactionDirect(Faction.OfPlayer);
				GenSpawn.Spawn(hopper, hopperCell, map, Rot4.North, WipeMode.Vanish, false);

				feed = ThingMaker.MakeThing(feedDef);
				feed.stackCount = 2;
				GenSpawn.Spawn(feed, hopperCell, map, Rot4.North, WipeMode.Vanish, false);

				var powerComp = dispenser.GetComp<CompPowerTrader>();
				if (powerComp == null)
				{
					return new
					{
						success = false,
						error = "Spawned dispenser has no CompPowerTrader."
					};
				}
				powerComp.PowerOn = true;

				const float feedContamination = 0.6f;
				feed.SetContamination(feedContamination);
				var feedBefore = feed.GetContamination();
				var stackBefore = feed.stackCount;
				var canDispense = dispenser.CanDispenseNow;
				var transferFactor = ZombieSettings.Values.contamination.dispenseFoodTransfer;

				meal = dispenser.TryDispenseFood();

				var feedAfter = feed.GetContamination();
				var stackAfter = feed.Destroyed ? 0 : feed.stackCount;
				var mealContamination = meal?.GetContamination() ?? -1f;
				var expectedMealContamination = feedContamination * transferFactor;
				var activeFeedField = typeof(Building_NutrientPasteDispenser_TryDispenseFood_Patch).GetField("activeFeed", BindingFlags.Static | BindingFlags.NonPublic);
				var activeFeedCleared = activeFeedField?.GetValue(null) == null;

				static bool CloseFloat(float value, float expected) => Mathf.Abs(value - expected) < 0.0001f;
				var mealProduced = canDispense
					&& meal != null
					&& meal.Spawned == false
					&& meal.def == ThingDefOf.MealNutrientPaste
					&& stackBefore == 2
					&& stackAfter == 1;
				var contaminationTransferred = mealProduced
					&& CloseFloat(feedBefore, feedContamination)
					&& CloseFloat(feedAfter, feedContamination)
					&& CloseFloat(mealContamination, expectedMealContamination);

				return new
				{
					success = mealProduced && contaminationTransferred && activeFeedCleared,
					dispenser = ZombieRuntimeActions.StableThingId(dispenser),
					dispenserCell = ZombieRuntimeActions.DescribeCell(dispenserCell),
					hopper = ZombieRuntimeActions.StableThingId(hopper),
					hopperCell = ZombieRuntimeActions.DescribeCell(hopperCell),
					powerOn = powerComp.PowerOn,
					canDispense,
					feed = ZombieRuntimeActions.StableThingId(feed),
					feedDef = feed.def.defName,
					stackBefore,
					stackAfter,
					feedBefore,
					feedAfter,
					meal = ZombieRuntimeActions.StableThingId(meal),
					mealDef = meal?.def?.defName,
					mealSpawned = meal?.Spawned,
					mealContamination,
					expectedMealContamination,
					transferFactor,
					activeFeedCleared,
					mealProduced,
					contaminationTransferred
				};
			}
			finally
			{
				meal?.ClearContamination();
				if (meal is { Destroyed: false, Spawned: true })
					meal.Destroy();
				feed?.ClearContamination();
				if (feed is { Destroyed: false, Spawned: true })
					feed.Destroy();
				if (hopper is { Destroyed: false, Spawned: true })
					hopper.Destroy();
				if (dispenser is { Destroyed: false, Spawned: true })
					dispenser.Destroy();
			}
		}

		[Tool("zombieland/contamination_frame_construction_contract", Description = "Verify contaminated frame materials transfer through real Frame.CompleteConstruction into the final building and worker.")]
		public static object ContaminationFrameConstructionContract(
			[ToolParameter(Description = "Destroy the completed building before returning.", Required = false, DefaultValue = true)] bool cleanup = true)
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var wallDef = ThingDefOf.Wall;
			var stuffDef = ThingDefOf.WoodLog;
			var frameDef = wallDef?.frameDef;
			if (wallDef == null || stuffDef == null || frameDef == null)
			{
				return new
				{
					success = false,
					error = "Wall, WoodLog, or Wall.frameDef is unavailable."
				};
			}

			bool TryFindFrameCell(IntVec3 root, float radius, out IntVec3 cell, out object error)
			{
				cell = IntVec3.Invalid;
				error = null;
				foreach (var candidate in GenRadial.RadialCellsAround(root, radius, true))
				{
					if (candidate.InBounds(map) == false)
						continue;
					if (candidate.Standable(map) == false)
						continue;
					if (candidate.Fogged(map))
						continue;
					if (candidate.GetEdifice(map) != null)
						continue;
					if (candidate.GetThingList(map).Any(thing =>
						thing is Pawn
						|| thing is Blueprint
						|| thing is Frame
						|| thing.def.category == ThingCategory.Plant
						|| thing.def.category == ThingCategory.Building
						|| thing.def.passability == Traversability.Impassable))
						continue;

					cell = candidate;
					return true;
				}

				error = new
				{
					success = false,
					error = $"No clear 1x1 frame fixture cell was found near ({root.x}, {root.z})."
				};
				return false;
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var workerCell, out var workerSpawnError) == false)
				return workerSpawnError;
			if (TryFindFrameCell(workerCell + new IntVec3(3, 0, 0), 16f, out var frameCell, out var frameCellError) == false)
				return frameCellError;

			var worker = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var frame = ThingMaker.MakeThing(frameDef, stuffDef) as Frame;
			var material = ThingMaker.MakeThing(stuffDef);
			Thing finalThing = null;
			try
			{
				GenSpawn.Spawn(worker, workerCell, map, Rot4.South);
				DisablePawnWork(worker);
				worker.needs?.AddOrRemoveNeedsAsAppropriate();
				worker.ClearContamination();

				if (frame == null)
				{
					return new
					{
						success = false,
						error = "Could not create a wall frame fixture."
					};
				}
				frame.SetFactionDirect(Faction.OfPlayer);
				GenSpawn.Spawn(frame, frameCell, map, Rot4.South, WipeMode.Vanish, false);

				const float materialContamination = 0.8f;
				material.stackCount = 10;
				material.AddContamination(materialContamination, (sbyte)map.Index);
				var materialBefore = material.GetContamination();
				var acceptedMaterial = frame.resourceContainer.TryAdd(material, canMergeWithExistingStacks: true);
				var frameMaterialCount = frame.resourceContainer.Count;
				var frameMaterialContamination = frame.resourceContainer.Sum(thing => thing.GetContamination());
				var workerBefore = worker.GetContamination();

				frame.CompleteConstruction(worker);
				finalThing = frameCell.GetEdifice(map);
				var frameDestroyed = frame.Destroyed;
				var materialDestroyed = material.Destroyed;
				var finalContamination = finalThing?.GetContamination() ?? -1f;
				var workerAfter = worker.GetContamination();
				var expectedFinalContamination = materialContamination
					* ZombieSettings.Values.contamination.constructionAdd
					* (1f - ZombieSettings.Values.contamination.constructionTransfer);
				var expectedWorkerContamination = materialContamination
					* ZombieSettings.Values.contamination.constructionAdd
					* ZombieSettings.Values.contamination.constructionTransfer;

				static bool CloseFloat(float value, float expected) => Mathf.Abs(value - expected) < 0.0001f;

				var materialCaptured = acceptedMaterial
					&& frameMaterialCount == 1
					&& CloseFloat(materialBefore, materialContamination)
					&& CloseFloat(frameMaterialContamination, materialContamination);
				var finalBuilt = frameDestroyed
					&& materialDestroyed
					&& finalThing != null
					&& finalThing.def == wallDef
					&& finalThing.Stuff == stuffDef;
				var contaminationTransferred = finalBuilt
					&& CloseFloat(workerBefore, 0f)
					&& CloseFloat(finalContamination, expectedFinalContamination)
					&& CloseFloat(workerAfter, expectedWorkerContamination);

				return new
				{
					success = materialCaptured && finalBuilt && contaminationTransferred,
					cleanup,
					worker = DescribePawn(worker),
					workerCell = ZombieRuntimeActions.DescribeCell(workerCell),
					frameCell = ZombieRuntimeActions.DescribeCell(frameCell),
					frame = ZombieRuntimeActions.StableThingId(frame),
					frameDestroyed,
					material = ZombieRuntimeActions.StableThingId(material),
					materialDestroyed,
					acceptedMaterial,
					frameMaterialCount,
					materialBefore,
					frameMaterialContamination,
					finalThing = ZombieRuntimeActions.StableThingId(finalThing),
					finalState = DescribeContaminationThing(finalThing),
					finalDef = finalThing?.def?.defName,
					finalStuff = finalThing?.Stuff?.defName,
					finalContamination,
					expectedFinalContamination,
					workerBefore,
					workerAfter,
					expectedWorkerContamination,
					constructionAdd = ZombieSettings.Values.contamination.constructionAdd,
					constructionTransfer = ZombieSettings.Values.contamination.constructionTransfer,
					materialCaptured,
					finalBuilt,
					contaminationTransferred
				};
			}
			finally
			{
				material.ClearContamination();
				frame?.ClearContamination();
				worker?.ClearContamination();
				if (cleanup)
					finalThing?.ClearContamination();
				if (cleanup && finalThing is { Destroyed: false, Spawned: true })
					finalThing.Destroy();
				if (frame is { Destroyed: false, Spawned: true })
					frame.Destroy();
				if (material is { Destroyed: false, Spawned: true })
					material.Destroy();
				if (worker is { Destroyed: false, Spawned: true })
					worker.Destroy();
			}
		}

		[Tool("zombieland/contamination_terrain_construction_contract", Description = "Verify contaminated frame materials transfer through real terrain Frame.CompleteConstruction into normal floors and foundations.")]
		public static object ContaminationTerrainConstructionContract(
			[ToolParameter(Description = "Restore terrain and contamination cells before returning.", Required = false, DefaultValue = true)] bool cleanup = true)
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var woodFloor = DefDatabase<TerrainDef>.GetNamedSilentFail("WoodPlankFloor");
			var bridge = DefDatabase<TerrainDef>.GetNamedSilentFail("Bridge");
			var shallowWater = DefDatabase<TerrainDef>.GetNamedSilentFail("WaterShallow");
			var temporaryTerrains = DefDatabase<TerrainDef>.AllDefsListForReading
				.Where(def => def.temporary && def.frameDef != null)
				.OrderBy(def => def.defName)
				.ToArray();
			var temporaryTerrain = temporaryTerrains.FirstOrDefault(def => def.CostList.NullOrEmpty() == false && def.CostList.Any(cost => cost.thingDef != null && cost.count > 0))
				?? temporaryTerrains.FirstOrDefault();
			var stuffDef = ThingDefOf.WoodLog;
			if (woodFloor?.frameDef == null || bridge?.frameDef == null || shallowWater == null || stuffDef == null)
			{
				return new
				{
					success = false,
					error = "WoodPlankFloor, Bridge, WaterShallow, their frame defs, or WoodLog is unavailable."
				};
			}

			bool TryFindTerrainFrameCell(IntVec3 root, float radius, out IntVec3 cell, out object error)
			{
				cell = IntVec3.Invalid;
				error = null;
				foreach (var candidate in GenRadial.RadialCellsAround(root, radius, true))
				{
					if (candidate.InBounds(map) == false)
						continue;
					if (candidate.Fogged(map))
						continue;
					if (candidate.GetEdifice(map) != null)
						continue;
					if (map.terrainGrid.FoundationAt(candidate) != null)
						continue;
					if (map.terrainGrid.TempTerrainAt(candidate) != null)
						continue;
					if (candidate.GetThingList(map).Any(thing =>
						thing is Pawn
						|| thing is Blueprint
						|| thing is Frame
						|| thing.def.category == ThingCategory.Plant
						|| thing.def.category == ThingCategory.Building
						|| thing.def.passability == Traversability.Impassable))
						continue;

					cell = candidate;
					return true;
				}

				error = new
				{
					success = false,
					error = $"No clear terrain frame fixture cell was found near ({root.x}, {root.z})."
				};
				return false;
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var workerCell, out var workerSpawnError) == false)
				return workerSpawnError;
			if (TryFindTerrainFrameCell(workerCell + new IntVec3(4, 0, 0), 16f, out var floorCell, out var floorCellError) == false)
				return floorCellError;
			if (TryFindTerrainFrameCell(floorCell + new IntVec3(4, 0, 0), 16f, out var bridgeCell, out var bridgeCellError) == false)
				return bridgeCellError;
			var tempCell = IntVec3.Invalid;
			if (temporaryTerrain != null && TryFindTerrainFrameCell(bridgeCell + new IntVec3(4, 0, 0), 16f, out tempCell, out var tempCellError) == false)
				return tempCellError;

			static bool CloseFloat(float value, float expected) => Mathf.Abs(value - expected) < 0.0001f;

			var worker = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var touchedCells = new List<(IntVec3 cell, TerrainDef top, TerrainDef foundation, TerrainDef temp, float contamination)>();
			var createdThings = new List<Thing>();
			try
			{
				void SnapshotTerrain(IntVec3 cell)
				{
					touchedCells.Add((cell, map.terrainGrid.TerrainAt(cell), map.terrainGrid.FoundationAt(cell), map.terrainGrid.TempTerrainAt(cell), map.GetContamination(cell)));
				}

				(bool success, object payload) CompleteTerrainFrame(TerrainDef terrainDef, IntVec3 cell, float materialContamination, TerrainDef underTerrain = null, ThingDef materialDef = null)
				{
					SnapshotTerrain(cell);
					if (underTerrain != null)
						map.terrainGrid.SetTerrain(cell, underTerrain);

					var frameMaterialDef = materialDef ?? stuffDef;
					var frame = ThingMaker.MakeThing(terrainDef.frameDef) as Frame;
					var material = ThingMaker.MakeThing(frameMaterialDef);
					createdThings.Add(frame);
					createdThings.Add(material);

					if (frame == null || material == null)
					{
						var errorPayload = new
						{
							success = false,
							error = $"Could not create terrain frame fixture for {terrainDef.defName}."
						};
						return (false, errorPayload);
					}

					frame.SetFactionDirect(Faction.OfPlayer);
					GenSpawn.Spawn(frame, cell, map, Rot4.South, WipeMode.Vanish, false);

					material.stackCount = Mathf.Max(1, terrainDef.CostList?.FirstOrDefault(cost => cost.thingDef == frameMaterialDef)?.count ?? 1);
					material.SetContamination(materialContamination);
					var materialBefore = material.GetContamination();
					var acceptedMaterial = frame.resourceContainer.TryAdd(material, canMergeWithExistingStacks: true);
					var frameMaterialCount = frame.resourceContainer.Count;
					var frameMaterialContamination = frame.resourceContainer.Sum(thing => thing.GetContamination());
					var groundBefore = map.GetContamination(cell);
					var oldTop = map.terrainGrid.TerrainAt(cell);
					var oldFoundation = map.terrainGrid.FoundationAt(cell);
					var oldTemp = map.terrainGrid.TempTerrainAt(cell);

					frame.CompleteConstruction(worker);

					var frameDestroyed = frame.Destroyed;
					var materialDestroyed = material.Destroyed;
					var topAfter = map.terrainGrid.TerrainAt(cell);
					var foundationAfter = map.terrainGrid.FoundationAt(cell);
					var tempAfter = map.terrainGrid.TempTerrainAt(cell);
					var groundAfter = map.GetContamination(cell);

					var builtTerrain = terrainDef.isFoundation
						? foundationAfter == terrainDef
						: terrainDef.temporary
							? tempAfter == terrainDef
							: topAfter == terrainDef;
					var materialCaptured = acceptedMaterial
						&& frameMaterialCount == 1
						&& CloseFloat(materialBefore, materialContamination)
						&& CloseFloat(frameMaterialContamination, materialContamination);
					var contaminationTransferred = builtTerrain
						&& CloseFloat(groundBefore, 0f)
						&& CloseFloat(groundAfter, materialContamination);

					var success = materialCaptured && builtTerrain && contaminationTransferred && frameDestroyed && materialDestroyed;
					var payload = new
					{
						success,
						terrain = terrainDef.defName,
						frameDef = terrainDef.frameDef.defName,
						temporary = terrainDef.temporary,
						isFoundation = terrainDef.isFoundation,
						materialDef = frameMaterialDef.defName,
						cell = ZombieRuntimeActions.DescribeCell(cell),
						frame = ZombieRuntimeActions.StableThingId(frame),
						frameDestroyed,
						material = ZombieRuntimeActions.StableThingId(material),
						materialDestroyed,
						acceptedMaterial,
						frameMaterialCount,
						materialBefore,
						frameMaterialContamination,
						oldTop = oldTop?.defName,
						oldFoundation = oldFoundation?.defName,
						oldTemp = oldTemp?.defName,
						underTerrain = underTerrain?.defName,
						topAfter = topAfter?.defName,
						foundationAfter = foundationAfter?.defName,
						tempAfter = tempAfter?.defName,
						groundBefore,
						groundAfter,
						expectedGroundAfter = materialContamination,
						materialCaptured,
						builtTerrain,
						contaminationTransferred
					};
					return (success, payload);
				}

				GenSpawn.Spawn(worker, workerCell, map, Rot4.South);
				DisablePawnWork(worker);
				worker.needs?.AddOrRemoveNeedsAsAppropriate();
				worker.ClearContamination();

				map.SetContamination(floorCell, 0f);
				map.SetContamination(bridgeCell, 0f);
				if (tempCell.IsValid)
					map.SetContamination(tempCell, 0f);

				var floorResult = CompleteTerrainFrame(woodFloor, floorCell, 0.62f);
				var bridgeResult = CompleteTerrainFrame(bridge, bridgeCell, 0.74f, shallowWater);
				var temporaryResult = temporaryTerrain == null
					? new
					{
						success = true,
						skipped = true,
						reason = "No active constructible TerrainDef has temporary=true and frameDef != null.",
						activeTemporaryTerrainDefs = temporaryTerrains.Select(def => def.defName).ToArray()
					}
					: CompleteTerrainFrame(
						temporaryTerrain,
						tempCell,
						0.57f,
						materialDef: temporaryTerrain.CostList?.FirstOrDefault(cost => cost.thingDef != null && cost.count > 0)?.thingDef ?? stuffDef).payload;
				var affectFloorResult = VerifyAffectFloorTransfer(map, bridgeCell + new IntVec3(4, 0, 0), woodFloor, cleanup);
				var workerAfter = worker.GetContamination();

				return new
				{
					success = floorResult.success && bridgeResult.success && ObjectSuccess(temporaryResult) && ObjectSuccess(affectFloorResult) && CloseFloat(workerAfter, 0f),
					cleanup,
					worker = DescribePawn(worker),
					workerCell = ZombieRuntimeActions.DescribeCell(workerCell),
					workerAfter,
					floor = floorResult.payload,
					bridge = bridgeResult.payload,
					temporary = temporaryResult,
					affectFloor = affectFloorResult
				};
			}
			finally
			{
				foreach (var thing in createdThings)
				{
					thing?.ClearContamination();
					if (thing is { Destroyed: false, Spawned: true })
						thing.Destroy();
				}
				worker?.ClearContamination();
				if (worker is { Destroyed: false, Spawned: true })
					worker.Destroy();
				if (cleanup)
				{
					for (var i = touchedCells.Count - 1; i >= 0; i--)
					{
						var (cell, top, foundation, temp, contamination) = touchedCells[i];
						if (cell.InBounds(map) == false)
							continue;
						if (map.terrainGrid.TempTerrainAt(cell) != null)
							map.terrainGrid.RemoveTempTerrain(cell, doLeavings: false, preventDestroyEffects: true);
						if (map.terrainGrid.FoundationAt(cell) != null)
							map.terrainGrid.RemoveFoundation(cell, doLeavings: false);
						map.terrainGrid.SetTerrain(cell, top);
						if (foundation != null)
							map.terrainGrid.SetFoundation(cell, foundation);
						if (temp != null)
							map.terrainGrid.SetTempTerrain(cell, temp);
						map.SetContamination(cell, contamination);
					}
				}
			}
		}

		static object VerifyAffectFloorTransfer(Map map, IntVec3 root, TerrainDef removableFloor, bool cleanup)
		{
			var patchTargets = PatchedMethodsForPatchClass("JobDriver_AffectFloor_MakeNewToils_Patch");
			if (removableFloor == null || removableFloor.Removable == false || JobDefOf.RemoveFloor == null || DesignationDefOf.RemoveFloor == null)
			{
				return new
				{
					success = false,
					targets = CompactPatchTargets(patchTargets),
					removableFloor = removableFloor?.defName,
					removable = removableFloor?.Removable,
					jobDef = JobDefOf.RemoveFloor?.defName,
					designationDef = DesignationDefOf.RemoveFloor?.defName,
					error = "A removable floor, RemoveFloor job, or RemoveFloor designation was unavailable."
				};
			}

			if (TryFindClearSpawnCell(map, root, 16f, out var floorCell, out var floorCellError) == false)
				return floorCellError;
			if (TryFindClearSpawnCell(map, floorCell + IntVec3.East, 8f, out var workerCell, out var workerCellError) == false)
				return workerCellError;

			var oldTerrain = map.terrainGrid.TerrainAt(floorCell);
			var oldFoundation = map.terrainGrid.FoundationAt(floorCell);
			var oldTemp = map.terrainGrid.TempTerrainAt(floorCell);
			var oldContamination = map.GetContamination(floorCell);
			var worker = (Pawn)null;
			try
			{
				if (oldTemp != null)
					map.terrainGrid.RemoveTempTerrain(floorCell, doLeavings: false, preventDestroyEffects: true);
				if (oldFoundation != null)
					map.terrainGrid.RemoveFoundation(floorCell, doLeavings: false);
				map.terrainGrid.SetTerrain(floorCell, removableFloor);
				if (map.terrainGrid.CanRemoveTopLayerAt(floorCell) == false)
				{
					return new
					{
						success = false,
						targets = CompactPatchTargets(patchTargets),
						floorCell = ZombieRuntimeActions.DescribeCell(floorCell),
						terrain = map.terrainGrid.TerrainAt(floorCell)?.defName,
						error = "Fixture floor cannot be removed by vanilla terrain grid."
					};
				}

				const float groundContamination = 0.8f;
				map.SetContamination(floorCell, groundContamination);
				worker = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(worker, workerCell, map, Rot4.West, WipeMode.Vanish);
				worker.ClearContamination();
				worker.jobs?.StopAll(false, true);
				worker.pather?.StopDead();
				worker.workSettings?.EnableAndInitializeIfNotAlreadyInitialized();
				worker.skills.GetSkill(SkillDefOf.Construction).Level = 20;

				map.designationManager.DesignationAt(floorCell, DesignationDefOf.RemoveFloor)?.Delete();
				map.designationManager.AddDesignation(new Designation(floorCell, DesignationDefOf.RemoveFloor));
				var workerBefore = worker.GetContamination(false);
				var terrainBefore = map.terrainGrid.TerrainAt(floorCell);
				var groundBefore = map.GetContamination(floorCell);
				var floorAdd = ZombieSettings.Values.contamination.floorAdd;
				var expectedWorkerAfter = workerBefore + groundBefore * floorAdd;
				var job = JobMaker.MakeJob(JobDefOf.RemoveFloor, floorCell);
				job.playerForced = true;
				var started = worker.jobs.TryTakeOrderedJob(job, new JobTag?(JobTag.Misc), false);
				var tickHit = -1;
				var samples = new List<object>();

				for (var tick = 1; tick <= 600; tick++)
				{
					AdvanceGameTicks(1);
					var terrainChanged = map.terrainGrid.TerrainAt(floorCell) != terrainBefore
						|| map.terrainGrid.CanRemoveTopLayerAt(floorCell) == false;
					if (tick == 1 || tick % 60 == 0 || terrainChanged)
					{
						samples.Add(new
						{
							tick,
							workerJob = worker.CurJobDef?.defName,
							terrain = map.terrainGrid.TerrainAt(floorCell)?.defName,
							canRemoveTopLayer = map.terrainGrid.CanRemoveTopLayerAt(floorCell),
							workerContamination = worker.GetContamination(false)
						});
					}
					if (terrainChanged)
					{
						tickHit = tick;
						break;
					}
				}

				var workerAfter = worker.GetContamination(false);
				var terrainAfter = map.terrainGrid.TerrainAt(floorCell);
				var floorRemoved = terrainAfter != terrainBefore || map.terrainGrid.CanRemoveTopLayerAt(floorCell) == false;
				var contaminationTransferred = floorRemoved && Approximately(workerAfter, expectedWorkerAfter);

				return new
				{
					success = patchTargets.Length > 0
						&& started
						&& tickHit > 0
						&& floorRemoved
						&& contaminationTransferred,
					targets = CompactPatchTargets(patchTargets),
					method = "RimWorld.JobDriver_AffectFloor::<MakeNewToils>b__tickIntervalAction",
					worker = DescribePawn(worker),
					workerCell = ZombieRuntimeActions.DescribeCell(workerCell),
					floorCell = ZombieRuntimeActions.DescribeCell(floorCell),
					started,
					tickHit,
					terrainBefore = terrainBefore?.defName,
					terrainAfter = terrainAfter?.defName,
					floorRemoved,
					floorAdd,
					groundBefore,
					workerBefore,
					workerAfter,
					expectedWorkerAfter,
					contaminationTransferred,
					samples
				};
			}
			finally
			{
				map.designationManager.DesignationAt(floorCell, DesignationDefOf.RemoveFloor)?.Delete();
				worker?.ClearContamination();
				if (worker is { Destroyed: false, Spawned: true })
					worker.Destroy();
				if (cleanup)
				{
					if (map.terrainGrid.TempTerrainAt(floorCell) != null)
						map.terrainGrid.RemoveTempTerrain(floorCell, doLeavings: false, preventDestroyEffects: true);
					if (map.terrainGrid.FoundationAt(floorCell) != null)
						map.terrainGrid.RemoveFoundation(floorCell, doLeavings: false);
					map.terrainGrid.SetTerrain(floorCell, oldTerrain);
					if (oldFoundation != null)
						map.terrainGrid.SetFoundation(floorCell, oldFoundation);
					if (oldTemp != null)
						map.terrainGrid.SetTempTerrain(floorCell, oldTemp);
					map.SetContamination(floorCell, oldContamination);
				}
			}
		}

		[Tool("zombieland/contamination_zombie_death_contract", Description = "Verify killing a real zombie contaminates its death cell while an ordinary pawn death does not.")]
		public static object ContaminationZombieDeathContract()
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var zombieCell, out var zombieSpawnError) == false)
				return zombieSpawnError;
			if (TryFindClearSpawnCell(map, zombieCell + new IntVec3(4, 0, 0), 10f, out var humanCell, out var humanSpawnError) == false)
				return humanSpawnError;

			map.SetContamination(zombieCell, 0f);
			map.SetContamination(humanCell, 0f);
			var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
			var human = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(human, humanCell, map, Rot4.South);
			DisablePawnWork(human);
			if (zombie == null)
			{
				return new
				{
					success = false,
					zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
					human = DescribePawn(human),
					error = "ZombieGenerator.SpawnZombie returned no death-contamination test zombie."
				};
			}

			var zombieGroundBefore = map.GetContamination(zombieCell);
			var humanGroundBefore = map.GetContamination(humanCell);
			zombie.Kill(null);
			human.Kill(null);
			var zombieGroundAfter = map.GetContamination(zombieCell);
			var humanGroundAfter = map.GetContamination(humanCell);
			var expectedZombieGroundAfter = Mathf.Clamp01(zombieGroundBefore + ZombieSettings.Values.contamination.zombieDeathAdd);

			static bool CloseFloat(float value, float expected) => Mathf.Abs(value - expected) < 0.0001f;
			var zombieDeathContaminated = CloseFloat(zombieGroundAfter, expectedZombieGroundAfter);
			var humanDeathIgnored = CloseFloat(humanGroundAfter, humanGroundBefore);

			return new
			{
				success = zombieDeathContaminated && humanDeathIgnored,
				zombie = DescribeZombie(zombie),
				human = DescribePawn(human),
				zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
				humanCell = ZombieRuntimeActions.DescribeCell(humanCell),
				zombieDeathAdd = ZombieSettings.Values.contamination.zombieDeathAdd,
				zombieGroundBefore,
				zombieGroundAfter,
				expectedZombieGroundAfter,
				humanGroundBefore,
				humanGroundAfter,
				zombieDeathContaminated,
				humanDeathIgnored
			};
		}

		[Tool("zombieland/contamination_corpse_inner_pawn_contract", Description = "Verify a real corpse inherits contamination from its inner pawn through the Corpse.InnerPawn setter patch.")]
		public static object ContaminationCorpseInnerPawnContract()
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var pawnCell, out var pawnSpawnError) == false)
				return pawnSpawnError;

			var pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			Corpse corpse = null;
			try
			{
				GenSpawn.Spawn(pawn, pawnCell, map, Rot4.South);
				DisablePawnWork(pawn);
				pawn.needs?.AddOrRemoveNeedsAsAppropriate();
				pawn.ClearContamination();

				const float pawnContamination = 0.47f;
				pawn.AddContamination(pawnContamination);
				var pawnBeforeDeath = pawn.GetContamination();

				pawn.Kill(null);
				corpse = pawn.Corpse
					?? map.listerThings.AllThings.OfType<Corpse>().FirstOrDefault(candidate => ReferenceEquals(candidate.InnerPawn, pawn));
				var corpseContamination = corpse?.GetContamination() ?? -1f;
				var innerPawnMatches = ReferenceEquals(corpse?.InnerPawn, pawn);

				static bool CloseFloat(float value, float expected) => Mathf.Abs(value - expected) < 0.0001f;
				var corpseInheritedContamination = corpse != null
					&& innerPawnMatches
					&& CloseFloat(pawnBeforeDeath, pawnContamination)
					&& CloseFloat(corpseContamination, pawnContamination);

				return new
				{
					success = corpseInheritedContamination,
					pawn = DescribePawn(pawn),
					pawnCell = ZombieRuntimeActions.DescribeCell(pawnCell),
					pawnBeforeDeath,
					expectedCorpseContamination = pawnContamination,
					corpse = DescribeCorpse(corpse),
					corpseContamination,
					innerPawnMatches,
					corpseInheritedContamination
				};
			}
			finally
			{
				corpse?.ClearContamination();
				pawn?.ClearContamination();
				if (corpse is { Destroyed: false, Spawned: true })
					corpse.Destroy();
				if (pawn is { Destroyed: false, Spawned: true })
					pawn.Destroy();
			}
		}

		[Tool("zombieland/contamination_mineable_yield_contract", Description = "Verify contaminated mineable ground transfers through real Mineable.DestroyMined into yielded resources.")]
		public static object ContaminationMineableYieldContract(
			[ToolParameter(Description = "Destroy yielded resources before returning.", Required = false, DefaultValue = true)] bool cleanup = true)
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var mineableDef = DefDatabase<ThingDef>.GetNamedSilentFail("MineableSteel");
			var yieldDef = mineableDef?.building?.mineableThing;
			if (mineableDef == null || yieldDef == null)
			{
				return new
				{
					success = false,
					error = "MineableSteel or its mineable yield def is unavailable."
				};
			}

			bool TryFindMineableCell(IntVec3 root, float radius, out IntVec3 cell, out object error)
			{
				cell = IntVec3.Invalid;
				error = null;
				foreach (var candidate in GenRadial.RadialCellsAround(root, radius, true))
				{
					if (candidate.InBounds(map) == false)
						continue;
					if (candidate.Fogged(map))
						continue;
					if (candidate.GetEdifice(map) != null)
						continue;
					if (candidate.GetPlant(map) != null)
						continue;
					if (candidate.GetThingList(map).Any(thing => thing is not Filth))
						continue;

					cell = candidate;
					return true;
				}

				error = new
				{
					success = false,
					error = $"No clear mineable fixture cell was found near ({root.x}, {root.z})."
				};
				return false;
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var workerCell, out var workerSpawnError) == false)
				return workerSpawnError;
			if (TryFindMineableCell(workerCell + new IntVec3(4, 0, 0), 16f, out var mineableCell, out var mineableCellError) == false)
				return mineableCellError;

			var worker = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var mineable = (Mineable)null;
			var yieldedThings = new List<Thing>();
			var oldGroundContamination = map.GetContamination(mineableCell);
			try
			{
				GenSpawn.Spawn(worker, workerCell, map, Rot4.South);
				DisablePawnWork(worker);
				worker.needs?.AddOrRemoveNeedsAsAppropriate();
				worker.ClearContamination();

				mineable = ThingMaker.MakeThing(mineableDef) as Mineable;
				if (mineable == null)
				{
					return new
					{
						success = false,
						error = "Could not create MineableSteel fixture."
					};
				}

				GenSpawn.Spawn(mineable, mineableCell, map, Rot4.South, WipeMode.Vanish, false);
				const float mineableContamination = 0.65f;
				map.SetContamination(mineableCell, mineableContamination);
				var mineableBefore = mineable.GetContamination();
				var existingYields = map.listerThings.ThingsOfDef(yieldDef).ToHashSet();

				mineable.DestroyMined(worker);

				yieldedThings = map.listerThings.ThingsOfDef(yieldDef)
					.Where(thing => existingYields.Contains(thing) == false)
					.ToList();
				var yieldContamination = yieldedThings.Count == 1 ? yieldedThings[0].GetContamination() : -1f;
				var expectedYieldContamination = mineableContamination * ZombieSettings.Values.contamination.destroyMineableAdd;

				static bool CloseFloat(float value, float expected) => Mathf.Abs(value - expected) < 0.0001f;
				var yielded = mineable.Destroyed
					&& yieldedThings.Count == 1
					&& yieldedThings[0].def == yieldDef
					&& yieldedThings[0].stackCount > 0;
				var contaminationTransferred = yielded
					&& CloseFloat(mineableBefore, mineableContamination)
					&& CloseFloat(yieldContamination, expectedYieldContamination);

				return new
				{
					success = yielded && contaminationTransferred,
					cleanup,
					worker = DescribePawn(worker),
					workerCell = ZombieRuntimeActions.DescribeCell(workerCell),
					mineable = ZombieRuntimeActions.StableThingId(mineable),
					mineableDef = mineableDef.defName,
					mineableCell = ZombieRuntimeActions.DescribeCell(mineableCell),
					mineableDestroyed = mineable.Destroyed,
					mineableBefore,
					yieldDef = yieldDef.defName,
					effectiveMineableYield = mineableDef.building.EffectiveMineableYield,
					yieldedCount = yieldedThings.Count,
					yieldedThings = yieldedThings
						.Select(thing => new
						{
							thing = ZombieRuntimeActions.StableThingId(thing),
							def = thing.def.defName,
							stackCount = thing.stackCount,
							contamination = thing.GetContamination()
						})
						.ToArray(),
					yieldContamination,
					expectedYieldContamination,
					destroyMineableAdd = ZombieSettings.Values.contamination.destroyMineableAdd,
					yielded,
					contaminationTransferred
				};
			}
			finally
			{
				if (cleanup)
				{
					foreach (var thing in yieldedThings)
					{
						thing?.ClearContamination();
						if (thing is { Destroyed: false, Spawned: true })
							thing.Destroy();
					}
				}
				mineable?.ClearContamination();
				if (mineable is { Destroyed: false, Spawned: true })
					mineable.Destroy(DestroyMode.Vanish);
				worker?.ClearContamination();
				if (worker is { Destroyed: false, Spawned: true })
					worker.Destroy();
				if (mineableCell.IsValid && mineableCell.InBounds(map))
					map.SetContamination(mineableCell, oldGroundContamination);
			}
		}

		[Tool("zombieland/contamination_deep_drill_contract", Description = "Verify deep-drill output receives the configured contamination through real CompDeepDrill.TryProducePortion.")]
		public static object ContaminationDeepDrillContract(
			[ToolParameter(Description = "Destroy yielded resources before returning.", Required = false, DefaultValue = true)] bool cleanup = true)
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var drillDef = ThingDefOf.DeepDrill;
			var resourceDef = ThingDefOf.Steel;
			var method = typeof(CompDeepDrill).GetMethod("TryProducePortion", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			if (drillDef == null || resourceDef == null || method == null)
			{
				return new
				{
					success = false,
					error = "DeepDrill, Steel, or CompDeepDrill.TryProducePortion is unavailable."
				};
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearBuildingFootprint(map, drillDef, root, 24f, out var drillCell, out var drillCellError) == false)
				return drillCellError;

			var drill = (ThingWithComps)null;
			var yieldedThings = new List<Thing>();
			var resourceCell = drillCell;
			var oldResourceDef = map.deepResourceGrid.ThingDefAt(resourceCell);
			var oldResourceCount = map.deepResourceGrid.CountAt(resourceCell);
			try
			{
				drill = ThingMaker.MakeThing(drillDef) as ThingWithComps;
				if (drill == null)
				{
					return new
					{
						success = false,
						error = "Could not create DeepDrill fixture."
					};
				}
				drill.SetFactionDirect(Faction.OfPlayer);
				GenSpawn.Spawn(drill, drillCell, map, Rot4.North, WipeMode.Vanish, false);

				var comp = drill.GetComp<CompDeepDrill>();
				if (comp == null)
				{
					return new
					{
						success = false,
						error = "Spawned DeepDrill has no CompDeepDrill."
					};
				}

				resourceCell = drill.Position;
				oldResourceDef = map.deepResourceGrid.ThingDefAt(resourceCell);
				oldResourceCount = map.deepResourceGrid.CountAt(resourceCell);
				var resourceCountBefore = resourceDef.deepCountPerPortion;
				map.deepResourceGrid.SetAt(resourceCell, resourceDef, resourceCountBefore);
				var existingYields = map.listerThings.ThingsOfDef(resourceDef).ToHashSet();
				var deepDrillAdd = ZombieSettings.Values.contamination.deepDrillAdd;

				method.Invoke(comp, new object[] { 1f, null });

				var resourceDefAfter = map.deepResourceGrid.ThingDefAt(resourceCell);
				var resourceCountAfter = map.deepResourceGrid.CountAt(resourceCell);
				yieldedThings = map.listerThings.ThingsOfDef(resourceDef)
					.Where(thing => existingYields.Contains(thing) == false)
					.ToList();
				var yieldContamination = yieldedThings.Count == 1 ? yieldedThings[0].GetContamination() : -1f;

				static bool CloseFloat(float value, float expected) => Mathf.Abs(value - expected) < 0.0001f;
				var yielded = yieldedThings.Count == 1
					&& yieldedThings[0].def == resourceDef
					&& yieldedThings[0].stackCount == resourceCountBefore
					&& resourceDefAfter == null
					&& resourceCountAfter == 0;
				var contaminationAdded = yielded
					&& CloseFloat(yieldContamination, deepDrillAdd);

				return new
				{
					success = yielded && contaminationAdded,
					cleanup,
					drill = ZombieRuntimeActions.StableThingId(drill),
					drillCell = ZombieRuntimeActions.DescribeCell(drillCell),
					interactionCell = ZombieRuntimeActions.DescribeCell(drill.InteractionCell),
					resourceCell = ZombieRuntimeActions.DescribeCell(resourceCell),
					resourceDef = resourceDef.defName,
					resourceCountBefore,
					resourceDefAfter = resourceDefAfter?.defName,
					resourceCountAfter,
					yieldedCount = yieldedThings.Count,
					yieldedThings = yieldedThings
						.Select(thing => new
						{
							thing = ZombieRuntimeActions.StableThingId(thing),
							def = thing.def.defName,
							stackCount = thing.stackCount,
							contamination = thing.GetContamination()
						})
						.ToArray(),
					yieldContamination,
					expectedYieldContamination = deepDrillAdd,
					yielded,
					contaminationAdded
				};
			}
			finally
			{
				if (cleanup)
				{
					foreach (var thing in yieldedThings)
					{
						thing?.ClearContamination();
						if (thing is { Destroyed: false, Spawned: true })
							thing.Destroy();
					}
				}
				if (drill is { Destroyed: false, Spawned: true })
					drill.Destroy();
				if (resourceCell.IsValid && resourceCell.InBounds(map))
					map.deepResourceGrid.SetAt(resourceCell, oldResourceDef, oldResourceCount);
			}
		}

		[Tool("zombieland/contamination_plant_stump_contract", Description = "Verify a contaminated tree transfers contamination through real Plant.TrySpawnStump into its stump.")]
		public static object ContaminationPlantStumpContract(
			[ToolParameter(Description = "Destroy generated plant/stump fixtures before returning.", Required = false, DefaultValue = true)] bool cleanup = true)
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var plantDef = DefDatabase<ThingDef>.GetNamedSilentFail("Plant_TreeOak");
			var expectedStumpDef = plantDef?.plant?.choppedThingDef;
			if (plantDef == null || expectedStumpDef == null)
			{
				return new
				{
					success = false,
					error = "Plant_TreeOak or its chopped stump def is unavailable."
				};
			}

			bool TryFindPlantCell(IntVec3 root, float radius, out IntVec3 cell, out object error)
			{
				cell = IntVec3.Invalid;
				error = null;
				foreach (var candidate in GenRadial.RadialCellsAround(root, radius, true))
				{
					if (candidate.InBounds(map) == false)
						continue;
					if (candidate.Fogged(map))
						continue;
					if (candidate.GetEdifice(map) != null)
						continue;
					if (candidate.GetPlant(map) != null)
						continue;
					if (candidate.GetThingList(map).Any(thing => thing is not Filth))
						continue;

					cell = candidate;
					return true;
				}

				error = new
				{
					success = false,
					error = $"No clear plant fixture cell was found near ({root.x}, {root.z})."
				};
				return false;
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindPlantCell(root, 24f, out var plantCell, out var plantCellError) == false)
				return plantCellError;

			var plant = (Plant)null;
			var stump = (Thing)null;
			try
			{
				plant = ThingMaker.MakeThing(plantDef) as Plant;
				if (plant == null)
				{
					return new
					{
						success = false,
						error = "Could not create Plant_TreeOak fixture."
					};
				}

				GenSpawn.Spawn(plant, plantCell, map, Rot4.South, WipeMode.Vanish, false);
				plant.Growth = 1f;

				const float plantContamination = 0.55f;
				plant.SetContamination(plantContamination);
				var plantBefore = plant.GetContamination();
				var harvestable = plant.HarvestableNow;

				stump = plant.TrySpawnStump(PlantDestructionMode.Chop);

				var plantAfter = plant.GetContamination();
				var stumpContamination = stump?.GetContamination() ?? -1f;
				var expectedStumpContamination = plantContamination * ZombieSettings.Values.contamination.stumpTransfer;
				var expectedPlantAfter = plantContamination - expectedStumpContamination;

				static bool CloseFloat(float value, float expected) => Mathf.Abs(value - expected) < 0.0001f;
				var spawnedStump = harvestable
					&& stump != null
					&& stump.Spawned
					&& stump.def == expectedStumpDef
					&& stump.Position == plantCell;
				var contaminationTransferred = spawnedStump
					&& CloseFloat(plantBefore, plantContamination)
					&& CloseFloat(stumpContamination, expectedStumpContamination)
					&& CloseFloat(plantAfter, expectedPlantAfter);

				return new
				{
					success = spawnedStump && contaminationTransferred,
					cleanup,
					cell = ZombieRuntimeActions.DescribeCell(plantCell),
					plant = ZombieRuntimeActions.StableThingId(plant),
					plantDef = plant.def.defName,
					plantGrowth = plant.Growth,
					harvestable,
					plantBefore,
					plantAfter,
					expectedPlantAfter,
					stump = ZombieRuntimeActions.StableThingId(stump),
					stumpDef = stump?.def?.defName,
					expectedStumpDef = expectedStumpDef.defName,
					stumpSpawned = stump?.Spawned,
					stumpContamination,
					expectedStumpContamination,
					stumpTransfer = ZombieSettings.Values.contamination.stumpTransfer,
					spawnedStump,
					contaminationTransferred
				};
			}
			finally
			{
				if (cleanup)
				{
					stump?.ClearContamination();
					if (stump is { Destroyed: false, Spawned: true })
						stump.Destroy();
					plant?.ClearContamination();
					if (plant is { Destroyed: false, Spawned: true })
						plant.Destroy();
				}
			}
		}

		[Tool("zombieland/contamination_plant_harvest_contract", Description = "Harvest a contaminated plant through the real PlantHarvest job and verify harvested products receive plant-transfer contamination.")]
		public static object ContaminationPlantHarvestContract(
			[ToolParameter(Description = "Plant def to harvest.", Required = false, DefaultValue = "Plant_Rice")] string plantDefName = "Plant_Rice",
			[ToolParameter(Description = "Destroy harvested products before returning.", Required = false, DefaultValue = true)] bool cleanup = true)
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			plantDefName = string.IsNullOrWhiteSpace(plantDefName) ? "Plant_Rice" : plantDefName.Trim();
			var plantDef = DefDatabase<ThingDef>.GetNamedSilentFail(plantDefName);
			var productDef = plantDef?.plant?.harvestedThingDef;
			var harvestJobDef = DefDatabase<JobDef>.GetNamedSilentFail("Harvest");
			if (plantDef == null || productDef == null || harvestJobDef == null)
			{
				return new
				{
					success = false,
					error = $"{plantDefName}, its harvested product, or JobDef Harvest is unavailable."
				};
			}

			bool TryFindPlantCell(IntVec3 root, float radius, out IntVec3 cell, out object error)
			{
				cell = IntVec3.Invalid;
				error = null;
				foreach (var candidate in GenRadial.RadialCellsAround(root, radius, true))
				{
					if (candidate.InBounds(map) == false)
						continue;
					if (candidate.Fogged(map))
						continue;
					if (candidate.GetEdifice(map) != null)
						continue;
					if (candidate.GetPlant(map) != null)
						continue;
					if (candidate.GetThingList(map).Any(thing => thing is not Filth))
						continue;

					cell = candidate;
					return true;
				}

				error = new
				{
					success = false,
					error = $"No clear plant fixture cell was found near ({root.x}, {root.z})."
				};
				return false;
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindPlantCell(root, 24f, out var plantCell, out var plantCellError) == false)
				return plantCellError;
			if (TryFindClearSpawnCell(map, plantCell + IntVec3.East, 8f, out var workerCell, out var workerCellError) == false)
				return workerCellError;

			var plant = (Plant)null;
			var worker = (Pawn)null;
			var products = new List<Thing>();
			try
			{
				worker = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(worker, workerCell, map, WipeMode.Vanish);
				DisablePawnWork(worker);
				worker.skills?.GetSkill(SkillDefOf.Plants).Notify_SkillDisablesChanged();
				worker.skills.GetSkill(SkillDefOf.Plants).Level = 20;

				plant = ThingMaker.MakeThing(plantDef) as Plant;
				if (plant == null)
				{
					return new
					{
						success = false,
						error = $"Could not create {plantDefName} fixture."
					};
				}

				GenSpawn.Spawn(plant, plantCell, map, Rot4.South, WipeMode.Vanish, false);
				plant.Growth = 1f;

				const float plantContamination = 0.64f;
				plant.SetContamination(plantContamination);
				var plantBefore = plant.GetContamination();
				var expectedProductDefs = plant.AllComps
					.SelectMany(comp => comp.GetAdditionalHarvestYield() ?? Enumerable.Empty<ThingDefCountClass>())
					.Where(extra => extra?.thingDef != null && extra.count > 0)
					.Select(extra => extra.thingDef)
					.Prepend(productDef)
					.Distinct()
					.ToArray();
				var productIdsBefore = expectedProductDefs
					.SelectMany(def => map.listerThings.ThingsOfDef(def))
					.Select(ZombieRuntimeActions.StableThingId)
					.ToHashSet(StringComparer.OrdinalIgnoreCase);
				var harvestable = plant.HarvestableNow;
				var workPerTick = JobDriver_PlantWork.WorkDonePerTick(worker, plant);
				var sourceDerivedWorkTicks = Mathf.CeilToInt(plant.def.plant.harvestWork / workPerTick);
				var maxTicks = sourceDerivedWorkTicks + 60;
				var job = JobMaker.MakeJob(harvestJobDef, plant);
				job.playerForced = true;
				var started = worker.jobs.TryTakeOrderedJob(job, new JobTag?(JobTag.Misc), false);
				var tickHit = -1;
				var samples = new List<object>();

				for (var tick = 1; tick <= maxTicks; tick++)
				{
					AdvanceGameTicks(1);
					products = expectedProductDefs
						.SelectMany(def => map.listerThings.ThingsOfDef(def))
						.Where(thing => productIdsBefore.Contains(ZombieRuntimeActions.StableThingId(thing)) == false)
						.ToList();
					var produced = products.Count > 0;
					if (tick == 1 || tick == sourceDerivedWorkTicks || tick == maxTicks || produced)
					{
						samples.Add(new
						{
							tick,
							workerJob = worker.CurJobDef?.defName,
							plantSpawned = plant.Spawned,
							plantDestroyed = plant.Destroyed,
							productCount = products.Count,
							productStackCount = products.Sum(thing => thing.stackCount)
						});
					}
					if (produced)
					{
						tickHit = tick;
						break;
					}
				}

				var productStates = products
					.Select(thing => new
					{
						id = ZombieRuntimeActions.StableThingId(thing),
						def = thing.def?.defName,
						stackCount = thing.stackCount,
						contamination = thing.GetContamination()
					})
					.ToList();
				var plantAfter = plant.GetContamination();
				var expectedProductContaminationValues = new List<float>();
				var remainingSourceContamination = plantContamination;
				foreach (var thing in products)
				{
					var expected = remainingSourceContamination * ZombieSettings.Values.contamination.plantTransfer;
					expectedProductContaminationValues.Add(expected);
					remainingSourceContamination -= expected;
				}
				var expectedProductContaminations = products
					.Zip(expectedProductContaminationValues, (thing, expected) => new
					{
						def = thing.def?.defName,
						expected
					})
					.ToList();
				var expectedSourceAfterTransferBeforeCollection = remainingSourceContamination;
				static bool CloseFloat(float value, float expected) => Mathf.Abs(value - expected) < 0.0001f;

				var producedDefs = products
					.Select(thing => thing.def)
					.Where(def => def != null)
					.ToHashSet();
				var expectedDefsProduced = expectedProductDefs.All(producedDefs.Contains);
				var productsContaminated = products.Count > 0
					&& products
						.Zip(expectedProductContaminationValues, (product, expected) => new { product, expected })
						.All(pair => CloseFloat(pair.product.GetContamination(), pair.expected));

				return new
				{
					success = harvestable
						&& started
						&& tickHit > 0
						&& products.Count > 0
						&& expectedDefsProduced
						&& productsContaminated
						&& plant.Destroyed,
					cleanup,
					worker = DescribePawn(worker),
					cells = new
					{
						worker = ZombieRuntimeActions.DescribeCell(workerCell),
						plant = ZombieRuntimeActions.DescribeCell(plantCell)
					},
					jobDef = harvestJobDef.defName,
					started,
					tickHit,
					maxTicks,
					sourceDerivedWorkTicks,
					workPerTick,
					harvestWork = plant.def.plant.harvestWork,
					harvestable,
					plant = ZombieRuntimeActions.StableThingId(plant),
					plantDef = plant.def.defName,
					plantBefore,
					plantAfter,
					expectedSourceAfterTransferBeforeCollection,
					plantDestroyed = plant.Destroyed,
					expectedProductDefs = expectedProductDefs.Select(def => def.defName).ToArray(),
					expectedDefsProduced,
					products = productStates,
					expectedProductContaminations,
					plantTransfer = ZombieSettings.Values.contamination.plantTransfer,
					productsContaminated,
					samples
				};
			}
			finally
			{
				if (cleanup)
				{
					foreach (var product in products)
					{
						product?.ClearContamination();
						if (product is { Destroyed: false, Spawned: true })
							product.Destroy();
					}
				}
				plant?.ClearContamination();
				if (plant is { Destroyed: false, Spawned: true })
					plant.Destroy();
				if (worker is { Destroyed: false, Spawned: true })
					worker.Destroy();
			}
		}

		[Tool("zombieland/contamination_plant_sow_contract", Description = "Sow a plant through the real PlantSow job and verify ground contamination plus sowing-pawn equalization reach the new plant.")]
		public static object ContaminationPlantSowContract(
			[ToolParameter(Description = "Destroy generated plant and restore terrain before returning.", Required = false, DefaultValue = true)] bool cleanup = true)
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var plantDef = DefDatabase<ThingDef>.GetNamedSilentFail("Plant_Rice");
			var sowJobDef = DefDatabase<JobDef>.GetNamedSilentFail("Sow");
			if (plantDef == null || sowJobDef == null)
			{
				return new
				{
					success = false,
					error = "Plant_Rice or JobDef Sow is unavailable."
				};
			}

			bool TryFindSowCell(IntVec3 root, float radius, out IntVec3 cell, out object error)
			{
				cell = IntVec3.Invalid;
				error = null;
				foreach (var candidate in GenRadial.RadialCellsAround(root, radius, true))
				{
					if (candidate.InBounds(map) == false)
						continue;
					if (candidate.Fogged(map))
						continue;
					if (candidate.GetEdifice(map) != null)
						continue;
					if (candidate.GetThingList(map).Any(thing =>
						thing is Pawn
						|| thing is Blueprint
						|| thing is Frame
						|| thing.def.category == ThingCategory.Plant
						|| thing.def.category == ThingCategory.Building
						|| thing.def.EverHaulable))
						continue;

					cell = candidate;
					return true;
				}

				error = new
				{
					success = false,
					error = $"No clear sow fixture cell was found near ({root.x}, {root.z})."
				};
				return false;
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindSowCell(root, 24f, out var sowCell, out var sowCellError) == false)
				return sowCellError;
			if (TryFindClearSpawnCell(map, sowCell + IntVec3.East, 8f, out var workerCell, out var workerCellError) == false)
				return workerCellError;

			var oldTerrain = map.terrainGrid.TerrainAt(sowCell);
			var oldFoundation = map.terrainGrid.FoundationAt(sowCell);
			var oldTempTerrain = map.terrainGrid.TempTerrainAt(sowCell);
			var oldGroundContamination = map.GetContamination(sowCell);
			var worker = (Pawn)null;
			var plant = (Plant)null;
			try
			{
				map.terrainGrid.SetTerrain(sowCell, TerrainDefOf.Soil);
				map.terrainGrid.RemoveTempTerrain(sowCell);
				if (map.terrainGrid.FoundationAt(sowCell) != null)
					map.terrainGrid.RemoveFoundation(sowCell, false);
				map.mapDrawer.MapMeshDirty(sowCell, MapMeshFlagDefOf.Terrain);

				worker = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(worker, workerCell, map, WipeMode.Vanish);
				DisablePawnWork(worker);
				worker.skills?.GetSkill(SkillDefOf.Plants).Notify_SkillDisablesChanged();
				worker.skills.GetSkill(SkillDefOf.Plants).Level = 20;

				const float groundContaminationBefore = 0.50f;
				const float workerContaminationBefore = 0.80f;
				map.SetContamination(sowCell, groundContaminationBefore);
				worker.SetContamination(workerContaminationBefore);

				var canPlantBefore = plantDef.CanNowPlantAt(sowCell, map);
				var job = JobMaker.MakeJob(sowJobDef, sowCell);
				job.plantDefToSow = plantDef;
				job.playerForced = true;
				var started = worker.jobs.TryTakeOrderedJob(job, new JobTag?(JobTag.Misc), false);
				var workPerTick = worker.GetStatValue(StatDefOf.PlantWorkSpeed);
				var sourceDerivedWorkTicks = Mathf.CeilToInt(plantDef.plant.sowWork / workPerTick);
				var maxTicks = sourceDerivedWorkTicks + 60;
				var tickHit = -1;
				var samples = new List<object>();
				var workerContaminationAfterSpawnEqualize = -1f;

				for (var tick = 1; tick <= maxTicks; tick++)
				{
					AdvanceGameTicks(1);
					plant = sowCell.GetPlant(map);
					if (plant != null && workerContaminationAfterSpawnEqualize < 0)
						workerContaminationAfterSpawnEqualize = worker.GetContamination(false);
					var finished = plant != null && plant.def == plantDef && plant.Growth > 0f;
					if (tick == 1 || tick == sourceDerivedWorkTicks || tick == maxTicks || finished)
					{
						samples.Add(new
						{
							tick,
							workerJob = worker.CurJobDef?.defName,
							plantSpawned = plant?.Spawned ?? false,
							plantGrowth = plant?.Growth ?? -1f,
							plantContamination = plant?.GetContamination() ?? -1f,
							workerContamination = worker.GetContamination(false)
						});
					}
					if (finished)
					{
						tickHit = tick;
						break;
					}
				}

				var groundAdd = groundContaminationBefore * ZombieSettings.Values.contamination.sowedPlantAdd;
				var equalizeWeight = ZombieSettings.Values.contamination.sowingPawnEqualize;
				var high = workerContaminationBefore;
				var low = groundAdd;
				if (high < low)
					(high, low) = (low, high);
				var expectedHighAfter = high * (1f - equalizeWeight) + low * equalizeWeight;
				var expectedLowAfter = high * equalizeWeight + low * (1f - equalizeWeight);
				var expectedWorkerContamination = workerContaminationBefore >= groundAdd ? expectedHighAfter : expectedLowAfter;
				var expectedPlantContamination = workerContaminationBefore >= groundAdd ? expectedLowAfter : expectedHighAfter;
				var plantContamination = plant?.GetContamination() ?? -1f;
				var workerContamination = worker.GetContamination(false);
				static bool CloseFloat(float value, float expected) => Mathf.Abs(value - expected) < 0.0001f;

				return new
				{
					success = canPlantBefore
						&& started
						&& tickHit > 0
						&& plant != null
						&& plant.def == plantDef
						&& plant.sown
						&& plant.Growth > 0f
						&& CloseFloat(plantContamination, expectedPlantContamination)
						&& CloseFloat(workerContaminationAfterSpawnEqualize, expectedWorkerContamination),
					cleanup,
					worker = DescribePawn(worker),
					cells = new
					{
						worker = ZombieRuntimeActions.DescribeCell(workerCell),
						sow = ZombieRuntimeActions.DescribeCell(sowCell)
					},
					jobDef = sowJobDef.defName,
					plantDef = plantDef.defName,
					started,
					canPlantBefore,
					tickHit,
					maxTicks,
					sourceDerivedWorkTicks,
					workPerTick,
					sowWork = plantDef.plant.sowWork,
					groundContaminationBefore,
					workerContaminationBefore,
					groundAdd,
					sowedPlantAdd = ZombieSettings.Values.contamination.sowedPlantAdd,
					sowingPawnEqualize = equalizeWeight,
					plant = ZombieRuntimeActions.StableThingId(plant),
					plantGrowth = plant?.Growth ?? -1f,
					plantSown = plant?.sown ?? false,
					plantContamination,
					expectedPlantContamination,
					workerContamination,
					expectedWorkerContamination,
					workerContaminationAfterSpawnEqualize,
					samples
				};
			}
			finally
			{
				if (cleanup)
				{
					plant?.ClearContamination();
					if (plant is { Destroyed: false, Spawned: true })
						plant.Destroy();
				}
				worker?.ClearContamination();
				if (worker is { Destroyed: false, Spawned: true })
					worker.Destroy();
				if (cleanup)
				{
					map.SetContamination(sowCell, oldGroundContamination);
					if (map.terrainGrid.FoundationAt(sowCell) != null)
						map.terrainGrid.RemoveFoundation(sowCell, false);
					map.terrainGrid.SetTerrain(sowCell, oldTerrain);
					if (oldFoundation != null)
						map.terrainGrid.SetFoundation(sowCell, oldFoundation);
					if (oldTempTerrain != null)
						map.terrainGrid.SetTempTerrain(sowCell, oldTempTerrain);
					map.mapDrawer.MapMeshDirty(sowCell, MapMeshFlagDefOf.Terrain);
				}
			}
		}

		[Tool("zombieland/contamination_wild_plant_spawn_contract", Description = "Spawn a wild plant through real WildPlantSpawner.SpawnPlant and verify contaminated ground transfers to the plant.")]
		public static object ContaminationWildPlantSpawnContract(
			[ToolParameter(Description = "Destroy generated plant and restore terrain before returning.", Required = false, DefaultValue = true)] bool cleanup = true)
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}
			if (map.wildPlantSpawner == null)
			{
				return new
				{
					success = false,
					error = "Current map has no WildPlantSpawner."
				};
			}
			var plantDef = DefDatabase<ThingDef>.GetNamedSilentFail("Plant_Grass");
			if (plantDef == null)
				plantDef = DefDatabase<ThingDef>.GetNamedSilentFail("Plant_Rice");
			if (plantDef == null)
			{
				return new
				{
					success = false,
					error = "Plant_Grass and Plant_Rice are unavailable."
				};
			}

			bool TryFindWildPlantCell(IntVec3 root, float radius, out IntVec3 cell, out object error)
			{
				cell = IntVec3.Invalid;
				error = null;
				foreach (var candidate in GenRadial.RadialCellsAround(root, radius, true))
				{
					if (candidate.InBounds(map) == false)
						continue;
					if (candidate.Fogged(map))
						continue;
					if (candidate.GetPlant(map) != null)
						continue;
					if (candidate.GetCover(map) != null)
						continue;
					if (candidate.GetEdifice(map) != null)
						continue;
					if (PlantUtility.SnowAllowsPlanting(candidate, map) == false)
						continue;
					if (PlantUtility.SandAllowsPlanting(candidate, map) == false)
						continue;
					if (candidate.GetThingList(map).Any(thing => thing is not Filth))
						continue;

					cell = candidate;
					return true;
				}

				error = new
				{
					success = false,
					error = $"No clear wild-plant fixture cell was found near ({root.x}, {root.z})."
				};
				return false;
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindWildPlantCell(root, 30f, out var plantCell, out var cellError) == false)
				return cellError;

			var oldTerrain = map.terrainGrid.TerrainAt(plantCell);
			var oldFoundation = map.terrainGrid.FoundationAt(plantCell);
			var oldTempTerrain = map.terrainGrid.TempTerrainAt(plantCell);
			var oldGroundContamination = map.GetContamination(plantCell);
			var plant = (Plant)null;
			try
			{
				map.terrainGrid.SetTerrain(plantCell, TerrainDefOf.Soil);
				map.terrainGrid.RemoveTempTerrain(plantCell);
				if (map.terrainGrid.FoundationAt(plantCell) != null)
					map.terrainGrid.RemoveFoundation(plantCell, false);
				map.mapDrawer.MapMeshDirty(plantCell, MapMeshFlagDefOf.Terrain);

				const float groundContamination = 0.72f;
				map.SetContamination(plantCell, groundContamination);

				var canEverPlantAfterTerrainSetup = plantDef.CanEverPlantAt(plantCell, map);
				plant = WildPlantSpawner.SpawnPlant(plantDef, map, plantCell, false);
				var plantContamination = plant?.GetContamination() ?? -1f;
				var expectedPlantContamination = groundContamination * ZombieSettings.Values.contamination.plantAdd;
				static bool CloseFloat(float value, float expected) => Mathf.Abs(value - expected) < 0.0001f;

				return new
				{
					success = canEverPlantAfterTerrainSetup
						&& plant != null
						&& plant.Spawned
						&& plant.def == plantDef
						&& plant.Position == plantCell
						&& CloseFloat(plantContamination, expectedPlantContamination),
					cleanup,
					cell = ZombieRuntimeActions.DescribeCell(plantCell),
					canEverPlantAfterTerrainSetup,
					plant = ZombieRuntimeActions.StableThingId(plant),
					plantDef = plant?.def?.defName,
					plantGrowth = plant?.Growth ?? -1f,
					groundContamination,
					plantAdd = ZombieSettings.Values.contamination.plantAdd,
					plantContamination,
					expectedPlantContamination
				};
			}
			finally
			{
				if (cleanup)
				{
					plant?.ClearContamination();
					if (plant is { Destroyed: false, Spawned: true })
						plant.Destroy();
					map.SetContamination(plantCell, oldGroundContamination);
					if (map.terrainGrid.FoundationAt(plantCell) != null)
						map.terrainGrid.RemoveFoundation(plantCell, false);
					map.terrainGrid.SetTerrain(plantCell, oldTerrain);
					if (oldFoundation != null)
						map.terrainGrid.SetFoundation(plantCell, oldFoundation);
					if (oldTempTerrain != null)
						map.terrainGrid.SetTempTerrain(plantCell, oldTempTerrain);
					map.mapDrawer.MapMeshDirty(plantCell, MapMeshFlagDefOf.Terrain);
				}
			}
		}

		[Tool("zombieland/contamination_ambrosia_sprout_contract", Description = "Run the real AmbrosiaSprout incident and verify contaminated ground transfers to spawned ambrosia.")]
		public static object ContaminationAmbrosiaSproutContract(
			[ToolParameter(Description = "Destroy generated ambrosia before returning.", Required = false, DefaultValue = true)] bool cleanup = true)
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var incidentDef = DefDatabase<IncidentDef>.AllDefs
				.FirstOrDefault(def => def.workerClass == typeof(IncidentWorker_AmbrosiaSprout));
			var worker = incidentDef?.Worker as IncidentWorker_AmbrosiaSprout;
			var canSpawnAtMethod = typeof(IncidentWorker_AmbrosiaSprout).GetMethod("CanSpawnAt", BindingFlags.Instance | BindingFlags.NonPublic);
			if (incidentDef == null || worker == null || canSpawnAtMethod == null)
			{
				return new
				{
					success = false,
					error = "AmbrosiaSprout incident worker or CanSpawnAt method is unavailable."
				};
			}

			bool CanSpawnAt(IntVec3 cell) => (bool)canSpawnAtMethod.Invoke(worker, new object[] { cell, map });

			bool TryFindAmbrosiaRootCell(out IntVec3 rootCell)
			{
				rootCell = IntVec3.Invalid;
				var mapCenter = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				foreach (var candidate in GenRadial.RadialCellsAround(mapCenter, 30f, true))
				{
					if (candidate.InBounds(map) == false)
						continue;
					if (candidate.Fogged(map))
						continue;
					if (candidate.GetEdifice(map) != null)
						continue;
					if (candidate.GetRoom(map)?.CellCount < 64)
						continue;
					if (candidate.GetRoom(map).PsychologicallyOutdoors == false)
						continue;
					if (candidate.GetThingList(map).Any(thing => thing is not Filth))
						continue;

					rootCell = candidate;
					return true;
				}
				return false;
			}

			if (TryFindAmbrosiaRootCell(out var rootCell) == false)
			{
				return new
				{
					success = false,
					error = "No clear AmbrosiaSprout fixture root was found.",
					incidentDef = incidentDef.defName
				};
			}

			const float groundContamination = 0.68f;
			var fixtureCells = GenRadial.RadialCellsAround(rootCell, 6f, true)
				.Where(cell => cell.InBounds(map) && cell.Fogged(map) == false && cell.GetEdifice(map) == null)
				.ToArray();
			var oldState = fixtureCells
				.Select(cell => new
				{
					cell,
					top = map.terrainGrid.TerrainAt(cell),
					foundation = map.terrainGrid.FoundationAt(cell),
					temp = map.terrainGrid.TempTerrainAt(cell),
					contamination = map.GetContamination(cell)
				})
				.ToArray();
			var ambrosiaBefore = map.listerThings.ThingsOfDef(ThingDefOf.Plant_Ambrosia)
				.Select(ZombieRuntimeActions.StableThingId)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
			var spawnedAmbrosia = new List<Thing>();
			try
			{
				foreach (var cell in fixtureCells)
				{
					if (map.terrainGrid.TempTerrainAt(cell) != null)
						map.terrainGrid.RemoveTempTerrain(cell, doLeavings: false, preventDestroyEffects: true);
					if (map.terrainGrid.FoundationAt(cell) != null)
						map.terrainGrid.RemoveFoundation(cell, false);
					map.terrainGrid.SetTerrain(cell, TerrainDefOf.Soil);
					map.SetContamination(cell, groundContamination);
					map.mapDrawer.MapMeshDirty(cell, MapMeshFlagDefOf.Terrain);
				}

				var validCells = fixtureCells.Where(CanSpawnAt).ToList();
				if (validCells.Count == 0)
				{
					return new
					{
						success = false,
						error = "AmbrosiaSprout fixture terrain setup produced no valid spawn cells.",
						cleanup,
						incidentDef = incidentDef.defName,
						rootCell = ZombieRuntimeActions.DescribeCell(rootCell),
						fixtureCellCount = fixtureCells.Length
					};
				}

				var parms = new IncidentParms
				{
					target = map
				};
				var executed = false;
				Rand.PushState(68141);
				try
				{
					executed = worker.TryExecute(parms);
				}
				finally
				{
					Rand.PopState();
				}

				spawnedAmbrosia = map.listerThings.ThingsOfDef(ThingDefOf.Plant_Ambrosia)
					.Where(thing => ambrosiaBefore.Contains(ZombieRuntimeActions.StableThingId(thing)) == false)
					.ToList();
				var expectedContamination = groundContamination * ZombieSettings.Values.contamination.ambrosiaAdd;
				static bool CloseFloat(float value, float expected) => Mathf.Abs(value - expected) < 0.0001f;
				var contaminationSamples = spawnedAmbrosia
					.Select(thing => new
					{
						thing = ZombieRuntimeActions.StableThingId(thing),
						cell = ZombieRuntimeActions.DescribeCell(thing.Position),
						contamination = thing.GetContamination()
					})
					.ToList();
				var allContaminated = spawnedAmbrosia.Count > 0
					&& spawnedAmbrosia.All(thing => CloseFloat(thing.GetContamination(), expectedContamination));

				return new
				{
					success = executed
						&& spawnedAmbrosia.Count > 0
						&& allContaminated,
					cleanup,
					incidentDef = incidentDef.defName,
					executed,
					rootCell = ZombieRuntimeActions.DescribeCell(rootCell),
					fixtureCellCount = fixtureCells.Length,
					validCellCount = validCells.Count,
					spawnedCount = spawnedAmbrosia.Count,
					groundContamination,
					ambrosiaAdd = ZombieSettings.Values.contamination.ambrosiaAdd,
					expectedContamination,
					allContaminated,
					contaminationSamples
				};
			}
			finally
			{
				if (cleanup)
				{
					foreach (var thing in spawnedAmbrosia)
					{
						thing?.ClearContamination();
						if (thing is { Destroyed: false, Spawned: true })
							thing.Destroy();
					}
				}
				if (cleanup)
				{
					foreach (var state in oldState)
					{
						if (state.cell.InBounds(map) == false)
							continue;
						if (map.terrainGrid.TempTerrainAt(state.cell) != null)
							map.terrainGrid.RemoveTempTerrain(state.cell, doLeavings: false, preventDestroyEffects: true);
						if (map.terrainGrid.FoundationAt(state.cell) != null)
							map.terrainGrid.RemoveFoundation(state.cell, false);
						map.terrainGrid.SetTerrain(state.cell, state.top);
						if (state.foundation != null)
							map.terrainGrid.SetFoundation(state.cell, state.foundation);
						if (state.temp != null)
							map.terrainGrid.SetTempTerrain(state.cell, state.temp);
						map.SetContamination(state.cell, state.contamination);
						map.mapDrawer.MapMeshDirty(state.cell, MapMeshFlagDefOf.Terrain);
					}
				}
			}
		}

		[Tool("zombieland/contamination_roof_collapse_contract", Description = "Verify contaminated ground transfers into collapsed roof rock and phase-two roof filth through real RoofCollapserImmediate helpers.")]
		public static object ContaminationRoofCollapseContract(
			[ToolParameter(Description = "Destroy generated collapsed roof rock before returning.", Required = false, DefaultValue = true)] bool cleanup = true)
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var roofDef = DefDatabase<RoofDef>.GetNamedSilentFail("RoofRockThick");
			var leavingDef = roofDef?.collapseLeavingThingDef;
			if (roofDef == null || leavingDef == null)
			{
				return new
				{
					success = false,
					error = "RoofRockThick or its collapse leaving def is unavailable."
				};
			}

			var phaseOneMethod = typeof(RoofCollapserImmediate).GetMethod("DropRoofInCellPhaseOne", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
			var phaseTwoMethod = typeof(RoofCollapserImmediate).GetMethod("DropRoofInCellPhaseTwo", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
			if (phaseOneMethod == null || phaseTwoMethod == null)
			{
				return new
				{
					success = false,
					error = "Could not reflect one or both RoofCollapserImmediate phase helpers.",
					phaseOneFound = phaseOneMethod != null,
					phaseTwoFound = phaseTwoMethod != null
				};
			}

			bool TryFindRoofCell(IntVec3 root, float radius, out IntVec3 cell, out object error)
			{
				cell = IntVec3.Invalid;
				error = null;
				foreach (var candidate in GenRadial.RadialCellsAround(root, radius, true))
				{
					if (candidate.InBounds(map) == false)
						continue;
					if (candidate.Fogged(map))
						continue;
					if (candidate.GetEdifice(map) != null)
						continue;
					if (candidate.GetThingList(map).Any(thing =>
						thing is Pawn
						|| thing is Blueprint
						|| thing is Frame
						|| thing.def.category == ThingCategory.Plant
						|| thing.def.category == ThingCategory.Building
						|| thing.def.EverHaulable))
						continue;

					cell = candidate;
					return true;
				}

				error = new
				{
					success = false,
					error = $"No clear roof-collapse fixture cell was found near ({root.x}, {root.z})."
				};
				return false;
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindRoofCell(root, 24f, out var roofCell, out var roofCellError) == false)
				return roofCellError;
			if (TryFindRoofCell(roofCell + new IntVec3(6, 0, 0), 24f, out var phaseTwoCell, out var phaseTwoCellError) == false)
				return phaseTwoCellError;

			var oldRoof = map.roofGrid.RoofAt(roofCell);
			var oldPhaseTwoRoof = map.roofGrid.RoofAt(phaseTwoCell);
			var oldGroundContamination = map.GetContamination(roofCell);
			var oldPhaseTwoGroundContamination = map.GetContamination(phaseTwoCell);
			var collapsedRock = (Thing)null;
			var phaseTwoFilths = new List<Filth>();
			try
			{
				const float groundContamination = 0.67f;
				map.roofGrid.SetRoof(roofCell, roofDef);
				map.SetContamination(roofCell, groundContamination);
				var groundBefore = map.GetContamination(roofCell);
				var existingRocks = map.listerThings.ThingsOfDef(leavingDef).ToHashSet();
				var crushedThings = new List<Thing>();

				phaseOneMethod.Invoke(null, new object[] { roofCell, map, crushedThings });

				collapsedRock = map.listerThings.ThingsOfDef(leavingDef)
					.FirstOrDefault(thing => existingRocks.Contains(thing) == false && thing.Position == roofCell);
				var groundAfterCollapse = map.GetContamination(roofCell);
				var rockContamination = collapsedRock?.GetContamination() ?? -1f;

				static bool CloseFloat(float value, float expected) => Mathf.Abs(value - expected) < 0.0001f;
				var spawnedRock = collapsedRock != null
					&& collapsedRock.Spawned
					&& collapsedRock.def == leavingDef
					&& collapsedRock.Position == roofCell;
				var contaminationTransferred = spawnedRock
					&& CloseFloat(groundBefore, groundContamination)
					&& CloseFloat(rockContamination, groundContamination);

				var phaseTwoPatchTargets = PatchedMethodsForPatchClass("RoofCollapserImmediate_DropRoofInCellPhaseTwo_Patch");
				var phaseTwoRoofDef = roofDef.filthLeaving != null
					? roofDef
					: DefDatabase<RoofDef>.AllDefsListForReading.FirstOrDefault(candidate => candidate.filthLeaving != null);
				if (phaseTwoRoofDef == null)
				{
					return new
					{
						success = false,
						phaseOne = new
						{
							success = spawnedRock && contaminationTransferred,
							cell = ZombieRuntimeActions.DescribeCell(roofCell),
							roofDef = roofDef.defName,
							leavingDef = leavingDef.defName,
							rock = ZombieRuntimeActions.StableThingId(collapsedRock),
							rockContamination,
							expectedRockContamination = groundContamination
						},
						phaseTwo = new
						{
							success = false,
							targets = CompactPatchTargets(phaseTwoPatchTargets),
							error = "No loaded RoofDef with filthLeaving was available for the phase-two probe."
						}
					};
				}

				const float phaseTwoGroundContamination = 0.61f;
				foreach (var cell in GenRadial.RadialCellsAround(phaseTwoCell, 2f, true).Where(cell => cell.InBounds(map)))
					ClearFilthAt(map, cell);
				map.roofGrid.SetRoof(phaseTwoCell, phaseTwoRoofDef);
				map.SetContamination(phaseTwoCell, phaseTwoGroundContamination);
				var phaseTwoGroundBefore = map.GetContamination(phaseTwoCell);
				var phaseTwoFilthDef = phaseTwoRoofDef.filthLeaving;
				phaseTwoMethod.Invoke(null, new object[] { phaseTwoCell, map });
				var phaseTwoGroundAfter = map.GetContamination(phaseTwoCell);
				phaseTwoFilths = GenRadial.RadialCellsAround(phaseTwoCell, 1f, true)
					.Where(cell => cell.InBounds(map))
					.SelectMany(cell => cell.GetThingList(map).OfType<Filth>())
					.Where(filth => filth.def == phaseTwoFilthDef)
					.OrderByDescending(filth => filth.GetContamination())
					.ToList();
				var phaseTwoFilth = phaseTwoFilths.FirstOrDefault();
				var phaseTwoFilthContamination = phaseTwoFilth?.GetContamination() ?? -1f;
				var expectedPhaseTwoFilthContamination = phaseTwoGroundBefore * ZombieSettings.Values.contamination.filthEqualize;
				var phaseTwoContaminated = phaseTwoPatchTargets.Length > 0
					&& phaseTwoFilth != null
					&& CloseFloat(phaseTwoFilthContamination, expectedPhaseTwoFilthContamination);

				return new
				{
					success = spawnedRock && contaminationTransferred && phaseTwoContaminated,
					cleanup,
					phaseOne = new
					{
						success = spawnedRock && contaminationTransferred,
						cell = ZombieRuntimeActions.DescribeCell(roofCell),
						roofDef = roofDef.defName,
						oldRoof = oldRoof?.defName,
						leavingDef = leavingDef.defName,
						crushedThings = crushedThings.Select(ZombieRuntimeActions.StableThingId).ToArray(),
						rock = ZombieRuntimeActions.StableThingId(collapsedRock),
						rockSpawned = collapsedRock?.Spawned,
						rockDef = collapsedRock?.def?.defName,
						rockIsMineable = collapsedRock is Mineable,
						groundBefore,
						groundAfterCollapse,
						rockContamination,
						expectedRockContamination = groundContamination,
						spawnedRock,
						contaminationTransferred
					},
					phaseTwo = new
					{
						success = phaseTwoContaminated,
						targets = CompactPatchTargets(phaseTwoPatchTargets),
						cell = ZombieRuntimeActions.DescribeCell(phaseTwoCell),
						roofDef = phaseTwoRoofDef.defName,
						oldRoof = oldPhaseTwoRoof?.defName,
						filthDef = phaseTwoFilthDef.defName,
						filthEqualize = ZombieSettings.Values.contamination.filthEqualize,
						filth = ZombieRuntimeActions.StableThingId(phaseTwoFilth),
						filthContamination = phaseTwoFilthContamination,
						expectedFilthContamination = expectedPhaseTwoFilthContamination,
						groundBefore = phaseTwoGroundBefore,
						groundAfter = phaseTwoGroundAfter,
						filthCount = phaseTwoFilths.Count,
						contaminationTransferred = phaseTwoContaminated
					}
				};
			}
			finally
			{
				if (cleanup)
				{
					collapsedRock?.ClearContamination();
					if (collapsedRock is { Destroyed: false, Spawned: true })
						collapsedRock.Destroy();
				}
				if (cleanup)
				{
					foreach (var filth in phaseTwoFilths.Where(filth => filth is { Destroyed: false, Spawned: true }).ToArray())
					{
						filth.ClearContamination();
						filth.Destroy();
					}
				}
				if (cleanup && roofCell.IsValid && roofCell.InBounds(map))
				{
					map.roofGrid.SetRoof(roofCell, oldRoof);
					map.SetContamination(roofCell, oldGroundContamination);
				}
				if (cleanup && phaseTwoCell.IsValid && phaseTwoCell.InBounds(map))
				{
					map.roofGrid.SetRoof(phaseTwoCell, oldPhaseTwoRoof);
					map.SetContamination(phaseTwoCell, oldPhaseTwoGroundContamination);
				}
			}
		}

		[Tool("zombieland/contamination_effect_manager_contract", Description = "Verify contaminated pawns register with the effect manager and can trigger the first source-derived contamination job.")]
		public static object ContaminationEffectManagerContract()
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var tickManager = map.GetComponent<TickManager>();
			var effects = tickManager?.contaminationEffects;
			if (effects == null)
			{
				return new
				{
					success = false,
					error = "Current map has no contamination effect manager."
				};
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var humanCell, out var humanSpawnError) == false)
				return humanSpawnError;

			var human = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(human, humanCell, map, Rot4.South);
			DisablePawnWork(human);
			human.needs?.AddOrRemoveNeedsAsAppropriate();
			human.ClearContamination();
			effects.Remove(human);
			human.mindState?.mentalStateHandler?.Reset();

			var trackedBefore = effects.pawns.ContainsKey(human);
			const float forceRestContamination = 0.24f;
			human.AddContamination(forceRestContamination);
			var trackedAfterAdd = effects.pawns.TryGetValue(human, out var effect);
			var nextEffectTickBeforeForce = effect?.nextEffectTick ?? -1;
			if (effect != null)
				effect.nextEffectTick = Find.TickManager.TicksGame;
			effects.Tick();

			var mentalStateAfterTick = human.mindState?.mentalStateHandler?.CurStateDef?.defName;
			var jobAfterTick = human.CurJobDef?.defName;
			var reportAfterTick = human.CurJob?.GetReport(human);
			var forceRecoverAfterTicks = human.mindState?.mentalStateHandler?.CurState?.forceRecoverAfterTicks ?? -1;
			var trackedAfterTick = effects.pawns.ContainsKey(human);
			var contaminationAfterTick = DescribeContamination(human);

			human.ClearContamination();
			var trackedAfterClear = effects.pawns.ContainsKey(human);
			var contaminationAfterClear = DescribeContamination(human);

			const float forceRestMin = 0.15f;
			const float forceRestMax = 0.40f;
			var expectedForceRestFactor = Mathf.InverseLerp(forceRestMin, forceRestMax, forceRestContamination);
			var expectedForceRecoverAfterTicks = (GenDate.TicksPerHour / 10) * (int)(1 + expectedForceRestFactor * 7);
			var registered = trackedBefore == false && trackedAfterAdd;
			var forceRestStarted = mentalStateAfterTick == EffectDefs.ContaminationStateForceRest.defName
				&& jobAfterTick == EffectDefs.ContaminationJobForceRest.defName
				&& forceRecoverAfterTicks == expectedForceRecoverAfterTicks;
			var unregistered = trackedAfterClear == false
				&& contaminationAfterClear.hasHediff == false
				&& contaminationAfterClear.stored == 0f;

			return new
			{
				success = registered && forceRestStarted && trackedAfterTick && unregistered,
				human = DescribePawn(human),
				humanCell = ZombieRuntimeActions.DescribeCell(humanCell),
				contamination = forceRestContamination,
				expectedForceRestFactor,
				trackedBefore,
				trackedAfterAdd,
				trackedAfterTick,
				trackedAfterClear,
				nextEffectTickBeforeForce,
				forcedEffectTick = Find.TickManager.TicksGame,
				mentalStateAfterTick,
				expectedMentalState = EffectDefs.ContaminationStateForceRest.defName,
				jobAfterTick,
				expectedJob = EffectDefs.ContaminationJobForceRest.defName,
				reportAfterTick,
				forceRecoverAfterTicks,
				expectedForceRecoverAfterTicks,
				contaminationAfterTick,
				contaminationAfterClear,
				registered,
				forceRestStarted,
				unregistered
			};
		}

		[Tool("zombieland/contamination_hallucination_contract", Description = "Verify the contamination hallucination effect starts the real job, ghost mote, and source-derived 30-tick movement loop.")]
		public static object ContaminationHallucinationContract()
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var humanCell, out var humanSpawnError) == false)
				return humanSpawnError;

			var human = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(human, humanCell, map, Rot4.South);
			DisablePawnWork(human);
			human.needs?.AddOrRemoveNeedsAsAppropriate();
			human.ClearContamination();
			human.mindState?.mentalStateHandler?.Reset();

			const float hallucinationMin = 0.25f;
			const float hallucinationMax = 0.50f;
			const float hallucinationContamination = 0.40f;
			var factor = Mathf.InverseLerp(hallucinationMin, hallucinationMax, hallucinationContamination);
			var expectedRecoverAfterTicks = (GenDate.TicksPerHour / 10) * (int)(1 + factor * 7);

			var applied = ContaminationEffect.Hallucination(human, factor);
			var mentalStateAfterApply = human.mindState?.mentalStateHandler?.CurStateDef?.defName;
			var jobAfterApply = human.CurJobDef?.defName;
			var recoverAfterApply = human.mindState?.mentalStateHandler?.CurState?.forceRecoverAfterTicks ?? -1;

			AdvanceGameTicks(1);
			var driverAfterInit = human.jobs?.curDriver as JobDriver_ContaminationHallucination;
			var destinationAfterInit = driverAfterInit?.destination ?? IntVec3.Invalid;
			var ghostAfterInit = driverAfterInit?.ghost;
			var ghostVecAfterInit = driverAfterInit?.ghostVec ?? Vector3.zero;
			var movingAfterInit = human.pather?.Moving ?? false;
			var pathDestinationAfterInit = movingAfterInit ? human.pather.Destination.Cell : IntVec3.Invalid;

			const int sourceDerivedUpdateTicks = 30;
			AdvanceGameTicks(sourceDerivedUpdateTicks);
			var driverAfterUpdate = human.jobs?.curDriver as JobDriver_ContaminationHallucination;
			var destinationAfterUpdate = driverAfterUpdate?.destination ?? IntVec3.Invalid;
			var ghostAfterUpdate = driverAfterUpdate?.ghost;
			var ghostVecAfterUpdate = driverAfterUpdate?.ghostVec ?? Vector3.zero;
			var ghostMoved = (ghostVecAfterUpdate - ghostVecAfterInit).sqrMagnitude > 0.0001f;
			var jobAfterUpdate = human.CurJobDef?.defName;

			var stateStarted = applied
				&& mentalStateAfterApply == EffectDefs.ContaminationStateHallucination.defName
				&& jobAfterApply == EffectDefs.ContaminationJobHallucination.defName
				&& recoverAfterApply == expectedRecoverAfterTicks;
			var jobInitialized = driverAfterInit != null
				&& destinationAfterInit.IsValid
				&& ghostAfterInit != null
				&& movingAfterInit
				&& pathDestinationAfterInit == destinationAfterInit;
			var periodicLoopRan = driverAfterUpdate != null
				&& ghostAfterUpdate != null
				&& ghostMoved
				&& jobAfterUpdate == EffectDefs.ContaminationJobHallucination.defName;

			return new
			{
				success = stateStarted && jobInitialized && periodicLoopRan,
				human = DescribePawn(human),
				humanCell = ZombieRuntimeActions.DescribeCell(humanCell),
				hallucinationContamination,
				factor,
				expectedRecoverAfterTicks,
				applied,
				mentalStateAfterApply,
				expectedMentalState = EffectDefs.ContaminationStateHallucination.defName,
				jobAfterApply,
				expectedJob = EffectDefs.ContaminationJobHallucination.defName,
				recoverAfterApply,
				destinationAfterInit = destinationAfterInit.IsValid ? ZombieRuntimeActions.DescribeCell(destinationAfterInit) : null,
				ghostAfterInit = ghostAfterInit != null,
				ghostVecAfterInit = DescribeVector(ghostVecAfterInit),
				movingAfterInit,
				pathDestinationAfterInit = pathDestinationAfterInit.IsValid ? ZombieRuntimeActions.DescribeCell(pathDestinationAfterInit) : null,
				sourceDerivedUpdateTicks,
				destinationAfterUpdate = destinationAfterUpdate.IsValid ? ZombieRuntimeActions.DescribeCell(destinationAfterUpdate) : null,
				ghostAfterUpdate = ghostAfterUpdate != null,
				ghostVecAfterUpdate = DescribeVector(ghostVecAfterUpdate),
				ghostMoved,
				jobAfterUpdate,
				stateStarted,
				jobInitialized,
				periodicLoopRan
			};
		}

		[Tool("zombieland/contamination_mimic_contract", Description = "Verify the contamination mimic effect survives RimWorld's 30-tick think-tree pass, tracks a victim, scares them, and starts their flee job.")]
		public static object ContaminationMimicContract()
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var existingPawns = map.mapPawns.AllPawnsSpawned.ToArray();
			var existingColonists = map.mapPawns.FreeColonists.ToArray();
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			var offsets = new[]
			{
				new IntVec3(2, 0, 0),
				new IntVec3(-2, 0, 0),
				new IntVec3(0, 0, 2),
				new IntVec3(0, 0, -2)
			};
			bool IsClearCell(IntVec3 candidate)
			{
				return candidate.InBounds(map)
					&& candidate.Standable(map)
					&& candidate.Fogged(map) == false
					&& candidate.GetThingList(map).Any(thing => thing is Pawn) == false;
			}
			Pawn GenerateMobileColonist()
			{
				for (var i = 0; i < 10; i++)
				{
					var pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
					if (pawn.Downed == false && pawn.health?.capacities?.CapableOf(PawnCapacityDefOf.Moving) == true)
						return pawn;
				}
				return PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			}

			var mimicCell = IntVec3.Invalid;
			var victimCell = IntVec3.Invalid;
			var secondVictimCell = IntVec3.Invalid;
			var searchRadius = Math.Min(70f, Math.Min(map.Size.x, map.Size.z) / 2f - 1f);
			foreach (var candidate in GenRadial.RadialCellsAround(root, searchRadius, true).OrderByDescending(cell => cell.DistanceToSquared(root)))
			{
				if (IsClearCell(candidate) == false)
					continue;
				if (existingPawns.Any(pawn => pawn.Position.DistanceToSquared(candidate) < 100))
					continue;

				foreach (var offset in offsets)
				{
					var candidateVictimCell = candidate + offset;
					var farOffset = new IntVec3(offset.x * 6, 0, offset.z * 6);
					var candidateSecondVictimCell = candidate + farOffset;
					if (IsClearCell(candidateVictimCell) == false)
						continue;
					if (IsClearCell(candidateSecondVictimCell) == false)
						continue;
					var victimDistance = candidate.DistanceToSquared(candidateVictimCell);
					if (existingColonists.Any(colonist => colonist.Position.DistanceToSquared(candidate) <= victimDistance))
						continue;
					if (map.reachability.CanReach(candidate, candidateVictimCell, PathEndMode.ClosestTouch, TraverseMode.PassDoors, Danger.Deadly) == false)
						continue;
					if (map.reachability.CanReach(candidateVictimCell, candidateSecondVictimCell, PathEndMode.ClosestTouch, TraverseMode.PassDoors, Danger.Deadly) == false)
						continue;

					mimicCell = candidate;
					victimCell = candidateVictimCell;
					secondVictimCell = candidateSecondVictimCell;
					break;
				}
				if (mimicCell.IsValid)
					break;
			}
			if (mimicCell.IsValid == false)
			{
				return new
				{
					success = false,
					error = "No isolated clear mimic/victim fixture cells were found."
				};
			}

			var mimic = GenerateMobileColonist();
			var victim = GenerateMobileColonist();
			var secondVictim = GenerateMobileColonist();
			GenSpawn.Spawn(mimic, mimicCell, map, Rot4.South);
			GenSpawn.Spawn(victim, victimCell, map, Rot4.South);
			GenSpawn.Spawn(secondVictim, secondVictimCell, map, Rot4.South);
			DisablePawnWork(mimic);
			DisablePawnWork(victim);
			DisablePawnWork(secondVictim);
			mimic.needs?.AddOrRemoveNeedsAsAppropriate();
			victim.needs?.AddOrRemoveNeedsAsAppropriate();
			secondVictim.needs?.AddOrRemoveNeedsAsAppropriate();
			victim.drafter.Drafted = true;
			secondVictim.drafter.Drafted = true;
			victim.jobs.StopAll(false, false);
			secondVictim.jobs.StopAll(false, false);
			mimic.ClearContamination();
			victim.ClearContamination();
			secondVictim.ClearContamination();
			mimic.mindState?.mentalStateHandler?.Reset();
			victim.mindState?.mentalStateHandler?.Reset();
			secondVictim.mindState?.mentalStateHandler?.Reset();

			const float mimicMin = 0.50f;
			const float mimicMax = 1.00f;
			const float mimicContamination = 0.80f;
			var factor = Mathf.InverseLerp(mimicMin, mimicMax, mimicContamination);
			var expectedRecoverAfterTicks = (GenDate.TicksPerHour / 10) * (int)(1 + factor * 7);

			var victimMemoriesBefore = MemoryDefCounts(victim);
			var applied = ContaminationEffect.Mimicing(mimic, factor);
			var mentalStateAfterApply = mimic.mindState?.mentalStateHandler?.CurStateDef?.defName;
			var jobAfterApply = mimic.CurJobDef?.defName;
			var recoverAfterApply = mimic.mindState?.mentalStateHandler?.CurState?.forceRecoverAfterTicks ?? -1;

			AdvanceGameTicks(1);
			var driverAfterInit = mimic.jobs?.curDriver as JobDriver_ContaminationMimic;
			var trackedVictimAfterInit = driverAfterInit?.victim;
			var movingAfterInit = mimic.pather?.Moving ?? false;
			var pathDestinationAfterInit = movingAfterInit ? mimic.pather.Destination.Cell : IntVec3.Invalid;

			const int sourceDerivedThinkTreeWindow = 30;
			AdvanceGameTicks(sourceDerivedThinkTreeWindow);
			var driverAfterThinkTreeWindow = mimic.jobs?.curDriver as JobDriver_ContaminationMimic;
			var jobAfterThinkTreeWindow = mimic.CurJobDef?.defName;
			var trackedVictimAfterThinkTreeWindow = driverAfterThinkTreeWindow?.victim;

			var previousVictim = driverAfterThinkTreeWindow?.previousVictims;
			var escapeJobStarted = victim.CurJobDef == JobDefOf.Flee || victim.CurJobDef == JobDefOf.FleeAndCower;
			var victimMovedFromSpawn = victim.Position != victimCell;
			var victimJobDuringEscape = victim.CurJobDef?.defName;
			var ticksUntilScare = 0;
			var maxArrivalTicks = Math.Max(1, expectedRecoverAfterTicks - sourceDerivedThinkTreeWindow - 1);
			while (ticksUntilScare < maxArrivalTicks && previousVictim != victim && escapeJobStarted == false)
			{
				AdvanceGameTicks(1);
				ticksUntilScare++;
				if (mimic.jobs?.curDriver is JobDriver_ContaminationMimic currentDriver)
					previousVictim = currentDriver.previousVictims;
				else
					victimMovedFromSpawn = victim.Position != victimCell;
				escapeJobStarted = victim.CurJobDef == JobDefOf.Flee || victim.CurJobDef == JobDefOf.FleeAndCower;
				if (escapeJobStarted)
					victimJobDuringEscape = victim.CurJobDef?.defName;
			}

			var driverAfterScare = mimic.jobs?.curDriver as JobDriver_ContaminationMimic;
			var victimMemoriesAfter = MemoryDefCounts(victim);
			victimMemoriesBefore.TryGetValue(CustomDefs.ZombieScare.defName, out var zombieScareBefore);
			victimMemoriesAfter.TryGetValue(CustomDefs.ZombieScare.defName, out var zombieScareAfter);
			var zombieScareMemoryGained = zombieScareAfter > zombieScareBefore;

			var stateStarted = applied
				&& mentalStateAfterApply == EffectDefs.ContaminationStateMimicing.defName
				&& jobAfterApply == EffectDefs.ContaminationJobMimic.defName
				&& recoverAfterApply == expectedRecoverAfterTicks;
			var jobInitialized = driverAfterInit != null
				&& trackedVictimAfterInit == victim
				&& movingAfterInit
				&& pathDestinationAfterInit == victimCell;
			var survivedThinkTreeWindow = driverAfterThinkTreeWindow != null
				&& jobAfterThinkTreeWindow == EffectDefs.ContaminationJobMimic.defName
				&& (trackedVictimAfterThinkTreeWindow == victim || trackedVictimAfterThinkTreeWindow == secondVictim);
			var victimScared = previousVictim == victim
				&& zombieScareMemoryGained
				&& escapeJobStarted;

			return new
			{
				success = stateStarted && jobInitialized && survivedThinkTreeWindow && victimScared,
				mimic = DescribePawn(mimic),
				victim = DescribePawn(victim),
				secondVictim = DescribePawn(secondVictim),
				mimicCell = ZombieRuntimeActions.DescribeCell(mimicCell),
				victimCell = ZombieRuntimeActions.DescribeCell(victimCell),
				secondVictimCell = ZombieRuntimeActions.DescribeCell(secondVictimCell),
				mimicContamination,
				factor,
				expectedRecoverAfterTicks,
				applied,
				mentalStateAfterApply,
				expectedMentalState = EffectDefs.ContaminationStateMimicing.defName,
				jobAfterApply,
				expectedJob = EffectDefs.ContaminationJobMimic.defName,
				recoverAfterApply,
				trackedVictimAfterInit = ZombieRuntimeActions.StableThingId(trackedVictimAfterInit),
				movingAfterInit,
				pathDestinationAfterInit = pathDestinationAfterInit.IsValid ? ZombieRuntimeActions.DescribeCell(pathDestinationAfterInit) : null,
				sourceDerivedThinkTreeWindow,
				jobAfterThinkTreeWindow,
				trackedVictimAfterThinkTreeWindow = ZombieRuntimeActions.StableThingId(trackedVictimAfterThinkTreeWindow),
				ticksUntilScare,
				maxArrivalTicks,
				previousVictim = ZombieRuntimeActions.StableThingId(previousVictim),
				driverAfterScareStillMimic = driverAfterScare != null,
				escapeJobStarted,
				victimMovedFromSpawn,
				victimJobDuringEscape,
				victimJobAfterScare = victim.CurJobDef?.defName,
				victimMemoriesBefore,
				victimMemoriesAfter,
				zombieScareBefore,
				zombieScareAfter,
				zombieScareMemoryGained,
				stateStarted,
				jobInitialized,
				survivedThinkTreeWindow,
				victimScared
			};
		}

		[Tool("zombieland/contamination_sleepwalk_contract", Description = "Verify the sleepwalk contamination effect starts from a real sleeping pawn, reaches an occupied bed, wakes the occupant, and starts their flee job.")]
		public static object ContaminationSleepwalkContract()
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var avoidGrid = map.GetComponent<TickManager>()?.avoidGrid;
			var existingPawns = map.mapPawns.AllPawnsSpawned.ToArray();
			var existingZombies = CurrentZombies(map);
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			var sleepwalkerCell = IntVec3.Invalid;
			var bedCell = IntVec3.Invalid;
			var searchRadius = Math.Min(70f, Math.Min(map.Size.x, map.Size.z) / 2f - 1f);
			foreach (var candidate in GenRadial.RadialCellsAround(root, searchRadius, true).OrderByDescending(cell => cell.DistanceToSquared(root)))
			{
				if (candidate.InBounds(map) == false || candidate.Standable(map) == false || candidate.Fogged(map))
					continue;
				if (candidate.GetThingList(map).Any(thing => thing is Pawn))
					continue;
				if (avoidGrid?.ShouldAvoid(map, candidate) == true)
					continue;
				if (existingPawns.Any(pawn => pawn.Position.DistanceToSquared(candidate) < 400))
					continue;
				if (existingZombies.Any(zombie => zombie.Position.DistanceToSquared(candidate) < 900))
					continue;
				if (TryFindClearBuildingFootprint(map, ThingDefOf.Bed, candidate + new IntVec3(8, 0, 0), 12f, out var candidateBedCell, out _) == false)
					continue;
				if (avoidGrid?.ShouldAvoid(map, candidateBedCell) == true)
					continue;

				sleepwalkerCell = candidate;
				bedCell = candidateBedCell;
				break;
			}
			if (sleepwalkerCell.IsValid == false)
			{
				return new
				{
					success = false,
					error = "No isolated sleepwalk fixture cells were found away from existing pawns and zombies."
				};
			}

			var bed = ThingMaker.MakeThing(ThingDefOf.Bed, GenStuff.DefaultStuffFor(ThingDefOf.Bed)) as Building_Bed;
			if (bed == null)
			{
				return new
				{
					success = false,
					error = "Could not create a bed for the sleepwalk fixture."
				};
			}
			bed.SetFactionDirect(Faction.OfPlayer);
			GenSpawn.Spawn(bed, bedCell, map, Rot4.North, WipeMode.Vanish, false);

			var occupantCell = bed.GetSleepingSlotPos(0);
			var sleepwalker = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var occupant = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(sleepwalker, sleepwalkerCell, map, Rot4.South);
			GenSpawn.Spawn(occupant, occupantCell, map, Rot4.South);
			DisablePawnWork(sleepwalker);
			DisablePawnWork(occupant);
			sleepwalker.needs?.AddOrRemoveNeedsAsAppropriate();
			occupant.needs?.AddOrRemoveNeedsAsAppropriate();
			sleepwalker.ClearContamination();
			occupant.ClearContamination();
			sleepwalker.mindState?.mentalStateHandler?.Reset();
			occupant.mindState?.mentalStateHandler?.Reset();

			var occupantSleepJob = JobMaker.MakeJob(JobDefOf.LayDown, bed);
			occupantSleepJob.forceSleep = true;
			occupant.jobs.ClearQueuedJobs();
			occupant.jobs.StartJob(occupantSleepJob, JobCondition.InterruptForced, null);
			var sleepwalkerSleepJob = JobMaker.MakeJob(JobDefOf.LayDown, sleepwalkerCell);
			sleepwalkerSleepJob.forceSleep = true;
			sleepwalker.jobs.ClearQueuedJobs();
			sleepwalker.jobs.StartJob(sleepwalkerSleepJob, JobCondition.InterruptForced, null);

			var sleepPrepTicks = 0;
			const int maxSleepPrepTicks = 180;
			while (sleepPrepTicks < maxSleepPrepTicks
				&& (sleepwalker.jobs?.curDriver?.asleep != true || occupant.jobs?.curDriver?.asleep != true || bed.CurOccupants.Contains(occupant) == false))
			{
				AdvanceGameTicks(1);
				sleepPrepTicks++;
			}
			var sleepwalkerAsleepBeforeApply = sleepwalker.jobs?.curDriver?.asleep == true;
			var occupantAsleepBeforeApply = occupant.jobs?.curDriver?.asleep == true;
			var bedOccupiedBeforeApply = bed.CurOccupants.Contains(occupant);

			const float sleepwalkMin = 0.35f;
			const float sleepwalkMax = 0.50f;
			const float sleepwalkContamination = 0.44f;
			var factor = Mathf.InverseLerp(sleepwalkMin, sleepwalkMax, sleepwalkContamination);
			var expectedRecoverAfterTicks = (GenDate.TicksPerHour / 10) * (int)(1 + factor * 7);

			var applied = ContaminationEffect.Sleepwalk(sleepwalker, factor);
			var mentalStateAfterApply = sleepwalker.mindState?.mentalStateHandler?.CurStateDef?.defName;
			var jobAfterApply = sleepwalker.CurJobDef?.defName;
			var recoverAfterApply = sleepwalker.mindState?.mentalStateHandler?.CurState?.forceRecoverAfterTicks ?? -1;

			AdvanceGameTicks(1);
			var driverAfterInit = sleepwalker.jobs?.curDriver as JobDriver_ContaminationSleepwalk;
			var trackedBedAfterInit = driverAfterInit?.bed;
			var movingAfterInit = sleepwalker.pather?.Moving ?? false;
			var pathDestinationAfterInit = movingAfterInit ? sleepwalker.pather.Destination.Cell : IntVec3.Invalid;

			const int sourceDerivedThinkTreeWindow = 30;
			AdvanceGameTicks(sourceDerivedThinkTreeWindow);
			var driverAfterThinkTreeWindow = sleepwalker.jobs?.curDriver as JobDriver_ContaminationSleepwalk;
			var jobAfterThinkTreeWindow = sleepwalker.CurJobDef?.defName;
			var trackedBedAfterThinkTreeWindow = driverAfterThinkTreeWindow?.bed;

			var occupantFleeStarted = occupant.CurJobDef == JobDefOf.Flee || occupant.CurJobDef == JobDefOf.FleeAndCower;
			var occupantAwakeAfterApply = RestUtility.Awake(occupant);
			var waitUntilAfterArrival = driverAfterThinkTreeWindow?.waitUntil ?? -1;
			var ticksUntilWake = 0;
			var maxArrivalTicks = Math.Max(1, expectedRecoverAfterTicks - sourceDerivedThinkTreeWindow - 1);
			while (ticksUntilWake < maxArrivalTicks && occupantFleeStarted == false)
			{
				AdvanceGameTicks(1);
				ticksUntilWake++;
				occupantFleeStarted = occupant.CurJobDef == JobDefOf.Flee || occupant.CurJobDef == JobDefOf.FleeAndCower;
				occupantAwakeAfterApply |= RestUtility.Awake(occupant);
				if (sleepwalker.jobs?.curDriver is JobDriver_ContaminationSleepwalk currentDriver)
					waitUntilAfterArrival = currentDriver.waitUntil;
				else
					break;
			}

			var driverAfterWake = sleepwalker.jobs?.curDriver as JobDriver_ContaminationSleepwalk;
			var stateStarted = applied
				&& mentalStateAfterApply == EffectDefs.ContaminationStateSleepwalking.defName
				&& jobAfterApply == EffectDefs.ContaminationJobSleepwalk.defName
				&& recoverAfterApply == expectedRecoverAfterTicks;
			var jobInitialized = driverAfterInit != null
				&& trackedBedAfterInit == bed
				&& movingAfterInit
				&& pathDestinationAfterInit == bed.Position;
			var survivedThinkTreeWindow = driverAfterThinkTreeWindow != null
				&& jobAfterThinkTreeWindow == EffectDefs.ContaminationJobSleepwalk.defName
				&& trackedBedAfterThinkTreeWindow == bed;
			var occupantWokenAndFleeing = occupantAwakeAfterApply
				&& occupantFleeStarted
				&& waitUntilAfterArrival > Find.TickManager.TicksGame;

			return new
			{
				success = sleepwalkerAsleepBeforeApply && occupantAsleepBeforeApply && bedOccupiedBeforeApply && stateStarted && jobInitialized && survivedThinkTreeWindow && occupantWokenAndFleeing,
				sleepwalker = DescribePawn(sleepwalker),
				occupant = DescribePawn(occupant),
				bed = new
				{
					id = ZombieRuntimeActions.StableThingId(bed),
					cell = ZombieRuntimeActions.DescribeCell(bed.Position),
					occupantCell = ZombieRuntimeActions.DescribeCell(occupantCell),
					occupants = bed.CurOccupants.Select(ZombieRuntimeActions.StableThingId).ToArray()
				},
				sleepwalkerCell = ZombieRuntimeActions.DescribeCell(sleepwalkerCell),
				sleepPrepTicks,
				maxSleepPrepTicks,
				sleepwalkerAsleepBeforeApply,
				occupantAsleepBeforeApply,
				bedOccupiedBeforeApply,
				sleepwalkContamination,
				factor,
				expectedRecoverAfterTicks,
				applied,
				mentalStateAfterApply,
				expectedMentalState = EffectDefs.ContaminationStateSleepwalking.defName,
				jobAfterApply,
				expectedJob = EffectDefs.ContaminationJobSleepwalk.defName,
				recoverAfterApply,
				trackedBedAfterInit = ZombieRuntimeActions.StableThingId(trackedBedAfterInit),
				movingAfterInit,
				pathDestinationAfterInit = pathDestinationAfterInit.IsValid ? ZombieRuntimeActions.DescribeCell(pathDestinationAfterInit) : null,
				sourceDerivedThinkTreeWindow,
				jobAfterThinkTreeWindow,
				trackedBedAfterThinkTreeWindow = ZombieRuntimeActions.StableThingId(trackedBedAfterThinkTreeWindow),
				ticksUntilWake,
				maxArrivalTicks,
				driverAfterWakeStillSleepwalk = driverAfterWake != null,
				waitUntilAfterArrival,
				currentTicks = Find.TickManager.TicksGame,
				occupantAwakeAfterApply,
				occupantFleeStarted,
				occupantJobAfterWake = occupant.CurJobDef?.defName,
				stateStarted,
				jobInitialized,
				survivedThinkTreeWindow,
				occupantWokenAndFleeing
			};
		}

		[Tool("zombieland/contamination_hoard_pather_failure_contract", Description = "Verify the hoarding contamination job survives a pather failure without ending as ErroredPather.")]
		public static object ContaminationHoardPatherFailureContract(
			[ToolParameter(Description = "Ticks to advance after the forced pather failure before evaluating durability. Use 0 to leave the prepared state for manual inspection.", Required = false, DefaultValue = 60)] int durableWindowTicks = 60)
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryBuildFogRoomFixture(map, root, 32f, out var fixture, out var fixtureError) == false)
				return fixtureError;
			if (TryBuildFogRoomFixture(map, fixture.doorCell + new IntVec3(10, 0, 0), 32f, out var sourceFixture, out var sourceError) == false)
				return sourceError;
			var bedCell = fixture.interiorRect.CenterCell;

			var bed = ThingMaker.MakeThing(ThingDefOf.Bed, GenStuff.DefaultStuffFor(ThingDefOf.Bed)) as Building_Bed;
			if (bed == null)
			{
				return new
				{
					success = false,
					error = "Could not create a bed for the hoarding pather-failure fixture."
				};
			}
			bed.SetFactionDirect(Faction.OfPlayer);
			GenSpawn.Spawn(bed, bedCell, map, Rot4.North, WipeMode.Vanish, false);

			var sourceThingCell = sourceFixture.interiorRect.CenterCell;
			var sourceThing = ThingMaker.MakeThing(ThingDefOf.Silver);
			sourceThing.stackCount = ThingDefOf.Silver.stackLimit;
			GenSpawn.Spawn(sourceThing, sourceThingCell, map, WipeMode.Vanish);
			map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
			var bedroom = bed.GetRoom(RegionType.Set_All);
			var sourceRoom = sourceThingCell.GetRoom(map);
			if (bedroom == null || sourceRoom == null || bedroom == sourceRoom || bedroom.IsHuge || sourceRoom.IsHuge)
			{
				return new
				{
					success = false,
					bedroomExists = bedroom != null,
					sourceRoomExists = sourceRoom != null,
					sameRoom = bedroom != null && bedroom == sourceRoom,
					bedroomHuge = bedroom?.IsHuge,
					sourceRoomHuge = sourceRoom?.IsHuge,
					error = "The hoarding pather-failure fixture did not produce two distinct non-huge rooms."
				};
			}

			var hoarderCell = fixture.interiorRect.Cells
				.Where(cell => cell.InBounds(map)
					&& cell.Standable(map)
					&& cell.GetEdifice(map) == null
					&& cell.GetThingList(map).Any(thing => thing is Pawn) == false)
				.OrderByDescending(cell => cell.DistanceToSquared(bedCell))
				.FirstOrDefault();
			if (hoarderCell.IsValid == false)
			{
				return new
				{
					success = false,
					error = "Could not find a clear hoarder cell in the fixture room."
				};
			}

			var hoarder = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(hoarder, hoarderCell, map, Rot4.South);
			DisablePawnWork(hoarder);
			hoarder.needs?.AddOrRemoveNeedsAsAppropriate();
			hoarder.ClearContamination();
			hoarder.mindState?.mentalStateHandler?.Reset();
			bed.CompAssignableToPawn?.TryAssignPawn(hoarder);
			bed.NotifyRoomAssignedPawnsChanged();

			const float hoardingContamination = 0.54f;
			var factor = Mathf.InverseLerp(0.45f, 0.60f, hoardingContamination);
			var applied = ContaminationEffect.Hoarding(hoarder, factor);
			AdvanceGameTicks(1);
			var driver = hoarder.jobs?.curDriver as JobDriver_ContaminationHoard;
			if (applied == false || driver == null)
			{
				return new
				{
					success = false,
					applied,
					hoarder = DescribePawn(hoarder),
					job = hoarder.CurJobDef?.defName,
					error = "The hoarding contamination job did not start."
				};
			}

			var unreachableThing = ThingMaker.MakeThing(ThingDefOf.Silver);
			unreachableThing.stackCount = 1;
			driver.state = JobDriver_ContaminationHoard.State.moveToThing;
			driver.thing = unreachableThing;
			driver.rejectedThings.Clear();
			driver.Notify_PatherFailed();
			var driverAfterFailure = hoarder.jobs?.curDriver as JobDriver_ContaminationHoard;
			var rejected = driverAfterFailure?.rejectedThings.Contains(unreachableThing) ?? false;
			var survived = driverAfterFailure != null
				&& hoarder.CurJobDef == EffectDefs.ContaminationJobHoard
				&& driverAfterFailure.state == JobDriver_ContaminationHoard.State.findThing
				&& driverAfterFailure.thing == null
				&& rejected;
			var jobAfterFailure = hoarder.CurJobDef?.defName;
			var driverStateAfterFailure = driverAfterFailure?.state.ToString();
			var thingClearedAfterFailure = driverAfterFailure?.thing == null;
			var clampedDurableWindowTicks = Mathf.Clamp(durableWindowTicks, 0, 5000);
			if (clampedDurableWindowTicks > 0)
				AdvanceGameTicks(clampedDurableWindowTicks);
			var driverAfterDurableWindow = hoarder.jobs?.curDriver as JobDriver_ContaminationHoard;
			var durableSurvived = survived
				&& driverAfterDurableWindow != null
				&& hoarder.CurJobDef == EffectDefs.ContaminationJobHoard
				&& driverAfterDurableWindow.rejectedThings.Contains(unreachableThing)
				&& (driverAfterDurableWindow.thing == null || driverAfterDurableWindow.thing != unreachableThing);

			return new
			{
				success = durableSurvived,
				hoarder = DescribePawn(hoarder),
				hoardingContamination,
				applied,
				jobAfterFailure,
				driverStateAfterFailure,
				thingCleared = thingClearedAfterFailure,
				rejected,
				survived,
				durableWindowTicks = clampedDurableWindowTicks,
				jobAfterDurableWindow = hoarder.CurJobDef?.defName,
				driverStateAfterDurableWindow = driverAfterDurableWindow?.state.ToString(),
				selectedThingAfterDurableWindow = ZombieRuntimeActions.StableThingId(driverAfterDurableWindow?.thing),
				sourceThing = ZombieRuntimeActions.StableThingId(sourceThing),
				sourceThingCell = ZombieRuntimeActions.DescribeCell(sourceThingCell),
				durableSurvived
			};
		}

		[Tool("zombieland/contamination_hoard_driver_flow_contract", Description = "Verify the hoarding contamination job initializes from a real assigned room and runs its pickup/drop arrival callbacks.")]
		public static object ContaminationHoardDriverFlowContract()
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryBuildFogRoomFixture(map, root, 32f, out var bedroomFixture, out var bedroomError) == false)
				return bedroomError;
			if (TryBuildFogRoomFixture(map, bedroomFixture.doorCell + new IntVec3(10, 0, 0), 32f, out var sourceFixture, out var sourceError) == false)
				return sourceError;

			var bedCell = bedroomFixture.interiorRect.CenterCell;
			var bed = ThingMaker.MakeThing(ThingDefOf.Bed, GenStuff.DefaultStuffFor(ThingDefOf.Bed)) as Building_Bed;
			if (bed == null)
			{
				return new
				{
					success = false,
					error = "Could not create a bed for the hoarding flow fixture."
				};
			}
			bed.SetFactionDirect(Faction.OfPlayer);
			GenSpawn.Spawn(bed, bedCell, map, Rot4.North, WipeMode.Vanish, false);

			var sourceThingCell = sourceFixture.interiorRect.CenterCell;
			var sourceThing = ThingMaker.MakeThing(ThingDefOf.Silver);
			sourceThing.stackCount = ThingDefOf.Silver.stackLimit;
			GenSpawn.Spawn(sourceThing, sourceThingCell, map, WipeMode.Vanish);

			map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
			var bedroom = bed.GetRoom(RegionType.Set_All);
			var sourceRoom = sourceThingCell.GetRoom(map);
			if (bedroom == null || sourceRoom == null || bedroom == sourceRoom || bedroom.IsHuge || sourceRoom.IsHuge)
			{
				return new
				{
					success = false,
					bedroomExists = bedroom != null,
					sourceRoomExists = sourceRoom != null,
					sameRoom = bedroom != null && bedroom == sourceRoom,
					bedroomHuge = bedroom?.IsHuge,
					sourceRoomHuge = sourceRoom?.IsHuge,
					error = "The hoarding flow fixture did not produce two distinct non-huge rooms."
				};
			}

			var hoarderCell = bedroomFixture.interiorRect.Cells
				.Where(cell => cell.InBounds(map)
					&& cell.Standable(map)
					&& cell.GetEdifice(map) == null
					&& cell.GetThingList(map).Any(thing => thing is Pawn) == false)
				.OrderByDescending(cell => cell.DistanceToSquared(bedCell))
				.FirstOrDefault();
			if (hoarderCell.IsValid == false)
			{
				return new
				{
					success = false,
					error = "Could not find a clear hoarder cell in the bedroom fixture."
				};
			}

			var hoarder = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(hoarder, hoarderCell, map, Rot4.South);
			DisablePawnWork(hoarder);
			hoarder.needs?.AddOrRemoveNeedsAsAppropriate();
			hoarder.ClearContamination();
			hoarder.mindState?.mentalStateHandler?.Reset();
			bed.CompAssignableToPawn?.TryAssignPawn(hoarder);
			bed.NotifyRoomAssignedPawnsChanged();

			const float hoardingContamination = 0.54f;
			var factor = Mathf.InverseLerp(0.45f, 0.60f, hoardingContamination);
			var expectedRecoverAfterTicks = (GenDate.TicksPerHour / 10) * (int)(1 + factor * 7);
			var applied = ContaminationEffect.Hoarding(hoarder, factor);
			var jobAfterApply = hoarder.CurJobDef?.defName;
			var recoverAfterApply = hoarder.mindState?.mentalStateHandler?.CurState?.forceRecoverAfterTicks ?? -1;

			AdvanceGameTicks(1);
			var driverAfterInit = hoarder.jobs?.curDriver as JobDriver_ContaminationHoard;
			var selectedThing = driverAfterInit?.thing;
			var storageCell = driverAfterInit?.cell ?? IntVec3.Invalid;
			var initialized = applied
				&& jobAfterApply == EffectDefs.ContaminationJobHoard.defName
				&& recoverAfterApply == expectedRecoverAfterTicks
				&& driverAfterInit != null
				&& driverAfterInit.room == bedroom
				&& driverAfterInit.state == JobDriver_ContaminationHoard.State.moveToThing
				&& selectedThing != null
				&& selectedThing.def == ThingDefOf.Silver
				&& storageCell.IsValid;
			if (initialized == false)
			{
				return new
				{
					success = false,
					hoarder = DescribePawn(hoarder),
					hoardingContamination,
					applied,
					jobAfterApply,
					currentJob = hoarder.CurJobDef?.defName,
					recoverAfterApply,
					expectedRecoverAfterTicks,
					driverExists = driverAfterInit != null,
					driverState = driverAfterInit?.state.ToString(),
					selectedThing = ZombieRuntimeActions.StableThingId(selectedThing),
					selectedThingDef = selectedThing?.def?.defName,
					storageCell = storageCell.IsValid ? ZombieRuntimeActions.DescribeCell(storageCell) : null,
					error = "The hoarding driver did not initialize into a move-to-thing state."
				};
			}

			const int sourceDerivedThinkTreeWindow = 30;
			var positionBeforeThinkTreeWindow = hoarder.Position;
			AdvanceGameTicks(sourceDerivedThinkTreeWindow);
			var driverAfterThinkTreeWindow = hoarder.jobs?.curDriver as JobDriver_ContaminationHoard;
			var selectedThingAfterThinkTreeWindow = driverAfterThinkTreeWindow?.thing;
			var survivedThinkTreeWindow = driverAfterThinkTreeWindow != null
				&& hoarder.CurJobDef == EffectDefs.ContaminationJobHoard
				&& driverAfterThinkTreeWindow.state == JobDriver_ContaminationHoard.State.moveToThing
				&& selectedThingAfterThinkTreeWindow != null
				&& selectedThingAfterThinkTreeWindow.def == ThingDefOf.Silver;
			if (survivedThinkTreeWindow == false)
			{
				return new
				{
					success = false,
					hoarder = DescribePawn(hoarder),
					hoardingContamination,
					initialized,
					sourceDerivedThinkTreeWindow,
					jobAfterThinkTreeWindow = hoarder.CurJobDef?.defName,
					driverAfterThinkTreeWindowExists = driverAfterThinkTreeWindow != null,
					driverStateAfterThinkTreeWindow = driverAfterThinkTreeWindow?.state.ToString(),
					selectedThingAfterThinkTreeWindow = ZombieRuntimeActions.StableThingId(selectedThingAfterThinkTreeWindow),
					selectedThingDefAfterThinkTreeWindow = selectedThingAfterThinkTreeWindow?.def?.defName,
					error = "The hoarding job did not survive the source-derived think-tree window."
				};
			}

			hoarder.pather.StopDead();
			driverAfterThinkTreeWindow.Notify_PatherArrived();
			var carriedAfterPickup = hoarder.carryTracker.CarriedThing;
			var stateAfterPickup = driverAfterThinkTreeWindow.state;
			var pickedUp = carriedAfterPickup != null
				&& carriedAfterPickup.def == ThingDefOf.Silver
				&& stateAfterPickup == JobDriver_ContaminationHoard.State.moveToStorage;

			hoarder.pather.StopDead();
			driverAfterThinkTreeWindow.Notify_PatherArrived();
			var carriedAfterDrop = hoarder.carryTracker.CarriedThing;
			var droppedThing = storageCell.GetThingList(map).FirstOrDefault(thing => thing.def == ThingDefOf.Silver);
			var droppedInBedroom = droppedThing != null && bedroom.Cells.Contains(droppedThing.Position);
			var dropped = pickedUp
				&& carriedAfterDrop == null
				&& droppedInBedroom
				&& driverAfterThinkTreeWindow.state == JobDriver_ContaminationHoard.State.findThing;

			return new
			{
				success = initialized && survivedThinkTreeWindow && pickedUp && dropped,
				hoarder = DescribePawn(hoarder),
				hoardingContamination,
				applied,
				expectedRecoverAfterTicks,
				recoverAfterApply,
				sourceDerivedThinkTreeWindow,
				positionBeforeThinkTreeWindow = ZombieRuntimeActions.DescribeCell(positionBeforeThinkTreeWindow),
				positionAfterThinkTreeWindow = ZombieRuntimeActions.DescribeCell(hoarder.Position),
				bedroom = new
				{
					center = ZombieRuntimeActions.DescribeCell(bedroomFixture.interiorRect.CenterCell),
					cellCount = bedroom.CellCount
				},
				sourceRoom = new
				{
					center = ZombieRuntimeActions.DescribeCell(sourceThingCell),
					cellCount = sourceRoom.CellCount
				},
				selectedThing = ZombieRuntimeActions.StableThingId(selectedThing),
				selectedThingDef = selectedThing?.def?.defName,
				storageCell = ZombieRuntimeActions.DescribeCell(storageCell),
				carriedAfterPickup = ZombieRuntimeActions.StableThingId(carriedAfterPickup),
				carriedAfterPickupDef = carriedAfterPickup?.def?.defName,
				stateAfterPickup = stateAfterPickup.ToString(),
				carriedAfterDrop = ZombieRuntimeActions.StableThingId(carriedAfterDrop),
				droppedThing = ZombieRuntimeActions.StableThingId(droppedThing),
				droppedThingCell = droppedThing?.Spawned == true ? ZombieRuntimeActions.DescribeCell(droppedThing.Position) : null,
				driverStateAfterDrop = driverAfterThinkTreeWindow.state.ToString(),
				initialized,
				survivedThinkTreeWindow,
				pickedUp,
				dropped
			};
		}

		[Tool("zombieland/contamination_breakdown_contract", Description = "Verify the breakdown contamination effect starts the real job, immediately picks a flee path, and survives RimWorld's 30-tick think-tree pass.")]
		public static object ContaminationBreakdownContract()
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var avoidGrid = map.GetComponent<TickManager>()?.avoidGrid;
			var existingPawns = map.mapPawns.AllPawnsSpawned.ToArray();
			var existingZombies = CurrentZombies(map);
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			var pawnCell = IntVec3.Invalid;
			var searchRadius = Math.Min(70f, Math.Min(map.Size.x, map.Size.z) / 2f - 1f);
			foreach (var candidate in GenRadial.RadialCellsAround(root, searchRadius, true).OrderByDescending(cell => cell.DistanceToSquared(root)))
			{
				if (candidate.InBounds(map) == false || candidate.Standable(map) == false || candidate.Fogged(map))
					continue;
				if (candidate.GetThingList(map).Any(thing => thing is Pawn))
					continue;
				if (avoidGrid?.ShouldAvoid(map, candidate) == true)
					continue;
				if (existingPawns.Any(pawn => pawn.Position.DistanceToSquared(candidate) < 400))
					continue;
				if (existingZombies.Any(zombie => zombie.Position.DistanceToSquared(candidate) < 900))
					continue;

				pawnCell = candidate;
				break;
			}
			if (pawnCell.IsValid == false)
			{
				return new
				{
					success = false,
					error = "No isolated breakdown fixture cell was found away from existing pawns and zombies."
				};
			}

			var pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(pawn, pawnCell, map, Rot4.South);
			DisablePawnWork(pawn);
			pawn.needs?.AddOrRemoveNeedsAsAppropriate();
			pawn.ClearContamination();
			pawn.mindState?.mentalStateHandler?.Reset();

			const float breakdownMin = 0.60f;
			const float breakdownMax = 0.80f;
			const float breakdownContamination = 0.72f;
			var factor = Mathf.InverseLerp(breakdownMin, breakdownMax, breakdownContamination);
			var expectedRecoverAfterTicks = (GenDate.TicksPerHour / 10) * (int)(1 + factor * 7);

			var applied = ContaminationEffect.Breakdown(pawn, factor);
			var mentalStateAfterApply = pawn.mindState?.mentalStateHandler?.CurStateDef?.defName;
			var jobAfterApply = pawn.CurJobDef?.defName;
			var recoverAfterApply = pawn.mindState?.mentalStateHandler?.CurState?.forceRecoverAfterTicks ?? -1;

			AdvanceGameTicks(1);
			var driverAfterInit = pawn.jobs?.curDriver as JobDriver_ContaminationBreakdown;
			var movingAfterInit = pawn.pather?.Moving ?? false;
			var pathDestinationAfterInit = movingAfterInit ? pawn.pather.Destination.Cell : IntVec3.Invalid;
			var movedAfterInit = pawn.Position != pawnCell;

			const int sourceDerivedThinkTreeWindow = 30;
			AdvanceGameTicks(sourceDerivedThinkTreeWindow);
			var driverAfterThinkTreeWindow = pawn.jobs?.curDriver as JobDriver_ContaminationBreakdown;
			var jobAfterThinkTreeWindow = pawn.CurJobDef?.defName;
			var movingAfterThinkTreeWindow = pawn.pather?.Moving ?? false;
			var pathDestinationAfterThinkTreeWindow = movingAfterThinkTreeWindow ? pawn.pather.Destination.Cell : IntVec3.Invalid;
			var movedAfterThinkTreeWindow = pawn.Position != pawnCell;

			var stateStarted = applied
				&& mentalStateAfterApply == EffectDefs.ContaminationStateBreakdown.defName
				&& jobAfterApply == EffectDefs.ContaminationJobBreakdown.defName
				&& recoverAfterApply == expectedRecoverAfterTicks;
			var jobInitialized = driverAfterInit != null
				&& (movingAfterInit || movedAfterInit)
				&& (pathDestinationAfterInit.IsValid || movedAfterInit);
			var survivedThinkTreeWindow = driverAfterThinkTreeWindow != null
				&& jobAfterThinkTreeWindow == EffectDefs.ContaminationJobBreakdown.defName
				&& (movingAfterThinkTreeWindow || movedAfterThinkTreeWindow);

			return new
			{
				success = stateStarted && jobInitialized && survivedThinkTreeWindow,
				pawn = DescribePawn(pawn),
				pawnCell = ZombieRuntimeActions.DescribeCell(pawnCell),
				breakdownContamination,
				factor,
				expectedRecoverAfterTicks,
				applied,
				mentalStateAfterApply,
				expectedMentalState = EffectDefs.ContaminationStateBreakdown.defName,
				jobAfterApply,
				expectedJob = EffectDefs.ContaminationJobBreakdown.defName,
				recoverAfterApply,
				movingAfterInit,
				pathDestinationAfterInit = pathDestinationAfterInit.IsValid ? ZombieRuntimeActions.DescribeCell(pathDestinationAfterInit) : null,
				movedAfterInit,
				sourceDerivedThinkTreeWindow,
				jobAfterThinkTreeWindow,
				movingAfterThinkTreeWindow,
				pathDestinationAfterThinkTreeWindow = pathDestinationAfterThinkTreeWindow.IsValid ? ZombieRuntimeActions.DescribeCell(pathDestinationAfterThinkTreeWindow) : null,
				movedAfterThinkTreeWindow,
				stateStarted,
				jobInitialized,
				survivedThinkTreeWindow
			};
		}

		[Tool("zombieland/contamination_ingestion_contract", Description = "Verify ingesting contaminated stack food transfers the source-derived partial-stack contamination to the eater.")]
		public static object ContaminationIngestionContract()
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var eaterCell, out var eaterSpawnError) == false)
				return eaterSpawnError;
			if (TryFindClearSpawnCell(map, eaterCell + new IntVec3(3, 0, 0), 8f, out var mealCell, out var mealSpawnError) == false)
				return mealSpawnError;

			var mealDef = ThingDefOf.MealSurvivalPack;
			var mealStack = Math.Min(5, mealDef.stackLimit);
			if (mealStack < 2)
			{
				return new
				{
					success = false,
					mealDef = mealDef.defName,
					stackLimit = mealDef.stackLimit,
					error = "Packaged survival meal is not stackable in this runtime."
				};
			}

			var eater = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(eater, eaterCell, map, Rot4.South);
			DisablePawnWork(eater);
			eater.needs?.AddOrRemoveNeedsAsAppropriate();
			eater.ClearContamination();

			var meal = ThingMaker.MakeThing(mealDef);
			meal.stackCount = mealStack;
			GenSpawn.Spawn(meal, mealCell, map, WipeMode.Vanish);
			const float mealContamination = 0.5f;
			meal.SetContamination(mealContamination);

			var eaterBefore = DescribeContamination(eater);
			var mealBefore = meal.GetContamination();
			var mealStackBefore = meal.stackCount;
			var nutritionWanted = meal.GetStatValue(StatDefOf.Nutrition);
			var nutritionIngested = meal.Ingested(eater, nutritionWanted);
			var eaterAfter = DescribeContamination(eater);
			var mealDestroyed = meal.Destroyed;
			var mealStackAfter = mealDestroyed ? 0 : meal.stackCount;
			var mealAfter = mealDestroyed ? 0f : meal.GetContamination();
			var numTaken = mealStackBefore - mealStackAfter;
			var expectedFactor = numTaken == 0 ? 0f : numTaken / (float)mealStackBefore;
			var requestedTransfer = mealBefore * ZombieSettings.Values.contamination.ingestTransfer * expectedFactor;
			var expectedTransfer = Mathf.Min(mealBefore, requestedTransfer);
			var expectedMealAfter = Mathf.Max(0f, mealBefore - expectedTransfer);
			var expectedEaterAfter = eaterBefore.stored + expectedTransfer;

			static bool Close(float? value, float expected) => value.HasValue && Mathf.Abs(value.Value - expected) < 0.0001f;
			static bool CloseFloat(float value, float expected) => Mathf.Abs(value - expected) < 0.0001f;

			var atePartialStack = numTaken > 0 && mealStackAfter > 0 && nutritionIngested > 0f;
			var contaminationTransferred = CloseFloat(eaterAfter.stored, expectedEaterAfter)
				&& Close(eaterAfter.needLevel, expectedEaterAfter)
				&& eaterAfter.hasHediff
				&& Close(eaterAfter.hediffSeverity, expectedEaterAfter)
				&& CloseFloat(mealAfter, expectedMealAfter);

			return new
			{
				success = atePartialStack && contaminationTransferred,
				eater = DescribePawn(eater),
				eaterCell = ZombieRuntimeActions.DescribeCell(eaterCell),
				meal = ZombieRuntimeActions.StableThingId(meal),
				mealDef = mealDef.defName,
				mealCell = ZombieRuntimeActions.DescribeCell(mealCell),
				mealStackBefore,
				mealStackAfter,
				mealDestroyed,
				numTaken,
				nutritionWanted,
				nutritionIngested,
				ingestTransfer = ZombieSettings.Values.contamination.ingestTransfer,
				expectedFactor,
				requestedTransfer,
				expectedTransfer,
				eaterBefore,
				eaterAfter,
				expectedEaterAfter,
				mealBefore,
				mealAfter,
				expectedMealAfter,
				atePartialStack,
				contaminationTransferred
			};
		}

		[Tool("zombieland/contamination_tending_contract", Description = "Verify real TendUtility.DoTend transfers contamination from medicine and equalizes doctor/patient contamination.")]
		public static object ContaminationTendingContract()
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
			if (Constants.CONTAMINATION == false)
			{
				return new
				{
					success = false,
					error = "Contamination is disabled in Zombieland advanced settings."
				};
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var doctorCell, out var doctorSpawnError) == false)
				return doctorSpawnError;
			if (TryFindClearSpawnCell(map, doctorCell + new IntVec3(2, 0, 0), 8f, out var patientCell, out var patientSpawnError) == false)
				return patientSpawnError;
			if (TryFindClearSpawnCell(map, doctorCell + new IntVec3(0, 0, 2), 8f, out var medicineCell, out var medicineSpawnError) == false)
				return medicineSpawnError;

			var doctor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			var patient = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(doctor, doctorCell, map, Rot4.South);
			GenSpawn.Spawn(patient, patientCell, map, Rot4.South);
			DisablePawnWork(doctor);
			DisablePawnWork(patient);
			doctor.needs?.AddOrRemoveNeedsAsAppropriate();
			patient.needs?.AddOrRemoveNeedsAsAppropriate();
			doctor.jobs?.StopAll(false, false);
			patient.jobs?.StopAll(false, false);
			doctor.ClearContamination();
			patient.ClearContamination();
			doctor.skills.GetSkill(SkillDefOf.Medicine).Level = 0;

			var part = patient.health.hediffSet.GetNotMissingParts()
				.FirstOrDefault(record => record.def == BodyPartDefOf.Torso)
				?? patient.health.hediffSet.GetNotMissingParts().FirstOrDefault(record => record.def.alive)
				?? patient.health.hediffSet.GetNotMissingParts().FirstOrDefault();
			var wound = HediffMaker.MakeHediff(HediffDefOf.Cut, patient, part);
			wound.Severity = 5f;
			patient.health.AddHediff(wound, part);

			var medicine = (Medicine)ThingMaker.MakeThing(ThingDefOf.MedicineIndustrial);
			medicine.stackCount = 2;
			GenSpawn.Spawn(medicine, medicineCell, map, WipeMode.Vanish);
			medicine.ClearContamination();

			const float medicineInitial = 0.50f;
			const float doctorInitial = 0.10f;
			const float patientInitial = 0.00f;
			medicine.AddContamination(medicineInitial);
			doctor.AddContamination(doctorInitial);

			var medicineBefore = medicine.GetContamination();
			var doctorBefore = DescribeContamination(doctor);
			var patientBefore = DescribeContamination(patient);
			var woundNeededTendBefore = patient.health.HasHediffsNeedingTend();

			TendUtility.DoTend(doctor, patient, medicine);

			var medicineAfter = medicine.GetContamination();
			var doctorAfter = DescribeContamination(doctor);
			var patientAfter = DescribeContamination(patient);
			var woundTended = wound.IsTended();

			var medicineTransfer = ZombieSettings.Values.contamination.medicineTransfer;
			var medicineAfterPatient = medicineInitial * (1f - medicineTransfer);
			var expectedPatientAfterMedicine = patientInitial + medicineInitial * medicineTransfer;
			var medicineDoctorTransfer = medicineAfterPatient * medicineTransfer;
			var expectedMedicineAfter = medicineAfterPatient - medicineDoctorTransfer;
			var expectedDoctorAfterMedicine = doctorInitial + medicineDoctorTransfer;
			var equalizeWeight = GenMath.LerpDoubleClamped(
				0,
				20,
				ZombieSettings.Values.contamination.tendEqualizeWorst,
				ZombieSettings.Values.contamination.tendEqualizeBest,
				doctor.skills.GetSkill(SkillDefOf.Medicine).Level
			);
			var high = Mathf.Max(expectedDoctorAfterMedicine, expectedPatientAfterMedicine);
			var low = Mathf.Min(expectedDoctorAfterMedicine, expectedPatientAfterMedicine);
			var highAfterEqualize = high * (1f - equalizeWeight) + low * equalizeWeight;
			var lowAfterEqualize = low + high - highAfterEqualize;
			var expectedDoctorAfter = expectedDoctorAfterMedicine >= expectedPatientAfterMedicine ? highAfterEqualize : lowAfterEqualize;
			var expectedPatientAfter = expectedPatientAfterMedicine >= expectedDoctorAfterMedicine ? highAfterEqualize : lowAfterEqualize;
			static bool Close(float? value, float expected) => value.HasValue && Mathf.Abs(value.Value - expected) < 0.0001f;
			static bool CloseFloat(float value, float expected) => Mathf.Abs(value - expected) < 0.0001f;

			var medicineTransferred = CloseFloat(medicineAfter, expectedMedicineAfter)
				&& medicine.stackCount == 1
				&& medicine.Destroyed == false;
			var doctorPatientEqualized = CloseFloat(doctorAfter.stored, expectedDoctorAfter)
				&& Close(doctorAfter.needLevel, expectedDoctorAfter)
				&& Close(doctorAfter.hediffSeverity, expectedDoctorAfter)
				&& CloseFloat(patientAfter.stored, expectedPatientAfter)
				&& Close(patientAfter.needLevel, expectedPatientAfter)
				&& Close(patientAfter.hediffSeverity, expectedPatientAfter);

			return new
			{
				success = woundNeededTendBefore && woundTended && medicineTransferred && doctorPatientEqualized,
				doctor = DescribePawn(doctor),
				patient = DescribePawn(patient),
				medicine = new
				{
					id = ZombieRuntimeActions.StableThingId(medicine),
					thingId = medicine.ThingID,
					defName = medicine.def?.defName,
					spawned = medicine.Spawned,
					destroyed = medicine.Destroyed,
					stackCount = medicine.stackCount
				},
				doctorCell = ZombieRuntimeActions.DescribeCell(doctorCell),
				patientCell = ZombieRuntimeActions.DescribeCell(patientCell),
				medicineCell = ZombieRuntimeActions.DescribeCell(medicineCell),
				woundNeededTendBefore,
				woundTended,
				medicineBefore,
				medicineAfter,
				expectedMedicineAfter,
				medicineStackAfter = medicine.stackCount,
				medicineTransfer,
				equalizeWeight,
				doctorBefore,
				doctorAfter,
				expectedDoctorAfter,
				patientBefore,
				patientAfter,
				expectedPatientAfter,
				medicineTransferred,
				doctorPatientEqualized
			};
		}

		static string[] SplitTargetList(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
				return Array.Empty<string>();

			return value
				.Split(new[] { ';', '|', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(entry => entry.Trim())
				.Where(entry => entry.Length > 0)
					.ToArray();
		}

		static bool IsContaminationEffectPawn(Pawn pawn)
		{
			if (pawn == null || pawn.Destroyed || pawn.Spawned == false)
				return false;

			return pawn.GetContamination() > 0f
				|| IsContaminationJobDef(pawn.CurJobDef)
				|| IsContaminationMentalStateDef(pawn.mindState?.mentalStateHandler?.CurStateDef)
				|| pawn.jobs?.curDriver?.GetType().Name.StartsWith("JobDriver_Contamination", StringComparison.Ordinal) == true;
		}

		static bool IsContaminationJobDef(JobDef jobDef)
		{
			return jobDef != null
				&& (jobDef == EffectDefs.ContaminationJobForceRest
					|| jobDef == EffectDefs.ContaminationJobHallucination
					|| jobDef == EffectDefs.ContaminationJobSleepwalk
					|| jobDef == EffectDefs.ContaminationJobHoard
					|| jobDef == EffectDefs.ContaminationJobMimic
					|| jobDef == EffectDefs.ContaminationJobBreakdown
					|| jobDef.defName?.Contains("Contamination") == true);
		}

		static bool IsContaminationMentalStateDef(MentalStateDef mentalStateDef)
		{
			return mentalStateDef != null
				&& (mentalStateDef == EffectDefs.ContaminationStateForceRest
					|| mentalStateDef == EffectDefs.ContaminationStateHallucination
					|| mentalStateDef == EffectDefs.ContaminationStateSleepwalking
					|| mentalStateDef == EffectDefs.ContaminationStateHoarding
					|| mentalStateDef == EffectDefs.ContaminationStateMimicing
					|| mentalStateDef == EffectDefs.ContaminationStateBreakdown
					|| mentalStateDef.defName?.Contains("Contamination") == true);
		}

		static object DescribeContaminationEffectPawn(Pawn pawn)
		{
			if (pawn == null)
				return null;

			var mentalState = pawn.mindState?.mentalStateHandler?.CurState;
			var driver = pawn.jobs?.curDriver;
			var moving = pawn.pather?.Moving ?? false;
			var destination = moving ? pawn.pather.Destination.Cell : IntVec3.Invalid;
			var carriedThing = pawn.carryTracker?.CarriedThing;

			return new
			{
				found = true,
				pawn = DescribePawn(pawn),
				contamination = DescribeContamination(pawn),
				mentalState = new
				{
					defName = mentalState?.def?.defName,
					className = mentalState?.GetType().FullName,
					forceRecoverAfterTicks = mentalState?.forceRecoverAfterTicks ?? -1
				},
				job = new
				{
					defName = pawn.CurJobDef?.defName,
					report = pawn.CurJob?.GetReport(pawn),
					playerForced = pawn.CurJob?.playerForced,
					driverType = driver?.GetType().FullName
				},
				pather = new
				{
					moving,
					destination = destination.IsValid ? ZombieRuntimeActions.DescribeCell(destination) : null
				},
				carry = new
				{
					carriedThing = ZombieRuntimeActions.StableThingId(carriedThing),
					carriedThingDef = carriedThing?.def?.defName,
					stackCount = carriedThing?.stackCount ?? 0
				},
				driverState = DescribeContaminationDriverState(driver)
			};
		}

		static object DescribeContaminationDriverState(JobDriver driver)
		{
			if (driver == null)
				return null;

			switch (driver)
			{
				case JobDriver_ContaminationHallucination hallucination:
					return new
					{
						type = nameof(JobDriver_ContaminationHallucination),
						destination = hallucination.destination.IsValid ? ZombieRuntimeActions.DescribeCell(hallucination.destination) : null,
						ghostPresent = hallucination.ghost != null && hallucination.ghost.Destroyed == false,
						ghostVec = DescribeVector(hallucination.ghostVec)
					};
				case JobDriver_ContaminationMimic mimic:
					return new
					{
						type = nameof(JobDriver_ContaminationMimic),
						victim = ZombieRuntimeActions.StableThingId(mimic.victim),
						victimPosition = mimic.victim?.Spawned == true ? ZombieRuntimeActions.DescribeCell(mimic.victim.Position) : null,
						previousVictim = ZombieRuntimeActions.StableThingId(mimic.previousVictims)
					};
				case JobDriver_ContaminationSleepwalk sleepwalk:
					return new
					{
						type = nameof(JobDriver_ContaminationSleepwalk),
						bed = ZombieRuntimeActions.StableThingId(sleepwalk.bed),
						bedCell = sleepwalk.bed?.Spawned == true ? ZombieRuntimeActions.DescribeCell(sleepwalk.bed.Position) : null,
						occupants = sleepwalk.bed?.CurOccupants.Select(ZombieRuntimeActions.StableThingId).ToArray() ?? Array.Empty<string>(),
						waitUntil = sleepwalk.waitUntil
					};
				case JobDriver_ContaminationHoard hoard:
					return new
					{
						type = nameof(JobDriver_ContaminationHoard),
						state = hoard.state.ToString(),
						roomCellCount = hoard.room?.CellCount ?? 0,
						storageCount = hoard.storage?.Count ?? 0,
						thing = ZombieRuntimeActions.StableThingId(hoard.thing),
						thingDef = hoard.thing?.def?.defName,
						thingCell = hoard.thing?.Spawned == true ? ZombieRuntimeActions.DescribeCell(hoard.thing.Position) : null,
						cell = hoard.cell.IsValid ? ZombieRuntimeActions.DescribeCell(hoard.cell) : null,
						rejectedCount = hoard.rejectedThings?.Count ?? 0
					};
				case JobDriver_ContaminationBreakdown _:
					return new
					{
						type = nameof(JobDriver_ContaminationBreakdown)
					};
				case JobDriver_ContaminationForceRest _:
					return new
					{
						type = nameof(JobDriver_ContaminationForceRest)
					};
				default:
					return new
					{
						type = driver.GetType().FullName
					};
			}
		}

		static object DescribeContaminationCell(Map map, string entry)
		{
			if (TryParseCell(entry, out var cell) == false)
			{
				return new
				{
					query = entry,
					valid = false,
					error = "Cell must be formatted as x,z."
				};
			}

			var inBounds = cell.InBounds(map);
			return new
			{
				query = entry,
				valid = inBounds,
				cell = ZombieRuntimeActions.DescribeCell(cell),
				contamination = inBounds ? map.GetContamination(cell) : 0f,
				thingCount = inBounds ? cell.GetThingList(map).Count : 0,
				things = inBounds
					? cell.GetThingList(map).Select(DescribeContaminationThing).ToArray()
					: Array.Empty<object>()
			};
		}

		static object DescribeContaminationPawn(Map map, string entry)
		{
			if (ZombieRuntimeActions.TryFindPawn(map, entry, out var pawn, out var error) == false)
			{
				return new
				{
					query = entry,
					found = false,
					error
				};
			}

			return new
			{
				query = entry,
				found = true,
				pawn = DescribePawn(pawn),
				contamination = DescribeContamination(pawn),
				thing = DescribeContaminationThing(pawn)
			};
		}

		static object DescribeContaminationThing(Map map, string entry)
		{
			var thing = FindThing(map, entry);
			if (thing == null)
			{
				return new
				{
					query = entry,
					found = false,
					error = $"No spawned thing matched '{entry}'."
				};
			}

			return new
			{
				query = entry,
				found = true,
				thing = DescribeContaminationThing(thing),
				pawnContamination = thing is Pawn pawn ? DescribeContamination(pawn) : null
			};
		}

		static object DescribeContaminationThing(Thing thing)
		{
			if (thing == null)
				return null;

			return new
			{
				id = ZombieRuntimeActions.StableThingId(thing),
				thingId = thing.ThingID,
				defName = thing.def?.defName,
				className = thing.GetType().FullName,
				label = thing.LabelCap.ToString(),
				spawned = thing.Spawned,
				destroyed = thing.Destroyed,
				position = thing.Spawned ? ZombieRuntimeActions.DescribeCell(thing.Position) : null,
				mapIndexOrState = thing.mapIndexOrState,
				stackCount = thing.stackCount,
				hitPoints = thing.def?.useHitPoints == true ? thing.HitPoints : -1,
				contamination = thing.GetContamination(false),
				contaminationIncludingHoldings = thing.GetContamination(true),
				effectiveness = thing.GetEffectiveness(),
				heldThings = thing is IThingHolder holder
					? ThingOwnerUtility.GetAllThingsRecursively(holder, false).Select(DescribeContaminationThing).ToArray()
					: Array.Empty<object>()
			};
		}

		static bool WriteContaminationCell(Map map, string entry, float value, out object result)
		{
			if (TryParseCell(entry, out var cell) == false)
			{
				result = new
				{
					query = entry,
					success = false,
					error = "Cell must be formatted as x,z."
				};
				return false;
			}
			if (cell.InBounds(map) == false)
			{
				result = new
				{
					query = entry,
					success = false,
					cell = ZombieRuntimeActions.DescribeCell(cell),
					error = "Cell is outside the current map."
				};
				return false;
			}

			map.SetContamination(cell, value);
			result = new
			{
				query = entry,
				success = true,
				cell = ZombieRuntimeActions.DescribeCell(cell),
				contamination = map.GetContamination(cell)
			};
			return true;
		}

		static bool WriteContaminationPawn(Map map, string entry, float value, out object result)
		{
			if (ZombieRuntimeActions.TryFindPawn(map, entry, out var pawn, out var error) == false)
			{
				result = new
				{
					query = entry,
					success = false,
					error
				};
				return false;
			}

			pawn.SetContamination(value);
			result = new
			{
				query = entry,
				success = true,
				pawn = DescribePawn(pawn),
				contamination = DescribeContamination(pawn)
			};
			return true;
		}

		static bool WriteContaminationThing(Map map, string entry, float value, out object result)
		{
			var thing = FindThing(map, entry);
			if (thing == null)
			{
				result = new
				{
					query = entry,
					success = false,
					error = $"No spawned thing matched '{entry}'."
				};
				return false;
			}

			thing.SetContamination(value);
			result = new
			{
				query = entry,
				success = true,
				thing = DescribeContaminationThing(thing),
				pawnContamination = thing is Pawn pawn ? DescribeContamination(pawn) : null
			};
			return true;
		}

		static bool TryParseCell(string entry, out IntVec3 cell)
		{
			cell = IntVec3.Invalid;
			if (string.IsNullOrWhiteSpace(entry))
				return false;

			var parts = entry.Split(new[] { ',', ':' }, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length != 2)
				return false;
			if (int.TryParse(parts[0].Trim(), out var x) == false)
				return false;
			if (int.TryParse(parts[1].Trim(), out var z) == false)
				return false;

			cell = new IntVec3(x, 0, z);
			return true;
		}

		static Thing FindThing(Map map, string target)
		{
			if (map == null || string.IsNullOrWhiteSpace(target))
				return null;

			var query = target.Trim();
			return map.listerThings.AllThings.FirstOrDefault(thing =>
				string.Equals(ZombieRuntimeActions.StableThingId(thing), query, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(thing.ThingID, query, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(thing.LabelCap.ToString(), query, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(thing.LabelShort, query, StringComparison.OrdinalIgnoreCase));
		}

		sealed class BridgeThingSetMaker : ThingSetMaker
		{
			public override void Generate(ThingSetMakerParams parms, List<Thing> outThings)
			{
				var component = ThingMaker.MakeThing(ThingDefOf.ComponentIndustrial);
				component.stackCount = 3;
				outThings.Add(component);

				var steel = ThingMaker.MakeThing(ThingDefOf.Steel);
				steel.stackCount = 25;
				outThings.Add(steel);
			}

			public override IEnumerable<ThingDef> AllGeneratableThingsDebugSub(ThingSetMakerParams parms)
			{
				yield return ThingDefOf.ComponentIndustrial;
				yield return ThingDefOf.Steel;
			}
		}

		sealed class BridgeTrader : ITrader
		{
			readonly List<Thing> goods;

			public BridgeTrader(IEnumerable<Thing> goods)
				=> this.goods = goods.ToList();

			public TraderKindDef TraderKind => DefDatabase<TraderKindDef>.AllDefsListForReading.FirstOrDefault();
			public IEnumerable<Thing> Goods => goods;
			public int RandomPriceFactorSeed => 7102;
			public string TraderName => "Zombieland contamination bridge trader";
			public bool CanTradeNow => true;
			public float TradePriceImprovementOffsetForPlayer => 0f;
			public Faction Faction => Faction.OfMechanoids ?? Faction.OfAncients;
			public TradeCurrency TradeCurrency => TradeCurrency.Silver;

			public IEnumerable<Thing> ColonyThingsWillingToBuy(Pawn playerNegotiator)
				=> Enumerable.Empty<Thing>();

			public void GiveSoldThingToTrader(Thing toGive, int countToGive, Pawn playerNegotiator)
			{
			}

			public void GiveSoldThingToPlayer(Thing toGive, int countToGive, Pawn playerNegotiator)
			{
			}
		}

	}
}
