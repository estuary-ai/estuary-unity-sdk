using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using Estuary.Models;

namespace Estuary
{
    /// <summary>
    /// Streaming mode for webcam video.
    /// </summary>
    public enum WebcamStreamMode
    {
        /// <summary>
        /// Stream video via LiveKit WebRTC video track (recommended).
        /// Lower latency, native video codec.
        /// Requires LiveKit SDK and desktop/mobile platform.
        /// </summary>
        LiveKit,

        /// <summary>
        /// Stream video frames over WebSocket as base64 JPEG.
        /// Works on all platforms including WebGL.
        /// Higher latency but universally compatible.
        /// </summary>
        WebSocket
    }

    /// <summary>
    /// Component for capturing webcam video and streaming it to the Estuary world model.
    /// LiveKit (WebRTC) is the preferred streaming method for lower latency.
    /// Falls back to WebSocket MJPEG when LiveKit is unavailable.
    /// </summary>
    [AddComponentMenu("Estuary/Estuary Webcam")]
    public class EstuaryWebcam : MonoBehaviour
    {
        #region Inspector Fields

        [Header("Streaming Settings")]
        [SerializeField]
        [Tooltip("Streaming mode: LiveKit (recommended) or WebSocket (fallback)")]
        private WebcamStreamMode streamMode = WebcamStreamMode.LiveKit;

        [SerializeField]
        [Tooltip("Automatically fall back to WebSocket if LiveKit unavailable")]
        private bool autoFallback = true;

        [SerializeField]
        [Tooltip("Target frames per second for world model (lower = less bandwidth)")]
        [Range(1, 30)]
        private int targetFps = 10;

        [SerializeField]
        [Tooltip("Target resolution width")]
        private int targetWidth = 1280;

        [SerializeField]
        [Tooltip("Target resolution height")]
        private int targetHeight = 720;

        [SerializeField]
        [Tooltip("JPEG quality for WebSocket mode (0-100)")]
        [Range(10, 100)]
        private int jpegQuality = 75;

        [Header("Camera Settings")]
        [SerializeField]
        [Tooltip("Preferred camera device name (empty = default)")]
        private string preferredDevice = "";

        [SerializeField]
        [Tooltip("Use front-facing camera if available")]
        private bool useFrontCamera = false;

        [Header("Pose Integration")]
        [SerializeField]
        [Tooltip("Camera transform to get pose from (optional, for AR)")]
        private Transform cameraTransform;

        [SerializeField]
        [Tooltip("Send camera pose with each frame")]
        private bool sendPose = false;

        [Header("Scene Graph")]
        [SerializeField]
        [Tooltip("Automatically subscribe to scene graph updates")]
        private bool autoSubscribeSceneGraph = true;

        [Header("Debug")]
        [SerializeField]
        [Tooltip("Enable debug logging")]
        private bool debugLogging = false;

        [Header("Events")]
        [SerializeField]
        private UnityEvent onStreamingStarted = new UnityEvent();

        [SerializeField]
        private UnityEvent onStreamingStopped = new UnityEvent();

        [SerializeField]
        private SceneGraphEvent onSceneGraphUpdated = new SceneGraphEvent();

        [SerializeField]
        private RoomIdentifiedEvent onRoomIdentified = new RoomIdentifiedEvent();

        [SerializeField]
        private ErrorEvent onError = new ErrorEvent();

        #endregion

        #region Properties

        /// <summary>
        /// Whether the webcam is currently streaming.
        /// </summary>
        public bool IsStreaming { get; private set; }

        /// <summary>
        /// Current webcam texture (can be used for preview).
        /// </summary>
        public WebCamTexture WebcamTexture
        {
            get
            {
                if (_videoManager != null && _videoManager.WebcamTexture != null)
                    return _videoManager.WebcamTexture;
                return _webcamTexture;
            }
        }

        /// <summary>
        /// World model session ID (set after starting).
        /// </summary>
        public string SessionId { get; private set; }

        /// <summary>
        /// Current streaming mode (may differ from configured if fallback occurred).
        /// </summary>
        public WebcamStreamMode ActiveStreamMode { get; private set; }

        /// <summary>
        /// Configured streaming mode.
        /// </summary>
        public WebcamStreamMode StreamMode
        {
            get => streamMode;
            set => streamMode = value;
        }

        /// <summary>
        /// Target FPS for streaming.
        /// </summary>
        public int TargetFps
        {
            get => targetFps;
            set => targetFps = Mathf.Clamp(value, 1, 30);
        }

        /// <summary>
        /// Current scene graph (null if not subscribed).
        /// </summary>
        public SceneGraph CurrentSceneGraph { get; private set; }

        /// <summary>
        /// Current identified room name.
        /// </summary>
        public string CurrentRoomName { get; private set; }

        /// <summary>
        /// List of available webcam devices.
        /// </summary>
        public WebCamDevice[] AvailableDevices => WebCamTexture.devices;

        /// <summary>
        /// Whether LiveKit is available for video streaming.
        /// </summary>
        public bool IsLiveKitAvailable
        {
            get
            {
#if LIVEKIT_AVAILABLE
                return true;
#else
                return false;
#endif
            }
        }

        #endregion

        #region C# Events

        /// <summary>
        /// Fired when streaming starts.
        /// </summary>
        public event Action OnStreamingStarted;

        /// <summary>
        /// Fired when streaming stops.
        /// </summary>
        public event Action OnStreamingStopped;

        /// <summary>
        /// Fired when scene graph is updated.
        /// </summary>
        public event Action<SceneGraph> OnSceneGraphUpdated;

        /// <summary>
        /// Fired when room is identified.
        /// </summary>
        public event Action<RoomIdentified> OnRoomIdentified;

        /// <summary>
        /// Fired when an error occurs.
        /// </summary>
        public event Action<string> OnError;

        #endregion

        #region Private Fields

        private WebCamTexture _webcamTexture;
        private Texture2D _frameTexture;
        private Coroutine _streamingCoroutine;
        private EstuaryClient _client;
        private bool _isSubscribedToSceneGraph;
        private float _lastFrameTime;

        // LiveKit video manager
        private LiveKitVideoManager _videoManager;
        private LiveKitVoiceManager _voiceManager;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            // Pre-create frame texture for WebSocket mode
            _frameTexture = new Texture2D(targetWidth, targetHeight, TextureFormat.RGB24, false);
            
            // Create video manager
            _videoManager = new LiveKitVideoManager();
            _videoManager.DebugLogging = debugLogging;
            _videoManager.SetCoroutineRunner(this);
            _videoManager.OnError += HandleVideoManagerError;
        }

        private void Update()
        {
            // Process video manager queue
            _videoManager?.ProcessMainThreadQueue();
        }

        private void OnDestroy()
        {
            StopStreaming();
            
            if (_frameTexture != null)
            {
                Destroy(_frameTexture);
            }

            if (_videoManager != null)
            {
                _videoManager.OnError -= HandleVideoManagerError;
                _videoManager.Dispose();
                _videoManager = null;
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Start webcam streaming to the world model.
        /// </summary>
        /// <param name="sessionId">World model session ID (use conversation session ID)</param>
        public void StartStreaming(string sessionId)
        {
            if (IsStreaming)
            {
                Log("[EstuaryWebcam] Already streaming");
                return;
            }

            if (string.IsNullOrEmpty(sessionId))
            {
                LogError("[EstuaryWebcam] Session ID is required");
                return;
            }

            SessionId = sessionId;

            // Get client reference
            if (EstuaryManager.HasInstance)
            {
                _client = GetClientFromManager();
                _voiceManager = GetVoiceManagerFromManager();
            }

            if (_client == null)
            {
                LogError("[EstuaryWebcam] EstuaryClient not available");
                return;
            }

            // Register world model event handlers
            RegisterWorldModelHandlers();

            // Determine which mode to use
            ActiveStreamMode = DetermineStreamMode();
            
            // Start streaming based on active mode
            if (ActiveStreamMode == WebcamStreamMode.LiveKit)
            {
                StartLiveKitStreaming();
            }
            else
            {
                StartWebSocketStreaming();
            }
        }

        /// <summary>
        /// Stop webcam streaming.
        /// </summary>
        public void StopStreaming()
        {
            if (!IsStreaming)
                return;

            IsStreaming = false;

            // Stop based on active mode
            if (ActiveStreamMode == WebcamStreamMode.LiveKit)
            {
                _ = StopLiveKitStreamingAsync();
            }
            else
            {
                StopWebSocketStreaming();
            }

            // Unsubscribe from scene graph
            if (_isSubscribedToSceneGraph)
            {
                _ = UnsubscribeFromSceneGraphAsync();
            }

            // Unregister handlers
            UnregisterWorldModelHandlers();

            Log("[EstuaryWebcam] Stopped streaming");

            OnStreamingStopped?.Invoke();
            onStreamingStopped?.Invoke();
        }

        /// <summary>
        /// Subscribe to scene graph updates.
        /// </summary>
        public async Task SubscribeToSceneGraphAsync()
        {
            if (_client == null || string.IsNullOrEmpty(SessionId))
            {
                LogError("[EstuaryWebcam] Cannot subscribe: not connected or no session");
                return;
            }

            var payload = new SceneGraphSubscribePayload(SessionId);
            await EmitWorldModelEventAsync("scene_graph_subscribe", payload);
            _isSubscribedToSceneGraph = true;
            Log("[EstuaryWebcam] Subscribed to scene graph updates");
        }

        /// <summary>
        /// Unsubscribe from scene graph updates.
        /// </summary>
        public async Task UnsubscribeFromSceneGraphAsync()
        {
            if (_client == null || string.IsNullOrEmpty(SessionId))
                return;

            var payload = new SceneGraphSubscribePayload(SessionId);
            await EmitWorldModelEventAsync("scene_graph_unsubscribe", payload);
            _isSubscribedToSceneGraph = false;
            Log("[EstuaryWebcam] Unsubscribed from scene graph updates");
        }

        /// <summary>
        /// Send current camera pose to the world model.
        /// </summary>
        public async Task SendPoseAsync(Matrix4x4 pose)
        {
            if (_client == null || string.IsNullOrEmpty(SessionId))
                return;

            var payload = new DevicePosePayload(SessionId, pose);
            await EmitWorldModelEventAsync("device_pose", payload);
        }

        /// <summary>
        /// Set the webcam device to use.
        /// </summary>
        public void SetDevice(string deviceName)
        {
            var wasStreaming = IsStreaming;
            var currentSessionId = SessionId;

            if (wasStreaming)
            {
                StopStreaming();
            }

            preferredDevice = deviceName;

            if (wasStreaming)
            {
                StartStreaming(currentSessionId);
            }
        }

        #endregion

        #region Private Methods - Mode Selection

        private WebcamStreamMode DetermineStreamMode()
        {
            // If LiveKit mode requested
            if (streamMode == WebcamStreamMode.LiveKit)
            {
#if LIVEKIT_AVAILABLE
                // Check if voice manager is connected (we share the room)
                if (_voiceManager != null && _voiceManager.IsConnected)
                {
                    Log("[EstuaryWebcam] Using LiveKit mode (voice manager connected)");
                    return WebcamStreamMode.LiveKit;
                }
                
                if (autoFallback)
                {
                    Log("[EstuaryWebcam] LiveKit room not connected, falling back to WebSocket mode");
                    return WebcamStreamMode.WebSocket;
                }
                else
                {
                    LogError("[EstuaryWebcam] LiveKit room not connected and autoFallback disabled");
                    return WebcamStreamMode.LiveKit; // Will fail on start
                }
#else
                if (autoFallback)
                {
                    Log("[EstuaryWebcam] LiveKit SDK not available, falling back to WebSocket mode");
                    return WebcamStreamMode.WebSocket;
                }
                else
                {
                    LogError("[EstuaryWebcam] LiveKit SDK not available and autoFallback disabled");
                    return WebcamStreamMode.LiveKit; // Will fail on start
                }
#endif
            }

            return WebcamStreamMode.WebSocket;
        }

        #endregion

        #region Private Methods - LiveKit Streaming

        private async void StartLiveKitStreaming()
        {
#if LIVEKIT_AVAILABLE
            try
            {
                Log($"[EstuaryWebcam] Starting LiveKit video streaming at {targetWidth}x{targetHeight} @ {targetFps} FPS");

                // Configure video manager
                _videoManager.TargetWidth = targetWidth;
                _videoManager.TargetHeight = targetHeight;
                _videoManager.TargetFps = targetFps;
                _videoManager.UseFrontCamera = useFrontCamera;
                _videoManager.PreferredDevice = preferredDevice;

                // Get room from voice manager
                object room = GetLiveKitRoom();
                
                if (room == null)
                {
                    if (autoFallback)
                    {
                        Log("[EstuaryWebcam] No LiveKit room available, falling back to WebSocket");
                        ActiveStreamMode = WebcamStreamMode.WebSocket;
                        StartWebSocketStreaming();
                        return;
                    }
                    else
                    {
                        LogError("[EstuaryWebcam] No LiveKit room available");
                        OnError?.Invoke("No LiveKit room available");
                        onError?.Invoke("No LiveKit room available");
                        return;
                    }
                }

                // Start publishing video
                bool success = await _videoManager.StartPublishingAsync(room);
                
                if (!success)
                {
                    if (autoFallback)
                    {
                        Log("[EstuaryWebcam] Failed to start LiveKit video, falling back to WebSocket");
                        ActiveStreamMode = WebcamStreamMode.WebSocket;
                        StartWebSocketStreaming();
                        return;
                    }
                    else
                    {
                        LogError("[EstuaryWebcam] Failed to start LiveKit video publishing");
                        OnError?.Invoke("Failed to start LiveKit video");
                        onError?.Invoke("Failed to start LiveKit video");
                        return;
                    }
                }

                IsStreaming = true;
                Log($"[EstuaryWebcam] LiveKit video streaming started");

                // Auto-subscribe to scene graph
                if (autoSubscribeSceneGraph)
                {
                    _ = SubscribeToSceneGraphAsync();
                }

                OnStreamingStarted?.Invoke();
                onStreamingStarted?.Invoke();
            }
            catch (Exception e)
            {
                LogError($"[EstuaryWebcam] LiveKit streaming error: {e.Message}");
                
                if (autoFallback)
                {
                    Log("[EstuaryWebcam] Falling back to WebSocket mode due to error");
                    ActiveStreamMode = WebcamStreamMode.WebSocket;
                    StartWebSocketStreaming();
                }
                else
                {
                    OnError?.Invoke($"LiveKit error: {e.Message}");
                    onError?.Invoke($"LiveKit error: {e.Message}");
                }
            }
#else
            LogError("[EstuaryWebcam] LiveKit SDK not available");
            if (autoFallback)
            {
                ActiveStreamMode = WebcamStreamMode.WebSocket;
                StartWebSocketStreaming();
            }
#endif
        }

        private async Task StopLiveKitStreamingAsync()
        {
            if (_videoManager != null)
            {
                await _videoManager.StopPublishingAsync();
            }
        }

        private object GetLiveKitRoom()
        {
#if LIVEKIT_AVAILABLE
            // Access the room from voice manager via reflection
            if (_voiceManager == null)
                return null;

            var vmType = typeof(LiveKitVoiceManager);
            var roomField = vmType.GetField("_room",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (roomField != null)
            {
                return roomField.GetValue(_voiceManager);
            }
#endif
            return null;
        }

        #endregion

        #region Private Methods - WebSocket Streaming

        private void StartWebSocketStreaming()
        {
            // Start webcam
            if (!StartWebcam())
            {
                return;
            }

            // Start streaming coroutine
            _streamingCoroutine = StartCoroutine(StreamWebSocketCoroutine());

            IsStreaming = true;
            Log($"[EstuaryWebcam] WebSocket streaming started at {targetFps} FPS ({targetWidth}x{targetHeight})");

            // Auto-subscribe to scene graph
            if (autoSubscribeSceneGraph)
            {
                _ = SubscribeToSceneGraphAsync();
            }

            OnStreamingStarted?.Invoke();
            onStreamingStarted?.Invoke();
        }

        private void StopWebSocketStreaming()
        {
            // Stop coroutine
            if (_streamingCoroutine != null)
            {
                StopCoroutine(_streamingCoroutine);
                _streamingCoroutine = null;
            }

            // Stop webcam
            StopWebcam();
        }

        private bool StartWebcam()
        {
            // Find device
            string deviceName = null;

            if (!string.IsNullOrEmpty(preferredDevice))
            {
                deviceName = preferredDevice;
            }
            else if (useFrontCamera)
            {
                foreach (var device in WebCamTexture.devices)
                {
                    if (device.isFrontFacing)
                    {
                        deviceName = device.name;
                        break;
                    }
                }
            }

            // Create webcam texture
            _webcamTexture = deviceName != null
                ? new WebCamTexture(deviceName, targetWidth, targetHeight, targetFps)
                : new WebCamTexture(targetWidth, targetHeight, targetFps);

            _webcamTexture.Play();

            if (!_webcamTexture.isPlaying)
            {
                LogError("[EstuaryWebcam] Failed to start webcam");
                OnError?.Invoke("Failed to start webcam");
                onError?.Invoke("Failed to start webcam");
                return false;
            }

            Log($"[EstuaryWebcam] Webcam started: {_webcamTexture.deviceName} ({_webcamTexture.width}x{_webcamTexture.height})");
            return true;
        }

        private void StopWebcam()
        {
            if (_webcamTexture != null)
            {
                _webcamTexture.Stop();
                Destroy(_webcamTexture);
                _webcamTexture = null;
            }
        }

        private IEnumerator StreamWebSocketCoroutine()
        {
            float frameInterval = 1f / targetFps;
            _lastFrameTime = Time.time;

            // Wait for webcam to initialize
            while (_webcamTexture != null && _webcamTexture.width < 100)
            {
                yield return null;
            }

            while (IsStreaming && _webcamTexture != null && _webcamTexture.isPlaying)
            {
                // Rate limit
                if (Time.time - _lastFrameTime < frameInterval)
                {
                    yield return null;
                    continue;
                }

                _lastFrameTime = Time.time;

                // Capture frame
                CaptureFrame();

                // Create payload
                Matrix4x4? pose = null;
                if (sendPose && cameraTransform != null)
                {
                    pose = cameraTransform.localToWorldMatrix;
                }

                var payload = VideoFramePayload.FromTexture(SessionId, _frameTexture, jpegQuality, pose);

                // Send via WebSocket
                _ = EmitWorldModelEventAsync("video_frame", payload);

                yield return null;
            }
        }

        private void CaptureFrame()
        {
            if (_webcamTexture == null || !_webcamTexture.isPlaying)
                return;

            // Resize frame texture if needed
            if (_frameTexture.width != _webcamTexture.width || _frameTexture.height != _webcamTexture.height)
            {
                _frameTexture.Reinitialize(_webcamTexture.width, _webcamTexture.height);
            }

            // Copy pixels
            _frameTexture.SetPixels(_webcamTexture.GetPixels());
            _frameTexture.Apply();
        }

        #endregion

        #region Private Methods - Events

        private EstuaryClient GetClientFromManager()
        {
            var managerType = typeof(EstuaryManager);
            var clientField = managerType.GetField("_client",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (clientField != null)
            {
                return clientField.GetValue(EstuaryManager.Instance) as EstuaryClient;
            }

            return null;
        }

        private LiveKitVoiceManager GetVoiceManagerFromManager()
        {
            var managerType = typeof(EstuaryManager);
            var vmField = managerType.GetField("_liveKitManager",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (vmField != null)
            {
                return vmField.GetValue(EstuaryManager.Instance) as LiveKitVoiceManager;
            }

            return null;
        }

        private void RegisterWorldModelHandlers()
        {
            if (_client == null)
                return;

            _client.OnSceneGraphUpdate += HandleSceneGraphUpdateInternal;
            _client.OnRoomIdentified += HandleRoomIdentifiedInternal;
            Log("[EstuaryWebcam] World model handlers registered");
        }

        private void UnregisterWorldModelHandlers()
        {
            if (_client == null)
                return;

            _client.OnSceneGraphUpdate -= HandleSceneGraphUpdateInternal;
            _client.OnRoomIdentified -= HandleRoomIdentifiedInternal;
            Log("[EstuaryWebcam] World model handlers unregistered");
        }

        private void HandleSceneGraphUpdateInternal(SceneGraphUpdate update)
        {
            HandleSceneGraphUpdate(update);
        }

        private void HandleRoomIdentifiedInternal(RoomIdentified room)
        {
            HandleRoomIdentified(room);
        }

        private void HandleVideoManagerError(string error)
        {
            LogError($"[EstuaryWebcam] Video manager error: {error}");
            OnError?.Invoke(error);
            onError?.Invoke(error);
        }

        private async Task EmitWorldModelEventAsync(string eventName, object payload)
        {
            if (_client == null)
            {
                Log($"[EstuaryWebcam] Cannot emit {eventName}: client not available");
                return;
            }

            try
            {
                await _client.EmitAsync(eventName, payload);
            }
            catch (Exception e)
            {
                LogError($"[EstuaryWebcam] Error emitting {eventName}: {e.Message}");
            }
        }

        internal void HandleSceneGraphUpdate(SceneGraphUpdate update)
        {
            if (update.SessionId != SessionId)
                return;

            CurrentSceneGraph = update.SceneGraph;
            Log($"[EstuaryWebcam] Scene graph updated: {CurrentSceneGraph?.EntityCount ?? 0} entities");

            OnSceneGraphUpdated?.Invoke(CurrentSceneGraph);
            onSceneGraphUpdated?.Invoke(CurrentSceneGraph);
        }

        internal void HandleRoomIdentified(RoomIdentified room)
        {
            if (room.SessionId != SessionId)
                return;

            CurrentRoomName = room.RoomName;
            Log($"[EstuaryWebcam] Room identified: {room.RoomName} ({room.Status})");

            OnRoomIdentified?.Invoke(room);
            onRoomIdentified?.Invoke(room);
        }

        #endregion

        #region Private Methods - Logging

        private void Log(string message)
        {
            if (debugLogging)
            {
                Debug.Log(message);
            }
        }

        private void LogError(string message)
        {
            Debug.LogError(message);
        }

        #endregion

        #region Unity Event Types

        [Serializable]
        public class SceneGraphEvent : UnityEvent<SceneGraph> { }

        [Serializable]
        public class RoomIdentifiedEvent : UnityEvent<RoomIdentified> { }

        [Serializable]
        public class ErrorEvent : UnityEvent<string> { }

        #endregion
    }
}
