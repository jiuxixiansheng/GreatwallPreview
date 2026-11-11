using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace Greatwall.CannonGame
{
    public class CannonChain : MonoBehaviour
    {
        // 链底距地面仅约 5.7cm，下拉行程限制在 5cm 内，避免链条穿地。
        private const float MaxPull = 0.05f;
        private const float FireThreshold = 0.045f;
        private const float PullStretch = 0.1f;
        private const float ReturnSpeed = 1.8f;

        private CannonGame _game;
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
        public event System.Action PullStarted;

        public float Pull => _pull;
        public bool IsHeld => _held;

        public void Initialize(CannonGame game, Transform chain, XRGrabInteractable grab)
        {
            _game = game;
            _chain = chain;
            _grab = grab;
            _chainHome = chain.position;
            _chainHomeScale = chain.localScale;
            _topOffset = chain.GetComponent<Renderer>().bounds.max.y - chain.position.y;
            _axis = Vector3.down;
            _grabHomePosition = transform.position;
            _grabHomeRotation = transform.rotation;
            grab.selectEntered.AddListener(OnSelectEntered);
            grab.selectExited.AddListener(OnSelectExited);
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            _held = true;
            _fired = false;
            _pullSoundStarted = false;
            _pull = 0f;
            _hand = args.interactorObject.transform;
            _handStart = _hand.position;
        }

        private void OnSelectExited(SelectExitEventArgs args)
        {
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

                if (!_pullSoundStarted && _pull >= 0.005f)
                {
                    _pullSoundStarted = true;
                    PullStarted?.Invoke();
                }

                if (!_fired && _pull >= FireThreshold)
                {
                    _fired = true;
                    _game.TryFire();
                }
            }
            else if (_pull > 0f)
            {
                _pull = Mathf.MoveTowards(_pull, 0f, ReturnSpeed * Time.deltaTime);
                ApplyPull(_pull);
            }
        }

        private void ApplyPull(float pull)
        {
            float s = 1f + PullStretch * (pull / MaxPull);
            _chain.localScale = new Vector3(_chainHomeScale.x, _chainHomeScale.y * s, _chainHomeScale.z);
            _chain.position = _chainHome + Vector3.up * (_topOffset * (1f - s));
        }
    }
}
