using System;
using Newtonsoft.Json;

namespace Estuary.Models
{
    /// <summary>Fields for creating or patching a memory. Null fields are omitted.</summary>
    [Serializable]
    public class MemoryWriteRequest
    {
        [JsonProperty("content")] public string Content;
        [JsonProperty("memoryType")] public string MemoryType;
        [JsonProperty("importance")] public float? Importance;
        [JsonProperty("memoryLayer")] public string MemoryLayer;
        [JsonProperty("confidence")] public float? Confidence;
    }
}
