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

		//public static Texture2D[] spitterImages;
		private static GameObject dust;
		//private static GameObject spitter;

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

			var me = LoadedModManager.GetMod<ZombielandMod>();
			var path = Path.Combine(me.Content.RootDir, "Resources", arch, "zombieland");
			var assets = AssetBundle.LoadFromFile(path);

			//spitterImages = new Texture2D[5];
			//var i = 0;
			//foreach (var mouth in new[] { "MC", "MO" })
			//	foreach (var brow in new[] { "BL", "BH" })
			//		spitterImages[i++] = assets.LoadAsset<Texture2D>($"Spitter-{mouth}-{brow}");
			//spitterImages[i] = assets.LoadAsset<Texture2D>($"Spitter-Open");

			dust = assets.LoadAsset<GameObject>("Dust");
			//spitter = assets.LoadAsset<GameObject>("Spitter");

			initialized = true;
		}

		public static GameObject NewDust() => Object.Instantiate(dust);

		//public static GameObject NewSpitter(IntVec3 center)
		//{
		//	center.y = 0;
		//	var pos = center.ToVector3() + new Vector3(0.5f, -0.1f, 0.5f);
		//	return Object.Instantiate(spitter, pos, Quaternion.identity);
		//}
	}
}