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
			if (pawn is not Zombie zombie || zombie.isAlbino)
				return null;
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
			if (pawn is not Zombie zombie || zombie.isAlbino == false)
				return null;
			// Keep the current scream or hack when RimWorld accepts a continuation.
			return JobMaker.MakeJob(CustomDefs.Sabotage);
		}

		public override ThinkResult TryIssueJobPackage(Pawn pawn, JobIssueParams jobParams)
		{
			if (pawn is not Zombie zombie || zombie.isAlbino == false)
				return ThinkResult.NoJob;
			return base.TryIssueJobPackage(pawn, jobParams);
		}
	}

	public class JobGiver_Symbiant : ThinkNode_JobGiver
	{
		public override ThinkNode DeepCopy(bool resolve = true)
		{
			return (JobGiver_Symbiant)base.DeepCopy(resolve);
		}

		public override Job TryGiveJob(Pawn pawn)
		{
			if (pawn is not ZombieSymbiant)
				return null;
			pawn.jobs.StopAll();
			return JobMaker.MakeJob(CustomDefs.Symbiant);
		}

		public override ThinkResult TryIssueJobPackage(Pawn pawn, JobIssueParams jobParams)
		{
			if (pawn is not ZombieSymbiant)
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

}
