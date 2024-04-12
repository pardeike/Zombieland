using System;
using UnityEngine.Purchasing.Extension;

namespace UnityEngine.Purchasing
{
	/// <summary>
	/// Store configuration for Android stores.
	/// </summary>
    public interface IAndroidStoreSelection : IStoreConfiguration
	{
        /// <summary>
        /// A property that retrieves the <c>AndroidStore</c> type, if applicabble.
        /// </summary>
	    AndroidStore androidStore { get; }
	    /// <summary>
	    /// A property that retrieves the <c>AppStore</c> type.
	    /// </summary>
        AppStore appStore { get; }
	}
}
