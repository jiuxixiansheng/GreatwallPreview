using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Sza.Characters
{
    [TrackColor(0.95f, 0.55f, 0.35f), TrackBindingType(typeof(TimelineCharacterActor)), TrackClipType(typeof(TimelineFaceClip))]
    public sealed class TimelineFaceTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject owner, int inputCount)
        {
            var mixer = ScriptPlayable<TimelineFaceMixer>.Create(graph, inputCount);
            mixer.GetBehaviour().director = owner.GetComponent<PlayableDirector>();
            return mixer;
        }
    }

    public sealed class TimelineFaceMixer : PlayableBehaviour
    {
        public PlayableDirector director;
        private TimelineCharacterActor actor;
        private TimelineFaceBehaviour active;
        private double previousLocalTime;
        private int activeLayer = -1;
        private bool warned;

        public override void OnGraphStart(Playable playable) { active = null; }

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            actor = playerData as TimelineCharacterActor;
            if (actor == null || actor.animator == null || !actor.gameObject.activeInHierarchy) return;
            TimelineFaceBehaviour next = null;
            double localTime = 0d;
            // ClipIn is supported; overlapping clips are intentionally not blended.
            for (int i = 0; i < playable.GetInputCount(); i++)
            {
                if (playable.GetInputWeight(i) <= 0f) continue;
                var input = (ScriptPlayable<TimelineFaceBehaviour>)playable.GetInput(i);
                next = input.GetBehaviour(); localTime = input.GetTime();
            }
            if (next == null) { Release(); return; }
            var animator = actor.animator;
            int separator = string.IsNullOrEmpty(next.statePath) ? -1 : next.statePath.IndexOf('.');
            int layer = separator < 0 ? -1 : animator.GetLayerIndex(next.statePath.Substring(0, separator));
            int hash = Animator.StringToHash(next.statePath ?? "");
            if (layer < 0 || !animator.HasState(layer, hash))
            {
                if (!warned) Debug.LogWarning("脸部轨道的 State Path 不存在：" + next.statePath, actor);
                warned = true; return;
            }
            float seconds = Mathf.Max(0f, (float)localTime);
            if (next.length > 0f)
                seconds = next.loop ? Mathf.Repeat(seconds, next.length) : Mathf.Min(seconds, Mathf.Max(0f, next.length - 0.00001f));
            bool changed = !ReferenceEquals(active, next) || info.seekOccurred || localTime < previousLocalTime - 0.05d ||
                System.Math.Abs(localTime - previousLocalTime - info.deltaTime) > 0.5d;
            animator.SetLayerWeight(layer, 1f);
            if (!Application.isPlaying)
            {
                animator.Play(hash, layer, next.length > 0f ? seconds / next.length : 0f);
                animator.Update(0f);
            }
            else if (changed)
            {
                // Resume/seek starts at the clip's local time, while ordinary playback advances at1x.
                animator.CrossFadeInFixedTime(hash, next.crossFadeSeconds, layer, seconds);
            }
            active = next; activeLayer = layer; previousLocalTime = localTime;
        }

        public override void OnGraphStop(Playable playable)
        {
            if (director != null && director.time > 0d && director.time < director.duration - 0.0001d) return;
            Release();
        }

        public override void OnPlayableDestroy(Playable playable) => Release();

        private void Release()
        {
            if (active == null) return;
            if (actor != null && actor.gameObject.activeInHierarchy && actor.animator != null && activeLayer >= 0)
            {
                var animator = actor.animator;
                int neutral = Animator.StringToHash(animator.GetLayerName(activeLayer) + ".Idle");
                if (animator.HasState(activeLayer, neutral))
                    animator.CrossFadeInFixedTime(neutral, active.crossFadeSeconds, activeLayer, 0f);
            }
            active = null;
        }
    }
}
