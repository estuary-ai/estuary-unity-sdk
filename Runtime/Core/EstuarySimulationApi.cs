using System;
using System.Collections;
using System.Text;
using UnityEngine.Networking;
using Estuary.Models;
using Newtonsoft.Json;

namespace Estuary
{
    /// <summary>
    /// REST client for the public Simulation API (v1) — /api/v1/simulation/*
    /// (see SDK_CONTRACT.md §REST API — Simulation (v1)).
    ///
    /// Build worlds from the caller's characters, fork per-player instances,
    /// trigger character-to-character conversations, and read back events,
    /// lore, the world-view document, and transcripts.
    ///
    /// Auth follows EstuaryHttpClient: Firebase Bearer token when a
    /// TokenProvider is configured, otherwise X-API-Key. All methods are
    /// coroutines for use with StartCoroutine (the EstuarySimulation
    /// component runs them for you).
    /// </summary>
    public class EstuarySimulationApi
    {
        private const string BasePath = "/api/v1/simulation";

        // Omit nulls: the server applies its own defaults to omitted optionals
        // and would reject explicit nulls on defaulted fields (e.g. priority).
        private static readonly JsonSerializerSettings RequestSettings =
            new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore };

        private readonly string _serverUrl;
        private readonly EstuaryHttpClient _http;

        public EstuarySimulationApi(EstuaryConfig config)
        {
            if (config == null)
                throw new ArgumentNullException(nameof(config));
            _serverUrl = config.ServerUrl.TrimEnd('/');
            _http = new EstuaryHttpClient(config);
        }

        #region Worlds

        /// <summary>Create a world (POST /worlds), optionally with characters + relationships inline.</summary>
        public IEnumerator CreateWorld(
            SimulationWorldCreateRequest body,
            Action<SimulationWorld> onSuccess, Action<string> onError)
            => Send("POST", "/worlds", body, onSuccess, onError);

        /// <summary>List the caller's worlds (GET /worlds), newest first.</summary>
        public IEnumerator ListWorlds(
            Action<SimulationWorldList> onSuccess, Action<string> onError,
            int limit = 50, int offset = 0)
            => Send("GET", $"/worlds?limit={limit}&offset={offset}", null, onSuccess, onError);

        /// <summary>Get a world with its characters and seed relationships (GET /worlds/{id}).</summary>
        public IEnumerator GetWorld(
            string worldId,
            Action<SimulationWorld> onSuccess, Action<string> onError)
            => Send("GET", $"/worlds/{Esc(worldId)}", null, onSuccess, onError);

        /// <summary>Update a world (PATCH /worlds/{id}): name/description/status; simulationConfig merges, seedLore replaces.</summary>
        public IEnumerator UpdateWorld(
            string worldId, SimulationWorldUpdateRequest body,
            Action<SimulationWorld> onSuccess, Action<string> onError)
            => Send("PATCH", $"/worlds/{Esc(worldId)}", body, onSuccess, onError);

        /// <summary>
        /// Delete a world and everything its simulation generated (DELETE
        /// /worlds/{id}): instances, events, lore, transcripts, AND the
        /// memories its conversations created — a destroyed world leaves
        /// nothing to bleed into a successor (contract v1.6).
        /// </summary>
        public IEnumerator DeleteWorld(string worldId, Action onSuccess, Action<string> onError)
            => SendNoContent("DELETE", $"/worlds/{Esc(worldId)}", null, onSuccess, onError);

        /// <summary>
        /// Delete every memory the world's simulation created across all its
        /// instances (DELETE /worlds/{id}/memories) — a soft reset that keeps
        /// the world, instances, lore, and transcripts. Live-chat memories
        /// with the same characters are untouched (contract v1.6).
        /// </summary>
        public IEnumerator ClearWorldMemories(
            string worldId,
            Action<SimulationWorldMemoriesCleared> onSuccess, Action<string> onError)
            => Send("DELETE", $"/worlds/{Esc(worldId)}/memories", null, onSuccess, onError);

        /// <summary>Add one of the caller's characters to a world (POST /worlds/{id}/characters). 409 if already a member.</summary>
        public IEnumerator AddCharacter(
            string worldId, string characterId, string roleInWorld,
            Action<SimulationWorldCharacter> onSuccess, Action<string> onError)
            => Send("POST", $"/worlds/{Esc(worldId)}/characters",
                new SimulationCharacterSpec(characterId, roleInWorld), onSuccess, onError);

        /// <summary>Remove a character from a world (also drops its seed relationships).</summary>
        public IEnumerator RemoveCharacter(
            string worldId, string characterId,
            Action onSuccess, Action<string> onError)
            => SendNoContent("DELETE", $"/worlds/{Esc(worldId)}/characters/{Esc(characterId)}",
                null, onSuccess, onError);

        /// <summary>Create or update a seed relationship (PUT /worlds/{id}/relationships; upserts on world+source+target).</summary>
        public IEnumerator UpsertRelationship(
            string worldId, SimulationRelationshipSpec body,
            Action<SimulationRelationship> onSuccess, Action<string> onError)
            => Send("PUT", $"/worlds/{Esc(worldId)}/relationships", body, onSuccess, onError);

        #endregion

        #region Instances

        /// <summary>
        /// Fork a per-player instance of a world (POST /worlds/{id}/instances).
        /// Instances default to "paused" — activating opts in to autonomous
        /// (billable) event generation. 409 if the player already has one.
        /// </summary>
        public IEnumerator CreateInstance(
            string worldId, string playerId,
            Action<SimulationInstance> onSuccess, Action<string> onError,
            string status = null)
            => Send("POST", $"/worlds/{Esc(worldId)}/instances",
                new InstanceCreateBody { playerId = playerId, status = status },
                onSuccess, onError);

        /// <summary>List a world's instances (GET /worlds/{id}/instances), newest first.</summary>
        public IEnumerator ListInstances(
            string worldId,
            Action<SimulationInstanceList> onSuccess, Action<string> onError,
            int limit = 50, int offset = 0)
            => Send("GET", $"/worlds/{Esc(worldId)}/instances?limit={limit}&offset={offset}",
                null, onSuccess, onError);

        /// <summary>Get instance state with evolved relationships and recent events (GET /instances/{id}).</summary>
        public IEnumerator GetInstance(
            string instanceId,
            Action<SimulationInstance> onSuccess, Action<string> onError)
            => Send("GET", $"/instances/{Esc(instanceId)}", null, onSuccess, onError);

        /// <summary>Activate or pause an instance (POST /instances/{id}/status; status "active" | "paused").</summary>
        public IEnumerator SetInstanceStatus(
            string instanceId, string status,
            Action<SimulationInstance> onSuccess, Action<string> onError)
            => Send("POST", $"/instances/{Esc(instanceId)}/status",
                new InstanceStatusBody { status = status }, onSuccess, onError);

        /// <summary>Delete an instance and all data it generated (DELETE /instances/{id}). Must be paused first (409 otherwise).</summary>
        public IEnumerator DeleteInstance(string instanceId, Action onSuccess, Action<string> onError)
            => SendNoContent("DELETE", $"/instances/{Esc(instanceId)}", null, onSuccess, onError);

        #endregion

        #region Conversations

        /// <summary>
        /// Trigger a character-to-character (or group) conversation
        /// (POST /instances/{id}/conversations). Immediate unless ScheduledAt
        /// is set; connect EstuarySimulationStream first to watch immediate
        /// triggers live. Rate-limited (10/min) and quota-enforced (429).
        /// </summary>
        public IEnumerator TriggerConversation(
            string instanceId, SimulationConversationTrigger body,
            Action<SimulationEvent> onSuccess, Action<string> onError)
            => Send("POST", $"/instances/{Esc(instanceId)}/conversations", body, onSuccess, onError,
                timeout: 30);

        /// <summary>List an instance's events (GET /instances/{id}/events), newest first, optionally filtered by status.</summary>
        public IEnumerator ListEvents(
            string instanceId,
            Action<SimulationEventList> onSuccess, Action<string> onError,
            string status = null, int limit = 50, int offset = 0)
        {
            var query = $"?limit={limit}&offset={offset}";
            if (!string.IsNullOrEmpty(status)) query += $"&status={Esc(status)}";
            return Send<SimulationEventList>(
                "GET", $"/instances/{Esc(instanceId)}/events{query}", null, onSuccess, onError);
        }

        /// <summary>List an instance's lore log (GET /instances/{id}/lore), newest first.</summary>
        public IEnumerator ListLore(
            string instanceId,
            Action<SimulationLoreList> onSuccess, Action<string> onError,
            int limit = 50, int offset = 0)
            => Send("GET", $"/instances/{Esc(instanceId)}/lore?limit={limit}&offset={offset}",
                null, onSuccess, onError);

        /// <summary>
        /// Get the instance's living world-view document
        /// (GET /instances/{id}/world-view). Markdown is null until the first
        /// conversation completes; also streamed as simulation_world_view.
        /// </summary>
        public IEnumerator GetWorldView(
            string instanceId,
            Action<SimulationWorldView> onSuccess, Action<string> onError)
            => Send("GET", $"/instances/{Esc(instanceId)}/world-view", null, onSuccess, onError);

        /// <summary>Get a conversation transcript (GET /instances/{id}/conversations/{conversationId}).</summary>
        public IEnumerator GetConversation(
            string instanceId, string conversationId,
            Action<SimulationConversation> onSuccess, Action<string> onError)
            => Send("GET", $"/instances/{Esc(instanceId)}/conversations/{Esc(conversationId)}",
                null, onSuccess, onError);

        #endregion

        #region Request plumbing

        [Serializable]
        private class InstanceCreateBody
        {
            public string playerId;
            public string status;  // null → omitted → server default "paused"
        }

        [Serializable]
        private class InstanceStatusBody
        {
            public string status;
        }

        private static string Esc(string segment) => UnityWebRequest.EscapeURL(segment ?? "");

        private IEnumerator SendNoContent(
            string method, string path, object body, Action onSuccess, Action<string> onError)
            => Send<object>(method, path, body, _ => onSuccess?.Invoke(), onError);

        private IEnumerator Send<T>(
            string method, string path, object body,
            Action<T> onSuccess, Action<string> onError, int timeout = 15)
        {
            string token = null;
            yield return _http.ResolveToken(t => token = t);

            var url = _serverUrl + BasePath + path;

            using (var request = new UnityWebRequest(url, method))
            {
                if (body != null)
                {
                    var json = JsonConvert.SerializeObject(body, RequestSettings);
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                    request.SetRequestHeader("Content-Type", "application/json");
                }
                request.downloadHandler = new DownloadHandlerBuffer();
                _http.ApplyAuth(request, token);
                request.timeout = timeout;

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke(DescribeError(request));
                    yield break;
                }

                if (request.responseCode == 204 ||
                    string.IsNullOrEmpty(request.downloadHandler.text))
                {
                    onSuccess?.Invoke(default);
                    yield break;
                }

                T parsed;
                try
                {
                    parsed = JsonConvert.DeserializeObject<T>(request.downloadHandler.text);
                }
                catch (Exception e)
                {
                    onError?.Invoke($"Failed to parse response: {e.Message}");
                    yield break;
                }
                onSuccess?.Invoke(parsed);
            }
        }

        /// <summary>
        /// Include the response body in errors — the server explains failures
        /// there ({"detail": ...}: validation, ownership 404s, 409 conflicts,
        /// 429 rate/quota) and UnityWebRequest.error is just the status line.
        /// </summary>
        private static string DescribeError(UnityWebRequest request)
        {
            var bodyText = request.downloadHandler?.text;
            return string.IsNullOrEmpty(bodyText)
                ? request.error
                : $"{request.error}: {bodyText}";
        }

        #endregion
    }
}
