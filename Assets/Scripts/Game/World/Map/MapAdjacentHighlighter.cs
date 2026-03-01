using System;
using Extensions;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Game.World.Map
{
    public class MapAdjacentHighlighter : MonoBehaviour
    {
        private static readonly int AdjacentTexProperty = Shader.PropertyToID("_AdjacentTex");
        private static readonly int AdjacentEnabledProperty = Shader.PropertyToID("_AdjacentEnabled");

        [Header("Dependencies")] public SpriteRenderer spriteRenderer;

        public MapRenderer mapRenderer;
        public MapProvider mapProvider;
        public MapConnections mapConnections;

        [Header("Output")]
        public Texture2D adjacentTexture;

        [Header("Test")]
        public int testProvinceId;

        private Color32[] _srcProvincePixels;
        private byte[] _maskBytes;
        private uint[] _neighborKeys;

        public void BuildAdjacentTexture(int provinceId)
        {
            var original = mapRenderer.provinceMapTexture;
            var w = original.width;
            var h = original.height;
            var len = w * h;

            // Cache source pixels ONCE (or refresh if texture size changes)
            if (_srcProvincePixels == null || _srcProvincePixels.Length != len)
            {
                _srcProvincePixels = original.GetPixels32(); // source province colors
            }

            // Ensure output buffer
            if (_maskBytes == null || _maskBytes.Length != len)
                _maskBytes = new byte[len];

            // Build neighbor color keys (packed uint)
            var neighborIds = mapConnections.GetNeighbors(provinceId);
            var nk = neighborIds.Length;

            if (_neighborKeys == null || _neighborKeys.Length < nk)
                _neighborKeys = new uint[Mathf.NextPowerOfTwo(Mathf.Max(1, nk))];

            for (var i = 0; i < nk; i++)
                _neighborKeys[i] = mapProvider.GetProvinceColor(neighborIds[i]).Pack();

            // Create / resize mask texture (R8 = 1 byte per pixel)
            if (adjacentTexture == null || adjacentTexture.width != w || adjacentTexture.height != h || adjacentTexture.format != TextureFormat.R8)
            {
                adjacentTexture = new Texture2D(w, h, TextureFormat.R8, mipChain: false, linear: true)
                {
                    name = $"{name}_AdjacentTex",
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
            }

            // Fill mask bytes: 255 for neighbor pixels, 0 otherwise
            for (var p = 0; p < len; p++)
            {
                var key = _srcProvincePixels[p].Pack();

                var isNeighbor = false;
                for (var j = 0; j < nk; j++)
                {
                    if (_neighborKeys[j] != key) continue;
                    isNeighbor = true; break;
                }

                _maskBytes[p] = isNeighbor ? (byte)255 : (byte)0;
            }

            // Upload to GPU
            adjacentTexture.SetPixelData(_maskBytes, 0);
            adjacentTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        }

        public void SetHighlightEnabled(bool highlightEnabled)
        {
            spriteRenderer.sharedMaterial.SetFloat(AdjacentEnabledProperty, highlightEnabled ? 1f : 0f);
        }

        public void ApplyAdjacentTexture()
        {
            if (adjacentTexture == null) throw new Exception("Adjacent texture is null");
            spriteRenderer.sharedMaterial.SetTexture(AdjacentTexProperty, adjacentTexture);
        }

        public void HighlightNeighbors(int provinceId)
        {
            SetHighlightEnabled(true);
            BuildAdjacentTexture(provinceId);
            ApplyAdjacentTexture();
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(MapAdjacentHighlighter))]
    public class MapAdjacentHighlighterEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var highlighter = (MapAdjacentHighlighter)target;
            if (GUILayout.Button("Apply Adjacent Texture"))
            {
                highlighter.BuildAdjacentTexture(highlighter.testProvinceId);
                highlighter.ApplyAdjacentTexture();
            }
        }
    }
#endif
}