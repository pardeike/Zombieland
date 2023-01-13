using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;
using Verse;

namespace ZombieLand
{
	public class SwingAction : IEquatable<SwingAction>
	{
		public int beginIndex;
		public int endIndex;
		public float angle = 180f;
		public int state = 0;

		public bool Equals(SwingAction other)
		{
			if (beginIndex == other.beginIndex && endIndex == other.endIndex)
				return true;
			if (beginIndex == other.endIndex && endIndex == other.beginIndex)
				return true;
			return false;
		}

		public override bool Equals(object obj) => Equals(obj as SwingAction);

		public override int GetHashCode()
		{
			return beginIndex ^ endIndex;
		}
	}

	public class CustomPawnState : PawnLeaner
	{
		public CustomPawnState(Pawn pawn) : base(pawn) { }

		public InfectionState infectionState = InfectionState.None;
		public SwingAction currentSwingAction = null;
		public SwingAction nextSwingAction = null;

		public void SetNextSwingAction(SwingAction newSwingAction)
		{
			nextSwingAction = newSwingAction;
		}

		public void UseNextSwingAction()
		{
			if (nextSwingAction != null)
			{
				var oldAngle = currentSwingAction?.angle ?? 180f;
				currentSwingAction = nextSwingAction;
				currentSwingAction.angle = oldAngle;
				currentSwingAction.state = 1;
			}
		}
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
			__instance.leaner = new CustomPawnState(pawn);
		}
	}

	public static class PawnInfoExtensions
	{
		public static InfectionState InfectionState(this Pawn pawn) => (pawn.drawer.leaner as CustomPawnState).infectionState;
		public static void SetInfectionState(this Pawn pawn, InfectionState state) => (pawn.drawer.leaner as CustomPawnState).infectionState = state;

		public static (SwingAction current, SwingAction next) SwingActions(this Pawn pawn)
		{
			var customPawnState = pawn.drawer.leaner as CustomPawnState;
			return (customPawnState?.currentSwingAction, customPawnState?.nextSwingAction);
		}
		public static void SetNextSwingAction(this Pawn pawn, SwingAction state) => (pawn.drawer.leaner as CustomPawnState).SetNextSwingAction(state);
		public static void UseNextSwingAction(this Pawn pawn) => (pawn.drawer.leaner as CustomPawnState).UseNextSwingAction();

		public static float SwingAngle(this Pawn pawn) => (pawn.drawer.leaner as CustomPawnState).currentSwingAction?.angle ?? 180f;
		public static void SetSwingAngle(this Pawn pawn, float state) => (pawn.drawer.leaner as CustomPawnState).currentSwingAction.angle = state;
	}
}