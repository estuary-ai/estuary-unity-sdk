using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using UnityEngine;
using Estuary.Models;
using Newtonsoft.Json;

namespace Estuary
{
    /// <summary>
    /// Socket.IO client for the /sim-v1 namespace (SDK_CONTRACT.md §Live
    /// streaming). Streams immediate conversation triggers for ONE world
    /// instance in real time: simulation_started / _message / _tool_call /
    /// _lore / _world_view / _complete / _error.
    ///
    /// Auth is {apiKey, instanceId} — the key's owner must own the instance
    /// (the server silently rejects the namespace connect otherwise; that
    /// surfaces here as OnError "Connection error: ..."). There is no Firebase
    /// token path on this namespace, so an API key is required.
    ///
    /// Scheduled (engine-processed) events do NOT stream — poll
    /// EstuarySimulationApi.ListEvents for those.
    ///
    /// Like EstuaryClient, events arrive on a background thread and are
    /// queued; call ProcessMainThreadQueue() from Update() (the
    /// EstuarySimulation component does this for you).
    /// </summary>
    public class EstuarySimulationStream : IDisposable
    {
        private const string SIM_V1_NAMESPACE = "/sim-v1";
        private const int RECONNECT_DELAY_MS = 2000;
        private const int MAX_RECONNECT_ATTEMPTS = 5;

        #region Events

        /// <summary>Connected to the namespace (auth accepted, room joined).</summary>
        public event Action OnConnected;

        /// <summary>Disconnected from the server (reason string).</summary>
        public event Action<string> OnDisconnected;

        /// <summary>Transport/connection error (including rejected auth).</summary>
        public event Action<string> OnError;

        /// <summary>A conversation started processing (eventId).</summary>
        public event Action<string> OnSimulationStarted;

        /// <summary>A character spoke a line.</summary>
        public event Action<SimulationStreamMessage> OnMessage;

        /// <summary>A character used a tool.</summary>
        public event Action<SimulationToolCall> OnToolCall;

        /// <summary>The conversation's lore summary was saved (text).</summary>
        public event Action<string> OnLore;

        /// <summary>The instance's world-view document was rewritten (full markdown).</summary>
        public event Action<string> OnWorldView;

        /// <summary>The conversation finished successfully.</summary>
        public event Action OnComplete;

        /// <summary>The conversation failed server-side (error message).</summary>
        public event Action<string> OnSimulationError;

        #endregion

        #region Properties

        /// <summary>Whether the namespace connection is established.</summary>
        public bool IsConnected { get; private set; }

        /// <summary>The instance whose stream this client is subscribed to.</summary>
        public string InstanceId { get; private set; }

        /// <summary>Enable debug logging.</summary>
        public bool DebugLogging { get; set; }

        #endregion

        #region Private Fields

        private string _serverUrl;
        private string _apiKey;
        private ISocketIOConnection _socket;
        private int _reconnectAttempts;
        private bool _clientDisconnect;
        private bool _disposed;
        private readonly ConcurrentQueue<Action> _mainThreadQueue = new ConcurrentQueue<Action>();

        #endregion

        #region Public Methods

        /// <summary>
        /// Connect to the /sim-v1 stream for one instance.
        /// </summary>
        /// <param name="serverUrl">The Estuary server URL (e.g., https://api.estuary-ai.com)</param>
        /// <param name="apiKey">The developer API key that owns the instance</param>
        /// <param name="instanceId">The world instance to stream</param>
        public async Task ConnectAsync(string serverUrl, string apiKey, string instanceId)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(EstuarySimulationStream));
            if (string.IsNullOrEmpty(serverUrl))
                throw new ArgumentNullException(nameof(serverUrl));
            if (string.IsNullOrEmpty(apiKey))
                throw new ArgumentException(
                    "The /sim-v1 stream requires an API key (no Firebase token path)",
                    nameof(apiKey));
            if (string.IsNullOrEmpty(instanceId))
                throw new ArgumentNullException(nameof(instanceId));

            _serverUrl = serverUrl;
            _apiKey = apiKey;
            InstanceId = instanceId;
            _clientDisconnect = false;
            _reconnectAttempts = 0;

            await ConnectInternalAsync();
        }

        /// <summary>Disconnect from the stream (no reconnect will follow).</summary>
        public async Task DisconnectAsync()
        {
            if (_disposed) return;
            _clientDisconnect = true;

            if (_socket != null)
            {
                try
                {
                    await _socket.DisconnectAsync();
                }
                catch (Exception e)
                {
                    Log($"Error during disconnect: {e.Message}");
                }
                _socket = null;
            }
            IsConnected = false;
        }

        /// <summary>Process queued events on the main thread. Call this from Update().</summary>
        public void ProcessMainThreadQueue()
        {
            while (_mainThreadQueue.TryDequeue(out var action))
            {
                try
                {
                    action?.Invoke();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _clientDisconnect = true;

            if (_socket != null)
            {
                _socket.OnConnected -= HandleConnected;
                _socket.OnDisconnected -= HandleDisconnected;
                _socket.OnError -= HandleError;
                _socket = null;
            }
            IsConnected = false;
        }

        #endregion

        #region Private Methods

        [Serializable]
        private class SimV1Auth
        {
            // Field names are the wire keys (JsonUtility serialization in the
            // socket layer): auth {apiKey, instanceId} per SDK_CONTRACT.md.
            public string apiKey;
            public string instanceId;
        }

        private async Task ConnectInternalAsync()
        {
            try
            {
                _socket = new BuiltInSocketIOConnection();

                _socket.OnConnected += HandleConnected;
                _socket.OnDisconnected += HandleDisconnected;
                _socket.OnError += HandleError;

                _socket.On("simulation_started", json =>
                    DispatchParsed<StartedPayload>(json, "simulation_started",
                        p => OnSimulationStarted?.Invoke(p.eventId)));
                _socket.On("simulation_message", json =>
                    DispatchParsed<SimulationStreamMessage>(json, "simulation_message",
                        p => OnMessage?.Invoke(p)));
                _socket.On("simulation_tool_call", json =>
                    DispatchParsed<SimulationToolCall>(json, "simulation_tool_call",
                        p => OnToolCall?.Invoke(p)));
                _socket.On("simulation_lore", json =>
                    DispatchParsed<LorePayload>(json, "simulation_lore",
                        p => OnLore?.Invoke(p.text)));
                _socket.On("simulation_world_view", json =>
                    DispatchParsed<WorldViewPayload>(json, "simulation_world_view",
                        p => OnWorldView?.Invoke(p.markdown)));
                _socket.On("simulation_complete", _ =>
                    _mainThreadQueue.Enqueue(() => OnComplete?.Invoke()));
                _socket.On("simulation_error", json =>
                    DispatchParsed<ErrorPayload>(json, "simulation_error",
                        p => OnSimulationError?.Invoke(p.error)));

                var auth = new SimV1Auth { apiKey = _apiKey, instanceId = InstanceId };
                await _socket.ConnectAsync(_serverUrl, SIM_V1_NAMESPACE, auth);
                Log($"Connecting to {_serverUrl}{SIM_V1_NAMESPACE} for instance {InstanceId}...");
            }
            catch (Exception e)
            {
                LogError($"Stream connection failed: {e.Message}");
                _mainThreadQueue.Enqueue(() => OnError?.Invoke(e.Message));
                await HandleReconnect();
            }
        }

        private void HandleConnected()
        {
            IsConnected = true;
            _reconnectAttempts = 0;
            Log("Connected to /sim-v1 stream");
            _mainThreadQueue.Enqueue(() => OnConnected?.Invoke());
        }

        private void HandleDisconnected(string reason)
        {
            IsConnected = false;
            Log($"Stream disconnected: {reason}");
            _mainThreadQueue.Enqueue(() => OnDisconnected?.Invoke(reason));

            if (reason != "client disconnect" && !_clientDisconnect)
            {
                _ = HandleReconnect();
            }
        }

        private void HandleError(string error)
        {
            LogError($"Stream error: {error}");
            _mainThreadQueue.Enqueue(() => OnError?.Invoke(error));
        }

        private async Task HandleReconnect()
        {
            if (_disposed || _clientDisconnect) return;
            if (_reconnectAttempts >= MAX_RECONNECT_ATTEMPTS)
            {
                LogError("Max stream reconnect attempts reached");
                return;
            }

            _reconnectAttempts++;
            Log($"Reconnecting stream (attempt {_reconnectAttempts}/{MAX_RECONNECT_ATTEMPTS})...");
            await Task.Delay(RECONNECT_DELAY_MS);
            if (_disposed || _clientDisconnect) return;
            await ConnectInternalAsync();
        }

        private void DispatchParsed<T>(string json, string eventName, Action<T> handler)
        {
            T payload;
            try
            {
                payload = JsonConvert.DeserializeObject<T>(json);
            }
            catch (Exception e)
            {
                LogError($"Failed to parse {eventName}: {e.Message}");
                return;
            }
            if (payload == null) return;
            _mainThreadQueue.Enqueue(() => handler(payload));
        }

        // Single-field payload envelopes (public fields = wire keys)
        [Serializable] private class StartedPayload { public string eventId; }
        [Serializable] private class LorePayload { public string text; }
        [Serializable] private class WorldViewPayload { public string markdown; }
        [Serializable] private class ErrorPayload { public string error; }

        private void Log(string message)
        {
            if (DebugLogging) Debug.Log($"[EstuarySimulationStream] {message}");
        }

        private void LogError(string message)
        {
            Debug.LogError($"[EstuarySimulationStream] {message}");
        }

        #endregion
    }
}
