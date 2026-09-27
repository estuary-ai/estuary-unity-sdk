using System;
using Newtonsoft.Json;

namespace Estuary.Models
{
    /// <summary>An optional citation supplied by the customer proxy.</summary>
    [Serializable]
    public class ApiEndpointCitation
    {
        [JsonProperty("title")] public string Title;
        [JsonProperty("url")] public string Url;
    }
}
