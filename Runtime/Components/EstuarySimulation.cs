using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using Estuary.Models;

namespace Estuary
{
    /// <summary>
    /// User-facing component for the Estuary character simulation (v1):
    /// run character-to-character conversations in a world instance and
    /// watch them stream live.
    ///
    /// - REST (worlds / instances / triggers / lore / world view / transcripts)
    ///   via the <see cref="Api"/> client — call its coroutines with this
    ///   component's StartCoroutine, or use the conveniences below.
    /// - Live streaming of immediate triggers via <see cref="Stream"/>
    ///   (/sim-v1 Socket.IO namespace) — connect with ConnectStream() after
    ///   setting InstanceId, then TriggerConversation and listen to the
    ///   events on this component.
    ///
    /// Requires an API key on the EstuaryConfig: /sim-v1 has no Firebase
    /// token path (REST calls fall back to a TokenProvider when configured).
    /// </summary>
    [AddComponentMenu("Estuary/Estuary Simulation")]
    public class EstuarySimulation : MonoBehaviour
    {
        #region Inspector Fields

        [SerializeField]
        [Tooltip("Estuary configuration (server URL + API key). Required.")]
        private EstuaryConfig config;

        [SerializeField]
        [Tooltip("World instance to stream/trigger against. Create one via Api.CreateInstance, or paste an existing id.")]
        private string instanceId = "";

        [SerializeField]
        [Tooltip("Connect the /sim-v1 live stream on Start (requires instanceId to be set).")]
        private bool connectStreamOnStart = false;

        [SerializeField]
        [Tooltip("World the instance above belongs to. Required for Destroy World On End / EndWorld().")]
        private string worldId = "";

        [SerializeField]
        [Tooltip("When the world ends (EndWorld(), or this component being destroyed at runtime), " +
                 "delete the world on the server — instances, transcripts, AND every memory its " +
                 "conversations created — so the next world starts as a fresh session with no " +
                 "bleed-through memories.")]
        private bool destroyWorldOnEnd = false;

        #endregion

        #region Events

        /// <summary>Live stream connected for the current instance.</summary>
        public event Action OnStreamConnected;

        /// <summary>Live stream disconnected (reason).</summary>
        public event Action<string> OnStreamDisconnected;

        /// <summary>Stream transport error, including rejected auth (bad key / unowned instance).</summary>
        public event Action<string> OnStreamError;

        /// <summary>A conversation started processing (eventId).</summary>
        public event Action<string> OnSimulationStarted;

        /// <summary>A character spoke a line.</summary>
        public event Action<SimulationStreamMessage> OnSimulationMessage;

        /// <summary>A character used a tool.</summary>
        public event Action<SimulationToolCall> OnSimulationToolCall;

        /// <summary>The conversation's lore summary was saved (text).</summary>
        public event Action<string> OnSimulationLore;

        /// <summary>The instance's world-view document was rewritten (full markdown).</summary>
        public event Action<string> OnWorldViewUpdated;

        /// <summary>
        /// A participant's private motive evolved (characterId, motive) — one
        /// invocation per changed participant. Motives are private: injected
        /// only into that character's own prompts. Contract v1.7.
        /// </summary>
        public event Action<string, string> OnMotiveUpdated;

        /// <summary>The conversation finished successfully.</summary>
        public event Action OnSimulationComplete;

        /// <summary>The conversation failed server-side (error message).</summary>
        public event Action<string> OnSimulationError;

        /// <summary>The world was destroyed server-side by EndWorld() (destroyWorldOnEnd).</summary>
        public event Action OnWorldDestroyed;

        #endregion

        #region Properties

        /// <summary>REST client for /api/v1/simulation/*. Lazily created from the config.</summary>
        public EstuarySimulationApi Api
        {
            get
            {
                if (_api == null)
                {
                    if (config == null)
                    {
                        Debug.LogError("[EstuarySimulation] EstuaryConfig is not assigned.");
                        return null;
                    }
                    _api = new EstuarySimulationApi(config);
                }
                return _api;
            }
        }

        /// <summary>The live /sim-v1 stream client (null until ConnectStream).</summary>
        public EstuarySimulationStream Stream => _stream;

        /// <summary>Whether the live stream is connected.</summary>
        public bool IsStreamConnected => _stream?.IsConnected ?? false;

        /// <summary>
        /// The world instance this component targets. Setting it while the
        /// stream is connected does NOT re-subscribe — call ConnectStream()
        /// again to switch the stream to the new instance.
        /// </summary>
        public string InstanceId
        {
            get => instanceId;
            set => instanceId = value;
        }

        /// <summary>The world InstanceId belongs to (used by EndWorld / destroyWorldOnEnd).</summary>
        public string WorldId
        {
            get => worldId;
            set => worldId = value;
        }

        /// <summary>Whether ending the world (EndWorld / runtime destruction) deletes it server-side.</summary>
        public bool DestroyWorldOnEnd
        {
            get => destroyWorldOnEnd;
            set => destroyWorldOnEnd = value;
        }

        #endregion

        #region Private Fields

        private EstuarySimulationApi _api;
        private EstuarySimulationStream _stream;
        private bool _worldDestroyRequested;

        #endregion

        #region Unity Lifecycle

        private void Start()
        {
            if (connectStreamOnStart)
            {
                if (string.IsNullOrEmpty(instanceId))
                {
                    Debug.LogWarning(
                        "[EstuarySimulation] connectStreamOnStart is set but instanceId is empty.");
                }
                else
                {
                    ConnectStream();
                }
            }
        }

        private void Update()
        {
            _stream?.ProcessMainThreadQueue();
        }

        private void OnDestroy()
        {
            TeardownStream();
            if (Application.isPlaying && destroyWorldOnEnd && !_worldDestroyRequested)
            {
                FireBestEffortWorldDestroy();
            }
        }

        #endregion

        #region World Lifecycle

        /// <summary>
        /// End the world session: disconnects the live stream and, when
        /// Destroy World On End is enabled, deletes the world server-side —
        /// instances, transcripts, and every memory its conversations created
        /// go with it, so the next world starts as a fresh session with no
        /// bleed-through memories. Prefer calling this at a controlled moment
        /// (end screen, scene transition) over relying on OnDestroy, where the
        /// delete is fire-and-forget and may be dropped on application quit.
        /// </summary>
        public void EndWorld(Action onDestroyed = null, Action<string> onError = null)
        {
            TeardownStream();

            if (!destroyWorldOnEnd) return;
            if (string.IsNullOrEmpty(worldId))
            {
                Debug.LogWarning("[EstuarySimulation] destroyWorldOnEnd is set but worldId is empty.");
                return;
            }

            var api = Api;
            if (api == null) return;

            var endedWorldId = worldId;
            StartCoroutine(api.DeleteWorld(endedWorldId,
                () =>
                {
                    // Only a CONFIRMED delete suppresses the OnDestroy fallback. If this
                    // component is destroyed while the DELETE is still in flight, the
                    // coroutine dies with it (UnityWebRequest aborted, no callback), and
                    // OnDestroy must still fire the best-effort raw request. A duplicate
                    // DELETE when both go through is harmless — the second returns 404.
                    _worldDestroyRequested = true;
                    if (config != null && config.DebugLogging)
                        Debug.Log($"[EstuarySimulation] World {endedWorldId} destroyed (memories purged).");
                    OnWorldDestroyed?.Invoke();
                    onDestroyed?.Invoke();
                },
                error =>
                {
                    // Flag was never set, so OnDestroy retries best-effort.
                    Debug.LogError($"[EstuarySimulation] World destroy failed: {error}");
                    onError?.Invoke(error);
                }));
        }

        /// <summary>
        /// Fire-and-forget DELETE /worlds/{id} for teardown paths where
        /// coroutines can no longer run (OnDestroy). Survives scene unloads
        /// but may be dropped on application quit — call EndWorld() for a
        /// guaranteed destroy. API-key auth only: a TokenProvider can't be
        /// resolved synchronously here (and falling back to the key would
        /// silently escalate to developer-level access).
        /// </summary>
        private void FireBestEffortWorldDestroy()
        {
            if (string.IsNullOrEmpty(worldId))
            {
                Debug.LogWarning("[EstuarySimulation] destroyWorldOnEnd is set but worldId is empty.");
                return;
            }
            if (config == null) return;
            if (config.TokenProvider != null || string.IsNullOrEmpty(config.ApiKey))
            {
                Debug.LogWarning(
                    "[EstuarySimulation] destroyWorldOnEnd needs an API key for the teardown-time " +
                    "destroy — call EndWorld() explicitly before this component is destroyed.");
                return;
            }

            var url = $"{config.ServerUrl.TrimEnd('/')}/api/v1/simulation/worlds/" +
                      UnityWebRequest.EscapeURL(worldId);
            var request = UnityWebRequest.Delete(url);
            request.SetRequestHeader("X-API-Key", config.ApiKey);
            request.timeout = 10;
            var op = request.SendWebRequest();
            op.completed += _ => request.Dispose();
        }

        #endregion

        #region Stream Lifecycle

        /// <summary>
        /// Connect the live stream for InstanceId (or switch it to
        /// <paramref name="newInstanceId"/> when given). Replaces any
        /// existing stream connection.
        /// </summary>
        public async void ConnectStream(string newInstanceId = null)
        {
            if (!string.IsNullOrEmpty(newInstanceId))
            {
                instanceId = newInstanceId;
            }

            if (config == null)
            {
                Debug.LogError("[EstuarySimulation] EstuaryConfig is not assigned.");
                return;
            }
            if (string.IsNullOrEmpty(config.ApiKey))
            {
                Debug.LogError(
                    "[EstuarySimulation] The /sim-v1 stream requires an API key on the config.");
                return;
            }
            if (string.IsNullOrEmpty(instanceId))
            {
                Debug.LogError("[EstuarySimulation] instanceId is not set.");
                return;
            }

            TeardownStream();

            _stream = new EstuarySimulationStream { DebugLogging = config.DebugLogging };
            _stream.OnConnected += HandleStreamConnected;
            _stream.OnDisconnected += HandleStreamDisconnected;
            _stream.OnError += HandleStreamError;
            _stream.OnSimulationStarted += HandleSimulationStarted;
            _stream.OnMessage += HandleSimulationMessage;
            _stream.OnToolCall += HandleSimulationToolCall;
            _stream.OnLore += HandleSimulationLore;
            _stream.OnWorldView += HandleWorldView;
            _stream.OnMotiveUpdated += HandleMotiveUpdated;
            _stream.OnComplete += HandleSimulationComplete;
            _stream.OnSimulationError += HandleSimulationError;

            try
            {
                await _stream.ConnectAsync(config.ServerUrl, config.ApiKey, instanceId);
            }
            catch (Exception e)
            {
                Debug.LogError($"[EstuarySimulation] Stream connect failed: {e.Message}");
                OnStreamError?.Invoke(e.Message);
            }
        }

        /// <summary>Disconnect the live stream (REST via Api keeps working).</summary>
        public async void DisconnectStream()
        {
            if (_stream == null) return;
            try
            {
                await _stream.DisconnectAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[EstuarySimulation] Stream disconnect error: {e.Message}");
            }
        }

        private void TeardownStream()
        {
            if (_stream == null) return;
            // Drain anything already queued so late events don't vanish silently
            _stream.ProcessMainThreadQueue();
            var old = _stream;
            _stream = null;
            _ = old.DisconnectAsync();
            old.Dispose();
        }

        #endregion

        #region Conveniences

        /// <summary>
        /// Trigger an immediate pair conversation on InstanceId. Connect the
        /// stream first to watch it live; onTriggered receives the created
        /// event (its ResultConversationId fills in once completed).
        /// </summary>
        public Coroutine TriggerConversation(
            string sourceCharacterId, string targetCharacterId, string intent,
            Action<SimulationEvent> onTriggered = null, Action<string> onError = null)
        {
            return RunApi(api => api.TriggerConversation(
                instanceId,
                new SimulationConversationTrigger
                {
                    SourceCharacterId = sourceCharacterId,
                    TargetCharacterId = targetCharacterId,
                    Intent = intent,
                },
                onTriggered, WrapError(onError)));
        }

        /// <summary>Trigger an immediate group conversation (2–6 participants) on InstanceId.</summary>
        public Coroutine TriggerGroupConversation(
            IList<string> participantIds, string intent,
            Action<SimulationEvent> onTriggered = null, Action<string> onError = null)
        {
            return RunApi(api => api.TriggerConversation(
                instanceId,
                new SimulationConversationTrigger
                {
                    ParticipantIds = new List<string>(participantIds),
                    Intent = intent,
                },
                onTriggered, WrapError(onError)));
        }

        /// <summary>
        /// Fetch the instance's world-view document via REST. The same
        /// document also streams as OnWorldViewUpdated after each
        /// conversation; Markdown is null until the first one completes.
        /// </summary>
        public Coroutine FetchWorldView(
            Action<SimulationWorldView> onSuccess, Action<string> onError = null)
        {
            return RunApi(api => api.GetWorldView(instanceId, onSuccess, WrapError(onError)));
        }

        /// <summary>Fetch the instance's lore log via REST, newest first.</summary>
        public Coroutine FetchLore(
            Action<SimulationLoreList> onSuccess, Action<string> onError = null,
            int limit = 50, int offset = 0)
        {
            return RunApi(api => api.ListLore(instanceId, onSuccess, WrapError(onError), limit, offset));
        }

        /// <summary>
        /// Soft reset: delete every memory this world's simulation created
        /// (across all its instances), keeping the world, instances, lore,
        /// and transcripts. Use EndWorld()/destroyWorldOnEnd instead to
        /// destroy the world entirely.
        /// </summary>
        public Coroutine ClearWorldMemories(
            Action<SimulationWorldMemoriesCleared> onSuccess = null, Action<string> onError = null)
        {
            var api = Api;
            if (api == null) return null;
            if (string.IsNullOrEmpty(worldId))
            {
                Debug.LogError("[EstuarySimulation] worldId is not set.");
                return null;
            }
            return StartCoroutine(api.ClearWorldMemories(worldId, onSuccess, WrapError(onError)));
        }

        private Coroutine RunApi(Func<EstuarySimulationApi, System.Collections.IEnumerator> call)
        {
            var api = Api;
            if (api == null) return null;
            if (string.IsNullOrEmpty(instanceId))
            {
                Debug.LogError("[EstuarySimulation] instanceId is not set.");
                return null;
            }
            return StartCoroutine(call(api));
        }

        private Action<string> WrapError(Action<string> onError)
        {
            return error =>
            {
                Debug.LogError($"[EstuarySimulation] {error}");
                onError?.Invoke(error);
            };
        }

        #endregion

        #region Stream Handlers

        private void HandleStreamConnected() => OnStreamConnected?.Invoke();
        private void HandleStreamDisconnected(string reason) => OnStreamDisconnected?.Invoke(reason);
        private void HandleStreamError(string error) => OnStreamError?.Invoke(error);
        private void HandleSimulationStarted(string eventId) => OnSimulationStarted?.Invoke(eventId);
        private void HandleSimulationMessage(SimulationStreamMessage msg) => OnSimulationMessage?.Invoke(msg);
        private void HandleSimulationToolCall(SimulationToolCall call) => OnSimulationToolCall?.Invoke(call);
        private void HandleSimulationLore(string text) => OnSimulationLore?.Invoke(text);
        private void HandleWorldView(string markdown) => OnWorldViewUpdated?.Invoke(markdown);

        private void HandleMotiveUpdated(string characterId, string motive) =>
            OnMotiveUpdated?.Invoke(characterId, motive);
        private void HandleSimulationComplete() => OnSimulationComplete?.Invoke();
        private void HandleSimulationError(string error) => OnSimulationError?.Invoke(error);

        #endregion

        #region Configuration

        /// <summary>Assign or replace the config used for REST + streaming.</summary>
        public void SetConfig(EstuaryConfig cfg)
        {
            config = cfg;
            _api = null;  // rebuilt lazily from the new config
        }

        #endregion
    }
}
