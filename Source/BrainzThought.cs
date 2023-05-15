using RimWorld;
using System;

namespace ZombieLand
{
	public class BrainzThought : Thought
	{
		public override int CurStageIndex => throw new NotImplementedException();

		public override void ExposeData()
		{
			base.ExposeData();
		}

		public override float MoodOffset()
		{
			return 1f;
		}
	}
}