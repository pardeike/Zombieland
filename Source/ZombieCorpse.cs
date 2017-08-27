using Harmony;
using RimWorld;
using System;
using System.Linq;
using Verse;

namespace ZombieLand
{
	public class ZombieCorpse : Corpse
	{
		public static Type type = typeof(ZombieCorpse);

		public override bool IngestibleNow => false;

		public override void SpawnSetup(Map map, bool respawningAfterLoad)
		{
			base.SpawnSetup(map, respawningAfterLoad);

			InnerPawn.Rotation = Rot4.Random;
			this.SetForbidden(false, false);

			GetComps<CompRottable>()
				.Select(comp => comp.props)
				.OfType<CompProperties_Rottable>()
				.Cast<CompProperties_Rottable>()
				.Do(rotcomp =>
				{
					rotcomp.daysToRotStart = 1f * GenTicks.SecondsToTicks(10) / 60000f;
					rotcomp.daysToDessicated = 1f * GenTicks.SecondsToTicks(30) / 60000f;
				});
		}

		public override void Destroy(DestroyMode mode = DestroyMode.Vanish)
		{
			var zombie = InnerPawn as Zombie;
			if (zombie != null)
				zombie.Dispose();

			base.Destroy(mode);
		}

		public override void DrawExtraSelectionOverlays()
		{
		}

		public override void DrawGUIOverlay()
		{
		}

		public override void TickRare()
		{
			var comps = AllComps;
			for (var i = 0; i < comps.Count; i++)
				comps[i].CompTickRare();

			if (Spawned && Bugged == false)
			{
				if (RottableUtility.GetRotStage(this) == RotStage.Dessicated)
				{
					Destroy(DestroyMode.Vanish);
					return;
				}
			}

			comps = InnerPawn.AllComps;
			for (var i = 0; i < comps.Count; i++)
				comps[i].CompTickRare();
		}
	}
}