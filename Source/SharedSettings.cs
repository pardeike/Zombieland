using Firebaser;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace ZombieLand
{
	class UserSettings
	{
		public string Creator;
		public int Downloads;
		public SettingsGroup Settings;
		public string Description;
	}

	class SharedSettings
	{
		// https://console.firebase.google.com/project/zombieland-saves/database/zombieland-saves/data

		private static readonly string auth = "9OQa7vgOzYleREZo89HnU5J3hdlX9qIvjeMf1GQa";

		public static string Save(SettingsGroup settings, string label)
		{
			var userSettings = new UserSettings()
			{
				Creator = SteamUtility.SteamPersonaName,
				Downloads = 0,
				Settings = settings,
				Description = ""
			};
			var client = new Connector("zombieland-saves", auth);
			return client.Put("/Settings/" + label, userSettings);
		}

		public static SettingsGroup Load(string label)
		{
			var client = new Connector("zombieland-saves", auth);
			return client.Get<SettingsGroup>("/Settings/" + label);
		}

		public static IEnumerable<string> Labels()
		{
			var client = new Connector("zombieland-saves", auth);
			return client.Get<Dictionary<string, bool>>("/Settings/", true)
				.Keys.OrderBy(s => s).AsEnumerable();
		}
	}
}