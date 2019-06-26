﻿using RimWorld;
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

		// TODO: check for this warning:
		//
		// Contains a call chain that results in a call to a virtual method defined by the class
		// Alert_ZombieInfectionProgress..ctor()
		// Alert_ZombieInfectionProgress.Prepare():Void ZombieLand  C:\Users\Admin\Source\ModRepos\ZombieLand\Source\Alerts.cs	97	Active
		//
		[System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Usage", "CA2214:DoNotCallOverridableMethodsInConstructors")]
		public Alert_ZombieInfectionProgress()
		{
			Prepare();
			defaultLabel = label.SafeTranslate();
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
			return "LetterLabelXDaysUntilZombies".SafeTranslate(timeStr);
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