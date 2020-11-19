using UnityEngine;
using Verse;

namespace ZombieLand
{
	class Dialog_PublishSettings : Window
	{
		private string name = "";
		private string description = "";
		private readonly SettingsGroup settings;

		public Dialog_PublishSettings(SettingsGroup settings)
		{
			doCloseButton = true;
			absorbInputAroundWindow = true;
			this.settings = settings;
		}

		public static void Present(SettingsGroup settings)
		{
			Find.WindowStack.Add(new Dialog_PublishSettings(settings));
		}

		public override Vector2 InitialSize => new Vector2(400f, 400f);

		public override void DoWindowContents(Rect inRect)
		{
			var listing = new Listing_Standard();
			listing.Begin(inRect);
			_ = listing.Label("SettingsName".Translate());
			name = listing.TextEntry(name);
			listing.Gap(4f);
			_ = listing.Label("Description".Translate());
			description = listing.TextEntry(description, 4);
			listing.Gap(8f);
			if (listing.ButtonText("PublishSettingsButton".Translate()))
				Save();
			listing.End();
		}

		private void Save()
		{
			_ = SharedSettings.Save(settings, name, description);
			Close(true);
		}
	}
}
