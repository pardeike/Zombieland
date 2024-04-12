using UnityEngine;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

namespace UnityEditor.Tilemaps
{
    /// <summary>
    /// Utility Class for creating Palettes
    /// </summary>
    public static class GridPaletteUtility
    {
        internal static RectInt GetBounds(GameObject palette)
        {
            if (palette == null)
                return new RectInt();

            Vector2Int min = new Vector2Int(int.MaxValue, int.MaxValue);
            Vector2Int max = new Vector2Int(int.MinValue, int.MinValue);

            foreach (var tilemap in palette.GetComponentsInChildren<Tilemap>())
            {
                Vector3Int p1 = tilemap.editorPreviewOrigin;
                Vector3Int p2 = p1 + tilemap.editorPreviewSize;
                Vector2Int tilemapMin = new Vector2Int(Mathf.Min(p1.x, p2.x), Mathf.Min(p1.y, p2.y));
                Vector2Int tilemapMax = new Vector2Int(Mathf.Max(p1.x, p2.x), Mathf.Max(p1.y, p2.y));
                min = new Vector2Int(Mathf.Min(min.x, tilemapMin.x), Mathf.Min(min.y, tilemapMin.y));
                max = new Vector2Int(Mathf.Max(max.x, tilemapMax.x), Mathf.Max(max.y, tilemapMax.y));
            }

            return GridEditorUtility.GetMarqueeRect(min, max);
        }

        /// <summary>
        /// Creates a Palette Asset at the current selected folder path. This will show a popup allowing you to choose
        /// a different folder path for saving the Palette Asset if required.
        /// </summary>
        /// <param name="name">Name of the Palette Asset.</param>
        /// <param name="layout">Grid Layout of the Palette Asset.</param>
        /// <param name="cellSizing">Cell Sizing of the Palette Asset.</param>
        /// <param name="cellSize">Cell Size of the Palette Asset.</param>
        /// <param name="swizzle">Cell Swizzle of the Palette.</param>
        /// <returns>The created Palette Asset if successful.</returns>
        public static GameObject CreateNewPaletteAtCurrentFolder(string name, GridLayout.CellLayout layout, GridPalette.CellSizing cellSizing, Vector3 cellSize, GridLayout.CellSwizzle swizzle)
        {
            string defaultPath = ProjectBrowser.s_LastInteractedProjectBrowser ? ProjectBrowser.s_LastInteractedProjectBrowser.GetActiveFolderPath() : "Assets";
            string folderPath = EditorUtility.SaveFolderPanel("Create palette into folder ", defaultPath, "");
            folderPath = FileUtil.GetProjectRelativePath(folderPath);

            if (string.IsNullOrEmpty(folderPath))
                return null;

            return CreateNewPalette(folderPath, name, layout, cellSizing, cellSize, swizzle);
        }

        /// <summary>
        /// Creates a Palette Asset at the given folder path.
        /// </summary>
        /// <param name="folderPath">Folder Path of the Palette Asset.</param>
        /// <param name="name">Name of the Palette Asset.</param>
        /// <param name="layout">Grid Layout of the Palette Asset.</param>
        /// <param name="cellSizing">Cell Sizing of the Palette Asset.</param>
        /// <param name="cellSize">Cell Size of the Palette Asset.</param>
        /// <param name="swizzle">Cell Swizzle of the Palette.</param>
        /// <returns>The created Palette Asset if successful.</returns>
        public static GameObject CreateNewPalette(string folderPath, string name, GridLayout.CellLayout layout, GridPalette.CellSizing cellSizing, Vector3 cellSize, GridLayout.CellSwizzle swizzle)
        {
            GameObject temporaryGO = new GameObject(name);
            Grid grid = temporaryGO.AddComponent<Grid>();

            // We set size to kEpsilon to mark this as new uninitialized palette
            // Nice default size can be decided when first asset is dragged in
            grid.cellSize = cellSize;
            grid.cellLayout = layout;
            grid.cellSwizzle = swizzle;
            CreateNewLayer(temporaryGO, "Layer1", layout);

            string path = AssetDatabase.GenerateUniqueAssetPath(folderPath + "/" + name + ".prefab");

            Object prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(temporaryGO, path, InteractionMode.AutomatedAction);
            GridPalette palette = CreateGridPalette(cellSizing);
            AssetDatabase.AddObjectToAsset(palette, prefab);
            PrefabUtility.ApplyPrefabInstance(temporaryGO, InteractionMode.AutomatedAction);
            AssetDatabase.Refresh();

            GameObject.DestroyImmediate(temporaryGO);
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        private static GameObject CreateNewLayer(GameObject paletteGO, string name, GridLayout.CellLayout layout)
        {
            GameObject newLayerGO = new GameObject(name);
            var tilemap = newLayerGO.AddComponent<Tilemap>();
            var renderer = newLayerGO.AddComponent<TilemapRenderer>();
            newLayerGO.transform.parent = paletteGO.transform;
            newLayerGO.layer = paletteGO.layer;

            // Set defaults for certain layouts
            switch (layout)
            {
                case GridLayout.CellLayout.Hexagon:
                {
                    tilemap.tileAnchor = Vector3.zero;
                    break;
                }
                case GridLayout.CellLayout.Isometric:
                case GridLayout.CellLayout.IsometricZAsY:
                {
                    renderer.sortOrder = TilemapRenderer.SortOrder.TopRight;
                    break;
                }
            }

            return newLayerGO;
        }

        internal static GridPalette CreateGridPalette(GridPalette.CellSizing cellSizing)
        {
            var palette = GridPalette.CreateInstance<GridPalette>();
            palette.name = "Palette Settings";
            palette.cellSizing = cellSizing;
            return palette;
        }

        internal static Vector3 CalculateAutoCellSize(Grid grid, Vector3 defaultValue)
        {
            Tilemap[] tilemaps = grid.GetComponentsInChildren<Tilemap>();
            foreach (var tilemap in tilemaps)
            {
                foreach (var position in tilemap.cellBounds.allPositionsWithin)
                {
                    Sprite sprite = tilemap.GetSprite(position);
                    if (sprite != null)
                    {
                        return new Vector3(sprite.rect.width, sprite.rect.height, 0f) / sprite.pixelsPerUnit;
                    }
                }
            }
            return defaultValue;
        }
    }
}
