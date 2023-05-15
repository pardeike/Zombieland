using System;
using UnityEngine;

namespace ZombieLand
{
	public class ColorData
	{
		const int saveMargin = 5;

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
            var x1 = 0;
            var y1 = 0;
            var x2 = width;
            var y2 = height;


            for (var x = 0; x < width; x++)
            {
                float sum = 0.0f;
                for (var y = 0; y < height; y++)
                {
                    sum += Math.Abs(pixels[x + y * width].a);
                }
                if (sum != 0.0f)
                {
                    x1 = Math.Max(x1, x - saveMargin);
                    break;
                }
            }
            for (var x = width - 1; x >= x1; x--)
            {
                float sum = 0.0f;
                for (var y = height - 1; y >= x1; y--)
                {
                    sum += Math.Abs(pixels[x + y * width].a);
                }
                if (sum != 0.0f)
                {
                    x2 = Math.Min(x2, x + saveMargin);
                    break;
                }
            }
            for (var y = 0; y < height; y++)
            {
                float sum = 0.0f;
                for (var x = 0; x < width; x++)
                {
                    sum += Math.Abs(pixels[x * height + y].a);
                }
                if (sum != 0.0f)
                {
                    y1 = Math.Max(y1, y - saveMargin);
                    break;
                }
            }
            for (var y = height - 1; y >= y1; y--)
            {
                float sum = 0.0f;
                for (var x = width - 1; x >= y1; x--)
                {
                    sum += Math.Abs(pixels[x * height + y].a);
                }
                if (sum != 0.0f)
                {
                    y2 = Math.Min(y2, y + saveMargin);
                    break;
                }
            }

            return new Rect(x1, y1, x2 - x1, y2 - y1);
        }
	}
}
