using Harmony;
using RimWorld;
using Steamworks;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using Verse;
using Verse.Steam;

[StaticConstructorOnStartup]
static class MultiVersionModFix
{
	const string _multiversionmodfix = "multiversionmodfix";
	static readonly Dictionary<string, List<System.Version>> cachedVersions = new Dictionary<string, List<System.Version>>();

	static MultiVersionModFix()
	{
		var instance = HarmonyInstance.Create(_multiversionmodfix);
		var aBool = false;
		var m_VersionCompatible = AccessTools.Property(typeof(ModMetaData), nameof(ModMetaData.VersionCompatible)).GetGetMethod();
		var m_VersionCompatiblePostfix = SymbolExtensions.GetMethodInfo(() => VersionCompatible_Postfix(null, ref aBool));
		instance.PostfixOnce(m_VersionCompatible, m_VersionCompatiblePostfix);
		var m_SetWorkshopItemDataFrom = AccessTools.Method(typeof(Workshop), "SetWorkshopItemDataFrom");
		var m_SetWorkshopItemDataFrom_Postfix = SymbolExtensions.GetMethodInfo(() => SetWorkshopItemDataFrom_Postfix(default(UGCUpdateHandle_t), null));
		instance.PostfixOnce(m_SetWorkshopItemDataFrom, m_SetWorkshopItemDataFrom_Postfix);
	}

	static void PostfixOnce(this HarmonyInstance instance, MethodInfo original, MethodInfo postfix)
	{
		var postfixes = instance.GetPatchInfo(original)?.Postfixes;
		if (postfixes == null || !postfixes.Any(patch => patch != null && patch.owner == _multiversionmodfix))
			instance.Patch(original, postfix: new HarmonyMethod(postfix));
	}

	static List<System.Version> GetTaggedVersions(string rootDir)
	{
		if (cachedVersions.TryGetValue(rootDir, out var cached))
			return cached;
		try
		{
			var xml = new XmlDocument();
			xml.Load(rootDir + Path.DirectorySeparatorChar + "About" + Path.DirectorySeparatorChar + "Manifest.xml");
			var tags = xml.SelectNodes("/Manifest/targetVersions/li").Cast<XmlNode>().Select(node => node.InnerText);
			var result = tags
				.Where(tag => VersionControl.IsWellFormattedVersionString(tag))
				.Select(tag => VersionControl.VersionFromString(tag)).ToList();
			cachedVersions[rootDir] = result;
			return result;
		}
		catch (System.Exception)
		{
			cachedVersions[rootDir] = null;
			return null;
		}
	}

	static void VersionCompatible_Postfix(ModMetaData __instance, ref bool __result)
	{
		var taggedVersions = GetTaggedVersions(__instance.RootDir.FullName);
		if (taggedVersions.NullOrEmpty()) return;
		var currentVersion = VersionControl.CurrentVersion;
		__result = taggedVersions
			.Any(version =>
				version.Major == currentVersion.Major &&
				version.Minor == currentVersion.Minor &&
				(version.Build == 0 || version.Build == currentVersion.Build));
	}

	static void SetWorkshopItemDataFrom_Postfix(UGCUpdateHandle_t updateHandle, WorkshopItemHook hook)
	{
		var taggedVersions = GetTaggedVersions(hook.Directory.FullName);
		if (!taggedVersions.NullOrEmpty())
		{
			var tags = taggedVersions.Select(version => version.Major + "." + version.Minor);
			tags.Add("Mod");
			SteamUGC.SetItemTags(updateHandle, tags.Distinct().ToList());
		}
	}
}