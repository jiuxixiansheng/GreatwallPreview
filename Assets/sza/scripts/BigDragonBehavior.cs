using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BigDragonBehavior : MonoBehaviour
{
    [Header("大龙材质控制")]
    [SerializeField] private Renderer[] bigDragonRenderers;

    [Header("动画控制")]
    [SerializeField] private Animator bigDragonAnimator;

    // 对应 Animator Parameters 里的 bool 参数：start
    [SerializeField] private string animatorStartBoolName = "start";

    [Header("转场设置")]
    [SerializeField] private float transitionDelay = 1f;

    // 如果 Shader Graph 里 distance 的 Reference 是 _distance，就改成 "_distance"
    private static readonly int DistanceID = Shader.PropertyToID("_distance");

    private readonly List<Material> bigDragonMaterials = new List<Material>();
    private Coroutine transitionCoroutine;

    private void Awake()
    {
        if (bigDragonRenderers == null || bigDragonRenderers.Length == 0)
        {
            bigDragonRenderers = GetComponentsInChildren<Renderer>();
        }

        if (bigDragonAnimator == null)
        {
            bigDragonAnimator = GetComponentInChildren<Animator>();
        }

        CacheMaterials();

        // 初始状态：大龙隐藏
        SetAllMaterialsDistance(1.01f);

        // 初始状态：大龙动画不开始
        SetAnimatorStart(false);
    }

    private void OnEnable()
    {
        EventDispatcher.Instance.AddListener<float>("大龙特效转场", PlayBigDragonTransition);
    }

    private void OnDisable()
    {
        EventDispatcher.Instance.RemoveListener<float>("大龙特效转场", PlayBigDragonTransition);
    }

    /// <summary>
    /// 大龙特效转场：
    /// 等待 transitionDelay 秒后，
    /// 让大龙所有材质 distance 从 1.01 变化到 0；
    /// 转场结束后，把 Animator 里的 start 设置为 true。
    /// </summary>
    private void PlayBigDragonTransition(float duration)
    {
        if (bigDragonMaterials.Count == 0)
        {
            Debug.LogWarning("BigDragonBehavior: 没有找到大龙材质。");
            return;
        }

        if (transitionCoroutine != null)
        {
            StopCoroutine(transitionCoroutine);
        }

        transitionCoroutine = StartCoroutine(BigDragonTransitionCoroutine(duration));
    }

    private IEnumerator BigDragonTransitionCoroutine(float duration)
    {
        duration = Mathf.Max(0.01f, duration);
        transitionDelay = Mathf.Max(0f, transitionDelay);

        float startValue = 1.01f;
        float endValue = 0f;

        // 每次转场开始前，先保持隐藏，并关闭动画启动条件
        SetAllMaterialsDistance(startValue);
        SetAnimatorStart(false);

        if (transitionDelay > 0f)
        {
            yield return new WaitForSeconds(transitionDelay);
        }

        float timer = 0f;

        while (timer < duration)
        {
            timer += Time.deltaTime;

            float t = Mathf.Clamp01(timer / duration);
            float value = Mathf.Lerp(startValue, endValue, t);

            SetAllMaterialsDistance(value);

            yield return null;
        }

        // 确保最终完全显示
        SetAllMaterialsDistance(endValue);

        // 转场结束后，大龙开始播放动画
        SetAnimatorStart(true);

        transitionCoroutine = null;
    }

    private void CacheMaterials()
    {
        bigDragonMaterials.Clear();

        for (int i = 0; i < bigDragonRenderers.Length; i++)
        {
            if (bigDragonRenderers[i] == null)
            {
                continue;
            }

            // materials 会实例化材质，只影响当前大龙
            Material[] materials = bigDragonRenderers[i].materials;

            for (int j = 0; j < materials.Length; j++)
            {
                if (materials[j] != null)
                {
                    bigDragonMaterials.Add(materials[j]);
                }
            }
        }
    }

    private void SetAllMaterialsDistance(float value)
    {
        for (int i = 0; i < bigDragonMaterials.Count; i++)
        {
            if (bigDragonMaterials[i] != null)
            {
                bigDragonMaterials[i].SetFloat(DistanceID, value);
            }
        }
    }

    private void SetAnimatorStart(bool value)
    {
        if (bigDragonAnimator == null)
        {
            return;
        }

        bigDragonAnimator.SetBool(animatorStartBoolName, value);
    }
}