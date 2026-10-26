using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Estuary.Models
{
    /// <summary>One text-only HTTP turn. Null optional fields use server defaults.</summary>
    [Serializable]
    public class CharacterTurnRequest
    {
        [JsonProperty("message")] public string Message;
        [JsonProperty("playerId")] public string PlayerId;
        [JsonProperty("timeoutSeconds")] public int? TimeoutSeconds;
        [JsonProperty("remember")] public bool? Remember;
        [JsonProperty("speakerName")] public string SpeakerName;
        [JsonProperty("callbackUrl")] public string CallbackUrl;
        [JsonProperty("callbackContext")] public JObject CallbackContext;
    }
}
