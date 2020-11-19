using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace ZombieLand
{
	class Dialog_UseSettings : Window
	{
		private string searchTerm = "";
		private List<UserSettings> allSettings = new List<UserSettings>();
		private readonly SettingsGroup settings;

		public Dialog_UseSettings(SettingsGroup settings)
		{
			scrollPosition = Vector2.zero;
			doCloseButton = true;
			absorbInputAroundWindow = true;
			RefreshList();
			this.settings = settings;
		}

		private void RefreshList()
		{
			allSettings = SharedSettings.GetAll().ToList();
			if (allSettings == null)
				allSettings = new List<UserSettings>();
			if (allSettings.Count > 100)
				allSettings = allSettings.OrderByDescending(us => us.Downloads).Take(100).ToList();
		}

		public static void Present(SettingsGroup settings)
		{
			Find.WindowStack.Add(new Dialog_UseSettings(settings));
		}

		public override Vector2 InitialSize => new Vector2(850f, 600f);

		static Vector2 scrollPosition = Vector2.zero;
		public override void DoWindowContents(Rect inRect)
		{
			const float rowHeight = 64f;
			var bottomSpace = CloseButSize.y + 8f;

			var listing = new Listing_Standard();
			listing.Begin(inRect);

			_ = listing.Label("SearchSettings".Translate());
			searchTerm = listing.TextEntry(searchTerm);
			var showTopOnly = searchTerm.Length == 0;
			listing.Gap(10f);
			if (showTopOnly)
			{
				_ = listing.Label("TOP5".Translate());
				listing.Gap(10f);
			}
			var currentSteamUser = SteamUtility.SteamPersonaName;
			var rows = showTopOnly ? allSettings.Where(us => us.Downloads > 0).OrderByDescending(us => us.Downloads).Take(5) : allSettings.Where(us =>
			{
				if (us.Creator.ToLower().Contains(searchTerm.ToLower()))
					return true;
				if (us.Name.ToLower().Contains(searchTerm.ToLower()))
					return true;
				return false;
			}).OrderByDescending(us => us.Downloads);

			var outRect = listing.GetRect(inRect.height - listing.CurHeight - bottomSpace);
			var scrollRect = new Rect(0f, 0f, inRect.width - 16f, rowHeight * rows.Count());
			Widgets.BeginScrollView(outRect, ref scrollPosition, scrollRect, true);
			var rowRect = scrollRect;
			rowRect.height = rowHeight;
			var counter = 0;
			foreach (var row in rows)
			{
				var isCurrentUser = row.Creator == currentSteamUser;
				counter++;

				if (counter > 1)
				{
					var rect0 = rowRect;
					rect0.yMin--;
					rect0.height = 1f;
					GUI.DrawTexture(rect0, BaseContent.GreyTex);
				}

				var rect1 = rowRect;
				rect1.width = 24f;
				rect1.height = 24f;
				if (isCurrentUser)
					rect1.y = (rowRect.yMin + rowRect.yMax) / 2f - rect1.height - 2f;
				else
					rect1.y += (rowHeight - rect1.height) / 2f;
				if (Widgets.ButtonText(rect1, "✔"))
					ChooseName(row.Name, row.Creator);

				if (isCurrentUser)
				{
					rect1.y = (rowRect.yMin + rowRect.yMax) / 2f + 2f;
					if (Widgets.ButtonText(rect1, "X"))
						Delete(row.Name, row.Creator);
				}

				var rect2 = rowRect;
				rect2.xMin += rect1.width + 14f;
				rect2.width *= 0.85f;
				Text.Anchor = TextAnchor.MiddleLeft;
				Widgets.Label(rect2, (showTopOnly ? counter + ". " : "") + row.Name + "\n" + row.Description);

				var rect3 = rowRect;
				rect3.xMin += rect2.width;
				Text.Anchor = TextAnchor.MiddleRight;
				Widgets.Label(rect3, row.Creator + "\n✔ " + row.Downloads);

				Text.Anchor = TextAnchor.UpperLeft;
				rowRect.y += rowHeight;
			}
			Widgets.EndScrollView();
			listing.End();
		}

		private void ChooseName(string label, string steamName)
		{
			var newSettings = SharedSettings.Load(label, steamName)?.Settings;
			if (newSettings != null)
			{
				AccessTools.GetFieldNames(settings).Do(name2 =>
				{
					var finfo = AccessTools.Field(typeof(SettingsGroup), name2);
					finfo.SetValue(settings, finfo.GetValue(newSettings));
				});
				Close(true);
				Dialogs.scrollPosition = Vector2.zero;
			}
			else
				Log.Error("Cannot load " + label + " by " + steamName);
		}

		private void Delete(string label, string steamName)
		{
			SharedSettings.Delete(label, steamName);
			RefreshList();
		}
	}
}
