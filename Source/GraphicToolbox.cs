using System;
using System.IO;
using System.Linq;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	static class GraphicToolbox
	{
		public static Texture2D LoadTexture(string texturePath)
		{
			texturePath = texturePath.Replace('/', Path.DirectorySeparatorChar);
			var filePath = Tools.GetModRootDirectory() + Path.DirectorySeparatorChar + "Textures" + Path.DirectorySeparatorChar + texturePath + ".png";
			var texture2D = new Texture2D(2, 2, TextureFormat.ARGB32, true) { wrapMode = TextureWrapMode.Clamp };
			texture2D.LoadImage(File.ReadAllBytes(filePath));
			return texture2D;
		}
		/*
		public static Texture2D LoadPNG(string filePath)
		{
			Texture2D textured;
			if (File.Exists(filePath) == false) return null;

			byte[] data = File.ReadAllBytes(filePath);
			textured = new Texture2D(2, 2);
			textured.LoadImage(data);
			textured.Compress(true);
			textured.name = Path.GetFileNameWithoutExtension(filePath);
			return textured;
		}
		*/

		public static Texture2D WriteableCopy(this Texture2D original, Color color)
		{
			var texture = new Texture2D(original.width, original.height, TextureFormat.ARGB32, true) { wrapMode = TextureWrapMode.Clamp };
			var pixels = original.GetPixels();
			for (int i = 0; i < pixels.Length; i++)
			{
				pixels[i].r *= color.r;
				pixels[i].g *= color.g;
				pixels[i].b *= color.b;
			}
			texture.SetPixels(pixels);
			texture.Apply(true);
			return texture;
		}

		public static Rect MinimumFrame(this Texture2D texture)
		{
			Func<Color, bool> alphaCheck = c => c.a != 0f;

			var w = texture.width;
			var h = texture.height;
			var x1 = 0;
			var y1 = 0;
			var x2 = w;
			var y2 = h;

			for (int x = 0; x < w; x++)
			{
				if (texture.GetPixels(x, 0, 1, h).Any(alphaCheck))
				{
					x1 = Math.Max(x1, x - 5);
					break;
				}
			}
			for (int x = w - 1; x >= x1; x--)
			{
				if (texture.GetPixels(x, 0, 1, h).Any(alphaCheck))
				{
					x2 = Math.Min(x2, x + 5);
					break;
				}
			}
			for (int y = 0; y < h; y++)
			{
				if (texture.GetPixels(0, y, w, 1).Any(alphaCheck))
				{
					y1 = Math.Max(y1, y - 5);
					break;
				}
			}
			for (int y = h - 1; y >= y1; y--)
			{
				if (texture.GetPixels(0, y, w, 1).Any(alphaCheck))
				{
					y2 = Math.Min(y2, y + 5);
					break;
				}
			}

			return new Rect(x1, y1, x2 - x1, y2 - y1);
		}

		public static void ApplyStains(this Texture2D texture, Texture2D part, bool flipH, bool flipV, float px = -1f, float py = -1f)
		{
			var rect = texture.MinimumFrame();
			var w = part.width;
			var h = part.height;
			var x = (int)(rect.x + (rect.width - w) * (px != -1f ? px : Rand.Value));
			var y = (int)(rect.y + (rect.height - h) * (py != -1f ? py : Rand.Value));
			var oPixels = texture.GetPixels(x, y, w, h);
			var pPixels = part.GetPixels();
			for (int i = 0; i < w; i++)
				for (int j = 0; j < h; j++)
				{
					var pIdx = (flipH ? (w - i - 1) : i) + (flipV ? (h - j - 1) : j) * w;
					var oIdx = i + j * w;

					var oa = oPixels[oIdx].a;
					var a = pPixels[pIdx].a * oa;
					if (oa * (oPixels[oIdx].r + oPixels[oIdx].g + oPixels[oIdx].b) > 0.05f)
					{
						oPixels[oIdx].r = oPixels[oIdx].r * (1 - a) + pPixels[pIdx].r * a;
						oPixels[oIdx].g = oPixels[oIdx].g * (1 - a) + pPixels[pIdx].g * a;
						oPixels[oIdx].b = oPixels[oIdx].b * (1 - a) + pPixels[pIdx].b * a;
					}
				}
			texture.SetPixels(x, y, w, h, oPixels);
		}

		public static Color Color(this ColorHSV hsvColor)
		{
			return ColorHSV.ToColor(hsvColor);
		}

		public static Color RandomSkinColor()
		{
			var hueDelta = Rand.Range(-0.15f, 0.15f);
			var satDelta = Rand.Range(-0.2f, 0.2f);
			var britDelta = Rand.Range(-0.2f, 0.1f);
			var hsvColor = Constants.ZOMBIE_SKIN_COLOR + new ColorHSV(hueDelta, satDelta, britDelta, 0);
			hsvColor.Normalize();
			return hsvColor.Color();
		}

		public static void DrawScaledMesh(Mesh mesh, Material mat, Vector3 pos, Quaternion q, float mx, float my, float mz = 1f)
		{
			Vector3 s = new Vector3(mx, mz, my);
			Matrix4x4 matrix = new Matrix4x4();
			matrix.SetTRS(pos, q, s);
			Graphics.DrawMesh(mesh, matrix, mat, 0);
		}
	}
}
