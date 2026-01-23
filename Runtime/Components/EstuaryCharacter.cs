using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using Estuary.Models;
using Estuary.Utilities;

namespace Estuary
{
    /// <summary>
    /// Component for making a GameObject an Estuary AI character.
    /// Attach this to any NPC that should be able to have conversations.
    /// </summary>
    [AddComponentMenu("Estuary/Estuary Character")]
    public class EstuaryCharacter : MonoBehaviour
    {
        #region Inspector Fields

        [Header("Character Settings")]
        [SerializeField]
        [Tooltip("The character ID from the Estuary dashboard")]
        private string characterId;

        [SerializeField]
        [Tooltip("Unique player identifier for conversation persistence")]
        private string playerId;

        [Header("Connection")]
        [SerializeField]
        [Tooltip("Automatically connect when the component starts")]
        private bool autoConnect = true;

        [SerializeField]
        [Tooltip("Automatically reconnect if connection is lost")]
        private bool autoReconnect = true;

        [Header("Voice")]
        [SerializeField]
        [Tooltip("Audio source for playing voice responses (optional, creates one if not set)")]
        private EstuaryAudioSource audioSource;

        [SerializeField]
        [Tooltip("Microphone component for voice input (optional)")]
        private EstuaryMicrophone microphone;

        [Header("Actions")]
        [SerializeField]
        [Tooltip("Strip action tags from bot response text (recommended for display)")]
        private bool stripActionsFromText = true;

        [Header("Events")]
        [SerializeField]
        private SessionConnectedEvent onConnected = new SessionConnectedEvent();

        [SerializeField]
        private UnityEvent onDisconnected = new UnityEvent();

        [SerializeField]
        private BotResponseEvent onBotResponse = new BotResponseEvent();

        [SerializeField]
        private BotVoiceEvent onVoiceReceived = new BotVoiceEvent();

        [SerializeField]
        private SttResponseEvent onTranscript = new SttResponseEvent();

        [SerializeField]
        private UnityEvent onInterrupt = new UnityEvent();

        [SerializeField]
        private ErrorEvent onError = new ErrorEvent();

        [SerializeField]
        [Tooltip("Fired when an action is parsed from a bot response")]
        private ActionReceivedEvent onActionReceived = new ActionReceivedEvent();

        #endregion

        #region Properties

        /// <summary>
        /// The character ID from the Estuary dashboard.
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
        /// Whether to automatically connect on Start.
        /// </summary>
        public bool AutoConnect
        {
            get => autoConnect;
            set => autoConnect = value;
        }

        /// <summary>
        /// Whether this character is currently connected.
        /// </summary>
        public bool IsConnected { get; private set; }

        /// <summary>
        /// Current session information.
        /// </summary>
        public SessionInfo CurrentSession { get; private set; }

        /// <summary>
        /// Whether a voice session is currently active.
        /// </summary>
        public bool IsVoiceSessionActive { get; private set; }

        /// <summary>
        /// The current partial response being built (for streaming).
        /// </summary>
        public string CurrentPartialResponse { get; private set; } = "";

        /// <summary>
        /// The message ID currently being processed.
        /// </summary>
        public string CurrentMessageId { get; private set; }

        #endregion

        #region C# Events

        /// <summary>
        /// Fired when session is connected.
        /// </summary>
        public event Action<SessionInfo> OnConnected;

        /// <summary>
        /// Fired when disconnected.
        /// </summary>
        public event Action OnDisconnected;

        /// <summary>
        /// Fired when a bot response is received.
        /// </summary>
        public event Action<BotResponse> OnBotResponse;

        /// <summary>
        /// Fired when voice audio is received.
        /// </summary>
        public event Action<BotVoice> OnVoiceReceived;

        /// <summary>
        /// Fired when speech-to-text result is received.
        /// </summary>
        public event Action<SttResponse> OnTranscript;

        /// <summary>
        /// Fired when an interrupt signal is received.
        /// </summary>
        public event Action<InterruptData> OnInterrupt;

        /// <summary>
        /// Fired when an error occurs.
        /// </summary>
        public event Action<string> OnError;

        /// <summary>
        /// Fired when connection state changes.
        /// </summary>
        public event Action<ConnectionState> OnConnectionStateChanged;

        /// <summary>
        /// Fired when an action is parsed from a bot response.
        /// Actions are embedded using XML-style tags: &lt;action name="sit" /&gt;
        /// </summary>
        public event Action<AgentAction> OnActionReceived;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            ValidateSettings();
        }

        private void Start()
        {
            // Register with manager
            if (EstuaryManager.HasInstance || autoConnect)
            {
                EstuaryManager.Instance.RegisterCharacter(this);

                if (autoConnect)
                {
                    Connect();
                }
            }
        }

        private void OnDestroy()
        {
            if (EstuaryManager.HasInstance)
            {
                EstuaryManager.Instance?.UnregisterCharacter(this);
            }
        }

        private void OnValidate()
        {
            ValidateSettings();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Connect to the Estuary server for this character.
        /// </summary>
        public void Connect()
        {
            if (string.IsNullOrEmpty(characterId))
            {
                Debug.LogError($"[EstuaryCharacter] Cannot connect: CharacterId is not set on {gameObject.name}");
                return;
            }

            if (string.IsNullOrEmpty(playerId))
            {
                Debug.LogError($"[EstuaryCharacter] Cannot connect: PlayerId is not set on {gameObject.name}");
                return;
            }

            // Make this the active character and connect
            EstuaryManager.Instance.SetActiveCharacter(this);
            EstuaryManager.Instance.Connect();
        }

        /// <summary>
        /// Disconnect from the server.
        /// </summary>
        public void Disconnect()
        {
            EstuaryManager.Instance.Disconnect();
        }

        /// <summary>
        /// Send a text message to this character.
        /// </summary>
        /// <param name="message">The message to send</param>
        public void SendText(string message)
        {
            _ = SendTextAsync(message);
        }

        /// <summary>
        /// Send a text message to this character asynchronously.
        /// </summary>
        /// <param name="message">The message to send</param>
        public async Task SendTextAsync(string message)
        {
            if (!IsConnected)
            {
                Debug.LogWarning($"[EstuaryCharacter] Cannot send text: not connected");
                return;
            }

            if (string.IsNullOrEmpty(message))
            {
                Debug.LogWarning($"[EstuaryCharacter] Cannot send empty message");
                return;
            }

            // Reset partial response state
            CurrentPartialResponse = "";
            CurrentMessageId = null;

            await EstuaryManager.Instance.SendTextAsync(message);
        }

        /// <summary>
        /// Start a voice session for this character.
        /// In LiveKit mode, this configures the microphone for native WebRTC capture with AEC.
        /// In WebSocket mode, this uses Unity's Microphone API.
        /// </summary>
        public void StartVoiceSession()
        {
            if (!IsConnected)
            {
                Debug.LogWarning($"[EstuaryCharacter] Cannot start voice session: not connected");
                return;
            }

            IsVoiceSessionActive = true;
            CurrentPartialResponse = "";
            CurrentMessageId = null;

            Debug.Log($"[EstuaryCharacter] Voice session started for {characterId}");

            // Configure and start microphone
            if (microphone != null)
            {
                // Configure microphone for the appropriate mode
                if (EstuaryManager.Instance.IsLiveKitEnabled)
                {
                    // LiveKit mode - need to wait for LiveKit to be ready
                    if (EstuaryManager.Instance.IsLiveKitReady)
                    {
                        // LiveKit already connected - start immediately
                        microphone.Configure(VoiceMode.LiveKit, EstuaryManager.Instance.LiveKitManager);
                        Debug.Log($"[EstuaryCharacter] Microphone configured for LiveKit (native capture with AEC)");
                        microphone.StartRecording();
                    }
                    else
                    {
                        // Wait for LiveKit to be ready
                        Debug.Log($"[EstuaryCharacter] Waiting for LiveKit to be ready before starting microphone...");
                        EstuaryManager.Instance.OnLiveKitReady += OnLiveKitReadyForVoice;
                    }
                }
                else
                {
                    // WebSocket mode - use Unity microphone capture
                    microphone.Configure(VoiceMode.WebSocket, null);
                    Debug.Log($"[EstuaryCharacter] Microphone configured for WebSocket");
                    microphone.StartRecording();
                }
            }
        }

        private void OnLiveKitReadyForVoice(string roomName)
        {
            // Unsubscribe to avoid multiple calls
            EstuaryManager.Instance.OnLiveKitReady -= OnLiveKitReadyForVoice;

            if (!IsVoiceSessionActive || microphone == null)
            {
                Debug.Log($"[EstuaryCharacter] Voice session no longer active, skipping microphone start");
                return;
            }

            Debug.Log($"[EstuaryCharacter] LiveKit ready, starting microphone with AEC");
            microphone.Configure(VoiceMode.LiveKit, EstuaryManager.Instance.LiveKitManager);
            microphone.StartRecording();
        }

        /// <summary>
        /// End the current voice session.
        /// </summary>
        public void EndVoiceSession()
        {
            IsVoiceSessionActive = false;

            Debug.Log($"[EstuaryCharacter] Voice session ended for {characterId}");

            // Unsubscribe from LiveKit ready event if we were waiting
            if (EstuaryManager.HasInstance)
            {
                EstuaryManager.Instance.OnLiveKitReady -= OnLiveKitReadyForVoice;
            }

            // Stop microphone if available
            if (microphone != null)
            {
                microphone.StopRecording();
            }
        }

        /// <summary>
        /// Stream audio data for speech-to-text.
        /// </summary>
        /// <param name="audioBase64">Base64-encoded audio data</param>
        public async Task StreamAudioAsync(string audioBase64)
        {
            if (!IsConnected || !IsVoiceSessionActive)
            {
                return;
            }

            await EstuaryManager.Instance.StreamAudioAsync(audioBase64);
        }

        /// <summary>
        /// Interrupt the current response (stop playback).
        /// </summary>
        public void Interrupt()
        {
            if (audioSource != null)
            {
                audioSource.StopPlayback();
            }

            CurrentPartialResponse = "";
            CurrentMessageId = null;
        }

        #endregion

        #region Internal Event Handlers (Called by EstuaryManager)

        internal void HandleSessionConnected(SessionInfo sessionInfo)
        {
            IsConnected = true;
            CurrentSession = sessionInfo;

            Debug.Log($"[EstuaryCharacter] Connected: {sessionInfo}");

            // Invoke events
            OnConnected?.Invoke(sessionInfo);
            onConnected?.Invoke(sessionInfo);
        }

        internal void HandleDisconnected(string reason)
        {
            IsConnected = false;
            CurrentSession = null;
            IsVoiceSessionActive = false;

            Debug.Log($"[EstuaryCharacter] Disconnected: {reason}");

            // Invoke events
            OnDisconnected?.Invoke();
            onDisconnected?.Invoke();

            // Auto-reconnect if enabled
            if (autoReconnect && reason != "client disconnect")
            {
                Debug.Log($"[EstuaryCharacter] Auto-reconnecting...");
                Connect();
            }
        }

        internal void HandleBotResponse(BotResponse response)
        {
            // Track message ID
            if (!string.IsNullOrEmpty(response.MessageId))
            {
                CurrentMessageId = response.MessageId;
            }

            // Parse and handle actions from the response text
            var responseText = response.Text;
            if (!string.IsNullOrEmpty(responseText) && ActionParser.ContainsActions(responseText))
            {
                var actions = ActionParser.ParseActions(responseText);
                
                // Fire action events for each parsed action
                foreach (var action in actions)
                {
                    Debug.Log($"[EstuaryCharacter] Action received: {action}");
                    OnActionReceived?.Invoke(action);
                    onActionReceived?.Invoke(action);
                }

                // Optionally strip action tags from text for display
                if (stripActionsFromText)
                {
                    responseText = ActionParser.StripActions(responseText);
                }
            }

            // Handle streaming responses
            if (response.IsFinal)
            {
                // Final response - use full text (possibly with actions stripped)
                CurrentPartialResponse = responseText;
            }
            else
            {
                // Partial response - accumulate
                CurrentPartialResponse += responseText;
            }

            // Invoke events with the (possibly modified) response
            OnBotResponse?.Invoke(response);
            onBotResponse?.Invoke(response);
        }

        internal void HandleBotVoice(BotVoice voice)
        {
            Debug.Log($"[EstuaryCharacter] HandleBotVoice called, audioSource={audioSource != null}, voice={voice}");
            
            // Play audio if we have an audio source
            if (audioSource != null)
            {
                audioSource.EnqueueAudio(voice);
            }
            else
            {
                Debug.LogWarning("[EstuaryCharacter] No audioSource assigned - voice will not play!");
            }

            // Invoke events
            OnVoiceReceived?.Invoke(voice);
            onVoiceReceived?.Invoke(voice);
        }

        internal void HandleSttResponse(SttResponse response)
        {
            // Invoke events
            OnTranscript?.Invoke(response);
            onTranscript?.Invoke(response);
        }

        internal void HandleInterrupt(InterruptData data)
        {
            // Stop current audio playback
            if (audioSource != null)
            {
                audioSource.StopPlayback();
            }

            // Clear current response state
            CurrentPartialResponse = "";
            CurrentMessageId = null;

            // Invoke events
            OnInterrupt?.Invoke(data);
            onInterrupt?.Invoke();
        }

        internal void HandleError(string error)
        {
            Debug.LogError($"[EstuaryCharacter] Error: {error}");

            // Invoke events
            OnError?.Invoke(error);
            onError?.Invoke(error);
        }

        internal void HandleConnectionStateChanged(ConnectionState state)
        {
            OnConnectionStateChanged?.Invoke(state);
        }

        #endregion

        #region Private Methods

        private void ValidateSettings()
        {
            if (string.IsNullOrEmpty(characterId))
            {
                Debug.LogWarning($"[EstuaryCharacter] CharacterId is not set on {gameObject.name}");
            }

            if (string.IsNullOrEmpty(playerId))
            {
                // Generate a default player ID if not set
                playerId = $"player_{SystemInfo.deviceUniqueIdentifier.Substring(0, 8)}";
            }
        }

        #endregion

        #region Unity Event Types

        [Serializable]
        public class SessionConnectedEvent : UnityEvent<SessionInfo> { }

        [Serializable]
        public class BotResponseEvent : UnityEvent<BotResponse> { }

        [Serializable]
        public class BotVoiceEvent : UnityEvent<BotVoice> { }

        [Serializable]
        public class SttResponseEvent : UnityEvent<SttResponse> { }

        [Serializable]
        public class ErrorEvent : UnityEvent<string> { }

        [Serializable]
        public class ActionReceivedEvent : UnityEvent<AgentAction> { }

        #endregion
    }
}






