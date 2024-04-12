using System;
using System.Collections.Generic;
using UnityEngine.Purchasing;
using ExporterValidationResults = UnityEditor.Purchasing.ProductCatalogEditor.ExporterValidationResults;

namespace UnityEditor.Purchasing
{
	internal class CloudJSONProductCatalogExporter : ProductCatalogEditor.IProductCatalogExporter
	{
		private const string kCatalogKey = "catalog";
		private const string kIDKey = "id";
		private const string kTypeKey = "type";
		private const string kNameKey = "name";
		private const string kStoreIDsKey = "store_ids";

		private const string kPriceKey = "price";
		private const string kApplePriceKey = "apple";
		private const string kGooglePriceKey = "google";

		private const string kPayoutsKey = "payouts";
		private const string kPayoutTypeKey = "t";
		private const string kPayoutSubtypeKey = "st";
		private const string kPayoutQuantityKey = "q";
		private const string kPayoutDataKey = "d";

	    private const decimal kMaxGooglePrice = 1000000000m;

		public string DefaultFileName {
			get {
				return "CloudCatalog";
			}
		}

	    public string DisplayName {
			get {
				return "Cloud JSON";
			}
		}

		public string FileExtension {
			get {
				return "json";
			}
		}

		public string StoreName {
			get {
				return DefaultFileName; // NOTE: Made-up name here
			}
		}

		public string MandatoryExportFolder {
			get {
				return null;
			}
		}

		public List<string> FilesToCopy {
            get {
                return null;
            }
        }

        public bool SaveCompletePackage {
            get {
                return false;
            }
        }

		public string Export(ProductCatalog catalog)
		{
			List<object> catalogList = new List<object>();

			foreach (var item in catalog.allProducts) {
				Dictionary<string, object> itemDict = new Dictionary<string, object>();

				itemDict[kIDKey] = item.id;
				itemDict[kTypeKey] = item.type.ToString();
				itemDict[kNameKey] = item.defaultDescription.Title;

				Dictionary<string, string> storeIDsDict = new Dictionary<string, string>();
				foreach (var storeID in item.allStoreIDs) {
					storeIDsDict[storeID.store] = storeID.id;
				}
				itemDict[kStoreIDsKey] = storeIDsDict;

				Dictionary<string, object> priceDict = new Dictionary<string, object> ();
				priceDict [kApplePriceKey] = ApplePriceTiers.ActualDollarsForAppleTier(item.applePriceTier);
				priceDict [kGooglePriceKey] = Convert.ToDouble(item.googlePrice.value);
				itemDict [kPriceKey] = priceDict;

				var payouts = new List<Dictionary<string, object>> ();
				foreach (var p in item.Payouts) {
					var payout = new Dictionary<string, object> ();
					payout [kPayoutTypeKey] = p.typeString;
					payout [kPayoutSubtypeKey] = p.subtype;
					payout [kPayoutQuantityKey] = p.quantity;
					payout [kPayoutDataKey] = p.data;
					payouts.Add (payout);
				}
				itemDict [kPayoutsKey] = payouts.ToArray();

				catalogList.Add(itemDict);
			}

			Dictionary<string, object> dict = new Dictionary<string, object>();
			dict[kCatalogKey] = catalogList;
			return MiniJson.JsonEncode(dict);
		}

		public ExporterValidationResults Validate(ProductCatalogItem item)
		{
			var results = new ExporterValidationResults();

			if (string.IsNullOrEmpty(item.id)) {
				results.fieldErrors["id"] = "ID field is required for cloud upload";
			}

			if (string.IsNullOrEmpty(item.defaultDescription.Title)) {
				results.fieldErrors["defaultDescription.Title"] = "Title is required for cloud upload";
			}

		    if (item.googlePrice.value < 0)
		    {
		        results.fieldErrors["googlePrice"] = "Google price cannot be less than 0";
		    }

		    if (item.googlePrice.value > kMaxGooglePrice)
		    {
		        results.fieldErrors["googlePrice"] = string.Format("Google price cannot be greater than {0}", kMaxGooglePrice);
		    }

			return results;
		}

		public ExporterValidationResults Validate(ProductCatalog catalog)
		{
			var results = new ExporterValidationResults();

			// Warn if exporting an empty catalog
			if (catalog.allProducts.Count == 0) {
				results.warnings.Add("Catalog is empty");
			}

			// Check for duplicate IDs
			var usedIds = new HashSet<string>();
			foreach (var product in catalog.allProducts) {
				if (usedIds.Contains(product.id)) {
					results.errors.Add("More than one product uses the ID \"" + product.id + "\"");
				}
				usedIds.Add(product.id);
			}

			return results;
		}

		public ProductCatalog NormalizeToType(ProductCatalog catalog)
		{
			return catalog;
		}
	}
}
