using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine.Purchasing.Extension;
using UnityEngine.Scripting;
using Uniject;

// #define HIGH_PERMISSION_DATA // NOTE: Used elsewhere in project, IAP-1647

namespace UnityEngine.Purchasing
{
    /// <summary>
    /// Class that provides a static interface to IAP Promo purchases.
    /// </summary>
    public class Promo
    {
        private static JSONStore s_PromoPurchaser = null;
        private static IStoreCallback s_Unity = null;

        private static RuntimePlatform s_RuntimePlatform;
        private static ILogger s_Logger;
        private static string s_Version;
        private static IUtil s_Util;
        private static IAsyncWebUtil s_WebUtil;

        private static bool s_IsReady = false;
        private static string s_ProductJSON;

        /// <summary>
        /// Check if the IAP Promo setup is ready.
        /// </summary>
        /// <returns> Whether or not promo products have been proviced to Ads. </returns>
        [Preserve]
        public static bool IsReady()
        {
            return s_IsReady;
        }

        /// <summary>
        /// Check the version of IAP Promo.
        /// </summary>
        /// <returns> The package version used, if known. </returns>

        [Preserve]
        public static string Version()
        {
            return s_Version;
        }

        /// <summary>
        /// No longer used because this class is static.
        /// Creates a Promo object, but there is no reason to do this.
        /// </summary>
        public Promo()
        {
        }

        internal static void InitPromo(RuntimePlatform platform, ILogger logger, IUtil util, IAsyncWebUtil webUtil)
        {
            InitPromo(platform, logger, "Unknown", util, webUtil);
        }

        internal static void InitPromo(RuntimePlatform platform, ILogger logger, string version, IUtil util, IAsyncWebUtil webUtil)
        {
            s_RuntimePlatform = platform;
            // This assumes that the StandardPurchasingModule logger has been set correctly
            if(logger != null)
            {
                s_Logger = logger;
            }
            else
            {
                throw new ArgumentException("UnityIAP: Promo initialized with null logger!");
            }
            s_Version = version;
            s_Util = util;
            s_WebUtil = webUtil;
        }

        private static HashSet<Product> UpdatePromoProductList()
        {
            if((s_Unity == null)||(s_Unity.products == null))
            {
                s_Logger.LogError("UnityIAP Promo", "Trying to update list without manager or products ready");
                return null;
            }

            // Set up (or update) promo system when products have been received
            // Here we want to take advantage of the fact that the PurchasingManager will
            // have processed the products and added them to the internal list
            var promoProducts = new HashSet<Product>();
            var availProducts = s_Unity.products.all;
            foreach(var prod in availProducts)
            {
                if((prod.availableToPurchase)&&
                    ((prod.definition.type == ProductType.Consumable)||(string.IsNullOrEmpty(prod.transactionID))))
                {
                    // If transactionID is null/empty then this is not something we currently own
                    // (assuming that we are un-setting this for subs eventually)
                    // including consumables here assumes that we will actually consume them in ProcessPurchaseIfNew()

                    // Console.WriteLine("Promo: Adding \"{0}\" ({1})", prod.definition.id, prod.definition.storeSpecificId);

                    promoProducts.Add(prod);
                }
            }
            return (promoProducts.Count > 0) ? promoProducts : null;
        }

        internal static void ProvideProductsToAds (JSONStore purchaser, IStoreCallback manager)
        {
            if((purchaser == null)||(manager == null))
            {
                s_Logger.LogError("UnityIAP Promo", "Attempt to set promo products without a valid purchaser!");
                return;
            }
            s_PromoPurchaser = purchaser;
            s_Unity = manager;

            ProvideProductsToAds(UpdatePromoProductList());
        }

        private static void ProvideProductsToAds (HashSet<Product> productsForAds)
        {
                var promos = new List<Dictionary<string, object>>();
                if(productsForAds != null)
                {
                    foreach(var product in productsForAds)
                    {
                        var promoDic = new Dictionary<string, object>();
                        promoDic.Add("productId", product.definition.id);
                        promoDic.Add("iapProductId", product.definition.id);
                        promoDic.Add("productType", product.definition.type.ToString());
                        promoDic.Add("localizedPriceString", product.metadata.localizedPriceString);
                        promoDic.Add("localizedTitle", product.metadata.localizedTitle);
                        promoDic.Add("imageUrl", null);
                        promoDic.Add("isoCurrencyCode", product.metadata.isoCurrencyCode);
                        promoDic.Add("localizedPrice", product.metadata.localizedPrice);
                        promos.Add(promoDic);
                    }
                }

                // Now storing the last-delivered JSON for future queries
                s_ProductJSON = MiniJSON.Json.Serialize(promos);
                if(promos.Count > 0)
                {
                    s_IsReady = true;

#if HIGH_PERMISSION_DATA
                    // Send an async notification to Ads SDK to simplify refresh on their end
                    var eventSys = EventQueue.Instance(s_Util, s_WebUtil);
                    eventSys.SendEvent(EventDestType.AdsIPC, "{\"type\":\"CatalogUpdated\"}");
#endif
                }
        }

        /// <summary>
        /// Queries the list of available promo products.
        /// </summary>
        /// <returns> The list of promo products as raw JSON. </returns>
        [Preserve]
        public static string QueryPromoProducts()
        {
            return s_ProductJSON;
        }

        /// <summary>
        /// Initiates a purchasing command for a promo product.
        /// Legacy for original promo Ads SDK.
        /// </summary>
        /// <param name="itemRequest"> The JSON item request command for purchase. </param>
        /// <returns> If the command was successful or not. </returns>
        [Preserve]
        public static bool InitiatePromoPurchase(string itemRequest)
        {
            return InitiatePurchasingCommand(itemRequest);
        }

        /// <summary>
        /// Initiates a purchasing command for a promo product.
        /// </summary>
        /// <param name="command"> The JSON item request command for purchase. </param>
        /// <returns> If the command was successful or not. </returns>
        [Preserve]
        public static bool InitiatePurchasingCommand(string command)
        {
            if(String.IsNullOrEmpty(command))
            {
                if(s_Logger != null)
                {
                    s_Logger.LogFormat(LogType.Warning, "Promo received null or empty command");
                }
                return false;
            }

            // Keep for debug for now...
            // if(s_Logger != null)
            // {
            //     s_Logger.LogFormat(LogType.Log, "Promo.IPC({0})", command);
            // }

            Dictionary<string, object> dict = null;
            string request;

            // MiniJSON has been known to throw unexpected exceptions, let's try
            // to deal with them here...
            try
            {
                object req;
                dict = (Dictionary<string, object>)MiniJSON.Json.Deserialize(command);
                if(dict == null)
                {
                    return false;
                }

                // extract & deal with purchaseTrackingUrls first...
                object sentUrls;
                if(dict.TryGetValue("purchaseTrackingUrls", out sentUrls))
                {
                    if(sentUrls != null)
                    {
                        List<object> trackingUrls = sentUrls as List<object>;

                        var eventSys = EventQueue.Instance(s_Util, s_WebUtil);

                        // This is not a great solution, but nobody seems to want
                        // to guarantee what trackingUrls will include...
                        if(trackingUrls.Count > 0)
                        {
                            eventSys.SetIapUrl(trackingUrls[0] as string);
                        }
                        if(trackingUrls.Count > 1)
                        {
                            eventSys.SetAdsUrl(trackingUrls[1] as string);
                        }
                    }
                    dict.Remove("purchaseTrackingUrls");
                }

                // Back to JSON for if/when sent to old purchasing method
                command = MiniJSON.Json.Serialize(dict);

                if(!dict.TryGetValue("request", out req))
                {
                    // pass this to the old IPP
                    return ExecPromoPurchase(command);
                }
                else
                {
                    request = ((String)req).ToLower();
                }
                switch(request)
                {
                    case "purchase":
                        return ExecPromoPurchase(command);

                    case "setids":
                        var profile = ProfileData.Instance(s_Util);
                        object param;

                        if(dict.TryGetValue("gamerToken", out param))
                        {
                            profile.SetGamerToken(param as string);
                        }
                        if (dict.TryGetValue("trackingOptOut", out param)) {
                            profile.SetTrackingOptOut(param as bool?);
                        }
                        if(dict.TryGetValue("gameId", out param))
                        {
                            profile.SetGameId(param as string);
                        }
                        if(dict.TryGetValue("abGroup", out param))
                        {
                            profile.SetABGroup(param as int?);
                        }

                        return true;

                    case "close":
                        // I don't think we're currently receiving these
                        // we may want to send an event here
                        return true;

                    default:
                        if(s_Logger != null)
                        {
                            s_Logger.LogWarning("UnityIAP Promo", "Unknown request received: " + request);
                        }
                        return false;
                }
            }
            catch(Exception e)
            {
                if(s_Logger != null)
                {
                    s_Logger.LogError("UnityIAP Promo", "Exception while processing incoming request: " + e
                        + "\n" + command);
                }
                return false;
            }
        }

        // This is the old InitiatePromoPurchasing(), just renamed to keep it in place
        internal static bool ExecPromoPurchase(string itemRequest)
        {
            if ((!s_IsReady)||(s_PromoPurchaser == null))
            {
                if(s_Logger != null)
                {
                    s_Logger.LogError("UnityIAP Promo", "Promo purchase attempted without proper configuration");
                }
                return false;
            }

            object prodId = null;
            Dictionary<string, object> tagDict = null;
            try
            {
                tagDict = (Dictionary<string, object>)MiniJSON.Json.Deserialize(itemRequest);
                if (!tagDict.TryGetValue("productId", out prodId))
                {
                    // If we get here then we have JSON in the arg but no productID
                    s_Logger.LogError("UnityIAP", "Promo purchase unable to determine Product ID");
                    return false;
                }
            }
            catch
            {
                // No JSON in the request
                s_Logger.LogError("UnityIAP", "Promo purchase argument exception");
                return false;
            }
            if(String.IsNullOrEmpty(prodId as string))
            {
                s_Logger.LogError("UnityIAP", "Promo product is null or empty!");
                return false;
            }
            Product toPurchase = s_Unity.products.WithID((string)prodId);
            if(toPurchase == null)
            {
                s_Logger.LogError("UnityIAP", "Promo product lookup failed");
                return false;
            }

            tagDict.Add("storeSpecificId", toPurchase.definition.storeSpecificId);
            string tag = MiniJSON.Json.Serialize(tagDict);

            s_PromoPurchaser.Purchase(toPurchase.definition, tag);
            return true;
        }


    }
}
