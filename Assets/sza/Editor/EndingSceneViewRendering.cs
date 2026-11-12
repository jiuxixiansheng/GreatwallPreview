using System.Collections.Generic;
using Sza.Ending;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Sza.Editor
{
    // Scene View also uses the project's renderer unless explicitly overridden.
    [InitializeOnLoad]
    internal static class EndingSceneViewRendering
    {
        private static readonly Dictionary<UniversalAdditionalCameraData, int> previous = new Dictionary<UniversalAdditionalCameraData, int>();

        static EndingSceneViewRendering()
        {
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += Restore;
        }

        private static void Update()
        {
            var config = Resources.Load<EndingSequenceSettings>("EndingSequenceSettings");
            if (config == null || SceneManager.GetActiveScene().path != config.endingScene) { Restore(); return; }
            int index = config.PresentationRendererIndex();
            if (index < 0) return;
            foreach (SceneView view in SceneView.sceneViews)
            {
                if (view == null || view.camera == null) continue;
                var data = view.camera.GetComponent<UniversalAdditionalCameraData>();
                if (data == null) data = view.camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
                if (previous.ContainsKey(data)) continue;
                previous.Add(data, new SerializedObject(data).FindProperty("m_RendererIndex").intValue);
                data.SetRenderer(index); view.Repaint();
            }
        }

        private static void Restore()
        {
            foreach (var item in previous) if (item.Key != null) item.Key.SetRenderer(item.Value);
            previous.Clear();
        }
    }
}
