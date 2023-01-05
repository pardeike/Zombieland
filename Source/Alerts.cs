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
		public override bool ColonistSelector(Pawn pawn) => pawn.InfectionState() == InfectionState.BittenNotVisible;

		public override void Prepare()
		{
			priority = AlertPriority.High;
			label = "ColonistsBittenByZombie";
		}
	}

	public class Alert_ImminentZombiInfection : Alert_ZombieInfectionProgress
	{
		public override bool ColonistSelector(Pawn pawn) => pawn.InfectionState() == InfectionState.BittenInfectable;

		public override void Prepare()
		{
			priority = AlertPriority.Critical;
			label = "ImminentZombiInfection";
		}
	}

	public class Alert_ZombieInfection : Alert_ZombieInfectionProgress
	{
		public static HashSet<Pawn> infectedColonists;
		private List<Hediff_Injury_ZombieBite> tmpHediffInjuryZombieBites = new();

		public override bool ColonistSelector(Pawn pawn) => pawn.InfectionState() == InfectionState.Infecting;

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
			tmpHediffInjuryZombieBites.Clear();
			pawn.health.hediffSet.GetHediffs(ref tmpHediffInjuryZombieBites);
			var tendDuration = tmpHediffInjuryZombieBites
						.Select(comps => comps.TryGetComp<HediffComp_Zombie_TendDuration>())
						.FirstOrDefault();

			var percent = string.Format("{0:P0}", tendDuration.InfectionProgress());
			return pawn.Name.ToStringShort + ", " + percent;
		}
	}

	// base classes

	public class Alert_ZombieInfectionProgress : Alert
	{
		public AlertPriority priority = AlertPriority.Medium;
		public string label = "";

		public virtual void Prepare() { }
		public virtual string NameDecorator(Pawn pawn) { return pawn.Name.ToStringShort; }
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
			Prepare();
			defaultLabel = label.SafeTranslate();
			defaultPriority = priority;
		}

		public override TaggedString GetExplanation()
		{
			var stringBuilder = new StringBuilder();
			foreach (var pawn in AffectedColonists)
				_ = stringBuilder.AppendLine("    " + NameDecorator(pawn));
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
			return "LetterLabelXDaysUntilZombies".SafeTranslate(timeStr);
		}

		public override TaggedString GetExplanation()
		{
			return "XDaysUntilZombies".Translate();
		}

		public override AlertReport GetReport()
		{
			if (ZombieSettings.Values.showZombieStats == false)
				return false;
			days = Days();
			return (days > 0);
		}

		double Days()
		{
			return Math.Round(ZombieSettings.Values.daysBeforeZombiesCome - GenDate.DaysPassedFloat, 1);
		}
	}
}
