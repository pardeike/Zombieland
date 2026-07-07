using HarmonyLib;
using RimBridgeServer.Sdk;
using RimWorld;
using System;
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
		sealed class ThreatForecastSnapshot
		{
			public float currentThreat { get; set; }
			public float rangeMin { get; set; }
			public float rangeMax { get; set; }
			public string forecastLabel { get; set; }
			public object[] samples { get; set; }
			public object[] zombieFreeEvents { get; set; }
		}

		sealed class PathingRegionMetrics
		{
			public int regionCount { get; set; }
			public int indexCount { get; set; }
			public int validRegionCount { get; set; }
			public int rootCount { get; set; }
			public int childCount { get; set; }
			public bool indexMatchesRegionCount { get; set; }
			public bool fixtureRoomProper { get; set; }
			public bool hasValidWanderDestination { get; set; }
		}

		sealed class RaidWorkerTryExecuteProbe
		{
			public string caseName { get; set; }
			public string mapId { get; set; }
			public int incidentSize { get; set; }
			public object spot { get; set; }
			public bool useAlert { get; set; }
			public bool ignoreLimit { get; set; }
			public string zombieType { get; set; }
			public string observedSpawnHowType { get; set; }
			public bool forcedResult { get; set; }
		}

		sealed class RaidCadenceProposalSnapshot
		{
			public int sampleCount { get; set; }
			public int totalProposals { get; set; }
			public int raidEnemyCount { get; set; }
			public int threatBigCount { get; set; }
			public string[] incidentDefs { get; set; }
			public string error { get; set; }
		}

		sealed class RaidCadenceSnapshot
		{
			public string phase { get; set; }
			public int ticksGame { get; set; }
			public float defaultThreatPoints { get; set; }
			public bool raidCanFire { get; set; }
			public string raidCanFireError { get; set; }
			public int hostileTargetCount { get; set; }
			public int zombielandHostileTargetCount { get; set; }
			public string dangerRating { get; set; }
			public float zombieThreatLevel { get; set; }
			public bool zombieFreeActive { get; set; }
			public float wealthItems { get; set; }
			public float wealthBuildings { get; set; }
			public float wealthPawns { get; set; }
			public float wealthTotal { get; set; }
			public RaidCadenceProposalSnapshot proposalSample { get; set; }
			public object hostileTargets { get; set; }
			public object storyteller { get; set; }
			public object corpsesAndDrops { get; set; }
		}

		sealed class ForecastTooltipPreviewWindow : Window
		{
			public const string StableTitle = "Zombieland Forecast Tooltip Preview";

			readonly string forecastLabel;
			readonly float? previewDifficulty;
			readonly List<ZombieFreeEventWindow> previewWindows;

			public override Vector2 InitialSize => new(780f, 410f);

			public ForecastTooltipPreviewWindow(string forecastLabel, float? previewDifficulty = null, List<ZombieFreeEventWindow> previewWindows = null)
			{
				this.forecastLabel = forecastLabel;
				this.previewDifficulty = previewDifficulty;
				this.previewWindows = previewWindows;
				doCloseButton = true;
				doCloseX = true;
				forcePause = false;
				absorbInputAroundWindow = false;
				preventCameraMotion = false;
				closeOnClickedOutside = false;
				draggable = true;
				resizeable = false;
			}

			public override void DoWindowContents(Rect inRect)
			{
				Text.Font = GameFont.Small;
				Text.Anchor = TextAnchor.UpperLeft;
				GUI.color = Color.white;
				Widgets.Label(new Rect(0f, 0f, inRect.width, 28f), StableTitle + ": " + forecastLabel);
				var drawRect = new Rect(0f, 36f, Patches.GlobalControlsUtility_DoDate_Patch.ThreatForecastTooltipWidth, Patches.GlobalControlsUtility_DoDate_Patch.ThreatForecastTooltipHeight);
				ZombieWeather.GenerateTooltipDrawer(drawRect, previewDifficulty, previewWindows)();
				Text.Anchor = TextAnchor.UpperLeft;
				GUI.color = Color.white;
			}
		}

		static readonly List<RaidWorkerTryExecuteProbe> raidWorkerTryExecuteProbes = new();
		static string activeRaidWorkerTryExecuteCase;

		static bool MatchesRequestedZombieType(Zombie zombie, ZombieType type)
		{
			if (zombie == null)
				return false;

			return type switch
			{
				ZombieType.SuicideBomber => zombie.IsSuicideBomber,
				ZombieType.ToxicSplasher => zombie.isToxicSplasher,
				ZombieType.TankyOperator => zombie.IsTanky,
				ZombieType.Miner => zombie.isMiner,
				ZombieType.Electrifier => zombie.isElectrifier,
				ZombieType.Albino => zombie.isAlbino,
				ZombieType.DarkSlimer => zombie.isDarkSlimer,
				ZombieType.Healer => zombie.isHealer,
				ZombieType.Normal => zombie.IsSuicideBomber == false
					&& zombie.isToxicSplasher == false
					&& zombie.IsTanky == false
					&& zombie.isMiner == false
					&& zombie.isElectrifier == false
					&& zombie.isAlbino == false
					&& zombie.isDarkSlimer == false
					&& zombie.isHealer == false,
				_ => false,
			};
		}

		[Tool("zombieland/incident_threat_state", Description = "Set up or read a reusable incident/threat fixture, and run scenario-level incident wave, spawn mix, infection, forecast, spawn-mode, raid-cadence, and pathing-region checks.")]
		public static object IncidentThreatState(
			[ToolParameter(Description = "Create a reusable capable-colony incident fixture before reading state.", Required = false, DefaultValue = false)] bool setupFixture = false,
			[ToolParameter(Description = "Optional action to run before readback: read, scheduledWave, spawnMatrix, threatForecast, forecastUi, spawnModes, raidWorker, raidCadence, eventDelivery, zeroThreat, zombieFreeEvent, zombieFreeAmbientSound, zombieFreeOverlap, zombieFreeReview, zombieFreeSchedule, zombieFreeForecast, zombieFreeHover, pathingRegions, or all.", Required = false, DefaultValue = "read")] string actionMode = "read",
			[ToolParameter(Description = "Ticks to advance before reading final state; clamped to 0..5000.", Required = false, DefaultValue = 0)] int advanceTicks = 0,
			[ToolParameter(Description = "Difficulty percentage used by the zombieFreeForecast and zombieFreeHover actions. Clamped to 50..500.", Required = false, DefaultValue = 100)] int zombieFreePreviewDifficultyPercent = 100)
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

			object setup = null;
			if (setupFixture)
			{
				if (TrySetupIncidentThreatFixture(map, out setup, out var setupError) == false)
					return setupError;
			}

			var normalizedActionMode = (actionMode ?? "read").Trim().ToLowerInvariant();
			if (TryRunIncidentThreatAction(map, normalizedActionMode, zombieFreePreviewDifficultyPercent, out var action, out var actionError) == false)
			{
				return new
				{
					success = false,
					error = actionError,
					actionMode
				};
			}
			var actionSucceeded = normalizedActionMode == "read"
				|| (bool)(action?.GetType().GetProperty("success")?.GetValue(action) ?? false);

			var clampedAdvanceTicks = Mathf.Clamp(advanceTicks, 0, 5000);
			if (clampedAdvanceTicks > 0)
				AdvanceGameTicks(clampedAdvanceTicks);

			var includeState = normalizedActionMode != "zombiefreehover";
			var state = includeState ? DescribeIncidentThreatState(map) : null;
			var tickManagerPresent = map.GetComponent<TickManager>() != null;
			return new
			{
				success = (setupFixture == false || (bool)(setup?.GetType().GetProperty("success")?.GetValue(setup) ?? false))
					&& actionSucceeded
					&& tickManagerPresent,
				setupFixture,
				setup,
				actionMode = normalizedActionMode,
				actionSucceeded,
				action,
				advancedTicks = clampedAdvanceTicks,
				stateIncluded = includeState,
				state
			};
		}

		static bool TryRunIncidentThreatAction(Map map, string actionMode, int zombieFreePreviewDifficultyPercent, out object result, out string error)
		{
			result = null;
			error = null;
			switch (actionMode)
			{
				case "read":
					return true;
				case "scheduledwave":
					result = RunScheduledIncidentWave(map);
					return true;
				case "spawnmatrix":
					result = RunIncidentSpawnMatrix(map);
					return true;
				case "threatforecast":
					result = RunThreatForecastContract(map);
					return true;
				case "forecastui":
					result = RunThreatForecastUiContract(map, true);
					return true;
				case "spawnmodes":
					result = RunSpawnModeContracts(map);
					return true;
				case "raidworker":
					result = RunRaidWorkerContract(map);
					return true;
				case "raidcadence":
					result = RunRaidCadenceContract(map);
					return true;
				case "eventdelivery":
					result = RunStoryEventDeliveryContract(map);
					return true;
				case "zerothreat":
					result = RunZeroThreatDeathContract(map);
					return true;
				case "zombiefreeevent":
					result = RunZombieFreeEventContract(map);
					return true;
				case "zombiefreeambientsound":
					result = RunZombieFreeAmbientSoundContract(map);
					return true;
				case "zombiefreeoverlap":
					result = RunZombieFreeOverlapContract(map);
					return true;
				case "zombiefreereview":
					result = RunZombieFreeReviewFixContract(map);
					return true;
				case "zombiefreeschedule":
					result = RunZombieFreeSchedulePreview();
					return true;
				case "zombiefreeforecast":
					result = RunZombieFreeForecastPreview(zombieFreePreviewDifficultyPercent);
					return true;
				case "zombiefreehover":
					result = RunZombieFreeHoverSetup(map, zombieFreePreviewDifficultyPercent);
					return true;
				case "pathingregions":
					result = RunPathingRegionsContract(map);
					return true;
				case "all":
					result = RunIncidentThreatAll(map);
					return true;
				default:
					error = "actionMode must be one of: read, scheduledWave, spawnMatrix, threatForecast, forecastUi, spawnModes, raidWorker, raidCadence, eventDelivery, zeroThreat, zombieFreeEvent, zombieFreeAmbientSound, zombieFreeOverlap, zombieFreeReview, zombieFreeSchedule, zombieFreeForecast, zombieFreeHover, pathingRegions, all.";
					return false;
			}
		}

		static object RunPathingRegionsContract(Map map)
		{
			var patchTargets = PatchedMethodsForPatchClass("RegionAndRoomUpdater_CreateOrUpdateRooms_Patch");
			var tickManager = map.GetComponent<TickManager>();
			var pathing = tickManager?.zombiePathing;
			if (pathing == null)
			{
				return new
				{
					success = false,
					patchTargets,
					error = "Current map has no Zombieland TickManager zombiePathing component."
				};
			}

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryBuildFogRoomFixture(map, root, 32f, out var fixture, out var fixtureError) == false)
				return fixtureError;

			var afterFixture = DescribePathingRegions(map, pathing, fixture, out var afterFixtureMetrics);
			pathing.backpointingRegionsIndices = new Dictionary<Region, int>();
			pathing.backpointingRegions = new List<BackpointingRegion>();
			var afterClear = DescribePathingRegions(map, pathing, fixture, out var afterClearMetrics);

			map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
			var afterRebuild = DescribePathingRegions(map, pathing, fixture, out var afterRebuildMetrics);

			return new
			{
				success = patchTargets.Length > 0
					&& afterFixtureMetrics.regionCount > 0
					&& afterClearMetrics.regionCount == 0
					&& afterRebuildMetrics.regionCount > 0
					&& afterRebuildMetrics.indexMatchesRegionCount
					&& afterRebuildMetrics.rootCount > 0
					&& afterRebuildMetrics.childCount > 0
					&& afterRebuildMetrics.fixtureRoomProper
					&& afterRebuildMetrics.hasValidWanderDestination,
				patchTargets,
				fixture = new
				{
					doorCell = ZombieRuntimeActions.DescribeCell(fixture.doorCell),
					targetWallCell = ZombieRuntimeActions.DescribeCell(fixture.targetWallCell),
					interiorCenter = ZombieRuntimeActions.DescribeCell(fixture.interiorRect.CenterCell),
					interiorCellCount = fixture.interiorRect.Area
				},
				afterFixture,
				afterClear,
				afterRebuild,
				note = "The verifier clears zombiePathing caches, calls the real RegionAndRoomUpdater.RebuildAllRegionsAndRooms path, and expects the installed CreateOrUpdateRooms postfix to repopulate the smart-wander region graph."
			};
		}

		static object DescribePathingRegions(Map map, ZombiePathing pathing, FogRoomFixture fixture, out PathingRegionMetrics metrics)
		{
			var regions = pathing?.backpointingRegions ?? new List<BackpointingRegion>();
			var validRegionCount = regions.Count(region => region.region != null && region.region.valid);
			var rootCount = regions.Count(region => region.parentIdx == -1);
			var childCount = regions.Count(region => region.parentIdx >= 0);
			var room = fixture.interiorRect.CenterCell.GetRoom(map);
			var samples = regions
				.Select((entry, index) => new { entry, index })
				.Where(row => row.entry.parentIdx >= 0 && row.entry.cell.IsValid)
				.Take(6)
				.Select(row =>
				{
					var destination = pathing.GetWanderDestination(row.entry.cell);
					return new
					{
						index = row.index,
						parentIdx = row.entry.parentIdx,
						cell = ZombieRuntimeActions.DescribeCell(row.entry.cell),
						destination = destination.IsValid ? ZombieRuntimeActions.DescribeCell(destination) : null,
						destinationValid = destination.IsValid,
						regionValid = row.entry.region != null && row.entry.region.valid,
						regionType = row.entry.region?.type.ToString(),
						door = row.entry.region?.door?.ThingID
					};
				})
				.ToArray();

			metrics = new PathingRegionMetrics
			{
				regionCount = regions.Count,
				indexCount = pathing?.backpointingRegionsIndices?.Count ?? 0,
				validRegionCount = validRegionCount,
				rootCount = rootCount,
				childCount = childCount,
				indexMatchesRegionCount = (pathing?.backpointingRegionsIndices?.Count ?? 0) == regions.Count,
				fixtureRoomProper = room?.ProperRoom == true,
				hasValidWanderDestination = samples.Any(sample => sample.destinationValid)
			};

			return new
			{
				metrics,
				fixtureRoom = room == null ? null : new
				{
					cellCount = room.CellCount,
					properRoom = room.ProperRoom,
					isDoorway = room.IsDoorway,
					isHuge = room.IsHuge,
					fogged = room.Fogged
				},
				samples
			};
		}

		static bool TrySetupIncidentThreatFixture(Map map, out object setup, out object error)
		{
			setup = null;
			error = null;
			var destroyedZombies = ZombieRuntimeActions.DestroyZombies(map);
			if (TryEnsureCapableIncidentColonists(map, 3, out var colonists, out error) == false)
				return false;
			var allOverMapSpawnField = PrepareAllOverMapIncidentSpawnField(map);
			var allOverMapSpawnFieldSucceeded = (bool)(allOverMapSpawnField.GetType().GetProperty("success")?.GetValue(allOverMapSpawnField) ?? false);

			var tickManager = map.GetComponent<TickManager>();
			setup = new
			{
				success = tickManager != null && colonists.Length >= 3 && allOverMapSpawnFieldSucceeded,
				destroyedZombies,
				allOverMapSpawnField,
				colonists = colonists.Select(DescribePawn).ToArray(),
				incidentState = DescribeIncidentThreatState(map),
				note = "Save this as ZL_Incident_Threat_base.rws before running action modes if a durable fixture is needed."
			};
			return true;
		}

		static object PrepareAllOverMapIncidentSpawnField(Map map)
		{
			var soil = TerrainDefOf.Soil ?? DefDatabase<TerrainDef>.GetNamed("Soil", false);
			if (soil == null)
			{
				return new
				{
					success = false,
					error = "TerrainDef Soil was not found."
				};
			}

			var center = new IntVec3(map.Size.x / 2, 0, Mathf.Min(map.Size.z - 24, map.Size.z / 2 + 24));
			var radius = Math.Max(Constants.SPAWN_INCIDENT_RADIUS + 6, 24);
			var changed = 0;
			var skippedEdifice = 0;
			for (var x = center.x - radius; x <= center.x + radius; x++)
			{
				for (var z = center.z - radius; z <= center.z + radius; z++)
				{
					var cell = new IntVec3(x, 0, z);
					if (cell.InBounds(map) == false)
						continue;
					if (cell.GetEdifice(map) != null)
					{
						skippedEdifice++;
						continue;
					}
					if (map.terrainGrid.TerrainAt(cell) != soil)
					{
						map.terrainGrid.SetTerrain(cell, soil);
						changed++;
					}
				}
			}

			map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
			Tools.nextPlayerReachableRegionsUpdate = 0;
			var diagnostics = DescribeSpawnCandidateDiagnostics(map, SpawnHowType.AllOverTheMap);
			var validSpotCount = (int)(diagnostics.GetType().GetProperty("validSpotCount")?.GetValue(diagnostics) ?? 0);
			return new
			{
				success = validSpotCount > 0,
				center = ZombieRuntimeActions.DescribeCell(center),
				radius,
				changed,
				skippedEdifice,
				diagnostics
			};
		}

		static bool TryEnsureCapableIncidentColonists(Map map, int minimumCapable, out Pawn[] colonists, out object error)
		{
			error = null;
			var created = new List<Pawn>();
			var existing = map.mapPawns.FreeHumanlikesSpawnedOfFaction(Faction.OfPlayer).ToList();
			foreach (var pawn in existing)
				PrepareIncidentColonist(pawn);

			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			while (Tools.ColonistsInfo(map).Item1 < minimumCapable && created.Count < 8)
			{
				if (TryFindClearSpawnCell(map, root + new IntVec3(created.Count * 2, 0, 0), 32f, out var cell, out error) == false)
				{
					colonists = existing.Concat(created).ToArray();
					return false;
				}

				var pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(pawn, cell, map, Rot4.South);
				PrepareIncidentColonist(pawn);
				created.Add(pawn);
			}

			colonists = map.mapPawns.FreeHumanlikesSpawnedOfFaction(Faction.OfPlayer).ToArray();
			if (Tools.ColonistsInfo(map).Item1 >= minimumCapable)
				return true;

			error = new
			{
				success = false,
				error = "Could not prepare enough capable incident colonists.",
				requestedCapable = minimumCapable,
				colonists = colonists.Select(DescribePawn).ToArray(),
				colonistInfo = DescribeColonistInfo(map)
			};
			return false;
		}

		static void PrepareIncidentColonist(Pawn pawn)
		{
			if (pawn == null)
				return;

			DisablePawnWork(pawn);
			pawn.drafter.Drafted = false;
			pawn.equipment?.DestroyAllEquipment(DestroyMode.Vanish);
			var weaponDef = DefDatabase<ThingDef>.GetNamed("Gun_BoltActionRifle", false)
				?? DefDatabase<ThingDef>.GetNamed("Gun_Pistol", false);
			if (weaponDef != null && pawn.equipment != null)
			{
				var weapon = ThingMaker.MakeThing(weaponDef) as ThingWithComps;
				if (weapon != null)
					pawn.equipment.AddEquipment(weapon);
			}
		}

		static object RunIncidentThreatAll(Map map)
		{
			var spawnMatrix = RunIncidentSpawnMatrix(map);
			var threatForecast = RunThreatForecastContract(map);
			var forecastUi = RunThreatForecastUiContract(map, false);
			var spawnModes = RunSpawnModeContracts(map);
			var raidWorker = RunRaidWorkerContract(map);
			var raidCadence = RunRaidCadenceContract(map);
			var eventDelivery = RunStoryEventDeliveryContract(map);
			var zeroThreat = RunZeroThreatDeathContract(map);
			var scheduledWave = RunScheduledIncidentWave(map);
			var spawnSuccess = (bool)(spawnMatrix?.GetType().GetProperty("success")?.GetValue(spawnMatrix) ?? false);
			var forecastSuccess = (bool)(threatForecast?.GetType().GetProperty("success")?.GetValue(threatForecast) ?? false);
			var forecastUiSuccess = (bool)(forecastUi?.GetType().GetProperty("success")?.GetValue(forecastUi) ?? false);
			var spawnModesSuccess = (bool)(spawnModes?.GetType().GetProperty("success")?.GetValue(spawnModes) ?? false);
			var raidWorkerSuccess = (bool)(raidWorker?.GetType().GetProperty("success")?.GetValue(raidWorker) ?? false);
			var raidCadenceSuccess = (bool)(raidCadence?.GetType().GetProperty("success")?.GetValue(raidCadence) ?? false);
			var eventDeliverySuccess = (bool)(eventDelivery?.GetType().GetProperty("success")?.GetValue(eventDelivery) ?? false);
			var zeroThreatSuccess = (bool)(zeroThreat?.GetType().GetProperty("success")?.GetValue(zeroThreat) ?? false);
			var scheduledSuccess = (bool)(scheduledWave?.GetType().GetProperty("success")?.GetValue(scheduledWave) ?? false);
			return new
			{
				success = scheduledSuccess && spawnSuccess && forecastSuccess && forecastUiSuccess && spawnModesSuccess && raidWorkerSuccess && raidCadenceSuccess && eventDeliverySuccess && zeroThreatSuccess,
				spawnMatrix,
				threatForecast,
				forecastUi,
				spawnModes,
				raidWorker,
				raidCadence,
				eventDelivery,
				zeroThreat,
				scheduledWave
			};
		}

		static object RunScheduledIncidentWave(Map map)
		{
			var tickManager = map.GetComponent<TickManager>();
			if (tickManager == null)
			{
				return new
				{
					success = false,
					error = "No Zombieland TickManager is attached to the current map."
				};
			}

			if (TryEnsureCapableIncidentColonists(map, 3, out var colonists, out var colonistError) == false)
				return colonistError;

			var spawnEventProcess = typeof(ZombiesRising).GetMethod("SpawnEventProcess", BindingFlags.Static | BindingFlags.NonPublic);
			var lastIncidentField = typeof(IncidentInfo).GetField("lastIncident", BindingFlags.Instance | BindingFlags.NonPublic);
			if (spawnEventProcess == null || lastIncidentField == null)
			{
				return new
				{
					success = false,
					error = "Could not find SpawnEventProcess or IncidentInfo.lastIncident by reflection.",
					spawnEventProcessFound = spawnEventProcess != null,
					lastIncidentFieldFound = lastIncidentField != null
				};
			}

			var settingsSnapshot = SnapshotZombieSettings();
			var originalInfo = tickManager.incidentInfo;
			var oldSpawnHowType = ZombieSettings.Values.spawnHowType;
			var initialIds = CurrentZombies(map)
				.Select(ZombieRuntimeActions.StableThingId)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
			var beforeLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
				.ToHashSet();

			try
			{
				ApplyZombieSettingsOverride(settings =>
				{
					settings.spawnWhenType = SpawnWhenType.AllTheTime;
					settings.spawnHowType = SpawnHowType.FromTheEdges;
					settings.useDynamicThreatLevel = false;
					settings.daysBeforeZombiesCome = 0;
					settings.maximumNumberOfZombies = 500;
					settings.baseNumberOfZombiesinEvent = 4;
					settings.colonyMultiplier = 1f;
					settings.extraDaysBetweenEvents = 0;
					settings.threatScale = Math.Max(settings.threatScale, 1f);
				});

				var info = new IncidentInfo
				{
					parameters = new IncidentParameters
					{
						daysStretched = -10f,
						scaleFactor = 1f
					}
				};
				lastIncidentField.SetValue(info, -GenDate.TicksPerDay * 100);
				tickManager.incidentInfo = info;

				Rand.PushState(8101);
				var scheduled = false;
				try
				{
					scheduled = ZombiesRising.ZombiesForNewIncident(tickManager);
				}
				finally
				{
					Rand.PopState();
				}

				var parameters = tickManager.incidentInfo.parameters;
				var scheduledLastIncident = (int)lastIncidentField.GetValue(tickManager.incidentInfo);
				var cellValidator = Tools.ZombieSpawnLocator(map, true);
				var spot = ZombiesRising.GetValidSpot(map, IntVec3.Invalid, cellValidator);
				var iterator = scheduled && spot.IsValid
					? spawnEventProcess.Invoke(null, new object[] { map, parameters.incidentSize, spot, cellValidator, true, false, ZombieType.Random }) as System.Collections.IEnumerator
					: null;
				var steps = 0;
				if (iterator != null)
				{
					while (steps < 8192 && iterator.MoveNext())
						steps++;
				}

				var newZombies = CurrentZombies(map)
					.OfType<Zombie>()
					.Where(zombie => initialIds.Contains(ZombieRuntimeActions.StableThingId(zombie)) == false)
					.ToArray();
				var newLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
					.Where(letter => beforeLetters.Contains(letter) == false)
					.ToArray();
				var expectedLabel = "LetterLabelZombiesRising".Translate().ToString();
				var matchingThreatLetters = newLetters
					.Where(letter => letter?.def == LetterDefOf.ThreatSmall && letter.Label == expectedLabel)
					.ToArray();
				var maxAfter = tickManager.GetMaxZombieCount();
				var canHaveMoreAfter = tickManager.CanHaveMoreZombies();

				return new
				{
					success = scheduled
						&& parameters.skipReason == "-"
						&& parameters.incidentSize > 0
						&& spot.IsValid
						&& iterator != null
						&& steps < 8192
						&& newZombies.Length > 0
						&& newZombies.Length <= parameters.incidentSize
						&& tickManager.ZombieCount() <= maxAfter
						&& matchingThreatLetters.Length == 1,
					sourcePath = "ZombiesRising.ZombiesForNewIncident scheduler result executed through the same SpawnEventProcess wave path",
					scheduled,
					spot = spot.IsValid ? ZombieRuntimeActions.DescribeCell(spot) : null,
					steps,
					parameters = DescribeIncidentParameters(parameters),
					scheduledLastIncident,
					currentTicks = GenTicks.TicksAbs,
					colonists = colonists.Select(DescribePawn).ToArray(),
					newZombieCount = newZombies.Length,
					zombieCountAfter = tickManager.ZombieCount(),
					maxZombieCountAfter = maxAfter,
					canHaveMoreAfter,
					zombies = newZombies.Select(DescribeZombie).ToArray(),
					letters = newLetters.Select(DescribeLetter).ToArray(),
					matchingThreatLetterCount = matchingThreatLetters.Length
				};
			}
			finally
			{
				ZombieSettings.Values.spawnHowType = oldSpawnHowType;
				RestoreZombieSettings(settingsSnapshot);
				tickManager.incidentInfo = originalInfo;
			}
		}

		static object RunIncidentSpawnMatrix(Map map)
		{
			var alertWave = IncidentAlertWaveContract();
			var specialTypes = IncidentSpecialTypeSpawnContract();
			var infectedHooks = InfectedIncidentHooksContract();
			var zombieFaction = ZombieFactionPawnGenerationContract();
			var alertSuccess = (bool)(alertWave?.GetType().GetProperty("success")?.GetValue(alertWave) ?? false);
			var specialSuccess = (bool)(specialTypes?.GetType().GetProperty("success")?.GetValue(specialTypes) ?? false);
			var infectedSuccess = (bool)(infectedHooks?.GetType().GetProperty("success")?.GetValue(infectedHooks) ?? false);
			var factionSuccess = (bool)(zombieFaction?.GetType().GetProperty("success")?.GetValue(zombieFaction) ?? false);
			return new
			{
				success = alertSuccess && specialSuccess && infectedSuccess && factionSuccess,
				alertWave,
				specialTypes,
				infectedHooks,
				zombieFaction
			};
		}

		static object RunThreatForecastContract(Map map)
		{
			var weather = map.GetComponent<ZombieWeather>();
			if (weather == null)
			{
				return new
				{
					success = false,
					error = "No ZombieWeather component is attached to the current map."
				};
			}

			var settingsSnapshot = SnapshotZombieSettings();
			try
			{
				ApplyZombieSettingsOverride(settings =>
				{
					settings.useDynamicThreatLevel = true;
					settings.daysBeforeZombiesCome = 0;
					settings.threatScale = Math.Max(settings.threatScale, 1f);
					settings.zombiesDieOnZeroThreat = true;
					settings.zombieFreeEvents = true;
				});

				var dynamic = DescribeThreatForecast(map);
				ApplyZombieSettingsOverride(settings => settings.useDynamicThreatLevel = false);
				var disabledThreat = ZombieWeather.GetThreatLevel(map);
				var disabled = DescribeThreatForecast(map);
				return new
				{
					success = dynamic.currentThreat >= 0f
						&& dynamic.currentThreat <= 1f
						&& dynamic.rangeMin >= 0f
						&& dynamic.rangeMax <= 1f
						&& dynamic.forecastLabel.Contains("ThreatLevel".Translate().ToString())
						&& disabledThreat == 1f,
					dynamic,
					disabled,
					disabledThreat,
					zombiesDieOnZeroThreat = ZombieSettings.Values.zombiesDieOnZeroThreat,
					sourcePath = "GlobalControlsUtility.DoDate forecast label uses ZombieWeather.GetFactorRangeFor; tooltip drawer uses ZombieWeather.GenerateTooltipDrawer"
				};
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
			}
		}

		static object RunZombieFreeEventContract(Map map)
		{
			var manager = ZombieFreeEventManager.Current;
			var weather = map.GetComponent<ZombieWeather>();
			var tickManager = map.GetComponent<TickManager>();
			if (manager == null || weather == null || tickManager == null)
			{
				return new
				{
					success = false,
					error = "The current game has no ZombieFreeEventManager, ZombieWeather, or Zombieland TickManager.",
					managerPresent = manager != null,
					weatherPresent = weather != null,
					tickManagerPresent = tickManager != null
				};
			}

			var settingsSnapshot = SnapshotZombieSettings();
			if (TrySnapshotZombieFreeSchedule(manager, out var scheduleSnapshot, out var scheduleSnapshotError) == false)
			{
				return new
				{
					success = false,
					error = scheduleSnapshotError
				};
			}
			var existingSpitters = CurrentZombies(map)
				.OfType<ZombieSpitter>()
				.Select(ZombieRuntimeActions.StableThingId)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
			ZombieSpitter spitter = null;
			try
			{
				ApplyZombieSettingsOverride(settings =>
				{
					settings.useDynamicThreatLevel = false;
					settings.daysBeforeZombiesCome = 0;
					settings.threatScale = Math.Max(settings.threatScale, 1f);
					settings.spitterThreat = Math.Max(settings.spitterThreat, 1f);
					settings.zombiesDieOnZeroThreat = false;
					settings.zombieFreeEvents = true;
				});

				if (TryFindClearSpawnCell(map, new IntVec3(map.Size.x / 2, 0, map.Size.z / 2), 28f, out var spitterCell, out var spitterCellError) == false)
					return spitterCellError;

				ZombieSpitter.Spawn(map, spitterCell);
				spitter = CurrentZombies(map)
					.OfType<ZombieSpitter>()
					.FirstOrDefault(candidate => existingSpitters.Contains(ZombieRuntimeActions.StableThingId(candidate)) == false);

				var forcedWindow = manager.DebugForceWindowStartingNow(GenDate.TicksPerDay * 2);
				var activeCondition = Find.World?.gameConditionManager?.GetActiveCondition(CustomDefs.ZombieFreeEvent);
				var conditionTooltip = activeCondition?.TooltipString ?? "";
				var threatWithDynamicDisabled = ZombieWeather.GetThreatLevel(map);
				var baseThreatWithDynamicDisabled = ZombieWeather.GetThreatLevelIgnoringZombieFreeEvent(map);
				var forecast = DescribeThreatForecast(map);
				var windowReadback = manager.WindowsForGameRange(GenTicks.TicksGame, GenTicks.TicksGame + GenDate.TicksPerDay * 3)
					.Select(window => new
					{
						offsetStartTicks = window.startTick - GenTicks.TicksGame,
						offsetEndTicks = window.endTick - GenTicks.TicksGame,
						window.DurationTicks,
						window.startHandled,
						window.letterSent
					})
					.ToArray();
				var condition = activeCondition == null
					? null
					: new
					{
						type = activeCondition.GetType().FullName,
						activeCondition.TicksLeft,
						tooltipHasTimeLeft = conditionTooltip.Contains("ZombieFreeEventTimeLeft".Translate().ToString()),
						tooltipHasLasted = conditionTooltip.Contains("Lasted".Translate().ToString()),
						tooltip = conditionTooltip
					};

				return new
				{
					success = ZombieFreeEventManager.IsActiveNow()
						&& threatWithDynamicDisabled == 0f
						&& baseThreatWithDynamicDisabled == 1f
						&& spitter?.state == SpitterState.Leaving
						&& forecast.zombieFreeEvents.Length > 0
						&& activeCondition is GameCondition_ZombieFreeEvent
						&& conditionTooltip.Contains("ZombieFreeEventTimeLeft".Translate().ToString())
						&& conditionTooltip.Contains("Lasted".Translate().ToString()) == false,
					activeNow = ZombieFreeEventManager.IsActiveNow(),
					forcedWindow = new
					{
						offsetStartTicks = forcedWindow.startTick - GenTicks.TicksGame,
						offsetEndTicks = forcedWindow.endTick - GenTicks.TicksGame,
						forcedWindow.DurationTicks
					},
					threatWithDynamicDisabled,
					baseThreatWithDynamicDisabled,
					canHaveMoreZombies = tickManager.CanHaveMoreZombies(),
					condition,
					spitter = spitter == null
						? null
						: new
						{
							id = ZombieRuntimeActions.StableThingId(spitter),
							spitter.state,
							spitter.Spawned,
							spitter.Destroyed
						},
					windowReadback,
					forecast,
					sourcePath = "ZombieFreeEventManager.DebugForceWindowStartingNow -> ZombieWeather.GetThreatLevel -> ZombieSpitter.StartLeavingMap"
				};
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
				RestoreZombieFreeSchedule(manager, scheduleSnapshot);
			}
		}

			static object RunZombieFreeAmbientSoundContract(Map map)
			{
				var manager = ZombieFreeEventManager.Current;
				var tickManager = map.GetComponent<TickManager>();
			var ambientSustainerField = tickManager == null ? null : AccessTools.Field(typeof(TickManager), "zombiesAmbientSound");
			var ambientVolumeField = tickManager == null ? null : AccessTools.Field(typeof(TickManager), "zombiesAmbientSoundVolume");
			if (manager == null || tickManager == null || ambientSustainerField == null || ambientVolumeField == null || CustomDefs.ZombiesClosingIn == null)
			{
				return new
				{
					success = false,
					error = "The current game cannot run the zombie-free ambient sound contract.",
					managerPresent = manager != null,
					tickManagerPresent = tickManager != null,
					ambientSustainerFieldPresent = ambientSustainerField != null,
					ambientVolumeFieldPresent = ambientVolumeField != null,
					zombiesClosingInSoundPresent = CustomDefs.ZombiesClosingIn != null
				};
			}

			var settingsSnapshot = SnapshotZombieSettings();
			if (TrySnapshotZombieFreeSchedule(manager, out var scheduleSnapshot, out var scheduleSnapshotError) == false)
			{
				return new
				{
					success = false,
					error = scheduleSnapshotError
				};
			}
			var oldUseSound = Constants.USE_SOUND;
			var oldAmbientVolume = Prefs.VolumeAmbient;
			var oldSustainer = ambientSustainerField.GetValue(tickManager) as Sustainer;
			var oldSustainerVolume = ambientVolumeField.GetValue(tickManager) is float volume ? volume : 0f;
			var hadAmbientTargetVolume = ZombieStateHandler.creepyAmbientSoundVolumes.TryGetValue(map.uniqueID, out var oldAmbientTargetVolume);
			var initialZombieIds = CurrentZombies(map)
				.Select(ZombieRuntimeActions.StableThingId)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
			Zombie spawnedZombie = null;
			Sustainer probeSustainer = null;
			try
			{
				Constants.USE_SOUND = true;
				Prefs.VolumeAmbient = Mathf.Max(Prefs.VolumeAmbient, 0.5f);
				ApplyZombieSettingsOverride(settings =>
				{
					settings.showZombieEventLetters = false;
					settings.useDynamicThreatLevel = false;
					settings.daysBeforeZombiesCome = 0;
					settings.threatScale = Math.Max(settings.threatScale, 1f);
					settings.playCreepyAmbientSound = true;
					settings.zombieFreeEvents = true;
				});

				if (TryFindClearSpawnCell(map, new IntVec3(map.Size.x / 2, 0, map.Size.z / 2), 28f, out var zombieCell, out var zombieCellError))
					spawnedZombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true) as Zombie;

				ZombieStateHandler.creepyAmbientSoundVolumes[map.uniqueID] = 1f;
				probeSustainer = CustomDefs.ZombiesClosingIn.TrySpawnSustainer(SoundInfo.OnCamera(MaintenanceType.None));
				ambientSustainerField.SetValue(tickManager, probeSustainer);
				ambientVolumeField.SetValue(tickManager, 0.5f);
				var beforeSustainerPresent = (ambientSustainerField.GetValue(tickManager) as Sustainer) != null;
				var before = DescribeAmbientSoundState(map, tickManager, ZombieSettings.Values, CurrentZombies(map).Length);

				manager.DebugClearSchedule();
				var forcedWindow = manager.DebugForceWindowStartingNow(GenDate.TicksPerDay);
				AdvanceGameTicks(30);

				var afterSustainerPresent = (ambientSustainerField.GetValue(tickManager) as Sustainer) != null;
				var afterTargetVolume = ZombieStateHandler.creepyAmbientSoundVolumes.TryGetValue(map.uniqueID, out var targetVolume)
					? targetVolume
					: -1f;
				var afterStoredVolume = ambientVolumeField.GetValue(tickManager) is float storedVolume ? storedVolume : -1f;
				var after = DescribeAmbientSoundState(map, tickManager, ZombieSettings.Values, CurrentZombies(map).Length);

				return new
				{
					success = ZombieFreeEventManager.IsActiveNow()
						&& beforeSustainerPresent
						&& afterSustainerPresent == false
						&& Mathf.Approximately(afterTargetVolume, 0f)
						&& Mathf.Approximately(afterStoredVolume, 0f),
					activeNow = ZombieFreeEventManager.IsActiveNow(),
					spawnedZombie = DescribeZombie(spawnedZombie),
					forcedWindow = new
					{
						offsetStartTicks = forcedWindow.startTick - GenTicks.TicksGame,
						offsetEndTicks = forcedWindow.endTick - GenTicks.TicksGame,
						forcedWindow.DurationTicks
					},
					beforeSustainerPresent,
					afterSustainerPresent,
					afterTargetVolume,
					afterStoredVolume,
					before,
					after,
					sourcePath = "TickManager.TickTasks -> ZombieFreeEventManager.IsActiveNow -> StopAmbientSound"
				};
			}
			finally
			{
				var currentSustainer = ambientSustainerField.GetValue(tickManager) as Sustainer;
				if (currentSustainer != null && ReferenceEquals(currentSustainer, oldSustainer) == false)
					currentSustainer.End();
				ambientSustainerField.SetValue(tickManager, oldSustainer);
				ambientVolumeField.SetValue(tickManager, oldSustainerVolume);
				if (hadAmbientTargetVolume)
					ZombieStateHandler.creepyAmbientSoundVolumes[map.uniqueID] = oldAmbientTargetVolume;
				else
					ZombieStateHandler.creepyAmbientSoundVolumes.Remove(map.uniqueID);

				foreach (var zombie in CurrentZombies(map).Where(zombie => initialZombieIds.Contains(ZombieRuntimeActions.StableThingId(zombie)) == false).ToArray())
					if (zombie.Destroyed == false)
						zombie.Destroy(DestroyMode.Vanish);

				Constants.USE_SOUND = oldUseSound;
				Prefs.VolumeAmbient = oldAmbientVolume;
				RestoreZombieSettings(settingsSnapshot);
				RestoreZombieFreeSchedule(manager, scheduleSnapshot);
				}
			}

			static object RunZombieFreeOverlapContract(Map map)
			{
				var manager = ZombieFreeEventManager.Current;
				if (TrySnapshotZombieFreeSchedule(manager, out var scheduleSnapshot, out var scheduleSnapshotError) == false)
				{
					return new
					{
						success = false,
						error = scheduleSnapshotError,
						managerPresent = manager != null,
						windowsFieldPresent = scheduleSnapshot?.windowsField != null,
						nextClusterStartFieldPresent = scheduleSnapshot?.nextClusterStartField != null
					};
				}

				var windowsField = scheduleSnapshot.windowsField;
				var nextClusterStartField = scheduleSnapshot.nextClusterStartField;
				var settingsSnapshot = SnapshotZombieSettings();
				var beforeLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
					.ToHashSet();
				Letter[] newLetters = Array.Empty<Letter>();
				try
				{
					ApplyZombieSettingsOverride(settings =>
					{
						settings.showZombieEventLetters = true;
						settings.useDynamicThreatLevel = false;
						settings.daysBeforeZombiesCome = 0;
						settings.threatScale = Math.Max(settings.threatScale, 1f);
						settings.zombieFreeEvents = true;
					});

					manager.DebugClearSchedule();

					var ticks = GenTicks.TicksGame;
					var overlappingWindows = new List<ZombieFreeEventWindow>
					{
						new(ticks, ticks + GenDate.TicksPerDay * 2),
						new(ticks + GenDate.TicksPerDay, ticks + GenDate.TicksPerDay * 3)
					};
					windowsField.SetValue(manager, overlappingWindows);
					nextClusterStartField.SetValue(manager, ticks + GenDate.TicksPerDay * 100);

					manager.WorldComponentTick();
					var afterFirstTickWindows = ((List<ZombieFreeEventWindow>)windowsField.GetValue(manager))
						.Select(CopyZombieFreeWindow)
						.ToList();
					var conditionAfterFirstTick = Find.World?.gameConditionManager?.GetActiveCondition(CustomDefs.ZombieFreeEvent);
					var conditionTicksAfterFirstTick = conditionAfterFirstTick?.TicksLeft ?? -1;
					var conditionTooltipAfterFirstTick = conditionAfterFirstTick?.TooltipString ?? "";
					var lettersAfterFirstTick = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
						.Where(letter => beforeLetters.Contains(letter) == false)
						.ToArray();

					manager.WorldComponentTick();
					var afterSecondTickWindows = ((List<ZombieFreeEventWindow>)windowsField.GetValue(manager))
						.Select(CopyZombieFreeWindow)
						.ToList();
					newLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
						.Where(letter => beforeLetters.Contains(letter) == false)
						.ToArray();

					var expectedTicksLeft = GenDate.TicksPerDay * 3;
					var matchingLetters = newLetters
						.Where(letter => letter?.def == CustomDefs.ZombieFreeEventLetter)
						.ToArray();
					var allWindowsHandled = afterSecondTickWindows
						.Where(window => window.startTick <= ticks && window.endTick >= ticks + GenDate.TicksPerDay * 2)
						.All(window => window.startHandled && window.letterSent);
					var noDuplicateLetters = newLetters.Length == lettersAfterFirstTick.Length;

					return new
					{
						success = ZombieFreeEventManager.IsActiveNow()
							&& conditionAfterFirstTick is GameCondition_ZombieFreeEvent
							&& conditionTicksAfterFirstTick == expectedTicksLeft
							&& matchingLetters.Length == 1
							&& lettersAfterFirstTick.Length == 1
							&& noDuplicateLetters
							&& allWindowsHandled
							&& conditionTooltipAfterFirstTick.Contains("ZombieFreeEventTimeLeft".Translate().ToString())
							&& conditionTooltipAfterFirstTick.Contains("Lasted".Translate().ToString()) == false,
						sourcePath = "Injected overlapping ZombieFreeEventManager windows -> WorldComponentTick -> ActiveWindowsAt grouped side effects",
						activeNow = ZombieFreeEventManager.IsActiveNow(),
						expectedTicksLeft,
						condition = new
						{
							type = conditionAfterFirstTick?.GetType().FullName,
							ticksLeft = conditionTicksAfterFirstTick,
							tooltipHasTimeLeft = conditionTooltipAfterFirstTick.Contains("ZombieFreeEventTimeLeft".Translate().ToString()),
							tooltipHasLasted = conditionTooltipAfterFirstTick.Contains("Lasted".Translate().ToString()),
							tooltip = conditionTooltipAfterFirstTick
						},
						overlappingWindows = DescribeZombieFreePreviewWindows(overlappingWindows),
						afterFirstTickWindows = DescribeZombieFreePreviewWindows(afterFirstTickWindows),
						afterSecondTickWindows = DescribeZombieFreePreviewWindows(afterSecondTickWindows),
						lettersAfterFirstTick = lettersAfterFirstTick.Select(DescribeLetter).ToArray(),
						newLettersAfterSecondTick = newLetters.Select(DescribeLetter).ToArray(),
						matchingZombieFreeLetterCount = matchingLetters.Length,
						noDuplicateLetters,
						allWindowsHandled
					};
				}
				finally
				{
					if (Find.LetterStack != null)
						foreach (var letter in newLetters)
							Find.LetterStack.RemoveLetter(letter);
					manager.DebugClearSchedule();
					RestoreZombieSettings(settingsSnapshot);
					RestoreZombieFreeSchedule(manager, scheduleSnapshot);
				}
			}

			static object RunZombieFreeReviewFixContract(Map map)
			{
				var manager = ZombieFreeEventManager.Current;
				if (TrySnapshotZombieFreeSchedule(manager, out var originalSchedule, out var scheduleSnapshotError) == false)
				{
					return new
					{
						success = false,
						error = scheduleSnapshotError,
						managerPresent = manager != null
					};
				}

				var settingsSnapshot = SnapshotZombieSettings();

				List<ZombieFreeEventWindow> ReadRawWindows()
				{
					return ((List<ZombieFreeEventWindow>)originalSchedule.windowsField.GetValue(manager))?
						.Select(CopyZombieFreeWindow)
						.Where(window => window != null)
						.OrderBy(window => window.startTick)
						.ToList() ?? new List<ZombieFreeEventWindow>();
				}

				int ReadRawNextClusterStart()
				{
					return (int)originalSchedule.nextClusterStartField.GetValue(manager);
				}

				void SetRawSchedule(List<ZombieFreeEventWindow> rawWindows, int nextClusterStartTick)
				{
					originalSchedule.windowsField.SetValue(manager, rawWindows
						.Select(CopyZombieFreeWindow)
						.Where(window => window != null)
						.ToList());
					originalSchedule.nextClusterStartField.SetValue(manager, nextClusterStartTick);
				}

				static bool WindowsEqual(List<ZombieFreeEventWindow> left, List<ZombieFreeEventWindow> right)
				{
					if (left == null || right == null || left.Count != right.Count)
						return false;
					for (var i = 0; i < left.Count; i++)
					{
						var a = left[i];
						var b = right[i];
						if (a.startTick != b.startTick || a.endTick != b.endTick || a.startHandled != b.startHandled || a.letterSent != b.letterSent)
							return false;
					}
					return true;
				}

				static bool ObjectSucceeded(object value)
				{
					return (bool)(value?.GetType().GetProperty("success")?.GetValue(value) ?? false);
				}

				try
				{
					ApplyZombieSettingsOverride(settings =>
					{
						settings.showZombieEventLetters = false;
						settings.useDynamicThreatLevel = false;
						settings.daysBeforeZombiesCome = 0;
						settings.threatScale = Math.Max(settings.threatScale, 1f);
						settings.playCreepyAmbientSound = true;
						settings.zombieFreeEvents = true;
					});

					var ticks = GenTicks.TicksGame;
					var sentinelNextClusterStart = ticks + GenDate.TicksPerDay * 1000;
					var sentinelWindows = new List<ZombieFreeEventWindow>
					{
						new(ticks + GenDate.TicksPerDay * 10, ticks + GenDate.TicksPerDay * 12)
						{
							startHandled = true,
							letterSent = true
						}
					};
					SetRawSchedule(sentinelWindows, sentinelNextClusterStart);
					var sentinelSchedule = new ZombieFreeScheduleSnapshot
					{
						windowsField = originalSchedule.windowsField,
						nextClusterStartField = originalSchedule.nextClusterStartField,
						windows = sentinelWindows,
						nextClusterStartTick = sentinelNextClusterStart
					};
					manager.DebugClearSchedule();
					RestoreZombieFreeSchedule(manager, sentinelSchedule);
					var helperRestoredWindows = ReadRawWindows();
					var helperRestored = WindowsEqual(sentinelWindows, helperRestoredWindows)
						&& ReadRawNextClusterStart() == sentinelNextClusterStart;

					SetRawSchedule(sentinelWindows, sentinelNextClusterStart);
					var ambientResult = RunZombieFreeAmbientSoundContract(map);
					var ambientRestoredWindows = ReadRawWindows();
					var ambientRestored = WindowsEqual(sentinelWindows, ambientRestoredWindows)
						&& ReadRawNextClusterStart() == sentinelNextClusterStart;

					ticks = GenTicks.TicksGame;
					var nearbyFutureWindow = new ZombieFreeEventWindow(
						ticks + GenDate.TicksPerHour,
						ticks + GenDate.TicksPerHour + GenDate.TicksPerDay * 2);
					SetRawSchedule(new List<ZombieFreeEventWindow> { nearbyFutureWindow }, ticks + GenDate.TicksPerDay * 1000);
					var forcedWindow = manager.DebugForceWindowStartingNow(GenDate.TicksPerDay * 2);
					var forceNowWindows = ReadRawWindows();
					var shiftedFutureWindow = forceNowWindows
						.Where(window => ReferenceEquals(window, forcedWindow) == false && window.startTick != forcedWindow.startTick)
						.FirstOrDefault(window => window.startTick >= forcedWindow.endTick + GenDate.TicksPerDay * 2);
					var forceStartsImmediately = forcedWindow.startTick == ticks
						&& forcedWindow.ActiveAt(ticks)
						&& ZombieFreeEventManager.IsActiveNow()
						&& HasZombieFreeWindowOverlaps(forceNowWindows) == false
						&& shiftedFutureWindow != null;

					var currentDay = Mathf.FloorToInt(ticks / (float)GenDate.TicksPerDay);
					var disabledSettings = ZombieSettings.Values?.MakeCopy() ?? new SettingsGroup();
					disabledSettings.daysBeforeZombiesCome = 0;
					disabledSettings.zombieFreeEvents = false;
					disabledSettings.threatScale = 1f;
					var enabledSettings = disabledSettings.MakeCopy();
					enabledSettings.zombieFreeEvents = true;
					ZombieSettings.ValuesOverTime = new List<SettingsKeyFrame>
					{
						new()
						{
							amount = currentDay,
							unit = SettingsKeyFrame.Unit.Days,
							values = disabledSettings.MakeCopy()
						},
						new()
						{
							amount = currentDay + 1,
							unit = SettingsKeyFrame.Unit.Days,
							values = enabledSettings.MakeCopy()
						},
						new()
						{
							amount = currentDay + 3,
							unit = SettingsKeyFrame.Unit.Days,
							values = disabledSettings.MakeCopy()
						}
					};
					ZombieSettings.Values = ZombieSettings.ValuesAtGameTick(ticks);

					var dayTwoTick = (currentDay + 2) * GenDate.TicksPerDay;
					var dayThreeTick = (currentDay + 3) * GenDate.TicksPerDay;
					var keyframedRawWindow = new ZombieFreeEventWindow(dayTwoTick, dayThreeTick + GenDate.TicksPerDay);
					SetRawSchedule(new List<ZombieFreeEventWindow> { keyframedRawWindow }, ticks + GenDate.TicksPerDay * 1000);
					var forecastWindows = ZombieFreeEventManager.WindowsForAbsRange(
						ZombieFreeEventManager.AbsTickForGameTick(dayTwoTick),
						ZombieFreeEventManager.AbsTickForGameTick(dayThreeTick + GenDate.TicksPerDay));
					var enabledTick = dayTwoTick + GenDate.TicksPerHour;
					var disabledTick = dayThreeTick + GenDate.TicksPerHour;
					var forecastShowsEnabledTick = forecastWindows.Any(window => window.ActiveAt(enabledTick));
					var forecastHidesDisabledTick = forecastWindows.All(window => window.ActiveAt(disabledTick) == false);
					var activeMatchesEnabledKeyframe = manager.IsActiveAtGameTick(enabledTick);
					var activeMatchesDisabledKeyframe = manager.IsActiveAtGameTick(disabledTick) == false;

					return new
					{
						success = helperRestored
							&& ObjectSucceeded(ambientResult)
							&& ambientRestored
							&& forceStartsImmediately
							&& forecastShowsEnabledTick
							&& forecastHidesDisabledTick
							&& activeMatchesEnabledKeyframe
							&& activeMatchesDisabledKeyframe,
						sourcePath = "Review regression contract for zombie-free schedule restore, force-now insertion, and keyframed forecast enablement",
						scheduleRestore = new
						{
							helperRestored,
							ambientSucceeded = ObjectSucceeded(ambientResult),
							ambientRestored,
							sentinel = DescribeZombieFreePreviewWindows(sentinelWindows),
							afterHelperRestore = DescribeZombieFreePreviewWindows(helperRestoredWindows),
							afterAmbientContract = DescribeZombieFreePreviewWindows(ambientRestoredWindows)
						},
						forceNow = new
						{
							forceStartsImmediately,
							forcedWindow = new
							{
								offsetStartTicks = forcedWindow.startTick - ticks,
								offsetEndTicks = forcedWindow.endTick - ticks,
								forcedWindow.DurationTicks
							},
							hasOverlaps = HasZombieFreeWindowOverlaps(forceNowWindows),
							shiftedFutureWindow = shiftedFutureWindow == null
								? null
								: new
								{
									offsetStartTicks = shiftedFutureWindow.startTick - ticks,
									offsetEndTicks = shiftedFutureWindow.endTick - ticks,
									shiftedFutureWindow.DurationTicks
								},
							windows = DescribeZombieFreePreviewWindows(forceNowWindows)
						},
						keyframedToggle = new
						{
							currentDay,
							enabledTickOffset = enabledTick - ticks,
							disabledTickOffset = disabledTick - ticks,
							forecastShowsEnabledTick,
							forecastHidesDisabledTick,
							activeMatchesEnabledKeyframe,
							activeMatchesDisabledKeyframe,
							forecastWindows = DescribeZombieFreePreviewWindows(forecastWindows)
						}
					};
				}
				finally
				{
					RestoreZombieSettings(settingsSnapshot);
					RestoreZombieFreeSchedule(manager, originalSchedule);
				}
			}

			static object RunZombieFreeSchedulePreview()
			{
			const int previewSeed = 73127;
			const int horizonDays = 180;
			var initialSilenceDays = ZombieSettings.Values.daysBeforeZombiesCome;
				var horizonTicks = horizonDays * GenDate.TicksPerDay;
				var difficulties = Enumerable.Range(0, 10)
					.Select(index => 0.5f + index * 0.5f)
					.ToArray();
				var previews = difficulties
					.Select(difficulty =>
					{
						var windows = ZombieFreeEventManager.DebugPreviewWindows(difficulty, previewSeed, horizonTicks, initialSilenceDays);
						var scheduledWindows = windows
							.Where(window => IsInitialSilencePreview(window) == false)
							.ToArray();
						return new
						{
							difficultyPercent = Mathf.RoundToInt(difficulty * 100f),
							difficulty,
							clusterPeriodDays = RoundDay(ZombieFreeEventManager.ClusterPeriodDaysFor(difficulty)),
							durationMeanDays = RoundDay(ZombieFreeEventManager.EventDurationMeanDaysFor(difficulty)),
							durationJitterDays = RoundDay(ZombieFreeEventManager.EventDurationJitterDaysFor(difficulty)),
							eventOffsetMaxDays = RoundDay(ZombieFreeEventManager.EventOffsetMaxDaysFor(difficulty)),
							hasOverlaps = HasZombieFreeWindowOverlaps(windows),
							minimumGapDays = MinimumZombieFreeWindowGapDays(windows),
							scheduledWindowCount = scheduledWindows.Length,
							firstScheduledStartDay = scheduledWindows.Length == 0 ? (float?)null : RoundDay(DayForTick(scheduledWindows[0].startTick)),
							windows = DescribeZombieFreePreviewWindows(windows)
						};
					})
					.ToArray();

				float DynamicStressDifficulty(int tick)
				{
					var day = DayForTick(tick);
					if (day <= 60f)
						return Mathf.Lerp(5f, 0.5f, day / 60f);
					if (day <= 120f)
						return Mathf.Lerp(0.5f, 5f, (day - 60f) / 60f);
					return 5f;
				}

				var dynamicStressWindows = ZombieFreeEventManager.DebugPreviewWindows(DynamicStressDifficulty, previewSeed, horizonTicks, initialSilenceDays);
				var dynamicStressHasOverlaps = HasZombieFreeWindowOverlaps(dynamicStressWindows);
				return new
				{
					success = previews.All(preview => preview.hasOverlaps == false)
						&& dynamicStressHasOverlaps == false,
					sourcePath = "ZombieFreeEventManager.DebugPreviewWindows using the same duration, jitter, period, and paired-cluster math as live scheduling",
					previewSeed,
					horizonDays,
					ticksPerDay = GenDate.TicksPerDay,
					initialSilenceDays = RoundDay(initialSilenceDays),
					dynamicStress = new
					{
						description = "500% -> 50% -> 500% threat-scale interpolation over 120 days",
						hasOverlaps = dynamicStressHasOverlaps,
						minimumGapDays = MinimumZombieFreeWindowGapDays(dynamicStressWindows),
						windows = DescribeZombieFreePreviewWindows(dynamicStressWindows)
					},
					difficulties = previews
				};
			}

		static object RunZombieFreeForecastPreview(int difficultyPercent)
		{
			const int previewSeed = 73127;
			const int horizonDays = 180;
			var clampedDifficultyPercent = Mathf.Clamp(difficultyPercent, 50, 500);
			var difficulty = clampedDifficultyPercent / 100f;
			var initialSilenceDays = ZombieSettings.Values.daysBeforeZombiesCome;
			var windows = ZombieFreeEventManager.DebugPreviewWindows(
				difficulty,
				previewSeed,
				horizonDays * GenDate.TicksPerDay,
				initialSilenceDays);
			var scheduledWindows = windows
				.Where(window => IsInitialSilencePreview(window) == false)
				.ToArray();
			var forecastLabel = $"{clampedDifficultyPercent}% | period {RoundDay(ZombieFreeEventManager.ClusterPeriodDaysFor(difficulty)):0.##}d | silence {RoundDay(ZombieFreeEventManager.EventDurationMeanDaysFor(difficulty)):0.##}+/-{RoundDay(ZombieFreeEventManager.EventDurationJitterDaysFor(difficulty)):0.##}d";

			var previewWindowOpened = false;
			if (Find.WindowStack != null)
			{
				_ = Find.WindowStack.TryRemove(typeof(ForecastTooltipPreviewWindow), false);
				Find.WindowStack.Add(new ForecastTooltipPreviewWindow(forecastLabel, difficulty, windows));
				previewWindowOpened = Find.WindowStack.IsOpen(typeof(ForecastTooltipPreviewWindow));
			}

			return new
			{
				success = previewWindowOpened,
				sourcePath = "ForecastTooltipPreviewWindow -> ZombieWeather.GenerateTooltipDrawer preview difficulty + ZombieFreeEventManager.DebugPreviewWindows",
				previewWindowOpened,
				previewWindowTitle = ForecastTooltipPreviewWindow.StableTitle,
				previewSeed,
				horizonDays,
				requestedDifficultyPercent = difficultyPercent,
				difficultyPercent = clampedDifficultyPercent,
				difficulty,
				initialSilenceDays = RoundDay(initialSilenceDays),
				clusterPeriodDays = RoundDay(ZombieFreeEventManager.ClusterPeriodDaysFor(difficulty)),
				durationMeanDays = RoundDay(ZombieFreeEventManager.EventDurationMeanDaysFor(difficulty)),
				durationJitterDays = RoundDay(ZombieFreeEventManager.EventDurationJitterDaysFor(difficulty)),
					eventOffsetMaxDays = RoundDay(ZombieFreeEventManager.EventOffsetMaxDaysFor(difficulty)),
					hasOverlaps = HasZombieFreeWindowOverlaps(windows),
					minimumGapDays = MinimumZombieFreeWindowGapDays(windows),
					scheduledWindowCount = scheduledWindows.Length,
					firstScheduledStartDay = scheduledWindows.Length == 0 ? (float?)null : RoundDay(DayForTick(scheduledWindows[0].startTick)),
					windows = DescribeZombieFreePreviewWindows(windows)
				};
			}

		static object RunZombieFreeHoverSetup(Map map, int difficultyPercent)
		{
			var weather = map.GetComponent<ZombieWeather>();
			var tickManager = map.GetComponent<TickManager>();
			var manager = ZombieFreeEventManager.Current;
			if (weather == null || tickManager == null || manager == null)
			{
				return new
				{
					success = false,
					error = "No ZombieWeather, Zombieland TickManager, or ZombieFreeEventManager is available for the current game.",
					weatherPresent = weather != null,
					tickManagerPresent = tickManager != null,
					managerPresent = manager != null
				};
			}

			const int previewSeed = 73127;
			var clampedDifficultyPercent = Mathf.Clamp(difficultyPercent, 50, 500);
			var difficulty = clampedDifficultyPercent / 100f;
			var settings = ZombieSettings.Values?.MakeCopy() ?? new SettingsGroup();
			settings.showZombieStats = true;
			settings.useDynamicThreatLevel = true;
			settings.daysBeforeZombiesCome = 3;
			settings.threatScale = difficulty;
			settings.zombiesDieOnZeroThreat = true;
			settings.zombieFreeEvents = true;

			ZombieSettings.ValuesOverTime = new List<SettingsKeyFrame>
			{
				new()
				{
					amount = 0,
					unit = SettingsKeyFrame.Unit.Days,
					values = settings.MakeCopy()
				}
			};
			ZombieSettings.Values = ZombieSettings.ValuesAtGameTick(GenTicks.TicksGame);

			if (Find.WindowStack != null)
				_ = Find.WindowStack.TryRemove(typeof(ForecastTooltipPreviewWindow), false);

			var scheduleEndTick = GenTicks.TicksGame + GenDate.TicksPerQuadrum * 5 + GenDate.TicksPerDay * 2;
			manager.DebugRebuildScheduleThrough(scheduleEndTick, previewSeed);

			Zombie spawnedZombie = null;
			if (tickManager.ZombieCount() <= 0)
			{
				if (TryFindClearSpawnCell(map, new IntVec3(map.Size.x / 2, 0, map.Size.z / 2), 24f, out var zombieCell, out var zombieCellError) == false)
					return zombieCellError;
				spawnedZombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true) as Zombie;
			}

			var geometry = DescribeThreatForecastHoverGeometry(map);
			var tooltipOnScreen = (bool)(geometry?.GetType().GetProperty("tooltipOnScreen")?.GetValue(geometry) ?? false);
			var zombieCount = (int)(geometry?.GetType().GetProperty("zombieCount")?.GetValue(geometry) ?? 0);
			var zombieCountTooltipOnScreen = (bool)(geometry?.GetType().GetProperty("zombieCountTooltipOnScreen")?.GetValue(geometry) ?? false);
			var forecast = DescribeThreatForecast(map);
			var qStart = GenTicks.TicksAbs - GenTicks.TicksAbs % GenDate.TicksPerQuadrum;
			var qEnd = qStart + GenDate.TicksPerQuadrum * 5;
			var quadrumWindows = ZombieFreeEventManager.WindowsForAbsRange(qStart, qEnd);
			var hasOverlaps = HasZombieFreeWindowOverlaps(quadrumWindows);

			return new
			{
				success = tooltipOnScreen
					&& zombieCount > 0
					&& zombieCountTooltipOnScreen
					&& forecast.forecastLabel.Contains("ThreatLevel".Translate().ToString())
					&& quadrumWindows.Count > 0
					&& hasOverlaps == false,
				sourcePath = "ZombieSettings.ValuesOverTime single keyframe -> ZombieFreeEventManager.DebugRebuildScheduleThrough -> GlobalControlsUtility.DoDate hover geometry",
				hoverCanStayOpenAcrossDifficultyChanges = true,
				requestedDifficultyPercent = difficultyPercent,
				difficultyPercent = clampedDifficultyPercent,
				difficulty,
				previewSeed,
				spawnedZombie = spawnedZombie == null ? null : DescribeZombie(spawnedZombie),
				settings = new
				{
					ZombieSettings.Values.showZombieStats,
					ZombieSettings.Values.useDynamicThreatLevel,
					ZombieSettings.Values.daysBeforeZombiesCome,
					ZombieSettings.Values.threatScale,
					ZombieSettings.Values.zombiesDieOnZeroThreat,
					ZombieSettings.Values.zombieFreeEvents,
					keyframeCount = ZombieSettings.ValuesOverTime?.Count ?? 0
				},
				schedule = new
				{
					scheduleEndTick,
					clusterPeriodDays = RoundDay(ZombieFreeEventManager.ClusterPeriodDaysFor(difficulty)),
					durationMeanDays = RoundDay(ZombieFreeEventManager.EventDurationMeanDaysFor(difficulty)),
					durationJitterDays = RoundDay(ZombieFreeEventManager.EventDurationJitterDaysFor(difficulty)),
					eventOffsetMaxDays = RoundDay(ZombieFreeEventManager.EventOffsetMaxDaysFor(difficulty)),
					hasOverlaps,
					minimumGapDays = MinimumZombieFreeWindowGapDays(quadrumWindows),
					quadrumWindows = DescribeZombieFreePreviewWindows(quadrumWindows)
				},
				forecast,
				geometry
			};
		}

		static object DescribeThreatForecastHoverGeometry(Map map)
		{
			var tickManager = map.GetComponent<TickManager>();
			var weather = map.GetComponent<ZombieWeather>();
			var zombieCount = tickManager?.ZombieCount() ?? 0;
			var (rangeMin, rangeMax) = weather.GetFactorRangeFor();
			var forecastLabel = Patches.GlobalControlsUtility_DoDate_Patch.FormatThreatForecast(rangeMin, rangeMax);

			var width = 220f;
			var leftX = Math.Max(0f, UI.screenWidth - width - 10f);
			var curBaseY = Math.Max(420f, UI.screenHeight - 10f);
			var dateRect = new Rect(leftX, curBaseY - DateReadout.Height, width, DateReadout.Height);
			var forecastBaseY = curBaseY - dateRect.height;
			object zombieCountRectDescription = null;
			object zombieCountTooltipRectDescription = null;
			object zombieCountHoverPoint = null;
			var zombieCountTooltipOnScreen = false;
			if (zombieCount > 0)
			{
				var zombieCountString = zombieCount + " Zombies";
				var zombieCountRect = Patches.GlobalControlsUtility_DoDate_Patch.GetRightAlignedReadoutRect(leftX, width, forecastBaseY, zombieCountString);
				var zombieCountTooltipRect = Patches.GlobalControlsUtility_DoDate_Patch.GetThreatForecastTooltipRect(zombieCountRect);
				zombieCountRectDescription = DescribeRect(zombieCountRect);
				zombieCountTooltipRectDescription = DescribeRect(zombieCountTooltipRect);
				zombieCountHoverPoint = new
				{
					x = zombieCountRect.center.x,
					y = zombieCountRect.center.y
				};
				zombieCountTooltipOnScreen = RectWithinScreen(zombieCountTooltipRect);
				forecastBaseY -= zombieCountRect.height;
			}

			var forecastRect = Patches.GlobalControlsUtility_DoDate_Patch.GetRightAlignedReadoutRect(leftX, width, forecastBaseY, forecastLabel);
			var tooltipRect = Patches.GlobalControlsUtility_DoDate_Patch.GetThreatForecastTooltipRect(forecastRect);
			var actualVisible = Patches.GlobalControlsUtility_DoDate_Patch.LastThreatForecastVisible;
			var actualForecastRect = Patches.GlobalControlsUtility_DoDate_Patch.LastThreatForecastRect;
			var actualTooltipRect = Patches.GlobalControlsUtility_DoDate_Patch.LastThreatForecastTooltipRect;
			var useActualGeometry = actualVisible
				&& actualForecastRect.width > 0f
				&& actualForecastRect.height > 0f
				&& RectWithinScreen(actualForecastRect);
			var activeForecastRect = useActualGeometry ? actualForecastRect : forecastRect;
			var activeTooltipRect = useActualGeometry ? actualTooltipRect : tooltipRect;
			var forecastCenter = activeForecastRect.center;
			var useZombieCountHoverPoint = zombieCountHoverPoint != null;
			return new
			{
				screen = new
				{
					width = UI.screenWidth,
					height = UI.screenHeight
				},
				leftX,
				width,
				curBaseY,
				dateRect = DescribeRect(dateRect),
				zombieCount,
				zombieCountRect = zombieCountRectDescription,
				zombieCountTooltipRect = zombieCountTooltipRectDescription,
				zombieCountTooltipOnScreen,
				forecastRect = DescribeRect(activeForecastRect),
				tooltipRect = DescribeRect(activeTooltipRect),
				hoverSource = useZombieCountHoverPoint ? "zombieCount" : "threatForecast",
				hoverPoint = useZombieCountHoverPoint
					? zombieCountHoverPoint
					: new
					{
						x = forecastCenter.x,
						y = forecastCenter.y
					},
				forecastLabel = useActualGeometry
					? Patches.GlobalControlsUtility_DoDate_Patch.LastThreatForecastLabel
					: forecastLabel,
				tooltipOnScreen = RectWithinScreen(activeTooltipRect),
				usingActualDrawnGeometry = useActualGeometry,
				hoverTargets = new
				{
					zombieCount = new
					{
						visible = zombieCount > 0,
						rect = zombieCountRectDescription,
						tooltipRect = zombieCountTooltipRectDescription,
						tooltipOnScreen = zombieCountTooltipOnScreen,
						hoverPoint = zombieCountHoverPoint
					},
					threatForecast = new
					{
						visible = true,
						rect = DescribeRect(activeForecastRect),
						tooltipRect = DescribeRect(activeTooltipRect),
						tooltipOnScreen = RectWithinScreen(activeTooltipRect),
						hoverPoint = new
						{
							x = forecastCenter.x,
							y = forecastCenter.y
						}
					}
				},
				actualDrawnGeometry = new
				{
					visible = actualVisible,
					frame = Patches.GlobalControlsUtility_DoDate_Patch.LastThreatForecastFrame,
					currentFrame = Time.frameCount,
					label = Patches.GlobalControlsUtility_DoDate_Patch.LastThreatForecastLabel,
					forecastRect = DescribeRect(actualForecastRect),
					tooltipRect = DescribeRect(actualTooltipRect)
				},
				actualHoverGeometry = new
				{
					source = Patches.GlobalControlsUtility_DoDate_Patch.LastThreatForecastHoverSource,
					frame = Patches.GlobalControlsUtility_DoDate_Patch.LastThreatForecastHoverFrame,
					currentFrame = Time.frameCount,
					label = Patches.GlobalControlsUtility_DoDate_Patch.LastThreatForecastHoverLabel,
					hoverRect = DescribeRect(Patches.GlobalControlsUtility_DoDate_Patch.LastThreatForecastHoverRect),
					tooltipRect = DescribeRect(Patches.GlobalControlsUtility_DoDate_Patch.LastThreatForecastHoverTooltipRect)
				},
				fallbackGeometry = new
				{
					zombieCountRect = zombieCountRectDescription,
					zombieCountTooltipRect = zombieCountTooltipRectDescription,
					forecastRect = DescribeRect(forecastRect),
					tooltipRect = DescribeRect(tooltipRect)
				},
				tooltipWindowId = Patches.GlobalControlsUtility_DoDate_Patch.ThreatForecastTooltipWindowId
			};
		}

		static object[] DescribeZombieFreePreviewWindows(List<ZombieFreeEventWindow> windows)
		{
			var result = new List<object>();
			var scheduledIndex = 0;
			float? previousEndDay = null;
			foreach (var window in windows.OrderBy(window => window.startTick))
			{
				var isInitialSilence = IsInitialSilencePreview(window);
				var startDay = DayForTick(window.startTick);
				var endDay = DayForTick(window.endTick);
				result.Add(new
				{
					kind = isInitialSilence ? "initialSilence" : "scheduledEvent",
					clusterIndex = isInitialSilence ? (int?)null : scheduledIndex / 2,
					eventInCluster = isInitialSilence ? (int?)null : scheduledIndex % 2 + 1,
					startTick = window.startTick,
					endTick = window.endTick,
					durationTicks = window.DurationTicks,
					startDay = RoundDay(startDay),
					endDay = RoundDay(endDay),
					durationDays = RoundDay(endDay - startDay),
					gapFromPreviousEndDays = previousEndDay.HasValue ? RoundDay(startDay - previousEndDay.Value) : (float?)null,
					letterWouldBeSent = window.letterSent == false
				});
				if (isInitialSilence == false)
					scheduledIndex++;
				previousEndDay = endDay;
			}
			return result.ToArray();
		}

			static bool IsInitialSilencePreview(ZombieFreeEventWindow window)
			{
				return window.startTick == 0 && window.letterSent;
			}

			static bool HasZombieFreeWindowOverlaps(IEnumerable<ZombieFreeEventWindow> windows)
			{
				var previousEnd = int.MinValue;
				foreach (var window in windows.OrderBy(window => window.startTick))
				{
					if (window.startTick < previousEnd)
						return true;
					previousEnd = Mathf.Max(previousEnd, window.endTick);
				}
				return false;
			}

			static float? MinimumZombieFreeWindowGapDays(IEnumerable<ZombieFreeEventWindow> windows)
			{
				float? minimumGap = null;
				ZombieFreeEventWindow previous = null;
				foreach (var window in windows.OrderBy(window => window.startTick))
				{
					if (previous != null)
					{
						var gap = DayForTick(window.startTick - previous.endTick);
						minimumGap = minimumGap.HasValue ? Mathf.Min(minimumGap.Value, gap) : gap;
					}
					previous = window;
				}
				return minimumGap.HasValue ? RoundDay(minimumGap.Value) : null;
			}

			static float DayForTick(int tick)
			{
			return tick / (float)GenDate.TicksPerDay;
		}

		static float RoundDay(float value)
		{
			return (float)Math.Round(value, 2);
		}

		static object RunThreatForecastUiContract(Map map, bool openPreviewWindow)
		{
			var weather = map.GetComponent<ZombieWeather>();
			var tickManager = map.GetComponent<TickManager>();
			if (weather == null || tickManager == null)
			{
				return new
				{
					success = false,
					error = "No ZombieWeather or Zombieland TickManager component is attached to the current map.",
					weatherPresent = weather != null,
					tickManagerPresent = tickManager != null
				};
			}

			var settingsSnapshot = SnapshotZombieSettings();
			var initialIds = CurrentZombies(map)
				.Select(ZombieRuntimeActions.StableThingId)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
			Zombie spawnedZombie = null;
			try
			{
				ApplyZombieSettingsOverride(settings =>
				{
					settings.showZombieStats = true;
					settings.useDynamicThreatLevel = true;
					settings.daysBeforeZombiesCome = 0;
					settings.threatScale = Math.Max(settings.threatScale, 1f);
				});

				if (TryFindClearSpawnCell(map, new IntVec3(map.Size.x / 2, 0, map.Size.z / 2), 24f, out var zombieCell, out var zombieCellError) == false)
					return zombieCellError;

				spawnedZombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true) as Zombie;
				var zombieCount = tickManager.ZombieCount();
				var zombieCountString = zombieCount + " Zombies";
				var (rangeMin, rangeMax) = weather.GetFactorRangeFor();
				var forecastLabel = Patches.GlobalControlsUtility_DoDate_Patch.FormatThreatForecast(rangeMin, rangeMax);

				var width = 220f;
				var leftX = Math.Max(0f, UI.screenWidth - width - 10f);
				var curBaseY = Math.Max(420f, UI.screenHeight - 10f);
				var dateRect = new Rect(leftX, curBaseY - DateReadout.Height, width, DateReadout.Height);
				var afterDateBaseY = curBaseY - dateRect.height;
				var zombieCountRect = Patches.GlobalControlsUtility_DoDate_Patch.GetRightAlignedReadoutRect(leftX, width, afterDateBaseY, zombieCountString);
				var afterZombieCountBaseY = afterDateBaseY - zombieCountRect.height;
				var forecastRect = Patches.GlobalControlsUtility_DoDate_Patch.GetRightAlignedReadoutRect(leftX, width, afterZombieCountBaseY, forecastLabel);
				var tooltipRect = Patches.GlobalControlsUtility_DoDate_Patch.GetThreatForecastTooltipRect(forecastRect);
				var readoutsDoNotOverlap = dateRect.Overlaps(zombieCountRect) == false
					&& dateRect.Overlaps(forecastRect) == false
					&& zombieCountRect.Overlaps(forecastRect) == false;
				var readoutsOnScreen = RectWithinScreen(dateRect)
					&& RectWithinScreen(zombieCountRect)
					&& RectWithinScreen(forecastRect);
				var tooltipOnScreen = RectWithinScreen(tooltipRect);
				var tooltipDrawerAvailable = ZombieWeather.GenerateTooltipDrawer(tooltipRect.AtZero()) != null;

				var previewWindowOpened = false;
				if (openPreviewWindow && Find.WindowStack != null)
				{
					_ = Find.WindowStack.TryRemove(typeof(ForecastTooltipPreviewWindow), false);
					Find.WindowStack.Add(new ForecastTooltipPreviewWindow(forecastLabel));
					previewWindowOpened = Find.WindowStack.IsOpen(typeof(ForecastTooltipPreviewWindow));
				}

				return new
				{
					success = spawnedZombie != null
						&& zombieCount > 0
						&& forecastLabel.Contains("ThreatLevel".Translate().ToString())
						&& readoutsDoNotOverlap
						&& readoutsOnScreen
						&& tooltipOnScreen
						&& tooltipDrawerAvailable
						&& (openPreviewWindow == false || previewWindowOpened),
					sourcePath = "GlobalControlsUtility.DoDate postfix geometry + ZombieWeather.GenerateTooltipDrawer preview window",
					openPreviewWindow,
					previewWindowOpened,
					previewWindowTitle = ForecastTooltipPreviewWindow.StableTitle,
					screen = new
					{
						width = UI.screenWidth,
						height = UI.screenHeight
					},
					settings = new
					{
						ZombieSettings.Values.showZombieStats,
						ZombieSettings.Values.useDynamicThreatLevel
					},
					zombieCount,
					zombieCountString,
					spawnedZombie = DescribeZombie(spawnedZombie),
					forecast = new
					{
						rangeMin,
						rangeMax,
						forecastLabel,
						threatLabel = "ThreatLevel".Translate().ToString()
					},
					geometry = new
					{
						leftX,
						width,
						curBaseY,
						dateRect = DescribeRect(dateRect),
						zombieCountRect = DescribeRect(zombieCountRect),
						forecastRect = DescribeRect(forecastRect),
						tooltipRect = DescribeRect(tooltipRect),
						readoutsDoNotOverlap,
						readoutsOnScreen,
						tooltipOnScreen
					},
					tooltip = new
					{
						drawerAvailable = tooltipDrawerAvailable,
						windowId = Patches.GlobalControlsUtility_DoDate_Patch.ThreatForecastTooltipWindowId,
						width = Patches.GlobalControlsUtility_DoDate_Patch.ThreatForecastTooltipWidth,
						height = Patches.GlobalControlsUtility_DoDate_Patch.ThreatForecastTooltipHeight,
						expectedLabels = new[]
						{
							"ThreatForecast".Translate().ToString(),
							"Next14Days".Translate().ToString(),
							"Next4Quadrums".Translate().ToString()
						}
					}
				};
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
				if (spawnedZombie != null)
				{
					_ = tickManager.allZombiesCached?.Remove(spawnedZombie);
					_ = tickManager.hummingZombies?.Remove(spawnedZombie);
					_ = tickManager.tankZombies?.Remove(spawnedZombie);
				}
				foreach (var zombie in CurrentZombies(map).Where(zombie => initialIds.Contains(ZombieRuntimeActions.StableThingId(zombie)) == false).ToArray())
				{
					_ = tickManager.allZombiesCached?.Remove(zombie as Zombie);
					if (zombie.Destroyed == false)
						zombie.Destroy(DestroyMode.Vanish);
				}
			}
		}

		static object RunSpawnModeContracts(Map map)
		{
			var tickManager = map.GetComponent<TickManager>();
			if (tickManager == null)
			{
				return new
				{
					success = false,
					error = "No Zombieland TickManager is attached to the current map."
				};
			}

			if (TryEnsureCapableIncidentColonists(map, 3, out _, out var colonistError) == false)
				return colonistError;

			var settingsSnapshot = SnapshotZombieSettings();
			var oldMapSpawnedTicks = tickManager.mapSpawnedTicks;
			var initialThingIds = map.listerThings.AllThings
				.Select(ZombieRuntimeActions.StableThingId)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
			var destroyedBefore = ZombieRuntimeActions.DestroyZombies(map);
			try
			{
				var allOverMapSpawnField = PrepareAllOverMapIncidentSpawnField(map);
				var darkSpawnField = PrepareDarkIncidentSpawnField(map);
				var allTimeAllOver = RunAmbientSpawnCase(
					map,
					tickManager,
					"all_time_all_over_soft_ground",
					SpawnWhenType.AllTheTime,
					SpawnHowType.AllOverTheMap,
					zombie => zombie != null
						&& zombie.Position.InBounds(map)
						&& map.terrainGrid.TerrainAt(zombie.Position) == TerrainDefOf.Soil
						&& map.terrainGrid.CanRemoveTopLayerAt(zombie.Position) == false,
					"Ambient all-over spawning must use valid non-floor soft ground.");
				var allTimeEdges = RunAmbientSpawnCase(
					map,
					tickManager,
					"all_time_from_edges",
					SpawnWhenType.AllTheTime,
					SpawnHowType.FromTheEdges,
					zombie => zombie != null
						&& (zombie.Position.x == 0
							|| zombie.Position.z == 0
							|| zombie.Position.x == map.Size.x - 1
							|| zombie.Position.z == map.Size.z - 1
							|| (zombie.Position.GetRoom(map)?.TouchesMapEdge ?? false)),
					"Ambient edge spawning must use an edge-reachable region.");
				var whenDark = RunAmbientSpawnCase(
					map,
					tickManager,
					"when_dark_all_over",
					SpawnWhenType.WhenDark,
					SpawnHowType.AllOverTheMap,
					zombie => zombie != null && map.IsDark(zombie.Position),
					"Dark-only spawning must use a cell with PsychGlow.Dark.");
				var eventOnly = RunAmbientSpawnCase(
					map,
					tickManager,
					"in_events_only_blocks_ambient",
					SpawnWhenType.InEventsOnly,
					SpawnHowType.AllOverTheMap,
					zombie => zombie == null,
					"Event-only spawning must block ambient population growth.",
					expectSpawn: false);

				var fogDoor = FoggedDoorSpawnsRoomZombies();
				var fogRemoval = FogBlockerRemovalSpawnsRoomZombies();
				var fogReplacement = FogBlockerReplacementDoesNotSpawnRoomZombies();
				var ambientCases = new[] { allTimeAllOver, allTimeEdges, whenDark, eventOnly };
				var fogCases = new[] { fogDoor, fogRemoval, fogReplacement };
				return new
				{
					success = ObjectSuccess(allOverMapSpawnField)
						&& ObjectSuccess(darkSpawnField)
						&& ambientCases.All(ObjectSuccess)
						&& fogCases.All(ObjectSuccess),
					sourcePath = "TickManager.IncreaseZombiePopulation + Tools.ZombieSpawnLocator + fog-room spawn hooks",
					destroyedBefore,
					fixtures = new
					{
						allOverMapSpawnField,
						darkSpawnField
					},
					ambientCases,
					fogCases
				};
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
				tickManager.mapSpawnedTicks = oldMapSpawnedTicks;
				_ = ZombieRuntimeActions.DestroyZombies(map);
				CleanupThingsCreatedAfter(map, initialThingIds);
			}
		}

		static int CleanupThingsCreatedAfter(Map map, HashSet<string> initialThingIds)
		{
			var destroyed = 0;
			foreach (var thing in map.listerThings.AllThings.ToArray())
			{
				if (thing == null || thing.Destroyed)
					continue;
				var id = ZombieRuntimeActions.StableThingId(thing);
				if (initialThingIds.Contains(id))
					continue;
				thing.Destroy(DestroyMode.Vanish);
				destroyed++;
			}
			if (destroyed > 0)
			{
				map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
				Tools.nextPlayerReachableRegionsUpdate = 0;
			}
			return destroyed;
		}

		static object RunStoryEventDeliveryContract(Map map)
		{
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			var faction = FindStoryEventDeliveryFaction();
			if (faction == null)
			{
				return new
				{
					success = false,
					error = "No non-player humanlike faction was found for the storyteller delivery fixture."
				};
			}

			var initialThingIds = map.listerThings.AllThings
				.Select(ZombieRuntimeActions.StableThingId)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
			var settingsSnapshot = SnapshotZombieSettings();
			try
			{
				ApplyZombieSettingsOverride(settings =>
				{
					settings.attackMode = AttackMode.OnlyHumans;
					settings.enemiesAttackZombies = true;
					settings.zombiesDieOnZeroThreat = false;
				});

				var dropSpot = RunSafeDropSpotZombieFilterProbe(map, faction);
				var activeThreat = RunAnyHostileActiveThreatFilterProbe(map, faction);
				var corpseWealth = RunZombieCorpseWealthFilterProbe(map);
				var storyDanger = RunHomeAreaZombieStoryDangerProbe(map);
				var patchTargets = new
				{
					dropCellFinder = PatchedMethodsForPatchClass("DropCellFinder_IsSafeDropSpot_Patch"),
					targetsHostileToFaction = PatchedMethodsForPatchClass("AttackTargetsCache_TargetsHostileToFaction_Patch"),
					wealthItems = PatchedMethodsForPatchClass("WealthWatcher_CalculateWealthItems_Patch"),
					wealthItemsFilter = PatchedMethodsForPatchClass("WealthWatcher_WealthItemsFilter_Patch"),
					dangerRating = PatchedMethodsForPatchClass("DangerWatcher_CalculateDangerRating_Patch")
				};
				return new
				{
					success = ObjectSuccess(dropSpot)
						&& ObjectSuccess(activeThreat)
						&& ObjectSuccess(corpseWealth)
						&& ObjectSuccess(storyDanger),
					faction = new
					{
						faction.def?.defName,
						faction.Name,
						faction.def?.humanlikeFaction,
						hostileToPlayer = faction.HostileTo(Faction.OfPlayer)
					},
					patchTargets,
					dropSpot,
					activeThreat,
					corpseWealth,
					storyDanger
				};
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
				CleanupThingsCreatedAfter(map, initialThingIds);
				ForceRaidCadenceWealthRecount(map);
			}
		}

		static Faction FindStoryEventDeliveryFaction()
		{
			return Find.FactionManager.AllFactions
				.Where(faction => faction != null)
				.Where(faction => faction != Faction.OfPlayer)
				.Where(faction => faction.def != ZombieDefOf.Zombies)
				.Where(faction => faction.def?.humanlikeFaction == true)
				.OrderBy(faction => faction.HostileTo(Faction.OfPlayer))
				.ThenBy(faction => faction.def?.defName)
				.FirstOrDefault();
		}

		static object RunSafeDropSpotZombieFilterProbe(Map map, Faction faction)
		{
			var method = SafeDropSpotMethod();
			if (method == null)
			{
				return new
				{
					success = false,
					error = "Could not reflect DropCellFinder.IsSafeDropSpot."
				};
			}
			if (TryFindSafeDropProbeCells(map, faction, method, out var dropCell, out var zombieCell, out var cellError) == false)
				return cellError;

			var safeBefore = InvokeSafeDropSpot(method, dropCell, map, faction, out var safeBeforeError);
			var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
			if (zombie == null)
			{
				return new
				{
					success = false,
					dropCell = ZombieRuntimeActions.DescribeCell(dropCell),
					zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
					error = "Could not spawn the drop safety zombie fixture."
				};
			}
			map.attackTargetsCache.UpdateTarget(zombie);
			var zombieHostileToFaction = zombie.HostileTo(faction);
			var filteredZombielandHostiles = map.attackTargetsCache.TargetsHostileToFaction(faction).Count(StorytellerEventFilters.IsZombielandAttackTarget);
			var safeNearZombie = InvokeSafeDropSpot(method, dropCell, map, faction, out var safeNearZombieError);
			return new
			{
				success = safeBefore == true
					&& safeNearZombie == true
					&& zombieHostileToFaction
					&& filteredZombielandHostiles == 0
					&& safeBeforeError == null
					&& safeNearZombieError == null,
				dropCell = ZombieRuntimeActions.DescribeCell(dropCell),
				zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
				distance = dropCell.DistanceTo(zombieCell),
				zombieHostileToFaction,
				filteredZombielandHostiles,
				safeBefore,
				safeNearZombie,
				safeBeforeError,
				safeNearZombieError,
				zombie = DescribeZombie(zombie)
			};
		}

		static object RunAnyHostileActiveThreatFilterProbe(Map map, Faction faction)
		{
			if (TryFindClearSpawnCell(map, new IntVec3(map.Size.x / 2, 0, map.Size.z / 2) + new IntVec3(8, 0, 0), 24f, out var cell, out var cellError) == false)
				return cellError;

			var zombie = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.Normal, true);
			if (zombie == null)
			{
				return new
				{
					success = false,
					cell = ZombieRuntimeActions.DescribeCell(cell),
					error = "Could not spawn the active-threat zombie fixture."
				};
			}
			map.attackTargetsCache.UpdateTarget(zombie);
			var zombieHostileToFaction = zombie.HostileTo(faction);
			var filteredZombielandHostiles = map.attackTargetsCache.TargetsHostileToFaction(faction).Count(StorytellerEventFilters.IsZombielandAttackTarget);
			var aggregateThreat = GenHostility.AnyHostileActiveThreatTo(map, faction, out var threat, countDormantPawnsAsHostile: true);
			return new
			{
				success = zombieHostileToFaction
					&& filteredZombielandHostiles == 0
					&& aggregateThreat == false
					&& StorytellerEventFilters.IsZombielandAttackTarget(threat) == false,
				zombieHostileToFaction,
				filteredZombielandHostiles,
				aggregateThreat,
				threat = threat?.Thing == null ? null : new
				{
					id = ZombieRuntimeActions.StableThingId(threat.Thing),
					threat.Thing.def?.defName,
					label = threat.Thing.LabelCap
				},
				zombie = DescribeZombie(zombie)
			};
		}

		static object RunZombieCorpseWealthFilterProbe(Map map)
		{
			ForceRaidCadenceWealthRecount(map);
			var wealthBefore = ReadFloatMember(map.wealthWatcher, "WealthItems");
			if (TryFindClearSpawnCell(map, new IntVec3(map.Size.x / 2, 0, map.Size.z / 2) + new IntVec3(-8, 0, 0), 24f, out var cell, out var cellError) == false)
				return cellError;

			var zombie = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.Normal, true);
			if (zombie == null)
			{
				return new
				{
					success = false,
					cell = ZombieRuntimeActions.DescribeCell(cell),
					error = "Could not spawn the corpse wealth zombie fixture."
				};
			}
			zombie.Kill(null);
			AdvanceGameTicks(1);
			var corpse = zombie.Corpse as Corpse
				?? map.listerThings.AllThings.OfType<Corpse>().OrderBy(thing => thing.Position.DistanceToSquared(cell)).FirstOrDefault();
			ForceRaidCadenceWealthRecount(map);
			var wealthAfter = ReadFloatMember(map.wealthWatcher, "WealthItems");
			var corpseWealth = corpse == null ? 0f : corpse.MarketValue * corpse.stackCount;
			return new
			{
				success = corpse != null
					&& StorytellerEventFilters.IsZombielandCorpse(corpse)
					&& Mathf.Abs(wealthAfter - wealthBefore) <= 0.5f,
				wealthBefore,
				wealthAfter,
				delta = wealthAfter - wealthBefore,
				corpseWealth,
				corpse = DescribeCorpse(corpse)
			};
		}

		static object RunHomeAreaZombieStoryDangerProbe(Map map)
		{
			var method = AccessTools.Method(typeof(DangerWatcher), "CalculateDangerRating");
			if (method == null)
			{
				return new
				{
					success = false,
					error = "Could not reflect DangerWatcher.CalculateDangerRating."
				};
			}
			if (TryFindClearSpawnCell(map, new IntVec3(map.Size.x / 2, 0, map.Size.z / 2) + new IntVec3(0, 0, 8), 24f, out var cell, out var cellError) == false)
				return cellError;

			var cells = GenRadial.RadialCellsAround(cell, 8f, true)
				.Where(candidate => candidate.InBounds(map))
				.Where(candidate => candidate.Standable(map))
				.Where(candidate => candidate.Fogged(map) == false)
				.Where(candidate => candidate.GetFirstPawn(map) == null)
				.Take(3)
				.ToArray();
			if (cells.Length < 3)
			{
				return new
				{
					success = false,
					cell = ZombieRuntimeActions.DescribeCell(cell),
					foundCells = cells.Length,
					error = "Could not find three clear story-danger zombie fixture cells."
				};
			}

			var originalHome = cells.ToDictionary(candidate => candidate, candidate => map.areaManager.Home[candidate]);
			try
			{
				var dangerBefore = InvokeCalculateDangerRating(map, method, out var dangerBeforeError);
				var zombies = new List<Zombie>();
				foreach (var candidate in cells)
				{
					map.areaManager.Home[candidate] = true;
					var zombie = ZombieRuntimeActions.SpawnZombie(candidate, map, ZombieType.Normal, true);
					if (zombie == null)
					{
						return new
						{
							success = false,
							cell = ZombieRuntimeActions.DescribeCell(candidate),
							error = "Could not spawn the story-danger zombie fixture."
						};
					}
					zombies.Add(zombie);
					map.attackTargetsCache.UpdateTarget(zombie);
				}
				var rawColonyZombielandHostiles = map.attackTargetsCache.TargetsHostileToColony.Count(StorytellerEventFilters.IsZombielandAttackTarget);
				var dangerAfter = InvokeCalculateDangerRating(map, method, out var dangerAfterError);
				map.dangerWatcher.Notify_ColonistHarmedExternally();
				var dangerAfterHarm = InvokeCalculateDangerRating(map, method, out var dangerAfterHarmError);
				var zombieCombatPower = zombies.Sum(zombie => zombie.kindDef?.combatPower ?? 0f);
				var affectsStoryDanger = zombies.All(StorytellerEventFilters.AffectsStoryDanger);
				return new
				{
					success = affectsStoryDanger
						&& rawColonyZombielandHostiles == 0
						&& zombieCombatPower > 150f
						&& zombieCombatPower < 400f
						&& dangerAfter == StoryDanger.Low
						&& dangerAfterHarm == StoryDanger.High
						&& dangerBeforeError == null
						&& dangerAfterError == null
						&& dangerAfterHarmError == null,
					cells = cells.Select(ZombieRuntimeActions.DescribeCell).ToArray(),
					zombieCombatPower,
					originalHome = originalHome.Select(entry => new { cell = ZombieRuntimeActions.DescribeCell(entry.Key), value = entry.Value }).ToArray(),
					homeAfterSetup = cells.All(candidate => map.areaManager.Home[candidate]),
					rawColonyZombielandHostiles,
					affectsStoryDanger,
					dangerBefore = dangerBefore.ToString(),
					dangerAfter = dangerAfter.ToString(),
					dangerAfterHarm = dangerAfterHarm.ToString(),
					dangerBeforeError,
					dangerAfterError,
					dangerAfterHarmError,
					zombies = zombies.Select(DescribeZombie).ToArray()
				};
			}
			finally
			{
				foreach (var entry in originalHome)
					map.areaManager.Home[entry.Key] = entry.Value;
			}
		}

		static MethodInfo SafeDropSpotMethod()
		{
			return StorytellerEventFilterSupport.SafeDropSpotMethod();
		}

		static bool TryFindSafeDropProbeCells(Map map, Faction faction, MethodInfo safeDropSpotMethod, out IntVec3 dropCell, out IntVec3 zombieCell, out object error)
		{
			dropCell = IntVec3.Invalid;
			zombieCell = IntVec3.Invalid;
			error = null;
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			foreach (var candidate in GenRadial.RadialCellsAround(root, 72f, true))
			{
				if (candidate.InBounds(map) == false || candidate.Standable(map) == false || candidate.Fogged(map))
					continue;
				if (InvokeSafeDropSpot(safeDropSpotMethod, candidate, map, faction, out _) == false)
					continue;
				var nearZombieCell = GenRadial.RadialCellsAround(candidate, 12f, false)
					.Where(cell => cell.InBounds(map))
					.Where(cell => cell.Standable(map))
					.Where(cell => cell.Fogged(map) == false)
					.Where(cell => cell.GetFirstPawn(map) == null)
					.Where(cell => cell.DistanceTo(candidate) >= 4f && cell.DistanceTo(candidate) <= 12f)
					.OrderBy(cell => cell.DistanceToSquared(candidate))
					.FirstOrDefault();
				if (nearZombieCell.IsValid == false)
					continue;
				dropCell = candidate;
				zombieCell = nearZombieCell;
				return true;
			}

			error = new
			{
				success = false,
				root = ZombieRuntimeActions.DescribeCell(root),
				error = "No baseline-safe drop cell with a nearby zombie spawn cell was found."
			};
			return false;
		}

		static bool InvokeSafeDropSpot(MethodInfo method, IntVec3 cell, Map map, Faction faction, out string error)
		{
			error = null;
			try
			{
				return (bool)method.Invoke(null, new object[] { cell, map, faction, null, 0, 35, 0, null });
			}
			catch (TargetInvocationException ex)
			{
				error = ex.InnerException?.Message ?? ex.Message;
				return false;
			}
			catch (Exception ex)
			{
				error = ex.Message;
				return false;
			}
		}

		static StoryDanger InvokeCalculateDangerRating(Map map, MethodInfo method, out string error)
		{
			error = null;
			try
			{
				return (StoryDanger)method.Invoke(map.dangerWatcher, Array.Empty<object>());
			}
			catch (TargetInvocationException ex)
			{
				error = ex.InnerException?.Message ?? ex.Message;
				return StoryDanger.None;
			}
			catch (Exception ex)
			{
				error = ex.Message;
				return StoryDanger.None;
			}
		}

		static object RunRaidWorkerContract(Map map)
		{
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			var zombieFaction = Find.FactionManager.FirstFactionOfDef(ZombieDefOf.Zombies);
			var worker = IncidentDefOf.RaidEnemy?.Worker as IncidentWorker_Raid;
			var raidWorkerMethod = typeof(IncidentWorker_Raid).GetMethod("TryExecuteWorker", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			var tryExecuteMethod = typeof(ZombiesRising).GetMethod(nameof(ZombiesRising.TryExecute), BindingFlags.Static | BindingFlags.Public);
			var probePrefix = typeof(ZombielandBridgeTools).GetMethod(nameof(RaidWorkerTryExecutePrefix), BindingFlags.Static | BindingFlags.NonPublic);
			if (zombieFaction == null || worker == null || raidWorkerMethod == null || tryExecuteMethod == null || probePrefix == null)
			{
				return new
				{
					success = false,
					error = "Could not resolve zombie faction, raid worker, raid worker method, or TryExecute probe method.",
					zombieFactionFound = zombieFaction != null,
					workerType = worker?.GetType().FullName,
					raidWorkerMethodFound = raidWorkerMethod != null,
					tryExecuteMethodFound = tryExecuteMethod != null,
					probePrefixFound = probePrefix != null
				};
			}

			var patchInfo = Harmony.GetPatchInfo(raidWorkerMethod);
			var prefixOwners = patchInfo?.Prefixes?.Select(patch => patch.owner).ToArray() ?? Array.Empty<string>();
			var zPatch = prefixOwners.Contains("net.pardeike.zombieland");
			if (zPatch == false)
			{
				return new
				{
					success = false,
					error = "Zombieland raid worker prefix is not installed on IncidentWorker_Raid.TryExecuteWorker.",
					prefixOwners
				};
			}

			var settingsSnapshot = SnapshotZombieSettings();
			var initialThingIds = map.listerThings.AllThings
				.Select(ZombieRuntimeActions.StableThingId)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
			var beforeLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
				.ToHashSet();
			var harmony = new Harmony("net.pardeike.zombieland.bridge.raidworker.probe");
			raidWorkerTryExecuteProbes.Clear();
			activeRaidWorkerTryExecuteCase = null;
			try
			{
				var allOverMapSpawnField = PrepareAllOverMapIncidentSpawnField(map);
				harmony.Patch(tryExecuteMethod, prefix: new HarmonyMethod(probePrefix) { priority = Priority.First });

				var edgeWalkIn = RunRaidWorkerCase(
					worker,
					raidWorkerMethod,
					map,
					zombieFaction,
					"edge_walk_in",
					PawnsArrivalModeDefOf.EdgeWalkIn,
					SpawnHowType.AllOverTheMap,
					SpawnHowType.FromTheEdges,
					83101);
				var centerDrop = RunRaidWorkerCase(
					worker,
					raidWorkerMethod,
					map,
					zombieFaction,
					"center_drop",
					PawnsArrivalModeDefOf.CenterDrop,
					SpawnHowType.FromTheEdges,
					SpawnHowType.AllOverTheMap,
					83102);

				var newZombies = CurrentZombies(map)
					.Where(zombie => initialThingIds.Contains(ZombieRuntimeActions.StableThingId(zombie)) == false)
					.ToArray();
				var newLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
					.Where(letter => beforeLetters.Contains(letter) == false)
					.ToArray();
				return new
				{
					success = ObjectSuccess(allOverMapSpawnField)
						&& ObjectSuccess(edgeWalkIn)
						&& ObjectSuccess(centerDrop)
						&& newZombies.Length == 0
						&& newLetters.Length == 0,
					sourcePath = "IncidentWorker_Raid.TryExecuteWorker prefix -> ZombiesRising.TryExecute, with temporary bridge probe preventing coroutine side effects",
					prefixOwners,
					fixtures = new
					{
						allOverMapSpawnField
					},
					cases = new[] { edgeWalkIn, centerDrop },
					newZombieCount = newZombies.Length,
					newLetterCount = newLetters.Length
				};
			}
			finally
			{
				harmony.Unpatch(tryExecuteMethod, HarmonyPatchType.Prefix, harmony.Id);
				activeRaidWorkerTryExecuteCase = null;
				raidWorkerTryExecuteProbes.Clear();
				RestoreZombieSettings(settingsSnapshot);
				CleanupThingsCreatedAfter(map, initialThingIds);
			}
		}

		static object RunRaidCadenceContract(Map map)
		{
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			var manager = ZombieFreeEventManager.Current;
			if (manager == null)
			{
				return new
				{
					success = false,
					error = "The current game has no ZombieFreeEventManager."
				};
			}

			if (TrySnapshotZombieFreeSchedule(manager, out var scheduleSnapshot, out var scheduleSnapshotError) == false)
			{
				return new
				{
					success = false,
					error = scheduleSnapshotError
				};
			}

			var settingsSnapshot = SnapshotZombieSettings();
			var initialThingIds = map.listerThings.AllThings
				.Select(ZombieRuntimeActions.StableThingId)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
			var spawned = new List<Pawn>();
			var spawnErrors = new List<string>();
			try
			{
				ApplyZombieSettingsOverride(settings =>
				{
					settings.zombieFreeEvents = true;
					settings.useDynamicThreatLevel = false;
					settings.daysBeforeZombiesCome = 0;
					settings.zombiesDieOnZeroThreat = false;
				});

				var baseline = DescribeRaidCadenceSnapshot(map, "baseline", initialThingIds);
				if (TrySpawnRaidCadenceZombies(map, spawned, spawnErrors) == false)
				{
					return new
					{
						success = false,
						error = "Could not spawn the controlled normal/spitter/symbiant raid-cadence fixture.",
						spawnErrors = spawnErrors.ToArray(),
						baseline
					};
				}
				foreach (var pawn in spawned)
					map.attackTargetsCache.UpdateTarget(pawn);

				var liveZombies = DescribeRaidCadenceSnapshot(map, "liveZombies", initialThingIds);

				var forcedWindow = manager.DebugForceWindowStartingNow(GenDate.TicksPerDay);
				manager.DebugRefreshCurrentWindowState();
				var silence = DescribeRaidCadenceSnapshot(map, "zombieSilence", initialThingIds);

				foreach (var pawn in spawned.Where(pawn => pawn != null && pawn.Destroyed == false && pawn.Dead == false).ToArray())
					pawn.Kill(null);
				AdvanceGameTicks(1);
				ForceRaidCadenceWealthRecount(map);
				var postCorpse = DescribeRaidCadenceSnapshot(map, "postZombieCorpse", initialThingIds);

				var liveMatchesBaseline = RaidCadenceEquivalent(baseline, liveZombies, false);
				var silenceMatchesBaseline = RaidCadenceEquivalent(baseline, silence, false);
				var postCorpseMatchesBaseline = RaidCadenceEquivalent(baseline, postCorpse, true);
				var proposalsMatch = RaidCadenceProposalEquivalent(baseline.proposalSample, liveZombies.proposalSample)
					&& RaidCadenceProposalEquivalent(baseline.proposalSample, silence.proposalSample)
					&& RaidCadenceProposalEquivalent(baseline.proposalSample, postCorpse.proposalSample);
				var eventDelivery = RunStoryEventDeliveryContract(map);
				var eventDeliverySuccess = ObjectSuccess(eventDelivery);

				return new
				{
					success = spawned.Count == 3
						&& spawnErrors.Count == 0
						&& liveZombies.zombielandHostileTargetCount == 0
						&& silence.zombielandHostileTargetCount == 0
						&& liveMatchesBaseline
						&& silenceMatchesBaseline
						&& postCorpseMatchesBaseline
						&& proposalsMatch
						&& eventDeliverySuccess
						&& ZombieFreeEventManager.IsActiveNow()
						&& silence.zombieThreatLevel == 0f,
					sourcePath = "AttackTargetsCache.TargetsHostileToColony postfix + delivery-side DropCellFinder/GenHostility/WealthWatcher/DangerWatcher filters + Storyteller raid input readback during forced ZombieFreeEventManager window",
					expectation = "Controlled live zombies, zombie silence, and the corpses/drops left by silenced zombies must not raise vanilla RaidEnemy CanFireNow, DefaultThreatPointsNow, or deterministic Storyteller interval proposal counts, while delivery-side gates ignore zombies where they should and home-area zombies still raise story danger.",
					forcedWindow = new
					{
						offsetStartTicks = forcedWindow.startTick - GenTicks.TicksGame,
						offsetEndTicks = forcedWindow.endTick - GenTicks.TicksGame,
						forcedWindow.DurationTicks
					},
					spawned = spawned.Select(DescribeZombie).ToArray(),
					spawnErrors = spawnErrors.ToArray(),
					comparisons = new
					{
						liveMatchesBaseline,
						silenceMatchesBaseline,
						postCorpseMatchesBaseline,
						proposalsMatch,
						eventDeliverySuccess
					},
					baseline,
					liveZombies,
					silence,
					postCorpse,
					eventDelivery
				};
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
				RestoreZombieFreeSchedule(manager, scheduleSnapshot);
				CleanupThingsCreatedAfter(map, initialThingIds);
				ForceRaidCadenceWealthRecount(map);
			}
		}

		static bool TrySpawnRaidCadenceZombies(Map map, List<Pawn> spawned, List<string> errors)
		{
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var normalCell, out var normalSpawnError) == false)
			{
				errors.Add($"normal: {normalSpawnError}");
				return false;
			}
			if (TryFindClearSpawnCell(map, normalCell + new IntVec3(3, 0, 0), 8f, out var spitterCell, out var spitterSpawnError) == false)
			{
				errors.Add($"spitter: {spitterSpawnError}");
				return false;
			}
			if (TryFindClearSpawnCell(map, normalCell + new IntVec3(6, 0, 0), 10f, out var symbiantCell, out var symbiantSpawnError) == false)
			{
				errors.Add($"symbiant: {symbiantSpawnError}");
				return false;
			}

			var normal = SpawnFireFixturePawn(map, normalCell, "normal");
			var spitter = SpawnFireFixturePawn(map, spitterCell, "spitter");
			var symbiant = SpawnFireFixturePawn(map, symbiantCell, "symbiant");
			if (normal is Zombie == false)
				errors.Add("Normal zombie fixture did not spawn as Zombie.");
			if (spitter is ZombieSpitter == false)
				errors.Add("Spitter fixture did not spawn as ZombieSpitter.");
			if (symbiant is ZombieSymbiant == false)
				errors.Add("Symbiant fixture did not spawn as ZombieSymbiant.");

			foreach (var pawn in new[] { normal, spitter, symbiant }.Where(pawn => pawn != null))
			{
				pawn.Name = new NameSingle($"ZL Raid Cadence {DescribeZombieKind(pawn as Zombie, pawn as ZombieSymbiant, pawn as ZombieSpitter)}");
				spawned.Add(pawn);
			}
			return errors.Count == 0;
		}

		static RaidCadenceSnapshot DescribeRaidCadenceSnapshot(Map map, string phase, HashSet<string> initialThingIds)
		{
			ForceRaidCadenceWealthRecount(map);
			var parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, map);
			var raidWorker = IncidentDefOf.RaidEnemy?.Worker;
			var raidCanFire = false;
			string raidCanFireError = null;
			try
			{
				raidCanFire = raidWorker?.CanFireNow(parms) ?? false;
			}
			catch (Exception ex)
			{
				raidCanFireError = $"{ex.GetType().Name}: {ex.Message}";
			}

			var hostileTargets = map.attackTargetsCache.TargetsHostileToColony;
			var zombielandTargets = hostileTargets
				.Select(target => target.Thing)
				.Where(thing => thing is Zombie || thing is ZombieSpitter || thing is ZombieSymbiant)
				.ToArray();
			var wealthWatcher = map.wealthWatcher;
			return new RaidCadenceSnapshot
			{
				phase = phase,
				ticksGame = GenTicks.TicksGame,
				defaultThreatPoints = StorytellerUtility.DefaultThreatPointsNow(map),
				raidCanFire = raidCanFire,
				raidCanFireError = raidCanFireError,
				hostileTargetCount = hostileTargets.Count,
				zombielandHostileTargetCount = zombielandTargets.Length,
				dangerRating = map.dangerWatcher?.DangerRating.ToString(),
				zombieThreatLevel = ZombieWeather.GetThreatLevel(map),
				zombieFreeActive = ZombieFreeEventManager.IsActiveNow(),
				wealthItems = ReadFloatMember(wealthWatcher, "WealthItems"),
				wealthBuildings = ReadFloatMember(wealthWatcher, "WealthBuildings"),
				wealthPawns = ReadFloatMember(wealthWatcher, "WealthPawns"),
				wealthTotal = ReadFloatMember(wealthWatcher, "WealthTotal"),
				proposalSample = DescribeRaidCadenceProposalSample(240, 62071),
				hostileTargets = new
				{
					count = hostileTargets.Count,
					zombielandCount = zombielandTargets.Length,
					zombielandTargets = zombielandTargets.Select(thing => new
					{
						id = ZombieRuntimeActions.StableThingId(thing),
						thing.def?.defName,
						label = thing.LabelCap
					}).ToArray(),
					firstTargets = hostileTargets.Take(12).Select(target => new
					{
						id = ZombieRuntimeActions.StableThingId(target.Thing),
						defName = target.Thing?.def?.defName,
						label = target.Thing?.LabelCap
					}).ToArray()
				},
				storyteller = DescribeRaidCadenceStoryteller(map, parms),
				corpsesAndDrops = DescribeRaidCadenceNewThings(map, initialThingIds)
			};
		}

		static object DescribeRaidCadenceStoryteller(Map map, IncidentParms parms)
		{
			var storyState = ReadMember(map, "StoryState");
			return new
			{
				storyteller = Find.Storyteller?.def?.defName,
				incidentCategory = IncidentCategoryDefOf.ThreatBig?.defName,
				raidDef = IncidentDefOf.RaidEnemy?.defName,
				parmsPoints = parms?.points ?? 0f,
				parmsTarget = parms?.target?.GetType().Name,
				lastThreatBigTick = ReadIntMember(storyState, "LastThreatBigTick"),
				recentRandomIncidentCount = ReadCollectionCount(ReadMember(storyState, "RecentRandomIncidents"))
			};
		}

		static object DescribeRaidCadenceNewThings(Map map, HashSet<string> initialThingIds)
		{
			var newThings = map.listerThings.AllThings
				.Where(thing => thing != null && thing.Destroyed == false && initialThingIds.Contains(ZombieRuntimeActions.StableThingId(thing)) == false)
				.ToArray();
			return new
			{
				newThingCount = newThings.Length,
				zombieCorpseCount = newThings
					.OfType<Corpse>()
					.Count(corpse => corpse.InnerPawn is Zombie || corpse.InnerPawn is ZombieSpitter || corpse.InnerPawn is ZombieSymbiant),
				zombieCorpses = newThings
					.OfType<Corpse>()
					.Where(corpse => corpse.InnerPawn is Zombie || corpse.InnerPawn is ZombieSpitter || corpse.InnerPawn is ZombieSymbiant)
					.Select(DescribeCorpse)
					.ToArray(),
				otherDrops = newThings
					.Where(thing => thing is not Pawn && thing is not Corpse)
					.Select(thing => new
					{
						id = ZombieRuntimeActions.StableThingId(thing),
						thing.def?.defName,
						label = thing.LabelCap,
						thing.stackCount,
						marketValue = thing.MarketValue,
						position = thing.Spawned ? ZombieRuntimeActions.DescribeCell(thing.Position) : null
					})
					.ToArray()
			};
		}

		static RaidCadenceProposalSnapshot DescribeRaidCadenceProposalSample(int sampleCount, int seed)
		{
			var snapshot = new RaidCadenceProposalSnapshot
			{
				sampleCount = sampleCount,
				incidentDefs = Array.Empty<string>()
			};
			var storyteller = Find.Storyteller;
			if (storyteller == null)
			{
				snapshot.error = "No active storyteller.";
				return snapshot;
			}

			var method = storyteller.GetType().GetMethod("MakeIncidentsForInterval", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
			if (method == null)
			{
				snapshot.error = "Could not resolve Storyteller.MakeIncidentsForInterval().";
				return snapshot;
			}

			var incidents = new List<IncidentDef>();
			Rand.PushState(seed);
			try
			{
				for (var i = 0; i < sampleCount; i++)
				{
					if (method.Invoke(storyteller, Array.Empty<object>()) is not System.Collections.IEnumerable enumerable)
						continue;
					foreach (var firingIncident in enumerable)
					{
						var incidentDef = ReadMember(firingIncident, "def") as IncidentDef;
						if (incidentDef != null)
							incidents.Add(incidentDef);
					}
				}
			}
			catch (Exception ex)
			{
				snapshot.error = $"{ex.GetType().Name}: {ex.Message}";
			}
			finally
			{
				Rand.PopState();
			}

			snapshot.totalProposals = incidents.Count;
			snapshot.raidEnemyCount = incidents.Count(def => def == IncidentDefOf.RaidEnemy);
			snapshot.threatBigCount = incidents.Count(def => def.category == IncidentCategoryDefOf.ThreatBig);
			snapshot.incidentDefs = incidents
				.Select(def => def.defName)
				.GroupBy(defName => defName)
				.OrderByDescending(group => group.Count())
				.ThenBy(group => group.Key)
				.Select(group => $"{group.Key}:{group.Count()}")
				.ToArray();
			return snapshot;
		}

		static bool RaidCadenceEquivalent(RaidCadenceSnapshot baseline, RaidCadenceSnapshot sample, bool allowCorpseOnlyNoise)
		{
			if (baseline == null || sample == null)
				return false;
			var threatPointTolerance = allowCorpseOnlyNoise ? 0.5f : 0.05f;
			var wealthTolerance = allowCorpseOnlyNoise ? 0.5f : 0.05f;
			return baseline.raidCanFire == sample.raidCanFire
				&& sample.raidCanFireError == null
				&& Mathf.Abs(baseline.defaultThreatPoints - sample.defaultThreatPoints) <= threatPointTolerance
				&& Mathf.Abs(baseline.wealthTotal - sample.wealthTotal) <= wealthTolerance;
		}

		static bool RaidCadenceProposalEquivalent(RaidCadenceProposalSnapshot baseline, RaidCadenceProposalSnapshot sample)
		{
			if (baseline == null || sample == null)
				return false;
			if (baseline.error != null || sample.error != null)
				return false;
			return baseline.totalProposals == sample.totalProposals
				&& baseline.raidEnemyCount == sample.raidEnemyCount
				&& baseline.threatBigCount == sample.threatBigCount
				&& baseline.incidentDefs.SequenceEqual(sample.incidentDefs);
		}

		static void ForceRaidCadenceWealthRecount(Map map)
		{
			var watcher = map?.wealthWatcher;
			if (watcher == null)
				return;

			var method = watcher.GetType()
				.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
				.Where(candidate => candidate.Name == "ForceRecount")
				.OrderBy(candidate => candidate.GetParameters().Length)
				.FirstOrDefault();
			if (method == null)
				return;

			var parameters = method.GetParameters()
				.Select(parameter => parameter.HasDefaultValue ? parameter.DefaultValue : parameter.ParameterType.IsValueType ? Activator.CreateInstance(parameter.ParameterType) : null)
				.ToArray();
			method.Invoke(watcher, parameters);
		}

		static object ReadMember(object instance, string name)
		{
			if (instance == null || string.IsNullOrEmpty(name))
				return null;
			var type = instance.GetType();
			var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (property != null)
				return property.GetValue(instance);
			var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			return field?.GetValue(instance);
		}

		static float ReadFloatMember(object instance, string name)
		{
			var value = ReadMember(instance, name);
			if (value == null)
				return 0f;
			try
			{
				return Convert.ToSingle(value);
			}
			catch
			{
				return 0f;
			}
		}

		static int ReadIntMember(object instance, string name)
		{
			var value = ReadMember(instance, name);
			if (value == null)
				return 0;
			try
			{
				return Convert.ToInt32(value);
			}
			catch
			{
				return 0;
			}
		}

		static int ReadCollectionCount(object instance)
		{
			if (instance == null)
				return 0;
			if (instance is System.Collections.ICollection collection)
				return collection.Count;
			if (instance is System.Collections.IEnumerable enumerable)
				return enumerable.Cast<object>().Count();
			return 0;
		}

		static object RunRaidWorkerCase(
			IncidentWorker_Raid worker,
			MethodInfo raidWorkerMethod,
			Map map,
			Faction zombieFaction,
			string caseName,
			PawnsArrivalModeDef arrivalMode,
			SpawnHowType sentinelMode,
			SpawnHowType expectedObservedMode,
			int seed)
		{
			var spawnCenter = IntVec3.Invalid;
			var parms = new IncidentParms
			{
				target = map,
				faction = zombieFaction,
				points = 8f,
				raidArrivalMode = arrivalMode,
				raidStrategy = RaidStrategyDefOf.ImmediateAttack,
				spawnCenter = spawnCenter
			};
			var beforeProbeCount = raidWorkerTryExecuteProbes.Count;
			var beforeMode = sentinelMode;
			ZombieSettings.Values.spawnHowType = beforeMode;
			activeRaidWorkerTryExecuteCase = caseName;
			object rawResult = null;
			Exception invocationException = null;
			Rand.PushState(seed);
			try
			{
				rawResult = raidWorkerMethod.Invoke(worker, new object[] { parms });
			}
			catch (TargetInvocationException ex)
			{
				invocationException = ex.InnerException ?? ex;
			}
			catch (Exception ex)
			{
				invocationException = ex;
			}
			finally
			{
				Rand.PopState();
				activeRaidWorkerTryExecuteCase = null;
			}

			var afterMode = ZombieSettings.Values.spawnHowType;
			var probes = raidWorkerTryExecuteProbes
				.Skip(beforeProbeCount)
				.ToArray();
			var probe = probes.SingleOrDefault();
			return new
			{
				success = invocationException == null
					&& rawResult is bool result
					&& result == false
					&& probes.Length == 1
					&& probe.observedSpawnHowType == expectedObservedMode.ToString()
					&& probe.incidentSize == Mathf.FloorToInt(parms.points)
					&& probe.useAlert == false
					&& probe.ignoreLimit == false
					&& afterMode == beforeMode,
				caseName,
				arrivalMode = arrivalMode?.defName,
				arrivalWalkIn = arrivalMode?.walkIn,
				points = parms.points,
				vanillaTryExecuteWorkerResult = rawResult,
				expectedResult = false,
				beforeSpawnHowType = beforeMode.ToString(),
				afterSpawnHowType = afterMode.ToString(),
				expectedObservedSpawnHowType = expectedObservedMode.ToString(),
				probeCount = probes.Length,
				probe,
				exception = invocationException?.ToString()
			};
		}

		static bool RaidWorkerTryExecutePrefix(
			Map map,
			int incidentSize,
			IntVec3 spot,
			bool useAlert,
			bool ignoreLimit,
			ZombieType zombieType,
			ref bool __result)
		{
			__result = true;
			raidWorkerTryExecuteProbes.Add(new RaidWorkerTryExecuteProbe
			{
				caseName = activeRaidWorkerTryExecuteCase,
				mapId = map?.uniqueID.ToString(),
				incidentSize = incidentSize,
				spot = spot.IsValid ? ZombieRuntimeActions.DescribeCell(spot) : null,
				useAlert = useAlert,
				ignoreLimit = ignoreLimit,
				zombieType = zombieType.ToString(),
				observedSpawnHowType = ZombieSettings.Values.spawnHowType.ToString(),
				forcedResult = __result
			});
			return false;
		}

		static object RunAmbientSpawnCase(
			Map map,
			TickManager tickManager,
			string name,
			SpawnWhenType spawnWhenType,
			SpawnHowType spawnHowType,
			Func<Zombie, bool> spawnedZombieValidator,
			string expectation,
			bool expectSpawn = true)
		{
			_ = ZombieRuntimeActions.DestroyZombies(map);
			var beforeIds = CurrentZombies(map)
				.Select(ZombieRuntimeActions.StableThingId)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
			ApplyZombieSettingsOverride(settings =>
			{
				settings.spawnWhenType = spawnWhenType;
				settings.spawnHowType = spawnHowType;
				settings.useDynamicThreatLevel = false;
				settings.daysBeforeZombiesCome = 0;
				settings.maximumNumberOfZombies = 500;
				settings.colonyMultiplier = 1f;
			});
			tickManager.mapSpawnedTicks = 0;
			SetPopulationSpawnCounter(tickManager, -1);

			Rand.PushState(50101 + name.GetHashCode());
			try
			{
				tickManager.IncreaseZombiePopulation();
			}
			finally
			{
				Rand.PopState();
			}

			var newZombies = CurrentZombies(map)
				.OfType<Zombie>()
				.Where(zombie => beforeIds.Contains(ZombieRuntimeActions.StableThingId(zombie)) == false)
				.ToArray();
			var spawnedZombie = newZombies.FirstOrDefault();
			var validatorMatched = spawnedZombieValidator(spawnedZombie);
			return new
			{
				name,
				success = expectSpawn
					? newZombies.Length == 1 && validatorMatched
					: newZombies.Length == 0 && validatorMatched,
				expectation,
				expectSpawn,
				spawnWhenType = spawnWhenType.ToString(),
				spawnHowType = spawnHowType.ToString(),
				spawnedCount = newZombies.Length,
				spawned = DescribeZombie(spawnedZombie),
				spawnCell = spawnedZombie == null ? null : DescribeSpawnCandidateCell(map, spawnedZombie.Position),
				validatorMatched
			};
		}

		static object PrepareDarkIncidentSpawnField(Map map)
		{
			var soil = TerrainDefOf.Soil ?? DefDatabase<TerrainDef>.GetNamed("Soil", false);
			if (soil == null)
			{
				return new
				{
					success = false,
					error = "TerrainDef Soil was not found."
				};
			}

			var center = new IntVec3(map.Size.x / 2, 0, Mathf.Min(map.Size.z - 16, map.Size.z / 2 + 36));
			var radius = 10;
			var changedTerrain = 0;
			var changedRoof = 0;
			for (var x = center.x - radius; x <= center.x + radius; x++)
			{
				for (var z = center.z - radius; z <= center.z + radius; z++)
				{
					var cell = new IntVec3(x, 0, z);
					if (cell.InBounds(map) == false || cell.GetEdifice(map) != null)
						continue;
					if (map.terrainGrid.TerrainAt(cell) != soil)
					{
						map.terrainGrid.SetTerrain(cell, soil);
						changedTerrain++;
					}
					if (map.roofGrid.RoofAt(cell) != RoofDefOf.RoofConstructed)
					{
						map.roofGrid.SetRoof(cell, RoofDefOf.RoofConstructed);
						changedRoof++;
					}
				}
			}
			map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
			Tools.nextPlayerReachableRegionsUpdate = 0;

			var oldSpawnWhenType = ZombieSettings.Values.spawnWhenType;
			var oldSpawnHowType = ZombieSettings.Values.spawnHowType;
			try
			{
				ZombieSettings.Values.spawnWhenType = SpawnWhenType.WhenDark;
				ZombieSettings.Values.spawnHowType = SpawnHowType.AllOverTheMap;
				var cellValidator = Tools.ZombieSpawnLocator(map);
				var reachableCells = Tools.PlayerReachableRegions(map)
					.SelectMany(region => region.Cells)
					.Distinct()
					.ToList();
				var darkValidCells = reachableCells
					.Where(cell => cellValidator(cell))
					.ToArray();
				return new
				{
					success = darkValidCells.Length > 0,
					center = ZombieRuntimeActions.DescribeCell(center),
					radius,
					changedTerrain,
					changedRoof,
					darkValidCellCount = darkValidCells.Length,
					sampleDarkCells = darkValidCells.Take(8).Select(cell => DescribeSpawnCandidateCell(map, cell)).ToArray()
				};
			}
			finally
			{
				ZombieSettings.Values.spawnWhenType = oldSpawnWhenType;
				ZombieSettings.Values.spawnHowType = oldSpawnHowType;
			}
		}

		static object RunZeroThreatDeathContract(Map map)
		{
			var tickManager = map.GetComponent<TickManager>();
			if (tickManager == null)
			{
				return new
				{
					success = false,
					error = "No Zombieland TickManager is attached to the current map."
				};
			}

			var settingsSnapshot = SnapshotZombieSettings();
			var spawnedZombies = new List<Zombie>();
			try
			{
				ApplyZombieSettingsOverride(settings =>
				{
					settings.useDynamicThreatLevel = true;
					settings.daysBeforeZombiesCome = Mathf.CeilToInt(GenDate.DaysPassedFloat) + 10;
					settings.zombiesDieOnZeroThreat = true;
				});
				var threatLevel = ZombieWeather.GetThreatLevel(map);
				var enabled = RunZeroThreatZombieTickSample(map, 88031, true, threatLevel, spawnedZombies);

				ApplyZombieSettingsOverride(settings => settings.zombiesDieOnZeroThreat = false);
				var disabled = RunZeroThreatZombieTickSample(map, 88031, false, threatLevel, spawnedZombies);

				return new
				{
					success = threatLevel <= 0.002f
						&& ObjectSuccess(enabled)
						&& ObjectSuccess(disabled),
					sourcePath = "Zombie.CustomTick zero-threat damage branch",
					threatLevel,
					enabled,
					disabled
				};
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
				foreach (var zombie in spawnedZombies.Distinct())
				{
					_ = tickManager.allZombiesCached?.Remove(zombie);
					if (zombie.Corpse != null && zombie.Corpse.Destroyed == false)
						zombie.Corpse.Destroy(DestroyMode.Vanish);
					if (zombie.Destroyed == false)
						zombie.Destroy(DestroyMode.Vanish);
				}
			}
		}

		static object RunZeroThreatZombieTickSample(Map map, int seed, bool expectDamage, float threatLevel, List<Zombie> spawnedZombies)
		{
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root + new IntVec3(expectDamage ? -8 : 8, 0, 0), 24f, out var cell, out var cellError) == false)
				return cellError;

			var zombie = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.Normal, true);
			if (zombie != null)
				spawnedZombies.Add(zombie);
			var injuryBefore = TotalInjurySeverity(zombie);
			var deadAtTick = -1;
			var injuryAfter = injuryBefore;
			Rand.PushState(seed);
			try
			{
				for (var tick = 0; tick < 60000; tick++)
				{
					zombie.CustomTick(threatLevel);
					injuryAfter = TotalInjurySeverity(zombie);
					if (zombie.Dead)
					{
						deadAtTick = tick;
						break;
					}
				}
			}
			finally
			{
				Rand.PopState();
			}

			var damaged = injuryAfter > injuryBefore + 0.001f;
			return new
			{
				success = expectDamage
					? damaged
					: damaged == false && zombie.Dead == false,
				expectDamage,
				cell = ZombieRuntimeActions.DescribeCell(cell),
				injuryBefore,
				injuryAfter,
				damageDelta = injuryAfter - injuryBefore,
				dead = zombie.Dead,
				deadAtTick,
				zombie = DescribeZombie(zombie)
			};
		}

		static bool ObjectSuccess(object result)
		{
			return result?.GetType().GetProperty("success")?.GetValue(result) is true;
		}

		static bool RectWithinScreen(Rect rect)
		{
			return rect.width > 0f
				&& rect.height > 0f
				&& rect.xMin >= 0f
				&& rect.yMin >= 0f
				&& rect.xMax <= UI.screenWidth
				&& rect.yMax <= UI.screenHeight;
		}

		static object DescribeRect(Rect rect)
		{
			return new
			{
				x = rect.x,
				y = rect.y,
				width = rect.width,
				height = rect.height,
				xMin = rect.xMin,
				xMax = rect.xMax,
				yMin = rect.yMin,
				yMax = rect.yMax
			};
		}

		static void SetPopulationSpawnCounter(TickManager tickManager, int value)
		{
			var field = typeof(TickManager).GetField("populationSpawnCounter", BindingFlags.Instance | BindingFlags.NonPublic);
			field?.SetValue(tickManager, value);
		}

		static ThreatForecastSnapshot DescribeThreatForecast(Map map)
		{
			var weather = map.GetComponent<ZombieWeather>();
			var currentThreat = ZombieWeather.GetThreatLevel(map);
			var (rangeMin, rangeMax) = weather.GetFactorRangeFor();
			var forecastStart = GenTicks.TicksAbs;
			var forecastEnd = forecastStart + GenDate.TicksPerDay * 14;
			return new ThreatForecastSnapshot
			{
				currentThreat = currentThreat,
				rangeMin = rangeMin,
				rangeMax = rangeMax,
				forecastLabel = FormatThreatForecast(rangeMin, rangeMax),
				samples = Enumerable.Range(0, 9)
					.Select(index =>
					{
						var ticks = GenTicks.TicksAbs + index * GenDate.TicksPerDay / 2;
						return new
						{
							offsetTicks = ticks - GenTicks.TicksAbs,
							threat = weather.GetFactorForTicks(ticks)
						};
					})
					.ToArray(),
				zombieFreeEvents = ZombieFreeEventManager.WindowsForAbsRange(forecastStart, forecastEnd)
					.Select(window => new
					{
						offsetStartTicks = ZombieFreeEventManager.AbsTickForGameTick(window.startTick) - GenTicks.TicksAbs,
						offsetEndTicks = ZombieFreeEventManager.AbsTickForGameTick(window.endTick) - GenTicks.TicksAbs,
						window.DurationTicks,
						window.startHandled,
						window.letterSent
					})
					.ToArray()
			};
		}

		static string FormatThreatForecast(float min, float max)
		{
			var n1 = Mathf.FloorToInt(min * 100);
			var n2 = Mathf.FloorToInt(max * 100);
			if (n1 == n2)
				return string.Format("{0:D0}%", n1) + " " + "ThreatLevel".Translate();
			return string.Format("{0:D0}-{1:D0}%", n1, n2) + " " + "ThreatLevel".Translate();
		}

		static object DescribeIncidentThreatState(Map map)
		{
			var tickManager = map.GetComponent<TickManager>();
			var lastIncidentField = typeof(IncidentInfo).GetField("lastIncident", BindingFlags.Instance | BindingFlags.NonPublic);
			var lastIncident = tickManager?.incidentInfo == null || lastIncidentField == null
				? (int?)null
				: (int)lastIncidentField.GetValue(tickManager.incidentInfo);
			var weather = map.GetComponent<ZombieWeather>();
			return new
			{
				tickManagerPresent = tickManager != null,
				ticksGame = Find.TickManager.TicksGame,
				ticksAbs = GenTicks.TicksAbs,
				settings = new
				{
					ZombieSettings.Values.spawnWhenType,
					ZombieSettings.Values.spawnHowType,
					ZombieSettings.Values.useDynamicThreatLevel,
					ZombieSettings.Values.zombiesDieOnZeroThreat,
					ZombieSettings.Values.zombieFreeEvents,
					ZombieSettings.Values.daysBeforeZombiesCome,
					ZombieSettings.Values.maximumNumberOfZombies,
					ZombieSettings.Values.baseNumberOfZombiesinEvent,
					ZombieSettings.Values.colonyMultiplier,
					ZombieSettings.Values.threatScale
				},
				colonists = DescribeColonistInfo(map),
				zombies = new
				{
					count = tickManager?.ZombieCount() ?? -1,
					liveCount = tickManager?.LiveZombieCount() ?? -1,
					maxCount = tickManager?.GetMaxZombieCount() ?? -1,
					canHaveMore = tickManager?.CanHaveMoreZombies() ?? false,
					spawning = ZombieGenerator.ZombiesSpawning
				},
				incident = new
				{
					lastIncident,
					parameters = tickManager?.incidentInfo?.parameters == null
						? null
						: DescribeIncidentParameters(tickManager.incidentInfo.parameters)
				},
				threat = weather == null ? null : DescribeThreatForecast(map),
				spawnDiagnostics = new
				{
					fromEdges = DescribeSpawnCandidateDiagnostics(map, SpawnHowType.FromTheEdges),
					allOverTheMap = DescribeSpawnCandidateDiagnostics(map, SpawnHowType.AllOverTheMap)
				},
				letters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
					.Select(DescribeLetter)
					.ToArray()
			};
		}

		static object DescribeSpawnCandidateDiagnostics(Map map, SpawnHowType spawnHowType)
		{
			var oldSpawnHowType = ZombieSettings.Values.spawnHowType;
			try
			{
				ZombieSettings.Values.spawnHowType = spawnHowType;
				var cellValidator = Tools.ZombieSpawnLocator(map, true);
				var spotValidator = ZombiesRising.SpotValidator(cellValidator);
				var reachableRegions = Tools.PlayerReachableRegions(map) ?? new List<Region>();
				var consideredRegions = spawnHowType == SpawnHowType.FromTheEdges
					? reachableRegions.Where(region => region.touchesMapEdge).ToList()
					: reachableRegions.ToList();
				var consideredCells = consideredRegions
					.SelectMany(region => region.Cells)
					.Distinct()
					.ToList();
				var validCells = consideredCells
					.Where(cell => cellValidator(cell))
					.ToList();
				var validSpots = consideredCells
					.Where(cell => spotValidator(cell))
					.ToList();

				IntVec3 randomCell;
				IntVec3 getValidSpot;
				Rand.PushState(91273);
				try
				{
					randomCell = Tools.RandomSpawnCell(map, spawnHowType == SpawnHowType.FromTheEdges, spotValidator);
					getValidSpot = ZombiesRising.GetValidSpot(map, IntVec3.Invalid, cellValidator);
				}
				finally
				{
					Rand.PopState();
				}

				return new
				{
					spawnHowType = spawnHowType.ToString(),
					reachableRegionCount = reachableRegions.Count,
					edgeReachableRegionCount = reachableRegions.Count(region => region.touchesMapEdge),
					consideredRegionCount = consideredRegions.Count,
					consideredCellCount = consideredCells.Count,
					validCellCount = validCells.Count,
					validSpotCount = validSpots.Count,
					sampleValidCells = validCells.Take(8).Select(cell => DescribeSpawnCandidateCell(map, cell)).ToArray(),
					sampleValidSpots = validSpots.Take(8).Select(cell => DescribeSpawnCandidateCell(map, cell)).ToArray(),
					randomCell = randomCell.IsValid ? DescribeSpawnCandidateCell(map, randomCell) : null,
					getValidSpot = getValidSpot.IsValid ? DescribeSpawnCandidateCell(map, getValidSpot) : null
				};
			}
			finally
			{
				ZombieSettings.Values.spawnHowType = oldSpawnHowType;
			}
		}

		static object DescribeSpawnCandidateCell(Map map, IntVec3 cell)
		{
			var terrainGrid = map.terrainGrid;
			var terrain = terrainGrid.TerrainAt(cell);
			var room = cell.GetRoom(map);
			return new
			{
				cell = ZombieRuntimeActions.DescribeCell(cell),
				terrain = terrain?.defName,
				canRemoveTopLayer = terrainGrid.CanRemoveTopLayerAt(cell),
				standable = cell.Standable(map),
				fogged = cell.Fogged(map),
				roomFogged = room?.Fogged,
				roomTouchesMapEdge = room?.TouchesMapEdge,
				edifice = cell.GetEdifice(map)?.def?.defName
			};
		}

		static object DescribeColonistInfo(Map map)
		{
			var (capable, incapable) = Tools.ColonistsInfo(map);
			var total = map.mapPawns.FreeHumanlikesSpawnedOfFaction(Faction.OfPlayer).Count();
			return new
			{
				capable,
				incapable,
				total,
				minimumCapable = (total + 1) / 3
			};
		}

		static object DescribeLetter(Letter letter)
		{
			return letter == null ? null : new
			{
				label = letter.Label,
				defName = letter.def?.defName,
				letter.arrivalTick
			};
		}

		[Tool("zombieland/incident_special_type_spawn_contract", Description = "Verify the ZombiesRising event spawn core preserves explicit special zombie type requests.")]
		public static object IncidentSpecialTypeSpawnContract()
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

			var spawnEventProcess = typeof(ZombiesRising).GetMethod("SpawnEventProcess", BindingFlags.Static | BindingFlags.NonPublic);
			if (spawnEventProcess == null)
			{
				return new
				{
					success = false,
					error = "Could not find ZombiesRising.SpawnEventProcess by reflection."
				};
			}

			var cellValidator = Tools.ZombieSpawnLocator(map, true);
			var spot = ZombiesRising.GetValidSpot(map, IntVec3.Invalid, cellValidator);
			if (spot.IsValid == false)
			{
				return new
				{
					success = false,
					error = "No valid event spawn spot was found."
				};
			}

			var tickManager = map.GetComponent<TickManager>();
			var initialIds = CurrentZombies(map)
				.Select(ZombieRuntimeActions.StableThingId)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
			var spawnedZombies = new List<Zombie>();
			var samples = new List<object>();
			var types = new[]
			{
				ZombieType.SuicideBomber,
				ZombieType.ToxicSplasher,
				ZombieType.TankyOperator,
				ZombieType.Miner,
				ZombieType.Electrifier,
				ZombieType.Albino,
				ZombieType.DarkSlimer,
				ZombieType.Healer,
				ZombieType.Normal
			};

			try
			{
				var success = true;
				foreach (var type in types)
				{
					var beforeIds = CurrentZombies(map)
						.Select(ZombieRuntimeActions.StableThingId)
						.ToHashSet(StringComparer.OrdinalIgnoreCase);
					var iterator = spawnEventProcess.Invoke(null, new object[] { map, 1, spot, cellValidator, false, true, type }) as System.Collections.IEnumerator;
					if (iterator == null)
					{
						success = false;
						samples.Add(new
						{
							type = type.ToString(),
							success = false,
							error = "SpawnEventProcess did not return an IEnumerator."
						});
						continue;
					}

					var steps = 0;
					while (steps < 2048 && iterator.MoveNext())
						steps++;

					var after = CurrentZombies(map)
						.OfType<Zombie>()
						.Where(zombie => beforeIds.Contains(ZombieRuntimeActions.StableThingId(zombie)) == false)
						.ToArray();
					spawnedZombies.AddRange(after);
					var best = after
						.OrderBy(zombie => zombie.Position.DistanceToSquared(spot))
						.FirstOrDefault();
					var matched = MatchesRequestedZombieType(best, type);
					success &= matched && steps < 2048 && after.Length == 1;
					samples.Add(new
					{
						type = type.ToString(),
						success = matched && steps < 2048 && after.Length == 1,
						matched,
						steps,
						spawnedCount = after.Length,
						spawned = DescribeZombie(best)
					});
				}

				var currentIds = CurrentZombies(map)
					.Select(ZombieRuntimeActions.StableThingId)
					.ToHashSet(StringComparer.OrdinalIgnoreCase);
				var totalNewZombies = currentIds.Count(id => initialIds.Contains(id) == false);
				return new
				{
					success,
					spot = ZombieRuntimeActions.DescribeCell(spot),
					requestedTypes = types.Select(type => type.ToString()).ToArray(),
					totalNewZombies,
					samples
				};
			}
			finally
			{
				foreach (var zombie in spawnedZombies.Distinct())
				{
					_ = tickManager?.allZombiesCached?.Remove(zombie);
					_ = tickManager?.hummingZombies?.Remove(zombie);
					_ = tickManager?.tankZombies?.Remove(zombie);
					if (zombie.Destroyed == false)
						zombie.Destroy(DestroyMode.Vanish);
				}
			}
		}

		[Tool("zombieland/incident_alert_wave_contract", Description = "Verify a multi-zombie incident wave spawns zombies and creates the expected RimWorld threat letter.")]
		public static object IncidentAlertWaveContract()
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

			var spawnEventProcess = typeof(ZombiesRising).GetMethod("SpawnEventProcess", BindingFlags.Static | BindingFlags.NonPublic);
			if (spawnEventProcess == null)
			{
				return new
				{
					success = false,
					error = "Could not find ZombiesRising.SpawnEventProcess by reflection."
				};
			}

			var oldSpawnHowType = ZombieSettings.Values.spawnHowType;
			var spawnedZombies = new List<Zombie>();
			try
			{
				object RunCase(string name, SpawnHowType spawnHowType, string expectedLabelKey)
				{
					ZombieSettings.Values.spawnHowType = spawnHowType;
					var cellValidator = Tools.ZombieSpawnLocator(map, true);
					var spot = ZombiesRising.GetValidSpot(map, IntVec3.Invalid, cellValidator);
					if (spot.IsValid == false)
					{
						return new
						{
							name,
							success = false,
							spawnHowType = spawnHowType.ToString(),
							error = "No valid event spawn spot was found.",
							diagnostics = DescribeSpawnCandidateDiagnostics(map, spawnHowType)
						};
					}

					var beforeIds = CurrentZombies(map)
						.Select(ZombieRuntimeActions.StableThingId)
						.ToHashSet(StringComparer.OrdinalIgnoreCase);
					var beforeLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
						.ToHashSet();
					var iterator = spawnEventProcess.Invoke(null, new object[] { map, 4, spot, cellValidator, true, true, ZombieType.Normal }) as System.Collections.IEnumerator;
					if (iterator == null)
					{
						return new
						{
							name,
							success = false,
							spawnHowType = spawnHowType.ToString(),
							spot = ZombieRuntimeActions.DescribeCell(spot),
							error = "SpawnEventProcess did not return an IEnumerator."
						};
					}

					var steps = 0;
					while (steps < 4096 && iterator.MoveNext())
						steps++;

					var after = CurrentZombies(map)
						.OfType<Zombie>()
						.Where(zombie => beforeIds.Contains(ZombieRuntimeActions.StableThingId(zombie)) == false)
						.ToArray();
					spawnedZombies.AddRange(after);
					var newLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>())
						.Where(letter => beforeLetters.Contains(letter) == false)
						.ToArray();
					var expectedLabel = expectedLabelKey.Translate().ToString();
					var matchingLetters = newLetters
						.Where(letter => letter?.def == LetterDefOf.ThreatSmall && letter.Label == expectedLabel)
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
						success = steps < 4096
							&& after.Length == 4
							&& after.All(zombie => MatchesRequestedZombieType(zombie, ZombieType.Normal))
							&& newLetters.Length == 1
							&& matchingLetters.Length == 1,
						spawnHowType = spawnHowType.ToString(),
						expectedLabel,
						spot = ZombieRuntimeActions.DescribeCell(spot),
						steps,
						spawnedCount = after.Length,
						zombies = after.Select(DescribeZombie).ToArray(),
						newLetterCount = newLetters.Length,
						letters = newLetters.Select(letter => new
						{
							label = letter.Label,
							defName = letter.def?.defName,
							letter.arrivalTick
						}).ToArray(),
						matchingLetterCount = matchingLetters.Length
					};
				}

				var edgeCase = RunCase("from_edges_threat_letter", SpawnHowType.FromTheEdges, "LetterLabelZombiesRising");
				var allOverCase = RunCase("all_over_map_threat_letter", SpawnHowType.AllOverTheMap, "LetterLabelZombiesRisingNearYourBase");
				var cases = new[] { edgeCase, allOverCase };
				return new
				{
					success = cases.All(sample => sample.GetType().GetProperty("success")?.GetValue(sample) is true),
					sourcePath = "ZombiesRising.SpawnEventProcess -> zombiesSpawning > 3 -> Find.LetterStack.ReceiveLetter",
					cases
				};
			}
			finally
			{
				ZombieSettings.Values.spawnHowType = oldSpawnHowType;
				foreach (var zombie in spawnedZombies.Distinct())
				{
					_ = tickManager.allZombiesCached?.Remove(zombie);
					_ = tickManager.hummingZombies?.Remove(zombie);
					_ = tickManager.tankZombies?.Remove(zombie);
					if (zombie.Destroyed == false)
						zombie.Destroy(DestroyMode.Vanish);
				}
			}
		}

		[Tool("zombieland/random_zombie_type_weights_contract", Description = "Verify ZombieType.Random honors each special-zombie settings weight and the normal fallback weight.")]
		public static object RandomZombieTypeWeightsContract()
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

			var oldSuicideBomberChance = ZombieSettings.Values.suicideBomberChance;
			var oldToxicSplasherChance = ZombieSettings.Values.toxicSplasherChance;
			var oldTankyOperatorChance = ZombieSettings.Values.tankyOperatorChance;
			var oldMinerChance = ZombieSettings.Values.minerChance;
			var oldElectrifierChance = ZombieSettings.Values.electrifierChance;
			var oldAlbinoChance = ZombieSettings.Values.albinoChance;
			var oldDarkSlimerChance = ZombieSettings.Values.darkSlimerChance;
			var oldHealerChance = ZombieSettings.Values.healerChance;
			var spawnedZombies = new List<Zombie>();

			void ClearChances()
			{
				ZombieSettings.Values.suicideBomberChance = 0f;
				ZombieSettings.Values.toxicSplasherChance = 0f;
				ZombieSettings.Values.tankyOperatorChance = 0f;
				ZombieSettings.Values.minerChance = 0f;
				ZombieSettings.Values.electrifierChance = 0f;
				ZombieSettings.Values.albinoChance = 0f;
				ZombieSettings.Values.darkSlimerChance = 0f;
				ZombieSettings.Values.healerChance = 0f;
			}

			void SelectOnly(ZombieType type)
			{
				ClearChances();
				switch (type)
				{
					case ZombieType.SuicideBomber:
						ZombieSettings.Values.suicideBomberChance = 1f;
						break;
					case ZombieType.ToxicSplasher:
						ZombieSettings.Values.toxicSplasherChance = 1f;
						break;
					case ZombieType.TankyOperator:
						ZombieSettings.Values.tankyOperatorChance = 1f;
						break;
					case ZombieType.Miner:
						ZombieSettings.Values.minerChance = 1f;
						break;
					case ZombieType.Electrifier:
						ZombieSettings.Values.electrifierChance = 1f;
						break;
					case ZombieType.Albino:
						ZombieSettings.Values.albinoChance = 1f;
						break;
					case ZombieType.DarkSlimer:
						ZombieSettings.Values.darkSlimerChance = 1f;
						break;
					case ZombieType.Healer:
						ZombieSettings.Values.healerChance = 1f;
						break;
				}
			}

			try
			{
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				var types = new[]
				{
					ZombieType.SuicideBomber,
					ZombieType.ToxicSplasher,
					ZombieType.TankyOperator,
					ZombieType.Miner,
					ZombieType.Electrifier,
					ZombieType.Albino,
					ZombieType.DarkSlimer,
					ZombieType.Healer,
					ZombieType.Normal
				};
				var samples = new List<object>();
				var success = true;
				for (var i = 0; i < types.Length; i++)
				{
					var expectedType = types[i];
					SelectOnly(expectedType);
					var cellRoot = root + new IntVec3((i % 3 - 1) * 4, 0, (i / 3 - 1) * 4);
					if (TryFindClearSpawnCell(map, cellRoot, 20f, out var cell, out var cellError) == false)
					{
						success = false;
						samples.Add(new
						{
							expectedType = expectedType.ToString(),
							success = false,
							cellError
						});
						continue;
					}

					Rand.PushState(6100 + i);
					Zombie zombie;
					try
					{
						zombie = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.Random, true);
					}
					finally
					{
						Rand.PopState();
					}

					if (zombie != null)
						spawnedZombies.Add(zombie);
					var matched = MatchesRequestedZombieType(zombie, expectedType);
					success &= matched;
					samples.Add(new
					{
						expectedType = expectedType.ToString(),
						success = matched,
						cell = ZombieRuntimeActions.DescribeCell(cell),
						zombie = DescribeZombie(zombie),
						bodyType = zombie?.story?.bodyType?.defName,
						chances = new
						{
							ZombieSettings.Values.suicideBomberChance,
							ZombieSettings.Values.toxicSplasherChance,
							ZombieSettings.Values.tankyOperatorChance,
							ZombieSettings.Values.minerChance,
							ZombieSettings.Values.electrifierChance,
							ZombieSettings.Values.albinoChance,
							ZombieSettings.Values.darkSlimerChance,
							ZombieSettings.Values.healerChance
						}
					});
				}

				return new
				{
					success,
					sourcePath = "ZombieGenerator.PrepareZombieType -> TryRandomElementByWeight(zombieTypeInitializers)",
					samples
				};
			}
			finally
			{
				ZombieSettings.Values.suicideBomberChance = oldSuicideBomberChance;
				ZombieSettings.Values.toxicSplasherChance = oldToxicSplasherChance;
				ZombieSettings.Values.tankyOperatorChance = oldTankyOperatorChance;
				ZombieSettings.Values.minerChance = oldMinerChance;
				ZombieSettings.Values.electrifierChance = oldElectrifierChance;
				ZombieSettings.Values.albinoChance = oldAlbinoChance;
				ZombieSettings.Values.darkSlimerChance = oldDarkSlimerChance;
				ZombieSettings.Values.healerChance = oldHealerChance;
				foreach (var zombie in spawnedZombies.Distinct())
				{
					_ = tickManager.allZombiesCached?.Remove(zombie);
					_ = tickManager.hummingZombies?.Remove(zombie);
					_ = tickManager.tankZombies?.Remove(zombie);
					if (zombie.Destroyed == false)
						zombie.Destroy(DestroyMode.Vanish);
				}
			}
		}

		[Tool("zombieland/child_zombie_generation_contract", Description = "Verify child chance creates child normal zombies without overriding suicide bomber or tanky body rules.")]
		public static object ChildZombieGenerationContract()
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

			if (BodyTypeDefOf.Child == null)
			{
				return new
				{
					success = true,
					skipped = true,
					reason = "BodyTypeDefOf.Child is unavailable in this RimWorld build."
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

			var oldChildChance = ZombieSettings.Values.childChance;
			var spawnedZombies = new List<Zombie>();
			try
			{
				ZombieSettings.Values.childChance = 1f;
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				var cases = new[]
				{
					new { name = "normal_child", type = ZombieType.Normal, expectedBody = BodyTypeDefOf.Child, expectedChild = true },
					new { name = "suicide_bomber_adult", type = ZombieType.SuicideBomber, expectedBody = BodyTypeDefOf.Hulk, expectedChild = false },
					new { name = "tanky_adult", type = ZombieType.TankyOperator, expectedBody = BodyTypeDefOf.Fat, expectedChild = false }
				};
				var samples = new List<object>();
				var success = true;
				for (var i = 0; i < cases.Length; i++)
				{
					var entry = cases[i];
					var cellRoot = root + new IntVec3((i - 1) * 4, 0, 8);
					if (TryFindClearSpawnCell(map, cellRoot, 20f, out var cell, out var cellError) == false)
					{
						success = false;
						samples.Add(new
						{
							entry.name,
							success = false,
							cellError
						});
						continue;
					}

					Rand.PushState(6200 + i);
					Zombie zombie;
					try
					{
						zombie = ZombieRuntimeActions.SpawnZombie(cell, map, entry.type, true);
					}
					finally
					{
						Rand.PopState();
					}

					if (zombie != null)
						spawnedZombies.Add(zombie);
					var bodyType = zombie?.story?.bodyType;
					var isChild = bodyType == BodyTypeDefOf.Child;
					var age = zombie?.ageTracker?.AgeBiologicalYearsFloat ?? -1f;
					var ageMatches = entry.expectedChild
						? age >= 4.5f && age <= 15.6f
						: age >= 16.4f;
					var matched = zombie != null
						&& bodyType == entry.expectedBody
						&& isChild == entry.expectedChild
						&& MatchesRequestedZombieType(zombie, entry.type)
						&& ageMatches;
					success &= matched;
					samples.Add(new
					{
						entry.name,
						success = matched,
						requestedType = entry.type.ToString(),
						expectedBody = entry.expectedBody.defName,
						bodyType = bodyType?.defName,
						expectedChild = entry.expectedChild,
						isChild,
						age,
						ageMatches,
						zombie = DescribeZombie(zombie)
					});
				}

				return new
				{
					success,
					childChance = ZombieSettings.Values.childChance,
					sourcePath = "ZombieGenerator.SpawnZombieIterativ -> isChild excludes SuicideBomber and Tanky",
					samples
				};
			}
			finally
			{
				ZombieSettings.Values.childChance = oldChildChance;
				foreach (var zombie in spawnedZombies.Distinct())
				{
					_ = tickManager.allZombiesCached?.Remove(zombie);
					_ = tickManager.hummingZombies?.Remove(zombie);
					_ = tickManager.tankZombies?.Remove(zombie);
					if (zombie.Destroyed == false)
						zombie.Destroy(DestroyMode.Vanish);
				}
			}
		}

		[Tool("zombieland/incident_scheduling_contract", Description = "Verify zombie incident scheduler skip reasons and positive incident-size calculation.")]
		public static object IncidentSchedulingContract()
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

			var lastIncidentField = typeof(IncidentInfo).GetField("lastIncident", BindingFlags.Instance | BindingFlags.NonPublic);
			if (lastIncidentField == null)
			{
				return new
				{
					success = false,
					error = "Could not find IncidentInfo.lastIncident by reflection."
				};
			}

			var originalInfo = tickManager.incidentInfo;
			var oldDaysBeforeZombies = ZombieSettings.Values.daysBeforeZombiesCome;
			var oldSpawnWhenType = ZombieSettings.Values.spawnWhenType;
			var oldMaximumZombies = ZombieSettings.Values.maximumNumberOfZombies;
			var oldUseDynamicThreatLevel = ZombieSettings.Values.useDynamicThreatLevel;
			var oldBaseNumberOfZombies = ZombieSettings.Values.baseNumberOfZombiesinEvent;
			var oldColonyMultiplier = ZombieSettings.Values.colonyMultiplier;
			var oldExtraDaysBetweenEvents = ZombieSettings.Values.extraDaysBetweenEvents;
			var temporaryColonists = new List<Pawn>();

			IncidentInfo NewIncidentInfo()
			{
				var info = new IncidentInfo
				{
					parameters = new IncidentParameters
					{
						daysStretched = -10f,
						scaleFactor = 1f
					}
				};
				lastIncidentField.SetValue(info, -GenDate.TicksPerDay * 100);
				return info;
			}

			object RunWithSeed(int seed, Func<object> action)
			{
				Rand.PushState(seed);
				try
				{
					return action();
				}
				finally
				{
					Rand.PopState();
				}
			}

			bool HasEnoughCapableColonists()
			{
				var colonists = Tools.ColonistsInfo(map);
				var total = map.mapPawns.FreeHumanlikesSpawnedOfFaction(Faction.OfPlayer).Count();
				var minimumCapable = (total + 1) / 3;
				return colonists.Item1 >= minimumCapable;
			}

			bool EnsureCapableColonistFixture(out object error)
			{
				error = null;
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				while (HasEnoughCapableColonists() == false && temporaryColonists.Count < 8)
				{
					var candidateRoot = root + new IntVec3(temporaryColonists.Count * 2, 0, 0);
					if (TryFindClearSpawnCell(map, candidateRoot, 32f, out var cell, out error) == false)
						return false;

					var pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
					GenSpawn.Spawn(pawn, cell, map, Rot4.South);
					pawn.equipment?.DestroyAllEquipment(DestroyMode.Vanish);
					var weaponDef = DefDatabase<ThingDef>.GetNamed("Gun_BoltActionRifle", false)
						?? DefDatabase<ThingDef>.GetNamed("Gun_Pistol", false);
					var weapon = weaponDef == null ? null : ThingMaker.MakeThing(weaponDef) as ThingWithComps;
					if (weapon == null)
					{
						error = new
						{
							success = false,
							error = "No test ranged weapon def was available for the incident scheduler fixture."
						};
						return false;
					}
					pawn.equipment?.AddEquipment(weapon);
					temporaryColonists.Add(pawn);
				}

				if (HasEnoughCapableColonists())
					return true;

				var colonists = Tools.ColonistsInfo(map);
				var total = map.mapPawns.FreeHumanlikesSpawnedOfFaction(Faction.OfPlayer).Count();
				error = new
				{
					success = false,
					error = "Could not create enough temporary capable colonists for the incident scheduler fixture.",
					capable = colonists.Item1,
					incapable = colonists.Item2,
					total,
					minimumCapable = (total + 1) / 3,
					temporaryColonists = temporaryColonists.Count
				};
				return false;
			}

			try
			{
				if (EnsureCapableColonistFixture(out var fixtureError) == false)
					return fixtureError;

				ZombieSettings.Values.spawnWhenType = SpawnWhenType.AllTheTime;
				ZombieSettings.Values.useDynamicThreatLevel = false;
				ZombieSettings.Values.extraDaysBetweenEvents = 0;
				ZombieSettings.Values.colonyMultiplier = 1f;

				var waiting = RunWithSeed(1101, () =>
				{
					tickManager.incidentInfo = NewIncidentInfo();
					ZombieSettings.Values.daysBeforeZombiesCome = Mathf.CeilToInt(GenDate.DaysPassedFloat) + 10;
					ZombieSettings.Values.baseNumberOfZombiesinEvent = 20;
					ZombieSettings.Values.maximumNumberOfZombies = Math.Max(500, tickManager.ZombieCount() + 100);
					var result = ZombiesRising.ZombiesForNewIncident(tickManager);
					var parameters = tickManager.incidentInfo.parameters;
					var lastIncident = (int)lastIncidentField.GetValue(tickManager.incidentInfo);
					return new
					{
						name = "waiting_for_zombies",
						success = result == false && parameters.skipReason == "waiting for zombies",
						result,
						expectedResult = false,
						expectedSkipReason = "waiting for zombies",
						lastIncident,
						parameters = DescribeIncidentParameters(parameters)
					};
				});

				var empty = RunWithSeed(1102, () =>
				{
					tickManager.incidentInfo = NewIncidentInfo();
					ZombieSettings.Values.daysBeforeZombiesCome = 0;
					ZombieSettings.Values.baseNumberOfZombiesinEvent = 0;
					ZombieSettings.Values.maximumNumberOfZombies = 0;
					var result = ZombiesRising.ZombiesForNewIncident(tickManager);
					var parameters = tickManager.incidentInfo.parameters;
					var lastIncident = (int)lastIncidentField.GetValue(tickManager.incidentInfo);
					return new
					{
						name = "empty_incident",
						success = result == false && parameters.skipReason == "empty incident" && parameters.incidentSize == 0,
						result,
						expectedResult = false,
						expectedSkipReason = "empty incident",
						lastIncident,
						parameters = DescribeIncidentParameters(parameters)
					};
				});

				var positive = RunWithSeed(1103, () =>
				{
					tickManager.incidentInfo = NewIncidentInfo();
					ZombieSettings.Values.daysBeforeZombiesCome = 0;
					ZombieSettings.Values.baseNumberOfZombiesinEvent = 20;
					ZombieSettings.Values.maximumNumberOfZombies = Math.Max(500, tickManager.ZombieCount() + 100);
					var result = ZombiesRising.ZombiesForNewIncident(tickManager);
					var parameters = tickManager.incidentInfo.parameters;
					var lastIncident = (int)lastIncidentField.GetValue(tickManager.incidentInfo);
					return new
					{
						name = "positive_incident_size",
						success = result
							&& parameters.skipReason == "-"
							&& parameters.incidentSize > 0
							&& parameters.calculatedZombies > 0
							&& parameters.maxAdditionalZombies > 0
							&& parameters.deltaDays > 0
							&& lastIncident == GenTicks.TicksAbs,
						result,
						expectedResult = true,
						expectedSkipReason = "-",
						lastIncident,
						currentTicks = GenTicks.TicksAbs,
						parameters = DescribeIncidentParameters(parameters)
					};
				});

				var colonists = Tools.ColonistsInfo(map);
				var cases = new[] { waiting, empty, positive };
				return new
				{
					success = cases.All(sample => sample.GetType().GetProperty("success")?.GetValue(sample) is true),
					map = map.uniqueID,
					threatLevel = ZombieWeather.GetThreatLevel(map),
					colonists = new
					{
						capable = colonists.Item1,
						incapable = colonists.Item2,
						total = map.mapPawns.FreeHumanlikesSpawnedOfFaction(Faction.OfPlayer).Count()
					},
					cases
				};
			}
			finally
			{
				tickManager.incidentInfo = originalInfo;
				ZombieSettings.Values.daysBeforeZombiesCome = oldDaysBeforeZombies;
				ZombieSettings.Values.spawnWhenType = oldSpawnWhenType;
				ZombieSettings.Values.maximumNumberOfZombies = oldMaximumZombies;
				ZombieSettings.Values.useDynamicThreatLevel = oldUseDynamicThreatLevel;
				ZombieSettings.Values.baseNumberOfZombiesinEvent = oldBaseNumberOfZombies;
				ZombieSettings.Values.colonyMultiplier = oldColonyMultiplier;
				ZombieSettings.Values.extraDaysBetweenEvents = oldExtraDaysBetweenEvents;
				foreach (var pawn in temporaryColonists)
				{
					if (pawn.Corpse != null && pawn.Corpse.Destroyed == false)
						pawn.Corpse.Destroy(DestroyMode.Vanish);
					if (pawn.Destroyed == false)
						pawn.Destroy(DestroyMode.Vanish);
				}
			}
		}

	}
}
