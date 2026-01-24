using System;
using UnityEngine;

namespace Estuary.Models
{
    /// <summary>
    /// Interrupt signal data from the server.
    /// </summary>
    [Serializable]
    public class InterruptData
    {
        [SerializeField] private string messageId;
        [SerializeField] private float interruptedAt;

        /// <summary>
        /// The message ID that was interrupted.
        /// </summary>
        public string MessageId => messageId;

        /// <summary>
        /// Server timestamp when the interrupt occurred (in seconds since epoch).
        /// Used to filter out audio packets that were generated before the interrupt.
        /// </summary>
        public float InterruptedAt => interruptedAt;

        public InterruptData() { }

        public InterruptData(string messageId, float interruptedAt = 0f)
        {
            this.messageId = messageId;
            this.interruptedAt = interruptedAt;
        }

        /// <summary>
        /// Create InterruptData from JSON string.
        /// </summary>
        public static InterruptData FromJson(string json)
        {
            var data = JsonUtility.FromJson<InterruptDataJson>(json);
            return new InterruptData
            {
                messageId = data.message_id,
                interruptedAt = data.interrupted_at
            };
        }

        public override string ToString()
        {
            return $"InterruptData(MessageId={MessageId}, InterruptedAt={InterruptedAt})";
        }

        // Internal class for JSON deserialization with snake_case fields
        [Serializable]
        private class InterruptDataJson
        {
            public string message_id;
            public float interrupted_at;
        }
    }
}






