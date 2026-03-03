using System.Runtime.InteropServices;
using UnityEngine;

namespace Estuary
{
    /// <summary>
    /// iOS-specific audio configuration for voice chat with AEC (Acoustic Echo Cancellation).
    ///
    /// On iOS, proper AEC requires configuring AVAudioSession for voice communication mode.
    /// This class provides utilities to configure the iOS audio session for optimal voice chat
    /// by setting the category to PlayAndRecord with VoiceChat mode, which enables hardware AEC.
    ///
    /// This is the iOS equivalent of <see cref="AndroidAudioConfiguration"/>.
    ///
    /// Note: This uses native Objective-C++ calls via iOSAudioBridge.mm plugin.
    /// Hardware AEC is always available on iOS when using VoiceChat mode.
    /// </summary>
    public static class iOSAudioConfiguration
    {
        private static bool _isConfigured = false;

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern int EstuaryiOS_ConfigureForVoiceChat();

        [DllImport("__Internal")]
        private static extern int EstuaryiOS_ResetAudioConfiguration();

        [DllImport("__Internal")]
        private static extern int EstuaryiOS_IsAecAvailable();
#endif

        /// <summary>
        /// Configure iOS audio for voice communication with AEC enabled.
        /// Sets AVAudioSession to PlayAndRecord category with VoiceChat mode.
        /// Call this before starting microphone capture.
        /// </summary>
        /// <returns>True if configuration was successful</returns>
        public static bool ConfigureForVoiceChat()
        {
            if (_isConfigured)
            {
                Debug.Log("[iOSAudioConfiguration] Already configured for voice chat");
                return true;
            }

#if UNITY_IOS && !UNITY_EDITOR
            try
            {
                int result = EstuaryiOS_ConfigureForVoiceChat();
                if (result == 1)
                {
                    _isConfigured = true;
                    Debug.Log("[iOSAudioConfiguration] Configured for voice chat (AVAudioSession VoiceChat mode)");
                    return true;
                }
                else
                {
                    Debug.LogError("[iOSAudioConfiguration] Failed to configure AVAudioSession for voice chat");
                    return false;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[iOSAudioConfiguration] Failed to configure: {e.Message}");
                return false;
            }
#else
            Debug.Log("[iOSAudioConfiguration] Not on iOS device, skipping configuration");
            _isConfigured = true;
            return true;
#endif
        }

        /// <summary>
        /// Reset iOS audio configuration to default state.
        /// Sets AVAudioSession back to Playback category with Default mode.
        /// Call this when voice chat ends.
        /// </summary>
        public static void ResetConfiguration()
        {
            if (!_isConfigured)
                return;

#if UNITY_IOS && !UNITY_EDITOR
            try
            {
                int result = EstuaryiOS_ResetAudioConfiguration();
                if (result == 1)
                {
                    Debug.Log("[iOSAudioConfiguration] Reset to default configuration");
                }
                else
                {
                    Debug.LogError("[iOSAudioConfiguration] Failed to reset AVAudioSession");
                }

                _isConfigured = false;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[iOSAudioConfiguration] Failed to reset: {e.Message}");
            }
#else
            _isConfigured = false;
#endif
        }

        /// <summary>
        /// Check if AEC (Acoustic Echo Cancellation) is available on this device.
        /// On iOS, hardware AEC is always available when using VoiceChat mode.
        /// </summary>
        /// <returns>True if AEC is available</returns>
        public static bool IsAecAvailable()
        {
#if UNITY_IOS && !UNITY_EDITOR
            try
            {
                return EstuaryiOS_IsAecAvailable() == 1;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[iOSAudioConfiguration] Failed to check AEC availability: {e.Message}");
                return false;
            }
#else
            return true; // Assume available on non-iOS platforms
#endif
        }

        /// <summary>
        /// Get information about device audio capabilities for debugging.
        /// </summary>
        public static string GetAudioCapabilitiesInfo()
        {
            var info = new System.Text.StringBuilder();
            info.AppendLine("=== iOS Audio Capabilities ===");
            info.AppendLine($"Platform: {Application.platform}");
            info.AppendLine($"Is Configured: {_isConfigured}");
            info.AppendLine($"AEC Available: {IsAecAvailable()}");
            info.AppendLine($"Microphone Devices: {Microphone.devices.Length}");

            foreach (var device in Microphone.devices)
            {
                int minFreq, maxFreq;
                Microphone.GetDeviceCaps(device, out minFreq, out maxFreq);
                info.AppendLine($"  - {device}: {minFreq}Hz - {maxFreq}Hz");
            }

            info.AppendLine("");
            info.AppendLine("NOTE: iOS hardware AEC is always available in VoiceChat mode");

            return info.ToString();
        }
    }
}
