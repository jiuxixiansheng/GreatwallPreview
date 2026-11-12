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
        private bool resumeVoice, resumeAgent;
        private float previousAnimatorSpeed = 1f;
        private static bool Follower => NetworkClient.active && !NetworkServer.active;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            IsPaused = false;
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

        private void OnDirectorPaused(PlayableDirector source) => SetPaused(true);
        private void OnDirectorPlayed(PlayableDirector source) => SetPaused(false);

        private void Update()
        {
            if (Follower) { if (agent != null && agent.enabled) agent.enabled = false; return; }
            bool paused = director != null && director.state != PlayState.Playing;
            var coordinator = NetworkGameplayCoordinator.Instance;
            if (coordinator != null && coordinator.story != null) paused |= coordinator.story.IsTransitionPending;
            SetPaused(paused);
        }

        public void ApplyNetworkPause(bool paused) => SetPaused(paused);

        private void SetPaused(bool paused)
        {
            if (IsPaused == paused) return;
            IsPaused = paused;
            if (paused)
            {
                if (animator != null) { previousAnimatorSpeed = animator.speed; animator.speed = 0f; }
                resumeVoice = voice != null && voice.isPlaying;
                if (resumeVoice) voice.Pause();
                resumeAgent = agent != null && agent.enabled && agent.isOnNavMesh && !agent.isStopped;
                if (resumeAgent) agent.isStopped = true;
            }
            else
            {
                if (animator != null) animator.speed = previousAnimatorSpeed;
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
