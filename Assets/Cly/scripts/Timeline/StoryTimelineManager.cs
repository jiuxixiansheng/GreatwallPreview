using System;
using System.Collections;
using System.Collections.Generic;
using Greatwall.Navigation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;
using UnityEngine.Playables;

namespace Greatwall.VRAnimation.Timeline
{
    [Serializable]
    public sealed class StoryChapterEntry
    {
        public string chapterName;

        [Tooltip("这个章节是否使用 Timeline。关闭后 Director 可以为空，章节切换需要外部调用 SwitchToNextChapter。")]
        public bool useTimeline = true;

        [Tooltip("仅在 Use Timeline 开启时有效。开启后 Timeline 播完自动切换到下一章；关闭后需要外部手动调用 SwitchToNextChapter。")]
        public bool switchWhenTimelineCompleted = true;

        public PlayableDirector director;
        public GameObject sceneRoot;
        public Transform characterSpawnPoint;
    }

    [DisallowMultipleComponent]
    public sealed class StoryTimelineManager : MonoBehaviour
    {
        [Header("Character")]
        [SerializeField] private NavMeshAgent characterAgent;
        [SerializeField] private DragonNavMeshMover characterMover;
        [SerializeField] private DragonTurnController turnController;
        [SerializeField] private bool resetVisualRotationOffset = true;

        [Header("Transition Pause")]
        [Tooltip("切章转场动画播放期间需要暂停的角色 Animator。")]
        [SerializeField] private Animator characterAnimatorToPause;
        [Tooltip("切章转场动画播放期间需要暂停的音源。")]
        [SerializeField] private AudioSource[] audioSourcesToPauseDuringTransition = new AudioSource[0];

        [Header("Chapter Scene Roots (Optional)")]
        [Tooltip("关闭此项即可完全停用本脚本的章节根物体启用/关闭逻辑，适合与其他人的场景系统合并。")]
        [SerializeField] private bool switchChapterSceneRootsDuringTransition = true;

        [Header("Chapters")]
        [SerializeField] private List<StoryChapterEntry> chapters = new List<StoryChapterEntry>();
        [SerializeField, Min(0)] private int startingChapterIndex;
        [SerializeField] private bool playStartingChapterOnStart;

        [Header("Chapter Transition Animation")]
        [Tooltip("切换章节前，等待你的转场动画播放多少秒。")]
        [SerializeField, Min(0f)] private float chapterTransitionAnimationSeconds = 0f;

        [Header("Temporary Transition")]
        [Tooltip("开启时不等待黑场，直接换位置并播放下一条 Timeline。接入正式黑场后关闭。")]
        [SerializeField] private bool immediateTransitionWithoutFade = true;
        [Tooltip("画面完全变黑并完成传送后，继续保持黑场多久，再播放下一章并开始淡入。")]
        [SerializeField, Min(0f)] private float blackScreenHoldSeconds = 0.5f;

        [Header("Replaceable Fade Interface")]
        public UnityEvent onFadeOutRequested = new UnityEvent();
        public UnityEvent onFadeInRequested = new UnityEvent();

        private sealed class TimelineRelaySubscription
        {
            public TimelineCompletionRelay relay;
            public UnityAction action;
        }

        private readonly List<TimelineRelaySubscription> relaySubscriptions =
            new List<TimelineRelaySubscription>();

        private readonly List<AudioSource> pausedAudioSources =
            new List<AudioSource>();

        private int currentChapterIndex;
        private int pendingChapterIndex = -1;
        private bool transitionPending;
        private Coroutine blackScreenHoldCoroutine;
        private Coroutine chapterTransitionAnimationCoroutine;

        private float cachedCharacterAnimatorSpeed = 1f;
        private bool characterAnimatorPausedByThisScript;

        private void Awake()
        {
            currentChapterIndex = Mathf.Clamp(
                startingChapterIndex,
                0,
                Mathf.Max(0, chapters.Count - 1));
        }

        private void OnEnable()
        {
            SubscribeToChapterCompletionEvents();
            EventDispatcher.Instance.AddListener("切换章节", SwitchToNextChapter);
        }

        private void Start()
        {
            SubscribeToChapterCompletionEvents();

            if (playStartingChapterOnStart && chapters.Count > 0)
            {
                SetupChapter(currentChapterIndex);
                PlayCurrentChapter();
            }
        }

        private void OnDisable()
        {
            if (chapterTransitionAnimationCoroutine != null)
            {
                StopCoroutine(chapterTransitionAnimationCoroutine);
                chapterTransitionAnimationCoroutine = null;
            }

            if (blackScreenHoldCoroutine != null)
            {
                StopCoroutine(blackScreenHoldCoroutine);
                blackScreenHoldCoroutine = null;
            }

            ResumePausedAnimationAndAudio();
            UnsubscribeFromChapterCompletionEvents();

            EventDispatcher.Instance.RemoveListener("切换章节", SwitchToNextChapter);
        }

        [ContextMenu("切换到下一章")]
        public void SwitchToNextChapter()
        {
            BeginNextChapterTransition();
        }

        public void BeginNextChapterTransition()
        {
            if (chapters == null || chapters.Count == 0)
                return;

            int nextChapterIndex = currentChapterIndex + 1;

            if (nextChapterIndex >= chapters.Count)
                nextChapterIndex = 0;

            BeginChapterTransitionTo(nextChapterIndex);
        }

        public void SwitchToChapterByNumber(int chapterNumber)
        {
            BeginChapterTransitionTo(chapterNumber - 1);
        }

        public void SwitchToChapterByIndex(int chapterIndex)
        {
            BeginChapterTransitionTo(chapterIndex);
        }

        private void BeginChapterTransitionTo(int targetChapterIndex)
        {
            if (transitionPending || chapters.Count == 0) return;

            if (!IsValidChapterIndex(targetChapterIndex))
            {
                Debug.Log("StoryTimelineManager: 所有章节已播放完成。", this);
                return;
            }

            if (targetChapterIndex == currentChapterIndex)
            {
                Debug.LogWarning("StoryTimelineManager: 目标章节就是当前章节，已忽略切换。", this);
                return;
            }

            transitionPending = true;
            pendingChapterIndex = targetChapterIndex;

            BeginChapterTransitionAnimation(chapterTransitionAnimationSeconds);
        }

        public void BeginChapterTransitionAnimation(float durationSeconds)
        {
            if (!transitionPending || !IsValidChapterIndex(pendingChapterIndex)) return;

            if (chapterTransitionAnimationCoroutine != null)
                StopCoroutine(chapterTransitionAnimationCoroutine);

            chapterTransitionAnimationCoroutine =
                StartCoroutine(WaitForChapterTransitionAnimationThenSwitch(durationSeconds));
        }

        private IEnumerator WaitForChapterTransitionAnimationThenSwitch(float durationSeconds)
        {
            PauseCurrentChapterAnimationAndAudio();

            PlayChapterTransitionAnimation(durationSeconds);

            if (durationSeconds > 0f)
                yield return new WaitForSecondsRealtime(durationSeconds);

            chapterTransitionAnimationCoroutine = null;
            ContinuePendingChapterTransitionAfterAnimation();
        }

        private void PlayChapterTransitionAnimation(float durationSeconds)
        {
            EventDispatcher.Instance.Dispatch("zhuanchang");
        }

        private void ContinuePendingChapterTransitionAfterAnimation()
        {
            if (!transitionPending || !IsValidChapterIndex(pendingChapterIndex)) return;

            if (immediateTransitionWithoutFade)
                ApplyTransitionAtBlack(false);
            else
                onFadeOutRequested?.Invoke();
        }

        public void NotifyFadeOutComplete()
        {
            ApplyTransitionAtBlack(true);
        }

        private void ApplyTransitionAtBlack(bool waitForBlackScreenHold)
        {
            if (!transitionPending || !IsValidChapterIndex(pendingChapterIndex)) return;

            StoryChapterEntry currentChapter = GetChapter(currentChapterIndex);

            if (currentChapter?.director != null)
                currentChapter.director.Stop();

            if (characterMover != null)
                characterMover.StopMoving();

            ActivateOnlyChapterSceneRoot(pendingChapterIndex);

            StoryChapterEntry nextChapter = GetChapter(pendingChapterIndex);
            if (!PlaceCharacterAt(nextChapter.characterSpawnPoint))
            {
                ActivateOnlyChapterSceneRoot(currentChapterIndex);
                CancelPendingTransition();
                return;
            }

            currentChapterIndex = pendingChapterIndex;
            pendingChapterIndex = -1;

            if (waitForBlackScreenHold && blackScreenHoldSeconds > 0f)
            {
                blackScreenHoldCoroutine =
                    StartCoroutine(FinishTransitionAfterBlackScreenHold());
                return;
            }

            FinishTransition();
        }

        private IEnumerator FinishTransitionAfterBlackScreenHold()
        {
            yield return new WaitForSecondsRealtime(blackScreenHoldSeconds);

            blackScreenHoldCoroutine = null;
            FinishTransition();
        }

        private void FinishTransition()
        {
            ResumePausedAnimationAndAudio();

            PlayCurrentChapter();
            onFadeInRequested?.Invoke();

            transitionPending = false;
        }

        public void PlayCurrentChapter()
        {
            StoryChapterEntry chapter = GetChapter(currentChapterIndex);
            if (chapter == null)
            {
                Debug.LogError(
                    $"StoryTimelineManager: 第 {currentChapterIndex} 个章节不存在。",
                    this);
                return;
            }

            if (!chapter.useTimeline)
            {
                Debug.Log(
                    $"StoryTimelineManager: 第 {currentChapterIndex} 个章节不使用 Timeline，等待外部函数触发切章。",
                    this);
                return;
            }

            if (chapter.director == null)
            {
                Debug.LogError(
                    $"StoryTimelineManager: 第 {currentChapterIndex} 个章节启用了 Timeline，但没有绑定 PlayableDirector。",
                    this);
                return;
            }

            chapter.director.time = 0d;
            chapter.director.Evaluate();
            chapter.director.Play();
        }

        private void SetupChapter(int chapterIndex)
        {
            StoryChapterEntry chapter = GetChapter(chapterIndex);
            if (chapter == null) return;

            ActivateOnlyChapterSceneRoot(chapterIndex);
            PlaceCharacterAt(chapter.characterSpawnPoint);
        }

        private void PauseCurrentChapterAnimationAndAudio()
        {
            StoryChapterEntry currentChapter = GetChapter(currentChapterIndex);
            if (currentChapter?.director != null)
                currentChapter.director.Pause();

            if (characterMover != null)
                characterMover.StopMoving();

            if (characterAgent != null && characterAgent.enabled && characterAgent.isOnNavMesh)
                characterAgent.isStopped = true;

            if (characterAnimatorToPause != null && !characterAnimatorPausedByThisScript)
            {
                cachedCharacterAnimatorSpeed = characterAnimatorToPause.speed;
                characterAnimatorToPause.speed = 0f;
                characterAnimatorPausedByThisScript = true;
            }

            pausedAudioSources.Clear();

            foreach (AudioSource audioSource in audioSourcesToPauseDuringTransition)
            {
                if (audioSource == null || !audioSource.isPlaying) continue;

                audioSource.Pause();
                pausedAudioSources.Add(audioSource);
            }
        }

        private void ResumePausedAnimationAndAudio()
        {
            if (characterAnimatorPausedByThisScript && characterAnimatorToPause != null)
                characterAnimatorToPause.speed = cachedCharacterAnimatorSpeed;

            characterAnimatorPausedByThisScript = false;

            foreach (AudioSource audioSource in pausedAudioSources)
            {
                if (audioSource != null)
                    audioSource.UnPause();
            }

            pausedAudioSources.Clear();

            if (characterAgent != null && characterAgent.enabled && characterAgent.isOnNavMesh)
                characterAgent.isStopped = false;
        }

        private bool PlaceCharacterAt(Transform spawnPoint)
        {
            if (characterAgent == null)
            {
                Debug.LogError("StoryTimelineManager: 没有绑定角色的 NavMeshAgent。", this);
                return false;
            }

            if (spawnPoint == null)
            {
                Debug.LogError("StoryTimelineManager: 下一章节没有设置 Character Spawn Point。", this);
                return false;
            }

            characterAgent.enabled = false;
            characterAgent.transform.SetPositionAndRotation(
                spawnPoint.position,
                spawnPoint.rotation);

            if (resetVisualRotationOffset &&
                turnController != null &&
                turnController.RotationRoot != characterAgent.transform)
            {
                turnController.ResetRotationOffset();
            }

            return true;
        }


        private void ActivateOnlyChapterSceneRoot(int targetChapterIndex)
        {
            if (!switchChapterSceneRootsDuringTransition) return;

            StoryChapterEntry targetChapter = GetChapter(targetChapterIndex);
            GameObject targetRoot = targetChapter?.sceneRoot;

            if (targetRoot == null)
            {
                Debug.LogWarning(
                    "StoryTimelineManager: 当前目标章节没有设置 Scene Root。",
                    this);
                return;
            }

            foreach (StoryChapterEntry chapter in chapters)
            {
                GameObject chapterRoot = chapter?.sceneRoot;
                if (chapterRoot == null || chapterRoot == targetRoot) continue;

                chapterRoot.SetActive(false);
            }

            targetRoot.SetActive(true);
        }

        private void CancelPendingTransition()
        {
            if (chapterTransitionAnimationCoroutine != null)
            {
                StopCoroutine(chapterTransitionAnimationCoroutine);
                chapterTransitionAnimationCoroutine = null;
            }

            if (blackScreenHoldCoroutine != null)
            {
                StopCoroutine(blackScreenHoldCoroutine);
                blackScreenHoldCoroutine = null;
            }

            ResumePausedAnimationAndAudio();

            transitionPending = false;
            pendingChapterIndex = -1;
        }

        private void SubscribeToChapterCompletionEvents()
        {
            UnsubscribeFromChapterCompletionEvents();

            for (int i = 0; i < chapters.Count; i++)
            {
                StoryChapterEntry chapter = chapters[i];
                if (chapter == null) continue;
                if (!chapter.useTimeline) continue;
                if (!chapter.switchWhenTimelineCompleted) continue;
                if (chapter.director == null) continue;

                TimelineCompletionRelay relay =
                    chapter.director.GetComponent<TimelineCompletionRelay>();

                if (relay == null)
                {
                    Debug.LogWarning(
                        $"StoryTimelineManager: '{chapter.director.name}' 没有 TimelineCompletionRelay。",
                        chapter.director);
                    continue;
                }

                int chapterIndex = i;
                UnityAction action = () => HandleTimelineCompleted(chapterIndex);

                relay.onTimelineCompleted.AddListener(action);

                relaySubscriptions.Add(new TimelineRelaySubscription
                {
                    relay = relay,
                    action = action
                });
            }
        }

        private void UnsubscribeFromChapterCompletionEvents()
        {
            foreach (TimelineRelaySubscription subscription in relaySubscriptions)
            {
                if (subscription?.relay != null && subscription.action != null)
                    subscription.relay.onTimelineCompleted.RemoveListener(subscription.action);
            }

            relaySubscriptions.Clear();
        }

        private void HandleTimelineCompleted(int completedChapterIndex)
        {
            if (completedChapterIndex != currentChapterIndex) return;

            StoryChapterEntry currentChapter = GetChapter(currentChapterIndex);
            if (currentChapter == null) return;
            if (!currentChapter.useTimeline) return;
            if (!currentChapter.switchWhenTimelineCompleted) return;

            BeginNextChapterTransition();
        }

        private StoryChapterEntry GetChapter(int index)
        {
            return IsValidChapterIndex(index) ? chapters[index] : null;
        }

        private bool IsValidChapterIndex(int index)
        {
            return index >= 0 && index < chapters.Count;
        }
    }
}