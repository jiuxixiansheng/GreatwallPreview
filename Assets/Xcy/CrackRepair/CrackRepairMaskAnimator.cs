using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Xcy.CrackRepair
{
    /// <summary>Updates one decal texture on the GPU from the original crack and its shape mask.</summary>
    [DisallowMultipleComponent]
    public sealed class CrackRepairMaskAnimator : MonoBehaviour
    {
        private static readonly int BaseMapId = Shader.PropertyToID("Base_Map");
        private static readonly int ShapeMaskId = Shader.PropertyToID("_ShapeMask");
        private static readonly int ProgressId = Shader.PropertyToID("_Progress");

        private DecalProjector projector;
        private Material originalDecalMaterial;
        private Material animatedDecalMaterial;
        private Material progressMaterial;
        private RenderTexture animatedTexture;
        private Texture originalTexture;
        private bool ready;

        public bool Initialize(DecalProjector target, Texture shapeMask)
        {
            projector = target;
            if (projector == null || shapeMask == null || projector.material == null) return false;

            Material template = Resources.Load<Material>("M_CrackRepairProgress");
            originalDecalMaterial = projector.material;
            originalTexture = originalDecalMaterial.GetTexture(BaseMapId);
            if (template == null || originalTexture == null)
            {
                Debug.LogError("Crack repair progress material or source texture is missing.", this);
                return false;
            }

            progressMaterial = new Material(template);
            progressMaterial.SetTexture(ShapeMaskId, shapeMask);
            animatedDecalMaterial = new Material(originalDecalMaterial);

            // The projector is less than a metre wide, so this resolution keeps
            // fine cracks while avoiding a full-size texture update each frame.
            int width = Mathf.Min(768, originalTexture.width);
            int height = Mathf.Max(1, Mathf.RoundToInt(
                width * (float)originalTexture.height / originalTexture.width));
            animatedTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name = name + " Repair Progress",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false
            };
            animatedTexture.Create();
            animatedDecalMaterial.SetTexture(BaseMapId, animatedTexture);
            projector.material = animatedDecalMaterial;
            ready = true;
            SetProgress(0f);
            return true;
        }

        public void SetProgress(float progress)
        {
            if (!ready) return;
            progressMaterial.SetFloat(ProgressId, Mathf.Clamp01(progress));
            Graphics.Blit(originalTexture, animatedTexture, progressMaterial);
        }

        private void OnDestroy()
        {
            if (projector != null && originalDecalMaterial != null)
                projector.material = originalDecalMaterial;
            if (animatedTexture != null)
            {
                animatedTexture.Release();
                Destroy(animatedTexture);
            }
            if (animatedDecalMaterial != null) Destroy(animatedDecalMaterial);
            if (progressMaterial != null) Destroy(progressMaterial);
        }
    }
}
