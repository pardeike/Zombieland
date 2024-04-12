using System.Collections.Generic;
using System.Linq;
using UnityEngine.Purchasing.Utils;

namespace UnityEngine.Purchasing.Models
{
    /// <summary>
    /// This is C# representation of the Java Class PurchasesResult
    /// <a href="https://developer.android.com/reference/com/android/billingclient/api/Purchase.PurchasesResult">See more</a>
    /// </summary>
    class GooglePurchaseResult
    {
        internal int m_ResponseCode;
        internal List<GooglePurchase> m_Purchases = new List<GooglePurchase>();

        internal GooglePurchaseResult(AndroidJavaObject purchaseResult, IGoogleCachedQuerySkuDetailsService cachedQuerySkuDetailsService)
        {
            m_ResponseCode = purchaseResult.Call<int>("getResponseCode");
            FillPurchases(purchaseResult, cachedQuerySkuDetailsService);
        }

        void FillPurchases(AndroidJavaObject purchaseResult, IGoogleCachedQuerySkuDetailsService cachedQuerySkuDetailsService)
        {
            AndroidJavaObject purchaseList = purchaseResult.Call<AndroidJavaObject>("getPurchasesList");
            if (purchaseList != null)
            {
                int size = purchaseList.Call<int>("size");
                for (int index = 0; index < size; index++)
                {
                    AndroidJavaObject purchase = purchaseList.Call<AndroidJavaObject>("get", index);
                    if (purchase != null)
                    {
                        m_Purchases.Add(GooglePurchaseHelper.MakeGooglePurchase(cachedQuerySkuDetailsService.GetCachedQueriedSkus().ToList(), purchase));
                    }
                    else
                    {
                        Debug.LogWarning("Failed to retrieve Purchase from Purchase List at index " + index + " of " + size + ". FillPurchases will skip this item");
                    }
                }
            }
        }
    }
}
