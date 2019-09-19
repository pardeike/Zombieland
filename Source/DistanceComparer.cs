using System.Collections.Generic;
using Verse;

namespace ZombieLand
{
	class DistanceComparer : IComparer<Zombie>
	{
		IntVec3 cell;

		public DistanceComparer(IntVec3 cell)
		{
			this.cell = cell;
		}

		public int Compare(Zombie z1, Zombie z2)
		{
			return z1.Position.DistanceToSquared(cell).CompareTo(z2.Position.DistanceToSquared(cell));
		}
	}
}