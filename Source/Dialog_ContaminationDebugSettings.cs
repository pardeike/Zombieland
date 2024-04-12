using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class Dialog_ContaminationDebugSettings : Window
	{
		private const float BotAreaHeight = 30f;
		private const float NumberYOffset = 10f;
		private readonly Pawn pawn;
		private readonly List<(MethodInfo, ContaminationRangeAttribute)> infos;
		private MethodInfo selected;
		private float factor = 0.5f;

		public override Vector2 InitialSize => new(300f, 200f);
		public override float Margin => 10f;

		public Dialog_ContaminationDebugSettings(Pawn pawn)
		{
			absorbInputAroundWindow = true;
			focusWhenOpened = true;
			doCloseX = true;
			closeOnCancel = true;
			this.pawn = pawn;

			infos = AccessTools.GetDeclaredMethods(typeof(ContaminationEffect))
				.Select(method => (method, method.GetCustomAttributes(typeof(ContaminationRangeAttribute), false).FirstOrDefault() as ContaminationRangeAttribute))
				.Where(pair => pair.Item2 is not null)
				.ToList();
		}

		public override void DoWindowContents(Rect inRect)
		{
			if (pawn?.Map == null)
			{
				Close(false);
				return;
			}

			inRect.yMin += 5f;

			var list = new Listing_Standard();
			list.Begin(inRect);

			if (list.ButtonText(selected?.Name ?? ""))
			{
				var options = infos.Select(pair =>
				{
					var matches = pair.Item1.Equals(selected);
					return new FloatMenuOption($"{pair.Item1.Name}{(matches ? " ✓" : "")}", () => selected = pair.Item1);
				}).ToList();
				Find.WindowStack.Add(new FloatMenu(options));
			}
			list.Gap();

			var rect = list.GetRect(20);
			factor = Widgets.HorizontalSlider(rect, factor, 0f, 1f, true, null, "Factor", $"{factor:P0}", 0.01f);
			list.Gap();

			if (list.ButtonText("Apply"))
			{
				selected?.Invoke(null, new object[] { pawn, factor });
				Close(true);
			}
			list.Gap();

			if (list.ButtonText("Close"))
				Close(true);

			list.End();
		}
	}
}