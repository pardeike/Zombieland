using HarmonyLib;
using RimBridgeServer.Sdk;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace ZombieLand
{
	public sealed partial class ZombielandBridgeTools
	{
		const string ZombielandHarmonyOwner = "net.pardeike.zombieland";
		const string CaptureFinalizerMethodName = "CaptureFinalizer";
		const string RecoveryFinalizerMethodName = "Finalizer";

		[Tool("zombieland/bootstrap_recovery_contract", Description = "Verify Zombieland's defensive init bootstrap patch surface, finalizer exception capture semantics, and optional TickManager runtime repair without duplicating vanilla initialization.")]
		public static object BootstrapRecoveryContract(
			[ToolParameter(Description = "When true, temporarily breaks only Zombieland's TickManager runtime iterator and verifies the bootstrap rebuilds it.", Required = false, DefaultValue = true)] bool exerciseRuntimeRepair = true)
		{
			var patchSurface = VerifyBootstrapPatchSurface();
			var finalizerMatrix = VerifyBootstrapFinalizerMatrix();
			var runtimeRepair = VerifyBootstrapRuntimeRepair(exerciseRuntimeRepair);
			var zombieGridRepair = VerifyBootstrapZombieGridRepair();
			var playerNotice = VerifyBootstrapPlayerNoticeText();

			return new
			{
				success = ObjectSuccess(patchSurface)
					&& ObjectSuccess(finalizerMatrix)
					&& ObjectSuccess(runtimeRepair)
					&& ObjectSuccess(zombieGridRepair)
					&& ObjectSuccess(playerNotice),
				sourcePath = "ZombieBootstrap finalizer capture plus TickManager.EnsureRuntimeInitialized recovery contract",
				patchSurface,
				finalizerMatrix,
				runtimeRepair,
				zombieGridRepair,
				playerNotice
			};
		}

		static object VerifyBootstrapPatchSurface()
		{
			var targets = new[]
			{
				DescribeBootstrapPatchTarget("Game.FinalizeInit", AccessTools.Method(typeof(Game), nameof(Game.FinalizeInit)), "ZombieLand.Patches+Game_FinalizeInit_Patch", requirePostfix: true, requireFinalizer: true),
				DescribeBootstrapPatchTarget("Map.FinalizeInit", AccessTools.Method(typeof(Map), nameof(Map.FinalizeInit)), "ZombieLand.Patches+Map_FinalizeInit_Patch", requireFinalizer: true),
				DescribeBootstrapPatchTarget("Map.FinalizeLoading", AccessTools.Method(typeof(Map), nameof(Map.FinalizeLoading)), "ZombieLand.Patches+Map_FinalizeLoading_Patch", requirePrefix: true, requireFinalizer: true),
				DescribeBootstrapPatchTarget("MapComponentUtility.FinalizeInit", AccessTools.Method(typeof(MapComponentUtility), nameof(MapComponentUtility.FinalizeInit)), "ZombieLand.Patches+MapComponentUtility_FinalizeInit_Patch", requireFinalizer: true),
				DescribeBootstrapPatchTarget("FactionGenerator.GenerateFactionsIntoWorldLayer", AccessTools.Method(typeof(FactionGenerator), nameof(FactionGenerator.GenerateFactionsIntoWorldLayer)), "ZombieLand.Patches+FactionGenerator_GenerateFactionsIntoWorldLayer_Patch", requirePrefix: true, requireFinalizer: true),
				DescribeBootstrapPatchTarget("WorldGenStep_Factions.GenerateFresh", AccessTools.Method(typeof(WorldGenStep_Factions), nameof(WorldGenStep_Factions.GenerateFresh)), "ZombieLand.Patches+WorldGenStep_Factions_GenerateFresh_Patch", requireFinalizer: true),
				DescribeBootstrapPatchTarget("FactionManager.ExposeData", AccessTools.Method(typeof(FactionManager), nameof(FactionManager.ExposeData)), "ZombieLand.Patches+FactionManager_ExposeData_Patch", requirePostfix: true, requireFinalizer: true)
			};

			return new
			{
				success = targets.All(ObjectSuccess),
				owner = ZombielandHarmonyOwner,
				targets
			};
		}

		static object DescribeBootstrapPatchTarget(string name, MethodBase method, string expectedPatchType, bool requirePrefix = false, bool requirePostfix = false, bool requireFinalizer = false)
		{
			var patchInfo = method == null ? null : Harmony.GetPatchInfo(method);
			var prefixOwners = PatchOwners(patchInfo?.Prefixes);
			var postfixOwners = PatchOwners(patchInfo?.Postfixes);
			var transpilerOwners = PatchOwners(patchInfo?.Transpilers);
			var finalizerOwners = PatchOwners(patchInfo?.Finalizers);
			var prefixes = PatchDetails(patchInfo?.Prefixes);
			var postfixes = PatchDetails(patchInfo?.Postfixes);
			var finalizers = PatchDetails(patchInfo?.Finalizers);

			var hasPrefix = HasExpectedPatch(prefixes, expectedPatchType, "Prefix");
			var hasPostfix = HasExpectedPatch(postfixes, expectedPatchType, "Postfix");
			var hasFinalizer = finalizers.Any(patch => patch.owner == ZombielandHarmonyOwner && patch.declaringType == expectedPatchType);
			var captureFinalizer = finalizers.FirstOrDefault(patch => patch.owner == ZombielandHarmonyOwner
				&& patch.declaringType == expectedPatchType
				&& patch.methodName == CaptureFinalizerMethodName
				&& patch.priority == ZombieBootstrap.CaptureFinalizerPriority);
			var recoveryFinalizer = finalizers.FirstOrDefault(patch => patch.owner == ZombielandHarmonyOwner
				&& patch.declaringType == expectedPatchType
				&& patch.methodName == RecoveryFinalizerMethodName
				&& patch.priority == Priority.Last);
			var hasCaptureFinalizer = captureFinalizer != null;
			var hasRecoveryFinalizer = recoveryFinalizer != null;
			var finalizerPriorityOrderValid = hasCaptureFinalizer
				&& hasRecoveryFinalizer
				&& captureFinalizer.priority > recoveryFinalizer.priority;
			var success = method != null
				&& (requirePrefix == false || hasPrefix)
				&& (requirePostfix == false || hasPostfix)
				&& (requireFinalizer == false || (hasFinalizer && hasCaptureFinalizer && hasRecoveryFinalizer && finalizerPriorityOrderValid));

			return new
			{
				success,
				name,
				methodFound = method != null,
				expectedPatchType,
				requirePrefix,
				requirePostfix,
				requireFinalizer,
				hasPrefix,
				hasPostfix,
				hasFinalizer,
				hasCaptureFinalizer,
				hasRecoveryFinalizer,
				finalizerPriorityOrderValid,
				prefixOwners,
				postfixOwners,
				transpilerOwners,
				finalizerOwners,
				prefixes,
				postfixes,
				finalizers
			};
		}

		static string[] PatchOwners(IEnumerable<Patch> patches)
			=> patches?
				.Select(patch => patch.owner)
				.Where(owner => owner.NullOrEmpty() == false)
				.Distinct()
				.OrderBy(owner => owner)
				.ToArray()
			?? Array.Empty<string>();

		static bool HasExpectedPatch(IEnumerable<PatchDetail> patches, string expectedPatchType, string methodName)
			=> patches.Any(patch => patch.owner == ZombielandHarmonyOwner
				&& patch.declaringType == expectedPatchType
				&& patch.methodName == methodName);

		static PatchDetail[] PatchDetails(IEnumerable<Patch> patches)
			=> patches?
				.Select(patch =>
				{
					var method = patch.PatchMethod;
					return new PatchDetail
					{
						owner = patch.owner,
						priority = patch.priority,
						index = patch.index,
						declaringType = method?.DeclaringType?.FullName,
						methodName = method?.Name
					};
				})
				.ToArray()
			?? Array.Empty<PatchDetail>();

		sealed class PatchDetail
		{
			public string owner { get; set; }
			public int priority { get; set; }
			public int index { get; set; }
			public string declaringType { get; set; }
			public string methodName { get; set; }
		}

		static object VerifyBootstrapFinalizerMatrix()
		{
			var suffix = Guid.NewGuid().ToString("N");
			var cleanSkipPhase = $"Bridge bootstrap clean skip {suffix}";
			var cleanRunDefaultPhase = $"Bridge bootstrap clean run default {suffix}";
			var cleanRunOptInPhase = $"Bridge bootstrap clean run opt in {suffix}";
			var ignoredPhase = $"Bridge bootstrap ignored captured {suffix}";
			var replacedPhase = $"Bridge bootstrap replaced exception {suffix}";
			var swallowedPhase = $"Bridge bootstrap swallowed {suffix}";
			var propagatedPhase = $"Bridge bootstrap propagated {suffix}";

			var cleanSkipRuns = ZombieBootstrap.ShouldRunFinalizerRecovery(cleanSkipPhase, null, false, out var cleanSkipObserved);
			var cleanRunDefaultRuns = ZombieBootstrap.ShouldRunFinalizerRecovery(cleanRunDefaultPhase, null, true, out var cleanRunDefaultObserved);
			var cleanRunOptInRuns = ZombieBootstrap.ShouldRunFinalizerRecovery(cleanRunOptInPhase, null, true, out var cleanRunOptInObserved, runWhenOriginalSucceeded: true);

			var ignoredException = new InvalidOperationException("synthetic ignored init exception");
			var capturedIgnored = ZombieBootstrap.CaptureFinalizerException(ignoredPhase, ignoredException);
			var ignoredRuns = ZombieBootstrap.ShouldRunFinalizerRecovery(ignoredPhase, ignoredException, true, out var ignoredObserved);
			var cleanAfterIgnoredRuns = ZombieBootstrap.ShouldRunFinalizerRecovery(ignoredPhase, null, true, out var cleanAfterIgnoredObserved);

			var replacedOriginalException = new InvalidOperationException("synthetic original init exception");
			var replacedCurrentException = new ApplicationException("synthetic replacement finalizer exception");
			var capturedReplaced = ZombieBootstrap.CaptureFinalizerException(replacedPhase, replacedOriginalException);
			var replacedRuns = ZombieBootstrap.ShouldRunFinalizerRecovery(replacedPhase, replacedCurrentException, true, out var replacedObserved);
			var replacedPassthrough = ZombieBootstrap.RecoveryPassthrough(replacedPhase, replacedCurrentException, replacedObserved, false);
			var cleanAfterReplacedRuns = ZombieBootstrap.ShouldRunFinalizerRecovery(replacedPhase, null, true, out var cleanAfterReplacedObserved);

			var swallowedException = new InvalidOperationException("synthetic swallowed init exception");
			var capturedSwallowed = ZombieBootstrap.CaptureFinalizerException(swallowedPhase, swallowedException);
			var swallowedRuns = ZombieBootstrap.ShouldRunFinalizerRecovery(swallowedPhase, null, false, out var swallowedObserved);
			var swallowedPassthrough = ZombieBootstrap.RecoveryPassthrough(swallowedPhase, null, swallowedObserved, false);

			var propagatedException = new ApplicationException("synthetic propagated init exception");
			var capturedPropagated = ZombieBootstrap.CaptureFinalizerException(propagatedPhase, propagatedException);
			var propagatedRuns = ZombieBootstrap.ShouldRunFinalizerRecovery(propagatedPhase, propagatedException, true, out var propagatedObserved);
			var propagatedPassthrough = ZombieBootstrap.RecoveryPassthrough(propagatedPhase, propagatedException, propagatedObserved, false);

			return new
			{
				success = cleanSkipRuns == false
					&& cleanSkipObserved == null
					&& cleanRunDefaultRuns == false
					&& cleanRunDefaultObserved == null
					&& cleanRunOptInRuns
					&& cleanRunOptInObserved == null
					&& ReferenceEquals(capturedIgnored, ignoredException)
					&& ignoredRuns
					&& ReferenceEquals(ignoredObserved, ignoredException)
					&& cleanAfterIgnoredRuns == false
					&& cleanAfterIgnoredObserved == null
					&& ReferenceEquals(capturedReplaced, replacedOriginalException)
					&& replacedRuns
					&& ReferenceEquals(replacedObserved, replacedCurrentException)
					&& ReferenceEquals(replacedPassthrough, replacedCurrentException)
					&& cleanAfterReplacedRuns == false
					&& cleanAfterReplacedObserved == null
					&& ReferenceEquals(capturedSwallowed, swallowedException)
					&& swallowedRuns
					&& ReferenceEquals(swallowedObserved, swallowedException)
					&& swallowedPassthrough == null
					&& ReferenceEquals(capturedPropagated, propagatedException)
					&& propagatedRuns
					&& ReferenceEquals(propagatedObserved, propagatedException)
					&& ReferenceEquals(propagatedPassthrough, propagatedException),
				cases = new
				{
					cleanSkippedOriginal = new
					{
						ranRecovery = cleanSkipRuns,
						observedException = cleanSkipObserved?.GetType().FullName
					},
					cleanRanOriginalDefault = new
					{
						ranRecovery = cleanRunDefaultRuns,
						observedException = cleanRunDefaultObserved?.GetType().FullName
					},
					cleanRanOriginalOptIn = new
					{
						ranRecovery = cleanRunOptInRuns,
						observedException = cleanRunOptInObserved?.GetType().FullName
					},
					ignoredCapturedException = new
					{
						captured = ReferenceEquals(capturedIgnored, ignoredException),
						ranRecovery = ignoredRuns,
						observedSameException = ReferenceEquals(ignoredObserved, ignoredException),
						cleanAfterIgnoredRanRecovery = cleanAfterIgnoredRuns,
						cleanAfterIgnoredObservedException = cleanAfterIgnoredObserved?.GetType().FullName
					},
					replacedException = new
					{
						captured = ReferenceEquals(capturedReplaced, replacedOriginalException),
						ranRecovery = replacedRuns,
						observedCurrentException = ReferenceEquals(replacedObserved, replacedCurrentException),
						passthroughCurrentException = ReferenceEquals(replacedPassthrough, replacedCurrentException),
						cleanAfterReplacedRanRecovery = cleanAfterReplacedRuns,
						cleanAfterReplacedObservedException = cleanAfterReplacedObserved?.GetType().FullName
					},
					swallowedException = new
					{
						captured = ReferenceEquals(capturedSwallowed, swallowedException),
						ranRecovery = swallowedRuns,
						observedSameException = ReferenceEquals(swallowedObserved, swallowedException),
						passthroughException = swallowedPassthrough?.GetType().FullName
					},
					propagatedException = new
					{
						captured = ReferenceEquals(capturedPropagated, propagatedException),
						ranRecovery = propagatedRuns,
						observedSameException = ReferenceEquals(propagatedObserved, propagatedException),
						passthroughSameException = ReferenceEquals(propagatedPassthrough, propagatedException)
					}
				}
			};
		}

		static object VerifyBootstrapRuntimeRepair(bool exerciseRuntimeRepair)
		{
			if (exerciseRuntimeRepair == false)
			{
				return new
				{
					success = true,
					skipped = true,
					reason = "Runtime repair probe disabled by request."
				};
			}

			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded for the TickManager runtime repair probe."
				};
			}

			var tickManager = map.GetComponent<TickManager>();
			if (tickManager == null)
			{
				return new
				{
					success = false,
					mapId = map.uniqueID,
					error = "Current map has no Zombieland TickManager component."
				};
			}

			var taskTickerField = AccessTools.Field(typeof(TickManager), "taskTicker");
			if (taskTickerField == null)
			{
				return new
				{
					success = false,
					mapId = map.uniqueID,
					error = "Could not reflect TickManager.taskTicker."
				};
			}

			var listerThingsField = AccessTools.Field(typeof(Map), "listerThings");
			var initializationProblemLoggedField = AccessTools.Field(typeof(TickManager), "initializationProblemLogged");
			var originalTaskTicker = taskTickerField.GetValue(tickManager);
			var originalListerThings = listerThingsField?.GetValue(map);
			var originalInitializationProblemLogged = initializationProblemLoggedField?.GetValue(tickManager);
			var originalInitialized = tickManager.isInitialized;
			var originalReady = tickManager.RuntimeReady;
			var success = false;
			var restoredOriginalRuntime = false;
			var restoredMapPrerequisite = false;
			var readyAfterBreak = false;
			var changed = false;
			var readyAfterRecovery = false;
			var missingPrerequisiteProbeAvailable = listerThingsField != null && originalListerThings != null;
			var missingPrerequisiteChanged = false;
			var missingPrerequisiteReadyAfter = true;
			var missingPrerequisiteRestored = false;
			var missingPrerequisiteReadyAfterRestore = false;
			var stateZeroReadyAfterBreak = false;
			var stateZeroRecovered = false;
			var stateZeroReadyAfterRecovery = false;
			var stateZeroFailedClosed = false;

			try
			{
				if (originalReady == false)
				{
					var changedExisting = ZombieBootstrap.EnsureMapStateAfterFinalize("Bridge bootstrap existing runtime repair contract", map, true);
					success = tickManager.RuntimeReady;
					return new
					{
						success,
						mapId = map.uniqueID,
						originalReady,
						originalInitialized,
						changedExisting,
						readyAfterRecovery = tickManager.RuntimeReady,
						note = "Runtime was already incomplete before the contract; the probe only attempted the normal recovery path."
					};
				}

				taskTickerField.SetValue(tickManager, null);
				tickManager.isInitialized = 2;
				readyAfterBreak = tickManager.RuntimeReady;

				changed = ZombieBootstrap.EnsureMapStateAfterFinalize("Bridge bootstrap forced runtime repair contract", map, true);
				readyAfterRecovery = tickManager.RuntimeReady;

				if (missingPrerequisiteProbeAvailable)
				{
					taskTickerField.SetValue(tickManager, null);
					tickManager.isInitialized = 2;
					listerThingsField.SetValue(map, null);
					initializationProblemLoggedField?.SetValue(tickManager, true);
					missingPrerequisiteChanged = ZombieBootstrap.EnsureMapStateAfterFinalize("Bridge bootstrap missing listerThings prerequisite contract", map, true);
					missingPrerequisiteReadyAfter = tickManager.RuntimeReady;
					if (originalInitializationProblemLogged != null)
						initializationProblemLoggedField?.SetValue(tickManager, originalInitializationProblemLogged);
					listerThingsField.SetValue(map, originalListerThings);
					restoredMapPrerequisite = true;
					missingPrerequisiteRestored = ZombieBootstrap.EnsureMapStateAfterFinalize("Bridge bootstrap restored listerThings prerequisite contract", map, true);
					missingPrerequisiteReadyAfterRestore = tickManager.RuntimeReady;
				}

				taskTickerField.SetValue(tickManager, null);
				tickManager.isInitialized = 0;
				stateZeroReadyAfterBreak = tickManager.RuntimeReady;
				initializationProblemLoggedField?.SetValue(tickManager, true);
				stateZeroRecovered = tickManager.TryEnsureRuntimeInitialized("Bridge bootstrap state-zero tick repair contract");
				stateZeroReadyAfterRecovery = tickManager.RuntimeReady;
				if (originalInitializationProblemLogged != null)
					initializationProblemLoggedField?.SetValue(tickManager, originalInitializationProblemLogged);
				stateZeroFailedClosed = stateZeroRecovered == false && stateZeroReadyAfterRecovery == false;

				success = readyAfterBreak == false
					&& changed
					&& readyAfterRecovery
					&& (missingPrerequisiteProbeAvailable == false || (missingPrerequisiteChanged == false && missingPrerequisiteReadyAfter == false && missingPrerequisiteRestored && missingPrerequisiteReadyAfterRestore))
					&& stateZeroReadyAfterBreak == false
					&& stateZeroFailedClosed;
			}
			finally
			{
				if (missingPrerequisiteProbeAvailable && ReferenceEquals(listerThingsField.GetValue(map), originalListerThings) == false)
				{
					listerThingsField.SetValue(map, originalListerThings);
					restoredMapPrerequisite = true;
				}
				if (originalInitializationProblemLogged != null)
					initializationProblemLoggedField?.SetValue(tickManager, originalInitializationProblemLogged);
				if (originalReady)
				{
					taskTickerField.SetValue(tickManager, originalTaskTicker);
					tickManager.isInitialized = originalInitialized;
					restoredOriginalRuntime = true;
				}
			}

			return new
			{
				success,
				mapId = map.uniqueID,
				originalReady,
				originalInitialized,
				readyAfterBreak,
				changed,
				readyAfterRecovery,
				initializedAfterRecovery = tickManager.isInitialized,
				taskTickerAfterRecovery = taskTickerField.GetValue(tickManager) != null,
				missingPrerequisiteProbeAvailable,
				missingPrerequisiteChanged,
				missingPrerequisiteReadyAfter,
				missingPrerequisiteRestored,
				missingPrerequisiteReadyAfterRestore,
				restoredMapPrerequisite,
				stateZeroReadyAfterBreak,
				stateZeroRecovered,
				stateZeroReadyAfterRecovery,
				stateZeroFailedClosed,
				restoredOriginalRuntime
			};
		}

		static object VerifyBootstrapZombieGridRepair()
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded for the zombie grid repair probe."
				};
			}

			var grid = map.GetComponent<PheromoneGrid>();
			if (grid == null)
			{
				return new
				{
					success = false,
					mapId = map.uniqueID,
					error = "Current map has no Zombieland PheromoneGrid component."
				};
			}

			var zombie = map.mapPawns?.AllPawnsSpawned?.OfType<Zombie>().FirstOrDefault(pawn => pawn.Spawned && pawn.Dead == false);
			if (zombie == null)
			{
				return new
				{
					success = true,
					mapId = map.uniqueID,
					skipped = true,
					reason = "Current map has no spawned live zombie for the grid rebuild probe."
				};
			}

			var originalLastGoto = zombie.lastGotoPosition;
			var snapshot = SnapshotZombieCounts(map, grid);
			var probeCell = zombie.Position;
			try
			{
				zombie.lastGotoPosition = probeCell;
				ClearZombieCounts(grid);
				grid.ChangeZombieCount(probeCell, -1);
				var underflowCount = grid.GetZombieCount(probeCell);

				ZombieBootstrap.ResetZombieGrid("Bridge bootstrap zombie grid repair contract", map, rebuildLiveZombieCounts: true);
				var rebuiltCount = grid.GetZombieCount(probeCell);

				return new
				{
					success = underflowCount == 0 && rebuiltCount > 0,
					mapId = map.uniqueID,
					zombie = zombie.ThingID,
					probeCell = ZombieRuntimeActions.DescribeCell(probeCell),
					underflowCount,
					rebuiltCount
				};
			}
			finally
			{
				zombie.lastGotoPosition = originalLastGoto;
				RestoreZombieCounts(grid, snapshot);
			}
		}

		static Dictionary<IntVec3, int> SnapshotZombieCounts(Map map, PheromoneGrid grid)
		{
			var snapshot = new Dictionary<IntVec3, int>();
			grid.IterateCells((x, z, cell) =>
			{
				if (cell.zombieCount != 0)
					snapshot[new IntVec3(x, 0, z)] = cell.zombieCount;
			});
			return snapshot;
		}

		static void ClearZombieCounts(PheromoneGrid grid)
			=> grid.IterateCellsQuick(cell => cell.zombieCount = 0);

		static object VerifyBootstrapPlayerNoticeText()
		{
			const string labelKey = "LetterLabelZombielandMapSetupFailed";
			const string textKey = "ZombielandMapSetupFailed";
			const string revealLogKey = "ZombielandRevealPlayerLog";
			const string revealFailedKey = "ZombielandPlayerLogRevealFailed";
			const string pathUnavailableKey = "ZombielandPlayerLogPathUnavailable";
			var labelCanTranslate = labelKey.CanTranslate();
			var textCanTranslate = textKey.CanTranslate();
			var revealLogCanTranslate = revealLogKey.CanTranslate();
			var revealFailedCanTranslate = revealFailedKey.CanTranslate();
			var pathUnavailableCanTranslate = pathUnavailableKey.CanTranslate();
			var label = labelKey.Translate().ToString();
			var text = textKey.Translate().ToString();
			var revealLog = revealLogKey.Translate().ToString();
			var labelResolved = label.NullOrEmpty() == false && label != labelKey;
			var textResolved = text.NullOrEmpty() == false && text != textKey;
			var revealLogResolved = revealLog.NullOrEmpty() == false && revealLog != revealLogKey;
			var mentionsZombieland = text.IndexOf("Zombieland", StringComparison.OrdinalIgnoreCase) >= 0;
			var mentionsRimWorld = text.IndexOf("RimWorld", StringComparison.OrdinalIgnoreCase) >= 0;

			return new
			{
				success = labelCanTranslate
					&& textCanTranslate
					&& labelResolved
					&& textResolved
					&& revealLogCanTranslate
					&& revealFailedCanTranslate
					&& pathUnavailableCanTranslate
					&& revealLogResolved
					&& mentionsZombieland
					&& mentionsRimWorld,
				labelKey,
				textKey,
				revealLogKey,
				revealFailedKey,
				pathUnavailableKey,
				labelCanTranslate,
				textCanTranslate,
				revealLogCanTranslate,
				revealFailedCanTranslate,
				pathUnavailableCanTranslate,
				label,
				revealLog,
				textLength = text.Length,
				labelResolved,
				textResolved,
				revealLogResolved,
				mentionsZombieland,
				mentionsRimWorld
			};
		}

		static void RestoreZombieCounts(PheromoneGrid grid, Dictionary<IntVec3, int> snapshot)
		{
			ClearZombieCounts(grid);
			foreach (var item in snapshot)
				grid.ChangeZombieCount(item.Key, item.Value);
		}
	}
}
