using System;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using System.Linq;
using RimWorld;

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

	static class EnumMethods
	{
		static readonly float[] halfToDouble = { 0.5f, 1.0f, 2.0f };
		public static float HalfToDoubleValue(this ZombieInstinct e)
		{
			return halfToDouble[(int)e];
		}
	}

	class SettingsGroup : IExposable, ICloneable
	{
		public SpawnWhenType spawnWhenType;
		public SpawnHowType spawnHowType;
		public AttackMode attackMode;
		public SmashMode smashMode;
		public bool smashOnlyWhenAgitated;
		public bool doubleTapRequired;
		public bool zombiesDieVeryEasily;
		public int daysBeforeZombiesCome;
		public int maximumNumberOfZombies;
		public int baseNumberOfZombiesinEvent;
		public float moveSpeedIdle;
		public float moveSpeedTracking;
		public float damageFactor;
		public ZombieInstinct zombieInstinct;
		public bool useCustomTextures;
		public bool zombiesTriggerDangerMusic;
		public bool zombiesEatDowned;
		public bool zombiesEatCorpses;
		public float zombieBiteInfectionChance;

		public object Clone()
		{
			return MemberwiseClone();
		}

		public void ExposeData()
		{
			Scribe_Values.Look(ref spawnWhenType, "spawnWhenType");
			Scribe_Values.Look(ref spawnHowType, "spawnHowType");
			Scribe_Values.Look(ref attackMode, "attackMode");
			Scribe_Values.Look(ref smashMode, "smashMode");
			Scribe_Values.Look(ref smashOnlyWhenAgitated, "smashOnlyWhenAgitated");
			Scribe_Values.Look(ref doubleTapRequired, "doubleTapRequired");
			Scribe_Values.Look(ref zombiesDieVeryEasily, "zombiesDieVeryEasily");
			Scribe_Values.Look(ref daysBeforeZombiesCome, "daysBeforeZombiesCome");
			Scribe_Values.Look(ref maximumNumberOfZombies, "maximumNumberOfZombies");
			Scribe_Values.Look(ref baseNumberOfZombiesinEvent, "baseNumberOfZombiesinEvent");
			Scribe_Values.Look(ref moveSpeedIdle, "moveSpeedIdle");
			Scribe_Values.Look(ref moveSpeedTracking, "moveSpeedTracking");
			Scribe_Values.Look(ref damageFactor, "damageFactor");
			Scribe_Values.Look(ref zombieInstinct, "zombieInstinct");
			Scribe_Values.Look(ref useCustomTextures, "useCustomTextures");
			Scribe_Values.Look(ref zombiesTriggerDangerMusic, "zombiesTriggerDangerMusic");
			Scribe_Values.Look(ref zombiesEatDowned, "zombiesEatDowned");
			Scribe_Values.Look(ref zombiesEatCorpses, "zombiesEatCorpses");
			Scribe_Values.Look(ref zombieBiteInfectionChance, "zombieBiteInfectionChance");
		}
	}

	class ZombieSettingsDefaults : ModSettings
	{
		static Vector2 scrollPosition = Vector2.zero;

		static SettingsGroup group;
		static readonly SettingsGroup defaults = new SettingsGroup()
		{
			spawnWhenType = SpawnWhenType.WhenDark,
			spawnHowType = SpawnHowType.FromTheEdges,
			attackMode = AttackMode.OnlyHumans,
			smashMode = SmashMode.DoorsOnly,
			smashOnlyWhenAgitated = true,
			doubleTapRequired = true,
			zombiesDieVeryEasily = false,
			daysBeforeZombiesCome = 3,
			maximumNumberOfZombies = 1000,
			baseNumberOfZombiesinEvent = 20,
			moveSpeedIdle = 0.2f,
			moveSpeedTracking = 1.3f,
			damageFactor = 1.0f,
			zombieInstinct = ZombieInstinct.Normal,
			useCustomTextures = true,
			zombiesTriggerDangerMusic = false,
			zombiesEatDowned = true,
			zombiesEatCorpses = true,
			zombieBiteInfectionChance = 0.5f
		};

		public static SettingsGroup Defaults()
		{
			if (group == null) group = defaults;
			return group.Clone() as SettingsGroup;
		}

		public static void DoWindowContentsInternal(ref SettingsGroup settings, Rect inRect, bool isDefaults)
		{
			if (settings == null) settings = defaults;

			inRect.yMin += 15f;
			inRect.yMax -= 15f;

			var numberOfColumns = 2;
			var defaultColumnWidth = (inRect.width - (numberOfColumns - 1) * 2f * Listing.ColumnSpacing) / numberOfColumns;
			var list = new Listing_Standard() { ColumnWidth = defaultColumnWidth };

			var outRect = new Rect(inRect.x, inRect.y, inRect.width, inRect.height);
			var scrollRect = new Rect(0f, 0f, inRect.width - 16f, inRect.height * 2f);
			Widgets.BeginScrollView(outRect, ref scrollPosition, scrollRect, true);

			list.Begin(scrollRect); // -----------------------------------------------------------------------------

			// When?
			list.Dialog_Enum("WhenDoZombiesSpawn", ref settings.spawnWhenType, true, false);

			// How?
			list.Dialog_Enum("HowDoZombiesSpawn", ref settings.spawnHowType);

			// Attack?
			list.Dialog_Enum("WhatDoZombiesAttack", ref settings.attackMode);

			// Smash?
			list.Dialog_Enum("WhatDoZombiesSmash", ref settings.smashMode, false);
			list.Dialog_Checkbox("SmashOnlyWhenAgitated", ref settings.smashOnlyWhenAgitated);

			// Senses
			list.Dialog_Enum("ZombieInstinctTitle", ref settings.zombieInstinct);
			list.Gap(4f);

			// Health
			list.Dialog_Label("ZombieHealthTitle");
			list.Dialog_Checkbox("DoubleTapRequired", ref settings.doubleTapRequired);
			list.Dialog_Checkbox("ZombiesDieVeryEasily", ref settings.zombiesDieVeryEasily);
			list.Gap(6f);

			// Eating
			list.Dialog_Label("ZombieEatingTitle");
			list.Dialog_Checkbox("ZombiesEatDowned", ref settings.zombiesEatDowned);
			list.Dialog_Checkbox("ZombiesEatCorpses", ref settings.zombiesEatCorpses);
			list.Gap(6f);

			list.NewColumn();
			list.ColumnWidth -= Listing.ColumnSpacing; // ----------------------------------------------------------

			// Days
			list.Dialog_Label("NewGameTitle", false);
			list.Dialog_Integer("DaysBeforeZombiesCome", null, 0, 100, ref settings.daysBeforeZombiesCome);

			// Total
			list.Dialog_Label("ZombiesOnTheMap");
			list.Dialog_Integer("MaximumNumberOfZombies", "Zombies", 0, 5000, ref settings.maximumNumberOfZombies);

			// Events
			list.Dialog_Label("ZombieEventTitle");
			list.Dialog_Integer("ZombiesPerColonistInEvent", null, 0, 200, ref settings.baseNumberOfZombiesinEvent);

			// Speed
			list.Dialog_Label("ZombieSpeedTitle");
			list.Dialog_FloatSlider("MoveSpeedIdle", "0.0x", ref settings.moveSpeedIdle, 0.05f, 2f);
			list.Gap(-4f);
			list.Dialog_FloatSlider("MoveSpeedTracking", "0.0x", ref settings.moveSpeedTracking, 0.2f, 3f);

			// Strength
			list.Dialog_Label("ZombieDamageFactorTitle");
			list.Dialog_FloatSlider("ZombieDamageFactor", "0.0x", ref settings.damageFactor, 0.1f, 4f);

			// Infections
			list.Dialog_Label("ZombieInfections");
			list.Dialog_FloatSlider("ZombieBiteInfectionChance", "0%", ref settings.zombieBiteInfectionChance, 0f, 1f);

			// Miscellaneous
			list.Dialog_Label("ZombieMiscTitle");
			list.Dialog_Checkbox("UseCustomTextures", ref settings.useCustomTextures);
			list.Dialog_Checkbox("ZombiesTriggerDangerMusic", ref settings.zombiesTriggerDangerMusic);

			list.End(); // -----------------------------------------------------------------------------------------

			Widgets.EndScrollView();
		}

		public static void DoWindowContents(Rect inRect)
		{
			DoWindowContentsInternal(ref group, inRect, true);
		}

		public override void ExposeData()
		{
			base.ExposeData();
			if (group == null) group = defaults;
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
			ZombieSettingsDefaults.DoWindowContentsInternal(ref Values, inRect, false);
		}

		public override void ExposeData()
		{
			base.ExposeData();
			Scribe_Deep.Look(ref Values, "values", new object[0]);
		}
	}

	class SettingsDialog : Page
	{
		public override string PageTitle => "ZombielandGameSettings".Translate();

		public override void DoWindowContents(Rect inRect)
		{
			DrawPageTitle(inRect);
			var mainRect = GetMainRect(inRect, 0f, false);
			ZombieSettingsDefaults.DoWindowContentsInternal(ref ZombieSettings.Values, mainRect, false);
			DoBottomButtons(inRect, null, null, null, true);
		}
	}
}