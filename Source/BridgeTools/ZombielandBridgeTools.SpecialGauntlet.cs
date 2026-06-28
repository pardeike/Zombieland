using RimBridgeServer.Sdk;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	public sealed partial class ZombielandBridgeTools
	{
		const string SpecialGauntletPrefix = "ZL Gauntlet";

		[Tool("zombieland/special_gauntlet_state", Description = "Set up, run, or read a reusable S-Special-Gauntlet fixture with all special zombies in one current-map scenario.")]
		public static object SpecialGauntletState(
			[ToolParameter(Description = "Action to perform: read, setup, or effects.", Required = false, DefaultValue = "read")] string actionMode = "read",
			[ToolParameter(Description = "Origin x coordinate for the gauntlet. Use -1 with z -1 to start near map center.", Required = false, DefaultValue = -1)] int x = -1,
			[ToolParameter(Description = "Origin z coordinate for the gauntlet. Use -1 with x -1 to start near map center.", Required = false, DefaultValue = -1)] int z = -1,
			[ToolParameter(Description = "Destroy existing Zombieland pawns before setup.", Required = false, DefaultValue = true)] bool clearExisting = true,
			[ToolParameter(Description = "When actionMode is read, advance this many game ticks before reading state.", Required = false, DefaultValue = 0)] int advanceTicks = 0)
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

			var normalizedAction = (actionMode ?? "read").Trim().ToLowerInvariant();
			if (normalizedAction == "setup")
				return SetupSpecialGauntlet(map, origin, clearExisting);
			if (normalizedAction == "effects" || normalizedAction == "run")
				return RunSpecialGauntletEffects(map, origin);
			if (normalizedAction == "read")
				return ReadSpecialGauntlet(map, origin, advanceTicks);

			return new
			{
				success = false,
				actionMode,
				error = "Unsupported actionMode. Use read, setup, or effects."
			};
		}

		static object SetupSpecialGauntlet(Map map, IntVec3 origin, bool clearExisting)
		{
			var errors = new List<string>();
			var spawned = new List<Pawn>();
			var destroyed = clearExisting ? ZombieRuntimeActions.DestroyZombies(map) : 0;
			foreach (var entry in referenceLineup)
			{
				var zombie = SpawnLineupZombie(map, origin + new IntVec3(entry.dx, 0, entry.dz), entry.type, $"{SpecialGauntletPrefix} {entry.type}", true, spawned, errors);
				if (zombie != null)
					zombie.Rotation = Rot4.South;
			}

			var spitter = SpawnLineupSpitter(map, origin + new IntVec3(8, 0, 0), $"{SpecialGauntletPrefix} Spitter", spawned, errors);
			if (spitter != null)
			{
				spitter.aggressive = true;
				spitter.Rotation = Rot4.South;
			}

			var symbiant = SpawnLineupSymbiant(map, origin + new IntVec3(10, 0, 0), $"{SpecialGauntletPrefix} Symbiant", spawned, errors);
			if (symbiant != null)
				symbiant.Rotation = Rot4.South;

			var wounded = SpawnLineupZombie(map, origin + new IntVec3(4, 0, 6), ZombieType.Normal, $"{SpecialGauntletPrefix} Wounded", true, spawned, errors);
			if (wounded != null)
				wounded.TakeDamage(new DamageInfo(DamageDefOf.Cut, 6f));

			var baitA = SpawnLineupColonist(map, origin + new IntVec3(8, 0, 4), $"{SpecialGauntletPrefix} Bait A", spawned, errors);
			var baitB = SpawnLineupColonist(map, origin + new IntVec3(10, 0, 4), $"{SpecialGauntletPrefix} Bait B", spawned, errors);
			if (baitA != null)
				baitA.Rotation = Rot4.South;
			if (baitB != null)
				baitB.Rotation = Rot4.South;

			for (var dx = -2; dx <= 12; dx++)
				for (var dz = -2; dz <= 8; dz++)
				{
					var cell = origin + new IntVec3(dx, 0, dz);
					if (cell.InBounds(map) && cell.Fogged(map) == false)
						map.areaManager.Home[cell] = true;
				}

			map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();
			var read = ReadSpecialGauntlet(map, origin);
			return new
			{
				success = errors.Count == 0 && (bool)(read.GetType().GetProperty("success")?.GetValue(read) ?? false),
				origin = ZombieRuntimeActions.DescribeCell(origin),
				destroyed,
				errors = errors.ToArray(),
				spawnedCount = spawned.Count,
				spawned = spawned.Select(pawn => pawn is Zombie || pawn is ZombieSpitter || pawn is ZombieSymbiant ? DescribeZombie(pawn) : DescribePawn(pawn)).ToArray(),
				read
			};
		}

		static object RunSpecialGauntletEffects(Map map, IntVec3 origin)
		{
			var errors = new List<string>();
			var results = new List<object>();

			results.Add(RunGauntletBomber(map, errors));
			results.Add(RunGauntletToxicSplasher(map, errors));
			results.Add(RunGauntletDarkSlimer(map, errors));
			results.Add(RunGauntletHealer(map, errors));
			results.Add(RunGauntletElectrifier(map, errors));
			results.Add(RunGauntletTanky(map, errors));
			results.Add(RunGauntletMiner(map, errors));
			results.Add(RunGauntletAlbino(map, errors));
			results.Add(RunGauntletSpitter(map, errors));
			results.Add(RunGauntletSymbiant(map, errors));

			AdvanceGameTicks(5);
			var read = ReadSpecialGauntlet(map, origin);
			var successes = results
				.Where(result => result != null)
				.Select(result => (bool)(result.GetType().GetProperty("success")?.GetValue(result) ?? false))
				.ToArray();

			return new
			{
				success = errors.Count == 0 && successes.Length == results.Count && successes.All(value => value),
				origin = ZombieRuntimeActions.DescribeCell(origin),
				errors = errors.ToArray(),
				results = results.ToArray(),
				read
			};
		}

		static object ReadSpecialGauntlet(Map map, IntVec3 origin, int advanceTicks = 0)
		{
			var cappedAdvanceTicks = Math.Max(0, Math.Min(advanceTicks, 1200));
			string advanceError = null;
			if (cappedAdvanceTicks > 0)
			{
				try
				{
					AdvanceGameTicks(cappedAdvanceTicks);
				}
				catch (Exception ex)
				{
					advanceError = ex.ToString();
				}
			}

			var zombies = CurrentZombies(map)
				.Where(pawn => pawn.Name?.ToStringShort?.StartsWith(SpecialGauntletPrefix, StringComparison.OrdinalIgnoreCase) == true)
				.OrderBy(pawn => pawn.Name?.ToStringShort)
				.ToArray();
			var humans = map.mapPawns.AllPawnsSpawned
				.Where(pawn => pawn.Name?.ToStringShort?.StartsWith(SpecialGauntletPrefix, StringComparison.OrdinalIgnoreCase) == true)
				.Where(pawn => pawn is not Zombie && pawn is not ZombieSpitter && pawn is not ZombieSymbiant)
				.OrderBy(pawn => pawn.Name?.ToStringShort)
				.ToArray();
			var corpseCount = map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse)
				.OfType<Corpse>()
				.Count(corpse => corpse.InnerPawn?.Name?.ToStringShort?.StartsWith(SpecialGauntletPrefix, StringComparison.OrdinalIgnoreCase) == true);
			var radius = 18f;
			return new
			{
				success = zombies.Length > 0 && advanceError == null,
				origin = ZombieRuntimeActions.DescribeCell(origin),
				requestedAdvanceTicks = advanceTicks,
				advancedTicks = cappedAdvanceTicks,
				advanceError,
				tick = Find.TickManager.TicksGame,
				zombieCount = zombies.Length,
				humanCount = humans.Length,
				corpseCount,
				stickyGoo = CountThingsNear(map, origin, CustomDefs.StickyGoo, radius),
				tarSmoke = CountThingsNear(map, origin, CustomDefs.TarSmoke, radius),
				tarSlime = CountThingsNear(map, origin, CustomDefs.TarSlime, radius),
				zombieBalls = CountThingsNear(map, origin, CustomDefs.ZombieBall, radius),
				zombies = zombies.Select(DescribeZombie).ToArray(),
				humans = humans.Select(DescribePawn).ToArray()
			};
		}

		static Zombie FindGauntletZombie(Map map, string kind)
		{
			return CurrentZombies(map)
				.OfType<Zombie>()
				.FirstOrDefault(zombie => zombie.Name?.ToStringShort?.IndexOf($"{SpecialGauntletPrefix} {kind}", StringComparison.OrdinalIgnoreCase) >= 0);
		}

		static ZombieSpitter FindGauntletSpitter(Map map)
		{
			return CurrentZombies(map)
				.OfType<ZombieSpitter>()
				.FirstOrDefault(spitter => spitter.Name?.ToStringShort?.IndexOf($"{SpecialGauntletPrefix} Spitter", StringComparison.OrdinalIgnoreCase) >= 0);
		}

		static ZombieSymbiant FindGauntletSymbiant(Map map)
		{
			return CurrentZombies(map)
				.OfType<ZombieSymbiant>()
				.FirstOrDefault(symbiant => symbiant.Name?.ToStringShort?.IndexOf($"{SpecialGauntletPrefix} Symbiant", StringComparison.OrdinalIgnoreCase) >= 0);
		}

		static object RunGauntletBomber(Map map, List<string> errors)
		{
			var zombie = FindGauntletZombie(map, nameof(ZombieType.SuicideBomber));
			if (zombie == null)
				return FailGauntletCase("suicide-bomber", errors, "Missing gauntlet suicide bomber.");

			var tickManager = map.GetComponent<TickManager>();
			var before = DescribeZombie(zombie);
			var queuedBefore = tickManager?.explosions?.Count ?? 0;
			zombie.Kill(null);
			var queuedAfterKill = tickManager?.explosions?.Count ?? 0;
			tickManager?.ExecuteExplosions();
			var queuedAfterExecute = tickManager?.explosions?.Count ?? 0;
			var success = zombie.Dead && queuedAfterKill == queuedBefore + 1 && queuedAfterExecute == 0;
			return new
			{
				caseName = "suicide-bomber",
				success,
				before,
				after = DescribeZombie(zombie),
				queuedBefore,
				queuedAfterKill,
				queuedAfterExecute
			};
		}

		static object RunGauntletToxicSplasher(Map map, List<string> errors)
		{
			var zombie = FindGauntletZombie(map, nameof(ZombieType.ToxicSplasher));
			if (zombie == null)
				return FailGauntletCase("toxic-splasher", errors, "Missing gauntlet toxic splasher.");

			var pos = zombie.Position;
			var stickyBefore = CountThingsNear(map, pos, CustomDefs.StickyGoo, 8f);
			var before = DescribeZombie(zombie);
			zombie.Kill(null);
			var stickyAfter = CountThingsNear(map, pos, CustomDefs.StickyGoo, 8f);
			return new
			{
				caseName = "toxic-splasher",
				success = zombie.Dead && stickyAfter > stickyBefore,
				position = ZombieRuntimeActions.DescribeCell(pos),
				before,
				after = DescribeZombie(zombie),
				stickyBefore,
				stickyAfter,
				stickyDelta = stickyAfter - stickyBefore
			};
		}

		static object RunGauntletDarkSlimer(Map map, List<string> errors)
		{
			var zombie = FindGauntletZombie(map, nameof(ZombieType.DarkSlimer));
			if (zombie == null)
				return FailGauntletCase("dark-slimer", errors, "Missing gauntlet dark slimer.");

			var pos = zombie.Position;
			var smokeBefore = CountThingsNear(map, pos, CustomDefs.TarSmoke, 4f);
			var damage = zombie.TakeDamage(new DamageInfo(DamageDefOf.Bullet, 1f, 0f, -1f, null, null, null, DamageInfo.SourceCategory.ThingOrUnknown, null, true, true));
			AdvanceGameTicks(4);
			var smokeAfter = CountThingsNear(map, pos, CustomDefs.TarSmoke, 4f);
			return new
			{
				caseName = "dark-slimer",
				success = smokeAfter > smokeBefore && pos.GetGas(map)?.def == CustomDefs.TarSmoke,
				position = ZombieRuntimeActions.DescribeCell(pos),
				damageTotal = damage.totalDamageDealt,
				smokeBefore,
				smokeAfter,
				smokeDelta = smokeAfter - smokeBefore,
				gasAtPosition = pos.GetGas(map)?.def?.defName,
				zombie = DescribeZombie(zombie)
			};
		}

		static object RunGauntletHealer(Map map, List<string> errors)
		{
			var healer = FindGauntletZombie(map, nameof(ZombieType.Healer));
			var wounded = CurrentZombies(map)
				.OfType<Zombie>()
				.FirstOrDefault(zombie => zombie.Name?.ToStringShort?.IndexOf($"{SpecialGauntletPrefix} Wounded", StringComparison.OrdinalIgnoreCase) >= 0);
			if (healer == null || wounded == null)
				return FailGauntletCase("healer", errors, "Missing gauntlet healer or wounded target.");

			healer.healInfo.Clear();
			if (wounded.health.hediffSet.hediffs.Any() == false)
				wounded.health.AddHediff(HediffMaker.MakeHediff(HediffDefOf.BloodLoss, wounded));
			var hediffsBefore = wounded.health.hediffSet.hediffs.Count;
			var interval = Zombie.nthTickValues[(int)NthTick.Every12];
			var tickHit = -1;
			for (var tick = 1; tick <= interval + 1; tick++)
			{
				AdvanceGameTicks(1);
				if (wounded.health.hediffSet.hediffs.Count == 0 && healer.healInfo.Any(info => ReferenceEquals(info.pawn, wounded)))
				{
					tickHit = tick;
					break;
				}
			}

			return new
			{
				caseName = "healer",
				success = hediffsBefore > 0 && wounded.health.hediffSet.hediffs.Count == 0 && tickHit > 0,
				interval,
				tickHit,
				hediffsBefore,
				hediffsAfter = wounded.health.hediffSet.hediffs.Count,
				healerInfoCount = healer.healInfo.Count,
				healer = DescribeZombie(healer),
				wounded = DescribeZombie(wounded)
			};
		}

		static object RunGauntletElectrifier(Map map, List<string> errors)
		{
			var zombie = FindGauntletZombie(map, nameof(ZombieType.Electrifier));
			if (zombie == null)
				return FailGauntletCase("electrifier", errors, "Missing gauntlet electrifier.");

			zombie.electricDisabledUntil = 0;
			zombie.absorbAttack.Clear();
			var beforeAbsorb = zombie.absorbAttack.Count;
			var bullet = zombie.TakeDamage(new DamageInfo(DamageDefOf.Bullet, 4f, 0f, -1f, null, null, null, DamageInfo.SourceCategory.ThingOrUnknown, null, true, true));
			var afterBulletAbsorb = zombie.absorbAttack.Count;
			var emp = new DamageInfo(DamageDefOf.EMP, 5f, 0f, -1f, null, null, null, DamageInfo.SourceCategory.ThingOrUnknown, null, true, true);
			_ = zombie.TakeDamage(emp);
			var disabledAfterEmp = zombie.IsActiveElectric == false && zombie.electricDisabledUntil > Find.TickManager.TicksGame;
			return new
			{
				caseName = "electrifier",
				success = bullet.totalDamageDealt == 0f && afterBulletAbsorb > beforeAbsorb && disabledAfterEmp,
				bulletDamageTotal = bullet.totalDamageDealt,
				beforeAbsorb,
				afterBulletAbsorb,
				electricDisabledUntil = zombie.electricDisabledUntil,
				activeAfterEmp = zombie.IsActiveElectric,
				zombie = DescribeZombie(zombie)
			};
		}

		static object RunGauntletTanky(Map map, List<string> errors)
		{
			var zombie = FindGauntletZombie(map, nameof(ZombieType.TankyOperator));
			if (zombie == null)
				return FailGauntletCase("tanky", errors, "Missing gauntlet tanky operator.");
			if (TryFindAdjacentBuildingCell(zombie, out var wallCell) == false)
				return FailGauntletCase("tanky", errors, "No adjacent building cell was available for tanky.");

			var wallDef = ThingDefOf.Wall;
			var stuffDef = GenStuff.DefaultStuffFor(wallDef);
			var wall = ThingMaker.MakeThing(wallDef, stuffDef) as Building;
			GenSpawn.Spawn(wall, wallCell, map, WipeMode.Vanish);
			wall.SetFaction(Faction.OfPlayer);
			var hpBefore = wall.HitPoints;
			zombie.jobs.StartJob(JobMaker.MakeJob(JobDefOf.AttackStatic, wall), JobCondition.InterruptForced);
			AdvanceGameTicks(180);
			var hpAfter = wall.Destroyed ? 0 : wall.HitPoints;
			return new
			{
				caseName = "tanky",
				success = hpAfter < hpBefore || zombie.CurJobDef == JobDefOf.AttackStatic,
				wall = DescribeFixtureThing(wall),
				hpBefore,
				hpAfter,
				zombie = DescribeZombie(zombie)
			};
		}

		static object RunGauntletMiner(Map map, List<string> errors)
		{
			var zombie = FindGauntletZombie(map, nameof(ZombieType.Miner));
			if (zombie == null)
				return FailGauntletCase("miner", errors, "Missing gauntlet miner.");
			if (TryFindAdjacentBuildingCell(zombie, out var cell) == false)
				return FailGauntletCase("miner", errors, "No adjacent mineable cell was available for miner.");

			var mineableDef = DefDatabase<ThingDef>.GetNamedSilentFail("MineableGranite")
				?? DefDatabase<ThingDef>.AllDefs.FirstOrDefault(def => typeof(Mineable).IsAssignableFrom(def.thingClass));
			if (mineableDef == null)
				return FailGauntletCase("miner", errors, "No Mineable ThingDef was available.");

			var mineable = ThingMaker.MakeThing(mineableDef) as Mineable;
			GenSpawn.Spawn(mineable, cell, map, WipeMode.Vanish);
			var hpBefore = mineable.HitPoints;
			var mined = ZombieStateHandler.Mine(null, zombie, true);
			var hpAfter = mineable.Destroyed ? 0 : mineable.HitPoints;
			return new
			{
				caseName = "miner",
				success = mined && hpAfter < hpBefore,
				mineable = DescribeFixtureThing(mineable),
				hpBefore,
				hpAfter,
				miningCounter = zombie.miningCounter,
				zombie = DescribeZombie(zombie)
			};
		}

		static object RunGauntletAlbino(Map map, List<string> errors)
		{
			var zombie = FindGauntletZombie(map, nameof(ZombieType.Albino));
			if (zombie == null)
				return FailGauntletCase("albino", errors, "Missing gauntlet albino.");

			if (zombie.CurJobDef != CustomDefs.Sabotage)
				zombie.jobs.StartJob(JobMaker.MakeJob(CustomDefs.Sabotage), JobCondition.InterruptForced);
			AdvanceGameTicks(1);
			zombie.scream = 0;
			AdvanceGameTicks(45);
			return new
			{
				caseName = "albino",
				success = zombie.scream > 0 && zombie.CurJobDef == CustomDefs.Sabotage,
				scream = zombie.scream,
				zombie = DescribeZombie(zombie)
			};
		}

		static object RunGauntletSpitter(Map map, List<string> errors)
		{
			var spitter = FindGauntletSpitter(map);
			if (spitter == null)
				return FailGauntletCase("spitter", errors, "Missing gauntlet spitter.");

			var before = CountThingsNear(map, spitter.Position, CustomDefs.ZombieBall, 30f);
			var projectile = ForceSpitterShot(map, spitter, 11);
			var after = CountThingsNear(map, spitter.Position, CustomDefs.ZombieBall, 30f);
			return new
			{
				caseName = "spitter",
				success = projectile != null && after > before && spitter.remainingZombies == 0,
				before,
				after,
				projectile = DescribeFixtureThing(projectile),
				spitter = DescribeZombie(spitter)
			};
		}

		static object RunGauntletSymbiant(Map map, List<string> errors)
		{
			var symbiant = FindGauntletSymbiant(map);
			if (symbiant == null)
				return FailGauntletCase("symbiant", errors, "Missing gauntlet symbiant.");

			return new
			{
				caseName = "symbiant",
				success = symbiant.Spawned && symbiant.CurJobDef == CustomDefs.Symbiant,
				symbiant = DescribeZombie(symbiant)
			};
		}

		static object FailGauntletCase(string caseName, List<string> errors, string error)
		{
			errors.Add(error);
			return new
			{
				caseName,
				success = false,
				error
			};
		}
	}
}
