using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Estuary.Models
{
    /// <summary>CharacterTurnEvent: canonical REST wire model. Unknown server fields are tolerated.</summary>
    [Serializable]
    public class CharacterTurnEvent
    {
        [JsonProperty("type")] public string Type;
        [JsonProperty("data")] public JObject Data;
    }
}
