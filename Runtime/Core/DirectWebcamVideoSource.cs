using System;
using System.Collections;
using UnityEngine;
using LiveKit;
using LiveKit.Proto;

namespace Estuary
{
    /// <summary>
    /// A webcam video source that captures frames from Unity's WebCamTexture and feeds them to LiveKit.
    /// 
    /// This class wraps LiveKit's TextureVideoSource to provide webcam video for publishing
    /// to a LiveKit room. It handles:
    /// - Webcam capture via Unity's WebCamTexture
    /// - Frame rate limiting to target FPS
    /// - Texture updates for LiveKit consumption
    /// - Front/back camera selection
    /// - Android camera permissions
    /// 
    /// Usage pattern:
    /// 1. Create DirectWebcamVideoSource
    /// 2. Call Start() to begin capturing
    /// 3. Use GetVideoSource() to get the TextureVideoSource for LiveKit track creation
    /// 4. Create LocalVideoTrack and publish
    /// </summary>
    public sealed class DirectWebcamVideoSource : IDisposable
    {
        private const int DEFAULT_WIDTH = 1280;
        private const int DEFAULT_HEIGHT = 720;
        private const int DEFAULT_FPS = 10;
        
        private readonly MonoBehaviour _coroutineRunner;
        private readonly string _deviceName;
        private readonly bool _useFrontCamera;
        
        private WebCamTexture _webcamTexture;
        private Texture2D _frameTexture;
        private TextureVideoSource _textureVideoSource;
        private Coroutine _captureCoroutine;
        private Coroutine _textureSourceUpdateCoroutine;
        private bool _started;
        private bool _disposed;
        
        // Guard against rapid start attempts
        private float _lastStartAttemptTime = 0f;
        private const float MIN_START_INTERVAL = 2f;
        
        private int _targetWidth;
        private int _targetHeight;
        private int _targetFps;
        private float _lastFrameTime;
        
        /// <summary>
        /// Whether the webcam is currently capturing.
        /// </summary>
        public bool IsCapturing => _started && _webcamTexture != null && _webcamTexture.isPlaying;
        
        /// <summary>
        /// Current webcam texture (can be used for preview).
        /// </summary>
        public WebCamTexture WebcamTexture => _webcamTexture;
        
        /// <summary>
        /// Current frame width.
        /// </summary>
        public int Width => _webcamTexture?.width ?? _targetWidth;
        
        /// <summary>
        /// Current frame height.
        /// </summary>
        public int Height => _webcamTexture?.height ?? _targetHeight;
        
        /// <summary>
        /// Target frames per second.
        /// </summary>
        public int TargetFps => _targetFps;
        
        /// <summary>
        /// The underlying TextureVideoSource for LiveKit track creation.
        /// </summary>
        public TextureVideoSource VideoSource => _textureVideoSource;
        
        /// <summary>
        /// Creates a new webcam video source for LiveKit publishing.
        /// </summary>
        /// <param name="coroutineRunner">MonoBehaviour to run capture coroutine on</param>
        /// <param name="deviceName">Webcam device name (null for default)</param>
        /// <param name="useFrontCamera">Prefer front-facing camera if no device specified</param>
        /// <param name="width">Target video width</param>
        /// <param name="height">Target video height</param>
        /// <param name="fps">Target frames per second</param>
        public DirectWebcamVideoSource(
            MonoBehaviour coroutineRunner,
            string deviceName = null,
            bool useFrontCamera = false,
            int width = DEFAULT_WIDTH,
            int height = DEFAULT_HEIGHT,
            int fps = DEFAULT_FPS)
        {
            _coroutineRunner = coroutineRunner;
            _deviceName = deviceName;
            _useFrontCamera = useFrontCamera;
            _targetWidth = width;
            _targetHeight = height;
            _targetFps = Mathf.Clamp(fps, 1, 30);
        }
        
        /// <summary>
        /// Set the target resolution. Requires restart to take effect.
        /// </summary>
        public void SetResolution(int width, int height)
        {
            _targetWidth = width;
            _targetHeight = height;
            
            if (_started)
            {
                // Restart to apply new resolution
                Stop();
                Start();
            }
        }
        
        /// <summary>
        /// Set the target frame rate.
        /// </summary>
        public void SetFrameRate(int fps)
        {
            _targetFps = Mathf.Clamp(fps, 1, 30);
        }
        
        /// <summary>
        /// Begins capturing video from the webcam.
        /// </summary>
        public void Start()
        {
            Debug.Log("[DirectWebcamVideoSource] Start() called");
            
            if (_started || _disposed)
            {
                Debug.Log($"[DirectWebcamVideoSource] Start() early exit - _started={_started}, _disposed={_disposed}");
                return;
            }
            
            // Guard against rapid start attempts to prevent flickering
            if (Time.time - _lastStartAttemptTime < MIN_START_INTERVAL && _lastStartAttemptTime > 0)
            {
                Debug.LogWarning($"[DirectWebcamVideoSource] Start() called too quickly (interval={Time.time - _lastStartAttemptTime:F2}s < {MIN_START_INTERVAL}s), ignoring");
                return;
            }
            _lastStartAttemptTime = Time.time;

#if UNITY_ANDROID && !UNITY_EDITOR
            // On Android, check and request camera permission first
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
            {
                Debug.Log("[DirectWebcamVideoSource] Requesting camera permission...");
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Camera);
                
                // Permission request is async, start a coroutine to wait for it
                if (_coroutineRunner != null)
                {
                    _coroutineRunner.StartCoroutine(WaitForPermissionAndStart());
                }
                else
                {
                    Debug.LogError("[DirectWebcamVideoSource] No coroutine runner to wait for permission");
                }
                return;
            }
#endif
            
            StartWebcamInternal();
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private IEnumerator WaitForPermissionAndStart()
        {
            // Wait for permission with timeout
            float timeout = 10f;
            float elapsed = 0f;
            
            while (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera) && elapsed < timeout)
            {
                yield return new WaitForSeconds(0.5f);
                elapsed += 0.5f;
            }
            
            if (UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Camera))
            {
                Debug.Log("[DirectWebcamVideoSource] Camera permission granted, starting capture");
                StartWebcamInternal();
            }
            else
            {
                Debug.LogError("[DirectWebcamVideoSource] Camera permission denied or timed out");
            }
        }
#endif

        private void StartWebcamInternal()
        {
            Debug.Log("[DirectWebcamVideoSource] StartWebcamInternal() called");
            
            if (_started || _disposed)
            {
                Debug.Log($"[DirectWebcamVideoSource] StartWebcamInternal early exit - _started={_started}, _disposed={_disposed}");
                return;
            }
            
            string deviceToUse = FindWebcamDevice();
            Debug.Log($"[DirectWebcamVideoSource] Available webcam devices: {WebCamTexture.devices.Length}");
            foreach (var device in WebCamTexture.devices)
            {
                Debug.Log($"[DirectWebcamVideoSource]   Device: {device.name} (front={device.isFrontFacing})");
            }
            
            if (string.IsNullOrEmpty(deviceToUse) && WebCamTexture.devices.Length == 0)
            {
                Debug.LogError("[DirectWebcamVideoSource] No webcam devices found");
                return;
            }
            
            Debug.Log($"[DirectWebcamVideoSource] Creating WebCamTexture with device={deviceToUse ?? "default"}, {_targetWidth}x{_targetHeight} @ {_targetFps}fps");
            _webcamTexture = deviceToUse != null
                ? new WebCamTexture(deviceToUse, _targetWidth, _targetHeight, _targetFps)
                : new WebCamTexture(_targetWidth, _targetHeight, _targetFps);
            
            Debug.Log("[DirectWebcamVideoSource] Calling WebCamTexture.Play()...");
            _webcamTexture.Play();
            
            if (!_webcamTexture.isPlaying)
            {
                Debug.LogError("[DirectWebcamVideoSource] Failed to start webcam - isPlaying=false after Play()");
                return;
            }
            
            Debug.Log($"[DirectWebcamVideoSource] SUCCESS! Webcam started: {_webcamTexture.deviceName} ({_webcamTexture.width}x{_webcamTexture.height} @ {_targetFps}fps)");
            
            _started = true;
            _lastFrameTime = Time.time;
            
            // Start capture coroutine to update the frame texture
            if (_coroutineRunner != null)
            {
                _captureCoroutine = _coroutineRunner.StartCoroutine(CaptureCoroutine());
            }
            else
            {
                Debug.LogError("[DirectWebcamVideoSource] No coroutine runner provided");
            }
        }
        
        private IEnumerator CaptureCoroutine()
        {
            Debug.Log("[DirectWebcamVideoSource] CaptureCoroutine started, waiting for webcam initialization...");
            
            // Wait for webcam to fully initialize (width > 100 indicates real frames)
            int initWaitFrames = 0;
            while (_webcamTexture != null && _webcamTexture.width < 100)
            {
                initWaitFrames++;
                if (initWaitFrames % 30 == 0) // Log every ~0.5 seconds at 60fps
                {
                    Debug.Log($"[DirectWebcamVideoSource] Waiting for webcam init... width={_webcamTexture?.width ?? 0}");
                }
                yield return null;
            }
            
            if (_webcamTexture == null || _disposed)
            {
                Debug.LogWarning("[DirectWebcamVideoSource] CaptureCoroutine exiting - webcam texture is null or disposed");
                yield break;
            }
            
            // Wait for resolution to stabilize (same size for multiple frames)
            // This prevents issues where webcam reports varying resolutions during startup
            Debug.Log($"[DirectWebcamVideoSource] Waiting for resolution to stabilize at {_webcamTexture.width}x{_webcamTexture.height}...");
            int stableWidth = _webcamTexture.width;
            int stableHeight = _webcamTexture.height;
            int stableFrameCount = 0;
            const int REQUIRED_STABLE_FRAMES = 10;
            
            while (stableFrameCount < REQUIRED_STABLE_FRAMES && !_disposed && _webcamTexture != null)
            {
                yield return null;
                
                if (_webcamTexture == null) break;
                
                if (_webcamTexture.width == stableWidth && _webcamTexture.height == stableHeight)
                {
                    stableFrameCount++;
                }
                else
                {
                    Debug.Log($"[DirectWebcamVideoSource] Resolution changed during stabilization: {stableWidth}x{stableHeight} -> {_webcamTexture.width}x{_webcamTexture.height}");
                    stableWidth = _webcamTexture.width;
                    stableHeight = _webcamTexture.height;
                    stableFrameCount = 0;
                }
            }
            
            if (_webcamTexture == null || _disposed)
            {
                Debug.LogWarning("[DirectWebcamVideoSource] CaptureCoroutine exiting - webcam texture is null or disposed after stabilization wait");
                yield break;
            }
            
            int width = _webcamTexture.width;
            int height = _webcamTexture.height;
            
            Debug.Log($"[DirectWebcamVideoSource] Resolution stabilized at {width}x{height}, creating textures...");
            
            // Create frame texture for LiveKit
            _frameTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            
            // Create TextureVideoSource for LiveKit
            _textureVideoSource = new TextureVideoSource(_frameTexture, VideoBufferType.Rgba);
            
            // Start the LiveKit video source so it begins reading from the texture
            // This is required by the LiveKit SDK for frames to be sent
            _textureVideoSource.Start();
            Debug.Log("[DirectWebcamVideoSource] TextureVideoSource started");
            
            // Start the LiveKit source's frame processing coroutine
            // This runs the LiveKit SDK's internal Update loop that sends frames
            if (_coroutineRunner != null)
            {
                _textureSourceUpdateCoroutine = _coroutineRunner.StartCoroutine(_textureVideoSource.Update());
                Debug.Log("[DirectWebcamVideoSource] TextureVideoSource Update coroutine started");
            }
            
            Debug.Log($"[DirectWebcamVideoSource] Capture loop started: {width}x{height} @ {_targetFps}fps");
            
            float frameInterval = 1f / _targetFps;
            
            // Main capture loop - no automatic restart to prevent flickering
            while (_started && !_disposed)
            {
                // Check if webcam stopped unexpectedly - exit cleanly instead of restarting
                // Automatic restarts cause flickering as they rapidly toggle the webcam LED
                if (_webcamTexture == null)
                {
                    Debug.LogError("[DirectWebcamVideoSource] Webcam texture is null, exiting capture loop");
                    _started = false;
                    break;
                }
                
                if (!_webcamTexture.isPlaying)
                {
                    Debug.LogError("[DirectWebcamVideoSource] Webcam stopped playing unexpectedly, exiting capture loop");
                    _started = false;
                    break;
                }
                
                // Rate limit to target FPS
                if (Time.time - _lastFrameTime >= frameInterval)
                {
                    _lastFrameTime = Time.time;
                    UpdateFrameTexture();
                }
                
                yield return null;
            }
            
            // Log why we exited
            Debug.Log($"[DirectWebcamVideoSource] Capture loop exited. _started={_started}, _disposed={_disposed}, webcamTexture={((_webcamTexture != null) ? "exists" : "null")}, isPlaying={_webcamTexture?.isPlaying}");
        }
        
        private int _resolutionMismatchLogCount = 0;
        private const int MAX_RESOLUTION_MISMATCH_LOGS = 5;
        
        private void UpdateFrameTexture()
        {
            if (_webcamTexture == null || !_webcamTexture.isPlaying || _frameTexture == null)
                return;
            
            int webcamWidth = _webcamTexture.width;
            int webcamHeight = _webcamTexture.height;
            int textureWidth = _frameTexture.width;
            int textureHeight = _frameTexture.height;
            
            // If resolution doesn't match, skip this frame
            // TextureVideoSource.Texture is read-only, so we can't update it after creation
            // The stability wait should prevent this from happening often
            if (textureWidth != webcamWidth || textureHeight != webcamHeight)
            {
                // Log only a few times to avoid spam
                if (_resolutionMismatchLogCount < MAX_RESOLUTION_MISMATCH_LOGS)
                {
                    _resolutionMismatchLogCount++;
                    Debug.LogWarning($"[DirectWebcamVideoSource] Resolution mismatch - webcam:{webcamWidth}x{webcamHeight} vs texture:{textureWidth}x{textureHeight}. Skipping frame. ({_resolutionMismatchLogCount}/{MAX_RESOLUTION_MISMATCH_LOGS} warnings)");
                }
                return;
            }
            
            // Reset mismatch counter when resolution matches again
            _resolutionMismatchLogCount = 0;
            
            // Copy webcam pixels to frame texture (with vertical flip)
            var pixels = _webcamTexture.GetPixels32();
            var flipped = new Color32[pixels.Length];
            
            for (int y = 0; y < textureHeight; y++)
            {
                int srcRow = y * textureWidth;
                int dstRow = (textureHeight - 1 - y) * textureWidth;
                for (int x = 0; x < textureWidth; x++)
                {
                    flipped[dstRow + x] = pixels[srcRow + x];
                }
            }
            
            _frameTexture.SetPixels32(flipped);
            _frameTexture.Apply();
        }
        
        /// <summary>
        /// Stops capturing video from the webcam.
        /// </summary>
        public void Stop()
        {
            if (!_started) return;
            
            Debug.Log("[DirectWebcamVideoSource] Stop() called");
            
            // Stop the LiveKit TextureVideoSource update coroutine first
            if (_textureSourceUpdateCoroutine != null && _coroutineRunner != null)
            {
                _coroutineRunner.StopCoroutine(_textureSourceUpdateCoroutine);
                _textureSourceUpdateCoroutine = null;
            }
            
            // Stop the LiveKit TextureVideoSource
            if (_textureVideoSource != null)
            {
                _textureVideoSource.Stop();
                Debug.Log("[DirectWebcamVideoSource] TextureVideoSource stopped");
            }
            
            // Stop our capture coroutine
            if (_captureCoroutine != null && _coroutineRunner != null)
            {
                _coroutineRunner.StopCoroutine(_captureCoroutine);
                _captureCoroutine = null;
            }
            
            // Stop and destroy the webcam texture
            if (_webcamTexture != null)
            {
                _webcamTexture.Stop();
                UnityEngine.Object.Destroy(_webcamTexture);
                _webcamTexture = null;
            }
            
            _started = false;
            
            Debug.Log("[DirectWebcamVideoSource] Stopped");
        }
        
        private string FindWebcamDevice()
        {
            if (!string.IsNullOrEmpty(_deviceName))
            {
                return _deviceName;
            }
            
            if (_useFrontCamera)
            {
                foreach (var device in WebCamTexture.devices)
                {
                    if (device.isFrontFacing)
                    {
                        return device.name;
                    }
                }
            }
            
            return null;
        }
        
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            
            Stop();
            
            if (_textureVideoSource != null)
            {
                _textureVideoSource.Dispose();
                _textureVideoSource = null;
            }
            
            if (_frameTexture != null)
            {
                UnityEngine.Object.Destroy(_frameTexture);
                _frameTexture = null;
            }
        }
    }
}
