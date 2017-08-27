using System;
using UnityEngine;

namespace ZombieLand
{
	public class DisposableMaterial : Material, IDisposable
	{
		private bool disposedValue = false;

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
			if (!disposedValue)
			{
				if (mainTexture != null)
					Destroy(mainTexture);
				disposedValue = true;
			}
		}

		public void Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}
	}
}