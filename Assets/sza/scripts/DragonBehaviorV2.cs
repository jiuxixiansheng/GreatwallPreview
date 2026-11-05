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
    private bool timelineSampling;
    private bool sampledVfxPlaying;

    private void Awake()
    {
        Initialize();
        SetDistance(0f);
        SetVfxRate(0f);
    }

    private void OnEnable()
    {
        timelineSampling = false;
        sampledVfxPlaying = false;
        Initialize();
        if (transitionVfx != null) transitionVfx.pause = false;
        SetDistance(0f);
        SetVfxRate(0f);

        EventDispatcher.Instance.AddListener<float>("小龙特效转场", PlayDistance);
    }

    private void OnDisable()
    {
        timelineSampling = false;
        sampledVfxPlaying = false;
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

    // OpeningDragon 在运行时生成；仅替换目标网格，保留小龙的 Skinned Mesh Source。
    public void BindTransitionTarget(SkinnedMeshRenderer target)
    {
        Initialize();

        if (transitionVfx == null || target == null) return;

        if (!transitionVfx.HasSkinnedMeshRenderer("Skinned Mesh"))
        {
            Debug.LogWarning("DragonBehaviorV2: 转场 VFX 缺少 Skinned Mesh 属性。", this);
            return;
        }

        transitionVfx.SetSkinnedMeshRenderer("Skinned Mesh", target);
    }

    private void PlayDistance(float duration)
    {
        if (timelineSampling) return;
        Initialize();

        if (distanceCoroutine != null)
        {
            StopCoroutine(distanceCoroutine);
        }

        distanceCoroutine = StartCoroutine(AnimateDistance(duration));
    }

    // 直接按导演时间采样，暂停、客户端校时和晚加入都不会重新开始计时。
    public void SampleTransition(float elapsed, float duration, bool paused = false)
    {
        if (propertyBlock == null || renderers == null) Initialize();
        timelineSampling = true;
        if (distanceCoroutine != null)
        {
            StopCoroutine(distanceCoroutine);
            distanceCoroutine = null;
        }

        float progress = duration <= 0f ? 1f : Mathf.Clamp01(elapsed / duration);
        SetDistance(Mathf.Lerp(0f, 1.01f, progress));
        if (transitionVfx != null && progress < 1f && !sampledVfxPlaying)
        {
            transitionVfx.Play();
            sampledVfxPlaying = true;
        }
        if (transitionVfx != null) transitionVfx.pause = paused;
        SetVfxRate(CalculateVfxRate(progress));
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