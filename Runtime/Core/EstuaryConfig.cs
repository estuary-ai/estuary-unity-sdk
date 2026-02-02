using System;
using UnityEngine;

namespace Estuary
{
    /// <summary>
    /// Voice communication mode for the SDK.
    /// </summary>
    public enum VoiceMode
    {
        /// <summary>
        /// Legacy mode: Audio streamed over Socket.IO WebSocket as base64-encoded PCM.
        /// Higher latency but simpler setup.
        /// </summary>
        WebSocket,

        /// <summary>
        /// WebRTC mode: Audio streamed via LiveKit for low-latency real-time voice.
        /// Requires LiveKit server and Unity SDK package.
        /// </summary>
        LiveKit
    }

    /// <summary>
    /// ScriptableObject for storing global Estuary SDK configuration.
    /// Create via Assets > Create > Estuary > Config.
    /// 
    /// Note: Character-specific settings (characterId, playerId) should be set
    /// on the EstuaryCharacter component, not here.
    /// </summary>
    [CreateAssetMenu(fileName = "EstuaryConfig", menuName = "Estuary/Config", order = 1)]
    public class EstuaryConfig : ScriptableObject
    {
        #region Inspector Fields

        [Header("Server Settings")]
        [SerializeField]
        [Tooltip("Estuary server URL (e.g., https://api.estuary-ai.com)")]
        private string serverUrl = "https://api.estuary-ai.com";

        [SerializeField]
        [Tooltip("Your Estuary API key. Get one at app.estuary-ai.com")]
        private string apiKey = "";

        [Header("Voice Settings")]
        [SerializeField]
        [Tooltip("Voice communication mode. LiveKit provides lower latency WebRTC streaming.")]
        private VoiceMode voiceMode = VoiceMode.LiveKit;

        [Header("Audio Settings")]
        [SerializeField]
        [Tooltip("Sample rate for microphone recording (16000 for both WebSocket and LiveKit)")]
        private int recordingSampleRate = 16000;

        [SerializeField]
        [Tooltip("Expected sample rate for voice playback (48000 for Unity's native rate)")]
        private int playbackSampleRate = 48000;

        [SerializeField]
        [Tooltip("Duration of audio chunks to send (in milliseconds)")]
        private int audioChunkDurationMs = 100;

        [Header("Debug")]
        [SerializeField]
        [Tooltip("Enable debug logging")]
        private bool debugLogging = false;

        #endregion

        #region Properties

        /// <summary>
        /// Estuary server URL.
        /// </summary>
        public string ServerUrl
        {
            get => serverUrl;
            set => serverUrl = value;
        }

        /// <summary>
        /// Your Estuary API key.
        /// </summary>
        public string ApiKey
        {
            get => apiKey;
            set => apiKey = value;
        }

        /// <summary>
        /// Voice communication mode.
        /// </summary>
        public VoiceMode VoiceMode
        {
            get => voiceMode;
            set => voiceMode = value;
        }

        /// <summary>
        /// Whether LiveKit mode is enabled.
        /// </summary>
        public bool IsLiveKitEnabled => voiceMode == VoiceMode.LiveKit;

        /// <summary>
        /// Sample rate for microphone recording.
        /// Returns 16000 for both LiveKit and WebSocket modes (optimized for STT and latency).
        /// </summary>
        public int RecordingSampleRate => voiceMode == VoiceMode.LiveKit ? 16000 : recordingSampleRate;

        /// <summary>
        /// Sample rate for voice playback.
        /// </summary>
        public int PlaybackSampleRate => playbackSampleRate;

        /// <summary>
        /// Duration of audio chunks in milliseconds.
        /// </summary>
        public int AudioChunkDurationMs => audioChunkDurationMs;

        /// <summary>
        /// Enable debug logging.
        /// </summary>
        public bool DebugLogging
        {
            get => debugLogging;
            set => debugLogging = value;
        }

        #endregion

        #region Validation

        private void OnValidate()
        {
            // Validate API key
            if (!string.IsNullOrEmpty(apiKey) && !apiKey.StartsWith("est_"))
            {
                Debug.LogWarning("[EstuaryConfig] API key should start with 'est_'");
            }

            // Validate sample rates
            if (recordingSampleRate != 16000)
            {
                Debug.LogWarning("[EstuaryConfig] Recording sample rate should be 16000 Hz for best STT results");
            }

            // Validate chunk duration
            if (audioChunkDurationMs < 50)
            {
                Debug.LogWarning("[EstuaryConfig] Audio chunk duration too short, may cause high CPU usage");
            }
            else if (audioChunkDurationMs > 500)
            {
                Debug.LogWarning("[EstuaryConfig] Audio chunk duration too long, may cause latency");
            }
        }

        #endregion

        #region Runtime Settings

        /// <summary>
        /// Set the API key at runtime (for builds where you don't want to store the key in the asset).
        /// </summary>
        /// <param name="key">The API key to use</param>
        public void SetApiKeyRuntime(string key)
        {
            apiKey = key;

            if (debugLogging)
            {
                Debug.Log("[EstuaryConfig] API key set at runtime");
            }
        }

        /// <summary>
        /// Set the server URL at runtime.
        /// </summary>
        /// <param name="url">The server URL to use</param>
        public void SetServerUrlRuntime(string url)
        {
            serverUrl = url;

            if (debugLogging)
            {
                Debug.Log($"[EstuaryConfig] Server URL set to: {serverUrl}");
            }
        }

        #endregion

        #region Validation Methods

        /// <summary>
        /// Check if the configuration is valid for connecting.
        /// Note: Character-specific validation (characterId, playerId) should be done on EstuaryCharacter.
        /// </summary>
        /// <returns>True if global configuration is valid</returns>
        public bool IsValid()
        {
            return !string.IsNullOrEmpty(serverUrl) 
                && !string.IsNullOrEmpty(apiKey);
        }

        /// <summary>
        /// Get validation errors (if any).
        /// Note: Character-specific validation (characterId, playerId) should be done on EstuaryCharacter.
        /// </summary>
        /// <returns>Error message, or null if valid</returns>
        public string GetValidationError()
        {
            if (string.IsNullOrEmpty(serverUrl))
            {
                return "Server URL is not set";
            }

            if (string.IsNullOrEmpty(apiKey))
            {
                return "API key is not set";
            }

            return null;
        }

        #endregion

        #region Factory Methods

        /// <summary>
        /// Create a default configuration at runtime.
        /// </summary>
        /// <returns>New EstuaryConfig instance</returns>
        public static EstuaryConfig CreateDefault()
        {
            var config = CreateInstance<EstuaryConfig>();
            config.serverUrl = "https://api.estuary-ai.com";
            return config;
        }

        /// <summary>
        /// Create a configuration for development/testing.
        /// </summary>
        /// <param name="apiKey">API key to use</param>
        /// <param name="serverUrl">Server URL (default: localhost)</param>
        /// <returns>New EstuaryConfig instance</returns>
        public static EstuaryConfig CreateForDevelopment(string apiKey, string serverUrl = "http://localhost:4001")
        {
            var config = CreateInstance<EstuaryConfig>();
            config.serverUrl = serverUrl;
            config.apiKey = apiKey;
            config.debugLogging = true;
            return config;
        }

        #endregion
    }
}

