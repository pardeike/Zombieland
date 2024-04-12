using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Diagnostics.SymbolStore;
using System.IO;
using System.Reflection;
using UnityEngine.Scripting;
using UnityEngine.Purchasing.Extension;
using System.Linq;
using System.Text;

// #define HIGH_PERMISSION_DATA // NOTE: Used elsewhere in project, IAP-1647

namespace UnityEngine.Purchasing
{
    /// <summary>
    /// Internal store implementation passing store requests from the user through to the underlaying
    /// native store system, and back again. Binds a native store system binding to a callback.
    /// </summary>
    internal class JSONStore : AbstractStore, IUnityCallback, IManagedStoreExtensions, IStoreInternal, IManagedStoreConfig, ITransactionHistoryExtensions
    {
        public Product[] storeCatalog {
            get {
                var result = new List<Product>();
                if (m_storeCatalog != null && unity.products.all != null)
                {
                    foreach (var catalogProduct in m_storeCatalog)
                    {
                        foreach (var controllerProduct in unity.products.all)
                        {
                            // Ensure owned products are excluded from list (except when consumable)
                            bool isProductOwned = false;
                            if (controllerProduct.definition.type != ProductType.Consumable)
                            {
                                if (controllerProduct.hasReceipt || !String.IsNullOrEmpty(controllerProduct.transactionID)) {
                                    isProductOwned = true;
                                }
                            }
                            // TODO: Update Engine Code so Product Definition comparision Equals checks against storeSpecificId
                            if (controllerProduct.availableToPurchase &&
                                !isProductOwned &&
                                controllerProduct.definition.storeSpecificId == catalogProduct.storeSpecificId)
                            {
                                result.Add(controllerProduct);
                            }
                        }
                    }
                }
                return result.ToArray();
            }
        }
        private StoreCatalogImpl m_managedStore;
        protected IStoreCallback unity;
        private INativeStore store;
        private List<ProductDefinition> m_storeCatalog;
        private bool isManagedStoreEnabled = true;
        private ProfileData m_profileData;
        private bool isRefreshing = false;
#if HIGH_PERMISSION_DATA
        private bool isFirstTimeRetrievingProducts = true;
#endif
        private Action refreshCallback;

        // m_Module is our StandardPurchasingModule, added via reflection to avoid core changes etc.
        private StandardPurchasingModule m_Module;
        private HashSet<ProductDefinition> m_BuilderProducts = new HashSet<ProductDefinition>();

        protected ILogger m_Logger;

        private EventQueue m_EventQueue;

        private Dictionary<string, object> promoPayload = null;

        // Default IAP-Events and E-Commerce endpoints
        private const string kIapEventsBase = "https://events.iap.unity3d.com/events";
        private const string kIecCatalogBase = "https://ecommerce.iap.unity3d.com";

        // For Reference Only -- override from app now
        //stg private const string kIapEventsBase = "https://events-iap-stg.ie.unityads.unity3d.com/events";
        //stg private const string kIecCatalogBase = "https://ecommerce-iap-stg.ie.unityads.unity3d.com";
        //qa  private const string kIecCatalogBase = "http://ec2-35-172-194-34.compute-1.amazonaws.com:8000";

        // ManagedStoreConfig Stuff
        //
        private bool catalogDisabled = false;
        private bool eventsDisabled = false;
        private bool testStore = false; // Do not use this directly
        private string iapBaseUrl = null; // Current implementation the catalog URL can be set once
        private string eventBaseUrl = kIapEventsBase;

        // ITransactionHistoryExtensions stuff
        //
        // Enhanced error information
        protected PurchaseFailureDescription lastPurchaseFailureDescription;
        private StoreSpecificPurchaseErrorCode _lastPurchaseErrorCode = StoreSpecificPurchaseErrorCode.Unknown;
        /// <summary>
        /// <seealso cref="TransactionLog.persistentDataPath"/>
        /// </summary>
        private readonly string m_persistentDataPath;

        private string kStoreSpecificErrorCodeKey = "storeSpecificErrorCode";

        public bool disableStoreCatalog
        {
            get
            {
                return catalogDisabled;
            }

            set
            {
                if (value == true)
                {
                    catalogDisabled = true;
                    eventsDisabled = true;
                    isManagedStoreEnabled = false;
                    // if (Application.isEditor)
                    //{
                        // We'd like to track when devs are using this option, but
                        // we also need to deal with 5.4-2018.1 so can't rely
                        // solely on the new, "blessed" events
                        //
                        // For now, we will need to rely on the re-purposed ecomm endpoint
                        // with additional parameters
                    //}
                    if (m_Logger != null)
                    {
                        m_Logger.LogWarning("UnityIAP", "Disabling store optimization");
                    }
                }
                else
                {
                    catalogDisabled = false;
                    eventsDisabled = false;
                    isManagedStoreEnabled = true;
                }
            }
        }

        public bool? trackingOptOut
        {
            get
            {
                var profile = ProfileData.Instance(m_Module.util);
                return profile.TrackingOptOut;
            }

            set
            {
                var profile = ProfileData.Instance(m_Module.util);
                profile.SetTrackingOptOut(value);
            }
        }

        public bool storeTestEnabled
        {
            get
            {
                return testStore;
            }
            set
            {
                // Once you start testing you never go back...
                if (testStore == false)
                {
                    testStore = value;
                    var profile = ProfileData.Instance(m_Module.util);
                    profile.SetStoreTestEnabled(value);
                }
            }
        }

        public string baseIapUrl
        {
            get
            {
                return iapBaseUrl;
            }
            set
            {
                if ((iapBaseUrl == null)&&(!String.IsNullOrEmpty(value)))
                {
                    storeTestEnabled = true;
                    iapBaseUrl = value;
                }
            }
        }

        // NB: this doesn't affect Urls provided by webview for Promo
        public string baseEventUrl
        {
            get
            {
                return eventBaseUrl;
            }
            set
            {
                if(!String.IsNullOrEmpty(value))
                {
                    storeTestEnabled = true;
                    eventBaseUrl = value;
                }
            }
        }

        /// <summary>
        /// No arg constructor due to cyclical dependency on IUnityCallback.
        /// </summary>
        public JSONStore()
        {
        }

        /// <summary>
        /// <seealso cref="TransactionLog"/>
        /// </summary>
        /// <param name="persistentDataPath"></param>
        public JSONStore(string persistentDataPath)
        {
            if (!string.IsNullOrEmpty(persistentDataPath))
            {
                m_persistentDataPath = Path.Combine(Path.Combine(persistentDataPath, "Unity"), "UnityPurchasing");
            }
        }

        public void SetNativeStore(INativeStore native) {
            this.store = native;
        }

        void IStoreInternal.SetModule(StandardPurchasingModule module)
        {
            if(module == null)
            {
                return;
            }
            this.m_Module = module;
            if(module.logger != null)
            {
                this.m_Logger = module.logger;
            }
            else
            {
                this.m_Logger = UnityEngine.Debug.unityLogger;
            }
        }

        public override void Initialize (IStoreCallback callback)
        {
            this.unity = callback;
            m_EventQueue = EventQueue.Instance(m_Module.util, m_Module.webUtil);
            m_profileData = ProfileData.Instance(m_Module.util);

            if(m_Module != null)
            {
                var storeName = m_Module.storeInstance.storeName;
                m_profileData.SetStoreName(storeName);
                if (String.IsNullOrEmpty(iapBaseUrl))
                {
                    iapBaseUrl = kIecCatalogBase;
                }
                m_managedStore = StoreCatalogImpl.CreateInstance(storeName, iapBaseUrl, m_Module.webUtil, m_Module.logger, m_Module.util, this);
            }
            else
            {
                if(m_Logger != null)
                {
                    m_Logger.LogWarning("UnityIAP", "JSONStore init has no reference to SPM, can't start managed store");
                }
            }
        }

        public override void RetrieveProducts (ReadOnlyCollection<ProductDefinition> products)
        {
#if HIGH_PERMISSION_DATA
            if ((isManagedStoreEnabled || Application.isEditor) &&
                m_managedStore != null &&
                (isRefreshing || isFirstTimeRetrievingProducts))
            {
                m_BuilderProducts = new HashSet<ProductDefinition>(products);
                m_managedStore.FetchProducts(ProcessManagedStoreResponse);
            }
            else // Fetch Additional Products triggered by developer with IStoreController or managedStore is unavailable
            {
#endif
               store.RetrieveProducts(JSONSerializer.SerializeProductDefs(products));
#if HIGH_PERMISSION_DATA
            }
            isFirstTimeRetrievingProducts = false;
#endif
        }

        internal void ProcessManagedStoreResponse(List<ProductDefinition> storeProducts)
        {
            m_storeCatalog = storeProducts;
            if (isRefreshing)
            {
                isRefreshing = false;
                // Skip native store layer during refresh if catalog contains no information
                if (storeCatalog.Length == 0 && refreshCallback != null)
                {
                    refreshCallback();
                    refreshCallback = null;
                    return;
                }
            }
            var products = new HashSet<ProductDefinition>(m_BuilderProducts);
            if (storeProducts != null) {
                products.UnionWith(storeProducts);
            }
            store.RetrieveProducts (JSONSerializer.SerializeProductDefs (products));
        }

        public override void Purchase (UnityEngine.Purchasing.ProductDefinition product, string developerPayload)
        {
            if(!string.IsNullOrEmpty(developerPayload))
            {
                Dictionary<string, object> dic = null;

                // try and get dev payload dictionary from the the developerPayload string, this may fail if the
                // developer payload is not a json string, so we catch the exception silently.
                try
                {
                     dic = (Dictionary<string, object>) MiniJSON.Json.Deserialize(developerPayload);
                }

                catch { }

                if ( (dic != null) && (dic.ContainsKey("iapPromo")) && (dic.TryGetValue("productId", out var prodId)) )
                {
                    promoPayload = dic;

                    // Add more fields to promoPayload
                    //
                    promoPayload.Add("type", "iap.purchase");
                    promoPayload.Add("iap_service", true);

                    var thisProduct = unity.products.WithID(prodId as string);
                    if (thisProduct != null)
                    {
                        promoPayload.Add("amount", thisProduct.metadata.localizedPrice);
                        promoPayload.Add("currency", thisProduct.metadata.isoCurrencyCode);
                    }

                    // For promotions we want to delete the promo JSON
                    // before sending upstream to stores
                    developerPayload = "iapPromo";
                }
            }
            store.Purchase (JSONSerializer.SerializeProductDef (product), developerPayload);
        }

        public override void FinishTransaction (UnityEngine.Purchasing.ProductDefinition product, string transactionId)
        {
            // Product definitions may be null if a store tells Unity IAP about an unknown product;
            // Unity IAP will not have a corresponding definition but will still finish the transaction.
            var def = product == null ? null : JSONSerializer.SerializeProductDef (product);
            store.FinishTransaction (def, transactionId);
        }

        public void OnSetupFailed (string reason)
        {
            var r = (InitializationFailureReason) Enum.Parse (typeof(InitializationFailureReason), reason, true);
            unity.OnSetupFailed (r);
        }

        public virtual void OnProductsRetrieved (string json)
        {
            // NB: AppleStoreImpl overrides this completely and does not call the base.
            unity.OnProductsRetrieved (JSONSerializer.DeserializeProductDescriptions (json));

            Promo.ProvideProductsToAds(this, unity);
        }

        public virtual void OnPurchaseSucceeded (string id, string receipt, string transactionID)
        {
            if (!eventsDisabled)
            {
                SendPurchaseSucceededEvent(id, receipt, transactionID);
            }
            unity.OnPurchaseSucceeded (id, receipt, transactionID);
            Promo.ProvideProductsToAds(this, unity);
        }

        protected void SendPurchaseSucceededEvent(string id, string receipt, string transactionID)
        {
#if HIGH_PERMISSION_DATA
            Product thisProduct = unity.products.WithStoreSpecificID(id);
            if ((promoPayload != null) &&
                ((id == (string) promoPayload["productId"]) || (id == (string) promoPayload["storeSpecificId"])))
            {
                promoPayload.Add("purchase", "OK");
                if (thisProduct != null)
                {
                    promoPayload.Add("productType", thisProduct.definition.type.ToString());
                }

                var unifiedData = new Dictionary<string, string>();
                unifiedData.Add("data", FormatUnifiedReceipt(receipt, transactionID));
                promoPayload.Add("receipt", unifiedData);

                var purchaseEvent = new PurchasingEvent(promoPayload);
                var profileDict = m_profileData.GetProfileDict();
                var eventjson = purchaseEvent.FlatJSON(profileDict);
                m_EventQueue.SendEvent(eventjson);
                promoPayload.Clear();
                promoPayload = null;
            }
            else
            {
                // enriched "organic" purchases here

                // thisProduct can be null if it was an unexpected product. This can happen if you restore a product
                // that is no longer being requested by the app.
                if (thisProduct != null)
                {
                    var purchaseDict = new Dictionary<string, object>();
                    purchaseDict.Add("type", "iap.purchase");
                    purchaseDict.Add("iap_service", true);
                    purchaseDict.Add("iapPromo", false);
                    purchaseDict.Add("purchase", "OK");
                    purchaseDict.Add("productId", thisProduct.definition.id);
                    purchaseDict.Add("storeSpecificId", thisProduct.definition.storeSpecificId);
                    purchaseDict.Add("amount", thisProduct.metadata.localizedPrice);
                    purchaseDict.Add("currency", thisProduct.metadata.isoCurrencyCode);
                    purchaseDict.Add("productType", thisProduct.definition.type.ToString());

                    var unifiedData = new Dictionary<string, string>();
                    unifiedData.Add("data", FormatUnifiedReceipt(receipt, transactionID));
                    purchaseDict.Add("receipt", unifiedData);

                    var purchaseEvent = new PurchasingEvent(purchaseDict);
                    var profileDict = m_profileData.GetProfileDict();
                    var eventjson = purchaseEvent.FlatJSON(profileDict);

                    m_EventQueue.SendEvent(EventDestType.IAP, eventjson, eventBaseUrl + "/v1/organic_purchase");
                }
            }
#endif
        }

        public void OnPurchaseFailed (string json)
        {
            OnPurchaseFailed(JSONSerializer.DeserializeFailureReason(json), json);
        }

        public void OnPurchaseFailed (PurchaseFailureDescription failure, string json = null)
        {
            if (!eventsDisabled)
            {
                SendPurchaseFailedEvent(failure, json);
            }

            lastPurchaseFailureDescription = failure;
            _lastPurchaseErrorCode = ParseStoreSpecificPurchaseErrorCode(json);

            unity.OnPurchaseFailed(failure);
        }

        protected void SendPurchaseFailedEvent(PurchaseFailureDescription failure, string json)
        {
#if HIGH_PERMISSION_DATA
            if (promoPayload != null)
            {
                promoPayload["type"] = "iap.purchasefailed";
                promoPayload.Add("purchase", "FAILED");
                if (json != null)
                {
                    promoPayload.Add("failureJSON", json);
                }

                var purchaseEvent = new PurchasingEvent(promoPayload);
                var profileDict = m_profileData.GetProfileDict();
                var eventjson = purchaseEvent.FlatJSON(profileDict);

                m_EventQueue.SendEvent(EventDestType.IAP, eventjson); // don't use Ads tracking event here

                promoPayload.Clear();
                promoPayload = null;
            }
            else
            {
                // enriched "organic" purchases here

                Product thisProduct = unity.products.WithStoreSpecificID(failure.productId);

                if (thisProduct != null)
                {
                    var purchaseDict = new Dictionary<string, object>();
                    purchaseDict.Add("type", "iap.purchasefailed");
                    purchaseDict.Add("iap_service", true);
                    purchaseDict.Add("iapPromo", false);
                    purchaseDict.Add("purchase", "FAILED");
                    purchaseDict.Add("productId", thisProduct.definition.id);
                    purchaseDict.Add("storeSpecificId", thisProduct.definition.storeSpecificId);
                    purchaseDict.Add("amount", thisProduct.metadata.localizedPrice);
                    purchaseDict.Add("currency", thisProduct.metadata.isoCurrencyCode);
                    if (json != null)
                    {
                        purchaseDict.Add("failureJSON", json);
                    }

                    var purchaseEvent = new PurchasingEvent(purchaseDict);
                    var profileDict = ProfileData.Instance(m_Module.util).GetProfileDict();
                    var eventjson = purchaseEvent.FlatJSON(profileDict);

                    m_EventQueue.SendEvent(EventDestType.IAP, eventjson, eventBaseUrl + "/v1/organic_purchase");
                }
            }
#endif
        }

        public void RefreshCatalog(Action callback)
        {
            if (isManagedStoreEnabled)
            {
                isRefreshing = true;
                refreshCallback = callback;
                var purchasingManager = unity as PurchasingManager;
                // Failure is null because PurchasingManager's initialized variable is already set so failure callback will never be triggered.
                purchasingManager.FetchAdditionalProducts(m_BuilderProducts, callback, null);
            }
            else
            {
                isRefreshing = false;
                refreshCallback = null;
                m_Logger.LogWarning("UnityIAP", "Unable to refresh catalog because managed store is disabled.");
                callback();
            }
        }

        public PurchaseFailureDescription GetLastPurchaseFailureDescription()
        {
            return lastPurchaseFailureDescription;
        }

        public StoreSpecificPurchaseErrorCode GetLastStoreSpecificPurchaseErrorCode()
        {
            return _lastPurchaseErrorCode;
        }

        /// <summary>
        /// <seealso cref="TransactionLog.HasRecordOf"/>
        /// </summary>
        internal bool HasRecordOf(string transactionID)
        {
            if (string.IsNullOrEmpty(transactionID) || string.IsNullOrEmpty(m_persistentDataPath))
                return false;
            return Directory.Exists(GetRecordPath(transactionID));
        }

        /// <summary>
        /// <seealso cref="TransactionLog.Record"/>
        /// </summary>
        internal void Record(string transactionID)
        {
            // Consumables have additional de-duplication logic.
            if (!(string.IsNullOrEmpty(transactionID) || string.IsNullOrEmpty(m_persistentDataPath)))
            {
                var path = GetRecordPath(transactionID);
                try
                {
                    Directory.CreateDirectory(path);
                }
                catch (Exception e)
                {
                    // A wide variety of exceptions can occur, for all of which
                    // nothing is the best course of action.
                    m_Logger.LogWarning("UnityIAP", "Ignoring transaction ID storage error: " + e.Message);
                }
            }
        }

        /// <summary>
        /// <seealso cref="TransactionLog.GetRecordPath"/>
        /// </summary>
        /// <param name="transactionID"></param>
        /// <returns></returns>
        private string GetRecordPath(string transactionID)
        {
            return Path.Combine(m_persistentDataPath, ComputeHash(transactionID));
        }

        /// <summary>
        /// <seealso cref="TransactionLog.ComputeHash"/>
        /// </summary>
        private string ComputeHash(string transactionID)
        {
            UInt64 hash = 3074457345618258791ul;
            for (int i = 0; i < transactionID.Length; i++)
            {
                hash += transactionID[i];
                hash *= 3074457345618258799ul;
            }

            StringBuilder builder = new StringBuilder(16);
            foreach (byte b in BitConverter.GetBytes(hash))
            {
                builder.AppendFormat("{0:X2}", b);
            }
            return builder.ToString();
        }

        private string FormatUnifiedReceipt(string platformReceipt, string transactionId)
        {
            var dic = new Dictionary<string, object>();
            if(m_Module != null)
            {
                dic["Store"] = m_Module.storeInstance.storeName;
            }
            else
            {
                dic["Store"] = "unknown";
            }

            dic["TransactionID"] = transactionId ?? string.Empty;
            dic["Payload"] = platformReceipt ?? string.Empty;

            return MiniJSON.Json.Serialize(dic);
        }

        private StoreSpecificPurchaseErrorCode ParseStoreSpecificPurchaseErrorCode(string json)
        {
            // If we didn't get any JSON just return Unknown.
            if (json == null)
            {
                return StoreSpecificPurchaseErrorCode.Unknown;
            }

            // If the dictionary contains a storeSpecificErrorCode, return it, otherwise return Unknown.
            var purchaseFailureDictionary = MiniJson.JsonDecode(json) as Dictionary<string, object>;
            if (purchaseFailureDictionary != null && purchaseFailureDictionary.ContainsKey(kStoreSpecificErrorCodeKey) && Enum.IsDefined(typeof(StoreSpecificPurchaseErrorCode), (string) purchaseFailureDictionary[kStoreSpecificErrorCodeKey]))
            {
                string storeSpecificErrorCodeString = (string) purchaseFailureDictionary[kStoreSpecificErrorCodeKey];
                return (StoreSpecificPurchaseErrorCode) Enum.Parse(typeof(StoreSpecificPurchaseErrorCode),
                    storeSpecificErrorCodeString);
            }
            return StoreSpecificPurchaseErrorCode.Unknown;
        }
    }
}
