using RimWorld;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class TarSlime : Filth
	{
		static readonly Vector2 size1 = new(0.85f, 0.85f);
		static readonly Vector2 size2 = new(1.5f, 1.5f);
		static readonly Vector2 size3 = new(2f, 2f);

		public override void SpawnSetup(Map map, bool respawningAfterLoad)
		{
			if (map.thingGrid.ThingAt<TarSlime>(Position) != null)
				return;
			if (Position.Standable(map) == false)
				return;
			if (respawningAfterLoad == false)
				thickness = 4;
			base.SpawnSetup(map, respawningAfterLoad);
		}

		public override void ThickenFilth()
		{
			// intentionally empty
		}

		public override void Destroy(DestroyMode mode)
		{
			if (thickness == 0)
				base.Destroy(mode);
		}

		public override void Print(SectionLayer layer)
		{
			if (thickness <= 0 || thickness > 4) return;

			var n = Position.x * 7879 + Position.z * 6577;
			var mat_heavy = Constants.TARSLIMES[thickness - 1];
			var mat_light = thickness == 1 ? null : Constants.TARSLIMES[thickness - 2];

			var mat1 = mat_heavy[(n >> 0) % 8];
			var rot1 = ((n >> 3) % 4) * 90;

			var mat2 = mat_heavy[(n >> 5) % 8];
			var rot2 = ((n >> 8) % 4) * 90;

			Material mat3 = null;
			int rot3 = 0;
			if (mat_light != null)
			{
				mat3 = mat_light[(n >> 10) % 8];
				rot3 = ((n >> 13) % 4) * 90;
			}

			Printer_Plane.PrintPlane(layer, this.TrueCenter(), size1, mat1, rot1, false, null, null, 0.01f, 0f);
			Printer_Plane.PrintPlane(layer, this.TrueCenter(), size2, mat2, rot2, false, null, null, 0.01f, 0f);
			if (mat3 != null)
				Printer_Plane.PrintPlane(layer, this.TrueCenter(), size3, mat3, rot3, false, null, null, 0.01f, 0f);
		}
	}
}
