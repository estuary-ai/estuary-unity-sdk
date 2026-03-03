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
        [SerializeField] private string livekitToken;
        [SerializeField] private string livekitUrl;
        [SerializeField] private string livekitRoom;

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

        /// <summary>
        /// LiveKit token (if embedded in session_info for latency optimization).
        /// Null if LiveKit is not enabled or token was not included.
        /// </summary>
        public string LiveKitToken => livekitToken;

        /// <summary>
        /// LiveKit server URL (if embedded in session_info).
        /// </summary>
        public string LiveKitUrl => livekitUrl;

        /// <summary>
        /// LiveKit room name (if embedded in session_info).
        /// </summary>
        public string LiveKitRoom => livekitRoom;

        /// <summary>
        /// Whether this session_info includes an embedded LiveKit token.
        /// </summary>
        public bool HasLiveKitToken => !string.IsNullOrEmpty(livekitToken);

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






