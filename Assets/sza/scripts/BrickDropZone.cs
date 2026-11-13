using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class BrickDropZone : MonoBehaviour
{
    [Header("解谜设置")]
    [Min(1)] public int targetBrickCount = 4;
    public string targetTag = "Brick";
    public List<GameObject> targetObjects = new List<GameObject>();
    [Header("Timeline 交互锁")]
    public bool waitForTimelineSignal;
    [Tooltip("只允许这些砖块参与修复；留空时按标签判断。")]
    public Rigidbody[] repairBricks = new Rigidbody[0];
    private readonly Dictionary<Rigidbody, HashSet<Collider>> bricksInside =
        new Dictionary<Rigidbody, HashSet<Collider>>();
    private bool isSolved;
    private bool repairUnlocked;
    private readonly HashSet<Rigidbody> accepted = new HashSet<Rigidbody>();
    private readonly HashSet<Rigidbody> earlyPlacements = new HashSet<Rigidbody>();
    public bool IsSolved => isSolved;
    public bool IsRepairUnlocked => !waitForTimelineSignal || repairUnlocked;
    public int CurrentBrickCount => accepted.Count;
    public event System.Action<BrickDropZone, Rigidbody> BrickAccepted;
    public event System.Action<BrickDropZone> Solved;

    private bool CanJudge => !NetworkClient.active || NetworkServer.active;

    private void OnTriggerEnter(Collider other) => TrackBrick(other);
    private void OnTriggerStay(Collider other) => TrackBrick(other);

    private void TrackBrick(Collider other)
    {
        if (isSolved || !CanJudge) return;
        Rigidbody brick = other.attachedRigidbody;
        if (brick == null || (!other.CompareTag(targetTag) && !brick.CompareTag(targetTag))) return;
        if (NetworkGameplayCoordinator.IsManaged &&
            !NetworkGameplayCoordinator.Instance.IsRegisteredBrick(brick)) return;
        if (repairBricks.Length > 0 && System.Array.IndexOf(repairBricks, brick) < 0) return;
        if (!bricksInside.TryGetValue(brick, out var colliders))
        {
            colliders = new HashSet<Collider>();
            bricksInside.Add(brick, colliders);
        }
        colliders.Add(other);
        if (IsHeld(brick))
        {
            accepted.Remove(brick);
            earlyPlacements.Remove(brick);
            return;
        }
        if (!IsRepairUnlocked)
        {
            earlyPlacements.Add(brick);
            return;
        }
        // A brick placed before the pause signal must be recovered or picked up again.
        if (earlyPlacements.Contains(brick) || !accepted.Add(brick)) return;
        BrickAccepted?.Invoke(this, brick);
        Debug.Log($"[修墙] 当前砖块：{accepted.Count}/{targetBrickCount}", this);
        if (accepted.Count >= targetBrickCount)
        {
            if (NetworkGameplayCoordinator.IsManaged && NetworkServer.active) NetworkGameplayCoordinator.Instance.ServerSolve(this);
            else ApplySolvedState(true);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (isSolved || !CanJudge) return;
        var brick = other.attachedRigidbody;
        if (brick == null || !bricksInside.TryGetValue(brick, out var colliders)) return;
        colliders.Remove(other);
        if (colliders.Count == 0) { bricksInside.Remove(brick); accepted.Remove(brick); }
    }

    private void OnDisable() { bricksInside.Clear(); accepted.Clear(); }

    public void BeginRepair()
    {
        if (!CanJudge || isSolved) return;
        repairUnlocked = true;
    }

    public void ApplyUnlockState(bool value) => repairUnlocked = value;
    public bool HasEarlyPlacement(Rigidbody brick) => earlyPlacements.Contains(brick);
    public bool ContainsBrick(Rigidbody brick) => bricksInside.ContainsKey(brick);
    public bool HasAcceptedBrick(Rigidbody brick) => accepted.Contains(brick);
    public void ClearEarlyPlacement(Rigidbody brick) => earlyPlacements.Remove(brick);
    public void ForgetBrick(Rigidbody brick)
    {
        bricksInside.Remove(brick);
        accepted.Remove(brick);
        earlyPlacements.Remove(brick);
    }
    public static bool IsHeld(Rigidbody brick)
    {
        var network = brick.GetComponent<NetworkGrabbable>();
        var grab = brick.GetComponent<XRGrabInteractable>();
        return (network != null && network.IsHeld) || (grab != null && grab.isSelected);
    }

    public void ForceSolvePuzzle()
    {
        if (isSolved || !IsRepairUnlocked) return;
        if (NetworkGameplayCoordinator.IsManaged && (NetworkServer.active || NetworkClient.active))
            NetworkGameplayCoordinator.Instance.RequestDebugSolve(this);
        else ApplySolvedState(true);
    }

    public void ApplySolvedState(bool dispatchEvent)
    {
        bool firstApplication = !isSolved;
        isSolved = true;
        if (firstApplication && dispatchEvent) Solved?.Invoke(this);
        if (firstApplication && dispatchEvent) EventDispatcher.Instance.Dispatch("修复城墙结束");
        foreach (var obj in targetObjects) if (obj != null) obj.SetActive(false);
        gameObject.SetActive(false);
    }
}
