using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Text;
using Estuary.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;

namespace Estuary
{
    public partial class EstuaryHttpClient
    {
        /// <summary>Stream a text-only HTTP turn as NDJSON. Events retain arrival order; rich results remain separate from prose.</summary>
        public IEnumerator StreamTurn(string characterId, CharacterTurnRequest body, Action<CharacterTurnEvent> onEvent,
            Action<CharacterTurnResponse> onCompleted, Action<string> onError)
        {
            string token = null;
            yield return ResolveToken(t => token = t);
            if (_config.TokenProvider != null && string.IsNullOrEmpty(token))
            {
                onError?.Invoke("Authentication token provider failed; no API-key fallback was attempted.");
                yield break;
            }
            var payload = JObject.FromObject(body, JsonSerializer.Create(RequestSettings));
            payload["stream"] = true;
            var spec = new RestRequest { Method = "POST", Url = _serverUrl + CharacterPath(characterId) + "/turn",
                Body = payload, Headers = RequestHeaders(token) };
            var reader = new TurnStreamReader();
            CharacterTurnResponse result = null;
            string error = null;
            Action drain = () =>
            {
                while (reader.Events.TryDequeue(out var evt))
                {
                    if (result != null || error != null) continue;
                    if (evt.Type == "turn_completed")
                    {
                        try { result = evt.Data.ToObject<CharacterTurnResponse>(); }
                        catch (JsonException e) { error = "Invalid turn completion: " + e.Message; continue; }
                    }
                    if (evt.Type == "error") error = (string)evt.Data?["message"] ?? evt.Data?.ToString() ?? "Turn failed";
                    onEvent?.Invoke(evt);
                }
            };

            if (TransportForTest != null)
            {
                var response = TransportForTest(spec);
                if (response.Status < 200 || response.Status >= 300) error = $"HTTP {response.Status}: {response.Body}";
                else
                {
                    var bytes = Encoding.UTF8.GetBytes(response.Body ?? "");
                    reader.Append(bytes, bytes.Length);
                    reader.Finish();
                    drain();
                }
            }
            else
            {
                using (var request = new UnityWebRequest(spec.Url, "POST"))
                {
                    var handler = new TurnDownloadHandler(reader);
                    request.downloadHandler = handler;
                    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload.ToString(Formatting.None)));
                    request.SetRequestHeader("Content-Type", "application/json");
                    request.SetRequestHeader("Accept", "application/x-ndjson");
                    foreach (var header in spec.Headers) request.SetRequestHeader(header.Key, header.Value);
                    request.timeout = (body.TimeoutSeconds ?? 45) + 15;
                    request.redirectLimit = 0;
                    var operation = request.SendWebRequest();
                    while (!operation.isDone)
                    {
                        // Never publish a JSON HTTP error as a stream event.
                        if (request.responseCode >= 200 && request.responseCode < 300) drain();
                        if (reader.Error != null) { request.Abort(); break; }
                        yield return null;
                    }
                    if (request.responseCode < 200 || request.responseCode >= 300)
                        error = $"HTTP {request.responseCode}: {request.error} {reader.ResponsePrefix}";
                    else if (request.result != UnityWebRequest.Result.Success)
                        error = reader.Error ?? request.error;
                    else { reader.Finish(); drain(); }
                }
            }
            error = error ?? reader.Error;
            if (error != null) onError?.Invoke(error);
            else if (result == null) onError?.Invoke("Turn stream ended without turn_completed.");
            else onCompleted?.Invoke(result);
        }
    }

    internal sealed class TurnDownloadHandler : DownloadHandlerScript
    {
        private readonly TurnStreamReader _reader;
        internal TurnDownloadHandler(TurnStreamReader reader) : base(new byte[8192]) { _reader = reader; }
        protected override bool ReceiveData(byte[] data, int length)
        {
            _reader.Append(data, length);
            return _reader.Error == null;
        }
    }

    // Handles UTF-8 split across network packets and a final line without a newline.
    internal sealed class TurnStreamReader
    {
        internal readonly ConcurrentQueue<CharacterTurnEvent> Events = new ConcurrentQueue<CharacterTurnEvent>();
        private readonly Decoder _decoder = new UTF8Encoding(false, true).GetDecoder();
        private readonly StringBuilder _line = new StringBuilder();
        private readonly StringBuilder _prefix = new StringBuilder();
        internal string Error { get; private set; }
        internal string ResponsePrefix => _prefix.ToString();

        internal void Append(byte[] data, int length)
        {
            if (Error != null || length == 0) return;
            try
            {
                var chars = new char[Encoding.UTF8.GetMaxCharCount(length)];
                int count = _decoder.GetChars(data, 0, length, chars, 0, false);
                for (int i = 0; i < count; i++)
                {
                    if (_prefix.Length < 4096) _prefix.Append(chars[i]);
                    if (chars[i] == '\n') ParseLine();
                    else _line.Append(chars[i]);
                    if (_line.Length > 1024 * 1024) throw new JsonException("Turn event exceeds 1 MiB.");
                }
            }
            catch (Exception e) { Error = "Invalid turn stream: " + e.Message; }
        }

        internal void Finish()
        {
            if (Error != null) return;
            try
            {
                _decoder.GetChars(Array.Empty<byte>(), 0, 0, new char[4], 0, true);
                ParseLine();
            }
            catch (Exception e) { Error = "Invalid turn stream: " + e.Message; }
        }

        private void ParseLine()
        {
            var text = _line.ToString().Trim();
            _line.Clear();
            if (text.Length == 0) return;
            var evt = JsonConvert.DeserializeObject<CharacterTurnEvent>(text);
            if (evt == null || string.IsNullOrEmpty(evt.Type) || evt.Data == null)
                throw new JsonException("Expected a {type,data} turn event.");
            Events.Enqueue(evt);
        }
    }
}
