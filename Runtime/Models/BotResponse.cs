using System;
using UnityEngine;

namespace Estuary.Models
{
    /// <summary>
    /// Bot text response from the AI character.
    /// </summary>
    [Serializable]
    public class BotResponse
    {
        [SerializeField] private string text;
        [SerializeField] private bool isFinal;
        [SerializeField] private bool partial;
        [SerializeField] private string messageId;
        [SerializeField] private int chunkIndex;
        [SerializeField] private bool isInterjection;

        /// <summary>
        /// The text content of the response.
        /// </summary>
        public string Text => text;

        /// <summary>
        /// Whether this is the final/complete response.
        /// </summary>
        public bool IsFinal => isFinal;

        /// <summary>
        /// Whether this is a partial (streaming) response.
        /// </summary>
        public bool IsPartial => partial;

        /// <summary>
        /// Unique identifier for this message (for tracking interrupts).
        /// </summary>
        public string MessageId => messageId;

        /// <summary>
        /// Index of this chunk in a streaming response.
        /// </summary>
        public int ChunkIndex => chunkIndex;

        /// <summary>
        /// Whether this response is an interjection (proactive message during silence).
        /// </summary>
        public bool IsInterjection => isInterjection;

        public BotResponse() { }

        public BotResponse(string text, bool isFinal = true, string messageId = null, int chunkIndex = 0, bool isInterjection = false)
        {
            this.text = text;
            this.isFinal = isFinal;
            this.partial = !isFinal;
            this.messageId = messageId;
            this.chunkIndex = chunkIndex;
            this.isInterjection = isInterjection;
        }

        /// <summary>
        /// Create BotResponse from JSON string.
        /// </summary>
        public static BotResponse FromJson(string json)
        {
            // Handle snake_case from backend
            var response = JsonUtility.FromJson<BotResponseJson>(json);
            return new BotResponse
            {
                text = response.text ?? "",
                isFinal = response.is_final,
                partial = response.partial,
                messageId = response.message_id,
                chunkIndex = response.chunk_index,
                isInterjection = response.is_interjection
            };
        }

        public override string ToString()
        {
            return $"BotResponse(Text=\"{(Text?.Length > 50 ? Text.Substring(0, 50) + "..." : Text)}\", IsFinal={IsFinal}, MessageId={MessageId})";
        }

        // Internal class for JSON deserialization with snake_case fields
        [Serializable]
        private class BotResponseJson
        {
            public string text;
            public bool is_final;
            public bool partial;
            public string message_id;
            public int chunk_index;
            public bool is_interjection;
        }
    }
}






