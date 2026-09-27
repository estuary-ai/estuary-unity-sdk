using System;
using Newtonsoft.Json;

namespace Estuary.Models
{
    /// <summary>Message-page position, total count and continuation flag.</summary>
    [Serializable]
    public class MessagePagination
    {
        [JsonProperty("page")] public int Page;
        [JsonProperty("limit")] public int Limit;
        [JsonProperty("total")] public int Total;
        [JsonProperty("hasMore")] public bool HasMore;
    }
}
