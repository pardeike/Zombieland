using HarmonyLib;
using RimWorld;
using System;
using Verse;

namespace ZombieLand
{
	static partial class Patches
	{
		[HarmonyPatch(typeof(CompFleshmass), nameof(CompFleshmass.Notify_Killed))]
		internal static class CompFleshmass_Notify_Killed_Patch
		{
			static bool Prepare() => ModsConfig.AnomalyActive;

			static void Postfix(CompFleshmass __instance, DamageInfo? dinfo)
			{
				if (FleshmassCollision.IsZombieFactionKill(dinfo))
					FleshmassCollision.NotifyZombieKill(__instance);
			}
		}

		[HarmonyPatch(typeof(LetterStack), nameof(LetterStack.ReceiveLetter), new Type[] { typeof(Letter), typeof(string), typeof(int), typeof(bool) })]
		internal static class FleshmassResponseLetter_Patch
		{
			static bool Prepare() => ModsConfig.AnomalyActive;

			// Skip the letter before native sound, pause/slowdown and archive effects,
			// and before Total Fog can retain it for later replay.
			[HarmonyPriority(Priority.First)]
			[HarmonyBefore("brrainz.totalfog")]
			static bool Prefix(Letter let)
			{
				return FleshmassCollision.SuppressesResponseLetter(let) == false;
			}
		}
	}
}
