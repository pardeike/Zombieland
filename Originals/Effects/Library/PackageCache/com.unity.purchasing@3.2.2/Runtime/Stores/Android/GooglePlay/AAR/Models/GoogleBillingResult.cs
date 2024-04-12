using UnityEngine;

namespace UnityEngine.Purchasing.Models
{
    /// <summary>
    /// This is C# representation of the Java Class BillingResult
    /// <a href="https://developer.android.com/reference/com/android/billingclient/api/BillingResult">See more</a>
    /// </summary>
    class GoogleBillingResult
    {
        internal int responseCode;
        internal string debugMessage;
        internal GoogleBillingResult(AndroidJavaObject billingResult)
        {
            if (billingResult != null)
            {
                responseCode = billingResult.Call<int>("getResponseCode");
                debugMessage = billingResult.Call<string>("getDebugMessage");
            }
        }
    }
}
