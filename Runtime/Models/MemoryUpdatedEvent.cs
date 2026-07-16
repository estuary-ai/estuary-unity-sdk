using System;
using UnityEngine;

namespace Estuary.Models
{
    /// <summary>
    /// Real-time push of newly extracted memories after a conversation ends
    /// (memory_updated event). Fired once background memory extraction completes.
    /// If the client is disconnected the push is dropped — poll the REST memory
    /// API on reconnect instead.
    ///
    /// The envelope uses snake_case keys, but the items in <see cref="NewMemories"/>
    /// use camelCase (see <see cref="MemoryData"/>).
    /// </summary>
    [Serializable]
    public class MemoryUpdatedEvent
    {
        [SerializeField] private string agent_id;
        [SerializeField] private string player_id;
        [SerializeField] private int memories_extracted;
        [SerializeField] private int facts_extracted;
        [SerializeField] private string conversation_id;
        [SerializeField] private MemoryData[] new_memories;
        [SerializeField] private string timestamp;

        public string AgentId => agent_id;
        public string PlayerId => player_id;

        /// <summary>Total number of memories extracted from the conversation.</summary>
        public int MemoriesExtracted => memories_extracted;

        /// <summary>Number of those that are facts.</summary>
        public int FactsExtracted => facts_extracted;

        public string ConversationId => conversation_id;

        /// <summary>The newly extracted memories (never null).</summary>
        public MemoryData[] NewMemories => new_memories ?? Array.Empty<MemoryData>();

        /// <summary>ISO 8601 timestamp of the extraction.</summary>
        public string Timestamp => timestamp;

        public MemoryUpdatedEvent() { }

        /// <summary>
        /// Create MemoryUpdatedEvent from a JSON string.
        /// </summary>
        public static MemoryUpdatedEvent FromJson(string json)
        {
            return JsonUtility.FromJson<MemoryUpdatedEvent>(json);
        }

        public override string ToString()
        {
            return $"MemoryUpdatedEvent(AgentId={AgentId}, MemoriesExtracted={MemoriesExtracted}, FactsExtracted={FactsExtracted})";
        }
    }
}
