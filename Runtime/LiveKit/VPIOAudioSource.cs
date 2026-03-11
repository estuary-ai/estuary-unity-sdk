using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;
using LiveKit;

namespace Estuary
{
    /// <summary>
    /// iOS-only microphone source that captures audio through the Voice Processing I/O (VPIO) audio unit.
    ///
    /// VPIO provides hardware AEC (Acoustic Echo Cancellation), Noise Suppression, and Auto Gain Control
    /// at the audio unit level. This is the ONLY way to get working AEC on iOS — Unity's Microphone.Start()
    /// uses RemoteIO which does NOT apply AEC even when AVAudioSession is in VideoChat mode.
    ///
    /// This class replaces DirectMicrophoneSource on iOS builds. It provides the same interface:
    /// Start(), Stop(), AudioRead event, and VAD (Voice Activity Detection).
    ///
    /// Audio flow:
    ///   VPIO audio unit (hardware AEC) → native ring buffer → C# polling → AudioRead → LiveKit RtcAudioSource
    /// </summary>
    public sealed class VPIOAudioSource : RtcAudioSource
    {
        private const int CHANNELS = 1;
        private const float POLL_INTERVAL_MS = 20f;

        // Max samples per poll: ~60ms at 48kHz = 2880 samples (headroom for native rate)
        private const int MAX_SAMPLES_PER_POLL = 2880;

        // Actual sample rate — set dynamically after VPIO starts
        private int _sampleRate = 48000;

        static VPIOAudioSource()
        {
            // Must be set before RtcAudioSource base constructor runs —
            // it reads DefaultMicrophoneSampleRate at construction time.
            RtcAudioSource.DefaultMicrophoneSampleRate = 48000;
            RtcAudioSource.DefaultChannels = CHANNELS;
        }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern int EstuaryVPIO_Start();

        [DllImport("__Internal")]
        private static extern void EstuaryVPIO_Stop();

        [DllImport("__Internal")]
        private static extern int EstuaryVPIO_ReadAudioData(float[] outputBuffer, int maxSamples);

        [DllImport("__Internal")]
        private static extern int EstuaryVPIO_IsCapturing();

        [DllImport("__Internal")]
        private static extern int EstuaryVPIO_AvailableSamples();

        [DllImport("__Internal")]
        private static extern int EstuaryVPIO_GetSampleRate();
#endif

        private readonly MonoBehaviour _coroutineRunner;
        private bool _started;
        private bool _disposed;
        private Coroutine _pollCoroutine;

        // Pre-allocated buffers
        private float[] _pollBuffer;
        private float[] _sliceCache;

        // VAD (Voice Activity Detection)
        private bool _vadEnabled;
        private float _vadThreshold = 0.010f;
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
        /// </summary>
        public bool VadEnabled
        {
            get => _vadEnabled;
            set => _vadEnabled = value;
        }

        /// <summary>
        /// Volume threshold for VAD (0-1).
        /// </summary>
        public float VadThreshold
        {
            get => _vadThreshold;
            set => _vadThreshold = Mathf.Clamp01(value);
        }

        /// <summary>
        /// Current audio volume level (0-1).
        /// </summary>
        public float CurrentVolume => _currentVolume;

        /// <summary>
        /// Creates a new VPIO audio source for iOS.
        /// </summary>
        /// <param name="coroutineRunner">MonoBehaviour to run the polling coroutine on</param>
        public VPIOAudioSource(MonoBehaviour coroutineRunner)
            : base(CHANNELS, RtcAudioSourceType.AudioSourceMicrophone)
        {
            _coroutineRunner = coroutineRunner;
            _pollBuffer = new float[MAX_SAMPLES_PER_POLL];
            Debug.Log($"[VPIOAudioSource] Initialized (rate will be set after VPIO starts)");
        }

        /// <summary>
        /// Starts VPIO audio capture with hardware AEC.
        /// </summary>
        public override void Start()
        {
            if (_started || _disposed) return;

            base.Start();

#if UNITY_IOS && !UNITY_EDITOR
            // Request microphone permission first
            if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
            {
                Debug.Log("[VPIOAudioSource] Requesting iOS microphone permission...");
                if (_coroutineRunner != null)
                    _coroutineRunner.StartCoroutine(RequestPermissionAndStart());
                else
                    Debug.LogError("[VPIOAudioSource] No coroutine runner for iOS permission request");
                return;
            }

            StartVPIOInternal();
#else
            Debug.LogError("[VPIOAudioSource] VPIO is only available on iOS devices");
#endif
        }

#if UNITY_IOS && !UNITY_EDITOR
        private IEnumerator RequestPermissionAndStart()
        {
            var request = Application.RequestUserAuthorization(UserAuthorization.Microphone);
            yield return request;

            if (Application.HasUserAuthorization(UserAuthorization.Microphone))
            {
                Debug.Log("[VPIOAudioSource] iOS microphone permission granted");
                StartVPIOInternal();
            }
            else
            {
                Debug.LogError("[VPIOAudioSource] iOS microphone permission denied");
            }
        }

        private void StartVPIOInternal()
        {
            if (_started || _disposed) return;

            int result = EstuaryVPIO_Start();
            if (result != 1)
            {
                Debug.LogError("[VPIOAudioSource] Failed to start VPIO capture");
                return;
            }

            // Query the actual sample rate from native VPIO
            _sampleRate = EstuaryVPIO_GetSampleRate();
            RtcAudioSource.DefaultMicrophoneSampleRate = (uint)_sampleRate;
            RtcAudioSource.DefaultChannels = CHANNELS;
            Debug.Log($"[VPIOAudioSource] Configured LiveKit defaults: {_sampleRate}Hz, {CHANNELS} channel(s)");

            _started = true;

            // Start polling coroutine
            if (_coroutineRunner != null)
            {
                _pollCoroutine = _coroutineRunner.StartCoroutine(PollAudioCoroutine());
            }
            else
            {
                Debug.LogError("[VPIOAudioSource] No coroutine runner provided");
            }

            Debug.Log($"[VPIOAudioSource] VPIO capture started (hardware AEC active, {_sampleRate}Hz)");
        }
#endif

        /// <summary>
        /// Stops VPIO audio capture.
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

#if UNITY_IOS && !UNITY_EDITOR
            EstuaryVPIO_Stop();
#endif

            _started = false;
            Debug.Log("[VPIOAudioSource] VPIO capture stopped");
        }

        private IEnumerator PollAudioCoroutine()
        {
            // Brief delay for VPIO to initialize
            yield return null;
            yield return null;

            var waitTime = new WaitForSeconds(POLL_INTERVAL_MS / 1000f);

            while (_started && !_disposed)
            {
                PollAudio();
                yield return waitTime;
            }
        }

        private void PollAudio()
        {
            if (!_started) return;

#if UNITY_IOS && !UNITY_EDITOR
            int samplesRead = EstuaryVPIO_ReadAudioData(_pollBuffer, MAX_SAMPLES_PER_POLL);
            if (samplesRead <= 0) return;

            // Calculate volume for VAD
            _currentVolume = CalculateRMS(_pollBuffer, samplesRead);

            // Apply VAD
            if (_vadEnabled)
            {
                bool isSpeaking = _currentVolume >= _vadThreshold;

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

                if (!isSpeaking)
                {
                    Array.Clear(_pollBuffer, 0, samplesRead);
                }
            }

            // Send to LiveKit — need an exactly-sized array
            float[] output;
            if (samplesRead == _pollBuffer.Length)
            {
                output = _pollBuffer;
            }
            else
            {
                output = GetSlice(_pollBuffer, samplesRead);
            }

            AudioRead?.Invoke(output, CHANNELS, _sampleRate);
#endif
        }

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

        private float[] GetSlice(float[] source, int length)
        {
            if (_sliceCache == null || _sliceCache.Length != length)
                _sliceCache = new float[length];
            Array.Copy(source, 0, _sliceCache, 0, length);
            return _sliceCache;
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

        ~VPIOAudioSource()
        {
            Dispose(false);
        }
    }
}
