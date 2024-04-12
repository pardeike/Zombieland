using System;
using UnityEngine.Purchasing.Extension;

namespace UnityEngine.Purchasing
{
    /// <summary>
    /// Access Google Play store specific configurations.
    /// </summary>
    public interface IGooglePlayConfiguration: IStoreConfiguration
    {
        /// <summary>
        /// SetPublicKey is deprecated, nothing will be returns and no code will be executed.
        /// </summary>
        /// <param name="key">deprecated, nothing will be returns and no code will be executed.</param>
        [Obsolete("SetPublicKey is deprecated, nothing will be returns and no code will be executed. Will be removed soon.")]
        void SetPublicKey(string key);

        /// <summary>
        /// aggressivelyRecoverLostPurchases is deprecated, nothing will be returns and no code will be executed.
        /// </summary>
        [Obsolete("aggressivelyRecoverLostPurchases is deprecated, nothing will be returns and no code will be executed. Will be removed soon.")]
        bool aggressivelyRecoverLostPurchases { get; set; }

        /// <summary>
        /// UsePurchaseTokenForTransactionId is deprecated, nothing will be returns and no code will be executed.
        /// </summary>
        /// <param name="usePurchaseToken">deprecated, nothing will be returns and no code will be executed.</param>
        [Obsolete("UsePurchaseTokenForTransactionId is deprecated, nothing will be returns and no code will be executed. Will be removed soon.")]
        void UsePurchaseTokenForTransactionId(bool usePurchaseToken);

        /// <summary>
        /// Set an optional listener for failures when connecting to the base Google Play Billing service. This may be called
        /// after <typeparamref name="UnityPurchasing.Initialize"/> if a user does not have a Google account added to their
        /// Android device.
        /// 
        /// This listener can be used to learn that initialization is paused, and the user must add a Google account
        /// in order to be able to purchase and to download previous purchases. Adding a valid account will allow
        /// the initialization to resume.
        /// </summary>
        /// <param name="action">Will be called when <typeparamref name="UnityPurchasing.Initialize"/>
        ///     is interrupted by a disconnection from the Google Play Billing service.</param>
        void SetServiceDisconnectAtInitializeListener(Action action);
    }
}
