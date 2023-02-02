using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class Graphic_Breakable : Graphic_Collection
	{
		public override Material MatSingle => subGraphics[0].MatSingle;

		public override Graphic GetColoredVersion(Shader newShader, Color newColor, Color newColorTwo)
		{
			return GraphicDatabase.Get<Graphic_Breakable>(path, newShader, drawSize, newColor, newColorTwo, data, null);
		}

		public override Material MatAt(Rot4 rot, Thing thing = null)
		{
			if (thing == null)
				return MatSingle;
			return MatSingleFor(thing);
		}

		public override Material MatSingleFor(Thing thing)
		{
			if (thing == null)
				return MatSingle;
			return SubGraphicFor(thing).MatSingle;
		}

		public virtual Graphic SubGraphicFor(Thing thing)
		{
			return SubGraphicForBreakState(thing.IsBroken());
		}

		public override void DrawWorker(Vector3 loc, Rot4 rot, ThingDef thingDef, Thing thing, float extraRotation)
		{
			var graphic = thing != null ? SubGraphicFor(thing) : subGraphics[0];
			graphic.DrawWorker(loc, rot, thingDef, thing, extraRotation);
		}

		public Graphic SubGraphicForBreakState(bool brokenDown)
		{
			return subGraphics.Length switch
			{
				2 => subGraphics[brokenDown ? 1 : 0],
				_ => subGraphics[0],
			};
		}

		public override string ToString()
		{
			return string.Concat(new object[]
			{
				"Broken(path=",
				path,
				", count=",
				subGraphics.Length,
				")"
			});
		}

	}
}