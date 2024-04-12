using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine.Purchasing
{
    interface IGoogleCachedQuerySkuDetailsService
    {
        HashSet<AndroidJavaObject> GetCachedQueriedSkus();

        void AddCachedQueriedSkus(List<AndroidJavaObject> queriedSkus);
    }
}
