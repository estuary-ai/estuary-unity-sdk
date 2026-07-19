using System;
using UnityEngine;

namespace Estuary.Models
{
    /// <summary>
    /// Real-time push when the extraction job evolves the character's private
    /// motive for this (character, player) relationship (motive_updated event,
    /// contract v1.7). Emitted immediately before the accompanying
    /// memory_updated. The motive is private to this relationship — it is only
    /// ever delivered to sessions of the same (character, player) pair.
    /// If the client is disconnected the push is dropped; the character OWNER
    /// can read it back via GET /api/v1/characters/{id}/motive?playerId=.
    /// </summary>
    [Serializable]
    public class MotiveUpdatedEvent
    {
        [SerializeField] private string agent_id;
        [SerializeField] private string player_id;
        [SerializeField] private string motive;
        [SerializeField] private string conversation_id;
        [SerializeField] private string timestamp;

        public string AgentId => agent_id;
        public string PlayerId => player_id;

        /// <summary>The evolved private motive text (≤500 chars).</summary>
        public string Motive => motive;

        public string ConversationId => conversation_id;

        /// <summary>ISO 8601 timestamp of the evolution.</summary>
        public string Timestamp => timestamp;

        public MotiveUpdatedEvent() { }

        /// <summary>
        /// Create MotiveUpdatedEvent from a JSON string.
        /// </summary>
        public static MotiveUpdatedEvent FromJson(string json)
        {
            return JsonUtility.FromJson<MotiveUpdatedEvent>(json);
        }

        public override string ToString()
        {
            return $"MotiveUpdatedEvent(AgentId={AgentId}, PlayerId={PlayerId})";
        }
    }
}
