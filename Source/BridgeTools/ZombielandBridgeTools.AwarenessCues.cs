using HarmonyLib;
using RimBridgeServer.Sdk;
using RimWorld;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace ZombieLand
{
	public sealed partial class ZombielandBridgeTools
	{
		[Tool("zombieland/awareness_cues_contract", Description = "Verify the Awareness Cues settings suppress or allow Zombieland letters, sounds, ambient sustainers, and thought bubbles while preserving default-on behavior.")]
		public static object AwarenessCuesContract(
			[ToolParameter(Description = "Optional x cell near which runtime fixtures should be placed. Negative values use map center.", Required = false, DefaultValue = -1)] int x = -1,
			[ToolParameter(Description = "Optional z cell near which runtime fixtures should be placed. Negative values use map center.", Required = false, DefaultValue = -1)] int z = -1)
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

			var tickManager = map.GetComponent<TickManager>();
			if (tickManager == null)
			{
				return new
				{
					success = false,
					error = "No Zombieland TickManager is attached to the current map."
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

			var spawnEventProcess = typeof(ZombiesRising).GetMethod("SpawnEventProcess", BindingFlags.Static | BindingFlags.NonPublic);
			var electricSustainerField = AccessTools.Field(typeof(TickManager), "electricSustainer");
			var tankSustainerField = AccessTools.Field(typeof(TickManager), "tankSustainer");
			if (spawnEventProcess == null || electricSustainerField == null || tankSustainerField == null)
			{
				return new
				{
					success = false,
					error = "Could not reflect all awareness cue probe members.",
					reflection = new
					{
						spawnEventProcess = spawnEventProcess != null,
						electricSustainerField = electricSustainerField != null,
						tankSustainerField = tankSustainerField != null
					}
				};
			}

			var settingsSnapshot = SnapshotZombieSettings();
			var oldUseSound = Constants.USE_SOUND;
			var oldAmbientVolume = Prefs.VolumeAmbient;
			var oldSoundRecording = DebugViewSettings.writeSoundEventsRecord;
			var oldSoundRecordingPrevious = soundEventsPreviousWriteState;
			var spawnedThings = new List<Thing>();
			var spawnedThoughts = new List<Mote>();
			var existingLetters = SnapshotAwarenessCueLetters();
			var cases = new List<object>();

			try
			{
				Constants.USE_SOUND = true;
				Prefs.VolumeAmbient = Mathf.Max(Prefs.VolumeAmbient, 0.5f);
				DebugViewSettings.writeSoundEventsRecord = true;
				soundEventsPreviousWriteState = null;
				ClearDebugSoundEvents();

				cases.Add(VerifyAwarenessCueDefaults());
				cases.Add(VerifyZombieEventLetters(map, spawnEventProcess, spawnedThings));
				cases.Add(VerifyZombieEventSiren(map, spawnEventProcess, spawnedThings));
				cases.Add(VerifySpecialZombieAmbientSounds(map, tickManager, root, electricSustainerField, tankSustainerField, spawnedThings));
				cases.Add(VerifyZombieActionSounds(map, root, spawnedThings));
				cases.Add(VerifyWallAndSabotageSounds(map, root, tickManager, spawnedThings));
				cases.Add(VerifyZombieThoughtBubbles(map, root, spawnedThings, spawnedThoughts));
				cases.Add(VerifyDeadBecomesZombieMessage(map, root, spawnedThings));

				return new
				{
					success = cases.All(ObjectSuccess),
					root = ZombieRuntimeActions.DescribeCell(root),
					sourceCoverage = new[]
					{
						"ZombieIncidents: incident letters and ZombiesRising siren",
						"TickManager: electric and tank ambient sustainers",
						"Tools: PlayTink, PlayAbsorb, CastThoughtBubble, ConvertToZombie letters",
						"ZombieStateHandler: wall-push sound through the real Stumble job"
					},
					cases
				};
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
				Constants.USE_SOUND = oldUseSound;
				Prefs.VolumeAmbient = oldAmbientVolume;
				DebugViewSettings.writeSoundEventsRecord = oldSoundRecording;
				soundEventsPreviousWriteState = oldSoundRecordingPrevious;
				ClearDebugSoundEvents();

				foreach (var thought in spawnedThoughts.Distinct())
					if (thought != null && thought.Destroyed == false)
						thought.Destroy(DestroyMode.Vanish);

				foreach (var thing in spawnedThings.Distinct().ToArray())
					DestroyAwarenessCueFixtureThing(tickManager, thing);

				RemoveAwarenessCueFixtureLetters(existingLetters);
			}
		}

		[Tool("zombieland/awareness_cues_translation_state", Description = "Report whether the loaded RimWorld language database can translate every Awareness Cues label and help key.")]
		public static object AwarenessCuesTranslationState()
		{
			var rows = AwarenessCueTranslationKeys()
				.Select(key =>
				{
					var helpKey = key + "_Help";
					var labelCanTranslate = key.CanTranslate();
					var helpCanTranslate = helpKey.CanTranslate();
					return new AwarenessCueTranslationRow
					{
						key = key,
						labelCanTranslate = labelCanTranslate,
						labelTranslation = key.Translate().ToString(),
						helpKey = helpKey,
						helpCanTranslate = helpCanTranslate,
						helpTranslation = helpKey.Translate().ToString()
					};
				})
				.ToArray();

			return new
			{
				success = rows.All(row => row.labelCanTranslate && row.helpCanTranslate),
				activeLanguage = DescribeAwarenessCueActiveLanguage(),
				rows
			};
		}

		static object VerifyAwarenessCueDefaults()
		{
			var constructed = new SettingsGroup();
			var defaults = ZombieSettingsDefaults.group;
			var constructedDefaults = new
			{
				constructed.showZombieEventLetters,
				constructed.playZombieEventSiren,
				constructed.playSpecialZombieAmbientSounds,
				constructed.playZombieActionSounds,
				constructed.playWallAndSabotageSounds,
				constructed.showZombieThoughtBubbles,
				constructed.deadBecomesZombieMessage
			};
			var persistedDefaults = defaults == null ? null : new
			{
				defaults.showZombieEventLetters,
				defaults.playZombieEventSiren,
				defaults.playSpecialZombieAmbientSounds,
				defaults.playZombieActionSounds,
				defaults.playWallAndSabotageSounds,
				defaults.showZombieThoughtBubbles,
				defaults.deadBecomesZombieMessage
			};

			var constructedOk = constructed.showZombieEventLetters
				&& constructed.playZombieEventSiren
				&& constructed.playSpecialZombieAmbientSounds
				&& constructed.playZombieActionSounds
				&& constructed.playWallAndSabotageSounds
				&& constructed.showZombieThoughtBubbles
				&& constructed.deadBecomesZombieMessage;
			var persistedOk = defaults == null
				|| defaults.showZombieEventLetters
					&& defaults.playZombieEventSiren
					&& defaults.playSpecialZombieAmbientSounds
					&& defaults.playZombieActionSounds
					&& defaults.playWallAndSabotageSounds
					&& defaults.showZombieThoughtBubbles
					&& defaults.deadBecomesZombieMessage;

			return new
			{
				name = "awareness_cue_defaults",
				success = constructedOk && persistedOk,
				constructedDefaults,
				persistedDefaults
			};
		}

		static object VerifyZombieEventLetters(Map map, MethodInfo spawnEventProcess, List<Thing> spawnedThings)
		{
			object RunCase(string name, bool showLetters, bool expectLetter)
			{
				SetAllAwarenessCueSettingsToDefaultOn();
				ApplyZombieSettingsOverride(settings =>
				{
					settings.showZombieEventLetters = showLetters;
					settings.playZombieEventSiren = false;
					settings.spawnHowType = SpawnHowType.FromTheEdges;
				});

				var result = InvokeAwarenessCueIncident(map, spawnEventProcess, 4, true, spawnedThings);
				var letters = AwarenessCueProperty<object[]>(result, "letters") ?? Array.Empty<object>();
				var matching = AwarenessCueProperty<int>(result, "matchingLetterCount");
				var spawnedCount = AwarenessCueProperty<int>(result, "spawnedCount");
				var invocationSuccess = ObjectSuccess(result);
				var letterOk = expectLetter ? matching == 1 : letters.Length == 0 && matching == 0;

				return new
				{
					name,
					success = invocationSuccess && spawnedCount == 4 && letterOk,
					showLetters,
					expectLetter,
					invocationSuccess,
					spawnedCount,
					newLetterCount = letters.Length,
					matchingLetterCount = matching,
					incident = result
				};
			}

			var enabled = RunCase("event_letters_enabled", true, true);
			var disabled = RunCase("event_letters_disabled", false, false);
			return new
			{
				name = "showZombieEventLetters",
				success = ObjectSuccess(enabled) && ObjectSuccess(disabled),
				enabled,
				disabled
			};
		}

		static object VerifyZombieEventSiren(Map map, MethodInfo spawnEventProcess, List<Thing> spawnedThings)
		{
			var (capable, incapable) = Tools.ColonistsInfo(map);
			var incidentSize = Math.Max(4, capable * 4 + 1);

			object RunCase(string name, bool playSiren, bool expectSiren)
			{
				SetAllAwarenessCueSettingsToDefaultOn();
				ApplyZombieSettingsOverride(settings =>
				{
					settings.showZombieEventLetters = false;
					settings.playZombieEventSiren = playSiren;
					settings.spawnHowType = SpawnHowType.FromTheEdges;
				});

				var sound = CaptureAwarenessCueSoundCase(
					name,
					new[] { "ZombiesRising" },
					expectSiren,
					() => InvokeAwarenessCueIncident(map, spawnEventProcess, incidentSize, true, spawnedThings),
					out var incident);
				var spawnedCount = AwarenessCueProperty<int>(incident, "spawnedCount");
				return new
				{
					name,
					success = ObjectSuccess(sound) && ObjectSuccess(incident) && spawnedCount == incidentSize,
					playSiren,
					expectSiren,
					capableColonists = capable,
					incapableColonists = incapable,
					incidentSize,
					spawnedCount,
					sound,
					incident
				};
			}

			var enabled = RunCase("event_siren_enabled", true, true);
			var disabled = RunCase("event_siren_disabled", false, false);
			return new
			{
				name = "playZombieEventSiren",
				success = ObjectSuccess(enabled) && ObjectSuccess(disabled),
				enabled,
				disabled
			};
		}

		static object VerifySpecialZombieAmbientSounds(
			Map map,
			TickManager tickManager,
			IntVec3 root,
			FieldInfo electricSustainerField,
			FieldInfo tankSustainerField,
			List<Thing> spawnedThings)
		{
			if (TryFindClearSpawnCell(map, root + new IntVec3(-6, 0, -6), 20f, out var electricCell, out var electricError) == false)
				return electricError;
			if (TryFindClearSpawnCell(map, electricCell + new IntVec3(4, 0, 0), 16f, out var tankCell, out var tankError) == false)
				return tankError;

			var oldElectricSustainer = electricSustainerField.GetValue(tickManager) as Sustainer;
			var oldTankSustainer = tankSustainerField.GetValue(tickManager) as Sustainer;
			Sustainer createdElectricSustainer = null;
			Sustainer createdTankSustainer = null;

			var electrifier = ZombieRuntimeActions.SpawnZombie(electricCell, map, ZombieType.Electrifier, true);
			var tanky = ZombieRuntimeActions.SpawnZombie(tankCell, map, ZombieType.TankyOperator, true);
			if (electrifier != null)
				spawnedThings.Add(electrifier);
			if (tanky != null)
				spawnedThings.Add(tanky);
			if (electrifier == null || tanky == null)
			{
				return new
				{
					name = "playSpecialZombieAmbientSounds",
					success = false,
					electricCell = ZombieRuntimeActions.DescribeCell(electricCell),
					tankCell = ZombieRuntimeActions.DescribeCell(tankCell),
					error = "Could not create special-zombie ambient sound fixtures."
				};
			}

			try
			{
				_ = tickManager.hummingZombies.Add(electrifier);
				_ = tickManager.tankZombies.Add(tanky);
				electricSustainerField.SetValue(tickManager, null);
				tankSustainerField.SetValue(tickManager, null);

				SetAllAwarenessCueSettingsToDefaultOn();
				ApplyZombieSettingsOverride(settings => settings.playSpecialZombieAmbientSounds = true);

				var enableAttempts = 0;
				Sustainer electricSustainer = null;
				Sustainer tankSustainer = null;
				for (; enableAttempts < 5000; enableAttempts++)
				{
					tickManager.UpdateElectricalHumming();
					tickManager.UpdateTankMovement();
					electricSustainer = electricSustainerField.GetValue(tickManager) as Sustainer;
					tankSustainer = tankSustainerField.GetValue(tickManager) as Sustainer;
					if (electricSustainer != null && tankSustainer != null)
						break;
				}
				createdElectricSustainer = electricSustainer;
				createdTankSustainer = tankSustainer;

				ApplyZombieSettingsOverride(settings => settings.playSpecialZombieAmbientSounds = false);
				var disableAttempts = 0;
				for (; disableAttempts < 5000; disableAttempts++)
				{
					tickManager.UpdateElectricalHumming();
					tickManager.UpdateTankMovement();
					electricSustainer = electricSustainerField.GetValue(tickManager) as Sustainer;
					tankSustainer = tankSustainerField.GetValue(tickManager) as Sustainer;
					if (electricSustainer == null && tankSustainer == null)
						break;
				}

				var enabledCreated = enableAttempts < 5000 && createdElectricSustainer != null && createdTankSustainer != null;
				var disabledStopped = disableAttempts < 5000
					&& electricSustainerField.GetValue(tickManager) == null
					&& tankSustainerField.GetValue(tickManager) == null;
				return new
				{
					name = "playSpecialZombieAmbientSounds",
					success = enabledCreated && disabledStopped,
					enabledCreated,
					disabledStopped,
					enableAttempts = enableAttempts + 1,
					disableAttempts = disableAttempts + 1,
					fixtures = new
					{
						electrifier = DescribeZombie(electrifier),
						tanky = DescribeZombie(tanky)
					},
					trackingSets = new
					{
						hummingContainsElectrifier = tickManager.hummingZombies.Contains(electrifier),
						tankContainsTanky = tickManager.tankZombies.Contains(tanky),
						hummingCount = tickManager.hummingZombies.Count,
						tankCount = tickManager.tankZombies.Count
					}
				};
			}
			finally
			{
				if (createdElectricSustainer != null && ReferenceEquals(createdElectricSustainer, oldElectricSustainer) == false)
					createdElectricSustainer.End();
				if (createdTankSustainer != null && ReferenceEquals(createdTankSustainer, oldTankSustainer) == false)
					createdTankSustainer.End();
				electricSustainerField.SetValue(tickManager, oldElectricSustainer);
				tankSustainerField.SetValue(tickManager, oldTankSustainer);
			}
		}

		static object VerifyZombieActionSounds(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			if (TryFindClearSpawnCell(map, root + new IntVec3(6, 0, -6), 20f, out var cell, out var error) == false)
				return error;

			var zombie = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.Normal, true);
			if (zombie != null)
				spawnedThings.Add(zombie);
			if (zombie == null)
			{
				return new
				{
					name = "playZombieActionSounds",
					success = false,
					cell = ZombieRuntimeActions.DescribeCell(cell),
					error = "Could not create zombie-action sound fixture."
				};
			}

			object RunCase(string name, bool playActionSounds, bool expectSounds)
			{
				SetAllAwarenessCueSettingsToDefaultOn();
				ApplyZombieSettingsOverride(settings => settings.playZombieActionSounds = playActionSounds);
				return CaptureAwarenessCueSoundCase(
					name,
					new[] { "TankyTink", "Bzzt" },
					expectSounds,
					() =>
					{
						Tools.PlayTink(zombie);
						Tools.PlayAbsorb(zombie);
						return null;
					},
					out _);
			}

			var enabled = RunCase("zombie_action_sounds_enabled", true, true);
			var disabled = RunCase("zombie_action_sounds_disabled", false, false);
			return new
			{
				name = "playZombieActionSounds",
				success = ObjectSuccess(enabled) && ObjectSuccess(disabled),
				zombie = DescribeZombie(zombie),
				enabled,
				disabled
			};
		}

		static object VerifyWallAndSabotageSounds(Map map, IntVec3 root, TickManager tickManager, List<Thing> spawnedThings)
		{
			object RunCase(string name, IntVec3 caseRoot, bool playSounds, bool expectSound)
			{
				if (TryCreateWallPushFixture(map, caseRoot, 24f, out var fixture, out var fixtureError) == false)
					return fixtureError;
				spawnedThings.Add(fixture.zombie);
				spawnedThings.Add(fixture.wall);

				var grid = map.GetGrid();
				var gridSnapshot = SnapshotAwarenessCueGridCounts(map, fixture.zombieCell, 4f);
				ClearWallPushGridNeighborhood(map, fixture.zombieCell);
				var originalMinimum = ZombieSettings.Values.minimumZombiesForWallPushing;
				var originalDangerousMessage = ZombieSettings.Values.dangerousSituationMessage;
				var effectiveMinimum = Math.Max(1, originalMinimum);
				var primedGridCount = Math.Max(0, effectiveMinimum - 4);

				try
				{
					SetAllAwarenessCueSettingsToDefaultOn();
					ZombieSettings.Values.minimumZombiesForWallPushing = effectiveMinimum;
					ZombieSettings.Values.dangerousSituationMessage = false;
					ZombieSettings.Values.playWallAndSabotageSounds = playSounds;
					grid.ChangeZombieCount(fixture.zombieCell, primedGridCount);
					PrepareWallPushZombie(map, fixture.zombie, fixture.zombieCell);

					var sound = CaptureAwarenessCueSoundCase(
						name,
						new[] { "WallPushing" },
						expectSound,
						() =>
						{
							fixture.zombie.jobs.StartJob(JobMaker.MakeJob(CustomDefs.Stumble), JobCondition.InterruptForced, null, true, false, null, null);
							for (var tick = 0; tick < 8; tick++)
								AdvanceGameTicks(1);
							return null;
						},
						out _);

					return new
					{
						name,
						success = ObjectSuccess(sound) && fixture.zombie.wallPushProgress >= 0f,
						playSounds,
						expectSound,
						wallPushStarted = fixture.zombie.wallPushProgress >= 0f,
						primedGridCount,
						fixture = new
						{
							zombie = DescribeZombie(fixture.zombie),
							zombieCell = ZombieRuntimeActions.DescribeCell(fixture.zombieCell),
							wallCell = ZombieRuntimeActions.DescribeCell(fixture.wallCell),
							destinationCell = ZombieRuntimeActions.DescribeCell(fixture.destinationCell)
						},
						sound
					};
				}
				finally
				{
					ZombieSettings.Values.minimumZombiesForWallPushing = originalMinimum;
					ZombieSettings.Values.dangerousSituationMessage = originalDangerousMessage;
					DestroyAwarenessCueFixtureThing(tickManager, fixture.zombie);
					DestroyAwarenessCueFixtureThing(tickManager, fixture.wall);
					RestoreAwarenessCueGridCounts(map, gridSnapshot);
				}
			}

			var enabled = RunCase("wall_push_sound_enabled", root + new IntVec3(-8, 0, 6), true, true);
			var disabled = RunCase("wall_push_sound_disabled", root + new IntVec3(8, 0, 6), false, false);
			return new
			{
				name = "playWallAndSabotageSounds",
				success = ObjectSuccess(enabled) && ObjectSuccess(disabled),
				sourcePath = "ZombieStateHandler.CheckWallPushing -> CustomDefs.WallPushing.PlayOneShot",
				enabled,
				disabled
			};
		}

		static object VerifyZombieThoughtBubbles(Map map, IntVec3 root, List<Thing> spawnedThings, List<Mote> spawnedThoughts)
		{
			if (TryFindClearSpawnCell(map, root + new IntVec3(-6, 0, 8), 20f, out var cell, out var error) == false)
				return error;

			var zombie = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.Normal, true);
			if (zombie != null)
				spawnedThings.Add(zombie);
			if (zombie == null)
			{
				return new
				{
					name = "showZombieThoughtBubbles",
					success = false,
					cell = ZombieRuntimeActions.DescribeCell(cell),
					error = "Could not create zombie thought-bubble fixture."
				};
			}

			object RunCase(string name, bool showThoughts, bool expectThought)
			{
				SetAllAwarenessCueSettingsToDefaultOn();
				ApplyZombieSettingsOverride(settings => settings.showZombieThoughtBubbles = showThoughts);

				var realtimeMotes = RealTime.moteList?.allMotes ?? new List<Mote>();
				var before = realtimeMotes
					.Where(mote => mote.def == CustomDefs.ZombieThought)
					.ToHashSet();
				Tools.CastThoughtBubble(zombie, Constants.EATING);
				var newThoughts = realtimeMotes
					.OfType<MoteBubble>()
					.Where(mote => mote.def == CustomDefs.ZombieThought && before.Contains(mote) == false)
					.ToArray();
				spawnedThoughts.AddRange(newThoughts);
				var thought = newThoughts
					.OrderBy(mote => mote.Position.DistanceToSquared(zombie.Position))
					.FirstOrDefault();

				return new
				{
					name,
					success = expectThought
						? thought != null && thought.Spawned && thought.Map == map && thought.iconMat == Constants.EATING
						: newThoughts.Length == 0,
					showThoughts,
					expectThought,
					spawnedThoughtCount = newThoughts.Length,
					thoughtThingId = thought?.ThingID,
					thoughtSpawned = thought?.Spawned ?? false,
					iconMaterial = thought?.iconMat?.name,
					expectedMaterial = Constants.EATING.name
				};
			}

			var enabled = RunCase("thought_bubbles_enabled", true, true);
			var disabled = RunCase("thought_bubbles_disabled", false, false);
			return new
			{
				name = "showZombieThoughtBubbles",
				success = ObjectSuccess(enabled) && ObjectSuccess(disabled),
				zombie = DescribeZombie(zombie),
				enabled,
				disabled
			};
		}

		static object VerifyDeadBecomesZombieMessage(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			object RunCase(string name, IntVec3 caseRoot, bool showMessage, bool expectLetter)
			{
				if (TryFindClearSpawnCell(map, caseRoot, 24f, out var cell, out var cellError) == false)
					return cellError;

				SetAllAwarenessCueSettingsToDefaultOn();
				ApplyZombieSettingsOverride(settings => settings.deadBecomesZombieMessage = showMessage);

				var pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				pawn.Name = new NameSingle($"ZL_Awareness_{name}");
				GenSpawn.Spawn(pawn, cell, map, Rot4.South);
				spawnedThings.Add(pawn);

				var beforeIds = CurrentZombies(map)
					.Select(ZombieRuntimeActions.StableThingId)
					.ToHashSet(StringComparer.OrdinalIgnoreCase);
				var beforeLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
					.ToHashSet();

				Tools.ConvertToZombie(pawn, map, true);

				var newZombies = CurrentZombies(map)
					.OfType<Zombie>()
					.Where(zombie => beforeIds.Contains(ZombieRuntimeActions.StableThingId(zombie)) == false)
					.ToArray();
				spawnedThings.AddRange(newZombies);
				var newLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
					.Where(letter => beforeLetters.Contains(letter) == false)
					.ToArray();
				var matchingLetters = newLetters
					.Where(letter => letter?.def == CustomDefs.ColonistTurnedZombie)
					.Select(letter => new
					{
						label = letter.Label,
						defName = letter.def?.defName,
						letter.arrivalTick
					})
					.ToArray();

				return new
				{
					name,
					success = newZombies.Length == 1
						&& (expectLetter ? matchingLetters.Length == 1 : newLetters.Length == 0 && matchingLetters.Length == 0),
					showMessage,
					expectLetter,
					cell = ZombieRuntimeActions.DescribeCell(cell),
					spawnedZombieCount = newZombies.Length,
					zombies = newZombies.Select(DescribeZombie).ToArray(),
					newLetterCount = newLetters.Length,
					matchingLetterCount = matchingLetters.Length,
					letters = newLetters.Select(letter => new
					{
						label = letter.Label,
						defName = letter.def?.defName,
						letter.arrivalTick
					}).ToArray()
				};
			}

			var enabled = RunCase("colonist_conversion_letter_enabled", root + new IntVec3(-10, 0, -2), true, true);
			var disabled = RunCase("colonist_conversion_letter_disabled", root + new IntVec3(10, 0, -2), false, false);
			return new
			{
				name = "deadBecomesZombieMessage",
				success = ObjectSuccess(enabled) && ObjectSuccess(disabled),
				sourcePath = "Tools.ConvertToZombie -> ZombieSettings.Values.deadBecomesZombieMessage -> Find.LetterStack.ReceiveLetter",
				enabled,
				disabled
			};
		}

		static object InvokeAwarenessCueIncident(Map map, MethodInfo spawnEventProcess, int incidentSize, bool useAlert, List<Thing> spawnedThings)
		{
			var cellValidator = Tools.ZombieSpawnLocator(map, true);
			var spot = ZombiesRising.GetValidSpot(map, IntVec3.Invalid, cellValidator);
			if (spot.IsValid == false)
			{
				return new
				{
					success = false,
					incidentSize,
					error = "No valid event spawn spot was found.",
					diagnostics = DescribeSpawnCandidateDiagnostics(map, ZombieSettings.Values.spawnHowType)
				};
			}

			var beforeIds = CurrentZombies(map)
				.Select(ZombieRuntimeActions.StableThingId)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
			var beforeLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
				.ToHashSet();
			var iterator = spawnEventProcess.Invoke(null, new object[] { map, incidentSize, spot, cellValidator, true, useAlert, ZombieType.Normal }) as IEnumerator;
			if (iterator == null)
			{
				return new
				{
					success = false,
					incidentSize,
					spot = ZombieRuntimeActions.DescribeCell(spot),
					error = "SpawnEventProcess did not return an IEnumerator."
				};
			}

			var steps = 0;
			while (steps < 8192 && iterator.MoveNext())
				steps++;

			var spawned = CurrentZombies(map)
				.OfType<Zombie>()
				.Where(zombie => beforeIds.Contains(ZombieRuntimeActions.StableThingId(zombie)) == false)
				.ToArray();
			spawnedThings.AddRange(spawned);
			var zombieDescriptions = spawned.Take(12).Select(DescribeZombie).ToArray();
			var newLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
				.Where(letter => beforeLetters.Contains(letter) == false)
				.ToArray();
			var expectedLabel = ZombieSettings.Values.spawnHowType == SpawnHowType.AllOverTheMap
				? "LetterLabelZombiesRisingNearYourBase".Translate().ToString()
				: "LetterLabelZombiesRising".Translate().ToString();
			var matchingLetters = newLetters
				.Where(letter => letter?.def == LetterDefOf.ThreatSmall && letter.Label == expectedLabel)
				.Select(letter => new
				{
					label = letter.Label,
					defName = letter.def?.defName,
					letter.arrivalTick
				})
				.ToArray();

			var result = new
			{
				success = steps < 8192
					&& spawned.Length == incidentSize
					&& spawned.All(zombie => MatchesRequestedZombieType(zombie, ZombieType.Normal)),
				incidentSize,
				spot = ZombieRuntimeActions.DescribeCell(spot),
				steps,
				spawnedCount = spawned.Length,
				zombies = zombieDescriptions,
				newLetterCount = newLetters.Length,
				matchingLetterCount = matchingLetters.Length,
				expectedLabel,
				letters = newLetters.Select(letter => new
				{
					label = letter.Label,
					defName = letter.def?.defName,
					letter.arrivalTick
				}).ToArray()
			};

			var tickManager = map.GetComponent<TickManager>();
			foreach (var zombie in spawned)
				DestroyAwarenessCueFixtureThing(tickManager, zombie);
			return result;
		}

		static object CaptureAwarenessCueSoundCase(string name, string[] filters, bool expectMatches, Func<object> action, out object actionResult)
		{
			ClearDebugSoundEvents();
			actionResult = action?.Invoke();
			var events = ReadDebugSoundEventLines();
			var matches = filters
				.Select(filter => new
				{
					filter,
					count = events.Count(line => line.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0),
					lines = events
						.Where(line => line.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
						.Take(12)
						.ToArray()
				})
				.ToArray();
			var allMatched = matches.All(match => match.count > 0);
			var noneMatched = matches.All(match => match.count == 0);

			return new
			{
				name,
				success = expectMatches ? allMatched : noneMatched,
				expectMatches,
				allMatched,
				noneMatched,
				writeSoundEventsRecord = DebugViewSettings.writeSoundEventsRecord,
				filters,
				matches,
				eventCount = events.Length,
				events = events.Take(40).ToArray()
			};
		}

		static void SetAllAwarenessCueSettingsToDefaultOn()
		{
			ApplyZombieSettingsOverride(settings =>
			{
				settings.showZombieEventLetters = true;
				settings.playZombieEventSiren = true;
				settings.playSpecialZombieAmbientSounds = true;
				settings.playZombieActionSounds = true;
				settings.playWallAndSabotageSounds = true;
				settings.showZombieThoughtBubbles = true;
				settings.deadBecomesZombieMessage = true;
			});
		}

		static T AwarenessCueProperty<T>(object value, string propertyName)
		{
			if (value == null)
				return default;
			var property = value.GetType().GetProperty(propertyName);
			if (property == null)
				return default;
			var propertyValue = property.GetValue(value);
			if (propertyValue is T typed)
				return typed;
			return default;
		}

		static Dictionary<IntVec3, int> SnapshotAwarenessCueGridCounts(Map map, IntVec3 root, float radius)
		{
			var snapshot = new Dictionary<IntVec3, int>();
			var grid = map.GetGrid();
			foreach (var cell in GenRadial.RadialCellsAround(root, radius, true))
			{
				if (cell.InBounds(map) == false)
					continue;
				snapshot[cell] = grid.GetZombieCount(cell);
			}
			return snapshot;
		}

		static void RestoreAwarenessCueGridCounts(Map map, Dictionary<IntVec3, int> snapshot)
		{
			var grid = map.GetGrid();
			foreach (var entry in snapshot)
			{
				var current = grid.GetZombieCount(entry.Key);
				var delta = entry.Value - current;
				if (delta != 0)
					grid.ChangeZombieCount(entry.Key, delta);
			}
		}

		static string[] AwarenessCueTranslationKeys()
		{
			return new[]
			{
				"AwarenessCuesTitle",
				"ShowZombieEventLetters",
				"PlayZombieEventSiren",
				"PlaySpecialZombieAmbientSounds",
				"PlayZombieActionSounds",
				"PlayWallAndSabotageSounds",
				"ShowZombieThoughtBubbles"
			};
		}

		static object DescribeAwarenessCueActiveLanguage()
		{
			var activeLanguage = AccessTools.Field(typeof(LanguageDatabase), "activeLanguage")?.GetValue(null);
			if (activeLanguage == null)
				return null;

			var type = activeLanguage.GetType();
			string ReadString(string name)
			{
				return AccessTools.Property(type, name)?.GetValue(activeLanguage)?.ToString()
					?? AccessTools.Field(type, name)?.GetValue(activeLanguage)?.ToString();
			}

			return new
			{
				type = type.FullName,
				folderName = ReadString("folderName"),
				friendlyNameEnglish = ReadString("friendlyNameEnglish") ?? ReadString("FriendlyNameEnglish"),
				friendlyNameNative = ReadString("friendlyNameNative") ?? ReadString("FriendlyNameNative")
			};
		}

		sealed class AwarenessCueTranslationRow
		{
			public string key;
			public bool labelCanTranslate;
			public string labelTranslation;
			public string helpKey;
			public bool helpCanTranslate;
			public string helpTranslation;
		}

		static HashSet<Letter> SnapshotAwarenessCueLetters()
		{
			return (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
				.ToHashSet();
		}

		static void RemoveAwarenessCueFixtureLetters(HashSet<Letter> existingLetters)
		{
			var letterStack = Find.LetterStack;
			var letters = letterStack?.LettersListForReading;
			if (letterStack == null || letters == null)
				return;

			foreach (var letter in letters
				.Where(letter => letter != null && existingLetters.Contains(letter) == false && IsAwarenessCueFixtureLetter(letter))
				.ToArray())
			{
				letterStack.RemoveLetter(letter);
			}
		}

		static bool IsAwarenessCueFixtureLetter(Letter letter)
		{
			if (letter.def == CustomDefs.ColonistTurnedZombie || letter.def == CustomDefs.OtherTurnedZombie)
				return true;

			if (letter.def != LetterDefOf.ThreatSmall)
				return false;

			var label = letter.Label;
			return label == "LetterLabelZombiesRising".Translate().ToString()
				|| label == "LetterLabelZombiesRisingNearYourBase".Translate().ToString();
		}

		static void DestroyAwarenessCueFixtureThing(TickManager tickManager, Thing thing)
		{
			if (thing == null)
				return;

			if (thing is Zombie zombie)
			{
				_ = tickManager?.allZombiesCached?.Remove(zombie);
				_ = tickManager?.hummingZombies?.Remove(zombie);
				_ = tickManager?.tankZombies?.Remove(zombie);
			}

			if (thing.Destroyed == false)
				thing.Destroy(DestroyMode.Vanish);
		}
	}
}
