using System.Collections;
using UnityEngine;
using UnityEngine.VFX;

public class DragonBehaviorV2 : MonoBehaviour
{
    private static readonly int DistanceId = Shader.PropertyToID("_distance");
    private const string RatePropertyName = "Rate";

    [Header("粒子效果")]
    [SerializeField] private VisualEffect transitionVfx;
    [SerializeField] private float maxRate = 800f;

    private Renderer[] renderers;
    private MaterialPropertyBlock propertyBlock;
    private Coroutine distanceCoroutine;

    private void Awake()
    {
        Initialize();
        SetDistance(0f);
        SetVfxRate(0f);
    }

    private void OnEnable()
    {
        Initialize();
        SetDistance(0f);
        SetVfxRate(0f);

        EventDispatcher.Instance.AddListener<float>("小龙特效转场", PlayDistance);
    }

    private void OnDisable()
    {
        EventDispatcher.Instance.RemoveListener<float>("小龙特效转场", PlayDistance);

        if (distanceCoroutine != null)
        {
            StopCoroutine(distanceCoroutine);
            distanceCoroutine = null;
        }

        SetDistance(0f);
        SetVfxRate(0f);
    }

    private void Initialize()
    {
        renderers = GetComponentsInChildren<Renderer>(true);

        if (transitionVfx == null)
        {
            transitionVfx = GetComponentInChildren<VisualEffect>(true);
        }

        if (propertyBlock == null)
        {
            propertyBlock = new MaterialPropertyBlock();
        }
    }

    private void PlayDistance(float duration)
    {
        Initialize();

        if (distanceCoroutine != null)
        {
            StopCoroutine(distanceCoroutine);
        }

        distanceCoroutine = StartCoroutine(AnimateDistance(duration));
    }

    private IEnumerator AnimateDistance(float duration)
    {
        SetDistance(0f);
        SetVfxRate(0f);

        if (duration <= 0f)
        {
            SetDistance(1.01f);
            SetVfxRate(0f);
            distanceCoroutine = null;
            yield break;
        }

        float elapsedTime = 0f;

        while (elapsedTime < duration)
        {
            elapsedTime += Time.deltaTime;

            float normalizedTime = Mathf.Clamp01(elapsedTime / duration);

            float distance = Mathf.Lerp(0f, 1.01f, normalizedTime);
            SetDistance(distance);

            float rate = CalculateVfxRate(normalizedTime);
            SetVfxRate(rate);

            yield return null;
        }

        SetDistance(1.01f);
        SetVfxRate(0f);

        distanceCoroutine = null;
    }

    private float CalculateVfxRate(float normalizedTime)
    {
        if (normalizedTime <= 0.25f)
        {
            float t = normalizedTime / 0.25f;
            return Mathf.Lerp(0f, maxRate, t);
        }

        if (normalizedTime >= 0.75f)
        {
            float t = (normalizedTime - 0.75f) / 0.25f;
            return Mathf.Lerp(maxRate, 0f, t);
        }

        return maxRate;
    }

    private void SetDistance(float value)
    {
        if (renderers == null || renderers.Length == 0)
        {
            Initialize();
        }

        foreach (Renderer r in renderers)
        {
            if (r == null) continue;

            r.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat(DistanceId, value);
            r.SetPropertyBlock(propertyBlock);
        }
    }

    private void SetVfxRate(float value)
    {
        if (transitionVfx == null) return;

        transitionVfx.SetFloat(RatePropertyName, value);
    }
}