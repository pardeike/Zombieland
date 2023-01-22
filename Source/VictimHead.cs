using UnityEngine;

namespace ZombieLand
{
	public class VictimHead
	{
		public int t;
		public Material material;
		public float alpha;
		public Vector3 position;
		public Quaternion quat;
		public float rotAngle;

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
	}
}