using System;
using Newtonsoft.Json;

namespace Estuary.Models
{
    /// <summary>Memories in one date bucket.</summary>
    [Serializable]
    public class MemoryTimelineEntry
    {
        [JsonProperty("date")] public string Date;
        [JsonProperty("memories")] public MemoryData[] Memories;
    }
}
