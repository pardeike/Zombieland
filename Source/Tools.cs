using Verse;

namespace ZombieLand
{
	class Tools
	{
		public static void ReApplyThingToListerThings(IntVec3 cell, Thing thing)
		{
			if ((((cell != IntVec3.Invalid) && (thing != null)) && (thing.Map != null)) && thing.Spawned)
			{
				Map map = thing.Map;
				RegionGrid regionGrid = map.regionGrid;
				Region validRegionAt = null;
				if (cell.InBounds(map))
				{
					validRegionAt = regionGrid.GetValidRegionAt(cell);
				}
				if ((validRegionAt != null) && !validRegionAt.ListerThings.Contains(thing))
				{
					validRegionAt.ListerThings.Add(thing);
				}
			}
		}

	}
}