using System;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using System.Linq;
using Harmony;

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
		public int baseNumberOfZombiesinEvent = 20;
		public float suicideBomberChance = 0.1f;
		public float moveSpeedIdle = 0.2f;
		public float moveSpeedTracking = 1.3f;
		public float damageFactor = 1.0f;
		public ZombieInstinct zombieInstinct = ZombieInstinct.Normal;
		public bool useCustomTextures = true;
		public bool zombiesTriggerDangerMusic;
		public bool zombiesEatDowned = true;
		public bool zombiesEatCorpses = true;
		public float zombieBiteInfectionChance = 0.5f;
		public int hoursInfectionIsUnknown = 8;
		public int hoursInfectionIsTreatable = 24;
		public int hoursInfectionPersists = 6 * 24;
		public bool anyTreatmentStopsInfection;
		public bool betterZombieAvoidance = true;
		public bool ragingZombies = true;
		public bool replaceTwinkie = true;
		public bool zombiesDropBlood = true;

		public object Clone()
		{
			return MemberwiseClone();
		}

		public void ExposeData()
		{
			this.AutoExposeDataWithDefaults();
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
			Dialogs.DoWindowContentsInternal(ref group, inRect, true);
		}

		public static void WriteSettings()
		{
		}

		public override void ExposeData()
		{
			base.ExposeData();
			if (group == null) group = new SettingsGroup();
			Scribe_Deep.Look(ref group, "defaults", new object[0]);
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
			Dialogs.DoWindowContentsInternal(ref Values, inRect, false);
		}

		public void WriteSettings()
		{
			Tools.EnableTwinkie(Values.replaceTwinkie);
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Deep.Look(ref Values, "values", new object[0]);
		}
	}
}