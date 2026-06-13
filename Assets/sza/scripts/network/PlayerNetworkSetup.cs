using UnityEngine;
using Mirror;

public class PlayerNetworkSetup : NetworkBehaviour
{
    [Header("1. 玩家的眼睛和耳朵")]
    public Camera localCamera;
    public AudioListener localAudioListener;

    [Header("2. 底盘移动隔离")]
    public MonoBehaviour[] movementScripts;

    [Header("3. 手部追踪隔离")]
    public MonoBehaviour[] handTrackingScripts;

    [Header("4. 物理防卡死")]
    public Collider playerCollider;

    [Header("5. 本地模型穿帮隐藏")]
    public Renderer[] localAvatarMeshes;

    void Start()
    {
        // 🚨 核心改动：只有“既不是本地玩家，又不是服务器”的时候，才执行隔离。
        if (!isLocalPlayer && !isServer)
        {
            // --- 客户端眼里的“别人分身”：严格隔离 ---
            if (localCamera != null) localCamera.enabled = false;
            if (localAudioListener != null) localAudioListener.enabled = false;

            foreach (var script in movementScripts) if (script != null) script.enabled = false;
            foreach (var script in handTrackingScripts) if (script != null) script.enabled = false;
            if (playerCollider != null) playerCollider.enabled = false;

            // 确保客户端能看见别人的模型
            foreach (var mesh in localAvatarMeshes) if (mesh != null) mesh.enabled = true;
            gameObject.name = "Remote_Player_" + netId;
        }
        else
        {
            // --- 本地玩家，或者是服务器：全副武装（不隔离） ---
            if (localCamera != null) localCamera.enabled = true;
            if (localAudioListener != null) localAudioListener.enabled = true;

            foreach (var script in movementScripts) if (script != null) script.enabled = true;
            foreach (var script in handTrackingScripts) if (script != null) script.enabled = true;
            if (playerCollider != null) playerCollider.enabled = true;

            // 针对服务器的特殊处理：服务器虽然不隔离组件，但必须得能看见玩家模型
            if (isServer && !isLocalPlayer)
            {
                foreach (var mesh in localAvatarMeshes) if (mesh != null) mesh.enabled = true;
                gameObject.name = "SERVER_UNISOLATED_PLAYER_" + netId;
            }
            else
            {
                foreach (var mesh in localAvatarMeshes) if (mesh != null) mesh.enabled = false;
                gameObject.name = "★ MY_LOCAL_PLAYER ★";
            }
        }
    }
}