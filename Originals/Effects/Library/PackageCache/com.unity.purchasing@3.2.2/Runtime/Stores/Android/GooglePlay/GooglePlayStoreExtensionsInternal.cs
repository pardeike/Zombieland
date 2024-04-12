using UnityEngine.Purchasing.Extension;

namespace UnityEngine.Purchasing
{
    class GooglePlayStoreExtensionsInternal : IGooglePlayStoreExtensionsInternal
    {
        GooglePlayStoreExtensions m_GooglePlayStoreExtensions;

        public void SetGooglePlayStoreExtensions(GooglePlayStoreExtensions googlePlayStoreExtensions)
        {
            m_GooglePlayStoreExtensions = googlePlayStoreExtensions;
        }

        public void SetStoreCallback(IStoreCallback storeCallback)
        {
            m_GooglePlayStoreExtensions?.SetStoreCallback(storeCallback);
        }

        public void NotifyDeferredPurchase(string productId, string receipt, string transactionId)
        {
            m_GooglePlayStoreExtensions?.NotifyDeferredPurchase(productId, receipt, transactionId);
        }

        public void NotifyDeferredProrationUpgradeDowngradeSubscription(string productId)
        {
            m_GooglePlayStoreExtensions?.NotifyDeferredProrationUpgradeDowngradeSubscription(productId);
        }
    }
}
