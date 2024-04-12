using System;
using UnityEngine.Purchasing.Models;
using UnityEngine.Scripting;

namespace UnityEngine.Purchasing
{
    /// <summary>
    /// This is C# representation of the Java Class GooglePriceChangeConfirmationListener
    /// <a href="https://developer.android.com/reference/com/android/billingclient/api/PriceChangeConfirmationListener">See more</a>
    /// </summary>
    class GooglePriceChangeConfirmationListener : AndroidJavaProxy
    {
        const string k_AndroidPriceChangeConfirmationListenerClassName = "com.android.billingclient.api.PriceChangeConfirmationListener";

        Action<GoogleBillingResult> m_OnPriceChangeConfirmationResult;

        internal GooglePriceChangeConfirmationListener(Action<GoogleBillingResult> onPriceChangeConfirmationResult)
            : base(k_AndroidPriceChangeConfirmationListenerClassName)
        {
            m_OnPriceChangeConfirmationResult = onPriceChangeConfirmationResult;
        }

        [Preserve]
        void onPriceChangeConfirmationResult(AndroidJavaObject javaBillingResult)
        {
            GoogleBillingResult billingResult = new GoogleBillingResult(javaBillingResult);
            m_OnPriceChangeConfirmationResult(billingResult);
        }
    }
}
