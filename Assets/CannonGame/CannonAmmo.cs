using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Greatwall.CannonGame
{
    [DefaultExecutionOrder(100)]
    public class CannonAmmo : MonoBehaviour, IXRSelectFilter
    {
        public enum AmmoState
        {
            Home,
            Held,
            Returning,
            Inserting,
            Loaded,
            Consumed
        }

        private const float InsertDuration = 0.14f;
        private const float ReturnDuration = 0.3f;
        private const float RespawnDelay = 2.8f;

        private XRGrabInteractable _grab;
        private CannonGame _game;
        private int _item;
        private Collider _collider;
        private Rigidbody _body;
        private GameObject _visual;
        private Vector3 _homePosition;
        private Quaternion _homeRotation;
        private AmmoState _state;

        private Vector3 _tweenFromPosition;
        private Quaternion _tweenFromRotation;
        private Vector3 _tweenTargetPosition;
        private Quaternion _tweenTargetRotation;
        private float _tweenTime;
        private float _tweenDuration;
        private float _respawnAt;
        private bool _initialized;
        private bool _remoteHeld;
        private Vector3 _remotePosition;
        private Quaternion _remoteRotation;

        public AmmoState State => _state;
        public bool IsHeld => _state == AmmoState.Held;
        public bool IsLoaded => _state == AmmoState.Loaded;
        public bool IsAvailable => _state == AmmoState.Home;
        internal Vector3 HomePosition => _homePosition;
        internal Quaternion HomeRotation => _homeRotation;
        public event System.Action<CannonAmmo> PickedUp;

        public void Initialize(CannonGame game, int item, XRGrabInteractable grab, Collider collider, Rigidbody body,
            GameObject visual, Vector3 homePosition, Quaternion homeRotation)
        {
            _game = game;
            _item = item;
            _grab = grab;
            _collider = collider;
            _body = body;
            _visual = visual;
            _homePosition = homePosition;
            _homeRotation = homeRotation;
            _state = AmmoState.Home;
            _initialized = true;
            _grab.selectFilters.Add(this);
            _grab.selectEntered.AddListener(OnSelectEntered);
            _grab.selectExited.AddListener(OnSelectExited);
        }

        public bool canProcess => isActiveAndEnabled;
        public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable) =>
            !CannonNetwork.IsActive || (_grab.isSelected ? _grab.IsSelected(interactor) :
                _game.Network != null && _game.Network.CanSelect(_item));

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (!_initialized) return;
            if (CannonNetwork.IsActive)
            {
                if (_state == AmmoState.Returning) _state = AmmoState.Home;
                _game.Network?.Grab(_item);
                return;
            }
            if (_state == AmmoState.Held) return;
            if (_state == AmmoState.Inserting || _state == AmmoState.Loaded || _state == AmmoState.Consumed) return;
            _state = AmmoState.Held;
            PickedUp?.Invoke(this);
        }

        private void OnSelectExited(SelectExitEventArgs args)
        {
            if (!_initialized) return;
            if (CannonNetwork.IsActive)
            {
                _game.Network?.Release(_item);
                return;
            }
            if (_state != AmmoState.Held) return;
            BeginTween(_homePosition, _homeRotation, ReturnDuration);
            _state = AmmoState.Returning;
        }

        internal void ApplyNetworkState(CannonItemState item, Vector3 slotPosition, Quaternion slotRotation,
            bool local, bool initial)
        {
            _remoteHeld = item.phase == CannonItemPhase.Held && !local;
            switch (item.phase)
            {
                case CannonItemPhase.Home:
                    if (_state == AmmoState.Held)
                    {
                        BeginTween(_homePosition, _homeRotation, ReturnDuration);
                        _state = AmmoState.Returning;
                    }
                    else if (_state != AmmoState.Home && _state != AmmoState.Returning)
                        ResetNetworkState();
                    break;
                case CannonItemPhase.Held:
                    bool fresh = _state != AmmoState.Held;
                    _state = AmmoState.Held;
                    _remotePosition = item.position;
                    _remoteRotation = item.rotation;
                    if (fresh && !initial) PickedUp?.Invoke(this);
                    break;
                case CannonItemPhase.Loaded:
                    if (_state != AmmoState.Loaded && _state != AmmoState.Inserting)
                        InsertIntoSlot(slotPosition, slotRotation);
                    break;
                case CannonItemPhase.CoolingDown:
                    if (_state != AmmoState.Consumed) Consume();
                    break;
            }
        }

        internal void CancelNetworkSelection()
        {
            if (!_grab.isSelected) return;
            if (_grab.interactionManager != null)
                _grab.interactionManager.CancelInteractableSelection((IXRSelectInteractable)_grab);
            if (_state == AmmoState.Home || _state == AmmoState.Returning)
                ResetNetworkState();
        }

        internal void ResetNetworkState()
        {
            _remoteHeld = false;
            transform.SetPositionAndRotation(_homePosition, _homeRotation);
            Respawn();
        }

        public void InsertIntoSlot(Vector3 position, Quaternion rotation)
        {
            if (!_initialized || _state == AmmoState.Consumed) return;

            _state = AmmoState.Inserting;
            BeginTween(position, rotation, InsertDuration);
            ReleaseSelections();
            _grab.enabled = false;
            _collider.enabled = false;
            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
        }

        public void Consume()
        {
            _state = AmmoState.Consumed;
            _visual.SetActive(false);
            _grab.enabled = false;
            _collider.enabled = false;
            transform.SetPositionAndRotation(_homePosition, _homeRotation);
            _respawnAt = Time.time + RespawnDelay;
        }

        private void ReleaseSelections()
        {
            if (_grab.interactionManager == null) return;

            var interactors = new List<IXRSelectInteractor>(_grab.interactorsSelecting);
            for (int i = 0; i < interactors.Count; i++)
            {
                if (interactors[i] != null) _grab.interactionManager.SelectExit(interactors[i], _grab);
            }
        }

        private void BeginTween(Vector3 position, Quaternion rotation, float duration)
        {
            _tweenFromPosition = transform.position;
            _tweenFromRotation = transform.rotation;
            _tweenTargetPosition = position;
            _tweenTargetRotation = rotation;
            _tweenTime = 0f;
            _tweenDuration = duration;
        }

        private void Update()
        {
            switch (_state)
            {
                case AmmoState.Held:
                    if (_remoteHeld)
                    {
                        float blend = 1f - Mathf.Exp(-28f * Time.deltaTime);
                        transform.SetPositionAndRotation(Vector3.Lerp(transform.position, _remotePosition, blend),
                            Quaternion.Slerp(transform.rotation, _remoteRotation, blend));
                    }
                    break;
                case AmmoState.Inserting:
                    StepTween();
                    if (_tweenTime >= _tweenDuration) _state = AmmoState.Loaded;
                    break;
                case AmmoState.Returning:
                    StepTween();
                    if (_tweenTime >= _tweenDuration) _state = AmmoState.Home;
                    break;
                case AmmoState.Consumed:
                    if (!CannonNetwork.IsActive && Time.time >= _respawnAt) Respawn();
                    break;
            }
        }

        private void StepTween()
        {
            _tweenTime += Time.deltaTime;
            float t = Mathf.Clamp01(_tweenTime / Mathf.Max(0.0001f, _tweenDuration));
            float eased = Mathf.SmoothStep(0f, 1f, t);
            transform.SetPositionAndRotation(
                Vector3.Lerp(_tweenFromPosition, _tweenTargetPosition, eased),
                Quaternion.Slerp(_tweenFromRotation, _tweenTargetRotation, eased));
        }

        private void Respawn()
        {
            _visual.SetActive(true);
            _grab.enabled = true;
            _collider.enabled = true;
            _state = AmmoState.Home;
        }
    }
}
