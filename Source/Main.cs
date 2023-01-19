using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
		public static DamageDef ZombieBite;
		public static DamageDef SuicideBomb;
		public static DamageDef ToxicSplatter;
		public static DamageDef ElectricalShock;
		public static SoundDef Bzzt;
		public static SoundDef ElectricShock;
		public static SoundDef Hacking;
		public static SoundDef Scream;
		public static SoundDef TankyTink;
		public static SoundDef Smash;
		public static SoundDef ToxicSplash;
		public static SoundDef ZombieDigOut;
		public static SoundDef ZombieEating;
		public static SoundDef ZombieElectricHum;
		public static SoundDef ZombieHit;
		public static SoundDef ZombieRage;
		public static SoundDef ZombieTracking;
		public static SoundDef ZombiesClosingIn;
		public static SoundDef ZombiesRising;
		public static SoundDef TarSmokePop;
		public static SoundDef ShockingRoom;
		public static SoundDef ShockingZombie;
		public static SoundDef WallPushing;
		public static SoundDef ChainsawIdle;
		public static SoundDef ChainsawStart;
		public static SoundDef ChainsawWork;
		public static SoundDef Crush;
		public static JobDef Stumble;
		public static JobDef Sabotage;
		public static JobDef ExtractZombieSerum;
		public static JobDef DoubleTap;
		public static JobDef RopeZombie;
		public static JobDef ZapZombies;
		public static RecipeDef CureZombieInfection;
		public static ThingDef Zombie;
		public static ThingDef ZombieExtract;
		public static ThingDef Corpse_Zombie;
		public static ThingDef TarSlime;
		public static ThingDef TarSmoke;
		public static ThingDef StickyGoo;
		public static ThingDef ZombieShocker;
		public static ThingDef ZombieThought;
		public static ThingDef ZombieZapA;
		public static ThingDef ZombieZapB;
		public static ThingDef ZombieZapC;
		public static ThingDef ZombieZapD;
		public static ThingDef ElectricalField;
		public static ThingDef Apparel_BombVest;
		public static ThingDef Mote_Block;
		public static HediffDef ZombieInfection;
		public static LetterDef ColonistTurnedZombie;
		public static LetterDef OtherTurnedZombie;
		public static LetterDef DangerousSituation;
		public static EffecterDef ZombieShockerRoom;
		public static ThingDef BumpLarge;
		public static ThingDef BumpMedium;
		public static ThingDef BumpSmall;
		public static ThingDef Chainsaw;
	}

	public class ZombielandMod : Mod
	{
		public static string Identifier = "";
		public static bool IsLoadingDefaults = true;
		public static Stopwatch frameWatch = Stopwatch.StartNew();

		public ZombielandMod(ModContentPack content) : base(content)
		{
			Identifier = content.PackageId;
			LongEventHandler.ExecuteWhenFinished(() =>
			{
				_ = GetSettings<ZombieSettingsDefaults>();
				IsLoadingDefaults = false;
			});
		}

		static void DrawJumpToCurrent(Rect rect, float offset)
		{
			var txt = "JumpToNow".Translate();
			var size = Text.CalcSize(txt);
			var r = new Rect(rect.width - 20f - size.x - offset, 0f, size.x + 12, size.y + 8);
			if (Widgets.ButtonText(r, txt))
				DialogTimeHeader.JumpToCurrent();
		}

		static float DrawAdvancedSettings(Rect rect)
		{
			var txt = "AdvancedSettings".Translate();
			var size = Text.CalcSize(txt);
			var r = new Rect(rect.width - 20f - size.x, 0f, size.x + 12, size.y + 8);
			if (Widgets.ButtonText(r, txt))
				Find.WindowStack.Add(new Dialog_AdvancedSettings());
			return size.x;
		}

		public override void DoSettingsWindowContents(Rect inRect)
		{
			var settings = ZombieSettings.GetGameSettings();
			if (settings != null)
			{
				var offset = DrawAdvancedSettings(inRect);
				DrawJumpToCurrent(inRect, offset + 20f);
				settings.DoWindowContents(inRect);
			}
			else
			{
				_ = DrawAdvancedSettings(inRect);
				ZombieSettingsDefaults.DoWindowContents(inRect);
			}
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
		public static void DebugRimworldMethodCalls(Func<Type, bool> typeFilter)
		{
			var harmony = new Harmony("net.pardeike.zombieland.debug");
			var transpiler = new HarmonyMethod(AccessTools.Method(typeof(ZombielandMod), "MethodCallsTranspiler"));
			var patches = new HashSet<MethodBase>();

			var asm = Assembly.GetAssembly(typeof(Job));
			var types = asm.GetTypes();
			Array.Sort(types, (a, b) => { return string.Compare(a.FullName, b.FullName, StringComparison.Ordinal); });
			for (var i = 0; i < types.Length; i++)
			{
				var type = types[i];
				if (typeFilter(type) == false)
					continue;
				var methods = type.GetMethods(AccessTools.all);
				Array.Sort(methods, (a, b) => { return string.Compare(a.Name, b.Name, StringComparison.Ordinal); });
				for (var j = 0; j < methods.Length; j++)
				{
					var method = methods[j];
					if (method.IsAbstract)
						continue;
					if (method.DeclaringType != type)
						continue;
					if (patches.Contains(method))
						continue;
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
