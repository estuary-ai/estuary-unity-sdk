using System;

namespace Estuary.Models
{
    /// <summary>
    /// Per-session client capability declaration sent in the auth payload.
    /// Tells the server what the device can physically do (camera, mic, speaker)
    /// plus which protocol features this build speaks (see <see cref="client_action"/>).
    /// When a capability is false, server-side tools that require it (e.g. the
    /// request_camera_image tool needs <see cref="camera"/>) are hidden from the
    /// LLM for the session.
    ///
    /// Device defaults are all true, matching the server's behavior when capabilities
    /// are omitted entirely — so declaring capabilities can only ever RESTRICT the
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

        /// <summary>
        /// Whether this client understands typed <c>client_action</c> events
        /// (SDK_CONTRACT v1.10). NOT a device capability — it declares what this
        /// SDK build speaks, so it is always true here and is forced true on the
        /// outbound payload regardless of what an integrator passes in.
        ///
        /// Unlike the device fields, the server defaults this one to FALSE when
        /// absent, and serves the retired XML &lt;action&gt; tag path instead. That
        /// inversion is deliberate: an old client can't announce that it's old.
        /// </summary>
        public bool client_action = true;

        public SessionCapabilities() { }

        public SessionCapabilities(bool camera, bool microphone, bool speaker)
        {
            this.camera = camera;
            this.microphone = microphone;
            this.speaker = speaker;
        }
    }
}
