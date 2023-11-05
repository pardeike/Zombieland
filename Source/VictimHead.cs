using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class VictimHead
	{
		public int t;
		public Texture2D texture;
		public Material material;
		public float alpha;
		public Vector3 position;
		public Quaternion quat;
		public float rotAngle;

		public VictimHead(Pawn victim)
		{
			t = 0;
			alpha = 1f;
			position = victim.DrawPos;
			position.y = Altitudes.AltitudeFor(AltitudeLayer.MoteOverhead);
			quat = Quaternion.AngleAxis(victim.Rotation.AsAngle, Vector3.up);
			rotAngle = Rand.Range(-10f, 10f);

			var renderTexture = RenderTexture.GetTemporary(128, 128, 32, RenderTextureFormat.ARGB32);
			Find.PawnCacheRenderer.RenderPawn(victim, renderTexture, new Vector3(0, 0, 0.4f), 1.75f, 0f, Rot4.South, true, false, true, false, true, default, null, null, false);
			Graphics.Blit(Constants.blood, renderTexture, MaterialPool.MatFrom(ShaderDatabase.Wound));
			texture = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.ARGB32, false) { name = "Chainsaw Victim Head" };
			RenderTexture.active = renderTexture;
			texture.ReadPixels(new Rect(0f, 0f, renderTexture.width, renderTexture.height), 0, 0);
			texture.Apply();
			RenderTexture.active = null;
			RenderTexture.ReleaseTemporary(renderTexture);

			material = MaterialPool.MatFrom(new MaterialRequest(texture, ShaderDatabase.Mote, Color.white));
		}

		public bool Tick()
		{
			if (t++ > 100)
				return true;

			quat = Quaternion.AngleAxis(rotAngle, Vector3.up) * quat;
			if (t >= 90)
				alpha -= 0.1f;
			return false;
		}

		public Vector3 Position
		{
			// https://www.desmos.com/calculator/taxvx1poha
			get
			{
				var x = t / 100f;
				const float a = 9;
				const float b = -0.38f;
				const float c = 8f;
				var d = x - b;
				var y = Mathf.Abs(Mathf.Sin(a * (x - b)) / d / d / d) / c;
				return position + new Vector3(x * Mathf.Sign(rotAngle), 0, y);
			}
		}

		public void Cleanup()
		{
			Object.Destroy(texture);
			Object.Destroy(material);
		}
	}
}