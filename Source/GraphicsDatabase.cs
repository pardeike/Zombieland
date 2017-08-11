using Harmony;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	[StaticConstructorOnStartup]
	public static class GraphicsDatabase
	{
		public static List<string> ZombieRGBSkinColors = new List<string>();
		public static Graphic TwinkieGraphic;

		static Dictionary<string, ColorData> database = new Dictionary<string, ColorData>();
		static string textureRoot = Tools.GetModRootDirectory() + Path.DirectorySeparatorChar + "Textures" + Path.DirectorySeparatorChar;
		static string pngSuffix = ".png";

		static GraphicsDatabase()
		{
			LoadSkinColors();

			AllZombieImagesAsPath().Do(path =>
			{
				var key = path.Replace('\\', '/');
				var filePath = textureRoot + path.Replace('/', Path.DirectorySeparatorChar) + pngSuffix;
				var rootTexture = new Texture2D(2, 2, TextureFormat.ARGB32, true) { wrapMode = TextureWrapMode.Clamp };
				if (rootTexture.LoadImage(File.ReadAllBytes(filePath)) == false)
					throw new System.Exception("Cannot load texture " + filePath);
				var width = rootTexture.width;
				var height = rootTexture.height;
				var originalPixels = rootTexture.GetPixels();

				if (key.StartsWith("Zombie/"))
				{
					ZombieRGBSkinColors.Do(hex =>
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
						database.Add(key + "#" + hex, data);
					});
				}
				else
				{
					var data = new ColorData(width, height, originalPixels);
					database.Add(key, data);
				}
			});

			var graphicData = new GraphicData()
			{
				shaderType = ShaderType.Cutout,
				texPath = "Twinkie",
				graphicClass = typeof(Graphic_Single)
			};
			TwinkieGraphic = graphicData.Graphic;
		}

		static void LoadSkinColors()
		{
			var colors = new Texture2D(2, 2, TextureFormat.ARGB32, false);
			if (colors.LoadImage(File.ReadAllBytes(textureRoot + "SkinColors.png")) == false)
				throw new System.Exception("Cannot load texture " + textureRoot + "SkinColors.png");
			var w = colors.width / 9;
			var h = colors.height / 9;
			for (var x = 1; x <= 7; x += 2)
				for (var y = 1; y <= 7; y += 2)
				{
					var c = colors.GetPixel(x * w, y * h);
					var hexColor = string.Format("{0:x02}{1:x02}{2:x02}", (int)(c.r * 255), (int)(c.g * 255), (int)(c.b * 255));
					ZombieRGBSkinColors.Add(hexColor);
				}
		}

		public static Texture2D LoadTexture(string path, int width, int height)
		{
			var texture = new Texture2D(width, height, TextureFormat.ARGB32, false);
			var filePath = textureRoot + path.Replace('/', Path.DirectorySeparatorChar) + pngSuffix;
			if (texture.LoadImage(File.ReadAllBytes(filePath)) == false)
				return null;
			return texture;
		}

		public static ColorData GetColorData(string path, string color, bool makeCopy = false)
		{
			ColorData data;
			var key = color == null ? path : path + "#" + color;
			if (database.TryGetValue(key, out data) == false)
			{
				Log.Error("Cannot find preloaded texture path '" + path + (color == null ? "" : "' for color '" + color + "'"));
				return null;
			}
			return makeCopy ? data.Clone() : data;
		}

		public static Texture2D ToTexture(this ColorData data)
		{
			var texture = new Texture2D(data.width, data.height, TextureFormat.ARGB32, true);
			texture.SetPixels(data.pixels);
			texture.Apply(true, true);
			return texture;
		}

		public static List<string> AllZombieImagesAsPath()
		{
			return Directory
				.GetFiles(textureRoot, "*.png", SearchOption.AllDirectories)
				.Select(path => path.Replace(textureRoot, "").Replace(pngSuffix, ""))
				.ToList();
		}
	}
}