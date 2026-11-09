using System.Collections;
using System.Collections.Generic;
using Greatwall.Flight;
using Greatwall.VRAnimation.Timeline;
using Mirror;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.Interaction.Toolkit;
using Xcy.CrackRepair;

namespace Xcy.Audio
{
    [DisallowMultipleComponent, DefaultExecutionOrder(-8000)]
    public sealed class InteractionAudioPlayer : MonoBehaviour
    {
        public InteractionAudioLibrary library;
        public StoryTimelineManager story;
        public BrickDropZone[] brickZones;
        public Part2CrackRepairCoordinator crackRepair;
        public DragonBoardingGate boardingGate;
        public FlightDirector flight;
        public Transform prologueTarget;
        public bool logPlayback = true;
        [Tooltip("可绑定影壁和光幕的视觉效果；当前音效不再放在Timeline中。")]
        public UnityEvent onWallGlow = new UnityEvent();
        public UnityEvent onLightCurtain = new UnityEvent();

        public int OneShotsPlayed { get; private set; }
        public int LoopsStarted { get; private set; }
        public int ActiveLoopCount => loops.Count;
        public bool PrologueRunning => prologueRoutine != null;

        private readonly List<AudioSource> oneShots = new List<AudioSource>();
        private readonly HashSet<AudioSource> prologueOneShots = new HashSet<AudioSource>();
        private readonly Dictionary<int, AudioSource> loops = new Dictionary<int, AudioSource>();
        private readonly Dictionary<int, Coroutine> repeats = new Dictionary<int, Coroutine>();
        private NetworkInteractionAudioRelay relay;
        private EventDispatcher dispatcher;
        private Coroutine prologueRoutine;
        private int chapter = -1;
        private bool transitioning;
        private bool flightObserved;
        private FlightPhase previousFlightPhase;

        private void Awake() => relay = GetComponent<NetworkInteractionAudioRelay>();

        private void OnEnable()
        {
            if (brickZones != null)
                foreach (var zone in brickZones)
                    if (zone != null) { zone.BrickAccepted += OnBrickAccepted; zone.Solved += OnBrickSolved; }
            if (crackRepair != null)
            {
                crackRepair.RepairUnlocked += OnCracksUnlocked;
                crackRepair.RepairFinished += OnCracksFinished;
                foreach (var crack in crackRepair.Cracks)
                    if (crack != null)
                    {
                        crack.RepairStarted += OnCrackStarted;
                        crack.RepairStopped += OnCrackStopped;
                    }
            }
            if (boardingGate != null)
            {
                boardingGate.onBoardingShown.AddListener(OnBoardingShown);
                boardingGate.onBoarded.AddListener(OnBoarded);
            }
            dispatcher = EventDispatcher.Instance;
            dispatcher.AddListener("zhuanchang", OnTransitionRequested);
        }

        private void OnDisable()
        {
            if (brickZones != null)
                foreach (var zone in brickZones)
                    if (zone != null) { zone.BrickAccepted -= OnBrickAccepted; zone.Solved -= OnBrickSolved; }
            if (crackRepair != null)
            {
                crackRepair.RepairUnlocked -= OnCracksUnlocked;
                crackRepair.RepairFinished -= OnCracksFinished;
                foreach (var crack in crackRepair.Cracks)
                    if (crack != null) { crack.RepairStarted -= OnCrackStarted; crack.RepairStopped -= OnCrackStopped; }
            }
            if (boardingGate != null)
            {
                boardingGate.onBoardingShown.RemoveListener(OnBoardingShown);
                boardingGate.onBoarded.RemoveListener(OnBoarded);
            }
            if (dispatcher != null) dispatcher.RemoveListener("zhuanchang", OnTransitionRequested);
            CancelPrologue();
            StopAllAudio();
        }

        private void Update()
        {
            if (story == null) return;
            if (chapter != story.CurrentChapterIndex)
            {
                // Let the opening sounds finish across the normal part0 -> part1 transfer.
                bool continuePrologue = chapter == 0 && story.CurrentChapterIndex == 1;
                chapter = story.CurrentChapterIndex;
                if (!continuePrologue) CancelPrologue();
                StopChapterAudio(continuePrologue);
                flightObserved = false;
            }
            if (story.IsTransitionPending && !transitioning)
            {
                if (chapter != 0) CancelPrologue();
                StopLoops();
            }
            transitioning = story.IsTransitionPending;

            // Flight phase is already synchronized by FlightDirector. Do not broadcast it a second time.
            bool observing = chapter == 4 && flight != null && flight.gameObject.activeInHierarchy;
            if (!observing) { flightObserved = false; return; }
            var phase = flight.CurrentPhase;
            if (flightObserved && phase != previousFlightPhase && phase == FlightPhase.TakeOff)
            {
                StopLoop(InteractionAudioCue.BoardingPrompt, 0);
                ApplyCue(InteractionAudioCue.TakeOff, 0, flight.transform.position, false);
            }
            previousFlightPhase = phase;
            flightObserved = true;
        }

        private bool InChapter(int index) => story != null && story.CurrentChapterIndex == index && !story.IsTransitionPending;
        private void Emit(InteractionAudioCue cue, int emitter, Vector3 position, bool stop = false)
        {
            if (relay != null) relay.EmitAuthority(cue, emitter, position, stop);
            else if (!NetworkClient.active && !NetworkServer.active) ApplyCue(cue, emitter, position, stop);
        }

        public void BeginPrologueInteraction(SelectEnterEventArgs _) => BeginPrologue();
        public void BeginPrologue()
        {
            if (!InChapter(0)) return;
            if (relay != null) relay.RequestPrologue();
            else RunPrologueFromAuthority();
        }

        public void RunPrologueFromAuthority()
        {
            if (!InChapter(0) || PrologueRunning) return;
            prologueRoutine = StartCoroutine(PrologueSequence());
            // Audio accompanies the transfer; its duration must never gate the chapter change.
            if (InChapter(0)) story.SwitchToNextChapter();
        }

        private IEnumerator PrologueSequence()
        {
            Vector3 position = prologueTarget != null ? prologueTarget.position : transform.position;
            Emit(InteractionAudioCue.WallGlow, 0, position);
            yield return new WaitForSeconds(Duration(InteractionAudioCue.WallGlow));
            bool canContinue = story != null && (story.CurrentChapterIndex == 0 ||
                story.CurrentChapterIndex == 1 && !story.IsTransitionPending);
            if (!canContinue) { prologueRoutine = null; yield break; }
            Emit(InteractionAudioCue.LightCurtain, 0, position);
            yield return new WaitForSeconds(Duration(InteractionAudioCue.LightCurtain));
            prologueRoutine = null;
        }

        private float Duration(InteractionAudioCue cue) => library != null && library.TryGet(cue, out var entry) ? entry.clip.length : 0f;
        private void CancelPrologue()
        {
            if (prologueRoutine != null) StopCoroutine(prologueRoutine);
            prologueRoutine = null;
        }

        public void BeginBrickRepairPrompt()
        {
            if (InChapter(1) && brickZones != null && brickZones.Length > 0 && !brickZones[0].IsSolved)
                Emit(InteractionAudioCue.BrickPrompt, 0, brickZones[0].transform.position);
        }

        private void OnBrickAccepted(BrickDropZone zone, Rigidbody brick)
        { if (InChapter(1)) Emit(InteractionAudioCue.BrickPlaced, 0, brick.position); }
        private void OnBrickSolved(BrickDropZone zone)
        {
            if (!InChapter(1)) return;
            Emit(InteractionAudioCue.BrickPrompt, 0, zone.transform.position, true);
            Emit(InteractionAudioCue.BrickCompleted, 0, zone.transform.position);
        }
        private void OnCracksUnlocked()
        { if (InChapter(2)) Emit(InteractionAudioCue.CrackPrompt, 0, crackRepair.transform.position); }
        private void OnCrackStarted(CrackRepairDecal crack)
        {
            if (InChapter(2)) Emit(InteractionAudioCue.CrackRepair, crackRepair.IndexOf(crack), crack.transform.position);
        }
        private void OnCrackStopped(CrackRepairDecal crack)
        { Emit(InteractionAudioCue.CrackRepair, crackRepair.IndexOf(crack), crack.transform.position, true); }
        private void OnCracksFinished()
        {
            if (!InChapter(2)) return;
            Emit(InteractionAudioCue.CrackPrompt, 0, crackRepair.transform.position, true);
            Emit(InteractionAudioCue.CrackCompleted, 0, crackRepair.transform.position);
        }
        private void OnTransitionRequested()
        {
            // The existing chapter system dispatches this at the actual start of the transfer.
            if (story != null && story.CurrentChapterIndex == 2)
                Emit(InteractionAudioCue.Transfer, 0, transform.position);
        }
        private void OnBoardingShown()
        {
            // The boarding area belongs to this local player; do not stop other players' prompts.
            if (InChapter(4)) ApplyCue(InteractionAudioCue.BoardingPrompt, 0, boardingGate.transform.position, false);
        }
        private void OnBoarded() => StopLoop(InteractionAudioCue.BoardingPrompt, 0);

        public void ApplyCue(InteractionAudioCue cue, int emitter, Vector3 position, bool stop)
        {
            if (stop)
            {
                if (cue == InteractionAudioCue.CrackPrompt) SetCrackPromptGlow(false);
                StopLoop(cue, emitter);
                return;
            }
            if (library == null || !library.TryGet(cue, out var entry)) return;
            if (cue == InteractionAudioCue.CrackPrompt) SetCrackPromptGlow(true);
            if (entry.loop || entry.repeatInterval > 0f)
            {
                int key = Key(cue, emitter);
                if (loops.ContainsKey(key)) return;
                var source = NewSource("Loop_" + cue + "_" + emitter);
                Configure(source, entry, position);
                source.loop = entry.loop && entry.repeatInterval <= 0f;
                source.clip = entry.clip;
                source.Play();
                loops.Add(key, source);
                if (entry.repeatInterval > 0f)
                    repeats.Add(key, StartCoroutine(RepeatWithGap(source, entry.repeatInterval)));
                LoopsStarted++;
            }
            else
            {
                AudioSource source = oneShots.Find(s => !s.isPlaying);
                if (source == null && oneShots.Count < 8) { source = NewSource("OneShot_" + oneShots.Count); oneShots.Add(source); }
                if (source == null) { source = oneShots[0]; source.Stop(); }
                Configure(source, entry, position);
                source.loop = false;
                source.clip = entry.clip;
                source.Play();
                if (cue == InteractionAudioCue.WallGlow || cue == InteractionAudioCue.LightCurtain)
                    prologueOneShots.Add(source);
                else
                    prologueOneShots.Remove(source);
                OneShotsPlayed++;
            }
            if (cue == InteractionAudioCue.WallGlow) onWallGlow.Invoke();
            if (cue == InteractionAudioCue.LightCurtain) onLightCurtain.Invoke();
            if (logPlayback) Debug.Log("[交互音效] " + cue + " / " + emitter, this);
        }

        private void SetCrackPromptGlow(bool active)
        {
            if (crackRepair == null) return;
            foreach (var crack in crackRepair.Cracks)
                if (crack != null) crack.SetPromptGlow(active);
        }

        private static int Key(InteractionAudioCue cue, int emitter) => (int)cue * 100 + emitter;
        private IEnumerator RepeatWithGap(AudioSource source, float gap)
        {
            while (source != null)
            {
                // Keep the source registered during the gap so network snapshots cannot restart it.
                while (source != null && source.isPlaying) yield return null;
                yield return new WaitForSeconds(gap);
                if (source != null) source.Play();
            }
        }
        private AudioSource NewSource(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            return source;
        }
        private static void Configure(AudioSource source, InteractionAudioLibrary.Entry entry, Vector3 position)
        {
            source.transform.position = position;
            source.volume = entry.volume;
            source.spatialBlend = entry.spatialBlend;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 1f;
            source.maxDistance = 20f;
        }
        private void StopLoop(InteractionAudioCue cue, int emitter)
        {
            int key = Key(cue, emitter);
            if (repeats.TryGetValue(key, out var routine))
            {
                StopCoroutine(routine);
                repeats.Remove(key);
            }
            if (!loops.TryGetValue(key, out var source)) return;
            loops.Remove(key);
            source.Stop();
            Destroy(source.gameObject);
        }
        private void StopLoops()
        {
            foreach (var routine in repeats.Values) StopCoroutine(routine);
            repeats.Clear();
            foreach (var source in loops.Values) if (source != null) { source.Stop(); Destroy(source.gameObject); }
            loops.Clear();
        }
        public void StopAllAudio()
        {
            CancelPrologue();
            StopChapterAudio(false);
        }
        private void StopChapterAudio(bool preservePrologue)
        {
            SetCrackPromptGlow(false);
            StopLoops();
            foreach (var source in oneShots)
                if (source != null && !(preservePrologue && prologueOneShots.Contains(source)))
                    source.Stop();
            if (!preservePrologue) prologueOneShots.Clear();
        }
    }
}
