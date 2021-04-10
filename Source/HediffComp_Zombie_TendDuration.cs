using System.Linq;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public enum InfectionState
	{
		None,
		BittenNotVisible,
		BittenVisible, // abstract mark
		BittenHarmless,
		BittenInfectable,
		Infecting,
		Infected,
		All // abstract mark
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
					if (pawn.RaceProps.Humanlike == false) return null;
					if (pawn.RaceProps.IsFlesh == false) return null;
					if (AlienTools.IsFleshPawn(pawn) == false) return null;

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
				) return InfectionState.None;

			var now = GenTicks.TicksAbs;
			if (now < ZombieInfector.infectionKnownDelay) return InfectionState.BittenNotVisible;
			if (ZombieInfector.infectionStartTime == 0) return InfectionState.BittenHarmless;
			if (now < ZombieInfector.infectionStartTime) return InfectionState.BittenInfectable;
			if (now < ZombieInfector.infectionEndTime) return InfectionState.Infecting;
			return InfectionState.Infected;
		}

		public bool InfectionStateBetween(InfectionState state1, InfectionState state2)
		{
			var current = GetInfectionState();
			return current >= state1 && current <= state2;
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
			if (GetInfectionState() != InfectionState.Infecting) return 0f;
			return GenMath.LerpDoubleClamped(ZombieInfector.infectionStartTime, ZombieInfector.infectionEndTime, 0f, 1f, GenTicks.TicksAbs);
		}

		public override bool CompShouldRemove
		{
			get
			{
				var pawn = Pawn;
				if (pawn.RaceProps.Humanlike == false) return base.CompShouldRemove;
				if (pawn.RaceProps.IsFlesh == false) return base.CompShouldRemove;
				if (AlienTools.IsFleshPawn(pawn) == false) return base.CompShouldRemove;

				var state = GetInfectionState();
				if (state == InfectionState.BittenNotVisible || state >= InfectionState.Infecting)
					return false;

				if (firstTimeVisible)
				{
					firstTimeVisible = false;
					GetterSetters.totalTendQualityByRef(this) = 0f;
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
				switch (GetInfectionState())
				{
					case InfectionState.BittenNotVisible:
						return "Can infect: unknown";

					case InfectionState.BittenHarmless:
						return "Can infect: no";

					case InfectionState.BittenInfectable:
						return "Can infect: yes";

					case InfectionState.Infecting:
						return "Infecting";
				}
				return null;
			}
		}

		public override TextureAndColor CompStateIcon
		{
			get
			{
				var pawn = Pawn;
				if (pawn.RaceProps.Humanlike == false) return base.CompStateIcon;
				if (pawn.RaceProps.IsFlesh == false) return base.CompStateIcon;
				if (AlienTools.IsFleshPawn(pawn) == false) return base.CompStateIcon;

				var state = GetInfectionState();
				if (state == InfectionState.None) return base.CompStateIcon;

				var result = base.CompStateIcon;
				var color = result.Color;
				switch (state)
				{
					case InfectionState.BittenInfectable:
						color = new Color(1f, 0.5f, 0f); // orange
						break;

					case InfectionState.Infecting:
						color = Color.red;
						break;
				}
				return new TextureAndColor(result.Texture, color);
			}
		}
	}
}
