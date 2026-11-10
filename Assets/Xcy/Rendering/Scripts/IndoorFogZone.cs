using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Xcy.Rendering
{
    /// <summary>Air inside this oriented box is excluded from outdoor fog.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class IndoorFogZone : MonoBehaviour
    {
        public Vector3 center;
        public Vector3 size = new Vector3(8, 4, 8);
        public const int MaxZones = 16;
        private static readonly List<IndoorFogZone> Zones = new List<IndoorFogZone>();
        private static readonly Matrix4x4[] Matrices = new Matrix4x4[MaxZones];
        private static readonly Vector4[] Extents = new Vector4[MaxZones];
        private static readonly List<IndoorFogZone> VisibleZones = new List<IndoorFogZone>();
        private static readonly int CountId = Shader.PropertyToID("_XcyIndoorCount");
        private static readonly int MatrixId = Shader.PropertyToID("_XcyIndoorWorldToLocal");
        private static readonly int ExtentId = Shader.PropertyToID("_XcyIndoorExtents");

        private void OnEnable()
        {
            if (Zones.Count == 0) RenderPipelineManager.beginCameraRendering += Upload;
            if (!Zones.Contains(this)) Zones.Add(this);
        }
        private void OnDisable()
        {
            Zones.Remove(this);
            if (Zones.Count != 0) return;
            RenderPipelineManager.beginCameraRendering -= Upload;
            Shader.SetGlobalInteger(CountId, 0);
        }
        private void OnValidate()
        {
            size = new Vector3(Mathf.Max(.01f, size.x), Mathf.Max(.01f, size.y), Mathf.Max(.01f, size.z));
        }
        private static void Upload(ScriptableRenderContext context, Camera camera)
        {
            // Additive scenes also share air; include active zones regardless of the camera's scene.
            VisibleZones.Clear();
            foreach (var zone in Zones)
                if (zone != null && zone.isActiveAndEnabled && zone.gameObject.scene.isLoaded) VisibleZones.Add(zone);
            VisibleZones.Sort((a, b) =>
            {
                float da = (a.transform.TransformPoint(a.center) - camera.transform.position).sqrMagnitude;
                float db = (b.transform.TransformPoint(b.center) - camera.transform.position).sqrMagnitude;
                int result = da.CompareTo(db);
                return result != 0 ? result : a.GetInstanceID().CompareTo(b.GetInstanceID());
            });
            int count = Mathf.Min(MaxZones, VisibleZones.Count);
            for (int i = 0; i < count; i++)
            {
                var z = VisibleZones[i];
                Matrices[i] = Matrix4x4.Translate(-z.center) * z.transform.worldToLocalMatrix;
                Extents[i] = new Vector4(z.size.x * .5f, z.size.y * .5f, z.size.z * .5f, 0);
            }
            Shader.SetGlobalMatrixArray(MatrixId, Matrices);
            Shader.SetGlobalVectorArray(ExtentId, Extents);
            Shader.SetGlobalInteger(CountId, count);
        }
        private void OnDrawGizmos()
        {
            var previous = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(.2f, .8f, .9f, .75f);
            Gizmos.DrawWireCube(center, size);
            Gizmos.matrix = previous;
        }
    }
}



