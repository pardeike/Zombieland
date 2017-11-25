using System;
using System.Linq;
using UnityEngine;

namespace ZombieLand
{
	public class ColorData
	{
		static int saveMargin = 5;

		public int width;
		public int height;
		public Color[] pixels;
		public Rect rect;

		public ColorData(int width, int height, Color[] pixels)
		{
			this.width = width;
			this.height = height;
			this.pixels = pixels;
			rect = MinimumFrame();
		}

		public ColorData Clone()
		{
			return new ColorData(width, height, pixels.Clone() as Color[]);
		}

		public Color[] GetRawPixels(int x, int y, int blockWidth, int blockHeight)
		{
			var dest = new Color[blockWidth * blockHeight];
			for (var iy = 0; iy < blockHeight; iy++)
			{
				var destRowStart = iy * blockWidth;
				var srcRowStart = (iy + y) * width;
				for (var ix = 0; ix < blockWidth; ix++)
					dest[destRowStart + ix] = pixels[srcRowStart + ix + x];
			}
			return dest;
		}

		public ColorData GetPixels(int x, int y, int blockWidth, int blockHeight)
		{
			var dest = GetRawPixels(x, y, blockWidth, blockHeight);
			return new ColorData(blockWidth, blockHeight, dest);
		}

		public void SetPixels(int x, int y, ColorData srcData)
		{
			var blockWidth = srcData.width;
			var blockHeight = srcData.height;
			var srcPixels = srcData.pixels;

			for (var iy = 0; iy < blockHeight; iy++)
			{
				var srcRowStart = iy * blockWidth;
				var destRowStart = (iy + y) * width;
				for (var ix = 0; ix < blockWidth; ix++)
					pixels[destRowStart + ix + x] = srcPixels[srcRowStart + ix];
			}
		}

		public Rect MinimumFrame()
		{
#pragma warning disable RECS0018
			Func<Color, bool> alphaCheck = c => c.a != 0f;
#pragma warning restore RECS0018

			var x1 = 0;
			var y1 = 0;
			var x2 = width;
			var y2 = height;

			for (var x = 0; x < width; x++)
			{
				if (GetRawPixels(x, 0, 1, height).Any(alphaCheck))
				{
					x1 = Math.Max(x1, x - saveMargin);
					break;
				}
			}
			for (var x = width - 1; x >= x1; x--)
			{
				if (GetRawPixels(x, 0, 1, height).Any(alphaCheck))
				{
					x2 = Math.Min(x2, x + saveMargin);
					break;
				}
			}
			for (var y = 0; y < height; y++)
			{
				if (GetRawPixels(0, y, width, 1).Any(alphaCheck))
				{
					y1 = Math.Max(y1, y - saveMargin);
					break;
				}
			}
			for (var y = height - 1; y >= y1; y--)
			{
				if (GetRawPixels(0, y, width, 1).Any(alphaCheck))
				{
					y2 = Math.Min(y2, y + saveMargin);
					break;
				}
			}

			return new Rect(x1, y1, x2 - x1, y2 - y1);
		}
	}
}