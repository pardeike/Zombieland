using Firebaser;
using System;
using System.Threading;
using Verse;

namespace ZombieLand
{
	public class UserLaunchStats
	{
		public int load;
		public int used;
		public DateTime last;
	}

	public class FireStats
	{
		private static readonly string db = "brrainz-mod-stats";
		private static readonly string auth = "0n7NV7z7H4KiJhD17fUXDROUPuLNB67hheNT3U6v";

		public static void Trigger(bool startup)
		{
			new Thread(delegate ()
			{
				var client = new Connector(db, auth);
				var date = DateTime.Today.ToString("yyyy-MM-dd");
				var user = SteamUtility.SteamPersonaName;
				if (user == null || user.Length == 0) user = "__unknown";
				var path = "/" + typeof(FireStats).Namespace + "/" + date + "/" + user;
				var stats = client.Get<UserLaunchStats>(path) ?? new UserLaunchStats() { load = 0, used = 0, last = DateTime.Now };
				if (startup) stats.load++; else stats.used++;
				stats.last = DateTime.Now;
				client.Put(path, stats);
			}).Start();
		}
	}
}