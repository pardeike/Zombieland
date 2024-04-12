using UnityEngine.Purchasing.Extension;
using UnityEngine.Purchasing.Interfaces;

namespace UnityEngine.Purchasing
{
    class GooglePlayPurchaseCallback: IGooglePurchaseCallback
    {
        IStoreCallback m_StoreCallback;
        IGooglePlayStoreExtensionsInternal m_GooglePlayStoreExtensions;

        public void SetStoreCallback(IStoreCallback storeCallback)
        {
            m_StoreCallback = storeCallback;
        }

        public void SetStoreExtension(IGooglePlayStoreExtensionsInternal extensions)
        {
            m_GooglePlayStoreExtensions = extensions;
        }

        public void OnPurchaseSuccessful(string sku, string receipt, string purchaseToken)
        {
            m_StoreCallback?.OnPurchaseSucceeded(sku, receipt, purchaseToken);
        }

        public void OnPurchaseFailed(PurchaseFailureDescription purchaseFailureDescription)
        {
            m_StoreCallback?.OnPurchaseFailed(purchaseFailureDescription);
        }

        public void NotifyDeferredPurchase(string sku, string receipt, string purchaseToken)
        {
            m_GooglePlayStoreExtensions?.NotifyDeferredPurchase(sku, receipt, purchaseToken);
        }

        public void NotifyDeferredProrationUpgradeDowngradeSubscription(string sku)
        {
            m_GooglePlayStoreExtensions?.NotifyDeferredProrationUpgradeDowngradeSubscription(sku);
        }
    }
}
