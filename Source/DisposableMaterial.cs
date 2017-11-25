using System;
using UnityEngine;

namespace ZombieLand
{
	public class DisposableMaterial : Material, IDisposable
	{
		bool disposed;

		public DisposableMaterial(Shader shader) : base(shader)
		{
		}

		public DisposableMaterial(Material source) : base(source)
		{
		}

		~DisposableMaterial()
		{
			Dispose(false);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (!disposed)
			{
				if (mainTexture != null)
					Destroy(mainTexture);

				disposed = true;
			}
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}
	}
}