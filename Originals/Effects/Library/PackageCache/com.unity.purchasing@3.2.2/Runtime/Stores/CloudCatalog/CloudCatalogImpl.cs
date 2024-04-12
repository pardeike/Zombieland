using System;
using System.IO;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Linq;
using UnityEngine.Purchasing.Extension;

namespace UnityEngine.Purchasing
{
    /// <summary>
    /// Fetches IAP products from Unity cloud.
    /// Caches products on local disk when the server is unavailable.
    /// </summary>
    public class CloudCatalogImpl
    {
        private IAsyncWebUtil m_AsyncUtil;
        private string m_CacheFileName;
        private ILogger m_Logger;
        private string m_CatalogURL;
        private string m_StoreName;
        const int kMaxRetryDelayInSeconds = 60 * 5;

		private const string kCatalogURL = "https://catalog.iap.cloud.unity3d.com";

        /// <summary>
        /// Creates a new intance of a cloud catalog for a given store.
        /// </summary>
        /// <param name="storeName"> The name of the store for which to create the catalog, e.g. GooglePlay. </param>
        /// <returns> The new instance created. </returns>
		public static CloudCatalogImpl CreateInstance (string storeName)
		{
			var g = new GameObject ();
			UnityEngine.Object.DontDestroyOnLoad (g);
			g.name = "Unity IAP";
			g.hideFlags = HideFlags.HideInHierarchy | HideFlags.HideInInspector;
			var web = g.AddComponent<AsyncWebUtil> ();
			var cachePath = Path.Combine (
					Path.Combine (Application.persistentDataPath, "Unity"),
					Path.Combine (Application.cloudProjectId, "IAP"));

            var logger = UnityEngine.Debug.unityLogger;

			string cacheFileName = null;
			try {
				Directory.CreateDirectory (cachePath);
				cacheFileName = Path.Combine (cachePath, "catalog.json");
			} catch (Exception e) {
                logger.Log ("Unable to cache IAP catalog", e);
				// Caching is not mandatory.
			}

			var catalogURL = string.Format ("{0}/{1}", kCatalogURL, Application.cloudProjectId);
			return new CloudCatalogImpl (web, cacheFileName, logger, catalogURL, storeName);
		}

        internal CloudCatalogImpl(IAsyncWebUtil util, string cacheFile, ILogger logger, string catalogURL, string storeName)
        {
            m_AsyncUtil = util;
            m_CacheFileName = cacheFile;
            m_Logger = logger;
            m_CatalogURL = catalogURL;
            m_StoreName = storeName;
        }

        /// <summary>
        /// Fetches the products contained in the cloud catalog aynchronously.
        /// </summary>
        /// <param name="callback"> The action to be executed once the fetch is complete. </param>
		public void FetchProducts (Action<HashSet<ProductDefinition>> callback)
		{
			FetchProducts (callback, 0);
		}

        internal void FetchProducts(Action<HashSet<ProductDefinition>> callback, int delayInSeconds)
        {
            m_AsyncUtil.Get(m_CatalogURL,
                response =>
                {
                    try
                    {
                        var result = ParseProductsFromJSON(response, m_StoreName);
                        TryPersistCatalog(response);
                        callback(result);
                    }
                    catch (SerializationException s)
                    {
                        m_Logger.LogError("Unity IAP", "Error parsing IAP catalog " + s);
                        m_Logger.LogError("Unity IAP", "Response: " + response);
                        callback(TryLoadCachedCatalog());
                    }
                },
                error =>
                {
                    // Fallback to cache if available,
                    // otherwise backoff and retry.
                    var cached = TryLoadCachedCatalog();
                    if (cached != null && cached.Count > 0)
                    {
                        m_Logger.LogWarning("Unity IAP", "Failed to fetch IAP catalog, using cache.");
                        callback(cached);
                    }
                    else
                    {
                        m_Logger.LogWarning("Unity IAP", "Failed to fetch IAP catalog, trying again.");
                        delayInSeconds = Math.Max(5, delayInSeconds * 2);
                        delayInSeconds = Math.Min(kMaxRetryDelayInSeconds, delayInSeconds);
                        m_AsyncUtil.Schedule(() => FetchProducts(callback, delayInSeconds), delayInSeconds);
                    }
                }
                );
        }

        /// <summary>
        /// Parse product definitions from JSON, selecting store specific identifiers
        /// for the specified store name.
        /// </summary>
        internal static HashSet<ProductDefinition> ParseProductsFromJSON(string json, string storeName)
        {
            var result = new HashSet<ProductDefinition>();
            try
            {
				var container = (Dictionary<string, object>)MiniJson.JsonDecode (json);
                object products;
                container.TryGetValue("products", out products);
				var productsList = products as List<object>;

                var snakeCaseStoreName = CamelCaseToSnakeCase(storeName);
				foreach (object product in productsList)
                {
					var productDict = (Dictionary<string, object>)product;
                    object id, storeIDs, typeString;
                    // Remove payouts and enabled references for 1.13.0.
                    //object enabled, payoutsJson;
                    productDict.TryGetValue("id", out id);
                    productDict.TryGetValue("store_ids", out storeIDs);
                    productDict.TryGetValue("type", out typeString);
                    // Remove payouts and enabled references for 1.13.0.
                    //productDict.TryGetValue("enabled", out enabled);
                    //productDict.TryGetValue("payouts", out payoutsJson);

					var idHash = storeIDs as Dictionary<string, object>;
                    string storeSpecificId = (string)id;
                    if (null != idHash && idHash.ContainsKey(snakeCaseStoreName))
                    {
                        object storeId = null;
                        idHash.TryGetValue(snakeCaseStoreName, out storeId);
                        if (null != storeId)
                            storeSpecificId = (string)storeId;
                    }

                    var type = (ProductType)Enum.Parse(typeof(ProductType), (string)typeString);

                    // Remove payouts and enabled references for 1.13.0.
                    //bool enabledBool = true;
                    //if (enabled != null && (enabled is bool || enabled is Boolean)) {
                    //    enabledBool = (bool)enabled;
                    //}
                    //var definition = new ProductDefinition((string)id, storeSpecificId, type, enabledBool);
                    var definition = new ProductDefinition((string)id, storeSpecificId, type);

                    // Remove payouts and enabled references for 1.13.0.
                    //List<object> payoutsJsonArray = payoutsJson as List<object>;
                    //if (payoutsJsonArray != null) {
                    //    var payouts = new List<PayoutDefinition>();
                    //    foreach (object payoutJson in payoutsJsonArray) {
                    //        Dictionary<string, object> payoutJsonDict = payoutJson as Dictionary<string, object>;
                    //        object payoutTypeString, subtype, quantity, data;
                    //        payoutJsonDict.TryGetValue("t", out payoutTypeString);
                    //        payoutJsonDict.TryGetValue("st", out subtype);
                    //        payoutJsonDict.TryGetValue("q", out quantity);
                    //        payoutJsonDict.TryGetValue("d", out data);
                    //
                    //        double q = quantity == null ? 0 : Convert.ToDouble (quantity);
                    //
                    //        payouts.Add(new PayoutDefinition((string)payoutTypeString, (string)subtype, q, (string)data));
                    //    }
                    //    definition.SetPayouts(payouts);
                    //}

                    result.Add(definition);
                }
                return result;
            }
            catch (Exception e)
            {
                throw new SerializationException ("Error parsing JSON", e);
            }
        }

        internal static string CamelCaseToSnakeCase(string s)
        {
            var segments = s.Select((a, b) => char.IsUpper(a) && b > 0 ? "_" + char.ToLower(a) : char.ToLower(a).ToString());
            return segments.Aggregate((a, b) => a + b);
        }

        private void TryPersistCatalog(string response)
        {
            if (null == m_CacheFileName)
                return;
            try
            {
                File.WriteAllText(m_CacheFileName, response);
            }
            catch (Exception e)
            {
                m_Logger.LogError("Failed persisting IAP catalog", e);
            }
        }

        private HashSet<ProductDefinition> TryLoadCachedCatalog()
        {
            if (null != m_CacheFileName && File.Exists(m_CacheFileName))
            {
                try
                {
                    var catalog = File.ReadAllText(m_CacheFileName);
                    return ParseProductsFromJSON(catalog, m_StoreName);
                }
                catch (Exception e)
                {
                    m_Logger.LogError("Error loading cached catalog", e);
                }
            }

            return new HashSet<ProductDefinition>();
        }
    }
}
