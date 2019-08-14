using Harmony;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;
using Verse;
using Verse.AI;

/*
Cannot play ZombieHit (subSound UnnamedSubSoundDef_: No resolved grains.
Verse.Log:Error(String, Boolean)
Verse.Sound.SubSoundDef:TryPlay(SoundInfo)
Verse.Sound.SoundStarter:PlayOneShot(SoundDef, SoundInfo)
ZombieLand.ZombieStateHandler:Smash(JobDriver_Stumble, Zombie, Boolean, Boolean)
ZombieLand.JobDriver_Stumble:TickAction()
Verse.AI.JobDriver:DriverTick()
Verse.AI.Pawn_JobTracker:JobTrackerTick()
ZombieLand.Zombie:CustomTick()
ZombieLand.<ZombieTicking>d__20:MoveNext()
ZombieLand.Verse_TickManager_TickManagerUpdate_Patch:SingleTick(TickManager, Int32)
Verse.TickManager:TickManagerUpdate_Patch1(Object)
Verse.Game:UpdatePlay()
Verse.Root_Play:Update()
*/

namespace ZombieLand
{
	[DefOf]
	public static class CustomDefs
	{
		public static DamageDef SuicideBomb;
		public static DamageDef ToxicSplatter;
		public static SoundDef ZombiesClosingIn;
		public static SoundDef ZombieElectricHum;
		public static SoundDef ElectricShock;
	}

	public class ZombielandMod : Mod
	{
		public static string Identifier = "";

		public ZombielandMod(ModContentPack content) : base(content)
		{
			Identifier = content.Identifier;
			_ = GetSettings<ZombieSettingsDefaults>();
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
					_ = patches.Add(method);
					try
					{
						_ = harmony.Patch(method, null, null, transpiler);
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