using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;

namespace Game.Services.Llm
{
    public class LlmService
    {
        private readonly HttpClient _httpClient;
        private readonly Model _model;
        private readonly LoggingService _logging = new();
        private readonly LlmLogger _llmLogger = new();

        public int TokensUsed { get; private set; }

        public enum Model
        {
            Gemini25Flash,
            Gemini3Flash,
            Llama33_70bInstruct,
            Grok41Fast,
            Claude35Haiku,
            GPTOss120b,
            ClaudeHaiku45,
            Qwen3Max,
            GPT4oMini
        }

        private static string GetModelString(Model model) =>
            model switch
            {
                Model.Gemini25Flash => "google/gemini-2.5-flash",
                Model.Gemini3Flash => "google/gemini-3-flash-preview",
                Model.Llama33_70bInstruct => "meta-llama/llama-3.3-70b-instruct",
                Model.Grok41Fast => "x-ai/grok-4.1-fast",
                Model.Claude35Haiku => "anthropic/claude-3.5-haiku",
                Model.GPTOss120b => "openai/gpt-oss-120b",
                Model.ClaudeHaiku45 => "anthropic/claude-haiku-4.5",
                Model.Qwen3Max => "qwen/qwen3-max",
                Model.GPT4oMini => "openai/gpt-4o-mini",
                _ => throw new Exception("Invalid model enum"),
            };

        public LlmService(HttpClient httpClient, string apiToken, Model model)
        {
            _httpClient = httpClient;
            _model = model;
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
        }

        public async Task<ApiResponse> AskTextAsync(
            string prompt,
            string systemPrompt = null,
            CancellationToken ct = default)
        {
            const string url = "https://openrouter.ai/api/v1/chat/completions";

            var messages = BuildMessages(prompt, systemPrompt);

            var payload = new
            {
                model = GetModelString(_model),
                messages
            };

            var json = JsonConvert.SerializeObject(payload, Formatting.Indented);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _httpClient.PostAsync(url, content, ct);
            response.EnsureSuccessStatusCode();

            var responseText = await response.Content.ReadAsStringAsync();
            var apiResponse = JsonConvert.DeserializeObject<ApiResponse>(responseText)
                              ?? throw new Exception("Failed to deserialize response content");

            if (apiResponse.Usage != null)
            {
                TokensUsed += apiResponse.Usage.TotalTokens;
                _logging.LogForService("LlmService", $"API call completed - Model: {GetModelString(_model)}, Tokens: {apiResponse.Usage.TotalTokens}");
                _logging.LogForService("LlmService", $"Result:\n{responseText}");
            }

            _llmLogger.Log(LlmLogger.LogType.Single, json, JsonConvert.SerializeObject(apiResponse, Formatting.Indented));

            return apiResponse;
        }

        public async IAsyncEnumerable<string> AskStreamAsync(
            string prompt,
            string systemPrompt = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            const string url = "https://openrouter.ai/api/v1/chat/completions";

            var messages = BuildMessages(prompt, systemPrompt);
            var responseBuffer = new StringBuilder();

            var payload = new
            {
                model = GetModelString(_model),
                stream = true,
                messages
            };

            var json = JsonConvert.SerializeObject(payload);

            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                ct);

            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream);

            ApiUsage lastUsage = null;

            try
            {
                while (!reader.EndOfStream && !ct.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (!line.StartsWith("data:")) continue;

                    var data = line.Substring("data:".Length).TrimStart();
                    if (data == "[DONE]") break;

                    ApiResponse partial;
                    try
                    {
                        partial = JsonConvert.DeserializeObject<ApiResponse>(data);
                    }
                    catch
                    {
                        continue;
                    }

                    if (partial?.Usage != null)
                        lastUsage = partial.Usage;

                    var delta = partial?.Choices?[0]?.Delta?.Content;
                    if (!string.IsNullOrEmpty(delta))
                    {
                        responseBuffer.Append(delta);
                        yield return delta;
                    }
                }
            }
            finally
            {
                // Log once, no matter how the stream ended
                _llmLogger.Log(
                    LlmLogger.LogType.Stream,
                    prompt,
                    responseBuffer.ToString()
                );

                if (lastUsage != null)
                {
                    TokensUsed += lastUsage.TotalTokens;
                    _logging.LogForService(
                        "LlmService",
                        $"Stream completed - Model: {GetModelString(_model)}, Tokens: {lastUsage.TotalTokens}"
                    );
                }
            }
        }


        private static List<object> BuildMessages(string prompt, string systemPrompt)
        {
            var messages = new List<object>();

            if (!string.IsNullOrEmpty(systemPrompt))
            {
                messages.Add(new
                {
                    role = "system",
                    content = new object[]
                    {
                        new { type = "text", text = systemPrompt }
                    }
                });
            }

            messages.Add(new
            {
                role = "user",
                content = new object[]
                {
                    new { type = "text", text = prompt }
                }
            });

            return messages;
        }
    }
}
