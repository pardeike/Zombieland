using RimWorld;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class ContaminationNeed : Need
	{
		public int lastGainTick = -999;

		public ContaminationNeed(Pawn pawn) : base(pawn)
		{
		}

		public override float CurLevel
		{
			get => pawn.GetContamination();
			set
			{
				if (DebugSettings.ShowDevGizmos == false)
					return;
				pawn.ClearContamination();
				pawn.AddContamination(value, null);
			}
		}

		public override int GUIChangeArrow => Find.TickManager.TicksGame < lastGainTick + 10 ? 1 : 0;
		public override bool IsFrozen => false;

		public override void NeedInterval() { }
		public override void SetInitialLevel() { }

		public override void DrawOnGUI(Rect rect, int maxThresholdMarkers = int.MaxValue, float customMargin = -1, bool drawArrows = true, bool doTooltip = true, Rect? rectForTooltip = null, bool drawLabel = true)
		{
			base.DrawOnGUI(rect, maxThresholdMarkers, customMargin, drawArrows, doTooltip, rectForTooltip, drawLabel);
		}
	}
}