using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace UnityEditor.Tilemaps.Tests
{
    [TestFixture]
    internal class GridEditorUtilityTests
    {
        [Test]
        public void ClampToGrid_PointInGridIsSame()
        {
            var point = new Vector3Int(1, 2, 3);
            var gridOrigin = Vector2Int.zero;
            var gridSize = Vector2Int.one * 4;

            var clampedPoint = GridEditorUtility.ClampToGrid(point, gridOrigin, gridSize);
            Assert.AreEqual(point, clampedPoint);
        }

        [Test]
        public void ClampToGrid_PointOutsideGridIsClamped()
        {
            var point = new Vector3Int(5, 2, 3);
            var gridOrigin = Vector2Int.zero;
            var gridSize = Vector2Int.one * 4;

            var clampedPoint = GridEditorUtility.ClampToGrid(point, gridOrigin, gridSize);
            Assert.AreEqual(new Vector3Int(3, 2, 3), clampedPoint);
        }

        [Test]
        public void ClampToGrid_NegativePointOutsideGridIsClamped()
        {
            var point = new Vector3Int(-1, 2, 3);
            var gridOrigin = Vector2Int.zero;
            var gridSize = Vector2Int.one * 4;

            var clampedPoint = GridEditorUtility.ClampToGrid(point, gridOrigin, gridSize);
            Assert.AreEqual(new Vector3Int(0, 2, 3), clampedPoint);
        }

        [Test]
        public void GetMarqueeRect_MinMaxPoints()
        {
            var minPoint = Vector2Int.one * -4;
            var maxPoint = Vector2Int.one * 4;

            var marquee = GridEditorUtility.GetMarqueeRect(minPoint, maxPoint);
            Assert.AreEqual(new RectInt(-4, -4, 9, 9), marquee);
        }

        [Test]
        public void GetMarqueeRect_AlternatePoints()
        {
            var xPoint = new Vector2Int(5, -4);
            var yPoint = new Vector2Int(-4, 5);

            var marquee = GridEditorUtility.GetMarqueeRect(xPoint, yPoint);
            Assert.AreEqual(new RectInt(-4, -4, 10, 10), marquee);
        }

        [Test]
        public void GetMarqueeRect_SamePoint()
        {
            var point = new Vector2Int(5, -4);

            var marquee = GridEditorUtility.GetMarqueeRect(point, point);
            Assert.AreEqual(new RectInt(5, -4, 1, 1), marquee);
        }

        [Test]
        public void GetMarqueeBounds_MinMaxPoints()
        {
            var minPoint = Vector3Int.one * -4;
            var maxPoint = Vector3Int.one * 4;

            var marquee = GridEditorUtility.GetMarqueeBounds(minPoint, maxPoint);
            Assert.AreEqual(new BoundsInt(-4, -4, -4, 9, 9, 9), marquee);
        }

        [Test]
        public void GetMarqueeBounds_AlternatePoints()
        {
            var xPoint = new Vector3Int(5, -4, 5);
            var yPoint = new Vector3Int(-4, 5, -4);

            var marquee = GridEditorUtility.GetMarqueeBounds(xPoint, yPoint);
            Assert.AreEqual(new BoundsInt(-4, -4, -4, 10, 10, 10), marquee);
        }

        [Test]
        public void GetMarqueeBounds_SamePoint()
        {
            var point = new Vector3Int(5, -4, 6);

            var marquee = GridEditorUtility.GetMarqueeBounds(point, point);
            Assert.AreEqual(new BoundsInt(5, -4, 6, 1, 1, 1), marquee);
        }

        [Test]
        public void GetPointsOnLine_HorizontalLine_PointCountCorrect()
        {
            var startPoint = Vector2Int.zero;
            var endPoint = new Vector2Int(5, 0);

            var count = GridEditorUtility.GetPointsOnLine(startPoint, endPoint).Count();
            Assert.AreEqual(endPoint.x - startPoint.x + 1, count);
        }

        [Test]
        public void GetPointsOnLine_VerticalLine_PointCountCorrect()
        {
            var startPoint = Vector2Int.zero;
            var endPoint = new Vector2Int(0, 5);

            var count = GridEditorUtility.GetPointsOnLine(startPoint, endPoint).Count();
            Assert.AreEqual(endPoint.y - startPoint.y + 1, count);
        }

        [Test]
        public void GetPointsOnLine_DiagonalLine_PointCountCorrect()
        {
            var startPoint = Vector2Int.zero;
            var endPoint = new Vector2Int(5, 5);

            var count = GridEditorUtility.GetPointsOnLine(startPoint, endPoint).Count();
            Assert.AreEqual(endPoint.y - startPoint.x + 1, count);
        }
    }
}
