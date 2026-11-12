using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Sza.Characters
{
    [Serializable]
    public sealed class CharacterPortalClip : PlayableAsset, ITimelineClipAsset
    {
        [Min(0f)] public float openingSeconds = 0.45f;
        [Min(0f)] public float closingSeconds = 0.4f;
        public ClipCaps clipCaps => ClipCaps.None;
        public override double duration => 4.4d;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            var playable = ScriptPlayable<CharacterPortalBehaviour>.Create(graph);
            playable.GetBehaviour().openingSeconds = openingSeconds;
            playable.GetBehaviour().closingSeconds = closingSeconds;
            return playable;
        }
    }

    public sealed class CharacterPortalBehaviour : PlayableBehaviour
    {
        public float openingSeconds, closingSeconds;
    }
}
