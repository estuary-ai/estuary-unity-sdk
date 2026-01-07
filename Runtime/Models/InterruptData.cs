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

        /// <summary>
        /// The message ID that was interrupted.
        /// </summary>
        public string MessageId => messageId;

        public InterruptData() { }

        public InterruptData(string messageId)
        {
            this.messageId = messageId;
        }

        /// <summary>
        /// Create InterruptData from JSON string.
        /// </summary>
        public static InterruptData FromJson(string json)
        {
            var data = JsonUtility.FromJson<InterruptDataJson>(json);
            return new InterruptData
            {
                messageId = data.message_id
            };
        }

        public override string ToString()
        {
            return $"InterruptData(MessageId={MessageId})";
        }

        // Internal class for JSON deserialization with snake_case fields
        [Serializable]
        private class InterruptDataJson
        {
            public string message_id;
        }
    }
}






