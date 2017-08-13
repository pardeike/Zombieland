using Harmony;
using System.Reflection;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class ZombielandMod : Mod
	{
		public static string Identifier = "";

		public ZombielandMod(ModContentPack content) : base(content)
		{
			Identifier = content.Identifier;
			GetSettings<ZombieSettingsDefaults>();

			// HarmonyInstance.DEBUG = true;
			var harmony = HarmonyInstance.Create("net.pardeike.zombieland");
			harmony.PatchAll(Assembly.GetExecutingAssembly());

			// prepare Twinkie
			LongEventHandler.QueueLongEvent(() => { Tools.EnableTwinkie(false); }, "PrepareTwinkie", true, null);

			// extra patch for Combat Extended
			Patches.Projectile_Launch_Patch.PatchCombatExtended(harmony);
		}

		public override void DoSettingsWindowContents(Rect inRect)
		{
			var settings = ZombieSettings.GetGameSettings();
			if (settings != null)
				settings.DoWindowContents(inRect);
			else
				ZombieSettingsDefaults.DoWindowContents(inRect);
		}

		public override void WriteSettings()
		{
			var settings = ZombieSettings.GetGameSettings();
			if (settings != null)
				settings.WriteSettings();
			else
				ZombieSettingsDefaults.WriteSettings();
		}

		public override string SettingsCategory()
		{
			var world = Find.World;
			if (world != null && world.components != null)
				return "ZombielandGameSettings".Translate();
			return "ZombielandDefaultSettings".Translate();
		}
	}
}