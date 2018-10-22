using System.Collections;
using System.Globalization;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	static class GraphicToolbox
	{
		public static Color HexColor(this string hex)
		{
			var r = int.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
			var g = int.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
			var b = int.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
			return new Color(r / 255f, g / 255f, b / 255f);
		}

		public static string RandomSkinColorString()
		{
			var idx = Rand.Range(0, GraphicsDatabase.zombieRGBSkinColors.Count);
			return GraphicsDatabase.zombieRGBSkinColors[idx];
		}

		public static IEnumerator ApplyStainsIterativ(this ColorData baseData, string part, bool flipH, bool flipV, float px = -1f, float py = -1f)
		{
			var stainData = GraphicsDatabase.GetColorData(part, null);
			var baseRect = baseData.rect;
			var stainWidth = stainData.width;
			var stainHeight = stainData.height;
			var x = (int)(baseRect.x + (baseRect.width - stainWidth) * (px != -1f ? px : Rand.Value));
			var y = (int)(baseRect.y + (baseRect.height - stainHeight) * (py != -1f ? py : Rand.Value));
			var oPixels = baseData.GetRawPixels(x, y, stainWidth, stainHeight);
			var pPixels = stainData.pixels;
			yield return null;
			for (var sx = 0; sx < stainWidth; sx++)
			{
				for (var sy = 0; sy < stainHeight; sy++)
				{
					var pIdx = (flipH ? (stainWidth - sx - 1) : sx) + (flipV ? (stainHeight - sy - 1) : sy) * stainWidth;
					var oIdx = sx + sy * stainWidth;

					var oa = oPixels[oIdx].a;
					var a = pPixels[pIdx].a * oa;
					if (oa * (oPixels[oIdx].r + oPixels[oIdx].g + oPixels[oIdx].b) > 0.05f)
					{
						oPixels[oIdx].r = oPixels[oIdx].r * (1 - a) + pPixels[pIdx].r * a;
						oPixels[oIdx].g = oPixels[oIdx].g * (1 - a) + pPixels[pIdx].g * a;
						oPixels[oIdx].b = oPixels[oIdx].b * (1 - a) + pPixels[pIdx].b * a;
					}
				}
				if (sx % 4 == 0)
					yield return null;
			}
			yield return null;
			baseData.SetPixels(x, y, new ColorData(stainWidth, stainHeight, oPixels));
		}

		public static void DrawScaledMesh(Mesh mesh, Material mat, Vector3 pos, Quaternion q, float mx, float my, float mz = 1f)
		{
			var s = new Vector3(mx, mz, my);
			var matrix = new Matrix4x4();
			matrix.SetTRS(pos, q, s);
			Graphics.DrawMesh(mesh, matrix, mat, 0);
		}
	}
}