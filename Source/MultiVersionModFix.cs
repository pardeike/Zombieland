using Harmony;
using RimWorld;
using Steamworks;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;
using Verse;
using Verse.Steam;

[HarmonyPatch(typeof(ModMetaData))]
[HarmonyPatch("VersionCompatible", MethodType.Getter)]
public static class MultiVersionModFix1
{
	public static ModContentPack GetPack()
	{
		var thisAssembly = typeof(MultiVersionModFix1).Assembly;
		return LoadedModManager.RunningModsListForReading
				.FirstOrFallback(mod => mod.assemblies.loadedAssemblies.Contains(thisAssembly));
	}

	static void Postfix(ModMetaData __instance, ref bool __result)
	{
		if (VersionControl.CurrentMajor == 0 && VersionControl.CurrentMinor >= 19)
			if (__instance.Identifier == GetPack().Identifier)
				__result = true;
	}
}

[HarmonyPatch(typeof(Workshop))]
[HarmonyPatch("SetWorkshopItemDataFrom")]
public static class MultiVersionModFix2
{
	static IList<string> GetTags(string rootDir)
	{
		var xml = new XmlDocument();
		xml.Load(rootDir + Path.DirectorySeparatorChar + "About" + Path.DirectorySeparatorChar + "Manifest.xml");
		return xml.SelectNodes("/Manifest/tags/li").Cast<XmlNode>().Select(node => node.InnerText).ToList();
	}

	static void Postfix(UGCUpdateHandle_t updateHandle, WorkshopItemHook hook)
	{
		var rootDir = MultiVersionModFix1.GetPack().RootDir;
		if (rootDir == hook.Directory.FullName)
			SteamUGC.SetItemTags(updateHandle, GetTags(rootDir));
	}
}