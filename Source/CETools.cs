using HarmonyLib;
using RimWorld;
using System;
using System.Reflection;
using UnityEngine;
using Verse;
using static ZombieLand.Patches;

namespace ZombieLand
{
	public class CETools
	{
		public static void Init(Harmony harmony)
		{
			_ = new PatchClassProcessor(harmony, typeof(Patch1)).Patch();
			_ = new PatchClassProcessor(harmony, typeof(Patch2)).Patch();
			_ = new PatchClassProcessor(harmony, typeof(Patch3)).Patch();
		}
	}

	class Patch1
	{
		static bool Prepare() => TargetMethod() != null;
		static MethodInfo TargetMethod()
		{
			var type = AccessTools.TypeByName("CombatExtended.Harmony.Harmony_DamageWorker_AddInjury_ApplyDamageToPart");
			if (type == null)
				return null;
			var method = AccessTools.Method(type, "ArmorReroute");
			if (method == null)
			{
				Error("Combat Extended installed, but method Harmony_DamageWorker_AddInjury_ApplyDamageToPart.ArmorReroute not found");
				return null;
			}
			return method;
		}

		static bool Prefix(ref DamageInfo dinfo)
		{
			return dinfo.Def != DamageDefOf.SurgicalCut;
		}
	}

	static class Patch2
	{
		static bool Prepare() => TargetMethod() != null;
		static MethodBase TargetMethod()
		{
			var type = AccessTools.TypeByName("CombatExtended.ProjectileCE");
			if (type == null)
				return null;
			var method = AccessTools.Method(type, "Launch", new Type[] { typeof(Thing), typeof(Vector2), typeof(float), typeof(float), typeof(float), typeof(float), typeof(Thing) });
			if (method == null)
			{
				Error("Combat Extended installed, but method ProjectileCE.Launch not found");
				return null;
			}
			return method;
		}

		static void Postfix(Thing launcher, Vector2 origin, float shotAngle, float shotHeight, float shotSpeed)
		{
			if (launcher is not Pawn pawn)
				return;
			if (launcher.Map == null)
				return;

			var noiseScale = 1f;
			if (pawn.equipment?.PrimaryEq?.PrimaryVerb?.verbProps != null)
				noiseScale = pawn.equipment.PrimaryEq.PrimaryVerb.verbProps.muzzleFlashScale / Constants.BASE_MUZZLE_FLASH_VALUE;

			var now = Tools.Ticks();
			var pos = new IntVec3(origin);
			var delta = Projectile_Launch_Patch.GetDistanceTraveled(shotSpeed, shotAngle, shotHeight);
			var magnitude = noiseScale * delta * Math.Min(1f, ZombieSettings.Values.zombieInstinct.HalfToDoubleValue());
			var radius = Tools.Boxed(magnitude, Constants.WEAPON_RANGE[0], Constants.WEAPON_RANGE[1]);
			var grid = launcher.Map.GetGrid();
			Tools.GetCircle(radius).Do(vec => grid.BumpTimestamp(pos + vec, now - vec.LengthHorizontalSquared));
		}
	}

	static class Patch3
	{
		static bool Prepare() => TargetMethod() != null;
		static MethodBase TargetMethod()
		{
			var type = AccessTools.TypeByName("CombatExtended.ArmorUtilityCE");
			if (type == null)
				return null;
			var boolRef = typeof(bool).MakeByRefType();
			var method = AccessTools.Method(type, "GetAfterArmorDamage", new Type[] { typeof(DamageInfo), typeof(Pawn), typeof(BodyPartRecord), boolRef, boolRef, boolRef });
			if (method == null)
			{
				Error("Combat Extended installed, but method ArmorUtilityCE.GetAfterArmorDamage not found");
				return null;
			}
			return method;
		}

		static bool Prefix(ref DamageInfo originalDinfo, Pawn pawn, BodyPartRecord hitPart, out bool shieldAbsorbed, ref DamageInfo __result)
		{
			__result = originalDinfo;
			var dinfo = new DamageInfo(originalDinfo);
			var dmgAmount = dinfo.Amount;

			shieldAbsorbed = false;
			if (pawn == null || hitPart == null)
				return true;
			var prefixResult = 0f;
			var result = ArmorUtility_GetPostArmorDamage_Patch.Prefix(pawn, ref dmgAmount, hitPart, dinfo.ArmorPenetrationInt, out var deflect, out var diminish, ref prefixResult);
			if (result && originalDinfo.Instigator != null)
				return (pawn.Spawned && pawn.Dead == false
					&& pawn.Destroyed == false
					&& originalDinfo.Instigator.Spawned
					&& originalDinfo.Instigator.Destroyed == false);

			dinfo.SetAmount(dmgAmount);
			originalDinfo = dinfo;
			__result = dinfo;
			shieldAbsorbed = deflect || diminish;

			return false;
		}
	}
}
