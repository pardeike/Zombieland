using Multiplayer.API;
using Verse;

namespace ZombieLand
{
	// Multiplayer runs every peer in lockstep. While a session is active, Zombieland
	// replaces frame-timed, threaded or camera-dependent work with tick-driven
	// deterministic code and routes UI changes through synchronized methods.
	// Without Multiplayer, IsActive is false and the normal code paths run unchanged.
	public static class MultiplayerSupport
	{
		public static bool IsActive => MP.enabled && MP.IsInMultiplayer;

		// True for interface code (drawing, tooltips, input) during a session. Such code must not change simulation state.
		public static bool InInterface => IsActive && MP.InInterface;

		// System.Random facade over Verse.Rand. Multiplayer keeps Verse.Rand in lockstep per map tick.
		public static readonly System.Random VerseRandom = new VerseRandomFacade();

		sealed class VerseRandomFacade : System.Random
		{
			public override int Next() => Rand.Range(0, int.MaxValue);
			public override int Next(int maxValue) => maxValue <= 0 ? 0 : Rand.Range(0, maxValue);
			public override int Next(int minValue, int maxValue) => minValue >= maxValue ? minValue : Rand.Range(minValue, maxValue);
			public override double NextDouble() => Rand.Value;
			protected override double Sample() => Rand.Value;
		}

		public static void Install()
		{
			if (MP.enabled == false)
				return;

			MP.RegisterAll(typeof(MultiplayerSupport).Assembly);
			Log.Message("Zombieland: multiplayer compatibility enabled");
		}

	}
}
