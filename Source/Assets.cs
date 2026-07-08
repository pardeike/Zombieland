using HarmonyLib;
using System.IO;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	[HarmonyPatch]
	[StaticConstructorOnStartup]
	public static class Assets
	{
		public static bool initialized = false;

		private static GameObject dust;
		private static Shader metaballShader;
		private static Shader zombieSymbiantShader;
		private static Shader mainMenuBackgroundEffectShader;

		[HarmonyPatch(typeof(UIRoot_Entry), nameof(UIRoot_Entry.Init))]
		[HarmonyPostfix]
		public static void LoadAssetBundle()
		{
			if (initialized)
				return;

			var arch = "Win64";
			var platform = Application.platform;
			if (platform == RuntimePlatform.LinuxEditor || platform == RuntimePlatform.LinuxPlayer)
				arch = "Linux";
			if (platform == RuntimePlatform.OSXEditor || platform == RuntimePlatform.OSXPlayer)
				arch = "MacOS";

			var path = Tools.GetModContentPath("Resources", arch, "zombieland");
			var assets = AssetBundle.LoadFromFile(path);

			dust = assets.LoadAsset<GameObject>("Dust");
			metaballShader = assets.LoadAsset<Shader>("Metaballs");
			zombieSymbiantShader = assets.LoadAsset<Shader>("ZombieSymbiant");
			mainMenuBackgroundEffectShader = assets.LoadAsset<Shader>("MainMenuBackgroundEffect");

			initialized = true;
		}

		public static GameObject NewDust() => Object.Instantiate(dust);
		public static Shader MetaballShader => metaballShader;
		public static Shader ZombieSymbiantShader => zombieSymbiantShader;
		public static Shader MainMenuBackgroundEffectShader => mainMenuBackgroundEffectShader;
	}
}
