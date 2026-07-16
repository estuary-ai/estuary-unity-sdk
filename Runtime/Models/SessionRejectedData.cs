using System;
using UnityEngine;

namespace Estuary.Models
{
    /// <summary>
    /// Data received when the server rejects the connection because a policy cap
    /// was hit (session_rejected event) — currently the per-share-token
    /// concurrent-session cap. The server disconnects immediately after emitting
    /// this, so the SDK suppresses auto-reconnect (mirroring session_timeout):
    /// blindly reconnecting would just hit the same cap again in a loop.
    /// </summary>
    [Serializable]
    public class SessionRejectedData
    {
        [SerializeField] private string reason;
        [SerializeField] private int cap;
        [SerializeField] private string share_token_id;

        /// <summary>
        /// Rejection reason code. "concurrent_limit" = too many active Socket.IO
        /// sessions are already open for this share token; wait for another
        /// device to disconnect, or retry later.
        /// </summary>
        public string Reason => reason;

        /// <summary>The current cap (informational).</summary>
        public int Cap => cap;

        /// <summary>The share token whose cap was hit.</summary>
        public string ShareTokenId => share_token_id;

        public SessionRejectedData() { }

        /// <summary>
        /// Create SessionRejectedData from a JSON string.
        /// </summary>
        public static SessionRejectedData FromJson(string json)
        {
            return JsonUtility.FromJson<SessionRejectedData>(json);
        }

        public override string ToString()
        {
            return $"SessionRejectedData(Reason={Reason}, Cap={Cap}, ShareTokenId={ShareTokenId})";
        }
    }
}
