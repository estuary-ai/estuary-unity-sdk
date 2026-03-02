using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Estuary.Models;

namespace Estuary
{
    /// <summary>
    /// REST API client for Estuary HTTP endpoints.
    /// Provides coroutine-based methods for image upload and model status polling.
    ///
    /// Usage: Instantiate with an EstuaryConfig, then run methods as coroutines via
    /// MonoBehaviour.StartCoroutine().
    ///
    /// This is a plain C# class (not a MonoBehaviour) so it can be owned by any component.
    /// </summary>
    public class EstuaryHttpClient
    {
        private readonly EstuaryConfig _config;

        /// <summary>
        /// Create a new HTTP client using the given configuration.
        /// </summary>
        /// <param name="config">EstuaryConfig providing ServerUrl and ApiKey.</param>
        public EstuaryHttpClient(EstuaryConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <summary>
        /// Upload an image to generate a character via POST /api/generate/image-to-character.
        /// Sends a multipart form with the image bytes and returns the created AgentResponse.
        /// </summary>
        /// <param name="imageBytes">Raw image bytes (PNG, JPG, or WebP).</param>
        /// <param name="mimeType">MIME type of the image (e.g., "image/png", "image/jpeg", "image/webp").</param>
        /// <param name="onSuccess">Called with the deserialized AgentResponse on success.</param>
        /// <param name="onError">Called with a descriptive error message on failure.</param>
        public IEnumerator UploadImageToCharacter(byte[] imageBytes, string mimeType,
            Action<AgentResponse> onSuccess, Action<string> onError)
        {
            var url = $"{_config.ServerUrl}/api/generate/image-to-character";

            // Determine file extension from MIME type
            string ext;
            switch (mimeType)
            {
                case "image/png":
                    ext = "png";
                    break;
                case "image/webp":
                    ext = "webp";
                    break;
                default:
                    ext = "jpg";
                    break;
            }

            if (_config.DebugLogging)
            {
                Debug.Log($"[EstuaryHttpClient] Uploading image to {url} ({imageBytes.Length} bytes, {mimeType})");
            }

            var formData = new List<IMultipartFormSection>
            {
                new MultipartFormFileSection("image", imageBytes, $"photo.{ext}", mimeType)
            };

            using (var request = UnityWebRequest.Post(url, formData))
            {
                request.SetRequestHeader("X-API-Key", _config.ApiKey);
                request.timeout = 30;

                yield return request.SendWebRequest();

                if (_config.DebugLogging)
                {
                    Debug.Log($"[EstuaryHttpClient] Upload response: {request.responseCode}");
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    var errorText = request.downloadHandler?.text ?? request.error;
                    onError?.Invoke($"Upload failed ({request.responseCode}): {errorText}");
                    yield break;
                }

                AgentResponse agent;
                try
                {
                    agent = JsonConvert.DeserializeObject<AgentResponse>(request.downloadHandler.text);
                }
                catch (JsonException ex)
                {
                    onError?.Invoke($"Failed to parse upload response: {ex.Message}");
                    yield break;
                }

                onSuccess?.Invoke(agent);
            }
        }

        /// <summary>
        /// Get the current model generation status for an agent via GET /api/generate/{agentId}/model-status.
        /// </summary>
        /// <param name="agentId">The agent UUID returned from image-to-character.</param>
        /// <param name="onSuccess">Called with the deserialized ModelStatusResponse on success.</param>
        /// <param name="onError">Called with a descriptive error message on failure.</param>
        public IEnumerator GetModelStatus(string agentId, Action<ModelStatusResponse> onSuccess,
            Action<string> onError)
        {
            var url = $"{_config.ServerUrl}/api/generate/{agentId}/model-status";

            if (_config.DebugLogging)
            {
                Debug.Log($"[EstuaryHttpClient] Getting model status from {url}");
            }

            using (var request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("X-API-Key", _config.ApiKey);
                request.timeout = 10;

                yield return request.SendWebRequest();

                if (_config.DebugLogging)
                {
                    Debug.Log($"[EstuaryHttpClient] Model status response: {request.responseCode}");
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    var errorText = request.downloadHandler?.text ?? request.error;
                    onError?.Invoke($"Get model status failed ({request.responseCode}): {errorText}");
                    yield break;
                }

                ModelStatusResponse status;
                try
                {
                    status = JsonConvert.DeserializeObject<ModelStatusResponse>(request.downloadHandler.text);
                }
                catch (JsonException ex)
                {
                    onError?.Invoke($"Failed to parse model status response: {ex.Message}");
                    yield break;
                }

                onSuccess?.Invoke(status);
            }
        }

        /// <summary>
        /// Poll model generation status with exponential backoff until completion or failure.
        /// Invokes onStatusChanged whenever the status transitions, onCompleted when the model
        /// is fully generated, and onError on any failure (network error, terminal failure status,
        /// or missing model configuration).
        /// </summary>
        /// <param name="agentId">The agent UUID to poll.</param>
        /// <param name="onStatusChanged">Called each time the model status changes (e.g., generating -> preview_ready).</param>
        /// <param name="onCompleted">Called when model generation reaches "completed" status.</param>
        /// <param name="onError">Called on network error, parse error, or terminal failure status.</param>
        /// <param name="initialInterval">Initial polling interval in seconds (default 2s).</param>
        /// <param name="maxInterval">Maximum polling interval in seconds (default 10s).</param>
        public IEnumerator PollModelStatus(string agentId,
            Action<ModelStatusResponse> onStatusChanged, Action<ModelStatusResponse> onCompleted,
            Action<string> onError, float initialInterval = 2f, float maxInterval = 10f)
        {
            var interval = initialInterval;
            string lastStatus = null;

            if (_config.DebugLogging)
            {
                Debug.Log($"[EstuaryHttpClient] Starting model status polling for agent {agentId} (interval={initialInterval}s, max={maxInterval}s)");
            }

            while (true)
            {
                yield return new WaitForSeconds(interval);

                var url = $"{_config.ServerUrl}/api/generate/{agentId}/model-status";

                using (var request = UnityWebRequest.Get(url))
                {
                    request.SetRequestHeader("X-API-Key", _config.ApiKey);
                    request.timeout = 10;

                    yield return request.SendWebRequest();

                    if (request.result != UnityWebRequest.Result.Success)
                    {
                        var errorText = request.downloadHandler?.text ?? request.error;
                        onError?.Invoke($"Poll model status failed ({request.responseCode}): {errorText}");
                        yield break;
                    }

                    ModelStatusResponse status;
                    try
                    {
                        status = JsonConvert.DeserializeObject<ModelStatusResponse>(request.downloadHandler.text);
                    }
                    catch (JsonException ex)
                    {
                        onError?.Invoke($"Failed to parse poll response: {ex.Message}");
                        yield break;
                    }

                    // Check for null/missing model status (agent has no 3D model configured)
                    if (string.IsNullOrEmpty(status.ModelStatus))
                    {
                        onError?.Invoke("Agent has no 3D model status — model generation may not be configured");
                        yield break;
                    }

                    // Notify on status change
                    if (status.ModelStatus != lastStatus)
                    {
                        if (_config.DebugLogging)
                        {
                            Debug.Log($"[EstuaryHttpClient] Model status changed: {lastStatus ?? "(none)"} -> {status.ModelStatus}");
                        }

                        lastStatus = status.ModelStatus;
                        onStatusChanged?.Invoke(status);
                    }

                    // Terminal: completed
                    if (status.IsCompleted)
                    {
                        if (_config.DebugLogging)
                        {
                            Debug.Log($"[EstuaryHttpClient] Model generation completed for agent {agentId}");
                        }

                        onCompleted?.Invoke(status);
                        yield break;
                    }

                    // Terminal: failed
                    if (status.IsFailed)
                    {
                        onError?.Invoke($"Model generation failed with status: {status.ModelStatus}");
                        yield break;
                    }
                }

                // Exponential backoff
                interval = Mathf.Min(interval * 1.5f, maxInterval);
            }
        }
    }
}
