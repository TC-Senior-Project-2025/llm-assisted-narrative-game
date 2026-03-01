using System.IO;
using Newtonsoft.Json;
using UnityEngine;

namespace Game.Services
{
    public static class JsonLoader
    {
        public static T Load<T>(string relativePath)
        {
            string path = Path.Combine(Application.streamingAssetsPath, relativePath);
            string json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<T>(json);
        }
    }
}