using HarmonyLib;
using RimWorld;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	[StaticConstructorOnStartup]
	public class RimConnectSupport
	{
		static readonly Dictionary<string, Func<int, string, (string, IntVec3)>> actions = new Dictionary<string, Func<int, string, (string, IntVec3)>>();

		static RimConnectSupport()
		{
			var tActionList = AccessTools.TypeByName("RimConnection.ActionList");
			if (tActionList == null) return;

			var tAction = AccessTools.TypeByName("RimConnection.Action");
			if (tAction == null) return;
			var mExecute = AccessTools.Method(tAction, "Execute");
			if (mExecute == null) return;
			var mBadEventNotification = AccessTools.Method("RimConnection.AlertManager:BadEventNotification", new[] { typeof(string), typeof(IntVec3) });
			if (mBadEventNotification == null) return;

			var harmony = new Harmony("net.pardeike.zombieland.rimconnect");
			var postfix = new HarmonyMethod(AccessTools.Method(typeof(RimConnectSupport), nameof(Postfix)));
			var mGenerateActionList = AccessTools.Method(tActionList, "GenerateActionList");
			_ = harmony.Patch(mGenerateActionList, postfix: postfix);
		}

		static void Postfix(ref IList __result)
		{
			var cat = "Zombies";
			_ = __result.Add(CreateActionClass("RandomZombieAction", "Random Zombie Event", "Creates some normal zombies", cat, (amount, boughtBy) => SpawnZombies(amount, boughtBy, ZombieType.Random)));
			_ = __result.Add(CreateActionClass("SuicideZombieAction", "Suicide Zombie Event", "Creates some suicide bomber zombies", cat, (amount, boughtBy) => SpawnZombies(amount, boughtBy, ZombieType.SuicideBomber)));
			_ = __result.Add(CreateActionClass("ToxicZombieAction", "Toxic Zombie Event", "Creates some toxic goo zombies", cat, (amount, boughtBy) => SpawnZombies(amount, boughtBy, ZombieType.ToxicSplasher)));
			_ = __result.Add(CreateActionClass("TankZombieAction", "Tank Zombie Event", "Creates some heavy tank zombies", cat, (amount, boughtBy) => SpawnZombies(amount, boughtBy, ZombieType.TankyOperator)));
			_ = __result.Add(CreateActionClass("MinerZombieAction", "Miner Zombie Event", "Creates some mining zombies", cat, (amount, boughtBy) => SpawnZombies(amount, boughtBy, ZombieType.Miner)));
			_ = __result.Add(CreateActionClass("ElectricZombieAction", "Electric Zombie Event", "Creates some electrical zombies", cat, (amount, boughtBy) => SpawnZombies(amount, boughtBy, ZombieType.Electrifier)));
			_ = __result.Add(CreateActionClass("AlbinoZombieAction", "Albino Zombie Event", "Creates some albino zombies", cat, (amount, boughtBy) => SpawnZombies(amount, boughtBy, ZombieType.Albino)));
			_ = __result.Add(CreateActionClass("DarkZombieAction", "Dark Zombie Event", "Creates some dark slimer zombies", cat, (amount, boughtBy) => SpawnZombies(amount, boughtBy, ZombieType.DarkSlimer)));
			_ = __result.Add(CreateActionClass("HealerZombieAction", "Healer Zombie Event", "Creates some healer zombies", cat, (amount, boughtBy) => SpawnZombies(amount, boughtBy, ZombieType.Healer)));
			_ = __result.Add(CreateActionClass("NormalZombieAction", "Normal Zombie Event", "Creates some normal zombies", cat, (amount, boughtBy) => SpawnZombies(amount, boughtBy, ZombieType.Normal)));
			_ = __result.Add(CreateActionClass("KillAllZombies", "Kill All Zombies", "Instantly kills all zombies on the map", cat, (amount, boughtBy) => KillAllZombies(boughtBy)));
			_ = __result.Add(CreateActionClass("AllZombiesRage", "Zombies Rage Event", "Makes all zombies rage", cat, (amount, boughtBy) => AllZombiesRage(boughtBy)));
			_ = __result.Add(CreateActionClass("SuperZombieDropRaid", "Super Zombie Drop", "Creates a drop raid with super zombies", cat, (amount, boughtBy) => SuperZombieDropRaid(amount, boughtBy)));
		}

		public static (string, IntVec3) SpawnZombies(int amount, string boughtBy, ZombieType type)
		{
			var map = Find.CurrentMap;
			if (map == null || map.AllowsZombies()) return (null, IntVec3.Invalid);
			var tickManager = map.GetComponent<TickManager>();
			if (tickManager == null) return (null, IntVec3.Invalid);
			var available = Mathf.Max(0, ZombieSettings.Values.maximumNumberOfZombies - tickManager.ZombieCount());
			amount = Mathf.Min(available, amount);
			if (amount == 0) return (null, IntVec3.Invalid);

			var cellValidator = Tools.ZombieSpawnLocator(map, true);
			var spot = ZombiesRising.GetValidSpot(map, IntVec3.Invalid, cellValidator);
			tickManager.rimConnectActions.Enqueue(map => ZombiesRising.TryExecute(map, amount, spot, false, true, type));
			return ($"{boughtBy} created an event with {amount} {type.ToString().ToLower()} zombies", spot);
		}

		public static (string, IntVec3) KillAllZombies(string boughtBy)
		{
			var map = Find.CurrentMap;
			if (map == null) return (null, IntVec3.Invalid);
			var tickManager = map.GetComponent<TickManager>();
			if (tickManager == null) return (null, IntVec3.Invalid);
			tickManager.allZombiesCached.Do(zombie =>
			{
				for (int i = 0; i < 1000; i++)
				{
					var dinfo = new DamageInfo(DamageDefOf.Crush, 100f, 100f, -1f, null, null, null, DamageInfo.SourceCategory.ThingOrUnknown, null, true, true);
					dinfo.SetIgnoreInstantKillProtection(true);
					_ = zombie.TakeDamage(dinfo);
					if (zombie.Destroyed)
						break;
				}
			});
			return ($"{boughtBy} killed all zombies on the map", IntVec3.Invalid);
		}

		public static (string, IntVec3) AllZombiesRage(string boughtBy)
		{
			var map = Find.CurrentMap;
			if (map == null) return (null, IntVec3.Invalid);
			var tickManager = map.GetComponent<TickManager>();
			if (tickManager == null) return (null, IntVec3.Invalid);
			tickManager.allZombiesCached.Do(zombie => ZombieStateHandler.StartRage(zombie));
			return ($"{boughtBy} made all zombies on the map rage", IntVec3.Invalid);
		}

		public static (string, IntVec3) SuperZombieDropRaid(int amount, string boughtBy)
		{
			var map = Find.CurrentMap;
			if (map == null) return (null, IntVec3.Invalid);
			var tickManager = map.GetComponent<TickManager>();
			if (tickManager == null) return (null, IntVec3.Invalid);
			var available = Mathf.Max(0, ZombieSettings.Values.maximumNumberOfZombies - tickManager.ZombieCount());
			amount = Mathf.Min(available, amount);
			if (amount == 0) return (null, IntVec3.Invalid);

			if (DropCellFinder.TryFindRaidDropCenterClose(out var spot, map) == false) return (null, IntVec3.Invalid);

			var zombies = new List<Zombie>();
			for (var i = 1; i <= amount; i++)
			{
				var enumerator = ZombieGenerator.SpawnZombieIterativ(IntVec3.Invalid, map, ZombieType.Normal, zombie =>
				{
					zombie.rubbleCounter = Constants.RUBBLE_AMOUNT;
					zombie.state = ZombieState.Wandering;
					PawnComponentsUtility.AddComponentsForSpawn(zombie);
					var job = JobMaker.MakeJob(CustomDefs.Stumble, zombie);
					zombie.jobs.StartJob(job);

					zombies.Add(zombie);
				});
				while (enumerator.MoveNext()) ;
			}

			DropPodUtility.DropThingsNear(spot, map, zombies, 0, false, false, true, false);

			return ($"{boughtBy} created an drop raid with {amount} super zombies", spot);
		}

		public static object CreateActionClass(string className, string name, string description, string category, Func<int, string, (string, IntVec3)> action)
		{
			var tAction = AccessTools.TypeByName("RimConnection.Action");
			var mExecute = AccessTools.Method(tAction, "Execute");
			var fValueTuple_Item1 = AccessTools.Field(typeof(ValueTuple<System.String, Verse.IntVec3>), "Item1");
			var fValueTuple_Item2 = AccessTools.Field(typeof(ValueTuple<System.String, Verse.IntVec3>), "Item2");

			var assemblyName = new AssemblyName("RimConnectSupport");
			var assemblyBuilder = AppDomain.CurrentDomain.DefineDynamicAssembly(assemblyName, AssemblyBuilderAccess.Run);
			var moduleBuilder = assemblyBuilder.DefineDynamicModule("DefaultModule");
			var attr1 = TypeAttributes.Public | TypeAttributes.Class | TypeAttributes.AutoClass | TypeAttributes.AnsiClass | TypeAttributes.ExplicitLayout;
			var typeBuilder = moduleBuilder.DefineType(className, attr1, tAction);

			var constructorBuilder = typeBuilder.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, new Type[0]);
			var gen1 = constructorBuilder.GetILGenerator();
			gen1.Emit(OpCodes.Ldarg_0);
			gen1.Emit(OpCodes.Call, AccessTools.DeclaredConstructor(tAction, new Type[0]));
			gen1.Emit(OpCodes.Ldarg_0);
			gen1.Emit(OpCodes.Ldstr, name);
			gen1.Emit(OpCodes.Call, AccessTools.PropertySetter(tAction, "Name"));
			gen1.Emit(OpCodes.Ldarg_0);
			gen1.Emit(OpCodes.Ldstr, description);
			gen1.Emit(OpCodes.Call, AccessTools.PropertySetter(tAction, "Description"));
			gen1.Emit(OpCodes.Ldarg_0);
			gen1.Emit(OpCodes.Ldstr, category);
			gen1.Emit(OpCodes.Call, AccessTools.PropertySetter(tAction, "Category"));
			gen1.Emit(OpCodes.Ret);

			var attr2 = MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual | MethodAttributes.Final;
			var methodBuilder = typeBuilder.DefineMethod("Execute", attr2, CallingConventions.HasThis, typeof(void), new[] { typeof(int), typeof(string) });
			var gen2 = methodBuilder.GetILGenerator();
			gen2.Emit(OpCodes.Ldstr, name);
			gen2.Emit(OpCodes.Ldarg_1);
			gen2.Emit(OpCodes.Ldarg_2);
			gen2.Emit(OpCodes.Call, AccessTools.Method(typeof(RimConnectSupport), nameof(RimConnectSupport.Execute)));
			gen2.Emit(OpCodes.Call, SymbolExtensions.GetMethodInfo(() => BadEventNotification(default)));
			gen2.Emit(OpCodes.Ret);
			typeBuilder.DefineMethodOverride(methodBuilder, mExecute);

			actions[name] = action;
			return Activator.CreateInstance(typeBuilder.CreateType());
		}

		static readonly MethodInfo mBadEventNotification = AccessTools.Method("RimConnection.AlertManager:BadEventNotification", new[] { typeof(string), typeof(IntVec3) });
		public static void BadEventNotification(ValueTuple<string, IntVec3> tuple)
		{
			if (tuple.Item1.NullOrEmpty() == false)
				_ = mBadEventNotification.Invoke(null, new object[] { tuple.Item1, tuple.Item2 });
		}

		public static (string, IntVec3) Execute(string name, int amount, string boughtBy)
		{
			if (actions.TryGetValue(name, out var action))
				return action(amount, boughtBy);
			return (null, IntVec3.Invalid);
		}
	}

	[HarmonyPatch]
	class RimConnection_Settings_CommandOptionSettings_Patch
	{
		static bool Prepare()
		{
			return TargetMethod() != null;
		}

		static MethodBase TargetMethod()
		{
			return AccessTools.Method("RimConnection.Settings.CommandOptionSettings:DoWindowContents");
		}

		static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			var list = instructions.ToList();
			for (var i = 0; i < list.Count; i++)
			{
				var code = list[i];
				if (code.opcode == OpCodes.Ldarga_S || code.opcode == OpCodes.Ldarga)
				{
					code = list[i - 1];
					if (code.opcode == OpCodes.Ldc_R4)
					{
						var value = (float)code.operand;
						if (value >= 180)
							code.operand = value + 30f;
					}
				}
			}
			var idx = list.FindLastIndex(code => code.opcode == OpCodes.Sub);
			if (idx > 0 && idx < list.Count)
				list.InsertRange(idx + 1, new[]
				{
					new CodeInstruction(OpCodes.Ldc_R4, 30f),
					new CodeInstruction(OpCodes.Sub)
				});
			return list.AsEnumerable();
		}
	}
}
