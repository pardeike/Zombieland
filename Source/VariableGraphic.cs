using System;
using System.Collections;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class VariableGraphic : Graphic, IDisposable
	{
		VariableMaterial[] mats = new VariableMaterial[4];
		int hash;
		public string bodyColor;

		public string GraphicPath => path;
		public override Material MatNorth => mats[0].GetMaterial;
		public override Material MatEast => mats[1].GetMaterial;
		public override Material MatSouth => mats[2].GetMaterial;
		public override Material MatWest => mats[3].GetMaterial;
		public override bool ShouldDrawRotated => MatWest == MatNorth;
		public override Material MatSingle => mats[2].GetMaterial;

		static readonly string[] directions = new[] { "_north", "_east", "_south", "_east" };
		public IEnumerator InitIterativ(GraphicRequest req, int n, int points)
		{
			var data = GraphicsDatabase.GetColorData(req.path + directions[n], bodyColor, true);
			yield return null;

			while (points > 0)
			{
				var stain = ZombieStains.GetRandom(points, req.path.Contains("Naked"));
				var it = data.ApplyStainsIterativ(stain.Key, Rand.Bool, Rand.Bool);
				while (it.MoveNext())
					yield return null;
				points -= stain.Value;

				hash = Gen.HashCombine(hash, stain);
				yield return null;
			}

			var request = new MaterialRequest
			{
				mainTex = null, // will be calculated lazy from 'data'
				shader = req.shader,
				color = color,
				colorTwo = colorTwo,
				maskTex = null
			};
			mats[n] = new VariableMaterial(request, data);
		}

		public static GraphicRequest minimal = new GraphicRequest();
		public override void Init(GraphicRequest req)
		{
			if (req == minimal)
				return;

			data = req.graphicData;
			path = req.path;
			color = req.color;
			colorTwo = req.colorTwo;
			drawSize = req.drawSize;

			hash = Gen.HashCombine(hash, path);
			hash = Gen.HashCombineStruct(hash, color);
			hash = Gen.HashCombineStruct(hash, colorTwo);

			for (var i = 0; i < 4; i++)
			{
				var iterator = InitIterativ(req, i, ZombieStains.maxStainPoints);
				while (iterator.MoveNext()) ;
			}
		}

		public void Dispose()
		{
			Dispose(true);
		}

		void Dispose(bool v)
		{
			_ = v;
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