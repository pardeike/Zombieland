using RimWorld;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	class Dialog_Settings : Page
	{
		public override string PageTitle => "ZombielandGameSettings".Translate();

		public override void PreOpen()
		{
			base.PreOpen();
			DialogTimeHeader.Reset();
		}

		public override void DoWindowContents(Rect inRect)
		{
			DrawPageTitle(inRect);
			var mainRect = GetMainRect(inRect, 0f, false);
			var idx = DialogTimeHeader.selectedKeyframe;
			var ticks = DialogTimeHeader.currentTicks;
			if (idx != -1)
				SettingsDialog.DoWindowContentsInternal(ref ZombieSettings.ValuesOverTime[idx].values, ref ZombieSettings.ValuesOverTime, mainRect);
			else
			{
				var settings = ZombieSettings.CalculateInterpolation(ZombieSettings.ValuesOverTime, ticks);
				SettingsDialog.DoWindowContentsInternal(ref settings, ref ZombieSettings.ValuesOverTime, mainRect);
			}
			DoBottomButtons(inRect, null, null, null, true, true);
		}
	}
}