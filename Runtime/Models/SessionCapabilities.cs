using System;

namespace Estuary.Models
{
    /// <summary>
    /// Per-session client capability declaration sent in the auth payload.
    /// Tells the server what the device can physically do (camera, mic, speaker).
    /// When a capability is false, server-side tools that require it (e.g. the
    /// request_camera_image tool needs <see cref="camera"/>) are hidden from the
    /// LLM for the session.
    ///
    /// Defaults are all true, matching the server's behavior when capabilities are
    /// omitted entirely — so declaring capabilities can only ever RESTRICT the
    /// device, never expand it. This is why the SDK always sends a fully-true
    /// instance rather than a null field (Unity's JsonUtility would serialize a
    /// null nested object as all-false, which would wrongly disable everything).
    ///
    /// Public fields (not properties) so Unity's JsonUtility serializes them with
    /// the exact wire key names on the outbound auth payload.
    /// </summary>
    [Serializable]
    public class SessionCapabilities
    {
        /// <summary>Schema version. Currently "1".</summary>
        public string version = "1";

        /// <summary>Whether the device has a usable camera. Default true.</summary>
        public bool camera = true;

        /// <summary>Whether the device has a usable microphone. Default true.</summary>
        public bool microphone = true;

        /// <summary>Whether the device has a usable speaker. Default true.</summary>
        public bool speaker = true;

        public SessionCapabilities() { }

        public SessionCapabilities(bool camera, bool microphone, bool speaker)
        {
            this.camera = camera;
            this.microphone = microphone;
            this.speaker = speaker;
        }
    }
}
