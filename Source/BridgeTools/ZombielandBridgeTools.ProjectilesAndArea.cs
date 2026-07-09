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
		const string AreaWorkflowPrefix = "ZL_Area_";

		[Tool("zombieland/area_workflow_state", Description = "Set up and inspect a reusable dangerous-area workflow fixture, including the real manage-areas dialog.")]
		public static object AreaWorkflowState(
			[ToolParameter(Description = "Create or refresh reusable allowed areas for every Zombieland risk mode.", Required = false, DefaultValue = false)] bool setupFixture = false,
			[ToolParameter(Description = "Open RimWorld's real Manage Areas dialog after preparing the selected Zombieland area.", Required = false, DefaultValue = false)] bool openManageDialog = false,
			[ToolParameter(Description = "Optional scenario action, such as read, behavior, ranged-projectiles, ce-ranged-projectiles, ce-compat, spitter-readiness, or warning-ui.", Required = false, DefaultValue = "read")] string actionMode = "read")
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
				setup = EnsureAreaWorkflowFixture(map);

			var fixtureAreas = FindAreaWorkflowAreas(map);
			var selectedArea = fixtureAreas.FirstOrDefault(area => area.mode == AreaRiskMode.ZombieInside).area
				?? fixtureAreas.FirstOrDefault().area;
			if (selectedArea == null)
			{
				return new
				{
					success = false,
					setupFixture,
					setup,
					error = "No Zombieland area workflow fixture exists; rerun with setupFixture=true."
				};
			}

			var normalizedActionMode = (actionMode ?? "read").Trim().ToLowerInvariant();
			object action = null;
			if (normalizedActionMode == "behavior")
				action = RunAreaWorkflowBehavior(map, fixtureAreas);
			else if (normalizedActionMode == "targeting")
				action = RunAreaWorkflowTargeting(map);
			else if (normalizedActionMode == "bug-reports")
				action = RunAreaWorkflowBugReports(map);
			else if (normalizedActionMode == "spitter-readiness")
				action = RunAreaWorkflowSpitterReadiness(map);
			else if (normalizedActionMode == "ranged-projectiles")
				action = RunAreaWorkflowRangedProjectiles(map);
			else if (normalizedActionMode == "ce-ranged-projectiles")
				action = RunAreaWorkflowCeRangedProjectiles(map);
			else if (normalizedActionMode == "danger-flee")
				action = RunAreaWorkflowDangerFlee(map);
			else if (normalizedActionMode == "smart-melee")
				action = RunAreaWorkflowSmartMelee(map);
			else if (normalizedActionMode == "job-gate")
				action = RunAreaWorkflowJobGate(map);
			else if (normalizedActionMode == "special-melee-verbs")
				action = RunAreaWorkflowSpecialMeleeVerbs(map);
			else if (normalizedActionMode == "special-damage")
				action = RunAreaWorkflowSpecialDamage(map);
			else if (normalizedActionMode == "ce-compat")
				action = RunAreaWorkflowCeCompat(map);
			else if (normalizedActionMode == "electrifier-melee-damage")
				action = RunAreaWorkflowElectrifierMeleeDamage(map);
			else if (normalizedActionMode == "animal-response")
				action = RunAreaWorkflowAnimalResponse(map);
			else if (normalizedActionMode == "ui-state")
				action = RunAreaWorkflowUiState(map);
			else if (normalizedActionMode == "downed-crawler-visuals")
				action = RunAreaWorkflowDownedCrawlerVisuals(map);
			else if (normalizedActionMode == "root-play-hooks")
				action = RunAreaWorkflowRootPlayHooks(map);
			else if (normalizedActionMode == "render-node-graphics")
				action = RunAreaWorkflowRenderNodeGraphics(map);
			else if (normalizedActionMode == "effecter-suppression")
				action = RunAreaWorkflowEffecterSuppression(map);
			else if (normalizedActionMode == "graphic-multi-texture")
				action = RunAreaWorkflowGraphicMultiTexture(map);
			else if (normalizedActionMode == "warmup-scaling")
				action = RunAreaWorkflowWarmupScaling(map);
			else if (normalizedActionMode == "zombie-stats")
				action = RunAreaWorkflowZombieStats(map);
			else if (normalizedActionMode == "visual-support")
				action = RunAreaWorkflowVisualSupport(map);
			else if (normalizedActionMode == "warning-ui")
				action = RunAreaWorkflowWarningUi(map, fixtureAreas);
			else if (normalizedActionMode != "read")
			{
				return new
				{
					success = false,
					actionMode,
					error = "Unsupported area workflow actionMode. Use read, behavior, targeting, bug-reports, spitter-readiness, ranged-projectiles, ce-ranged-projectiles, danger-flee, smart-melee, job-gate, special-melee-verbs, special-damage, ce-compat, electrifier-melee-damage, animal-response, ui-state, downed-crawler-visuals, root-play-hooks, render-node-graphics, effecter-suppression, graphic-multi-texture, warmup-scaling, zombie-stats, visual-support, or warning-ui."
				};
			}
			var actionSucceeded = normalizedActionMode == "read"
				|| (bool)(action?.GetType().GetProperty("success")?.GetValue(action) ?? false);

			var canMakeNewAllowed = map.areaManager.CanMakeNewAllowed();
			var tryMakeNewAllowed = map.areaManager.TryMakeNewAllowed(out Area_Allowed throwawayArea);
			var tryMakeNewAllowedMatchesCanMakeNewAllowed = tryMakeNewAllowed == canMakeNewAllowed;
			var throwawayCreatedWhenAllowed = canMakeNewAllowed == false;
			var throwawayRiskBeforeRemove = canMakeNewAllowed == false;
			var throwawayRiskCleaned = true;
			if (throwawayArea != null && map.areaManager.AllAreas.Contains(throwawayArea))
			{
				throwawayCreatedWhenAllowed = true;
				Dialog_ManageAreas_Patches.SetMode(throwawayArea, AreaRiskMode.ZombieInside);
				throwawayRiskBeforeRemove = Dialog_ManageAreas_Patches.GetMode(throwawayArea) == AreaRiskMode.ZombieInside;
				map.areaManager.Remove(throwawayArea);
				throwawayRiskCleaned = ZombieSettings.Values.dangerousAreas.ContainsKey(throwawayArea) == false
					&& ZombieAreaManager.pawnsInDanger.Values.Contains(throwawayArea) == false;
			}

			var sortBefore = map.areaManager.AllAreas.Select(area => area.Label).ToArray();
			map.areaManager.SortAreas();
			var sortAfter = map.areaManager.AllAreas.Select(area => area.Label).ToArray();
			var sortPreserved = sortBefore.SequenceEqual(sortAfter);

			var probeDialog = new Dialog_ManageAreas(map);
			var initialSize = probeDialog.InitialSize;
			var expectedMinWidth = 550f + Dialog_ManageAreas_Patches.ZombieRiskButtonWidth + Dialog_ManageAreas_Patches.ZombieRiskButtonGap;
			var expectedMinHeight = 400f + Dialog_ManageAreas_Patches.ZombieRiskDialogExtraHeight;
			var initialSizeExpanded = initialSize.x >= expectedMinWidth;
			var initialHeightExpanded = initialSize.y >= expectedMinHeight;
			var doAreaRowTargetExists = AccessTools.DeclaredMethod(typeof(Dialog_ManageAreas), "DoAreaRow", new[] { typeof(Rect), typeof(Area), typeof(int) }) != null;

			var selectedMode = Dialog_ManageAreas_Patches.GetMode(selectedArea);
			var selectedModeText = selectedMode.ToStringHuman();
			var allAreaCount = map.areaManager.AllAreas.Count;
			var mutableAreaCount = map.areaManager.AllAreas.Count(area => area.Mutable);
			var allowedAreaCount = map.areaManager.AllAreas.Count(area => area is Area_Allowed);
			var canMakeNewAllowedMatchesVanillaLimit = canMakeNewAllowed == (allowedAreaCount < 10);
			var riskButtonCoversVanillaRows = mutableAreaCount > 0
				&& map.areaManager.AllAreas.Where(area => area.Mutable).All(area => Dialog_ManageAreas_Patches.GetMode(area).ToStringHuman() != null);
			var footerFitsCurrentRows = Dialog_ManageAreas_Patches.CanRenderZombieRiskFooter(map);
			var oldFont = Text.Font;
			string footerTextForCurrentWidth;
			try
			{
				Text.Font = GameFont.Tiny;
				footerTextForCurrentWidth = Dialog_ManageAreas_Patches.ZombieRiskFooterText(Dialog_ManageAreas_Patches.ZombieRiskFooterTextMaxWidth);
			}
			finally
			{
				Text.Font = oldFont;
			}
			var footerTextUsesInformativeHint = footerTextForCurrentWidth != "ShowZombieRisk".Translate().ToString();

			if (openManageDialog && Find.WindowStack != null)
			{
				_ = Find.WindowStack.TryRemove(typeof(Dialog_ManageAreas), false);
				var dialog = new Dialog_ManageAreas(map);
				Find.WindowStack.Add(dialog);
			}
			var manageDialogOpened = Find.WindowStack?.IsOpen(typeof(Dialog_ManageAreas)) == true;

			var areas = fixtureAreas
				.Select(pair => new
				{
					label = pair.area.Label,
					mode = pair.mode.ToString(),
					modeText = pair.mode.ToStringHuman(),
					activeCellCount = pair.area.ActiveCells.Count(),
					index = map.areaManager.AllAreas.IndexOf(pair.area)
				})
				.ToArray();

			return new
			{
				success = tryMakeNewAllowedMatchesCanMakeNewAllowed
					&& throwawayCreatedWhenAllowed
					&& sortPreserved
					&& initialSizeExpanded
					&& initialHeightExpanded
					&& doAreaRowTargetExists
					&& canMakeNewAllowedMatchesVanillaLimit
					&& throwawayRiskBeforeRemove
					&& throwawayRiskCleaned
					&& riskButtonCoversVanillaRows
					&& footerTextUsesInformativeHint
					&& areas.Length >= 5
					&& (openManageDialog == false || manageDialogOpened)
					&& actionSucceeded,
				setupFixture,
				setup,
				openManageDialog,
				manageDialogOpened,
				actionMode = normalizedActionMode,
				actionSucceeded,
				action,
				selected = new
				{
					label = selectedArea.Label,
					mode = selectedMode.ToString(),
					modeText = selectedModeText,
					index = map.areaManager.AllAreas.IndexOf(selectedArea)
				},
				canMakeNewAllowed,
				tryMakeNewAllowed,
				tryMakeNewAllowedMatchesCanMakeNewAllowed,
				throwawayCreatedWhenAllowed,
				throwawayRiskBeforeRemove,
				throwawayRiskCleaned,
				sortPreserved,
				vanillaDialogRowPatch = new
				{
					initialSize = new { x = initialSize.x, y = initialSize.y },
					expectedMinWidth,
					expectedMinHeight,
					initialSizeExpanded,
					initialHeightExpanded,
					doAreaRowTargetExists,
					Dialog_ManageAreas_Patches.VanillaWidgetRowGap,
					Dialog_ManageAreas_Patches.VanillaAreaRowTrashRight,
					Dialog_ManageAreas_Patches.ZombieRiskButtonLeft,
					Dialog_ManageAreas_Patches.ZombieRiskButtonLabelWidth,
					Dialog_ManageAreas_Patches.ZombieRiskButtonHorizontalPadding,
					Dialog_ManageAreas_Patches.ZombieRiskButtonWidth,
					Dialog_ManageAreas_Patches.ZombieRiskButtonGap,
					Dialog_ManageAreas_Patches.ZombieRiskDialogExtraHeight,
					Dialog_ManageAreas_Patches.ZombieRiskFooterTextMaxWidth,
					footerTextForCurrentWidth,
					footerTextUsesInformativeHint,
					allAreaCount,
					mutableAreaCount,
					allowedAreaCount,
					vanillaAllowedAreaLimit = 10,
					canMakeNewAllowedMatchesVanillaLimit,
					riskButtonCoversVanillaRows,
					footerFitsCurrentRows
				},
				areas
			};
		}

		[Tool("zombieland/spit_zombie_ball", Description = "Put a spitter into its firing state and verify the real job-driver shoot path spawns a ZombieBall projectile.")]
		public static object SpitZombieBall(
			[ToolParameter(Description = "Optional spitter id, ThingID, label, or short name. When omitted, a fresh spitter is spawned near map center.", Required = false, DefaultValue = "")] string target = "",
			[ToolParameter(Description = "Deterministic Rand seed for target selection and projectile launch setup.", Required = false, DefaultValue = 515151)] int seed = 515151)
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

			if (TryFindOrSpawnSpitter(map, target, out var spitter, out var spawnedSpitter, out var error) == false)
				return error;

			var before = DescribeZombie(spitter);
			var zombieBallsBefore = map.listerThings.AllThings.Count(thing => thing.def == CustomDefs.ZombieBall);
			var zombieCountBefore = CurrentZombies(map).Length;
			_ = ForceSpitterShot(map, spitter, seed);

			var zombieBallsAfter = map.listerThings.AllThings.Count(thing => thing.def == CustomDefs.ZombieBall);
			var zombieCountAfter = CurrentZombies(map).Length;
			var zombieBalls = map.listerThings.AllThings
				.Where(thing => thing.def == CustomDefs.ZombieBall)
				.Select(thing => new
				{
					thingId = thing.ThingID,
					position = ZombieRuntimeActions.DescribeCell(thing.Position),
					spawned = thing.Spawned
				})
				.ToArray();

			return new
			{
				success = zombieBallsAfter > zombieBallsBefore && spitter.remainingZombies == 0,
				spawnedSpitter,
				seed,
				zombieBallsBefore,
				zombieBallsAfter,
				zombieBallDelta = zombieBallsAfter - zombieBallsBefore,
				zombieCountBefore,
				zombieCountAfter,
				before,
				after = DescribeZombie(spitter),
				zombieBalls
			};
		}

		[Tool("zombieland/impact_zombie_ball", Description = "Launch a real ZombieBall projectile from a spitter and verify its impact path spawns a zombie.")]
		public static object ImpactZombieBall(
			[ToolParameter(Description = "Optional spitter id, ThingID, label, or short name. When omitted, a fresh spitter is spawned near map center.", Required = false, DefaultValue = "")] string target = "",
			[ToolParameter(Description = "Deterministic Rand seed for target selection and projectile launch setup.", Required = false, DefaultValue = 616161)] int seed = 616161)
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

			if (TryFindOrSpawnSpitter(map, target, out var spitter, out var spawnedSpitter, out var error) == false)
				return error;

			var before = DescribeZombie(spitter);
			var zombieCountBefore = CurrentZombies(map).Length;
			var zombieBallsBefore = map.listerThings.AllThings.Count(thing => thing.def == CustomDefs.ZombieBall);
			var projectileTarget = GenRadial.RadialCellsAround(spitter.Position, 8f, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.DistanceTo(spitter.Position) >= 3f)
				.Where(cell => map.roofGrid.RoofAt(cell)?.isThickRoof != true)
				.DefaultIfEmpty(IntVec3.Invalid)
				.First();
			if (projectileTarget.IsValid == false)
			{
				return new
				{
					success = false,
					spawnedSpitter,
					before,
					after = DescribeZombie(spitter),
					error = "No nearby clear projectile target was found."
				};
			}

			ZombieBall projectile;
			Rand.PushState(seed);
			try
			{
				projectile = GenSpawn.Spawn(CustomDefs.ZombieBall, spitter.Position, map, WipeMode.Vanish) as ZombieBall;
				projectile?.Launch(spitter, spitter.DrawPos + new UnityEngine.Vector3(0, 0, 0.5f), projectileTarget, projectileTarget, ProjectileHitFlags.IntendedTarget);
			}
			finally
			{
				Rand.PopState();
			}

			if (projectile == null)
			{
				return new
				{
					success = false,
					spawnedSpitter,
					before,
					after = DescribeZombie(spitter),
					error = "Spawning ZombieBall did not produce a projectile."
				};
			}

			var projectileStart = projectile.Position;
			var speed = Math.Max(0.001f, projectile.def.projectile.SpeedTilesPerTick);
			var projectileUpdateRateTicks = Math.Max(1, projectile.UpdateRateTicks);
			projectile.Impact(null);

			var zombieCountAfter = CurrentZombies(map).Length;
			var zombieBallsAfter = map.listerThings.AllThings.Count(thing => thing.def == CustomDefs.ZombieBall);
			var spawnedZombies = CurrentZombies(map)
				.Where(pawn => pawn is Zombie)
				.OrderBy(pawn => pawn.Position.DistanceToSquared(projectileTarget))
				.Take(5)
				.Select(DescribeZombie)
				.ToArray();

			return new
			{
				success = zombieCountAfter > zombieCountBefore && zombieBallsAfter <= zombieBallsBefore,
				spawnedSpitter,
				seed,
				projectileStart = ZombieRuntimeActions.DescribeCell(projectileStart),
				projectileTarget = ZombieRuntimeActions.DescribeCell(projectileTarget),
				speedTilesPerTick = speed,
				projectileUpdateRateTicks,
				impactCalledDirectly = true,
				zombieBallsBefore,
				zombieBallsAfter,
				zombieCountBefore,
				zombieCountAfter,
				before,
				after = DescribeZombie(spitter),
				nearestSpawnedZombies = spawnedZombies
			};
		}

		[Tool("zombieland/zombie_ball_in_flight", Description = "Launch a real ZombieBall, advance to the source-derived halfway point, verify it is still in flight, then let it impact.")]
		public static object ZombieBallInFlight(
			[ToolParameter(Description = "Deterministic Rand seed for projectile launch setup.", Required = false, DefaultValue = 717171)] int seed = 717171)
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
			if (TryFindClearSpawnCell(map, root, 16f, out var spitterCell, out var spawnError) == false)
				return spawnError;

			var existingSpitters = CurrentZombies(map).OfType<ZombieSpitter>()
				.Select(ZombieRuntimeActions.StableThingId)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
			ZombieSpitter.Spawn(map, spitterCell);
			var spitter = CurrentZombies(map).OfType<ZombieSpitter>()
				.FirstOrDefault(candidate => existingSpitters.Contains(ZombieRuntimeActions.StableThingId(candidate)) == false)
				?? CurrentZombies(map).OfType<ZombieSpitter>().OrderBy(candidate => candidate.Position.DistanceToSquared(spitterCell)).FirstOrDefault();
			if (spitter == null)
			{
				return new
				{
					success = false,
					spitterCell = ZombieRuntimeActions.DescribeCell(spitterCell),
					error = "Could not spawn a spitter for ZombieBall travel."
				};
			}

			var targetCell = GenRadial.RadialCellsAround(spitter.Position, 14f, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.DistanceTo(spitter.Position) >= 8f)
				.Where(cell => map.roofGrid.RoofAt(cell)?.isThickRoof != true)
				.OrderByDescending(cell => cell.DistanceToSquared(spitter.Position))
				.FirstOrDefault();
			if (targetCell.IsValid == false)
			{
				return new
				{
					success = false,
					spitter = DescribeZombie(spitter),
					error = "No clear distant ZombieBall target was found."
				};
			}

			var zombieCountBefore = CurrentZombies(map).Length;
			var ballsBefore = map.listerThings.AllThings.Count(thing => thing.def == CustomDefs.ZombieBall);
			ZombieBall projectile;
			Rand.PushState(seed);
			try
			{
				projectile = GenSpawn.Spawn(CustomDefs.ZombieBall, spitter.Position, map, WipeMode.Vanish) as ZombieBall;
				projectile?.Launch(spitter, spitter.DrawPos + new Vector3(0, 0, 0.5f), targetCell, targetCell, ProjectileHitFlags.IntendedTarget);
			}
			finally
			{
				Rand.PopState();
			}

			if (projectile == null)
			{
				return new
				{
					success = false,
					spitter = DescribeZombie(spitter),
					error = "Spawning ZombieBall did not produce a projectile."
				};
			}

			var startCell = projectile.Position;
			var startExact = projectile.ExactPosition;
			var startRotation = projectile.ExactRotation.eulerAngles.y;
			var origin = spitter.DrawPos + new Vector3(0, 0, 0.5f);
			var destination = targetCell.ToVector3Shifted();
			var startingTicks = Math.Max(1, Mathf.CeilToInt((origin - destination).magnitude / projectile.def.projectile.SpeedTilesPerTick));
			var halfwayTicks = Math.Max(1, startingTicks / 2);
			AdvanceGameTicks(halfwayTicks);

			var inFlightSpawned = projectile.Spawned && projectile.Destroyed == false;
			var halfwayCell = inFlightSpawned ? projectile.Position : IntVec3.Invalid;
			var halfwayExact = inFlightSpawned ? projectile.ExactPosition : Vector3.zero;
			var halfwayRotation = inFlightSpawned ? projectile.ExactRotation.eulerAngles.y : 0f;
			var movedFromStart = inFlightSpawned && (halfwayExact - startExact).MagnitudeHorizontalSquared() > 0.01f;
			var notYetAtTarget = inFlightSpawned && projectile.Position != targetCell;
			var remainingTicks = startingTicks - halfwayTicks + 5;
			AdvanceGameTicks(remainingTicks);

			var ballsAfter = map.listerThings.AllThings.Count(thing => thing.def == CustomDefs.ZombieBall);
			var zombieCountAfter = CurrentZombies(map).Length;
			var nearestSpawnedZombies = CurrentZombies(map)
				.Where(pawn => pawn is Zombie)
				.OrderBy(pawn => pawn.Position.DistanceToSquared(targetCell))
				.Take(5)
				.Select(DescribeZombie)
				.ToArray();

			return new
			{
				success = inFlightSpawned
					&& movedFromStart
					&& notYetAtTarget
					&& Math.Abs(Mathf.DeltaAngle(startRotation, halfwayRotation)) > 0.1f
					&& ballsAfter <= ballsBefore
					&& zombieCountAfter > zombieCountBefore,
				seed,
				spitter = DescribeZombie(spitter),
				startCell = ZombieRuntimeActions.DescribeCell(startCell),
				targetCell = ZombieRuntimeActions.DescribeCell(targetCell),
				halfwayCell = halfwayCell.IsValid ? ZombieRuntimeActions.DescribeCell(halfwayCell) : null,
				startExact = DescribeVector(startExact),
				halfwayExact = DescribeVector(halfwayExact),
				startRotation,
				halfwayRotation,
				startingTicks,
				halfwayTicks,
				remainingTicks,
				speedTilesPerTick = projectile.def.projectile.SpeedTilesPerTick,
				inFlightSpawned,
				movedFromStart,
				notYetAtTarget,
				ballsBefore,
				ballsAfter,
				zombieCountBefore,
				zombieCountAfter,
				nearestSpawnedZombies
			};
		}

		[Tool("zombieland/spawn_symbiant", Description = "Spawn a ZombieSymbiant through its runtime spawn path and verify it enters the map with the symbiant job.")]
		public static object SpawnSymbiant(
			[ToolParameter(Description = "Target x coordinate. Use -1 with z -1 to spawn near map center.", Required = false, DefaultValue = -1)] int x = -1,
			[ToolParameter(Description = "Target z coordinate. Use -1 with x -1 to spawn near map center.", Required = false, DefaultValue = -1)] int z = -1)
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

			var root = x >= 0 && z >= 0 ? new IntVec3(x, 0, z) : new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var cell, out var error) == false)
				return error;

			var existing = CurrentZombies(map).OfType<ZombieSymbiant>()
				.Select(ZombieRuntimeActions.StableThingId)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
			ZombieSymbiant.Spawn(map, cell);
			var symbiant = CurrentZombies(map).OfType<ZombieSymbiant>()
				.FirstOrDefault(candidate => existing.Contains(ZombieRuntimeActions.StableThingId(candidate)) == false)
				?? CurrentZombies(map).OfType<ZombieSymbiant>().OrderBy(candidate => candidate.Position.DistanceToSquared(cell)).FirstOrDefault();

			return new
			{
				success = symbiant?.Spawned == true && symbiant.CurJobDef == CustomDefs.Symbiant,
				requestedCell = ZombieRuntimeActions.DescribeCell(root),
				spawnCell = ZombieRuntimeActions.DescribeCell(cell),
				symbiant = DescribeZombie(symbiant),
				assets = new
				{
					Assets.initialized,
					hasMetaballShader = Assets.MetaballShader != null,
					hasZombieSymbiantShader = Assets.ZombieSymbiantShader != null,
					zombieSymbiantShader = Assets.ZombieSymbiantShader?.name
				}
			};
		}

		[Tool("zombieland/zombie_area_risk_contract", Description = "Verify dangerous-area risk modes classify colonists, normal zombies, spitters, and symbiants consistently.")]
		public static object ZombieAreaRiskContract()
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
			if (TryFindClearSpawnCell(map, root + new IntVec3(-6, 0, 0), 16f, out var colonistCell, out var colonistCellError) == false)
				return colonistCellError;
			if (TryFindClearSpawnCell(map, root + new IntVec3(-2, 0, 0), 16f, out var normalCell, out var normalCellError) == false)
				return normalCellError;
			if (TryFindClearSpawnCell(map, root + new IntVec3(2, 0, 0), 16f, out var spitterCell, out var spitterCellError) == false)
				return spitterCellError;
			if (TryFindClearSpawnCell(map, root + new IntVec3(6, 0, 0), 16f, out var symbiantCell, out var symbiantCellError) == false)
				return symbiantCellError;

			var previousDangerousAreas = ZombieSettings.Values.dangerousAreas.ToDictionary(pair => pair.Key, pair => pair.Value);
			var createdAreas = new List<Area>();
			var spawnedThings = new List<Thing>();
			try
			{
				_ = ZombieRuntimeActions.DestroyZombies(map);
				var colonist = GenerateAreaWorkflowPawn(Faction.OfPlayer, false);
				GenSpawn.Spawn(colonist, colonistCell, map, Rot4.South);
				DisablePawnWork(colonist);
				spawnedThings.Add(colonist);

				var normal = ZombieRuntimeActions.SpawnZombie(normalCell, map, ZombieType.Normal, true);
				if (normal != null)
					spawnedThings.Add(normal);
				ZombieSpitter.Spawn(map, spitterCell);
				var spitter = CurrentZombies(map).OfType<ZombieSpitter>().OrderBy(candidate => candidate.Position.DistanceToSquared(spitterCell)).FirstOrDefault();
				if (spitter != null)
					spawnedThings.Add(spitter);
				ZombieSymbiant.Spawn(map, symbiantCell);
				var symbiant = CurrentZombies(map).OfType<ZombieSymbiant>().OrderBy(candidate => candidate.Position.DistanceToSquared(symbiantCell)).FirstOrDefault();
				if (symbiant != null)
					spawnedThings.Add(symbiant);

				if (normal == null || spitter == null || symbiant == null)
				{
					return new
					{
						success = false,
						colonist = DescribePawn(colonist),
						normal = DescribeZombie(normal),
						spitter = DescribeZombie(spitter),
						symbiant = DescribeZombie(symbiant),
						error = "Could not create all dangerous-area pawn fixtures."
					};
				}

				if (map.areaManager.TryMakeNewAllowed(out Area_Allowed area) == false)
				{
					return new
					{
						success = false,
						error = "Could not create a test allowed area."
					};
				}
				createdAreas.Add(area);
				area.labelInt = "ZombielandRiskContract";

				void RunAreaStateUpdater()
				{
					ZombieAreaManager.pawnsInDanger.Clear();
					ZombieAreaManager.lastMap = null;
					var stateUpdater = typeof(ZombieAreaManager)
						.GetMethod("StateUpdater", BindingFlags.Static | BindingFlags.NonPublic)
						.Invoke(null, null) as System.Collections.IEnumerator;
					ZombieAreaManager.stateUpdater = stateUpdater;
					for (var i = 0; i < 64; i++)
					{
						if (ZombieAreaManager.stateUpdater.MoveNext())
							continue;
						ZombieAreaManager.stateUpdater = stateUpdater;
						break;
					}
				}

				object Snapshot(string label, AreaRiskMode mode, params IntVec3[] cells)
				{
					foreach (var cell in area.ActiveCells.ToArray())
						area[cell] = false;
					foreach (var cell in cells)
						area[cell] = true;

					ZombieSettings.Values.dangerousAreas.Clear();
					if (mode != AreaRiskMode.Ignore)
						ZombieSettings.Values.dangerousAreas[area] = mode;
					RunAreaStateUpdater();

					var entries = ZombieAreaManager.pawnsInDanger
						.Select(pair => new
						{
							pawn = DescribePawn(pair.Key),
							kind = DescribeZombieKind(pair.Key as Zombie, pair.Key as ZombieSymbiant, pair.Key as ZombieSpitter),
							area = pair.Value?.Label
						})
						.ToArray();
					return new
					{
						label,
						mode = mode.ToString(),
						activeCells = cells.Select(ZombieRuntimeActions.DescribeCell).ToArray(),
						colonist = ZombieAreaManager.pawnsInDanger.ContainsKey(colonist),
						normal = ZombieAreaManager.pawnsInDanger.ContainsKey(normal),
						spitter = ZombieAreaManager.pawnsInDanger.ContainsKey(spitter),
						symbiant = ZombieAreaManager.pawnsInDanger.ContainsKey(symbiant),
						entries
					};
				}

				var ignore = Snapshot("ignore", AreaRiskMode.Ignore, colonist.Position, normal.Position, spitter.Position, symbiant.Position);
				var colonistInside = Snapshot("colonistInside", AreaRiskMode.ColonistInside, colonist.Position, normal.Position, spitter.Position, symbiant.Position);
				var colonistOutside = Snapshot("colonistOutside", AreaRiskMode.ColonistOutside, normal.Position, spitter.Position, symbiant.Position);
				var zombieInside = Snapshot("zombieInside", AreaRiskMode.ZombieInside, colonist.Position, normal.Position, spitter.Position, symbiant.Position);
				var zombieOutside = Snapshot("zombieOutside", AreaRiskMode.ZombieOutside, colonist.Position);

				bool Has(object snapshot, string field)
				{
					return (bool)snapshot.GetType().GetProperty(field).GetValue(snapshot);
				}

				var success = Has(ignore, "colonist") == false
					&& Has(ignore, "normal") == false
					&& Has(ignore, "spitter") == false
					&& Has(ignore, "symbiant") == false
					&& Has(colonistInside, "colonist")
					&& Has(colonistInside, "normal") == false
					&& Has(colonistInside, "spitter") == false
					&& Has(colonistInside, "symbiant") == false
					&& Has(colonistOutside, "colonist")
					&& Has(colonistOutside, "normal") == false
					&& Has(colonistOutside, "spitter") == false
					&& Has(colonistOutside, "symbiant") == false
					&& Has(zombieInside, "colonist") == false
					&& Has(zombieInside, "normal")
					&& Has(zombieInside, "spitter")
					&& Has(zombieInside, "symbiant")
					&& Has(zombieOutside, "colonist") == false
					&& Has(zombieOutside, "normal")
					&& Has(zombieOutside, "spitter")
					&& Has(zombieOutside, "symbiant");

				return new
				{
					success,
					area = area.Label,
					colonist = DescribePawn(colonist),
					normal = DescribeZombie(normal),
					spitter = DescribeZombie(spitter),
					symbiant = DescribeZombie(symbiant),
					snapshots = new[]
					{
						ignore,
						colonistInside,
						colonistOutside,
						zombieInside,
						zombieOutside
					}
				};
			}
			finally
			{
				ZombieSettings.Values.dangerousAreas.Clear();
				foreach (var pair in previousDangerousAreas)
					ZombieSettings.Values.dangerousAreas[pair.Key] = pair.Value;
				foreach (var thing in spawnedThings)
					if (thing != null && thing.Destroyed == false)
						thing.Destroy(DestroyMode.Vanish);
				foreach (var area in createdAreas)
					if (area != null && map.areaManager.AllAreas.Contains(area))
						map.areaManager.Remove(area);
				ZombieAreaManager.pawnsInDanger.Clear();
				ZombieAreaManager.lastMap = null;
			}
		}

		static object EnsureAreaWorkflowFixture(Map map)
		{
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			var cells = GenRadial.RadialCellsAround(root, 4f, true)
				.Where(cell => cell.InBounds(map))
				.Take(20)
				.ToArray();

			var ignore = EnsureAreaWorkflowArea(map, "Ignore", AreaRiskMode.Ignore, new Color(0.45f, 0.45f, 0.45f), cells.Take(4));
			var colonistInside = EnsureAreaWorkflowArea(map, "ColonistInside", AreaRiskMode.ColonistInside, new Color(0.75f, 0.1f, 0.1f), cells.Skip(4).Take(4));
			var colonistOutside = EnsureAreaWorkflowArea(map, "ColonistOutside", AreaRiskMode.ColonistOutside, new Color(0.1f, 0.7f, 0.1f), cells.Skip(8).Take(4));
			var zombieInside = EnsureAreaWorkflowArea(map, "ZombieInside", AreaRiskMode.ZombieInside, new Color(0.9f, 0.45f, 0f), cells.Skip(12).Take(4));
			var zombieOutside = EnsureAreaWorkflowArea(map, "ZombieOutside", AreaRiskMode.ZombieOutside, new Color(1f, 0.1f, 0.55f), cells.Skip(16).Take(4));

			var fixtureAreas = new[] { ignore, colonistInside, colonistOutside, zombieInside, zombieOutside };

			return new
			{
				root = ZombieRuntimeActions.DescribeCell(root),
				labels = fixtureAreas.Select(area => area.Label).ToArray(),
				activeCellCounts = fixtureAreas.Select(area => area.ActiveCells.Count()).ToArray()
			};
		}

		static object RunAreaWorkflowBehavior(Map map, (Area_Allowed area, AreaRiskMode mode)[] fixtureAreas)
		{
			var colonistInsideArea = fixtureAreas.FirstOrDefault(pair => pair.mode == AreaRiskMode.ColonistInside).area;
			var zombieInsideArea = fixtureAreas.FirstOrDefault(pair => pair.mode == AreaRiskMode.ZombieInside).area;
			if (colonistInsideArea == null || zombieInsideArea == null)
			{
				return new
				{
					success = false,
					error = "Area workflow behavior needs ColonistInside and ZombieInside fixture areas."
				};
			}

			var oldBetterAvoidance = ZombieSettings.Values.betterZombieAvoidance;
			var oldHighlightDangerousAreas = ZombieSettings.Values.highlightDangerousAreas;
			try
			{
				ZombieSettings.Values.betterZombieAvoidance = true;
				ZombieSettings.Values.highlightDangerousAreas = true;
				_ = ZombieRuntimeActions.DestroyZombies(map);
				DestroyAreaWorkflowPawns(map);

				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root + new IntVec3(-4, 0, 0), 12f, out var actorCell, out var actorError) == false)
					return actorError;
				var zombieCell = GenRadial.RadialCellsAround(actorCell, 3f, false)
					.Where(cell => cell.InBounds(map))
					.Where(cell => cell.Standable(map))
					.Where(cell => cell.Fogged(map) == false)
					.Where(cell => cell.GetFirstPawn(map) == null)
					.OrderBy(cell => cell.DistanceToSquared(actorCell))
					.FirstOrDefault();
				if (zombieCell.IsValid == false)
				{
					return new
					{
						success = false,
						actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
						error = "No nearby clear zombie cell was found for the area workflow behavior fixture."
					};
				}

				var actor = GenerateAreaWorkflowPawn(Faction.OfPlayer, false);
				actor.Name = new NameSingle("ZL_Area_Worker");
				GenSpawn.Spawn(actor, actorCell, map, Rot4.South);
				DisablePawnWork(actor);
				actor.playerSettings.AreaRestrictionInPawnCurrentMap = colonistInsideArea;
				var config = ColonistSettings.Values.ConfigFor(actor);
				if (config != null)
					config.autoAvoidZombies = true;

				var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
				if (zombie == null)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
						error = "ZombieGenerator.SpawnZombie returned no area workflow zombie."
					};
				}
				zombie.Name = new NameSingle("ZL_Area_Zombie");
				zombie.state = ZombieState.Tracking;

				SetAreaCells(map, colonistInsideArea, actor.Position);
				SetAreaCells(map, zombieInsideArea, zombie.Position);
				ZombieSettings.Values.dangerousAreas[colonistInsideArea] = AreaRiskMode.ColonistInside;
				ZombieSettings.Values.dangerousAreas[zombieInsideArea] = AreaRiskMode.ZombieInside;
				RunZombieAreaStateUpdater();

				var colonistInDangerArea = ZombieAreaManager.pawnsInDanger.TryGetValue(actor, out var actorDangerArea)
					&& actorDangerArea == colonistInsideArea;
				var zombieInDangerArea = ZombieAreaManager.pawnsInDanger.TryGetValue(zombie, out var zombieDangerArea)
					&& zombieDangerArea == zombieInsideArea;
				var warningEntries = ZombieAreaManager.pawnsInDanger
					.Select(pair => new
					{
						pawn = DescribePawn(pair.Key),
						area = pair.Value?.Label
					})
					.ToArray();

				var avoidGrid = BuildAvoidGridForZombie(map, zombie);
				var actorAvoidCost = AvoidCost(avoidGrid, map, actor.Position);
				var actorShouldAvoid = avoidGrid.ShouldAvoid(map, actor.Position);
				var normalDanger = DangerUtility.GetDangerFor(actor.Position, actor, map);

				var forcedWait = JobMaker.MakeJob(JobDefOf.Wait_Combat);
				forcedWait.playerForced = true;
				actor.jobs.StartJob(forcedWait, JobCondition.InterruptForced, null, false, true);
				var forcedDanger = DangerUtility.GetDangerFor(actor.Position, actor, map);
				actor.jobs.EndCurrentJob(JobCondition.InterruptForced);

				actor.drafter.Drafted = true;
				var draftedWait = JobMaker.MakeJob(JobDefOf.Wait_Combat);
				draftedWait.playerForced = false;
				actor.jobs.StartJob(draftedWait, JobCondition.InterruptForced, null, false, true);
				for (var tick = 0; tick < 5; tick++)
					AdvanceGameTicks(1);
				var draftedJob = actor.CurJobDef?.defName;
				var draftedInterruptedToFlee = actor.CurJobDef == JobDefOf.Flee;
				actor.drafter.Drafted = false;
				actor.jobs.EndCurrentJob(JobCondition.InterruptForced);

				var waitJob = JobMaker.MakeJob(JobDefOf.Wait_Combat);
				waitJob.playerForced = false;
				actor.jobs.StartJob(waitJob, JobCondition.InterruptForced, null, false, true);
				var startedJob = actor.CurJobDef?.defName;
				var tickHit = -1;
				var samples = new List<object>();
				const int maxTicks = 30;
				for (var tick = 1; tick <= maxTicks; tick++)
				{
					AdvanceGameTicks(1);
					var currentJob = actor.CurJob;
					if (tick == 1 || tick == maxTicks || currentJob?.def == JobDefOf.Flee)
					{
						samples.Add(new
						{
							tick,
							job = actor.CurJobDef?.defName,
							currentJob?.playerForced,
							target = currentJob?.targetA.Cell.IsValid == true ? ZombieRuntimeActions.DescribeCell(currentJob.targetA.Cell) : null
						});
					}
					if (currentJob?.def == JobDefOf.Flee)
					{
						tickHit = tick;
						break;
					}
				}

				var fleeJob = actor.CurJob;
				var fleeDestination = fleeJob?.targetA.Cell ?? IntVec3.Invalid;
				var fleeDestinationAvoids = fleeDestination.IsValid && avoidGrid.ShouldAvoid(map, fleeDestination) == false;
				var fleeDestinationInAssignedArea = fleeDestination.IsValid && colonistInsideArea[fleeDestination];
				var hostilityResponse = VerifyAreaWorkflowHostilityResponse(map, actor, zombie, actorCell);
				var meleeInterruption = VerifyAreaWorkflowMeleeInterruption(map, actorCell, zombie, avoidGrid);
				var destroyCleanup = VerifyAreaWorkflowPawnDestroyCleanup(map, actorCell + new IntVec3(6, 0, 0), colonistInsideArea, zombieInsideArea);
				var assignedAreaFleePolicy = VerifyAreaWorkflowAssignedAreaFleePolicy(map, colonistInsideArea, zombie, avoidGrid);

				return new
				{
					success = colonistInDangerArea
							&& zombieInDangerArea
							&& actor.playerSettings.AreaRestrictionInPawnCurrentMap == colonistInsideArea
							&& actorShouldAvoid
							&& actorAvoidCost > 0
							&& normalDanger == Danger.Deadly
							&& forcedDanger != Danger.Deadly
							&& draftedInterruptedToFlee == false
							&& startedJob == JobDefOf.Wait_Combat.defName
							&& tickHit > 0
						&& fleeJob?.playerForced == true
						&& fleeDestinationAvoids
						&& fleeDestinationInAssignedArea == false
						&& ObjectSuccess(hostilityResponse)
						&& ObjectSuccess(meleeInterruption)
						&& ObjectSuccess(destroyCleanup)
						&& ObjectSuccess(assignedAreaFleePolicy),
					actor = DescribePawn(actor),
					zombie = DescribeZombie(zombie),
					assignedArea = actor.playerSettings.AreaRestrictionInPawnCurrentMap?.Label,
					colonistDangerArea = actorDangerArea?.Label,
					zombieDangerArea = zombieDangerArea?.Label,
					warningEntries,
					avoidance = new
					{
						actorAvoidCost,
						actorShouldAvoid,
						normalDanger = normalDanger.ToString(),
						forcedDanger = forcedDanger.ToString()
					},
					drafted = new
					{
						jobAfterTicks = draftedJob,
						interruptedToFlee = draftedInterruptedToFlee
					},
					undrafted = new
					{
						startedJob,
						tickHit,
						maxTicks,
						fleeJob = fleeJob?.def?.defName,
						fleeJobPlayerForced = fleeJob?.playerForced,
						fleeDestination = fleeDestination.IsValid ? ZombieRuntimeActions.DescribeCell(fleeDestination) : null,
						fleeDestinationAvoids,
						fleeDestinationInAssignedArea,
						samples
					},
					hostilityResponse,
					meleeInterruption,
					destroyCleanup,
					assignedAreaFleePolicy
				};
			}
			finally
			{
				ZombieSettings.Values.betterZombieAvoidance = oldBetterAvoidance;
				ZombieSettings.Values.highlightDangerousAreas = oldHighlightDangerousAreas;
			}
		}

		static object RunAreaWorkflowWarningUi(Map map, (Area_Allowed area, AreaRiskMode mode)[] fixtureAreas)
		{
			var colonistInsideArea = fixtureAreas.FirstOrDefault(pair => pair.mode == AreaRiskMode.ColonistInside).area;
			var zombieInsideArea = fixtureAreas.FirstOrDefault(pair => pair.mode == AreaRiskMode.ZombieInside).area;
			if (colonistInsideArea == null || zombieInsideArea == null)
			{
				return new
				{
					success = false,
					error = "Area warning UI needs ColonistInside and ZombieInside fixture areas."
				};
			}

			var messageTargets = PatchedMethodsForPatchClass("Messages_MessagesDoGUI_Patch");
			var drawTargets = PatchedMethodsForPatchClass("Message_Draw_Patch");
			var liveMessagesField = typeof(Messages).GetField("liveMessages", BindingFlags.Static | BindingFlags.NonPublic);
			var messagesTopLeftField = typeof(Messages).GetField("MessagesTopLeftStandard", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
			var messageDrawPrefix = typeof(Patches)
				.GetNestedType("Message_Draw_Patch", BindingFlags.NonPublic)
				?.GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic);
			var oldHighlightDangerousAreas = ZombieSettings.Values.highlightDangerousAreas;

			try
			{
				ZombieSettings.Values.highlightDangerousAreas = true;
				_ = ZombieRuntimeActions.DestroyZombies(map);
				DestroyAreaWorkflowPawns(map);
				ZombieAreaManager.pawnsInDanger.Clear();
				ZombieAreaManager.warningShowing = false;

				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root + new IntVec3(-4, 0, 0), 12f, out var actorCell, out var actorError) == false)
					return actorError;
				var zombieCell = GenRadial.RadialCellsAround(actorCell, 3f, false)
					.Where(cell => cell.InBounds(map))
					.Where(cell => cell.Standable(map))
					.Where(cell => cell.Fogged(map) == false)
					.Where(cell => cell.GetFirstPawn(map) == null)
					.OrderBy(cell => cell.DistanceToSquared(actorCell))
					.FirstOrDefault();
				if (zombieCell.IsValid == false)
				{
					return new
					{
						success = false,
						actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
						error = "No nearby clear zombie cell was found for the area warning UI fixture."
					};
				}

				var actor = GenerateAreaWorkflowPawn(Faction.OfPlayer, false);
				actor.Name = new NameSingle("ZL_Area_Warning_Worker");
				GenSpawn.Spawn(actor, actorCell, map, Rot4.South);
				DisablePawnWork(actor);
				actor.playerSettings.AreaRestrictionInPawnCurrentMap = colonistInsideArea;

				var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
				if (zombie == null)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
						error = "ZombieGenerator.SpawnZombie returned no area warning UI zombie."
					};
				}
				zombie.Name = new NameSingle("ZL_Area_Warning_Zombie");
				zombie.state = ZombieState.Tracking;

				SetAreaCells(map, colonistInsideArea, actor.Position);
				SetAreaCells(map, zombieInsideArea, zombie.Position);
				ZombieSettings.Values.dangerousAreas[colonistInsideArea] = AreaRiskMode.ColonistInside;
				ZombieSettings.Values.dangerousAreas[zombieInsideArea] = AreaRiskMode.ZombieInside;
				RunZombieAreaStateUpdater();

				var warningEntries = ZombieAreaManager.pawnsInDanger
					.Select(pair => new
					{
						pawn = DescribePawn(pair.Key),
						area = pair.Value?.Label
					})
					.ToArray();
				var actorInWarning = ZombieAreaManager.pawnsInDanger.TryGetValue(actor, out var actorArea)
					&& actorArea == colonistInsideArea;
				var zombieInWarning = ZombieAreaManager.pawnsInDanger.TryGetValue(zombie, out var zombieArea)
					&& zombieArea == zombieInsideArea;

				var liveMessageCountBefore = LiveMessageCount(liveMessagesField);
				const string probeMessage = "Zombieland area warning UI probe";
				Messages.Message(probeMessage, new LookTargets(actor, zombie), MessageTypeDefOf.NeutralEvent, false);
				var liveMessageCountAfter = LiveMessageCount(liveMessagesField);
				var messageAdded = liveMessageCountAfter > liveMessageCountBefore;

				var standardTopLeft = messagesTopLeftField?.GetValue(null) is Vector2 vector
					? vector
					: Vector2.zero;
				var yOffsetNoWarning = (int)standardTopLeft.y;
				var noWarningArgs = new object[] { yOffsetNoWarning };
				ZombieAreaManager.warningShowing = false;
				messageDrawPrefix?.Invoke(null, noWarningArgs);
				var yOffsetAfterNoWarning = (int)noWarningArgs[0];

				var yOffsetWithWarning = (int)standardTopLeft.y;
				var warningArgs = new object[] { yOffsetWithWarning };
				ZombieAreaManager.warningShowing = true;
				messageDrawPrefix?.Invoke(null, warningArgs);
				var yOffsetAfterWarning = (int)warningArgs[0];

				// Leave warningShowing false here; the real MessagesDoGUI prefix sets it during the render path.
				ZombieAreaManager.warningShowing = false;

				return new
				{
					success = messageTargets.Length > 0
						&& drawTargets.Length > 0
						&& messageDrawPrefix != null
						&& actorInWarning
						&& zombieInWarning
						&& warningEntries.Length >= 2
						&& messageAdded
						&& yOffsetAfterNoWarning == yOffsetNoWarning
						&& yOffsetAfterWarning == yOffsetWithWarning + 29,
					patchTargets = new
					{
						messagesDoGUI = messageTargets,
						messageDraw = drawTargets
					},
					actor = DescribePawn(actor),
					zombie = DescribeZombie(zombie),
					warningEntries,
					message = new
					{
						text = probeMessage,
						liveMessageCountBefore,
						liveMessageCountAfter,
						messageAdded
					},
					yOffsetProbe = new
					{
						standardTopLeft = new { x = standardTopLeft.x, y = standardTopLeft.y },
						noWarningBefore = yOffsetNoWarning,
						noWarningAfter = yOffsetAfterNoWarning,
						warningBefore = yOffsetWithWarning,
						warningAfter = yOffsetAfterWarning,
						expectedWarningAfter = yOffsetWithWarning + 29
					},
					cells = new
					{
						actor = ZombieRuntimeActions.DescribeCell(actor.Position),
						zombie = ZombieRuntimeActions.DescribeCell(zombie.Position)
					},
					note = "The fixture intentionally leaves pawnsInDanger and a live message in place so generic screenshot/UI tools can capture the real MessagesDoGUI prefix path."
				};
			}
			finally
			{
				ZombieSettings.Values.highlightDangerousAreas = oldHighlightDangerousAreas;
			}
		}

		static int LiveMessageCount(FieldInfo liveMessagesField)
		{
			return liveMessagesField?.GetValue(null) is System.Collections.ICollection collection
				? collection.Count
				: -1;
		}

		static object VerifyAreaWorkflowAssignedAreaFleePolicy(Map map, Area_Allowed assignedArea, Zombie zombie, AvoidGrid avoidGrid)
		{
			var preferInArea = VerifyAreaWorkflowAssignedAreaFleeCase(map, assignedArea, zombie, avoidGrid, "PreferInArea", true);
			var emergencyFallback = VerifyAreaWorkflowAssignedAreaFleeCase(map, assignedArea, zombie, avoidGrid, "EmergencyFallback", false);
			var vanillaFleeAndCower = VerifyAreaWorkflowVanillaFleeAndCowerPolicy(map, assignedArea, zombie);
			return new
			{
				success = ObjectSuccess(preferInArea) && ObjectSuccess(emergencyFallback) && ObjectSuccess(vanillaFleeAndCower),
				preferInArea,
				emergencyFallback,
				vanillaFleeAndCower
			};
		}

		static object VerifyAreaWorkflowAssignedAreaFleeCase(Map map, Area_Allowed assignedArea, Zombie zombie, AvoidGrid avoidGrid, string label, bool includeSafeAllowedCell)
		{
			if (TryFindAvoidDangerSpawnCell(map, avoidGrid, zombie.Position, out var pawnCell, out var pawnCellError) == false)
				return pawnCellError;

			var pawn = GenerateAreaWorkflowPawn(Faction.OfPlayer, false);
			pawn.Name = new NameSingle($"ZL_Area_{label}Flee");
			GenSpawn.Spawn(pawn, pawnCell, map, Rot4.South);
			DisablePawnWork(pawn);
			pawn.playerSettings.AreaRestrictionInPawnCurrentMap = assignedArea;
			var config = ColonistSettings.Values.ConfigFor(pawn);
			if (config != null)
				config.autoAvoidZombies = true;

			try
			{
				var candidateDestinations = CollectAvoidanceFleeDestinations(map, pawn, avoidGrid);
				var allowedSafeDestination = IntVec3.Invalid;
				if (includeSafeAllowedCell)
				{
					allowedSafeDestination = candidateDestinations
						.OrderByDescending(dest => (pawn.Position - dest).LengthHorizontalSquared)
						.FirstOrDefault();
					if (allowedSafeDestination.IsValid == false)
					{
						return new
						{
							success = false,
							label,
							pawn = DescribePawn(pawn),
							candidateCount = candidateDestinations.Count,
							error = "No safe flee destination candidate was available for the assigned-area preference probe."
						};
					}
					SetAreaCells(map, assignedArea, pawn.Position, allowedSafeDestination);
				}
				else
					SetAreaCells(map, assignedArea, pawn.Position);

				var pawnAvoidCost = AvoidCost(avoidGrid, map, pawn.Position);
				var pawnShouldAvoid = avoidGrid.ShouldAvoid(map, pawn.Position);
				var waitJob = JobMaker.MakeJob(JobDefOf.Wait);
				waitJob.playerForced = false;
				pawn.jobs.StartJob(waitJob, JobCondition.InterruptForced, null, false, true);
				var startedJob = pawn.CurJobDef?.defName;
				var tickHit = -1;
				var samples = new List<object>();
				for (var tick = 1; tick <= 30; tick++)
				{
					AdvanceGameTicks(1);
					var currentJob = pawn.CurJob;
					if (tick == 1 || tick == 30 || currentJob?.def == JobDefOf.Flee)
					{
						samples.Add(new
						{
							tick,
							job = pawn.CurJobDef?.defName,
							currentJob?.playerForced,
							target = currentJob?.targetA.Cell.IsValid == true ? ZombieRuntimeActions.DescribeCell(currentJob.targetA.Cell) : null
						});
					}
					if (currentJob?.def == JobDefOf.Flee)
					{
						tickHit = tick;
						break;
					}
				}

				var fleeJob = pawn.CurJob;
				var fleeDestination = fleeJob?.targetA.Cell ?? IntVec3.Invalid;
				var fleeDestinationAvoids = fleeDestination.IsValid && avoidGrid.ShouldAvoid(map, fleeDestination) == false;
				var fleeDestinationInAssignedArea = fleeDestination.IsValid && assignedArea[fleeDestination];
				var expectedInAssignedArea = includeSafeAllowedCell;
				return new
				{
					success = pawnShouldAvoid
							&& pawnAvoidCost > 0
							&& startedJob == JobDefOf.Wait.defName
							&& tickHit > 0
						&& fleeJob?.playerForced == true
						&& fleeDestinationAvoids
						&& fleeDestinationInAssignedArea == expectedInAssignedArea
						&& (includeSafeAllowedCell == false || fleeDestination == allowedSafeDestination),
					label,
					pawn = DescribePawn(pawn),
					assignedArea = assignedArea?.Label,
					pawnAvoidCost,
					pawnShouldAvoid,
					startedJob,
					tickHit,
					fleeJob = fleeJob?.def?.defName,
					fleeJobPlayerForced = fleeJob?.playerForced,
					allowedSafeDestination = allowedSafeDestination.IsValid ? ZombieRuntimeActions.DescribeCell(allowedSafeDestination) : null,
					fleeDestination = fleeDestination.IsValid ? ZombieRuntimeActions.DescribeCell(fleeDestination) : null,
					fleeDestinationAvoids,
					fleeDestinationInAssignedArea,
					expectedInAssignedArea,
					samples
				};
			}
			finally
			{
				ColonistSettings.Values.RemoveColonist(pawn);
				if (pawn.Destroyed == false)
					pawn.Destroy(DestroyMode.Vanish);
			}
		}

		static object VerifyAreaWorkflowVanillaFleeAndCowerPolicy(Map map, Area_Allowed assignedArea, Zombie zombie)
		{
			var tryGetFleeJob = AccessTools.Method(typeof(JobGiver_ConfigurableHostilityResponse), "TryGetFleeJob", new[] { typeof(Pawn) });
			if (tryGetFleeJob == null)
			{
				return new
				{
					success = false,
					error = "RimWorld.JobGiver_ConfigurableHostilityResponse.TryGetFleeJob was not found."
				};
			}

			var pawnCell = GenRadial.RadialCellsAround(zombie.Position, 7.5f, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.Where(cell => cell.InHorDistOf(zombie.Position, 7.9f))
				.Where(cell => GenSight.LineOfSight(cell, zombie.Position, map))
				.OrderByDescending(cell => cell.DistanceToSquared(zombie.Position))
				.FirstOrDefault();
			if (pawnCell.IsValid == false)
			{
				return new
				{
					success = false,
					zombie = DescribeZombie(zombie),
					error = "No clear vanilla flee-and-cower pawn cell was available near the zombie."
				};
			}

			var pawn = GenerateAreaWorkflowPawn(Faction.OfPlayer, false);
			pawn.Name = new NameSingle("ZL_Area_VanillaFleeAndCower");
			GenSpawn.Spawn(pawn, pawnCell, map, Rot4.South);
			DisablePawnWork(pawn);
			pawn.playerSettings.AreaRestrictionInPawnCurrentMap = assignedArea;

			try
			{
				var threats = new List<Thing> { zombie };
				var unrestrictedDestination = CellFinderLoose.GetFleeDest(pawn, threats);
				if (unrestrictedDestination.IsValid == false || unrestrictedDestination == pawn.Position)
				{
					return new
					{
						success = false,
						pawn = DescribePawn(pawn),
						zombie = DescribeZombie(zombie),
						unrestrictedDestination = unrestrictedDestination.IsValid ? ZombieRuntimeActions.DescribeCell(unrestrictedDestination) : null,
						error = "Vanilla CellFinderLoose.GetFleeDest did not produce a useful flee destination."
					};
				}

				var allowedDestination = IntVec3.Invalid;
				var expectedDestination = IntVec3.Invalid;
				foreach (var candidate in GenRadial.RadialCellsAround(pawn.Position, 30f, false)
					.Where(cell => cell.InBounds(map))
					.Where(cell => cell.Standable(map))
					.Where(cell => cell.Fogged(map) == false)
					.Where(cell => cell != pawn.Position && cell != unrestrictedDestination)
					.Where(cell => cell.GetFirstPawn(map) == null)
					.Where(cell => pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
					.OrderByDescending(cell => cell.DistanceToSquared(zombie.Position)))
				{
					SetAreaCells(map, assignedArea, pawn.Position, candidate);
					if (Patches.TryFindAreaRestrictedZombieFleeDestination(pawn, threats, 23f, assignedArea, out var areaDestination))
					{
						allowedDestination = candidate;
						expectedDestination = areaDestination;
						break;
					}
				}

				if (allowedDestination.IsValid == false)
				{
					return new
					{
						success = false,
						pawn = DescribePawn(pawn),
						zombie = DescribeZombie(zombie),
						unrestrictedDestination = ZombieRuntimeActions.DescribeCell(unrestrictedDestination),
						error = "No assigned-area vanilla flee destination candidate was found."
					};
				}

				var unrestrictedDestinationInAssignedArea = assignedArea[unrestrictedDestination];
				var giver = Activator.CreateInstance(typeof(JobGiver_ConfigurableHostilityResponse));
				var fleeJob = tryGetFleeJob.Invoke(giver, new object[] { pawn }) as Job;
				var fleeDestination = fleeJob?.targetA.Cell ?? IntVec3.Invalid;
				var fleeDestinationInAssignedArea = fleeDestination.IsValid && assignedArea[fleeDestination];
				var oldActiveJob = JobMaker.MakeJob(JobDefOf.FleeAndCower, unrestrictedDestination);
				pawn.jobs.StartJob(oldActiveJob, JobCondition.InterruptForced, null, false, true);
				AdvanceGameTicks(1);
				var activeRetargetedDestination = pawn.CurJob?.targetA.Cell ?? IntVec3.Invalid;
				var activeRetargetedDestinationInAssignedArea = activeRetargetedDestination.IsValid && assignedArea[activeRetargetedDestination];

				return new
				{
					success = fleeJob?.def == JobDefOf.FleeAndCower
						&& unrestrictedDestinationInAssignedArea == false
						&& fleeDestinationInAssignedArea
						&& fleeDestination == expectedDestination
						&& activeRetargetedDestinationInAssignedArea
						&& activeRetargetedDestination == expectedDestination,
					pawn = DescribePawn(pawn),
					zombie = DescribeZombie(zombie),
					assignedArea = assignedArea?.Label,
					unrestrictedDestination = ZombieRuntimeActions.DescribeCell(unrestrictedDestination),
					unrestrictedDestinationInAssignedArea,
					allowedDestination = ZombieRuntimeActions.DescribeCell(allowedDestination),
					expectedDestination = ZombieRuntimeActions.DescribeCell(expectedDestination),
					fleeJob = fleeJob?.def?.defName,
					fleeDestination = fleeDestination.IsValid ? ZombieRuntimeActions.DescribeCell(fleeDestination) : null,
					fleeDestinationInAssignedArea,
					activeRetargetedDestination = activeRetargetedDestination.IsValid ? ZombieRuntimeActions.DescribeCell(activeRetargetedDestination) : null,
					activeRetargetedDestinationInAssignedArea
				};
			}
			finally
			{
				ColonistSettings.Values.RemoveColonist(pawn);
				if (pawn.Destroyed == false)
					pawn.Destroy(DestroyMode.Vanish);
			}
		}

		static bool TryFindAvoidDangerSpawnCell(Map map, AvoidGrid avoidGrid, IntVec3 root, out IntVec3 result, out object error)
		{
			result = GenRadial.RadialCellsAround(root, 8f, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.Where(cell => avoidGrid.ShouldAvoid(map, cell))
				.OrderBy(cell => cell.DistanceToSquared(root))
				.FirstOrDefault();
			error = null;
			if (result.IsValid)
				return true;

			error = new
			{
				success = false,
				root = ZombieRuntimeActions.DescribeCell(root),
				error = "No clear avoid-danger pawn cell was available for the assigned-area flee policy probe."
			};
			return false;
		}

		static List<IntVec3> CollectAvoidanceFleeDestinations(Map map, Pawn pawn, AvoidGrid avoidGrid)
		{
			var safeDestinations = new List<IntVec3>();
			var pos = pawn.Position;
			map.floodFiller.FloodFill(pos, cell =>
			{
				if (cell.x == pos.x && cell.z == pos.z)
					return true;
				if (cell.Walkable(map) == false)
					return false;
				if (cell.GetEdifice(map) is Building_Door buildingDoor && buildingDoor.CanPhysicallyPass(pawn) == false)
					return false;
				return PawnUtility.AnyPawnBlockingPathAt(cell, pawn, true, false, false) == false;
			}, cell =>
			{
				if (cell.Standable(map) && avoidGrid.ShouldAvoid(map, cell) == false)
					safeDestinations.Add(cell);
				return false;
			}, 64, false, null);
			return safeDestinations;
		}

		static object VerifyAreaWorkflowHostilityResponse(Map map, Pawn fleeingPawn, Zombie nearbyZombie, IntVec3 root)
		{
			var patchTargets = PatchedMethodsForPatchClass("JobGiver_ConfigurableHostilityResponse_TryGetAttackNearbyEnemyJob_Patch");
			var prefix = FindNestedPatchMethod("JobGiver_ConfigurableHostilityResponse_TryGetAttackNearbyEnemyJob_Patch", "Prefix");
			var reachHelper = FindNestedPatchMethod("JobGiver_ConfigurableHostilityResponse_TryGetAttackNearbyEnemyJob_Patch", "MyCanReachImmediate");
			if (prefix == null || reachHelper == null)
			{
				return new
				{
					success = false,
					patchTargets,
					reflection = new
					{
						prefix = prefix != null,
						reachHelper = reachHelper != null
					},
					error = "Could not reflect hostility-response patch helpers."
				};
			}

			var prefixArgs = new object[] { fleeingPawn, JobMaker.MakeJob(JobDefOf.AttackMelee, nearbyZombie) };
			var prefixContinues = (bool)prefix.Invoke(null, prefixArgs);
			var prefixResult = prefixArgs[1] as Job;
			var nearbyZombieHostile = nearbyZombie != null && nearbyZombie.HostileTo(fleeingPawn);

			if (TryFindClearSpawnCell(map, root + new IntVec3(12, 0, 0), 12f, out var electricCell, out var electricError) == false)
				return electricError;
			if (TryFindClearSpawnCell(map, root + new IntVec3(16, 0, 0), 12f, out var normalCell, out var normalError) == false)
				return normalError;

			Zombie electric = null;
			Zombie normal = null;
			try
			{
				electric = ZombieRuntimeActions.SpawnZombie(electricCell, map, ZombieType.Electrifier, true);
				normal = ZombieRuntimeActions.SpawnZombie(normalCell, map, ZombieType.Normal, true);
				if (electric != null)
					electric.Name = new NameSingle("ZL_Area_HostilityActiveElectric");
				if (normal != null)
					normal.Name = new NameSingle("ZL_Area_HostilityNormalReach");

				if (electric == null || normal == null)
				{
					return new
					{
						success = false,
						patchTargets,
						electric = DescribeZombie(electric),
						normal = DescribeZombie(normal),
						error = "Could not create hostility-response reach fixtures."
					};
				}

				electric.electricDisabledUntil = GenTicks.TicksGame - 1;
				var electricTarget = new LocalTargetInfo(electric);
				var normalTarget = new LocalTargetInfo(normal);
				var vanillaElectricReach = fleeingPawn.CanReachImmediate(electricTarget, PathEndMode.Touch);
				var patchedElectricReach = (bool)reachHelper.Invoke(null, new object[] { fleeingPawn, electricTarget, PathEndMode.Touch });
				var vanillaNormalReach = fleeingPawn.CanReachImmediate(normalTarget, PathEndMode.Touch);
				var patchedNormalReach = (bool)reachHelper.Invoke(null, new object[] { fleeingPawn, normalTarget, PathEndMode.Touch });

				return new
				{
					success = patchTargets.Length > 0
						&& fleeingPawn.CurJobDef == JobDefOf.Flee
						&& (fleeingPawn.CurJob?.playerForced ?? false)
						&& nearbyZombieHostile
						&& prefixContinues == false
						&& prefixResult == null
						&& electric.IsActiveElectric
						&& vanillaElectricReach == false
						&& patchedElectricReach
						&& patchedNormalReach == vanillaNormalReach,
					patchTargets,
					forcedFleePrefix = new
					{
						pawn = DescribePawn(fleeingPawn),
						nearbyZombie = DescribeZombie(nearbyZombie),
						nearbyZombieHostile,
						prefixContinues,
						resultJob = prefixResult?.def?.defName
					},
					activeElectricReach = new
					{
						electric = DescribeZombie(electric),
						active = electric.IsActiveElectric,
						distanceSquared = fleeingPawn.Position.DistanceToSquared(electric.Position),
						vanillaReach = vanillaElectricReach,
						patchedReach = patchedElectricReach
					},
					normalReachControl = new
					{
						normal = DescribeZombie(normal),
						distanceSquared = fleeingPawn.Position.DistanceToSquared(normal.Position),
						vanillaReach = vanillaNormalReach,
						patchedReach = patchedNormalReach
					}
				};
			}
			finally
			{
				if (electric != null && electric.Destroyed == false)
					electric.Destroy(DestroyMode.Vanish);
				if (normal != null && normal.Destroyed == false)
					normal.Destroy(DestroyMode.Vanish);
			}
		}

		static object VerifyAreaWorkflowPawnDestroyCleanup(Map map, IntVec3 root, Area colonistArea, Area zombieArea)
		{
			var patchTargets = PatchedMethodsForPatchClass("Pawn_Destroy_Patch");
			if (TryFindClearSpawnCell(map, root, 10f, out var colonistCell, out var colonistError) == false)
				return colonistError;
			if (TryFindClearSpawnCell(map, root + new IntVec3(3, 0, 0), 10f, out var zombieCell, out var zombieError) == false)
				return zombieError;

			Pawn colonist = null;
			Zombie zombie = null;
			try
			{
				colonist = GenerateAreaWorkflowPawn(Faction.OfPlayer, false);
				colonist.Name = new NameSingle("ZL_Area_DestroyCleanupColonist");
				GenSpawn.Spawn(colonist, colonistCell, map, Rot4.South);
				DisablePawnWork(colonist);

				zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
				if (zombie == null)
				{
					return new
					{
						success = false,
						patchTargets,
						colonist = DescribePawn(colonist),
						zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
						error = "ZombieGenerator.SpawnZombie returned no destroy-cleanup zombie."
					};
				}
				zombie.Name = new NameSingle("ZL_Area_DestroyCleanupZombie");

				var config = ColonistSettings.Values.ConfigFor(colonist);
				var colonistConfigBefore = config != null && ColonistSettings.colonists.ContainsKey(colonist);
				ZombieAreaManager.pawnsInDanger[colonist] = colonistArea;
				ZombieAreaManager.pawnsInDanger[zombie] = zombieArea;
				var dangerContainsColonistBefore = ZombieAreaManager.pawnsInDanger.ContainsKey(colonist);
				var dangerContainsZombieBefore = ZombieAreaManager.pawnsInDanger.ContainsKey(zombie);

				colonist.Destroy(DestroyMode.Vanish);
				zombie.Destroy(DestroyMode.Vanish);

				var dangerContainsColonistAfter = ZombieAreaManager.pawnsInDanger.ContainsKey(colonist);
				var dangerContainsZombieAfter = ZombieAreaManager.pawnsInDanger.ContainsKey(zombie);
				var colonistConfigAfter = ColonistSettings.colonists.ContainsKey(colonist);

				return new
				{
					success = patchTargets.Length > 0
						&& colonistConfigBefore
						&& dangerContainsColonistBefore
						&& dangerContainsZombieBefore
						&& dangerContainsColonistAfter == false
						&& dangerContainsZombieAfter == false
						&& colonistConfigAfter == false,
					patchTargets,
					colonist = DescribePawn(colonist),
					zombie = DescribeZombie(zombie),
					colonistConfigBefore,
					colonistConfigAfter,
					dangerContainsColonistBefore,
					dangerContainsColonistAfter,
					dangerContainsZombieBefore,
					dangerContainsZombieAfter
				};
			}
			finally
			{
				if (colonist != null)
				{
					ZombieAreaManager.pawnsInDanger.Remove(colonist);
					ColonistSettings.Values.RemoveColonist(colonist);
					if (colonist.Destroyed == false)
						colonist.Destroy(DestroyMode.Vanish);
				}
				if (zombie != null)
				{
					ZombieAreaManager.pawnsInDanger.Remove(zombie);
					if (zombie.Destroyed == false)
						zombie.Destroy(DestroyMode.Vanish);
				}
			}
		}

		static object VerifyAreaWorkflowMeleeInterruption(Map map, IntVec3 root, Zombie zombie, AvoidGrid avoidGrid)
		{
			if (TryFindClearSpawnCell(map, root + new IntVec3(2, 0, 0), 8f, out var meleeCell, out var cellError) == false)
				return cellError;

			var pawn = GenerateAreaWorkflowPawn(Faction.OfPlayer, true);
			pawn.Name = new NameSingle("ZL_Area_MeleeInterruption");
			GenSpawn.Spawn(pawn, meleeCell, map, Rot4.South);
			DisablePawnWork(pawn);
			EquipAreaWorkflowMeleeWeapon(pawn);
			var config = ColonistSettings.Values.ConfigFor(pawn);
			if (config != null)
				config.autoAvoidZombies = true;

			var avoidCost = AvoidCost(avoidGrid, map, pawn.Position);
			var shouldAvoid = avoidGrid.ShouldAvoid(map, pawn.Position);
			var meleeJob = JobMaker.MakeJob(JobDefOf.AttackMelee, zombie);
			meleeJob.playerForced = false;
			pawn.jobs.StartJob(meleeJob, JobCondition.InterruptForced, null, false, true);
			var startedJob = pawn.CurJobDef?.defName;
			var tickHit = -1;
			var samples = new List<object>();
			for (var tick = 1; tick <= 30; tick++)
			{
				AdvanceGameTicks(1);
				var currentJob = pawn.CurJob;
				if (tick == 1 || tick == 30 || currentJob?.def == JobDefOf.Flee)
				{
					samples.Add(new
					{
						tick,
						job = pawn.CurJobDef?.defName,
						currentJob?.playerForced,
						target = currentJob?.targetA.Cell.IsValid == true ? ZombieRuntimeActions.DescribeCell(currentJob.targetA.Cell) : null
					});
				}
				if (currentJob?.def == JobDefOf.Flee)
				{
					tickHit = tick;
					break;
				}
			}
			var fleeJob = pawn.CurJob;
			var fleeDestination = fleeJob?.targetA.Cell ?? IntVec3.Invalid;
			var fleeDestinationAvoids = fleeDestination.IsValid && avoidGrid.ShouldAvoid(map, fleeDestination) == false;
			var result = new
			{
				success = shouldAvoid
					&& avoidCost > 0
					&& startedJob == JobDefOf.AttackMelee.defName
					&& tickHit > 0
					&& fleeJob?.playerForced == true
					&& fleeDestinationAvoids,
				pawn = DescribePawn(pawn),
				target = DescribeZombie(zombie),
				avoidCost,
				shouldAvoid,
				startedJob,
				tickHit,
				fleeJob = fleeJob?.def?.defName,
				fleeJobPlayerForced = fleeJob?.playerForced,
				fleeDestination = fleeDestination.IsValid ? ZombieRuntimeActions.DescribeCell(fleeDestination) : null,
				fleeDestinationAvoids,
				samples
			};
			if (pawn.Destroyed == false)
				pawn.Destroy(DestroyMode.Vanish);
			return result;
		}

		static void DestroyAreaWorkflowPawns(Map map)
		{
			foreach (var pawn in map.mapPawns.AllPawnsSpawned
				.Where(pawn => pawn.Name?.ToStringShort?.StartsWith("ZL_Area_", StringComparison.Ordinal) == true)
				.ToArray())
			{
				pawn.Destroy(DestroyMode.Vanish);
			}
		}

		static void SetAreaCells(Map map, Area area, params IntVec3[] activeCells)
		{
			foreach (var cell in area.ActiveCells.ToArray())
				area[cell] = false;
			foreach (var cell in activeCells)
				if (cell.InBounds(map))
					area[cell] = true;
		}

		static void RunZombieAreaStateUpdater()
		{
			ZombieAreaManager.pawnsInDanger.Clear();
			ZombieAreaManager.lastMap = null;
			var stateUpdater = typeof(ZombieAreaManager)
				.GetMethod("StateUpdater", BindingFlags.Static | BindingFlags.NonPublic)
				.Invoke(null, null) as System.Collections.IEnumerator;
			ZombieAreaManager.stateUpdater = stateUpdater;
			for (var i = 0; i < 96; i++)
			{
				if (ZombieAreaManager.stateUpdater.MoveNext())
					continue;
				ZombieAreaManager.stateUpdater = stateUpdater;
				break;
			}
		}

		static object RunAreaWorkflowTargeting(Map map)
		{
			var oldAttackMode = ZombieSettings.Values.attackMode;
			var oldEnemiesAttackZombies = ZombieSettings.Values.enemiesAttackZombies;
			var oldAnimalsAttackZombies = ZombieSettings.Values.animalsAttackZombies;
			var oldDoubleTapRequired = ZombieSettings.Values.doubleTapRequired;
			var oldBetterAvoidance = ZombieSettings.Values.betterZombieAvoidance;
			var spawnedThings = new List<Thing>();

			try
			{
				ZombieSettings.Values.attackMode = AttackMode.Everything;
				ZombieSettings.Values.enemiesAttackZombies = true;
				ZombieSettings.Values.animalsAttackZombies = true;
				ZombieSettings.Values.doubleTapRequired = true;
				ZombieSettings.Values.betterZombieAvoidance = false;
				_ = ZombieRuntimeActions.DestroyZombies(map);
				DestroyAreaWorkflowPawns(map);

				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root + new IntVec3(-10, 0, 0), 16f, out var shooterCell, out var shooterError) == false)
					return shooterError;

				var targetCells = GenRadial.RadialCellsAround(shooterCell, 14f, false)
					.Where(cell => cell.InBounds(map))
					.Where(cell => cell.Standable(map))
					.Where(cell => cell.Fogged(map) == false)
					.Where(cell => cell.GetFirstPawn(map) == null)
					.Where(cell => cell.DistanceTo(shooterCell) >= 6f)
					.Where(cell => GenSight.LineOfSight(shooterCell, cell, map, true))
					.OrderBy(cell => cell.DistanceToSquared(shooterCell))
					.Take(10)
					.ToArray();
				if (targetCells.Length < 8)
				{
					return new
					{
						success = false,
						shooterCell = ZombieRuntimeActions.DescribeCell(shooterCell),
						targetCellCount = targetCells.Length,
						error = "Not enough line-of-sight target cells were found for the mixed-targeting fixture."
					};
				}

				var player = SpawnArmedAreaWorkflowPawn(map, "ZL_Area_PlayerShooter", shooterCell, Faction.OfPlayer, spawnedThings);
				var enemy = SpawnArmedAreaWorkflowPawn(map, "ZL_Area_EnemyShooter", shooterCell + new IntVec3(0, 0, -2), Faction.OfAncientsHostile, spawnedThings);
				var animal = SpawnAreaWorkflowAnimal(map, "ZL_Area_TestAnimal", shooterCell + new IntVec3(0, 0, 2), spawnedThings);
				if (player == null || enemy == null || animal == null)
				{
					return new
					{
						success = false,
						player = DescribePawn(player),
						enemy = DescribePawn(enemy),
						animal = DescribePawn(animal),
						error = "Could not create all mixed-targeting attacker pawns."
					};
				}

				var playerVerb = player.equipment?.PrimaryEq?.PrimaryVerb;
				var enemyVerb = enemy.equipment?.PrimaryEq?.PrimaryVerb;
				var animalVerb = animal.CurrentEffectiveVerb;
				if (playerVerb == null || enemyVerb == null || animalVerb == null)
				{
					return new
					{
						success = false,
						playerVerb = DescribeVerb(playerVerb),
						enemyVerb = DescribeVerb(enemyVerb),
						animalVerb = DescribeVerb(animalVerb),
						error = "At least one targeting attacker had no effective verb."
					};
				}

				var normal = SpawnTargetZombie(map, targetCells[0], ZombieType.Normal, "ZL_Area_NormalTarget", spawnedThings);
				var roped = SpawnTargetZombie(map, targetCells[1], ZombieType.Normal, "ZL_Area_RopedTarget", spawnedThings);
				var confused = SpawnTargetZombie(map, targetCells[2], ZombieType.Normal, "ZL_Area_ConfusedTarget", spawnedThings);
				var electric = SpawnTargetZombie(map, targetCells[3], ZombieType.Electrifier, "ZL_Area_ElectricTarget", spawnedThings);
				var albino = SpawnTargetZombie(map, targetCells[4], ZombieType.Albino, "ZL_Area_AlbinoTarget", spawnedThings);
				var suicide = SpawnTargetZombie(map, targetCells[5], ZombieType.SuicideBomber, "ZL_Area_SuicideTarget", spawnedThings);
				var spitter = SpawnTargetSpitter(map, targetCells[6], "ZL_Area_SpitterTarget", spawnedThings);
				var symbiant = SpawnTargetSymbiant(map, targetCells[7], "ZL_Area_SymbiantTarget", spawnedThings);
				if (new Pawn[] { normal, roped, confused, electric, albino, suicide, spitter, symbiant }.Any(pawn => pawn == null))
				{
					return new
					{
						success = false,
						targets = new
						{
							normal = DescribeZombie(normal),
							roped = DescribeZombie(roped),
							confused = DescribeZombie(confused),
							electric = DescribeZombie(electric),
							albino = DescribeZombie(albino),
							suicide = DescribeZombie(suicide),
							spitter = DescribeZombie(spitter as ZombieSpitter),
							symbiant = DescribeZombie(symbiant as ZombieSymbiant)
						},
						error = "Could not create all mixed-targeting zombie pawns."
					};
				}
				roped.ropedBy = player;
				confused.paralyzedUntil = GenTicks.TicksAbs + 2500;
				RefreshZombieTargetCache(map);

				var allTargets = new List<IAttackTarget> { normal, roped, confused, electric, albino, suicide, spitter, symbiant };
				var playerAvailable = InvokeAvailableTargetsPatch(allTargets, player, playerVerb);
				var playerTargetIds = TargetIds(playerAvailable);
				var playerBest = AttackTargetFinder.BestAttackTarget(player, TargetScanFlags.NeedLOSToAll | TargetScanFlags.NeedThreat, thing => thing is Zombie, 0f, 20f);
				var playerRopedBest = BestSpecificTarget(player, roped);
				var playerConfusedBest = BestSpecificTarget(player, confused);

				ZombieSettings.Values.enemiesAttackZombies = false;
				var enemyDisabled = InvokeAvailableTargetsPatch(allTargets, enemy, enemyVerb);
				var enemyDisabledIds = TargetIds(enemyDisabled);
				var enemyNormalDisabledBest = BestSpecificTarget(enemy, normal);
				ZombieSettings.Values.enemiesAttackZombies = true;
				var enemyEnabled = InvokeAvailableTargetsPatch(allTargets, enemy, enemyVerb);
				var enemyEnabledIds = TargetIds(enemyEnabled);
				var enemyBest = AttackTargetFinder.BestAttackTarget(enemy, TargetScanFlags.NeedLOSToAll | TargetScanFlags.NeedThreat, thing => thing is Zombie, 0f, 20f);
				var enemyNormalEnabledBest = BestSpecificTarget(enemy, normal);
				var enemySpitterBest = BestSpecificTarget(enemy, spitter);
				var enemySymbiantBest = BestSpecificTarget(enemy, symbiant);
				var enemyElectricBest = BestSpecificTarget(enemy, electric);

				ZombieSettings.Values.animalsAttackZombies = false;
				var animalDisabledBest = AttackTargetFinder.BestAttackTarget(animal, TargetScanFlags.NeedThreat, thing => thing is Zombie, 0f, 20f);
				ZombieSettings.Values.animalsAttackZombies = true;
				var animalEnabledBest = AttackTargetFinder.BestAttackTarget(animal, TargetScanFlags.NeedThreat, thing => thing is Zombie, 0f, 20f);

				var normalScore = InvokeShootingScorePatch(normal, player, playerVerb);
				var suicideScore = InvokeShootingScorePatch(suicide, player, playerVerb);

				var oldFriendlyFireRadius = playerVerb.verbProps.ai_AvoidFriendlyFireRadius;
				float zombieBlastOffset;
				float colonistBlastOffset;
				float zombieConeOffset;
				float colonistConeOffset;
				try
				{
					playerVerb.verbProps.ai_AvoidFriendlyFireRadius = Math.Max(3f, oldFriendlyFireRadius);
					zombieBlastOffset = InvokeFriendlyFireOffset("FriendlyFireBlastRadiusTargetScoreOffset", normal, player, playerVerb);
					zombieConeOffset = InvokeFriendlyFireOffset("FriendlyFireConeTargetScoreOffset", normal, player, playerVerb);
					var friendly = SpawnAreaWorkflowPawn(map, "ZL_Area_FriendlyNearby", normal.Position + IntVec3.East, Faction.OfPlayer, spawnedThings);
					colonistBlastOffset = InvokeFriendlyFireOffset("FriendlyFireBlastRadiusTargetScoreOffset", normal, player, playerVerb);
					colonistConeOffset = InvokeFriendlyFireOffset("FriendlyFireConeTargetScoreOffset", normal, player, playerVerb);
				}
				finally
				{
					playerVerb.verbProps.ai_AvoidFriendlyFireRadius = oldFriendlyFireRadius;
				}

				var friendlyFirePatchOwners = new[]
				{
					PatchOwners("FriendlyFireBlastRadiusTargetScoreOffset"),
					PatchOwners("FriendlyFireConeTargetScoreOffset")
				};
				var friendlyFireHelper = VerifyFriendlyFireHelper(normal, player);
				var availablePatchOwners = PatchOwners("GetAvailableShootingTargetsByScore");
				var scorePatchOwners = PatchOwners("GetShootingTargetScore");
				var directHostility = VerifyAreaWorkflowDirectHostility(map, player, enemy, normal, spitter, symbiant, shooterCell, spawnedThings);
				var targetCache = VerifyAreaWorkflowTargetCache(map, normal, spitter, symbiant, shooterCell, spawnedThings);
				var availableBranches = VerifyAreaWorkflowAvailableTargetBranches(map, shooterCell, allTargets, normal, roped, confused, electric, albino, spitter, symbiant, spawnedThings);
				var waitAutoAttack = VerifyAreaWorkflowWaitAutoAttack(map, shooterCell, spawnedThings);
				var turretTargeting = VerifyAreaWorkflowTurretTargeting(map, shooterCell, player, spawnedThings);
				var tarSmokeMeleeTargeting = VerifyAreaWorkflowTarSmokeMeleeTargeting(map, shooterCell, spawnedThings);
				var tarSmokeAimChance = VerifyAreaWorkflowTarSmokeAimChance(map, shooterCell, spawnedThings);
				var downedCombat = VerifyAreaWorkflowDownedCombat(map, shooterCell, spawnedThings);
				var rangedProjectilePatches = VerifyAreaWorkflowRangedProjectilePatches(map, shooterCell + new IntVec3(24, 0, 13), spawnedThings);

				var success = playerTargetIds.Contains(StableId(normal))
					&& playerTargetIds.Contains(StableId(roped)) == false
					&& playerTargetIds.Contains(StableId(confused)) == false
					&& (playerVerb.CanHarmElectricZombies() || playerTargetIds.Contains(StableId(electric)) == false)
					&& playerRopedBest == null
					&& playerConfusedBest == null
					&& enemyDisabledIds.Contains(StableId(normal)) == false
					&& enemyEnabledIds.Contains(StableId(normal))
					&& enemyEnabledIds.Contains(StableId(spitter)) == false
					&& enemyEnabledIds.Contains(StableId(symbiant)) == false
					&& enemyEnabledIds.Contains(StableId(albino)) == false
					&& (enemyVerb.CanHarmElectricZombies() || enemyEnabledIds.Contains(StableId(electric)) == false)
					&& enemyNormalDisabledBest == null
					&& ReferenceEquals(enemyNormalEnabledBest?.Thing, normal)
					&& enemySpitterBest == null
					&& enemySymbiantBest == null
					&& (enemyVerb.CanHarmElectricZombies() || enemyElectricBest == null)
					&& animalDisabledBest == null
					&& ReferenceEquals(animalEnabledBest?.Thing, normal)
					&& suicideScore > normalScore
					&& colonistBlastOffset < zombieBlastOffset
					&& friendlyFirePatchOwners.All(ownerSet => ownerSet.Contains("net.pardeike.zombieland"))
					&& availablePatchOwners.Contains("net.pardeike.zombieland")
					&& scorePatchOwners.Contains("net.pardeike.zombieland")
					&& friendlyFireHelper.zombiesRemoved
					&& friendlyFireHelper.nonZombiesKept
					&& ObjectSuccess(directHostility)
					&& ObjectSuccess(targetCache)
					&& ObjectSuccess(availableBranches)
					&& ObjectSuccess(waitAutoAttack)
					&& ObjectSuccess(turretTargeting)
					&& ObjectSuccess(tarSmokeMeleeTargeting)
					&& ObjectSuccess(tarSmokeAimChance)
					&& ObjectSuccess(downedCombat)
					&& ObjectSuccess(rangedProjectilePatches);

				return new
				{
					success,
					attackers = new
					{
						player = DescribePawn(player),
						enemy = DescribePawn(enemy),
						animal = DescribePawn(animal),
						playerVerb = DescribeVerb(playerVerb),
						enemyVerb = DescribeVerb(enemyVerb),
						animalVerb = DescribeVerb(animalVerb)
					},
					targets = new
					{
						normal = DescribeZombie(normal),
						roped = DescribeZombie(roped),
						confused = DescribeZombie(confused),
						electric = DescribeZombie(electric),
						albino = DescribeZombie(albino),
						suicide = DescribeZombie(suicide),
						spitter = DescribeZombie(spitter as ZombieSpitter),
						symbiant = DescribeZombie(symbiant as ZombieSymbiant)
					},
					available = new
					{
						player = DescribeTargetPairs(playerAvailable),
						enemyDisabled = DescribeTargetPairs(enemyDisabled),
						enemyEnabled = DescribeTargetPairs(enemyEnabled)
					},
					bestTargets = new
					{
						player = DescribeTarget(playerBest),
						playerRoped = DescribeTarget(playerRopedBest),
						playerConfused = DescribeTarget(playerConfusedBest),
						enemy = DescribeTarget(enemyBest),
						enemyNormalDisabled = DescribeTarget(enemyNormalDisabledBest),
						enemyNormalEnabled = DescribeTarget(enemyNormalEnabledBest),
						enemySpitter = DescribeTarget(enemySpitterBest),
						enemySymbiant = DescribeTarget(enemySymbiantBest),
						enemyElectric = DescribeTarget(enemyElectricBest),
						animalDisabled = DescribeTarget(animalDisabledBest),
						animalEnabled = DescribeTarget(animalEnabledBest)
					},
					scores = new
					{
						normalScore,
						suicideScore
					},
					friendlyFire = new
					{
						zombieBlastOffset,
						colonistBlastOffset,
						zombieConeOffset,
						colonistConeOffset,
						patchOwners = friendlyFirePatchOwners,
						helper = new
						{
							friendlyFireHelper.zombiesRemoved,
							friendlyFireHelper.nonZombiesKept,
							kept = friendlyFireHelper.kept
						}
					},
					patchOwners = new
					{
						availableTargets = availablePatchOwners,
						shootingScore = scorePatchOwners
					},
					directHostility,
					targetCache,
					availableBranches,
					waitAutoAttack,
					turretTargeting,
					tarSmokeMeleeTargeting,
					tarSmokeAimChance,
					downedCombat,
					rangedProjectilePatches
				};
			}
			finally
			{
				ZombieSettings.Values.attackMode = oldAttackMode;
				ZombieSettings.Values.enemiesAttackZombies = oldEnemiesAttackZombies;
				ZombieSettings.Values.animalsAttackZombies = oldAnimalsAttackZombies;
				ZombieSettings.Values.doubleTapRequired = oldDoubleTapRequired;
				ZombieSettings.Values.betterZombieAvoidance = oldBetterAvoidance;
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}
		}

		static object RunAreaWorkflowBugReports(Map map)
		{
			var oldAttackMode = ZombieSettings.Values.attackMode;
			var oldEnemiesAttackZombies = ZombieSettings.Values.enemiesAttackZombies;
			var oldSpitterThreat = ZombieSettings.Values.spitterThreat;
			var spawnedThings = new List<Thing>();

			try
			{
				ZombieSettings.Values.attackMode = AttackMode.Everything;
				ZombieSettings.Values.enemiesAttackZombies = true;
				ZombieSettings.Values.spitterThreat = 1f;
				_ = ZombieRuntimeActions.DestroyZombies(map);
				DestroyAreaWorkflowPawns(map);

				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root + new IntVec3(-18, 0, 0), 18f, out var raiderCell, out var raiderError) == false)
					return raiderError;

				var raider = SpawnArmedAreaWorkflowPawn(map, "ZL_Area_BugRaider", raiderCell, Faction.OfAncientsHostile, spawnedThings);
				var verb = raider?.equipment?.PrimaryEq?.PrimaryVerb;
				if (raider == null || verb == null)
				{
					return new
					{
						success = false,
						raider = DescribePawn(raider),
						verb = DescribeVerb(verb),
						error = "Could not create an armed hostile raider for the bug-report fixture."
					};
				}

				if (FindLineOfSightTargetCell(map, raiderCell, 6f, out var nearCell) == false
					|| FindLineOfSightTargetCell(map, raiderCell, 9f, out var edgeCell) == false
					|| FindLineOfSightTargetCell(map, raiderCell, 10f, out var justOutsideCell) == false
					|| FindLineOfSightTargetCell(map, raiderCell, 25f, out var farCell) == false)
				{
					return new
					{
						success = false,
						raiderCell = ZombieRuntimeActions.DescribeCell(raiderCell),
						error = "Could not find all line-of-sight target cells for the raider distance fixture."
					};
				}

				var near = SpawnTargetZombie(map, nearCell, ZombieType.Normal, "ZL_Area_BugRaiderNearZombie", spawnedThings);
				var edge = SpawnTargetZombie(map, edgeCell, ZombieType.Normal, "ZL_Area_BugRaiderEdgeZombie", spawnedThings);
				var justOutside = SpawnTargetZombie(map, justOutsideCell, ZombieType.Normal, "ZL_Area_BugRaiderOutsideZombie", spawnedThings);
				var far = SpawnTargetZombie(map, farCell, ZombieType.Normal, "ZL_Area_BugRaiderFarZombie", spawnedThings);
				var spitter = SpawnTargetSpitter(map, raiderCell + new IntVec3(0, 0, -6), "ZL_Area_BugSpitterHealth", spawnedThings);
				if (new Pawn[] { near, edge, justOutside, far, spitter }.Any(pawn => pawn == null))
				{
					return new
					{
						success = false,
						targets = new
						{
							near = DescribeZombie(near),
							edge = DescribeZombie(edge),
							justOutside = DescribeZombie(justOutside),
							far = DescribeZombie(far),
							spitter = DescribeZombie(spitter)
						},
						error = "Could not create all bug-report target pawns."
					};
				}
				spitter.colonyDurabilityFactor = 1f;

				var allTargets = new List<IAttackTarget> { near, edge, justOutside, far };
				var available = InvokeAvailableTargetsPatch(allTargets, raider, verb);
				var targeting = new[]
				{
					DescribeRaiderZombieTargetCase("near6", raider, near, verb, available),
					DescribeRaiderZombieTargetCase("edge9", raider, edge, verb, available),
					DescribeRaiderZombieTargetCase("outside10", raider, justOutside, verb, available),
					DescribeRaiderZombieTargetCase("far25", raider, far, verb, available)
				};

				var spitterHealth = DescribeSpitterHealthAndDamage(spitter);
				return new
				{
					success = targeting.All(ObjectSuccess) && ObjectSuccess(spitterHealth),
					raider = DescribePawn(raider),
					verb = DescribeVerb(verb),
					settings = new
					{
						ZombieSettings.Values.attackMode,
						ZombieSettings.Values.enemiesAttackZombies,
						ZombieSettings.Values.spitterThreat
					},
					available = DescribeTargetPairs(available),
					targeting,
					spitterHealth
				};
			}
			finally
			{
				ZombieSettings.Values.attackMode = oldAttackMode;
				ZombieSettings.Values.enemiesAttackZombies = oldEnemiesAttackZombies;
				ZombieSettings.Values.spitterThreat = oldSpitterThreat;
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}
		}

		static object RunAreaWorkflowSpitterReadiness(Map map)
		{
			var oldSpitterThreat = ZombieSettings.Values.spitterThreat;
			var oldThreatScale = ZombieSettings.Values.threatScale;
			var spawnedThings = new List<Thing>();

			try
			{
				ZombieSettings.Values.spitterThreat = 1f;
				ZombieSettings.Values.threatScale = 1f;
				_ = ZombieRuntimeActions.DestroyZombies(map);
				DestroyAreaWorkflowPawns(map);

				var minimumFactor = ZombieSpitter.CalculateColonyDurabilityFactor(1, 0f, 0f);
				var noColonistFactor = ZombieSpitter.CalculateColonyDurabilityFactor(0, 0f, 0f);
				var weakFactor = ZombieSpitter.CalculateColonyDurabilityFactor(5, 750f, 10f);
				var partialFactor = ZombieSpitter.CalculateColonyDurabilityFactor(5, 750f, 120f);
				var armedFactor = ZombieSpitter.CalculateColonyDurabilityFactor(5, 750f, 250f);
				var factorCases = new[]
				{
					new
					{
						success = Approximately(noColonistFactor, 1f),
						label = "no-colonists",
						freeColonists = 0,
						colonistPoints = 0f,
						supportPoints = 0f,
						factor = noColonistFactor
					},
					new
					{
						success = Approximately(weakFactor, minimumFactor),
						label = "weak-colony",
						freeColonists = 5,
						colonistPoints = 750f,
						supportPoints = 10f,
						factor = weakFactor
					},
					new
					{
						success = partialFactor > minimumFactor && partialFactor < 1f,
						label = "partial-support",
						freeColonists = 5,
						colonistPoints = 750f,
						supportPoints = 120f,
						factor = partialFactor
					},
					new
					{
						success = Approximately(armedFactor, 1f),
						label = "armed-colony",
						freeColonists = 5,
						colonistPoints = 750f,
						supportPoints = 250f,
						factor = armedFactor
					}
				};

				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (FindAreaWorkflowCells(map, root + new IntVec3(8, 0, -8), 2, 12f, true, out var cells, out var cellError) == false)
					return cellError;

				var weakSpitter = SpawnTargetSpitter(map, cells[0], "ZL_Area_SpitterReadinessWeak", spawnedThings);
				var armedSpitter = SpawnTargetSpitter(map, cells[1], "ZL_Area_SpitterReadinessArmed", spawnedThings);
				if (weakSpitter == null || armedSpitter == null)
				{
					return new
					{
						success = false,
						weakSpitter = DescribeZombie(weakSpitter),
						armedSpitter = DescribeZombie(armedSpitter),
						error = "Could not create both spitter readiness damage fixtures."
					};
				}

				var weakDamage = DescribeSpitterReadinessDamageCase("weak-colony", weakSpitter, weakFactor, 8601);
				var armedDamage = DescribeSpitterReadinessDamageCase("armed-colony", armedSpitter, armedFactor, 8611);
				var damageComparison = new
				{
					success = weakDamage.success
						&& armedDamage.success
						&& weakDamage.bulletScaledAmount > armedDamage.bulletScaledAmount * 1.5f
						&& weakDamage.cutScaledAmount > armedDamage.cutScaledAmount * 1.5f
						&& weakDamage.cutInjuryDelta > armedDamage.cutInjuryDelta * 1.5f,
					weakBulletScaledVsArmed = armedDamage.bulletScaledAmount <= 0f ? 0f : weakDamage.bulletScaledAmount / armedDamage.bulletScaledAmount,
					weakCutScaledVsArmed = armedDamage.cutScaledAmount <= 0f ? 0f : weakDamage.cutScaledAmount / armedDamage.cutScaledAmount,
					weakBulletVsArmed = armedDamage.bulletInjuryDelta <= 0f ? 0f : weakDamage.bulletInjuryDelta / armedDamage.bulletInjuryDelta,
					weakCutVsArmed = armedDamage.cutInjuryDelta <= 0f ? 0f : weakDamage.cutInjuryDelta / armedDamage.cutInjuryDelta
				};

				return new
				{
					success = factorCases.All(item => item.success) && damageComparison.success,
					settings = new
					{
						ZombieSettings.Values.spitterThreat,
						ZombieSettings.Values.threatScale
					},
					minimumFactor,
					factorCases,
					damage = new
					{
						weak = weakDamage,
						armed = armedDamage,
						comparison = damageComparison
					}
				};
			}
			finally
			{
				ZombieSettings.Values.spitterThreat = oldSpitterThreat;
				ZombieSettings.Values.threatScale = oldThreatScale;
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}
		}

		static bool FindLineOfSightTargetCell(Map map, IntVec3 source, float targetDistance, out IntVec3 result)
		{
			var targetDistanceSquared = targetDistance * targetDistance;
			result = GenRadial.RadialCellsAround(source, targetDistance + 1f, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.Where(cell => Mathf.Abs(cell.DistanceToSquared(source) - targetDistanceSquared) <= targetDistance)
				.Where(cell => GenSight.LineOfSight(source, cell, map, true))
				.OrderBy(cell => Mathf.Abs(cell.DistanceToSquared(source) - targetDistanceSquared))
				.FirstOrDefault();
			return result.IsValid;
		}

		static object DescribeRaiderZombieTargetCase(string label, Pawn raider, Zombie zombie, Verb verb, IEnumerable<Pair<IAttackTarget, float>> available)
		{
			var best = BestSpecificTarget(raider, zombie, 40f);
			var distanceSquared = raider.Position.DistanceToSquared(zombie.Position);
			var expectedSelectable = distanceSquared <= 81;
			var availableContains = available.Any(pair => ReferenceEquals(pair.first.Thing, zombie));
			var bestMatches = ReferenceEquals(best?.Thing, zombie);
			var attackDistance = verb == null ? 1f : verb.verbProps.range * verb.verbProps.range;
			return new
			{
				success = bestMatches == expectedSelectable && availableContains == expectedSelectable,
				label,
				expectedSelectable,
				distanceSquared,
				attackDistance,
				zombieAvoidRadius = Tools.ZombieAvoidRadius(zombie, true),
				hostileToRaider = zombie.HostileTo(raider),
				activeThreatToRaiderFaction = GenHostility.IsActiveThreatTo(zombie, raider.Faction, false, false),
				availableContains,
				best = DescribeTarget(best),
				zombie = DescribeZombie(zombie)
			};
		}

		static object DescribeSpitterHealthAndDamage(ZombieSpitter spitter)
		{
			var part = spitter?.health?.hediffSet?.GetNotMissingParts().FirstOrDefault();
			var rangedPart = FindNonHeadPart(spitter);
			var meleePart = FindNonHeadPart(spitter);
			var incomingDamageFactor = spitter.GetStatValue(StatDefOf.IncomingDamageFactor);
			var rangedBefore = TotalInjurySeverity(spitter);
			var rangedDamage = ApplyPawnDamage(spitter, DamageDefOf.Bullet, 10f, rangedPart, 7601, 0f);
			var rangedAfter = TotalInjurySeverity(spitter);
			var meleeBefore = rangedAfter;
			var meleeDamage = ApplyPawnDamage(spitter, DamageDefOf.Cut, 2f, meleePart, 7602, 0f);
			var meleeAfter = TotalInjurySeverity(spitter);
			var healthScale = spitter?.HealthScale ?? 0f;
			var coreMaxHealth = part?.def?.GetMaxHealth(spitter) ?? 0f;
			var colonyDurabilityFactor = spitter?.colonyDurabilityFactor ?? 0f;
			return new
			{
				success = spitter != null
					&& Mathf.Approximately(incomingDamageFactor, 1f)
					&& Mathf.Approximately(colonyDurabilityFactor, 1f)
					&& coreMaxHealth >= 4000f
					&& rangedDamage.injuryDelta <= 2f
					&& meleeDamage.injuryDelta <= 8f,
				spitter = DescribeZombie(spitter),
				colonyDurabilityFactor,
				baseHealthScale = spitter?.RaceProps?.baseHealthScale,
				lifeStageHealthScaleFactor = spitter?.ageTracker?.CurLifeStage?.healthScaleFactor,
				healthScale,
				statMaxHitPoints = spitter?.MaxHitPoints,
				incomingDamageFactor,
				corePart = DescribeBodyPartDetail(part),
				corePartBaseHitPoints = part?.def?.hitPoints,
				coreMaxHealth,
				rangedPart = DescribeBodyPartDetail(rangedPart),
				rangedBefore,
				rangedAfter,
				rangedDamage,
				meleePart = DescribeBodyPartDetail(meleePart),
				meleeBefore,
				meleeAfter,
				meleeDamage
			};
		}

		static SpitterReadinessDamageResult DescribeSpitterReadinessDamageCase(string label, ZombieSpitter spitter, float durabilityFactor, int seed)
		{
			if (spitter == null)
			{
				return new SpitterReadinessDamageResult
				{
					success = false,
					label = label,
					durabilityFactor = durabilityFactor,
					error = "No spitter was available for the readiness damage case."
				};
			}
			spitter.colonyDurabilityFactor = durabilityFactor;
			var rangedPart = FindNonHeadPart(spitter);
			var meleePart = FindNonHeadPart(spitter);
			var bulletScaledAmount = ScaledSpitterDamageAmount(spitter, DamageDefOf.Bullet, 10f);
			var cutScaledAmount = ScaledSpitterDamageAmount(spitter, DamageDefOf.Cut, 2f);
			var bulletDamage = ApplyPawnDamageUntilInjured(spitter, DamageDefOf.Bullet, 10f, rangedPart, seed, 0f);
			var cutDamage = ApplyPawnDamageUntilInjured(spitter, DamageDefOf.Cut, 2f, meleePart, seed + 100, 0f);
			return new SpitterReadinessDamageResult
			{
				success = durabilityFactor > 0f
					&& Mathf.Approximately(spitter.colonyDurabilityFactor, durabilityFactor)
					&& bulletScaledAmount > 0f
					&& cutScaledAmount > bulletScaledAmount
					&& bulletDamage.injuryDelta > 0f
					&& cutDamage.injuryDelta > bulletDamage.injuryDelta,
				label = label,
				durabilityFactor = durabilityFactor,
				spitter = DescribeZombie(spitter),
				rangedPart = DescribeBodyPartDetail(rangedPart),
				meleePart = DescribeBodyPartDetail(meleePart),
				bulletScaledAmount = bulletScaledAmount,
				cutScaledAmount = cutScaledAmount,
				bulletInjuryDelta = bulletDamage.injuryDelta,
				cutInjuryDelta = cutDamage.injuryDelta,
				bulletDamage = bulletDamage,
				cutDamage = cutDamage
			};
		}

		static float ScaledSpitterDamageAmount(ZombieSpitter spitter, DamageDef damageDef, float amount)
		{
			var dinfo = new DamageInfo(damageDef, amount, 0f, 0f, null, null, null, DamageInfo.SourceCategory.ThingOrUnknown, null, true, true);
			spitter.ApplySpitterDamageScaling(ref dinfo);
			return dinfo.Amount;
		}

		static object RunAreaWorkflowRangedProjectiles(Map map)
		{
			var spawnedThings = new List<Thing>();
			try
			{
				return VerifyAreaWorkflowRangedProjectilePatches(map, new IntVec3(114, 0, 113), spawnedThings);
			}
			finally
			{
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}
		}

		static object RunAreaWorkflowDangerFlee(Map map)
		{
			var spawnedThings = new List<Thing>();
			try
			{
				return VerifyAreaWorkflowDangerAndFlee(map, new IntVec3(91, 0, 113), spawnedThings);
			}
			finally
			{
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}
		}

		static object RunAreaWorkflowSmartMelee(Map map)
		{
			var spawnedThings = new List<Thing>();
			try
			{
				return VerifyAreaWorkflowSmartMelee(map, new IntVec3(104, 0, 113), spawnedThings);
			}
			finally
			{
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}
		}

		static object RunAreaWorkflowJobGate(Map map)
		{
			var spawnedThings = new List<Thing>();
			try
			{
				return VerifyAreaWorkflowJobGate(map, new IntVec3(88, 0, 126), spawnedThings);
			}
			finally
			{
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}
		}

		static object RunAreaWorkflowSpecialMeleeVerbs(Map map)
		{
			var spawnedThings = new List<Thing>();
			try
			{
				return VerifyAreaWorkflowSpecialMeleeVerbs(map, new IntVec3(104, 0, 126), spawnedThings);
			}
			finally
			{
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}
		}

		static object RunAreaWorkflowSpecialDamage(Map map)
		{
			var spawnedThings = new List<Thing>();
			try
			{
				return VerifyAreaWorkflowSpecialDamage(map, new IntVec3(104, 0, 136), spawnedThings);
			}
			finally
			{
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}
		}

		static object RunAreaWorkflowCeCompat(Map map)
		{
			var spawnedThings = new List<Thing>();
			try
			{
				return VerifyAreaWorkflowCeCompat(map, spawnedThings);
			}
			finally
			{
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}
		}

		static object RunAreaWorkflowCeRangedProjectiles(Map map)
		{
			var spawnedThings = new List<Thing>();
			try
			{
				return VerifyAreaWorkflowCeRangedProjectiles(map, new IntVec3(126, 0, 113), spawnedThings);
			}
			finally
			{
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}
		}

		static object RunAreaWorkflowElectrifierMeleeDamage(Map map)
		{
			var spawnedThings = new List<Thing>();
			try
			{
				return VerifyAreaWorkflowElectrifierMeleeDamage(map, new IntVec3(118, 0, 126), spawnedThings);
			}
			finally
			{
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}
		}

		static object RunAreaWorkflowAnimalResponse(Map map)
		{
			var spawnedThings = new List<Thing>();
			try
			{
				return VerifyAreaWorkflowAnimalResponse(map, new IntVec3(80, 0, 142), spawnedThings);
			}
			finally
			{
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}
		}

		static object RunAreaWorkflowUiState(Map map)
		{
			var spawnedThings = new List<Thing>();
			try
			{
				return VerifyAreaWorkflowUiState(map, new IntVec3(104, 0, 156), spawnedThings);
			}
			finally
			{
				Find.Selector?.ClearSelection();
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}
		}

		static object RunAreaWorkflowDownedCrawlerVisuals(Map map)
		{
			var spawnedThings = new List<Thing>();
			try
			{
				return VerifyAreaWorkflowDownedCrawlerVisuals(map, new IntVec3(124, 0, 156), spawnedThings);
			}
			finally
			{
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}
		}

		static object RunAreaWorkflowRootPlayHooks(Map map)
		{
			var spawnedThings = new List<Thing>();
			try
			{
				return VerifyAreaWorkflowRootPlayHooks(map, new IntVec3(144, 0, 156), spawnedThings);
			}
			finally
			{
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}
		}

		static object RunAreaWorkflowRenderNodeGraphics(Map map)
		{
			var spawnedThings = new List<Thing>();
			try
			{
				return VerifyAreaWorkflowRenderNodeGraphics(map, new IntVec3(164, 0, 156), spawnedThings);
			}
			finally
			{
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}
		}

		static object RunAreaWorkflowEffecterSuppression(Map map)
		{
			var spawnedThings = new List<Thing>();
			try
			{
				return VerifyAreaWorkflowEffecterSuppression(map, new IntVec3(184, 0, 156), spawnedThings);
			}
			finally
			{
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}
		}

		static object RunAreaWorkflowGraphicMultiTexture(Map map)
		{
			var spawnedThings = new List<Thing>();
			try
			{
				return VerifyAreaWorkflowGraphicMultiTexture(map, new IntVec3(204, 0, 156), spawnedThings);
			}
			finally
			{
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}
		}

		static object RunAreaWorkflowWarmupScaling(Map map)
		{
			var spawnedThings = new List<Thing>();
			try
			{
				return VerifyAreaWorkflowWarmupScaling(map, new IntVec3(184, 0, 156), spawnedThings);
			}
			finally
			{
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}
		}

		static object RunAreaWorkflowZombieStats(Map map)
		{
			var spawnedThings = new List<Thing>();
			try
			{
				return VerifyAreaWorkflowZombieStats(map, new IntVec3(204, 0, 156), spawnedThings);
			}
			finally
			{
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}
		}

		static object RunAreaWorkflowVisualSupport(Map map)
		{
			var spawnedThings = new List<Thing>();
			try
			{
				return VerifyAreaWorkflowVisualSupport(map, new IntVec3(184, 0, 156), spawnedThings);
			}
			finally
			{
				foreach (var thing in spawnedThings.Where(thing => thing != null && thing.Destroyed == false).ToArray())
					thing.Destroy(DestroyMode.Vanish);
			}
		}

		static Pawn SpawnAreaWorkflowPawn(Map map, string name, IntVec3 cell, Faction faction, List<Thing> spawnedThings)
		{
			var pawn = GenerateAreaWorkflowPawn(faction, false);
			pawn.Name = new NameSingle(name);
			GenSpawn.Spawn(pawn, cell, map, Rot4.South);
			DisablePawnWork(pawn);
			spawnedThings.Add(pawn);
			return pawn;
		}

		static Pawn GenerateAreaWorkflowPawn(Faction faction, bool mustBeCapableOfViolence)
		{
			var request = new PawnGenerationRequest(
				PawnKindDefOf.Colonist,
				faction,
				PawnGenerationContext.NonPlayer,
				forceGenerateNewPawn: true,
				canGeneratePawnRelations: false,
				mustBeCapableOfViolence: mustBeCapableOfViolence,
				colonistRelationChanceFactor: 0f,
				forceNoIdeo: true,
				dontGiveWeapon: true,
				forceNoGear: true);
			return PawnGenerator.GeneratePawn(request);
		}

		static Pawn SpawnArmedAreaWorkflowPawn(Map map, string name, IntVec3 cell, Faction faction, List<Thing> spawnedThings)
		{
			Pawn pawn = null;
			for (var attempt = 0; attempt < 20; attempt++)
			{
				var candidate = GenerateAreaWorkflowPawn(faction, true);
				if (candidate.WorkTagIsDisabled(WorkTags.Violent))
					continue;
				pawn = candidate;
				break;
			}
			pawn ??= GenerateAreaWorkflowPawn(faction, true);
			pawn.Name = new NameSingle(name);
			GenSpawn.Spawn(pawn, cell, map, Rot4.South);
			DisablePawnWork(pawn);
			spawnedThings.Add(pawn);
			pawn.equipment?.DestroyAllEquipment(DestroyMode.Vanish);
			var weaponDef = DefDatabase<ThingDef>.GetNamed("Gun_BoltActionRifle", false)
				?? DefDatabase<ThingDef>.GetNamed("Gun_Pistol", false);
			var weapon = weaponDef == null ? null : ThingMaker.MakeThing(weaponDef) as ThingWithComps;
			if (weapon != null)
				pawn.equipment.AddEquipment(weapon);
			return pawn;
		}

		static Pawn SpawnAreaWorkflowMech(Map map, string name, IntVec3 cell, Faction faction, List<Thing> spawnedThings)
		{
			var kindDef = PawnKindDefOf.Mech_Scyther
				?? DefDatabase<PawnKindDef>.AllDefs.FirstOrDefault(def => def.race?.race?.IsMechanoid == true);
			if (kindDef == null)
				return null;
			var request = new PawnGenerationRequest(
				kindDef,
				faction,
				PawnGenerationContext.NonPlayer,
				forceGenerateNewPawn: true,
				canGeneratePawnRelations: false,
				colonistRelationChanceFactor: 0f,
				forceNoIdeo: true);
			var pawn = PawnGenerator.GeneratePawn(request);
			pawn.Name = new NameSingle(name);
			GenSpawn.Spawn(pawn, cell, map, Rot4.South);
			spawnedThings.Add(pawn);
			return pawn;
		}

		static Pawn SpawnAreaWorkflowAnimal(Map map, string name, IntVec3 cell, List<Thing> spawnedThings)
		{
			var kindDef = DefDatabase<PawnKindDef>.GetNamed("Warg", false)
				?? DefDatabase<PawnKindDef>.GetNamed("Husky", false)
				?? DefDatabase<PawnKindDef>.AllDefs.FirstOrDefault(def => def.RaceProps?.Animal == true && def.RaceProps.IsFlesh);
			if (kindDef == null)
				return null;
			var pawn = PawnGenerator.GeneratePawn(kindDef, Faction.OfPlayer);
			pawn.Name = new NameSingle(name);
			GenSpawn.Spawn(pawn, cell, map, Rot4.South);
			spawnedThings.Add(pawn);
			return pawn;
		}

		static Pawn SpawnAreaWorkflowAnimal(Map map, string name, IntVec3 cell, Faction faction, List<Thing> spawnedThings, Predicate<PawnKindDef> predicate)
		{
			var kindDef = DefDatabase<PawnKindDef>.AllDefs
				.Where(def => def.RaceProps?.Animal == true && def.RaceProps.IsFlesh)
				.Where(def => predicate?.Invoke(def) != false)
				.OrderByDescending(def => def.combatPower)
				.FirstOrDefault();
			if (kindDef == null)
				return null;
			var pawn = PawnGenerator.GeneratePawn(kindDef, faction);
			pawn.Name = new NameSingle(name);
			GenSpawn.Spawn(pawn, cell, map, Rot4.South);
			spawnedThings.Add(pawn);
			return pawn;
		}

		static Zombie SpawnTargetZombie(Map map, IntVec3 cell, ZombieType type, string name, List<Thing> spawnedThings)
		{
			var zombie = ZombieRuntimeActions.SpawnZombie(cell, map, type, true);
			if (zombie == null)
				return null;
			zombie.Name = new NameSingle(name);
			zombie.state = ZombieState.Tracking;
			spawnedThings.Add(zombie);
			return zombie;
		}

		static ZombieSpitter SpawnTargetSpitter(Map map, IntVec3 cell, string name, List<Thing> spawnedThings)
		{
			var existing = CurrentZombies(map).OfType<ZombieSpitter>().Select(StableId).ToHashSet();
			var originalShowLetters = ZombieSettings.Values.showZombieEventLetters;
			var originalPlaySiren = ZombieSettings.Values.playZombieEventSiren;
			try
			{
				ZombieSettings.Values.showZombieEventLetters = false;
				ZombieSettings.Values.playZombieEventSiren = false;
				ZombieSpitter.Spawn(map, cell);
			}
			finally
			{
				ZombieSettings.Values.showZombieEventLetters = originalShowLetters;
				ZombieSettings.Values.playZombieEventSiren = originalPlaySiren;
			}
			var spitter = CurrentZombies(map).OfType<ZombieSpitter>()
				.FirstOrDefault(candidate => existing.Contains(StableId(candidate)) == false)
				?? CurrentZombies(map).OfType<ZombieSpitter>().OrderBy(candidate => candidate.Position.DistanceToSquared(cell)).FirstOrDefault();
			if (spitter == null)
				return null;
			spitter.Name = new NameSingle(name);
			spitter.state = SpitterState.Idle;
			spawnedThings.Add(spitter);
			return spitter;
		}

		static ZombieSymbiant SpawnTargetSymbiant(Map map, IntVec3 cell, string name, List<Thing> spawnedThings)
		{
			var existing = CurrentZombies(map).OfType<ZombieSymbiant>().Select(StableId).ToHashSet();
			ZombieSymbiant.Spawn(map, cell);
			var symbiant = CurrentZombies(map).OfType<ZombieSymbiant>()
				.FirstOrDefault(candidate => existing.Contains(StableId(candidate)) == false)
				?? CurrentZombies(map).OfType<ZombieSymbiant>().OrderBy(candidate => candidate.Position.DistanceToSquared(cell)).FirstOrDefault();
			if (symbiant == null)
				return null;
			symbiant.Name = new NameSingle(name);
			spawnedThings.Add(symbiant);
			return symbiant;
		}

		static void RefreshZombieTargetCache(Map map)
		{
			var tickManager = map.GetComponent<TickManager>();
			if (tickManager?.allZombiesCached == null)
				return;
			tickManager.allZombiesCached.RemoveWhere(zombie => zombie == null || zombie.Destroyed || zombie.Spawned == false || zombie.Dead);
			foreach (var zombie in CurrentZombies(map).OfType<Zombie>())
				_ = tickManager.allZombiesCached.Add(zombie);
		}

		static List<Pair<IAttackTarget, float>> InvokeAvailableTargetsPatch(List<IAttackTarget> targets, IAttackTargetSearcher searcher, Verb verb)
		{
			var rawTargets = targets.ToList();
			var prefix = typeof(AttackTargetFinder_GetAvailableShootingTargetsByScore_Patch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic);
			prefix?.Invoke(null, new object[] { rawTargets, searcher, verb });
			var result = rawTargets.Select(target => new Pair<IAttackTarget, float>(target, 0f)).ToList();
			var postfix = typeof(AttackTargetFinder_GetAvailableShootingTargetsByScore_Patch).GetMethod("Postfix", BindingFlags.Static | BindingFlags.NonPublic);
			postfix?.Invoke(null, new object[] { result, searcher, verb });
			return result;
		}

		static float InvokeShootingScorePatch(IAttackTarget target, IAttackTargetSearcher searcher, Verb verb)
		{
			var prefix = typeof(AttackTargetFinder_GetShootingTargetScore_Patch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.NonPublic);
			var args = new object[] { searcher, target, verb, 0f };
			var runOriginal = (bool)(prefix?.Invoke(null, args) ?? true);
			if (runOriginal == false)
				return (float)args[3];
			var method = typeof(AttackTargetFinder).GetMethod("GetShootingTargetScore", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			return method == null ? 0f : (float)method.Invoke(null, new object[] { target, searcher, verb });
		}

		static IAttackTarget BestSpecificTarget(IAttackTargetSearcher searcher, Thing target)
		{
			return BestSpecificTarget(searcher, target, 20f);
		}

		static IAttackTarget BestSpecificTarget(IAttackTargetSearcher searcher, Thing target, float maxDist)
		{
			return AttackTargetFinder.BestAttackTarget(
				searcher,
				TargetScanFlags.NeedLOSToAll | TargetScanFlags.NeedThreat,
				thing => ReferenceEquals(thing, target),
				0f,
				maxDist);
		}

		static (bool zombiesRemoved, bool nonZombiesKept, object[] kept) VerifyFriendlyFireHelper(Zombie zombie, Pawn nonZombie)
		{
			var method = typeof(AttackTargetFinder_FriendlyFire_Patch).GetMethod("RemoveZombies", BindingFlags.Static | BindingFlags.NonPublic);
			var input = new List<Thing> { zombie, nonZombie };
			var kept = method?.Invoke(null, new object[] { input }) as List<Thing> ?? new List<Thing>();
			return (
				kept.Any(thing => thing is Zombie) == false,
				kept.Contains(nonZombie),
				kept.Select(thing => new
				{
					id = StableId(thing),
					defName = thing?.def?.defName,
					label = thing?.LabelCap
				}).Cast<object>().ToArray());
		}

		static object VerifyAreaWorkflowDirectHostility(Map map, Pawn player, Pawn enemy, Zombie normal, ZombieSpitter spitter, ZombieSymbiant symbiant, IntVec3 root, List<Thing> spawnedThings)
		{
			var settings = ZombieSettings.Values;
			var oldEnemiesAttackZombies = settings.enemiesAttackZombies;
			var oldAnimalsAttackZombies = settings.animalsAttackZombies;
			var oldAttackMode = settings.attackMode;
			var oldEnemyInfectionState = enemy.InfectionState();
			var zombieFaction = Find.FactionManager.FirstFactionOfDef(ZombieDefOf.Zombies);

			try
			{
				if (TryFindClearSpawnCell(map, root + new IntVec3(4, 0, 4), 10f, out var wildAnimalCell, out var cellError) == false)
					return cellError;

				if (zombieFaction == null)
				{
					return new
					{
						success = false,
						error = "No zombie faction was loaded."
					};
				}

				var wildAnimal = SpawnAreaWorkflowAnimal(map, "ZL_Area_WildHostilityAnimal", wildAnimalCell, spawnedThings);
				if (wildAnimal == null)
				{
					return new
					{
						success = false,
						error = "Could not create a non-colony animal for direct hostility checks."
					};
				}
				wildAnimal.SetFaction(null);

				settings.enemiesAttackZombies = false;
				settings.animalsAttackZombies = false;
				settings.attackMode = AttackMode.Everything;
				var playerThing = TryHostileTo(player, normal);
				var enemyThingDisabled = TryHostileTo(enemy, normal);
				var animalThingDisabled = TryHostileTo(wildAnimal, normal);
				var normalThreatToPlayer = GenHostility.IsActiveThreatTo(normal, Faction.OfPlayer, false, false);
				var normalThreatToNull = GenHostility.IsActiveThreatTo(normal, null, false, false);
				var normalThreatToEnemyDisabled = GenHostility.IsActiveThreatTo(normal, enemy.Faction, false, false);

				settings.enemiesAttackZombies = true;
				settings.animalsAttackZombies = true;
				var enemyThingEnabled = TryHostileTo(enemy, normal);
				var animalThingEnabled = TryHostileTo(wildAnimal, normal);
				var enemyFaction = TryHostileTo(enemy, zombieFaction);
				var spitterToEnemyFaction = TryHostileTo(spitter, enemy.Faction);
				var symbiantToEnemyFaction = TryHostileTo(symbiant, enemy.Faction);
				var normalThreatToEnemyEverything = GenHostility.IsActiveThreatTo(normal, enemy.Faction, false, false);

				settings.attackMode = AttackMode.OnlyColonists;
				var normalThreatToEnemyOnlyColonists = GenHostility.IsActiveThreatTo(normal, enemy.Faction, false, false);

				settings.attackMode = AttackMode.Everything;
				enemy.SetInfectionState(InfectionState.Infecting);
				var enemyThingInfecting = TryHostileTo(enemy, normal);

				var noErrors = new[]
				{
					playerThing,
					enemyThingDisabled,
					enemyThingEnabled,
					animalThingDisabled,
					animalThingEnabled,
					enemyFaction,
					spitterToEnemyFaction,
					symbiantToEnemyFaction,
					enemyThingInfecting
				}.All(sample => sample.error == null);

				var success = noErrors
					&& playerThing.value
					&& enemyThingDisabled.value == false
					&& enemyThingEnabled.value
					&& animalThingDisabled.value == false
					&& animalThingEnabled.value
					&& enemyFaction.value
					&& spitterToEnemyFaction.value == false
					&& symbiantToEnemyFaction.value == false
					&& enemyThingInfecting.value == false
					&& normalThreatToPlayer == false
					&& normalThreatToNull == false
					&& normalThreatToEnemyDisabled == false
					&& normalThreatToEnemyEverything
					&& normalThreatToEnemyOnlyColonists == false;

				return new
				{
					success,
					noErrors,
					samples = new
					{
						playerThing = DescribeHostility(playerThing),
						enemyThingDisabled = DescribeHostility(enemyThingDisabled),
						enemyThingEnabled = DescribeHostility(enemyThingEnabled),
						animalThingDisabled = DescribeHostility(animalThingDisabled),
						animalThingEnabled = DescribeHostility(animalThingEnabled),
						enemyFaction = DescribeHostility(enemyFaction),
						spitterToEnemyFaction = DescribeHostility(spitterToEnemyFaction),
						symbiantToEnemyFaction = DescribeHostility(symbiantToEnemyFaction),
						enemyThingInfecting = DescribeHostility(enemyThingInfecting)
					},
					activeThreat = new
					{
						normalThreatToPlayer,
						normalThreatToNull,
						normalThreatToEnemyDisabled,
						normalThreatToEnemyEverything,
						normalThreatToEnemyOnlyColonists
					}
				};
			}
			finally
			{
				settings.enemiesAttackZombies = oldEnemiesAttackZombies;
				settings.animalsAttackZombies = oldAnimalsAttackZombies;
				settings.attackMode = oldAttackMode;
				enemy.SetInfectionState(oldEnemyInfectionState);
			}
		}

		static object VerifyAreaWorkflowTargetCache(Map map, Zombie normal, ZombieSpitter spitter, ZombieSymbiant symbiant, IntVec3 root, List<Thing> spawnedThings)
		{
			if (TryFindClearSpawnCell(map, root + new IntVec3(4, 0, -4), 10f, out var hostileCell, out var cellError) == false)
				return cellError;

			var hostile = SpawnAreaWorkflowPawn(map, "ZL_Area_CacheHostile", hostileCell, Faction.OfAncientsHostile, spawnedThings);
			var cache = map.attackTargetsCache;
			cache.UpdateTarget(hostile);
			cache.UpdateTarget(normal);
			cache.UpdateTarget(spitter);
			cache.UpdateTarget(symbiant);

			var before = cache.TargetsHostileToColony;
			var containsHostileBefore = before.Contains(hostile);
			var containsNormalBefore = before.Contains(normal);
			var containsSpitterBefore = before.Contains(spitter);
			var containsSymbiantBefore = before.Contains(symbiant);
			var zombielandTargetsBefore = before
				.Select(target => target.Thing)
				.Where(thing => thing is Zombie || thing is ZombieSpitter || thing is ZombieSymbiant)
				.Select(thing => thing.def?.defName)
				.Distinct()
				.OrderBy(defName => defName)
				.ToArray();

			var hostileDescription = DescribePawn(hostile);
			hostile.Destroy(DestroyMode.Vanish);
			cache.UpdateTarget(hostile);
			var after = cache.TargetsHostileToColony;
			var containsHostileAfter = after.Contains(hostile);

			return new
			{
				success = containsHostileBefore
					&& containsNormalBefore == false
					&& containsSpitterBefore == false
					&& containsSymbiantBefore == false
					&& zombielandTargetsBefore.Length == 0
					&& containsHostileAfter == false,
				hostile = hostileDescription,
				before = new
				{
					count = before.Count,
					containsHostile = containsHostileBefore,
					containsNormal = containsNormalBefore,
					containsSpitter = containsSpitterBefore,
					containsSymbiant = containsSymbiantBefore,
					zombielandTargets = zombielandTargetsBefore
				},
				after = new
				{
					count = after.Count,
					containsHostile = containsHostileAfter
				}
			};
		}

		static object VerifyAreaWorkflowAvailableTargetBranches(
			Map map,
			IntVec3 root,
			List<IAttackTarget> allTargets,
			Zombie normal,
			Zombie roped,
			Zombie confused,
			Zombie electric,
			Zombie albino,
			ZombieSpitter spitter,
			ZombieSymbiant symbiant,
			List<Thing> spawnedThings)
		{
			if (TryFindClearSpawnCell(map, root + new IntVec3(-4, 0, 4), 12f, out var friendlyHumanCell, out var friendlyHumanCellError) == false)
				return friendlyHumanCellError;
			if (TryFindClearSpawnCell(map, root + new IntVec3(-2, 0, 6), 12f, out var playerMechCell, out var playerMechCellError) == false)
				return playerMechCellError;
			if (TryFindClearSpawnCell(map, root + new IntVec3(2, 0, 6), 12f, out var friendlyMechCell, out var friendlyMechCellError) == false)
				return friendlyMechCellError;

			var friendlyHuman = SpawnArmedAreaWorkflowPawn(map, "ZL_Area_FriendlyHumanSearcher", friendlyHumanCell, null, spawnedThings);
			var playerMech = SpawnAreaWorkflowMech(map, "ZL_Area_PlayerMechSearcher", playerMechCell, Faction.OfPlayer, spawnedThings);
			var friendlyMech = SpawnAreaWorkflowMech(map, "ZL_Area_FriendlyMechSearcher", friendlyMechCell, null, spawnedThings);
			if (friendlyHuman == null || playerMech == null || friendlyMech == null)
			{
				return new
				{
					success = false,
					friendlyHuman = DescribePawn(friendlyHuman),
					playerMech = DescribePawn(playerMech),
					friendlyMech = DescribePawn(friendlyMech),
					error = "Could not create all available-target branch pawn searchers."
				};
			}

			var friendlyHumanVerb = friendlyHuman.equipment?.PrimaryEq?.PrimaryVerb;
			var playerMechVerb = playerMech.CurrentEffectiveVerb;
			var friendlyMechVerb = friendlyMech.CurrentEffectiveVerb;
			if (friendlyHumanVerb == null || playerMechVerb == null || friendlyMechVerb == null)
			{
				return new
				{
					success = false,
					friendlyHumanVerb = DescribeVerb(friendlyHumanVerb),
					playerMechVerb = DescribeVerb(playerMechVerb),
					friendlyMechVerb = DescribeVerb(friendlyMechVerb),
					error = "At least one available-target branch searcher had no effective verb."
				};
			}

			if (SpawnAreaWorkflowTurretGun(map, root + new IntVec3(4, 0, 6), Faction.OfPlayer, spawnedThings, out var playerTurret, out var playerTurretError) == false)
				return playerTurretError;
			if (SpawnAreaWorkflowTurretGun(map, root + new IntVec3(8, 0, 6), Faction.OfAncientsHostile, spawnedThings, out var enemyTurret, out var enemyTurretError) == false)
				return enemyTurretError;

			var playerTurretVerb = playerTurret.CurrentEffectiveVerb;
			var enemyTurretVerb = enemyTurret.CurrentEffectiveVerb;
			if (playerTurretVerb == null || enemyTurretVerb == null)
			{
				return new
				{
					success = false,
					playerTurretVerb = DescribeVerb(playerTurretVerb),
					enemyTurretVerb = DescribeVerb(enemyTurretVerb),
					error = "At least one available-target branch turret had no effective verb."
				};
			}

			var oldAttackMode = ZombieSettings.Values.attackMode;
			var oldEnemiesAttackZombies = ZombieSettings.Values.enemiesAttackZombies;
			var oldAnimalsAttackZombies = ZombieSettings.Values.animalsAttackZombies;
			try
			{
				ZombieSettings.Values.attackMode = AttackMode.Everything;
				ZombieSettings.Values.enemiesAttackZombies = true;
				ZombieSettings.Values.animalsAttackZombies = true;

				var friendlyHumanIds = TargetIds(InvokeAvailableTargetsPatch(allTargets, friendlyHuman, friendlyHumanVerb));
				var friendlyMechIds = TargetIds(InvokeAvailableTargetsPatch(allTargets, friendlyMech, friendlyMechVerb));
				var playerThingIds = TargetIds(InvokeAvailableTargetsPatch(allTargets, playerTurret, playerTurretVerb));

				ZombieSettings.Values.attackMode = AttackMode.OnlyHumans;
				var playerMechOnlyHumansIds = TargetIds(InvokeAvailableTargetsPatch(allTargets, playerMech, playerMechVerb));

				ZombieSettings.Values.attackMode = AttackMode.Everything;
				var playerMechEverythingIds = TargetIds(InvokeAvailableTargetsPatch(allTargets, playerMech, playerMechVerb));

				ZombieSettings.Values.enemiesAttackZombies = false;
				var enemyThingDisabledIds = TargetIds(InvokeAvailableTargetsPatch(allTargets, enemyTurret, enemyTurretVerb));

				ZombieSettings.Values.enemiesAttackZombies = true;
				var enemyThingEnabledIds = TargetIds(InvokeAvailableTargetsPatch(allTargets, enemyTurret, enemyTurretVerb));

				var cases = new[]
				{
					DescribeAvailableBranchCase(
						"friendlyHumanEverything",
						friendlyHumanIds,
						ContainsTarget(friendlyHumanIds, normal)
							&& ContainsTarget(friendlyHumanIds, roped) == false
							&& ContainsTarget(friendlyHumanIds, confused) == false
							&& ContainsTarget(friendlyHumanIds, albino) == false
							&& ContainsTarget(friendlyHumanIds, spitter) == false
							&& ContainsTarget(friendlyHumanIds, symbiant) == false
							&& (friendlyHumanVerb.CanHarmElectricZombies() || ContainsTarget(friendlyHumanIds, electric) == false),
						normal, roped, confused, electric, albino, spitter, symbiant),
					DescribeAvailableBranchCase(
						"friendlyMechEverything",
						friendlyMechIds,
						ContainsTarget(friendlyMechIds, normal)
							&& ContainsTarget(friendlyMechIds, roped) == false
							&& ContainsTarget(friendlyMechIds, confused) == false
							&& ContainsTarget(friendlyMechIds, albino) == false
							&& ContainsTarget(friendlyMechIds, spitter) == false
							&& ContainsTarget(friendlyMechIds, symbiant) == false
							&& (friendlyMechVerb.CanHarmElectricZombies() || ContainsTarget(friendlyMechIds, electric) == false),
						normal, roped, confused, electric, albino, spitter, symbiant),
					DescribeAvailableBranchCase(
						"playerMechOnlyHumans",
						playerMechOnlyHumansIds,
						playerMechOnlyHumansIds.Length == 0,
						normal, roped, confused, electric, albino, spitter, symbiant),
					DescribeAvailableBranchCase(
						"playerMechEverything",
						playerMechEverythingIds,
						ContainsTarget(playerMechEverythingIds, normal)
							&& ContainsTarget(playerMechEverythingIds, roped) == false
							&& ContainsTarget(playerMechEverythingIds, confused) == false
							&& ContainsTarget(playerMechEverythingIds, albino)
							&& ContainsTarget(playerMechEverythingIds, spitter)
							&& ContainsTarget(playerMechEverythingIds, symbiant)
							&& (playerMechVerb.CanHarmElectricZombies() || ContainsTarget(playerMechEverythingIds, electric) == false),
						normal, roped, confused, electric, albino, spitter, symbiant),
					DescribeAvailableBranchCase(
						"playerThingEverything",
						playerThingIds,
						ContainsTarget(playerThingIds, normal)
							&& ContainsTarget(playerThingIds, roped) == false
							&& ContainsTarget(playerThingIds, confused) == false
							&& ContainsTarget(playerThingIds, albino) == false
							&& ContainsTarget(playerThingIds, spitter)
							&& ContainsTarget(playerThingIds, symbiant)
							&& (playerTurretVerb.CanHarmElectricZombies() || ContainsTarget(playerThingIds, electric) == false),
						normal, roped, confused, electric, albino, spitter, symbiant),
					DescribeAvailableBranchCase(
						"enemyThingDisabled",
						enemyThingDisabledIds,
						enemyThingDisabledIds.Length == 0,
						normal, roped, confused, electric, albino, spitter, symbiant),
					DescribeAvailableBranchCase(
						"enemyThingEnabled",
						enemyThingEnabledIds,
						ContainsTarget(enemyThingEnabledIds, normal)
							&& ContainsTarget(enemyThingEnabledIds, roped)
							&& ContainsTarget(enemyThingEnabledIds, confused)
							&& ContainsTarget(enemyThingEnabledIds, albino) == false
							&& ContainsTarget(enemyThingEnabledIds, spitter) == false
							&& ContainsTarget(enemyThingEnabledIds, symbiant) == false
							&& (enemyTurretVerb.CanHarmElectricZombies() || ContainsTarget(enemyThingEnabledIds, electric) == false),
						normal, roped, confused, electric, albino, spitter, symbiant)
				};

				return new
				{
					success = cases.All(ObjectSuccess),
					attackers = new
					{
						friendlyHuman = DescribePawn(friendlyHuman),
						friendlyHumanVerb = DescribeVerb(friendlyHumanVerb),
						playerMech = DescribePawn(playerMech),
						playerMechVerb = DescribeVerb(playerMechVerb),
						friendlyMech = DescribePawn(friendlyMech),
						friendlyMechVerb = DescribeVerb(friendlyMechVerb),
						playerTurret = DescribeTarget(playerTurret),
						playerTurretVerb = DescribeVerb(playerTurretVerb),
						enemyTurret = DescribeTarget(enemyTurret),
						enemyTurretVerb = DescribeVerb(enemyTurretVerb)
					},
					cases
				};
			}
			finally
			{
				ZombieSettings.Values.attackMode = oldAttackMode;
				ZombieSettings.Values.enemiesAttackZombies = oldEnemiesAttackZombies;
				ZombieSettings.Values.animalsAttackZombies = oldAnimalsAttackZombies;
			}
		}

		static object DescribeAvailableBranchCase(string label, string[] ids, bool success, params IAttackTarget[] targets)
		{
			return new
			{
				success,
				label,
				ids,
				contains = targets.ToDictionary(
					target => StableId(target),
					target => ids.Contains(StableId(target)))
			};
		}

		static bool ContainsTarget(string[] ids, IAttackTarget target)
		{
			return ids.Contains(StableId(target));
		}

		static bool SpawnAreaWorkflowTurretGun(
			Map map,
			IntVec3 root,
			Faction faction,
			List<Thing> spawnedThings,
			out Building_TurretGun turret,
			out object error)
		{
			turret = null;
			error = null;

			var turretDef = DefDatabase<ThingDef>.GetNamed("Turret_MiniTurret", false)
				?? DefDatabase<ThingDef>.AllDefs.FirstOrDefault(def => def.thingClass != null && typeof(Building_TurretGun).IsAssignableFrom(def.thingClass));
			if (turretDef == null)
			{
				error = new
				{
					success = false,
					error = "No Building_TurretGun ThingDef was available for non-pawn targeting checks."
				};
				return false;
			}

			if (TryFindClearBuildingFootprint(map, turretDef, root, 18f, out var turretCell, out error) == false)
				return false;

			var turretStuff = turretDef.MadeFromStuff ? GenStuff.DefaultStuffFor(turretDef) ?? ThingDefOf.Steel : null;
			turret = ThingMaker.MakeThing(turretDef, turretStuff) as Building_TurretGun;
			if (turret == null)
			{
				error = new
				{
					success = false,
					turretDef = turretDef.defName,
					error = "The selected turret def did not create a Building_TurretGun."
				};
				return false;
			}
			turret.SetFactionDirect(faction);
			GenSpawn.Spawn(turret, turretCell, map, Rot4.North, WipeMode.Vanish, false);
			spawnedThings.Add(turret);

			var power = turret.GetComp<CompPowerTrader>();
			if (power != null)
				power.PowerOn = true;
			return true;
		}

		static object VerifyAreaWorkflowTarSmokeMeleeTargeting(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			if (TryFindAdjacentPawnPairCells(map, root + new IntVec3(10, 0, 8), out var actorCell, out var targetCell, out var cellError) == false)
				return cellError;

			ClearGasAt(map, targetCell);
			var actor = SpawnMeleeAreaWorkflowPawn(map, "ZL_Area_TarSmokeMeleeActor", actorCell, Faction.OfPlayer, spawnedThings);
			EquipAreaWorkflowMeleeWeapon(actor);
			var target = SpawnAreaWorkflowPawn(map, "ZL_Area_TarSmokeMeleeTarget", targetCell, Faction.OfAncientsHostile, spawnedThings);
			var verb = actor.equipment?.PrimaryEq?.PrimaryVerb ?? actor.CurrentEffectiveVerb;
			if (verb == null)
			{
				return new
				{
					success = false,
					actor = DescribePawn(actor),
					target = DescribePawn(target),
					error = "The tar-smoke melee actor had no effective melee verb."
				};
			}

			var canHitBeforeSmoke = verb.CanHitTargetFrom(actor.Position, target);
			var gasAtTargetBefore = target.Position.GetGas(map)?.def?.defName;
			var smoke = GenSpawn.Spawn(ThingMaker.MakeThing(CustomDefs.TarSmoke), target.Position, map);
			if (smoke != null)
				spawnedThings.Add(smoke);
			var gasAtTargetAfter = target.Position.GetGas(map)?.def?.defName;
			var canHitAfterSmoke = verb.CanHitTargetFrom(actor.Position, target);

			return new
			{
				success = verb.IsMeleeAttack
					&& canHitBeforeSmoke
					&& gasAtTargetBefore == null
					&& smoke?.def == CustomDefs.TarSmoke
					&& gasAtTargetAfter == CustomDefs.TarSmoke.defName
					&& canHitAfterSmoke,
				actor = DescribePawn(actor),
				target = DescribePawn(target),
				verb = DescribeVerb(verb),
				actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
				targetCell = ZombieRuntimeActions.DescribeCell(targetCell),
				smoke = ZombieRuntimeActions.StableThingId(smoke),
				gasAtTargetBefore,
				gasAtTargetAfter,
				canHitBeforeSmoke,
				canHitAfterSmoke
			};
		}

		static object VerifyAreaWorkflowTarSmokeAimChance(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			if (TryFindClearSpawnCell(map, root + new IntVec3(-12, 0, 10), 16f, out var actorCell, out var actorError) == false)
				return actorError;
			var targetCell = GenRadial.RadialCellsAround(actorCell, 12f, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.Where(cell => cell.DistanceTo(actorCell) >= 6f)
				.Where(cell => GenSight.LineOfSight(actorCell, cell, map, true))
				.OrderBy(cell => cell.DistanceToSquared(actorCell))
				.FirstOrDefault();
			if (targetCell.IsValid == false)
			{
				return new
				{
					success = false,
					actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
					error = "No line-of-sight target cell was found for the tar-smoke aim-chance fixture."
				};
			}

			ClearGasAt(map, targetCell);
			var actor = SpawnArmedAreaWorkflowPawn(map, "ZL_Area_TarSmokeAimActor", actorCell, Faction.OfPlayer, spawnedThings);
			var target = SpawnAreaWorkflowPawn(map, "ZL_Area_TarSmokeAimTarget", targetCell, Faction.OfAncientsHostile, spawnedThings);
			var verb = actor?.equipment?.PrimaryEq?.PrimaryVerb;
			if (actor == null || target == null || verb == null)
			{
				return new
				{
					success = false,
					actor = DescribePawn(actor),
					target = DescribePawn(target),
					verb = DescribeVerb(verb),
					error = "Could not create the tar-smoke aim-chance fixture."
				};
			}

			var reportBeforeSmoke = ShotReport.HitReportFor(actor, verb, target);
			var aimChanceBeforeSmoke = reportBeforeSmoke.AimOnTargetChance_StandardTarget;
			var canHitBeforeSmoke = verb.CanHitTargetFrom(actor.Position, target);
			var gasAtTargetBefore = target.Position.GetGas(map)?.def?.defName;

			var syntheticCoverSmoke = ThingMaker.MakeThing(CustomDefs.TarSmoke);
			var reportWithSyntheticCover = reportBeforeSmoke;
			var boxedReport = (object)reportWithSyntheticCover;
			AccessTools.Field(typeof(ShotReport), "covers")?.SetValue(boxedReport, new List<CoverInfo> { new CoverInfo(syntheticCoverSmoke, 1f) });
			reportWithSyntheticCover = (ShotReport)boxedReport;
			var aimChanceWithSyntheticCoverSmoke = reportWithSyntheticCover.AimOnTargetChance_StandardTarget;

			var smoke = GenSpawn.Spawn(ThingMaker.MakeThing(CustomDefs.TarSmoke), target.Position, map);
			if (smoke != null)
				spawnedThings.Add(smoke);
			var gasAtTargetAfter = target.Position.GetGas(map)?.def?.defName;
			var canHitAfterSmoke = verb.CanHitTargetFrom(actor.Position, target);
			var aimChanceAfterTargetSmoke = ShotReport.HitReportFor(actor, verb, target).AimOnTargetChance_StandardTarget;

			return new
			{
				success = canHitBeforeSmoke
					&& aimChanceBeforeSmoke > 0f
					&& gasAtTargetBefore == null
					&& syntheticCoverSmoke?.def == CustomDefs.TarSmoke
					&& aimChanceWithSyntheticCoverSmoke == 0f
					&& smoke?.def == CustomDefs.TarSmoke
					&& gasAtTargetAfter == CustomDefs.TarSmoke.defName
					&& canHitAfterSmoke == false
					&& aimChanceAfterTargetSmoke == 0f,
				actor = DescribePawn(actor),
				target = DescribePawn(target),
				verb = DescribeVerb(verb),
				actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
				targetCell = ZombieRuntimeActions.DescribeCell(targetCell),
				smoke = ZombieRuntimeActions.StableThingId(smoke),
				canHitBeforeSmoke,
				canHitAfterSmoke,
				aimChanceBeforeSmoke,
				aimChanceWithSyntheticCoverSmoke,
				aimChanceAfterTargetSmoke,
				gasAtTargetBefore,
				gasAtTargetAfter,
				coverBranchMode = "synthetic ShotReport.covers TarSmoke entry"
			};
		}

		static object VerifyAreaWorkflowTurretTargeting(Map map, IntVec3 root, Pawn roper, List<Thing> spawnedThings)
		{
			if (SpawnAreaWorkflowTurretGun(map, root + new IntVec3(0, 0, 12), Faction.OfPlayer, spawnedThings, out var turret, out var turretError) == false)
				return turretError;

			var verb = turret.CurrentEffectiveVerb;
			if (verb == null)
			{
				return new
				{
					success = false,
					turret = DescribeTarget(turret),
					error = "The spawned turret had no CurrentEffectiveVerb."
				};
			}

			var maxDistance = Math.Min(verb.verbProps.range - 1f, 18f);
			var targetCells = GenRadial.RadialCellsAround(turret.Position, maxDistance, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.Where(cell => cell.GetEdifice(map) == null)
				.Where(cell => cell.GetFirstThing<Mineable>(map) == null)
				.Where(cell => cell.DistanceTo(turret.Position) >= 6f)
				.Where(cell => GenSight.LineOfSight(turret.Position, cell, map, true))
				.Where(cell => verb.CanHitTargetFrom(turret.Position, cell))
				.OrderBy(cell => cell.DistanceToSquared(turret.Position))
				.Take(6)
				.ToArray();
			if (targetCells.Length < 6)
			{
				return new
				{
					success = false,
					turret = DescribeTarget(turret),
					verb = DescribeVerb(verb),
					targetCellCount = targetCells.Length,
					error = "Not enough turret-visible target cells were found."
				};
			}

			var normal = SpawnTargetZombie(map, targetCells[0], ZombieType.Normal, "ZL_Area_TurretNormal", spawnedThings);
			var roped = SpawnTargetZombie(map, targetCells[1], ZombieType.Normal, "ZL_Area_TurretRoped", spawnedThings);
			var confused = SpawnTargetZombie(map, targetCells[2], ZombieType.Normal, "ZL_Area_TurretConfused", spawnedThings);
			var electric = SpawnTargetZombie(map, targetCells[3], ZombieType.Electrifier, "ZL_Area_TurretElectric", spawnedThings);
			var spitter = SpawnTargetSpitter(map, targetCells[4], "ZL_Area_TurretSpitter", spawnedThings);
			var symbiant = SpawnTargetSymbiant(map, targetCells[5], "ZL_Area_TurretSymbiant", spawnedThings);
			if (new Pawn[] { normal, roped, confused, electric, spitter, symbiant }.Any(pawn => pawn == null))
			{
				return new
				{
					success = false,
					turret = DescribeTarget(turret),
					targets = new
					{
						normal = DescribeZombie(normal),
						roped = DescribeZombie(roped),
						confused = DescribeZombie(confused),
						electric = DescribeZombie(electric),
						spitter = DescribeZombie(spitter),
						symbiant = DescribeZombie(symbiant)
					},
					error = "Could not create all turret-targeting zombie fixtures."
				};
			}

			roped.ropedBy = roper;
			confused.paralyzedUntil = GenTicks.TicksAbs + 2500;
			electric.electricDisabledUntil = GenTicks.TicksGame - 1;
			RefreshZombieTargetCache(map);

			var flags = TargetScanFlags.NeedLOSToAll | TargetScanFlags.NeedThreat;
			var normalBest = BestSpecificTarget(turret, normal);
			var ropedBest = BestSpecificTarget(turret, roped);
			var confusedBest = BestSpecificTarget(turret, confused);
			var electricBest = BestSpecificTarget(turret, electric);
			var spitterBest = BestSpecificTarget(turret, spitter);
			var symbiantBest = BestSpecificTarget(turret, symbiant);
			var generalBest = AttackTargetFinder.BestAttackTarget(
				turret,
				flags,
				thing => thing is Zombie || thing is ZombieSpitter || thing is ZombieSymbiant,
				0f,
				Math.Max(20f, verb.verbProps.range));
			var rejectedIds = new[] { StableId(roped), StableId(confused), StableId(electric) };
			var generalBestThing = generalBest?.Thing;
			var generalBestAllowed = generalBestThing switch
			{
				Zombie zombie => zombie.IsRopedOrConfused == false && (verb.CanHarmElectricZombies() || zombie.IsActiveElectric == false),
				ZombieSpitter => true,
				ZombieSymbiant _ => true,
				_ => false
			};
			var potentialIds = map.attackTargetsCache.GetPotentialTargetsFor(turret).Select(StableId).Where(id => id != null).ToArray();
			var patchOwners = PatchOwners("BestAttackTarget");

			return new
			{
				success = patchOwners.Contains("net.pardeike.zombieland")
					&& verb.CanHarmElectricZombies() == false
					&& electric.IsActiveElectric
					&& ReferenceEquals(normalBest?.Thing, normal)
					&& ropedBest == null
					&& confusedBest == null
					&& electricBest == null
					&& ReferenceEquals(spitterBest?.Thing, spitter)
					&& ReferenceEquals(symbiantBest?.Thing, symbiant)
					&& generalBestAllowed
					&& rejectedIds.Contains(StableId(generalBest)) == false,
				patchOwners,
				turret = DescribeTarget(turret),
				verb = DescribeVerb(verb),
				canHarmElectric = verb.CanHarmElectricZombies(),
				targets = new
				{
					normal = DescribeZombie(normal),
					roped = DescribeZombie(roped),
					confused = DescribeZombie(confused),
					electric = DescribeZombie(electric),
					spitter = DescribeZombie(spitter),
					symbiant = DescribeZombie(symbiant)
				},
				bestTargets = new
				{
					normal = DescribeTarget(normalBest),
					roped = DescribeTarget(ropedBest),
					confused = DescribeTarget(confusedBest),
					electric = DescribeTarget(electricBest),
					spitter = DescribeTarget(spitterBest),
					symbiant = DescribeTarget(symbiantBest),
					general = DescribeTarget(generalBest),
					generalAllowed = generalBestAllowed
				},
				potentialTargetIds = potentialIds
			};
		}

		static object VerifyAreaWorkflowWaitAutoAttack(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			var patchOwners = PatchOwners(typeof(JobDriver_Wait), "CheckForAutoAttack");
			var cases = new List<object>();
			var caseIndex = 0;

			object RunCase(string label, bool expectDamage, Func<IntVec3, Pawn, Pawn> spawnTarget, Action<Pawn, Pawn> configure = null, bool requireDamage = true, Action<Pawn, Pawn, int> maintain = null)
			{
				caseIndex++;
				if (TryFindAdjacentPawnPairCells(map, root + new IntVec3(caseIndex * 5, 0, 5), out var actorCell, out var targetCell, out var cellError) == false)
				{
					return new
					{
						success = false,
						label,
						error = cellError
					};
				}

				var actor = SpawnMeleeAreaWorkflowPawn(map, "ZL_Area_WaitActor_" + label, actorCell, Faction.OfPlayer, spawnedThings);
				EquipAreaWorkflowMeleeWeapon(actor);
				var target = spawnTarget(targetCell, actor);
				if (target != null)
					spawnedThings.Add(target);
				if (actor == null || target == null)
				{
					return new
					{
						success = false,
						label,
						actor = DescribePawn(actor),
						target = DescribeTarget(target),
						error = "Could not create the Wait_Combat auto-attack fixture."
					};
				}

				actor.drafter.Drafted = true;
				configure?.Invoke(actor, target);
				RefreshZombieTargetCache(map);
				var injuryBefore = TotalInjurySeverity(target);
				var deadBefore = target.Dead;
				var samples = new List<object>();
				var sampledDamage = false;
				var attacked = false;

				var waitJob = JobMaker.MakeJob(JobDefOf.Wait_Combat);
				waitJob.playerForced = false;
				waitJob.canUseRangedWeapon = false;
				actor.jobs.StartJob(waitJob, JobCondition.InterruptForced, null, false, true);

				const int maxTicks = 900;
				for (var tick = 1; tick <= maxTicks && actor.Destroyed == false && target.Destroyed == false; tick++)
				{
					maintain?.Invoke(actor, target, tick);
					AdvanceGameTicks(1);
					maintain?.Invoke(actor, target, tick);
					var actorStance = actor.stances?.curStance?.GetType().Name;
					var targetInjury = TotalInjurySeverity(target);
					var targetDamaged = targetInjury > injuryBefore || target.Dead || target.Downed;
					var startedAttack = actor.CurJobDef == JobDefOf.AttackMelee || targetDamaged;
					attacked |= startedAttack;
					if (tick == 1 || tick == 60 || tick == 180 || tick == maxTicks || targetDamaged)
					{
						var zombieTarget = target as Zombie;
						if (targetDamaged == false || sampledDamage == false)
						{
							samples.Add(new
							{
								tick,
								actorJob = actor.CurJobDef?.defName,
								actorStance,
								startedAttack,
								targetInjury,
								targetDead = target.Dead,
								targetRopedBy = zombieTarget?.ropedBy?.ThingID,
								targetIsRopedOrConfused = zombieTarget?.IsRopedOrConfused,
								targetParalyzedUntilDelta = zombieTarget == null ? (int?)null : zombieTarget.paralyzedUntil - GenTicks.TicksAbs
							});
							if (targetDamaged)
								sampledDamage = true;
						}
					}
					if (expectDamage && targetDamaged)
						break;
					if (expectDamage && requireDamage == false && attacked)
						break;
				}

				var injuryAfter = TotalInjurySeverity(target);
				var damaged = injuryAfter > injuryBefore || (deadBefore == false && target.Dead) || target.Downed;
				var success = expectDamage
					? requireDamage ? damaged : damaged || attacked
					: damaged == false && attacked == false;
				var result = new
				{
					success,
					label,
					expectDamage,
					requireDamage,
					damaged,
					attacked,
					actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
					targetCell = ZombieRuntimeActions.DescribeCell(targetCell),
					injuryBefore,
					injuryAfter,
					injuryDelta = injuryAfter - injuryBefore,
					deadBefore,
					deadAfter = target.Dead,
					downedAfter = target.Downed,
					actor = DescribePawn(actor),
					target = DescribeTarget(target),
					samples = samples.ToArray()
				};

				if (actor.Destroyed == false)
					actor.jobs?.EndCurrentJob(JobCondition.InterruptForced);
				if (actor.Destroyed == false)
					actor.Destroy(DestroyMode.Vanish);
				if (target.Destroyed == false)
					target.Destroy(DestroyMode.Vanish);
				return result;
			}

			cases.Add(RunCase("normalZombie", true, (cell, actor) => SpawnTargetZombie(map, cell, ZombieType.Normal, "ZL_Area_WaitNormal", spawnedThings)));
			cases.Add(RunCase("ropedZombie", false, (cell, actor) => SpawnTargetZombie(map, cell, ZombieType.Normal, "ZL_Area_WaitRoped", spawnedThings), (actor, target) =>
			{
				if (target is Zombie zombie)
					zombie.ropedBy = actor;
			}, maintain: (actor, target, tick) =>
			{
				if (target is Zombie zombie)
					zombie.ropedBy = actor;
			}));
			cases.Add(RunCase("confusedZombie", false, (cell, actor) => SpawnTargetZombie(map, cell, ZombieType.Normal, "ZL_Area_WaitConfused", spawnedThings), (actor, target) =>
			{
				if (target is Zombie zombie)
					zombie.paralyzedUntil = GenTicks.TicksAbs + 2500;
			}));
			cases.Add(RunCase("spitter", true, (cell, actor) => SpawnTargetSpitter(map, cell, "ZL_Area_WaitSpitter", spawnedThings)));
			cases.Add(RunCase("symbiant", true, (cell, actor) => SpawnTargetSymbiant(map, cell, "ZL_Area_WaitSymbiant", spawnedThings), requireDamage: false));
			cases.Add(RunCase("hostilePawn", true, (cell, actor) => SpawnMeleeAreaWorkflowPawn(map, "ZL_Area_WaitHostile", cell, Faction.OfAncientsHostile, spawnedThings), requireDamage: false));

			return new
			{
				success = patchOwners.Contains("net.pardeike.zombieland") && cases.All(ObjectSuccess),
				patchOwners,
				cases = cases.ToArray()
			};
		}

		static object VerifyAreaWorkflowDownedCombat(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			var settingsSnapshot = SnapshotZombieSettings();
			try
			{
				ApplyZombieSettingsOverride(settings => settings.doubleTapRequired = false);
				var killIncappedTargets = PatchedMethodsForPatchClass("Toils_Combat_FollowAndMeleeAttack_KillIncappedTarget_Patch");
				var downedReplacementTargets = PatchedMethodsForPatchClass("JobDriver_AttackStatic_TickAction_Patch");
				var meleeCase = VerifyDownedMeleeAttack(map, root + new IntVec3(0, 0, 13), spawnedThings);
				var attackStaticCase = VerifyDownedAttackStatic(map, root + new IntVec3(12, 0, 13), spawnedThings);

				return new
				{
					success = killIncappedTargets.Length > 0
						&& downedReplacementTargets.Length > 0
						&& ObjectSuccess(meleeCase)
						&& ObjectSuccess(attackStaticCase),
					doubleTapRequired = ZombieSettings.Values.doubleTapRequired,
					patchTargets = new
					{
						killIncapped = killIncappedTargets,
						downedReplacement = downedReplacementTargets
					},
					meleeCase,
					attackStaticCase
				};
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
			}
		}

		static object VerifyAreaWorkflowDangerAndFlee(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			var settingsSnapshot = SnapshotZombieSettings();
			var dangerRatingPatchTargets = PatchedMethodsForPatchClass("DangerWatcher_CalculateDangerRating_Patch");
			var fleePatchTargets = PatchedMethodsForPatchClass("FleeUtility_ShouldFleeFrom_Patch");
			var originalHome = new Dictionary<IntVec3, bool>();

			void SetHome(IntVec3 cell, bool value)
			{
				if (cell.IsValid == false || cell.InBounds(map) == false)
					return;
				if (originalHome.ContainsKey(cell) == false)
					originalHome[cell] = map.areaManager.Home[cell];
				map.areaManager.Home[cell] = value;
			}

			bool InvokeDanger(Thing thing)
			{
				return StorytellerEventFilters.AffectsStoryDanger(thing);
			}

			try
			{
				ApplyZombieSettingsOverride(settings =>
				{
					settings.betterZombieAvoidance = true;
					settings.attackMode = AttackMode.Everything;
					settings.enemiesAttackZombies = true;
					settings.animalsAttackZombies = true;
					settings.doubleTapRequired = false;
				});

				if (TryFindClearSpawnCell(map, root, 18f, out var actorCell, out var actorError) == false)
					return actorError;

				var targetCells = GenRadial.RadialCellsAround(actorCell, 10f, false)
					.Where(cell => cell.InBounds(map))
					.Where(cell => cell.Standable(map))
					.Where(cell => cell.Fogged(map) == false)
					.Where(cell => cell != actorCell)
					.Where(cell => cell.GetFirstPawn(map) == null)
					.OrderBy(cell => cell.DistanceToSquared(actorCell))
					.Take(9)
					.ToArray();
				if (targetCells.Length < 9)
				{
					return new
					{
						success = false,
						actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
						foundTargetCells = targetCells.Length,
						error = "Could not find enough nearby cells for danger/flee patch fixtures."
					};
				}

				var actor = SpawnMeleeAreaWorkflowPawn(map, "ZL_Area_DangerFleeActor", actorCell, Faction.OfPlayer, spawnedThings);
				var normal = SpawnTargetZombie(map, targetCells[0], ZombieType.Normal, "ZL_Area_DangerFleeNormal", spawnedThings);
				var outside = SpawnTargetZombie(map, targetCells[1], ZombieType.Normal, "ZL_Area_DangerFleeOutside", spawnedThings);
				var roped = SpawnTargetZombie(map, targetCells[2], ZombieType.Normal, "ZL_Area_DangerFleeRoped", spawnedThings);
				var confused = SpawnTargetZombie(map, targetCells[3], ZombieType.Normal, "ZL_Area_DangerFleeConfused", spawnedThings);
				var downed = SpawnTargetZombie(map, targetCells[4], ZombieType.Normal, "ZL_Area_DangerFleeDowned", spawnedThings);
				var unspawned = SpawnTargetZombie(map, targetCells[5], ZombieType.Normal, "ZL_Area_DangerFleeUnspawned", spawnedThings);
				var electric = SpawnTargetZombie(map, targetCells[6], ZombieType.Electrifier, "ZL_Area_DangerFleeElectric", spawnedThings);
				var albino = SpawnTargetZombie(map, targetCells[7], ZombieType.Albino, "ZL_Area_DangerFleeAlbino", spawnedThings);
				var hostile = SpawnMeleeAreaWorkflowPawn(map, "ZL_Area_DangerFleeHostile", targetCells[8], Faction.OfAncientsHostile, spawnedThings);

				if (actor == null || normal == null || outside == null || roped == null || confused == null || downed == null || unspawned == null || electric == null || albino == null || hostile == null)
				{
					return new
					{
						success = false,
						actor = DescribePawn(actor),
						normal = DescribeZombie(normal),
						outside = DescribeZombie(outside),
						roped = DescribeZombie(roped),
						confused = DescribeZombie(confused),
						downed = DescribeZombie(downed),
						unspawned = DescribeZombie(unspawned),
						electric = DescribeZombie(electric),
						albino = DescribeZombie(albino),
						hostile = DescribePawn(hostile),
						error = "Could not create all danger/flee patch fixtures."
					};
				}

				roped.ropedBy = actor;
				confused.paralyzedUntil = GenTicks.TicksAbs + 2500;
				electric.electricDisabledUntil = GenTicks.TicksGame - 1;
				if (TryMakeDownedForCombat(downed, out var downedError) == false)
				{
					return new
					{
						success = false,
						downed = DescribeZombie(downed),
						error = downedError
					};
				}

				var unspawnedCell = unspawned.Position;
				unspawned.DeSpawn(DestroyMode.Vanish);

				SetHome(normal.Position, true);
				SetHome(outside.Position, false);
				SetHome(roped.Position, true);
				SetHome(downed.Position, true);
				SetHome(unspawnedCell, true);

				var dangerHome = InvokeDanger(normal);
				var dangerOutside = InvokeDanger(outside);
				var dangerRoped = InvokeDanger(roped);
				var dangerDowned = InvokeDanger(downed);
				var dangerUnspawned = InvokeDanger(unspawned);

				var normalThreat = FleeUtility.ShouldFleeFrom(normal, actor, true, false);
				var ropedThreat = FleeUtility.ShouldFleeFrom(roped, actor, true, false);
				var confusedThreat = FleeUtility.ShouldFleeFrom(confused, actor, true, false);
				var electricThreat = FleeUtility.ShouldFleeFrom(electric, actor, true, false);
				var albinoThreat = FleeUtility.ShouldFleeFrom(albino, actor, true, false);
				var hostileThreat = FleeUtility.ShouldFleeFrom(hostile, actor, true, false);

				return new
				{
					success = dangerRatingPatchTargets.Length > 0
						&& fleePatchTargets.Length > 0
						&& dangerHome
						&& dangerOutside == false
						&& dangerRoped == false
						&& dangerDowned == false
						&& dangerUnspawned == false
						&& normalThreat
						&& ropedThreat == false
						&& confusedThreat == false
						&& electricThreat == false
						&& albinoThreat == false
						&& hostileThreat,
					patchTargets = new
					{
						dangerRating = dangerRatingPatchTargets,
						flee = fleePatchTargets
					},
					actor = DescribePawn(actor),
					danger = new
					{
						home = DescribeDangerCase("home", normal, dangerHome),
						outside = DescribeDangerCase("outsideHome", outside, dangerOutside),
						roped = DescribeDangerCase("roped", roped, dangerRoped),
						downed = DescribeDangerCase("downed", downed, dangerDowned),
						unspawned = new
						{
							label = "unspawned",
							result = dangerUnspawned,
							spawned = unspawned.Spawned,
							destroyed = unspawned.Destroyed,
							position = ZombieRuntimeActions.DescribeCell(unspawnedCell)
						}
					},
					flee = new
					{
						normal = DescribeFleeCase("normal", normal, actor, normalThreat),
						roped = DescribeFleeCase("roped", roped, actor, ropedThreat),
						confused = DescribeFleeCase("confused", confused, actor, confusedThreat),
						electric = DescribeFleeCase("electric", electric, actor, electricThreat),
						albino = DescribeFleeCase("albino", albino, actor, albinoThreat),
						hostile = new
						{
							label = "hostilePawn",
							result = hostileThreat,
							hostileToActor = hostile.HostileTo(actor),
							target = DescribePawn(hostile)
						}
					},
					settings = new
					{
						ZombieSettings.Values.betterZombieAvoidance,
						ZombieSettings.Values.attackMode,
						ZombieSettings.Values.enemiesAttackZombies,
						ZombieSettings.Values.animalsAttackZombies,
						ZombieSettings.Values.doubleTapRequired
					}
				};
			}
			finally
			{
				foreach (var pair in originalHome)
					map.areaManager.Home[pair.Key] = pair.Value;
				RestoreZombieSettings(settingsSnapshot);
			}
		}

		static object DescribeDangerCase(string label, Zombie zombie, bool result)
		{
			return new
			{
				label,
				result,
				home = zombie?.Spawned == true && zombie.Map?.areaManager?.Home[zombie.Position] == true,
				zombie = DescribeZombie(zombie)
			};
		}

		static object DescribeFleeCase(string label, Zombie zombie, Pawn actor, bool result)
		{
			return new
			{
				label,
				result,
				seesAsThreat = actor.SeesZombieAsThreat(zombie),
				hostileToActor = zombie.HostileTo(actor),
				zombie = DescribeZombie(zombie)
			};
		}

		static object VerifyAreaWorkflowSmartMelee(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			var settingsSnapshot = SnapshotZombieSettings();
			var meleePatchTargets = PatchedMethodsForPatchClass("Verb_MeleeAttack_TryCastShot_Patch");
			var scratchHitPartTargets = PatchedMethodsForPatchClass("DamageWorker_Scratch_ChooseHitPart_Patch");
			var biteHitPartTargets = PatchedMethodsForPatchClass("DamageWorker_Bite_ChooseHitPart_Patch");
			try
			{
				ApplyZombieSettingsOverride(settings =>
				{
					settings.safeMeleeLimit = 1;
					settings.doubleTapRequired = false;
				});

				var smartBlock = VerifySmartMeleeBiteBlock(map, root, spawnedThings);
				var chainsawSuppression = VerifySmartMeleeChainsawSuppression(map, root + new IntVec3(8, 0, 0), spawnedThings);
				var ropedKill = VerifySmartMeleeRopedKill(map, root + new IntVec3(16, 0, 0), spawnedThings);
				var ordinaryFallback = VerifySmartMeleeOrdinaryFallback(map, root + new IntVec3(24, 0, 0), spawnedThings);
				var downedHitParts = VerifySmartMeleeDownedHitParts(map, root + new IntVec3(32, 0, 0), spawnedThings);

				return new
				{
					success = meleePatchTargets.Length > 0
						&& scratchHitPartTargets.Length > 0
						&& biteHitPartTargets.Length > 0
						&& ObjectSuccess(smartBlock)
						&& ObjectSuccess(chainsawSuppression)
						&& ObjectSuccess(ropedKill)
						&& ObjectSuccess(ordinaryFallback)
						&& ObjectSuccess(downedHitParts),
					patchTargets = new
					{
						melee = meleePatchTargets,
						scratchHitPart = scratchHitPartTargets,
						biteHitPart = biteHitPartTargets
					},
					settings = new
					{
						ZombieSettings.Values.safeMeleeLimit,
						ZombieSettings.Values.doubleTapRequired
					},
					smartBlock,
					chainsawSuppression,
					ropedKill,
					ordinaryFallback,
					downedHitParts
				};
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
			}
		}

		static object VerifySmartMeleeDownedHitParts(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			if (TryFindAdjacentPawnPairCells(map, root, out var downedCell, out var targetCell, out var cellError) == false)
				return cellError;
			if (TryFindClearSpawnCell(map, root + new IntVec3(4, 0, 0), 8f, out var standingCell, out var standingError) == false)
				return standingError;

			var downedZombie = SpawnTargetZombie(map, downedCell, ZombieType.Normal, "ZL_Area_SmartMeleeDownedHitPart", spawnedThings);
			var standingZombie = SpawnTargetZombie(map, standingCell, ZombieType.Normal, "ZL_Area_SmartMeleeStandingHitPart", spawnedThings);
			var target = SpawnMeleeAreaWorkflowPawn(map, "ZL_Area_SmartMeleeHitPartTarget", targetCell, Faction.OfPlayer, spawnedThings);
			if (downedZombie == null || standingZombie == null || target == null)
			{
				return new
				{
					success = false,
					downedZombie = DescribeZombie(downedZombie),
					standingZombie = DescribeZombie(standingZombie),
					target = DescribePawn(target),
					error = "Could not create downed hit-part fixtures."
				};
			}
			if (TryMakeDownedForCombat(downedZombie, out var downedError) == false)
			{
				return new
				{
					success = false,
					downedZombie = DescribeZombie(downedZombie),
					error = downedError
				};
			}

			var scratchDef = DefDatabase<DamageDef>.GetNamed("Scratch", false);
			var biteDef = DefDatabase<DamageDef>.GetNamed("Bite", false) ?? CustomDefs.ZombieBite;
			var scratch = InvokeDownedChooseHitPartProbe("scratch", typeof(DamageWorker_Scratch), "DamageWorker_Scratch_ChooseHitPart_Patch", scratchDef, downedZombie, standingZombie, target, 7101);
			var bite = InvokeDownedChooseHitPartProbe("bite", typeof(DamageWorker_Bite), "DamageWorker_Bite_ChooseHitPart_Patch", biteDef, downedZombie, standingZombie, target, 7102);

			return new
			{
				success = ObjectSuccess(scratch)
					&& ObjectSuccess(bite),
				downedZombie = DescribeZombie(downedZombie),
				standingZombie = DescribeZombie(standingZombie),
				target = DescribePawn(target),
				scratch,
				bite
			};
		}

		static object InvokeDownedChooseHitPartProbe(string label, Type damageWorkerType, string patchClassName, DamageDef damageDef, Zombie downedZombie, Zombie standingZombie, Pawn target, int seed)
		{
			var chooseHitPart = AccessTools.Method(damageWorkerType, "ChooseHitPart");
			var prefix = FindNestedPatchMethod(patchClassName, "Prefix");
			if (chooseHitPart == null || prefix == null || damageDef == null)
			{
				return new
				{
					success = false,
					label,
					damageWorkerType = damageWorkerType?.Name,
					damageDef = damageDef?.defName,
					reflection = new
					{
						chooseHitPart = chooseHitPart != null,
						prefix = prefix != null
					},
					error = "Could not reflect downed choose-hit-part members."
				};
			}

			var standingInfo = new DamageInfo(damageDef, 1f, 0f, -1f, standingZombie, null, null, DamageInfo.SourceCategory.ThingOrUnknown, null, true, true);
			var standingArgs = new object[] { standingInfo };
			prefix.Invoke(null, standingArgs);
			standingInfo = (DamageInfo)standingArgs[0];

			var downedInfo = new DamageInfo(damageDef, 1f, 0f, -1f, downedZombie, null, null, DamageInfo.SourceCategory.ThingOrUnknown, null, true, true);
			BodyPartRecord part;
			Rand.PushState(seed);
			try
			{
				var worker = Activator.CreateInstance(damageWorkerType, true);
				part = chooseHitPart.Invoke(worker, new object[] { downedInfo, target }) as BodyPartRecord;
			}
			finally
			{
				Rand.PopState();
			}

			return new
			{
				success = standingInfo.Height == BodyPartHeight.Undefined
					&& part != null
					&& part.height == BodyPartHeight.Bottom
					&& part.depth == BodyPartDepth.Outside,
				label,
				damageWorkerType = damageWorkerType.Name,
				damageDef = damageDef.defName,
				seed,
				standingPrefix = new
				{
					heightAfterPrefix = standingInfo.Height.ToString()
				},
				downed = new
				{
					zombie = DescribeZombie(downedZombie),
					part = DescribeBodyPartDetail(part),
					height = part?.height.ToString(),
					depth = part?.depth.ToString()
				}
			};
		}

		static object VerifySmartMeleeBiteBlock(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			if (TryFindAdjacentPawnPairCells(map, root, out var zombieCell, out var targetCell, out var cellError) == false)
				return cellError;

			var zombie = SpawnTargetZombie(map, zombieCell, ZombieType.Normal, "ZL_Area_SmartMeleeZombie", spawnedThings);
			var target = SpawnMeleeAreaWorkflowPawn(map, "ZL_Area_SmartMeleeDefender", targetCell, Faction.OfPlayer, spawnedThings);
			EquipAreaWorkflowMeleeWeapon(target);
			target.skills?.GetSkill(SkillDefOf.Melee).Notify_SkillDisablesChanged();
			if (target.skills != null)
				target.skills.GetSkill(SkillDefOf.Melee).Level = 20;
			_ = target.meleeVerbs?.TryGetMeleeVerb(zombie);

			var biteVerb = zombie?.meleeVerbs?.GetUpdatedAvailableVerbsList(false)
				.Select(entry => entry.verb)
				.FirstOrDefault(verb => verb.GetDamageDef() == CustomDefs.ZombieBite);
			if (zombie == null || target == null || biteVerb == null)
			{
				return new
				{
					success = false,
					zombie = DescribeZombie(zombie),
					target = DescribePawn(target),
					biteVerbFound = biteVerb != null,
					error = "Could not create a smart melee bite-block fixture."
				};
			}

			var injuryBefore = TotalInjurySeverity(target);
			var blockMotesBefore = map.listerThings.AllThings.Count(thing => thing.def == CustomDefs.Mote_Block);
			var cast = InvokePatchedMeleeTryCastShot(biteVerb, target);
			var injuryAfter = TotalInjurySeverity(target);
			var blockMotesAfter = map.listerThings.AllThings.Count(thing => thing.def == CustomDefs.Mote_Block);

			return new
			{
				success = ObjectSuccess(cast)
					&& injuryAfter == injuryBefore
					&& target.Dead == false
					&& target.Downed == false,
				zombie = DescribeZombie(zombie),
				target = DescribePawn(target),
				biteVerb = DescribeVerb(biteVerb),
				targetMeleeLevel = target.skills?.GetSkill(SkillDefOf.Melee)?.Level,
				targetCurrentMeleeVerb = DescribeVerb(target.meleeVerbs?.curMeleeVerb),
				injuryBefore,
				injuryAfter,
				injuryDelta = injuryAfter - injuryBefore,
				blockMotesBefore,
				blockMotesAfter,
				cast
			};
		}

		static object VerifySmartMeleeChainsawSuppression(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			if (TryFindAdjacentPawnPairCells(map, root, out var actorCell, out var targetCell, out var cellError) == false)
				return cellError;

			var actor = SpawnMeleeAreaWorkflowPawn(map, "ZL_Area_SmartMeleeChainsawActor", actorCell, Faction.OfPlayer, spawnedThings);
			var zombie = SpawnTargetZombie(map, targetCell, ZombieType.Normal, "ZL_Area_SmartMeleeChainsawTarget", spawnedThings);
			if (TryEquipAreaWorkflowChainsaw(map, actor, actorCell, spawnedThings, out var chainsaw, out var chainsawError) == false)
				return chainsawError;

			var verb = actor.equipment?.PrimaryEq?.PrimaryVerb;
			var injuryBefore = TotalInjurySeverity(zombie);
			var cast = InvokePatchedMeleeTryCastShot(verb, zombie);
			var injuryAfter = TotalInjurySeverity(zombie);

			return new
			{
				success = actor.equipment?.Primary is Chainsaw
					&& verb is Verb_MeleeAttack
					&& ObjectSuccess(cast)
					&& injuryAfter == injuryBefore
					&& zombie.Dead == false,
				actor = DescribePawn(actor),
				zombie = DescribeZombie(zombie),
				chainsaw = DescribeTarget(chainsaw as IAttackTarget),
				verb = DescribeVerb(verb),
				cast,
				injuryBefore,
				injuryAfter,
				injuryDelta = injuryAfter - injuryBefore
			};
		}

		static object VerifySmartMeleeRopedKill(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			if (TryFindAdjacentPawnPairCells(map, root, out var actorCell, out var targetCell, out var cellError) == false)
				return cellError;

			var actor = SpawnMeleeAreaWorkflowPawn(map, "ZL_Area_SmartMeleeRopedActor", actorCell, Faction.OfPlayer, spawnedThings);
			EquipAreaWorkflowMeleeWeapon(actor);
			var zombie = SpawnTargetZombie(map, targetCell, ZombieType.Normal, "ZL_Area_SmartMeleeRopedTarget", spawnedThings);
			zombie.ropedBy = actor;
			var verb = actor.equipment?.PrimaryEq?.PrimaryVerb ?? actor.CurrentEffectiveVerb;
			var deadBefore = zombie.Dead;
			var ropedOrConfusedBefore = zombie.IsRopedOrConfused;
			var cast = InvokePatchedMeleeTryCastShot(verb, zombie);
			var deadAfter = zombie.Dead;

			return new
			{
				success = verb is Verb_MeleeAttack
					&& ropedOrConfusedBefore
					&& deadBefore == false
					&& deadAfter
					&& ObjectSuccess(cast),
				actor = DescribePawn(actor),
				zombie = DescribeZombie(zombie),
				verb = DescribeVerb(verb),
				ropedByBefore = ZombieRuntimeActions.StableThingId(actor),
				ropedOrConfusedBefore,
				deadBefore,
				deadAfter,
				cast
			};
		}

		static object VerifySmartMeleeOrdinaryFallback(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			if (TryFindAdjacentPawnPairCells(map, root, out var actorCell, out var targetCell, out var cellError) == false)
				return cellError;

			var actor = SpawnMeleeAreaWorkflowPawn(map, "ZL_Area_SmartMeleeOrdinaryActor", actorCell, Faction.OfPlayer, spawnedThings);
			EquipAreaWorkflowMeleeWeapon(actor);
			var zombie = SpawnTargetZombie(map, targetCell, ZombieType.Normal, "ZL_Area_SmartMeleeOrdinaryTarget", spawnedThings);
			var verb = actor.equipment?.PrimaryEq?.PrimaryVerb ?? actor.CurrentEffectiveVerb;
			var prefixProbe = ProbeSmartMeleePrefix(verb);
			var tryMeleeResult = actor.meleeVerbs?.TryMeleeAttack(zombie, verb, false);

			return new
			{
				success = verb is Verb_MeleeAttack
					&& ObjectSuccess(prefixProbe)
					&& tryMeleeResult == true
					&& zombie.Dead == false,
				actor = DescribePawn(actor),
				zombie = DescribeZombie(zombie),
				verb = DescribeVerb(verb),
				prefixProbe,
				tryMeleeResult
			};
		}

		static object InvokePatchedMeleeTryCastShot(Verb verb, LocalTargetInfo target)
		{
			var tryCastShot = AccessTools.Method(typeof(Verb_MeleeAttack), "TryCastShot");
			if (tryCastShot == null || verb is not Verb_MeleeAttack)
			{
				return new
				{
					success = false,
					tryCastShotFound = tryCastShot != null,
					verb = DescribeVerb(verb),
					error = "Could not invoke Verb_MeleeAttack.TryCastShot for the melee patch probe."
				};
			}
			try
			{
				verb.currentTarget = target;
				var result = (bool)tryCastShot.Invoke(verb, Array.Empty<object>());
				return new
				{
					success = result == false,
					result,
					verb = DescribeVerb(verb),
					target = DescribeTarget(target.Thing as IAttackTarget)
				};
			}
			catch (Exception ex)
			{
				return new
				{
					success = false,
					error = ex.GetBaseException().Message,
					verb = DescribeVerb(verb)
				};
			}
		}

		static object ProbeSmartMeleePrefix(Verb verb)
		{
			var prefix = FindNestedPatchMethod("Verb_MeleeAttack_TryCastShot_Patch", "Prefix");
			if (prefix == null || verb == null)
			{
				return new
				{
					success = false,
					prefixFound = prefix != null,
					verb = DescribeVerb(verb)
				};
			}
			var args = new object[] { verb, false };
			var continueOriginal = (bool)prefix.Invoke(null, args);
			var result = (bool)args[1];
			return new
			{
				success = continueOriginal && result == false,
				continueOriginal,
				result,
				verb = DescribeVerb(verb)
			};
		}

		static bool TryEquipAreaWorkflowChainsaw(Map map, Pawn actor, IntVec3 cell, List<Thing> spawnedThings, out Chainsaw chainsaw, out object error)
		{
			chainsaw = null;
			error = null;
			if (actor?.equipment == null)
			{
				error = new
				{
					success = false,
					actor = DescribePawn(actor),
					error = "Chainsaw fixture actor had no equipment tracker."
				};
				return false;
			}

			chainsaw = ThingMaker.MakeThing(CustomDefs.Chainsaw) as Chainsaw;
			if (chainsaw == null)
			{
				error = new
				{
					success = false,
					error = "Could not create Chainsaw."
				};
				return false;
			}
			spawnedThings.Add(chainsaw);
			GenSpawn.Spawn(chainsaw, cell, map, WipeMode.Vanish);
			var refuelable = chainsaw.TryGetComp<CompRefuelable>();
			var breakable = chainsaw.TryGetComp<CompBreakable>();
			if (refuelable == null || breakable == null)
			{
				error = new
				{
					success = false,
					chainsaw = ZombieRuntimeActions.StableThingId(chainsaw),
					error = "The spawned chainsaw did not have refuelable and breakable comps."
				};
				return false;
			}
			var fuel = ThingMaker.MakeThing(ThingDefOf.Chemfuel);
			fuel.stackCount = Math.Min(ThingDefOf.Chemfuel.stackLimit, refuelable.GetFuelCountToFullyRefuel());
			GenSpawn.Spawn(fuel, cell, map, WipeMode.Vanish);
			refuelable.Refuel(new List<Thing> { fuel });
			chainsaw.DeSpawn();
			actor.equipment.DestroyAllEquipment(DestroyMode.Vanish);
			actor.equipment.AddEquipment(chainsaw);
			_ = spawnedThings.Remove(chainsaw);
			return true;
		}

		static object VerifyAreaWorkflowJobGate(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			var patchTargets = PatchedMethodsForPatchClass("Pawn_JobTracker_StartJob_Patch");
			if (FindJobGateCells(map, root, out var cells, out var cellError) == false)
				return cellError;

			var normal = SpawnTargetZombie(map, cells[0], ZombieType.Normal, "ZL_Area_JobGateNormal", spawnedThings);
			var spitter = SpawnTargetSpitter(map, cells[1], "ZL_Area_JobGateSpitter", spawnedThings);
			var symbiant = SpawnTargetSymbiant(map, cells[2], "ZL_Area_JobGateSymbiant", spawnedThings);
			if (normal == null || spitter == null || symbiant == null)
			{
				return new
				{
					success = false,
					normal = DescribeZombie(normal),
					spitter = DescribeZombie(spitter),
					symbiant = DescribeZombie(symbiant),
					error = "Could not spawn all Zombieland pawn job-gate fixtures."
				};
			}

			var allowedCases = new[]
			{
				VerifyJobGateAllowed(map, normal, "normal"),
				VerifyJobGateAllowed(map, spitter, "spitter"),
				VerifyJobGateAllowed(map, symbiant, "symbiant")
			};
			var blockedCases = new[]
			{
				VerifyJobGateBlocked(map, normal, cells[3], "normal"),
				VerifyJobGateBlocked(map, spitter, cells[4], "spitter"),
				VerifyJobGateBlocked(map, symbiant, cells[5], "symbiant")
			};

			return new
			{
				success = patchTargets.Length > 0
					&& allowedCases.All(ObjectSuccess)
					&& blockedCases.All(ObjectSuccess),
				patchTargets = new
				{
					startJob = patchTargets
				},
				allowedCases,
				blockedCases
			};
		}

		static bool FindJobGateCells(Map map, IntVec3 root, out IntVec3[] cells, out object error)
		{
			cells = GenRadial.RadialCellsAround(root, 18f, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.OrderBy(cell => cell.DistanceToSquared(root))
				.Take(6)
				.ToArray();
			if (cells.Length >= 6)
			{
				error = null;
				return true;
			}
			error = new
			{
				success = false,
				root = ZombieRuntimeActions.DescribeCell(root),
				foundCells = cells.Length,
				error = "Could not find enough cells for the job-gate fixture."
			};
			return false;
		}

		static object VerifyJobGateAllowed(Map map, Pawn pawn, string label)
		{
			EndCurrentPawnJob(pawn);
			var waitJob = JobMaker.MakeJob(JobDefOf.Wait);
			pawn.jobs.StartJob(waitJob, JobCondition.InterruptForced, null, false, true);
			var fields = DescribeJobTrackerFields(pawn.jobs);

			return new
			{
				success = pawn.CurJobDef == JobDefOf.Wait
					&& fields.startingNewJob == false,
				label,
				pawn = DescribeZombie(pawn),
				job = DescribeJob(pawn.CurJob),
				fields
			};
		}

		static object VerifyJobGateBlocked(Map map, Pawn pawn, IntVec3 itemCell, string label)
		{
			var item = ThingMaker.MakeThing(ThingDefOf.Steel);
			item.stackCount = 1;
			GenSpawn.Spawn(item, itemCell, map, WipeMode.Vanish);
			var haulJob = JobMaker.MakeJob(JobDefOf.HaulToCell, item, pawn.Position);

			var reservedBefore = map.reservationManager.Reserve(pawn, haulJob, item, errorOnFailed: false);
			SetJobTrackerFields(pawn.jobs, 7, "dirty", true);
			var fieldsBefore = DescribeJobTrackerFields(pawn.jobs);
			var curJobBefore = pawn.CurJobDef?.defName;
			pawn.jobs.StartJob(haulJob, JobCondition.InterruptForced, null, false, true);
			var fieldsAfter = DescribeJobTrackerFields(pawn.jobs);
			var reservationAfter = map.reservationManager.ReservedBy(item, pawn, haulJob);
			var itemReservedAfter = map.reservationManager.IsReserved(item);

			return new
			{
				success = reservedBefore
					&& reservationAfter == false
					&& itemReservedAfter == false
					&& fieldsBefore.jobsGivenThisTick == 7
					&& fieldsBefore.jobsGivenThisTickTextual == "dirty"
					&& fieldsBefore.startingNewJob == true
					&& fieldsAfter.jobsGivenThisTick == 0
					&& fieldsAfter.jobsGivenThisTickTextual == ""
					&& fieldsAfter.startingNewJob == false
					&& pawn.CurJobDef?.defName == curJobBefore
					&& pawn.CurJobDef != JobDefOf.HaulToCell,
				label,
				pawn = DescribeZombie(pawn),
				disallowedJob = DescribeJob(haulJob),
				curJobBefore,
				curJobAfter = pawn.CurJobDef?.defName,
				reservedBefore,
				reservationAfter,
				itemReservedAfter,
				item = ZombieRuntimeActions.StableThingId(item),
				itemCell = ZombieRuntimeActions.DescribeCell(itemCell),
				fieldsBefore,
				fieldsAfter
			};
		}

		static void SetJobTrackerFields(Pawn_JobTracker jobs, int jobsGivenThisTick, string textual, bool startingNewJob)
		{
			AccessTools.Field(typeof(Pawn_JobTracker), "jobsGivenThisTick")?.SetValue(jobs, jobsGivenThisTick);
			AccessTools.Field(typeof(Pawn_JobTracker), "jobsGivenThisTickTextual")?.SetValue(jobs, textual);
			AccessTools.Field(typeof(Pawn_JobTracker), "startingNewJob")?.SetValue(jobs, startingNewJob);
		}

		sealed class JobTrackerFieldSnapshot
		{
			public int? jobsGivenThisTick { get; set; }
			public string jobsGivenThisTickTextual { get; set; }
			public bool? startingNewJob { get; set; }
		}

		static JobTrackerFieldSnapshot DescribeJobTrackerFields(Pawn_JobTracker jobs)
		{
			var jobsGivenRaw = AccessTools.Field(typeof(Pawn_JobTracker), "jobsGivenThisTick")?.GetValue(jobs);
			var startingRaw = AccessTools.Field(typeof(Pawn_JobTracker), "startingNewJob")?.GetValue(jobs);
			return new JobTrackerFieldSnapshot
			{
				jobsGivenThisTick = jobsGivenRaw is int jobsGiven ? jobsGiven : null,
				jobsGivenThisTickTextual = AccessTools.Field(typeof(Pawn_JobTracker), "jobsGivenThisTickTextual")?.GetValue(jobs) as string,
				startingNewJob = startingRaw is bool starting ? starting : null
			};
		}

		static object DescribeJob(Job job)
		{
			return new
			{
				def = job?.def?.defName,
				targetA = DescribeLocalTarget(job?.targetA ?? LocalTargetInfo.Invalid),
				targetB = DescribeLocalTarget(job?.targetB ?? LocalTargetInfo.Invalid),
				playerForced = job?.playerForced
			};
		}

		static void EndCurrentPawnJob(Pawn pawn)
		{
			if (pawn?.jobs?.curJob != null)
				pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
		}

		static object VerifyAreaWorkflowSpecialMeleeVerbs(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			var patchTargets = PatchedMethodsForPatchClass("Pawn_MeleeVerbs_GetUpdatedAvailableVerbsList_Patch");
			if (FindSpecialMeleeVerbCells(map, root, out var cells, out var cellError) == false)
				return cellError;

			var normal = SpawnTargetZombie(map, cells[0], ZombieType.Normal, "ZL_Area_MeleeVerbsNormal", spawnedThings);
			var albino = SpawnTargetZombie(map, cells[1], ZombieType.Albino, "ZL_Area_MeleeVerbsAlbino", spawnedThings);
			var activeElectric = SpawnTargetZombie(map, cells[2], ZombieType.Electrifier, "ZL_Area_MeleeVerbsActiveElectric", spawnedThings);
			var disabledElectric = SpawnTargetZombie(map, cells[3], ZombieType.Electrifier, "ZL_Area_MeleeVerbsDisabledElectric", spawnedThings);
			var target = SpawnMeleeAreaWorkflowPawn(map, "ZL_Area_MeleeVerbsTarget", cells[4], Faction.OfPlayer, spawnedThings);

			if (normal == null || albino == null || activeElectric == null || disabledElectric == null || target == null)
			{
				return new
				{
					success = false,
					normal = DescribeZombie(normal),
					albino = DescribeZombie(albino),
					activeElectric = DescribeZombie(activeElectric),
					disabledElectric = DescribeZombie(disabledElectric),
					target = DescribePawn(target),
					error = "Could not create all special melee verb fixtures."
				};
			}

			activeElectric.electricDisabledUntil = GenTicks.TicksGame - 1;
			disabledElectric.electricDisabledUntil = GenTicks.TicksGame + GenDate.TicksPerHour;

			var normalCase = DescribeSpecialMeleeVerbCase("normal", normal, target);
			var albinoCase = DescribeSpecialMeleeVerbCase("albino", albino, target);
			var activeElectricCase = DescribeSpecialMeleeVerbCase("activeElectric", activeElectric, target);
			var disabledElectricCase = DescribeSpecialMeleeVerbCase("disabledElectric", disabledElectric, target);

			return new
			{
				success = patchTargets.Length > 0
					&& SpecialVerbCaseHasBite(normalCase)
					&& SpecialVerbCaseHidesBite(albinoCase)
					&& SpecialVerbCaseHidesBite(activeElectricCase)
					&& SpecialVerbCaseHidesBite(disabledElectricCase),
				patchTargets = new
				{
					meleeVerbs = patchTargets
				},
				normal = normalCase,
				albino = albinoCase,
				activeElectric = activeElectricCase,
				disabledElectric = disabledElectricCase,
				target = DescribePawn(target)
			};
		}

		static bool FindSpecialMeleeVerbCells(Map map, IntVec3 root, out IntVec3[] cells, out object error)
		{
			cells = GenRadial.RadialCellsAround(root, 18f, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.OrderBy(cell => cell.DistanceToSquared(root))
				.Take(5)
				.ToArray();
			if (cells.Length >= 5)
			{
				error = null;
				return true;
			}
			error = new
			{
				success = false,
				root = ZombieRuntimeActions.DescribeCell(root),
				foundCells = cells.Length,
				error = "Could not find enough cells for the special melee verb fixture."
			};
			return false;
		}

		sealed class SpecialMeleeVerbCase
		{
			public string label { get; set; }
			public bool success { get; set; }
			public bool hasZombieBite { get; set; }
			public string[] damageDefs { get; set; }
			public object pawn { get; set; }
			public bool isAlbino { get; set; }
			public bool isElectrifier { get; set; }
			public bool isActiveElectric { get; set; }
			public object selectedVerb { get; set; }
		}

		static SpecialMeleeVerbCase DescribeSpecialMeleeVerbCase(string label, Zombie zombie, Pawn target)
		{
			var entries = zombie.meleeVerbs.GetUpdatedAvailableVerbsList(false);
			var damageDefs = entries
				.Select(entry => entry.verb.GetDamageDef()?.defName)
				.Where(defName => defName != null)
				.ToArray();
			var hasZombieBite = damageDefs.Contains(CustomDefs.ZombieBite.defName);
			var selectedVerb = zombie.meleeVerbs.TryGetMeleeVerb(target);
			var shouldHideBite = zombie.isAlbino || zombie.isElectrifier;
			return new SpecialMeleeVerbCase
			{
				label = label,
				success = shouldHideBite ? hasZombieBite == false : hasZombieBite,
				hasZombieBite = hasZombieBite,
				damageDefs = damageDefs,
				pawn = DescribeZombie(zombie),
				isAlbino = zombie.isAlbino,
				isElectrifier = zombie.isElectrifier,
				isActiveElectric = zombie.IsActiveElectric,
				selectedVerb = DescribeVerb(selectedVerb)
			};
		}

		static bool SpecialVerbCaseHasBite(object value)
		{
			return value is SpecialMeleeVerbCase meleeCase && meleeCase.success && meleeCase.hasZombieBite;
		}

		static bool SpecialVerbCaseHidesBite(object value)
		{
			return value is SpecialMeleeVerbCase meleeCase && meleeCase.success && meleeCase.hasZombieBite == false;
		}

		static object VerifyAreaWorkflowSpecialDamage(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			var patchTargets = PatchedMethodsForPatchClass("DamageWorker_AddInjury_ApplyDamageToPart_Patch");
			if (FindAreaWorkflowCells(map, root, 13, 18f, true, out var cells, out var cellError) == false)
				return cellError;

			var oldSpitterThreat = ZombieSettings.Values.spitterThreat;
			try
			{
				ZombieSettings.Values.spitterThreat = 1f;

				var spitterMelee = SpawnFireFixturePawn(map, cells[0], "spitter") as ZombieSpitter;
				var spitterRanged = SpawnFireFixturePawn(map, cells[1], "spitter") as ZombieSpitter;
				var normal = SpawnTargetZombie(map, cells[2], ZombieType.Normal, "ZL_Area_DamageNormal", spawnedThings);
				var former = SpawnFormerZombieForDamage(map, cells[3], spawnedThings);
				var albino = SpawnTargetZombie(map, cells[4], ZombieType.Albino, "ZL_Area_DamageAlbino", spawnedThings);
				var explosiveAlbino = SpawnTargetZombie(map, cells[5], ZombieType.Albino, "ZL_Area_DamageAlbinoExplosive", spawnedThings);
				var darkSlimer = SpawnTargetZombie(map, cells[6], ZombieType.DarkSlimer, "ZL_Area_DamageDarkSlimer", spawnedThings);
				var electrifierRanged = SpawnTargetZombie(map, cells[7], ZombieType.Electrifier, "ZL_Area_DamageElectricRanged", spawnedThings);
				var electrifierCut = SpawnTargetZombie(map, cells[8], ZombieType.Electrifier, "ZL_Area_DamageElectricCut", spawnedThings);
				var armorHuman = SpawnAreaWorkflowPawn(map, "ZL_Area_DamageArmorHuman", cells[9], Faction.OfPlayer, spawnedThings);
				var shieldTanky = SpawnTargetZombie(map, cells[10], ZombieType.TankyOperator, "ZL_Area_DamageShieldTanky", spawnedThings);
				var helmetTanky = SpawnTargetZombie(map, cells[11], ZombieType.TankyOperator, "ZL_Area_DamageHelmetTanky", spawnedThings);
				var suitTanky = SpawnTargetZombie(map, cells[12], ZombieType.TankyOperator, "ZL_Area_DamageSuitTanky", spawnedThings);

				foreach (var pawn in new Pawn[] { spitterMelee, spitterRanged })
					if (pawn != null)
						spawnedThings.Add(pawn);
				armorHuman?.apparel?.DestroyAll();
				foreach (var zombie in new[] { normal, former, albino, explosiveAlbino, darkSlimer, electrifierRanged, electrifierCut, shieldTanky, helmetTanky, suitTanky })
					zombie?.apparel?.DestroyAll();

				if (spitterMelee == null || spitterRanged == null || normal == null || former == null || albino == null || explosiveAlbino == null || darkSlimer == null || electrifierRanged == null || electrifierCut == null || armorHuman == null || shieldTanky == null || helmetTanky == null || suitTanky == null)
				{
					return new
					{
						success = false,
						patchTargets,
						spitterMelee = DescribePawn(spitterMelee),
						spitterRanged = DescribePawn(spitterRanged),
						normal = DescribeZombie(normal),
						former = DescribeZombie(former),
						albino = DescribeZombie(albino),
						explosiveAlbino = DescribeZombie(explosiveAlbino),
						darkSlimer = DescribeZombie(darkSlimer),
						electrifierRanged = DescribeZombie(electrifierRanged),
						electrifierCut = DescribeZombie(electrifierCut),
						armorHuman = DescribePawn(armorHuman),
						shieldTanky = DescribeZombie(shieldTanky),
						helmetTanky = DescribeZombie(helmetTanky),
						suitTanky = DescribeZombie(suitTanky),
						error = "Could not create all special damage fixtures."
					};
				}

				electrifierRanged.electricDisabledUntil = GenTicks.TicksGame - 1;
				electrifierCut.electricDisabledUntil = GenTicks.TicksGame - 1;

				var spitterScaling = VerifySpecialDamageSpitterScaling(spitterMelee, spitterRanged);
				var formerReduction = VerifySpecialDamageFormerReduction(normal, former);
				var albinoGating = VerifySpecialDamageAlbinoGating(albino, explosiveAlbino);
				var darkSlimerSmoke = VerifySpecialDamageDarkSlimerSmoke(map, darkSlimer);
				var electricAbsorption = VerifySpecialDamageElectricAbsorption(electrifierRanged, electrifierCut);
				var tankyArmor = VerifySpecialDamageTankyArmor(armorHuman, shieldTanky, helmetTanky, suitTanky);

				return new
				{
					success = patchTargets.Length > 0
						&& ObjectSuccess(spitterScaling)
						&& ObjectSuccess(formerReduction)
						&& ObjectSuccess(albinoGating)
						&& ObjectSuccess(darkSlimerSmoke)
						&& ObjectSuccess(electricAbsorption)
						&& ObjectSuccess(tankyArmor),
					patchTargets = new
					{
						applyDamageToPart = patchTargets,
						armorUtility = PatchedMethodsForPatchClass("ArmorUtility_GetPostArmorDamage_Patch")
					},
					spitterScaling,
					formerReduction,
					albinoGating,
					darkSlimerSmoke,
					electricAbsorption,
					tankyArmor,
					settings = new
					{
						spitterThreat = ZombieSettings.Values.spitterThreat,
						difficulty = Tools.Difficulty()
					}
				};
			}
			finally
			{
				ZombieSettings.Values.spitterThreat = oldSpitterThreat;
			}
		}

		static object VerifyAreaWorkflowCeCompat(Map map, List<Thing> spawnedThings)
		{
			var settingsSnapshot = SnapshotZombieSettings();
			try
			{
				ApplyZombieSettingsOverride(settings =>
				{
					settings.spitterThreat = 1f;
					settings.zombieInstinct = ZombieInstinct.Normal;
					settings.reducedTurretConsumption = 1f;
				});

				var projectileCeType = AccessTools.TypeByName("CombatExtended.ProjectileCE");
				var armorRerouteType = AccessTools.TypeByName("CombatExtended.HarmonyCE.Harmony_DamageWorker_AddInjury_ApplyDamageToPart")
					?? AccessTools.TypeByName("CombatExtended.Harmony.Harmony_DamageWorker_AddInjury_ApplyDamageToPart");
				var armorUtilityCeType = AccessTools.TypeByName("CombatExtended.ArmorUtilityCE");
				var compAmmoUserType = AccessTools.TypeByName("CombatExtended.CompAmmoUser");
				var ceTypesPresent = projectileCeType != null && armorRerouteType != null && armorUtilityCeType != null && compAmmoUserType != null;

				if (FindAreaWorkflowCells(map, new IntVec3(118, 0, 118), 5, 24f, out var cells, out var cellError) == false)
					return cellError;

				var surgicalCut = VerifyCeSurgicalCutArmorReroute(armorRerouteType);
				var projectileNoise = VerifyCeProjectileNoise(map, cells[0], projectileCeType, spawnedThings);
				var spitterArmor = VerifyCeSpitterAfterArmorDamage(map, cells[2], armorUtilityCeType, spawnedThings);
				var turretAmmo = VerifyCeTurretAmmoReduction(map, cells[4], compAmmoUserType, spawnedThings);

				return new
				{
					success = ceTypesPresent
						&& ObjectSuccess(surgicalCut)
						&& ObjectSuccess(projectileNoise)
						&& ObjectSuccess(spitterArmor)
						&& ObjectSuccess(turretAmmo),
					ceTypes = new
					{
						projectileCe = projectileCeType?.FullName,
						armorReroute = armorRerouteType?.FullName,
						armorUtilityCe = armorUtilityCeType?.FullName,
						compAmmoUser = compAmmoUserType?.FullName
					},
					patchTargets = new
					{
						armorReroute = PatchedMethodsForPatchClass("CETools_Patch1"),
						projectileLaunch = PatchedMethodsForPatchClass("CETools_Patch2"),
						afterArmorDamage = PatchedMethodsForPatchClass("CETools_Patch3"),
						ammoUser = PatchedMethodsForPatchClass("CETools_Patch4")
					},
					settings = new
					{
						ZombieSettings.Values.spitterThreat,
						ZombieSettings.Values.zombieInstinct,
						ZombieSettings.Values.reducedTurretConsumption
					},
					surgicalCut,
					projectileNoise,
					spitterArmor,
					turretAmmo
				};
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
			}
		}

		static object VerifyCeSurgicalCutArmorReroute(Type armorRerouteType)
		{
			var method = armorRerouteType == null ? null : AccessTools.Method(armorRerouteType, "ArmorReroute");
			if (method == null)
			{
				return new
				{
					success = false,
					armorRerouteType = armorRerouteType?.FullName,
					error = "Could not resolve CE ArmorReroute."
				};
			}

			var dinfo = new DamageInfo(DamageDefOf.SurgicalCut, 7f);
			var args = new object[] { null, dinfo, false, false };
			object error = null;
			try
			{
				method.Invoke(null, args);
			}
			catch (Exception ex)
			{
				error = ex.GetBaseException().Message;
			}
			var output = args[1] is DamageInfo outputInfo ? outputInfo : dinfo;

			return new
			{
				success = error == null
					&& output.Def == DamageDefOf.SurgicalCut
					&& Approximately(output.Amount, dinfo.Amount, 0.0001f)
					&& (bool)args[2] == false
					&& (bool)args[3] == false,
				method = method.FullDescription(),
				nullPawnWouldReachCeOriginal = "would throw without the Zombieland SurgicalCut prefix skip",
				error,
				input = new { def = dinfo.Def.defName, dinfo.Amount },
				output = new { def = output.Def.defName, output.Amount },
				deflectedByArmor = args[2],
				diminishedByArmor = args[3]
			};
		}

		static object VerifyCeProjectileNoise(Map map, IntVec3 root, Type projectileCeType, List<Thing> spawnedThings)
		{
			if (projectileCeType == null)
				return new { success = false, error = "CombatExtended.ProjectileCE is not loaded." };
			if (TryFindClearSpawnCell(map, root, 16f, out var shooterCell, out var shooterError) == false)
				return shooterError;
			if (TryFindClearSpawnCell(map, shooterCell + new IntVec3(0, 0, 5), 12f, out var spitterCell, out var spitterError) == false)
				return spitterError;

			var shooter = SpawnArmedAreaWorkflowPawn(map, "ZL_Area_CEProjectileShooter", shooterCell, Faction.OfPlayer, spawnedThings);
			var spitter = SpawnTargetSpitter(map, spitterCell, "ZL_Area_CEProjectileSpitter", spawnedThings);
			var projectileDef = FindCeProjectileDef(projectileCeType, shooter);
			var launchParameters = CeProjectilePhysicsLaunchParameters();
			var launch = AccessTools.Method(projectileCeType, "Launch", launchParameters);
			if (shooter == null || spitter == null || projectileDef == null || launch == null)
			{
				return new
				{
					success = false,
					shooter = DescribePawn(shooter),
					spitter = DescribeZombie(spitter),
					projectileDef = projectileDef?.defName,
					launchFound = launch != null,
					error = "Could not create CE projectile noise fixtures."
				};
			}

			const float radius = 16f;
			var shotAngle = 0.25f;
			var shotRotation = 0f;
			var shotHeight = 1f;
			var shotSpeed = 50f;
			var distance = 12f;

			ClearPheromones(map, shooterCell, radius);
			var beforeHuman = SnapshotPheromones(map, shooterCell, radius);
			var humanProjectile = ThingMaker.MakeThing(projectileDef) as ThingWithComps;
			if (humanProjectile != null)
			{
				GenSpawn.Spawn(humanProjectile, shooterCell, map, WipeMode.Vanish);
				spawnedThings.Add(humanProjectile);
			}
			var humanError = InvokeCeProjectileLaunch(launch, humanProjectile, shooter, shooterCell, shotAngle, shotRotation, shotHeight, shotSpeed, shooter.equipment?.Primary, distance);
			var humanChange = DescribePheromoneChange(map, beforeHuman, out var humanChangedCount);

			ClearPheromones(map, spitterCell, radius);
			var beforeSpitter = SnapshotPheromones(map, spitterCell, radius);
			var spitterProjectile = ThingMaker.MakeThing(projectileDef) as ThingWithComps;
			if (spitterProjectile != null)
			{
				GenSpawn.Spawn(spitterProjectile, spitterCell, map, WipeMode.Vanish);
				spawnedThings.Add(spitterProjectile);
			}
			var spitterErrorText = InvokeCeProjectileLaunch(launch, spitterProjectile, spitter, spitterCell, shotAngle, shotRotation, shotHeight, shotSpeed, null, distance);
			var spitterChange = DescribePheromoneChange(map, beforeSpitter, out var spitterChangedCount);

			var overrideProjectileDef = FindCeProjectileDef(projectileCeType, shooter, true);
			var overrideLaunch = overrideProjectileDef?.thingClass == null
				? null
				: AccessTools.DeclaredMethod(overrideProjectileDef.thingClass, "Launch", launchParameters);
			ClearPheromones(map, shooterCell, radius);
			var beforeOverride = SnapshotPheromones(map, shooterCell, radius);
			var overrideProjectile = overrideProjectileDef == null ? null : ThingMaker.MakeThing(overrideProjectileDef) as ThingWithComps;
			if (overrideProjectile != null)
			{
				GenSpawn.Spawn(overrideProjectile, shooterCell, map, WipeMode.Vanish);
				spawnedThings.Add(overrideProjectile);
			}
			var overrideError = InvokeCeProjectileLaunch(overrideLaunch, overrideProjectile, shooter, shooterCell, shotAngle, shotRotation, shotHeight, shotSpeed, shooter.equipment?.Primary, distance);
			var overrideChange = DescribePheromoneChange(map, beforeOverride, out var overrideChangedCount);
			var overrideCovered = overrideProjectileDef == null
				|| (overrideProjectile != null && overrideLaunch != null && overrideError == null && overrideChangedCount > 0);

			return new
			{
				success = humanProjectile != null
					&& spitterProjectile != null
					&& humanError == null
					&& spitterErrorText == null
					&& humanChangedCount > 0
					&& spitterChangedCount == 0
					&& overrideCovered,
				projectileDef = projectileDef.defName,
				projectileType = projectileDef.thingClass?.FullName,
				launch = launch.FullDescription(),
				patchTargets = PatchedMethodsForPatchClass("CETools_Patch2"),
				shot = new { shotAngle, shotRotation, shotHeight, shotSpeed, distance },
				human = new
				{
					shooter = DescribePawn(shooter),
					cell = ZombieRuntimeActions.DescribeCell(shooterCell),
					error = humanError,
					changedCount = humanChangedCount,
					change = humanChange
				},
				spitter = new
				{
					zombie = DescribeZombie(spitter),
					cell = ZombieRuntimeActions.DescribeCell(spitterCell),
					error = spitterErrorText,
					changedCount = spitterChangedCount,
					change = spitterChange
				},
				declaredOverride = new
				{
					covered = overrideCovered,
					projectileDef = overrideProjectileDef?.defName,
					projectileType = overrideProjectileDef?.thingClass?.FullName,
					launch = overrideLaunch?.FullDescription(),
					error = overrideError,
					changedCount = overrideChangedCount,
					change = overrideChange
				}
			};
		}

		static Type[] CeProjectilePhysicsLaunchParameters() =>
			new[] { typeof(Thing), typeof(Vector2), typeof(float), typeof(float), typeof(float), typeof(float), typeof(Thing), typeof(float) };

		static ThingDef FindCeProjectileDef(Type projectileCeType, Pawn shooter, bool requireDeclaredPhysicsLaunch = false)
		{
			var equippedProjectile = shooter?.equipment?.PrimaryEq?.PrimaryVerb?.verbProps?.defaultProjectile;
			if (requireDeclaredPhysicsLaunch == false && equippedProjectile != null && projectileCeType.IsAssignableFrom(equippedProjectile.thingClass))
				return equippedProjectile;
			var parameters = CeProjectilePhysicsLaunchParameters();
			return DefDatabase<ThingDef>.AllDefsListForReading
				.Where(def => def.thingClass != null && projectileCeType.IsAssignableFrom(def.thingClass))
				.Where(def => requireDeclaredPhysicsLaunch == false || AccessTools.DeclaredMethod(def.thingClass, "Launch", parameters) != null)
				.OrderBy(def => def.defName)
				.FirstOrDefault();
		}

		static string InvokeCeProjectileLaunch(MethodInfo launch, ThingWithComps projectile, Thing launcher, IntVec3 originCell, float shotAngle, float shotRotation, float shotHeight, float shotSpeed, Thing equipment, float distance)
		{
			if (launch == null)
				return "Launch method was null.";
			if (projectile == null)
				return "Projectile instance was null.";
			try
			{
				var origin = new Vector2(originCell.x, originCell.z);
				launch.Invoke(projectile, new object[] { launcher, origin, shotAngle, shotRotation, shotHeight, shotSpeed, equipment, distance });
				return null;
			}
			catch (Exception ex)
			{
				return ex.GetBaseException().Message;
			}
		}

		static object VerifyAreaWorkflowCeRangedProjectiles(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			var projectileCeType = AccessTools.TypeByName("CombatExtended.ProjectileCE");
			var verbShootCeType = AccessTools.TypeByName("CombatExtended.Verb_ShootCE");
			var verbLaunchProjectileCeType = AccessTools.TypeByName("CombatExtended.Verb_LaunchProjectileCE");
			var compAmmoUserType = AccessTools.TypeByName("CombatExtended.CompAmmoUser");
			var skipMissing = FindNestedPatchMethod("Verb_LaunchProjectile_TryCastShot_Patch", "SkipMissingShotsAtZombies");
			if (projectileCeType == null || verbShootCeType == null || verbLaunchProjectileCeType == null || compAmmoUserType == null || skipMissing == null)
			{
				return new
				{
					success = false,
					error = "Combat Extended verb/projection types or the Zombieland ranged helper were not loaded.",
					reflection = new
					{
						projectileCe = projectileCeType?.FullName,
						verbShootCe = verbShootCeType?.FullName,
						verbLaunchProjectileCe = verbLaunchProjectileCeType?.FullName,
						compAmmoUser = compAmmoUserType?.FullName,
						skipMissingFound = skipMissing != null
					}
				};
			}

			var settingsSnapshot = SnapshotZombieSettings();
			var oldColonistsHitChance = Constants.COLONISTS_HIT_ZOMBIES_CHANCE;
			try
			{
				ApplyZombieSettingsOverride(settings =>
				{
					settings.threatScale = 1f;
					settings.zombieInstinct = ZombieInstinct.Normal;
				});
				Constants.COLONISTS_HIT_ZOMBIES_CHANCE = 1f;

				if (TryFindClearSpawnCell(map, root, 16f, out var shooterCell, out var shooterError) == false)
					return shooterError;
				var targetCell = GenRadial.RadialCellsAround(shooterCell, 10f, false)
					.Where(cell => cell.InBounds(map))
					.Where(cell => cell.Standable(map))
					.Where(cell => cell.Fogged(map) == false)
					.Where(cell => cell.GetFirstPawn(map) == null)
					.Where(cell => cell.DistanceTo(shooterCell) >= 5f)
					.Where(cell => GenSight.LineOfSight(shooterCell, cell, map, true))
					.OrderBy(cell => cell.DistanceToSquared(shooterCell))
					.FirstOrDefault();
				if (targetCell.IsValid == false)
				{
					return new
					{
						success = false,
						shooterCell = ZombieRuntimeActions.DescribeCell(shooterCell),
						error = "No clear line-of-sight CE target cell was found."
					};
				}

				var shooter = SpawnArmedAreaWorkflowPawn(map, "ZL_Area_CERangedShooter", shooterCell, Faction.OfPlayer, spawnedThings);
				var zombie = SpawnTargetZombie(map, targetCell, ZombieType.Normal, "ZL_Area_CERangedZombie", spawnedThings);
				var verb = shooter?.equipment?.PrimaryEq?.PrimaryVerb;
				var weapon = shooter?.equipment?.Primary as ThingWithComps;
				var compAmmo = FindCompAssignableTo(weapon, compAmmoUserType);
				var ammoSetup = SetupCeAmmoForShot(compAmmo, compAmmoUserType);
				if (shooter == null || zombie == null || verb == null || weapon == null)
				{
					return new
					{
						success = false,
						shooter = DescribePawn(shooter),
						zombie = DescribeZombie(zombie),
						verb = DescribeVerb(verb),
						weaponDef = weapon?.def?.defName,
						error = "Could not create CE ranged projectile fixtures."
					};
				}

				var isCeShootVerb = verbShootCeType.IsInstanceOfType(verb);
				var isCeLaunchVerb = verbLaunchProjectileCeType.IsInstanceOfType(verb);
				var vanillaSkipMissingApplies = (bool)skipMissing.Invoke(null, new object[] { verb, new LocalTargetInfo(zombie) });

				const float radius = 16f;
				ClearPheromones(map, shooterCell, radius);
				var pheromonesBefore = SnapshotPheromones(map, shooterCell, radius);
				var projectilesBefore = map.listerThings.AllThings
					.Where(thing => projectileCeType.IsInstanceOfType(thing))
					.Select(thing => thing.ThingID)
					.ToHashSet();
				var ammoBefore = ReadCeAmmoCount(compAmmo, compAmmoUserType);
				var injuryBefore = TotalInjurySeverity(zombie);

				verb.currentTarget = new LocalTargetInfo(zombie);
				verb.canHitNonTargetPawnsNow = true;
				object castError = null;
				var castResult = false;
				try
				{
					Rand.PushState(197504);
					castResult = verb.TryCastShot();
				}
				catch (Exception ex)
				{
					castError = ex.GetBaseException().Message;
				}
				finally
				{
					Rand.PopState();
				}

				var launchedProjectile = map.listerThings.AllThings
					.Where(thing => projectileCeType.IsInstanceOfType(thing))
					.FirstOrDefault(thing => projectilesBefore.Contains(thing.ThingID) == false);
				if (launchedProjectile != null)
					spawnedThings.Add(launchedProjectile);

				var pheromoneChange = DescribePheromoneChange(map, pheromonesBefore, out var changedCount);
				var ammoAfter = ReadCeAmmoCount(compAmmo, compAmmoUserType);
				var intendedTarget = AccessTools.Field(projectileCeType, "intendedTarget")?.GetValue(launchedProjectile);
				var destination = AccessTools.Field(projectileCeType, "Destination")?.GetValue(launchedProjectile);
				var exactPosition = AccessTools.Property(projectileCeType, "ExactPosition")?.GetValue(launchedProjectile);
				var intendedTargetIsZombie = IntendedTargetMatchesThing(intendedTarget, zombie);
				var destinationCell = destination is Vector2 vector ? new IntVec3(Mathf.RoundToInt(vector.x), 0, Mathf.RoundToInt(vector.y)) : IntVec3.Invalid;

				var ticksRun = 0;
				var injuryChangedTick = -1;
				for (; ticksRun < 90 && launchedProjectile != null && launchedProjectile.Destroyed == false; ticksRun++)
				{
					AdvanceGameTicks(1);
					if (TotalInjurySeverity(zombie) > injuryBefore || zombie.Dead)
					{
						injuryChangedTick = ticksRun + 1;
						break;
					}
				}
				var injuryAfter = TotalInjurySeverity(zombie);

				return new
				{
					success = isCeShootVerb
						&& isCeLaunchVerb
						&& vanillaSkipMissingApplies == false
						&& ObjectSuccess(ammoSetup)
						&& castError == null
						&& castResult
						&& launchedProjectile != null
						&& intendedTargetIsZombie
						&& changedCount > 0
						&& ammoAfter < ammoBefore,
					ceTypes = new
					{
						projectileCe = projectileCeType.FullName,
						verbShootCe = verbShootCeType.FullName,
						verbLaunchProjectileCe = verbLaunchProjectileCeType.FullName,
						compAmmoUser = compAmmoUserType.FullName
					},
					patchTargets = new
					{
						vanillaLaunchProjectile = PatchedMethodsForPatchClass("Verb_LaunchProjectile_TryCastShot_Patch"),
						ceProjectileLaunch = PatchedMethodsForPatchClass("CETools_Patch2")
					},
					shooter = DescribePawn(shooter),
					zombie = DescribeZombie(zombie),
					weapon = new
					{
						def = weapon.def?.defName,
						compAmmoType = compAmmo?.GetType().FullName,
						ammoSetup,
						ammoBefore,
						ammoAfter,
						ammoDelta = ammoAfter - ammoBefore
					},
					verb = DescribeVerb(verb),
					ceVerb = new
					{
						isCeShootVerb,
						isCeLaunchVerb,
						vanillaSkipMissingApplies,
						note = "CE Verb_ShootCE does not inherit Verse.Verb_LaunchProjectile, so the vanilla forced-miss helper is intentionally a boundary rather than CE proof."
					},
					cast = new
					{
						castResult,
						castError,
						projectileDef = launchedProjectile?.def?.defName,
						projectileType = launchedProjectile?.GetType().FullName,
						intendedTargetIsZombie,
						destination = destination == null ? null : destination.ToString(),
						destinationCell = destinationCell.IsValid ? ZombieRuntimeActions.DescribeCell(destinationCell) : null,
						exactPosition = exactPosition == null ? null : exactPosition.ToString()
					},
					pheromones = new
					{
						changedCount,
						change = pheromoneChange
					},
					impactWindow = new
					{
						ticksRun,
						injuryChangedTick,
						injuryBefore,
						injuryAfter,
						injuryDelta = injuryAfter - injuryBefore,
						zombieDead = zombie.Dead,
						projectileStillSpawned = launchedProjectile?.Spawned == true && launchedProjectile.Destroyed == false
					}
				};
			}
			finally
			{
				Constants.COLONISTS_HIT_ZOMBIES_CHANCE = oldColonistsHitChance;
				RestoreZombieSettings(settingsSnapshot);
			}
		}

		static ThingComp FindCompAssignableTo(ThingWithComps thing, Type compType)
		{
			if (thing == null || compType == null)
				return null;
			return thing.AllComps?.FirstOrDefault(comp => compType.IsInstanceOfType(comp));
		}

		static object SetupCeAmmoForShot(object compAmmo, Type compAmmoUserType)
		{
			var magSize = AccessTools.Property(compAmmoUserType, "MagSize");
			var curMagCount = AccessTools.Property(compAmmoUserType, "CurMagCount");
			var hasMagazine = AccessTools.Property(compAmmoUserType, "HasMagazine");
			var resetAmmoCount = AccessTools.Method(compAmmoUserType, "ResetAmmoCount");
			if (compAmmo == null || magSize == null || curMagCount == null || hasMagazine == null)
			{
				return new
				{
					success = false,
					compAmmoType = compAmmo?.GetType().FullName,
					reflection = new
					{
						magSize = magSize != null,
						curMagCount = curMagCount != null,
						hasMagazine = hasMagazine != null,
						resetAmmoCount = resetAmmoCount != null
					}
				};
			}

			object resetError = null;
			try
			{
				resetAmmoCount?.Invoke(compAmmo, new object[] { null });
			}
			catch (Exception ex)
			{
				resetError = ex.GetBaseException().Message;
			}

			var hasMagazineValue = (bool)hasMagazine.GetValue(compAmmo);
			var magSizeValue = Convert.ToInt32(magSize.GetValue(compAmmo));
			var targetCount = Math.Max(3, Math.Min(8, magSizeValue > 0 ? magSizeValue : 8));
			if (hasMagazineValue)
				curMagCount.SetValue(compAmmo, targetCount);
			var currentCount = Convert.ToInt32(curMagCount.GetValue(compAmmo));

			return new
			{
				success = resetError == null && hasMagazineValue && currentCount >= 3,
				compAmmoType = compAmmo.GetType().FullName,
				hasMagazine = hasMagazineValue,
				magSize = magSizeValue,
				targetCount,
				currentCount,
				resetError
			};
		}

		static int ReadCeAmmoCount(object compAmmo, Type compAmmoUserType)
		{
			var curMagCount = AccessTools.Property(compAmmoUserType, "CurMagCount");
			if (compAmmo == null || curMagCount == null)
				return -1;
			return Convert.ToInt32(curMagCount.GetValue(compAmmo));
		}

		static bool IntendedTargetMatchesThing(object intendedTarget, Thing thing)
		{
			if (intendedTarget == null || thing == null)
				return false;
			var type = intendedTarget.GetType();
			var hasThing = AccessTools.Property(type, "HasThing")?.GetValue(intendedTarget) as bool?;
			var targetThing = AccessTools.Property(type, "Thing")?.GetValue(intendedTarget) as Thing;
			return hasThing == true && targetThing == thing;
		}

		static object VerifyCeSpitterAfterArmorDamage(Map map, IntVec3 root, Type armorUtilityCeType, List<Thing> spawnedThings)
		{
			var boolRef = typeof(bool).MakeByRefType();
			var method = armorUtilityCeType == null ? null : AccessTools.Method(armorUtilityCeType, "GetAfterArmorDamage", new[] { typeof(DamageInfo), typeof(Pawn), typeof(BodyPartRecord), boolRef, boolRef, boolRef });
			if (method == null)
			{
				return new
				{
					success = false,
					armorUtilityCeType = armorUtilityCeType?.FullName,
					error = "Could not resolve CE ArmorUtilityCE.GetAfterArmorDamage."
				};
			}
			if (TryFindClearSpawnCell(map, root, 12f, out var spitterCell, out var spitterError) == false)
				return spitterError;

			var spitter = SpawnTargetSpitter(map, spitterCell, "ZL_Area_CEAfterArmorSpitter", spawnedThings);
			var part = FindNonHeadPart(spitter);
			if (spitter == null || part == null)
			{
				return new
				{
					success = false,
					spitter = DescribeZombie(spitter),
					part = DescribeBodyPartDetail(part),
					error = "Could not create a CE after-armor spitter fixture."
				};
			}

			const float inputAmount = 22f;
			var original = new DamageInfo(DamageDefOf.Bullet, inputAmount, 0f, -1f, null, part, null, DamageInfo.SourceCategory.ThingOrUnknown, null, true, true);
			var args = new object[] { original, spitter, part, false, false, false };
			DamageInfo result = original;
			object error = null;
			Rand.PushState(6610);
			try
			{
				result = (DamageInfo)method.Invoke(null, args);
			}
			catch (Exception ex)
			{
				error = ex.GetBaseException().Message;
			}
			finally
			{
				Rand.PopState();
			}

			return new
			{
				success = error == null
					&& result.Def == DamageDefOf.Bullet
					&& result.Amount > 0f
					&& result.Amount < inputAmount
					&& (bool)args[4],
				method = method.FullDescription(),
				error,
				spitter = DescribeZombie(spitter),
				part = DescribeBodyPartDetail(part),
				input = new { def = original.Def.defName, amount = inputAmount },
				result = new { def = result.Def.defName, result.Amount },
				armorDeflected = args[3],
				armorReduced = args[4],
				shieldAbsorbed = args[5]
			};
		}

		static object VerifyCeTurretAmmoReduction(Map map, IntVec3 root, Type compAmmoUserType, List<Thing> spawnedThings)
		{
			if (compAmmoUserType == null)
				return new { success = false, error = "CombatExtended.CompAmmoUser is not loaded." };
			var turretDef = DefDatabase<ThingDef>.GetNamed("Turret_MiniTurret", false)
				?? DefDatabase<ThingDef>.AllDefsListForReading
					.Where(def => typeof(Building_Turret).IsAssignableFrom(def.thingClass))
					.Where(def => def.comps?.Any(comp => comp.compClass != null && compAmmoUserType.IsAssignableFrom(comp.compClass)) == true)
					.OrderBy(def => def.defName)
					.FirstOrDefault();
			if (turretDef == null)
				return new { success = false, error = "No CE-compatible turret def was found." };
			if (TryFindClearBuildingFootprint(map, turretDef, root, 18f, out var turretCell, out var turretError) == false)
				return turretError;
			var stuff = turretDef.MadeFromStuff ? GenStuff.DefaultStuffFor(turretDef) ?? ThingDefOf.Steel : null;
			var turret = ThingMaker.MakeThing(turretDef, stuff) as ThingWithComps;
			if (turret == null)
			{
				return new
				{
					success = false,
					turretDef = turretDef.defName,
					error = "The selected CE turret def did not create a ThingWithComps."
				};
			}
			turret.SetFactionDirect(Faction.OfPlayer);
			GenSpawn.Spawn(turret, turretCell, map, Rot4.North, WipeMode.Vanish, false);
			spawnedThings.Add(turret);

			object compAmmo = turret.AllComps?.FirstOrDefault(comp => compAmmoUserType.IsInstanceOfType(comp));
			if (compAmmo == null)
			{
				var compAmmoProperty = AccessTools.Property(turret.GetType(), "CompAmmo");
				compAmmo = compAmmoProperty?.GetValue(turret);
			}
			var curMagCount = AccessTools.Property(compAmmoUserType, "CurMagCount");
			var magSize = AccessTools.Property(compAmmoUserType, "MagSize");
			var hasMagazine = AccessTools.Property(compAmmoUserType, "HasMagazine");
			var notifyShotFired = AccessTools.Method(compAmmoUserType, "Notify_ShotFired", new[] { typeof(int) });
			if (compAmmo == null || curMagCount == null || magSize == null || hasMagazine == null || notifyShotFired == null)
			{
				return new
				{
					success = false,
					turret = DescribeTarget(turret as IAttackTarget),
					turretDef = turretDef.defName,
					compAmmoPresent = compAmmo != null,
					reflection = new
					{
						curMagCount = curMagCount != null,
						magSize = magSize != null,
						hasMagazine = hasMagazine != null,
						notifyShotFired = notifyShotFired != null
					},
					error = "Could not resolve CE turret ammo reflection members."
				};
			}

			var hasMagazineValue = (bool)hasMagazine.GetValue(compAmmo);
			var magSizeValue = Convert.ToInt32(magSize.GetValue(compAmmo));
			var startCount = Math.Max(3, Math.Min(8, magSizeValue > 0 ? magSizeValue : 8));
			curMagCount.SetValue(compAmmo, startCount);

			ZombieSettings.Values.reducedTurretConsumption = 1f;
			var skipBefore = Convert.ToInt32(curMagCount.GetValue(compAmmo));
			var skipError = InvokeNotifyShotFired(notifyShotFired, compAmmo, 1);
			var skipAfter = Convert.ToInt32(curMagCount.GetValue(compAmmo));

			ZombieSettings.Values.reducedTurretConsumption = 0f;
			curMagCount.SetValue(compAmmo, startCount);
			var consumeBefore = Convert.ToInt32(curMagCount.GetValue(compAmmo));
			var consumeError = InvokeNotifyShotFired(notifyShotFired, compAmmo, 1);
			var consumeAfter = Convert.ToInt32(curMagCount.GetValue(compAmmo));

			return new
			{
				success = hasMagazineValue
					&& skipError == null
					&& consumeError == null
					&& skipAfter == skipBefore
					&& consumeAfter == consumeBefore - 1,
				turret = DescribeTarget(turret as IAttackTarget),
				turretDef = turretDef.defName,
				turretCell = ZombieRuntimeActions.DescribeCell(turretCell),
				compAmmoType = compAmmo.GetType().FullName,
				hasMagazine = hasMagazineValue,
				magSize = magSizeValue,
				startCount,
				notifyShotFired = notifyShotFired.FullDescription(),
				reducedConsumptionSkip = new
				{
					setting = 1f,
					before = skipBefore,
					after = skipAfter,
					error = skipError
				},
				normalConsumption = new
				{
					setting = 0f,
					before = consumeBefore,
					after = consumeAfter,
					error = consumeError
				}
			};
		}

		static string InvokeNotifyShotFired(MethodInfo notifyShotFired, object compAmmo, int ammoConsumedPerShot)
		{
			try
			{
				notifyShotFired.Invoke(compAmmo, new object[] { ammoConsumedPerShot });
				return null;
			}
			catch (Exception ex)
			{
				return ex.GetBaseException().Message;
			}
		}

		static bool FindAreaWorkflowCells(Map map, IntVec3 root, int count, float radius, out IntVec3[] cells, out object error)
		{
			return FindAreaWorkflowCells(map, root, count, radius, false, out cells, out error);
		}

		static bool FindAreaWorkflowCells(Map map, IntVec3 root, int count, float radius, bool requireDry, out IntVec3[] cells, out object error)
		{
			var result = new List<IntVec3>();
			var nextRoot = root;
			for (var i = 0; i < count; i++)
			{
				if (TryFindAreaWorkflowCell(map, nextRoot, radius, requireDry, out var cell, out var spawnError) == false)
				{
					cells = result.ToArray();
					error = new
					{
						success = false,
						root = ZombieRuntimeActions.DescribeCell(root),
						foundCells = result.Count,
						requestedCells = count,
						error = spawnError
					};
					return false;
				}
				result.Add(cell);
				nextRoot = cell + new IntVec3(4, 0, 0);
			}

			cells = result.ToArray();
			error = null;
			return true;
		}

		static bool TryFindAreaWorkflowCell(Map map, IntVec3 root, float radius, bool requireDry, out IntVec3 cell, out object error)
		{
			if (requireDry == false)
				return TryFindClearSpawnCell(map, root, radius, out cell, out error);

			cell = IntVec3.Invalid;
			error = null;
			if (map == null)
			{
				error = new
				{
					success = false,
					error = "No current map is loaded."
				};
				return false;
			}

			if (root.InBounds(map) == false)
			{
				error = new
				{
					success = false,
					error = $"Cell ({root.x}, {root.z}) is outside the current map."
				};
				return false;
			}

			foreach (var candidate in GenRadial.RadialCellsAround(root, radius, true))
			{
				if (candidate.InBounds(map) == false)
					continue;
				if (candidate.Standable(map) == false)
					continue;
				if (candidate.Fogged(map))
					continue;
				if (candidate.GetTerrain(map)?.IsWater == true)
					continue;
				if (candidate.GetThingList(map).Any(thing => thing is Pawn))
					continue;

				cell = candidate;
				return true;
			}

			error = new
			{
				success = false,
				error = $"No clear dry standable unfogged cell was found near ({root.x}, {root.z})."
			};
			return false;
		}

		static Zombie SpawnFormerZombieForDamage(Map map, IntVec3 cell, List<Thing> spawnedThings)
		{
			var beforeIds = CurrentZombies(map)
				.Select(ZombieRuntimeActions.StableThingId)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
			var pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(pawn, cell, map, Rot4.South);
			DisablePawnWork(pawn);
			pawn.apparel?.DestroyAll();
			pawn.equipment?.DestroyAllEquipment(DestroyMode.Vanish);
			pawn.inventory?.DestroyAll();
			ZombieRuntimeActions.ConvertPawnToZombie(pawn, map, true);
			var former = CurrentZombies(map)
				.OfType<Zombie>()
				.Where(zombie => beforeIds.Contains(ZombieRuntimeActions.StableThingId(zombie)) == false)
				.OrderBy(zombie => zombie.Position.DistanceToSquared(cell))
				.FirstOrDefault();
			if (former != null)
				spawnedThings.Add(former);
			return former;
		}

		static object VerifySpecialDamageSpitterScaling(ZombieSpitter meleeSpitter, ZombieSpitter rangedSpitter)
		{
			var factor = 6f - ZombieSettings.Values.spitterThreat;
			var meleePart = FindNonHeadPart(meleeSpitter);
			var rangedPart = FindNonHeadPart(rangedSpitter);
			var meleeBefore = TotalInjurySeverity(meleeSpitter);
			var rangedBefore = TotalInjurySeverity(rangedSpitter);
			var meleeDamage = ApplyPawnDamage(meleeSpitter, DamageDefOf.Cut, 2f, meleePart, 6501, 0f);
			var rangedDamage = ApplyPawnDamage(rangedSpitter, DamageDefOf.Bullet, 10f, rangedPart, 6502, 0f);
			var meleeAfter = TotalInjurySeverity(meleeSpitter);
			var rangedAfter = TotalInjurySeverity(rangedSpitter);
			return new
			{
				success = meleeDamage.totalDamage > rangedDamage.totalDamage * 2f
					&& meleeDamage.injuryDelta > rangedDamage.injuryDelta * 2f,
				factor,
				meleeSpitter = DescribePawn(meleeSpitter),
				rangedSpitter = DescribePawn(rangedSpitter),
				meleePart = DescribeBodyPartDetail(meleePart),
				rangedPart = DescribeBodyPartDetail(rangedPart),
				meleeBefore,
				meleeAfter,
				meleeInjuryDelta = meleeAfter - meleeBefore,
				rangedBefore,
				rangedAfter,
				rangedInjuryDelta = rangedAfter - rangedBefore,
				meleeDamage,
				rangedDamage
			};
		}

		static object VerifySpecialDamageFormerReduction(Zombie normal, Zombie former)
		{
			var normalPart = FindNonHeadPart(normal);
			var formerPart = FindNonHeadPart(former);
			var normalDamage = ApplyPawnDamage(normal, DamageDefOf.Bullet, 12f, normalPart, 6503, 0f);
			var formerDamage = ApplyPawnDamage(former, DamageDefOf.Bullet, 12f, formerPart, 6504, 0f);
			return new
			{
				success = former.wasMapPawnBefore
					&& normal.wasMapPawnBefore == false
					&& normalDamage.totalDamage > 0f
					&& formerDamage.totalDamage > 0f
					&& formerDamage.totalDamage < normalDamage.totalDamage,
				normal = DescribeZombie(normal),
				former = DescribeZombie(former),
				normalPart = DescribeBodyPartDetail(normalPart),
				formerPart = DescribeBodyPartDetail(formerPart),
				normalDamage,
				formerDamage
			};
		}

		static object VerifySpecialDamageAlbinoGating(Zombie albino, Zombie explosiveAlbino)
		{
			var part = FindNonHeadPart(albino);
			var explosivePart = FindNonHeadPart(explosiveAlbino);
			var bulletTotals = new List<float>();
			var bulletInjuryDeltas = new List<float>();
			for (var i = 0; i < 24; i++)
			{
				var before = TotalInjurySeverity(albino);
				var result = ApplyPawnDamage(albino, DamageDefOf.Bullet, 1f, part, 6505 + i, 0f);
				var after = TotalInjurySeverity(albino);
				bulletTotals.Add(result.totalDamage);
				bulletInjuryDeltas.Add(after - before);
				if (bulletTotals.Any(total => total > 0f) && bulletTotals.Any(total => total <= 0f))
					break;
			}

			var explosiveDamage = ApplyPawnDamage(explosiveAlbino, DamageDefOf.Bomb, 1f, explosivePart, 6530, 0f);
			var bulletHits = bulletTotals.Count(total => total > 0f);
			var bulletBlocked = bulletTotals.Count(total => total <= 0f);
			return new
			{
				success = albino.isAlbino
					&& explosiveAlbino.isAlbino
					&& bulletHits > 0
					&& bulletBlocked > 0
					&& explosiveDamage.totalDamage > 0f,
				albino = DescribeZombie(albino),
				explosiveAlbino = DescribeZombie(explosiveAlbino),
				part = DescribeBodyPartDetail(part),
				explosivePart = DescribeBodyPartDetail(explosivePart),
				bulletAttempts = bulletTotals.Count,
				bulletHits,
				bulletBlocked,
				bulletTotals = bulletTotals.ToArray(),
				bulletInjuryDeltas = bulletInjuryDeltas.ToArray(),
				explosiveDamage
			};
		}

		static object VerifySpecialDamageDarkSlimerSmoke(Map map, Zombie darkSlimer)
		{
			var part = FindNonHeadPart(darkSlimer);
			var position = darkSlimer.Position;
			var smokeRadius = 1f + Tools.Difficulty();
			var countRadius = smokeRadius + 1f;
			var ticksToRun = Math.Max(1, (int)Math.Ceiling(smokeRadius * 1.5f) + 2);
			var tarSmokeThingsBefore = CountThingsNear(map, position, CustomDefs.TarSmoke, countRadius);
			var gasAtPositionBefore = position.GetGas(map)?.def?.defName;
			var damage = ApplyPawnDamage(darkSlimer, DamageDefOf.Bullet, 1f, part, 6531, 0f);
			AdvanceGameTicks(ticksToRun);
			var tarSmokeThingsAfter = CountThingsNear(map, position, CustomDefs.TarSmoke, countRadius);
			var gasAtPositionAfter = position.GetGas(map)?.def?.defName;
			return new
			{
				success = darkSlimer.isDarkSlimer
					&& tarSmokeThingsAfter > tarSmokeThingsBefore
					&& gasAtPositionAfter == CustomDefs.TarSmoke.defName,
				darkSlimer = DescribeZombie(darkSlimer),
				part = DescribeBodyPartDetail(part),
				position = ZombieRuntimeActions.DescribeCell(position),
				smokeRadius,
				countRadius,
				ticksToRun,
				gasAtPositionBefore,
				gasAtPositionAfter,
				tarSmokeThingsBefore,
				tarSmokeThingsAfter,
				tarSmokeThingDelta = tarSmokeThingsAfter - tarSmokeThingsBefore,
				damage
			};
		}

		static object VerifySpecialDamageElectricAbsorption(Zombie rangedElectric, Zombie cutElectric)
		{
			var rangedPart = FindNonHeadPart(rangedElectric);
			var cutPart = FindNonHeadPart(cutElectric);
			var rangedAbsorbBefore = rangedElectric.absorbAttack.Count;
			var rangedActiveBefore = rangedElectric.IsActiveElectric;
			var cutActiveBefore = cutElectric.IsActiveElectric;
			var rangedInWater = rangedElectric.InWater();
			var cutInWater = cutElectric.InWater();
			var rangedDamage = ApplyPawnDamage(rangedElectric, DamageDefOf.Bullet, 4f, rangedPart, 6532, 45f);
			var rangedAbsorbAfter = rangedElectric.absorbAttack.Count;
			var cutDamage = ApplyPawnDamage(cutElectric, DamageDefOf.Cut, 1f, cutPart, 6533, 0f);
			return new
			{
				success = rangedActiveBefore
					&& cutActiveBefore
					&& rangedDamage.totalDamage <= 0f
					&& rangedAbsorbAfter > rangedAbsorbBefore
					&& cutDamage.totalDamage > 0f,
				rangedElectric = DescribeZombie(rangedElectric),
				cutElectric = DescribeZombie(cutElectric),
				rangedActiveBefore,
				cutActiveBefore,
				rangedInWater,
				cutInWater,
				rangedTerrain = rangedElectric.Position.GetTerrain(rangedElectric.Map)?.defName,
				cutTerrain = cutElectric.Position.GetTerrain(cutElectric.Map)?.defName,
				rangedPart = DescribeBodyPartDetail(rangedPart),
				cutPart = DescribeBodyPartDetail(cutPart),
				rangedAbsorbBefore,
				rangedAbsorbAfter,
				rangedAbsorbDelta = rangedAbsorbAfter - rangedAbsorbBefore,
				rangedAbsorbAttack = rangedElectric.absorbAttack.Select(pair => new { angle = pair.Key, index = pair.Value }).ToArray(),
				rangedDamage,
				cutDamage
			};
		}

		static object VerifySpecialDamageTankyArmor(Pawn humanControl, Zombie shieldTanky, Zombie helmetTanky, Zombie suitTanky)
		{
			shieldTanky.hasTankyShield = 1f;
			shieldTanky.hasTankyHelmet = -1f;
			shieldTanky.hasTankySuit = -1f;
			helmetTanky.hasTankyShield = -1f;
			helmetTanky.hasTankyHelmet = 1f;
			helmetTanky.hasTankySuit = -1f;
			suitTanky.hasTankyShield = -1f;
			suitTanky.hasTankyHelmet = -1f;
			suitTanky.hasTankySuit = 1f;

			var humanPart = FindNonHeadPart(humanControl);
			var shieldPart = FindNonHeadPart(shieldTanky);
			var helmetPart = FindHeadshotPart(helmetTanky);
			var suitPart = FindNonHeadPart(suitTanky);
			var human = InvokeArmorUtilityProbe(humanControl, humanPart, 10f, 0f, 6540);
			var shield = InvokeArmorUtilityProbe(shieldTanky, shieldPart, 10f, 0f, 6541);
			var helmet = InvokeArmorUtilityProbe(helmetTanky, helmetPart, 5f, 0f, 6542);
			var suit = InvokeArmorUtilityProbe(suitTanky, suitPart, 10f, 0f, 6543);

			return new
			{
				success = human.result > 0f
					&& human.deflected == false
					&& human.diminished == false
					&& shield.result <= 0f
					&& shield.deflected
					&& shield.diminished
					&& shield.armorAfter.shield > 0f
					&& shield.armorAfter.shield < shield.armorBefore.shield
					&& helmet.result <= 0f
					&& helmet.deflected
					&& helmet.diminished
					&& helmet.armorAfter.helmet > 0f
					&& helmet.armorAfter.helmet < helmet.armorBefore.helmet
					&& suit.result <= 0f
					&& suit.deflected
					&& suit.diminished
					&& suit.armorAfter.suit > 0f
					&& suit.armorAfter.suit < suit.armorBefore.suit,
				humanControl = DescribePawn(humanControl),
				shieldTanky = DescribeZombie(shieldTanky),
				helmetTanky = DescribeZombie(helmetTanky),
				suitTanky = DescribeZombie(suitTanky),
				human,
				shield,
				helmet,
				suit
			};
		}

		static ArmorUtilityProbeResult InvokeArmorUtilityProbe(Pawn pawn, BodyPartRecord part, float amount, float armorPenetration, int seed)
		{
			var damageDef = DamageDefOf.Bullet;
			var armorBefore = pawn is Zombie zombieBefore ? DescribeTankyArmorSnapshot(zombieBefore) : null;
			float result;
			bool deflected;
			bool diminished;
			Rand.PushState(seed);
			try
			{
				result = ArmorUtility.GetPostArmorDamage(pawn, amount, armorPenetration, part, ref damageDef, out deflected, out diminished);
			}
			finally
			{
				Rand.PopState();
			}

			return new ArmorUtilityProbeResult
			{
				pawn = DescribePawn(pawn),
				part = DescribeBodyPartDetail(part),
				inputAmount = amount,
				armorPenetration = armorPenetration,
				seed = seed,
				damageDef = damageDef?.defName,
				result = result,
				deflected = deflected,
				diminished = diminished,
				armorBefore = armorBefore,
				armorAfter = pawn is Zombie zombieAfter ? DescribeTankyArmorSnapshot(zombieAfter) : null
			};
		}

		static TankyArmorSnapshot DescribeTankyArmorSnapshot(Zombie zombie)
		{
			return zombie == null
				? null
				: new TankyArmorSnapshot
				{
					shield = zombie.hasTankyShield,
					helmet = zombie.hasTankyHelmet,
					suit = zombie.hasTankySuit,
					isTanky = zombie.IsTanky
				};
		}

		sealed class TankyArmorSnapshot
		{
			public float shield { get; set; }
			public float helmet { get; set; }
			public float suit { get; set; }
			public bool isTanky { get; set; }
		}

		sealed class ArmorUtilityProbeResult
		{
			public object pawn { get; set; }
			public object part { get; set; }
			public float inputAmount { get; set; }
			public float armorPenetration { get; set; }
			public int seed { get; set; }
			public string damageDef { get; set; }
			public float result { get; set; }
			public bool deflected { get; set; }
			public bool diminished { get; set; }
			public TankyArmorSnapshot armorBefore { get; set; }
			public TankyArmorSnapshot armorAfter { get; set; }
		}

		static DamageProbeResult ApplyPawnDamage(Pawn pawn, DamageDef damageDef, float amount, BodyPartRecord part, int seed, float angle)
		{
			var before = TotalInjurySeverity(pawn);
			DamageWorker.DamageResult result;
			Rand.PushState(seed);
			try
			{
				var dinfo = new DamageInfo(damageDef, amount, 0f, angle, null, part, null, DamageInfo.SourceCategory.ThingOrUnknown, null, true, true);
				result = pawn.TakeDamage(dinfo);
			}
			finally
			{
				Rand.PopState();
			}
			var after = TotalInjurySeverity(pawn);
			return new DamageProbeResult
			{
				damageDef = damageDef?.defName,
				inputAmount = amount,
				seed = seed,
				angle = angle,
				totalDamage = result.totalDamageDealt,
				partsHit = result.parts?.Select(DescribeBodyPartDetail).ToArray() ?? Array.Empty<object>(),
				headshot = result.headshot,
				deflected = result.deflected,
				diminished = result.diminished,
				injuryBefore = before,
				injuryAfter = after,
				injuryDelta = after - before
			};
		}

		static DamageProbeResult ApplyPawnDamageUntilInjured(Pawn pawn, DamageDef damageDef, float amount, BodyPartRecord part, int seed, float angle)
		{
			var result = default(DamageProbeResult);
			for (var attempt = 0; attempt < 24; attempt++)
			{
				result = ApplyPawnDamage(pawn, damageDef, amount, part, seed + attempt, angle);
				if (result.injuryDelta > 0f)
					return result;
			}
			return result;
		}

		sealed class DamageProbeResult
		{
			public string damageDef { get; set; }
			public float inputAmount { get; set; }
			public int seed { get; set; }
			public float angle { get; set; }
			public float totalDamage { get; set; }
			public object[] partsHit { get; set; }
			public bool headshot { get; set; }
			public bool deflected { get; set; }
			public bool diminished { get; set; }
			public float injuryBefore { get; set; }
			public float injuryAfter { get; set; }
			public float injuryDelta { get; set; }
		}

		sealed class SpitterReadinessDamageResult
		{
			public bool success { get; set; }
			public string label { get; set; }
			public string error { get; set; }
			public float durabilityFactor { get; set; }
			public object spitter { get; set; }
			public object rangedPart { get; set; }
			public object meleePart { get; set; }
			public float bulletScaledAmount { get; set; }
			public float cutScaledAmount { get; set; }
			public float bulletInjuryDelta { get; set; }
			public float cutInjuryDelta { get; set; }
			public DamageProbeResult bulletDamage { get; set; }
			public DamageProbeResult cutDamage { get; set; }
		}

		static object VerifyAreaWorkflowUiState(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			var settingsSnapshot = SnapshotZombieSettings();
			var labelPatchTargets = PatchedMethodsForPatchClass("GenMapUI_DrawPawnLabel_Patch");
			var inspectPatchTargets = PatchedMethodsForPatchClass("MainTabWindow_Inspect_CurTabs_Patch");
			var downedPatchTargets = PatchedMethodsForPatchClass("Pawn_Downed_Patch");
			try
			{
				if (FindUiStateCells(map, root, out var cells, out var cellError) == false)
					return cellError;

				var ordinaryZombie = SpawnTargetZombie(map, cells[0], ZombieType.Normal, "ZL_Area_UiStateOrdinaryZombie", spawnedThings);
				var formerZombie = SpawnTargetZombie(map, cells[1], ZombieType.Normal, "ZL_Area_UiStateFormerZombie", spawnedThings);
				var inspectHuman = SpawnAreaWorkflowPawn(map, "ZL_Area_UiStateInspectHuman", cells[2], Faction.OfPlayer, spawnedThings);
				var downedZombie = SpawnTargetZombie(map, cells[3], ZombieType.Normal, "ZL_Area_UiStateDownedZombie", spawnedThings);
				var downedHuman = SpawnAreaWorkflowPawn(map, "ZL_Area_UiStateDownedHuman", cells[4], Faction.OfPlayer, spawnedThings);
				if (ordinaryZombie == null || formerZombie == null || inspectHuman == null || downedZombie == null || downedHuman == null)
				{
					return new
					{
						success = false,
						ordinaryZombie = DescribeZombie(ordinaryZombie),
						formerZombie = DescribeZombie(formerZombie),
						inspectHuman = DescribePawn(inspectHuman),
						downedZombie = DescribeZombie(downedZombie),
						downedHuman = DescribePawn(downedHuman),
						error = "Could not create all UI-state fixtures."
					};
				}

				formerZombie.wasMapPawnBefore = true;
				var labels = VerifyUiStateLabels(ordinaryZombie, formerZombie, inspectHuman);
				var inspectTabs = VerifyUiStateInspectTabs(ordinaryZombie, inspectHuman);
				var downedState = VerifyUiStateDownedFacade(downedZombie, downedHuman);

				return new
				{
					success = labelPatchTargets.Length > 0
						&& inspectPatchTargets.Length > 0
						&& downedPatchTargets.Length > 0
						&& ObjectSuccess(labels)
						&& ObjectSuccess(inspectTabs)
						&& ObjectSuccess(downedState),
					patchTargets = new
					{
						labels = labelPatchTargets,
						inspectTabs = inspectPatchTargets,
						downed = downedPatchTargets
					},
					fixtures = new
					{
						ordinaryZombie = DescribeZombie(ordinaryZombie),
						formerZombie = DescribeZombie(formerZombie),
						inspectHuman = DescribePawn(inspectHuman),
						downedZombie = DescribeZombie(downedZombie),
						downedHuman = DescribePawn(downedHuman)
					},
					labels,
					inspectTabs,
					downedState
				};
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
			}
		}

		static bool FindUiStateCells(Map map, IntVec3 root, out IntVec3[] cells, out object error)
		{
			cells = GenRadial.RadialCellsAround(root, 18f, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.OrderBy(cell => cell.DistanceToSquared(root))
				.Take(5)
				.ToArray();
			if (cells.Length >= 5)
			{
				error = null;
				return true;
			}

			error = new
			{
				success = false,
				root = ZombieRuntimeActions.DescribeCell(root),
				foundCells = cells.Length,
				error = "Could not find enough cells for the UI-state fixture."
			};
			return false;
		}

		static object VerifyUiStateLabels(Zombie ordinaryZombie, Zombie formerZombie, Pawn ordinaryPawn)
		{
			var prefix = FindNestedPatchMethod("GenMapUI_DrawPawnLabel_Patch", "Prefix");
			if (prefix == null)
			{
				return new
				{
					success = false,
					error = "Could not find GenMapUI_DrawPawnLabel_Patch.Prefix."
				};
			}

			var ordinaryZombieContinues = (bool)prefix.Invoke(null, new object[] { ordinaryZombie });
			var formerZombieContinues = (bool)prefix.Invoke(null, new object[] { formerZombie });
			var ordinaryPawnContinues = (bool)prefix.Invoke(null, new object[] { ordinaryPawn });
			return new
			{
				success = ordinaryZombie.wasMapPawnBefore == false
					&& ordinaryZombieContinues == false
					&& formerZombie.wasMapPawnBefore
					&& formerZombieContinues
					&& ordinaryPawnContinues,
				ordinaryZombie = new
				{
					wasMapPawnBefore = ordinaryZombie.wasMapPawnBefore,
					continueDraw = ordinaryZombieContinues
				},
				formerZombie = new
				{
					wasMapPawnBefore = formerZombie.wasMapPawnBefore,
					continueDraw = formerZombieContinues
				},
				ordinaryPawn = new
				{
					def = ordinaryPawn?.def?.defName,
					continueDraw = ordinaryPawnContinues
				}
			};
		}

		static object VerifyUiStateInspectTabs(Zombie zombie, Pawn ordinaryPawn)
		{
			IEnumerable<InspectTabBase> SelectAndReadTabs(object selected)
			{
				Find.Selector.ClearSelection();
				Find.Selector.Select(selected);
				return new MainTabWindow_Inspect().CurTabs;
			}

			var postfix = FindNestedPatchMethod("MainTabWindow_Inspect_CurTabs_Patch", "Postfix");
			var nullArgs = new object[] { null };
			postfix?.Invoke(null, nullArgs);
			var nullAfterPostfix = nullArgs[0] as IEnumerable<InspectTabBase>;
			var sentinel = new List<InspectTabBase>();
			var sentinelArgs = new object[] { sentinel };
			postfix?.Invoke(null, sentinelArgs);
			var sentinelAfterPostfix = sentinelArgs[0] as IEnumerable<InspectTabBase>;

			var zombieTabs = SelectAndReadTabs(zombie);
			var ordinaryTabs = SelectAndReadTabs(ordinaryPawn);
			var zombieTabsArray = zombieTabs?.ToArray();
			var ordinaryTabsArray = ordinaryTabs?.ToArray();

			return new
			{
				success = postfix != null
					&& nullAfterPostfix != null
					&& ReferenceEquals(sentinel, sentinelAfterPostfix)
					&& zombieTabsArray != null
					&& ordinaryTabsArray != null,
				postfixFound = postfix != null,
				nullPostfix = new
				{
					returnedNull = nullAfterPostfix == null,
					count = nullAfterPostfix?.Count() ?? -1
				},
				nonNullPostfixPreservesReference = ReferenceEquals(sentinel, sentinelAfterPostfix),
				zombieSelection = new
				{
					selected = StableId(zombie),
					tabsNull = zombieTabsArray == null,
					tabCount = zombieTabsArray?.Length ?? -1,
					tabTypes = zombieTabsArray?.Select(tab => tab?.GetType().Name ?? "<null>").ToArray() ?? Array.Empty<string>()
				},
				ordinarySelection = new
				{
					selected = StableId(ordinaryPawn),
					tabsNull = ordinaryTabsArray == null,
					tabCount = ordinaryTabsArray?.Length ?? -1,
					tabTypes = ordinaryTabsArray?.Select(tab => tab?.GetType().Name ?? "<null>").ToArray() ?? Array.Empty<string>()
				}
			};
		}

		static object VerifyUiStateDownedFacade(Zombie zombie, Pawn ordinaryPawn)
		{
			if (TryMakeDownedForCombat(zombie, out var zombieDownedError) == false)
			{
				return new
				{
					success = false,
					zombie = DescribeZombie(zombie),
					error = zombieDownedError
				};
			}
			if (TryMakeDownedForCombat(ordinaryPawn, out var ordinaryDownedError) == false)
			{
				return new
				{
					success = false,
					ordinaryPawn = DescribePawn(ordinaryPawn),
					error = ordinaryDownedError
				};
			}

			ApplyZombieSettingsOverride(settings => settings.doubleTapRequired = true);
			var zombieHealthDownedWithDoubleTap = zombie.health.Downed;
			var zombiePublicDownedWithDoubleTap = zombie.Downed;
			var ordinaryPublicDownedWithDoubleTap = ordinaryPawn.Downed;

			ApplyZombieSettingsOverride(settings => settings.doubleTapRequired = false);
			var zombiePublicDownedWithoutDoubleTap = zombie.Downed;
			var ordinaryPublicDownedWithoutDoubleTap = ordinaryPawn.Downed;

			return new
			{
				success = zombieHealthDownedWithDoubleTap
					&& zombiePublicDownedWithDoubleTap == false
					&& zombiePublicDownedWithoutDoubleTap
					&& ordinaryPublicDownedWithDoubleTap
					&& ordinaryPublicDownedWithoutDoubleTap,
				zombie = new
				{
					healthDowned = zombieHealthDownedWithDoubleTap,
					publicDownedWithDoubleTap = zombiePublicDownedWithDoubleTap,
					publicDownedWithoutDoubleTap = zombiePublicDownedWithoutDoubleTap
				},
				ordinaryPawn = new
				{
					publicDownedWithDoubleTap = ordinaryPublicDownedWithDoubleTap,
					publicDownedWithoutDoubleTap = ordinaryPublicDownedWithoutDoubleTap
				}
			};
		}

		static object VerifyAreaWorkflowDownedCrawlerVisuals(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			var wigglerPatchTargets = PatchedMethodsForPatchClass("PawnDownedWiggler_WigglerTick_Patch");
			var bodyAnglePatchTargets = PatchedMethodsForPatchClass("PawnRenderer_BodyAngle_Patch");
			var processPostTickVisuals = AccessTools.Method(typeof(PawnDownedWiggler), nameof(PawnDownedWiggler.ProcessPostTickVisuals), new[] { typeof(int) });
			var wigglerField = AccessTools.Field(typeof(PawnRenderer), "wiggler");
			var destinationField = AccessTools.Field(typeof(Pawn_PathFollower), "destination");
			var bodyAnglePrefix = FindNestedPatchMethod("PawnRenderer_BodyAngle_Patch", "Prefix");
			if (processPostTickVisuals == null || wigglerField == null || destinationField == null || bodyAnglePrefix == null)
			{
				return new
				{
					success = false,
					error = "Could not reflect all downed-crawler visual patch probe members.",
					reflection = new
					{
						processPostTickVisuals = processPostTickVisuals != null,
						wigglerField = wigglerField != null,
						destinationField = destinationField != null,
						bodyAnglePrefix = bodyAnglePrefix != null
					},
					patchTargets = new
					{
						wiggler = wigglerPatchTargets,
						bodyAngle = bodyAnglePatchTargets
					}
				};
			}

			if (FindDownedCrawlerVisualCells(map, root, out var cells, out var cellError) == false)
				return cellError;

			var downedZombie = SpawnTargetZombie(map, cells[0], ZombieType.Normal, "ZL_Area_DownedVisualZombie", spawnedThings);
			var standingZombie = SpawnTargetZombie(map, cells[1], ZombieType.Normal, "ZL_Area_DownedVisualStandingZombie", spawnedThings);
			var downedHuman = SpawnAreaWorkflowPawn(map, "ZL_Area_DownedVisualHuman", cells[2], Faction.OfPlayer, spawnedThings);
			if (downedZombie == null || standingZombie == null || downedHuman == null)
			{
				return new
				{
					success = false,
					downedZombie = DescribeZombie(downedZombie),
					standingZombie = DescribeZombie(standingZombie),
					downedHuman = DescribePawn(downedHuman),
					error = "Could not create all downed-crawler visual fixtures."
				};
			}

			if (TryMakeDownedForCombat(downedZombie, out var downedZombieError) == false)
				return new { success = false, downedZombie = DescribeZombie(downedZombie), error = downedZombieError };
			if (TryMakeDownedForCombat(downedHuman, out var downedHumanError) == false)
				return new { success = false, downedHuman = DescribePawn(downedHuman), error = downedHumanError };

			var renderer = downedZombie.Drawer?.renderer;
			var standingRenderer = standingZombie.Drawer?.renderer;
			var humanRenderer = downedHuman.Drawer?.renderer;
			var zombieWiggler = wigglerField.GetValue(renderer) as PawnDownedWiggler;
			var standingWiggler = wigglerField.GetValue(standingRenderer) as PawnDownedWiggler;
			var humanWiggler = wigglerField.GetValue(humanRenderer) as PawnDownedWiggler;
			if (renderer == null || standingRenderer == null || humanRenderer == null || zombieWiggler == null || standingWiggler == null || humanWiggler == null)
			{
				return new
				{
					success = false,
					downedZombie = DescribeZombie(downedZombie),
					standingZombie = DescribeZombie(standingZombie),
					downedHuman = DescribePawn(downedHuman),
					error = "Could not access all pawn renderers and downed wigglers."
				};
			}

			var destinationCell = cells[3];
			destinationField.SetValue(downedZombie.pather, new LocalTargetInfo(destinationCell));
			zombieWiggler.downedAngle = 12.5f;
			processPostTickVisuals.Invoke(zombieWiggler, new object[] { 1 });
			var expectedWiggleAngle = ExpectedDownedCrawlerWiggleAngle(downedZombie, destinationCell);
			var wiggleAngleAfterPostTick = zombieWiggler.downedAngle;
			var wigglerUpdated = AnglesApproximatelyEqual(wiggleAngleAfterPostTick, expectedWiggleAngle, 0.05f);

			downedZombie.currentDownedAngle = -1f;
			var firstAngle = renderer.BodyAngle(PawnRenderFlags.None);
			var expectedFirstAngle = zombieWiggler.downedAngle + 360f;
			var firstAngleMatches = AnglesApproximatelyEqual(firstAngle, expectedFirstAngle, 0.05f)
				&& AnglesApproximatelyEqual(downedZombie.currentDownedAngle, expectedFirstAngle, 0.05f);

			zombieWiggler.downedAngle += 48f;
			var wiggleAngleAfterShift = zombieWiggler.downedAngle;
			var expectedSecondAngle = (expectedFirstAngle * 15f + wiggleAngleAfterShift + 360f) / 16f;
			var secondAngle = renderer.BodyAngle(PawnRenderFlags.None);
			var secondAngleMatches = AnglesApproximatelyEqual(secondAngle, expectedSecondAngle, 0.05f)
				&& AnglesApproximatelyEqual(downedZombie.currentDownedAngle, expectedSecondAngle, 0.05f);

			var downedZombiePrefixArgs = new object[] { downedZombie, zombieWiggler, 0f };
			var downedZombiePrefixContinues = (bool)bodyAnglePrefix.Invoke(null, downedZombiePrefixArgs);
			var downedZombiePrefixAngle = (float)downedZombiePrefixArgs[2];
			var standingZombiePrefixArgs = new object[] { standingZombie, standingWiggler, 0f };
			var standingZombiePrefixContinues = (bool)bodyAnglePrefix.Invoke(null, standingZombiePrefixArgs);
			var downedHumanPrefixArgs = new object[] { downedHuman, humanWiggler, 0f };
			var downedHumanPrefixContinues = (bool)bodyAnglePrefix.Invoke(null, downedHumanPrefixArgs);

			return new
			{
				success = wigglerPatchTargets.Length > 0
					&& bodyAnglePatchTargets.Length > 0
					&& wigglerUpdated
					&& firstAngleMatches
					&& secondAngleMatches
					&& downedZombiePrefixContinues == false
					&& downedZombiePrefixAngle > 0f
					&& standingZombiePrefixContinues
					&& downedHumanPrefixContinues,
				patchTargets = new
				{
					wiggler = wigglerPatchTargets,
					bodyAngle = bodyAnglePatchTargets
				},
				fixtures = new
				{
					downedZombie = DescribeZombie(downedZombie),
					standingZombie = DescribeZombie(standingZombie),
					downedHuman = DescribePawn(downedHuman),
					destination = ZombieRuntimeActions.DescribeCell(destinationCell)
				},
				wiggler = new
				{
					destination = ZombieRuntimeActions.DescribeCell(((LocalTargetInfo)destinationField.GetValue(downedZombie.pather)).Cell),
					angleAfterPostTick = wiggleAngleAfterPostTick,
					expectedAngle = expectedWiggleAngle,
					angleAfterSmoothingProbeShift = wiggleAngleAfterShift,
					updated = wigglerUpdated
				},
				bodyAngle = new
				{
					firstAngle,
					expectedFirstAngle,
					firstAngleMatches,
					secondAngle,
					expectedSecondAngle,
					secondAngleMatches,
					currentDownedAngle = downedZombie.currentDownedAngle
				},
				prefixBranches = new
				{
					downedZombie = new { continues = downedZombiePrefixContinues, result = downedZombiePrefixAngle },
					standingZombie = new { continues = standingZombiePrefixContinues },
					downedHuman = new { continues = downedHumanPrefixContinues }
				}
			};
		}

		static bool FindDownedCrawlerVisualCells(Map map, IntVec3 root, out IntVec3[] cells, out object error)
		{
			cells = GenRadial.RadialCellsAround(root, 22f, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.OrderBy(cell => cell.DistanceToSquared(root))
				.Take(4)
				.ToArray();
			if (cells.Length >= 4)
			{
				error = null;
				return true;
			}

			error = new
			{
				success = false,
				root = ZombieRuntimeActions.DescribeCell(root),
				foundCells = cells.Length,
				error = "Could not find enough cells for the downed-crawler visual fixture."
			};
			return false;
		}

		static float ExpectedDownedCrawlerWiggleAngle(Pawn pawn, IntVec3 destinationCell)
		{
			var vec = destinationCell - pawn.Position;
			var pos = pawn.DrawPos;
			return vec.AngleFlat + 15f * Mathf.Sin(6f * pos.x) * Mathf.Cos(6f * pos.z);
		}

		static bool AnglesApproximatelyEqual(float actual, float expected, float tolerance)
		{
			return Mathf.Abs(actual - expected) <= tolerance;
		}

		static object VerifyAreaWorkflowRootPlayHooks(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			var rootUpdateTargets = PatchedMethodsForPatchClass("Root_Play_Update_Patch");
			var quickTestTargets = PatchedMethodsForPatchClass("Root_Play_SetupForQuickTestPlay_Patch");
			var rootUpdatePostfix = FindNestedPatchMethod("Root_Play_Update_Patch", "Postfix");
			var quickTestPostfix = FindNestedPatchMethod("Root_Play_SetupForQuickTestPlay_Patch", "Postfix");
			var rootUpdateMethod = AccessTools.Method(typeof(Root_Play), nameof(Root_Play.Update));
			var quickTestMethod = AccessTools.Method(typeof(Root_Play), nameof(Root_Play.SetupForQuickTestPlay));
			var electricSustainerField = AccessTools.Field(typeof(TickManager), "electricSustainer");
			var tankSustainerField = AccessTools.Field(typeof(TickManager), "tankSustainer");
			var tickManager = map.GetComponent<TickManager>();

			if (rootUpdatePostfix == null || quickTestPostfix == null || rootUpdateMethod == null || quickTestMethod == null || electricSustainerField == null || tankSustainerField == null || tickManager == null)
			{
				return new
				{
					success = false,
					error = "Could not reflect all Root_Play hook probe members.",
					reflection = new
					{
						rootUpdatePostfix = rootUpdatePostfix != null,
						quickTestPostfix = quickTestPostfix != null,
						rootUpdateMethod = rootUpdateMethod != null,
						quickTestMethod = quickTestMethod != null,
						electricSustainerField = electricSustainerField != null,
						tankSustainerField = tankSustainerField != null,
						tickManager = tickManager != null
					},
					patchTargets = new
					{
						rootUpdate = rootUpdateTargets,
						quickTest = quickTestTargets
					}
				};
			}

			var settingsProbe = VerifyRootPlayQuickTestDefaults(quickTestPostfix);
			var soundProbe = VerifyRootPlayUpdateSoundHooks(map, tickManager, root, spawnedThings, rootUpdatePostfix, electricSustainerField, tankSustainerField);

			return new
			{
				success = rootUpdateTargets.Length > 0
					&& quickTestTargets.Length > 0
					&& ObjectSuccess(settingsProbe)
					&& ObjectSuccess(soundProbe),
				patchTargets = new
				{
					rootUpdate = rootUpdateTargets,
					quickTest = quickTestTargets
				},
				settingsProbe,
				soundProbe
			};
		}

		static object VerifyRootPlayQuickTestDefaults(MethodInfo quickTestPostfix)
		{
			var settingsSnapshot = SnapshotZombieSettings();
			var oldScrollPosition = SettingsDialog.scrollPosition;
			try
			{
				ZombieSettings.Values = new SettingsGroup
				{
					threatScale = 0.123f,
					doubleTapRequired = false,
					moveSpeedIdle = 0.987f,
					playCreepyAmbientSound = false
				};
				ZombieSettings.ValuesOverTime = new()
				{
					new SettingsKeyFrame
					{
						amount = 7,
						unit = SettingsKeyFrame.Unit.Days,
						values = ZombieSettings.Values.MakeCopy()
					}
				};
				SettingsDialog.scrollPosition = new Vector2(31f, 47f);

				quickTestPostfix.Invoke(null, Array.Empty<object>());

				var defaults = ZombieSettingsDefaults.group;
				var defaultFrames = ZombieSettingsDefaults.groupOverTime;
				var values = ZombieSettings.Values;
				var frames = ZombieSettings.ValuesOverTime;
				var valuesMatchDefaults = values != null
					&& defaults != null
					&& Mathf.Approximately(values.threatScale, defaults.threatScale)
					&& values.doubleTapRequired == defaults.doubleTapRequired
					&& Mathf.Approximately(values.moveSpeedIdle, defaults.moveSpeedIdle)
					&& values.playCreepyAmbientSound == defaults.playCreepyAmbientSound;
				var framesMatchDefaults = frames != null
					&& defaultFrames != null
					&& frames.Count == defaultFrames.Count
					&& frames.Count > 0
					&& frames[0] != null
					&& defaultFrames[0] != null
					&& frames[0].Ticks == defaultFrames[0].Ticks
					&& Mathf.Approximately(frames[0].values.threatScale, defaultFrames[0].values.threatScale);
				var scrollReset = SettingsDialog.scrollPosition == Vector2.zero;

				return new
				{
					success = valuesMatchDefaults && framesMatchDefaults && scrollReset,
					values = new
					{
						valuesMatchDefaults,
						threatScale = values?.threatScale,
						defaultThreatScale = defaults?.threatScale,
						doubleTapRequired = values?.doubleTapRequired,
						defaultDoubleTapRequired = defaults?.doubleTapRequired,
						moveSpeedIdle = values?.moveSpeedIdle,
						defaultMoveSpeedIdle = defaults?.moveSpeedIdle,
						playCreepyAmbientSound = values?.playCreepyAmbientSound,
						defaultPlayCreepyAmbientSound = defaults?.playCreepyAmbientSound
					},
					frames = new
					{
						framesMatchDefaults,
						count = frames?.Count ?? -1,
						defaultCount = defaultFrames?.Count ?? -1,
						firstTicks = frames?.FirstOrDefault()?.Ticks ?? -1,
						defaultFirstTicks = defaultFrames?.FirstOrDefault()?.Ticks ?? -1
					},
					scroll = new
					{
						scrollReset,
						position = DescribeVector(SettingsDialog.scrollPosition)
					}
				};
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
				SettingsDialog.scrollPosition = oldScrollPosition;
			}
		}

		static object VerifyRootPlayUpdateSoundHooks(
			Map map,
			TickManager tickManager,
			IntVec3 root,
			List<Thing> spawnedThings,
			MethodInfo rootUpdatePostfix,
			FieldInfo electricSustainerField,
			FieldInfo tankSustainerField)
		{
			var oldUseSound = Constants.USE_SOUND;
			var oldVolumeAmbient = Prefs.VolumeAmbient;
			var oldElectricSustainer = electricSustainerField.GetValue(tickManager) as Sustainer;
			var oldTankSustainer = tankSustainerField.GetValue(tickManager) as Sustainer;
			Sustainer spawnedElectricSustainer = null;
			Sustainer spawnedTankSustainer = null;

			try
			{
				if (FindDownedCrawlerVisualCells(map, root, out var cells, out var cellError) == false)
					return cellError;

				var electrifier = SpawnTargetZombie(map, cells[0], ZombieType.Electrifier, "ZL_Area_RootPlayElectrifier", spawnedThings);
				var tanky = SpawnTargetZombie(map, cells[1], ZombieType.TankyOperator, "ZL_Area_RootPlayTanky", spawnedThings);
				if (electrifier == null || tanky == null)
				{
					return new
					{
						success = false,
						electrifier = DescribeZombie(electrifier),
						tanky = DescribeZombie(tanky),
						error = "Could not create root-play sound hook fixtures."
					};
				}

				_ = tickManager.hummingZombies.Add(electrifier);
				_ = tickManager.tankZombies.Add(tanky);
				Constants.USE_SOUND = true;
				Prefs.VolumeAmbient = Mathf.Max(Prefs.VolumeAmbient, 0.5f);

				var attempts = 0;
				Sustainer electricSustainer = null;
				Sustainer tankSustainer = null;
				for (; attempts < 5000; attempts++)
				{
					rootUpdatePostfix.Invoke(null, Array.Empty<object>());
					electricSustainer = electricSustainerField.GetValue(tickManager) as Sustainer;
					tankSustainer = tankSustainerField.GetValue(tickManager) as Sustainer;
					if (electricSustainer != null && tankSustainer != null)
						break;
				}

				if (electricSustainer != null && ReferenceEquals(electricSustainer, oldElectricSustainer) == false)
					spawnedElectricSustainer = electricSustainer;
				if (tankSustainer != null && ReferenceEquals(tankSustainer, oldTankSustainer) == false)
					spawnedTankSustainer = tankSustainer;

				return new
				{
					success = attempts < 5000
						&& electricSustainer != null
						&& tankSustainer != null
						&& tickManager.hummingZombies.Contains(electrifier)
						&& tickManager.tankZombies.Contains(tanky),
					attempts = attempts + 1,
					fixtures = new
					{
						electrifier = DescribeZombie(electrifier),
						tanky = DescribeZombie(tanky)
					},
					sound = new
					{
						useSound = Constants.USE_SOUND,
						volumeAmbient = Prefs.VolumeAmbient,
						electricSustainerCreated = electricSustainer != null,
						tankSustainerCreated = tankSustainer != null,
						electricVolumeFactor = electricSustainer?.info.volumeFactor,
						tankVolumeFactor = tankSustainer?.info.volumeFactor
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
				if (spawnedElectricSustainer != null)
					spawnedElectricSustainer.End();
				if (spawnedTankSustainer != null)
					spawnedTankSustainer.End();
				electricSustainerField.SetValue(tickManager, oldElectricSustainer);
				tankSustainerField.SetValue(tickManager, oldTankSustainer);
				Constants.USE_SOUND = oldUseSound;
				Prefs.VolumeAmbient = oldVolumeAmbient;
			}
		}

		static object VerifyAreaWorkflowRenderNodeGraphics(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			var bodyPatchTargets = PatchedMethodsForPatchClass("PawnRenderNode_Body_GraphicFor_Patch");
			var headPatchTargets = PatchedMethodsForPatchClass("PawnRenderNode_Head_GraphicFor_Patch");
			var bodyPrefix = FindNestedPatchMethod("PawnRenderNode_Body_GraphicFor_Patch", "Prefix");
			var headPrefix = FindNestedPatchMethod("PawnRenderNode_Head_GraphicFor_Patch", "Prefix");
			var bodyMethod = AccessTools.Method(typeof(PawnRenderNode_Body), nameof(PawnRenderNode_Body.GraphicFor), new[] { typeof(Pawn) });
			var headMethod = AccessTools.Method(typeof(PawnRenderNode_Head), nameof(PawnRenderNode_Head.GraphicFor), new[] { typeof(Pawn) });
			if (bodyPrefix == null || headPrefix == null || bodyMethod == null || headMethod == null)
			{
				return new
				{
					success = false,
					error = "Could not reflect all render-node graphic probe members.",
					reflection = new
					{
						bodyPrefix = bodyPrefix != null,
						headPrefix = headPrefix != null,
						bodyMethod = bodyMethod != null,
						headMethod = headMethod != null
					},
					patchTargets = new
					{
						body = bodyPatchTargets,
						head = headPatchTargets
					}
				};
			}

			if (FindDownedCrawlerVisualCells(map, root, out var cells, out var cellError) == false)
				return cellError;

			var zombie = SpawnTargetZombie(map, cells[0], ZombieType.Normal, "ZL_Area_RenderNodeZombie", spawnedThings);
			var headlessZombie = SpawnTargetZombie(map, cells[1], ZombieType.Normal, "ZL_Area_RenderNodeHeadlessZombie", spawnedThings);
			var ordinaryHuman = SpawnAreaWorkflowPawn(map, "ZL_Area_RenderNodeHuman", cells[2], Faction.OfPlayer, spawnedThings);
			if (zombie == null || headlessZombie == null || ordinaryHuman == null)
			{
				return new
				{
					success = false,
					zombie = DescribeZombie(zombie),
					headlessZombie = DescribeZombie(headlessZombie),
					ordinaryHuman = DescribePawn(ordinaryHuman),
					error = "Could not create render-node graphic fixtures."
				};
			}

			ZombieRenderCompat.ResolveAllGraphics(zombie);
			ZombieRenderCompat.ResolveAllGraphics(headlessZombie);
			ZombieRenderCompat.ResolveAllGraphics(ordinaryHuman);

			var customBody = MakeRenderNodeProbeBodyGraphic(zombie);
			var customHead = MakeRenderNodeProbeHeadGraphic(zombie);
			var customHeadForHeadless = MakeRenderNodeProbeHeadGraphic(headlessZombie);
			if (customBody == null || customHead == null || customHeadForHeadless == null)
			{
				return new
				{
					success = false,
					zombie = DescribeZombie(zombie),
					headlessZombie = DescribeZombie(headlessZombie),
					error = "Could not create render-node probe graphics."
				};
			}

			ZombieRenderCompat.SetBodyGraphic(zombie, customBody);
			ZombieRenderCompat.SetHeadGraphic(zombie, customHead);
			ZombieRenderCompat.SetHeadGraphic(headlessZombie, customHeadForHeadless);

			var bodyNode = new PawnRenderNode_Body(zombie, new PawnRenderNodeProperties(), zombie.Drawer.renderer.renderTree);
			var headNode = new PawnRenderNode_Head(zombie, new PawnRenderNodeProperties(), zombie.Drawer.renderer.renderTree);
			var headlessHeadNode = new PawnRenderNode_Head(headlessZombie, new PawnRenderNodeProperties(), headlessZombie.Drawer.renderer.renderTree);
			var actualBodyGraphic = bodyNode.GraphicFor(zombie);
			var actualHeadGraphic = headNode.GraphicFor(zombie);

			var headRemoval = TryRemoveHeadForRenderNodeProbe(headlessZombie, out var headRemovalError);
			var headlessHeadGraphic = headlessHeadNode.GraphicFor(headlessZombie);

			var humanBodyArgs = new object[] { ordinaryHuman, null };
			var humanBodyContinues = (bool)bodyPrefix.Invoke(null, humanBodyArgs);
			var humanHeadArgs = new object[] { ordinaryHuman, null };
			var humanHeadContinues = (bool)headPrefix.Invoke(null, humanHeadArgs);
			var headlessHeadArgs = new object[] { headlessZombie, customHeadForHeadless };
			var headlessHeadContinues = (bool)headPrefix.Invoke(null, headlessHeadArgs);
			var headlessHeadPrefixResult = headlessHeadArgs[1] as Graphic;

			return new
			{
				success = bodyPatchTargets.Length > 0
					&& headPatchTargets.Length > 0
					&& ReferenceEquals(actualBodyGraphic, customBody)
					&& ReferenceEquals(actualHeadGraphic, customHead)
					&& headRemoval
					&& headlessZombie.health.hediffSet.HasHead == false
					&& headlessHeadGraphic == null
					&& humanBodyContinues
					&& humanHeadContinues
					&& headlessHeadContinues == false
					&& headlessHeadPrefixResult == null,
				patchTargets = new
				{
					body = bodyPatchTargets,
					head = headPatchTargets
				},
				fixtures = new
				{
					zombie = DescribeZombie(zombie),
					headlessZombie = DescribeZombie(headlessZombie),
					ordinaryHuman = DescribePawn(ordinaryHuman)
				},
				customRouting = new
				{
					bodyReferenceMatched = ReferenceEquals(actualBodyGraphic, customBody),
					headReferenceMatched = ReferenceEquals(actualHeadGraphic, customHead),
					bodyGraphicType = actualBodyGraphic?.GetType().Name,
					headGraphicType = actualHeadGraphic?.GetType().Name
				},
				passThrough = new
				{
					humanBodyContinues,
					humanBodyResultStillNull = humanBodyArgs[1] == null,
					humanHeadContinues,
					humanHeadResultStillNull = humanHeadArgs[1] == null
				},
				headless = new
				{
					headRemoval,
					headRemovalError,
					hasHead = headlessZombie.health.hediffSet.HasHead,
					actualGraphicIsNull = headlessHeadGraphic == null,
					prefixContinues = headlessHeadContinues,
					prefixResultIsNull = headlessHeadPrefixResult == null
				}
			};
		}

		static Graphic MakeRenderNodeProbeBodyGraphic(Pawn pawn)
		{
			var path = pawn?.story?.bodyType?.bodyNakedGraphicPath;
			if (path.NullOrEmpty())
				return null;
			return GraphicDatabase.Get<Graphic_Multi>(path, ShaderDatabase.Cutout, Vector2.one, Color.green);
		}

		static Graphic MakeRenderNodeProbeHeadGraphic(Pawn pawn)
		{
			return pawn?.story?.headType?.GetGraphic(pawn, Color.cyan)
				?? HeadTypeDefOf.Skull.GetGraphic(pawn, Color.cyan);
		}

		static bool TryRemoveHeadForRenderNodeProbe(Pawn pawn, out string error)
		{
			error = null;
			var head = pawn?.health?.hediffSet?
				.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Undefined)
				.FirstOrDefault(part => part.def == BodyPartDefOf.Head);
			if (head == null)
			{
				error = "Could not find a non-missing head body part.";
				return false;
			}

			var missing = (Hediff_MissingPart)HediffMaker.MakeHediff(HediffDefOf.MissingBodyPart, pawn, null);
			missing.IsFresh = true;
			missing.lastInjury = HediffDefOf.Misc;
			missing.Part = head;
			pawn.health.hediffSet.AddDirect(missing, null, null);
			if (pawn.health.hediffSet.HasHead)
			{
				error = "Adding MissingBodyPart to the head did not clear HasHead.";
				return false;
			}
			return true;
		}

		sealed class ProbeSubEffecter : SubEffecter
		{
			public int triggerCount;
			public object lastTargetA;
			public object lastTargetB;
			public int lastOverrideSpawnTick;

			public ProbeSubEffecter(Effecter parent) : base(new SubEffecterDef(), parent)
			{
			}

			public override void SubTrigger(TargetInfo A, TargetInfo B, int overrideSpawnTick = -1, bool force = false)
			{
				triggerCount++;
				lastTargetA = DescribeTargetInfo(A);
				lastTargetB = DescribeTargetInfo(B);
				lastOverrideSpawnTick = overrideSpawnTick;
			}
		}

		static object VerifyAreaWorkflowEffecterSuppression(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			var patchTargets = PatchedMethodsForPatchClass("Effecter_Trigger_Patch");
			var prefix = FindNestedPatchMethod("Effecter_Trigger_Patch", "Prefix");
			var triggerMethod = AccessTools.Method(typeof(Effecter), nameof(Effecter.Trigger), new[] { typeof(TargetInfo), typeof(TargetInfo), typeof(int) });
			var childrenField = AccessTools.Field(typeof(Effecter), "children");
			if (prefix == null || triggerMethod == null || childrenField == null)
			{
				return new
				{
					success = false,
					error = "Could not reflect all effecter suppression probe members.",
					reflection = new
					{
						prefix = prefix != null,
						triggerMethod = triggerMethod != null,
						childrenField = childrenField != null
					},
					patchTargets
				};
			}

			if (FindDownedCrawlerVisualCells(map, root, out var cells, out var cellError) == false)
				return cellError;

			var zombie = SpawnTargetZombie(map, cells[0], ZombieType.Normal, "ZL_Area_EffecterZombie", spawnedThings);
			var ordinaryHuman = SpawnAreaWorkflowPawn(map, "ZL_Area_EffecterHuman", cells[1], Faction.OfPlayer, spawnedThings);
			if (zombie == null || ordinaryHuman == null)
			{
				return new
				{
					success = false,
					zombie = DescribeZombie(zombie),
					ordinaryHuman = DescribePawn(ordinaryHuman),
					error = "Could not create effecter suppression fixtures."
				};
			}

			var zombieDeflect = RunProbeEffecterCase(childrenField, EffecterDefOf.Deflect_General, zombie);
			var humanDeflect = RunProbeEffecterCase(childrenField, EffecterDefOf.Deflect_General, ordinaryHuman);
			var zombieNonDeflect = RunProbeEffecterCase(childrenField, EffecterDefOf.Mine, zombie);
			var prefixZombieDeflect = (bool)prefix.Invoke(null, new object[] { EffecterDefOf.Deflect_General, new TargetInfo(zombie) });
			var prefixHumanDeflect = (bool)prefix.Invoke(null, new object[] { EffecterDefOf.Deflect_General, new TargetInfo(ordinaryHuman) });
			var prefixZombieNonDeflect = (bool)prefix.Invoke(null, new object[] { EffecterDefOf.Mine, new TargetInfo(zombie) });

			return new
			{
				success = patchTargets.Length > 0
					&& ObjectSuccess(zombieDeflect)
					&& ObjectSuccess(humanDeflect)
					&& ObjectSuccess(zombieNonDeflect)
					&& ((EffecterProbeCase)zombieDeflect).triggerCount == 0
					&& ((EffecterProbeCase)humanDeflect).triggerCount == 1
					&& ((EffecterProbeCase)zombieNonDeflect).triggerCount == 1
					&& prefixZombieDeflect == false
					&& prefixHumanDeflect
					&& prefixZombieNonDeflect,
				patchTargets,
				fixtures = new
				{
					zombie = DescribeZombie(zombie),
					ordinaryHuman = DescribePawn(ordinaryHuman)
				},
				actualTriggerCases = new
				{
					zombieDeflect,
					humanDeflect,
					zombieNonDeflect
				},
				prefixBranches = new
				{
					zombieDeflect = prefixZombieDeflect,
					humanDeflect = prefixHumanDeflect,
					zombieNonDeflect = prefixZombieNonDeflect
				}
			};
		}

		sealed class EffecterProbeCase
		{
			public bool success { get; set; }
			public string effecterDef { get; set; }
			public object target { get; set; }
			public int triggerCount { get; set; }
			public int childCount { get; set; }
			public object lastTargetA { get; set; }
			public object lastTargetB { get; set; }
			public int lastOverrideSpawnTick { get; set; }
			public string error { get; set; }
		}

		static EffecterProbeCase RunProbeEffecterCase(FieldInfo childrenField, EffecterDef effecterDef, Thing target)
		{
			var effecter = new Effecter(effecterDef);
			var children = childrenField.GetValue(effecter) as List<SubEffecter>;
			if (children == null)
			{
				return new EffecterProbeCase
				{
					success = false,
					effecterDef = effecterDef?.defName,
					target = StableId(target),
					error = "Could not access Effecter.children."
				};
			}

			children.Clear();
			var probe = new ProbeSubEffecter(effecter);
			children.Add(probe);
			effecter.Trigger(new TargetInfo(target), TargetInfo.Invalid, 123);
			return new EffecterProbeCase
			{
				success = true,
				effecterDef = effecterDef?.defName,
				target = StableId(target),
				triggerCount = probe.triggerCount,
				childCount = children.Count,
				lastTargetA = probe.lastTargetA,
				lastTargetB = probe.lastTargetB,
				lastOverrideSpawnTick = probe.lastOverrideSpawnTick
			};
		}

		static object DescribeTargetInfo(TargetInfo target)
		{
			return new
			{
				hasThing = target.HasThing,
				thing = StableId(target.Thing),
				cell = target.Cell.IsValid ? ZombieRuntimeActions.DescribeCell(target.Cell) : null,
				map = target.Map?.uniqueID
			};
		}

		static object VerifyAreaWorkflowGraphicMultiTexture(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			var patchTargets = PatchedMethodsForPatchClass("Graphic_Multi_Init_Patch");
			var captureTextureError = FindNestedPatchMethod("Graphic_Multi_Init_Patch", "CaptureTextureError");
			var initMethod = AccessTools.Method(typeof(Graphic_Multi), nameof(Graphic_Multi.Init), new[] { typeof(GraphicRequest) });
			if (captureTextureError == null || initMethod == null)
			{
				return new
				{
					success = false,
					error = "Could not reflect all Graphic_Multi texture probe members.",
					reflection = new
					{
						captureTextureError = captureTextureError != null,
						initMethod = initMethod != null
					},
					patchTargets
				};
			}

			if (FindDownedCrawlerVisualCells(map, root, out var cells, out var cellError) == false)
				return cellError;

			var human = SpawnAreaWorkflowPawn(map, "ZL_Area_GraphicMultiHuman", cells[0], Faction.OfPlayer, spawnedThings);
			if (human?.story?.bodyType?.bodyNakedGraphicPath == null)
			{
				return new
				{
					success = false,
					human = DescribePawn(human),
					error = "Could not create a human with a usable body graphic path."
				};
			}

			var oldSuppressError = Patches.Graphic_Multi_Init_Patch.suppressError;
			var oldTextureError = Patches.Graphic_Multi_Init_Patch.textureError;
			try
			{
				Patches.Graphic_Multi_Init_Patch.suppressError = true;
				Patches.Graphic_Multi_Init_Patch.textureError = false;
				var missingPath = $"ZombielandTextureProbe/Missing_{GenTicks.TicksGame}_{human.thingIDNumber}";
				var missingGraphic = new Graphic_Multi();
				missingGraphic.Init(new GraphicRequest(
					typeof(Graphic_Multi),
					missingPath,
					ShaderDatabase.Cutout,
					Vector2.one,
					Color.white,
					Color.white,
					null,
					0,
					new List<ShaderParameter>(),
					null));
				var missingTextureError = Patches.Graphic_Multi_Init_Patch.textureError;
				var missingMatIsBadMat = ReferenceEquals(missingGraphic.MatSingle, BaseContent.BadMat);

				Patches.Graphic_Multi_Init_Patch.textureError = false;
				var validPath = human.story.bodyType.bodyNakedGraphicPath;
				var validGraphic = new Graphic_Multi();
				validGraphic.Init(new GraphicRequest(
					typeof(Graphic_Multi),
					validPath,
					ShaderDatabase.Cutout,
					Vector2.one,
					Color.white,
					Color.white,
					null,
					0,
					new List<ShaderParameter>(),
					null));
				var validTextureError = Patches.Graphic_Multi_Init_Patch.textureError;
				var validMatIsBadMat = ReferenceEquals(validGraphic.MatSingle, BaseContent.BadMat);

				Patches.Graphic_Multi_Init_Patch.textureError = false;
				captureTextureError.Invoke(null, new object[] { "suppressed probe" });
				var directCaptureSuppressedSetsFlag = Patches.Graphic_Multi_Init_Patch.textureError;

				return new
				{
					success = patchTargets.Length > 0
						&& missingTextureError
						&& missingMatIsBadMat
						&& validTextureError == false
						&& validMatIsBadMat == false
						&& directCaptureSuppressedSetsFlag,
					patchTargets,
					fixture = new
					{
						human = DescribePawn(human),
						validPath,
						missingPath
					},
					missingTexture = new
					{
						textureError = missingTextureError,
						matSingleIsBadMat = missingMatIsBadMat,
						suppressError = true
					},
					validTexture = new
					{
						textureError = validTextureError,
						matSingleIsBadMat = validMatIsBadMat
					},
					directCapture = new
					{
						suppressedSetsFlag = directCaptureSuppressedSetsFlag,
						unsuppressedBranchSource = "CaptureTextureError sets textureError and calls Patches.Error(text) only when suppressError == false."
					}
				};
			}
			finally
			{
				Patches.Graphic_Multi_Init_Patch.suppressError = oldSuppressError;
				Patches.Graphic_Multi_Init_Patch.textureError = oldTextureError;
			}
		}

		sealed class WarmupScalingProbeCase
		{
			public string name { get; set; }
			public bool success { get; set; }
			public string error { get; set; }
			public object caster { get; set; }
			public object targetCell { get; set; }
			public object verb { get; set; }
			public bool castResult { get; set; }
			public string stanceType { get; set; }
			public float warmupTime { get; set; }
			public float aimingDelayFactor { get; set; }
			public int gridCount { get; set; }
			public int divisor { get; set; }
			public int expectedTicks { get; set; }
			public int? actualTicks { get; set; }
			public int? directModifyTicks { get; set; }
			public bool isRaging { get; set; }
			public bool wasMapPawnBefore { get; set; }
		}

		static object VerifyAreaWorkflowWarmupScaling(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			var patchTargets = PatchedMethodsForPatchClass("Verb_TryStartCastOn_Patch");
			var modifyTicks = FindNestedPatchMethod("Verb_TryStartCastOn_Patch", "ModifyTicks");
			var tryStartCastOn = AccessTools.Method(
				typeof(Verb),
				nameof(Verb.TryStartCastOn),
				new[] { typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(bool), typeof(bool), typeof(bool), typeof(bool) });
			var warmupTimeField = AccessTools.Field(typeof(VerbProperties), "warmupTime");
			var ticksLeftField = AccessTools.Field(typeof(Stance_Busy), "ticksLeft");
			if (modifyTicks == null || tryStartCastOn == null || warmupTimeField == null || ticksLeftField == null)
			{
				return new
				{
					success = false,
					error = "Could not reflect all warmup-scaling probe members.",
					reflection = new
					{
						modifyTicks = modifyTicks != null,
						tryStartCastOn = tryStartCastOn != null,
						warmupTimeField = warmupTimeField != null,
						ticksLeftField = ticksLeftField != null
					},
					patchTargets
				};
			}

			if (FindWarmupScalingCells(map, root, out var targetCell, out var casterCells, out var cellError) == false)
				return cellError;

			var grid = map.GetGrid();
			var originalGridCounts = new Dictionary<IntVec3, int>();
			void RecordGridCell(IntVec3 cell)
			{
				if (cell.IsValid && originalGridCounts.ContainsKey(cell) == false)
					originalGridCounts[cell] = grid.GetZombieCount(cell);
			}

			void SetGridCount(IntVec3 cell, int count)
			{
				RecordGridCell(cell);
				var current = grid.GetZombieCount(cell);
				if (current != 0)
					grid.ChangeZombieCount(cell, -current);
				if (count != 0)
					grid.ChangeZombieCount(cell, count);
			}

			try
			{
				var human = SpawnAreaWorkflowPawn(map, "ZL_Area_WarmupHuman", casterCells[0], Faction.OfPlayer, spawnedThings);
				var normal = SpawnTargetZombie(map, casterCells[1], ZombieType.Normal, "ZL_Area_WarmupNormalZombie", spawnedThings);
				var raging = SpawnTargetZombie(map, casterCells[2], ZombieType.Normal, "ZL_Area_WarmupRagingZombie", spawnedThings);
				var former = SpawnTargetZombie(map, casterCells[3], ZombieType.Normal, "ZL_Area_WarmupFormerZombie", spawnedThings);
				if (human == null || normal == null || raging == null || former == null)
				{
					return new
					{
						success = false,
						human = DescribePawn(human),
						normal = DescribeZombie(normal),
						raging = DescribeZombie(raging),
						former = DescribeZombie(former),
						error = "Could not create all warmup-scaling fixtures."
					};
				}

				raging.raging = GenTicks.TicksAbs + 60000;
				former.raging = 0;
				former.wasMapPawnBefore = true;
				SetGridCount(normal.Position, 4);
				SetGridCount(raging.Position, 4);
				SetGridCount(former.Position, 5);

				var cases = new[]
				{
					RunWarmupScalingCase("humanControl", human, targetCell, 1, modifyTicks, warmupTimeField, ticksLeftField),
					RunWarmupScalingCase("normalZombieNoScale", normal, targetCell, 1, modifyTicks, warmupTimeField, ticksLeftField),
					RunWarmupScalingCase("ragingZombieGridScale", raging, targetCell, 4, modifyTicks, warmupTimeField, ticksLeftField),
					RunWarmupScalingCase("formerZombieGridScale", former, targetCell, 5, modifyTicks, warmupTimeField, ticksLeftField)
				};

				return new
				{
					success = patchTargets.Length > 0
						&& cases.All(entry => entry.success)
						&& cases[0].actualTicks == cases[1].actualTicks
						&& cases[2].actualTicks < cases[1].actualTicks
						&& cases[3].actualTicks < cases[1].actualTicks,
					patchTargets,
					target = new
					{
						cell = ZombieRuntimeActions.DescribeCell(targetCell),
						sourceEvidence = "Verb.TryStartCastOn computes Stance_Warmup ticks from (WarmupTime * AimingDelayFactor).SecondsToTicks()."
					},
					cases
				};
			}
			finally
			{
				foreach (var pair in originalGridCounts)
				{
					var current = grid.GetZombieCount(pair.Key);
					if (current != 0)
						grid.ChangeZombieCount(pair.Key, -current);
					if (pair.Value != 0)
						grid.ChangeZombieCount(pair.Key, pair.Value);
				}
			}
		}

		static WarmupScalingProbeCase RunWarmupScalingCase(
			string name,
			Pawn caster,
			IntVec3 targetCell,
			int expectedDivisor,
			MethodInfo modifyTicks,
			FieldInfo warmupTimeField,
			FieldInfo ticksLeftField)
		{
			var verb = EquipAreaWorkflowRangedWeapon(caster);
			if (verb == null)
			{
				return new WarmupScalingProbeCase
				{
					name = name,
					success = false,
					caster = DescribePawn(caster),
					targetCell = ZombieRuntimeActions.DescribeCell(targetCell),
					error = "The caster has no ranged verb for the warmup-scaling probe."
				};
			}

			var oldWarmupTime = (float)warmupTimeField.GetValue(verb.verbProps);
			const float probeWarmupTime = 2f;
			try
			{
				caster.jobs?.EndCurrentJob(JobCondition.InterruptForced);
				caster.pather?.StopDead();
				caster.stances?.CancelBusyStanceHard();
				warmupTimeField.SetValue(verb.verbProps, probeWarmupTime);

				var zombie = caster as Zombie;
				var gridCount = zombie?.Map?.GetGrid()?.GetZombieCount(zombie.Position) ?? 0;
				var actualDivisor = zombie != null && (zombie.raging > 0 || zombie.wasMapPawnBefore) && gridCount > 0 ? gridCount : 1;
				var aimingDelay = caster.GetStatValue(StatDefOf.AimingDelayFactor);
				var expectedTicks = (verb.WarmupTime * aimingDelay).SecondsToTicks() / actualDivisor;
				var directModifyTicks = (int)modifyTicks.Invoke(null, new object[] { verb.WarmupTime * aimingDelay, verb });
				var castResult = verb.TryStartCastOn(new LocalTargetInfo(targetCell), LocalTargetInfo.Invalid, false, true, false, false);
				var stance = caster.stances?.curStance;
				var actualTicks = stance is Stance_Warmup ? (int?)ticksLeftField.GetValue(stance) : null;

				return new WarmupScalingProbeCase
				{
					name = name,
					success = castResult
						&& stance is Stance_Warmup
						&& actualTicks == expectedTicks
						&& directModifyTicks == expectedTicks
						&& actualDivisor == expectedDivisor,
					caster = DescribePawn(caster),
					targetCell = ZombieRuntimeActions.DescribeCell(targetCell),
					verb = DescribeVerb(verb),
					castResult = castResult,
					stanceType = stance?.GetType().Name,
					warmupTime = verb.WarmupTime,
					aimingDelayFactor = aimingDelay,
					gridCount = gridCount,
					divisor = actualDivisor,
					expectedTicks = expectedTicks,
					actualTicks = actualTicks,
					directModifyTicks = directModifyTicks,
					isRaging = zombie?.raging > 0,
					wasMapPawnBefore = zombie?.wasMapPawnBefore ?? false
				};
			}
			catch (Exception ex)
			{
				return new WarmupScalingProbeCase
				{
					name = name,
					success = false,
					caster = DescribePawn(caster),
					targetCell = ZombieRuntimeActions.DescribeCell(targetCell),
					verb = DescribeVerb(verb),
					error = ex.GetBaseException().Message
				};
			}
			finally
			{
				warmupTimeField.SetValue(verb.verbProps, oldWarmupTime);
				caster.stances?.CancelBusyStanceHard();
			}
		}

		static Verb EquipAreaWorkflowRangedWeapon(Pawn pawn)
		{
			if (pawn?.equipment == null)
				return null;
			pawn.equipment.DestroyAllEquipment(DestroyMode.Vanish);
			var weaponDef = DefDatabase<ThingDef>.GetNamed("Gun_BoltActionRifle", false)
				?? DefDatabase<ThingDef>.GetNamed("Gun_Pistol", false)
				?? DefDatabase<ThingDef>.AllDefs.FirstOrDefault(def => def.IsRangedWeapon);
			var weapon = weaponDef == null ? null : ThingMaker.MakeThing(weaponDef) as ThingWithComps;
			if (weapon == null)
				return null;
			pawn.equipment.AddEquipment(weapon);
			return pawn.equipment.PrimaryEq?.PrimaryVerb;
		}

		static bool FindWarmupScalingCells(Map map, IntVec3 root, out IntVec3 targetCell, out IntVec3[] casterCells, out object error)
		{
			targetCell = IntVec3.Invalid;
			casterCells = Array.Empty<IntVec3>();
			error = null;

			if (TryFindClearSpawnCell(map, root, 16f, out targetCell, out var targetError) == false)
			{
				error = targetError;
				return false;
			}

			var target = targetCell;
			casterCells = GenRadial.RadialCellsAround(target, 14f, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell != target)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.Where(cell => cell.DistanceTo(target) >= 7f)
				.Where(cell => GenSight.LineOfSight(cell, target, map, true))
				.OrderBy(cell => cell.DistanceToSquared(root))
				.Take(4)
				.ToArray();
			if (casterCells.Length >= 4)
				return true;

			error = new
			{
				success = false,
				targetCell = ZombieRuntimeActions.DescribeCell(targetCell),
				foundCasterCells = casterCells.Length,
				error = "Could not find enough line-of-sight caster cells for the warmup-scaling fixture."
			};
			return false;
		}

		sealed class StatProbeCase
		{
			public string name { get; set; }
			public bool success { get; set; }
			public string stat { get; set; }
			public float actual { get; set; }
			public float expected { get; set; }
			public float tolerance { get; set; }
			public object pawn { get; set; }
			public string note { get; set; }
		}

		static object VerifyAreaWorkflowZombieStats(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			var patchTargets = PatchedMethodsForPatchClass("StatExtension_GetStatValue_Patch");
			var damageFactorPatchTargets = PatchedMethodsForPatchClass("Verb_GetDamageFactorFor_Patch");
			var prefix = FindNestedPatchMethod("StatExtension_GetStatValue_Patch", "Prefix");
			var getStatValue = AccessTools.Method(typeof(StatExtension), nameof(StatExtension.GetStatValue), new[] { typeof(Thing), typeof(StatDef), typeof(bool), typeof(int) });
			if (prefix == null || getStatValue == null)
			{
				return new
				{
					success = false,
					error = "Could not reflect all zombie stat probe members.",
					reflection = new
					{
						prefix = prefix != null,
						getStatValue = getStatValue != null
					},
					patchTargets
				};
			}

			if (FindZombieStatsCells(map, root, out var cells, out var cellError) == false)
				return cellError;

			var settingsSnapshot = SnapshotZombieSettings();
			var tickerSnapshot = new
			{
				percentZombiesTicked = ZombieTicker.percentZombiesTicked.ToArray(),
				percentZombiesTickedIndex = ZombieTicker.percentZombiesTickedIndex
			};
			var existingLetters = (Find.LetterStack?.LettersListForReading ?? new List<Letter>()).ToHashSet();
			var hiddenFreePawns = map.mapPawns.FreeColonistsAndPrisonersSpawned
				.Where(pawn => pawn?.Spawned == true)
				.Select(pawn => (pawn, position: pawn.Position, rotation: pawn.Rotation))
				.ToList();

			try
			{
				foreach (var snapshot in hiddenFreePawns)
					snapshot.pawn.DeSpawn(DestroyMode.Vanish);

				ZombieSettings.Values.moveSpeedIdle = 0.25f;
				ZombieSettings.Values.moveSpeedTracking = 0.75f;
				ZombieSettings.Values.spitterThreat = 2.25f;
				ZombieSettings.Values.damageFactor = 1.25f;
				ZombieTicker.percentZombiesTicked = Enumerable.Repeat(1f, ZombieTicker.percentZombiesTicked.Length).ToArray();
				ZombieTicker.percentZombiesTickedIndex = 0;

				var baseMoveSpeed = ThingDefOf.Human.statBases.First(modifier => modifier.stat == StatDefOf.MoveSpeed).value;
				var albinoNoPawns = SpawnTargetZombie(map, cells[9], ZombieType.Albino, "ZL_Area_StatsAlbinoNoPawns", spawnedThings);
				if (albinoNoPawns == null)
				{
					return new
					{
						success = false,
						albinoNoPawns = DescribeZombie(albinoNoPawns),
						error = "Could not create the no-nearby-pawns albino stat fixture."
					};
				}
				PrepareAlbinoMoveSpeedProbe(albinoNoPawns);
				var albinoNoPawnsMoveSpeed = MakeStatCase("albinoNoPawnsMoveSpeed", albinoNoPawns, StatDefOf.MoveSpeed, ZombieSettings.Values.moveSpeedIdle * baseMoveSpeed, "albino with no free colonists or prisoners nearby uses normal idle movement");

				var human = SpawnAreaWorkflowPawn(map, "ZL_Area_StatsHuman", cells[0], Faction.OfPlayer, spawnedThings);
				var idleFat = SpawnTargetZombie(map, cells[1], ZombieType.Normal, "ZL_Area_StatsIdleFat", spawnedThings);
				var trackingThin = SpawnTargetZombie(map, cells[2], ZombieType.Normal, "ZL_Area_StatsTrackingThin", spawnedThings);
				var formerHulk = SpawnTargetZombie(map, cells[3], ZombieType.Normal, "ZL_Area_StatsFormerHulk", spawnedThings);
				var tanky = SpawnTargetZombie(map, cells[4], ZombieType.Normal, "ZL_Area_StatsTanky", spawnedThings);
				var downedRoped = SpawnTargetZombie(map, cells[5], ZombieType.Normal, "ZL_Area_StatsDownedRoped", spawnedThings);
				var albino = SpawnTargetZombie(map, cells[6], ZombieType.Albino, "ZL_Area_StatsAlbino", spawnedThings);
				var raging = SpawnTargetZombie(map, cells[7], ZombieType.Normal, "ZL_Area_StatsRaging", spawnedThings);
				var spitter = SpawnTargetSpitter(map, cells[8], "ZL_Area_StatsSpitter", spawnedThings);
				var reservedSpeedCells = hiddenFreePawns.Select(snapshot => snapshot.position).Concat(cells).ToList();
				if (TryFindZombieStatsAlbinoSpeedCells(map, human?.Position ?? IntVec3.Invalid, reservedSpeedCells, out var farAlbinoCell, out var nearAlbinoCell, out var speedCellError) == false)
					return speedCellError;

				var albinoFar = SpawnTargetZombie(map, farAlbinoCell, ZombieType.Albino, "ZL_Area_StatsAlbinoFar", spawnedThings);
				var albinoNear = SpawnTargetZombie(map, nearAlbinoCell, ZombieType.Albino, "ZL_Area_StatsAlbinoNear", spawnedThings);
				if (human == null || idleFat == null || trackingThin == null || formerHulk == null || tanky == null || downedRoped == null || albino == null || raging == null || spitter == null || albinoFar == null || albinoNear == null)
				{
					return new
					{
						success = false,
						fixtures = new
						{
							human = DescribePawn(human),
							idleFat = DescribeZombie(idleFat),
							trackingThin = DescribeZombie(trackingThin),
							formerHulk = DescribeZombie(formerHulk),
							tanky = DescribeZombie(tanky),
							downedRoped = DescribeZombie(downedRoped),
							albino = DescribeZombie(albino),
							raging = DescribeZombie(raging),
							spitter = DescribeZombie(spitter),
							albinoFar = DescribeZombie(albinoFar),
							albinoNear = DescribeZombie(albinoNear)
						},
						error = "Could not create all zombie stat fixtures."
					};
				}

				idleFat.state = ZombieState.Wandering;
				idleFat.raging = 0;
				idleFat.story.bodyType = BodyTypeDefOf.Fat;
				trackingThin.state = ZombieState.Tracking;
				trackingThin.raging = 0;
				trackingThin.story.bodyType = BodyTypeDefOf.Thin;
				formerHulk.wasMapPawnBefore = true;
				formerHulk.raging = 0;
				formerHulk.story.bodyType = BodyTypeDefOf.Hulk;
				tanky.hasTankyHelmet = 1f;
				tanky.hasTankySuit = 1f;
				raging.raging = GenTicks.TicksAbs + 60000;
				raging.story.bodyType = BodyTypeDefOf.Male;
				PrepareAlbinoMoveSpeedProbe(albino);
				PrepareAlbinoMoveSpeedProbe(albinoFar);
				PrepareAlbinoMoveSpeedProbe(albinoNear);
				if (TryMakeDownedForCombat(downedRoped, out var downedError) == false)
				{
					return new
					{
						success = false,
						downedRoped = DescribeZombie(downedRoped),
						error = downedError
					};
				}
				downedRoped.ropedBy = human;

				var tickRate = Find.TickManager.TickRateMultiplier;
				var cases = new List<StatProbeCase>
				{
					albinoNoPawnsMoveSpeed,
					MakeStatCase("idleFatMoveSpeed", idleFat, StatDefOf.MoveSpeed, ZombieSettings.Values.moveSpeedIdle * 0.7f * baseMoveSpeed, "idle Fat zombie uses idle setting, Fat body factor, and human base speed"),
					MakeStatCase("trackingThinMoveSpeed", trackingThin, StatDefOf.MoveSpeed, ZombieSettings.Values.moveSpeedTracking * 0.8f * baseMoveSpeed, "tracking Thin zombie uses tracking setting and Thin body factor"),
					MakeStatCase("formerHulkMoveSpeed", formerHulk, StatDefOf.MoveSpeed, ZombieSettings.Values.moveSpeedTracking * 0.8f * baseMoveSpeed * 2f, "former-map-pawn Hulk zombie uses tracking speed, Hulk factor, and former-pawn doubling"),
					MakeStatCase("tankyMoveSpeed", tanky, StatDefOf.MoveSpeed, 0.004f * baseMoveSpeed * tickRate, "tanky movement ignores ordinary speed settings"),
					MakeStatCase("downedRopedMoveSpeed", downedRoped, StatDefOf.MoveSpeed, 0.4f * tickRate, "roped health-downed zombie uses the roped crawler speed"),
					MakeStatCase("albinoFarMoveSpeed", albinoFar, StatDefOf.MoveSpeed, ZombieSettings.Values.moveSpeedIdle * baseMoveSpeed, "albino more than 30 cells from a free colonist uses normal idle movement"),
					MakeStatCase("albinoNearMoveSpeed", albinoNear, StatDefOf.MoveSpeed, ExpectedAlbinoMoveSpeed(albinoNear, human, baseMoveSpeed), "albino near a free colonist uses the proximity speed curve"),
					MakeStatCase("formerMeleeHit", formerHulk, StatDefOf.MeleeHitChance, 1f, "former-map-pawn melee hit override"),
					MakeStatCase("downedMeleeHit", downedRoped, StatDefOf.MeleeHitChance, 0.1f, "downed zombie melee hit override"),
					MakeStatCase("tankyMeleeHit", tanky, StatDefOf.MeleeHitChance, 0.9f, "tanky helmet/suit melee hit override"),
					MakeStatCase("trackingMeleeHit", trackingThin, StatDefOf.MeleeHitChance, Constants.ZOMBIE_HIT_CHANCE_TRACKING, "tracking zombie uses tracking hit constant"),
					MakeStatCase("formerMeleeDodge", formerHulk, StatDefOf.MeleeDodgeChance, 0.9f, "former-map-pawn dodge override"),
					MakeStatCase("albinoMeleeDodge", albino, StatDefOf.MeleeDodgeChance, 0f, "albino dodge override"),
					MakeStatCase("normalMeleeDodge", idleFat, StatDefOf.MeleeDodgeChance, 0.02f, "ordinary zombie dodge override"),
					MakeStatCase("formerPainShock", formerHulk, StatDefOf.PainShockThreshold, 4000f, "former-map-pawn pain shock override"),
					MakeStatCase("ragingPainShock", raging, StatDefOf.PainShockThreshold, 1000f, "raging pain shock override"),
					MakeStatCase("tankyPainShock", tanky, StatDefOf.PainShockThreshold, 5000f, "tanky armor pain shock override"),
					MakeStatCase("thinPainShock", trackingThin, StatDefOf.PainShockThreshold, 0.1f, "Thin body pain shock override"),
					MakeStatCase("fatPainShock", idleFat, StatDefOf.PainShockThreshold, 10f, "Fat body pain shock override"),
					MakeStatCase("tankyComfyTemperatureMin", tanky, StatDefOf.ComfyTemperatureMin, -999f, "tanky temperature minimum override"),
					MakeStatCase("tankyComfyTemperatureMax", tanky, StatDefOf.ComfyTemperatureMax, 999f, "tanky temperature maximum override"),
					MakeStatCase("spitterIncomingDamageFactor", spitter, StatDefOf.IncomingDamageFactor, 1f, "spitter uses vanilla incoming damage factor; Zombieland spitter scaling is handled by the injury damage prefix")
				};

				var smokeSensitivity = DefDatabase<StatDef>.GetNamed("SmokeSensitivity", false);
				if (smokeSensitivity != null)
					cases.Add(MakeStatCase("ignoredSmokeSensitivity", idleFat, smokeSensitivity, 0f, "zombies ignore smoke sensitivity stat"));
				var suppressability = DefDatabase<StatDef>.GetNamed("Suppressability", false);
				if (suppressability != null)
					cases.Add(MakeStatCase("ignoredSuppressability", idleFat, suppressability, 0f, "zombies ignore suppressability stat"));

				var humanPrefixArgs = new object[] { human, StatDefOf.MoveSpeed, -12345f };
				var humanPrefixContinues = (bool)prefix.Invoke(null, humanPrefixArgs);
				var humanPrefixResultUnchanged = Mathf.Approximately((float)humanPrefixArgs[2], -12345f);
				var humanMoveSpeed = human.GetStatValue(StatDefOf.MoveSpeed);
				var damageFactors = VerifyZombieVerbDamageFactors(human, idleFat, trackingThin, formerHulk, tanky);

				return new
				{
					success = patchTargets.Length > 0
						&& damageFactorPatchTargets.Length > 0
						&& humanPrefixContinues
						&& humanPrefixResultUnchanged
						&& humanMoveSpeed > 0f
						&& cases.All(entry => entry.success)
						&& ObjectSuccess(damageFactors),
					patchTargets,
					damageFactorPatchTargets,
					fixtures = new
					{
						human = DescribePawn(human),
						idleFat = DescribeZombie(idleFat),
						trackingThin = DescribeZombie(trackingThin),
						formerHulk = DescribeZombie(formerHulk),
						tanky = DescribeZombie(tanky),
						downedRoped = DescribeZombie(downedRoped),
						albino = DescribeZombie(albino),
						raging = DescribeZombie(raging),
						spitter = DescribeZombie(spitter),
						albinoNoPawns = DescribeZombie(albinoNoPawns),
						albinoFar = DescribeZombie(albinoFar),
						albinoNear = DescribeZombie(albinoNear)
					},
					settings = new
					{
						ZombieSettings.Values.moveSpeedIdle,
						ZombieSettings.Values.moveSpeedTracking,
						ZombieSettings.Values.spitterThreat,
						zombieTickerPercent = ZombieTicker.PercentTicking,
						baseHumanMoveSpeed = baseMoveSpeed,
						tickRate
					},
					humanPassThrough = new
					{
						humanPrefixContinues,
						humanPrefixResultUnchanged,
						moveSpeed = humanMoveSpeed
					},
					cases = cases.ToArray(),
					damageFactors,
					optionalStats = new
					{
						smokeSensitivityPresent = smokeSensitivity != null,
						suppressabilityPresent = suppressability != null
					}
				};
			}
			finally
			{
				foreach (var snapshot in hiddenFreePawns)
					if (snapshot.pawn != null && snapshot.pawn.Destroyed == false && snapshot.pawn.Spawned == false)
						GenSpawn.Spawn(snapshot.pawn, snapshot.position, map, snapshot.rotation, WipeMode.Vanish);

				RestoreZombieSettings(settingsSnapshot);
				ZombieTicker.percentZombiesTicked = tickerSnapshot.percentZombiesTicked.ToArray();
				ZombieTicker.percentZombiesTickedIndex = tickerSnapshot.percentZombiesTickedIndex;
				RemoveZombieStatsFixtureLetters(existingLetters);
			}
		}

		static object VerifyZombieVerbDamageFactors(Pawn human, Zombie idleFat, Zombie trackingThin, Zombie formerHulk, Zombie tanky)
		{
			var verbProps = idleFat?.meleeVerbs?.TryGetMeleeVerb(human)?.verbProps;
			if (verbProps == null)
			{
				return new
				{
					success = false,
					error = "Could not find a zombie melee verb for damage-factor probing.",
					zombie = DescribeZombie(idleFat),
					target = DescribePawn(human)
				};
			}

			var humanCase = MakeDamageFactorCase("humanPassThrough", human, verbProps, 1f, "ordinary human uses vanilla damage factor only");
			var fatCase = MakeDamageFactorCase("fatZombie", idleFat, verbProps, 4f * ZombieSettings.Values.damageFactor, "Fat zombie scales by body type and damageFactor setting");
			var thinCase = MakeDamageFactorCase("thinZombie", trackingThin, verbProps, 0.5f * ZombieSettings.Values.damageFactor, "Thin zombie scales by body type and damageFactor setting");
			var formerCase = MakeDamageFactorCase("formerHulkZombie", formerHulk, verbProps, 3f * ZombieSettings.Values.damageFactor * 5f, "Former-map-pawn Hulk zombie scales by body type, damageFactor setting, and former-pawn multiplier");
			tanky.hasTankyShield = 1f;
			tanky.hasTankyHelmet = 1f;
			tanky.hasTankySuit = 1f;
			var tankyCase = MakeDamageFactorCase("fullyArmoredTanky", tanky, verbProps, 60f, "Tanky shield, helmet, and suit replace body-type scaling with equipment-weight scaling");
			var cases = new[] { humanCase, fatCase, thinCase, formerCase, tankyCase };

			return new
			{
				success = cases.All(entry => entry.success),
				verb = new
				{
					label = verbProps.label,
					isMeleeAttack = verbProps.IsMeleeAttack
				},
				damageFactorSetting = ZombieSettings.Values.damageFactor,
				cases
			};
		}

		static DamageFactorProbeCase MakeDamageFactorCase(string name, Pawn attacker, VerbProperties verbProps, float expectedPostfixMultiplier, string note, float tolerance = 0.0001f)
		{
			var baseFactor = CalculateExpectedVanillaDamageFactor(attacker, verbProps);
			var actual = verbProps.GetDamageFactorFor(null, attacker, null);
			var expected = baseFactor * expectedPostfixMultiplier;
			return new DamageFactorProbeCase
			{
				name = name,
				success = Mathf.Abs(actual - expected) <= tolerance,
				attacker = DescribePawn(attacker),
				baseFactor = baseFactor,
				expectedPostfixMultiplier = expectedPostfixMultiplier,
				actual = actual,
				expected = expected,
				tolerance = tolerance,
				note = note
			};
		}

		static float CalculateExpectedVanillaDamageFactor(Pawn attacker, VerbProperties verbProps)
		{
			var result = 1f;
			if (attacker != null && verbProps.IsMeleeAttack)
			{
				result *= attacker.ageTracker.CurLifeStage.meleeDamageFactor;
				result *= attacker.GetStatValue(StatDefOf.MeleeDamageFactor);
			}
			return result;
		}

		sealed class DamageFactorProbeCase
		{
			public string name { get; set; }
			public bool success { get; set; }
			public object attacker { get; set; }
			public float baseFactor { get; set; }
			public float expectedPostfixMultiplier { get; set; }
			public float actual { get; set; }
			public float expected { get; set; }
			public float tolerance { get; set; }
			public string note { get; set; }
		}

		static StatProbeCase MakeStatCase(string name, Thing thing, StatDef stat, float expected, string note, float tolerance = 0.0001f)
		{
			var actual = thing.GetStatValue(stat);
			return new StatProbeCase
			{
				name = name,
				success = Mathf.Abs(actual - expected) <= tolerance,
				stat = stat?.defName,
				actual = actual,
				expected = expected,
				tolerance = tolerance,
				pawn = thing is Pawn pawn ? DescribePawn(pawn) : StableId(thing),
				note = note
			};
		}

		static void PrepareAlbinoMoveSpeedProbe(Zombie albino)
		{
			if (albino == null)
				return;

			albino.state = ZombieState.Wandering;
			albino.raging = 0;
			albino.wasMapPawnBefore = false;
			albino.ropedBy = null;
			albino.story.bodyType = BodyTypeDefOf.Male;
		}

		static void RemoveZombieStatsFixtureLetters(HashSet<Letter> existingLetters)
		{
			var letterStack = Find.LetterStack;
			var letters = letterStack?.LettersListForReading;
			if (letterStack == null || letters == null)
				return;

			foreach (var letter in letters
				.Where(letter => letter != null && existingLetters.Contains(letter) == false)
				.ToArray())
				letterStack.RemoveLetter(letter);
		}

		static float ExpectedAlbinoMoveSpeed(Zombie albino, Pawn target, float baseMoveSpeed)
		{
			var minDistSquared = target == null ? 900 : Math.Min(900, albino.Position.DistanceToSquared(target.Position));
			var albinoSpeed = GenMath.LerpDoubleClamped(36, 900, 5f, 1f, minDistSquared);
			var speed = albinoSpeed > 1f ? ZombieSettings.Values.moveSpeedTracking : ZombieSettings.Values.moveSpeedIdle;
			return speed * baseMoveSpeed * albinoSpeed;
		}

		static bool IsZombieStatsClearCell(Map map, IntVec3 cell)
		{
			return cell.InBounds(map)
				&& cell.Standable(map)
				&& cell.Fogged(map) == false
				&& cell.GetFirstPawn(map) == null;
		}

		static bool TryFindZombieStatsAlbinoSpeedCells(Map map, IntVec3 targetCell, IEnumerable<IntVec3> reservedCells, out IntVec3 farAlbinoCell, out IntVec3 nearAlbinoCell, out object error)
		{
			farAlbinoCell = IntVec3.Invalid;
			nearAlbinoCell = IntVec3.Invalid;
			error = null;
			var reserved = new HashSet<IntVec3>(reservedCells ?? Enumerable.Empty<IntVec3>());
			if (targetCell.IsValid == false)
			{
				error = new
				{
					success = false,
					error = "No valid target cell was available for albino speed fixtures."
				};
				return false;
			}

			nearAlbinoCell = GenRadial.RadialCellsAround(targetCell, 6f, false)
				.Where(cell => cell.DistanceToSquared(targetCell) <= 36)
				.Where(cell => reserved.Contains(cell) == false)
				.Where(cell => IsZombieStatsClearCell(map, cell))
				.OrderByDescending(cell => cell.DistanceToSquared(targetCell))
				.FirstOrDefault();

			farAlbinoCell = map.AllCells
				.Where(cell => cell.DistanceToSquared(targetCell) >= 900)
				.Where(cell => reserved.Contains(cell) == false)
				.Where(cell => IsZombieStatsClearCell(map, cell))
				.OrderBy(cell => cell.DistanceToSquared(targetCell))
				.FirstOrDefault();

			if (nearAlbinoCell.IsValid && farAlbinoCell.IsValid)
				return true;

			error = new
			{
				success = false,
				targetCell = ZombieRuntimeActions.DescribeCell(targetCell),
				nearFound = nearAlbinoCell.IsValid,
				farFound = farAlbinoCell.IsValid,
				error = "Could not find deterministic near and far albino speed cells."
			};
			return false;
		}

		static bool FindZombieStatsCells(Map map, IntVec3 root, out IntVec3[] cells, out object error)
		{
			cells = GenRadial.RadialCellsAround(root, 24f, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.OrderBy(cell => cell.DistanceToSquared(root))
				.Take(10)
				.ToArray();
			if (cells.Length >= 10)
			{
				error = null;
				return true;
			}

			var fallbackRoot = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			cells = map.AllCells
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.OrderBy(cell => cell.DistanceToSquared(fallbackRoot))
				.Take(10)
				.ToArray();
			if (cells.Length >= 10)
			{
				error = null;
				return true;
			}

			error = new
			{
				success = false,
				root = ZombieRuntimeActions.DescribeCell(root),
				foundCells = cells.Length,
				error = "Could not find enough cells for the zombie-stat fixture."
			};
			return false;
		}

		static object VerifyAreaWorkflowVisualSupport(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			var drawTrackerConstructorTargets = PatchedMethodsForPatchClass("Pawn_DrawTracker_Constructor_Patch");
			var rendererConstructorTargets = PatchedMethodsForPatchClass("PawnRenderer_Constructor_With_Pawn_Patch");
			var damageNotifyTargets = PatchedMethodsForPatchClass("DamageFlasher_Notify_DamageApplied_Patch");
			var damageMatTargets = PatchedMethodsForPatchClass("DamageFlasher_GetDamagedMat_Patch");
			var drawPosTargets = PatchedMethodsForPatchClass("Pawn_DrawTracker_DrawPos_Patch");
			var tickVisualTargets = PatchedMethodsForPatchClass("Pawn_DrawTrackerDrawTrackerTick_Patch");
			var dinfoDefField = AccessTools.Field(typeof(ZombieDamageFlasher), nameof(ZombieDamageFlasher.dinfoDef));
			var jitterOffsetField = AccessTools.Field(typeof(ZombieLeaner), "jitterOffset");
			var extraOffsetInternalField = AccessTools.Field(typeof(ZombieLeaner), "extraOffsetInternal");
			var randTickFrequencyField = AccessTools.Field(typeof(ZombieLeaner), "randTickFrequency");
			var randTickOffsetField = AccessTools.Field(typeof(ZombieLeaner), "randTickOffset");
			var drawTrackerProcessPostTickVisuals = AccessTools.Method(typeof(Pawn_DrawTracker), nameof(Pawn_DrawTracker.ProcessPostTickVisuals), new[] { typeof(int) });
			if (dinfoDefField == null || jitterOffsetField == null || extraOffsetInternalField == null || randTickFrequencyField == null || randTickOffsetField == null || drawTrackerProcessPostTickVisuals == null)
			{
				return new
				{
					success = false,
					error = "Could not reflect all visual-support probe members.",
					reflection = new
					{
						dinfoDefField = dinfoDefField != null,
						jitterOffsetField = jitterOffsetField != null,
						extraOffsetInternalField = extraOffsetInternalField != null,
						randTickFrequencyField = randTickFrequencyField != null,
						randTickOffsetField = randTickOffsetField != null,
						drawTrackerProcessPostTickVisuals = drawTrackerProcessPostTickVisuals != null
					}
				};
			}

			if (FindVisualSupportCells(map, root, out var cells, out var cellError) == false)
				return cellError;

			var zombie = SpawnTargetZombie(map, cells[0], ZombieType.Normal, "ZL_Area_VisualSupportZombie", spawnedThings);
			var human = SpawnAreaWorkflowPawn(map, "ZL_Area_VisualSupportHuman", cells[1], Faction.OfPlayer, spawnedThings);
			if (zombie == null || human == null)
			{
				return new
				{
					success = false,
					zombie = DescribeZombie(zombie),
					human = DescribePawn(human),
					error = "Could not create visual-support fixtures."
				};
			}

			var damageFlash = VerifyVisualSupportDamageFlash(zombie, human, dinfoDefField);
			var customPawnState = VerifyVisualSupportCustomPawnState(zombie, human);
			var leaner = VerifyVisualSupportLeaner(zombie, human, jitterOffsetField, extraOffsetInternalField, randTickFrequencyField, randTickOffsetField, drawTrackerProcessPostTickVisuals);

			return new
			{
				success = drawTrackerConstructorTargets.Length > 0
					&& rendererConstructorTargets.Length > 0
					&& damageNotifyTargets.Length > 0
					&& damageMatTargets.Length > 0
					&& drawPosTargets.Length > 0
					&& tickVisualTargets.Length > 0
					&& ObjectSuccess(damageFlash)
					&& ObjectSuccess(customPawnState)
					&& ObjectSuccess(leaner),
				patchTargets = new
				{
					drawTrackerConstructor = drawTrackerConstructorTargets,
					rendererConstructor = rendererConstructorTargets,
					damageNotify = damageNotifyTargets,
					damageMaterial = damageMatTargets,
					drawPos = drawPosTargets,
					postTickVisuals = tickVisualTargets
				},
				fixtures = new
				{
					zombie = DescribeZombie(zombie),
					human = DescribePawn(human)
				},
				damageFlash,
				customPawnState,
				leaner
			};
		}

		static object VerifyVisualSupportDamageFlash(Zombie zombie, Pawn human, FieldInfo dinfoDefField)
		{
			var zombieFlasher = zombie?.Drawer?.renderer?.flasher;
			var humanFlasher = human?.Drawer?.renderer?.flasher;
			if (zombieFlasher == null || humanFlasher == null)
			{
				return new
				{
					success = false,
					zombieFlasher = zombieFlasher?.GetType().Name,
					humanFlasher = humanFlasher?.GetType().Name,
					error = "Could not read renderer flashers."
				};
			}

			var zombieFlasherIsCustom = zombieFlasher is ZombieDamageFlasher;
			var humanFlasherIsCustom = humanFlasher is ZombieDamageFlasher;
			var biteBase = new Material(ShaderDatabase.Cutout) { color = Color.white };
			var cutBase = new Material(ShaderDatabase.Cutout) { color = Color.white };
			zombieFlasher.Notify_DamageApplied(new DamageInfo(CustomDefs.ZombieBite, 1f));
			var capturedBiteDef = dinfoDefField.GetValue(zombieFlasher) as DamageDef;
			var biteMaterial = zombieFlasher.GetDamagedMat(biteBase);
			var biteColor = biteMaterial?.color ?? Color.clear;

			humanFlasher.Notify_DamageApplied(new DamageInfo(DamageDefOf.Cut, 1f));
			var capturedCutDef = dinfoDefField.GetValue(humanFlasher) as DamageDef;
			var cutMaterial = humanFlasher.GetDamagedMat(cutBase);
			var cutColor = cutMaterial?.color ?? Color.clear;
			var expectedBiteColor = new Color(0f, 0.8f, 0f, 1f);

			return new
			{
				success = zombieFlasherIsCustom
					&& humanFlasherIsCustom
					&& capturedBiteDef == CustomDefs.ZombieBite
					&& capturedCutDef == DamageDefOf.Cut
					&& ColorsApproximatelyEqual(biteColor, expectedBiteColor, 0.03f)
					&& ColorsApproximatelyEqual(cutColor, expectedBiteColor, 0.03f) == false,
				zombieFlasherType = zombieFlasher.GetType().Name,
				humanFlasherType = humanFlasher.GetType().Name,
				capturedBiteDef = capturedBiteDef?.defName,
				capturedCutDef = capturedCutDef?.defName,
				biteColor = DescribeColor(biteColor),
				expectedBiteColor = DescribeColor(expectedBiteColor),
				cutColor = DescribeColor(cutColor),
				cutStayedNonGreen = ColorsApproximatelyEqual(cutColor, expectedBiteColor, 0.03f) == false
			};
		}

		static object VerifyVisualSupportCustomPawnState(Zombie zombie, Pawn human)
		{
			var humanLeaner = human?.Drawer?.leaner;
			var zombieLeaner = zombie?.Drawer?.leaner;
			var humanInitial = human?.InfectionState();
			var zombieState = zombie?.InfectionState();
			if (human != null)
				human.SetInfectionState(InfectionState.Infecting);
			var humanAfterSet = human?.InfectionState();
			if (human != null)
				human.SetInfectionState(InfectionState.None);
			var humanAfterReset = human?.InfectionState();

			return new
			{
				success = humanLeaner is CustomPawnState
					&& zombieLeaner is ZombieLeaner
					&& humanInitial == InfectionState.None
					&& humanAfterSet == InfectionState.Infecting
					&& humanAfterReset == InfectionState.None
					&& zombieState == InfectionState.Infected,
				humanLeanerType = humanLeaner?.GetType().Name,
				zombieLeanerType = zombieLeaner?.GetType().Name,
				humanStates = new
				{
					initial = humanInitial?.ToString(),
					afterSet = humanAfterSet?.ToString(),
					afterReset = humanAfterReset?.ToString()
				},
				zombieState = zombieState?.ToString()
			};
		}

		static object VerifyVisualSupportLeaner(
			Zombie zombie,
		Pawn human,
		FieldInfo jitterOffsetField,
		FieldInfo extraOffsetInternalField,
		FieldInfo randTickFrequencyField,
		FieldInfo randTickOffsetField,
		MethodInfo drawTrackerProcessPostTickVisuals)
		{
			var zombieLeaner = zombie?.Drawer?.leaner as ZombieLeaner;
			var humanHasZombieLeaner = human?.Drawer?.leaner is ZombieLeaner;
			if (zombieLeaner == null)
			{
				return new
				{
					success = false,
					zombieLeanerType = zombie?.Drawer?.leaner?.GetType().Name,
					humanLeanerType = human?.Drawer?.leaner?.GetType().Name,
					error = "Zombie draw tracker does not have a ZombieLeaner."
				};
			}

			var originalLeaner = zombie.Drawer.leaner;
			var forcedOffset = new Vector3(0.21f, 0f, -0.13f);
			jitterOffsetField.SetValue(zombieLeaner, Vector3.zero);
			extraOffsetInternalField.SetValue(zombieLeaner, forcedOffset);
			zombieLeaner.extraOffset = Vector3.zero;
			var patchedDrawPos = zombie.Drawer.DrawPos;
			zombie.Drawer.leaner = new PawnLeaner(zombie);
			var vanillaDrawPos = zombie.Drawer.DrawPos;
			zombie.Drawer.leaner = originalLeaner;
			var drawDelta = patchedDrawPos - vanillaDrawPos;
			var drawPosAppliesOffset = VectorsApproximatelyEqual(drawDelta, forcedOffset, 0.01f);

			randTickFrequencyField.SetValue(zombieLeaner, 1);
			randTickOffsetField.SetValue(zombieLeaner, 0);
			jitterOffsetField.SetValue(zombieLeaner, new Vector3(0.11f, 0f, 0.12f));
			extraOffsetInternalField.SetValue(zombieLeaner, new Vector3(0.22f, 0f, 0.23f));
			zombieLeaner.extraOffset = Vector3.zero;
			zombie.state = ZombieState.Emerging;
			drawTrackerProcessPostTickVisuals.Invoke(zombie.Drawer, new object[] { 1 });
			var resetOffset = zombieLeaner.ZombieOffset;
			var postTickReset = VectorsApproximatelyEqual(resetOffset, Vector3.zero, 0.0001f);

			zombie.state = ZombieState.Wandering;
			jitterOffsetField.SetValue(zombieLeaner, Vector3.zero);
			extraOffsetInternalField.SetValue(zombieLeaner, Vector3.zero);
			zombieLeaner.extraOffset = new Vector3(0.4f, 0f, 0.2f);
			Rand.PushState(191919);
			drawTrackerProcessPostTickVisuals.Invoke(zombie.Drawer, new object[] { 1 });
			Rand.PopState();
			var updatedOffset = zombieLeaner.ZombieOffset;
			var postTickUpdates = updatedOffset.MagnitudeHorizontalSquared() > 0.001f;

			return new
			{
				success = humanHasZombieLeaner == false
					&& drawPosAppliesOffset
					&& postTickReset
					&& postTickUpdates,
				zombieLeanerType = zombieLeaner.GetType().Name,
				humanLeanerType = human?.Drawer?.leaner?.GetType().Name,
				humanHasZombieLeaner,
				drawPos = new
				{
					vanilla = DescribeVector(vanillaDrawPos),
					patched = DescribeVector(patchedDrawPos),
					delta = DescribeVector(drawDelta),
					expectedDelta = DescribeVector(forcedOffset),
					appliesOffset = drawPosAppliesOffset
				},
				postTickReset = new
				{
					offset = DescribeVector(resetOffset),
					reset = postTickReset,
					state = ZombieState.Emerging.ToString()
				},
				postTickUpdate = new
				{
					offset = DescribeVector(updatedOffset),
					updated = postTickUpdates,
					state = ZombieState.Wandering.ToString()
				}
			};
		}

		static bool FindVisualSupportCells(Map map, IntVec3 root, out IntVec3[] cells, out object error)
		{
			cells = GenRadial.RadialCellsAround(root, 12f, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.OrderBy(cell => cell.DistanceToSquared(root))
				.Take(2)
				.ToArray();
			if (cells.Length >= 2)
			{
				error = null;
				return true;
			}

			error = new
			{
				success = false,
				root = ZombieRuntimeActions.DescribeCell(root),
				foundCells = cells.Length,
				error = "Could not find enough cells for the visual-support fixture."
			};
			return false;
		}

		static bool VectorsApproximatelyEqual(Vector3 actual, Vector3 expected, float tolerance)
		{
			return Mathf.Abs(actual.x - expected.x) <= tolerance
				&& Mathf.Abs(actual.y - expected.y) <= tolerance
				&& Mathf.Abs(actual.z - expected.z) <= tolerance;
		}

		static object VerifyAreaWorkflowAnimalResponse(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			var settingsSnapshot = SnapshotZombieSettings();
			var manhunterPatchTargets = PatchedMethodsForPatchClass("PawnUtility_GetManhunterOnDamageChance_Patch");
			var preyPatchTargets = PatchedMethodsForPatchClass("FoodUtility_GetPreyScoreFor_Patch");
			try
			{
				if (FindAnimalResponseCells(map, root, out var cells, out var cellError) == false)
					return cellError;

				var manhunterVictim = SpawnAreaWorkflowAnimal(
					map,
					"ZL_Area_AnimalResponseVictim",
					cells[0],
					null,
					spawnedThings,
					def => def.race?.race?.manhunterOnDamageChance > 0f);
				var humanInstigator = SpawnAreaWorkflowPawn(map, "ZL_Area_AnimalResponseHumanInstigator", cells[1], Faction.OfPlayer, spawnedThings);
				var zombieInstigator = SpawnTargetZombie(map, cells[2], ZombieType.Normal, "ZL_Area_AnimalResponseZombieInstigator", spawnedThings);
				var spitterInstigator = SpawnTargetSpitter(map, cells[3], "ZL_Area_AnimalResponseSpitterInstigator", spawnedThings);
				var symbiantInstigator = SpawnTargetSymbiant(map, cells[4], "ZL_Area_AnimalResponseSymbiantInstigator", spawnedThings);
				var predator = SpawnAreaWorkflowAnimal(
					map,
					"ZL_Area_AnimalResponsePredator",
					cells[5],
					null,
					spawnedThings,
					def => def.combatPower > 0f && def.RaceProps?.maxPreyBodySize > 0f);
				var humanPrey = SpawnAreaWorkflowPawn(map, "ZL_Area_AnimalResponseHumanPrey", cells[6], Faction.OfPlayer, spawnedThings);
				var zombiePrey = SpawnTargetZombie(map, cells[7], ZombieType.Normal, "ZL_Area_AnimalResponseZombiePrey", spawnedThings);

				if (manhunterVictim == null || humanInstigator == null || zombieInstigator == null || spitterInstigator == null || symbiantInstigator == null || predator == null || humanPrey == null || zombiePrey == null)
				{
					return new
					{
						success = false,
						manhunterVictim = DescribePawn(manhunterVictim),
						humanInstigator = DescribePawn(humanInstigator),
						zombieInstigator = DescribeZombie(zombieInstigator),
						spitterInstigator = DescribeZombie(spitterInstigator),
						symbiantInstigator = DescribeZombie(symbiantInstigator),
						predator = DescribePawn(predator),
						humanPrey = DescribePawn(humanPrey),
						zombiePrey = DescribeZombie(zombiePrey),
						error = "Could not create all animal-response fixtures."
					};
				}

				var manhunter = VerifyAnimalResponseManhunter(manhunterVictim, humanInstigator, zombieInstigator, spitterInstigator, symbiantInstigator);
				var preyScores = VerifyAnimalResponsePreyScores(predator, humanPrey, zombiePrey, spitterInstigator, symbiantInstigator);

				return new
				{
					success = manhunterPatchTargets.Length > 0
						&& preyPatchTargets.Length > 0
						&& ObjectSuccess(manhunter)
						&& ObjectSuccess(preyScores),
					patchTargets = new
					{
						manhunter = manhunterPatchTargets,
						prey = preyPatchTargets
					},
					fixtures = new
					{
						manhunterVictim = DescribePawn(manhunterVictim),
						humanInstigator = DescribePawn(humanInstigator),
						zombieInstigator = DescribeZombie(zombieInstigator),
						spitterInstigator = DescribeZombie(spitterInstigator),
						symbiantInstigator = DescribeZombie(symbiantInstigator),
						predator = DescribePawn(predator),
						humanPrey = DescribePawn(humanPrey),
						zombiePrey = DescribeZombie(zombiePrey)
					},
					manhunter,
					preyScores
				};
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
			}
		}

		static bool FindAnimalResponseCells(Map map, IntVec3 root, out IntVec3[] cells, out object error)
		{
			var candidates = map.AllCells
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.ToArray();
			var used = new HashSet<IntVec3>();
			var anchors = new[]
			{
				root,
				root + new IntVec3(2, 0, 0),
				root + new IntVec3(4, 0, 0),
				root + new IntVec3(6, 0, 0),
				root + new IntVec3(8, 0, 0),
				root + new IntVec3(0, 0, 12),
				root + new IntVec3(100, 0, 12),
				root + new IntVec3(1, 0, 12)
			};
			cells = anchors
				.Select(anchor =>
				{
					var cell = candidates
						.Where(candidate => used.Contains(candidate) == false)
						.OrderBy(candidate => candidate.DistanceToSquared(anchor))
						.FirstOrDefault();
					if (cell.IsValid)
						_ = used.Add(cell);
					return cell;
				})
				.ToArray();
			if (cells.All(cell => cell.IsValid))
			{
				error = null;
				return true;
			}

			error = new
			{
				success = false,
				root = ZombieRuntimeActions.DescribeCell(root),
				foundCells = cells.Count(cell => cell.IsValid),
				error = "Could not find enough cells for the animal-response fixture."
			};
			return false;
		}

		static object VerifyAnimalResponseManhunter(Pawn victim, Pawn humanInstigator, Zombie zombieInstigator, ZombieSpitter spitterInstigator, ZombieSymbiant symbiantInstigator)
		{
			const float distance = 3f;
			var settings = ZombieSettings.Values;
			var humanExpected = EstimateVanillaManhunterChance(victim, humanInstigator, distance);
			var zombieExpected = EstimateVanillaManhunterChance(victim, zombieInstigator, distance);
			var originalSetting = settings.zombiesCauseManhuntingResponse;
			try
			{
				settings.zombiesCauseManhuntingResponse = true;
				var humanEnabled = PawnUtility.GetManhunterOnDamageChance(victim, humanInstigator, distance);
				var zombieEnabled = PawnUtility.GetManhunterOnDamageChance(victim, zombieInstigator, distance);
				var spitterEnabled = PawnUtility.GetManhunterOnDamageChance(victim, spitterInstigator, distance);
				var symbiantEnabled = PawnUtility.GetManhunterOnDamageChance(victim, symbiantInstigator, distance);

				settings.zombiesCauseManhuntingResponse = false;
				var humanDisabled = PawnUtility.GetManhunterOnDamageChance(victim, humanInstigator, distance);
				var zombieDisabled = PawnUtility.GetManhunterOnDamageChance(victim, zombieInstigator, distance);
				var spitterDisabled = PawnUtility.GetManhunterOnDamageChance(victim, spitterInstigator, distance);
				var symbiantDisabled = PawnUtility.GetManhunterOnDamageChance(victim, symbiantInstigator, distance);

				var expectedZombieEnabled = zombieExpected / 20f;
				return new
				{
					success = humanExpected > 0f
						&& Approximately(humanEnabled, humanExpected)
						&& Approximately(humanDisabled, humanExpected)
						&& zombieExpected > 0f
						&& Approximately(zombieEnabled, expectedZombieEnabled)
						&& Approximately(zombieDisabled, 0f)
						&& Approximately(spitterEnabled, 0f)
						&& Approximately(spitterDisabled, 0f)
						&& Approximately(symbiantEnabled, 0f)
						&& Approximately(symbiantDisabled, 0f),
					distance,
					human = new
					{
						expectedVanilla = humanExpected,
						enabled = humanEnabled,
						disabled = humanDisabled
					},
					normalZombie = new
					{
						expectedVanilla = zombieExpected,
						expectedEnabled = expectedZombieEnabled,
						enabled = zombieEnabled,
						disabled = zombieDisabled
					},
					spitter = new
					{
						enabled = spitterEnabled,
						disabled = spitterDisabled
					},
					symbiant = new
					{
						enabled = symbiantEnabled,
						disabled = symbiantDisabled
					},
					victimManhunterOnDamageChance = victim.def?.race?.manhunterOnDamageChance,
					settingRestoredTo = originalSetting
				};
			}
			finally
			{
				settings.zombiesCauseManhuntingResponse = originalSetting;
			}
		}

		static float EstimateVanillaManhunterChance(Pawn victim, Thing instigator, float distance)
		{
			var chance = PawnUtility.GetManhunterOnDamageChance(victim.def);
			if (victim.health.hediffSet.HasHediff(HediffDefOf.Scaria))
				chance += 0.5f;
			if (instigator != null)
			{
				chance *= GenMath.LerpDoubleClamped(1f, 30f, 3f, 1f, distance);
				chance *= 1f - instigator.GetStatValue(StatDefOf.HuntingStealth);
				if (instigator is Pawn pawn)
					chance *= PawnUtility.GetManhunterChanceFactorForInstigator(pawn);
			}
			return Mathf.Clamp01(chance);
		}

		static object VerifyAnimalResponsePreyScores(Pawn predator, Pawn humanPrey, Zombie zombiePrey, ZombieSpitter spitterPrey, ZombieSymbiant symbiantPrey)
		{
			var settings = ZombieSettings.Values;
			var originalSetting = settings.animalsAttackZombies;
			try
			{
				settings.animalsAttackZombies = false;
				var humanDisabled = FoodUtility.GetPreyScoreFor(predator, humanPrey);
				var zombieDisabled = FoodUtility.GetPreyScoreFor(predator, zombiePrey);
				var spitterDisabled = FoodUtility.GetPreyScoreFor(predator, spitterPrey);
				var symbiantDisabled = FoodUtility.GetPreyScoreFor(predator, symbiantPrey);

				settings.animalsAttackZombies = true;
				var humanEnabled = FoodUtility.GetPreyScoreFor(predator, humanPrey);
				var zombieEnabled = FoodUtility.GetPreyScoreFor(predator, zombiePrey);
				var spitterEnabled = FoodUtility.GetPreyScoreFor(predator, spitterPrey);
				var symbiantEnabled = FoodUtility.GetPreyScoreFor(predator, symbiantPrey);

				return new
				{
					success = Approximately(humanEnabled, humanDisabled)
						&& humanDisabled > zombieDisabled
						&& zombieEnabled > zombieDisabled + 9000f
						&& zombieEnabled > humanEnabled
						&& Approximately(spitterDisabled, 0f)
						&& Approximately(spitterEnabled, 0f)
						&& Approximately(symbiantDisabled, 0f)
						&& Approximately(symbiantEnabled, 0f),
					human = new
					{
						disabled = humanDisabled,
						enabled = humanEnabled
					},
					normalZombie = new
					{
						disabled = zombieDisabled,
						enabled = zombieEnabled,
						enabledDelta = zombieEnabled - zombieDisabled
					},
					spitter = new
					{
						disabled = spitterDisabled,
						enabled = spitterEnabled
					},
					symbiant = new
					{
						disabled = symbiantDisabled,
						enabled = symbiantEnabled
					},
					distances = new
					{
						human = (predator.Position - humanPrey.Position).LengthHorizontal,
						normalZombie = (predator.Position - zombiePrey.Position).LengthHorizontal,
						spitter = (predator.Position - spitterPrey.Position).LengthHorizontal,
						symbiant = (predator.Position - symbiantPrey.Position).LengthHorizontal
					},
					settingRestoredTo = originalSetting
				};
			}
			finally
			{
				settings.animalsAttackZombies = originalSetting;
			}
		}

		static object VerifyAreaWorkflowElectrifierMeleeDamage(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			var settingsSnapshot = SnapshotZombieSettings();
			var patchTargets = PatchedMethodsForPatchClass("Pawn_MeleeVerbs_ChooseMeleeVerb_Patch");
			try
			{
				ApplyZombieSettingsOverride(settings => settings.threatScale = 1f);

				var chainsawShock = VerifyElectrifierChainsawShock(map, root, spawnedThings);
				var smokepopConversion = VerifyElectrifierSmokepopConversion(map, root + new IntVec3(8, 0, 0), spawnedThings);
				var componentCarrier = VerifyElectrifierComponentCarrierDamage(map, root + new IntVec3(16, 0, 0), spawnedThings);
				var disabledFallback = VerifyElectrifierDisabledFallback(map, root + new IntVec3(24, 0, 0), spawnedThings);

				return new
				{
					success = patchTargets.Length > 0
						&& ObjectSuccess(chainsawShock)
						&& ObjectSuccess(smokepopConversion)
						&& ObjectSuccess(componentCarrier)
						&& ObjectSuccess(disabledFallback),
					patchTargets = new
					{
						damageInfosToApply = patchTargets
					},
					settings = new
					{
						ZombieSettings.Values.threatScale
					},
					chainsawShock,
					smokepopConversion,
					componentCarrier,
					disabledFallback
				};
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
			}
		}

		static object VerifyElectrifierChainsawShock(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			if (TryFindAdjacentPawnPairCells(map, root, out var zombieCell, out var targetCell, out var cellError) == false)
				return cellError;

			var zombie = SpawnTargetZombie(map, zombieCell, ZombieType.Electrifier, "ZL_Area_ElectricDamageChainsaw", spawnedThings);
			var target = SpawnMeleeAreaWorkflowPawn(map, "ZL_Area_ElectricDamageChainsawTarget", targetCell, Faction.OfPlayer, spawnedThings);
			NormalizeFireDamagePawn(target);
			if (zombie == null || target == null)
			{
				return new
				{
					success = false,
					zombie = DescribeZombie(zombie),
					target = DescribePawn(target),
					error = "Could not create the electrifier chainsaw shock fixture."
				};
			}

			zombie.electricDisabledUntil = GenTicks.TicksGame - 1;
			if (TryEquipAreaWorkflowChainsaw(map, target, targetCell, spawnedThings, out var chainsaw, out var chainsawError) == false)
				return chainsawError;

			var stalledBefore = chainsaw.stalledCounter;
			var damageProbe = ProbeElectrifierDamageInfos(zombie, target, out var damageInfos);
			var stalledAfter = chainsaw.stalledCounter;

			return new
			{
				success = ObjectSuccess(damageProbe)
					&& zombie.IsActiveElectric
					&& target.equipment?.Primary == chainsaw
					&& stalledBefore == 0
					&& stalledAfter == 120
					&& damageInfos.Any(info => info.Def != CustomDefs.ElectricalShock),
				zombie = DescribeZombie(zombie),
				target = DescribePawn(target),
				chainsaw = new
				{
					def = chainsaw.def?.defName,
					stalledBefore,
					stalledAfter
				},
				damageProbe
			};
		}

		static object VerifyElectrifierSmokepopConversion(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			if (TryFindAdjacentPawnPairCells(map, root, out var zombieCell, out var targetCell, out var cellError) == false)
				return cellError;

			var apparelDef = DefDatabase<ThingDef>.GetNamed("Apparel_SmokepopBelt", false);
			if (apparelDef == null)
			{
				return new
				{
					success = false,
					error = "ThingDef Apparel_SmokepopBelt was not found."
				};
			}

			var zombie = SpawnTargetZombie(map, zombieCell, ZombieType.Electrifier, "ZL_Area_ElectricDamageSmokepop", spawnedThings);
			var target = SpawnMeleeAreaWorkflowPawn(map, "ZL_Area_ElectricDamageSmokepopTarget", targetCell, Faction.OfPlayer, spawnedThings);
			NormalizeFireDamagePawn(target);
			if (zombie == null || target == null)
			{
				return new
				{
					success = false,
					zombie = DescribeZombie(zombie),
					target = DescribePawn(target),
					error = "Could not create the electrifier smokepop conversion fixture."
				};
			}

			zombie.electricDisabledUntil = GenTicks.TicksGame - 1;
			var belt = ThingMaker.MakeThing(apparelDef) as Apparel;
			if (belt == null)
			{
				return new
				{
					success = false,
					error = "Apparel_SmokepopBelt did not create Apparel."
				};
			}
			target.apparel.Wear(belt, false);

			var damageProbe = ProbeElectrifierDamageInfos(zombie, target, out var damageInfos);
			var converted = damageInfos.Any(info => info.Def == CustomDefs.ElectricalShock && info.Weapon == CustomDefs.ElectricalField);

			return new
			{
				success = ObjectSuccess(damageProbe)
					&& zombie.IsActiveElectric
					&& converted,
				zombie = DescribeZombie(zombie),
				target = DescribePawn(target),
				belt = belt.def?.defName,
				converted,
				damageProbe
			};
		}

		static object VerifyElectrifierComponentCarrierDamage(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			if (TryFindAdjacentPawnPairCells(map, root, out var zombieCell, out var targetCell, out var cellError) == false)
				return cellError;

			var zombie = SpawnTargetZombie(map, zombieCell, ZombieType.Electrifier, "ZL_Area_ElectricDamageComponent", spawnedThings);
			var target = SpawnMeleeAreaWorkflowPawn(map, "ZL_Area_ElectricDamageComponentTarget", targetCell, Faction.OfPlayer, spawnedThings);
			NormalizeFireDamagePawn(target);
			if (zombie == null || target?.inventory?.innerContainer == null)
			{
				return new
				{
					success = false,
					zombie = DescribeZombie(zombie),
					target = DescribePawn(target),
					error = "Could not create the electrifier component-carrier fixture."
				};
			}

			zombie.electricDisabledUntil = GenTicks.TicksGame - 1;
			var componentItem = ThingMaker.MakeThing(CustomDefs.Chainsaw) as ThingWithComps;
			if (componentItem == null)
			{
				return new
				{
					success = false,
					error = "Could not create inventory component-cost item."
				};
			}
			componentItem.HitPoints = componentItem.MaxHitPoints;
			var added = target.inventory.innerContainer.TryAdd(componentItem, true);
			var hasComponentCost = componentItem.def?.costList?.Any(cost => cost.thingDef == ThingDefOf.ComponentIndustrial || cost.thingDef == ThingDefOf.ComponentSpacer) == true;
			var hitPointsBefore = componentItem.HitPoints;
			var damageProbe = ProbeElectrifierDamageInfos(zombie, target, out var damageInfos);
			var hitPointsAfter = componentItem.Destroyed ? 0 : componentItem.HitPoints;

			return new
			{
				success = added
					&& hasComponentCost
					&& ObjectSuccess(damageProbe)
					&& zombie.IsActiveElectric
					&& hitPointsAfter < hitPointsBefore
					&& damageInfos.All(info => info.Def != CustomDefs.ElectricalShock),
				zombie = DescribeZombie(zombie),
				target = DescribePawn(target),
				componentItem = new
				{
					def = componentItem.def?.defName,
					added,
					hasComponentCost,
					hitPointsBefore,
					hitPointsAfter,
					hitPointDelta = hitPointsAfter - hitPointsBefore
				},
				damageProbe
			};
		}

		static object VerifyElectrifierDisabledFallback(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			if (TryFindAdjacentPawnPairCells(map, root, out var zombieCell, out var targetCell, out var cellError) == false)
				return cellError;

			var zombie = SpawnTargetZombie(map, zombieCell, ZombieType.Electrifier, "ZL_Area_ElectricDamageDisabled", spawnedThings);
			var target = SpawnMeleeAreaWorkflowPawn(map, "ZL_Area_ElectricDamageDisabledTarget", targetCell, Faction.OfPlayer, spawnedThings);
			NormalizeFireDamagePawn(target);
			if (zombie == null || target?.inventory?.innerContainer == null)
			{
				return new
				{
					success = false,
					zombie = DescribeZombie(zombie),
					target = DescribePawn(target),
					error = "Could not create the disabled electrifier fallback fixture."
				};
			}

			zombie.electricDisabledUntil = GenTicks.TicksGame + GenDate.TicksPerHour;
			var componentItem = ThingMaker.MakeThing(CustomDefs.Chainsaw) as ThingWithComps;
			if (componentItem == null)
			{
				return new
				{
					success = false,
					error = "Could not create disabled fallback component-cost item."
				};
			}
			componentItem.HitPoints = componentItem.MaxHitPoints;
			var added = target.inventory.innerContainer.TryAdd(componentItem, true);
			var hitPointsBefore = componentItem.HitPoints;
			var damageProbe = ProbeElectrifierDamageInfos(zombie, target, out var damageInfos);
			var hitPointsAfter = componentItem.Destroyed ? 0 : componentItem.HitPoints;

			return new
			{
				success = added
					&& ObjectSuccess(damageProbe)
					&& zombie.IsActiveElectric == false
					&& hitPointsAfter == hitPointsBefore
					&& damageInfos.Length > 0
					&& damageInfos.All(info => info.Def != CustomDefs.ElectricalShock),
				zombie = DescribeZombie(zombie),
				target = DescribePawn(target),
				componentItem = new
				{
					def = componentItem.def?.defName,
					added,
					hitPointsBefore,
					hitPointsAfter,
					hitPointDelta = hitPointsAfter - hitPointsBefore
				},
				damageProbe
			};
		}

		sealed class ElectrifierDamageProbe
		{
			public bool success { get; set; }
			public object zombie { get; set; }
			public object target { get; set; }
			public object verb { get; set; }
			public object[] damageInfos { get; set; }
			public string[] damageInfoDefs { get; set; }
			public int electricalShockCount { get; set; }
			public int electricalFieldCount { get; set; }
			public string error { get; set; }
		}

		static object ProbeElectrifierDamageInfos(Zombie zombie, Pawn target, out DamageInfo[] damageInfos)
		{
			damageInfos = Array.Empty<DamageInfo>();
			var verb = zombie?.meleeVerbs?.TryGetMeleeVerb(target);
			if (TryMeleeDamageInfosToApply(verb, target, out damageInfos, out var error) == false)
			{
				return new ElectrifierDamageProbe
				{
					success = false,
					zombie = DescribeZombie(zombie),
					target = DescribePawn(target),
					verb = DescribeVerb(verb),
					error = error
				};
			}

			return new ElectrifierDamageProbe
			{
				success = damageInfos.Length > 0,
				zombie = DescribeZombie(zombie),
				target = DescribePawn(target),
				verb = DescribeVerb(verb),
				damageInfos = DescribeDamageInfos(damageInfos),
				damageInfoDefs = damageInfos.Select(info => info.Def?.defName).ToArray(),
				electricalShockCount = damageInfos.Count(info => info.Def == CustomDefs.ElectricalShock),
				electricalFieldCount = damageInfos.Count(info => info.Weapon == CustomDefs.ElectricalField)
			};
		}

		static object VerifyAreaWorkflowRangedProjectilePatches(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			var settingsSnapshot = SnapshotZombieSettings();
			var oldColonistsHitChance = Constants.COLONISTS_HIT_ZOMBIES_CHANCE;
			var forcedMissRadiusField = AccessTools.Field(typeof(VerbProperties), "forcedMissRadius");
			var warmupTimeField = AccessTools.Field(typeof(VerbProperties), "warmupTime");
			var impactSomething = AccessTools.Method(typeof(Projectile), "ImpactSomething");
			var destinationField = AccessTools.Field(typeof(Projectile), "destination");
			var projectilePatchTargets = PatchedMethodsForPatchClass("Projectile_ImpactSomething_Patch");
			var launchPatchTargets = PatchedMethodsForPatchClass("Verb_LaunchProjectile_TryCastShot_Patch");
			try
			{
				ApplyZombieSettingsOverride(settings => settings.threatScale = 1f);
				Constants.COLONISTS_HIT_ZOMBIES_CHANCE = 1f;

				if (forcedMissRadiusField == null || warmupTimeField == null || impactSomething == null || destinationField == null)
				{
					return new
					{
						success = false,
						error = "Could not reflect all projectile patch probe members.",
						reflection = new
						{
							forcedMissRadiusField = forcedMissRadiusField != null,
							warmupTimeField = warmupTimeField != null,
							impactSomething = impactSomething != null,
							destinationField = destinationField != null
						},
						patchTargets = new
						{
							projectileImpact = projectilePatchTargets,
							launchProjectile = launchPatchTargets
						}
					};
				}

				if (TryFindClearSpawnCell(map, root, 16f, out var shooterCell, out var shooterError) == false)
					return shooterError;
				var targetCell = GenRadial.RadialCellsAround(shooterCell, 12f, false)
					.Where(cell => cell.InBounds(map))
					.Where(cell => cell.Standable(map))
					.Where(cell => cell.Fogged(map) == false)
					.Where(cell => cell.GetFirstPawn(map) == null)
					.Where(cell => cell.DistanceTo(shooterCell) >= 6f)
					.Where(cell => cell.DistanceTo(shooterCell) <= 12f)
					.Where(cell => GenSight.LineOfSight(shooterCell, cell, map, true))
					.OrderBy(cell => cell.DistanceToSquared(shooterCell))
					.FirstOrDefault();
				if (targetCell.IsValid == false)
				{
					return new
					{
						success = false,
						shooterCell = ZombieRuntimeActions.DescribeCell(shooterCell),
						error = "No line-of-sight target cell was found for the ranged projectile patch fixture."
					};
				}

				var shooter = SpawnArmedAreaWorkflowPawn(map, "ZL_Area_RangedPatchShooter", shooterCell, Faction.OfPlayer, spawnedThings);
				var zombie = SpawnTargetZombie(map, targetCell, ZombieType.Normal, "ZL_Area_RangedPatchZombie", spawnedThings);
				if (shooter == null || zombie == null)
				{
					return new
					{
						success = false,
						shooter = DescribePawn(shooter),
						zombie = DescribeZombie(zombie),
						error = "Could not create the ranged projectile patch fixture."
					};
				}
				if (TryMakeDownedForCombat(zombie, out var downedError) == false)
				{
					return new
					{
						success = false,
						shooter = DescribePawn(shooter),
						zombie = DescribeZombie(zombie),
						error = downedError
					};
				}

				var verb = shooter.equipment?.PrimaryEq?.PrimaryVerb as Verb_LaunchProjectile;
				if (verb == null)
				{
					return new
					{
						success = false,
						shooter = DescribePawn(shooter),
						zombie = DescribeZombie(zombie),
						error = "The shooter has no launch-projectile verb."
					};
				}

				var helperProbe = VerifyRangedProjectilePatchHelpers(verb, shooter, zombie);
				var deathStateTransitions = VerifyZombieDeathStateTransitionPatches(map, root + new IntVec3(10, 0, 0), spawnedThings);
				var oldForcedMissRadius = (float)forcedMissRadiusField.GetValue(verb.verbProps);
				var oldWarmupTime = (float)warmupTimeField.GetValue(verb.verbProps);
				var projectilesBefore = map.listerThings.AllThings.OfType<Projectile>().Select(projectile => projectile.ThingID).ToHashSet();
				Projectile launchedProjectile = null;
				var castResult = false;
				object castError = null;
				try
				{
					forcedMissRadiusField.SetValue(verb.verbProps, 9f);
					warmupTimeField.SetValue(verb.verbProps, 0f);
					castResult = verb.TryStartCastOn(zombie, surpriseAttack: false, canHitNonTargetPawns: true, preventFriendlyFire: false, nonInterruptingSelfCast: false);
					launchedProjectile = map.listerThings.AllThings
						.OfType<Projectile>()
						.FirstOrDefault(projectile => projectilesBefore.Contains(projectile.ThingID) == false);
					if (launchedProjectile != null)
						spawnedThings.Add(launchedProjectile);
				}
				catch (Exception ex)
				{
					castError = ex.GetBaseException().Message;
				}
				finally
				{
					forcedMissRadiusField.SetValue(verb.verbProps, oldForcedMissRadius);
					warmupTimeField.SetValue(verb.verbProps, oldWarmupTime);
				}

				var destination = launchedProjectile == null ? Vector3.zero : (Vector3)destinationField.GetValue(launchedProjectile);
				var destinationCell = destination.ToIntVec3();
				var destinationHitsZombie = launchedProjectile != null && destinationCell == zombie.Position;
				var intendedTargetIsZombie = launchedProjectile != null && launchedProjectile.intendedTarget.Thing == zombie;
				var usedTargetIsZombie = launchedProjectile != null && launchedProjectile.usedTarget.Thing == zombie;

				var injuryBeforeImpact = TotalInjurySeverity(zombie);
				var vanillaWouldMissSeed = FindRandChanceSeed(false, 0.5f);
				object impactError = null;
				var randPushed = false;
				try
				{
					if (launchedProjectile != null)
					{
						Rand.PushState(vanillaWouldMissSeed);
						randPushed = true;
						impactSomething.Invoke(launchedProjectile, Array.Empty<object>());
						Rand.PopState();
						randPushed = false;
					}
				}
				catch (Exception ex)
				{
					if (randPushed)
						Rand.PopState();
					impactError = ex.GetBaseException().Message;
				}
				var injuryAfterImpact = TotalInjurySeverity(zombie);
				var impactDamagedDownedZombie = injuryAfterImpact > injuryBeforeImpact || zombie.Dead;

				return new
				{
					success = projectilePatchTargets.Length > 0
						&& launchPatchTargets.Length > 0
						&& ObjectSuccess(helperProbe)
						&& ObjectSuccess(deathStateTransitions)
						&& castResult
						&& castError == null
						&& launchedProjectile != null
						&& intendedTargetIsZombie
						&& usedTargetIsZombie
						&& destinationHitsZombie
						&& impactError == null
						&& impactDamagedDownedZombie,
					patchTargets = new
					{
						projectileImpact = projectilePatchTargets,
						launchProjectile = launchPatchTargets
					},
					shooterCell = ZombieRuntimeActions.DescribeCell(shooterCell),
					targetCell = ZombieRuntimeActions.DescribeCell(targetCell),
					distanceSquared = (targetCell - shooterCell).LengthHorizontalSquared,
					settings = new
					{
						ZombieSettings.Values.threatScale,
						Constants.COLONISTS_HIT_ZOMBIES_CHANCE
					},
					shooter = DescribePawn(shooter),
					zombie = DescribeZombie(zombie),
					verb = DescribeVerb(verb),
					helperProbe,
					deathStateTransitions,
					forcedMissRadius = new
					{
						before = oldForcedMissRadius,
						probe = 9f,
						after = (float)forcedMissRadiusField.GetValue(verb.verbProps)
					},
					warmupTime = new
					{
						before = oldWarmupTime,
						probe = 0f,
						after = (float)warmupTimeField.GetValue(verb.verbProps)
					},
					cast = new
					{
						castResult,
						castError,
						projectileDef = launchedProjectile?.def?.defName,
						projectileThingId = launchedProjectile?.ThingID,
						usedTargetIsZombie,
						intendedTargetIsZombie,
						destination = DescribeVector(destination),
						destinationCell = launchedProjectile == null ? null : ZombieRuntimeActions.DescribeCell(destinationCell),
						destinationHitsZombie
					},
					impact = new
					{
						vanillaWouldMissSeed,
						impactError,
						injuryBeforeImpact,
						injuryAfterImpact,
						injuryDelta = injuryAfterImpact - injuryBeforeImpact,
						impactDamagedDownedZombie,
						zombieDead = zombie.Dead
					}
				};
			}
			finally
			{
				Constants.COLONISTS_HIT_ZOMBIES_CHANCE = oldColonistsHitChance;
				RestoreZombieSettings(settingsSnapshot);
			}
		}

		static object VerifyRangedProjectilePatchHelpers(Verb verb, Pawn shooter, Zombie zombie)
		{
			var postureFix = FindNestedPatchMethod("Projectile_ImpactSomething_Patch", "GetPostureFix");
			var randChance = FindNestedPatchMethod("Projectile_ImpactSomething_Patch", "RandChance");
			var skipMissing = FindNestedPatchMethod("Verb_LaunchProjectile_TryCastShot_Patch", "SkipMissingShotsAtZombies");
			if (postureFix == null || randChance == null || skipMissing == null)
			{
				return new
				{
					success = false,
					postureFixFound = postureFix != null,
					randChanceFound = randChance != null,
					skipMissingFound = skipMissing != null
				};
			}

			var zombiePosture = (PawnPosture)postureFix.Invoke(null, new object[] { zombie });
			var shooterPosture = (PawnPosture)postureFix.Invoke(null, new object[] { shooter });
			var zombieHalfChance = (bool)randChance.Invoke(null, new object[] { 0.5f, zombie });
			var nonZombieZeroChance = (bool)randChance.Invoke(null, new object[] { 0f, shooter });
			var skipMissingAtZombie = (bool)skipMissing.Invoke(null, new object[] { verb, new LocalTargetInfo(zombie) });
			var skipMissingAtShooter = (bool)skipMissing.Invoke(null, new object[] { verb, new LocalTargetInfo(shooter) });

			return new
			{
				success = zombiePosture == PawnPosture.Standing
					&& shooterPosture == shooter.GetPosture()
					&& zombieHalfChance
					&& nonZombieZeroChance == false
					&& skipMissingAtZombie
					&& skipMissingAtShooter == false,
				zombiePosture = zombiePosture.ToString(),
				shooterPosture = shooterPosture.ToString(),
				shooterActualPosture = shooter.GetPosture().ToString(),
				zombieHalfChance,
				nonZombieZeroChance,
				skipMissingAtZombie,
				skipMissingAtShooter
			};
		}

		static object VerifyZombieDeathStateTransitionPatches(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			var headshotMethod = AccessTools.Method(typeof(DamageWorker_AddInjury), "IsHeadshot");
			var headshotTargets = PatchedMethodsForPatchClass("DamageWorker_AddInjury_IsHeadshot_Patch");
			var addDirectTargets = PatchedMethodsForPatchClass("HediffSet_AddDirect_Patch");
			if (headshotMethod == null)
			{
				return new
				{
					success = false,
					error = "Could not reflect DamageWorker_AddInjury.IsHeadshot.",
					patchTargets = new
					{
						headshot = headshotTargets,
						addDirect = addDirectTargets
					}
				};
			}

			var painFieldDef = DefDatabase<HediffDef>.GetNamedSilentFail("PainField");
			var agonyPulseDef = DefDatabase<HediffDef>.GetNamedSilentFail("AgonyPulse");
			var requiredCells = 4 + new[] { painFieldDef, agonyPulseDef }.Count(def => def != null);
			var cells = GenRadial.RadialCellsAround(root, 14f, true)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.Take(requiredCells)
				.ToArray();
			if (cells.Length < requiredCells)
			{
				return new
				{
					success = false,
					root = ZombieRuntimeActions.DescribeCell(root),
					cellCount = cells.Length,
					requiredCells,
					error = "Not enough clear cells were found for the zombie death-state transition fixture."
				};
			}

			var headshotZombie = SpawnTargetZombie(map, cells[0], ZombieType.Normal, "ZL_Area_HeadshotStateZombie", spawnedThings);
			var bodyshotZombie = SpawnTargetZombie(map, cells[1], ZombieType.Normal, "ZL_Area_BodyshotStateZombie", spawnedThings);
			var brainInjuryZombie = SpawnTargetZombie(map, cells[2], ZombieType.Normal, "ZL_Area_BrainInjuryStateZombie", spawnedThings);
			var limbInjuryZombie = SpawnTargetZombie(map, cells[3], ZombieType.Normal, "ZL_Area_LimbInjuryStateZombie", spawnedThings);
			var nextCell = 4;
			var painFieldZombie = painFieldDef == null ? null : SpawnTargetZombie(map, cells[nextCell++], ZombieType.Normal, "ZL_Area_PainFieldStateZombie", spawnedThings);
			var agonyPulseZombie = agonyPulseDef == null ? null : SpawnTargetZombie(map, cells[nextCell++], ZombieType.Normal, "ZL_Area_AgonyPulseStateZombie", spawnedThings);
			var fixtureZombies = new[] { headshotZombie, bodyshotZombie, brainInjuryZombie, limbInjuryZombie, painFieldDef == null ? headshotZombie : painFieldZombie, agonyPulseDef == null ? headshotZombie : agonyPulseZombie };
			if (fixtureZombies.Any(zombie => zombie == null))
			{
				return new
				{
					success = false,
					patchTargets = new
					{
						headshot = headshotTargets,
						addDirect = addDirectTargets
					},
					headshotZombie = DescribeZombie(headshotZombie),
					bodyshotZombie = DescribeZombie(bodyshotZombie),
					brainInjuryZombie = DescribeZombie(brainInjuryZombie),
					limbInjuryZombie = DescribeZombie(limbInjuryZombie),
					painFieldZombie = DescribeZombie(painFieldZombie),
					agonyPulseZombie = DescribeZombie(agonyPulseZombie),
					error = "Could not create all zombie death-state transition fixtures."
				};
			}

			var headPart = FindHeadshotPart(headshotZombie);
			var bodyPart = FindNonHeadPart(bodyshotZombie);
			var brainPart = brainInjuryZombie.health?.hediffSet?.GetBrain();
			var limbPart = FindNonConsciousnessPart(limbInjuryZombie);
			if (headPart == null || bodyPart == null || brainPart == null || limbPart == null)
			{
				return new
				{
					success = false,
					patchTargets = new
					{
						headshot = headshotTargets,
						addDirect = addDirectTargets
					},
					parts = new
					{
						head = DescribeBodyPartDetail(headPart),
						body = DescribeBodyPartDetail(bodyPart),
						brain = DescribeBodyPartDetail(brainPart),
						limb = DescribeBodyPartDetail(limbPart)
					},
					error = "Could not find all required body parts for the zombie death-state transition fixture."
				};
			}

			var headDinfo = new DamageInfo(DamageDefOf.Bullet, 1f, 0f, -1f, null, headPart, null, DamageInfo.SourceCategory.ThingOrUnknown, null, true, true);
			var bodyDinfo = new DamageInfo(DamageDefOf.Bullet, 1f, 0f, -1f, null, bodyPart, null, DamageInfo.SourceCategory.ThingOrUnknown, null, true, true);
			var headshotResult = (bool)headshotMethod.Invoke(null, new object[] { headDinfo, headshotZombie });
			var bodyshotResult = (bool)headshotMethod.Invoke(null, new object[] { bodyDinfo, bodyshotZombie });
			var headshotStateAfter = headshotZombie.state;
			var bodyshotStateAfter = bodyshotZombie.state;

			var brainCut = HediffMaker.MakeHediff(HediffDefOf.Cut, brainInjuryZombie, brainPart);
			brainInjuryZombie.health.hediffSet.AddDirect(brainCut, new DamageInfo(DamageDefOf.Cut, 1f), null);
			var brainStateAfter = brainInjuryZombie.state;

			var limbCut = HediffMaker.MakeHediff(HediffDefOf.Cut, limbInjuryZombie, limbPart);
			limbInjuryZombie.health.hediffSet.AddDirect(limbCut, new DamageInfo(DamageDefOf.Cut, 1f), null);
			var limbStateAfter = limbInjuryZombie.state;

			var painFieldEffect = VerifyZombieBrainEffectHediff(painFieldZombie, painFieldZombie?.health?.hediffSet?.GetBrain(), painFieldDef, out var painFieldEffectSuccess);
			var agonyPulseEffect = VerifyZombieBrainEffectHediff(agonyPulseZombie, agonyPulseZombie?.health?.hediffSet?.GetBrain(), agonyPulseDef, out var agonyPulseEffectSuccess);

			return new
			{
				success = headshotTargets.Length > 0
					&& addDirectTargets.Length > 0
					&& headshotResult
					&& headshotStateAfter == ZombieState.ShouldDie
					&& bodyshotResult == false
					&& bodyshotStateAfter != ZombieState.ShouldDie
					&& brainCut.def.isBad
					&& brainPart.def.tags.Contains(BodyPartTagDefOf.ConsciousnessSource)
					&& brainStateAfter == ZombieState.ShouldDie
					&& limbCut.def.isBad
					&& limbPart.def.tags.Contains(BodyPartTagDefOf.ConsciousnessSource) == false
					&& limbStateAfter != ZombieState.ShouldDie
					&& painFieldEffectSuccess
					&& agonyPulseEffectSuccess,
				patchTargets = new
				{
					headshot = headshotTargets,
					addDirect = addDirectTargets
				},
				headshot = new
				{
					zombie = DescribeZombie(headshotZombie),
					part = DescribeBodyPartDetail(headPart),
					result = headshotResult,
					stateAfter = headshotStateAfter.ToString()
				},
				bodyshot = new
				{
					zombie = DescribeZombie(bodyshotZombie),
					part = DescribeBodyPartDetail(bodyPart),
					result = bodyshotResult,
					stateAfter = bodyshotStateAfter.ToString()
				},
				brainInjury = new
				{
					zombie = DescribeZombie(brainInjuryZombie),
					part = DescribeBodyPartDetail(brainPart),
					hediff = brainCut.def.defName,
					isBad = brainCut.def.isBad,
					consciousnessSource = brainPart.def.tags.Contains(BodyPartTagDefOf.ConsciousnessSource),
					stateAfter = brainStateAfter.ToString()
				},
				limbInjury = new
				{
					zombie = DescribeZombie(limbInjuryZombie),
					part = DescribeBodyPartDetail(limbPart),
					hediff = limbCut.def.defName,
					isBad = limbCut.def.isBad,
					consciousnessSource = limbPart.def.tags.Contains(BodyPartTagDefOf.ConsciousnessSource),
					stateAfter = limbStateAfter.ToString()
				},
				painFieldEffect,
				agonyPulseEffect
			};
		}

		static object VerifyZombieBrainEffectHediff(Zombie zombie, BodyPartRecord brainPart, HediffDef hediffDef, out bool success)
		{
			if (hediffDef == null)
			{
				success = true;
				return new
				{
					available = false
				};
			}
			if (zombie == null || brainPart == null)
			{
				success = false;
				return new
				{
					available = true,
					hediff = hediffDef.defName,
					zombie = DescribeZombie(zombie),
					part = DescribeBodyPartDetail(brainPart),
					error = "Could not create a zombie brain-effect fixture."
				};
			}

			var stateBefore = zombie.state;
			var hediff = HediffMaker.MakeHediff(hediffDef, zombie, brainPart);
			if (hediff == null)
			{
				success = false;
				return new
				{
					available = true,
					hediff = hediffDef.defName,
					zombie = DescribeZombie(zombie),
					part = DescribeBodyPartDetail(brainPart),
					error = "Could not create the brain-effect hediff."
				};
			}

			zombie.health.hediffSet.AddDirect(hediff, null, null);
			var stateAfter = zombie.state;
			var structural = hediff is Hediff_Injury || hediff is Hediff_MissingPart;
			success = hediff.def.isBad
				&& structural == false
				&& brainPart.def.tags.Contains(BodyPartTagDefOf.ConsciousnessSource)
				&& stateAfter != ZombieState.ShouldDie;

			return new
			{
				available = true,
				zombie = DescribeZombie(zombie),
				part = DescribeBodyPartDetail(brainPart),
				hediff = hediff.def.defName,
				hediffType = hediff.GetType().Name,
				isBad = hediff.def.isBad,
				structural,
				consciousnessSource = brainPart.def.tags.Contains(BodyPartTagDefOf.ConsciousnessSource),
				stateBefore = stateBefore.ToString(),
				stateAfter = stateAfter.ToString()
			};
		}

		static BodyPartRecord FindHeadshotPart(Pawn pawn)
		{
			return pawn?.health?.hediffSet?
				.GetNotMissingParts()
				.FirstOrDefault(part => part.groups.Contains(BodyPartGroupDefOf.FullHead));
		}

		static BodyPartRecord FindNonHeadPart(Pawn pawn)
		{
			return pawn?.health?.hediffSet?
				.GetNotMissingParts()
				.FirstOrDefault(part => part.groups.Contains(BodyPartGroupDefOf.FullHead) == false && part.depth == BodyPartDepth.Outside);
		}

		static BodyPartRecord FindNonConsciousnessPart(Pawn pawn)
		{
			return pawn?.health?.hediffSet?
				.GetNotMissingParts()
				.FirstOrDefault(part => part.def.tags.Contains(BodyPartTagDefOf.ConsciousnessSource) == false && part.depth == BodyPartDepth.Outside);
		}

		static object DescribeBodyPartDetail(BodyPartRecord part)
		{
			return part == null ? null : new
			{
				def = part.def?.defName,
				label = part.Label,
				height = part.height.ToString(),
				depth = part.depth.ToString(),
				groups = part.groups.Select(group => group.defName).ToArray(),
				tags = part.def.tags.Select(tag => tag.defName).ToArray()
			};
		}

		static MethodInfo FindNestedPatchMethod(string nestedTypeName, string methodName)
		{
			return typeof(Patches)
				.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
				.FirstOrDefault(type => type.Name == nestedTypeName)
				?.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
		}

		static int FindRandChanceSeed(bool desiredResult, float chance)
		{
			for (var seed = 1; seed < 10000; seed++)
			{
				Rand.PushState(seed);
				var result = Rand.Chance(chance);
				Rand.PopState();
				if (result == desiredResult)
					return seed;
			}
			return 1;
		}

		static object VerifyDownedMeleeAttack(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			if (TryFindAdjacentPawnPairCells(map, root, out var actorCell, out var targetCell, out var cellError) == false)
				return cellError;

			var actor = SpawnMeleeAreaWorkflowPawn(map, "ZL_Area_DownedMeleeActor", actorCell, Faction.OfPlayer, spawnedThings);
			EquipAreaWorkflowMeleeWeapon(actor);
			var zombie = SpawnTargetZombie(map, targetCell, ZombieType.Normal, "ZL_Area_DownedMeleeZombie", spawnedThings);
			if (actor == null || zombie == null)
			{
				return new
				{
					success = false,
					actor = DescribePawn(actor),
					zombie = DescribeZombie(zombie),
					error = "Could not create the downed melee fixture."
				};
			}

			if (TryMakeDownedForCombat(zombie, out var downedError) == false)
			{
				return new
				{
					success = false,
					actor = DescribePawn(actor),
					zombie = DescribeZombie(zombie),
					error = downedError
				};
			}

			var healthDownedBefore = zombie.health.Downed;
			var publicDownedBefore = zombie.Downed;
			var injuryBefore = TotalInjurySeverity(zombie);
			var job = JobMaker.MakeJob(JobDefOf.AttackMelee, zombie);
			job.killIncappedTarget = false;
			job.playerForced = false;
			actor.drafter.Drafted = true;
			actor.jobs.StartJob(job, JobCondition.InterruptForced, null, false, true);
			var startedJob = actor.CurJobDef?.defName;
			var samples = new List<object>();
			var attacked = false;
			for (var tick = 1; tick <= 900 && actor.Destroyed == false && zombie.Destroyed == false; tick++)
			{
				AdvanceGameTicks(1);
				var actorStance = actor.stances?.curStance?.GetType().Name;
				attacked |= actor.stances?.curStance is Stance_Cooldown;
				var injuryNow = TotalInjurySeverity(zombie);
				if (tick == 1 || tick == 60 || tick == 180 || tick == 900 || attacked || injuryNow > injuryBefore || zombie.Dead)
				{
					samples.Add(new
					{
						tick,
						actorJob = actor.CurJobDef?.defName,
						actorStance,
						attacked,
						zombieHealthDowned = zombie.health.Downed,
						zombiePublicDowned = zombie.Downed,
						zombieInjury = injuryNow,
						zombieDead = zombie.Dead
					});
					if (attacked || injuryNow > injuryBefore || zombie.Dead)
						break;
				}
			}

			var injuryAfter = TotalInjurySeverity(zombie);
			var damaged = injuryAfter > injuryBefore || zombie.Dead;
			return new
			{
				success = healthDownedBefore
					&& publicDownedBefore
					&& startedJob == JobDefOf.AttackMelee.defName
					&& (attacked || damaged),
				startedJob,
				jobKillIncappedTarget = job.killIncappedTarget,
				actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
				targetCell = ZombieRuntimeActions.DescribeCell(targetCell),
				healthDownedBefore,
				publicDownedBefore,
				injuryBefore,
				injuryAfter,
				injuryDelta = injuryAfter - injuryBefore,
				damaged,
				attacked,
				actor = DescribePawn(actor),
				zombie = DescribeZombie(zombie),
				samples = samples.ToArray()
			};
		}

		static object VerifyDownedAttackStatic(Map map, IntVec3 root, List<Thing> spawnedThings)
		{
			if (TryFindClearSpawnCell(map, root, 16f, out var shooterCell, out var shooterError) == false)
				return shooterError;
			var targetCell = GenRadial.RadialCellsAround(shooterCell, 12f, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.Where(cell => cell.DistanceTo(shooterCell) >= 6f)
				.Where(cell => GenSight.LineOfSight(shooterCell, cell, map, true))
				.OrderBy(cell => cell.DistanceToSquared(shooterCell))
				.FirstOrDefault();
			if (targetCell.IsValid == false)
			{
				return new
				{
					success = false,
					shooterCell = ZombieRuntimeActions.DescribeCell(shooterCell),
					error = "No line-of-sight target cell was found for the downed AttackStatic fixture."
				};
			}

			var shooter = SpawnArmedAreaWorkflowPawn(map, "ZL_Area_DownedStaticShooter", shooterCell, Faction.OfPlayer, spawnedThings);
			var zombie = SpawnTargetZombie(map, targetCell, ZombieType.Normal, "ZL_Area_DownedStaticZombie", spawnedThings);
			if (shooter == null || zombie == null)
			{
				return new
				{
					success = false,
					shooter = DescribePawn(shooter),
					zombie = DescribeZombie(zombie),
					error = "Could not create the downed AttackStatic fixture."
				};
			}
			if (TryMakeDownedForCombat(zombie, out var downedError) == false)
			{
				return new
				{
					success = false,
					shooter = DescribePawn(shooter),
					zombie = DescribeZombie(zombie),
					error = downedError
				};
			}

			RefreshZombieTargetCache(map);
			var healthDownedBefore = zombie.health.Downed;
			var publicDownedBefore = zombie.Downed;
			var injuryBefore = TotalInjurySeverity(zombie);
			var job = JobMaker.MakeJob(JobDefOf.AttackStatic, zombie);
			job.killIncappedTarget = false;
			job.playerForced = false;
			shooter.drafter.Drafted = true;
			shooter.jobs.StartJob(job, JobCondition.InterruptForced, null, false, true);
			var startedJob = shooter.CurJobDef?.defName;
			var verb = shooter.equipment?.PrimaryEq?.PrimaryVerb;
			var samples = new List<object>();
			var attacked = false;
			for (var tick = 1; tick <= 1200 && shooter.Destroyed == false && zombie.Destroyed == false; tick++)
			{
				AdvanceGameTicks(1);
				var shooterStance = shooter.stances?.curStance?.GetType().Name;
				attacked |= shooter.stances?.curStance is Stance_Cooldown;
				var injuryNow = TotalInjurySeverity(zombie);
				if (tick == 1 || tick == 60 || tick == 180 || tick == 600 || tick == 1200 || attacked || injuryNow > injuryBefore || zombie.Dead)
				{
					samples.Add(new
					{
						tick,
						shooterJob = shooter.CurJobDef?.defName,
						shooterStance,
						attacked,
						zombieHealthDowned = zombie.health.Downed,
						zombiePublicDowned = zombie.Downed,
						zombieInjury = injuryNow,
						zombieDead = zombie.Dead
					});
					if (attacked || injuryNow > injuryBefore || zombie.Dead)
						break;
				}
			}

			var injuryAfter = TotalInjurySeverity(zombie);
			var damaged = injuryAfter > injuryBefore || zombie.Dead;
			return new
			{
				success = healthDownedBefore
					&& publicDownedBefore
					&& startedJob == JobDefOf.AttackStatic.defName
					&& (attacked || damaged),
				startedJob,
				jobKillIncappedTarget = job.killIncappedTarget,
				shooterCell = ZombieRuntimeActions.DescribeCell(shooterCell),
				targetCell = ZombieRuntimeActions.DescribeCell(targetCell),
				healthDownedBefore,
				publicDownedBefore,
				injuryBefore,
				injuryAfter,
				injuryDelta = injuryAfter - injuryBefore,
				damaged,
				attacked,
				shooter = DescribePawn(shooter),
				zombie = DescribeZombie(zombie),
				verb = DescribeVerb(verb),
				samples = samples.ToArray()
			};
		}

		static float InvokeFriendlyFireOffset(string methodName, IAttackTarget target, IAttackTargetSearcher searcher, Verb verb)
		{
			var method = typeof(AttackTargetFinder).GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
			return (float)method.Invoke(null, new object[] { target, searcher, verb });
		}

		static string[] PatchOwners(string methodName)
		{
			var method = typeof(AttackTargetFinder).GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			return PatchOwners(method);
		}

		static string[] PatchOwners(Type type, string methodName)
		{
			var method = type?.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			return PatchOwners(method);
		}

		static string[] PatchOwners(MethodBase method)
		{
			if (method == null)
				return Array.Empty<string>();
			var patchInfo = Harmony.GetPatchInfo(method);
			return (patchInfo?.Prefixes ?? Enumerable.Empty<Patch>())
				.Concat(patchInfo?.Postfixes ?? Enumerable.Empty<Patch>())
				.Concat(patchInfo?.Transpilers ?? Enumerable.Empty<Patch>())
				.Select(patch => patch.owner)
				.Distinct()
				.OrderBy(owner => owner)
				.ToArray() ?? Array.Empty<string>();
		}

		static object[] PatchedMethodsForPatchClass(string nestedClassName)
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
						.Where(patch => patch.PatchMethod?.DeclaringType?.Name == nestedClassName)
						.ToArray()
				})
				.Where(entry => entry.patches.Length > 0)
				.Select(entry => new
				{
					method = entry.method.FullDescription(),
					owners = entry.patches.Select(patch => patch.owner).Distinct().OrderBy(owner => owner).ToArray(),
					patchMethods = entry.patches.Select(patch => patch.PatchMethod?.FullDescription()).Distinct().OrderBy(text => text).ToArray()
				})
				.Cast<object>()
				.ToArray();
		}

		static bool TryMakeDownedForCombat(Pawn pawn, out string error)
		{
			error = null;
			if (pawn == null)
			{
				error = "Pawn was null.";
				return false;
			}
			if (pawn.RaceProps.IsFlesh)
			{
				var bloodLoss = HediffMaker.MakeHediff(HediffDefOf.BloodLoss, pawn);
				bloodLoss.Severity = 0.45f;
				pawn.health.hediffSet.AddDirect(bloodLoss);
			}
			var anesthetic = HediffMaker.MakeHediff(HediffDefOf.Anesthetic, pawn);
			anesthetic.Severity = 1f;
			pawn.health.hediffSet.AddDirect(anesthetic);

			var makeDowned = AccessTools.Method(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.MakeDowned));
			if (makeDowned == null)
			{
				error = "Could not reflect Pawn_HealthTracker.MakeDowned.";
				return false;
			}
			makeDowned.Invoke(pawn.health, new object[makeDowned.GetParameters().Length]);
			if (pawn.health.Downed == false)
			{
				error = "Pawn_HealthTracker.MakeDowned did not leave the pawn health-downed.";
				return false;
			}
			return true;
		}

		static Pawn SpawnMeleeAreaWorkflowPawn(Map map, string name, IntVec3 cell, Faction faction, List<Thing> spawnedThings)
		{
			Pawn pawn = null;
			for (var attempt = 0; attempt < 20; attempt++)
			{
				var candidate = GenerateAreaWorkflowPawn(faction, true);
				if (candidate.WorkTagIsDisabled(WorkTags.Violent))
					continue;
				pawn = candidate;
				break;
			}
			pawn ??= GenerateAreaWorkflowPawn(faction, true);
			pawn.Name = new NameSingle(name);
			GenSpawn.Spawn(pawn, cell, map, Rot4.South);
			DisablePawnWork(pawn);
			pawn.equipment?.DestroyAllEquipment(DestroyMode.Vanish);
			pawn.inventory?.DestroyAll();
			spawnedThings.Add(pawn);
			return pawn;
		}

		static void EquipAreaWorkflowMeleeWeapon(Pawn pawn)
		{
			if (pawn?.equipment == null)
				return;
			pawn.equipment.DestroyAllEquipment(DestroyMode.Vanish);
			var weaponDef = DefDatabase<ThingDef>.GetNamed("MeleeWeapon_Club", false)
				?? DefDatabase<ThingDef>.GetNamed("MeleeWeapon_Knife", false)
				?? DefDatabase<ThingDef>.AllDefs.FirstOrDefault(def => def.IsMeleeWeapon);
			var weaponStuff = weaponDef?.MadeFromStuff == true ? GenStuff.DefaultStuffFor(weaponDef) ?? ThingDefOf.Steel : null;
			var weapon = weaponDef == null ? null : ThingMaker.MakeThing(weaponDef, weaponStuff) as ThingWithComps;
			if (weapon != null)
				pawn.equipment.AddEquipment(weapon);
		}

		static bool TryFindAdjacentPawnPairCells(Map map, IntVec3 root, out IntVec3 actorCell, out IntVec3 targetCell, out object error)
		{
			actorCell = IntVec3.Invalid;
			targetCell = IntVec3.Invalid;
			error = null;

			var candidates = GenRadial.RadialCellsAround(root, 12f, true)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.ToArray();
			foreach (var candidate in candidates)
			{
				var adjacent = GenAdj.AdjacentCells
					.Select(offset => candidate + offset)
					.FirstOrDefault(cell => cell.InBounds(map)
						&& cell.Standable(map)
						&& cell.Fogged(map) == false
						&& cell.GetFirstPawn(map) == null);
				if (adjacent.IsValid == false)
					continue;
				actorCell = candidate;
				targetCell = adjacent;
				return true;
			}

			error = new
			{
				success = false,
				root = ZombieRuntimeActions.DescribeCell(root),
				error = "No adjacent clear pawn cells were found for the Wait_Combat auto-attack fixture."
			};
			return false;
		}

		static string StableId(object obj)
		{
			return ZombieRuntimeActions.StableThingId((obj as IAttackTarget)?.Thing ?? obj as Thing);
		}

		static string[] TargetIds(IEnumerable<Pair<IAttackTarget, float>> pairs)
		{
			return pairs.Select(pair => StableId(pair.first)).Where(id => id != null).ToArray();
		}

		static object[] DescribeTargetPairs(IEnumerable<Pair<IAttackTarget, float>> pairs)
		{
			return pairs.Select(pair => new
			{
				id = StableId(pair.first),
				thing = DescribeTarget(pair.first),
				score = pair.second
			}).Cast<object>().ToArray();
		}

		static object DescribeTarget(IAttackTarget target)
		{
			var thing = target?.Thing;
			return new
			{
				id = StableId(target),
				defName = thing?.def?.defName,
				kindDef = (thing as Pawn)?.kindDef?.defName,
				label = thing?.LabelCap,
				position = thing?.Spawned == true ? ZombieRuntimeActions.DescribeCell(thing.Position) : null
			};
		}

		static Area_Allowed EnsureAreaWorkflowArea(Map map, string suffix, AreaRiskMode mode, Color color, IEnumerable<IntVec3> activeCells)
		{
			var label = AreaWorkflowPrefix + suffix;
			var area = map.areaManager.AllAreas.OfType<Area_Allowed>().FirstOrDefault(candidate => candidate.Label == label);
			if (area == null)
			{
				if (map.areaManager.TryMakeNewAllowed(out area) == false)
					throw new InvalidOperationException("Could not create area workflow fixture area " + label + ".");
				area.labelInt = label;
			}
			else if (area.Label != label)
				area.labelInt = label;

			foreach (var cell in area.ActiveCells.ToArray())
				area[cell] = false;
			foreach (var cell in activeCells)
				if (cell.InBounds(map))
					area[cell] = true;

			area.colorInt = color;
			area.colorTextureInt = null;
			area.Drawer.material = null;
			area.Drawer.SetDirty();

			if (mode == AreaRiskMode.Ignore)
				_ = ZombieSettings.Values.dangerousAreas.Remove(area);
			else
				ZombieSettings.Values.dangerousAreas[area] = mode;

			return area;
		}

		static (Area_Allowed area, AreaRiskMode mode)[] FindAreaWorkflowAreas(Map map)
		{
			return map.areaManager.AllAreas
				.OfType<Area_Allowed>()
				.Where(area => area.Label.StartsWith(AreaWorkflowPrefix, StringComparison.Ordinal))
				.Select(area => (area, Dialog_ManageAreas_Patches.GetMode(area)))
				.OrderBy(pair => pair.area.Label)
				.ToArray();
		}

	}
}
