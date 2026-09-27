using System;
using Estuary.Models;

namespace Estuary
{
    /// <summary>Optional extension implemented by the bundled LiveKit manager; preserves custom voice-manager compatibility.</summary>
    public interface ILiveKitBotStateSource
    {
        event Action<BotSpeakingState> OnBotSpeakingStateChanged;
        BotSpeakingState CurrentBotSpeakingState { get; }
    }
}
