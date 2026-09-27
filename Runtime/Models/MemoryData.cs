using System;
using UnityEngine;
using Newtonsoft.Json;

namespace Estuary.Models
{
    /// <summary>
    /// A single extracted memory, delivered inside a memory_updated push
    /// (see <see cref="MemoryUpdatedEvent"/>).
    ///
    /// NOTE: unlike most wire events (which are snake_case), MemoryData uses
    /// camelCase keys, matching the server's Memory.to_dict().
    /// </summary>
    [Serializable, JsonObject(MemberSerialization.OptIn)]
    public class MemoryData
    {
        [SerializeField, JsonProperty("id")] private string id;
        [SerializeField, JsonProperty("userId")] private string userId;
        [SerializeField, JsonProperty("agentId")] private string agentId;
        [SerializeField, JsonProperty("playerId")] private string playerId;
        [SerializeField, JsonProperty("content")] private string content;
        [SerializeField, JsonProperty("memoryType")] private string memoryType;
        [SerializeField, JsonProperty("confidence")] private float confidence;
        [SerializeField, JsonProperty("status")] private string status;
        [SerializeField, JsonProperty("sourceConversationId")] private string sourceConversationId;
        [SerializeField, JsonProperty("sourceQuote")] private string sourceQuote;
        [SerializeField, JsonProperty("source")] private string source;
        [SerializeField, JsonProperty("lastAccessedAt")] private string lastAccessedAt;
        [SerializeField, JsonProperty("accessCount")] private int accessCount;
        [SerializeField, JsonProperty("extractedAt")] private string extractedAt;
        [SerializeField, JsonProperty("createdAt")] private string createdAt;
        [SerializeField, JsonProperty("updatedAt")] private string updatedAt;

        [SerializeField, JsonProperty("importance")] private float importance;
        [SerializeField, JsonProperty("memoryLayer")] private string memoryLayer;
        [SerializeField, JsonProperty("sourceMessageTimestamp")] private string sourceMessageTimestamp;
        public float Importance => importance;
        public string MemoryLayer => memoryLayer;
        public string SourceMessageTimestamp => sourceMessageTimestamp;
        public string Id => id;
        public string UserId => userId;
        public string AgentId => agentId;
        public string PlayerId => playerId;
        public string Content => content;

        /// <summary>
        /// "fact" | "preference" | "relationship" | "event" | "emotional_state"
        /// | "correction" | "character_self" | "spatial_change".
        /// </summary>
        public string MemoryType => memoryType;

        public float Confidence => confidence;

        /// <summary>"active" | "superseded" | "decayed" | "deleted".</summary>
        public string Status => status;

        public string SourceConversationId => sourceConversationId;
        public string SourceQuote => sourceQuote;

        /// <summary>"text_chat" | "simulation".</summary>
        public string Source => source;

        /// <summary>ISO 8601 timestamp, or null.</summary>
        public string LastAccessedAt => lastAccessedAt;

        public int AccessCount => accessCount;

        /// <summary>ISO 8601 timestamp, or null.</summary>
        public string ExtractedAt => extractedAt;

        /// <summary>ISO 8601 timestamp, or null.</summary>
        public string CreatedAt => createdAt;

        /// <summary>ISO 8601 timestamp, or null.</summary>
        public string UpdatedAt => updatedAt;

        public MemoryData() { }

        public override string ToString()
        {
            return $"MemoryData(Id={Id}, Type={MemoryType}, Content={Content})";
        }
    }
}
