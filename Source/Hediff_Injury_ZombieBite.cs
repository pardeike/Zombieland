using System;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class Hediff_Injury_ZombieBite : Hediff_Injury
	{
		public bool mayBecomeZombieWhenDead;

		HediffComp_Zombie_TendDuration tendDurationComp;
		public HediffComp_Zombie_TendDuration TendDuration
		{
			get
			{
				if (tendDurationComp == null)
					tendDurationComp = this.TryGetComp<HediffComp_Zombie_TendDuration>();
				return tendDurationComp;
			}
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Values.Look(ref mayBecomeZombieWhenDead, "mayBecomeZombieWhenDead", false);
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
						return "HoursBeforeBecomingInfected".SafeTranslate(new object[] { ticksToStart.ToHourString(false) });

					case InfectionState.Infecting:
						var ticksToEnd = TendDuration.TicksBeforeEndOfInfection();
						return "HoursBeforeBecomingAZombie".SafeTranslate(new object[] { ticksToEnd.ToHourString(false) });
				}
				return base.LabelInBrackets;
			}
		}

		public override Color LabelColor
		{
			get
			{
				if (TendDuration != null)
					switch (TendDuration.GetInfectionState())
					{
						case InfectionState.BittenInfectable:
							// developing stage: orange
							return new Color(1f, 0.5f, 0f);

						case InfectionState.Infecting:
							// final stage: red
							return Color.red;
					}
				return base.LabelColor;
			}
		}

		public override void Tick()
		{
			var state = TendDuration.GetInfectionState();
			if (state == InfectionState.None)
			{
				base.Tick();
				return;
			}

			if (state == InfectionState.Infected && pawn.Map != null)
			{
				Tools.QueueConvertToZombie(pawn, pawn.Map);
				return;
			}

			if (TendDuration.IsTended && (state >= InfectionState.BittenHarmless && state <= InfectionState.Infecting))
			{
				if (Severity > 0f)
					Severity = Math.Max(0f, Severity - 0.001f);
			}
			else
				base.Tick();
		}

		bool InfectionLocked()
		{
			var duration = TendDuration;
			if (duration == null)
				return false;

			var state = duration.GetInfectionState();
			return state == InfectionState.Infecting || state == InfectionState.Infected;
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

		public override void Heal(float amount)
		{
			if (pawn.RaceProps.Humanlike && pawn.RaceProps.IsFlesh && AlienTools.IsFleshPawn(pawn))
			{
				var tendDuration = TendDuration;
				if (tendDuration != null)
				{
					var state = tendDuration.GetInfectionState();
					if (state != InfectionState.BittenVisible && state != InfectionState.BittenHarmless)
						return;
				}
			}

			base.Heal(amount);
		}

		public override float SummaryHealthPercentImpact
		{
			get
			{
				if (InfectionLocked() == false) return base.SummaryHealthPercentImpact;
				return this.IsTended() ? 0f : base.SummaryHealthPercentImpact;
			}
		}

		public override bool TryMergeWith(Hediff other)
		{
			if (InfectionLocked() == false)
				return base.TryMergeWith(other);
			return false;
		}
	}
}
