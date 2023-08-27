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
	static class Pawn_FilthTracker_Notify_EnteredNewCell_TestPatch
	{
		static void Prefix(Pawn_FilthTracker __instance) => Filth_MakeThing_TestPatch.filthSource = __instance.pawn;
		static void Postfix(Pawn_FilthTracker __instance)
		{
			var pawn = __instance.pawn;
			ContaminationFactors.enterCellEqualize.Equalize(pawn, pawn.Position);
			Filth_MakeThing_TestPatch.filthSource = null;
		}
	}

	[HarmonyPatch]
	static class JobDriver_CleanFilth_MakeNewToils_TestPatch
	{
		static MethodBase TargetMethod()
		{
			var m_ThinFilth = SymbolExtensions.GetMethodInfo((Filth filth) => filth.ThinFilth());
			var type = AccessTools.FirstInner(typeof(JobDriver_CleanFilth), type => type.Name.Contains("DisplayClass"));
			return AccessTools.FirstMethod(type, method => method.CallsMethod(m_ThinFilth));
		}

		static void ThinFilth(Filth filth, JobDriver_CleanFilth jobDriver)
		{
			filth.TransferContamination(ContaminationFactors.filthTransfer, () => Log.Warning($"{jobDriver.pawn} cleaned {filth}"), jobDriver.pawn);
			filth.ThinFilth();
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> instructions.ExtraArgumentsTranspiler(typeof(Filth), () => ThinFilth(default, default), default, 1, true);
	}

	[HarmonyPatch(typeof(CompSpawnerFilth), nameof(CompSpawnerFilth.TrySpawnFilth))]
	static class CompSpawnerFilth_TrySpawnFilth_TestPatch
	{
		static void Prefix(CompSpawnerFilth __instance) => Filth_MakeThing_TestPatch.filthSource = __instance.parent;
		static void Postfix() => Filth_MakeThing_TestPatch.filthSource = null;
	}

	[HarmonyPatch(typeof(GenLeaving), nameof(GenLeaving.DoLeavingsFor))]
	[HarmonyPatch(new[] { typeof(Thing), typeof(Map), typeof(DestroyMode), typeof(CellRect), typeof(Predicate<IntVec3>), typeof(List<Thing>) })]
	static class GenLeaving_DoLeavingsFor_TestPatch
	{
		static void Prefix(Thing diedThing, ref List<Thing> listOfLeavingsOut)
		{
			listOfLeavingsOut ??= new List<Thing>();
			Filth_MakeThing_TestPatch.filthSource = diedThing;
		}
		static void Postfix(Thing diedThing, List<Thing> listOfLeavingsOut)
		{
			Filth_MakeThing_TestPatch.filthSource = null;
			if (listOfLeavingsOut.Any())
			{
				var leavingsArray = listOfLeavingsOut.ToArray();
				diedThing.TransferContamination(ContaminationFactors.leavingsTransfer, () => Log.Warning($"Produce {leavingsArray.Join(t => $"{t}")} from {diedThing}"), leavingsArray);
			}
		}
	}

	[HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
	static class Thing_TakeDamage_TestPatch
	{
		static void Prefix(Thing __instance) => Filth_MakeThing_TestPatch.filthSource = __instance;
		static void Postfix() => Filth_MakeThing_TestPatch.filthSource = null;
	}

	[HarmonyPatch(typeof(HediffComp_DissolveGearOnDeath), nameof(HediffComp_DissolveGearOnDeath.Notify_PawnKilled))]
	static class HediffComp_DissolveGearOnDeath_Notify_PawnKilled_TestPatch
	{
		static void Prefix(HediffComp_DissolveGearOnDeath __instance) => Filth_MakeThing_TestPatch.filthSource = __instance.Pawn;
		static void Postfix() => Filth_MakeThing_TestPatch.filthSource = null;
	}

	[HarmonyPatch(typeof(Projectile_Liquid), nameof(Projectile_Liquid.DoImpact))]
	static class Projectile_Liquid_DoImpact_TestPatch
	{
		static void Prefix(Thing hitThing) => Filth_MakeThing_TestPatch.filthSource = hitThing;
		static void Postfix() => Filth_MakeThing_TestPatch.filthSource = null;
	}

	[HarmonyPatch(typeof(TunnelHiveSpawner), nameof(TunnelHiveSpawner.Tick))]
	static class TunnelHiveSpawner_Tick_TestPatch
	{
		static void Prefix(TunnelHiveSpawner __instance)
		{
			Filth_MakeThing_TestPatch.filthSource = __instance;
			Filth_MakeThing_TestPatch.filthCell = new TargetInfo(__instance.Position, __instance.Map);
		}
		static void Postfix()
		{
			Filth_MakeThing_TestPatch.filthSource = null;
			Filth_MakeThing_TestPatch.filthCell = null;
		}
	}

	[HarmonyPatch(typeof(DamageWorker_Flame), nameof(DamageWorker_Flame.Apply))]
	static class DamageWorker_Flame_Apply_TestPatch
	{
		static void Prefix(Thing victim) => Filth_MakeThing_TestPatch.filthSource = victim;
		static void Postfix() => Filth_MakeThing_TestPatch.filthSource = null;
	}

	[HarmonyPatch(typeof(Verse.Explosion), nameof(Verse.Explosion.TrySpawnExplosionThing))]
	static class Verse_Explosion_TrySpawnExplosionThing_TestPatch
	{
		static void Prefix(Verse.Explosion __instance)
			=> Filth_MakeThing_TestPatch.filthSource = __instance.damagedThings.OrderBy(t => t.GetContamination()).LastOrDefault();
		static void Postfix() => Filth_MakeThing_TestPatch.filthSource = null;
	}

	[HarmonyPatch(typeof(RoofCollapserImmediate), nameof(RoofCollapserImmediate.DropRoofInCellPhaseTwo))]
	static class RoofCollapserImmediate_DropRoofInCellPhaseTwo_TestPatch
	{
		static void Prefix(IntVec3 c, Map map) => Filth_MakeThing_TestPatch.filthCell = new TargetInfo(c, map);
		static void Postfix() => Filth_MakeThing_TestPatch.filthCell = null;
	}

	[HarmonyPatch]
	static class JobDriver_Vomit_MakeNewToils_TestPatch
	{
		static readonly MethodInfo m_TryMakeFilth = SymbolExtensions.GetMethodInfo(() => FilthMaker.TryMakeFilth(IntVec3.Invalid, default, ThingDefOf.Filth_Vomit, "", 0, FilthSourceFlags.Any));

		static void Prefix(JobDriver_Vomit __instance) => Filth_MakeThing_TestPatch.filthSource = __instance.pawn;
		static void Postfix() => Filth_MakeThing_TestPatch.filthSource = null;

		static MethodBase TargetMethod()
		{
			return AccessTools.FirstMethod(typeof(JobDriver_Vomit), method => method.CallsMethod(m_TryMakeFilth));
		}
	}

	[HarmonyPatch(typeof(PregnancyUtility), nameof(PregnancyUtility.SpawnBirthFilth))]
	static class PregnancyUtility_SpawnBirthFilth_TestPatch
	{
		static void Prefix(Pawn mother) => Filth_MakeThing_TestPatch.filthSource = mother;
		static void Postfix() => Filth_MakeThing_TestPatch.filthSource = null;
	}

	[HarmonyPatch(typeof(Corpse), nameof(Corpse.ButcherProducts))]
	static class Corpse_ButcherProducts_TestPatch
	{
		static IEnumerable<Thing> Postfix(IEnumerable<Thing> things, Corpse __instance)
		{
			foreach (var thing in things)
			{
				Filth_MakeThing_TestPatch.filthSource = __instance;
				yield return thing;
			}
			Filth_MakeThing_TestPatch.filthSource = null;
		}
	}

	[HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.DropBloodFilth))]
	static class Pawn_HealthTracker_DropBloodFilth_TestPatch
	{
		static void Prefix(Pawn_HealthTracker __instance) => Filth_MakeThing_TestPatch.filthSource = __instance.pawn;
		static void Postfix() => Filth_MakeThing_TestPatch.filthSource = null;
	}

	[HarmonyPatch]
	static class Filth_MakeThing_TestPatch
	{
		public static TargetInfo filthCell = null;
		public static Thing filthSource = null;

		static IEnumerable<MethodBase> TargetMethods()
		{
			yield return SymbolExtensions.GetMethodInfo((Pawn_FilthTracker tracker) => tracker.GainFilth(default, default));
			yield return SymbolExtensions.GetMethodInfo(() => FilthMaker.TryMakeFilth(default, default, default, (IEnumerable<string>)default, default, default));
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
				{
					newThing.mapIndexOrState = (sbyte)filthCell.mapInt.Index;
					ContaminationFactors.filthEqualize.Equalize((LocalTargetInfo)filthCell, newThing, () => Log.Warning($"Gained {newThing} from {filthCell}"));
				}
				if (filthSource != null)
				{
					var factor = nastyFilths.Contains(filthSource.def) ? ContaminationFactors.bloodEqualize : ContaminationFactors.filthEqualize;
					factor.Equalize(filthSource, newThing, () => Log.Warning($"Gained {newThing} from {filthSource}"));
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