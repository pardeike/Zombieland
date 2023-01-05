using HarmonyLib;
using System.Linq;
using System.Reflection;
using Verse;

namespace ZombieLand
{
	public class CustomLeaner : PawnLeaner
	{
		public CustomLeaner(Pawn pawn) : base(pawn) { }

		public InfectionState infectionState = InfectionState.None;
	}

	[HarmonyPatch]
	static class Pawn_DrawTracker_Constructor_Patch
	{
		static MethodBase TargetMethod()
		{
			return AccessTools.Constructor(typeof(Pawn_DrawTracker), new[] { typeof(Pawn) });
		}

		[HarmonyPriority(-100000)]
		static void Postfix(Pawn_DrawTracker __instance, Pawn pawn)
		{
			if (__instance.leaner.GetType() != typeof(PawnLeaner))
			{
				var patches = Harmony.GetPatchInfo(TargetMethod());
				var postfixes = patches.Prefixes.Where(p => p.owner != "net.pardeike.zombieland");
				var transpilers = patches.Transpilers;
				var mods = postfixes.Union(transpilers).Join(t => t.owner);
				Log.Error($"ZombieLand error: Pawn_DrawTracker.leaner is not of type PawnLeaner (Possible mod conflict with: {mods})");
				return;
			}
			__instance.leaner = new CustomLeaner(pawn);
		}
	}

	public static class PawnInfoExtensions
	{
		public static InfectionState InfectionState(this Pawn pawn) => (pawn.drawer.leaner as CustomLeaner).infectionState;
		public static void SetInfectionState(this Pawn pawn, InfectionState state) => (pawn.drawer.leaner as CustomLeaner).infectionState = state;
	}
}