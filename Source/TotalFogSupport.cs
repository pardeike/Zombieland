using HarmonyLib;
using System;
using Verse;

namespace ZombieLand
{
	[StaticConstructorOnStartup]
	static class TotalFogSupport
	{
		// Bind once without a Total Fog dependency. Ordinary zombie rendering
		// uses native culling; cross-pawn effects and live readouts query sight here.
		static readonly Func<Thing, bool> isVisible = BindVisibility();
		static readonly Func<Map, IntVec3, bool> isCellVisible = BindCellVisibility();
		static readonly Func<TargetInfo, float> audibility = BindAudibility();

		static TotalFogSupport()
		{
			if (isCellVisible == null)
				return;
			var type = AccessTools.TypeByName("TotalFog.Visibility");
			var method = AccessTools.DeclaredMethod(type, "RegisterRenderer", new[] { typeof(Type), typeof(Func<Thing, bool>) });
			if (method == null || method.IsStatic == false || method.ReturnType != typeof(void))
			{
				isCellVisible = null;
				return;
			}
			var register = (Action<Type, Func<Thing, bool>>)Delegate.CreateDelegate(typeof(Action<Type, Func<Thing, bool>>), method);
			register(typeof(ZombieSymbiant), thing => ((ZombieSymbiant)thing).HasVisibleRenderPart());
			var inspection = AccessTools.DeclaredMethod(type, "RegisterInspectionCell", new[] { typeof(Type), typeof(Func<Thing, IntVec3>) });
			if (inspection?.IsStatic == true && inspection.ReturnType == typeof(void))
			{
				var registerInspection = (Action<Type, Func<Thing, IntVec3>>)Delegate.CreateDelegate(typeof(Action<Type, Func<Thing, IntVec3>>), inspection);
				registerInspection(typeof(ZombieSymbiant), thing => ((ZombieSymbiant)thing).FogInspectionCell);
			}
		}

		internal static bool HasVisibilityFilter => isVisible != null;
		internal static bool HasCellVisibilityFilter => isCellVisible != null;
		internal static bool IsVisible(Thing thing) => isVisible == null || isVisible(thing);
		internal static bool IsVisible(Map map, IntVec3 cell) => isCellVisible == null || isCellVisible(map, cell);
		internal static float Audibility(Thing thing) => Audibility(new TargetInfo(thing));
		internal static float Audibility(TargetInfo source) => audibility == null ? 1f : audibility(source);

		internal static int VisibleZombieCount(TickManager tickManager)
		{
			if (isVisible == null)
				return tickManager.ZombieCount();
			var zombies = tickManager.allZombiesCached;
			if (zombies == null)
				return 0;
			var count = 0;
			foreach (var zombie in zombies)
				if (zombie.Spawned && zombie.Dead == false && isVisible(zombie))
					count++;
			return count;
		}

		static Func<Thing, bool> BindVisibility()
		{
			var type = AccessTools.TypeByName("TotalFog.Visibility");
			if (type == null)
				return null;
			var method = AccessTools.DeclaredMethod(type, "IsVisible", new[] { typeof(Thing) });
			if (method == null || method.IsStatic == false || method.ReturnType != typeof(bool))
			{
				Log.Warning("Zombieland: Total Fog's live visibility query is unavailable.");
				return null;
			}
			return (Func<Thing, bool>)Delegate.CreateDelegate(typeof(Func<Thing, bool>), method);
		}

		static Func<Map, IntVec3, bool> BindCellVisibility()
		{
			var type = AccessTools.TypeByName("TotalFog.Visibility");
			if (type == null)
				return null;
			var method = AccessTools.DeclaredMethod(type, "IsVisible", new[] { typeof(Map), typeof(IntVec3) });
			return method?.IsStatic == true && method.ReturnType == typeof(bool)
				? (Func<Map, IntVec3, bool>)Delegate.CreateDelegate(typeof(Func<Map, IntVec3, bool>), method)
				: null;
		}

		static Func<TargetInfo, float> BindAudibility()
		{
			var type = AccessTools.TypeByName("TotalFog.SoundAudibility");
			if (type == null)
				return null;
			var method = AccessTools.DeclaredMethod(type, "GetAudibilityFactor", new[] { typeof(TargetInfo) });
			return method?.IsStatic == true && method.ReturnType == typeof(float)
				? (Func<TargetInfo, float>)Delegate.CreateDelegate(typeof(Func<TargetInfo, float>), method)
				: null;
		}
	}
}
