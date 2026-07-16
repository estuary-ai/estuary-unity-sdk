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

        [SerializeField]
        [Tooltip("Automatically start a voice session after connecting")]
        private bool autoStartVoiceSession = false;

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

        [SerializeField]
        [Tooltip("Fired when the server ends the session for inactivity (disconnect follows; no auto-reconnect)")]
        private SessionTimeoutEvent onSessionTimeout = new SessionTimeoutEvent();

        [SerializeField]
        [Tooltip("Fired when the server releases voice after voice inactivity (socket stays; text keeps working)")]
        private VoiceTimeoutEvent onVoiceTimeout = new VoiceTimeoutEvent();

        [SerializeField]
        [Tooltip("Fired when the server requests a camera image (vision intent). Capture a frame and call SendCameraImage with the RequestId.")]
        private CameraCaptureRequestEvent onCameraCaptureRequested = new CameraCaptureRequestEvent();

        [SerializeField]
        [Tooltip("Fired when the server pushes newly extracted memories after a conversation ends")]
        private MemoryUpdatedEventUnity onMemoryUpdated = new MemoryUpdatedEventUnity();

        [SerializeField]
        [Tooltip("Fired when the server rejects the connection due to a policy cap (disconnect follows; no auto-reconnect)")]
        private SessionRejectedEvent onSessionRejected = new SessionRejectedEvent();

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
        /// Whether to automatically start voice after connecting.
        /// </summary>
        public bool AutoStartVoiceSession
        {
            get => autoStartVoiceSession;
            set => autoStartVoiceSession = value;
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

        // Set when the server ends the session (session_timeout). The client
        // layer suppresses its own reconnect, but this component ALSO
        // reconnects on disconnect (autoReconnect) — without this flag the
        // character layer would re-auth and resurrect billing in a loop even
        // though the socket layer behaved. Cleared on explicit Connect().
        private bool _serverEndedSession;

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

        /// <summary>
        /// Fired when the server ends the session due to inactivity. The server
        /// disconnects right after; the SDK will NOT auto-reconnect (at any
        /// layer). Resume with an explicit Connect() on user intent.
        /// </summary>
        public event Action<SessionTimeoutData> OnSessionTimeout;

        /// <summary>
        /// Fired when the server releases the voice session after no user speech
        /// for the voice-idle timeout. The socket stays connected and text chat
        /// continues. The mic has been stopped and voice state cleared — the
        /// recommended UX is to show the mic as auto-muted and call
        /// StartVoiceSession() when the user unmutes.
        /// </summary>
        public event Action<VoiceTimeoutData> OnVoiceTimeout;

        /// <summary>
        /// Fired when the server proactively requests a camera image (vision
        /// intent). Capture a frame, encode it as base64, and call
        /// SendCameraImage with the request's RequestId.
        /// </summary>
        public event Action<CameraCaptureRequest> OnCameraCaptureRequested;

        /// <summary>
        /// Fired when the server pushes newly extracted memories after a
        /// conversation ends (memory_updated).
        /// </summary>
        public event Action<MemoryUpdatedEvent> OnMemoryUpdated;

        /// <summary>
        /// Fired when the server rejects the connection because a policy cap was
        /// hit (e.g. the per-share-token concurrent-session limit). A disconnect
        /// follows; the SDK will NOT auto-reconnect (it would loop against the
        /// cap). Surface the reason and retry on explicit user intent.
        /// </summary>
        public event Action<SessionRejectedData> OnSessionRejected;

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

            // Explicit user intent overrides a prior server-ended session
            _serverEndedSession = false;

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
        /// Automatically suppresses TTS if voice session is not active.
        /// </summary>
        /// <param name="message">The message to send</param>
        public void SendText(string message)
        {
            _ = SendTextAsync(message);
        }

        /// <summary>
        /// Send a text message to this character with a text-only override.
        /// </summary>
        /// <param name="message">The message to send</param>
        /// <param name="textOnly">If true, suppress TTS for this message</param>
        public void SendText(string message, bool textOnly)
        {
            _ = SendTextAsync(message, textOnly);
        }

        /// <summary>
        /// Send a text message to this character asynchronously.
        /// Automatically suppresses TTS if voice session is not active.
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

            // Automatically suppress TTS when voice session is not active
            bool textOnly = !IsVoiceSessionActive;
            await EstuaryManager.Instance.SendTextAsync(message, textOnly);
        }

        /// <summary>
        /// Send a text message to this character asynchronously with a text-only override.
        /// </summary>
        /// <param name="message">The message to send</param>
        /// <param name="textOnly">If true, suppress TTS for this message</param>
        public async Task SendTextAsync(string message, bool textOnly)
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

            await EstuaryManager.Instance.SendTextAsync(message, textOnly);
        }

        /// <summary>
        /// Script this character to say a specific prewritten line with TTS.
        /// </summary>
        /// <param name="text">The scripted line text</param>
        /// <param name="textOnly">If true, text-only response (no TTS audio). Default false.</param>
        public void SayLine(string text, bool textOnly = false)
        {
            _ = SayLineAsync(text, textOnly);
        }

        /// <summary>
        /// Script this character to say a specific prewritten line with TTS asynchronously.
        /// </summary>
        /// <param name="text">The scripted line text</param>
        /// <param name="textOnly">If true, text-only response (no TTS audio). Default false.</param>
        public async Task SayLineAsync(string text, bool textOnly = false)
        {
            if (!IsConnected)
            {
                Debug.LogWarning("[EstuaryCharacter] Cannot say line: not connected");
                return;
            }
            // Reset partial response state
            CurrentPartialResponse = "";
            CurrentMessageId = null;
            await EstuaryManager.Instance.SayLineAsync(text, textOnly);
        }

        /// <summary>
        /// Start a voice session for this character.
        /// In LiveKit mode, this configures the microphone for native WebRTC capture with AEC.
        /// In WebSocket mode, this uses Unity's Microphone API.
        /// </summary>
        public void StartVoiceSession()
        {
            _ = StartVoiceSessionAsync();
        }

        /// <summary>
        /// Start a voice session for this character asynchronously.
        /// This enables backend STT (Deepgram) and starts the microphone.
        /// </summary>
        public async Task StartVoiceSessionAsync()
        {
            if (!IsConnected)
            {
                Debug.LogWarning($"[EstuaryCharacter] Cannot start voice session: not connected");
                return;
            }

            // Start voice mode on the backend first (enables Deepgram STT)
            await EstuaryManager.Instance.StartVoiceModeAsync();

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
                        // Subscribe to ready event before requesting token
                        Debug.Log($"[EstuaryCharacter] Waiting for LiveKit to be ready before starting microphone...");
                        EstuaryManager.Instance.OnLiveKitReady += OnLiveKitReadyForVoice;
                        
                        // Request LiveKit token if not already connecting
                        // This handles the case where AutoStartVoiceSession was false and we're starting manually
                        if (EstuaryManager.Instance.LiveKitState == LiveKitConnectionState.Disconnected)
                        {
                            Debug.Log($"[EstuaryCharacter] Requesting LiveKit token for manual voice start...");
                            await EstuaryManager.Instance.RequestLiveKitTokenAsync();
                        }
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
            _ = EndVoiceSessionAsync();
        }

        /// <summary>
        /// End the current voice session asynchronously.
        /// This stops the microphone and disables backend STT (Deepgram).
        /// </summary>
        public async Task EndVoiceSessionAsync()
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

            // Stop voice mode on the backend (disables Deepgram STT)
            if (EstuaryManager.HasInstance && EstuaryManager.Instance.IsConnected)
            {
                await EstuaryManager.Instance.StopVoiceModeAsync();
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

        /// <summary>
        /// Send a camera image to the character for vision-language processing.
        /// Call this in response to OnCameraCaptureRequested (echo the request's
        /// RequestId) or proactively. The reply arrives via the normal bot
        /// response/voice events.
        /// </summary>
        /// <param name="imageBase64">Base64-encoded image bytes (no data: URI prefix)</param>
        /// <param name="mimeType">Image MIME type, e.g. "image/jpeg"</param>
        /// <param name="requestId">Optional correlation ID from a camera_capture request</param>
        /// <param name="text">Optional accompanying prompt text</param>
        public void SendCameraImage(string imageBase64, string mimeType = "image/jpeg", string requestId = null, string text = null)
        {
            _ = SendCameraImageAsync(imageBase64, mimeType, requestId, text);
        }

        /// <summary>
        /// Send a camera image to the character asynchronously. See
        /// <see cref="SendCameraImage"/>.
        /// </summary>
        public async Task SendCameraImageAsync(string imageBase64, string mimeType = "image/jpeg", string requestId = null, string text = null)
        {
            if (!IsConnected)
            {
                Debug.LogWarning("[EstuaryCharacter] Cannot send camera image: not connected");
                return;
            }
            await EstuaryManager.Instance.SendCameraImageAsync(imageBase64, mimeType, requestId, text);
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

            if (autoStartVoiceSession && !IsVoiceSessionActive)
            {
                StartVoiceSession();
            }
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

            // Auto-reconnect if enabled — but NEVER after a server-side idle
            // reap (session_timeout). Reconnecting would re-authenticate and,
            // combined with voice auto-start, resurrect billed voice resources
            // with nobody talking, in a loop. Resume requires explicit Connect().
            if (_serverEndedSession)
            {
                _serverEndedSession = false;
                Debug.Log($"[EstuaryCharacter] Server ended the session (idle timeout) — skipping auto-reconnect. Call Connect() to resume.");
            }
            else if (autoReconnect && reason != "client disconnect")
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
            Debug.Log($"[EstuaryCharacter] HandleBotVoice: received={voice.SampleRate}Hz, expected={AudioSettings.outputSampleRate}Hz, match={voice.SampleRate == AudioSettings.outputSampleRate}");

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

        /// <summary>
        /// Track message ID for interrupt handling in LiveKit mode.
        /// In LiveKit mode, audio plays via WebRTC track, but we still need
        /// to track message_id for interrupt filtering.
        /// </summary>
        /// <param name="messageId">Message ID from the bot_voice metadata event</param>
        internal void TrackMessageId(string messageId)
        {
            if (!string.IsNullOrEmpty(messageId))
            {
                CurrentMessageId = messageId;
            }
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

        internal void HandleSessionTimeout(SessionTimeoutData data)
        {
            Debug.Log($"[EstuaryCharacter] Session ended by server for inactivity: {data}");

            // The disconnect that follows must not trigger this component's
            // auto-reconnect (see HandleDisconnected).
            _serverEndedSession = true;

            // Stop the mic now — the server has already torn down voice.
            ReleaseLocalVoiceState();

            // Invoke events
            OnSessionTimeout?.Invoke(data);
            onSessionTimeout?.Invoke(data);
        }

        internal void HandleVoiceTimeout(VoiceTimeoutData data)
        {
            Debug.Log($"[EstuaryCharacter] Voice released by server for voice inactivity: {data}");

            // The socket stays open and text keeps working — do NOT touch
            // _serverEndedSession (no disconnect follows). The server already
            // closed STT and deleted the room, so only local cleanup here; no
            // stop_voice is sent. Resume = StartVoiceSession() on user intent
            // (auto-mute illusion UX).
            ReleaseLocalVoiceState();

            // Invoke events
            OnVoiceTimeout?.Invoke(data);
            onVoiceTimeout?.Invoke(data);
        }

        /// <summary>
        /// Belt-and-braces cleanup for any server-side voice teardown the client
        /// didn't process as an event (e.g. the LiveKit room dying while the
        /// socket stays up). Idempotent: stops the mic and clears voice-active
        /// state so the next StartVoiceSession() starts fresh instead of
        /// streaming into a dead transport.
        /// </summary>
        internal void HandleVoiceTransportClosed()
        {
            if (!IsVoiceSessionActive) return;

            Debug.Log($"[EstuaryCharacter] Voice transport closed — clearing stale voice session state");
            ReleaseLocalVoiceState();
        }

        private void ReleaseLocalVoiceState()
        {
            IsVoiceSessionActive = false;

            // Unsubscribe the LiveKit-ready waiter in case a voice start was pending
            if (EstuaryManager.HasInstance)
            {
                EstuaryManager.Instance.OnLiveKitReady -= OnLiveKitReadyForVoice;
            }

            if (microphone != null)
            {
                microphone.StopRecording();
            }
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

        internal void HandleCameraCaptureRequest(CameraCaptureRequest request)
        {
            Debug.Log($"[EstuaryCharacter] Camera capture requested: {request}");

            OnCameraCaptureRequested?.Invoke(request);
            onCameraCaptureRequested?.Invoke(request);
        }

        internal void HandleMemoryUpdated(MemoryUpdatedEvent data)
        {
            Debug.Log($"[EstuaryCharacter] Memory updated: {data}");

            OnMemoryUpdated?.Invoke(data);
            onMemoryUpdated?.Invoke(data);
        }

        internal void HandleSessionRejected(SessionRejectedData data)
        {
            Debug.LogWarning($"[EstuaryCharacter] Session rejected by server: {data}");

            // The disconnect that follows must not trigger this component's
            // auto-reconnect — reconnecting would just hit the same policy cap
            // again in a loop (same reasoning as session_timeout).
            _serverEndedSession = true;

            // Idempotent local cleanup in case a voice session was somehow active.
            ReleaseLocalVoiceState();

            OnSessionRejected?.Invoke(data);
            onSessionRejected?.Invoke(data);
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

        [Serializable]
        public class SessionTimeoutEvent : UnityEvent<SessionTimeoutData> { }

        [Serializable]
        public class VoiceTimeoutEvent : UnityEvent<VoiceTimeoutData> { }

        [Serializable]
        public class CameraCaptureRequestEvent : UnityEvent<CameraCaptureRequest> { }

        // Named ...Unity to avoid a clash with the Estuary.Models.MemoryUpdatedEvent
        // data model it carries.
        [Serializable]
        public class MemoryUpdatedEventUnity : UnityEvent<MemoryUpdatedEvent> { }

        [Serializable]
        public class SessionRejectedEvent : UnityEvent<SessionRejectedData> { }

        #endregion
    }
}






