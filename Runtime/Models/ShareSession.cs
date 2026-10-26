using System;
using Newtonsoft.Json;

namespace Estuary.Models
{
    /// <summary>Credentials and character metadata returned by anonymous share redemption.</summary>
    [Serializable]
    public class ShareSession
    {
        [JsonProperty("sessionToken")] public string SessionToken;
        [JsonProperty("characterId")] public string CharacterId;
        [JsonProperty("playerId")] public string PlayerId;
        [JsonProperty("serverUrl")] public string ServerUrl;
        [JsonProperty("character")] public AgentResponse Character;
    }
}
