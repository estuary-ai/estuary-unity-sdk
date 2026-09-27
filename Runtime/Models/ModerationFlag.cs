using System;
using Newtonsoft.Json;

namespace Estuary.Models
{
    /// <summary>Replace displayed text and remove media for MessageId with Message.</summary>
    [Serializable]
    public class ModerationFlag
    {
        [JsonProperty("message_id")] public string MessageId;
        [JsonProperty("action")] public string Action;
        [JsonProperty("categories")] public string[] Categories = Array.Empty<string>();
        [JsonProperty("message")] public string Message;
    }
}
