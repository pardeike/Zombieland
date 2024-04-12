using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEditor.Callbacks;
using UnityEditor.Connect;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

namespace UnityEditor.Purchasing {

    /// <summary>
    /// Editor tools to set build-time configurations for app stores.
    /// </summary>
    [InitializeOnLoad]
    public static class UnityPurchasingEditor
    {
        private const string UdpPackageName = "com.unity.purchasing.udp";

        private const string ModePath = "Assets/Resources/BillingMode.json";
        private const string prevModePath = "Assets/Plugins/UnityPurchasing/Resources/BillingMode.json";
        private static ListRequest m_ListRequestOfPackage;
        private static bool m_UmpPackageInstalled;
        private const string BinPath = "Packages/com.unity.purchasing/Plugins/UnityPurchasing/Android";
        private const string AssetStoreUdpBinPath = "Assets/Plugins/UDP/Android";
        private static readonly string PackManUdpBinPath = $"Packages/{UdpPackageName}/Android";

        private static bool hasRegisteredForPlaymodeStateChanges = false;

        private static StoreConfiguration config;

        private static readonly bool s_udpAvailable = UdpSynchronizationApi.CheckUdpAvailability();

        internal const string MenuItemRoot = "Window/" + PurchasingDisplayName;
        internal const string PurchasingDisplayName = "Unity IAP";

        // Check if UDP upm package is installed.
        internal static bool IsUdpUmpPackageInstalled()
        {
            if (m_ListRequestOfPackage == null || m_ListRequestOfPackage.IsCompleted)
            {
                return m_UmpPackageInstalled;
            }
            else
            {
                //As a backup, don't block user if the default location is present.
                return File.Exists($"Packages/{UdpPackageName}/package.json");
            }
        }

        private static void ListingCurrentPackageProgress()
        {
            if (m_ListRequestOfPackage.IsCompleted)
            {
                m_UmpPackageInstalled = false;
                EditorApplication.update -= ListingCurrentPackageProgress;
                if (m_ListRequestOfPackage.Status == StatusCode.Success)
                {
                    foreach (var package in m_ListRequestOfPackage.Result)
                    {
                        if (package.name.Equals(UdpPackageName))
                        {
                            m_UmpPackageInstalled = true;
                            break;
                        }
                    }
                }
                else if (m_ListRequestOfPackage.Status >= StatusCode.Failure)
                {
                    Debug.LogError(m_ListRequestOfPackage.Error.message);
                }
            }
        }

        internal static bool IsUdpAssetStorePackageInstalled()
        {
            return File.Exists("Assets/UDP/UDP.dll") || File.Exists("Assets/Plugins/UDP/UDP.dll");
        }

        [InitializeOnLoadMethod]
        private static void CheckUdpUmpPackageInstalled()
        {
            m_ListRequestOfPackage = Client.List();
            EditorApplication.update += ListingCurrentPackageProgress;
        }

        /// <summary>
        /// Since we are changing the billing mode's location, it may be necessary to migrate existing billing
        /// mode file to the new location.
        /// </summary>
        [InitializeOnLoadMethod]
        internal static void MigrateBillingMode()
        {
            try
            {
                FileInfo file = new FileInfo(ModePath);
                // This will create the new billing file location, if it already exists, this will not do anything.
                file.Directory.Create();

                // See if the file already exists in the new location.
                if (File.Exists(ModePath))
                {
                    return;
                }

                // check if the old exists before moving it
                if (DoesPrevModePathExist())
                {
                    AssetDatabase.MoveAsset(prevModePath, ModePath);
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        internal static bool DoesPrevModePathExist()
        {
            return File.Exists(prevModePath);
        }

        // Notice: Multiple files per target supported. While Key must be unique, Value can be duplicated!
        private static Dictionary<string, AppStore> StoreSpecificFiles = new Dictionary<string, AppStore>()
        {
            {"billing-3.0.3.aar", AppStore.GooglePlay},
            {"AmazonAppStore.aar", AppStore.AmazonAppStore},
            {"SamsungApps.aar", AppStore.SamsungApps},
        };
        private static Dictionary<string, AppStore> UdpSpecificFiles = new Dictionary<string, AppStore>() {
            { "udp.aar", AppStore.UDP},
            { "udpsandbox.aar", AppStore.UDP},
            { "utils.aar", AppStore.UDP}
        };

        // Create or read BillingMode.json at Project Editor load
        static UnityPurchasingEditor()
        {
            EditorApplication.delayCall += () => {
                if (File.Exists(ModePath)) {
                    config = StoreConfiguration.Deserialize(File.ReadAllText(ModePath));
                    RefreshCheckmarks();
                } else {
                    // New project. Create default BillingMode.json.
                    TargetAndroidStore(AppStore.GooglePlay);
                }
            };

            if (!hasRegisteredForPlaymodeStateChanges) {
#if UNITY_2017_2_OR_NEWER
                EditorApplication.playModeStateChanged += RefreshCheckmarksOnPlaymodeState;
#else
                EditorApplication.playmodeStateChanged += RefreshCheckmarks;
#endif
            }
        }

        private const string AmazonMenuItem = MenuItemRoot + "/Android/Target Amazon";
        [MenuItem(AmazonMenuItem, false, 200)]
        private static void TargetAmazon()
        {
            TargetAndroidStore(AppStore.AmazonAppStore);
        }
        // HACK required to enable setting of checkmarks on project load
        [MenuItem(AmazonMenuItem, true, 200)]
        private static bool ValidateAmazon()
        {
            RefreshCheckmarks();
            return true;
        }

        private const string GooglePlayMenuItem = MenuItemRoot + "/Android/Target Google Play";
        [MenuItem(GooglePlayMenuItem, false, 200)]
        private static void TargetGooglePlay()
        {
            TargetAndroidStore(AppStore.GooglePlay);
        }
        // HACK required to enable setting of checkmarks on project load
        [MenuItem(GooglePlayMenuItem, true, 200)]
        private static bool ValidateGooglePlay()
        {
            RefreshCheckmarks();
            return true;
        }

        private const string SamsungAppsMenuItem = MenuItemRoot + "/Android/Target Samsung Galaxy Apps";
        [MenuItem(SamsungAppsMenuItem, false, 200)]
        private static void TargetSamsungApps()
        {
            TargetAndroidStore(AppStore.SamsungApps);
        }
        // HACK required to enable setting of checkmarks on project load
        [MenuItem(SamsungAppsMenuItem, true, 200)]
        private static bool ValidateSamsungApps()
        {
            RefreshCheckmarks();
            return true;
        }

        private const string UdpMenuItem = MenuItemRoot + "/Android/Target Unity Distribution Portal (UDP)";
        [MenuItem(UdpMenuItem, false, 200)]
        private static void TargetUdp()
        {
            if (s_udpAvailable && (IsUdpUmpPackageInstalled() || IsUdpAssetStorePackageInstalled()) && UdpSynchronizationApi.CheckUdpCompatibility())
            {
                TargetAndroidStore(AppStore.UDP);
            }
            else
            {
                UdpInstaller.PromptUdpInstallation();
            }
        }

        // HACK required to enable setting of checkmarks on project load
        [MenuItem(UdpMenuItem, true, 200)]
        private static bool ValidateUdp()
        {
            // If UDP is not available, the menu item will be disabled
            if (UdpSynchronizationApi.CheckUdpAvailability())
            {
                RefreshCheckmarks();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Target a specified Android store.
        /// This sets the correct plugin importer settings for the store
        /// and writes the choice to BillingMode.json so the player
        /// can choose the correct store API at runtime.
        /// </summary>
        /// <param name="target">App store to enable for next build</param>
        public static void TargetAndroidStore(AppStore target)
        {
            if (((int)target < (int)AppStoreMeta.AndroidStoreStart || (int)target > (int)AppStoreMeta.AndroidStoreEnd) &&
                target != AppStore.NotSpecified)
            {
                throw new ArgumentException(string.Format("AppStore parameter ({0}) must be an Android app store", target));
            }

            if (target == AppStore.SamsungApps)
            {
                Debug.LogWarning(
                    AppStore.SamsungApps +
                    " is obsolete and will be removed in v4. Please Use Unity Distribution Platform for Samsung Galaxy Apps support");
            }
            ConfigureProject(target);
            UpdateCheckmarks(target);
            SaveConfig(target);
        }
        /// <summary>
        /// Target a specified Android store.
        /// This sets the correct plugin importer settings for the store
        /// and writes the choice to BillingMode.json so the player
        /// can choose the correct store API at runtime.
        /// </summary>
        /// <see cref="TargetAndroidStore(UnityEngine.Purchasing.AppStore)"/>
        /// <param name="target">App store to enable for next build</param>
        [System.Obsolete("Use TargetAndroidStore(AppStore) instead")]
        public static void TargetAndroidStore(AndroidStore target)
        {
            AppStore appStore = AppStore.NotSpecified;
            try
            {
                appStore = (AppStore) Enum.Parse(typeof(AppStore), target.ToString());
            }
            catch (Exception)
            {
                // No-op
            }
            TargetAndroidStore(appStore);
        }

        // Unfortunately the UnityEditor API updates only the in-memory list of
        // files available to the build when what we want is a persistent modification
        // to the .meta files. So we must also rely upon the PostProcessScene attribute
        // below to process the
        private static void ConfigureProject(AppStore target)
        {
            foreach (var mapping in StoreSpecificFiles) {
                // All files enabled when store is determined at runtime.
                var enabled = target == AppStore.NotSpecified;
                // Otherwise this file must be needed on the target.
                enabled |= mapping.Value == target;

                string path = string.Format("{0}/{1}", BinPath, mapping.Key);
                PluginImporter importer = ((PluginImporter)PluginImporter.GetAtPath(path));

                if (importer != null) {
                    importer.SetCompatibleWithPlatform (BuildTarget.Android, enabled);
                } else {
                    // Search for any occurrence of this file
                    // Only fail if more than one found
                    string[] paths = FindPaths(mapping.Key);

                    if (paths.Length == 1) {
                        importer = ((PluginImporter)PluginImporter.GetAtPath(paths[0]));
                        importer.SetCompatibleWithPlatform(BuildTarget.Android, enabled);
                    }
                }
            }

            var UdpBinPath = IsUdpUmpPackageInstalled() ? PackManUdpBinPath :
                IsUdpAssetStorePackageInstalled() ? AssetStoreUdpBinPath :
                null;

            if (s_udpAvailable && !string.IsNullOrEmpty(UdpBinPath))
            {
                foreach (var mapping in UdpSpecificFiles)
                {
                    // All files enabled when store is determined at runtime.
                    var enabled = target == AppStore.NotSpecified;
                    // Otherwise this file must be needed on the target.
                    enabled |= mapping.Value == target;

                    var path = $"{UdpBinPath}/{mapping.Key}";
                    PluginImporter importer = ((PluginImporter) PluginImporter.GetAtPath(path));

                    if (importer != null)
                    {
                        importer.SetCompatibleWithPlatform(BuildTarget.Android, enabled);
                    }
                    else
                    {
                        // Search for any occurrence of this file
                        // Only fail if more than one found
                        string[] paths = FindPaths(mapping.Key);

                        if (paths.Length == 1)
                        {
                            importer = ((PluginImporter) PluginImporter.GetAtPath(paths[0]));
                            importer.SetCompatibleWithPlatform(BuildTarget.Android, enabled);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// To enable or disable importation of assets at build-time, collect Project-relative
        /// paths matching <paramref name="filename"/>.
        /// </summary>
        /// <param name="filename">Name of file to search for in this Project</param>
        /// <returns>Relative paths matching <paramref name="filename"/></returns>
        public static string[] FindPaths(string filename)
        {
            List<string> paths = new List<string>();

            string[] guids = AssetDatabase.FindAssets(Path.GetFileNameWithoutExtension(filename));

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string foundFilename = Path.GetFileName(path);

                if (filename == foundFilename)
                {
                    paths.Add(path);
                }
            }

            return paths.ToArray();
        }

        private static void SaveConfig(AppStore enabled)
        {
            var configToSave = new StoreConfiguration (enabled);
            File.WriteAllText(ModePath, StoreConfiguration.Serialize(configToSave));
            AssetDatabase.ImportAsset(ModePath);
            config = configToSave;
        }


#if UNITY_2017_1_OR_NEWER
        private static void RefreshCheckmarksOnPlaymodeState(PlayModeStateChange state)
        {
            RefreshCheckmarks();
        }
#endif
        private static void RefreshCheckmarks()
        {
            if (config == null) {
                return;
            }

            try
            {
                UpdateCheckmarks(config.androidStore);
            }
            catch (Exception)
            {
                // Ignored
            }
        }

        private static void UpdateCheckmarks(AppStore target)
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            if (null == Array.Find<System.Reflection.Assembly>(assemblies, (a) => a.GetName().Name == "UnityEngine.Purchasing")) {
                // If the assembly is not available, the menu items below won't exist
                Debug.LogError("The Unity IAP plugin is installed, but Unity IAP is disabled. Please enable Unity IAP in the Services window.");
                return;
            }

            // GooglePlay is default when none specified

            Menu.SetChecked(AmazonMenuItem, target == AppStore.AmazonAppStore);
            Menu.SetChecked(GooglePlayMenuItem, target == AppStore.GooglePlay || target == AppStore.NotSpecified);
            Menu.SetChecked(SamsungAppsMenuItem, target == AppStore.SamsungApps);
            Menu.SetChecked(UdpMenuItem, target == AppStore.UDP);
        }

        // Run me to configure the project's set of Android stores before build
        [PostProcessSceneAttribute(0)]
        private static void OnPostProcessScene()
        {
            if (File.Exists(ModePath))
            {
                try {
                    config = StoreConfiguration.Deserialize(File.ReadAllText(ModePath));
                    ConfigureProject(config.androidStore);
                } catch (Exception e) {
                    Debug.LogError("Unity IAP unable to strip undesired Android stores from build, use menu (e.g. "+GooglePlayMenuItem+") and check file: " + ModePath);
                    Debug.LogError(e);
                }
            }
        }
    }
}
