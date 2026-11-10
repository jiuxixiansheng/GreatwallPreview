using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace Greatwall.VRAnimation
{
    /// <summary>
    /// 通用剧情动画播放器。剧情顺序存放在 AnimationSequenceAsset 中，代码不需要知道每段故事的具体逻辑。
    /// </summary>
    public sealed class AnimationSequencePlayer : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private Animator bodyAnimator;
        [SerializeField] private FacialSpeechController facialSpeech;
        [SerializeField] private int bodyLayerIndex = 0;

        [Header("Events")]
        [Tooltip("传入当前字幕文本；传入空字符串表示清除字幕。")]
        public UnityEvent<string> onSubtitleChanged = new UnityEvent<string>();
        public UnityEvent onSequenceStarted = new UnityEvent();
        public UnityEvent onSequenceFinished = new UnityEvent();

        public bool IsPlaying { get; private set; }
        public int CurrentStepIndex { get; private set; } = -1;

        private Coroutine playRoutine;
        private bool completeManualStep;

        private void Reset()
        {
            bodyAnimator = GetComponent<Animator>();
            facialSpeech = GetComponent<FacialSpeechController>();
        }

        public void Play(AnimationSequenceAsset sequence)
        {
            if (sequence == null || sequence.steps == null || sequence.steps.Count == 0)
            {
                Debug.LogWarning("AnimationSequencePlayer: 没有可播放的 Sequence。", this);
                return;
            }

            Stop();
            playRoutine = StartCoroutine(PlayRoutine(sequence));
        }

        public void Stop()
        {
            if (playRoutine != null) StopCoroutine(playRoutine);
            playRoutine = null;
            IsPlaying = false;
            CurrentStepIndex = -1;
            completeManualStep = false;
            facialSpeech?.StopSpeech();
            onSubtitleChanged?.Invoke(string.Empty);
        }

        public void SkipCurrentStep()
        {
            if (IsPlaying) completeManualStep = true;
        }

        public void CompleteManualStep()
        {
            if (IsPlaying) completeManualStep = true;
        }

        private IEnumerator PlayRoutine(AnimationSequenceAsset sequence)
        {
            IsPlaying = true;
            onSequenceStarted?.Invoke();

            for (CurrentStepIndex = 0; CurrentStepIndex < sequence.steps.Count; CurrentStepIndex++)
            {
                AnimationSequenceStep step = sequence.steps[CurrentStepIndex];
                if (step == null) continue;

                completeManualStep = false;
                onSubtitleChanged?.Invoke(step.subtitle ?? string.Empty);

                PlayBody(step);
                if (facialSpeech != null)
                {
                    facialSpeech.PlaySpeech(
                        step.voice,
                        step.faceStatePath,
                        step.faceCrossFadeSeconds,
                        step.faceTalkWhileVoicePlays);
                }

                yield return WaitForStep(step);

                facialSpeech?.StopSpeech();
                onSubtitleChanged?.Invoke(string.Empty);
            }

            IsPlaying = false;
            CurrentStepIndex = -1;
            playRoutine = null;
            onSequenceFinished?.Invoke();
        }

        private void PlayBody(AnimationSequenceStep step)
        {
            if (bodyAnimator == null) return;
            string statePath = step.GetBodyStatePath();
            if (string.IsNullOrWhiteSpace(statePath)) return;

            int hash = Animator.StringToHash(statePath);
            if (!bodyAnimator.HasState(bodyLayerIndex, hash))
            {
                Debug.LogError(
                    $"AnimationSequencePlayer: 在第 {bodyLayerIndex} 层找不到状态 '{statePath}'。请检查 Layer、子状态机和状态名。",
                    this);
                return;
            }

            bodyAnimator.CrossFadeInFixedTime(hash, Mathf.Max(0f, step.crossFadeSeconds), bodyLayerIndex, 0f);
        }

        private IEnumerator WaitForStep(AnimationSequenceStep step)
        {
            switch (step.advanceMode)
            {
                case SequenceAdvanceMode.Voice:
                    yield return WaitForVoice(step);
                    break;
                case SequenceAdvanceMode.BodyAndVoice:
                    yield return WaitForBody(step);
                    yield return WaitForVoice(step);
                    break;
                case SequenceAdvanceMode.Manual:
                    yield return new WaitUntil(() => completeManualStep);
                    break;
                default:
                    yield return WaitForBody(step);
                    break;
            }
        }

        private IEnumerator WaitForBody(AnimationSequenceStep step)
        {
            if (bodyAnimator == null) yield break;
            string statePath = step.GetBodyStatePath();
            if (string.IsNullOrWhiteSpace(statePath)) yield break;

            int hash = Animator.StringToHash(statePath);
            if (!bodyAnimator.HasState(bodyLayerIndex, hash)) yield break;

            // 等待 CrossFade 完成，避免刚切换状态时读取到上一个状态的时间。
            yield return null;
            while (!completeManualStep)
            {
                AnimatorStateInfo info = bodyAnimator.GetCurrentAnimatorStateInfo(bodyLayerIndex);
                if (info.IsName(statePath) && info.normalizedTime >= step.bodyExitNormalizedTime)
                    yield break;
                yield return null;
            }
        }

        private IEnumerator WaitForVoice(AnimationSequenceStep step)
        {
            if (step.voice == null || facialSpeech == null) yield break;
            while (!completeManualStep && facialSpeech.IsVoicePlaying)
                yield return null;
        }
    }
}
