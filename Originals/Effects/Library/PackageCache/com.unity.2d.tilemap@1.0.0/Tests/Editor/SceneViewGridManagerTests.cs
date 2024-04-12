using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace UnityEditor.Tilemaps.Tests
{
    internal class SceneViewGridManagerTests
    {
        private static float m_Epsilon = 0.0001f;

        private class Vector3Close : IEqualityComparer<Vector3>
        {
            // Check for equality with Vector3 tolerance
            public bool Equals(Vector3 lhs, Vector3 rhs)
            {
                return Vector3.SqrMagnitude(lhs - rhs) < m_Epsilon * m_Epsilon;
            }

            public int GetHashCode(Vector3 v)
            {
                return v.GetHashCode();
            }
        }
        private static Vector3Close m_Vector3Close = new Vector3Close();

        private Grid m_Grid;
        private SceneViewGridManager m_SceneViewGridManager;

        [SetUp]
        public void SetUp()
        {
            var go = new GameObject();
            m_Grid = go.AddComponent<Grid>();
            m_SceneViewGridManager = SceneViewGridManager.instance;
            Selection.activeGameObject = go;
            Selection.selectionChanged();

            EditorSnapSettings.ResetSnapSettings();
        }

        [TearDown]
        public void TearDown()
        {
            Selection.activeGameObject = null;
            Selection.selectionChanged();

            if (m_Grid != null)
            {
                Object.DestroyImmediate(m_Grid.gameObject);
                m_Grid = null;
            }
            EditorSnapSettings.ResetSnapSettings();
        }

        [Test]
        public void SelectedGrid_IsCurrentActiveGridProxy()
        {
            Assert.AreNotEqual(null, m_SceneViewGridManager.activeGridProxy);
            Assert.AreEqual(m_Grid, m_SceneViewGridManager.activeGridProxy);
        }

        [Test]
        public void NoSelectedGrid_HasNoActiveGridProxy()
        {
            Selection.activeGameObject = null;
            Selection.selectionChanged();

            Assert.AreEqual(null, m_SceneViewGridManager.activeGridProxy);
            Assert.AreNotEqual(m_Grid, m_SceneViewGridManager.activeGridProxy);
        }

        public class SnapPositionTestCase
        {
            public bool snapEnabled;
            public Vector3 snapSettingsMove;
            public Vector3 gridCellSize;
            public Vector3 position;
            public Vector3 expectedPosition;

            public override String ToString()
            {
                return String.Format("{0}, {1}, {2}", snapEnabled, snapSettingsMove, gridCellSize);
            }
        }

        private static IEnumerable<SnapPositionTestCase> SpawnPositionTestCases()
        {
            yield return new SnapPositionTestCase
            {
                snapEnabled = true,
                snapSettingsMove = new Vector3(1.0f, 1.0f, 1.0f),
                gridCellSize = new Vector3(1.0f, 1.0f, 1.0f),
                position = new Vector3(0.6f, 0.7f, 0.8f),
                expectedPosition = new Vector3(1.0f, 1.0f, 1.0f),
            };

            yield return new SnapPositionTestCase
            {
                snapEnabled = true,
                snapSettingsMove = new Vector3(0.0f, 1.0f, 0.0f),
                gridCellSize = new Vector3(1.0f, 1.0f, 1.0f),
                position = new Vector3(0.6f, 0.7f, 0.8f),
                expectedPosition = new Vector3(1.0f, 1.0f, 1.0f),
            };

            yield return new SnapPositionTestCase
            {
                snapEnabled = true,
                snapSettingsMove = new Vector3(0.0f, 0.4f, 0.0f),
                gridCellSize = new Vector3(1.0f, 1.0f, 1.0f),
                position = new Vector3(0.6f, 0.7f, 0.8f),
                expectedPosition = new Vector3(1.0f, 0.8f, 1.0f),
            };

            yield return new SnapPositionTestCase
            {
                snapEnabled = true,
                snapSettingsMove = new Vector3(2.0f, 0.5f, 1.0f),
                gridCellSize = new Vector3(1.0f, 1.0f, 1.0f),
                position = new Vector3(0.6f, 0.7f, 0.8f),
                expectedPosition = new Vector3(0.0f, 0.5f, 1.0f),
            };

            yield return new SnapPositionTestCase
            {
                snapEnabled = true,
                snapSettingsMove = new Vector3(1.0f, 1.0f, 1.0f),
                gridCellSize = new Vector3(2.0f, 0.5f, 1.0f),
                position = new Vector3(0.6f, 0.7f, 0.8f),
                expectedPosition = new Vector3(0.0f, 0.5f, 1.0f),
            };

            yield return new SnapPositionTestCase
            {
                snapEnabled = true,
                snapSettingsMove = new Vector3(0.5f, 2.0f, 1.0f),
                gridCellSize = new Vector3(2.0f, 0.5f, 1.0f),
                position = new Vector3(0.6f, 0.7f, 0.8f),
                expectedPosition = new Vector3(1.0f, 1.0f, 1.0f),
            };

            yield return new SnapPositionTestCase
            {
                snapEnabled = false,
                snapSettingsMove = new Vector3(1.0f, 1.0f, 1.0f),
                gridCellSize = new Vector3(1.0f, 1.0f, 1.0f),
                position = new Vector3(0.6f, 0.7f, 0.8f),
                expectedPosition = new Vector3(0.6f, 0.7f, 0.8f),
            };
        }

        [Test]
        public void EditorSnapSettingsApplied_AppliesSnapToGrid([ValueSource("SpawnPositionTestCases")] SnapPositionTestCase testCase)
        {
            EditorTools.EditorTools.SetActiveTool<UnityEditor.MoveTool>();

            EditorSnapSettings.gridSnapEnabled = testCase.snapEnabled;
            EditorSnapSettings.move = testCase.snapSettingsMove;
            Tools.pivotRotation = PivotRotation.Global;

            m_Grid.cellSize = testCase.gridCellSize;

            var snappedPosition = m_SceneViewGridManager.OnSnapPosition(testCase.position);
            Assert.AreEqual(testCase.expectedPosition, snappedPosition);
            Assert.That(snappedPosition, Is.EqualTo(testCase.expectedPosition).Using(m_Vector3Close));
        }
    }
}
