using System;
using Mirror;
using UnityEngine;

/// <summary>Host/offline recovery only; the normal network grab and transform own the brick.</summary>
[DisallowMultipleComponent]
public sealed class BrickRecovery : MonoBehaviour
{
    public BrickDropZone repairZone;
    [Tooltip("可编辑 Center / Size；只用于范围计算，不需要启用碰撞。")]
    public BoxCollider allowedArea;
    public NetworkGrabbable[] bricks = Array.Empty<NetworkGrabbable>();
    [Min(0f)] public float returnDelay = 4f;

    private sealed class Entry
    {
        public NetworkGrabbable grab;
        public Rigidbody body;
        public Transform parent;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public float waitingSince = -1f;
    }
    private Entry[] entries;

    private void Awake()
    {
        entries = new Entry[bricks.Length];
        for (int i = 0; i < bricks.Length; i++)
        {
            var brick = bricks[i];
            if (brick == null) continue;
            entries[i] = new Entry { grab = brick, body = brick.GetComponent<Rigidbody>(),
                parent = brick.transform.parent, localPosition = brick.transform.localPosition,
                localRotation = brick.transform.localRotation };
        }
    }

    public bool IsInsideAllowedArea(Vector3 position)
    {
        if (allowedArea == null) return true;
        Vector3 p = allowedArea.transform.InverseTransformPoint(position) - allowedArea.center;
        Vector3 half = allowedArea.size * 0.5f;
        return Mathf.Abs(p.x) <= half.x && Mathf.Abs(p.y) <= half.y && Mathf.Abs(p.z) <= half.z;
    }

    private void Update()
    {
        if ((NetworkClient.active && !NetworkServer.active) || repairZone == null || repairZone.IsSolved) return;
        foreach (var entry in entries)
        {
            if (entry == null || entry.grab == null || !entry.grab.gameObject.activeInHierarchy) continue;
            if (BrickDropZone.IsHeld(entry.body))
            {
                entry.waitingSince = -1f;
                repairZone.ClearEarlyPlacement(entry.body);
                continue;
            }
            bool early = repairZone.HasEarlyPlacement(entry.body) ||
                (!repairZone.IsRepairUnlocked && repairZone.ContainsBrick(entry.body));
            bool lost = !IsInsideAllowedArea(entry.body.worldCenterOfMass) &&
                !repairZone.HasAcceptedBrick(entry.body);
            if (!early && !lost) { entry.waitingSince = -1f; continue; }
            if (entry.waitingSince < 0f) entry.waitingSince = Time.unscaledTime;
            if (Time.unscaledTime - entry.waitingSince < returnDelay) continue;
            Vector3 position = entry.parent != null ? entry.parent.TransformPoint(entry.localPosition) : entry.localPosition;
            Quaternion rotation = entry.parent != null ? entry.parent.rotation * entry.localRotation : entry.localRotation;
            if (!entry.grab.TryReturnToPose(position, rotation)) continue;
            repairZone.ForgetBrick(entry.body);
            entry.waitingSince = -1f;
        }
    }

    private void OnDisable()
    {
        if (entries != null) foreach (var entry in entries) if (entry != null) entry.waitingSince = -1f;
    }

    private void OnDrawGizmosSelected()
    {
        if (allowedArea == null) return;
        Gizmos.matrix = allowedArea.transform.localToWorldMatrix;
        Gizmos.color = new Color(0.1f, 0.9f, 1f, 0.08f);
        Gizmos.DrawCube(allowedArea.center, allowedArea.size);
        Gizmos.color = new Color(0.1f, 0.9f, 1f, 1f);
        Gizmos.DrawWireCube(allowedArea.center, allowedArea.size);
        Gizmos.matrix = Matrix4x4.identity;
    }
}
