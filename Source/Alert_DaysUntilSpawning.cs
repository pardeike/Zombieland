using RimWorld;
using System;
using Verse;

namespace ZombieLand
{
	public class Alert_DaysUntilSpawning : Alert
	{
		double days;

		public Alert_DaysUntilSpawning()
		{
			defaultLabel = "";
			defaultExplanation = "XDaysUntilZombies".Translate();
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