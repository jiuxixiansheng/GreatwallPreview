using System.Collections;
using UnityEngine;
using UnityEngine.VFX;

public class DragonBehavior : MonoBehaviour
{
    [Header("材质控制")]
    [SerializeField] private Renderer dragonRenderer;

    [Header("粒子控制")]
    [SerializeField] private VisualEffect dragonVFX;
    [SerializeField] private float maxParticleRate = 800f;

    // 如果 Shader Graph 里 distance 的 Reference 是 _distance，就改成 "_distance"
    private static readonly int DistanceID = Shader.PropertyToID("_distance");

    // VFX Graph Blackboard 里控制发射速率的 Float 属性名
    private static readonly int VFXRateID = Shader.PropertyToID("Rate");

    private Material dragonMaterial;
    private Coroutine transitionCoroutine;

    private void Awake()
    {
        if (dragonRenderer == null)
        {
            dragonRenderer = GetComponentInChildren<Renderer>();
        }

        if (dragonRenderer != null)
        {
            // 实例化材质，只影响当前这条小龙
            dragonMaterial = dragonRenderer.material;
        }

        if (dragonVFX == null)
        {
            dragonVFX = GetComponentInChildren<VisualEffect>();
        }

        if (dragonVFX != null)
        {
            dragonVFX.SetFloat(VFXRateID, 0f);
        }
    }

    private void OnEnable()
    {
        EventDispatcher.Instance.AddListener<float>("小龙特效转场", PlaySmallDragonTransition);
    }

    private void OnDisable()
    {
        EventDispatcher.Instance.RemoveListener<float>("小龙特效转场", PlaySmallDragonTransition);
    }

    /// <summary>
    /// 小龙特效转场：
    /// 1. 材质 distance 在 duration 秒内从 0 变化到 1.01
    /// 2. 前 1/4 时间粒子从少到多
    /// 3. 中间 1/2 时间粒子保持最大
    /// 4. 后 1/4 时间粒子从多到少并消失
    /// </summary>
    private void PlaySmallDragonTransition(float duration)
    {
        if (dragonMaterial == null)
        {
            Debug.LogWarning("DragonBehavior: 没有找到小龙材质。");
            return;
        }

        if (transitionCoroutine != null)
        {
            StopCoroutine(transitionCoroutine);
        }

        transitionCoroutine = StartCoroutine(TransitionCoroutine(duration));
    }

    private IEnumerator TransitionCoroutine(float duration)
    {
        duration = Mathf.Max(0.01f, duration);

        float timer = 0f;

        float distanceStart = 0f;
        float distanceEnd = 1.01f;

        dragonMaterial.SetFloat(DistanceID, distanceStart);

        if (dragonVFX != null)
        {
            dragonVFX.SetFloat(VFXRateID, 0f);
            dragonVFX.Play();
        }

        while (timer < duration)
        {
            timer += Time.deltaTime;

            float progress = Mathf.Clamp01(timer / duration);

            // 控制材质 distance：全程从 0 到 1.01
            float distanceValue = Mathf.Lerp(distanceStart, distanceEnd, progress);
            dragonMaterial.SetFloat(DistanceID, distanceValue);

            // 控制粒子 Rate：
            // 0% ~ 25%：从 0 到 maxParticleRate
            // 25% ~ 75%：保持 maxParticleRate
            // 75% ~ 100%：从 maxParticleRate 到 0
            float particleRate = CalculateParticleRate(progress);
            if (dragonVFX != null)
            {
                dragonVFX.SetFloat(VFXRateID, particleRate);
            }

            yield return null;
        }

        dragonMaterial.SetFloat(DistanceID, distanceEnd);

        if (dragonVFX != null)
        {
            dragonVFX.SetFloat(VFXRateID, 0f);
            dragonVFX.Stop();
        }

        transitionCoroutine = null;
    }

    private float CalculateParticleRate(float progress)
    {
        if (progress < 0.25f)
        {
            float t = progress / 0.25f;
            t = Mathf.SmoothStep(0f, 1f, t);
            return Mathf.Lerp(0f, maxParticleRate, t);
        }

        if (progress > 0.75f)
        {
            float t = (progress - 0.75f) / 0.25f;
            t = Mathf.SmoothStep(0f, 1f, t);
            return Mathf.Lerp(maxParticleRate, 0f, t);
        }

        return maxParticleRate;
    }
}