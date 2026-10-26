using System;
using Newtonsoft.Json;

namespace Estuary.Models
{
    /// <summary>A stored text or image message in a conversation.</summary>
    [Serializable]
    public class ConversationMessage
    {
        [JsonProperty("id")] public long Id;
        [JsonProperty("conversationId")] public string ConversationId;
        [JsonProperty("role")] public string Role;
        [JsonProperty("content")] public string Content;
        [JsonProperty("speaker")] public string Speaker;
        [JsonProperty("timestamp")] public string Timestamp;
        [JsonProperty("imageUrl")] public string ImageUrl;
        [JsonProperty("imageDescription")] public string ImageDescription;
    }
}
