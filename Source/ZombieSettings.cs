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

	public enum ZombieResponsePolicy
	{
		Minimal,
		Adaptive,
		Full
	}

	public enum AnomalyTargetingOverride
	{
		Automatic,
		Never,
		Allow
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
			_ => amount * GenDate.TicksPerDay
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
			values = values.MakeCopy()
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
		public ZombieResponsePolicy friendlyZombieResponse = ZombieResponsePolicy.Adaptive;
		public ZombieResponsePolicy enemyZombieResponse = ZombieResponsePolicy.Full;
		public bool animalsAttackZombies = false;
		public AnomalyTargetingOverride anomalyGhoulTargeting = AnomalyTargetingOverride.Automatic;
		public AnomalyTargetingOverride anomalyShamblerTargeting = AnomalyTargetingOverride.Automatic;
		public AnomalyTargetingOverride anomalyEntityTargeting = AnomalyTargetingOverride.Automatic;
		public AnomalyTargetingOverride anomalyNociosphereTargeting = AnomalyTargetingOverride.Automatic;
		public AnomalyTargetingOverride anomalyAttacksZombies = AnomalyTargetingOverride.Automatic;
		public bool ordinaryZombiesAttackFleshmass = true;
		public bool tankyAndSuicideZombiesAttackFleshmass = true;
		public bool formerColonistAndSpecialZombiesAttackFleshmass = true;
		public SmashMode smashMode = SmashMode.DoorsOnly;
		public bool smashOnlyWhenAgitated = true;
		public bool doubleTapRequired = true;
		public bool zombiesDieVeryEasily = false;
		public float healthFactor = 1f;
		public int daysBeforeZombiesCome = 3;
		public int maximumNumberOfZombies = 500;
		public bool useDynamicThreatLevel = true;
		public bool zombiesDieOnZeroThreat = true;
		public bool zombieFreeEvents = true;
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
		public float damageFactor = 1.0f;
		public ZombieInstinct zombieInstinct = ZombieInstinct.Normal;
		public bool useCustomTextures = true;
		public bool playZombielandMusic = true;
		public bool mixZombielandMusicModes = false;
		public int zombielandMusicShare = 50;
		public bool playCreepyAmbientSound = true;
		public bool showZombieEventLetters = true;
		public bool playZombieEventSiren = true;
		public bool playSpecialZombieAmbientSounds = true;
		public bool playZombieActionSounds = true;
		public bool playWallAndSabotageSounds = true;
		public bool showZombieThoughtBubbles = true;
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
		public float spitterThreat = 1f;
		public bool symbiantEnabled = true;
		public int symbiantMaxCells = 400;
		public bool muteSymbiantSplashSounds = true;
		public int minimumZombiesForWallPushing = 18;
		public List<string> blacklistedApparel = new();
		public float contaminationBaseFactor = 1f;
		public ContaminationFactors contamination = new();
		public object Clone() => MemberwiseClone();
		public SettingsGroup MakeCopy() => Clone() as SettingsGroup;

		public void ExposeData()
		{
			// no base.ExposeData() to call

			this.AutoExposeDataWithDefaults((settings, name, value, defaultValue) =>
			{
				if (value is ZombieResponsePolicy responsePolicy && defaultValue is ZombieResponsePolicy defaultResponsePolicy)
				{
					var savedValue = responsePolicy.ToString();
					if (Scribe.mode == LoadSaveMode.LoadingVars
						&& name == nameof(enemyZombieResponse)
						&& Scribe.loader.curXmlParent[name] == null
						&& Scribe.loader.curXmlParent["enemiesAttackZombies"] is { } legacyNode
						&& bool.TryParse(legacyNode.InnerText, out var legacyValue))
					{
						savedValue = MigrateLegacyEnemyZombieResponse(legacyValue).ToString();
					}
					else
						Scribe_Values.Look(ref savedValue, name, defaultResponsePolicy.ToString());
					if (Scribe.mode == LoadSaveMode.LoadingVars)
						AccessTools.Field(typeof(SettingsGroup), name).SetValue(settings, ParseZombieResponsePolicy(savedValue, defaultResponsePolicy));
					return true;
				}

				if (value is AnomalyTargetingOverride anomalyTargetingOverride && defaultValue is AnomalyTargetingOverride defaultAnomalyTargetingOverride)
				{
					var savedValue = anomalyTargetingOverride.ToString();
					Scribe_Values.Look(ref savedValue, name, defaultAnomalyTargetingOverride.ToString());
					if (Scribe.mode == LoadSaveMode.LoadingVars)
						AccessTools.Field(typeof(SettingsGroup), name).SetValue(settings, ParseAnomalyTargetingOverride(savedValue, defaultAnomalyTargetingOverride));
					return true;
				}

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
				zombielandMusicShare = ZombielandMusic.NormalizeShare(zombielandMusicShare);
				Tools.UpdateBiomeBlacklist(biomesWithoutZombies);
			}
		}

		internal static ZombieResponsePolicy ParseZombieResponsePolicy(string value, ZombieResponsePolicy defaultValue)
		{
			if (value.NullOrEmpty())
				return defaultValue;
			if (Enum.TryParse<ZombieResponsePolicy>(value, true, out var result))
				return result;
			return defaultValue;
		}

		internal static ZombieResponsePolicy MigrateLegacyEnemyZombieResponse(bool legacyValue)
		{
			return legacyValue ? ZombieResponsePolicy.Full : ZombieResponsePolicy.Minimal;
		}

		static AnomalyTargetingOverride ParseAnomalyTargetingOverride(string value, AnomalyTargetingOverride defaultValue)
		{
			if (value.NullOrEmpty())
				return defaultValue;

			if (Enum.TryParse<AnomalyTargetingOverride>(value, true, out var result))
				return result;

			return value.Trim().ToLowerInvariant() switch
			{
				"default" or "baserule" or "base rule" => AnomalyTargetingOverride.Automatic,
				"ignore" or "ignored" => AnomalyTargetingOverride.Never,
				"attack" => AnomalyTargetingOverride.Allow,
				_ => defaultValue
			};
		}
	}

	class ZombieSettingsDefaults : ModSettings
	{
		public static SettingsGroup group;
		public static List<SettingsKeyFrame> groupOverTime;

		internal static bool NormalizeTimeline(ref List<SettingsKeyFrame> timeline, SettingsGroup fallback)
		{
			fallback ??= new SettingsGroup();
			var repaired = timeline == null;
			timeline ??= new List<SettingsKeyFrame>();

			var normalized = timeline
				.Where(frame => frame?.values != null)
				.OrderBy(frame => frame.Ticks)
				.ToList();
			if (normalized.Count != timeline.Count)
				repaired = true;
			else
				for (var i = 0; i < normalized.Count; i++)
					if (ReferenceEquals(normalized[i], timeline[i]) == false)
					{
						repaired = true;
						break;
					}

			if (normalized.Count == 0 || normalized[0].Ticks > 0)
			{
				normalized.Insert(0, new SettingsKeyFrame
				{
					amount = 0,
					unit = SettingsKeyFrame.Unit.Days,
					values = fallback.MakeCopy()
				});
				repaired = true;
			}

			if (repaired)
			{
				timeline.Clear();
				timeline.AddRange(normalized);
			}
			return repaired;
		}

		internal static bool EnsureValidTimeline()
		{
			var repaired = group == null;
			group ??= new SettingsGroup();
			return NormalizeTimeline(ref groupOverTime, group) || repaired;
		}

		public static void Defaults()
		{
			group = (new SettingsGroup()).MakeCopy();
			groupOverTime = new() { new SettingsKeyFrame() { values = group.MakeCopy() } };
		}

		public static void DoWindowContents(Rect inRect)
		{
			var idx = DialogTimeHeader.selectedKeyframe;
			var ticks = DialogTimeHeader.currentTicks;
			if (idx != -1)
			{
				if (idx >= groupOverTime.Count)
				{
					DialogTimeHeader.selectedKeyframe = 0;
					idx = 0;
				}
				SettingsDialog.DoWindowContentsInternal(ref groupOverTime[idx].values, ref groupOverTime, inRect);
			}
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
			groupOverTime ??= new() { new SettingsKeyFrame() { values = group.MakeCopy() } };
			Scribe_Deep.Look(ref group, "defaults", Array.Empty<object>());
			Scribe_Collections.Look(ref groupOverTime, "defaultsOverTime", LookMode.Deep, Array.Empty<object>());

			if (Scribe.mode == LoadSaveMode.PostLoadInit && EnsureValidTimeline())
				Log.Warning("Zombieland repaired an invalid default settings timeline.");
		}
	}

	class ZombieSettings : WorldComponent
	{
		public readonly struct ThreatTimelineSettings
		{
			public readonly float threatScale;
			public readonly int daysBeforeZombiesCome;
			public readonly bool zombieFreeEvents;
			public readonly float dynamicThreatSmoothness;
			public readonly float dynamicThreatStretch;

			public ThreatTimelineSettings(SettingsGroup settings)
			{
				threatScale = settings?.threatScale ?? 1f;
				daysBeforeZombiesCome = settings?.daysBeforeZombiesCome ?? 3;
				zombieFreeEvents = settings?.zombieFreeEvents ?? true;
				dynamicThreatSmoothness = settings?.dynamicThreatSmoothness ?? 2.5f;
				dynamicThreatStretch = settings?.dynamicThreatStretch ?? 20f;
			}

			public ThreatTimelineSettings(float threatScale, int daysBeforeZombiesCome, bool zombieFreeEvents, float dynamicThreatSmoothness, float dynamicThreatStretch)
			{
				this.threatScale = threatScale;
				this.daysBeforeZombiesCome = daysBeforeZombiesCome;
				this.zombieFreeEvents = zombieFreeEvents;
				this.dynamicThreatSmoothness = dynamicThreatSmoothness;
				this.dynamicThreatStretch = dynamicThreatStretch;
			}
		}

		public static SettingsGroup Values;
		public static List<SettingsKeyFrame> ValuesOverTime;

		static ZombieSettings()
		{
			Values = ZombieSettingsDefaults.group;
			ValuesOverTime = ZombieSettingsDefaults.groupOverTime;
		}

		public ZombieSettings(World world) : base(world)
		{
		}

		public static void ApplyDefaults()
		{
			ZombieSettingsDefaults.EnsureValidTimeline();
			ValuesOverTime = new(ZombieSettingsDefaults.groupOverTime);
			Values = CalculateInterpolation(ValuesOverTime, 0);
			SettingsDialog.scrollPosition = Vector2.zero;
		}

		static readonly Dictionary<string, FieldInfo> fieldInfos = new();
		public static SettingsGroup CalculateInterpolation(List<SettingsKeyFrame> settingsOverTime, int ticks)
		{
			SettingsKeyFrame lowerFrame = null;
			SettingsKeyFrame upperFrame = null;
			if (settingsOverTime != null)
				foreach (var frame in settingsOverTime)
				{
					if (frame?.values == null)
						continue;
					if (frame.Ticks > ticks)
					{
						upperFrame = frame;
						break;
					}
					lowerFrame = frame;
				}

			if (lowerFrame == null)
				return upperFrame?.values.MakeCopy() ?? new SettingsGroup();
			if (upperFrame == null)
				return lowerFrame.values.MakeCopy();
			var lowerTicks = lowerFrame.Ticks;
			var upperTicks = upperFrame.Ticks;
			var lowerValues = lowerFrame.values;
			var upperValues = upperFrame.values;
			if (upperTicks <= lowerTicks)
				return lowerValues.MakeCopy();
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

		public static SettingsGroup ValuesAtGameTick(int ticks)
		{
			if (ValuesOverTime == null || ValuesOverTime.Count == 0)
				return Values?.MakeCopy() ?? ZombieSettingsDefaults.group?.MakeCopy() ?? new SettingsGroup();
			return CalculateInterpolation(ValuesOverTime, Mathf.Max(0, ticks));
		}

		public static ThreatTimelineSettings ThreatSettingsAtGameTick(int ticks)
		{
			var fallback = Values ?? ZombieSettingsDefaults.group ?? new SettingsGroup();
			var valuesOverTime = ValuesOverTime;
			if (valuesOverTime == null || valuesOverTime.Count == 0)
				return new ThreatTimelineSettings(fallback);

			ticks = Mathf.Max(0, ticks);
			var upperIndex = valuesOverTime.FirstIndexOf(key => key != null && key.Ticks > ticks);
			if (upperIndex == -1)
				return new ThreatTimelineSettings(valuesOverTime.LastOrDefault(key => key?.values != null)?.values ?? fallback);
			if (upperIndex == 0)
				return new ThreatTimelineSettings(valuesOverTime[0]?.values ?? fallback);

			var lowerFrame = valuesOverTime[upperIndex - 1];
			var upperFrame = valuesOverTime[upperIndex];
			var lowerValues = lowerFrame?.values ?? fallback;
			var upperValues = upperFrame?.values ?? lowerValues;
			var lowerTicks = lowerFrame?.Ticks ?? 0;
			var upperTicks = upperFrame?.Ticks ?? lowerTicks;
			if (upperTicks <= lowerTicks)
				return new ThreatTimelineSettings(lowerValues);

			return new ThreatTimelineSettings(
				GenMath.LerpDoubleClamped(lowerTicks, upperTicks, lowerValues.threatScale, upperValues.threatScale, ticks),
				(int)GenMath.LerpDoubleClamped(lowerTicks, upperTicks, lowerValues.daysBeforeZombiesCome, upperValues.daysBeforeZombiesCome, ticks),
				lowerValues.zombieFreeEvents,
				GenMath.LerpDoubleClamped(lowerTicks, upperTicks, lowerValues.dynamicThreatSmoothness, upperValues.dynamicThreatSmoothness, ticks),
				GenMath.LerpDoubleClamped(lowerTicks, upperTicks, lowerValues.dynamicThreatStretch, upperValues.dynamicThreatStretch, ticks)
			);
		}

		public static float ThreatScaleAtGameTick(int ticks)
		{
			return ThreatSettingsAtGameTick(ticks).threatScale;
		}

		public static bool ZombieFreeEventsAtGameTick(int ticks)
		{
			return ThreatSettingsAtGameTick(ticks).zombieFreeEvents;
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
				Values ??= ZombieSettingsDefaults.group?.MakeCopy() ?? new SettingsGroup();
				if (ZombieSettingsDefaults.NormalizeTimeline(ref ValuesOverTime, Values))
					Log.Warning("Zombieland repaired an invalid saved-game settings timeline.");

				var ticks = Mathf.Clamp(GenTicks.TicksGame, 0, ValuesOverTime.Last().Ticks);
				var settings = CalculateInterpolation(ValuesOverTime, ticks);
				ContaminationFactors.ApplyBaseFactor(settings.contamination, settings.contaminationBaseFactor);
				LongEventHandler.ExecuteWhenFinished(RimHudIntegration.TryApplyForActiveGame);
			}
		}
	}
}
