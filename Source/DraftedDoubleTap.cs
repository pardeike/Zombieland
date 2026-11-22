using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using Verse;

namespace ZombieLand
{
	[HarmonyPatch(typeof(FloatMenuOptionProvider_WorkGivers), "GetWorkGiverOption")]
	static class FloatMenuOptionProvider_DraftedDoubleTap_Patch
	{
		static int OrderPriority(int priority, Pawn pawn, WorkGiverDef workGiver)
		{
			if (pawn.Drafted && workGiver.giverClass == typeof(WorkGiver_DoubleTap) && pawn.WorkTypeIsDisabled(workGiver.workType) == false)
				return 1;
			return priority;
		}

		// Keep the native menu and job checks, changing only the assignment gate
		// for an explicit drafted Double Tap. Automatic hunting is unaffected.
		// Leave GetPriority available for Achtung to replace, then adjust its result.
		[HarmonyBefore("net.pardeike.rimworld.mods.achtung")]
		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var original = AccessTools.Method(typeof(Pawn_WorkSettings), nameof(Pawn_WorkSettings.GetPriority));
			var replacement = AccessTools.Method(typeof(FloatMenuOptionProvider_DraftedDoubleTap_Patch), nameof(OrderPriority));
			var result = new List<CodeInstruction>();
			var matches = 0;
			foreach (var instruction in instructions)
			{
				result.Add(instruction);
				if (instruction.Calls(original))
				{
					result.Add(new CodeInstruction(OpCodes.Ldarg_1));
					result.Add(new CodeInstruction(OpCodes.Ldarg_2));
					result.Add(new CodeInstruction(OpCodes.Call, replacement));
					matches++;
				}
			}
			if (matches != 1)
				throw new InvalidOperationException($"Zombieland: expected one ordered-work priority check, found {matches}.");
			return result;
		}
	}
}
