using System;
using Newtonsoft.Json;

namespace Estuary.Models
{
    /// <summary>Stable facts remembered about a player.</summary>
    [Serializable]
    public class CoreFactsResponse
    {
        [JsonProperty("coreFacts")] public CoreFact[] CoreFacts;
    }
}
