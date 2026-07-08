using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace ZombieLand
{
	static partial class Patches
	{
		[HarmonyPatch(typeof(MusicManagerPlay), "ChooseNextSong")]
		static class MusicManagerPlay_ChooseNextSong_Patch
		{
			static bool Prefix(MusicManagerPlay __instance, Queue<SongDef> ___recentSongs, ref SongDef __result)
			{
				if (ZombielandMusic.TryChooseNextSong(__instance, ___recentSongs, out var song) == false)
					return true;

				__result = song;
				return false;
			}
		}

		[HarmonyPatch(typeof(MusicManagerEntry), "StartPlaying")]
		static class MusicManagerEntry_StartPlaying_Patch
		{
			static void Prefix()
				=> ZombielandMusic.ApplyEntrySongReplacement();
		}
	}
}
