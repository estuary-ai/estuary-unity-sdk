using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

        /// <summary>
        /// Seed motive (contract v1.7): the character's private inner drive.
        /// Only populated on owner-scoped v1 character reads — the legacy
        /// /api/agents payload deliberately omits it (privacy), so expect null
        /// from GetAgents.
        /// </summary>
        [JsonProperty("motive")] public string Motive;

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

        [JsonProperty("player_id")] public string PlayerId;
        [JsonProperty("tts_provider")] public string TtsProvider;
        [JsonProperty("tts_model")] public string TtsModel;
        [JsonProperty("tts_voice")] public string TtsVoice;
        [JsonProperty("tts_speed")] public float TtsSpeed;
        [JsonProperty("tts_delivery_mode")] public string TtsDeliveryMode;
        [JsonProperty("llm_provider")] public string LlmProvider;
        [JsonProperty("llm_model")] public string LlmModel;
        [JsonProperty("stt_provider")] public string SttProvider;
        [JsonProperty("stt_language")] public string SttLanguage;
        [JsonProperty("stt_language_hints")] public string[] SttLanguageHints;
        [JsonProperty("stt_context_terms")] public string[] SttContextTerms;
        [JsonProperty("response_language")] public string ResponseLanguage;
        [JsonProperty("text_only")] public bool TextOnly;
        [JsonProperty("actions")] public JArray Actions;
        [JsonProperty("api_endpoints")] public JArray ApiEndpoints;
        [JsonProperty("model_rigged")] public bool ModelRigged;
        [JsonProperty("model_animations")] public string[] ModelAnimations;
        [JsonExtensionData] public System.Collections.Generic.IDictionary<string, JToken> AdditionalFields;
        [JsonProperty("modelRigged")] private bool LegacyModelRigged { set => ModelRigged = value; }
        [JsonProperty("modelAnimations")] private string[] LegacyModelAnimations { set => ModelAnimations = value; }
        [JsonProperty("model_url")] private string V1ModelUrl { set => ModelUrl = value; }
        [JsonProperty("model_preview_url")] private string V1ModelPreviewUrl { set => ModelPreviewUrl = value; }
        [JsonProperty("model_status")] private string V1ModelStatus { set => ModelStatus = value; }
        [JsonProperty("source_image_url")] private string V1SourceImageUrl { set => SourceImageUrl = value; }
        [JsonProperty("model_provider")] private string V1ModelProvider { set => ModelProvider = value; }
        [JsonProperty("generated_voice_id")] private string V1GeneratedVoiceId { set => GeneratedVoiceId = value; }

        /// <summary>True when a textured or preview GLB is ready to load into a scene.</summary>
        public bool HasLoadableModel =>
            ModelStatus == "completed" || ModelStatus == "texture_failed" ||
            ((ModelStatus == "rig_failed" || ModelStatus == "animation_failed") && !string.IsNullOrEmpty(ModelUrl));

        /// <summary>
        /// The best URL to load: the textured model when available, otherwise the
        /// untextured preview (used when texturing failed but the mesh is usable).
        /// </summary>
        public string BestModelUrl =>
            !string.IsNullOrEmpty(ModelUrl) ? ModelUrl : ModelPreviewUrl;
    }

    /// <summary>
    /// Response model for the model status polling endpoint.
    /// Matches GET /api/v1/characters/{characterId}/model.
    /// </summary>
    public class ModelStatusResponse
    {
        [JsonProperty("modelStatus")] public string ModelStatus;
        [JsonProperty("modelPreviewUrl")] public string ModelPreviewUrl;
        [JsonProperty("modelUrl")] public string ModelUrl;
        [JsonProperty("thumbnailUrl")] public string ThumbnailUrl;
        [JsonProperty("progress")] public int Progress;

        [JsonProperty("characterId")] public string CharacterId;
        [JsonProperty("rigged")] public bool Rigged;
        [JsonProperty("animations")] public string[] Animations = System.Array.Empty<string>();
        public bool IsInProgress => ModelStatus == "generating" || ModelStatus == "preview_ready" ||
            ModelStatus == "posing" || ModelStatus == "rig_checking" || ModelStatus == "rigging" || ModelStatus == "animating";
        public bool IsPartialSuccess => IsTextureFailed ||
            ((ModelStatus == "rig_failed" || ModelStatus == "animation_failed") && !string.IsNullOrEmpty(ModelUrl));
        public bool IsCompleted => ModelStatus == "completed";
        public bool IsFailed => ModelStatus == "failed" ||
            ((ModelStatus == "rig_failed" || ModelStatus == "animation_failed") && string.IsNullOrEmpty(ModelUrl));
        public bool IsTextureFailed => ModelStatus == "texture_failed";
    }
}
