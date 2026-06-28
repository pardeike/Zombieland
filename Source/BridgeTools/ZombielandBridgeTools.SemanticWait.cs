using RimBridgeServer.Sdk;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Verse;

namespace ZombieLand
{
	public sealed partial class ZombielandBridgeTools
	{
		sealed class SemanticTickSample
		{
			public int ticksGame { get; set; }
			public int ticksAdvanced { get; set; }
			public bool targetFound { get; set; }
			public bool destroyed { get; set; }
			public string targetId { get; set; }
			public string thingDef { get; set; }
			public string label { get; set; }
			public object position { get; set; }
			public int? hitPoints { get; set; }
			public float? injurySeverity { get; set; }
			public int? stackCount { get; set; }
			public float? contamination { get; set; }
			public string currentJob { get; set; }
			public string currentJobReport { get; set; }
			public int? thingCount { get; set; }
			public int zombieCount { get; set; }
			public string signature { get; set; }
		}

		[Tool("zombieland/wait_for_semantic_change", Description = "Advance bounded game ticks until a generic semantic condition changes for a thing, pawn, cell, or map.")]
		public static object WaitForSemanticChange(
			[ToolParameter(Description = "Condition: anyChange, hitPointsChanged, damaged, contaminationChanged, positionChanged, jobChanged, destroyed, thingCountChanged, or zombieCountChanged.", Required = false, DefaultValue = "anyChange")] string condition = "anyChange",
			[ToolParameter(Description = "Target kind: thing, pawn, cell, or map.", Required = false, DefaultValue = "map")] string targetKind = "map",
			[ToolParameter(Description = "Optional thing/pawn id, ThingID, label, or short name for thing/pawn target kinds.", Required = false, DefaultValue = "")] string targetId = "",
			[ToolParameter(Description = "Cell X for cell targets.", Required = false, DefaultValue = -1)] int cellX = -1,
			[ToolParameter(Description = "Cell Z for cell targets.", Required = false, DefaultValue = -1)] int cellZ = -1,
			[ToolParameter(Description = "Optional ThingDef defName for thing-count sampling.", Required = false, DefaultValue = "")] string thingDefName = "",
			[ToolParameter(Description = "Maximum game ticks to advance; clamped to 0..20000.", Required = false, DefaultValue = 600)] int maxTicks = 600,
			[ToolParameter(Description = "Ticks between condition samples; clamped to 1..1000.", Required = false, DefaultValue = 1)] int sampleEveryTicks = 1,
			[ToolParameter(Description = "Maximum intermediate samples to return; final sample is always returned separately.", Required = false, DefaultValue = 24)] int maxSamples = 24)
		{
			var map = CurrentMap;
			if (map == null)
			{
				return new
				{
					success = false,
					error = "No current map is loaded."
				};
			}

			var targetKindKey = NormalizeSemanticKey(targetKind);
			var conditionKey = NormalizeSemanticKey(condition);
			var clampedMaxTicks = Math.Max(0, Math.Min(maxTicks, 20000));
			var clampedSampleEveryTicks = Math.Max(1, Math.Min(sampleEveryTicks, 1000));
			var clampedMaxSamples = Math.Max(0, Math.Min(maxSamples, 100));
			var cell = new IntVec3(cellX, 0, cellZ);
			var thingDef = string.IsNullOrWhiteSpace(thingDefName) ? null : DefDatabase<ThingDef>.GetNamedSilentFail(thingDefName.Trim());

			if (ValidateSemanticWaitRequest(map, targetKindKey, conditionKey, targetId, cell, thingDefName, thingDef, out var validationError) == false)
			{
				return new
				{
					success = false,
					error = validationError
				};
			}

			var initial = SampleSemanticWaitState(map, targetKindKey, targetId, cell, thingDef, 0);
			if ((targetKindKey == "thing" || targetKindKey == "pawn") && initial.targetFound == false)
			{
				return new
				{
					success = false,
					error = $"No spawned {targetKindKey} matched '{targetId}'."
				};
			}

			var samples = new List<SemanticTickSample>();
			var ticksAdvanced = 0;
			var matched = false;
			var matchError = default(string);
			var final = initial;

			while (ticksAdvanced < clampedMaxTicks)
			{
				var step = Math.Min(clampedSampleEveryTicks, clampedMaxTicks - ticksAdvanced);
				AdvanceGameTicks(step);
				ticksAdvanced += step;
				final = SampleSemanticWaitState(map, targetKindKey, targetId, cell, thingDef, ticksAdvanced);
				if (samples.Count < clampedMaxSamples)
					samples.Add(final);
				if (SemanticConditionMatched(conditionKey, initial, final, out matchError))
				{
					matched = true;
					break;
				}
				if (matchError != null)
					break;
			}

			return new
			{
				success = matched && matchError == null,
				conditionMatched = matched,
				error = matchError,
				condition = conditionKey,
				targetKind = targetKindKey,
				targetId,
				cell = targetKindKey == "cell" ? ZombieRuntimeActions.DescribeCell(cell) : null,
				thingDef = thingDef?.defName,
				startTick = initial.ticksGame,
				endTick = final.ticksGame,
				ticksAdvanced,
				maxTicks = clampedMaxTicks,
				sampleEveryTicks = clampedSampleEveryTicks,
				initial,
				final,
				samples = samples.ToArray()
			};
		}

		static bool ValidateSemanticWaitRequest(Map map, string targetKind, string condition, string targetId, IntVec3 cell, string thingDefName, ThingDef thingDef, out string error)
		{
			error = null;
			var validTargetKinds = new HashSet<string> { "thing", "pawn", "cell", "map" };
			if (validTargetKinds.Contains(targetKind) == false)
			{
				error = "Target kind must be thing, pawn, cell, or map.";
				return false;
			}

			var validConditions = new HashSet<string>
			{
				"anychange",
				"hitpointschanged",
				"damaged",
				"contaminationchanged",
				"positionchanged",
				"jobchanged",
				"destroyed",
				"thingcountchanged",
				"zombiecountchanged"
			};
			if (validConditions.Contains(condition) == false)
			{
				error = "Condition must be anyChange, hitPointsChanged, damaged, contaminationChanged, positionChanged, jobChanged, destroyed, thingCountChanged, or zombieCountChanged.";
				return false;
			}

			if ((targetKind == "thing" || targetKind == "pawn") && string.IsNullOrWhiteSpace(targetId))
			{
				error = "Thing and pawn targets require targetId.";
				return false;
			}
			if (targetKind == "cell" && (cell.IsValid == false || cell.InBounds(map) == false))
			{
				error = "Cell targets require in-bounds cellX and cellZ.";
				return false;
			}
			if (string.IsNullOrWhiteSpace(thingDefName) == false && thingDef == null)
			{
				error = $"ThingDef '{thingDefName}' was not found.";
				return false;
			}
			return true;
		}

		static SemanticTickSample SampleSemanticWaitState(Map map, string targetKind, string targetId, IntVec3 cell, ThingDef thingDef, int ticksAdvanced)
		{
			var sample = new SemanticTickSample
			{
				ticksGame = Find.TickManager.TicksGame,
				ticksAdvanced = ticksAdvanced,
				zombieCount = CurrentZombies(map).Length
			};

			if (targetKind == "thing" || targetKind == "pawn")
			{
				var thing = targetKind == "pawn"
					? FindSemanticPawn(map, targetId)
					: FindSemanticThing(map, targetId);
				var pawn = thing as Pawn;
				sample.targetFound = thing != null;
				sample.destroyed = thing?.Destroyed ?? true;
				sample.targetId = ZombieRuntimeActions.StableThingId(thing);
				sample.thingDef = thing?.def?.defName;
				sample.label = thing?.LabelCap.ToString();
				sample.position = thing?.Spawned == true ? ZombieRuntimeActions.DescribeCell(thing.Position) : null;
				sample.hitPoints = thing?.HitPoints;
				sample.injurySeverity = pawn?.health?.hediffSet?.hediffs.OfType<Hediff_Injury>().Sum(hediff => hediff.Severity);
				sample.stackCount = thing?.stackCount;
				sample.contamination = thing == null ? null : thing.GetContamination(includeHoldings: true);
				sample.currentJob = pawn?.CurJobDef?.defName;
				sample.currentJobReport = pawn?.CurJob?.GetReport(pawn);
			}
			else if (targetKind == "cell")
			{
				var things = cell.GetThingList(map);
				sample.targetFound = true;
				sample.position = ZombieRuntimeActions.DescribeCell(cell);
				sample.contamination = map.GetContamination(cell, true);
				sample.thingCount = thingDef == null ? things.Count : things.Count(thing => thing.def == thingDef);
			}
			else
			{
				sample.targetFound = true;
				sample.thingCount = thingDef == null
					? map.listerThings.AllThings.Count
					: map.listerThings.ThingsOfDef(thingDef).Count;
			}

			sample.signature = SemanticWaitSignature(sample);
			return sample;
		}

		static Thing FindSemanticThing(Map map, string target)
		{
			if (map == null || string.IsNullOrWhiteSpace(target))
				return null;

			var query = target.Trim();
			return SemanticThings(map).FirstOrDefault(thing =>
				string.Equals(ZombieRuntimeActions.StableThingId(thing), query, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(thing.ThingID, query, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(thing.LabelCap.ToString(), query, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(thing.LabelShortCap, query, StringComparison.OrdinalIgnoreCase)
				|| string.Equals(thing.def?.defName, query, StringComparison.OrdinalIgnoreCase));
		}

		static Pawn FindSemanticPawn(Map map, string target)
		{
			if (ZombieRuntimeActions.TryFindPawn(map, target, out var pawn, out _))
				return pawn;
			return FindSemanticThing(map, target) as Pawn;
		}

		static IEnumerable<Thing> SemanticThings(Map map)
		{
			var seen = new HashSet<int>();
			foreach (var thing in map.listerThings.AllThings.Concat(map.mapPawns.AllPawnsSpawned.Cast<Thing>()))
			{
				if (thing == null || seen.Add(thing.thingIDNumber) == false)
					continue;
				yield return thing;
			}
		}

		static bool SemanticConditionMatched(string condition, SemanticTickSample initial, SemanticTickSample current, out string error)
		{
			error = null;
			switch (condition)
			{
				case "anychange":
					return string.Equals(initial.signature, current.signature, StringComparison.Ordinal) == false;
				case "hitpointschanged":
					return initial.hitPoints != current.hitPoints;
				case "damaged":
					return (initial.hitPoints.HasValue && current.hitPoints.HasValue && current.hitPoints.Value < initial.hitPoints.Value)
						|| (initial.injurySeverity.HasValue && current.injurySeverity.HasValue && current.injurySeverity.Value > initial.injurySeverity.Value + 0.0001f);
				case "contaminationchanged":
					return NullableFloatChanged(initial.contamination, current.contamination);
				case "positionchanged":
					return string.Equals(CellSignature(initial.position), CellSignature(current.position), StringComparison.Ordinal) == false;
				case "jobchanged":
					return string.Equals(initial.currentJob, current.currentJob, StringComparison.Ordinal) == false;
				case "destroyed":
					return initial.targetFound && (current.targetFound == false || current.destroyed);
				case "thingcountchanged":
					return initial.thingCount != current.thingCount;
				case "zombiecountchanged":
					return initial.zombieCount != current.zombieCount;
				default:
					error = $"Unsupported condition '{condition}'.";
					return false;
			}
		}

		static string SemanticWaitSignature(SemanticTickSample sample)
			=> string.Join("|", new[]
			{
				sample.targetFound.ToString(),
				sample.destroyed.ToString(),
				sample.targetId ?? "",
				sample.thingDef ?? "",
				CellSignature(sample.position),
				sample.hitPoints?.ToString(CultureInfo.InvariantCulture) ?? "",
				sample.injurySeverity?.ToString("R", CultureInfo.InvariantCulture) ?? "",
				sample.stackCount?.ToString(CultureInfo.InvariantCulture) ?? "",
				sample.contamination?.ToString("R", CultureInfo.InvariantCulture) ?? "",
				sample.currentJob ?? "",
				sample.thingCount?.ToString(CultureInfo.InvariantCulture) ?? "",
				sample.zombieCount.ToString(CultureInfo.InvariantCulture)
			});

		static bool NullableFloatChanged(float? a, float? b)
		{
			if (a.HasValue != b.HasValue)
				return true;
			if (a.HasValue == false)
				return false;
			return Math.Abs(a.Value - b.Value) > 0.0001f;
		}

		static string CellSignature(object cell)
		{
			if (cell == null)
				return "";
			var type = cell.GetType();
			var x = type.GetProperty("x")?.GetValue(cell);
			var z = type.GetProperty("z")?.GetValue(cell);
			return x == null || z == null ? cell.ToString() : $"{x},{z}";
		}

		static string NormalizeSemanticKey(string value)
			=> (value ?? "").Replace("_", "").Replace("-", "").Replace(" ", "").Trim().ToLowerInvariant();
	}
}
