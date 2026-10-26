using System;
using Newtonsoft.Json;

namespace Estuary.Models
{
    /// <summary>Count returned after deleting all memories for a pair.</summary>
    [Serializable]
    public class DeletedMemories
    {
        [JsonProperty("deletedCount")] public int DeletedCount;
    }
}
