using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using static ZombieLand.Patches;

namespace ZombieLand
{
	public class Alert_ColonistsBittenByZombie : Alert_ZombieInfectionProgress
	{
		public override bool ColonistSelector(Pawn pawn)
		{
			return Tools.HasInfectionState(pawn, InfectionState.BittenNotVisible);
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
			return Tools.HasInfectionState(pawn, InfectionState.BittenInfectable);
		}

		public override void Prepare()
		{
			priority = AlertPriority.Critical;
			label = "ImminentZombiInfection";
		}
	}

	public class Alert_ZombieInfection : Alert_ZombieInfectionProgress
	{
		public static HashSet<Pawn> infectedColonists;

		public override bool ColonistSelector(Pawn pawn)
		{
			return Tools.HasInfectionState(pawn, InfectionState.Infecting, InfectionState.Infected);
		}

		public override IEnumerable<Pawn> AffectedColonists
		{
			get
			{
				var colonists = base.AffectedColonists;
				Need_CurLevel_Patch.infectedColonists = new HashSet<Pawn>(colonists);
				return colonists;
			}
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

		public virtual IEnumerable<Pawn> AffectedColonists
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
#pragma warning disable RECS0021
			Prepare();
#pragma warning restore RECS0021
			defaultLabel = label.Translate();
			defaultPriority = priority;
		}

		public override string GetExplanation()
		{
			var stringBuilder = new StringBuilder();
			foreach (var pawn in AffectedColonists)
				stringBuilder.AppendLine("    " + NameDecorator(pawn));
			return string.Format((label + "Desc").Translate(), stringBuilder.ToString());
		}

		public override AlertReport GetReport()
		{
			var pawn = AffectedColonists.FirstOrDefault();
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
			defaultExplanation = "";
			defaultPriority = AlertPriority.Medium;
		}

		public override string GetLabel()
		{
			var timeStr = Tools.TranslateHoursToText((float)days * GenDate.HoursPerDay);
			return "LetterLabelXDaysUntilZombies".Translate(timeStr);
		}

		public override string GetExplanation()
		{
			return "XDaysUntilZombies".Translate();
		}

		public override AlertReport GetReport()
		{
			days = Days();
			return (days > 0);
		}

		public override void AlertActiveUpdate()
		{

		}

		double Days()
		{
			return Math.Round(ZombieSettings.Values.daysBeforeZombiesCome - GenDate.DaysPassedFloat, 1);
		}
	}
}