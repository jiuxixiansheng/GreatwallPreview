using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Greatwall.VRAnimation.Timeline
{
    [TrackColor(0.95f, 0.55f, 0.35f)]
    [TrackBindingType(typeof(CharacterTimelineAnimator))]
    [TrackClipType(typeof(FaceActionClip))]
    public sealed class FaceActionTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject owner, int inputCount)
        {
            var playable = ScriptPlayable<FaceActionMixerBehaviour>.Create(graph, inputCount);
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
