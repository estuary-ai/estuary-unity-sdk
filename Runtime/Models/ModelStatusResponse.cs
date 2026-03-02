using System;
using Newtonsoft.Json;

namespace Estuary.Models
{
    /// <summary>
    /// Response model for GET /api/generate/{agent_id}/model-status.
    /// Maps the camelCase JSON fields returned by _status_response() on the backend.
    /// </summary>
    [Serializable]
    public class ModelStatusResponse
    {
        /// <summary>Current 3D model generation status (generating, preview_ready, completed, failed, texture_failed).</summary>
        [JsonProperty("modelStatus")]
        public string ModelStatus { get; set; }

        /// <summary>Preview GLB model URL (available after preview stage completes).</summary>
        [JsonProperty("modelPreviewUrl")]
        public string ModelPreviewUrl { get; set; }

        /// <summary>Final textured GLB model URL (available after refine stage completes).</summary>
        [JsonProperty("modelUrl")]
        public string ModelUrl { get; set; }

        /// <summary>Thumbnail image URL from Meshy preview render.</summary>
        [JsonProperty("thumbnailUrl")]
        public string ThumbnailUrl { get; set; }

        /// <summary>True when model is still being generated (status is "generating" or "preview_ready").</summary>
        public bool IsInProgress => ModelStatus == "generating" || ModelStatus == "preview_ready";

        /// <summary>True when model generation has fully completed.</summary>
        public bool IsCompleted => ModelStatus == "completed";

        /// <summary>True when model generation failed at any stage.</summary>
        public bool IsFailed => ModelStatus == "failed" || ModelStatus == "texture_failed";

        public override string ToString()
        {
            return $"ModelStatusResponse(ModelStatus={ModelStatus}, IsInProgress={IsInProgress}, IsCompleted={IsCompleted}, IsFailed={IsFailed})";
        }
    }
}
