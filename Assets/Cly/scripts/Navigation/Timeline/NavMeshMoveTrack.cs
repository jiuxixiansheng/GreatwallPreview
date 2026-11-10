using UnityEngine;
using UnityEngine.Timeline;

namespace Greatwall.Navigation.Timeline
{
    [TrackColor(0.2f, 0.8f, 0.45f)]
    [TrackBindingType(typeof(DragonNavMeshMover))]
    [TrackClipType(typeof(NavMeshMoveClip))]
    public sealed class NavMeshMoveTrack : TrackAsset
    {
    }
}
