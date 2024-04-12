using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEditor.Tilemaps.Tests
{
    [CreateAssetMenu]
    [CustomGridBrush]
    internal class MyBrush : GridBrushBase
    {
        public string m_LastCalledMethod;
        public List<string> m_LastCalledMethods;
        public List<string> m_LastCalledEditorMethods;

        public void OnEnable()
        {
            m_LastCalledEditorMethods = new List<string>();
            m_LastCalledMethods = new List<string>();
        }

        public override void Paint(GridLayout gridLayout, GameObject brushTarget, Vector3Int position)
        {
            m_LastCalledMethod = "Paint";
            m_LastCalledMethods.Add("Paint");
        }

        public override void Erase(GridLayout gridLayout, GameObject brushTarget, Vector3Int position)
        {
            m_LastCalledMethod = "Erase";
            m_LastCalledMethods.Add("Erase");
        }

        public override void BoxFill(GridLayout gridLayout, GameObject brushTarget, BoundsInt position)
        {
            m_LastCalledMethod = "BoxFill";
            m_LastCalledMethods.Add("BoxFill");
        }

        public override void BoxErase(GridLayout gridLayout, GameObject brushTarget, BoundsInt position)
        {
            m_LastCalledMethod = "BoxErase";
            m_LastCalledMethods.Add("BoxErase");
        }

        public override void Pick(GridLayout gridLayout, GameObject brushTarget, BoundsInt position, Vector3Int pivot)
        {
            m_LastCalledMethod = "Pick";
            m_LastCalledMethods.Add("Pick");
        }

        public override void Select(GridLayout gridLayout, GameObject brushTarget, BoundsInt position)
        {
            m_LastCalledMethod = "Select";
            m_LastCalledMethods.Add("Select");
        }

        public override void FloodFill(GridLayout gridLayout, GameObject brushTarget, Vector3Int position)
        {
            m_LastCalledMethod = "FloodFill";
            m_LastCalledMethods.Add("FloodFill");
        }

        public override void Move(GridLayout gridLayout, GameObject brushTarget, BoundsInt from, BoundsInt to)
        {
            m_LastCalledMethod = "Move";
            m_LastCalledMethods.Add("Move");
        }

        public override void MoveStart(GridLayout gridLayout, GameObject brushTarget, BoundsInt position)
        {
            m_LastCalledMethod = "MoveStart";
            m_LastCalledMethods.Add("MoveStart");
        }

        public override void MoveEnd(GridLayout gridLayout, GameObject brushTarget, BoundsInt position)
        {
            m_LastCalledMethod = "MoveEnd";
            m_LastCalledMethods.Add("MoveEnd");
        }

        public override void ChangeZPosition(int changeValue)
        {
            var changeMethod = String.Format("ChangeZPosition {0}", changeValue);
            m_LastCalledMethod = changeMethod;
            m_LastCalledMethods.Add(changeMethod);
        }

        public override void ResetZPosition()
        {
            m_LastCalledMethod = "ResetZPosition";
            m_LastCalledMethods.Add("ResetZPosition");
        }
    }
}
