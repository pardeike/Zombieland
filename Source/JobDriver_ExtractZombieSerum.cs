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

		private readonly ThingDef extractDef = ThingDef.Named("ZombieExtract");

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look<float>(ref extractProcess, "extractProcess", 0f, false);
		}

		public override string GetReport()
		{
			return "ExtractingZombieSerum".Translate();
		}

		public override bool TryMakePreToilReservations(bool errorOnFailed)
		{
			if (ZombieSettings.Values.corpsesExtractAmount == 0)
				return false;
			return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
		}

		public override IEnumerable<Toil> MakeNewToils()
		{
			AddEndCondition(delegate
			{
				if (ZombieSettings.Values.corpsesExtractAmount == 0)
					return JobCondition.Incompletable;
				return JobCondition.Ongoing;
			});

			_ = this.FailOnDespawnedOrNull(TargetIndex.A);
			yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);

			var extract = new Toil
			{
				activeSkill = (() => SkillDefOf.Medicine),
				defaultCompleteMode = ToilCompleteMode.Never,

				initAction = delegate ()
				{
					pawn.pather.StopDead();
				},

				tickAction = delegate ()
				{
					var zombieCorpse = (ZombieCorpse)job.GetTarget(TargetIndex.A).Thing;
					extractProcess += pawn.GetStatValue(StatDefOf.MedicalTendSpeed, true) / 2;
					if (extractProcess >= extractWork)
					{
						var extractResult = ThingMaker.MakeThing(extractDef, null);
						extractResult.stackCount = ZombieSettings.Values.corpsesExtractAmount;
						_ = GenPlace.TryPlaceThing(extractResult, pawn.Position, pawn.Map, ThingPlaceMode.Near, null, null);

						if (zombieCorpse != null)
							zombieCorpse.Destroy();

						pawn.jobs.EndCurrentJob(JobCondition.Succeeded, true);
					}
				}
			};
			_ = extract.FailOnDespawnedOrNull(TargetIndex.A);
			_ = extract.FailOnCannotTouch(TargetIndex.A, PathEndMode.Touch);
			extract.AddEndCondition(delegate
			{
				var zombieCorpse = (ZombieCorpse)job.GetTarget(TargetIndex.A).Thing;
				if (zombieCorpse == null || zombieCorpse.Destroyed || zombieCorpse.Spawned == false)
					return JobCondition.Incompletable;
				return JobCondition.Ongoing;
			});
			_ = extract.WithProgressBar(TargetIndex.A, () => extractProcess / extractWork, false, -0.5f);

			yield return extract;
		}
	}
}
