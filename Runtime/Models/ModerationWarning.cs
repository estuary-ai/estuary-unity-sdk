using System;
using Newtonsoft.Json;

namespace Estuary.Models
{
    /// <summary>A session warning, or termination followed by disconnect. Termination requires explicit reconnect.</summary>
    [Serializable]
    public class ModerationWarning
    {
        [JsonProperty("level")] public string Level;
        [JsonProperty("message")] public string Message;
        public bool IsTerminated => Level == "terminated";
    }
}
