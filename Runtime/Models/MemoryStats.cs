using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Estuary.Models
{
    /// <summary>Memory counts by status, type and layer.</summary>
    [Serializable]
    public class MemoryStats
    {
        [JsonProperty("totalActive")] public int TotalActive;
        [JsonProperty("totalSuperseded")] public int TotalSuperseded;
        [JsonProperty("totalDecayed")] public int TotalDecayed;
        [JsonProperty("byType")] public Dictionary<string, int> ByType;
        [JsonProperty("coreFacts")] public int CoreFacts;
        [JsonProperty("byLayer")] public Dictionary<string, int> ByLayer;
        [JsonProperty("lastLayerTransferAt")] public string LastLayerTransferAt;
        [JsonProperty("recentPromotions7d")] public int RecentPromotions7d;
        [JsonProperty("recentDemotions7d")] public int RecentDemotions7d;
    }
}
