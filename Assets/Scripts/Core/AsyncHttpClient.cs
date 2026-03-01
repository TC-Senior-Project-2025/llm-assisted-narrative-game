using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace Core
{
    public class AsyncHttpClient
    {
        public string BaseUrl;
        public string AccessToken;
        
        public AsyncHttpClient(string baseUrl)
        {
            this.BaseUrl = baseUrl;
        }

        public async Task<TRes> GetAsync<TRes>(string path)
        {
            using var request = UnityWebRequest.Get($"{BaseUrl}{path}");
            request.SetRequestHeader("Authorization", $"Bearer {AccessToken}");
            await request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                throw new System.Exception($"Error in POST request: {request.error}");
            }

            var resText = request.downloadHandler.text;
            return JsonConvert.DeserializeObject<TRes>(resText);
        }

        private static string PrefixPath(string path)
        {
            if (path.Length == 0)
            {
                path = "/";
            }
            else if (path[0] != '/')
            {
                path = $"/{path}";
            }

            return path;
        }
        
        public async Task<string> PostJsonAsync(string path, object payload = null)
        {
            var prefixedPath = PrefixPath(path);
            
            using var request = new UnityWebRequest($"{BaseUrl}{prefixedPath}", "POST");
            request.SetRequestHeader("Authorization", $"Bearer {AccessToken}");

            if (payload != null)
            {
                var json = JsonConvert.SerializeObject(payload);
                var bodyRaw = Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.SetRequestHeader("Content-Type", "application/json");
            }
           
            request.downloadHandler = new DownloadHandlerBuffer();

            await request.SendWebRequest();

            return request.result != UnityWebRequest.Result.Success 
                ? throw new System.Exception($"Error in POST request: {request.error}") 
                : request.downloadHandler.text;
        }
        
        public async Task<TRes> PostJsonAsync<TRes>(string path, object payload = null)
        {
            var resText = await PostJsonAsync(path, payload);
            return JsonConvert.DeserializeObject<TRes>(resText);
        }
        
        public async Task DeleteAsync(string path, object payload = null)
        {
            var prefixedPath = PrefixPath(path);
            
            using var request = new UnityWebRequest($"{BaseUrl}{prefixedPath}", "DELETE");
            request.SetRequestHeader("Authorization", $"Bearer {AccessToken}");

            if (payload != null)
            {
                var json = JsonConvert.SerializeObject(payload);
                var bodyRaw = Encoding.UTF8.GetBytes(json);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.SetRequestHeader("Content-Type", "application/json");
            }

            await request.SendWebRequest();
        }
    }
}
