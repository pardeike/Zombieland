using System.Linq;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public enum InfectionState
	{
		None,             // no infection
		BittenNotVisible, // unclear/invisible
		BittenVisible,    // # abstract mark
		BittenHarmless,   // simple wound
		BittenInfectable, // orange stage
		Infecting,        // red stage
		Infected,         // flag for zombie conversion to begin
		All               // # abstract mark
	}

	public class HediffCompProperties_Zombie_TendDuration : HediffCompProperties_TendDuration
	{
		public HediffCompProperties_Zombie_TendDuration()
		{
			compClass = typeof(HediffComp_Zombie_TendDuration);
		}
	}

	public class HediffComp_Zombie_TendDuration : HediffComp_TendDuration
	{
		bool firstTimeVisible = true;

		HediffComp_Zombie_Infecter _zombieInfector;
		public HediffComp_Zombie_Infecter ZombieInfector
		{
			get
			{
				if (_zombieInfector == null)
				{
					var pawn = Pawn;
					if (pawn.RaceProps.Humanlike == false)
						return null;
					if (pawn.RaceProps.IsFlesh == false)
						return null;
					if (AlienTools.IsFleshPawn(pawn) == false)
						return null;
					if (SoSTools.IsHologram(pawn))
						return null;

					if (Customization.CannotBecomeZombie(pawn))
						return null;

					_zombieInfector = parent.comps
						.OfType<HediffComp_Zombie_Infecter>()
						.FirstOrDefault();
				}
				return _zombieInfector;
			}
		}

		public InfectionState GetInfectionState()
		{
			var pawn = Pawn;
			if (ZombieInfector == null
				|| pawn == null
				|| pawn.RaceProps.Humanlike == false
				|| pawn.RaceProps.IsFlesh == false
				|| AlienTools.IsFleshPawn(pawn) == false
				|| SoSTools.IsHologram(pawn)
				)
				return InfectionState.None;

			var now = GenTicks.TicksAbs;
			if (now < ZombieInfector.infectionKnownDelay)
				return InfectionState.BittenNotVisible;
			if (ZombieInfector.infectionStartTime == 0)
				return InfectionState.BittenHarmless;
			if (now < ZombieInfector.infectionStartTime)
				return InfectionState.BittenInfectable;
			if (now < ZombieInfector.infectionEndTime)
				return InfectionState.Infecting;
			return InfectionState.Infected;
		}

		public int TicksBeforeStartOfInfection()
		{
			return ZombieInfector.infectionStartTime - GenTicks.TicksAbs;
		}

		public int TicksBeforeEndOfInfection()
		{
			return ZombieInfector.infectionEndTime - GenTicks.TicksAbs;
		}

		public float InfectionProgress()
		{
			if (GetInfectionState() != InfectionState.Infecting)
				return 0f;
			return GenMath.LerpDoubleClamped(ZombieInfector.infectionStartTime, ZombieInfector.infectionEndTime, 0f, 1f, GenTicks.TicksAbs);
		}

		public override bool CompShouldRemove
		{
			get
			{
				var pawn = Pawn;
				if (pawn.RaceProps.Humanlike == false)
					return base.CompShouldRemove;
				if (pawn.RaceProps.IsFlesh == false)
					return base.CompShouldRemove;
				if (AlienTools.IsFleshPawn(pawn) == false)
					return base.CompShouldRemove;
				if (SoSTools.IsHologram(pawn))
					return base.CompShouldRemove;

				var state = GetInfectionState();
				if (state == InfectionState.BittenNotVisible || state >= InfectionState.Infecting)
					return false;

				if (firstTimeVisible)
				{
					firstTimeVisible = false;
					totalTendQuality = 0f;
				}

				return base.CompShouldRemove;
			}
		}

		public override void CompExposeData()
		{
			base.CompExposeData();
			Scribe_Values.Look(ref firstTimeVisible, "firstTimeVisible");
		}

		public override string CompTipStringExtra
		{
			get
			{
				return GetInfectionState() switch
				{
					InfectionState.BittenNotVisible => "Zombie infection not yet known",
					InfectionState.BittenHarmless => "No zombie infection risk",
					InfectionState.BittenInfectable => "Developing zombie infection",
					InfectionState.Infecting => (Tools.Difficulty() > 1.5 ? "Uncurable" : "Curable") + " zombie infection",
					_ => null,
				};
			}
		}

		public override TextureAndColor CompStateIcon
		{
			get
			{
				var pawn = Pawn;
				if (pawn.RaceProps.Humanlike == false)
					return base.CompStateIcon;
				if (pawn.RaceProps.IsFlesh == false)
					return base.CompStateIcon;
				if (AlienTools.IsFleshPawn(pawn) == false)
					return base.CompStateIcon;
				if (SoSTools.IsHologram(pawn))
					return base.CompStateIcon;

				var state = GetInfectionState();
				if (state == InfectionState.None)
					return base.CompStateIcon;

				var result = base.CompStateIcon;
				var color = result.Color;
				switch (state)
				{
					case InfectionState.BittenInfectable:
						// developing stage: orange
						color = new Color(1f, 0.5f, 0f);
						break;

					case InfectionState.Infecting:
						// final stage: red
						color = Color.red;
						break;
				}
				return new TextureAndColor(result.Texture, color);
			}
		}
	}
}
