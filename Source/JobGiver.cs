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

		public override Job TryGiveJob(Pawn pawn)
		{
			if (pawn is not Zombie zombie || zombie.isAlbino) return null;
			pawn.jobs.StopAll();
			return JobMaker.MakeJob(CustomDefs.Stumble);
		}

		public override ThinkResult TryIssueJobPackage(Pawn pawn, JobIssueParams jobParams)
		{
			if (pawn is not Zombie zombie || zombie.isAlbino)
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

		public override Job TryGiveJob(Pawn pawn)
		{
			if (pawn is not Zombie zombie || zombie.isAlbino == false) return null;
			zombie.jobs.StopAll();
			return JobMaker.MakeJob(CustomDefs.Sabotage);
		}

		public override ThinkResult TryIssueJobPackage(Pawn pawn, JobIssueParams jobParams)
		{
			if (pawn is not Zombie zombie || zombie.isAlbino == false)
				return ThinkResult.NoJob;
			return base.TryIssueJobPackage(pawn, jobParams);
		}
	}

	public class JobGiver_Spitter : ThinkNode_JobGiver
	{
		public override ThinkNode DeepCopy(bool resolve = true)
		{
			return (JobGiver_Spitter)base.DeepCopy(resolve);
		}

		public override Job TryGiveJob(Pawn pawn)
		{
			if (pawn is not ZombieSpitter)
				return null;
			pawn.jobs.StopAll();
			return JobMaker.MakeJob(CustomDefs.Spitter);
		}

		public override ThinkResult TryIssueJobPackage(Pawn pawn, JobIssueParams jobParams)
		{
			if (pawn is not ZombieSpitter)
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

		public override Job TryGiveJob(Pawn pawn)
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

	public class JobGiver_DoubleTap : ThinkNode_JobGiver
	{
		public override ThinkNode DeepCopy(bool resolve = true)
		{
			return (JobGiver_DoubleTap)base.DeepCopy(resolve);
		}

		public override Job TryGiveJob(Pawn pawn)
		{
			return JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("DoubleTap"));
		}

		public override ThinkResult TryIssueJobPackage(Pawn pawn, JobIssueParams jobParams)
		{
			if (ZombieSettings.Values.hoursAfterDeathToBecomeZombie == -1)
				return ThinkResult.NoJob;
			return base.TryIssueJobPackage(pawn, jobParams);
		}
	}
}
