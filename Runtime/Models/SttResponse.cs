using System;
using UnityEngine;

namespace Estuary.Models
{
    /// <summary>
    /// Speech-to-text response from the server.
    /// </summary>
    [Serializable]
    public class SttResponse
    {
        [SerializeField] private string text;
        [SerializeField] private bool isFinal;

        /// <summary>
        /// The transcribed text.
        /// </summary>
        public string Text => text;

        /// <summary>
        /// Whether this is the final transcription (speech ended) or an interim result.
        /// </summary>
        public bool IsFinal => isFinal;

        public SttResponse() { }

        public SttResponse(string text, bool isFinal)
        {
            this.text = text;
            this.isFinal = isFinal;
        }

        /// <summary>
        /// Create SttResponse from JSON string.
        /// </summary>
        public static SttResponse FromJson(string json)
        {
            // Handle snake_case from backend
            var response = JsonUtility.FromJson<SttResponseJson>(json);
            return new SttResponse
            {
                text = response.text ?? "",
                isFinal = response.is_final
            };
        }

        public override string ToString()
        {
            return $"SttResponse(Text=\"{Text}\", IsFinal={IsFinal})";
        }

        // Internal class for JSON deserialization with snake_case fields
        [Serializable]
        private class SttResponseJson
        {
            public string text;
            public bool is_final;
        }
    }
}






