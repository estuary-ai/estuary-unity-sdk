using System;
using System.Collections;
using UnityEngine;

#if LIVEKIT_AVAILABLE
using LiveKit;
#endif

namespace Estuary
{
    /// <summary>
    /// A microphone audio source that bypasses Unity's audio output DSP to avoid sample rate mismatches.
    /// 
    /// This solves the issue where LiveKit's MicrophoneSource fails on macOS when using built-in speakers
    /// because Unity's OnAudioFilterRead reports the OUTPUT device sample rate (e.g., 44100 Hz) instead
    /// of the microphone's sample rate (48000 Hz).
    /// 
    /// By polling the microphone's AudioClip directly with GetData(), we capture audio at the exact
    /// sample rate we started recording at, regardless of the output device configuration.
    /// 
    /// This class still uses LiveKit's RtcAudioSource base class, which means:
    /// - AEC (Acoustic Echo Cancellation) is still enabled via native WebRTC
    /// - AGC (Auto Gain Control) is still enabled
    /// </summary>
#if LIVEKIT_AVAILABLE
    public sealed class DirectMicrophoneSource : RtcAudioSource
    {
        private const int SAMPLE_RATE = 48000;
        private const int CHANNELS = 2; // Stereo for LiveKit
        private const float POLL_INTERVAL_MS = 20f; // Poll every 20ms for low latency
        
        private readonly string _deviceName;
        private readonly MonoBehaviour _coroutineRunner;
        
        private AudioClip _micClip;
        private int _lastReadPosition;
        private float[] _readBuffer;
        private bool _started;
        private bool _disposed;
        private Coroutine _pollCoroutine;
        
        // VAD (Voice Activity Detection) settings
        private bool _vadEnabled = true;
        private float _vadThreshold = 0.015f; // RMS threshold - tuned for near-field AR glasses
        private float _currentVolume;
        private bool _wasSpeaking;
        
        public override event Action<float[], int, int> AudioRead;
        
        /// <summary>
        /// Fired when speech is detected (volume crosses above threshold).
        /// </summary>
        public event Action OnSpeechDetected;
        
        /// <summary>
        /// Fired when silence is detected (volume drops below threshold).
        /// </summary>
        public event Action OnSilenceDetected;
        
        /// <summary>
        /// Enable/disable voice activity detection.
        /// When enabled, only audio above the threshold is sent to LiveKit.
        /// Audio below threshold is replaced with silence to filter ambient conversations.
        /// </summary>
        public bool VadEnabled 
        { 
            get => _vadEnabled; 
            set => _vadEnabled = value; 
        }
        
        /// <summary>
        /// Volume threshold for VAD (0-1). Audio below this RMS level is treated as silence.
        /// Default: 0.015 (1.5%). Increase to filter more aggressively.
        /// Recommended values:
        /// - 0.005-0.01: Very sensitive, picks up quiet speech
        /// - 0.01-0.02: Good balance for AR glasses near-field
        /// - 0.02-0.05: Aggressive filtering, requires speaking clearly
        /// </summary>
        public float VadThreshold 
        { 
            get => _vadThreshold; 
            set => _vadThreshold = Mathf.Clamp01(value); 
        }
        
        /// <summary>
        /// Current audio volume level (0-1). Useful for debugging/UI visualization.
        /// </summary>
        public float CurrentVolume => _currentVolume;
        
        /// <summary>
        /// Creates a new direct microphone source.
        /// </summary>
        /// <param name="deviceName">Microphone device name (null for default)</param>
        /// <param name="coroutineRunner">MonoBehaviour to run the polling coroutine on</param>
        public DirectMicrophoneSource(string deviceName, MonoBehaviour coroutineRunner) 
            : base(CHANNELS, RtcAudioSourceType.AudioSourceMicrophone)
        {
            _deviceName = deviceName;
            _coroutineRunner = coroutineRunner;
            
            // Allocate buffer for ~20ms of audio at 48kHz stereo
            int samplesPerPoll = (int)(SAMPLE_RATE * (POLL_INTERVAL_MS / 1000f));
            _readBuffer = new float[samplesPerPoll * CHANNELS * 2]; // Extra space for variable timing
        }
        
        /// <summary>
        /// Begins capturing audio from the microphone.
        /// </summary>
        public override void Start()
        {
            if (_started || _disposed) return;
            
            base.Start();

#if UNITY_ANDROID && !UNITY_EDITOR
            // On Android, check and request microphone permission first
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                Debug.Log("[DirectMicrophoneSource] Requesting microphone permission...");
                UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
                
                // Permission request is async, start a coroutine to wait for it
                if (_coroutineRunner != null)
                {
                    _coroutineRunner.StartCoroutine(WaitForPermissionAndStart());
                }
                else
                {
                    Debug.LogError("[DirectMicrophoneSource] No coroutine runner to wait for permission");
                }
                return;
            }
#else
            if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
            {
                Debug.LogError("[DirectMicrophoneSource] Microphone access not authorized");
                return;
            }
#endif
            
            StartMicrophoneInternal();
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private IEnumerator WaitForPermissionAndStart()
        {
            // Wait for permission with timeout
            float timeout = 10f;
            float elapsed = 0f;
            
            while (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone) && elapsed < timeout)
            {
                yield return new WaitForSeconds(0.5f);
                elapsed += 0.5f;
            }
            
            if (UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                Debug.Log("[DirectMicrophoneSource] Microphone permission granted, starting capture");
                StartMicrophoneInternal();
            }
            else
            {
                Debug.LogError("[DirectMicrophoneSource] Microphone permission denied or timed out");
            }
        }
#endif

        private void StartMicrophoneInternal()
        {
            if (_started || _disposed) return;
            
            // Start Unity microphone at exactly 48000 Hz
            _micClip = Microphone.Start(_deviceName, loop: true, lengthSec: 1, frequency: SAMPLE_RATE);
            
            if (_micClip == null)
            {
                Debug.LogError("[DirectMicrophoneSource] Failed to start microphone");
                return;
            }
            
            _lastReadPosition = 0;
            _started = true;
            
            // Start polling coroutine
            if (_coroutineRunner != null)
            {
                _pollCoroutine = _coroutineRunner.StartCoroutine(PollMicrophoneCoroutine());
            }
            else
            {
                Debug.LogError("[DirectMicrophoneSource] No coroutine runner provided");
            }
            
            Debug.Log($"[DirectMicrophoneSource] Started microphone '{_deviceName}' at {SAMPLE_RATE}Hz");
        }
        
        /// <summary>
        /// Stops capturing audio from the microphone.
        /// </summary>
        public override void Stop()
        {
            if (!_started) return;
            
            base.Stop();
            
            // Stop polling coroutine
            if (_pollCoroutine != null && _coroutineRunner != null)
            {
                _coroutineRunner.StopCoroutine(_pollCoroutine);
                _pollCoroutine = null;
            }
            
            // Stop Unity microphone
            if (Microphone.IsRecording(_deviceName))
            {
                Microphone.End(_deviceName);
            }
            
            _micClip = null;
            _started = false;
            
            Debug.Log("[DirectMicrophoneSource] Stopped microphone");
        }
        
        private IEnumerator PollMicrophoneCoroutine()
        {
            // Wait for microphone to initialize
            while (Microphone.GetPosition(_deviceName) <= 0)
            {
                yield return null;
            }
            
            var waitTime = new WaitForSeconds(POLL_INTERVAL_MS / 1000f);
            
            while (_started && !_disposed)
            {
                PollMicrophone();
                yield return waitTime;
            }
        }
        
        private void PollMicrophone()
        {
            if (_micClip == null || !_started) return;
            
            int currentPosition = Microphone.GetPosition(_deviceName);
            if (currentPosition < 0) return;
            
            int samplesToRead;
            
            // Handle circular buffer wrap-around
            if (currentPosition < _lastReadPosition)
            {
                // Wrapped around - read from last position to end, then from start to current
                samplesToRead = (_micClip.samples - _lastReadPosition) + currentPosition;
            }
            else
            {
                samplesToRead = currentPosition - _lastReadPosition;
            }
            
            if (samplesToRead <= 0) return;
            
            // Ensure buffer is large enough
            int totalSamples = samplesToRead * CHANNELS;
            if (_readBuffer.Length < totalSamples)
            {
                _readBuffer = new float[totalSamples * 2]; // Double for safety
            }
            
            // Read audio data directly from the microphone clip
            // This bypasses Unity's audio output DSP entirely
            float[] audioData = new float[totalSamples];
            
            if (currentPosition < _lastReadPosition)
            {
                // Handle wrap-around: read in two parts
                int firstPartSamples = _micClip.samples - _lastReadPosition;
                float[] firstPart = new float[firstPartSamples * _micClip.channels];
                float[] secondPart = new float[currentPosition * _micClip.channels];
                
                _micClip.GetData(firstPart, _lastReadPosition);
                if (currentPosition > 0)
                {
                    _micClip.GetData(secondPart, 0);
                }
                
                // Combine and convert to stereo if needed
                ConvertToStereo(firstPart, secondPart, audioData, _micClip.channels);
            }
            else
            {
                // Simple read
                float[] rawData = new float[samplesToRead * _micClip.channels];
                _micClip.GetData(rawData, _lastReadPosition);
                
                // Convert to stereo if needed
                ConvertToStereo(rawData, null, audioData, _micClip.channels);
            }
            
            _lastReadPosition = currentPosition;
            
            // Calculate volume for VAD (Voice Activity Detection)
            _currentVolume = CalculateRMS(audioData);
            
            // Apply VAD - filter out audio below threshold to prevent ambient conversations
            if (_vadEnabled)
            {
                bool isSpeaking = _currentVolume >= _vadThreshold;
                
                // Fire speech detection events on state change
                if (isSpeaking && !_wasSpeaking)
                {
                    _wasSpeaking = true;
                    OnSpeechDetected?.Invoke();
                }
                else if (!isSpeaking && _wasSpeaking)
                {
                    _wasSpeaking = false;
                    OnSilenceDetected?.Invoke();
                }
                
                // If below threshold, replace audio with silence
                // This prevents distant/ambient conversations from being sent to the server
                if (!isSpeaking)
                {
                    Array.Clear(audioData, 0, audioData.Length);
                }
            }
            
            // Fire the AudioRead event with the CORRECT sample rate (48000)
            // This goes to RtcAudioSource.OnAudioRead which sends to native FFI with AEC
            AudioRead?.Invoke(audioData, CHANNELS, SAMPLE_RATE);
        }
        
        /// <summary>
        /// Calculate RMS (Root Mean Square) volume of audio samples.
        /// RMS gives a more accurate representation of perceived loudness than peak amplitude.
        /// </summary>
        /// <param name="samples">Audio samples to analyze</param>
        /// <returns>RMS volume level (0-1)</returns>
        private float CalculateRMS(float[] samples)
        {
            if (samples == null || samples.Length == 0) return 0f;
            
            float sum = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                sum += samples[i] * samples[i];
            }
            return Mathf.Sqrt(sum / samples.Length);
        }
        
        /// <summary>
        /// Converts audio data to stereo format expected by LiveKit.
        /// </summary>
        private void ConvertToStereo(float[] firstPart, float[] secondPart, float[] output, int sourceChannels)
        {
            int outputIndex = 0;
            
            // Process first part
            if (sourceChannels == 1)
            {
                // Mono to stereo: duplicate each sample
                for (int i = 0; i < firstPart.Length && outputIndex < output.Length - 1; i++)
                {
                    output[outputIndex++] = firstPart[i];
                    output[outputIndex++] = firstPart[i];
                }
                
                if (secondPart != null)
                {
                    for (int i = 0; i < secondPart.Length && outputIndex < output.Length - 1; i++)
                    {
                        output[outputIndex++] = secondPart[i];
                        output[outputIndex++] = secondPart[i];
                    }
                }
            }
            else
            {
                // Already stereo (or more), just copy
                Array.Copy(firstPart, 0, output, 0, Math.Min(firstPart.Length, output.Length));
                outputIndex = Math.Min(firstPart.Length, output.Length);
                
                if (secondPart != null && outputIndex < output.Length)
                {
                    Array.Copy(secondPart, 0, output, outputIndex, Math.Min(secondPart.Length, output.Length - outputIndex));
                }
            }
        }
        
        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                Stop();
            }
            _disposed = true;
            base.Dispose(disposing);
        }
        
        ~DirectMicrophoneSource()
        {
            Dispose(false);
        }
    }
#endif
}



