using RimWorld;
using Verse;

namespace ZombieLand
{
	public class CompProperties_Breakable : CompProperties
	{
		public CompProperties_Breakable()
		{
			compClass = typeof(CompBreakable);
		}
	}

	public class CompBreakable : ThingComp
	{
		public bool broken;
		private OverlayHandle? overlayBrokenDown;

		public override void PostExposeData()
		{
			base.PostExposeData();
			Scribe_Values.Look(ref broken, "brokenDown", false, false);
		}

		private void UpdateOverlays()
		{
			if (!parent.Spawned)
				return;
			parent.Map.overlayDrawer.Disable(parent, ref overlayBrokenDown);
			if (broken)
				overlayBrokenDown = new OverlayHandle?(parent.Map.overlayDrawer.Enable(parent, OverlayTypes.BrokenDown));
		}

		public override void PostSpawnSetup(bool respawningAfterLoad)
		{
			base.PostSpawnSetup(respawningAfterLoad);
			parent.Map.GetComponent<BrokenManager>().Register(this);
			UpdateOverlays();
		}

		public override void PostDeSpawn(Map map)
		{
			base.PostDeSpawn(map);
			map.GetComponent<BrokenManager>().Deregister(this);
		}

		public void Notify_Repaired()
		{
			broken = false;
			parent.Map.GetComponent<BrokenManager>().Notify_Repaired(parent);
			UpdateOverlays();
		}

		public void DoBreakdown()
		{
			broken = true;
			parent.Map.GetComponent<BrokenManager>().Notify_BrokenDown(parent);
			UpdateOverlays();
		}

		public override string CompInspectStringExtra()
		{
			if (broken)
				return "BrokenDown".Translate();
			return null;
		}

		[DebugAction("General", "Break...", false, false, false, 0, false, actionType = DebugActionType.ToolMap, allowedGameStates = AllowedGameStates.PlayingOnMap)]
		private static void BreakDown()
		{
			foreach (Thing thing in Find.CurrentMap.thingGrid.ThingsAt(UI.MouseCell()))
			{
				var compBreakable = thing.TryGetComp<CompBreakable>();
				if (compBreakable != null && compBreakable.broken == false)
					compBreakable.DoBreakdown();
			}
		}
	}
}
