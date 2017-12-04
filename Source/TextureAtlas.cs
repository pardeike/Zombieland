using System.Collections.Generic;
using System.IO;
using UnityEngine;
using System.Linq;

namespace ZombieLand
{
	public struct AtlasImage
	{
		public string path;
		public ColorData data;
	}

	public class TextureAtlas
	{
		static readonly List<AtlasImage> images = new List<AtlasImage>();
		public List<AtlasImage> AllImages => images;

		public TextureAtlas(string basePath)
		{
			var atlas = new Texture2D(1, 1, TextureFormat.ARGB32, false);
			if (atlas.LoadImage(File.ReadAllBytes(basePath + ".png")) == false)
				return;

			using (var reader = new StreamReader(@basePath + ".cvs"))
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
					images.Add(new AtlasImage()
					{
						path = name,
						data = new ColorData(w, h, pixels)
					});
				}
			}
		}
	}
}