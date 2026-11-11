using System.Collections.Generic;
using Greatwall.CannonGame;
using UnityEngine;

namespace Xcy.Audio
{
    /// <summary>Local feedback for the existing local cannon game; no Timeline or scene bindings.</summary>
    [DisallowMultipleComponent]
    public sealed class CannonInteractionAudio : MonoBehaviour
    {
        public InteractionAudioLibrary library;
        private CannonGame game;
        private CannonChain chain;
        private bool subscribed;
        private readonly List<AudioSource> sources = new List<AudioSource>();

        public void Initialize(CannonGame cannon, CannonChain pull)
        {
            Unsubscribe();
            game = cannon;
            chain = pull;
            ResolveLibrary();
            if (library == null)
                Debug.LogError("[火炮音效] 当前场景的 InteractionAudioPlayer 未配置音效库。", this);
            if (isActiveAndEnabled) Subscribe();
        }

        private void OnEnable() => Subscribe();
        private void ResolveLibrary()
        {
            if (library != null) return;
            // Reuse the same asset as the scene's InteractionAudioSystem, including inactive chapters.
            foreach (var player in FindObjectsByType<InteractionAudioPlayer>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (player.gameObject.scene != gameObject.scene || player.library == null) continue;
                library = player.library;
                break;
            }
        }
        private void OnDisable()
        {
            Unsubscribe();
            foreach (var source in sources) if (source != null) source.Stop();
        }

        private void Subscribe()
        {
            if (subscribed || game == null) return;
            foreach (var ammo in game.Ammo) if (ammo != null) ammo.PickedUp += OnPickup;
            game.AmmoInserted += OnLoad;
            game.Fired += OnFire;
            game.FireworkExploded += OnFirework;
            if (chain != null) chain.PullStarted += OnPull;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;
            if (game != null)
            {
                foreach (var ammo in game.Ammo) if (ammo != null) ammo.PickedUp -= OnPickup;
                game.AmmoInserted -= OnLoad;
                game.Fired -= OnFire;
                game.FireworkExploded -= OnFirework;
            }
            if (chain != null) chain.PullStarted -= OnPull;
            subscribed = false;
        }

        private void OnPickup(CannonAmmo ammo) => Play(InteractionAudioCue.CannonPickup, ammo.transform.position);
        private void OnLoad(CannonAmmo ammo) => Play(InteractionAudioCue.CannonLoad, game.SlotPosition);
        private void OnPull() => Play(InteractionAudioCue.CannonPull, chain.transform.position);
        private void OnFire() => Play(InteractionAudioCue.CannonFire, game.MuzzlePosition);
        private void OnFirework(Vector3 position) => Play(InteractionAudioCue.CannonFirework, position);

        private void Play(InteractionAudioCue cue, Vector3 position)
        {
            if (!isActiveAndEnabled) return;
            ResolveLibrary();
            if (library == null || !library.TryGet(cue, out var entry)) return;
            var source = sources.Find(s => s != null && !s.isPlaying);
            if (source == null && sources.Count < 8)
            {
                var child = new GameObject("CannonInteractionSound_" + sources.Count);
                child.transform.SetParent(transform, false);
                source = child.AddComponent<AudioSource>();
                source.playOnAwake = false;
                sources.Add(source);
            }
            // Never interrupt a cannon report to make room for another small interaction sound.
            if (source == null) return;
            source.transform.position = position;
            source.clip = entry.clip;
            source.loop = false;
            source.volume = entry.volume;
            source.spatialBlend = entry.spatialBlend;
            source.rolloffMode = AudioRolloffMode.Linear;
            // The shell bursts tens of metres away, beyond the other interaction sounds' range.
            source.minDistance = cue == InteractionAudioCue.CannonFirework ? 10f : 2f;
            source.maxDistance = cue == InteractionAudioCue.CannonFirework ? 150f : 25f;
            source.Play();
        }
    }
}
