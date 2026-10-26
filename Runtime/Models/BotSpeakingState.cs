using System;
using System.Collections.Generic;

namespace Estuary.Models
{
    /// <summary>LiveKit bot attributes. MessageId may be empty on older servers. This is not a local playback clock.</summary>
    [Serializable]
    public class BotSpeakingState
    {
        public string ParticipantIdentity;
        public string State;
        public string MessageId;
        public bool IsSpeaking => State == "speaking";

        /// <summary>Read a complete attribute snapshot, tolerating absent/empty utterance IDs on older servers.</summary>
        public static BotSpeakingState FromAttributes(string identity, IReadOnlyDictionary<string, string> attributes)
        {
            if (attributes == null || !attributes.TryGetValue("estuary.state", out var state)) return null;
            attributes.TryGetValue("estuary.message_id", out var messageId);
            return new BotSpeakingState { ParticipantIdentity = identity, State = state, MessageId = messageId ?? "" };
        }
    }
}
