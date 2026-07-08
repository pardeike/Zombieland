using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public static class ContaminationStorageRange
	{
		const string SaveKey = "zombielandAllowedContaminationRange";
		const float SliderHeight = 32f;
		const float SliderGap = 5f;
		public const float SliderRowHeight = SliderHeight + SliderGap;

		static readonly ConditionalWeakTable<StorageSettings, RangeHolder> ranges = new();
		static readonly ConditionalWeakTable<ThingFilter, StorageSettings> filterOwners = new();
		static readonly System.Reflection.FieldInfo inspectTabSizeField = AccessTools.Field(typeof(InspectTabBase), "size");

		public static readonly FloatRange DefaultRange = new(0f, 1f);

		sealed class RangeHolder
		{
			public FloatRange range = DefaultRange;
		}

		public static void Register(StorageSettings settings)
		{
			if (settings?.filter == null)
				return;
			_ = ranges.GetOrCreateValue(settings);
			filterOwners.Remove(settings.filter);
			filterOwners.Add(settings.filter, settings);
		}

		public static FloatRange GetRange(StorageSettings settings)
			=> settings == null ? DefaultRange : Normalize(ranges.GetOrCreateValue(settings).range);

		public static void SetRange(StorageSettings settings, FloatRange range, bool notifyChanged = true)
		{
			if (settings == null)
				return;
			range = Normalize(range);
			var holder = ranges.GetOrCreateValue(settings);
			if (holder.range == range)
				return;
			holder.range = range;
			Register(settings);
			if (notifyChanged)
				settings.owner?.Notify_SettingsChanged();
		}

		public static void ExposeData(StorageSettings settings)
		{
			if (settings == null)
				return;
			Register(settings);
			var holder = ranges.GetOrCreateValue(settings);
			var range = holder.range;
			Scribe_Values.Look(ref range, SaveKey, DefaultRange);
			holder.range = Normalize(range);
			Register(settings);
		}

		public static void CopyRange(StorageSettings target, StorageSettings source)
		{
			if (target == null || source == null)
				return;
			SetRange(target, GetRange(source), notifyChanged: false);
			Register(target);
		}

		public static bool Allows(StorageSettings settings, Thing thing)
		{
			if (settings == null || thing == null)
				return true;
			var range = GetRange(settings);
			if (range == DefaultRange)
				return true;
			var contamination = Mathf.Clamp01(thing.GetContamination(includeHoldings: true));
			contamination = GenMath.RoundedHundredth(contamination);
			return range.IncludesEpsilon(contamination);
		}

		public static bool DrawRangeForFilter(ref float y, float width, ThingFilter filter)
		{
			if (TryGetFilterOwner(filter, out var settings) == false)
				return false;

			var sliderRect = new Rect(20f, y, width - 20f, SliderHeight);
			var range = GetRange(settings);
			var previous = range;
			Widgets.FloatRange(
				sliderRect,
				RuntimeHelpers.GetHashCode(filter) ^ 0x5A1C0DE,
				ref range,
				0f,
				1f,
				"ZombielandStorageContaminationRange",
				ToStringStyle.PercentZero,
				0f,
				GameFont.Small,
				null,
				0.01f);
			if (range != previous)
				SetRange(settings, range);
			y += SliderRowHeight;
			Text.Font = GameFont.Small;
			return true;
		}

		public static bool CanDrawRangeForFilter(ThingFilter filter)
			=> TryGetFilterOwner(filter, out _);

		public static bool CanResizeStorageTab()
			=> Constants.CONTAMINATION && inspectTabSizeField != null;

		public static void IncreaseStorageTabHeight(InspectTabBase tab)
		{
			if (tab == null || inspectTabSizeField == null)
				return;
			var size = (Vector2)inspectTabSizeField.GetValue(tab);
			size.y += SliderRowHeight;
			inspectTabSizeField.SetValue(tab, size);
		}

		static FloatRange Normalize(FloatRange range)
		{
			range.min = Mathf.Clamp01(GenMath.RoundedHundredth(range.min));
			range.max = Mathf.Clamp01(GenMath.RoundedHundredth(range.max));
			if (range.min > range.max)
				(range.min, range.max) = (range.max, range.min);
			return range;
		}

		static bool TryGetFilterOwner(ThingFilter filter, out StorageSettings settings)
		{
			settings = null;
			if (filter == null || filterOwners.TryGetValue(filter, out settings) == false)
				return false;
			return true;
		}
	}

	[HarmonyPatch(typeof(StorageSettings), MethodType.Constructor)]
	[HarmonyPatch(new Type[] { })]
	static class ContaminationStorageSettings_DefaultConstructor_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Postfix(StorageSettings __instance)
			=> ContaminationStorageRange.Register(__instance);
	}

	[HarmonyPatch(typeof(StorageSettings), MethodType.Constructor)]
	[HarmonyPatch(new[] { typeof(IStoreSettingsParent) })]
	static class ContaminationStorageSettings_OwnerConstructor_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Postfix(StorageSettings __instance)
			=> ContaminationStorageRange.Register(__instance);
	}

	[HarmonyPatch(typeof(StorageSettings), nameof(StorageSettings.ExposeData))]
	static class ContaminationStorageSettings_ExposeData_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Postfix(StorageSettings __instance)
			=> ContaminationStorageRange.ExposeData(__instance);
	}

	[HarmonyPatch(typeof(StorageSettings), nameof(StorageSettings.CopyFrom))]
	static class ContaminationStorageSettings_CopyFrom_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Postfix(StorageSettings __instance, StorageSettings other)
			=> ContaminationStorageRange.CopyRange(__instance, other);
	}

	[HarmonyPatch(typeof(StorageSettings), nameof(StorageSettings.AllowedToAccept), typeof(Thing))]
	static class ContaminationStorageSettings_AllowedToAccept_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Postfix(StorageSettings __instance, Thing t, ref bool __result)
		{
			if (__result)
				__result = ContaminationStorageRange.Allows(__instance, t);
		}
	}

	[HarmonyPatch(typeof(ThingFilterUI), nameof(ThingFilterUI.DoThingFilterConfigWindow))]
	static class ContaminationThingFilterUI_DoThingFilterConfigWindow_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var codes = instructions.ToList();
			var anomalyActive = AccessTools.PropertyGetter(typeof(ModsConfig), nameof(ModsConfig.AnomalyActive));
			var drawMentalBreak = AccessTools.Method(
				typeof(ThingFilterUI),
				"DrawMentalBreakFilterConfig",
				new[] { typeof(float).MakeByRefType(), typeof(float), typeof(ThingFilter) });
			var rectWidth = AccessTools.PropertyGetter(typeof(Rect), nameof(Rect.width));
			var drawContamination = AccessTools.Method(typeof(ContaminationStorageRange), nameof(ContaminationStorageRange.DrawRangeForFilter));

			if (anomalyActive == null || drawMentalBreak == null || rectWidth == null || drawContamination == null)
				throw new MissingMethodException("Cannot resolve ThingFilterUI contamination storage range UI patch members.");

			var insertAt = codes.FindIndex(code => code.Calls(anomalyActive));
			if (insertAt < 0)
				throw new MissingMethodException($"Cannot find {anomalyActive.FullDescription()} in ThingFilterUI.DoThingFilterConfigWindow.");

			var mentalBreakCall = codes.FindIndex(insertAt, code => code.Calls(drawMentalBreak));
			if (mentalBreakCall < 4 || codes[mentalBreakCall - 2].Calls(rectWidth) == false)
				throw new MissingMethodException($"Cannot find the ThingFilterUI slider draw sequence before {drawMentalBreak.FullDescription()}.");

			var inserted = new List<CodeInstruction>
			{
				CleanCopy(codes[mentalBreakCall - 4]),
				CleanCopy(codes[mentalBreakCall - 3]),
				CleanCopy(codes[mentalBreakCall - 2]),
				CleanCopy(codes[mentalBreakCall - 1]),
				new(OpCodes.Call, drawContamination),
				new(OpCodes.Pop)
			};

			inserted[0].labels.AddRange(codes[insertAt].labels);
			codes[insertAt].labels.Clear();
			codes.InsertRange(insertAt, inserted);
			return codes;
		}

		static CodeInstruction CleanCopy(CodeInstruction instruction)
		{
			var copy = new CodeInstruction(instruction);
			copy.labels.Clear();
			copy.blocks.Clear();
			return copy;
		}
	}

	[HarmonyPatch(typeof(ITab_Storage), MethodType.Constructor)]
	static class ContaminationITabStorage_Constructor_Patch
	{
		static bool Prepare() => ContaminationStorageRange.CanResizeStorageTab();

		static void Postfix(ITab_Storage __instance)
			=> ContaminationStorageRange.IncreaseStorageTabHeight(__instance);
	}
}
