using RimWorld;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class Thought_Zombie : Thought_Memory, ISocialThought
	{
		public float opinionOffset;

		public override bool ShouldDiscard => otherPawn == null || opinionOffset == 0f || base.ShouldDiscard;
		public override bool VisibleInNeedsTab => base.VisibleInNeedsTab && MoodOffset() != 0f;
		private float AgePct => age / (float)DurationTicks;
		private float AgeFactor => Mathf.InverseLerp(1f, def.lerpOpinionToZeroAfterDurationPct, AgePct);

		public float OpinionOffset()
		{
			if (ThoughtUtility.ThoughtNullified(pawn, def) || ShouldDiscard)
				return 0f;
			return opinionOffset * AgeFactor;
		}

		public Pawn OtherPawn() => otherPawn;

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look<float>(ref opinionOffset, "opinionOffset", 0f, false);
		}

		public override void Init()
		{
			base.Init();
			opinionOffset = CurStage.baseOpinionOffset;
		}

		public override bool TryMergeWithExistingMemory(out bool showBubble)
		{
			showBubble = false;
			return false;
		}

		public override bool GroupsWith(Thought other)
		{
			return other is Thought_MemorySocial thought_MemorySocial && base.GroupsWith(other) && otherPawn == thought_MemorySocial.otherPawn;
		}
	}
}
