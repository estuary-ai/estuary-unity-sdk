using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Estuary.Models;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor.PackageManager;
using UnityEngine;

namespace Estuary.Tests
{
    public class RestConformanceTests
    {
        private EstuaryConfig _config;
        private EstuaryHttpClient _http;
        private RestRequest _request;
        private RestResponse _response;
        private string _error;
        private int _calls;
        private static string PackageRoot => PackageInfo.FindForAssembly(typeof(EstuaryClient).Assembly).resolvedPath;
        private static JObject Fixture => JObject.Parse(File.ReadAllText(Path.Combine(PackageRoot, "Tests/Fixtures/rest.json")));

        [SetUp]
        public void SetUp()
        {
            _config = ScriptableObject.CreateInstance<EstuaryConfig>();
            _config.SetApiKeyRuntime("test-key");
            _config.SetServerUrlRuntime("https://estuary.invalid");
            _http = new EstuaryHttpClient(_config);
            _response = new RestResponse { Status = 200, Body = "{}" };
            _calls = 0;
            _error = null;
            _http.TransportForTest = request => { _calls++; _request = request; return _response; };
        }

        [TearDown] public void TearDown() => UnityEngine.Object.DestroyImmediate(_config);

        private static IEnumerable<TestCaseData> Cases()
            => Fixture["cases"].Where(c => c["sdk"]?["unity"] != null)
                .Select(c => new TestCaseData(c).SetName("REST_" + (string)c["id"]));

        internal static void Run(IEnumerator routine)
        {
            while (routine.MoveNext())
                if (routine.Current is IEnumerator nested) Run(nested);
        }

        private void Failed(string error) => _error = error;

        [TestCaseSource(nameof(Cases))]
        public void PublicMethodMatchesSharedContract(JToken test)
        {
            string c = (string)Fixture["args"]["characterId"], p = (string)Fixture["args"]["playerId"];
            var call = test["call"];
            IEnumerator routine;
            switch ((string)test["id"])
            {
                case "memories.list": routine = _http.GetMemories(c, p, null, Failed, (int)call["limit"], (int)call["offset"], (string)call["memoryType"], (string)call["status"]); break;
                case "memories.timeline": routine = _http.GetMemoryTimeline(c, p, null, Failed, (string)call["groupBy"]); break;
                case "memories.stats": routine = _http.GetMemoryStats(c, p, null, Failed); break;
                case "memories.coreFacts": routine = _http.GetCoreFacts(c, p, null, Failed); break;
                case "memories.graph": routine = _http.GetMemoryGraph(c, p, null, Failed, (bool)call["includeEntities"], (bool)call["includeCharacterMemories"]); break;
                case "memories.search": routine = _http.SearchMemories(c, p, (string)call["query"], null, Failed, (int)call["limit"]); break;
                case "memories.deleteAll": routine = _http.DeleteAllMemories(c, p, (bool)call["confirm"], null, Failed); break;
                case "memories.create": routine = _http.CreateMemory(c, p, test["json"].ToObject<MemoryWriteRequest>(), null, Failed); break;
                case "memories.update": routine = _http.UpdateMemory(c, p, "mem_9", test["json"].ToObject<MemoryWriteRequest>(), null, Failed); break;
                case "memories.delete": routine = _http.DeleteMemory(c, p, "mem_9", null, Failed, true); break;
                case "conversations.list": routine = _http.ListConversations(c, null, Failed, (int)call["limit"], (int)call["offset"], (string)call["sortBy"], (string)call["sortOrder"]); break;
                case "conversations.stats": routine = _http.GetConversationStats(c, null, Failed); break;
                case "conversations.get": routine = _http.GetConversation(c, p, null, Failed); break;
                case "conversations.messages": routine = _http.GetMessages(c, p, null, Failed, (int)call["limit"], (int)call["page"]); break;
                case "conversations.delete": routine = _http.DeleteConversation(c, p, null, Failed); break;
                case "characters.get": routine = _http.GetCharacter(c, null, Failed); break;
                case "characters.list": routine = _http.ListCharacters(null, Failed, (int)call["limit"], (int)call["offset"]); break;
                case "characters.create": routine = _http.CreateCharacter((JObject)test["json"], null, Failed); break;
                case "characters.update": routine = _http.UpdateCharacter(c, (JObject)test["json"], null, Failed); break;
                case "characters.testApiEndpoint": routine = _http.TestApiEndpoint(c, (JObject)test["json"]["endpoint"], (JObject)test["json"]["arguments"], null, Failed); break;
                case "characters.transfer": routine = _http.TransferCharacter(c, (string)test["json"]["destination"], null, Failed, (string)test["json"]["orgId"]); break;
                case "characters.delete": routine = _http.DeleteAgent(c, null, Failed); break;
                case "characters.motive": routine = _http.GetCharacterMotive(c, null, Failed, p); break;
                case "characters.setMotive": routine = _http.SetCharacterMotive(c, p, (string)test["json"]["motive"], null, Failed); break;
                case "characters.resetMotive": routine = _http.ResetCharacterMotive(c, p, null, Failed); break;
                case "characters.avatar": routine = _http.UploadAvatar(c, new byte[] { 1 }, "image/png", null, Failed); break;
                case "characters.modelFile": routine = _http.UploadModel(c, new byte[] { 1 }, null, Failed); break;
                case "generation.fromImage": routine = _http.UploadImageToCharacter(new byte[] { 1 }, "image/jpeg", null, Failed); break;
                case "generation.startModel": routine = _http.GenerateModel(c, null, Failed); break;
                case "generation.rigged": routine = _http.GenerateModel(c, null, Failed, rigged: true); break;
                case "generation.modelStatus": routine = _http.GetModelStatus(c, null, Failed); break;
                case "shares.create": routine = _http.CreateShare(test["json"].ToObject<ShareCreateRequest>(), null, Failed); break;
                case "shares.open": routine = _http.OpenShare("share_1", null, Failed); break;
                case "shares.exchange": routine = _http.ExchangeShare("share_token", null, Failed, p); break;
                case "shares.list": routine = _http.ListShares(c, null, Failed); break;
                case "shares.revoke": routine = _http.RevokeShare("share_1", null, Failed); break;
                case "shares.revokeByKey": routine = _http.RevokeSharesByApiKey("key_1", null, Failed); break;
                case "turn.single": routine = _http.Turn(c, test["json"].ToObject<CharacterTurnRequest>(), null, Failed); break;
                case "turn.stream":
                    _response.Body = "{\"type\":\"turn_completed\",\"data\":{\"text\":\"Hi\"}}\n";
                    routine = _http.StreamTurn(c, test["json"].ToObject<CharacterTurnRequest>(), null, null, Failed); break;
                default: throw new AssertionException("Implement Unity conformance case " + test["id"]);
            }
            Run(routine);
            Assert.IsNull(_error);
            Assert.AreEqual(1, _calls);
            Assert.AreEqual((string)test["method"], _request.Method);
            var uri = new Uri(_request.Url);
            Assert.AreEqual((string)test["path"], uri.AbsolutePath);
            var query = new JObject();
            foreach (var part in uri.Query.TrimStart('?').Split('&'))
            {
                if (part.Length == 0) continue;
                var pieces = part.Split(new[] { '=' }, 2);
                query[Uri.UnescapeDataString(pieces[0])] = Uri.UnescapeDataString(pieces[1]);
            }
            Assert.IsTrue(JToken.DeepEquals(test["query"] ?? new JObject(), query), query.ToString());
            if (test["json"] != null) Assert.IsTrue(JToken.DeepEquals(test["json"], JToken.Parse(_request.Body.ToString(Newtonsoft.Json.Formatting.None))), _request.Body?.ToString());
            StringAssert.IsMatch((string)Fixture["headers"]["X-Estuary-Client"]["pattern"], _request.Headers["X-Estuary-Client"]);
        }

        [Test]
        public void VersionAndVendoredFixtureMatchSources()
        {
            Assert.AreEqual((string)JObject.Parse(File.ReadAllText(Path.Combine(PackageRoot, "package.json")))["version"], EstuarySdk.Version);
            var canonical = Path.Combine(PackageRoot, "../sdk-conformance/rest.json");
            if (File.Exists(canonical)) Assert.IsTrue(JToken.DeepEquals(Fixture, JObject.Parse(File.ReadAllText(canonical))));
        }

        [Test]
        public void FailedTokenProviderNeverFallsBackToApiKey()
        {
            _config.TokenProvider = () => Task.FromException<string>(new Exception("expired"));
            Run(_http.GetCharacter("id", null, Failed));
            Assert.AreEqual(0, _calls);
            StringAssert.Contains("token provider failed", _error);
        }

        [Test]
        public void AuthContextAndEscapedIdsAreSent()
        {
            _config.TokenProvider = () => Task.FromResult("firebase-token");
            _http.OrgId = "org"; _http.PlayerId = "player";
            Run(_http.GetCharacter("a/b ?#", null, Failed));
            StringAssert.EndsWith("a%2Fb%20%3F%23", _request.Url);
            Assert.AreEqual("Bearer firebase-token", _request.Headers["Authorization"]);
            Assert.IsFalse(_request.Headers.ContainsKey("X-API-Key"));
            Assert.AreEqual("org", _request.Headers["X-Org-Id"]);
            Assert.AreEqual("player", _request.Headers["X-Player-Id"]);
        }

        [Test]
        public void ShareRedemptionIsAnonymousEvenWithFailingTokenProvider()
        {
            _config.TokenProvider = () => throw new Exception("must not run");
            _http.PlayerId = "private-player"; _http.OrgId = "private-org";
            Run(_http.OpenShare("share", null, Failed));
            Assert.IsNull(_error);
            CollectionAssert.AreEquivalent(new[] { "X-Estuary-Client" }, _request.Headers.Keys);
        }

        [Test]
        public void ShareCreationUsesBearerApiKey()
        {
            Run(_http.CreateShare(new ShareCreateRequest { CharacterId = "id", Ttl = "7d" }, null, Failed));
            Assert.AreEqual("Bearer test-key", _request.Headers["Authorization"]);
            Assert.IsFalse(_request.Headers.ContainsKey("X-API-Key"));
        }

        [TestCase(201)] [TestCase(202)] [TestCase(204)]
        public void SuccessfulEmptyResponsesDoNotRequireJson(int status)
        {
            _response.Status = status; _response.Body = "";
            bool success = false;
            Run(_http.DeleteConversation("c", "p", () => success = true, Failed));
            Assert.IsTrue(success); Assert.IsNull(_error);
        }

        [Test]
        public void ScopeFailureIncludesDetailAndDoesNotRetry()
        {
            _response.Status = 403; _response.Body = "{\"detail\":{\"error\":\"insufficient_scope\",\"requiredScopes\":[\"memories:write\"]}}";
            Run(_http.DeleteMemory("c", "p", "m", null, Failed));
            Assert.AreEqual(1, _calls);
            StringAssert.Contains("403", _error); StringAssert.Contains("memories:write", _error);
        }

        [Test]
        public void LegacyGetAgentsRetainsUnpaginatedRoute()
        {
            _response.Body = "[]";
            Run(_http.GetAgents(null, Failed));
            StringAssert.EndsWith("/api/agents", _request.Url);
            Assert.IsNull(_error);
        }

        [Test]
        public void MultipartCarriesMimeOptionsAndIdempotencyKey()
        {
            Run(_http.UploadImageToCharacter(new byte[] { 1, 2 }, "image/png", null, Failed,
                new ImageGenerationOptions { UseDefaultVoice = false, TtsProvider = "inworld" }, idempotencyKey: "request-1"));
            Assert.AreEqual("request-1", _request.Headers["Idempotency-Key"]);
            var image = _request.Form.Single(f => f.sectionName == "image");
            Assert.AreEqual("image/png", image.contentType);
            StringAssert.EndsWith(".png", image.fileName);
            Assert.AreEqual("false", Encoding.UTF8.GetString(_request.Form.Single(f => f.sectionName == "useDefaultVoice").sectionData));
        }

        [Test]
        public void CharacterResponseMapsCanonicalAndLegacyModelFields()
        {
            _response.Body = "{\"id\":\"c\",\"model_url\":\"https://assets.invalid/rig.glb\",\"model_status\":\"completed\",\"model_rigged\":true,\"model_animations\":[\"preset:biped:wave\"],\"stt_provider\":\"soniox\",\"llm_temperature\":0.4}";
            AgentResponse character = null;
            Run(_http.GetCharacter("c", value => character = value, Failed));
            Assert.IsNull(_error);
            Assert.IsTrue(character.ModelRigged);
            Assert.AreEqual("https://assets.invalid/rig.glb", character.BestModelUrl);
            Assert.AreEqual("preset:biped:wave", character.ModelAnimations[0]);
            Assert.AreEqual("soniox", character.SttProvider);
            Assert.AreEqual(0.4, (double)character.AdditionalFields["llm_temperature"]);
            _response.Body = "[{\"id\":\"c\",\"modelUrl\":\"https://assets.invalid/old.glb\",\"modelRigged\":true,\"modelAnimations\":[\"idle\"]}]";
            Run(_http.GetAgents(list => character = list[0], Failed));
            Assert.IsTrue(character.ModelRigged);
            Assert.AreEqual("idle", character.ModelAnimations[0]);
            Assert.AreEqual("https://assets.invalid/old.glb", character.ModelUrl);
        }

        [Test]
        public void MemoryResponseRetainsRestAndPushFields()
        {
            _response.Body = "{\"memories\":[{\"id\":\"m\",\"agentId\":\"c\",\"playerId\":\"p\",\"content\":\"Enjoys tea\",\"importance\":0.75,\"memoryLayer\":\"ltm\",\"sourceMessageTimestamp\":\"2026-09-26T12:00:00Z\"}],\"total\":1}";
            MemoryList result = null;
            Run(_http.GetMemories("c", "p", value => result = value, Failed));
            Assert.IsNull(_error);
            Assert.AreEqual("Enjoys tea", result.Memories[0].Content);
            Assert.AreEqual(0.75f, result.Memories[0].Importance);
            Assert.AreEqual("ltm", result.Memories[0].MemoryLayer);
            Assert.AreEqual("2026-09-26T12:00:00Z", result.Memories[0].SourceMessageTimestamp);
        }

        [Test]
        public void SimulationMotiveUsesSharedHeadersAndEscaping()
        {
            var simulation = new EstuarySimulationApi(_config) { OrgId = "org" };
            simulation.Http.TransportForTest = _http.TransportForTest;
            Run(simulation.SetCharacterMotive("instance/1", "char/2", "Find a friend", null, Failed));
            StringAssert.EndsWith("/instances/instance%2F1/motives/char%2F2", _request.Url);
            Assert.AreEqual("PATCH", _request.Method);
            Assert.AreEqual("Find a friend", (string)_request.Body["motive"]);
            Assert.AreEqual("org", _request.Headers["X-Org-Id"]);
            Assert.AreEqual(EstuarySdk.ClientIdentification, _request.Headers["X-Estuary-Client"]);
        }
    }
}
