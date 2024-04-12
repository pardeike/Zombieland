using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using NUnit.Framework;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

namespace UnityEditor.Tilemaps.Tests
{
    internal class PaletteTest
    {
        const float paletteHeaderHeight = 24f;
        const string defaultPalettePath = "Packages/com.unity.2d.tilemap/Tests/Palettes/Default.prefab";
        const string defaultTemporaryPalettePath = "Assets/Temp/Default.prefab";
        const string path = "Assets";
        const string path2 = "Assets";
        const string name = "Palette1";
        const string name2 = "Palette2";
        const string temporaryPalettePath = "Assets/Temp/TempPalette.prefab";
        const string temporaryAssetPath = "Assets/Temp/MyBrushGenerated.asset";
        const string temporaryTilePath = "Assets/Temp/temp_tile.asset";
        const string tilePath = "Packages/com.unity.2d.tilemap/Tests/Tiles/blue_spritesheet_0.asset";
        const string bigTilePath = "Packages/com.unity.2d.tilemap/Tests/Tiles/big_blue_spritesheet_0.asset";
        const string emptyColumnRowTexturePath = "Packages/com.unity.2d.tilemap/Tests/Sprites/1103034.png";
        const string square0000TexturePath = "Packages/com.unity.2d.tilemap/Tests/Sprites/Square0000.png";
        const string square1111TexturePath = "Packages/com.unity.2d.tilemap/Tests/Sprites/Square1111.png";
        const string square1222TexturePath = "Packages/com.unity.2d.tilemap/Tests/Sprites/Square1222.png";

        // MyBrush validates game object name with the exact invalid name
        const string validMyBrushTargetName = MyBrushEditor.invalidName + "IsActuallyValidNow";
        const string invalidMyBrushTargetName = MyBrushEditor.invalidName;

        private GridPaintPaletteWindow m_Window;

        [SetUp]
        public void SetUp()
        {
            AssetDatabase.CreateFolder("Assets", "Temp");

            AssetDatabase.CopyAsset(defaultPalettePath, defaultTemporaryPalettePath);
            AssetDatabase.Refresh();

            var gridPalette = AssetDatabase.LoadAssetAtPath(defaultTemporaryPalettePath, typeof(GridPalette));
            if (gridPalette == null)
            {
                GridPalette palette = GridPalette.CreateInstance<GridPalette>();
                palette.name = "Palette Settings";
                palette.cellSizing = GridPalette.CellSizing.Automatic;
                AssetDatabase.AddObjectToAsset(palette, defaultTemporaryPalettePath);
                AssetDatabase.ForceReserializeAssets(new string[] {defaultTemporaryPalettePath});
            }

            AssetDatabase.Refresh();

            SessionState.EraseInt(GridPaletteBrushes.s_SessionStateLastUsedBrush);
            GridPaintingState.gridBrush = GridPaletteBrushes.brushes[0];
        }

        [TearDown]
        public void TearDown()
        {
            SessionState.EraseInt(GridPaletteBrushes.s_SessionStateLastUsedBrush);
            GridPaintingState.gridBrush = GridPaletteBrushes.brushes[0];

            GridPaintActiveTargetsPreferences.restoreEditModeSelection = true;

            if (AssetDatabase.GetAllAssetPaths().Contains(path + "/" + name + ".prefab"))
            {
                AssetDatabase.DeleteAsset(path + "/" + name + ".prefab");
            }

            if (AssetDatabase.GetAllAssetPaths().Contains(path2 + "/" + name2 + ".prefab"))
            {
                AssetDatabase.DeleteAsset(path2 + "/" + name2 + ".prefab");
            }

            if (AssetDatabase.LoadAllAssetsAtPath(defaultTemporaryPalettePath).Length > 0)
            {
                AssetDatabase.DeleteAsset(defaultTemporaryPalettePath);
            }

            if (AssetDatabase.LoadAllAssetsAtPath(temporaryPalettePath).Length > 0)
            {
                AssetDatabase.DeleteAsset(temporaryPalettePath);
            }

            if (AssetDatabase.LoadAllAssetsAtPath(temporaryAssetPath).Length > 0)
            {
                AssetDatabase.DeleteAsset(temporaryAssetPath);
            }

            if (AssetDatabase.LoadAllAssetsAtPath(temporaryTilePath).Length > 0)
            {
                AssetDatabase.DeleteAsset(temporaryTilePath);
            }

            if (AssetDatabase.IsValidFolder("Assets/Temp"))
            {
                FileUtil.DeleteFileOrDirectory("Assets/Temp");
                FileUtil.DeleteFileOrDirectory("Assets/Temp.meta");
                AssetDatabase.Refresh();
            }

            GridPaletteBrushes.FlushCache();
            GridPalettes.CleanCache();

            if (m_Window != null)
            {
                m_Window.Close();
                m_Window = null;
            }
        }

        private GameObject CreateTilemapGameObjectWithName(string name)
        {
            var gameObject = new GameObject();
            gameObject.name = name;
            gameObject.AddComponent<Grid>();
            gameObject.AddComponent<Tilemap>();
            return gameObject;
        }

        private GridPaintPaletteWindow CreatePaletteWindow()
        {
            var w = EditorWindow.GetWindow<GridPaintPaletteWindow>();
            w.position = new Rect(40f, 10f, 400f, 300f);
            m_Window = w;
            return w;
        }

        [Test]
        public void CreatePaletteWindow_ReturnsValidPaletteWindow()
        {
            var w = CreatePaletteWindow();
            Assert.NotNull(w);
        }

        [Test]
        public void CreatePaletteWindow_DefaultActiveBrushIsGridBrush()
        {
            CreatePaletteWindow();
            Assert.NotNull(GridPaintingState.gridBrush);
            Assert.AreEqual(typeof(GridBrush), GridPaintingState.gridBrush.GetType());
        }

        [Test]
        public void CreatePaletteWindow_CanLoadDefaultPalette()
        {
            var w = CreatePaletteWindow();
            var defaultPalette = AssetDatabase.LoadAssetAtPath<GameObject>(defaultTemporaryPalettePath);
            Assert.NotNull(w.palette);
            Assert.AreEqual(defaultPalette, w.palette);
        }

        [Test]
        [TestCase(Grid.CellLayout.Rectangle, GridLayout.CellSwizzle.XYZ)]
        [TestCase(Grid.CellLayout.Hexagon, GridLayout.CellSwizzle.XYZ)]
        [TestCase(Grid.CellLayout.Hexagon, GridLayout.CellSwizzle.YXZ)]
        public void CreatePalette_ReturnsValidPalettesWithGridAndChildTilemap(Grid.CellLayout layout,
            GridLayout.CellSwizzle swizzle)
        {
            int paletteCount = GridPalettes.palettes.Count;

            GameObject palette1 = GridPaletteUtility.CreateNewPalette(path, name, layout,
                GridPalette.CellSizing.Automatic, Vector3.one, swizzle);
            GameObject palette2 = GridPaletteUtility.CreateNewPalette(path2, name2, layout,
                GridPalette.CellSizing.Automatic, Vector3.one, swizzle);

            GridPalettes.CleanCache();
            Assert.AreEqual(paletteCount + 2, GridPalettes.palettes.Count);

            Assert.NotNull(palette1.GetComponent<Grid>());
            Assert.NotNull(palette1.GetComponentInChildren<Tilemap>());
            Assert.NotNull(palette2.GetComponent<Grid>());
            Assert.NotNull(palette2.GetComponentInChildren<Tilemap>());
        }

        [Test]
        public void ChangeActiveGridBrush_DoesNotChangeActivePaintTargetIfStillValid()
        {
            var invalidGO = CreateTilemapGameObjectWithName(invalidMyBrushTargetName);
            var validGO = CreateTilemapGameObjectWithName(validMyBrushTargetName);

            CreatePaletteWindow();
            GridPaintingState.scenePaintTarget = validGO;
            Assert.AreEqual(validGO, GridPaintingState.scenePaintTarget);

            Assert.IsTrue(GridPaletteBrushes.brushes[2] is MyBrush);
            GridPaintingState.gridBrush = GridPaletteBrushes.brushes[2];
            Assert.AreEqual(GridPaletteBrushes.brushes[2], GridPaintingState.gridBrush);
            Assert.AreEqual(validGO, GridPaintingState.scenePaintTarget);

            Object.DestroyImmediate(invalidGO);
            Object.DestroyImmediate(validGO);
        }

        [Test]
        public void ChangeActiveGridBrush_ChangesActivePaintTargetIfNotValid()
        {
            var invalidGO = CreateTilemapGameObjectWithName(invalidMyBrushTargetName);
            var validGO = CreateTilemapGameObjectWithName(validMyBrushTargetName);

            CreatePaletteWindow();
            GridPaintingState.scenePaintTarget = invalidGO;
            Assert.AreEqual(invalidGO, GridPaintingState.scenePaintTarget);

            Assert.IsTrue(GridPaletteBrushes.brushes[2] is MyBrush);
            GridPaintingState.gridBrush = GridPaletteBrushes.brushes[2];
            Assert.AreEqual(GridPaletteBrushes.brushes[2], GridPaintingState.gridBrush);
            Assert.AreEqual(validGO, GridPaintingState.scenePaintTarget);

            Object.DestroyImmediate(invalidGO);
            Object.DestroyImmediate(validGO);
        }

        [Test]
        public void PickFromPalette_DefaultGridBrushSelectsWithCorrectSize()
        {
            var w = CreatePaletteWindow();
            w.palette = AssetDatabase.LoadAssetAtPath<GameObject>(defaultTemporaryPalettePath);
            Event ev = new Event();

            Vector2 startDrag = new Vector2(w.clipboardView.guiRect.width * 0.25f,
                w.clipboardView.guiRect.height * 0.25f);
            Vector2 endDrag = new Vector2(w.clipboardView.guiRect.width * 0.75f,
                w.clipboardView.guiRect.height * 0.75f);

            Grid grid = w.clipboardView.paletteInstance.GetComponent<Grid>();
            Vector3Int startCell = grid.LocalToCell(w.clipboardView.ScreenToLocal(startDrag));
            Vector3Int endCell = grid.LocalToCell(w.clipboardView.ScreenToLocal(endDrag));
            Vector3Int size = new Vector3Int(endCell.x - startCell.x + 1, startCell.y - endCell.y + 1, 1);

            startDrag += new Vector2(0f, paletteHeaderHeight);
            endDrag += new Vector2(0f, paletteHeaderHeight);

            ev.mousePosition = startDrag;
            ev.type = EventType.MouseMove;
            w.SendEvent(ev);
            ev.type = EventType.MouseDown;
            w.SendEvent(ev);
            ev.type = EventType.MouseDrag;
            w.SendEvent(ev);
            ev.type = EventType.MouseDrag;
            ev.mousePosition = endDrag;
            w.SendEvent(ev);
            ev.type = EventType.MouseUp;
            w.SendEvent(ev);

            Assert.NotNull(GridPaintingState.defaultBrush);
            Assert.AreEqual(size, GridPaintingState.defaultBrush.size);
        }

        [Test]
        public void PickAndDragOutsidePalette_CancelsPickAction()
        {
            var w = CreatePaletteWindow();
            w.palette = AssetDatabase.LoadAssetAtPath<GameObject>(defaultTemporaryPalettePath);
            Event ev = new Event();
            Assert.GreaterOrEqual(GridPaletteBrushes.brushes.Count, 1);
            GridPaintingState.defaultBrush.Init(new Vector3Int(1, 1, 1));
            GridPaintingState.defaultBrush.SetTile(Vector3Int.zero, null);

            Vector2 startDrag = new Vector2(w.clipboardView.guiRect.width * 0.25f,
                w.clipboardView.guiRect.height * 0.25f);
            Vector2 endDrag = new Vector2(w.clipboardView.guiRect.width * 0.75f,
                w.clipboardView.guiRect.height * 0.75f);

            Grid grid = w.clipboardView.paletteInstance.GetComponent<Grid>();
            Vector3Int startCell = grid.LocalToCell(w.clipboardView.ScreenToLocal(startDrag));
            Vector3Int endCell = grid.LocalToCell(w.clipboardView.ScreenToLocal(endDrag));
            Vector3Int size = new Vector3Int(endCell.x - startCell.x + 1, startCell.y - endCell.y + 1, 1);

            startDrag += new Vector2(0f, paletteHeaderHeight);
            endDrag += new Vector2(0f, paletteHeaderHeight);

            ev.mousePosition = startDrag;
            ev.type = EventType.MouseMove;
            w.SendEvent(ev);
            ev.type = EventType.MouseDown;
            w.SendEvent(ev);
            ev.type = EventType.MouseDrag;
            w.SendEvent(ev);
            ev.type = EventType.MouseDrag;
            ev.mousePosition =
                new Vector2(-10, -10); // MousePosition is relative to window, negative values are outside of window
            w.SendEvent(ev);
            ev.type = EventType.MouseUp;
            w.SendEvent(ev);

            Assert.AreNotEqual(size, GridPaintingState.defaultBrush.size);
            Assert.IsNull(GridPaintingState.defaultBrush.cells[0].tile);
        }

        [Test]
        public void PickAndDragOutsideAndBackInsideOfPalette_DoesPickAction()
        {
            var w = CreatePaletteWindow();
            w.palette = AssetDatabase.LoadAssetAtPath<GameObject>(defaultTemporaryPalettePath);
            Event ev = new Event();
            Assert.GreaterOrEqual(GridPaletteBrushes.brushes.Count, 1);
            GridPaintingState.defaultBrush.Init(new Vector3Int(1, 1, 1));
            GridPaintingState.defaultBrush.SetTile(Vector3Int.zero, null);

            Vector2 startDrag = new Vector2(w.clipboardView.guiRect.width * 0.25f,
                w.clipboardView.guiRect.height * 0.25f);
            Vector2 endDrag = new Vector2(w.clipboardView.guiRect.width * 0.75f,
                w.clipboardView.guiRect.height * 0.75f);

            Grid grid = w.clipboardView.paletteInstance.GetComponent<Grid>();
            Vector3Int startCell = grid.LocalToCell(w.clipboardView.ScreenToLocal(startDrag));
            Vector3Int endCell = grid.LocalToCell(w.clipboardView.ScreenToLocal(endDrag));
            Vector3Int size = new Vector3Int(endCell.x - startCell.x + 1, startCell.y - endCell.y + 1, 1);

            startDrag += new Vector2(0f, paletteHeaderHeight);
            endDrag += new Vector2(0f, paletteHeaderHeight);

            ev.mousePosition = startDrag;
            ev.type = EventType.MouseMove;
            w.SendEvent(ev);
            ev.type = EventType.MouseDown;
            w.SendEvent(ev);
            ev.type = EventType.MouseDrag;
            w.SendEvent(ev);
            ev.type = EventType.MouseDrag;
            ev.mousePosition =
                new Vector2(-10, -10); // MousePosition is relative to window, negative values are outside of window
            w.SendEvent(ev);
            ev.type = EventType.MouseDrag;
            ev.mousePosition = endDrag;
            w.SendEvent(ev);
            ev.type = EventType.MouseUp;
            w.SendEvent(ev);

            Assert.AreEqual(size, GridPaintingState.defaultBrush.size);
        }

        [Test]
        public void PaintAndEraseOnPalette_SetsAndRemovesTileFromPalette()
        {
            var w = CreatePaletteWindow();
            Assert.True(AssetDatabase.CopyAsset(defaultTemporaryPalettePath, temporaryPalettePath),
                "Unable to create temporary palette");
            w.palette = AssetDatabase.LoadAssetAtPath<GameObject>(temporaryPalettePath);
            var tile = AssetDatabase.LoadAssetAtPath<Tile>(tilePath);

            Assert.GreaterOrEqual(GridPaletteBrushes.brushes.Count, 1);
            GridPaintingState.defaultBrush.Init(new Vector3Int(1, 1, 1));
            GridPaintingState.defaultBrush.SetTile(Vector3Int.zero, tile);
            w.clipboardView.unlocked = true;
            TilemapEditorTool.SetActiveEditorTool(typeof(PaintTool));

            Event ev = new Event();
            ev.mousePosition = w.clipboardView.GridToScreen(new Vector2(2.5f, -2.5f)) +
                new Vector2(0, paletteHeaderHeight);
            ev.type = EventType.MouseMove;
            w.SendEvent(ev);
            ev.type = EventType.MouseDown;
            w.SendEvent(ev);
            ev.type = EventType.MouseUp;
            w.SendEvent(ev);
            w.clipboardView.unlocked = false;

            Grid grid = w.clipboardView.paletteInstance.GetComponent<Grid>();
            Vector3 local = w.clipboardView.ScreenToLocal(ev.mousePosition - new Vector2(0, paletteHeaderHeight));
            Vector3Int pos3 = grid.LocalToCell(local);
            Vector2Int pos = new Vector2Int(pos3.x, pos3.y);
            Tilemap tilemap = AssetDatabase.LoadAssetAtPath<GameObject>(temporaryPalettePath)
                .GetComponentInChildren<Tilemap>();
            Assert.AreEqual(tile, tilemap.GetTile(new Vector3Int(pos.x, pos.y, 0)));

            w.clipboardView.unlocked = true;
            ev.shift = true;
            ev.mousePosition = w.clipboardView.GridToScreen(new Vector2(2.5f, -2.5f)) +
                new Vector2(0, paletteHeaderHeight);
            ev.type = EventType.MouseMove;
            w.SendEvent(ev);
            ev.type = EventType.MouseDown;
            w.SendEvent(ev);
            ev.type = EventType.MouseUp;
            w.SendEvent(ev);
            w.clipboardView.unlocked = false;

            tilemap = AssetDatabase.LoadAssetAtPath<GameObject>(temporaryPalettePath).GetComponentInChildren<Tilemap>();
            Assert.AreEqual(null, tilemap.GetTile(new Vector3Int(pos.x, pos.y, 0)));
        }

        [Test]
        public void EraseOnUneditablePalette_SwitchesToPaint()
        {
            var w = CreatePaletteWindow();
            Assert.True(AssetDatabase.CopyAsset(defaultTemporaryPalettePath, temporaryPalettePath),
                "Unable to create temporary palette");
            w.palette = AssetDatabase.LoadAssetAtPath<GameObject>(temporaryPalettePath);
            var tile = AssetDatabase.LoadAssetAtPath<Tile>(tilePath);

            Assert.GreaterOrEqual(GridPaletteBrushes.brushes.Count, 1);
            GridPaintingState.defaultBrush.Init(new Vector3Int(1, 1, 1));
            GridPaintingState.defaultBrush.SetTile(Vector3Int.zero, tile);
            TilemapEditorTool.SetActiveEditorTool(typeof(EraseTool));
            w.clipboardView.unlocked = false;

            Event ev = new Event();
            ev.mousePosition = w.clipboardView.GridToScreen(new Vector2(2.5f, -2.5f)) +
                new Vector2(0, paletteHeaderHeight);
            ev.type = EventType.MouseMove;
            w.SendEvent(ev);
            ev.type = EventType.MouseDown;
            w.SendEvent(ev);
            ev.type = EventType.MouseUp;
            w.SendEvent(ev);

            Assert.AreEqual(typeof(PaintTool), EditorTools.EditorTools.activeToolType);
        }

        [Test]
        public void PaintOnPalette_EraseAndCreateNewTileAsset_RemovesTileFromPalette()
        {
            var w = CreatePaletteWindow();
            Assert.True(AssetDatabase.CopyAsset(defaultTemporaryPalettePath, temporaryPalettePath),
                "Unable to create temporary palette");
            w.palette = AssetDatabase.LoadAssetAtPath<GameObject>(temporaryPalettePath);
            var tile = AssetDatabase.LoadAssetAtPath<Tile>(tilePath);

            Assert.GreaterOrEqual(GridPaletteBrushes.brushes.Count, 1);
            GridPaintingState.defaultBrush.Init(new Vector3Int(1, 1, 1));
            GridPaintingState.defaultBrush.SetTile(Vector3Int.zero, tile);
            w.clipboardView.unlocked = true;
            TilemapEditorTool.SetActiveEditorTool(typeof(PaintTool));

            Event ev = new Event();
            ev.mousePosition = w.clipboardView.GridToScreen(new Vector2(2.5f, -2.5f)) +
                new Vector2(0, paletteHeaderHeight);
            ev.type = EventType.MouseMove;
            w.SendEvent(ev);
            ev.type = EventType.MouseDown;
            w.SendEvent(ev);
            ev.type = EventType.MouseUp;
            w.SendEvent(ev);
            w.clipboardView.unlocked = false;

            Grid grid = w.clipboardView.paletteInstance.GetComponent<Grid>();
            Vector3 local = w.clipboardView.ScreenToLocal(ev.mousePosition - new Vector2(0, paletteHeaderHeight));
            Vector3Int pos3 = grid.LocalToCell(local);
            Vector2Int pos = new Vector2Int(pos3.x, pos3.y);
            Tilemap tilemap = AssetDatabase.LoadAssetAtPath<GameObject>(temporaryPalettePath)
                .GetComponentInChildren<Tilemap>();
            Assert.AreEqual(tile, tilemap.GetTile(new Vector3Int(pos.x, pos.y, 0)));

            w.clipboardView.unlocked = true;
            ev.shift = true;
            ev.mousePosition = w.clipboardView.GridToScreen(new Vector2(2.5f, -2.5f)) +
                new Vector2(0, paletteHeaderHeight);
            ev.type = EventType.MouseMove;
            w.SendEvent(ev);
            ev.type = EventType.MouseDown;
            w.SendEvent(ev);
            ev.type = EventType.MouseUp;
            w.SendEvent(ev);

            var temporaryTile = ScriptableObject.CreateInstance<Tile>();
            AssetDatabase.CreateAsset(temporaryTile, temporaryTilePath);

            tilemap = AssetDatabase.LoadAssetAtPath<GameObject>(temporaryPalettePath).GetComponentInChildren<Tilemap>();
            Assert.AreEqual(null, tilemap.GetTile(new Vector3Int(pos.x, pos.y, 0)));
        }

        [Test]
        [TestCase(Grid.CellLayout.Rectangle, GridLayout.CellSwizzle.XYZ, 3, -3, 2, 2)]
        [TestCase(Grid.CellLayout.Hexagon, GridLayout.CellSwizzle.XYZ, 3, -3, 2, 2)]
        [TestCase(Grid.CellLayout.Hexagon, GridLayout.CellSwizzle.YXZ, -3, 3, 2, 2)]
        public void PaintDifferentSizedTilesOnAutomaticCellSizePalette_UpdatesPaletteCellSize(Grid.CellLayout layout,
            GridLayout.CellSwizzle swizzle, int tileX, int tileY, float expectedCellXSize, float expectedCellYSize)
        {
            var smallTile = AssetDatabase.LoadAssetAtPath<Tile>(tilePath);
            var bigTile = AssetDatabase.LoadAssetAtPath<Tile>(bigTilePath);

            var newPalette = GridPaletteUtility.CreateNewPalette(path, name, layout, GridPalette.CellSizing.Automatic,
                Vector3.one, swizzle);
            var tilemap = newPalette.GetComponentInChildren<Tilemap>();
            tilemap.SetTile(new Vector3Int(tileX, tileY, 0), bigTile);

            var w = CreatePaletteWindow();
            w.palette = newPalette;
            w.clipboardView.previewUtility.camera.orthographicSize = 1000;
            w.clipboardView.ClampZoomAndPan();
            Assert.GreaterOrEqual(GridPaletteBrushes.brushes.Count, 1);
            GridPaintingState.defaultBrush.Init(new Vector3Int(1, 1, 1));
            GridPaintingState.defaultBrush.SetTile(Vector3Int.zero, smallTile);
            w.clipboardView.unlocked = true;
            TilemapEditorTool.SetActiveEditorTool(typeof(PaintTool));

            Event ev = new Event();
            ev.mousePosition = w.clipboardView.GridToScreen(new Vector2(tileX + 1.5f, tileY + 1.5f)) +
                new Vector2(0, paletteHeaderHeight);
            ev.type = EventType.MouseMove;
            w.SendEvent(ev);
            ev.type = EventType.MouseDown;
            w.SendEvent(ev);
            ev.type = EventType.MouseUp;
            w.SendEvent(ev);
            w.clipboardView.unlocked = false;

            var grid = w.palette.GetComponent<Grid>();
            Assert.AreEqual(expectedCellXSize, grid.cellSize.x);
            Assert.AreEqual(expectedCellYSize, grid.cellSize.y);
        }

        [Test,
         Description(
             "Case 947462: Deleting a tilemap in a grid prefab and saving back that prefab creates a duplicate in scene")]
        [TestCase(Grid.CellLayout.Rectangle, GridLayout.CellSwizzle.XYZ)]
        [TestCase(Grid.CellLayout.Hexagon, GridLayout.CellSwizzle.XYZ)]
        [TestCase(Grid.CellLayout.Hexagon, GridLayout.CellSwizzle.YXZ)]
        public void PalettePrefabIsUpdated_DoesNotCreateAnInstanceInScene(Grid.CellLayout layout,
            GridLayout.CellSwizzle swizzle)
        {
            var palettePrefab = GridPaletteUtility.CreateNewPalette(path, name, layout,
                GridPalette.CellSizing.Automatic, Vector3.one, swizzle);

            var w = CreatePaletteWindow();
            w.palette = palettePrefab;

            Object[] objs = GameObject.FindObjectsOfType<Grid>();
            Assert.AreEqual(0, objs.Length,
                "There should be 0 Grids in this test as Palette instances have HideAndDontSave hide flags");

            var sceneGameObject = (GameObject)PrefabUtility.InstantiatePrefab(palettePrefab);
            var grid = sceneGameObject.GetComponent<Grid>();

            Object[] objsAfterInstantiatePrefab = GameObject.FindObjectsOfType<Grid>();
            Assert.AreEqual(1, objsAfterInstantiatePrefab.Length,
                "There should be 1 Grid in this test which is the instantiated prefab.");
            Assert.AreEqual(objsAfterInstantiatePrefab[0], grid,
                "The Grid found should be the Grid instantiated from the prefab.");

            grid.cellGap = new Vector3(2.0f, 2.0f, 0.0f);

            PrefabUtility.SaveAsPrefabAssetAndConnect(sceneGameObject, AssetDatabase.GetAssetPath(palettePrefab),
                InteractionMode.AutomatedAction);

            Object[] objsAfterReplacePrefab = GameObject.FindObjectsOfType<Grid>();
            Assert.AreEqual(1, objsAfterReplacePrefab.Length,
                "There should be 1 Grid in this test which is the instantiated prefab after replacing the prefab.");
            Assert.AreEqual(objsAfterReplacePrefab[0], grid,
                "The Grid found should be the Grid instantiated from the prefab after replacing the prefab.");

            // Clean up
            Object.DestroyImmediate(sceneGameObject);
        }

        [Test]
        public void SelectAndDragOutsideofPalette_CancelsSelectAction()
        {
            var w = CreatePaletteWindow();
            w.palette = AssetDatabase.LoadAssetAtPath<GameObject>(defaultTemporaryPalettePath);
            Selection.activeObject = null;
            TilemapEditorTool.SetActiveEditorTool(typeof(SelectTool));

            Event ev = new Event();
            ev.mousePosition = w.clipboardView.GridToScreen(new Vector2(2.5f, -2.5f)) +
                new Vector2(0, paletteHeaderHeight);
            ev.type = EventType.MouseDown;
            w.SendEvent(ev);
            ev.type = EventType.MouseDrag;
            ev.mousePosition =
                new Vector2(-10, -10); // MousePosition is relative to window, negative values are outside of window
            w.SendEvent(ev);
            ev.type = EventType.MouseUp;
            w.SendEvent(ev);

            Assert.IsNull(Selection.activeObject);
        }

        [Test]
        public void SelectAndDragOutsideAndBackInsideOfPalette_DoesSelectAction()
        {
            var w = CreatePaletteWindow();
            w.palette = AssetDatabase.LoadAssetAtPath<GameObject>(defaultTemporaryPalettePath);
            Selection.activeObject = null;
            TilemapEditorTool.SetActiveEditorTool(typeof(SelectTool));

            Event ev = new Event();
            ev.mousePosition = w.clipboardView.GridToScreen(new Vector2(2.5f, -2.5f)) +
                new Vector2(0, paletteHeaderHeight);
            ev.type = EventType.MouseDown;
            w.SendEvent(ev);
            ev.type = EventType.MouseDrag;
            ev.mousePosition =
                new Vector2(-10, -10); // MousePosition is relative to window, negative values are outside of window
            w.SendEvent(ev);
            ev.type = EventType.MouseDrag;
            ev.mousePosition = w.clipboardView.GridToScreen(new Vector2(2.5f, -2.5f)) +
                new Vector2(0, paletteHeaderHeight);                // Drag back inside Palette
            w.SendEvent(ev);
            ev.type = EventType.MouseUp;
            w.SendEvent(ev);

            Assert.NotNull(Selection.activeObject);
            Assert.AreEqual(typeof(GridSelection), Selection.activeObject.GetType());
        }

        [Test]
        public void BrushCache_LoadsAllAvailableGridBrushesInProject()
        {
            Assert.AreEqual(GridPaletteBrushes.brushes.Count, 3);
            Assert.IsTrue(GridPaletteBrushes.brushes[0] is GridBrush);
            Assert.IsTrue(GridPaletteBrushes.brushes[1] is MyBrush);
            Assert.IsTrue(GridPaletteBrushes.brushes[2] is MyBrush);
        }

        [Test]
        public void BrushCache_RefreshesWhenNewGridBrushIsAddedToProject()
        {
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<MyBrush>(), temporaryAssetPath);
            Assert.AreEqual(GridPaletteBrushes.brushes.Count, 4);
            Assert.IsTrue(GridPaletteBrushes.brushes[3] is MyBrush);
        }

        [Test]
        public void GridPaintingState_NoActiveTargets_HasNoScenePaintTarget()
        {
            var tilemapGO1 = CreateTilemapGameObjectWithName(validMyBrushTargetName);
            var tilemapGO2 = CreateTilemapGameObjectWithName(validMyBrushTargetName);

            CreatePaletteWindow();

            GridPaintingState.AutoSelectPaintTarget();
            Assert.IsNotNull(GridPaintingState.scenePaintTarget);

            tilemapGO2.SetActive(false);
            GridPaintingState.FlushCache();
            GridPaintingState.AutoSelectPaintTarget();
            Assert.IsTrue(GridPaintingState.scenePaintTarget == tilemapGO1);

            tilemapGO1.SetActive(false);
            GridPaintingState.FlushCache();
            GridPaintingState.AutoSelectPaintTarget();
            Assert.IsTrue(GridPaintingState.scenePaintTarget == null);

            Object.DestroyImmediate(tilemapGO1);
            Object.DestroyImmediate(tilemapGO2);
        }

        public struct TileDragAndDropTestCase
        {
            public string assetPath;
            public Vector2Int pixel;
            public Vector2Int offset;
            public Vector2Int padding;

            public override String ToString()
            {
                return Path.GetFileName(assetPath);
            }
        }

        private static IEnumerable<TileDragAndDropTestCase> TileDragAndDropTestCases()
        {
            yield return new TileDragAndDropTestCase()
            { assetPath = emptyColumnRowTexturePath, pixel = new Vector2Int(16, 16), offset = new Vector2Int(16, 0), padding = new Vector2Int(0, 0) };
            yield return new TileDragAndDropTestCase
            { assetPath = square0000TexturePath, pixel = new Vector2Int(32, 32), offset = new Vector2Int(0, 0), padding = new Vector2Int(0, 0) };
            yield return new TileDragAndDropTestCase
            { assetPath = square1111TexturePath, pixel = new Vector2Int(32, 32), offset = new Vector2Int(1, 1), padding = new Vector2Int(1, 1) };
            yield return new TileDragAndDropTestCase
            { assetPath = square1222TexturePath, pixel = new Vector2Int(32, 32), offset = new Vector2Int(1, 2), padding = new Vector2Int(2, 2) };
        }

        [Test, Description("case 1103034: Missing Tiles when dragging a Spritesheet with empty column and row onto a Tile Palette")]
        public void TileDragAndDrop_SpritesheetTexture_HasCorrectEstimatedGridPixelOffsetPadding([ValueSource("TileDragAndDropTestCases")] TileDragAndDropTestCase testCase)
        {
            var sprites = AssetDatabase.LoadAllAssetsAtPath(testCase.assetPath).Where(x => x is Sprite)
                .Cast<Sprite>().ToList();
            Assert.IsTrue(sprites.Count > 0);
            var gridPixelSize = TileDragAndDrop.EstimateGridPixelSize(sprites);
            var gridOffsetSize = TileDragAndDrop.EstimateGridOffsetSize(sprites);
            var gridPaddingSize = TileDragAndDrop.EstimateGridPaddingSize(sprites, gridPixelSize, gridOffsetSize);
            Assert.AreEqual(testCase.pixel, gridPixelSize);
            Assert.AreEqual(testCase.offset, gridOffsetSize);
            Assert.AreEqual(testCase.padding, gridPaddingSize);
        }
    }
}
