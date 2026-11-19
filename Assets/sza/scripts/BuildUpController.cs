using UnityEngine;
using UnityEngine.Rendering;
using System.Collections;
using System.Collections.Generic;

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

    [Header("出现完成后的渲染队列")]
    [Tooltip("出场完成后切换，重新播放时恢复原队列。只修改运行时材质副本。")]
    [SerializeField] private bool changeRenderQueueOnComplete;
    [Tooltip("2450 在玩家透明深度预通道之前绘制，避免半透明玩家挖空身后的小楼。")]
    [SerializeField, Range(-1, 5000)] private int renderQueueAfterAppearance = 2450;
    [SerializeField, Min(0f)] private float renderQueueChangeDelay;

    public bool IsRenderQueueApplied { get; private set; }
    private Coroutine queueChangeRoutine;
    private sealed class QueueMaterials
    {
        public Renderer renderer;
        public Material[] originals;
        public Material[] instances;
    }
    private readonly Dictionary<Renderer, QueueMaterials> queueMaterials =
        new Dictionary<Renderer, QueueMaterials>();

    private bool isPlaying = false;
    private float elapsedTime = 0f;
    private MaterialPropertyBlock propBlock;
    private EventDispatcher eventDispatcher;

    private void OnEnable()
    {
        Initialize();
        UpdateShaderProperties();
        ApplyShadowCasting();

        eventDispatcher = EventDispatcher.Instance;
        eventDispatcher.AddListener("修复小楼", AnimateAppearance);
        eventDispatcher.AddListener("切换阴影投射状态", ToggleShadowCasting);
    }

    private void OnDisable()
    {
        CancelQueueChange();
        SetRenderQueueApplied(false);
        // Do not recreate a dispatcher while its scene is being unloaded.
        if (eventDispatcher != null)
        {
            eventDispatcher.RemoveListener("修复小楼", AnimateAppearance);
            eventDispatcher.RemoveListener("切换阴影投射状态", ToggleShadowCasting);
        }
        eventDispatcher = null;
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
        CancelQueueChange();
        SetRenderQueueApplied(false);
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
                OnAppearanceCompleted();
            }
        }

        UpdateShaderProperties();
    }

    private void OnAppearanceCompleted()
    {
        if (!Application.isPlaying || NetworkGameplayCoordinator.IsFollower || !changeRenderQueueOnComplete) return;
        if (renderQueueChangeDelay <= 0f) SetRenderQueueApplied(true);
        else queueChangeRoutine = StartCoroutine(ChangeQueueAfterDelay());
    }

    private IEnumerator ChangeQueueAfterDelay()
    {
        yield return new WaitForSeconds(renderQueueChangeDelay);
        queueChangeRoutine = null;
        SetRenderQueueApplied(changeRenderQueueOnComplete);
    }

    private void CancelQueueChange()
    {
        if (queueChangeRoutine == null) return;
        StopCoroutine(queueChangeRoutine);
        queueChangeRoutine = null;
    }

    // The host publishes the completed state; late joiners do not replay the delay.
    public void ApplyNetworkRenderQueue(bool applied)
    {
        if (!Application.isPlaying) return;
        CancelQueueChange();
        SetRenderQueueApplied(changeRenderQueueOnComplete && applied);
    }

    private void SetRenderQueueApplied(bool applied)
    {
        if (IsRenderQueueApplied == applied) return;
        IsRenderQueueApplied = applied;
        if (!applied)
        {
            foreach (var entry in queueMaterials.Values)
                if (entry.renderer != null) entry.renderer.sharedMaterials = entry.originals;
            return;
        }

        Initialize();
        foreach (Renderer r in houseRenderers)
        {
            if (r == null) continue;
            if (!queueMaterials.TryGetValue(r, out var entry))
            {
                Material[] originals = r.sharedMaterials;
                var instances = new Material[originals.Length];
                for (int i = 0; i < originals.Length; i++)
                    if (originals[i] != null) instances[i] = new Material(originals[i]);
                entry = new QueueMaterials { renderer = r, originals = originals, instances = instances };
                queueMaterials.Add(r, entry);
            }
            foreach (Material material in entry.instances)
                if (material != null) material.renderQueue = renderQueueAfterAppearance;
            r.sharedMaterials = entry.instances;
        }
    }

    private void OnDestroy()
    {
        CancelQueueChange();
        SetRenderQueueApplied(false);
        foreach (var entry in queueMaterials.Values)
            foreach (Material material in entry.instances)
            {
                if (material == null) continue;
                if (Application.isPlaying) Destroy(material);
                else DestroyImmediate(material);
            }
        queueMaterials.Clear();
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
