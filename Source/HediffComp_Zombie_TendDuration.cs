using Harmony;
using System;
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
					if (Pawn.RaceProps.Humanlike == false) return null;
					if (Pawn.RaceProps.IsFlesh == false) return null;

					_zombieInfector = parent.comps
						.OfType<HediffComp_Zombie_Infecter>()
						.FirstOrDefault();
				}
				return _zombieInfector;
			}
		}

		public InfectionState GetInfectionState()
		{
			if (ZombieInfector == null
				|| Pawn == null
				|| Pawn.Map == null
				|| Pawn.RaceProps.Humanlike == false
				|| Pawn.Spawned == false
				|| Pawn.Destroyed
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
			var progress = GenMath.LerpDouble(ZombieInfector.infectionStartTime, ZombieInfector.infectionEndTime, 0f, 1f, GenTicks.TicksAbs);
			return Mathf.Clamp01(progress);
		}

		public override bool CompShouldRemove
		{
			get
			{
				if (Pawn.RaceProps.Humanlike == false) return base.CompShouldRemove;
				if (Pawn.RaceProps.IsFlesh == false) return base.CompShouldRemove;

				var state = GetInfectionState();
				if (state == InfectionState.BittenNotVisible || state >= InfectionState.Infecting)
					return false;

				if (firstTimeVisible)
				{
					firstTimeVisible = false;
					GetterSetters.setTotalTendQuality(this, 0f);
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
				if (Pawn.RaceProps.Humanlike == false) return base.CompStateIcon;
				if (Pawn.RaceProps.IsFlesh == false) return base.CompStateIcon;

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