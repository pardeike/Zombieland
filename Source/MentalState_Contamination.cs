using Verse.AI;

namespace ZombieLand
{
	public class MentalState_Contamination : MentalState
	{
		public override bool CanEndBeforeMaxDurationNow => false;
	}
}
