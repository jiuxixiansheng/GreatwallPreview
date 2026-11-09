using UnityEngine;
using UnityEngine.Playables;
using Unity.XR.CoreUtils;
using Mirror;

namespace Greatwall.VRAnimation.Timeline
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider), typeof(Rigidbody))]
    public sealed class CrackRepairTrigger : MonoBehaviour
    {
        [Header("允许触发监听的物体")]
        [Tooltip("可选的额外触发物体。自身或子物体的碰撞体进入区域后开始监听。")]
        [SerializeField] private GameObject[] triggerObjects = new GameObject[0];
        [Tooltip("自动接受本机网络玩家；单机模式下接受 XR Origin，无需拖入运行时玩家。")]
        [SerializeField] private bool automaticallyDetectPlayers = true;

        [Header("修复完成事件（必须与发送端一致）")]
        [SerializeField] private string completionEventName = "裂缝修复完成";

        [Header("修复完成后继续播放的 Timeline")]
        [SerializeField] private PlayableDirector timelineToResume;

        public PlayableDirector TargetTimeline => timelineToResume;

        private bool waitingForRepair;
        private bool completed;
        private bool listening;
        private EventDispatcher dispatcher;
        private string registeredEventName;

        private void Reset() => ConfigureTrigger();
        private void Awake() => ConfigureTrigger();

        private void ConfigureTrigger()
        {
            GetComponent<BoxCollider>().isTrigger = true;
            Rigidbody body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
        }

        private void OnEnable()
        {
            if (Application.isPlaying && waitingForRepair && !completed) RegisterListener();
        }

        private void OnTriggerEnter(Collider other) => TryBeginWaiting(other);
        private void OnTriggerStay(Collider other) => TryBeginWaiting(other);

        private void TryBeginWaiting(Collider other)
        {
            if (!isActiveAndEnabled || completed || waitingForRepair || other == null) return;
            if (MatchesTriggerObject(other)) BeginListening();
        }

        private bool MatchesTriggerObject(Collider other)
        {
            if (other == null) return false;
            if (triggerObjects != null)
                foreach (GameObject target in triggerObjects)
                    if (target != null && (other.transform == target.transform ||
                                          other.transform.IsChildOf(target.transform))) return true;

            if (!automaticallyDetectPlayers) return false;
            var player = other.GetComponentInParent<PlayerNetworkSetup>();
            if (player != null) return player.isLocalPlayer;
            if (NetworkClient.active || NetworkServer.active) return false;
            return other.GetComponentInParent<XROrigin>() != null;
        }

        /// <summary>由区域检测或外部事件开始监听，不重置已完成的一次交互。</summary>
        public void BeginListening()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || completed || waitingForRepair) return;
            if (!RegisterListener()) return;
            waitingForRepair = true;
        }

        /// <summary>修复阶段的 Signal 到达时调用；重新进入本章也能再次使用。</summary>
        public void PrepareForRepair()
        {
            if (!Application.isPlaying) return;
            RemoveListener();
            completed = false;
            waitingForRepair = true;
            if (isActiveAndEnabled) RegisterListener();
        }

        private bool RegisterListener()
        {
            if (listening) return true;
            if (string.IsNullOrWhiteSpace(completionEventName))
            {
                Debug.LogError("[CrackRepairTrigger] 请填写修复完成事件名称。", this);
                return false;
            }
            dispatcher = EventDispatcher.Instance;
            registeredEventName = completionEventName;
            dispatcher.AddListener(registeredEventName, OnRepairCompleted);
            listening = true;
            return true;
        }

        private void OnRepairCompleted()
        {
            if (completed || !waitingForRepair || timelineToResume == null) return;
            if (NetworkGameplayCoordinator.IsManaged)
            {
                var network = NetworkGameplayCoordinator.Instance;
                if (network.story == null || network.story.CurrentDirector != timelineToResume ||
                    network.story.IsTransitionPending) return;
                // Host 恢复 Timeline、Animator 和音频，并通过现有快照同步给客户端。
                // 客户端完成交互时使用现有 Resume 请求通知 Host。
                network.ResumeTimeline();
            }
            else
            {
                var stories = FindObjectsByType<StoryTimelineManager>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);
                StoryTimelineManager owner = null;
                foreach (var story in stories)
                    if (story.CurrentDirector == timelineToResume) { owner = story; break; }
                if (owner != null)
                {
                    if (owner.IsTransitionPending) return;
                    owner.SetNetworkPlayback(true);
                }
                else timelineToResume.Resume();
            }
            completed = true;
            waitingForRepair = false;
            RemoveListener();
        }

        private void OnDisable() => RemoveListener();

        private void RemoveListener()
        {
            if (!listening) return;
            if (dispatcher != null) dispatcher.RemoveListener(registeredEventName, OnRepairCompleted);
            listening = false;
            dispatcher = null;
        }
    }
}
