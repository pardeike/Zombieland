using System;
using System.Collections.Generic;

namespace UnityEngine.Purchasing
{
    /// <summary>
    /// IAP E-Commerce Managed Store functionality.
    /// </summary>
    public interface IManagedStoreExtensions : IStoreExtension
    {
        /// <summary>
        /// Get the available store catalog items.
        /// Store catalog prioritizes its array of products in the same order as the IAP E-Commerce API response.
        /// Any product that is not available for purchase nor found in the native store will not be in the list.
        /// </summary>
        Product[] storeCatalog { get; }

        /// <summary>
        /// Refresh the managed store catalog.
        /// </summary>
        /// <param name="callback"> The action executed upon refreshing the catalog. </param>
        void RefreshCatalog(Action callback);
    }
}
