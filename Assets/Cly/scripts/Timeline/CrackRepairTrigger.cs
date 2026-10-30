using UnityEngine;
using UnityEngine.Playables;
using Unity.XR.CoreUtils;

namespace Greatwall.VRAnimation.Timeline
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider), typeof(Rigidbody))]
    public sealed class CrackRepairTrigger : MonoBehaviour
    {
        [Header("修复完成事件（必须与发送端一致）")]
        [SerializeField] private string completionEventName = "裂缝修复完成";

        [Header("修复完成后继续播放的 Timeline")]
        [SerializeField] private PlayableDirector timelineToResume;

        private bool waitingForRepair;
        private bool completed;
        private bool listening;
        private EventDispatcher dispatcher;
        private string registeredEventName;

        private void Reset()
        {
            ConfigureTrigger();
        }

        private void Awake()
        {
            ConfigureTrigger();
        }

        private void ConfigureTrigger()
        {
            GetComponent<BoxCollider>().isTrigger = true;
            Rigidbody body = GetComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
        }

        private void OnEnable()
        {
            // 临时禁用后恢复等待，但已经完成的交互不会重新开始。
            if (waitingForRepair && !completed) RegisterListener();
        }

        private void OnTriggerEnter(Collider other)
        {
            TryBeginWaiting(other);
        }

        private void OnTriggerStay(Collider other)
        {
            // 兼容组件启用时玩家已经在盒内，不依赖再次跨过边界。
            TryBeginWaiting(other);
        }

        private void TryBeginWaiting(Collider other)
        {
            if (!isActiveAndEnabled || completed || waitingForRepair) return;

            // 复用多人玩家身体设置；单机场景也接受 XR 身体和现有头部碰撞体。
            PlayerNetworkSetup player = other.GetComponentInParent<PlayerNetworkSetup>();
            bool isNetworkPlayerBody = player != null && player.playerCollider == other;
            XROrigin origin = other.GetComponentInParent<XROrigin>();
            bool isXRPlayerBody = other is CharacterController &&
                                  origin != null;
            // 项目现有 head 是 Main Camera 的直接子物体，带非 Trigger 的 SphereCollider。
            // 按组件和层级识别所有 Rig，不依赖 head 或玩家物体的名称。
            bool isXRPlayerHead = origin != null && origin.Camera != null &&
                                  other is SphereCollider && !other.isTrigger &&
                                  (other.transform == origin.Camera.transform ||
                                   other.transform.parent == origin.Camera.transform);
            if (!isNetworkPlayerBody && !isXRPlayerBody && !isXRPlayerHead)
                return;

            BeginListening();
        }

        /// <summary>
        /// 供其他玩家检测脚本或 Inspector 的 UnityEvent 调用。
        /// 与碰撞盒检测共用一次性注册及完成后锁定逻辑。
        /// </summary>
        public void BeginListening()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || completed || waitingForRepair) return;
            if (!RegisterListener()) return;

            waitingForRepair = true;
            Debug.Log("[CrackRepairTrigger] 已开始监听裂缝修复完成。", this);
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

        // 不在 OnTriggerExit 取消：玩家离开区域后仍然等待修复完成。
        private void OnRepairCompleted()
        {
            if (completed || !waitingForRepair) return;

            completed = true;
            waitingForRepair = false;
            RemoveListener();

            if (timelineToResume != null) timelineToResume.Resume();
        }

        private void OnDisable()
        {
            RemoveListener();
        }

        private void RemoveListener()
        {
            if (!listening) return;
            if (dispatcher != null)
                dispatcher.RemoveListener(registeredEventName, OnRepairCompleted);
            listening = false;
            dispatcher = null;
        }
    }
}
