using Firebaser;
using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace ZombieLand
{
	class UserSettings
	{
		public string Name;
		public string Creator;
		public string Description;
		public SettingsGroup Settings;
		public int Downloads;
	}

	class DownloadIncrement
	{
		public int Downloads;
	}

	class SharedSettings
	{
		private static readonly string db = "zombieland-saves";
		private static readonly string a1 = "9OQa7vgOzYleRE";
		private static readonly string a2 = "Zo89HnU5J3hdlX9";
		private static readonly string a3 = "qIvjeMf1GQa";
		private static readonly Connector client = new Connector(db, a1 + a2 + a3);
		private static bool SimpleChar(char c) { return "-abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".Contains(c); }
		private static string Cleaned(string s) { return new string(s.Where(c => SimpleChar(c)).ToArray()); }

		public static string Key(string label, string steamName)
		{
			return Cleaned(label) + "_" + Cleaned(steamName);
		}

		public static bool HasConnectivity()
		{
			return client.IsAvailable();
		}

		public static string Save(SettingsGroup settings, string label, string description)
		{
			var currentSteamUser = SteamUtility.SteamPersonaName;
			var userSettings = new UserSettings()
			{
				Name = label,
				Creator = currentSteamUser,
				Description = description,
				Settings = settings,
				Downloads = 0
			};
			var path = "/Settings/" + Key(label, currentSteamUser);
			try
			{
				return client.Put(path, userSettings);
			}
			catch (Exception e)
			{
				Log.Error("Exception " + e + " while saving to " + path);
			}
			return null;
		}

		public static UserSettings Load(string label, string steamName)
		{
			var path = "/Settings/" + Key(label, steamName);
			try
			{
				var settings = client.Get<UserSettings>(path);
				if (settings != null)
					_ = client.Patch(path, new DownloadIncrement() { Downloads = settings.Downloads + 1 });
				return settings;
			}
			catch (Exception e)
			{
				Log.Error("Exception " + e + " while loading from " + path);
			}
			return null;
		}

		public static void Delete(string label, string steamName)
		{
			var path = "/Settings/" + Key(label, steamName);
			try
			{
				_ = client.Delete(path);
			}
			catch (Exception e)
			{
				Log.Error("Exception " + e + " while deleting " + path);
			}
		}

		public static IEnumerable<UserSettings> GetAll()
		{
			return client.Get<Dictionary<string, UserSettings>>("/Settings/")?
				.Values.AsEnumerable() ?? new List<UserSettings>();
		}
	}
}