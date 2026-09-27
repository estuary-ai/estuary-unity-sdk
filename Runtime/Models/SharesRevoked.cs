using System;
using Newtonsoft.Json;

namespace Estuary.Models
{
    /// <summary>Counts returned by revoking shares created with one API key.</summary>
    [Serializable]
    public class SharesRevoked
    {
        [JsonProperty("revoked")] public int Revoked;
        [JsonProperty("killedSessions")] public int KilledSessions;
        [JsonProperty("removedSessionBlobs")] public int RemovedSessionBlobs;
    }
}
