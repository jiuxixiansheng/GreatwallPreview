using UnityEngine;
using UnityEngine.Splines;
using System.Collections;
using System.Collections.Generic;

public class SplineSequenceManager : MonoBehaviour
{
    [System.Serializable]
    public class SplineStep
    {
        public string stepName;
        public SplineAnimate animator;

        [Tooltip("是否自动开始移动？如果不勾选，龙会停在这里死等，直到外部事件放行。")]
        public bool autoPlay = true;

        public float delayBeforeStart;
        public float delayAfterFinish;
    }

    [Header("路径序列配置")]
    public List<SplineStep> pathSequence = new List<SplineStep>();

    [Header("触发设置")]
    public bool playOnStart = true;

    private int currentIndex = 0;
    private Coroutine currentSequenceRoutine;

    void Start()
    {
        // 初始化：全部暂停，确保不会乱跑
        for (int i = 0; i < pathSequence.Count; i++)
        {
            if (pathSequence[i].animator != null)
            {
                pathSequence[i].animator.Pause();
            }
        }

        if (playOnStart && pathSequence.Count > 0)
        {
            StartPathSequence();
        }
    }

    public void StartPathSequence()
    {
        if (currentSequenceRoutine != null) StopCoroutine(currentSequenceRoutine);
        currentSequenceRoutine = StartCoroutine(PlayStep(0));
    }

    public void UnlockPathByNumber(int stepNumber)
    {
        int realIndex = stepNumber - 1;

        if (realIndex >= 0 && realIndex < pathSequence.Count)
        {
            pathSequence[realIndex].autoPlay = true;
            Debug.Log($"[SplineSequence] 收到外部触发！第 {stepNumber} 段路径 ({pathSequence[realIndex].stepName}) 已被放行！");
        }
    }

    private IEnumerator PlayStep(int index)
    {
        if (index >= pathSequence.Count || pathSequence[index].animator == null) yield break;

        currentIndex = index;
        var currentStep = pathSequence[index];

        // ==========================================
        // 【新增核心修复】：一进入该阶段，立刻强行将位置吸附到曲线起点
        // ==========================================
        currentStep.animator.NormalizedTime = 0f; // 进度归零
        currentStep.animator.Play();              // 瞬间激活底层的 Transform 覆盖计算
        currentStep.animator.Pause();             // 立刻暂停，把它死死冻结在起点

        // 阻塞等待：如果不自动播放，就在这里等着
        while (!currentStep.autoPlay)
        {
            yield return null;
        }

        // 1. 启动前的等待时间（此时龙已经站在起点发呆了）
        if (currentStep.delayBeforeStart > 0)
        {
            yield return new WaitForSeconds(currentStep.delayBeforeStart);
        }

        // 2. 正式开始播放当前路径
        currentStep.animator.Restart(true);

        // 3. 等待当前路径播放完成
        while (currentStep.animator.IsPlaying)
        {
            yield return null;
        }

        // 4. 结束后的等待时间
        if (currentStep.delayAfterFinish > 0)
        {
            yield return new WaitForSeconds(currentStep.delayAfterFinish);
        }

        // 5. 自动进入下一个路径段落
        currentSequenceRoutine = StartCoroutine(PlayStep(index + 1));
    }
}