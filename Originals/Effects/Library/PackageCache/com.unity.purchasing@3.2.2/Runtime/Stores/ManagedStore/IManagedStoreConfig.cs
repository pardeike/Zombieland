using UnityEngine.Purchasing.Extension;

namespace UnityEngine.Purchasing
{
    /// <summary>
    /// Currently unused. An enum of different store test
    /// </summary>
    public enum StoreTestMode
    {
        /// <summary>
        /// Normal store operation using real transactions
        /// </summary>
        Normal,

        /// <summary>
        /// Sandbox store operation, using simulated transactions on the native platform.
        /// </summary>
        Sandbox,

        /// <summary>
        /// Test mode, using local test responses at the level of Unity IAP.
        /// </summary>
        TestMode,

        /// <summary>
        /// Test mode, using server-side test responses at the level of Unity IAP.
        /// </summary>
        ServerTest,

        /// <summary>
        /// Unknown purchasing mode.
        /// </summary>
        Unknown,
    };


    /// <summary>
    /// IAP E-Commerce Managed Store Configuration
    /// </summary>
    public interface IManagedStoreConfig : IStoreConfiguration
    {
        /// <summary>
        /// Whether or not the store catalog is disabled.
        /// </summary>
        bool disableStoreCatalog { get; set; }

        /// <summary>
        /// Whether or the tracking has been opted out for Promo products.
        /// </summary>
        bool? trackingOptOut { get; set; }

        // Test Mode Options -- the following should all latch TestEnabled to true

        /// <summary>
        /// Whether or not store testing is enabled.
        /// </summary>
        bool storeTestEnabled { get; set; }

        /// <summary>
        /// The base URL for the e-Commerce portal.
        /// </summary>
        string baseIapUrl { get; set; }

        /// <summary>
        /// The base URL for web request events.
        /// </summary>
        string baseEventUrl { get; set; }
    }

}
