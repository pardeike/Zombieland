using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using RimWorld;
using Verse;
using static HarmonyLib.Code;

// TODO the following is code that adds all patches to track future contamination
//      its purpose is to test compatibility before actually releasing such a feature
//
namespace ZombieLand
{
	//[HarmonyPatch(typeof(ThingMaker), nameof(ThingMaker.MakeThing))]
	static file class Log
	{
		public static void Warning(string txt)
		{
			_ = txt; //Verse.Log.Warning(txt);
		}

		[HarmonyPostfix]
		static void DebugLogMakeThing(Thing __result)
		{
			if (MapGenerator.mapBeingGenerated == null && Current.Game?.initData == null)
			{
				Verse.Log.ResetMessageCount();
				Verse.Log.Message($"# {__result}");
			}
		}
	}

	[HarmonyPatch(typeof(Frame), nameof(Frame.CompleteConstruction))]
	static class Frame_CompleteConstruction_TestPatch
	{
		static float ClearAndDestroyContents(ThingOwner self, DestroyMode mode)
		{
			var marketValue = self.Sum(thing => { Log.Warning($"Consume {thing} [{thing.MarketValue}]"); return thing.MarketValue; });
			self.ClearAndDestroyContents(mode);
			return marketValue;
		}

		static Thing MakeThing(ThingDef def, ThingDef stuff, float sum)
		{
			var result = ThingMaker.MakeThing(def, stuff);
			Log.Warning($"Produce {result} [{sum}]");
			return result;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
		{
			var sumVar = generator.DeclareLocal(typeof(float));

			var from1 = SymbolExtensions.GetMethodInfo((ThingOwner owner) => owner.ClearAndDestroyContents(DestroyMode.Vanish));
			var to1 = SymbolExtensions.GetMethodInfo(() => ClearAndDestroyContents(default, default));

			var from2 = SymbolExtensions.GetMethodInfo(() => ThingMaker.MakeThing(default, default));
			var to2 = SymbolExtensions.GetMethodInfo(() => MakeThing(default, default, default));

			return new CodeMatcher(instructions)
				 .MatchStartForward(new CodeMatch(operand: from1))
				 .ThrowIfInvalid($"Cannot find {from1.FullDescription()}")
				 .SetOperandAndAdvance(to1)
				 .Insert(Stloc[sumVar])
				 .MatchStartForward(new CodeMatch(operand: from2))
				 .ThrowIfInvalid($"Cannot find {from2.FullDescription()}")
				 .InsertAndAdvance(Ldloc[sumVar])
				 .SetInstruction(Call[to2])
				 .InstructionEnumeration();
		}
	}

	[HarmonyPatch(typeof(GenRecipe), nameof(GenRecipe.MakeRecipeProducts))]
	static class GenReciepe_MakeRecipeProducts_TestPatch
	{
		static IEnumerable<Thing> Postfix(IEnumerable<Thing> things, List<Thing> ingredients)
		{
			var thingList = things.ToArray();
			Log.Warning($"Produce {thingList.Join(t => $"{t}")} from {ingredients.Join(t => $"{t}")}");
			foreach (var thing in thingList)
				yield return thing;
		}
	}

	[HarmonyPatch(typeof(Thing), nameof(Thing.ButcherProducts))]
	static class Thing_ButcherProducts_TestPatch
	{
		static IEnumerable<Thing> Postfix(IEnumerable<Thing> things, Thing __instance)
		{
			var thingList = things.ToArray();
			Log.Warning($"Produce {thingList.Join(t => $"{t}")} from {__instance}");
			foreach (var thing in thingList)
				yield return thing;
		}
	}

	[HarmonyPatch(typeof(Thing), nameof(Thing.SplitOff))]
	static class Thing_SplitOff_TestPatch
	{
		static void Postfix(Thing __result, Thing __instance)
		{
			if (__result != __instance)
				Log.Warning($"Split off {__result} from {__instance}");
		}
	}

	[HarmonyPatch(typeof(MinifiedThing), nameof(MinifiedThing.SplitOff))]
	static class MinifiedThing_SplitOff_TestPatch
	{
		static void Postfix(Thing __result, MinifiedThing __instance)
		{
			if (__result != __instance)
				Log.Warning($"Split off {__result} from {__instance}");
		}
	}

	[HarmonyPatch(typeof(MinifyUtility), nameof(MinifyUtility.MakeMinified))]
	static class MinifyUtility_MakeMinified_TestPatch
	{
		static void Postfix(MinifiedThing __result, Thing thing)
		{
			Log.Warning($"Minified {__result} from {thing}");
		}
	}

	[HarmonyPatch]
	static class GenSpawn_Spawn_Replacement_TestPatch
	{
		static IEnumerable<MethodBase> TargetMethods()
		{
			yield return SymbolExtensions.GetMethodInfo((WildPlantSpawner spawner) => spawner.CheckSpawnWildPlantAt(IntVec3.Zero, 0f, 0f, false));
			yield return SymbolExtensions.GetMethodInfo((TunnelJellySpawner spawner) => spawner.Spawn(null, IntVec3.Zero));
		}

		static Thing Spawn(Thing newThing, IntVec3 loc, Map map, WipeMode wipeMode)
		{
			var thing = GenSpawn.Spawn(newThing, loc, map, wipeMode);
			if (MapGenerator.mapBeingGenerated == null && Current.Game?.initData == null)
				Log.Warning($"Spawned {thing} at {loc}");
			return thing;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var from = SymbolExtensions.GetMethodInfo(() => GenSpawn.Spawn((Thing)null, IntVec3.Zero, null, WipeMode.Vanish));
			var to = SymbolExtensions.GetMethodInfo(() => Spawn(null, IntVec3.Zero, null, WipeMode.Vanish));
			return instructions.MethodReplacer(from, to);
		}
	}

	[HarmonyPatch(typeof(SmoothableWallUtility), nameof(SmoothableWallUtility.SmoothWall))]
	static class SmoothableWallUtility_SmoothWall_TestPatch
	{
		static void Prefix(Thing target, out float __state)
		{
			__state = target.MarketValue;
		}

		static void Postfix(Thing __result, float __state)
		{
			Log.Warning($"Smoothed {__result} [{__state}]");
		}
	}

	[HarmonyPatch(typeof(SmoothableWallUtility), nameof(SmoothableWallUtility.Notify_BuildingDestroying))]
	static class SmoothableWallUtility_Notify_BuildingDestroying_TestPatch
	{
		static Thing Spawn(Thing newThing, IntVec3 loc, Map map, Rot4 rot, WipeMode wipeMode, bool respawningAfterLoad, Thing destroyedThing)
		{
			var thing = GenSpawn.Spawn(newThing, loc, map, rot, wipeMode, respawningAfterLoad);
			Log.Warning($"Produced {thing} from destroyed {destroyedThing}");
			return thing;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var from = SymbolExtensions.GetMethodInfo(() => GenSpawn.Spawn(null, IntVec3.Zero, null, Rot4.Invalid, WipeMode.Vanish, false));
			var to = SymbolExtensions.GetMethodInfo(() => Spawn(null, IntVec3.Zero, null, Rot4.Invalid, WipeMode.Vanish, false, null));

			return new CodeMatcher(instructions)
			.MatchStartForward(new CodeMatch(operand: from))
				 .ThrowIfInvalid($"Cannot find {from.FullDescription()}")
				 .InsertAndAdvance(Ldarg_0)
				 .SetInstruction(Call[to])
				 .InstructionEnumeration();
		}
	}

	[HarmonyPatch(typeof(Mineable), nameof(Mineable.TrySpawnYield))]
	static class Mineable_TrySpawnYield_TestPatch
	{
		static Thing MakeThing(ThingDef def, ThingDef stuff, Mineable mineable)
		{
			var thing = ThingMaker.MakeThing(def, stuff);
			Log.Warning($"Yielded {thing} from {mineable}");
			return thing;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> Tools.MakeThingTranspiler(instructions, () => MakeThing(default, default, default));
	}

	[HarmonyPatch(typeof(Pawn_FilthTracker), nameof(Pawn_FilthTracker.TryPickupFilth))]
	static class Pawn_FilthTracker_TryPickupFilth_TestPatch
	{
		static void Gain(Pawn_FilthTracker self, Filth filth)
		{
			if (filth != null)
				Log.Warning($"Gained {filth} on {self.pawn} at {self.pawn.Position}");
		}

		static void GainFilth(Pawn_FilthTracker self, ThingDef filthDef)
		{
			self.GainFilth(filthDef);
			var newFilth = self.carriedFilth.LastOrDefault();
			Gain(self, newFilth);
		}

		static void GainFilth(Pawn_FilthTracker self, ThingDef filthDef, IEnumerable<string> sources)
		{
			self.GainFilth(filthDef, sources);
			var newFilth = self.carriedFilth.LastOrDefault();
			Gain(self, newFilth);
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var from1 = SymbolExtensions.GetMethodInfo((Pawn_FilthTracker tracker) => tracker.GainFilth(default));
			var to1 = SymbolExtensions.GetMethodInfo(() => GainFilth(default, default));

			var from2 = SymbolExtensions.GetMethodInfo((Pawn_FilthTracker tracker) => tracker.GainFilth(default, default));
			var to2 = SymbolExtensions.GetMethodInfo(() => GainFilth(default, default, default));

			return instructions.MethodReplacer(from1, to1).MethodReplacer(from2, to2);
		}
	}

	[HarmonyPatch(typeof(Pawn_FilthTracker), nameof(Pawn_FilthTracker.DropCarriedFilth))]
	static class Pawn_FilthTracker_DropCarriedFilth_TestPatch
	{
		static void Prefix(Filth f) => FilthMaker_TryMakeFilth_TestPatch.filthSource = f;
		static void Postfix() => FilthMaker_TryMakeFilth_TestPatch.filthSource = null;
	}
	[HarmonyPatch(typeof(GenLeaving), nameof(GenLeaving.DoLeavingsFor))]
	[HarmonyPatch(new[] { typeof(Thing), typeof(Map), typeof(DestroyMode), typeof(CellRect), typeof(Predicate<IntVec3>), typeof(List<Thing>) })]
	static class GenLeaving_DoLeavingsFor_TestPatch
	{
		static void Prefix(Thing diedThing, ref List<Thing> listOfLeavingsOut)
		{
			listOfLeavingsOut ??= new List<Thing>();
			FilthMaker_TryMakeFilth_TestPatch.filthSource = diedThing;
		}
		static void Postfix(Thing diedThing, List<Thing> listOfLeavingsOut)
		{
			FilthMaker_TryMakeFilth_TestPatch.filthSource = null;
			if (listOfLeavingsOut.Any())
				Log.Warning($"Produce {listOfLeavingsOut.Join(t => $"{t}")} from {diedThing}");
		}
	}
	[HarmonyPatch(typeof(GenLeaving), nameof(GenLeaving.DropFilthDueToDamage))]
	static class Pawn_FilthTracker_DropFilthDueToDamage_TestPatch
	{
		static void Prefix(Thing t) => FilthMaker_TryMakeFilth_TestPatch.filthSource = t;
		static void Postfix() => FilthMaker_TryMakeFilth_TestPatch.filthSource = null;
	}
	[HarmonyPatch]
	static class JobDriver_Vomit_MakeNewToils_TestPatch
	{
		static readonly MethodInfo m_TryMakeFilth = SymbolExtensions.GetMethodInfo(() => FilthMaker.TryMakeFilth(IntVec3.Invalid, default, ThingDefOf.Filth_Vomit, "", 0, FilthSourceFlags.Any));
		static bool TryMakeFilth(IntVec3 c, Map map, ThingDef filthDef, string source, int count, FilthSourceFlags additionalFlags, JobDriver_Vomit jobDriver)
		{
			FilthMaker_TryMakeFilth_TestPatch.filthSource = jobDriver.pawn;
			var result = FilthMaker.TryMakeFilth(c, map, filthDef, source, count, additionalFlags);
			FilthMaker_TryMakeFilth_TestPatch.filthSource = null;
			return result;
		}

		static MethodBase TargetMethod()
		{
			return AccessTools.FirstMethod(typeof(JobDriver_Vomit), method =>
				 PatchProcessor.ReadMethodBody(method).Any(pair => pair.Value is MethodInfo method && method == m_TryMakeFilth));
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var from = m_TryMakeFilth;
			var to = SymbolExtensions.GetMethodInfo(() => TryMakeFilth(default, default, default, default, default, default, default));

			return new CodeMatcher(instructions)
			.MatchStartForward(new CodeMatch(operand: from))
				 .ThrowIfInvalid($"Cannot find {from.FullDescription()}")
				 .InsertAndAdvance(Ldarg_0)
				 .SetInstruction(Call[to])
				 .InstructionEnumeration();
		}
	}
	[HarmonyPatch(typeof(FilthMaker), nameof(FilthMaker.TryMakeFilth))]
	[HarmonyPatch(new[] { typeof(IntVec3), typeof(Map), typeof(ThingDef), typeof(IEnumerable<string>), typeof(bool), typeof(FilthSourceFlags) })]
	static class FilthMaker_TryMakeFilth_TestPatch
	{
		public static Thing filthSource = null;

		static void AddSources(Filth self, IEnumerable<string> sources)
		{
			if (filthSource != null)
				Log.Warning($"Dropping {self} from {filthSource}");
			self.AddSources(sources);
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var from = SymbolExtensions.GetMethodInfo((Filth filth) => filth.AddSources(default));
			var to = SymbolExtensions.GetMethodInfo(() => AddSources(default, default));
			return instructions.MethodReplacer(from, to);
		}
	}

	[HarmonyPatch(typeof(Building_NutrientPasteDispenser), nameof(Building_NutrientPasteDispenser.TryDispenseFood))]
	static class Building_NutrientPasteDispenser_TryDispenseFood_TestPatch
	{
		static Thing AddToThingList(Thing thing, List<Thing> things)
		{
			things.Add(thing);
			return thing;
		}

		static Thing MakeThing(ThingDef def, ThingDef stuff, List<Thing> things)
		{
			var result = ThingMaker.MakeThing(def, stuff);
			Log.Warning($"Produce {result} from [{things.Join(t => $"{t}")}]");
			return result;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
		{
			var thingList = generator.DeclareLocal(typeof(List<Thing>));
			var thingListConstructor = AccessTools.DeclaredConstructor(thingList.LocalType, Array.Empty<Type>());

			var m_SplitOff = SymbolExtensions.GetMethodInfo((Thing thing) => thing.SplitOff(0));
			var m_AddToThingList = SymbolExtensions.GetMethodInfo(() => AddToThingList(default, default));

			var from2 = SymbolExtensions.GetMethodInfo(() => ThingMaker.MakeThing(default, default));
			var to2 = SymbolExtensions.GetMethodInfo(() => MakeThing(default, default, default));

			return new CodeMatcher(instructions)
				 .MatchStartForward(Newobj)
				 .Insert(Newobj[thingListConstructor], Stloc[thingList])
				 .MatchStartForward(new CodeMatch(operand: m_SplitOff))
				 .Advance(1)
				 .Insert(Ldloc[thingList], Call[m_AddToThingList])
				 .MatchStartForward(new CodeMatch(operand: from2))
				 .ThrowIfInvalid($"Cannot find {from2.FullDescription()}")
				 .InsertAndAdvance(Ldloc[thingList])
				 .SetInstruction(Call[to2])
				 .InstructionEnumeration();
		}
	}

	[HarmonyPatch]
	static class ThingComp_TestPatches
	{
		static IEnumerable<MethodBase> TargetMethods()
		{
			yield return SymbolExtensions.GetMethodInfo((CompChangeableProjectile comp) => comp.RemoveShell());
			yield return SymbolExtensions.GetMethodInfo((CompDeepDrill comp) => comp.TryProducePortion(0f, null));
			yield return SymbolExtensions.GetMethodInfo((CompEggLayer comp) => comp.ProduceEgg());
			yield return SymbolExtensions.GetMethodInfo((CompHasGatherableBodyResource comp) => comp.Gathered(default));
			yield return AccessTools.Method(typeof(CompMechCarrier), nameof(CompMechCarrier.PostSpawnSetup));
			yield return SymbolExtensions.GetMethodInfo((CompPlantable comp) => comp.DoPlant(default, default, default));
			yield return SymbolExtensions.GetMethodInfo((CompPollutionPump comp) => comp.Pump());
			yield return AccessTools.Method(typeof(CompRefuelable), nameof(CompRefuelable.PostDestroy));
			yield return SymbolExtensions.GetMethodInfo((CompSpawnerItems comp) => comp.SpawnItems());
			yield return SymbolExtensions.GetMethodInfo((CompSpawner comp) => comp.TryDoSpawn());
			yield return AccessTools.Method(typeof(CompTreeConnection), nameof(CompTreeConnection.CompTick));
			yield return SymbolExtensions.GetMethodInfo((CompWasteProducer comp) => comp.ProduceWaste(0));
		}

		static Thing MakeThing(ThingDef def, ThingDef stuff, ThingComp thingComp)
		{
			var result = ThingMaker.MakeThing(def, stuff);
			Log.Warning($"Produce {result} from {thingComp.parent}");
			return result;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> Tools.MakeThingTranspiler(instructions, () => MakeThing(default, default, default));
	}

	[HarmonyPatch(typeof(Building_SubcoreScanner), nameof(Building_SubcoreScanner.Tick))]
	static class Building_SubcoreScanner_Tick_TestPatch
	{
		static Thing MakeThing(ThingDef def, ThingDef stuff, Building_SubcoreScanner scanner)
		{
			var result = ThingMaker.MakeThing(def, stuff);
			Log.Warning($"Produce {result} from {scanner}");
			return result;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> Tools.MakeThingTranspiler(instructions, () => MakeThing(default, default, default));
	}

	[HarmonyPatch(typeof(Building_GeneExtractor), nameof(Building_GeneExtractor.Finish))]
	static class Building_GeneExtractor_Finish_TestPatch
	{
		static Thing MakeThing(ThingDef def, ThingDef stuff, Building_GeneExtractor extractor)
		{
			var result = ThingMaker.MakeThing(def, stuff);
			Log.Warning($"Produce {result} from {extractor.ContainedPawn}");
			return result;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> Tools.MakeThingTranspiler(instructions, () => MakeThing(default, default, default));
	}

	[HarmonyPatch]
	static class Building_TestPatches
	{
		static IEnumerable<MethodBase> TargetMethods()
		{
			yield return SymbolExtensions.GetMethodInfo((Building_GeneAssembler building) => building.Finish());
			yield return SymbolExtensions.GetMethodInfo((Building_FermentingBarrel building) => building.TakeOutBeer());
		}

		static Thing MakeThing(ThingDef def, ThingDef stuff, Building building)
		{
			var result = ThingMaker.MakeThing(def, stuff);
			Log.Warning($"Produce {result} from {building}");
			return result;
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
			=> Tools.MakeThingTranspiler(instructions, () => MakeThing(default, default, default));
	}
}