using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ObjectDisappear : MonoBehaviour
{
    [Header("Shader 参数")]
    [Tooltip("Shader Graph 里 distance 属性的 Reference。通常是 _distance，如果不生效可以改成 distance。")]
    [SerializeField] private string distancePropertyName = "_distance";

    [Header("动画设置")]
    [SerializeField] private float startDistance = 0f;
    [SerializeField] private float endDistance = 1.01f;
    [SerializeField] private float duration = 2f;
    [SerializeField] private bool resetDistanceOnEnable = true;

    [Header("消失完成后的渲染队列")]
    [Tooltip("开启后，完全消失并等待指定秒数后修改本物体及子物体材质的渲染队列；重新播放或重置时恢复原队列。")]
    [SerializeField] private bool changeRenderQueueOnComplete;
    [Tooltip("消失完成后使用的队列。-1 使用 Shader 默认值；透明物体通常为 3000。")]
    [SerializeField, Range(-1, 5000)] private int renderQueueAfterDisappear = 3100;

    [Tooltip("消失动画结束后等待多少秒再修改队列。0 表示立即修改，受 Time.timeScale 影响。")]
    [SerializeField, Min(0f)] private float renderQueueChangeDelay = 0f;

    public bool IsRenderQueueApplied { get; private set; }

    private Coroutine renderQueueCoroutine;
    private Renderer[] childRenderers;
    private MaterialPropertyBlock propertyBlock;
    private Coroutine disappearCoroutine;
    private int distanceId;

    private sealed class RenderQueueMaterials
    {
        public Renderer renderer;
        public Material[] originals;
        public Material[] instances;
    }

    private readonly Dictionary<Renderer, RenderQueueMaterials> renderQueueMaterials =
        new Dictionary<Renderer, RenderQueueMaterials>();

    private void Awake()
    {
        Initialize();

        if (resetDistanceOnEnable)
        {
            SetDistance(startDistance);
        }
    }

    private void OnEnable()
    {
        Initialize();

        if (resetDistanceOnEnable)
        {
            SetDistance(startDistance);
        }

        EventDispatcher.Instance.AddListener("修复小楼", PlayDisappear);
    }

    private void OnDisable()
    {
        CancelPendingRenderQueueChange();
        EventDispatcher.Instance.RemoveListener("修复小楼", PlayDisappear);
    }

    private void Initialize()
    {
        childRenderers = GetComponentsInChildren<Renderer>(true);
        propertyBlock ??= new MaterialPropertyBlock();
        distanceId = Shader.PropertyToID(distancePropertyName);
    }

    [ContextMenu("播放消失动画")]
    public void PlayDisappear()
    {
        PlayDisappear(duration);
    }

    public void PlayDisappear(float animationDuration)
    {
        Initialize();

        if (disappearCoroutine != null)
        {
            StopCoroutine(disappearCoroutine);
        }

        disappearCoroutine = StartCoroutine(AnimateDistance(animationDuration));
    }

    public void SetDistance(float value)
    {
        if (childRenderers == null || childRenderers.Length == 0)
        {
            Initialize();
        }

        float clampedValue = Mathf.Clamp(value, startDistance, endDistance);

        foreach (Renderer r in childRenderers)
        {
            if (r == null) continue;

            r.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat(distanceId, clampedValue);
            r.SetPropertyBlock(propertyBlock);
        }

        ApplyRenderQueueForDistance(clampedValue);
    }

    // 只在完成状态首次到达时开始计时，重复设置终点或收到快照不会重启等待。
    public void ApplyRenderQueueForDistance(float value)
    {
        if (!Application.isPlaying || NetworkGameplayCoordinator.IsFollower) return;

        if (!changeRenderQueueOnComplete || value < endDistance)
        {
            CancelPendingRenderQueueChange();
            SetRenderQueueApplied(false);
            return;
        }

        if (IsRenderQueueApplied || renderQueueCoroutine != null) return;

        if (renderQueueChangeDelay <= 0f)
            SetRenderQueueApplied(true);
        else if (isActiveAndEnabled)
            renderQueueCoroutine = StartCoroutine(ChangeRenderQueueAfterDelay());
    }

    // 客户端直接恢复 Host 的最终状态，晚加入时不重新等待完整延迟。
    public void ApplyNetworkRenderQueue(bool applied)
    {
        if (!Application.isPlaying) return;

        CancelPendingRenderQueueChange();
        SetRenderQueueApplied(changeRenderQueueOnComplete && applied);
    }

    private IEnumerator ChangeRenderQueueAfterDelay()
    {
        yield return new WaitForSeconds(Mathf.Max(0f, renderQueueChangeDelay));
        renderQueueCoroutine = null;
        SetRenderQueueApplied(changeRenderQueueOnComplete);
    }

    private void CancelPendingRenderQueueChange()
    {
        if (renderQueueCoroutine == null) return;
        StopCoroutine(renderQueueCoroutine);
        renderQueueCoroutine = null;
    }

    private void SetRenderQueueApplied(bool applied)
    {
        IsRenderQueueApplied = applied;

        if (!applied)
        {
            foreach (RenderQueueMaterials entry in renderQueueMaterials.Values)
            {
                for (int i = 0; i < entry.instances.Length; i++)
                {
                    if (entry.instances[i] != null && entry.originals[i] != null)
                        entry.instances[i].renderQueue = entry.originals[i].renderQueue;
                }
            }
            return;
        }

        if (childRenderers == null) Initialize();
        int queue = Mathf.Clamp(renderQueueAfterDisappear, -1, 5000);

        foreach (Renderer r in childRenderers)
        {
            if (r == null) continue;

            if (!renderQueueMaterials.TryGetValue(r, out RenderQueueMaterials entry))
            {
                Material[] originals = r.sharedMaterials;
                Material[] instances = new Material[originals.Length];
                for (int i = 0; i < originals.Length; i++)
                {
                    if (originals[i] != null)
                        instances[i] = new Material(originals[i]);
                }

                entry = new RenderQueueMaterials { renderer = r, originals = originals, instances = instances };
                renderQueueMaterials.Add(r, entry);
                r.sharedMaterials = instances;
            }

            foreach (Material material in entry.instances)
                if (material != null) material.renderQueue = queue;
        }
    }

    private void OnDestroy()
    {
        CancelPendingRenderQueueChange();
        foreach (RenderQueueMaterials entry in renderQueueMaterials.Values)
        {
            if (entry.renderer != null) entry.renderer.sharedMaterials = entry.originals;
            foreach (Material material in entry.instances)
            {
                if (material == null) continue;
                if (Application.isPlaying) Destroy(material);
                else DestroyImmediate(material);
            }
        }
        renderQueueMaterials.Clear();
    }

    private IEnumerator AnimateDistance(float animationDuration)
    {
        SetDistance(startDistance);

        if (animationDuration <= 0f)
        {
            SetDistance(endDistance);
            disappearCoroutine = null;
            yield break;
        }

        float elapsedTime = 0f;

        while (elapsedTime < animationDuration)
        {
            elapsedTime += Time.deltaTime;

            float t = Mathf.Clamp01(elapsedTime / animationDuration);
            float distance = Mathf.Lerp(startDistance, endDistance, t);

            SetDistance(distance);

            yield return null;
        }

        SetDistance(endDistance);
        disappearCoroutine = null;
    }
}
