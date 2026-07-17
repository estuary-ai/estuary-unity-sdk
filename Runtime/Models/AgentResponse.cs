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

        /// <summary>
        /// 3D model generation provider: "tripo" (default) or "meshy". Drives the
        /// orientation fix when instantiating the GLB — the two providers export
        /// with different forward axes. May be null on older agents.
        /// </summary>
        [JsonProperty("modelProvider")] public string ModelProvider;

        /// <summary>Voice id generated for this character (if any). Null when not generated.</summary>
        [JsonProperty("generatedVoiceId")] public string GeneratedVoiceId;

        /// <summary>True when a textured or preview GLB is ready to load into a scene.</summary>
        public bool HasLoadableModel =>
            ModelStatus == "completed" || ModelStatus == "texture_failed";

        /// <summary>
        /// The best URL to load: the textured model when available, otherwise the
        /// untextured preview (used when texturing failed but the mesh is usable).
        /// </summary>
        public string BestModelUrl =>
            !string.IsNullOrEmpty(ModelUrl) ? ModelUrl : ModelPreviewUrl;
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
