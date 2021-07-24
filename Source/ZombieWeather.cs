using RimWorld;
using System;
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
		public static int CurrentTicks() => GenTicks.TicksAbs;

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
			return ZombieSettings.Values.useDynamicThreatLevel ? (map?.GetComponent<ZombieWeather>()?.GetFactorFor(0) ?? 1f) : 1f;
		}

		public float GetFactorForTicks(int ticks, int deltaDays = 0)
		{
			if ((ticks - GenTicks.TicksAbs + GenTicks.TicksGame) / (float)GenDate.TicksPerDay <= ZombieSettings.Values.daysBeforeZombiesCome)
				return 0f;

			var currentDay = ticks / (float)GenDate.TicksPerDay;
			var x = currentDay + deltaDays;
			var m = ZombieSettings.Values.dynamicThreatSmoothness;
			var n = ZombieSettings.Values.dynamicThreatStretch;
			var val = 0
				+ Mathf.Sin(f1 * x / (m + Mathf.Sin(x / f2 + o3) / n) + o1)
				+ Mathf.Sin(f2 * x / (m + Mathf.Sin(x / f3 + o4) / n) + o2)
				+ Mathf.Sin(f3 * x / (m + Mathf.Sin(x / f4 + o1) / n) + o3)
				+ Mathf.Sin(f4 * x / (m + Mathf.Sin(x / f1 + o2) / n) + o4);
			return Mathf.Clamp01((Tools.Difficulty() / 2f + val) / p);
		}

		public float GetFactorFor(int deltaDays)
		{
			return GetFactorForTicks(CurrentTicks(), deltaDays);
		}

		public (float, float) GetFactorRangeFor(int deltaDays = 0)
		{
			var t = CurrentTicks();
			t -= t % GenDate.TicksPerDay;
			var d = GenDate.TicksPerDay / 4;
			t += d / 2;
			var min = float.MaxValue;
			var max = float.MinValue;
			for (var i = 0; i < 4; i++)
			{
				var f = GetFactorForTicks(t, deltaDays);
				if (f < min) min = f;
				if (f > max) max = f;
				t += d;
			}
			return (min, max);
		}

		public float GetAverageFor(int deltaStartDay, int deltaEndDay)
		{
			var t = CurrentTicks();
			t -= t % GenDate.TicksPerDay;
			t += GenDate.TicksPerDay / 2;
			var sum = 0f;
			for (var i = deltaStartDay; i <= deltaEndDay; i++)
				sum += GetFactorForTicks(t, i);
			return sum / (deltaEndDay - deltaStartDay + 1);
		}

		public static Action GeneateTooltipDrawer(Rect rect)
		{
			const float g = 40f;
			static Rect R(int x1, int y1, int x2, int y2) => new Rect(g * x1, g * y1, g * (x2 - x1), g * (y2 - y1));

			return () =>
			{
				var map = Find.CurrentMap;
				var weather = map?.GetComponent<ZombieWeather>();
				if (weather == null) return;
				var r = new Rect(0, 0, 3, 3);
				var ticks = CurrentTicks();

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

				var dayStart = ticks;
				var tpd = GenDate.TicksPerDay;
				dayStart -= dayStart % tpd;
				for (var x = 0; x < 15 * g; x++)
				{
					var t = dayStart + (int)(x * tpd / g);
					var f = weather.GetFactorForTicks(t);
					var y = 3 * g - 2 * g * f;
					r.center = new Vector2(x + g * 2, y);
					Widgets.DrawTextureFitted(r, Constants.dot, 1f);
				}

				GUI.color = Color.magenta;
				var dx = (ticks % tpd) * g / tpd;
				Widgets.DrawLineVertical(dx + g * 2, g * 1 - 2, g * 2 + 6);
				var currentFactor = Mathf.FloorToInt(weather.GetFactorFor(0) * 100);
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
				var labels = new[] { GenDate.Season(ticks, Find.WorldGrid.LongLatOf(map.Tile)).ToString(), "+1", "+2", "+3", "+4" };
				for (var i = 0; i < labels.Length; i++)
					Widgets.Label(R(2 + i * 3, 7, 5 + i * 3, 8), labels[i]);

				GUI.color = new Color(0.5f, 0.5f, 0.5f);
				Widgets.DrawLineVertical(g * 2, g * 5, g * 2);
				Widgets.DrawLineHorizontal(g * 2, g * 7, g * 15);
				GUI.color = new Color(0.5f, 0.5f, 0.5f, 0.75f);
				Widgets.DrawLineHorizontal(g * 2, g * 6, g * 15);

				GUI.color = Color.gray;
				var qStart = ticks;
				var tpq = GenDate.TicksPerQuadrum;
				qStart -= qStart % tpq;
				var buffer = new float[8];
				var bIndex = 0;
				for (var x = 0; x < 15 * g; x++)
				{
					var t = qStart + (int)(x * tpq / (g * 3));
					var f = weather.GetFactorForTicks(t);
					if (x == 0)
						for (var i = 0; i < buffer.Length; i++) buffer[i] = f;
					else
					{
						bIndex = (bIndex + 1) % 8;
						buffer[bIndex] = f;
					}
					var y = 7 * g - 2 * g * buffer.Average();
					Widgets.DrawLineVertical(x + g * 2, y, 7 * g - y);
				}

				GUI.color = Color.white;
				Widgets.DrawLineVertical(g * 5, g * 5 - 2, g * 2 + 4);
				Widgets.DrawLineVertical(g * 8, g * 5 - 2, g * 2 + 4);
				Widgets.DrawLineVertical(g * 11, g * 5 - 2, g * 2 + 4);
				Widgets.DrawLineVertical(g * 14, g * 5 - 2, g * 2 + 4);

				GUI.color = Color.magenta;
				dx = (ticks % tpq) * 3 * g / tpq;
				Widgets.DrawLineVertical(dx + g * 2, g * 5 - 4, g * 2 + 6);
				GUI.color = Color.white;

				Text.Anchor = TextAnchor.UpperLeft;
			};
		}
	}
}
