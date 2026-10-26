using System;
using Newtonsoft.Json;

namespace Estuary.Models
{
    /// <summary>Server error with an optional machine-readable code, including rate_limited. Never automatically retried.</summary>
    [Serializable]
    public class ServerError
    {
        [JsonProperty("error")] public string Code;
        [JsonProperty("message")] public string Message;
    }
}
