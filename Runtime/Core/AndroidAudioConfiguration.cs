using UnityEngine;

namespace Estuary
{
    /// <summary>
    /// Android audio configuration mode for voice chat.
    /// </summary>
    public enum AndroidAudioMode
    {
        /// <summary>
        /// Full voice communication mode with MODE_IN_COMMUNICATION.
        /// Best AEC but may cause audio playback issues on some devices.
        /// </summary>
        VoiceCommunication,
        
        /// <summary>
        /// Normal mode with audio focus for voice.
        /// Less intrusive, LiveKit handles its own audio playback.
        /// May have reduced AEC quality but better audio streaming.
        /// </summary>
        Normal,
        
        /// <summary>
        /// Skip all Android audio configuration, let LiveKit handle everything.
        /// Use this if you experience choppy audio with other modes.
        /// </summary>
        None
    }

    /// <summary>
    /// Android-specific audio configuration for voice chat with AEC (Acoustic Echo Cancellation).
    /// 
    /// On Android, proper AEC requires configuring the audio session for voice communication mode.
    /// This class provides utilities to configure Android's audio system for optimal voice chat.
    /// 
    /// Note: This uses Unity's AndroidJavaClass to call Android APIs directly.
    /// For Ray Neo X3 Pro and other Android AR devices, this ensures hardware AEC is enabled.
    /// 
    /// IMPORTANT: If you experience choppy audio, try using AndroidAudioMode.Normal or AndroidAudioMode.None
    /// as MODE_IN_COMMUNICATION can interfere with LiveKit's audio buffering on some devices.
    /// </summary>
    public static class AndroidAudioConfiguration
    {
        private static bool _isConfigured = false;
        private static bool _previousSpeakerphoneState = false;
        private static AndroidAudioMode _currentMode = AndroidAudioMode.None;
        
        /// <summary>
        /// The audio mode to use. Default is VoiceCommunication for best AEC.
        /// Change to Normal or None if experiencing choppy audio.
        /// </summary>
        public static AndroidAudioMode PreferredMode { get; set; } = AndroidAudioMode.VoiceCommunication;

#if UNITY_ANDROID && !UNITY_EDITOR
        private static AndroidJavaObject _audioManager;
        private static AndroidJavaObject _audioFocusRequest;
        private const int MODE_NORMAL = 0; // AudioManager.MODE_NORMAL
        private const int MODE_IN_COMMUNICATION = 3; // AudioManager.MODE_IN_COMMUNICATION
        private const int STREAM_VOICE_CALL = 0; // AudioManager.STREAM_VOICE_CALL
        private const int STREAM_MUSIC = 3; // AudioManager.STREAM_MUSIC
        private const int AUDIOFOCUS_GAIN_TRANSIENT = 2;
        private const int AUDIOFOCUS_GAIN = 1;
#endif

        /// <summary>
        /// Configure Android audio for voice communication with AEC enabled.
        /// Call this before starting microphone capture.
        /// Uses PreferredMode to determine the configuration style.
        /// </summary>
        /// <returns>True if configuration was successful</returns>
        public static bool ConfigureForVoiceChat()
        {
            if (_isConfigured)
            {
                Debug.Log("[AndroidAudioConfiguration] Already configured for voice chat");
                return true;
            }

            // Check if we should skip configuration entirely
            if (PreferredMode == AndroidAudioMode.None)
            {
                Debug.Log("[AndroidAudioConfiguration] Mode is None, skipping Android audio configuration (LiveKit will handle audio)");
                _isConfigured = true;
                _currentMode = AndroidAudioMode.None;
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
                    
                    // Log current audio state for debugging
                    int currentMode = _audioManager.Call<int>("getMode");
                    Debug.Log($"[AndroidAudioConfiguration] Current audio mode: {currentMode}, speakerphone: {_previousSpeakerphoneState}");

                    if (PreferredMode == AndroidAudioMode.VoiceCommunication)
                    {
                        // Full voice communication mode - best AEC but may cause playback issues
                        // Set audio mode to MODE_IN_COMMUNICATION for voice chat
                        // This enables hardware AEC on most Android devices
                        _audioManager.Call("setMode", MODE_IN_COMMUNICATION);
                        
                        // CRITICAL: Enable speakerphone to route audio to speakers instead of earpiece
                        // MODE_IN_COMMUNICATION defaults to earpiece output, which doesn't exist on AR glasses
                        _audioManager.Call("setSpeakerphoneOn", true);
                        
                        // Request audio focus for voice communication
                        // This tells Android we're doing a voice call, enabling AEC
                        int focusResult = _audioManager.Call<int>(
                            "requestAudioFocus",
                            null, // AudioFocusChangeListener
                            STREAM_VOICE_CALL,
                            AUDIOFOCUS_GAIN_TRANSIENT
                        );

                        _isConfigured = true;
                        _currentMode = AndroidAudioMode.VoiceCommunication;
                        Debug.Log("[AndroidAudioConfiguration] Configured for voice chat (MODE_IN_COMMUNICATION)");
                        Debug.Log($"[AndroidAudioConfiguration] Audio focus result: {focusResult}");
                        Debug.Log("[AndroidAudioConfiguration] NOTE: If audio is choppy, try AndroidAudioMode.Normal or AndroidAudioMode.None");
                    }
                    else if (PreferredMode == AndroidAudioMode.Normal)
                    {
                        // Normal mode - less intrusive, lets LiveKit handle audio output
                        // Keep audio mode as normal but request audio focus for better microphone priority
                        _audioManager.Call("setMode", MODE_NORMAL);
                        
                        // Request audio focus (non-transient) to prevent interruptions
                        // Use STREAM_MUSIC to not interfere with LiveKit's audio output
                        int focusResult = _audioManager.Call<int>(
                            "requestAudioFocus",
                            null, // AudioFocusChangeListener
                            STREAM_MUSIC,
                            AUDIOFOCUS_GAIN
                        );

                        _isConfigured = true;
                        _currentMode = AndroidAudioMode.Normal;
                        Debug.Log("[AndroidAudioConfiguration] Configured for voice chat (MODE_NORMAL)");
                        Debug.Log($"[AndroidAudioConfiguration] Audio focus result: {focusResult}");
                        Debug.Log("[AndroidAudioConfiguration] AEC may be limited in this mode, but audio streaming should be smoother");
                    }

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
            _isConfigured = true;
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
        /// Get information about device audio capabilities for debugging.
        /// </summary>
        public static string GetAudioCapabilitiesInfo()
        {
            var info = new System.Text.StringBuilder();
            info.AppendLine("=== Audio Capabilities ===");
            info.AppendLine($"Platform: {Application.platform}");
            info.AppendLine($"Preferred Audio Mode: {PreferredMode}");
            info.AppendLine($"Current Audio Mode: {_currentMode}");
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
            info.AppendLine("TIP: If audio is choppy, try setting AndroidAudioConfiguration.PreferredMode = AndroidAudioMode.Normal or AndroidAudioMode.None");
            
            return info.ToString();
        }
    }
}
