using UnityEngine;

/// <summary>
/// 给尾巴根部添加轻微、持续的待机左右摆动。
/// 建议目标骨骼设置为 c_tail_00_x，后面的骨骼交给 EZSoftBone。
/// </summary>
[DefaultExecutionOrder(-100)]
public class TailIdleSway : MonoBehaviour
{
    [Header("目标骨骼")]
    [Tooltip("通常设置为 c_tail_00_x。不要设置为整条尾巴的每一节骨骼。")]
    public Transform targetBone;

    [Header("摆动参数")]
    [Tooltip("绕目标骨骼本地哪个轴旋转。大多数尾巴可以先使用 Y 轴。")]
    public Vector3 localAxis = Vector3.up;

    [Range(0f, 15f)]
    [Tooltip("左右摆动的最大角度，建议从 2～4 度开始。")]
    public float amplitude = 3f;

    [Min(0f)]
    [Tooltip("每秒摆动的周期数量，建议从 0.6～1.0 开始。")]
    public float frequency = 0.8f;

    [Range(0f, 1f)]
    [Tooltip("摆动总权重。设为 0 可以临时关闭。")]
    public float weight = 1f;

    [Tooltip("用于让不同角色的尾巴不要完全同步。")]
    public float phaseOffset = 0f;

    [Header("频率随机化")]
    [Tooltip("让摆动频率在小范围内平滑变化，避免机械式循环。")]
    public bool randomizeFrequency = true;

    [Range(0f, 0.5f)]
    [Tooltip("频率变化范围。0.15 表示当前频率上下约 15%。")]
    public float frequencyRandomRange = 0.15f;

    [Min(0.5f)]
    [Tooltip("多久选择一次新的目标频率。")]
    public float frequencyChangeInterval = 4f;

    [Min(0.1f)]
    [Tooltip("频率变化的平滑速度。数值越大，变化越快。")]
    public float frequencyChangeSmooth = 1.5f;

    [Tooltip("是否使用不受 Time.timeScale 影响的时间。一般保持关闭。")]
    public bool useUnscaledTime = false;

    // 上一帧由本脚本施加的旋转，用来避免旋转不断累积。
    private Quaternion lastAppliedSway = Quaternion.identity;
    private bool initialized;
    private float runtimeFrequency;
    private float targetFrequency;
    private float nextFrequencyChangeTime;
    private float wavePhase;

    private void OnEnable()
    {
        initialized = false;
        lastAppliedSway = Quaternion.identity;
        runtimeFrequency = frequency;
        targetFrequency = frequency;
        nextFrequencyChangeTime = 0f;
        wavePhase = 0f;
    }

    private void Update()
    {
        // 先清除上一帧由本脚本添加的旋转。
        // Animator 会在 Update 与 LateUpdate 之间写入当前动画姿势。
        RemoveLastAppliedSway();
    }

    private void LateUpdate()
    {
        if (targetBone == null)
        {
            return;
        }

        if (!initialized)
        {
            lastAppliedSway = Quaternion.identity;
            initialized = true;
        }

        Vector3 axis = localAxis.sqrMagnitude > 0.0001f
            ? localAxis.normalized
            : Vector3.up;

        // 此时 Animator 已经完成当前帧的动画，直接读取动画姿势。
        Quaternion animatedLocalRotation = targetBone.localRotation;

        float currentTime = useUnscaledTime
            ? Time.unscaledTime
            : Time.time;

        float dt = useUnscaledTime
            ? Time.unscaledDeltaTime
            : Time.deltaTime;
        dt = Mathf.Min(dt, 1f / 30f);

        if (randomizeFrequency)
        {
            if (currentTime >= nextFrequencyChangeTime)
            {
                float minMultiplier = 1f - frequencyRandomRange;
                float maxMultiplier = 1f + frequencyRandomRange;
                targetFrequency = frequency * Random.Range(minMultiplier, maxMultiplier);
                nextFrequencyChangeTime = currentTime + frequencyChangeInterval;
            }

            float smoothFactor = 1f - Mathf.Exp(-frequencyChangeSmooth * dt);
            runtimeFrequency = Mathf.Lerp(runtimeFrequency, targetFrequency, smoothFactor);
        }
        else
        {
            runtimeFrequency = frequency;
        }

        wavePhase += runtimeFrequency * Mathf.PI * 2f * dt;

        float angle = Mathf.Sin(
            wavePhase + phaseOffset * runtimeFrequency * Mathf.PI * 2f
        ) * amplitude * weight;

        Quaternion currentSway = Quaternion.AngleAxis(angle, axis);

        // 在 Animator 当前姿势上叠加轻微摆动。
        targetBone.localRotation = animatedLocalRotation * currentSway;
        lastAppliedSway = currentSway;
    }

    private void OnDisable()
    {
        RemoveLastAppliedSway();
    }

    private void RemoveLastAppliedSway()
    {
        if (targetBone == null)
        {
            lastAppliedSway = Quaternion.identity;
            return;
        }

        targetBone.localRotation =
            targetBone.localRotation * Quaternion.Inverse(lastAppliedSway);

        lastAppliedSway = Quaternion.identity;
    }

    /// <summary>
    /// 角色瞬移、重生或重新摆放位置时调用。
    /// </summary>
    [ContextMenu("Reset Sway")]
    public void ResetSway()
    {
        RemoveLastAppliedSway();
        initialized = true;
    }
}
