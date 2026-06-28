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
		[Tool("zombieland/zap_zombies_with_shocker", Description = "Build a powered zombie shocker room, run the real ZapZombies job, and verify a zombie in the room is paralyzed.")]
		public static object ZapZombiesWithShocker()
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

			var destroyedZombies = ZombieRuntimeActions.DestroyZombies(map);
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindShockerFixtureCell(map, root, 24f, out var shockerCell, out var fixtureError) == false)
				return fixtureError;

			var wallDef = ThingDefOf.Wall;
			var conduitDef = DefDatabase<ThingDef>.GetNamed("PowerConduit", false);
			var batteryDef = DefDatabase<ThingDef>.GetNamed("Battery", false);
			if (wallDef == null || conduitDef == null || batteryDef == null)
			{
				return new
				{
					success = false,
					error = "ThingDef Wall, PowerConduit, or Battery was not found."
				};
			}

			var fixtureThings = new List<Thing>();
			for (var dx = -2; dx <= 2; dx++)
			{
				for (var dz = 0; dz <= 4; dz++)
				{
					if (dx != -2 && dx != 2 && dz != 0 && dz != 4)
						continue;

					var wallCell = shockerCell + new IntVec3(dx, 0, dz);
					var wall = ThingMaker.MakeThing(wallDef, ThingDefOf.WoodLog) as Building;
					if (wall == null)
						continue;
					GenSpawn.Spawn(wall, wallCell, map, WipeMode.Vanish);
					wall.SetFaction(Faction.OfPlayer);
					fixtureThings.Add(wall);
				}
			}

			var shocker = ThingMaker.MakeThing(CustomDefs.ZombieShocker) as ZombieShocker;
			if (shocker == null)
			{
				return new
				{
					success = false,
					error = "Could not create ZombieShocker."
				};
			}
			shocker.SetFactionDirect(Faction.OfPlayer);
			GenSpawn.Spawn(shocker, shockerCell, map, Rot4.North, WipeMode.Vanish, false);
			fixtureThings.Add(shocker);

			var conduitCell = shockerCell + IntVec3.South;
			var batteryCell = shockerCell + new IntVec3(1, 0, -3);
			var bridgeConduitCell = batteryCell + new IntVec3(0, 0, 2);
			var actorCell = shockerCell + IntVec3.South + IntVec3.West;
			var zombieCell = shockerCell + new IntVec3(0, 0, 2);
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

			var actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(actor, actorCell, map, WipeMode.Vanish);
			DisablePawnWork(actor);

			var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
			if (zombie == null)
			{
				return new
				{
					success = false,
					shockerCell = ZombieRuntimeActions.DescribeCell(shockerCell),
					error = "ZombieGenerator.SpawnZombie returned no shocker test zombie."
				};
			}
			zombie.ropedBy = null;
			zombie.paralyzedUntil = 0;

			var room = ZombieShocker.GetValidRoom(map, shockerCell + IntVec3.North);
			var roomCellCount = room?.Cells.Count(cell => cell.Standable(map)) ?? 0;
			var hasValidRoom = shocker.HasValidRoom();
			var canReserveAndReach = actor.CanReach(shocker, PathEndMode.InteractionCell, Danger.Deadly)
				&& actor.CanReserve(shocker);
			var batteryCount = shockerPower?.PowerNet?.batteryComps?.Count ?? 0;
			var storedEnergyBefore = batteryPower?.StoredEnergy ?? 0f;
			var ropedBefore = zombie.ropedBy != null;
			var paralyzedUntilBefore = zombie.paralyzedUntil;
			var zapMotesBefore = CountZombieZapMotesNear(map, zombieCell, 2f);

			var job = JobMaker.MakeJob(CustomDefs.ZapZombies, shocker);
			job.playerForced = true;
			_ = actor.jobs.TryTakeOrderedJob(job, new JobTag?(JobTag.Misc), false);
			var startedJob = actor.CurJobDef?.defName;
			var maxTicks = 90 + 45 + roomCellCount + 20;
			var tickHit = -1;
			var samples = new List<object>();

			for (var tick = 1; tick <= maxTicks; tick++)
			{
				AdvanceGameTicks(1);
				var ropedNow = zombie.ropedBy != null;
				var paralyzedNow = zombie.paralyzedUntil > GenTicks.TicksAbs;
				var zapMotesNow = CountZombieZapMotesNear(map, zombieCell, 2f);
				var hitNow = paralyzedNow && zapMotesNow > zapMotesBefore;
				if (tick == 1 || tick == 90 || tick == 135 || tick == maxTicks || tick % 30 == 0 || hitNow)
				{
					samples.Add(new
					{
						tick,
						actorJob = actor.CurJobDef?.defName,
						roped = ropedNow,
						paralyzed = paralyzedNow,
						zombie.paralyzedUntil,
						zapMotes = zapMotesNow
					});
				}

				if (hitNow)
				{
					tickHit = tick;
					break;
				}
			}

			var zapMotesAfter = CountZombieZapMotesNear(map, zombieCell, 2f);
			var storedEnergyAfter = batteryPower?.StoredEnergy ?? 0f;
			var ropedAfter = zombie.ropedBy != null;
			var paralyzedAfter = zombie.paralyzedUntil > GenTicks.TicksAbs;

			return new
			{
				success = hasValidRoom
					&& canReserveAndReach
					&& shockerPower?.PowerOn == true
					&& batteryCount > 0
					&& startedJob == CustomDefs.ZapZombies.defName
					&& ropedBefore == false
					&& ropedAfter == false
					&& paralyzedAfter
					&& tickHit > 0
					&& zapMotesAfter > zapMotesBefore,
				destroyedZombies,
				shocker = new
				{
					id = ZombieRuntimeActions.StableThingId(shocker),
					cell = ZombieRuntimeActions.DescribeCell(shockerCell),
					rotation = shocker.Rotation.ToString(),
					powerOn = shockerPower?.PowerOn,
					hasPowerNet = shockerPower?.PowerNet != null,
					batteryCount,
					hasValidRoom,
					onWall = shocker.OnWall()
				},
				actor = DescribePawn(actor),
				zombie = DescribeZombie(zombie),
				cells = new
				{
					shocker = ZombieRuntimeActions.DescribeCell(shockerCell),
					conduit = ZombieRuntimeActions.DescribeCell(conduitCell),
					bridgeConduit = ZombieRuntimeActions.DescribeCell(bridgeConduitCell),
					battery = ZombieRuntimeActions.DescribeCell(batteryCell),
					actor = ZombieRuntimeActions.DescribeCell(actorCell),
					zombie = ZombieRuntimeActions.DescribeCell(zombieCell)
				},
				fixtureThingCount = fixtureThings.Count,
				roomCellCount,
				canReserveAndReach,
				startedJob,
				maxTicks,
				tickHit,
				ropedBefore,
				ropedAfter,
				paralyzedUntilBefore,
				paralyzedUntilAfter = zombie.paralyzedUntil,
				paralyzedAfter,
				zapMotesBefore,
				zapMotesAfter,
				zapMoteDelta = zapMotesAfter - zapMotesBefore,
				storedEnergyBefore,
				storedEnergyAfter,
				storedEnergyDelta = storedEnergyBefore - storedEnergyAfter,
				samples
			};
		}

		[Tool("zombieland/thumper_impact_cycle", Description = "Spawn and fuel a real Zombie Thumper, run its source-derived cycle to impact, and verify fuel is consumed.")]
		public static object ThumperImpactCycle()
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
			if (TryFindClearBuildingFootprint(map, CustomDefs.Thumper, root, 24f, out var thumperCell, out var footprintError) == false)
				return footprintError;

			var thumper = ThingMaker.MakeThing(CustomDefs.Thumper) as ZombieThumper;
			if (thumper == null)
			{
				return new
				{
					success = false,
					error = "Could not create ZombieThumper."
				};
			}

			thumper.SetFactionDirect(Faction.OfPlayer);
			GenSpawn.Spawn(thumper, thumperCell, map, Rot4.North, WipeMode.Vanish, false);
			var refuelable = thumper.TryGetComp<CompRefuelable>();
			var switchable = thumper.TryGetComp<CompSwitchable>();
			var chemfuelDef = ThingDefOf.Chemfuel;
			if (refuelable == null || chemfuelDef == null)
			{
				return new
				{
					success = false,
					thumperCell = ZombieRuntimeActions.DescribeCell(thumperCell),
					error = "The spawned thumper did not have a refuelable comp or Chemfuel was unavailable."
				};
			}

			var fuel = ThingMaker.MakeThing(chemfuelDef);
			fuel.stackCount = Math.Min(chemfuelDef.stackLimit, refuelable.GetFuelCountToFullyRefuel());
			GenSpawn.Spawn(fuel, thumperCell + IntVec3.South, map, WipeMode.Vanish);
			refuelable.Refuel(new List<Thing> { fuel });
			if (switchable != null)
				switchable.isActive = true;

			thumper.intensity = 0.05f;
			thumper.intervalTicks = GenDate.TicksPerHour / 25;
			var upTicks = Mathf.FloorToInt(ZombieThumper.upwardsTicks * thumper.intensity);
			var fallTicks = Mathf.FloorToInt(Mathf.Sqrt(upTicks / ZombieThumper.accelerationFactor));
			var impactByTicks = Math.Max(thumper.intervalTicks, 30 + upTicks) + fallTicks + 3;
			var fuelBefore = refuelable.Fuel;
			var isActiveBefore = thumper.IsActive;
			var radiusBefore = thumper.Radius;
			var stateField = typeof(ZombieThumper).GetField("state", BindingFlags.Instance | BindingFlags.NonPublic);
			var stateValueField = typeof(ZombieThumper).GetField("stateValue", BindingFlags.Instance | BindingFlags.NonPublic);
			var lastImpactTicksField = typeof(ZombieThumper).GetField("lastImpactTicks", BindingFlags.Instance | BindingFlags.NonPublic);
			var lastImpactBefore = (int)(lastImpactTicksField?.GetValue(thumper) ?? 0);
			var samples = new List<object>();
			var tickHit = -1;

			for (var tick = 1; tick <= impactByTicks; tick++)
			{
				AdvanceGameTicks(1);
				var fuelNow = refuelable.Fuel;
				if (tick == 1 || tick == upTicks + 30 || tick == impactByTicks || tick % 25 == 0 || fuelNow < fuelBefore)
				{
					samples.Add(new
					{
						tick,
						state = stateField?.GetValue(thumper)?.ToString(),
						stateValue = (int)(stateValueField?.GetValue(thumper) ?? 0),
						fuel = fuelNow,
						lastImpactTicks = (int)(lastImpactTicksField?.GetValue(thumper) ?? 0)
					});
				}

				if (fuelNow < fuelBefore)
				{
					tickHit = tick;
					break;
				}
			}

			var fuelAfter = refuelable.Fuel;
			var lastImpactAfter = (int)(lastImpactTicksField?.GetValue(thumper) ?? 0);

			return new
			{
				success = thumper.Spawned
					&& isActiveBefore
					&& radiusBefore > 0
					&& tickHit > 0
					&& fuelAfter < fuelBefore
					&& lastImpactAfter > lastImpactBefore,
				thumper = new
				{
					id = ZombieRuntimeActions.StableThingId(thumper),
					cell = ZombieRuntimeActions.DescribeCell(thumperCell),
					spawned = thumper.Spawned,
					hitPoints = thumper.HitPoints,
					intensity = thumper.intensity,
					intervalTicks = thumper.intervalTicks,
					radius = thumper.Radius,
					isActive = thumper.IsActive,
					state = stateField?.GetValue(thumper)?.ToString(),
					stateValue = (int)(stateValueField?.GetValue(thumper) ?? 0)
				},
				upTicks,
				fallTicks,
				impactByTicks,
				tickHit,
				fuelBefore,
				fuelAfter,
				fuelDelta = fuelBefore - fuelAfter,
				lastImpactBefore,
				lastImpactAfter,
				lastImpactDelta = lastImpactAfter - lastImpactBefore,
				hasFuelAfter = refuelable.HasFuel,
				samples
			};
		}

		[Tool("zombieland/thumper_visual_wave_damage_contract", Description = "Run a thumper through lift, release, impact, visible dust-wave expansion, and distance-based seismic damage.")]
		public static object ThumperVisualWaveDamageContract()
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
			if (TryFindThumperWaveFixture(map, root, 30f, out var thumperCell, out var nearCell, out var midCell, out var farCell, out var fixtureError) == false)
				return fixtureError;

			Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;

			var thumper = ThingMaker.MakeThing(CustomDefs.Thumper) as ZombieThumper;
			if (thumper == null)
			{
				return new
				{
					success = false,
					error = "Could not create ZombieThumper."
				};
			}

			thumper.SetFactionDirect(Faction.OfPlayer);
			GenSpawn.Spawn(thumper, thumperCell, map, Rot4.North, WipeMode.Vanish, false);
			var refuelable = thumper.TryGetComp<CompRefuelable>();
			var switchable = thumper.TryGetComp<CompSwitchable>();
			if (refuelable == null)
			{
				return new
				{
					success = false,
					thumperCell = ZombieRuntimeActions.DescribeCell(thumperCell),
					error = "The spawned thumper did not have a refuelable comp."
				};
			}

			var fuel = ThingMaker.MakeThing(ThingDefOf.Chemfuel);
			fuel.stackCount = Math.Min(ThingDefOf.Chemfuel.stackLimit, refuelable.GetFuelCountToFullyRefuel());
			GenSpawn.Spawn(fuel, thumperCell + IntVec3.South, map, WipeMode.Vanish);
			refuelable.Refuel(new List<Thing> { fuel });
			if (switchable != null)
				switchable.isActive = true;

			thumper.intensity = 0.5f;
			thumper.intervalTicks = 130;
			var radius = thumper.Radius;
			var upTicks = Mathf.FloorToInt(ZombieThumper.upwardsTicks * thumper.intensity);
			var fallTicks = Mathf.FloorToInt(Mathf.Sqrt(upTicks / ZombieThumper.accelerationFactor));
			var impactByTicks = Math.Max(thumper.intervalTicks, 30 + upTicks) + fallTicks + 3;
			var stateField = typeof(ZombieThumper).GetField("state", BindingFlags.Instance | BindingFlags.NonPublic);
			var stateValueField = typeof(ZombieThumper).GetField("stateValue", BindingFlags.Instance | BindingFlags.NonPublic);
			var lastImpactTicksField = typeof(ZombieThumper).GetField("lastImpactTicks", BindingFlags.Instance | BindingFlags.NonPublic);
			var dustsField = typeof(ZombieThumper).GetField("dusts", BindingFlags.Instance | BindingFlags.NonPublic);
			var dustObjField = dustsField?.FieldType.GetGenericArguments().FirstOrDefault()?.GetField("obj", BindingFlags.Instance | BindingFlags.Public);
			var dustRadiusField = dustsField?.FieldType.GetGenericArguments().FirstOrDefault()?.GetField("currentRadius", BindingFlags.Instance | BindingFlags.Public);

			var nearWall = SpawnWoodWall(map, nearCell);
			var midWall = SpawnWoodWall(map, midCell);
			var farWall = SpawnWoodWall(map, farCell);
			if (nearWall == null || midWall == null || farWall == null)
			{
				return new
				{
					success = false,
					error = "Could not create thumper wave target walls."
				};
			}

			var fuelBefore = refuelable.Fuel;
			var lastImpactBefore = (int)(lastImpactTicksField?.GetValue(thumper) ?? 0);
			var nearBefore = nearWall.HitPoints;
			var midBefore = midWall.HitPoints;
			var farBefore = farWall.HitPoints;
			var samples = new List<object>();
			var dustSamples = new List<object>();
			var maxUpPole = 0f;
			var maxPausedPole = 0f;
			var minFallingPole = 1f;
			var impactTick = -1;

			for (var tick = 1; tick <= impactByTicks; tick++)
			{
				AdvanceGameTicks(1);
				var stateName = stateField?.GetValue(thumper)?.ToString() ?? "";
				var stateValue = (int)(stateValueField?.GetValue(thumper) ?? 0);
				var pole = ZombieThumper.DebugPolePosition(stateName, stateValue, thumper.intensity);
				if (stateName == "Upwards")
					maxUpPole = Mathf.Max(maxUpPole, pole);
				if (stateName == "Paused")
					maxPausedPole = Mathf.Max(maxPausedPole, pole);
				if (stateName == "Falling")
					minFallingPole = Mathf.Min(minFallingPole, pole);
				if (tick == 1 || tick == 30 || tick == 60 || tick == 90 || tick == 120 || tick == impactByTicks || stateName == "Impacting")
				{
					samples.Add(new
					{
						tick,
						state = stateName,
						stateValue,
						pole,
						fuel = refuelable.Fuel,
						lastImpactTicks = (int)(lastImpactTicksField?.GetValue(thumper) ?? 0)
					});
				}
				if (stateName == "Impacting")
				{
					impactTick = tick;
					break;
				}
			}

			var dustAtImpact = DescribeThumperDusts(dustsField, dustObjField, dustRadiusField, thumper);
			var sampleTicks = new[] { 1, 60, 120, 180, 240, 300, 330 };
			foreach (var tick in sampleTicks)
			{
				AdvanceGameTicks(tick == 1 ? 1 : tick - (dustSamples.Count == 0 ? 0 : sampleTicks[dustSamples.Count - 1]));
				dustSamples.Add(new
				{
					tick,
					dusts = DescribeThumperDusts(dustsField, dustObjField, dustRadiusField, thumper),
					nearHitPoints = nearWall.Destroyed ? 0 : nearWall.HitPoints,
					midHitPoints = midWall.Destroyed ? 0 : midWall.HitPoints,
					farHitPoints = farWall.Destroyed ? 0 : farWall.HitPoints
				});
			}

			var nearAfter = nearWall.Destroyed ? 0 : nearWall.HitPoints;
			var midAfter = midWall.Destroyed ? 0 : midWall.HitPoints;
			var farAfter = farWall.Destroyed ? 0 : farWall.HitPoints;
			var nearDelta = nearBefore - nearAfter;
			var midDelta = midBefore - midAfter;
			var farDelta = farBefore - farAfter;
			var finalDusts = DescribeThumperDusts(dustsField, dustObjField, dustRadiusField, thumper);
			var dustVisualsMatched = dustAtImpact.Any(DustVisualMatchesThumperContract);
			var radii = dustSamples
				.Select(sample => sample.GetType().GetProperty("dusts")?.GetValue(sample))
				.OfType<object[]>()
				.SelectMany(items => items)
				.Select(item => item.GetType().GetProperty("currentRadius")?.GetValue(item))
				.OfType<int>()
				.ToArray();
			var radiusExpanded = radii.Length >= 2 && radii.Max() > radii.Min() && radii.Max() >= 20;
			var fuelAfter = refuelable.Fuel;
			var lastImpactAfter = (int)(lastImpactTicksField?.GetValue(thumper) ?? 0);

			return new
			{
				success = thumper.Spawned
					&& impactTick > 0
					&& fuelAfter < fuelBefore
					&& lastImpactAfter > lastImpactBefore
					&& maxUpPole >= 0.95f
					&& maxPausedPole >= 0.99f
					&& minFallingPole <= 0.15f
					&& dustAtImpact.Length > 0
					&& dustVisualsMatched
					&& radiusExpanded
					&& nearDelta > 0
					&& midDelta > 0
					&& nearDelta > farDelta
					&& nearDelta >= midDelta
					&& ZombieThumper.DebugSeismicWaveDamage(3, radius) > ZombieThumper.DebugSeismicWaveDamage(12, radius)
					&& ZombieThumper.DebugSeismicWaveDamage(12, radius) > ZombieThumper.DebugSeismicWaveDamage(22, radius),
				thumper = new
				{
					id = ZombieRuntimeActions.StableThingId(thumper),
					cell = ZombieRuntimeActions.DescribeCell(thumperCell),
					thumper.intensity,
					thumper.intervalTicks,
					radius,
					upTicks,
					fallTicks,
					impactByTicks,
					impactTick
				},
				animation = new
				{
					maxUpPole,
					maxPausedPole,
					minFallingPole,
					samples
				},
				wave = new
				{
					dustAtImpact,
					dustSamples,
					finalDusts,
					dustVisualsMatched,
					radiusExpanded,
					radii
				},
				damage = new
				{
					near = DescribeWaveTarget(nearWall, nearCell, 3, radius, nearBefore, nearAfter),
					mid = DescribeWaveTarget(midWall, midCell, 12, radius, midBefore, midAfter),
					far = DescribeWaveTarget(farWall, farCell, 22, radius, farBefore, farAfter)
				},
				fuelBefore,
				fuelAfter,
				fuelDelta = fuelBefore - fuelAfter,
				lastImpactBefore,
				lastImpactAfter,
				lastImpactDelta = lastImpactAfter - lastImpactBefore
			};
		}

		static bool TryFindThumperWaveFixture(Map map, IntVec3 root, float radius, out IntVec3 thumperCell, out IntVec3 nearCell, out IntVec3 midCell, out IntVec3 farCell, out object error)
		{
			thumperCell = IntVec3.Invalid;
			nearCell = IntVec3.Invalid;
			midCell = IntVec3.Invalid;
			farCell = IntVec3.Invalid;
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

			foreach (var candidate in GenRadial.RadialCellsAround(root, radius, true))
			{
				if (candidate.InBounds(map) == false || candidate.Fogged(map))
					continue;

				var clearFootprint = true;
				foreach (var footprintCell in GenAdj.OccupiedRect(candidate, Rot4.North, CustomDefs.Thumper.size))
				{
					if (IsClearFixtureCell(map, footprintCell, false) == false)
					{
						clearFootprint = false;
						break;
					}
				}
				if (clearFootprint == false)
					continue;

				var near = candidate + new IntVec3(3, 0, 0);
				var mid = candidate + new IntVec3(12, 0, 0);
				var far = candidate + new IntVec3(22, 0, 0);
				if (IsClearFixtureCell(map, near, true) == false
					|| IsClearFixtureCell(map, mid, true) == false
					|| IsClearFixtureCell(map, far, true) == false)
					continue;

				thumperCell = candidate;
				nearCell = near;
				midCell = mid;
				farCell = far;
				return true;
			}

			error = new
			{
				success = false,
				error = $"No clear thumper shockwave fixture was found near ({root.x}, {root.z})."
			};
			return false;
		}

		static bool IsClearFixtureCell(Map map, IntVec3 cell, bool requireStandable)
		{
			if (cell.InBounds(map) == false || cell.Fogged(map))
				return false;
			if (requireStandable && cell.Standable(map) == false)
				return false;
			if (cell.GetEdifice(map) != null || cell.GetFirstThing<Mineable>(map) != null)
				return false;
			if (cell.GetThingList(map).Any(thing => thing is Pawn))
				return false;
			return true;
		}

		static object[] DescribeThumperDusts(FieldInfo dustsField, FieldInfo dustObjField, FieldInfo dustRadiusField, ZombieThumper thumper)
		{
			if (!(dustsField?.GetValue(thumper) is System.Collections.IEnumerable dusts))
				return Array.Empty<object>();

			var result = new List<object>();
			foreach (var dust in dusts)
			{
				var obj = dustObjField?.GetValue(dust) as GameObject;
				var particleSystem = obj?.GetComponent<ParticleSystem>();
				var renderer = obj?.GetComponent<ParticleSystemRenderer>();
				var shapeRotation = Vector3.zero;
				var transformEuler = Vector3.zero;
				var simulationSpeed = 0f;
				var startSpeed = 0f;
				var startLifetime = 0f;
				var colorOverLifetimeEnabled = false;
				if (particleSystem != null)
				{
					var main = particleSystem.main;
					var shape = particleSystem.shape;
					var colorOverLifetime = particleSystem.colorOverLifetime;
					shapeRotation = shape.rotation;
					simulationSpeed = main.simulationSpeed;
					startSpeed = main.startSpeed.constant;
					startLifetime = main.startLifetime.constant;
					colorOverLifetimeEnabled = colorOverLifetime.enabled;
				}
				if (obj != null)
					transformEuler = obj.transform.localEulerAngles;
				var expectedStartSpeed = ZombieThumper.DebugShockwaveStartSpeed(thumper.Radius);
				result.Add(new
				{
					currentRadius = (int)(dustRadiusField?.GetValue(dust) ?? -1),
					active = obj?.activeSelf ?? false,
					alive = particleSystem?.IsAlive(true) ?? false,
					playing = particleSystem?.isPlaying ?? false,
					time = particleSystem?.time ?? 0f,
					simulationSpeed,
					startSpeed,
					expectedStartSpeed,
					startSpeedSynced = Mathf.Abs(startSpeed - expectedStartSpeed) <= 0.1f,
					startLifetime,
					colorOverLifetimeEnabled,
					transformEuler = new
					{
						transformEuler.x,
						transformEuler.y,
						transformEuler.z
					},
					shapeRotation = new
					{
						shapeRotation.x,
						shapeRotation.y,
						shapeRotation.z
					},
					rendererEnabled = renderer?.enabled ?? false,
					renderMode = renderer?.renderMode.ToString(),
					material = renderer?.sharedMaterial?.name,
					shader = renderer?.sharedMaterial?.shader?.name,
					renderQueue = renderer?.sharedMaterial?.renderQueue ?? 0
				});
			}
			return result.ToArray();
		}

		static bool DustVisualMatchesThumperContract(object dust)
		{
			var type = dust.GetType();
			var startSpeedSynced = (bool)(type.GetProperty("startSpeedSynced")?.GetValue(dust) ?? false);
			var colorOverLifetimeEnabled = (bool)(type.GetProperty("colorOverLifetimeEnabled")?.GetValue(dust) ?? false);
			var transformEuler = type.GetProperty("transformEuler")?.GetValue(dust);
			var x = (float)(transformEuler?.GetType().GetProperty("x")?.GetValue(transformEuler) ?? 0f);
			return startSpeedSynced && colorOverLifetimeEnabled && Mathf.Abs(Mathf.DeltaAngle(x, 90f)) <= 0.5f;
		}

		static object DescribeWaveTarget(Building wall, IntVec3 cell, int distance, int maxRadius, int hitPointsBefore, int hitPointsAfter)
		{
			return new
			{
				id = ZombieRuntimeActions.StableThingId(wall),
				cell = ZombieRuntimeActions.DescribeCell(cell),
				distance,
				expectedDamage = ZombieThumper.DebugSeismicWaveDamage(distance, maxRadius),
				wall.Destroyed,
				hitPointsBefore,
				hitPointsAfter,
				hitPointDelta = hitPointsBefore - hitPointsAfter
			};
		}

		[Tool("zombieland/chainsaw_equip_toggle", Description = "Equip a real fueled chainsaw, start it through its gizmo, tick it while equipped, then verify undrafting stops it.")]
		public static object ChainsawEquipToggle()
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

			var destroyedZombies = ZombieRuntimeActions.DestroyZombies(map);
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var actorCell, out var actorSpawnError) == false)
				return actorSpawnError;

			var actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(actor, actorCell, map, WipeMode.Vanish);
			DisablePawnWork(actor);
			actor.equipment?.DestroyAllEquipment(DestroyMode.Vanish);
			var chainsawCell = actorCell + IntVec3.East;
			if (chainsawCell.InBounds(map) == false || chainsawCell.Standable(map) == false)
				chainsawCell = actorCell;

			var chainsaw = ThingMaker.MakeThing(CustomDefs.Chainsaw) as Chainsaw;
			if (chainsaw == null)
			{
				return new
				{
					success = false,
					error = "Could not create Chainsaw."
				};
			}

			GenSpawn.Spawn(chainsaw, chainsawCell, map, WipeMode.Vanish);
			var refuelable = chainsaw.TryGetComp<CompRefuelable>();
			var breakable = chainsaw.TryGetComp<CompBreakable>();
			if (refuelable == null || breakable == null)
			{
				return new
				{
					success = false,
					chainsaw = ZombieRuntimeActions.StableThingId(chainsaw),
					error = "The spawned chainsaw did not have refuelable and breakable comps."
				};
			}

			var fuel = ThingMaker.MakeThing(ThingDefOf.Chemfuel);
			fuel.stackCount = Math.Min(ThingDefOf.Chemfuel.stackLimit, refuelable.GetFuelCountToFullyRefuel());
			GenSpawn.Spawn(fuel, chainsawCell + IntVec3.South, map, WipeMode.Vanish);
			refuelable.Refuel(new List<Thing> { fuel });
			var refuelableStats = VerifyRefuelableSpecialDisplayStats(refuelable.Props);
			var refuelableGizmoGuard = VerifyRefuelableFuelStatusGizmoGuard(refuelable);
			var fuelBeforeEquip = refuelable.Fuel;
			chainsaw.DeSpawn();
			actor.equipment.AddEquipment(chainsaw);
			actor.jobs?.EndCurrentJob(JobCondition.InterruptForced);
			actor.drafter.Drafted = true;
			var equipped = ReferenceEquals(actor.equipment.Primary, chainsaw);
			var pawnSet = ReferenceEquals(chainsaw.pawn, actor);
			var movementAngle = VerifyChainsawMovementAngle(map, actor, chainsaw);
			var gizmos = chainsaw.GetGizmos().ToArray();
			var toggle = gizmos.OfType<Command_Action>().FirstOrDefault(command => command.disabled == false);
			var toggleAvailable = toggle != null;
			toggle?.action();
			var runningAfterToggle = chainsaw.running;
			var fuelAfterToggle = refuelable.Fuel;
			var samples = new List<object>();

			for (var tick = 1; tick <= 20; tick++)
			{
				AdvanceGameTicks(1);
				if (tick == 1 || tick == 20 || refuelable.Fuel < fuelAfterToggle)
				{
					samples.Add(new
					{
						tick,
						chainsaw.running,
						chainsaw.swinging,
						chainsaw.inactiveCounter,
						chainsaw.stalledCounter,
						fuel = refuelable.Fuel
					});
				}
			}

			var fuelAfterTicks = refuelable.Fuel;
			actor.drafter.Drafted = false;
			var runningAfterUndraft = chainsaw.running;

			return new
			{
				success = equipped
					&& pawnSet
					&& ObjectSuccess(movementAngle)
					&& ObjectSuccess(refuelableStats)
					&& ObjectSuccess(refuelableGizmoGuard)
					&& toggleAvailable
					&& runningAfterToggle
					&& fuelAfterTicks < fuelAfterToggle
					&& runningAfterUndraft == false
					&& breakable.broken == false,
				actor = DescribePawn(actor),
				chainsaw = new
				{
					id = ZombieRuntimeActions.StableThingId(chainsaw),
					thingId = chainsaw.ThingID,
					spawned = chainsaw.Spawned,
					equipped,
					pawnSet,
					hitPoints = chainsaw.HitPoints,
					breakable.broken,
					chainsaw.running,
					chainsaw.swinging,
					chainsaw.inactiveCounter,
					chainsaw.stalledCounter,
					description = chainsaw.DescriptionDetailed
				},
				actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
				chainsawCell = ZombieRuntimeActions.DescribeCell(chainsawCell),
				movementAngle,
				refuelableStats,
				refuelableGizmoGuard,
				gizmoCount = gizmos.Length,
				toggleAvailable,
				runningAfterToggle,
				runningAfterUndraft,
				fuelBeforeEquip,
				fuelAfterToggle,
				fuelAfterTicks,
				fuelDeltaWhileRunning = fuelAfterToggle - fuelAfterTicks,
				hasFuelAfter = refuelable.HasFuel,
				destroyedZombies,
				samples
			};
		}

		static object VerifyRefuelableFuelStatusGizmoGuard(CompRefuelable refuelable)
		{
			var target = typeof(Gizmo_SetFuelLevel).GetMethod(
				nameof(Gizmo_SetFuelLevel.GizmoOnGUI),
				BindingFlags.Instance | BindingFlags.Public);
			var patchInfo = target == null ? null : HarmonyLib.Harmony.GetPatchInfo(target);
			var prefix = patchInfo?.Prefixes
				.Select(patch => patch.PatchMethod)
				.FirstOrDefault(method => method.DeclaringType?.Name?.Contains("Gizmo_RefuelableFuelStatus_GizmoOnGUI_Patch") == true);
			if (target == null || prefix == null)
			{
				return new
				{
					success = false,
					targetFound = target != null,
					prefixFound = prefix != null,
					error = "Could not find the installed Gizmo_SetFuelLevel.GizmoOnGUI prefix."
				};
			}

			var nullPrefixResult = (bool)prefix.Invoke(null, new object[] { null });
			var normalPrefixResult = (bool)prefix.Invoke(null, new object[] { refuelable });
			var normalGizmo = new Gizmo_SetFuelLevel(refuelable);
			var nullGizmo = new Gizmo_SetFuelLevel(null);

			return new
			{
				success = nullPrefixResult == false
					&& normalPrefixResult
					&& refuelable != null
					&& normalGizmo.Visible
					&& nullGizmo != null,
				target = $"{target.DeclaringType?.FullName}.{target.Name}",
				prefix = $"{prefix.DeclaringType?.FullName}.{prefix.Name}",
				nullRefuelablePrefixResult = nullPrefixResult,
				normalRefuelablePrefixResult = normalPrefixResult,
				normalGizmo = new
				{
					normalGizmo.Visible,
					widthAt140 = normalGizmo.GetWidth(140f),
					refuelable.Fuel,
					refuelable.TargetFuelLevel,
					refuelable.HasFuel
				}
			};
		}

		static object VerifyRefuelableSpecialDisplayStats(CompProperties_Refuelable chainsawRefuelableProps)
		{
			if (chainsawRefuelableProps == null)
			{
				return new
				{
					success = false,
					error = "Chainsaw refuelable props were null."
				};
			}

			var chainsawEntries = chainsawRefuelableProps
				.SpecialDisplayStats(StatRequest.For(CustomDefs.Chainsaw, null))
				.ToList();
			var chainsawFuelEntry = chainsawEntries.FirstOrDefault(entry =>
				entry.category == StatCategoryDefOf.Weapon_Melee
				&& entry.ValueString == ((int)chainsawRefuelableProps.fuelCapacity).ToString());

			var vanillaRefuelableDef = DefDatabase<ThingDef>.AllDefs
				.Where(def => def != CustomDefs.Chainsaw)
				.Where(def => def.building?.IsTurret == true)
				.Select(def => new
				{
					def,
					props = def.GetCompProperties<CompProperties_Refuelable>()
				})
				.FirstOrDefault(item => item.props != null);
			if (vanillaRefuelableDef == null)
			{
				return new
				{
					success = false,
					chainsawEntries = chainsawEntries.Select(DescribeStatDrawEntry).ToArray(),
					error = "No vanilla refuelable turret definition was available for the pass-through stat probe."
				};
			}

			var vanillaEntries = vanillaRefuelableDef.props
				.SpecialDisplayStats(StatRequest.For(vanillaRefuelableDef.def, null))
				.ToList();
			var vanillaShotsEntry = vanillaEntries.FirstOrDefault(entry =>
				entry.category == StatCategoryDefOf.Building
				&& entry.ValueString == ((int)vanillaRefuelableDef.props.fuelCapacity).ToString());

			return new
			{
				success = chainsawEntries.Count == 1
					&& chainsawFuelEntry != null
					&& vanillaShotsEntry != null
					&& vanillaEntries.Any(entry => entry.category == StatCategoryDefOf.Weapon_Melee) == false,
				chainsaw = new
				{
					def = CustomDefs.Chainsaw.defName,
					fuelCapacity = chainsawRefuelableProps.fuelCapacity,
					fuelLabel = chainsawRefuelableProps.FuelLabel,
					entries = chainsawEntries.Select(DescribeStatDrawEntry).ToArray()
				},
				vanillaRefuelable = new
				{
					def = vanillaRefuelableDef.def.defName,
					fuelCapacity = vanillaRefuelableDef.props.fuelCapacity,
					fuelLabel = vanillaRefuelableDef.props.FuelLabel,
					entries = vanillaEntries.Select(DescribeStatDrawEntry).ToArray()
				}
			};
		}

		static object DescribeStatDrawEntry(StatDrawEntry entry)
		{
			return new
			{
				category = entry.category?.defName,
				stat = entry.stat?.defName,
				label = entry.LabelCap,
				value = entry.ValueString,
				priority = entry.DisplayPriorityWithinCategory
			};
		}

		static object VerifyChainsawMovementAngle(Map map, Pawn actor, Chainsaw chainsaw)
		{
			if (actor?.pather == null || chainsaw == null)
			{
				return new
				{
					success = false,
					error = "Chainsaw movement angle probe requires a spawned pawn with a path follower and chainsaw."
				};
			}

			var origin = actor.Position;
			var destination = GenRadial.RadialCellsAround(origin, 16f, false)
				.Where(cell => cell.InBounds(map))
				.Where(cell => cell.Standable(map))
				.Where(cell => cell.Fogged(map) == false)
				.Where(cell => cell.GetFirstPawn(map) == null)
				.Where(cell => cell.DistanceTo(origin) >= 8f)
				.Where(cell => actor.CanReach(cell, PathEndMode.OnCell, Danger.Deadly))
				.OrderByDescending(cell => cell.DistanceToSquared(origin))
				.FirstOrDefault();
			if (destination.IsValid == false)
			{
				return new
				{
					success = false,
					actor = DescribePawn(actor),
					error = "No reachable destination was found for the chainsaw movement angle probe."
				};
			}

			var wasSwinging = chainsaw.swinging;
			var angleBefore = chainsaw.angle;
			chainsaw.angle = 359f;
			actor.jobs?.EndCurrentJob(JobCondition.InterruptForced);
			actor.pather.StartPath(destination, PathEndMode.OnCell);

			var samples = new List<object>();
			object successSample = null;
			for (var tick = 1; tick <= 60; tick++)
			{
				AdvanceGameTicks(1);
				var nextCell = actor.pather.nextCell;
				var hasMoveDelta = nextCell.IsValid && nextCell != actor.Position;
				var expectedAngle = hasMoveDelta ? (nextCell - actor.Position).AngleFlat : float.NaN;
				var angleDelta = hasMoveDelta ? Mathf.Abs(Mathf.DeltaAngle(chainsaw.angle, expectedAngle)) : float.NaN;
				var angleMatches = hasMoveDelta && angleDelta < 0.01f;

				if (tick <= 3 || angleMatches || tick == 60)
				{
					var sample = new
					{
						tick,
						position = ZombieRuntimeActions.DescribeCell(actor.Position),
						nextCell = nextCell.IsValid ? ZombieRuntimeActions.DescribeCell(nextCell) : null,
						actor.pather.Moving,
						actor.pather.MovingNow,
						chainsaw.swinging,
						chainsaw.angle,
						expectedAngle,
						angleDelta,
						angleMatches
					};
					samples.Add(sample);
					if (angleMatches && successSample == null)
						successSample = sample;
				}

				if (angleMatches)
					break;
			}

			actor.pather.StopDead();

			return new
			{
				success = successSample != null && wasSwinging == false && chainsaw.swinging == false,
				actor = DescribePawn(actor),
				origin = ZombieRuntimeActions.DescribeCell(origin),
				destination = ZombieRuntimeActions.DescribeCell(destination),
				wasSwinging,
				stillNotSwinging = chainsaw.swinging == false,
				angleBefore,
				angleAfter = chainsaw.angle,
				successSample,
				samples
			};
		}

		[Tool("zombieland/chainsaw_slaughter_zombie", Description = "Run a fueled chainsaw against an adjacent live zombie and verify the chainsaw tick kills it through the custom slaughter path.")]
		public static object ChainsawSlaughterZombie()
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

			var destroyedZombies = ZombieRuntimeActions.DestroyZombies(map);
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var actorCell, out var actorSpawnError) == false)
				return actorSpawnError;

			var actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(actor, actorCell, map, WipeMode.Vanish);
			DisablePawnWork(actor);
			actor.equipment?.DestroyAllEquipment(DestroyMode.Vanish);

			var zombieCell = IntVec3.Invalid;
			var targetIndex = -1;
			var adjacent = GenAdj.AdjacentCellsAround;
			for (var i = 0; i < adjacent.Length; i++)
			{
				var candidate = actorCell + adjacent[i];
				if (candidate.InBounds(map) == false || candidate.Standable(map) == false || candidate.Fogged(map))
					continue;
				if (candidate.GetThingList(map).Any(thing => thing is Pawn))
					continue;
				zombieCell = candidate;
				targetIndex = i;
				break;
			}
			if (zombieCell.IsValid == false)
			{
				return new
				{
					success = false,
					actor = DescribePawn(actor),
					error = "No adjacent zombie target cell was available."
				};
			}

			var chainsaw = ThingMaker.MakeThing(CustomDefs.Chainsaw) as Chainsaw;
			if (chainsaw == null)
			{
				return new
				{
					success = false,
					error = "Could not create Chainsaw."
				};
			}

			GenSpawn.Spawn(chainsaw, actorCell, map, WipeMode.Vanish);
			var refuelable = chainsaw.TryGetComp<CompRefuelable>();
			var breakable = chainsaw.TryGetComp<CompBreakable>();
			if (refuelable == null || breakable == null)
			{
				return new
				{
					success = false,
					chainsaw = ZombieRuntimeActions.StableThingId(chainsaw),
					error = "The spawned chainsaw did not have refuelable and breakable comps."
				};
			}

			var fuel = ThingMaker.MakeThing(ThingDefOf.Chemfuel);
			fuel.stackCount = Math.Min(ThingDefOf.Chemfuel.stackLimit, refuelable.GetFuelCountToFullyRefuel());
			GenSpawn.Spawn(fuel, actorCell + IntVec3.South, map, WipeMode.Vanish);
			refuelable.Refuel(new List<Thing> { fuel });
			chainsaw.DeSpawn();
			actor.equipment.AddEquipment(chainsaw);
			actor.drafter.Drafted = true;

			var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
			if (zombie == null)
			{
				return new
				{
					success = false,
					actor = DescribePawn(actor),
					error = "ZombieGenerator.SpawnZombie returned no chainsaw target zombie."
				};
			}

			var meleeSuppression = VerifyMeleeAttackSuppression(map, actor, zombie, actorCell + new IntVec3(8, 0, 0));
			var rotationSuppression = VerifyChainsawRotationSuppression(actor, chainsaw);

			var tickManager = map.GetComponent<TickManager>();
			var victimHeadsBefore = tickManager?.victimHeads?.Count ?? 0;
			var hitPointsBefore = chainsaw.HitPoints;
			var fuelBefore = refuelable.Fuel;
			var toggle = chainsaw.GetGizmos().OfType<Command_Action>().FirstOrDefault(command => command.disabled == false);
			var floatMenuMeleeActions = VerifyFloatMenuMeleeActions(map, actor, zombie, actorCell + new IntVec3(-8, 0, 0), toggle);
			chainsaw.angle = targetIndex * 45f + 22.5f;
			var runningAfterToggle = chainsaw.running;
			var samples = new List<object>();
			var tickHit = -1;

			for (var tick = 1; tick <= 10; tick++)
			{
				AdvanceGameTicks(1);
				samples.Add(new
				{
					tick,
					zombieDead = zombie.Dead,
					zombieDestroyed = zombie.Destroyed,
					chainsaw.running,
					chainsaw.swinging,
					chainsaw.angle,
					chainsawHitPoints = chainsaw.HitPoints,
					fuel = refuelable.Fuel,
					victimHeads = tickManager?.victimHeads?.Count ?? 0
				});

				if (zombie.Dead)
				{
					tickHit = tick;
					break;
				}
			}

			var victimHeadsAfter = tickManager?.victimHeads?.Count ?? 0;
			var fuelAfter = refuelable.Fuel;
			var hitPointsAfter = chainsaw.HitPoints;

			return new
			{
				success = runningAfterToggle
					&& ObjectSuccess(meleeSuppression)
					&& ObjectSuccess(floatMenuMeleeActions)
					&& ObjectSuccess(rotationSuppression)
					&& tickHit > 0
					&& zombie.Dead
					&& victimHeadsAfter > victimHeadsBefore
					&& hitPointsAfter < hitPointsBefore
					&& fuelAfter < fuelBefore
					&& breakable.broken == false,
				destroyedZombies,
				actor = DescribePawn(actor),
				zombie = DescribeZombie(zombie),
				meleeSuppression,
				floatMenuMeleeActions,
				rotationSuppression,
				cells = new
				{
					actor = ZombieRuntimeActions.DescribeCell(actorCell),
					zombie = ZombieRuntimeActions.DescribeCell(zombieCell)
				},
				targetIndex,
				targetOffset = ZombieRuntimeActions.DescribeCell(adjacent[targetIndex]),
				runningAfterToggle,
				tickHit,
				hitPointsBefore,
				hitPointsAfter,
				hitPointDelta = hitPointsBefore - hitPointsAfter,
				fuelBefore,
				fuelAfter,
				fuelDelta = fuelBefore - fuelAfter,
				victimHeadsBefore,
				victimHeadsAfter,
				victimHeadDelta = victimHeadsAfter - victimHeadsBefore,
				chainsaw = new
				{
					id = ZombieRuntimeActions.StableThingId(chainsaw),
					equipped = ReferenceEquals(actor.equipment.Primary, chainsaw),
					pawnSet = ReferenceEquals(chainsaw.pawn, actor),
					breakable.broken,
					chainsaw.running,
					chainsaw.swinging,
					chainsaw.angle
				},
				samples
			};
		}

		static object VerifyChainsawRotationSuppression(Pawn chainsawPawn, Chainsaw chainsaw)
		{
			if (chainsawPawn?.rotationTracker == null || chainsaw == null)
			{
				return new
				{
					success = false,
					error = "Chainsaw rotation suppression probe requires a pawn rotation tracker and chainsaw."
				};
			}

			chainsawPawn.jobs?.EndCurrentJob(JobCondition.InterruptForced);
			chainsawPawn.pather?.StopDead();
			chainsawPawn.Rotation = Rot4.North;
			chainsaw.swinging = true;
			chainsawPawn.rotationTracker.UpdateRotation();
			var swingingRotation = chainsawPawn.Rotation;

			chainsawPawn.Rotation = Rot4.North;
			chainsaw.swinging = false;
			chainsawPawn.rotationTracker.UpdateRotation();
			var normalRotation = chainsawPawn.Rotation;

			return new
			{
				success = swingingRotation == Rot4.North
					&& normalRotation == Rot4.South
					&& chainsaw.swinging == false,
				chainsawPawn = DescribePawn(chainsawPawn),
				startRotation = Rot4.North.ToString(),
				swingingRotation = swingingRotation.ToString(),
				normalRotation = normalRotation.ToString(),
				restoredSwinging = chainsaw.swinging == false,
				drafted = chainsawPawn.Drafted
			};
		}

		static object VerifyFloatMenuMeleeActions(Map map, Pawn chainsawPawn, Zombie normalZombie, IntVec3 controlRoot, Command_Action toggle)
		{
			if (chainsawPawn == null || normalZombie == null)
			{
				return new
				{
					success = false,
					error = "Float-menu melee action probe requires a chainsaw pawn and normal zombie."
				};
			}
			if (toggle == null)
			{
				return new
				{
					success = false,
					error = "Chainsaw had no enabled toggle action for the running float-menu probe."
				};
			}

			if (TryFindClearSpawnCell(map, controlRoot, 20f, out var controlCell, out var controlError) == false)
				return controlError;

			var controlPawn = GenerateAreaWorkflowPawn(Faction.OfPlayer, true);
			GenSpawn.Spawn(controlPawn, controlCell, map, Rot4.South);
			DisablePawnWork(controlPawn);
			controlPawn.equipment?.DestroyAllEquipment(DestroyMode.Vanish);
			controlPawn.drafter.Drafted = true;

			var stoppedRunningBefore = chainsawPawn.equipment?.Primary is Chainsaw { running: true };
			var stoppedAction = FloatMenuUtility.GetMeleeAttackAction(chainsawPawn, new LocalTargetInfo(normalZombie), out var stoppedFailStr);
			var ordinaryAction = FloatMenuUtility.GetMeleeAttackAction(controlPawn, new LocalTargetInfo(normalZombie), out var ordinaryFailStr);
			toggle.action();
			var runningAfterToggle = chainsawPawn.equipment?.Primary is Chainsaw { running: true };
			var runningAction = FloatMenuUtility.GetMeleeAttackAction(chainsawPawn, new LocalTargetInfo(normalZombie), out var runningFailStr);
			var controlDescription = DescribePawn(controlPawn);
			var controlViolentDisabled = controlPawn.WorkTagIsDisabled(WorkTags.Violent);
			controlPawn.Destroy(DestroyMode.Vanish);

			return new
			{
				success = stoppedAction != null
					&& ordinaryAction != null
					&& runningAfterToggle
					&& runningAction == null,
				chainsawPawn = DescribePawn(chainsawPawn),
				controlPawn = controlDescription,
				target = DescribeZombie(normalZombie),
				stoppedChainsaw = new
				{
					actionAvailable = stoppedAction != null,
					failStr = stoppedFailStr?.ToString(),
					running = stoppedRunningBefore
				},
				ordinaryPawn = new
				{
					actionAvailable = ordinaryAction != null,
					failStr = ordinaryFailStr?.ToString(),
					violentDisabled = controlViolentDisabled
				},
				runningChainsaw = new
				{
					actionAvailable = runningAction != null,
					failStr = runningFailStr?.ToString(),
					runningAfterToggle
				}
			};
		}

		static object VerifyMeleeAttackSuppression(Map map, Pawn chainsawPawn, Zombie normalZombie, IntVec3 root)
		{
			if (chainsawPawn?.meleeVerbs == null || normalZombie == null)
			{
				return new
				{
					success = false,
					error = "Chainsaw melee suppression probe requires a chainsaw pawn and normal zombie."
				};
			}

			var injuryBefore = TotalInjurySeverity(normalZombie);
			var chainsawTryMeleeResult = chainsawPawn.meleeVerbs.TryMeleeAttack(normalZombie);
			var injuryAfter = TotalInjurySeverity(normalZombie);

			if (TryFindClearSpawnCell(map, root, 20f, out var spitterCell, out var spitterError) == false)
				return spitterError;
			if (TryFindClearSpawnCell(map, spitterCell + new IntVec3(4, 0, 0), 20f, out var symbiantCell, out var symbiantError) == false)
				return symbiantError;

			var zombieFaction = Find.FactionManager.FirstFactionOfDef(ZombieDefOf.Zombies);
			var spitter = PawnGenerator.GeneratePawn(ZombieDefOf.ZombieSpitter, zombieFaction) as ZombieSpitter;
			var symbiant = PawnGenerator.GeneratePawn(ZombieDefOf.ZombieSymbiant, zombieFaction) as ZombieSymbiant;
			if (spitter == null || symbiant == null)
			{
				return new
				{
					success = false,
					spitterGenerated = spitter != null,
					symbiantGenerated = symbiant != null,
					error = "Could not generate both special zombie melee-suppression pawns."
				};
			}

			GenSpawn.Spawn(spitter, spitterCell, map, Rot4.South, WipeMode.Vanish, false);
			GenSpawn.Spawn(symbiant, symbiantCell, map, Rot4.South, WipeMode.Vanish, false);
			if (spitter.meleeVerbs == null || symbiant.meleeVerbs == null)
			{
				var spitterDescription = DescribePawn(spitter);
				var symbiantDescription = DescribePawn(symbiant);
				var spitterHasMeleeVerbs = spitter.meleeVerbs != null;
				var symbiantHasMeleeVerbs = symbiant.meleeVerbs != null;
				spitter.Destroy(DestroyMode.Vanish);
				symbiant.Destroy(DestroyMode.Vanish);
				return new
				{
					success = false,
					spitter = spitterDescription,
					symbiant = symbiantDescription,
					spitterHasMeleeVerbs,
					symbiantHasMeleeVerbs,
					error = "Generated special zombies did not both have melee verb trackers."
				};
			}

			var spitterTryMeleeResult = spitter.meleeVerbs.TryMeleeAttack(chainsawPawn);
			var symbiantTryMeleeResult = symbiant.meleeVerbs.TryMeleeAttack(chainsawPawn);
			var spitterEvidence = DescribePawn(spitter);
			var symbiantEvidence = DescribePawn(symbiant);
			spitter.Destroy(DestroyMode.Vanish);
			symbiant.Destroy(DestroyMode.Vanish);

			return new
			{
				success = chainsawTryMeleeResult == false
					&& injuryAfter == injuryBefore
					&& normalZombie.Dead == false
					&& normalZombie.Destroyed == false
					&& spitterTryMeleeResult == false
					&& symbiantTryMeleeResult == false,
				chainsawPawn = DescribePawn(chainsawPawn),
				normalZombie = DescribeZombie(normalZombie),
				chainsawTryMeleeResult,
				normalZombieInjuryBefore = injuryBefore,
				normalZombieInjuryAfter = injuryAfter,
				spitter = spitterEvidence,
				symbiant = symbiantEvidence,
				spitterTryMeleeResult,
				symbiantTryMeleeResult
			};
		}

		[Tool("zombieland/chainsaw_hits_building_contract", Description = "Verify a running chainsaw aimed at an adjacent building damages the building, breaks, and drops.")]
		public static object ChainsawHitsBuildingContract()
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

			var destroyedZombies = ZombieRuntimeActions.DestroyZombies(map);
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var actorCell, out var actorSpawnError) == false)
				return actorSpawnError;

			var adjacent = GenAdj.AdjacentCellsAround;
			var buildingCell = IntVec3.Invalid;
			var buildingIndex = -1;
			var zombieCell = IntVec3.Invalid;
			for (var i = 0; i < adjacent.Length; i++)
			{
				var candidate = actorCell + adjacent[i];
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
					actorCell = ZombieRuntimeActions.DescribeCell(actorCell),
					buildingCell = buildingCell.IsValid ? ZombieRuntimeActions.DescribeCell(buildingCell) : null,
					zombieCell = zombieCell.IsValid ? ZombieRuntimeActions.DescribeCell(zombieCell) : null,
					error = "No adjacent building/zombie fixture cells were available for chainsaw building impact."
				};
			}

			var actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(actor, actorCell, map, WipeMode.Vanish);
			DisablePawnWork(actor);
			actor.equipment?.DestroyAllEquipment(DestroyMode.Vanish);

			var chainsaw = ThingMaker.MakeThing(CustomDefs.Chainsaw) as Chainsaw;
			if (chainsaw == null)
			{
				return new
				{
					success = false,
					error = "Could not create Chainsaw."
				};
			}

			GenSpawn.Spawn(chainsaw, actorCell, map, WipeMode.Vanish);
			var refuelable = chainsaw.TryGetComp<CompRefuelable>();
			var breakable = chainsaw.TryGetComp<CompBreakable>();
			if (refuelable == null || breakable == null)
			{
				return new
				{
					success = false,
					chainsaw = ZombieRuntimeActions.StableThingId(chainsaw),
					error = "The spawned chainsaw did not have refuelable and breakable comps."
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
					actor = DescribePawn(actor),
					error = "ZombieGenerator.SpawnZombie returned no chainsaw work-branch zombie."
				};
			}

			var fuel = ThingMaker.MakeThing(ThingDefOf.Chemfuel);
			fuel.stackCount = Math.Min(ThingDefOf.Chemfuel.stackLimit, refuelable.GetFuelCountToFullyRefuel());
			GenSpawn.Spawn(fuel, actorCell + IntVec3.South, map, WipeMode.Vanish);
			refuelable.Refuel(new List<Thing> { fuel });
			chainsaw.DeSpawn();
			actor.equipment.AddEquipment(chainsaw);
			actor.drafter.Drafted = true;

			var wallHitPointsBefore = wall.HitPoints;
			var chainsawHitPointsBefore = chainsaw.HitPoints;
			var fuelBefore = refuelable.Fuel;
			var toggle = chainsaw.GetGizmos().OfType<Command_Action>().FirstOrDefault(command => command.disabled == false);
			toggle?.action();
			chainsaw.angle = buildingIndex * 45f + 22.5f;
			var runningAfterToggle = chainsaw.running;
			AdvanceGameTicks(1);

			var wallHitPointsAfter = wall.Destroyed ? 0 : wall.HitPoints;
			var chainsawHitPointsAfter = chainsaw.Destroyed ? 0 : chainsaw.HitPoints;
			var fuelAfter = refuelable.Fuel;
			var stillEquipped = ReferenceEquals(actor.equipment?.Primary, chainsaw);
			var trackedAsBroken = map.GetComponent<BrokenManager>()?.brokenThings?.Contains(chainsaw) ?? false;

			return new
			{
				success = runningAfterToggle
					&& wallHitPointsAfter < wallHitPointsBefore
					&& stillEquipped == false
					&& chainsaw.Spawned
					&& breakable.broken
					&& trackedAsBroken
					&& chainsawHitPointsAfter < chainsawHitPointsBefore
					&& fuelAfter < fuelBefore
					&& zombie.Dead == false,
				destroyedZombies,
				actor = DescribePawn(actor),
				zombie = DescribeZombie(zombie),
				cells = new
				{
					actor = ZombieRuntimeActions.DescribeCell(actorCell),
					building = ZombieRuntimeActions.DescribeCell(buildingCell),
					zombie = ZombieRuntimeActions.DescribeCell(zombieCell)
				},
				buildingIndex,
				buildingOffset = ZombieRuntimeActions.DescribeCell(adjacent[buildingIndex]),
				runningAfterToggle,
				stillEquipped,
				chainsawSpawned = chainsaw.Spawned,
				chainsawPosition = chainsaw.Spawned ? ZombieRuntimeActions.DescribeCell(chainsaw.Position) : null,
				chainsawHitPointsBefore,
				chainsawHitPointsAfter,
				chainsawHitPointDelta = chainsawHitPointsBefore - chainsawHitPointsAfter,
				chainsawBroken = breakable.broken,
				trackedAsBroken,
				fuelBefore,
				fuelAfter,
				fuelDelta = fuelBefore - fuelAfter,
				wall = new
				{
					id = ZombieRuntimeActions.StableThingId(wall),
					wall.Destroyed,
					hitPointsBefore = wallHitPointsBefore,
					hitPointsAfter = wallHitPointsAfter,
					hitPointDelta = wallHitPointsBefore - wallHitPointsAfter
				}
			};
		}

		[Tool("zombieland/chainsaw_crowd_pressure_drop_contract", Description = "Surround a drafted colonist from too many hostile angles and verify the equipped running chainsaw drops through its pressure branch.")]
		public static object ChainsawCrowdPressureDropContract()
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

			var destroyedZombies = ZombieRuntimeActions.DestroyZombies(map);
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			var adjacent = GenAdj.AdjacentCellsAround;
			var pressureIndices = new[] { 0, 2, 4, 6 };
			var actorCell = IntVec3.Invalid;
			object actorSpawnError = null;
			if (TryFindClearSpawnCell(map, root, 16f, out var firstCandidate, out actorSpawnError) == false)
				return actorSpawnError;
			foreach (var candidate in GenRadial.RadialCellsAround(firstCandidate, 16f, true))
			{
				if (candidate.InBounds(map) == false || candidate.Standable(map) == false || candidate.Fogged(map))
					continue;
				if (candidate.GetThingList(map).Any(thing => thing is Pawn))
					continue;
				if (pressureIndices.All(index =>
				{
					var cell = candidate + adjacent[index];
					return cell.InBounds(map)
						&& cell.Standable(map)
						&& cell.Fogged(map) == false
						&& cell.GetFirstThing<Mineable>(map) == null
						&& cell.GetThingList(map).Any(thing => thing is Pawn) == false;
				}))
				{
					actorCell = candidate;
					break;
				}
			}
			if (actorCell.IsValid == false)
			{
				return new
				{
					success = false,
					error = "Could not find a clear chainsaw pressure fixture with four alternate adjacent zombie cells."
				};
			}

			var actor = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
			GenSpawn.Spawn(actor, actorCell, map, WipeMode.Vanish);
			DisablePawnWork(actor);
			actor.equipment?.DestroyAllEquipment(DestroyMode.Vanish);

			var chainsaw = ThingMaker.MakeThing(CustomDefs.Chainsaw) as Chainsaw;
			if (chainsaw == null)
			{
				return new
				{
					success = false,
					error = "Could not create Chainsaw."
				};
			}

			GenSpawn.Spawn(chainsaw, actorCell, map, WipeMode.Vanish);
			var refuelable = chainsaw.TryGetComp<CompRefuelable>();
			var breakable = chainsaw.TryGetComp<CompBreakable>();
			if (refuelable == null || breakable == null)
			{
				return new
				{
					success = false,
					chainsaw = ZombieRuntimeActions.StableThingId(chainsaw),
					error = "The spawned chainsaw did not have refuelable and breakable comps."
				};
			}

			var fuel = ThingMaker.MakeThing(ThingDefOf.Chemfuel);
			fuel.stackCount = Math.Min(ThingDefOf.Chemfuel.stackLimit, refuelable.GetFuelCountToFullyRefuel());
			GenSpawn.Spawn(fuel, actorCell + IntVec3.South, map, WipeMode.Vanish);
			refuelable.Refuel(new List<Thing> { fuel });
			chainsaw.DeSpawn();
			actor.equipment.AddEquipment(chainsaw);
			actor.jobs?.EndCurrentJob(JobCondition.InterruptForced);
			actor.drafter.Drafted = true;

			var zombies = new List<Zombie>();
			foreach (var index in pressureIndices)
			{
				var zombie = ZombieRuntimeActions.SpawnZombie(actorCell + adjacent[index], map, ZombieType.Normal, true);
				if (zombie != null)
					zombies.Add(zombie);
			}

			var fuelBefore = refuelable.Fuel;
			var hitPointsBefore = chainsaw.HitPoints;
			var toggle = chainsaw.GetGizmos().OfType<Command_Action>().FirstOrDefault(command => command.disabled == false);
			toggle?.action();
			var runningAfterToggle = chainsaw.running;
			var equippedBeforeTick = ReferenceEquals(actor.equipment?.Primary, chainsaw);
			AdvanceGameTicks(1);

			var fuelAfter = refuelable.Fuel;
			var hitPointsAfter = chainsaw.Destroyed ? 0 : chainsaw.HitPoints;
			var equippedAfterTick = ReferenceEquals(actor.equipment?.Primary, chainsaw);
			var chainsawSpawned = chainsaw.Spawned;
			var pawnCleared = chainsaw.pawn == null;
			var zombieStates = zombies.Select(zombie => new
			{
				zombie = DescribeZombie(zombie),
				alive = zombie.Dead == false && zombie.Destroyed == false,
				hostileToActor = zombie.HostileTo(actor)
			}).ToArray();

			return new
			{
				success = zombies.Count == pressureIndices.Length
					&& runningAfterToggle
					&& equippedBeforeTick
					&& equippedAfterTick == false
					&& chainsawSpawned
					&& pawnCleared
					&& chainsaw.running == false
					&& chainsaw.swinging == false
					&& breakable.broken == false
					&& hitPointsAfter == hitPointsBefore
					&& fuelAfter < fuelBefore
					&& zombieStates.All(state => state.alive && state.hostileToActor),
				destroyedZombies,
				actor = DescribePawn(actor),
				cells = new
				{
					actor = ZombieRuntimeActions.DescribeCell(actorCell),
					zombies = pressureIndices
						.Select(index => new
						{
							index,
							offset = ZombieRuntimeActions.DescribeCell(adjacent[index]),
							cell = ZombieRuntimeActions.DescribeCell(actorCell + adjacent[index])
						})
						.ToArray()
				},
				pressureIndices,
				runningAfterToggle,
				equippedBeforeTick,
				equippedAfterTick,
				chainsaw = new
				{
					id = ZombieRuntimeActions.StableThingId(chainsaw),
					spawned = chainsawSpawned,
					cell = chainsawSpawned ? ZombieRuntimeActions.DescribeCell(chainsaw.Position) : null,
					pawnCleared,
					breakable.broken,
					chainsaw.running,
					chainsaw.swinging,
					hitPointsBefore,
					hitPointsAfter,
					hitPointDelta = hitPointsBefore - hitPointsAfter
				},
				fuelBefore,
				fuelAfter,
				fuelDelta = fuelBefore - fuelAfter,
				zombieStates
			};
		}

	}
}
