using System.Collections;
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

    private Renderer[] childRenderers;
    private MaterialPropertyBlock propertyBlock;
    private Coroutine disappearCoroutine;
    private int distanceId;

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