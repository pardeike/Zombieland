using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	[HarmonyPatch]
	static class Skyfaller_SpawnThings_TestPatch
	{
		static MethodBase TargetMethod()
			=> AccessTools.FirstMethod(typeof(Skyfaller), m => m.Name.StartsWith("<SpawnThings"));

		static void Postfix(Thing thing)
		{
			if (thing is Mineable mineable)
			{
				thing.AddContamination(ContaminationFactors.meteoriteAdd, () => Log.Warning($"Skyfaller produced {thing} at {thing.Position}"));
				return;
			}
			if (thing.def == ThingDefOf.ShipChunk)
			{
				thing.AddContamination(ContaminationFactors.meteoriteAdd, () => Log.Warning($"Skyfaller produced {thing} at {thing.Position}"));
				return;
			}
		}
	}

	[HarmonyPatch(typeof(ThingSetMaker), nameof(ThingSetMaker.Generate))]
	[HarmonyPatch(new[] { typeof(ThingSetMakerParams) })]
	static class ThingSetMaker_Generate_TestPatch
	{
		static void Postfix(List<Thing> __result)
		{
			if (Tools.IsPlaying())
				foreach (var thing in __result.Where(t => t is not Mineable))
					if (Rand.Chance(ContaminationFactors.randomThingCreateChance))
					{
						var amount = Tools.MoveableWeight(Rand.Value, ContaminationFactors.randomThingDensityDistribution);
						thing.AddContamination(amount, () => Log.Warning($"Made {thing}"));
					}
		}
	}

	[HarmonyPatch(typeof(TradeDeal), nameof(TradeDeal.AddAllTradeables))]
	static class TradeDeal_AddAllTradeables_TestPatch
	{
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
			{
				Log.Warning($"Trade deal {__instance} already contaminated: ({things.Where(t => t.GetContamination() > 0).Join(t => $"{t}:{t.GetContamination()}")})");
				return;
			}
			Log.Warning($"Contaminating trade deal {__instance} [{uncontaminated.Length} items]");
			foreach (var thing in uncontaminated)
				if (Rand.Chance(ContaminationFactors.randomThingCreateChance))
				{
					var amount = Tools.MoveableWeight(Rand.Value, ContaminationFactors.randomThingDensityDistribution);
					thing.AddContamination(amount, () => Log.Warning($"New tradeable {thing}"));
				}
		}
	}
}