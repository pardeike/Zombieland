using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class Hediff_SymbiantSymbiosis : HediffWithComps
	{
		const int SyncInterval = 250;

		public string symbiantThingId;

		public override string Description
		{
			get
			{
				var description = base.Description;
				if (pawn == null)
					return description;
				var symbiant = ZombieSymbiant.LinkedSymbiantFor(pawn);
				if (symbiant == null)
					return description + "\n\n" + "SymbiantHostBondMissing".Translate();
				if (symbiant.IsActiveBondWith(pawn) == false)
					return "SymbiantHostRelocatedMessage".Translate(pawn.LabelShortCap);
				return description + "\n\n" + "SymbiantHostBondDescription".Translate(
					symbiant.CellCount,
					ZombieSymbiant.MaxCells,
					symbiant.NextBenefitCellSize,
					symbiant.SharedHealthSummary,
					symbiant.SharedDamageLeakPercentDisplay,
					symbiant.BenefitSummary
				);
			}
		}

		public override bool ShouldRemove
		{
			get
			{
				if (ZombieSymbiant.DebugDisableHostHediffSync)
					return false;
				return ZombieSymbiant.LinkedSymbiantFor(pawn) == null;
			}
		}

		public override void Tick()
		{
			if (ZombieSymbiant.DebugDisableHostHediffSync)
				return;
			base.Tick();
			if (pawn.IsHashIntervalTick(SyncInterval) == false)
				return;
			var symbiant = ZombieSymbiant.LinkedSymbiantFor(pawn);
			if (symbiant == null)
			{
				pawn.health.RemoveHediff(this);
				return;
			}
			symbiantThingId = symbiant.ThingID;
			Severity = ZombieSymbiant.HostHediffSeverity(ZombieSymbiant.SymbiantBenefitFactor(pawn));
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref symbiantThingId, "symbiantThingId");
		}
	}
}
