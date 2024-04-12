using System;
using UnityEngine.Purchasing.Interfaces;
using UnityEngine.Purchasing.Models;

namespace UnityEngine.Purchasing
{
    class GooglePriceChangeService : IGooglePriceChangeService
    {
        IGoogleBillingClient m_BillingClient;
        QuerySkuDetailsService m_QuerySkuDetailsService;

        internal GooglePriceChangeService(IGoogleBillingClient billingClient, IGoogleCachedQuerySkuDetailsService cachedQuerySkuDetailsService)
        {
            m_BillingClient = billingClient;
            m_QuerySkuDetailsService = new QuerySkuDetailsService(billingClient, cachedQuerySkuDetailsService);
        }

        public void PriceChange(ProductDefinition product, Action<GoogleBillingResult> onPriceChangedListener)
        {
            m_QuerySkuDetailsService.QueryAsyncSku(product, skuDetailsList =>
            {
                foreach (AndroidJavaObject skuDetails in skuDetailsList)
                {
                    m_BillingClient.LaunchPriceChangeConfirmationFlow(skuDetails, new GooglePriceChangeConfirmationListener(onPriceChangedListener));
                }
            });

        }
    }
}
