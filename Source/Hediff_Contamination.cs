using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class Hediff_Contamination : HediffWithComps
	{
		public override void PostAdd(DamageInfo? dinfo)
		{
			base.PostAdd(dinfo);
			if (Constants.CONTAMINATION)
				Severity = Mathf.Min(1f, pawn.GetContamination());
		}

		public override void Tick()
		{
			base.Tick();
			if (Constants.CONTAMINATION && pawn.IsHashIntervalTick(60))
				Severity = Mathf.Min(1f, pawn.GetContamination());
		}

		public override void PostTick()
		{
			base.PostTick();
			if (Constants.CONTAMINATION && Severity >= 1 && pawn != null && pawn.Destroyed == false && pawn.Dead == false && pawn.health?.isBeingKilled != true)
				pawn.Kill(null, this);
		}

		public override string Description => base.Description + " " + "ContaminationEffectiveness".Translate(Mathf.FloorToInt(pawn.GetEffectiveness() * 100));

		public override bool ShouldRemove => Constants.CONTAMINATION == false;
		public override TextureAndColor StateIcon
			=> ContaminationThresholds.IsVisible(pawn.GetContamination()) ? new(Constants.ShowContaminationOverlay, Color.green) : base.StateIcon;
		public override void Tended(float quality, float maxQuality, int batchPosition) { }
		public override bool TryMergeWith(Hediff other) => false;
		public override IEnumerable<Gizmo> GetGizmos() { yield break; }
	}
}
