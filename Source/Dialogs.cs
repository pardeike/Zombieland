using Harmony;
using RimWorld;
using System;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace ZombieLand
{
	class SettingsDialog : Page
	{
		public override string PageTitle => "ZombielandGameSettings".Translate();

		public override void DoWindowContents(Rect inRect)
		{
			DrawPageTitle(inRect);
			var mainRect = GetMainRect(inRect, 0f, false);
			Dialogs.DoWindowContentsInternal(ref ZombieSettings.Values, mainRect, false);
			DoBottomButtons(inRect, null, null, null, true);
		}
	}

	public class Dialog_Save : Dialog_SaveFileList
	{
		protected override bool ShouldDoTypeInField => true;

		public Dialog_Save()
		{
			interactButLabel = "OverwriteButton".Translate();
			bottomAreaHeight = 85f;
			if (Faction.OfPlayer.HasName)
				typingName = Faction.OfPlayer.Name;
			else
				typingName = SaveGameFilesUtility.UnusedDefaultFileName(Faction.OfPlayer.def.LabelCap);
		}

		protected override void DoFileInteraction(string fileName)
		{
			Close(true);
			ZombieRemover.RemoveZombieland(fileName);
		}

		public override void PostClose()
		{
		}

		public static void Save()
		{
			// for quick debugging
			// ZombieRemover.RemoveZombieland(null);
			// return;

			Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("ConfirmUninstallZombieland".Translate(), () =>
			{
				Find.WindowStack.currentlyDrawnWindow.Close();
				Find.WindowStack.Add(new Dialog_Save());

			}, true, null));
		}
	}

	static class Dialogs
	{
		static Color contentColor = new Color(1f, 1f, 1f, 0.7f);
		static float inset = 6f;

		public static void Dialog_ToolTip(this Listing_Standard list, string help)
		{
			var rectLine = list.GetRect(Text.LineHeight);
			Widgets.DrawHighlightIfMouseover(rectLine);
			TooltipHandler.TipRegion(rectLine, help.Translate());
		}

		public static void Dialog_Headline(this Listing_Standard list, string text)
		{
			var font = Text.Font;
			Text.Font = GameFont.Medium;
			list.Label(text.Translate());
			Text.Font = font;
		}

		public static void Dialog_Label(this Listing_Standard list, string text, bool addGapBefore = true)
		{
			text = text.Translate();
			if (addGapBefore) list.Gap();

			var anchor = Text.Anchor;
			Text.Anchor = TextAnchor.MiddleLeft;
			var textHeight = Text.CalcHeight(text, list.ColumnWidth - 3f - inset) + 2 * 3f;
			var rect = list.GetRect(textHeight).Rounded();
			GUI.color = new Color(0f, 0f, 0f, 0.3f);
			GUI.DrawTexture(rect, BaseContent.WhiteTex);
			GUI.color = Color.white;
			rect.xMin += inset;
			Widgets.Label(rect, text);
			Text.Anchor = anchor;
			list.Gap(list.verticalSpacing * 3f);
		}

		public static void Dialog_Text(this Listing_Standard list, GameFont font, string text, params object[] args)
		{
			text = text.Translate(args);
			var anchor = Text.Anchor;
			Text.Anchor = TextAnchor.MiddleLeft;
			var savedFont = Text.Font;
			Text.Font = font;
			var textHeight = Text.CalcHeight(text, list.ColumnWidth - 3f - inset) + 2 * 3f;
			var rect = list.GetRect(textHeight).Rounded();
			GUI.color = Color.white;
			rect.xMin += inset;
			Widgets.Label(rect, text);
			Text.Anchor = anchor;
			Text.Font = savedFont;
			list.Gap(2 * list.verticalSpacing);
		}

		public static void Dialog_Button(this Listing_Standard list, string desc, string label, bool dangerous, Action action, bool addGap = true)
		{
			var description = desc.Translate();
			var buttonText = label.Translate();
			var descriptionWidth = (list.ColumnWidth - 3 * inset) * 2 / 3;
			var buttonWidth = list.ColumnWidth - 3 * inset - descriptionWidth;
			var height = Math.Max(30f, Text.CalcHeight(description, descriptionWidth));

			var rect = list.GetRect(height);
			var rect2 = rect;
			rect.xMin += inset;
			rect.width = descriptionWidth;
			Widgets.Label(rect, description);

			rect2.xMax -= inset;
			rect2.xMin = rect2.xMax - buttonWidth;
			rect2.yMin += (height - 30f) / 2;
			rect2.yMax -= (height - 30f) / 2;

			var color = GUI.color;
			GUI.color = dangerous ? new Color(1f, 0.3f, 0.35f) : Color.white;
			if (Widgets.ButtonText(rect2, buttonText, true, true, true)) action();
			GUI.color = color;

			if (addGap) list.Gap(2 * list.verticalSpacing);
		}

		public static void Dialog_Checkbox(this Listing_Standard list, string desc, ref bool forBool, bool addGap = true)
		{
			var label = desc.Translate();
			var indent = 24 + "_".GetWidthCached();
			var height = Math.Max(Text.LineHeight, Text.CalcHeight(label, list.ColumnWidth - indent));

			var rect = list.GetRect(height);
			rect.xMin += inset;
			var line = new Rect(rect);
			Widgets.Checkbox(new Vector2(rect.x, rect.y - 1f), ref forBool);

			var curXField = Traverse.Create(list).Field("curX");
			var curX = curXField.GetValue<float>();
			curXField.SetValue(curX + indent);

			var anchor = Text.Anchor;
			Text.Anchor = TextAnchor.UpperLeft;
			line.xMin += indent;
			var color = GUI.color;
			GUI.color = contentColor;
			Widgets.Label(line, label);
			GUI.color = color;
			Text.Anchor = anchor;

			curXField.SetValue(curX);

			var oldValue = forBool;
			if (Widgets.ButtonInvisible(rect, false))
				forBool = !forBool;
			if (forBool != oldValue)
				SoundDefOf.RadioButtonClicked.PlayOneShotOnCamera(null);

			if (addGap) list.Gap(2 * list.verticalSpacing);
		}

		public static bool Dialog_RadioButton(this Listing_Standard list, bool active, string desc)
		{
			var label = desc.Translate();
			var indent = 24 + "_".GetWidthCached();
			var height = Math.Max(Text.LineHeight, Text.CalcHeight(label, list.ColumnWidth - indent));

			var rect = list.GetRect(height);
			rect.xMin += inset;
			var line = new Rect(rect);
			var result = Widgets.RadioButton(line.xMin, line.yMin, active);

			var curXField = Traverse.Create(list).Field("curX");
			var curX = curXField.GetValue<float>();
			curXField.SetValue(curX + indent);

			var anchor = Text.Anchor;
			Text.Anchor = TextAnchor.UpperLeft;
			line.xMin += indent;
			var color = GUI.color;
			GUI.color = contentColor;
			Widgets.Label(line, label);
			GUI.color = color;
			Text.Anchor = anchor;

			curXField.SetValue(curX);

			result |= Widgets.ButtonInvisible(rect, false);
			if (result && !active)
				SoundDefOf.RadioButtonClicked.PlayOneShotOnCamera(null);

			list.Gap(list.verticalSpacing * 2);
			return result;
		}

		public static void Dialog_Enum<T>(this Listing_Standard list, string desc, ref T forEnum, bool addGapAfter = true, bool addGapBefore = true)
		{
			list.Dialog_Label(desc, addGapBefore);

			var type = forEnum.GetType();
			var choices = Enum.GetValues(type);
			foreach (var choice in choices)
			{
				var label = type.Name + "_" + choice.ToString();
				if (list.Dialog_RadioButton(forEnum.Equals(choice), label))
					forEnum = (T)choice;
			}

			if (addGapAfter) list.Gap(8f);
		}

		public static void Dialog_Integer(this Listing_Standard list, string desc, string unit, int min, int max, ref int value)
		{
			var extraSpace = "_".GetWidthCached();
			var descLength = desc.Translate().GetWidthCached() + extraSpace;
			var unitLength = (unit == null) ? 0 : unit.Translate().GetWidthCached() + extraSpace;

			var rectLine = list.GetRect(Text.LineHeight);
			rectLine.xMin += inset;
			rectLine.xMax -= inset;

			var rectLeft = rectLine.LeftPartPixels(descLength).Rounded();
			var rectRight = rectLine.RightPartPixels(unitLength).Rounded();
			var rectMiddle = new Rect(rectLeft.xMax, rectLeft.yMin, rectRight.xMin - rectLeft.xMax, rectLeft.height);

			var color = GUI.color;
			GUI.color = contentColor;
			Widgets.Label(rectLeft, desc.Translate());

			var alignment = Text.CurTextFieldStyle.alignment;
			Text.CurTextFieldStyle.alignment = TextAnchor.MiddleRight;
			var buffer = value.ToString();
			Widgets.TextFieldNumeric(rectMiddle, ref value, ref buffer, min, max);
			Text.CurTextFieldStyle.alignment = alignment;

			var anchor = Text.Anchor;
			Text.Anchor = TextAnchor.MiddleRight;
			Widgets.Label(rectRight, unit.Translate());
			Text.Anchor = anchor;

			GUI.color = color;

			list.Gap(8f);
		}

		public static void Dialog_FloatSlider(this Listing_Standard list, string desc, string format, ref float value, float min, float max, float multiplier = 1f)
		{
			list.Gap(16f);
			var valstr = string.Format("{0:" + format + "}", value * multiplier);
			var srect = list.GetRect(24f);
			srect.xMin += inset;
			srect.xMax -= inset;
			value = Widgets.HorizontalSlider(srect, value, min, max, false, null, desc.Translate(), valstr, -1f);
		}

		public static void Dialog_TimeSlider(this Listing_Standard list, string desc, ref int value, int min, int max, bool fullDaysOnly = false)
		{
			list.Gap(16f);
			var valstr = Tools.TranslateHoursToText(value);
			var srect = list.GetRect(24f);
			srect.xMin += inset;
			srect.xMax -= inset;
			var newValue = (double)Widgets.HorizontalSlider(srect, value, min, max, false, null, desc.Translate(), valstr, -1f);
			if (fullDaysOnly)
				newValue = Math.Round(newValue / 24f, MidpointRounding.ToEven) * 24f;
			value = (int)newValue;
		}

		static Vector2 scrollPosition = Vector2.zero;
		public static void DoWindowContentsInternal(ref SettingsGroup settings, Rect inRect, bool isDefaults)
		{
			if (settings == null) settings = new SettingsGroup();
			var inGame = Current.Game != null && Current.ProgramState == ProgramState.Playing;

			inRect.yMin += 15f;
			inRect.yMax -= 15f;

			var numberOfColumns = 2;
			var defaultColumnWidth = (inRect.width - (numberOfColumns - 1) * 2f * Listing.ColumnSpacing) / numberOfColumns;
			var list = new Listing_Standard() { ColumnWidth = defaultColumnWidth };

			var outRect = new Rect(inRect.x, inRect.y, inRect.width, inRect.height);
			var scrollRect = new Rect(0f, 0f, inRect.width - 16f, inRect.height * 2.2f);
			Widgets.BeginScrollView(outRect, ref scrollPosition, scrollRect, true);

			list.Begin(scrollRect); // -----------------------------------------------------------------------------

			// When?
			list.Dialog_Enum("WhenDoZombiesSpawn", ref settings.spawnWhenType, true, false);

			// How?
			list.Dialog_Enum("HowDoZombiesSpawn", ref settings.spawnHowType);

			// Attack?
			list.Dialog_Enum("WhatDoZombiesAttack", ref settings.attackMode);
			list.Dialog_Checkbox("EnemiesAttackZombies", ref settings.enemiesAttackZombies);
			list.Dialog_Checkbox("AnimalsAttackZombies", ref settings.animalsAttackZombies);

			// Smash?
			list.Dialog_Enum("WhatDoZombiesSmash", ref settings.smashMode);
			if (settings.smashMode != SmashMode.Nothing)
				list.Dialog_Checkbox("SmashOnlyWhenAgitated", ref settings.smashOnlyWhenAgitated);

			// Senses
			list.Dialog_Enum("ZombieInstinctTitle", ref settings.zombieInstinct);
			list.Dialog_Checkbox("RagingZombies", ref settings.ragingZombies);
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

			// Eating
			list.Dialog_Label("SpecialZombiesTitle");
			list.Dialog_FloatSlider("SuicideBomberChance", "0%", ref settings.suicideBomberChance, 0f, 1f - settings.toxicSplasherChance - settings.tankyOperatorChance);
			list.Dialog_FloatSlider("ToxicSplasherChance", "0%", ref settings.toxicSplasherChance, 0f, 1f - settings.suicideBomberChance - settings.tankyOperatorChance);
			list.Dialog_FloatSlider("ToxicSplasherChance", "0%", ref settings.tankyOperatorChance, 0f, 1f - settings.suicideBomberChance - settings.toxicSplasherChance);
			var normalChance = 1 - settings.suicideBomberChance - settings.toxicSplasherChance - settings.tankyOperatorChance;
			list.Dialog_Text(GameFont.Tiny, "NormalZombieChance", string.Format("{0:0%}", normalChance));

			list.NewColumn();
			list.ColumnWidth -= Listing.ColumnSpacing; // ----------------------------------------------------------

			// Days
			list.Dialog_Label("NewGameTitle", false);
			list.Dialog_Integer("DaysBeforeZombiesCome", null, 0, 100, ref settings.daysBeforeZombiesCome);

			// Total
			list.Dialog_Label("ZombiesOnTheMap");
			list.Dialog_Integer("MaximumNumberOfZombies", "Zombies", 0, 5000, ref settings.maximumNumberOfZombies);
			list.Dialog_FloatSlider("ColonyMultiplier", "0.0x", ref settings.colonyMultiplier, 1f, 10f);

			// Events
			list.Dialog_Label("ZombieEventTitle");
			list.Dialog_Integer("ZombiesPerColonistInEvent", null, 0, 200, ref settings.baseNumberOfZombiesinEvent);
			list.Dialog_Integer("ExtraDaysBetweenEvents", null, 0, 10000, ref settings.extraDaysBetweenEvents);

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
			list.Dialog_TimeSlider("ZombieBiteInfectionUnknown", ref settings.hoursInfectionIsUnknown, 0, 48);
			list.Dialog_TimeSlider("ZombieBiteInfectionTreatable", ref settings.hoursInfectionIsTreatable, 0, 6 * 24);
			list.Dialog_TimeSlider("ZombieBiteInfectionPersists", ref settings.hoursInfectionPersists, 0, 30 * 24, true);
			list.Dialog_Checkbox("AnyTreatmentStopsInfection", ref settings.anyTreatmentStopsInfection);

			// Miscellaneous
			list.Dialog_Label("ZombieMiscTitle");
			list.Dialog_Checkbox("UseCustomTextures", ref settings.useCustomTextures);
			list.Dialog_Checkbox("ReplaceTwinkie", ref settings.replaceTwinkie);
			list.Dialog_Checkbox("ZombiesTriggerDangerMusic", ref settings.zombiesTriggerDangerMusic);
			list.Dialog_Checkbox("PlayCreepyAmbientSound", ref settings.playCreepyAmbientSound);
			list.Dialog_Checkbox("BetterZombieAvoidance", ref settings.betterZombieAvoidance);
			list.Dialog_Checkbox("ZombiesDropBlood", ref settings.zombiesDropBlood);

			// Actions
			list.Dialog_Label("ZombieActionsTitle");
			list.Dialog_Button("ZombieSettingsReset", "Reset", false, settings.Reset);
			if (inGame) list.Dialog_Button("UninstallZombieland", "UninstallButton", true, Dialog_Save.Save, false);

			list.End(); // -----------------------------------------------------------------------------------------

			Widgets.EndScrollView();
		}
	}
}