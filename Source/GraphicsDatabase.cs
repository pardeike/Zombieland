using Harmony;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Verse;
using System;
using RimWorld;

namespace ZombieLand
{
	[StaticConstructorOnStartup]
	public static class GraphicsDatabase
	{
		static readonly string textureRoot = Tools.GetModRootDirectory() + Path.DirectorySeparatorChar + "Textures" + Path.DirectorySeparatorChar;

		public static List<string> zombieRGBSkinColors = new List<string>();
		public static Graphic twinkieGraphic;

		static readonly Dictionary<string, ColorData> colorDataDatabase = new Dictionary<string, ColorData>();

		static GraphicsDatabase()
		{
			ReadSkinColors();

			var atlas = new TextureAtlas(textureRoot + "Parts");
			atlas.AllImages.Do(item =>
			{
				var path = item.path;
				var width = item.data.width;
				var height = item.data.height;
				var originalPixels = item.data.pixels;

				if (path.StartsWith("Zombie/", System.StringComparison.Ordinal))
				{
					if (zombieRGBSkinColors.Count == 0)
						throw new Exception("zombieRGBSkinColors not initialized");

					zombieRGBSkinColors.Do(hex =>
					{
						var pixels = originalPixels.Clone() as Color[];
						var color = hex.HexColor();
						for (var i = 0; i < pixels.Length; i++)
						{
							// Linear burn gives best coloring results
							//
							Tools.ColorBlend(ref pixels[i].r, color.r);
							Tools.ColorBlend(ref pixels[i].g, color.g);
							Tools.ColorBlend(ref pixels[i].b, color.b);
						}

						var data = new ColorData(width, height, pixels);
						colorDataDatabase.Add(path + "#" + hex, data);
					});

					// add toxic green too
					{
						var pixels = originalPixels.Clone() as Color[];
						var color = Color.green;
						for (var i = 0; i < pixels.Length; i++)
						{
							// Linear burn gives best coloring results
							//
							Tools.ColorBlend(ref pixels[i].r, color.r);
							Tools.ColorBlend(ref pixels[i].g, color.g);
							Tools.ColorBlend(ref pixels[i].b, color.b);
						}

						var data = new ColorData(width, height, pixels);
						colorDataDatabase.Add(path + "#toxic", data);
					}
				}
				else
				{
					var data = new ColorData(width, height, originalPixels);
					colorDataDatabase.Add(path, data);
				}
			});

			var graphicData = new GraphicData()
			{
				shaderType = ShaderTypeDefOf.Cutout,
				texPath = "Twinkie",
				graphicClass = typeof(Graphic_Single)
			};
			twinkieGraphic = graphicData.Graphic;
		}

		static void ReadSkinColors()
		{
			var colors = new Texture2D(1, 1, TextureFormat.ARGB32, false);
			if (colors.LoadImage(File.ReadAllBytes(textureRoot + "SkinColors.png")) == false)
				throw new Exception("Cannot read SkinColors");

			var w = colors.width / 9;
			var h = colors.height / 9;
			for (var x = 1; x <= 7; x += 2)
				for (var y = 1; y <= 7; y += 2)
				{
					var c = colors.GetPixel(x * w, y * h);
					var hexColor = string.Format("{0:x02}{1:x02}{2:x02}", (int)(c.r * 255), (int)(c.g * 255), (int)(c.b * 255));
					zombieRGBSkinColors.Add(hexColor);
				}
		}

		public static Texture2D ToTexture(this ColorData data)
		{
			var texture = new Texture2D(data.width, data.height, TextureFormat.ARGB32, false);
			texture.SetPixels(data.pixels);
			texture.Apply(true, true);
			return texture;
		}

		public static ColorData GetColorData(string path, string color, bool makeCopy = false)
		{
			var key = color == null ? path : path + "#" + color;
			if (colorDataDatabase.TryGetValue(key, out var data) == false)
			{
				Log.Error("Cannot find preloaded texture path '" + path + (color == null ? "" : "' for color '" + color + "'"));
				return null;
			}
			return makeCopy ? data.Clone() : data;
		}

		public static Texture2D GetTexture(string path)
		{
			if (colorDataDatabase.TryGetValue(path, out var data) == false)
			{
				Log.Error("Cannot find preloaded texture path '" + path);
				return null;
			}
			return data.ToTexture();
		}
	}
}