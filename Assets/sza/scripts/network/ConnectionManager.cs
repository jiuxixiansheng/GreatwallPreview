using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ConnectionManager : MonoBehaviour
{
    // ==========================================
    // 单例模式 (Singleton)
    // ==========================================
    private static ConnectionManager _singleton = null;
    public static ConnectionManager singleton { get { return _singleton; } }

    // ==========================================
    // 属性配置
    // ==========================================
    private string _connectionIP = "";
    public string connectionIP { get { return _connectionIP; } }

    private ushort _connectionPort = 0;
    public ushort connectionPort { get { return _connectionPort; } }

    private Type _connectionType = Type.None;
    public Type connectionType { get { return _connectionType; } }

    // 连接类型枚举
    public enum Type
    {
        None = 0,
        Client = 1,
        Server = 2
    }

    // ==========================================
    // 生命周期
    // ==========================================
    private void Awake()
    {
        // 经典的单例初始化防重复逻辑
        if (_singleton == null)
        {
            _singleton = this;
            DontDestroyOnLoad(gameObject); // 保证切换场景时不被销毁
        }
        else
        {
            Destroy(gameObject); // 如果场景里已经有一个了，就销毁自己，防止出现“双胞胎”
        }
    }

    // ==========================================
    // 初始化方法
    // ==========================================

    // 作为客户端初始化
    public void InitializeAsClient(string ip, ushort port)
    {
        _connectionIP = ip;
        _connectionPort = port;
        _connectionType = Type.Client;
    }

    // 作为服务器初始化 (服务器不需要知道要连哪个IP，只要知道开哪个端口就行)
    public void InitializeAsServer(ushort port)
    {
        _connectionPort = port;
        _connectionType = Type.Server;
    }
}