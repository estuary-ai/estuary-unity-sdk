using System;
using Newtonsoft.Json;

namespace Estuary.Models
{
    /// <summary>A page of memories for one character and player.</summary>
    [Serializable]
    public class MemoryList
    {
        [JsonProperty("memories")] public MemoryData[] Memories;
        [JsonProperty("total")] public int Total;
        [JsonProperty("limit")] public int Limit;
        [JsonProperty("offset")] public int Offset;
    }
}
