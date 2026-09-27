using System;
using Newtonsoft.Json;

namespace Estuary.Models
{
    /// <summary>Completed HTTP turn with conversation correlation IDs and typed actions.</summary>
    [Serializable]
    public class CharacterTurnResponse
    {
        [JsonProperty("text")] public string Text;
        [JsonProperty("messageId")] public string MessageId;
        [JsonProperty("sessionId")] public string SessionId;
        [JsonProperty("conversationId")] public string ConversationId;
        [JsonProperty("characterId")] public string CharacterId;
        [JsonProperty("playerId")] public string PlayerId;
        [JsonProperty("actions")] public CharacterTurnAction[] Actions;
    }
}
