using System;
using UnityEngine;

namespace Estuary.Models
{
    /// <summary>
    /// Data received when the server releases the session's voice resources
    /// after no user speech for its voice-idle timeout (voice_timeout event).
    /// The server closes STT and deletes the LiveKit room, but the Socket.IO
    /// connection STAYS OPEN and text chat continues — unlike session_timeout,
    /// no disconnect follows this event.
    /// </summary>
    [Serializable]
    public class VoiceTimeoutData
    {
        [SerializeField] private string reason;
        [SerializeField] private int idle_seconds;
        [SerializeField] private int timeout_seconds;

        /// <summary>
        /// Timeout reason (always "voice_inactivity").
        /// </summary>
        public string Reason => reason;

        /// <summary>
        /// How long the voice lane was idle (no user speech) before the
        /// server released it, in seconds.
        /// </summary>
        public int IdleSeconds => idle_seconds;

        /// <summary>
        /// The server's configured voice-idle timeout, in seconds.
        /// </summary>
        public int TimeoutSeconds => timeout_seconds;

        public VoiceTimeoutData() { }

        /// <summary>
        /// Create VoiceTimeoutData from JSON string.
        /// </summary>
        public static VoiceTimeoutData FromJson(string json)
        {
            return JsonUtility.FromJson<VoiceTimeoutData>(json);
        }

        public override string ToString()
        {
            return $"VoiceTimeoutData(Reason={Reason}, IdleSeconds={IdleSeconds}, TimeoutSeconds={TimeoutSeconds})";
        }
    }
}
