using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	[HarmonyPatch(typeof(PawnRenderer))]
	[HarmonyPatch(MethodType.Constructor, typeof(Pawn))]
	static class PawnRenderer_Constructor_With_Pawn_Patch
	{
		static void Postfix(PawnRenderer __instance, Pawn pawn)
		{
			__instance.flasher = new ZombieDamageFlasher(pawn);
		}
	}

	[HarmonyPatch(typeof(DamageFlasher))]
	[HarmonyPatch(nameof(DamageFlasher.Notify_DamageApplied))]
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
	[HarmonyPatch(nameof(DamageFlasher.GetDamagedMat))]
	static class DamageFlasher_GetDamagedMat_Patch
	{
		static readonly Color greenDamagedMatStartingColor = new(0f, 0.8f, 0f);
		static readonly Dictionary<Material, Material> greenDamagedMats = new();

		private static int DamageFlashTicksLeft(DamageFlasher damageFlasher)
		{
			// copied from DamageFlasher.DamageFlashTicksLeft
			return damageFlasher.lastDamageTick + 16 - GenTicks.TicksGame;
		}

		static Material GetGreenDamageFlashMat(Material baseMat, float damPct)
		{
			if (damPct < 0.01f)
				return baseMat;
			if (greenDamagedMats.TryGetValue(baseMat, out var material) == false)
			{
				material = MaterialAllocator.Create(baseMat);
				greenDamagedMats.Add(baseMat, material);
			}
			material.color = Color.Lerp(baseMat.color, greenDamagedMatStartingColor, damPct);
			return material;
		}

		[HarmonyPriority(Priority.Last)]
		static void Postfix(DamageFlasher __instance, Material baseMat, ref Material __result)
		{
			if (__instance is ZombieDamageFlasher zombieDamageFlasher
				&& zombieDamageFlasher.dinfoDef == CustomDefs.ZombieBite
				&& baseMat != null
				&& __result != null)
			{
				var damPct = DamageFlashTicksLeft(__instance) / 16f;
				__result = GetGreenDamageFlashMat(baseMat, damPct);
			}
		}
	}

	class ZombieDamageFlasher : DamageFlasher
	{
		public DamageDef dinfoDef;

		public ZombieDamageFlasher(Pawn pawn) : base(pawn) { }
	}
}
