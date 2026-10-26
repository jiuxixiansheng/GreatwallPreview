using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Greatwall.VRAnimation.Timeline
{
    [TrackColor(0.4f, 0.8f, 0.5f)]
    [TrackBindingType(typeof(CharacterTimelineAnimator))]
    [TrackClipType(typeof(UpperBodyActionClip))]
    public sealed class UpperBodyActionTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            var mixer = ScriptPlayable<UpperBodyActionMixerBehaviour>.Create(graph, inputCount);
            var startTimes = new double[inputCount];
            int index = 0;
            // 与 Timeline 建立片段输入使用相同的 GetClips 顺序。
            foreach (TimelineClip clip in GetClips())
            {
                if (index >= inputCount) break;
                startTimes[index++] = clip.start;
            }
            mixer.GetBehaviour().clipStartTimes = startTimes;
            return mixer;
        }
    }
}
