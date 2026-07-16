using System;
using UnityEngine;

namespace Estuary.Models
{
    /// <summary>
    /// A single extracted memory, delivered inside a memory_updated push
    /// (see <see cref="MemoryUpdatedEvent"/>).
    ///
    /// NOTE: unlike most wire events (which are snake_case), MemoryData uses
    /// camelCase keys, matching the server's Memory.to_dict().
    /// </summary>
    [Serializable]
    public class MemoryData
    {
        [SerializeField] private string id;
        [SerializeField] private string userId;
        [SerializeField] private string agentId;
        [SerializeField] private string playerId;
        [SerializeField] private string content;
        [SerializeField] private string memoryType;
        [SerializeField] private float confidence;
        [SerializeField] private string status;
        [SerializeField] private string sourceConversationId;
        [SerializeField] private string sourceQuote;
        [SerializeField] private string source;
        [SerializeField] private string lastAccessedAt;
        [SerializeField] private int accessCount;
        [SerializeField] private string extractedAt;
        [SerializeField] private string createdAt;
        [SerializeField] private string updatedAt;

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
