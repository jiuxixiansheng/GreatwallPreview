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

        private int currentChapterIndex;
        private int pendingChapterIndex = -1;
        private bool transitionPending;
        private Coroutine blackScreenHoldCoroutine;

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
                    // ===== 可选章节场景根物体切换（可由同事的场景系统替代）=====
                    ActivateOnlyChapterSceneRoot(currentChapterIndex);
                    // ===== 可选章节场景根物体切换结束 =====

                    PlaceCharacterAt(startingChapter.characterSpawnPoint);

                    // ===== 可选玩家传送（可由同事的转场系统替代）=====
                    PlacePlayerAt(startingChapter.playerSpawnPoint);
                    // ===== 可选玩家传送结束 =====
                }

                PlayCurrentChapter();
            }
        }

        private void OnDisable()
        {
            if (blackScreenHoldCoroutine != null)
            {
                StopCoroutine(blackScreenHoldCoroutine);
                blackScreenHoldCoroutine = null;
            }

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

            // ===== 可选章节场景根物体切换（可由同事的场景系统替代）=====
            // 若同事已经负责章节根物体的启用和关闭，关闭 Inspector 中的
            // Switch Chapter Scene Roots During Transition 即可。
            ActivateOnlyChapterSceneRoot(pendingChapterIndex);
            // ===== 可选章节场景根物体切换结束 =====

            if (!PlaceCharacterAt(nextChapter.characterSpawnPoint))
            {
                ActivateOnlyChapterSceneRoot(currentChapterIndex);
                CancelPendingTransition();
                return;
            }

            // ===== 可选玩家传送（可由同事的转场系统替代）=====
            // 若同事已经负责移动玩家，关闭 Inspector 中的
            // Move Player During Chapter Transition 即可，无需删除其他章节逻辑。
            PlacePlayerAt(nextChapter.playerSpawnPoint);
            // ===== 可选玩家传送结束 =====

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

            // 转场只负责把小龙根物体精确放到章节出生点。
            // Agent 保持关闭，避免它在下一帧把根物体拉回旧位置；
            // 真正执行 NavMesh Move Clip 时，由 DragonNavMeshMover 重新启用。
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

        // ===== 可选玩家传送（可由同事的转场系统替代）=====
        // 合并其他人的玩家传送功能时，优先在 Inspector 关闭开关。
        // 如果确定永久不用，也可以删除本方法、章节中的 playerSpawnPoint，
        // 以及上方两处 PlacePlayerAt(...) 调用。
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

            // 先让玩家实际视线的水平朝向与 SpawnPoint 一致。
            // 不能直接设置 XR Origin 的 rotation，因为头显在根物体内部还有实时旋转偏移。
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

            // 旋转后重新读取头显位置，再平移 XR Origin。
            // X/Z 以实际头显位置对准 SpawnPoint，Y 仍表示 XR Origin 的地面高度。
            Vector3 rootPosition = playerRoot.position;
            Vector3 headPosition = playerHead.position;

            playerRoot.position = new Vector3(
                rootPosition.x + spawnPoint.position.x - headPosition.x,
                spawnPoint.position.y,
                rootPosition.z + spawnPoint.position.z - headPosition.z);
        }
        // ===== 可选玩家传送结束 =====

        // ===== 可选章节场景根物体切换（可由同事的场景系统替代）=====
        // 合并其他人的章节场景切换功能时，优先在 Inspector 关闭开关。
        // 如果确定永久不用，也可以删除本方法、章节中的 sceneRoot，
        // 以及上方两处 ActivateOnlyChapterSceneRoot(...) 调用。
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
        // ===== 可选章节场景根物体切换结束 =====

        private void CancelPendingTransition()
        {
            if (blackScreenHoldCoroutine != null)
            {
                StopCoroutine(blackScreenHoldCoroutine);
                blackScreenHoldCoroutine = null;
            }

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
