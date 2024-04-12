using System.Linq;
using UnityEngine;

namespace UnityEditor.Tilemaps.Tests
{
    [CustomEditor(typeof(MyBrush))]
    internal class MyBrushEditor : GridBrushEditorBase
    {
        public const string invalidName = "MyBrushInvalid";

        public override void OnToolActivated(GridBrushBase.Tool tool)
        {
            (target as MyBrush).m_LastCalledEditorMethods.Add("OnToolActivated_" + tool.ToString());
        }

        public override void OnToolDeactivated(GridBrushBase.Tool tool)
        {
            (target as MyBrush).m_LastCalledEditorMethods.Add("OnToolDeactivated_" + tool.ToString());
        }

        public override GameObject[] validTargets
        {
            get
            {
                return GameObject.FindObjectsOfType<Grid>().Where(x => x.gameObject.name != invalidName)
                    .Select(x => x.gameObject).ToArray();
            }
        }
    }
}
