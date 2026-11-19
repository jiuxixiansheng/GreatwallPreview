using Mirror;
using UnityEngine;

/// <summary>Shares a hand pose relative to the player root across different XR tracking origins.</summary>
public sealed class NetworkRigHandTransform : NetworkTransformUnreliable
{
    // Camera Offset can differ between the tracked rig and its remote copies.
    protected override Vector3 GetPosition() => transform.InverseTransformPoint(target.position);

    protected override Quaternion GetRotation() => Quaternion.Inverse(transform.rotation) * target.rotation;

    protected override void SetPosition(Vector3 position) => target.position = transform.TransformPoint(position);

    protected override void SetRotation(Quaternion rotation) => target.rotation = transform.rotation * rotation;
}
