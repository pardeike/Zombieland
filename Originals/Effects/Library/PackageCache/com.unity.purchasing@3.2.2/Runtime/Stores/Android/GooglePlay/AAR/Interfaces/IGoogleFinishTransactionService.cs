using System;
using UnityEngine.Purchasing.Models;

namespace UnityEngine.Purchasing.Interfaces
{
    interface IGoogleFinishTransactionService
    {
        void FinishTransaction(ProductDefinition product, string purchaseToken, Action<ProductDefinition, GooglePurchase, GoogleBillingResult, string> onConsume, Action<ProductDefinition, GooglePurchase, GoogleBillingResult> onAcknowledge);
    }
}
