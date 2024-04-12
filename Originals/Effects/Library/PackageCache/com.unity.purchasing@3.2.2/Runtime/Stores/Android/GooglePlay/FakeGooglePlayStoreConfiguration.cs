using System;

namespace UnityEngine.Purchasing
{
    /// <summary>
    /// Access Google Play store specific configurations.
    /// </summary>
    public class FakeGooglePlayStoreConfiguration : IGooglePlayConfiguration
    {
        /// <summary>
        /// SetPublicKey is deprecated, nothing will be returns and no code will be executed.
        /// </summary>
        /// <param name="key">deprecated, nothing will be returns and no code will be executed.</param>
        public void SetPublicKey(string key) { }

        /// <summary>
        /// aggressivelyRecoverLostPurchases is deprecated, nothing will be returns and no code will be executed.
        /// </summary>
        public bool aggressivelyRecoverLostPurchases { get; set; }

        /// <summary>
        /// UsePurchaseTokenForTransactionId is deprecated, nothing will be returns and no code will be executed.
        /// </summary>
        /// <param name="usePurchaseToken">deprecated, nothing will be returns and no code will be executed.</param>
        public void UsePurchaseTokenForTransactionId(bool usePurchaseToken) { }

        /// <summary>
        /// THIS IS A FAKE, NO CODE WILL BE EXECUTED!
        ///
        /// Set an optional listener for failures when connecting to the base Google Play Billing service.
        /// </summary>
        /// <param name="action">Will never be called because this is a fake.</param>
        public void SetServiceDisconnectAtInitializeListener(Action action) { }
    }
}
