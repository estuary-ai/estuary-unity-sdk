using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace Estuary.Models
{
    /// <summary>An action returned by the HTTP turn endpoint, retaining JSON argument types.</summary>
    public class CharacterTurnAction
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("arguments")] public Dictionary<string, JToken> Arguments;
    }
}
