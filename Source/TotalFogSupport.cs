using HarmonyLib;
using System;
using Verse;

namespace ZombieLand
{
	[StaticConstructorOnStartup]
	static class TotalFogSupport
	{
		// Bind once without a Total Fog dependency. Ordinary zombie rendering
		// never calls this; only cross-pawn effects need a second visibility query.
		static readonly Func<Thing, bool> isVisible = BindVisibility();

		internal static bool IsVisible(Thing thing) => isVisible == null || isVisible(thing);

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
