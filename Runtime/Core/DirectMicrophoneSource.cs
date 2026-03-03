using System;
using System.Collections;
using UnityEngine;
using LiveKit;

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
    public sealed class DirectMicrophoneSource : RtcAudioSource
    {
        private const int SAMPLE_RATE = 16000;
        private const int CHANNELS = 1; // Mono for voice (industry standard for conversational AI)
        private const float POLL_INTERVAL_MS = 20f; // Poll every 20ms for low latency
        
        // Static constructor to configure LiveKit's default sample rate BEFORE any instance is created
        static DirectMicrophoneSource()
        {
            // Override LiveKit's default (48000) to match our target sample rate
            RtcAudioSource.DefaultMicrophoneSampleRate = SAMPLE_RATE;
            RtcAudioSource.DefaultChannels = CHANNELS;
            UnityEngine.Debug.Log($"[DirectMicrophoneSource] Configured LiveKit defaults: {SAMPLE_RATE}Hz, {CHANNELS} channel(s)");
        }
        
        private readonly string _deviceName;
        private readonly MonoBehaviour _coroutineRunner;
        
        private AudioClip _micClip;
        private int _lastReadPosition;
        private float[] _readBuffer;
        private float[] _audioDataBuffer;      // Pre-allocated output buffer (reused each poll)
        private float[] _firstPartBuffer;      // Pre-allocated wrap-around buffer (reused each poll)
        private float[] _secondPartBuffer;     // Pre-allocated wrap-around buffer (reused each poll)
        private float[] _rawDataBuffer;        // Pre-allocated simple-read buffer (reused each poll)
        private bool _started;
        private bool _disposed;
        private Coroutine _pollCoroutine;
        
        // VAD (Voice Activity Detection) settings
        private bool _vadEnabled = false;
        private float _vadThreshold = 0.010f; // RMS threshold - tuned for near-field AR glasses
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
            
            // Pre-allocate buffers for ~20ms of audio at 16kHz mono
            // Use 2x size for variable timing and wrap-around headroom
            int samplesPerPoll = (int)(SAMPLE_RATE * (POLL_INTERVAL_MS / 1000f));
            int initialSize = samplesPerPoll * CHANNELS * 2;
            _readBuffer = new float[initialSize];
            _audioDataBuffer = new float[initialSize];
            _firstPartBuffer = new float[initialSize];
            _secondPartBuffer = new float[initialSize];
            _rawDataBuffer = new float[initialSize];
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

            // Ensure pre-allocated buffers are large enough (only reallocate if too small, keep larger buffer)
            int totalSamples = samplesToRead * CHANNELS;
            if (_readBuffer.Length < totalSamples)
            {
                int newSize = totalSamples * 2;
                _readBuffer = new float[newSize];
            }
            if (_audioDataBuffer.Length < totalSamples)
            {
                _audioDataBuffer = new float[totalSamples * 2];
            }

            // Clear the output region of the audio data buffer
            Array.Clear(_audioDataBuffer, 0, totalSamples);

            if (currentPosition < _lastReadPosition)
            {
                // Handle wrap-around: read in two parts using pre-allocated buffers
                int firstPartSamples = _micClip.samples - _lastReadPosition;
                int firstPartTotal = firstPartSamples * _micClip.channels;
                int secondPartTotal = currentPosition * _micClip.channels;

                if (_firstPartBuffer.Length < firstPartTotal)
                    _firstPartBuffer = new float[firstPartTotal * 2];
                if (_secondPartBuffer.Length < secondPartTotal)
                    _secondPartBuffer = new float[Math.Max(secondPartTotal * 2, 1)];

                _micClip.GetData(_firstPartBuffer, _lastReadPosition);
                if (currentPosition > 0)
                {
                    _micClip.GetData(_secondPartBuffer, 0);
                }

                // Copy to output buffer (mono) — uses slices of pre-allocated buffers
                CopyToOutput(_firstPartBuffer, firstPartTotal,
                             _secondPartBuffer, currentPosition > 0 ? secondPartTotal : 0,
                             _audioDataBuffer, totalSamples, _micClip.channels);
            }
            else
            {
                // Simple read using pre-allocated buffer
                int rawTotal = samplesToRead * _micClip.channels;
                if (_rawDataBuffer.Length < rawTotal)
                    _rawDataBuffer = new float[rawTotal * 2];

                _micClip.GetData(_rawDataBuffer, _lastReadPosition);

                // Copy to output buffer (mono)
                CopyToOutput(_rawDataBuffer, rawTotal,
                             null, 0,
                             _audioDataBuffer, totalSamples, _micClip.channels);
            }

            _lastReadPosition = currentPosition;
            
            // Calculate volume for VAD (Voice Activity Detection)
            _currentVolume = CalculateRMS(_audioDataBuffer, totalSamples);

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
                    Array.Clear(_audioDataBuffer, 0, totalSamples);
                }
            }

            // Fire the AudioRead event with 16kHz mono
            // We must pass an array sized exactly to the data length for LiveKit's native FFI
            // Reuse _readBuffer as the correctly-sized output if it fits, otherwise slice
            if (_readBuffer.Length >= totalSamples)
            {
                Array.Copy(_audioDataBuffer, 0, _readBuffer, 0, totalSamples);
            }
            // This goes to RtcAudioSource.OnAudioRead which sends to native FFI with AEC
            AudioRead?.Invoke(
                totalSamples == _audioDataBuffer.Length ? _audioDataBuffer : GetSlice(_audioDataBuffer, totalSamples),
                CHANNELS, SAMPLE_RATE);
        }

        /// <summary>
        /// Returns a correctly-sized slice from a larger buffer. Reuses a cached array when possible.
        /// </summary>
        private float[] _sliceCache;
        private float[] GetSlice(float[] source, int length)
        {
            if (_sliceCache == null || _sliceCache.Length != length)
                _sliceCache = new float[length];
            Array.Copy(source, 0, _sliceCache, 0, length);
            return _sliceCache;
        }
        
        /// <summary>
        /// Calculate RMS (Root Mean Square) volume of audio samples.
        /// RMS gives a more accurate representation of perceived loudness than peak amplitude.
        /// </summary>
        /// <param name="samples">Audio samples buffer (may be larger than active data)</param>
        /// <param name="length">Number of active samples to analyze</param>
        /// <returns>RMS volume level (0-1)</returns>
        private float CalculateRMS(float[] samples, int length)
        {
            if (samples == null || length <= 0) return 0f;

            float sum = 0f;
            for (int i = 0; i < length; i++)
            {
                sum += samples[i] * samples[i];
            }
            return Mathf.Sqrt(sum / length);
        }

        /// <summary>
        /// Copies audio data to the output buffer, handling mono/stereo conversion if needed.
        /// Uses explicit lengths to avoid allocating new arrays — works with pre-allocated buffers.
        /// </summary>
        private void CopyToOutput(float[] firstPart, int firstPartLength,
                                  float[] secondPart, int secondPartLength,
                                  float[] output, int outputLength, int sourceChannels)
        {
            int outputIndex = 0;

            if (sourceChannels == 1)
            {
                // Source is mono, output is mono - direct copy
                for (int i = 0; i < firstPartLength && outputIndex < outputLength; i++)
                {
                    output[outputIndex++] = firstPart[i];
                }

                if (secondPart != null && secondPartLength > 0)
                {
                    for (int i = 0; i < secondPartLength && outputIndex < outputLength; i++)
                    {
                        output[outputIndex++] = secondPart[i];
                    }
                }
            }
            else
            {
                // Source is stereo, downmix to mono by averaging channels
                for (int i = 0; i < firstPartLength - 1 && outputIndex < outputLength; i += 2)
                {
                    output[outputIndex++] = (firstPart[i] + firstPart[i + 1]) * 0.5f;
                }

                if (secondPart != null && secondPartLength > 0)
                {
                    for (int i = 0; i < secondPartLength - 1 && outputIndex < outputLength; i += 2)
                    {
                        output[outputIndex++] = (secondPart[i] + secondPart[i + 1]) * 0.5f;
                    }
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
}



