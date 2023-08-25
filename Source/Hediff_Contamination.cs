﻿using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class Hediff_Contamination : HediffWithComps
	{
		public override void PostAdd(DamageInfo? dinfo)
		{
			base.PostAdd(dinfo);
			Severity = Mathf.Min(1f, ContaminationManager.Instance.Get(pawn));
		}

		public override void PostTick()
		{
			base.PostTick();
			if (pawn.IsHashIntervalTick(60))
				Severity = Mathf.Min(1f, ContaminationManager.Instance.Get(pawn));
		}

		public override bool ShouldRemove => false;
		public override TextureAndColor StateIcon => new(Constants.ShowContaminationOverlay, Color.green);
		public override void Tended(float quality, float maxQuality, int batchPosition) { }
		public override bool TryMergeWith(Hediff other) => false;
		public override IEnumerable<Gizmo> GetGizmos() { yield break; }
	}
}