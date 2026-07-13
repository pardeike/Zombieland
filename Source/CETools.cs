using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
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
					typeof(CETools_Patch4),
					typeof(CETools_Patch5_SymbiantCanHit),
					typeof(CETools_Patch6_SymbiantCanHitWithReport),
					typeof(CETools_Patch7_SymbiantShootLine),
					typeof(CETools_Patch8_SymbiantProjectileImpact),
					typeof(CETools_Patch9_SymbiantCollisionBounds),
					typeof(CETools_Patch10_SymbiantRayCast)
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

	static class CESymbiantCombat
	{
		internal delegate bool CandidateProbe(IntVec3 cell, out ShootLine line);

		[ThreadStatic] internal static ZombieSymbiant collisionSymbiant;
		[ThreadStatic] internal static IntVec3 collisionCell;
		[ThreadStatic] internal static ZombieSymbiant raySymbiant;
		[ThreadStatic] internal static IntVec3 rayCell;
		static Type projectileType;
		static Type explosiveProjectileType;
		static FieldInfo intendedTargetField;
		static MethodInfo exactPositionGetter;

		internal static bool TryGetProjectileState(object instance, out Thing projectile, out LocalTargetInfo intendedTarget, out IntVec3 exactCell)
		{
			projectile = instance as Thing;
			intendedTarget = LocalTargetInfo.Invalid;
			exactCell = IntVec3.Invalid;
			try
			{
				projectileType ??= AccessTools.TypeByName("CombatExtended.ProjectileCE");
				if (projectile == null || projectileType?.IsInstanceOfType(instance) != true)
					return false;
				intendedTargetField ??= AccessTools.Field(projectileType, "intendedTarget");
				exactPositionGetter ??= AccessTools.PropertyGetter(projectileType, "ExactPosition");
				if (intendedTargetField == null || exactPositionGetter == null)
					return false;
				intendedTarget = (LocalTargetInfo)intendedTargetField.GetValue(instance);
				exactCell = ((Vector3)exactPositionGetter.Invoke(instance, null)).ToIntVec3();
				return true;
			}
			catch (Exception ex)
			{
				Warn(nameof(TryGetProjectileState), ex);
				return false;
			}
		}

		internal static bool IsExplosiveProjectile(object instance)
		{
			explosiveProjectileType ??= AccessTools.TypeByName("CombatExtended.ProjectileCE_Explosive");
			return explosiveProjectileType?.IsInstanceOfType(instance) == true;
		}

		internal static IEnumerable<MethodBase> DeclaredMethods(string name, params Type[] parameters)
		{
			var baseType = AccessTools.TypeByName("CombatExtended.Verb_LaunchProjectileCE");
			if (baseType == null)
				yield break;
			foreach (var type in baseType.Assembly.GetTypes().Where(type => baseType.IsAssignableFrom(type)))
			{
				var method = AccessTools.DeclaredMethod(type, name, parameters);
				if (method != null && method.ReturnType == typeof(bool))
					yield return method;
			}
		}

		internal static IEnumerable<IntVec3> CandidateCells(ZombieSymbiant symbiant, IntVec3 root)
		{
			return ZombieSymbiantCombat.OrderedBoundaryCells(symbiant, root);
		}

		internal static bool TryBindFirstCandidate(Verb verb, ZombieSymbiant symbiant, IntVec3 root, CandidateProbe probe, out ShootLine line)
		{
			line = default;
			foreach (var cell in CandidateCells(symbiant, root))
			{
				if (probe(cell, out var candidateLine) == false)
					continue;
				line = candidateLine;
				ZombieSymbiantCombat.BindRangedCell(verb, symbiant, root, cell, candidateLine);
				return true;
			}
			return false;
		}

		internal static void Warn(string adapter, Exception ex)
		{
			Log.WarningOnce($"[Zombieland] Combat Extended Symbiant adapter '{adapter}' failed open: {ex.GetBaseException().Message}", ("Zombieland.CESymbiant." + adapter).GetHashCode());
		}
	}

	[HarmonyPatch]
	static class CETools_Patch5_SymbiantCanHit
	{
		static bool Prepare() => CETools.latePatching && TargetMethods().Any();
		static IEnumerable<MethodBase> TargetMethods() => CESymbiantCombat.DeclaredMethods(
			"CanHitTargetFrom", typeof(IntVec3), typeof(LocalTargetInfo));

		[HarmonyPriority(Priority.First)]
		static bool Prefix(object __instance, MethodBase __originalMethod, IntVec3 root, LocalTargetInfo targ, ref bool __result)
		{
			if (targ.Thing is not ZombieSymbiant symbiant)
				return true;
			try
			{
				bool Probe(IntVec3 cell, out ShootLine line)
				{
					line = new ShootLine(root, cell);
					return (bool)__originalMethod.Invoke(__instance, new object[] { root, new LocalTargetInfo(cell) });
				}
				__result = CESymbiantCombat.TryBindFirstCandidate((Verb)__instance, symbiant, root, Probe, out _);
				return false;
			}
			catch (Exception ex)
			{
				CESymbiantCombat.Warn(nameof(CETools_Patch5_SymbiantCanHit), ex);
				return true;
			}
		}
	}

	[HarmonyPatch]
	static class CETools_Patch6_SymbiantCanHitWithReport
	{
		static bool Prepare() => CETools.latePatching && TargetMethods().Any();
		static IEnumerable<MethodBase> TargetMethods() => CESymbiantCombat.DeclaredMethods(
			"CanHitTargetFrom", typeof(IntVec3), typeof(LocalTargetInfo), typeof(string).MakeByRefType());

		[HarmonyPriority(Priority.First)]
		static bool Prefix(object __instance, MethodBase __originalMethod, IntVec3 root, LocalTargetInfo targ, ref string report, ref bool __result)
		{
			if (targ.Thing is not ZombieSymbiant symbiant)
				return true;
			try
			{
				string lastReport = null;
				bool Probe(IntVec3 cell, out ShootLine line)
				{
					var args = new object[] { root, new LocalTargetInfo(cell), null };
					var canHit = (bool)__originalMethod.Invoke(__instance, args);
					lastReport = args[2] as string;
					line = new ShootLine(root, cell);
					return canHit;
				}
				__result = CESymbiantCombat.TryBindFirstCandidate((Verb)__instance, symbiant, root, Probe, out _);
				report = lastReport;
				return false;
			}
			catch (Exception ex)
			{
				CESymbiantCombat.Warn(nameof(CETools_Patch6_SymbiantCanHitWithReport), ex);
				return true;
			}
		}
	}

	[HarmonyPatch]
	static class CETools_Patch7_SymbiantShootLine
	{
		static MethodBase targetMethod;
		static bool Prepare()
		{
			if (CETools.latePatching == false)
				return false;
			var type = AccessTools.TypeByName("CombatExtended.Verb_LaunchProjectileCE");
			targetMethod = AccessTools.Method(type, "TryFindCEShootLineFromTo", new[]
			{
				typeof(IntVec3), typeof(LocalTargetInfo), typeof(ShootLine).MakeByRefType(), typeof(Vector3).MakeByRefType()
			});
			return targetMethod != null;
		}
		static MethodBase TargetMethod() => targetMethod;

		[HarmonyPriority(Priority.First)]
		static bool Prefix(object __instance, IntVec3 root, LocalTargetInfo targ, ref ShootLine resultingLine, ref Vector3 targetPos, ref bool __result)
		{
			if (targ.Thing is not ZombieSymbiant symbiant)
				return true;
			try
			{
				var selectedTargetPos = default(Vector3);
				bool Probe(IntVec3 cell, out ShootLine line)
				{
					var args = new object[] { root, new LocalTargetInfo(cell), default(ShootLine), default(Vector3) };
					var canHit = (bool)targetMethod.Invoke(__instance, args);
					line = (ShootLine)args[2];
					if (canHit)
						selectedTargetPos = (Vector3)args[3];
					return canHit;
				}
				__result = CESymbiantCombat.TryBindFirstCandidate((Verb)__instance, symbiant, root, Probe, out var selectedLine);
				if (__result)
				{
					resultingLine = selectedLine;
					targetPos = selectedTargetPos;
				}
				return false;
			}
			catch (Exception ex)
			{
				CESymbiantCombat.Warn(nameof(CETools_Patch7_SymbiantShootLine), ex);
				return true;
			}
		}
	}

	[HarmonyPatch]
	static class CETools_Patch8_SymbiantProjectileImpact
	{
		static MethodBase targetMethod;
		static bool Prepare()
		{
			if (CETools.latePatching == false)
				return false;
			var type = AccessTools.TypeByName("CombatExtended.ProjectileCE");
			targetMethod = AccessTools.DeclaredMethod(type, "ImpactSomething");
			return targetMethod != null;
		}
		static MethodBase TargetMethod() => targetMethod;

		[HarmonyPriority(Priority.First)]
		static void Prefix(object __instance)
		{
			if (CESymbiantCombat.TryGetProjectileState(__instance, out var projectile, out var intendedTarget, out var exactCell) == false
				|| intendedTarget.Thing is not ZombieSymbiant symbiant
				|| symbiant.Spawned == false
				|| symbiant.Map != projectile.Map
				|| symbiant.ContainsCell(exactCell) == false
				|| CESymbiantCombat.IsExplosiveProjectile(__instance))
				return;
			CESymbiantCombat.collisionSymbiant = symbiant;
			CESymbiantCombat.collisionCell = exactCell;
		}

		static Exception Finalizer(Exception __exception)
		{
			CESymbiantCombat.collisionSymbiant = null;
			CESymbiantCombat.collisionCell = IntVec3.Invalid;
			return __exception;
		}

		static List<Thing> ThingsAtLogicalSymbiantCell(ThingGrid grid, IntVec3 cell)
		{
			var things = grid.ThingsListAt(cell);
			var symbiant = CESymbiantCombat.collisionSymbiant;
			if (symbiant?.Spawned != true || symbiant.ContainsCell(cell) == false || things.Contains(symbiant))
				return things;
			var result = new List<Thing>(things.Count + 1);
			result.AddRange(things);
			result.Add(symbiant);
			return result;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var source = AccessTools.Method(typeof(ThingGrid), nameof(ThingGrid.ThingsListAt), new[] { typeof(IntVec3) });
			var replacement = AccessTools.Method(typeof(CETools_Patch8_SymbiantProjectileImpact), nameof(ThingsAtLogicalSymbiantCell));
			var replaced = false;
			foreach (var instruction in instructions)
			{
				if (instruction.Calls(source))
				{
					instruction.opcode = OpCodes.Call;
					instruction.operand = replacement;
					replaced = true;
				}
				yield return instruction;
			}
			if (replaced == false)
				Log.WarningOnce("[Zombieland] Combat Extended Symbiant ImpactSomething adapter found no ThingsListAt(IntVec3) anchor; projectiles will use CE's root-cell behavior.", "Zombieland.CESymbiant.ImpactAnchor".GetHashCode());
		}
	}

	[HarmonyPatch]
	static class CETools_Patch9_SymbiantCollisionBounds
	{
		static MethodBase TargetMethod()
		{
			if (CETools.latePatching == false)
				return null;
			var type = AccessTools.TypeByName("CombatExtended.CE_Utility");
			return AccessTools.Method(type, "GetBoundsFor", new[] { typeof(Thing) });
		}
		static bool Prepare() => TargetMethod() != null;

		static void Postfix(Thing thing, ref Bounds __result)
		{
			var cell = IntVec3.Invalid;
			if (thing == CESymbiantCombat.collisionSymbiant && CESymbiantCombat.collisionCell.IsValid)
				cell = CESymbiantCombat.collisionCell;
			else if (thing == CESymbiantCombat.raySymbiant && CESymbiantCombat.rayCell.IsValid)
				cell = CESymbiantCombat.rayCell;
			if (cell.IsValid == false)
				return;
			var center = __result.center;
			var cellCenter = cell.ToVector3Shifted();
			center.x = cellCenter.x;
			center.z = cellCenter.z;
			__result.center = center;
			ZombieSymbiantCombat.RecordCombatExtendedLogicalCollision(cell);
		}
	}

	[HarmonyPatch]
	static class CETools_Patch10_SymbiantRayCast
	{
		static MethodBase[] targetMethods;
		static bool Prepare()
		{
			if (CETools.latePatching == false)
				return false;
			var type = AccessTools.TypeByName("CombatExtended.ProjectileCE");
			targetMethods = new[]
			{
				AccessTools.DeclaredMethod(type, "RayCast"),
				AccessTools.DeclaredMethod(type, "CheckCellForCollision")
			}.Where(method => method != null).Cast<MethodBase>().ToArray();
			return targetMethods.Length > 0;
		}
		static IEnumerable<MethodBase> TargetMethods() => targetMethods;

		static void Prefix(object __instance)
		{
			CESymbiantCombat.raySymbiant = CESymbiantCombat.TryGetProjectileState(__instance, out _, out var intendedTarget, out _)
				? intendedTarget.Thing as ZombieSymbiant
				: null;
			CESymbiantCombat.rayCell = IntVec3.Invalid;
		}

		static Exception Finalizer(Exception __exception)
		{
			CESymbiantCombat.raySymbiant = null;
			CESymbiantCombat.rayCell = IntVec3.Invalid;
			return __exception;
		}

		static List<Thing> ThingsAtLogicalSymbiantCell(ThingGrid grid, IntVec3 cell)
		{
			var things = grid.ThingsListAtFast(cell);
			var symbiant = CESymbiantCombat.raySymbiant;
			if (symbiant?.Spawned != true || symbiant.ContainsCell(cell) == false)
				return things;
			CESymbiantCombat.rayCell = cell;
			if (things.Contains(symbiant))
				return things;
			var result = new List<Thing>(things.Count + 1);
			result.AddRange(things);
			result.Add(symbiant);
			return result;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
		{
			var source = AccessTools.Method(typeof(ThingGrid), nameof(ThingGrid.ThingsListAtFast), new[] { typeof(IntVec3) });
			var replacement = AccessTools.Method(typeof(CETools_Patch10_SymbiantRayCast), nameof(ThingsAtLogicalSymbiantCell));
			var replaced = false;
			foreach (var instruction in instructions)
			{
				if (instruction.Calls(source))
				{
					instruction.opcode = OpCodes.Call;
					instruction.operand = replacement;
					replaced = true;
				}
				yield return instruction;
			}
			if (replaced == false)
				Log.WarningOnce($"[Zombieland] Combat Extended Symbiant collision adapter found no ThingsListAtFast(IntVec3) anchor in {__originalMethod?.DeclaringType?.FullName}.{__originalMethod?.Name}; this CE projectile path will use root-cell behavior.", ("Zombieland.CESymbiant.CollisionAnchor." + __originalMethod?.DeclaringType?.FullName + "." + __originalMethod?.Name).GetHashCode());
		}
	}
}
