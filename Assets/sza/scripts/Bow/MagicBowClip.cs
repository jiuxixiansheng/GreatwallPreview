using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Sza.Bow
{
    [System.Serializable]
    public sealed class MagicBowClip : PlayableAsset, ITimelineClipAsset
    {
        public ClipCaps clipCaps => ClipCaps.None;
        public override double duration => 8d;
        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner) =>
            Playable.Create(graph);
    }
}
