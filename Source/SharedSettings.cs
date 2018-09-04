using fastJSON;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
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
		const string firebase = "https://zombieland-saves.firebaseio.com/";
		const string auth = "9OQa7vgOzYleREZo89HnU5J3hdlX9qIvjeMf1GQa";

		private static RemoteCertificateValidationCallback PauseCertValidation()
		{
			var previousCallback = ServicePointManager.ServerCertificateValidationCallback;
			ServicePointManager.ServerCertificateValidationCallback
				= delegate (object s, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
				{ return true; };
			return previousCallback;
		}

		private static T GetFirebase<T>(string url, bool shallow)
		{
			var previousCallback = PauseCertValidation();
			var query = new NameValueCollection { { "auth", auth }, { "shallow", shallow ? "true" : "false" } };
			var client = new WebClient { QueryString = query, Encoding = Encoding.UTF8 };
			var result = client.DownloadString(firebase + url + ".json");
			ServicePointManager.ServerCertificateValidationCallback = previousCallback;
			return JSON.ToObject<T>(result, new JSONParameters() { UseExtensions = false });
		}

		private static string PostFirebase<T>(string url, T obj)
		{
			var previousCallback = PauseCertValidation();
			var query = new NameValueCollection { { "auth", auth } };
			var client = new WebClient { QueryString = query, Encoding = Encoding.UTF8 };
			var json = JSON.ToJSON(obj, new JSONParameters() { UseExtensions = false });
			var result = client.UploadString(firebase + url + ".json", "PUT", json);
			ServicePointManager.ServerCertificateValidationCallback = previousCallback;
			return result;
		}

		public static string Save(SettingsGroup settings, string label)
		{
			var userSettings = new UserSettings() { Creator = SteamUtility.SteamPersonaName, Downloads = 0, Settings = settings, Description = "" };
			return PostFirebase("Settings/" + label, userSettings);
		}

		public static SettingsGroup Load(string label)
		{
			var data = GetFirebase<UserSettings>("Settings/" + label, false);
			return data.Settings;
		}

		public static IEnumerable<string> Labels()
		{
			return GetFirebase<Dictionary<string, bool>>("Settings/", true)
				.Keys.OrderBy(s => s).AsEnumerable();
		}
	}
}