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
		static int OrderPriority(Pawn_WorkSettings settings, WorkTypeDef workType, Pawn pawn, WorkGiverDef workGiver)
		{
			if (pawn.Drafted && workGiver.giverClass == typeof(WorkGiver_DoubleTap) && pawn.WorkTypeIsDisabled(workType) == false)
				return 1;
			return settings.GetPriority(workType);
		}

		// Keep the native menu and job checks, changing only the assignment gate
		// for an explicit drafted Double Tap. Automatic hunting is unaffected.
		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var original = AccessTools.Method(typeof(Pawn_WorkSettings), nameof(Pawn_WorkSettings.GetPriority));
			var replacement = AccessTools.Method(typeof(FloatMenuOptionProvider_DraftedDoubleTap_Patch), nameof(OrderPriority));
			var result = new List<CodeInstruction>();
			var matches = 0;
			foreach (var instruction in instructions)
			{
				if (instruction.Calls(original))
				{
					var pawn = new CodeInstruction(OpCodes.Ldarg_1);
					pawn.labels.AddRange(instruction.labels);
					instruction.labels.Clear();
					result.Add(pawn);
					result.Add(new CodeInstruction(OpCodes.Ldarg_2));
					instruction.opcode = OpCodes.Call;
					instruction.operand = replacement;
					matches++;
				}
				result.Add(instruction);
			}
			if (matches != 1)
				throw new InvalidOperationException($"Zombieland: expected one ordered-work priority check, found {matches}.");
			return result;
		}
	}
}
