using System;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using Estuary.Utilities;

namespace Estuary
{
    /// <summary>
    /// Component for capturing microphone audio and streaming it to Estuary.
    /// 
    /// In LiveKit mode: Uses native WebRTC microphone capture with AEC (echo cancellation).
    /// The microphone is captured by LiveKit directly, this component controls mute/unmute.
    /// 
    /// In WebSocket mode: Captures audio via Unity's Microphone API and streams over Socket.IO.
    /// </summary>
    [AddComponentMenu("Estuary/Estuary Microphone")]
    public class EstuaryMicrophone : MonoBehaviour
    {
        #region Inspector Fields

        [Header("Target")]
        [SerializeField]
        [Tooltip("The EstuaryCharacter to send audio to")]
        private EstuaryCharacter targetCharacter;

        [Header("Recording Settings (WebSocket mode only)")]
        [SerializeField]
        [Tooltip("Sample rate for recording (WebSocket mode only, LiveKit uses native capture)")]
        private int sampleRate = 16000;

        [SerializeField]
        [Tooltip("Duration of each audio chunk in milliseconds (WebSocket mode only)")]
        private int chunkDurationMs = 100;

        [SerializeField]
        [Tooltip("Specific microphone device to use (empty = default, WebSocket mode only)")]
        private string microphoneDevice = "";

        [Header("Input Mode")]
        [SerializeField]
        [Tooltip("Key to hold for push-to-talk mode (None = always on when recording)")]
        private KeyCode pushToTalkKey = KeyCode.None;

        [SerializeField]
        [Tooltip("Enable voice activity detection (WebSocket mode only - LiveKit handles VAD server-side)")]
        private bool useVoiceActivityDetection = false;

        [SerializeField]
        [Tooltip("Volume threshold for voice activity detection (0-1, WebSocket mode only)")]
        [Range(0f, 1f)]
        private float vadThreshold = 0.5f;

        [Header("Events")]
        [SerializeField]
        private UnityEvent onRecordingStarted = new UnityEvent();

        [SerializeField]
        private UnityEvent onRecordingStopped = new UnityEvent();

        [SerializeField]
        private VolumeEvent onVolumeChanged = new VolumeEvent();

        [SerializeField]
        private UnityEvent onSpeechDetected = new UnityEvent();

        [SerializeField]
        private UnityEvent onSilenceDetected = new UnityEvent();

        #endregion

        #region Properties

        /// <summary>
        /// Whether the microphone is currently recording/active.
        /// In LiveKit mode, this means the microphone is unmuted.
        /// </summary>
        public bool IsRecording { get; private set; }

        /// <summary>
        /// Whether currently muted (LiveKit mode).
        /// </summary>
        public bool IsMuted => _useLiveKit ? !IsRecording : !IsRecording;

        /// <summary>
        /// Current audio volume level (0-1).
        /// Note: In LiveKit mode, volume monitoring is not available as audio goes directly through WebRTC.
        /// </summary>
        public float CurrentVolume { get; private set; }

        /// <summary>
        /// Whether speech is currently detected (WebSocket VAD mode only).
        /// In LiveKit mode, VAD is handled server-side by Deepgram.
        /// </summary>
        public bool IsSpeechDetected { get; private set; }

        /// <summary>
        /// Target character for audio streaming.
        /// </summary>
        public EstuaryCharacter TargetCharacter
        {
            get => targetCharacter;
            set => targetCharacter = value;
        }

        /// <summary>
        /// Sample rate for recording (WebSocket mode only).
        /// </summary>
        public int SampleRate => sampleRate;

        /// <summary>
        /// List of available microphone devices.
        /// </summary>
        public string[] AvailableDevices => Microphone.devices;

        /// <summary>
        /// Whether using LiveKit native capture (true) or WebSocket streaming (false).
        /// </summary>
        public bool IsLiveKitMode => _useLiveKit;

        #endregion

        #region C# Events

        /// <summary>
        /// Fired when recording starts (or microphone is unmuted in LiveKit mode).
        /// </summary>
        public event Action OnRecordingStarted;

        /// <summary>
        /// Fired when recording stops (or microphone is muted in LiveKit mode).
        /// </summary>
        public event Action OnRecordingStopped;

        /// <summary>
        /// Fired when volume level changes (WebSocket mode only).
        /// </summary>
        public event Action<float> OnVolumeChanged;

        /// <summary>
        /// Fired when speech is detected (WebSocket VAD mode only).
        /// </summary>
        public event Action OnSpeechDetected;

        /// <summary>
        /// Fired when silence is detected (WebSocket VAD mode only).
        /// </summary>
        public event Action OnSilenceDetected;

        #endregion

        #region Private Fields

        // WebSocket mode fields
        private AudioClip _recordingClip;
        private int _lastSamplePosition;
        private float[] _sampleBuffer;
        private Coroutine _recordingCoroutine;
        private bool _wasSpeaking;

        // LiveKit mode fields
        private LiveKitVoiceManager _liveKitManager;
        private bool _useLiveKit;

        // VAD-only fields for LiveKit mode (parallel Unity microphone capture)
        private AudioClip _vadRecordingClip;
        private float[] _vadBuffer;
        private int _vadLastPosition;
        private int _vadCoroutineSampleRate;
        private Coroutine _vadCoroutine;

        // Push-to-talk state
        private bool _pttWasPressed;

        // Constants
        private const int RECORDING_LENGTH_SECONDS = 10;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            ValidateSettings();
        }

        private void Update()
        {
            // Handle push-to-talk for LiveKit mode
            if (_useLiveKit && _liveKitManager != null && pushToTalkKey != KeyCode.None)
            {
                HandleLiveKitPushToTalk();
            }
        }

        private void OnDestroy()
        {
            StopRecording();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus && IsRecording)
            {
                StopRecording();
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Start recording/streaming from the microphone.
        /// In LiveKit mode: Enables native WebRTC microphone capture with AEC.
        /// In WebSocket mode: Starts Unity microphone capture and streams audio.
        /// </summary>
        public async void StartRecording()
        {
            if (IsRecording)
            {
                Debug.LogWarning("[EstuaryMicrophone] Already recording");
                return;
            }

            if (_useLiveKit)
            {
                await StartLiveKitRecording();
            }
            else
            {
                StartWebSocketRecording();
            }
        }

        /// <summary>
        /// Stop recording/streaming from the microphone.
        /// </summary>
        public async void StopRecording()
        {
            if (!IsRecording)
                return;

            if (_useLiveKit)
            {
                await StopLiveKitRecording();
            }
            else
            {
                StopWebSocketRecording();
            }
        }

        /// <summary>
        /// Toggle recording on/off.
        /// </summary>
        public void ToggleRecording()
        {
            if (IsRecording)
                StopRecording();
            else
                StartRecording();
        }

        /// <summary>
        /// Mute the microphone (LiveKit mode).
        /// </summary>
        public async void Mute()
        {
            if (_useLiveKit && _liveKitManager != null)
            {
                await _liveKitManager.MuteAsync();
                IsRecording = false;
                OnRecordingStopped?.Invoke();
                onRecordingStopped?.Invoke();
            }
            else
            {
                StopRecording();
            }
        }

        /// <summary>
        /// Unmute the microphone (LiveKit mode).
        /// </summary>
        public async void Unmute()
        {
            if (_useLiveKit && _liveKitManager != null)
            {
                await _liveKitManager.UnmuteAsync();
                IsRecording = _liveKitManager.IsPublishing;
                if (IsRecording)
                {
                    OnRecordingStarted?.Invoke();
                    onRecordingStarted?.Invoke();
                }
            }
            else
            {
                StartRecording();
            }
        }

        /// <summary>
        /// Set the microphone device to use (WebSocket mode only).
        /// In LiveKit mode, device selection is handled by the OS/WebRTC.
        /// </summary>
        /// <param name="deviceName">Device name, or empty/null for default</param>
        public void SetMicrophoneDevice(string deviceName)
        {
            if (_useLiveKit)
            {
                Debug.LogWarning("[EstuaryMicrophone] Device selection not available in LiveKit mode - WebRTC uses system default");
                return;
            }

            var wasRecording = IsRecording;
            if (wasRecording)
                StopRecording();

            microphoneDevice = deviceName;
            Debug.Log($"[EstuaryMicrophone] Set microphone device to {deviceName}");

            if (wasRecording)
                StartRecording();
        }

        /// <summary>
        /// Set the LiveKit voice manager for WebRTC audio streaming.
        /// When set, audio will be captured natively by WebRTC with AEC enabled.
        /// </summary>
        /// <param name="manager">The LiveKitVoiceManager to use</param>
        public void SetLiveKitManager(LiveKitVoiceManager manager)
        {
            _liveKitManager = manager;
            _useLiveKit = manager != null;
            
            if (_useLiveKit)
            {
                Debug.Log("[EstuaryMicrophone] LiveKit mode enabled - using native WebRTC capture with AEC");
                
                // Subscribe to mute state changes
                _liveKitManager.OnMuteStateChanged += OnLiveKitMuteStateChanged;
            }
            else
            {
                Debug.Log("[EstuaryMicrophone] WebSocket mode enabled - using Unity microphone capture");
            }
        }

        /// <summary>
        /// Configure the microphone for LiveKit or WebSocket mode.
        /// </summary>
        /// <param name="voiceMode">The voice mode to use</param>
        /// <param name="liveKitManager">LiveKit manager (required if voiceMode is LiveKit)</param>
        public void Configure(VoiceMode voiceMode, LiveKitVoiceManager liveKitManager = null)
        {
            _useLiveKit = voiceMode == VoiceMode.LiveKit && liveKitManager != null;
            _liveKitManager = liveKitManager;

            if (_useLiveKit)
            {
                Debug.Log("[EstuaryMicrophone] Configured for LiveKit mode (native WebRTC capture with AEC)");
                _liveKitManager.OnMuteStateChanged += OnLiveKitMuteStateChanged;
            }
            else
            {
                Debug.Log($"[EstuaryMicrophone] Configured for WebSocket mode at {sampleRate}Hz");
            }
        }

        #endregion

        #region Private Methods

        private void ValidateSettings()
        {
            if (targetCharacter == null)
            {
                targetCharacter = GetComponent<EstuaryCharacter>();

                if (targetCharacter == null)
                {
                    targetCharacter = GetComponentInParent<EstuaryCharacter>();
                }
            }

            // Only warn about sample rate in WebSocket mode
            if (!_useLiveKit && sampleRate != 16000)
            {
                Debug.LogWarning($"[EstuaryMicrophone] Non-standard sample rate {sampleRate}. WebSocket mode expects 16000Hz for STT.");
            }
        }

        private bool HasMicrophonePermission()
        {
            // On most platforms, Unity handles permissions automatically
            // For specific platforms, you may need to request permissions explicitly

#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
                return false;
            }
#endif

#if UNITY_IOS && !UNITY_EDITOR
            // iOS permissions are handled via Info.plist
            // Unity should auto-request on first Microphone.Start()
#endif

            return true;
        }

        #region LiveKit Mode

        private async Task StartLiveKitRecording()
        {
            if (_liveKitManager == null)
            {
                Debug.LogError("[EstuaryMicrophone] LiveKit manager not set");
                return;
            }

            if (!_liveKitManager.IsConnected)
            {
                Debug.LogError("[EstuaryMicrophone] LiveKit not connected");
                return;
            }

            Debug.Log("[EstuaryMicrophone] Starting LiveKit native microphone capture...");

            // Start native WebRTC microphone capture with AEC
            var success = await _liveKitManager.StartPublishingAsync();

            if (success)
            {
                IsRecording = true;
                Debug.Log("[EstuaryMicrophone] LiveKit microphone active (AEC enabled)");

                // Also start Unity microphone capture for local VAD (voice activity detection)
                // This runs in parallel with LiveKit's native capture, just for detecting user speech
                // The audio isn't sent anywhere - it's only used to detect interrupts
                StartUnityMicrophoneForVAD();

                // Fire events
                OnRecordingStarted?.Invoke();
                onRecordingStarted?.Invoke();
            }
            else
            {
                Debug.LogError("[EstuaryMicrophone] Failed to start LiveKit microphone");
            }
        }

        /// <summary>
        /// Start Unity's microphone capture just for VAD (voice activity detection) in LiveKit mode.
        /// This doesn't interfere with LiveKit's WebRTC capture - it runs in parallel.
        /// The captured audio is only used to detect when the user is speaking, enabling client-side interrupts.
        /// </summary>
        private void StartUnityMicrophoneForVAD()
        {
            if (!useVoiceActivityDetection)
            {
                Debug.Log("[EstuaryMicrophone] VAD disabled in LiveKit mode - enable useVoiceActivityDetection for client-side interrupts");
                return;
            }

            if (_vadRecordingClip != null)
            {
                Debug.Log("[EstuaryMicrophone] Unity microphone already running for VAD");
                return;
            }

            try
            {
                // Use a lower sample rate for VAD-only capture (reduces CPU usage)
                int vadSampleRate = 16000;
                
                // Get device - use empty string for default
                string device = string.IsNullOrEmpty(microphoneDevice) ? null : microphoneDevice;
                
                // Create a short clip for VAD analysis (1 second buffer)
                _vadRecordingClip = Microphone.Start(device, true, 1, vadSampleRate);
                
                if (_vadRecordingClip == null)
                {
                    Debug.LogWarning("[EstuaryMicrophone] Failed to start Unity microphone for VAD");
                    return;
                }

                // Wait for microphone to initialize
                int timeout = 0;
                while (Microphone.GetPosition(device) <= 0 && timeout < 100)
                {
                    timeout++;
                    System.Threading.Thread.Sleep(10);
                }

                if (Microphone.GetPosition(device) <= 0)
                {
                    Debug.LogWarning("[EstuaryMicrophone] Unity microphone didn't start in time for VAD");
                    Microphone.End(device);
                    _vadRecordingClip = null;
                    return;
                }

                // Initialize VAD state
                _vadCoroutineSampleRate = vadSampleRate;
                _vadBuffer = new float[vadSampleRate / 10]; // 100ms buffer for VAD
                _vadLastPosition = 0;
                
                // Start VAD processing coroutine
                _vadCoroutine = StartCoroutine(ProcessVADCoroutine());
                
                Debug.Log("[EstuaryMicrophone] Started Unity microphone for VAD (parallel with LiveKit)");
            }
            catch (Exception e)
            {
                Debug.LogError($"[EstuaryMicrophone] Error starting Unity microphone for VAD: {e.Message}");
            }
        }

        private IEnumerator ProcessVADCoroutine()
        {
            string device = string.IsNullOrEmpty(microphoneDevice) ? null : microphoneDevice;
            
            while (_vadRecordingClip != null && _useLiveKit && IsRecording)
            {
                int currentPos = Microphone.GetPosition(device);
                
                if (currentPos > 0 && currentPos != _vadLastPosition)
                {
                    int samplesToRead = currentPos - _vadLastPosition;
                    if (samplesToRead < 0) // Wrapped around
                    {
                        samplesToRead = _vadRecordingClip.samples - _vadLastPosition + currentPos;
                    }
                    
                    if (samplesToRead > _vadBuffer.Length)
                        samplesToRead = _vadBuffer.Length;
                    
                    // Read samples for VAD analysis
                    _vadRecordingClip.GetData(_vadBuffer, _vadLastPosition % _vadRecordingClip.samples);
                    _vadLastPosition = currentPos;
                    
                    // Calculate volume and check for speech
                    float volume = AudioConverter.CalculateRMS(_vadBuffer);
                    CurrentVolume = volume;
                    OnVolumeChanged?.Invoke(volume);
                    onVolumeChanged?.Invoke(volume);
                    
                    // Voice activity detection
                    bool isSpeaking = volume > vadThreshold;
                    
                    if (isSpeaking && !_wasSpeaking)
                    {
                        IsSpeechDetected = true;
                        OnSpeechDetected?.Invoke();
                        onSpeechDetected?.Invoke();
                        Debug.Log($"[EstuaryMicrophone] Speech detected in LiveKit mode (volume: {volume:F3})");
                    }
                    else if (!isSpeaking && _wasSpeaking)
                    {
                        IsSpeechDetected = false;
                        OnSilenceDetected?.Invoke();
                        onSilenceDetected?.Invoke();
                    }
                    
                    _wasSpeaking = isSpeaking;
                }
                
                yield return new WaitForSeconds(0.05f); // Check every 50ms
            }
        }

        private void StopUnityMicrophoneForVAD()
        {
            if (_vadCoroutine != null)
            {
                StopCoroutine(_vadCoroutine);
                _vadCoroutine = null;
            }
            
            if (_vadRecordingClip != null && _useLiveKit)
            {
                string device = string.IsNullOrEmpty(microphoneDevice) ? null : microphoneDevice;
                Microphone.End(device);
                _vadRecordingClip = null;
                Debug.Log("[EstuaryMicrophone] Stopped Unity microphone for VAD");
            }
        }

        private async Task StopLiveKitRecording()
        {
            if (_liveKitManager == null)
                return;

            Debug.Log("[EstuaryMicrophone] Stopping LiveKit microphone...");

            // Stop VAD microphone first
            StopUnityMicrophoneForVAD();

            await _liveKitManager.StopPublishingAsync();
            IsRecording = false;

            Debug.Log("[EstuaryMicrophone] LiveKit microphone stopped");

            // Fire events
            OnRecordingStopped?.Invoke();
            onRecordingStopped?.Invoke();
        }

        private void HandleLiveKitPushToTalk()
        {
            var isPressed = Input.GetKey(pushToTalkKey);

            if (isPressed && !_pttWasPressed)
            {
                // Key just pressed - unmute
                Unmute();
            }
            else if (!isPressed && _pttWasPressed)
            {
                // Key just released - mute
                Mute();
            }

            _pttWasPressed = isPressed;
        }

        private void OnLiveKitMuteStateChanged(bool isMuted)
        {
            IsRecording = !isMuted;

            if (isMuted)
            {
                OnRecordingStopped?.Invoke();
                onRecordingStopped?.Invoke();
            }
            else
            {
                OnRecordingStarted?.Invoke();
                onRecordingStarted?.Invoke();
            }
        }

        #endregion

        #region WebSocket Mode

        private void StartWebSocketRecording()
        {
            if (!HasMicrophonePermission())
            {
                Debug.LogError("[EstuaryMicrophone] Microphone permission not granted");
                return;
            }

            if (Microphone.devices.Length == 0)
            {
                Debug.LogError("[EstuaryMicrophone] No microphone devices found");
                return;
            }

            // Select microphone device
            var device = string.IsNullOrEmpty(microphoneDevice) ? null : microphoneDevice;

            // Calculate buffer size
            var samplesPerChunk = (sampleRate * chunkDurationMs) / 1000;
            _sampleBuffer = new float[samplesPerChunk];

            // Start recording
            _recordingClip = Microphone.Start(device, true, RECORDING_LENGTH_SECONDS, sampleRate);

            if (_recordingClip == null)
            {
                Debug.LogError("[EstuaryMicrophone] Failed to start microphone");
                return;
            }

            _lastSamplePosition = 0;
            IsRecording = true;

            // Start processing coroutine
            _recordingCoroutine = StartCoroutine(ProcessAudioCoroutine());

            Debug.Log($"[EstuaryMicrophone] Started WebSocket recording at {sampleRate}Hz");

            // Fire events
            OnRecordingStarted?.Invoke();
            onRecordingStarted?.Invoke();
        }

        private void StopWebSocketRecording()
        {
            IsRecording = false;

            // Stop coroutine
            if (_recordingCoroutine != null)
            {
                StopCoroutine(_recordingCoroutine);
                _recordingCoroutine = null;
            }

            // Stop microphone
            var device = string.IsNullOrEmpty(microphoneDevice) ? null : microphoneDevice;
            Microphone.End(device);

            // Cleanup
            if (_recordingClip != null)
            {
                Destroy(_recordingClip);
                _recordingClip = null;
            }

            Debug.Log("[EstuaryMicrophone] Stopped WebSocket recording");

            // Fire events
            OnRecordingStopped?.Invoke();
            onRecordingStopped?.Invoke();
        }

        private IEnumerator ProcessAudioCoroutine()
        {
            // Wait for microphone to start
            var device = string.IsNullOrEmpty(microphoneDevice) ? null : microphoneDevice;
            while (!(Microphone.GetPosition(device) > 0))
            {
                yield return null;
            }

            var samplesPerChunk = _sampleBuffer.Length;
            var waitTime = new WaitForSeconds(chunkDurationMs / 1000f);

            while (IsRecording)
            {
                yield return waitTime;

                if (!IsRecording)
                    break;

                // Get current position
                var currentPosition = Microphone.GetPosition(device);

                // Calculate samples available
                var samplesAvailable = currentPosition - _lastSamplePosition;
                if (samplesAvailable < 0)
                {
                    // Wrapped around
                    samplesAvailable += _recordingClip.samples;
                }

                // Process in chunks
                while (samplesAvailable >= samplesPerChunk)
                {
                    // Read samples
                    _recordingClip.GetData(_sampleBuffer, _lastSamplePosition);
                    _lastSamplePosition = (_lastSamplePosition + samplesPerChunk) % _recordingClip.samples;
                    samplesAvailable -= samplesPerChunk;

                    // Process the chunk
                    ProcessAudioChunk(_sampleBuffer);
                }
            }
        }

        private void ProcessAudioChunk(float[] samples)
        {
            // Calculate volume
            CurrentVolume = AudioConverter.CalculateRMS(samples);
            OnVolumeChanged?.Invoke(CurrentVolume);
            onVolumeChanged?.Invoke(CurrentVolume);

            // Voice activity detection (WebSocket mode only)
            if (useVoiceActivityDetection)
            {
                var isSpeaking = CurrentVolume > vadThreshold;

                if (isSpeaking && !_wasSpeaking)
                {
                    IsSpeechDetected = true;
                    OnSpeechDetected?.Invoke();
                    onSpeechDetected?.Invoke();
                }
                else if (!isSpeaking && _wasSpeaking)
                {
                    IsSpeechDetected = false;
                    OnSilenceDetected?.Invoke();
                    onSilenceDetected?.Invoke();
                }

                _wasSpeaking = isSpeaking;

                // Don't send audio if no speech detected
                if (!isSpeaking)
                    return;
            }

            // Check push-to-talk (WebSocket mode)
            if (pushToTalkKey != KeyCode.None && !Input.GetKey(pushToTalkKey))
            {
                return;
            }

            // Send audio via WebSocket
            SendAudioChunkViaWebSocket(samples);
        }

        private async void SendAudioChunkViaWebSocket(float[] samples)
        {
            if (targetCharacter == null || !targetCharacter.IsConnected)
                return;

            try
            {
                // Convert to Base64 PCM16
                var base64Audio = Base64Helper.EncodeAudio(samples);

                // Send to character
                await targetCharacter.StreamAudioAsync(base64Audio);
            }
            catch (Exception e)
            {
                Debug.LogError($"[EstuaryMicrophone] Error sending WebSocket audio: {e.Message}");
            }
        }

        #endregion

        #endregion

        #region Unity Event Types

        [Serializable]
        public class VolumeEvent : UnityEvent<float> { }

        #endregion
    }
}






