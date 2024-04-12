using System;
using System.Collections.Generic;
using UnityEngine.Purchasing.Extension;

namespace UnityEngine.Purchasing.Utils
{
    static class SkuDetailsConverter
    {
        internal static void ConvertOnQuerySkuDetailsResponse(List<AndroidJavaObject> skus, Action<List<ProductDescription>> onProductsReceived)
        {
            var products = ConvertSkusDetailsToProducts(skus);
            onProductsReceived(products);
        }

        static List<ProductDescription> ConvertSkusDetailsToProducts(List<AndroidJavaObject> skus)
        {
            List<ProductDescription> products = new List<ProductDescription>();
            foreach (AndroidJavaObject skuDetails in skus)
            {
                products.AddRange(skuDetails.ToListProducts());
            }

            return products;
        }

        static List<ProductDescription> ToListProducts(this AndroidJavaObject skusDetails)
        {
            return new List<ProductDescription>()
            {
                BuildProductDescription(new AndroidJavaObjectWrapper(skusDetails))
            };
        }

        /// <summary>
        /// Build a `ProductDescription` from a SkuDetails `AndroidJavaObject`
        /// <a href="https://developer.android.com/reference/com/android/billingclient/api/SkuDetails">Learn more about SkuDetails</a>
        /// </summary>
        /// <param name="skuDetails">`AndroidJavaObject` of SkuDetails</param>
        /// <returns>`ProductDescription` representation of a SkuDetails</returns>
        internal static ProductDescription BuildProductDescription(IAndroidJavaObjectWrapper skuDetails)
        {
            string sku = skuDetails.Call<string>("getSku");
            string price = skuDetails.Call<string>("getPrice");
            decimal priceAmount = Convert.ToDecimal(skuDetails.Call<long>("getPriceAmountMicros") > 0 ? skuDetails.Call<long>("getPriceAmountMicros") / 1000000.0 : 0);
            string title = skuDetails.Call<string>("getTitle");
            string description = skuDetails.Call<string>("getDescription");
            string priceCurrencyCode = skuDetails.Call<string>("getPriceCurrencyCode");
            string originalJson = skuDetails.Call<string>("getOriginalJson");
            string subscriptionPeriod = skuDetails.Call<string>("getSubscriptionPeriod");
            string freeTrialPeriod = skuDetails.Call<string>("getFreeTrialPeriod");
            string introductoryPrice = skuDetails.Call<string>("getIntroductoryPrice");
            string introductoryPricePeriod = skuDetails.Call<string>("getIntroductoryPricePeriod");
            int introductoryPriceCycles = skuDetails.Call<int>("getIntroductoryPriceCycles");

            GoogleProductMetadata productMetadata = new GoogleProductMetadata(
                price,
                title,
                description,
                priceCurrencyCode,
                priceAmount)
            {
                originalJson = originalJson,
                introductoryPrice = introductoryPrice,
                subscriptionPeriod = subscriptionPeriod,
                freeTrialPeriod = freeTrialPeriod,
                introductoryPricePeriod = introductoryPricePeriod,
                introductoryPriceCycles = introductoryPriceCycles
            };

            ProductDescription product = new ProductDescription(
                sku,
                productMetadata,
                "",
                ""
            );
            return product;
        }
    }
}
