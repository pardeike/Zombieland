using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Verse;
using static HarmonyLib.Code;

namespace ZombieLand
{
	[HarmonyPatch(typeof(Pawn_FilthTracker), nameof(Pawn_FilthTracker.Notify_EnteredNewCell))]
	static class Pawn_FilthTracker_Notify_EnteredNewCell_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Prefix(Pawn_FilthTracker __instance) => Filth_MakeThing_Patch.filthSource = __instance.pawn;
		static void Postfix(Pawn_FilthTracker __instance)
		{
			var pawn = __instance.pawn;
			var pawnContamination = pawn.GetContamination(includeHoldings: true);
			var cellContamination = pawn.Map.GetContamination(pawn.Position);
			var delta = cellContamination * ZombieSettings.Values.contamination.cellFactor - pawnContamination;
			if (delta > 0)
				pawn.AddContamination(delta, null, ZombieSettings.Values.contamination.enterCellAdd);
			else
				ZombieSettings.Values.contamination.enterCellLoose.Equalize(pawn, pawn.Position);
			Filth_MakeThing_Patch.filthSource = null;
		}
	}

	[HarmonyPatch]
	static class JobDriver_CleanFilth_MakeNewToils_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static MethodBase TargetMethod()
		{
			var m_ThinFilth = SymbolExtensions.GetMethodInfo((Filth filth) => filth.ThinFilth());
			var type = AccessTools.FirstInner(typeof(JobDriver_CleanFilth), type => type.Name.Contains("DisplayClass"));
			if (type == null)
			{
				Patches.Error("Cannot find RimWorld.JobDriver_CleanFilth MakeNewToils display class");
				return null;
			}
			var method = AccessTools.FirstMethod(type, method => method.CallsMethod(m_ThinFilth));
			if (method == null)
				Patches.Error("Cannot find RimWorld.JobDriver_CleanFilth clean delegate");
			return method;
		}

		static void ThinFilth(Filth filth, JobDriver_CleanFilth jobDriver)
		{
			filth.TransferContamination(ZombieSettings.Values.contamination.filthTransfer, jobDriver.pawn);
			filth.ThinFilth();
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> instructions.ExtraArgumentsTranspiler(typeof(Filth), () => ThinFilth(default, default), default, 1, true);
	}

	[HarmonyPatch(typeof(CompSpawnerFilth), nameof(CompSpawnerFilth.TrySpawnFilth))]
	static class CompSpawnerFilth_TrySpawnFilth_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Prefix(CompSpawnerFilth __instance) => Filth_MakeThing_Patch.filthSource = __instance.parent;
		static void Postfix() => Filth_MakeThing_Patch.filthSource = null;
	}

	[HarmonyPatch(typeof(GenLeaving), nameof(GenLeaving.DoLeavingsFor))]
	[HarmonyPatch(new[] { typeof(Thing), typeof(Map), typeof(DestroyMode), typeof(CellRect), typeof(Predicate<IntVec3>), typeof(List<Thing>) })]
	static class GenLeaving_DoLeavingsFor_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Prefix(Thing diedThing, ref List<Thing> listOfLeavingsOut)
		{
			listOfLeavingsOut ??= new List<Thing>();
			Filth_MakeThing_Patch.filthSource = diedThing;
		}
		static void Postfix(Thing diedThing, Map map, List<Thing> listOfLeavingsOut)
		{
			Filth_MakeThing_Patch.filthSource = null;
			if (listOfLeavingsOut.Any())
			{
				var leavingsArray = listOfLeavingsOut.ToArray();
				diedThing.TransferContamination(ZombieSettings.Values.contamination.leavingsTransfer, map, leavingsArray);
			}
		}
	}

	[HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
	static class Thing_TakeDamage_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Prefix(Thing __instance) => Filth_MakeThing_Patch.filthSource = __instance;
		static void Postfix() => Filth_MakeThing_Patch.filthSource = null;
	}

	[HarmonyPatch(typeof(HediffComp_DissolveGearOnDeath), nameof(HediffComp_DissolveGearOnDeath.Notify_PawnKilled))]
	static class HediffComp_DissolveGearOnDeath_Notify_PawnKilled_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Prefix(HediffComp_DissolveGearOnDeath __instance) => Filth_MakeThing_Patch.filthSource = __instance.Pawn;
		static void Postfix() => Filth_MakeThing_Patch.filthSource = null;
	}

	[HarmonyPatch(typeof(Projectile_Liquid), nameof(Projectile_Liquid.DoImpact))]
	static class Projectile_Liquid_DoImpact_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Prefix(Thing hitThing) => Filth_MakeThing_Patch.filthSource = hitThing;
		static void Postfix() => Filth_MakeThing_Patch.filthSource = null;
	}

	[HarmonyPatch(typeof(GroundSpawner), nameof(GroundSpawner.Tick))]
	static class TunnelHiveSpawner_Tick_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Prefix(GroundSpawner __instance, out bool __state)
		{
			__state = __instance is TunnelHiveSpawner && __instance.Spawned;
			if (__state)
			{
				Filth_MakeThing_Patch.filthSource = __instance;
				Filth_MakeThing_Patch.filthCell = new TargetInfo(__instance.Position, __instance.Map);
			}
		}
		static void Postfix(bool __state)
		{
			if (__state)
			{
				Filth_MakeThing_Patch.filthSource = null;
				Filth_MakeThing_Patch.filthCell = null;
			}
		}
	}

	[HarmonyPatch(typeof(DamageWorker_Flame), nameof(DamageWorker_Flame.Apply))]
	static class DamageWorker_Flame_Apply_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Prefix(Thing victim) => Filth_MakeThing_Patch.SetFilthSource(victim, snapshotContamination: true);
		static void Postfix() => Filth_MakeThing_Patch.ClearFilthSource();
	}

	[HarmonyPatch(typeof(Verse.Explosion), nameof(Verse.Explosion.TrySpawnExplosionThing))]
	static class Verse_Explosion_TrySpawnExplosionThing_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static Thing MostContaminatedThing(IEnumerable<Thing> things)
		{
			Thing result = null;
			var resultContamination = 0f;
			foreach (var thing in things)
			{
				if (thing == null)
					continue;
				var contamination = thing.GetContamination(includeHoldings: true);
				if (result == null || contamination >= resultContamination)
				{
					result = thing;
					resultContamination = contamination;
				}
			}
			return result;
		}

		static void Prefix(Verse.Explosion __instance)
			=> Filth_MakeThing_Patch.filthSource = MostContaminatedThing(__instance.damagedThings);
		static void Postfix() => Filth_MakeThing_Patch.filthSource = null;
	}

	[HarmonyPatch(typeof(RoofCollapserImmediate), nameof(RoofCollapserImmediate.DropRoofInCellPhaseTwo))]
	static class RoofCollapserImmediate_DropRoofInCellPhaseTwo_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Prefix(IntVec3 c, Map map) => Filth_MakeThing_Patch.filthCell = new TargetInfo(c, map);
		static void Postfix() => Filth_MakeThing_Patch.filthCell = null;
	}

	[HarmonyPatch]
	static class JobDriver_Vomit_MakeNewToils_Patch
	{
		static readonly MethodInfo m_TryMakeFilth = SymbolExtensions.GetMethodInfo(() => FilthMaker.TryMakeFilth(IntVec3.Invalid, default, ThingDefOf.Filth_Vomit, "", 0, FilthSourceFlags.Any));

		static bool Prepare() => Constants.CONTAMINATION;

		static void Prefix(JobDriver_Vomit __instance) => Filth_MakeThing_Patch.filthSource = __instance.pawn;
		static void Postfix() => Filth_MakeThing_Patch.filthSource = null;

		static MethodBase TargetMethod()
		{
			var method = AccessTools.FirstMethod(typeof(JobDriver_Vomit), method => method.CallsMethod(m_TryMakeFilth));
			if (method == null)
				Patches.Error("Cannot find RimWorld.JobDriver_Vomit filth delegate");
			return method;
		}
	}

	[HarmonyPatch(typeof(PregnancyUtility), nameof(PregnancyUtility.SpawnBirthFilth))]
	static class PregnancyUtility_SpawnBirthFilth_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Prefix(Pawn mother) => Filth_MakeThing_Patch.filthSource = mother;
		static void Postfix() => Filth_MakeThing_Patch.filthSource = null;
	}

	[HarmonyPatch(typeof(Corpse), nameof(Corpse.ButcherProducts))]
	static class Corpse_ButcherProducts_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static IEnumerable<Thing> Postfix(IEnumerable<Thing> things, Corpse __instance)
		{
			Filth_MakeThing_Patch.filthSource = __instance;
			try
			{
				foreach (var thing in things)
				{
					thing.AddContamination(__instance.GetContamination(includeHoldings: true), __instance.mapIndexOrState, ZombieSettings.Values.contamination.filthEqualize);
					yield return thing;
				}
			}
			finally
			{
				Filth_MakeThing_Patch.filthSource = null;
			}
		}
	}

	[HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.DropBloodFilth))]
	static class Pawn_HealthTracker_DropBloodFilth_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Prefix(Pawn_HealthTracker __instance) => Filth_MakeThing_Patch.filthSource = __instance.pawn;
		static void Postfix() => Filth_MakeThing_Patch.filthSource = null;
	}

	[HarmonyPatch]
	static class Filth_MakeThing_Patch
	{
		public static TargetInfo filthCell = null;
		public static Thing filthSource = null;
		public static float? filthSourceContamination = null;
		public static Map filthSourceMap = null;

		static bool Prepare() => Constants.CONTAMINATION;

		public static void SetFilthSource(Thing source, bool snapshotContamination = false)
		{
			filthSource = source;
			filthSourceContamination = null;
			filthSourceMap = null;
			if (snapshotContamination && source != null)
			{
				_ = ContaminationManager.TryGetThingMap(source, null, out filthSourceMap);
				filthSourceContamination = ContaminationManager.Instance.Get(source, true, filthSourceMap);
			}
		}

		public static void ClearFilthSource()
		{
			filthSource = null;
			filthSourceContamination = null;
			filthSourceMap = null;
		}

		static IEnumerable<MethodBase> TargetMethods()
		{
			yield return SymbolExtensions.GetMethodInfo((Pawn_FilthTracker tracker) => tracker.GainFilth(default, default));
			yield return SymbolExtensions.GetMethodInfo((Filth outFilth) => FilthMaker.TryMakeFilth(default, default, default, default, default, out outFilth, default));
		}

		static readonly HashSet<ThingDef> nastyFilths = new()
		{
			ThingDefOf.Filth_Blood, ThingDefOf.Filth_Vomit, ThingDefOf.Filth_AmnioticFluid, ThingDefOf.Filth_Slime,
			ThingDefOf.Filth_CorpseBile, ThingDefOf.Filth_PodSlime, ThingDefOf.Filth_OilSmear
		};
		static Thing FilthContamination(Thing newThing)
		{
			if (Tools.IsPlaying())
			{
				if (filthCell.IsValid)
					ZombieSettings.Values.contamination.filthEqualize.Equalize((LocalTargetInfo)filthCell, newThing, filthCell.mapInt);
				if (filthSource != null || filthSourceContamination.HasValue)
				{
					var factor = filthSource != null && nastyFilths.Contains(filthSource.def) ? ZombieSettings.Values.contamination.bloodEqualize : ZombieSettings.Values.contamination.filthEqualize;
					var sourceMap = filthSourceMap;
					if (sourceMap == null)
						_ = ContaminationManager.TryGetThingMap(filthSource, null, out sourceMap);
					var sourceContamination = filthSourceContamination ?? ContaminationManager.Instance.Get(filthSource, true, sourceMap);
					ContaminationManager.Instance.Add(newThing, sourceContamination * factor, sourceMap);
				}
			}
			return newThing;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var m_MakeThing = SymbolExtensions.GetMethodInfo(() => ThingMaker.MakeThing(default, default));
			var m_TransferFilth = SymbolExtensions.GetMethodInfo(() => FilthContamination(default));

			return new CodeMatcher(instructions)
				.MatchEndForward(new CodeMatch(operand: m_MakeThing), new CodeMatch())
				.ThrowIfInvalid($"Cannot find {m_MakeThing.FullDescription()}")
				.Insert(Call[m_TransferFilth])
				.InstructionEnumeration();
		}
	}
}
