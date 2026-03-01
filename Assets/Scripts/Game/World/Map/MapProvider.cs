using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using AYellowpaper.SerializedCollections;
using Core;
using UnityEngine;
using Game.Services;


#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Game.World.Map
{
    public class MapProvider : MonoBehaviour
    {
        public MapData mapData;

        // [SerializeField] private SerializedDictionary<int, List<int>> countryCodeToProvinceId;
        [SerializeField] private SerializedDictionary<string, int> hexCodeToProvinceId;
        // [SerializeField] private SerializedDictionary<int, int> provinceIdToCountryId;
        // [SerializeField] private SerializedDictionary<Color32, int> colorToCountryId;
        [SerializeField] private SerializedDictionary<Color32, int> colorToProvinceId;
        [SerializeField] private SerializedDictionary<int, Color32> provinceIdToColor;

        private void Start()
        {
            PrebuildData();
        }

        public void PrebuildData()
        {
            // countryCodeToProvinceId = new(
            //     GameService.Main.state.CurrentValue.Commandery
            //         .GroupBy(kv => kv.Value.CountryId)
            //         .ToDictionary(
            //             g => g.Key,
            //             g => g.Select(kv => kv.Key).ToList()
            //         ));

            hexCodeToProvinceId = new SerializedDictionary<string, int>(
                mapData.provinceIdToHexCode.ToDictionary(kv => kv.Value, kv => kv.Key)
            );

            // provinceIdToCountryId = new SerializedDictionary<int, int>();
            // foreach (var (countryId, provinceIds) in countryCodeToProvinceId)
            // {
            //     foreach (var provinceId in provinceIds)
            //     {
            //         provinceIdToCountryId.Add(provinceId, countryId);
            //     }
            // }

            // colorToCountryId = new SerializedDictionary<Color32, int>(new Color32Equality());
            // foreach (var (hexCode, provinceId) in hexCodeToProvinceId)
            // {
            //     ColorUtility.TryParseHtmlString("#" + hexCode, out var color);
            //     if (provinceIdToCountryId.ContainsKey(provinceId))
            //     {
            //         colorToCountryId.Add(color, provinceIdToCountryId[provinceId]);
            //     }
            // }

            colorToProvinceId = new SerializedDictionary<Color32, int>(new Color32Equality());
            foreach (var (hexCode, provinceId) in hexCodeToProvinceId)
            {
                ColorUtility.TryParseHtmlString("#" + hexCode, out var color);
                colorToProvinceId.Add(color, provinceId);
            }

            provinceIdToColor = new SerializedDictionary<int, Color32>();
            foreach (var (color, provinceId) in colorToProvinceId)
            {
                provinceIdToColor.Add(provinceId, color);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetProvinceId(Color32 color)
        {
            return colorToProvinceId[color];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetProvinceId(Color32 color, out int provinceId)
        {
            return colorToProvinceId.TryGetValue(color, out provinceId);
        }

        // [MethodImpl(MethodImplOptions.AggressiveInlining)]
        // public bool TryGetCountryId(Color32 color, out int countryId)
        // {
        //     var province = GameService.Main.state.CurrentValue.Commandery[colorToProvinceId[color]];
        //     return colorToCountryId.TryGetValue(color, out countryId);
        // }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetCountry(Color32 color)
        {
            var province = GameService.Main.State.CurrentValue.Commandery[colorToProvinceId[color]];
            return province.CountryId;
            // return colorToCountryId.GetValueOrDefault(color);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Dictionary<int, List<int>> GetCountries()
        {
            return GameService.Main.State.CurrentValue.Commandery
                .GroupBy(kv => kv.Value.CountryId)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(kv => kv.Key).ToList());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Color32 GetProvinceColor(int provinceId)
        {
            return provinceIdToColor[provinceId];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetProvinceCountryId(int provinceId)
        {
            return GameService.Main.State.CurrentValue.Commandery[provinceId].CountryId;
            // return provinceIdToCountryId[provinceId];
        }
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(MapProvider))]
    public class MapProviderEditor : Editor
    {
        private MapProvider Target => (MapProvider)target;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            if (GUILayout.Button("Prebuild Data"))
            {
                Target.PrebuildData();
            }
        }
    }
#endif
}
