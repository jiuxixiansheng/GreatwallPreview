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
