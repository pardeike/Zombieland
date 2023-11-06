using HarmonyLib;
using Verse;
using static HarmonyLib.AccessTools;

namespace ZombieLand
{
	public class DubsTools
	{
		public static void Init()
		{
			var harmony = new Harmony("net.pardeike.zombieland.dubs");
			var method = Method("Analyzer.Fixes.H_DrawNamesFix:Prefix");
			if (method != null)
			{
				var prefix = SymbolExtensions.GetMethodInfo((bool b) => Prefix(default, ref b));
				harmony.Patch(method, prefix: new HarmonyMethod(prefix));
			}
		}

		static bool Prefix([HarmonyArgument("__instance")] PawnUIOverlay instance, ref bool __result)
		{
			if (instance.pawn is not Zombie)
				return true;

			__result = true;
			return false;
		}
	}
}