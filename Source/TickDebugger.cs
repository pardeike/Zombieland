using Harmony;
using RimWorld;
using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	[StaticConstructorOnStartup]
	static class TickDebugging
	{
		static readonly bool TICK_DEBUGGING_ACTIVE = false;

		public static readonly Texture2D CursorTex;
		public static Vector2 CursorHotspot = new Vector2(15f, 15f);
		public static bool showing = false;

		static TickDebugging()
		{
			if (TICK_DEBUGGING_ACTIVE == false)
				return;

			CursorTex = new Texture2D(1, 1, TextureFormat.ARGB32, false);
			var path = Tools.GetModRootDirectory() + Path.DirectorySeparatorChar + "Textures" + Path.DirectorySeparatorChar + "HairCursor.png";
			CursorTex.LoadImage(File.ReadAllBytes(path));

			var harmony = HarmonyInstance.Create("Zombieland.TickDebugging");
			var original1 = AccessTools.Method(typeof(MapInterface), "MapInterfaceOnGUI_BeforeMainTabs");
			var postfix1 = SymbolExtensions.GetMethodInfo(() => TickerOnGUI_Postfix());
			harmony.Patch(original1, null, new HarmonyMethod(postfix1));

			var original2 = AccessTools.Method(typeof(Game), "UpdatePlay");
			var sw = new Stopwatch();
			var prefix2 = SymbolExtensions.GetMethodInfo(() => UpdatePlay_Prefix(out sw));
			var postfix2 = SymbolExtensions.GetMethodInfo(() => UpdatePlay_Postfix(sw));
			harmony.Patch(original1, new HarmonyMethod(prefix2), new HarmonyMethod(postfix2));
		}

		static void TickerOnGUI_Postfix()
		{
			if (Event.current.type != EventType.Repaint) return;
			TickDebugger.TickerOnGUI();
		}

		static void UpdatePlay_Prefix(out Stopwatch __state)
		{
			TickDebugger.Advance();
			__state = Stopwatch.StartNew();
		}

		static void UpdatePlay_Postfix(Stopwatch __state)
		{
			TickDebugger.Update(0, "Game.UpdatePlay", __state, true);
		}
	}

	public class TickDebugger
	{
		private static TickerData[] tickers = new TickerData[12];
		public static int maxTickers = -1;

		private static readonly Color[] colors = new Color[]
		{
			/* 00 */ new Color(0.15686f, 0.15686f, 0.15686f),
			/* 01 */ Color.green,
			/* 02 */ Color.red,
			/* 03 */ Color.cyan,
			/* 04 */ Color.blue,
			/* 05 */ new Color(0.784313725f, 0.7490196f, 0.9f),
			/* 06 */ Color.magenta,
			/* 07 */ Color.yellow,
			/* 08 */ new Color(0.5f, 0.25f, 0.125f),
			/* 09 */ Color.gray,
			/* 10 */ new Color(0.5f, 0.5f, 0.125f),
			/* 11 */ new Color(0.625f, 0.25f, 0.625f),
		};

		private class TickerData
		{
			public static int width = 800;
			public static int height = 128;

			public string name = "Unknown";
			public Color color;
			public float value = 0f;
			public int[] data = new int[width];
			public bool resetPos;

			public TickerData(int idx, string name, bool resetPos)
			{
				color = colors[idx];
				this.name = name == null ? "Index " + idx : name + " [" + idx + "]";
				this.resetPos = resetPos;
			}
		}

		public static void Update(int idx, string name, Stopwatch sw, bool resetPos = false)
		{
			if (tickers[idx] == null)
			{
				tickers[idx] = new TickerData(idx, name, resetPos);
				maxTickers = Math.Max(maxTickers, idx);
			}
			tickers[idx].value += sw.ElapsedMilliseconds * 60f / 1000f;

			sw = Stopwatch.StartNew();
		}

		public static void Advance()
		{
			if (Find.CurrentMap != null && Find.TickManager.Paused == false)
			{
				var lastIndex = TickerData.width - 1;
				for (var t = 0; t < maxTickers; t++)
				{
					var ticker = tickers[t];
					for (var i = 0; i < lastIndex; i++)
						ticker.data[i] = ticker.data[i + 1];
					ticker.data[lastIndex] = (int)GenMath.LerpDouble(0f, 2f, 0, TickerData.height - 1, ticker.value);
					ticker.value = 0;
				}
			}
		}

		static Color[] tickerImageData;
		public static void TickerOnGUI()
		{
			if (tickerImageData == null)
			{
				var tex = new Texture2D(1, 1, TextureFormat.ARGB32, false);
				var path = Tools.GetModRootDirectory() + Path.DirectorySeparatorChar + "Textures" + Path.DirectorySeparatorChar + "Ticker.png";
				tex.LoadImage(File.ReadAllBytes(path));
				tickerImageData = tex.GetPixels();
				TickerData.width = tex.width;
				TickerData.height = tex.height;
				UnityEngine.Object.Destroy(tex);

				for (var t = 0; t < maxTickers; t++)
					tickers[t].data = new int[TickerData.width];
			}

			var texture = new Texture2D(TickerData.width, TickerData.height, TextureFormat.ARGB32, false);
			texture.SetPixels(tickerImageData);

			var posx = Screen.width / 2 - TickerData.width / 2;
			var posy = 80;
			var tickerRect = new Rect(posx, posy, TickerData.width, TickerData.height);
			var mouse = UI.MousePositionOnUIInverted;
			if (tickerRect.Contains(mouse))
			{
				if (TickDebugging.showing == false)
					Cursor.SetCursor(TickDebugging.CursorTex, TickDebugging.CursorHotspot, CursorMode.Auto);
				TickDebugging.showing = true;
			}
			else
			{
				if (TickDebugging.showing)
					CustomCursor.Activate();
				TickDebugging.showing = false;
			}

			string tickerName = null;
			var mpos = new Vector2(mouse.x - posx, TickerData.height - (mouse.y - posy));

			for (var x = 0; x < TickerData.width; x++)
			{
				var pos = 0;
				for (var t = 0; t < maxTickers; t++)
				{
					var ticker = tickers[t];
					var val = ticker.data[x];
					for (var y = pos; y < pos + val; y++)
					{
						if (x == mpos.x && y == mpos.y)
							tickerName = ticker.name;
						texture.SetPixel(x, y, ticker.color);
					}
					if (ticker.resetPos == false)
						pos += val;
				}
			}

			if (tickerName != null)
			{
				Text.Font = GameFont.Small;

				var num = 260;
				var vector = Text.CalcSize(tickerName);
				if (vector.x > num)
				{
					vector.x = num;
					vector.y = Text.CalcHeight(tickerName, vector.x);
				}
				var bgRect = new Rect(0f, 0f, vector.x, vector.y);
				bgRect = bgRect.ContractedBy(-4f);

				bgRect.position = mouse + new Vector2(10, -10);
				Find.WindowStack.ImmediateWindow(61471346, bgRect, WindowLayer.Super, delegate
				{
					var r = bgRect.AtZero();
					Widgets.DrawAtlas(r, ActiveTip.TooltipBGAtlas);
					Text.Font = GameFont.Small;
					Widgets.Label(r.ContractedBy(4f), tickerName);
				}, false, false, 1f);
			}

			texture.Apply();

			GUI.DrawTexture(tickerRect, texture);
			UnityEngine.Object.Destroy(texture);
		}
	}
}