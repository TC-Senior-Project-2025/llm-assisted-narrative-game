using System.Collections.Generic;
using AYellowpaper.SerializedCollections;
using UnityEngine;

namespace Game.World.Map
{
    [CreateAssetMenu(fileName = "Map Data", menuName = "Scriptable Objects/Map/Map Data v2")]
    public class MapData : ScriptableObject
    {
        public SerializedDictionary<int, string> provinceIdToHexCode;
        // public SerializedDictionary<int, List<int>> countryCodeToProvinceId;
    }
}
