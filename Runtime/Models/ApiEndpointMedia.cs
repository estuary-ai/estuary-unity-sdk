using System;
using Newtonsoft.Json;

namespace Estuary.Models
{
    /// <summary>Untrusted customer-proxy media. Only load validated HTTPS URLs; never attach Estuary credentials.</summary>
    [Serializable]
    public class ApiEndpointMedia
    {
        [JsonProperty("type")] public string Type;
        [JsonProperty("url")] public string Url;
        [JsonProperty("mimeType")] public string MimeType;
        [JsonProperty("title")] public string Title;
        [JsonProperty("alt")] public string Alt;
        [JsonProperty("sourceUrl")] public string SourceUrl;
    }
}
