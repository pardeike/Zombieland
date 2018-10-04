using Harmony;
using RimWorld;
using System.Linq;
using System.Reflection;
using Verse;

namespace SaneModdersWorld
{
	[HarmonyPatch(typeof(ModMetaData))]
	[HarmonyPatch("VersionCompatible", MethodType.Getter)]
	[StaticConstructorOnStartup]
	public static class MultiVersionModFix
	{
		static readonly FastInvokeHandler translateHandler = null;
		const string methodName = "Translate";
		const string oldClassName = "Verse.Translator";
		const string newClassName = "Verse.TranslatorFormattedStringExtensions";

		static MultiVersionModFix()
		{
			bool IsTranslateMethod(MethodInfo method) =>
				method.Name == methodName &&
				method.GetParameters().Count() == 2 &&
				method.GetParameters()[0].ParameterType == typeof(string) &&
				method.GetParameters()[1].ParameterType.IsArray;

			var oldTranslate = AccessTools.TypeByName(oldClassName).GetMethods()
				.FirstOrDefault(IsTranslateMethod);
			if (oldTranslate != null)
			{
				translateHandler = MethodInvoker.GetHandler(oldTranslate);
				return;
			}

			var newTranslate = AccessTools.TypeByName(newClassName).GetMethods()
				.FirstOrDefault(IsTranslateMethod);
			translateHandler = MethodInvoker.GetHandler(newTranslate);
		}

		static void Postfix(ModMetaData __instance, ref bool __result)
		{
			var thisAssembly = typeof(MultiVersionModFix).Assembly;
			var modContentPack = LoadedModManager.RunningModsListForReading
				.FirstOrFallback(mod => mod.assemblies.loadedAssemblies.Contains(thisAssembly));

			if (VersionControl.CurrentMajor == 0 && VersionControl.CurrentMinor >= 19)
				if (__instance.Identifier == modContentPack.Identifier)
					__result = true;
		}

		public static string Translate(this string key, params object[] args)
		{
			return translateHandler(null, new object[] { key, args }) as string;
		}
	}
}