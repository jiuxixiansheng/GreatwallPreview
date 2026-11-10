using UnityEngine;
using UnityEngine.Timeline;

namespace Greatwall.VRAnimation.Timeline
{
    [TrackColor(0.95f, 0.55f, 0.35f)]
    [TrackBindingType(typeof(CharacterTimelineAnimator))]
    [TrackClipType(typeof(FaceActionClip))]
    public sealed class FaceActionTrack : TrackAsset
    {
    }
}
