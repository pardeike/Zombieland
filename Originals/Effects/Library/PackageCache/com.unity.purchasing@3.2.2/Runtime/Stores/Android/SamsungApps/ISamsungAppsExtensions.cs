using System;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Extension;

namespace UnityEngine.Purchasing
{
	/// <summary>
	/// Access Samsung Apps specific functionality.
	/// </summary>
	public interface ISamsungAppsExtensions : IStoreExtension
	{
        /// <summary>
        /// Restores previously purchased transactions.
        /// </summary>
        /// <param name="callback"> The callback received after restoring transactions. </param>
		void RestoreTransactions(Action<bool> callback);
	}
}
