using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
public class BuildUpController : MonoBehaviour
{
    private static readonly int DistanceId = Shader.PropertyToID("_distance");

    [Header("组件引用")]
    [Tooltip("如果留空，脚本会自动抓取当前物体及其所有子物体身上的所有 Renderer")]
    public Renderer[] houseRenderers;

    [Header("Shader 参数")]
    [Range(0f, 1.01f)]
    public float distance = 0f;

    [Header("动画配置")]
    public float startDistance = 0f;
    public float endDistance = 1.01f;
    public float duration = 4f;

    [Header("阴影投射")]
    [SerializeField] private bool castShadows = true;

    private bool isPlaying = false;
    private float elapsedTime = 0f;
    private MaterialPropertyBlock propBlock;

    private void OnEnable()
    {
        Initialize();
        UpdateShaderProperties();
        ApplyShadowCasting();

        EventDispatcher.Instance.AddListener("修复小楼", AnimateAppearance);
        EventDispatcher.Instance.AddListener("切换阴影投射状态", ToggleShadowCasting);
    }

    private void OnDisable()
    {
        EventDispatcher.Instance.RemoveListener("修复小楼", AnimateAppearance);
        EventDispatcher.Instance.RemoveListener("切换阴影投射状态", ToggleShadowCasting);
    }

    private void OnValidate()
    {
        Initialize();
        UpdateShaderProperties();
        ApplyShadowCasting();
    }

    private void Initialize()
    {
        if (houseRenderers == null || houseRenderers.Length == 0)
        {
            houseRenderers = GetComponentsInChildren<Renderer>(true);
        }

        if (propBlock == null)
        {
            propBlock = new MaterialPropertyBlock();
        }
    }

    private void Start()
    {
        if (Application.isPlaying)
        {
            UpdateShaderProperties();
            ApplyShadowCasting();
        }
    }

    [ContextMenu("播放出现动画")]
    public void AnimateAppearance()
    {
        elapsedTime = 0f;
        distance = startDistance;
        isPlaying = true;

        UpdateShaderProperties();
    }

    [ContextMenu("切换阴影投射")]
    private void ToggleShadowCasting()
    {
        castShadows = !castShadows;
        ApplyShadowCasting();
    }

    public void SetDistance(float value)
    {
        distance = Mathf.Clamp(value, 0f, 1.01f);
        UpdateShaderProperties();
    }

    private void Update()
    {
        if (Application.isPlaying && isPlaying)
        {
            elapsedTime += Time.deltaTime;

            float t = duration > 0f ? elapsedTime / duration : 1f;
            t = Mathf.Clamp01(t);

            distance = Mathf.Lerp(startDistance, endDistance, t);

            if (t >= 1f)
            {
                distance = endDistance;
                isPlaying = false;
            }
        }

        UpdateShaderProperties();
    }

    private void UpdateShaderProperties()
    {
        if (propBlock == null)
        {
            Initialize();
        }

        if (houseRenderers == null || houseRenderers.Length == 0)
        {
            return;
        }

        foreach (Renderer r in houseRenderers)
        {
            if (r == null) continue;

            r.GetPropertyBlock(propBlock);
            propBlock.SetFloat(DistanceId, distance);
            r.SetPropertyBlock(propBlock);
        }
    }

    private void ApplyShadowCasting()
    {
        if (houseRenderers == null || houseRenderers.Length == 0)
        {
            Initialize();
        }

        foreach (Renderer r in houseRenderers)
        {
            if (r == null) continue;

            r.shadowCastingMode = castShadows
                ? ShadowCastingMode.On
                : ShadowCastingMode.Off;
        }
    }
}