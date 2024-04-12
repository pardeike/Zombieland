using System;
using UnityEngine.Purchasing.Models;

namespace UnityEngine.Purchasing.Interfaces
{
    interface IGoogleBillingClient
    {
        void StartConnection(IBillingClientStateListener billingClientStateListener);
        void EndConnection();
        AndroidJavaObject QueryPurchase(string skuType);
        void QuerySkuDetailsAsync(AndroidJavaObject skuDetailsParamsBuilder, SkuDetailsResponseListener listener);
        AndroidJavaObject LaunchBillingFlow(AndroidJavaObject sku, string oldSku, string oldPurchaseToken, int prorationMode);
        void ConsumeAsync(string purchaseToken, ProductDefinition product, GooglePurchase googlePurchase, Action<ProductDefinition, GooglePurchase, GoogleBillingResult, string> onConsume);
        void AcknowledgePurchase(string purchaseToken, ProductDefinition product, GooglePurchase googlePurchase, Action<ProductDefinition, GooglePurchase, GoogleBillingResult> onAcknowledge);
        void SetObfuscationAccountId(string obfuscationAccountId);
        void SetObfuscationProfileId(string obfuscationProfileId);
        void LaunchPriceChangeConfirmationFlow(AndroidJavaObject skuDetails, GooglePriceChangeConfirmationListener listener);
    }
}
