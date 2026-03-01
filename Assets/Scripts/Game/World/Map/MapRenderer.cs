using System;
using System.Collections.Generic;
using AYellowpaper.SerializedCollections;
using Core;
using Extensions;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Game.World.Map
{
    public class MapRenderer : MonoBehaviour
    {
        [Header("Settings")]
        public bool useLandColor = true;
        public GUIStyle labelStyle;

        [Header("Input Texture")]
        public Texture2D provinceMapTexture;
        public float pixelsPerUnit = 100;

        [Header("Color Pickers")]
        public Color32 seaColorPicker = Color.black;
        public Color32 outsideColorPicker = Color.white;

        [Header("Fill Colors")]
        public Color32 landColor = Color.gray2;
        public Color32 outsideColor = Color.gray1;
        public Color32 seaColor = Color.black;

        [Header("Borders")]
        public bool showProvinceBorders = true;
        public bool showCountryBorders = true;
        public Color32 provinceBorderColor = Color.gray4;
        public Color32 countryBorderColor = Color.gray6;

        [Serializable]
        public struct Area
        {
            public Color32 color;
            public Vector3 centroid;
            public Bounds bounds;
        }

        [Header("Areas")]
        [SerializeField, SerializedDictionary("Color", "Area Data")]
        private SerializedDictionary<Color32, Area> areas;

        [SerializeField] private SpriteRenderer spriteRenderer;
        [SerializeField] private MapProvider mapProvider;

        private Color32[] _outputBuffer;


        private void OnDrawGizmos()
        {
#if UNITY_EDITOR
            Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;

            foreach (var (color, area) in GetAreas())
            {
                if (color.Equals(seaColorPicker) || color.Equals(outsideColorPicker)) continue;

                var pos = area.centroid;
                pos.z = spriteRenderer.transform.position.z;

                var hex = ColorUtility.ToHtmlStringRGBA(color);
                var style = labelStyle;

                if (mapProvider.TryGetProvinceId(color, out var id))
                {
                    hex = $"{id}";
                    style = new GUIStyle(labelStyle)
                    {
                        fontStyle = FontStyle.BoldAndItalic
                    };
                }

                Handles.Label(pos, hex, style);
            }
#endif
        }

        public Vector3 GetProvinceCenter(Color provinceColor)
        {
            return areas[provinceColor].centroid;
        }

        public Vector3 GetProvinceCenter(int provinceId)
        {
            var color = mapProvider.GetProvinceColor(provinceId);
            return areas[color].centroid;
        }

        public Bounds GetProvinceBounds(Color provinceColor)
        {
            return areas[provinceColor].bounds;
        }

        public void Render()
        {
            var width = provinceMapTexture.width;
            var height = provinceMapTexture.height;
            var pixels = provinceMapTexture.GetPixels32();

            areas = new SerializedDictionary<Color32, Area>(new Color32Equality());

            CalculateAreas(pixels);
            Draw(pixels);

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false, linear: true)
            {
                name = $"{name}_RenderedMap",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            texture.SetPixels32(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);

            var sprite = Sprite.Create(texture, new Rect(0, 0, width, height), new Vector2(0.5f, 0.5f), pixelsPerUnit);
            spriteRenderer.sprite = sprite;
        }

        public IEnumerable<KeyValuePair<Color32, Area>> GetAreas()
        {
            return areas;
        }

        private void Draw(Color32[] pixels)
        {
            var width = provinceMapTexture.width;
            var height = provinceMapTexture.height;

            var n = pixels.Length;
            if (_outputBuffer == null || _outputBuffer.Length != n)
                _outputBuffer = new Color32[n];

            for (var y = 0; y < height; y++)
            {
                var row = y * width;
                for (var x = 0; x < width; x++)
                {
                    var i = x + row;
                    var pixel = pixels[i];

                    // Pass 1: Base color
                    var baseColor = pixel.SameAs(seaColorPicker) ? seaColor
                        : pixel.SameAs(outsideColorPicker) ? outsideColor
                        : (useLandColor ? landColor : pixel);

                    // Pass 2: Border color
                    var isBorder = false;
                    var isCountryBorder = false;

                    if (showCountryBorders || showProvinceBorders)
                    {
                        if (x > 0 && x < width - 1 && y > 0 && y < height - 1)
                        {
                            var country = mapProvider.GetCountry(pixel);
                            var left = pixels[i - 1];
                            var right = pixels[i + 1];
                            var up = pixels[i - width];
                            var down = pixels[i + width];

                            if (showCountryBorders)
                            {
                                isCountryBorder =
                                    country != mapProvider.GetCountry(left) ||
                                    country != mapProvider.GetCountry(right) ||
                                    country != mapProvider.GetCountry(up) ||
                                    country != mapProvider.GetCountry(down);
                            }

                            if (isCountryBorder)
                            {
                                isBorder = true;
                            }
                            else
                            {
                                if (showProvinceBorders)
                                {
                                    var isProvinceBorder =
                                        !pixel.SameAs(left) ||
                                        !pixel.SameAs(right) ||
                                        !pixel.SameAs(up) ||
                                        !pixel.SameAs(down);

                                    if (isProvinceBorder)
                                    {
                                        isBorder = true;
                                    }
                                }
                            }
                        }
                    }

                    _outputBuffer[i] = isBorder ?
                        (isCountryBorder ? countryBorderColor : provinceBorderColor) : baseColor;
                }
            }

            Array.Copy(_outputBuffer, pixels, pixels.Length);
        }

        private void CalculateAreas(Color32[] pixels)
        {
            var width = provinceMapTexture.width;
            var height = provinceMapTexture.height;

            var sum = new Dictionary<Color32, Vector2Int>(new Color32Equality());
            var count = new Dictionary<Color32, int>(new Color32Equality());
            var minX = new Dictionary<Color32, int>(new Color32Equality());
            var maxX = new Dictionary<Color32, int>(new Color32Equality());
            var minY = new Dictionary<Color32, int>(new Color32Equality());
            var maxY = new Dictionary<Color32, int>(new Color32Equality());

            for (var y = 0; y < height; y++)
            {
                var row = y * width;
                for (var x = 0; x < width; x++)
                {
                    var pixel = pixels[x + row];

                    if (sum.TryGetValue(pixel, out var s))
                    {
                        sum[pixel] = s + new Vector2Int(x, y);
                        count[pixel] += 1;
                        if (x < minX[pixel]) minX[pixel] = x;
                        if (x > maxX[pixel]) maxX[pixel] = x;
                        if (y < minY[pixel]) minY[pixel] = y;
                        if (y > maxY[pixel]) maxY[pixel] = y;
                    }
                    else
                    {
                        sum[pixel] = new Vector2Int(x, y);
                        count[pixel] = 1;
                        minX[pixel] = x;
                        maxX[pixel] = x;
                        minY[pixel] = y;
                        maxY[pixel] = y;
                    }
                }
            }

            foreach (var (color, s) in sum)
            {
                var centroid = PixelToWorld(s / count[color]);
                var min = PixelToWorld(new Vector2(minX[color], minY[color]));
                var max = PixelToWorld(new Vector2(maxX[color], maxY[color]));

                var bounds = new Bounds();
                bounds.SetMinMax(min, max);

                areas[color] = new Area
                {
                    color = color,
                    centroid = centroid,
                    bounds = bounds
                };
            }
        }

        private Vector3 PixelToWorld(Vector2 pixel)
        {
            var texture = provinceMapTexture;
            var width = texture.width;
            var height = texture.height;

            var localX = (pixel.x + 0.5f - width * 0.5f) / pixelsPerUnit;
            var localY = (pixel.y + 0.5f - height * 0.5f) / pixelsPerUnit;

            var local = new Vector3(localX, localY, 0f);

            return spriteRenderer.transform.TransformPoint(local);
        }
    }

#if UNITY_EDITOR

    [CustomEditor(typeof(MapRenderer))]
    public class MapRendererEditor : Editor
    {
        private MapRenderer Target => (MapRenderer)target;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (GUILayout.Button("Render Map"))
            {
                Target.Render();
            }
        }
    }

#endif
}
