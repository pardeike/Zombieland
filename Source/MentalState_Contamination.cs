using Verse.AI;

namespace ZombieLand
{
	public class MentalState_Contamination : MentalState
	{
		public MentalState_Contamination()
		{
		}

		public override bool CanEndBeforeMaxDurationNow => false;
	}
}