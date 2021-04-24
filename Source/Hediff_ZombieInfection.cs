using RimWorld;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class Hediff_ZombieInfection : Hediff_Injury
	{
		public int ticksWhenBecomingZombie = -1;

		public void InitializeExpiringDate()
		{
			var hours = ZombieSettings.Values.hoursAfterDeathToBecomeZombie;
			ticksWhenBecomingZombie = hours == -1 ? -1 : GenTicks.TicksGame + GenDate.TicksPerHour * hours;
		}

		public override string LabelBase => "ZombieInfection".Translate();
		public override string LabelInBrackets => null;
		public override Color LabelColor => Color.gray;
		public override string SeverityLabel => null;
		public override float SummaryHealthPercentImpact => 0;
		public override float PainOffset => 0;
		public override float BleedRate => 0;

		public override void PostAdd(DamageInfo? dinfo)
		{
			// do nothing
		}

		public override void Heal(float amount)
		{
			// do nothing
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref ticksWhenBecomingZombie, "ticksWhenBecomingZombie", ZombieSettings.Values.hoursAfterDeathToBecomeZombie);
		}
	}
}
