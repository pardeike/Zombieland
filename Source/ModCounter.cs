using System;

public static class ModCounter
{
	// a very simple and gdpr friendly mod launch counter.
	// no personal information is transfered and firebase
	// doesn't store the IP or any other traceable information

	const string baseUrl = "http://us-central1-brrainz-mod-stats.cloudfunctions.net/ping?";
	public static void Trigger()
	{
		try
		{
			var uri = new Uri(baseUrl + "Zombieland");
			using (var client = new System.Net.WebClient())
				client.DownloadStringAsync(uri);
		}
		catch
		{
		}
	}
}