using System.Collections;
using System.Collections.Generic;
using kcp2k;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class NetworkData : MonoBehaviour
{
    [Header("场馆配置")]
    [Tooltip("如果是服务器端，将使用此相机作为上帝视角；如果是客户端，将销毁此相机避免冲突")]
    [SerializeField] private GameObject _observerCamera = null;

    private void Start()
    {

        // 2. 动态修改底层的网络传输端口
        // 这里默认使用了 Mirror 官方推荐的 KCP 传输协议（基于 UDP，非常适合 VR 的低延迟需求）
        var transport = NetworkManager.singleton.GetComponent<KcpTransport>();
        if (transport != null)
        {
            // ⚠️ TA 避坑提示：在较新的 Mirror 版本中，这个属性可能叫首字母大写的 Port
            transport.port = ConnectionManager.singleton.connectionPort;
        }

        // 3. 根据上一幕记录的连接类型，执行真正的网络连接逻辑
        if (ConnectionManager.singleton.connectionType == ConnectionManager.Type.Server)
        {
            // 截图原版代码为纯后台服务器
            NetworkManager.singleton.StartServer();

            // 💡 如果你的场馆主控电脑需要看上帝视角/导播画面，建议注释掉上面那句，换成下面这句：
            // NetworkManager.singleton.StartHost(); 
        }
        else if (ConnectionManager.singleton.connectionType == ConnectionManager.Type.Client)
        {
            // ==========================================
            // 客户端逻辑：直接拔掉导播相机，把渲染权交给马上要生成的 VR 玩家
            // ==========================================
            if (_observerCamera != null)
            {
                Destroy(_observerCamera);
            }
            // 客户端需要先把记事本里的 IP 填给管理器，然后再启动连接
            NetworkManager.singleton.networkAddress = ConnectionManager.singleton.connectionIP;
            NetworkManager.singleton.StartClient();
        }
    }

    // ==========================================
    // 截图中未展示的清理逻辑：安全退出游戏并返回主菜单
    // ==========================================

}