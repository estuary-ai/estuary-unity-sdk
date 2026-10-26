using System;
using Newtonsoft.Json;

namespace Estuary.Models
{
    /// <summary>Conversation metadata for one character and player.</summary>
    [Serializable]
    public class ConversationRecord
    {
        [JsonProperty("id")] public string Id;
        [JsonProperty("characterId")] public string CharacterId;
        [JsonProperty("playerId")] public string PlayerId;
        [JsonProperty("apiKeyId")] public string ApiKeyId;
        [JsonProperty("createdAt")] public string CreatedAt;
        [JsonProperty("lastActivity")] public string LastActivity;
        [JsonProperty("messageCount")] public int? MessageCount;
        [JsonProperty("lastMemoryExtractionAt")] public string LastMemoryExtractionAt;
    }
}
