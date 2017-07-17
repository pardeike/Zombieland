using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using Verse;

namespace ZombieLand
{
	/*
	[HarmonyPatch(typeof(Translator))]
	[HarmonyPatch("PseudoTranslated")]
	static class Translator_PseudoTranslated_Patch
	{
		static bool Prefix(string original, ref string __result)
		{
			var lang = LanguageDatabase.activeLanguage;
			if (lang.folderName == SystemLanguage.Unknown.ToString())
			{
				Log.Warning("# unknown language '" + lang.folderName + "'");
				return true;
			}

			var translation = GoogleTranslator.TranslateOnline(original, lang.folderName);
			if (translation != null && translation.Length > 0)
			{
				__result = translation;
				return false;
			}

			return true;
		}
	}
	*/

	public static class GoogleTranslator
	{
		static GoogleTranslator()
		{
			languageTokens = new Dictionary<string, string>();
			foreach (var token in tokens)
			{
				var parts = token.Split(';');
				languageTokens[parts[0]] = parts[1];
			}

			cache = new Dictionary<string, string>();
			// TODO: load cache from file
		}

		static Dictionary<string, string> languageTokens;
		static string[] tokens = new string[] {
			"Afrikaans;af", "Arabic;ar", "Basque;eu",
			"Belarusian;be", "Bulgarian;bg", "Catalan;ca",
			"Chinese;ch", "Czech;cs", "Danish;da",
			"Dutch;nl", "English;en", "Estonian;et",
			"Faroese;fo", "Finnish;fi", "French;fr",
			"German;de", "Greek;el", "Hebrew;he",
			"Icelandic;is", "Indonesian;id", "Italian;it",
			"Japanese;ja", "Korean;ko", "Latvian;lv",
			"Lithuanian;lt", "Norwegian;no", "Polish;pl",
			"Portuguese;pt", "Romanian;ro", "Russian;ru",
			"SerboCroatian;hr", "Slovak;sk", "Slovenian;sl",
			"Spanish;es", "Swedish;sv", "Thai;th",
			"Turkish;tr", "Ukrainian;uk", "Vietnamese;vi",
			"ChineseSimplified;zh", "ChineseTraditional;zh", "Hungarian;hu"
			};

		static Dictionary<string, string> cache;

		const string GoogleTranslateUrl = "http://www.google.com/translate_t?hl=en&ie=UTF8&text={0}&langpair=en|{1}";
		const string GoogleResultSpawnExpression = "<span.*? id=\"?result_box\"?.*? class=\"?(?:long_text|short_text)\"?.*?>.*?<span.*?>(.*?)</span>";

		public static string TranslateOnline(string input, string language)
		{
			var token = languageTokens[language];
			var key = token + ":" + input;

			string cachedResult = null;
			if (cache.TryGetValue(key, out cachedResult) && cachedResult != null && cachedResult.Length > 0)
				return cachedResult;

			var url = string.Format(GoogleTranslateUrl, input, token);
			Log.Warning("# " + key + " => " + url);

			var saved = ServicePointManager.ServerCertificateValidationCallback;
			var result = "";
			try
			{
				ServicePointManager.ServerCertificateValidationCallback = delegate (
					object s,
					X509Certificate certificate,
					X509Chain chain,
					SslPolicyErrors sslPolicyErrors)
				{ return true; };

				var webClient = new WebClient() { Encoding = Encoding.GetEncoding("iso-8859-1") };
				var html = webClient.DownloadString(url);
				var rx = new Regex(GoogleResultSpawnExpression, RegexOptions.Singleline | RegexOptions.IgnoreCase);
				var matches = rx.Matches(html);
				if (matches.Count != 1)
				{
					Log.Error("HTML: " + html);
					throw new Exception("matches.Count is " + matches.Count);
				}
				var groups = matches[0].Groups;
				if (groups.Count != 2)
				{
					Log.Error("HTML: " + html);
					throw new Exception("groups.Count is " + groups.Count);
				}
				result = RestSharp.Contrib.HttpUtility.HtmlDecode(groups[1].Value);
			}
			catch (Exception e)
			{
				Log.Error("Cannot load " + url + ": " + e);
				result = "";
			}
			finally
			{
				ServicePointManager.ServerCertificateValidationCallback = saved;
			}

			Log.Warning(key + " => " + result);
			cache[key] = result;
			// TODO: save cache to file

			return result;
		}
	}

}