using System;
using NUnit.Framework;
using UnityEditor.Presets;
using UnityEngine;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

namespace UnityEditor.Tilemaps.Tests
{
    internal class TileUtilityTest
    {
        private const string kPresetAssetPath = "Assets/TilePresetTest.preset";

        private Sprite m_Sprite;
        private TileBase m_Tile;

        [SetUp]
        public void SetUp()
        {
            m_Sprite = Sprite.Create(new Rect(0f, 0f, 100f, 100f), Vector2.zero, 100f);

            if (AssetDatabase.LoadAllAssetsAtPath(kPresetAssetPath).Length > 0)
            {
                AssetDatabase.DeleteAsset(kPresetAssetPath);
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (m_Sprite != null)
            {
                Object.DestroyImmediate(m_Sprite);
                m_Sprite = null;
            }
            if (m_Tile != null)
            {
                Object.DestroyImmediate(m_Tile);
                m_Tile = null;
            }
            if (AssetDatabase.LoadAllAssetsAtPath(kPresetAssetPath).Length > 0)
            {
                AssetDatabase.DeleteAsset(kPresetAssetPath);
            }
        }

        [Test]
        public void CreateDefaultTile_IsATile()
        {
            m_Tile = TileUtility.CreateDefaultTile();
            Assert.AreEqual(typeof(Tile), m_Tile.GetType());
        }

        [Test]
        public void CreateDefaultTileWithSprite_HasDefaultTileProperties()
        {
            m_Tile = TileUtility.DefaultTile(m_Sprite);
            var tile = m_Tile as Tile;

            Assert.AreEqual(typeof(Tile), m_Tile.GetType());
            Assert.IsNotNull(tile);

            Assert.AreEqual(m_Sprite, tile.sprite);
            Assert.AreEqual(Color.white, tile.color);
        }

        [Test]
        public void CreatePreset_CreateDefaultTile_HasPresetTileProperties()
        {
            Tile presetTile = TileUtility.DefaultTile(m_Sprite) as Tile;
            Assert.IsNotNull(presetTile);

            presetTile.color = Color.red;

            var preset = new Preset(presetTile);
            var defaultPreset = new DefaultPreset(String.Empty, preset);
            var presetType = preset.GetPresetType();

            AssetDatabase.CreateAsset(preset, kPresetAssetPath);

            Preset.SetDefaultPresetsForType(presetType, new[] { defaultPreset });

            m_Tile = TileUtility.CreateDefaultTile();
            var tile = m_Tile as Tile;

            Assert.AreEqual(typeof(Tile), m_Tile.GetType());
            Assert.IsNotNull(tile);

            Assert.AreEqual(m_Sprite, tile.sprite);
            Assert.NotNull(tile.sprite);
            Assert.AreEqual(Color.red, tile.color);
            Assert.AreNotEqual(Color.white, tile.color);

            Object.DestroyImmediate(presetTile);
            Preset.SetDefaultPresetsForType(presetType, null);
        }
    }
}
