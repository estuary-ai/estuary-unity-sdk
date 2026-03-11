using System;
using System.Collections;
using UnityEngine;
using LiveKit;

namespace Estuary
{
    /// <summary>
    /// Android audio source type for AudioRecord.
    /// VOICE_RECOGNITION is optimized for near-field voice capture with ambient noise rejection.
    /// </summary>
    public enum AndroidAudioSource
    {
        /// <summary>
        /// Default audio source - may pick up ambient sounds.
        /// </summary>
        Default = 0,

        /// <summary>
        /// Microphone audio source - standard microphone input.
        /// </summary>
        Mic = 1,

        /// <summary>
        /// Voice recognition optimized - near-field capture, rejects distant sounds.
        /// Best for voice assistants and speech-to-text.
        /// </summary>
        VoiceRecognition = 6,

        /// <summary>
        /// Voice communication optimized - includes AEC.
        /// Best for VoIP calls.
        /// </summary>
        VoiceCommunication = 7,

        /// <summary>
        /// Unprocessed audio - raw microphone input without any processing.
        /// </summary>
        Unprocessed = 9
    }

    /// <summary>
    /// OBSOLETE: This class does not work due to Unity JNI limitations.
    /// 
    /// Unity's AndroidJavaObject.Call() does not support output parameters for arrays.
    /// When calling AudioRecord.read(buffer, offset, size), the buffer is copied to Java,
    /// filled with audio data, but the modified data is NEVER copied back to C#.
    /// The C# buffer always remains empty/zeroed.
    /// 
    /// Use DirectMicrophoneSource instead, which uses Unity's built-in Microphone API
    /// and works reliably on all platforms including Android.
    /// 
    /// Original description (for historical reference):
    /// A microphone audio source that uses Android's native AudioRecord API directly.
    /// This was intended to provide access to VOICE_RECOGNITION audio source for
    /// near-field voice capture with ambient noise rejection.
    /// </summary>
    [Obsolete("AndroidMicrophoneSource does not work due to Unity JNI array marshalling limitations. Use DirectMicrophoneSource instead.")]
    public sealed class AndroidMicrophoneSource : RtcAudioSource
    {
        private const int SAMPLE_RATE = 16000;
        private const int CHANNELS = 1; // Mono for voice, will be converted to stereo
        private const int BITS_PER_SAMPLE = 16;
        private const float POLL_INTERVAL_MS = 20f;

        // Android AudioRecord constants
        private const int CHANNEL_IN_MONO = 16; // AudioFormat.CHANNEL_IN_MONO
        private const int ENCODING_PCM_16BIT = 2; // AudioFormat.ENCODING_PCM_16BIT
        private const int STATE_INITIALIZED = 1; // AudioRecord.STATE_INITIALIZED
        private const int RECORDSTATE_RECORDING = 3; // AudioRecord.RECORDSTATE_RECORDING

        private readonly MonoBehaviour _coroutineRunner;
        private readonly AndroidAudioSource _audioSource;

        private bool _started;
        private bool _disposed;
        private Coroutine _pollCoroutine;

#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _audioRecord;
        private int _audioSessionId;
        private int _bufferSize;
        private sbyte[] _byteBuffer; // Use sbyte[] for proper JNI marshalling (Java byte is signed)
        private float[] _floatBuffer;
#endif

        public override event Action<float[], int, int> AudioRead;

        /// <summary>
        /// The audio session ID used by the AudioRecord instance.
        /// </summary>
        public int AudioSessionId
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return _audioSessionId;
#else
                return 0;
#endif
            }
        }

        /// <summary>
        /// Creates a new Android microphone source using native AudioRecord.
        /// </summary>
        /// <param name="coroutineRunner">MonoBehaviour to run the polling coroutine on</param>
        /// <param name="audioSource">The Android audio source type to use</param>
        public AndroidMicrophoneSource(MonoBehaviour coroutineRunner, AndroidAudioSource audioSource = AndroidAudioSource.VoiceRecognition)
            : base(2, RtcAudioSourceType.AudioSourceMicrophone) // Output stereo for LiveKit
        {
            _coroutineRunner = coroutineRunner;
            _audioSource = audioSource;
        }

        /// <summary>
        /// Begins capturing audio from the microphone using Android AudioRecord.
        /// </summary>
        public override void Start()
        {
            if (_started || _disposed) return;

            base.Start();

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                // Check and request microphone permission first
                if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
                {
                    Debug.Log("[AndroidMicrophoneSource] Requesting microphone permission...");
                    UnityEngine.Android.Permission.RequestUserPermission(UnityEngine.Android.Permission.Microphone);
                    // Permission request is async, we need to wait and retry
                    if (_coroutineRunner != null)
                    {
                        _coroutineRunner.StartCoroutine(WaitForPermissionAndStart());
                    }
                    return;
                }

                StartAudioRecordInternal();
            }
            catch (Exception e)
            {
                Debug.LogError($"[AndroidMicrophoneSource] Failed to start: {e.Message}\n{e.StackTrace}");
            }
#else
            Debug.LogWarning("[AndroidMicrophoneSource] Only available on Android devices");
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private IEnumerator WaitForPermissionAndStart()
        {
            // Wait a few frames for the permission dialog to appear and be responded to
            float timeout = 10f;
            float elapsed = 0f;
            
            while (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone) && elapsed < timeout)
            {
                yield return new WaitForSeconds(0.5f);
                elapsed += 0.5f;
            }

            if (UnityEngine.Android.Permission.HasUserAuthorizedPermission(UnityEngine.Android.Permission.Microphone))
            {
                Debug.Log("[AndroidMicrophoneSource] Microphone permission granted, starting audio capture");
                StartAudioRecordInternal();
            }
            else
            {
                Debug.LogError("[AndroidMicrophoneSource] Microphone permission denied");
            }
        }

        private void StartAudioRecordInternal()
        {
            if (_started || _disposed) return;

            try
            {
                Debug.Log($"[AndroidMicrophoneSource] Starting with audio source: {_audioSource}");

                // Get minimum buffer size
                using (var audioRecordClass = new AndroidJavaClass("android.media.AudioRecord"))
                {
                    _bufferSize = audioRecordClass.CallStatic<int>(
                        "getMinBufferSize",
                        SAMPLE_RATE,
                        CHANNEL_IN_MONO,
                        ENCODING_PCM_16BIT
                    );

                    if (_bufferSize <= 0)
                    {
                        Debug.LogError($"[AndroidMicrophoneSource] Invalid buffer size: {_bufferSize}");
                        return;
                    }

                    // Use larger buffer for stability
                    _bufferSize = Math.Max(_bufferSize * 2, SAMPLE_RATE / 10); // At least 100ms buffer
                    Debug.Log($"[AndroidMicrophoneSource] Buffer size: {_bufferSize} samples");

                    // Create AudioRecord with specified audio source
                    _audioRecord = new AndroidJavaObject(
                        "android.media.AudioRecord",
                        (int)_audioSource,    // audioSource
                        SAMPLE_RATE,          // sampleRateInHz
                        CHANNEL_IN_MONO,      // channelConfig
                        ENCODING_PCM_16BIT,   // audioFormat
                        _bufferSize * 2       // bufferSizeInBytes (16-bit = 2 bytes per sample)
                    );

                    // Check if initialization succeeded
                    int state = _audioRecord.Call<int>("getState");
                    if (state != STATE_INITIALIZED)
                    {
                        Debug.LogError($"[AndroidMicrophoneSource] AudioRecord initialization failed, state: {state}");
                        _audioRecord.Dispose();
                        _audioRecord = null;
                        return;
                    }

                    // Get the audio session ID
                    _audioSessionId = _audioRecord.Call<int>("getAudioSessionId");
                    Debug.Log($"[AndroidMicrophoneSource] AudioRecord initialized, session ID: {_audioSessionId}");

                    // Allocate byte buffer for reading audio data
                    // Using sbyte[] because Java's byte type is signed and Unity's JNI bridge expects sbyte[]
                    // Each sample is 16-bit (2 bytes), so buffer size in bytes is _bufferSize * 2
                    _byteBuffer = new sbyte[_bufferSize * 2];
                    _floatBuffer = new float[_bufferSize * 2]; // Stereo output

                    // Start recording
                    _audioRecord.Call("startRecording");

                    int recordState = _audioRecord.Call<int>("getRecordingState");
                    if (recordState != RECORDSTATE_RECORDING)
                    {
                        Debug.LogError($"[AndroidMicrophoneSource] Failed to start recording, state: {recordState}");
                        _audioRecord.Call("release");
                        _audioRecord.Dispose();
                        _audioRecord = null;
                        return;
                    }

                    Debug.Log($"[AndroidMicrophoneSource] Recording started with {_audioSource}");
                }

                _started = true;

                // Start polling coroutine
                if (_coroutineRunner != null)
                {
                    _pollCoroutine = _coroutineRunner.StartCoroutine(PollAudioCoroutine());
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[AndroidMicrophoneSource] Failed to start audio record: {e.Message}\n{e.StackTrace}");
            }
        }
#endif

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

#if UNITY_ANDROID && !UNITY_EDITOR
            // Stop and release AudioRecord
            if (_audioRecord != null)
            {
                try
                {
                    _audioRecord.Call("stop");
                    _audioRecord.Call("release");
                    _audioRecord.Dispose();
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[AndroidMicrophoneSource] Error stopping AudioRecord: {e.Message}");
                }
                _audioRecord = null;
            }

            _audioSessionId = 0;
#endif

            _started = false;
            Debug.Log("[AndroidMicrophoneSource] Stopped");
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private IEnumerator PollAudioCoroutine()
        {
            var waitTime = new WaitForSeconds(POLL_INTERVAL_MS / 1000f);

            while (_started && !_disposed && _audioRecord != null)
            {
                PollAudio();
                yield return waitTime;
            }
        }

        private void PollAudio()
        {
            if (_audioRecord == null || !_started || _byteBuffer == null) return;

            try
            {
                // Read audio data from AudioRecord as bytes
                // Using byte[] because Unity's JNI bridge properly marshals byte arrays
                int bytesRead = _audioRecord.Call<int>("read", _byteBuffer, 0, _byteBuffer.Length);

                if (bytesRead <= 0)
                {
                    if (bytesRead < 0)
                    {
                        Debug.LogWarning($"[AndroidMicrophoneSource] Read error: {bytesRead}");
                    }
                    return;
                }

                // Convert bytes to samples (16-bit PCM = 2 bytes per sample)
                int samplesRead = bytesRead / 2;
                
                // Convert byte[] (16-bit PCM little-endian) to float[] and mono to stereo
                int stereoSamples = samplesRead * 2;
                if (_floatBuffer.Length < stereoSamples)
                {
                    _floatBuffer = new float[stereoSamples];
                }

                for (int i = 0; i < samplesRead; i++)
                {
                    // Convert two bytes to a 16-bit signed integer (little-endian)
                    // sbyte is already signed, so we need to handle the conversion carefully
                    int byteIndex = i * 2;
                    // Convert sbytes to unsigned values for combining into 16-bit sample
                    int lowByte = _byteBuffer[byteIndex] & 0xFF;
                    int highByte = _byteBuffer[byteIndex + 1];
                    short sample16 = (short)(lowByte | (highByte << 8));
                    float sample = sample16 / 32768f; // Convert 16-bit to float [-1, 1]
                    
                    // Duplicate mono to stereo
                    _floatBuffer[i * 2] = sample;
                    _floatBuffer[i * 2 + 1] = sample;
                }

                // Create properly sized array for the event
                float[] audioData = new float[stereoSamples];
                Array.Copy(_floatBuffer, audioData, stereoSamples);

                // Fire the AudioRead event
                AudioRead?.Invoke(audioData, 2, SAMPLE_RATE);
            }
            catch (Exception e)
            {
                Debug.LogError($"[AndroidMicrophoneSource] Poll error: {e.Message}");
            }
        }
#endif

        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                Stop();
            }
            _disposed = true;
            base.Dispose(disposing);
        }

        ~AndroidMicrophoneSource()
        {
            Dispose(false);
        }
    }
}
