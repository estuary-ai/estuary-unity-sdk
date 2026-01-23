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
        private readonly Queue<byte[]> _liveKitAudioQueue = new Queue<byte[]>();
        private int _liveKitSampleRate = 48000;
        private int _liveKitChannels = 1;
        private Coroutine _liveKitPlaybackCoroutine;
        private AudioClip _liveKitStreamClip;
        private float[] _liveKitBuffer;
        private readonly object _liveKitLock = new object();

        // LiveKit streaming buffer
        private const int LIVEKIT_BUFFER_SECONDS = 1;
        private const float LIVEKIT_BUFFER_THRESHOLD = 0.1f; // Start playing when 100ms of audio is buffered

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
            // Stop coroutine
            if (_playbackCoroutine != null)
            {
                StopCoroutine(_playbackCoroutine);
                _playbackCoroutine = null;
            }

            // Stop audio
            if (audioSource != null && audioSource.isPlaying)
            {
                audioSource.Stop();
            }

            // Clear queue
            ClearQueue();

            _isProcessingQueue = false;
            _currentlyPlayingMessageId = null;

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

        #region LiveKit Audio Handling

        private void InitializeLiveKitBuffer()
        {
            // Create a streaming buffer for LiveKit audio
            var bufferSize = _liveKitSampleRate * LIVEKIT_BUFFER_SECONDS;
            _liveKitBuffer = new float[bufferSize];

            lock (_liveKitLock)
            {
                _liveKitAudioQueue.Clear();
            }
        }

        private void CleanupLiveKitBuffer()
        {
            if (_liveKitPlaybackCoroutine != null)
            {
                StopCoroutine(_liveKitPlaybackCoroutine);
                _liveKitPlaybackCoroutine = null;
            }

            if (_liveKitStreamClip != null)
            {
                Destroy(_liveKitStreamClip);
                _liveKitStreamClip = null;
            }

            _liveKitBuffer = null;

            lock (_liveKitLock)
            {
                _liveKitAudioQueue.Clear();
            }
        }

        private void HandleLiveKitAudioReceived(byte[] pcmData, int sampleRate, int channels)
        {
            if (!_useLiveKit || pcmData == null || pcmData.Length == 0)
                return;

            // Update sample rate and channels if different
            if (sampleRate != _liveKitSampleRate || channels != _liveKitChannels)
            {
                _liveKitSampleRate = sampleRate;
                _liveKitChannels = channels;
                InitializeLiveKitBuffer();
            }

            // Queue the audio data
            lock (_liveKitLock)
            {
                _liveKitAudioQueue.Enqueue(pcmData);
            }

            // Start playback coroutine if not already running
            if (_liveKitPlaybackCoroutine == null)
            {
                _liveKitPlaybackCoroutine = StartCoroutine(ProcessLiveKitAudioCoroutine());
            }
        }

        private IEnumerator ProcessLiveKitAudioCoroutine()
        {
            var isFirstChunk = true;

            while (_useLiveKit)
            {
                byte[] audioData = null;

                lock (_liveKitLock)
                {
                    if (_liveKitAudioQueue.Count > 0)
                    {
                        audioData = _liveKitAudioQueue.Dequeue();
                    }
                }

                if (audioData != null)
                {
                    // Convert PCM16 bytes to float samples
                    var samples = AudioConverter.PCM16ToFloat(audioData);

                    if (samples.Length > 0)
                    {
                        // Create AudioClip for this chunk
                        var clip = AudioConverter.CreateAudioClip(samples, _liveKitSampleRate, _liveKitChannels, "LiveKitAudio");

                        if (clip != null)
                        {
                            // Fire started event on first chunk
                            if (isFirstChunk)
                            {
                                isFirstChunk = false;
                                OnPlaybackStarted?.Invoke();
                                onPlaybackStarted?.Invoke();
                                Debug.Log("[EstuaryAudioSource] Started LiveKit audio playback");
                            }

                            // Play the clip
                            audioSource.clip = clip;
                            audioSource.Play();

                            // Wait for clip to finish
                            while (audioSource.isPlaying)
                            {
                                yield return null;
                            }

                            // Cleanup
                            Destroy(clip);
                        }
                    }
                }
                else
                {
                    // No audio in queue, wait a bit and check if there's more coming
                    yield return new WaitForSeconds(0.05f);

                    // Check if queue is still empty after waiting
                    bool queueEmpty;
                    lock (_liveKitLock)
                    {
                        queueEmpty = _liveKitAudioQueue.Count == 0;
                    }

                    if (queueEmpty && !isFirstChunk)
                    {
                        // Playback complete
                        OnPlaybackComplete?.Invoke();
                        onPlaybackComplete?.Invoke();
                        Debug.Log("[EstuaryAudioSource] LiveKit audio playback complete");
                        isFirstChunk = true; // Reset for next playback session
                    }
                }
            }

            _liveKitPlaybackCoroutine = null;
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
                Debug.Log("[EstuaryAudioSource] User speech detected, interrupting playback");
                StopPlayback();
            }
        }

        #endregion
    }
}






