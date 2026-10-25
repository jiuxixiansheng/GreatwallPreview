using UnityEditor;
using UnityEngine;

namespace Xcy.Rendering.Editor
{
    [CustomEditor(typeof(IndoorFogZone))]
    public sealed class IndoorFogZoneEditor : UnityEditor.Editor
    {
        private readonly UnityEditor.IMGUI.Controls.BoxBoundsHandle handle = new UnityEditor.IMGUI.Controls.BoxBoundsHandle();
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.HelpBox("Only air inside this box is fog-free. Fit the box to the room interior. Overlapping boxes are supported. Up to 16 nearest active boxes are used per camera.", MessageType.Info);
        }
        private void OnSceneGUI()
        {
            var zone = (IndoorFogZone)target;
            using (new Handles.DrawingScope(zone.transform.localToWorldMatrix))
            {
                handle.center = zone.center;
                handle.size = zone.size;
                EditorGUI.BeginChangeCheck();
                handle.DrawHandle();
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(zone, "Resize indoor fog zone");
                    zone.center = handle.center;
                    zone.size = handle.size;
                    EditorUtility.SetDirty(zone);
                }
            }
        }
        [MenuItem("GameObject/Xcy/Indoor Fog Zone", false, 10)]
        private static void CreateZone(MenuCommand command)
        {
            var go = new GameObject("Indoor Fog Zone");
            GameObjectUtility.SetParentAndAlign(go, command.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "Create indoor fog zone");
            go.AddComponent<IndoorFogZone>();
            Selection.activeGameObject = go;
        }
    }
}
