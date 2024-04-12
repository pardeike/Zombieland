using System;
using NUnit.Framework;
using UnityEngine.Tilemaps;

namespace UnityEditor.Tilemaps.Tests
{
    internal class EditorToolTest
    {
        [TestCase(typeof(SelectTool))]
        [TestCase(typeof(MoveTool))]
        [TestCase(typeof(PaintTool))]
        [TestCase(typeof(BoxTool))]
        [TestCase(typeof(PickingTool))]
        [TestCase(typeof(EraseTool))]
        [TestCase(typeof(FillTool))]
        [Test]
        public void TilemapEditorTool_CanSetEditorTool(Type editorToolType)
        {
            TilemapEditorTool.SetActiveEditorTool(editorToolType);
            Assert.AreEqual(editorToolType, EditorTools.EditorTools.activeToolType);
        }

        [TestCase(typeof(RectTool))]
        [TestCase(typeof(ScaleTool))]
        [TestCase(typeof(UnityEditor.MoveTool))]
        [TestCase(typeof(TransformTool))]
        [TestCase(typeof(RotateTool))]
        [Test]
        public void TilemapEditorTool_CanNotSetUnityGlobalEditorTool(Type editorToolType)
        {
            TilemapEditorTool.SetActiveEditorTool(typeof(PaintTool));
            Assert.Throws(typeof(ArgumentException), () => { TilemapEditorTool.SetActiveEditorTool(editorToolType); },
                "The tool to set must be valid and derive from TilemapEditorTool");
        }

        [TestCase(typeof(SelectTool), typeof(FillTool))]
        [TestCase(typeof(MoveTool), typeof(FillTool))]
        [TestCase(typeof(PaintTool), typeof(FillTool))]
        [TestCase(typeof(BoxTool), typeof(FillTool))]
        [TestCase(typeof(PickingTool), typeof(FillTool))]
        [TestCase(typeof(EraseTool), typeof(FillTool))]
        [TestCase(typeof(FillTool), typeof(SelectTool))]
        [Test]
        public void TilemapEditorTool_IsSet_IsActive(Type editorToolType, Type otherEditorToolType)
        {
            TilemapEditorTool.SetActiveEditorTool(editorToolType);
            Assert.IsTrue(TilemapEditorTool.IsActive(editorToolType));
            Assert.IsFalse(TilemapEditorTool.IsActive(otherEditorToolType));
            Assert.IsFalse(TilemapEditorTool.IsActive(typeof(ViewModeTool)));
        }

        [TestCase(typeof(ViewModeTool), typeof(RectTool), typeof(SelectTool), typeof(RectTool))]
        [TestCase(typeof(RectTool), typeof(ViewModeTool), typeof(SelectTool), typeof(ViewModeTool))]
        [TestCase(typeof(RectTool), typeof(MoveTool), typeof(SelectTool), typeof(RectTool))]
        [TestCase(typeof(RectTool), typeof(SelectTool), typeof(SelectTool), typeof(SelectTool))]
        public void TilemapEditorTool_ToggleEditorTool_SwitchesToRightTool(Type initialToolType, Type startToolType, Type editorToolType, Type resultToolType)
        {
            EditorTools.EditorTools.SetActiveTool(initialToolType);
            EditorTools.EditorTools.SetActiveTool(startToolType);
            TilemapEditorTool.ToggleActiveEditorTool(editorToolType);
            TilemapEditorTool.ToggleActiveEditorTool(editorToolType);
            Assert.AreEqual(resultToolType, EditorTools.EditorTools.activeToolType);
        }
    }
}
