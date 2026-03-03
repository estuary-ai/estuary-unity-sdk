using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Estuary.Models;
using Newtonsoft.Json;

namespace Estuary
{
    /// <summary>
    /// HTTP client for Estuary REST API endpoints.
    /// Uses UnityWebRequest for HTTP operations with API key authentication.
    /// All methods are coroutines for use with StartCoroutine.
    /// </summary>
    public class EstuaryHttpClient
    {
        readonly string _serverUrl;
        readonly string _apiKey;

        public EstuaryHttpClient(EstuaryConfig config)
        {
            _serverUrl = config.ServerUrl.TrimEnd('/');
            _apiKey = config.ApiKey;
        }

        /// <summary>
        /// Uploads an image to generate a character via POST /api/generate/image-to-character.
        /// Multipart form upload with "image" field.
        /// </summary>
        public IEnumerator UploadImageToCharacter(
            byte[] imageBytes, string mimeType,
            Action<AgentResponse> onSuccess, Action<string> onError)
        {
            var url = $"{_serverUrl}/api/generate/image-to-character";

            var form = new List<IMultipartFormSection>
            {
                new MultipartFormFileSection("image", imageBytes, "photo.jpg", mimeType)
            };

            using (var request = UnityWebRequest.Post(url, form))
            {
                request.SetRequestHeader("X-API-Key", _apiKey);
                request.timeout = 30;

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke(request.error);
                    yield break;
                }

                try
                {
                    var response = JsonConvert.DeserializeObject<AgentResponse>(
                        request.downloadHandler.text);
                    onSuccess?.Invoke(response);
                }
                catch (Exception e)
                {
                    onError?.Invoke($"Failed to parse response: {e.Message}");
                }
            }
        }

        /// <summary>
        /// Gets the current model generation status via GET /api/generate/{agentId}/model-status.
        /// </summary>
        public IEnumerator GetModelStatus(
            string agentId,
            Action<ModelStatusResponse> onSuccess, Action<string> onError)
        {
            var url = $"{_serverUrl}/api/generate/{agentId}/model-status";

            using (var request = UnityWebRequest.Get(url))
            {
                request.SetRequestHeader("X-API-Key", _apiKey);
                request.timeout = 10;

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke(request.error);
                    yield break;
                }

                try
                {
                    var response = JsonConvert.DeserializeObject<ModelStatusResponse>(
                        request.downloadHandler.text);
                    onSuccess?.Invoke(response);
                }
                catch (Exception e)
                {
                    onError?.Invoke($"Failed to parse response: {e.Message}");
                }
            }
        }

        /// <summary>
        /// Downloads a GLB file from a URL and returns the raw bytes.
        /// Accepts both full URLs (e.g. S3) and relative paths (e.g. /static/agent_models/...).
        /// Relative paths are resolved against _serverUrl.
        /// No API key header needed -- these are not authenticated API endpoints.
        /// </summary>
        /// <param name="url">URL to the GLB file (full or relative path)</param>
        /// <param name="onSuccess">Called with raw GLB bytes on successful download</param>
        /// <param name="onError">Called with error message on failure</param>
        public IEnumerator DownloadGlb(string url, Action<byte[]> onSuccess, Action<string> onError)
        {
            // Resolve relative paths against server URL
            var resolvedUrl = url.StartsWith("/") ? _serverUrl.TrimEnd('/') + url : url;

            using (var request = UnityWebRequest.Get(resolvedUrl))
            {
                request.timeout = 60; // GLBs can be several MB on mobile networks

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke(request.error);
                    yield break;
                }

                onSuccess?.Invoke(request.downloadHandler.data);
            }
        }

        /// <summary>
        /// Polls model status at regular intervals until completion or failure.
        /// Calls onStatusChanged when status transitions, onCompleted when done,
        /// onError on network or terminal failure.
        /// </summary>
        public IEnumerator PollModelStatus(
            string agentId,
            Action<ModelStatusResponse> onStatusChanged,
            Action<ModelStatusResponse> onCompleted,
            Action<string> onError,
            float initialInterval = 2f,
            float maxInterval = 10f)
        {
            var interval = initialInterval;
            string lastStatus = null;
            int lastProgress = -1;

            while (true)
            {
                yield return new WaitForSeconds(interval);

                ModelStatusResponse status = null;
                string error = null;

                yield return GetModelStatus(agentId,
                    s => status = s,
                    e => error = e);

                if (error != null)
                {
                    onError?.Invoke(error);
                    yield break;
                }

                // Notify on status or progress change
                if (status.ModelStatus != lastStatus || status.Progress != lastProgress)
                {
                    lastStatus = status.ModelStatus;
                    lastProgress = status.Progress;
                    onStatusChanged?.Invoke(status);
                }

                // Terminal states
                // texture_failed = partial success (preview is usable, textures didn't apply)
                // Treat as completion so the model viewer can display preview with a notice.
                if (status.IsCompleted || status.IsTextureFailed)
                {
                    onCompleted?.Invoke(status);
                    yield break;
                }

                if (status.IsFailed)
                {
                    onError?.Invoke($"Model generation failed with status: {status.ModelStatus}");
                    yield break;
                }

                // Exponential backoff
                interval = Mathf.Min(interval * 1.5f, maxInterval);
            }
        }
    }
}
