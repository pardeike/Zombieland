using System.IO;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	[StaticConstructorOnStartup]
	public static class Assets
	{
		public static readonly AssetBundle assets = LoadAssetBundle();

		public static AssetBundle LoadAssetBundle()
		{
			var arch = "Win64";
			var platform = Application.platform;
			if (platform == RuntimePlatform.LinuxEditor || platform == RuntimePlatform.LinuxPlayer)
				arch = "Linux";
			if (platform == RuntimePlatform.OSXEditor || platform == RuntimePlatform.OSXPlayer)
				arch = "MacOS";

			var me = LoadedModManager.GetMod<ZombielandMod>();
			var path = Path.Combine(me.Content.RootDir, "Resources", arch, "thumper");
			return AssetBundle.LoadFromFile(path);
		}

		public static GameObject NewDust()
		{
			var dust = assets.LoadAsset<GameObject>("Dust");
			return Object.Instantiate(dust);
		}
	}
}