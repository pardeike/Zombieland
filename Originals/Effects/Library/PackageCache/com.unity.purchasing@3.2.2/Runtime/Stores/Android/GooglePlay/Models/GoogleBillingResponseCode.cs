namespace UnityEngine.Purchasing.Models
{
    /// <summary>
    /// Values from Java BillingResponseCode
    /// <a href="https://developer.android.com/reference/com/android/billingclient/api/BillingClient.BillingResponseCode">See more</a>
    /// </summary>
    class GoogleBillingResponseCode
    {
        internal const int k_Ok = 0;
        internal const int k_ServiceUnavailable = 2;
        internal const int k_DeveloperError = 5;
        internal const int k_FatalError = 6;
    }
}
