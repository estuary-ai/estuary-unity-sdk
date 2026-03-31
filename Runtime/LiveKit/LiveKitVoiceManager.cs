using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using UnityEngine;

using LiveKit;
using LiveKit.Proto;

namespace Estuary
{
    /// <summary>
    /// Manages LiveKit WebRTC connections for real-time voice chat.
    /// Uses native WebRTC microphone capture for proper AEC (Acoustic Echo Cancellation).
    /// Handles room connections, audio track publishing, and audio track subscription.
    /// Implements ILiveKitVoiceManager for use by core components via LiveKitBridge.
    /// </summary>
    public class LiveKitVoiceManager : ILiveKitVoiceManager
    {
        #region Events

        /// <summary>
        /// Fired when successfully connected to a LiveKit room.
        /// </summary>
        public event Action<string> OnConnected;

        /// <summary>
        /// Fired when disconnected from the LiveKit room.
        /// </summary>
        public event Action<string> OnDisconnected;

        /// <summary>
        /// Fired when an error occurs.
        /// </summary>
        public event Action<string> OnError;

        /// <summary>
        /// Fired when audio data is received from a remote participant (bot TTS).
        /// Parameters: PCM audio bytes, sample rate, channels
        /// Note: Currently unused as LiveKit handles audio playback automatically.
        /// Reserved for future custom audio processing.
        /// </summary>
#pragma warning disable CS0067 // Event is never used
        public event Action<byte[], int, int> OnAudioReceived;
#pragma warning restore CS0067

        /// <summary>
        /// Fired when bot audio playback should be muted/unmuted due to interrupt.
        /// </summary>
        public event Action<bool> OnBotAudioMuteRequested;

        /// <summary>
        /// Fired when a remote participant connects.
        /// </summary>
        public event Action<string> OnParticipantConnected;

        /// <summary>
        /// Fired when a remote participant disconnects.
        /// </summary>
        public event Action<string> OnParticipantDisconnected;

        /// <summary>
        /// Fired when microphone mute state changes.
        /// </summary>
        public event Action<bool> OnMuteStateChanged;

        /// <summary>
        /// Fired when the bot audio source is created at runtime.
        /// Useful for integrating lip sync or other audio-reactive features.
        /// </summary>
        public event Action<AudioSource> OnBotAudioSourceCreated;

        #endregion

        #region Properties

        /// <summary>
        /// Whether currently connected to a LiveKit room.
        /// </summary>
        public bool IsConnected { get; private set; }

        /// <summary>
        /// Current room name (null if not connected).
        /// </summary>
        public string CurrentRoomName { get; private set; }

        /// <summary>
        /// Whether microphone is currently publishing (unmuted).
        /// </summary>
        public bool IsPublishing { get; private set; }

        /// <summary>
        /// Whether microphone is currently muted.
        /// </summary>
        public bool IsMuted => !IsPublishing;

        /// <summary>
        /// Enable debug logging.
        /// </summary>
        public bool DebugLogging { get; set; }

        /// <summary>
        /// Output volume for bot audio (0.0 to 1.0). Default 1.0.
        /// Applied to the bot AudioSource. When bot audio is muted (interrupt),
        /// volume is set to 0; when unmuted, this value is restored.
        /// </summary>
        public float OutputVolume
        {
            get => _outputVolume;
            set
            {
                _outputVolume = Mathf.Clamp01(value);
                // Apply immediately if bot audio is not muted
                if (_botAudioSource != null && !_botAudioMuted)
                {
                    _botAudioSource.volume = _outputVolume;
                }
            }
        }

        #endregion

        #region Private Fields

        private Room _room;
        private LocalAudioTrack _localAudioTrack;
        private RtcAudioSource _microphoneSource;
        private IRemoteTrack _botAudioTrack;  // Track from bot for muting during interrupts
        private RemoteTrackPublication _botAudioPublication;  // Publication for enabling/disabling
        private bool _botAudioMuted;
        private AudioStream _botAudioStream;  // AudioStream for playing bot audio via LiveKit
        private GameObject _botAudioGameObject;  // GameObject holding the AudioSource for bot audio
        private AudioSource _botAudioSource;  // AudioSource for bot audio playback

        // Message tracking for interrupt filtering
        private string _currentMessageId;  // Current message being played
        private string _interruptedMessageId;  // Message that was interrupted (to filter out)
        private float _lastInterruptTimestamp;  // Timestamp of last interrupt (to filter stale audio)

        private float _outputVolume = 1f;
        private bool _disposed;
        private readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();
        private MonoBehaviour _coroutineRunner;
        private TaskCompletionSource<bool> _connectTcs;
        private TaskCompletionSource<bool> _publishTcs;

        #endregion

        #region Constructor

        public LiveKitVoiceManager()
        {
        }

        /// <summary>
        /// Set the MonoBehaviour to use for running coroutines.
        /// </summary>
        public void SetCoroutineRunner(MonoBehaviour runner)
        {
            _coroutineRunner = runner;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Pre-create the LiveKit Room instance and attach event handlers.
        /// Call this early (e.g., when session_info arrives) so that when the token
        /// arrives, ConnectAsync only needs to call Room.Connect() -- no allocation overhead.
        /// </summary>
        public void PrewarmRoom()
        {
            if (_disposed || _room != null) return;

            Log("Pre-warming LiveKit Room instance...");
            _room = new Room();
            SetupRoomEventHandlers();
            Log("Room pre-warmed and event handlers attached");
        }

        /// <summary>
        /// Connect to a LiveKit room using the provided token.
        /// </summary>
        /// <param name="url">LiveKit server URL</param>
        /// <param name="token">JWT access token</param>
        /// <param name="roomName">Room name (for reference)</param>
        public async Task<bool> ConnectAsync(string url, string token, string roomName)
        {
            if (_disposed)
            {
                LogError("Cannot connect: manager has been disposed");
                return false;
            }

            if (IsConnected)
            {
                Log("Already connected, disconnecting first...");
                await DisconnectAsync();
            }

            try
            {
                CurrentRoomName = roomName;
                Log($"Connecting to LiveKit room: {roomName} at {url}");

                // Use pre-warmed room if available, otherwise create new
                if (_room == null)
                {
                    _room = new Room();
                    SetupRoomEventHandlers();
                }

                // Create task completion source
                _connectTcs = new TaskCompletionSource<bool>();

                // Connect to room using coroutine
                if (_coroutineRunner != null)
                {
                    _coroutineRunner.StartCoroutine(ConnectCoroutine(url, token));
                }
                else
                {
                    LogError("No coroutine runner set");
                    return false;
                }

                // Wait for connection to complete
                return await _connectTcs.Task;
            }
            catch (Exception e)
            {
                LogError($"Failed to connect to LiveKit: {e.Message}");
                DispatchToMainThread(() => OnError?.Invoke(e.Message));
                return false;
            }
        }

        private IEnumerator ConnectCoroutine(string url, string token)
        {
            // Configure room options for optimized audio streaming
            var options = new LiveKit.RoomOptions();
            
            // Auto-subscribe to tracks immediately when they become available
            // This reduces latency when the bot starts speaking
            options.AutoSubscribe = true;
            
            // Configure adaptive streaming for better quality on varying network conditions
            // Dynacast allows the server to dynamically adjust quality based on subscribers
            options.Dynacast = true;
            
            Log($"Connecting with options: AutoSubscribe={options.AutoSubscribe}, Dynacast={options.Dynacast}");
            
            var connectInstruction = _room.Connect(url, token, options);
            yield return connectInstruction;

            if (connectInstruction.IsError)
            {
                LogError("Failed to connect to LiveKit room");
                _connectTcs?.TrySetResult(false);
                DispatchToMainThread(() => OnError?.Invoke("Failed to connect to LiveKit room"));
                yield break;
            }

            IsConnected = true;
            Log($"Connected to LiveKit room: {CurrentRoomName}");
            _connectTcs?.TrySetResult(true);
            DispatchToMainThread(() => OnConnected?.Invoke(CurrentRoomName));
        }

        /// <summary>
        /// Disconnect from the current LiveKit room.
        /// </summary>
        public async Task DisconnectAsync()
        {
            if (!IsConnected)
                return;

            try
            {
                Log("Disconnecting from LiveKit room...");

                // Stop publishing first
                await StopPublishingAsync();

                // Disconnect from room
                if (_room != null)
                {
                    _room.Disconnect();
                    _room = null;
                }

                IsConnected = false;
                var roomName = CurrentRoomName;
                CurrentRoomName = null;

                Log("Disconnected from LiveKit room");
                DispatchToMainThread(() => OnDisconnected?.Invoke("client disconnect"));
            }
            catch (Exception e)
            {
                LogError($"Error disconnecting from LiveKit: {e.Message}");
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Start publishing audio using native WebRTC microphone capture.
        /// This enables proper AEC (Acoustic Echo Cancellation) and auto gain control.
        /// </summary>
        public async Task<bool> StartPublishingAsync()
        {
            if (!IsConnected)
            {
                LogError("Cannot publish: not connected to a room");
                return false;
            }

            if (IsPublishing)
            {
                Log("Already publishing audio");
                return true;
            }

            try
            {
                Log("Starting native microphone capture with AEC enabled...");

                // Create task completion source
                _publishTcs = new TaskCompletionSource<bool>();

                // Start publishing using coroutine
                if (_coroutineRunner != null)
                {
                    _coroutineRunner.StartCoroutine(StartPublishingCoroutine());
                }
                else
                {
                    LogError("No coroutine runner set");
                    return false;
                }

                return await _publishTcs.Task;
            }
            catch (Exception e)
            {
                LogError($"Failed to start microphone: {e.Message}");
                DispatchToMainThread(() => OnError?.Invoke($"Failed to enable microphone: {e.Message}"));
                return false;
            }
        }

        private IEnumerator StartPublishingCoroutine()
        {
            // Get default microphone device
            string deviceName = null;
            if (Microphone.devices.Length > 0)
            {
                deviceName = Microphone.devices[0];
                Log($"Using microphone: {deviceName}");
            }
            else
            {
                LogError("No microphone devices found");
                _publishTcs?.TrySetResult(false);
                DispatchToMainThread(() => OnError?.Invoke("No microphone devices found"));
                yield break;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            // On Android, configure the audio system for voice communication mode
            // This enables hardware AEC (Acoustic Echo Cancellation) at the platform level
            //
            // NOTE: Using AndroidAudioMode.Normal by default for better audio streaming.
            // MODE_IN_COMMUNICATION can cause choppy audio on some devices (especially AR glasses)
            // as it changes audio buffering behavior.
            Log("Configuring Android audio for voice chat...");
            Log(AndroidAudioConfiguration.GetAudioCapabilitiesInfo());

            // Default to Normal mode for smoother audio streaming
            // VoiceCommunication mode can interfere with LiveKit's audio output on some devices
            if (AndroidAudioConfiguration.PreferredMode == AndroidAudioMode.VoiceCommunication)
            {
                Log("Using VoiceCommunication mode - if audio is choppy, try AndroidAudioMode.Normal");
            }

            if (!AndroidAudioConfiguration.ConfigureForVoiceChat())
            {
                LogError("Failed to configure Android audio - AEC may not be enabled");
            }
            else
            {
                Log($"Android AEC available: {AndroidAudioConfiguration.IsAecAvailable()}");
            }
#endif

#if UNITY_IOS && !UNITY_EDITOR
            Log("Using VPIOAudioSource (hardware AEC via VPIO audio unit)");
            _microphoneSource = new VPIOAudioSource(_coroutineRunner);
#else
            Log("Using DirectMicrophoneSource");
            _microphoneSource = new DirectMicrophoneSource(deviceName, _coroutineRunner);
#endif

            // Create local audio track from microphone source
            _localAudioTrack = LocalAudioTrack.CreateAudioTrack("microphone", _microphoneSource, _room);

            // Create publish options with voice-optimized audio encoding.
            // 32kbps is sufficient for mono speech (Opus codec), halving bandwidth vs 64kbps
            // with no perceptible quality loss for voice. DTX (Discontinuous Transmission)
            // reduces bandwidth during silence by not sending full frames.
            var options = new TrackPublishOptions();
            options.AudioEncoding = new AudioEncoding();
            options.AudioEncoding.MaxBitrate = 32000;
            options.Source = TrackSource.SourceMicrophone;
            options.Dtx = true;

            // Publish the track
            var publishInstruction = _room.LocalParticipant.PublishTrack(_localAudioTrack, options);
            yield return publishInstruction;

            if (publishInstruction.IsError)
            {
                LogError("Failed to publish audio track");
                _publishTcs?.TrySetResult(false);
                DispatchToMainThread(() => OnError?.Invoke("Failed to publish audio track"));
                yield break;
            }

            // Start capturing AFTER publish succeeds
            _microphoneSource.Start();

            IsPublishing = true;
#if UNITY_ANDROID && !UNITY_EDITOR
            Log("Microphone capture started (AEC enabled via Android platform audio processing)");
#elif UNITY_IOS && !UNITY_EDITOR
            Log("Microphone capture started (AEC enabled via iOS VPIO audio unit)");
#else
            Log("Microphone capture started (AEC enabled via WebRTC)");
#endif
            _publishTcs?.TrySetResult(true);
            DispatchToMainThread(() => OnMuteStateChanged?.Invoke(false));
        }

        /// <summary>
        /// Stop publishing audio (disable microphone).
        /// </summary>
        public async Task StopPublishingAsync()
        {
            if (!IsPublishing)
            {
                await Task.CompletedTask;
                return;
            }

            try
            {
                Log("Stopping microphone...");

                // Stop and dispose microphone source (handles cleanup internally)
                if (_microphoneSource != null)
                {
                    _microphoneSource.Stop();
                    _microphoneSource.Dispose();
                    _microphoneSource = null;
                }

                // Unpublish track
                if (_localAudioTrack != null && _room?.LocalParticipant != null)
                {
                    _room.LocalParticipant.UnpublishTrack(_localAudioTrack, true);
                    _localAudioTrack = null;
                }

#if UNITY_ANDROID && !UNITY_EDITOR
                // Reset Android audio configuration
                AndroidAudioConfiguration.ResetConfiguration();
#endif

                IsPublishing = false;
                Log("Microphone stopped");
                DispatchToMainThread(() => OnMuteStateChanged?.Invoke(true));
            }
            catch (Exception e)
            {
                LogError($"Error stopping microphone: {e.Message}");
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Mute the microphone (stops sending audio).
        /// </summary>
        public async Task MuteAsync()
        {
            if (!IsConnected || !IsPublishing)
            {
                await Task.CompletedTask;
                return;
            }

            try
            {
                // Mute by stopping the microphone source
                if (_microphoneSource != null)
                {
                    _microphoneSource.Stop();
                }
                IsPublishing = false;
                Log("Microphone muted");
                DispatchToMainThread(() => OnMuteStateChanged?.Invoke(true));
            }
            catch (Exception e)
            {
                LogError($"Error muting: {e.Message}");
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Unmute the microphone (resumes sending audio).
        /// </summary>
        public async Task UnmuteAsync()
        {
            if (!IsConnected)
            {
                await Task.CompletedTask;
                return;
            }

            try
            {
                // Unmute by restarting the microphone source
                if (_microphoneSource != null)
                {
                    _microphoneSource.Start();
                    IsPublishing = true;
                    Log("Microphone unmuted");
                    DispatchToMainThread(() => OnMuteStateChanged?.Invoke(false));
                }
                else
                {
                    // Audio source was destroyed, need to republish from scratch
                    await StartPublishingAsync();
                }
            }
            catch (Exception e)
            {
                LogError($"Error unmuting: {e.Message}");
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Toggle mute state.
        /// </summary>
        public async Task ToggleMuteAsync()
        {
            if (IsPublishing)
                await MuteAsync();
            else
                await UnmuteAsync();
        }

        /// <summary>
        /// Signal an interrupt to stop receiving bot audio.
        /// This notifies the server to stop streaming TTS audio and mutes bot audio locally.
        /// </summary>
        /// <param name="messageId">Optional message ID being interrupted</param>
        /// <param name="interruptedAt">Optional timestamp of the interrupt (if known from server)</param>
        public async Task SignalInterruptAsync(string messageId = null, float interruptedAt = 0f)
        {
            if (!IsConnected)
            {
                await Task.CompletedTask;
                return;
            }

            // Validate: Only process interrupt if there's something to interrupt
            var interruptedId = messageId ?? _currentMessageId;
            if (string.IsNullOrEmpty(interruptedId) && interruptedAt <= 0f)
            {
                Log("Ignoring interrupt with no message_id and no timestamp - nothing to interrupt");
                await Task.CompletedTask;
                return;
            }

            Log($"Signaling interrupt to server (messageId: {interruptedId ?? "none"}, timestamp: {interruptedAt})");

            // Mark the current message as interrupted so we can filter out remaining chunks
            if (!string.IsNullOrEmpty(interruptedId))
            {
                _interruptedMessageId = interruptedId;
                Log($"Marked message {interruptedId} as interrupted - will filter future chunks");
            }

            // Store interrupt timestamp for filtering stale audio
            if (interruptedAt > 0f)
            {
                _lastInterruptTimestamp = interruptedAt;
                Log($"Stored interrupt timestamp: {interruptedAt} - will discard audio older than this");
            }

            // Immediately mute bot audio to stop playback
            MuteBotAudio(true);

            // Notify server via EstuaryClient to stop sending audio
            if (EstuaryManager.HasInstance && EstuaryManager.Instance.IsConnected)
            {
                await EstuaryManager.Instance.NotifyInterruptAsync(interruptedId);
            }

            await Task.CompletedTask;
        }

        /// <summary>
        /// Mute or unmute the bot's audio track.
        /// Call with true when interrupting to immediately stop bot audio playback.
        /// Call with false when ready to receive new audio (e.g., when new response starts).
        /// </summary>
        /// <param name="muted">True to mute, false to unmute</param>
        public void MuteBotAudio(bool muted)
        {
            try
            {
                _botAudioMuted = muted;
                bool success = false;

                // Use volume-based muting to keep AudioStream pipeline intact
                // AudioStream relies on continuous OnAudioFilterRead callbacks - Stop() breaks it
                if (_botAudioSource != null)
                {
                    if (muted)
                    {
                        // Mute by setting volume to 0 - keeps AudioStream streaming
                        _botAudioSource.volume = 0f;
                        Log("Bot audio MUTED (volume=0) via known AudioSource");
                        success = true;
                    }
                    else
                    {
                        // Unmute by restoring to configured output volume
                        _botAudioSource.volume = _outputVolume;
                        Log($"Bot audio UNMUTED (volume={_outputVolume}) via known AudioSource");
                        success = true;
                    }
                }

                if (!success)
                {
                    Log($"No bot AudioSource available to {(muted ? "mute" : "unmute")}");
                }

                // Always fire the event so other components can respond
                DispatchToMainThread(() => OnBotAudioMuteRequested?.Invoke(muted));
            }
            catch (System.Exception e)
            {
                LogError($"Error {(muted ? "muting" : "unmuting")} bot audio: {e.Message}\n{e.StackTrace}");
            }
        }

        /// <summary>
        /// Check if bot audio is currently muted.
        /// </summary>
        public bool IsBotAudioMuted
        {
            get
            {
                return _botAudioMuted;
            }
        }

        /// <summary>
        /// Notify that a new audio chunk is starting for a specific message.
        /// Call this when receiving bot_voice events via Socket.IO to track message_id and filter stale audio.
        /// </summary>
        /// <param name="messageId">Message ID from bot_voice event</param>
        /// <param name="timestamp">Timestamp when this audio chunk was generated (optional)</param>
        /// <returns>True if this audio should be played, false if it should be discarded</returns>
        public bool NotifyAudioChunk(string messageId, float timestamp = 0f)
        {
            if (string.IsNullOrEmpty(messageId))
                return false;

            // CRITICAL: If we have an active interrupt, ONLY allow audio from a different message ID
            // This prevents playing remaining chunks from the interrupted message, even if their
            // timestamps are slightly after the interrupt (due to TTS generation timing)
            if (!string.IsNullOrEmpty(_interruptedMessageId) && messageId == _interruptedMessageId)
            {
                Log($"Discarding audio chunk from interrupted message {messageId} (timestamp: {timestamp})");
                return false;
            }

            // CRITICAL: Also discard any audio with timestamp <= interrupt, regardless of message ID
            // This catches any stale audio that might have been queued before the interrupt
            if (_lastInterruptTimestamp > 0f && timestamp > 0f && timestamp <= _lastInterruptTimestamp)
            {
                Log($"Discarding stale audio chunk {messageId} (timestamp: {timestamp} <= interrupt: {_lastInterruptTimestamp})");
                return false;
            }

            // Check if this is a new message
            if (_currentMessageId != messageId)
            {
                // New message starting - this should be fresh audio from a new response
                Log($"New message {messageId} starting (previous: {_currentMessageId})");

                // Clear interrupted flag if we're moving past the interrupted message
                if (_interruptedMessageId == _currentMessageId)
                {
                    Log($"Clearing interrupted message {_interruptedMessageId}");
                    _interruptedMessageId = null;
                }

                // Clear interrupt timestamp - we're starting a genuinely new response
                if (_lastInterruptTimestamp > 0f)
                {
                    Log($"Clearing interrupt timestamp {_lastInterruptTimestamp} for new message {messageId}");
                    _lastInterruptTimestamp = 0f;
                }

                _currentMessageId = messageId;
                Log($"Audio playback started for message {messageId}");

                // Unmute audio for new message (may have been muted during interrupt)
                if (_botAudioMuted)
                {
                    Log($"Unmuting bot audio for new message {messageId}");
                    MuteBotAudio(false);
                }
            }

            return true;  // Audio should be played
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

        /// <summary>
        /// Get the underlying LiveKit Room object for sharing with other managers (e.g., video).
        /// Returns null if not connected.
        /// </summary>
        public object GetRoom()
        {
            return _room;
        }

        #endregion

        #region Private Methods

        private void SetupRoomEventHandlers()
        {
            if (_room == null) return;

            _room.ParticipantConnected += (participant) =>
            {
                Log($"Participant connected: {participant.Identity}");
                DispatchToMainThread(() => OnParticipantConnected?.Invoke(participant.Identity));
            };

            _room.ParticipantDisconnected += (participant) =>
            {
                Log($"Participant disconnected: {participant.Identity}");
                DispatchToMainThread(() => OnParticipantDisconnected?.Invoke(participant.Identity));
            };

            _room.TrackSubscribed += (track, publication, participant) =>
            {
                Log($"Track subscribed from {participant.Identity}");
                // Store bot's audio track for muting during interrupts
                // Check if it's an audio track from the bot participant
                var isFromBot = participant.Identity != null && participant.Identity.StartsWith("bot-");
                
                if (track is RemoteAudioTrack audioTrack && isFromBot)
                {
                    _botAudioTrack = track;
                    _botAudioPublication = publication;
                    _botAudioMuted = false;
                    Log($"Bot audio track stored for interrupt handling");
                    
                    // Clean up any existing audio stream
                    CleanupBotAudioStream();
                    
                    // Create a new GameObject with AudioSource for bot audio playback
                    _botAudioGameObject = new GameObject($"LiveKit_BotAudio_{participant.Identity}");
                    _botAudioSource = _botAudioGameObject.AddComponent<AudioSource>();
                    _botAudioSource.spatialBlend = 0f;  // 2D audio (no positional audio for voice)
                    _botAudioSource.volume = _outputVolume;
                    _botAudioSource.priority = 0;  // Highest priority for voice
                    _botAudioSource.dopplerLevel = 0f;  // No doppler effect for voice
                    _botAudioSource.bypassEffects = true;  // Skip audio effects pipeline for lower latency
                    _botAudioSource.bypassListenerEffects = true;  // Skip listener effects
                    _botAudioSource.bypassReverbZones = true;  // Skip reverb zones
                    _botAudioSource.loop = false;  // Defensive: no looping
                    _botAudioSource.playOnAwake = false;  // Defensive: no auto-play
                    
                    // Create AudioStream to play the remote audio track
                    // This is required by LiveKit SDK - it does NOT auto-play remote audio
                    try
                    {
                        _botAudioStream = new AudioStream(audioTrack, _botAudioSource);
                        Log($"Bot audio stream created and playing via AudioSource");
                        
                        // Notify listeners that bot audio source is ready (for lip sync, etc.)
                        DispatchToMainThread(() => OnBotAudioSourceCreated?.Invoke(_botAudioSource));
                    }
                    catch (System.Exception ex)
                    {
                        LogError($"Failed to create AudioStream for bot audio: {ex.Message}");
                    }
                }
            };

            _room.TrackUnsubscribed += (track, publication, participant) =>
            {
                Log($"Track unsubscribed from {participant.Identity}");
                // Clear bot audio track reference when unsubscribed
                var isAudioTrack = track is RemoteAudioTrack;
                var isFromBot = participant.Identity != null && participant.Identity.StartsWith("bot-");
                
                if (isAudioTrack && isFromBot)
                {
                    // Clean up the audio stream and GameObject
                    CleanupBotAudioStream();
                    
                    _botAudioTrack = null;
                    _botAudioPublication = null;
                    _botAudioMuted = false;
                    
                    Log("Bot audio stream cleaned up");
                }
            };

            _room.Disconnected += (room) =>
            {
                Log("Room disconnected");
                IsConnected = false;
                CurrentRoomName = null;
                DispatchToMainThread(() => OnDisconnected?.Invoke("room disconnected"));
            };

            _room.ConnectionStateChanged += (state) =>
            {
                Log($"Connection state changed: {state}");
            };
        }

        /// <summary>
        /// Configure the AudioSource that LiveKit creates for the remote audio track.
        /// This can help reduce choppy audio by adjusting buffer settings.
        /// </summary>
        private IEnumerator ConfigureLiveKitAudioSourceCoroutine(string participantIdentity)
        {
            // Wait a few frames for LiveKit to create the AudioSource
            yield return null;
            yield return null;
            yield return null;

            // Find AudioSources that might be the LiveKit audio output
            var audioSources = UnityEngine.Object.FindObjectsOfType<AudioSource>();
            int configuredCount = 0;

            foreach (var source in audioSources)
            {
                var objName = source.gameObject.name.ToLower();
                
                // LiveKit typically creates GameObjects with names containing the participant identity or "audio"/"track"
                if (objName.Contains("bot") ||
                    objName.Contains(participantIdentity.ToLower()) ||
                    objName.Contains("remote") ||
                    objName.Contains("track"))
                {
                    // Configure for better streaming
                    // Priority: High priority for voice audio
                    source.priority = 0; // 0 = highest priority
                    
                    // Spatial blend: 2D audio (no positional audio for voice)
                    source.spatialBlend = 0f;
                    
                    // Doppler: Disable doppler effect for voice
                    source.dopplerLevel = 0f;
                    
                    // Volume rolloff: None (2D audio)
                    source.rolloffMode = AudioRolloffMode.Linear;
                    source.minDistance = 1f;
                    source.maxDistance = 500f;
                    
                    Log($"Configured LiveKit AudioSource: {source.gameObject.name} " +
                        $"(priority={source.priority}, spatialBlend={source.spatialBlend})");
                    configuredCount++;
                }
            }

            if (configuredCount == 0)
            {
                Log($"No LiveKit AudioSource found to configure (searched {audioSources.Length} sources)");
            }
            else
            {
                Log($"Configured {configuredCount} LiveKit AudioSource(s) for optimal voice playback");
            }
        }

        private void DispatchToMainThread(Action action)
        {
            _mainThreadQueue.Enqueue(action);
        }

        /// <summary>
        /// Immediately stops bot audio playback. Call before disconnect to prevent buzzing.
        /// </summary>
        public void StopBotAudio()
        {
            if (_botAudioSource != null)
                _botAudioSource.Stop();
        }

        /// <summary>
        /// Clean up the bot audio stream and associated GameObjects.
        /// </summary>
        private void CleanupBotAudioStream()
        {
            // Stop AudioSource FIRST to halt audio thread callbacks before
            // disposing the stream or destroying the GameObject
            if (_botAudioSource != null)
            {
                _botAudioSource.Stop();
            }

            if (_botAudioStream != null)
            {
                try
                {
                    _botAudioStream.Dispose();
                }
                catch (System.Exception ex)
                {
                    LogError($"Error disposing AudioStream: {ex.Message}");
                }
                _botAudioStream = null;
            }

            if (_botAudioGameObject != null)
            {
                UnityEngine.Object.Destroy(_botAudioGameObject);
                _botAudioGameObject = null;
            }

            _botAudioSource = null;
        }

        private void Log(string message)
        {
            if (DebugLogging)
            {
                Debug.Log($"[LiveKitVoiceManager] {message}");
            }
        }

        private void LogError(string message)
        {
            Debug.LogError($"[LiveKitVoiceManager] {message}");
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _ = DisconnectAsync();

            // Clean up bot audio stream
            CleanupBotAudioStream();

            // Stop and dispose microphone source (handles cleanup internally)
            if (_microphoneSource != null)
            {
                _microphoneSource.Stop();
                _microphoneSource.Dispose();
                _microphoneSource = null;
            }
            
            _localAudioTrack = null;
            _room = null;

#if UNITY_ANDROID && !UNITY_EDITOR
            // Reset Android audio configuration
            AndroidAudioConfiguration.ResetConfiguration();
#endif
        }

        #endregion
    }
}
