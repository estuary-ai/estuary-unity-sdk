using System;
using Newtonsoft.Json;

namespace Estuary.Models
{
    /// <summary>Images and citations returned by a character's API tool, separate from character prose.</summary>
    [Serializable]
    public class ApiEndpointResult
    {
        [JsonProperty("tool_call_id")] public string ToolCallId;
        [JsonProperty("operation")] public string Operation;
        [JsonProperty("status")] public string Status;
        [JsonProperty("media")] public ApiEndpointMedia[] Media = Array.Empty<ApiEndpointMedia>();
        [JsonProperty("citations")] public ApiEndpointCitation[] Citations = Array.Empty<ApiEndpointCitation>();
        [JsonProperty("message_id")] public string MessageId;
        [JsonProperty("timestamp")] public string Timestamp;
    }

}
