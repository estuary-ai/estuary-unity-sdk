using System;

namespace Estuary
{
    /// <summary>
    /// Static service locator for optional LiveKit integration.
    /// The Estuary.LiveKit assembly registers concrete factory delegates at runtime
    /// via RuntimeInitializeOnLoadMethod in LiveKitRegistrar. Core components use
    /// this bridge to create managers without directly referencing LiveKit types.
    ///
    /// When LiveKit is not installed, IsAvailable returns false and Create methods return null.
    /// </summary>
    public static class LiveKitBridge
    {
        /// <summary>
        /// Whether LiveKit integration is available (factories have been registered).
        /// </summary>
        public static bool IsAvailable => _voiceManagerFactory != null;

        private static Func<ILiveKitVoiceManager> _voiceManagerFactory;
        private static Func<ILiveKitVideoManager> _videoManagerFactory;

        /// <summary>
        /// Register the factory for creating ILiveKitVoiceManager instances.
        /// Called by LiveKitRegistrar in the Estuary.LiveKit assembly.
        /// </summary>
        public static void RegisterVoiceManagerFactory(Func<ILiveKitVoiceManager> factory)
        {
            _voiceManagerFactory = factory;
        }

        /// <summary>
        /// Register the factory for creating ILiveKitVideoManager instances.
        /// Called by LiveKitRegistrar in the Estuary.LiveKit assembly.
        /// </summary>
        public static void RegisterVideoManagerFactory(Func<ILiveKitVideoManager> factory)
        {
            _videoManagerFactory = factory;
        }

        /// <summary>
        /// Create a new ILiveKitVoiceManager instance.
        /// Returns null if LiveKit is not installed.
        /// </summary>
        public static ILiveKitVoiceManager CreateVoiceManager()
        {
            return _voiceManagerFactory?.Invoke();
        }

        /// <summary>
        /// Create a new ILiveKitVideoManager instance.
        /// Returns null if LiveKit is not installed.
        /// </summary>
        public static ILiveKitVideoManager CreateVideoManager()
        {
            return _videoManagerFactory?.Invoke();
        }
    }
}
