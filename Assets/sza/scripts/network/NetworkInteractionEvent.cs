using Mirror;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(NetworkIdentity))]
public class NetworkInteractionEvent : NetworkBehaviour
{
    private XRBaseInteractable _interactable;

    void Awake()
    {
        // 获取物体身上的 XRI 交互组件（比如 XRSimpleInteractable 或 XRGrabInteractable）
        _interactable = GetComponent<XRBaseInteractable>();
        _interactable.selectEntered.AddListener(OnLocalInteract);
    }

    // 1. 本地玩家的手柄真实触发了交互
    private void OnLocalInteract(SelectEnterEventArgs args)
    {
        // 只有本地玩家（发起者）才需要去通知服务器。防止别人本地的分身重复发送请求。
        // 注意：XRI 中 args.interactorObject.transform.gameObject 可以拿到是谁触发的，
        // 这里我们可以简单粗暴地直接发送请求。
        CmdTriggerAction();
    }

    // 2. 客户端向服务器发送指令
    // 🚨 极其关键：requiresAuthority = false！
    // 因为场景里的按钮/机关默认是属于服务器的，客户端没有权限。加上这句允许“越权”点击。
    [Command(requiresAuthority = false)]
    private void CmdTriggerAction()
    {
        // 服务器收到指令后，立刻要求全场所有人执行逻辑
        RpcPlayEffect();
    }

    // 3. 服务器广播给所有客户端（包括发起者自己）
    [ClientRpc]
    private void RpcPlayEffect()
    {
        // ==========================================
        // 真正在所有人屏幕上发生的事情写在这里！
        // ==========================================
        Debug.Log("全场注意：有人按下了这个开关！");

        // 比如：播放粒子特效、播放开门动画、生成预制体等
        // GetComponent<ParticleSystem>().Play();
        // GetComponent<AudioSource>().Play();
    }

    void OnDestroy()
    {
        if (_interactable != null)
        {
            _interactable.selectEntered.RemoveListener(OnLocalInteract);
        }
    }
}