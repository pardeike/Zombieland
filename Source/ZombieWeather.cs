using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	// https://www.desmos.com/calculator/obcl83g1hz

	[StaticConstructorOnStartup]
	public class ZombieWeather : MapComponent
	{
		static readonly Texture2D ForecastBackground = Tools.LoadTexture("Forecast", true);
		static readonly Color ZombieFreeEventSpanColor = new(0.18f, 0.85f, 0.30f, 0.42f);

		sealed class ZombieFreeEventDisplaySpan
		{
			public int startTick;
			public int endTick;
		}

		const float p = 3f; // v-stretch
		float f1 = 1, f2 = 2, f3 = 3, f4 = 4;
		float o1 = 1, o2 = 2, o3 = 3, o4 = 4;

		public ZombieWeather(Map map) : base(map)
		{
			f1 = Rand.Range(1f, 2f);
			f2 = Rand.Range(2f, 3f);
			f3 = Rand.Range(3f, 4f);
			f4 = Rand.Range(4f, 5f);
			o1 = Rand.Range(0f, 4f);
			o2 = Rand.Range(0f, 4f);
			o3 = Rand.Range(0f, 4f);
			o4 = Rand.Range(0f, 4f);
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref f1, "f1");
			Scribe_Values.Look(ref f2, "f2");
			Scribe_Values.Look(ref f3, "f3");
			Scribe_Values.Look(ref f4, "f4");
			Scribe_Values.Look(ref o1, "o1");
			Scribe_Values.Look(ref o2, "o2");
			Scribe_Values.Look(ref o3, "o3");
			Scribe_Values.Look(ref o4, "o4");
		}

		public static float GetThreatLevel(Map map)
		{
			if (ZombieFreeEventManager.IsActiveNow())
				return 0f;

			return ZombieSettings.Values.useDynamicThreatLevel
				? map?.GetComponent<ZombieWeather>()?.GetFactorForTicks(GenTicks.TicksAbs) ?? 1f
				: 1f;
		}

		public static float GetThreatLevelIgnoringZombieFreeEvent(Map map)
		{
			return ZombieSettings.Values.useDynamicThreatLevel
				? map?.GetComponent<ZombieWeather>()?.GetBaseFactorForTicks(GenTicks.TicksAbs) ?? 1f
				: 1f;
		}

		public float GetFactorForTicks(int t)
		{
			if (ZombieFreeEventManager.IsActiveAtAbsTick(t))
				return 0f;
			return GetBaseFactorForTicks(t);
		}

		public float GetBaseFactorForTicks(int t)
		{
			var ticks = t - GenTicks.TicksAbs + GenTicks.TicksGame;
			var settings = ZombieSettings.ValuesAtGameTick(ticks);
			if (ticks / (float)GenDate.TicksPerDay <= settings.daysBeforeZombiesCome)
				return 0f;

			var tm = map?.GetComponent<TickManager>();
			if (tm == null || tm.NewMapZombieDelay(ticks))
				return 0f;

			var currentDay = t / (float)GenDate.TicksPerDay;
			var x = currentDay;
			var m = settings.dynamicThreatSmoothness;
			var n = settings.dynamicThreatStretch;
			var val = 0
				+ Mathf.Sin(f1 * x / (m + Mathf.Sin(x / f2 + o3) / n) + o1)
				+ Mathf.Sin(f2 * x / (m + Mathf.Sin(x / f3 + o4) / n) + o2)
				+ Mathf.Sin(f3 * x / (m + Mathf.Sin(x / f4 + o1) / n) + o3)
				+ Mathf.Sin(f4 * x / (m + Mathf.Sin(x / f1 + o2) / n) + o4);
			return Mathf.Clamp01((settings.threatScale / 2f + val) / p);
		}

		static List<ZombieFreeEventDisplaySpan> ZombieFreeEventSpansForAbsRange(int absStartTick, int absEndTick)
		{
			var result = new List<ZombieFreeEventDisplaySpan>();
			if (absEndTick <= absStartTick)
				return result;

			var windows = ZombieFreeEventManager.WindowsForAbsRange(absStartTick, absEndTick);
			for (var i = 0; i < windows.Count; i++)
			{
				var window = windows[i];
				var windowStart = window.startTick == 0
					? absStartTick
					: ZombieFreeEventManager.AbsTickForGameTick(window.startTick);
				var windowEnd = ZombieFreeEventManager.AbsTickForGameTick(window.endTick);
				var start = Mathf.Max(absStartTick, windowStart);
				var end = Mathf.Min(absEndTick, windowEnd);
				if (end <= start)
					continue;

				result.Add(new ZombieFreeEventDisplaySpan
				{
					startTick = start,
					endTick = end
				});
			}
			NormalizeZombieFreeEventSpans(result);
			return result;
		}

		static List<ZombieFreeEventDisplaySpan> ZombieFreeEventSpansForGameRange(int gameStartTick, int gameEndTick, List<ZombieFreeEventWindow> windows)
		{
			var result = new List<ZombieFreeEventDisplaySpan>();
			if (gameEndTick <= gameStartTick || windows == null)
				return result;

			for (var i = 0; i < windows.Count; i++)
			{
				var window = windows[i];
				if (window.Overlaps(gameStartTick, gameEndTick) == false)
					continue;

				var start = window.startTick == 0
					? gameStartTick
					: Mathf.Max(gameStartTick, window.startTick);
				var end = Mathf.Min(gameEndTick, window.endTick);
				if (end <= start)
					continue;

				result.Add(new ZombieFreeEventDisplaySpan
				{
					startTick = start,
					endTick = end
				});
			}
			NormalizeZombieFreeEventSpans(result);
			return result;
		}

		static void NormalizeZombieFreeEventSpans(List<ZombieFreeEventDisplaySpan> spans)
		{
			if (spans == null || spans.Count <= 1)
				return;

			spans.Sort((a, b) =>
			{
				var result = a.startTick.CompareTo(b.startTick);
				return result != 0 ? result : a.endTick.CompareTo(b.endTick);
			});

			for (var i = 1; i < spans.Count;)
			{
				var previous = spans[i - 1];
				var current = spans[i];
				if (current.startTick <= previous.endTick)
				{
					previous.endTick = Mathf.Max(previous.endTick, current.endTick);
					spans.RemoveAt(i);
					continue;
				}
				i++;
			}
		}

		static bool IsInZombieFreeEventSpan(int absTick, List<ZombieFreeEventDisplaySpan> spans)
		{
			for (var i = 0; i < spans.Count; i++)
				if (absTick >= spans[i].startTick && absTick < spans[i].endTick)
					return true;
			return false;
		}

		static void DrawZombieFreeEventSpans(Rect graphRect, int absStartTick, int absEndTick, List<ZombieFreeEventDisplaySpan> spans)
		{
			var ticks = absEndTick - absStartTick;
			if (ticks <= 0)
				return;

			for (var i = 0; i < spans.Count; i++)
			{
				var span = spans[i];
				var x1 = graphRect.x + graphRect.width * (span.startTick - absStartTick) / ticks;
				var x2 = graphRect.x + graphRect.width * (span.endTick - absStartTick) / ticks;
				Widgets.DrawBoxSolid(new Rect(x1, graphRect.y, Mathf.Max(1f, x2 - x1), graphRect.height), ZombieFreeEventSpanColor);
			}
		}

		public (float, float) GetFactorRangeFor()
		{
			var t = GenTicks.TicksAbs;
			t -= t % GenDate.TicksPerDay;
			var d = GenDate.TicksPerDay / 4;
			t += d / 2;
			var min = float.MaxValue;
			var minTicks = -1;
			var max = float.MinValue;
			var maxTicks = -1;
			for (var i = 0; i < 4; i++)
			{
				var f = GetFactorForTicks(t);
				if (f < min)
				{
					min = f;
					minTicks = t;
				}
				if (f > max)
				{
					max = f;
					maxTicks = t;
				}
				t += d;
			}
			if (minTicks != -1 && maxTicks != -1 && minTicks > maxTicks)
				return (max, min);
			return (min, max);
		}

		public static Action GenerateTooltipDrawer(Rect rect, float? previewDifficulty = null, List<ZombieFreeEventWindow> previewWindows = null)
		{
			const float g = 40f;
			static Rect R(int x1, int y1, int x2, int y2) => new(g * x1, g * y1, g * (x2 - x1), g * (y2 - y1));

			return () =>
			{
				var map = Find.CurrentMap;
				var weather = map?.GetComponent<ZombieWeather>();
				if (weather == null)
					return;
				var previewMode = previewDifficulty.HasValue && previewWindows != null;
				var previousThreatScale = ZombieSettings.Values.threatScale;
				var previousKeyframeThreatScales = ZombieSettings.ValuesOverTime?
					.Select(keyframe => keyframe?.values?.threatScale)
					.ToArray();

				try
				{
					if (previewDifficulty.HasValue)
					{
						ZombieSettings.Values.threatScale = previewDifficulty.Value;
						if (ZombieSettings.ValuesOverTime != null)
							foreach (var keyframe in ZombieSettings.ValuesOverTime)
								if (keyframe?.values != null)
									keyframe.values.threatScale = previewDifficulty.Value;
					}

					var r = new Rect(0, 0, 3, 3);
					var currentTicks = GenTicks.TicksAbs;

					Text.Font = GameFont.Tiny;
					GUI.color = Color.white;

					Widgets.DrawAtlas(rect, ActiveTip.TooltipBGAtlas);

					GUI.color = new Color(1f, 1f, 1f, 0.05f);
					for (var i = 1; i <= 7; i++)
						Widgets.DrawLineHorizontal(g * 0, g * i, g * 18);
					for (var i = 1; i <= 17; i++)
						Widgets.DrawLineVertical(g * i, g * 0, g * 8);
					GUI.color = Color.white;

					Text.Anchor = TextAnchor.MiddleLeft;
					Widgets.Label(R(0, 0, 3, 1), "    " + "ThreatForecast".Translate());

					Text.Anchor = TextAnchor.MiddleCenter;
					Widgets.Label(R(2, 0, 17, 1), "Next14Days".Translate());

					GUI.color = new Color(1f, 1f, 1f, 0.5f);
					Widgets.DrawTextureFitted(R(2, 1, 17, 3), ForecastBackground, 1);
					GUI.color = Color.white;

					Text.Anchor = TextAnchor.UpperCenter;
					Widgets.Label(R(0, 1, 2, 2), "100%");
					Text.Anchor = TextAnchor.LowerCenter;
					Widgets.Label(R(0, 2, 2, 3), "0%");

					Text.Anchor = TextAnchor.MiddleCenter;
					for (var i = 0; i <= 14; i++)
						Widgets.Label(R(2 + i, 3, 3 + i, 4), $"{i}");

					GUI.color = new Color(0.5f, 0.5f, 0.5f);
					Widgets.DrawLineVertical(g * 2, g * 1, g * 2);
					Widgets.DrawLineHorizontal(g * 2, g * 3, g * 15);
					GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
					Widgets.DrawLineHorizontal(g * 2, g * 2, g * 15);
					GUI.color = Color.white;

					var tpd = GenDate.TicksPerDay;
					var dayStart = previewMode ? 0 : currentTicks - currentTicks % tpd;
					var dayZombieFreeSpans = previewMode
						? ZombieFreeEventSpansForGameRange(dayStart, dayStart + 15 * tpd, previewWindows)
						: ZombieFreeEventSpansForAbsRange(dayStart, dayStart + 15 * tpd);
					for (var x = 0; x < 15 * g; x++)
					{
						var displayTick = dayStart + (int)(x * tpd / g);
						if (IsInZombieFreeEventSpan(displayTick, dayZombieFreeSpans))
							continue;
						var factorTick = previewMode
							? ZombieFreeEventManager.AbsTickForGameTick(displayTick)
							: displayTick;
						var f = weather.GetBaseFactorForTicks(factorTick);
						var y = 3 * g - 2 * g * f;
						r.center = new Vector2(x + g * 2, y);
						Widgets.DrawTextureFitted(r, Constants.dot, 1f);
					}

					DrawZombieFreeEventSpans(R(2, 1, 17, 3), dayStart, dayStart + 15 * tpd, dayZombieFreeSpans);

					GUI.color = Color.magenta;
					var dx = previewMode ? 0f : (currentTicks % tpd) * g / tpd;
					Widgets.DrawLineVertical(dx + g * 2, g * 1 - 2, g * 2 + 6);
					var currentFactor = previewMode
						? IsInZombieFreeEventSpan(0, dayZombieFreeSpans)
							? 0
							: Mathf.FloorToInt(weather.GetBaseFactorForTicks(ZombieFreeEventManager.AbsTickForGameTick(0)) * 100)
						: Mathf.FloorToInt(weather.GetFactorForTicks(currentTicks) * 100);
					Text.Anchor = TextAnchor.MiddleLeft;
					Widgets.Label(new Rect(dx + g * 2 + 2, g * 1 - 16, 45, 16), string.Format("{0:D0}%", currentFactor));
					GUI.color = Color.white;

					Text.Anchor = TextAnchor.MiddleCenter;
					Widgets.Label(R(2, 4, 17, 5), "Next4Quadrums".Translate());

					GUI.color = new Color(1f, 1f, 1f, 0.5f);
					Widgets.DrawTextureFitted(R(2, 5, 17, 7), ForecastBackground, 1);
					GUI.color = Color.white;

					Text.Anchor = TextAnchor.UpperCenter;
					Widgets.Label(R(0, 5, 2, 6), "100%");
					Text.Anchor = TextAnchor.LowerCenter;
					Widgets.Label(R(0, 6, 2, 7), "0%");

					Text.Anchor = TextAnchor.MiddleCenter;
					var seasonTick = previewMode ? ZombieFreeEventManager.AbsTickForGameTick(0) : currentTicks;
					var labels = new[] { GenDate.Season(seasonTick, Find.WorldGrid.LongLatOf(map.Tile)).ToString(), "+1", "+2", "+3", "+4" };
					for (var i = 0; i < labels.Length; i++)
						Widgets.Label(R(2 + i * 3, 7, 5 + i * 3, 8), labels[i]);

					GUI.color = new Color(0.5f, 0.5f, 0.5f);
					Widgets.DrawLineVertical(g * 2, g * 5, g * 2);
					Widgets.DrawLineHorizontal(g * 2, g * 7, g * 15);
					GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.75f);
					Widgets.DrawLineHorizontal(g * 2, g * 6, g * 15);

					GUI.color = Color.gray;
					var tpq = GenDate.TicksPerQuadrum;
					var qStart = previewMode ? 0 : currentTicks - currentTicks % tpq;
					var quadrumZombieFreeSpans = previewMode
						? ZombieFreeEventSpansForGameRange(qStart, qStart + 5 * tpq, previewWindows)
						: ZombieFreeEventSpansForAbsRange(qStart, qStart + 5 * tpq);
					var buffer = new float[8];
					var bIndex = 0;
					for (var x = 0; x < 15 * g; x++)
					{
						var displayTick = qStart + (int)(x * tpq / (g * 3));
						var factorTick = previewMode
							? ZombieFreeEventManager.AbsTickForGameTick(displayTick)
							: displayTick;
						var f = weather.GetBaseFactorForTicks(factorTick);
						if (x == 0)
							for (var i = 0; i < buffer.Length; i++)
								buffer[i] = f;
						else
						{
							bIndex = (bIndex + 1) % 8;
							buffer[bIndex] = f;
						}
						if (IsInZombieFreeEventSpan(displayTick, quadrumZombieFreeSpans))
							continue;
						var y = 7 * g - 2 * g * buffer.Average();
						Widgets.DrawLineVertical(x + g * 2, y, 7 * g - y);
					}

					DrawZombieFreeEventSpans(R(2, 5, 17, 7), qStart, qStart + 5 * tpq, quadrumZombieFreeSpans);

					GUI.color = Color.white;
					Widgets.DrawLineVertical(g * 5, g * 5 - 2, g * 2 + 4);
					Widgets.DrawLineVertical(g * 8, g * 5 - 2, g * 2 + 4);
					Widgets.DrawLineVertical(g * 11, g * 5 - 2, g * 2 + 4);
					Widgets.DrawLineVertical(g * 14, g * 5 - 2, g * 2 + 4);

					GUI.color = Color.magenta;
					dx = previewMode ? 0f : (currentTicks % tpq) * 3 * g / tpq;
					Widgets.DrawLineVertical(dx + g * 2, g * 5 - 4, g * 2 + 6);
					GUI.color = Color.white;

					Text.Anchor = TextAnchor.UpperLeft;
				}
				finally
				{
					if (previewDifficulty.HasValue)
					{
						ZombieSettings.Values.threatScale = previousThreatScale;
						if (previousKeyframeThreatScales != null && ZombieSettings.ValuesOverTime != null)
							for (var i = 0; i < previousKeyframeThreatScales.Length && i < ZombieSettings.ValuesOverTime.Count; i++)
								if (previousKeyframeThreatScales[i].HasValue && ZombieSettings.ValuesOverTime[i]?.values != null)
									ZombieSettings.ValuesOverTime[i].values.threatScale = previousKeyframeThreatScales[i].Value;
					}
				}
			};
		}
	}
}
