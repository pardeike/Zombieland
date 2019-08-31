using Verse;
using Verse.AI;

namespace ZombieLand
{
	public class JobGiver_Stumble : ThinkNode_JobGiver
	{
		public override ThinkNode DeepCopy(bool resolve = true)
		{
			return (JobGiver_Stumble)base.DeepCopy(resolve);
		}

		protected override Job TryGiveJob(Pawn pawn)
		{
			return new Job(DefDatabase<JobDef>.GetNamed("Stumble"));
		}
	}

	public class JobGiver_ExtractZombieSerum : ThinkNode_JobGiver
	{
		public override ThinkNode DeepCopy(bool resolve = true)
		{
			return (JobGiver_ExtractZombieSerum)base.DeepCopy(resolve);
		}

		protected override Job TryGiveJob(Pawn pawn)
		{
			return new Job(DefDatabase<JobDef>.GetNamed("ExtractZombieSerum"));
		}
	}
}