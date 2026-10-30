using Mirror;
using UnityEngine;

public enum GameplayRequestKind : byte { Snapshot, Chapter, Pause, Resume, DebugSolve }

public struct GameplayRequestMessage : NetworkMessage
{
    public GameplayRequestKind kind;
    public int chapter;
    public int argument;
    public uint configuration;
}

public struct GameplayVisualState
{
    public bool distanceSet, restoreSet;
    public float distance, restore;
    public int shadows;
}

public struct GameplayAnimatorParameter
{
    public int hash;
    public bool isBool, boolValue;
    public float floatValue;
}

public struct GameplaySnapshotMessage : NetworkMessage
{
    public uint configuration, sequence;
    public int chapter;
    public double serverTime, timelineTime;
    public bool playing, transitioning;
    public bool[] solved, objectActive;
    public GameplayVisualState[] visuals;
    public Vector3 characterPosition;
    public Quaternion characterRotation;
    public GameplayAnimatorParameter[] parameters;
}
