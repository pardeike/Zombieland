using Mono.Unix.Native;
using Verse;

namespace ZombieLand
{
	public class CompProperties_ZombieShocker : CompProperties
	{
		public CompProperties_ZombieShocker()
		{
			compClass = typeof(CompZombieShocker);
		}
	}

	public class CompZombieShocker : ThingComp
	{
		public CompProperties_ZombieShocker Props => (CompProperties_ZombieShocker)props;

		public override void CompTick()
		{
			base.CompTick();
			if (OnWall() == false)
				parent?.Destroy();
		}

		public bool OnWall()
		{
			if (parent?.Map == null)
				return false;
			return parent.Map.edificeGrid[parent.Position] != null;
		}
	}
}