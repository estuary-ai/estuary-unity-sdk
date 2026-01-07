using System;
using UnityEngine;

namespace Estuary.Utilities
{
    /// <summary>
    /// Utility class for audio format conversions.
    /// </summary>
    public static class AudioConverter
    {
        /// <summary>
        /// Default sample rate for recording (matches Deepgram requirements).
        /// </summary>
        public const int DEFAULT_RECORD_SAMPLE_RATE = 16000;

        /// <summary>
        /// Default sample rate for playback (ElevenLabs output).
        /// </summary>
        public const int DEFAULT_PLAYBACK_SAMPLE_RATE = 24000;

        /// <summary>
        /// Convert Unity float samples (-1 to 1) to 16-bit PCM bytes.
        /// </summary>
        /// <param name="floatSamples">Audio samples as float array</param>
        /// <returns>16-bit PCM audio as byte array</returns>
        public static byte[] FloatToPCM16(float[] floatSamples)
        {
            if (floatSamples == null || floatSamples.Length == 0)
                return Array.Empty<byte>();

            var bytes = new byte[floatSamples.Length * 2];

            for (int i = 0; i < floatSamples.Length; i++)
            {
                // Clamp to -1 to 1 range
                var sample = Mathf.Clamp(floatSamples[i], -1f, 1f);

                // Convert to 16-bit signed integer
                var value = (short)(sample * 32767);

                // Little-endian byte order
                bytes[i * 2] = (byte)(value & 0xFF);
                bytes[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
            }

            return bytes;
        }

        /// <summary>
        /// Convert 16-bit PCM bytes to Unity float samples.
        /// </summary>
        /// <param name="pcmBytes">16-bit PCM audio as byte array</param>
        /// <returns>Audio samples as float array</returns>
        public static float[] PCM16ToFloat(byte[] pcmBytes)
        {
            if (pcmBytes == null || pcmBytes.Length == 0)
                return Array.Empty<float>();

            var sampleCount = pcmBytes.Length / 2;
            var floatSamples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                // Little-endian byte order
                var value = (short)(pcmBytes[i * 2] | (pcmBytes[i * 2 + 1] << 8));

                // Convert to float (-1 to 1)
                floatSamples[i] = value / 32768f;
            }

            return floatSamples;
        }

        /// <summary>
        /// Convert MP3 bytes to PCM float samples (requires Unity's audio decoder).
        /// This creates a temporary AudioClip to decode MP3 data.
        /// </summary>
        /// <param name="mp3Bytes">MP3 audio data</param>
        /// <param name="sampleRate">Expected sample rate of the MP3</param>
        /// <returns>Decoded float samples, or empty array if decoding fails</returns>
        public static float[] DecodeMp3ToFloat(byte[] mp3Bytes, int sampleRate = DEFAULT_PLAYBACK_SAMPLE_RATE)
        {
            // Unity doesn't have built-in MP3 decoding for raw bytes
            // For MP3 support, you would need to:
            // 1. Use a third-party library like NAudio or NLayer
            // 2. Save to temp file and use UnityWebRequest
            // 3. Use the WAV format instead

            // For now, assume the backend sends PCM data or use WAV format
            Debug.LogWarning("[AudioConverter] MP3 decoding not implemented. Use PCM or WAV format from backend.");
            return Array.Empty<float>();
        }

        /// <summary>
        /// Create an AudioClip from float samples.
        /// </summary>
        /// <param name="samples">Audio samples</param>
        /// <param name="sampleRate">Sample rate</param>
        /// <param name="channels">Number of channels (1 for mono)</param>
        /// <param name="name">Name for the AudioClip</param>
        /// <returns>AudioClip with the audio data</returns>
        public static AudioClip CreateAudioClip(float[] samples, int sampleRate, int channels = 1, string name = "EstuaryAudio")
        {
            if (samples == null || samples.Length == 0)
                return null;

            var clip = AudioClip.Create(name, samples.Length / channels, channels, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        /// <summary>
        /// Create an AudioClip from PCM16 bytes.
        /// </summary>
        /// <param name="pcmBytes">16-bit PCM audio data</param>
        /// <param name="sampleRate">Sample rate</param>
        /// <param name="channels">Number of channels</param>
        /// <param name="name">Name for the AudioClip</param>
        /// <returns>AudioClip with the audio data</returns>
        public static AudioClip CreateAudioClipFromPCM16(byte[] pcmBytes, int sampleRate, int channels = 1, string name = "EstuaryAudio")
        {
            var samples = PCM16ToFloat(pcmBytes);
            return CreateAudioClip(samples, sampleRate, channels, name);
        }

        /// <summary>
        /// Resample audio from one sample rate to another.
        /// </summary>
        /// <param name="samples">Input samples</param>
        /// <param name="sourceSampleRate">Original sample rate</param>
        /// <param name="targetSampleRate">Desired sample rate</param>
        /// <returns>Resampled audio</returns>
        public static float[] Resample(float[] samples, int sourceSampleRate, int targetSampleRate)
        {
            if (sourceSampleRate == targetSampleRate)
                return samples;

            var ratio = (float)targetSampleRate / sourceSampleRate;
            var newLength = Mathf.RoundToInt(samples.Length * ratio);
            var resampled = new float[newLength];

            for (int i = 0; i < newLength; i++)
            {
                var sourceIndex = i / ratio;
                var index0 = Mathf.FloorToInt(sourceIndex);
                var index1 = Mathf.Min(index0 + 1, samples.Length - 1);
                var t = sourceIndex - index0;

                // Linear interpolation
                resampled[i] = Mathf.Lerp(samples[index0], samples[index1], t);
            }

            return resampled;
        }

        /// <summary>
        /// Get audio data from an AudioClip.
        /// </summary>
        /// <param name="clip">The AudioClip to extract data from</param>
        /// <returns>Float array of audio samples</returns>
        public static float[] GetAudioData(AudioClip clip)
        {
            if (clip == null)
                return Array.Empty<float>();

            var samples = new float[clip.samples * clip.channels];
            clip.GetData(samples, 0);
            return samples;
        }

        /// <summary>
        /// Convert stereo audio to mono by averaging channels.
        /// </summary>
        /// <param name="stereoSamples">Interleaved stereo samples</param>
        /// <returns>Mono samples</returns>
        public static float[] StereoToMono(float[] stereoSamples)
        {
            if (stereoSamples == null || stereoSamples.Length == 0)
                return Array.Empty<float>();

            var monoLength = stereoSamples.Length / 2;
            var monoSamples = new float[monoLength];

            for (int i = 0; i < monoLength; i++)
            {
                monoSamples[i] = (stereoSamples[i * 2] + stereoSamples[i * 2 + 1]) / 2f;
            }

            return monoSamples;
        }

        /// <summary>
        /// Calculate the RMS volume of audio samples.
        /// </summary>
        /// <param name="samples">Audio samples</param>
        /// <returns>RMS volume (0 to 1)</returns>
        public static float CalculateRMS(float[] samples)
        {
            if (samples == null || samples.Length == 0)
                return 0f;

            float sum = 0f;
            foreach (var sample in samples)
            {
                sum += sample * sample;
            }

            return Mathf.Sqrt(sum / samples.Length);
        }

        /// <summary>
        /// Calculate the dB level from RMS.
        /// </summary>
        /// <param name="rms">RMS value</param>
        /// <returns>dB level (typically -60 to 0)</returns>
        public static float RMSToDecibels(float rms)
        {
            if (rms <= 0f)
                return -60f;

            return 20f * Mathf.Log10(rms);
        }
    }
}






