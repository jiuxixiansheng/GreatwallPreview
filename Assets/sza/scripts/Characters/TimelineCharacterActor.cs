using Greatwall.Navigation;
using Greatwall.VRAnimation.Timeline;
using Mirror;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Playables;

namespace Sza.Characters
{
    [DisallowMultipleComponent, DefaultExecutionOrder(-500)]
    public sealed class TimelineCharacterActor : MonoBehaviour
    {
        public string actorId = "luo_zhewen";
        public NavMeshAgent agent;
        public DragonNavMeshMover mover;
        public Animator animator;
        public CharacterTimelineAnimator timelineAnimator;
        public DistanceDrivenLocomotion locomotion;
        public Transform visualRoot;
        public AudioSource voice;
        public Transform spawnPoint;
        public PlayableDirector director;
        [Min(0.1f)] public float groundSearchDistance = 1.5f;

        public bool IsPaused { get; private set; }
        private bool resumeVoice, resumeAgent, animatorFrozen;
        private StoryTimelineManager story;
        private bool IsChapterTransitionActive =>
            (NetworkGameplayCoordinator.Instance?.story ?? story)?.IsChapterTransitionActive == true;
        private float previousAnimatorSpeed = 1f;
        private static bool Follower => NetworkClient.active && !NetworkServer.active;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            IsPaused = false;
            if (story == null)
                story = FindFirstObjectByType<StoryTimelineManager>();
            if (mover != null) mover.onMoveStarted.AddListener(ResetVisualTurn);
            if (!Follower)
            {
                if (spawnPoint != null) transform.SetPositionAndRotation(spawnPoint.position, spawnPoint.rotation);
                ResetVisualTurn();
                RestoreLocalNavigation();
            }
            else if (agent != null) agent.enabled = false;
            if (animator != null) { animator.Rebind(); animator.Update(0f); }
            if (director != null)
            {
                director.paused += OnDirectorPaused;
                director.played += OnDirectorPlayed;
            }
        }

        private void OnDirectorPaused(PlayableDirector source) => RefreshLocalPause();
        private void OnDirectorPlayed(PlayableDirector source) => RefreshLocalPause();

        private void Update()
        {
            if (Follower) { if (agent != null && agent.enabled) agent.enabled = false; return; }
            RefreshLocalPause();
        }

        private void RefreshLocalPause()
        {
            if (Follower) return;
            bool transitioning = IsChapterTransitionActive;
            SetPaused(transitioning || (director != null && director.state != PlayState.Playing), transitioning);
        }

        public void ApplyNetworkPause(bool paused) => SetPaused(paused, paused && IsChapterTransitionActive);

        private void SetPaused(bool paused, bool freezeAnimator = false)
        {
            // An interaction can become a chapter transition without the Director resuming in between.
            if (animatorFrozen != freezeAnimator)
            {
                if (animator != null)
                {
                    if (freezeAnimator) { previousAnimatorSpeed = animator.speed; animator.speed = 0f; }
                    else animator.speed = previousAnimatorSpeed;
                }
                animatorFrozen = freezeAnimator;
            }

            if (IsPaused == paused) return;
            IsPaused = paused;
            if (paused)
            {
                resumeVoice = voice != null && voice.isPlaying;
                if (resumeVoice) voice.Pause();
                resumeAgent = agent != null && agent.enabled && agent.isOnNavMesh && !agent.isStopped;
                if (resumeAgent) agent.isStopped = true;
            }
            else
            {
                if (resumeVoice && voice != null) voice.UnPause();
                if (!Follower && resumeAgent && agent != null && agent.enabled && agent.isOnNavMesh && agent.hasPath)
                    agent.isStopped = false;
                resumeVoice = resumeAgent = false;
            }
        }

        public void RestoreLocalNavigation()
        {
            if (Follower || agent == null || !gameObject.activeInHierarchy) return;
            if (agent.enabled && agent.isOnNavMesh) return;
            agent.enabled = false;
            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            if (!NavMesh.SamplePosition(transform.position, out var hit, groundSearchDistance, filter)) return;
            transform.position = hit.position;
            agent.enabled = true;
            agent.Warp(hit.position);
        }

        private void ResetVisualTurn()
        {
            if (visualRoot != null) visualRoot.localRotation = Quaternion.identity;
        }

        private void OnDisable()
        {
            if (!Application.isPlaying) return;
            if (director != null)
            {
                director.paused -= OnDirectorPaused;
                director.played -= OnDirectorPlayed;
            }
            if (mover != null) { mover.onMoveStarted.RemoveListener(ResetVisualTurn); mover.StopMoving(); }
            SetPaused(false);
            if (agent != null) agent.enabled = false;
            if (voice != null) voice.Stop();
        }

        private void OnDrawGizmosSelected()
        {
            if (spawnPoint == null) return;
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 1f);
            Gizmos.DrawWireSphere(spawnPoint.position, 0.2f);
            Gizmos.DrawRay(spawnPoint.position, spawnPoint.forward * 0.6f);
        }
    }
}
