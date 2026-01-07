using System;
using System.Text;
using UnityEngine;

namespace Estuary.Utilities
{
    /// <summary>
    /// Utility class for Base64 encoding/decoding operations.
    /// </summary>
    public static class Base64Helper
    {
        /// <summary>
        /// Encode bytes to Base64 string.
        /// </summary>
        /// <param name="data">Byte array to encode</param>
        /// <returns>Base64 encoded string</returns>
        public static string Encode(byte[] data)
        {
            if (data == null || data.Length == 0)
                return string.Empty;

            return Convert.ToBase64String(data);
        }

        /// <summary>
        /// Decode Base64 string to bytes.
        /// </summary>
        /// <param name="base64">Base64 encoded string</param>
        /// <returns>Decoded byte array, or empty array if decoding fails</returns>
        public static byte[] Decode(string base64)
        {
            if (string.IsNullOrEmpty(base64))
                return Array.Empty<byte>();

            try
            {
                return Convert.FromBase64String(base64);
            }
            catch (FormatException e)
            {
                Debug.LogError($"[Base64Helper] Failed to decode Base64: {e.Message}");
                return Array.Empty<byte>();
            }
        }

        /// <summary>
        /// Try to decode Base64 string to bytes.
        /// </summary>
        /// <param name="base64">Base64 encoded string</param>
        /// <param name="result">Decoded bytes if successful</param>
        /// <returns>True if decoding succeeded</returns>
        public static bool TryDecode(string base64, out byte[] result)
        {
            result = null;

            if (string.IsNullOrEmpty(base64))
                return false;

            try
            {
                result = Convert.FromBase64String(base64);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        /// <summary>
        /// Encode string to Base64.
        /// </summary>
        /// <param name="text">String to encode</param>
        /// <param name="encoding">Text encoding (default: UTF8)</param>
        /// <returns>Base64 encoded string</returns>
        public static string EncodeString(string text, Encoding encoding = null)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            encoding ??= Encoding.UTF8;
            var bytes = encoding.GetBytes(text);
            return Convert.ToBase64String(bytes);
        }

        /// <summary>
        /// Decode Base64 to string.
        /// </summary>
        /// <param name="base64">Base64 encoded string</param>
        /// <param name="encoding">Text encoding (default: UTF8)</param>
        /// <returns>Decoded string, or empty string if decoding fails</returns>
        public static string DecodeString(string base64, Encoding encoding = null)
        {
            var bytes = Decode(base64);
            if (bytes.Length == 0)
                return string.Empty;

            encoding ??= Encoding.UTF8;
            return encoding.GetString(bytes);
        }

        /// <summary>
        /// Encode float audio samples to Base64 (as 16-bit PCM).
        /// </summary>
        /// <param name="samples">Audio samples</param>
        /// <returns>Base64 encoded PCM16 audio</returns>
        public static string EncodeAudio(float[] samples)
        {
            var pcmBytes = AudioConverter.FloatToPCM16(samples);
            return Encode(pcmBytes);
        }

        /// <summary>
        /// Decode Base64 audio (16-bit PCM) to float samples.
        /// </summary>
        /// <param name="base64">Base64 encoded PCM16 audio</param>
        /// <returns>Float audio samples</returns>
        public static float[] DecodeAudio(string base64)
        {
            var pcmBytes = Decode(base64);
            return AudioConverter.PCM16ToFloat(pcmBytes);
        }

        /// <summary>
        /// Check if a string is valid Base64.
        /// </summary>
        /// <param name="base64">String to check</param>
        /// <returns>True if valid Base64</returns>
        public static bool IsValidBase64(string base64)
        {
            if (string.IsNullOrEmpty(base64))
                return false;

            // Check length (must be multiple of 4)
            if (base64.Length % 4 != 0)
                return false;

            try
            {
                Convert.FromBase64String(base64);
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        /// <summary>
        /// Get the decoded byte length without actually decoding.
        /// </summary>
        /// <param name="base64">Base64 string</param>
        /// <returns>Estimated decoded length in bytes</returns>
        public static int GetDecodedLength(string base64)
        {
            if (string.IsNullOrEmpty(base64))
                return 0;

            var padding = 0;
            if (base64.EndsWith("=="))
                padding = 2;
            else if (base64.EndsWith("="))
                padding = 1;

            return (base64.Length * 3 / 4) - padding;
        }
    }
}






