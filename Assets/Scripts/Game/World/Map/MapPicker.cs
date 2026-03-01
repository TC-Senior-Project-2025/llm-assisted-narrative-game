using System;
using System.Collections;
using Extensions;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Game.World.Map
{
    public class MapPicker : MonoBehaviour
    {
        public MapRenderer mapRenderer;

        [Header("Map visual")]
        public SpriteRenderer spriteRenderer;

        [Header("New Input System Actions")]
        public InputActionReference pointAction;
        public InputActionReference clickAction;
        public InputActionReference rightClickAction;

        [Header("Shader params")]
        public string hoverKeyParam = "_HoverKey";
        public string hoverEnabledParam = "_HoverEnabled";
        public string selectKeyParam = "_SelectKey";
        public string selectEnabledParam = "_SelectEnabled";

        public UnityEvent<Color32> hoverChanged = new();
        public UnityEvent<Color32> provinceClicked = new();
        public UnityEvent<Color32> provinceDoubleClicked = new();
        public UnityEvent<Color32> provinceRightClicked = new();

        [Header("Click settings")]
        [Tooltip("Max time between clicks to count as a double click.")]
        public float doubleClickThreshold = 0.25f;

        private Texture2D _provinceMapTexture;
        private Material _mapMaterial;
        private Color32[] _idPixels;
        private int _w, _h;

        private bool _hasHover;
        private Color32 _hoverKey;
        private Color32 _selectedKey;

        private Camera _cam;
        private Vector2 _lastPointerPos;

        // NEW: click tracking
        private float _lastClickTime = -999f;
        private Coroutine _singleClickRoutine;

        private void Awake()
        {
            if (mapRenderer == null) throw new Exception("provinceIdTex missing");
            if (spriteRenderer == null) throw new Exception("mapRenderer missing");
            if (pointAction == null) throw new Exception("pointAction missing");
            if (clickAction == null) throw new Exception("clickAction missing");
            if (rightClickAction == null) throw new Exception("rightClickAction missing");

            _cam = Camera.main;
            if (_cam == null) throw new Exception("No Camera.main found.");

            _provinceMapTexture = mapRenderer.provinceMapTexture;
            _w = _provinceMapTexture.width;
            _h = _provinceMapTexture.height;
            _idPixels = _provinceMapTexture.GetPixels32();

            if (_mapMaterial != null) return;

            _mapMaterial = Instantiate(spriteRenderer.sharedMaterial);
            spriteRenderer.material = _mapMaterial;
        }

        private void OnEnable()
        {
            pointAction.action.Enable();
            clickAction.action.Enable();
            rightClickAction.action.Enable();

            pointAction.action.performed += OnPoint;
            pointAction.action.canceled += OnPoint;

            clickAction.action.performed += OnClick;
            rightClickAction.action.performed += OnRightClick;
        }

        private void OnDisable()
        {
            rightClickAction.action.performed -= OnRightClick;
            clickAction.action.performed -= OnClick;
            pointAction.action.performed -= OnPoint;
            pointAction.action.canceled -= OnPoint;

            rightClickAction.action.Disable();
            clickAction.action.Disable();
            pointAction.action.Disable();
        }

        public void Deselect()
        {
            _mapMaterial.SetFloat(selectEnabledParam, 0);
        }

        private void OnPoint(InputAction.CallbackContext ctx)
        {
            _lastPointerPos = ctx.ReadValue<Vector2>();

            if (IsPointerOverAnyUI(_lastPointerPos))
            {
                if (_hasHover)
                {
                    _hasHover = false;
                    _hoverKey = default;
                    SetKeyOnMaterial(hoverKeyParam, hoverEnabledParam, _hoverKey, isEnabled: false);
                    hoverChanged.Invoke(_hoverKey);
                }
                return;
            }

            UpdateHover(_lastPointerPos);
        }

        private void OnClick(InputAction.CallbackContext ctx)
        {
            if (!ctx.ReadValueAsButton()) return;

            if (IsPointerOverAnyUI(_lastPointerPos))
                return;

            if (!_hasHover) return;

            var now = Time.unscaledTime;
            var isDouble = (now - _lastClickTime) <= doubleClickThreshold;
            _lastClickTime = now;

            _selectedKey = _hoverKey;
            SetKeyOnMaterial(selectKeyParam, selectEnabledParam, _selectedKey, isEnabled: true);
            provinceClicked?.Invoke(_selectedKey);

            if (!isDouble) return;

            provinceDoubleClicked?.Invoke(_selectedKey);
        }

        private void OnRightClick(InputAction.CallbackContext ctx)
        {
            if (!ctx.ReadValueAsButton()) return;

            if (IsPointerOverAnyUI(_lastPointerPos))
                return;

            if (!_hasHover) return;

            provinceRightClicked?.Invoke(_hoverKey);
        }

        private void UpdateHover(Vector2 screenPos)
        {
            if (!TryGetProvinceAtScreenPos(screenPos, out var key))
            {
                if (!_hasHover) return;
                _hasHover = false;
                _hoverKey = default;

                SetKeyOnMaterial(hoverKeyParam, hoverEnabledParam, _hoverKey, isEnabled: false);
                hoverChanged.Invoke(_hoverKey);
                return;
            }

            if (_hasHover && key.Equals(_hoverKey)) return;

            _hasHover = true;
            _hoverKey = key;

            SetKeyOnMaterial(hoverKeyParam, hoverEnabledParam, _hoverKey, isEnabled: true);
            hoverChanged.Invoke(_hoverKey);
        }

        private static bool IsPointerOverAnyUI(Vector2 screenPos)
        {
            // NOTE: screenPos isn't used here; this works for mouse. If you later need multi-touch UI blocking,
            // you’ll want to pass pointerId into IsPointerOverGameObject(pointerId).
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        private bool TryGetProvinceAtScreenPos(Vector2 screenPos, out Color32 key)
        {
            key = default;

            var world = _cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, 0f));
            world.z = spriteRenderer.transform.position.z;

            var local = spriteRenderer.transform.InverseTransformPoint(world);

            var sprite = spriteRenderer.sprite;
            if (sprite == null) return false;

            var texRect = sprite.textureRect;
            var ppu = sprite.pixelsPerUnit;
            var pivotPx = sprite.pivot;

            var localPx = new Vector2(local.x * ppu + pivotPx.x, local.y * ppu + pivotPx.y);

            var px = Mathf.FloorToInt(localPx.x + texRect.x);
            var py = Mathf.FloorToInt(localPx.y + texRect.y);

            if (px < 0 || py < 0 || px >= _w || py >= _h) return false;

            var c = _idPixels[px + py * _w];
            // if (c.Equals(mapRenderer.outsideColorPicker)) return false;
            // if (c.Equals(mapRenderer.seaColorPicker)) return false;

            var isOutside =
                c.SameAs(mapRenderer.outsideColorPicker) ||
                c.SameAs(mapRenderer.seaColorPicker);

            key = isOutside ? Color.clear : c;
            return true;
        }

        private void SetKeyOnMaterial(string keyParam, string enabledParam, Color32 key, bool isEnabled)
        {
            var v = isEnabled
                ? new Vector4(key.r / 255f, key.g / 255f, key.b / 255f, key.a / 255f)
                : Vector4.zero;

            _mapMaterial.SetVector(keyParam, v);
            _mapMaterial.SetFloat(enabledParam, isEnabled ? 1f : 0f);
        }
    }
}