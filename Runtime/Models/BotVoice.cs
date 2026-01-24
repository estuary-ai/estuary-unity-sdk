using System;
using UnityEngine;

namespace Estuary.Models
{
    /// <summary>
    /// Bot voice audio response from the AI character.
    /// </summary>
    [Serializable]
    public class BotVoice
    {
        [SerializeField] private string audio;
        [SerializeField] private int sampleRate;
        [SerializeField] private int chunkIndex;
        [SerializeField] private string messageId;
        [SerializeField] private bool isInterjection;
        [SerializeField] private float timestamp;

        /// <summary>
        /// Base64-encoded audio data.
        /// </summary>
        public string Audio => audio;

        /// <summary>
        /// Sample rate of the audio (default: 24000 for ElevenLabs).
        /// </summary>
        public int SampleRate => sampleRate > 0 ? sampleRate : 24000;

        /// <summary>
        /// Index of this chunk in a streaming response.
        /// </summary>
        public int ChunkIndex => chunkIndex;

        /// <summary>
        /// Unique identifier for this message (for tracking interrupts).
        /// </summary>
        public string MessageId => messageId;

        /// <summary>
        /// Whether this audio is from an interjection.
        /// </summary>
        public bool IsInterjection => isInterjection;

        /// <summary>
        /// Server timestamp when this audio chunk was generated (in seconds since epoch).
        /// Used to filter out audio chunks that were generated before an interrupt.
        /// </summary>
        public float Timestamp => timestamp;

        /// <summary>
        /// Decoded audio bytes (cached after first access).
        /// </summary>
        private byte[] _decodedAudio;
        public byte[] DecodedAudio
        {
            get
            {
                if (_decodedAudio == null && !string.IsNullOrEmpty(audio))
                {
                    try
                    {
                        _decodedAudio = Convert.FromBase64String(audio);
                    }
                    catch (FormatException e)
                    {
                        Debug.LogError($"Failed to decode audio: {e.Message}");
                        _decodedAudio = Array.Empty<byte>();
                    }
                }
                return _decodedAudio ?? Array.Empty<byte>();
            }
        }

        public BotVoice() { }

        public BotVoice(string audio, int sampleRate = 24000, string messageId = null, int chunkIndex = 0, float timestamp = 0f)
        {
            this.audio = audio;
            this.sampleRate = sampleRate;
            this.messageId = messageId;
            this.chunkIndex = chunkIndex;
            this.timestamp = timestamp;
        }

        /// <summary>
        /// Create BotVoice from JSON string.
        /// </summary>
        public static BotVoice FromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                throw new ArgumentException("Cannot parse BotVoice from null or empty JSON");
            }
            
            // Handle snake_case from backend
            var response = JsonUtility.FromJson<BotVoiceJson>(json);
            if (response == null)
            {
                throw new ArgumentException($"Failed to deserialize BotVoice JSON: {json}");
            }
            
            return new BotVoice
            {
                audio = response.audio,
                sampleRate = response.sample_rate > 0 ? response.sample_rate : 24000,
                chunkIndex = response.chunk_index,
                messageId = response.message_id,
                isInterjection = response.is_interjection,
                timestamp = response.timestamp
            };
        }

        public override string ToString()
        {
            return $"BotVoice(AudioLength={DecodedAudio.Length} bytes, SampleRate={SampleRate}, ChunkIndex={ChunkIndex}, MessageId={MessageId})";
        }

        // Internal class for JSON deserialization with snake_case fields
        [Serializable]
        private class BotVoiceJson
        {
            public string audio;
            public int sample_rate;
            public int chunk_index;
            public string message_id;
            public bool is_interjection;
            public float timestamp;
        }
    }
}






