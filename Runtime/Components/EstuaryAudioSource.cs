using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;
using Estuary.Models;
using Estuary.Utilities;

namespace Estuary
{
    /// <summary>
    /// Component for playing back Estuary voice responses.
    /// Handles audio queuing, playback, and interruption.
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
        [SerializeField]
        [Tooltip("Expected sample rate from server (ElevenLabs default: 24000)")]
        private int expectedSampleRate = 24000;

        [SerializeField]
        [Tooltip("Stop playback when user starts speaking")]
        private bool autoInterruptOnUserSpeech = true;

        [SerializeField]
        [Tooltip("Reference to microphone for interrupt detection")]
        private EstuaryMicrophone microphoneRef;

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
        /// Number of audio clips queued for playback.
        /// </summary>
        public int QueuedClipCount => _audioQueue.Count;

        /// <summary>
        /// Current message ID being played.
        /// </summary>
        public string CurrentMessageId { get; private set; }

        /// <summary>
        /// Volume for voice playback.
        /// </summary>
        public float Volume
        {
            get => volume;
            set
            {
                volume = Mathf.Clamp01(value);
                if (audioSource != null)
                    audioSource.volume = volume;
            }
        }

        #endregion

        #region C# Events

        /// <summary>
        /// Fired when playback starts.
        /// </summary>
        public event Action OnPlaybackStarted;

        /// <summary>
        /// Fired when all queued audio has finished playing.
        /// </summary>
        public event Action OnPlaybackComplete;

        /// <summary>
        /// Fired when playback is interrupted.
        /// </summary>
        public event Action OnPlaybackInterrupted;

        #endregion

        #region Private Fields

        private readonly Queue<AudioQueueItem> _audioQueue = new Queue<AudioQueueItem>();
        private Coroutine _playbackCoroutine;
        private bool _isProcessingQueue;
        private string _currentlyPlayingMessageId;

        // LiveKit mode fields
        private LiveKitVoiceManager _liveKitManager;
        private bool _useLiveKit;
        private int _liveKitSampleRate = 24000;
        private int _liveKitChannels = 1;
        private Coroutine _liveKitPlaybackCoroutine;
        private readonly object _liveKitLock = new object();

        // Ring buffer for continuous audio streaming (fixes audio gaps)
        private const int RING_BUFFER_SECONDS = 3;
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

        private struct AudioQueueItem
        {
            public AudioClip Clip;
            public string MessageId;
            public int ChunkIndex;
        }

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
        }

        private void OnEnable()
        {
            // Subscribe to microphone events for auto-interrupt
            if (autoInterruptOnUserSpeech && microphoneRef != null)
            {
                microphoneRef.OnSpeechDetected += HandleUserSpeechDetected;
            }
        }

        private void OnDisable()
        {
            // Unsubscribe from microphone events
            if (microphoneRef != null)
            {
                microphoneRef.OnSpeechDetected -= HandleUserSpeechDetected;
            }

            // Unsubscribe from LiveKit events
            if (_liveKitManager != null)
            {
                _liveKitManager.OnAudioReceived -= HandleLiveKitAudioReceived;
            }

            StopPlayback();
            CleanupLiveKitBuffer();
        }

        private void OnDestroy()
        {
            ClearQueue();
            CleanupLiveKitBuffer();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Enqueue audio for playback.
        /// </summary>
        /// <param name="voice">BotVoice data containing audio</param>
        public void EnqueueAudio(BotVoice voice)
        {
            Debug.Log($"[EstuaryAudioSource] EnqueueAudio called, voice={voice}, DecodedAudioLength={voice?.DecodedAudio?.Length ?? 0}");
            
            if (voice == null || voice.DecodedAudio.Length == 0)
            {
                Debug.LogWarning("[EstuaryAudioSource] Received empty audio data");
                return;
            }

            // Create AudioClip from decoded audio
            var clip = CreateAudioClipFromVoice(voice);
            if (clip == null)
            {
                Debug.LogError("[EstuaryAudioSource] Failed to create AudioClip from voice data");
                return;
            }

            Debug.Log($"[EstuaryAudioSource] Created AudioClip: samples={clip.samples}, frequency={clip.frequency}, length={clip.length}s");

            // Add to queue
            _audioQueue.Enqueue(new AudioQueueItem
            {
                Clip = clip,
                MessageId = voice.MessageId,
                ChunkIndex = voice.ChunkIndex
            });

            Debug.Log($"[EstuaryAudioSource] Audio queued, queue size={_audioQueue.Count}, isProcessing={_isProcessingQueue}");

            // Start processing queue if not already
            if (!_isProcessingQueue)
            {
                _playbackCoroutine = StartCoroutine(ProcessQueueCoroutine());
            }
        }

        /// <summary>
        /// Enqueue raw audio bytes for playback.
        /// </summary>
        /// <param name="audioBytes">PCM16 audio bytes</param>
        /// <param name="sampleRate">Sample rate of the audio</param>
        /// <param name="messageId">Optional message ID</param>
        public void EnqueueAudio(byte[] audioBytes, int sampleRate, string messageId = null)
        {
            var clip = AudioConverter.CreateAudioClipFromPCM16(audioBytes, sampleRate);
            if (clip == null)
            {
                Debug.LogError("[EstuaryAudioSource] Failed to create AudioClip from bytes");
                return;
            }

            _audioQueue.Enqueue(new AudioQueueItem
            {
                Clip = clip,
                MessageId = messageId,
                ChunkIndex = 0
            });

            if (!_isProcessingQueue)
            {
                _playbackCoroutine = StartCoroutine(ProcessQueueCoroutine());
            }
        }

        /// <summary>
        /// Stop current playback and clear the queue.
        /// </summary>
        public void StopPlayback()
        {
            // Stop WebSocket queue coroutine
            if (_playbackCoroutine != null)
            {
                StopCoroutine(_playbackCoroutine);
                _playbackCoroutine = null;
            }

            // Stop LiveKit streaming coroutine
            if (_liveKitPlaybackCoroutine != null)
            {
                StopCoroutine(_liveKitPlaybackCoroutine);
                _liveKitPlaybackCoroutine = null;
            }

            // Stop audio
            if (audioSource != null && audioSource.isPlaying)
            {
                audioSource.Stop();
                audioSource.loop = false;
            }

            // Clear WebSocket queue
            ClearQueue();

            // Clear LiveKit ring buffer
            if (_useLiveKit)
            {
                ClearLiveKitAudioBuffer();
            }

            _isProcessingQueue = false;
            _currentlyPlayingMessageId = null;
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

            // Only interrupt if this message is currently playing or queued
            if (_currentlyPlayingMessageId == messageId)
            {
                StopPlayback();
            }
            else
            {
                // Remove matching items from queue
                var newQueue = new Queue<AudioQueueItem>();
                while (_audioQueue.Count > 0)
                {
                    var item = _audioQueue.Dequeue();
                    if (item.MessageId != messageId)
                    {
                        newQueue.Enqueue(item);
                    }
                    else
                    {
                        Destroy(item.Clip);
                    }
                }

                while (newQueue.Count > 0)
                {
                    _audioQueue.Enqueue(newQueue.Dequeue());
                }
            }
        }

        /// <summary>
        /// Clear all queued audio without stopping current playback.
        /// </summary>
        public void ClearQueue()
        {
            while (_audioQueue.Count > 0)
            {
                var item = _audioQueue.Dequeue();
                if (item.Clip != null)
                {
                    Destroy(item.Clip);
                }
            }
        }

        /// <summary>
        /// Set the microphone reference for auto-interrupt.
        /// </summary>
        /// <param name="microphone">Microphone component</param>
        public void SetMicrophoneReference(EstuaryMicrophone microphone)
        {
            // Unsubscribe from old
            if (microphoneRef != null)
            {
                microphoneRef.OnSpeechDetected -= HandleUserSpeechDetected;
            }

            microphoneRef = microphone;

            // Subscribe to new
            if (autoInterruptOnUserSpeech && microphoneRef != null)
            {
                microphoneRef.OnSpeechDetected += HandleUserSpeechDetected;
            }
        }

        /// <summary>
        /// Set the LiveKit voice manager for WebRTC audio playback.
        /// When set, audio will be received via LiveKit instead of WebSocket.
        /// </summary>
        /// <param name="manager">The LiveKitVoiceManager to use</param>
        public void SetLiveKitManager(LiveKitVoiceManager manager)
        {
            // Unsubscribe from old manager
            if (_liveKitManager != null)
            {
                _liveKitManager.OnAudioReceived -= HandleLiveKitAudioReceived;
            }

            _liveKitManager = manager;
            _useLiveKit = manager != null;

            // Subscribe to new manager
            if (_liveKitManager != null)
            {
                _liveKitManager.OnAudioReceived += HandleLiveKitAudioReceived;
                InitializeLiveKitBuffer();
                Debug.Log("[EstuaryAudioSource] LiveKit mode enabled");
            }
            else
            {
                CleanupLiveKitBuffer();
                Debug.Log("[EstuaryAudioSource] WebSocket mode enabled");
            }
        }

        /// <summary>
        /// Configure the audio source for LiveKit or WebSocket mode.
        /// </summary>
        /// <param name="voiceMode">The voice mode to use</param>
        /// <param name="liveKitManager">LiveKit manager (required if voiceMode is LiveKit)</param>
        public void Configure(VoiceMode voiceMode, LiveKitVoiceManager liveKitManager = null)
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

        #region LiveKit Audio Handling - Ring Buffer Streaming

        private void InitializeLiveKitBuffer()
        {
            // Use expected sample rate for buffer (incoming audio will be resampled if needed)
            _liveKitSampleRate = expectedSampleRate;

            // Calculate fade-out samples based on actual sample rate (10ms fade)
            _fadeOutSamples = (int)(_liveKitSampleRate * FADE_OUT_MS / 1000f);

            // Create ring buffer for continuous audio streaming
            // Buffer size = sample rate * channels * seconds
            _ringBufferSize = _liveKitSampleRate * _liveKitChannels * RING_BUFFER_SECONDS;
            _ringBuffer = new float[_ringBufferSize];
            _writePosition = 0;
            _readPosition = 0;
            _samplesAvailable = 0;
            _isStreamingActive = false;
            _hasStartedPlaying = false;
            _streamingClipPlaying = false;
            _hasLoggedResample = false;

            Debug.Log($"[EstuaryAudioSource] Initialized ring buffer: {_ringBufferSize} samples ({RING_BUFFER_SECONDS}s at {_liveKitSampleRate}Hz), fade-out: {_fadeOutSamples} samples ({FADE_OUT_MS}ms)");
        }

        private void CleanupLiveKitBuffer()
        {
            _isStreamingActive = false;
            _hasStartedPlaying = false;
            _streamingClipPlaying = false;

            if (_liveKitPlaybackCoroutine != null)
            {
                StopCoroutine(_liveKitPlaybackCoroutine);
                _liveKitPlaybackCoroutine = null;
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

            lock (_liveKitLock)
            {
                _ringBuffer = null;
                _writePosition = 0;
                _readPosition = 0;
                _samplesAvailable = 0;
            }
        }

        /// <summary>
        /// Clear all pending audio from the ring buffer (used for interrupts).
        /// </summary>
        public void ClearLiveKitAudioBuffer()
        {
            lock (_liveKitLock)
            {
                if (_ringBuffer != null)
                {
                    Array.Clear(_ringBuffer, 0, _ringBuffer.Length);
                }
                _writePosition = 0;
                _readPosition = 0;
                _samplesAvailable = 0;
            }

            _hasStartedPlaying = false;

            if (audioSource != null && audioSource.isPlaying)
            {
                audioSource.Stop();
            }
            
            _streamingClipPlaying = false;

            Debug.Log("[EstuaryAudioSource] Cleared LiveKit audio buffer (interrupt)");
        }

        private void HandleLiveKitAudioReceived(byte[] pcmData, int sampleRate, int channels)
        {
            if (!_useLiveKit || pcmData == null || pcmData.Length == 0)
                return;

            // Only reinitialize buffer if channels change (sample rate handled via resampling)
            if (channels != _liveKitChannels)
            {
                _liveKitChannels = channels;
                // Use expected sample rate for buffer, not incoming rate (we'll resample)
                _liveKitSampleRate = expectedSampleRate;
                InitializeLiveKitBuffer();
            }

            // Convert PCM16 bytes to float samples
            var samples = AudioConverter.PCM16ToFloat(pcmData);
            if (samples.Length == 0)
                return;

            // Resample if incoming audio is at a different rate than expected
            // This prevents playback speed issues and maintains consistent buffer timing
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
            lock (_liveKitLock)
            {
                if (_ringBuffer == null)
                    return;

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

            // Start streaming playback if not already running
            if (!_isStreamingActive)
            {
                _isStreamingActive = true;
                _liveKitPlaybackCoroutine = StartCoroutine(StreamingPlaybackCoroutine());
            }
        }

        private IEnumerator StreamingPlaybackCoroutine()
        {
            Debug.Log("[EstuaryAudioSource] Starting streaming playback coroutine");

            // Calculate pre-buffer threshold in samples
            int preBufferSamples = (int)(_liveKitSampleRate * _liveKitChannels * MIN_BUFFER_BEFORE_PLAY);
            float emptyBufferTime = 0f;
            const float maxEmptyBufferWait = 1.0f; // Consider stream ended after 1s of empty buffer

            // Wait for minimum buffer before starting playback
            while (_isStreamingActive)
            {
                int available;
                lock (_liveKitLock)
                {
                    available = _samplesAvailable;
                }

                if (available >= preBufferSamples)
                {
                    Debug.Log($"[EstuaryAudioSource] Pre-buffer threshold reached: {available} samples ({(float)available / _liveKitSampleRate / _liveKitChannels * 1000:F0}ms)");
                    break;
                }

                yield return new WaitForSeconds(0.01f);
            }

            if (!_isStreamingActive)
            {
                _liveKitPlaybackCoroutine = null;
                yield break;
            }

            // Create streaming AudioClip with PCM reader callback
            _streamingClip = AudioClip.Create(
                "LiveKitStream",
                _ringBufferSize / _liveKitChannels, // Length in samples per channel
                _liveKitChannels,
                _liveKitSampleRate,
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
                Debug.Log("[EstuaryAudioSource] Started LiveKit streaming playback");
            }

            // Monitor buffer and detect end of stream
            int bufferLogCounter = 0;
            const int bufferLogInterval = 20; // Log every ~1 second (20 * 0.05s)
            int lowBufferWarnings = 0;
            int lowBufferThresholdSamples = (int)(_liveKitSampleRate * _liveKitChannels * BUFFER_LOW_THRESHOLD);
            
            while (_isStreamingActive && _streamingClipPlaying)
            {
                int available;
                lock (_liveKitLock)
                {
                    available = _samplesAvailable;
                }

                if (available == 0)
                {
                    emptyBufferTime += 0.05f;
                    
                    if (emptyBufferTime >= maxEmptyBufferWait)
                    {
                        // Stream appears to have ended
                        Debug.Log("[EstuaryAudioSource] Stream ended (buffer empty for 1s)");
                        break;
                    }
                }
                else
                {
                    emptyBufferTime = 0f;
                    
                    // Warn if buffer is running low (potential underrun)
                    if (available < lowBufferThresholdSamples)
                    {
                        lowBufferWarnings++;
                        float bufferMs = (float)available / _liveKitSampleRate / _liveKitChannels * 1000;
                        Debug.LogWarning($"[EstuaryAudioSource] Buffer low: {bufferMs:F0}ms remaining (threshold: {BUFFER_LOW_THRESHOLD * 1000:F0}ms)");
                    }
                }

                // Periodic buffer health log
                bufferLogCounter++;
                if (bufferLogCounter >= bufferLogInterval)
                {
                    bufferLogCounter = 0;
                    float fillPercent = (float)available / _ringBufferSize * 100;
                    float bufferMs = (float)available / _liveKitSampleRate / _liveKitChannels * 1000;
                    Debug.Log($"[EstuaryAudioSource] Buffer health: {fillPercent:F1}% ({bufferMs:F0}ms), low warnings: {lowBufferWarnings}");
                }

                yield return new WaitForSeconds(0.05f);
            }

            // Stop playback
            if (audioSource != null && audioSource.isPlaying)
            {
                audioSource.Stop();
                audioSource.loop = false;
            }
            _streamingClipPlaying = false;

            // Fire playback complete event
            if (_hasStartedPlaying)
            {
                _hasStartedPlaying = false;
                OnPlaybackComplete?.Invoke();
                onPlaybackComplete?.Invoke();
                Debug.Log("[EstuaryAudioSource] LiveKit streaming playback complete");

                // Notify server
                NotifyPlaybackComplete();
            }

            // Cleanup
            if (_streamingClip != null)
            {
                Destroy(_streamingClip);
                _streamingClip = null;
            }

            _isStreamingActive = false;
            _liveKitPlaybackCoroutine = null;
        }

        /// <summary>
        /// PCM reader callback for streaming AudioClip.
        /// Called by Unity's audio thread to fill the playback buffer.
        /// Implements smooth fade-out on buffer underrun to avoid clicks/pops.
        /// </summary>
        private void OnAudioRead(float[] data)
        {
            lock (_liveKitLock)
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
                int fadeOutSamples = _fadeOutSamples > 0 ? _fadeOutSamples : 240; // Fallback to 10ms at 24kHz
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

        private AudioClip CreateAudioClipFromVoice(BotVoice voice)
        {
            try
            {
                // Decode base64 to PCM bytes
                var pcmBytes = voice.DecodedAudio;

                // Convert PCM16 to float samples
                var samples = AudioConverter.PCM16ToFloat(pcmBytes);

                if (samples.Length == 0)
                {
                    Debug.LogWarning("[EstuaryAudioSource] No samples decoded from voice data");
                    return null;
                }

                // Create AudioClip
                var sampleRate = voice.SampleRate > 0 ? voice.SampleRate : expectedSampleRate;
                return AudioConverter.CreateAudioClip(samples, sampleRate, 1, $"EstuaryVoice_{voice.ChunkIndex}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[EstuaryAudioSource] Error creating AudioClip: {e.Message}");
                return null;
            }
        }

        private IEnumerator ProcessQueueCoroutine()
        {
            _isProcessingQueue = true;
            var isFirstClip = true;
            var emptyQueueWaitTime = 0f;
            const float maxEmptyQueueWait = 2.0f; // Wait up to 2 seconds for more chunks

            while (true)
            {
                if (_audioQueue.Count > 0)
                {
                    // Reset wait timer when we have audio
                    emptyQueueWaitTime = 0f;
                    
                    var item = _audioQueue.Dequeue();

                    if (item.Clip == null)
                        continue;

                    // Update current message ID
                    _currentlyPlayingMessageId = item.MessageId;
                    CurrentMessageId = item.MessageId;

                    // Fire started event on first clip
                    if (isFirstClip)
                    {
                        isFirstClip = false;
                        OnPlaybackStarted?.Invoke();
                        onPlaybackStarted?.Invoke();
                        Debug.Log($"[EstuaryAudioSource] Started playing message {item.MessageId}");
                    }

                    // Play the clip
                    audioSource.clip = item.Clip;
                    audioSource.Play();

                    // Wait for clip to finish
                    while (audioSource.isPlaying)
                    {
                        yield return null;
                    }

                    // Cleanup
                    Destroy(item.Clip);
                }
                else
                {
                    // Queue is empty - wait a bit for more chunks to arrive
                    if (!isFirstClip) // Only wait if we've started playing
                    {
                        yield return new WaitForSeconds(0.05f);
                        emptyQueueWaitTime += 0.05f;

                        // If we've waited too long with no new audio, consider playback complete
                        if (emptyQueueWaitTime >= maxEmptyQueueWait)
                        {
                            break;
                        }
                    }
                    else
                    {
                        // Haven't started yet, just wait briefly
                        yield return new WaitForSeconds(0.01f);
                        emptyQueueWaitTime += 0.01f;
                        
                        if (emptyQueueWaitTime >= 0.5f)
                        {
                            // No audio received for 500ms, exit
                            break;
                        }
                    }
                }
            }

            // All clips played
            _isProcessingQueue = false;
            _currentlyPlayingMessageId = null;
            _playbackCoroutine = null;

            // Fire complete event only if we actually played something
            if (!isFirstClip)
            {
                OnPlaybackComplete?.Invoke();
                onPlaybackComplete?.Invoke();

                Debug.Log("[EstuaryAudioSource] Playback complete");

                // Notify server that playback is complete
                NotifyPlaybackComplete();
            }
        }

        private async void NotifyPlaybackComplete()
        {
            try
            {
                if (EstuaryManager.HasInstance && EstuaryManager.Instance.IsConnected)
                {
                    await EstuaryManager.Instance.NotifyAudioPlaybackCompleteAsync();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EstuaryAudioSource] Failed to notify playback complete: {e.Message}");
            }
        }

        private void HandleUserSpeechDetected()
        {
            if (autoInterruptOnUserSpeech && IsPlaying)
            {
                Debug.Log("[EstuaryAudioSource] User speech detected, interrupting playback and notifying server");
                StopPlayback();
                
                // CRITICAL: Also notify the server to stop generating audio
                // Without this, the server keeps generating TTS even though we stopped playback locally
                NotifyServerInterrupt();
            }
        }

        private async void NotifyServerInterrupt()
        {
            try
            {
                if (EstuaryManager.HasInstance && EstuaryManager.Instance.IsConnected)
                {
                    await EstuaryManager.Instance.NotifyInterruptAsync(CurrentMessageId);
                    Debug.Log($"[EstuaryAudioSource] Notified server of interrupt (messageId: {CurrentMessageId ?? "none"})");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EstuaryAudioSource] Failed to notify server of interrupt: {e.Message}");
            }
        }

        #endregion
    }
}






