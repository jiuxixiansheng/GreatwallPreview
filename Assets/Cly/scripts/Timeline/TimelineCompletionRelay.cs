using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Playables;

namespace Greatwall.VRAnimation.Timeline
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayableDirector))]
    public sealed class TimelineCompletionRelay : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayableDirector director;

        [Header("Completion")]
        [SerializeField, Min(0f)] private float completionToleranceSeconds = 0.05f;
        [SerializeField] private bool invokeWhenStoppedEarly;

        [Header("Events")]
        public UnityEvent onTimelineCompleted = new UnityEvent();

        private bool armed;
        private bool invoked;
        private double furthestObservedTime;

        private void Reset()
        {
            director = GetComponent<PlayableDirector>();
        }

        private void Awake()
        {
            if (director == null) director = GetComponent<PlayableDirector>();
        }

        private void OnEnable()
        {
            if (director == null) return;

            director.played += HandlePlayed;
            director.stopped += HandleStopped;

            if (director.state == PlayState.Playing)
                ArmForCurrentPlayback();
            EventDispatcher.Instance.AddListener("ÐÞ¸´³ÇÇ½½áÊø", ResumeTimeline);
        }

        private void OnDisable()
        {
            if (director != null)
            {
                director.played -= HandlePlayed;
                director.stopped -= HandleStopped;
            }

            armed = false;
            EventDispatcher.Instance.RemoveListener("ÐÞ¸´³ÇÇ½½áÊø", ResumeTimeline);
        }

        private void Update()
        {
            if (!armed || invoked || director == null) return;

            furthestObservedTime = System.Math.Max(furthestObservedTime, director.time);
            if (HasReachedTimelineEnd()) InvokeCompleted();
        }

        private void HandlePlayed(PlayableDirector playedDirector)
        {
            if (playedDirector != director) return;
            ArmForCurrentPlayback();
        }

        private void HandleStopped(PlayableDirector stoppedDirector)
        {
            if (stoppedDirector != director || !armed) return;

            furthestObservedTime = System.Math.Max(furthestObservedTime, director.time);
            if (!invoked && (HasReachedTimelineEnd() || invokeWhenStoppedEarly))
                InvokeCompleted();

            armed = false;
        }

        private void ArmForCurrentPlayback()
        {
            armed = true;
            invoked = false;
            furthestObservedTime = director != null ? director.time : 0d;
        }

        private bool HasReachedTimelineEnd()
        {
            if (director == null || director.duration <= 0d) return false;
            return furthestObservedTime >=
                   director.duration - completionToleranceSeconds;
        }

        private void InvokeCompleted()
        {
            if (invoked) return;

            invoked = true;
            onTimelineCompleted?.Invoke();
        }

        public void PauseTimeline()
        {
            if (NetworkGameplayCoordinator.IsManaged)
            {
                NetworkGameplayCoordinator.Instance.HandleTimelineSignal(director, false);
                return;
            }
            director.Pause();
        }

        private void ResumeTimeline()
        {
            if (NetworkGameplayCoordinator.IsManaged)
            {
                NetworkGameplayCoordinator.Instance.HandleTimelineSignal(director, true);
                return;
            }
            director.Resume();
        }
    }
}
