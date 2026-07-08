using HarmonyLib;
using RimWorld;
using System;
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
			var contamination = Mathf.Clamp01(ComparableThing(thing)?.GetContamination() ?? 0f);
			contamination = GenMath.RoundedHundredth(contamination);
			return range.IncludesEpsilon(contamination);
		}

		public static bool DrawRangeForFilter(ref float y, float width, ThingFilter filter)
		{
			if (filter == null || filterOwners.TryGetValue(filter, out var settings) == false)
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

		public static Thing ComparableThing(Thing thing)
			=> thing is MinifiedThing minifiedThing ? minifiedThing.InnerThing : thing;

		static FloatRange Normalize(FloatRange range)
		{
			range.min = Mathf.Clamp01(GenMath.RoundedHundredth(range.min));
			range.max = Mathf.Clamp01(GenMath.RoundedHundredth(range.max));
			if (range.min > range.max)
				(range.min, range.max) = (range.max, range.min);
			return range;
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

	[HarmonyPatch(typeof(ThingFilterUI), "DrawQualityFilterConfig")]
	static class ContaminationThingFilterUI_DrawQualityFilterConfig_Patch
	{
		static bool Prepare() => Constants.CONTAMINATION;

		static void Postfix(ref float y, float width, ThingFilter filter)
			=> ContaminationStorageRange.DrawRangeForFilter(ref y, width, filter);
	}

	[HarmonyPatch(typeof(ITab_Storage), MethodType.Constructor)]
	static class ContaminationITabStorage_Constructor_Patch
	{
		static bool Prepare() => ContaminationStorageRange.CanResizeStorageTab();

		static void Postfix(ITab_Storage __instance)
			=> ContaminationStorageRange.IncreaseStorageTabHeight(__instance);
	}
}
