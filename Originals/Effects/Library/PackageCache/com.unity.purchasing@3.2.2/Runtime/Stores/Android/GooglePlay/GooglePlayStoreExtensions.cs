using System;
using System.Collections.Generic;
using UnityEngine.Purchasing.Extension;
using UnityEngine.Purchasing.Interfaces;
using UnityEngine.Purchasing.Models;

namespace UnityEngine.Purchasing
{
    class GooglePlayStoreExtensions: IGooglePlayStoreExtensions, IGooglePlayStoreExtensionsInternal
    {
        IGooglePlayStoreService m_GooglePlayStoreService;
        IGooglePlayStoreFinishTransactionService m_GooglePlayStoreFinishTransactionService;
        IStoreCallback m_StoreCallback;
        GooglePlayStoreExtensionsInternal m_GooglePlayStoreExtensionsInternal;
        Action<Product> m_DeferredPurchaseAction;
        Action<Product> m_DeferredProrationUpgradeDowngradeSubscriptionAction;

        internal GooglePlayStoreExtensions(IGooglePlayStoreService googlePlayStoreService, IGooglePlayStoreFinishTransactionService googlePlayStoreFinishTransactionService, GooglePlayStoreExtensionsInternal googlePlayStoreExtensionsInternal)
        {
            m_GooglePlayStoreService = googlePlayStoreService;
            m_GooglePlayStoreFinishTransactionService = googlePlayStoreFinishTransactionService;
            m_GooglePlayStoreExtensionsInternal = googlePlayStoreExtensionsInternal;
            m_GooglePlayStoreExtensionsInternal.SetGooglePlayStoreExtensions(this);
        }

        public void UpgradeDowngradeSubscription(string oldSku, string newSku)
        {
            UpgradeDowngradeSubscription(oldSku, newSku, GooglePlayProrationMode.k_ImmediateWithoutProration);
        }

        public void UpgradeDowngradeSubscription(string oldSku, string newSku, int desiredProrationMode)
        {
            Product product = m_StoreCallback.FindProductById(newSku);
            Product oldProduct = m_StoreCallback.FindProductById(oldSku);
            if (product != null && product.definition.type == ProductType.Subscription &&
                oldProduct != null && oldProduct.definition.type == ProductType.Subscription)
            {
                m_GooglePlayStoreService.Purchase(product.definition, oldProduct, desiredProrationMode);
            }
            else
            {
                m_StoreCallback?.OnPurchaseFailed(
                    new PurchaseFailureDescription(
                        newSku ?? "",
                        PurchaseFailureReason.ProductUnavailable,
                        "Please verify that the products are subscriptions and are not null."));
            }
        }

        public void RestoreTransactions(Action<bool> callback)
        {
            m_GooglePlayStoreService.FetchPurchases(purchase =>
            {
                if (purchase != null)
                {
                    callback(true);
                }
            });
        }

        public void FinishAdditionalTransaction(string productId, string transactionId)
        {
            Product product = m_StoreCallback.FindProductById(productId);
            if (product != null && transactionId != null)
            {
                m_GooglePlayStoreFinishTransactionService.FinishTransaction(product.definition, transactionId);
            }
            else
            {
                m_StoreCallback?.OnPurchaseFailed(
                    new PurchaseFailureDescription(productId ?? "", PurchaseFailureReason.ProductUnavailable,
                        "Please make the product id and transaction id is not null"));
            }
        }

        public void ConfirmSubscriptionPriceChange(string productId, Action<bool> callback)
        {
            Product product = m_StoreCallback.FindProductById(productId);
            if (product != null)
            {
                m_GooglePlayStoreService.ConfirmSubscriptionPriceChange(product.definition, result =>
                {
                    callback(result.responseCode == GoogleBillingResponseCode.k_Ok);
                });
            }
        }

        public void SetDeferredPurchaseListener(Action<Product> action)
        {
            m_DeferredPurchaseAction = action;
        }

        public void SetDeferredProrationUpgradeDowngradeSubscriptionListener(Action<Product> action)
        {
            m_DeferredProrationUpgradeDowngradeSubscriptionAction = action;
        }

        public void SetStoreCallback(IStoreCallback storeCallback)
        {
            m_StoreCallback = storeCallback;
        }

        public void NotifyDeferredPurchase(string productId, string receipt, string transactionId)
        {
            Product product = m_StoreCallback.FindProductById(productId);
            if (product != null)
            {
                ProductPurchaseUpdater.UpdateProductReceiptAndTransactionID(product, receipt, transactionId, GooglePlay.Name);
                m_DeferredPurchaseAction?.Invoke(product);
            }
        }

        public void NotifyDeferredProrationUpgradeDowngradeSubscription(string productId)
        {
            Product product = m_StoreCallback.FindProductById(productId);
            if (product != null)
            {
                m_DeferredProrationUpgradeDowngradeSubscriptionAction?.Invoke(product);
            }
        }

        public void SetObfuscatedAccountId(string accountId)
        {
            m_GooglePlayStoreService.SetObfuscatedAccountId(accountId);
        }

        public void SetObfuscatedProfileId(string profileId)
        {
            m_GooglePlayStoreService.SetObfuscatedProfileId(profileId);
        }

        public bool IsPurchasedProductDeferred(Product product)
        {
            try
            {
                var unifiedReceipt = MiniJson.JsonDecode(product.receipt) as Dictionary<string, object>;
                var payloadStr = unifiedReceipt["Payload"] as string;

                var payload = MiniJson.JsonDecode(payloadStr) as Dictionary<string, object>;
                var jsonStr = payload["json"] as string;

                var jsonDic = MiniJson.JsonDecode(jsonStr) as Dictionary<string, object>;
                var purchaseState = (long)jsonDic["purchaseState"];

                //PurchaseState codes: https://developer.android.com/reference/com/android/billingclient/api/Purchase.PurchaseState#pending
                return purchaseState == 2 || purchaseState == 4;
            }
            catch
            {
                Debug.LogWarning("Cannot parse Google receipt for transaction " + product.transactionID);
                return false;
            }
        }

        public Dictionary<string, string> GetProductJSONDictionary()
        {
            return null;
        }

        public void SetLogLevel(int level) { }

        public bool IsOwned(Product p)
        {
            return false;
        }
    }
}
