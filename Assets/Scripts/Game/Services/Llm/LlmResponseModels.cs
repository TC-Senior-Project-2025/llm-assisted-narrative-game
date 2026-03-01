using Newtonsoft.Json;
using System.Collections.Generic;

namespace Game.Services.Llm
{
    public class ApiResponse
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("provider")]
        public string Provider { get; set; }

        [JsonProperty("model")]
        public string Model { get; set; }

        [JsonProperty("object")]
        public string ObjectType { get; set; }

        [JsonProperty("created")]
        public long Created { get; set; }

        [JsonProperty("choices")]
        public List<ApiChoice> Choices { get; set; }

        [JsonProperty("system_fingerprint")]
        public string SystemFingerprint { get; set; }

        [JsonProperty("usage")]
        public ApiUsage Usage { get; set; }
    }

    public class ApiChoice
    {
        [JsonProperty("logprobs")]
        public object Logprobs { get; set; }

        [JsonProperty("finish_reason")]
        public string FinishReason { get; set; }

        [JsonProperty("native_finish_reason")]
        public string NativeFinishReason { get; set; }

        [JsonProperty("index")]
        public int Index { get; set; }

        [JsonProperty("message")]
        public ApiMessage Message { get; set; }

        [JsonProperty("delta")]
        public ApiMessage Delta { get; set; }
    }

    public class ApiMessage
    {
        [JsonProperty("role")]
        public string Role { get; set; }

        [JsonProperty("content")]
        public string Content { get; set; }

        [JsonProperty("refusal")]
        public object Refusal { get; set; }

        [JsonProperty("reasoning")]
        public object Reasoning { get; set; }
    }

    public class ApiUsage
    {
        [JsonProperty("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonProperty("completion_tokens")]
        public int CompletionTokens { get; set; }

        [JsonProperty("total_tokens")]
        public int TotalTokens { get; set; }

        [JsonProperty("prompt_tokens_details")]
        public TokenDetails PromptTokensDetails { get; set; }

        [JsonProperty("completion_tokens_details")]
        public TokenDetails CompletionTokensDetails { get; set; }
    }

    public class TokenDetails
    {
        [JsonProperty("cached_tokens")]
        public int CachedTokens { get; set; }

        [JsonProperty("audio_tokens")]
        public int AudioTokens { get; set; }

        [JsonProperty("reasoning_tokens")]
        public int? ReasoningTokens { get; set; }
    }
}
