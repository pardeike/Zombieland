using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Reflection;
using Verse;

namespace ZombieLand
{
	[HarmonyPatch]
	static class Skyfaller_SpawnThings_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static MethodBase TargetMethod()
		{
			var method = AccessTools.FirstMethod(typeof(Skyfaller), method =>
			{
				if (method.Name.StartsWith("<SpawnThings>") == false || method.ReturnType != typeof(void))
					return false;
				var parameters = method.GetParameters();
				return parameters.Length == 2
					&& parameters[0].ParameterType == typeof(Thing)
					&& parameters[1].ParameterType == typeof(int);
			});
			if (method == null)
				Patches.Error("Cannot find RimWorld.Skyfaller SpawnThings placement callback");
			return method;
		}

		static void Postfix(Thing thing)
		{
			if (thing == null)
				return;

			if (thing is Mineable mineable)
			{
				mineable.AddContamination(ZombieSettings.Values.contamination.meteoriteAdd);
				return;
			}

			if (thing.def == ThingDefOf.ShipChunk)
			{
				thing.AddContamination(ZombieSettings.Values.contamination.meteoriteAdd);
				return;
			}
		}
	}

	[HarmonyPatch(typeof(ThingSetMaker), nameof(ThingSetMaker.Generate))]
	[HarmonyPatch(new[] { typeof(ThingSetMakerParams) })]
	static class ThingSetMaker_Generate_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Postfix(List<Thing> __result)
		{
			if (Tools.IsPlaying() == false || __result == null)
				return;
			foreach (var thing in __result)
			{
				if (thing is Mineable)
					continue;
				if (Rand.Chance(ZombieSettings.Values.contamination.randomThingCreateChance))
				{
					var amount = Tools.MoveableWeight(Rand.Value, 1 - ZombieSettings.Values.contamination.randomThingDensityDistribution);
					thing.SetContamination(amount);
				}
			}
		}
	}

	[HarmonyPatch(typeof(TradeDeal), nameof(TradeDeal.AddAllTradeables))]
	static class TradeDeal_AddAllTradeables_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Postfix(TradeDeal __instance)
		{
			var manager = ContaminationManager.Instance;
			var things = new List<Thing>();
			foreach (var tradeable in __instance.AllTradeables)
			{
				if (tradeable.HasAnyThing == false)
					continue;
				foreach (var thing in tradeable.thingsTrader)
				{
					if (thing is Mineable)
						continue;
					if (manager.Get(thing) != 0)
						return;
					things.Add(thing);
				}
			}
			foreach (var thing in things)
				if (Rand.Chance(ZombieSettings.Values.contamination.randomThingCreateChance))
				{
					var amount = Tools.MoveableWeight(Rand.Value, 1 - ZombieSettings.Values.contamination.randomThingDensityDistribution);
					thing.SetContamination(amount);
				}
		}
	}

	[HarmonyPatch(typeof(MechClusterUtility), nameof(MechClusterUtility.SpawnCluster))]
	static class MechClusterUtility_SpawnCluster_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Postfix(List<Thing> __result)
		{
			if (__result == null || __result.Count == 0)
				return;
			if (Rand.Chance(ZombieSettings.Values.contamination.mechClusterChance) == false)
				return;
			var amount = Tools.MoveableWeight(Rand.Value, 1 - ZombieSettings.Values.contamination.mechClusterDensityDistribution);
			foreach (var thing in __result)
				thing.SetContamination(amount);
		}
	}
}
