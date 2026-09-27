using System;
using Newtonsoft.Json;

namespace Estuary.Models
{
    /// <summary>Share settings. Temporary TTL values: 24h, 7d, 30d or 90d.</summary>
    [Serializable]
    public class ShareCreateRequest
    {
        [JsonProperty("characterId")] public string CharacterId;
        [JsonProperty("permanent")] public bool? Permanent;
        [JsonProperty("ttl")] public string Ttl;
        [JsonProperty("memorySharing")] public string MemorySharing;
        [JsonProperty("maxExchanges")] public int? MaxExchanges;
        [JsonProperty("maxInteractions")] public int? MaxInteractions;
    }
}
