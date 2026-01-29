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
    /// Manages LiveKit video track publishing for webcam streaming to the world model.
    /// Works alongside LiveKitVoiceManager, sharing the same room connection.
    /// 
    /// Uses LiveKit's built-in camera enablement for maximum compatibility.
    /// </summary>
    public class LiveKitVideoManager : IDisposable
    {
        #region Events

        /// <summary>
        /// Fired when video publishing starts.
        /// </summary>
        public event Action OnPublishingStarted;

        /// <summary>
        /// Fired when video publishing stops.
        /// </summary>
        public event Action OnPublishingStopped;

        /// <summary>
        /// Fired when an error occurs.
        /// </summary>
        public event Action<string> OnError;

        #endregion

        #region Properties

        /// <summary>
        /// Whether video is currently being published.
        /// </summary>
        public bool IsPublishing { get; private set; }

        /// <summary>
        /// Current webcam texture (for preview).
        /// </summary>
        public WebCamTexture WebcamTexture => _videoSource?.WebcamTexture;

        /// <summary>
        /// Target video width.
        /// </summary>
        public int TargetWidth { get; set; } = 1280;

        /// <summary>
        /// Target video height.
        /// </summary>
        public int TargetHeight { get; set; } = 720;

        /// <summary>
        /// Target frames per second.
        /// </summary>
        public int TargetFps { get; set; } = 10;

        /// <summary>
        /// Whether to use front-facing camera.
        /// </summary>
        public bool UseFrontCamera { get; set; } = false;

        /// <summary>
        /// Preferred webcam device name (empty for default).
        /// </summary>
        public string PreferredDevice { get; set; } = "";

        /// <summary>
        /// Enable debug logging.
        /// </summary>
        public bool DebugLogging { get; set; }

        #endregion

        #region Private Fields

        private Room _room;
        private LocalVideoTrack _localVideoTrack;
        private DirectWebcamVideoSource _videoSource;
        private bool _disposed;
        private readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();
        private MonoBehaviour _coroutineRunner;
        private TaskCompletionSource<bool> _publishTcs;

        #endregion

        #region Constructor

        public LiveKitVideoManager()
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
        /// Start publishing video to a LiveKit room.
        /// The room must already be connected (use LiveKitVoiceManager.ConnectAsync first).
        /// </summary>
        /// <param name="room">The connected LiveKit room</param>
        public async Task<bool> StartPublishingAsync(object room)
        {
            if (_disposed)
            {
                LogError("Cannot publish: manager has been disposed");
                return false;
            }

            if (IsPublishing)
            {
                Log("Already publishing video");
                return true;
            }

            _room = room as Room;
            if (_room == null)
            {
                LogError("Invalid room object - must be a LiveKit Room");
                return false;
            }

            try
            {
                Log($"Starting video publishing...");

                _publishTcs = new TaskCompletionSource<bool>();

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
                LogError($"Failed to start video: {e.Message}");
                DispatchToMainThread(() => OnError?.Invoke($"Failed to enable video: {e.Message}"));
                return false;
            }
        }

        private IEnumerator StartPublishingCoroutine()
        {
            Debug.Log("[LiveKitVideoManager] StartPublishingCoroutine() entered");
            Log("Starting video track publishing...");

            // Create DirectWebcamVideoSource which wraps TextureVideoSource for LiveKit publishing
            Debug.Log($"[LiveKitVideoManager] Creating DirectWebcamVideoSource: {TargetWidth}x{TargetHeight} @ {TargetFps}fps");
            _videoSource = new DirectWebcamVideoSource(
                _coroutineRunner,
                deviceName: string.IsNullOrEmpty(PreferredDevice) ? null : PreferredDevice,
                useFrontCamera: UseFrontCamera,
                width: TargetWidth,
                height: TargetHeight,
                fps: TargetFps
            );

            // Start capturing first so VideoSource is initialized
            Debug.Log("[LiveKitVideoManager] Calling _videoSource.Start()");
            _videoSource.Start();

            // Wait for webcam and VideoSource to initialize
            Debug.Log("[LiveKitVideoManager] Waiting for VideoSource to initialize...");
            float timeout = 3f;
            float elapsed = 0f;
            while ((_videoSource.VideoSource == null || !_videoSource.IsCapturing) && elapsed < timeout)
            {
                yield return new WaitForSeconds(0.1f);
                elapsed += 0.1f;
                if (elapsed % 1f < 0.15f) // Log every ~1 second
                {
                    Debug.Log($"[LiveKitVideoManager] Waiting... elapsed={elapsed:F1}s, VideoSource={(_videoSource.VideoSource != null ? "created" : "null")}, IsCapturing={_videoSource.IsCapturing}");
                }
            }

            Debug.Log($"[LiveKitVideoManager] Wait complete. VideoSource={(_videoSource.VideoSource != null ? "created" : "null")}, IsCapturing={_videoSource.IsCapturing}");

            if (_videoSource.VideoSource == null || !_videoSource.IsCapturing)
            {
                LogError($"Failed to start webcam capture - VideoSource={(_videoSource.VideoSource != null ? "created" : "null")}, IsCapturing={_videoSource.IsCapturing}");
                _videoSource?.Stop();
                _videoSource?.Dispose();
                _videoSource = null;
                _publishTcs?.TrySetResult(false);
                DispatchToMainThread(() => OnError?.Invoke("Failed to start webcam"));
                yield break;
            }

            // Create local video track from the TextureVideoSource
            Debug.Log("[LiveKitVideoManager] Creating LocalVideoTrack from TextureVideoSource");
            _localVideoTrack = LocalVideoTrack.CreateVideoTrack("camera", _videoSource.VideoSource, _room);

            // Configure video encoding options
            var options = new TrackPublishOptions();
            options.VideoEncoding = new VideoEncoding();
            options.VideoEncoding.MaxBitrate = 1_500_000; // 1.5 Mbps
            options.VideoEncoding.MaxFramerate = (uint)TargetFps;
            options.Source = TrackSource.SourceCamera;

            // Publish the video track to the room
            Log($"Publishing video track: {TargetWidth}x{TargetHeight} @ {TargetFps}fps");
            var publishInstruction = _room.LocalParticipant.PublishTrack(_localVideoTrack, options);
            yield return publishInstruction;

            if (publishInstruction.IsError)
            {
                LogError("Failed to publish video track");
                _videoSource?.Stop();
                _videoSource?.Dispose();
                _videoSource = null;
                _localVideoTrack = null;
                _publishTcs?.TrySetResult(false);
                DispatchToMainThread(() => OnError?.Invoke("Failed to publish video track"));
                yield break;
            }

            Log($"Video publishing started: {_videoSource.Width}x{_videoSource.Height} @ {TargetFps}fps");

            IsPublishing = true;
            
            _publishTcs?.TrySetResult(true);
            DispatchToMainThread(() => OnPublishingStarted?.Invoke());
        }

        /// <summary>
        /// Stop publishing video.
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
                Log("Stopping video publishing...");

                // Unpublish video track if we have one
                if (_localVideoTrack != null && _room?.LocalParticipant != null)
                {
                    _room.LocalParticipant.UnpublishTrack(_localVideoTrack, true);
                    _localVideoTrack = null;
                }

                // Stop video source
                if (_videoSource != null)
                {
                    _videoSource.Stop();
                    _videoSource.Dispose();
                    _videoSource = null;
                }

                _room = null;
                IsPublishing = false;
                Log("Video publishing stopped");
                DispatchToMainThread(() => OnPublishingStopped?.Invoke());
            }
            catch (Exception e)
            {
                LogError($"Error stopping video: {e.Message}");
            }

            await Task.CompletedTask;
        }


        /// <summary>
        /// Update video settings while publishing.
        /// Note: Changing resolution requires stop/start to take effect.
        /// </summary>
        public void UpdateSettings(int width, int height, int fps)
        {
            TargetWidth = width;
            TargetHeight = height;
            TargetFps = fps;

            if (_videoSource != null)
            {
                _videoSource.SetResolution(width, height);
                _videoSource.SetFrameRate(fps);
            }
        }

        /// <summary>
        /// Get the current video source.
        /// Returns null if not currently publishing.
        /// </summary>
        public DirectWebcamVideoSource GetVideoSource()
        {
            return _videoSource;
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

        private void DispatchToMainThread(Action action)
        {
            _mainThreadQueue.Enqueue(action);
        }

        private void Log(string message)
        {
            // Always log video manager messages for debugging
            // if (DebugLogging)
            {
                Debug.Log($"[LiveKitVideoManager] {message}");
            }
        }

        private void LogError(string message)
        {
            Debug.LogError($"[LiveKitVideoManager] {message}");
        }

        #endregion

        #region IDisposable

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _ = StopPublishingAsync();

            if (_videoSource != null)
            {
                _videoSource.Stop();
                _videoSource.Dispose();
                _videoSource = null;
            }

            _localVideoTrack = null;
            _room = null;
        }

        #endregion
    }
}
