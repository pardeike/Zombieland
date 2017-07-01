using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;

namespace ZombieLand
{
	public class Alert_ColonistsBittenByZombie : Alert_ZombieInfectionProgress
	{
		public override bool ColonistSelector(Pawn pawn)
		{
			var result = pawn.health.hediffSet
						.GetHediffs<Hediff_Injury_ZombieBite>()
						.Cast<HediffWithComps>()
						.FirstOrDefault(comps =>
						{
							var tendDuration = comps.TryGetComp<HediffComp_Zombie_TendDuration>();
							if (tendDuration == null || comps.IsTended()) return false;
							return tendDuration.GetInfectionState() == InfectionState.BittenNotVisible;
						});
			return (result != null);
		}

		public override void Prepare()
		{
			priority = AlertPriority.High;
			label = "ColonistsBittenByZombie";
		}
	}

	public class Alert_ImminentZombiInfection : Alert_ZombieInfectionProgress
	{
		public override bool ColonistSelector(Pawn pawn)
		{
			var result = pawn.health.hediffSet
						.GetHediffs<Hediff_Injury_ZombieBite>()
						.Cast<HediffWithComps>()
						.FirstOrDefault(comps =>
						{
							var tendDuration = comps.TryGetComp<HediffComp_Zombie_TendDuration>();
							if (tendDuration == null) return false;
							return tendDuration.GetInfectionState() == InfectionState.BittenInfectable;

						});
			return (result != null);
		}

		public override void Prepare()
		{
			priority = AlertPriority.Critical;
			label = "ImminentZombiInfection";
		}
	}

	public class Alert_ZombieInfection : Alert_ZombieInfectionProgress
	{
		public override bool ColonistSelector(Pawn pawn)
		{
			var result = pawn.health.hediffSet
						.GetHediffs<Hediff_Injury_ZombieBite>()
						.Cast<HediffWithComps>()
						.FirstOrDefault(comps =>
						{
							var tendDuration = comps.TryGetComp<HediffComp_Zombie_TendDuration>();
							if (tendDuration == null) return false;
							return tendDuration.InfectionStateBetween(InfectionState.Infecting, InfectionState.Infected);

						});
			return (result != null);
		}

		public override void Prepare()
		{
			priority = AlertPriority.High;
			label = "ZombieInfection";
		}

		public override string NameDecorator(Pawn pawn)
		{
			var tendDuration = pawn.health.hediffSet
						.GetHediffs<Hediff_Injury_ZombieBite>()
						.Cast<HediffWithComps>()
						.Select(comps => comps.TryGetComp<HediffComp_Zombie_TendDuration>())
						.FirstOrDefault();

			var percent = string.Format("{0:P0}", tendDuration.InfectionProgress());
			return pawn.NameStringShort + ", " + percent;
		}
	}

	// base classes

	public class Alert_ZombieInfectionProgress : Alert
	{
		public AlertPriority priority = AlertPriority.Medium;
		public string label = "";

		public virtual void Prepare() { }
		public virtual string NameDecorator(Pawn pawn) { return pawn.NameStringShort; }
		public virtual bool ColonistSelector(Pawn pawn) { return false; }

		private IEnumerable<Pawn> AffectedColonists
		{
			get
			{
				foreach (var pawn in PawnsFinder.AllMaps_FreeColonistsSpawned)
					if (ColonistSelector(pawn))
						yield return pawn;
			}
		}

		public Alert_ZombieInfectionProgress()
		{
			Prepare();
			defaultLabel = label.Translate();
			defaultPriority = priority;
		}

		public override string GetExplanation()
		{
			StringBuilder stringBuilder = new StringBuilder();
			foreach (Pawn pawn in AffectedColonists)
				stringBuilder.AppendLine("    " + NameDecorator(pawn));
			return string.Format((label + "Desc").Translate(), stringBuilder.ToString());
		}

		public override AlertReport GetReport()
		{
			Pawn pawn = AffectedColonists.FirstOrDefault<Pawn>();
			if (pawn == null)
				return false;
			return AlertReport.CulpritIs(pawn);
		}
	}

	public class Alert_DaysUntilSpawning : Alert
	{
		double days;

		public Alert_DaysUntilSpawning()
		{
			defaultLabel = "";
			var whereKey = ZombieSettings.Values.spawnHowType == SpawnHowType.AllOverTheMap ? "XDays_Everywhere" : "XDays_Edges";
			defaultExplanation = "XDaysUntilZombies".Translate(whereKey.Translate());
			defaultPriority = AlertPriority.Medium;
		}

		public override string GetLabel()
		{
			if (days < 1f)
				return "LetterLabelXDaysUntilZombies".Translate(Math.Round(24f * days), "ZombieHours".Translate());

			return "LetterLabelXDaysUntilZombies".Translate(days, "ZombieDays".Translate());
		}

		public override AlertReport GetReport()
		{
			days = Days();
			return (days > 0);
		}

		public override void AlertActiveUpdate()
		{

		}

		private double Days()
		{
			return Math.Round(ZombieSettings.Values.daysBeforeZombiesCome - GenDate.DaysPassedFloat, 1);
		}
	}
}