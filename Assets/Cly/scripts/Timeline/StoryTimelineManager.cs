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
        public PlayableDirector director;
        public GameObject sceneRoot;
        public Transform characterSpawnPoint;
        public Transform playerSpawnPoint;
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

        [Header("Player (Optional)")]
        [Tooltip("关闭此项即可完全停用本脚本的玩家传送，适合与其他人的转场系统合并。")]
        [SerializeField] private bool movePlayerDuringChapterTransition = true;
        [Tooltip("绑定 XR Origin、VR Rig 或其他代表玩家整体位置的根物体，不要绑定头显 Camera。")]
        [SerializeField] private Transform playerRoot;
        [Tooltip("绑定 Player Root 下的 Main Camera，用于消除头显实时位移造成的传送偏差。")]
        [SerializeField] private Transform playerHead;

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

        private readonly List<TimelineCompletionRelay> subscribedRelays =
            new List<TimelineCompletionRelay>();

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

            if (playerHead == null && playerRoot != null)
            {
                Camera playerCamera = playerRoot.GetComponentInChildren<Camera>(true);
                if (playerCamera != null)
                    playerHead = playerCamera.transform;
            }
        }

        private void OnEnable()
        {
            SubscribeToChapterCompletionEvents();
        }

        private void Start()
        {
            SubscribeToChapterCompletionEvents();

            if (playStartingChapterOnStart && chapters.Count > 0)
            {
                StoryChapterEntry startingChapter = GetChapter(currentChapterIndex);
                if (startingChapter != null)
                {
                    ActivateOnlyChapterSceneRoot(currentChapterIndex);
                    PlaceCharacterAt(startingChapter.characterSpawnPoint);
                    PlacePlayerAt(startingChapter.playerSpawnPoint);
                }

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
        }

        public void BeginNextChapterTransition()
        {
            if (transitionPending || chapters.Count == 0) return;

            int nextIndex = currentChapterIndex + 1;
            if (nextIndex >= chapters.Count)
            {
                Debug.Log("StoryTimelineManager: 所有章节已播放完成。", this);
                return;
            }

            transitionPending = true;
            pendingChapterIndex = nextIndex;

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
            // TODO: 在这里播放你的转场动画。
            // durationSeconds 表示脚本会等待多少秒后真正切换到下一章。
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
            StoryChapterEntry nextChapter = GetChapter(pendingChapterIndex);

            if (currentChapter?.director != null)
                currentChapter.director.Stop();

            if (characterMover != null)
                characterMover.StopMoving();

            ActivateOnlyChapterSceneRoot(pendingChapterIndex);

            if (!PlaceCharacterAt(nextChapter.characterSpawnPoint))
            {
                ActivateOnlyChapterSceneRoot(currentChapterIndex);
                CancelPendingTransition();
                return;
            }

            PlacePlayerAt(nextChapter.playerSpawnPoint);

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
            if (chapter?.director == null)
            {
                Debug.LogError(
                    $"StoryTimelineManager: 第 {currentChapterIndex} 个章节没有绑定 PlayableDirector。",
                    this);
                return;
            }

            chapter.director.time = 0d;
            chapter.director.Evaluate();
            chapter.director.Play();
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

        private void PlacePlayerAt(Transform spawnPoint)
        {
            if (!movePlayerDuringChapterTransition) return;

            if (playerRoot == null)
            {
                Debug.LogWarning(
                    "StoryTimelineManager: 已开启玩家传送，但没有绑定 Player Root。",
                    this);
                return;
            }

            if (spawnPoint == null)
            {
                Debug.LogWarning(
                    "StoryTimelineManager: 当前目标章节没有设置 Player Spawn Point。",
                    this);
                return;
            }

            if (playerHead == null || !playerHead.IsChildOf(playerRoot))
            {
                Debug.LogWarning(
                    "StoryTimelineManager: 没有绑定有效的 Player Head，将使用旧的根物体传送方式。",
                    this);

                playerRoot.SetPositionAndRotation(
                    spawnPoint.position,
                    spawnPoint.rotation);
                return;
            }

            Vector3 currentHeadForward =
                Vector3.ProjectOnPlane(playerHead.forward, Vector3.up);
            Vector3 targetForward =
                Vector3.ProjectOnPlane(spawnPoint.forward, Vector3.up);

            if (currentHeadForward.sqrMagnitude > 0.0001f &&
                targetForward.sqrMagnitude > 0.0001f)
            {
                float yawDelta = Vector3.SignedAngle(
                    currentHeadForward,
                    targetForward,
                    Vector3.up);

                playerRoot.Rotate(Vector3.up, yawDelta, Space.World);
            }

            Vector3 rootPosition = playerRoot.position;
            Vector3 headPosition = playerHead.position;

            playerRoot.position = new Vector3(
                rootPosition.x + spawnPoint.position.x - headPosition.x,
                spawnPoint.position.y,
                rootPosition.z + spawnPoint.position.z - headPosition.z);
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

            foreach (StoryChapterEntry chapter in chapters)
            {
                if (chapter?.director == null) continue;

                TimelineCompletionRelay relay =
                    chapter.director.GetComponent<TimelineCompletionRelay>();
                if (relay == null)
                {
                    Debug.LogWarning(
                        $"StoryTimelineManager: '{chapter.director.name}' 没有 TimelineCompletionRelay。",
                        chapter.director);
                    continue;
                }

                relay.onTimelineCompleted.AddListener(HandleTimelineCompleted);
                subscribedRelays.Add(relay);
            }
        }

        private void UnsubscribeFromChapterCompletionEvents()
        {
            foreach (TimelineCompletionRelay relay in subscribedRelays)
            {
                if (relay != null)
                    relay.onTimelineCompleted.RemoveListener(HandleTimelineCompleted);
            }

            subscribedRelays.Clear();
        }

        private void HandleTimelineCompleted()
        {
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