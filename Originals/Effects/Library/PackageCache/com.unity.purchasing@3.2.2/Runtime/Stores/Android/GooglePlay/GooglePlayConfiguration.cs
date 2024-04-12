using System;

namespace UnityEngine.Purchasing
{
    /// <summary>
    /// Access Google Play store specific configurations.
    /// </summary>
    public class GooglePlayConfiguration: IGooglePlayConfiguration
    {
        Action m_InitializationConnectionLister;
        IGooglePlayConfigurationInternal m_GooglePlayConfigurationInternal;

        internal void SetGooglePlayConfigurationInternal(IGooglePlayConfigurationInternal googlePlayConfigurationInternal)
        {
            m_GooglePlayConfigurationInternal = googlePlayConfigurationInternal;
            m_GooglePlayConfigurationInternal.SetGooglePlayConfiguration(this);
        }

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
        /// Set an optional listener for failures when connecting to the base Google Play Billing service. This may be called
        /// after <typeparamref name="UnityPurchasing.Initialize"/> if a user does not have a Google account added to their
        /// Android device.
        /// </summary>
        /// <param name="action">Will be called when <typeparamref name="UnityPurchasing.Initialize"/>
        ///     is interrupted by a disconnection from the Google Play Billing service.</param>
        public void SetServiceDisconnectAtInitializeListener(Action action)
        {
            m_InitializationConnectionLister = action;
        }

        /// <summary>
        /// Internal API, do not use.
        /// </summary>
        public void NotifyInitializationConnectionFailed()
        {
            m_InitializationConnectionLister?.Invoke();
        }
    }
}
