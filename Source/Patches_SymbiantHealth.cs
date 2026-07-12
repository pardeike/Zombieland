using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace ZombieLand
{
	static partial class Patches
	{
		sealed class SymbiantDamagePatchState
		{
			public ZombieSymbiant symbiant;
			public Dictionary<Hediff, float> hediffSeveritiesBefore;
		}

		[HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
		static class Thing_TakeDamage_Symbiant_Patch
		{
			static void Prefix(Thing __instance, out SymbiantDamagePatchState __state)
			{
				__state = null;
				if (__instance is not ZombieSymbiant symbiant || symbiant.Destroyed || symbiant.Dead)
					return;
				__state = new SymbiantDamagePatchState
				{
					symbiant = symbiant,
					hediffSeveritiesBefore = symbiant.health?.hediffSet?.hediffs?
						.ToDictionary(hediff => hediff, hediff => hediff.Severity)
						?? new Dictionary<Hediff, float>()
				};
			}

			[HarmonyPriority(Priority.Last)]
			static void Postfix(DamageInfo dinfo, DamageWorker.DamageResult __result, SymbiantDamagePatchState __state)
			{
				__state?.symbiant?.CompleteDamageApplication(dinfo, __result, __state.hediffSeveritiesBefore);
			}
		}

		[HarmonyPatch(typeof(HediffSet), nameof(HediffSet.GetPartHealth))]
		static class HediffSet_GetPartHealth_Symbiant_Patch
		{
			static bool Prefix(HediffSet __instance, BodyPartRecord part, ref float __result)
			{
				if (__instance?.pawn is not ZombieSymbiant symbiant || symbiant.Destroyed || symbiant.Dead)
					return true;
				__result = part?.def?.GetMaxHealth(symbiant) ?? 0f;
				return false;
			}
		}

		[HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.ShouldBeDead))]
		static class Pawn_HealthTracker_ShouldBeDead_Symbiant_Patch
		{
			static bool Prefix(Pawn ___pawn, ref bool __result)
			{
				if (___pawn is not ZombieSymbiant symbiant || symbiant.Destroyed || symbiant.Dead)
					return true;
				__result = false;
				return false;
			}
		}

		[HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.ShouldBeDowned))]
		static class Pawn_HealthTracker_ShouldBeDowned_Symbiant_Patch
		{
			static bool Prefix(Pawn ___pawn, ref bool __result)
			{
				if (___pawn is not ZombieSymbiant symbiant || symbiant.Destroyed || symbiant.Dead)
					return true;
				__result = false;
				return false;
			}
		}

		[HarmonyPatch(typeof(SummaryHealthHandler), nameof(SummaryHealthHandler.SummaryHealthPercent), MethodType.Getter)]
		static class SummaryHealthHandler_SummaryHealthPercent_Symbiant_Patch
		{
			static bool Prefix(Pawn ___pawn, ref float __result)
			{
				if (___pawn is not ZombieSymbiant symbiant || symbiant.Destroyed || symbiant.Dead)
					return true;
				__result = symbiant.SharedHealthFraction;
				return false;
			}
		}
	}
}
