using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;
using Verse;

namespace ZombieLand
{
	static class MaterialCleaner
	{
		public static void UnCache(this Graphic graphic)
		{
			if (graphic == null)
				return;
			if (graphic is Graphic_Multi multi)
				for (var i = 0; i < 4; i++)
				{
					var mat = multi.mats[i];
					if (mat != null)
					{
						MaterialPool.matDictionaryReverse.Remove(mat);
						MaterialPool.matDictionary.RemoveAll(pair => pair.Value == mat);
						UnityEngine.Object.DestroyImmediate(multi.mats[i]);
						multi.mats[i] = null;
					}
				}
			else if (graphic is Graphic_Single single && single.mat != null)
			{
				MaterialPool.matDictionaryReverse.Remove(single.mat);
				MaterialPool.matDictionary.RemoveAll(pair => pair.Value == single.mat);
				UnityEngine.Object.DestroyImmediate(single.mat);
				single.mat = null;
			}
		}
	}

	struct Snapshot
	{
		public DateTime time;
		public string[] textureNames;

		public long usedHeapSizeLong;
		public long monoHeapSizeLong;
		public long totalAllocatedMemoryLong;
		public long totalReservedMemoryLong;
		public long totalUnusedReservedMemoryLong;
		public long monoUsedSizeLong;

		public long currentTextureMemory;
		public long desiredTextureMemory;
		public long nonStreamingTextureCount;
		public long nonStreamingTextureMemory;
		public long streamingTextureCount;
		public long targetTextureMemory;
		public long totalTextureMemory;

		public long totalRuntimeTextureCount;
		public long totalRuntimeTextureMemory;
		public long totalRuntimeMeshCount;
		public long totalRuntimeMeshMemory;

		public long allocatedMemoryForGraphicsDriver;
		public long streamingMipmapUploadCount;
		public long streamingTextureLoadingCount;
		public long streamingTexturePendingLoadCount;

		public Snapshot()
		{
			time = DateTime.Now;

			usedHeapSizeLong = Profiler.usedHeapSizeLong / 1048576L;
			monoHeapSizeLong = Profiler.GetMonoHeapSizeLong() / 1048576L;
			totalAllocatedMemoryLong = Profiler.GetTotalAllocatedMemoryLong() / 1048576L;
			totalReservedMemoryLong = Profiler.GetTotalReservedMemoryLong() / 1048576L;
			totalUnusedReservedMemoryLong = Profiler.GetTotalUnusedReservedMemoryLong() / 1048576L;
			monoUsedSizeLong = Profiler.GetMonoUsedSizeLong() / 1048576L;

			currentTextureMemory = (long)(Texture.currentTextureMemory / 1048576uL);
			desiredTextureMemory = (long)(Texture.desiredTextureMemory / 1048576uL);
			nonStreamingTextureCount = (long)Texture.nonStreamingTextureCount;
			nonStreamingTextureMemory = (long)Texture.nonStreamingTextureMemory / 1048;
			streamingTextureCount = (long)Texture.streamingTextureCount;
			targetTextureMemory = (long)(Texture.targetTextureMemory / 1048576uL);
			totalTextureMemory = (long)(Texture.totalTextureMemory / 1048576uL);

			var allTextures = Resources.FindObjectsOfTypeAll(typeof(Texture));
			textureNames = allTextures.Where(t => t.name?.Length > 0).Select(t => t.name).ToArray();
			totalRuntimeTextureCount = allTextures.Length;
			totalRuntimeTextureMemory = allTextures.Sum(Profiler.GetRuntimeMemorySizeLong) / 1048576L;
			var allMeshes = Resources.FindObjectsOfTypeAll(typeof(Mesh));
			totalRuntimeMeshCount = allMeshes.Length;
			totalRuntimeMeshMemory = allMeshes.Sum(Profiler.GetRuntimeMemorySizeLong) / 1048576L;

			allocatedMemoryForGraphicsDriver = Profiler.GetAllocatedMemoryForGraphicsDriver() / 1048576L;
			streamingMipmapUploadCount = (long)Texture.streamingMipmapUploadCount;
			streamingTextureLoadingCount = (long)Texture.streamingTextureLoadingCount;
			streamingTexturePendingLoadCount = (long)Texture.streamingTexturePendingLoadCount;
		}

		public override readonly string ToString()
		{
			var str = new StringBuilder();
			str.AppendLine(time.ToLongTimeString());
			str.AppendLine();
			str.AppendLine($"{usedHeapSizeLong} MB");
			str.AppendLine($"{monoHeapSizeLong} MB");
			str.AppendLine($"{totalAllocatedMemoryLong} MB");
			str.AppendLine($"{totalReservedMemoryLong} MB");
			str.AppendLine($"{totalUnusedReservedMemoryLong} MB");
			str.AppendLine($"{monoUsedSizeLong} MB");
			str.AppendLine();
			str.AppendLine($"{currentTextureMemory} MB");
			str.AppendLine($"{desiredTextureMemory} MB");
			str.AppendLine($"{nonStreamingTextureCount}");
			str.AppendLine($"{nonStreamingTextureMemory} MB");
			str.AppendLine($"{streamingTextureCount}");
			str.AppendLine($"{targetTextureMemory} MB");
			str.AppendLine($"{totalTextureMemory} MB");
			str.AppendLine();
			str.AppendLine($"{totalRuntimeTextureCount}");
			str.AppendLine($"{totalRuntimeTextureMemory} MB");
			str.AppendLine($"{totalRuntimeMeshCount}");
			str.AppendLine($"{totalRuntimeMeshMemory} MB");
			str.AppendLine();
			str.AppendLine($"{allocatedMemoryForGraphicsDriver}");
			str.AppendLine($"{streamingMipmapUploadCount}");
			str.AppendLine($"{streamingTextureLoadingCount}");
			str.AppendLine($"{streamingTexturePendingLoadCount}");
			return str.ToString();
		}

		public readonly string ToString(Snapshot from)
		{
			var str = new StringBuilder();
			str.AppendLine($"-{time - from.time:m\\:ss}");
			str.AppendLine();
			str.AppendLine($"{usedHeapSizeLong - from.usedHeapSizeLong} MB");
			str.AppendLine($"{monoHeapSizeLong - from.monoHeapSizeLong} MB");
			str.AppendLine($"{totalAllocatedMemoryLong - from.totalAllocatedMemoryLong} MB");
			str.AppendLine($"{totalReservedMemoryLong - from.totalReservedMemoryLong} MB");
			str.AppendLine($"{totalUnusedReservedMemoryLong - from.totalUnusedReservedMemoryLong} MB");
			str.AppendLine($"{monoUsedSizeLong - from.monoUsedSizeLong} MB");
			str.AppendLine();
			str.AppendLine($"{currentTextureMemory - from.currentTextureMemory} MB");
			str.AppendLine($"{desiredTextureMemory - from.desiredTextureMemory} MB");
			str.AppendLine($"{nonStreamingTextureCount - from.nonStreamingTextureCount}");
			str.AppendLine($"{nonStreamingTextureMemory - from.nonStreamingTextureMemory} MB");
			str.AppendLine($"{streamingTextureCount - from.streamingTextureCount}");
			str.AppendLine($"{targetTextureMemory - from.targetTextureMemory} MB");
			str.AppendLine($"{totalTextureMemory - from.totalTextureMemory} MB");
			str.AppendLine();
			str.AppendLine($"{totalRuntimeTextureCount - from.totalRuntimeTextureCount}");
			str.AppendLine($"{totalRuntimeTextureMemory - from.totalRuntimeTextureMemory} MB");
			str.AppendLine($"{totalRuntimeMeshCount - from.totalRuntimeMeshCount}");
			str.AppendLine($"{totalRuntimeMeshMemory - from.totalRuntimeMeshMemory} MB");
			str.AppendLine();
			str.AppendLine($"{allocatedMemoryForGraphicsDriver - from.allocatedMemoryForGraphicsDriver}");
			str.AppendLine($"{streamingMipmapUploadCount - from.streamingMipmapUploadCount}");
			str.AppendLine($"{streamingTextureLoadingCount - from.streamingTextureLoadingCount}");
			str.AppendLine($"{streamingTexturePendingLoadCount - from.streamingTexturePendingLoadCount}");
			return str.ToString();
		}
	}

	/*
	[HarmonyPatch(typeof(GraphicDatabase), nameof(GraphicDatabase.Get))]
	[HarmonyPatch(new[] { typeof(Type), typeof(string), typeof(Shader), typeof(Vector2), typeof(Color), typeof(Color), typeof(GraphicData), typeof(List<ShaderParameter>), typeof(string) })]
	static class GraphicDatabase_Get_Patch
	{
		static void Postfix(Type graphicClass, string path, GraphicData data, Graphic __result)
		{
			if (graphicClass == typeof(Graphic_Multi) && __result is Graphic_Multi multi)
			{
				var names = multi.mats.Select(m => m.name).ToArray();
				Log.Warning($"=> MULTI: {path} : {data.texPath} [{names.Join()}]");
			}
			else if (graphicClass == typeof(Graphic_Single) && __result is Graphic_Single single)
			{
				Log.Warning($"=> SINGLE: {path} : {data.texPath} [{single.mat.name}]");
			}
		}
	}

	[HarmonyPatch(typeof(Texture2D), "Internal_Create")]
	static class Texture2D_Internal_Create_Patch
	{
		static void Postfix(Texture2D mono)
		{
			Log.Warning($"### Internal_Create: {mono.name}");
		}
	}

	[HarmonyPatch(typeof(Graphic_Multi), nameof(Graphic_Multi.Init))]
	static class Graphic_Multi_Init_Patch2
	{
		static void Prefix(GraphicRequest req)
		{
			Log.Warning($"### Graphic_Multi.Init: {req.path}");
		}
	}
	*/

	[HarmonyPatch]
	static class DebugWindowOnGUI_Patch
	{
		static IntVec2 topLeft = new(10, 10), topLeftDrag = IntVec2.Zero;
		static Vector2 lastMouseDown;
		static bool isDragging = false;
		static readonly List<Snapshot> snapshots = new() { };
		static float lastUIScale = -1f, labelWidth = 0f, textHeight = 0f, bottomHeight = 0f;

		static IEnumerable<MethodBase> TargetMethods()
		{
			yield return SymbolExtensions.GetMethodInfo((UIRoot_Entry me) => me.DoMainMenu());
			yield return SymbolExtensions.GetMethodInfo(() => DebugTools.DebugToolsOnGUI());
		}

		static void Postfix()
		{
			var eventType = Event.current.type;
			if (eventType == EventType.Layout)
				return;

			var labelBuilder = new StringBuilder();
			labelBuilder.AppendLine($"    Time:");
			labelBuilder.AppendLine();
			labelBuilder.AppendLine($"Used Heap:");
			labelBuilder.AppendLine($"Mono Heap:");
			labelBuilder.AppendLine($"Total Allocated:");
			labelBuilder.AppendLine($"Total Reserved:");
			labelBuilder.AppendLine($"Total Unused Reserved:");
			labelBuilder.AppendLine($"Mono Used:");
			labelBuilder.AppendLine();
			labelBuilder.AppendLine($"Current Tex:");
			labelBuilder.AppendLine($"Desired Tex:");
			labelBuilder.AppendLine($"Non Streaming Tex #:");
			labelBuilder.AppendLine($"Non Streaming Tex:");
			labelBuilder.AppendLine($"Streaming Tex #:");
			labelBuilder.AppendLine($"Target Tex:");
			labelBuilder.AppendLine($"Total Tex:");
			labelBuilder.AppendLine();
			labelBuilder.AppendLine($"Total Runtime Textures:");
			labelBuilder.AppendLine($"Total Runtime Text Mem:");
			labelBuilder.AppendLine($"Total Runtime Meshs:");
			labelBuilder.AppendLine($"Total Runtime Mesh Mem:");
			labelBuilder.AppendLine();
			labelBuilder.AppendLine($"Allocated Mem Graphics Driver:");
			labelBuilder.AppendLine($"Streaming Tex Mipmap Upload #:");
			labelBuilder.AppendLine($"Streaming Tex Loading #:");
			labelBuilder.AppendLine($"Streaming Tex Pending Load #:");
			labelBuilder.AppendLine();
			labelBuilder.Append($"Target Budget: {QualitySettings.streamingMipmapsMemoryBudget} MB");

			var current = new Snapshot();
			var labels = labelBuilder.ToString();

			Text.Font = GameFont.Tiny;
			GUI.color = UnityEngine.Color.white;
			var columnWidth = 80f;
			var padding = 10f;
			if (labelWidth == 0 || Prefs.UIScale != lastUIScale)
			{
				lastUIScale = Prefs.UIScale;
				var size = Text.CalcSize(labels);
				labelWidth = size.x + padding;
				textHeight = size.y;
				bottomHeight = Text.CalcSize("XXX\nXXX").y;
			}

			var x = Mathf.Clamp(topLeft.x + topLeftDrag.x, 0, UI.screenWidth - 16);
			var z = Mathf.Clamp(topLeft.z + topLeftDrag.z, 0, UI.screenHeight - 16);

			var r = new Rect(x, z, labelWidth + columnWidth * (snapshots.Count + 1) + 2 * padding, textHeight + 2 * padding);
			Widgets.DrawRectFast(r, new Color(0, 0, 0, 0.6f));

			var drag = new Rect(r.x, r.y, 16, 16);
			Widgets.DrawRectFast(drag, new Color(1, 1, 1, 0.2f));
			Text.Anchor = TextAnchor.MiddleCenter;
			Widgets.Label(drag, "☰");
			Text.Anchor = TextAnchor.UpperLeft;

			var mousePosition = Event.current.mousePosition;
			var mousePressed = Input.GetMouseButton(0);
			if (mousePressed == false)
			{
				if (isDragging)
				{
					topLeft += topLeftDrag;
					topLeftDrag = IntVec2.Zero;
				}
				isDragging = false;
			}
			if (Mouse.IsOver(drag) && isDragging == false && mousePressed)
			{
				lastMouseDown = mousePosition;
				isDragging = true;
				Event.current.Use();
			}
			else if (isDragging && eventType == EventType.MouseDrag)
			{
				var delta = mousePosition - lastMouseDown;
				topLeftDrag.x = (int)delta.x;
				topLeftDrag.z = (int)delta.y;
				Event.current.Use();
			}

			var br = new Rect(r.x + labelWidth + padding, r.y + r.height - padding - 22, columnWidth - padding, 22);
			if (Widgets.ButtonText(br.LeftPartPixels((columnWidth - padding - 5) / 2), "add"))
				snapshots.Add(current);
			if (snapshots.Count > 0 && Widgets.ButtonText(br.RightPartPixels((columnWidth - padding - 5) / 2), "tex"))
				Log.Error($"New textures: " + current.textureNames.Except(snapshots[0].textureNames).Join());

			r = r.ExpandedBy(-padding);
			Widgets.Label(r, labels);
			r.width = columnWidth;
			r.x += labelWidth;
			r.height -= bottomHeight;
			Widgets.Label(r, current.ToString());

			if (snapshots.Count > 0)
				for (var i = snapshots.Count - 1; i >= 0; i--)
				{
					r.x += columnWidth;
					br.x += columnWidth;
					var over = Mouse.IsOver(r);
					if (over)
					{
						var r2 = new Rect(r.x - 5, r.y - 5, r.width, r.height + 10);
						Widgets.DrawRectFast(r2, new Color(0, 0, 0, 0.2f));
					}
					var str = over ? snapshots[i].ToString(i == snapshots.Count - 1 ? current : snapshots[i + 1]) : snapshots[i].ToString();
					Widgets.Label(r, str);

					if (Widgets.ButtonText(br.LeftPartPixels((columnWidth - padding - 5) / 2), "del"))
					{
						snapshots.RemoveAt(i);
						break;
					}
				}
		}
	}
}