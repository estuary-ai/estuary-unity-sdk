using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Estuary.Models
{
    /// <summary>Heterogeneous memory nodes and edges; their JSON fields are preserved.</summary>
    [Serializable]
    public class MemoryGraph
    {
        [JsonProperty("nodes")] public JArray Nodes;
        [JsonProperty("edges")] public JArray Edges;
        [JsonProperty("stats")] public JObject Stats;
        [JsonProperty("stale")] public bool Stale;
    }
}
