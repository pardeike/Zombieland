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
		sealed class AnomalyMusicPlaybackSample
		{
			SongDef selectedSong;

			public int share { get; set; }
			public bool predicateSatisfied { get; set; }
			public bool expectedTransitionSelected { get; set; }
			public string triggeredTransition { get; set; }
			public string sequence { get; set; }
			public string managerState { get; set; }
			public bool isPlaying { get; set; }
			public string songDefName { get; set; }
			public string songLabel { get; set; }
			public string clipPath { get; set; }
			public string audioClipName { get; set; }
			public bool audioClipMatchesSong { get; set; }
			public bool isZombielandSong { get; set; }
			public AnomalyMusicPlayObservation playCommand { get; set; }

			internal SongDef SelectedSong => selectedSong;

			public void SetSelectedSong(SongDef song)
			{
				selectedSong = song;
			}
		}

		sealed class AnomalyMusicPlayObservation
		{
			SongDef sourceSong;
			SongDef finalSong;
			SongDef currentSong;

			public int callCount { get; set; }
			public string sourceRequested { get; set; }
			public string finalArgument { get; set; }
			public string currentSongAfterPlay { get; set; }
			public string audioClipAfterPlay { get; set; }
			public bool sourceWasZombieland { get; set; }
			public bool finalWasZombieland { get; set; }
			public bool finalArgumentMatchesCurrentSong { get; set; }
			public bool audioClipMatchesCurrentSong { get; set; }

			internal SongDef SourceSong => sourceSong;
			internal SongDef FinalSong => finalSong;
			internal SongDef CurrentSong => currentSong;

			public void SetSourceSong(SongDef song)
			{
				sourceSong = song;
			}

			public void SetFinalSong(SongDef song)
			{
				finalSong = song;
			}

			public void SetCurrentSong(SongDef song)
			{
				currentSong = song;
			}
		}

		sealed class AnomalyMusicStageResult
		{
			public bool success { get; set; }
			public string id { get; set; }
			public string transition { get; set; }
			public string predicateSource { get; set; }
			public object fixture { get; set; }
			public string[] satisfiedTransitions { get; set; }
			public string highestSatisfiedTransition { get; set; }
			public AnomalyMusicPlaybackSample control { get; set; }
			public AnomalyMusicPlaybackSample replacement { get; set; }
			public string wouldNormallyPlay { get; set; }
			public string replacementActuallyPlayed { get; set; }
			public string expectedReplacement { get; set; }
			public bool exactOneToOneReplacement { get; set; }
		}

		sealed class AnomalyMusicScenarioResult
		{
			public bool success { get; set; }
			public string stage { get; set; }
			public string error { get; set; }
			public bool anomalyActive { get; set; }
			public bool exactMinimalLoadout { get; set; }
			public bool exactAllOfficialDlcLoadout { get; set; }
			public bool supportedLoadout { get; set; }
			public string[] activePackages { get; set; }
			public string[] expectedPackages { get; set; }
			public string[] allOfficialDlcPackages { get; set; }
			public object loadoutContract { get; set; }
			public object replacementContract { get; set; }
			public AnomalyMusicStageResult relax { get; set; }
			public AnomalyMusicStageResult tension { get; set; }
			public AnomalyMusicStageResult combat { get; set; }
			public AnomalyMusicFinalSettingsResult finalSettings { get; set; }
			public object playCommandObserver { get; set; }
			public AnomalyMusicCleanupResult cleanup { get; set; }
			public object finalizationContract { get; set; }
		}

		sealed class AnomalyMusicFinalSettingsResult
		{
			public bool success { get; set; }
			public bool leaveSettingsAt100 { get; set; }
			public bool playZombielandMusic { get; set; }
			public int zombielandMusicShare { get; set; }
			public bool expectedPlayZombielandMusic { get; set; }
			public int expectedZombielandMusicShare { get; set; }
		}

		sealed class AnomalyMusicCleanupResult
		{
			public bool success { get; set; }
			public bool observerPatchRemoved { get; set; }
			public bool noctolithRemoved { get; set; }
			public bool unnaturalDarknessRemoved { get; set; }
			public bool metalhorrorsRemoved { get; set; }
			public string dangerRating { get; set; }
			public bool dangerMusicMode { get; set; }
		}

		const string AnomalyMusicObserverHarmonyId = "net.pardeike.zombieland.bridge.anomaly-music-observer";
		static readonly FieldInfo anomalyMusicTransitionsField = AccessTools.Field(typeof(MusicManagerPlay), "transitions");
		static readonly FieldInfo anomalyMusicAudioSourceField = AccessTools.Field(typeof(MusicManagerPlay), "audioSource");
		static readonly FieldInfo dangerWatcherLastUpdateTickField = AccessTools.Field(typeof(DangerWatcher), "lastUpdateTick");
		static readonly MethodInfo anomalyMusicPlaySongMethod = AccessTools.Method(typeof(MusicManagerPlay), "PlaySong", new[] { typeof(SongDef), typeof(bool), typeof(bool) });
		static AnomalyMusicPlayObservation currentAnomalyMusicPlayObservation;

		[Tool("zombieland/anomaly_music_replacement_scenario", Description = "Provoke the real Anomaly relax, tension, and combat music predicates, then compare normal 0% playback with exact 100% Zombieland replacement playback.")]
		public static object AnomalyMusicReplacementScenario(
			[ToolParameter(Description = "Leave the loaded game's Zombieland music settings enabled at 100% after cleanup; otherwise restore the original settings.", DefaultValue = true)] bool leaveSettingsAt100 = true,
			[ToolParameter(Description = "Deterministic base seed used to select the same source track in each 0%/100% pair.", DefaultValue = 73101)] int seed = 73101)
		{
			var result = new AnomalyMusicScenarioResult
			{
				stage = "validate",
				anomalyActive = ModsConfig.AnomalyActive
			};
			var map = CurrentMap;
			if (Current.Game == null || map == null)
			{
				result.error = "A loaded playable map is required.";
				return result;
			}
			if (ModsConfig.AnomalyActive == false)
			{
				result.error = "The Anomaly DLC is not active.";
				return result;
			}

			var expectedPackages = new[]
			{
				"brrainz.harmony",
				"brrainz.rimbridgeserver_steam",
				"ludeon.rimworld",
				"ludeon.rimworld.anomaly",
				"brrainz.zombieland"
			};
			var allOfficialDlcPackages = new[]
			{
				"brrainz.harmony",
				"brrainz.rimbridgeserver_steam",
				"ludeon.rimworld",
				"ludeon.rimworld.royalty",
				"ludeon.rimworld.ideology",
				"ludeon.rimworld.biotech",
				"ludeon.rimworld.anomaly",
				"ludeon.rimworld.odyssey",
				"brrainz.zombieland"
			};
			var activePackages = LoadedModManager.RunningModsListForReading
				.Select(mod => mod.PackageId)
				.OrderBy(packageId => packageId, StringComparer.OrdinalIgnoreCase)
				.ToArray();
			result.expectedPackages = expectedPackages.OrderBy(packageId => packageId, StringComparer.OrdinalIgnoreCase).ToArray();
			result.allOfficialDlcPackages = allOfficialDlcPackages.OrderBy(packageId => packageId, StringComparer.OrdinalIgnoreCase).ToArray();
			result.activePackages = activePackages;
			result.exactMinimalLoadout = PackageSetsEqual(activePackages, expectedPackages);
			result.exactAllOfficialDlcLoadout = PackageSetsEqual(activePackages, allOfficialDlcPackages);
			result.supportedLoadout = IsSupportedAnomalyMusicLoadout(activePackages, expectedPackages, allOfficialDlcPackages);
			result.loadoutContract = AnomalyMusicLoadoutContractState(expectedPackages, allOfficialDlcPackages);
			result.replacementContract = ZombielandMusic.AnomalyReplacementContractState();

			var manager = Find.MusicManagerPlay;
			var transitions = anomalyMusicTransitionsField?.GetValue(manager) as List<MusicTransition>;
			if (manager == null || transitions == null || anomalyMusicAudioSourceField == null)
			{
				result.error = "The live music manager transition or audio-source state was unavailable.";
				return result;
			}

			var settingsSnapshot = SnapshotZombieSettings();
			var initialEffectiveSettings = ZombieSettings.ValuesAtGameTick(GenTicks.TicksGame)?.MakeCopy();
			Harmony observerHarmony = null;
			Thing noctolith = null;
			GameCondition unnaturalDarkness = null;
			var metalhorrors = new List<Pawn>();
			try
			{
				result.stage = "observer.install";
				if (anomalyMusicPlaySongMethod == null)
					throw new InvalidOperationException("Could not resolve MusicManagerPlay.PlaySong for the temporary playback observer.");
				var observerPrefix = AccessTools.Method(typeof(ZombielandBridgeTools), nameof(ObserveAnomalyMusicPlayPrefix));
				var observerPostfix = AccessTools.Method(typeof(ZombielandBridgeTools), nameof(ObserveAnomalyMusicPlayPostfix));
				if (observerPrefix == null || observerPostfix == null)
					throw new InvalidOperationException("Could not resolve the temporary playback observer methods.");
				observerHarmony = new Harmony(AnomalyMusicObserverHarmonyId);
				observerHarmony.Unpatch(anomalyMusicPlaySongMethod, HarmonyPatchType.All, observerHarmony.Id);
				observerHarmony.Patch(
					anomalyMusicPlaySongMethod,
					prefix: new HarmonyMethod(observerPrefix) { priority = Priority.First },
					postfix: new HarmonyMethod(observerPostfix) { priority = Priority.Last });
				var observerPatchInfo = Harmony.GetPatchInfo(anomalyMusicPlaySongMethod);
				result.playCommandObserver = new
				{
					target = anomalyMusicPlaySongMethod.FullDescription(),
					owner = observerHarmony.Id,
					prefixInstalled = observerPatchInfo?.Prefixes.Any(patch => patch.owner == observerHarmony.Id) == true,
					postfixInstalled = observerPatchInfo?.Postfixes.Any(patch => patch.owner == observerHarmony.Id) == true,
					prefixPriority = Priority.First,
					postfixPriority = Priority.Last
				};

				result.stage = "relax.setup";
				var noctolithDef = DefDatabase<ThingDef>.GetNamedSilentFail("Noctolith")
					?? throw new InvalidOperationException("The Anomaly Noctolith ThingDef was not loaded.");
				if (TryFindClearSpawnCell(map, map.Center, 24f, out var noctolithCell, out _) == false)
					throw new InvalidOperationException("Could not find a clear cell for the Noctolith relax fixture.");
				noctolith = ThingMaker.MakeThing(noctolithDef);
				GenSpawn.Spawn(noctolith, noctolithCell, map, Rot4.South);
				result.relax = RunAnomalyMusicStage(
					manager,
					transitions,
					"relax",
					"HorrorRelax",
					"HorrorRelaxTransition.IsValidMap: a spawned Noctolith while DangerMusicMode is false",
					new
					{
						thing = DescribeAnomalyThing(noctolith),
						dangerMusicMode = manager.DangerMusicMode
					},
					seed);
				manager.Stop();
				CleanupAnomalyThing(noctolith);

				result.stage = "tension.setup";
				var darknessDef = DefDatabase<GameConditionDef>.GetNamedSilentFail("UnnaturalDarkness")
					?? throw new InvalidOperationException("The Anomaly UnnaturalDarkness GameConditionDef was not loaded.");
				unnaturalDarkness = GameConditionMaker.MakeCondition(darknessDef, GenDate.TicksPerDay);
				map.gameConditionManager.RegisterCondition(unnaturalDarkness);
				result.tension = RunAnomalyMusicStage(
					manager,
					transitions,
					"tension",
					"HorrorTension",
					"HorrorTensionTransition.IsTransitionSatisfied: active UnnaturalDarkness while DangerMusicMode is false",
					new
					{
						condition = unnaturalDarkness.def.defName,
						active = map.gameConditionManager.ConditionIsActive(darknessDef),
						dangerMusicMode = manager.DangerMusicMode
					},
					seed + 100);
				manager.Stop();
				unnaturalDarkness.End();

				result.stage = "combat.setup";
				var metalhorrorKind = DefDatabase<PawnKindDef>.GetNamedSilentFail("Metalhorror")
					?? throw new InvalidOperationException("The Anomaly Metalhorror PawnKindDef was not loaded.");
				var entitiesFaction = ResolveAnomalyMatrixFaction("entities")
					?? throw new InvalidOperationException("The Anomaly Entities faction was not loaded.");
				for (var i = 0; i < 2; i++)
				{
					var requestedCell = map.Center + new IntVec3(12 + i * 3, 0, 8);
					if (TryFindClearSpawnCell(map, requestedCell, 20f, out var entityCell, out _) == false)
						throw new InvalidOperationException($"Could not find a clear cell for Metalhorror {i + 1}.");
					var pawn = PawnGenerator.GeneratePawn(metalhorrorKind, entitiesFaction);
					GenSpawn.Spawn(pawn, entityCell, map, Rot4.South);
					ApplyAnomalyState(pawn, null, "awake");
					metalhorrors.Add(pawn);
				}
				InvalidateDangerWatcher(map);
				var dangerRating = map.dangerWatcher.DangerRating;
				result.combat = RunAnomalyMusicStage(
					manager,
					transitions,
					"combat",
					"HorrorCombat",
					"HorrorCombatTransition.IsValidEntity plus natural DangerWatcher.High: two hostile, awake Metalhorrors and a spawned player colonist",
					new
					{
						colonistCount = map.mapPawns.ColonistsSpawnedCount,
						dangerRating = dangerRating.ToString(),
						dangerMusicMode = manager.DangerMusicMode,
						entities = metalhorrors.Select(pawn => new
						{
							pawn = DescribePawn(pawn),
							pawn.kindDef.combatPower,
							isAnomalyEntity = pawn.RaceProps.IsAnomalyEntity,
							hostileToPlayer = pawn.HostileTo(Faction.OfPlayer),
							awake = pawn.canBeDormant?.Awake,
							fogged = pawn.Fogged()
						}).ToArray()
					},
					seed + 200);

				result.stage = "cleanup";
			}
			catch (Exception ex)
			{
				result.error = ex.GetBaseException().Message;
			}
			finally
			{
				currentAnomalyMusicPlayObservation = null;
				if (observerHarmony != null && anomalyMusicPlaySongMethod != null)
					observerHarmony.Unpatch(anomalyMusicPlaySongMethod, HarmonyPatchType.All, observerHarmony.Id);
				manager.Stop();
				if (unnaturalDarkness != null && map.gameConditionManager.ActiveConditions.Contains(unnaturalDarkness))
					unnaturalDarkness.End();
				if (noctolith != null)
					CleanupAnomalyThing(noctolith);
				for (var i = metalhorrors.Count - 1; i >= 0; i--)
					CleanupAnomalyThing(metalhorrors[i]);
				InvalidateDangerWatcher(map);
				var dangerAfterCleanup = map.dangerWatcher.DangerRating;

				if (leaveSettingsAt100)
					ApplyAnomalyMusicSettings(100);
				else
					RestoreZombieSettings(settingsSnapshot);

				var values = ZombieSettings.ValuesAtGameTick(GenTicks.TicksGame);
				var expectedPlayZombielandMusic = leaveSettingsAt100 || initialEffectiveSettings?.playZombielandMusic == true;
				var expectedZombielandMusicShare = leaveSettingsAt100 ? 100 : initialEffectiveSettings?.zombielandMusicShare ?? -1;
				result.finalSettings = new AnomalyMusicFinalSettingsResult
				{
					success = values != null
						&& values.playZombielandMusic == expectedPlayZombielandMusic
						&& values.zombielandMusicShare == expectedZombielandMusicShare,
					leaveSettingsAt100 = leaveSettingsAt100,
					playZombielandMusic = values?.playZombielandMusic == true,
					zombielandMusicShare = values?.zombielandMusicShare ?? -1,
					expectedPlayZombielandMusic = expectedPlayZombielandMusic,
					expectedZombielandMusicShare = expectedZombielandMusicShare
				};
				var cleanup = new AnomalyMusicCleanupResult
				{
					observerPatchRemoved = anomalyMusicPlaySongMethod == null
						|| Harmony.GetPatchInfo(anomalyMusicPlaySongMethod)?.Owners.Contains(AnomalyMusicObserverHarmonyId) != true,
					noctolithRemoved = noctolith == null || noctolith.Destroyed || noctolith.Spawned == false,
					unnaturalDarknessRemoved = unnaturalDarkness == null || map.gameConditionManager.ActiveConditions.Contains(unnaturalDarkness) == false,
					metalhorrorsRemoved = metalhorrors.All(pawn => pawn == null || pawn.Destroyed || pawn.Spawned == false),
					dangerRating = dangerAfterCleanup.ToString(),
					dangerMusicMode = manager.DangerMusicMode
				};
				cleanup.success = cleanup.observerPatchRemoved
					&& cleanup.noctolithRemoved
					&& cleanup.unnaturalDarknessRemoved
					&& cleanup.metalhorrorsRemoved
					&& cleanup.dangerRating == "None"
					&& cleanup.dangerMusicMode == false;
				result.cleanup = cleanup;
			}

			var loadoutContractPassed = ObjectSuccess(result.loadoutContract);
			var replacementContractPassed = ObjectSuccess(result.replacementContract);
			result.finalizationContract = AnomalyMusicFinalizationContractState();
			result.success = AnomalyMusicScenarioSuccessGate(
				result.error == null,
				result.supportedLoadout,
				loadoutContractPassed,
				replacementContractPassed,
				result.relax?.success == true,
				result.tension?.success == true,
				result.combat?.success == true,
				result.cleanup?.success == true,
				result.finalSettings?.success == true)
				&& ObjectSuccess(result.finalizationContract);
			if (result.success)
				result.stage = "complete";
			else if (result.error == null)
			{
				if (result.supportedLoadout == false || loadoutContractPassed == false)
					result.stage = "verify.loadout";
				else if (result.cleanup?.success != true)
					result.stage = "verify.cleanup";
				else if (result.finalSettings?.success != true)
					result.stage = "verify.final-settings";
				else
					result.stage = "verify.scenario";
				result.error = "One or more Anomaly music scenario postconditions failed.";
			}

			return result;
		}

		static bool PackageSetsEqual(IEnumerable<string> actualPackages, IEnumerable<string> expectedPackages)
			=> new HashSet<string>(actualPackages ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase)
				.SetEquals(expectedPackages ?? Enumerable.Empty<string>());

		static bool IsSupportedAnomalyMusicLoadout(string[] activePackages, string[] minimalPackages, string[] allOfficialDlcPackages)
			=> PackageSetsEqual(activePackages, minimalPackages) || PackageSetsEqual(activePackages, allOfficialDlcPackages);

		static object AnomalyMusicLoadoutContractState(string[] minimalPackages, string[] allOfficialDlcPackages)
		{
			var minimalAccepted = IsSupportedAnomalyMusicLoadout(minimalPackages, minimalPackages, allOfficialDlcPackages);
			var allOfficialDlcAccepted = IsSupportedAnomalyMusicLoadout(allOfficialDlcPackages, minimalPackages, allOfficialDlcPackages);
			var unexpectedModRejected = IsSupportedAnomalyMusicLoadout(
				allOfficialDlcPackages.Concat(new[] { "example.unexpected.mod" }).ToArray(),
				minimalPackages,
				allOfficialDlcPackages) == false;
			return new
			{
				success = minimalAccepted && allOfficialDlcAccepted && unexpectedModRejected,
				minimalAccepted,
				allOfficialDlcAccepted,
				unexpectedModRejected
			};
		}

		static bool AnomalyMusicScenarioSuccessGate(
			bool noError,
			bool supportedLoadout,
			bool loadoutContractPassed,
			bool replacementContractPassed,
			bool relaxPassed,
			bool tensionPassed,
			bool combatPassed,
			bool cleanupPassed,
			bool finalSettingsPassed)
		{
			return noError
				&& supportedLoadout
				&& loadoutContractPassed
				&& replacementContractPassed
				&& relaxPassed
				&& tensionPassed
				&& combatPassed
				&& cleanupPassed
				&& finalSettingsPassed;
		}

		static object AnomalyMusicFinalizationContractState()
		{
			var validScenarioAccepted = AnomalyMusicScenarioSuccessGate(true, true, true, true, true, true, true, true, true);
			var cleanupFailureRejected = AnomalyMusicScenarioSuccessGate(true, true, true, true, true, true, true, false, true) == false;
			var finalSettingsFailureRejected = AnomalyMusicScenarioSuccessGate(true, true, true, true, true, true, true, true, false) == false;
			var unsupportedLoadoutRejected = AnomalyMusicScenarioSuccessGate(true, false, true, true, true, true, true, true, true) == false;
			var scenarioFailureRejected = AnomalyMusicScenarioSuccessGate(true, true, true, true, true, false, true, true, true) == false;
			return new
			{
				success = validScenarioAccepted
					&& cleanupFailureRejected
					&& finalSettingsFailureRejected
					&& unsupportedLoadoutRejected
					&& scenarioFailureRejected,
				validScenarioAccepted,
				cleanupFailureRejected,
				finalSettingsFailureRejected,
				unsupportedLoadoutRejected,
				scenarioFailureRejected
			};
		}

		static AnomalyMusicStageResult RunAnomalyMusicStage(
			MusicManagerPlay manager,
			List<MusicTransition> transitions,
			string id,
			string transitionDefName,
			string predicateSource,
			object fixture,
			int seed)
		{
			var transition = transitions.FirstOrDefault(candidate => candidate.def?.defName == transitionDefName)
				?? throw new InvalidOperationException($"The {transitionDefName} music transition was not cached by RimWorld.");
			var satisfiedTransitions = transitions
				.Where(candidate => candidate.IsTransitionSatisfied())
				.OrderByDescending(candidate => candidate.def.priority)
				.ToArray();
			var highest = satisfiedTransitions.FirstOrDefault();
			var control = RunAnomalyMusicPlaybackPass(manager, transition, 0, seed);
			var replacement = RunAnomalyMusicPlaybackPass(manager, transition, 100, seed);
			var fullSettings = new SettingsGroup
			{
				playZombielandMusic = true,
				zombielandMusicShare = 100
			};
			var expectedReplacement = ZombielandMusic.ResolveSequenceReplacement(control.SelectedSong, fullSettings, 0f);
			var exactReplacement = control.SelectedSong != null
				&& expectedReplacement != null
				&& expectedReplacement != control.SelectedSong
				&& replacement.SelectedSong == expectedReplacement;
			var playCommandObserved = control.playCommand?.callCount == 1
				&& control.playCommand.SourceSong == control.SelectedSong
				&& control.playCommand.FinalSong == control.SelectedSong
				&& control.playCommand.CurrentSong == control.SelectedSong
				&& control.playCommand.finalArgumentMatchesCurrentSong
				&& control.playCommand.audioClipMatchesCurrentSong
				&& replacement.playCommand?.callCount == 1
				&& replacement.playCommand.SourceSong == control.SelectedSong
				&& replacement.playCommand.FinalSong == replacement.SelectedSong
				&& replacement.playCommand.CurrentSong == replacement.SelectedSong
				&& replacement.playCommand.finalArgumentMatchesCurrentSong
				&& replacement.playCommand.audioClipMatchesCurrentSong;

			return new AnomalyMusicStageResult
			{
				success = highest == transition
					&& control.predicateSatisfied
					&& control.expectedTransitionSelected
					&& control.isPlaying
					&& control.audioClipMatchesSong
					&& control.isZombielandSong == false
					&& replacement.predicateSatisfied
					&& replacement.expectedTransitionSelected
					&& replacement.isPlaying
					&& replacement.audioClipMatchesSong
					&& replacement.isZombielandSong
					&& exactReplacement
					&& playCommandObserved,
				id = id,
				transition = transitionDefName,
				predicateSource = predicateSource,
				fixture = fixture,
				satisfiedTransitions = satisfiedTransitions.Select(candidate => candidate.def.defName).ToArray(),
				highestSatisfiedTransition = highest?.def?.defName,
				control = control,
				replacement = replacement,
				wouldNormallyPlay = control.songDefName,
				replacementActuallyPlayed = replacement.songDefName,
				expectedReplacement = expectedReplacement?.defName,
				exactOneToOneReplacement = exactReplacement
			};
		}

		static AnomalyMusicPlaybackSample RunAnomalyMusicPlaybackPass(
			MusicManagerPlay manager,
			MusicTransition expectedTransition,
			int share,
			int seed)
		{
			ApplyAnomalyMusicSettings(share);
			manager.Stop();
			var predicateSatisfied = expectedTransition.IsTransitionSatisfied();
			currentAnomalyMusicPlayObservation = new AnomalyMusicPlayObservation();
			Rand.PushState(seed);
			try
			{
				manager.CheckTransitions();
			}
			finally
			{
				Rand.PopState();
			}

			var song = manager.CurrentSong;
			var audioSource = anomalyMusicAudioSourceField.GetValue(manager) as AudioSource;
			var sample = new AnomalyMusicPlaybackSample
			{
				share = share,
				predicateSatisfied = predicateSatisfied,
				expectedTransitionSelected = manager.TriggeredTransition == expectedTransition,
				triggeredTransition = manager.TriggeredTransition?.def?.defName,
				sequence = manager.MusicSequenceWorker?.def?.defName,
				managerState = manager.State.ToString(),
				isPlaying = manager.IsPlaying && audioSource?.isPlaying == true,
				songDefName = song?.defName,
				songLabel = song?.label,
				clipPath = song?.clipPath,
				audioClipName = audioSource?.clip?.name,
				audioClipMatchesSong = song?.clip != null && audioSource?.clip == song.clip,
				isZombielandSong = ZombielandMusic.IsZombielandSong(song),
				playCommand = currentAnomalyMusicPlayObservation
			};
			currentAnomalyMusicPlayObservation = null;
			sample.SetSelectedSong(song);
			return sample;
		}

		static void ObserveAnomalyMusicPlayPrefix(SongDef song)
		{
			var observation = currentAnomalyMusicPlayObservation;
			if (observation == null)
				return;
			observation.callCount++;
			if (observation.callCount != 1)
				return;
			observation.SetSourceSong(song);
			observation.sourceRequested = song?.defName;
			observation.sourceWasZombieland = ZombielandMusic.IsZombielandSong(song);
		}

		static void ObserveAnomalyMusicPlayPostfix(MusicManagerPlay __instance, SongDef song)
		{
			var observation = currentAnomalyMusicPlayObservation;
			if (observation == null || observation.callCount != 1)
				return;
			var currentSong = __instance.CurrentSong;
			var audioSource = anomalyMusicAudioSourceField?.GetValue(__instance) as AudioSource;
			observation.SetFinalSong(song);
			observation.SetCurrentSong(currentSong);
			observation.finalArgument = song?.defName;
			observation.currentSongAfterPlay = currentSong?.defName;
			observation.audioClipAfterPlay = audioSource?.clip?.name;
			observation.finalWasZombieland = ZombielandMusic.IsZombielandSong(song);
			observation.finalArgumentMatchesCurrentSong = song != null && song == currentSong;
			observation.audioClipMatchesCurrentSong = currentSong?.clip != null && audioSource?.clip == currentSong.clip;
		}

		static void ApplyAnomalyMusicSettings(int share)
		{
			ApplyZombieSettingsOverride(values =>
			{
				values.playZombielandMusic = true;
				values.zombielandMusicShare = share;
			});
		}

		static void InvalidateDangerWatcher(Map map)
		{
			if (map?.dangerWatcher == null || dangerWatcherLastUpdateTickField == null)
				return;
			dangerWatcherLastUpdateTickField.SetValue(map.dangerWatcher, GenTicks.TicksGame - 102);
		}
	}
}
