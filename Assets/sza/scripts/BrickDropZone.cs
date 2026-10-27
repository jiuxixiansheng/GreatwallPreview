using System.Collections.Generic;
using Mirror;
using UnityEngine;

public class BrickDropZone : MonoBehaviour
{
    [Header("解谜设置")]
    [Min(1)] public int targetBrickCount = 3;
    public string targetTag = "Brick";
    public List<GameObject> targetObjects = new List<GameObject>();
    private readonly Dictionary<Rigidbody, HashSet<Collider>> bricksInside =
        new Dictionary<Rigidbody, HashSet<Collider>>();
    private bool isSolved;
    public bool IsSolved => isSolved;
    public int CurrentBrickCount => bricksInside.Count;

    private bool CanJudge => !NetworkGameplayCoordinator.IsManaged || NetworkServer.active;

    private void OnTriggerEnter(Collider other)
    {
        if (isSolved || !CanJudge) return;
        Rigidbody brick = other.attachedRigidbody;
        if (brick == null || (!other.CompareTag(targetTag) && !brick.CompareTag(targetTag))) return;
        if (NetworkGameplayCoordinator.IsManaged &&
            !NetworkGameplayCoordinator.Instance.IsRegisteredBrick(brick)) return;
        if (!bricksInside.TryGetValue(brick, out var colliders))
        {
            colliders = new HashSet<Collider>();
            bricksInside.Add(brick, colliders);
        }
        colliders.Add(other);
        Debug.Log($"[修墙] 当前砖块：{bricksInside.Count}/{targetBrickCount}", this);
        if (bricksInside.Count >= targetBrickCount)
        {
            if (NetworkGameplayCoordinator.IsManaged) NetworkGameplayCoordinator.Instance.ServerSolve(this);
            else ApplySolvedState(true);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (isSolved || !CanJudge) return;
        var brick = other.attachedRigidbody;
        if (brick == null || !bricksInside.TryGetValue(brick, out var colliders)) return;
        colliders.Remove(other);
        if (colliders.Count == 0) bricksInside.Remove(brick);
    }

    private void OnDisable() => bricksInside.Clear();

    public void ForceSolvePuzzle()
    {
        if (isSolved) return;
        if (NetworkGameplayCoordinator.IsManaged) NetworkGameplayCoordinator.Instance.RequestDebugSolve(this);
        else ApplySolvedState(true);
    }

    public void ApplySolvedState(bool dispatchEvent)
    {
        bool firstApplication = !isSolved;
        isSolved = true;
        if (firstApplication && dispatchEvent) EventDispatcher.Instance.Dispatch("修复城墙结束");
        foreach (var obj in targetObjects) if (obj != null) obj.SetActive(false);
        gameObject.SetActive(false);
    }
}
