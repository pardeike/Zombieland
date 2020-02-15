using HarmonyLib;
using RimWorld;
using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	[StaticConstructorOnStartup]
	public static class MultiVersionMethods
	{
		static MultiVersionMethods()
		{
			Prepare_TryFindRandomPawnEntryCell();
			Prepare_DoBottomButtons();
		}

		public delegate bool TryFindRandomPawnEntryCellDelegate(out IntVec3 result, Map map, float roadChance, Predicate<IntVec3> extraValidator, out IntVec3 result2, Map map2, float roadChance2, bool allowFogged, Predicate<IntVec3> extraValidator2);
		public static TryFindRandomPawnEntryCellDelegate TryFindRandomPawnEntryCell;
		static void Prepare_TryFindRandomPawnEntryCell()
		{
			var method = AccessTools.Method(typeof(RCellFinder), "TryFindRandomPawnEntryCell");
			TryFindRandomPawnEntryCell = CreateMultiWrapper<TryFindRandomPawnEntryCellDelegate>(method, 4);
		}

		public delegate void DoBottomButtonsDelegate(Page _this, Rect rect, string nextLabel, string midLabel, Action midAct, bool showNext, Rect rect2, string nextLabel2, string midLabel2, Action midAct2, bool showNext2, bool doNextOnKeypress);
		public static DoBottomButtonsDelegate DoBottomButtons;
		static void Prepare_DoBottomButtons()
		{
			var method = AccessTools.Method(typeof(Page), "DoBottomButtons");
			DoBottomButtons = CreateMultiWrapper<DoBottomButtonsDelegate>(method, 5);
		}

		//

		static T CreateMultiWrapper<T>(MethodInfo method, int oldParameterCount) where T : class
		{
			var parameters = typeof(T).GetMethod("Invoke").GetParameters().Select(param => param.ParameterType);
			var skipInstance = method.IsStatic ? 0 : 1;
			var oldTypes = parameters.Skip(skipInstance).Take(oldParameterCount).ToArray();
			var newTypes = parameters.Skip(skipInstance).Skip(oldParameterCount).ToArray();

			var name = method.Name + "_" + typeof(MultiVersionMethods).Namespace + "_MultiDelegate";
			var parameterTypes = oldTypes.Concat(newTypes).ToList();
			if (method.IsStatic == false)
				parameterTypes.Insert(0, method.DeclaringType);
			var dynamicMethod = new DynamicMethod(name, MethodAttributes.Static, CallingConventions.Standard, method.ReturnType, parameterTypes.ToArray(), method.DeclaringType, true);
			var il = dynamicMethod.GetILGenerator();
			var idx = 0;
			if (method.IsStatic == false)
			{
				il.Emit(OpCodes.Ldarg_0);
				idx++;
			}
			var realArgTypes = method.GetParameters().Select(param => param.ParameterType).ToArray();
			if (realArgTypes.SequenceEqual(newTypes))
				idx += oldParameterCount;
			for (var i = 0; i < realArgTypes.Length; i++)
				il.Emit(OpCodes.Ldarg, idx++);
			il.EmitCall(OpCodes.Call, method, null);
			il.Emit(OpCodes.Ret);
			return dynamicMethod.CreateDelegate(typeof(T)) as T;
		}
	}
}