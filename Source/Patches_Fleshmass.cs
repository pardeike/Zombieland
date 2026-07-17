using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace ZombieLand
{
	static partial class Patches
	{
		[HarmonyPatch(typeof(CompFleshmass), nameof(CompFleshmass.Notify_Killed))]
		internal static class CompFleshmass_Notify_Killed_Patch
		{
			static bool Prepare() => ModsConfig.AnomalyActive;

			static void Prefix(DamageInfo? dinfo, out HashSet<Letter> __state)
			{
				__state = FleshmassCollision.CaptureResponseLetters(dinfo);
			}

			static void Postfix(CompFleshmass __instance, DamageInfo? dinfo, HashSet<Letter> __state)
			{
				if (FleshmassCollision.IsZombieFactionKill(dinfo))
				{
					var source = __instance?.source;
					if (source != null && source.Spawned)
						source.TryGetComp<CompGrowsFleshmassTendrils>()?.Notify_FleshmassDestroyedByPlayer(__instance.parent);
				}

				FleshmassCollision.SuppressNewResponseLetters(__state);
			}
		}
	}
}
