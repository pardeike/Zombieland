using System;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class Hediff_Injury_ZombieBite : Hediff_Injury
	{
		static Color infectionColor = Color.red.SaturationChanged(0.75f);

		private HediffComp_Zombie_TendDuration tendDurationComp;
		public HediffComp_Zombie_TendDuration TendDuration
		{
			get
			{
				if (tendDurationComp == null)
					tendDurationComp = this.TryGetComp<HediffComp_Zombie_TendDuration>();
				return tendDurationComp;
			}
		}

		public override string LabelInBrackets
		{
			get
			{
				var state = TendDuration.GetInfectionState();
				switch (state)
				{
					case InfectionState.BittenHarmless:
						return "NoInfectionRisk".Translate();

					case InfectionState.BittenInfectable:
						var ticksToStart = TendDuration.TicksBeforeStartOfInfection();
						return "HoursBeforeBecomingInfected".Translate(new object[] { Tools.ToHourString(ticksToStart, false) });

					case InfectionState.Infecting:
						var ticksToEnd = TendDuration.TicksBeforeEndOfInfection();
						return "HoursBeforeBecomingAZombie".Translate(new object[] { Tools.ToHourString(ticksToEnd, false) });
				}
				return base.LabelInBrackets;
			}
		}

		public override bool CauseDeathNow()
		{
			if (TendDuration != null && TendDuration.GetInfectionState() == InfectionState.Infected)
				return true;

			return base.CauseDeathNow();
		}

		public override Color LabelColor
		{
			get
			{
				if (TendDuration != null)
					switch (TendDuration.GetInfectionState())
					{
						case InfectionState.BittenInfectable:
							return new Color(1f, 0.5f, 0f); // orange

						case InfectionState.Infecting:
							return Color.red;
					}
				return base.LabelColor;
			}
		}

		public override void Tick()
		{
			if (TendDuration.IsTended && TendDuration.InfectionStateBetween(InfectionState.BittenHarmless, InfectionState.Infecting))
			{
				if (Severity > 0f)
					Severity = Math.Max(0f, Severity - 0.001f);
			}
			else
				base.Tick();
		}

		private bool InfectionLocked()
		{
			return TendDuration != null && TendDuration.GetInfectionState() == InfectionState.Infecting;
		}

		public override float PainFactor
		{
			get
			{
				if (InfectionLocked() == false) return base.PainFactor;
				return this.IsTended() ? 0f : base.PainFactor;
			}
		}

		public override float PainOffset
		{
			get
			{
				if (InfectionLocked() == false) return base.PainOffset;
				return this.IsTended() ? 0f : base.PainOffset;
			}
		}

		public override float SummaryHealthPercentImpact
		{
			get
			{
				if (InfectionLocked() == false) return base.PainFactor;
				return this.IsTended() ? 0f : base.SummaryHealthPercentImpact;
			}
		}

		public override void Heal(float amount)
		{
			if (InfectionLocked() == false)
				base.Heal(amount);
		}

		public override bool TryMergeWith(Hediff other)
		{
			if (InfectionLocked() == false)
				return base.TryMergeWith(other);
			return false;
		}
	}
}
