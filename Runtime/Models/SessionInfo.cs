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
        // Field names MUST match the wire keys exactly — Unity's JsonUtility binds
        // JSON by field name. The server emits the four core fields as snake_case
        // (session_id / conversation_id / character_id / player_id); the embedded
        // LiveKit fields below are camelCase on the wire (livekitToken/Url/Room).
        [SerializeField] private string session_id;
        [SerializeField] private string conversation_id;
        [SerializeField] private string character_id;
        [SerializeField] private string player_id;
        [SerializeField] private string livekitToken;
        [SerializeField] private string livekitUrl;
        [SerializeField] private string livekitRoom;

        /// <summary>
        /// Unique identifier for this session.
        /// </summary>
        public string SessionId => session_id;

        /// <summary>
        /// Unique identifier for the conversation (persists across sessions).
        /// </summary>
        public string ConversationId => conversation_id;

        /// <summary>
        /// The character ID this session is connected to.
        /// </summary>
        public string CharacterId => character_id;

        /// <summary>
        /// The player ID associated with this session.
        /// </summary>
        public string PlayerId => player_id;

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
            this.session_id = sessionId;
            this.conversation_id = conversationId;
            this.character_id = characterId;
            this.player_id = playerId;
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
