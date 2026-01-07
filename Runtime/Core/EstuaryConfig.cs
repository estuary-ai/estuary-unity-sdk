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
    /// ScriptableObject for storing Estuary SDK configuration.
    /// Create via Assets > Create > Estuary > Config.
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

        [Header("Character Settings")]
        [SerializeField]
        [Tooltip("The UUID of the character to connect to (get this from your Estuary dashboard)")]
        private string characterId = "";

        [SerializeField]
        [Tooltip("Unique identifier for the player (used for conversation persistence)")]
        private string playerId = "";

        [Header("Voice Settings")]
        [SerializeField]
        [Tooltip("Voice communication mode. LiveKit provides lower latency WebRTC streaming.")]
        private VoiceMode voiceMode = VoiceMode.LiveKit;

        [SerializeField]
        [Tooltip("Automatically connect to LiveKit when session is established (LiveKit mode only)")]
        private bool autoConnectLiveKit = true;

        [Header("Audio Settings")]
        [SerializeField]
        [Tooltip("Sample rate for microphone recording (16000 for WebSocket STT, 48000 for LiveKit)")]
        private int recordingSampleRate = 16000;

        [SerializeField]
        [Tooltip("Expected sample rate for voice playback (ElevenLabs: 24000)")]
        private int playbackSampleRate = 24000;

        [SerializeField]
        [Tooltip("Duration of audio chunks to send (in milliseconds)")]
        private int audioChunkDurationMs = 100;

        [Header("Connection Settings")]
        [SerializeField]
        [Tooltip("Automatically reconnect if connection is lost")]
        private bool autoReconnect = true;

        [SerializeField]
        [Tooltip("Maximum number of reconnection attempts")]
        private int maxReconnectAttempts = 5;

        [SerializeField]
        [Tooltip("Delay between reconnection attempts (in milliseconds)")]
        private int reconnectDelayMs = 2000;

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
        /// The character UUID to connect to.
        /// </summary>
        public string CharacterId
        {
            get => characterId;
            set => characterId = value;
        }

        /// <summary>
        /// Unique player identifier for conversation persistence.
        /// </summary>
        public string PlayerId
        {
            get => playerId;
            set => playerId = value;
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
        /// Whether to automatically connect to LiveKit when session is established.
        /// </summary>
        public bool AutoConnectLiveKit
        {
            get => autoConnectLiveKit;
            set => autoConnectLiveKit = value;
        }

        /// <summary>
        /// Whether LiveKit mode is enabled.
        /// </summary>
        public bool IsLiveKitEnabled => voiceMode == VoiceMode.LiveKit;

        /// <summary>
        /// Sample rate for microphone recording.
        /// Returns 48000 for LiveKit mode (WebRTC standard), 16000 for WebSocket mode.
        /// </summary>
        public int RecordingSampleRate => voiceMode == VoiceMode.LiveKit ? 48000 : recordingSampleRate;

        /// <summary>
        /// Sample rate for voice playback.
        /// </summary>
        public int PlaybackSampleRate => playbackSampleRate;

        /// <summary>
        /// Duration of audio chunks in milliseconds.
        /// </summary>
        public int AudioChunkDurationMs => audioChunkDurationMs;

        /// <summary>
        /// Whether to automatically reconnect.
        /// </summary>
        public bool AutoReconnect => autoReconnect;

        /// <summary>
        /// Maximum reconnection attempts.
        /// </summary>
        public int MaxReconnectAttempts => maxReconnectAttempts;

        /// <summary>
        /// Delay between reconnection attempts.
        /// </summary>
        public int ReconnectDelayMs => reconnectDelayMs;

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
            // Validate server URL
            if (!string.IsNullOrEmpty(serverUrl))
            {
                if (!serverUrl.StartsWith("http://") && !serverUrl.StartsWith("https://"))
                {
                    Debug.LogWarning("[EstuaryConfig] Server URL should start with http:// or https://");
                }

                // Remove trailing slash
                serverUrl = serverUrl.TrimEnd('/');
            }

            // Validate API key
            if (!string.IsNullOrEmpty(apiKey) && !apiKey.StartsWith("est_"))
            {
                Debug.LogWarning("[EstuaryConfig] API key should start with 'est_'");
            }

            // Validate character ID (should be a UUID)
            if (!string.IsNullOrEmpty(characterId) && characterId.Length != 36)
            {
                Debug.LogWarning("[EstuaryConfig] Character ID should be a UUID (36 characters)");
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

        #region Runtime API Key Setting

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
            serverUrl = url?.TrimEnd('/') ?? "";

            if (debugLogging)
            {
                Debug.Log($"[EstuaryConfig] Server URL set to: {serverUrl}");
            }
        }

        #endregion

        #region Validation Methods

        /// <summary>
        /// Check if the configuration is valid for connecting.
        /// </summary>
        /// <returns>True if configuration is valid</returns>
        public bool IsValid()
        {
            return !string.IsNullOrEmpty(serverUrl) 
                && !string.IsNullOrEmpty(apiKey)
                && !string.IsNullOrEmpty(characterId)
                && !string.IsNullOrEmpty(playerId);
        }

        /// <summary>
        /// Get validation errors (if any).
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

            if (string.IsNullOrEmpty(characterId))
            {
                return "Character ID is not set";
            }

            if (string.IsNullOrEmpty(playerId))
            {
                return "Player ID is not set";
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

