using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

namespace UnityEditor.Tilemaps.Tests
{
    internal class GridPaintingStateTest
    {
        private readonly string m_PaletteAssetPath = "Assets";
        private readonly string m_PaletteName1 = "Palette1";
        private readonly string m_PaletteName2 = "Palette2";
        private readonly string m_CustomBrushPath = "Packages/com.unity.2d.tilemap/Tests/Editor/MyBrushInstance.asset";

        private GridPaintPaletteWindow m_Window;
        private GameObject m_TilemapGO1;
        private GameObject m_TilemapGO2;
        private GameObject m_PaletteGO1;
        private GameObject m_PaletteGO2;

        [SetUp]
        public void SetUp()
        {
            m_TilemapGO1 = CreateTilemapGameObjectWithName("1");
            m_TilemapGO2 = CreateTilemapGameObjectWithName("2");

            var paletteFullPath1 = GetPaletteFullPath(m_PaletteName1);
            if (!AssetDatabase.GetAllAssetPaths().Contains(paletteFullPath1))
            {
                m_PaletteGO1 = GridPaletteUtility.CreateNewPalette(m_PaletteAssetPath, m_PaletteName1,
                    GridLayout.CellLayout.Rectangle, GridPalette.CellSizing.Automatic, Vector3.one,
                    GridLayout.CellSwizzle.XYZ);
                m_PaletteGO2 = GridPaletteUtility.CreateNewPalette(m_PaletteAssetPath, m_PaletteName2,
                    GridLayout.CellLayout.Rectangle, GridPalette.CellSizing.Automatic, Vector3.one,
                    GridLayout.CellSwizzle.XYZ);
            }
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(m_TilemapGO1);
            Object.DestroyImmediate(m_TilemapGO2);

            var paletteFullPath1 = GetPaletteFullPath(m_PaletteName1);
            if (AssetDatabase.GetAllAssetPaths().Contains(paletteFullPath1))
            {
                AssetDatabase.DeleteAsset(paletteFullPath1);
            }
            var paletteFullPath2 = GetPaletteFullPath(m_PaletteName2);
            if (AssetDatabase.GetAllAssetPaths().Contains(paletteFullPath2))
            {
                AssetDatabase.DeleteAsset(paletteFullPath2);
            }

            GridPaintActiveTargetsPreferences.restoreEditModeSelection = true;

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
            m_Window = w;
            return w;
        }

        private string GetPaletteFullPath(string paletteName)
        {
            return m_PaletteAssetPath + "/" + paletteName + ".prefab";
        }

        [Test]
        public void DefaultTargetRestoreEditModeSelectionEditorPref_IsTrue()
        {
            Assert.IsTrue(GridPaintActiveTargetsPreferences.restoreEditModeSelection);
        }

        [Test]
        public void SetActiveTarget_CanGetActiveTarget()
        {
            CreatePaletteWindow();

            GridPaintingState.scenePaintTarget = m_TilemapGO1;
            Assert.AreEqual(m_TilemapGO1, GridPaintingState.scenePaintTarget);

            GridPaintingState.scenePaintTarget = m_TilemapGO2;
            Assert.AreEqual(m_TilemapGO2, GridPaintingState.scenePaintTarget);
        }

        private GridBrushBase GetDefaultBrush()
        {
            return GridPaintingState.brushes.First(
                brush => brush.name == "Default Brush" && brush.GetType() == typeof(GridBrush));
        }

        private GridBrushBase GetCustomBrush()
        {
            return AssetDatabase.LoadAssetAtPath<GridBrushBase>(m_CustomBrushPath);
        }

        [Test]
        public void GetDefaultBrush_IsNotNull()
        {
            Assert.IsNotNull(GetDefaultBrush());
        }

        [Test]
        public void GetCustomBrush_IsNotNull()
        {
            Assert.IsNotNull(GetCustomBrush());
        }

        [Test]
        public void SetActiveBrush_CanGetActiveBrush()
        {
            CreatePaletteWindow();

            GridPaintingState.gridBrush = GetDefaultBrush();
            Assert.AreEqual(GetDefaultBrush(), GridPaintingState.gridBrush);

            GridPaintingState.gridBrush = GetCustomBrush();
            Assert.AreEqual(GetCustomBrush(), GridPaintingState.gridBrush);
        }

        [Test]
        public void GetAllBrushes_ContainsDefaultBrush()
        {
            CreatePaletteWindow();

            Assert.IsNotNull(GridPaintingState.brushes.Contains(GetDefaultBrush()));
        }

        [Test]
        public void GetAllBrushes_ContainsCustomBrush()
        {
            CreatePaletteWindow();

            var customBrush = GetCustomBrush();
            Assert.IsTrue(GridPaintingState.brushes.Contains(customBrush));
        }

        [Test]
        public void SetActiveBrushCallback_CanGetActiveBrushFromCallback()
        {
            CreatePaletteWindow();

            GridPaintingState.gridBrush = GetDefaultBrush();
            var brushChangedCount = 0;
            GridBrushBase brushToChange = null;
            var brushChangedDelegate = new Action<GridBrushBase>(changedBrush =>
            {
                Assert.AreEqual(brushToChange, changedBrush);
                ++brushChangedCount;
            });

            GridPaintingState.brushChanged += brushChangedDelegate;
            try
            {
                var customBrush = GetCustomBrush();
                brushToChange = customBrush;
                GridPaintingState.gridBrush = customBrush;
                Assert.AreEqual(1, brushChangedCount);
                var defaultBrush = GetDefaultBrush();
                brushToChange = defaultBrush;
                GridPaintingState.gridBrush = defaultBrush;
                Assert.AreEqual(2, brushChangedCount);
            }
            finally
            {
                GridPaintingState.brushChanged -= brushChangedDelegate;
            }
        }

        [Test]
        public void SetActiveBrushCallback_DoesNotCallWhenSetWithSameBrush()
        {
            CreatePaletteWindow();

            GridPaintingState.gridBrush = GetDefaultBrush();
            var brushChangedCount = 0;
            GridBrushBase brushToChange = null;
            var brushChangedDelegate = new Action<GridBrushBase>(changedBrush =>
            {
                Assert.AreEqual(brushToChange, changedBrush);
                ++brushChangedCount;
            });

            GridPaintingState.brushChanged += brushChangedDelegate;
            try
            {
                var customBrush = GetCustomBrush();
                brushToChange = customBrush;
                GridPaintingState.gridBrush = customBrush;
                Assert.AreEqual(1, brushChangedCount);
                GridPaintingState.gridBrush = customBrush;
                Assert.AreEqual(1, brushChangedCount);
            }
            finally
            {
                GridPaintingState.brushChanged -= brushChangedDelegate;
            }
        }

        [Test]
        public void SetActivePalette_CanGetActivePalette()
        {
            CreatePaletteWindow();

            GridPaintingState.palette = m_PaletteGO1;
            Assert.AreEqual(m_PaletteGO1, GridPaintingState.palette);

            GridPaintingState.palette = m_PaletteGO2;
            Assert.AreEqual(m_PaletteGO2, GridPaintingState.palette);
        }

        [Test]
        public void SetActivePaletteCallback_CanGetActivePaletteFromCallback()
        {
            CreatePaletteWindow();

            GridPaintingState.palette = m_PaletteGO1;
            var paletteChangedCount = 0;
            GameObject paletteToChange = null;
            var paletteChangedDelegate = new Action<GameObject>(changedPalette =>
            {
                Assert.AreEqual(paletteToChange, changedPalette);
                ++paletteChangedCount;
            });

            GridPaintingState.paletteChanged += paletteChangedDelegate;
            try
            {
                paletteToChange = m_PaletteGO2;
                GridPaintingState.palette = m_PaletteGO2;
                paletteToChange = m_PaletteGO1;
                GridPaintingState.palette = m_PaletteGO1;
                Assert.AreEqual(2, paletteChangedCount);
            }
            finally
            {
                GridPaintingState.paletteChanged -= paletteChangedDelegate;
            }
        }

        [Test]
        public void SetActivePaletteCallback_DoesNotCallWhenSetWithSamePalette()
        {
            CreatePaletteWindow();

            GridPaintingState.palette = m_PaletteGO1;
            var paletteChangedCount = 0;
            GameObject paletteToChange = null;
            var paletteChangedDelegate = new Action<GameObject>(changedPalette =>
            {
                Assert.AreEqual(paletteToChange, changedPalette);
                ++paletteChangedCount;
            });

            GridPaintingState.paletteChanged += paletteChangedDelegate;
            try
            {
                paletteToChange = m_PaletteGO2;
                GridPaintingState.palette = m_PaletteGO2;
                Assert.AreEqual(1, paletteChangedCount);
                GridPaintingState.palette = m_PaletteGO2;
                Assert.AreEqual(1, paletteChangedCount);
            }
            finally
            {
                GridPaintingState.paletteChanged -= paletteChangedDelegate;
            }
        }

        [Test]
        public void GetPalettes_ContainsAllPalettes()
        {
            CreatePaletteWindow();

            Assert.AreEqual(2, GridPaintingState.palettes.Count);
            Assert.IsTrue(GridPaintingState.palettes.Contains(m_PaletteGO1));
            Assert.IsTrue(GridPaintingState.palettes.Contains(m_PaletteGO2));
            Assert.IsFalse(GridPaintingState.palettes.Contains(m_TilemapGO1));
            Assert.IsFalse(GridPaintingState.palettes.Contains(m_TilemapGO2));
        }

        [Test]
        public void SetInvalidActivePalette_ThrowsArgumentException()
        {
            Assert.Throws(typeof(ArgumentException), () => { GridPaintingState.palette = m_TilemapGO1; }, "Unable to set invalid Palette.");
        }

        [Test]
        public void SetNullActivePalette_ThrowsArgumentException()
        {
            Assert.Throws(typeof(ArgumentException), () => { GridPaintingState.palette = null; }, "Unable to set invalid Palette.");
        }

        public class TargetRestoreEditModeSelectionEditorPrefTestCase
        {
            public bool restoreEditModeSelection;
            public string result;

            public override String ToString()
            {
                return String.Format("{0}, {1}", restoreEditModeSelection, result);
            }
        }

        private static IEnumerable<TargetRestoreEditModeSelectionEditorPrefTestCase>
        TargetRestoreEditModeSelectionEditorPrefTestCaseTestCases()
        {
            yield return new TargetRestoreEditModeSelectionEditorPrefTestCase
            {restoreEditModeSelection = true, result = "1"};
            yield return new TargetRestoreEditModeSelectionEditorPrefTestCase
            {restoreEditModeSelection = false, result = "2"};
        }

        [UnityTest]
        public IEnumerator
        TargetRestoreEditModeSelectionEditorPref_SetPaintTargetOnPlayMode_HandlesPaintTargetInEditMode(
            [ValueSource("TargetRestoreEditModeSelectionEditorPrefTestCaseTestCases")]
            TargetRestoreEditModeSelectionEditorPrefTestCase testCase)
        {
            GridPaintActiveTargetsPreferences.restoreEditModeSelection =
                testCase.restoreEditModeSelection;

            CreatePaletteWindow();

            GridPaintingState.scenePaintTarget = m_TilemapGO1;
            Assert.AreEqual("1", GridPaintingState.scenePaintTarget.name);

            yield return new EnterPlayMode();

            m_TilemapGO2 = GameObject.Find("2");

            GridPaintingState.scenePaintTarget = m_TilemapGO2;
            Assert.AreEqual("2", GridPaintingState.scenePaintTarget.name);

            yield return new ExitPlayMode();

            m_TilemapGO1 = GameObject.Find("1");
            m_TilemapGO2 = GameObject.Find("2");

            Assert.AreEqual(testCase.result, GridPaintingState.scenePaintTarget.name);
        }
    }
}
