using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Greatwall.VRAnimation.Timeline
{
    [TrackColor(0.75f, 0.45f, 0.9f)]
    [TrackBindingType(typeof(CharacterTimelineAnimator))]
    [TrackClipType(typeof(RightHandActionClip))]
    public sealed class RightHandActionTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject owner, int inputCount)
        {
            var playable = ScriptPlayable<RightHandActionMixerBehaviour>.Create(graph, inputCount);
            var mixer = playable.GetBehaviour();
            mixer.director = owner.GetComponent<PlayableDirector>();
            mixer.clipStartTimes = new double[inputCount];
            int index = 0;
            foreach (var clip in GetClips())
            {
                if (index >= inputCount) break;
                mixer.clipStartTimes[index++] = clip.start;
            }
            return playable;
        }
    }
}
