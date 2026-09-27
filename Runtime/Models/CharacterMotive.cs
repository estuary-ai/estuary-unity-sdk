using System;
using Newtonsoft.Json;

namespace Estuary.Models
{
    /// <summary>Private seed motive and optional per-player override.</summary>
    [Serializable]
    public class CharacterMotive
    {
        [JsonProperty("seedMotive")] public string SeedMotive;
        [JsonProperty("liveMotive")] public string LiveMotive;
        [JsonProperty("updatedAt")] public string UpdatedAt;
    }
}
