using UnityEngine;

namespace Estuary
{
    /// <summary>
    /// Android-specific audio configuration for voice chat with AEC (Acoustic Echo Cancellation).
    /// 
    /// On Android, proper AEC requires configuring the audio session for voice communication mode.
    /// This class provides utilities to configure Android's audio system for optimal voice chat.
    /// 
    /// Note: This uses Unity's AndroidJavaClass to call Android APIs directly.
    /// For Ray Neo X3 Pro and other Android AR devices, this ensures hardware AEC is enabled.
    /// </summary>
    public static class AndroidAudioConfiguration
    {
        private static bool _isConfigured = false;
        private static bool _previousSpeakerphoneState = false;

#if UNITY_ANDROID && !UNITY_EDITOR
        private static AndroidJavaObject _audioManager;
        private static AndroidJavaObject _audioFocusRequest;
        private const int MODE_IN_COMMUNICATION = 3; // AudioManager.MODE_IN_COMMUNICATION
        private const int STREAM_VOICE_CALL = 0; // AudioManager.STREAM_VOICE_CALL
        private const int AUDIOFOCUS_GAIN_TRANSIENT = 2;
#endif

        /// <summary>
        /// Configure Android audio for voice communication with AEC enabled.
        /// Call this before starting microphone capture.
        /// </summary>
        /// <returns>True if configuration was successful</returns>
        public static bool ConfigureForVoiceChat()
        {
            if (_isConfigured)
            {
                Debug.Log("[AndroidAudioConfiguration] Already configured for voice chat");
                return true;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var context = currentActivity.Call<AndroidJavaObject>("getApplicationContext"))
                {
                    // Get AudioManager
                    _audioManager = context.Call<AndroidJavaObject>(
                        "getSystemService",
                        "audio"
                    );

                    if (_audioManager == null)
                    {
                        Debug.LogError("[AndroidAudioConfiguration] Failed to get AudioManager");
                        return false;
                    }

                    // Store current speakerphone state
                    _previousSpeakerphoneState = _audioManager.Call<bool>("isSpeakerphoneOn");

                    // Set audio mode to MODE_IN_COMMUNICATION for voice chat
                    // This enables hardware AEC on most Android devices
                    _audioManager.Call("setMode", MODE_IN_COMMUNICATION);
                    
                    // Request audio focus for voice communication
                    // This tells Android we're doing a voice call, enabling AEC
                    int focusResult = _audioManager.Call<int>(
                        "requestAudioFocus",
                        null, // AudioFocusChangeListener
                        STREAM_VOICE_CALL,
                        AUDIOFOCUS_GAIN_TRANSIENT
                    );

                    _isConfigured = true;
                    Debug.Log("[AndroidAudioConfiguration] Configured for voice chat (MODE_IN_COMMUNICATION)");
                    Debug.Log($"[AndroidAudioConfiguration] Audio focus result: {focusResult}");
                    return true;
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[AndroidAudioConfiguration] Failed to configure: {e.Message}");
                return false;
            }
#else
            Debug.Log("[AndroidAudioConfiguration] Not on Android, skipping configuration");
            return true;
#endif
        }

        /// <summary>
        /// Reset Android audio configuration to default state.
        /// Call this when voice chat ends.
        /// </summary>
        public static void ResetConfiguration()
        {
            if (!_isConfigured)
                return;

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (_audioManager != null)
                {
                    // Reset audio mode to normal
                    _audioManager.Call("setMode", 0); // MODE_NORMAL
                    
                    // Restore speakerphone state
                    _audioManager.Call("setSpeakerphoneOn", _previousSpeakerphoneState);
                    
                    // Abandon audio focus
                    _audioManager.Call<int>("abandonAudioFocus", (AndroidJavaObject)null);

                    _audioManager.Dispose();
                    _audioManager = null;
                }

                _isConfigured = false;
                Debug.Log("[AndroidAudioConfiguration] Reset to default configuration");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[AndroidAudioConfiguration] Failed to reset: {e.Message}");
            }
#else
            _isConfigured = false;
#endif
        }

        /// <summary>
        /// Check if AEC (Acoustic Echo Cancellation) is available on this device.
        /// </summary>
        /// <returns>True if AEC is available</returns>
        public static bool IsAecAvailable()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var aecClass = new AndroidJavaClass("android.media.audiofx.AcousticEchoCanceler"))
                {
                    return aecClass.CallStatic<bool>("isAvailable");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[AndroidAudioConfiguration] Failed to check AEC availability: {e.Message}");
                return false;
            }
#else
            return true; // Assume available on non-Android platforms
#endif
        }

        /// <summary>
        /// Check if noise suppression is available on this device.
        /// </summary>
        /// <returns>True if noise suppression is available</returns>
        public static bool IsNoiseSuppressionAvailable()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var nsClass = new AndroidJavaClass("android.media.audiofx.NoiseSuppressor"))
                {
                    return nsClass.CallStatic<bool>("isAvailable");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[AndroidAudioConfiguration] Failed to check NS availability: {e.Message}");
                return false;
            }
#else
            return true;
#endif
        }

        /// <summary>
        /// Get information about device audio capabilities for debugging.
        /// </summary>
        public static string GetAudioCapabilitiesInfo()
        {
            var info = new System.Text.StringBuilder();
            info.AppendLine("=== Audio Capabilities ===");
            info.AppendLine($"Platform: {Application.platform}");
            info.AppendLine($"AEC Available: {IsAecAvailable()}");
            info.AppendLine($"Noise Suppression Available: {IsNoiseSuppressionAvailable()}");
            info.AppendLine($"Microphone Devices: {Microphone.devices.Length}");
            
            foreach (var device in Microphone.devices)
            {
                int minFreq, maxFreq;
                Microphone.GetDeviceCaps(device, out minFreq, out maxFreq);
                info.AppendLine($"  - {device}: {minFreq}Hz - {maxFreq}Hz");
            }
            
            return info.ToString();
        }
    }
}
