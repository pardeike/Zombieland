using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using Uniject;

namespace UnityEngine.Purchasing
{
    /// <summary>
    /// Fetches product list from IAP E-Commerce Server and caches content.
    /// If the product list cannot be retrieved from the server or correctly parsed,
    /// the last cached response will be used if available.
    /// </summary>
    internal class StoreCatalogImpl
    {
        private IAsyncWebUtil m_AsyncUtil;
        private ILogger m_Logger;
        private string m_CatalogURL;
        private string m_StoreName;
        private FileReference m_cachedStoreCatalogReference;
        private const string kFileName = "store.json";

        private static ProfileData profile;

        private const string kCatalogURL = "https://ecommerce.iap.unity3d.com";
        //stg private const string kCatalogURL = "https://ecommerce-iap-stg.ie.unityads.unity3d.com";
        //qa  private const string kCatalogURL = "http://ec2-35-172-194-34.compute-1.amazonaws.com:8000";

        /// <summary>
        /// Fetches a Product catalog for the given parameters from the given catalog service. Returns null if
        /// either current ProfileData or key input parameters are incomplete.
        /// </summary>
        /// <param name="storeName">if null or empty, returns a null catalog provider</param>
        /// <param name="baseUrl">if null or empty, returns a null catalog provider</param>
        /// <param name="webUtil"></param>
        /// <param name="logger"></param>
        /// <param name="util"></param>
        /// <returns></returns>
        public static StoreCatalogImpl CreateInstance (string storeName, string baseUrl, IAsyncWebUtil webUtil, ILogger logger, IUtil util, JSONStore baseStore = null)
        {
            if ((String.IsNullOrEmpty(storeName))||(String.IsNullOrEmpty(baseUrl)))
            {
                return null;
            }


            if (logger == null)
            {
                logger = UnityEngine.Debug.unityLogger;
            }
            profile = ProfileData.Instance(util);
            Dictionary<string, object> queryParams = profile.GetProfileIds();
            if((baseStore != null) && baseStore.disableStoreCatalog)
            {
                queryParams.Add("storeDisabled", "true");
            }
            var storeCatalogURL = baseUrl + "/catalog" + queryParams.ToQueryString();
            var fileReference = FileReference.CreateInstance(kFileName, logger, util);
            return new StoreCatalogImpl(webUtil, logger, storeCatalogURL, storeName, fileReference);
        }

        internal StoreCatalogImpl(IAsyncWebUtil util, ILogger logger, string catalogURL, string storeName, FileReference fileReference)
        {
            m_AsyncUtil = util;
            m_Logger = logger;
            m_CatalogURL = catalogURL;
            m_StoreName = storeName;
            m_cachedStoreCatalogReference = fileReference;
        }

        internal void FetchProducts(Action<List<ProductDefinition>> callback)
        {
            m_AsyncUtil.Get(m_CatalogURL,
                response =>
                {
                    var result = ParseProductsFromJSON(response, m_StoreName, m_Logger);
                    if (result == null)
                    {
                        m_Logger.LogError("Failed to fetch IAP catalog due to malformed response for " + m_StoreName, "response: " + response);
                        handleCachedCatalog(callback);
                    }
                    else
                    {
                        if (m_cachedStoreCatalogReference != null)
                        {
                            m_cachedStoreCatalogReference.Save(response);
                        }
                        callback(result);
                    }
                },
                error =>
                {
                    handleCachedCatalog(callback);
                }
            );
        }

        /// <summary>
        /// Parse product definitions from JSON, selecting store specific identifiers
        /// for the specified store name.
        /// </summary>
        internal static List<ProductDefinition> ParseProductsFromJSON(string json, string storeName, ILogger logger)
        {
            if (String.IsNullOrEmpty(json))
            {
                return null;
            }

            var result = new HashSet<ProductDefinition>();
            try
            {
                var container = (Dictionary<string, object>)MiniJson.JsonDecode (json);
                object storeCatalog;
                object abGroupObject;
                container.TryGetValue("catalog", out storeCatalog);
                if(container.TryGetValue("abGroup", out abGroupObject))
                {
                    if(profile != null)
                    {
                        profile.SetStoreABGroup(Convert.ToInt32(abGroupObject));
                    }
                }
                var cat = storeCatalog as Dictionary<string, object>;
                object catid;
                if(cat.TryGetValue("id", out catid))
                {
                    if(profile != null)
                    {
                        var id = catid as string;
                        if(id == "")
                        {
                            id = "empty-catalog-id";
                        }
                        profile.SetCatalogId(id);
                    }
                }
                else
                {
                    // Tracking the case when store catalog returns without an "id" field
                    // May need to be an errr at some point
                    if(profile != null)
                    {
                        profile.SetCatalogId("no-catalog-id-present");
                    }
                }
                object products;
                cat.TryGetValue("products", out products);
                var jsonList = (List<object>)products;
                return jsonList.DecodeJSON(storeName);
            }
            catch (Exception e) {
                if(logger != null)
                {
                    logger.LogWarning("UnityIAP", "Error parsing catalog, exception " + e);
                }
                return null;
            }
        }

        private void handleCachedCatalog(Action<List<ProductDefinition>> callback)
        {
            List<ProductDefinition> cache = null;
            if (m_cachedStoreCatalogReference != null)
            {
                cache = ParseProductsFromJSON(m_cachedStoreCatalogReference.Load(), m_StoreName, m_Logger);
            }

            callback(cache);
        }
    }
}
