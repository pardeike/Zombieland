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
		public static bool latePatching = false;

		public static void Init(Harmony harmony)
		{
			latePatching = true;
			_ = new PatchClassProcessor(harmony, typeof(Patch1)).Patch();
			_ = new PatchClassProcessor(harmony, typeof(Patch2)).Patch();
			_ = new PatchClassProcessor(harmony, typeof(Patch3)).Patch();
			_ = new PatchClassProcessor(harmony, typeof(Patch4)).Patch();
			latePatching = false;
		}
	}

	[HarmonyPatch]
	class Patch1
	{
		static bool Prepare() => CETools.latePatching && TargetMethod() != null;
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

	[HarmonyPatch]
	static class Patch2
	{
		static bool Prepare() => CETools.latePatching && TargetMethod() != null;
		static MethodBase TargetMethod()
		{
			var type = AccessTools.TypeByName("CombatExtended.ProjectileCE");
			if (type == null)
				return null;
			var method = AccessTools.Method(type, "Launch", new Type[] { typeof(Thing), typeof(Vector2), typeof(float), typeof(float), typeof(float), typeof(float), typeof(Thing), typeof(float) });
			if (method == null)
			{
				Error("Combat Extended installed, but method ProjectileCE.Launch(Thing,Vector2,float,float,float,float,Thing,float) not found");
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
			var delta = GetDistanceTraveled(shotSpeed, shotAngle, shotHeight);
			var magnitude = noiseScale * delta * Math.Min(1f, ZombieSettings.Values.zombieInstinct.HalfToDoubleValue());
			var radius = Tools.Boxed(magnitude, Constants.WEAPON_RANGE[0], Constants.WEAPON_RANGE[1]);
			var grid = launcher.Map.GetGrid();
			Tools.GetCircle(radius).Do(vec => grid.BumpTimestamp(pos + vec, now - vec.LengthHorizontalSquared));
		}

		public static float GetDistanceTraveled(float velocity, float angle, float shotHeight)
		{
			if (shotHeight < 0.001f)
				return (velocity * velocity / 9.8f) * Mathf.Sin(2f * angle);
			var velsin = velocity * Mathf.Sin(angle);
			return ((velocity * Mathf.Cos(angle)) / 9.8f) * (velsin + Mathf.Sqrt(velsin * velsin + 2f * 9.8f * shotHeight));
		}
	}

	[HarmonyPatch]
	static class Patch3
	{
		static bool Prepare() => CETools.latePatching && TargetMethod() != null;
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

		static bool Prefix(ref DamageInfo originalDinfo, Pawn pawn, BodyPartRecord hitPart, out bool armorDeflected, out bool shieldAbsorbed, out bool armorReduced, ref DamageInfo __result)
		{
			__result = originalDinfo;
			var dinfo = new DamageInfo(originalDinfo);
			var dmgAmount = dinfo.Amount;

			armorDeflected = false;
			shieldAbsorbed = false;
			armorReduced = false;
			if (pawn == null || hitPart == null)
				return true;
			if (pawn is ZombieSpitter)
			{
				if (originalDinfo.Def == DamageDefOf.Bullet)
				{
					var diff = ZombieSettings.Values.spitterThreat;
					armorDeflected = Rand.Range(0, 5.1f) < diff;
					dinfo.SetAmount(dmgAmount / (1 + 10 * diff));
				}
				return armorDeflected;
			}
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

	[HarmonyPatch]
	static class Patch4
	{
		static bool Prepare() => CETools.latePatching && TargetMethod() != null;
		static MethodBase TargetMethod()
		{
			var type = AccessTools.TypeByName("CombatExtended.CompAmmoUser");
			if (type == null)
				return null;
			var method = AccessTools.Method(type, "TryReduceAmmoCount");
			if (method == null)
			{
				Error("Combat Extended installed, but method CompAmmoUser.TryReduceAmmoCount not found");
				return null;
			}
			return method;
		}

		static bool Prefix(Building_Turret ___turret, ref bool __result)
		{
			if (___turret == null)
				return true;
			if (Rand.Chance(ZombieSettings.Values.reducedTurretConsumption))
			{
				__result = true;
				return false;
			}
			return true;
		}
	}
}
