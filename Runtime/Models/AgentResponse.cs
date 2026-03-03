using Newtonsoft.Json;

namespace Estuary.Models
{
    /// <summary>
    /// Response model for agent/character data from the backend API.
    /// Matches the camelCase JSON returned by Agent.to_dict() on the server.
    /// </summary>
    public class AgentResponse
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("name")] public string Name;
        [JsonProperty("tagline")] public string Tagline;
        [JsonProperty("personality")] public string Personality;
        [JsonProperty("background")] public string Background;
        [JsonProperty("avatar")] public string Avatar;
        [JsonProperty("appearance")] public string Appearance;
        [JsonProperty("modelUrl")] public string ModelUrl;
        [JsonProperty("modelPreviewUrl")] public string ModelPreviewUrl;
        [JsonProperty("modelStatus")] public string ModelStatus;
        [JsonProperty("sourceImageUrl")] public string SourceImageUrl;
    }

    /// <summary>
    /// Response model for the model status polling endpoint.
    /// Matches GET /api/generate/{agent_id}/model-status response.
    /// </summary>
    public class ModelStatusResponse
    {
        [JsonProperty("modelStatus")] public string ModelStatus;
        [JsonProperty("modelPreviewUrl")] public string ModelPreviewUrl;
        [JsonProperty("modelUrl")] public string ModelUrl;
        [JsonProperty("thumbnailUrl")] public string ThumbnailUrl;
        [JsonProperty("progress")] public int Progress;

        public bool IsInProgress => ModelStatus == "generating" || ModelStatus == "preview_ready";
        public bool IsCompleted => ModelStatus == "completed";
        public bool IsFailed => ModelStatus == "failed";
        public bool IsTextureFailed => ModelStatus == "texture_failed";
    }
}
