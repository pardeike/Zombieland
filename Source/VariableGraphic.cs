using System;
using System.Linq;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class VariableGraphic : Graphic, IDisposable
	{
		VariableMaterial[] mats = new VariableMaterial[3];
		int hash;
		public string bodyColor;

		public string GraphicPath => path;
		public override Material MatSingle => mats[2].GetMaterial;
		public override Material MatSouth => mats[2].GetMaterial;
		public override Material MatWest => mats[1].GetMaterial;
		public override Material MatNorth => mats[0].GetMaterial;
		public override bool ShouldDrawRotated => MatWest == MatNorth;

		public override void Init(GraphicRequest req)
		{
			data = req.graphicData;
			path = req.path;
			color = req.color;
			colorTwo = req.colorTwo;
			drawSize = req.drawSize;

			hash = Gen.HashCombine(hash, path);
			hash = Gen.HashCombineStruct(hash, color);
			hash = Gen.HashCombineStruct(hash, colorTwo);

			mats = new ColorData[]
			{
				GraphicsDatabase.GetColorData(req.path + "_north", bodyColor, true),
				GraphicsDatabase.GetColorData(req.path + "_west", bodyColor, true),
				GraphicsDatabase.GetColorData(req.path + "_south", bodyColor, true)
			}
			.Select(data =>
			{
				var points = ZombieStains.maxStainPoints;
				while (points > 0)
				{
					var stain = ZombieStains.GetRandom(points, req.path.Contains("Naked"));
					data.ApplyStains(stain.Key, Rand.Bool, Rand.Bool);
					points -= stain.Value;

					hash = Gen.HashCombine(hash, stain);
				}

				var request = new MaterialRequest
				{
					mainTex = null, // will be calculated lazy from 'data'
					shader = req.shader,
					color = color,
					colorTwo = colorTwo,
					maskTex = null
				};
				return new VariableMaterial(request, data);
			})
			.ToArray();
		}

		public void Dispose()
		{
			Dispose(true);
		}

		void Dispose(bool v)
		{
			if (mats != null)
			{
				foreach (var mat in mats)
					mat.Dispose();
				mats = null;
			}
		}

		public override Graphic GetColoredVersion(Shader newShader, Color newColor, Color newColorTwo)
		{
			return this;
		}

		public override string ToString()
		{
			return string.Concat(new object[]
			{
				"Multi(initPath=",
				path,
				", color=",
				color,
				", colorTwo=",
				colorTwo,
				")"
			});
		}

		public override int GetHashCode()
		{
			return hash;
		}
	}
}