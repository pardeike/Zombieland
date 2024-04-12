# Changelog

## [3.2.2] - 2021-06-02
### Fixed
- WebGL - While WebGL is not supported with an included app store implementation, the WebGL Player will no longer crash when the `StandardPurchasingModule.Initialize` API is called if Project Settings > Player > WebGL > Publishing Settings > Enable Exceptions > "Explicitly Thrown Exceptions Only" or "None" are set.
- Amazon - Better support for Android R8 compiler. Added minification (Project Settings > Player > Publishing Settings > Minify) "keep" ProGuard rules.

## [3.2.1] - 2021-05-18
### Changed
- Manual and API documentation updated. 

## [3.2.0] - 2021-05-17
### Added
- GooglePlay - Automatic resumption of initialization when a user's device initially does not have a Google account, and they correct that Android setting without killing the app, then they resume the app. NOTE this does not impact Unity IAP's behavior when a user removes their Google account after initialization.
- GooglePlay - API `IGooglePlayConfiguration.SetServiceDisconnectAtInitializeListener(Action)` called when Unity IAP fails to connect to the underlying Google Play Billing service. The `Action` may be called multiple times after `UnityPurchasing.Initialize` if a user does not have a Google account added to their Android device. Initialization of Unity IAP will remain paused until this is corrected. Inform the user they must add a Google account in order to be able to purchase. See documentation "Store Guides" > "Google Play" for a sample usage.
- GooglePlay - It is now possible to check if a purchased product is pending or not by calling IsPurchasedProductDeferred() from GooglePlayStoreExtensions.
- UDP - RegisterPurchaseDeferredListener in IUDPExtensions can be used to assign a callback for pending purchases.

### Fixed
- GooglePlay - Receipts for Pending purchases are now UnifiedReceipts and not raw Google receipts. Any parsers you have for these can extract the raw receipt json by parsing the "Payload" field.
- Editor - The Fake Store UI used in Play Mode in the Editor, as well as some unsupported platforms has been restored. A null reference exception when trying to make a purchase no longer occurs.
- UDP - Added a null check when comparing Store-Specific IDs

### Changed:
- Samsung Galaxy - Support is being deprecated when not using Unity Distribution Portal as a target. The feature will be removed soon. Please use the Unity Distribution Portal package with IAP for full Samsung Galaxy support.

## [3.1.0] - 2021-04-15
### Added
- GooglePlay - Google Play Billing Library version 3.0.3.
  - Fixes a broken purchase flow when user resumed their app through the Android Launcher after interrupting an ongoing purchase. Now `IStoreListener.OnPurchaseFailed(PurchaseFailureDescription.reason: PurchaseFailureReason.UserCancelled)` is called on resumption. E.g. first the user initiates a purchase, then sees the Google purchasing dialog, and sends their app to the background via the device's Home key. They tap the app's icon in the Launcher, see no dialog, and, finally, the app will now receive this callback.

### Changed
- `string StandardPurchasingModule.k_PackageVersion` is obsolete and will incorrectly report `"3.0.1"`. Please use the new `string StandardPurchasingModule.Version` to read the correct current version of this package.
- Reduced logging, and corrected the severity of several logs.

### Fixed
- tvOS - build errors due to undesirable call to `[[SKPaymentQueue defaultQueue] presentCodeRedemptionSheet]` which will now only be used for iOS 14.
- tvOS, macOS - Builds missing Xcode project In-App Purchasing capability and StoreKit.framework.
- Security - Tangle files causing compilation errors on platforms not supported by Security: non-GooglePlay and non-Apple.
- GooglePlay - Subscription upgrade/downgrade using proration mode [DEFERRED](https://developer.android.com/reference/com/android/billingclient/api/BillingFlowParams.ProrationMode#DEFERRED) (via `IGooglePlayStoreExtensions.UpgradeDowngradeSubscription(string oldSku, string newSku, int desiredProrationMode)`) reported `OnPurchaseFailed` with `PurchaseFailureReason.Unknown`, when the deferred subscription upgrade/downgrade succeeded. This subscription change generates no immediate transaction and no receipt. Now a custom `Action<Product>` can be called when the change succeeds, and is set by the new `SetDeferredProrationUpgradeDowngradeSubscriptionListener` API:
  - Adds `IGooglePlayStoreExtensions.SetDeferredProrationUpgradeDowngradeSubscriptionListener(Action<Product> action)`. Sets listener for deferred subscription change events. Deferred subscription changes only take effect at the renewal cycle and no transaction is done immediately, therefore there is no receipt nor token. The `Action<Product>` is the deferred subscription change event. No payout is granted here. Instead, notify the user that the subscription change will take effect at the next renewal cycle.

## [3.0.2] - 2021-03-30

### Added
- Comprehensive manual and API documentation.

## [3.0.1] - 2021-03-08
### Removed
- Pre-release disclaimer.

## [3.0.0] - 2021-03-05

## [3.0.0-pre.7] - 2021-03-03
### Added
- GooglePlay - populate `Product.receipt` for `Action<Product>` parameter returned by `IGooglePlayStoreExtensions.SetDeferredPurchaseListener` callback

### Changed 
- WinRT - This feature is now shipped as C# code under assembly definitions instead of .dll files.
- Security - This feature is now shipped as C# code under assembly definitions instead of .dll files.
- Receipt Validation Obfuscator - The Tangle File Obfuscate function is now Editor-only and no longer part of the Runtime Security module.

### Fixed
- Windows Standalone - launches FakeStore when detected by StandardPurchasingModule; disentangled from WinRT
- Security - restored Receipt Validation Obfuscator Editor functionality
- GooglePlay - fix regression, avoiding exception when using IGooglePlayConfiguration while running on a non-Google target

## [3.0.0-pre.6] - 2021-02-09
### Fixed
- WinRT - There was a bad path being pointed to by the .dll's meta file, preventing compilation to this target.

## [3.0.0-pre.5] - 2021-01-12
### Added
- Apple - Support for [auto-renewable subscription Offer Codes](https://developer.apple.com/documentation/storekit/in-app_purchase/subscriptions_and_offers/implementing_offer_codes_in_your_app) on iOS and iPadOS 14 and later via `IAppleExtensions.PresentOfferRedemptionSheet()`. E.g. 

 ```csharp
public void ShowSubscriptionOfferRedemption(IExtensionProvider extensions)
{
    var appleExtensions = extensions.GetExtension<IAppleExtensions>();
    appleExtensions.PresentOfferRedemptionSheet();
}
```

### Fixed
 - Security and WinRT stub dlls and references to Analytics no longer break builds unsupported platforms like PS4, XboxOne, Switch and Lumin. These platforms are still unsupported but will no longer raise errors on build.  

### Removed
- Support for Facebook in-app purchasing is no longer provided. All classes and implementations have been removed.

## [3.0.0-pre.4] - 2020-10-09
- Fix builds for UWP

## [3.0.0-pre.3] - 2020-10-09
- First integration into Unity 2021
- Includes changes listed in [CHANGELOG-ASSETSTORE.md](CHANGELOG-ASSETSTORE.md), starting from version 1, ending 2020-10-09
- **This is the first release of the Unified *Unity In App Purchasing*, combining the old package and its Asset Store Components.**

## [2.2.2] - 2021-01-19
- Fixed logs incorrectly formatted showing “purchases({0}): -id of product-”
- Renamed method IStoreCallback.OnPurchasesRetrieved to IStoreCallback.OnAllPurchasesRetrieved, deprecated old method name. This is to fix a problem when refreshing receipts.

## [2.2.1] - 2020-11-19
- Fixed exposure of function calls at runtime used by the Asset Store Package 2.2.0 and up.

## [2.2.0] - 2020-10-22
- Google Billing v3

## [2.1.2] - 2020-09-20
Fix migration tooling's obfuscator file destination path to target Scripts instead of Resources

## [2.1.1] - 2020-08-25
- Fix compilation compatibility with platforms that don't use Unity Analytics (ex: PS4)
- Fix compilation compatibility with "Scripting Runtime Version" option set to ".Net 3.5 Equivalent (Deprecated)" in Unity 2018.4

## [2.1.0] - 2020-06-29
- Source Code provided instead of precompiled dlls.
- Live vs Stub DLLs are now using asmdef files to differentiate their targeting via the Editor
- Fixed errors regarding failing to find assemblies when toggling In-App Purchasing in the Service Window or Purchasing Service Settings
- Fixed failure to find UI assemblies when updating the Editor version.
- Added menu to support eventual migration to In-App Purchasing version 3.

## [2.0.6] - 2019-02-18
- Remove embedded prebuilt assemblies.

## [2.0.5] - 2019-02-08
- Fixed Unsupported platform error

## [2.0.4] - 2019-01-20
- Added editor and playmode testing.

## [2.0.3] - 2018-06-14
- Fixed issue related to 2.0.2 that caused new projects to not compile in the editor. 
- Engine dll is enabled for editor by default.
- Removed meta data that disabled engine dll for windows store.

## [2.0.2] - 2018-06-12
- Fixed issue where TypeLoadException occured while using "UnityEngine.Purchasing" because SimpleJson was not found. fogbugzId: 1035663.

## [2.0.1] - 2018-02-14
- Fixed issue where importing the asset store package would fail due to importer settings.

## [2.0.0] - 2018-02-07
- Fixed issue with IAP_PURCHASING flag not set on project load.
