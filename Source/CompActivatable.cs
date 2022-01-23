using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace ZombieLand
{
	public class CompActivatable : ThingComp
	{
		private Texture2D cachedCommandTex;

		private CompProperties_Activatable Props => (CompProperties_Activatable)props;
		public Graphic CurrentGraphic => parent.DefaultGraphic;

		private Texture2D CommandTex
		{
			get
			{
				if (cachedCommandTex == null)
					cachedCommandTex = ContentFinder<Texture2D>.Get(Props.commandTexture, true);
				return cachedCommandTex;
			}
		}

		public void Activate()
		{
			SoundDefOf.FlickSwitch.PlayOneShot(new TargetInfo(parent.Position, parent.Map, false));
			parent.BroadcastCompSignal("Activate");
			if (parent.Spawned)
				parent.Map.mapDrawer.MapMeshDirty(parent.Position, MapMeshFlag.Things | MapMeshFlag.Buildings);
		}

		/*
		public override IEnumerable<Gizmo> CompGetGizmosExtra()
		{
			foreach (Gizmo gizmo in base.CompGetGizmosExtra())
				yield return gizmo;

			if (parent.Faction != Faction.OfPlayer)
				yield break;

			yield return new Command_Action
			{
				icon = CommandTex,
				defaultLabel = "Activate".Translate(),
				defaultDesc = "ActivateDescription".Translate(),
				action = Activate
			};
		}
		*/
	}
}
