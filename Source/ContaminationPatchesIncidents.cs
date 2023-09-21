﻿using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;

namespace ZombieLand
{
	[HarmonyPatch]
	static class Skyfaller_SpawnThings_TestPatch
	{
		static bool Prepare() => Constants.CONTAMINATION > 0;

		static MethodBase TargetMethod()
			=> AccessTools.FirstMethod(typeof(Skyfaller), m => m.Name.StartsWith("<SpawnThings"));

		static void Postfix(Thing thing)
		{
			if (thing == null)
				return;

			if (thing is Mineable mineable)
			{
				mineable.AddContamination(ZombieSettings.Values.contamination.meteoriteAdd, null/*() => Log.Warning($"Skyfaller produced {mineable} at {mineable.Position}")*/);
				return;
			}

			if (thing.def == ThingDefOf.ShipChunk)
			{
				thing.AddContamination(ZombieSettings.Values.contamination.meteoriteAdd, null/*() => Log.Warning($"Skyfaller produced {thing} at {thing.Position}")*/);
				return;
			}
		}
	}

	[HarmonyPatch(typeof(ThingSetMaker), nameof(ThingSetMaker.Generate))]
	[HarmonyPatch(new[] { typeof(ThingSetMakerParams) })]
	static class ThingSetMaker_Generate_TestPatch
	{
		static bool Prepare() => Constants.CONTAMINATION > 0;

		static void Postfix(List<Thing> __result)
		{
			if (Tools.IsPlaying())
				foreach (var thing in __result.Where(t => t is not Mineable))
					if (Rand.Chance(ZombieSettings.Values.contamination.randomThingCreateChance))
					{
						var amount = Tools.MoveableWeight(Rand.Value, ZombieSettings.Values.contamination.randomThingDensityDistribution);
						thing.AddContamination(amount, null/*() => Log.Warning($"Made {thing}")*/);
					}
		}
	}

	[HarmonyPatch(typeof(TradeDeal), nameof(TradeDeal.AddAllTradeables))]
	static class TradeDeal_AddAllTradeables_TestPatch
	{
		static bool Prepare() => Constants.CONTAMINATION > 0;

		static void Postfix(TradeDeal __instance)
		{
			var manager = ContaminationManager.Instance;
			var things = __instance.AllTradeables
				.Where(tradeable => tradeable.HasAnyThing)
				.SelectMany(tradeable => tradeable.thingsTrader)
				.Where(thing => thing is not Mineable)
				.ToArray();
			var uncontaminated = things.Where(thing => manager.Get(thing) == 0).ToArray();
			if (things.Length > uncontaminated.Length)
				return;
			foreach (var thing in uncontaminated)
				if (Rand.Chance(ZombieSettings.Values.contamination.randomThingCreateChance))
				{
					var amount = Tools.MoveableWeight(Rand.Value, ZombieSettings.Values.contamination.randomThingDensityDistribution);
					thing.AddContamination(amount, null/*() => Log.Warning($"New tradeable {thing}")*/);
				}
		}
	}

	[HarmonyPatch(typeof(MechClusterUtility), nameof(MechClusterUtility.SpawnCluster))]
	static class MechClusterUtility_SpawnCluster_TestPatch
	{
		static bool Prepare() => Constants.CONTAMINATION > 0;

		static void Postfix(List<Thing> __result)
		{
			if (Rand.Chance(ZombieSettings.Values.contamination.mechClusterChance) == false)
				return;
			var amount = Tools.MoveableWeight(Rand.Value, ZombieSettings.Values.contamination.mechClusterDensityDistribution);
			foreach (var thing in __result)
				thing.AddContamination(amount, null/*() => Log.Warning($"New mech cluster item {thing}")*/);
		}
	}
}