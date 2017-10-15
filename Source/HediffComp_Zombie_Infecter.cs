using RimWorld;
using System.Text;
using Verse;

namespace ZombieLand
{
	public class HediffCompProperties_Zombie_Infecter : HediffCompProperties
	{
		public float minBedTendQualityToAvoidInfection = 0.17f;
		public QualityCategory minBedQualityToAvoidInfection = QualityCategory.Masterwork;
		public float minTendQualityToAvoidInfection = 0.7f;

		public HediffCompProperties_Zombie_Infecter()
		{
			compClass = typeof(HediffComp_Zombie_Infecter);
		}
	}

	public class HediffComp_Zombie_Infecter : HediffComp
	{
		public int infectionKnownDelay = 0;
		public int infectionStartTime = 0;
		public int infectionEndTime = 0;

		public HediffCompProperties_Zombie_Infecter Props => (HediffCompProperties_Zombie_Infecter)props;

		public override void CompExposeData()
		{
			Scribe_Values.Look(ref infectionKnownDelay, "infectionKnownDelay", 0);
			Scribe_Values.Look(ref infectionStartTime, "infectionStartTime", 0);
			Scribe_Values.Look(ref infectionEndTime, "infectionEndTime", 0);
		}

		public override void CompPostPostAdd(DamageInfo? dinfo)
		{
			if (Pawn == null
				|| Pawn.Spawned == false
				|| Pawn.Map == null
				|| Pawn.Dead
				|| Pawn.IsColonist == false
				|| Pawn.health == null
				|| Pawn.health.hediffSet == null
				|| parent == null
				|| parent.Part == null
				|| parent.Part.def == null)
				return;

			if (parent.Part.def.IsSolid(parent.Part, Pawn.health.hediffSet.hediffs))
				return;
			if (Pawn.health.hediffSet.PartOrAnyAncestorHasDirectlyAddedParts(parent.Part))
				return;

			var h = GenDate.TicksPerHour;

			var ticks = ZombieSettings.Values.hoursInfectionIsUnknown * h;
			infectionKnownDelay = GenTicks.TicksAbs + ticks;

			if (Rand.Chance(ZombieSettings.Values.zombieBiteInfectionChance))
			{
				ticks = ZombieSettings.Values.hoursInfectionIsTreatable * h;
				infectionStartTime = GenTicks.TicksAbs + ticks;

				ticks = ZombieSettings.Values.hoursInfectionPersists * h;
				infectionEndTime = infectionStartTime + ticks;
			}
		}

		public override void CompTended(float quality, int batchPosition = 0)
		{
			if (Pawn.Spawned == false)
				return;

			if (infectionStartTime == 0)
				return;

			if (GenTicks.TicksAbs >= infectionStartTime)
				return;

			var bed = Pawn.CurrentBed();
			if (bed == null)
				return;

			if (ZombieSettings.Values.anyTreatmentStopsInfection)
			{
				infectionKnownDelay = 0;
				infectionStartTime = 0;
				infectionEndTime = 0;
				return;
			}

			var tendQuality = bed.GetStatValue(StatDefOf.MedicalTendQualityOffset, true);
			if (tendQuality < Props.minBedTendQualityToAvoidInfection)
				return;

			QualityCategory bedQuality;
			bed.TryGetQuality(out bedQuality);
			if (bedQuality < Props.minBedQualityToAvoidInfection)
				return;

			if (quality < Props.minTendQualityToAvoidInfection)
				return;

			if (Rand.Chance(quality))
			{
				infectionKnownDelay = 0;
				infectionStartTime = 0;
				infectionEndTime = 0;
			}
		}

		public override string CompDebugString()
		{
			var sb = new StringBuilder();

			if (infectionKnownDelay >= 0)
				sb.Append("Revealed in " + infectionKnownDelay.ToHourString() + "\n");

			if (infectionStartTime == 0)
				sb.Append("No infection risk\n");
			else
				sb.Append("Starts in " + infectionStartTime.ToHourString() + "\n");

			if (infectionEndTime >= 0)
				sb.Append("Death in " + infectionEndTime.ToHourString() + "\n");

			var result = sb.ToString();
			return result.TrimEndNewlines();
		}

	}

}