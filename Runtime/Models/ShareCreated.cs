using System;
using Newtonsoft.Json;

namespace Estuary.Models
{
    /// <summary>Created share token, URLs and optional expiration.</summary>
    [Serializable]
    public class ShareCreated
    {
        [JsonProperty("token")] public string Token;
        [JsonProperty("shareUrl")] public string ShareUrl;
        [JsonProperty("anchorUrl")] public string AnchorUrl;
        [JsonProperty("expiresAt")] public string ExpiresAt;
    }
}
