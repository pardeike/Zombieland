using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Remoting;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

namespace UnityEditor.Tilemaps.Tests
{
    internal class GridPaintPaletteWindowPreferencesTest
    {
        [SetUp]
        public void SetUp()
        {
            EditorPrefs.DeleteKey(GridPaintActiveTargetsPreferences.targetSortingModeEditorPref);
            EditorPrefs.DeleteKey(GridPaintActiveTargetsPreferences.createTileFromPaletteEditorPref);
        }

        [TearDown]
        public void TearDown()
        {
            EditorPrefs.DeleteKey(GridPaintActiveTargetsPreferences.targetSortingModeEditorPref);
            EditorPrefs.DeleteKey(GridPaintActiveTargetsPreferences.createTileFromPaletteEditorPref);
        }

        [Test]
        public void DefaultGridPaintSorting_IsNull()
        {
            Assert.IsNull(GridPaintActiveTargetsPreferences.GetTargetComparer());
        }

        [Test]
        public void DefaultCreateTileFromPalette_IsDefaultTile()
        {
            var method = typeof(TileUtility).GetMethod("DefaultTile", BindingFlags.Static | BindingFlags.Public);
            Assert.AreEqual(method, GridPaintActiveTargetsPreferences.GetCreateTileFromPaletteUsingPreferences());
        }

        public class CreateRedTile
        {
            [CreateTileFromPalette]
            public static TileBase RedTile(Sprite sprite)
            {
                var redTile = ScriptableObject.CreateInstance<Tile>();
                redTile.sprite = sprite;
                redTile.name = sprite.name;
                redTile.color = Color.red;
                return redTile;
            }
        }

        public class CreateGreenTile
        {
            public static TileBase GreenTile(Sprite sprite)
            {
                var greenTile = ScriptableObject.CreateInstance<Tile>();
                greenTile.sprite = sprite;
                greenTile.name = sprite.name;
                greenTile.color = Color.green;
                return greenTile;
            }

            [CreateTileFromPalette]
            public static TileBase BlackTile(Sprite sprite)
            {
                var blackTile = ScriptableObject.CreateInstance<Tile>();
                blackTile.sprite = sprite;
                blackTile.name = sprite.name;
                blackTile.color = Color.black;
                return blackTile;
            }
        }

        [GridPaintSorting]
        public class Numerical : IComparer<GameObject>
        {
            public int Compare(GameObject go1, GameObject go2)
            {
                return go2.GetInstanceID() - go1.GetInstanceID();
            }

            [GridPaintSorting]
            public static IComparer<GameObject> NumericalCompare()
            {
                return new Numerical();
            }
        }

        [TestCase("DefaultTile", true)]
        [TestCase("RedTile", true)]
        [TestCase("GreenTile", false)]
        [TestCase("WhiteTile", false)]
        [TestCase("BlackTile", true)]
        [Test]
        public void CreateTileFromPalette_CanGetAllMethodsWithAttribute(string methodName, bool result)
        {
            var methods = CreateTileFromPaletteAttribute.createTileFromPaletteMethods;
            var method = methods.Find((info => info.Name == methodName));
            Assert.AreEqual(result, method != null);
        }

        [TestCase("Alphabetical", true)]
        [TestCase("ReverseAlphabetical", true)]
        [TestCase("Magic", false)]
        [TestCase("ReverseNumerical", false)]
        [TestCase("Numerical", true)]
        [Test]
        public void GridPaintSorting_CanGetAllTypesWithAttribute(string typeName, bool result)
        {
            var types = GridPaintSortingAttribute.sortingTypes;
            var type = types.Find((info => info.Name == typeName));
            Assert.AreEqual(result, type != null);
        }

        [TestCase("Alphabetical", false)]
        [TestCase("ReverseAlphabetical", false)]
        [TestCase("Magic", false)]
        [TestCase("Numerical", false)]
        [TestCase("NumericalCompare", true)]
        [Test]
        public void GridPaintSorting_CanGetAllMethodsWithAttribute(string methodName, bool result)
        {
            var methods = GridPaintSortingAttribute.sortingMethods;
            var method = methods.Find((info => info.Name == methodName));
            Assert.AreEqual(result, method != null);
        }

        [TestCase(typeof(Numerical), "NumericalCompare")]
        [Test]
        public void SetGridPaintSortingMethod_CanGetComparer(Type type, string methodName)
        {
            EditorPrefs.SetString(GridPaintActiveTargetsPreferences.targetSortingModeEditorPref, CombineTypeAndMethodName(type, methodName));
            var comparer = GridPaintActiveTargetsPreferences.GetTargetComparer();
            Assert.NotNull(comparer);
            Assert.AreEqual(type, comparer.GetType());
        }

        [TestCase(typeof(Numerical))]
        [Test]
        public void SetGridPaintSortingType_CanGetComparer(Type type)
        {
            EditorPrefs.SetString(GridPaintActiveTargetsPreferences.targetSortingModeEditorPref, type.FullName);
            var comparer = GridPaintActiveTargetsPreferences.GetTargetComparer();
            Assert.NotNull(comparer);
            Assert.AreEqual(type, comparer.GetType());
        }

        [TestCase(typeof(CreateRedTile), "RedTile")]
        [TestCase(typeof(CreateGreenTile), "BlackTile")]
        [Test]
        public void SetCreateTileFromPalette_CanGetMethod(Type type, string methodName)
        {
            EditorPrefs.SetString(GridPaintActiveTargetsPreferences.createTileFromPaletteEditorPref, CombineTypeAndMethodName(type, methodName));
            var method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public);
            var paletteMethod = GridPaintActiveTargetsPreferences.GetCreateTileFromPaletteUsingPreferences();
            Assert.NotNull(paletteMethod);
            Assert.AreEqual(method, paletteMethod);
        }

        public static string CombineTypeAndMethodName(Type type, string methodName)
        {
            return String.Format("{0}.{1}", type.Name, methodName);
        }
    }
}
