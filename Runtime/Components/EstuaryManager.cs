using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Estuary.Models;

namespace Estuary
{
    /// <summary>
    /// Singleton manager for the Estuary SDK.
    /// Handles the global connection and routes events to registered characters.
    /// </summary>
    [AddComponentMenu("Estuary/Estuary Manager")]
    public class EstuaryManager : MonoBehaviour
    {
        #region Singleton

        private static EstuaryManager _instance;
        private static readonly object _lock = new object();
        private static bool _applicationQuitting;

        /// <summary>
        /// Get the singleton instance of EstuaryManager.
        /// Creates a new GameObject if one doesn't exist.
        /// </summary>
        public static EstuaryManager Instance
        {
            get
            {
                if (_applicationQuitting)
                {
                    Debug.LogWarning("[EstuaryManager] Instance requested after application quit. Returning null.");
                    return null;
                }

                lock (_lock)
                {
                    if (_instance == null)
                    {
                        _instance = FindObjectOfType<EstuaryManager>();

                        if (_instance == null)
                        {
                            var go = new GameObject("EstuaryManager");
                            _instance = go.AddComponent<EstuaryManager>();
                            DontDestroyOnLoad(go);
                        }
                    }
                    return _instance;
                }
            }
        }

        /// <summary>
        /// Check if an instance exists without creating one.
        /// </summary>
        public static bool HasInstance => _instance != null;

        #endregion

        #region Inspector Fields

        [Header("Configuration")]
        [SerializeField]
        [Tooltip("Estuary configuration asset. Create via Assets > Create > Estuary > Config")]
        private EstuaryConfig config;

        [Header("Debug")]
        [SerializeField]
        [Tooltip("Enable debug logging")]
        private bool debugLogging = false;

        #endregion

        #region Properties

        /// <summary>
        /// The Estuary configuration.
        /// </summary>
        public EstuaryConfig Config
        {
            get => config;
            set => config = value;
        }

        /// <summary>
        /// Whether the SDK is connected to the server.
        /// </summary>
        public bool IsConnected => _client?.IsConnected ?? false;

        /// <summary>
        /// Current connection state.
        /// </summary>
        public ConnectionState ConnectionState => _client?.State ?? ConnectionState.Disconnected;

        /// <summary>
        /// Enable or disable debug logging.
        /// </summary>
        public bool DebugLogging
        {
            get => debugLogging;
            set
            {
                debugLogging = value;
                if (_client != null)
                    _client.DebugLogging = value;
                if (_liveKitManager != null)
                    _liveKitManager.DebugLogging = value;
            }
        }

        /// <summary>
        /// Current LiveKit connection state.
        /// </summary>
        public LiveKitConnectionState LiveKitState => _liveKitState;

        /// <summary>
        /// Whether LiveKit is ready for voice communication.
        /// </summary>
        public bool IsLiveKitReady => _liveKitState == LiveKitConnectionState.Ready;

        /// <summary>
        /// Whether LiveKit mode is enabled in the configuration.
        /// </summary>
        public bool IsLiveKitEnabled => config != null && config.IsLiveKitEnabled;

        /// <summary>
        /// Get the LiveKitVoiceManager for advanced usage.
        /// Returns null if not in LiveKit mode.
        /// </summary>
        public LiveKitVoiceManager LiveKitManager => _liveKitManager;

        #endregion

        #region Events

        /// <summary>
        /// Fired when connection state changes.
        /// </summary>
        public event EstuaryEvents.ConnectionStateHandler OnConnectionStateChanged;

        /// <summary>
        /// Fired when a global error occurs.
        /// </summary>
        public event EstuaryEvents.ErrorHandler OnError;

        /// <summary>
        /// Fired when LiveKit connection state changes.
        /// </summary>
        public event Action<LiveKitConnectionState> OnLiveKitStateChanged;

        /// <summary>
        /// Fired when LiveKit room is ready (bot joined, audio flowing).
        /// </summary>
        public event Action<string> OnLiveKitReady;

        #endregion

        #region Private Fields

        private EstuaryClient _client;
        private LiveKitVoiceManager _liveKitManager;
        private LiveKitConnectionState _liveKitState = LiveKitConnectionState.Disconnected;
        private LiveKitTokenResponse _pendingLiveKitToken;
        private readonly Dictionary<string, EstuaryCharacter> _registeredCharacters = new Dictionary<string, EstuaryCharacter>();
        private EstuaryCharacter _activeCharacter;
        private bool _initialized;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Debug.LogWarning("[EstuaryManager] Duplicate instance detected, destroying this one.");
                Destroy(gameObject);
                return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);

            Initialize();
        }

        private void Update()
        {
            // Process queued events from background threads
            _client?.ProcessMainThreadQueue();
            _liveKitManager?.ProcessMainThreadQueue();
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                Cleanup();
                _instance = null;
            }
        }

        private void OnApplicationQuit()
        {
            _applicationQuitting = true;
            Cleanup();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Connect to Estuary servers using the configured settings.
        /// </summary>
        public void Connect()
        {
            if (config == null)
            {
                Debug.LogError("[EstuaryManager] Cannot connect: Config is not set");
                return;
            }

            if (_activeCharacter == null)
            {
                Debug.LogError("[EstuaryManager] Cannot connect: No active character. Call RegisterCharacter first.");
                return;
            }

            _ = ConnectAsync();
        }

        /// <summary>
        /// Connect to Estuary servers asynchronously.
        /// </summary>
        public async Task ConnectAsync()
        {
            if (config == null)
            {
                Debug.LogError("[EstuaryManager] Cannot connect: Config is not set");
                return;
            }

            if (_activeCharacter == null)
            {
                Debug.LogError("[EstuaryManager] Cannot connect: No active character");
                return;
            }

            try
            {
                await _client.ConnectAsync(
                    config.ServerUrl,
                    config.ApiKey,
                    _activeCharacter.CharacterId,
                    _activeCharacter.PlayerId
                );
            }
            catch (Exception e)
            {
                Debug.LogError($"[EstuaryManager] Connection failed: {e.Message}");
            }
        }

        /// <summary>
        /// Disconnect from Estuary servers.
        /// </summary>
        public void Disconnect()
        {
            _ = DisconnectAsync();
        }

        /// <summary>
        /// Disconnect from Estuary servers asynchronously.
        /// </summary>
        public async Task DisconnectAsync()
        {
            // Disconnect LiveKit first
            if (_liveKitManager != null && _liveKitManager.IsConnected)
            {
                await DisconnectLiveKitAsync();
            }

            // Then disconnect Socket.IO
            if (_client != null)
            {
                await _client.DisconnectAsync();
            }
        }

        /// <summary>
        /// Register a character with the manager.
        /// </summary>
        /// <param name="character">The character to register</param>
        public void RegisterCharacter(EstuaryCharacter character)
        {
            if (character == null)
            {
                Debug.LogError("[EstuaryManager] Cannot register null character");
                return;
            }

            var key = GetCharacterKey(character);
            _registeredCharacters[key] = character;

            Log($"Registered character: {character.CharacterId} (player: {character.PlayerId})");

            // If this is the first character, make it active
            if (_activeCharacter == null)
            {
                SetActiveCharacter(character);
            }
        }

        /// <summary>
        /// Unregister a character from the manager.
        /// </summary>
        /// <param name="character">The character to unregister</param>
        public void UnregisterCharacter(EstuaryCharacter character)
        {
            if (character == null) return;

            var key = GetCharacterKey(character);
            _registeredCharacters.Remove(key);

            Log($"Unregistered character: {character.CharacterId}");

            // If this was the active character, clear it
            if (_activeCharacter == character)
            {
                _activeCharacter = null;
            }
        }

        /// <summary>
        /// Set the active character for the current connection.
        /// This will disconnect and reconnect if already connected.
        /// </summary>
        /// <param name="character">The character to make active</param>
        public void SetActiveCharacter(EstuaryCharacter character)
        {
            if (character == null)
            {
                Debug.LogError("[EstuaryManager] Cannot set null character as active");
                return;
            }

            var wasConnected = IsConnected;
            var previousCharacter = _activeCharacter;

            _activeCharacter = character;

            Log($"Active character set to: {character.CharacterId}");

            // Reconnect if needed
            if (wasConnected && previousCharacter != character)
            {
                _ = ReconnectWithNewCharacter();
            }
        }

        /// <summary>
        /// Send a text message to the current character.
        /// </summary>
        /// <param name="text">The message text</param>
        public async Task SendTextAsync(string text)
        {
            if (_client == null || !_client.IsConnected)
            {
                Debug.LogError("[EstuaryManager] Cannot send text: not connected");
                return;
            }

            await _client.SendTextAsync(text);
        }

        /// <summary>
        /// Stream audio data to the server.
        /// </summary>
        /// <param name="audioBase64">Base64-encoded audio</param>
        public async Task StreamAudioAsync(string audioBase64)
        {
            if (_client == null || !_client.IsConnected)
            {
                return;
            }

            await _client.StreamAudioAsync(audioBase64);
        }

        /// <summary>
        /// Notify the server that audio playback has completed.
        /// </summary>
        public async Task NotifyAudioPlaybackCompleteAsync()
        {
            if (_client == null || !_client.IsConnected)
            {
                return;
            }

            await _client.NotifyAudioPlaybackCompleteAsync();
        }

        #region LiveKit Methods

        /// <summary>
        /// Request a LiveKit token from the server.
        /// Call this after Socket.IO session is established to initiate LiveKit voice mode.
        /// </summary>
        public async Task RequestLiveKitTokenAsync()
        {
            if (_client == null || !_client.IsConnected)
            {
                Debug.LogError("[EstuaryManager] Cannot request LiveKit token: not connected");
                return;
            }

            SetLiveKitState(LiveKitConnectionState.RequestingToken);
            Log("Requesting LiveKit token...");

            await _client.RequestLiveKitTokenAsync();
        }

        /// <summary>
        /// Connect to LiveKit using a previously received token.
        /// This is called automatically when a token is received if AutoConnectLiveKit is enabled.
        /// </summary>
        public async Task ConnectLiveKitAsync()
        {
            if (_pendingLiveKitToken == null)
            {
                Debug.LogError("[EstuaryManager] Cannot connect LiveKit: no token available");
                return;
            }

            await ConnectLiveKitWithTokenAsync(_pendingLiveKitToken);
        }

        /// <summary>
        /// Connect to LiveKit with a specific token response.
        /// </summary>
        /// <param name="tokenResponse">The token response from the server</param>
        public async Task ConnectLiveKitWithTokenAsync(LiveKitTokenResponse tokenResponse)
        {
            if (_liveKitManager == null)
            {
                Debug.LogError("[EstuaryManager] LiveKit manager not initialized");
                return;
            }

            if (string.IsNullOrEmpty(tokenResponse?.token))
            {
                Debug.LogError("[EstuaryManager] Invalid LiveKit token");
                return;
            }

            SetLiveKitState(LiveKitConnectionState.Connecting);
            Log($"Connecting to LiveKit room: {tokenResponse.room}");

            var success = await _liveKitManager.ConnectAsync(
                tokenResponse.url,
                tokenResponse.token,
                tokenResponse.room
            );

            if (!success)
            {
                SetLiveKitState(LiveKitConnectionState.Error);
                Debug.LogError("[EstuaryManager] Failed to connect to LiveKit");
            }
        }

        /// <summary>
        /// Disconnect from LiveKit and clean up resources.
        /// </summary>
        public async Task DisconnectLiveKitAsync()
        {
            if (_liveKitManager == null)
                return;

            // Notify server we're leaving
            if (_client != null && _client.IsConnected)
            {
                await _client.NotifyLiveKitLeftAsync();
            }

            await _liveKitManager.DisconnectAsync();
            SetLiveKitState(LiveKitConnectionState.Disconnected);
        }

        /// <summary>
        /// Start publishing microphone audio via LiveKit.
        /// Call this after LiveKit is ready.
        /// </summary>
        public async Task StartLiveKitPublishingAsync()
        {
            if (_liveKitManager == null || !_liveKitManager.IsConnected)
            {
                Debug.LogError("[EstuaryManager] Cannot start publishing: LiveKit not connected");
                return;
            }

            Log("Starting LiveKit audio publishing...");
            await _liveKitManager.StartPublishingAsync();
        }

        /// <summary>
        /// Stop publishing microphone audio via LiveKit.
        /// </summary>
        public async Task StopLiveKitPublishingAsync()
        {
            if (_liveKitManager == null)
                return;

            Log("Stopping LiveKit audio publishing...");
            await _liveKitManager.StopPublishingAsync();
        }

        #endregion

        #endregion

        #region Private Methods

        private void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            _client = new EstuaryClient
            {
                DebugLogging = debugLogging
            };

            // Subscribe to client events
            _client.OnSessionConnected += HandleSessionConnected;
            _client.OnDisconnected += HandleDisconnected;
            _client.OnBotResponse += HandleBotResponse;
            _client.OnBotVoice += HandleBotVoice;
            _client.OnSttResponse += HandleSttResponse;
            _client.OnInterrupt += HandleInterrupt;
            _client.OnError += HandleError;
            _client.OnConnectionStateChanged += HandleConnectionStateChanged;

            // Subscribe to LiveKit client events
            _client.OnLiveKitTokenReceived += HandleLiveKitTokenReceived;
            _client.OnLiveKitReady += HandleLiveKitRoomReady;
            _client.OnLiveKitError += HandleLiveKitError;

            // Create LiveKit manager
            _liveKitManager = new LiveKitVoiceManager
            {
                DebugLogging = debugLogging
            };

            // Set this MonoBehaviour as the coroutine runner for LiveKit
            _liveKitManager.SetCoroutineRunner(this);

            // Subscribe to LiveKit manager events
            _liveKitManager.OnConnected += HandleLiveKitConnected;
            _liveKitManager.OnDisconnected += HandleLiveKitDisconnected;
            _liveKitManager.OnError += HandleLiveKitManagerError;

            Log("EstuaryManager initialized");
        }

        private void Cleanup()
        {
            // Cleanup LiveKit manager
            if (_liveKitManager != null)
            {
                _liveKitManager.OnConnected -= HandleLiveKitConnected;
                _liveKitManager.OnDisconnected -= HandleLiveKitDisconnected;
                _liveKitManager.OnError -= HandleLiveKitManagerError;
                _liveKitManager.Dispose();
                _liveKitManager = null;
            }

            if (_client != null)
            {
                _client.OnSessionConnected -= HandleSessionConnected;
                _client.OnDisconnected -= HandleDisconnected;
                _client.OnBotResponse -= HandleBotResponse;
                _client.OnBotVoice -= HandleBotVoice;
                _client.OnSttResponse -= HandleSttResponse;
                _client.OnInterrupt -= HandleInterrupt;
                _client.OnError -= HandleError;
                _client.OnConnectionStateChanged -= HandleConnectionStateChanged;
                _client.OnLiveKitTokenReceived -= HandleLiveKitTokenReceived;
                _client.OnLiveKitReady -= HandleLiveKitRoomReady;
                _client.OnLiveKitError -= HandleLiveKitError;

                _client.Dispose();
                _client = null;
            }

            _registeredCharacters.Clear();
            _activeCharacter = null;
            _initialized = false;
            _liveKitState = LiveKitConnectionState.Disconnected;
            _pendingLiveKitToken = null;
        }

        private async Task ReconnectWithNewCharacter()
        {
            await DisconnectAsync();
            await Task.Delay(100);
            await ConnectAsync();
        }

        private string GetCharacterKey(EstuaryCharacter character)
        {
            return $"{character.CharacterId}:{character.PlayerId}";
        }

        #endregion

        #region Event Handlers

        private void HandleSessionConnected(SessionInfo sessionInfo)
        {
            Log($"Session connected: {sessionInfo}");

            // Notify active character
            _activeCharacter?.HandleSessionConnected(sessionInfo);

            // Auto-connect to LiveKit if enabled
            if (config != null && config.IsLiveKitEnabled && config.AutoConnectLiveKit)
            {
                Log("Auto-requesting LiveKit token...");
                _ = RequestLiveKitTokenAsync();
            }
        }

        private void HandleDisconnected(string reason)
        {
            Log($"Disconnected: {reason}");

            // Notify active character
            _activeCharacter?.HandleDisconnected(reason);
        }

        private void HandleBotResponse(BotResponse response)
        {
            Log($"Bot response: {response}");

            // Route to active character
            _activeCharacter?.HandleBotResponse(response);
        }

        private void HandleBotVoice(BotVoice voice)
        {
            Log($"Bot voice: {voice}");

            // Route to active character
            _activeCharacter?.HandleBotVoice(voice);
        }

        private void HandleSttResponse(SttResponse response)
        {
            Log($"STT response: {response}");

            // Route to active character
            _activeCharacter?.HandleSttResponse(response);
        }

        private void HandleInterrupt(InterruptData data)
        {
            Log($"Interrupt: {data}");

            // Route to active character
            _activeCharacter?.HandleInterrupt(data);
        }

        private void HandleError(string error)
        {
            Debug.LogError($"[EstuaryManager] Error: {error}");
            OnError?.Invoke(error);

            // Notify active character
            _activeCharacter?.HandleError(error);
        }

        private void HandleConnectionStateChanged(ConnectionState state)
        {
            Log($"Connection state: {state}");
            OnConnectionStateChanged?.Invoke(state);

            // Notify active character
            _activeCharacter?.HandleConnectionStateChanged(state);

            // Disconnect LiveKit if Socket.IO disconnects
            if (state == ConnectionState.Disconnected && _liveKitState != LiveKitConnectionState.Disconnected)
            {
                _ = DisconnectLiveKitAsync();
            }
        }

        #endregion

        #region LiveKit Event Handlers

        private async void HandleLiveKitTokenReceived(LiveKitTokenResponse tokenResponse)
        {
            Log($"Received LiveKit token for room: {tokenResponse.room}");
            _pendingLiveKitToken = tokenResponse;

            // Auto-connect if configured
            if (config != null && config.AutoConnectLiveKit)
            {
                await ConnectLiveKitWithTokenAsync(tokenResponse);
            }
        }

        private async void HandleLiveKitConnected(string roomName)
        {
            Log($"Connected to LiveKit room: {roomName}");
            SetLiveKitState(LiveKitConnectionState.WaitingForBot);

            // Notify server that we've joined
            if (_client != null && _client.IsConnected)
            {
                await _client.NotifyLiveKitJoinedAsync();
            }

            // Start publishing audio
            if (config != null && config.AutoConnectLiveKit)
            {
                await StartLiveKitPublishingAsync();
            }
        }

        private void HandleLiveKitRoomReady(string roomName)
        {
            Log($"LiveKit room ready: {roomName}");
            SetLiveKitState(LiveKitConnectionState.Ready);
            OnLiveKitReady?.Invoke(roomName);
        }

        private void HandleLiveKitDisconnected(string reason)
        {
            Log($"LiveKit disconnected: {reason}");
            SetLiveKitState(LiveKitConnectionState.Disconnected);
        }

        private void HandleLiveKitError(string error)
        {
            Debug.LogError($"[EstuaryManager] LiveKit error: {error}");
            SetLiveKitState(LiveKitConnectionState.Error);
            OnError?.Invoke($"LiveKit error: {error}");
        }

        private void HandleLiveKitManagerError(string error)
        {
            Debug.LogError($"[EstuaryManager] LiveKit manager error: {error}");
            OnError?.Invoke($"LiveKit error: {error}");
        }

        private void SetLiveKitState(LiveKitConnectionState newState)
        {
            if (_liveKitState != newState)
            {
                _liveKitState = newState;
                Log($"LiveKit state: {newState}");
                OnLiveKitStateChanged?.Invoke(newState);
            }
        }

        #endregion

        #region Logging

        private void Log(string message)
        {
            if (debugLogging)
            {
                Debug.Log($"[EstuaryManager] {message}");
            }
        }

        #endregion
    }
}




