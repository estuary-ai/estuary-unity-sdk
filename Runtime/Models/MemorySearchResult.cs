using System;
using Newtonsoft.Json;

namespace Estuary.Models
{
    /// <summary>Semantic memory matches and the search query.</summary>
    [Serializable]
    public class MemorySearchResult
    {
        [JsonProperty("results")] public MemorySearchMatch[] Results;
        [JsonProperty("query")] public string Query;
        [JsonProperty("total")] public int Total;
    }
}
