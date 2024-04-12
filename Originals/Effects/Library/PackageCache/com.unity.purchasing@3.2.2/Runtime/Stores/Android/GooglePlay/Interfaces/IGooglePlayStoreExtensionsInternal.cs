using System;
using System.Collections.Generic;
using UnityEngine.Purchasing.Extension;
using UnityEngine.Purchasing.Models;

namespace UnityEngine.Purchasing
{
    interface IGooglePlayStoreExtensionsInternal
    {
        void SetStoreCallback(IStoreCallback storeCallback);
        void NotifyDeferredPurchase(string productId, string receipt, string transactionId);
        void NotifyDeferredProrationUpgradeDowngradeSubscription(string productId);
    }
}
