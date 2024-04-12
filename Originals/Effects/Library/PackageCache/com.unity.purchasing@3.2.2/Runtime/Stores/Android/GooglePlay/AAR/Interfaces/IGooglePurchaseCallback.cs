using UnityEngine.Purchasing.Extension;

namespace UnityEngine.Purchasing.Interfaces
{
    interface IGooglePurchaseCallback
    {
        void SetStoreCallback(IStoreCallback storeCallback);
        void SetStoreExtension(IGooglePlayStoreExtensionsInternal extensions);
        void OnPurchaseSuccessful(string sku, string receipt, string purchaseToken);
        void OnPurchaseFailed(PurchaseFailureDescription purchaseFailureDescription);
        void NotifyDeferredPurchase(string sku, string receipt, string purchaseToken);
        void NotifyDeferredProrationUpgradeDowngradeSubscription(string sku);
    }
}
