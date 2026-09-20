using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Estuary.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
        readonly EstuaryConfig _config;

        /// <summary>
        /// Optional player identity sent as X-Player-Id on every request.
        /// Used by third-party apps to scope API results per end-user.
        /// </summary>
        public string PlayerId { get; set; }

        public EstuaryHttpClient(EstuaryConfig config)
        {
            _serverUrl = config.ServerUrl.TrimEnd('/');
            _apiKey = config.ApiKey;
            _config = config;
        }

        /// <summary>
        /// Gets the current auth token if a token provider is set.
        /// Returns null if no provider or if the provider fails.
        /// Internal so sibling REST clients (EstuarySimulationApi) reuse the
        /// exact same auth resolution instead of duplicating it.
        /// </summary>
        internal IEnumerator ResolveToken(Action<string> onToken)
        {
            var tokenProvider = _config?.TokenProvider;
            if (tokenProvider == null)
            {
                onToken?.Invoke(null);
                yield break;
            }

            var tokenTask = tokenProvider();
            while (!tokenTask.IsCompleted)
                yield return null;

            if (tokenTask.Exception != null || string.IsNullOrEmpty(tokenTask.Result))
            {
                Debug.LogWarning("[EstuaryHttpClient] Token provider failed, falling back to API key");
                onToken?.Invoke(null);
                yield break;
            }

            onToken?.Invoke(tokenTask.Result);
        }

        /// <summary>
        /// Applies auth header to a UnityWebRequest.
        /// Uses Bearer token if available, otherwise X-API-Key (only when no token provider is configured).
        /// Also identifies the SDK to the gateway via X-Estuary-Client.
        /// </summary>
        internal void ApplyAuth(UnityWebRequest request, string token)
        {
            foreach (var header in BuildHeaders(token))
                request.SetRequestHeader(header.Key, header.Value);
        }

        /// <summary>
        /// Headers sent on every Estuary REST request. Split from ApplyAuth (and static) so
        /// EditMode tests can inspect them without a UnityWebRequest or a config asset.
        /// </summary>
        internal Dictionary<string, string> BuildHeaders(string token)
            => BuildHeaders(token, _apiKey, _config?.TokenProvider != null, PlayerId);

        internal static Dictionary<string, string> BuildHeaders(
            string token, string apiKey, bool hasTokenProvider, string playerId)
        {
            var headers = new Dictionary<string, string>
            {
                { EstuarySdkInfo.ClientHeaderName, EstuarySdkInfo.ClientHeaderValue }
            };

            if (!string.IsNullOrEmpty(token))
            {
                headers["Authorization"] = $"Bearer {token}";
            }
            else if (!hasTokenProvider && !string.IsNullOrEmpty(apiKey))
            {
                // Only use API key when no token provider is configured (server-to-server / legacy SDK).
                // When a token provider exists (per-user Firebase auth), falling back to the
                // API key would silently escalate to developer-level access.
                headers["X-API-Key"] = apiKey;
            }

            if (!string.IsNullOrEmpty(playerId))
            {
                headers["X-Player-Id"] = playerId;
            }

            return headers;
        }

        // v1 CharacterResponse spells these snake_case; AgentResponse keeps the legacy camelCase names.
        static readonly string[][] V1CharacterFieldAliases =
        {
            new[] { "model_url", "modelUrl" },
            new[] { "model_preview_url", "modelPreviewUrl" },
            new[] { "model_status", "modelStatus" },
            new[] { "model_provider", "modelProvider" },
            new[] { "source_image_url", "sourceImageUrl" },
            new[] { "generated_voice_id", "generatedVoiceId" },
        };

        /// <summary>
        /// Parses a character body into AgentResponse. Accepts both the v1 CharacterResponse
        /// shape (snake_case model/voice fields) and the legacy camelCase agent dict.
        /// ModelProvider is null when the gateway predates model_provider on the v1 shape.
        /// </summary>
        internal static AgentResponse ParseCharacter(string json)
        {
            var obj = JObject.Parse(json);
            foreach (var alias in V1CharacterFieldAliases)
            {
                if (obj[alias[1]] == null && obj[alias[0]] != null)
                    obj[alias[1]] = obj[alias[0]];
            }
            return obj.ToObject<AgentResponse>();
        }

        /// <summary>
        /// Uploads an image to generate a character via POST /api/v1/characters/from-image.
        /// Multipart form upload with "image" field. The server answers 201 with the v1
        /// CharacterResponse shape, which is mapped onto AgentResponse.
        /// </summary>
        public IEnumerator UploadImageToCharacter(
            byte[] imageBytes, string mimeType,
            Action<AgentResponse> onSuccess, Action<string> onError)
        {
            string token = null;
            yield return ResolveToken(t => token = t);

            var url = _serverUrl + EstuaryRestRoutes.UploadImageToCharacter().Path;

            var form = new List<IMultipartFormSection>
            {
                new MultipartFormFileSection("image", imageBytes, "photo.jpg", mimeType)
            };

            using (var request = UnityWebRequest.Post(url, form))
            {
                ApplyAuth(request, token);
                request.timeout = 30;

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke(request.error);
                    yield break;
                }

                try
                {
                    var response = ParseCharacter(request.downloadHandler.text);
                    onSuccess?.Invoke(response);
                }
                catch (Exception e)
                {
                    onError?.Invoke($"Failed to parse response: {e.Message}");
                }
            }
        }

        /// <summary>
        /// Gets the current model generation status via GET /api/v1/characters/{agentId}/model.
        /// </summary>
        public IEnumerator GetModelStatus(
            string agentId,
            Action<ModelStatusResponse> onSuccess, Action<string> onError)
        {
            string token = null;
            yield return ResolveToken(t => token = t);

            var url = _serverUrl + EstuaryRestRoutes.GetModelStatus(agentId).Path;

            using (var request = UnityWebRequest.Get(url))
            {
                ApplyAuth(request, token);
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
        /// Triggers 3D model generation for an existing agent via POST /api/v1/characters/{agentId}/model.
        /// Also serves as retry when previous generation failed.
        /// The server answers 202 with {characterId, modelStatus, rigged}; only ModelStatus is
        /// populated on the result, the URLs and progress arrive through GetModelStatus.
        /// </summary>
        public IEnumerator GenerateModel(
            string agentId,
            Action<ModelStatusResponse> onSuccess, Action<string> onError)
        {
            string token = null;
            yield return ResolveToken(t => token = t);

            var url = _serverUrl + EstuaryRestRoutes.GenerateModel(agentId).Path;

            using (var request = new UnityWebRequest(url, "POST"))
            {
                request.uploadHandler = new UploadHandlerRaw(new byte[0]);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                ApplyAuth(request, token);
                request.timeout = 30;

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
                request.timeout = 120; // GLBs can be several MB on mobile networks

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
        /// Gets all agents/characters for the authenticated user via GET /api/agents.
        /// Returns a simple JSON array (no pagination).
        /// Deliberately still on the legacy route: GET /api/v1/characters is paginated (max 100)
        /// and this method has no paging parameters, so callers expect every character.
        /// </summary>
        public IEnumerator GetAgents(Action<List<AgentResponse>> onSuccess, Action<string> onError)
        {
            string token = null;
            yield return ResolveToken(t => token = t);

            var url = _serverUrl + EstuaryRestRoutes.GetAgents().Path;

            using (var request = UnityWebRequest.Get(url))
            {
                ApplyAuth(request, token);
                request.timeout = 10;

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke(request.error);
                    yield break;
                }

                try
                {
                    var agents = JsonConvert.DeserializeObject<List<AgentResponse>>(
                        request.downloadHandler.text);
                    onSuccess?.Invoke(agents);
                }
                catch (Exception e)
                {
                    onError?.Invoke($"Failed to parse response: {e.Message}");
                }
            }
        }

        /// <summary>
        /// Deletes an agent/character via DELETE /api/v1/characters/{agentId}.
        /// Returns 200 on success (body ignored), 404 if not found or not owned by user.
        /// </summary>
        public IEnumerator DeleteAgent(string agentId, Action onSuccess, Action<string> onError)
        {
            string token = null;
            yield return ResolveToken(t => token = t);

            var url = _serverUrl + EstuaryRestRoutes.DeleteAgent(agentId).Path;

            using (var request = UnityWebRequest.Delete(url))
            {
                ApplyAuth(request, token);
                request.timeout = 10;

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke(request.error);
                    yield break;
                }

                onSuccess?.Invoke();
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
            float maxInterval = 10f,
            float maxDuration = 300f)
        {
            var interval = initialInterval;
            string lastStatus = null;
            int lastProgress = -1;
            float elapsed = 0f;

            while (true)
            {
                yield return new WaitForSeconds(interval);
                elapsed += interval;

                if (elapsed >= maxDuration)
                {
                    onError?.Invoke("Model generation timed out");
                    yield break;
                }

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
