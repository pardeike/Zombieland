using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace ZombieLand
{
	static class DialogExtensions
	{
		static Color contentColor = new(1f, 1f, 1f, 0.7f);
		public const float inset = 6f;
		public static string currentHelpItem = null;

		public static QuickSearchWidget searchWidget = new();
		public static (int, int) searchWidgetSelectionState = (0, 0);
		public static string shouldFocusNow = searchWidget.controlName;

		public static void Help(this Listing_Standard list, string helpItem, float height = 0f)
		{
			var curX = list.curX;
			var curY = list.curY;
			var rect = new Rect(curX, curY, list.ColumnWidth, height > 0f ? height : Text.LineHeight);
			if (Mouse.IsOver(rect))
				currentHelpItem = helpItem;
		}

		public static void ResetHelpItem()
		{
			currentHelpItem = null;
		}

		public static void Dialog_Label(this Listing_Standard list, string labelId, Color color, bool provideHelp = true)
		{
			var labelText = provideHelp ? labelId.SafeTranslate() : labelId;

			var anchor = Text.Anchor;
			Text.Anchor = TextAnchor.MiddleLeft;
			var textHeight = Text.CalcHeight(labelText, list.ColumnWidth - 3f - inset) + 2 * 3f;

			if (provideHelp)
				list.Help(labelId);

			var rect = list.GetRect(textHeight).Rounded();
			var color2 = color;
			color2.r *= 0.25f;
			color2.g *= 0.25f;
			color2.b *= 0.25f;
			color2.a *= 0.2f;
			GUI.color = color2;
			var r = rect.ContractedBy(1f);
			r.yMax -= 2f;
			GUI.DrawTexture(r, BaseContent.WhiteTex);
			GUI.color = color;
			rect.xMin += inset;
			Widgets.Label(rect, labelText);
			GUI.color = Color.white;
			Text.Anchor = anchor;
		}

		public static void Dialog_Text(this Listing_Standard list, GameFont font, string textId, params object[] args)
		{
			var text = textId.SafeTranslate(args);
			var anchor = Text.Anchor;
			Text.Anchor = TextAnchor.MiddleLeft;
			var savedFont = Text.Font;
			Text.Font = font;
			var textHeight = Text.CalcHeight(text, list.ColumnWidth - 3f - inset) + 2 * 3f;
			list.Help(textId);
			var rect = list.GetRect(textHeight).Rounded();
			GUI.color = Color.white;
			rect.xMin += inset;
			Widgets.Label(rect, text);
			Text.Anchor = anchor;
			Text.Font = savedFont;
		}

		public static void Dialog_Button(this Listing_Standard list, string desc, string labelId, bool dangerous, Action action)
		{
			list.Gap(6f);

			var description = desc.SafeTranslate();
			var buttonText = labelId.SafeTranslate();
			var descriptionWidth = (list.ColumnWidth - 3 * inset) * 2 / 3;
			var buttonWidth = list.ColumnWidth - 3 * inset - descriptionWidth;
			var height = Math.Max(30f, Text.CalcHeight(description, descriptionWidth));

			list.Help(labelId, height);

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
			if (Widgets.ButtonText(rect2, buttonText, true, true, true))
				action();
			GUI.color = color;
		}

		public static void Dialog_Checkbox(this Listing_Standard list, string labelId, ref bool forBool, bool skipTranslation = false, bool disabled = false)
		{
			list.Gap(2f);

			var label = skipTranslation ? labelId : labelId.SafeTranslate();
			var indent = 24 + "_".GetWidthCached();
			var height = Math.Max(Text.LineHeight, Text.CalcHeight(label, list.ColumnWidth - indent));

			list.Help(labelId, height);

			var rect = list.GetRect(height);
			rect.xMin += inset;

			var oldValue = forBool;
			var butRect = rect;
			butRect.xMin += 24f;
			if (disabled == false)
			{
				if (Widgets.ButtonInvisible(butRect, false))
					forBool = !forBool;
				if (forBool != oldValue)
				{
					if (forBool)
						SoundDefOf.Checkbox_TurnedOn.PlayOneShotOnCamera(null);
					else
						SoundDefOf.Checkbox_TurnedOff.PlayOneShotOnCamera(null);
				}
			}

			Widgets.Checkbox(new Vector2(rect.x, rect.y - 1f), ref forBool, disabled: disabled);

			var curX = list.curX;
			list.curX = curX + indent;

			var anchor = Text.Anchor;
			Text.Anchor = TextAnchor.UpperLeft;
			rect.xMin += indent;
			var color = GUI.color;
			GUI.color = contentColor;
			Widgets.Label(rect, label);
			GUI.color = color;
			Text.Anchor = anchor;

			list.curX = curX;
		}

		public static bool Dialog_RadioButton(this Listing_Standard list, bool active, string labelId)
		{
			var label = labelId.SafeTranslate();
			var indent = 24 + "_".GetWidthCached();
			var height = Math.Max(Text.LineHeight, Text.CalcHeight(label, list.ColumnWidth - indent));

			list.Help(labelId, height);

			var rect = list.GetRect(height);
			rect.xMin += inset;
			var line = new Rect(rect);
			var result = Widgets.RadioButton(line.xMin, line.yMin, active);

			var curX = list.curX;
			list.curX = curX + indent;

			var anchor = Text.Anchor;
			Text.Anchor = TextAnchor.UpperLeft;
			line.xMin += indent;
			var color = GUI.color;
			GUI.color = contentColor;
			Widgets.Label(line, label);
			GUI.color = color;
			Text.Anchor = anchor;

			list.curX = curX;

			result |= Widgets.ButtonInvisible(rect, false);
			if (result && !active)
				SoundDefOf.Click.PlayOneShotOnCamera(null);

			return result;
		}

		public static void Dialog_Enum<T>(this Listing_Standard list, string desc, ref T forEnum)
		{
			list.Dialog_Label(desc, Color.yellow);

			var type = forEnum.GetType();
			var choices = Enum.GetValues(type);
			foreach (var choice in choices)
			{
				list.Gap(2f);
				var label = type.Name + "_" + choice.ToString();
				if (list.Dialog_RadioButton(forEnum.Equals(choice), label))
					forEnum = (T)choice;
			}
		}

		public static void Dialog_List<T>(this Listing_Standard list, string labelId, T value, Action<T> updateValue, List<T> choices, Func<T, string> translator, T defaultValue)
		{
			var labelText = labelId.SafeTranslate();
			var valueText = choices.Contains(value) ? value.ToString() : defaultValue.ToString();

			var extraSpace = "_".GetWidthCached();
			var descLength = labelText.GetWidthCached() + extraSpace;
			var valueLength = valueText.GetWidthCached();

			translator ??= val => val.ToString();

			list.Help(labelId, Text.LineHeight);

			var rectLine = list.GetRect(Text.LineHeight);
			rectLine.xMin += inset;
			rectLine.xMax -= inset;

			var rectLeft = rectLine.LeftPartPixels(descLength).Rounded();
			var rectRight = rectLine.RightPartPixels(valueLength).Rounded();

			var color = GUI.color;
			var anchor = Text.Anchor;
			GUI.color = contentColor;
			Text.Anchor = TextAnchor.MiddleLeft;
			Widgets.Label(rectLeft, labelText);
			GUI.color = Color.white;
			Widgets.Label(rectRight, valueText);
			Text.Anchor = anchor;
			GUI.color = color;

			if (Event.current.type == EventType.MouseDown && Mouse.IsOver(rectLine))
			{
				var found = false;
				var options = choices.Select(choice =>
				{
					var matches = choice.Equals(value);
					found |= matches;
					return new FloatMenuOption($"{translator(choice)}{(choice.Equals(value) ? " ✓" : "")}", () => updateValue(choice));
				}).ToList();
				if (choices.Contains(defaultValue) == false)
					options.Insert(0, new FloatMenuOption($"{translator(defaultValue)}{(found ? "" : " ✓")}", () => updateValue(default)));
				Find.WindowStack.Add(new FloatMenu(options));
			}
		}

		public static void Dialog_Integer(this Listing_Standard list, string labelId, string unit, int min, int max, ref int value)
		{
			list.Gap(6f);

			var unitString = unit.SafeTranslate();
			var extraSpace = "_".GetWidthCached();
			var descLength = labelId.Translate().GetWidthCached() + extraSpace;
			var unitLength = (unit == null) ? 0 : unitString.GetWidthCached() + extraSpace;

			list.Help(labelId, Text.LineHeight);

			var rectLine = list.GetRect(Text.LineHeight);
			rectLine.xMin += inset;
			rectLine.xMax -= inset;

			var rectLeft = rectLine.LeftPartPixels(descLength).Rounded();
			var rectRight = rectLine.RightPartPixels(unitLength).Rounded();
			var rectMiddle = new Rect(rectLeft.xMax, rectLeft.yMin, rectRight.xMin - rectLeft.xMax, rectLeft.height);

			var color = GUI.color;
			GUI.color = contentColor;
			Widgets.Label(rectLeft, labelId.Translate());

			var alignment = Text.CurTextFieldStyle.alignment;
			Text.CurTextFieldStyle.alignment = TextAnchor.MiddleRight;
			var buffer = value.ToString();
			Widgets.TextFieldNumeric(rectMiddle, ref value, ref buffer, min, max);
			Text.CurTextFieldStyle.alignment = alignment;

			var anchor = Text.Anchor;
			Text.Anchor = TextAnchor.MiddleRight;
			Widgets.Label(rectRight, unitString);
			Text.Anchor = anchor;

			GUI.color = color;
		}

		public static void Dialog_FloatSlider(this Listing_Standard list, string labelId, Func<float, string> labelFormatFunc, bool logarithmic, ref float value, float min, float max, Func<float, float> formatFunc = null)
		{
			list.Help(labelId, 32f);

			list.Gap(12f);

			var format = labelFormatFunc(value);
			var valstr = string.Format(format, formatFunc != null ? formatFunc(value) : value);

			var srect = list.GetRect(24f);
			srect.xMin += inset;
			srect.xMax -= inset;

			var inValue = logarithmic ? (float)(1 - Math.Pow(1 - (double)value, 10)) : value;
			if (inValue < min)
				inValue = min;
			if (inValue > max)
				inValue = max;
			var outValue = Tools.HorizontalSlider(srect, inValue, min, max, false, null, labelId.SafeTranslate(), valstr, -1f);
			value = logarithmic ? (float)(1 - Math.Pow(1 - outValue, 1 / (double)10)) : outValue;
			if (value < min)
				value = min;
			if (value > max)
				value = max;
		}

		public static void Dialog_EnumSlider<T>(this Listing_Standard list, string labelId, ref T forEnum)
		{
			list.Help(labelId, 32f);

			var type = forEnum.GetType();
			var choices = Enum.GetValues(type);
			var max = choices.Length - 1;

			list.Gap(12f);

			var srect = list.GetRect(24f);
			srect.xMin += inset;
			srect.xMax -= inset;

			var value = $"{typeof(T).Name}_{forEnum}".SafeTranslate();
			var n = (int)Tools.HorizontalSlider(srect, Convert.ToInt32(forEnum), 0, max, false, null, labelId.SafeTranslate(), value, 1);
			forEnum = (T)Enum.ToObject(typeof(T), n);
		}

		public static void Dialog_IntSlider(this Listing_Standard list, string labelId, Func<int, string> format, ref int value, int min, int max)
		{
			list.Help(labelId, 32f);

			list.Gap(12f);

			var srect = list.GetRect(24f);
			srect.xMin += inset;
			srect.xMax -= inset;

			value = (int)(0.5f + Tools.HorizontalSlider(srect, value, min, max, false, null, labelId.SafeTranslate(), format(value), -1f));
		}

		public static void Dialog_TimeSlider(this Listing_Standard list, string labelId, ref int value, int min, int max, Func<int, string> valueStringConverter = null, bool fullDaysOnly = false)
		{
			list.Gap(-4f);
			list.Help(labelId, 32f);

			list.Gap(12f);

			valueStringConverter ??= (n) => null;
			var valstr = valueStringConverter(value) ?? Tools.TranslateHoursToText(value);

			var srect = list.GetRect(24f);
			srect.xMin += inset;
			srect.xMax -= inset;

			var newValue = (double)Tools.HorizontalSlider(srect, value, min, max, false, null, labelId.SafeTranslate(), valstr, -1f);
			if (fullDaysOnly)
				value = (int)(Math.Round(newValue / 24f, MidpointRounding.ToEven) * 24f);
			else
				value = (int)newValue;
		}

		public static void ChooseExtractArea(this Listing_Standard list, SettingsGroup settings)
		{
			if (Current.Game == null)
				return;
			var multiMap = Find.Maps.Count > 1;
			var areas = Find.Maps
				.Where(map => map.IsBlacklisted() == false)
				.SelectMany(map => map.areaManager.AllAreas
				.Where(area => area.Mutable)
				.OrderBy(area => area.ListPriority)
				.Select(area => (map, area)))
				.Select(pair => multiMap ? $"{pair.area.Label}:{pair.map.Index + 1}" : pair.area.Label)
				.ToList();
			list.Gap(-2f);
			list.Dialog_List("ExtractZombieArea", settings.extractZombieArea, area => settings.extractZombieArea = area ?? "", areas, null, "Everywhere".Translate());
		}

		public static void ChooseWanderingStyle(this Listing_Standard list, SettingsGroup settings)
		{
			var defaultChoice = Enum.GetName(typeof(WanderingStyle), WanderingStyle.Smart);
			var choices = Enum.GetValues(typeof(WanderingStyle)).Cast<WanderingStyle>().ToList();
			list.Dialog_List("SmartWandering", settings.wanderingStyle, value => settings.wanderingStyle = value, choices, value => $"SmartWandering_{value}".Translate(), WanderingStyle.Smart);
		}

		public static string ExtractAmount(float f)
		{
			if (f == 0)
				return "Off".TranslateSimple();
			return "{0:0%} " + "CorpsesExtractChance".Translate(f);
		}

		public static void MiniButton(this Listing_Standard list, Texture2D texture, Action action)
		{
			const float size = 11f;
			var butRect = new Rect(list.curX + 1, list.curY + 2, size, size);
			if (Widgets.ButtonImage(butRect, texture, true))
				action();
		}

		public static int exampleMeleeSkill = 10;
		public static int exampleZombieCount = 1;
		public static void ExplainSafeMelee(this Listing_Standard list, int safeMeleeLimit)
		{
			Text.Font = GameFont.Tiny;
			var chance = Mathf.FloorToInt(100f * exampleMeleeSkill * Mathf.Max(0, safeMeleeLimit - exampleZombieCount + 1) / 20f);
			var text = "SafeMeleeExample".Translate(exampleMeleeSkill, exampleZombieCount, chance).Resolve();
			var buttonText = "[_]";
			var buttonWidth = buttonText.GetWidthCached();
			list.curX = 7f;
			for (var i = 0; i <= 4; i++)
			{
				var idx = text.IndexOf(buttonText);
				var part = idx == -1 ? text : text.Substring(0, idx);

				var num = Text.CalcHeight("x", list.ColumnWidth);
				var rect = new Rect(list.curX, list.curY, list.ColumnWidth, num);
				Widgets.Label(rect, part);
				list.curX += part.GetWidthCached();
				if (i == 4)
					break;

				list.MiniButton(i % 2 == 0 ? Constants.MinusButton : Constants.PlusButton, () =>
				{
					switch (i)
					{
						case 0:
							if (exampleMeleeSkill > 0)
								exampleMeleeSkill--;
							break;
						case 1:
							exampleMeleeSkill++;
							break;
						case 2:
							if (exampleZombieCount > 1)
								exampleZombieCount--;
							break;
						case 3:
							exampleZombieCount++;
							break;
					}
				});
				list.curX += buttonWidth;

				text = text.Substring(idx + buttonText.Length);
			}

			list.curX = 0;
			list.Gap(12);
		}

		public static bool Section<T>(params string[] term)
		{
			var search = searchWidget.filter.Text.Trim().ToLower();
			if (search == "")
				return true;
			if (term.Any(t =>
			{
				if (t.StartsWith(":"))
				{
					t = t.Substring(1);
					if (t.SafeTranslate().ToLower().Contains(search))
						return true;
					if ($"{t}_Help".SafeTranslate().ToLower().Contains(search))
						return true;
					return false;
				}
				return t.ToLower().Contains(search);
			}))
				return true;
			var type = typeof(T);
			if (type != typeof(string))
			{
				if (type.Name.SafeTranslate().ToLower().Contains(search))
					return true;
				var choices = Enum.GetValues(type);
				foreach (var choice in choices)
				{
					var label = type.Name + "_" + choice.ToString();
					if (label.SafeTranslate().ToLower().Contains(search))
						return true;
					if ($"{label}_Help".SafeTranslate().ToLower().Contains(search))
						return true;
				}
			}
			return false;
		}
	}
}
