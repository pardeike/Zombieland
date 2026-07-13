using HarmonyLib;
using RimWorld;
using System;
using Verse;

namespace ZombieLand
{
	static partial class Patches
	{
		static void ResetMapOwnedStaticState(bool recreateAvoider)
		{
			Tools.ResetMapOwnedState(recreateAvoider);
			TargetCachePatches.ResetMapOwnedState();
		}

		// settings backwards compatibility
		//
		[HarmonyPatch(typeof(ParseHelper))]
		[HarmonyPatch(nameof(ParseHelper.FromString))]
		[HarmonyPatch(new[] { typeof(string), typeof(Type) })]
		static class ParseHelper_FromString_Patch
		{
			[HarmonyPriority(Priority.First)]
			static void Prefix(ref string str, Type itemType)
			{
				itemType = Nullable.GetUnderlyingType(itemType) ?? itemType;
				if (itemType == typeof(AreaRiskMode))
				{
					str = str switch
					{
						"IfInside" => nameof(AreaRiskMode.ColonistInside),
						"IfOutside" => nameof(AreaRiskMode.ColonistOutside),
						_ => str
					};
				}
			}
		}

		[HarmonyPatch(typeof(MainMenuDrawer))]
		[HarmonyPatch(nameof(MainMenuDrawer.Init))]
		static class MainMenuDrawer_Init_Patch
		{
			static void Postfix()
			{
				if (PatchGroups.HasFailures)
				{
					LongEventHandler.ExecuteWhenFinished(() =>
					{
						PatchGroups.TryShowFailureDialogAtStartScreen();
					});
				}
			}
		}

		// use default mod settings for quick test play
		//
		[HarmonyPatch(typeof(Root_Play))]
		[HarmonyPatch(nameof(Root_Play.SetupForQuickTestPlay))]
		static class Root_Play_SetupForQuickTestPlay_Patch
		{
			static void Postfix()
			{
				ZombieSettings.ApplyDefaults();
			}
		}

		// patches to keep track of frame time
		//
		[HarmonyPatch(typeof(Root))]
		[HarmonyPatch(nameof(Root.Update))]
		static class Root_Update_Patch
		{
			static void Prefix()
			{
				ZombielandMod.frameWatch.Restart();
				if (PatchGroups.HasFailures)
					PatchGroups.TryShowFailureDialogAtStartScreen();
			}
		}

		// patches to clean up after us
		//
		[HarmonyPatch(typeof(Root))]
		[HarmonyPatch(nameof(Root.Shutdown))]
		static class Root_Shutdown_Patch
		{
			static void Prefix()
			{
				ResetMapOwnedStaticState(false);
				ZombieTicker.ResetAdaptiveState();
				AlbinoScreamVomit.ClearPendingMultipliers();
				ZombieSymbiant.ResetTransientStaticState();

				// var maps = Find.Maps;
				// if (maps != null)
				// 	foreach (var map in maps)
				// 		map?.GetComponent<TickManager>()?.MapRemoved();
				//
				// MemoryUtility.ClearAllMapsAndWorld();
			}
		}

		[HarmonyPatch(typeof(GameDataSaveLoader))]
		[HarmonyPatch(nameof(GameDataSaveLoader.LoadGame), typeof(string))]
		static class GameDataSaveLoader_LoadGame_Patch
		{
			static void Prefix()
			{
				ResetMapOwnedStaticState(true);
				ZombieTicker.ResetAdaptiveState();
				AlbinoScreamVomit.ClearPendingMultipliers();
				ZombieBootstrap.ResetLogDedupers();
				ZombieSymbiant.ResetTransientStaticState();
			}
		}

		[HarmonyPatch(typeof(Current))]
		[HarmonyPatch(nameof(Current.Notify_LoadedSceneChanged))]
		static class Current_Notify_LoadedSceneChanged_Patch
		{
			static void Prefix()
			{
				if (GenScene.InEntryScene)
				{
					ResetMapOwnedStaticState(true);
					ZombieTicker.ResetAdaptiveState();
					AlbinoScreamVomit.ClearPendingMultipliers();
					ZombieSymbiant.ResetTransientStaticState();
				}
			}
		}
	}
}
