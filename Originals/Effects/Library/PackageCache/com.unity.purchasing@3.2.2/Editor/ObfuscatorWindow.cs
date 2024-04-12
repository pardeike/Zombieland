using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using static UnityEditor.Purchasing.UnityPurchasingEditor;

namespace UnityEditor.Purchasing
{
    /// <summary>
    /// Unity IAP Client-Side Receipt Validation obfuscation window.
    /// </summary>
    /// <remarks>
    /// Collects certificate details for supported stores.
    /// Generates .cs file in Assets/, used by Unity IAP Receipt Validation.
    /// </remarks>
    internal class ObfuscatorWindow : RichEditorWindow
    {
        // Localize me
        private const string kLabelTitle = "Receipt Validation Obfuscator";

        private const string kLabelGenerateGoogle = "Obfuscate Google Play License Key";

        private const string kLabelGoogleKey = "Google Play Public License Key";
        private const string kPublicKeyPlaceholder = "--Paste Public Key Here--";

        private const string kLabelGoogleInstructions =
            "Follow these four steps to set up receipt validation for Google Play.";

        private const string kLabelGooglePlayDeveloperConsoleInstructions =
            "1. Get your license key from the Google Play Developer Console:";

        private const string kLabelGooglePlayDeveloperConsoleLink = "\tOpen Google Play Developer Console";
        private const string kGooglePlayDevConsoleURL = "https://play.google.com/apps/publish/";

        private const string kLabelGooglePlayDeveloperConsoleSteps =
            "\ta. Select your app from the list\n" +
            "\tb. Go to \"Monetization setup\" under \"Monetize\"\n" +
            "\tc. Copy the key from the \"Licensing\" section";

        private const string kLabelGooglePasteKeyInstructions = "2. Paste the key here:";

        private const string kObfuscateKeyInstructions =
            "3. Obfuscate the key. (Creates Tangle classes in your project.)";

        private const string kDashboardInstructions =
            "4. To ensure correct revenue data, enter your key in the Analytics dashboard.";

        private const string kLabelDashboardLink = "\tOpen Analytics Dashboard";
        private const string kDashboardURL = "https://analytics.cloud.unity3d.com/projects/<cloud_id>/edit/";

        private GUIStyle m_ErrorStyle;
        private string m_GoogleError;
        private string m_AppleError;

        /// <summary>
        /// The current Google Play Public Key, in string
        /// </summary>
        string m_GooglePlayPublicKey = kPublicKeyPlaceholder;

        [MenuItem(MenuItemRoot + "/Receipt Validation Obfuscator", false, 200)]
        static void Init()
        {
            // Get existing open window or if none, make a new one:
            ObfuscatorWindow window = (ObfuscatorWindow) EditorWindow.GetWindow(typeof(ObfuscatorWindow));
            window.titleContent.text = kLabelTitle;
            window.minSize = new Vector2(340, 180);
            window.Show();
        }

        private ObfuscatorWindow()
        {
        }

        void OnGUI()
        {
            if (m_ErrorStyle == null)
            {
                m_ErrorStyle = new GUIStyle();
                m_ErrorStyle.normal.textColor = Color.red;
            }

            // Apple error message, if any
            if (!string.IsNullOrEmpty(m_AppleError))
                GUILayout.Label(m_AppleError, m_ErrorStyle);

            // Google Play
            GUILayout.Label(kLabelGoogleKey, EditorStyles.boldLabel);
            GUILayout.Label(kLabelGoogleInstructions);
            GUILayout.Space(5);

            GUILayout.Label(kLabelGooglePlayDeveloperConsoleInstructions);
            GUILink(kLabelGooglePlayDeveloperConsoleLink, kGooglePlayDevConsoleURL);

            GUILayout.Label(kLabelGooglePlayDeveloperConsoleSteps);
            GUILayout.Label(kLabelGooglePasteKeyInstructions);
            m_GooglePlayPublicKey = EditorGUILayout.TextArea(
                m_GooglePlayPublicKey,
                GUILayout.MinHeight(20),
                GUILayout.MaxHeight(50));

            GUILayout.Label(kObfuscateKeyInstructions);
            if (!string.IsNullOrEmpty(m_GoogleError))
                GUILayout.Label(m_GoogleError, m_ErrorStyle);
            if (GUILayout.Button(kLabelGenerateGoogle))
                ObfuscateSecrets(includeGoogle: true);

            GUILayout.Label(kDashboardInstructions);

#if UNITY_2018_1_OR_NEWER
            GUILink(kLabelDashboardLink, kDashboardURL.Replace("<cloud_id>", CloudProjectSettings.projectId));
#else
            GUILink(kLabelDashboardLink, kDashboardURL.Replace("<cloud_id>", PlayerSettings.cloudProjectId));
#endif
        }

        void ObfuscateSecrets(bool includeGoogle)
        {
            ObfuscationGenerator.ObfuscateSecrets(includeGoogle: includeGoogle,
                appleError: ref m_AppleError, googleError: ref m_GoogleError,
                googlePlayPublicKey: m_GooglePlayPublicKey);
        }
    }

    /// <summary>
    /// Writes obfuscated secrets.
    /// </summary>
    internal class ObfuscationGenerator
    {
        internal const string kPrevOutputPath = "Assets/Plugins/UnityPurchasing/generated";
        internal const string kBadOutputPath = "Assets/Resources/UnityPurchasing/generated";
        internal const string kOutputPath = "Assets/Scripts/UnityPurchasing/generated";

        private const string kObfuscationClassSuffix = "Tangle.cs";

        private const string m_GeneratedCredentialsTemplateFilename = "IAPGeneratedCredentials.cs.template";
        private const string m_GeneratedCredentialsTemplateFilenameNoExtension = "IAPGeneratedCredentials.cs";

        private const string kAppleCertPath = "Packages/com.unity.purchasing/Editor/AppleIncRootCertificate.cer";

        /// <summary>
        /// Since we are changing the obfuscation files' location, it may be necessary to migrate existing tangle files to the new location.
        /// Also in 2.0.0, a poor choice of new location was used and has been corrected. If that path exists, its contents are to be moved as well.
        /// </summary>
        [InitializeOnLoadMethod]
        internal static void MigrateObfuscations()
        {
            try
            {
                // Check if the old directory exists before moving its contents
                if (DoPrevObfuscationFilesExist())
                {
                    MoveObfuscatorFiles(kPrevOutputPath);
                }
                else if (DoBadObfuscationFilesExist())
                {
                    MoveObfuscatorFiles(kBadOutputPath);
                }
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private static void MoveObfuscatorFiles(string oldPath)
        {
            // This will create the new obfuscation class location, if it already exists, this will not do anything.
            Directory.CreateDirectory (kOutputPath);

            foreach (var prevFile in Directory.GetFiles(oldPath))
            {
                var fileName = Path.GetFileName(prevFile);
                if (fileName.EndsWith(kObfuscationClassSuffix))
                {
                    var newFile = $"{kOutputPath}/{fileName}";

                    // See if the file already exists in the new location.
                    if (File.Exists(newFile))
                    {
                        break;
                    }

                    AssetDatabase.MoveAsset(prevFile, newFile);
                }
            }
        }

        internal static bool DoPrevObfuscationFilesExist()
        {
            return (Directory.Exists(kPrevOutputPath) && (Directory.GetFiles(kPrevOutputPath).Length > 0));
        }

        internal static bool DoBadObfuscationFilesExist()
        {
            return (Directory.Exists(kBadOutputPath) && (Directory.GetFiles(kBadOutputPath).Length > 0));
        }

        /// <summary>
        /// Generates specified obfuscated class files.
        /// </summary>
        public static void ObfuscateSecrets(bool includeGoogle, ref string appleError,
            ref string googleError, string googlePlayPublicKey)
        {
            try
            {
                // First things first! Obfuscate! XHTLOA!
                {
                    appleError = null;
                    int key = 0;
                    int[] order = new int[0];
                    byte[] tangled = new byte[0];
                    try
                    {
                        byte[] bytes = System.IO.File.ReadAllBytes(kAppleCertPath);
                        order = new int[bytes.Length / 20 + 1];

                        // TODO: Integrate with upgraded Tangle!

                        tangled = TangleObfuscator.Obfuscate(bytes, order, out key);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning("Invalid Apple Root Certificate. Generating incomplete credentials file. " + e);
                        appleError = "  Invalid Apple Root Certificate";
                    }
                    BuildObfuscatedClass("Apple", key, order, tangled, tangled.Length != 0);
                }

                if (includeGoogle)
                {
                    googleError = null;
                    int key = 0;
                    int[] order = new int[0];
                    byte[] tangled = new byte[0];
                    try
                    {
                        var bytes = Convert.FromBase64String(googlePlayPublicKey);
                        order = new int[bytes.Length / 20 + 1];

                        tangled = TangleObfuscator.Obfuscate(bytes, order, out key);
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning("Invalid Google Play Public Key. Generating incomplete credentials file. " + e);
                        googleError =
                            "  The Google Play License Key is invalid. GooglePlayTangle was generated with incomplete credentials.";
                    }
                    BuildObfuscatedClass("GooglePlay", key, order, tangled, tangled.Length != 0);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning(e.StackTrace);
            }

            // Ensure all the Tangle classes exist, even if they were not generated at this time. Apple will always
            // be generated.
            if (!ObfuscatedClassExists("GooglePlay"))
            {
                try
                {
                    BuildObfuscatedClass("GooglePlay", 0, new int[0], new byte[0], false);
                }
                catch (Exception e)
                {
                    Debug.LogWarning(e.StackTrace);
                }
            }

            AssetDatabase.Refresh();
        }

        private static string FullPathForTangleClass(string classnamePrefix)
        {
            return Path.Combine(kOutputPath, string.Format($"{classnamePrefix}{kObfuscationClassSuffix}"));
        }

        private static bool ObfuscatedClassExists(string classnamePrefix)
        {
            return File.Exists(FullPathForTangleClass(classnamePrefix));
        }

        private static void BuildObfuscatedClass(string classnamePrefix, int key, int[] order, byte[] data, bool populated) {
            Dictionary<string, string> substitutionDictionary = new Dictionary<string, string>()
            {
                {"{NAME}", classnamePrefix.ToString()},
                {"{KEY}", key.ToString()},
                {"{ORDER}", String.Format("{0}",String.Join(",", Array.ConvertAll(order, i => i.ToString())))},
                {"{DATA}", Convert.ToBase64String(data)},
                {"{POPULATED}", populated.ToString().ToLowerInvariant()} // Defaults to XML-friendly values
            };

            string templateRelativePath = null;
            string templateText = LoadTemplateText(out templateRelativePath);

            if (templateText != null)
            {
                string outfileText = templateText;

                // Apply the parameters to the template
                foreach (var pair in substitutionDictionary)
                {
                    outfileText = outfileText.Replace(pair.Key, pair.Value);
                }
                Directory.CreateDirectory (kOutputPath);
                File.WriteAllText(FullPathForTangleClass(classnamePrefix), outfileText);
            }
        }

        /// <summary>
        /// Loads the template file.
        /// </summary>
        /// <returns>The template file's text.</returns>
        /// <param name="templateRelativePath">Relative Assets/ path to template file.</param>
        private static string LoadTemplateText(out string templateRelativePath)
        {
            string[] assetGUIDs =
                AssetDatabase.FindAssets(m_GeneratedCredentialsTemplateFilenameNoExtension);
            string templateGUID = null;
            templateRelativePath = null;

            if (assetGUIDs.Length > 0)
            {
                templateGUID = assetGUIDs[0];
            }
            else
            {
                Debug.LogError(String.Format("Could not find template \"{0}\"",
                    m_GeneratedCredentialsTemplateFilename));
            }

            string templateText = null;

            if (templateGUID != null)
            {
                templateRelativePath = AssetDatabase.GUIDToAssetPath(templateGUID);

                string templateAbsolutePath =
                    System.IO.Path.GetDirectoryName(Application.dataPath)
                    + System.IO.Path.DirectorySeparatorChar
                    + templateRelativePath;

                templateText = System.IO.File.ReadAllText(templateAbsolutePath);
            }

            return templateText;
        }
    }
}
