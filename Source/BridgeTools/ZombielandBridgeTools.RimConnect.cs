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
		[Tool("zombieland/rimconnect_rage_kill_contract", Description = "Verify Zombieland's streamer/RimConnect rage and kill-all actions affect cached zombies.")]
		public static object RimConnectRageKillContract()
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
					error = "Current map has no Zombieland TickManager."
				};
			}

			var destroyedExisting = ZombieRuntimeActions.DestroyZombies(map);
			var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (TryFindClearSpawnCell(map, root, 16f, out var firstCell, out var firstError) == false)
				return firstError;
			var secondRoot = firstCell + new IntVec3(3, 0, 0);
			if (TryFindClearSpawnCell(map, secondRoot, 16f, out var secondCell, out var secondError) == false)
				return secondError;

			var zombies = new List<Zombie>();
			try
			{
				var first = ZombieRuntimeActions.SpawnZombie(firstCell, map, ZombieType.Normal, true);
				var second = ZombieRuntimeActions.SpawnZombie(secondCell, map, ZombieType.ToxicSplasher, true);
				zombies.AddRange(new[] { first, second }.Where(zombie => zombie != null));
				var cachedBefore = tickManager.allZombiesCached.Count;

				var rageResult = RimConnectSupport.AllZombiesRage("Bridge");
				var rageDeadline = GenTicks.TicksAbs;
				var allRaging = zombies.All(zombie => zombie.raging > rageDeadline);
				var rageMessageOk = rageResult.Item1?.Contains("Bridge made all zombies", StringComparison.OrdinalIgnoreCase) == true;

				var killResult = RimConnectSupport.KillAllZombies("Bridge");
				var allDeadOrGone = zombies.All(zombie => zombie.Destroyed || zombie.Dead);
				var remainingSpawnedZombies = CurrentZombies(map)
					.Where(pawn => zombies.Contains(pawn))
					.ToArray();
				var killMessageOk = killResult.Item1?.Contains("Bridge killed all zombies", StringComparison.OrdinalIgnoreCase) == true;

				return new
				{
					success = cachedBefore == zombies.Count
						&& allRaging
						&& rageMessageOk
						&& allDeadOrGone
						&& remainingSpawnedZombies.Length == 0
						&& killMessageOk,
					destroyedExisting,
					cachedBefore,
					rageMessage = rageResult.Item1,
					rageCell = ZombieRuntimeActions.DescribeCell(rageResult.Item2),
					allRaging,
					rageDeadline,
					killMessage = killResult.Item1,
					killCell = ZombieRuntimeActions.DescribeCell(killResult.Item2),
					allDeadOrGone,
					remainingSpawnedZombieCount = remainingSpawnedZombies.Length,
					zombies = zombies.Select(DescribeZombie).ToArray()
				};
			}
			finally
			{
				foreach (var zombie in zombies)
				{
					_ = tickManager.allZombiesCached?.Remove(zombie);
					_ = tickManager.hummingZombies?.Remove(zombie);
					_ = tickManager.tankZombies?.Remove(zombie);
					if (zombie.Corpse != null && zombie.Corpse.Destroyed == false)
						zombie.Corpse.Destroy(DestroyMode.Vanish);
					if (zombie.Destroyed == false)
						zombie.Destroy(DestroyMode.Vanish);
				}
			}
		}

		[Tool("zombieland/rimconnect_special_type_queue_contract", Description = "Verify RimConnect special zombie event helpers enqueue the requested explicit zombie type.")]
		public static object RimConnectSpecialTypeQueueContract()
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
			var originalActions = tickManager.rimConnectActions.ToArray();
			var oldMaximumZombies = ZombieSettings.Values.maximumNumberOfZombies;
			try
			{
				tickManager.rimConnectActions.Clear();
				ZombieSettings.Values.maximumNumberOfZombies = Math.Max(
					ZombieSettings.Values.maximumNumberOfZombies,
					tickManager.ZombieCount() + types.Length + 16
				);

				var results = types.Select(type =>
				{
					var beforeQueueCount = tickManager.rimConnectActions.Count;
					var (message, spot) = RimConnectSupport.SpawnZombies(1, "Bridge", type);
					var afterQueueCount = tickManager.rimConnectActions.Count;
					var action = afterQueueCount > beforeQueueCount
						? tickManager.rimConnectActions.Dequeue()
						: null;
					ZombieType? capturedType = null;
					var actionTarget = action?.Target;
					if (actionTarget != null)
					{
						var capturedTypeField = actionTarget
							.GetType()
							.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
							.FirstOrDefault(field => field.FieldType == typeof(ZombieType));
						if (capturedTypeField != null)
							capturedType = (ZombieType)capturedTypeField.GetValue(actionTarget);
					}
					var expectedMessageFragment = type.ToString().ToLower();
					var messageMatches = message != null && message.Contains(expectedMessageFragment);
					var spotValid = spot.IsValid && spot.InBounds(map);
					var queuedOne = afterQueueCount == beforeQueueCount + 1 && action != null;
					var typeMatches = capturedType == type;

					return new
					{
						type = type.ToString(),
						success = queuedOne && typeMatches && spotValid && messageMatches,
						message,
						expectedMessageFragment,
						messageMatches,
						spot = spotValid ? ZombieRuntimeActions.DescribeCell(spot) : null,
						spotValid,
						beforeQueueCount,
						afterQueueCount,
						queuedOne,
						capturedType = capturedType?.ToString(),
						typeMatches,
						actionMethod = action?.Method?.Name,
						actionTargetType = action?.Target?.GetType().FullName
					};
				}).ToArray();

				return new
				{
					success = results.All(result => result.success),
					originalQueueCount = originalActions.Length,
					results
				};
			}
			finally
			{
				ZombieSettings.Values.maximumNumberOfZombies = oldMaximumZombies;
				tickManager.rimConnectActions.Clear();
				foreach (var action in originalActions)
					tickManager.rimConnectActions.Enqueue(action);
			}
		}

		[Tool("zombieland/rimconnect_super_drop_contract", Description = "Verify the RimConnect super zombie drop action creates RimWorld drop-pod things without errors.")]
		public static object RimConnectSuperDropContract(
			[ToolParameter(Description = "Number of super zombies to put in the drop raid.", Required = false, DefaultValue = 2)] int amount = 2)
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

			amount = Math.Max(1, Math.Min(amount, 8));
			var destroyedExisting = ZombieRuntimeActions.DestroyZombies(map);
			var oldMaximumZombies = ZombieSettings.Values.maximumNumberOfZombies;
			var beforeIds = map.listerThings.AllThings
				.Select(ZombieRuntimeActions.StableThingId)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);
			try
			{
				ZombieSettings.Values.maximumNumberOfZombies = Math.Max(
					ZombieSettings.Values.maximumNumberOfZombies,
					tickManager.ZombieCount() + amount + 16
				);
				var (message, spot) = RimConnectSupport.SuperZombieDropRaid(amount, "Bridge");
				var afterThings = map.listerThings.AllThings
					.Where(thing => beforeIds.Contains(ZombieRuntimeActions.StableThingId(thing)) == false)
					.ToArray();
				var dropPodThings = afterThings
					.Where(thing => thing is Skyfaller || thing.def?.defName?.IndexOf("DropPod", StringComparison.OrdinalIgnoreCase) >= 0)
					.ToArray();
				var messageMatches = message?.Contains($"Bridge created an drop raid with {amount} super zombies", StringComparison.OrdinalIgnoreCase) == true;
				var spotValid = spot.IsValid && spot.InBounds(map);
				return new
				{
					success = messageMatches
						&& spotValid
						&& dropPodThings.Length > 0,
					amount,
					destroyedExisting,
					message,
					messageMatches,
					spot = spotValid ? ZombieRuntimeActions.DescribeCell(spot) : null,
					spotValid,
					newThingCount = afterThings.Length,
					dropPodThingCount = dropPodThings.Length,
					newThings = afterThings.Select(thing => new
					{
						id = ZombieRuntimeActions.StableThingId(thing),
						def = thing.def?.defName,
						type = thing.GetType().FullName,
						position = ZombieRuntimeActions.DescribeCell(thing.Position),
						spawned = thing.Spawned
					}).ToArray()
				};
			}
			finally
			{
				ZombieSettings.Values.maximumNumberOfZombies = oldMaximumZombies;
				var createdThings = map.listerThings.AllThings
					.Where(thing => beforeIds.Contains(ZombieRuntimeActions.StableThingId(thing)) == false)
					.ToArray();
				foreach (var thing in createdThings)
					if (thing.Destroyed == false)
						thing.Destroy(DestroyMode.Vanish);
				tickManager.allZombiesCached?.Clear();
			}
		}

	}
}
