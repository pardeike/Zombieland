using UnityEngine;
using UnityEngine.Purchasing;
using System.Collections.Generic;
using static UnityEditor.Purchasing.UnityPurchasingEditor;

namespace UnityEditor.Purchasing
{
    /// <summary>
    /// IAPButtonMenu class creates options in menus to create the IAPButton.
    /// </summary>
	public static class IAPButtonMenu
	{
        /// <summary>
        /// Add option to create a IAPButton from the GameObject menu.
        /// </summary>
        [MenuItem("GameObject/" + PurchasingDisplayName + "/IAP Button", false, 10)]
        public static void GameObjectCreateUnityIAPButton()
        {
            CreateUnityIAPButton();
        }

        /// <summary>
        /// Add option to create a IAPButton from the Window/UnityIAP menu.
        /// </summary>
		[MenuItem (MenuItemRoot + "/Create IAP Button", false, 100)]
		public static void CreateUnityIAPButton()
		{
			// Create Button
			EditorApplication.ExecuteMenuItem("GameObject/UI/Button");

			// Get GameObject of Button
			GameObject gO = Selection.activeGameObject;

			// Add IAP Button component to GameObject
			IAPButton iapButton = null;
			if (gO) {
				iapButton = gO.AddComponent<IAPButton>();
			}

			if (iapButton != null) {
				UnityEditorInternal.ComponentUtility.MoveComponentUp(iapButton);
				UnityEditorInternal.ComponentUtility.MoveComponentUp(iapButton);
				UnityEditorInternal.ComponentUtility.MoveComponentUp(iapButton);
			}
		}
	}

    /// <summary>
    /// IAPListenerMenu class creates options in menus to create the IAPListener.
    /// </summary>
    public static class IAPListenerMenu
    {
        /// <summary>
        /// Add option to create a IAPListener from the GameObject menu.
        /// </summary>
        [MenuItem("GameObject/" + PurchasingDisplayName + "/IAP Listener", false, 10)]
        public static void GameObjectCreateUnityIAPListener()
        {
            CreateUnityIAPListener();
        }

        /// <summary>
        /// Add option to create a IAPListener from the Window/UnityIAP menu.
        /// </summary>
        [MenuItem (MenuItemRoot + "/Create IAP Listener", false, 100)]
        public static void CreateUnityIAPListener()
        {
            // Create empty GameObject
            EditorApplication.ExecuteMenuItem("GameObject/Create Empty");

            // Get GameObject
            GameObject gO = Selection.activeGameObject;

            // Add IAP Listener component to GameObject
            if (gO) {
                gO.AddComponent<IAPListener>();
                gO.name = "IAP Listener";
            }
        }
    }


    /// <summary>
    /// Customer Editor class for the IAPButton. This class handle how the IAPButton should represent itself in the UnityEditor.
    /// </summary>
	[CustomEditor(typeof(IAPButton))]
	[CanEditMultipleObjects]
	public class IAPButtonEditor : Editor
	{
		private static readonly string[] excludedFields = new string[] { "m_Script" };
		private static readonly string[] restoreButtonExcludedFields = new string[] { "m_Script", "consumePurchase", "onPurchaseComplete", "onPurchaseFailed", "titleText", "descriptionText", "priceText" };
		private const string kNoProduct = "<None>";

		private List<string> m_ValidIDs = new List<string>();
		private SerializedProperty m_ProductIDProperty;

		/// <summary>
		/// Event trigger when IAPButton is enabled in the scene.
		/// </summary>
        public void OnEnable()
		{
			m_ProductIDProperty = serializedObject.FindProperty("productId");
		}

		/// <summary>
		/// Event trigger when trying to draw the IAPButton in the inspector.
		/// </summary>
        public override void OnInspectorGUI()
		{
			IAPButton button = (IAPButton)target;

			serializedObject.Update();

			if (button.buttonType == IAPButton.ButtonType.Purchase) {
				EditorGUILayout.LabelField(new GUIContent("Product ID:", "Select a product from the IAP catalog."));

				var catalog = ProductCatalog.LoadDefaultCatalog();

				m_ValidIDs.Clear();
				m_ValidIDs.Add(kNoProduct);
				foreach (var product in catalog.allProducts) {
					m_ValidIDs.Add(product.id);
				}

				int currentIndex = string.IsNullOrEmpty(button.productId) ? 0 : m_ValidIDs.IndexOf(button.productId);
				int newIndex = EditorGUILayout.Popup(currentIndex, m_ValidIDs.ToArray());
				if (newIndex > 0 && newIndex < m_ValidIDs.Count) {
					m_ProductIDProperty.stringValue = m_ValidIDs[newIndex];
				} else {
					m_ProductIDProperty.stringValue = string.Empty;
				}

				if (GUILayout.Button("IAP Catalog...")) {
					ProductCatalogEditor.ShowWindow();
				}
			}

			DrawPropertiesExcluding(serializedObject, button.buttonType == IAPButton.ButtonType.Restore ? restoreButtonExcludedFields : excludedFields);

			serializedObject.ApplyModifiedProperties();
		}
	}
}
