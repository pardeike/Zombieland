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
			if (!(pawn is Zombie zombie) || zombie.isAlbino) return null;
			pawn.jobs.StopAll();
			return JobMaker.MakeJob(CustomDefs.Stumble);
		}

		public override ThinkResult TryIssueJobPackage(Pawn pawn, JobIssueParams jobParams)
		{
			if (!(pawn is Zombie zombie) || zombie.isAlbino)
				return ThinkResult.NoJob;
			return base.TryIssueJobPackage(pawn, jobParams);
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
			if (!(pawn is Zombie zombie) || zombie.isAlbino == false) return null;
			zombie.jobs.StopAll();
			return JobMaker.MakeJob(CustomDefs.Sabotage);
		}

		public override ThinkResult TryIssueJobPackage(Pawn pawn, JobIssueParams jobParams)
		{
			if (!(pawn is Zombie zombie) || zombie.isAlbino == false)
				return ThinkResult.NoJob;
			return base.TryIssueJobPackage(pawn, jobParams);
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
