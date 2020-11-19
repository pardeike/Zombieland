using System;
using System.Reflection;
using System.Reflection.Emit;
using Verse;
using static HarmonyLib.AccessTools;

namespace ZombieLand
{
	public static class AlienTools
	{
		public static Func<Pawn, bool> IsFleshPawn = pawn => pawn.RaceProps.Humanlike;

		public static void Init()
		{
			var t_ThingDef_AlienRace = TypeByName("AlienRace.ThingDef_AlienRace");
			if (t_ThingDef_AlienRace == null) return;
			var t_ThingDef_AlienRace_AlienSettings = TypeByName("AlienRace.ThingDef_AlienRace+AlienSettings");
			if (t_ThingDef_AlienRace_AlienSettings == null) return;
			var f_alienRace = Field(t_ThingDef_AlienRace, "alienRace");
			if (f_alienRace == null) return;
			var f_compatibility = Field(t_ThingDef_AlienRace_AlienSettings, "compatibility");
			if (f_compatibility == null) return;
			var m_IsFleshPawn = Method(f_compatibility.FieldType, "IsFleshPawn");
			if (m_IsFleshPawn == null) return;

			// public static bool IsFleshPawn(this Pawn pawn)
			// {
			// 	var alienDef = pawn.def as ThingDef_AlienRace;
			// 	if (alienDef == null) return true;
			// 	return alienDef.alienRace.compatibility.IsFleshPawn(pawn);
			// }

			var dynamicMethod = new DynamicMethod("CheckAlienRace", MethodAttributes.Static | MethodAttributes.Public, CallingConventions.Standard, typeof(bool), new[] { typeof(Pawn) }, typeof(Pawn), true);
			var il = dynamicMethod.GetILGenerator();
			var jump = il.DefineLabel();
			var variable = il.DeclareLocal(t_ThingDef_AlienRace);
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Ldfld, Field(typeof(Thing), nameof(Thing.def)));
			il.Emit(OpCodes.Isinst, t_ThingDef_AlienRace);
			il.Emit(OpCodes.Stloc, variable);
			il.Emit(OpCodes.Ldloc, variable);
			il.Emit(OpCodes.Brtrue_S, jump);
			il.Emit(OpCodes.Ldc_I4_1);
			il.Emit(OpCodes.Ret);
			il.MarkLabel(jump);
			il.Emit(OpCodes.Ldloc, variable);
			il.Emit(OpCodes.Ldfld, f_alienRace);
			il.Emit(OpCodes.Ldfld, f_compatibility);
			il.Emit(OpCodes.Ldarg_0);
			il.Emit(OpCodes.Callvirt, m_IsFleshPawn);
			il.Emit(OpCodes.Ret);
			IsFleshPawn = dynamicMethod.CreateDelegate(typeof(Func<Pawn, bool>)) as Func<Pawn, bool>;
		}
	}
}