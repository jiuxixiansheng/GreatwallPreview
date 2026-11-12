using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Sza.Characters
{
    [TrackColor(0.45f, 0.35f, 0.95f), TrackBindingType(typeof(CharacterPortalEntrance)), TrackClipType(typeof(CharacterPortalClip))]
    public sealed class CharacterPortalTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject owner, int inputCount)
        {
            var mixer = ScriptPlayable<CharacterPortalMixer>.Create(graph, inputCount);
            mixer.GetBehaviour().director = owner.GetComponent<PlayableDirector>();
            return mixer;
        }
    }

    public sealed class CharacterPortalMixer : PlayableBehaviour
    {
        public PlayableDirector director;
        private CharacterPortalEntrance entrance;
        private bool subscribed;

        public override void OnGraphStart(Playable playable)
        {
            if (director == null || subscribed) return;
            director.stopped += OnDirectorStopped;
            subscribed = true;
        }

        private void OnDirectorStopped(PlayableDirector source)
        {
            if (entrance != null) entrance.Release();
        }

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            var next = playerData as CharacterPortalEntrance;
            if (entrance != next && entrance != null) entrance.Release();
            entrance = next;
            if (entrance == null) return;
            if (!entrance.isActiveAndEnabled) { entrance.Release(); return; }
            for (int i = playable.GetInputCount() - 1; i >= 0; i--)
            {
                if (playable.GetInputWeight(i) <= 0f) continue;
                var input = (ScriptPlayable<CharacterPortalBehaviour>)playable.GetInput(i);
                if (input.GetTime() < 0d || input.GetTime() >= input.GetDuration()) continue;
                var data = input.GetBehaviour();
                entrance.Evaluate(input.GetTime(), input.GetDuration(), data.openingSeconds, data.closingSeconds);
                return;
            }
            entrance.Release();
        }

        public override void OnPlayableDestroy(Playable playable)
        {
            if (director != null && subscribed) director.stopped -= OnDirectorStopped;
            subscribed = false;
            if (entrance != null) entrance.Release();
        }
    }
}
