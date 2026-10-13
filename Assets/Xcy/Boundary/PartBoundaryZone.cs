using UnityEngine;
using UnityEngine.Rendering;

namespace Xcy.Boundary
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class PartBoundaryZone : MonoBehaviour
    {
        [SerializeField, Tooltip("此边界对应的 Part 根物体。")]
        private GameObject sceneRoot;
        [SerializeField] private BoxCollider volume;
        [SerializeField, Tooltip("引导地面相对立方体底面的高度（米）。")]
        private float guideHeightOffset = 0.05f;

        private MeshRenderer guideRenderer;
        private MaterialPropertyBlock guideProperties;
        private MeshRenderer warningRenderer;
        private MaterialPropertyBlock warningProperties;

        public bool IsAvailable => isActiveAndEnabled && sceneRoot != null && sceneRoot.activeInHierarchy;
        public BoxCollider Volume => volume != null ? volume : GetComponent<BoxCollider>();
        public Vector3 CenterWorld => transform.TransformPoint(Volume.center);

        private void OnValidate()
        {
            volume = GetComponent<BoxCollider>();
            if (volume != null) volume.isTrigger = true;
        }

        public bool Contains(Vector3 worldPosition, float inset = 0f)
        {
            BoxCollider box = Volume;
            Vector3 point = transform.InverseTransformPoint(worldPosition) - box.center;
            Vector3 half = box.size * 0.5f;
            half.x = Mathf.Max(0f, half.x - inset);
            half.y = Mathf.Max(0f, half.y - inset);
            half.z = Mathf.Max(0f, half.z - inset);
            return Mathf.Abs(point.x) <= half.x &&
                   Mathf.Abs(point.y) <= half.y &&
                   Mathf.Abs(point.z) <= half.z;
        }

        public float DistanceToNearestSide(Vector3 worldPosition, out Vector3 nearestPoint)
        {
            BoxCollider box = Volume;
            Vector3 local = transform.InverseTransformPoint(worldPosition) - box.center;
            Vector3 half = box.size * 0.5f;
            float xDistance = (half.x - Mathf.Abs(local.x)) * Mathf.Abs(transform.lossyScale.x);
            float zDistance = (half.z - Mathf.Abs(local.z)) * Mathf.Abs(transform.lossyScale.z);

            if (xDistance <= zDistance)
                local.x = (local.x >= 0f ? 1f : -1f) * half.x;
            else
                local.z = (local.z >= 0f ? 1f : -1f) * half.z;

            local.y = Mathf.Clamp(local.y, -half.y, half.y);
            nearestPoint = transform.TransformPoint(box.center + local);
            return Mathf.Min(xDistance, zDistance);
        }

        public void SetProximityWarning(Material material, Color color, float opacity,
            Vector3 patchCenter, float patchRadius, float patchFeather,
            float gridSpacing, float lineWidth)
        {
            if (opacity <= 0.001f || !IsAvailable)
            {
                if (warningRenderer != null) warningRenderer.enabled = false;
                return;
            }

            if (warningRenderer == null) CreateWarning(material);
            if (warningRenderer == null) return;

            BoxCollider box = Volume;
            Transform warning = warningRenderer.transform;
            warning.localPosition = box.center;
            warning.localRotation = Quaternion.identity;
            warning.localScale = box.size;
            warningRenderer.enabled = true;

            if (warningProperties == null) warningProperties = new MaterialPropertyBlock();
            warningProperties.SetColor("_Tint", color);
            warningProperties.SetFloat("_Opacity", opacity);
            warningProperties.SetVector("_PatchCenterWS", patchCenter);
            warningProperties.SetFloat("_PatchRadius", patchRadius);
            warningProperties.SetFloat("_PatchFeather", patchFeather);
            warningProperties.SetFloat("_GridSpacing", gridSpacing);
            warningProperties.SetFloat("_LineWidth", lineWidth);
            warningProperties.SetVector("_BoxSize", box.size);
            warningRenderer.SetPropertyBlock(warningProperties);
        }

        public void SetGuide(Material material, Color color, float opacity, float edgeSoftness)
        {
            if (guideRenderer == null) CreateGuide(material);
            if (guideRenderer == null) return;

            bool visible = opacity > 0.001f && IsAvailable;
            guideRenderer.enabled = visible;
            if (!visible) return;

            BoxCollider box = Volume;
            Transform guide = guideRenderer.transform;
            guide.localPosition = box.center + Vector3.down * (box.size.y * 0.5f - guideHeightOffset);
            guide.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            guide.localScale = new Vector3(box.size.x, box.size.z, 1f);

            if (guideProperties == null) guideProperties = new MaterialPropertyBlock();
            guideProperties.SetColor("_Tint", color);
            guideProperties.SetFloat("_Opacity", opacity);
            guideProperties.SetFloat("_EdgeSoftness", edgeSoftness);
            guideRenderer.SetPropertyBlock(guideProperties);
        }

        private void CreateGuide(Material material)
        {
            if (material == null) return;
            GameObject guide = GameObject.CreatePrimitive(PrimitiveType.Quad);
            guide.name = "Boundary Guide (runtime)";
            guide.transform.SetParent(transform, false);
            guide.hideFlags = HideFlags.DontSave;
            Collider collider = guide.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            guideRenderer = guide.GetComponent<MeshRenderer>();
            guideRenderer.sharedMaterial = material;
            guideRenderer.shadowCastingMode = ShadowCastingMode.Off;
            guideRenderer.receiveShadows = false;
            guideRenderer.lightProbeUsage = LightProbeUsage.Off;
            guideRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private void CreateWarning(Material material)
        {
            if (material == null) return;
            GameObject warning = GameObject.CreatePrimitive(PrimitiveType.Cube);
            warning.name = "Boundary Proximity Warning (runtime)";
            warning.transform.SetParent(transform, false);
            warning.hideFlags = HideFlags.DontSave;
            Collider collider = warning.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            warningRenderer = warning.GetComponent<MeshRenderer>();
            warningRenderer.sharedMaterial = material;
            warningRenderer.shadowCastingMode = ShadowCastingMode.Off;
            warningRenderer.receiveShadows = false;
            warningRenderer.lightProbeUsage = LightProbeUsage.Off;
            warningRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private void OnDrawGizmosSelected()
        {
            BoxCollider box = Volume;
            if (box == null) return;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0.9f, 0.25f, 0.9f);
            Gizmos.DrawWireCube(box.center, box.size);
        }
    }
}
