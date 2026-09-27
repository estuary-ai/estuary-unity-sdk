using System;
using System.Collections.Generic;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using Estuary.Models;
using Estuary.Utilities;

namespace Estuary
{
    /// <summary>
    /// Component for playing back Estuary voice responses.
    /// Uses a ring buffer for smooth streaming playback in WebSocket mode.
    /// In LiveKit mode, audio is handled automatically by LiveKit's AudioStream.
    /// </summary>
    [AddComponentMenu("Estuary/Estuary Audio Source")]
    [RequireComponent(typeof(AudioSource))]
    public class EstuaryAudioSource : MonoBehaviour
    {
        #region Inspector Fields

        [Header("Audio")]
        [SerializeField]
        [Tooltip("AudioSource for playback (auto-attached if not set)")]
        private AudioSource audioSource;

        [SerializeField]
        [Tooltip("Volume for voice playback")]
        [Range(0f, 1f)]
        private float volume = 1f;

        [Header("Playback Settings")]
        [Tooltip("Expected sample rate from server (matches Unity's audio output rate)")]
        private int expectedSampleRate => AudioSettings.outputSampleRate;

        [Header("Events")]
        [SerializeField]
        private UnityEvent onPlaybackStarted = new UnityEvent();

        [SerializeField]
        private UnityEvent onPlaybackComplete = new UnityEvent();

        [SerializeField]
        private UnityEvent onPlaybackInterrupted = new UnityEvent();

        #endregion

        #region Properties

        /// <summary>
        /// Whether audio is currently playing.
        /// </summary>
        public bool IsPlaying => audioSource != null && audioSource.isPlaying;

        /// <summary>
        /// Amount of audio buffered for playback (in samples).
        /// </summary>
        public int BufferedSampleCount
        {
            get
            {
                lock (_streamingLock)
                {
                    return _samplesAvailable;
                }
            }
        }

        /// <summary>
        /// Current message ID being played.
        /// </summary>
        public string CurrentMessageId { get; private set; }

        /// <summary>
        /// Volume for voice playback (0.0 to 1.0).
        /// In WebSocket mode, controls the AudioSource volume directly.
        /// In LiveKit mode, controls the LiveKit bot audio output volume.
        /// </summary>
        public float Volume
        {
            get => volume;
            set
            {
                volume = Mathf.Clamp01(value);
                if (audioSource != null)
                    audioSource.volume = volume;
                // Also apply to LiveKit voice manager if in LiveKit mode
                if (_liveKitManager != null)
                    _liveKitManager.OutputVolume = volume;
            }
        }

        #endregion

        #region C# Events

        /// <summary>
        /// Fired when playback starts.
        /// </summary>
        public event Action OnPlaybackStarted;

        /// <summary>
        /// Fired when audio streaming has finished.
        /// </summary>
        public event Action OnPlaybackComplete;

        /// <summary>
        /// Fired when playback is interrupted.
        /// </summary>
        public event Action OnPlaybackInterrupted;

        #endregion

        #region Private Fields

        private string _currentlyPlayingMessageId;
        private readonly AudioPlaybackTracker _playbackTracker = new AudioPlaybackTracker();
        private double _outputLatency;
        private double _nextRenderTime;
        private int _playbackGeneration;

        /// <summary>Fired after the final PCM for this message has rendered locally.</summary>
        public event Action<string> OnMessagePlaybackComplete;

        // LiveKit mode fields
        private ILiveKitVoiceManager _liveKitManager;
        private bool _useLiveKit;

        // Streaming playback fields (WebSocket mode - ring buffer)
        private int _streamingSampleRate = 48000;
        private int _streamingChannels = 1;
        private Coroutine _streamingPlaybackCoroutine;
        private readonly object _streamingLock = new object();

        // Ring buffer for continuous audio streaming (fixes audio gaps)
        // 30 seconds to handle chatty characters with long responses
        private const int RING_BUFFER_SECONDS = 30;
        private const float MIN_BUFFER_BEFORE_PLAY = 0.25f; // 250ms pre-buffer before starting playback (increased to reduce underruns)
        private const float BUFFER_LOW_THRESHOLD = 0.05f; // 50ms - consider underrun if below this
        private const float FADE_OUT_MS = 10f; // 10ms fade, smooth transition to silence on underrun
        private int _fadeOutSamples; // Calculated based on actual sample rate (FADE_OUT_MS * sampleRate / 1000)
        private bool _hasLoggedResample; // Track if we've logged about resampling (to avoid spam)
        private float[] _ringBuffer;
        private int _ringBufferSize;
        private int _writePosition;
        private int _readPosition;
        private int _samplesAvailable;
        private bool _isStreamingActive;
        private bool _hasStartedPlaying;
        private AudioClip _streamingClip;
        private bool _streamingClipPlaying;

        #endregion

        #region Unity Lifecycle

        private void Awake()
        {
            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
            }

            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
            }

            // Configure audio source for voice playback
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 0f; // 2D audio
            audioSource.volume = volume;

            // Initialize ring buffer for WebSocket streaming playback
            InitializeStreamingBuffer();
        }

        private void OnDisable()
        {
            StopPlayback();
            CleanupStreamingBuffer();
        }

        private void OnDestroy()
        {
            CleanupStreamingBuffer();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Enqueue audio for playback via ring buffer streaming.
        /// </summary>
        /// <param name="voice">BotVoice data containing audio</param>
        public void EnqueueAudio(BotVoice voice)
        {
            // LiveKit handles its own audio via AudioStream - don't process here
            if (_useLiveKit)
            {
                Debug.Log("[EstuaryAudioSource] Ignoring EnqueueAudio in LiveKit mode (audio handled by AudioStream)");
                return;
            }

            if (voice == null) return;
            if (voice.IsFinal && voice.DecodedAudio.Length == 0)
            {
                MarkAudioComplete(voice.MessageId);
                return;
            }
            if (voice.DecodedAudio.Length == 0)
            {
                Debug.LogWarning("[EstuaryAudioSource] Received empty audio data");
                return;
            }

            // Update current message ID
            CurrentMessageId = voice.MessageId;
            _currentlyPlayingMessageId = voice.MessageId;

            // Convert PCM16 bytes to float samples
            var samples = AudioConverter.PCM16ToFloat(voice.DecodedAudio);
            if (samples.Length == 0)
            {
                Debug.LogWarning("[EstuaryAudioSource] No samples decoded from voice data");
                return;
            }

            // Resample if incoming audio is at a different rate than expected
            var voiceSampleRate = voice.SampleRate > 0 ? voice.SampleRate : expectedSampleRate;
            if (voiceSampleRate != expectedSampleRate)
            {
                if (!_hasLoggedResample)
                {
                    Debug.Log($"[EstuaryAudioSource] Resampling audio from {voiceSampleRate}Hz to {expectedSampleRate}Hz");
                    _hasLoggedResample = true;
                }
                samples = AudioConverter.Resample(samples, voiceSampleRate, expectedSampleRate);
            }

            Debug.Log($"[EstuaryAudioSource] Writing {samples.Length} samples to ring buffer (chunk {voice.ChunkIndex})");

            // Write samples to ring buffer
            WriteToRingBuffer(samples, voice.MessageId);
            // Legacy filler interjections are delivered as a single complete PCM packet.
            if (voice.IsFinal || voice.IsInterjection) MarkAudioComplete(voice.MessageId);

            // Start streaming playback if not already running
            if (!_isStreamingActive)
            {
                _isStreamingActive = true;
                _streamingPlaybackCoroutine = StartCoroutine(StreamingPlaybackCoroutine());
            }
        }

        /// <summary>
        /// Enqueue raw audio bytes for playback via ring buffer streaming.
        /// </summary>
        /// <param name="audioBytes">PCM16 audio bytes</param>
        /// <param name="sampleRate">Sample rate of the audio</param>
        /// <param name="messageId">Optional message ID. Call MarkAudioComplete after its final raw chunk.</param>
        public void EnqueueAudio(byte[] audioBytes, int sampleRate, string messageId = null)
        {
            // LiveKit handles its own audio via AudioStream - don't process here
            if (_useLiveKit)
            {
                Debug.Log("[EstuaryAudioSource] Ignoring EnqueueAudio in LiveKit mode (audio handled by AudioStream)");
                return;
            }

            if (audioBytes == null || audioBytes.Length == 0)
            {
                Debug.LogWarning("[EstuaryAudioSource] Received empty audio bytes");
                return;
            }

            // Update current message ID
            CurrentMessageId = messageId;
            _currentlyPlayingMessageId = messageId;

            // Convert PCM16 bytes to float samples
            var samples = AudioConverter.PCM16ToFloat(audioBytes);
            if (samples.Length == 0)
            {
                Debug.LogWarning("[EstuaryAudioSource] No samples decoded from audio bytes");
                return;
            }

            // Resample if incoming audio is at a different rate than expected
            if (sampleRate != expectedSampleRate)
            {
                if (!_hasLoggedResample)
                {
                    Debug.Log($"[EstuaryAudioSource] Resampling audio from {sampleRate}Hz to {expectedSampleRate}Hz");
                    _hasLoggedResample = true;
                }
                samples = AudioConverter.Resample(samples, sampleRate, expectedSampleRate);
            }

            // Write samples to ring buffer
            WriteToRingBuffer(samples, messageId);

            // Start streaming playback if not already running
            if (!_isStreamingActive)
            {
                _isStreamingActive = true;
                _streamingPlaybackCoroutine = StartCoroutine(StreamingPlaybackCoroutine());
            }
        }

        /// <summary>Mark the last raw PCM chunk for a message. BotVoice callers use IsFinal automatically.</summary>
        public void MarkAudioComplete(string messageId)
        {
            lock (_streamingLock) _playbackTracker.MarkFinal(messageId);
        }

        /// <summary>
        /// Stop current playback and clear the streaming buffer.
        /// </summary>
        public void StopPlayback()
        {
            _playbackGeneration++;
            // Stop streaming playback coroutine
            if (_streamingPlaybackCoroutine != null)
            {
                StopCoroutine(_streamingPlaybackCoroutine);
                _streamingPlaybackCoroutine = null;
            }

            // Stop audio
            if (audioSource != null && audioSource.isPlaying)
            {
                audioSource.Stop();
                audioSource.loop = false;
            }

            if (_streamingClip != null)
            {
                Destroy(_streamingClip);
                _streamingClip = null;
            }

            // Clear streaming buffer
            ClearStreamingBuffer();

            _currentlyPlayingMessageId = null;
            CurrentMessageId = null;
            _isStreamingActive = false;
            _streamingClipPlaying = false;

            // Fire interrupted event
            OnPlaybackInterrupted?.Invoke();
            onPlaybackInterrupted?.Invoke();

            Debug.Log("[EstuaryAudioSource] Playback stopped");
        }

        /// <summary>
        /// Interrupt playback for a specific message ID.
        /// </summary>
        /// <param name="messageId">Message ID to interrupt</param>
        public void InterruptMessage(string messageId)
        {
            if (string.IsNullOrEmpty(messageId))
            {
                StopPlayback();
                return;
            }

            // Only interrupt if this message is currently playing
            if (_currentlyPlayingMessageId == messageId)
            {
                StopPlayback();
            }
        }

        /// <summary>Remove a moderated message's queued PCM without discarding a newer message.</summary>
        public void RedactMessage(string messageId)
        {
            if (string.IsNullOrEmpty(messageId)) return;
            if (_currentlyPlayingMessageId == messageId)
            {
                StopPlayback();
                return;
            }
            lock (_streamingLock)
                _playbackTracker.Redact(messageId, (offset, count) =>
                {
                    for (int i = 0; i < count; i++)
                        _ringBuffer[(_readPosition + offset + i) % _ringBufferSize] = 0f;
                });
        }

        /// <summary>
        /// Set the LiveKit voice manager for WebRTC audio playback.
        /// When set, audio will be received via LiveKit instead of WebSocket.
        /// NOTE: LiveKit handles audio playback automatically via AudioStream,
        /// so no ring buffer is needed for LiveKit mode.
        /// </summary>
        /// <param name="manager">The ILiveKitVoiceManager to use</param>
        public void SetLiveKitManager(ILiveKitVoiceManager manager)
        {
            _liveKitManager = manager;
            _useLiveKit = manager != null;

            // Sync volume to LiveKit manager
            if (manager != null)
            {
                manager.OutputVolume = volume;
            }

            Debug.Log($"[EstuaryAudioSource] {(manager != null ? "LiveKit" : "WebSocket")} mode enabled");
        }

        /// <summary>
        /// Configure the audio source for LiveKit or WebSocket mode.
        /// </summary>
        /// <param name="voiceMode">The voice mode to use</param>
        /// <param name="liveKitManager">LiveKit manager (required if voiceMode is LiveKit)</param>
        public void Configure(VoiceMode voiceMode, ILiveKitVoiceManager liveKitManager = null)
        {
            if (voiceMode == VoiceMode.LiveKit && liveKitManager != null)
            {
                SetLiveKitManager(liveKitManager);
            }
            else
            {
                SetLiveKitManager(null);
            }

            Debug.Log($"[EstuaryAudioSource] Configured for {voiceMode} mode");
        }

        #endregion

        #region WebSocket Audio Handling - Ring Buffer Streaming

        private void InitializeStreamingBuffer()
        {
            // Use expected sample rate for buffer (incoming audio will be resampled if needed)
            _streamingSampleRate = expectedSampleRate;
            AudioSettings.GetDSPBufferSize(out var bufferLength, out var bufferCount);
            _outputLatency = (double)bufferLength * bufferCount / _streamingSampleRate;

            // Calculate fade-out samples based on actual sample rate (10ms fade)
            _fadeOutSamples = (int)(_streamingSampleRate * FADE_OUT_MS / 1000f);

            // Create ring buffer for continuous audio streaming
            // Buffer size = sample rate * channels * seconds
            _ringBufferSize = _streamingSampleRate * _streamingChannels * RING_BUFFER_SECONDS;
            _ringBuffer = new float[_ringBufferSize];
            _writePosition = 0;
            _readPosition = 0;
            _samplesAvailable = 0;
            _isStreamingActive = false;
            _hasStartedPlaying = false;
            _streamingClipPlaying = false;
            _hasLoggedResample = false;

            Debug.Log($"[EstuaryAudioSource] Initialized streaming buffer: {_ringBufferSize} samples ({RING_BUFFER_SECONDS}s at {_streamingSampleRate}Hz), fade-out: {_fadeOutSamples} samples ({FADE_OUT_MS}ms)");
        }

        private void CleanupStreamingBuffer()
        {
            _isStreamingActive = false;
            _hasStartedPlaying = false;
            _streamingClipPlaying = false;

            if (_streamingPlaybackCoroutine != null)
            {
                StopCoroutine(_streamingPlaybackCoroutine);
                _streamingPlaybackCoroutine = null;
            }

            if (audioSource != null && audioSource.isPlaying)
            {
                audioSource.Stop();
            }

            if (_streamingClip != null)
            {
                Destroy(_streamingClip);
                _streamingClip = null;
            }

            lock (_streamingLock)
            {
                _ringBuffer = null;
                _writePosition = 0;
                _readPosition = 0;
                _samplesAvailable = 0;
                _playbackTracker.Reset();
                _nextRenderTime = 0;
            }
        }

        /// <summary>
        /// Clear all pending audio from the ring buffer (used for interrupts).
        /// </summary>
        public void ClearStreamingBuffer()
        {
            lock (_streamingLock)
            {
                if (_ringBuffer != null)
                {
                    Array.Clear(_ringBuffer, 0, _ringBuffer.Length);
                }
                _writePosition = 0;
                _readPosition = 0;
                _samplesAvailable = 0;
                _playbackTracker.Reset();
                _nextRenderTime = 0;
            }

            _hasStartedPlaying = false;

            if (audioSource != null && audioSource.isPlaying)
            {
                audioSource.Stop();
            }
            
            _streamingClipPlaying = false;

            Debug.Log("[EstuaryAudioSource] Cleared streaming audio buffer (interrupt)");
        }

        /// <summary>
        /// Write audio samples to the ring buffer for streaming playback.
        /// </summary>
        private void WriteToRingBuffer(float[] samples, string messageId)
        {
            if (samples == null || samples.Length == 0)
                return;

            lock (_streamingLock)
            {
                if (_ringBuffer == null)
                    return;

                _playbackTracker.Enqueue(messageId, samples.Length);
                int overflow = Math.Max(0, _samplesAvailable + samples.Length - _ringBufferSize);
                if (overflow > 0)
                    _playbackTracker.Consume(overflow, AudioSettings.dspTime,
                        _streamingSampleRate * _streamingChannels, _outputLatency);
                for (int i = 0; i < samples.Length; i++)
                {
                    _ringBuffer[_writePosition] = samples[i];
                    _writePosition = (_writePosition + 1) % _ringBufferSize;
                    
                    // Track available samples, cap at buffer size
                    if (_samplesAvailable < _ringBufferSize)
                    {
                        _samplesAvailable++;
                    }
                    else
                    {
                        // Buffer overflow - move read position to avoid playing old data
                        _readPosition = (_readPosition + 1) % _ringBufferSize;
                    }
                }
            }
        }

        private IEnumerator StreamingPlaybackCoroutine()
        {
            Debug.Log("[EstuaryAudioSource] Starting streaming playback coroutine");

            // Calculate pre-buffer threshold in samples
            int preBufferSamples = (int)(_streamingSampleRate * _streamingChannels * MIN_BUFFER_BEFORE_PLAY);
            int generation = _playbackGeneration;

            // Wait for minimum buffer before starting playback
            while (_isStreamingActive)
            {
                int available;
                bool finalReceived;
                lock (_streamingLock)
                {
                    available = _samplesAvailable;
                    finalReceived = _playbackTracker.HasFinal;
                }

                if (available >= preBufferSamples || (available > 0 && finalReceived))
                {
                    Debug.Log($"[EstuaryAudioSource] Pre-buffer threshold reached: {available} samples ({(float)available / _streamingSampleRate / _streamingChannels * 1000:F0}ms)");
                    break;
                }

                yield return new WaitForSeconds(0.01f);
            }

            if (!_isStreamingActive)
            {
                _streamingPlaybackCoroutine = null;
                yield break;
            }

            // Create streaming AudioClip with PCM reader callback
            _streamingClip = AudioClip.Create(
                "WebSocketStream",
                _ringBufferSize / _streamingChannels, // Length in samples per channel
                _streamingChannels,
                _streamingSampleRate,
                true, // Stream
                OnAudioRead,
                OnAudioSetPosition
            );

            audioSource.clip = _streamingClip;
            audioSource.loop = true; // Loop to keep playing continuously
            audioSource.Play();
            _streamingClipPlaying = true;

            // Fire playback started event
            if (!_hasStartedPlaying)
            {
                _hasStartedPlaying = true;
                OnPlaybackStarted?.Invoke();
                onPlaybackStarted?.Invoke();
                Debug.Log("[EstuaryAudioSource] Started WebSocket streaming playback");
            }
            if (generation != _playbackGeneration) yield break;

            // A final packet closes a message, but completion waits for local
            // rendering. An underrun alone never means the server has finished.
            while (_isStreamingActive && _streamingClipPlaying)
            {
                List<string> completed;
                lock (_streamingLock)
                    completed = _playbackTracker.TakeCompleted(AudioSettings.dspTime);
                foreach (var messageId in completed)
                {
                    // Report before callbacks: a subscriber can dispatch a new
                    // response or disconnect synchronously from its callback.
                    NotifyPlaybackComplete(messageId);
                    OnMessagePlaybackComplete?.Invoke(messageId);
                    if (generation != _playbackGeneration) yield break;
                }
                bool drained;
                lock (_streamingLock)
                    drained = _playbackTracker.PendingCount == 0 && _samplesAvailable == 0;
                if (drained) break;
                yield return null;
            }

            // Stop playback
            if (audioSource != null && audioSource.isPlaying)
            {
                audioSource.Stop();
                audioSource.loop = false;
            }
            _streamingClipPlaying = false;

            bool played = _hasStartedPlaying;
            _hasStartedPlaying = false;

            // Cleanup
            if (_streamingClip != null)
            {
                Destroy(_streamingClip);
                _streamingClip = null;
            }

            // Reset buffer positions for next message
            lock (_streamingLock)
            {
                _writePosition = 0;
                _readPosition = 0;
                _samplesAvailable = 0;
                _playbackTracker.Reset();
                _nextRenderTime = 0;
            }
            _hasLoggedResample = false;

            _isStreamingActive = false;
            _streamingPlaybackCoroutine = null;
            CurrentMessageId = _currentlyPlayingMessageId = null;

            // Complete cleanup before user callbacks, which may enqueue another turn.
            if (played)
            {
                OnPlaybackComplete?.Invoke();
                onPlaybackComplete?.Invoke();
            }
        }

        /// <summary>
        /// PCM reader callback for streaming AudioClip.
        /// Called by Unity's audio thread to fill the playback buffer.
        /// Implements smooth fade-out on buffer underrun to avoid clicks/pops.
        /// </summary>
        private void OnAudioRead(float[] data)
        {
            lock (_streamingLock)
            {
                if (_ringBuffer == null)
                {
                    // Fill with silence if no buffer
                    Array.Clear(data, 0, data.Length);
                    return;
                }

                int samplesToRead = Math.Min(data.Length, _samplesAvailable);

                // Check if we're about to underrun (not enough samples to fill the request)
                bool isUnderrun = samplesToRead < data.Length;

                // Calculate fade-out region: apply fade to last _fadeOutSamples of available audio
                int fadeOutSamples = _fadeOutSamples > 0 ? _fadeOutSamples : 480; // Fallback to 10ms at 48kHz
                int fadeStartIndex = isUnderrun ? Math.Max(0, samplesToRead - fadeOutSamples) : -1;

                for (int i = 0; i < samplesToRead; i++)
                {
                    float sample = _ringBuffer[_readPosition];
                    _readPosition = (_readPosition + 1) % _ringBufferSize;

                    // Apply fade-out if we're in the underrun fade region
                    if (fadeStartIndex >= 0 && i >= fadeStartIndex)
                    {
                        // Linear fade from 1.0 to 0.0 over the fade region
                        float fadeProgress = (float)(i - fadeStartIndex) / fadeOutSamples;
                        float fadeMultiplier = 1.0f - fadeProgress;
                        sample *= fadeMultiplier;
                    }

                    data[i] = sample;
                }
                
                _samplesAvailable -= samplesToRead;
                // Unity may prefetch multiple PCM blocks at the same DSP time.
                // Include queued silence so later chunks cannot complete early.
                double blockStart = Math.Max(AudioSettings.dspTime, _nextRenderTime);
                double samplesPerSecond = _streamingSampleRate * _streamingChannels;
                _nextRenderTime = blockStart + data.Length / samplesPerSecond;
                _playbackTracker.Consume(samplesToRead, blockStart, samplesPerSecond, _outputLatency);

                // Fill remaining with silence (after fade-out, so no click)
                if (samplesToRead < data.Length)
                {
                    Array.Clear(data, samplesToRead, data.Length - samplesToRead);
                }
            }
        }

        /// <summary>
        /// Position callback for streaming AudioClip.
        /// Called when the clip position is set.
        /// </summary>
        private void OnAudioSetPosition(int newPosition)
        {
            // We don't support seeking in our ring buffer, just ignore
        }

        #endregion

        #region Private Methods

        private async void NotifyPlaybackComplete(string messageId)
        {
            try
            {
                if (EstuaryManager.HasInstance && EstuaryManager.Instance.IsConnected)
                {
                    await EstuaryManager.Instance.NotifyAudioPlaybackCompleteAsync(messageId);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EstuaryAudioSource] Failed to notify playback complete: {e.Message}");
            }
        }

        #endregion
    }
}





