using Harmony;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	class ZombielandMod : Mod
	{
		public ZombielandMod(ModContentPack content) : base(content)
		{
			GetSettings<ZombieSettingsDefaults>();

			// HarmonyInstance.DEBUG = true;
			var harmony = HarmonyInstance.Create("net.pardeike.zombieland");
			harmony.PatchAll(Assembly.GetExecutingAssembly());
		}

		public override void DoSettingsWindowContents(Rect inRect)
		{
			var settings = ZombieSettings.GetGameSettings();
			if (settings != null)
				settings.DoWindowContents(inRect);
			else
				ZombieSettingsDefaults.DoWindowContents(inRect);
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