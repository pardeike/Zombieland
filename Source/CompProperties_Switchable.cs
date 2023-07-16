using RimWorld;
using System.Collections.Generic;
using Verse;

namespace ZombieLand
{
	public class CompProperties_Switchable : CompProperties
	{
		public CompProperties_Switchable()
		{
			compClass = typeof(CompSwitchable);
		}
	}

	public class CompSwitchable : ThingComp
	{
		public bool isActive = true;

		public override void PostExposeData()
		{
			base.PostExposeData();
			Scribe_Values.Look(ref isActive, "isSwitched", true);
		}

		public override IEnumerable<Gizmo> CompGetGizmosExtra()
		{
			foreach (Gizmo gizmo in base.CompGetGizmosExtra())
				yield return gizmo;

			if (parent.Faction != Faction.OfPlayer)
				yield break;

			yield return new Command_SimpleToggle
			{
				hotKey = KeyBindingDefOf.Command_TogglePower,
				icon = isActive ? Constants.toggledOn : Constants.toggledOff,
				defaultLabel = "ToggleStateLabel".Translate(),
				defaultDesc = "ToggleStateDesc".Translate(),
				isActive = () => isActive,
				toggleAction = () => { isActive = !isActive; }
			};
		}
	}
}