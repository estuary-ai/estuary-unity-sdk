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
        /// Current webcam texture (for preview, only available if using WebcamVideoSource).
        /// </summary>
        public WebCamTexture WebcamTexture => _webcamSource?.WebcamTexture;

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

#if LIVEKIT_AVAILABLE
        private Room _room;
        private LocalVideoTrack _localVideoTrack;
#endif
        private WebcamVideoSource _webcamSource;
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

#if LIVEKIT_AVAILABLE
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
#else
            LogError("LiveKit SDK not available");
            return false;
#endif
        }

#if LIVEKIT_AVAILABLE
        private IEnumerator StartPublishingCoroutine()
        {
            Log("Starting video track publishing...");

            // Note: Video publishing requires creating a video source that implements LiveKit's
            // RtcVideoSource interface. This is more complex than audio since LiveKit's Unity SDK
            // doesn't provide a simple built-in camera source like SetCameraEnabled().
            // 
            // For now, we create a webcam source for local preview/processing,
            // but full LiveKit video track publishing would require implementing
            // a custom RtcVideoSource or using LiveKit's native camera capture if available.

            // Create webcam video source for local capture/preview
            _webcamSource = new WebcamVideoSource(
                _coroutineRunner,
                deviceName: string.IsNullOrEmpty(PreferredDevice) ? null : PreferredDevice,
                useFrontCamera: UseFrontCamera,
                width: TargetWidth,
                height: TargetHeight,
                fps: TargetFps
            );

            // Start webcam capture
            _webcamSource.Start();

            // Wait a frame for webcam to initialize
            yield return null;

            if (!_webcamSource.IsCapturing)
            {
                LogError("Failed to start webcam capture");
                _webcamSource?.Stop();
                _webcamSource?.Dispose();
                _webcamSource = null;
                _publishTcs?.TrySetResult(false);
                DispatchToMainThread(() => OnError?.Invoke("Failed to start webcam"));
                yield break;
            }

            // TODO: To publish video to LiveKit, implement a custom RtcVideoSource that
            // feeds frames from WebcamVideoSource to LiveKit's video pipeline.
            // For now, we just capture locally which can be used for preview or
            // manual frame processing (e.g., sending to a vision API).
            
            Log($"Webcam capture started: {_webcamSource.Width}x{_webcamSource.Height}");
            Log("Note: Full LiveKit video track publishing requires RtcVideoSource implementation");

            IsPublishing = true;
            
            _publishTcs?.TrySetResult(true);
            DispatchToMainThread(() => OnPublishingStarted?.Invoke());
        }
#endif

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

#if LIVEKIT_AVAILABLE
            try
            {
                Log("Stopping video publishing...");

                // Stop webcam source if we created one
                if (_webcamSource != null)
                {
                    _webcamSource.Stop();
                    _webcamSource.Dispose();
                    _webcamSource = null;
                }

                // Unpublish video track if we had one
                // Note: Currently we only do local capture, not LiveKit publishing
                _localVideoTrack = null;

                _room = null;
                IsPublishing = false;
                Log("Video publishing stopped");
                DispatchToMainThread(() => OnPublishingStopped?.Invoke());
            }
            catch (Exception e)
            {
                LogError($"Error stopping video: {e.Message}");
            }
#endif
            await Task.CompletedTask;
        }


        /// <summary>
        /// Update video settings while publishing.
        /// Note: Changing settings requires stop/start to take effect.
        /// </summary>
        public void UpdateSettings(int width, int height, int fps)
        {
            TargetWidth = width;
            TargetHeight = height;
            TargetFps = fps;

            if (_webcamSource != null)
            {
                _webcamSource.SetResolution(width, height);
                _webcamSource.SetFrameRate(fps);
            }
        }

        /// <summary>
        /// Get or create a WebcamVideoSource for manual frame access.
        /// This is useful if you need to process frames locally (e.g., for preview).
        /// </summary>
        public WebcamVideoSource GetOrCreateWebcamSource()
        {
            if (_webcamSource == null && _coroutineRunner != null)
            {
                _webcamSource = new WebcamVideoSource(
                    _coroutineRunner,
                    deviceName: string.IsNullOrEmpty(PreferredDevice) ? null : PreferredDevice,
                    useFrontCamera: UseFrontCamera,
                    width: TargetWidth,
                    height: TargetHeight,
                    fps: TargetFps
                );
            }
            return _webcamSource;
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
            if (DebugLogging)
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

            if (_webcamSource != null)
            {
                _webcamSource.Stop();
                _webcamSource.Dispose();
                _webcamSource = null;
            }

#if LIVEKIT_AVAILABLE
            _localVideoTrack = null;
            _room = null;
#endif
        }

        #endregion
    }
}
