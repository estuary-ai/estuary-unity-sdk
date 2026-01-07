using System;
using UnityEngine;

namespace Estuary.Models
{
    /// <summary>
    /// Session information received when connecting to an Estuary character.
    /// </summary>
    [Serializable]
    public class SessionInfo
    {
        [SerializeField] private string sessionId;
        [SerializeField] private string conversationId;
        [SerializeField] private string characterId;
        [SerializeField] private string playerId;

        /// <summary>
        /// Unique identifier for this session.
        /// </summary>
        public string SessionId => sessionId;

        /// <summary>
        /// Unique identifier for the conversation (persists across sessions).
        /// </summary>
        public string ConversationId => conversationId;

        /// <summary>
        /// The character ID this session is connected to.
        /// </summary>
        public string CharacterId => characterId;

        /// <summary>
        /// The player ID associated with this session.
        /// </summary>
        public string PlayerId => playerId;

        public SessionInfo() { }

        public SessionInfo(string sessionId, string conversationId, string characterId, string playerId)
        {
            this.sessionId = sessionId;
            this.conversationId = conversationId;
            this.characterId = characterId;
            this.playerId = playerId;
        }

        /// <summary>
        /// Create SessionInfo from JSON dictionary.
        /// </summary>
        public static SessionInfo FromJson(string json)
        {
            return JsonUtility.FromJson<SessionInfo>(json);
        }

        public override string ToString()
        {
            return $"SessionInfo(SessionId={SessionId}, ConversationId={ConversationId}, CharacterId={CharacterId}, PlayerId={PlayerId})";
        }
    }
}






