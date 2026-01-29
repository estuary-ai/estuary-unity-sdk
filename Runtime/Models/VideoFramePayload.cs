using System;
using UnityEngine;

namespace Estuary.Models
{
    /// <summary>
    /// Payload for sending video frames to the world model.
    /// </summary>
    [Serializable]
    public class VideoFramePayload
    {
        /// <summary>
        /// Session ID for the world model session.
        /// </summary>
        public string sessionId;

        /// <summary>
        /// Base64-encoded JPEG frame data.
        /// </summary>
        public string frame;

        /// <summary>
        /// Unix timestamp in seconds (with millisecond precision).
        /// </summary>
        public double timestamp;

        /// <summary>
        /// 4x4 camera pose matrix (row-major, optional).
        /// </summary>
        public float[] pose;

        public VideoFramePayload() { }

        public VideoFramePayload(string sessionId, string frameBase64, double timestamp, Matrix4x4? cameraPose = null)
        {
            this.sessionId = sessionId;
            this.frame = frameBase64;
            this.timestamp = timestamp;
            
            if (cameraPose.HasValue)
            {
                this.pose = Matrix4x4ToArray(cameraPose.Value);
            }
        }

        /// <summary>
        /// Create payload from a Texture2D.
        /// </summary>
        /// <param name="sessionId">Session ID</param>
        /// <param name="texture">Source texture</param>
        /// <param name="quality">JPEG quality (0-100)</param>
        /// <param name="cameraPose">Optional camera pose</param>
        public static VideoFramePayload FromTexture(
            string sessionId, 
            Texture2D texture, 
            int quality = 75,
            Matrix4x4? cameraPose = null)
        {
            var jpegBytes = texture.EncodeToJPG(quality);
            var base64 = Convert.ToBase64String(jpegBytes);
            var timestamp = GetUnixTimestamp();

            return new VideoFramePayload(sessionId, base64, timestamp, cameraPose);
        }

        private static double GetUnixTimestamp()
        {
            return (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        private static float[] Matrix4x4ToArray(Matrix4x4 matrix)
        {
            return new float[]
            {
                matrix.m00, matrix.m01, matrix.m02, matrix.m03,
                matrix.m10, matrix.m11, matrix.m12, matrix.m13,
                matrix.m20, matrix.m21, matrix.m22, matrix.m23,
                matrix.m30, matrix.m31, matrix.m32, matrix.m33
            };
        }
    }

    /// <summary>
    /// Payload for device pose updates.
    /// </summary>
    [Serializable]
    public class DevicePosePayload
    {
        /// <summary>
        /// Session ID for the world model session.
        /// </summary>
        public string sessionId;

        /// <summary>
        /// 4x4 pose matrix (row-major).
        /// </summary>
        public float[] pose;

        public DevicePosePayload() { }

        public DevicePosePayload(string sessionId, Matrix4x4 poseMatrix)
        {
            this.sessionId = sessionId;
            this.pose = new float[]
            {
                poseMatrix.m00, poseMatrix.m01, poseMatrix.m02, poseMatrix.m03,
                poseMatrix.m10, poseMatrix.m11, poseMatrix.m12, poseMatrix.m13,
                poseMatrix.m20, poseMatrix.m21, poseMatrix.m22, poseMatrix.m23,
                poseMatrix.m30, poseMatrix.m31, poseMatrix.m32, poseMatrix.m33
            };
        }
    }

    /// <summary>
    /// Payload for scene graph subscription.
    /// </summary>
    [Serializable]
    public class SceneGraphSubscribePayload
    {
        /// <summary>
        /// Session ID for the world model session.
        /// </summary>
        public string sessionId;

        public SceneGraphSubscribePayload() { }

        public SceneGraphSubscribePayload(string sessionId)
        {
            this.sessionId = sessionId;
        }
    }

    /// <summary>
    /// Payload for enabling LiveKit video on the backend.
    /// </summary>
    [Serializable]
    public class EnableLiveKitVideoPayload
    {
        /// <summary>
        /// Session ID for the world model session.
        /// </summary>
        public string sessionId;

        /// <summary>
        /// Target frames per second.
        /// </summary>
        public int targetFps;

        public EnableLiveKitVideoPayload() { }

        public EnableLiveKitVideoPayload(string sessionId, int targetFps)
        {
            this.sessionId = sessionId;
            this.targetFps = targetFps;
        }
    }
}
