using System;
using Newtonsoft.Json;

namespace Estuary.Models
{
    /// <summary>Memories grouped by day, week or month.</summary>
    [Serializable]
    public class MemoryTimeline
    {
        [JsonProperty("timeline")] public MemoryTimelineEntry[] Timeline;
        [JsonProperty("totalMemories")] public int TotalMemories;
        [JsonProperty("groupBy")] public string GroupBy;
    }
}
