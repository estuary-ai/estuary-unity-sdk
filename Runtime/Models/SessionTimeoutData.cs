using System;
using UnityEngine;

namespace Estuary.Models
{
    /// <summary>
    /// Data received when the server ends the session due to inactivity
    /// (no conversation activity for the server's idle timeout).
    /// The server disconnects the socket immediately after sending this.
    /// </summary>
    [Serializable]
    public class SessionTimeoutData
    {
        [SerializeField] private string reason;
        [SerializeField] private int idle_seconds;
        [SerializeField] private int timeout_seconds;

        /// <summary>
        /// Timeout reason (always "inactivity").
        /// </summary>
        public string Reason => reason;

        /// <summary>
        /// How long the session was idle before the server ended it, in seconds.
        /// </summary>
        public int IdleSeconds => idle_seconds;

        /// <summary>
        /// The server's configured idle timeout, in seconds.
        /// </summary>
        public int TimeoutSeconds => timeout_seconds;

        public SessionTimeoutData() { }

        /// <summary>
        /// Create SessionTimeoutData from JSON string.
        /// </summary>
        public static SessionTimeoutData FromJson(string json)
        {
            return JsonUtility.FromJson<SessionTimeoutData>(json);
        }

        public override string ToString()
        {
            return $"SessionTimeoutData(Reason={Reason}, IdleSeconds={IdleSeconds}, TimeoutSeconds={TimeoutSeconds})";
        }
    }
}
