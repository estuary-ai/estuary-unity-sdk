using System;
using UnityEngine;

namespace Estuary.Models
{
    /// <summary>
    /// Data received when the server proactively requests a camera image
    /// (camera_capture event) — e.g. when it detects vision intent in user
    /// speech. Respond by capturing a frame, encoding it as base64, and calling
    /// SendCameraImage with the matching <see cref="RequestId"/> so the server
    /// can correlate the image with its request.
    /// </summary>
    [Serializable]
    public class CameraCaptureRequest
    {
        [SerializeField] private string request_id;
        [SerializeField] private string text;

        /// <summary>
        /// Correlation ID to echo back on the camera_image response.
        /// </summary>
        public string RequestId => request_id;

        /// <summary>
        /// Optional prompt/context text accompanying the request (may be null).
        /// </summary>
        public string Text => text;

        public CameraCaptureRequest() { }

        /// <summary>
        /// Create CameraCaptureRequest from a JSON string.
        /// </summary>
        public static CameraCaptureRequest FromJson(string json)
        {
            return JsonUtility.FromJson<CameraCaptureRequest>(json);
        }

        public override string ToString()
        {
            return $"CameraCaptureRequest(RequestId={RequestId}, Text={Text})";
        }
    }
}
