using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Sza.Bow
{
    [TrackColor(1f, 0.78f, 0.1f), TrackClipType(typeof(MagicBowClip)), TrackBindingType(typeof(MagicBowDirector))]
    public sealed class MagicBowTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            var playable = ScriptPlayable<MagicBowMixer>.Create(graph, inputCount);
            foreach (var clip in GetClips()) { playable.GetBehaviour().start = clip.start; break; }
            playable.GetBehaviour().director = graph.GetResolver() as PlayableDirector;
            return playable;
        }
    }

    public sealed class MagicBowMixer : PlayableBehaviour
    {
        public double start;
        public PlayableDirector director;
        private MagicBowDirector target;
        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            target = playerData as MagicBowDirector;
            if (target == null || director == null) return;
            double time = director.time - start;
            if (time < -0.00001d) target.ResetDemo();
            else target.Evaluate((float)System.Math.Max(0d, time));
        }
        public override void OnPlayableDestroy(Playable playable)
        {
            if (!Application.isPlaying && target != null) target.ResetDemo();
        }
    }
}
