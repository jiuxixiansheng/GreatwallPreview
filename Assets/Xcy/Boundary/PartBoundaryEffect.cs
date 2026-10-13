using UnityEngine;
using UnityEngine.UI;

namespace Xcy.Boundary
{
    [DisallowMultipleComponent]
    public sealed class PartBoundaryEffect : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Camera playerCamera;
        [SerializeField] private PartBoundaryZone[] zones;
        [SerializeField] private Material blackoutMaterial;
        [SerializeField] private Material guideMaterial;
        [SerializeField] private Material proximityWarningMaterial;
        [SerializeField] private Font promptFont;

        [Header("Blackout")]
        [SerializeField, Min(0f)] private float fadeOutSeconds = 0.6f;
        [SerializeField, Min(0f)] private float fadeInSeconds = 0.4f;
        [SerializeField, Range(0f, 1f)] private float maximumBlackOpacity = 1f;
        [SerializeField, Min(0f), Tooltip("返回时需要比边界线深入的距离，避免边缘反复闪烁。")]
        private float reentryInset = 0.3f;

        [Header("Return guide")]
        [SerializeField] private Color guideColor = new Color(1f, 0.95f, 0.65f, 1f);
        [SerializeField, Range(0f, 1f)] private float guideOpacity = 0.48f;
        [SerializeField, Range(0.001f, 0.5f)] private float guideEdgeSoftness = 0.08f;
        [SerializeField, Min(0f)] private float pulseSpeed = 1.5f;
        [SerializeField, Range(0f, 1f)] private float pulseStrength = 0.12f;
        [SerializeField] private string returnMessage = "请返回黄色区域";

        [Header("Approaching boundary")]
        [SerializeField, Min(0.01f), Tooltip("距离侧面边界多近时开始显示红色线框（米）。")]
        private float warningDistance = 1.5f;
        [SerializeField] private Color warningColor = new Color(1f, 0.12f, 0.1f, 1f);
        [SerializeField, Range(0f, 1f)] private float warningMaximumOpacity = 0.8f;
        [SerializeField, Min(0.1f), Tooltip("玩家附近线框区域的半径（米）。")]
        private float warningPatchRadius = 1.75f;
        [SerializeField, Min(0.01f), Tooltip("线框区域边缘消散的宽度（米）。")]
        private float warningPatchFeather = 0.65f;
        [SerializeField, Min(0.05f)] private float warningGridSpacing = 0.45f;
        [SerializeField, Range(0.002f, 0.2f)] private float warningLineWidth = 0.025f;

        private Material runtimeBlackoutMaterial;
        private Font runtimePromptFont;
        private MeshRenderer blackoutRenderer;
        private Canvas promptCanvas;
        private Text promptText;
        private RectTransform arrowRoot;
        private RawImage[] arrowPieces;
        private PartBoundaryZone activeZone;
        private bool outside;
        private float fade;

        private void Awake()
        {
            if (playerCamera == null) playerCamera = Camera.main;
            if (playerCamera == null || blackoutMaterial == null || guideMaterial == null || proximityWarningMaterial == null)
            {
                Debug.LogError("PartBoundaryEffect: 请在 Inspector 中绑定玩家相机和三种材质。", this);
                enabled = false;
                return;
            }

            runtimeBlackoutMaterial = new Material(blackoutMaterial);
            CreateBlackoutSphere();
            CreatePrompt();
            SetVisuals(0f);
        }

        private void Update()
        {
            PartBoundaryZone nextZone = null;
            if (zones != null)
            {
                foreach (PartBoundaryZone zone in zones)
                {
                    if (zone != null && zone.IsAvailable)
                    {
                        nextZone = zone;
                        break;
                    }
                }
            }

            if (nextZone != activeZone)
            {
                activeZone = nextZone;
                outside = activeZone != null && !activeZone.Contains(playerCamera.transform.position);
            }
            else if (activeZone != null)
            {
                float inset = outside ? reentryInset : 0f;
                outside = !activeZone.Contains(playerCamera.transform.position, inset);
            }
            else
            {
                outside = false;
            }

            float target = outside ? 1f : 0f;
            float seconds = outside ? fadeOutSeconds : fadeInSeconds;
            fade = seconds <= 0f ? target : Mathf.MoveTowards(fade, target, Time.unscaledDeltaTime / seconds);
            SetVisuals(fade);
        }

        private void SetVisuals(float amount)
        {
            bool visible = amount > 0.001f;
            if (blackoutRenderer != null) blackoutRenderer.enabled = visible;
            if (runtimeBlackoutMaterial != null)
                runtimeBlackoutMaterial.SetFloat("_Opacity", amount * maximumBlackOpacity);

            float pulse = 1f - pulseStrength * 0.5f +
                          pulseStrength * 0.5f * Mathf.Sin(Time.unscaledTime * pulseSpeed * Mathf.PI * 2f);
            Vector3 warningCenter = Vector3.zero;
            float warningOpacity = 0f;
            if (activeZone != null && !outside)
            {
                float distance = activeZone.DistanceToNearestSide(playerCamera.transform.position, out warningCenter);
                float closeness = Mathf.Clamp01(1f - distance / Mathf.Max(0.01f, warningDistance));
                warningOpacity = Mathf.SmoothStep(0f, 1f, closeness) * warningMaximumOpacity;
            }
            if (zones != null)
            {
                foreach (PartBoundaryZone zone in zones)
                {
                    if (zone == null) continue;
                    zone.SetGuide(guideMaterial, guideColor,
                        zone == activeZone ? amount * guideOpacity * pulse : 0f,
                        guideEdgeSoftness);
                    zone.SetProximityWarning(proximityWarningMaterial, warningColor,
                        zone == activeZone ? warningOpacity : 0f,
                        warningCenter, warningPatchRadius, warningPatchFeather,
                        warningGridSpacing, warningLineWidth);
                }
            }

            if (promptCanvas != null) promptCanvas.enabled = visible && activeZone != null;
            if (promptText != null)
            {
                promptText.text = returnMessage;
                Color color = guideColor;
                color.a = amount;
                promptText.color = color;
                if (arrowPieces != null)
                {
                    foreach (RawImage piece in arrowPieces) piece.color = color;
                }
            }

            if (arrowRoot != null && activeZone != null)
            {
                Vector3 forward = playerCamera.transform.forward;
                Vector3 toward = activeZone.CenterWorld - playerCamera.transform.position;
                forward.y = 0f;
                toward.y = 0f;
                if (forward.sqrMagnitude > 0.001f && toward.sqrMagnitude > 0.001f)
                {
                    float angle = Vector3.SignedAngle(forward, toward, Vector3.up);
                    arrowRoot.localRotation = Quaternion.Euler(0f, 0f, -angle);
                }
            }
        }

        private void CreateBlackoutSphere()
        {
            GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Boundary Blackout (runtime)";
            sphere.transform.SetParent(playerCamera.transform, false);
            sphere.transform.localPosition = Vector3.zero;
            sphere.transform.localRotation = Quaternion.identity;
            sphere.transform.localScale = Vector3.one * 0.6f;
            sphere.hideFlags = HideFlags.DontSave;
            Collider collider = sphere.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            blackoutRenderer = sphere.GetComponent<MeshRenderer>();
            blackoutRenderer.sharedMaterial = runtimeBlackoutMaterial;
            blackoutRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            blackoutRenderer.receiveShadows = false;
        }

        private void CreatePrompt()
        {
            GameObject canvasObject = new GameObject("Boundary Prompt (runtime)", typeof(RectTransform), typeof(Canvas));
            canvasObject.transform.SetParent(playerCamera.transform, false);
            canvasObject.transform.localPosition = new Vector3(0f, 0.08f, 1.2f);
            canvasObject.transform.localRotation = Quaternion.identity;
            canvasObject.transform.localScale = Vector3.one * 0.0015f;
            canvasObject.hideFlags = HideFlags.DontSave;
            promptCanvas = canvasObject.GetComponent<Canvas>();
            promptCanvas.renderMode = RenderMode.WorldSpace;
            promptCanvas.worldCamera = playerCamera;
            promptCanvas.overrideSorting = true;
            promptCanvas.sortingOrder = 300;
            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(650f, 190f);

            GameObject textObject = new GameObject("Return Message", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textObject.transform.SetParent(canvasObject.transform, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.anchorMin = new Vector2(0f, 0.42f);
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            promptText = textObject.GetComponent<Text>();
            runtimePromptFont = promptFont == null
                ? Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Noto Sans CJK SC", "Arial" }, 72)
                : null;
            promptText.font = promptFont != null ? promptFont : runtimePromptFont;
            promptText.fontSize = 42;
            promptText.alignment = TextAnchor.MiddleCenter;
            promptText.horizontalOverflow = HorizontalWrapMode.Overflow;
            promptText.raycastTarget = false;
            promptText.text = returnMessage;

            GameObject arrowObject = new GameObject("Return Direction", typeof(RectTransform));
            arrowObject.transform.SetParent(canvasObject.transform, false);
            arrowRoot = arrowObject.GetComponent<RectTransform>();
            arrowRoot.anchorMin = arrowRoot.anchorMax = new Vector2(0.5f, 0.5f);
            arrowRoot.sizeDelta = new Vector2(80f, 80f);
            arrowRoot.anchoredPosition = new Vector2(0f, -55f);
            arrowPieces = new[]
            {
                CreateArrowPiece("Shaft", new Vector2(0f, -9f), new Vector2(10f, 45f), 0f),
                CreateArrowPiece("Left Head", new Vector2(-11f, 13f), new Vector2(10f, 32f), -45f),
                CreateArrowPiece("Right Head", new Vector2(11f, 13f), new Vector2(10f, 32f), 45f)
            };
        }

        private RawImage CreateArrowPiece(string name, Vector2 position, Vector2 size, float rotation)
        {
            GameObject pieceObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            pieceObject.transform.SetParent(arrowRoot, false);
            RectTransform rect = pieceObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localRotation = Quaternion.Euler(0f, 0f, rotation);
            RawImage image = pieceObject.GetComponent<RawImage>();
            image.texture = Texture2D.whiteTexture;
            image.raycastTarget = false;
            return image;
        }

        private void OnDestroy()
        {
            if (runtimeBlackoutMaterial != null) Destroy(runtimeBlackoutMaterial);
            if (runtimePromptFont != null) Destroy(runtimePromptFont);
        }
    }
}
