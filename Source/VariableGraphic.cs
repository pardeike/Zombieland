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
			stainsHead.Add("Stains/Scratch", 4);
			stainsHead.Add("Stains/Stain2", 2);
			stainsHead.Add("Stains/Stain1", 1);
			stainsHead.Add("Stains/Scar", 1);

			stainsBody = new Dictionary<Texture2D, int>();
			stainsBody.Add("Stains/Ribs2", 5);
			stainsBody.Add("Stains/Ribs1", 4);
			stainsBody.Add("Stains/Scratch", 2);
			stainsBody.Add("Stains/Stain2", 2);
			stainsBody.Add("Stains/Stain1", 1);
			stainsBody.Add("Stains/Scar", 1);
		}

		static void Add(this Dictionary<Texture2D, int> dict, string name, int points)
		{
			dict.Add(ContentFinder<Texture2D>.Get(name), points);
		}

		public static KeyValuePair<Texture2D, int> GetRandom(int remainingPoints, bool isBody)
		{
			var stains = isBody ? stainsBody : stainsHead;
			return stains.Where(st => st.Value <= remainingPoints).RandomElement();
		}
	}

	public class VariableGraphic : Graphic
	{
		private Material[] mats = new Material[3];
		private int hash = 0;

		public string GraphicPath { get { return path; } }
		public override Material MatSingle { get { return mats[2]; } }
		public override Material MatFront { get { return mats[2]; } }
		public override Material MatSide { get { return mats[1]; } }
		public override Material MatBack { get { return mats[0]; } }
		public override bool ShouldDrawRotated { get { return MatSide == MatBack; } }

		public Material GetMaterial(MaterialRequest req)
		{
			var material = new Material(req.shader)
			{
				name = req.shader.name + "_" + req.mainTex.name,
				mainTexture = req.mainTex,
				color = req.color
			};
			if (req.maskTex != null)
			{
				material.SetTexture(ShaderIDs.MaskTexId, req.maskTex);
				material.SetColor(ShaderIDs.ColorTwoId, req.colorTwo);
			}
			return material;
		}

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

			mats = new Texture2D[]
			{
				ContentFinder<Texture2D>.Get(req.path + "_back"),
				ContentFinder<Texture2D>.Get(req.path + "_side"),
				ContentFinder<Texture2D>.Get(req.path + "_front")
			}
			.Select(texture => texture.WriteableCopy(GraphicToolbox.RandomSkinColor()))
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
				return GetMaterial(request); // MaterialPool.MatFrom(request);
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