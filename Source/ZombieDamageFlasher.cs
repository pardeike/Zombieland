using HarmonyLib;
using System;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	[HarmonyPatch(typeof(PawnGraphicSet))]
	[HarmonyPatch(MethodType.Constructor, typeof(Pawn))]
	static class PawnGraphicSet_Constructor_With_Pawn_Patch
	{
		static void Postfix(PawnGraphicSet __instance)
		{
			__instance.flasher = new ZombieDamageFlasher(__instance.pawn);
		}
	}

	[HarmonyPatch(typeof(DamageFlasher))]
	[HarmonyPatch("Notify_DamageApplied")]
	static class DamageFlasher_Notify_DamageApplied_Patch
	{
		[HarmonyPriority(Priority.First)]
		static void Prefix(DamageFlasher __instance, DamageInfo dinfo)
		{
			if (__instance is ZombieDamageFlasher zombieDamageFlasher)
				zombieDamageFlasher.dinfoDef = dinfo.Def;
		}
	}

	[HarmonyPatch(typeof(DamageFlasher))]
	[HarmonyPatch("GetDamagedMat")]
	static class DamageFlasher_GetDamagedMat_Patch
	{
		static readonly Color greenDamagedMatStartingColor = new Color(0f, 0.8f, 0f);

		private static int DamageFlashTicksLeft(DamageFlasher damageFlasher)
		{
			// copied from DamageFlasher.DamageFlashTicksLeft
			return GetterSetters.lastDamageTickByRef(damageFlasher) + 16 - Find.TickManager.TicksGame;
		}

		[HarmonyPriority(Priority.Last)]
		static void Postfix(DamageFlasher __instance, Material baseMat, Material __result)
		{
			if (__instance is ZombieDamageFlasher zombieDamageFlasher
				&& zombieDamageFlasher.dinfoDef == Tools.ZombieBiteDamageDef
				&& __result != null)
			{
				var damPct = DamageFlashTicksLeft(__instance) / 16f;
				__result.color = Color.Lerp(baseMat.color, greenDamagedMatStartingColor, damPct);
			}
		}
	}

	class ZombieDamageFlasher : DamageFlasher
	{
		public DamageDef dinfoDef;

		public ZombieDamageFlasher(Pawn pawn) : base(pawn) { }
	}
}