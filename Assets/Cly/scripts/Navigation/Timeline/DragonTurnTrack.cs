using UnityEngine;
using UnityEngine.Timeline;

namespace Greatwall.Navigation.Timeline
{
    [TrackColor(0.75f, 0.45f, 0.95f)]
    [TrackBindingType(typeof(DragonTurnController))]
    [TrackClipType(typeof(DragonTurnClip))]
    public sealed class DragonTurnTrack : TrackAsset
    {
    }
}
