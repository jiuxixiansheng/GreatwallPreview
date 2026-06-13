using Mirror;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(NetworkIdentity))]
[RequireComponent(typeof(XRGrabInteractable))]
public class NetworkGrabbable : NetworkBehaviour
{
    private XRGrabInteractable _grabInteractable;

    void Awake()
    {
        _grabInteractable = GetComponent<XRGrabInteractable>();

        // 绑定 XR 交互的生命周期事件
        _grabInteractable.selectEntered.AddListener(OnGrab);
        _grabInteractable.selectExited.AddListener(OnRelease);
    }

    private void OnGrab(SelectEnterEventArgs args)
    {
        // 如果我只是个客户端，我必须向服务器讨要权限
        if (!isServer)
        {
            CmdRequestAuthority();
        }
    }

    private void OnRelease(SelectExitEventArgs args)
    {
        // 松手时，把权限还回去
        if (!isServer)
        {
            CmdRemoveAuthority();
        }
    }

    // ==========================================
    // 跨网通信：向服务器发号施令
    // [RequiresAuthority = false] 极其关键！因为在抓取前，客户端是没有权限的，必须允许“越权”发送这个请求。
    // ==========================================
    [Command(requiresAuthority = false)]
    private void CmdRequestAuthority(NetworkConnectionToClient sender = null)
    {
        // 服务器收到请求：把这个物体的控制权，强行分配给刚才伸手抓它的那个客户端
        netIdentity.AssignClientAuthority(sender);
    }

    [Command(requiresAuthority = false)]
    private void CmdRemoveAuthority(NetworkConnectionToClient sender = null)
    {
        // 服务器收回控制权
        netIdentity.RemoveClientAuthority();
    }

    void OnDestroy()
    {
        if (_grabInteractable != null)
        {
            _grabInteractable.selectEntered.RemoveListener(OnGrab);
            _grabInteractable.selectExited.RemoveListener(OnRelease);
        }
    }
}