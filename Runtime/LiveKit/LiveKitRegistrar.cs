using UnityEngine;

namespace Estuary
{
    /// <summary>
    /// Automatically registers LiveKit concrete implementations with LiveKitBridge
    /// when the Estuary.LiveKit assembly is loaded (i.e., when LiveKit SDK is installed).
    /// Uses RuntimeInitializeOnLoadMethod to run before any scene is loaded.
    /// </summary>
    static class LiveKitRegistrar
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            LiveKitBridge.RegisterVoiceManagerFactory(() => new LiveKitVoiceManager());
            LiveKitBridge.RegisterVideoManagerFactory(() => new LiveKitVideoManager());
            Debug.Log("[Estuary] LiveKit integration registered");
        }
    }
}
