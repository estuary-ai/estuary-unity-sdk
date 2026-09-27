using System;
using Newtonsoft.Json;

namespace Estuary.Models
{
    /// <summary>A matching memory with similarity and text.</summary>
    [Serializable]
    public class MemorySearchMatch
    {
        [JsonProperty("memoryId")] public string MemoryId;
        [JsonProperty("similarity")] public float Similarity;
        [JsonProperty("content")] public string Content;
    }
}
