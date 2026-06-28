using RimBridgeServer.Annotations;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	public sealed partial class ZombielandBridgeTools
	{
		struct LineupEntry
		{
			public readonly ZombieType type;
			public readonly int dx;
			public readonly int dz;

			public LineupEntry(ZombieType type, int dx, int dz)
			{
				this.type = type;
				this.dx = dx;
				this.dz = dz;
			}
		}

		struct FogRoomFixture
		{
			public IntVec3 doorCell;
			public IntVec3 targetWallCell;
			public CellRect interiorRect;
			public Building_Door door;
			public Building targetWall;
		}

		sealed class NeedSnapshot
		{
			public bool hasTracker;
			public int allCount;
			public int internalCount;
			public int miscCount;
			public string[] allDefs;
			public string[] internalDefs;
			public string[] miscDefs;
			public bool hasFoodField;
			public bool hasMoodField;
			public float? foodLevel;
		}

		sealed class RecordSnapshot
		{
			public float rawValue;
			public float publicValue;
			public int publicInt;
		}

		sealed class BloodFilthSnapshot
		{
			public object pawn;
			public string bloodDef;
			public object cell;
			public int before;
			public int after;
			public int delta;
		}

		sealed class DamageLogSnapshot
		{
			public object pawn;
			public float damageAmount;
			public int beforeHediffCount;
			public int afterHediffCount;
			public float damageTotal;
			public int resultPartCount;
			public int resultHediffCount;
			public int combatTextBefore;
			public int combatTextAfter;
			public int combatTextDelta;
		}

		sealed class ContaminationSnapshot
		{
			public float stored;
			public bool hasNeed;
			public float? needLevel;
			public bool hasHediff;
			public float? hediffSeverity;
			public float effectiveness;
			public int hediffCount;
			public bool hasDecontaminationImmunity;
			public int decontaminationImmunityTicksLeft;
			public float decontaminationImmunityDaysLeft;
		}

		sealed class FireDamageSample
		{
			public string kind;
			public int seed;
			public bool burnLonger;
			public float injuryBefore;
			public float injuryAfter;
			public float injuryDelta;
			public bool deadAfter;
			public object pawn;
			public string error;
		}

		sealed class FireDamageComparison
		{
			public string kind;
			public float disabledTotal;
			public float enabledTotal;
			public float delta;
			public float[] disabledDeltas;
			public float[] enabledDeltas;
			public int disabledDeadCount;
			public int enabledDeadCount;
			public string[] errors;
		}

		sealed class WallPushFixture
		{
			public Zombie zombie;
			public Building wall;
			public IntVec3 zombieCell;
			public IntVec3 wallCell;
			public IntVec3 destinationCell;
		}

		static readonly LineupEntry[] referenceLineup =
		{
			new(ZombieType.Electrifier, 0, 0),
			new(ZombieType.SuicideBomber, 2, 0),
			new(ZombieType.Healer, 4, 0),
			new(ZombieType.DarkSlimer, 6, 0),
			new(ZombieType.Albino, 2, 2),
			new(ZombieType.TankyOperator, 0, 4),
			new(ZombieType.ToxicSplasher, 4, 4),
			new(ZombieType.Miner, 6, 4)
		};

		static Map CurrentMap => Find.CurrentMap;

		static void FillZombieTickPercent(float percent)
		{
			for (var i = 0; i < ZombieTicker.percentZombiesTicked.Length; i++)
				ZombieTicker.percentZombiesTicked[i] = percent;
			ZombieTicker.percentZombiesTickedIndex = 0;
		}

		static object DescribeColor(Color color)
		{
			return new
			{
				r = color.r,
				g = color.g,
				b = color.b,
				a = color.a
			};
		}

		static object DescribeIncidentParameters(IncidentParameters parameters)
		{
			if (parameters == null)
				return null;

			return new
			{
				parameters.spawnMode,
				parameters.daysBeforeZombies,
				parameters.maxNumberOfZombies,
				parameters.numberOfZombiesPerColonist,
				parameters.colonyMultiplier,
				parameters.capableColonists,
				parameters.incapableColonists,
				parameters.totalColonistCount,
				parameters.minimumCapableColonists,
				parameters.daysPassed,
				parameters.currentZombieCount,
				parameters.maxBaseLevelZombies,
				parameters.extendedCount,
				parameters.maxAdditionalZombies,
				parameters.calculatedZombies,
				parameters.rampUpDays,
				parameters.scaleFactor,
				parameters.daysStretched,
				parameters.deltaDays,
				parameters.incidentSize,
				parameters.skipReason
			};
		}

		static bool ColorsApproximatelyEqual(Color a, Color b, float tolerance = 0.01f)
		{
			return Mathf.Abs(a.r - b.r) <= tolerance
				&& Mathf.Abs(a.g - b.g) <= tolerance
				&& Mathf.Abs(a.b - b.b) <= tolerance
				&& Mathf.Abs(a.a - b.a) <= tolerance;
		}

		static bool SelectsThroughSelector(object obj)
		{
			var selector = Find.Selector;
			selector.ClearSelection();
			selector.Select(obj, false, false);
			var selected = selector.IsSelected(obj);
			selector.ClearSelection();
			return selected;
		}

		static bool TryHasAnySocialMemoryWith(Pawn pawn, Pawn otherPawn, out bool result, out string error)
		{
			result = false;
			error = null;
			var method = typeof(RelationsUtility).GetMethod("HasAnySocialMemoryWith", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			if (method == null)
			{
				error = "Could not find RelationsUtility.HasAnySocialMemoryWith by reflection.";
				return false;
			}

			result = (bool)method.Invoke(null, new object[] { pawn, otherPawn });
			return true;
		}

		static Dictionary<string, int> MemoryDefCounts(Pawn pawn)
		{
			return pawn?.needs?.mood?.thoughts?.memories?.Memories?
				.GroupBy(memory => memory.def?.defName ?? "<null>")
				.ToDictionary(group => group.Key, group => group.Count())
				?? new Dictionary<string, int>();
		}

		static int TotalMemoryCount(Pawn pawn)
		{
			return pawn?.needs?.mood?.thoughts?.memories?.Memories?.Count ?? 0;
		}

		static readonly FieldInfo needsTrackerNeedsField = typeof(Pawn_NeedsTracker).GetField("needs", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		static readonly FieldInfo needsTrackerMiscNeedsField = typeof(Pawn_NeedsTracker).GetField("needsMisc", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		static readonly MethodInfo needsTrackerAddNeedMethod = typeof(Pawn_NeedsTracker).GetMethod("AddNeed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

		static List<Need> InternalNeeds(Pawn_NeedsTracker needsTracker, FieldInfo field)
		{
			return field?.GetValue(needsTracker) as List<Need> ?? new List<Need>();
		}

		static string[] NeedDefNames(IEnumerable<Need> needs)
		{
			return needs?
				.Select(need => need?.def?.defName ?? "<null>")
				.OrderBy(name => name)
				.ToArray() ?? Array.Empty<string>();
		}

		static NeedSnapshot DescribeNeeds(Pawn pawn)
		{
			var needsTracker = pawn?.needs;
			var allNeeds = needsTracker?.AllNeeds ?? new List<Need>();
			var internalNeeds = InternalNeeds(needsTracker, needsTrackerNeedsField);
			var miscNeeds = InternalNeeds(needsTracker, needsTrackerMiscNeedsField);
			return new NeedSnapshot
			{
				hasTracker = needsTracker != null,
				allCount = allNeeds.Count,
				internalCount = internalNeeds.Count,
				miscCount = miscNeeds.Count,
				allDefs = NeedDefNames(allNeeds),
				internalDefs = NeedDefNames(internalNeeds),
				miscDefs = NeedDefNames(miscNeeds),
				hasFoodField = needsTracker?.food != null,
				hasMoodField = needsTracker?.mood != null,
				foodLevel = needsTracker?.food?.CurLevel
			};
		}

		static bool TryForceAddNeed(Pawn pawn, NeedDef needDef, out string error)
		{
			error = null;
			if (pawn?.needs == null)
			{
				error = "Pawn has no needs tracker.";
				return false;
			}
			if (needDef == null)
			{
				error = "NeedDef is null.";
				return false;
			}
			if (needsTrackerAddNeedMethod == null)
			{
				error = "Could not find Pawn_NeedsTracker.AddNeed by reflection.";
				return false;
			}

			if (pawn.needs.TryGetNeed(needDef) == null)
				needsTrackerAddNeedMethod.Invoke(pawn.needs, new object[] { needDef });
			return true;
		}

		static float ImmunityFor(Pawn pawn, HediffDef hediffDef)
		{
			return pawn?.health?.immunity?.GetImmunity(hediffDef, true) ?? 0f;
		}

		static int ImmunityRecordCount(Pawn pawn)
		{
			return pawn?.health?.immunity?.ImmunityListForReading?.Count ?? 0;
		}

		static readonly FieldInfo recordsTrackerRecordsField = typeof(Pawn_RecordsTracker).GetField("records", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

		static System.Collections.IList RawRecordValues(Pawn pawn)
		{
			var records = recordsTrackerRecordsField?.GetValue(pawn?.records);
			var valuesField = records?.GetType().GetField("values", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			return valuesField?.GetValue(records) as System.Collections.IList;
		}

		static bool TryFindRawRecordDef(Pawn pawn, RecordType recordType, out RecordDef recordDef, out string error)
		{
			recordDef = null;
			error = null;
			var values = RawRecordValues(pawn);
			if (values == null)
			{
				error = "Could not read Pawn_RecordsTracker.records values.";
				return false;
			}

			recordDef = DefDatabase<RecordDef>.AllDefsListForReading
				.Where(def => def.type == recordType && def.index >= 0 && def.index < values.Count)
				.OrderBy(def => def.index)
				.FirstOrDefault();
			if (recordDef != null)
				return true;

			error = $"No {recordType} RecordDef has an index inside the pawn's raw record map of size {values.Count}.";
			return false;
		}

		static float RawRecordValue(Pawn pawn, RecordDef recordDef)
		{
			var values = RawRecordValues(pawn);
			var value = values != null && recordDef != null && recordDef.index >= 0 && recordDef.index < values.Count
				? values[recordDef.index]
				: null;
			return value == null ? 0f : Convert.ToSingle(value);
		}

		static RecordSnapshot DescribeRecord(Pawn pawn, RecordDef recordDef)
		{
			return new RecordSnapshot
			{
				rawValue = RawRecordValue(pawn, recordDef),
				publicValue = pawn?.records?.GetValue(recordDef) ?? 0f,
				publicInt = pawn?.records?.GetAsInt(recordDef) ?? 0
			};
		}

		static readonly MethodInfo pathFollowerCostToMoveIntoCellMethod = typeof(Pawn_PathFollower).GetMethod("CostToMoveIntoCell", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Pawn), typeof(IntVec3) }, null);
		static readonly MethodInfo fireDoFireDamageMethod = typeof(Fire).GetMethod("DoFireDamage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Thing) }, null);
		static readonly MethodInfo fireDoComplexCalcsMethod = typeof(Fire).GetMethod("DoComplexCalcs", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		static readonly MethodInfo fireVulnerableToRainMethod = typeof(Fire).GetMethod("VulnerableToRain", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		static readonly MethodInfo fireWatcherUpdateObservationsMethod = typeof(FireWatcher).GetMethod("UpdateObservations", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		static readonly MethodInfo meleeDamageInfosToApplyMethod = typeof(Verb_MeleeAttackDamage).GetMethod("DamageInfosToApply", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		static readonly MethodInfo worldPawnsShouldMothballMethod = typeof(RimWorld.Planet.WorldPawns).GetMethod("ShouldMothball", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Pawn) }, null);
		static readonly MethodInfo tickManagerNothingHappeningMethod = typeof(Verse.TickManager).GetMethod("NothingHappeningInGame", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		static readonly MethodInfo pawnPathFollowerWillCollideMethod = typeof(Pawn_PathFollower).GetMethod("WillCollideWithPawnAt", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(IntVec3), typeof(bool), typeof(bool) }, null);

		static bool TryCostToMoveIntoCell(Pawn pawn, IntVec3 cell, out float cost, out string error)
		{
			cost = 0f;
			error = null;
			if (pathFollowerCostToMoveIntoCellMethod == null)
			{
				error = "Could not find Pawn_PathFollower.CostToMoveIntoCell(Pawn, IntVec3).";
				return false;
			}
			if (pawn == null)
			{
				error = "Pawn is null.";
				return false;
			}

			cost = Convert.ToSingle(pathFollowerCostToMoveIntoCellMethod.Invoke(null, new object[] { pawn, cell }));
			return true;
		}

		static bool TryDoFireDamage(Fire fire, Thing target, out string error)
		{
			error = null;
			if (fireDoFireDamageMethod == null)
			{
				error = "Could not find Fire.DoFireDamage(Thing).";
				return false;
			}
			if (fire == null || target == null)
			{
				error = "Fire and target are required.";
				return false;
			}

			fireDoFireDamageMethod.Invoke(fire, new object[] { target });
			return true;
		}

		static bool TryDoFireComplexCalcs(Fire fire, out string error)
		{
			error = null;
			if (fireDoComplexCalcsMethod == null)
			{
				error = "Could not find Fire.DoComplexCalcs().";
				return false;
			}
			if (fire == null)
			{
				error = "Fire is required.";
				return false;
			}

			fireDoComplexCalcsMethod.Invoke(fire, null);
			return true;
		}

		static bool TryVulnerableToRain(Fire fire, out bool vulnerable, out string error)
		{
			vulnerable = false;
			error = null;
			if (fireVulnerableToRainMethod == null)
			{
				error = "Could not find Fire.VulnerableToRain().";
				return false;
			}
			if (fire == null)
			{
				error = "Fire is required.";
				return false;
			}

			vulnerable = (bool)fireVulnerableToRainMethod.Invoke(fire, null);
			return true;
		}

		static bool TryShouldMothball(Pawn pawn, out bool shouldMothball, out string error)
		{
			shouldMothball = false;
			error = null;
			if (worldPawnsShouldMothballMethod == null)
			{
				error = "Could not find WorldPawns.ShouldMothball(Pawn).";
				return false;
			}
			if (pawn == null)
			{
				error = "Pawn is required.";
				return false;
			}

			shouldMothball = (bool)worldPawnsShouldMothballMethod.Invoke(Find.WorldPawns, new object[] { pawn });
			return true;
		}

		static bool TryNothingHappeningInGame(out bool nothingHappening, out string error)
		{
			nothingHappening = false;
			error = null;
			if (tickManagerNothingHappeningMethod == null)
			{
				error = "Could not find TickManager.NothingHappeningInGame().";
				return false;
			}

			nothingHappening = (bool)tickManagerNothingHappeningMethod.Invoke(Find.TickManager, null);
			return true;
		}

		static bool TryWillCollideWithPawnAt(Pawn pawn, IntVec3 cell, out bool willCollide, out string error)
		{
			willCollide = false;
			error = null;
			if (pawnPathFollowerWillCollideMethod == null)
			{
				error = "Could not find Pawn_PathFollower.WillCollideWithPawnAt(IntVec3, bool, bool).";
				return false;
			}
			if (pawn?.pather == null)
			{
				error = "Pawn with path follower is required.";
				return false;
			}

			willCollide = (bool)pawnPathFollowerWillCollideMethod.Invoke(pawn.pather, new object[] { cell, false, false });
			return true;
		}

		static bool TryMeleeDamageInfosToApply(Verb verb, LocalTargetInfo target, out DamageInfo[] damageInfos, out string error)
		{
			damageInfos = Array.Empty<DamageInfo>();
			error = null;
			if (meleeDamageInfosToApplyMethod == null)
			{
				error = "Could not find Verb_MeleeAttackDamage.DamageInfosToApply(LocalTargetInfo).";
				return false;
			}
			if (verb is not Verb_MeleeAttackDamage)
			{
				error = $"Verb is not Verb_MeleeAttackDamage: {verb?.GetType().Name ?? "null"}.";
				return false;
			}

			try
			{
				damageInfos = ((IEnumerable<DamageInfo>)meleeDamageInfosToApplyMethod.Invoke(verb, new object[] { target })).ToArray();
				return true;
			}
			catch (TargetInvocationException ex)
			{
				error = ex.InnerException?.Message ?? ex.Message;
				return false;
			}
		}

		static bool TryApplyMeleeDamageToTarget(Verb verb, LocalTargetInfo target, out DamageWorker.DamageResult result, out string error)
		{
			result = null;
			error = null;
			if (verb == null)
			{
				error = "Verb is null.";
				return false;
			}

			var method = verb.GetType().GetMethod("ApplyMeleeDamageToTarget", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (method == null)
			{
				error = $"Could not find {verb.GetType().Name}.ApplyMeleeDamageToTarget(LocalTargetInfo).";
				return false;
			}

			try
			{
				result = (DamageWorker.DamageResult)method.Invoke(verb, new object[] { target });
				return true;
			}
			catch (TargetInvocationException ex)
			{
				error = ex.InnerException?.Message ?? ex.Message;
				return false;
			}
		}

		static bool TrySampleRainVulnerability(Fire fire, int samples, int seed, out int trueCount, out int falseCount, out string error)
		{
			trueCount = 0;
			falseCount = 0;
			error = null;

			Rand.PushState(seed);
			try
			{
				for (var i = 0; i < samples; i++)
				{
					if (TryVulnerableToRain(fire, out var vulnerable, out error) == false)
						return false;
					if (vulnerable)
						trueCount++;
					else
						falseCount++;
				}
			}
			finally
			{
				Rand.PopState();
			}

			return true;
		}

		static float TotalInjurySeverity(Pawn pawn)
		{
			return pawn?.health?.hediffSet?.hediffs?
				.OfType<Hediff_Injury>()
				.Sum(hediff => hediff.Severity) ?? 0f;
		}

		static Pawn SpawnFireFixturePawn(Map map, IntVec3 cell, string kind)
		{
			if (kind == "human")
			{
				var human = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
				GenSpawn.Spawn(human, cell, map, Rot4.South);
				DisablePawnWork(human);
				return human;
			}

			if (kind == "normal")
				return ZombieRuntimeActions.SpawnZombie(cell, map, ZombieType.Normal, true);

			if (kind == "spitter")
			{
				var spitter = PawnGenerator.GeneratePawn(ZombieDefOf.ZombieSpitter, Find.FactionManager.FirstFactionOfDef(ZombieDefOf.Zombies));
				GenSpawn.Spawn(spitter, cell, map, Rot4.South, WipeMode.Vanish, false);
				return spitter;
			}

			if (kind == "symbiant")
			{
				var symbiant = PawnGenerator.GeneratePawn(ZombieDefOf.ZombieSymbiant, Find.FactionManager.FirstFactionOfDef(ZombieDefOf.Zombies));
				GenSpawn.Spawn(symbiant, cell, map, Rot4.South, WipeMode.Vanish, false);
				return symbiant;
			}

			return null;
		}

		static void DestroyFireFixturePawn(Pawn pawn)
		{
			if (pawn == null)
				return;

			var fire = pawn.GetAttachment(ThingDefOf.Fire) as Fire;
			if (fire != null)
			{
				pawn.TryGetComp<CompAttachBase>()?.RemoveAttachment(fire);
				if (fire.Destroyed == false)
					fire.Destroy(DestroyMode.Vanish);
			}

			if (pawn.Destroyed == false)
				pawn.Destroy(DestroyMode.Vanish);
		}

		static void NormalizeFireDamagePawn(Pawn pawn)
		{
			if (pawn == null)
				return;
			pawn.apparel?.DestroyAll();
			pawn.equipment?.DestroyAllEquipment(DestroyMode.Vanish);
			pawn.inventory?.DestroyAll();
			foreach (var hediff in pawn.health?.hediffSet?.hediffs?.ToArray() ?? Array.Empty<Hediff>())
				pawn.health.RemoveHediff(hediff);
		}

		static FireDamageSample SampleFireDamage(Map map, Fire fire, IntVec3 cell, string kind, bool burnLonger, int seed)
		{
			Pawn pawn = null;
			var originalBurnLonger = ZombieSettings.Values.zombiesBurnLonger;
			try
			{
				foreach (var existingPawn in cell.GetThingList(map).OfType<Pawn>().ToArray())
					existingPawn.Destroy(DestroyMode.Vanish);

				pawn = SpawnFireFixturePawn(map, cell, kind);
				if (pawn == null)
				{
					return new FireDamageSample
					{
						kind = kind,
						seed = seed,
						burnLonger = burnLonger,
						error = $"Could not spawn {kind} fire-damage fixture pawn."
					};
				}

				NormalizeFireDamagePawn(pawn);
				var before = TotalInjurySeverity(pawn);
				ZombieSettings.Values.zombiesBurnLonger = burnLonger;
				Rand.PushState(seed);
				try
				{
					if (TryDoFireDamage(fire, pawn, out var error) == false)
					{
						return new FireDamageSample
						{
							kind = kind,
							seed = seed,
							burnLonger = burnLonger,
							injuryBefore = before,
							injuryAfter = TotalInjurySeverity(pawn),
							injuryDelta = TotalInjurySeverity(pawn) - before,
							deadAfter = pawn.Dead,
							pawn = DescribePawn(pawn),
							error = error
						};
					}
				}
				finally
				{
					Rand.PopState();
				}

				var after = TotalInjurySeverity(pawn);
				return new FireDamageSample
				{
					kind = kind,
					seed = seed,
					burnLonger = burnLonger,
					injuryBefore = before,
					injuryAfter = after,
					injuryDelta = after - before,
					deadAfter = pawn.Dead,
					pawn = DescribePawn(pawn)
				};
			}
			finally
			{
				ZombieSettings.Values.zombiesBurnLonger = originalBurnLonger;
				if (pawn != null && pawn.Destroyed == false)
					pawn.Destroy(DestroyMode.Vanish);
			}
		}

		static FireDamageComparison CompareFireDamage(Map map, Fire fire, IntVec3 cell, string kind, int samples, int seed)
		{
			var disabled = new FireDamageSample[samples];
			var enabled = new FireDamageSample[samples];
			for (var i = 0; i < samples; i++)
			{
				var sampleSeed = seed + i;
				disabled[i] = SampleFireDamage(map, fire, cell, kind, false, sampleSeed);
				enabled[i] = SampleFireDamage(map, fire, cell, kind, true, sampleSeed);
			}

			var disabledTotal = disabled.Sum(sample => sample.injuryDelta);
			var enabledTotal = enabled.Sum(sample => sample.injuryDelta);
			return new FireDamageComparison
			{
				kind = kind,
				disabledTotal = disabledTotal,
				enabledTotal = enabledTotal,
				delta = disabledTotal - enabledTotal,
				disabledDeltas = disabled.Select(sample => sample.injuryDelta).ToArray(),
				enabledDeltas = enabled.Select(sample => sample.injuryDelta).ToArray(),
				disabledDeadCount = disabled.Count(sample => sample.deadAfter),
				enabledDeadCount = enabled.Count(sample => sample.deadAfter),
				errors = disabled.Concat(enabled)
					.Where(sample => sample.error != null)
					.Select(sample => $"{sample.kind}:{sample.seed}:{sample.burnLonger}:{sample.error}")
					.ToArray()
			};
		}

		static object DescribeZombie(Pawn pawn)
		{
			var zombie = pawn as Zombie;
			var symbiant = pawn as ZombieSymbiant;
			var spitter = pawn as ZombieSpitter;

			return new
			{
				pawnId = ZombieRuntimeActions.StableThingId(pawn),
				thingId = pawn?.ThingID,
				defName = pawn?.def?.defName,
				kindDef = pawn?.kindDef?.defName,
				label = pawn?.LabelCap,
				spawned = pawn?.Spawned ?? false,
				dead = pawn?.Dead ?? false,
				downed = pawn?.Downed ?? false,
				faction = pawn?.Faction?.Name,
				position = pawn == null ? null : ZombieRuntimeActions.DescribeCell(pawn.Position),
				lastGotoPosition = zombie != null && zombie.lastGotoPosition.IsValid ? ZombieRuntimeActions.DescribeCell(zombie.lastGotoPosition) : null,
				gridAtPosition = zombie?.Spawned == true ? zombie.Map.GetGrid().GetZombieCount(zombie.Position) : 0,
				gridAtLastGotoPosition = zombie?.Spawned == true && zombie.lastGotoPosition.IsValid ? zombie.Map.GetGrid().GetZombieCount(zombie.lastGotoPosition) : 0,
				state = zombie?.state.ToString() ?? spitter?.state.ToString(),
				raging = zombie?.raging ?? 0,
				kind = DescribeZombieKind(zombie, symbiant, spitter),
				wasMapPawnBefore = zombie?.wasMapPawnBefore ?? false,
				isSuicideBomber = zombie?.IsSuicideBomber ?? false,
				bombWillGoOff = zombie?.bombWillGoOff,
				bombTickingInterval = zombie?.bombTickingInterval,
				isToxicSplasher = zombie?.isToxicSplasher ?? false,
				isTanky = zombie?.IsTanky ?? false,
				isMiner = zombie?.isMiner ?? false,
				isElectrifier = zombie?.isElectrifier ?? false,
				isAlbino = zombie?.isAlbino ?? false,
				isDarkSlimer = zombie?.isDarkSlimer ?? false,
				isHealer = zombie?.isHealer ?? false,
				isOnFire = zombie?.isOnFire ?? false,
				fireSurvivalBoost = zombie?.fireSurvivalBoost ?? false,
				hasFireSurvivalBoost = zombie?.HasFireSurvivalBoost ?? false,
				spitterAggressive = spitter?.aggressive,
				spitterWaves = spitter?.waves,
				spitterRemainingZombies = spitter?.remainingZombies,
				spitterSpitInterval = spitter?.spitInterval,
				spitterTickCounter = spitter?.tickCounter,
				currentJob = pawn?.CurJobDef?.defName,
				currentJobReport = SafeCurrentJobReport(pawn),
				patherMoving = pawn?.pather?.Moving ?? false,
				patherDestination = pawn?.pather?.Moving == true ? ZombieRuntimeActions.DescribeCell(pawn.pather.Destination.Cell) : null
			};
		}

		static object DescribePawn(Pawn pawn)
		{
			return new
			{
				pawnId = ZombieRuntimeActions.StableThingId(pawn),
				thingId = pawn?.ThingID,
				defName = pawn?.def?.defName,
				kindDef = pawn?.kindDef?.defName,
				label = pawn?.LabelCap,
				spawned = pawn?.Spawned ?? false,
				dead = pawn?.Dead ?? false,
				downed = pawn?.Downed ?? false,
				faction = pawn?.Faction?.Name,
				position = pawn == null ? null : ZombieRuntimeActions.DescribeCell(pawn.Position),
				currentJob = pawn?.CurJobDef?.defName,
				currentJobReport = SafeCurrentJobReport(pawn),
				stunned = pawn?.stances?.stunner?.Stunned
			};
		}

		static string SafeCurrentJobReport(Pawn pawn)
		{
			var job = pawn?.CurJob;
			if (job == null || job.def == JobDefOf.IdleWhileDespawned)
				return null;
			return job.GetReport(pawn);
		}

		static void DisablePawnWork(Pawn pawn)
		{
			if (pawn?.workSettings == null)
				return;

			pawn.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
			pawn.workSettings.DisableAll();
		}

		static object DescribeVerb(Verb verb)
		{
			var damageDef = verb?.GetDamageDef();
			return new
			{
				isNull = verb == null,
				label = verb?.verbProps?.label,
				verbClass = verb?.GetType().Name,
				isMelee = verb?.IsMeleeAttack ?? false,
				damageDef = damageDef?.defName,
				damageIsRanged = damageDef?.isRanged,
				canHarmElectric = verb.CanHarmElectricZombies()
			};
		}

		static object[] DescribeDamageInfos(IEnumerable<DamageInfo> damageInfos)
		{
			return damageInfos
				.Select(info => new
				{
					def = info.Def?.defName,
					amount = info.Amount,
					weapon = info.Weapon?.defName,
					instigator = ZombieRuntimeActions.StableThingId(info.Instigator as Thing)
				})
				.Cast<object>()
				.ToArray();
		}

		static (bool value, string error) TryHostileTo(Thing a, Thing b)
		{
			try
			{
				return (GenHostility.HostileTo(a, b), null);
			}
			catch (Exception ex)
			{
				return (false, $"{ex.GetType().Name}: {ex.Message}");
			}
		}

		static (bool value, string error) TryHostileTo(Thing thing, Faction faction)
		{
			try
			{
				return (GenHostility.HostileTo(thing, faction), null);
			}
			catch (Exception ex)
			{
				return (false, $"{ex.GetType().Name}: {ex.Message}");
			}
		}

		static object DescribeHostility((bool value, string error) sample)
		{
			return new
			{
				value = sample.value,
				error = sample.error
			};
		}

		static object DescribeTankyArmor(Zombie zombie)
		{
			return zombie == null ? null : new
			{
				shield = zombie.hasTankyShield,
				helmet = zombie.hasTankyHelmet,
				suit = zombie.hasTankySuit,
				isTanky = zombie.IsTanky
			};
		}

		static DamageLogSnapshot DamageAndAssociateWithLog(Pawn pawn, float amount)
		{
			var beforeHediffCount = pawn?.health?.hediffSet?.hediffs?.Count ?? 0;
			var part = pawn.health.hediffSet.GetNotMissingParts().FirstOrDefault(record => record.def.alive)
				?? pawn.health.hediffSet.GetNotMissingParts().FirstOrDefault();
			var hediff = HediffMaker.MakeHediff(HediffDefOf.Cut, pawn, part);
			hediff.Severity = amount;
			pawn.health.AddHediff(hediff, part);

			var result = new DamageWorker.DamageResult
			{
				hitThing = pawn,
				totalDamageDealt = amount
			};
			result.AddPart(pawn, part);
			result.AddHediff(hediff);

			var combatTextBefore = string.IsNullOrEmpty(hediff.combatLogText) ? 0 : 1;
			var log = new BattleLogEntry_DamageTaken(pawn, RulePackDefOf.DamageEvent_Fire);
			result.AssociateWithLog(log);
			var combatTextAfter = string.IsNullOrEmpty(hediff.combatLogText) ? 0 : 1;

			return new DamageLogSnapshot
			{
				pawn = DescribePawn(pawn),
				damageAmount = amount,
				beforeHediffCount = beforeHediffCount,
				afterHediffCount = pawn?.health?.hediffSet?.hediffs?.Count ?? 0,
				damageTotal = result.totalDamageDealt,
				resultPartCount = result.parts?.Count ?? 0,
				resultHediffCount = result.hediffs?.Count ?? 0,
				combatTextBefore = combatTextBefore,
				combatTextAfter = combatTextAfter,
				combatTextDelta = combatTextAfter - combatTextBefore
			};
		}

		static object DescribeCorpse(Corpse corpse)
		{
			var destroyed = corpse?.Destroyed ?? true;
			var bugged = corpse?.Bugged ?? true;
			var compRottable = corpse == null || destroyed ? null : corpse.TryGetComp<CompRottable>();
			var innerPawn = corpse == null || bugged ? null : corpse.InnerPawn;
			var label = corpse == null
				? null
				: bugged
					? corpse.def?.label?.CapitalizeFirst()
					: corpse.LabelCap.ToString();
			return new
			{
				corpseId = ZombieRuntimeActions.StableThingId(corpse),
				thingId = corpse?.ThingID,
				label,
				spawned = corpse?.Spawned ?? false,
				destroyed,
				bugged,
				position = corpse == null || corpse.Spawned == false ? null : ZombieRuntimeActions.DescribeCell(corpse.Position),
				rotStage = corpse == null || destroyed ? null : corpse.GetRotStage().ToString(),
				rotProgress = compRottable?.RotProgress ?? 0f,
				innerPawnId = ZombieRuntimeActions.StableThingId(innerPawn),
				innerPawnThingId = innerPawn?.ThingID,
				innerPawnLabel = innerPawn?.LabelCap
			};
		}

		static ContaminationSnapshot DescribeContamination(Pawn pawn)
		{
			var need = pawn?.needs?.TryGetNeed<ContaminationNeed>();
			var hediffs = pawn?.health?.hediffSet?.hediffs?
				.OfType<Hediff_Contamination>()
				.ToArray() ?? Array.Empty<Hediff_Contamination>();
			var hediff = hediffs.FirstOrDefault();
			var immunityTicksLeft = pawn == null ? 0 : ContaminationManager.Instance.DecontaminationImmunityTicksLeft(pawn);
			return new ContaminationSnapshot
			{
				stored = pawn?.GetContamination() ?? 0f,
				hasNeed = need != null,
				needLevel = need?.CurLevel,
				hasHediff = hediff != null,
				hediffSeverity = hediff?.Severity,
				effectiveness = pawn?.GetEffectiveness() ?? 0f,
				hediffCount = hediffs.Length,
				hasDecontaminationImmunity = immunityTicksLeft > 0,
				decontaminationImmunityTicksLeft = immunityTicksLeft,
				decontaminationImmunityDaysLeft = immunityTicksLeft / (float)GenDate.TicksPerDay
			};
		}

		static string DescribeZombieKind(Zombie zombie, ZombieSymbiant symbiant, ZombieSpitter spitter)
		{
			if (symbiant != null)
				return "Symbiant";
			if (spitter != null)
				return "Spitter";
			if (zombie == null)
				return null;
			if (zombie.IsSuicideBomber)
				return "SuicideBomber";
			if (zombie.isToxicSplasher)
				return "ToxicSplasher";
			if (zombie.IsTanky)
				return "TankyOperator";
			if (zombie.isMiner)
				return "Miner";
			if (zombie.isElectrifier)
				return "Electrifier";
			if (zombie.isAlbino)
				return "Albino";
			if (zombie.isDarkSlimer)
				return "DarkSlimer";
			if (zombie.isHealer)
				return "Healer";
			return "Normal";
		}

		static bool TryParseZombieType(string value, out ZombieType zombieType, out string error)
		{
			error = null;
			if (string.IsNullOrWhiteSpace(value))
			{
				zombieType = ZombieType.Normal;
				return true;
			}

			if (Enum.TryParse(value.Trim(), true, out zombieType))
				return true;

			var names = string.Join(", ", Enum.GetNames(typeof(ZombieType)));
			error = $"Unknown zombie type '{value}'. Valid values: {names}.";
			return false;
		}

		static bool TryFindSpawnCell(int x, int z, out Map map, out IntVec3 cell, out object error)
		{
			map = CurrentMap;
			cell = IntVec3.Invalid;
			error = null;

			if (map == null)
			{
				error = new
				{
					success = false,
					error = "No current map is loaded."
				};
				return false;
			}

			var root = x >= 0 && z >= 0 ? new IntVec3(x, 0, z) : new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
			if (root.InBounds(map) == false)
			{
				error = new
				{
					success = false,
					error = $"Cell ({root.x}, {root.z}) is outside the current map."
				};
				return false;
			}

			foreach (var candidate in GenRadial.RadialCellsAround(root, 12f, true))
			{
				if (candidate.InBounds(map) == false)
					continue;
				if (candidate.Standable(map) == false)
					continue;
				if (candidate.Fogged(map))
					continue;

				cell = candidate;
				return true;
			}

			error = new
			{
				success = false,
				error = $"No standable unfogged cell was found near ({root.x}, {root.z})."
			};
			return false;
		}

		static Pawn[] CurrentZombies(Map map)
		{
			if (map == null)
				return Array.Empty<Pawn>();

			var pawns = new List<Pawn>();
			if (map.mapPawns?.AllPawnsSpawned != null)
				pawns.AddRange(map.mapPawns.AllPawnsSpawned.Where(pawn => pawn is Zombie || pawn is ZombieSpitter));

			var symbiant = ZombieSymbiant.ActiveSymbiant(map);
			if (symbiant != null && pawns.Contains(symbiant) == false)
				pawns.Add(symbiant);
			return pawns.ToArray();
		}

		static bool TryFindZombie(Map map, string target, out Pawn pawn, out string error)
		{
			pawn = null;
			error = null;
			if (map == null)
			{
				error = "No current map is loaded.";
				return false;
			}
			if (string.IsNullOrWhiteSpace(target))
			{
				error = "A zombie id, ThingID, label, or short name is required.";
				return false;
			}

			var query = target.Trim();
			pawn = CurrentZombies(map).FirstOrDefault(candidate =>
				string.Equals(ZombieRuntimeActions.StableThingId(candidate), query, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(candidate.ThingID, query, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(candidate.LabelShortCap, query, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(candidate.Name?.ToStringShort, query, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(candidate.Name?.ToStringFull, query, StringComparison.OrdinalIgnoreCase));
			if (pawn != null)
				return true;

			error = $"No spawned Zombieland pawn matched '{target}'.";
			return false;
		}

		static bool TryFindOrSpawnSpitter(Map map, string target, out ZombieSpitter spitter, out bool spawnedSpitter, out object error)
		{
			spitter = null;
			spawnedSpitter = false;
			error = null;

			if (string.IsNullOrWhiteSpace(target))
			{
				var root = new IntVec3(map.Size.x / 2, 0, map.Size.z / 2);
				if (TryFindClearSpawnCell(map, root, 16f, out var cell, out error) == false)
					return false;

				var existing = CurrentZombies(map).OfType<ZombieSpitter>()
					.Select(ZombieRuntimeActions.StableThingId)
					.ToHashSet(StringComparer.OrdinalIgnoreCase);
				ZombieSpitter.Spawn(map, cell);
				spitter = CurrentZombies(map).OfType<ZombieSpitter>()
					.FirstOrDefault(candidate => existing.Contains(ZombieRuntimeActions.StableThingId(candidate)) == false)
					?? CurrentZombies(map).OfType<ZombieSpitter>().OrderBy(candidate => candidate.Position.DistanceToSquared(cell)).FirstOrDefault();
				spawnedSpitter = spitter != null;
			}
			else if (TryFindZombie(map, target, out var pawn, out var findError) == false)
			{
				error = new
				{
					success = false,
					error = findError
				};
				return false;
			}
			else
			{
				spitter = pawn as ZombieSpitter;
			}

			if (spitter != null)
				return true;

			error = new
			{
				success = false,
				error = "Target is not a zombie spitter."
			};
			return false;
		}

		static ZombieBall ForceSpitterShot(Map map, ZombieSpitter spitter, int seed)
		{
			if (spitter.CurJobDef != CustomDefs.Spitter)
				spitter.jobs.StartJob(JobMaker.MakeJob(CustomDefs.Spitter));

			var existing = map.listerThings.AllThings
				.Where(thing => thing.def == CustomDefs.ZombieBall)
				.Select(thing => thing.ThingID)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);

			Rand.PushState(seed);
			try
			{
				spitter.aggressive = true;
				spitter.waves = Math.Max(1, spitter.waves);
				spitter.remainingZombies = 1;
				spitter.spitInterval = 4;
				spitter.tickCounter = spitter.spitInterval;
				spitter.state = SpitterState.Spitting;
				AdvanceGameTicks(1);
			}
			finally
			{
				Rand.PopState();
			}

			return map.listerThings.AllThings
				.OfType<ZombieBall>()
				.FirstOrDefault(projectile => existing.Contains(projectile.ThingID) == false)
				?? map.listerThings.AllThings.OfType<ZombieBall>().FirstOrDefault();
		}

		static int CountThingsNear(Map map, IntVec3 center, ThingDef thingDef, float radius)
		{
			if (map == null || center.IsValid == false || thingDef == null)
				return 0;

			return GenRadial.RadialCellsAround(center, radius, true)
				.Where(cell => cell.InBounds(map))
				.SelectMany(cell => cell.GetThingList(map))
				.Count(thing => thing.def == thingDef);
		}

		static int CountThingsAt(Map map, IntVec3 cell, ThingDef thingDef)
		{
			if (map == null || cell.IsValid == false || thingDef == null)
				return 0;

			return cell.GetThingList(map).Count(thing => thing.def == thingDef);
		}

		static void ClearFilthAt(Map map, IntVec3 cell)
		{
			if (map == null || cell.IsValid == false)
				return;

			foreach (var filth in cell.GetThingList(map).OfType<Filth>().ToArray())
				filth.Destroy();
		}

		static void ClearGasAt(Map map, IntVec3 cell)
		{
			if (map == null || cell.IsValid == false)
				return;

			foreach (var gas in cell.GetThingList(map).Where(thing => thing.def.category == ThingCategory.Gas).ToArray())
				gas.Destroy();
		}

		static int CountZombieZapMotesNear(Map map, IntVec3 center, float radius)
		{
			return CountThingsNear(map, center, CustomDefs.ZombieZapA, radius)
				+ CountThingsNear(map, center, CustomDefs.ZombieZapB, radius)
				+ CountThingsNear(map, center, CustomDefs.ZombieZapC, radius)
				+ CountThingsNear(map, center, CustomDefs.ZombieZapD, radius);
		}

		static Dictionary<IntVec3, long> SnapshotPheromones(Map map, IntVec3 center, float radius)
		{
			var grid = map.GetGrid();
			return GenRadial.RadialCellsAround(center, radius, true)
				.Where(cell => cell.InBounds(map))
				.ToDictionary(cell => cell, cell => grid.GetTimestamp(cell));
		}

		static void ClearPheromones(Map map, IntVec3 center, float radius)
		{
			var grid = map.GetGrid();
			foreach (var cell in GenRadial.RadialCellsAround(center, radius, true))
				if (cell.InBounds(map))
					grid.SetTimestamp(cell, 0);
		}

		static AvoidGrid BuildAvoidGridForZombie(Map map, Zombie zombie)
		{
			var tickManager = map.GetComponent<TickManager>();
			var specs = new List<ZombieCostSpecs>
			{
				new()
				{
					position = zombie.Position,
					radius = Tools.ZombieAvoidRadius(zombie),
					maxCosts = TickManager.ZombieMaxCosts(zombie)
				}
			};
			tickManager.avoidGrid = Tools.avoider.UpdateZombiePositionsImmediately(map, specs);
			return tickManager.avoidGrid;
		}

		static int AvoidCost(AvoidGrid avoidGrid, Map map, IntVec3 cell)
		{
			return avoidGrid.GetCosts()[cell.x + cell.z * map.Size.x];
		}

		static IntVec3[] DescribePathCells(PawnPath path)
		{
			if (path?.Found != true)
				return Array.Empty<IntVec3>();
			return Enumerable.Range(0, path.NodesLeftCount).Select(path.Peek).ToArray();
		}

		static object DescribePheromoneChange(Map map, Dictionary<IntVec3, long> before, out int changedCount)
		{
			var grid = map.GetGrid();
			var changed = before
				.Select(pair => new
				{
					cell = pair.Key,
					before = pair.Value,
					after = grid.GetTimestamp(pair.Key)
				})
				.Where(item => item.after > item.before)
				.ToArray();

			changedCount = changed.Length;
			return new
			{
				changedCount = changed.Length,
				maxDelta = changed.Length == 0 ? 0 : changed.Max(item => item.after - item.before),
				changedCells = changed
					.OrderByDescending(item => item.after - item.before)
					.Take(12)
					.Select(item => new
					{
						cell = ZombieRuntimeActions.DescribeCell(item.cell),
						item.before,
						item.after,
						delta = item.after - item.before
					})
					.ToArray()
			};
		}

		static void AdvanceGameTicks(int ticks)
		{
			var tickManager = Find.TickManager;
			ZombieTicker.managers = Find.Maps.Select(map => map.GetComponent<TickManager>()).OfType<TickManager>().ToArray();
			for (var i = 0; i < ticks; i++)
				tickManager.DoSingleTick();
		}

		static (SettingsGroup values, List<SettingsKeyFrame> valuesOverTime) SnapshotZombieSettings()
		{
			return (
				ZombieSettings.Values?.MakeCopy(),
				ZombieSettings.ValuesOverTime?.Select(keyFrame => keyFrame.Copy()).ToList()
			);
		}

		static void ApplyZombieSettingsOverride(Action<SettingsGroup> configure)
		{
			if (configure == null)
				return;

			if (ZombieSettings.Values != null)
				configure(ZombieSettings.Values);

			if (ZombieSettings.ValuesOverTime != null)
				foreach (var keyFrame in ZombieSettings.ValuesOverTime)
					if (keyFrame?.values != null)
						configure(keyFrame.values);
		}

		static void RestoreZombieSettings((SettingsGroup values, List<SettingsKeyFrame> valuesOverTime) snapshot)
		{
			if (snapshot.values != null)
				ZombieSettings.Values = snapshot.values;
			if (snapshot.valuesOverTime != null)
				ZombieSettings.ValuesOverTime = snapshot.valuesOverTime;
		}

		static bool TryFindAdjacentMoveCell(Pawn pawn, out IntVec3 cell)
		{
			cell = IntVec3.Invalid;
			var map = pawn?.Map;
			if (map == null)
				return false;

			foreach (var offset in GenAdj.AdjacentCells)
			{
				var candidate = pawn.Position + offset;
				if (candidate.InBounds(map) == false)
					continue;
				if (candidate.Standable(map) == false)
					continue;
				if (pawn.HasValidDestination(candidate) == false)
					continue;
				if (candidate.GetThingList(map).Any(thing => thing is Pawn && thing != pawn))
					continue;

				cell = candidate;
				return true;
			}
			return false;
		}

		static bool TryFindAdjacentClearCell(Pawn pawn, out IntVec3 cell)
		{
			cell = IntVec3.Invalid;
			var map = pawn?.Map;
			if (map == null)
				return false;

			foreach (var offset in GenAdj.AdjacentCells)
			{
				var candidate = pawn.Position + offset;
				if (candidate.InBounds(map) == false)
					continue;
				if (candidate.Standable(map) == false)
					continue;
				if (candidate.Fogged(map))
					continue;
				if (candidate.GetFirstThing<Mineable>(map) != null)
					continue;
				if (candidate.GetThingList(map).Any(thing => thing is Pawn))
					continue;

				cell = candidate;
				return true;
			}
			return false;
		}

		static bool TryFindAdjacentBuildingCell(Pawn pawn, out IntVec3 cell)
		{
			cell = IntVec3.Invalid;
			var map = pawn?.Map;
			if (map == null)
				return false;

			foreach (var offset in GenAdj.CardinalDirections)
			{
				var candidate = pawn.Position + offset;
				if (candidate.InBounds(map) == false)
					continue;
				if (candidate.Fogged(map))
					continue;
				if (candidate.GetEdifice(map) != null)
					continue;
				if (candidate.GetThingList(map).Any(thing => thing is Pawn))
					continue;

				cell = candidate;
				return true;
			}
			return false;
		}

		static bool TryFindClearSpawnCell(Map map, IntVec3 root, float radius, out IntVec3 cell, out object error)
		{
			cell = IntVec3.Invalid;
			error = null;
			if (map == null)
			{
				error = new
				{
					success = false,
					error = "No current map is loaded."
				};
				return false;
			}

			if (root.InBounds(map) == false)
			{
				error = new
				{
					success = false,
					error = $"Cell ({root.x}, {root.z}) is outside the current map."
				};
				return false;
			}

			foreach (var candidate in GenRadial.RadialCellsAround(root, radius, true))
			{
				if (candidate.InBounds(map) == false)
					continue;
				if (candidate.Standable(map) == false)
					continue;
				if (candidate.Fogged(map))
					continue;
				if (candidate.GetThingList(map).Any(thing => thing is Pawn))
					continue;

				cell = candidate;
				return true;
			}

			error = new
			{
				success = false,
				error = $"No clear standable unfogged cell was found near ({root.x}, {root.z})."
			};
			return false;
		}

		static bool TryFindShockerFixtureCell(Map map, IntVec3 root, float radius, out IntVec3 shockerCell, out object error)
		{
			shockerCell = IntVec3.Invalid;
			error = null;
			if (map == null)
			{
				error = new
				{
					success = false,
					error = "No current map is loaded."
				};
				return false;
			}

			foreach (var candidate in GenRadial.RadialCellsAround(root, radius, true))
			{
				var clear = true;
				for (var dx = -2; dx <= 2 && clear; dx++)
				{
					for (var dz = -3; dz <= 4 && clear; dz++)
					{
						var cell = candidate + new IntVec3(dx, 0, dz);
						if (cell.InBounds(map) == false || cell.Fogged(map) || cell.Standable(map) == false)
						{
							clear = false;
							break;
						}
						if (cell.GetEdifice(map) != null || cell.GetFirstThing<Mineable>(map) != null)
						{
							clear = false;
							break;
						}
						if (cell.GetThingList(map).Any(thing => thing is Pawn))
						{
							clear = false;
							break;
						}
					}
				}

				if (clear)
				{
					shockerCell = candidate;
					return true;
				}
			}

			error = new
			{
				success = false,
				error = $"No clear zombie shocker fixture area was found near ({root.x}, {root.z})."
			};
			return false;
		}

		static bool TryFindFogRoomFixtureDoorCell(Map map, IntVec3 root, float radius, out IntVec3 doorCell, out object error)
		{
			doorCell = IntVec3.Invalid;
			error = null;
			if (map == null)
			{
				error = new
				{
					success = false,
					error = "No current map is loaded."
				};
				return false;
			}

			foreach (var candidate in GenRadial.RadialCellsAround(root, radius, true))
			{
				var fixtureRect = CellRect.FromLimits(candidate.x - 3, candidate.z - 1, candidate.x + 3, candidate.z + 6);
				if (fixtureRect.InBounds(map) == false)
					continue;

				var clear = true;
				foreach (var cell in fixtureRect.Cells)
				{
					if (cell.Fogged(map)
						|| cell.Standable(map) == false
						|| cell.GetEdifice(map) != null
						|| cell.GetFirstThing<Mineable>(map) != null
						|| cell.GetThingList(map).Any(thing => thing is Pawn))
					{
						clear = false;
						break;
					}
				}

				if (clear)
				{
					doorCell = candidate;
					return true;
				}
			}

			error = new
			{
				success = false,
				error = $"No clear fog-room fixture area was found near ({root.x}, {root.z})."
			};
			return false;
		}

		static bool TryBuildFogRoomFixture(Map map, IntVec3 root, float radius, out FogRoomFixture fixture, out object error)
		{
			fixture = default;
			error = null;
			if (TryFindFogRoomFixtureDoorCell(map, root, radius, out var doorCell, out error) == false)
				return false;

			var wallDef = ThingDefOf.Wall;
			var doorDef = ThingDefOf.Door;
			var stuffDef = ThingDefOf.WoodLog;
			var interiorRect = CellRect.FromLimits(doorCell.x - 2, doorCell.z + 1, doorCell.x + 2, doorCell.z + 5);
			var fixtureRect = CellRect.FromLimits(doorCell.x - 3, doorCell.z, doorCell.x + 3, doorCell.z + 6);
			var targetWallCell = doorCell + IntVec3.West;
			Building targetWall = null;
			foreach (var cell in fixtureRect.EdgeCells)
			{
				if (cell == doorCell)
					continue;

				var wall = ThingMaker.MakeThing(wallDef, stuffDef) as Building;
				if (wall == null)
					continue;
				GenSpawn.Spawn(wall, cell, map, WipeMode.Vanish);
				wall.SetFaction(Faction.OfPlayer);
				if (cell == targetWallCell)
					targetWall = wall;
			}

			var door = ThingMaker.MakeThing(doorDef, stuffDef) as Building_Door;
			if (door == null || targetWall == null)
			{
				error = new
				{
					success = false,
					targetWallCell = ZombieRuntimeActions.DescribeCell(targetWallCell),
					error = "Could not create the fog-room fixture door or target wall."
				};
				return false;
			}

			GenSpawn.Spawn(door, doorCell, map, WipeMode.Vanish);
			door.SetFaction(Faction.OfPlayer);
			map.regionAndRoomUpdater.RebuildAllRegionsAndRooms();

			fixture = new FogRoomFixture
			{
				doorCell = doorCell,
				targetWallCell = targetWallCell,
				interiorRect = interiorRect,
				door = door,
				targetWall = targetWall
			};
			return true;
		}

		static bool TryFindClearBuildingFootprint(Map map, ThingDef thingDef, IntVec3 root, float radius, out IntVec3 cell, out object error)
		{
			cell = IntVec3.Invalid;
			error = null;
			if (map == null)
			{
				error = new
				{
					success = false,
					error = "No current map is loaded."
				};
				return false;
			}

			foreach (var candidate in GenRadial.RadialCellsAround(root, radius, true))
			{
				if (candidate.InBounds(map) == false || candidate.Fogged(map))
					continue;

				var occupied = false;
				foreach (var footprintCell in GenAdj.OccupiedRect(candidate, Rot4.North, thingDef.size))
				{
					if (footprintCell.InBounds(map) == false
						|| footprintCell.Fogged(map)
						|| footprintCell.Standable(map) == false
						|| footprintCell.GetEdifice(map) != null
						|| footprintCell.GetFirstThing<Mineable>(map) != null
						|| footprintCell.GetThingList(map).Any(thing => thing is Pawn))
					{
						occupied = true;
						break;
					}
				}

				if (occupied)
					continue;

				cell = candidate;
				return true;
			}

			error = new
			{
				success = false,
				error = $"No clear footprint for {thingDef?.defName ?? "building"} was found near ({root.x}, {root.z})."
			};
			return false;
		}

		static bool TryFindWallPushFixtureCells(Map map, IntVec3 root, float radius, out IntVec3 zombieCell, out IntVec3 wallCell, out IntVec3 destinationCell, out object error)
		{
			zombieCell = IntVec3.Invalid;
			wallCell = IntVec3.Invalid;
			destinationCell = IntVec3.Invalid;
			error = null;
			if (map == null)
			{
				error = new
				{
					success = false,
					error = "No current map is loaded."
				};
				return false;
			}

			var directions = new[] { IntVec3.East, IntVec3.West, IntVec3.North, IntVec3.South };
			foreach (var candidate in GenRadial.RadialCellsAround(root, radius, true))
			{
				if (candidate.InBounds(map) == false || candidate.Fogged(map) || candidate.Standable(map) == false)
					continue;
				if (candidate.GetEdifice(map) != null || candidate.GetFirstThing<Mineable>(map) != null)
					continue;
				if (candidate.GetThingList(map).Any(thing => thing is Pawn))
					continue;

				foreach (var direction in directions)
				{
					var wall = candidate + direction;
					var destination = wall + direction;
					if (wall.InBounds(map) == false || destination.InBounds(map) == false)
						continue;
					if (wall.Fogged(map) || destination.Fogged(map))
						continue;
					if (wall.GetEdifice(map) != null || wall.GetFirstThing<Mineable>(map) != null)
						continue;
					if (wall.GetThingList(map).Any(thing => thing is Pawn))
						continue;
					if (destination.Standable(map) == false)
						continue;
					if (destination.GetEdifice(map) != null || destination.GetFirstThing<Mineable>(map) != null)
						continue;
					if (destination.GetThingList(map).Any(thing => thing is Pawn))
						continue;

					var otherWallCount = 0;
					foreach (var adjacentDirection in directions)
					{
						var adjacent = candidate + adjacentDirection;
						if (adjacent == wall)
							continue;
						if (adjacent.InBounds(map) && adjacent.GetThingList(map).Any(thing => thing is Pawn))
						{
							otherWallCount = int.MaxValue;
							break;
						}
						if (adjacent.InBounds(map) && adjacent.IsWallOrDoor(map))
							otherWallCount++;
					}
					if (otherWallCount > 0)
						continue;

					zombieCell = candidate;
					wallCell = wall;
					destinationCell = destination;
					return true;
				}
			}

			error = new
			{
				success = false,
				error = $"No clear wall-push fixture triplet was found near ({root.x}, {root.z})."
			};
			return false;
		}

		static Building SpawnWoodWall(Map map, IntVec3 cell)
		{
			var wall = ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.WoodLog) as Building;
			if (wall == null)
				return null;
			GenSpawn.Spawn(wall, cell, map, WipeMode.Vanish);
			wall.SetFaction(Faction.OfPlayer);
			return wall;
		}

		static bool TryCreateWallPushFixture(Map map, IntVec3 root, float radius, out WallPushFixture fixture, out object error)
		{
			fixture = null;
			if (TryFindWallPushFixtureCells(map, root, radius, out var zombieCell, out var wallCell, out var destinationCell, out error) == false)
				return false;

			var wall = SpawnWoodWall(map, wallCell);
			if (wall == null)
			{
				error = new
				{
					success = false,
					error = "Could not create test wall."
				};
				return false;
			}

			var zombie = ZombieRuntimeActions.SpawnZombie(zombieCell, map, ZombieType.Normal, true);
			if (zombie == null)
			{
				error = new
				{
					success = false,
					error = "Could not spawn wall-push test zombie.",
					zombieCell = ZombieRuntimeActions.DescribeCell(zombieCell),
					wallCell = ZombieRuntimeActions.DescribeCell(wallCell),
					destinationCell = ZombieRuntimeActions.DescribeCell(destinationCell)
				};
				return false;
			}

			fixture = new WallPushFixture
			{
				zombie = zombie,
				wall = wall,
				zombieCell = zombieCell,
				wallCell = wallCell,
				destinationCell = destinationCell
			};
			return true;
		}

		static void ClearWallPushGridNeighborhood(Map map, IntVec3 zombieCell)
		{
			var grid = map.GetGrid();
			var nearbyCells = new[] { zombieCell, zombieCell + IntVec3.East, zombieCell + IntVec3.West, zombieCell + IntVec3.North, zombieCell + IntVec3.South };
			foreach (var cell in nearbyCells)
			{
				if (cell.InBounds(map) == false)
					continue;
				var current = grid.GetZombieCount(cell);
				if (current != 0)
					grid.ChangeZombieCount(cell, -current);
			}
		}

		static void PrepareWallPushZombie(Map map, Zombie zombie, IntVec3 zombieCell)
		{
			var tickManager = map.GetComponent<TickManager>();
			tickManager?.allZombiesCached?.RemoveWhere(cached => cached == null || cached.Destroyed || cached.Spawned == false || cached.Dead);
			_ = tickManager?.allZombiesCached?.Add(zombie);

			zombie.pather?.StopDead();
			zombie.jobs?.EndCurrentJob(JobCondition.InterruptForced);
			zombie.state = ZombieState.Wandering;
			zombie.wallPushProgress = -1f;
			zombie.wallPushStart = Vector3.zero;
			zombie.wallPushDestination = Vector3.zero;
			zombie.wallPushCooldown = 0;
			zombie.lastGotoPosition = zombieCell;
			zombie.Rotation = Rot4.South;
		}

		static void ClearThrottleKey(string key)
		{
			var field = typeof(Tools).GetField("nextExecutions", BindingFlags.Static | BindingFlags.NonPublic);
			if (field?.GetValue(null) is Dictionary<string, float> nextExecutions)
				_ = nextExecutions.Remove(key);
		}


		static object DescribeVector(Vector3 vector)
		{
			return new
			{
				x = vector.x,
				y = vector.y,
				z = vector.z
			};
		}

	}
}
