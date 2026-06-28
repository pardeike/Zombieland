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

namespace ZombieLand
{
	public sealed partial class ZombielandBridgeTools
	{
		[Tool("zombieland/defense_room_state", Description = "Set up or read a reusable defense-room fixture covering shocker, thumper, chainsaw, power, gizmos, breakage, and save-load state.")]
		public static object DefenseRoomState(
			[ToolParameter(Description = "Create a reusable defense-room fixture before reading state.", Required = false, DefaultValue = false)] bool setupFixture = false,
			[ToolParameter(Description = "Try the equipped chainsaw's enabled command-action gizmo after setup/read preparation.", Required = false, DefaultValue = false)] bool activateChainsaw = false,
			[ToolParameter(Description = "Optional action to run before readback: read, zapShocker, repairChainsaw, chainsawBuilding, thumperImpact, wallDoorPressure, infestationThumper, turretFuel, or floatMenu.", Required = false, DefaultValue = "read")] string actionMode = "read",
			[ToolParameter(Description = "Ticks to advance before reading final state; clamped to 0..5000.", Required = false, DefaultValue = 0)] int advanceTicks = 0)
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
				if (TrySetupDefenseRoomFixture(map, out setup, out var setupError) == false)
					return setupError;
			}

			var chainsawActivated = false;
			var chainsawActivateError = (string)null;
			if (activateChainsaw)
				chainsawActivated = TryActivateFirstEquippedChainsaw(map, out chainsawActivateError);

			var normalizedActionMode = (actionMode ?? "read").Trim().ToLowerInvariant();
			if (TryRunDefenseRoomAction(map, normalizedActionMode, out var action, out var actionError) == false)
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

			var shockers = map.listerThings.ThingsOfDef(CustomDefs.ZombieShocker)
				.OfType<ZombieShocker>()
				.OrderBy(ZombieRuntimeActions.StableThingId)
				.Select(DescribeDefenseThing)
				.ToArray();
			var thumpers = map.listerThings.ThingsOfDef(CustomDefs.Thumper)
				.OfType<ZombieThumper>()
				.OrderBy(ZombieRuntimeActions.StableThingId)
				.Select(DescribeDefenseThing)
				.ToArray();
			var spawnedChainsaws = map.listerThings.ThingsOfDef(CustomDefs.Chainsaw)
				.OfType<Chainsaw>()
				.OrderBy(ZombieRuntimeActions.StableThingId)
				.ToArray();
			var equippedChainsaws = map.mapPawns.FreeHumanlikesSpawnedOfFaction(Faction.OfPlayer)
				.Select(pawn => pawn.equipment?.Primary)
				.OfType<Chainsaw>()
				.OrderBy(ZombieRuntimeActions.StableThingId)
				.ToArray();
			var chainsaws = spawnedChainsaws.Concat(equippedChainsaws)
				.Distinct()
				.Select(DescribeDefenseThing)
				.ToArray();
			var batteries = map.listerThings.AllThings
				.Where(thing => thing.TryGetComp<CompPowerBattery>() != null)
				.OrderBy(ZombieRuntimeActions.StableThingId)
				.Select(DescribeDefenseThing)
				.ToArray();

			var placement = EvaluateShockerPlacement(map, shockers.OfType<object>().FirstOrDefault());
			var brokenManager = map.GetComponent<BrokenManager>();
			var brokenChainsaws = spawnedChainsaws.Count(chainsaw => chainsaw.TryGetComp<CompBreakable>()?.broken == true);
			var poweredShocker = map.listerThings.ThingsOfDef(CustomDefs.ZombieShocker)
				.OfType<ZombieShocker>()
				.Any(shocker => shocker.compPowerTrader?.PowerOn == true && shocker.compPowerTrader.PowerNet?.batteryComps?.Count > 0);
			var activeThumper = map.listerThings.ThingsOfDef(CustomDefs.Thumper)
				.OfType<ZombieThumper>()
				.Any(thumper => thumper.IsActive);
			var equippedRunningChainsaw = equippedChainsaws.Any(chainsaw => chainsaw.running);

			return new
			{
				success = (setupFixture == false || (poweredShocker && activeThumper && equippedChainsaws.Length > 0 && brokenChainsaws > 0))
					&& (activateChainsaw == false || chainsawActivated)
					&& actionSucceeded
					&& shockers.Length > 0
					&& thumpers.Length > 0
					&& chainsaws.Length > 0,
				setupFixture,
				setup,
				activateChainsaw,
				chainsawActivated,
				chainsawActivateError,
				actionMode = normalizedActionMode,
				actionSucceeded,
				action,
				advancedTicks = clampedAdvanceTicks,
				ticksGame = Find.TickManager.TicksGame,
				summary = new
				{
					shockerCount = shockers.Length,
					poweredShocker,
					activeThumper,
					thumperCount = thumpers.Length,
					chainsawCount = chainsaws.Length,
					equippedChainsawCount = equippedChainsaws.Length,
					spawnedChainsawCount = spawnedChainsaws.Length,
					equippedRunningChainsaw,
					brokenChainsaws,
					brokenManagerCount = brokenManager?.brokenThings?.Count ?? 0,
					batteryCount = batteries.Length
				},
				placement,
				shockers,
				thumpers,
				chainsaws,
				batteries,
				colonists = map.mapPawns.FreeHumanlikesSpawnedOfFaction(Faction.OfPlayer)
					.OrderBy(pawn => pawn.thingIDNumber)
					.Select(DescribePawn)
					.ToArray(),
				zombies = CurrentZombies(map)
					.OrderBy(ZombieRuntimeActions.StableThingId)
					.Select(DescribeZombie)
					.ToArray()
			};
		}

		static bool TrySetupDefenseRoomFixture(Map map, out object setup, out object error)
		{
			setup = null;
			error = null;
			var destroyedZombies = ZombieRuntimeActions.DestroyZombies(map);
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindShockerFixtureCell(map, root, 24f, out var shockerCell, out error) == false)
				return false;

			var wallDef = ThingDefOf.Wall;
			var conduitDef = DefDatabase<ThingDef>.GetNamed("PowerConduit", false);
			var batteryDef = DefDatabase<ThingDef>.GetNamed("Battery", false);
			if (wallDef == null || conduitDef == null || batteryDef == null)
			{
				error = new
				{
					success = false,
					error = "ThingDef Wall, PowerConduit, or Battery was not found."
				};
				return false;
			}

			var fixtureThings = new List<Thing>();
			for (var dx = -2; dx <= 2; dx++)
			{
				for (var dz = 0; dz <= 4; dz++)
				{
					if (dx != -2 && dx != 2 && dz != 0 && dz != 4)
						continue;

					var wall = ThingMaker.MakeThing(wallDef, ThingDefOf.WoodLog) as Building;
					if (wall == null)
						continue;
					GenSpawn.Spawn(wall, shockerCell + new IntVec3(dx, 0, dz), map, WipeMode.Vanish);
					wall.SetFaction(Faction.OfPlayer);
					fixtureThings.Add(wall);
				}
			}

			var shocker = ThingMaker.MakeThing(CustomDefs.ZombieShocker) as ZombieShocker;
			if (shocker == null)
			{
				error = new
				{
					success = false,
					error = "Could not create ZombieShocker."
				};
				return false;
			}
			shocker.SetFactionDirect(Faction.OfPlayer);
			GenSpawn.Spawn(shocker, shockerCell, map, Rot4.North, WipeMode.Vanish, false);
			fixtureThings.Add(shocker);

			var conduitCell = shockerCell + IntVec3.South;
			var batteryCell = shockerCell + new IntVec3(1, 0, -3);
			var bridgeConduitCell = batteryCell + new IntVec3(0, 0, 2);
			var conduit = GenSpawn.Spawn(ThingMaker.MakeThing(conduitDef), conduitCell, map, WipeMode.Vanish) as Building;
			var bridgeConduit = GenSpawn.Spawn(ThingMaker.MakeThing(conduitDef), bridgeConduitCell, map, WipeMode.Vanish) as Building;
			var battery = GenSpawn.Spawn(ThingMaker.MakeThing(batteryDef), batteryCell, map, WipeMode.Vanish) as Building;
			conduit?.SetFaction(Faction.OfPlayer);
			bridgeConduit?.SetFaction(Faction.OfPlayer);
			battery?.SetFaction(Faction.OfPlayer);
			if (conduit != null)
				fixtureThings.Add(conduit);
			if (bridgeConduit != null)
				fixtureThings.Add(bridgeConduit);
			if (battery != null)
				fixtureThings.Add(battery);

			map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
			var shockerPower = shocker.GetComp<CompPowerTrader>();
			var conduitPower = conduit?.GetComp<CompPower>();
			var bridgeConduitPower = bridgeConduit?.GetComp<CompPower>();
			var batteryPower = battery?.GetComp<CompPowerBattery>();
			batteryPower?.SetStoredEnergyPct(1f);
			if (conduitPower != null)
				map.powerNetManager.Notify_TransmitterSpawned(conduitPower);
			if (bridgeConduitPower != null)
				map.powerNetManager.Notify_TransmitterSpawned(bridgeConduitPower);
			if (batteryPower != null)
				map.powerNetManager.Notify_TransmitterSpawned(batteryPower);
			map.powerNetManager.UpdatePowerNetsAndConnections_First();
			if (shockerPower?.PowerNet == null && conduitPower != null)
				shockerPower.ConnectToTransmitter(conduitPower);
			if (shockerPower?.PowerNet == null && bridgeConduitPower != null)
				shockerPower.ConnectToTransmitter(bridgeConduitPower);
			if (shockerPower?.PowerNet == null && batteryPower != null)
				shockerPower.ConnectToTransmitter(batteryPower);
			AdvanceGameTicks(1);
			if (shockerPower != null)
				shockerPower.PowerOn = true;

			var selectedRotation = shocker.Rotation;
			foreach (var rot in Rot4.AllRotations)
			{
				shocker.Rotation = rot;
				if (shocker.HasValidRoom())
				{
					selectedRotation = rot;
					break;
				}
			}
			shocker.Rotation = selectedRotation;

			var thumperRoot = shockerCell + new IntVec3(10, 0, 0);
			if (TryFindClearBuildingFootprint(map, CustomDefs.Thumper, thumperRoot, 24f, out var thumperCell, out error) == false)
				return false;
			var thumper = ThingMaker.MakeThing(CustomDefs.Thumper) as ZombieThumper;
			if (thumper == null)
			{
				error = new
				{
					success = false,
					error = "Could not create ZombieThumper."
				};
				return false;
			}
			thumper.SetFactionDirect(Faction.OfPlayer);
			GenSpawn.Spawn(thumper, thumperCell, map, Rot4.North, WipeMode.Vanish, false);
			fixtureThings.Add(thumper);
			var thumperRefuelable = thumper.TryGetComp<CompRefuelable>();
			if (thumperRefuelable != null)
			{
				var fuel = ThingMaker.MakeThing(ThingDefOf.Chemfuel);
				fuel.stackCount = Math.Min(ThingDefOf.Chemfuel.stackLimit, thumperRefuelable.GetFuelCountToFullyRefuel());
				GenSpawn.Spawn(fuel, thumperCell + IntVec3.South, map, WipeMode.Vanish);
				thumperRefuelable.Refuel(new List<Thing> { fuel });
			}
			var thumperSwitchable = thumper.TryGetComp<CompSwitchable>();
			if (thumperSwitchable != null)
				thumperSwitchable.isActive = true;
			thumper.intensity = 0.25f;
			thumper.intervalTicks = GenDate.TicksPerHour / 12;

			var actorRoot = thumperCell + new IntVec3(0, 0, 6);
			if (TryFindClearSpawnCell(map, actorRoot, 16f, out var actorCell, out error) == false)
				return false;
			var actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(actor, actorCell, map, WipeMode.Vanish);
			DisablePawnWork(actor);
			actor.equipment?.DestroyAllEquipment(DestroyMode.Vanish);
			actor.drafter.Drafted = true;

			var equippedChainsaw = ThingMaker.MakeThing(CustomDefs.Chainsaw) as Chainsaw;
			if (equippedChainsaw == null)
			{
				error = new
				{
					success = false,
					error = "Could not create equipped Chainsaw."
				};
				return false;
			}
			GenSpawn.Spawn(equippedChainsaw, actorCell, map, WipeMode.Vanish);
			equippedChainsaw.TryGetComp<CompRefuelable>()?.Refuel(new List<Thing> { MakeFuelStack(ThingDefOf.Chemfuel, 20) });
			equippedChainsaw.DeSpawn();
			actor.equipment.AddEquipment(equippedChainsaw);

			var brokenCell = actorCell + IntVec3.East;
			if (brokenCell.InBounds(map) == false || brokenCell.Standable(map) == false)
				brokenCell = actorCell;
			var brokenChainsaw = ThingMaker.MakeThing(CustomDefs.Chainsaw) as Chainsaw;
			if (brokenChainsaw == null)
			{
				error = new
				{
					success = false,
					error = "Could not create broken Chainsaw."
				};
				return false;
			}
			GenSpawn.Spawn(brokenChainsaw, brokenCell, map, WipeMode.Vanish);
			brokenChainsaw.TryGetComp<CompRefuelable>()?.Refuel(new List<Thing> { MakeFuelStack(ThingDefOf.Chemfuel, 10) });
			brokenChainsaw.TryGetComp<CompBreakable>()?.DoBreakdown(map);
			map.areaManager.Home[brokenChainsaw.Position] = true;
			brokenChainsaw.SetForbidden(false, false);

			var componentCell = actorCell + IntVec3.South;
			if (componentCell.InBounds(map) == false || componentCell.Standable(map) == false)
				componentCell = actorCell;
			var component = ThingMaker.MakeThing(ThingDefOf.ComponentIndustrial);
			component.stackCount = 3;
			GenSpawn.Spawn(component, componentCell, map, WipeMode.Vanish);
			component.SetForbidden(false, false);
			fixtureThings.Add(component);

			var zombieCell = shockerCell + new IntVec3(0, 0, 2);
			var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);

			setup = new
			{
				destroyedZombies,
				fixtureThingCount = fixtureThings.Count,
				shocker = ZombieRuntimeActions.StableThingId(shocker),
				thumper = ZombieRuntimeActions.StableThingId(thumper),
				actor = ZombieRuntimeActions.StableThingId(actor),
				equippedChainsaw = ZombieRuntimeActions.StableThingId(equippedChainsaw),
				brokenChainsaw = ZombieRuntimeActions.StableThingId(brokenChainsaw),
				component = ZombieRuntimeActions.StableThingId(component),
				zombie = ZombieRuntimeActions.StableThingId(zombie),
				cells = new
				{
					shocker = ZombieRuntimeActions.DescribeCell(shockerCell),
					conduit = ZombieRuntimeActions.DescribeCell(conduitCell),
					bridgeConduit = ZombieRuntimeActions.DescribeCell(bridgeConduitCell),
					battery = ZombieRuntimeActions.DescribeCell(batteryCell),
					thumper = ZombieRuntimeActions.DescribeCell(thumperCell),
					actor = ZombieRuntimeActions.DescribeCell(actorCell),
					brokenChainsaw = ZombieRuntimeActions.DescribeCell(brokenCell),
					component = ZombieRuntimeActions.DescribeCell(componentCell),
					zombie = ZombieRuntimeActions.DescribeCell(zombieCell)
				}
			};
			return true;
		}

		static Thing MakeFuelStack(ThingDef fuelDef, int count)
		{
			var fuel = ThingMaker.MakeThing(fuelDef);
			fuel.stackCount = Math.Min(fuelDef.stackLimit, count);
			return fuel;
		}

		static bool TryActivateFirstEquippedChainsaw(Map map, out string error)
		{
			error = null;
			var chainsaw = map.mapPawns.FreeHumanlikesSpawnedOfFaction(Faction.OfPlayer)
				.Select(pawn => pawn.equipment?.Primary)
				.OfType<Chainsaw>()
				.FirstOrDefault();
			if (chainsaw == null)
			{
				error = "No equipped chainsaw was found.";
				return false;
			}

			var action = chainsaw.GetGizmos().OfType<Command_Action>().FirstOrDefault(command => command.disabled == false);
			if (action == null)
			{
				error = "No enabled chainsaw Command_Action gizmo was found.";
				return false;
			}

			action.action();
			return chainsaw.running;
		}

		static bool TryRunDefenseRoomAction(Map map, string actionMode, out object result, out string error)
		{
			result = null;
			error = null;
			switch (actionMode)
			{
				case "read":
					return true;
				case "zapshocker":
					result = RunSavedRoomShockerZap(map);
					return true;
				case "repairchainsaw":
					result = RunSavedRoomChainsawRepair(map);
					return true;
				case "chainsawbuilding":
					result = RunSavedRoomChainsawBuilding(map);
					return true;
				case "thumperimpact":
					result = RunSavedRoomThumperImpact(map);
					return true;
				case "walldoorpressure":
					result = RunSavedRoomWallDoorPressure(map);
					return true;
				case "infestationthumper":
					result = RunSavedRoomInfestationThumper(map);
					return true;
				case "turretfuel":
					result = RunSavedRoomTurretFuel(map);
					return true;
				case "floatmenu":
					result = RunSavedRoomFloatMenu(map);
					return true;
				default:
					error = "actionMode must be one of: read, zapShocker, repairChainsaw, chainsawBuilding, thumperImpact, wallDoorPressure, infestationThumper, turretFuel, floatMenu.";
					return false;
			}
		}

		static object RunSavedRoomFloatMenu(Map map)
		{
			var patchTargets = PatchedMethodsForPatchClass("FloatMenuMakerMap_GetOptions_Patch");
			var zapLabel = "ZapZombies".Translate().ToString();
			var ropeLabel = "RopeZombie".Translate().ToString();
			var shocker = map.listerThings.ThingsOfDef(CustomDefs.ZombieShocker)
				.OfType<ZombieShocker>()
				.FirstOrDefault(candidate => candidate.compPowerTrader?.PowerOn == true && candidate.HasValidRoom());
			if (shocker == null)
			{
				return new
				{
					success = false,
					patchTargets,
					error = "No powered valid-room ZombieShocker exists in the current map."
				};
			}

			var actor = map.mapPawns.FreeHumanlikesSpawnedOfFaction(Faction.OfPlayer)
				.OrderBy(pawn => pawn.Position.DistanceToSquared(shocker.Position))
				.FirstOrDefault(pawn => pawn.CanReach(shocker, PathEndMode.ClosestTouch, Danger.Deadly, false, false, TraverseMode.ByPawn) && pawn.CanReserve(shocker));
			if (actor == null)
			{
				if (TryFindClearSpawnCell(map, shocker.Position + IntVec3.South, 8f, out var actorCell, out var actorError) == false)
					return actorError;
				actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(actor, actorCell, map, WipeMode.Vanish);
				DisablePawnWork(actor);
			}
			actor.drafter.Drafted = false;

			Zombie confusedZombie = null;
			Zombie normalZombie = null;
			try
			{
				var shockerClick = shocker.Position.ToVector3Shifted();
				var shockerOptions = FloatMenuMakerMap.GetOptions(new List<Pawn> { actor }, shockerClick, out var shockerContext);
				var shockerCustomOptions = DescribeCustomFloatMenuOptions(shockerOptions, zapLabel, ropeLabel);
				var shockerCanReach = actor.CanReach(shocker, PathEndMode.ClosestTouch, Danger.Deadly, false, false, TraverseMode.ByPawn);
				var shockerCanReserve = actor.CanReserve(shocker);
				var shockerPowerOn = shocker.compPowerTrader?.PowerOn == true;
				var shockerHasValidRoom = shocker.HasValidRoom();

				if (TryFindClearSpawnCell(map, actor.Position + new IntVec3(4, 0, 0), 12f, out var confusedCell, out var confusedError) == false)
					return confusedError;
				confusedZombie = ZombieRuntimeActions.SpawnZombie(confusedCell, map, ZombieType.Normal, true);
				if (confusedZombie == null)
				{
					return new
					{
						success = false,
						patchTargets,
						error = "Could not spawn the confused rope-menu zombie."
					};
				}
				confusedZombie.paralyzedUntil = GenTicks.TicksGame + 10000;
				confusedZombie.ropedBy = null;

				if (TryFindClearSpawnCell(map, actor.Position + new IntVec3(6, 0, 0), 12f, out var normalCell, out var normalError) == false)
					return normalError;
				normalZombie = ZombieRuntimeActions.SpawnZombie(normalCell, map, ZombieType.Normal, true);
				if (normalZombie == null)
				{
					return new
					{
						success = false,
						patchTargets,
						error = "Could not spawn the ordinary rope-menu control zombie."
					};
				}
				normalZombie.paralyzedUntil = 0;
				normalZombie.ropedBy = null;

				var ropeOptions = FloatMenuMakerMap.GetOptions(new List<Pawn> { actor }, confusedZombie.DrawPos, out var ropeContext);
				var ordinaryZombieOptions = FloatMenuMakerMap.GetOptions(new List<Pawn> { actor }, normalZombie.DrawPos, out var ordinaryZombieContext);
				var emptySelectionOptions = FloatMenuMakerMap.GetOptions(new List<Pawn>(), shockerClick, out var emptySelectionContext);
				var ropeCustomOptions = DescribeCustomFloatMenuOptions(ropeOptions, zapLabel, ropeLabel);
				var ordinaryZombieCustomOptions = DescribeCustomFloatMenuOptions(ordinaryZombieOptions, zapLabel, ropeLabel);
				var emptySelectionCustomOptions = DescribeCustomFloatMenuOptions(emptySelectionOptions, zapLabel, ropeLabel);

				var zapPresent = shockerCustomOptions.Count(option => option.Label == zapLabel) == 1;
				var ropePresent = ropeCustomOptions.Count(option => option.Label == ropeLabel) == 1;
				var ordinaryRopeAbsent = ordinaryZombieCustomOptions.Any(option => option.Label == ropeLabel) == false;
				var emptySelectionAbsent = emptySelectionCustomOptions.Length == 0;

				return new
				{
					success = patchTargets.Length > 0
						&& zapPresent
						&& ropePresent
						&& ordinaryRopeAbsent
						&& emptySelectionAbsent
						&& shockerCanReach
						&& shockerCanReserve
						&& shockerPowerOn
						&& shockerHasValidRoom
						&& confusedZombie.IsConfused
						&& normalZombie.IsConfused == false,
					action = "floatMenu",
					patchTargets,
					labels = new
					{
						zapLabel,
						ropeLabel
					},
					actor = DescribePawn(actor),
					shocker = new
					{
						thing = DescribeDefenseThing(shocker),
						click = ZombieRuntimeActions.DescribeCell(shocker.Position),
						canReach = shockerCanReach,
						canReserve = shockerCanReserve,
						powerOn = shockerPowerOn,
						hasValidRoom = shockerHasValidRoom,
						contextCreated = shockerContext != null,
						customOptions = shockerCustomOptions
					},
					rope = new
					{
						confusedZombie = DescribeZombie(confusedZombie),
						contextCreated = ropeContext != null,
						customOptions = ropeCustomOptions
					},
					ordinaryZombieControl = new
					{
						normalZombie = DescribeZombie(normalZombie),
						contextCreated = ordinaryZombieContext != null,
						customOptions = ordinaryZombieCustomOptions
					},
					emptySelectionControl = new
					{
						contextCreated = emptySelectionContext != null,
						customOptions = emptySelectionCustomOptions
					}
				};
			}
			finally
			{
				if (confusedZombie != null && confusedZombie.Destroyed == false)
					confusedZombie.Destroy(DestroyMode.Vanish);
				if (normalZombie != null && normalZombie.Destroyed == false)
					normalZombie.Destroy(DestroyMode.Vanish);
			}
		}

		sealed class FloatMenuOptionSummary
		{
			public string Label { get; set; }
			public bool Disabled { get; set; }
		}

		static FloatMenuOptionSummary[] DescribeCustomFloatMenuOptions(List<FloatMenuOption> options, string zapLabel, string ropeLabel)
		{
			return options
				.Where(option => option.Label == zapLabel || option.Label == ropeLabel)
				.Select(option => new FloatMenuOptionSummary
				{
					Label = option.Label,
					Disabled = option.Disabled
				})
				.ToArray();
		}

		static object RunSavedRoomTurretFuel(Map map)
		{
			var patchTargets = PatchedMethodsForPatchClass("CompRefuelable_ConsumeFuel_Patch");
			var settingsSnapshot = SnapshotZombieSettings();
			var fuelField = AccessTools.Field(typeof(CompRefuelable), "fuel");
			if (fuelField == null)
			{
				return new
				{
					success = false,
					patchTargets,
					error = "Could not reflect CompRefuelable.fuel."
				};
			}

			var root = new IntVec3(map.Size.x / 2 + 18, 0, map.Size.z / 2 - 18);
			var turretDef = DefDatabase<ThingDef>.GetNamed("Turret_MiniTurret", false)
				?? DefDatabase<ThingDef>.AllDefs.FirstOrDefault(def => def.thingClass != null && typeof(Building_Turret).IsAssignableFrom(def.thingClass) && def.GetCompProperties<CompProperties_Refuelable>() != null);
			if (turretDef == null)
			{
				return new
				{
					success = false,
					patchTargets,
					error = "No refuelable turret def was available."
				};
			}
			if (TryFindClearBuildingFootprint(map, turretDef, root, 24f, out var turretCell, out var turretCellError) == false)
				return turretCellError;
			if (TryFindClearBuildingFootprint(map, CustomDefs.Thumper, root + new IntVec3(8, 0, 0), 24f, out var thumperCell, out var thumperCellError) == false)
				return thumperCellError;

			Thing turret = null;
			Thing thumper = null;
			try
			{
				var stuff = turretDef.MadeFromStuff ? GenStuff.DefaultStuffFor(turretDef) ?? ThingDefOf.Steel : null;
				turret = ThingMaker.MakeThing(turretDef, stuff);
				turret.SetFactionDirect(Faction.OfPlayer);
				GenSpawn.Spawn(turret, turretCell, map, Rot4.North, WipeMode.Vanish, false);

				thumper = ThingMaker.MakeThing(CustomDefs.Thumper);
				thumper.SetFactionDirect(Faction.OfPlayer);
				GenSpawn.Spawn(thumper, thumperCell, map, Rot4.North, WipeMode.Vanish, false);

				var turretRefuelable = turret.TryGetComp<CompRefuelable>();
				var controlRefuelable = thumper.TryGetComp<CompRefuelable>();
				if (turretRefuelable == null || controlRefuelable == null)
				{
					return new
					{
						success = false,
						patchTargets,
						turret = DescribeDefenseThing(turret),
						control = DescribeDefenseThing(thumper),
						error = "Turret or non-turret control had no refuelable comp."
					};
				}

				ApplyZombieSettingsOverride(settings => settings.reducedTurretConsumption = 0.5f);
				var normalAmount = 4f;
				var startFuel = 20f;
				SetRefuelableFuel(fuelField, turretRefuelable, startFuel);
				SetRefuelableFuel(fuelField, controlRefuelable, startFuel);
				var turretBefore = turretRefuelable.Fuel;
				var controlBefore = controlRefuelable.Fuel;
				turretRefuelable.ConsumeFuel(normalAmount);
				controlRefuelable.ConsumeFuel(normalAmount);
				var turretAfter = turretRefuelable.Fuel;
				var controlAfter = controlRefuelable.Fuel;
				var turretDelta = turretBefore - turretAfter;
				var controlDelta = controlBefore - controlAfter;
				var expectedTurretDelta = normalAmount * (1f - ZombieSettings.Values.reducedTurretConsumption);
				var expectedControlDelta = normalAmount;

				SetRefuelableFuel(fuelField, turretRefuelable, 1f);
				var lowBefore = turretRefuelable.Fuel;
				turretRefuelable.ConsumeFuel(normalAmount);
				var lowAfter = turretRefuelable.Fuel;

				return new
				{
					success = patchTargets.Length > 0
						&& Approximately(turretDelta, expectedTurretDelta, 0.0001f)
						&& Approximately(controlDelta, expectedControlDelta, 0.0001f)
						&& Approximately(lowBefore, 1f, 0.0001f)
						&& Approximately(lowAfter, 0f, 0.0001f)
						&& lowAfter >= 0f,
					patchTargets,
					setting = ZombieSettings.Values.reducedTurretConsumption,
					inputAmount = normalAmount,
					turret = new
					{
						thing = DescribeDefenseThing(turret),
						fuelBefore = turretBefore,
						fuelAfter = turretAfter,
						fuelDelta = turretDelta,
						expectedFuelDelta = expectedTurretDelta
					},
					nonTurretControl = new
					{
						thing = DescribeDefenseThing(thumper),
						fuelBefore = controlBefore,
						fuelAfter = controlAfter,
						fuelDelta = controlDelta,
						expectedFuelDelta = expectedControlDelta
					},
					lowFuelClamp = new
					{
						fuelBefore = lowBefore,
						fuelAfter = lowAfter,
						nonNegative = lowAfter >= 0f
					}
				};
			}
			finally
			{
				RestoreZombieSettings(settingsSnapshot);
				if (turret != null && turret.Destroyed == false)
					turret.Destroy(DestroyMode.Vanish);
				if (thumper != null && thumper.Destroyed == false)
					thumper.Destroy(DestroyMode.Vanish);
			}
		}

		static void SetRefuelableFuel(FieldInfo fuelField, CompRefuelable refuelable, float fuel)
		{
			fuelField.SetValue(refuelable, Mathf.Clamp(fuel, 0f, refuelable.Props.fuelCapacity));
		}

		static object RunSavedRoomShockerZap(Map map)
		{
			var shocker = map.listerThings.ThingsOfDef(CustomDefs.ZombieShocker).OfType<ZombieShocker>().FirstOrDefault();
			if (shocker == null)
			{
				return new
				{
					success = false,
					error = "No ZombieShocker exists in the current map."
				};
			}

			var actor = map.mapPawns.FreeHumanlikesSpawnedOfFaction(Faction.OfPlayer)
				.FirstOrDefault(pawn => pawn.CanReach(shocker, PathEndMode.InteractionCell, Danger.Deadly) && pawn.CanReserve(shocker));
			if (actor == null)
			{
				var actorRoot = shocker.Position + IntVec3.South + IntVec3.West;
				if (TryFindClearSpawnCell(map, actorRoot, 8f, out var actorCell, out var actorError) == false)
					return actorError;
				actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(actor, actorCell, map, WipeMode.Vanish);
				DisablePawnWork(actor);
			}
			actor.drafter.Drafted = false;

			var targetCell = shocker.Position + IntVec3.North.RotatedBy(shocker.Rotation) + IntVec3.North.RotatedBy(shocker.Rotation);
			var zombie = CurrentZombies(map)
				.OfType<Zombie>()
				.Where(candidate => candidate.Spawned && candidate.Dead == false)
				.OrderBy(candidate => candidate.Position.DistanceToSquared(targetCell))
				.FirstOrDefault();
			if (zombie == null)
				zombie = ZombieRuntimeActions.SpawnZombie(targetCell, map, ZombieType.Normal, true);
			if (zombie == null)
			{
				return new
				{
					success = false,
					error = "No zombie could be found or spawned for shocker zap."
				};
			}
			zombie.ropedBy = null;
			zombie.paralyzedUntil = 0;

			var shockerPower = shocker.GetComp<CompPowerTrader>();
			var batteryCount = shockerPower?.PowerNet?.batteryComps?.Count ?? 0;
			var room = ZombieShocker.GetValidRoom(map, shocker.Position + IntVec3.North.RotatedBy(shocker.Rotation));
			var roomCellCount = room?.Cells.Count(cell => cell.Standable(map)) ?? 0;
			var zapMotesBefore = CountZombieZapMotesNear(map, zombie.Position, 3f);
			var paralyzedUntilBefore = zombie.paralyzedUntil;
			var started = false;
			if (shockerPower?.PowerOn == true && batteryCount > 0 && shocker.HasValidRoom())
			{
				var job = JobMaker.MakeJob(CustomDefs.ZapZombies, shocker);
				job.playerForced = true;
				started = actor.jobs.TryTakeOrderedJob(job, new JobTag?(JobTag.Misc), false);
			}

			var maxTicks = 420 + roomCellCount;
			var tickHit = -1;
			var samples = new List<object>();
			for (var tick = 1; tick <= maxTicks; tick++)
			{
				AdvanceGameTicks(1);
				var paralyzedNow = zombie.paralyzedUntil > GenTicks.TicksAbs;
				var zapMotesNow = CountZombieZapMotesNear(map, zombie.Position, 3f);
				if (tick == 1 || tick == 90 || tick % 30 == 0 || paralyzedNow)
				{
					samples.Add(new
					{
						tick,
						actorJob = actor.CurJobDef?.defName,
						paralyzed = paralyzedNow,
						zombie.paralyzedUntil,
						zapMotes = zapMotesNow
					});
				}
				if (paralyzedNow)
				{
					tickHit = tick;
					break;
				}
			}

			var zapMotesAfter = CountZombieZapMotesNear(map, zombie.Position, 3f);
			var paralyzedAfter = zombie.paralyzedUntil > GenTicks.TicksAbs;
			return new
			{
				success = started
					&& shockerPower?.PowerOn == true
					&& batteryCount > 0
					&& shocker.HasValidRoom()
					&& paralyzedAfter
					&& tickHit > 0,
				action = "zapShocker",
				shocker = ZombieRuntimeActions.StableThingId(shocker),
				actor = DescribePawn(actor),
				zombie = DescribeZombie(zombie),
				batteryCount,
				roomCellCount,
				started,
				maxTicks,
				tickHit,
				paralyzedUntilBefore,
				paralyzedUntilAfter = zombie.paralyzedUntil,
				zapMotesBefore,
				zapMotesAfter,
				samples
			};
		}

		static object RunSavedRoomChainsawRepair(Map map)
		{
			var chainsaw = map.listerThings.ThingsOfDef(CustomDefs.Chainsaw)
				.OfType<Chainsaw>()
				.FirstOrDefault(candidate => candidate.TryGetComp<CompBreakable>()?.broken == true);
			if (chainsaw == null)
			{
				return new
				{
					success = false,
					error = "No spawned broken chainsaw exists in the current map."
				};
			}

			var actor = map.mapPawns.FreeHumanlikesSpawnedOfFaction(Faction.OfPlayer)
				.OrderBy(pawn => pawn.Position.DistanceToSquared(chainsaw.Position))
				.FirstOrDefault();
			if (actor == null)
			{
				if (TryFindClearSpawnCell(map, chainsaw.Position, 8f, out var actorCell, out var actorError) == false)
					return actorError;
				actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(actor, actorCell, map, WipeMode.Vanish);
			}
			DisablePawnWork(actor);
			actor.drafter.Drafted = false;
			actor.skills?.GetSkill(SkillDefOf.Construction).Notify_SkillDisablesChanged();
			actor.skills.GetSkill(SkillDefOf.Construction).Level = 20;
			map.areaManager.Home[chainsaw.Position] = true;
			chainsaw.SetForbidden(false, false);

			var component = map.listerThings.ThingsOfDef(ThingDefOf.ComponentIndustrial)
				.OrderBy(thing => thing.Position.DistanceToSquared(actor.Position))
				.FirstOrDefault();
			if (component == null)
			{
				var componentCell = actor.Position + IntVec3.South;
				if (componentCell.InBounds(map) == false || componentCell.Standable(map) == false)
					componentCell = actor.Position;
				component = ThingMaker.MakeThing(ThingDefOf.ComponentIndustrial);
				component.stackCount = 1;
				GenSpawn.Spawn(component, componentCell, map, WipeMode.Vanish);
			}
			component.SetForbidden(false, false);

			var breakable = chainsaw.TryGetComp<CompBreakable>();
			var manager = map.GetComponent<BrokenManager>();
			var componentStackBefore = component.stackCount;
			var workGiver = new WorkGiver_FixBrokenChainsaw();
			var hasJob = workGiver.HasJobOnThing(actor, chainsaw, true);
			var job = hasJob ? workGiver.JobOnThing(actor, chainsaw, true) : null;
			if (job != null)
				job.playerForced = true;
			var started = job != null && actor.jobs.TryTakeOrderedJob(job, new JobTag?(JobTag.Misc), false);
			var tickHit = -1;
			var samples = new List<object>();
			Rand.PushState(3);
			try
			{
				for (var tick = 1; tick <= 1250; tick++)
				{
					AdvanceGameTicks(1);
					var brokenNow = breakable?.broken ?? false;
					if (tick == 1 || tick % 200 == 0 || brokenNow == false)
					{
						samples.Add(new
						{
							tick,
							actorJob = actor.CurJobDef?.defName,
							broken = brokenNow,
							componentDestroyed = component.Destroyed,
							managerBrokenCount = manager?.brokenThings?.Count ?? 0
						});
					}
					if (brokenNow == false)
					{
						tickHit = tick;
						break;
					}
				}
			}
			finally
			{
				Rand.PopState();
			}

			var trackedAfter = manager?.brokenThings?.Contains(chainsaw) ?? false;
			return new
			{
				success = hasJob
					&& started
					&& tickHit > 0
					&& breakable?.broken == false
					&& trackedAfter == false
					&& (component.Destroyed || component.stackCount < componentStackBefore),
				action = "repairChainsaw",
				actor = DescribePawn(actor),
				chainsaw = ZombieRuntimeActions.StableThingId(chainsaw),
				component = ZombieRuntimeActions.StableThingId(component),
				hasJob,
				jobDef = job?.def?.defName,
				started,
				tickHit,
				trackedAfter,
				componentStackBefore,
				componentStackAfter = component.Destroyed ? 0 : component.stackCount,
				componentDestroyed = component.Destroyed,
				samples
			};
		}

		static object RunSavedRoomChainsawBuilding(Map map)
		{
			var actor = map.mapPawns.FreeHumanlikesSpawnedOfFaction(Faction.OfPlayer)
				.FirstOrDefault(pawn => pawn.equipment?.Primary is Chainsaw);
			var chainsaw = actor?.equipment?.Primary as Chainsaw;
			if (actor == null || chainsaw == null)
			{
				return new
				{
					success = false,
					error = "No colonist with equipped chainsaw exists in the current map."
				};
			}

			actor.drafter.Drafted = true;
			if (chainsaw.running == false)
				TryActivateFirstEquippedChainsaw(map, out _);

			var adjacent = GenAdj.AdjacentCellsAround;
			var buildingCell = IntVec3.Invalid;
			var buildingIndex = -1;
			var zombieCell = IntVec3.Invalid;
			for (var i = 0; i < adjacent.Length; i++)
			{
				var candidate = actor.Position + adjacent[i];
				if (candidate.InBounds(map) == false || candidate.Fogged(map))
					continue;
				if (candidate.GetEdifice(map) != null || candidate.GetFirstThing<Mineable>(map) != null)
					continue;
				if (candidate.GetThingList(map).Any(thing => thing is Pawn))
					continue;
				if (buildingCell.IsValid == false)
				{
					buildingCell = candidate;
					buildingIndex = i;
				}
				else if (candidate.Standable(map))
				{
					zombieCell = candidate;
					break;
				}
			}
			if (buildingCell.IsValid == false || zombieCell.IsValid == false)
			{
				return new
				{
					success = false,
					actor = DescribePawn(actor),
					buildingCell = buildingCell.IsValid ? ZombieRuntimeActions.DescribeCell(buildingCell) : null,
					zombieCell = zombieCell.IsValid ? ZombieRuntimeActions.DescribeCell(zombieCell) : null,
					error = "No adjacent clear building/zombie pair was available."
				};
			}

			var wall = ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.WoodLog) as Building;
			if (wall == null)
			{
				return new
				{
					success = false,
					error = "Could not create test wall."
				};
			}
			GenSpawn.Spawn(wall, buildingCell, map, WipeMode.Vanish);
			wall.SetFaction(Faction.OfPlayer);
			var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
			if (zombie == null)
			{
				return new
				{
					success = false,
					zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
					error = "ZombieGenerator.SpawnZombie returned no chainsaw target zombie."
				};
			}
			var breakable = chainsaw.TryGetComp<CompBreakable>();
			var refuelable = chainsaw.TryGetComp<CompRefuelable>();
			var wallHitPointsBefore = wall.HitPoints;
			var chainsawHitPointsBefore = chainsaw.HitPoints;
			var fuelBefore = refuelable?.Fuel ?? 0f;
			chainsaw.angle = buildingIndex * 45f + 22.5f;
			AdvanceGameTicks(1);

			var wallHitPointsAfter = wall.Destroyed ? 0 : wall.HitPoints;
			var chainsawHitPointsAfter = chainsaw.Destroyed ? 0 : chainsaw.HitPoints;
			var trackedAsBroken = map.GetComponent<BrokenManager>()?.brokenThings?.Contains(chainsaw) ?? false;
			return new
			{
				success = wallHitPointsAfter < wallHitPointsBefore
					&& ReferenceEquals(actor.equipment?.Primary, chainsaw) == false
					&& chainsaw.Spawned
					&& breakable?.broken == true
					&& trackedAsBroken
					&& chainsawHitPointsAfter < chainsawHitPointsBefore
					&& zombie.Dead == false,
				action = "chainsawBuilding",
				actor = DescribePawn(actor),
				zombie = DescribeZombie(zombie),
				chainsaw = ZombieRuntimeActions.StableThingId(chainsaw),
				chainsawSpawned = chainsaw.Spawned,
				chainsawPosition = chainsaw.Spawned ? ZombieRuntimeActions.DescribeCell(chainsaw.Position) : null,
				chainsawBroken = breakable?.broken ?? false,
				trackedAsBroken,
				chainsawHitPointsBefore,
				chainsawHitPointsAfter,
				fuelBefore,
				fuelAfter = refuelable?.Fuel ?? 0f,
				wall = new
				{
					id = ZombieRuntimeActions.StableThingId(wall),
					cell = ZombieRuntimeActions.DescribeCell(buildingCell),
					wall.Destroyed,
					hitPointsBefore = wallHitPointsBefore,
					hitPointsAfter = wallHitPointsAfter,
					hitPointDelta = wallHitPointsBefore - wallHitPointsAfter
				}
			};
		}

		static object RunSavedRoomThumperImpact(Map map)
		{
			var thumper = map.listerThings.ThingsOfDef(CustomDefs.Thumper).OfType<ZombieThumper>().FirstOrDefault();
			if (thumper == null)
			{
				return new
				{
					success = false,
					error = "No ZombieThumper exists in the current map."
				};
			}

			var refuelable = thumper.TryGetComp<CompRefuelable>();
			if (refuelable == null)
			{
				return new
				{
					success = false,
					error = "The saved thumper has no refuelable comp."
				};
			}
			var switchable = thumper.TryGetComp<CompSwitchable>();
			if (switchable != null)
				switchable.isActive = true;
			if (refuelable.HasFuel == false)
				refuelable.Refuel(new List<Thing> { MakeFuelStack(ThingDefOf.Chemfuel, refuelable.GetFuelCountToFullyRefuel()) });

			thumper.intensity = 0.5f;
			thumper.intervalTicks = 130;
			var radius = thumper.Radius;
			var targetDistances = new[] { 3, 12, 22 };
			var walls = new List<Building>();
			foreach (var distance in targetDistances)
			{
				var cell = thumper.Position + new IntVec3(distance, 0, 0);
				if (cell.InBounds(map) == false || cell.Fogged(map) || cell.GetEdifice(map) != null || cell.GetThingList(map).Any(thing => thing is Pawn))
				{
					return new
					{
						success = false,
						cell = ZombieRuntimeActions.DescribeCell(cell),
						error = "A thumper target wall cell was not clear."
					};
				}
				var wall = SpawnWoodWall(map, cell);
				if (wall == null)
				{
					return new
					{
						success = false,
						cell = ZombieRuntimeActions.DescribeCell(cell),
						error = "Could not create thumper target wall."
					};
				}
				walls.Add(wall);
			}

			var stateField = typeof(ZombieThumper).GetField("state", BindingFlags.Instance | BindingFlags.NonPublic);
			var lastImpactTicksField = typeof(ZombieThumper).GetField("lastImpactTicks", BindingFlags.Instance | BindingFlags.NonPublic);
			var fuelBefore = refuelable.Fuel;
			var lastImpactBefore = (int)(lastImpactTicksField?.GetValue(thumper) ?? 0);
			var beforeHitPoints = walls.Select(wall => wall.HitPoints).ToArray();
			var impactTick = -1;
			var samples = new List<object>();
			for (var tick = 1; tick <= 500; tick++)
			{
				AdvanceGameTicks(1);
				var state = stateField?.GetValue(thumper)?.ToString();
				if (tick == 1 || tick % 60 == 0 || state == "Impacting")
				{
					samples.Add(new
					{
						tick,
						state,
						fuel = refuelable.Fuel,
						lastImpactTicks = (int)(lastImpactTicksField?.GetValue(thumper) ?? 0)
					});
				}
				if ((int)(lastImpactTicksField?.GetValue(thumper) ?? 0) > lastImpactBefore)
				{
					impactTick = tick;
					break;
				}
			}
			for (var i = 0; i < 330; i++)
				AdvanceGameTicks(1);

			var afterHitPoints = walls.Select(wall => wall.Destroyed ? 0 : wall.HitPoints).ToArray();
			var deltas = beforeHitPoints.Zip(afterHitPoints, (before, after) => before - after).ToArray();
			return new
			{
				success = impactTick > 0
					&& refuelable.Fuel < fuelBefore
					&& deltas[0] > 0
					&& deltas[1] > 0
					&& deltas[0] >= deltas[1]
					&& deltas[1] > deltas[2],
				action = "thumperImpact",
				thumper = ZombieRuntimeActions.StableThingId(thumper),
				radius,
				impactTick,
				fuelBefore,
				fuelAfter = refuelable.Fuel,
				lastImpactBefore,
				lastImpactAfter = (int)(lastImpactTicksField?.GetValue(thumper) ?? 0),
				targets = walls.Select((wall, index) => new
				{
					id = ZombieRuntimeActions.StableThingId(wall),
					cell = ZombieRuntimeActions.DescribeCell(wall.Position),
					distance = targetDistances[index],
					hitPointsBefore = beforeHitPoints[index],
					hitPointsAfter = afterHitPoints[index],
					hitPointDelta = deltas[index]
				}).ToArray(),
				samples
			};
		}

		static object RunSavedRoomInfestationThumper(Map map)
		{
			var thumper = map.listerThings.ThingsOfDef(CustomDefs.Thumper).OfType<ZombieThumper>().FirstOrDefault();
			if (thumper == null)
			{
				return new
				{
					success = false,
					error = "No ZombieThumper exists in the current map."
				};
			}

			var switchable = thumper.TryGetComp<CompSwitchable>();
			var oldActive = switchable?.isActive ?? false;
			var oldFaction = thumper.Faction;
			var candidates = GenRadial.RadialCellsAround(thumper.Position, Math.Min(10f, thumper.Radius), false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell != thumper.Position)
				.Where(cell => cell.Walkable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetEdifice(map) == null)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.OrderBy(cell => cell.DistanceToSquared(thumper.Position))
				.ToArray();
			var roofDef = RoofDefOf.RoofRockThick;
			if (roofDef == null)
			{
				return new
				{
					success = false,
					error = "RoofRockThick is unavailable."
				};
			}

			var roofCells = new List<IntVec3>();
			var oldRoofs = new Dictionary<IntVec3, RoofDef>();
			try
			{
				if (thumper.Faction != Faction.OfPlayer)
					thumper.SetFactionDirect(Faction.OfPlayer);
				if (switchable != null)
					switchable.isActive = false;

				var calculate = typeof(InfestationCellFinder).GetMethod("CalculateLocationCandidates", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
				var getScore = typeof(InfestationCellFinder).GetMethod("GetScoreAt", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
				var candidateListField = typeof(InfestationCellFinder).GetField("locationCandidates", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
				if (calculate == null || getScore == null || candidateListField == null)
				{
					return new
					{
						success = false,
						error = "Could not reflect InfestationCellFinder candidate methods."
					};
				}

				var tested = new List<object>();
				foreach (var candidate in candidates)
				{
					RestoreRoofs(map, oldRoofs, roofCells);
					roofCells.Clear();
					oldRoofs.Clear();
					foreach (var roofCell in GenRadial.RadialCellsAround(candidate, 16f, true))
					{
						if (roofCell.InBounds(map) == false)
							continue;
						if (roofCell.GetEdifice(map) != null)
							continue;
						oldRoofs[roofCell] = map.roofGrid.RoofAt(roofCell);
						map.roofGrid.SetRoof(roofCell, roofDef);
						roofCells.Add(roofCell);
					}
					map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
					calculate.Invoke(null, new object[] { map });
					var inactiveScore = (float)getScore.Invoke(null, new object[] { candidate, map });
					var inactiveCandidateCount = CandidateCount(candidateListField);
					var inactiveInsideRadiusCount = CandidateCountInsideRadius(candidateListField, thumper.Position, thumper.Radius + 0.5f);

					if (switchable != null)
						switchable.isActive = true;
					calculate.Invoke(null, new object[] { map });
					var activeScore = (float)getScore.Invoke(null, new object[] { candidate, map });
					var activeCandidateCount = CandidateCount(candidateListField);
					var activeInsideRadiusCount = CandidateCountInsideRadius(candidateListField, thumper.Position, thumper.Radius + 0.5f);
					if (switchable != null)
						switchable.isActive = false;

					var sample = new
					{
						cell = ZombieRuntimeActions.DescribeCell(candidate),
						distance = candidate.DistanceTo(thumper.Position),
						roofedCells = roofCells.Count,
						inactiveScore,
						activeScore,
						inactiveCandidateCount,
						activeCandidateCount,
						inactiveInsideRadiusCount,
						activeInsideRadiusCount
					};
					tested.Add(sample);
					if (inactiveScore > 0f && activeScore == 0f && activeInsideRadiusCount < inactiveInsideRadiusCount)
					{
						return new
						{
							success = true,
							action = "infestationThumper",
							thumper = ZombieRuntimeActions.StableThingId(thumper),
							thumperCell = ZombieRuntimeActions.DescribeCell(thumper.Position),
							thumperRadius = thumper.Radius,
							candidate = sample,
							tested = tested.ToArray()
						};
					}
				}

				return new
				{
					success = false,
					action = "infestationThumper",
					thumper = ZombieRuntimeActions.StableThingId(thumper),
					thumperCell = ZombieRuntimeActions.DescribeCell(thumper.Position),
					thumperRadius = thumper.Radius,
					error = "No temporary thick-roofed infestation candidate produced a positive inactive score that the active thumper suppressed.",
					tested = tested.ToArray()
				};
			}
			finally
			{
				RestoreRoofs(map, oldRoofs, roofCells);
				if (switchable != null)
					switchable.isActive = oldActive;
				if (oldFaction != thumper.Faction)
					thumper.SetFactionDirect(oldFaction);
				map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
			}
		}

		static void RestoreRoofs(Map map, Dictionary<IntVec3, RoofDef> oldRoofs, List<IntVec3> roofCells)
		{
			foreach (var cell in roofCells)
			{
				if (cell.InBounds(map) == false)
					continue;
				oldRoofs.TryGetValue(cell, out var oldRoof);
				map.roofGrid.SetRoof(cell, oldRoof);
			}
		}

		static int CandidateCount(FieldInfo candidateListField)
			=> (candidateListField.GetValue(null) as System.Collections.ICollection)?.Count ?? -1;

		static int CandidateCountInsideRadius(FieldInfo candidateListField, IntVec3 root, float radius)
		{
			var list = candidateListField.GetValue(null) as System.Collections.IEnumerable;
			if (list == null)
				return -1;

			var count = 0;
			foreach (var item in list)
			{
				var cellField = item.GetType().GetField("cell", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
				if (cellField == null)
					continue;
				var cell = (IntVec3)cellField.GetValue(item);
				if (cell.DistanceTo(root) <= radius)
					count++;
			}
			return count;
		}

		static object RunSavedRoomWallDoorPressure(Map map)
		{
			var shocker = map.listerThings.ThingsOfDef(CustomDefs.ZombieShocker).OfType<ZombieShocker>().FirstOrDefault();
			var thumper = map.listerThings.ThingsOfDef(CustomDefs.Thumper).OfType<ZombieThumper>().FirstOrDefault();
			var root = thumper?.Position ?? shocker?.Position ?? new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			var wallPush = RunSavedRoomWallPushPressure(map, root + new IntVec3(-8, 0, 8));
			var doorPressure = RunSavedRoomDoorPressure(map, root + new IntVec3(8, 0, 8));
			var wallSuccess = (bool)(wallPush?.GetType().GetProperty("success")?.GetValue(wallPush) ?? false);
			var doorSuccess = (bool)(doorPressure?.GetType().GetProperty("success")?.GetValue(doorPressure) ?? false);
			return new
			{
				success = wallSuccess && doorSuccess,
				action = "wallDoorPressure",
				wallPush,
				doorPressure
			};
		}

		static object RunSavedRoomWallPushPressure(Map map, IntVec3 root)
		{
			if (TryCreateWallPushFixture(map, root, 20f, out var fixture, out var error) == false)
				return error;

			var zombie = fixture.zombie;
			var wall = fixture.wall;
			var grid = map.GetGrid();
			var effectiveMinimum = Math.Max(1, ZombieSettings.Values.minimumZombiesForWallPushing);
			var primedGridCount = Math.Max(0, effectiveMinimum - 4);
			var originalMinimum = ZombieSettings.Values.minimumZombiesForWallPushing;
			var originalDangerousSituationMessage = ZombieSettings.Values.dangerousSituationMessage;
			var originalHome = map.areaManager.Home[fixture.wallCell];
			var originalRoof = map.roofGrid.RoofAt(fixture.destinationCell);
			var beforeLetters = DangerousLetterCount();
			var expectedLandingTicks = 102;
			var maxStartDelayTicks = Zombie.nthTickValues[(int)NthTick.Every8] + 1;
			var maxTicks = expectedLandingTicks + maxStartDelayTicks + 10;
			var startedPush = false;
			var startTick = -1;
			var landed = false;
			var landingTick = -1;
			var samples = new List<object>();

			ClearWallPushGridNeighborhood(map, fixture.zombieCell);
			grid.ChangeZombieCount(fixture.zombieCell, primedGridCount);
			map.roofGrid.SetRoof(fixture.destinationCell, RoofDefOf.RoofConstructed);
			PrepareWallPushZombie(map, zombie, fixture.zombieCell);

			try
			{
				ClearThrottleKey("DangerousSituation");
				map.areaManager.Home[fixture.wallCell] = true;
				ZombieSettings.Values.minimumZombiesForWallPushing = effectiveMinimum;
				ZombieSettings.Values.dangerousSituationMessage = true;
				zombie.jobs.StartJob(JobMaker.MakeJob(CustomDefs.Stumble), JobCondition.InterruptForced, null, true, false, null, null);
				for (var tick = 1; tick <= maxTicks; tick++)
				{
					AdvanceGameTicks(1);
					if (zombie.wallPushProgress >= 0f)
					{
						startedPush = true;
						if (startTick < 0)
							startTick = tick;
					}
					if (tick == 1 || tick % 20 == 0 || zombie.wallPushProgress < 0f && startedPush)
					{
						samples.Add(new
						{
							tick,
							position = ZombieRuntimeActions.DescribeCell(zombie.Position),
							progress = zombie.wallPushProgress,
							pushDestination = DescribeVector(zombie.wallPushDestination),
							currentJob = zombie.CurJobDef?.defName
						});
					}
					if (zombie.Position == fixture.destinationCell && zombie.wallPushProgress < 0f)
					{
						landed = true;
						landingTick = tick;
						break;
					}
				}
			}
			finally
			{
				map.areaManager.Home[fixture.wallCell] = originalHome;
				ZombieSettings.Values.minimumZombiesForWallPushing = originalMinimum;
				ZombieSettings.Values.dangerousSituationMessage = originalDangerousSituationMessage;
			}

			var afterLetters = DangerousLetterCount();
			var letterDelta = afterLetters - beforeLetters;
			var roofAfterLanding = map.roofGrid.RoofAt(fixture.destinationCell);
			var wallAtCellAfter = fixture.wallCell.GetEdifice(map);
			return new
			{
				success = startedPush
					&& landed
					&& zombie.Position == fixture.destinationCell
					&& wall.Destroyed == false
					&& wallAtCellAfter == wall
					&& roofAfterLanding == null
					&& letterDelta == 1,
				startedPush,
				startTick,
				landed,
				landingTick,
				landingTicksAfterStart = startTick < 0 || landingTick < 0 ? -1 : landingTick - startTick + 1,
				expectedLandingTicks,
				maxStartDelayTicks,
				maxTicks,
				effectiveMinimum,
				primedGridCount,
				beforeLetters,
				afterLetters,
				letterDelta,
				zombieCell = ZombieRuntimeActions.DescribeCell(fixture.zombieCell),
				wallCell = ZombieRuntimeActions.DescribeCell(fixture.wallCell),
				destinationCell = ZombieRuntimeActions.DescribeCell(fixture.destinationCell),
				wall = ZombieRuntimeActions.StableThingId(wall),
				wallDestroyed = wall.Destroyed,
				wallStillPresent = wallAtCellAfter == wall,
				originalRoof = originalRoof?.defName,
				roofAfterLanding = roofAfterLanding?.defName,
				zombie = DescribeZombie(zombie),
				samples
			};
		}

		static object RunSavedRoomDoorPressure(Map map, IntVec3 root)
		{
			if (TryFindDoorPressureFixtureCells(map, root, 20f, out var zombieCell, out var doorCell, out var error) == false)
				return error;

			var door = ThingMaker.MakeThing(ThingDefOf.Door, GenStuff.DefaultStuffFor(ThingDefOf.Door)) as Building_Door;
			if (door == null)
			{
				return new
				{
					success = false,
					error = "Could not create test door."
				};
			}
			GenSpawn.Spawn(door, doorCell, map, WipeMode.Vanish);
			door.SetFaction(Faction.OfPlayer);
			map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();

			var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
			if (zombie == null)
			{
				return new
				{
					success = false,
					zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
					doorCell = ZombieRuntimeActions.DescribeCell(doorCell),
					error = "Could not spawn door-pressure zombie."
				};
			}
			PrepareWallPushZombie(map, zombie, zombieCell);

			var targetMethod = FindAttackStaticTickDelegateMethod();
			var patchInfo = targetMethod == null ? null : Harmony.GetPatchInfo(targetMethod);
			var zPatch = patchInfo?.Prefixes?.FirstOrDefault(patch => patch.owner == "net.pardeike.zombieland");
			var job = JobMaker.MakeJob(JobDefOf.AttackStatic, door);
			job.maxNumStaticAttacks = 999;
			job.expiryInterval = 5000;
			job.playerForced = true;
			job.canBashDoors = true;
			zombie.jobs.StartJob(job, JobCondition.Succeeded, null, true, false, null, null);
			var started = zombie.CurJobDef == JobDefOf.AttackStatic;

			var samples = new List<object>();
			for (var tick = 1; tick <= 20; tick++)
			{
				AdvanceGameTicks(1);
				samples.Add(new
				{
					tick,
					phase = "closedDoor",
					job = zombie.CurJobDef?.defName,
					doorOpen = door.Open,
					doorHitPoints = door.HitPoints
				});
				if (zombie.CurJobDef == JobDefOf.AttackStatic)
					break;
			}

			var jobBeforeOpen = zombie.CurJobDef?.defName;
			var hitPointsBeforeOpen = door.HitPoints;
			door.StartManualOpenBy(zombie);
			var doorOpenAfterManual = door.Open;
			var endedAfterOpen = false;
			var tickEnded = -1;
			for (var tick = 1; tick <= 30; tick++)
			{
				AdvanceGameTicks(1);
				samples.Add(new
				{
					tick,
					phase = "openDoor",
					job = zombie.CurJobDef?.defName,
					doorOpen = door.Open,
					doorHitPoints = door.HitPoints
				});
				if (zombie.CurJobDef != JobDefOf.AttackStatic)
				{
					endedAfterOpen = true;
					tickEnded = tick;
					break;
				}
			}

			return new
			{
				success = targetMethod != null
					&& zPatch != null
					&& started
					&& jobBeforeOpen == JobDefOf.AttackStatic.defName
					&& doorOpenAfterManual
					&& endedAfterOpen
					&& tickEnded > 0,
				started,
				jobBeforeOpen,
				doorOpenAfterManual,
				endedAfterOpen,
				tickEnded,
				hitPointsBeforeOpen,
				hitPointsAfterOpen = door.Destroyed ? 0 : door.HitPoints,
				zombie = DescribeZombie(zombie),
				door = new
				{
					id = ZombieRuntimeActions.StableThingId(door),
					cell = ZombieRuntimeActions.DescribeCell(door.Position),
					door.Open,
					door.Destroyed,
					door.HitPoints
				},
				patch = new
				{
					targetMethod = targetMethod?.FullDescription(),
					patchedByZombieland = zPatch != null,
					owner = zPatch?.owner,
					prefix = zPatch?.PatchMethod?.FullDescription()
				},
				samples
			};
		}

		static int DangerousLetterCount()
		{
			return Find.LetterStack?.LettersListForReading?
				.Count(letter => letter?.def == CustomDefs.DangerousSituation) ?? 0;
		}

		static MethodBase FindAttackStaticTickDelegateMethod()
		{
			return typeof(JobDriver_AttackStatic)
				.InnerMethodsStartingWith("<MakeNewToils>b__")
				.FirstOrDefault(method =>
				{
					var parameters = method.GetParameters();
					return parameters.Length == 1 && parameters[0].ParameterType == typeof(int);
				});
		}

		static bool TryFindDoorPressureFixtureCells(Map map, IntVec3 root, float radius, out IntVec3 zombieCell, out IntVec3 doorCell, out object error)
		{
			zombieCell = IntVec3.Invalid;
			doorCell = IntVec3.Invalid;
			error = null;
			foreach (var candidate in GenRadial.RadialCellsAround(root, radius, true))
			{
				if (candidate.InBounds(map) == false || candidate.Fogged(map) || candidate.Standable(map) == false)
					continue;
				if (candidate.GetEdifice(map) != null || candidate.GetFirstThing<Mineable>(map) != null)
					continue;
				if (candidate.GetThingList(map).Any(thing => thing is Pawn))
					continue;

				foreach (var direction in new[] { IntVec3.East, IntVec3.West, IntVec3.North, IntVec3.South })
				{
					var doorCandidate = candidate + direction;
					if (doorCandidate.InBounds(map) == false || doorCandidate.Fogged(map))
						continue;
					if (doorCandidate.GetEdifice(map) != null || doorCandidate.GetFirstThing<Mineable>(map) != null)
						continue;
					if (doorCandidate.GetThingList(map).Any(thing => thing is Pawn))
						continue;

					zombieCell = candidate;
					doorCell = doorCandidate;
					return true;
				}
			}

			error = new
			{
				success = false,
				error = $"No clear door-pressure fixture pair was found near ({root.x}, {root.z})."
			};
			return false;
		}

		static object EvaluateShockerPlacement(Map map, object ignored)
		{
			var shocker = map.listerThings.ThingsOfDef(CustomDefs.ZombieShocker).OfType<ZombieShocker>().FirstOrDefault();
			if (shocker == null)
				return null;

			var worker = new PlaceWorker_ZombieShocker();
			var valid = worker.AllowsPlacing(CustomDefs.ZombieShocker, shocker.Position, shocker.Rotation, map);
			var invalidCenter = shocker.Position + IntVec3.North.RotatedBy(shocker.Rotation);
			var invalidInsideRoom = worker.AllowsPlacing(CustomDefs.ZombieShocker, invalidCenter, shocker.Rotation, map);
			var edificeUnderShocker = map.edificeGrid[shocker.Position];
			var thingsAtShockerCell = map.thingGrid.ThingsListAt(shocker.Position).ToArray();
			var supportBuildings = thingsAtShockerCell
				.Where(thing => thing != shocker && thing.def?.category == ThingCategory.Building)
				.ToArray();
			var spawningWipesPatchTargets = PatchedMethodsForPatchClass("GenSpawn_SpawningWipes_Patch");
			var shockerWipesWall = GenSpawn.SpawningWipes(CustomDefs.ZombieShocker, ThingDefOf.Wall, true);
			var shockerWipesDoor = GenSpawn.SpawningWipes(CustomDefs.ZombieShocker, ThingDefOf.Door, true);
			var controlWallWipesWall = GenSpawn.SpawningWipes(ThingDefOf.Wall, ThingDefOf.Wall, true);
			var noRoomCell = map.AllCells.FirstOrDefault(cell => cell.InBounds(map)
				&& cell.Standable(map)
				&& cell.Fogged(map) == false
				&& cell.GetRoom(map) == null
				&& cell.GetEdifice(map) == null
				&& cell.DistanceTo(shocker.Position) > 8f);
			var invalidNoRoom = noRoomCell.IsValid
				? worker.AllowsPlacing(CustomDefs.ZombieShocker, noRoomCell, shocker.Rotation, map)
				: new AcceptanceReport("No no-room test cell found.");
			return new
			{
				valid = DescribeAcceptanceReport(valid),
				invalidInsideRoom = DescribeAcceptanceReport(invalidInsideRoom),
				invalidNoRoom = DescribeAcceptanceReport(invalidNoRoom),
				noRoomCell = noRoomCell.IsValid ? ZombieRuntimeActions.DescribeCell(noRoomCell) : null,
				spawningWipes = new
				{
					success = spawningWipesPatchTargets.Length > 0
						&& shocker.OnWall()
						&& supportBuildings.Length > 0
						&& shockerWipesWall == false
						&& shockerWipesDoor == false
						&& controlWallWipesWall,
					patchTargets = spawningWipesPatchTargets,
					shockerCell = ZombieRuntimeActions.DescribeCell(shocker.Position),
					edificeUnderShocker = edificeUnderShocker == null ? null : new
					{
						id = ZombieRuntimeActions.StableThingId(edificeUnderShocker),
						defName = edificeUnderShocker.def?.defName,
						className = edificeUnderShocker.GetType().FullName,
						spawned = edificeUnderShocker.Spawned,
						destroyed = edificeUnderShocker.Destroyed
					},
					thingsAtShockerCell = thingsAtShockerCell.Select(thing => new
					{
						id = ZombieRuntimeActions.StableThingId(thing),
						defName = thing.def?.defName,
						className = thing.GetType().FullName,
						spawned = thing.Spawned,
						destroyed = thing.Destroyed
					}).ToArray(),
					supportBuildings = supportBuildings.Select(thing => new
					{
						id = ZombieRuntimeActions.StableThingId(thing),
						defName = thing.def?.defName,
						className = thing.GetType().FullName
					}).ToArray(),
					shockerOnWall = shocker.OnWall(),
					shockerWipesWall,
					shockerWipesDoor,
					controlWallWipesWall
				}
			};
		}

		static object DescribeAcceptanceReport(AcceptanceReport report)
		{
			return new
			{
				accepted = report.Accepted,
				reason = report.Reason
			};
		}

		static object DescribeDefenseThing(Thing thing)
		{
			if (thing == null)
				return null;

			var refuelable = thing.TryGetComp<CompRefuelable>();
			var breakable = thing.TryGetComp<CompBreakable>();
			var switchable = thing.TryGetComp<CompSwitchable>();
			var powerTrader = thing.TryGetComp<CompPowerTrader>();
			var powerBattery = thing.TryGetComp<CompPowerBattery>();
			var thumperStateField = typeof(ZombieThumper).GetField("state", BindingFlags.Instance | BindingFlags.NonPublic);
			var thumperStateValueField = typeof(ZombieThumper).GetField("stateValue", BindingFlags.Instance | BindingFlags.NonPublic);
			var thumperLastImpactField = typeof(ZombieThumper).GetField("lastImpactTicks", BindingFlags.Instance | BindingFlags.NonPublic);
			var pawn = thing is Chainsaw chainsaw ? chainsaw.pawn : null;
			var room = thing is ZombieShocker shocker && shocker.HasValidRoom()
				? ZombieShocker.GetValidRoom(shocker.Map, shocker.Position + IntVec3.North.RotatedBy(shocker.Rotation))
				: null;

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
				faction = thing.Faction?.def?.defName,
				hitPoints = thing.def?.useHitPoints == true ? thing.HitPoints : -1,
				maxHitPoints = thing.def?.useHitPoints == true ? thing.MaxHitPoints : -1,
				inspect = thing.GetInspectString(),
				gizmos = thing.GetGizmos().Select(DescribeDefenseGizmo).ToArray(),
				refuelable = refuelable == null ? null : new
				{
					refuelable.Fuel,
					refuelable.TargetFuelLevel,
					refuelable.HasFuel,
					refuelable.FuelPercentOfMax,
					fuelCapacity = refuelable.Props.fuelCapacity
				},
				breakable = breakable == null ? null : new
				{
					breakable.broken,
					trackedAsBroken = thing.Map?.GetComponent<BrokenManager>()?.brokenThings?.Contains(thing) ?? false
				},
				switchable = switchable == null ? null : new
				{
					switchable.isActive
				},
				powerTrader = powerTrader == null ? null : new
				{
					powerTrader.PowerOn,
					powerTrader.PowerOutput,
					hasPowerNet = powerTrader.PowerNet != null,
					batteryCount = powerTrader.PowerNet?.batteryComps?.Count ?? 0
				},
				powerBattery = powerBattery == null ? null : new
				{
					powerBattery.StoredEnergy,
					powerBattery.Props.storedEnergyMax
				},
				shocker = thing is ZombieShocker zombieShocker ? new
				{
					onWall = zombieShocker.OnWall(),
					hasValidRoom = zombieShocker.HasValidRoom(),
					rotation = zombieShocker.Rotation.ToString(),
					roomCellCount = room?.Cells.Count(cell => cell.Standable(zombieShocker.Map)) ?? 0
				} : null,
				thumper = thing is ZombieThumper zombieThumper ? new
				{
					zombieThumper.intensity,
					zombieThumper.intervalTicks,
					radius = zombieThumper.Radius,
					zombieThumper.IsActive,
					state = thumperStateField?.GetValue(zombieThumper)?.ToString(),
					stateValue = (int)(thumperStateValueField?.GetValue(zombieThumper) ?? 0),
					lastImpactTicks = (int)(thumperLastImpactField?.GetValue(zombieThumper) ?? 0)
				} : null,
				chainsaw = thing is Chainsaw describedChainsaw ? new
				{
					equipped = pawn != null,
					pawn = ZombieRuntimeActions.StableThingId(pawn),
					pawnDrafted = pawn?.Drafted ?? false,
					describedChainsaw.running,
					describedChainsaw.swinging,
					describedChainsaw.angle,
					describedChainsaw.inactiveCounter,
					describedChainsaw.stalledCounter
				} : null
			};
		}

		static object DescribeDefenseGizmo(Gizmo gizmo)
		{
			var command = gizmo as Command;
			return new
			{
				type = gizmo.GetType().FullName,
				label = command?.defaultLabel,
				desc = command?.defaultDesc,
				disabled = command?.disabled ?? false,
				hotKey = command?.hotKey?.defName
			};
		}
	}
}
