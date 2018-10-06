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
public static class MultiVersionModFix3
{
	static IList<string> GetTags(string rootDir)
	{
		var xml = new XmlDocument();
		xml.Load(rootDir + Path.DirectorySeparatorChar + "About" + Path.DirectorySeparatorChar + "Manifest.xml");
		var tags = xml.SelectNodes("/Manifest/targetVersions/li").Cast<XmlNode>().Select(node => (node.InnerText + "$").Replace(".0$", ""));
		return tags.Add("Mod").ToList();
	}

	static void Postfix(UGCUpdateHandle_t updateHandle, WorkshopItemHook hook)
	{
		var rootDir = MultiVersionModFix1.GetPack().RootDir;
		if (rootDir == hook.Directory.FullName)
			SteamUGC.SetItemTags(updateHandle, GetTags(rootDir));
	}
}