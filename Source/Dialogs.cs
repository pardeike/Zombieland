using Harmony;
using RimWorld;
using System;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace ZombieLand
{
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

		public static bool Dialog_RadioButton(this Listing_Standard list, bool active, string desc, float tabIn = 0f)
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
	}
}