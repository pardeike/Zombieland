using UnityEngine.Purchasing.Extension;

namespace UnityEngine.Purchasing
{
    /// <summary>
    /// Store configuration for Samsung Apps.
    /// </summary>
	public interface ISamsungAppsConfiguration : IStoreConfiguration
	{
        /// <summary>
        /// Sets the purchase mode for testing or shipping Samsung Apps.
        /// </summary>
        /// <param name="mode"> The way in which store purchases will behave. See <c>SamsungAppsMode</c> </param>
		void SetMode(SamsungAppsMode mode);
	}
}
