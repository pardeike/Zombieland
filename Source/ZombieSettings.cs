using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
		Random,
		Simple,
		Smart
	}

	public enum AreaRiskMode : byte
	{
		Ignore,
		ColonistInside,
		ColonistOutside,
		ZombieInside,
		ZombieOutside,
	}

	internal class NoteDialog : Dialog_MessageBox
	{
		internal NoteDialog(string text, string buttonAText = null, Action buttonAAction = null, string buttonBText = null, Action buttonBAction = null, string title = null, bool buttonADestructive = false, Action acceptAction = null, Action cancelAction = null)
			: base(text, buttonAText, buttonAAction, buttonBText, buttonBAction, title, buttonADestructive, acceptAction, cancelAction) { }

		public override Vector2 InitialSize => new(320, 240);
	}

	public class ZombieRiskArea : IExposable
	{
		public int area;
		public int map;
		public AreaRiskMode mode;

		public static List<ZombieRiskArea> temp = new();

		public void ExposeData()
		{
			Scribe_Values.Look(ref area, nameof(area));
			Scribe_Values.Look(ref map, nameof(map));
			Scribe_Values.Look(ref mode, nameof(mode));
		}
	}

	public class SettingsKeyFrame : IExposable
	{
		static readonly Dictionary<string, char> firstLetters;
		static SettingsKeyFrame()
		{
			firstLetters = Enum.GetNames(typeof(Unit))
				.Select(u => (u, u.Translate().CapitalizeFirst().ToString()[0]))
				.ToDictionary(pair => pair.u, pair => pair.Item2);
		}

		public enum Unit
		{
			Days,
			Seasons,
			Years
		}

		public int amount = 0;
		public Unit unit = Unit.Days;
		public SettingsGroup values;

		public int Ticks => unit switch
		{
			Unit.Days => amount * GenDate.TicksPerDay,
			Unit.Seasons => amount * GenDate.TicksPerSeason,
			Unit.Years => amount * GenDate.TicksPerYear,
			_ => throw new NotImplementedException()
		};

		public void ExposeData()
		{
			Scribe_Values.Look(ref amount, nameof(amount), 0);
			Scribe_Values.Look(ref unit, nameof(unit), Unit.Days);
			Scribe_Deep.Look(ref values, nameof(values));
		}

		public override string ToString()
		{
			if (amount == 0)
				return "0";
			return $"{amount}{firstLetters[unit.ToString()]}";
		}

		public SettingsKeyFrame Copy() => new()
		{
			amount = amount,
			unit = unit,
			values = values.Clone() as SettingsGroup
		};
	}

	public static class CopyPasteSettings
	{
		public class Holder
		{
			public SettingsKeyFrame[] settings;
		}

		public static void ToClipboard(this List<SettingsKeyFrame> settingsOverTime)
		{
			var holder = new Holder() { settings = settingsOverTime.ToArray() };
			var hex = Tools.SerializeToHex(holder);
			GUIUtility.systemCopyBuffer = $"[{hex}]";
		}

		public static void FromClipboard(this List<SettingsKeyFrame> settingsOverTime)
		{
			var chars = GUIUtility.systemCopyBuffer.ToLower().ToCharArray();
			var hex = chars.Where(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')).Join(null, "");
			if (hex.NullOrEmpty() == false)
			{
				try
				{
					var holder = Tools.DeserializeFromHex<Holder>(hex);
					DialogTimeHeader.Reset();
					settingsOverTime.Clear();
					settingsOverTime.AddRange(holder.settings
						.Select(setting => setting.Copy()));
				}
				catch (Exception ex)
				{
					Log.Error($"Cannot restore ZombieLand settings from {hex}: {ex}");
				}
			}
		}
	}

	public class SettingsGroup : IExposable, ICloneable
	{
		public float threatScale = 1f;
		public SpawnWhenType spawnWhenType = SpawnWhenType.AllTheTime;
		public SpawnHowType spawnHowType = SpawnHowType.FromTheEdges;
		public AttackMode attackMode = AttackMode.OnlyHumans;
		public bool enemiesAttackZombies = true;
		public bool animalsAttackZombies = false;
		public SmashMode smashMode = SmashMode.DoorsOnly;
		public bool smashOnlyWhenAgitated = true;
		public bool doubleTapRequired = true;
		public bool zombiesDieVeryEasily = false;
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
		public float suicideBomberChance = 0.0025f;
		public float toxicSplasherChance = 0.0025f;
		public float tankyOperatorChance = 0.0025f;
		public float minerChance = 0.0025f;
		public float electrifierChance = 0.0025f;
		public float albinoChance = 0.0025f;
		public float darkSlimerChance = 0.0025f;
		public float healerChance = 0.0025f;
		public float moveSpeedIdle = 0.1f;
		public float moveSpeedTracking = 0.5f;
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
		public bool dangerousSituationMessage = true;
		public float corpsesExtractAmount = 1f;
		public float lootExtractAmount = 0.1f;
		public string extractZombieArea = "";
		public int corpsesHoursToDessicated = 2;
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
		public HashSet<string> biomesWithoutZombies = new();
		public bool showZombieStats = true;
		public Dictionary<Area, AreaRiskMode> dangerousAreas = new();
		public bool highlightDangerousAreas = false;
		public bool disableRandomApparel = false;
		public bool floatingZombies = true;
		public float childChance = 0.02f;
		public int minimumZombiesForWallPushing = 24;

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

				var dict = (Dictionary<Area, AreaRiskMode>)(value ?? defaultValue);
				if (Scribe.mode == LoadSaveMode.Saving)
				{
					if (Scribe.EnterNode(fieldName))
					{
						foreach (var (area, mode) in dict)
							if (Find.Maps.Select(map => map.uniqueID).Contains(area.Map.uniqueID))
							{
								var riskArea = new ZombieRiskArea() { area = area.ID, map = area.Map.uniqueID, mode = mode };
								Scribe_Deep.Look(ref riskArea, "area", Array.Empty<ZombieRiskArea>());
							}
						Scribe.ExitNode();
					}
				}
				if (Scribe.mode == LoadSaveMode.LoadingVars)
				{
					Scribe_Collections.Look(ref ZombieRiskArea.temp, fieldName, LookMode.Deep);
					ZombieRiskArea.temp ??= new List<ZombieRiskArea>();
				}
				if (Scribe.mode == LoadSaveMode.PostLoadInit)
				{
					if (Find.Maps != null)
						foreach (var riskArea in ZombieRiskArea.temp)
							if (riskArea != null)
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
				if (ZombielandMod.IsLoadingDefaults == false)
				{
					// upgrade from old settings
					//
					//if (moveSpeedUpgraded == false && (moveSpeedIdle > 1 || moveSpeedTracking > 1))
					//	LongEventHandler.QueueLongEvent(() =>
					//	{
					//		var note = "Zombieland Mod\n\nZombie speed has been normalized to be relative to human speed. Your move speed settings are quite high (> 1), make sure you adjust them.";
					//		Find.WindowStack.Add(new NoteDialog(note));
					//		moveSpeedUpgraded = true;
					//	}, "speed-upgrade", true, null);
				}

				Tools.UpdateBiomeBlacklist(biomesWithoutZombies);
			}
		}

		public void Reset()
		{
			if (Current.ProgramState == ProgramState.Playing)
			{
				ZombieSettings.Values = ZombieSettingsDefaults.group;
				ZombieSettings.ValuesOverTime = new(ZombieSettingsDefaults.groupOverTime);
				SettingsDialog.scrollPosition = Vector2.zero;
				DialogExtensions.shouldFocusNow = DialogExtensions.searchWidget.controlName;
				DialogExtensions.searchWidget.Reset();
				return;
			}

			var type = GetType();
			var defaults = Activator.CreateInstance(type);
			AccessTools.GetFieldNames(this).Do(name =>
			{
				var finfo = AccessTools.Field(type, name);
				finfo.SetValue(this, finfo.GetValue(defaults));
			});
			SettingsDialog.scrollPosition = Vector2.zero;
		}
	}

	class ZombieSettingsDefaults : ModSettings
	{
		public static SettingsGroup group;
		public static List<SettingsKeyFrame> groupOverTime;

		public static SettingsGroup Defaults()
		{
			group ??= new SettingsGroup();
			groupOverTime ??= new() { new SettingsKeyFrame() { values = group.Clone() as SettingsGroup } };
			return group.Clone() as SettingsGroup;
		}

		public static void DoWindowContents(Rect inRect)
		{
			var idx = DialogTimeHeader.selectedKeyframe;
			var ticks = DialogTimeHeader.currentTicks;
			if (idx != -1)
				SettingsDialog.DoWindowContentsInternal(ref groupOverTime[idx].values, ref groupOverTime, inRect);
			else
			{
				var settings = ZombieSettings.CalculateInterpolation(groupOverTime, ticks);
				SettingsDialog.DoWindowContentsInternal(ref settings, ref groupOverTime, inRect);
			}
		}

		public static void WriteSettings()
		{
		}

		public override void ExposeData()
		{
			base.ExposeData();
			group ??= new SettingsGroup();
			Scribe_Deep.Look(ref group, "defaults", Array.Empty<object>());
			Scribe_Collections.Look(ref groupOverTime, "defaultsOverTime", LookMode.Deep);
		}
	}

	class ZombieSettings : WorldComponent
	{
		public static SettingsGroup Values = ZombieSettingsDefaults.Defaults();
		public static List<SettingsKeyFrame> ValuesOverTime = new() { new SettingsKeyFrame() { values = Values.Clone() as SettingsGroup } };

		public ZombieSettings(World world) : base(world)
		{
		}

		public static void ApplyDefaults()
		{
			ZombieSettings.ValuesOverTime = new(ZombieSettingsDefaults.groupOverTime);
			ZombieSettings.Values = ZombieSettings.CalculateInterpolation(ZombieSettings.ValuesOverTime, 0);
			SettingsDialog.scrollPosition = Vector2.zero;
		}

		static readonly Dictionary<string, FieldInfo> fieldInfos = new();
		public static SettingsGroup CalculateInterpolation(List<SettingsKeyFrame> settingsOverTime, int ticks)
		{
			var n = settingsOverTime.Count;
			if (n == 1)
				return settingsOverTime[0].values.Clone() as SettingsGroup;
			var upperIndex = settingsOverTime.FirstIndexOf(key => key.Ticks > ticks);
			if (upperIndex == -1)
				return settingsOverTime.Last().values.Clone() as SettingsGroup;
			var lowerFrame = settingsOverTime[upperIndex - 1];
			var upperFrame = settingsOverTime[upperIndex];
			var lowerTicks = lowerFrame.Ticks;
			var upperTicks = upperFrame.Ticks;
			var lowerValues = lowerFrame.values;
			var upperValues = upperFrame.values;
			var result = new SettingsGroup();
			AccessTools.GetFieldNames(result).Do(name =>
			{
				if (fieldInfos.TryGetValue(name, out var field) == false)
					fieldInfos.Add(name, field = AccessTools.Field(typeof(SettingsGroup), name));
				var type = field.FieldType;
				var lowerValue = field.GetValue(lowerValues);
				var upperValue = field.GetValue(upperValues);
				if (type == typeof(int))
				{
					var val = (int)GenMath.LerpDoubleClamped(lowerTicks, upperTicks, (int)lowerValue, (int)upperValue, ticks);
					field.SetValue(result, val);
				}
				else if (type == typeof(float))
				{
					var val = GenMath.LerpDoubleClamped(lowerTicks, upperTicks, (float)lowerValue, (float)upperValue, ticks);
					field.SetValue(result, val);
				}
				else
					field.SetValue(result, lowerValue);
			});
			return result;
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
			var idx = DialogTimeHeader.selectedKeyframe;
			var ticks = DialogTimeHeader.currentTicks;
			if (idx != -1)
				SettingsDialog.DoWindowContentsInternal(ref ValuesOverTime[idx].values, ref ValuesOverTime, inRect);
			else
			{
				var settings = CalculateInterpolation(ValuesOverTime, ticks);
				SettingsDialog.DoWindowContentsInternal(ref settings, ref ValuesOverTime, inRect);
			}
		}

		public void WriteSettings()
		{
			Tools.EnableTwinkie(Values.replaceTwinkie);
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Deep.Look(ref Values, "values", Array.Empty<object>());
			Scribe_Collections.Look(ref ValuesOverTime, "valuesOverTime", LookMode.Deep);

			if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
				if (ValuesOverTime == null || ValuesOverTime.Count == 0)
					ValuesOverTime = new List<SettingsKeyFrame> { new() { amount = 0, unit = SettingsKeyFrame.Unit.Days, values = Values } };
			}
		}
	}
}