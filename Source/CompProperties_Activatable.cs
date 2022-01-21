using Verse;

namespace ZombieLand
{
	public class CompProperties_Activatable : CompProperties
	{
		[NoTranslate]
		public string commandTexture = "UI/Commands/DesirePower";

		public CompProperties_Activatable()
		{
			this.compClass = typeof(CompActivatable);
		}
	}
}
