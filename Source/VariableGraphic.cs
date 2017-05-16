using Harmony;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	[StaticConstructorOnStartup]
	static class ZombieStains
	{
		static Dictionary<Texture2D, int> stainsHead;
		static Dictionary<Texture2D, int> stainsBody;

		static ZombieStains()
		{
			stainsHead = new Dictionary<Texture2D, int>();
			stainsHead.Add(ContentFinder<Texture2D>.Get("Stains/Scratch"), 4);
			stainsHead.Add(ContentFinder<Texture2D>.Get("Stains/Stain2"), 2);
			stainsHead.Add(ContentFinder<Texture2D>.Get("Stains/Stain1"), 1);
			stainsHead.Add(ContentFinder<Texture2D>.Get("Stains/Scar"), 1);

			stainsBody = new Dictionary<Texture2D, int>();
			stainsBody.Add(ContentFinder<Texture2D>.Get("Stains/Ribs2"), 5);
			stainsBody.Add(ContentFinder<Texture2D>.Get("Stains/Ribs1"), 4);
			stainsBody.Add(ContentFinder<Texture2D>.Get("Stains/Scratch"), 2);
			stainsBody.Add(ContentFinder<Texture2D>.Get("Stains/Stain2"), 2);
			stainsBody.Add(ContentFinder<Texture2D>.Get("Stains/Stain1"), 1);
		}

		public static KeyValuePair<Texture2D, int> GetRandom(int remainingPoints, bool isBody)
		{
			var stains = isBody ? stainsBody : stainsHead;
			return stains.Where(st => st.Value <= remainingPoints).RandomElement();
		}
	}

	class VariableGraphic : Graphic
	{
		private Material[] mats = new Material[3];
		private int hash = 0;

		public string GraphicPath { get { return path; } }
		public override Material MatSingle { get { return mats[2]; } }
		public override Material MatFront { get { return mats[2]; } }
		public override Material MatSide { get { return mats[1]; } }
		public override Material MatBack { get { return mats[0]; } }
		public override bool ShouldDrawRotated { get { return MatSide == MatBack; } }

		public override void Init(GraphicRequest req)
		{
			data = req.graphicData;
			path = req.path;
			color = Color.white;
			colorTwo = Color.white;
			drawSize = req.drawSize;

			hash = Gen.HashCombine(hash, path);
			hash = Gen.HashCombineStruct(hash, color);
			hash = Gen.HashCombineStruct(hash, colorTwo);

			mats = new Texture2D[]
			{
				ContentFinder<Texture2D>.Get(req.path + "_back"),
				ContentFinder<Texture2D>.Get(req.path + "_side"),
				ContentFinder<Texture2D>.Get(req.path + "_front")
			}
			.Select(texture => texture.WriteableCopy(req.color))
			.Select(texture =>
			{
				var points = 6;
				while (points > 0)
				{
					var stain = ZombieStains.GetRandom(points, req.path.Contains("Naked"));
					texture.ApplyStains(stain.Key, Rand.Bool, Rand.Bool);
					points -= stain.Value;

					hash = Gen.HashCombine(hash, stain);
				}

				texture.Apply(true, true);
				return texture;
			})
			.Select(texture =>
			{
				MaterialRequest request = new MaterialRequest
				{
					mainTex = texture,
					shader = req.shader,
					color = color,
					colorTwo = colorTwo,
					maskTex = null
				};
				return MaterialPool.MatFrom(request);
			})
			.ToArray();
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