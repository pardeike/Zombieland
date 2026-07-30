using RimBridgeServer.Sdk;
using RimWorld;
using System;
using System.Linq;
using Verse;

namespace ZombieLand
{
	public sealed partial class ZombielandBridgeTools
	{
		[Tool("zombieland/albino_hack_targets", Description = "List current-map albino sabotage targets and live planner state using the same target eligibility code as the albino job driver.")]
		public static object AlbinoHackTargets(
			[ToolParameter(Description = "Maximum direct targets to return per category.", Required = false, DefaultValue = 200)] int maxTargets = 200,
			[ToolParameter(Description = "Maximum route-only closed doors to return.", Required = false, DefaultValue = 120)] int maxDoors = 120)
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

			var cappedTargets = Math.Max(1, Math.Min(maxTargets, 1000));
			var cappedDoors = Math.Max(0, Math.Min(maxDoors, 500));
			var buildingTargets = map.listerBuildings.allBuildingsColonist
				.Where(SabotageHandler.CanHackBuilding)
				.OrderBy(building => building.def?.defName)
				.ThenBy(building => building.Position.x)
				.ThenBy(building => building.Position.z)
				.ToArray();
			var weaponTargets = map.listerThings.ThingsInGroup(ThingRequestGroup.Weapon)
				.Where(SabotageHandler.CanHackThing)
				.OrderByDescending(weapon => SabotageHandler.WeaponSabotageScore(map, weapon))
				.ThenBy(weapon => weapon.def?.defName)
				.ThenBy(weapon => weapon.Position.x)
				.ThenBy(weapon => weapon.Position.z)
				.ToArray();
			var selectableWeaponTargets = weaponTargets
				.Where(weapon => IsAlbinoEnoughHackedItem(map, weapon) == false)
				.ToArray();
			var routeDoors = map.listerBuildings.allBuildingsColonist
				.OfType<Building_Door>()
				.Where(door => door.Spawned && door.Destroyed == false && door.Open == false)
				.OrderBy(door => door.Position.x)
				.ThenBy(door => door.Position.z)
				.ToArray();
			var albinos = map.mapPawns.AllPawnsSpawned
				.OfType<Zombie>()
				.Where(zombie => zombie.isAlbino)
				.OrderBy(zombie => zombie.ThingID)
				.ToArray();

			return new
			{
				success = true,
				map = new
				{
					id = map.uniqueID,
					size = new { x = map.Size.x, z = map.Size.z },
					ticksGame = GenTicks.TicksGame
				},
				rules = new[]
				{
					"Direct building targets are map.listerBuildings.allBuildingsColonist where SabotageHandler.CanHackBuilding(building) is true.",
					"Direct weapon targets are spawned ranged weapons with hit points.",
					"The albino now tries the best safe-route direct hack target before lower-value room/home movement, skipping direct targets whose current route crosses dangerous pressure.",
					"After a successful weapon hack, Zombieland remembers that item on the map so all albinos skip it afterward.",
					"A planned scream is allowed to walk into pressure because the scream itself is the mitigation; ordinary movement remains pressure-averse.",
					"Closed doors are route-only blockers: they are hacked only when a chosen path crosses them."
				},
				hackDurationTicks = 240,
				counts = new
				{
					albinos = albinos.Length,
					directBuildings = buildingTargets.Length,
					directWeapons = weaponTargets.Length,
					selectableDirectWeapons = selectableWeaponTargets.Length,
					enoughHackedWeapons = weaponTargets.Length - selectableWeaponTargets.Length,
					routeOnlyClosedDoors = routeDoors.Length
				},
				albinos = albinos.Select(DescribeAlbinoPlannerState).ToArray(),
				buildings = buildingTargets.Take(cappedTargets).Select(DescribeAlbinoBuildingHackTarget).ToArray(),
				weapons = weaponTargets.Take(cappedTargets).Select(weapon => DescribeAlbinoWeaponHackTarget(map, weapon)).ToArray(),
				routeOnlyDoors = routeDoors.Take(cappedDoors).Select(DescribeAlbinoRouteDoorHackTarget).ToArray(),
				truncated = new
				{
					buildings = buildingTargets.Length > cappedTargets,
					weapons = weaponTargets.Length > cappedTargets,
					routeOnlyDoors = routeDoors.Length > cappedDoors
				}
			};
		}

		static object DescribeAlbinoPlannerState(Zombie zombie)
		{
			var driver = zombie.jobs?.curDriver as JobDriver_Sabotage;
			var pather = zombie.pather;
			return new
			{
				id = ZombieRuntimeActions.StableThingId(zombie),
				thingId = zombie.ThingID,
				label = zombie.LabelCap.ToString(),
				position = ZombieRuntimeActions.DescribeCell(zombie.Position),
				job = zombie.CurJobDef?.defName,
				driver = driver == null ? null : new
				{
					destination = driver.destination.IsValid ? ZombieRuntimeActions.DescribeCell(driver.destination) : null,
					driver.interruptibleDestination,
					driver.safetyDestination,
					driver.safetyPathPressureLimit,
					driver.fallbackDestination,
					driver.waitCounter,
					driver.nextDefensiveScreamCheckTick,
					driver.nextStrategicRecheckTick
				},
				pather = pather == null ? null : new
				{
					pather.Moving,
					destination = pather.Moving && pather.Destination.Cell.IsValid ? ZombieRuntimeActions.DescribeCell(pather.Destination.Cell) : null,
					nextCell = pather.nextCell.IsValid ? ZombieRuntimeActions.DescribeCell(pather.nextCell) : null,
					pather.nextCellCostLeft,
					pather.nextCellCostTotal,
					pathFound = pather.curPath?.Found,
					pathNodesLeft = pather.curPath?.NodesLeftCount
				}
			};
		}

		static object DescribeAlbinoBuildingHackTarget(Building building)
		{
			var flickable = building.TryGetComp<CompFlickable>();
			var breakdownable = building.TryGetComp<CompBreakdownable>();
			var action = flickable?.SwitchIsOn == true
				? "turn_off_flickable"
				: breakdownable?.BrokenDown == false ? "force_breakdown" : "none";
			var effect = action == "turn_off_flickable"
				? "Set CompFlickable.SwitchIsOn to false."
				: action == "force_breakdown"
					? "Call CompBreakdownable.DoBreakdown()."
					: "No current albino hack effect.";

			return new
			{
				id = ZombieRuntimeActions.StableThingId(building),
				thingId = building.ThingID,
				defName = building.def?.defName,
				label = building.LabelCap.ToString(),
				position = ZombieRuntimeActions.DescribeCell(building.Position),
				occupiedRect = DescribeAlbinoOccupiedRect(building),
				faction = building.Faction?.def?.defName,
				hitPoints = building.HitPoints,
				maxHitPoints = building.MaxHitPoints,
				action,
				effect,
				selection = "Survival-first safe-route target selection when the building sabotage branch is selected.",
				hasFlickable = flickable != null,
				flickableOn = flickable?.SwitchIsOn,
				hasBreakdownable = breakdownable != null,
				brokenDown = breakdownable?.BrokenDown,
				hasPowerTrader = building.TryGetComp<CompPowerTrader>() != null
			};
		}

		static object DescribeAlbinoWeaponHackTarget(Map map, Thing weapon)
		{
			var damage = Math.Max(1, weapon.HitPoints / 2);
			var enoughHacked = IsAlbinoEnoughHackedItem(map, weapon);
			return new
			{
				id = ZombieRuntimeActions.StableThingId(weapon),
				thingId = weapon.ThingID,
				defName = weapon.def?.defName,
				label = weapon.LabelCap.ToString(),
				position = ZombieRuntimeActions.DescribeCell(weapon.Position),
				occupiedRect = DescribeAlbinoOccupiedRect(weapon),
				faction = weapon.Faction?.def?.defName,
				hitPoints = weapon.HitPoints,
				maxHitPoints = weapon.MaxHitPoints,
				marketValue = weapon.MarketValue,
				weaponSabotageScore = SabotageHandler.WeaponSabotageScore(map, weapon),
				inHomeArea = map.areaManager.Home[weapon.Position],
				enoughHacked,
				selectable = enoughHacked == false,
				action = "damage_ranged_weapon",
				effect = $"Apply Deterioration damage for {damage}, which is half of current hit points rounded down with a minimum of 1.",
				expectedHitPointsAfterDamage = Math.Max(0, weapon.HitPoints - damage),
				selection = "Survival-first safe-route target selection when the weapon sabotage branch is selected; WeaponSabotageScore is only a tie-breaker after route safety and distance. A map-remembered enough-hacked weapon is skipped by all albinos."
			};
		}

		static object DescribeAlbinoRouteDoorHackTarget(Building_Door door)
		{
			return new
			{
				id = ZombieRuntimeActions.StableThingId(door),
				thingId = door.ThingID,
				defName = door.def?.defName,
				label = door.LabelCap.ToString(),
				position = ZombieRuntimeActions.DescribeCell(door.Position),
				occupiedRect = DescribeAlbinoOccupiedRect(door),
				faction = door.Faction?.def?.defName,
				hitPoints = door.HitPoints,
				maxHitPoints = door.MaxHitPoints,
				open = door.Open,
				action = "manual_open_blocking_door",
				effect = "Call Building_Door.StartManualOpenBy(albino) and multiply ticksUntilClose by 4.",
				selection = "Route-only: used when a path to a scream cell, room cell, home cell, building target, or weapon target is blocked by this closed door."
			};
		}

		static object DescribeAlbinoOccupiedRect(Thing thing)
		{
			var rect = thing.OccupiedRect();
			return new
			{
				minX = rect.minX,
				maxX = rect.maxX,
				minZ = rect.minZ,
				maxZ = rect.maxZ
			};
		}
	}
}
