using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	public class JobDriver_DoubleTap : JobDriver
	{
		private const float smashBrainWork = 100;
		private float smashBrainProcess = 0;
		private int repeatCounter = 0;

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look<float>(ref smashBrainProcess, "smashBrainProcess", 0f, false);
		}

		public override string GetReport()
		{
			return "DoubleTapping".Translate();
		}

		public override bool TryMakePreToilReservations(bool errorOnFailed)
		{
			if (ZombieSettings.Values.hoursAfterDeathToBecomeZombie == -1)
				return false;
			var corpse = job.GetTarget(TargetIndex.A).Thing as Corpse;
			var innerPawn = corpse.InnerPawn;
			if (innerPawn == null)
				return false;
			if (innerPawn.RaceProps.Humanlike == false || innerPawn.health.hediffSet.GetBrain() == null)
				return false;
			return pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
		}

		public override IEnumerable<Toil> MakeNewToils()
		{
			AddEndCondition(delegate
			{
				if (ZombieSettings.Values.hoursAfterDeathToBecomeZombie == -1)
					return JobCondition.Incompletable;
				return JobCondition.Ongoing;
			});

			_ = this.FailOnDespawnedOrNull(TargetIndex.A);
			yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.OnCell);

			var doubleTap = new Toil
			{
				activeSkill = (() => SkillDefOf.Melee),
				defaultCompleteMode = ToilCompleteMode.Never,

				initAction = pawn.pather.StopDead,

				tickAction = delegate ()
				{
					var corpse = (Corpse)job.GetTarget(TargetIndex.A).Thing;
					var corpseMap = corpse.Map;
					var deadPawn = corpse.InnerPawn;
					var brain = deadPawn?.health.hediffSet.GetBrain();
					var verb = pawn.meleeVerbs.TryGetMeleeVerb(corpse);

					if (corpseMap == null || deadPawn == null || brain == null || verb == null)
					{
						pawn.jobs.EndCurrentJob(JobCondition.Incompletable, true);
						return;
					}

					if (repeatCounter == 0)
					{
						var cell = CellFinder.RandomClosewalkCellNear(corpse.Position, corpse.Map, 1, c => GenGrid.Standable(c, corpse.Map));
						_ = FilthMaker.TryMakeFilth(cell, corpseMap, corpse.InnerPawn.RaceProps.BloodDef, pawn.Name.ToStringShort, (int)Mathf.Max(1, smashBrainProcess / 20));

						repeatCounter = Rand.Range(40, 80);
						smashBrainProcess += pawn.GetStatValue(StatDefOf.MeleeDPS, true) * 4;
						Tools.PlaySmash(corpse);
					}
					else
						repeatCounter--;

					if (smashBrainProcess >= smashBrainWork)
					{
						var part1 = (Hediff_MissingPart)HediffMaker.MakeHediff(HediffDefOf.MissingBodyPart, deadPawn, null);
						part1.IsFresh = true;
						part1.lastInjury = HediffDefOf.Shredded;
						part1.Part = brain;
						deadPawn.health.hediffSet.AddDirect(part1, null, null);

						var head = deadPawn?.health.hediffSet.GetNotMissingParts(BodyPartHeight.Undefined, BodyPartDepth.Undefined, null, null).FirstOrDefault((BodyPartRecord x) => x.def == BodyPartDefOf.Head);
						if (head != null)
						{
							var part2 = (Hediff_MissingPart)HediffMaker.MakeHediff(HediffDefOf.MissingBodyPart, deadPawn, null);
							part2.IsFresh = true;
							part2.lastInjury = HediffDefOf.Shredded;
							part2.Part = head;
							deadPawn.health.hediffSet.AddDirect(part2, null, null);
						}

						pawn.jobs.EndCurrentJob(JobCondition.Succeeded, true);
					}
				}
			};

			_ = doubleTap.FailOnDespawnedOrNull(TargetIndex.A);
			_ = doubleTap.FailOnCannotTouch(TargetIndex.A, PathEndMode.OnCell);

			doubleTap.AddEndCondition(delegate
			{
				var corpse = (Corpse)job.GetTarget(TargetIndex.A).Thing;
				if (corpse == null || corpse.Destroyed || corpse.Spawned == false)
					return JobCondition.Incompletable;
				if (corpse.InnerPawn?.health.hediffSet.GetBrain() == null)
					return JobCondition.Incompletable;
				return JobCondition.Ongoing;
			});
			_ = doubleTap.WithProgressBar(TargetIndex.A, () => smashBrainProcess / smashBrainWork, false, -0.5f);

			yield return doubleTap;
		}
	}
}
