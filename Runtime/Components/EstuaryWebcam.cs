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

        [Header("Auto-Start")]
        [SerializeField]
        [Tooltip("Automatically start streaming when EstuaryManager connects")]
        private bool autoStartOnConnect = false;

        [Header("Debug")]
        [SerializeField]
        [Tooltip("Enable debug logging")]
        private bool debugLogging = true; // Default to true for easier debugging

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
                return LiveKitBridge.IsAvailable;
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
        private ILiveKitVideoManager _videoManager;
        private ILiveKitVoiceManager _voiceManager;

        #endregion

        #region Editor Domain Reload Guard

#if UNITY_EDITOR
        /// <summary>
        /// Check if Unity Editor is still transitioning into play mode.
        /// Returns true if we should wait before starting the webcam.
        /// </summary>
        private static bool IsEditorTransitioning()
        {
            // isPlayingOrWillChangePlaymode is true during transitions
            // isPlaying is true only when fully in play mode
            // If both are true but isCompiling is also true, we're still transitioning
            return UnityEditor.EditorApplication.isCompiling ||
                   (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode && 
                    !UnityEditor.EditorApplication.isPlaying);
        }
#endif

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            Debug.Log("[EstuaryWebcam] Awake()");
            
            // Pre-create frame texture for WebSocket mode
            _frameTexture = new Texture2D(targetWidth, targetHeight, TextureFormat.RGB24, false);
            
            // Create video manager via bridge (null if LiveKit SDK is not installed)
            _videoManager = LiveKitBridge.CreateVideoManager();
            if (_videoManager != null)
            {
                _videoManager.DebugLogging = debugLogging;
                _videoManager.SetCoroutineRunner(this);
            }
        }

        private void Start()
        {
            Debug.Log($"[EstuaryWebcam] Start() - autoStartOnConnect={autoStartOnConnect}");
            
            // If auto-start is enabled, subscribe to EstuaryManager connection events
            if (autoStartOnConnect)
            {
                StartCoroutine(WaitForManagerAndAutoStart());
            }
        }

        private System.Collections.IEnumerator WaitForManagerAndAutoStart()
        {
            Debug.Log("[EstuaryWebcam] Waiting for EstuaryManager...");
            
#if UNITY_EDITOR
            // Wait for Unity Editor to finish transitioning into play mode
            // This prevents webcam flickering during domain reload
            float transitionTimeout = 10f;
            float transitionElapsed = 0f;
            while (IsEditorTransitioning() && transitionElapsed < transitionTimeout)
            {
                yield return new WaitForSeconds(0.1f);
                transitionElapsed += 0.1f;
            }
            
            if (transitionElapsed > 0)
            {
                Debug.Log($"[EstuaryWebcam] Editor transition complete after {transitionElapsed:F1}s");
            }
            
            // Additional delay to let everything settle after play mode entry
            yield return new WaitForSeconds(0.5f);
#endif
            
            // Wait for EstuaryManager to exist
            float timeout = 30f;
            float elapsed = 0f;
            while (!EstuaryManager.HasInstance && elapsed < timeout)
            {
                yield return new WaitForSeconds(0.5f);
                elapsed += 0.5f;
            }
            
            if (!EstuaryManager.HasInstance)
            {
                Debug.LogWarning("[EstuaryWebcam] Timeout waiting for EstuaryManager");
                yield break;
            }
            
            Debug.Log("[EstuaryWebcam] EstuaryManager found, subscribing to LiveKit state events...");
            
            // Subscribe to LiveKit state change events (we need LiveKit room for video)
            EstuaryManager.Instance.OnLiveKitStateChanged += OnLiveKitStateChanged;
            
            // Check if already ready (in case we missed the event during startup)
            var currentState = GetLiveKitState();
            Debug.Log($"[EstuaryWebcam] Current LiveKit state: {currentState}");
            if (currentState == LiveKitConnectionState.Ready)
            {
                Debug.Log("[EstuaryWebcam] Already connected to LiveKit (Ready state), starting streaming...");
                OnLiveKitConnected();
            }
        }

        private LiveKitConnectionState GetLiveKitState()
        {
            if (!EstuaryManager.HasInstance)
                return LiveKitConnectionState.Disconnected;

            return EstuaryManager.Instance.LiveKitState;
        }

        private void OnLiveKitStateChanged(LiveKitConnectionState state)
        {
            Debug.Log($"[EstuaryWebcam] OnLiveKitStateChanged: {state}");
            
            // LiveKit is ready when state is Ready (not Connected - that enum value doesn't exist)
            if (state == LiveKitConnectionState.Ready)
            {
                Debug.Log("[EstuaryWebcam] LiveKit is Ready, triggering OnLiveKitConnected");
                OnLiveKitConnected();
            }
            else if (state == LiveKitConnectionState.Disconnected)
            {
                OnLiveKitDisconnected();
            }
        }

        private void OnLiveKitConnected()
        {
            Debug.Log("[EstuaryWebcam] OnLiveKitConnected - attempting to start streaming");
            
            if (IsStreaming)
            {
                Debug.Log("[EstuaryWebcam] Already streaming, skipping");
                return;
            }
            
            // Get session ID from manager
            string sessionId = GetSessionIdFromManager();
            
            if (!string.IsNullOrEmpty(sessionId))
            {
                Debug.Log($"[EstuaryWebcam] Starting streaming with sessionId={sessionId}");
                StartStreaming(sessionId);
            }
            else
            {
                Debug.LogWarning("[EstuaryWebcam] No session ID available from manager");
            }
        }

        private void OnLiveKitDisconnected()
        {
            Debug.Log("[EstuaryWebcam] OnLiveKitDisconnected - stopping streaming");
            StopStreaming();
        }

        private string GetSessionIdFromManager()
        {
            if (!EstuaryManager.HasInstance)
                return null;

            // Use public property instead of reflection
            var activeChar = EstuaryManager.Instance.ActiveCharacter;
            if (activeChar != null)
            {
                // Use character + player ID to create a unique session ID for webcam
                string sessionId = $"webcam_{activeChar.CharacterId}_{activeChar.PlayerId}";
                Debug.Log($"[EstuaryWebcam] Generated session ID from active character: {sessionId}");
                return sessionId;
            }

            // Fallback: generate a random session ID
            string fallbackId = $"webcam_{System.Guid.NewGuid():N}";
            Debug.Log($"[EstuaryWebcam] Generated fallback session ID: {fallbackId}");
            return fallbackId;
        }

        private void Update()
        {
            // Process video manager queue
            _videoManager?.ProcessMainThreadQueue();
        }

        private void OnDestroy()
        {
            Debug.Log("[EstuaryWebcam] OnDestroy()");
            
            // Unsubscribe from manager events - get instance once to avoid race condition
            // where HasInstance is true but Instance returns null during shutdown
            var manager = EstuaryManager.HasInstance ? EstuaryManager.Instance : null;
            if (manager != null)
            {
                manager.OnLiveKitStateChanged -= OnLiveKitStateChanged;
            }
            
            StopStreaming();
            
            if (_frameTexture != null)
            {
                Destroy(_frameTexture);
            }

            if (_videoManager != null)
            {
                _videoManager.Dispose();
                _videoManager = null;
            }
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            // Only pause webcam when focus is lost in actual builds, not in the Editor
            // This prevents the webcam from stopping when clicking on Console, Inspector, etc.
#if !UNITY_EDITOR
            if (!IsStreaming) return;
            
            var webcam = WebcamTexture;
            if (webcam == null) return;
            
            if (hasFocus)
            {
                // Resume webcam capture when focus is regained
                if (!webcam.isPlaying)
                {
                    Log("[EstuaryWebcam] Resuming webcam (focus regained)");
                    webcam.Play();
                }
            }
            else
            {
                // Pause webcam capture when focus is lost
                if (webcam.isPlaying)
                {
                    Log("[EstuaryWebcam] Pausing webcam (focus lost)");
                    webcam.Stop();
                }
            }
#endif
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Start webcam streaming to the world model.
        /// </summary>
        /// <param name="sessionId">World model session ID (use conversation session ID)</param>
        public void StartStreaming(string sessionId)
        {
            Debug.Log($"[EstuaryWebcam] StartStreaming() called with sessionId={sessionId}");
            
            if (IsStreaming)
            {
                Debug.Log("[EstuaryWebcam] Already streaming, returning");
                return;
            }

            if (string.IsNullOrEmpty(sessionId))
            {
                Debug.LogError("[EstuaryWebcam] Session ID is required");
                return;
            }

            SessionId = sessionId;

            // Get client reference
            Debug.Log($"[EstuaryWebcam] EstuaryManager.HasInstance={EstuaryManager.HasInstance}");
            if (EstuaryManager.HasInstance)
            {
                _client = GetClientFromManager();
                _voiceManager = GetVoiceManagerFromManager();
                Debug.Log($"[EstuaryWebcam] Got client={(_client != null)}, voiceManager={(_voiceManager != null)}");
            }

            if (_client == null)
            {
                Debug.LogError("[EstuaryWebcam] EstuaryClient not available");
                return;
            }

            // Register world model event handlers
            RegisterWorldModelHandlers();

            // Determine which mode to use
            ActiveStreamMode = DetermineStreamMode();
            Debug.Log($"[EstuaryWebcam] ActiveStreamMode={ActiveStreamMode}");
            
            // Start streaming based on active mode
            if (ActiveStreamMode == WebcamStreamMode.LiveKit)
            {
                Debug.Log("[EstuaryWebcam] Starting LiveKit streaming...");
                StartLiveKitStreaming();
            }
            else
            {
                Debug.Log("[EstuaryWebcam] Starting WebSocket streaming...");
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
            // Use effective session ID for backend communication (SDK session or local fallback)
            var sessionId = GetEffectiveSessionId();
            if (_client == null || string.IsNullOrEmpty(sessionId))
            {
                LogError("[EstuaryWebcam] Cannot subscribe: not connected or no session");
                return;
            }

            var payload = new SceneGraphSubscribePayload(sessionId);
            await EmitWorldModelEventAsync("scene_graph_subscribe", payload);
            _isSubscribedToSceneGraph = true;
            Log($"[EstuaryWebcam] Subscribed to scene graph updates (session: {sessionId})");
        }

        /// <summary>
        /// Unsubscribe from scene graph updates.
        /// </summary>
        public async Task UnsubscribeFromSceneGraphAsync()
        {
            // Capture local references to avoid race condition during shutdown
            var client = _client;
            var sessionId = GetEffectiveSessionId();
            
            // Check if client is still valid and connected (may be disposed during shutdown)
            if (client == null || !client.IsConnected || string.IsNullOrEmpty(sessionId))
                return;

            var payload = new SceneGraphSubscribePayload(sessionId);
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
            }

            return WebcamStreamMode.WebSocket;
        }

        #endregion

        #region Private Methods - LiveKit Streaming

        private async void StartLiveKitStreaming()
        {
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

                // Notify backend to subscribe to LiveKit video track
                await EnableLiveKitVideoOnBackendAsync();

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
        }

        private async Task StopLiveKitStreamingAsync()
        {
            if (_videoManager != null)
            {
                await _videoManager.StopPublishingAsync();
            }

            // Notify backend to stop listening to LiveKit video
            await DisableLiveKitVideoOnBackendAsync();
        }

        /// <summary>
        /// Notify backend to subscribe to LiveKit video track.
        /// </summary>
        private async Task EnableLiveKitVideoOnBackendAsync()
        {
            // Use effective session ID for backend communication (SDK session or local fallback)
            var sessionId = GetEffectiveSessionId();
            if (_client == null || string.IsNullOrEmpty(sessionId))
            {
                Log("[EstuaryWebcam] Cannot enable LiveKit video on backend: no client or session");
                return;
            }

            try
            {
                // Use proper payload class for reliable JSON serialization
                var payload = new EnableLiveKitVideoPayload(sessionId, targetFps);
                await EmitWorldModelEventAsync("enable_livekit_video", payload);
                Log($"[EstuaryWebcam] Enabled LiveKit video on backend (session: {sessionId}, fps: {targetFps})");
            }
            catch (Exception e)
            {
                LogError($"[EstuaryWebcam] Failed to enable LiveKit video on backend: {e.Message}");
            }
        }

        /// <summary>
        /// Notify backend to stop listening to LiveKit video.
        /// </summary>
        private async Task DisableLiveKitVideoOnBackendAsync()
        {
            // Capture local reference to avoid race condition during shutdown
            var client = _client;
            
            // Check if client is still valid and connected (may be disposed during shutdown)
            if (client == null || !client.IsConnected)
                return;

            try
            {
                await EmitWorldModelEventAsync("disable_livekit_video", new { });
                Log("[EstuaryWebcam] Disabled LiveKit video on backend");
            }
            catch (Exception e)
            {
                LogError($"[EstuaryWebcam] Failed to disable LiveKit video on backend: {e.Message}");
            }
        }

        private object GetLiveKitRoom()
        {
            // Get room from voice manager via interface method (no reflection needed)
            return _voiceManager?.GetRoom();
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

        private ILiveKitVoiceManager GetVoiceManagerFromManager()
        {
            return EstuaryManager.Instance?.LiveKitManager;
        }

        /// <summary>
        /// Gets the effective session ID for backend communication.
        /// Prefers the SDK session ID from the connected client, falls back to local SessionId.
        /// </summary>
        private string GetEffectiveSessionId()
        {
            // Prefer SDK session ID if available, otherwise fall back to local SessionId
            return _client?.CurrentSession?.SessionId ?? SessionId;
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
            // Capture local reference to avoid race condition during shutdown
            var client = _client;
            
            // Check if client is still valid and connected (may be disposed during shutdown)
            if (client == null || !client.IsConnected)
            {
                Log($"[EstuaryWebcam] Cannot emit {eventName}: client not available or disconnected");
                return;
            }

            try
            {
                await client.EmitAsync(eventName, payload);
            }
            catch (ObjectDisposedException)
            {
                // Client was disposed during await, this is expected during shutdown
                Log($"[EstuaryWebcam] Client disposed during {eventName} emit (shutdown)");
            }
            catch (NullReferenceException)
            {
                // Race condition where client state changed during await
                Log($"[EstuaryWebcam] Client became null during {eventName} emit (shutdown)");
            }
            catch (Exception e)
            {
                LogError($"[EstuaryWebcam] Error emitting {eventName}: {e.Message}");
            }
        }

        internal void HandleSceneGraphUpdate(SceneGraphUpdate update)
        {
            // Accept updates for effective session ID (SDK session or local fallback)
            var sessionId = GetEffectiveSessionId();
            if (update.SessionId != sessionId)
            {
                Log($"[EstuaryWebcam] Ignoring scene graph update for different session: {update.SessionId} (expected: {sessionId})");
                return;
            }

            CurrentSceneGraph = update.SceneGraph;
            Log($"[EstuaryWebcam] Scene graph updated: {CurrentSceneGraph?.EntityCount ?? 0} entities");

            OnSceneGraphUpdated?.Invoke(CurrentSceneGraph);
            onSceneGraphUpdated?.Invoke(CurrentSceneGraph);
        }

        internal void HandleRoomIdentified(RoomIdentified room)
        {
            // Accept updates for effective session ID (SDK session or local fallback)
            var sessionId = GetEffectiveSessionId();
            if (room.SessionId != sessionId)
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
