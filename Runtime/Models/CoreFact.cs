using System;
using Newtonsoft.Json;

namespace Estuary.Models
{
    /// <summary>One stable fact about a player.</summary>
    [Serializable]
    public class CoreFact
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("userId")] public string UserId;
        [JsonProperty("agentId")] public string AgentId;
        [JsonProperty("playerId")] public string PlayerId;
        [JsonProperty("factKey")] public string FactKey;
        [JsonProperty("factValue")] public string FactValue;
        [JsonProperty("sourceMemoryId")] public string SourceMemoryId;
        [JsonProperty("createdAt")] public string CreatedAt;
        [JsonProperty("updatedAt")] public string UpdatedAt;
    }
}
