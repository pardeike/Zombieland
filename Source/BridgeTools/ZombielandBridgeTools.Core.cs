using HarmonyLib;
using RimBridgeServer.Sdk;
using RimWorld;
using RimWorld.Planet;
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
		static bool? soundEventsPreviousWriteState;

		[Tool("zombieland/get_status", Description = "Read a compact live Zombieland status summary for the current RimWorld session.")]
		public static object GetStatus()
		{
			var map = CurrentMap;
			var tickManager = map?.GetComponent<TickManager>();
			var zombies = CurrentZombies(map);
			var gameTickManager = Current.Game?.tickManager;
			var survivalMeal = ThingDefOf.MealSurvivalPack;
			var zombieRace = CustomDefs.Zombie?.race;
			var settings = ZombieSettings.Values;
			var zombieGrid = DescribeZombieGrid(map, zombies);
			var ideology = DescribeIdeologyLoadState(map, zombies);
			var isSosOuterSpaceMap = map != null && map.Biome == SoSTools.sosOuterSpaceBiomeDef;
			var ambientSound = map == null ? null : DescribeAmbientSoundState(map, tickManager, settings, zombies.Length);

			return new
			{
				success = true,
				hasCurrentMap = map != null,
				mapId = map?.uniqueID ?? -1,
				mapBiome = map?.Biome?.defName,
				mapBiomeLabel = map?.Biome?.label,
				mapSize = map == null ? null : new
				{
					x = map.Size.x,
					z = map.Size.z
				},
				defsLoaded = new
				{
					zombieFaction = ZombieDefOf.Zombies != null,
					zombieKind = ZombieDefOf.Zombie != null,
					zombieRace = ZombieDefOf.Zombie?.race != null,
					zombieSymbiantKind = ZombieDefOf.ZombieSymbiant != null,
					zombieSpitterKind = ZombieDefOf.ZombieSpitter != null
				},
				tickManager = tickManager == null ? null : new
				{
					initialized = tickManager.isInitialized,
					cachedZombieCount = tickManager.allZombiesCached?.Count ?? 0,
					liveZombieCount = tickManager.ZombieCount(),
					currentColonyPoints = tickManager.currentColonyPoints,
					spawningInProgress = ZombieGenerator.ZombiesSpawning,
					currentZombiesTickingLength = tickManager.currentZombiesTickingCount,
					currentZombiesTickingCount = tickManager.currentZombiesTickingCount,
					currentZombiesTickingCapacity = tickManager.currentZombiesTicking?.Length ?? 0,
					currentZombiesTickingIndex = tickManager.currentZombiesTickingIndex
				},
				sos = new
				{
					installed = SoSTools.isInstalled,
					outerSpaceBiomeDef = SoSTools.sosOuterSpaceBiomeDef?.defName,
					shipOrbitingWorldObjectDef = SoSTools.sosShipOrbitingWorldObjectDef?.defName,
					currentMapIsOuterSpace = isSosOuterSpaceMap,
					floatingZombiesSetting = settings?.floatingZombies,
					floatingBackCount = tickManager?.floatingSpaceZombiesBack?.Count ?? 0,
					floatingForeCount = tickManager?.floatingSpaceZombiesFore?.Count ?? 0,
					expectedBackCount = SoSTools.Floater.backCount,
					expectedForeCount = SoSTools.Floater.foreCount,
					floatingBackReady = (tickManager?.floatingSpaceZombiesBack?.Count ?? 0) >= SoSTools.Floater.backCount,
					floatingForeReady = (tickManager?.floatingSpaceZombiesFore?.Count ?? 0) >= SoSTools.Floater.foreCount
				},
				zombieTicker = new
				{
					percentTicking = ZombieTicker.PercentTicking,
					percentSamples = ZombieTicker.percentZombiesTicked.ToArray(),
					percentIndex = ZombieTicker.percentZombiesTickedIndex,
					zombiesTicked = ZombieTicker.zombiesTicked,
					maxTicking = ZombieTicker.maxTicking,
					currentTicking = ZombieTicker.currentTicking,
					managersCount = ZombieTicker.managers?.Count() ?? 0,
					frameWatchRunning = ZombielandMod.frameWatch.IsRunning,
					frameWatchElapsedMilliseconds = ZombielandMod.frameWatch.ElapsedMilliseconds,
					saturation = ZombieTicker.DescribeSaturation(tickManager)
				},
				ideology,
				ambientSound,
				zombieGrid,
				spawnedZombieCount = zombies.Length,
				ordinaryZombies = zombies.OfType<Zombie>().Count(),
				symbiants = zombies.OfType<ZombieSymbiant>().Count(),
				spitters = zombies.OfType<ZombieSpitter>().Count(),
				finalizedDefs = new
				{
					replaceTwinkieSetting = settings?.replaceTwinkie,
					survivalMealLabel = survivalMeal?.label,
					survivalMealDescription = survivalMeal?.description,
					survivalMealGraphicType = survivalMeal?.graphic?.GetType().FullName,
					survivalMealCachedGraphicType = survivalMeal?.graphicData?.cachedGraphic?.GetType().FullName,
					twinkieCachedGraphicApplied = survivalMeal?.graphicData?.cachedGraphic == GraphicsDatabase.twinkieGraphic,
					healthFactorSetting = settings?.healthFactor,
					zombieBaseHealthScale = zombieRace?.baseHealthScale,
					zombieHealthScaleApplied = settings != null
						&& zombieRace != null
						&& Mathf.Abs(zombieRace.baseHealthScale - settings.healthFactor) < 0.0001f
				},
				timeSpeed = gameTickManager == null ? null : gameTickManager.CurTimeSpeed.ToString()
			};
		}

		[Tool("zombieland/zombie_lightweight_perf_state", Description = "Read cheap read-only zombie performance counters without resetting frameWatch or running the heavier status probe.")]
		public static object ZombieLightweightPerfState(
			[ToolParameter(Description = "When true, try to include Unity Profiler memory counters via reflection. This is optional and best-effort.", Required = false, DefaultValue = false)] bool includeUnityProfilerMemory = false)
		{
			var map = CurrentMap;
			var tickManager = map?.GetComponent<TickManager>();
			var cached = tickManager?.allZombiesCached;
			var cachedZombieCount = cached?.Count ?? 0;
			var liveCachedZombieCount = 0;

			if (cached != null)
				foreach (var zombie in cached)
					if (zombie != null && zombie.Spawned && zombie.Dead == false)
						liveCachedZombieCount++;

			var ordinaryZombies = 0;
			var symbiants = 0;
			var spitters = 0;
			var tanky = 0;
			var activeElectrical = 0;
			var electrifiers = 0;
			var miners = 0;
			var albinos = 0;
			var darkSlimers = 0;
			var suicideBombers = 0;
			var toxicSplashers = 0;
			var healers = 0;

			var pawns = map?.mapPawns?.AllPawnsSpawned;
			if (pawns != null)
				foreach (var pawn in pawns)
				{
					if (pawn == null || pawn.Spawned == false || pawn.Dead)
						continue;
					if (pawn is Zombie zombie)
					{
						ordinaryZombies++;
						if (zombie.IsTanky)
							tanky++;
						if (zombie.IsActiveElectric)
							activeElectrical++;
						if (zombie.isElectrifier)
							electrifiers++;
						if (zombie.isMiner)
							miners++;
						if (zombie.isAlbino)
							albinos++;
						if (zombie.isDarkSlimer)
							darkSlimers++;
						if (zombie.IsSuicideBomber)
							suicideBombers++;
						if (zombie.isToxicSplasher)
							toxicSplashers++;
						if (zombie.isHealer)
							healers++;
					}
					else if (pawn is ZombieSymbiant)
						symbiants++;
					else if (pawn is ZombieSpitter)
						spitters++;
				}

			return new
			{
				success = true,
				hasCurrentMap = map != null,
				mapId = map?.uniqueID ?? -1,
				mapSize = map == null ? null : new
				{
					x = map.Size.x,
					z = map.Size.z,
					area = map.Size.x * map.Size.z
				},
				memory = new
				{
					gcTotalMemoryBytes = GC.GetTotalMemory(false),
					gcCollectionForced = false,
					unityProfiler = includeUnityProfilerMemory ? TryReadUnityProfilerMemory() : null
				},
				zombies = new
				{
					cachedZombieCount,
					liveCachedZombieCount,
					liveZombielandPawnCount = ordinaryZombies + symbiants + spitters,
					pendingSpawns = ZombieGenerator.ZombiesSpawning,
					liveCountIncludingPendingSpawns = liveCachedZombieCount + symbiants + spitters + ZombieGenerator.ZombiesSpawning,
					special = new
					{
						ordinaryZombies,
						symbiants,
						spitters,
						tanky,
						activeElectrical,
						electrifiers,
						miners,
						albinos,
						darkSlimers,
						suicideBombers,
						toxicSplashers,
						healers
					}
				},
				ticking = tickManager == null ? null : new
				{
					currentCount = tickManager.currentZombiesTickingCount,
					currentCapacity = tickManager.currentZombiesTicking?.Length ?? 0,
					currentIndex = tickManager.currentZombiesTickingIndex,
					candidateCount = tickManager.CurrentZombiesTickingCandidatesCount,
					candidateCapacity = tickManager.CurrentZombiesTickingCandidatesCapacity
				},
				saturation = ZombieTicker.DescribeSaturation(tickManager),
				zombieGrid = DescribeZombieGridDensity(map),
				frameWatch = new
				{
					running = ZombielandMod.frameWatch.IsRunning,
					elapsedMilliseconds = ZombielandMod.frameWatch.ElapsedMilliseconds
				}
			};
		}

		static object DescribeAmbientSoundState(Map map, TickManager tickManager, SettingsGroup settings, int zombieCount)
		{
			float? localHour = map == null ? null : GenLocalDate.HourFloat(map);
			float? normalizedHour = null;
			if (localHour.HasValue)
				normalizedHour = localHour.Value < 12f ? localHour.Value + 24f : localHour.Value;

			float? targetVolume = null;
			if (map != null && ZombieStateHandler.creepyAmbientSoundVolumes.TryGetValue(map.uniqueID, out var ambientVolume))
				targetVolume = ambientVolume;

			var ambientSustainerField = tickManager == null ? null : AccessTools.Field(typeof(TickManager), "zombiesAmbientSound");
			var ambientVolumeField = tickManager == null ? null : AccessTools.Field(typeof(TickManager), "zombiesAmbientSoundVolume");
			var ambientSustainer = ambientSustainerField?.GetValue(tickManager) as Sustainer;
			float? currentVolume = null;
			if (ambientVolumeField?.GetValue(tickManager) is float volume)
				currentVolume = volume;

			return new
			{
				useSound = Constants.USE_SOUND,
				playSetting = settings?.playCreepyAmbientSound,
				zombieCount,
				localHour,
				normalizedHour,
				spawningHours = Constants.ZOMBIE_SPAWNING_HOURS.ToArray(),
				targetVolumeKnown = targetVolume.HasValue,
				targetVolume,
				hasSustainer = ambientSustainer != null,
				currentVolume,
				sustainerVolumeFactor = ambientSustainer?.info.volumeFactor
			};
		}

		[Tool("zombieland/create_sos_space_map_fixture", Description = "Create a Save Our Ship 2 orbiting-ship map fixture so Zombieland space-map behavior can be verified through normal ticks.")]
		public static object CreateSoSSpaceMapFixture(
			[ToolParameter(Description = "Square map size to generate.", Required = false, DefaultValue = 200)] int size = 200)
		{
			if (SoSTools.isInstalled == false)
				return new { success = false, message = "Save Our Ship 2 is not installed." };
			if (SoSTools.sosShipOrbitingWorldObjectDef == null)
				return new { success = false, message = "Save Our Ship 2 ShipOrbiting world object def was not resolved." };

			size = Mathf.Clamp(size, 50, 300);
			var previousMap = CurrentMap;
			var mapGeneratorDef = DefDatabase<MapGeneratorDef>.GetNamed("EmptySpaceMap", false);
			if (mapGeneratorDef == null)
				return new { success = false, message = "Save Our Ship 2 EmptySpaceMap generator def was not resolved." };

			try
			{
				var parent = WorldObjectMaker.MakeWorldObject(SoSTools.sosShipOrbitingWorldObjectDef) as MapParent;
				if (parent == null)
					return new
					{
						success = false,
						message = "ShipOrbiting world object is not a MapParent.",
						worldObjectClass = SoSTools.sosShipOrbitingWorldObjectDef.worldObjectClass?.FullName
					};

				parent.SetFaction(Faction.OfPlayer);
				parent.Tile = FindSoSTestTile(previousMap);
				Find.WorldObjects.Add(parent);

				var generatedMap = MapGenerator.GenerateMap(new IntVec3(size, 1, size), parent, mapGeneratorDef, null, null, false);
				Current.Game.CurrentMap = generatedMap;

				var tickManager = generatedMap.GetComponent<TickManager>();
				return new
				{
					success = true,
					previousMapId = previousMap?.uniqueID ?? -1,
					mapId = generatedMap.uniqueID,
					mapIndex = generatedMap.Index,
					mapSize = new { x = generatedMap.Size.x, z = generatedMap.Size.z },
					mapBiome = generatedMap.Biome?.defName,
					parentDef = parent.def?.defName,
					parentType = parent.GetType().FullName,
					parentTile = parent.Tile.ToString(),
					mapGenerator = mapGeneratorDef.defName,
					currentMapIsOuterSpace = generatedMap.Biome == SoSTools.sosOuterSpaceBiomeDef,
					tickManagerPresent = tickManager != null,
					floatingBackCount = tickManager?.floatingSpaceZombiesBack?.Count ?? 0,
					floatingForeCount = tickManager?.floatingSpaceZombiesFore?.Count ?? 0
				};
			}
			catch (Exception ex)
			{
				return new
				{
					success = false,
					message = ex.Message,
					exceptionType = ex.GetType().FullName,
					stackTrace = ex.StackTrace
				};
			}
		}

		static PlanetTile FindSoSTestTile(Map previousMap)
		{
			var finder = AccessTools.Method("SaveOurShip2.ShipInteriorMod2:FindWorldTileOnLayers", new[] { typeof(bool) });
			if (finder != null)
			{
				var reflectedTile = finder.Invoke(null, new object[] { true });
				if (reflectedTile is PlanetTile tile && tile != PlanetTile.Invalid)
					return tile;
			}

			if (previousMap != null && previousMap.Tile != PlanetTile.Invalid)
				return previousMap.Tile;

			return TileFinder.RandomStartingTile();
		}

		[Tool("zombieland/startup_support_state", Description = "Verify startup asset loading and shared Zombieland service hook dispatch without clearing the live game.")]
		public static object StartupSupportState()
		{
			var assetTargets = PatchedMethodsForPatchClass(nameof(Assets));
			var legacyParseTargets = PatchedMethodsForPatchClass("ParseHelper_FromString_Patch");
			var mainMenuInitTargets = PatchedMethodsForPatchClass("MainMenuDrawer_Init_Patch");
			var timeControlTargets = PatchedMethodsForPatchClass(nameof(TimeControlService));
			var clearMapsTargets = PatchedMethodsForPatchClass(nameof(ClearMapsService));
			var rootUpdateTargets = PatchedMethodsForPatchClass("Root_Update_Patch");
			var rootShutdownTargets = PatchedMethodsForPatchClass("Root_Shutdown_Patch");
			var assetProbe = VerifyStartupAssets();
			var legacyParseProbe = VerifyLegacyAreaRiskModeParsing();
			var mainMenuProbe = VerifyMainMenuStartupErrors();
			var timeControlProbe = VerifyTimeControlService();
			var clearMapsProbe = VerifyClearMapsService();
			var rootLifecycleProbe = VerifyRootLifecycleHooks();
			var defResolutionProbe = VerifyZombielandDefResolution();
			var optionalIntegrationsProbe = VerifyOptionalIntegrations();

			return new
			{
				success = assetTargets.Length > 0
					&& legacyParseTargets.Length > 0
					&& mainMenuInitTargets.Length > 0
					&& timeControlTargets.Length > 0
					&& clearMapsTargets.Length > 0
					&& rootUpdateTargets.Length > 0
					&& rootShutdownTargets.Length > 0
					&& ObjectSuccess(assetProbe)
					&& ObjectSuccess(legacyParseProbe)
					&& ObjectSuccess(mainMenuProbe)
					&& ObjectSuccess(timeControlProbe)
					&& ObjectSuccess(clearMapsProbe)
					&& ObjectSuccess(rootLifecycleProbe)
					&& ObjectSuccess(defResolutionProbe)
					&& ObjectSuccess(optionalIntegrationsProbe),
				patchTargets = new
				{
					assets = assetTargets,
					legacyParse = legacyParseTargets,
					mainMenuInit = mainMenuInitTargets,
					timeControl = timeControlTargets,
					clearMaps = clearMapsTargets,
					rootUpdate = rootUpdateTargets,
					rootShutdown = rootShutdownTargets
				},
				assets = assetProbe,
				legacyParse = legacyParseProbe,
				mainMenuInit = mainMenuProbe,
				timeControl = timeControlProbe,
				clearMaps = clearMapsProbe,
				rootLifecycle = rootLifecycleProbe,
				defResolution = defResolutionProbe,
				optionalIntegrations = optionalIntegrationsProbe
			};
		}

		static object VerifyOptionalIntegrations()
		{
			var activePackages = LoadedModManager.RunningModsListForReading
				.Select(mod => mod.PackageIdPlayerFacing)
				.Where(id => id.NullOrEmpty() == false)
				.OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
				.ToArray();
			var activePackageSet = new HashSet<string>(activePackages, StringComparer.OrdinalIgnoreCase);

			var integrations = new[]
			{
				DescribeOptionalIntegration(
					"Combat Extended",
					"ceteam.combatextended",
					activePackageSet,
					new[]
					{
						ExternalMemberTypeMethod("armorReroute", "CombatExtended.HarmonyCE.Harmony_DamageWorker_AddInjury_ApplyDamageToPart", "ArmorReroute"),
						ExternalMemberTypeMethod("projectileLaunch", "CombatExtended.ProjectileCE", "Launch", new[] { typeof(Thing), typeof(Vector2), typeof(float), typeof(float), typeof(float), typeof(float), typeof(Thing), typeof(float) }),
						ExternalMemberTypeMethod("afterArmorDamage", "CombatExtended.ArmorUtilityCE", "GetAfterArmorDamage", new[] { typeof(DamageInfo), typeof(Pawn), typeof(BodyPartRecord), typeof(bool).MakeByRefType(), typeof(bool).MakeByRefType(), typeof(bool).MakeByRefType() }),
						ExternalMemberTypeMethod("ammoUser", "CombatExtended.CompAmmoUser", "Notify_ShotFired", new[] { typeof(int) })
					},
					PatchedMethodsForPatchClass("CETools_Patch1")
						.Concat(PatchedMethodsForPatchClass("CETools_Patch2"))
						.Concat(PatchedMethodsForPatchClass("CETools_Patch3"))
						.Concat(PatchedMethodsForPatchClass("CETools_Patch4"))
						.ToArray()),
				DescribeOptionalIntegration(
					"Humanoid Alien Races",
					"erdelf.humanoidalienraces",
					activePackageSet,
					new[]
					{
						ExternalMemberType("alienRaceDef", "AlienRace.ThingDef_AlienRace"),
						ExternalMemberField("alienRaceSettings", "AlienRace.ThingDef_AlienRace", "alienRace"),
						ExternalMemberField("compatibility", "AlienRace.ThingDef_AlienRace+AlienSettings", "compatibility"),
						ExternalAlienFleshPawnMember()
					},
					Array.Empty<object>()),
				DescribeOptionalIntegration(
					"Vehicle Framework",
					"SmashPhil.VehicleFramework",
					activePackageSet,
					new[]
					{
						ExternalMemberType("vehiclePawn", "Vehicles.VehiclePawn"),
						ExternalMemberType("vehicleStatDefOf", "Vehicles.VehicleStatDefOf"),
						ExternalMemberTypeMethod("vehicleGetStatValue", "Vehicles.VehiclePawn", "GetStatValue"),
						ExternalMemberStringMethod("vehicleFleeDestination", "Vehicles.VehicleDamager:TryFindDirectFleeDestination")
					},
					PatchedMethodsForPatchOwner("net.pardeike.zombieland.vehicles")),
				DescribeDubsIntegration(activePackageSet),
				DescribeOptionalIntegration(
					"Save Our Ship 2",
					"kentington.saveourship2",
					activePackageSet,
					new[]
					{
						ExternalMemberTypeMethod("shipInteriorIsHologram", "SaveOurShip2.ShipInteriorMod2", "IsHologram"),
						ExternalMemberStringMethod("shipInteriorGenerateShip", "SaveOurShip2.ShipInteriorMod2:GenerateShip"),
						ExternalMemberStringMethod("legacyShipCombatGenerateShip", "RimWorld.ShipCombatManager:GenerateShip"),
						ExternalMemberStringMethod("spaceSubMeshPrefix", "SaveOurShip2.GenerateSpaceSubMesh:Prefix"),
						ExternalMemberStringMethod("legacySpaceSubMeshGenerateMesh", "SaveOurShip2.GenerateSpaceSubMesh:GenerateMesh")
					},
					PatchedMethodsForPatchClass("RimWorld_ShipCombatManager_GenerateShip_Patch")
						.Concat(PatchedMethodsForPatchClass("SaveOurShip2_GenerateSpaceSubMesh_GenerateMesh_Patch"))
						.Concat(PatchedMethodsForPatchClass("Map_MapUpdate_Patch"))
						.ToArray()),
				DescribeOptionalIntegration(
					"RimConnect",
					"betterscenes.rimconnect",
					activePackageSet,
					new[]
					{
						ExternalMemberTypeMethod("actionList", "RimConnection.ActionList", "GenerateActionList"),
						ExternalMemberTypeMethod("actionExecute", "RimConnection.Action", "Execute"),
						ExternalMemberStringMethod("badEventNotification", "RimConnection.AlertManager:BadEventNotification", new[] { typeof(string), typeof(IntVec3) }),
						ExternalMemberStringMethod("settingsWindow", "RimConnection.Settings.CommandOptionSettings:DoWindowContents")
					},
					PatchedMethodsForPatchClass("RimConnection_Settings_CommandOptionSettings_Patch")
						.Concat(PatchedMethodsForPatchOwner("net.pardeike.zombieland.rimconnect"))
						.ToArray()),
				DescribeOptionalIntegration(
					"Camera+",
					"brrainz.cameraplus",
					activePackageSet,
					new[]
					{
						ExternalMemberType("cameraDelegates", "CameraPlus.CameraDelegates"),
						ExternalMemberTypeMethod("cameraDelegateCache", "CameraPlus.Caches", "GetCachedCameraDelegate", new[] { typeof(Pawn) }),
						ExternalMemberTypeMethod("markerColors", "CameraPlus.DotTools", "GetMarkerColors", new[] { typeof(Pawn), typeof(Color).MakeByRefType(), typeof(Color).MakeByRefType() }),
						ExternalMemberTypeMethod("markerTextures", "CameraPlus.DotTools", "GetMarkerTextures", new[] { typeof(Pawn), typeof(Texture2D).MakeByRefType(), typeof(Texture2D).MakeByRefType() }),
						InternalMemberMethod("cameraSupportColors", typeof(CameraPlusSupport.Methods), "GetCameraPlusColors"),
						InternalMemberMethod("cameraSupportMarkers", typeof(CameraPlusSupport.Methods), "GetCameraPlusMarkers")
					},
					Array.Empty<object>())
			};
			var customization = DescribeCustomizationSupport();
			var cameraMarkers = DescribeCameraPlusMarkerTextures();

			return new
			{
				success = integrations.All(ObjectSuccess)
					&& ObjectSuccess(customization)
					&& ObjectSuccess(cameraMarkers),
				activePackages,
				integrations,
				customization,
				cameraMarkers
			};
		}

		static object DescribeOptionalIntegration(string name, string packageId, HashSet<string> activePackageSet, OptionalMemberSnapshot[] members, object[] patchTargets)
		{
			var packageActive = activePackageSet.Contains(packageId);
			var dependencyMembers = members
				.Where(member => member.internalSupport == false)
				.ToArray();
			var dependencyPresent = dependencyMembers.Any(member => member.typePresent || member.methodPresent || member.fieldPresent);
			var missingMembers = packageActive
				? members.Where(member => member.success == false).ToArray()
				: dependencyPresent
					? dependencyMembers.Where(member => member.success == false).ToArray()
					: Array.Empty<OptionalMemberSnapshot>();
			return new
			{
				success = packageActive
					? dependencyPresent && missingMembers.Length == 0
					: missingMembers.Length == 0,
				name,
				packageId,
				packageActive,
				dependencyPresent,
				runtimeState = packageActive ? "active" : dependencyPresent ? "assembly_or_type_present_but_inactive" : "absent",
				memberChecks = members,
				missingMemberCount = missingMembers.Length,
				missingMembers,
				patchTargets,
				patchTargetCount = patchTargets.Length
			};
		}

		static object DescribeDubsIntegration(HashSet<string> activePackageSet)
		{
			var packageId = "Dubwise.DubsPerformanceAnalyzer.steam";
			var packageActive = activePackageSet.Contains(packageId);
			var currentOverlayEntry = ExternalMemberTypeMethod("currentThingOverlayEntry", "Analyzer.Profiling.H_ThingOverlaysOnGUI", "GetPatchMethods");
			var legacyDrawNamesPrefix = ExternalMemberStringMethod("legacyDrawNamesPrefix", "Analyzer.Fixes.H_DrawNamesFix:Prefix");
			var members = new[] { currentOverlayEntry, legacyDrawNamesPrefix };
			var patchTargets = PatchedMethodsForPatchOwner("net.pardeike.zombieland.dubs");
			var dependencyPresent = members.Any(member => member.typePresent || member.methodPresent || member.fieldPresent);
			var knownTargetPresent = currentOverlayEntry.success || legacyDrawNamesPrefix.success;
			var missingMembers = dependencyPresent && knownTargetPresent == false ? members : Array.Empty<OptionalMemberSnapshot>();

			return new
			{
				success = packageActive
					? dependencyPresent && knownTargetPresent
					: dependencyPresent == false || knownTargetPresent,
				name = "Dubs Performance Analyzer",
				packageId,
				packageActive,
				dependencyPresent,
				runtimeState = packageActive
					? currentOverlayEntry.success
						? "active_current_overlay_entry"
						: legacyDrawNamesPrefix.success
							? "active_legacy_draw_names_fix"
							: "active_missing_known_targets"
					: dependencyPresent
						? "assembly_or_type_present_but_inactive"
						: "absent",
				targetFamily = currentOverlayEntry.success ? "current_overlay_entry" : legacyDrawNamesPrefix.success ? "legacy_draw_names_fix" : "none",
				memberChecks = members,
				missingMemberCount = missingMembers.Length,
				missingMembers,
				patchTargets,
				patchTargetCount = patchTargets.Length
			};
		}

		static object DescribeCustomizationSupport()
		{
			try
			{
				var type = typeof(Customization);
				var canBecomeZombieCount = ((ICollection)type
					.GetField("canBecomeZombieEvaluators", BindingFlags.Static | BindingFlags.NonPublic)
					?.GetValue(null))?.Count ?? 0;
				var attractsZombiesCount = ((ICollection)type
					.GetField("attractsZombiesEvaluators", BindingFlags.Static | BindingFlags.NonPublic)
					?.GetValue(null))?.Count ?? 0;
				return new
				{
					success = true,
					zombielandSupportAssemblies = canBecomeZombieCount + attractsZombiesCount,
					canBecomeZombieCount,
					attractsZombiesCount
				};
			}
			catch (Exception ex)
			{
				return new
				{
					success = false,
					error = ex.GetType().Name + ": " + ex.Message
				};
			}
		}

		static object DescribeCameraPlusMarkerTextures()
		{
			var inner = ContentFinder<Texture2D>.Get("InnerCameraMarker", false);
			var outer = ContentFinder<Texture2D>.Get("OuterCameraMarker", false);
			return new
			{
				success = inner != null && outer != null,
				inner = inner?.name,
				outer = outer?.name
			};
		}

		static object[] PatchedMethodsForPatchOwner(string owner)
		{
			return Harmony.GetAllPatchedMethods()
				.Select(method => new
				{
					method,
					patchInfo = Harmony.GetPatchInfo(method)
				})
				.Select(entry => new
				{
					entry.method,
					patches = (entry.patchInfo?.Prefixes ?? Enumerable.Empty<Patch>())
						.Concat(entry.patchInfo?.Postfixes ?? Enumerable.Empty<Patch>())
						.Concat(entry.patchInfo?.Transpilers ?? Enumerable.Empty<Patch>())
						.Where(patch => patch.owner == owner)
						.ToArray()
				})
				.Where(entry => entry.patches.Length > 0)
				.Select(entry => new
				{
					method = entry.method.FullDescription(),
					patchMethods = entry.patches.Select(patch => patch.PatchMethod?.FullDescription()).Distinct().OrderBy(text => text).ToArray()
				})
				.Cast<object>()
				.ToArray();
		}

		static OptionalMemberSnapshot ExternalMemberType(string name, string typeName)
		{
			var type = AccessTools.TypeByName(typeName);
			return new OptionalMemberSnapshot
			{
				success = type != null,
				name = name,
				typeName = typeName,
				typePresent = type != null
			};
		}

		static OptionalMemberSnapshot ExternalMemberField(string name, string typeName, string fieldName)
		{
			var type = AccessTools.TypeByName(typeName);
			var field = type == null ? null : AccessTools.Field(type, fieldName);
			return new OptionalMemberSnapshot
			{
				success = type != null && field != null,
				name = name,
				typeName = typeName,
				memberName = fieldName,
				typePresent = type != null,
				fieldPresent = field != null,
				resolvedMember = field == null ? null : $"{field.DeclaringType?.FullName}.{field.Name}"
			};
		}

		static OptionalMemberSnapshot ExternalMemberTypeMethod(string name, string typeName, string methodName, Type[] parameters = null)
		{
			var type = AccessTools.TypeByName(typeName);
			var method = type == null ? null : AccessTools.Method(type, methodName, parameters);
			return new OptionalMemberSnapshot
			{
				success = type != null && method != null,
				name = name,
				typeName = typeName,
				memberName = methodName,
				typePresent = type != null,
				methodPresent = method != null,
				resolvedMember = method?.FullDescription()
			};
		}

		static OptionalMemberSnapshot ExternalMemberStringMethod(string name, string methodName, Type[] parameters = null)
		{
			var method = AccessTools.Method(methodName, parameters);
			return new OptionalMemberSnapshot
			{
				success = method != null,
				name = name,
				memberName = methodName,
				methodPresent = method != null,
				typePresent = method?.DeclaringType != null,
				resolvedMember = method?.FullDescription()
			};
		}

		static OptionalMemberSnapshot ExternalAlienFleshPawnMember()
		{
			var settingsType = AccessTools.TypeByName("AlienRace.ThingDef_AlienRace+AlienSettings");
			var compatibilityField = settingsType == null ? null : AccessTools.Field(settingsType, "compatibility");
			var method = compatibilityField?.FieldType == null ? null : AccessTools.Method(compatibilityField.FieldType, "IsFleshPawn");
			return new OptionalMemberSnapshot
			{
				success = settingsType != null && compatibilityField != null && method != null,
				name = "isFleshPawn",
				typeName = compatibilityField?.FieldType?.FullName ?? "AlienRace.ThingDef_AlienRace+AlienSettings.compatibility",
				memberName = "IsFleshPawn",
				typePresent = settingsType != null,
				fieldPresent = compatibilityField != null,
				methodPresent = method != null,
				resolvedMember = method?.FullDescription()
			};
		}

		static OptionalMemberSnapshot InternalMemberMethod(string name, Type type, string methodName)
		{
			var method = AccessTools.Method(type, methodName);
			return new OptionalMemberSnapshot
			{
				success = method != null,
				name = name,
				typeName = type.FullName,
				memberName = methodName,
				typePresent = type != null,
				methodPresent = method != null,
				internalSupport = true,
				resolvedMember = method?.FullDescription()
			};
		}

		sealed class OptionalMemberSnapshot
		{
			public bool success;
			public string name;
			public string typeName;
			public string memberName;
			public bool typePresent;
			public bool methodPresent;
			public bool fieldPresent;
			public bool internalSupport;
			public string resolvedMember;
		}

		static object VerifyZombielandDefResolution()
		{
			var content = LoadedModManager.GetMod<ZombielandMod>()?.Content;
			if (content == null)
			{
				return new
				{
					success = false,
					error = "Could not resolve the active Zombieland ModContentPack."
				};
			}

			var allDefs = new List<Def>();
			var typeCounts = new List<object>();
			var databaseErrors = new List<object>();
			foreach (var defType in GenDefDatabase.AllDefTypesWithDatabases().OrderBy(type => type.FullName, StringComparer.Ordinal))
			{
				try
				{
					var databaseType = typeof(DefDatabase<>).MakeGenericType(defType);
					var property = databaseType.GetProperty("AllDefsListForReading", BindingFlags.Public | BindingFlags.Static);
					if (property?.GetValue(null) is not IEnumerable loadedDefs)
						continue;

					var ownedDefs = loadedDefs
						.OfType<Def>()
						.Where(def => IsZombielandDef(def, content))
						.OrderBy(def => def.defName, StringComparer.Ordinal)
						.ToArray();
					if (ownedDefs.Length == 0)
						continue;

					allDefs.AddRange(ownedDefs);
					typeCounts.Add(new
					{
						type = defType.FullName,
						count = ownedDefs.Length,
						samples = ownedDefs.Take(8).Select(def => def.defName).ToArray()
					});
				}
				catch (Exception ex)
				{
					databaseErrors.Add(new
					{
						type = defType.FullName,
						error = ex.GetType().Name + ": " + ex.Message
					});
				}
			}

			var configErrors = allDefs
				.SelectMany(DescribeDefConfigErrors)
				.Take(50)
				.ToArray();
			var graphicResults = allDefs
				.OfType<ThingDef>()
				.Where(def => def.graphicData != null)
				.Select(DescribeThingGraphicResolution)
				.ToArray();
			var graphicErrors = graphicResults
				.Where(result => result.success == false)
				.Take(50)
				.ToArray();
			var pawnKindGraphicResults = allDefs
				.OfType<PawnKindDef>()
				.SelectMany(DescribePawnKindGraphicResolution)
				.ToArray();
			var pawnKindGraphicErrors = pawnKindGraphicResults
				.Where(result => result.success == false)
				.Take(50)
				.ToArray();
			var soundResults = allDefs
				.OfType<SoundDef>()
				.OrderBy(def => def.defName, StringComparer.Ordinal)
				.Select(DescribeSoundDefResolution)
				.ToArray();
			var soundErrors = soundResults
				.Where(result => result.success == false)
				.Take(50)
				.ToArray();
			var classResolutionErrors = allDefs
				.SelectMany(DescribeDefClassResolution)
				.Take(50)
				.ToArray();

			return new
			{
				success = allDefs.Count > 0
					&& databaseErrors.Count == 0
					&& configErrors.Length == 0
					&& graphicErrors.Length == 0
					&& pawnKindGraphicErrors.Length == 0
					&& soundErrors.Length == 0
					&& classResolutionErrors.Length == 0,
				mod = new
				{
					name = content.Name,
					packageId = content.PackageIdPlayerFacing
				},
				totalDefs = allDefs.Count,
				typeCounts = typeCounts.ToArray(),
				configErrorCount = configErrors.Length,
				configErrors,
				classResolutionErrorCount = classResolutionErrors.Length,
				classResolutionErrors,
				graphics = new
				{
					checkedThingDefs = graphicResults.Length,
					errorCount = graphicErrors.Length,
					errors = graphicErrors
				},
				pawnKindGraphics = new
				{
					checkedStages = pawnKindGraphicResults.Length,
					errorCount = pawnKindGraphicErrors.Length,
					errors = pawnKindGraphicErrors
				},
				sounds = new
				{
					checkedDefs = soundResults.Length,
					errorCount = soundErrors.Length,
					errors = soundErrors,
					sustainDefs = soundResults
						.Where(result => result is SoundResolutionSnapshot snapshot && snapshot.sustain)
						.Select(result => ((SoundResolutionSnapshot)result).defName)
						.ToArray()
				},
				databaseErrorCount = databaseErrors.Count,
				databaseErrors = databaseErrors.ToArray()
			};
		}

		static bool IsZombielandDef(Def def, ModContentPack content)
		{
			return def?.modContentPack == content
				|| string.Equals(def?.modContentPack?.PackageIdPlayerFacing, "brrainz.zombieland", StringComparison.OrdinalIgnoreCase);
		}

		static IEnumerable<object> DescribeDefConfigErrors(Def def)
		{
			IEnumerable<string> errors;
			try
			{
				errors = def.ConfigErrors() ?? Enumerable.Empty<string>();
			}
			catch (Exception ex)
			{
				return new object[]
				{
					new
					{
						def = DescribeDefIdentity(def),
						error = ex.GetType().Name + ": " + ex.Message
					}
				};
			}

			return errors
				.Where(error => error.NullOrEmpty() == false)
				.Select(error => new
				{
					def = DescribeDefIdentity(def),
					error
				});
		}

		static IEnumerable<object> DescribeDefClassResolution(Def def)
		{
			foreach (var fieldName in new[] { "thingClass", "workerClass", "driverClass", "stateClass", "needClass", "hediffClass", "giverClass" })
			{
				var field = def.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (field == null || field.GetValue(def) is not Type type)
					continue;
				if (type.Assembly == null)
				{
					yield return new
					{
						def = DescribeDefIdentity(def),
						field = fieldName,
						type = type.FullName,
						error = "Type has no assembly."
					};
				}
			}
		}

		static DefGraphicResolutionSnapshot DescribeThingGraphicResolution(ThingDef def)
		{
			try
			{
				var graphic = def.graphicData.Graphic;
				var material = graphic?.MatSingle;
				var uiIcon = def.uiIcon;
				var success = graphic != null && material.NullOrBad() == false;
				return new DefGraphicResolutionSnapshot
				{
					success = success,
					defName = def.defName,
					type = def.GetType().FullName,
					texPath = def.graphicData.texPath,
					graphicClass = def.graphicData.graphicClass?.FullName,
					graphicType = graphic?.GetType().FullName,
					material = material?.name,
					uiIcon = uiIcon?.name,
					uiIconBad = uiIcon.NullOrBad(),
					error = success ? null : "Graphic or material resolved null/bad."
				};
			}
			catch (Exception ex)
			{
				return new DefGraphicResolutionSnapshot
				{
					success = false,
					defName = def.defName,
					type = def.GetType().FullName,
					texPath = def.graphicData?.texPath,
					graphicClass = def.graphicData?.graphicClass?.FullName,
					error = ex.GetType().Name + ": " + ex.Message
				};
			}
		}

		static IEnumerable<DefGraphicResolutionSnapshot> DescribePawnKindGraphicResolution(PawnKindDef def)
		{
			if (def.lifeStages == null)
				yield break;
			for (var i = 0; i < def.lifeStages.Count; i++)
			{
				var graphicData = def.lifeStages[i]?.bodyGraphicData;
				if (graphicData == null)
					continue;
				yield return DescribePawnKindGraphicResolution(def, i, graphicData);
			}
		}

		static DefGraphicResolutionSnapshot DescribePawnKindGraphicResolution(PawnKindDef def, int stageIndex, GraphicData graphicData)
		{
			try
			{
				var graphic = graphicData.Graphic;
				var material = graphic?.MatSingle;
				var success = graphic != null && material.NullOrBad() == false;
				return new DefGraphicResolutionSnapshot
				{
					success = success,
					defName = def.defName,
					type = def.GetType().FullName,
					stageIndex = stageIndex,
					texPath = graphicData.texPath,
					graphicClass = graphicData.graphicClass?.FullName,
					graphicType = graphic?.GetType().FullName,
					material = material?.name,
					error = success ? null : "PawnKind body graphic or material resolved null/bad."
				};
			}
			catch (Exception ex)
			{
				return new DefGraphicResolutionSnapshot
				{
					success = false,
					defName = def.defName,
					type = def.GetType().FullName,
					stageIndex = stageIndex,
					texPath = graphicData.texPath,
					graphicClass = graphicData.graphicClass?.FullName,
					error = ex.GetType().Name + ": " + ex.Message
				};
			}
		}

		static SoundResolutionSnapshot DescribeSoundDefResolution(SoundDef def)
		{
			try
			{
				var subSounds = def.subSounds ?? new List<SubSoundDef>();
				var subSoundSnapshots = subSounds
					.Select((subSound, index) => DescribeSubSoundResolution(def, subSound, index))
					.ToArray();
				var success = def.isUndefined == false
					&& subSounds.Count > 0
					&& subSoundSnapshots.All(snapshot => snapshot.success)
					&& (def.sustainStartSound == null || def.sustainStartSound.isUndefined == false)
					&& (def.sustainStopSound == null || def.sustainStopSound.isUndefined == false)
					&& (def.sustainFadeoutStartSound == null || def.sustainFadeoutStartSound.isUndefined == false);
				return new SoundResolutionSnapshot
				{
					success = success,
					defName = def.defName,
					sustain = def.sustain,
					subSoundCount = subSounds.Count,
					totalResolvedGrains = subSoundSnapshots.Sum(snapshot => snapshot.resolvedGrainCount),
					sustainStartSound = def.sustainStartSound?.defName,
					sustainStopSound = def.sustainStopSound?.defName,
					sustainFadeoutStartSound = def.sustainFadeoutStartSound?.defName,
					subSounds = subSoundSnapshots,
					error = success ? null : "SoundDef has no subsounds, unresolved grains, or undefined sustain references."
				};
			}
			catch (Exception ex)
			{
				return new SoundResolutionSnapshot
				{
					success = false,
					defName = def.defName,
					error = ex.GetType().Name + ": " + ex.Message
				};
			}
		}

		static SubSoundResolutionSnapshot DescribeSubSoundResolution(SoundDef parent, SubSoundDef subSound, int index)
		{
			var resolvedGrainCount = 0;
			var error = (string)null;
			try
			{
				var field = typeof(SubSoundDef).GetField("resolvedGrains", BindingFlags.Instance | BindingFlags.NonPublic);
				if (field?.GetValue(subSound) is ICollection grains)
					resolvedGrainCount = grains.Count;
				else
					error = "Could not read private resolvedGrains collection.";
			}
			catch (Exception ex)
			{
				error = ex.GetType().Name + ": " + ex.Message;
			}

			return new SubSoundResolutionSnapshot
			{
				success = error == null && subSound.grains.Count > 0 && resolvedGrainCount > 0,
				index = index,
				name = subSound.name,
				grains = subSound.grains.Count,
				resolvedGrainCount = resolvedGrainCount,
				error = error ?? (subSound.grains.Count == 0 ? "SubSound has no grains." : resolvedGrainCount == 0 ? $"{parent.defName} subSound has no resolved grains." : null)
			};
		}

		static object DescribeDefIdentity(Def def)
		{
			return new
			{
				defName = def?.defName,
				type = def?.GetType().FullName,
				fileName = def?.fileName
			};
		}

		sealed class DefGraphicResolutionSnapshot
		{
			public bool success;
			public string defName;
			public string type;
			public int? stageIndex;
			public string texPath;
			public string graphicClass;
			public string graphicType;
			public string material;
			public string uiIcon;
			public bool uiIconBad;
			public string error;
		}

		sealed class SoundResolutionSnapshot
		{
			public bool success;
			public string defName;
			public bool sustain;
			public int subSoundCount;
			public int totalResolvedGrains;
			public string sustainStartSound;
			public string sustainStopSound;
			public string sustainFadeoutStartSound;
			public SubSoundResolutionSnapshot[] subSounds;
			public string error;
		}

		sealed class SubSoundResolutionSnapshot
		{
			public bool success;
			public int index;
			public string name;
			public int grains;
			public int resolvedGrainCount;
			public string error;
		}

		static object VerifyStartupAssets()
		{
			var dustInstantiated = false;
			string dustError = null;
			string dustName = null;
			bool dustHasParticleSystem = false;
			bool dustHasRenderer = false;
			GameObject dust = null;
			try
			{
				dust = Assets.NewDust();
				dustInstantiated = dust != null;
				dustName = dust?.name;
				dustHasParticleSystem = dust?.GetComponent<ParticleSystem>() != null;
				dustHasRenderer = dust?.GetComponent<ParticleSystemRenderer>() != null;
			}
			catch (Exception ex)
			{
				dustError = ex.GetType().Name + ": " + ex.Message;
			}
			finally
			{
				if (dust != null)
					UnityEngine.Object.Destroy(dust);
			}

			return new
			{
				success = Assets.initialized
					&& Assets.MetaballShader != null
					&& Assets.ZombieSymbiantShader != null
					&& dustInstantiated
					&& dustHasParticleSystem
					&& dustHasRenderer,
				Assets.initialized,
				metaballShader = Assets.MetaballShader?.name,
				zombieSymbiantShader = Assets.ZombieSymbiantShader?.name,
				dust = new
				{
					instantiated = dustInstantiated,
					name = dustName,
					hasParticleSystem = dustHasParticleSystem,
					hasRenderer = dustHasRenderer,
					error = dustError
				}
			};
		}

		static object VerifyLegacyAreaRiskModeParsing()
		{
			object ifInside = null;
			object ifOutside = null;
			object currentName = null;
			string error = null;
			try
			{
				ifInside = ParseHelper.FromString("IfInside", typeof(AreaRiskMode));
				ifOutside = ParseHelper.FromString("IfOutside", typeof(AreaRiskMode));
				currentName = ParseHelper.FromString(nameof(AreaRiskMode.ZombieInside), typeof(AreaRiskMode));
			}
			catch (Exception ex)
			{
				error = ex.GetType().Name + ": " + ex.Message;
			}

			return new
			{
				success = error == null
					&& ifInside is AreaRiskMode insideMode
					&& insideMode == AreaRiskMode.ColonistInside
					&& ifOutside is AreaRiskMode outsideMode
					&& outsideMode == AreaRiskMode.ColonistOutside
					&& currentName is AreaRiskMode currentMode
					&& currentMode == AreaRiskMode.ZombieInside,
				legacy = new
				{
					ifInside = ifInside?.ToString(),
					expectedIfInside = AreaRiskMode.ColonistInside.ToString(),
					ifOutside = ifOutside?.ToString(),
					expectedIfOutside = AreaRiskMode.ColonistOutside.ToString()
				},
				current = new
				{
					zombieInside = currentName?.ToString()
				},
				error
			};
		}

		static object VerifyMainMenuStartupErrors()
		{
			var errorsField = typeof(Patches).GetField("errors", BindingFlags.Static | BindingFlags.NonPublic);
			var errorCount = -1;
			if (errorsField?.GetValue(null) is System.Collections.ICollection errors)
				errorCount = errors.Count;
			var errorDialogOpen = Find.WindowStack?.IsOpen(typeof(Dialog_ErrorMessage)) == true;
			var patchFailureDialogOpen = Find.WindowStack?.IsOpen(typeof(Dialog_PatchGroupFailures)) == true;
			var patchGroups = PatchGroups.Current
				.Select(result => new
				{
					result.Id,
					result.Label,
					state = result.State.ToString(),
					result.Order,
					result.IsFailure,
					patchTypeCount = result.PatchTypes.Count,
					failedPatchTypes = result.FailedPatchTypes.ToArray(),
					result.Summary
				})
				.OrderBy(result => result.Order)
				.ToArray();
			var patchGroupFailures = patchGroups
				.Where(result => result.IsFailure)
				.ToArray();

			return new
			{
				success = errorCount == 0 && errorDialogOpen == false && patchFailureDialogOpen == false && patchGroupFailures.Length == 0,
				errorCount,
				errorDialogOpen,
				patchFailureDialogOpen,
				patchGroupFailureCount = patchGroupFailures.Length,
				patchGroups,
				patchGroupFailures,
				windowStackAvailable = Find.WindowStack != null
			};
		}

		static object VerifyTimeControlService()
		{
			var postfix = typeof(TimeControlService).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic);
			var curTimeSpeedField = typeof(TimeControlService).GetField("curTimeSpeed", BindingFlags.Static | BindingFlags.NonPublic);
			var tickManager = Current.Game?.tickManager;
			if (postfix == null || curTimeSpeedField == null || tickManager == null)
			{
				return new
				{
					success = false,
					reflection = new
					{
						postfix = postfix != null,
						curTimeSpeedField = curTimeSpeedField != null,
						tickManager = tickManager != null
					}
				};
			}

			var subscriber = new object();
			var notifications = new List<string>();
			var originalCurTimeSpeed = curTimeSpeedField.GetValue(null);
			try
			{
				curTimeSpeedField.SetValue(null, null);
				TimeControlService.Subscribe(subscriber, speed => notifications.Add(speed.ToString()));
				postfix.Invoke(null, Array.Empty<object>());
				postfix.Invoke(null, Array.Empty<object>());
				TimeControlService.Unsubscribe(subscriber);
			}
			finally
			{
				TimeControlService.Unsubscribe(subscriber);
				curTimeSpeedField.SetValue(null, originalCurTimeSpeed);
			}

			return new
			{
				success = notifications.Count == 1
					&& notifications[0] == tickManager.CurTimeSpeed.ToString(),
				currentTimeSpeed = tickManager.CurTimeSpeed.ToString(),
				notifications = notifications.ToArray()
			};
		}

		static object VerifyClearMapsService()
		{
			var prefix = typeof(ClearMapsService).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic);
			if (prefix == null)
			{
				return new
				{
					success = false,
					reflection = new
					{
						prefix = false
					}
				};
			}

			var subscriber = new object();
			var callbackCount = 0;
			try
			{
				ClearMapsService.Subscribe(subscriber, () => callbackCount++);
				prefix.Invoke(null, Array.Empty<object>());
				ClearMapsService.Unsubscribe(subscriber);
			}
			finally
			{
				ClearMapsService.Unsubscribe(subscriber);
			}

			return new
			{
				success = callbackCount == 1,
				callbackCount,
				note = "Invoked the Zombieland prefix directly to avoid clearing the live game."
			};
		}

		static object VerifyRootLifecycleHooks()
		{
			var rootUpdatePrefix = typeof(Patches)
				.GetNestedType("Root_Update_Patch", BindingFlags.NonPublic)
				?.GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic);
			var rootShutdownPrefix = typeof(Patches)
				.GetNestedType("Root_Shutdown_Patch", BindingFlags.NonPublic)
				?.GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic);
			if (rootUpdatePrefix == null || rootShutdownPrefix == null)
			{
				return new
				{
					success = false,
					reflection = new
					{
						rootUpdatePrefix = rootUpdatePrefix != null,
						rootShutdownPrefix = rootShutdownPrefix != null
					}
				};
			}

			var originalAvoiderRunning = Tools.avoider.running;
			var frameWatch = ZombielandMod.frameWatch;
			var wasRunningBeforeReset = frameWatch.IsRunning;
			try
			{
				frameWatch.Reset();
				var runningAfterReset = frameWatch.IsRunning;
				var elapsedAfterReset = frameWatch.ElapsedTicks;
				rootUpdatePrefix.Invoke(null, Array.Empty<object>());
				var runningAfterUpdatePrefix = frameWatch.IsRunning;
				var elapsedAfterUpdatePrefix = frameWatch.ElapsedTicks;

				Tools.avoider.running = true;
				rootShutdownPrefix.Invoke(null, Array.Empty<object>());
				var avoiderRunningAfterShutdownPrefix = Tools.avoider.running;

				return new
				{
					success = runningAfterReset == false
						&& elapsedAfterReset == 0
						&& runningAfterUpdatePrefix
						&& avoiderRunningAfterShutdownPrefix == false,
					frameWatch = new
					{
						wasRunningBeforeReset,
						runningAfterReset,
						elapsedAfterReset,
						runningAfterUpdatePrefix,
						elapsedAfterUpdatePrefix
					},
					avoider = new
					{
						originalRunning = originalAvoiderRunning,
						runningBeforeShutdownPrefix = true,
						runningAfterShutdownPrefix = avoiderRunningAfterShutdownPrefix
					},
					note = "Invoked only Zombieland root prefixes; vanilla Root.Shutdown was not called."
				};
			}
			finally
			{
				Tools.avoider.running = originalAvoiderRunning;
				if (wasRunningBeforeReset)
					frameWatch.Restart();
				else
					frameWatch.Stop();
			}
		}

		static object DescribeZombieGridDensity(Map map)
		{
			if (map == null)
				return null;

			var grid = map.GetComponent<PheromoneGrid>();
			if (grid == null)
				return new
				{
					available = false,
					nonZeroCells = 0,
					totalZombieCount = 0,
					maxZombieCount = 0,
					mapArea = map.Size.x * map.Size.z,
					nonZeroCellDensity = 0f,
					zombieCountDensity = 0f
				};

			var nonZeroCells = 0;
			var totalZombieCount = 0;
			var maxZombieCount = 0;
			grid.IterateCellsQuick(cell =>
			{
				if (cell.zombieCount == 0)
					return;
				nonZeroCells++;
				totalZombieCount += cell.zombieCount;
				maxZombieCount = Math.Max(maxZombieCount, cell.zombieCount);
			});

			var area = map.Size.x * map.Size.z;
			return new
			{
				available = true,
				nonZeroCells,
				totalZombieCount,
				maxZombieCount,
				mapArea = area,
				nonZeroCellDensity = area == 0 ? 0f : nonZeroCells / (float)area,
				zombieCountDensity = area == 0 ? 0f : totalZombieCount / (float)area
			};
		}

		static object TryReadUnityProfilerMemory()
		{
			try
			{
				var profilerType = AccessTools.TypeByName("UnityEngine.Profiling.Profiler");
				if (profilerType == null)
					return new
					{
						available = false,
						error = "UnityEngine.Profiling.Profiler type was not found."
					};

				long? ReadLong(string methodName)
				{
					var method = AccessTools.Method(profilerType, methodName, Type.EmptyTypes);
					if (method == null)
						return null;
					var value = method.Invoke(null, Array.Empty<object>());
					return value == null ? null : Convert.ToInt64(value);
				}

				return new
				{
					available = true,
					totalAllocatedMemoryBytes = ReadLong("GetTotalAllocatedMemoryLong"),
					totalReservedMemoryBytes = ReadLong("GetTotalReservedMemoryLong"),
					totalUnusedReservedMemoryBytes = ReadLong("GetTotalUnusedReservedMemoryLong"),
					monoUsedSizeBytes = ReadLong("GetMonoUsedSizeLong"),
					monoHeapSizeBytes = ReadLong("GetMonoHeapSizeLong")
				};
			}
			catch (Exception ex)
			{
				return new
				{
					available = false,
					error = ex.GetType().FullName + ": " + ex.Message
				};
			}
		}

		static object DescribeZombieGrid(Map map, Pawn[] zombies)
		{
			if (map == null)
				return null;

			var grid = map.GetGrid();
			var nonZeroCells = 0;
			var totalZombieCount = 0;
			var maxZombieCount = 0;
			grid.IterateCells((_, _, cell) =>
			{
				if (cell.zombieCount == 0)
					return;
				nonZeroCells++;
				totalZombieCount += cell.zombieCount;
				maxZombieCount = Math.Max(maxZombieCount, cell.zombieCount);
			});

			var liveOrdinaryZombies = zombies.OfType<Zombie>()
				.Where(zombie => zombie.Spawned && zombie.Dead == false)
				.ToArray();
			var liveOrdinaryWithGridCount = liveOrdinaryZombies.Count(zombie =>
				grid.GetZombieCount(zombie.Position) > 0
				|| (zombie.lastGotoPosition.IsValid && grid.GetZombieCount(zombie.lastGotoPosition) > 0));

			return new
			{
				nonZeroCells,
				totalZombieCount,
				maxZombieCount,
				liveOrdinaryZombieCount = liveOrdinaryZombies.Length,
				liveOrdinaryWithGridCount,
				liveOrdinaryWithoutGridCount = liveOrdinaryZombies.Length - liveOrdinaryWithGridCount,
				samples = liveOrdinaryZombies
					.OrderBy(zombie => zombie.ThingID, StringComparer.Ordinal)
					.Take(12)
					.Select(zombie => new
					{
						pawnId = ZombieRuntimeActions.StableThingId(zombie),
						thingId = zombie.ThingID,
						position = ZombieRuntimeActions.DescribeCell(zombie.Position),
						lastGotoPosition = zombie.lastGotoPosition.IsValid ? ZombieRuntimeActions.DescribeCell(zombie.lastGotoPosition) : null,
						gridAtPosition = grid.GetZombieCount(zombie.Position),
						gridAtLastGotoPosition = zombie.lastGotoPosition.IsValid ? grid.GetZombieCount(zombie.lastGotoPosition) : 0
					})
					.ToArray()
			};
		}

		static object DescribeIdeologyLoadState(Map map, Pawn[] zombies)
		{
			var patchTargets = PatchedMethodsForPatchClass("Pawn_IdeoTracker_ExposeData_Patch");
			var pawns = map?.mapPawns?.AllPawnsSpawned ?? Enumerable.Empty<Pawn>();
			var livingHumans = pawns
				.Where(pawn => pawn?.Destroyed == false && pawn.Dead == false && pawn.RaceProps?.Humanlike == true)
				.ToArray();
			var nonZombieHumans = livingHumans
				.Where(pawn => ZombieAreaManager.IsZombielandPawn(pawn) == false)
				.ToArray();
			var zombiePawns = zombies
				.Where(pawn => pawn?.Destroyed == false && pawn.Dead == false)
				.ToArray();

			object DescribeIdeoPawn(Pawn pawn)
			{
				return new
				{
					pawnId = ZombieRuntimeActions.StableThingId(pawn),
					thingId = pawn.ThingID,
					label = pawn.LabelShort,
					defName = pawn.def?.defName,
					kindDef = pawn.kindDef?.defName,
					shouldHaveIdeo = pawn.ShouldHaveIdeo,
					hasIdeoTracker = pawn.ideo != null,
					hasIdeo = pawn.ideo?.Ideo != null,
					ideoName = pawn.ideo?.Ideo?.name,
					certainty = pawn.ideo?.Certainty
				};
			}

			return new
			{
				ideologyInstalled = ModsConfig.IdeologyActive,
				patchTargets,
				nonZombieHumanCount = nonZombieHumans.Length,
				nonZombieHumansWithIdeoTracker = nonZombieHumans.Count(pawn => pawn.ideo != null),
				nonZombieHumansShouldHaveIdeo = nonZombieHumans.Count(pawn => pawn.ShouldHaveIdeo),
				nonZombieHumansWithIdeo = nonZombieHumans.Count(pawn => pawn.ideo?.Ideo != null),
				zombielandPawnCount = zombiePawns.Length,
				zombielandPawnsWithIdeoTracker = zombiePawns.Count(pawn => pawn.ideo != null),
				zombielandPawnsShouldHaveIdeo = zombiePawns.Count(pawn => pawn.ShouldHaveIdeo),
				zombielandPawnsWithIdeo = zombiePawns.Count(pawn => pawn.ideo?.Ideo != null),
				nonZombieSamples = nonZombieHumans
					.OrderBy(pawn => pawn.ThingID, StringComparer.Ordinal)
					.Take(6)
					.Select(DescribeIdeoPawn)
					.ToArray(),
				zombieSamples = zombiePawns
					.OrderBy(pawn => pawn.ThingID, StringComparer.Ordinal)
					.Take(6)
					.Select(DescribeIdeoPawn)
					.ToArray()
			};
		}

		[Tool("zombieland/list_zombies", Description = "List spawned Zombieland pawns on the current map with stable ids and compact state.")]
		public static object ListZombies([ToolParameter(Description = "Maximum number of zombies to return.", Required = false, DefaultValue = 100)] int limit = 100)
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

			var cappedLimit = Math.Max(1, Math.Min(limit, 500));
			var zombies = CurrentZombies(map)
				.OrderBy(pawn => pawn.ThingID, StringComparer.Ordinal)
				.Take(cappedLimit)
				.Select(DescribeZombie)
				.ToArray();

			return new
			{
				success = true,
				count = zombies.Length,
				limit = cappedLimit,
				zombies
			};
		}

		[Tool("zombieland/sound_events_state", Description = "Start, stop, clear, or read RimWorld's built-in debug sound-event recorder for reusable audio trigger evidence.")]
		public static object SoundEventsState(
			[ToolParameter(Description = "Action to perform: begin, read, clear, or end.", Required = false, DefaultValue = "read")] string action = "read",
			[ToolParameter(Description = "Optional case-insensitive substring used to filter returned event lines.", Required = false, DefaultValue = "")] string filter = "",
			[ToolParameter(Description = "Maximum number of event lines to return.", Required = false, DefaultValue = 80)] int limit = 80)
		{
			var normalized = (action ?? "read").Trim().ToLowerInvariant();
			if (normalized != "begin" && normalized != "read" && normalized != "clear" && normalized != "end")
			{
				return new
				{
					success = false,
					error = "Unsupported action. Use begin, read, clear, or end.",
					writeSoundEventsRecord = DebugViewSettings.writeSoundEventsRecord
				};
			}

			var clearSucceeded = false;
			if (normalized == "begin")
			{
				if (soundEventsPreviousWriteState.HasValue == false)
					soundEventsPreviousWriteState = DebugViewSettings.writeSoundEventsRecord;
				DebugViewSettings.writeSoundEventsRecord = true;
				clearSucceeded = ClearDebugSoundEvents();
			}
			else if (normalized == "clear")
				clearSucceeded = ClearDebugSoundEvents();
			else if (normalized == "end")
			{
				if (soundEventsPreviousWriteState.HasValue)
					DebugViewSettings.writeSoundEventsRecord = soundEventsPreviousWriteState.Value;
				else
					DebugViewSettings.writeSoundEventsRecord = false;
				soundEventsPreviousWriteState = null;
			}

			var cappedLimit = Math.Max(1, Math.Min(limit, 500));
			var filterText = (filter ?? "").Trim();
			var events = ReadDebugSoundEventLines()
				.Where(line => filterText.Length == 0 || line.IndexOf(filterText, StringComparison.OrdinalIgnoreCase) >= 0)
				.Take(cappedLimit)
				.ToArray();

			return new
			{
				success = true,
				action = normalized,
				writeSoundEventsRecord = DebugViewSettings.writeSoundEventsRecord,
				previousWriteSoundEventsRecord = soundEventsPreviousWriteState,
				clearSucceeded = normalized == "begin" || normalized == "clear" ? clearSucceeded : null as bool?,
				filter = filterText,
				count = events.Length,
				limit = cappedLimit,
				events
			};
		}

		static bool ClearDebugSoundEvents()
		{
			var queue = AccessTools.Field(typeof(DebugSoundEventsLog), "queue")?.GetValue(null);
			var clear = queue == null ? null : AccessTools.Method(queue.GetType(), "Clear");
			if (queue == null || clear == null)
				return false;
			clear.Invoke(queue, Array.Empty<object>());
			return true;
		}

		static string[] ReadDebugSoundEventLines()
		{
			var text = DebugSoundEventsLog.EventsListingDebugString ?? "";
			return text
				.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(line => line.Trim())
				.Where(line => line.Length > 0)
				.ToArray();
		}

		[Tool("zombieland/defensive_defaults_contract", Description = "Verify legacy defensive defaults do not throw when old/corrupt enum-style state is encountered.")]
		public static object DefensiveDefaultsContract()
		{
			var brainzStage = new BrainzThought().CurStageIndex;
			var invalidUnitTicks = new SettingsKeyFrame
			{
				amount = 2,
				unit = (SettingsKeyFrame.Unit)999
			}.Ticks;
			var expectedInvalidUnitTicks = 2 * GenDate.TicksPerDay;

			if (TryFindSpawnCell(-1, -1, out var map, out var cell, out var spawnError) == false)
				return spawnError;

			var faction = Find.FactionManager.AllFactions
				.FirstOrDefault(candidate => candidate != null && candidate != Faction.OfPlayer && candidate.def != null);
			if (faction == null)
			{
				return new
				{
					success = false,
					error = "No non-player faction was available for the invalid attack-mode hostility check."
				};
			}

			var oldAttackMode = ZombieSettings.Values.attackMode;
			var oldEnemiesAttackZombies = ZombieSettings.Values.enemiesAttackZombies;
			Zombie zombie = null;
			try
			{
				zombie = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.Normal, true);
				if (zombie == null)
				{
					return new
					{
						success = false,
						cell = ZombieRuntimeActions.DescribeCell(cell),
						error = "ZombieGenerator.SpawnZombie returned no defensive-defaults test zombie."
					};
				}

				ZombieSettings.Values.attackMode = (AttackMode)999;
				ZombieSettings.Values.enemiesAttackZombies = true;
				var invalidAttackModeThreat = GenHostility.IsActiveThreatTo(zombie, faction, false, false);

				var success = brainzStage == 0
					&& invalidUnitTicks == expectedInvalidUnitTicks
					&& invalidAttackModeThreat == false;
				return new
				{
					success,
					brainzStage,
					invalidUnitTicks,
					expectedInvalidUnitTicks,
					faction = faction.def.defName,
					invalidAttackModeThreat,
					zombie = DescribeZombie(zombie)
				};
			}
			finally
			{
				ZombieSettings.Values.attackMode = oldAttackMode;
				ZombieSettings.Values.enemiesAttackZombies = oldEnemiesAttackZombies;
				if (zombie != null && zombie.Destroyed == false)
					zombie.Destroy(DestroyMode.Vanish);
			}
		}

		[Tool("zombieland/brainz_thought_bubble_contract", Description = "Verify the BRRAINZ zombie thought bubble spawns the custom ZombieThought mote with the expected icon material.")]
		public static object BrainzThoughtBubbleContract()
		{
			if (TryFindSpawnCell(-1, -1, out var map, out var cell, out var spawnError) == false)
				return spawnError;

			var realtimeMotes = RealTime.moteList?.allMotes ?? new List<Mote>();
			var existingThoughts = realtimeMotes
				.Where(thing => thing.def == CustomDefs.ZombieThought)
				.ToHashSet();

			Zombie zombie = null;
			MoteBubble[] spawnedThoughts = Array.Empty<MoteBubble>();
			try
			{
				zombie = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.Normal, true);
				if (zombie == null)
				{
					return new
					{
						success = false,
						cell = ZombieRuntimeActions.DescribeCell(cell),
						error = "ZombieGenerator.SpawnZombie returned no thought-bubble test zombie."
					};
				}

				ZombieStateHandler.CastBrainzThought(zombie);
				spawnedThoughts = realtimeMotes
					.OfType<MoteBubble>()
					.Where(mote => mote.def == CustomDefs.ZombieThought && existingThoughts.Contains(mote) == false)
					.ToArray();
				var thought = spawnedThoughts
					.OrderBy(mote => mote.Position.DistanceToSquared(zombie.Position))
					.FirstOrDefault();

				var thoughtPosition = thought?.Position ?? IntVec3.Invalid;
				var success = thought != null
					&& thought.Spawned
					&& thought.Map == map
					&& thoughtPosition == zombie.Position
					&& thought.iconMat == Constants.BRRAINZ;

				return new
				{
					success,
					zombie = DescribeZombie(zombie),
					thoughtCountBefore = existingThoughts.Count,
					spawnedThoughtCount = spawnedThoughts.Length,
					thoughtThingId = thought?.ThingID,
					thoughtSpawned = thought?.Spawned ?? false,
					thoughtPosition = thoughtPosition.IsValid ? ZombieRuntimeActions.DescribeCell(thoughtPosition) : null,
					expectedPosition = ZombieRuntimeActions.DescribeCell(zombie.Position),
					iconMaterial = thought?.iconMat?.name,
					expectedMaterial = Constants.BRRAINZ.name
				};
			}
			finally
			{
				foreach (var thought in spawnedThoughts)
					if (thought.Destroyed == false)
						thought.Destroy(DestroyMode.Vanish);
				if (zombie != null && zombie.Destroyed == false)
					zombie.Destroy(DestroyMode.Vanish);
			}
		}

		[Tool("zombieland/zombie_thought_bubble_materials_contract", Description = "Verify every custom zombie thought-bubble material spawns through RimWorld's realtime mote path.")]
		public static object ZombieThoughtBubbleMaterialsContract()
		{
			if (TryFindSpawnCell(-1, -1, out var map, out var cell, out var spawnError) == false)
				return spawnError;

			var cases = new (string label, Material material, Action<Pawn> cast)[]
			{
				("BRRAINZ", Constants.BRRAINZ, ZombieStateHandler.CastBrainzThought),
				("Eating", Constants.EATING, pawn => Tools.CastThoughtBubble(pawn, Constants.EATING)),
				("Hacking", Constants.HACKING, pawn => Tools.CastThoughtBubble(pawn, Constants.HACKING)),
				("Raging", Constants.RAGING, pawn => Tools.CastThoughtBubble(pawn, Constants.RAGING))
			};

			var realtimeMotes = RealTime.moteList?.allMotes ?? new List<Mote>();
			Zombie zombie = null;
			var spawnedThoughts = new List<MoteBubble>();
			try
			{
				zombie = ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.Normal, true);
				if (zombie == null)
				{
					return new
					{
						success = false,
						cell = ZombieRuntimeActions.DescribeCell(cell),
						error = "ZombieGenerator.SpawnZombie returned no thought-bubble material test zombie."
					};
				}

				var results = cases.Select(testCase =>
				{
					var before = realtimeMotes
						.Where(mote => mote.def == CustomDefs.ZombieThought)
						.ToHashSet();

					testCase.cast(zombie);
					var newThoughts = realtimeMotes
						.OfType<MoteBubble>()
						.Where(mote => mote.def == CustomDefs.ZombieThought && before.Contains(mote) == false)
						.ToArray();
					spawnedThoughts.AddRange(newThoughts);

					var thought = newThoughts
						.OrderBy(mote => mote.Position.DistanceToSquared(zombie.Position))
						.FirstOrDefault();
					var thoughtPosition = thought?.Position ?? IntVec3.Invalid;
					var materialMatches = thought?.iconMat == testCase.material;
					var positionMatches = thoughtPosition == zombie.Position;
					var ok = thought != null
						&& thought.Spawned
						&& thought.Map == map
						&& positionMatches
						&& materialMatches;

					return new
					{
						label = testCase.label,
						success = ok,
						expectedMaterial = testCase.material.name,
						iconMaterial = thought?.iconMat?.name,
						materialMatches,
						spawnedThoughtCount = newThoughts.Length,
						thoughtThingId = thought?.ThingID,
						thoughtSpawned = thought?.Spawned ?? false,
						thoughtPosition = thoughtPosition.IsValid ? ZombieRuntimeActions.DescribeCell(thoughtPosition) : null,
						expectedPosition = ZombieRuntimeActions.DescribeCell(zombie.Position),
						positionMatches
					};
				}).ToArray();

				return new
				{
					success = results.All(result => result.success),
					zombie = DescribeZombie(zombie),
					results
				};
			}
			finally
			{
				foreach (var thought in spawnedThoughts)
					if (thought.Destroyed == false)
						thought.Destroy(DestroyMode.Vanish);
				if (zombie != null && zombie.Destroyed == false)
					zombie.Destroy(DestroyMode.Vanish);
			}
		}

		[Tool("zombieland/spawn_zombie", Description = "Spawn one Zombieland zombie near a map cell for runtime smoke tests.")]
		public static object SpawnZombie(
			[ToolParameter(Description = "Target x coordinate. Use -1 with z -1 to spawn near map center.", Required = false, DefaultValue = -1)] int x = -1,
			[ToolParameter(Description = "Target z coordinate. Use -1 with x -1 to spawn near map center.", Required = false, DefaultValue = -1)] int z = -1,
			[ToolParameter(Description = "Zombie type name, for example Normal, Random, SuicideBomber, ToxicSplasher, TankyOperator, Miner, Electrifier, Albino, DarkSlimer, or Healer.", Required = false, DefaultValue = "Normal")] string type = "Normal",
			[ToolParameter(Description = "When true, skip the underground dig-out state and spawn the zombie standing.", Required = false, DefaultValue = true)] bool appearDirectly = true,
			[ToolParameter(Description = "Optional zombie-count grid value to seed at the spawned cell for save/load and pathing fixtures. Zero leaves the grid untouched.", Required = false, DefaultValue = 0)] int primeGridCount = 0)
		{
			if (TryParseZombieType(type, out var zombieType, out var parseError) == false)
			{
				return new
				{
					success = false,
					error = parseError
				};
			}

			if (TryFindSpawnCell(x, z, out var map, out var cell, out var error) == false)
				return error;

			var zombie = ZombieRuntimeActions.SpawnZombie(cell, map, zombieType, appearDirectly);
			if (zombie == null)
			{
				return new
				{
					success = false,
					error = "ZombieGenerator.SpawnZombie returned no zombie."
				};
			}

			var tickManager = map.GetComponent<TickManager>();
			var grid = map.GetGrid();
			var primedGridCount = 0;
			if (primeGridCount > 0)
			{
				var current = grid.GetZombieCount(cell);
				if (current != 0)
					grid.ChangeZombieCount(cell, -current);
				grid.ChangeZombieCount(cell, primeGridCount);
				zombie.lastGotoPosition = cell;
				primedGridCount = grid.GetZombieCount(cell);
			}
			return new
			{
				success = zombie.Spawned,
				requestedCell = ZombieRuntimeActions.DescribeCell(new IntVec3(x, 0, z)),
				spawnCell = ZombieRuntimeActions.DescribeCell(cell),
				appearDirectly,
				primeGridCount,
				primedGridCount,
				zombie = DescribeZombie(zombie),
				tickManagerCached = tickManager?.allZombiesCached?.Contains(zombie) ?? false
			};
		}

		[Tool("zombieland/spawn_zombie_group", Description = "Spawn a bounded group of Zombieland zombies around a map cell for generic scenario and performance fixtures.")]
		public static object SpawnZombieGroup(
			[ToolParameter(Description = "Target center x coordinate. Use -1 with z -1 to spawn near map center.", Required = false, DefaultValue = -1)] int x = -1,
			[ToolParameter(Description = "Target center z coordinate. Use -1 with x -1 to spawn near map center.", Required = false, DefaultValue = -1)] int z = -1,
			[ToolParameter(Description = "Maximum number of zombies to spawn. Clamped to 1..200.", Required = false, DefaultValue = 50)] int count = 50,
			[ToolParameter(Description = "Search radius in cells around the center. Clamped to 3..60.", Required = false, DefaultValue = 18)] int radius = 18,
			[ToolParameter(Description = "Zombie type name, for example Normal, Random, SuicideBomber, ToxicSplasher, TankyOperator, Miner, Electrifier, Albino, DarkSlimer, or Healer.", Required = false, DefaultValue = "Normal")] string type = "Normal",
			[ToolParameter(Description = "When true, skip the underground dig-out state and spawn each zombie standing.", Required = false, DefaultValue = true)] bool appearDirectly = true,
			[ToolParameter(Description = "Optional zombie-count grid value to seed at each spawned cell. Zero leaves the grid untouched.", Required = false, DefaultValue = 0)] int primeGridCount = 0)
		{
			if (TryParseZombieType(type, out var zombieType, out var parseError) == false)
			{
				return new
				{
					success = false,
					error = parseError
				};
			}

			if (TryFindSpawnCell(x, z, out var map, out var center, out var error) == false)
				return error;

			var cappedCount = Math.Max(1, Math.Min(count, 200));
			var cappedRadius = Math.Max(3, Math.Min(radius, 60));
			var cells = GenRadial.RadialCellsAround(center, cappedRadius, true)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetThingList(map).Any(thing => thing is Pawn) == false)
				.Take(cappedCount)
				.ToArray();

			var tickManager = map.GetComponent<TickManager>();
			var grid = map.GetGrid();
			var spawned = new List<Pawn>(cells.Length);
			foreach (var cell in cells)
			{
				var zombie = ZombieRuntimeActions.SpawnZombie(cell, map, zombieType, appearDirectly);
				if (zombie == null)
					continue;

				if (primeGridCount > 0 && zombie is Zombie ordinary)
				{
					var current = grid.GetZombieCount(cell);
					if (current != 0)
						grid.ChangeZombieCount(cell, -current);
					grid.ChangeZombieCount(cell, primeGridCount);
					ordinary.lastGotoPosition = cell;
				}
				spawned.Add(zombie);
			}

			return new
			{
				success = spawned.Count == cappedCount,
				requestedCell = ZombieRuntimeActions.DescribeCell(new IntVec3(x, 0, z)),
				centerCell = ZombieRuntimeActions.DescribeCell(center),
				requestedCount = count,
				count = cappedCount,
				spawnedCount = spawned.Count,
				radius = cappedRadius,
				type = zombieType.ToString(),
				appearDirectly,
				primeGridCount,
				tickManagerCachedCount = tickManager?.allZombiesCached?.Count(zombie => spawned.Contains(zombie)) ?? 0,
				zombies = spawned.Take(20).Select(DescribeZombie).ToArray()
			};
		}

		[Tool("zombieland/pheromone_state", Description = "Read, clear, or set Zombieland pheromone timestamps in a bounded current-map area for generic senses/pathing fixtures.")]
		public static object PheromoneState(
			[ToolParameter(Description = "Action to perform: read, clear, or set.", Required = false, DefaultValue = "read")] string action = "read",
			[ToolParameter(Description = "Center x coordinate.", Required = false, DefaultValue = -1)] int x = -1,
			[ToolParameter(Description = "Center z coordinate.", Required = false, DefaultValue = -1)] int z = -1,
			[ToolParameter(Description = "Radius around the center. Use 0 for a single cell. Clamped to 0..60.", Required = false, DefaultValue = 0f)] float radius = 0f,
			[ToolParameter(Description = "For set action, timestamp age in RimWorld game ticks. Zero means current tick; values above the fadeoff create stale pheromones.", Required = false, DefaultValue = 0)] int ageGameTicks = 0,
			[ToolParameter(Description = "For set action, optional zombie-count value to write. Use -1 to leave zombie counts unchanged.", Required = false, DefaultValue = -1)] int zombieCount = -1,
			[ToolParameter(Description = "For clear action, also zero zombie-count values.", Required = false, DefaultValue = false)] bool clearZombieCounts = false,
			[ToolParameter(Description = "Maximum sample cells to return.", Required = false, DefaultValue = 20)] int maxSamples = 20)
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

			var center = x < 0 && z < 0 ? map.Center : new IntVec3(x, 0, z);
			if (center.InBounds(map) == false)
			{
				return new
				{
					success = false,
					error = $"Cell ({center.x}, {center.z}) is outside the current map."
				};
			}

			var normalizedAction = (action ?? "read").Trim().ToLowerInvariant();
			if (normalizedAction != "read" && normalizedAction != "clear" && normalizedAction != "set")
			{
				return new
				{
					success = false,
					error = "Unsupported action. Use read, clear, or set."
				};
			}

			var cappedRadius = Math.Max(0f, Math.Min(radius, 60f));
			var cells = (cappedRadius <= 0f ? new[] { center } : GenRadial.RadialCellsAround(center, cappedRadius, true))
				.Where(cell => cell.InBounds(map))
				.Distinct()
				.ToArray();
			var grid = map.GetGrid();
			var now = Tools.Ticks();
			var fadeoff = Tools.PheromoneFadeoff();
			var threshold = now - fadeoff;

			if (normalizedAction == "clear")
			{
				foreach (var cell in cells)
				{
					grid.SetTimestamp(cell, 0);
					if (clearZombieCounts)
					{
						var current = grid.GetZombieCount(cell);
						if (current != 0)
							grid.ChangeZombieCount(cell, -current);
					}
				}
			}
			else if (normalizedAction == "set")
			{
				var timestamp = now - Math.Max(0, ageGameTicks) * 1000L;
				foreach (var cell in cells)
				{
					grid.SetTimestamp(cell, timestamp);
					if (zombieCount >= 0)
					{
						var current = grid.GetZombieCount(cell);
						if (current != 0)
							grid.ChangeZombieCount(cell, -current);
						if (zombieCount != 0)
							grid.ChangeZombieCount(cell, zombieCount);
					}
				}
			}

			var samples = cells
				.Select(cell =>
				{
					var timestamp = grid.GetTimestamp(cell);
					return new
					{
						cell,
						timestamp,
						ageGameTicks = timestamp == 0 ? (long?)null : (now - timestamp) / 1000L,
						fresh = timestamp > threshold,
						zombieCount = grid.GetZombieCount(cell),
						walkable = cell.Walkable(map),
						standable = cell.Standable(map),
						edifice = cell.GetEdifice(map)?.def?.defName
					};
				})
				.ToArray();

			var sampleLimit = Math.Max(1, Math.Min(maxSamples, 100));
			return new
			{
				success = true,
				action = normalizedAction,
				center = ZombieRuntimeActions.DescribeCell(center),
				radius = cappedRadius,
				now,
				fadeoff,
				fadeoffGameTicks = fadeoff / 1000L,
				threshold,
				cellCount = cells.Length,
				nonZeroTimestampCells = samples.Count(sample => sample.timestamp != 0),
				freshCells = samples.Count(sample => sample.fresh),
				nonZeroZombieCountCells = samples.Count(sample => sample.zombieCount != 0),
				samples = samples
					.OrderByDescending(sample => sample.timestamp)
					.ThenBy(sample => sample.cell.x)
					.ThenBy(sample => sample.cell.z)
					.Take(sampleLimit)
					.Select(sample => new
					{
						cell = ZombieRuntimeActions.DescribeCell(sample.cell),
						sample.timestamp,
						sample.ageGameTicks,
						sample.fresh,
						sample.zombieCount,
						sample.walkable,
						sample.standable,
						sample.edifice
					})
					.ToArray()
			};
		}

		[Tool("zombieland/place_wall_line", Description = "Place a reusable straight wall fixture line on the current map for pathing, scent, and room scenarios.")]
		public static object PlaceWallLine(
			[ToolParameter(Description = "Start x coordinate.", Required = false, DefaultValue = -1)] int x = -1,
			[ToolParameter(Description = "Start z coordinate.", Required = false, DefaultValue = -1)] int z = -1,
			[ToolParameter(Description = "Number of wall cells to place. Clamped to 1..80.", Required = false, DefaultValue = 5)] int length = 5,
			[ToolParameter(Description = "Direction of the line: north, south, east, or west.", Required = false, DefaultValue = "north")] string direction = "north",
			[ToolParameter(Description = "Stuff defName for the wall. Defaults to WoodLog.", Required = false, DefaultValue = "WoodLog")] string stuffDefName = "WoodLog",
			[ToolParameter(Description = "When true, assign the walls to the player faction.", Required = false, DefaultValue = true)] bool playerFaction = true)
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

			var start = x < 0 && z < 0 ? map.Center : new IntVec3(x, 0, z);
			if (start.InBounds(map) == false)
			{
				return new
				{
					success = false,
					error = $"Cell ({start.x}, {start.z}) is outside the current map."
				};
			}

			var normalizedDirection = (direction ?? "north").Trim().ToLowerInvariant();
			var step = normalizedDirection switch
			{
				"north" => IntVec3.North,
				"south" => IntVec3.South,
				"east" => IntVec3.East,
				"west" => IntVec3.West,
				_ => IntVec3.Invalid
			};
			if (step.IsValid == false)
			{
				return new
				{
					success = false,
					error = "Unsupported direction. Use north, south, east, or west."
				};
			}

			var wallDef = ThingDefOf.Wall;
			var stuffDef = DefDatabase<ThingDef>.GetNamedSilentFail(stuffDefName) ?? ThingDefOf.WoodLog;
			var cappedLength = Math.Max(1, Math.Min(length, 80));
			var spawned = new List<Building>();
			var skipped = new List<object>();
			for (var i = 0; i < cappedLength; i++)
			{
				var cell = start + step * i;
				if (cell.InBounds(map) == false || cell.Fogged(map) || cell.GetThingList(map).Any(thing => thing is Pawn))
				{
					skipped.Add(new
					{
						cell = ZombieRuntimeActions.DescribeCell(cell),
						reason = "out-of-bounds, fogged, or occupied by pawn"
					});
					continue;
				}

				foreach (var existing in cell.GetThingList(map).Where(thing => thing.def.category == ThingCategory.Building).ToArray())
					existing.Destroy(DestroyMode.Vanish);

				var wall = ThingMaker.MakeThing(wallDef, stuffDef) as Building;
				if (wall == null)
				{
					skipped.Add(new
					{
						cell = ZombieRuntimeActions.DescribeCell(cell),
						reason = "could not create wall"
					});
					continue;
				}

				GenSpawn.Spawn(wall, cell, map, WipeMode.Vanish);
				if (playerFaction)
					wall.SetFaction(Faction.OfPlayer);
				spawned.Add(wall);
			}

			map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();

			return new
			{
				success = spawned.Count == cappedLength,
				start = ZombieRuntimeActions.DescribeCell(start),
				direction = normalizedDirection,
				requestedLength = length,
				length = cappedLength,
				spawnedCount = spawned.Count,
				stuffDef = stuffDef.defName,
				playerFaction,
				walls = spawned.Select(wall => new
				{
					thingId = wall.ThingID,
					cell = ZombieRuntimeActions.DescribeCell(wall.Position),
					walkable = wall.Position.Walkable(map),
					standable = wall.Position.Standable(map)
				}).ToArray(),
				skipped
			};
		}

		[Tool("zombieland/place_thing", Description = "Place one generic thing or building on the current map for reusable scenario fixtures.")]
		public static object PlaceThing(
			[ToolParameter(Description = "ThingDef defName to place, for example Door, Wall, Steel, or FirefoamPopper.", Required = true)] string defName,
			[ToolParameter(Description = "Target x coordinate.", Required = false, DefaultValue = -1)] int x = -1,
			[ToolParameter(Description = "Target z coordinate.", Required = false, DefaultValue = -1)] int z = -1,
			[ToolParameter(Description = "Stuff defName for made-from-stuff things. Empty uses the default stuff.", Required = false, DefaultValue = "")] string stuffDefName = "",
			[ToolParameter(Description = "Stack count for stackable things. Clamped to 1..500.", Required = false, DefaultValue = 1)] int stackCount = 1,
			[ToolParameter(Description = "Faction owner: none, player, zombies, or hostile. Buildings default well with player.", Required = false, DefaultValue = "player")] string faction = "player",
			[ToolParameter(Description = "Destroy existing buildings at the target cell before placing.", Required = false, DefaultValue = true)] bool wipeBuildings = true,
			[ToolParameter(Description = "Optional hit points to assign after spawning. Zero keeps default.", Required = false, DefaultValue = 0)] int hitPoints = 0)
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

			var cell = x < 0 && z < 0 ? map.Center : new IntVec3(x, 0, z);
			if (cell.InBounds(map) == false)
			{
				return new
				{
					success = false,
					error = $"Cell ({cell.x}, {cell.z}) is outside the current map."
				};
			}
			if (cell.Fogged(map))
			{
				return new
				{
					success = false,
					error = $"Cell ({cell.x}, {cell.z}) is fogged."
				};
			}

			var thingDef = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
			if (thingDef == null)
			{
				return new
				{
					success = false,
					error = $"ThingDef '{defName}' was not found."
				};
			}

			ThingDef stuffDef = null;
			if (thingDef.MadeFromStuff)
			{
				stuffDef = string.IsNullOrWhiteSpace(stuffDefName)
					? GenStuff.DefaultStuffFor(thingDef)
					: DefDatabase<ThingDef>.GetNamedSilentFail(stuffDefName);
				if (stuffDef == null)
				{
					return new
					{
						success = false,
						error = $"Stuff def '{stuffDefName}' was not found or no default stuff is available for {thingDef.defName}."
					};
				}
			}

			if (wipeBuildings)
				foreach (var existing in cell.GetThingList(map).Where(thing => thing.def.category == ThingCategory.Building).ToArray())
					existing.Destroy(DestroyMode.Vanish);

			var thing = ThingMaker.MakeThing(thingDef, stuffDef);
			if (thing == null)
			{
				return new
				{
					success = false,
					error = $"ThingMaker.MakeThing returned null for {thingDef.defName}."
				};
			}

			if (thing.def.stackLimit > 1)
				thing.stackCount = Math.Max(1, Math.Min(stackCount, Math.Min(500, thing.def.stackLimit)));

			GenSpawn.Spawn(thing, cell, map, WipeMode.Vanish);
			var owner = ResolveFixtureFaction(faction);
			if (thing is ThingWithComps || thing is Building)
				thing.SetFactionDirect(owner);
			if (hitPoints > 0)
				thing.HitPoints = Math.Min(hitPoints, thing.MaxHitPoints);
			map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();

			return new
			{
				success = thing.Spawned,
				thing = DescribeFixtureThing(thing),
				requestedCell = ZombieRuntimeActions.DescribeCell(cell),
				stuffDef = stuffDef?.defName,
				faction = owner?.def?.defName
			};
		}

		[Tool("zombieland/start_map_fire", Description = "Start a normal RimWorld map fire at a current-map cell for reusable fire/corridor fixtures.")]
		public static object StartMapFire(
			[ToolParameter(Description = "Target x coordinate.", Required = false, DefaultValue = -1)] int x = -1,
			[ToolParameter(Description = "Target z coordinate.", Required = false, DefaultValue = -1)] int z = -1,
			[ToolParameter(Description = "Initial fire size. Clamped to 0.1..1.75.", Required = false, DefaultValue = 0.5f)] float size = 0.5f)
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

			var cell = x < 0 && z < 0 ? map.Center : new IntVec3(x, 0, z);
			if (cell.InBounds(map) == false || cell.Fogged(map))
			{
				return new
				{
					success = false,
					error = $"Cell ({cell.x}, {cell.z}) is outside the current map or fogged."
				};
			}

			var started = FireUtility.TryStartFireIn(cell, map, Math.Max(0.1f, Math.Min(size, 1.75f)), null);
			var fire = cell.GetThingList(map).OfType<Fire>().FirstOrDefault();
			return new
			{
				success = started && fire != null,
				started,
				cell = ZombieRuntimeActions.DescribeCell(cell),
				fire = DescribeFixtureThing(fire)
			};
		}

		[Tool("zombieland/spawn_pawn_fixture", Description = "Spawn an ordinary pawn fixture and optionally make it health-downed, killed into a corpse, or burning.")]
		public static object SpawnPawnFixture(
			[ToolParameter(Description = "PawnKindDef defName. Defaults to Colonist.", Required = false, DefaultValue = "Colonist")] string pawnKindDefName = "Colonist",
			[ToolParameter(Description = "Target x coordinate.", Required = false, DefaultValue = -1)] int x = -1,
			[ToolParameter(Description = "Target z coordinate.", Required = false, DefaultValue = -1)] int z = -1,
			[ToolParameter(Description = "Faction owner: player, hostile, none, or zombies.", Required = false, DefaultValue = "player")] string faction = "player",
			[ToolParameter(Description = "Optional name prefix to make the fixture easy to identify.", Required = false, DefaultValue = "ZL_Fixture")] string namePrefix = "ZL_Fixture",
			[ToolParameter(Description = "Disable work after spawning if the pawn has work settings.", Required = false, DefaultValue = true)] bool disableWork = true,
			[ToolParameter(Description = "Make the pawn health-downed through RimWorld's MakeDowned path.", Required = false, DefaultValue = false)] bool downed = false,
			[ToolParameter(Description = "Kill the pawn after spawning and return the resulting corpse.", Required = false, DefaultValue = false)] bool dead = false,
			[ToolParameter(Description = "Attach fire to the pawn after spawning. Ignored for dead fixtures.", Required = false, DefaultValue = false)] bool attachFire = false)
		{
			if (TryFindSpawnCell(x, z, out var map, out var cell, out var error) == false)
				return error;

			var kindDef = DefDatabase<PawnKindDef>.GetNamedSilentFail(pawnKindDefName) ?? PawnKindDefOf.Colonist;
			var owner = ResolveFixtureFaction(faction);
			var pawn = PawnGenerator.GeneratePawn(kindDef, owner);
			if (string.IsNullOrWhiteSpace(namePrefix) == false && pawn.Name is NameTriple)
			{
				var index = map.mapPawns.AllPawnsSpawned.Count(p => p.Name?.ToStringShort?.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase) == true) + 1;
				pawn.Name = new NameTriple(namePrefix, index.ToString(), "Fixture");
			}
			GenSpawn.Spawn(pawn, cell, map, Rot4.South);
			if (disableWork)
				DisablePawnWork(pawn);

			string downedError = null;
			if (downed && dead == false)
				_ = TryMakeDownedForCombat(pawn, out downedError);

			var fireAttached = false;
			if (attachFire && dead == false)
			{
				FireUtility.TryAttachFire(pawn, 1f, null);
				fireAttached = pawn.GetAttachment(ThingDefOf.Fire) is Fire;
			}

			Corpse corpse = null;
			if (dead)
			{
				pawn.Kill(null);
				corpse = cell.GetThingList(map).OfType<Corpse>().FirstOrDefault(c => c.InnerPawn == pawn)
					?? map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse).OfType<Corpse>().FirstOrDefault(c => c.InnerPawn == pawn);
			}

			return new
			{
				success = dead ? corpse != null : pawn.Spawned && (downed == false || pawn.health.Downed),
				requestedCell = ZombieRuntimeActions.DescribeCell(new IntVec3(x, 0, z)),
				spawnCell = ZombieRuntimeActions.DescribeCell(cell),
				pawn = DescribePawn(pawn),
				healthDowned = pawn.health?.Downed ?? false,
				publicDowned = pawn.Downed,
				downedError,
				fireAttached,
				corpse = DescribeFixtureThing(corpse)
			};
		}

		[Tool("zombieland/read_cell_things", Description = "Read spawned things, pawns, corpses, buildings, and fire in a bounded current-map area.")]
		public static object ReadCellThings(
			[ToolParameter(Description = "Center x coordinate.", Required = false, DefaultValue = -1)] int x = -1,
			[ToolParameter(Description = "Center z coordinate.", Required = false, DefaultValue = -1)] int z = -1,
			[ToolParameter(Description = "Radius around the center. Use 0 for a single cell. Clamped to 0..60.", Required = false, DefaultValue = 0f)] float radius = 0f,
			[ToolParameter(Description = "Maximum thing samples to return.", Required = false, DefaultValue = 100)] int maxThings = 100)
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

			var center = x < 0 && z < 0 ? map.Center : new IntVec3(x, 0, z);
			if (center.InBounds(map) == false)
			{
				return new
				{
					success = false,
					error = $"Cell ({center.x}, {center.z}) is outside the current map."
				};
			}

			var cappedRadius = Math.Max(0f, Math.Min(radius, 60f));
			var cells = (cappedRadius <= 0f ? new[] { center } : GenRadial.RadialCellsAround(center, cappedRadius, true))
				.Where(cell => cell.InBounds(map))
				.Distinct()
				.ToArray();
			var things = cells
				.SelectMany(cell => cell.GetThingList(map).Select(thing => new { cell, thing }))
				.Take(Math.Max(1, Math.Min(maxThings, 300)))
				.ToArray();

			return new
			{
				success = true,
				center = ZombieRuntimeActions.DescribeCell(center),
				radius = cappedRadius,
				cellCount = cells.Length,
				thingCount = things.Length,
				things = things.Select(entry => DescribeFixtureThing(entry.thing)).ToArray()
			};
		}

		static Faction ResolveFixtureFaction(string faction)
		{
			var key = (faction ?? "player").Trim().ToLowerInvariant();
			if (key == "none" || key == "null")
				return null;
			if (key == "zombies")
				return Find.FactionManager.FirstFactionOfDef(ZombieDefOf.Zombies);
			if (key == "hostile" || key == "enemy")
				return Find.FactionManager.AllFactionsVisible.FirstOrDefault(candidate => candidate != null && candidate.HostileTo(Faction.OfPlayer));
			return Faction.OfPlayer;
		}

		static object DescribeFixtureThing(Thing thing)
		{
			if (thing == null)
				return null;

			var pawn = thing as Pawn;
			var corpse = thing as Corpse;
			var fire = thing as Fire;
			return new
			{
				id = ZombieRuntimeActions.StableThingId(thing),
				thingId = thing.ThingID,
				defName = thing.def?.defName,
				label = thing.LabelShort,
				position = thing.Spawned ? ZombieRuntimeActions.DescribeCell(thing.Position) : null,
				spawned = thing.Spawned,
				destroyed = thing.Destroyed,
				stackCount = thing.stackCount,
				hitPoints = thing.HitPoints,
				maxHitPoints = thing.MaxHitPoints,
				faction = thing.Faction?.def?.defName,
				forbidden = thing.Spawned && thing.IsForbidden(Faction.OfPlayer),
				category = thing.def?.category.ToString(),
				isPawn = pawn != null,
				isCorpse = corpse != null,
				isFire = fire != null,
				pawn = DescribeFixturePawn(pawn),
				corpseInnerPawn = corpse?.InnerPawn == null ? null : DescribePawn(corpse.InnerPawn),
				fireSize = fire?.fireSize,
				edifice = thing.Position.IsValid && thing.Spawned ? thing.Position.GetEdifice(thing.Map)?.def?.defName : null
			};
		}

		static object DescribeFixturePawn(Pawn pawn)
		{
			if (pawn == null)
				return null;
			if (pawn is Zombie || pawn is ZombieSpitter || pawn is ZombieSymbiant)
				return DescribeZombie(pawn);
			return DescribePawn(pawn);
		}

		[Tool("zombieland/spawn_colonist", Description = "Spawn one player colonist near a map cell for generic scenario fixtures.")]
		public static object SpawnColonist(
			[ToolParameter(Description = "Target x coordinate. Use -1 with z -1 to spawn near map center.", Required = false, DefaultValue = -1)] int x = -1,
			[ToolParameter(Description = "Target z coordinate. Use -1 with x -1 to spawn near map center.", Required = false, DefaultValue = -1)] int z = -1,
			[ToolParameter(Description = "Optional colonist name prefix to make the fixture easy to identify.", Required = false, DefaultValue = "ZL_Horde_Bait")] string namePrefix = "ZL_Horde_Bait",
			[ToolParameter(Description = "Disable all work after spawning so the pawn stays available as a scenario fixture.", Required = false, DefaultValue = true)] bool disableWork = true)
		{
			if (TryFindSpawnCell(x, z, out var map, out var cell, out var error) == false)
				return error;

			var pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			if (string.IsNullOrWhiteSpace(namePrefix) == false)
			{
				var index = map.mapPawns.FreeColonistsSpawned.Count(p => p.Name?.ToStringShort?.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase) == true) + 1;
				pawn.Name = new NameTriple(namePrefix, index.ToString(), "Fixture");
			}
			GenSpawn.Spawn(pawn, cell, map, Rot4.South);
			if (disableWork)
				DisablePawnWork(pawn);

			return new
			{
				success = pawn.Spawned && pawn.Faction == Faction.OfPlayer,
				requestedCell = ZombieRuntimeActions.DescribeCell(new IntVec3(x, 0, z)),
				spawnCell = ZombieRuntimeActions.DescribeCell(cell),
				disableWork,
				pawn = DescribePawn(pawn)
			};
		}

		[Tool("zombieland/spawn_spitter_visual_fixture", Description = "Spawn one idle zombie spitter without launching a ZombieBall so its custom three-layer rendering can be inspected.")]
		public static object SpawnSpitterVisualFixture(
			[ToolParameter(Description = "Target x coordinate. Use -1 with z -1 to spawn near map center.", Required = false, DefaultValue = -1)] int x = -1,
			[ToolParameter(Description = "Target z coordinate. Use -1 with x -1 to spawn near map center.", Required = false, DefaultValue = -1)] int z = -1,
			[ToolParameter(Description = "Whether to use the aggressive tinted spitter materials.", Required = false, DefaultValue = false)] bool aggressive = false)
		{
			if (TryFindSpawnCell(x, z, out var map, out var cell, out var error) == false)
				return error;

			var existing = CurrentZombies(map).OfType<ZombieSpitter>()
				.Select(ZombieRuntimeActions.StableThingId)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
			ZombieSpitter.Spawn(map, cell);
			var spitter = CurrentZombies(map).OfType<ZombieSpitter>()
				.FirstOrDefault(candidate => existing.Contains(ZombieRuntimeActions.StableThingId(candidate)) == false)
				?? CurrentZombies(map).OfType<ZombieSpitter>().OrderBy(candidate => candidate.Position.DistanceToSquared(cell)).FirstOrDefault();
			if (spitter == null)
			{
				return new
				{
					success = false,
					requestedCell = ZombieRuntimeActions.DescribeCell(new IntVec3(x, 0, z)),
					spawnCell = ZombieRuntimeActions.DescribeCell(cell),
					error = "ZombieSpitter.Spawn did not produce a spawned spitter."
				};
			}

			spitter.aggressive = aggressive;
			spitter.state = SpitterState.Idle;
			spitter.tickCounter = 0;
			spitter.remainingZombies = 0;
			spitter.spitInterval = 0;
			spitter.Rotation = Rot4.South;

			return new
			{
				success = spitter.Spawned,
				requestedCell = ZombieRuntimeActions.DescribeCell(new IntVec3(x, 0, z)),
				spawnCell = ZombieRuntimeActions.DescribeCell(cell),
				aggressive,
				spitter = DescribeZombie(spitter)
			};
		}

		[Tool("zombieland/spawn_reference_lineup", Description = "Clear and spawn the eight common special zombie types in a compact visual comparison pattern.")]
		public static object SpawnReferenceLineup(
			[ToolParameter(Description = "Origin x coordinate for the top-left zombie. Use -1 with z -1 to start near map center.", Required = false, DefaultValue = -1)] int x = -1,
			[ToolParameter(Description = "Origin z coordinate for the top-left zombie. Use -1 with x -1 to start near map center.", Required = false, DefaultValue = -1)] int z = -1,
			[ToolParameter(Description = "Destroy existing Zombieland pawns on the current map before spawning the lineup.", Required = false, DefaultValue = true)] bool clearExisting = true,
			[ToolParameter(Description = "When true, skip the underground dig-out state and spawn each zombie standing.", Required = false, DefaultValue = true)] bool appearDirectly = true,
			[ToolParameter(Description = "Add spitter/symbiant plus staged render states for a reusable S-Special-Gauntlet visual screenshot.", Required = false, DefaultValue = false)] bool stageRenderStates = false)
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

			var origin = x >= 0 && z >= 0 ? new IntVec3(x, 0, z) : new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (origin.InBounds(map) == false)
			{
				return new
				{
					success = false,
					error = $"Cell ({origin.x}, {origin.z}) is outside the current map."
				};
			}

			var destroyed = clearExisting ? ZombieRuntimeActions.DestroyZombies(map) : 0;
			var success = true;
			var spawned = new List<Pawn>();
			var results = referenceLineup.Select<LineupEntry, object>(entry =>
			{
				var requestedCell = new IntVec3(origin.x + entry.dx, 0, origin.z + entry.dz);
				if (TryFindSpawnCell(requestedCell.x, requestedCell.z, out var spawnMap, out var cell, out var error) == false)
				{
					success = false;
					return new
					{
						success = false,
						type = entry.type.ToString(),
						requestedCell = ZombieRuntimeActions.DescribeCell(requestedCell),
						spawnCell = (object)null,
						error,
						zombie = (object)null
					};
				}

				var zombie = ZombieRuntimeActions.SpawnZombie(cell, spawnMap, entry.type, appearDirectly);
				if (zombie != null)
				{
					zombie.Name = new NameSingle($"ZL Visual {entry.type}");
					zombie.Rotation = Rot4.South;
					spawned.Add(zombie);
				}
				success &= zombie?.Spawned ?? false;
				return new
				{
					success = zombie?.Spawned ?? false,
					type = entry.type.ToString(),
					requestedCell = ZombieRuntimeActions.DescribeCell(requestedCell),
					spawnCell = ZombieRuntimeActions.DescribeCell(cell),
					error = zombie == null ? "ZombieGenerator.SpawnZombie returned no zombie." : null,
					zombie = DescribeZombie(zombie)
				};
			}).ToArray();
			var stage = stageRenderStates ? StageReferenceLineupRenderStates(map, origin, spawned) : null;

			return new
			{
				success = success && (stageRenderStates == false || ObjectSuccess(stage)),
				origin = ZombieRuntimeActions.DescribeCell(origin),
				destroyed,
				appearDirectly,
				stageRenderStates,
				count = results.Length,
				results,
				stage
			};
		}

		static object StageReferenceLineupRenderStates(Map map, IntVec3 origin, List<Pawn> spawned)
		{
			var staged = new List<object>();
			var errors = new List<string>();
			var tickAbs = GenTicks.TicksAbs;

			var anchor = SpawnLineupColonist(map, origin + new IntVec3(8, 0, 5), "ZL Visual Rope Anchor", spawned, errors);
			var wounded = ZombieRuntimeActions.SpawnZombie(ResolveLineupCell(map, origin + new IntVec3(4, 0, 6)), map, ZombieType.Normal, true);
			if (wounded != null)
			{
				wounded.Name = new NameSingle("ZL Visual Wounded Target");
				wounded.TakeDamage(new DamageInfo(DamageDefOf.Cut, 6f));
				spawned.Add(wounded);
				staged.Add(new { role = "healer-target", zombie = DescribeZombie(wounded) });
			}
			else
				errors.Add("Could not spawn wounded healer target.");

			var electrifier = spawned.OfType<Zombie>().FirstOrDefault(zombie => zombie.isElectrifier);
			if (electrifier != null)
			{
				electrifier.electricDisabledUntil = 0;
				electrifier.electricCounter = 0;
				electrifier.absorbAttack.Clear();
				electrifier.absorbAttack.Add(new KeyValuePair<float, int>(0f, -2));
				staged.Add(new { role = "electrifier-arc-absorb", zombie = DescribeZombie(electrifier), absorbCount = electrifier.absorbAttack.Count });
			}
			else
				errors.Add("No electrifier was available to stage electric render effects.");

			var bomber = spawned.OfType<Zombie>().FirstOrDefault(zombie => zombie.IsSuicideBomber);
			if (bomber != null)
			{
				bomber.lastBombTick = tickAbs;
				staged.Add(new { role = "bomber-light", zombie = DescribeZombie(bomber) });
			}

			var albino = spawned.OfType<Zombie>().FirstOrDefault(zombie => zombie.isAlbino);
			if (albino != null)
			{
				albino.scream = 180;
				staged.Add(new { role = "albino-scream", zombie = DescribeZombie(albino), scream = albino.scream });
			}
			else
				errors.Add("No albino was available to stage scream render effects.");

			var healer = spawned.OfType<Zombie>().FirstOrDefault(zombie => zombie.isHealer);
			if (healer != null && wounded != null)
			{
				healer.healInfo.Clear();
				healer.healInfo.Add(new HealerInfo(wounded) { step = 12 });
				staged.Add(new { role = "healer-beam", zombie = DescribeZombie(healer), target = DescribeZombie(wounded), healInfoCount = healer.healInfo.Count });
			}
			else
				errors.Add("No healer/wounded pair was available to stage healer render effects.");

			var raging = SpawnLineupZombie(map, origin + new IntVec3(8, 0, 0), ZombieType.Normal, "ZL Visual Raging", true, spawned, errors);
			if (raging != null)
			{
				raging.raging = tickAbs + 60000;
				raging.Rotation = Rot4.South;
				staged.Add(new { role = "raging-aura-eyes", zombie = DescribeZombie(raging), ragingUntil = raging.raging });
			}

			var roped = SpawnLineupZombie(map, origin + new IntVec3(8, 0, 2), ZombieType.Normal, "ZL Visual Roped", true, spawned, errors);
			if (roped != null && anchor != null)
			{
				roped.ropedBy = anchor;
				roped.Rotation = Rot4.South;
				staged.Add(new { role = "roped-icon-line", zombie = DescribeZombie(roped), anchor = DescribePawn(anchor), ropingFactor = roped.RopingFactorTo(anchor) });
			}

			var confused = SpawnLineupZombie(map, origin + new IntVec3(10, 0, 2), ZombieType.Normal, "ZL Visual Confused", true, spawned, errors);
			if (confused != null)
			{
				confused.paralyzedUntil = tickAbs + 2500;
				confused.Rotation = Rot4.South;
				staged.Add(new { role = "confused-icon", zombie = DescribeZombie(confused), paralyzedUntil = confused.paralyzedUntil });
			}

			var emerging = SpawnLineupZombie(map, origin + new IntVec3(10, 0, 0), ZombieType.Normal, "ZL Visual Emerging", false, spawned, errors);
			if (emerging != null)
			{
				emerging.state = ZombieState.Emerging;
				emerging.Rotation = Rot4.South;
				staged.Add(new { role = "emerging-render", zombie = DescribeZombie(emerging) });
			}

			var spitter = SpawnLineupSpitter(map, origin + new IntVec3(10, 0, 4), "ZL Visual Spitter", spawned, errors);
			if (spitter != null)
				staged.Add(new { role = "spitter-custom-render", zombie = DescribeZombie(spitter) });

			var symbiant = SpawnLineupSymbiant(map, origin + new IntVec3(12, 0, 4), "ZL Visual Symbiant", spawned, errors);
			if (symbiant != null)
				staged.Add(new { role = "symbiant-render", zombie = DescribeZombie(symbiant) });

			return new
			{
				success = errors.Count == 0,
				errors = errors.ToArray(),
				anchor = DescribePawn(anchor),
				spawnedCount = spawned.Count,
				staged = staged.ToArray()
			};
		}

		static IntVec3 ResolveLineupCell(Map map, IntVec3 requestedCell)
		{
			if (requestedCell.InBounds(map) && requestedCell.Standable(map) && requestedCell.Fogged(map) == false && requestedCell.GetFirstPawn(map) == null)
				return requestedCell;

			return GenRadial.RadialCellsAround(requestedCell, 8f, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.OrderBy(cell => cell.DistanceToSquared(requestedCell))
				.FirstOrDefault();
		}

		static Pawn SpawnLineupColonist(Map map, IntVec3 requestedCell, string name, List<Pawn> spawned, List<string> errors)
		{
			var cell = ResolveLineupCell(map, requestedCell);
			if (cell.IsValid == false)
			{
				errors.Add($"Could not find a spawn cell for {name}.");
				return null;
			}

			var pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			pawn.Name = new NameSingle(name);
			GenSpawn.Spawn(pawn, cell, map, Rot4.South);
			DisablePawnWork(pawn);
			spawned.Add(pawn);
			return pawn;
		}

		static Zombie SpawnLineupZombie(Map map, IntVec3 requestedCell, ZombieType type, string name, bool appearDirectly, List<Pawn> spawned, List<string> errors)
		{
			var cell = ResolveLineupCell(map, requestedCell);
			if (cell.IsValid == false)
			{
				errors.Add($"Could not find a spawn cell for {name}.");
				return null;
			}

			var zombie = ZombieRuntimeActions.SpawnZombie(cell, map, type, appearDirectly);
			if (zombie == null)
			{
				errors.Add($"Could not spawn {name}.");
				return null;
			}
			zombie.Name = new NameSingle(name);
			spawned.Add(zombie);
			return zombie;
		}

		static ZombieSpitter SpawnLineupSpitter(Map map, IntVec3 requestedCell, string name, List<Pawn> spawned, List<string> errors)
		{
			var cell = ResolveLineupCell(map, requestedCell);
			if (cell.IsValid == false)
			{
				errors.Add($"Could not find a spawn cell for {name}.");
				return null;
			}

			var existing = CurrentZombies(map).OfType<ZombieSpitter>().Select(ZombieRuntimeActions.StableThingId).ToHashSet(StringComparer.OrdinalIgnoreCase);
			ZombieSpitter.Spawn(map, cell);
			var spitter = CurrentZombies(map).OfType<ZombieSpitter>()
				.FirstOrDefault(candidate => existing.Contains(ZombieRuntimeActions.StableThingId(candidate)) == false)
				?? CurrentZombies(map).OfType<ZombieSpitter>().OrderBy(candidate => candidate.Position.DistanceToSquared(cell)).FirstOrDefault();
			if (spitter == null)
			{
				errors.Add($"Could not spawn {name}.");
				return null;
			}
			spitter.Name = new NameSingle(name);
			spitter.state = SpitterState.Idle;
			spitter.Rotation = Rot4.South;
			spawned.Add(spitter);
			return spitter;
		}

		static ZombieSymbiant SpawnLineupSymbiant(Map map, IntVec3 requestedCell, string name, List<Pawn> spawned, List<string> errors)
		{
			var cell = ResolveLineupCell(map, requestedCell);
			if (cell.IsValid == false)
			{
				errors.Add($"Could not find a spawn cell for {name}.");
				return null;
			}

			var existing = CurrentZombies(map).OfType<ZombieSymbiant>().Select(ZombieRuntimeActions.StableThingId).ToHashSet(StringComparer.OrdinalIgnoreCase);
			ZombieSymbiant.Spawn(map, cell);
			var symbiant = CurrentZombies(map).OfType<ZombieSymbiant>()
				.FirstOrDefault(candidate => existing.Contains(ZombieRuntimeActions.StableThingId(candidate)) == false)
				?? CurrentZombies(map).OfType<ZombieSymbiant>().OrderBy(candidate => candidate.Position.DistanceToSquared(cell)).FirstOrDefault();
			if (symbiant == null)
			{
				errors.Add($"Could not spawn {name}.");
				return null;
			}
			symbiant.Name = new NameSingle(name);
			spawned.Add(symbiant);
			return symbiant;
		}

	}
}
