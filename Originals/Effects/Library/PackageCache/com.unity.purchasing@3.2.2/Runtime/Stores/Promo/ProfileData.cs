using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using Uniject;
using UnityEngine;




// User Profile

// really a combination of various device, user and server-provided IDs that are being
// used in a "profile"-ish way

// #define HIGH_PERMISSION_DATA // NOTE: Used elsewhere in project, IAP-1647

namespace UnityEngine.Purchasing
{

    internal class ProfileData
    {
        IUtil m_Util;
        private static ProfileData ProfileInstance;

        public string AppId { get; internal set; }

        public string UserId { get; internal set; }
        public ulong SessionId { get; internal set; }

        public string Platform { get; internal set; }
        public int PlatformId { get; internal set; }

        public string SdkVer { get; internal set; }
        public string OsVer { get; internal set; }
        public int ScreenWidth { get; internal set; }
        public int ScreenHeight { get; internal set; }
        public float ScreenDpi { get; internal set; }
        public string ScreenOrientation { get; internal set; }

        public string DeviceId { get; internal set; }

        public string BuildGUID { get; internal set; }

        public string IapVer { get; internal set; }

        public string AdsGamerToken { get; internal set; }
        public bool? TrackingOptOut { get; internal set; }
        public int? AdsABGroup { get; internal set; }
        public string AdsGameId { get; internal set; }

        public int? StoreABGroup { get; internal set; }
        public string CatalogId { get; internal set; }
        public string MonetizationId { get; internal set; }
        public string StoreName { get; internal set; }
        public string GameVersion { get; internal set; }

        public bool? StoreTestEnabled { get; internal set; }

        const string kConnectSessionInfoApplicationId = "appid";
        const string kConnectSessionInfoUserId = "userid";
        const string kConnectSessionInfoSessionId = "sessionid";
        const string kConnectSessionInfoPlatformName = "platform";
        const string kConnectSessionInfoPlatformId = "platformid";
        const string kConnectSessionInfoSdkVersion = "sdk_ver";
        const string kConnectSessionInfoDeviceId = "deviceid";
        const string kConnectSessionInfoBuildGuid = "build_guid";

        const string kConnectSessionInfoIapVersion = "iap_ver";

        // Not really from ConnectSessionInfo
        const string kConnectSessionInfoAdsGamerToken = "gamerToken";
        const string kConnectSessionInfoAdsTrackingOptOut = "trackingOptOut";
        const string kConnectSessionInfoAdsGameId = "gameId";
        const string kConnectSessionInfoAdsABGroup = "abGroup";

        const string kConnectSessionInfoStoreABGroup = "store_abgroup";
        const string kConnectSessionInfoCatalogId = "catalogid";
        const string kConnectSessionInfoMonetizationId = "umpid";
        const string kConnectSessionInfoStoreTest = "iap_test";
        const string kConnectSessionInfoStoreName = "store";
        const string kConnectSessionInfoGameVersion = "game_ver";


        // DeviceInfo Shenanigans for iap-events-coordinator
        const string kOsVersion  = "osv"; // "os_ver" for DeviceInfo
        const string kDpi = "ppi"; // "dpi" for DeviceInfo
        const string kWidth = "w";
        const string kHeight = "h";
        const string kOrient = "orient";


// These are modified copy from engine ConnectSessionInfo.cpp
// const string kConnectSessionInfoDebugDevice = "debug_device";
// const string kConnectSessionInfoCloudUserId = "clouduserid";
// const string kConnectSessionInfoCloudProjectId = "cloudprojectid";
// const string kConnectSessionInfoOrganizationId = "organizationid";
// const string kConnectSessionInfoLocalProjectId = "localprojectid";
// const string kConnectSessionInfoMachineId = "machineid";
// const string kConnectSessionInfoLicenseHash = "license_hash";


        private ProfileData(IUtil util)
        {
            m_Util = util;
            // Get what we can from internal sources
            AppId = util.cloudProjectId;
            if(AppId.Length == 0)
            {
                AppId = "Unknown";
            }
            Platform = util.platform.ToString();
            PlatformId = (int)util.platform;
            SdkVer = util.unityVersion;
            OsVer = util.operatingSystem;
#if HIGH_PERMISSION_DATA
            DeviceId = util.deviceUniqueIdentifier;
#else
            DeviceId = SystemInfo.unsupportedIdentifier;
#endif
            GameVersion = util.gameVersion;

            IapVer = Promo.Version();

            // These are available in PlayerPrefs for 5.4+
            UserId = util.userId;
            SessionId = util.sessionId;

            // Adding limited device info to IAP events (because Ads)
            ScreenWidth = util.screenWidth;
            ScreenHeight = util.screenHeight;
            ScreenDpi = util.screenDpi;
            ScreenOrientation = util.screenOrientation; // update on send

            // this is 5.6+ so we should flip to reflection or drop
            // BuildGUID = Application.buildGUID;
            // BuildGUID = String.Empty;
        }

        internal Dictionary<string, object> GetProfileDict()
        {
            var result = new Dictionary<string, object>();

            result.Add(kConnectSessionInfoApplicationId, AppId);
            result.Add(kConnectSessionInfoPlatformName, Platform);
            result.Add(kConnectSessionInfoPlatformId, PlatformId);
            if(!String.IsNullOrEmpty(AdsGameId))
            {
                result.Add(kConnectSessionInfoAdsGameId, AdsGameId);
            }
            result.Add(kConnectSessionInfoSdkVersion, SdkVer);
            if(DeviceId != SystemInfo.unsupportedIdentifier)
            {
                result.Add(kConnectSessionInfoDeviceId, DeviceId);
            }
            if(!String.IsNullOrEmpty(UserId))
            {
                result.Add(kConnectSessionInfoUserId, UserId);
            }
            if(SessionId != 0)
            {
                result.Add(kConnectSessionInfoSessionId, SessionId);
            }
            if(!String.IsNullOrEmpty(BuildGUID))
            {
                result.Add(kConnectSessionInfoBuildGuid, BuildGUID);
            }
            if(!String.IsNullOrEmpty(IapVer))
            {
                result.Add(kConnectSessionInfoIapVersion, IapVer);
            }
            if(!String.IsNullOrEmpty(AdsGamerToken))
            {
                result.Add(kConnectSessionInfoAdsGamerToken, AdsGamerToken);
            }
            if (TrackingOptOut != null) {
                result.Add(kConnectSessionInfoAdsTrackingOptOut, TrackingOptOut);
            }
            if(AdsABGroup != null)
            {
                result.Add(kConnectSessionInfoAdsABGroup, AdsABGroup);
            }
            if(StoreABGroup != null)
            {
                result.Add(kConnectSessionInfoStoreABGroup, StoreABGroup);
            }
            if(!String.IsNullOrEmpty(CatalogId))
            {
                result.Add(kConnectSessionInfoCatalogId, CatalogId);
            }
            if(StoreTestEnabled != null)
            {
                result.Add(kConnectSessionInfoStoreTest, StoreTestEnabled);
            }
            if (!String.IsNullOrEmpty(StoreName)) {
                result.Add(kConnectSessionInfoStoreName, StoreName);
            }
            if (!String.IsNullOrEmpty(GameVersion)) {
                result.Add(kConnectSessionInfoGameVersion, GameVersion);
            }

            if (!String.IsNullOrEmpty(OsVer)) {
                result.Add(kOsVersion, OsVer);
            }
            result.Add(kWidth, ScreenWidth);
            result.Add(kHeight, ScreenHeight);
            result.Add(kDpi, ScreenDpi);
            // Update on Send (does Ads expect width & height to change?)
            ScreenOrientation = m_Util.screenOrientation;
            if (!String.IsNullOrEmpty(ScreenOrientation)) {
                result.Add(kOrient, ScreenOrientation);
            }

            return result;
        }

        internal Dictionary<string, object> GetProfileIds()
        {
            var result = new Dictionary<string, object>();

            // Post e-comm ID set for counter
            result.Add(kConnectSessionInfoPlatformName, Platform);
            result.Add(kConnectSessionInfoSdkVersion, SdkVer);
            result.Add(kConnectSessionInfoIapVersion, IapVer);
            result.Add(kConnectSessionInfoApplicationId, AppId);

#if HIGH_PERMISSION_DATA
            if(DeviceId != SystemInfo.unsupportedIdentifier)
            {
                result.Add(kConnectSessionInfoDeviceId, DeviceId);
            }
            if(!String.IsNullOrEmpty(UserId))
            {
                result.Add(kConnectSessionInfoUserId, UserId);
            }
            if(!String.IsNullOrEmpty(AdsGamerToken))
            {
                result.Add(kConnectSessionInfoAdsGamerToken, AdsGamerToken);
            }
            if (TrackingOptOut != null) {
                result.Add(kConnectSessionInfoAdsTrackingOptOut, TrackingOptOut);
            }
            if(!String.IsNullOrEmpty(MonetizationId))
            {
                result.Add(kConnectSessionInfoMonetizationId, MonetizationId);
            }
            if (SessionId != 0) {
                var sessionIdString = Convert.ToString(SessionId);
                if (!String.IsNullOrEmpty(sessionIdString))
                {
                    result.Add(kConnectSessionInfoSessionId, sessionIdString);
                }
            }

#endif

            return result;
        }

        internal static ProfileData Instance(IUtil util)
        {
            if(ProfileInstance == null)
            {
                ProfileInstance = new ProfileData(util);

            }
            return ProfileInstance;
        }

        internal void SetGamerToken(string gamerToken)
        {
            if(!String.IsNullOrEmpty(gamerToken))
            {
                AdsGamerToken = gamerToken;
            }
        }

        internal void SetTrackingOptOut(bool? trackingOptOut)
        {
            if (trackingOptOut != null) {
                TrackingOptOut = trackingOptOut;
            }
        }

        internal void SetGameId(string gameid)
        {
            if(!String.IsNullOrEmpty(gameid))
            {
                AdsGameId = gameid;
            }
        }

        internal void SetABGroup(int? abgroup)
        {
            // Was told Ads ABGroup will not be 0 -- should confirm that...
            if((abgroup != null)&&(abgroup > 0))
            {
                AdsABGroup = abgroup;
            }
        }

        internal void SetStoreABGroup(int? abgroup)
        {
            // IEC ABGroup can be 0
            if(abgroup != null)
            {
                StoreABGroup = abgroup;
            }
        }

        internal void SetCatalogId(string storeid)
        {
            if(storeid != null) // allow an empty string
            {
                CatalogId = storeid;
            }
        }

        internal void SetMonetizationId(string umpid)
        {
            if(!String.IsNullOrEmpty(umpid))
            {
                MonetizationId = umpid;
            }
        }

        internal void SetStoreTestEnabled(bool enable)
        {
            StoreTestEnabled = enable;
        }

        internal void SetStoreName(string storename)
        {
            if (!String.IsNullOrEmpty(storename)) {
                StoreName = storename;
            }
        }

    }
}
