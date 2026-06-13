using UnityEngine;
using UnityEngine.Events; // 【新增】引入 UnityEvent 命名空间
using System.Collections;
using System.Collections.Generic;

[RequireComponent(typeof(AudioSource))]
public class VoiceSequenceManager : MonoBehaviour
{
    [System.Serializable]
    public class VoiceStep
    {
        public string stepName;
        public AudioClip voiceClip;

        [Range(0f, 1f)]
        public float volume = 1.0f;

        [Tooltip("是否自动播放？如果勾选，按顺序自动播；如果不勾选，序列会停在这里一直等，直到被外部触发放行。")]
        public bool autoPlay = true;

        public float delayBeforeStart;
        public float delayAfterFinish;

        // ==========================================
        // 【新增核心功能】在音频结束时调用的自定义事件
        // ==========================================
        [Header("台词结束后的触发器")]
        [Tooltip("当这句台词播放完毕（包含结束后的延迟时间）后，执行哪些外部脚本的函数？")]
        public UnityEvent onStepFinish;
    }

    public AudioSource targetAudioSource;
    public List<VoiceStep> voiceSequence = new List<VoiceStep>();
    public bool playOnStart = true;

    private int currentIndex = 0;
    private Coroutine currentSequenceRoutine;

    void Awake()
    {
        if (targetAudioSource == null) targetAudioSource = GetComponent<AudioSource>();
    }

    void Start()
    {
        if (playOnStart && voiceSequence.Count > 0) StartVoiceSequence();
    }

    public void StartVoiceSequence()
    {
        if (currentSequenceRoutine != null) StopCoroutine(currentSequenceRoutine);
        currentSequenceRoutine = StartCoroutine(PlayStep(0));
    }

    public void UnlockVoiceByNumber(int stepNumber)
    {
        int realIndex = stepNumber - 1;

        if (realIndex >= 0 && realIndex < voiceSequence.Count)
        {
            voiceSequence[realIndex].autoPlay = true;
            Debug.Log($"[VoiceSequence] 收到外部触发！第 {stepNumber} 句台词 ({voiceSequence[realIndex].stepName}) 已被放行！");
        }
        else
        {
            Debug.LogWarning($"[VoiceSequence] 找不到第 {stepNumber} 句台词，请检查输入的编号是否超出了列表长度！");
        }
    }

    private IEnumerator PlayStep(int index)
    {
        if (index >= voiceSequence.Count || voiceSequence[index].voiceClip == null) yield break;

        currentIndex = index;
        var currentStep = voiceSequence[index];

        // 阻塞等待
        while (!currentStep.autoPlay)
        {
            yield return null;
        }

        // 1. 开口前的等待
        if (currentStep.delayBeforeStart > 0) yield return new WaitForSeconds(currentStep.delayBeforeStart);

        // 2. 播放当前台词
        targetAudioSource.volume = currentStep.volume;
        targetAudioSource.clip = currentStep.voiceClip;
        targetAudioSource.Play();

        // 3. 等待音频播放完毕
        yield return new WaitForSeconds(currentStep.voiceClip.length);

        // 4. 说完后的停顿
        if (currentStep.delayAfterFinish > 0) yield return new WaitForSeconds(currentStep.delayAfterFinish);

        // ==========================================
        // 【新增执行逻辑】触发 Inspector 里拖入的所有事件
        // ==========================================
        currentStep.onStepFinish?.Invoke();

        // 5. 自动进入下一句台词
        currentSequenceRoutine = StartCoroutine(PlayStep(index + 1));
    }
}