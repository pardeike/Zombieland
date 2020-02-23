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
			return JobMaker.MakeJob(CustomDefs.Stumble);
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
			return JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("ExtractZombieSerum"));
		}
	}
}