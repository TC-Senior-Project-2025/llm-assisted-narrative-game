using System.Threading.Tasks;
using UnityEngine;

namespace Game.Services
{
    public static class ResourcesService
    {
        public static string LoadPrompt(string promptName)
        {
            var textAsset = Resources.Load<TextAsset>($"Prompts/{promptName}");

            if (textAsset == null)
            {
                Debug.LogError($"Prompt not found: Assets/Resources/Prompts/{promptName}.txt");
                return string.Empty;
            }

            return textAsset.text;
        }

        public static async Task<string> LoadPromptAsync(string promptName)
        {
            var resourceRequest = Resources.LoadAsync<TextAsset>($"Prompts/{promptName}");
            await resourceRequest;

            if (resourceRequest.asset == null)
            {
                Debug.LogError($"Prompt not found: Assets/Resources/Prompts/{promptName}.txt");
                return null;
            }

            return ((TextAsset)resourceRequest.asset).text;
        }
    }
}