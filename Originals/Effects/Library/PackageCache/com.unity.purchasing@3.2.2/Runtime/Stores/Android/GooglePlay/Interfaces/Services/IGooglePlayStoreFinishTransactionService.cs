using UnityEngine.Purchasing.Extension;
using UnityEngine.Purchasing.Models;

namespace UnityEngine.Purchasing
{
    interface IGooglePlayStoreFinishTransactionService
    {
        void SetStoreCallback(IStoreCallback storeCallback);
        void FinishTransaction(ProductDefinition product, string purchaseToken);
        void OnConsume(ProductDefinition product, GooglePurchase googlePurchase, GoogleBillingResult billingResult, string purchaseToken);
        void OnAcknowledge(ProductDefinition product, GooglePurchase googlePurchase, GoogleBillingResult billingResult);
        void HandleFinishTransaction(ProductDefinition product, GooglePurchase googlePurchase, GoogleBillingResult billingResult, string purchaseToken);
    }
}
