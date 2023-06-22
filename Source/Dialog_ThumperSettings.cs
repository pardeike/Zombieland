using RimWorld;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	public class Dialog_ThumperSettings : Window
	{
		private const float BotAreaHeight = 30f;
		private const float NumberYOffset = 10f;
		private readonly ZombieThumper thumper;

		public override Vector2 InitialSize => new(300f, 140f);
		public override float Margin => 10f;

		public Dialog_ThumperSettings(ZombieThumper thumper)
		{
			this.thumper = thumper;
		}

		public override void DoWindowContents(Rect inRect)
		{
			if(thumper.Map == null)
			{
				Close(false);
				return;
			}

			inRect.yMin += 5f;

			Rect rect2 = inRect.TopPartPixels(30f);
			thumper.intensity = Widgets.HorizontalSlider_NewTemp(rect2, thumper.intensity, 0f, 1f, true, null, "Intensity".Translate(), $"{thumper.intensity:P0}", 0.01f);
			
			rect2.y += 40f;
			var thumpsPerHour = Tools.Boxed(Mathf.FloorToInt((float)GenDate.TicksPerHour / thumper.intervalTicks + 0.5f), 1, 25);
			var label = $"{thumpsPerHour}x per hour";
			thumpsPerHour = (int)Widgets.HorizontalSlider_NewTemp(rect2, thumpsPerHour, 1, 25, true, null, "Interval".Translate(), label, 1f);
			thumper.intervalTicks = Mathf.FloorToInt((float)GenDate.TicksPerHour / thumpsPerHour + 0.5f);

			rect2 = new Rect(inRect.x + inRect.width / 2f, inRect.yMax - 30f, inRect.width / 2f, 30f);
			if (Widgets.ButtonText(rect2, "CloseButton".Translate(), true, true, true, null))
				Close(true);
		}
	}
}