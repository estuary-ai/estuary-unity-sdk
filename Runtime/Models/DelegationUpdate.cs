using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Estuary.Models
{
    /// <summary>Authoritative status of a delegated task. URLs are data; opening them requires app/user intent.</summary>
    [Serializable]
    public class DelegationUpdate
    {
        [JsonProperty("status")] public string Status;
        [JsonProperty("invocation_id")] public string InvocationId;
        [JsonProperty("task")] public string Task;
        [JsonProperty("result")] public JToken Result;
        [JsonProperty("error")] public string Error;
        [JsonProperty("authorization_url")] public string AuthorizationUrl;
        [JsonProperty("message_id")] public string MessageId;
        [JsonProperty("timestamp")] public string Timestamp;
    }
}
