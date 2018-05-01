using Harmony;
using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class Hediff_Injury_ZombieBite : Hediff_Injury
	{
		static Color infectionColor = Color.red.SaturationChanged(0.75f);

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
						return "HoursBeforeBecomingInfected".Translate(new object[] { ticksToStart.ToHourString(false) });

					case InfectionState.Infecting:
						var ticksToEnd = TendDuration.TicksBeforeEndOfInfection();
						return "HoursBeforeBecomingAZombie".Translate(new object[] { ticksToEnd.ToHourString(false) });
				}
				return base.LabelInBrackets;
			}
		}

		/* public override bool CauseDeathNow()
		{
			if (TendDuration != null && TendDuration.GetInfectionState() == InfectionState.Infected)
				return true;

			return base.CauseDeathNow();
		} */

		public void ConvertToZombie()
		{
			if (pawn == null || pawn.Spawned == false || pawn.Dead || pawn.RaceProps.Humanlike == false)
				return;

			var pos = pawn.Position;
			var map = pawn.Map;
			var rot = pawn.Rotation;

			if (map == null)
			{
				pawn.Kill(null);
				return;
			}

			var zombie = ZombieGenerator.GeneratePawn();

			zombie.Name = pawn.Name;
			zombie.gender = pawn.gender;

			var apparelToTransfer = new List<Apparel>();
			pawn.apparel.WornApparelInDrawOrder.Do(apparel =>
			{
				if (pawn.apparel.TryDrop(apparel, out var newApparel))
					apparelToTransfer.Add(newApparel);
			});

			zombie.ageTracker.AgeBiologicalTicks = pawn.ageTracker.AgeBiologicalTicks;
			zombie.ageTracker.AgeChronologicalTicks = pawn.ageTracker.AgeChronologicalTicks;
			zombie.ageTracker.BirthAbsTicks = pawn.ageTracker.BirthAbsTicks;

			zombie.story.childhood = pawn.story.childhood;
			zombie.story.adulthood = pawn.story.adulthood;
			zombie.story.melanin = pawn.story.melanin;
			zombie.story.crownType = pawn.story.crownType;
			zombie.story.hairDef = pawn.story.hairDef;
			zombie.story.bodyType = pawn.story.bodyType;

			var zTweener = Traverse.Create(zombie.Drawer.tweener);
			var pTweener = Traverse.Create(pawn.Drawer.tweener);
			zTweener.Field("tweenedPos").SetValue(pTweener.Field("tweenedPos").GetValue());
			zTweener.Field("lastDrawFrame").SetValue(pTweener.Field("lastDrawFrame").GetValue());
			zTweener.Field("lastTickSpringPos").SetValue(pTweener.Field("lastTickSpringPos").GetValue());

			ZombieGenerator.AssignNewCustomGraphics(zombie);
			ZombieGenerator.FinalizeZombieGeneration(zombie);
			GenPlace.TryPlaceThing(zombie, pos, map, ThingPlaceMode.Direct, null);

			var wasColonist = pawn.IsColonist;
			pawn.Kill(null);
			if (pawn.Corpse != null && pawn.Corpse.Destroyed == false)
				pawn.Corpse.Destroy();

			apparelToTransfer.ForEach(apparel => zombie.apparel.Wear(apparel));
			zombie.Rotation = rot;
			zombie.rubbleCounter = Constants.RUBBLE_AMOUNT;
			zombie.state = ZombieState.Wandering;
			zombie.wasColonist = true;

			var who = wasColonist ? "Colonist" : "Someone";
			var label = (who + "BecameAZombieLabel").Translate();
			var text = "ColonistBecameAZombieDesc".Translate(new object[] { zombie.NameStringShort });
			Find.LetterStack.ReceiveLetter(label, text, LetterDefOf.ThreatBig, zombie);
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
			var state = TendDuration.GetInfectionState();
			if (state == InfectionState.None)
			{
				base.Tick();
				return;
			}

			if (state == InfectionState.Infected)
			{
				ConvertToZombie();
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

		public override void Heal(float amount)
		{
			if (pawn.RaceProps.Humanlike)
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
