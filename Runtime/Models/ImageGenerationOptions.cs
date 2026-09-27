using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Estuary.Models
{
    /// <summary>ImageGenerationOptions: canonical REST wire model. Unknown server fields are tolerated.</summary>
    [Serializable]
    public class ImageGenerationOptions
    {
        [JsonProperty("ttsProvider")] public string TtsProvider;
        [JsonProperty("appearancePrompt")] public string AppearancePrompt;
        [JsonProperty("voicePrompt")] public string VoicePrompt;
        [JsonProperty("personaPrompt")] public string PersonaPrompt;
        [JsonProperty("useDefaultVoice")] public bool? UseDefaultVoice;
    }
}
