using UnityEngine;
using UnityEngine.Playables;

namespace Greatwall.VRAnimation.Timeline
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider), typeof(Rigidbody))]
    public sealed class CrackRepairTrigger : MonoBehaviour
    {
        [Header("允许触发监听的物体")]
        [Tooltip("在编辑模式下拖入场景物体。任意一个物体自身或子物体的碰撞体进入盒子后，开始监听；数组为空时不触发。")]
        [SerializeField] private GameObject[] triggerObjects = new GameObject[0];

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
            // 兼容组件启用时目标物体已经在盒内，不依赖再次跨过边界。
            TryBeginWaiting(other);
        }

        private void TryBeginWaiting(Collider other)
        {
            if (!isActiveAndEnabled || completed || waitingForRepair) return;

            if (!MatchesTriggerObject(other)) return;
            if (!RegisterListener()) return;

            waitingForRepair = true;
            Debug.Log("[CrackRepairTrigger] 已开始监听裂缝修复完成。", this);
        }

        private bool MatchesTriggerObject(Collider other)
        {
            if (triggerObjects == null) return false;

            foreach (GameObject target in triggerObjects)
            {
                if (target == null) continue;
                if (other.transform == target.transform || other.transform.IsChildOf(target.transform))
                    return true;
            }

            return false;
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
