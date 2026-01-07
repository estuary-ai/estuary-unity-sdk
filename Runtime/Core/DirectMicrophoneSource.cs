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
    /// - Noise Suppression is still enabled
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
        
        public override event Action<float[], int, int> AudioRead;
        
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
            
            if (!Application.HasUserAuthorization(UserAuthorization.Microphone))
            {
                Debug.LogError("[DirectMicrophoneSource] Microphone access not authorized");
                return;
            }
            
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
            
            // Fire the AudioRead event with the CORRECT sample rate (48000)
            // This goes to RtcAudioSource.OnAudioRead which sends to native FFI with AEC
            AudioRead?.Invoke(audioData, CHANNELS, SAMPLE_RATE);
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



