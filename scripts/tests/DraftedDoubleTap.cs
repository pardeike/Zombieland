using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using Verse;
using ZombieLand;

static class Program
{
	static int Main(string[] args)
	{
		var directories = new[] { args[0], args[1], Path.GetDirectoryName(args[2]), args[3] };
		AssemblyLoadContext.Default.Resolving += (_, name) =>
		{
			var path = directories.Select(dir => Path.Combine(dir, name.Name + ".dll")).FirstOrDefault(File.Exists);
			return path == null ? null : AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
		};
		try
		{
			Run(args[2]);
			return 0;
		}
		catch (Exception error)
		{
			Console.Error.WriteLine(error);
			return 1;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	static void Run(string achtungPath)
	{
		var original = AccessTools.Method(typeof(FloatMenuOptionProvider_WorkGivers), "GetWorkGiverOption");
		var nativePriority = AccessTools.Method(typeof(Pawn_WorkSettings), nameof(Pawn_WorkSettings.GetPriority));
		var patchType = typeof(FloatMenuOptionProvider_DraftedDoubleTap_Patch);
		var transpiler = AccessTools.Method(patchType, "Transpiler");
		var adjust = AccessTools.Method(patchType, "OrderPriority");
		var achtungType = Assembly.LoadFrom(achtungPath).GetType("AchtungMod.FloatMenuOptionProvider_WorkGivers_GetWorkGiverOption_Patch", true);
		var achtungTranspiler = AccessTools.Method(achtungType, "Transpiler");
		var achtungPriority = AccessTools.Method(achtungType, "AchtungGetPriority");
		var achtungDecorate = AccessTools.Method(achtungType, "DecorateForcedTask");

		List<CodeInstruction> Apply(MethodInfo method, IEnumerable<CodeInstruction> input)
			=> ((IEnumerable<CodeInstruction>)method.Invoke(null, new object[] { input })).ToList();
		void Check(bool condition, string message)
		{
			if (!condition) throw new Exception(message);
			Console.WriteLine("PASS " + message);
		}

		var native = PatchProcessor.GetOriginalInstructions(original);
		Check(native.Count(code => code.Calls(nativePriority)) == 1, "Real menu has one native priority gate");
		var solo = Apply(transpiler, native);
		Check(solo.Count(code => code.Calls(nativePriority)) == 1, "Zombieland retains the native call for other transpilers");
		Check(solo.Count(code => code.Calls(adjust)) == 1, "Zombieland adjusts exactly one priority result");
		foreach (var achtungFirst in new[] { true, false })
		{
			var patches = new[]
			{
				new Patch(new HarmonyMethod(transpiler), achtungFirst ? 1 : 0, "zombieland"),
				new Patch(new HarmonyMethod(achtungTranspiler), achtungFirst ? 0 : 1, "net.pardeike.rimworld.mods.achtung")
			};
			var ordered = PatchProcessor.GetSortedPatchMethods(original, patches);
			Check(ordered.SequenceEqual(new[] { transpiler, achtungTranspiler }), "Harmony orders Zombieland first, Achtung installed first=" + achtungFirst);
			var combined = PatchProcessor.GetOriginalInstructions(original);
			foreach (var method in ordered) combined = Apply(method, combined);
			var index = combined.FindIndex(code => code.Calls(achtungPriority));
			Check(index >= 0 && combined.Count(code => code.Calls(achtungPriority)) == 1
				&& combined.Count(code => code.Calls(nativePriority)) == 0
				&& combined[index + 1].opcode == OpCodes.Ldarg_1
				&& combined[index + 2].opcode == OpCodes.Ldarg_2
				&& combined[index + 3].Calls(adjust), "Real Achtung priority feeds Zombieland adjustment");
			Check(combined.Count(code => code.Calls(achtungDecorate)) == 1, "Achtung keeps forced-menu decoration");
		}

		var label = new DynamicMethod("labels", typeof(void), Type.EmptyTypes).GetILGenerator().DefineLabel();
		var call = new CodeInstruction(OpCodes.Callvirt, nativePriority);
		call.labels.Add(label);
		call.blocks.Add(new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock));
		var labelled = Apply(transpiler, new[] { call });
		Check(labelled[0] == call && call.Calls(nativePriority) && call.labels.Contains(label)
			&& call.blocks.Count == 1, "Original call keeps branch labels and exception blocks");
		foreach (var count in new[] { 0, 2 })
		{
			try
			{
				Apply(transpiler, Enumerable.Range(0, count).Select(_ => new CodeInstruction(OpCodes.Callvirt, nativePriority)));
				throw new Exception("Accepted invalid anchor count " + count);
			}
			catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException)
			{
				Console.WriteLine("PASS Reject invalid anchor count " + count);
			}
		}

		// Set only the fields read by the helper. No game, map, defs, or UI are initialized.
		var pawn = (Pawn)RuntimeHelpers.GetUninitializedObject(typeof(Pawn));
		pawn.drafter = (Pawn_DraftController)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_DraftController));
		var workType = new WorkTypeDef();
		var disabled = new List<WorkTypeDef>();
		AccessTools.Field(typeof(Pawn), "cachedDisabledWorkTypes").SetValue(pawn, disabled);
		var giver = new WorkGiverDef { workType = workType };
		var rows = 0;
		foreach (var drafted in new[] { false, true })
		foreach (var doubleTap in new[] { false, true })
		foreach (var incapable in new[] { false, true })
		foreach (var priority in new[] { 0, 1, 2, 4 })
		{
			AccessTools.Field(typeof(Pawn_DraftController), "draftedInt").SetValue(pawn.drafter, drafted);
			giver.giverClass = doubleTap ? typeof(WorkGiver_DoubleTap) : typeof(WorkGiver_Scanner);
			disabled.Clear();
			if (incapable) disabled.Add(workType);
			var expected = drafted && doubleTap && !incapable ? 1 : priority;
			var actual = (int)adjust.Invoke(null, new object[] { priority, pawn, giver });
			if (actual != expected)
				throw new Exception($"Priority {priority}, drafted={drafted}, doubleTap={doubleTap}, incapable={incapable}: expected {expected}, got {actual}");
			rows++;
		}
		Console.WriteLine($"PASS {rows} priority rows preserve normal and incapable work and enable eligible drafted Double Tap");
	}
}
