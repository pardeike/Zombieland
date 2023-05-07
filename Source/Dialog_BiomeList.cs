using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class Dialog_BiomeList : Window
	{
		public List<(BiomeDef def, TaggedString name)> allBiomes;
		public override Vector2 InitialSize => new(320, 480);

		private readonly SettingsGroup settings;
		private Vector2 scrollPosition = Vector2.zero;

		public Dialog_BiomeList(SettingsGroup settings)
		{
			this.settings = settings;

			var sosOuterSpaceBiomeDefName = SoSTools.sosOuterSpaceBiomeDef?.defName;
			if (sosOuterSpaceBiomeDefName != null)
				if (settings.biomesWithoutZombies.Contains(sosOuterSpaceBiomeDefName) == false)
					_ = settings.biomesWithoutZombies.Add(sosOuterSpaceBiomeDefName);

			doCloseButton = true;
			absorbInputAroundWindow = true;
			allBiomes = DefDatabase<BiomeDef>.AllDefsListForReading
				.Select(def => (def, name: def.LabelCap))
				.OrderBy(item => item.name.ToString())
				.ToList();
		}

		public override void PreClose()
		{
			Tools.UpdateBiomeBlacklist(settings.biomesWithoutZombies);
		}

		public override void DoWindowContents(Rect inRect)
		{
			inRect.yMax -= 60;

			var header = "BlacklistedBiomes".SafeTranslate();
			var num = Text.CalcHeight(header, inRect.width);
			Widgets.Label(new Rect(inRect.xMin, inRect.yMin, inRect.width, num), header);
			inRect.yMin += num + 8;

			var outerRect = new Rect(inRect.x, inRect.y, inRect.width, inRect.height);
			var innerRect = new Rect(0f, 0f, inRect.width - 24f, allBiomes.Count * (2 + Text.LineHeight));
			Widgets.BeginScrollView(outerRect, ref scrollPosition, innerRect, true);

			var list = new Listing_Standard();
			list.Begin(innerRect);
			foreach (var (def, name) in allBiomes)
			{
				var defName = def.defName;
				var on = settings.biomesWithoutZombies.Contains(defName);
				var wasOn = on;
				list.Dialog_Checkbox(name, ref on, true, def == SoSTools.sosOuterSpaceBiomeDef);
				if (on && wasOn == false)
					_ = settings.biomesWithoutZombies.Add(defName);
				if (on == false && wasOn)
					_ = settings.biomesWithoutZombies.Remove(defName);
			}
			list.End();

			Widgets.EndScrollView();
		}
	}
}