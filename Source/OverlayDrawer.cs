using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class OverlayDrawer
	{
		private readonly Material material;
		private readonly Vector3 drawSize;
		private readonly Vector3 drawOffset;

		public OverlayDrawer(string materialPath, Shader shader, Vector3 drawSize, Vector3 drawOffset)
		{
			material = MaterialPool.MatFrom(materialPath, shader);
			this.drawSize = drawSize;
			this.drawOffset = drawOffset;
		}

		public void Draw(Vector3 position, AltitudeLayer altitude, int altitudeOffset, Color color)
		{
			position.y = altitude.AltitudeFor(altitudeOffset);
			Matrix4x4 matrix = default;
			matrix.SetTRS(
			  pos: position + drawOffset,
			  q: Quaternion.identity,
			  s: drawSize
			);

			material.color = color;
			Graphics.DrawMesh(MeshPool.plane10, matrix, material, 0);
		}
	}
}