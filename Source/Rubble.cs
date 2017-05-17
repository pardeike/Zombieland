using System.Collections.Generic;
using Verse;

namespace ZombieLand
{
	public class Rubble : IExposable
	{
		public float destX, destY;
		public float pX, pY;
		public float drop, dropSpeed;

		public float scale;
		public float rot;

		public static Rubble Create(float progress)
		{
			var revProgress = (1f - progress) * 1.2f;
			var dx = Rand.Range(-revProgress, revProgress);
			return new Rubble()
			{
				destX = dx,
				destY = Rand.Range(0, progress),
				scale = Rand.Range(0f, 0.5f + progress / 2f),
				pX = dx * Rand.Range(0f, 0.5f),
				pY = Rand.Range(progress / 2f, progress),
				drop = 0f,
				dropSpeed = 0f,
				rot = Rand.Range(-0.05f, 0.05f)
			};
		}

		public void ExposeData()
		{
			Scribe_Values.Look(ref destX, "destX");
			Scribe_Values.Look(ref destX, "destY");
			Scribe_Values.Look(ref pX, "pX");
			Scribe_Values.Look(ref pY, "pY");
			Scribe_Values.Look(ref drop, "drop");
			Scribe_Values.Look(ref dropSpeed, "dropSpeed");
			Scribe_Values.Look(ref dropSpeed, "dropSpeed");
			Scribe_Values.Look(ref scale, "scale");
			Scribe_Values.Look(ref rot, "rot");
		}
	}
}