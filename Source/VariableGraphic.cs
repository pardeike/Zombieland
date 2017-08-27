using System;
using System.Linq;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class VariableGraphic : Graphic, IDisposable
	{
		private VariableMaterial[] mats = new VariableMaterial[3];
		private int hash;

		public string GraphicPath => path;
		public override Material MatSingle => mats[2].GetMaterial;
		public override Material MatFront => mats[2].GetMaterial;
		public override Material MatSide => mats[1].GetMaterial;
		public override Material MatBack => mats[0].GetMaterial;
		public override bool ShouldDrawRotated => MatSide == MatBack;

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

			var bodyColor = GraphicToolbox.RandomSkinColorString();
			mats = new ColorData[]
			{
				GraphicsDatabase.GetColorData(req.path + "_back", bodyColor, true),
				GraphicsDatabase.GetColorData(req.path + "_side", bodyColor, true),
				GraphicsDatabase.GetColorData(req.path + "_front", bodyColor, true)
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

		private void Dispose(bool v)
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