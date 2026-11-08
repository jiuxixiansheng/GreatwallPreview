using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Greatwall.CannonGame
{
    [DefaultExecutionOrder(100)]
    public class CannonAmmo : MonoBehaviour
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

        public AmmoState State => _state;
        public bool IsHeld => _state == AmmoState.Held;
        public bool IsLoaded => _state == AmmoState.Loaded;
        public bool IsAvailable => _state == AmmoState.Home;

        public void Initialize(XRGrabInteractable grab, Collider collider, Rigidbody body, GameObject visual, Vector3 homePosition, Quaternion homeRotation)
        {
            _grab = grab;
            _collider = collider;
            _body = body;
            _visual = visual;
            _homePosition = homePosition;
            _homeRotation = homeRotation;
            _state = AmmoState.Home;
            _initialized = true;
            _grab.selectEntered.AddListener(OnSelectEntered);
            _grab.selectExited.AddListener(OnSelectExited);
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (!_initialized) return;
            if (_state == AmmoState.Inserting || _state == AmmoState.Loaded || _state == AmmoState.Consumed) return;
            _state = AmmoState.Held;
        }

        private void OnSelectExited(SelectExitEventArgs args)
        {
            if (!_initialized || _state != AmmoState.Held) return;
            BeginTween(_homePosition, _homeRotation, ReturnDuration);
            _state = AmmoState.Returning;
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
                case AmmoState.Inserting:
                    StepTween();
                    if (_tweenTime >= _tweenDuration) _state = AmmoState.Loaded;
                    break;
                case AmmoState.Returning:
                    StepTween();
                    if (_tweenTime >= _tweenDuration) _state = AmmoState.Home;
                    break;
                case AmmoState.Consumed:
                    if (Time.time >= _respawnAt) Respawn();
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
