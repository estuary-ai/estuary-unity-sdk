using System;
using Newtonsoft.Json;

namespace Estuary.Models
{
    /// <summary>A chronological page of messages with pagination metadata.</summary>
    [Serializable]
    public class ConversationMessages
    {
        [JsonProperty("messages")] public ConversationMessage[] Messages;
        [JsonProperty("pagination")] public MessagePagination Pagination;
    }
}
