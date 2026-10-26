using System;
using Newtonsoft.Json;

namespace Estuary.Models
{
    /// <summary>One page of accessible characters.</summary>
    [Serializable]
    public class CharacterList
    {
        [JsonProperty("characters")] public AgentResponse[] Characters;
        [JsonProperty("total")] public int Total;
        [JsonProperty("limit")] public int Limit;
        [JsonProperty("offset")] public int Offset;
    }
}
