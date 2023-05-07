using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class ThingDefComparer : IComparer<ThingDef>
	{
		public int Compare(ThingDef x, ThingDef y) => x.label.CompareTo(y.label);
	}

	public class Dialog_ApparelBlacklist : Window
	{
		public override Vector2 InitialSize => new(320, 480);

		private readonly List<ThingDef> things;
		private readonly SettingsGroup settings;
		private Vector2 scrollPosition = Vector2.zero;

		public Dialog_ApparelBlacklist(SettingsGroup settings)
		{
			this.settings = settings;

			things = ZombieGenerator.AllApparel[false].SelectMany(a => a.Value).Select(t => t.thing).Distinct().ToList();
			things.Sort(new ThingDefComparer());

			_ = this.settings.blacklistedApparel.RemoveAll(apparel => things.Any(thing => thing.defName == apparel) == false);

			doCloseButton = true;
			absorbInputAroundWindow = true;
		}

		public override void PreClose()
		{
			Tools.UpdateBiomeBlacklist(settings.biomesWithoutZombies);
		}

		public override void DoWindowContents(Rect inRect)
		{
			inRect.yMax -= 60;

			var header = "BlacklistedApparel".SafeTranslate();
			var num = Text.CalcHeight(header, inRect.width);
			Widgets.Label(new Rect(inRect.xMin, inRect.yMin, inRect.width, num), header);
			inRect.yMin += num + 8;

			var outerRect = new Rect(inRect.x, inRect.y, inRect.width, inRect.height - 42);
			var innerRect = new Rect(0f, 0f, inRect.width - 24f, things.Count * (2 + Text.LineHeight));
			Widgets.BeginScrollView(outerRect, ref scrollPosition, innerRect, true);

			var list = new Listing_Standard();
			list.Begin(innerRect);
			foreach (var thing in things)
			{
				var on = settings.blacklistedApparel.Contains(thing.defName);
				var wasOn = on;
				list.Dialog_Checkbox(thing.LabelCap, ref on, true, false, thing.description);
				if (on && wasOn == false)
					settings.blacklistedApparel.Add(thing.defName);
				if (on == false && wasOn)
					_ = settings.blacklistedApparel.Remove(thing.defName);
			}
			list.End();

			Widgets.EndScrollView();

			var rect = new Rect(outerRect.x, outerRect.yMax + 12, outerRect.width, 30);
			var buttonWidth = rect.width / 2 - 10;

			if (Widgets.ButtonText(rect.LeftPartPixels(buttonWidth), "SelectAll".SafeTranslate()))
			{
				settings.blacklistedApparel.Clear();
				settings.blacklistedApparel.AddRange(things.Select(thing => thing.defName));
			}
			if (Widgets.ButtonText(rect.RightPartPixels(buttonWidth), "DeselectAll".SafeTranslate()))
				settings.blacklistedApparel.Clear();
		}
	}
}