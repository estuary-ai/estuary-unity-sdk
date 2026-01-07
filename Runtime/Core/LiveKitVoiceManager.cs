using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using UnityEngine;

#if LIVEKIT_AVAILABLE
using LiveKit;
using LiveKit.Proto;
#endif

namespace Estuary
{
    /// <summary>
    /// Token response data from the server.
    /// </summary>
    [Serializable]
    public class LiveKitTokenResponse
    {
        public string token;
        public string url;
        public string room;
    }

    /// <summary>
    /// Manages LiveKit WebRTC connections for real-time voice chat.
    /// Uses native WebRTC microphone capture for proper AEC (Acoustic Echo Cancellation).
    /// Handles room connections, audio track publishing, and audio track subscription.
    /// </summary>
    public class LiveKitVoiceManager : IDisposable
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
        /// </summary>
        public event Action<byte[], int, int> OnAudioReceived;

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

        #endregion

        #region Private Fields

#if LIVEKIT_AVAILABLE
        private Room _room;
        private LocalAudioTrack _localAudioTrack;
        private DirectMicrophoneSource _microphoneSource;
#endif
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

#if LIVEKIT_AVAILABLE
            try
            {
                CurrentRoomName = roomName;
                Log($"Connecting to LiveKit room: {roomName} at {url}");

                // Create room instance
                _room = new Room();

                // Set up event handlers before connecting
                SetupRoomEventHandlers();

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
#else
            LogError("LiveKit SDK not available. Please ensure the LiveKit package is installed.");
            DispatchToMainThread(() => OnError?.Invoke("LiveKit SDK not available"));
            return false;
#endif
        }

#if LIVEKIT_AVAILABLE
        private IEnumerator ConnectCoroutine(string url, string token)
        {
            var options = new LiveKit.RoomOptions();
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
#endif

        /// <summary>
        /// Disconnect from the current LiveKit room.
        /// </summary>
        public async Task DisconnectAsync()
        {
            if (!IsConnected)
                return;

#if LIVEKIT_AVAILABLE
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
#endif
            await Task.CompletedTask;
        }

        /// <summary>
        /// Start publishing audio using native WebRTC microphone capture.
        /// This enables proper AEC (Acoustic Echo Cancellation), noise suppression, and auto gain control.
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

#if LIVEKIT_AVAILABLE
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
#else
            LogError("LiveKit SDK not available");
            return false;
#endif
        }

#if LIVEKIT_AVAILABLE
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

            // Create DirectMicrophoneSource - our custom implementation that bypasses Unity's audio DSP
            // This solves the sample rate mismatch issue on macOS when using built-in speakers
            // DirectMicrophoneSource polls the mic directly at 48kHz, regardless of output device settings
            _microphoneSource = new DirectMicrophoneSource(deviceName, _coroutineRunner);

            // Create local audio track from microphone source
            _localAudioTrack = LocalAudioTrack.CreateAudioTrack("microphone", _microphoneSource, _room);

            // Create publish options with proper audio encoding
            var options = new TrackPublishOptions();
            options.AudioEncoding = new AudioEncoding();
            options.AudioEncoding.MaxBitrate = 64000;
            options.Source = TrackSource.SourceMicrophone;

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
            Log("Microphone capture started (AEC enabled via direct polling at 48kHz)");
            _publishTcs?.TrySetResult(true);
            DispatchToMainThread(() => OnMuteStateChanged?.Invoke(false));
        }
#endif

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

#if LIVEKIT_AVAILABLE
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

                IsPublishing = false;
                Log("Microphone stopped");
                DispatchToMainThread(() => OnMuteStateChanged?.Invoke(true));
            }
            catch (Exception e)
            {
                LogError($"Error stopping microphone: {e.Message}");
            }
#endif
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

#if LIVEKIT_AVAILABLE
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
#endif
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

#if LIVEKIT_AVAILABLE
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
#endif
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

#if LIVEKIT_AVAILABLE
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
                Log($"Track subscribed: {track.Kind} from {participant.Identity}");
                // Audio from remote participants (bot) is automatically played by LiveKit
                // The SDK handles audio playback through Unity's audio system
            };

            _room.TrackUnsubscribed += (track, publication, participant) =>
            {
                Log($"Track unsubscribed: {track.Kind} from {participant.Identity}");
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
#endif

        private void DispatchToMainThread(Action action)
        {
            _mainThreadQueue.Enqueue(action);
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

#if LIVEKIT_AVAILABLE
            // Stop and dispose microphone source (handles cleanup internally)
            if (_microphoneSource != null)
            {
                _microphoneSource.Stop();
                _microphoneSource.Dispose();
                _microphoneSource = null;
            }
            
            _localAudioTrack = null;
            _room = null;
#endif
        }

        #endregion
    }
}
