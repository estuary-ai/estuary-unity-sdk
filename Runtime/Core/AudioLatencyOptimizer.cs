using UnityEngine;

namespace Estuary
{
    /// <summary>
    /// Configures Unity audio settings for minimal voice playback latency.
    /// Runs before any scene loads to ensure DSP buffer is set before AudioSources start.
    /// </summary>
    public static class AudioLatencyOptimizer
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ConfigureLowLatencyAudio()
        {
            // Set DSP buffer to 256 samples (2 buffers) for low-latency voice playback.
            // Default is typically 1024 samples (~21ms at 48kHz), which adds unnecessary
            // playback latency for real-time voice. 256 samples reduces this to ~5ms.
            // Must be called before any AudioSource starts playing.
            AudioSettings.SetDSPBufferSize(256, 2);
            Debug.Log("[AudioLatencyOptimizer] DSP buffer set to 256 samples (2 buffers) for low-latency voice");
        }
    }
}
