using HarmonyLib;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public enum SpawnWhenType
	{
		AllTheTime,
		WhenDark,
		InEventsOnly
	}

	public enum SpawnHowType
	{
		AllOverTheMap,
		FromTheEdges
	}

	public enum AttackMode
	{
		Everything,
		OnlyHumans,
		OnlyColonists
	}

	public enum SmashMode
	{
		Nothing,
		DoorsOnly,
		AnyBuilding
	}

	public enum ZombieInstinct
	{
		Dull,
		Normal,
		Sensitive
	}

	public enum WanderingStyle
	{
		Off,
		Simple,
		Smart
	}

	public enum ZombieRiskMode : byte
	{
		Ignore,
		IfInside,
		IfOutside
	}

	internal class NoteDialog : Dialog_MessageBox
	{
		internal NoteDialog(string text, string buttonAText = null, Action buttonAAction = null, string buttonBText = null, Action buttonBAction = null, string title = null, bool buttonADestructive = false, Action acceptAction = null, Action cancelAction = null)
			: base(text, buttonAText, buttonAAction, buttonBText, buttonBAction, title, buttonADestructive, acceptAction, cancelAction) { }

		public override Vector2 InitialSize => new Vector2(320, 240);
	}

	public class ZombieRiskArea : IExposable
	{
		public int area;
		public int map;
		public ZombieRiskMode mode;

		public static List<ZombieRiskArea> temp = new List<ZombieRiskArea>();

		public void ExposeData()
		{
			Scribe_Values.Look(ref area, nameof(area));
			Scribe_Values.Look(ref map, nameof(map));
			Scribe_Values.Look(ref mode, nameof(mode));
		}
	}

	public class SettingsGroup : IExposable, ICloneable
	{
		public SpawnWhenType spawnWhenType = SpawnWhenType.AllTheTime;
		public SpawnHowType spawnHowType = SpawnHowType.FromTheEdges;
		public AttackMode attackMode = AttackMode.OnlyHumans;
		public bool enemiesAttackZombies = true;
		public bool animalsAttackZombies = false;
		public SmashMode smashMode = SmashMode.DoorsOnly;
		public bool smashOnlyWhenAgitated = true;
		public bool doubleTapRequired = true;
		public bool zombiesDieVeryEasily;
		public int daysBeforeZombiesCome = 3;
		public int maximumNumberOfZombies = 500;
		public bool useDynamicThreatLevel = true;
		public bool zombiesDieOnZeroThreat = true;
		public float dynamicThreatSmoothness = 2.5f;
		public float dynamicThreatStretch = 20f;
		public float infectedRaidsChance = 0.1f;
		public float colonyMultiplier = 1f;
		public int baseNumberOfZombiesinEvent = 20;
		internal int extraDaysBetweenEvents = 0;
		public float suicideBomberChance = 0.01f;
		public float toxicSplasherChance = 0.01f;
		public float tankyOperatorChance = 0.01f;
		public float minerChance = 0.01f;
		public float electrifierChance = 0.01f;
		public float albinoChance = 0.01f;
		public float darkSlimerChance = 0.01f;
		public float healerChance = 0.01f;
		public float moveSpeedIdle = 0.15f;
		public float moveSpeedTracking = 0.6f;
		public bool moveSpeedUpgraded = false;
		public float damageFactor = 1.0f;
		public ZombieInstinct zombieInstinct = ZombieInstinct.Normal;
		public bool useCustomTextures = true;
		public bool playCreepyAmbientSound = true;
		public bool zombiesEatDowned = true;
		public bool zombiesEatCorpses = true;
		public float zombieBiteInfectionChance = 0.5f;
		public int hoursInfectionIsUnknown = 8;
		public int hoursInfectionIsTreatable = 24;
		public int hoursInfectionPersists = 6 * 24;
		public bool anyTreatmentStopsInfection;
		public int hoursAfterDeathToBecomeZombie = 8;
		public bool deadBecomesZombieMessage = true;
		public float corpsesExtractAmount = 1f;
		public float lootExtractAmount = 0.1f;
		public string extractZombieArea = "";
		public int corpsesHoursToDessicated = 1;
		public bool betterZombieAvoidance = true;
		public bool ragingZombies = true;
		public int zombieRageLevel = 3;
		public bool replaceTwinkie = true;
		public bool zombiesDropBlood = true;
		public bool zombiesBurnLonger = true;
		public float reducedTurretConsumption = 0f;
		public bool zombiesCauseManhuntingResponse = true;
		public int safeMeleeLimit = 1;
		public WanderingStyle wanderingStyle = WanderingStyle.Smart;
		public bool showHealthBar = true;
		public HashSet<string> biomesWithoutZombies = new HashSet<string>();
		public bool showZombieStats = true;
		public Dictionary<Area, ZombieRiskMode> dangerousAreas = new Dictionary<Area, ZombieRiskMode>();

		// unused
		public int suicideBomberIntChance = 1;
		public int toxicSplasherIntChance = 1;
		public int tankyOperatorIntChance = 1;
		public int minerIntChance = 1;
		public int electrifierIntChance = 1;

		public object Clone()
		{
			return MemberwiseClone();
		}

		public void ExposeData()
		{
			// no base.ExposeData() to call

			this.AutoExposeDataWithDefaults((settings, name, value, defaultValue) =>
			{
				const string fieldName = nameof(dangerousAreas);
				if (name != fieldName)
					return false;

				var dict = (Dictionary<Area, ZombieRiskMode>)(value ?? defaultValue);
				if (Scribe.mode == LoadSaveMode.Saving)
				{
					if (Scribe.EnterNode(fieldName))
					{
						foreach (var (area, mode) in dict)
						{
							var riskArea = new ZombieRiskArea() { area = area.ID, map = area.Map.uniqueID, mode = mode };
							Scribe_Deep.Look(ref riskArea, "area", Array.Empty<object>());
						}
						Scribe.ExitNode();
					}
				}
				if (Scribe.mode == LoadSaveMode.LoadingVars)
					Scribe_Collections.Look(ref ZombieRiskArea.temp, fieldName, LookMode.Deep);
				if (Scribe.mode == LoadSaveMode.PostLoadInit)
				{
					foreach (var riskArea in ZombieRiskArea.temp)
					{
						var realArea = Find.Maps
								.Where(map => map.uniqueID == riskArea.map)
								.SelectMany(map => map.areaManager.AllAreas)
								.FirstOrDefault(area => area.ID == riskArea.area);
						if (realArea != null)
							dict[realArea] = riskArea.mode;
					}
					settings.dangerousAreas = dict;
				}
				return true;
			});

			if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
				var chanceDirty = false;

				if (suicideBomberChance < 0 || suicideBomberChance > 1)
				{
					suicideBomberChance = 0.01f;
					chanceDirty = true;
				}
				if (toxicSplasherChance < 0 || toxicSplasherChance > 1)
				{
					toxicSplasherChance = 0.01f;
					chanceDirty = true;
				}
				if (tankyOperatorChance < 0 || tankyOperatorChance > 1)
				{
					tankyOperatorChance = 0.01f;
					chanceDirty = true;
				}
				if (minerChance < 0 || minerChance > 1)
				{
					minerChance = 0.01f;
					chanceDirty = true;
				}
				if (electrifierChance < 0 || electrifierChance > 1)
				{
					electrifierChance = 0.01f;
					chanceDirty = true;
				}
				if (albinoChance < 0 || albinoChance > 1)
				{
					albinoChance = 0.01f;
					chanceDirty = true;
				}
				if (darkSlimerChance < 0 || darkSlimerChance > 1)
				{
					darkSlimerChance = 0.01f;
					chanceDirty = true;
				}
				if (healerChance < 0 || healerChance > 1)
				{
					healerChance = 0.01f;
					chanceDirty = true;
				}

				if (suicideBomberChance
					+ toxicSplasherChance
					+ tankyOperatorChance
					+ minerChance
					+ electrifierChance
					+ albinoChance
					+ darkSlimerChance
					+ healerChance > 1) chanceDirty = true;

				if (ZombielandMod.IsLoadingDefaults == false)
				{
					if (moveSpeedUpgraded == false && (moveSpeedIdle > 1 || moveSpeedTracking > 1))
						LongEventHandler.QueueLongEvent(() =>
						{
							var note = "Zombieland Mod\n\nZombie speed has been normalized to be relative to human speed. Your move speed settings are quite high (> 1), make sure you adjust them.";
							Find.WindowStack.Add(new NoteDialog(note));
							moveSpeedUpgraded = true;
						}, "speed-upgrade", true, null);

					if (chanceDirty)
						LongEventHandler.QueueLongEvent(() =>
						{
							var note = "Zombieland Mod\n\nSpecial zombie percentages were reset to their defaults. Please adjust them and save the game.";
							Find.WindowStack.Add(new NoteDialog(note));
						}, "special-zombies", true, null);
				}

				Tools.UpdateBiomeBlacklist(biomesWithoutZombies);
			}
		}

		public void Reset()
		{
			var type = GetType();
			var defaults = Activator.CreateInstance(type);
			AccessTools.GetFieldNames(this).Do(name =>
			{
				var finfo = AccessTools.Field(type, name);
				finfo.SetValue(this, finfo.GetValue(defaults));
			});
			Dialogs.scrollPosition = Vector2.zero;
		}

		public void ToClipboard()
		{
			var hex = Tools.SerializeToHex(this);
			GUIUtility.systemCopyBuffer = $"[{hex}]";
		}

		public void FromClipboard()
		{
			var chars = GUIUtility.systemCopyBuffer.ToLower().ToCharArray();
			var hex = chars.Where(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')).Join(null, "");
			if (hex.NullOrEmpty() == false)
			{
				try
				{
					ZombieSettings.Values = Tools.DeserializeFromHex<SettingsGroup>(hex);
					var world = Find.World;
					if (world != null && world.components != null)
					{
						var settings = world.components.OfType<ZombieSettings>().FirstOrDefault();
						Traverse.CopyFields(new Traverse(ZombieSettings.Values), new Traverse(settings));
					}
				}
				catch (Exception ex)
				{
					Log.Error($"Cannot restore ZombieLand settings from {hex}: {ex}");
				}
			}
		}
	}

	class ZombieSettingsDefaults : ModSettings
	{
		static SettingsGroup group;

		public static SettingsGroup Defaults()
		{
			if (group == null) group = new SettingsGroup();
			return group.Clone() as SettingsGroup;
		}

		public static void DoWindowContents(Rect inRect)
		{
			Dialogs.DoWindowContentsInternal(ref group, inRect);
		}

		public static void WriteSettings()
		{
		}

		public override void ExposeData()
		{
			base.ExposeData();
			if (group == null) group = new SettingsGroup();
			Scribe_Deep.Look(ref group, "defaults", Array.Empty<object>());
		}
	}

	class ZombieSettings : WorldComponent
	{
		public static SettingsGroup Values;

		public ZombieSettings(World world) : base(world)
		{
			Values = ZombieSettingsDefaults.Defaults();
		}

		public static ZombieSettings GetGameSettings()
		{
			ZombieSettings settings = null;
			var world = Find.World;
			if (world != null && world.components != null)
				settings = world.components.OfType<ZombieSettings>().FirstOrDefault();
			return settings;
		}

		public void DoWindowContents(Rect inRect)
		{
			Dialogs.DoWindowContentsInternal(ref Values, inRect);
		}

		public void WriteSettings()
		{
			Tools.EnableTwinkie(Values.replaceTwinkie);
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Deep.Look(ref Values, "values", Array.Empty<object>());
		}
	}
}
