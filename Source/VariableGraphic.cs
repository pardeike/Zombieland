using System.Linq;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class PreparedMaterial
	{
		Material material;
		MaterialRequest req;
		ColorData data;

		public PreparedMaterial(MaterialRequest req, ColorData data)
		{
			this.req = req;
			this.data = data;
		}

		~PreparedMaterial()
		{
			var tex = material?.mainTexture;
			if (tex != null)
				Object.Destroy(tex);
		}

		public Material GetMaterial
		{
			get
			{
				if (material == null)
				{
					var mainTex = data.ToTexture();
					data = null;
					material = new Material(req.shader)
					{
						name = req.shader.name + "_" + mainTex.name,
						mainTexture = mainTex,
						color = req.color
					};
					if (req.maskTex != null)
					{
						material.SetTexture(ShaderPropertyIDs.MaskTex, req.maskTex);
						material.SetColor(ShaderPropertyIDs.ColorTwo, req.colorTwo);
					}
				}
				return material;
			}
		}
	}

	public class VariableGraphic : Graphic
	{
		private PreparedMaterial[] mats = new PreparedMaterial[3];
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
				return new PreparedMaterial(request, data);
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