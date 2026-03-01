using System;
using System.Collections.Generic;
using Extensions;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Game.World.Map
{
    public class MapFogOfWar : MonoBehaviour
    {
        private static readonly int FogOfWarTexProperty = Shader.PropertyToID("_FogOfWarTex");
        private static readonly int FogOfWarEnabledProperty = Shader.PropertyToID("_FogOfWarEnabled");
        private static readonly int FogOfWarStrengthProperty = Shader.PropertyToID("_FogOfWarStrength");

        [Header("Settings")][Range(0f, 1f)] public float fogStrength = 0.9f;

        [Header("Input")] public List<int> occupiedProvinces = new();

        [Header("Dependencies")] public SpriteRenderer spriteRenderer;
        public MapRenderer mapRenderer;
        public MapProvider mapProvider;
        public MapConnections mapConnections;

        [Header("Output")][SerializeField] private Texture2D fogOfWarTexture;

        private Color32[] _srcPixels;
        private byte[] _fogBytes;
        private readonly HashSet<int> _owned = new();
        private readonly HashSet<int> _neighbors = new();
        private readonly HashSet<uint> _ownedKeys = new();
        private readonly HashSet<uint> _neighborKeys = new();

        public enum VisibilityState
        {
            None,
            Partial,
            Full
        }

        public void BuildFogOfWarTexture()
        {
            var original = mapRenderer.provinceMapTexture;
            int w = original.width, h = original.height;
            var len = w * h;

            // Cache source pixels once (or refresh if size changed)
            if (_srcPixels == null || _srcPixels.Length != len)
                _srcPixels = original.GetPixels32();

            // Ensure output buffer
            if (_fogBytes == null || _fogBytes.Length != len)
                _fogBytes = new byte[len];

            // Build owned + neighbors (int sets)
            _owned.Clear();
            _neighbors.Clear();

            foreach (var pid in occupiedProvinces)
                _owned.Add(pid);

            foreach (var pid in occupiedProvinces)
            {
                foreach (var n in mapConnections.GetNeighbors(pid))
                {
                    if (!_owned.Contains(n))
                        _neighbors.Add(n);
                }
            }

            // Convert province ids -> packed color keys
            _ownedKeys.Clear();
            _neighborKeys.Clear();

            foreach (var pid in _owned)
                _ownedKeys.Add(mapProvider.GetProvinceColor(pid).Pack());

            foreach (var pid in _neighbors)
                _neighborKeys.Add(mapProvider.GetProvinceColor(pid).Pack());

            // Fill fog bytes (R8)
            for (int i = 0; i < len; i++)
            {
                uint key = _srcPixels[i].Pack();

                // owned: 255, neighbor: 128, unknown: 0
                _fogBytes[i] = _ownedKeys.Contains(key) ? (byte)255
                    : _neighborKeys.Contains(key) ? (byte)128
                    : (byte)0;
            }

            // Ensure R8 texture
            if (fogOfWarTexture == null ||
                fogOfWarTexture.width != w ||
                fogOfWarTexture.height != h ||
                fogOfWarTexture.format != TextureFormat.R8)
            {
                fogOfWarTexture = new Texture2D(w, h, TextureFormat.R8, mipChain: false, linear: true)
                {
                    name = $"{name}_FogOfWarTex",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
            }

            // Upload to GPU
            fogOfWarTexture.SetPixelData(_fogBytes, 0);
            fogOfWarTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        }

        private void Start()
        {
            UpdateFogStrength();
        }

        private void OnValidate()
        {
            UpdateFogStrength();
        }

        private void UpdateFogStrength()
        {
            if (spriteRenderer != null)
            {
                spriteRenderer.sharedMaterial.SetFloat(FogOfWarStrengthProperty, fogStrength);
            }
        }

        public void ApplyTexture()
        {
            if (fogOfWarTexture == null)
            {
                throw new Exception("FoW texture is null");
            }

            spriteRenderer.sharedMaterial.SetTexture(FogOfWarTexProperty, fogOfWarTexture);
        }

        public void SetFogOfWarEnabled(bool fowEnabled)
        {
            spriteRenderer.sharedMaterial.SetFloat(FogOfWarEnabledProperty, fowEnabled ? 1f : 0f);
        }

        public VisibilityState GetFogOfWarValue(int provinceId)
        {
            if (_neighbors.Contains(provinceId)) return VisibilityState.Partial;
            else if (_owned.Contains(provinceId)) return VisibilityState.Full;
            return VisibilityState.None;
        }

        public void Clear()
        {
            occupiedProvinces = new List<int>();
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(MapFogOfWar))]
    public class MapFogOfWarEditor : Editor
    {
        private MapFogOfWar Target => (MapFogOfWar)target;

        public override void OnInspectorGUI()
        {
            base.DrawDefaultInspector();
            if (GUILayout.Button("Apply Texture"))
            {
                Target.BuildFogOfWarTexture();
                // Target.ApplyTexture();
            }

            if (GUILayout.Button("Clear"))
            {
                Target.Clear();
                ;
            }
        }
    }
#endif
}