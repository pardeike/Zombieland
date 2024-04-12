namespace UnityEngine.Purchasing.Models
{
    /// <summary>
    /// This is C# representation of the Java Class BillingResponseCode
    /// <a href="https://developer.android.com/reference/com/android/billingclient/api/BillingClient.BillingResponseCode">See more</a>
    /// </summary>
    static class BillingClientResponseEnum
    {
        const string k_AndroidBillingClientResponseCodeClassName = "com.android.billingclient.api.BillingClient$BillingResponseCode";

        static AndroidJavaObject GetBillingResponseCodeJavaObject()
        {
            return new AndroidJavaClass(k_AndroidBillingClientResponseCodeClassName);
        }

        internal static int OK()
        {
            return GetBillingResponseCodeJavaObject().GetStatic<int>("OK");
        }

        internal static int USER_CANCELED()
        {
            return GetBillingResponseCodeJavaObject().GetStatic<int>("USER_CANCELED");
        }

        internal static int SERVICE_UNAVAILABLE()
        {
            return GetBillingResponseCodeJavaObject().GetStatic<int>("SERVICE_UNAVAILABLE");
        }

        internal static int ITEM_ALREADY_OWNED()
        {
            return GetBillingResponseCodeJavaObject().GetStatic<int>("ITEM_ALREADY_OWNED");
        }
    }
}
