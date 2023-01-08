using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	static class SettingsDialog
	{
		public static Vector2 scrollPosition = Vector2.zero;

		public static void DoWindowContentsInternal(ref SettingsGroup settings, ref List<SettingsKeyFrame> settingsOverTime, Rect inRect)
		{
			inRect.yMin += 15f;
			inRect.yMax -= 15f;

			var firstColumnWidth = (inRect.width - Listing.ColumnSpacing) * 3.5f / 5f;
			var secondColumnWidth = inRect.width - Listing.ColumnSpacing - firstColumnWidth;

			var outerRect = new Rect(inRect.x, inRect.y, firstColumnWidth, inRect.height);
			var innerRect = new Rect(0f, 0f, firstColumnWidth - 24f, 3460);

			outerRect = DialogTimeHeader.Draw(ref settingsOverTime, outerRect);

			Widgets.BeginScrollView(outerRect, ref scrollPosition, innerRect, true);

			DialogExtensions.ResetHelpItem();
			var headerColor = Color.yellow;
			var inGame = Current.Game != null && Current.ProgramState == ProgramState.Playing;

			var list = new Listing_Standard();
			list.Begin(innerRect);

			{
				// About
				var intro = "Zombieland_Settings".SafeTranslate();
				var textHeight = Text.CalcHeight(intro, list.ColumnWidth - 3f - DialogExtensions.inset) + 2 * 3f;
				Widgets.Label(list.GetRect(textHeight).Rounded(), intro);
				list.Gap(10f);

				// Difficulty
				if (DialogExtensions.Section<string>(":ZombielandDifficultyTitle", ":ZombielandDifficulty"))
				{
					list.Dialog_Label("ZombielandDifficultyTitle", headerColor);
					list.Gap(6f);
					list.Dialog_FloatSlider("ZombielandDifficulty", _ => "{0:0%}", false, ref settings.threatScale, 0f, 5f);
					list.Gap(12f);
				}

				// When?
				if (DialogExtensions.Section<SpawnWhenType>(":WhenDoZombiesSpawn"))
				{
					list.Dialog_Enum("WhenDoZombiesSpawn", ref settings.spawnWhenType);
					list.Gap(26f);
				}

				// How?
				if (DialogExtensions.Section<SpawnHowType>(":HowDoZombiesSpawn", ":SmartWandering", ":BlacklistedBiomes", ":Biomes"))
				{
					list.Dialog_Enum("HowDoZombiesSpawn", ref settings.spawnHowType);
					list.Gap(8);
					list.ChooseWanderingStyle(settings);
					var localSettings = settings;
					list.Dialog_Button("BlacklistedBiomes", "Biomes", false, () => Find.WindowStack.Add(new Dialog_BiomeList(localSettings)));
					list.Gap(30f);
				}

				// Attack?
				if (DialogExtensions.Section<AttackMode>(":WhatDoZombiesAttack", ":EnemiesAttackZombies", ":AnimalsAttackZombies", ":WallPushing"))
				{
					list.Dialog_Enum("WhatDoZombiesAttack", ref settings.attackMode);
					list.Dialog_Checkbox("EnemiesAttackZombies", ref settings.enemiesAttackZombies);
					list.Dialog_Checkbox("AnimalsAttackZombies", ref settings.animalsAttackZombies);
					list.Gap(10f);
					list.Dialog_IntSlider("WallPushing", n => n == 0 ? "Off".TranslateSimple() : n.ToString(), ref settings.minimumZombiesForWallPushing, 0, 32);
					list.Gap(30f);
				}

				// Smash?
				if (DialogExtensions.Section<SmashMode>(":WhatDoZombiesSmash", ":SmashOnlyWhenAgitated"))
				{
					list.Dialog_Enum("WhatDoZombiesSmash", ref settings.smashMode);
					if (settings.smashMode != SmashMode.Nothing)
					{
						list.Dialog_Checkbox("SmashOnlyWhenAgitated", ref settings.smashOnlyWhenAgitated);
					}
					list.Gap(30f);
				}

				// Senses
				if (DialogExtensions.Section<ZombieInstinct>(":ZombieInstinctTitle", ":RagingZombies", ":RageLevel"))
				{
					list.Dialog_Enum("ZombieInstinctTitle", ref settings.zombieInstinct);
					list.Dialog_Checkbox("RagingZombies", ref settings.ragingZombies);
					var rageLevelNames = new string[] { "RageLevelVeryLow", "RageLevelLow", "RageLevelNormal", "RageLevelHigh", "RageLevelVeryHigh" };
					list.Gap(8f);
					if (settings.ragingZombies)
						list.Dialog_IntSlider("RageLevel", level => rageLevelNames[level - 1].Translate(), ref settings.zombieRageLevel, 1, 5);
					list.Gap(22f);
				}

				// Health
				if (DialogExtensions.Section<string>(":ZombieHealthTitle", ":DoubleTapRequired", ":ZombiesDieVeryEasily"))
				{
					list.Dialog_Label("ZombieHealthTitle", headerColor);
					list.Dialog_Checkbox("DoubleTapRequired", ref settings.doubleTapRequired);
					list.Dialog_Checkbox("ZombiesDieVeryEasily", ref settings.zombiesDieVeryEasily);
					list.Gap(30f);
				}

				// Eating
				if (DialogExtensions.Section<string>(":ZombieEatingTitle", ":ZombiesEatDowned", ":ZombiesEatCorpses"))
				{
					list.Dialog_Label("ZombieEatingTitle", headerColor);
					list.Dialog_Checkbox("ZombiesEatDowned", ref settings.zombiesEatDowned);
					list.Dialog_Checkbox("ZombiesEatCorpses", ref settings.zombiesEatCorpses);
					list.Gap(30f);
				}

				// Types
				if (DialogExtensions.Section<string>(":SpecialZombiesTitle", ":SuicideBomberChance", ":ToxicSplasherChance", ":TankyOperatorChance", ":MinerChance", ":ElectrifierChance", ":AlbinoChance", ":DarkSlimerChance", ":HealerChance", ":NormalZombieChance", "ChildZombieChance"))
				{
					list.Dialog_Label("SpecialZombiesTitle", headerColor);
					list.Gap(8f);
					var localSettings = settings;
					var chances = new[]
					{
						( new FloatRef(() => localSettings.suicideBomberChance, f => localSettings.suicideBomberChance = f), "SuicideBomberChance"),
						( new FloatRef(() => localSettings.toxicSplasherChance, f => localSettings.toxicSplasherChance = f), "ToxicSplasherChance"),
						( new FloatRef(() => localSettings.tankyOperatorChance, f => localSettings.tankyOperatorChance = f), "TankyOperatorChance"),
						( new FloatRef(() => localSettings.minerChance, f => localSettings.minerChance = f), "MinerChance"),
						( new FloatRef(() => localSettings.electrifierChance, f => localSettings.electrifierChance = f), "ElectrifierChance"),
						( new FloatRef(() => localSettings.albinoChance, f => localSettings.albinoChance = f), "AlbinoChance"),
						( new FloatRef(() => localSettings.darkSlimerChance, f => localSettings.darkSlimerChance = f), "DarkSlimerChance"),
						( new FloatRef(() => localSettings.healerChance, f => localSettings.healerChance = f), "HealerChance"),
					};
					var total = chances.Sum(c => c.Item1.Value);
					var max = Mathf.Min(1f, 2f * chances.Aggregate(0.04f, (prev, curr) => Mathf.Max(prev, curr.Item1.Value)));
					var normalChance = 1f - total;
					for (var i = 0; i < chances.Length; i++)
					{
						var chance = chances[i].Item1;
						var value = chance.Value;
						var remaining = total - value;
						list.Dialog_FloatSlider(chances[i].Item2, _ => "{0:0.00%}", false, ref value, 0f, Mathf.Min(max, 1f - remaining));
						chance.Value = value;
					}
					list.Gap(-6f);
					list.Dialog_Text(GameFont.Tiny, "NormalZombieChance", string.Format("{0:0.00%}", normalChance));
					list.Gap(12f);
					list.Dialog_FloatSlider("ChildZombieChance", _ => "{0:0.00%}", true, ref settings.childChance, 0f, 1f);
					list.Gap(24f);
				}

				// Days
				if (DialogExtensions.Section<string>(":NewGameTitle", ":DaysBeforeZombiesCome"))
				{
					list.Dialog_Label("NewGameTitle", headerColor);
					list.Dialog_Integer("DaysBeforeZombiesCome", null, 0, 100, ref settings.daysBeforeZombiesCome);
					list.Gap(34f);
				}

				// Total
				if (DialogExtensions.Section<string>(":ZombiesOnTheMap", ":MaximumNumberOfZombies", ":ColonyMultiplier"))
				{
					list.Dialog_Label("ZombiesOnTheMap", headerColor);
					list.Gap(2f);
					list.Dialog_Integer("MaximumNumberOfZombies", "Zombies", 0, 5000, ref settings.maximumNumberOfZombies);
					list.Gap(12f);
					list.Dialog_FloatSlider("ColonyMultiplier", _ => "{0:0.0}x", false, ref settings.colonyMultiplier, 0.1f, 10f);
				}

				if (DialogExtensions.Section<string>(":DynamicThreatLevelTitle", ":UseDynamicThreatLevel", ":DynamicThreatSmoothness", ":DynamicThreatStretch", ":ZombiesDieOnZeroThreat"))
				{
					list.Dialog_Label("DynamicThreatLevelTitle", headerColor);
					list.Gap(8f);
					list.Dialog_Checkbox("UseDynamicThreatLevel", ref settings.useDynamicThreatLevel);
					if (settings.useDynamicThreatLevel)
					{
						list.Gap(8f);
						list.Dialog_FloatSlider("DynamicThreatSmoothness", _ => "{0:0%}", false, ref settings.dynamicThreatSmoothness, 1f, 5f, f => (f - 1f) / 4f);
						list.Gap(-4f);
						list.Dialog_FloatSlider("DynamicThreatStretch", _ => "{0:0%}", false, ref settings.dynamicThreatStretch, 10f, 30f, f => (f - 10f) / 20f);
						list.Gap(-6f);
						list.Dialog_Checkbox("ZombiesDieOnZeroThreat", ref settings.zombiesDieOnZeroThreat);
					}
					list.Gap(28f);
				}

				// Events
				if (DialogExtensions.Section<string>(":ZombieEventTitle", ":ZombiesPerColonistInEvent", ":ExtraDaysBetweenEvents", ":InfectedRaidsChance"))
				{
					list.Dialog_Label("ZombieEventTitle", headerColor);
					list.Dialog_Integer("ZombiesPerColonistInEvent", null, 0, 200, ref settings.baseNumberOfZombiesinEvent);
					list.Dialog_Integer("ExtraDaysBetweenEvents", null, 0, 10000, ref settings.extraDaysBetweenEvents);
					list.Gap(12f);
					list.Dialog_FloatSlider("InfectedRaidsChance", f => f == 0 ? "Off".TranslateSimple() : "{0:0.0%}", true, ref settings.infectedRaidsChance, 0f, 1f);
					list.Gap(28f);
				}

				// Speed
				if (DialogExtensions.Section<string>(":ZombieSpeedTitle", ":MoveSpeedIdle", ":MoveSpeedTracking"))
				{
					list.Dialog_Label("ZombieSpeedTitle", headerColor);
					list.Gap(8f);
					list.Dialog_FloatSlider("MoveSpeedIdle", _ => "{0:0.00}x", false, ref settings.moveSpeedIdle, 0.01f, 2f);
					list.Dialog_FloatSlider("MoveSpeedTracking", _ => "{0:0.00}x", false, ref settings.moveSpeedTracking, 0.05f, 3f);
					list.Gap(24f);
				}

				// Damage
				if (DialogExtensions.Section<string>(":ZombieDamageTitle", ":ZombieDamageFactor", ":SafeMeleeLimit", ":ZombiesCauseManhunting"))
				{
					list.Dialog_Label("ZombieDamageTitle", headerColor);
					list.Gap(8f);
					list.Dialog_FloatSlider("ZombieDamageFactor", _ => "{0:0.0}x", false, ref settings.damageFactor, 0.1f, 4f);
					list.Dialog_IntSlider("SafeMeleeLimit", n => n == 0 ? "Off".TranslateSimple() : n.ToString(), ref settings.safeMeleeLimit, 0, 4);
					if (settings.safeMeleeLimit > 0)
					{
						list.Gap(-2f);
						list.ExplainSafeMelee(settings.safeMeleeLimit);
						list.Gap(12f);
					}
					list.Gap(6f);
					list.Dialog_Checkbox("ZombiesCauseManhunting", ref settings.zombiesCauseManhuntingResponse);
					list.Gap(36f);
				}

				// Tweaks
				if (DialogExtensions.Section<string>(":ZombieGameTweaks", ":ReduceTurretConsumption"))
				{
					list.Dialog_Label("ZombieGameTweaks", headerColor);
					list.Gap(8f);
					list.Dialog_FloatSlider("ReduceTurretConsumption", _ => "{0:0%}", false, ref settings.reducedTurretConsumption, 0f, 1f);
					list.Gap(28f);
				}

				// Infections
				if (DialogExtensions.Section<string>(":ZombieInfection", ":ZombieBiteInfectionChance", ":ZombieBiteInfectionUnknown", ":ZombieBiteInfectionTreatable", ":ZombieBiteInfectionTreatable", ":ZombieBiteInfectionPersists", ":AnyTreatmentStopsInfection", ":HoursAfterDeathToBecomeZombie", ":DeadBecomesZombieMessage", ":DangerousSituationMessage"))
				{
					list.Dialog_Label("ZombieInfection", headerColor);
					list.Gap(8f);
					list.Dialog_FloatSlider("ZombieBiteInfectionChance", _ => "{0:0%}", false, ref settings.zombieBiteInfectionChance, 0f, 1f);
					list.Dialog_TimeSlider("ZombieBiteInfectionUnknown", ref settings.hoursInfectionIsUnknown, 0, 48);
					list.Dialog_TimeSlider("ZombieBiteInfectionTreatable", ref settings.hoursInfectionIsTreatable, 0, 6 * 24);
					list.Dialog_TimeSlider("ZombieBiteInfectionPersists", ref settings.hoursInfectionPersists, 0, 30 * 24, null, true);
					list.Gap(-4f);
					list.Dialog_Checkbox("AnyTreatmentStopsInfection", ref settings.anyTreatmentStopsInfection);
					list.Gap(22f);
					static string hoursTranslator(int n) => n == -1 ? "Off".Translate() : (n == 0 ? "Immediately".Translate() : null);
					list.Dialog_TimeSlider("HoursAfterDeathToBecomeZombie", ref settings.hoursAfterDeathToBecomeZombie, -1, 6 * 24, hoursTranslator, false);
					if (settings.hoursAfterDeathToBecomeZombie > -1)
					{
						list.Gap(-4f);
						list.Dialog_Checkbox("DeadBecomesZombieMessage", ref settings.deadBecomesZombieMessage);
					}
					list.Gap(30f);
				}

				// Zombie loot
				if (DialogExtensions.Section<string>(":ZombieHarvestingTitle", ":CorpsesExtractAmount", ":LootExtractAmount", ":CorpsesDaysToDessicated"))
				{
					list.Dialog_Label("ZombieHarvestingTitle", headerColor);
					list.Gap(8f);
					var f1 = Mathf.Round(settings.corpsesExtractAmount * 100f) / 100f;
					list.Dialog_FloatSlider("CorpsesExtractAmount", DialogExtensions.ExtractAmount, false, ref f1, 0, 4);
					settings.corpsesExtractAmount = Mathf.Round(f1 * 100f) / 100f;
					var f2 = Mathf.Round(settings.lootExtractAmount * 100f) / 100f;
					list.Dialog_FloatSlider("LootExtractAmount", DialogExtensions.ExtractAmount, false, ref f2, 0, 4);
					settings.lootExtractAmount = Mathf.Round(f2 * 100f) / 100f;
					list.Dialog_TimeSlider("CorpsesDaysToDessicated", ref settings.corpsesHoursToDessicated, 1, 120);
					list.ChooseExtractArea(settings);
					list.Gap(28f);
				}

				// Miscellaneous
				if (DialogExtensions.Section<string>(":ZombieMiscTitle", ":UseCustomTextures", ":ReplaceTwinkie", ":PlayCreepyAmbientSound", ":BetterZombieAvoidance", ":ZombiesDropBlood", ":ZombiesBurnLonger", ":ShowHealthBar", ":ShowZombieStats", ":HighlightDangerousAreas", ":DisableRandomApparel", ":FloatingZombiesInSOS2"))
				{
					list.Dialog_Label("ZombieMiscTitle", headerColor);
					list.Dialog_Checkbox("UseCustomTextures", ref settings.useCustomTextures);
					list.Dialog_Checkbox("ReplaceTwinkie", ref settings.replaceTwinkie);
					list.Dialog_Checkbox("PlayCreepyAmbientSound", ref settings.playCreepyAmbientSound);
					list.Dialog_Checkbox("BetterZombieAvoidance", ref settings.betterZombieAvoidance);
					list.Dialog_Checkbox("ZombiesDropBlood", ref settings.zombiesDropBlood);
					list.Dialog_Checkbox("ZombiesBurnLonger", ref settings.zombiesBurnLonger);
					list.Dialog_Checkbox("ShowHealthBar", ref settings.showHealthBar);
					list.Dialog_Checkbox("ShowZombieStats", ref settings.showZombieStats);
					list.Dialog_Checkbox("HighlightDangerousAreas", ref settings.highlightDangerousAreas);
					list.Dialog_Checkbox("DisableRandomApparel", ref settings.disableRandomApparel);
					list.Dialog_Checkbox("DangerousSituationMessage", ref settings.dangerousSituationMessage);
					if (SoSTools.isInstalled)
						list.Dialog_Checkbox("FloatingZombiesInSOS2", ref settings.floatingZombies);
					else
						settings.floatingZombies = true;
					list.Gap(30f);
				}

				// Actions
				if (DialogExtensions.Section<string>(":ZombieActionsTitle", ":ZombieSettingsReset", ":ResetButton", ":UninstallZombieland", ":UninstallButton"))
				{
					list.Dialog_Label("ZombieActionsTitle", headerColor);
					list.Gap(8f);
					list.Dialog_Button("ZombieSettingsReset", "ResetButton", false, settings.Reset);
					if (inGame)
						list.Dialog_Button("UninstallZombieland", "UninstallButton", true, Dialog_SaveThenUninstall.Run);
				}
			}

			list.End();
			Widgets.EndScrollView();

			var boxHeight = 136f;
			var clipboardActionsRect = new Rect(inRect.x + firstColumnWidth + Listing.ColumnSpacing, inRect.y + inRect.height - boxHeight, inRect.width - firstColumnWidth - Listing.ColumnSpacing, boxHeight);

			var auxColumn = new Rect(inRect.x + firstColumnWidth + Listing.ColumnSpacing, inRect.y, secondColumnWidth, inRect.height - boxHeight);
			list = new Listing_Standard();
			list.Begin(auxColumn);

			list.ColumnWidth -= 6;
			var serachRect = list.GetRect(28f);
			list.ColumnWidth += 6;
			DialogExtensions.searchWidget.OnGUISimple(serachRect, () =>
			{
				var editor = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
				if (editor != null)
					DialogExtensions.searchWidgetSelectionState = (editor.cursorIndex, editor.selectIndex);

				scrollPosition = Vector2.zero;
				DialogExtensions.shouldFocusNow = DialogExtensions.searchWidget.controlName;
			});

			if (DialogExtensions.currentHelpItem != null)
			{
				list.Gap(16f);

				var title = DialogExtensions.currentHelpItem.SafeTranslate().Replace(": {0}", "");
				list.Dialog_Label(title, Color.white, false);
				list.Gap(8f);

				var text = (DialogExtensions.currentHelpItem + "_Help").SafeTranslate();
				var anchor = Text.Anchor;
				Text.Anchor = TextAnchor.MiddleLeft;
				var textHeight = Text.CalcHeight(text, list.ColumnWidth - 3f - DialogExtensions.inset) + 2 * 3f;
				var rect = list.GetRect(textHeight).Rounded();
				GUI.color = Color.white;
				Widgets.Label(rect, text);
				Text.Anchor = anchor;
			}

			list.End();

			list = new Listing_Standard();
			list.Begin(clipboardActionsRect);
			list.Dialog_Label("ClipboardActionTitle", headerColor);
			list.Gap(8f);
			list.Dialog_Button("CopySettings", "CopyButton", false, settingsOverTime.ToClipboard);
			list.Dialog_Button("PasteSettings", "PasteButton", true, settingsOverTime.FromClipboard);
			list.End();

			if (DialogExtensions.shouldFocusNow != null && Event.current.type == EventType.Layout)
			{
				GUI.FocusControl(DialogExtensions.shouldFocusNow);

				var editor = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), GUIUtility.keyboardControl);
				if (editor != null)
				{
					editor.OnFocus();
					editor.cursorIndex = DialogExtensions.searchWidgetSelectionState.Item1;
					editor.selectIndex = DialogExtensions.searchWidgetSelectionState.Item2;
				}

				DialogExtensions.shouldFocusNow = null;
			}

			if (Current.ProgramState == ProgramState.Playing)
			{
				var ticks = Find.TickManager.TicksGame;
				ZombieSettings.Values = ZombieSettings.CalculateInterpolation(ZombieSettings.ValuesOverTime, ticks);
			}
		}
	}
}