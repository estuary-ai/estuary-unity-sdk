using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Estuary
{
    /// <summary>
    /// Interface abstracting the LiveKitVoiceManager public API.
    /// Core components use this interface so they compile without the LiveKit SDK.
    /// The concrete implementation lives in the Estuary.LiveKit assembly and is
    /// registered at runtime via LiveKitBridge.
    /// </summary>
    public interface ILiveKitVoiceManager : IDisposable
    {
        #region Events

        /// <summary>
        /// Fired when successfully connected to a LiveKit room.
        /// </summary>
        event Action<string> OnConnected;

        /// <summary>
        /// Fired when disconnected from the LiveKit room.
        /// </summary>
        event Action<string> OnDisconnected;

        /// <summary>
        /// Fired when an error occurs.
        /// </summary>
        event Action<string> OnError;

        /// <summary>
        /// Fired when microphone mute state changes.
        /// </summary>
        event Action<bool> OnMuteStateChanged;

        /// <summary>
        /// Fired when the bot audio source is created at runtime.
        /// Useful for integrating lip sync or other audio-reactive features.
        /// </summary>
        event Action<AudioSource> OnBotAudioSourceCreated;

        #endregion

        #region Properties

        /// <summary>
        /// Whether currently connected to a LiveKit room.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Whether microphone is currently publishing (unmuted).
        /// </summary>
        bool IsPublishing { get; }

        /// <summary>
        /// Whether microphone is currently muted.
        /// </summary>
        bool IsMuted { get; }

        /// <summary>
        /// Enable debug logging.
        /// </summary>
        bool DebugLogging { get; set; }

        /// <summary>
        /// Current room name (null if not connected).
        /// </summary>
        string CurrentRoomName { get; }

        /// <summary>
        /// Check if bot audio is currently muted.
        /// </summary>
        bool IsBotAudioMuted { get; }

        /// <summary>
        /// Output volume for bot audio (0.0 to 1.0). Default 1.0.
        /// Applied to the bot AudioSource. When bot audio is muted (interrupt),
        /// volume is set to 0; when unmuted, this value is restored.
        /// </summary>
        float OutputVolume { get; set; }

        #endregion

        #region Methods

        /// <summary>
        /// Set the MonoBehaviour to use for running coroutines.
        /// </summary>
        void SetCoroutineRunner(MonoBehaviour runner);

        /// <summary>
        /// Connect to a LiveKit room using the provided token.
        /// </summary>
        Task<bool> ConnectAsync(string url, string token, string roomName);

        /// <summary>
        /// Disconnect from the current LiveKit room.
        /// </summary>
        Task DisconnectAsync();

        /// <summary>
        /// Start publishing audio using native WebRTC microphone capture.
        /// </summary>
        Task<bool> StartPublishingAsync();

        /// <summary>
        /// Stop publishing audio (disable microphone).
        /// </summary>
        Task StopPublishingAsync();

        /// <summary>
        /// Mute the microphone (stops sending audio).
        /// </summary>
        Task MuteAsync();

        /// <summary>
        /// Unmute the microphone (resumes sending audio).
        /// </summary>
        Task UnmuteAsync();

        /// <summary>
        /// Toggle mute state.
        /// </summary>
        Task ToggleMuteAsync();

        /// <summary>
        /// Signal an interrupt to stop receiving bot audio.
        /// </summary>
        Task SignalInterruptAsync(string messageId = null, float interruptedAt = 0f);

        /// <summary>
        /// Mute or unmute the bot's audio track.
        /// </summary>
        void MuteBotAudio(bool muted);

        /// <summary>
        /// Notify that a new audio chunk is starting for a specific message.
        /// </summary>
        bool NotifyAudioChunk(string messageId, float timestamp = 0f);

        /// <summary>
        /// Notify that a new outbound turn was dispatched (user final transcript, text,
        /// say_line, or camera image send), so a new bot response is expected. Arms the
        /// bounded auto-unmute safety net if the bot audio is interrupt-muted (or when
        /// the interrupt mute lands after this call). Interrupts with no following turn
        /// stay muted until the next response's metadata arrives.
        /// </summary>
        void NotifyResponseExpected();

        /// <summary>
        /// Safety-net delay (seconds) to auto-restore interrupt-muted bot audio, measured
        /// from the new turn's dispatch (NotifyResponseExpected), when the new response's
        /// metadata hasn't arrived yet — prevents the first chunk being clipped without
        /// timed-resuming the interrupted tail.
        /// </summary>
        float AutoUnmuteDelaySeconds { get; set; }

        /// <summary>
        /// Process queued events on the main thread. Call this from Update().
        /// </summary>
        void ProcessMainThreadQueue();

        /// <summary>
        /// Pre-create the LiveKit Room instance and attach event handlers.
        /// </summary>
        void PrewarmRoom();

        /// <summary>
        /// Get the underlying LiveKit room object for sharing with other managers (e.g., video).
        /// Returns null if not connected. The caller should treat this as an opaque object.
        /// </summary>
        object GetRoom();

        #endregion
    }
}
