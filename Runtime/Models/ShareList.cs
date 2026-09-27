using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Estuary.Models
{
    /// <summary>Owned shares with their full JSON metadata.</summary>
    [Serializable]
    public class ShareList
    {
        [JsonProperty("shares")] public JObject[] Shares;
    }
}
