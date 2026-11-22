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

		// The sliders edit local values. During a Multiplayer session they are sent when the drag ends.
		private float intensity;
		private int intervalTicks;
		private float sentIntensity;
		private int sentIntervalTicks;

		public Dialog_ThumperSettings(ZombieThumper thumper)
		{
			this.thumper = thumper;
			intensity = sentIntensity = thumper.intensity;
			intervalTicks = sentIntervalTicks = thumper.intervalTicks;
		}

		void SubmitChanges(bool force)
		{
			if (intensity == sentIntensity && intervalTicks == sentIntervalTicks)
				return;
			if (force == false && MultiplayerSupport.IsActive && Input.GetMouseButton(0))
				return;
			sentIntensity = intensity;
			sentIntervalTicks = intervalTicks;
			MultiplayerCommands.SetThumperSettings(thumper, intensity, intervalTicks);
		}

		public override void PostClose()
		{
			base.PostClose();
			if (thumper.Map != null)
				SubmitChanges(true);
		}

		public override void DoWindowContents(Rect inRect)
		{
			if (thumper.Map == null)
			{
				Close(false);
				return;
			}

			inRect.yMin += 5f;

			Rect rect2 = inRect.TopPartPixels(30f);
			intensity = Widgets.HorizontalSlider(rect2, intensity, 0f, 1f, true, null, "Intensity".Translate(), $"{intensity:P0}", 0.01f);

			rect2.y += 40f;
			var oldThumpsPerHour = Tools.Boxed(Mathf.FloorToInt((float)GenDate.TicksPerHour / intervalTicks + 0.5f), 1, 25);
			var label = $"{oldThumpsPerHour}x per hour";
			var thumpsPerHour = (int)Widgets.HorizontalSlider(rect2, oldThumpsPerHour, 1, 25, true, null, "Interval".Translate(), label, 1f);
			if (thumpsPerHour != oldThumpsPerHour)
				intervalTicks = Mathf.FloorToInt((float)GenDate.TicksPerHour / thumpsPerHour + 0.5f);
			SubmitChanges(false);

			rect2 = new Rect(inRect.x + inRect.width / 2f, inRect.yMax - 30f, inRect.width / 2f, 30f);
			if (Widgets.ButtonText(rect2, "CloseButton".Translate(), true, true, true, null))
				Close(true);
		}
	}
}
