using System;
using Newtonsoft.Json;

namespace Estuary.Models
{
    /// <summary>A filtered, sorted page of conversations.</summary>
    [Serializable]
    public class ConversationList
    {
        [JsonProperty("conversations")] public ConversationRecord[] Conversations;
        [JsonProperty("total")] public int Total;
        [JsonProperty("limit")] public int Limit;
        [JsonProperty("offset")] public int Offset;
        [JsonProperty("sortBy")] public string SortBy;
        [JsonProperty("sortOrder")] public string SortOrder;
    }
}
