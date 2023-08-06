using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace ZombieLand
{
	[HarmonyPatch]
	static class Verb_MeleeAttack_ApplyMeleeDamageToTarget_TestPatch
	{
		static IEnumerable<MethodBase> TargetMethods()
		{
			return typeof(Verb_MeleeAttack)
				.AllSubclassesNonAbstract()
				.Select(type => AccessTools.Method(type, nameof(Verb_MeleeAttack.ApplyMeleeDamageToTarget)));
		}

		static void Postfix(Verb_MeleeAttack __instance, LocalTargetInfo target, DamageWorker.DamageResult __result)
		{
			if (__result.totalDamageDealt <= 0f)
				return;
			var pawn = __instance.Caster;
			var thing = target.Thing;
			ContaminationFactors.meleeEqualize.Equalize(pawn, thing, () => Log.Warning($"# {pawn} melee {thing}"));
		}
	}
}