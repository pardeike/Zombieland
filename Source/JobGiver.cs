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
			var zombie = pawn as Zombie;
			if (zombie == null || zombie.isAlbino) return null;
			pawn.jobs.StopAll();
			return JobMaker.MakeJob(CustomDefs.Stumble);
		}
	}

	public class JobGiver_Sabotage : ThinkNode_JobGiver
	{
		public override ThinkNode DeepCopy(bool resolve = true)
		{
			return (JobGiver_Sabotage)base.DeepCopy(resolve);
		}

		protected override Job TryGiveJob(Pawn pawn)
		{
			var zombie = pawn as Zombie;
			if (zombie == null || zombie.isAlbino == false) return null;
			zombie.jobs.StopAll();
			return JobMaker.MakeJob(CustomDefs.Sabotage);
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

		public override ThinkResult TryIssueJobPackage(Pawn pawn, JobIssueParams jobParams)
		{
			if (ZombieSettings.Values.corpsesExtractAmount == 0)
				return ThinkResult.NoJob;
			return base.TryIssueJobPackage(pawn, jobParams);
		}
	}
}