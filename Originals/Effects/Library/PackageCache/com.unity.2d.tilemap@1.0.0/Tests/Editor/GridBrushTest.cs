using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using NUnit.Framework;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

namespace UnityEditor.Tilemaps.Tests
{
    internal class GridBrushTest
    {
        private static SceneView s_SceneView;
        private static GridPaintPaletteWindow s_PaletteWindow;
        private static List<UnityEngine.Object> s_Objects;

        private GameObject m_TilemapGameObject;
        private Grid m_Grid;
        private Tilemap m_Tilemap;
        private TileBase m_TileA;
        private TileBase m_TileB;
        private TileBase m_TileC;

        [TearDown]
        public void TearDown()
        {
            SessionState.EraseInt(GridPaletteBrushes.s_SessionStateLastUsedBrush);
            GridPaintingState.gridBrush = GridPaletteBrushes.brushes[0];

            GridPaletteBrushes.FlushCache();
            foreach (var obj in s_Objects)
                Object.DestroyImmediate(obj);
            Object.DestroyImmediate(m_TileA);
            Object.DestroyImmediate(m_TileB);
            Object.DestroyImmediate(m_TileC);

            s_Objects.Clear();
            s_SceneView.Close();
            s_PaletteWindow.Close();
        }

        [SetUp]
        public void SetUp()
        {
            s_SceneView = EditorWindow.GetWindow<SceneView>();
            s_PaletteWindow = EditorWindow.GetWindow<GridPaintPaletteWindow>();
            s_Objects = new List<UnityEngine.Object>();

            m_TilemapGameObject = CreateTilemapGameObject();
            m_Grid = m_TilemapGameObject.GetComponent<Grid>();
            m_Tilemap = m_TilemapGameObject.GetComponent<Tilemap>();
            Selection.activeGameObject = m_TilemapGameObject;

            m_TileA = CreateScriptableObject<Tile>();
            m_TileB = CreateScriptableObject<Tile>();
            m_TileC = CreateScriptableObject<Tile>();

            ResetSceneViewWindowPosition();
            ResetPaletteWindowPosition();
            ResetSceneViewCamera();

            SessionState.EraseInt(GridPaletteBrushes.s_SessionStateLastUsedBrush);
            GridPaintingState.gridBrush = GridPaletteBrushes.brushes[0];
        }

        private T CreateScriptableObject<T>() where T : ScriptableObject
        {
            var obj = ScriptableObject.CreateInstance<T>();
            s_Objects.Add(obj);
            return obj;
        }

        private GameObject CreateGameObject()
        {
            var obj = new GameObject();
            s_Objects.Add(obj);
            return obj;
        }

        private GameObject CreateTilemapGameObject()
        {
            var go = CreateGameObject();
            go.AddComponent<Grid>();
            go.AddComponent<Tilemap>();
            return go;
        }

        private MyBrush SetupCustomBrush()
        {
            GridPaletteBrushes.FlushCache();
            GridPaintingState.gridBrush = GridPaletteBrushes.brushes[1];
            MyBrush brush = GridPaintingState.gridBrush as MyBrush;
            brush.m_LastCalledMethod = "";
            return brush;
        }

        [Test]
        public void GridBrush_EnsureDefaultState()
        {
            var gridBrush = CreateScriptableObject<GridBrush>();

            Assert.AreEqual(1, gridBrush.size.x);
            Assert.AreEqual(1, gridBrush.size.y);
            Assert.AreEqual(0, gridBrush.pivot.x);
            Assert.AreEqual(0, gridBrush.pivot.y);
        }

        [Test]
        public void GridBrush_SetTiles_CellsInBrushHasTileSet()
        {
            var gridBrush = CreateScriptableObject<GridBrush>();

            gridBrush.Init(new Vector3Int(2, 1, 1));

            gridBrush.SetTile(new Vector3Int(0, 0, 0), m_TileA);
            gridBrush.SetTile(new Vector3Int(1, 0, 0), m_TileB);

            Assert.AreEqual(m_TileA, gridBrush.cells[0].tile);
            Assert.AreEqual(m_TileB, gridBrush.cells[1].tile);
        }

        [Test]
        public void GridBrush_SetMatrix_CellsInBrushHasMatrixSet()
        {
            var gridBrush = CreateScriptableObject<GridBrush>();

            gridBrush.Init(new Vector3Int(2, 1, 1));

            var matrixA = Matrix4x4.identity;
            matrixA.m20 = 5;
            var matrixB = Matrix4x4.identity;
            matrixB.m02 = 9;

            gridBrush.SetTile(Vector3Int.zero, m_TileA);
            gridBrush.SetTile(new Vector3Int(1, 0, 0), m_TileB);
            gridBrush.SetMatrix(Vector3Int.zero, matrixB);
            gridBrush.SetMatrix(new Vector3Int(1, 0, 0), matrixA);

            Assert.AreEqual(matrixB, gridBrush.cells[0].matrix);
            Assert.AreEqual(matrixA, gridBrush.cells[1].matrix);
        }

        [Test]
        public void GridBrush_FlipX_CellsInBrushAreFlippedHorizontally()
        {
            var gridBrush = CreateScriptableObject<GridBrush>();
            var matrixFlipX = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(-1f, 1f, 1f));

            gridBrush.Init(new Vector3Int(2, 1, 1));

            gridBrush.SetTile(Vector3Int.zero, m_TileA);
            gridBrush.SetTile(new Vector3Int(1, 0, 0), m_TileB);
            gridBrush.Flip(GridBrush.FlipAxis.X, Grid.CellLayout.Rectangle);

            Assert.AreEqual(2, gridBrush.size.x);
            Assert.AreEqual(1, gridBrush.size.y);
            Assert.AreEqual(1, gridBrush.pivot.x);
            Assert.AreEqual(0, gridBrush.pivot.y);
            Assert.AreEqual(m_TileB, gridBrush.cells[0].tile);
            Assert.AreEqual(matrixFlipX, gridBrush.cells[0].matrix);
            Assert.AreEqual(m_TileA, gridBrush.cells[1].tile);
            Assert.AreEqual(matrixFlipX, gridBrush.cells[1].matrix);
        }

        [Test]
        public void GridBrush_FlipY_CellsInBrushAreFlippedVertically()
        {
            var gridBrush = CreateScriptableObject<GridBrush>();
            var matrixFlipY = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(1f, -1f, 1f));

            gridBrush.Init(new Vector3Int(1, 2, 1));

            gridBrush.SetTile(Vector3Int.zero, m_TileA);
            gridBrush.SetTile(new Vector3Int(0, 1, 0), m_TileB);
            gridBrush.Flip(GridBrush.FlipAxis.Y, Grid.CellLayout.Rectangle);

            Assert.AreEqual(1, gridBrush.size.x);
            Assert.AreEqual(2, gridBrush.size.y);
            Assert.AreEqual(0, gridBrush.pivot.x);
            Assert.AreEqual(1, gridBrush.pivot.y);
            Assert.AreEqual(m_TileB, gridBrush.cells[0].tile);
            Assert.AreEqual(matrixFlipY, gridBrush.cells[0].matrix);
            Assert.AreEqual(m_TileA, gridBrush.cells[1].tile);
            Assert.AreEqual(matrixFlipY, gridBrush.cells[1].matrix);
        }

        [Test]
        public void GridBrush_RotateClockwise_CellsInBrushAreRotatedClockwise()
        {
            var gridBrush = CreateScriptableObject<GridBrush>();
            var matrixRotateClockwise = new Matrix4x4(new Vector4(0f, 1f, 0f, 0f), new Vector4(-1f, 0f, 0f, 0f),
                new Vector4(0f, 0f, 1f, 0f), new Vector4(0f, 0f, 0f, 1f));

            gridBrush.Init(new Vector3Int(2, 1, 1));

            gridBrush.SetTile(Vector3Int.zero, m_TileA);
            gridBrush.SetTile(new Vector3Int(1, 0, 0), m_TileB);
            gridBrush.Rotate(GridBrush.RotationDirection.Clockwise, Grid.CellLayout.Rectangle);

            Assert.AreEqual(1, gridBrush.size.x);
            Assert.AreEqual(2, gridBrush.size.y);
            Assert.AreEqual(0, gridBrush.pivot.x);
            Assert.AreEqual(0, gridBrush.pivot.y);
            Assert.AreEqual(m_TileA, gridBrush.cells[0].tile);
            Assert.AreEqual(matrixRotateClockwise, gridBrush.cells[0].matrix);
            Assert.AreEqual(m_TileB, gridBrush.cells[1].tile);
            Assert.AreEqual(matrixRotateClockwise, gridBrush.cells[1].matrix);
        }

        [Test]
        public void GridBrush_RotateCounterClockwise_CellsInBrushAreRotatedCounterClockwise()
        {
            var gridBrush = CreateScriptableObject<GridBrush>();
            var matrixRotateCounterClockwise = new Matrix4x4(new Vector4(0f, -1f, 0f, 0f), new Vector4(1f, 0f, 0f, 0f),
                new Vector4(0f, 0f, 1f, 0f), new Vector4(0f, 0f, 0f, 1f));

            gridBrush.Init(new Vector3Int(2, 1, 1));

            gridBrush.SetTile(Vector3Int.zero, m_TileA);
            gridBrush.SetTile(new Vector3Int(1, 0, 0), m_TileB);
            gridBrush.Rotate(GridBrush.RotationDirection.CounterClockwise, Grid.CellLayout.Rectangle);

            Assert.AreEqual(1, gridBrush.size.x);
            Assert.AreEqual(2, gridBrush.size.y);
            Assert.AreEqual(0, gridBrush.pivot.x);
            Assert.AreEqual(1, gridBrush.pivot.y);
            Assert.AreEqual(m_TileB, gridBrush.cells[0].tile);
            Assert.AreEqual(matrixRotateCounterClockwise, gridBrush.cells[0].matrix);
            Assert.AreEqual(m_TileA, gridBrush.cells[1].tile);
            Assert.AreEqual(matrixRotateCounterClockwise, gridBrush.cells[1].matrix);
        }

        public class FlipRotateTestCase
        {
            public GridBrush.FlipAxis flipAxis;
            public GridBrush.RotationDirection rotationDirection;
            public bool flipBeforeRotate;
            public bool m_TileBBeforeTileA;
            public Vector3Int size;
            public Vector3Int pivot;
            public Matrix4x4 transform;

            public override String ToString()
            {
                return String.Format(flipBeforeRotate ? "{0}, {1}" : "{1}, {0}"
                    , Enum.GetName(typeof(GridBrush.FlipAxis), flipAxis)
                    , Enum.GetName(typeof(GridBrush.RotationDirection), rotationDirection));
            }
        }

        private static IEnumerable<FlipRotateTestCase> FlipRotateTestCases()
        {
            yield return new FlipRotateTestCase
            {
                flipAxis = GridBrush.FlipAxis.X,
                rotationDirection = GridBrushBase.RotationDirection.Clockwise,
                flipBeforeRotate = false,
                m_TileBBeforeTileA = false,
                size = new Vector3Int(1, 2, 1),
                pivot = Vector3Int.zero,
                transform = new Matrix4x4(new Vector4(0f, 1f, 0f, 0f), new Vector4(1f, 0f, 0f, 0f),
                    new Vector4(0f, 0f, 1f, 0f), new Vector4(0f, 0f, 0f, 1f))
            };
            yield return new FlipRotateTestCase
            {
                flipAxis = GridBrush.FlipAxis.X,
                rotationDirection = GridBrushBase.RotationDirection.Clockwise,
                flipBeforeRotate = true,
                m_TileBBeforeTileA = true,
                size = new Vector3Int(1, 2, 1),
                pivot = new Vector3Int(0, 1, 0),
                transform = new Matrix4x4(new Vector4(0f, -1f, 0f, 0f), new Vector4(-1f, 0f, 0f, 0f),
                    new Vector4(0f, 0f, 1f, 0f), new Vector4(0f, 0f, 0f, 1f))
            };
            yield return new FlipRotateTestCase
            {
                flipAxis = GridBrush.FlipAxis.X,
                rotationDirection = GridBrushBase.RotationDirection.CounterClockwise,
                flipBeforeRotate = false,
                m_TileBBeforeTileA = true,
                size = new Vector3Int(1, 2, 1),
                pivot = new Vector3Int(0, 1, 0),
                transform = new Matrix4x4(new Vector4(0f, -1f, 0f, 0f), new Vector4(-1f, 0f, 0f, 0f),
                    new Vector4(0f, 0f, 1f, 0f), new Vector4(0f, 0f, 0f, 1f))
            };
            yield return new FlipRotateTestCase
            {
                flipAxis = GridBrush.FlipAxis.X,
                rotationDirection = GridBrushBase.RotationDirection.CounterClockwise,
                flipBeforeRotate = true,
                m_TileBBeforeTileA = false,
                size = new Vector3Int(1, 2, 1),
                pivot = Vector3Int.zero,
                transform = new Matrix4x4(new Vector4(0f, 1f, 0f, 0f), new Vector4(1f, 0f, 0f, 0f),
                    new Vector4(0f, 0f, 1f, 0f), new Vector4(0f, 0f, 0f, 1f))
            };
            yield return new FlipRotateTestCase
            {
                flipAxis = GridBrush.FlipAxis.Y,
                rotationDirection = GridBrushBase.RotationDirection.Clockwise,
                flipBeforeRotate = false,
                m_TileBBeforeTileA = true,
                size = new Vector3Int(1, 2, 1),
                pivot = new Vector3Int(0, 1, 0),
                transform = new Matrix4x4(new Vector4(0f, -1f, 0f, 0f), new Vector4(-1f, 0f, 0f, 0f),
                    new Vector4(0f, 0f, 1f, 0f), new Vector4(0f, 0f, 0f, 1f))
            };
            yield return new FlipRotateTestCase
            {
                flipAxis = GridBrush.FlipAxis.Y,
                rotationDirection = GridBrushBase.RotationDirection.Clockwise,
                flipBeforeRotate = true,
                m_TileBBeforeTileA = false,
                size = new Vector3Int(1, 2, 1),
                pivot = Vector3Int.zero,
                transform = new Matrix4x4(new Vector4(0f, 1f, 0f, 0f), new Vector4(1f, 0f, 0f, 0f),
                    new Vector4(0f, 0f, 1f, 0f), new Vector4(0f, 0f, 0f, 1f))
            };
            yield return new FlipRotateTestCase
            {
                flipAxis = GridBrush.FlipAxis.Y,
                rotationDirection = GridBrushBase.RotationDirection.CounterClockwise,
                flipBeforeRotate = false,
                m_TileBBeforeTileA = false,
                size = new Vector3Int(1, 2, 1),
                pivot = Vector3Int.zero,
                transform = new Matrix4x4(new Vector4(0f, 1f, 0f, 0f), new Vector4(1f, 0f, 0f, 0f),
                    new Vector4(0f, 0f, 1f, 0f), new Vector4(0f, 0f, 0f, 1f))
            };
            yield return new FlipRotateTestCase
            {
                flipAxis = GridBrush.FlipAxis.Y,
                rotationDirection = GridBrushBase.RotationDirection.CounterClockwise,
                flipBeforeRotate = true,
                m_TileBBeforeTileA = true,
                size = new Vector3Int(1, 2, 1),
                pivot = new Vector3Int(0, 1, 0),
                transform = new Matrix4x4(new Vector4(0f, -1f, 0f, 0f), new Vector4(-1f, 0f, 0f, 0f),
                    new Vector4(0f, 0f, 1f, 0f), new Vector4(0f, 0f, 0f, 1f))
            };
        }

        [Test]
        public void GridBrush_RotationAndFlip_CellsInBrushAreRotatedAndFlipped(
            [ValueSource("FlipRotateTestCases")] FlipRotateTestCase testCase)
        {
            var gridBrush = CreateScriptableObject<GridBrush>();

            gridBrush.Init(new Vector3Int(2, 1, 1));

            gridBrush.SetTile(Vector3Int.zero, m_TileA);
            gridBrush.SetTile(new Vector3Int(1, 0, 0), m_TileB);

            if (testCase.m_TileBBeforeTileA)
            {
                var temp = m_TileA;
                m_TileA = m_TileB;
                m_TileB = temp;
            }

            if (testCase.flipBeforeRotate)
            {
                gridBrush.Flip(testCase.flipAxis, Grid.CellLayout.Rectangle);
                gridBrush.Rotate(testCase.rotationDirection, Grid.CellLayout.Rectangle);
            }
            else
            {
                gridBrush.Rotate(testCase.rotationDirection, Grid.CellLayout.Rectangle);
                gridBrush.Flip(testCase.flipAxis, Grid.CellLayout.Rectangle);
            }

            Assert.AreEqual(testCase.size, gridBrush.size);
            Assert.AreEqual(testCase.pivot, gridBrush.pivot);
            Assert.AreEqual(m_TileA, gridBrush.cells[0].tile);
            Assert.AreEqual(m_TileB, gridBrush.cells[1].tile);
            Assert.AreEqual(testCase.transform, gridBrush.cells[0].matrix);
            Assert.AreEqual(testCase.transform, gridBrush.cells[1].matrix);
        }

        [Test]
        public void GridBrush_PaintOnTilemap_GridBrushSetsTilesOnTilemap()
        {
            var gridBrush = CreateScriptableObject<GridBrush>();

            gridBrush.Init(new Vector3Int(1, 1, 1));
            gridBrush.SetTile(Vector3Int.zero, m_TileA);

            var targetPositionA1 = Vector3Int.zero;
            var targetPositionA2 = new Vector3Int(1, 1, 0);
            gridBrush.Paint(m_Grid, Selection.activeGameObject, targetPositionA1);
            gridBrush.Paint(m_Grid, Selection.activeGameObject, targetPositionA2);

            gridBrush.SetTile(Vector3Int.zero, m_TileB);

            var targetPositionB1 = new Vector3Int(0, 1, 0);
            var targetPositionB2 = new Vector3Int(1, 0, 0);
            gridBrush.Paint(m_Grid, Selection.activeGameObject, targetPositionB1);
            gridBrush.Paint(m_Grid, Selection.activeGameObject, targetPositionB2);

            Assert.AreEqual(m_TileA, m_Tilemap.GetTile(targetPositionA1));
            Assert.AreEqual(m_TileA, m_Tilemap.GetTile(targetPositionA2));
            Assert.AreEqual(m_TileB, m_Tilemap.GetTile(targetPositionB1));
            Assert.AreEqual(m_TileB, m_Tilemap.GetTile(targetPositionB2));
        }

        [Test]
        public void GridBrush_EraseOnTilemap_GridBrushErasesTilesOnTilemap()
        {
            var gridBrush = CreateScriptableObject<GridBrush>();
            gridBrush.Init(new Vector3Int(1, 1, 1));
            var targetPositionA1 = Vector3Int.zero;
            var targetPositionA2 = new Vector3Int(1, 1, 0);
            m_Tilemap.SetTile(targetPositionA1, m_TileA);
            m_Tilemap.SetTile(targetPositionA2, m_TileB);

            Selection.activeGameObject = m_TilemapGameObject;
            gridBrush.Erase(m_Grid, Selection.activeGameObject, targetPositionA1);
            gridBrush.Erase(m_Grid, Selection.activeGameObject, targetPositionA2);

            Assert.AreEqual(null, m_Tilemap.GetTile(targetPositionA1));
            Assert.AreEqual(null, m_Tilemap.GetTile(targetPositionA2));
        }

        [Test]
        public void GridBrush_SetWithMultipleTiles_PaintOnTilemap_GridBrushSetsMultipleTilesOnTilemap()
        {
            var gridBrush = CreateScriptableObject<GridBrush>();
            gridBrush.Init(new Vector3Int(3, 2, 1));

            gridBrush.SetTile(new Vector3Int(0, 0, 0), m_TileA);
            gridBrush.SetTile(new Vector3Int(1, 0, 0), m_TileB);
            gridBrush.SetTile(new Vector3Int(2, 0, 0), m_TileA);
            gridBrush.SetTile(new Vector3Int(0, 1, 0), m_TileB);
            gridBrush.SetTile(new Vector3Int(2, 1, 0), m_TileA);

            var targetPosition = new Vector3Int(5, 6, 0);
            gridBrush.Paint(m_Grid, Selection.activeGameObject, targetPosition);

            Assert.AreEqual(m_TileA, m_Tilemap.GetTile(targetPosition + new Vector3Int(0, 0, 0)));
            Assert.AreEqual(m_TileB, m_Tilemap.GetTile(targetPosition + new Vector3Int(1, 0, 0)));
            Assert.AreEqual(m_TileA, m_Tilemap.GetTile(targetPosition + new Vector3Int(2, 0, 0)));
            Assert.AreEqual(m_TileB, m_Tilemap.GetTile(targetPosition + new Vector3Int(0, 1, 0)));
            Assert.AreEqual(null, m_Tilemap.GetTile(targetPosition + new Vector3Int(1, 1, 0)));
            Assert.AreEqual(m_TileA, m_Tilemap.GetTile(targetPosition + new Vector3Int(2, 1, 0)));
        }

        [Test]
        public void GridBrush_SetWithMultipleTiles_PaintOnTilemap_TriggersSyncTileOncePerCall()
        {
            int count = 0;
            var gridBrush = CreateScriptableObject<GridBrush>();
            gridBrush.Init(new Vector3Int(3, 2, 1));

            gridBrush.SetTile(new Vector3Int(0, 0, 0), m_TileA);
            gridBrush.SetTile(new Vector3Int(1, 0, 0), m_TileB);
            gridBrush.SetTile(new Vector3Int(2, 0, 0), m_TileA);
            gridBrush.SetTile(new Vector3Int(0, 1, 0), m_TileB);
            gridBrush.SetTile(new Vector3Int(2, 1, 0), m_TileA);

            System.Action<Tilemap, Tilemap.SyncTile[]> tilemapAction = (tilemap, syncTiles) =>
            {
                count++;
                Assert.AreEqual(tilemap, m_Tilemap);
                Assert.AreEqual(5, syncTiles.Length);
            };

            var targetPositionA = new Vector3Int(5, 6, 0);
            var targetPositionB = new Vector3Int(6, 6, 0);
            Tilemap.SetSyncTileCallback(tilemapAction);
            gridBrush.Paint(m_Grid, Selection.activeGameObject, targetPositionA);
            Assert.AreEqual(1, count);
            gridBrush.Paint(m_Grid, Selection.activeGameObject, targetPositionB);
            Assert.AreEqual(2, count);
            Tilemap.RemoveSyncTileCallback(tilemapAction);
        }

        [Test]
        public void GridBrush_SetWithMultipleTiles_DragPaintOnTilemap_TriggersSyncTileOncePerCall()
        {
            int count = 0;
            var gridBrush = CreateScriptableObject<GridBrush>();
            gridBrush.Init(new Vector3Int(3, 2, 1));

            gridBrush.SetTile(new Vector3Int(0, 0, 0), m_TileA);
            gridBrush.SetTile(new Vector3Int(1, 0, 0), m_TileB);
            gridBrush.SetTile(new Vector3Int(2, 0, 0), m_TileA);
            gridBrush.SetTile(new Vector3Int(0, 1, 0), m_TileB);
            gridBrush.SetTile(new Vector3Int(2, 1, 0), m_TileA);

            System.Action<Tilemap, Tilemap.SyncTile[]> tilemapAction = (tilemap, syncTiles) =>
            {
                count++;
                Assert.AreEqual(tilemap, m_Tilemap);
            };

            GridPaintingState.gridBrush = gridBrush;
            TilemapEditorTool.SetActiveEditorTool(typeof(PaintTool));
            SceneViewMouseEnter();

            Tilemap.SetSyncTileCallback(tilemapAction);
            SceneViewMouseDrag(new Vector2(75, 75), new Vector2(175, 175));
            Tilemap.RemoveSyncTileCallback(tilemapAction);

            Assert.AreEqual(1, count);
        }

        [Test]
        public void GridBrush_SetWithMultipleTiles_EraseOnTilemap_GridBrushErasesMultipleTilesOnTilemap()
        {
            var gridBrush = CreateScriptableObject<GridBrush>();
            gridBrush.Init(new Vector3Int(3, 5, 1));
            var targetPosition = new Vector3Int(5, 5, 0);
            m_Tilemap.SetTile(targetPosition + new Vector3Int(0, 0, 0), m_TileA);
            m_Tilemap.SetTile(targetPosition + new Vector3Int(1, 1, 0), m_TileA);
            m_Tilemap.SetTile(targetPosition + new Vector3Int(2, 2, 0), m_TileA);

            gridBrush.Erase(m_Grid, Selection.activeGameObject, targetPosition);

            Assert.AreEqual(null, m_Tilemap.GetTile(targetPosition + new Vector3Int(0, 0, 0)));
            Assert.AreEqual(null, m_Tilemap.GetTile(targetPosition + new Vector3Int(1, 1, 0)));
            Assert.AreEqual(null, m_Tilemap.GetTile(targetPosition + new Vector3Int(2, 2, 0)));
        }

        [Test]
        public void GridBrush_SetWithMultipleTiles_EraseOnTilemap_TriggersSyncTileOncePerCall()
        {
            int count = 0;
            var gridBrush = CreateScriptableObject<GridBrush>();
            gridBrush.Init(new Vector3Int(3, 2, 1));

            gridBrush.SetTile(new Vector3Int(0, 0, 0), m_TileA);
            gridBrush.SetTile(new Vector3Int(1, 0, 0), m_TileB);
            gridBrush.SetTile(new Vector3Int(2, 0, 0), m_TileA);
            gridBrush.SetTile(new Vector3Int(0, 1, 0), m_TileB);
            gridBrush.SetTile(new Vector3Int(2, 1, 0), m_TileA);

            System.Action<Tilemap, Tilemap.SyncTile[]> tilemapAction = (tilemap, syncTiles) =>
            {
                count++;
                Assert.AreEqual(tilemap, m_Tilemap);
                Assert.AreEqual(5, syncTiles.Length);
            };

            var targetPosition = new Vector3Int(5, 6, 0);
            gridBrush.Paint(m_Grid, Selection.activeGameObject, targetPosition);

            Tilemap.SetSyncTileCallback(tilemapAction);
            gridBrush.Erase(m_Grid, Selection.activeGameObject, targetPosition);
            Assert.AreEqual(1, count);
            Tilemap.RemoveSyncTileCallback(tilemapAction);
        }

        [Test]
        public void GridBrush_SetWithMultipleTiles_DragEraseOnTilemap_TriggersSyncTileOncePerCall()
        {
            int count = 0;
            var gridBrush = CreateScriptableObject<GridBrush>();
            gridBrush.Init(new Vector3Int(3, 2, 1));

            gridBrush.SetTile(new Vector3Int(0, 0, 0), m_TileA);
            gridBrush.SetTile(new Vector3Int(1, 0, 0), m_TileB);
            gridBrush.SetTile(new Vector3Int(2, 0, 0), m_TileA);
            gridBrush.SetTile(new Vector3Int(0, 1, 0), m_TileB);
            gridBrush.SetTile(new Vector3Int(2, 1, 0), m_TileA);

            System.Action<Tilemap, Tilemap.SyncTile[]> tilemapAction = (tilemap, syncTiles) =>
            {
                count++;
                Assert.AreEqual(tilemap, m_Tilemap);
            };

            m_Tilemap.origin = new Vector3Int(-10, -10, 0);
            m_Tilemap.size = new Vector3Int(30, 30, 0);
            m_Tilemap.FloodFill(Vector3Int.zero, m_TileC);

            GridPaintingState.gridBrush = gridBrush;
            TilemapEditorTool.SetActiveEditorTool(typeof(EraseTool));
            SceneViewMouseEnter();

            Tilemap.SetSyncTileCallback(tilemapAction);
            SceneViewMouseDrag(new Vector2(75, 75), new Vector2(175, 175));
            Tilemap.RemoveSyncTileCallback(tilemapAction);

            Assert.AreEqual(1, count);
        }

        [Test]
        public void GridBrush_BoxFillOnTilemap_TriggersSyncTileOncePerCall()
        {
            int count = 0;
            var gridBrush = CreateScriptableObject<GridBrush>();
            gridBrush.Init(new Vector3Int(3, 2, 1));

            gridBrush.SetTile(new Vector3Int(0, 0, 0), m_TileA);
            gridBrush.SetTile(new Vector3Int(1, 0, 0), m_TileB);
            gridBrush.SetTile(new Vector3Int(2, 0, 0), m_TileA);
            gridBrush.SetTile(new Vector3Int(0, 1, 0), m_TileB);
            gridBrush.SetTile(new Vector3Int(2, 1, 0), m_TileA);

            System.Action<Tilemap, Tilemap.SyncTile[]> tilemapAction = (tilemap, syncTiles) =>
            {
                count++;
                Assert.AreEqual(tilemap, m_Tilemap);
                Assert.AreEqual(43, syncTiles.Length);
            };

            var targetPosition = new BoundsInt(5, 6, 0, 7, 7, 1);
            Tilemap.SetSyncTileCallback(tilemapAction);
            gridBrush.BoxFill(m_Grid, Selection.activeGameObject, targetPosition);
            Assert.AreEqual(1, count);
            Tilemap.RemoveSyncTileCallback(tilemapAction);
        }

        [Test]
        public void GridBrush_DragBoxFillOnTilemap_TriggersSyncTileOncePerCall()
        {
            int count = 0;
            var gridBrush = CreateScriptableObject<GridBrush>();
            gridBrush.Init(new Vector3Int(3, 2, 1));

            gridBrush.SetTile(new Vector3Int(0, 0, 0), m_TileA);
            gridBrush.SetTile(new Vector3Int(1, 0, 0), m_TileB);
            gridBrush.SetTile(new Vector3Int(2, 0, 0), m_TileA);
            gridBrush.SetTile(new Vector3Int(0, 1, 0), m_TileB);
            gridBrush.SetTile(new Vector3Int(2, 1, 0), m_TileA);

            System.Action<Tilemap, Tilemap.SyncTile[]> tilemapAction = (tilemap, syncTiles) =>
            {
                count++;
                Assert.AreEqual(tilemap, m_Tilemap);
            };

            GridPaintingState.gridBrush = gridBrush;
            TilemapEditorTool.SetActiveEditorTool(typeof(BoxTool));
            SceneViewMouseEnter();

            Tilemap.SetSyncTileCallback(tilemapAction);
            SceneViewMouseDrag(new Vector2(75, 75), new Vector2(175, 175));
            Tilemap.RemoveSyncTileCallback(tilemapAction);
            Assert.AreEqual(1, count);
        }

        [Test]
        public void GridBrush_BoxFillOnTilemap_GridBrushBoxFillsTilesOnTilemap()
        {
            var gridBrush = CreateScriptableObject<GridBrush>();
            gridBrush.Init(new Vector3Int(3, 2, 1));

            gridBrush.SetTile(new Vector3Int(0, 0, 0), m_TileA);
            gridBrush.SetTile(new Vector3Int(1, 0, 0), m_TileB);
            gridBrush.SetTile(new Vector3Int(2, 0, 0), m_TileA);
            gridBrush.SetTile(new Vector3Int(0, 1, 0), m_TileB);
            gridBrush.SetTile(new Vector3Int(2, 1, 0), m_TileA);

            var targetPosition = new BoundsInt(5, 6, 0, 7, 7, 1);
            gridBrush.BoxFill(m_Grid, Selection.activeGameObject, targetPosition);

            Assert.AreEqual(m_TileA, m_Tilemap.GetTile(targetPosition.min + new Vector3Int(0, 0, 0)));
            Assert.AreEqual(m_TileB, m_Tilemap.GetTile(targetPosition.min + new Vector3Int(1, 0, 0)));
            Assert.AreEqual(m_TileA, m_Tilemap.GetTile(targetPosition.min + new Vector3Int(2, 0, 0)));
            Assert.AreEqual(m_TileB, m_Tilemap.GetTile(targetPosition.min + new Vector3Int(0, 1, 0)));
            Assert.AreEqual(null, m_Tilemap.GetTile(targetPosition.min + new Vector3Int(1, 1, 0)));
            Assert.AreEqual(m_TileA, m_Tilemap.GetTile(targetPosition.min + new Vector3Int(2, 1, 0)));
            Assert.AreEqual(m_TileA, m_Tilemap.GetTile(targetPosition.min + new Vector3Int(3, 2, 0)));
            Assert.AreEqual(m_TileB, m_Tilemap.GetTile(targetPosition.min + new Vector3Int(4, 2, 0)));
            Assert.AreEqual(m_TileA, m_Tilemap.GetTile(targetPosition.min + new Vector3Int(5, 2, 0)));
            Assert.AreEqual(m_TileB, m_Tilemap.GetTile(targetPosition.min + new Vector3Int(3, 3, 0)));
            Assert.AreEqual(null, m_Tilemap.GetTile(targetPosition.min + new Vector3Int(4, 3, 0)));
            Assert.AreEqual(m_TileA, m_Tilemap.GetTile(targetPosition.min + new Vector3Int(5, 3, 0)));
        }

        [Test]
        public void GridBrush_BoxEraseOnTilemap_GridBrushBoxErasesTilesOnTilemap()
        {
            var gridBrush = CreateScriptableObject<GridBrush>();
            gridBrush.Init(new Vector3Int(3, 5, 1));
            var targetPosition = new BoundsInt(5, 5, 0, 3, 3, 1);
            m_Tilemap.SetTile(targetPosition.min + new Vector3Int(0, 0, 0), m_TileA);
            m_Tilemap.SetTile(targetPosition.min + new Vector3Int(1, 1, 0), m_TileA);
            m_Tilemap.SetTile(targetPosition.min + new Vector3Int(2, 2, 0), m_TileA);

            Selection.activeGameObject = m_TilemapGameObject;
            gridBrush.BoxErase(m_Grid, Selection.activeGameObject, targetPosition);

            Assert.AreEqual(null, m_Tilemap.GetTile(targetPosition.min + new Vector3Int(0, 0, 0)));
            Assert.AreEqual(null, m_Tilemap.GetTile(targetPosition.min + new Vector3Int(1, 1, 0)));
            Assert.AreEqual(null, m_Tilemap.GetTile(targetPosition.min + new Vector3Int(2, 2, 0)));
        }

        [Test]
        public void GridBrush_FloodFillOnTilemap_GridBrushFloodFillsTilesOnTilemap()
        {
            var gridBrush = CreateScriptableObject<GridBrush>();
            gridBrush.Init(new Vector3Int(1, 1, 1));

            Selection.activeGameObject = m_TilemapGameObject;
            gridBrush.SetTile(new Vector3Int(0, 0, 0), m_TileA);

            var targetPosition = Vector3Int.zero;
            gridBrush.FloodFill(m_Grid, Selection.activeGameObject, targetPosition);

            for (int y = m_Tilemap.origin.y; y < m_Tilemap.origin.y + m_Tilemap.size.y; ++y)
            {
                for (int x = m_Tilemap.origin.x; x < m_Tilemap.origin.x + m_Tilemap.size.x; ++x)
                {
                    Assert.AreEqual(m_TileA, m_Tilemap.GetTile(new Vector3Int(x, y, 0)));
                }
            }
        }

        [Test]
        public void GridBrush_FloodFillOnTilemap_TriggersSyncTileOncePerCall()
        {
            int count = 0;
            var gridBrush = CreateScriptableObject<GridBrush>();
            gridBrush.Init(new Vector3Int(1, 1, 1));

            gridBrush.SetTile(new Vector3Int(0, 0, 0), m_TileA);

            System.Action<Tilemap, Tilemap.SyncTile[]> tilemapAction = (tilemap, syncTiles) =>
            {
                count++;
                Assert.AreEqual(tilemap, m_Tilemap);
                Assert.AreEqual(tilemap.size.x * tilemap.size.y, syncTiles.Length);
            };

            var targetPosition = Vector3Int.zero;
            Tilemap.SetSyncTileCallback(tilemapAction);
            gridBrush.FloodFill(m_Grid, Selection.activeGameObject, targetPosition);
            Assert.AreEqual(1, count);
            Tilemap.RemoveSyncTileCallback(tilemapAction);
        }

        [Test, Description("Case 1120310: Pick on an invalid (destroyed) GameObject Brush Target throws NullReferenceException")]
        public void GridBrush_PickOnInvalidGameObject_ChangesSizeWithoutThrowingException()
        {
            var gridBrush = CreateScriptableObject<GridBrush>();
            gridBrush.Init(new Vector3Int(1, 1, 1));

            var size = new Vector3Int(3, 2, 1);
            var targetPosition = new BoundsInt(Vector3Int.zero, size);
            gridBrush.Pick(m_Grid, null, targetPosition, Vector3Int.zero);

            Assert.AreEqual(size, gridBrush.size);
        }

        [Test]
        public void GridBrush_SelectAndMoveOnTilemap_GridBrushMovesTilesOnTilemap()
        {
            var gridBrush = CreateScriptableObject<GridBrush>();
            m_TilemapGameObject.transform.position = new Vector3(0.5f, 0.5f, 0f);

            var startPosition = new Vector3Int(0, 0, 0);
            var toPosition = new Vector3Int(0, 1, 0);
            var startBounds = new BoundsInt(startPosition, new Vector3Int(2, 1, 1));
            var toBounds = new BoundsInt(toPosition, new Vector3Int(2, 1, 1));

            m_Tilemap.SetTile(startPosition, m_TileA);
            m_Tilemap.SetTile(startPosition + Vector3Int.right, m_TileB);

            gridBrush.Select(m_Grid, Selection.activeGameObject, new BoundsInt(startPosition, new Vector3Int(2, 1, 1)));
            gridBrush.MoveStart(m_Grid, Selection.activeGameObject,
                new BoundsInt(startPosition, new Vector3Int(2, 1, 1)));
            gridBrush.Move(m_Grid, Selection.activeGameObject, startBounds, toBounds);
            gridBrush.MoveEnd(m_Grid, Selection.activeGameObject, new BoundsInt(toPosition, new Vector3Int(2, 1, 1)));

            Assert.AreEqual(null, m_Tilemap.GetTile(startPosition));
            Assert.AreEqual(null, m_Tilemap.GetTile(startPosition + Vector3Int.right));
            Assert.AreEqual(m_TileA, m_Tilemap.GetTile(toPosition));
            Assert.AreEqual(m_TileB, m_Tilemap.GetTile(toPosition + Vector3Int.right));
        }

        [Test, Description("case 1117888: Prefab Mode with Auto-Save triggers a GridPaintingState Flush which should clean up previews.")]
        public void GridBrush_PaintPreview_FlushGridPaintingState_CleansUpPaintPreview()
        {
            var gridBrush = GridPaintingState.gridBrush as GridBrush;

            Assert.NotNull(gridBrush, "Default Brush is not a GridBrush");
            gridBrush.Init(Vector3Int.one);
            gridBrush.cells[0].tile = m_TileA;

            GridPaintingState.scenePaintTarget = m_TilemapGameObject;
            TilemapEditorTool.SetActiveEditorTool(typeof(PaintTool));

            SceneViewMouseEnter();
            SceneViewMouseMove(new Vector2(100, 100));

            var bounds = new BoundsInt(m_Tilemap.editorPreviewOrigin, m_Tilemap.editorPreviewSize);
            var previewTile = m_Tilemap.editorPreviewOrigin;
            foreach (var position in bounds.allPositionsWithin)
            {
                if (m_Tilemap.HasEditorPreviewTile(position))
                {
                    previewTile = position;
                    break;
                }
            }
            Assert.AreEqual(m_TileA, m_Tilemap.GetEditorPreviewTile(previewTile));
            m_Tilemap.SetTile(previewTile + Vector3Int.right, m_TileB);

            GridPaintingState.FlushCache();

            Assert.AreNotEqual(m_TileA, m_Tilemap.GetEditorPreviewTile(previewTile));
            Assert.IsNull(m_Tilemap.GetEditorPreviewTile(previewTile));
        }

        [Test]
        public void GridBrush_PaintStartUpTileAndMove_StartsUpTileOnce()
        {
            var tile = ScriptableObject.CreateInstance<StartUpTile>();
            var gridBrush = GridPaintingState.gridBrush as GridBrush;

            Assert.NotNull(gridBrush, "Default Brush is not a GridBrush");
            gridBrush.Init(Vector3Int.one);
            gridBrush.cells[0].tile = tile;

            GridPaintingState.scenePaintTarget = m_TilemapGameObject;
            TilemapEditorTool.SetActiveEditorTool(typeof(PaintTool));

            SceneViewMouseEnter();
            SceneViewMouseMove(new Vector2(100, 100));
            SceneViewMouseClick(new Vector2(100, 100), false);
            SceneViewMouseMove(new Vector2(300, 300));

            Assert.AreEqual(1, StartUpTile.startUpCalls,
                "Start Up calls are not called only once when painting only on StartUp tile");
            StartUpTile.startUpCalls = 0; // Reset StartUpTile
        }

        [Test, Description("Case 1119051: When Grid Swizzle is set, use Grid Forward with Tilemap transform instead of Tile Orientation")]
        public void GridBrush_PickPaintCell_PicksCorrectCellForSwizzledGrid()
        {
            var gridBrush = GridPaintingState.gridBrush as GridBrush;

            Assert.NotNull(gridBrush, "Default Brush is not a GridBrush");
            gridBrush.Init(Vector3Int.one);
            gridBrush.cells[0].tile = m_TileA;

            m_Tilemap.layoutGrid.cellSwizzle = GridLayout.CellSwizzle.XZY;
            m_TilemapGameObject.transform.rotation = Quaternion.Euler(45, 30, 0);

            GridPaintingState.scenePaintTarget = m_TilemapGameObject;

            TilemapEditorTool.SetActiveEditorTool(typeof(PaintTool));

            SceneViewMouseEnter();
            SceneViewMouseClick(new Vector2(100, 100), false);

            var bounds = new BoundsInt(m_Tilemap.origin, m_Tilemap.size);
            var tilePosition = m_Tilemap.origin;

            foreach (var position in bounds.allPositionsWithin)
            {
                if (m_Tilemap.HasTile(position))
                {
                    tilePosition = position;
                    break;
                }
            }

            Assert.AreEqual(m_TileA, m_Tilemap.GetTile(tilePosition));
            Assert.AreNotEqual(0, tilePosition.y);
        }

        [Test]
        public void GridBrush_HasAGridBrushEditor()
        {
            var gridBrush = GridPaintingState.gridBrush as GridBrush;
            Assert.IsNotNull(gridBrush);
            var gridBrushEditor = Editor.CreateEditor(gridBrush) as GridBrushEditor;
            Assert.IsNotNull(gridBrushEditor);
        }

        [TestCase(true)]
        [TestCase(false)]
        [Test]
        public void GridBrushEditor_CanSetCanChangeZPosition(bool canChangeZPosition)
        {
            var gridBrush = GridPaintingState.gridBrush as GridBrush;
            var gridBrushEditor = Editor.CreateEditor(gridBrush) as GridBrushEditor;
            gridBrushEditor.canChangeZPosition = canChangeZPosition;
            Assert.AreEqual(canChangeZPosition, gridBrushEditor.canChangeZPosition);
        }

        [TestCase(true, 1)]
        [TestCase(false, 0)]
        [Test]
        public void GridBrush_IncreaseZPositionShortcut_IncreaseZPositionMethodCalled(bool canChangeZPosition, int zPosition)
        {
            var gridBrush = GridPaintingState.gridBrush as GridBrush;
            gridBrush.canChangeZPosition = canChangeZPosition;

            TilemapEditorTool.SetActiveEditorTool(typeof(PickingTool));
            SceneViewMouseEnter();
            // Default Keyboard shortcut is Minus
            KeyboardEvent(KeyCode.Minus);

            Assert.AreEqual(zPosition, GridPaintingState.lastActiveGrid.zPosition);
        }

        [TestCase(true, -1)]
        [TestCase(false, 0)]
        [Test]
        public void GridBrush_DecreaseZPositionShortcut_DecreaseZPositionMethodCalled(bool canChangeZPosition, int zPosition)
        {
            var gridBrush = GridPaintingState.gridBrush as GridBrush;
            gridBrush.canChangeZPosition = canChangeZPosition;

            TilemapEditorTool.SetActiveEditorTool(typeof(PickingTool));
            SceneViewMouseEnter();
            // Default Keyboard shortcut is Equals
            KeyboardEvent(KeyCode.Equals);

            Assert.AreEqual(zPosition, GridPaintingState.lastActiveGrid.zPosition);
        }

        [Test]
        public void GridBrushEditor_PicksAllValidTilemapsAsTargets()
        {
            var tilemapGameObjectB = CreateTilemapGameObject();

            var gridBrush = GridPaintingState.gridBrush as GridBrush;
            var gridBrushEditor = Editor.CreateEditor(gridBrush) as GridBrushEditor;

            var validTargets = gridBrushEditor.validTargets;
            Assert.AreEqual(2, validTargets.Length);
            Assert.IsTrue(validTargets.Contains(m_TilemapGameObject));
            Assert.IsTrue(validTargets.Contains(tilemapGameObjectB));
        }

        [Test]
        public void GridBrushEditor_PicksAllActiveTilemapsAsTargets()
        {
            var tilemapGameObjectB = CreateTilemapGameObject();
            tilemapGameObjectB.SetActive(false);

            var gridBrush = GridPaintingState.gridBrush as GridBrush;
            var gridBrushEditor = Editor.CreateEditor(gridBrush) as GridBrushEditor;

            var validTargets = gridBrushEditor.validTargets;
            Assert.AreEqual(1, validTargets.Length);
            Assert.IsTrue(validTargets.Contains(m_TilemapGameObject));
            Assert.IsFalse(validTargets.Contains(tilemapGameObjectB));
        }

        [Test]
        public void GridBrushEditor_PicksAllActiveInHierarchyTilemapsAsTargets()
        {
            var tilemapGameObjectB = CreateTilemapGameObject();

            var gameObjectC = CreateGameObject();
            tilemapGameObjectB.transform.parent = gameObjectC.transform;
            gameObjectC.SetActive(false);

            var gridBrush = GridPaintingState.gridBrush as GridBrush;
            var gridBrushEditor = Editor.CreateEditor(gridBrush) as GridBrushEditor;

            var validTargets = gridBrushEditor.validTargets;
            Assert.AreEqual(1, validTargets.Length);
            Assert.IsTrue(validTargets.Contains(m_TilemapGameObject));
            Assert.IsFalse(validTargets.Contains(tilemapGameObjectB));
            Assert.IsFalse(validTargets.Contains(gameObjectC));
        }

        [Test]
        public void CustomGridBrush_PaintOnTarget_CustomPaintMethodCalled()
        {
            var brush = SetupCustomBrush();

            TilemapEditorTool.SetActiveEditorTool(typeof(PaintTool));
            SceneViewMouseEnter();
            SceneViewMouseClick(new Vector2(100, 100), false);

            Assert.AreEqual(brush.m_LastCalledMethod, "Paint");
        }

        [UnityTest]
        public IEnumerator CustomGridBrush_GridSwizzledParallelToView_PaintOnTarget_CustomPaintMethodNotCalled()
        {
            m_Grid.cellSwizzle = GridLayout.CellSwizzle.YZX;
            yield return null;

            var brush = SetupCustomBrush();

            TilemapEditorTool.SetActiveEditorTool(typeof(PaintTool));
            SceneViewMouseEnter();
            SceneViewMouseClick(new Vector2(100, 100), false);

            Assert.AreEqual(brush.m_LastCalledMethod, "");
        }

        [UnityTest]
        public IEnumerator CustomGridBrush_GridTransformParallelToView_PaintOnTarget_CustomPaintMethodNotCalled()
        {
            m_Grid.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            yield return null;

            var brush = SetupCustomBrush();

            TilemapEditorTool.SetActiveEditorTool(typeof(PaintTool));
            SceneViewMouseEnter();
            SceneViewMouseClick(new Vector2(100, 100), false);

            Assert.AreEqual(brush.m_LastCalledMethod, "");
        }

        [Test]
        public void CustomGridBrush_EraseOnTarget_CustomEraseMethodCalled()
        {
            var brush = SetupCustomBrush();

            TilemapEditorTool.SetActiveEditorTool(typeof(EraseTool));
            SceneViewMouseEnter();
            SceneViewMouseClick(new Vector2(100, 100), false);

            Assert.AreEqual(brush.m_LastCalledMethod, "Erase");
        }

        [Test]
        public void CustomGridBrush_BoxFillOnTarget_CustomBoxFillMethodCalled()
        {
            var brush = SetupCustomBrush();

            TilemapEditorTool.SetActiveEditorTool(typeof(BoxTool));
            SceneViewMouseEnter();
            SceneViewMouseClick(new Vector2(100, 100), false);

            Assert.AreEqual(brush.m_LastCalledMethod, "BoxFill");
        }

        [Test]
        public void CustomGridBrush_BoxEraseOnTarget_CustomBoxEraseMethodCalled()
        {
            var brush = SetupCustomBrush();

            TilemapEditorTool.SetActiveEditorTool(typeof(BoxTool));
            SceneViewMouseEnter();
            SceneViewMouseClick(new Vector2(100, 100), true);

            Assert.AreEqual(brush.m_LastCalledMethod, "BoxErase");
        }

        [Test]
        public void CustomGridBrush_PickOnTarget_CustomPickMethodCalled()
        {
            var brush = SetupCustomBrush();

            TilemapEditorTool.SetActiveEditorTool(typeof(PickingTool));
            SceneViewMouseEnter();
            SceneViewMouseClick(new Vector2(100, 100), false);

            Assert.AreEqual(brush.m_LastCalledMethod, "Pick");
        }

        [Test]
        public void CustomGridBrush_SelectOnTarget_CustomSelectMethodCalled()
        {
            var brush = SetupCustomBrush();

            TilemapEditorTool.SetActiveEditorTool(typeof(SelectTool));
            SceneViewMouseEnter();
            SceneViewMouseClick(new Vector2(100, 100), false);

            Assert.AreEqual(brush.m_LastCalledMethod, "Select");
        }

        [Test]
        public void CustomGridBrush_IsSetAsLastUsedSessionBrush()
        {
            var defaultBrush = GridPaintingState.gridBrush;
            Assert.AreEqual(GridPaletteBrushes.brushes.IndexOf(defaultBrush), SessionState.GetInt(GridPaletteBrushes.s_SessionStateLastUsedBrush, -1));

            var brush = SetupCustomBrush();

            Assert.AreNotEqual(defaultBrush, brush);
            Assert.AreEqual(GridPaletteBrushes.brushes.IndexOf(GridPaintingState.gridBrush), SessionState.GetInt(GridPaletteBrushes.s_SessionStateLastUsedBrush, -1));
            Assert.AreEqual(brush, GridPaintingState.gridBrush);
        }

        [Test]
        public void CustomGridBrush_MoveOnTarget_CustomSelectAndMoveMethodsCalled()
        {
            var brush = SetupCustomBrush();

            TilemapEditorTool.SetActiveEditorTool(typeof(SelectTool));
            SceneViewMouseEnter();
            SceneViewMouseDrag(new Vector2(75, 75), new Vector2(175, 175));

            TilemapEditorTool.SetActiveEditorTool(typeof(MoveTool));
            brush.m_LastCalledMethod = "";
            brush.m_LastCalledEditorMethods.Clear();
            brush.m_LastCalledMethods.Clear();
            SceneViewMouseDrag(new Vector2(125, 125), new Vector2(200, 200));

            Assert.AreEqual(brush.m_LastCalledMethods[0], "MoveStart");
            for (int i = 1; i < brush.m_LastCalledEditorMethods.Count - 1; i++)
                Assert.AreEqual(brush.m_LastCalledMethods[i], "Move");
            Assert.AreEqual(brush.m_LastCalledMethods[brush.m_LastCalledMethods.Count - 1], "MoveEnd");
        }

        [Test]
        public void CustomGridBrush_FloodFillOnTarget_CustomFloodFillMethodCalled()
        {
            var brush = SetupCustomBrush();

            TilemapEditorTool.SetActiveEditorTool(typeof(FillTool));
            SceneViewMouseEnter();
            SceneViewMouseClick(new Vector2(100, 100), false);

            Assert.AreEqual(brush.m_LastCalledMethod, "FloodFill");
        }

        [Test]
        public void CustomGridBrush_IncreaseZPositionShortcut_IncreaseZPositionMethodCalled()
        {
            var brush = SetupCustomBrush();

            TilemapEditorTool.SetActiveEditorTool(typeof(PickingTool));
            SceneViewMouseEnter();
            // Default Keyboard shortcut is Minus
            KeyboardEvent(KeyCode.Minus);

            Assert.AreEqual(brush.m_LastCalledMethod, "ChangeZPosition 1");
        }

        [Test]
        public void CustomGridBrush_DecreaseZPositionShortcut_DecreaseZPositionMethodCalled()
        {
            var brush = SetupCustomBrush();

            TilemapEditorTool.SetActiveEditorTool(typeof(PickingTool));
            SceneViewMouseEnter();
            // Default Keyboard shortcut is Equals
            KeyboardEvent(KeyCode.Equals);

            Assert.AreEqual(brush.m_LastCalledMethod, "ChangeZPosition -1");
        }

        [Test]
        public void CustomGridBrush_ResetZPosition_ResetZPositionMethodCalled()
        {
            var brush = SetupCustomBrush();

            TilemapEditorTool.SetActiveEditorTool(typeof(PickingTool));
            SceneViewMouseEnter();
            s_PaletteWindow.ResetZPosition();

            Assert.AreEqual(brush.m_LastCalledMethod, "ResetZPosition");
        }

        [Test]
        public void CustomGridBrush_EditorOnToolActivatedAndDeactivated_CustomActivateAndDeactivateMethodsCalled()
        {
            var brush = SetupCustomBrush();
            TilemapEditorTool.SetActiveEditorTool(typeof(BoxTool));
            Assert.AreEqual("OnToolActivated_" + GridBrushBase.Tool.Box.ToString(),
                brush.m_LastCalledEditorMethods[brush.m_LastCalledEditorMethods.Count - 1]);
            TilemapEditorTool.SetActiveEditorTool(typeof(EraseTool));
            Assert.AreEqual("OnToolDeactivated_" + GridBrushBase.Tool.Box.ToString(),
                brush.m_LastCalledEditorMethods[brush.m_LastCalledEditorMethods.Count - 2]);
            Assert.AreEqual("OnToolActivated_" + GridBrushBase.Tool.Erase.ToString(),
                brush.m_LastCalledEditorMethods[brush.m_LastCalledEditorMethods.Count - 1]);
            TilemapEditorTool.SetActiveEditorTool(typeof(FillTool));
            Assert.AreEqual("OnToolDeactivated_" + GridBrushBase.Tool.Erase.ToString(),
                brush.m_LastCalledEditorMethods[brush.m_LastCalledEditorMethods.Count - 2]);
            Assert.AreEqual("OnToolActivated_" + GridBrushBase.Tool.FloodFill.ToString(),
                brush.m_LastCalledEditorMethods[brush.m_LastCalledEditorMethods.Count - 1]);
            TilemapEditorTool.SetActiveEditorTool(typeof(PaintTool));
            Assert.AreEqual("OnToolDeactivated_" + GridBrushBase.Tool.FloodFill.ToString(),
                brush.m_LastCalledEditorMethods[brush.m_LastCalledEditorMethods.Count - 2]);
            Assert.AreEqual("OnToolActivated_" + GridBrushBase.Tool.Paint.ToString(),
                brush.m_LastCalledEditorMethods[brush.m_LastCalledEditorMethods.Count - 1]);
            TilemapEditorTool.SetActiveEditorTool(typeof(PickingTool));
            Assert.AreEqual("OnToolDeactivated_" + GridBrushBase.Tool.Paint.ToString(),
                brush.m_LastCalledEditorMethods[brush.m_LastCalledEditorMethods.Count - 2]);
            Assert.AreEqual("OnToolActivated_" + GridBrushBase.Tool.Pick.ToString(),
                brush.m_LastCalledEditorMethods[brush.m_LastCalledEditorMethods.Count - 1]);
            TilemapEditorTool.SetActiveEditorTool(typeof(SelectTool));
            Assert.AreEqual("OnToolDeactivated_" + GridBrushBase.Tool.Pick.ToString(),
                brush.m_LastCalledEditorMethods[brush.m_LastCalledEditorMethods.Count - 2]);
            Assert.AreEqual("OnToolActivated_" + GridBrushBase.Tool.Select.ToString(),
                brush.m_LastCalledEditorMethods[brush.m_LastCalledEditorMethods.Count - 1]);
            TilemapEditorTool.SetActiveEditorTool(typeof(MoveTool));
            Assert.AreEqual("OnToolDeactivated_" + GridBrushBase.Tool.Select.ToString(),
                brush.m_LastCalledEditorMethods[brush.m_LastCalledEditorMethods.Count - 2]);
            Assert.AreEqual("OnToolActivated_" + GridBrushBase.Tool.Move.ToString(),
                brush.m_LastCalledEditorMethods[brush.m_LastCalledEditorMethods.Count - 1]);
        }

        [UnityTest]
        public IEnumerator CustomGridBrush_WithNoValidTargets_CreateNewObject_MaintainsActiveTarget()
        {
            Selection.activeGameObject = null;
            m_TilemapGameObject.name = MyBrushEditor.invalidName;

            SetupCustomBrush();
            TilemapEditorTool.SetActiveEditorTool(typeof(PaintTool));
            SceneViewMouseEnter();

            Selection.activeGameObject = m_TilemapGameObject;

            // Force OnSelectionChanged to trigger for Editor
            Selection.selectionChanged();

            Assert.IsTrue(GridPaintingState.validTargets == null || GridPaintingState.validTargets.Length == 0);
            Assert.AreEqual(m_TilemapGameObject, GridPaintingState.scenePaintTarget);

            CreateGameObject();

            // Wait for Editor to trigger OnHierarchyChanged
            yield return null;
            yield return null;

            Assert.IsTrue(GridPaintingState.validTargets == null || GridPaintingState.validTargets.Length == 0);
            Assert.AreEqual(m_TilemapGameObject, GridPaintingState.scenePaintTarget);
        }

        private void ResetSceneViewWindowPosition()
        {
            s_SceneView.position = new Rect(10f, 10f, 400f, 300f);
        }

        private void ResetPaletteWindowPosition()
        {
            s_PaletteWindow.position = new Rect(40f, 10f, 400f, 300f);
        }

        private void ResetSceneViewCamera()
        {
            s_SceneView.in2DMode = true;
            s_SceneView.pivot = new Vector3(0f, 0f, -10f);
            s_SceneView.rotation = Quaternion.identity;
            s_SceneView.size = 6.0f;
            s_SceneView.orthographic = true;
        }

        private EditorWindow SelectAndFocusSceneView()
        {
            EditorWindow window = SceneView.sceneViews[0] as EditorWindow;
            if (!window.hasFocus)
                window.Focus();
            return window;
        }

        private void KeyboardEvent(KeyCode keyCode)
        {
            var window = SelectAndFocusSceneView();
            var ev = new Event();
            ev.keyCode = keyCode;
            ev.type = EventType.KeyDown;
            window.SendEvent(ev);
            ev.type = EventType.KeyUp;
            window.SendEvent(ev);
        }

        private void SceneViewMouseClick(Vector2 position, bool shift)
        {
            var window = SelectAndFocusSceneView();
            var ev = new Event();
            ev.shift = shift;
            ev.mousePosition = position;
            ev.type = EventType.MouseDown;
            window.SendEvent(ev);
            ev.type = EventType.MouseUp;
            window.SendEvent(ev);
        }

        private void SceneViewMouseMove(Vector2 position)
        {
            var window = SelectAndFocusSceneView();
            var ev = new Event();
            ev.mousePosition = position;
            ev.type = EventType.MouseMove;
            window.SendEvent(ev);
        }

        private void SceneViewMouseDrag(Vector2 from, Vector2 to)
        {
            const int dragSteps = 5;
            var window = SelectAndFocusSceneView();
            var ev = new Event();
            ev.mousePosition = from;
            ev.type = EventType.MouseDown;
            window.SendEvent(ev);

            for (int i = 0; i < dragSteps; i++)
            {
                ev.mousePosition = Vector2.Lerp(from, to, (float)i / (float)dragSteps);
                ev.type = EventType.MouseDrag;
                window.SendEvent(ev);
            }

            ev.mousePosition = to;
            ev.type = EventType.MouseUp;
            window.SendEvent(ev);
        }

        private void SceneViewMouseEnter()
        {
            var window = SelectAndFocusSceneView();
            var ev = new Event();
            ev.type = EventType.MouseEnterWindow;
            window.SendEvent(ev);
        }
    }
}
