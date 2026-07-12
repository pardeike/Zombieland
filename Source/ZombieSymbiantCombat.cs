using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace ZombieLand
{
	/// <summary>
	/// Presents the Symbiant's logical cells to combat without creating proxy Things or moving the
	/// real Pawn. The Pawn remains the sole attack target and damage recipient.
	/// </summary>
	public static class ZombieSymbiantCombat
	{
		sealed class Geometry
		{
			internal int version = int.MinValue;
			internal IntVec3 position = IntVec3.Invalid;
			internal IntVec3[] cells = [];
			internal IntVec3[] boundaryCells = [];
			internal (IntVec3 target, IntVec3 stand)[] meleePairs = [];
			internal float maxRootRadius;
		}

		sealed class AimBinding
		{
			internal ZombieSymbiant symbiant;
			internal IntVec3 cell;
			internal IntVec3 source;
			internal ShootLine line;
			internal int shapeVersion;
			internal int tick;
		}

		internal sealed class TargetScanContext
		{
			internal IAttackTargetSearcher searcher;
			internal TargetScanFlags flags;
			internal Predicate<Thing> validator;
			internal float minDist;
			internal float maxDist;
			internal IntVec3 locus;
			internal float maxTravelRadiusFromLocus;
			internal bool canBashDoors;
			internal bool canTakeTargetsCloserThanEffectiveMinRange;
			internal bool canBashFences;
			internal bool onlyRanged;
			internal bool logicalCandidateEnteredShootingPool;
		}

		static readonly ConditionalWeakTable<ZombieSymbiant, Geometry> geometries = new();
		static readonly ConditionalWeakTable<Verb, AimBinding> aimBindings = new();
		[ThreadStatic] static Verb castingVerb;
		[ThreadStatic] static int castingDepth;
		[ThreadStatic] static Stack<TargetScanContext> targetScanContexts;
		public static int ExplosionMatchedCellCount { get; private set; }
		public static int ExplosionAppliedDamageCount { get; private set; }
		public static int ExplosionAlreadyDamagedCount { get; private set; }
		public static IntVec3 LastExplosionMatchedCell { get; private set; } = IntVec3.Invalid;
		public static int CombatExtendedLogicalCollisionCount { get; private set; }
		public static IntVec3 LastCombatExtendedLogicalCollisionCell { get; private set; } = IntVec3.Invalid;

		internal static void RecordExplosionCell(IntVec3 cell, bool alreadyDamaged, bool applied)
		{
			ExplosionMatchedCellCount++;
			LastExplosionMatchedCell = cell;
			if (alreadyDamaged)
				ExplosionAlreadyDamagedCount++;
			if (applied)
				ExplosionAppliedDamageCount++;
		}

		internal static void RecordCombatExtendedLogicalCollision(IntVec3 cell)
		{
			CombatExtendedLogicalCollisionCount++;
			LastCombatExtendedLogicalCollisionCell = cell;
		}

		public static bool IsPermittedHostileAttacker(Thing attacker)
		{
			if (attacker is not Pawn pawn || pawn.Destroyed || pawn.Dead || pawn.RaceProps?.Animal == true)
				return false;
			if (pawn is Zombie || pawn is ZombieSpitter || pawn is ZombieSymbiant)
				return false;
			if (pawn.Faction?.HostileTo(Faction.OfPlayer) != true)
				return false;
			if (AnomalyTargeting.TryGetZombieHostilityOverride(pawn, out _))
				return false;
			return pawn.RaceProps?.Humanlike == true || pawn.RaceProps?.IsMechanoid == true;
		}

		public static IReadOnlyList<IntVec3> Cells(ZombieSymbiant symbiant) => GetGeometry(symbiant).cells;
		public static IReadOnlyList<IntVec3> BoundaryCells(ZombieSymbiant symbiant) => GetGeometry(symbiant).boundaryCells;
		public static float MaxRootRadius(ZombieSymbiant symbiant) => GetGeometry(symbiant).maxRootRadius;

		static Geometry GetGeometry(ZombieSymbiant symbiant)
		{
			if (symbiant == null)
				return new Geometry();
			var geometry = geometries.GetOrCreateValue(symbiant);
			if (geometry.version == symbiant.CombatShapeVersion && geometry.position == symbiant.Position)
				return geometry;

			var cells = symbiant.AbsoluteCells.Distinct().ToArray();
			var occupied = cells.ToHashSet();
			var boundary = cells
				.Where(cell => GenAdj.AdjacentCells.Any(offset => occupied.Contains(cell + offset) == false))
				.ToArray();
			var pairs = new List<(IntVec3 target, IntVec3 stand)>();
			foreach (var target in boundary)
				foreach (var offset in GenAdj.AdjacentCells)
				{
					var stand = target + offset;
					if (occupied.Contains(stand) == false)
						pairs.Add((target, stand));
				}

			geometry.version = symbiant.CombatShapeVersion;
			geometry.position = symbiant.Position;
			geometry.cells = cells;
			geometry.boundaryCells = boundary;
			geometry.meleePairs = pairs.ToArray();
			geometry.maxRootRadius = cells.Length == 0 ? 0f : cells.Max(cell => cell.DistanceTo(symbiant.Position));
			return geometry;
		}

		static IEnumerable<IntVec3> OrderedBoundaryCells(ZombieSymbiant symbiant, IntVec3 source)
		{
			return GetGeometry(symbiant).boundaryCells
				.OrderBy(cell => cell.DistanceToSquared(source))
				.ThenBy(cell => cell.x)
				.ThenBy(cell => cell.z);
		}

		public static bool TrySelectRangedCell(Verb verb, IntVec3 source, ZombieSymbiant symbiant, out IntVec3 cell, out ShootLine line, bool ignoreRange = false, Predicate<IntVec3> cellValidator = null)
		{
			cell = IntVec3.Invalid;
			line = default;
			if (verb == null || symbiant?.Spawned != true || symbiant.Destroyed || symbiant.Dead || symbiant.Map != verb.caster?.Map)
				return false;

			if (aimBindings.TryGetValue(verb, out var existing)
				&& existing.symbiant == symbiant
				&& existing.shapeVersion == symbiant.CombatShapeVersion
				&& existing.source == source
				&& symbiant.ContainsCell(existing.cell)
				&& (cellValidator == null || cellValidator(existing.cell))
				&& GenTicks.TicksGame - existing.tick <= 60)
			{
				cell = existing.cell;
				line = existing.line;
				return true;
			}

			var map = symbiant.Map;
			foreach (var candidate in OrderedBoundaryCells(symbiant, source))
			{
				if (candidate.InBounds(map) == false || candidate.GetEdifice(map)?.def.Fillage == FillCategory.Full)
					continue;
				if (candidate.GetGas(map)?.def == CustomDefs.TarSmoke)
					continue;
				if (cellValidator != null && cellValidator(candidate) == false)
					continue;
				if (verb.TryFindShootLineFromTo(source, new LocalTargetInfo(candidate), out var candidateLine, ignoreRange) == false)
					continue;

				cell = candidate;
				line = candidateLine;
				var binding = aimBindings.GetOrCreateValue(verb);
				binding.symbiant = symbiant;
				binding.cell = candidate;
				binding.source = source;
				binding.line = candidateLine;
				binding.shapeVersion = symbiant.CombatShapeVersion;
				binding.tick = GenTicks.TicksGame;
				return true;
			}
			return false;
		}

		public static bool TryGetBoundRangedCell(Verb verb, ZombieSymbiant symbiant, out IntVec3 cell)
		{
			cell = IntVec3.Invalid;
			if (verb == null || symbiant == null || aimBindings.TryGetValue(verb, out var binding) == false)
				return false;
			if (binding.symbiant != symbiant || binding.shapeVersion != symbiant.CombatShapeVersion || symbiant.ContainsCell(binding.cell) == false)
				return false;
			cell = binding.cell;
			return true;
		}

		internal static void BindRangedCell(Verb verb, ZombieSymbiant symbiant, IntVec3 source, IntVec3 cell, ShootLine line)
		{
			if (verb == null || symbiant == null || symbiant.ContainsCell(cell) == false)
				return;
			var binding = aimBindings.GetOrCreateValue(verb);
			binding.symbiant = symbiant;
			binding.cell = cell;
			binding.source = source;
			binding.line = line;
			binding.shapeVersion = symbiant.CombatShapeVersion;
			binding.tick = GenTicks.TicksGame;
		}

		public static bool TrySelectMeleeCells(Pawn pawn, ZombieSymbiant symbiant, out IntVec3 standCell, out IntVec3 targetCell, Danger danger = Danger.Deadly, Predicate<IntVec3> targetValidator = null, bool canBashDoors = false, bool canBashFences = false)
		{
			standCell = IntVec3.Invalid;
			targetCell = IntVec3.Invalid;
			if (IsPermittedHostileAttacker(pawn) == false || symbiant?.Spawned != true || symbiant.Map != pawn.Map)
				return false;

			var map = pawn.Map;
			foreach (var pair in GetGeometry(symbiant).meleePairs
				.OrderBy(pair => pair.stand.DistanceToSquared(pawn.Position))
				.ThenBy(pair => pair.target.DistanceToSquared(pawn.Position))
				.ThenBy(pair => pair.stand.x)
				.ThenBy(pair => pair.stand.z))
			{
				if (pair.stand.InBounds(map) == false || pair.target.InBounds(map) == false)
					continue;
				if (targetValidator != null && targetValidator(pair.target) == false)
					continue;
				if (pair.stand != pawn.Position && (pair.stand.Standable(map) == false || pair.stand.GetFirstPawn(map) != null))
					continue;
				if (pawn.CanReach(new LocalTargetInfo(pair.stand), PathEndMode.OnCell, danger, canBashDoors, canBashFences) == false)
					continue;
				standCell = pair.stand;
				targetCell = pair.target;
				return true;
			}
			return false;
		}

		internal static bool PrepareMeleeJob(Pawn pawn, Job job)
		{
			if (pawn == null || job?.def != JobDefOf.AttackMelee || job.targetA.Thing is not ZombieSymbiant symbiant)
				return false;
			if (TrySelectMeleeCells(pawn, symbiant, out var stand, out var target) == false)
				return false;
			job.targetB = stand;
			job.targetC = target;
			return true;
		}

		public static bool TryGetMeleeJobCells(Pawn pawn, ZombieSymbiant symbiant, out IntVec3 stand, out IntVec3 target)
		{
			stand = IntVec3.Invalid;
			target = IntVec3.Invalid;
			var job = pawn?.CurJob;
			if (job?.def != JobDefOf.AttackMelee || job.targetA.Thing != symbiant || job.targetB.HasThing || job.targetC.HasThing)
				return false;
			stand = job.targetB.Cell;
			target = job.targetC.Cell;
			if (stand.IsValid && target.IsValid && symbiant.ContainsCell(target) && symbiant.ContainsCell(stand) == false && stand.AdjacentTo8WayOrInside(target)
				&& (stand == pawn.Position || (stand.Standable(pawn.Map) && stand.GetFirstPawn(pawn.Map) == null)))
				return true;

			if (TrySelectMeleeCells(pawn, symbiant, out stand, out target))
			{
				job.targetB = stand;
				job.targetC = target;
				return true;
			}

			job.targetB = LocalTargetInfo.Invalid;
			job.targetC = LocalTargetInfo.Invalid;
			stand = IntVec3.Invalid;
			target = IntVec3.Invalid;
			return false;
		}

		internal static void BeginTargetScan(
			IAttackTargetSearcher searcher,
			TargetScanFlags flags,
			Predicate<Thing> validator,
			float minDist,
			float maxDist,
			IntVec3 locus,
			float maxTravelRadiusFromLocus,
			bool canBashDoors,
			bool canTakeTargetsCloserThanEffectiveMinRange,
			bool canBashFences,
			bool onlyRanged)
		{
			targetScanContexts ??= new Stack<TargetScanContext>();
			targetScanContexts.Push(new TargetScanContext
			{
				searcher = searcher,
				flags = flags,
				validator = validator,
				minDist = minDist,
				maxDist = maxDist,
				locus = locus,
				maxTravelRadiusFromLocus = maxTravelRadiusFromLocus,
				canBashDoors = canBashDoors,
				canTakeTargetsCloserThanEffectiveMinRange = canTakeTargetsCloserThanEffectiveMinRange,
				canBashFences = canBashFences,
				onlyRanged = onlyRanged
			});
		}

		internal static void EndTargetScan()
		{
			if (targetScanContexts?.Count > 0)
				targetScanContexts.Pop();
		}

		internal static TargetScanContext CurrentTargetScan(IAttackTargetSearcher searcher)
		{
			if (targetScanContexts?.Count > 0 && targetScanContexts.Peek().searcher == searcher)
				return targetScanContexts.Peek();
			return null;
		}

		internal static bool TryGetLogicalAttackTarget(TargetScanContext context, out ZombieSymbiant symbiant)
		{
			symbiant = null;
			if (context?.searcher?.Thing is not Pawn hostile
				|| IsPermittedHostileAttacker(hostile) == false
				|| hostile.Map == null)
				return false;

			var candidate = ZombieSymbiant.ActiveSymbiant(hostile.Map);
			var verb = context.searcher.CurrentEffectiveVerb;
			if (candidate?.Spawned != true || candidate.Destroyed || candidate.Dead || verb == null || candidate == hostile)
				return false;
			if (hostile.HostileTo(candidate) == false || (context.validator != null && context.validator(candidate) == false))
				return false;
			var lord = hostile.GetLord();
			if (lord != null && lord.LordJob.ValidateAttackTarget(hostile, candidate) == false)
				return false;
			if (((context.flags & TargetScanFlags.NeedThreat) != 0 || (context.flags & TargetScanFlags.NeedAutoTargetable) != 0)
				&& candidate.ThreatDisabled(context.searcher))
				return false;
			if ((context.flags & TargetScanFlags.NeedAutoTargetable) != 0 && AttackTargetFinder.IsAutoTargetable(candidate) == false)
				return false;
			if ((context.flags & TargetScanFlags.NeedActiveThreat) != 0 && GenHostility.IsActiveThreatTo(candidate, hostile.Faction) == false)
				return false;
			if (verb.IsEMP() && candidate.RaceProps?.IsFlesh == true)
				return false;
			if ((context.flags & TargetScanFlags.NeedNonBurning) != 0 && candidate.IsBurning())
				return false;
			if (hostile.RaceProps != null && (int)hostile.RaceProps.intelligence >= 2)
			{
				var explosive = candidate.TryGetComp<CompExplosive>();
				if (explosive?.wickStarted == true)
					return false;
			}
			if (candidate.IsCombatant() == false && (context.flags & TargetScanFlags.IgnoreNonCombatants) != 0)
				return false;

			var source = hostile.Position;
			var map = hostile.Map;
			var effectiveMinRange = context.canTakeTargetsCloserThanEffectiveMinRange
				? 0f
				: verb.verbProps.EffectiveMinRange(candidate, hostile);
			var maxLocusDistance = context.maxTravelRadiusFromLocus + verb.EffectiveRange;
			var requirePawnLos = (context.flags & TargetScanFlags.NeedLOSToAll) != 0
				&& (context.flags & TargetScanFlags.NeedLOSToPawns) != 0;
			var validateGasEndpoints = (context.flags & TargetScanFlags.NeedLOSToAll) != 0
				&& (context.flags & TargetScanFlags.LOSBlockableByGas) != 0
				&& (verb.EquipmentSource == null
					|| verb.EquipmentSource.TryGetComp<CompUniqueWeapon>() is not CompUniqueWeapon unique
					|| unique.IgnoreAccuracyMaluses == false);
			var requireVisibleCell = hostile.IsColonist || hostile.Faction == Faction.OfPlayer;
			bool ValidTargetCell(IntVec3 cell)
			{
				var distance = cell.DistanceTo(source);
				if (distance < context.minDist || distance > context.maxDist)
					return false;
				if (effectiveMinRange > 0f && distance < effectiveMinRange)
					return false;
				if (context.maxTravelRadiusFromLocus < 9999f && cell.DistanceTo(context.locus) > maxLocusDistance)
					return false;
				if ((context.flags & TargetScanFlags.NeedNotUnderThickRoof) != 0 && cell.GetRoof(map)?.isThickRoof == true)
					return false;
				if (validateGasEndpoints && (source.AnyGas(map, GasType.BlindSmoke) || cell.AnyGas(map, GasType.BlindSmoke)))
					return false;
				if ((requirePawnLos || candidate.IsCombatant() == false) && GenSight.LineOfSight(source, cell, map) == false)
					return false;
				if (requireVisibleCell && cell.Fogged(map))
					return false;
				return true;
			}

			if (verb.IsMeleeAttack == false || context.onlyRanged)
			{
				if (context.onlyRanged && verb.IsMeleeAttack)
					return false;
				if (TrySelectRangedCell(verb, source, candidate, out _, out _, false, ValidTargetCell) == false)
					return false;
				if ((context.flags & TargetScanFlags.NeedReachable) != 0
					&& TrySelectMeleeCells(hostile, candidate, out _, out _, Danger.Some, ValidTargetCell, context.canBashDoors, context.canBashFences) == false)
					return false;
				symbiant = candidate;
				return true;
			}

			if (context.onlyRanged)
				return false;
			if (hostile.mindState?.duty != null && hostile.mindState.duty.radius > 0f && hostile.InMentalState == false)
			{
				var focus = hostile.mindState.duty.focus.Cell;
				var radius = hostile.mindState.duty.radius;
				var previous = (Predicate<IntVec3>)ValidTargetCell;
				bool ValidDutyCell(IntVec3 cell) => previous(cell) && cell.InHorDistOf(focus, radius);
				if (TrySelectMeleeCells(hostile, candidate, out _, out _, Danger.Deadly, ValidDutyCell, context.canBashDoors, context.canBashFences) == false)
					return false;
			}
			else if (TrySelectMeleeCells(hostile, candidate, out _, out _, Danger.Deadly, ValidTargetCell, context.canBashDoors, context.canBashFences) == false)
				return false;

			symbiant = candidate;
			return true;
		}

		internal static void BeginProjectileCast(Verb verb)
		{
			if (castingDepth++ == 0)
				castingVerb = verb;
		}

		internal static void EndProjectileCast()
		{
			if (--castingDepth <= 0)
			{
				castingDepth = 0;
				castingVerb = null;
			}
		}

		internal static bool TryGetCastingCell(ZombieSymbiant symbiant, out IntVec3 cell)
		{
			return TryGetBoundRangedCell(castingVerb, symbiant, out cell);
		}
	}

	[HarmonyPatch(typeof(ShootLeanUtility), nameof(ShootLeanUtility.CalcShootableCellsOf))]
	static class ShootLeanUtility_CalcShootableCellsOf_Symbiant_Patch
	{
		static bool Prefix(List<IntVec3> outCells, Thing target, IntVec3 shooterPos)
		{
			if (target is not ZombieSymbiant symbiant)
				return true;
			outCells.Clear();
			outCells.AddRange(ZombieSymbiantCombat.BoundaryCells(symbiant)
				.OrderBy(cell => cell.DistanceToSquared(shooterPos))
				.ThenBy(cell => cell.x)
				.ThenBy(cell => cell.z));
			return false;
		}
	}

	[HarmonyPatch(typeof(Verb), nameof(Verb.TryFindShootLineFromTo))]
	static class Verb_TryFindShootLineFromTo_Symbiant_Patch
	{
		[HarmonyPriority(Priority.First)]
		static bool Prefix(Verb __instance, IntVec3 root, LocalTargetInfo targ, bool ignoreRange, ref bool __result, ref ShootLine resultingLine)
		{
			if (targ.Thing is not ZombieSymbiant symbiant)
				return true;

			if (__instance.IsMeleeAttack)
			{
				var pawn = __instance.CasterPawn;
				if (ZombieSymbiantCombat.TryGetMeleeJobCells(pawn, symbiant, out var stand, out var target)
					&& root == stand)
				{
					resultingLine = new ShootLine(root, target);
					__result = true;
					return false;
				}
				var adjacent = ZombieSymbiantCombat.BoundaryCells(symbiant)
					.Where(cell => root.AdjacentTo8WayOrInside(cell))
					.OrderBy(cell => cell.DistanceToSquared(root))
					.ThenBy(cell => cell.x)
					.ThenBy(cell => cell.z)
					.DefaultIfEmpty(IntVec3.Invalid)
					.First();
				resultingLine = new ShootLine(root, adjacent.IsValid ? adjacent : symbiant.Position);
				__result = adjacent.IsValid;
				return false;
			}

			__result = ZombieSymbiantCombat.TrySelectRangedCell(__instance, root, symbiant, out _, out resultingLine, ignoreRange);
			return false;
		}
	}

	[HarmonyPatch(typeof(ShotReport), nameof(ShotReport.HitReportFor))]
	static class ShotReport_HitReportFor_SymbiantCell_Patch
	{
		[HarmonyPriority(Priority.First)]
		static void Prefix(Thing caster, Verb verb, ref LocalTargetInfo target)
		{
			if (target.Thing is not ZombieSymbiant symbiant)
				return;
			if (ZombieSymbiantCombat.TryGetBoundRangedCell(verb, symbiant, out var cell)
				|| ZombieSymbiantCombat.TrySelectRangedCell(verb, caster.Position, symbiant, out cell, out _))
				target = new LocalTargetInfo(cell);
		}
	}

	[HarmonyPatch(typeof(ReachabilityUtility), nameof(ReachabilityUtility.CanReach), new[] { typeof(Pawn), typeof(LocalTargetInfo), typeof(PathEndMode), typeof(Danger), typeof(bool), typeof(bool), typeof(TraverseMode) })]
	static class ReachabilityUtility_CanReach_Symbiant_Patch
	{
		[HarmonyPriority(Priority.First)]
		static void Prefix(Pawn pawn, ref LocalTargetInfo dest, ref PathEndMode peMode, Danger maxDanger)
		{
			if (dest.Thing is not ZombieSymbiant symbiant || ZombieSymbiantCombat.IsPermittedHostileAttacker(pawn) == false)
				return;
			if (ZombieSymbiantCombat.TrySelectMeleeCells(pawn, symbiant, out var stand, out _, maxDanger))
			{
				dest = new LocalTargetInfo(stand);
				peMode = PathEndMode.OnCell;
			}
		}
	}

	[HarmonyPatch(typeof(ReachabilityImmediate), nameof(ReachabilityImmediate.CanReachImmediate), new[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(Map), typeof(PathEndMode), typeof(Pawn) })]
	static class ReachabilityImmediate_CanReachImmediate_Symbiant_Patch
	{
		[HarmonyPriority(Priority.First)]
		static void Prefix(ref LocalTargetInfo target, ref PathEndMode peMode, Pawn pawn)
		{
			if (target.Thing is not ZombieSymbiant symbiant || pawn == null)
				return;
			if (ZombieSymbiantCombat.TryGetMeleeJobCells(pawn, symbiant, out _, out var targetCell))
			{
				target = new LocalTargetInfo(targetCell);
				peMode = PathEndMode.Touch;
			}
		}
	}

	[HarmonyPatch(typeof(JobGiver_AIFightEnemy), "MeleeAttackJob")]
	static class JobGiver_AIFightEnemy_MeleeAttackJob_Symbiant_Patch
	{
		static void Postfix(Pawn pawn, Thing enemyTarget, ref Job __result)
		{
			if (enemyTarget is ZombieSymbiant)
				ZombieSymbiantCombat.PrepareMeleeJob(pawn, __result);
		}
	}

	[HarmonyPatch(typeof(AttackTargetFinder), nameof(AttackTargetFinder.BestAttackTarget))]
	static class AttackTargetFinder_BestAttackTarget_SymbiantContext_Patch
	{
		[HarmonyPriority(Priority.Last)]
		static void Prefix(
			IAttackTargetSearcher searcher,
			TargetScanFlags flags,
			Predicate<Thing> validator,
			float minDist,
			float maxDist,
			IntVec3 locus,
			float maxTravelRadiusFromLocus,
			bool canBashDoors,
			bool canTakeTargetsCloserThanEffectiveMinRange,
			bool canBashFences,
			bool onlyRanged)
		{
			ZombieSymbiantCombat.BeginTargetScan(
				searcher,
				flags,
				validator,
				minDist,
				maxDist,
				locus,
				maxTravelRadiusFromLocus,
				canBashDoors,
				canTakeTargetsCloserThanEffectiveMinRange,
				canBashFences,
				onlyRanged);
		}

		[HarmonyPriority(Priority.Last)]
		static Exception Finalizer(Exception __exception)
		{
			ZombieSymbiantCombat.EndTargetScan();
			return __exception;
		}
	}

	[HarmonyPatch(typeof(AttackTargetFinder), "GetRandomShootingTargetByScore")]
	static class AttackTargetFinder_GetRandomShootingTargetByScore_Symbiant_Patch
	{
		[HarmonyPriority(Priority.First)]
		static void Prefix(List<IAttackTarget> targets, IAttackTargetSearcher searcher, Verb verb)
		{
			var context = ZombieSymbiantCombat.CurrentTargetScan(searcher);
			if (context == null || context.searcher.CurrentEffectiveVerb != verb
				|| ZombieSymbiantCombat.TryGetLogicalAttackTarget(context, out var symbiant) == false)
				return;
			if (targets.Contains(symbiant) == false)
				targets.Add(symbiant);
			context.logicalCandidateEnteredShootingPool = true;
		}
	}

	[HarmonyPatch(typeof(Verb_LaunchProjectile), nameof(Verb_LaunchProjectile.TryCastShot))]
	static class Verb_LaunchProjectile_TryCastShot_SymbiantContext_Patch
	{
		[HarmonyPriority(Priority.First)]
		static void Prefix(Verb_LaunchProjectile __instance) => ZombieSymbiantCombat.BeginProjectileCast(__instance);

		[HarmonyPriority(Priority.Last)]
		static Exception Finalizer(Exception __exception)
		{
			ZombieSymbiantCombat.EndProjectileCast();
			return __exception;
		}
	}

	[HarmonyPatch(typeof(Projectile), nameof(Projectile.Launch), new[] { typeof(Thing), typeof(Vector3), typeof(LocalTargetInfo), typeof(LocalTargetInfo), typeof(ProjectileHitFlags), typeof(bool), typeof(Thing), typeof(ThingDef) })]
	static class Projectile_Launch_SymbiantCell_Patch
	{
		[HarmonyPriority(Priority.First)]
		static void Prefix(ref LocalTargetInfo usedTarget, LocalTargetInfo intendedTarget)
		{
			if (usedTarget.Thing is not ZombieSymbiant symbiant || intendedTarget.Thing != symbiant)
				return;
			if (ZombieSymbiantCombat.TryGetCastingCell(symbiant, out var cell))
				usedTarget = new LocalTargetInfo(cell);
		}
	}

	[HarmonyPatch(typeof(Projectile), nameof(Projectile.ImpactSomething))]
	static class Projectile_ImpactSomething_SymbiantCell_Patch
	{
		[HarmonyPriority(Priority.First)]
		static void Prefix(Projectile __instance, ref LocalTargetInfo ___usedTarget)
		{
			if (__instance.intendedTarget.Thing is not ZombieSymbiant symbiant
				|| symbiant.Spawned == false
				|| symbiant.Map != __instance.Map
				|| symbiant.ContainsCell(__instance.Position) == false)
				return;
			___usedTarget = new LocalTargetInfo(symbiant);
		}
	}

	[HarmonyPatch(typeof(Pawn_DrawTracker), nameof(Pawn_DrawTracker.Notify_MeleeAttackOn))]
	static class Pawn_DrawTracker_Notify_MeleeAttackOn_Symbiant_Patch
	{
		static bool Prefix(Thing Target, Pawn ___pawn, JitterHandler ___jitterer)
		{
			if (Target is not ZombieSymbiant symbiant
				|| ZombieSymbiantCombat.TryGetMeleeJobCells(___pawn, symbiant, out _, out var targetCell) == false)
				return true;
			if (targetCell != ___pawn.Position)
				___jitterer.AddOffset(0.5f, (targetCell - ___pawn.Position).AngleFlat);
			return false;
		}
	}
}
