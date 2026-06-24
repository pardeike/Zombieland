using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;
using static ZombieLand.Patches;

namespace ZombieLand
{
	public class CETools
	{
		public static bool latePatching = false;

		public static Type TypeByNames(params string[] typeNames)
		{
			return typeNames
				.Select(AccessTools.TypeByName)
				.FirstOrDefault(type => type != null);
		}

		public static void Init(Harmony harmony)
		{
			latePatching = true;
			try
			{
				PatchGroups.ApplyLateGroup(harmony, PatchGroups.Optional, new[]
				{
					typeof(CETools_Patch1),
					typeof(CETools_Patch2),
					typeof(CETools_Patch3),
					typeof(CETools_Patch4)
				});
			}
			finally
			{
				latePatching = false;
			}
		}
	}

	[HarmonyPatch]
	class CETools_Patch1
	{
		static bool Prepare() => CETools.latePatching && TargetMethod() != null;
		static MethodInfo TargetMethod()
		{
			var type = CETools.TypeByNames(
				"CombatExtended.HarmonyCE.Harmony_DamageWorker_AddInjury_ApplyDamageToPart",
				"CombatExtended.Harmony.Harmony_DamageWorker_AddInjury_ApplyDamageToPart");
			if (type == null)
				return null;
			var boolRef = typeof(bool).MakeByRefType();
			var method = AccessTools.Method(type, "ArmorReroute", new[] { typeof(Pawn), typeof(DamageInfo).MakeByRefType(), boolRef, boolRef });
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
	static class CETools_Patch2
	{
		static bool Prepare() => CETools.latePatching && TargetMethods().Any();
		static IEnumerable<MethodBase> TargetMethods()
		{
			var type = AccessTools.TypeByName("CombatExtended.ProjectileCE");
			if (type == null)
				yield break;
			var parameters = new[] { typeof(Thing), typeof(Vector2), typeof(float), typeof(float), typeof(float), typeof(float), typeof(Thing), typeof(float) };
			var baseMethod = AccessTools.Method(type, "Launch", parameters);
			if (baseMethod == null)
			{
				Error("Combat Extended installed, but method ProjectileCE.Launch(Thing,Vector2,float,float,float,float,Thing,float) not found");
				yield break;
			}
			yield return baseMethod;
			foreach (var subclass in type.Assembly.GetTypes().Where(candidate => candidate != type && type.IsAssignableFrom(candidate)))
			{
				var overrideMethod = AccessTools.DeclaredMethod(subclass, "Launch", parameters);
				if (overrideMethod != null)
					yield return overrideMethod;
			}
		}

		static void Postfix(Thing launcher, Vector2 origin, float shotAngle, float shotHeight, float shotSpeed)
		{
			if (launcher is not Pawn pawn || launcher is ZombieSpitter)
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
	static class CETools_Patch3
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

		static bool Prefix(ref DamageInfo originalDinfo, Pawn pawn, BodyPartRecord hitPart, out bool armorDeflected, out bool armorReduced, out bool shieldAbsorbed, ref DamageInfo __result)
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
					armorReduced = dinfo.Amount < dmgAmount;
					originalDinfo = dinfo;
					__result = dinfo;
					return false;
				}
				return true;
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
			armorDeflected = deflect;
			armorReduced = diminish;
			shieldAbsorbed = deflect || diminish;

			return false;
		}
	}

	[HarmonyPatch]
	static class CETools_Patch4
	{
		static bool Prepare() => CETools.latePatching && TargetMethod() != null;
		static MethodBase TargetMethod()
		{
			var type = AccessTools.TypeByName("CombatExtended.CompAmmoUser");
			if (type == null)
				return null;
			var method = AccessTools.Method(type, "Notify_ShotFired", new Type[] { typeof(int) });
			if (method == null)
			{
				Error("Combat Extended installed, but method CompAmmoUser.Notify_ShotFired(int) not found");
				return null;
			}
			return method;
		}

		static bool Prefix(Building_Turret ___turret)
		{
			if (___turret == null)
				return true;
			if (Rand.Chance(ZombieSettings.Values.reducedTurretConsumption))
			{
				return false;
			}
			return true;
		}
	}
}
