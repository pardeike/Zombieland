using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace ZombieLand
{
	public enum PatchGroupState
	{
		Skipped,
		Succeeded,
		Failed
	}

	public sealed class PatchGroupResult
	{
		public string Id { get; internal set; }
		public string Label { get; internal set; }
		public int Order { get; internal set; }
		public PatchGroupState State { get; internal set; }
		public IReadOnlyList<string> PatchTypes { get; internal set; } = Array.Empty<string>();
		public IReadOnlyList<string> FailedPatchTypes { get; internal set; } = Array.Empty<string>();
		public string Summary { get; internal set; }
		public string Detail { get; internal set; }
		public DateTime Timestamp { get; internal set; }

		public bool IsFailure => State == PatchGroupState.Failed;
	}

	internal sealed class PatchGroupDefinition
	{
		public readonly string Id;
		public readonly string Label;
		public readonly int Order;

		public PatchGroupDefinition(string id, string label, int order)
		{
			Id = id;
			Label = label;
			Order = order;
		}
	}

	internal sealed class PatchGroupFailureException : Exception
	{
		public readonly string GroupId;
		public readonly Type PatchType;

		public PatchGroupFailureException(string groupId, Type patchType, string message) : base(message)
		{
			GroupId = groupId;
			PatchType = patchType;
		}
	}

	public static class PatchGroups
	{
		public const string Startup = "startup";
		public const string Core = "core";
		public const string Zombies = "zombies";
		public const string Hostility = "hostility";
		public const string Combat = "combat";
		public const string Pathing = "pathing";
		public const string Infection = "infection";
		public const string UI = "ui";
		public const string Settings = "settings";
		public const string Contamination = "contamination";
		public const string Social = "social";
		public const string Items = "items";
		public const string Rendering = "rendering";
		public const string Optional = "optional";
		public const string Misc = "misc";

		static readonly PatchGroupDefinition[] definitions =
		{
			new(Startup, "Startup", 0),
			new(Core, "Core", 10),
			new(Zombies, "Zombies", 20),
			new(Hostility, "Hostility", 30),
			new(Combat, "Combat", 40),
			new(Pathing, "Pathing", 50),
			new(Infection, "Infection", 60),
			new(UI, "User Interface", 70),
			new(Settings, "Settings", 80),
			new(Contamination, "Contamination", 90),
			new(Social, "Social", 100),
			new(Items, "Items", 110),
			new(Rendering, "Rendering", 120),
			new(Optional, "Optional Mods", 130),
			new(Misc, "Misc", 140)
		};

		static readonly Dictionary<string, PatchGroupDefinition> definitionsById = definitions.ToDictionary(definition => definition.Id);
		static readonly List<PatchGroupResult> results = new();

		[ThreadStatic] static PatchGroupDefinition activeGroup;
		[ThreadStatic] static Type activePatchType;
		static bool failureDialogShown;

		public static IReadOnlyList<PatchGroupResult> Current => results.AsReadOnly();
		public static IReadOnlyList<PatchGroupResult> Failures => results.Where(result => result.IsFailure).OrderBy(result => result.Order).ToArray();
		public static bool HasFailures => results.Any(result => result.IsFailure);

		internal static bool IsApplyingPatchGroup => activeGroup != null;

		internal static void ThrowActiveFailure(string error)
		{
			if (activeGroup != null)
				throw new PatchGroupFailureException(activeGroup.Id, activePatchType, error);
		}

		internal static void ApplyAll(Harmony harmony, Assembly assembly)
		{
			results.Clear();
			failureDialogShown = false;

			var groupedTypes = DiscoverPatchTypes(assembly)
				.Where(type => IsLatePatchType(type) == false)
				.GroupBy(DefinitionFor)
				.ToDictionary(group => group.Key.Id, group => group.ToArray());

			foreach (var definition in definitions)
			{
				groupedTypes.TryGetValue(definition.Id, out var patchTypes);
				ApplyGroup(harmony, definition, patchTypes ?? Array.Empty<Type>());
			}
		}

		internal static void ApplyLateGroup(Harmony harmony, string groupId, IEnumerable<Type> patchTypes)
		{
			var definition = DefinitionById(groupId);
			_ = ApplyGroup(harmony, definition, patchTypes.Where(IsHarmonyPatchClass).ToArray());
		}

		internal static void RunLateAction(string groupId, string name, Action action)
		{
			var definition = DefinitionById(groupId);
			try
			{
				action();
			}
			catch (Exception ex)
			{
				var result = FailureResult(definition, new[] { name }, new[] { name }, ex);
				StoreResult(result);
				Log.Error($"Zombieland patch group '{definition.Label}' failed while applying {name} and was disabled:\n{ex}");
			}
		}

		internal static void RecordExternalFailure(string groupId, string summary, string detail = null)
		{
			var definition = DefinitionById(groupId);
			var result = new PatchGroupResult
			{
				Id = definition.Id,
				Label = definition.Label,
				Order = definition.Order,
				State = PatchGroupState.Failed,
				PatchTypes = Array.Empty<string>(),
				FailedPatchTypes = Array.Empty<string>(),
				Summary = summary,
				Detail = detail ?? summary,
				Timestamp = DateTime.UtcNow
			};
			StoreResult(result);
		}

		public static bool TryShowFailureDialogAtStartScreen()
		{
			if (failureDialogShown || HasFailures == false)
				return false;
			if (Verse.Current.ProgramState != ProgramState.Entry)
				return false;
			if (LongEventHandler.AnyEventNowOrWaiting || LongEventHandler.ShouldWaitForEvent)
				return false;
			if (Find.WindowStack == null)
				return false;
			if (Find.WindowStack.IsOpen(typeof(Dialog_PatchGroupFailures)))
				return false;

			failureDialogShown = true;
			Find.WindowStack.Add(new Dialog_PatchGroupFailures(Current));
			return true;
		}

		static PatchGroupResult ApplyGroup(Harmony harmony, PatchGroupDefinition definition, IReadOnlyList<Type> patchTypes)
		{
			if (patchTypes.Count == 0)
			{
				var skipped = new PatchGroupResult
				{
					Id = definition.Id,
					Label = definition.Label,
					Order = definition.Order,
					State = PatchGroupState.Skipped,
					PatchTypes = Array.Empty<string>(),
					FailedPatchTypes = Array.Empty<string>(),
					Timestamp = DateTime.UtcNow
				};
				StoreResult(skipped);
				return skipped;
			}

			var processors = new List<PatchClassProcessor>();
			var patchTypeNames = patchTypes.Select(TypeName).ToArray();
			try
			{
				activeGroup = definition;
				foreach (var patchType in patchTypes)
				{
					activePatchType = patchType;
					var processor = new PatchClassProcessor(harmony, patchType);
					processors.Add(processor);
					_ = processor.Patch();
				}

				var result = new PatchGroupResult
				{
					Id = definition.Id,
					Label = definition.Label,
					Order = definition.Order,
					State = PatchGroupState.Succeeded,
					PatchTypes = patchTypeNames,
					FailedPatchTypes = Array.Empty<string>(),
					Timestamp = DateTime.UtcNow
				};
				StoreResult(result);
				return result;
			}
			catch (Exception ex)
			{
				var failureSummary = SummaryFor(ex);
				for (var i = processors.Count - 1; i >= 0; i--)
				{
					try
					{
						processors[i].Unpatch();
					}
					catch (Exception rollbackEx)
					{
						if (SummaryFor(rollbackEx) == failureSummary)
							continue;
						Log.Warning($"Zombieland could not roll back a patch in group '{definition.Label}': {rollbackEx}");
					}
				}

				var failedPatchTypes = activePatchType == null ? Array.Empty<string>() : new[] { TypeName(activePatchType) };
				var result = FailureResult(definition, patchTypeNames, failedPatchTypes, ex);
				StoreResult(result);
				Log.Error($"Zombieland patch group '{definition.Label}' failed and was disabled:\n{ex}");
				return result;
			}
			finally
			{
				activeGroup = null;
				activePatchType = null;
			}
		}

		static PatchGroupResult FailureResult(PatchGroupDefinition definition, IReadOnlyList<string> patchTypeNames, IReadOnlyList<string> failedPatchTypes, Exception ex)
		{
			return new PatchGroupResult
			{
				Id = definition.Id,
				Label = definition.Label,
				Order = definition.Order,
				State = PatchGroupState.Failed,
				PatchTypes = patchTypeNames.ToArray(),
				FailedPatchTypes = failedPatchTypes.ToArray(),
				Summary = SummaryFor(ex),
				Detail = ex.ToString(),
				Timestamp = DateTime.UtcNow
			};
		}

		static void StoreResult(PatchGroupResult result)
		{
			var index = results.FindIndex(existing => existing.Id == result.Id);
			if (index >= 0)
				results[index] = MergeResults(results[index], result);
			else
				results.Add(result);
			results.Sort((a, b) => a.Order.CompareTo(b.Order));
		}

		static PatchGroupResult MergeResults(PatchGroupResult existing, PatchGroupResult incoming)
		{
			var patchTypes = existing.PatchTypes
				.Concat(incoming.PatchTypes)
				.Distinct()
				.ToArray();
			var failedPatchTypes = existing.FailedPatchTypes
				.Concat(incoming.FailedPatchTypes)
				.Distinct()
				.ToArray();

			if (existing.IsFailure || incoming.IsFailure)
			{
				var preferredFailure = incoming.IsFailure ? incoming : existing;
				return new PatchGroupResult
				{
					Id = existing.Id,
					Label = existing.Label,
					Order = existing.Order,
					State = PatchGroupState.Failed,
					PatchTypes = patchTypes,
					FailedPatchTypes = failedPatchTypes,
					Summary = preferredFailure.Summary,
					Detail = JoinDetails(existing.Detail, incoming.Detail),
					Timestamp = incoming.Timestamp > existing.Timestamp ? incoming.Timestamp : existing.Timestamp
				};
			}

			return new PatchGroupResult
			{
				Id = existing.Id,
				Label = existing.Label,
				Order = existing.Order,
				State = existing.State == PatchGroupState.Succeeded || incoming.State == PatchGroupState.Succeeded ? PatchGroupState.Succeeded : PatchGroupState.Skipped,
				PatchTypes = patchTypes,
				FailedPatchTypes = failedPatchTypes,
				Summary = incoming.Summary ?? existing.Summary,
				Detail = JoinDetails(existing.Detail, incoming.Detail),
				Timestamp = incoming.Timestamp > existing.Timestamp ? incoming.Timestamp : existing.Timestamp
			};
		}

		static string JoinDetails(params string[] details)
		{
			var parts = details
				.Where(detail => string.IsNullOrEmpty(detail) == false)
				.Distinct()
				.ToArray();
			return parts.Length == 0 ? null : string.Join("\n\n", parts);
		}

		static IEnumerable<Type> DiscoverPatchTypes(Assembly assembly)
		{
			return assembly.GetTypes()
				.Where(IsHarmonyPatchClass)
				.OrderBy(type => type.MetadataToken);
		}

		static bool IsHarmonyPatchClass(Type type)
		{
			return type.GetCustomAttributes(typeof(HarmonyPatch), false).Any();
		}

		static bool IsLatePatchType(Type type)
		{
			return type.FullName?.StartsWith("ZombieLand.CETools_Patch", StringComparison.Ordinal) == true;
		}

		static PatchGroupDefinition DefinitionFor(Type type)
		{
			var fullName = type.FullName ?? type.Name;
			var name = type.Name;
			var exactDefinition = ExactDefinitionFor(fullName);
			if (exactDefinition != null)
				return exactDefinition;

			if (StartupPatchNames.Contains(name))
				return DefinitionById(Startup);
			if (ContainsAny(name, "Settings", "AreaManager", "ManageAreas", "SelectScenario", "StitchedPages", "MainTabWindow_Menu", "MainMenuDrawer_DoMainMenuControls"))
				return DefinitionById(Settings);
			if (ContainsAny(name, "Renderer", "RenderNode", "Graphic_Multi", "DrawTracker", "Wiggler", "Effecter", "DamageFlasher"))
				return DefinitionById(Rendering);
			if (ContainsAny(name, "GlobalControls", "SelectionDrawer", "MapInterface", "Gizmo", "GenMapUI", "Inspect", "Mouseover", "Message", "Alert", "Selector", "Selectable", "HealthCard", "Listing"))
				return DefinitionById(UI);
			if (ContainsAny(name, "PathFinder", "PathRequest", "PathFollower", "PawnCollision", "RegionAndRoom", "Door", "Flee", "Danger"))
				return DefinitionById(Pathing);
			if (ContainsAny(name, "Verb", "Projectile", "DamageWorker", "Armor", "Melee", "AttackStatic", "Explosion", "Fire", "ShotReport", "StunHandler"))
				return DefinitionById(Combat);
			if (ContainsAny(name, "Infection", "Hediff", "Immunity", "HealthTracker", "CanHeal", "RemoveBodyPart", "ShouldRemove", "Capacities", "Bite", "Scratch", "Pain"))
				return DefinitionById(Infection);
			if (ContainsAny(name, "Thought", "Relations", "Interactions", "Tale", "Opinion", "Memory", "Ideo"))
				return DefinitionById(Social);
			if (ContainsAny(name, "ThingFilter", "ThingMaker", "Refuelable", "Fuel", "Apparel", "Attach", "AmbientTemperature", "Thing_", "Comp"))
				return DefinitionById(Items);
			if (ContainsAny(name, "Zombie", "TickManager", "WorldPawns", "Infestation", "Mothball", "JobTracker", "WorkGiver", "Corpse", "Pawn_", "Need_", "MentalState", "FoodUtility", "Reservation", "Haul", "MutantUtility", "RecordsTracker", "Clamor"))
				return DefinitionById(Zombies);
			if (ContainsAny(name, "Game_", "Map_", "Root_", "Faction", "Incident", "PawnGenerator", "PawnGroup", "DataSave"))
				return DefinitionById(Core);

			return DefinitionById(Misc);
		}

		static PatchGroupDefinition ExactDefinitionFor(string fullName)
		{
			if (StartupPatchTypes.Contains(fullName))
				return DefinitionById(Startup);
			if (CorePatchTypes.Contains(fullName))
				return DefinitionById(Core);
			if (ZombiePatchTypes.Contains(fullName))
				return DefinitionById(Zombies);
			if (HostilityPatchTypes.Contains(fullName))
				return DefinitionById(Hostility);
			if (CombatPatchTypes.Contains(fullName))
				return DefinitionById(Combat);
			if (PathingPatchTypes.Contains(fullName))
				return DefinitionById(Pathing);
			if (InfectionPatchTypes.Contains(fullName))
				return DefinitionById(Infection);
			if (UIPatchTypes.Contains(fullName))
				return DefinitionById(UI);
			if (SettingsPatchTypes.Contains(fullName))
				return DefinitionById(Settings);
			if (ContaminationPatchTypes.Contains(fullName))
				return DefinitionById(Contamination);
			if (SocialPatchTypes.Contains(fullName))
				return DefinitionById(Social);
			if (ItemsPatchTypes.Contains(fullName))
				return DefinitionById(Items);
			if (RenderingPatchTypes.Contains(fullName))
				return DefinitionById(Rendering);
			if (OptionalPatchTypes.Contains(fullName))
				return DefinitionById(Optional);
			return null;
		}

		static PatchGroupDefinition DefinitionById(string id)
		{
			return definitionsById.TryGetValue(id, out var definition) ? definition : definitionsById[Misc];
		}

		static bool ContainsAny(string text, params string[] needles)
		{
			return needles.Any(needle => text.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0);
		}

		static string TypeName(Type type)
		{
			return type.FullName?.Replace("+", ".") ?? type.Name;
		}

		static string SummaryFor(Exception ex)
		{
			var current = ex;
			while (current.InnerException != null)
				current = current.InnerException;
			return string.IsNullOrEmpty(current.Message) ? current.GetType().Name : current.Message;
		}

		static readonly HashSet<string> StartupPatchTypes = new()
		{
			"ZombieLand.Assets",
			"ZombieLand.Patches+MainMenuDrawer_Init_Patch",
			"ZombieLand.Patches+ParseHelper_FromString_Patch",
			"ZombieLand.TimeControlService",
			"ZombieLand.ClearMapsService"
		};

		static readonly HashSet<string> StartupPatchNames = new()
		{
			"Root_Update_Patch",
			"Root_Shutdown_Patch",
			"Root_Play_SetupForQuickTestPlay_Patch",
			"GameDataSaveLoader_LoadGame_Patch"
		};

		static readonly HashSet<string> CorePatchTypes = new()
		{
			"ZombieLand.Patches+Game_FinalizeInit_Patch",
			"ZombieLand.Patches+Map_FinalizeInit_Patch",
			"ZombieLand.Patches+Map_FinalizeLoading_Patch",
			"ZombieLand.Patches+MapComponentUtility_FinalizeInit_Patch",
			"ZombieLand.Patches+WorldGenStep_Factions_GenerateFresh_Patch",
			"ZombieLand.Patches+FactionGenerator_GenerateFactionsIntoWorldLayer_Patch",
			"ZombieLand.Patches+FactionManager_ExposeData_Patch",
			"ZombieLand.Patches+IncidentWorker_TryExecute_Patch",
			"ZombieLand.Patches+IncidentWorker_Raid_TryExecuteWorker_Patch",
			"ZombieLand.Patches+IncidentWorker_Patches",
			"ZombieLand.Patches+PawnGenerator_GenerateNewPawnInternal_Patch",
			"ZombieLand.Patches+DropCellFinder_IsSafeDropSpot_Patch",
			"ZombieLand.Patches+WealthWatcher_CalculateWealthItems_Patch",
			"ZombieLand.Patches+WealthWatcher_WealthItemsFilter_Patch",
			"ZombieLand.Patches+DangerWatcher_CalculateDangerRating_Patch"
		};

		static readonly HashSet<string> ZombiePatchTypes = new()
		{
			"ZombieLand.Patches+Verse_TickManager_TickManagerUpdate_Patch",
			"ZombieLand.Patches+TickManager_DoSingleTick_Patch",
			"ZombieLand.Patches+Verse_TickManager_NothingHappeningInGame_Patch",
			"ZombieLand.Patches+WorldPawns_ShouldMothball_Patch",
			"ZombieLand.Patches+InfestationCellFinder_CalculateLocationCandidates_Patch",
			"ZombieLand.Patches+Pawn_Tick_Patch",
			"ZombieLand.Patches+Pawn_EquipmentTracker_EquipmentTrackerTick_Patch",
			"ZombieLand.Patches+Pawn_DraftController_setDrafted_Patch",
			"ZombieLand.Patches+Pawn_JobTracker_StartJob_Patch",
			"ZombieLand.Patches+Pawn_JobTracker_ShouldStartJobFromThinkTree_Patch",
			"ZombieLand.Patches+MentalStateHandler_TryStartMentalState_Patch",
			"ZombieLand.Patches+WorkGiver_Scanner_HasJobOnCell_Patches",
			"ZombieLand.Patches+WorkGiver_Scanner_JobOnCell_Patches",
			"ZombieLand.Patches+WorkGiver_Scanner_HasJobOnThing_Patches",
			"ZombieLand.Patches+WorkGiver_Scanner_JobOnThing_Patches",
			"ZombieLand.Patches+WorkGiver_Haul_JobOnThing_Patch",
			"ZombieLand.Patches+Pawn_Downed_Patch",
			"ZombieLand.Patches+Pawn_Kill_Patch",
			"ZombieLand.Patches+Pawn_Destroy_Patch",
			"ZombieLand.Patches+Corpse_RotStageChanged_Patch",
			"ZombieLand.Patches+Corpse_TickRare_Patch",
			"ZombieLand.Patches+PawnComponentsUtility_RemoveComponentsOnKilled_Patch",
			"ZombieLand.Patches+MutantUtility_CanResurrectAsShambler_Patch",
			"ZombieLand.Patches+MutantUtility_ResurrectAsShambler_Patch",
			"ZombieLand.Patches+GenClamor_DoClamor_Patch",
			"ZombieLand.Patches+Pawn_HearClamor_Patch",
			"ZombieLand.Patches+Pawn_RecordsTracker_Increment_Patch",
			"ZombieLand.Patches+Pawn_RecordsTracker_GetValue_Patch",
			"ZombieLand.Patches+Pawn_RecordsTracker_GetAsInt_Patch",
			"ZombieLand.Patches+Pawn_AnythingToStrip_Patch",
			"ZombieLand.Patches+PawnCapacitiesHandler_CanBeAwake_Patch",
			"ZombieLand.Patches+Pawn_HealthTracker_MakeDowned_Patch",
			"ZombieLand.Patches+Pawn_HealthTracker_MakeUndowned_Patch",
			"ZombieLand.PsychicRitualToil_SkipAbductionPlayer_ApplyOutcome_Patch"
		};

		static readonly HashSet<string> HostilityPatchTypes = new()
		{
			"ZombieLand.AttackTargetFinder_BestAttackTarget_Patch",
			"ZombieLand.AttackTargetFinder_FriendlyFire_Patch",
			"ZombieLand.AttackTargetFinder_GetAvailableShootingTargetsByScore_Patch",
			"ZombieLand.AttackTargetFinder_GetShootingTargetScore_Patch",
			"ZombieLand.GenHostility_HostileTo_Thing_Faction_Patch",
			"ZombieLand.GenHostility_HostileTo_Thing_Thing_Patch",
			"ZombieLand.GenHostility_IsActiveThreat_Patch",
			"ZombieLand.JobDriver_Wait_CheckForAutoAttack_Patch",
			"ZombieLand.TargetCachePatches+AttackTargetsCache_DeregisterTarget_Patch",
			"ZombieLand.TargetCachePatches+AttackTargetsCache_RegisterTarget_Patch",
			"ZombieLand.TargetCachePatches+AttackTargetsCache_TargetsHostileToColony_Patch"
		};

		static readonly HashSet<string> CombatPatchTypes = new()
		{
			"ZombieLand.Patches+Pawn_MeleeVerbs_TryMeleeAttack_Patch",
			"ZombieLand.Patches+FloatMenuUtility_GetMeleeAttackAction_Patch",
			"ZombieLand.Patches+Projectile_ImpactSomething_Patch",
			"ZombieLand.Patches+Verb_MeleeAttack_TryCastShot_Patch",
			"ZombieLand.Patches+Verb_LaunchProjectile_TryCastShot_Patch",
			"ZombieLand.Patches+JobDriver_AttackStatic_MakeNewToils_b__1_Patch",
			"ZombieLand.Patches+Pawn_MeleeVerbs_GetUpdatedAvailableVerbsList_Patch",
			"ZombieLand.Patches+Pawn_MeleeVerbs_ChooseMeleeVerb_Patch",
			"ZombieLand.Patches+Toils_Combat_FollowAndMeleeAttack_KillIncappedTarget_Patch",
			"ZombieLand.Patches+JobDriver_AttackStatic_TickAction_Patch",
			"ZombieLand.Patches+Verb_TryStartCastOn_Patch",
			"ZombieLand.Patches+Verb_GetDamageFactorFor_Patch",
			"ZombieLand.Patches+Fire_VulnerableToRain_Patch",
			"ZombieLand.Patches+Explosion_AffectCell_Patch",
			"ZombieLand.Patches+Fire_DoFireDamage_Patch",
			"ZombieLand.Patches+Fire_SpawnSmokeParticles_Patch",
			"ZombieLand.Patches+SustainerAggregatorUtility_AggregateOrSpawnSustainerFor_Patch",
			"ZombieLand.Patches+FireWatcher_UpdateObservations_Patch",
			"ZombieLand.Patches+DamageWorker_DamageResult_AssociateWithLog_Patch",
			"ZombieLand.Patches+ShotReport_HitReportFor_Patch",
			"ZombieLand.Patches+ShotReport_AimOnTargetChance_StandardTarget_Patch",
			"ZombieLand.Patches+DamageWorker_AddInjury_ApplyToPawn_Patch",
			"ZombieLand.Patches+Pawn_DrawTracker_Notify_DamageApplied_Patch",
			"ZombieLand.Patches+ImpactSoundUtility_PlayImpactSound_Patch",
			"ZombieLand.Patches+LifeStageUtility_PlayNearestLifestageSound_Patch",
			"ZombieLand.Patches+DamageWorker_AddInjury_ApplyDamageToPart_Patch",
			"ZombieLand.Patches+ArmorUtility_GetPostArmorDamage_Patch",
			"ZombieLand.Patches+Verb_CausesTimeSlowdown_Patch",
			"ZombieLand.Patches+Pawn_TryGetAttackVerb_Patch",
			"ZombieLand.Patches+Pawn_TryStartAttack_Patch",
			"ZombieLand.Patches+DamageWorker_AddInjury_IsHeadshot_Patch",
			"ZombieLand.Patches+Projectile_Launch_Patch"
		};

		static readonly HashSet<string> PathingPatchTypes = new()
		{
			"ZombieLand.Patches+Pawn_PathFollower_SetupMoveIntoNextCell_Patch",
			"ZombieLand.Patches+FleeUtility_ShouldFleeFrom_Patch",
			"ZombieLand.Patches+PathFinder_CreateRequest_Patch",
			"ZombieLand.Patches+PathFinder_FindPathNow_Patch",
			"ZombieLand.Patches+PathRequest_Resolve_Patch",
			"ZombieLand.Patches+PathRequest_Dispose_Patch",
			"ZombieLand.Patches+Pawn_PathFollower_NeedNewPath_Patch",
			"ZombieLand.Patches+Building_Door_PawnCanOpen_Patch",
			"ZombieLand.Patches+Building_Door_Tick_Patch",
			"ZombieLand.Patches+Building_Door_StartManualCloseBy_Patch",
			"ZombieLand.Patches+FleeUtility_FleeJob_Patch",
			"ZombieLand.Patches+JobGiver_ConfigurableHostilityResponse_TryGetFleeJob_Patch",
			"ZombieLand.Patches+JobGiver_ConfigurableHostilityResponse_TryGetAttackNearbyEnemyJob_Patch",
			"ZombieLand.Patches+DangerUtility_GetDangerFor_Patch",
			"ZombieLand.Patches+Pawn_PathFollower_StartPath_Patch",
			"ZombieLand.Patches+Pawn_PathFollower_WillCollideWithPawnAt_Patch",
			"ZombieLand.Patches+PawnCollisionTweenerUtility_PawnCollisionPosOffsetFor_Patch",
			"ZombieLand.Patches+Pawn_PathFollower_CostToMoveIntoCell_Patch",
			"ZombieLand.Patches+RegionAndRoomUpdater_CreateOrUpdateRooms_Patch",
			"ZombieLand.Patches+FogGrid_Notify_PawnEnteringDoor_Patch"
		};

		static readonly HashSet<string> InfectionPatchTypes = new()
		{
			"ZombieLand.Patches+DamageWorker_Scratch_ChooseHitPart_Patch",
			"ZombieLand.Patches+DamageWorker_Bite_ChooseHitPart_Patch",
			"ZombieLand.Patches+HediffSet_CalculatePain_Patch",
			"ZombieLand.Patches+PawnCapacitiesHandler_GetLevel_Patch",
			"ZombieLand.Patches+SkillRecord_GetLevel_Patch",
			"ZombieLand.Patches+SkillRecord_GetLevelForUI_Patch",
			"ZombieLand.Patches+HealthUtility_DamageUntilDowned_Patch",
			"ZombieLand.Patches+Pawn_GeneTracker_AddGene_Gene_Patch",
			"ZombieLand.Patches+Pawn_GeneTracker_AddGene_GeneDef_Patch",
			"ZombieLand.Patches+Pawn_StoryTracker_SkinColorBase_Patch",
			"ZombieLand.Patches+HediffUtility_CanHealNaturally_Patch",
			"ZombieLand.Patches+Recipe_RemoveBodyPart_GetPartsToApplyOn_Patch",
			"ZombieLand.Patches+Hediff_ShouldRemove_Patch",
			"ZombieLand.Patches+HediffSet_AddDirect_Patch",
			"ZombieLand.Patches+ImmunityHandler_ImmunityHandlerTickInterval_Patch",
			"ZombieLand.Patches+Pawn_PreApplyDamage_Patch",
			"ZombieLand.Patches+Pawn_HealthTracker_PreApplyDamage_Patch",
			"ZombieLand.Pawn_DrawTracker_Constructor_Patch"
			};

		static readonly HashSet<string> UIPatchTypes = new()
		{
			"ZombieLand.Patches+GenUI_ThingsUnderMouse_Patch",
			"ZombieLand.Patches+SelectionDrawer_DrawSelectionOverlays_Patch",
			"ZombieLand.Patches+MapInterface_MapInterfaceUpdate_Patch",
			"ZombieLand.Patches+MapInterface_MapInterfaceOnGUI_AfterMainTabs_Patch",
			"ZombieLand.Patches+GlobalControlsUtility_DoDate_Patch",
			"ZombieLand.Patches+Pawn_EquipmentTracker_YieldGizmos_Patch",
			"ZombieLand.Patches+CompProperties_Refuelable_SpecialDisplayStats_Patch",
			"ZombieLand.Patches+Gizmo_RefuelableFuelStatus_GizmoOnGUI_Patch",
			"ZombieLand.Patches+EditWindow_DebugInspector_CurrentDebugString_Patch",
			"ZombieLand.Patches+GenMapUI_DrawPawnLabel_Patch",
			"ZombieLand.Patches+MainTabWindow_Inspect_CurTabs_Patch",
			"ZombieLand.Patches+RecipeDef_PotentiallyMissingIngredients_Patch",
			"ZombieLand.Patches+HealthCardUtility_DrawOverviewTab_Patch",
			"ZombieLand.Patches+Selector_SelectInternal_Patch",
			"ZombieLand.Patches+ThingSelectionUtility_SelectableByMapClick_Patch",
			"ZombieLand.Patches+Messages_MessagesDoGUI_Patch",
			"ZombieLand.Patches+Message_Draw_Patch",
			"ZombieLand.Patches+Alert_ColonistLeftUnburied_IsCorpseOfColonist_Patch"
		};

		static readonly HashSet<string> SettingsPatchTypes = new()
		{
			"ZombieLand.AreaManager_Patches",
			"ZombieLand.Dialog_ManageAreas_Patches",
			"ZombieLand.Patches+Page_SelectScenario_BeginScenarioConfiguration_Patch",
			"ZombieLand.Patches+PageUtility_StitchedPages_Patch",
			"ZombieLand.Patches+Game_InitNewGame_Patch",
			"ZombieLand.Patches+MainTabWindow_Menu_RequestedTabSize_Path",
			"ZombieLand.Patches+MainTabWindow_Menu_DoWindowContents_Path",
			"ZombieLand.Patches+MainMenuDrawer_DoMainMenuControls_Path",
			"ZombieLand.Patches+PriorityWork_GetGizmos_Patch"
		};

		static readonly HashSet<string> SocialPatchTypes = new()
		{
			"ZombieLand.Patches+Faction_TryAffectGoodwillWith_Patch",
			"ZombieLand.Patches+TaleRecorder_RecordTale_Patch",
			"ZombieLand.Patches+Thought_Memory_Save_Patch",
			"ZombieLand.Patches+RelationsUtility_HasAnySocialMemoryWith_Patch",
			"ZombieLand.Patches+Pawn_RelationsTracker_OpinionOf_Patch",
			"ZombieLand.Patches+RelationsUtility_PawnsKnowEachOther_Patch",
			"ZombieLand.Patches+ThoughtHandler_GetSocialThoughts_Patch",
			"ZombieLand.Patches+SituationalThoughtHandler_AppendSocialThoughts_Patch",
			"ZombieLand.Patches+Corpse_GiveObservedThought_Patch",
			"ZombieLand.Patches+Corpse_GiveObservedHistoryEvent_Patch",
			"ZombieLand.Patches+ThoughtUtility_CanGetThought_Patch",
			"ZombieLand.Patches+ForbidUtility_SetForbiddenIfOutsideHomeArea_Patch",
			"ZombieLand.Patches+Pawn_InteractionsTracker_TryInteractWith_Patch",
			"ZombieLand.Patches+Pawn_InteractionsTracker_InteractionsTrackerTickInterval_Patch",
			"ZombieLand.Patches+PawnNameColorUtility_PawnNameColorOf_Patch",
			"ZombieLand.Patches+PawnDiedOrDownedThoughtsUtility_TryGiveThoughts_Patch",
			"ZombieLand.Patches+IndividualThoughtToAdd_Constructor_Patch",
			"ZombieLand.Patches+Thought_Tale_OpinionOffset_Patch",
			"ZombieLand.Patches+Pawn_IdeoTracker_ExposeData_Patch"
		};

		static readonly HashSet<string> ItemsPatchTypes = new()
		{
			"ZombieLand.Patches+GenSpawn_SpawningWipes_Patch",
			"ZombieLand.Patches+Thing_Position_Patch",
			"ZombieLand.Patches+CompRefuelable_ConsumeFuel_Patch",
			"ZombieLand.Patches+Thing_AmbientTemperature_Patch",
			"ZombieLand.Patches+CompAttachBase_AddAttachment_Patch",
			"ZombieLand.Patches+CompAttachBase_RemoveAttachment_Patch",
			"ZombieLand.Patches+ThingFilter_SetAllow_Patch",
			"ZombieLand.Patches+Listing_TreeThingFilter_Visible_Patch",
			"ZombieLand.Patches+ThingMaker_MakeThing_Patch",
			"ZombieLand.Patches+Building_DeSpawn_Patch"
		};

		static readonly HashSet<string> RenderingPatchTypes = new()
		{
			"ZombieLand.Patches+PawnRenderer_DrawEquipment_Patch",
			"ZombieLand.Patches+Pawn_RotationTracker_UpdateRotation_Patch",
			"ZombieLand.Patches+PawnDownedWiggler_WigglerTick_Patch",
			"ZombieLand.Patches+PawnRenderer_BodyAngle_Patch",
			"ZombieLand.Patches+Root_Play_Update_Patch",
			"ZombieLand.Patches+PawnRenderer_RenderPawnAt_Patch",
			"ZombieLand.Patches+PawnRenderNode_Body_GraphicFor_Patch",
			"ZombieLand.Patches+PawnRenderNode_Head_GraphicFor_Patch",
			"ZombieLand.Patches+Effecter_Trigger_Patch",
			"ZombieLand.Patches+Map_MapUpdate_Patch",
			"ZombieLand.Patches+Graphic_Multi_Init_Patch",
			"ZombieLand.PawnRenderer_Constructor_With_Pawn_Patch",
			"ZombieLand.DamageFlasher_Notify_DamageApplied_Patch",
			"ZombieLand.DamageFlasher_GetDamagedMat_Patch",
			"ZombieLand.Pawn_DrawTracker_DrawPos_Patch",
			"ZombieLand.Pawn_DrawTrackerDrawTrackerTick_Patch"
		};

		static readonly HashSet<string> OptionalPatchTypes = new()
		{
			"ZombieLand.RimConnection_Settings_CommandOptionSettings_Patch",
			"ZombieLand.RimWorld_ShipCombatManager_GenerateShip_Patch",
			"ZombieLand.SaveOurShip2_GenerateSpaceSubMesh_GenerateMesh_Patch"
		};

		static readonly HashSet<string> ContaminationPatchTypes = new()
		{
			"ZombieLand.BeautyDrawer_DrawBeautyAroundMouse_Patch",
			"ZombieLand.Blueprint_TryReplaceWithSolidThing_Patch",
			"ZombieLand.Building_GeneExtractor_Finish_Patch",
			"ZombieLand.Building_NutrientPasteDispenser_TryDispenseFood_Patch",
			"ZombieLand.Building_SubcoreScanner_Tick_Patch",
			"ZombieLand.CompDeepDrill_TryProducePortion_Patch",
			"ZombieLand.CompLifespan_Expire_Patch",
			"ZombieLand.CompSpawnerFilth_TrySpawnFilth_Patch",
			"ZombieLand.Corpse_ButcherProducts_Patch",
			"ZombieLand.Corpse_InnerPawn_Setter_Patch",
			"ZombieLand.DamageWorker_Flame_Apply_Patch",
			"ZombieLand.ExecutionUtility_ExecutionInt_Patch",
			"ZombieLand.Filth_MakeThing_Patch",
			"ZombieLand.Fire_DoComplexCalcs_Patch",
			"ZombieLand.Frame_CompleteConstruction_Patch",
			"ZombieLand.Game_FinalizeInit_Patch",
			"ZombieLand.GenConstruct_PlaceBlueprintForInstall_Patch",
			"ZombieLand.GenConstruct_PlaceBlueprintForReinstall_Patch",
			"ZombieLand.GenLeaving_DoLeavingsFor_Patch",
			"ZombieLand.GenReciepe_MakeRecipeProducts_Patch",
			"ZombieLand.GenSpawn_Spawn_Replacement_Patch",
			"ZombieLand.GridUtility_Unpollute_Patch",
			"ZombieLand.HediffComp_DissolveGearOnDeath_Notify_PawnKilled_Patch",
			"ZombieLand.IncidentWorker_AmbrosiaSprout_TryExecuteWorker_Patch",
			"ZombieLand.InspectPaneFiller_DoPaneContentsFor_Patch",
			"ZombieLand.InspectPaneUtility_PaneWidthFor_Patch",
			"ZombieLand.JobDriver_AffectFloor_MakeNewToils_Patch",
			"ZombieLand.JobDriver_CleanFilth_MakeNewToils_Patch",
			"ZombieLand.JobDriver_ClearPollution_ClearPollutionAt_Patch",
			"ZombieLand.JobDriver_ClearSnow_MakeNewToils_Patch",
			"ZombieLand.JobDriver_DisassembleMech_MakeNewToils_Patch",
			"ZombieLand.JobDriver_Lovin_MakeNewToils_Patch",
			"ZombieLand.JobDriver_PlantSow_MakeNewToils_Patch",
			"ZombieLand.JobDriver_PlantWork_MakeNewToils_Patch",
			"ZombieLand.JobDriver_RepairMech_MakeNewToils_Patch",
			"ZombieLand.JobDriver_Repair_MakeNewToils_Patch",
			"ZombieLand.JobDriver_Vomit_MakeNewToils_Patch",
			"ZombieLand.Jobdriver_ClearPollution_Spawn_Patch",
			"ZombieLand.MainTabWindow_Quests_DoRow_Patch",
			"ZombieLand.MapGenerator_GenerateContentsIntoMap_Patch",
			"ZombieLand.MechClusterUtility_SpawnCluster_Patch",
			"ZombieLand.MedicalRecipesUtility_GenSpawn_Spawn_Patches",
			"ZombieLand.Mineable_DestroyMined_Patch",
			"ZombieLand.Mineable_Destroy_Patch",
			"ZombieLand.Mineable_TrySpawnYield_Patch",
			"ZombieLand.MinifiedThing_SplitOff_Patch",
			"ZombieLand.MinifyUtility_MakeMinified_Patch",
			"ZombieLand.Misc_Building_Patch",
			"ZombieLand.MouseoverReadout_MouseoverReadoutOnGUI_Patch",
			"ZombieLand.PawnUtility_GainComfortFromCellIfPossible_Patch",
			"ZombieLand.PawnUtility_Mated_Patch",
			"ZombieLand.Pawn_CarryTracker_CarryHandsTickInterval_Patch",
			"ZombieLand.Pawn_CarryTracker_TryStartCarry_Patch_Patch",
			"ZombieLand.Pawn_FilthTracker_Notify_EnteredNewCell_Patch",
			"ZombieLand.Pawn_HealthTracker_DropBloodFilth_Patch",
			"ZombieLand.Pawn_Kill_Patch",
			"ZombieLand.Pawn_NeedsTracker_ShouldHaveNeed_Patch",
			"ZombieLand.Plant_TrySpawnStump_Patch",
			"ZombieLand.PlaySettings_DoPlaySettingsGlobalControls_Patch",
			"ZombieLand.PregnancyUtility_SpawnBirthFilth_Patch",
			"ZombieLand.Projectile_Liquid_DoImpact_Patch",
			"ZombieLand.Recipe_RemoveImplant_ApplyOnPawn_Patches",
			"ZombieLand.RoofCollapserImmediate_DropRoofInCellPhaseOne_Patch",
			"ZombieLand.RoofCollapserImmediate_DropRoofInCellPhaseTwo_Patch",
			"ZombieLand.Skyfaller_SpawnThings_Patch",
			"ZombieLand.SmoothableWallUtility_Notify_BuildingDestroying_Patch",
			"ZombieLand.SmoothableWallUtility_SmoothWall_Patch",
			"ZombieLand.TendUtility_DoTend_Patch",
			"ZombieLand.ThingComp_MakeThing_Patch",
			"ZombieLand.ThingMaker_MakeThing_PlantHarvestContext_Patch",
			"ZombieLand.ThingOwner_TryTransferToContainer_Patch",
			"ZombieLand.ThingSetMaker_Generate_Patch",
			"ZombieLand.ThingWithComps_SplitOff_Patch",
			"ZombieLand.Thing_Destroy_Patch",
			"ZombieLand.Thing_Ingested_Patch",
			"ZombieLand.Thing_SpecialDisplayStats_Patch",
			"ZombieLand.Thing_SplitOff_Patch",
			"ZombieLand.Thing_TakeDamage_Patch",
			"ZombieLand.Thing_TryAbsorbStack_Patch",
			"ZombieLand.TradeDeal_AddAllTradeables_Patch",
			"ZombieLand.TunnelHiveSpawner_Tick_Patch",
			"ZombieLand.Verb_MeleeAttack_ApplyMeleeDamageToTarget_Patch",
			"ZombieLand.Verse_Explosion_TrySpawnExplosionThing_Patch",
			"ZombieLand.Widgets_ThingIcon_Patch",
			"ZombieLand.WildPlantSpawner_SpawnPlant_Patch",
			"ZombieLand.WorldGenerator_GenerateWorld_Patch"
		};
	}
}
