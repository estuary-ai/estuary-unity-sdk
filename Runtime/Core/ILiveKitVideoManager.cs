using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Estuary
{
    /// <summary>
    /// Interface abstracting the LiveKitVideoManager public API.
    /// Core components use this interface so they compile without the LiveKit SDK.
    /// The concrete implementation lives in the Estuary.LiveKit assembly and is
    /// registered at runtime via LiveKitBridge.
    /// </summary>
    public interface ILiveKitVideoManager : IDisposable
    {
        #region Properties

        /// <summary>
        /// Target video width.
        /// </summary>
        int TargetWidth { get; set; }

        /// <summary>
        /// Target video height.
        /// </summary>
        int TargetHeight { get; set; }

        /// <summary>
        /// Target frames per second.
        /// </summary>
        int TargetFps { get; set; }

        /// <summary>
        /// Whether video is currently being published.
        /// </summary>
        bool IsPublishing { get; }

        /// <summary>
        /// Enable debug logging.
        /// </summary>
        bool DebugLogging { get; set; }

        /// <summary>
        /// Whether to use front-facing camera.
        /// </summary>
        bool UseFrontCamera { get; set; }

        /// <summary>
        /// Preferred webcam device name (empty for default).
        /// </summary>
        string PreferredDevice { get; set; }

        /// <summary>
        /// Current webcam texture (for preview). Null if not publishing.
        /// </summary>
        WebCamTexture WebcamTexture { get; }

        #endregion

        #region Methods

        /// <summary>
        /// Set the MonoBehaviour to use for running coroutines.
        /// </summary>
        void SetCoroutineRunner(MonoBehaviour runner);

        /// <summary>
        /// Start publishing video to a LiveKit room.
        /// The room parameter is the LiveKit Room object (passed as object to avoid direct dependency).
        /// </summary>
        Task<bool> StartPublishingAsync(object room);

        /// <summary>
        /// Stop publishing video.
        /// </summary>
        Task StopPublishingAsync();

        /// <summary>
        /// Update video settings while publishing.
        /// </summary>
        void UpdateSettings(int width, int height, int fps);

        /// <summary>
        /// Process queued events on the main thread. Call this from Update().
        /// </summary>
        void ProcessMainThreadQueue();

        #endregion
    }
}
