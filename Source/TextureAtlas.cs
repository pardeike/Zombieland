using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ZombieLand
{
#pragma warning disable CA1815
	public struct AtlasImage
	{
		public string path;
		public ColorData data;
	}
#pragma warning restore CA1815

	public static class TextureAtlas
	{
		public static readonly string textureRoot = Tools.GetModRootDirectory() + Path.DirectorySeparatorChar + "Textures" + Path.DirectorySeparatorChar;

		public static List<AtlasImage> AllImages { get; } = new List<AtlasImage>();

		static TextureAtlas()
		{
			var atlas = new Texture2D(1, 1, TextureFormat.ARGB32, false);
			if (atlas.LoadImage(File.ReadAllBytes(textureRoot + "Parts.png")) == false)
				return;

			using (var reader = new StreamReader(textureRoot + "Parts.cvs"))
			{
				var listA = new List<string>();
				var listB = new List<string>();
				while (!reader.EndOfStream)
				{
					var line = reader.ReadLine();
					var vals = line.Split(',');
					if (vals.Count() != 5) continue;

					var x = int.Parse(vals[0]);
					var y = int.Parse(vals[1]);
					var w = int.Parse(vals[2]);
					var h = int.Parse(vals[3]);
					var name = vals[4];

					var pixels = atlas.GetPixels(x, y, w, h);
					AllImages.Add(new AtlasImage()
					{
						path = name,
						data = new ColorData(w, h, pixels)
					});
				}
			}
		}
	}
}