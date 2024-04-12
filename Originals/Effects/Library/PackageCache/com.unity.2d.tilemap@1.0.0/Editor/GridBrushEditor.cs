using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEditor.SceneManagement;
using UnityEngine.Scripting.APIUpdating;

namespace UnityEditor.Tilemaps
{
    /// <summary>Editor for GridBrush.</summary>
    [MovedFrom(true, "UnityEditor", "UnityEditor")]
    [CustomEditor(typeof(GridBrush))]
    public class GridBrushEditor : GridBrushEditorBase
    {
        private static class Styles
        {
            public static readonly GUIContent tileLabel = EditorGUIUtility.TrTextContent("Tile", "Tile set in tilemap");
            public static readonly GUIContent spriteLabel = EditorGUIUtility.TrTextContent("Sprite", "Sprite set when tile is set in tilemap");
            public static readonly GUIContent colorLabel = EditorGUIUtility.TrTextContent("Color", "Color set when tile is set in tilemap");
            public static readonly GUIContent colliderTypeLabel = EditorGUIUtility.TrTextContent("Collider Type", "Collider shape used for tile");
            public static readonly GUIContent lockColorLabel = EditorGUIUtility.TrTextContent("Lock Color", "Prevents tilemap from changing color of tile");
            public static readonly GUIContent lockTransformLabel = EditorGUIUtility.TrTextContent("Lock Transform", "Prevents tilemap from changing transform of tile");
        }

        private class GridBrushProperties
        {
            public static readonly GUIContent floodFillPreviewLabel = EditorGUIUtility.TrTextContent("Show Flood Fill Preview", "Whether a preview is shown while painting a Tilemap when Flood Fill mode is enabled");
            public static readonly string floodFillPreviewEditorPref = "GridBrush.EnableFloodFillPreview";
        }

        /// <summary>The GridBrush that is the target for this editor.</summary>
        public GridBrush brush { get { return target as GridBrush; } }
        private int m_LastPreviewRefreshHash;

        // These are used to clean out previews that happened on previous update
        private GridLayout m_LastGrid;
        private GameObject m_LastBrushTarget;
        private BoundsInt? m_LastBounds;
        private GridBrushBase.Tool? m_LastTool;

        // These are used to handle selection in Selection Inspector
        private TileBase[] m_SelectionTiles;
        private Color[] m_SelectionColors;
        private Matrix4x4[] m_SelectionMatrices;
        private TileFlags[] m_SelectionFlagsArray;
        private Sprite[] m_SelectionSprites;
        private Tile.ColliderType[] m_SelectionColliderTypes;

        protected virtual void OnEnable()
        {
            Undo.undoRedoPerformed += ClearLastPreview;
        }

        protected virtual void OnDisable()
        {
            Undo.undoRedoPerformed -= ClearLastPreview;
            ClearLastPreview();
        }

        private void ClearLastPreview()
        {
            ClearPreview();
            m_LastPreviewRefreshHash = 0;
        }

        /// <summary>Callback for painting the GUI for the GridBrush in the Scene View.</summary>
        /// <param name="gridLayout">Grid that the brush is being used on.</param>
        /// <param name="brushTarget">Target of the GridBrushBase::ref::Tool operation. By default the currently selected GameObject.</param>
        /// <param name="position">Current selected location of the brush.</param>
        /// <param name="tool">Current GridBrushBase::ref::Tool selected.</param>
        /// <param name="executing">Whether brush is being used.</param>
        public override void OnPaintSceneGUI(GridLayout gridLayout, GameObject brushTarget, BoundsInt position, GridBrushBase.Tool tool, bool executing)
        {
            BoundsInt gizmoRect = position;
            bool refreshPreviews = false;
            if (Event.current.type == EventType.Layout)
            {
                int newPreviewRefreshHash = GetHash(gridLayout, brushTarget, position, tool, brush);
                refreshPreviews = newPreviewRefreshHash != m_LastPreviewRefreshHash;
                if (refreshPreviews)
                    m_LastPreviewRefreshHash = newPreviewRefreshHash;
            }
            if (tool == GridBrushBase.Tool.Move)
            {
                if (refreshPreviews && executing)
                {
                    ClearPreview();
                    PaintPreview(gridLayout, brushTarget, position.min);
                }
            }
            else if (tool == GridBrushBase.Tool.Paint || tool == GridBrushBase.Tool.Erase)
            {
                if (refreshPreviews)
                {
                    ClearPreview();
                    if (tool != GridBrushBase.Tool.Erase)
                    {
                        PaintPreview(gridLayout, brushTarget, position.min);
                    }
                }
                gizmoRect = new BoundsInt(position.min - brush.pivot, brush.size);
            }
            else if (tool == GridBrushBase.Tool.Box)
            {
                if (refreshPreviews)
                {
                    ClearPreview();
                    BoxFillPreview(gridLayout, brushTarget, position);
                }
            }
            else if (tool == GridBrushBase.Tool.FloodFill)
            {
                if (refreshPreviews)
                {
                    ClearPreview();
                    FloodFillPreview(gridLayout, brushTarget, position.min);
                }
            }

            base.OnPaintSceneGUI(gridLayout, brushTarget, gizmoRect, tool, executing);
        }

        /// <summary>Callback for drawing the Inspector GUI when there is an active GridSelection made in a Tilemap.</summary>
        public override void OnSelectionInspectorGUI()
        {
            BoundsInt selection = GridSelection.position;
            Tilemap tilemap = GridSelection.target.GetComponent<Tilemap>();

            int cellCount = selection.size.x * selection.size.y * selection.size.z;
            if (tilemap != null && cellCount > 0)
            {
                base.OnSelectionInspectorGUI();
                GUILayout.Space(10f);

                if (m_SelectionTiles == null || m_SelectionTiles.Length != cellCount)
                {
                    m_SelectionTiles = new TileBase[cellCount];
                    m_SelectionColors = new Color[cellCount];
                    m_SelectionMatrices = new Matrix4x4[cellCount];
                    m_SelectionFlagsArray = new TileFlags[cellCount];
                    m_SelectionSprites = new Sprite[cellCount];
                    m_SelectionColliderTypes = new Tile.ColliderType[cellCount];
                }

                int index = 0;
                foreach (var p in selection.allPositionsWithin)
                {
                    m_SelectionTiles[index] = tilemap.GetTile(p);
                    m_SelectionColors[index] = tilemap.GetColor(p);
                    m_SelectionMatrices[index] = tilemap.GetTransformMatrix(p);
                    m_SelectionFlagsArray[index] = tilemap.GetTileFlags(p);
                    m_SelectionSprites[index] = tilemap.GetSprite(p);
                    m_SelectionColliderTypes[index] = tilemap.GetColliderType(p);
                    index++;
                }

                EditorGUI.BeginChangeCheck();
                EditorGUI.showMixedValue = m_SelectionTiles.Any(tile => tile != m_SelectionTiles.First());
                var position = new Vector3Int(selection.xMin, selection.yMin, selection.zMin);
                TileBase newTile = EditorGUILayout.ObjectField(Styles.tileLabel, tilemap.GetTile(position), typeof(TileBase), false) as TileBase;
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(tilemap, "Edit Tilemap");
                    foreach (var p in selection.allPositionsWithin)
                        tilemap.SetTile(p, newTile);
                }

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUI.showMixedValue = m_SelectionSprites.Any(sprite => sprite != m_SelectionSprites.First());
                    EditorGUILayout.ObjectField(Styles.spriteLabel, m_SelectionSprites[0], typeof(Sprite), false, GUILayout.Height(EditorGUI.kSingleLineHeight));
                }

                bool colorFlagsAllEqual = m_SelectionFlagsArray.All(flags => (flags & TileFlags.LockColor) == (m_SelectionFlagsArray.First() & TileFlags.LockColor));
                using (new EditorGUI.DisabledScope(!colorFlagsAllEqual || (m_SelectionFlagsArray[0] & TileFlags.LockColor) != 0))
                {
                    EditorGUI.showMixedValue = m_SelectionColors.Any(color => color != m_SelectionColors.First());
                    EditorGUI.BeginChangeCheck();
                    Color newColor = EditorGUILayout.ColorField(Styles.colorLabel, m_SelectionColors[0]);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(tilemap, "Edit Tilemap");
                        foreach (var p in selection.allPositionsWithin)
                            tilemap.SetColor(p, newColor);
                    }
                }

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUI.showMixedValue = m_SelectionColliderTypes.Any(colliderType => colliderType != m_SelectionColliderTypes.First());
                    EditorGUILayout.EnumPopup(Styles.colliderTypeLabel, m_SelectionColliderTypes[0]);
                }

                bool transformFlagsAllEqual = m_SelectionFlagsArray.All(flags => (flags & TileFlags.LockTransform) == (m_SelectionFlagsArray.First() & TileFlags.LockTransform));
                using (new EditorGUI.DisabledScope(!transformFlagsAllEqual || (m_SelectionFlagsArray[0] & TileFlags.LockTransform) != 0))
                {
                    EditorGUI.showMixedValue = m_SelectionMatrices.Any(matrix => matrix != m_SelectionMatrices.First());
                    EditorGUI.BeginChangeCheck();
                    Matrix4x4 newTransformMatrix = TileEditor.TransformMatrixOnGUI(m_SelectionMatrices[0]);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(tilemap, "Edit Tilemap");
                        foreach (var p in selection.allPositionsWithin)
                            tilemap.SetTransformMatrix(p, newTransformMatrix);
                    }
                }

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUI.showMixedValue = !colorFlagsAllEqual;
                    EditorGUILayout.Toggle(Styles.lockColorLabel, (m_SelectionFlagsArray[0] & TileFlags.LockColor) != 0);
                    EditorGUI.showMixedValue = !transformFlagsAllEqual;
                    EditorGUILayout.Toggle(Styles.lockTransformLabel, (m_SelectionFlagsArray[0] & TileFlags.LockTransform) != 0);
                }

                EditorGUI.showMixedValue = false;
            }
        }

        /// <summary> Callback when the mouse cursor leaves and editing area. </summary>
        /// <remarks> Cleans up brush previews. </remarks>
        public override void OnMouseLeave()
        {
            ClearPreview();
        }

        /// <summary> Callback when the GridBrush Tool is deactivated. </summary>
        /// <param name="tool">GridBrush Tool that is deactivated.</param>
        /// <remarks> Cleans up brush previews. </remarks>
        public override void OnToolDeactivated(GridBrushBase.Tool tool)
        {
            ClearPreview();
        }

        /// <summary> Whether the GridBrush can change Z Position. </summary>
        public override bool canChangeZPosition
        {
            get { return brush.canChangeZPosition; }
            set { brush.canChangeZPosition = value; }
        }

        /// <summary>Callback for registering an Undo action before the GridBrushBase does the current GridBrushBase::ref::Tool action.</summary>
        /// <param name="brushTarget">Target of the GridBrushBase::ref::Tool operation. By default the currently selected GameObject.</param>
        /// <param name="tool">Current GridBrushBase::ref::Tool selected.</param>
        /// <remarks>Implement this for any special Undo behaviours when a brush is used.</remarks>
        public override void RegisterUndo(GameObject brushTarget, GridBrushBase.Tool tool)
        {
            if (brushTarget != null)
            {
                Undo.RegisterCompleteObjectUndo(new Object[] { brushTarget, brushTarget.GetComponent<Tilemap>() }, tool.ToString());
            }
        }

        /// <summary>Returns all valid targets that the brush can edit.</summary>
        /// <remarks>Valid targets for the GridBrush are any GameObjects with a Tilemap component.</remarks>
        public override GameObject[] validTargets
        {
            get
            {
                StageHandle currentStageHandle = StageUtility.GetCurrentStageHandle();
                return currentStageHandle.FindComponentsOfType<Tilemap>().Where(x => x.gameObject.scene.isLoaded
                    && x.gameObject.activeInHierarchy).Select(x => x.gameObject).ToArray();
            }
        }

        /// <summary>Paints preview data into a cell of a grid given the coordinates of the cell.</summary>
        /// <param name="gridLayout">Grid to paint data to.</param>
        /// <param name="brushTarget">Target of the paint operation. By default the currently selected GameObject.</param>
        /// <param name="position">The coordinates of the cell to paint data to.</param>
        /// <remarks>The grid brush will paint preview sprites in its brush cells onto an associated Tilemap. This will not instantiate objects associated with the painted tiles.</remarks>
        public virtual void PaintPreview(GridLayout gridLayout, GameObject brushTarget, Vector3Int position)
        {
            Vector3Int min = position - brush.pivot;
            Vector3Int max = min + brush.size;
            BoundsInt bounds = new BoundsInt(min, max - min);

            if (brushTarget != null)
            {
                Tilemap map = brushTarget.GetComponent<Tilemap>();
                foreach (Vector3Int location in bounds.allPositionsWithin)
                {
                    Vector3Int brushPosition = location - min;
                    GridBrush.BrushCell cell = brush.cells[brush.GetCellIndex(brushPosition)];
                    if (cell.tile != null && map != null)
                    {
                        SetTilemapPreviewCell(map, location, cell.tile, cell.matrix, cell.color);
                    }
                }
            }

            m_LastGrid = gridLayout;
            m_LastBounds = bounds;
            m_LastBrushTarget = brushTarget;
            m_LastTool = GridBrushBase.Tool.Paint;
        }

        /// <summary>Does a preview of what happens when a GridBrush.BoxFill is done with the same parameters.</summary>
        /// <param name="gridLayout">Grid to box fill data to.</param>
        /// <param name="brushTarget">Target of box fill operation. By default the currently selected GameObject.</param>
        /// <param name="position">The bounds to box fill data to.</param>
        public virtual void BoxFillPreview(GridLayout gridLayout, GameObject brushTarget, BoundsInt position)
        {
            if (brushTarget != null)
            {
                Tilemap map = brushTarget.GetComponent<Tilemap>();
                if (map != null)
                {
                    foreach (Vector3Int location in position.allPositionsWithin)
                    {
                        Vector3Int local = location - position.min;
                        GridBrush.BrushCell cell = brush.cells[brush.GetCellIndexWrapAround(local.x, local.y, local.z)];
                        if (cell.tile != null)
                        {
                            SetTilemapPreviewCell(map, location, cell.tile, cell.matrix, cell.color);
                        }
                    }
                }
            }

            m_LastGrid = gridLayout;
            m_LastBounds = position;
            m_LastBrushTarget = brushTarget;
            m_LastTool = GridBrushBase.Tool.Box;
        }

        /// <summary>Does a preview of what happens when a GridBrush.FloodFill is done with the same parameters.</summary>
        /// <param name="gridLayout">Grid to paint data to.</param>
        /// <param name="brushTarget">Target of the flood fill operation. By default the currently selected GameObject.</param>
        /// <param name="position">The coordinates of the cell to flood fill data to.</param>
        public virtual void FloodFillPreview(GridLayout gridLayout, GameObject brushTarget, Vector3Int position)
        {
            // This can be quite taxing on a large Tilemap, so users can choose whether to do this or not
            if (!EditorPrefs.GetBool(GridBrushProperties.floodFillPreviewEditorPref, true))
                return;

            var bounds = new BoundsInt(position, Vector3Int.one);
            if (brushTarget != null && brush.cellCount > 0)
            {
                Tilemap map = brushTarget.GetComponent<Tilemap>();
                if (map != null)
                {
                    GridBrush.BrushCell cell = brush.cells[0];
                    map.EditorPreviewFloodFill(position, cell.tile);
                    // Set floodfill bounds as tilemap bounds
                    bounds.min = map.origin;
                    bounds.max = map.origin + map.size;
                }
            }

            m_LastGrid = gridLayout;
            m_LastBounds = bounds;
            m_LastBrushTarget = brushTarget;
            m_LastTool = GridBrushBase.Tool.FloodFill;
        }

        [SettingsProvider]
        internal static SettingsProvider CreateSettingsProvider()
        {
            var settingsProvider = new SettingsProvider("Preferences/2D/Grid Brush", SettingsScope.User, SettingsProvider.GetSearchKeywordsFromGUIContentProperties<GridBrushProperties>()) {
                guiHandler = searchContext =>
                {
                    PreferencesGUI();
                }
            };
            return settingsProvider;
        }

        private static void PreferencesGUI()
        {
            using (new SettingsWindow.GUIScope())
            {
                EditorGUI.BeginChangeCheck();
                var val = EditorGUILayout.Toggle(GridBrushProperties.floodFillPreviewLabel, EditorPrefs.GetBool(GridBrushProperties.floodFillPreviewEditorPref, true));
                if (EditorGUI.EndChangeCheck())
                {
                    EditorPrefs.SetBool(GridBrushProperties.floodFillPreviewEditorPref, val);
                }
            }
        }

        /// <summary>Clears any preview drawn previously by the GridBrushEditor.</summary>
        public virtual void ClearPreview()
        {
            if (m_LastGrid == null || m_LastBounds == null || m_LastBrushTarget == null || m_LastTool == null)
                return;

            Tilemap map = m_LastBrushTarget.GetComponent<Tilemap>();
            if (map != null)
            {
                switch (m_LastTool)
                {
                    case GridBrushBase.Tool.FloodFill:
                    {
                        map.ClearAllEditorPreviewTiles();
                        break;
                    }
                    case GridBrushBase.Tool.Box:
                    {
                        Vector3Int min = m_LastBounds.Value.position;
                        Vector3Int max = min + m_LastBounds.Value.size;
                        BoundsInt bounds = new BoundsInt(min, max - min);
                        foreach (Vector3Int location in bounds.allPositionsWithin)
                        {
                            ClearTilemapPreview(map, location);
                        }
                        break;
                    }
                    case GridBrushBase.Tool.Paint:
                    {
                        BoundsInt bounds = m_LastBounds.Value;
                        foreach (Vector3Int location in bounds.allPositionsWithin)
                        {
                            ClearTilemapPreview(map, location);
                        }
                        break;
                    }
                }
            }

            m_LastBrushTarget = null;
            m_LastGrid = null;
            m_LastBounds = null;
            m_LastTool = null;
        }

        private static void SetTilemapPreviewCell(Tilemap map, Vector3Int location, TileBase tile, Matrix4x4 transformMatrix, Color color)
        {
            if (map == null)
                return;
            map.SetEditorPreviewTile(location, tile);
            map.SetEditorPreviewTransformMatrix(location, transformMatrix);
            map.SetEditorPreviewColor(location, color);
        }

        private static void ClearTilemapPreview(Tilemap map, Vector3Int location)
        {
            if (map == null)
                return;
            map.SetEditorPreviewTile(location, null);
            map.SetEditorPreviewTransformMatrix(location, Matrix4x4.identity);
            map.SetEditorPreviewColor(location, Color.white);
        }

        private static int GetHash(GridLayout gridLayout, GameObject brushTarget, BoundsInt position, GridBrushBase.Tool tool, GridBrush brush)
        {
            int hash = 0;
            unchecked
            {
                hash = hash * 33 + (gridLayout != null ? gridLayout.GetHashCode() : 0);
                hash = hash * 33 + (brushTarget != null ? brushTarget.GetHashCode() : 0);
                hash = hash * 33 + position.GetHashCode();
                hash = hash * 33 + tool.GetHashCode();
                hash = hash * 33 + (brush != null ? brush.GetHashCode() : 0);
            }

            return hash;
        }
    }
}
