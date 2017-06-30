using Harmony;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	[HarmonyPatch]
	static class PawnGraphicSet_Constructor_With_Pawn_Patch
	{
		static MethodBase TargetMethod()
		{
			return AccessTools.Constructor(typeof(PawnGraphicSet), new Type[] { typeof(Pawn) });
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var from = AccessTools.Constructor(typeof(DamageFlasher), new Type[] { typeof(Pawn) });
			var to = AccessTools.Constructor(typeof(ZombieDamageFlasher), new Type[] { typeof(Pawn) });
			return Transpilers.MethodReplacer(instructions, from, to);
		}
	}

	[HarmonyPatch(typeof(DamageFlasher))]
	[HarmonyPatch("Notify_DamageApplied")]
	static class DamageFlasher_Notify_DamageApplied_Patch
	{
		[HarmonyPriority(Priority.First)]
		static void Prefix(DamageFlasher __instance, DamageInfo dinfo)
		{
			var zombieDamageFlasher = __instance as ZombieDamageFlasher;
			if (zombieDamageFlasher != null)
				zombieDamageFlasher.dinfoDef = dinfo.Def;
		}
	}

	[HarmonyPatch(typeof(DamageFlasher))]
	[HarmonyPatch("GetDamagedMat")]
	static class DamageFlasher_GetDamagedMat_Patch
	{
		static Color greenDamagedMatStartingColor = new Color(0f, 0.8f, 0f);

		[HarmonyPriority(Priority.Last)]
		static void Postfix(DamageFlasher __instance, Material baseMat, Material __result)
		{
			var zombieDamageFlasher = __instance as ZombieDamageFlasher;
			if (zombieDamageFlasher != null && zombieDamageFlasher.isColonist
				&& zombieDamageFlasher.dinfoDef == ZombieDamageFlasher.zombieBiteDamageDef
				&& __result != null)
			{
				var damPct = zombieDamageFlasher.damageFlashTicksLeft.GetValue<int>() / 16f;
				__result.color = Color.Lerp(baseMat.color, greenDamagedMatStartingColor, damPct);
			}
		}
	}

	class ZombieDamageFlasher : DamageFlasher
	{
		public bool isColonist;
		public DamageDef dinfoDef;
		public Traverse damageFlashTicksLeft;

		public static DamageDef zombieBiteDamageDef = DefDatabase<DamageDef>.GetNamed("ZombieBite");

		public ZombieDamageFlasher(Pawn pawn) : base(pawn)
		{
			isColonist = pawn.IsColonist;
			damageFlashTicksLeft = Traverse.Create(this).Property("DamageFlashTicksLeft");
		}
	}
}