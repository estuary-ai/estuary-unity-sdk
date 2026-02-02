using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Estuary.Models;

namespace Estuary
{
    /// <summary>
    /// Low-level Socket.IO client for communicating with Estuary servers.
    /// This class handles the raw WebSocket connection and message parsing.
    /// 
    /// Note: This implementation uses a WebSocket-based Socket.IO client.
    /// For production, integrate with socket.io-client-csharp (SocketIOClient NuGet package).
    /// </summary>
    public class EstuaryClient : IDisposable
    {
        #region Constants

        private const string SDK_NAMESPACE = "/sdk";
        private const int RECONNECT_DELAY_MS = 2000;
        private const int MAX_RECONNECT_ATTEMPTS = 5;

        #endregion

        #region Events

        /// <summary>
        /// Fired when successfully connected and session is established.
        /// </summary>
        public event EstuaryEvents.SessionConnectedHandler OnSessionConnected;

        /// <summary>
        /// Fired when disconnected from the server.
        /// </summary>
        public event EstuaryEvents.DisconnectedHandler OnDisconnected;

        /// <summary>
        /// Fired when a bot text response is received.
        /// </summary>
        public event EstuaryEvents.BotResponseHandler OnBotResponse;

        /// <summary>
        /// Fired when bot voice audio is received.
        /// </summary>
        public event EstuaryEvents.BotVoiceHandler OnBotVoice;

        /// <summary>
        /// Fired when speech-to-text result is received.
        /// </summary>
        public event EstuaryEvents.SttResponseHandler OnSttResponse;

        /// <summary>
        /// Fired when an interrupt signal is received.
        /// </summary>
        public event EstuaryEvents.InterruptHandler OnInterrupt;

        /// <summary>
        /// Fired when an error occurs.
        /// </summary>
        public event EstuaryEvents.ErrorHandler OnError;

        /// <summary>
        /// Fired when connection state changes.
        /// </summary>
        public event EstuaryEvents.ConnectionStateHandler OnConnectionStateChanged;

        /// <summary>
        /// Fired when a LiveKit token is received from the server.
        /// </summary>
        public event EstuaryEvents.LiveKitTokenReceivedHandler OnLiveKitTokenReceived;

        /// <summary>
        /// Fired when the LiveKit room is ready (bot has joined).
        /// </summary>
        public event EstuaryEvents.LiveKitReadyHandler OnLiveKitReady;

        /// <summary>
        /// Fired when a LiveKit error occurs.
        /// </summary>
        public event EstuaryEvents.LiveKitErrorHandler OnLiveKitError;

        /// <summary>
        /// Fired when voice mode (backend STT) is started.
        /// </summary>
        public event EstuaryEvents.VoiceStartedHandler OnVoiceStarted;

        /// <summary>
        /// Fired when voice mode (backend STT) is stopped.
        /// </summary>
        public event EstuaryEvents.VoiceStoppedHandler OnVoiceStopped;

        /// <summary>
        /// Fired when a voice mode error occurs.
        /// </summary>
        public event EstuaryEvents.VoiceErrorHandler OnVoiceError;

        /// <summary>
        /// Fired when the API key owner has exceeded their monthly interaction quota.
        /// </summary>
        public event EstuaryEvents.QuotaExceededHandler OnQuotaExceeded;

        /// <summary>
        /// Fired when a scene graph update is received from the world model.
        /// </summary>
        public event EstuaryEvents.SceneGraphUpdateHandler OnSceneGraphUpdate;

        /// <summary>
        /// Fired when a room is identified by the world model.
        /// </summary>
        public event EstuaryEvents.RoomIdentifiedHandler OnRoomIdentified;

        #endregion

        #region Properties

        /// <summary>
        /// Current connection state.
        /// </summary>
        public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

        /// <summary>
        /// Whether the client is currently connected.
        /// </summary>
        public bool IsConnected => State == ConnectionState.Connected;

        /// <summary>
        /// Current session information (null if not connected).
        /// </summary>
        public SessionInfo CurrentSession { get; private set; }

        /// <summary>
        /// Enable debug logging.
        /// </summary>
        public bool DebugLogging { get; set; }

        /// <summary>
        /// Whether voice mode (backend STT) is currently active.
        /// </summary>
        public bool IsVoiceModeActive => _isVoiceModeActive;

        #endregion

        #region Private Fields

        private string _serverUrl;
        private string _apiKey;
        private string _characterId;
        private string _playerId;

        private CancellationTokenSource _cancellationTokenSource;
        private int _reconnectAttempts;
        private bool _disposed;
        private bool _isVoiceModeActive;

        // Queue for main thread dispatching
        private readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();

        // Simulated socket connection (replace with actual SocketIOClient in production)
        private ISocketIOConnection _socket;

        #endregion

        #region Public Methods

        /// <summary>
        /// Connect to an Estuary character.
        /// </summary>
        /// <param name="serverUrl">The Estuary server URL (e.g., https://api.estuary-ai.com)</param>
        /// <param name="apiKey">Your Estuary API key</param>
        /// <param name="characterId">The character UUID to connect to</param>
        /// <param name="playerId">Unique player identifier for conversation persistence</param>
        public async Task ConnectAsync(string serverUrl, string apiKey, string characterId, string playerId)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(EstuaryClient));

            if (string.IsNullOrEmpty(serverUrl))
                throw new ArgumentNullException(nameof(serverUrl));
            if (string.IsNullOrEmpty(apiKey))
                throw new ArgumentNullException(nameof(apiKey));
            if (string.IsNullOrEmpty(characterId))
                throw new ArgumentNullException(nameof(characterId));
            if (string.IsNullOrEmpty(playerId))
                throw new ArgumentNullException(nameof(playerId));

            _serverUrl = serverUrl;
            _apiKey = apiKey;
            _characterId = characterId;
            _playerId = playerId;

            await ConnectInternalAsync();
        }

        /// <summary>
        /// Disconnect from the server.
        /// </summary>
        public async Task DisconnectAsync()
        {
            if (_disposed) return;

            _cancellationTokenSource?.Cancel();

            if (_socket != null)
            {
                try
                {
                    await _socket.DisconnectAsync();
                }
                catch (Exception e)
                {
                    Log($"Error during disconnect: {e.Message}");
                }
            }

            SetState(ConnectionState.Disconnected);
            CurrentSession = null;
            _reconnectAttempts = 0;
        }

        /// <summary>
        /// Send a text message to the character.
        /// </summary>
        /// <param name="text">The message text</param>
        public async Task SendTextAsync(string text)
        {
            if (!IsConnected)
            {
                LogError("Cannot send text: not connected");
                return;
            }

            var payload = new TextPayload { text = text };
            await _socket.EmitAsync("text", payload);
            Log($"Sent text: {text}");
        }

        /// <summary>
        /// Send a text message with an explicit text-only override.
        /// </summary>
        /// <param name="text">The message text</param>
        /// <param name="textOnly">If true, suppress TTS for this message</param>
        public async Task SendTextAsync(string text, bool textOnly)
        {
            if (!IsConnected)
            {
                LogError("Cannot send text: not connected");
                return;
            }

            var payload = new TextPayloadWithMode { text = text, textOnly = textOnly };
            await _socket.EmitAsync("text", payload);
            Log($"Sent text (textOnly={textOnly}): {text}");
        }

        /// <summary>
        /// Stream audio data to the server for speech-to-text.
        /// </summary>
        /// <param name="audioBase64">Base64-encoded 16-bit PCM audio at 16kHz</param>
        public async Task StreamAudioAsync(string audioBase64)
        {
            if (!IsConnected)
            {
                LogError("Cannot stream audio: not connected");
                return;
            }

            var payload = new AudioPayload { audio = audioBase64 };
            await _socket.EmitAsync("stream_audio", payload);
        }

        /// <summary>
        /// Notify the server that audio playback has completed.
        /// </summary>
        public async Task NotifyAudioPlaybackCompleteAsync()
        {
            if (!IsConnected)
            {
                LogError("Cannot notify playback complete: not connected");
                return;
            }

            await _socket.EmitAsync("audio_playback_complete", null);
            Log("Notified audio playback complete");
        }

        /// <summary>
        /// Request a LiveKit token for WebRTC voice chat.
        /// The server will respond with a token, URL, and room name.
        /// </summary>
        public async Task RequestLiveKitTokenAsync()
        {
            if (!IsConnected)
            {
                LogError("Cannot request LiveKit token: not connected");
                return;
            }

            Log("Requesting LiveKit token...");
            await _socket.EmitAsync("livekit_token", null);
        }

        /// <summary>
        /// Start voice mode on the backend (enables Deepgram STT).
        /// Call this before streaming audio to enable speech-to-text.
        /// The server will respond with a voice_started event.
        /// </summary>
        public async Task StartVoiceModeAsync()
        {
            if (!IsConnected)
            {
                LogError("Cannot start voice mode: not connected");
                return;
            }

            if (_isVoiceModeActive)
            {
                Log("Voice mode already active");
                return;
            }

            Log("Starting voice mode...");
            await _socket.EmitAsync("start_voice", null);
        }

        /// <summary>
        /// Stop voice mode on the backend (disables Deepgram STT).
        /// Call this when switching back to text-only mode.
        /// The server will respond with a voice_stopped event.
        /// </summary>
        public async Task StopVoiceModeAsync()
        {
            if (!IsConnected)
            {
                LogError("Cannot stop voice mode: not connected");
                return;
            }

            if (!_isVoiceModeActive)
            {
                Log("Voice mode not active");
                return;
            }

            Log("Stopping voice mode...");
            await _socket.EmitAsync("stop_voice", null);
        }

        /// <summary>
        /// Notify the server that the client has joined the LiveKit room.
        /// This triggers the bot to join the same room.
        /// </summary>
        public async Task NotifyLiveKitJoinedAsync()
        {
            if (!IsConnected)
            {
                LogError("Cannot notify LiveKit join: not connected");
                return;
            }

            Log("Notifying server of LiveKit join...");
            await _socket.EmitAsync("livekit_join", null);
        }

        /// <summary>
        /// Notify the server that the client has left the LiveKit room.
        /// </summary>
        public async Task NotifyLiveKitLeftAsync()
        {
            if (!IsConnected)
            {
                LogError("Cannot notify LiveKit leave: not connected");
                return;
            }

            Log("Notifying server of LiveKit leave...");
            await _socket.EmitAsync("livekit_leave", null);
        }

        /// <summary>
        /// Notify the server of mute state change (for UI sync).
        /// </summary>
        /// <param name="muted">Whether the microphone is muted</param>
        public async Task NotifyLiveKitMuteAsync(bool muted)
        {
            if (!IsConnected)
            {
                return;
            }

            var payload = new LiveKitMutePayload { muted = muted };
            await _socket.EmitAsync("livekit_mute", payload);
        }

        /// <summary>
        /// Notify the server that the client is interrupting the current response.
        /// This signals the server to stop streaming TTS audio and stop generation.
        /// </summary>
        /// <param name="messageId">Optional message ID being interrupted</param>
        public async Task NotifyInterruptAsync(string messageId = null)
        {
            if (!IsConnected)
            {
                Log("Cannot notify interrupt: not connected");
                return;
            }

            Log($"Notifying server of client interrupt (messageId: {messageId ?? "none"})");
            var payload = new ClientInterruptPayload { message_id = messageId };
            await _socket.EmitAsync("client_interrupt", payload);
        }

        [Serializable]
        private class ClientInterruptPayload
        {
            public string message_id;
        }

        /// <summary>
        /// Emit a custom event to the server.
        /// Used by world model components to send video frames, poses, etc.
        /// </summary>
        /// <param name="eventName">Event name</param>
        /// <param name="data">Event data (must be serializable)</param>
        public async Task EmitAsync(string eventName, object data)
        {
            if (!IsConnected)
            {
                LogError($"Cannot emit {eventName}: not connected");
                return;
            }

            await _socket.EmitAsync(eventName, data);
            Log($"Emitted {eventName}");
        }

        /// <summary>
        /// Enable LiveKit video streaming for world model.
        /// When enabled, video frames sent via LiveKit will be forwarded to the world model.
        /// </summary>
        /// <param name="sessionId">World model session ID</param>
        /// <param name="targetFps">Target FPS for video processing (default 10)</param>
        public async Task EnableLiveKitVideoAsync(string sessionId, int targetFps = 10)
        {
            if (!IsConnected)
            {
                LogError("Cannot enable LiveKit video: not connected");
                return;
            }

            var payload = new EnableLiveKitVideoPayload
            {
                sessionId = sessionId,
                targetFps = targetFps
            };
            await _socket.EmitAsync("enable_livekit_video", payload);
            Log($"Enabled LiveKit video for session {sessionId}");
        }

        /// <summary>
        /// Disable LiveKit video streaming for world model.
        /// </summary>
        public async Task DisableLiveKitVideoAsync()
        {
            if (!IsConnected)
            {
                return;
            }

            await _socket.EmitAsync("disable_livekit_video", null);
            Log("Disabled LiveKit video");
        }

        [Serializable]
        private class EnableLiveKitVideoPayload
        {
            public string sessionId;
            public int targetFps;
        }

        /// <summary>
        /// Process queued events on the main thread. Call this from Update().
        /// </summary>
        public void ProcessMainThreadQueue()
        {
            while (_mainThreadQueue.TryDequeue(out var action))
            {
                try
                {
                    action?.Invoke();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        #endregion

        #region Private Methods

        private async Task ConnectInternalAsync()
        {
            SetState(ConnectionState.Connecting);
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                // Create socket connection
                _socket = CreateSocketConnection();

                // Set up event handlers
                _socket.OnConnected += HandleConnected;
                _socket.OnDisconnected += HandleDisconnected;
                _socket.OnError += HandleError;
                _socket.On("session_info", HandleSessionInfo);
                _socket.On("bot_response", HandleBotResponse);
                _socket.On("bot_voice", HandleBotVoice);
                _socket.On("stt_response", HandleSttResponse);
                _socket.On("interrupt", HandleInterruptEvent);
                _socket.On("auth_error", HandleAuthError);
                _socket.On("error", HandleServerError);

                // LiveKit event handlers
                _socket.On("livekit_token", HandleLiveKitToken);
                _socket.On("livekit_ready", HandleLiveKitReady);
                _socket.On("livekit_error", HandleLiveKitError);

                // Voice mode event handlers
                _socket.On("voice_started", HandleVoiceStarted);
                _socket.On("voice_stopped", HandleVoiceStopped);
                _socket.On("voice_error", HandleVoiceError);

                // Quota event handler
                _socket.On("quota_exceeded", HandleQuotaExceeded);

                // World model event handlers
                _socket.On("scene_graph_update", HandleSceneGraphUpdate);
                _socket.On("room_identified", HandleRoomIdentified);

                // Connect WITH auth - Socket.IO v4 passes auth in the namespace connect message
                var auth = new AuthenticateData
                {
                    api_key = _apiKey,
                    character_id = _characterId,
                    player_id = _playerId
                };
                await _socket.ConnectAsync(_serverUrl, SDK_NAMESPACE, auth);
                Log($"Connecting to {_serverUrl}{SDK_NAMESPACE} with auth for character {_characterId}...");
            }
            catch (Exception e)
            {
                LogError($"Connection failed: {e.Message}");
                SetState(ConnectionState.Error);
                DispatchToMainThread(() => OnError?.Invoke(e.Message));
                await HandleReconnect();
            }
        }

        private ISocketIOConnection CreateSocketConnection()
        {
            // In production, replace this with actual SocketIOClient:
            // return new SocketIOClientWrapper();
            
            // For now, use the built-in WebSocket implementation
            return new BuiltInSocketIOConnection();
        }

        private void HandleConnected()
        {
            // Auth was sent in the namespace connect message, server should respond with session_info
            Log("Socket connected to namespace, waiting for session_info...");
            _reconnectAttempts = 0;
        }
        
        [Serializable]
        private class AuthenticateData
        {
            public string api_key;
            public string character_id;
            public string player_id;
        }
        
        [Serializable]
        private class TextPayload
        {
            public string text;
        }

        [Serializable]
        private class TextPayloadWithMode
        {
            public string text;
            public bool textOnly;
        }
        
        [Serializable]
        private class AudioPayload
        {
            public string audio;
        }

        [Serializable]
        private class LiveKitMutePayload
        {
            public bool muted;
        }

        private void HandleDisconnected(string reason)
        {
            Log($"Socket disconnected: {reason}");
            SetState(ConnectionState.Disconnected);
            CurrentSession = null;
            DispatchToMainThread(() => OnDisconnected?.Invoke(reason));

            // Attempt reconnect if not intentional
            if (reason != "client disconnect")
            {
                _ = HandleReconnect();
            }
        }

        private void HandleError(string error)
        {
            LogError($"Socket error: {error}");
            DispatchToMainThread(() => OnError?.Invoke(error));
        }

        private void HandleSessionInfo(string json)
        {
            try
            {
                var sessionInfo = SessionInfo.FromJson(json);
                CurrentSession = sessionInfo;
                SetState(ConnectionState.Connected);
                Log($"Session established: {sessionInfo}");
                DispatchToMainThread(() => OnSessionConnected?.Invoke(sessionInfo));
            }
            catch (Exception e)
            {
                LogError($"Failed to parse session_info: {e.Message}");
            }
        }

        private void HandleBotResponse(string json)
        {
            try
            {
                var response = BotResponse.FromJson(json);
                Log($"Received bot_response: {response}");
                DispatchToMainThread(() => OnBotResponse?.Invoke(response));
            }
            catch (Exception e)
            {
                LogError($"Failed to parse bot_response: {e.Message}");
            }
        }

        private void HandleBotVoice(string json)
        {
            // Ignore empty bot_voice events (server may send these as signals)
            if (string.IsNullOrEmpty(json))
            {
                Log("Received empty bot_voice event, ignoring");
                return;
            }
            
            try
            {
                Log($"Parsing bot_voice JSON (length={json.Length}): {json.Substring(0, Math.Min(200, json.Length))}...");
                var voice = BotVoice.FromJson(json);
                Log($"Received bot_voice: {voice}, HasSubscribers={OnBotVoice != null}");
                DispatchToMainThread(() => OnBotVoice?.Invoke(voice));
            }
            catch (Exception e)
            {
                LogError($"Failed to parse bot_voice: {e.Message}");
            }
        }

        private void HandleSttResponse(string json)
        {
            try
            {
                var response = SttResponse.FromJson(json);
                Log($"Received stt_response: {response}");
                DispatchToMainThread(() => OnSttResponse?.Invoke(response));
            }
            catch (Exception e)
            {
                LogError($"Failed to parse stt_response: {e.Message}");
            }
        }

        private void HandleInterruptEvent(string json)
        {
            try
            {
                var data = string.IsNullOrEmpty(json) ? new InterruptData() : InterruptData.FromJson(json);
                Log($"Received interrupt: {data}");
                DispatchToMainThread(() => OnInterrupt?.Invoke(data));
            }
            catch (Exception e)
            {
                LogError($"Failed to parse interrupt: {e.Message}");
            }
        }
        
        private void HandleAuthError(string json)
        {
            try
            {
                // Parse error message
                var errorMsg = "Authentication failed";
                if (!string.IsNullOrEmpty(json))
                {
                    // Simple JSON parsing for {"error": "message"}
                    var errorStart = json.IndexOf("\"error\"");
                    if (errorStart >= 0)
                    {
                        var valueStart = json.IndexOf(':', errorStart) + 1;
                        var valueEnd = json.IndexOf('"', json.IndexOf('"', valueStart) + 1);
                        var msgStart = json.IndexOf('"', valueStart) + 1;
                        if (msgStart > 0 && valueEnd > msgStart)
                        {
                            errorMsg = json.Substring(msgStart, valueEnd - msgStart);
                        }
                    }
                }
                
                LogError($"Authentication error: {errorMsg}");
                SetState(ConnectionState.Error);
                DispatchToMainThread(() => OnError?.Invoke($"Authentication error: {errorMsg}"));
            }
            catch (Exception e)
            {
                LogError($"Failed to parse auth_error: {e.Message}");
                DispatchToMainThread(() => OnError?.Invoke("Authentication failed"));
            }
        }
        
        private void HandleServerError(string json)
        {
            try
            {
                var errorMsg = "Server error";
                if (!string.IsNullOrEmpty(json))
                {
                    // Simple JSON parsing for {"message": "..."}
                    var msgStart = json.IndexOf("\"message\"");
                    if (msgStart >= 0)
                    {
                        var valueStart = json.IndexOf(':', msgStart) + 1;
                        var valueEnd = json.IndexOf('"', json.IndexOf('"', valueStart) + 1);
                        var textStart = json.IndexOf('"', valueStart) + 1;
                        if (textStart > 0 && valueEnd > textStart)
                        {
                            errorMsg = json.Substring(textStart, valueEnd - textStart);
                        }
                    }
                }
                
                LogError($"Server error: {errorMsg}");
                DispatchToMainThread(() => OnError?.Invoke(errorMsg));
            }
            catch (Exception e)
            {
                LogError($"Failed to parse error: {e.Message}");
            }
        }

        private void HandleLiveKitToken(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                LogError("Received empty livekit_token response");
                return;
            }

            try
            {
                var tokenResponse = JsonUtility.FromJson<LiveKitTokenResponse>(json);
                Log($"Received LiveKit token for room: {tokenResponse.room}");
                DispatchToMainThread(() => OnLiveKitTokenReceived?.Invoke(tokenResponse));
            }
            catch (Exception e)
            {
                LogError($"Failed to parse livekit_token: {e.Message}");
                DispatchToMainThread(() => OnLiveKitError?.Invoke("Failed to parse LiveKit token response"));
            }
        }

        private void HandleLiveKitReady(string json)
        {
            try
            {
                var roomName = "";
                if (!string.IsNullOrEmpty(json))
                {
                    // Parse {"room": "room-name"}
                    var roomStart = json.IndexOf("\"room\"");
                    if (roomStart >= 0)
                    {
                        var valueStart = json.IndexOf(':', roomStart) + 1;
                        var firstQuote = json.IndexOf('"', valueStart);
                        var secondQuote = json.IndexOf('"', firstQuote + 1);
                        if (firstQuote >= 0 && secondQuote > firstQuote)
                        {
                            roomName = json.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
                        }
                    }
                }
                
                Log($"LiveKit room ready: {roomName}");
                DispatchToMainThread(() => OnLiveKitReady?.Invoke(roomName));
            }
            catch (Exception e)
            {
                LogError($"Failed to parse livekit_ready: {e.Message}");
            }
        }

        private void HandleLiveKitError(string json)
        {
            try
            {
                var errorMsg = "LiveKit error";
                if (!string.IsNullOrEmpty(json))
                {
                    // Parse {"error": "message"}
                    var errorStart = json.IndexOf("\"error\"");
                    if (errorStart >= 0)
                    {
                        var valueStart = json.IndexOf(':', errorStart) + 1;
                        var firstQuote = json.IndexOf('"', valueStart);
                        var secondQuote = json.IndexOf('"', firstQuote + 1);
                        if (firstQuote >= 0 && secondQuote > firstQuote)
                        {
                            errorMsg = json.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
                        }
                    }
                }
                
                LogError($"LiveKit error: {errorMsg}");
                DispatchToMainThread(() => OnLiveKitError?.Invoke(errorMsg));
            }
            catch (Exception e)
            {
                LogError($"Failed to parse livekit_error: {e.Message}");
            }
        }

        private void HandleVoiceStarted(string json)
        {
            Log("Voice mode started on backend");
            _isVoiceModeActive = true;
            DispatchToMainThread(() => OnVoiceStarted?.Invoke());
        }

        private void HandleVoiceStopped(string json)
        {
            Log("Voice mode stopped on backend");
            _isVoiceModeActive = false;
            DispatchToMainThread(() => OnVoiceStopped?.Invoke());
        }

        private void HandleVoiceError(string json)
        {
            try
            {
                var errorMsg = "Voice mode error";
                if (!string.IsNullOrEmpty(json))
                {
                    // Parse {"error": "message"}
                    var errorStart = json.IndexOf("\"error\"");
                    if (errorStart >= 0)
                    {
                        var valueStart = json.IndexOf(':', errorStart) + 1;
                        var firstQuote = json.IndexOf('"', valueStart);
                        var secondQuote = json.IndexOf('"', firstQuote + 1);
                        if (firstQuote >= 0 && secondQuote > firstQuote)
                        {
                            errorMsg = json.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
                        }
                    }
                }
                
                LogError($"Voice mode error: {errorMsg}");
                DispatchToMainThread(() => OnVoiceError?.Invoke(errorMsg));
            }
            catch (Exception e)
            {
                LogError($"Failed to parse voice_error: {e.Message}");
            }
        }

        private void HandleQuotaExceeded(string json)
        {
            try
            {
                if (string.IsNullOrEmpty(json))
                {
                    LogError("Received empty quota_exceeded response");
                    return;
                }

                var data = QuotaExceededData.FromJson(json);
                LogError($"Quota exceeded: {data}");
                
                // Quota exceeded typically means the connection will be terminated
                SetState(ConnectionState.Error);
                
                DispatchToMainThread(() => OnQuotaExceeded?.Invoke(data));
                DispatchToMainThread(() => OnError?.Invoke(data.Message));
            }
            catch (Exception e)
            {
                LogError($"Failed to parse quota_exceeded: {e.Message}");
                DispatchToMainThread(() => OnError?.Invoke("Quota exceeded"));
            }
        }

        private void HandleSceneGraphUpdate(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            try
            {
                var update = SceneGraphUpdate.FromJson(json);
                // Always log scene graph updates (important for debugging world model)
                Debug.Log($"[EstuaryClient] Received scene graph update: {update.SceneGraph?.EntityCount ?? 0} entities");
                DispatchToMainThread(() => OnSceneGraphUpdate?.Invoke(update));
            }
            catch (Exception e)
            {
                LogError($"Failed to parse scene_graph_update: {e.Message}");
            }
        }

        private void HandleRoomIdentified(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return;
            }

            try
            {
                var room = RoomIdentified.FromJson(json);
                Log($"Room identified: {room.RoomName} ({room.Status})");
                DispatchToMainThread(() => OnRoomIdentified?.Invoke(room));
            }
            catch (Exception e)
            {
                LogError($"Failed to parse room_identified: {e.Message}");
            }
        }

        private async Task HandleReconnect()
        {
            if (_disposed || _cancellationTokenSource?.IsCancellationRequested == true)
                return;

            if (_reconnectAttempts >= MAX_RECONNECT_ATTEMPTS)
            {
                LogError($"Max reconnect attempts ({MAX_RECONNECT_ATTEMPTS}) reached");
                SetState(ConnectionState.Error);
                return;
            }

            _reconnectAttempts++;
            SetState(ConnectionState.Reconnecting);
            Log($"Reconnecting... attempt {_reconnectAttempts}/{MAX_RECONNECT_ATTEMPTS}");

            await Task.Delay(RECONNECT_DELAY_MS * _reconnectAttempts);

            if (_disposed || _cancellationTokenSource?.IsCancellationRequested == true)
                return;

            await ConnectInternalAsync();
        }

        private void SetState(ConnectionState newState)
        {
            if (State != newState)
            {
                State = newState;
                DispatchToMainThread(() => OnConnectionStateChanged?.Invoke(newState));
            }
        }

        private void DispatchToMainThread(Action action)
        {
            _mainThreadQueue.Enqueue(action);
        }

        private void Log(string message)
        {
            if (DebugLogging)
            {
                Debug.Log($"[EstuaryClient] {message}");
            }
        }

        private void LogError(string message)
        {
            Debug.LogError($"[EstuaryClient] {message}");
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();

            if (_socket != null)
            {
                _socket.OnConnected -= HandleConnected;
                _socket.OnDisconnected -= HandleDisconnected;
                _socket.OnError -= HandleError;
                _socket.Dispose();
                _socket = null;
            }

            CurrentSession = null;
        }

        #endregion
    }

    #region Socket.IO Interface

    /// <summary>
    /// Interface for Socket.IO connection implementations.
    /// </summary>
    public interface ISocketIOConnection : IDisposable
    {
        event Action OnConnected;
        event Action<string> OnDisconnected;
        event Action<string> OnError;

        Task ConnectAsync(string url, string ns, object auth);
        Task DisconnectAsync();
        Task EmitAsync(string eventName, object data);
        void On(string eventName, Action<string> handler);
    }

    /// <summary>
    /// Built-in Socket.IO implementation using WebSocket.
    /// This is a basic implementation - for production, use socket.io-client-csharp.
    /// </summary>
    internal class BuiltInSocketIOConnection : ISocketIOConnection
    {
        public event Action OnConnected;
        public event Action<string> OnDisconnected;
        public event Action<string> OnError;

        private System.Net.WebSockets.ClientWebSocket _webSocket;
        private CancellationTokenSource _receiveCts;
        private readonly ConcurrentDictionary<string, Action<string>> _eventHandlers = new ConcurrentDictionary<string, Action<string>>();
        private bool _disposed;
        private string _namespace;
        private object _auth;  // Store auth for namespace connection

        public async Task ConnectAsync(string url, string ns, object auth)
        {
            _namespace = ns;
            _auth = auth;  // Store for use in namespace connect
            _webSocket = new System.Net.WebSockets.ClientWebSocket();
            _receiveCts = new CancellationTokenSource();

            // Build Socket.IO connection URL (auth goes in namespace connect, not URL)
            var uri = BuildSocketIOUri(url, ns, null);

            try
            {
                await _webSocket.ConnectAsync(uri, CancellationToken.None);
                // Don't invoke OnConnected yet - wait for namespace connection confirmation
                // OnConnected will be invoked in ProcessSocketIOMessage when we receive "40/namespace"

                // Start receive loop
                _ = ReceiveLoopAsync();
            }
            catch (Exception e)
            {
                OnError?.Invoke(e.Message);
                throw;
            }
        }

        public async Task DisconnectAsync()
        {
            if (_webSocket?.State == System.Net.WebSockets.WebSocketState.Open)
            {
                try
                {
                    await _webSocket.CloseAsync(
                        System.Net.WebSockets.WebSocketCloseStatus.NormalClosure,
                        "client disconnect",
                        CancellationToken.None);
                }
                catch { }
            }
            _receiveCts?.Cancel();
            OnDisconnected?.Invoke("client disconnect");
        }

        public async Task EmitAsync(string eventName, object data)
        {
            if (_webSocket?.State != System.Net.WebSockets.WebSocketState.Open)
                return;

            // Socket.IO message format: 42/namespace,["event",data]
            string json;
            if (data == null)
            {
                json = "null";
            }
            else
            {
                // JsonUtility.ToJson works with [Serializable] classes
                json = JsonUtility.ToJson(data);
            }
            
            var message = $"42{_namespace},[\"{eventName}\",{json}]";
            Debug.Log($"[SocketIO] Emitting: {message}");
            var bytes = System.Text.Encoding.UTF8.GetBytes(message);

            await _webSocket.SendAsync(
                new ArraySegment<byte>(bytes),
                System.Net.WebSockets.WebSocketMessageType.Text,
                true,
                CancellationToken.None);
        }

        public void On(string eventName, Action<string> handler)
        {
            _eventHandlers[eventName] = handler;
        }

        private Uri BuildSocketIOUri(string baseUrl, string ns, object auth)
        {
            // Convert HTTP(S) to WS(S)
            var wsUrl = baseUrl.Replace("https://", "wss://").Replace("http://", "ws://");
            
            // Add Socket.IO path and query params
            var authJson = auth != null ? JsonUtility.ToJson(auth) : "";
            var encodedAuth = Uri.EscapeDataString(authJson);
            
            return new Uri($"{wsUrl}/socket.io/?EIO=4&transport=websocket&auth={encodedAuth}");
        }

        private async Task ReceiveLoopAsync()
        {
            // Large buffer for audio messages (can be 100KB+ for a few seconds of audio)
            var buffer = new byte[16384];
            // StringBuilder to accumulate fragmented messages
            var messageBuilder = new System.Text.StringBuilder();

            try
            {
                while (!_disposed && _webSocket?.State == System.Net.WebSockets.WebSocketState.Open)
                {
                    var result = await _webSocket.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        _receiveCts.Token);

                    if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
                    {
                        OnDisconnected?.Invoke("server closed");
                        break;
                    }

                    if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Text)
                    {
                        // Append this chunk to the message builder
                        var chunk = System.Text.Encoding.UTF8.GetString(buffer, 0, result.Count);
                        messageBuilder.Append(chunk);

                        // Only process when we have the complete message
                        if (result.EndOfMessage)
                        {
                            var message = messageBuilder.ToString();
                            messageBuilder.Clear();
                            ProcessSocketIOMessage(message);
                        }
                        // If not EndOfMessage, continue accumulating chunks
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                OnError?.Invoke(e.Message);
                OnDisconnected?.Invoke(e.Message);
            }
        }

        private void ProcessSocketIOMessage(string message)
        {
            // Socket.IO message format:
            // 0 - open
            // 40 - connect (namespace)
            // 42["event",data] - event
            // 2 - ping
            // 3 - pong

            if (message.StartsWith("0"))
            {
                // Server accepted WebSocket connection, now connect to namespace WITH auth
                // Socket.IO v4 format: 40/namespace,{auth_json}
                string connectMsg;
                if (_auth != null)
                {
                    var authJson = JsonUtility.ToJson(_auth);
                    connectMsg = $"40{_namespace},{authJson}";
                }
                else
                {
                    connectMsg = $"40{_namespace},";
                }
                _ = SendRawAsync(connectMsg);
                Debug.Log($"[SocketIO] Sent namespace connect with auth: {connectMsg}");
            }
            else if (message.StartsWith($"40{_namespace}") || message.StartsWith("40,"))
            {
                // Connected to namespace - THIS is when we should send authentication
                Debug.Log($"[SocketIO] Namespace connected: {message}");
                OnConnected?.Invoke();
            }
            else if (message.StartsWith("44"))
            {
                // Connect error from server
                var errorStart = message.IndexOf('{');
                var error = errorStart >= 0 ? message.Substring(errorStart) : "Connection refused";
                Debug.LogError($"[SocketIO] Connection error: {error}");
                OnError?.Invoke($"Connection error: {error}");
            }
            else if (message.StartsWith("2"))
            {
                // Respond to ping with pong
                _ = SendRawAsync("3");
            }
            else if (message.StartsWith("42"))
            {
                // Event message
                ParseAndDispatchEvent(message);
            }
        }

        private void ParseAndDispatchEvent(string message)
        {
            try
            {
                // Remove Socket.IO prefix (42 or 42/namespace)
                var jsonStart = message.IndexOf('[');
                if (jsonStart < 0) return;

                var json = message.Substring(jsonStart);
                
                // Parse as array: ["eventName", data]
                // Simple parsing - in production use proper JSON parser
                var firstQuote = json.IndexOf('"');
                if (firstQuote < 0) return; // No opening quote found
                
                var eventNameStart = firstQuote + 1;
                var eventNameEnd = json.IndexOf('"', eventNameStart);
                if (eventNameEnd < 0) return; // No closing quote found
                
                var eventName = json.Substring(eventNameStart, eventNameEnd - eventNameStart);

                // Find data portion
                var dataStart = json.IndexOf(',', eventNameEnd);
                string dataJson = null;
                if (dataStart >= 0)
                {
                    var dataEnd = json.LastIndexOf(']');
                    if (dataEnd > dataStart) // Validate indices before substring
                    {
                        dataJson = json.Substring(dataStart + 1, dataEnd - dataStart - 1).Trim();
                    }
                }

                // Dispatch to handler
                Debug.Log($"[SocketIO] Received event '{eventName}' with data length {dataJson?.Length ?? 0}");
                
                // Extra debug logging for bot_voice events to help diagnose audio issues
                if (eventName == "bot_voice")
                {
                    Debug.Log($"[SocketIO] bot_voice event received! Data preview: {(dataJson?.Length > 200 ? dataJson.Substring(0, 200) + "..." : dataJson ?? "null")}");
                }
                
                if (_eventHandlers.TryGetValue(eventName, out var handler))
                {
                    handler(dataJson);
                }
                else
                {
                    Debug.LogWarning($"[SocketIO] No handler registered for event '{eventName}'");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to parse Socket.IO event: {e.Message}");
            }
        }

        private async Task SendRawAsync(string message)
        {
            if (_webSocket?.State != System.Net.WebSockets.WebSocketState.Open)
                return;

            var bytes = System.Text.Encoding.UTF8.GetBytes(message);
            await _webSocket.SendAsync(
                new ArraySegment<byte>(bytes),
                System.Net.WebSockets.WebSocketMessageType.Text,
                true,
                CancellationToken.None);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _receiveCts?.Cancel();
            _receiveCts?.Dispose();
            _webSocket?.Dispose();
        }
    }

    #endregion
}

