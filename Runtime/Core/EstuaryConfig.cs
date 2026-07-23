using System;
using System.Threading.Tasks;
using UnityEngine;
using Estuary.Models;

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

        [SerializeField]
        [Tooltip("LiveKit only: when the bot audio is interrupt-muted and a NEW turn is dispatched (final transcript / text / camera image send), auto-restore the audio this many milliseconds after that dispatch if the new response's metadata hasn't arrived yet — prevents the first chunk being clipped. An interrupt with no following turn stays muted (the interrupted tail never resumes on a timer). Too high re-introduces first-chunk clipping.")]
        private float botAudioAutoUnmuteMs = 250f;

        [Header("Session Capabilities")]
        [SerializeField]
        [Tooltip("Declare that this device has a usable camera. When off, camera/vision tools are hidden from the character for the session.")]
        private bool deviceHasCamera = true;

        [SerializeField]
        [Tooltip("Declare that this device has a usable microphone. When off, mic-dependent tools are hidden.")]
        private bool deviceHasMicrophone = true;

        [SerializeField]
        [Tooltip("Declare that this device has a usable speaker.")]
        private bool deviceHasSpeaker = true;

        [Header("Animation (experimental)")]
        [SerializeField]
        [Tooltip("Opt in to bot_animation ARKit-52 blendshape frames for lipsync. Requires the server to have A2F enabled AND a 16kHz connect. NOTE: the Unity SDK does not yet render these frames — see CLAUDE.md parity.")]
        private bool enableAnimation = false;

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
        /// LiveKit bot-audio auto-unmute safety-net window, in seconds (see field tooltip).
        /// </summary>
        public float BotAudioAutoUnmuteSeconds => Mathf.Max(0f, botAudioAutoUnmuteMs) / 1000f;

        /// <summary>
        /// Per-session device capability declaration sent in the auth payload.
        /// Built from the deviceHas* toggles. Never null — defaults are all true,
        /// matching the server's behavior when capabilities are omitted.
        /// </summary>
        public SessionCapabilities Capabilities =>
            new SessionCapabilities(deviceHasCamera, deviceHasMicrophone, deviceHasSpeaker);

        /// <summary>
        /// Whether to opt in to bot_animation blendshape frames. See the field
        /// tooltip — the Unity SDK does not yet render these; opting in only sets
        /// the auth flag.
        /// </summary>
        public bool EnableAnimation => enableAnimation;

        /// <summary>
        /// Enable debug logging.
        /// </summary>
        public bool DebugLogging
        {
            get => debugLogging;
            set => debugLogging = value;
        }

        /// <summary>
        /// Optional async token provider for Firebase or other auth.
        /// When set, the SDK uses Bearer token auth instead of API key.
        /// Set this at runtime: config.TokenProvider = () => FirebaseAuth.DefaultInstance.CurrentUser.TokenAsync(false);
        /// </summary>
        [NonSerialized]
        private Func<Task<string>> _tokenProvider;

        public Func<Task<string>> TokenProvider
        {
            get => _tokenProvider;
            set => _tokenProvider = value;
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
                && (!string.IsNullOrEmpty(apiKey) || _tokenProvider != null);
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

            if (string.IsNullOrEmpty(apiKey) && _tokenProvider == null)
            {
                return "API key is not set and no token provider configured";
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

