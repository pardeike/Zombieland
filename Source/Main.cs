using Harmony;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using Verse;
using Verse.AI;

namespace ZombieLand
{
	[DefOf]
	public static class CustomDefs
	{
		public static DamageDef SuicideBomb;
		public static DamageDef ToxicSplatter;
		public static SoundDef ZombiesClosingIn;
	}

	public class ZombielandMod : Mod
	{
		public static string Identifier = "";

		public ZombielandMod(ModContentPack content) : base(content)
		{
			Identifier = content.Identifier;
			GetSettings<ZombieSettingsDefaults>();

			// HarmonyInstance.DEBUG = true;
			var harmony = HarmonyInstance.Create("net.pardeike.zombieland");
			harmony.PatchAll(Assembly.GetExecutingAssembly());

			// prepare Twinkie
			LongEventHandler.QueueLongEvent(() => { Tools.EnableTwinkie(false); }, "", true, null);

			// extra patch for Combat Extended
			Patches.Projectile_Launch_Patch.PatchCombatExtended(harmony);

			// for debugging
			/*
			DebugRimworldMethodCalls((Type type) =>
			{
				if (type.Name.Contains("AttackTarget")) return true;
				if (type.Name.Contains("_AI")) return true;
				if (type.Name.Contains("Reachability")) return true;
				return false;
			}); */
		}

		public override void DoSettingsWindowContents(Rect inRect)
		{
			var settings = ZombieSettings.GetGameSettings();
			if (settings != null)
				settings.DoWindowContents(inRect);
			else
				ZombieSettingsDefaults.DoWindowContents(inRect);
		}

		public override void WriteSettings()
		{
			base.WriteSettings();
			var settings = ZombieSettings.GetGameSettings();
			if (settings != null)
				settings.WriteSettings();
			else
				ZombieSettingsDefaults.WriteSettings();
		}

		public override string SettingsCategory()
		{
			var world = Find.World;
			if (world != null && world.components != null)
				return "ZombielandGameSettings".Translate();
			return "ZombielandDefaultSettings".Translate();
		}

		// for debugging

		static IEnumerable<CodeInstruction> MethodCallsTranspiler(IEnumerable<CodeInstruction> instructions, MethodBase method)
		{
			yield return new CodeInstruction(OpCodes.Ldstr, method.DeclaringType.FullName + "." + method.Name);
			yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(Log), "Warning"));
			foreach (var instruction in instructions)
				yield return instruction;
		}
		public void DebugRimworldMethodCalls(Func<Type, bool> typeFilter)
		{
			var harmony = HarmonyInstance.Create("net.pardeike.zombieland.debug");
			var transpiler = new HarmonyMethod(AccessTools.Method(typeof(ZombielandMod), "MethodCallsTranspiler"));
			var patches = new HashSet<MethodBase>();

			var asm = Assembly.GetAssembly(typeof(Job));
			var types = asm.GetTypes();
			Array.Sort(types, (a, b) => { return string.Compare(a.FullName, b.FullName, StringComparison.Ordinal); });
			for (var i = 0; i < types.Length; i++)
			{
				var type = types[i];
				if (typeFilter(type) == false) continue;
				var methods = type.GetMethods(AccessTools.all);
				Array.Sort(methods, (a, b) => { return string.Compare(a.Name, b.Name, StringComparison.Ordinal); });
				for (var j = 0; j < methods.Length; j++)
				{
					var method = methods[j];
					if (method.IsAbstract) continue;
					if (method.DeclaringType != type) continue;
					if (patches.Contains(method)) continue;
					patches.Add(method);
					try
					{
						harmony.Patch(method, null, null, transpiler);
					}
					catch (Exception ex)
					{
						Log.Error("Exception patching " + method + ": " + ex);
					}
				}
			}
		}
	}
}