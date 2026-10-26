using System;
using Newtonsoft.Json;

namespace Estuary.Models
{
    /// <summary>Aggregate conversation counts and activity dates.</summary>
    [Serializable]
    public class ConversationStats
    {
        [JsonProperty("totalConversations")] public int TotalConversations;
        [JsonProperty("totalMessages")] public int TotalMessages;
        [JsonProperty("firstActivity")] public string FirstActivity;
        [JsonProperty("lastActivity")] public string LastActivity;
    }
}
