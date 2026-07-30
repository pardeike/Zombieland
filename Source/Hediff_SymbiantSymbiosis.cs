using RimWorld;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class Hediff_SymbiantSymbiosis : HediffWithComps
	{
		const int SyncInterval = 250;
		const float CapacityFactorPerBenefit = 0.25f;

		HediffStage capacityBenefitStage;
		PawnCapacityModifier movingCapacityModifier;
		PawnCapacityModifier manipulationCapacityModifier;

		public string symbiantThingId;

		public override HediffStage CurStage
		{
			get
			{
				var movingBenefitCount = ZombieSymbiant.MoveSpeedBenefitCount(pawn);
				var manipulationBenefitCount = ZombieSymbiant.ManipulationBenefitCount(pawn);
				if (movingBenefitCount <= 0 && manipulationBenefitCount <= 0)
					return base.CurStage;
				capacityBenefitStage ??= new HediffStage();
				movingCapacityModifier ??= new PawnCapacityModifier
				{
					capacity = PawnCapacityDefOf.Moving
				};
				manipulationCapacityModifier ??= new PawnCapacityModifier
				{
					capacity = PawnCapacityDefOf.Manipulation
				};
				capacityBenefitStage.capMods.Clear();
				if (movingBenefitCount > 0)
				{
					movingCapacityModifier.postFactor = 1f + movingBenefitCount * CapacityFactorPerBenefit;
					capacityBenefitStage.capMods.Add(movingCapacityModifier);
				}
				if (manipulationBenefitCount > 0)
				{
					manipulationCapacityModifier.postFactor = 1f + manipulationBenefitCount * CapacityFactorPerBenefit;
					capacityBenefitStage.capMods.Add(manipulationCapacityModifier);
				}
				return capacityBenefitStage;
			}
		}

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
				) + "\n\n" + "SymbiantSharedHealthRecoveryDescription".Translate(
					ZombieSymbiant.SharedHealthRecoveryDelayTicks.ToStringTicksToPeriod(),
					ZombieSymbiant.SharedHealthRecoveryMissingFraction.ToStringPercent(),
					ZombieSymbiant.SharedHealthRecoveryIntervalTicks.ToStringTicksToPeriod()
				);
			}
		}

		public override string TipStringExtra
		{
			get
			{
				var extra = base.TipStringExtra;
				if (extra.NullOrEmpty())
					return extra;
				if (ZombieSymbiant.MoveSpeedBenefitCount(pawn) <= 0
					&& ZombieSymbiant.ManipulationBenefitCount(pawn) <= 0)
					return extra;
				return "SymbiantCombinedCapacityEffects".Translate().Resolve()
					+ "\n" + extra.TrimStart('\r', '\n');
			}
		}

		public override string SeverityLabel => null;
		public override float SummaryHealthPercentImpact => 0f;
		public override float BleedRate => 0f;
		public override float PainOffset => 0f;

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
			symbiant.SyncHostDamageEchoes(pawn);
		}

		public override bool TendableNow(bool ignoreTimer = false) => false;
		public override bool CauseDeathNow() => false;
		public override bool TryMergeWith(Hediff other) => false;

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref symbiantThingId, "symbiantThingId");
		}
	}
}
