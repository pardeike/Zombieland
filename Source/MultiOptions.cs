using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class MultiOptions<T> : Window
	{
		public List<T> items;
		private readonly Vector2 size;
		private readonly float rowHeight;
		public override Vector2 InitialSize => size;

		private readonly string title;
		private readonly Func<List<T>> valueClosure;
		private readonly Action<Listing_Standard, List<T>, T> rowRenderer;
		private Vector2 scrollPosition = Vector2.zero;

		public MultiOptions(string title, Func<List<T>> valueClosure, Action<Listing_Standard, List<T>, T> rowRenderer, Vector2 size, float rowHeight = 24f) : base()
		{
			this.title = title.SafeTranslate();
			this.valueClosure = valueClosure;
			this.rowRenderer = rowRenderer;
			this.size = size;
			this.rowHeight = rowHeight;
			doCloseButton = true;
			absorbInputAroundWindow = true;
		}

		public override void DoWindowContents(Rect inRect)
		{
			var values = valueClosure();

			inRect.yMax -= 60;

			var num = Text.CalcHeight(title, inRect.width);
			Widgets.Label(new Rect(inRect.xMin, inRect.yMin, inRect.width, num), title);
			inRect.yMin += num + 8;

			var outerRect = new Rect(inRect.x, inRect.y, inRect.width, inRect.height);
			var innerRect = new Rect(0f, 0f, inRect.width - 24f, values.Count * rowHeight);
			Widgets.BeginScrollView(outerRect, ref scrollPosition, innerRect, true);

			var list = new Listing_Standard();
			list.Begin(innerRect);
			foreach (var value in values)
				rowRenderer(list, values, value);
			list.End();

			Widgets.EndScrollView();
		}
	}
}