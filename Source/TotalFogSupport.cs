using HarmonyLib;
using System;
using Verse;

namespace ZombieLand
{
	[StaticConstructorOnStartup]
	static class TotalFogSupport
	{
		// Bind once without a Total Fog dependency. Ordinary zombie rendering
		// uses native culling; cross-pawn effects and live readouts query sight here.
		static readonly Func<Thing, bool> isVisible = BindVisibility();

		internal static bool HasVisibilityFilter => isVisible != null;
		internal static bool IsVisible(Thing thing) => isVisible == null || isVisible(thing);

		internal static int VisibleZombieCount(TickManager tickManager)
		{
			if (isVisible == null)
				return tickManager.ZombieCount();
			var zombies = tickManager.allZombiesCached;
			if (zombies == null)
				return 0;
			var count = 0;
			foreach (var zombie in zombies)
				if (zombie.Spawned && zombie.Dead == false && isVisible(zombie))
					count++;
			return count;
		}

		static Func<Thing, bool> BindVisibility()
		{
			var type = AccessTools.TypeByName("TotalFog.Visibility");
			if (type == null)
				return null;
			var method = AccessTools.DeclaredMethod(type, "IsVisible", new[] { typeof(Thing) });
			if (method == null || method.IsStatic == false || method.ReturnType != typeof(bool))
			{
				Log.Warning("Zombieland: Total Fog's live visibility query is unavailable.");
				return null;
			}
			return (Func<Thing, bool>)Delegate.CreateDelegate(typeof(Func<Thing, bool>), method);
		}
	}
}
