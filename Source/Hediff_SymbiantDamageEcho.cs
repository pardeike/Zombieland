using UnityEngine;
using Verse;

namespace ZombieLand
{
	public sealed class SymbiantDamageEchoRecord : IExposable
	{
		public string categoryKey;
		public string cachedLabel;
		public float amount;

		public void ExposeData()
		{
			Scribe_Values.Look(ref categoryKey, "categoryKey");
			Scribe_Values.Look(ref cachedLabel, "cachedLabel");
			Scribe_Values.Look(ref amount, "amount");
		}
	}

	public class Hediff_SymbiantDamageEcho : Hediff
	{
		public string symbiantThingId;
		public string categoryKey;
		public string cachedCategoryLabel;
		public float displayAmount;

		ZombieSymbiant LinkedSymbiant
		{
			get
			{
				var symbiant = ZombieSymbiant.LinkedSymbiantFor(pawn);
				return symbiant?.ThingID == symbiantThingId ? symbiant : null;
			}
		}

		bool IsCurrentAndActive
		{
			get
			{
				var symbiant = LinkedSymbiant;
				return symbiant != null
					&& symbiant.IsActiveBondWith(pawn)
					&& symbiant.HasDamageEchoCategory(categoryKey);
			}
		}

		public override string Label => "SymbiantDamageEchoLabel".Translate(
			(cachedCategoryLabel.NullOrEmpty() ? "SymbiantDamageEchoOther".Translate().Resolve() : cachedCategoryLabel).CapitalizeFirst(),
			ZombieSymbiant.FormatDamageEchoAmount(displayAmount)
		).Resolve();

		public override string SeverityLabel => null;
		public override Color LabelColor => Color.gray;
		public override int UIGroupKey => GenText.StableStringHash((symbiantThingId ?? "") + "|" + (categoryKey ?? ""));
		public override bool Visible => IsCurrentAndActive;
		public override bool ShouldRemove => IsCurrentAndActive == false;
		public override float SummaryHealthPercentImpact => 0f;
		public override float BleedRate => 0f;
		public override float PainOffset => 0f;

		public override string Description => "SymbiantDamageEchoDescription".Translate(
			ZombieSymbiant.FormatDamageEchoAmount(displayAmount)
		).Resolve();

		public override bool TendableNow(bool ignoreTimer = false) => false;
		public override bool CauseDeathNow() => false;
		public override bool TryMergeWith(Hediff other) => false;

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref symbiantThingId, "symbiantThingId");
			Scribe_Values.Look(ref categoryKey, "categoryKey");
			Scribe_Values.Look(ref cachedCategoryLabel, "cachedCategoryLabel");
			Scribe_Values.Look(ref displayAmount, "displayAmount");
		}
	}
}
