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
			if ((pawn is Zombie) == false) return null;
			pawn.jobs.StopAll();
			return new Job(CustomDefs.Stumble);
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