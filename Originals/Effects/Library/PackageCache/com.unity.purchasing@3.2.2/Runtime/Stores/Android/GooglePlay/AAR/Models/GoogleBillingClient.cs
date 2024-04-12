using System;
using UnityEngine;
using UnityEngine.Purchasing.Interfaces;

namespace UnityEngine.Purchasing.Models
{
    /// <summary>
    /// This is C# representation of the Java Class BillingClient
    /// <a href="https://developer.android.com/reference/com/android/billingclient/api/BillingClient">See more</a>
    /// </summary>
    class GoogleBillingClient : IGoogleBillingClient
    {
        const string k_AndroidBillingFlowParamClassName = "com.android.billingclient.api.BillingFlowParams";

        static AndroidJavaClass GetBillingFlowParamClass()
        {
            return new AndroidJavaClass(k_AndroidBillingFlowParamClassName);
        }

        const string k_AndroidPriceChangeFlowParamClassName = "com.android.billingclient.api.PriceChangeFlowParams";

        static AndroidJavaClass GetPriceChangeFlowParamClass()
        {
            return new AndroidJavaClass(k_AndroidPriceChangeFlowParamClassName);
        }

        const string k_AndroidConsumeParamsClassName = "com.android.billingclient.api.ConsumeParams";
        static AndroidJavaClass GetConsumeParamsClass()
        {
            return new AndroidJavaClass(k_AndroidConsumeParamsClassName);
        }

        const string k_AndroidAcknowledgePurchaseParamsClassName = "com.android.billingclient.api.AcknowledgePurchaseParams";
        static AndroidJavaClass GetAcknowledgePurchaseParamsClass()
        {
            return new AndroidJavaClass(k_AndroidAcknowledgePurchaseParamsClassName);
        }

        const string k_AndroidBillingClientClassName = "com.android.billingclient.api.BillingClient";

        static AndroidJavaClass GetBillingClientClass()
        {
            return new AndroidJavaClass(k_AndroidBillingClientClassName);
        }

        AndroidJavaObject m_BillingClient;
        string m_ObfuscatedAccountId;
        string m_ObfuscatedProfileId;
        IGooglePurchaseUpdatedListener m_GooglePurchaseUpdatedListener;

        internal GoogleBillingClient(IGooglePurchaseUpdatedListener googlePurchaseUpdatedListener)
        {
            m_GooglePurchaseUpdatedListener = googlePurchaseUpdatedListener;
            AndroidJavaObject builder = GetBillingClientClass().CallStatic<AndroidJavaObject>("newBuilder", UnityActivity.GetCurrentActivity());
            builder = builder.Call<AndroidJavaObject>("setListener", googlePurchaseUpdatedListener);
            builder = builder.Call<AndroidJavaObject>("enablePendingPurchases");
            m_BillingClient = builder.Call<AndroidJavaObject>("build");
        }

        public void SetObfuscationAccountId(string obfuscationAccountId)
        {
            m_ObfuscatedAccountId = obfuscationAccountId;
        }

        public void SetObfuscationProfileId(string obfuscationProfileId)
        {
            m_ObfuscatedProfileId = obfuscationProfileId;
        }

        public void StartConnection(IBillingClientStateListener billingClientStateListener)
        {
            m_BillingClient.Call("startConnection", billingClientStateListener);
        }

        public void EndConnection()
        {
            m_BillingClient.Call("endConnection");
        }

        public AndroidJavaObject QueryPurchase(string skuType)
        {
            return m_BillingClient.Call<AndroidJavaObject>("queryPurchases", skuType);
        }

        public void QuerySkuDetailsAsync(AndroidJavaObject skuDetailsParamsBuilder, SkuDetailsResponseListener listener)
        {
            m_BillingClient.Call("querySkuDetailsAsync", skuDetailsParamsBuilder.Call<AndroidJavaObject>("build"), listener);
        }

        public AndroidJavaObject LaunchBillingFlow(AndroidJavaObject sku, string oldSku,  string oldPurchaseToken, int prorationMode)
        {
            return m_BillingClient.Call<AndroidJavaObject>("launchBillingFlow", UnityActivity.GetCurrentActivity(), MakeBillingFlowParams(sku, oldSku, oldPurchaseToken, prorationMode));
        }

        AndroidJavaObject MakeBillingFlowParams(AndroidJavaObject sku, string oldSku, string oldPurchaseToken, int prorationMode)
        {
            AndroidJavaObject billingFlowParams = GetBillingFlowParamClass().CallStatic<AndroidJavaObject>("newBuilder");

            billingFlowParams = SetObfuscatedAccountIdIfNeeded(billingFlowParams);
            billingFlowParams = SetObfuscatedProfileIdIfNeeded(billingFlowParams);

            billingFlowParams = billingFlowParams.Call<AndroidJavaObject>("setSkuDetails", sku);

            if (oldSku != null && oldPurchaseToken != null)
            {
                billingFlowParams = billingFlowParams.Call<AndroidJavaObject>("setOldSku", oldSku, oldPurchaseToken);
            }

            if (prorationMode != GooglePlayProrationMode.k_NullProrationMode)
            {
                billingFlowParams = billingFlowParams.Call<AndroidJavaObject>("setReplaceSkusProrationMode", prorationMode);
            }

            billingFlowParams = billingFlowParams.Call<AndroidJavaObject>("build");
            return billingFlowParams;
        }

        AndroidJavaObject SetObfuscatedProfileIdIfNeeded(AndroidJavaObject billingFlowParams)
        {
            if (m_ObfuscatedProfileId != null)
            {
                billingFlowParams = billingFlowParams.Call<AndroidJavaObject>("setObfuscatedProfileId", m_ObfuscatedProfileId);
            }

            return billingFlowParams;
        }

        AndroidJavaObject SetObfuscatedAccountIdIfNeeded(AndroidJavaObject billingFlowParams)
        {
            if (m_ObfuscatedAccountId != null)
            {
                billingFlowParams = billingFlowParams.Call<AndroidJavaObject>("setObfuscatedAccountId", m_ObfuscatedAccountId);
            }

            return billingFlowParams;
        }

        public void ConsumeAsync(string purchaseToken, ProductDefinition product, GooglePurchase googlePurchase, Action<ProductDefinition, GooglePurchase, GoogleBillingResult, string> onConsume)
        {
            AndroidJavaObject consumeParams = GetConsumeParamsClass().CallStatic<AndroidJavaObject>("newBuilder");
            consumeParams = consumeParams.Call<AndroidJavaObject>("setPurchaseToken", purchaseToken);
            consumeParams = consumeParams.Call<AndroidJavaObject>("build");

            m_BillingClient.Call("consumeAsync", consumeParams, new GoogleConsumeResponseListener(product, googlePurchase, onConsume));
        }

        public void AcknowledgePurchase(string purchaseToken, ProductDefinition product, GooglePurchase googlePurchase, Action<ProductDefinition, GooglePurchase, GoogleBillingResult> onAcknowledge)
        {
            AndroidJavaObject acknowledgePurchaseParams = GetAcknowledgePurchaseParamsClass().CallStatic<AndroidJavaObject>("newBuilder");
            acknowledgePurchaseParams = acknowledgePurchaseParams.Call<AndroidJavaObject>("setPurchaseToken", purchaseToken);
            acknowledgePurchaseParams = acknowledgePurchaseParams.Call<AndroidJavaObject>("build");

            m_BillingClient.Call("acknowledgePurchase", acknowledgePurchaseParams, new GoogleAcknowledgePurchaseListener(product, googlePurchase, onAcknowledge));
        }

        public void LaunchPriceChangeConfirmationFlow(AndroidJavaObject skuDetails, GooglePriceChangeConfirmationListener listener)
        {
            m_BillingClient.Call("launchPriceChangeConfirmationFlow", UnityActivity.GetCurrentActivity(), MakePriceChangeFlowParams(skuDetails), listener);
        }

        AndroidJavaObject MakePriceChangeFlowParams(AndroidJavaObject skuDetails)
        {
            AndroidJavaObject priceChangeFlowParams = GetPriceChangeFlowParamClass().CallStatic<AndroidJavaObject>("newBuilder");
            priceChangeFlowParams = priceChangeFlowParams.Call<AndroidJavaObject>("setSkuDetails", skuDetails);
            priceChangeFlowParams = priceChangeFlowParams.Call<AndroidJavaObject>("build");
            return priceChangeFlowParams;
        }
    }
}
