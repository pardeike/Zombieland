using System.Collections.Generic;
using System.Linq;

namespace UnityEngine.Purchasing
{
    class GoogleCachedQuerySkuDetailsService: IGoogleCachedQuerySkuDetailsService
    {
        HashSet<AndroidJavaObject> m_CachedQueriedSkus = new HashSet<AndroidJavaObject>();

        public HashSet<AndroidJavaObject> GetCachedQueriedSkus()
        {
            return m_CachedQueriedSkus;
        }

        bool ContainsSku(string sku)
        {
            return m_CachedQueriedSkus.Any(skuDetails => skuDetails.Call<string>("getSku") == sku);
        }

        public void AddCachedQueriedSkus(List<AndroidJavaObject> queriedSkus)
        {
            foreach (var queriedSkuDetails in queriedSkus)
            {
                string queriedSku = queriedSkuDetails.Call<string>("getSku");

                if (ContainsSku(queriedSku))
                {
                    UpdateCachedQueriedSku(queriedSku, queriedSkuDetails);
                }
                else
                {
                    m_CachedQueriedSkus.Add(queriedSkuDetails);
                }
            }
        }

        void UpdateCachedQueriedSku(string queriedSku, AndroidJavaObject queriedSkuDetails)
        {
            var foundSkuDetails = m_CachedQueriedSkus
                .FirstOrDefault(skuDetails => skuDetails.Call<string>("getSku") == queriedSku);
            if (foundSkuDetails != null)
            {
                foundSkuDetails = queriedSkuDetails;
            }
        }
    }
}
