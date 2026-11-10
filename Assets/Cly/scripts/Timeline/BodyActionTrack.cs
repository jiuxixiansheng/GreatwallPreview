using UnityEngine;
using UnityEngine.Timeline;

namespace Greatwall.VRAnimation.Timeline
{
    [TrackColor(0.25f, 0.65f, 0.95f)]
    [TrackBindingType(typeof(CharacterTimelineAnimator))]
    [TrackClipType(typeof(BodyActionClip))]
    public sealed class BodyActionTrack : TrackAsset
    {
    }
}
