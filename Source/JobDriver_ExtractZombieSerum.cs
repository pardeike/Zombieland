using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	public class JobDriver_ExtractZombieSerum : JobDriver
	{
		private const float extractWork = 100;
		private float extractProcess = 0;
		private float nextZombieCheck = 0;

		private readonly ThingDef extractDef = ThingDef.Named("ZombieExtract");

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look<float>(ref extractProcess, "extractProcess", 0f, false);
			Scribe_Values.Look<float>(ref nextZombieCheck, "nextZombieCheck", 0f, false);
		}

		public override string GetReport()
		{
			return "ExtractingZombieSerum";
		}

		public override bool TryMakePreToilReservations(bool errorOnFailed)
		{
			var corpse = job.GetTarget(TargetIndex.A).Thing;
			return pawn.Reserve(corpse, job, 1, -1, null, errorOnFailed);
		}

		public override void Notify_PatherArrived()
		{
			base.Notify_PatherArrived();
		}

		protected override IEnumerable<Toil> MakeNewToils()
		{
			_ = this.FailOnDespawnedOrNull(TargetIndex.A);
			yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

			var wait = new Toil();
			wait.initAction = delegate ()
			{
				var actor = wait.actor;
				actor.pather.StopDead();
			};
			wait.tickAction = delegate ()
			{
				var actor = wait.actor;
				extractProcess += actor.GetStatValue(StatDefOf.MedicalTendSpeed, true) / 2;
				if (extractProcess >= extractWork)
				{
					var extract = ThingMaker.MakeThing(extractDef, null);
					extract.stackCount = 1;
					_ = GenPlace.TryPlaceThing(extract, actor.Position, actor.Map, ThingPlaceMode.Near, null, null);

					var zombieCorpse = (ZombieCorpse)job.GetTarget(TargetIndex.A).Thing;
					if (zombieCorpse != null)
						zombieCorpse.Destroy();

					actor.jobs.EndCurrentJob(JobCondition.Succeeded, true);
				}
				if (job.playerForced == false && extractProcess >= nextZombieCheck)
				{
					nextZombieCheck += 0.02f;

					var map = actor.Map;
					var center = actor.Position;
					foreach (var vec in GenRadial.RadialPatternInRadius(4f))
						if (map.thingGrid.ThingAt<Zombie>(center + vec) != null)
						{
							actor.jobs.EndCurrentJob(JobCondition.InterruptOptional, true);
							break;
						}
				}
			};
			_ = wait.FailOnDespawnedOrNull(TargetIndex.A);
			_ = wait.FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch);
			wait.AddEndCondition(delegate
			{
				var zombieCorpse = (ZombieCorpse)job.GetTarget(TargetIndex.A).Thing;
				if (zombieCorpse == null || zombieCorpse.Destroyed || zombieCorpse.Spawned == false)
					return JobCondition.Incompletable;
				return JobCondition.Ongoing;
			});
			wait.defaultCompleteMode = ToilCompleteMode.Never;
			_ = wait.WithProgressBar(TargetIndex.A, () => extractProcess / extractWork, false, -0.5f);
			wait.activeSkill = (() => SkillDefOf.Medicine);
			yield return wait;
		}
	}
}