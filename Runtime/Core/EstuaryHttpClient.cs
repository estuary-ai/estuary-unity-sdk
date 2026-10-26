using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Estuary.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Estuary
{
    /// <summary>Coroutine REST client. Start requests with StartCoroutine; failures include HTTP status and response detail.</summary>
    public partial class EstuaryHttpClient
    {
        readonly string _serverUrl;
        readonly string _apiKey;
        readonly EstuaryConfig _config;
        internal static readonly JsonSerializerSettings RequestSettings =
            new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };

        /// <summary>Optional verified player identity used to scope REST operations.</summary>
        public string PlayerId { get; set; }
        /// <summary>Optional organization context for org-owned resources.</summary>
        public string OrgId { get; set; }

        // Stub the final request, not a separate builder: conformance tests exercise the real public methods.
        internal Func<RestRequest, RestResponse> TransportForTest;

        public EstuaryHttpClient(EstuaryConfig config)
        {
            _config = config != null ? config : throw new ArgumentNullException(nameof(config));
            _serverUrl = config.ServerUrl.TrimEnd('/');
            _apiKey = config.ApiKey;
        }

        internal IEnumerator ResolveToken(Action<string> onToken)
        {
            var provider = _config.TokenProvider;
            if (provider == null) { onToken(null); yield break; }
            System.Threading.Tasks.Task<string> task;
            try { task = provider(); }
            catch (Exception) { onToken(null); yield break; }
            if (task == null) { onToken(null); yield break; }
            while (!task.IsCompleted) yield return null;
            onToken(task.IsCanceled || task.IsFaulted ? null : task.Result);
        }

        internal Dictionary<string, string> RequestHeaders(string token, bool anonymous = false, bool bearerKey = false)
        {
            var headers = new Dictionary<string, string> { ["X-Estuary-Client"] = EstuarySdk.ClientIdentification };
            if (anonymous) return headers;
            if (!string.IsNullOrEmpty(token)) headers["Authorization"] = "Bearer " + token;
            else if (_config.TokenProvider == null && !string.IsNullOrEmpty(_apiKey))
            {
                if (bearerKey) headers["Authorization"] = "Bearer " + _apiKey;
                else headers["X-API-Key"] = _apiKey;
            }
            if (!string.IsNullOrEmpty(PlayerId)) headers["X-Player-Id"] = PlayerId;
            if (!string.IsNullOrEmpty(OrgId)) headers["X-Org-Id"] = OrgId;
            return headers;
        }

        internal void ApplyAuth(UnityWebRequest request, string token)
        {
            foreach (var header in RequestHeaders(token)) request.SetRequestHeader(header.Key, header.Value);
        }

        internal static string Esc(string value) => Uri.EscapeDataString(value ?? "");
        internal static string CharacterPath(string id) => "/api/v1/characters/" + Esc(id);
        internal static string PairPath(string characterId, string playerId) => CharacterPath(characterId) + "/players/" + Esc(playerId);
        internal static string Query(params object[] pairs)
        {
            var parts = new List<string>();
            for (int i = 0; i < pairs.Length; i += 2)
            {
                if (pairs[i + 1] == null) continue;
                var value = pairs[i + 1] is bool b ? (b ? "true" : "false") : Convert.ToString(pairs[i + 1], CultureInfo.InvariantCulture);
                parts.Add(Esc((string)pairs[i]) + "=" + Esc(value));
            }
            return parts.Count == 0 ? "" : "?" + string.Join("&", parts);
        }

        internal IEnumerator Send<T>(string method, string path, object body, Action<T> onSuccess, Action<string> onError,
            int timeout = 30, List<IMultipartFormSection> form = null, bool anonymous = false, bool bearerKey = false,
            string idempotencyKey = null)
        {
            string token = null;
            if (!anonymous) yield return ResolveToken(t => token = t);
            if (!anonymous && _config.TokenProvider != null && string.IsNullOrEmpty(token))
            {
                onError?.Invoke("Authentication token provider failed; no API-key fallback was attempted.");
                yield break;
            }
            var spec = new RestRequest
            {
                Method = method, Url = _serverUrl + path, Body = body == null ? null : JToken.FromObject(body, JsonSerializer.Create(RequestSettings)),
                Headers = RequestHeaders(token, anonymous, bearerKey), Form = form
            };
            if (!string.IsNullOrEmpty(idempotencyKey)) spec.Headers["Idempotency-Key"] = idempotencyKey;
            RestResponse result;
            if (TransportForTest != null) result = TransportForTest(spec);
            else
            {
                using (var request = form != null ? UnityWebRequest.Post(spec.Url, form) : new UnityWebRequest(spec.Url, method))
                {
                    request.method = method;
                    if (form == null)
                    {
                        request.downloadHandler = new DownloadHandlerBuffer();
                        if (spec.Body != null)
                        {
                            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(spec.Body.ToString(Formatting.None)));
                            request.SetRequestHeader("Content-Type", "application/json");
                        }
                    }
                    foreach (var header in spec.Headers) request.SetRequestHeader(header.Key, header.Value);
                    request.timeout = timeout;
                    // Redirects could otherwise forward credentials to a third-party host.
                    request.redirectLimit = 0;
                    yield return request.SendWebRequest();
                    result = new RestResponse { Status = request.responseCode, Body = request.downloadHandler?.text, Error = request.error };
                }
            }
            if (result.Status < 200 || result.Status >= 300)
            {
                onError?.Invoke($"HTTP {result.Status}: {result.Error} {result.Body}".Trim());
                yield break;
            }
            T value;
            try { value = string.IsNullOrWhiteSpace(result.Body) ? default : JsonConvert.DeserializeObject<T>(result.Body); }
            catch (Exception e) { onError?.Invoke($"Failed to parse response: {e.Message}"); yield break; }
            onSuccess?.Invoke(value);
        }

        internal IEnumerator SendNoContent(string method, string path, object body, Action onSuccess, Action<string> onError)
            => Send<JToken>(method, path, body, _ => onSuccess?.Invoke(), onError);

        /// <summary>Generate a character persona from an image. Optional instruction overrides use canonical camelCase keys.</summary>
        public IEnumerator UploadImageToCharacter(byte[] imageBytes, string mimeType,
            Action<AgentResponse> onSuccess, Action<string> onError, ImageGenerationOptions options = null, string idempotencyKey = null)
        {
            var form = FileForm("image", imageBytes, "photo" + ImageExtension(mimeType), mimeType);
            if (options != null)
                foreach (var field in JObject.FromObject(options, JsonSerializer.Create(RequestSettings)))
                    form.Add(new MultipartFormDataSection(field.Key, field.Value.Type == JTokenType.Boolean
                        ? ((bool)field.Value ? "true" : "false") : field.Value.ToString()));
            return Send("POST", "/api/v1/characters/from-image", null, onSuccess, onError, 60, form, idempotencyKey: idempotencyKey);
        }

        /// <summary>Read model progress and rig/animation metadata.</summary>
        public IEnumerator GetModelStatus(string agentId, Action<ModelStatusResponse> onSuccess, Action<string> onError)
            => Send("GET", CharacterPath(agentId) + "/model", null, onSuccess, onError);

        /// <summary>Start or retry model generation. Set rigged for the animated humanoid pipeline.</summary>
        public IEnumerator GenerateModel(string agentId, Action<ModelStatusResponse> onSuccess, Action<string> onError, bool rigged = false)
            => Send("POST", CharacterPath(agentId) + "/model", rigged ? new { rigged = true } : null, onSuccess, onError);

        /// <summary>Legacy unpaginated list retained for source and behavior compatibility. Use ListCharacters for v1 pagination.</summary>
        public IEnumerator GetAgents(Action<List<AgentResponse>> onSuccess, Action<string> onError)
            => Send("GET", "/api/agents", null, onSuccess, onError);

        /// <summary>Delete a character and its associated data.</summary>
        public IEnumerator DeleteAgent(string agentId, Action onSuccess, Action<string> onError)
            => SendNoContent("DELETE", CharacterPath(agentId), null, onSuccess, onError);

        internal static List<IMultipartFormSection> FileForm(string field, byte[] bytes, string filename, string mimeType)
            => new List<IMultipartFormSection> { new MultipartFormFileSection(field, bytes, filename, mimeType) };
        private static string ImageExtension(string mime) => mime == "image/png" ? ".png" : mime == "image/webp" ? ".webp" : ".jpg";

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
                if (status.IsCompleted || status.IsPartialSuccess)
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

    internal sealed class RestRequest
    {
        public string Method;
        public string Url;
        public JToken Body;
        public Dictionary<string, string> Headers;
        public List<IMultipartFormSection> Form;
    }

    internal sealed class RestResponse
    {
        public long Status;
        public string Body;
        public string Error;
    }
}
