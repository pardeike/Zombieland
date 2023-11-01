using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace ZombieLand
{
	public struct AtlasImage
	{
		public string path;
		public ColorData data;
		public bool noRecolor;
	}

	public class AtlasMeta
	{
		public float fill_rate;
		public int grid_height;
		public int grid_width;
		public int height;
		public string image;
		public string name;
		public int padding_x;
		public int padding_y;
		public bool pot;
		public AtlasItem[] regions;
		public int regions_count;
		public bool rotations;
		public bool using_grid;
		public int width;
	}

	public class AtlasItem
	{
		public int idx;
		public string name;
		public int[] origin;
		public int[] rect;
		public bool rotated;
	}

	// uses https://github.com/witnessmonolith/atlased_documentation
	public static class TextureAtlas
	{
		public static readonly string textureRoot = Tools.GetModRootDirectory() + Path.DirectorySeparatorChar + "Textures" + Path.DirectorySeparatorChar;

		public static List<AtlasImage> AllImages { get; } = new List<AtlasImage>();

		static TextureAtlas()
		{
			var atlas = new Texture2D(1, 1, TextureFormat.ARGB32, false) { name = "PartsNew.png" };
			if (atlas.LoadImage(File.ReadAllBytes(textureRoot + "PartsNew.png")) == false)
				return;

			using var reader = new StreamReader(textureRoot + "PartsNew.json");
			var jsonReader = new JsonTextReader(reader);
			var serializer = new JsonSerializer();
			var meta = serializer.Deserialize<AtlasMeta>(jsonReader);
			AllImages = meta.regions
				.Select(r =>
				{
					var (x, y, w, h, name) = (r.rect[0], r.rect[1], r.rect[2], r.rect[3], r.name);
					name = name
						.Replace("_front", "_south")
						.Replace("_back", "_north")
						.Replace("_side", "_east");
					y = meta.height - y - h;
					return new AtlasImage()
					{
						path = name,
						data = new ColorData(w, h, atlas.GetPixels(x, y, w, h))
					};
				})
				.ToList();
		}
	}
}
