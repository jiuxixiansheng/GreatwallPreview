using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace Greatwall.CannonGame
{
    public class CannonChain : MonoBehaviour, IXRSelectFilter
    {
        // 链底距地面仅约 5.7cm，下拉行程限制在 5cm 内，避免链条穿地。
        internal const float MaxPull = 0.05f;
        internal const float FireThreshold = 0.045f;
        private const float PullSoundThreshold = 0.005f;
        private const float PullStretch = 0.1f;
        private const float ReturnSpeed = 1.8f;

        private CannonGame _game;
        private int _item;
        private Transform _chain;
        private XRGrabInteractable _grab;
        private Vector3 _chainHome;
        private Vector3 _chainHomeScale;
        private float _topOffset;
        private Vector3 _axis;
        private Vector3 _grabHomePosition;
        private Quaternion _grabHomeRotation;
        private Transform _hand;
        private Vector3 _handStart;
        private float _pull;
        private bool _held;
        private bool _fired;
        private bool _pullSoundStarted;
        private bool _remoteHeld;
        private bool _networkPullSoundStarted;
        public event System.Action PullStarted;

        public float Pull => _pull;
        public bool IsHeld => _held;
        internal Vector3 GrabHomePosition => _grabHomePosition;

        public void Initialize(CannonGame game, int item, Transform chain, XRGrabInteractable grab)
        {
            _game = game;
            _item = item;
            _chain = chain;
            _grab = grab;
            _chainHome = chain.position;
            _chainHomeScale = chain.localScale;
            _topOffset = chain.GetComponent<Renderer>().bounds.max.y - chain.position.y;
            _axis = Vector3.down;
            _grabHomePosition = transform.position;
            _grabHomeRotation = transform.rotation;
            grab.selectFilters.Add(this);
            grab.selectEntered.AddListener(OnSelectEntered);
            grab.selectExited.AddListener(OnSelectExited);
        }

        public bool canProcess => isActiveAndEnabled;
        public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable) =>
            !CannonNetwork.IsActive || (_grab.isSelected ? _grab.IsSelected(interactor) :
                _game.Network != null && _game.Network.CanSelect(_item));

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            _held = true;
            _fired = false;
            _pullSoundStarted = false;
            _pull = 0f;
            _hand = args.interactorObject.transform;
            _handStart = _hand.position;
            if (CannonNetwork.IsActive) _game.Network?.Grab(_item);
        }

        private void OnSelectExited(SelectExitEventArgs args)
        {
            if (CannonNetwork.IsActive) _game.Network?.Release(_item);
            _held = false;
            _hand = null;
            transform.SetPositionAndRotation(_grabHomePosition, _grabHomeRotation);
        }

        private void Update()
        {
            if (_held && _hand != null)
            {
                float raw = Vector3.Dot(_hand.position - _handStart, _axis);
                _pull = Mathf.Clamp(raw, 0f, MaxPull);
                ApplyPull(_pull);

                if (!_pullSoundStarted && _pull >= PullSoundThreshold && !CannonNetwork.IsActive)
                {
                    _pullSoundStarted = true;
                    PullStarted?.Invoke();
                }

                if (!_fired && _pull >= FireThreshold)
                {
                    _fired = true;
                    if (CannonNetwork.IsActive) _game.Network?.SendChainThreshold();
                    else _game.TryFire();
                }
            }
            else if (!_remoteHeld && _pull > 0f)
            {
                _pull = Mathf.MoveTowards(_pull, 0f, ReturnSpeed * Time.deltaTime);
                ApplyPull(_pull);
            }
        }

        internal void ApplyNetworkPull(CannonItemState item, bool local, bool initial)
        {
            bool held = item.phase == CannonItemPhase.Held;
            _remoteHeld = held && !local;
            if (_remoteHeld || initial)
            {
                _pull = item.pull;
                ApplyPull(_pull);
            }

            if (!held) _networkPullSoundStarted = false;
            else if (!_networkPullSoundStarted && item.pull >= PullSoundThreshold)
            {
                _networkPullSoundStarted = true;
                if (!initial) PullStarted?.Invoke();
            }
        }

        internal void CancelNetworkSelection()
        {
            if (_grab.isSelected && _grab.interactionManager != null)
                _grab.interactionManager.CancelInteractableSelection((IXRSelectInteractable)_grab);
        }

        private void ApplyPull(float pull)
        {
            float s = 1f + PullStretch * (pull / MaxPull);
            _chain.localScale = new Vector3(_chainHomeScale.x, _chainHomeScale.y * s, _chainHomeScale.z);
            _chain.position = _chainHome + Vector3.up * (_topOffset * (1f - s));
        }
    }
}
