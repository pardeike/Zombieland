using HarmonyLib;
using RimWorld.Planet;
using System;
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

	internal class NoteDialog : Dialog_MessageBox
	{
		internal NoteDialog(string text, string buttonAText = null, Action buttonAAction = null, string buttonBText = null, Action buttonBAction = null, string title = null, bool buttonADestructive = false, Action acceptAction = null, Action cancelAction = null)
			: base(text, buttonAText, buttonAAction, buttonBText, buttonBAction, title, buttonADestructive, acceptAction, cancelAction) { }

		public override Vector2 InitialSize => new Vector2(320, 240);
	}

	class SettingsGroup : IExposable, ICloneable
	{
		public SpawnWhenType spawnWhenType = SpawnWhenType.WhenDark;
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
		public float colonyMultiplier = 1f;
		public int baseNumberOfZombiesinEvent = 20;
		internal int extraDaysBetweenEvents = 0;
		public float suicideBomberChance = 0.01f;
		public float toxicSplasherChance = 0.01f;
		public float tankyOperatorChance = 0.01f;
		public float minerChance = 0.01f;
		public float electrifierChance = 0.01f;
		public float moveSpeedIdle = 0.2f;
		public float moveSpeedTracking = 1.3f;
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
		public int corpsesExtractAmount = 1;
		public int corpsesHoursToDessicated = 1;
		public bool betterZombieAvoidance = true;
		public bool ragingZombies = true;
		public int zombieRageLevel = 3;
		public bool replaceTwinkie = true;
		public bool zombiesDropBlood = true;
		public bool zombiesBurnLonger = true;
		public float reducedTurretConsumption = 0f;

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

			this.AutoExposeDataWithDefaults();

			if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
				var dirty = false;

				if (suicideBomberChance < 0 || suicideBomberChance > 1)
				{
					suicideBomberChance = 0.01f;
					dirty = true;
				}
				if (toxicSplasherChance < 0 || toxicSplasherChance > 1)
				{
					toxicSplasherChance = 0.01f;
					dirty = true;
				}
				if (tankyOperatorChance < 0 || tankyOperatorChance > 1)
				{
					tankyOperatorChance = 0.01f;
					dirty = true;
				}
				if (minerChance < 0 || minerChance > 1)
				{
					minerChance = 0.01f;
					dirty = true;
				}
				if (electrifierChance < 0 || electrifierChance > 1)
				{
					electrifierChance = 0.01f;
					dirty = true;
				}

				if (dirty && ZombielandMod.IsLoadingDefaults == false)
					LongEventHandler.QueueLongEvent(() =>
					{
						var note = "Zombieland Mod\n\nSpecial zombie percentages were reset to their defaults. Please adjust them and save the game.";
						Find.WindowStack.Add(new NoteDialog(note));
					}, "special-zombies", true, null);
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

		public void Load()
		{
			Dialog_UseSettings.Present(this);
		}

		public void Publish()
		{
			Dialog_PublishSettings.Present(this);
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