using UnityEngine;
using Mirror;
using kcp2k;

[RequireComponent(typeof(NetworkManager), typeof(KcpTransport))]
public class NetworkSceneStarter : MonoBehaviour
{
    private void Start()
    {
        ConnectionManager config = ConnectionManager.singleton;

        if (config == null ||
            config.connectionType == ConnectionManager.Type.None ||
            config.connectionPort == 0)
        {
            Debug.LogError("没有有效的连接配置，请从 Menu 场景启动。");
            return;
        }

        NetworkManager manager = GetComponent<NetworkManager>();
        KcpTransport transport = GetComponent<KcpTransport>();

        manager.transport = transport;
        transport.port = config.connectionPort;

        if (config.connectionType == ConnectionManager.Type.Server)
        {
            manager.StartHost();
        }
        else if (config.connectionType == ConnectionManager.Type.Client)
        {
            manager.networkAddress = config.connectionIP;
            manager.StartClient();
        }
    }
}