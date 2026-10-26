using System;
using Newtonsoft.Json;

namespace Estuary.Models
{
    /// <summary>Result of revoking one share and its active sessions.</summary>
    [Serializable]
    public class ShareRevoked
    {
        [JsonProperty("revoked")] public bool Revoked;
        [JsonProperty("killedSessions")] public int KilledSessions;
        [JsonProperty("removedSessionBlobs")] public int RemovedSessionBlobs;
    }
}
