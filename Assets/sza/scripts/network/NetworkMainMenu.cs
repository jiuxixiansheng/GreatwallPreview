using UnityEngine;
using UnityEngine.UI;
using Mirror;
using UnityEngine.SceneManagement; // 【新增】引入 Mirror 网络库命名空间

public class NetworkMainMenu : MonoBehaviour
{
    [Header("UI 按钮引用")]
    [SerializeField] private Button serverButton = null;
    [SerializeField] private Button clientButton = null;

    private void Start()
    {
        // ==========================================
        // 【自动执行逻辑】
        // 如果你打包的是“专用服务器 (Dedicated Server)”版本，
        // 游戏一运行就会跳过 UI，直接静默启动服务器。
        // ==========================================
#if !UNITY_EDITOR && UNITY_SERVER
        OnServerClicked();
        return;
#endif

        // ==========================================
        // 【按钮事件绑定】
        // 这种代码绑定的方式比在 Inspector 里拖拽 OnClick 更安全，不容易丢失
        // ==========================================
        if (serverButton != null)
            serverButton.onClick.AddListener(OnServerClicked);

        if (clientButton != null)
            clientButton.onClick.AddListener(OnClientClicked);
    }

    // 点击“作为服务器启动”时执行
    private void OnServerClicked()
    {
        ConnectionManager.singleton.InitializeAsServer(5678);
        SceneManager.LoadScene(1);
    }

    // 点击“作为客户端连接”时执行
    private void OnClientClicked()
    {
        ConnectionManager.singleton.InitializeAsClient("192.168.31.82",5678);
        SceneManager.LoadScene(1);
    }

    private void OnDestroy()
    {
        // 良好的防内存泄漏习惯：对象销毁时注销监听事件
        if (serverButton != null)
            serverButton.onClick.RemoveListener(OnServerClicked);

        if (clientButton != null)
            clientButton.onClick.RemoveListener(OnClientClicked);
    }
}