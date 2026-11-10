using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace Greatwall.CannonGame
{
    public class CannonGuidance : MonoBehaviour
    {
        private const string HintText = "拾取炮弹";
        private const float HintHeight = 0.95f;
        private const float HintScale = 0.0022f;

        private CannonGame _game;
        private Transform _hint;
        private CanvasGroup _hintGroup;
        private Vector3 _hintBase;
        private float _hintAlpha;
        private Camera _camera;

        private MeshRenderer _glowRenderer;
        private Material _glowMaterial;
        private float _glow;
        private float _time;

        public void Initialize(CannonGame game, IReadOnlyList<CannonAmmo> ammo, Transform chain, Font font)
        {
            _game = game;

            var sum = Vector3.zero;
            int count = 0;
            for (int i = 0; i < ammo.Count; i++)
            {
                if (ammo[i] == null) continue;
                sum += ammo[i].transform.position;
                count++;
            }
            _hintBase = (count > 0 ? sum / count : transform.position) + Vector3.up * HintHeight;

            if (font != null) CreateHint(font);
            CreateChainGlow(chain);
        }

        private void CreateHint(Font font)
        {
            var go = new GameObject("PickupHint", typeof(RectTransform));
            _hint = go.transform;
            _hint.SetParent(transform, false);
            _hint.position = _hintBase;
            _hint.localScale = Vector3.one * HintScale;

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 20;

            var rect = (RectTransform)_hint;
            rect.sizeDelta = new Vector2(560f, 150f);

            _hintGroup = go.AddComponent<CanvasGroup>();
            _hintGroup.alpha = 0f;
            _hintGroup.interactable = false;
            _hintGroup.blocksRaycasts = false;

            var label = new GameObject("Label", typeof(RectTransform));
            var labelRect = (RectTransform)label.transform;
            labelRect.SetParent(_hint, false);
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            var text = label.AddComponent<Text>();
            text.font = font;
            text.text = HintText;
            text.fontSize = 68;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(1f, 0.88f, 0.6f, 1f);
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            var outline = label.AddComponent<Outline>();
            outline.effectColor = new Color(0.08f, 0.04f, 0.01f, 0.9f);
            outline.effectDistance = new Vector2(2.2f, -2.2f);
        }

        private void CreateChainGlow(Transform chain)
        {
            if (chain == null) return;

            var sourceFilter = chain.GetComponent<MeshFilter>();
            if (sourceFilter == null || sourceFilter.sharedMesh == null) return;

            var go = new GameObject("ChainGlow");
            go.layer = chain.gameObject.layer;
            go.transform.SetParent(chain, false);
            go.AddComponent<MeshFilter>().sharedMesh = sourceFilter.sharedMesh;

            _glowRenderer = go.AddComponent<MeshRenderer>();
            _glowRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _glowRenderer.receiveShadows = false;
            _glowRenderer.enabled = false;

            var template = Resources.Load<Material>("CannonChainGlow");
            if (template == null)
            {
                Debug.LogWarning("[CannonGuidance] 缺少 Resources/CannonChainGlow.mat，铁链呼吸光不可用。", this);
                return;
            }

            _glowMaterial = new Material(template);
            int subMeshCount = Mathf.Max(1, sourceFilter.sharedMesh.subMeshCount);
            var materials = new Material[subMeshCount];
            for (int i = 0; i < subMeshCount; i++) materials[i] = _glowMaterial;
            _glowRenderer.sharedMaterials = materials;
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            _time += dt;

            bool loaded = _game != null && _game.LoadedAmmo != null;
            bool held = false;
            if (_game != null)
            {
                var ammo = _game.Ammo;
                for (int i = 0; i < ammo.Count; i++)
                {
                    if (ammo[i] != null && ammo[i].IsHeld)
                    {
                        held = true;
                        break;
                    }
                }
            }

            UpdateHint(dt, !loaded && !held);
            UpdateGlow(dt, loaded);
        }

        private void UpdateHint(float dt, bool visible)
        {
            _hintAlpha = Mathf.MoveTowards(_hintAlpha, visible ? 1f : 0f, dt * 2.5f);
            if (_hintGroup == null) return;

            _hintGroup.alpha = _hintAlpha * (0.85f + 0.15f * Mathf.Sin(_time * 2.4f));
            bool active = _hintAlpha > 0.01f;
            if (_hintGroup.gameObject.activeSelf != active) _hintGroup.gameObject.SetActive(active);
            if (!active) return;

            _hint.position = _hintBase + Vector3.up * (Mathf.Sin(_time * 1.5f) * 0.025f);

            var camera = ResolveCamera();
            if (camera == null) return;

            var to = _hint.position - camera.transform.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.0001f)
                _hint.rotation = Quaternion.LookRotation(to.normalized, Vector3.up);
        }

        private Camera ResolveCamera()
        {
            if (_camera != null && _camera.isActiveAndEnabled) return _camera;

            _camera = Camera.main;
            if (_camera == null)
            {
                var cameras = Camera.allCameras;
                if (cameras.Length > 0) _camera = cameras[0];
            }
            return _camera;
        }

        private void UpdateGlow(float dt, bool loaded)
        {
            if (_glowRenderer == null || _glowMaterial == null) return;

            _glow = Mathf.MoveTowards(_glow, loaded ? 1f : 0f, dt * 2f);
            float breath = 0.5f + 0.5f * Mathf.Sin(_time * Mathf.PI * 2f * 0.5f);
            float intensity = _glow * (0.5f + 0.5f * breath);

            _glowMaterial.SetFloat("_Intensity", intensity);
            _glowMaterial.SetFloat("_Width", Mathf.Lerp(0.012f, 0.02f, breath));
            _glowRenderer.enabled = intensity > 0.01f;
        }
    }
}
