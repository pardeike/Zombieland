using System;
using Verse;

namespace ZombieLand
{
	public class VariableMaterial : IDisposable
	{
		DisposableMaterial material;
		readonly MaterialRequest req;
		ColorData data;

		bool disposed;

		public VariableMaterial(MaterialRequest req, ColorData data)
		{
			this.req = req;
			this.data = data;
		}

		protected virtual void Dispose(bool disposing)
		{
			_ = disposing;
			if (!disposed)
			{
				material?.Dispose();
				disposed = true;
			}
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		public DisposableMaterial GetMaterial
		{
			get
			{
				if (material == null)
				{
					var mainTex = data.ToTexture();
					mainTex.name = "VariableMaterial";
					data = null;
					material = new DisposableMaterial(req.shader)
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
}
