using System;

namespace UnityEngine.Purchasing
{
    /// <summary>
    /// Mock implementation for Samsung interfaces for stub and test purposes.
    /// </summary>
	public class FakeSamsungAppsExtensions : ISamsungAppsExtensions, ISamsungAppsConfiguration
	{
        /// <summary>
        /// Sets the purchase mode for testing or shipping Samsung Apps.
        /// </summary>
        /// <param name="mode"> The way in which store purchases will behave. See <c>SamsungAppsMode</c> </param>
		public void SetMode(SamsungAppsMode mode)
		{
		}

        /// <summary>
        /// Restores previously purchased transactions.
        /// </summary>
        /// <param name="callback"> The callback received after restoring transactions. </param>
		public void RestoreTransactions(Action<bool> callback)
		{
			callback(true);
		}
	}
}

