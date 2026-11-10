using System.Collections;
using UnityEngine;

namespace Greatwall.VRAnimation
{
    /// <summary>
    /// 控制角色 Animator 的面部层。面部层应使用 Avatar Mask，只包含头部/面部骨骼。
    /// Face_Idle -> Face_Talk 的过渡条件为 Face_Talking == true。
    /// </summary>
    public sealed class FacialSpeechController : MonoBehaviour
    {
        [Header("Animator")]
        [SerializeField] private Animator animator;
        [SerializeField] private int facialLayerIndex = 1;
        [SerializeField] private string talkingBool = "Face_Talking";
        [SerializeField] private string idleFaceStatePath = "Face.Face_Idle";

        [Header("Audio")]
        [SerializeField] private AudioSource voiceSource;

        [Header("Fallback")]
        [Tooltip("没有语音文件时，调用 PlaySyntheticSpeech 使用的默认时长。")]
        [SerializeField, Min(0.1f)] private float fallbackSpeechSeconds = 2f;

        private Coroutine fallbackRoutine;

        public bool IsTalking => animator != null && animator.GetBool(talkingBool);
        public bool IsVoicePlaying => voiceSource != null && voiceSource.isPlaying;

        private void Reset()
        {
            animator = GetComponent<Animator>();
            voiceSource = GetComponent<AudioSource>();
        }

        private void Awake()
        {
            if (animator == null) animator = GetComponent<Animator>();
            if (voiceSource == null) voiceSource = GetComponent<AudioSource>();
            if (animator != null && facialLayerIndex >= 0 && facialLayerIndex < animator.layerCount)
                animator.SetLayerWeight(facialLayerIndex, 1f);
            SetTalking(false);
        }

        public void SetTalking(bool value)
        {
            if (animator == null) return;
            animator.SetBool(talkingBool, value);
        }

        public void PlaySpeech(
            AudioClip clip,
            string faceStatePath,
            float crossFadeSeconds,
            bool useGenericTalkingFallback)
        {
            StopSpeech(false);

            if (!string.IsNullOrWhiteSpace(faceStatePath))
            {
                PlayFaceState(faceStatePath, crossFadeSeconds);
            }
            else
            {
                SetTalking(useGenericTalkingFallback && clip != null);
            }

            if (voiceSource != null && clip != null)
            {
                voiceSource.clip = clip;
                voiceSource.Play();
            }
        }

        public void PlaySyntheticSpeech(float seconds)
        {
            StopSpeech(false);
            SetTalking(true);
            fallbackRoutine = StartCoroutine(StopAfter(seconds));
        }

        public void StopSpeech()
        {
            StopSpeech(true);
        }

        public void StopSpeech(bool returnToIdleFace)
        {
            if (fallbackRoutine != null)
            {
                StopCoroutine(fallbackRoutine);
                fallbackRoutine = null;
            }

            if (voiceSource != null)
            {
                voiceSource.Stop();
                voiceSource.clip = null;
            }

            SetTalking(false);

            if (returnToIdleFace)
                PlayFaceState(idleFaceStatePath, 0.1f, false);
        }

        public float CurrentSpeechLength(AudioClip clip)
        {
            return clip == null ? fallbackSpeechSeconds : clip.length;
        }

        private IEnumerator StopAfter(float seconds)
        {
            yield return new WaitForSeconds(Mathf.Max(0.1f, seconds));
            fallbackRoutine = null;
            SetTalking(false);
        }

        private void PlayFaceState(string statePath, float crossFadeSeconds, bool logMissingState = true)
        {
            if (animator == null || string.IsNullOrWhiteSpace(statePath)) return;

            int hash = Animator.StringToHash(statePath);
            if (!animator.HasState(facialLayerIndex, hash))
            {
                if (logMissingState)
                {
                    Debug.LogError(
                        $"FacialSpeechController: 在第 {facialLayerIndex} 层找不到面部状态 '{statePath}'。",
                        this);
                }

                return;
            }

            animator.CrossFadeInFixedTime(hash, Mathf.Max(0f, crossFadeSeconds), facialLayerIndex, 0f);
        }
    }
}
