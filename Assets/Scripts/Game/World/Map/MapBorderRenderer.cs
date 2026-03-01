#if UNITY_EDITOR
using UnityEditor;
#endif

using System;
using System.Collections.Generic;
using System.Linq;
using Core;
using UnityEngine;

namespace Game.World.Map
{
    public class MapBorderRenderer : MonoBehaviour
    {
        private static readonly int CountryTexProperty = Shader.PropertyToID("_CountryTex");
        private static readonly int OwnerColorStrengthProperty = Shader.PropertyToID("_OwnerColorStrength");

        [Header("Dependencies")]
        public SpriteRenderer spriteRenderer;
        public MapRenderer mapRenderer;
        public MapProvider mapProvider;
        public MapConnections mapConnections;

        [Header("Settings")]
        [Range(0f, 1f)]
        public float ownerColorStrength = 0.5f;

        public List<Color32> countryColors = new List<Color32>()
        {
            new Color32(200, 200, 200, 255), // 0
            new Color32(255, 50, 50, 255),   // 1 Qin
            new Color32(50, 50, 255, 255),   // 2 Chu
            new Color32(50, 255, 50, 255),   // 3 Qi
            new Color32(255, 255, 0, 255),   // 4 Zhao
            new Color32(0, 255, 255, 255),   // 5 Wei
            new Color32(255, 0, 255, 255),   // 6 Yan
            new Color32(255, 128, 0, 255),   // 7 Han
        };

        [Header("Output")]
        public Texture2D countryTexture;

        private void Start()
        {
            BuildCountryTexture();
            ApplyTexture();
        }

        public void Render()
        {
            BuildCountryTexture();
            ApplyTexture();
        }

        public void BuildCountryTexture()
        {
            var original = mapRenderer.provinceMapTexture;
            var pixels = original.GetPixels32();

            var countries = new Dictionary<int, Color32[]>();
            foreach (var country in mapProvider.GetCountries())
            {
                var provinceColors = country.Value.Select(provinceId => mapProvider.GetProvinceColor(provinceId));
                countries[country.Key] = provinceColors.ToArray();
            }

            var provinceColorToCountryColor = new Dictionary<Color32, Color32>(new Color32Equality());

            foreach (var (countryId, provinceColors) in countries)
            {
                // Use the defined color if available, otherwise fallback to black or a default
                var countryColor = (countryId >= 0 && countryId < countryColors.Count)
                    ? countryColors[countryId]
                    : new Color32(0, 0, 0, 0); // Transparent/Black if undefined

                // Ensure alpha is fully opaque for visibility if the user provided color has alpha 0, 
                // though usually we want to control opacity in shader. 
                if (countryColor.a == 0) countryColor.a = 255;

                foreach (var provinceColor in provinceColors)
                {
                    provinceColorToCountryColor[provinceColor] = countryColor;
                }
            }

            for (var i = 0; i < pixels.Length; i++)
            {
                if (provinceColorToCountryColor.TryGetValue(pixels[i], out var countryColor))
                {
                    pixels[i] = countryColor;
                }
                else
                {
                    pixels[i] = new Color32(0, 0, 0, 0);
                }
            }

            if (countryTexture == null ||
                countryTexture.width != original.width ||
                countryTexture.height != original.height)
            {
                countryTexture = new Texture2D(
                        original.width,
                        original.height,
                        TextureFormat.RGBA32,
                        mipChain: false,
                        linear: true)
                { name = $"{name}_CountryTex", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            }

            countryTexture.SetPixels32(pixels);
            countryTexture.Apply(false, false);
        }

        public void ApplyTexture()
        {
            if (countryTexture == null)
            {
                throw new Exception("Country texture is null");
            }
            spriteRenderer.sharedMaterial.SetTexture(CountryTexProperty, countryTexture);
            spriteRenderer.sharedMaterial.SetFloat(OwnerColorStrengthProperty, ownerColorStrength);
        }

        private void OnValidate()
        {
            if (spriteRenderer != null && spriteRenderer.sharedMaterial != null)
            {
                spriteRenderer.sharedMaterial.SetFloat(OwnerColorStrengthProperty, ownerColorStrength);
            }
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(MapBorderRenderer))]
    public class MapBorderRendererEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var mapBorderRenderer = (MapBorderRenderer)target;
            if (GUILayout.Button("Apply Country Texture"))
            {
                mapBorderRenderer.BuildCountryTexture();
                mapBorderRenderer.ApplyTexture();
            }
        }
    }
#endif
}
