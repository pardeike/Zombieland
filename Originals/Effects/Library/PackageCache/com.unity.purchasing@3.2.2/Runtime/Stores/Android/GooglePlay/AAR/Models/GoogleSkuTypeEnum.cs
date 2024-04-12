using UnityEngine;

namespace UnityEngine.Purchasing.Models
{
    /// <summary>
    /// This is C# representation of the Java Class SkuType
    /// <a href="https://developer.android.com/reference/com/android/billingclient/api/BillingClient.SkuType">See more</a>
    /// </summary>
    class GoogleSkuTypeEnum
    {
        const string k_AndroidSkuTypeClassName = "com.android.billingclient.api.BillingClient$SkuType";

        static AndroidJavaObject GetSkuTypeJavaObject()
        {
            return new AndroidJavaClass(k_AndroidSkuTypeClassName);
        }

        internal static string InApp()
        {
            return GetSkuTypeJavaObject().GetStatic<string>("INAPP");
        }

        internal static string Sub()
        {
            return GetSkuTypeJavaObject().GetStatic<string>("SUBS");
        }
    }
}
