using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public static class DialogTimeHeader
	{
		public const float timeHeaderHeight = 110f;
		const float labelWidth = 40;
		const float labelHeight = 24;
		public static Color backgroundColor = new(106f / 255f, 81f / 255f, 46f / 255f);
		public static Color frameColor = new(85f / 255f, 101f / 255f, 117f / 255f);
		public static Color selectionColor = new(1f, 230f / 255f, 24f / 255f);
		public static Color yellowBackground = ((Func<Color>)(() =>
		{
			var c = Color.yellow;
			c.r *= 0.25f;
			c.g *= 0.25f;
			c.b *= 0.25f;
			c.a *= 0.2f;
			return c;
		}))();

		public static int selectedKeyframe = 0;
		public static int currentTicks = 0;
		public static bool dragging = false;

		public static void Reset()
		{
			selectedKeyframe = 0;
			currentTicks = 0;
			dragging = false;
		}

		public static void JumpToCurrent()
		{
			currentTicks = Mathf.Clamp(GenTicks.TicksGame, 0, ZombieSettings.ValuesOverTime.Last().Ticks);
			selectedKeyframe = ZombieSettings.ValuesOverTime.FirstIndexOf(key => key.Ticks == currentTicks);
		}

		public static Rect Draw(ref List<SettingsKeyFrame> settingsOverTime, Rect inRect)
		{
			var vOffset = inRect.yMin;
			var width = (int)inRect.width - 26; // scrollbar space
			var n = settingsOverTime.Count;
			if (n == 1 && selectedKeyframe == -1)
				selectedKeyframe = 0;

			var availableWidth = width - labelWidth * n;
			var spacing = n == 1 ? 0 : availableWidth / (n - 1);

			if (Input.GetMouseButton(0) == false)
				dragging = false;

			var keyFrameXAndTicks = new List<(float x, int ticks)>();
			for (var i = 0; i < n; i++)
			{
				var selected = i == selectedKeyframe;

				var info = settingsOverTime[i];
				var w = labelWidth + spacing;
				var pos = i * w;
				if (i == n - 1)
					pos = n == 1 ? 0 : width - labelWidth;
				var rect = new Rect((int)pos, vOffset, labelWidth, labelHeight);
				Widgets.DrawBoxSolidWithOutline(rect, backgroundColor, selected ? selectionColor : frameColor, selected ? 2 : 1);
				keyFrameXAndTicks.Add((rect.x + labelWidth / 2, info.Ticks));

				if (selected)
				{
					var rect2 = new Rect((int)pos, vOffset + labelHeight, labelWidth, 26);
					Widgets.DrawBoxSolid(rect2, yellowBackground);
				}

				Text.Anchor = TextAnchor.MiddleCenter;
				var labelRect = rect;
				labelRect.y += 1;
				Widgets.Label(labelRect, info.ToString());
				if (Mouse.IsOver(rect))
				{
					if (selected == false)
						Widgets.DrawBoxSolid(rect, new Color(0, 0, 0, 0.25f));
					if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
					{
						selectedKeyframe = i;
						currentTicks = settingsOverTime[i].Ticks;
						Event.current.Use();
					}
				}
			}
			Text.Anchor = TextAnchor.UpperLeft;

			if (n == 1)
			{
				var font = Text.Font;
				Text.Font = GameFont.Tiny;
				Text.Anchor = TextAnchor.MiddleLeft;
				var rect = new Rect(60, vOffset, width - 60, labelHeight);
				Widgets.Label(rect, "ZombieLandKeyframe0".Translate());
				Text.Font = font;
				Text.Anchor = TextAnchor.UpperLeft;
			}

			vOffset += labelHeight;
			vOffset += 12;
			GUI.color = frameColor;
			Widgets.DrawLineHorizontal(0, vOffset, width - Constants.timeArrow.width);
			Widgets.DrawLineHorizontal(0, vOffset + 1, width - Constants.timeArrow.width);
			GUI.color = Color.white;

			var arrowRect = new Rect(width - Constants.timeArrow.width, vOffset - Constants.timeArrow.height / 2 + 1, Constants.timeArrow.width, Constants.timeArrow.height);
			Graphics.DrawTexture(arrowRect, Constants.timeArrow);

			if (dragging)
			{
				var mousePos = Event.current.mousePosition.x;
				var mouseX = Mathf.Clamp(mousePos, 0, width);
				var upperIndex1 = keyFrameXAndTicks.FirstIndexOf(pair => pair.x > mouseX);
				if (upperIndex1 == -1)
					upperIndex1 = keyFrameXAndTicks.Count - 1;
				if (upperIndex1 == 0)
					currentTicks = 0;
				if (upperIndex1 > 0)
				{
					currentTicks = (int)GenMath.LerpDoubleClamped(
						keyFrameXAndTicks[upperIndex1 - 1].x,
						keyFrameXAndTicks[upperIndex1].x,
						keyFrameXAndTicks[upperIndex1 - 1].ticks,
						keyFrameXAndTicks[upperIndex1].ticks,
						mouseX
					);
				}
			}

			var upperIndex2 = settingsOverTime.FirstIndexOf(key => key.Ticks > currentTicks);
			if (upperIndex2 == -1)
				upperIndex2 = settingsOverTime.Count - 1;
			if (upperIndex2 > 0)
			{
				var knobRange = width - Constants.timeKnob[0].width;
				var knobFraction = GenMath.LerpDoubleClamped(
					settingsOverTime[upperIndex2 - 1].Ticks,
					settingsOverTime[upperIndex2].Ticks,
					(upperIndex2 - 1) / (n - 1f),
					upperIndex2 / (n - 1f),
					currentTicks
				);

				var knobRect = new Rect(knobRange * knobFraction, vOffset - Constants.timeKnob[0].height / 2, Constants.timeKnob[0].width, Constants.timeKnob[0].height);
				GUI.DrawTexture(knobRect, Constants.timeKnob[selectedKeyframe == -1 ? 1 : 0]);
				if (Mouse.IsOver(knobRect))
				{
					GUI.DrawTexture(knobRect, Constants.timeKnobHighlight);
					if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
					{
						dragging = true;
						selectedKeyframe = -1;
						Event.current.Use();
					}
				}
			}

			vOffset += 12 + 2;
			var editRect = new Rect(0, vOffset, width, 40);

			if (selectedKeyframe > -1)
			{
				var info = settingsOverTime[selectedKeyframe];
				var backup = info.Copy();

				Widgets.DrawBoxSolid(editRect, yellowBackground);
				editRect = editRect.ExpandedBy(-5);

				if (selectedKeyframe > 0)
				{
					var buffer = info.amount.ToString();
					const float fieldWidth = 45f;
					Widgets.TextFieldNumeric(editRect.LeftPartPixels(fieldWidth), ref info.amount, ref buffer, 0);
					var radioPos = editRect.x + fieldWidth + 10;

					var units = Enum.GetNames(typeof(SettingsKeyFrame.Unit));
					for (var i = 0; i < units.Length; i++)
					{
						var label = units[i].Translate().CapitalizeFirst();
						var labelSize = Text.CalcSize(label).x;
						var radioWidth = 24 + 5 + labelSize + 10;
						var r = new Rect(radioPos, editRect.y + 3, radioWidth, 24);
						var unit = (SettingsKeyFrame.Unit)Enum.Parse(typeof(SettingsKeyFrame.Unit), units[i]);
						if (Widgets.RadioButton(r.x, r.y, info.unit == unit) && info.unit != unit)
							info.unit = unit;
						Text.Anchor = TextAnchor.MiddleLeft;
						Widgets.Label(r.RightPartPixels(labelSize + 10), label);
						Text.Anchor = TextAnchor.UpperLeft;
						radioPos += radioWidth;
					}

					var newValue = info.Ticks;
					if (newValue <= settingsOverTime[selectedKeyframe - 1].Ticks)
					{
						info.amount = backup.amount;
						info.unit = backup.unit;
					}
					else if (selectedKeyframe < n - 1 && newValue >= settingsOverTime[selectedKeyframe + 1].Ticks)
					{
						info.amount = backup.amount;
						info.unit = backup.unit;
					}

					currentTicks = settingsOverTime[selectedKeyframe].Ticks;
				}

				var deleteLabel = "Delete".Translate();
				var deleteLabelWidth = Text.CalcSize(deleteLabel).x + 20;
				var duplicateLabel = "DuplicateButton".Translate();
				var duplicateLabelWidth = Text.CalcSize(duplicateLabel).x + 20;

				var w = selectedKeyframe > 0 ? deleteLabelWidth : duplicateLabelWidth;
				var rect2 = new Rect(editRect.width - w + 5, editRect.y, w, editRect.height);
				if (selectedKeyframe > 0)
				{
					GUI.color = new Color(1f, 0.3f, 0.35f);
					if (Widgets.ButtonText(rect2, deleteLabel))
					{
						settingsOverTime.RemoveAt(selectedKeyframe);
						if (selectedKeyframe >= settingsOverTime.Count)
							selectedKeyframe--;
					}
					GUI.color = Color.white;
					rect2.x -= duplicateLabelWidth + 5;
					rect2.width = duplicateLabelWidth;
				}
				if (Widgets.ButtonText(rect2, duplicateLabel))
				{
					var newValue = info.Copy();
					newValue.amount++;
					settingsOverTime.Insert(selectedKeyframe + 1, newValue);
					for (var i = selectedKeyframe + 2; i < settingsOverTime.Count; i++)
						while (settingsOverTime[i - 1].Ticks >= settingsOverTime[i].Ticks)
							settingsOverTime[i].amount++;
					selectedKeyframe++;
				}

				if (n == 1)
				{
					var font = Text.Font;
					Text.Font = GameFont.Tiny;
					Text.Anchor = TextAnchor.MiddleLeft;
					var rect = editRect.LeftPartPixels(400);
					Widgets.Label(rect, "ZombieLandKeyframes".Translate());
					Text.Font = font;
					Text.Anchor = TextAnchor.UpperLeft;
				}
			}

			return inRect.BottomPartPixels(inRect.height - timeHeaderHeight);
		}
	}
}
